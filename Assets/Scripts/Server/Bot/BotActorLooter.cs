// IMapItemLooter 的 BotActor 实现：把 MapItemPickup 的拾取决策树接到真实背包/金币。
// 入包规则复用 TradeItemRules.Receive（= 源端 InventoryManipulator.addFromDrop 的堆叠/占格语义），
// 落库复用 BotTradeService.Persist（= 源端 saveCharToDB 的背包两表 + meso），与交易结算同一条路径。
// 效果系统（药水/怪物卡图鉴/脚本道具/NX/提示）通过 Hooks 委托给上层，本文件不发明效果数值。
using System;
using System.Collections.Generic;
using Server.Field;

namespace Server.Bot
{
    /// <summary>拾取流程里需要上层（玩法/前端）处理的效果与 UI 回调。</summary>
    public sealed class BotLootHooks
    {
        /// <summary>源端 chr.needQuestItem(questid, itemid)（questid 0/-1 已在 MapItemPickup 内放行）。</summary>
        public Func<int, int, bool> NeedQuestItem;
        /// <summary>源端 ii.isConsumeOnPickup(itemId)。</summary>
        public Func<int, bool> IsConsumeOnPickup;
        /// <summary>源端 chr.applyConsumeOnPickup(itemId)：拾取即生效，返回是否已处理。</summary>
        public Func<int, bool> ApplyConsumeOnPickup;
        /// <summary>源端 ItemScriptManager 的 runOnPickup（243xxx 脚本道具），返回是否接管。</summary>
        public Func<int, bool> TryRunItemScript;
        /// <summary>源端 getCashShop().gainCash(NX_CREDIT, amount)。</summary>
        public Action<int> GainNxCredit;
        /// <summary>源端 chr.updateAriantScore()。</summary>
        public Action OnAriantSpiritJewel;
        /// <summary>源端 sendPacket(showItemUnavailable())。</summary>
        public Action ShowItemUnavailable;
        /// <summary>源端 sendPacket(getInventoryFull()) + getShowInventoryFull()。</summary>
        public Action ShowInventoryFull;
        /// <summary>源端 enableActions()。</summary>
        public Action EnableActions;
    }

    public sealed class BotActorLooter : IMapItemLooter
    {
        readonly BotActor actor;
        readonly Trade.BotTradeService trades;
        readonly BotPartyService parties;
        readonly BotLootHooks hooks;
        /// <summary>源端 chr.isLoggedInWorld()：金币队伍均分时过滤掉已离线成员。</summary>
        public Func<int, bool> OnlineLookup;
        /// <summary>背包/金币变更已落库（前端据此重灌 Profile 镜像，等价交易的 InventoryCommitted）。</summary>
        public event Action<BotActor> Committed;

        public BotActorLooter(BotActor actor, Trade.BotTradeService trades, BotPartyService parties, BotLootHooks hooks = null)
        {
            this.actor = actor ?? throw new ArgumentNullException(nameof(actor));
            this.trades = trades;
            this.parties = parties;
            this.hooks = hooks ?? new BotLootHooks();
            OnlineLookup = id =>
            {
                var target = parties?.Get(id);
                return target != null && target.online;
            };
        }

        public int CharacterId => actor.id;
        public int PartyId => parties?.For(actor.id)?.id ?? -1;

        public bool IsLoggedInWorld(int characterId) => OnlineLookup?.Invoke(characterId) ?? true;

        // 源端 chr.isPartyMember(characterId)：拾取者与「掉落归属者」是否同队
        public bool IsPartyMember(int characterId)
        {
            if (parties == null) return false;
            var party = parties.For(characterId);
            return party != null && party.members.Contains(actor.id);
        }

        // 源端 chr.getPartyMembersOnSameMap()：同图队友（不含自己）
        public IReadOnlyList<int> PartyMembersOnSameMap()
        {
            var found = new List<int>();
            if (parties == null) return found;
            foreach (var member in parties.Members(actor.id))
                if (member.id != actor.id && member.mapId == actor.mapId && member.online) found.Add(member.id);
            return found;
        }

        public void GainMeso(int amount)
        {
            if (amount == 0) return;
            if (actor is BotCharacter bot) bot.meso = unchecked(bot.meso + amount);
            else if (actor.character != null) actor.character.record.meso = unchecked((int)(actor.character.record.meso + amount));
            Commit();
        }

        static List<InventoryEntry> Inventory(BotActor actor)
            => actor is BotCharacter bot ? bot.inventory
            : actor.character?.inventory ?? throw new InvalidOperationException("Loot actor has no loaded inventory");

        // 源端 InventoryManipulator.checkSpace：用克隆背包试装一次，装得下才返回 true（不留副作用）。
        public bool HasSpace(InventoryEntry item)
        {
            if (item == null) return false;
            var inventory = Inventory(actor);
            var trial = new List<InventoryEntry>();
            foreach (var entry in inventory) trial.Add(entry.Copy());
            return trades != null && trades.Rules.Receive(actor, trial, item);
        }

        // 源端 InventoryManipulator.addFromDrop：真实改背包，成功后由 MapItemPickup 摘除地面物品。
        public bool AddFromDrop(InventoryEntry item)
        {
            if (item == null) return false;
            if (trades == null) return false;
            if (!trades.Rules.Receive(actor, Inventory(actor), item)) return false;
            Commit();
            return true;
        }

        public bool NeedQuestItem(int questId, int itemId) => hooks.NeedQuestItem?.Invoke(questId, itemId) ?? true;
        public bool IsConsumeOnPickup(int itemId) => hooks.IsConsumeOnPickup?.Invoke(itemId) ?? false;
        public bool ApplyConsumeOnPickup(int itemId) => hooks.ApplyConsumeOnPickup?.Invoke(itemId) ?? false;
        public bool TryRunItemScript(int itemId) => hooks.TryRunItemScript?.Invoke(itemId) ?? false;
        public void GainNxCredit(int amount) => hooks.GainNxCredit?.Invoke(amount);
        public void OnAriantSpiritJewel() => hooks.OnAriantSpiritJewel?.Invoke();
        public void ShowItemUnavailable() => hooks.ShowItemUnavailable?.Invoke();
        public void ShowInventoryFull() => hooks.ShowInventoryFull?.Invoke();
        public void EnableActions() => hooks.EnableActions?.Invoke();

        /// <summary>拾取成功后落库（与交易结算同一 Persist），真人角色才需要。</summary>
        public void Commit()
        {
            if (actor.IsBot() || trades == null || actor.character == null) return;
            actor.character.tradeRevision++;
            trades.Persist(actor);
            Committed?.Invoke(actor);
        }
    }
}
