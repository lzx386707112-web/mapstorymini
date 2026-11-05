using UnityEngine;
using System.Collections.Generic;

// Source UseItemHandler and dedicated equipment / chair / cash handlers use distinct routes.
public static class NativeInventoryActions
{
    static int carriedItem,carriedPosition,carriedType;
    static bool swallowDrop,carriedFromInventory;
    public static bool Carrying => carriedItem != 0;
    public static void CancelCarry() => carriedItem = 0;
    public static void PickEquipped(int id, int slot)
    {
        if (Carrying || slot >= 0 || slot == EquipSlot.Weapon || Profile.EquipOfSlot(slot) != id) return;
        SkillWindow.CancelSkillDrag();
        carriedItem = id; carriedPosition = slot; carriedType = 1; carriedFromInventory = false;
    }
    public static void Pick(int id,int inventoryPosition=0) => Pick(id,inventoryPosition,false);
    public static void PickFromInventory(int id,int inventoryPosition=0) => Pick(id,inventoryPosition,true);
    static string PickOwnedItem(int id, int inventoryPosition)
    {
        var entry = GameSession.Character?.inventory.Find(x => x.itemId == id &&
            InventoryDao.IsBagPosition(x.position) &&
            (inventoryPosition == 0 || x.position == inventoryPosition));
        if (entry == null) return "背包里没有这个物品";
        // 双击使用卷轴仍是背包 DragStart，不能退成没有来源页/格的键位载荷。
        PickFromInventory(id, entry.position);
        return null;
    }
    static void Pick(int id,int inventoryPosition,bool fromInventory)
    {
        // Native keymap item bindings are for consumables, usable setup and cash items.
        // 背包格多一条：源端把「格子里的东西丢到地上」实现成 ITEM_MOVE(action=0)，
        // 任何背包页都能丢，所以从背包拿起时装备/材料也允许携带（它们只是不能绑键位）。
        // 但已经穿在身上的（装备页 position<0）不能拿：InventoryManipulator.drop 只认
        // position>0，源端同样要求先脱下，否则拿起后落到地面会被服务端拒绝、光标上的
        // 道具凭空消失。
        if (id / 1000000 != 2 && id / 10000 != 301 && id / 1000000 != 5 &&
            !(NativeVegaWindow.Visible && id/1000000==1) &&
            !(fromInventory && id > 0 && inventoryPosition >= 0)) return;
        SkillWindow.CancelSkillDrag();
        carriedItem = id;carriedPosition=inventoryPosition;carriedFromInventory=fromInventory;
        // 源端拖拽载荷带来源页签（BeiDou.exe 0x81D42E 把窗口 +0x5e4 的当前页签写进载荷 +0x18），
        // 放下时 0x4F3057 要求「目标窗口页签 == 载荷页签」才发 ITEM_MOVE，跨页落格直接拒绝。
        // 这里同口径：类型取道具 id 首位（1..5），等价于「页签 + 1」。
        carriedType = fromInventory ? id / 1000000 : 0;
        if (fromInventory && carriedType == 1 && NativeAndroidWindow.Visible)
            Debug.Log("[Android/Drag] pick item=" + id + " source=" + inventoryPosition);
    }
    public static void CarryStep()
    {
        if (Event.current != null && Event.current.type == EventType.Repaint) CarryPointerStep();
    }
    public static void CarryPointerStep()
    {
        if (swallowDrop)
        {
            if (Pointer.Released)
            {
                if (Carrying && carriedFromInventory && carriedType == 1 && NativeAndroidWindow.Visible)
                    Debug.Log("[Android/Drag] release swallowed item=" + carriedItem + " pointer=" + Pointer.Pos);
                // 源端双击是独立消息 0x203（背包窗 vtable idx22 = 0x81D8AD，命中格 → 使用/穿戴），
                // 第二击不进「放回原格」的丢弃路径；这里的 ConsumeClick 会把 DoubleClicked 一起清掉，
                // 背包双击使用/穿戴因此永远无反应。DoubleClicked 必须留给 InvenWindow 的双击处理。
                if (!Pointer.DoubleClicked) Pointer.ConsumeClick();
                swallowDrop = false;
            }
            return;
        }
        if (!Carrying) return;
        if (carriedFromInventory && carriedType == 1 && NativeAndroidWindow.Visible &&
            (Pointer.Pressed || Pointer.Released))
        {
            bool hit = NativeAndroidWindow.SlotAt(Pointer.Pos, out int androidSlot);
            Debug.Log("[Android/Drag] item=" + carriedItem + " source=" + carriedPosition +
                " pressed=" + Pointer.Pressed + " released=" + Pointer.Released +
                " dragEnded=" + Pointer.DragEnded + " pointer=" + Pointer.Pos +
                " hit=" + hit + " target=" + androidSlot + " equipVisible=" + EquipWindow.Visible +
                " npcBlocking=" + NpcSession.Blocking);
        }
        if (Pointer.RightPressed || Input.GetKeyDown(KeyCode.Escape) ||
            NpcSession.Blocking && !SymbolCatalog.IsSymbolSlot(carriedPosition))
        { carriedItem = 0; return; }
        if((Pointer.Pressed||Pointer.Released&&Pointer.DragEnded)&&NativeEnhancementWindow.Drop(Pointer.Pos,carriedItem,carriedPosition))
        {carriedItem=0;swallowDrop=Pointer.Pressed;Pointer.ConsumeClick();Pointer.Pressed=false;return;}
        // 装备窗 DragStart 的载荷保留实际穿着位（现金优先）；只有放到背包才请求卸下。
        if (!carriedFromInventory && carriedType == 1 && carriedPosition < 0)
        {
            if (Profile.EquipOfSlot(carriedPosition) != carriedItem) { carriedItem = 0; return; }
            if (Pointer.Pressed && EquipWindow.SlotAt(Pointer.Pos, out int cell) &&
                EquipSlot.WornSlotOf(cell) == carriedPosition)
            {
                carriedItem = 0; swallowDrop = true;
                Pointer.ConsumeClick(); Pointer.Pressed = false;
                return;
            }
            if ((Pointer.Pressed || Pointer.Released && Pointer.DragEnded) && InvenWindow.Visible &&
                InvenWindow.InventoryType == 1 && InvenWindow.SlotAt(Pointer.Pos, out int destination))
            {
                string error = EquipLoadout.UnequipToInventory(carriedPosition, destination);
                if (error == null) { carriedItem = 0; swallowDrop = Pointer.Pressed; }
                else BottomBar.Toast(error);
                Pointer.ConsumeClick(); Pointer.Pressed = false;
            }
            return;
        }
        // 松手丢弃：把物品拖出背包/窗口后直接抬手也丢到地上（源端只有点击-点击一种口径，
        // 这是按用户要求补的拖拽口径；落点必须仍是地面区，窗口/底栏/虚拟键上松手不丢）。
        if (!Pointer.Pressed)
        {
            if (Pointer.Released && EquipWindow.DropPetPotion(Pointer.Pos,carriedItem))
            {
                carriedItem=0; Pointer.ConsumeClick(); return;
            }
            // 拖到装备窗的格子上松手 = 直接穿上/替换（源端把「落到已装备格」当 action<0 的穿戴，
            // 0x7FEA63 按目标格分派）。必须先于背包格落点判定，否则会被空格移动吃掉。
            if (Pointer.Released && Pointer.DragEnded && carriedFromInventory && carriedType == 1 &&
                EquipWindow.Visible && EquipWindow.SlotAt(Pointer.Pos, out int equipCell))
            {
                if (FitsEquipCell(equipCell, EquipSlot.SlotOf(carriedItem)))
                {
                    int equipId = carriedItem, equipFrom = carriedPosition;
                    carriedItem = 0;
                    string equipError = EquipLoadout.Equip(equipId, Profile.Attributes, equipFrom, equipCell);
                    if (equipError != null) BottomBar.Toast(equipError);
                }
                else BottomBar.Toast("这件装备不能放在" + EquipSlot.NameOf(equipCell) + "这一格");
                Pointer.ConsumeClick(); return;
            }
            // 按住拖动到另一格再松手：同样走 ITEM_MOVE（项目补的拖拽口径，必须要求真的拖动过，
            // 否则「点一下拿起」的抬手会被当成落格，等于刚拿起来就放回）。
            if (Pointer.Released && Pointer.DragEnded && carriedFromInventory && InvenWindow.Visible &&
                InvenWindow.SlotAt(Pointer.Pos, out int releaseTarget) &&
                InvenWindow.InventoryType == carriedType && releaseTarget != carriedPosition)
            {
                int moveId = carriedItem, moveFrom = carriedPosition;
                carriedItem = 0;
                if (MoveToSlot(moveFrom, releaseTarget, carriedType)) { Pointer.ConsumeClick(); return; }
                carriedItem = moveId;
            }
            if (Pointer.Released && carriedFromInventory && FieldAcceptsDrop(Pointer.Pos))
            {
                int dropId = carriedItem, dropPosition = carriedPosition;
                carriedItem = 0;
                if (DropToField(dropId, dropPosition)) { Pointer.ConsumeClick(); Pointer.Pressed = false; return; }
                carriedItem = dropId;   // 没丢成（数量弹窗被拒等），保留携带
            }
            return;
        }
        int id = carriedItem;
        carriedItem = 0;
        bool placed=EquipWindow.DropPetPotion(Pointer.Pos,id);
        if(!placed) placed=NativeVegaWindow.Drop(Pointer.Pos,id,carriedPosition);
        if(!placed && (id/10000==204 || NativeScrollService.IsHeartScroll(id) || IsCashEquipment(id)))
        {
            var target=EquipWindow.EquipmentAt(Pointer.Pos) ?? InvenWindow.EquipmentAt(Pointer.Pos);
            // 心臟穿在 -54，而装备窗底图没有这一格（282 把它放进机器人换装面板，见
            // NativeAndroidWindow 头注释）。所以卷轴拖到那片面板的「心臟」格时，
            // 目标是当前穿着的心臟；拖到機器人/AP 格不算（卷轴只对心臟有效）。
            if(target==null && NativeAndroidWindow.Visible &&
               NativeAndroidWindow.SlotAt(Pointer.Pos,out int androidCell) && androidCell==NativeAndroidService.HeartSlot)
                target=GameSession.Character.inventory.Find(e=>e.inventoryType==1 && e.position==NativeAndroidService.HeartSlot);
            if(target!=null) { placed=true; if(IsCashEquipment(id)) NativeItemUseEffects.CashEquipment(id,target); else NativeItemUseEffects.Scroll(id,target); }
        }
        // 装备窗落格（点击口径）：先拿起、再点装备窗上的对应格子 → 直接穿上/替换。
        // 与拖拽口径同一条路，都走 EquipLoadout.Equip（服务端 InventoryManipulator.equip）。
        if(!placed && carriedFromInventory && carriedType==1 && EquipWindow.Visible &&
           EquipWindow.SlotAt(Pointer.Pos,out int equipCellPress))
        {
            if(FitsEquipCell(equipCellPress,EquipSlot.SlotOf(id)))
            {
                string equipError = EquipLoadout.Equip(id, Profile.Attributes, carriedPosition, equipCellPress);
                if(equipError!=null) BottomBar.Toast(equipError);
                placed = true;
            }
            else
            {
                BottomBar.Toast("这件装备不能放在" + EquipSlot.NameOf(equipCellPress) + "这一格");
                carriedItem = id;   // 格子不收，保留携带，别把道具吞掉
                return;
            }
        }
        // 背包格落点：源端放下时会先问命中窗口（0x4EFBB8 → 0x4F3057），
        // 目标格与手上道具同页且不是同一格 → 发 ITEM_MOVE(type, src, dst)，
        // 服务端 Inventory.move 决定「空格移动 / 同 id 合并 / 否则互换槽位」。
        // 必须排在卷轴/现金装备的落点之后：那是另一条判定分支，不能先被移动吃掉。
        if (!placed && carriedFromInventory && InvenWindow.Visible && InvenWindow.SlotAt(Pointer.Pos, out int targetPosition))
        {
            if (InvenWindow.InventoryType != carriedType)
            {
                // 跨背包页不落格（源端 0x4F30D8 比较页类型后直接返回）：保持携带，什么都不做。
                carriedItem = id;
                return;
            }
            if (targetPosition == carriedPosition)
            {
                // 点回原格 = 放回原处，源端同样不发请求（0x4F30E6 src==dst 直接返回）。
                swallowDrop = true; Pointer.ConsumeClick(); Pointer.Pressed = false;
                return;
            }
            if (MoveToSlot(carriedPosition, targetPosition, carriedType))
            {
                swallowDrop = true; Pointer.ConsumeClick(); Pointer.Pressed = false;
                return;
            }
            carriedItem = id;   // 服务端拒绝（格子越界/已空等），保留携带
            return;
        }
        if(!placed && id/1000000!=1) placed = KeyConfigWindow.Visible && !KeyConfigWindow.Carrying && KeyConfigWindow.DropSkill(Pointer.Pos, -id);
        if (!placed && id/1000000!=1 && !KeyConfigWindow.Visible)
            for (int i = 0; i < QuickSlots.Count; i++)
                if (BottomBar.SlotRect(i).Contains(Pointer.Pos)) { QuickBarKeys.BindSkill(i, -id); placed = true; break; }
        if (!placed && carriedFromInventory && FieldAcceptsDrop(Pointer.Pos)) placed = DropToField(id, carriedPosition);
        if (placed) { swallowDrop = true; Pointer.ConsumeClick(); Pointer.Pressed = false; }
        else carriedItem = id; // 无效落点不能静默清掉携带；允许移到正确格再次放下。
    }

