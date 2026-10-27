using System;
using System.Collections.Generic;

namespace Server.Bot
{
    // ScrollInfoManager + ScrolledItemComparator + UpgradeSimulator估价部分。
    // 与实际卷轴消耗/成功判定分开：此处按原期望成本公式定价，不掷成功骰。
    public sealed class MarketScrollPricing
    {
        public sealed class Scroll
        {
            public readonly long Price;
            public readonly int SuccessRate, StatBonus;
            public Scroll(long price, int successRate, int statBonus)
            { Price = price; SuccessRate = successRate; StatBonus = statBonus; }
        }
        public sealed class ScrollInfo
        {
            public readonly int Ten, Sixty, Thirty, Seventy;
            public ScrollInfo(int ten, int sixty, int thirty = 0, int seventy = 0)
            { Ten = ten; Sixty = sixty; Thirty = thirty; Seventy = seventy; }
            public int First => Ten != 0 ? Ten : Thirty;
            public int Second => Sixty != 0 ? Sixty : Seventy;
        }
        static readonly Dictionary<int, Dictionary<string, ScrollInfo>> scrolls = new Dictionary<int, Dictionary<string, ScrollInfo>> {
            {147, new Dictionary<string, ScrollInfo> {{"watt", new ScrollInfo(2044702,2044701)}}},
            {133, new Dictionary<string, ScrollInfo> {{"watt", new ScrollInfo(2043302,2043301)}}},
            {100, new Dictionary<string, ScrollInfo> {{"int", new ScrollInfo(0,0,2040013,2040012)}}},
            {104, new Dictionary<string, ScrollInfo> {{"luk", new ScrollInfo(0,0,2040411,2040410)}, {"str", new ScrollInfo(0,0,2040407,2040406)}}},
            {106, new Dictionary<string, ScrollInfo> {{"dex", new ScrollInfo(0,0,2040611,2040610)}}},
            {105, new Dictionary<string, ScrollInfo> {{"dex", new ScrollInfo(2040502,2040501,2040509,2040508)}, {"int", new ScrollInfo(2040514,2040513,2040519,2040518)}, {"luk", new ScrollInfo(0,0,2040521,2040520)}}},
            {108, new Dictionary<string, ScrollInfo> {{"watt", new ScrollInfo(2040805,2040804,2040811,2040810)}, {"matt", new ScrollInfo(0,0,2040815,2040814)}, {"int", new ScrollInfo(0,0,2040815,2040814)}}},
            {107, new Dictionary<string, ScrollInfo> {{"dex", new ScrollInfo(2040705,2040704,2040715,2040714)}}},
            {110, new Dictionary<string, ScrollInfo> {{"str", new ScrollInfo(2041014,2041013,2041035,2041034)}, {"dex", new ScrollInfo(2041020,2041019,2041039,2041038)}, {"int", new ScrollInfo(2041017,2041016,2041037,2041036)}, {"luk", new ScrollInfo(2041023,2041022,2041041,2041040)}}},
            {103, new Dictionary<string, ScrollInfo> {{"int", new ScrollInfo(2040302,2040301,2040305,2040304)}, {"matt", new ScrollInfo(2040302,2040301,2040305,2040304)}, {"dex", new ScrollInfo(2040307,2040306,2040307,2040306)}}},
            {109, new Dictionary<string, ScrollInfo> {{"watt", new ScrollInfo(0,0,2040917,2040916)}, {"matt", new ScrollInfo(0,0,2040922,2040921)}, {"luk", new ScrollInfo(0,0,2040907,2040906)}}}
        };
        readonly MarketItemPool pool;
        readonly BotItemCatalog catalog;
        readonly Random random;
        public MarketScrollPricing(MarketItemPool pool, BotItemCatalog catalog, Random random = null)
        { this.pool = pool; this.catalog = catalog; this.random = random ?? new Random(); }
        public ScrollInfo Find(int itemId, string stat)
        {
            if (!scrolls.TryGetValue(itemId / 10000, out var types) || !types.TryGetValue(stat, out var info)) return null;
            return pool.Exists(info.First) ? info : null;
        }
        public ScrollInfo DefaultScrolls(int itemId)
        {
            int type = itemId / 10000;
            if (type == 133 || type == 147) return Find(itemId, "watt");
            if (!scrolls.ContainsKey(type)) return null;
            int job = BotItemCatalog.Int(catalog.Info(itemId), "reqJob");
            switch (job)
            {
                case 0:
                    string[] stats = { "watt", "matt", "str", "luk", "dex", "int" };
                    for (int i = stats.Length - 1; i > 0; i--)
                    { int j = random.Next(i + 1); string tmp = stats[i]; stats[i] = stats[j]; stats[j] = tmp; }
                    foreach (string stat in stats) { var found = Find(itemId, stat); if (found != null) return found; }
                    return null;
                case 1: return Find(itemId, "str");
                case 2: return Find(itemId, type == 108 ? "matt" : "int");
                case 4: return Find(itemId, "dex");
                case 8: return Find(itemId, "luk") ?? Find(itemId, "dex");
                default: return null;
            }
        }
        public static string HighestStat(InventoryEntry item, InventoryEntry clean, out int bonus)
        {
            int[] differences = { item.watk-clean.watk, item.matk-clean.matk, item.str-clean.str,
                item.dex-clean.dex, item.int_-clean.int_, item.luk-clean.luk };
            string[] names = { "watt", "matt", "str", "dex", "int", "luk" };
            bonus = int.MinValue; string result = null;
            // Java HashMap.put以差值为key；同值后写覆盖前写，故相等必须取最后一个。
            for (int i = 0; i < differences.Length; i++)
                if (differences[i] >= bonus) { bonus = differences[i]; result = names[i]; }
            return result;
        }
        public static int[] Pattern(int slots, int version, Random random)
        {
            int roll = random.Next(100) + 1;
            int first = version >= 40 ? (roll <= 15 ? 4 : roll <= 40 ? 3 : roll <= 85 ? 2 : 1)
                : (roll <= 3 ? 3 : roll <= 38 ? 2 : 1);
            int remaining = slots - first, second = 0;
            if (remaining > 0)
            {
                roll = random.Next(100) + 1;
                second = roll <= 10 ? 5 : roll <= 40 ? 4 : roll <= 70 ? 3 : version >= 40 || roll <= 90 ? 2 : 1;
                second = Math.Min(second, remaining);
            }
            return new[] { first, second }; // 原端第一组不按slots裁剪。
        }
        public static double[,] MinimumCosts(IList<Scroll> options, long baseCost, int slots, int maxBonus, int maxIncrement)
        {
            var costs = new double[slots + 1, maxBonus + 1];
            for (int i = 0; i <= slots; i++) for (int j = 0; j <= maxBonus; j++) costs[i,j] = double.PositiveInfinity;
            costs[0,0] = baseCost;
            for (int i = 0; i < slots; i++) for (int j = 0; j <= maxBonus - maxIncrement; j++)
                foreach (var scroll in options)
                    if (scroll.SuccessRate > 0 && j + scroll.StatBonus <= maxBonus)
                        costs[i+1,j+scroll.StatBonus] = Math.Min(costs[i+1,j+scroll.StatBonus], (costs[i,j] + scroll.Price) / (scroll.SuccessRate / 100.0));
            return costs;
        }
        static long JavaLong(double value) => double.IsNaN(value) ? 0 : value >= long.MaxValue ? long.MaxValue : value <= long.MinValue ? long.MinValue : (long)value;
        public static SortedDictionary<int, long> CleanCosts(double[,] costs)
        {
            var prices = new SortedDictionary<int, long>();
            for (int i = 0; i < costs.GetLength(0); i++) for (int j = 0; j < costs.GetLength(1); j++)
                if (costs[i,j] < double.PositiveInfinity)
                { long value = JavaLong(costs[i,j]); if (!prices.TryGetValue(j, out long old) || value < old) prices[j] = value; }
            var keys = new List<int>(prices.Keys);
            for (int i = 1; i < keys.Count - 1; i++)
                if (prices[keys[i]] > prices[keys[i+1]])
                    prices[keys[i]] = JavaLong(prices[keys[i-1]] + .60 * unchecked(prices[keys[i+1]] - prices[keys[i-1]]));
            for (int i = 1; i < keys.Count; i++) prices[keys[i]] = JavaLong(prices[keys[i]] * .6);
            return prices;
        }
        Scroll ReadScroll(int id)
        {
            var row = pool.Item(id);
            if (row == null || !row.ContainsKey("success_rate")) throw new InvalidOperationException("Original scroll pricing node missing: " + id);
            return new Scroll(pool.CurrentPrice(row), BotItemCatalog.Int(row, "success_rate"), BotItemCatalog.Int(row, "stat_bonus"));
        }
        public int EquipValue(InventoryEntry equip)
        {
            var clean = catalog.CleanEquip(equip.itemId);
            string stat = HighestStat(equip, clean, out int bonus);
            long baseCost = pool.Price(equip.itemId) ?? 100;
            var info = Find(equip.itemId, stat);
            if (info == null) return unchecked((int)baseCost);
            var options = new List<Scroll>();
            if (info.Ten != 0 && info.Sixty != 0) { options.Add(ReadScroll(info.Ten)); options.Add(ReadScroll(info.Sixty)); }
            if (pool.Version > 40 && info.Thirty != 0 && info.Seventy != 0)
            { options.Add(ReadScroll(info.Thirty)); options.Add(ReadScroll(info.Seventy)); }
            int increment = int.MinValue;
            foreach (var option in options) increment = Math.Max(increment, option.StatBonus);
            int maximum = unchecked(increment * clean.upgradeslots);
            var prices = CleanCosts(MinimumCosts(options, baseCost, clean.upgradeslots, maximum, increment));
            long price = prices.TryGetValue(bonus, out long value) ? value : 1;
            return price > int.MaxValue ? int.MaxValue : unchecked((int)price);
        }
        public bool IsClean(InventoryEntry item) => item.inventoryType == 1
            && BotItemCatalog.Int(item.equipmentData, "level") == 0
            && item.upgradeslots == catalog.CleanEquip(item.itemId).upgradeslots;
        public int? ItemValue(InventoryEntry item)
            => item.inventoryType == 1 && !IsClean(item) ? EquipValue(item) : pool.Price(item.itemId);
        static int Improve(int value, Dictionary<string, int> stats, string key)
            => stats.TryGetValue(key, out int bonus) ? unchecked((short)Math.Min(short.MaxValue, value + bonus)) : value;
        public void ImproveStats(InventoryEntry equip, int scrollId)
        {
            var stats = catalog.EquipStats(scrollId);
            if (stats == null) throw new InvalidOperationException("Original scroll IMG missing: " + scrollId);
            equip.str = Improve(equip.str, stats, "STR"); equip.dex = Improve(equip.dex, stats, "DEX");
            equip.int_ = Improve(equip.int_, stats, "INT"); equip.luk = Improve(equip.luk, stats, "LUK");
            equip.watk = Improve(equip.watk, stats, "PAD"); equip.matk = Improve(equip.matk, stats, "MAD");
            equip.wdef = Improve(equip.wdef, stats, "PDD"); equip.mdef = Improve(equip.mdef, stats, "MDD");
            equip.acc = Improve(equip.acc, stats, "ACC"); equip.avoid = Improve(equip.avoid, stats, "EVA");
            equip.speed = Improve(equip.speed, stats, "Speed"); equip.jump = Improve(equip.jump, stats, "Jump");
            equip.hp = Improve(equip.hp, stats, "MHP"); equip.mp = Improve(equip.mp, stats, "MMP");
        }
        public InventoryEntry ScrollEquip(InventoryEntry equip, DbStore db, int gameVersion = 55)
        {
            var info = DefaultScrolls(equip.itemId); if (info == null) return equip;
            int[] pattern = Pattern(equip.upgradeslots, gameVersion, random);
            bool perfect = GameConfigDao.GetBool(db, "use_perfect_gm_scroll", true);
            bool preserveOnFailure = GameConfigDao.GetBool(db, "use_perfect_scrolling", false);
            int rolls = GameConfigDao.GetInt(db, "scroll_chance_rolls", 1);
            for (int group = 0; group < 2; group++) for (int i = 0; i < pattern[group]; i++)
            {
                int scrollId = group == 0 ? info.First : info.Second;
                var stats = catalog.EquipStats(scrollId);
                if (stats == null) throw new InvalidOperationException("Original scroll IMG missing: " + scrollId);
                if (equip.upgradeslots <= 0 && !perfect) continue;
                bool success = perfect || random.NextDouble() >= Math.Pow(1 - stats["success"] / 100.0, rolls);
                if (success)
                {
                    ImproveStats(equip, scrollId);
                    if (!perfect) equip.upgradeslots = unchecked((sbyte)(equip.upgradeslots - 1));
                    int level = BotItemCatalog.Int(equip.equipmentData, "level");
                    if (equip.equipmentData == null) equip.equipmentData = new Dictionary<string, object>();
                    equip.equipmentData["level"] = unchecked((sbyte)(level + 1));
                }
                else
                {
                    if (!preserveOnFailure) equip.upgradeslots = unchecked((sbyte)(equip.upgradeslots - 1));
                    // 源ScrollGivenItem忽略scrollEquipWithId的null返回，故诅咒不销毁这里的对象。
                    random.Next(100);
                }
            }
            equip.upgradeslots = 0;
            return equip;
        }
    }
}
