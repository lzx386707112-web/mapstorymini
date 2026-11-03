using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Run in an isolated batch editor; uses a temporary DB, never a player save.
public static class AndroidInventoryDragChecks
{
    const BindingFlags Private = BindingFlags.Static | BindingFlags.NonPublic;
    static void Set(Type t, string name, object value) => t.GetField(name, Private).SetValue(null, value);
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        string path = Path.Combine(Path.GetTempPath(), "android-drag-" + Guid.NewGuid().ToString("N") + ".db");
        int exit = 0;
        try
        {
            var db = DbStore.Create(path);
            var login = LoginService.Login(db, "drag_check", "pw123456");
            var made = CharFactory.CreateCharacter(db, login.accountId, 0, "DragCheck", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            var server = new GameServer();
            typeof(GameServer).GetProperty("Db").SetValue(server, db);
            typeof(GameSession).GetProperty("Net").SetValue(null, new InProcessTransport(server));
            typeof(GameSession).GetProperty("CharacterId").SetValue(null, made.characterId);
            typeof(GameSession).GetProperty("InGame").SetValue(null, true);
            Set(typeof(Profile), "data", new PlayerProfileData());
            Set(typeof(Profile), "debugOverride", true);
            EquipWindow.Visible = NativeAndroidWindow.Visible = true;
            UiKit.RefreshMetrics();
            var rectOf = typeof(NativeAndroidWindow).GetMethod("RectOf", Private);
            var cellRect = typeof(NativeAndroidWindow).GetMethod("CellRect", Private);
            var slots = (int[])typeof(NativeAndroidWindow).GetField("Slots", Private).GetValue(null);
            foreach (var pair in new[] { new[] { 1672000, 1672042, -54 }, new[] { 1662000, 1662088, -32 } })
            {
                long first = InventoryDao.InsertItem(db, made.characterId, pair[0], 1, pair[2], 1);
                InventoryDao.InsertEquipment(db, first);
                long second = InventoryDao.InsertItem(db, made.characterId, pair[1], 1, 13, 1);
                InventoryDao.InsertEquipment(db, second);
                typeof(GameSession).GetProperty("Character").SetValue(null, CharService.Load(db, made.characterId));
                GameSession.ApplyInventoryToProfile();
                Profile.Data.RemoveItem(pair[1], Profile.ItemCount(pair[1]));
                Require(Profile.ItemCount(pair[1]) == 0, "stale count fixture failed");
                Require(EquipLoadout.Equip(pair[1], new CharacterAttributes { level = 200 }, 13) == null,
                    "first robot/heart replacement rejected actual inventory " + pair[1]);
                Require(Profile.EquipOfSlot(pair[2]) == pair[1] &&
                    GameSession.Character.inventory.Exists(x => x.itemRowId == first && x.position == 13),
                    "replacement failed to swap instances " + pair[1]);
                Require(EquipLoadout.Equip(pair[0], new CharacterAttributes { level = 200 }, 12) != null,
                    "wrong source position accepted");
                Require(EquipLoadout.Equip(pair[0], new CharacterAttributes { level = 200 }, 13) == null,
                    "subsequent replacement failed");
                Debug.Log("[AndroidInventoryDragChecks] PASS first replacement with stale mirror " + pair[1]);
                db.Table("inventoryitems").Delete(x => DbStore.Long(x, "inventoryitemid") == first || DbStore.Long(x, "inventoryitemid") == second);
                db.Table("inventoryequipment").Delete(x => DbStore.Long(x, "inventoryitemid") == first || DbStore.Long(x, "inventoryitemid") == second);
            }
            foreach (int slot in NativeAndroidService.ApSlots)
            {
                int item = 0;
                foreach (int id in Server.Bot.BotItemCatalog.Shared.ItemIds)
                    if (NativeInventoryService.EquipmentSlotOf(id) <= -100 &&
                        NativeAndroidService.ApAccepts(slot, NativeInventoryService.EquipmentSlotOf(id))) { item = id; break; }
                Require(item != 0, "no cash fixture for " + slot);
                int bag = 1;
                while (CharService.Load(db, made.characterId).inventory.Exists(x => x.inventoryType == 1 && x.position == bag)) bag++;
                long row = InventoryDao.InsertItem(db, made.characterId, item, 1, bag, 1);
                InventoryDao.InsertEquipment(db, row);
                typeof(GameSession).GetProperty("Character").SetValue(null, CharService.Load(db, made.characterId));
                GameSession.ApplyInventoryToProfile();
                NativeInventoryActions.CancelCarry(); Set(typeof(NativeInventoryActions), "swallowDrop", false);
                NativeInventoryActions.PickFromInventory(item, bag);
                Require(NativeInventoryActions.Carrying, "pickup failed " + slot);
                Rect cell = (Rect)cellRect.Invoke(null, new object[] { rectOf.Invoke(null, null), Array.IndexOf(slots, slot) });
                // 真机反馈落在名称/行间留白，旧版只有黑框能命中。
                Pointer.Pos = new Vector2(cell.center.x, cell.y + 44 * BottomBar.S);
                Require(NativeAndroidWindow.SlotAt(Pointer.Pos, out int target) && target == slot,
                    "slot label/gap misses or maps to neighbour " + slot);
                Pointer.Pressed = false; Pointer.Released = Pointer.DragEnded = true;
                NativeInventoryActions.CarryPointerStep();
                Require(Profile.EquipOfSlot(slot) == item, "drag failed slot=" + slot + " item=" + item);
                Require(!NativeInventoryActions.Carrying, "drag retained cursor " + slot);
                Debug.Log("[AndroidInventoryDragChecks] PASS cash bag drag " + item + " -> " + slot);
                EquipLoadout.Unequip(slot);
                var returned = GameSession.Character.inventory.Find(x => x.itemRowId == row);
                Require(returned != null && InventoryDao.IsBagPosition(returned.position), "unequip lost instance " + slot);
                NativeInventoryActions.PickFromInventory(item, returned.position);
                Pointer.Pos = new Vector2(UiKit.W - 1, 1);
                Pointer.Pressed = true; Pointer.Released = Pointer.DragEnded = false;
                NativeInventoryActions.CarryPointerStep();
                Require(NativeInventoryActions.Carrying, "invalid placement discarded carried equipment " + slot);
                Pointer.Pos = new Vector2(cell.center.x, cell.yMax + 6 * BottomBar.S);
                Pointer.Pressed = true; Pointer.Released = Pointer.DragEnded = false;
                NativeInventoryActions.CarryPointerStep();
                Require(Profile.EquipOfSlot(slot) == item && !NativeInventoryActions.Carrying,
                    "click-place failed " + slot);
                Pointer.Pressed = false; Pointer.Released = true;
                NativeInventoryActions.CarryPointerStep();
                Require(!Pointer.Released, "place release leaked " + slot);
                Pointer.Released = false;
                Debug.Log("[AndroidInventoryDragChecks] PASS unequip and click-place " + item + " -> " + slot);
            }
        }
        catch (Exception e) { Debug.LogException(e); exit = 1; }
        finally { if (File.Exists(path)) File.Delete(path); EditorApplication.Exit(exit); }
    }
}
