using System;
using System.Collections.Generic;

namespace Server.Bot.Trade
{
    // Trade.java的单机主线程适配。AI报价与玩家库存托管分开，提交双边结算使用同一数据库事务。
    public sealed class BotTradeService
    {
        readonly DbStore db;
        readonly TradeItemRules rules;
        /// <summary>地面拾取入库复用同一套堆叠/占格规则（TradeItemRules.Receive）。</summary>
        public TradeItemRules Rules => rules;
        readonly HashSet<Session> invitations = new HashSet<Session>();
        public Action<BotActor, string> ChatOutput;
        public Action<BotActor, TradeResult> ResultOutput;
        public Action<Exception> OnError;
        public Action<BotActor, BotActor> Invited;
        public event Action<BotActor,int> InventoryCommitted;
        public BotTradeService(DbStore db, BotItemCatalog catalog) { this.db = db; rules = new TradeItemRules(db, catalog); }
        public Session Start(BotActor actor)
        {
            if (actor.trade is Session prior) return prior;
            if (actor.trade != null) return null;
            var session = new Session(this, actor); actor.trade = session; return session;
        }
        public bool Invite(BotActor from, BotActor to)
        {
            if (from == null || to == null || from.id == to.id || to.trade != null) return false;
            var local = Start(from);
            if (local == null || local.FullTrade || to.trade != null || local.other != null) return false;
            int minimum = GameConfigDao.GetInt(db, "minimum_gm_level_to_trade");
            int gmFrom = from is BotCharacter fb ? fb.gmLevel : from.character?.record.gm ?? 0;
            int gmTo = to is BotCharacter tb ? tb.gmLevel : to.character?.record.gm ?? 0;
            if ((gmFrom > 0 && gmTo == 0 && gmFrom < minimum) || (gmFrom == 0 && gmTo > 0 && gmTo < minimum))
            { local.Cancel(TradeResult.NO_RESPONSE); return false; }
            var remote = Start(to); local.other = remote; remote.other = local;
            remote.IsIncoming = true; remote.InvitationPending = true; invitations.Add(remote);
            var bot = BotStore.GetBotById(to.id);
            if (to.IsBot() && bot != null && ReferenceEquals(bot.chr, to)) bot.tradeLogic.queue.Add(to, from);
            Invited?.Invoke(from, to); return true;
        }
        public void TickInvitationTimeouts()
        {
            foreach (var session in new List<Session>(invitations))
                if (session.invitationTicks > 5)
                {
                    session.InvitationPending = false; invitations.Remove(session);
                    // InviteCoordinator expires the invite only. A subsequent visitTrade cancels it.
                }
                else session.invitationTicks++;
        }
        public static int Fee(long amount) => amount >= 100000000 ? (int)(amount * 6 / 100)
            : amount >= 25000000 ? (int)(amount * 5 / 100) : amount >= 10000000 ? (int)(amount * 4 / 100)
            : amount >= 5000000 ? (int)(amount * 3 / 100) : amount >= 1000000 ? (int)(amount * 18 / 1000)
            : amount >= 100000 ? (int)(amount * 8 / 1000) : 0;
        static List<InventoryEntry> Inventory(BotActor actor) => actor is BotCharacter bot ? bot.inventory
            : actor.character?.inventory ?? throw new InvalidOperationException("Trade actor has no loaded inventory");
        static int Balance(BotActor actor) => actor is BotCharacter bot ? bot.meso : actor.character.record.meso;
        static void Balance(BotActor actor, int value)
        { if (actor is BotCharacter bot) bot.meso = value; else actor.character.record.meso = value; }
        static List<InventoryEntry> Clone(List<InventoryEntry> source)
        { var copy = new List<InventoryEntry>(); foreach (var item in source) copy.Add(item.Copy()); return copy; }
        // 背包/金币落库：交易结算与地面拾取（BotActorLooter）共用同一条持久化路径，避免两处口径漂移。
        public void Persist(BotActor actor)
        {
            if (actor.IsBot()) return;
            int cid = actor.id;
            var items = Inventory(actor);
            var ids = new HashSet<long>(); foreach (var row in InventoryDao.ItemsByCharacter(db, cid)) ids.Add(DbStore.Long(row, "inventoryitemid"));
            db.Table("inventoryequipment").Delete(r => ids.Contains(DbStore.Long(r, "inventoryitemid")));
            db.Table("inventoryitems").Delete(DbStore.Eq("characterid", (long)cid));
            foreach (var item in items)
            {
                var row = new DbRow(); if (item.itemData != null) foreach (var pair in item.itemData) row[pair.Key] = pair.Value;
                row.Remove("inventoryitemid"); row["characterid"] = cid; row["type"] = item.itemId / 1000000;
                row["itemid"] = item.itemId; row["inventorytype"] = item.inventoryType; row["position"] = item.position;
                row["quantity"] = item.quantity; row["flag"] = item.flag; row["owner"] = item.owner;
                row["giftFrom"] = item.giftFrom; row["petid"] = item.petId; row["expiration"] = item.expiration;
                long id = db.Table("inventoryitems").Insert(row);
                item.itemRowId = id; item.itemData = new Dictionary<string, object>(row);
                if (!item.isEquip) continue;
                var equip = new DbRow(); if (item.equipmentData != null) foreach (var pair in item.equipmentData) equip[pair.Key] = pair.Value;
                equip.Remove("inventoryequipmentid"); equip["inventoryitemid"] = id;
                equip["str"] = item.str; equip["dex"] = item.dex; equip["int"] = item.int_; equip["luk"] = item.luk;
                equip["hp"] = item.hp; equip["mp"] = item.mp; equip["watk"] = item.watk; equip["matk"] = item.matk;
                equip["wdef"] = item.wdef; equip["mdef"] = item.mdef; equip["acc"] = item.acc; equip["avoid"] = item.avoid;
                equip["hands"] = item.hands; equip["speed"] = item.speed; equip["jump"] = item.jump; equip["upgradeslots"] = item.upgradeslots;
                db.Table("inventoryequipment").Insert(equip);
                item.equipmentData = new Dictionary<string, object>(equip);
            }
            db.Table("characters").Update(DbStore.Eq("id", (long)cid), new DbRow { ["meso"] = Balance(actor) });
        }
        void CommitInventories(Session first, List<InventoryEntry> left, int leftBalance, Session second, List<InventoryEntry> right, int rightBalance)
        {
            var oldLeft = Clone(Inventory(first.actor)); int oldLeftBalance = Balance(first.actor);
            var oldRight = second == null ? null : Clone(Inventory(second.actor)); int oldRightBalance = second == null ? 0 : Balance(second.actor);
            db.BeginTransaction();
            try
            {
                Inventory(first.actor).Clear(); Inventory(first.actor).AddRange(left); Balance(first.actor, leftBalance); Persist(first.actor);
                if (second != null) { Inventory(second.actor).Clear(); Inventory(second.actor).AddRange(right); Balance(second.actor, rightBalance); Persist(second.actor); }
                db.Commit();
            }
            catch
            {
                db.Rollback(); Inventory(first.actor).Clear(); Inventory(first.actor).AddRange(oldLeft); Balance(first.actor, oldLeftBalance);
                if (second != null) { Inventory(second.actor).Clear(); Inventory(second.actor).AddRange(oldRight); Balance(second.actor, oldRightBalance); }
                throw;
            }
            PublishInventory(first.actor,leftBalance-oldLeftBalance);
            if (second!=null) PublishInventory(second.actor,rightBalance-oldRightBalance);
        }
        void PublishInventory(BotActor actor,int mesoDelta)
        {
            if (actor.IsBot()) return;
            actor.character.tradeRevision++;
            try { InventoryCommitted?.Invoke(actor,mesoDelta); }
            catch (Exception e) { OnError?.Invoke(e); }
        }
        // 商店复用同一背包持久化与UI修订通知；不建立虚假的交易会话。
        public bool Purchase(BotActor actor, InventoryEntry item, int price)
        {
            if (actor == null || actor.trade != null || price < 0 || Balance(actor) < price) return false;
            var before = Clone(Inventory(actor)); var after = Clone(before); int balance = Balance(actor);
            if (!rules.Receive(actor, after, item)) return false;
            db.BeginTransaction();
            try
            {
                Inventory(actor).Clear(); Inventory(actor).AddRange(after); Balance(actor, balance - price); Persist(actor);
                db.Commit();
            }
            catch
            {
                db.Rollback(); Inventory(actor).Clear(); Inventory(actor).AddRange(before); Balance(actor, balance); throw;
            }
            PublishInventory(actor, -price); return true;
        }
        public sealed class Session : IBotTradeSession
        {
            readonly BotTradeService service;
            readonly List<InventoryEntry> items = new List<InventoryEntry>();
            internal Session other;
            bool ended;
            public BotActor actor { get; }
            public IBotTradeSession partner => other;
            public int meso { get; private set; }
            public bool locked { get; private set; }
            public bool FullTrade { get; private set; }
            public bool IsIncoming { get; internal set; }
            public bool InvitationPending { get; internal set; }
            internal int invitationTicks;
            public IList<InventoryEntry> Items => new List<InventoryEntry>(items);
            public Action<TradeResult> ResultCallback { get; set; }
            internal Session(BotTradeService service, BotActor actor) { this.service = service; this.actor = actor; }
            public bool AddItem(InventoryEntry item, int slot)
            {
                if (!actor.IsBot() || locked) return false;
                return AddEscrow(item, slot);
            }
            bool AddEscrow(InventoryEntry item, int slot)
            {
                if (ended || items.Count > 9) return false;
                foreach (var prior in items) if (prior.position == slot) return false;
                var copy = item.Copy(); copy.position = slot; items.Add(copy); return true;
            }
            public bool SwapItem(InventoryEntry item, int slot)
            {
                if (!actor.IsBot() || locked || ended || items.Count > 9) return false;
                items.RemoveAll(prior => prior.position == slot);
                var copy = item.Copy(); copy.position = slot; items.Add(copy); return true;
            }
            public bool OfferFromInventory(int type, int position, int quantity, int targetSlot)
            {
                if (ended || locked || targetSlot < 1 || targetSlot > 9 || position <= 0 || quantity < 1) return false;
                var inventory = Clone(Inventory(actor));
                var item = inventory.Find(i => i.inventoryType == type && i.position == position);
                if (item == null || quantity > item.quantity || !service.rules.CanOffer(item)) return false;
                if (TradeItemRules.Rechargeable(item.itemId)) quantity = item.quantity;
                var offered = item.Copy(); offered.quantity = quantity;
                if (!AddEscrow(offered, targetSlot)) return false;
                item.quantity -= quantity; if (item.quantity == 0) inventory.Remove(item);
                try { service.CommitInventories(this, inventory, Balance(actor), null, null, 0); }
                catch { items.RemoveAt(items.Count - 1); throw; }
                return true;
            }
            public void SetMeso(int amount)
            {
                if (locked) throw new InvalidOperationException("Trade is locked");
                if (ended || amount < 0 || Balance(actor) < amount) return;
                service.CommitInventories(this, Clone(Inventory(actor)), Balance(actor) - amount, null, null, 0);
                meso = unchecked(meso + amount);
            }
            public void SetMesoBot(int amount) { if (actor.IsBot() && !ended && !locked && amount >= 0) meso = amount; }
            public void Chat(string text) { if (!ended) service.ChatOutput?.Invoke(actor, text); }
            public void Accept()
            {
                if (ended || other == null || actor.trade != this || other.actor.trade != other) return;
                if (FullTrade || !IsIncoming) return;
                if (!InvitationPending) { Cancel(TradeResult.NO_RESPONSE); return; }
                InvitationPending = false; service.invitations.Remove(this);
                FullTrade = true; other.FullTrade = true;
            }
            public void Confirm()
            {
                if (ended || locked || other == null) return;
                locked = true;
                if (!other.locked) return;
                Complete();
            }
            void Complete()
            {
                long leftMeso = (long)Balance(actor) + other.meso - Fee(other.meso);
                long rightMeso = (long)Balance(other.actor) + meso - Fee(meso);
                if (leftMeso < 0 || leftMeso > int.MaxValue || rightMeso < 0 || rightMeso > int.MaxValue)
                { Cancel(TradeResult.UNSUCCESSFUL); return; }
                var left = Clone(Inventory(actor)); var right = Clone(Inventory(other.actor));
                if (!BuildIncoming(this, left, other.items) || !BuildIncoming(other, right, items)) return;
                int maxLevel = GameConfigDao.GetInt(service.db, "trade_limit_meso_under_level"); if (maxLevel == 0) maxLevel = 15;
                int maxMeso = GameConfigDao.GetInt(service.db, "trade_limit_meso_max"); if (maxMeso == 0) maxMeso = 1000000;
                // 原端先local再else-if partner，只有选中的一方累计。
                Session limited = maxLevel != -1 && actor.level <= maxLevel ? this
                    : maxLevel != -1 && other.actor.level <= maxLevel ? other : null;
                int received = limited == this ? other.meso : meso;
                if (limited != null && maxMeso != -1 && (long)limited.actor.mesosTraded + received > maxMeso)
                { limited.Cancel(TradeResult.NO_RESPONSE); return; }
                try { service.CommitInventories(this, left, (int)leftMeso, other, right, (int)rightMeso); }
                catch (Exception e) { service.OnError?.Invoke(e); Cancel(TradeResult.UNSUCCESSFUL); return; }
                if (limited != null) limited.actor.mesosTraded += received;
                meso = 0; other.meso = 0; items.Clear(); other.items.Clear();
                InvitationPending = other.InvitationPending = false;
                service.invitations.Remove(this); service.invitations.Remove(other);
                ended = other.ended = true;
                Notify(this, TradeResult.SUCCESSFUL); Notify(other, TradeResult.SUCCESSFUL);
                actor.trade = null; other.actor.trade = null;
            }
            bool BuildIncoming(Session receiver, List<InventoryEntry> inventory, List<InventoryEntry> offered)
            {
                foreach (var item in offered)
                {
                    if (service.rules.Unique(item.itemId) && item.quantity > 1)
                    { receiver.Cancel(TradeResult.UNSUCCESSFUL); return false; }
                    var copy = item.Copy(); TradeItemRules.ApplyKarma(copy);
                    if (service.rules.Receive(receiver.actor, inventory, copy)) continue;
                    bool unique = false;
                    foreach (var prior in Inventory(receiver.actor))
                        if (prior.itemId == copy.itemId && service.rules.Unique(copy.itemId)) { unique = true; break; }
                    receiver.Cancel(unique ? TradeResult.UNSUCCESSFUL_UNIQUE_ITEM_LIMIT : TradeResult.UNSUCCESSFUL); return false;
                }
                return true;
            }
            static void Notify(Session session, TradeResult result)
            {
                var bot = BotStore.GetBotById(session.actor.id);
                if (bot != null && ReferenceEquals(bot.chr, session.actor)) bot.tradeLogic.Clear(session.actor);
                try
                {
                    session.service.ResultOutput?.Invoke(session.actor, result);
                    if (result == TradeResult.SUCCESSFUL) session.ResultCallback?.Invoke(result);
                }
                catch (Exception e) { session.service.OnError?.Invoke(e); }
            }
            public void Decline() => Cancel(TradeResult.PARTNER_CANCEL);
            public void Cancel(TradeResult result)
            {
                if (ended) return;
                var left = Clone(Inventory(actor));
                foreach (var item in items)
                    if (!service.rules.Receive(actor, left, item)) throw new InvalidOperationException("Cannot return escrowed trade items");
                List<InventoryEntry> right = null;
                if (other != null)
                {
                    right = Clone(Inventory(other.actor));
                    foreach (var item in other.items)
                        if (!service.rules.Receive(other.actor, right, item)) throw new InvalidOperationException("Cannot return partner escrowed trade items");
                }
                service.CommitInventories(this, left, checked(Balance(actor) + meso), other, right, other == null ? 0 : checked(Balance(other.actor) + other.meso));
                InvitationPending = false; service.invitations.Remove(this);
                if (other != null) { other.InvitationPending = false; service.invitations.Remove(other); }
                meso = 0; items.Clear(); ended = true; actor.trade = null;
                Notify(this, result == TradeResult.PARTNER_CANCEL ? TradeResult.NO_RESPONSE : result);
                if (other != null)
                {
                    other.meso = 0; other.items.Clear(); other.ended = true; other.actor.trade = null;
                    Notify(other, result == TradeResult.UNSUCCESSFUL_UNIQUE_ITEM_LIMIT ? TradeResult.UNSUCCESSFUL : result);
                }
            }
        }
    }
}