    // 装备窗落格判定：目标格收不收这件装备。
    // 归一化到普通槽位（现金 -100）后要求一致；四个戒指格互相收（源端四个戒指位等价，
    // 具体落到哪个空槽由 InventoryManipulator.equip 决定）。
    static bool FitsEquipCell(int cellSlot, int itemSlot)
    {
        if (cellSlot == 0 || itemSlot == 0) return false;
        // 机器人换装槽：收什么品类由源端 BodyPart 表决定（帽只进 APHat、武器只进 APWeapon…）
        if (NativeAndroidService.IsApSlot(cellSlot)) return NativeAndroidService.ApAccepts(cellSlot, itemSlot);
        int cell = cellSlot <= -100 ? cellSlot + 100 : cellSlot;
        int item = itemSlot <= -100 ? itemSlot + 100 : itemSlot;
        if (cell == item) return true;
        return IsRingSlot(cell) && IsRingSlot(item);
    }
    static bool IsRingSlot(int slot) => slot == -12 || slot == -13 || slot == -15 || slot == -16;

    // 源端「放到另一个背包格」= ITEM_MOVE(0x47) 的 action>0 分支：
    //   ItemMoveHandler → InventoryManipulator.move(c, type, src, dst) → Inventory.move
    //   dst 空 → 改 position；同 id 且非 207 充能、owner 相同 → 相加（超 slotMax 留余量）；
    //   其余（含装备/现金同 id）→ 两个槽位互换。判定全部在服务端 NativeInventorySort.MoveItem。
    static bool MoveToSlot(int from, int to, int type)
    {
        if (!GameSession.InGame || GameSession.Character == null || from <= 0 || to <= 0 || from == to) return false;
        var response = GameSession.Net.ChangeInventory(new InventoryChangeRequest
        {
            characterId = GameSession.CharacterId,
            moveFrom = from, moveTo = to, moveInventoryType = type,
        });
        if (response == null || !response.ok)
        {
            BottomBar.Toast(response == null || string.IsNullOrEmpty(response.error) ? "无法移动这件物品" : response.error);
            return false;
        }
        GameSession.Character.inventory = response.inventory;
        GameSession.ApplyInventoryToProfile(); Profile.Save();
        return true;
    }

