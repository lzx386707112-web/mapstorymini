using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Isolated editor regression; temporary DB and profile, never a player save.
public static class InventoryCarryChecks
{
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;
    static void Set(Type t, string n, object v) => t.GetField(n, Flags).SetValue(null, v);
    static void Require(bool ok, string text) { if (!ok) throw new Exception(text); }
    static void Place(int position, bool drag = false)
    {
        Pointer.Pos = new Vector2((30 + 24 + (position - 1) % 4 * 36) * BottomBar.S,
            (90 + 66 + (position - 1) / 4 * 34) * BottomBar.S);
        Pointer.Pressed = !drag; Pointer.Released = Pointer.DragEnded = drag;
        NativeInventoryActions.CarryPointerStep();
        Pointer.Pressed = false; Pointer.Released = true;
        NativeInventoryActions.CarryPointerStep();
        Pointer.Pressed = Pointer.Released = Pointer.DragEnded = false;
    }
    public static void Run()
    {
        string path = Path.Combine(Path.GetTempPath(), "carry-" + Guid.NewGuid().ToString("N") + ".db");
        int exit = 0;
        try
        {
            var db = DbStore.Create(path);
            var login = LoginService.Login(db, "carrycheck", "pw123456");
            var made = CharFactory.CreateCharacter(db, login.accountId, 0, "CarryCheck", CharFactory.JobGroupAdventurer,
                20000, 30300, 0, 0, 1040002, 1060002, 1072001, 1302000, 0);
            var server = new GameServer();
            typeof(GameServer).GetProperty("Db").SetValue(server, db);
            typeof(GameSession).GetProperty("Net").SetValue(null, new InProcessTransport(server));
            typeof(GameSession).GetProperty("CharacterId").SetValue(null, made.characterId);
            typeof(GameSession).GetProperty("InGame").SetValue(null, true);
            Set(typeof(Profile), "data", new PlayerProfileData()); Set(typeof(Profile), "debugOverride", true);
            UiKit.RefreshMetrics(); InvenWindow.Visible = true;
            Set(typeof(InvenWindow), "pos", new Vector2(30, 90));
            Set(typeof(InvenWindow), "scroll", 0); Set(typeof(InvenWindow), "expanded", false);
            var pick = typeof(NativeInventoryActions).GetMethod("PickOwnedItem", Flags);
            int audited = 0;
            foreach (int id in Server.Bot.BotItemCatalog.Shared.ItemIds)
            {
                bool scroll = id / 10000 == 204 || NativeScrollService.IsHeartScroll(id);
                if (!scroll && FlameCatalog.CreateTypeOf(id) < 0 && id / 1000 != 2590 && id / 1000 != 2591 &&
                    !SymbolCatalog.IsSymbol(id) && CubeRules.GetFamily(id) == null && VMatrixCatalog.Gemstone(id) == null) continue;
                int type = id / 1000000; Set(typeof(InvenWindow), "tab", type - 1);
                typeof(GameSession).GetProperty("Character").SetValue(null, CharService.Load(db, made.characterId));
                GameSession.Character.inventory.Clear();
                GameSession.Character.inventory.Add(new InventoryEntry { itemId = id, inventoryType = type, position = 7, quantity = 1 });
                Set(typeof(NativeInventoryActions), "swallowDrop", false);
                if (scroll) Require(pick.Invoke(null, new object[] { id, 7 }) == null, "owned pickup " + id);
                else NativeInventoryActions.PickFromInventory(id, 7);
                Require(NativeInventoryActions.Carrying, "pickup " + id);
                Place(7); Require(!NativeInventoryActions.Carrying, "return to source " + id);
                if (scroll)
                {
                    Require(pick.Invoke(null, new object[] { id, 0 }) == null, "resolve source " + id);
                    Place(7); Require(!NativeInventoryActions.Carrying, "return resolved source " + id);
                    Require(pick.Invoke(null, new object[] { id, 8 }) != null, "reject wrong source " + id);
                }
                audited++;
            }
            foreach (int id in new[] { 2048716, 2048753, 2590000, 2591000, 1712000, 5062000, 2435719, 2616000 })
            {
                int type = id / 1000000; Set(typeof(InvenWindow), "tab", type - 1);
                db.Table("inventoryitems").Delete(x => DbStore.Int(x, "characterid") == made.characterId);
                long row = InventoryDao.InsertItem(db, made.characterId, id, type, 7, 1);
                InventoryDao.InsertItem(db, made.characterId, type == 1 ? 1302000 : type == 5 ? 5062001 : 2000000, type, 8, 1);
                typeof(GameSession).GetProperty("Character").SetValue(null, CharService.Load(db, made.characterId));
                Set(typeof(NativeInventoryActions), "swallowDrop", false);
                pick.Invoke(null, new object[] { id, 7 }); Place(6);
                Require(!NativeInventoryActions.Carrying && GameSession.Character.inventory.Exists(x => x.itemRowId == row && x.position == 6), "empty move " + id);
                pick.Invoke(null, new object[] { id, 6 }); Place(8, true);
                Require(!NativeInventoryActions.Carrying && GameSession.Character.inventory.Exists(x => x.itemRowId == row && x.position == 8), "drag swap " + id);
            }
            Debug.Log("[InventoryCarryChecks] PASS audited=" + audited + " move/swap=16");
        }
        catch (Exception e) { Debug.LogException(e); exit = 1; }
        finally { if (File.Exists(path)) File.Delete(path); EditorApplication.Exit(exit); }
    }
}
