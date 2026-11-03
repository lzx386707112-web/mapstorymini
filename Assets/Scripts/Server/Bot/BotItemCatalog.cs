using System;
using System.Collections.Generic;
using System.Globalization;

namespace Server.Bot
{
    // ItemInformationProvider的只读IMG数据适配；数据由export_solomapling_items.py产出。
    public sealed class BotItemCatalog
    {
        static BotItemCatalog shared;
        // 8.6MB 的商品表走 BotWarmup 的后台解析，别在进图那一帧同步解析。
        public static BotItemCatalog Shared => shared ??= new BotItemCatalog(BotWarmup.Root("Bot/Items"));
        readonly Dictionary<string, object> names, items, descriptions;
        public BotItemCatalog(string json) : this(json == null ? null : MiniJsonDb.Parse(json) as Dictionary<string, object>) { }
        public BotItemCatalog(Dictionary<string, object> root)
        {
            if (root == null) throw new InvalidOperationException("Original bot item catalog is missing");
            names = (Dictionary<string, object>)root["names"];
            items = (Dictionary<string, object>)root["items"];
            descriptions = root.TryGetValue("descriptions", out var desc) ? desc as Dictionary<string, object> : null;
        }
        public int ItemCount => items.Count;
        public IEnumerable<int> ItemIds
        {
            get { foreach (string key in items.Keys) yield return int.Parse(key, CultureInfo.InvariantCulture); }
        }
        public string Name(int id) => names.TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var value) ? value as string : null;
        public string Description(int id) => descriptions != null && descriptions.TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var value) ? value as string : null;
        public Dictionary<string, object> Info(int id)
            => items.TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var value) && value is Dictionary<string, object> item
                && item.TryGetValue("info", out var info) ? info as Dictionary<string, object> : null;
        public static int Int(Dictionary<string, object> info, string key, int fallback = 0)
        {
            if (info == null || !info.TryGetValue(key, out var value) || value == null) return fallback;
            if (value is long l) return unchecked((int)l);
            if (value is double d) return unchecked((int)d);
            return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : fallback;
        }
        Dictionary<string, object> Record(int id)
            => items.TryGetValue(id.ToString(CultureInfo.InvariantCulture), out var value) ? value as Dictionary<string, object> : null;

        // 卷轴的适用范围：源端 ItemInformationProvider 的 `req` 是允许的装备 id 白名单
        // （2616004~2616010 未來心臟專用卷只认六款未來心臟，2643008 妖精之心強化石只认妖精之心）。
        public int[] RequiredEquips(int id)
        {
            var record = Record(id);
            if (record == null || !record.TryGetValue("req", out var raw) || !(raw is List<object> list)) return null;
            var ids = new int[list.Count];
            for (int i = 0; i < list.Count; i++) ids[i] = Convert.ToInt32(list[i], CultureInfo.InvariantCulture);
            return ids;
        }

        // `reqScope` 是段口径：卡勒馬特殊心臟卷（2643009/2643010）没有 req，只有
        // {scope: 167, div: 10000}，即 167xxxx 段的心脏全部可用。条目里没有 reqScope 返回 false。
        public bool ScopedTo(int id, int itemId)
        {
            var record = Record(id);
            if (record == null || !record.TryGetValue("reqScope", out var raw) || !(raw is List<object> list)) return false;
            foreach (var entry in list)
            {
                if (!(entry is List<object> range) || range.Count < 2) continue;
                int scope = Convert.ToInt32(range[0], CultureInfo.InvariantCulture);
                int divisor = Convert.ToInt32(range[1], CultureInfo.InvariantCulture);
                if (divisor > 0 && itemId / divisor == scope) return true;
            }
            return false;
        }

        public Dictionary<string, int> EquipStats(int id)
        {
            var info = Info(id); if (info == null) return null;
            var stats = new Dictionary<string, int>();
            foreach (var pair in info)
                if (pair.Key.StartsWith("inc", StringComparison.Ordinal)) stats[pair.Key.Substring(3)] = Int(info, pair.Key);
            foreach (string key in new[] { "reqJob", "reqLevel", "reqDEX", "reqSTR", "reqINT", "reqLUK", "reqPOP", "cash", "tuc", "cursed", "success", "fs" })
                stats[key] = Int(info, key);
            return stats;
        }
        public InventoryEntry CleanEquip(int id)
        {
            var info = Info(id);
            return new InventoryEntry {
                itemId = id, inventoryType = 1, quantity = 1, isEquip = true,
                str = unchecked((short)Int(info, "incSTR")), dex = unchecked((short)Int(info, "incDEX")),
                int_ = unchecked((short)Int(info, "incINT")), luk = unchecked((short)Int(info, "incLUK")),
                hp = unchecked((short)Int(info, "incMHP")), mp = unchecked((short)Int(info, "incMMP")),
                watk = unchecked((short)Int(info, "incPAD")), matk = unchecked((short)Int(info, "incMAD")),
                wdef = unchecked((short)Int(info, "incPDD")), mdef = unchecked((short)Int(info, "incMDD")),
                acc = unchecked((short)Int(info, "incACC")), avoid = unchecked((short)Int(info, "incEVA")),
                speed = unchecked((short)Int(info, "incSpeed")), jump = unchecked((short)Int(info, "incJump")),
                upgradeslots = unchecked((sbyte)Int(info, "tuc")),
                flag = Int(info, "tradeBlock") == 1 ? 8 : Int(info, "fs") > 0 ? 2 : 0
            };
        }
        public InventoryEntry CleanItem(int id, int quantity = 1) => id / 1000000 == 1 ? CleanEquip(id)
            : new InventoryEntry { itemId = id, inventoryType = id / 1000000, quantity = unchecked((short)quantity) };
    }
}