    // 源端「丢到地上」= ITEM_MOVE(0x47) 且 action==0（ItemMoveHandler → InventoryManipulator.drop）。
    // 交互沿用本项目的点击口径：点一次从背包拿起，再点一次落在窗口/底栏/虚拟按键之外的地面就丢。
    static bool FieldAcceptsDrop(Vector2 pointer)
        => !NativeUi.WindowAtPointer && !VirtualControls.BlockingPointer && pointer.y < UiKit.H - 80f * BottomBar.S;

    static bool DropToField(int itemId, int position)
    {
        var loop = GameLoop.I;
        var player = loop == null ? null : loop.player;
        var field = loop == null ? null : loop.field;
        if (player == null || field == null || field.StateId < 0 || GameSession.Character == null) return false;
        var entry = GameSession.Character.inventory.Find(x => x.itemId == itemId && InventoryDao.IsBagPosition(x.position) &&
            (position == 0 || x.position == position));
        if (entry == null) return false;
        // 源端丢弃门控（EXE 0x4f34f5..0x4f38a1，按 info 字段分派）：
        //   现金道具 info/cash       → 0x372「这种道具不能扔掉。」硬拦（0x5d4a2b 读 cash）
        //   任务道具 info/quest      → 0x15c3「这种道具丢弃后无法再次获得，还想丢弃吗？」确认（0x5d50b8 读 quest）
        //   不可交易 info/tradeBlock → 0x1232「丢弃后变无法交易，确认？」确认（0x5d4db9 读 tradeBlock）
        // 源端还有：封印道具 0x2F7「已封印…无法丢弃」、婚戒 0x10A3、卡片(430xxxx) 0x130B、
        //   4220xxx 硬拦——本项目暂无封印/婚戒/卡片系统，先不接。
        var info = NativeItemSource.Node(itemId)?.Get("info");
        if (info != null && info.Int("cash") == 1)
        { NativeUtilPrompt.Notice("这种道具不能扔掉。"); return true; }
        if (info != null && info.Int("quest") == 1)
        {
            NativeUtilPrompt.Confirm("这种道具丢弃后无法再次获得，还想丢弃吗？", yes => { if (yes) DropToFieldAsk(itemId, position); });
            return true;
        }
        if (info != null && info.Int("tradeBlock") == 1)
        {
            NativeUtilPrompt.Confirm("丢弃物品后获得的话，会变成无法交易状态。你确认要丢弃吗？", yes => { if (yes) DropToFieldAsk(itemId, position); });
            return true;
        }
        DropToFieldAsk(itemId, position);
        return true;
    }

