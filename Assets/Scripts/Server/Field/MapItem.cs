// 权威地面掉落物实体。对齐源端 org.gms.server.maps.MapItem（GMS083 结构）：
//   金币与道具共用一个实体（meso>0 时 item 为 null），归属用 character_ownerid + party_ownerid，
//   dropType 决定自由拾取窗口（0 = userOwn 15 秒，2/3 = FFA 立即可捡）。
// 只存纯数据与归属规则，不碰渲染、不碰背包结算（拾取入库在 MapItemPickup）。
using System;

namespace Server.Field
{
    public sealed class MapItem
    {
        // AbstractMapObject.objectId：地图内对象编号，拾取/移除广播都用它
        public int objectId;

        // item == null 表示金币掉落；meso > 0 时 getItemId() 返回 meso 金额（源端 packet 语义）
        public InventoryEntry item;
        public int meso;

        // 掉落者（MapObject）与归属者（Character）在源端是两个引用；这里只留 id
        public int dropperId;
        public int ownerCharacterId;
        public int partyOwnerId = -1;
        public int questId = -1;

        // 源端 byte type：0 = DROP_USEROWN（15 秒归属），2 = DROP_NOOWN（立即自由），3 = 也算自由
        public byte type;
        public bool pickedUp, playerDrop, partyDrop, permanentOwner;
        public long dropTime;

        public float x, y;

        // 所在地图：源端 MapItem 通过 getMap() 拿，这里在生成时写定，避免跨图误捡。
        public int mapId;

        /// <summary>源端 MapItem.getItemId()：金币掉落返回金额，道具掉落返回 itemId。</summary>
        public int ItemId => meso > 0 ? meso : item.itemId;

        /// <summary>源端 MapItem.getItem().getQuantity()；金币掉落返回金额。</summary>
        public int Quantity => meso > 0 ? meso : item.quantity;

        public bool IsMeso => meso > 0;

        /// <summary>源端 getClientsideOwnerId()：无队伍时给角色 id，否则给队伍 id（队伍拾取归属）。</summary>
        public int ClientSideOwnerId => partyOwnerId == -1 ? ownerCharacterId : partyOwnerId;

        public long AgeMs(long nowMs) => nowMs - dropTime;

        /// <summary>源端 hasExpiredOwnershipTime()：掉落满 15 秒后归属窗口过期。</summary>
        public bool HasExpiredOwnershipTime(long nowMs) => AgeMs(nowMs) >= 15_000;

        /// <summary>源端 isFFADrop()：type 2/3 或归属窗口已过期 = 谁都能捡。</summary>
        public bool IsFFADrop(long nowMs) => type == 2 || type == 3 || HasExpiredOwnershipTime(nowMs);

        /// <summary>
        /// 源端 hasClientsideOwnership(player)：本人、同队，或归属窗口已过期。
        /// </summary>
        public bool HasClientsideOwnership(long nowMs, int characterId, int partyId)
            => ownerCharacterId == characterId || partyOwnerId == partyId || HasExpiredOwnershipTime(nowMs);

        /// <summary>
        /// 源端 canBePickedBy(chr)。注意源端这里会「收养」：本人进队/本人回队时把 partyOwnerId 改成自己的队伍，
        /// 所以调用方传 isPartyMember/partyId 解析器，由本方法在需要时写回 partyOwnerId。
        /// </summary>
        /// <param name="isPartyMemberOfOwner">chr.isPartyMember(ownerCharacterId)：拾取者是否与归属者同队</param>
        public bool CanBePickedBy(long nowMs, int characterId, int partyId, Func<int, bool> isPartyMemberOfOwner)
        {
            if (ownerCharacterId <= 0 || IsFFADrop(nowMs)) return true;

            if (partyOwnerId == -1)
            {
                if (characterId == ownerCharacterId) return true;
                if (isPartyMemberOfOwner != null && isPartyMemberOfOwner(ownerCharacterId))
                {
                    partyOwnerId = partyId;
                    return true;
                }
            }
            else
            {
                if (partyId == partyOwnerId) return true;
                if (characterId == ownerCharacterId)
                {
                    partyOwnerId = partyId;
                    return true;
                }
            }

            return !permanentOwner && HasExpiredOwnershipTime(nowMs);
        }

        /// <summary>源端 MapItem：partyDrop 在构造时由 owner 的队伍状态决定。</summary>
        public void SetOwner(int characterId, int partyId)
        {
            ownerCharacterId = characterId;
            partyOwnerId = partyId;
            partyDrop = partyId != -1;
        }
    }
}
