using System;
using System.Collections.Generic;

namespace Server.Bot.Trade
{
    // 原ItemInformationProvider/InventoryManipulator交易所需规则。
    public sealed class TradeItemRules
    {
        readonly DbStore db;
        readonly BotItemCatalog catalog;
        public TradeItemRules(DbStore db, BotItemCatalog catalog) { this.db = db; this.catalog = catalog; }
        public static bool Rechargeable(int id) => id / 10000 == 207 || id / 10000 == 233;
        public static bool MonsterCard(int id) => id / 10000 == 238;
        public bool Unique(int id) => BotItemCatalog.Int(catalog.Info(id), "only") == 1;
        bool Config(string key) => GameConfigDao.Get(db, key).Equals("true", StringComparison.OrdinalIgnoreCase);
        public bool CanOffer(InventoryEntry item)
        {
            var info = catalog.Info(item.itemId);
            bool cash = BotItemCatalog.Int(info, "cash") == 1;
            bool unmerchable = Config("use_enforce_unmerchable_cash") && cash
                || Config("use_enforce_unmerchable_pet") && item.itemId / 1000 == 5000;
            if (!Config("trade_limit_item_cash") && unmerchable) return false;
            bool dropRestricted = BotItemCatalog.Int(info, "tradeBlock") == 1 || BotItemCatalog.Int(info, "accountSharable") == 1
                || BotItemCatalog.Int(info, "quest") == 1;
            int karma = item.isEquip ? 16 : 2;
            return Config("trade_limit_item_nodrop") || !dropRestricted || (item.flag & karma) == karma;
        }
        public int SlotMax(int id, BotActor actor)
        {
            var info = catalog.Info(id);
            int value = 0;
            if (info != null)
            {
                int type = id / 1000000, configured = unchecked((short)GameConfigDao.GetInt(db, "item_slot_max"));
                bool change = (type == 2 || type == 4) && configured > 0;
                if (!info.ContainsKey("slotMax")) value = type == 1 ? 1 : change ? configured : 100;
                else { int original = BotItemCatalog.Int(info, "slotMax"); value = original == 1 ? 1 : change ? configured : unchecked((short)original); }
            }
            int skill = id / 10000 == 207 ? (actor.job >= 1400 && actor.job <= 1412 ? 14100000 : 4100000)
                : id / 10000 == 233 ? 5200000 : 0;
            if (skill != 0 && actor.character != null)
                foreach (var entry in actor.character.skills) if (entry.skillId == skill) { value += entry.skillLevel * 10; break; }
            return unchecked((short)value);
        }
        public static void ApplyKarma(InventoryEntry item)
        { int karma = item.isEquip ? 16 : 2; if ((item.flag & karma) == karma) item.flag = (item.flag ^ karma) | 8; }
        public static int SlotLimit(BotActor actor, int type)
        {
            var c = actor.character?.record;
            if (c == null) return type == 5 ? 96 : 24;
            return type switch { 1 => c.equipslots, 2 => c.useslots, 3 => c.setupslots, 4 => c.etcslots, 5 => 96, _ => 0 };
        }
        public static int FreeSlot(BotActor actor, List<InventoryEntry> inventory, int type)
        {
            var used = new HashSet<int>(); foreach (var item in inventory) if (item.inventoryType == type) used.Add(item.position);
            for (int slot = 1, max = SlotLimit(actor, type); slot <= max; slot++) if (!used.Contains(slot)) return slot;
            return -1;
        }
        public bool Receive(BotActor actor, List<InventoryEntry> inventory, InventoryEntry offered)
        {
            int id = offered.itemId;
            if (Unique(id) && !MonsterCard(id)) foreach (var item in inventory) if (item.itemId == id) return false;
            int quantity = unchecked((short)offered.quantity), type = offered.inventoryType;
            if (type == 1)
            {
                if (quantity != 1) return false;
                int slot = FreeSlot(actor, inventory, type); if (slot < 0) return false;
                var copy = offered.Copy(); copy.itemRowId = 0; copy.position = slot; inventory.Add(copy); return true;
            }
            if (Rechargeable(id) || offered.petId != -1)
            {
                int slot = FreeSlot(actor, inventory, type); if (slot < 0) return false;
                var copy = offered.Copy(); copy.itemRowId = 0; copy.position = slot; copy.owner = ""; copy.giftFrom = "";
                inventory.Add(copy); return true;
            }
            int maxStack = SlotMax(id, actor);
            if (maxStack <= 0 && quantity > 0) return false;
            // listById的源端槽位顺序。
            var existing = inventory.FindAll(i => i.inventoryType == type && i.itemId == id && i.position > 0);
            existing.Sort((a, b) => a.position.CompareTo(b.position));
            foreach (var item in existing)
            {
                if (quantity <= 0) break;
                if (item.quantity >= maxStack || item.flag != offered.flag || item.owner != offered.owner) continue;
                int take = Math.Min(quantity, maxStack - item.quantity); item.quantity += take; quantity -= take;
            }
            while (quantity > 0)
            {
                int slot = FreeSlot(actor, inventory, type); if (slot < 0) return false;
                var copy = offered.Copy(); copy.itemRowId = 0; copy.position = slot;
                copy.quantity = Math.Min(quantity, maxStack); copy.giftFrom = ""; quantity -= copy.quantity; inventory.Add(copy);
            }
            return true;
        }
    }
}
