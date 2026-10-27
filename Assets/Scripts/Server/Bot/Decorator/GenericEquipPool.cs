using System;
using System.Collections.Generic;

namespace Server.Bot.Decorator
{
    public sealed class GenericEquipPool
    {
        readonly Dictionary<string, List<PoolItem>> pools = new Dictionary<string, List<PoolItem>>();
        readonly Random random;
        sealed class PoolItem { public int id, level, gender; }
        public GenericEquipPool(BotResources resources, BotItemCatalog catalog, Random random = null)
        {
            this.random = random ?? new Random();
            var data = resources.ReadData("soloMapling/ArtificialPlayer/BotDecoratorSystem/GenericEquipPool.yaml") as Dictionary<string, object>;
            if (data == null) throw new InvalidOperationException("Original generic equipment pool is missing");
            foreach (var pair in data)
            {
                if (!(pair.Value is List<object> entries)) continue;
                var list = new List<PoolItem>();
                foreach (var raw in entries)
                {
                    int id = Convert.ToInt32(raw);
                    list.Add(new PoolItem { id = id, level = BotItemCatalog.Int(catalog.Info(id), "reqLevel"), gender = id / 1000 % 10 });
                }
                pools[pair.Key] = list;
            }
        }
        public int? Pick(string category, int level, int gender)
        {
            if (!pools.TryGetValue(category, out var list)) return null;
            var eligible = new List<PoolItem>();
            var weights = new List<double>();
            double total = 0;
            foreach (var item in list)
            {
                if (item.level > level || (item.gender != 2 && item.gender != gender)) continue;
                double weight = Math.Max(.05, 1.0 / (1.0 + (level - item.level) * .05));
                eligible.Add(item); weights.Add(weight); total += weight;
            }
            if (eligible.Count == 0) return null;
            double roll = random.NextDouble() * total, accumulated = 0;
            for (int i = 0; i < eligible.Count; i++)
            { accumulated += weights[i]; if (roll <= accumulated) return eligible[i].id; }
            return eligible[eligible.Count - 1].id;
        }
    }
}
