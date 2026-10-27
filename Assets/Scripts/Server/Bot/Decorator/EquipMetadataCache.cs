using System;
using System.Collections.Generic;

namespace Server.Bot.Decorator
{
    public sealed class EquipMetadataCache
    {
        public sealed class Entry
        {
            public int Id, Gender, RequiredLevel, RequiredJob, Price;
            public string Type, Name;
            public bool Cash, Untradeable, Quest;
        }
        readonly List<Entry> all = new List<Entry>();
        public IReadOnlyList<Entry> All => all;
        public EquipMetadataCache(BotItemCatalog catalog, Dictionary<string, object> pools)
        {
            var ranges = (List<object>)pools["ranges"];
            var extras = (List<object>)pools["extraRingRanges"];
            foreach (int id in catalog.ItemIds)
            {
                string type = null;
                foreach (List<object> range in extras)
                    if (id >= Convert.ToInt32(range[0]) && id <= Convert.ToInt32(range[1])) { type = "RING"; break; }
                if (type == null)
                    foreach (List<object> range in ranges)
                        if (id >= Convert.ToInt32(range[1]) && id <= Convert.ToInt32(range[2])) { type = (string)range[0]; break; }
                var info = catalog.Info(id);
                if (type == null || info == null) continue;
                int gender = id / 1000 % 10;
                all.Add(new Entry { Id = id, Type = type, Gender = gender <= 2 ? gender : 2,
                    RequiredLevel = BotItemCatalog.Int(info, "reqLevel"), RequiredJob = BotItemCatalog.Int(info, "reqJob"),
                    Cash = BotItemCatalog.Int(info, "cash") == 1, Untradeable = BotItemCatalog.Int(info, "tradeBlock") == 1,
                    Quest = BotItemCatalog.Int(info, "quest") == 1, Price = BotItemCatalog.Int(info, "price"), Name = catalog.Name(id) ?? "?" });
            }
            all.Sort((a, b) => a.Id.CompareTo(b.Id));
        }
        public IEnumerable<Entry> ByType(string type, bool cashOnly = false)
        { foreach (var entry in all) if (entry.Type == type && (!cashOnly || entry.Cash)) yield return entry; }
        public static Dictionary<string, object> LoadPools() =>
            MiniJsonDb.Parse(PlatformRes.LoadText("Bot/EquipmentPools")) as Dictionary<string, object>
            ?? throw new InvalidOperationException("Original equipment pool data is missing");
    }
}
