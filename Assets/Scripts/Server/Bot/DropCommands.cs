// 假人掉落/投掷/拾取命令面。对齐源端 soloMapling.ArtificialPlayer.BotCommandsPack.DropCommands：
//   botDrop*（掉在脚下）/ botThrow*（扔到指定点）/ botThrowToOwner*（只给归属者）
//   botThrowEquipsInARow（一字排开展示）/ botLoot*（捡起并从地图移除）/ lootItemListOnFloor / botLootSelectedItems
//
// 重要语义（照搬源端，不自行补全）：
//   - botLoot* 走的是 MapleMap.pickItemDrop：只广播移除 + 标记已拾取，**不入背包**。
//     源端假人「捡东西」就是让地面物品消失（Gacha 回收、Dice 赌注清理、Blackjack 牌面回收都用它）。
//     真人拾取入库是 Character.pickupItem（见 MapItemPickup），两者不能混。
//   - botDrop* 的 playerDrop=true、ffaDrop=true（dropType 2：谁都能捡）；
//     botThrow* 的 playerDrop=false；botThrowToOwner* / *OwnerOnly 走 permanentOwner=true 的长期归属。
//   - lootItemListOnFloor 源端每件之间 Thread.sleep(100)。主线程不能阻塞，
//     改成 BotTasks 可取消时序，保留「按列表顺序、每件间隔 100ms」的先后与取消语义。
using System;
using System.Collections.Generic;
using Server.Field;

namespace Server.Bot
{
    public static class DropCommands
    {
        // 源端 DropCommands.PICKUP_GRACE_PERIOD_MS
        public const long PickupGracePeriodMs = 3000;

        /// <summary>掉落者/归属者的地图与坐标（源端 Character.getMap()/getPosition()）。</summary>
        public struct DropOrigin
        {
            public int mapId, x, y, characterId, partyId;
            public static DropOrigin From(BotActor actor) => new DropOrigin
            {
                mapId = actor.mapId,
                x = (int)actor.x,
                y = (int)actor.y,
                characterId = actor.id,
                partyId = MapItemService.PartyOf != null ? MapItemService.PartyOf(actor.id) : -1,
            };
        }

        static void GetPos(BotActor actor, out int mapId, out float x, out float y, out int partyId)
        {
            mapId = actor.mapId; x = actor.x; y = actor.y;
            partyId = MapItemService.PartyOf != null ? MapItemService.PartyOf(actor.id) : -1;
        }

        // ——————————————————— Meso / Equip / Item / Item Qty — 掉在脚下，谁都能捡 ———————————————————

        /// <summary>源端 botDropMeso：spawnMesoDrop(meso, pos, fakechar, fakechar, true, (byte)2, (short)0)。</summary>
        public static MapItem BotDropMeso(BotActor fakechar, int meso)
        {
            GetPos(fakechar, out int mapId, out float x, out float y, out int partyId);
            return MapItemService.SpawnMesoDrop(mapId, meso, x, y, fakechar.id, fakechar.id, partyId, true, 2);
        }

        /// <summary>源端 botDropEquip：generateCleanItemEquip → spawnItemDrop(..., ffaDrop=true, playerDrop=true)。</summary>
        public static MapItem BotDropEquip(BotActor fakechar, int itemId)
        {
            GetPos(fakechar, out int mapId, out float x, out float y, out int partyId);
            return MapItemService.SpawnItemDrop(mapId, fakechar.id, fakechar.id, partyId,
                BotLogic.GenerateCleanItemEquip(itemId), x, y, true, true);
        }

        /// <summary>源端 botDropItem：generateCleanItem → spawnItemDrop(..., true, true)。</summary>
        public static MapItem BotDropItem(BotActor fakechar, int itemId)
        {
            GetPos(fakechar, out int mapId, out float x, out float y, out int partyId);
            return MapItemService.SpawnItemDrop(mapId, fakechar.id, fakechar.id, partyId,
                BotLogic.GenerateCleanItem(itemId), x, y, true, true);
        }

        /// <summary>源端 botDropItemQty：generateCleanItemWithQty → spawnItemDrop(..., true, true)。</summary>
        public static MapItem BotDropItemQty(BotActor fakechar, int itemId, int qty)
        {
            GetPos(fakechar, out int mapId, out float x, out float y, out int partyId);
            return MapItemService.SpawnItemDrop(mapId, fakechar.id, fakechar.id, partyId,
                BotLogic.GenerateCleanItemWithQty(itemId, qty), x, y, true, true);
        }

