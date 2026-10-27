using System.Collections.Generic;

namespace Server.Bot.Trade
{
    public readonly struct ItemQuantity
    {
        public readonly int itemId, quantity;
        public ItemQuantity(int itemId, int quantity = 1)
        { this.itemId = itemId; this.quantity = quantity > 0 ? quantity : 1; }
    }

    // BotTradeWants.java。重复需求逐条核验，不擅自合并改变源端规则。
    public sealed class BotTradeWants
    {
        public int mesoWanted = 1, mesoOffering;
        public readonly List<ItemQuantity> itemsWanted = new List<ItemQuantity>(9);
        public void AddItemWanted(int id, int quantity = 1) => itemsWanted.Add(new ItemQuantity(id, quantity));
        public ItemQuantity? GetItemQuantityAt(int index)
            => index >= 0 && index < itemsWanted.Count ? itemsWanted[index] : (ItemQuantity?)null;
        public List<int> GetListItemsWanted()
        {
            var ids = new List<int>(itemsWanted.Count);
            foreach (var item in itemsWanted) ids.Add(item.itemId);
            return ids;
        }
        public void SetWants(int mesos, IEnumerable<int> ids)
        {
            Reset(); mesoWanted = mesos;
            foreach (int id in ids) AddItemWanted(id);
        }
        public void SetWants(int mesos, int id) => SetWants(mesos, new[] { id });
        public void SetWants(int id) => SetWants(0, id);
        public void SetWants(IEnumerable<int> ids) => SetWants(0, ids);
        public bool VerifyTrade(int mesos, IList<InventoryEntry> offered)
            => VerifySufficientMeso(mesos) && VerifySufficientItems(offered);
        public bool VerifySufficientMeso(int mesos)
        {
            if (mesos >= mesoWanted) return true;
            double tolerance = mesoWanted >= 1000000000 ? .01 : mesoWanted >= 100000000 ? .02
                : mesoWanted >= 10000000 ? .05 : mesoWanted >= 1000000 ? .10
                : mesoWanted >= 100000 ? .15 : mesoWanted >= 10000 ? .20 : 0;
            return unchecked(mesoWanted - mesos) <= (int)(mesoWanted * tolerance);
        }
        public bool VerifySufficientItems(IList<InventoryEntry> offered)
        {
            if (itemsWanted.Count == 0) return true;
            if (offered == null || offered.Count == 0) return false;
            var counts = new Dictionary<int, int>();
            foreach (var item in offered)
            {
                counts.TryGetValue(item.itemId, out int current);
                counts[item.itemId] = unchecked(current + (item.quantity > 1 ? item.quantity : 1));
            }
            foreach (var wanted in itemsWanted)
                if (!counts.TryGetValue(wanted.itemId, out int available) || available < wanted.quantity) return false;
            return true;
        }
        public void Reset() { mesoWanted = 4206969; itemsWanted.Clear(); }
    }

    public sealed class BotTradeInventory
    {
        public readonly List<InventoryEntry> itemsForSale = new List<InventoryEntry>(9);
        public InventoryEntry MainItem => itemsForSale.Count == 0 ? null : itemsForSale[0];
        public void SetMain(InventoryEntry item)
        { if (itemsForSale.Count == 0) itemsForSale.Add(item); else itemsForSale[0] = item; }
        public void Add(InventoryEntry item) => itemsForSale.Add(item);
        public void Remove(InventoryEntry item) => itemsForSale.Remove(item);
        public void Reset() => itemsForSale.Clear();
    }
}
