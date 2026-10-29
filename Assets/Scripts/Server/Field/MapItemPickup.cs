// 真人/假人拾取入库的权威路径。对齐源端 org.gms.client.Character.pickupItem(MapObject ob, int petIndex)：
//   400ms 拾取延迟 → canBePickedBy 归属校验 → 锁定/已拾取检查 → 空间检查
//   → isSelfLootableOnly 分支 → needQuestItem → 金币队伍均分 / 243 脚本道具 / NX 卡 / consumeOnPickup / addFromDrop
//   → pickItemDrop 广播移除。
// 与 DropCommands.botLoot*（只移除不入背包，源端假人回收语义）**不是同一条路径**，不能互相替代。
//
// 决策树逐分支照搬源端；效果系统（StatEffect.applyTo、队伍共享道具、脚本道具 runOnPickup）
// 通过 IMapItemLooter 委托给上层，本文件不自行发明效果数值。
using System;
using System.Collections.Generic;

namespace Server.Field
{
    /// <summary>源端 Character 在拾取时暴露给地图物品的最小面（真人与假人各有实现）。</summary>
    public interface IMapItemLooter
    {
        /// <summary>源端 chr.getId()。</summary>
        int CharacterId { get; }
        /// <summary>源端 chr.getPartyId()；无队伍为 -1。</summary>
        int PartyId { get; }
        /// <summary>源端 chr.isLoggedInWorld()：金币队伍均分时只发给仍在世界的成员。</summary>
        bool IsLoggedInWorld(int characterId);

        /// <summary>源端 chr.isPartyMember(characterId)。</summary>
        bool IsPartyMember(int characterId);
        /// <summary>源端 chr.getPartyMembersOnSameMap()：同地图队伍成员 id 列表（不含自己）。</summary>
        IReadOnlyList<int> PartyMembersOnSameMap();

        /// <summary>源端 chr.gainMeso(amount, …)。</summary>
        void GainMeso(int amount);
        /// <summary>源端 InventoryManipulator.checkSpace(client, itemId, quantity, owner)。</summary>
        bool HasSpace(InventoryEntry item);
        /// <summary>源端 InventoryManipulator.addFromDrop(client, item, true)：入背包（含堆叠/占格）。</summary>
        bool AddFromDrop(InventoryEntry item);

        /// <summary>源端 chr.needQuestItem(questid, itemid)。</summary>
        bool NeedQuestItem(int questId, int itemId);
        /// <summary>源端 ii.isConsumeOnPickup(itemId)（spec/consumeOnPickup 或 specEx/consumeOnPickup == 1）。</summary>
        bool IsConsumeOnPickup(int itemId);
        /// <summary>源端 chr.applyConsumeOnPickup(itemId)：拾取即生效（药水/怪物卡图鉴/队伍道具）。返回是否已处理。</summary>
        bool ApplyConsumeOnPickup(int itemId);
        /// <summary>源端 ItemScriptManager.runItemScript 的 runOnPickup 判定（243xxx 脚本道具）。返回是否接管。</summary>
        bool TryRunItemScript(int itemId);

        /// <summary>源端 getCashShop().gainCash(CashShop.NX_CREDIT, amount)。</summary>
        void GainNxCredit(int amount);
        /// <summary>源端 chr.updateAriantScore()（ARPQ_SPIRIT_JEWEL 专用）。</summary>
        void OnAriantSpiritJewel();

        /// <summary>源端 sendPacket(showItemUnavailable())。</summary>
        void ShowItemUnavailable();
        /// <summary>源端 sendPacket(getInventoryFull()) + sendPacket(getShowInventoryFull())。</summary>
        void ShowInventoryFull();
        /// <summary>源端 enableActions()：拾取流程结束复位动作锁。</summary>
        void EnableActions();
    }

    public static class MapItemPickup
    {
        /// <summary>源端 pickupItem：掉落 400ms 内不许捡。</summary>
        public const long PickupDelayMs = 400;

        // 源端 ItemId.NX_CARD_100 / NX_CARD_250
        public const int NxCard100 = 4031865;
        public const int NxCard250 = 4031866;
        // 源端 ItemId.ARPQ_SPIRIT_JEWEL
        public const int ArpqSpiritJewel = 4001157;

        public static bool IsNxCard(int itemId) => itemId == NxCard100 || itemId == NxCard250;
        /// <summary>源端 ItemId.isMonsterCard：itemId/10000 == 238。</summary>
        public static bool IsMonsterCard(int itemId) => itemId / 10000 == 238;

        /// <summary>源端 MapId.isSelfLootableOnly：Happyville 树 209000001..209000015 与 GPQ 泉 990000500..990000502。</summary>
        public static bool IsSelfLootableOnly(int mapId)
            => (mapId >= 209000001 && mapId <= 209000015) || (mapId >= 990000500 && mapId <= 990000502);

        /// <summary>源端 chr.needQuestItem 的快捷判定：questid 0/-1 不限制。</summary>
        public static bool NeedQuestItemUnrestricted(int questId) => questId == 0 || questId == -1;