        /// <summary>
        /// 源端 botDropItemWithExpiry：spawnItemDropNoExpire + 两个定时器——
        /// expiryMs 后广播移除（客户端先看不见），expiryMs + 3000ms 宽限后才真正 makeDisappearItemFromMap。
        /// 期间仍可被拾取，是「限时掉落」的原语义。
        /// </summary>
        public static MapItem BotDropItemWithExpiry(BotActor fakechar, int itemId, bool isEquip, long expiryMs)
        {
            GetPos(fakechar, out int mapId, out float x, out float y, out int partyId);
            var item = isEquip ? BotLogic.GenerateCleanItemEquip(itemId) : BotLogic.GenerateCleanItem(itemId);
            var drop = MapItemService.SpawnItemDropNoExpire(mapId, fakechar.id, fakechar.id, partyId, item, x, y, true, true);
            if (drop != null && expiryMs > 0) ScheduleExpiry(mapId, drop, expiryMs);
            return drop;
        }

        // 源端用 TimerManager.schedule 排两个延时；这里用 BotTasks（同为可取消延时队列）。
        static void ScheduleExpiry(int mapId, MapItem drop, long expiryMs)
        {
            BotTasks.Schedule(() =>
            {
                if (drop.pickedUp) return;
                RemovedVisual(drop);
            }, expiryMs);
            BotTasks.Schedule(() =>
            {
                if (drop.pickedUp) return;
                MapItemService.MakeDisappear(mapId, drop);
            }, expiryMs + PickupGracePeriodMs);
        }

        // 源端 PacketCreator.removeItemFromMap(drop.getObjectId(), 0, 0) 的广播：
        // 只通知客户端消失，服务端实体仍在（宽限期内仍可捡）。这里复用 Removed 事件的 reason 0。
        static void RemovedVisual(MapItem drop)
        {
            MapItemService.NotifyRemoved(drop, 0);
        }

        // ——————————————————— Meso / Equip / Item / Item Qty — 扔到指定点，谁都能捡 ———————————————————

        /// <summary>源端 botThrowMeso：spawnMesoDrop(meso, throwPos, fakechar, fakechar, false, (byte)2, (short)0)。</summary>
        public static MapItem BotThrowMeso(BotActor fakechar, int meso, int throwX, int throwY)
        {
            GetPos(fakechar, out int mapId, out _, out _, out int partyId);
            return MapItemService.SpawnMesoDrop(mapId, meso, throwX, throwY, fakechar.id, fakechar.id, partyId, false, 2);
        }

        /// <summary>源端 botThrowEquip(itemId, throwPos)：spawnItemDrop(..., ffaDrop=true, playerDrop=false)。</summary>
        public static MapItem BotThrowEquip(BotActor fakechar, int itemId, int throwX, int throwY)
        {
            GetPos(fakechar, out int mapId, out _, out _, out int partyId);
            return MapItemService.SpawnItemDrop(mapId, fakechar.id, fakechar.id, partyId,
                BotLogic.GenerateCleanItemEquip(itemId), throwX, throwY, true, false);
        }

        /// <summary>源端 botThrowEquip(Equip eq, throwPos)：直接把已有装备实例扔出去（保留强化等属性）。</summary>
        public static MapItem BotThrowEquip(BotActor fakechar, InventoryEntry eq, int throwX, int throwY)
        {
            if (eq == null) return null;
            GetPos(fakechar, out int mapId, out _, out _, out int partyId);
            return MapItemService.SpawnItemDrop(mapId, fakechar.id, fakechar.id, partyId, eq, throwX, throwY, true, false);
        }

        /// <summary>源端 botThrowItem：generateCleanItem → spawnItemDrop(..., true, false)，返回被扔出的 item。</summary>
        public static InventoryEntry BotThrowItem(BotActor fakechar, int itemId, int throwX, int throwY, out MapItem drop)
        {
            var itemToDrop = BotLogic.GenerateCleanItem(itemId);
            GetPos(fakechar, out int mapId, out _, out _, out int partyId);
            drop = MapItemService.SpawnItemDrop(mapId, fakechar.id, fakechar.id, partyId, itemToDrop, throwX, throwY, true, false);
            return itemToDrop;
        }

        /// <summary>源端 botThrowItemNoExpire：spawnItemDropNoExpire(..., true, false)。</summary>
        public static MapItem BotThrowItemNoExpire(BotActor fakechar, int itemId, int throwX, int throwY)
        {
            GetPos(fakechar, out int mapId, out _, out _, out int partyId);
            return MapItemService.SpawnItemDropNoExpire(mapId, fakechar.id, fakechar.id, partyId,
                BotLogic.GenerateCleanItem(itemId), throwX, throwY, true, false);
        }