    // 门控通过后：可堆叠道具（>1 且非装备）先问「扔掉多少？」（源端字符串池 885），装备/单件直接丢 1 个。
    static void DropToFieldAsk(int itemId, int position)
    {
        var entry = GameSession.Character == null ? null :
            GameSession.Character.inventory.Find(x => x.itemId == itemId && InventoryDao.IsBagPosition(x.position) &&
                (position == 0 || x.position == position));
        if (entry == null) return;
        if (entry.quantity > 1 && itemId / 1000000 != 1)
        {
            // 默认填全部数量（而非 1）：与背包金币丢弃默认 10 一致的体验。
            NativeUtilPrompt.Number("扔掉多少？", entry.quantity, 1, entry.quantity, value => DropToField(itemId, position, (int)value));
            return;
        }
        DropToField(itemId, position, 1);
    }

    static void DropToField(int itemId, int position, int quantity)
    {
        var loop = GameLoop.I;
        var player = loop == null ? null : loop.player;
        var field = loop == null ? null : loop.field;
        if (player == null || field == null || field.StateId < 0) return;
        // 源端 dropPos 取 Character.getPosition()（脚下），落点吸附在服务端 CalcItemDropPos 里做。
        var response = GameSession.Net.ChangeInventory(new InventoryChangeRequest
        {
            characterId = GameSession.CharacterId, itemId = itemId, sourcePosition = position,
            dropToField = true, dropQuantity = quantity, dropMapId = field.StateId,
            dropX = Mathf.RoundToInt(player.transform.position.x * Art.PPU),
            dropY = Mathf.RoundToInt(-player.transform.position.y * Art.PPU),
        });
        if (!response.ok) { BottomBar.Toast(string.IsNullOrEmpty(response.error) ? "无法丢弃这件物品" : response.error); return; }
        GameSession.Character.inventory = response.inventory;
        GameSession.ApplyInventoryToProfile(); Profile.Save();
    }
    static bool IsCashEquipment(int id)=>id==5060000 || id==5060001 || id>=5061000 && id<=5061003 || id==5060002 || id/10000==550 || id/10000==552 || id/10000==557;

    // ——————— 丢金币（背包窗 BtCoin 按钮，EXE 0x81D996）———————
    // 源端顺序：死亡状态拦截（字符串 66）→ 管理账号拦截（775）→ CUtilDlgEx type=2 数量弹窗
    //（GetString 5280「扔掉多少？」，0x9AC732(10, 10, min(meso,0xC350), 0, 10, 0)）→ 确认后发 MESO_DROP。
    // 服务端 MesoDropHandler：isAlive + meso>9 && meso<50001 && meso<=getMeso() → gainMeso(-meso)
    // → spawnMesoDrop(meso, position, player, player, true, (byte)2)。
    public static void PromptDropMeso()
    {
        if (!GameSession.InGame || GameSession.Character == null) return;
        var player = GameLoop.I != null ? GameLoop.I.player : null;
        if (player == null || player.hp <= 0) { BottomBar.Toast("死亡状态中不能执行该操作"); return; }
        int gm = GameSession.Character.record.gm;
        int dropLevel = GameConfigDao.GetInt(GameServer.Local == null ? null : GameServer.Local.Db, "minimum_gm_level_to_drop", 4);
        if (gm > 0 && gm < dropLevel) { BottomBar.Toast("管理账号不能扔掉金币。"); return; }
        long cap = System.Math.Min(Profile.gold, 50000);
        if (cap < 10) { BottomBar.Toast("金币不足，无法丢弃"); return; }
        NativeUtilPrompt.NumberLong("扔掉多少？", 10, 10, cap, DropMeso);
    }

