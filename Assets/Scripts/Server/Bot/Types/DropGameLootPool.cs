// DropGameLootPool.java 对齐移植：按 tier（medium/elite）从 YAML 加载权重奖池，
// 每次掉落先掷 33% 决定是否走 special_items，再在对应列表里按权重抽取。
// 源端直接读文件路径，这里改读打包好的 SoloMapling 资源包（同一条相对路径）。
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Server.Bot.Types
{
    public sealed class DropGameLootPool
    {
        // 源端 LOOT_POOL_PATH 的资源包内路径
        public const string ResourcePath = "soloMapling/ArtificialPlayer/BotDialoguePack/DropGameLootPool.yaml";
        const int SpecialChancePercent = 33;

        public readonly struct LootEntry
        {
            public readonly int itemId;
            public readonly int weight;
            public readonly bool isEquip;
            public LootEntry(int itemId, int weight, bool isEquip)
            { this.itemId = itemId; this.weight = weight; this.isEquip = isEquip; }
        }

        static readonly Random sharedRandom = new Random();
        readonly Random random;
        readonly List<LootEntry> entries = new List<LootEntry>();
        readonly List<LootEntry> specialEntries = new List<LootEntry>();
        int totalWeight, specialTotalWeight;

        public DropGameLootPool(Random random = null) { this.random = random ?? sharedRandom; }

        public bool IsEmpty => entries.Count == 0 && specialEntries.Count == 0;
        public int Size => entries.Count + specialEntries.Count;

        public static DropGameLootPool Load(string tier, Random random = null)
        {
            var pool = new DropGameLootPool(random);
            try
            {
                var root = BotResources.Load().ReadData(ResourcePath) as Dictionary<string, object>;
                if (root == null || !root.TryGetValue(tier, out var node)
                    || !(node is Dictionary<string, object> tierNode)) return pool;
                LoadEntries(tierNode, "items", pool.entries, pool, false);
                LoadEntries(tierNode, "special_items", pool.specialEntries, pool, true);
            }
            catch (Exception)
            {
                // 源端同样只记日志、返回空池；空池由调用方走降级逻辑。
            }
            return pool;
        }

        static void LoadEntries(Dictionary<string, object> tierNode, string key, List<LootEntry> target,
            DropGameLootPool pool, bool special)
        {
            if (!tierNode.TryGetValue(key, out var value) || !(value is List<object> items)) return;
            int weight = 0;
            foreach (var raw in items)
            {
                if (!(raw is Dictionary<string, object> entry)) continue;
                int itemId = ToInt(entry, "id");
                int w = ToInt(entry, "weight");
                // 源端 entry.containsKey("equip") && Boolean.parseBoolean(...)
                bool isEquip = entry.TryGetValue("equip", out var flag)
                    && bool.TryParse(Convert.ToString(flag, CultureInfo.InvariantCulture), out bool parsed) && parsed;
                target.Add(new LootEntry(itemId, w, isEquip));
                weight += w;
            }
            if (special) pool.specialTotalWeight = weight; else pool.totalWeight = weight;
        }

        // 导出后的 YAML 数值都是字符串（源端 Integer.parseInt(String.valueOf(...))）。
        static int ToInt(Dictionary<string, object> entry, string key)
        {
            if (!entry.TryGetValue(key, out var value) || value == null) return 0;
            if (value is long l) return unchecked((int)l);
            if (value is int i) return i;
            return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int n) ? n : 0;
        }

        public LootEntry? RollItem()
        {
            if (entries.Count == 0 && specialEntries.Count == 0) return null;
            bool useSpecial = specialEntries.Count > 0 && random.Next(100) < SpecialChancePercent;
            if (useSpecial) return RollFrom(specialEntries, specialTotalWeight);
            if (entries.Count > 0) return RollFrom(entries, totalWeight);
            return RollFrom(specialEntries, specialTotalWeight);
        }

        LootEntry? RollFrom(List<LootEntry> list, int total)
        {
            if (list.Count == 0 || total == 0) return null;
            int roll = random.Next(total);
            int cumulative = 0;
            foreach (var entry in list)
            {
                cumulative += entry.weight;
                if (roll < cumulative) return entry;
            }
            return list[list.Count - 1];
        }
    }
}