        /// <summary>源端 botThrowItemNoExpireOwnerOnly：spawnItemDropNoExpire(..., false, false) + setPermanentOwner(true)。</summary>
        public static MapItem BotThrowItemNoExpireOwnerOnly(BotActor fakechar, int itemId, int throwX, int throwY)
        {
            GetPos(fakechar, out int mapId, out _, out _, out int partyId);
            var drop = MapItemService.SpawnItemDropNoExpire(mapId, fakechar.id, fakechar.id, partyId,
                BotLogic.GenerateCleanItem(itemId), throwX, throwY, false, false);
            if (drop != null) drop.permanentOwner = true;
            return drop;
        }

        /// <summary>源端 botDropItemQtyOwnerOnly：spawnItemDropNoExpire(..., false, false) + setPermanentOwner(true)。</summary>
        public static MapItem BotDropItemQtyOwnerOnly(BotActor fakechar, int itemId, int qty)
        {
            GetPos(fakechar, out int mapId, out float x, out float y, out int partyId);
            var drop = MapItemService.SpawnItemDropNoExpire(mapId, fakechar.id, fakechar.id, partyId,
                BotLogic.GenerateCleanItemWithQty(itemId, qty), x, y, false, false);
            if (drop != null) drop.permanentOwner = true;
            return drop;
        }

        /// <summary>源端 botThrowItemQty：generateCleanItemWithQty → spawnItemDrop(..., true, false)。</summary>
        public static MapItem BotThrowItemQty(BotActor fakechar, int itemId, int qty, int throwX, int throwY)
        {
            GetPos(fakechar, out int mapId, out _, out _, out int partyId);
            return MapItemService.SpawnItemDrop(mapId, fakechar.id, fakechar.id, partyId,
                BotLogic.GenerateCleanItemWithQty(itemId, qty), throwX, throwY, true, false);
        }

        // ——————————————————— 扔给指定归属者（只有 ta 能捡）———————————————————

        /// <summary>源端 botThrowItemToOwner：spawnItemDropNoExpire(dropper, owner, item, throwPos, false, false) + permanentOwner。</summary>
        public static MapItem BotThrowItemToOwner(BotActor dropper, int itemId, int throwX, int throwY, BotActor owner)
        {
            if (owner == null) return null;
            GetPos(dropper, out int mapId, out _, out _, out _);
            int ownerParty = MapItemService.PartyOf != null ? MapItemService.PartyOf(owner.id) : -1;
            var drop = MapItemService.SpawnItemDropNoExpire(mapId, dropper.id, owner.id, ownerParty,
                BotLogic.GenerateCleanItem(itemId), throwX, throwY, false, false);
            if (drop != null) drop.permanentOwner = true;
            return drop;
        }

        /// <summary>源端 botThrowToOwnerMeso：spawnMesoDrop(meso, owner.pos, fakechar, owner, false, (byte)0, (short)0)。</summary>
        public static MapItem BotThrowToOwnerMeso(BotActor fakechar, int meso, BotActor owner)
        {
            if (owner == null) return null;
            GetPos(fakechar, out int mapId, out _, out _, out _);
            int ownerParty = MapItemService.PartyOf != null ? MapItemService.PartyOf(owner.id) : -1;
            return MapItemService.SpawnMesoDrop(mapId, meso, owner.x, owner.y, fakechar.id, owner.id, ownerParty, false, 0);
        }

        /// <summary>源端 botThrowToOwnerEquip：spawnItemDrop(fakechar, owner, equip, owner.pos, false, false)。</summary>
        public static MapItem BotThrowToOwnerEquip(BotActor fakechar, int itemId, BotActor owner)
        {
            if (owner == null) return null;
            GetPos(fakechar, out int mapId, out _, out _, out _);
            int ownerParty = MapItemService.PartyOf != null ? MapItemService.PartyOf(owner.id) : -1;
            return MapItemService.SpawnItemDrop(mapId, fakechar.id, owner.id, ownerParty,
                BotLogic.GenerateCleanItemEquip(itemId), owner.x, owner.y, false, false);
        }

        /// <summary>源端 botThrowToOwnerItem。</summary>
        public static MapItem BotThrowToOwnerItem(BotActor fakechar, int itemId, BotActor owner)
        {
            if (owner == null) return null;
            GetPos(fakechar, out int mapId, out _, out _, out _);
            int ownerParty = MapItemService.PartyOf != null ? MapItemService.PartyOf(owner.id) : -1;
            return MapItemService.SpawnItemDrop(mapId, fakechar.id, owner.id, ownerParty,
                BotLogic.GenerateCleanItem(itemId), owner.x, owner.y, false, false);
        }

        /// <summary>源端 botThrowToOwnerItemQty。</summary>
        public static MapItem BotThrowToOwnerItemQty(BotActor fakechar, int itemId, int qty, BotActor owner)
        {
            if (owner == null) return null;
            GetPos(fakechar, out int mapId, out _, out _, out _);
            int ownerParty = MapItemService.PartyOf != null ? MapItemService.PartyOf(owner.id) : -1;
            return MapItemService.SpawnItemDrop(mapId, fakechar.id, owner.id, ownerParty,
                BotLogic.GenerateCleanItemWithQty(itemId, qty), owner.x, owner.y, false, false);
        }