    static void DropMeso(long meso)
    {
        var loop = GameLoop.I;
        var player = loop == null ? null : loop.player;
        var field = loop == null ? null : loop.field;
        if (player == null || field == null || field.StateId < 0) return;
        // 源端 MesoDropHandler 用 Character.getPosition()（脚下）作为落点。
        var response = GameSession.Net.ChangeInventory(new InventoryChangeRequest
        {
            characterId = GameSession.CharacterId,
            dropMeso = meso, dropMapId = field.StateId,
            dropX = Mathf.RoundToInt(player.transform.position.x * Art.PPU),
            dropY = Mathf.RoundToInt(-player.transform.position.y * Art.PPU),
        });
        if (response == null || !response.ok)
        { BottomBar.Toast(response == null || string.IsNullOrEmpty(response.error) ? "无法丢弃金币" : response.error); return; }
        // record.meso 是服务端权威；Profile.Data.gold 是展示值，两者同步扣减。
        GameSession.Character.record.meso = response.meso;
        Profile.Data.gold = checked(Profile.Data.gold - meso);
        Profile.Save();
    }

    public static void DrawCarry()
    {
        if (!Carrying) return;
        NativeCursor.Request(NativeCursor.StateHold);
        var icon = NativeItemSource.Icon(carriedItem);
        float size = 32 * BottomBar.S;
        if (icon != null) GUI.DrawTexture(new Rect(Pointer.Pos.x-size/2, Pointer.Pos.y-size/2, size, size), icon, ScaleMode.ScaleToFit);
    }
    public static void DoubleClick(int itemId, int inventoryPosition = 0)
    {
        carriedItem = 0;
        string error = TryUse(itemId, inventoryPosition);
        if (NativeAndroidService.IsRobot(itemId) || NativeAndroidService.IsHeart(itemId))
            Debug.Log("[Android/Equip] item=" + itemId + " source=" + inventoryPosition +
                " level=" + Profile.level + " result=" + (error ?? "ok") +
                " robot=" + Profile.EquipOfSlot(NativeAndroidService.RobotSlot) +
                " heart=" + Profile.EquipOfSlot(NativeAndroidService.HeartSlot));
        if (!string.IsNullOrEmpty(error)) BottomBar.Toast(error);
    }

    // StatEffect: item time is milliseconds; Alchemist scales duration by y.
    public static bool UseStatPotion(int id,PlayerCtrl player,int count=1)
    {
        var spec=NativeItemSource.Node(id)?.Get("spec");
        if (spec==null) return false;
        string[] fields={"pad","pdd","mad","mdd","acc","eva","speed","jump","expBuff","barrier"};
        string[] keys={"WATK","WDEF","MATK","MDEF","ACC","AVOID","SPEED","JUMP","EXP_BUFF","AURA"};
        var allowed=new HashSet<string>(fields) { "time","hp","mp","hpR","mpR","poison","seal","darkness","weakness","curse",
            "exp","inflation","morphRandom","consumeOnPickup","onlyPickup","prob","BFSkill","party","expinc","mhpR","mmpR","mhpRRate","mmpRRate","berserk","booster","repeatEffect","morph","thaw","padRate","madRate","pddRate","mddRate","accRate","evaRate","speedRate" };
        foreach (var child in spec.Children) if (!allowed.Contains(child.Name)) return false;
        var stats=new Dictionary<string,int>();
        if (id==2022001 || id==2022186) stats["MAP_PROTECTION"]=1;
        else if (id==2022040) stats["MAP_PROTECTION"]=2;
        for (int i=0;i<fields.Length;i++) { int value=i<8 ? (short)spec.Int(fields[i]) : spec.Int(fields[i]); if (value!=0) stats[keys[i]]=value; }
        if(id>=2022450 && id<=2022452 && spec.Int("expinc")!=0) stats["EXP_INCREASE"]=spec.Int("expinc");
        if(id>=2022585 && id<=2022588 || id>=2022616 && id<=2022617)
        {
            if(spec.Int("berserk")!=0) stats["BERSERK"]=spec.Int("berserk");
            if(spec.Int("booster")!=0) stats["BOOSTER"]=spec.Int("booster");
        }
        if(id>=2022359 && id<=2022421 || id==2022198 || id==2022337)
        {
            if(spec.Int("mhpR")!=0) stats["HPREC"]=(sbyte)spec.Int("mhpR");
            if(spec.Int("mmpR")!=0) stats["MPREC"]=(sbyte)spec.Int("mmpR");
        }
        if(spec.Int("morph")>0) stats["MORPH"]=spec.Int("morph");
        bool cure=spec.Int("poison")>0 || spec.Int("seal")>0 || spec.Int("darkness")>0 || spec.Int("weakness")>0 || spec.Int("curse")>0;
        if (stats.Count==0 && !cure && spec.Int("hp")==0 && spec.Int("mp")==0 && spec.Int("hpR")==0 && spec.Int("mpR")==0) return false;
        int duration=spec.Int("time",-1),hp=(short)spec.Int("hp"),mp=(short)spec.Int("mp");
        int job=Profile.JobId;
        int alchemist=job/10==41 ? 4110000 : job/10==141 ? 14110003 : 0;
        int level=alchemist==0 ? 0 : SkillProgress.LevelOf(alchemist);
        if (level>0)
        {
            var row=SkillBook.Get(alchemist)?.Level(level);
            duration=(int)(duration*(Art.Int(row,"y")/100.0));
            hp=(int)(hp*(Art.Int(row,"x")/100.0)); mp=(int)(mp*(Art.Int(row,"x")/100.0));
        }
        int hpRate=(int)(player.hpMax*(spec.Int("hpR")/100.0));
        if (player.GetComponent<NativeMobDebuffs>()?.Has(133)==true) { hp/=2; hpRate/=2; }
        if (count<=0 || !Profile.RemoveItem(id,count)) return false;
        player.hp=Mathf.Clamp(player.hp+(hp+(long)hpRate)*count,0,player.hpMax);
        player.mp=Mathf.Clamp(player.mp+(mp+(long)(player.mpMax*(spec.Int("mpR")/100.0)))*count,0,player.mpMax);
        var diseases=player.GetComponent<NativeMobDebuffs>();
        if(spec.Int("poison")>0) diseases?.Clear(125);
        if(spec.Int("seal")>0) diseases?.Clear(120);
        if(spec.Int("darkness")>0) diseases?.Clear(121);
        if(spec.Int("weakness")>0) { diseases?.Clear(122); diseases?.Clear(126); }
        if(spec.Int("curse")>0) diseases?.Clear(124);
        if(stats.Count>0 && duration>=0) NativeBuffs.For(player).ApplyItem(id,stats,duration,NativeBuffs.Now);
        GameSession.SaveToDb(); return true;
    }