        /// <summary>
        /// 源端 Character.pickupItem(ob, petIndex)。petIndex > -1 表示宠物捡（广播用 reason 5）。
        /// 返回 true 表示这次调用把地面物品从地图上摘掉了。
        /// </summary>
        public static bool PickupItem(MapItem mapitem, IMapItemLooter looter, long nowMs, int petIndex = -1)
        {
            if (mapitem == null || looter == null) return false;
            if (mapitem.AgeMs(nowMs) < PickupDelayMs) { looter.EnableActions(); return false; }
            // 源端这一段是 `if (now - dropTime < 400 || !canBePickedBy(this)) { enableActions(); return; }`：
            // 归属/延迟被拒时**不发** showItemUnavailable，只复位动作锁。保持原样，不自行加提示。
            if (!mapitem.CanBePickedBy(nowMs, looter.CharacterId, looter.PartyId, looter.IsPartyMember))
            {
                looter.EnableActions();
                return false;
            }

            // 源端 mpcs：只有金币掉落才提前取同图队友（用于均分）
            var mpcs = new List<int>();
            if (mapitem.meso > 0 && !mapitem.pickedUp) mpcs.AddRange(looter.PartyMembersOnSameMap());

            if (mapitem.pickedUp)
            {
                looter.ShowItemUnavailable();
                looter.EnableActions();
                return false;
            }

            bool isPet = petIndex > -1;
            int pickupReason = isPet ? 5 : 2;
            InventoryEntry mItem = mapitem.item;
            bool hasSpaceInventory = true;

            bool bypassSpace = IsNxCard(mapitem.ItemId) || mapitem.meso > 0
                || (looter.IsConsumeOnPickup(mapitem.ItemId) && !IsMonsterCard(mapitem.ItemId))
                || (hasSpaceInventory = looter.HasSpace(mItem));

            if (bypassSpace)
            {
                int mapId = mapitem.mapId;
                if (IsSelfLootableOnly(mapId))
                {
                    // 源端：这些地图只有本人扔的东西才能捡
                    if (!mapitem.playerDrop || mapitem.dropperId == looter.CharacterId)
                    {
                        if (mapitem.meso > 0)
                        {
                            GrantMeso(mapitem, looter, mpcs);
                            MapItemService.PickItemDrop(mapId, mapitem, pickupReason, looter.CharacterId, petIndex);
                        }
                        else if (IsNxCard(mapitem.ItemId))
                        {
                            GrantNx(mapitem, looter);
                            MapItemService.PickItemDrop(mapId, mapitem, pickupReason, looter.CharacterId, petIndex);
                        }
                        else if (looter.AddFromDrop(mItem))
                        {
                            MapItemService.PickItemDrop(mapId, mapitem, pickupReason, looter.CharacterId, petIndex);
                        }
                        else
                        {
                            looter.EnableActions();
                            return false;
                        }
                    }
                    else
                    {
                        looter.ShowItemUnavailable();
                        looter.EnableActions();
                        return false;
                    }
                    looter.EnableActions();
                    return mapitem.pickedUp;
                }

                if (!NeedQuestItemUnrestricted(mapitem.questId) && !looter.NeedQuestItem(mapitem.questId, mapitem.ItemId))
                {
                    looter.ShowItemUnavailable();
                    looter.EnableActions();
                    return false;
                }

                if (mapitem.meso > 0)
                {
                    GrantMeso(mapitem, looter, mpcs);
                }
                else if (mItem != null && mItem.itemId / 10000 == 243)
                {
                    // 源端：243xxx 脚本道具，runOnPickup 成功则跑脚本，否则照常入包
                    if (!looter.TryRunItemScript(mItem.itemId) && !looter.AddFromDrop(mItem))
                    {
                        looter.EnableActions();
                        return false;
                    }
                }
                else if (IsNxCard(mapitem.ItemId))
                {
                    GrantNx(mapitem, looter);
                }
                else if (looter.ApplyConsumeOnPickup(mapitem.ItemId))
                {
                    // 已处理（药水/怪物卡图鉴等），不入背包
                }
                else if (looter.AddFromDrop(mItem))
                {
                    if (mItem != null && mItem.itemId == ArpqSpiritJewel) looter.OnAriantSpiritJewel();
                }
                else
                {
                    looter.EnableActions();
                    return false;
                }

                MapItemService.PickItemDrop(mapId, mapitem, pickupReason, looter.CharacterId, petIndex);
            }
            else if (!hasSpaceInventory)
            {
                looter.ShowInventoryFull();
            }

            looter.EnableActions();
            return mapitem.pickedUp;
        }

        // 源端：金币按同图队伍成员均分（mesosamm = meso / mpcs.size()），否则自己全拿
        static void GrantMeso(MapItem mapitem, IMapItemLooter looter, List<int> mpcs)
        {
            if (mpcs.Count > 0)
            {
                int mesosamm = mapitem.meso / mpcs.Count;
                foreach (int memberId in mpcs)
                    if (looter.IsLoggedInWorld(memberId)) looter.GainMeso(mesosamm);
            }
            else looter.GainMeso(mapitem.meso);
        }

        // 源端：NX_CARD_100 = 100 NX，其它（NX_CARD_250）= 250 NX，按数量相乘
        static void GrantNx(MapItem mapitem, IMapItemLooter looter)
        {
            int nxGain = (mapitem.ItemId == NxCard100 ? 100 : 250) * (mapitem.item?.quantity ?? 1);
            looter.GainNxCredit(nxGain);
        }
    }
}