        /// <summary>源端 botThrowEquipsInARow：以 centerPos 为中心按 spacing 一字排开展示。</summary>
        public static void BotThrowEquipsInARow(BotActor fakechar, int[] items, int centerX, int centerY, int spacing)
        {
            if (items == null || items.Length == 0) return;
            int itemCount = items.Length;
            int startX = centerX - (itemCount / 2) * spacing;
            for (int i = 0; i < itemCount; i++)
                BotThrowEquip(fakechar, items[i], startX + i * spacing, centerY);
        }

        // ——————————————————— 拾取（源端 botLoot*：只移除，不入背包）———————————————————

        /// <summary>源端 botLoot(fakechar, range)：在脚下 rangeSq（距离平方）内捡起全部地面物品。</summary>
        public static void BotLoot(BotActor fakechar, double range)
        {
            GetPos(fakechar, out _, out float x, out float y, out _);
            BotLoot(fakechar, (int)x, (int)y, range);
        }

        /// <summary>源端 botLoot(fakechar, itemLocation, range)：在指定点 rangeSq 内捡起全部。</summary>
        public static void BotLoot(BotActor fakechar, int itemX, int itemY, double range)
        {
            // 源端 getMapObjectsInRange(itemLocation, range, [ITEM]) 后逐个 pickItemDrop
            foreach (var mapItem in MapItemService.ItemsInRange(fakechar.mapId, itemX, itemY, range))
                Pick(fakechar, mapItem);
        }

        /// <summary>源端 botLootLocation：固定 1000（= 脚边）。</summary>
        public static void BotLootLocation(BotActor fakechar, int itemX, int itemY)
            => BotLoot(fakechar, itemX, itemY, 1000);

        /// <summary>源端 botLootOwnerItems：只捡自己（ownerId）的东西。</summary>
        public static void BotLootOwnerItems(BotActor fakechar, int itemX, int itemY, double range)
            => BotLootTargetCharactersItems(fakechar, fakechar, itemX, itemY, range);

        /// <summary>源端 botLootTargetCharactersItems：只捡目标角色归属的物品。</summary>
        public static void BotLootTargetCharactersItems(BotActor fakechar, BotActor targetCharacter, int itemX, int itemY, double range)
        {
            foreach (var mapItem in MapItemService.ItemsInRange(fakechar.mapId, itemX, itemY, range))
                if (mapItem.ownerCharacterId == targetCharacter.id) Pick(fakechar, mapItem);
        }

        // 源端 pickItemDrop(pickupPacket, mapItem)：removeItemFromMap(objectId, 2, fakechar.id)
        static void Pick(BotActor fakechar, MapItem mapItem)
        {
            if (mapItem == null || mapItem.pickedUp) return;
            MapItemService.PickItemDrop(fakechar.mapId, mapItem, 2);
        }

        /// <summary>
        /// 源端 lootItemListOnFloor：按列表顺序逐件捡起，每件之间 sleepAmountSeconds(100)（=Thread.sleep(100ms)）。
        /// 主线程适配：改成 BotTasks 可取消时序，保留「顺序 + 100ms 间隔 + 可取消」语义。
        /// 返回句柄供调用方取消（源端为 void，需要取消语义的调用方可忽略返回值）。
        /// </summary>
        public static BotTasks.Handle LootItemListOnFloor(BotActor fakechar, List<MapItem> items)
        {
            if (fakechar == null || items == null || items.Count == 0) return null;
            int mapId = fakechar.mapId;
            var queue = new Queue<MapItem>(items);
            BotTasks.Handle chain = null;
            void Step()
            {
                if (queue.Count == 0) return;
                var mapItem = queue.Dequeue();
                if (mapItem != null && !mapItem.pickedUp) MapItemService.PickItemDrop(mapId, mapItem, 2);
                if (queue.Count > 0) chain = BotTasks.Schedule(Step, 100);
            }
            Step();
            return chain;
        }

        /// <summary>源端 botLootSelectedItems：先按 itemId 过滤地面物品（范围 10000），再逐件捡起。</summary>
        public static void BotLootSelectedItems(BotActor chr, int[] itemsToLoot)
        {
            var itemsOnFloor = BotLogic.CheckForItemsOnFloor(chr.mapId, (int)chr.x, (int)chr.y, 10000, itemsToLoot);
            if (itemsOnFloor.Count > 0) LootItemListOnFloor(chr, itemsOnFloor);
        }
    }
}