    // null means handled; unsupported routes retain the item.
    public static string TryUse(int itemId, int inventoryPosition = 0)
    {
        if (!GameSession.InGame || NpcSession.Blocking) return "当前无法使用物品";
        var player = GameLoop.I?.player;
        if (player == null || player.hp <= 0) return "当前无法使用物品";
        if(CubeRules.GetFamily(itemId)!=null||itemId==2049400||itemId==2049401
            ||FlameCatalog.IsFlameItem(itemId)||itemId/1000==2590||itemId/1000==2591)
        {
            var cost=GameSession.Character.inventory.Find(x=>x.itemId==itemId&&x.position>0&&(inventoryPosition==0||x.position==inventoryPosition));
            if(cost==null)return "背包里没有这个物品";
            if(NativeEnhancementWindow.Visible)return NativeEnhancementWindow.Select(cost);
            NativeEnhancementWindow.Open(FlameCatalog.IsFlameItem(itemId)?4:itemId/1000==2590||itemId/1000==2591?5:
                CubeRules.GetFamily(itemId)?.bonus==true?2:1,cost);return null;
        }
        if(NativeEnhancementWindow.Visible&&itemId/1000000==1)
        {
            var target=GameSession.Character.inventory.Find(x=>x.itemId==itemId&&(inventoryPosition==0||x.position==inventoryPosition));
            return NativeEnhancementWindow.Select(target);
        }
        if(NativeVegaWindow.Visible && (itemId/1000000==1 || itemId/10000==204))
        {
            var item=GameSession.Character.inventory.Find(x=>x.itemId==itemId && InventoryDao.IsBagPosition(x.position) && (inventoryPosition==0 || x.position==inventoryPosition));
            return NativeVegaWindow.Select(item);
        }
        if (itemId / 1000000 == 1) return EquipLoadout.Equip(itemId, Profile.Attributes, inventoryPosition);
        if (Profile.ItemCount(itemId) <= 0) return "背包里没有这个物品";
        if(itemId/10000==212 || itemId/10000==524)
        {
            var response=GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=GameSession.CharacterId,itemId=itemId,sourcePosition=inventoryPosition});
            if(!response.ok) return response.error;
            GameSession.Character.inventory=response.inventory;GameSession.ApplyInventoryToProfile();Profile.Save();return null;
        }
        if (itemId / 1000000 == 2)
        {
            if(itemId==2430680)
            {
                var result=GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=GameSession.CharacterId,
                    itemId=itemId,sourcePosition=inventoryPosition,worldQuestDraw=true});
                if(!result.ok) return result.error;
                GameSession.Character.inventory=result.inventory;GameSession.ApplyInventoryToProfile();Profile.Save();
                if(!string.IsNullOrEmpty(result.message)) NativeSystemMessage.Drop(5,result.message);
                return null;
            }
            if(itemId/10000==237)
            {
                var node=NativeItemSource.Node(itemId);
                var result=GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=GameSession.CharacterId,itemId=itemId,
                    bankExp=node?.Int("spec/exp") ?? 0,maxBankLevel=node?.Int("info/maxLevel",256) ?? 256});
                if(!result.ok) return result.error;
                GameSession.Character.inventory=result.inventory;
                GameSession.Character.record.gachaexp=result.bankedExp;
                GameSession.ApplyInventoryToProfile(); Profile.Save(); return null;
            }
            if(itemId==2003609)
            {
                int exp=NativeItemSource.Node(itemId)?.Int("spec/exp") ?? 0;
                if(exp<=0) return null;
                if(Profile.RemoveItem(itemId,1,inventoryPosition)) Profile.GrantExperience(exp);
                return null;
            }
            if(itemId/10000==204 || NativeScrollService.IsHeartScroll(itemId)) return PickOwnedItem(itemId, inventoryPosition);
            // 282 核心宝石（2438xxx/2439xxx…）：252 版是 UseItemHandler 里独立分支，
            // 打开 V 矩阵的宝石使用窗，扣道具与加 V 点都由服务端 VMatrix(op 1) 完成。
            if(VMatrixCatalog.Gemstone(itemId) != null) { NativeVMatrixWindow.OpenGemstone(itemId); return null; }
            if(itemId/10000==227) return NativeCatchItems.Use(itemId,inventoryPosition);
            if(itemId/10000==210) return NativeItemUseEffects.SummonBag(itemId,inventoryPosition);
            if(itemId/10000==228 || itemId/10000==229) return NativeItemUseEffects.SkillBook(itemId,inventoryPosition);
            if(itemId/10000==238)
            {
                if(!NativeMonsterBook.Collect(itemId,1)) return "无法收录这张怪物卡片";
                return Profile.RemoveItem(itemId,1,inventoryPosition) ? null : "背包里没有这个物品";
            }
            if(itemId>=2030000 && itemId<=2030100) return NativeItemUseEffects.TownScroll(itemId,inventoryPosition);
            if(itemId/10000==226)
            {
                int fatigue=NativeItemSource.Node(itemId)?.Int("spec/incFatigue") ?? 0;
                if(!Profile.RemoveItem(itemId,1,inventoryPosition)) return "背包里没有这个物品";
                var record=GameSession.Character.record;
                record.mounttiredness=System.Math.Max(0,record.mounttiredness+fatigue);
                GameSession.SaveToDb();return null;
            }
            // UseItemHandler.handleCureItems consumes even without a matching disease.
            if (itemId >= 2050001 && itemId <= 2050004)
            {
                if (!Profile.RemoveItem(itemId, 1)) return "背包里没有这个物品";
                var diseases = player.GetComponent<NativeMobDebuffs>();
                if (itemId == 2050001) diseases?.Clear(121);
                else if (itemId == 2050002) { diseases?.Clear(122); diseases?.Clear(126); }
                else if (itemId == 2050003) { diseases?.Clear(120); diseases?.Clear(124); }
                else foreach (int disease in new[] { 120, 121, 122, 124, 125, 126 }) diseases?.Clear(disease);
                GameSession.SaveToDb();
                return null;
            }
            var spec = NativeItemSource.Node(itemId)?.Get("spec");
            if(spec?.Int("isHasItem")==1)
            {
                var result=GameSession.Net.ChangeInventory(new InventoryChangeRequest {
                    characterId=GameSession.CharacterId,itemId=itemId,grantItemId=spec.Int("hasItemId"),grantQuantity=spec.Int("hasItemQuantity"),
                    grantMinutes=spec.Int("hasItemTime",-1),consumeGrantSource=spec.Int("remove")==1 });
                if(!result.ok) return result.error;
                GameSession.Character.inventory=result.inventory; GameSession.ApplyInventoryToProfile(); Profile.Save();
                return null;
            }
            if (spec?.Int("isNpc") == 1)
            {
                if (spec.Int("remove") == 1 && !Profile.RemoveItem(itemId, 1)) return "背包里没有这个物品";
                return NpcSession.Talk(spec.Int("npc"), spec.Text("script")) != null ? null : "物品NPC脚本尚未接入";
            }
            if (!string.IsNullOrEmpty(spec?.Text("script")))
            {
                if(string.IsNullOrEmpty(ScriptSource.Load("item",spec.Text("script")))) return "源端缺失此物品的使用脚本，无法使用";
                return NpcSession.UseItem(itemId) != null ? null : "物品脚本无法启动";
            }
            if(NativeItemSource.Node(itemId)?.Get("reward")!=null) return NativeItemUseEffects.RewardBox(itemId);
            // ItemInformationProvider.getItemEffect prefers specEx over spec.
            // These original items deliberately apply a disease, not their empty spec.
            var extended=NativeItemSource.Node(itemId)?.Get("specEx");
            if(extended!=null)
            {
                var disease=extended.Get("0");
                int skill=disease?.Int("mobSkill") ?? 0,level=disease?.Int("level") ?? 0;
                var effect=skill>0 && level>0 ? WzAssets.Node("Skill/MobSkill.img/"+skill+"/level/"+level) : null;
                if(effect==null) return "源端物品效果数据缺失";
                if(!Profile.RemoveItem(itemId,1,inventoryPosition)) return "背包里没有这个物品";
                // StatEffect target>0 excludes the user. This local session has no
                // other human characters; a target-others item must not debuff self.
                if(disease.Int("target")==0)
                    NativeMobDebuffs.For(player).Apply(skill,effect.Int("x"),effect.Int("y"),effect.Int("time")*1000L);
                GameSession.SaveToDb();return null;
            }
            if (NpcSession.UseRestorative(itemId)) return null;
            if (UseStatPotion(itemId,player)) return null;
            return "该消耗品的特殊使用功能尚未接入";
        }
        if (itemId / 10000 == 301) return player.UseChair(itemId) ? null : "请站在地面上使用椅子";
        if (itemId / 1000000 == 5)
        {
            // 4EFD25 -> 48645B / 4863D5 / 486845 -> A1DC5B:
            // these tickets have no ordinary double-click request. Their real
            // use remains in the original NPC / quest scripts.
            int family=itemId/10000;
            if(family==515 || family==522 || family==525 || family==538 || family==542 || family==546 || family==549) return null;
            if(itemId==5610000 || itemId==5610001) {NativeVegaWindow.Open(itemId);return null;}
            if(itemId==5050000) {NativeResetWindow.Open(itemId,inventoryPosition);return null;}
            // 505 SP 洗点：cashType24 → 0xA0C6DD；5050001..4 = 1..4 转技能点重置卷
            if(itemId>=5050001 && itemId<=5050004) {NativeSpResetWindow.Open(itemId,inventoryPosition);return null;}
            // 雇佣商人许可（503xxxx）：自由市场里开店 / 打开自家店。
            if(itemId/10000==503) {NativeMerchantWindow.Open(itemId);return null;}
            // 营业执照（514xxxx）：个人商店，CREATE createType 4/5 由许可 id 分派（ItemConstants.isPlayerShop）。
            if(itemId/10000==514) {NativePlayerShopWindow.Open(itemId);return null;}
            // 快速投递券（itemType 533）：只开寄件窗口，券在真正寄出时才扣（DueyProcessor.dueySendItem）。
            if(itemId/10000==533) {NativeDeliveryWindow.Open(true);return null;}
            // 传送石：cashType22 → 0xA0CAB0；5040000..4 普通 / 5041000 VIP
            if(itemId/10000==504) {NativeTeleportWindow.Open(itemId,inventoryPosition);return null;}
            // 改名（领头宠物）：0xA0BA15 → 7A06F3 过滤链；服务端 getPet(0) 无宠物不消耗
            if(itemId/10000==517) {NativeRenameWindow.Open(itemId,inventoryPosition);return null;}
            // 黑板：cashType32 → 0xA0CE2E（CUtilDlgEx type3 max40）
            if(itemId/10000==537) {NativeAdBoard.Open(itemId);return null;}
            // 喇叭类（itemType 507，UseCashItemHandler 294..375）：广播 = 聊天框白字
            //（单机唯一真人，与假人喇叭 BotField 同口径；serverNotice(2)/(3) 样式差异不可见）。
            // whisper/耳朵、道具展示（case 6）、三行输入（case 7）在无观众会话里无可见差异，简化为一行。
            // 源端 medal 前缀（勋章名）单机恒为无勋章空串。
            if(itemId/10000==507)
            {
                int megaKind=itemId/1000%10;    // 源端 (itemId/1000)%10：1 普通 / 2 超级 / 5 TV / 6 道具 / 7 三连
                if(itemId>=5075000 && itemId<=5075005)
                {
                    // MapleTV 券（case 5，tvType=itemId%10）：正在播放时源端 dropMessage 拒绝且不消耗。
                    // 源端固定发 5 条消息（TV 轮播，第 5 条限 15 字）；单机一次输入一条。
                    NativeUtilPrompt.Text("输入要在 MapleTV 上播放的消息。", "", 0, 60, text => {
                        if(string.IsNullOrEmpty(text)) return;
                        var response=GameSession.Net.Tv(new TvRequest { characterId=GameSession.CharacterId, itemId=itemId, message=text });
                        if(response==null || !response.ok) { BottomBar.Toast(response?.error ?? "MapleTV 无法开始"); return; }
                        if(!Profile.RemoveItem(itemId,1,inventoryPosition)) { BottomBar.Toast("背包里没有这个物品"); return; }
                        GameSession.SaveToDb();
                        NativeMapleTvDisplay.Start(response);
                        // megassenger（tvType>=3）的 ear serverNotice(3) 广播：单机等价聊天框白字
                        if(response.tvType>=3) BottomBar.AddChat(GameSession.Character.record.name + " : " + text, BottomBar.ChatWhite);
                    });
                    return null;
                }
                if(megaKind==5) return "MapleTV 效果尚未迁移";
                if(megaKind!=1 && megaKind!=2 && megaKind!=6 && megaKind!=7) return "源端未启用此喇叭";
                if(megaKind==1 && GameSession.Character.record.level<=9) return "10 级以上才能使用喇叭";
                NativeUtilPrompt.Text(megaKind==2 ? "输入要全服广播的消息（超级喇叭）。" : "输入要广播的消息。", "", 0, 70, text => {
                    if(string.IsNullOrEmpty(text)) return;
                    if(!Profile.RemoveItem(itemId,1,inventoryPosition)) { BottomBar.Toast("背包里没有这个物品"); return; }
                    GameSession.SaveToDb();
                    BottomBar.AddChat(GameSession.Character.record.name + " : " + text, BottomBar.ChatWhite);
                });
                return null;
            }
            // 猫头鹰搜索器（itemType 523）：开搜索窗，搜到结果才扣券（UseCashItemHandler 428..444）。
            if(itemId/10000==523) {NativeItemSearchWindow.Open(itemId,inventoryPosition);return null;}
            // 远程扭蛋操作券（5451000）：选村后跑 gachaponRemote.js（RemoteGachaponHandler）。
            if(itemId==5451000) {NativeRemoteGachaponWindow.Open();return null;}
            if(itemId==5510000) return "死亡后可在复活确认窗口使用原地复活术";
            if(itemId/1000==5000)
            {
                var response=GameSession.Net.ChangeInventory(new InventoryChangeRequest {characterId=GameSession.CharacterId,itemId=itemId,sourcePosition=inventoryPosition,togglePet=true});
                if(!response.ok) return response.error;
                GameSession.Character.inventory=response.inventory;GameSession.ApplyInventoryToProfile();Profile.Save();return null;
            }
            if(itemId/10000==516)
            {
                player.Express(itemId-5159992);return null;
            }
            if(itemId/10000==510)
            {
                if(!Profile.RemoveItem(itemId,1,inventoryPosition)) return "背包里没有这个物品";
                AudioManager.PlayBgm("Jukebox/Congratulation");return null;
            }
            if(itemId==5180000) return NpcSession.Talk(1032102,"waterOfLife")!=null ? null : "生命之水脚本无法启动";
            // BeiDou cashType28 dispatches to A0EAAC without sending a use
            // request. Pet skill scrolls are retained, exactly as in that client.
            if(itemId/10000==519) return null;
            if(itemId/10000==529 || itemId/10000==528 && itemId!=5281000)
                return "源端未启用此道具";
            if(itemId/1000==5211 || itemId/1000==5360)
                return "此倍率券在背包中按指定时段自动生效";
            if(IsCashEquipment(itemId)) return PickOwnedItem(itemId, inventoryPosition);
            if(itemId/10000==520)
            {
                int meso=NativeItemSource.Node(itemId)?.Int("info/meso",-1) ?? -1;
                if(!Profile.RemoveItem(itemId,1,inventoryPosition)) return "背包里没有这个物品";
                Profile.AddGold(meso); return null;
            }
            if(itemId/10000==545)
            {
                if(NativeNpcShopWindow.Visible) return null;
                NativeNpcShopWindow.OpenShop(1338,false);
                if(NativeNpcShopWindow.Visible) Profile.RemoveItem(itemId,1,inventoryPosition);
                return null;
            }
            if(itemId/10000==530) return UseStatPotion(itemId,player) ? null : "无法应用此变身道具";
            return "该现金道具的使用功能尚未接入";
        }
        return "这个物品不能直接使用";
    }
}
