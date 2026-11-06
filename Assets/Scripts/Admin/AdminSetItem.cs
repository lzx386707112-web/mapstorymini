// 套装管理（持久化口径对齐源端 SetItemController + SetItemConfigService，URL 与 DTO 字段不变）。
//
// 2026-11-06 起内置套装改为 TMS282 客户端 Etc/SetItemInfo.img 全量导出的 877 套
// （Resources/Admin/SetItem282.json，导出工具 tools/ms282/export_set_item_data.py）。
// 显示层与玩法结算共用这一份数据（用户决策「全量替换结算数据」）；繁体原文留在 *Tw
// 字段里供审计，运行时一律取简体字段。旧的 55 套 beidou catalog 只作为资源缺失时的回退。
// 两类持久化仍进 game_config：
//   set_item_bonus_overrides        {"套装id:档位件数": {"STAT": 差量}}（REMOVED_VALUE=int.MinValue 表示删除该属性）
//   set_item_catalog_customizations {"customDefinitions":[...],"disabledBuiltInIds":[...],"slotOverrides":{...}}
// 语义逐条对齐源端：覆盖层只存与默认值的差量；槽位与默认一致时删覆盖；自定义套装 id 从 20000 起。
//
// 推断边界（完整推导见 .agents/skills/beidou-client-reverse/references/inventory-and-tooltip.md）：
//   ① 现代 ItemID 是扁平 int 表，槽位分组由装备 id 前缀推断，EXE 的分组函数未定位；
//      因此结算改按「穿着件数中属于本套装的数量」计（可重复戒指套装才可达高档），
//      显示仍按推断出的槽位行 + (无) 补齐 completeCount。
//   ② activeSkill 的「达成→授予 / 卸下→移除」触发链未逐指令定位，本工程以
//      ActiveSkillsFor() 每次重算出的授予表为准，由 NativeSetItemSkills 承接。
//   ③ Option 的结算函数地址未定位，按 Item/ItemOption.img level 节点通用属性键映射；
//      incDAMr 仅在该档带 boss:1 时归 BossDamage，否则归 Damage。
//   ④ incPQEXPr 等无对应机制的字段只显示、不参与结算，面板上标注「未接入」。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

public static class AdminSetItem
{
    public const string BonusCode = "set_item_bonus_overrides";
    public const string CatalogCode = "set_item_catalog_customizations";
    const int FirstCustomId = 20000;
    const int RemovedValue = int.MinValue;

    // 源端 SetItemManager.SUPPORTED_STAT_KEYS + 282 Effect/Option 新增的键。
    // 282 的 nbdR→IgnoreDefense、incCr→CriticalChance、Option 的 incAsrR→StatusRes
    // 等在旧 55 套 catalog 里不存在，但不加进这张表后台就无法编辑/校验。
    static readonly string[] SupportedStats =
    {
        "STR", "DEX", "INT", "LUK", "PAD", "MAD", "PDD", "MDD", "ACC", "EVA",
        "SPD", "JMP", "HP", "MP", "FinalDamage", "BossDamage", "NormalDamage", "Damage",
        "ExpRate", "DropRate", "MesoRate",
        "STRPct", "DEXPct", "INTPct", "LUKPct", "PADPct", "MADPct",
        "PDDPct", "MDDPct", "ACCPct", "EVAPct", "HPPct", "MPPct",
        // 282 增量
        "IgnoreDefense", "CriticalChance", "CriticalDamage", "StatusRes", "PvpDamage",
    };

    sealed class TierOption
    {
        public string Text;                 // 简体、占位符已替换
        public Dictionary<string, int> Stats = new Dictionary<string, int>();
    }
    sealed class TierSkill { public int Id, Level; public string Name; }

    sealed class Tier
    {
        public int Required;
        // Effect/<n>/inc* 的直接属性（后台可改，写入 bonusStore 差量）
        public Dictionary<string, int> Stats = new Dictionary<string, int>();
        // Effect/<n>/Option 展开后的属性（只结算，不进后台差量）
        public Dictionary<string, int> OptionStats = new Dictionary<string, int>();
        public readonly List<TierOption> Options = new List<TierOption>();
        public readonly List<TierSkill> Skills = new List<TierSkill>();
        public readonly Dictionary<string, int> Unmapped = new Dictionary<string, int>();
    }
    sealed class SlotMeta { public string Part, Represent; }
    sealed class Definition
    {
        public int Id, JobIndex;
        public string Name;
        public int CompleteCount;          // 282 completeCount；<=0 时回退 Slots.Count
        public List<List<int>> Slots = new List<List<int>>();
        public readonly List<SlotMeta> Metas = new List<SlotMeta>();
        public readonly Dictionary<string, string> Desc = new Dictionary<string, string>();
        public HashSet<int> ItemIds;        // 282 结算用成员表（null = 旧 catalog 口径）
        public int JokerPossible;
        public List<Tier> Tiers = new List<Tier>();
        public bool BuiltIn;
    }

    public static void Register(AdminApi api)
    {
        api.Get("/setItem/v1/catalog", ctx => Catalog());
        api.Put("/setItem/v1/{definitionId}", Update);
        api.Delete("/setItem/v1/{definitionId}", Reset);
        api.Put("/setItem/v1/series/{definitionId}", UpdateSeries);
        api.Get("/setItem/v1/equipment/search", SearchEquipment);
        api.Post("/setItem/v1/custom", Create);
        api.Put("/setItem/v1/{definitionId}/enabled", SetEnabled);
        api.Delete("/setItem/v1/custom/{definitionId}", DeleteCustom);
    }

    // ——————————— 定义装载 ———————————
    static List<Definition> builtins;
    static List<Definition> customDefs;                     // game_config customDefinitions
    static HashSet<int> disabledIds;                        // game_config disabledBuiltInIds
    static Dictionary<int, List<List<int>>> slotOverrides;  // game_config slotOverrides
    // 加成差量，key = "套装id:档位件数"（源端 SetItemBonusOverrides.key），REMOVED_VALUE 表示删除
    static readonly Dictionary<string, Dictionary<string, int>> bonusStore = new Dictionary<string, Dictionary<string, int>>();

    // 282 迁入的旁表：装备名（GMS083 String 树缺的 282 专属道具）、技能名、反向索引。
    static readonly Dictionary<int, string> equipNames282 = new Dictionary<int, string>();
    static readonly Dictionary<int, string> skillNames282 = new Dictionary<int, string>();
    static readonly Dictionary<int, string> skillDescs282 = new Dictionary<int, string>();
    static readonly Dictionary<int, List<Definition>> defsByItem = new Dictionary<int, List<Definition>>();
    static bool using282Data;

    public static bool Using282Data { get { EnsureLoaded(); return using282Data; } }
    public static int BuiltInSetCount { get { EnsureLoaded(); return builtins.Count; } }
    public static string EquipName282(int id)
    {
        EnsureLoaded();
        return equipNames282.TryGetValue(id, out var n) ? n : "";
    }
    public static string SkillName282(int id)
    {
        EnsureLoaded();
        return skillNames282.TryGetValue(id, out var n) ? n : ("技能 " + id);
    }

    static void EnsureLoaded()
    {
        if (builtins != null) return;
        builtins = new List<Definition>();
        equipNames282.Clear(); skillNames282.Clear(); skillDescs282.Clear(); defsByItem.Clear();

        string json = PlatformRes.LoadText("Admin/SetItem282");
        var root = AdminJson.AsDict(AdminJson.Parse(json));
        if (root != null) Load282(root);
        // 资源缺失/坏 JSON 时退回旧的 55 套 beidou catalog，保证后台与玩法不至于空转。
        if (builtins.Count == 0) LoadLegacyCatalog();
        using282Data = builtins.Count > 0 && root != null && builtins.Count > 55;

        RebuildItemIndex();
        ReloadConfig();
    }

    // ——————————— 282 SetItemInfo.img 导出的 JSON ———————————
    static void Load282(Dictionary<string, object> root)
    {
        foreach (var kv in AdminJson.AsDict(AdminJson.Get(root, "equipNames")) ?? new Dictionary<string, object>())
            if (int.TryParse(kv.Key, out int id)) equipNames282[id] = kv.Value as string ?? "";
        foreach (var kv in AdminJson.AsDict(AdminJson.Get(root, "skills")) ?? new Dictionary<string, object>())
        {
            if (!int.TryParse(kv.Key, out int id)) continue;
            var s = AdminJson.AsDict(kv.Value);
            if (s == null) continue;
            skillNames282[id] = AdminJson.GetString(s, "name");
            skillDescs282[id] = AdminJson.GetString(s, "desc");
        }

        foreach (var row in AdminJson.AsList(AdminJson.Get(root, "sets")) ?? new List<object>())
        {
            var d = AdminJson.AsDict(row);
            if (d == null) continue;
            var def = new Definition
            {
                Id = AdminJson.GetInt(d, "id"),
                // 282 数据没有 jobIndex：套装本身按道具限定职业，不做额外门控。
                JobIndex = -1,
                Name = AdminJson.GetString(d, "name"),
                CompleteCount = AdminJson.GetInt(d, "completeCount"),
                JokerPossible = AdminJson.GetInt(d, "jokerPossible"),
                BuiltIn = true,
                ItemIds = new HashSet<int>(),
            };
            foreach (var slotObj in AdminJson.AsList(AdminJson.Get(d, "slots")) ?? new List<object>())
            {
                var slot = AdminJson.AsDict(slotObj);
                if (slot == null) continue;
                var items = new List<int>();
                foreach (var idObj in AdminJson.AsList(AdminJson.Get(slot, "items")) ?? new List<object>())
                {
                    int itemId = Convert.ToInt32(idObj);
                    if (itemId > 0) { items.Add(itemId); def.ItemIds.Add(itemId); }
                }
                def.Slots.Add(items);
                def.Metas.Add(new SlotMeta
                {
                    Part = AdminJson.GetString(slot, "part"),
                    Represent = AdminJson.GetString(slot, "represent"),
                });
            }
            foreach (var kv in AdminJson.AsDict(AdminJson.Get(d, "desc")) ?? new Dictionary<string, object>())
                def.Desc[kv.Key] = kv.Value as string ?? "";

            foreach (var tObj in AdminJson.AsList(AdminJson.Get(d, "tiers")) ?? new List<object>())
            {
                var td = AdminJson.AsDict(tObj);
                if (td == null) continue;
                var tier = new Tier { Required = AdminJson.GetInt(td, "required") };
                foreach (var kv in AdminJson.AsDict(AdminJson.Get(td, "stats")) ?? new Dictionary<string, object>())
                    tier.Stats[kv.Key] = Convert.ToInt32(kv.Value);
                foreach (var kv in AdminJson.AsDict(AdminJson.Get(td, "unmapped")) ?? new Dictionary<string, object>())
                    tier.Unmapped[kv.Key] = Convert.ToInt32(kv.Value);
                foreach (var oObj in AdminJson.AsList(AdminJson.Get(td, "options")) ?? new List<object>())
                {
                    var od = AdminJson.AsDict(oObj);
                    if (od == null) continue;
                    var option = new TierOption { Text = AdminJson.GetString(od, "text") };
                    foreach (var kv in AdminJson.AsDict(AdminJson.Get(od, "stats")) ?? new Dictionary<string, object>())
                    {
                        int v = Convert.ToInt32(kv.Value);
                        option.Stats[kv.Key] = v;
                        tier.OptionStats[kv.Key] = tier.OptionStats.TryGetValue(kv.Key, out int cur) ? cur + v : v;
                    }
                    tier.Options.Add(option);
                }
                foreach (var sObj in AdminJson.AsList(AdminJson.Get(td, "activeSkills")) ?? new List<object>())
                {
                    var sd = AdminJson.AsDict(sObj);
                    if (sd == null) continue;
                    int skillId = AdminJson.GetInt(sd, "id");
                    if (skillId <= 0) continue;
                    tier.Skills.Add(new TierSkill
                    {
                        Id = skillId,
                        Level = Math.Max(1, AdminJson.GetInt(sd, "level", 1)),
                        Name = skillNames282.TryGetValue(skillId, out var n) ? n : ("技能 " + skillId),
                    });
                }
                def.Tiers.Add(tier);
            }
            def.Tiers.Sort((a, b) => a.Required.CompareTo(b.Required));
            builtins.Add(def);
        }
    }

    // ——————————— 回退：旧 beidou 55 套 ———————————
    static void LoadLegacyCatalog()
    {
        string json = PlatformRes.LoadText("Admin/SetItemCatalog");
        foreach (var row in AdminJson.AsList(AdminJson.Parse(json)) ?? new List<object>())
        {
            var d = AdminJson.AsDict(row);
            var def = new Definition
            {
                Id = AdminJson.GetInt(d, "id"),
                JobIndex = AdminJson.GetInt(d, "jobIndex", -1),
                Name = AdminJson.GetString(d, "name"),
                BuiltIn = true,
            };
            foreach (var slot in AdminJson.AsList(d["slots"]) ?? new List<object>())
            {
                var items = new List<int>();
                foreach (var id in AdminJson.AsList(slot) ?? new List<object>()) items.Add(Convert.ToInt32(id));
                def.Slots.Add(items);
                def.Metas.Add(new SlotMeta());
            }
            foreach (var t in AdminJson.AsList(d["tiers"]) ?? new List<object>())
            {
                var td = AdminJson.AsDict(t);
                var tier = new Tier { Required = AdminJson.GetInt(td, "requiredCount") };
                foreach (var kv in AdminJson.AsDict(td["stats"]) ?? new Dictionary<string, object>())
                    tier.Stats[kv.Key] = Convert.ToInt32(kv.Value);
                def.Tiers.Add(tier);
            }
            def.Tiers.Sort((a, b) => a.Required.CompareTo(b.Required));
            builtins.Add(def);
        }
    }

    static void RebuildItemIndex()
    {
        defsByItem.Clear();
        foreach (var def in builtins)
        {
            foreach (var slot in def.Slots)
                foreach (int id in slot)
                {
                    if (!defsByItem.TryGetValue(id, out var list))
                        defsByItem[id] = list = new List<Definition>();
                    if (!list.Contains(def)) list.Add(def);
                }
        }
    }

    // 从 game_config 读覆盖层（reload 语义，源端 reload()）
    static void ReloadConfig()
    {
        customDefs = new List<Definition>();
        disabledIds = new HashSet<int>();
        slotOverrides = new Dictionary<int, List<List<int>>>();

        try
        {
            var db = AdminBackend.Db;
            if (db == null) return;
            var catalog = AdminJson.AsDict(AdminJson.Parse(GameConfigDao.Get(db, CatalogCode, "")));
            if (catalog != null)
            {
                foreach (var row in AdminJson.GetList(catalog, "customDefinitions"))
                    customDefs.Add(ParseStored(AdminJson.AsDict(row)));
                foreach (var id in AdminJson.GetList(catalog, "disabledBuiltInIds"))
                    disabledIds.Add(Convert.ToInt32(id));
                var slots = AdminJson.GetDict(catalog, "slotOverrides");
                if (slots != null)
                    foreach (var kv in slots)
                    {
                        var list = new List<List<int>>();
                        foreach (var slot in AdminJson.AsList(kv.Value))
                        {
                            var items = new List<int>();
                            foreach (var id in AdminJson.AsList(slot)) items.Add(Convert.ToInt32(id));
                            list.Add(items);
                        }
                        slotOverrides[Convert.ToInt32(kv.Key)] = list;
                    }
            }
            var bonuses = AdminJson.AsDict(AdminJson.Parse(GameConfigDao.Get(db, BonusCode, "")));
            bonusStore.Clear();
            if (bonuses != null)
                foreach (var kv in bonuses)
                {
                    var stats = new Dictionary<string, int>();
                    foreach (var s in AdminJson.AsDict(kv.Value))
                        stats[s.Key] = Convert.ToInt32(s.Value);
                    bonusStore[kv.Key] = stats;
                }
        }
        catch { /* 无库/坏 JSON 时用默认（源端 parse 失败也是回默认） */ }
    }

    static Definition ParseStored(Dictionary<string, object> d)
    {
        var def = new Definition
        {
            Id = AdminJson.GetInt(d, "id"),
            JobIndex = AdminJson.GetInt(d, "jobIndex", -1),
            Name = AdminJson.GetString(d, "name"),
            BuiltIn = false,
        };
        foreach (var slot in AdminJson.GetList(d, "slots"))
        {
            var items = new List<int>();
            foreach (var id in AdminJson.AsList(slot)) items.Add(Convert.ToInt32(id));
            def.Slots.Add(items);
        }
        var tiers = AdminJson.GetDict(d, "tiers");
        if (tiers != null)
            foreach (var kv in tiers)
            {
                var tier = new Tier { Required = Convert.ToInt32(kv.Key) };
                foreach (var s in AdminJson.AsDict(kv.Value)) tier.Stats[s.Key] = Convert.ToInt32(s.Value);
                def.Tiers.Add(tier);
            }
        def.Tiers.Sort((a, b) => a.Required.CompareTo(b.Required));
        return def;
    }

    static List<Definition> AllDefs()
    {
        var all = new List<Definition>(builtins);
        all.AddRange(customDefs);
        all.Sort((a, b) => a.Id.CompareTo(b.Id));
        return all;
    }

    static Definition Require(int id)
    {
        foreach (var d in builtins) if (d.Id == id) return d;
        foreach (var d in customDefs) if (d.Id == id) return d;
        throw new AdminApiException("套装不存在: " + id);
    }

    // ——————————— catalog ———————————
    static object Catalog()
    {
        EnsureLoaded();
        var result = new List<object>();
        foreach (var def in AllDefs())
        {
            var defaultDef = def.BuiltIn ? builtins.FirstOrDefault(b => b.Id == def.Id) : null;
            var slots = slotOverrides.TryGetValue(def.Id, out var ov) ? ov : def.Slots;

            var slotDtos = new List<object>();
            foreach (var slot in slots)
            {
                var items = new List<object>();
                foreach (var itemId in slot)
                    items.Add(AdminJson.Obj("id", (long)itemId, "name", AdminNames.ItemName(itemId)));
                slotDtos.Add(items);
            }
            var defaultSlotDtos = new List<object>();
            var defaultSlots = defaultDef != null ? defaultDef.Slots : def.Slots;
            foreach (var slot in defaultSlots)
            {
                var items = new List<object>();
                foreach (var itemId in slot)
                    items.Add(AdminJson.Obj("id", (long)itemId, "name", AdminNames.ItemName(itemId)));
                defaultSlotDtos.Add(items);
            }

            var tierDtos = new List<object>();
            for (int i = 0; i < def.Tiers.Count; i++)
            {
                var tier = def.Tiers[i];
                var effective = new Dictionary<string, int>(tier.Stats);
                string key = def.Id + ":" + tier.Required;
                if (bonusStore.TryGetValue(key, out var diff))
                {
                    foreach (var kv in diff)
                    {
                        if (kv.Value == RemovedValue) effective.Remove(kv.Key);
                        else effective[kv.Key] = kv.Value;
                    }
                }
                var defaultStats = defaultDef != null && i < defaultDef.Tiers.Count
                    ? defaultDef.Tiers[i].Stats : tier.Stats;
                var effDto = new List<object>();
                foreach (var kv in effective) { effDto.Add(kv.Key); effDto.Add((long)kv.Value); }
                var defDto = new List<object>();
                foreach (var kv in defaultStats) { defDto.Add(kv.Key); defDto.Add((long)kv.Value); }
                tierDtos.Add(AdminJson.Obj(
                    "requiredCount", (long)tier.Required,
                    "stats", FlatObj(effDto),
                    "defaultStats", FlatObj(defDto),
                    "customized", bonusStore.ContainsKey(key)));
            }

            result.Add(AdminJson.Obj(
                "id", (long)def.Id,
                "jobIndex", (long)def.JobIndex,
                "name", def.Name,
                "completeCount", (long)(def.CompleteCount > 0 ? def.CompleteCount : slots.Count),
                "builtIn", def.BuiltIn,
                "enabled", def.BuiltIn ? !disabledIds.Contains(def.Id) : true,
                "slots", slotDtos,
                "defaultSlots", defaultSlotDtos,
                "slotsCustomized", slotOverrides.ContainsKey(def.Id),
                "tiers", tierDtos));
        }
        return result;
    }

    static Dictionary<string, object> FlatObj(List<object> flat)
    {
        var d = new Dictionary<string, object>();
        for (int i = 0; i + 1 < flat.Count; i += 2) d[(string)flat[i]] = flat[i + 1];
        return d;
    }

    // ——————————— 更新（源端 update + prepareUpdate） ———————————
    static object Update(AdminCtx ctx)
    {
        int id = (int)ctx.ParamLong("definitionId");
        var body = ctx.Body;
        return AdminMainThread.Run(() => { PrepareUpdate(Require(id), AdminJson.AsDict(body), allowSlots: true); Persist(); return (long)1; });
    }

    // 源端 updateSeries：同一名字的内置套装一起改（系列），只支持加成不支持槽位
    static object UpdateSeries(AdminCtx ctx)
    {
        int id = (int)ctx.ParamLong("definitionId");
        var body = ctx.Body;
        if (AdminJson.Get(body, "slots") != null)
            throw new AdminApiException("系列统一修改仅支持加成，槽位请进入套装详情修改");
        return AdminMainThread.Run(() =>
        {
            var rep = Require(id);
            foreach (var d in builtins.ToList())
                if (d.Id == id || (rep.BuiltIn && d.BuiltIn && d.Name == rep.Name))
                    PrepareUpdate(d, AdminJson.AsDict(body), allowSlots: false);
            foreach (var d in customDefs.ToList())
                if (d.Id == id || (rep.BuiltIn && d.BuiltIn && d.Name == rep.Name))
                    PrepareUpdate(d, AdminJson.AsDict(body), allowSlots: false);
            Persist();
            return (long)1;
        });
    }

    static void PrepareUpdate(Definition def, Dictionary<string, object> body, bool allowSlots)
    {
        var tiersBody = AdminJson.GetDict(body, "tiers");
        if (tiersBody == null) throw new AdminApiException("套装档位配置不能为空");

        // 档位 key 必须与默认档位完全一致（多、少、错都算不完整）
        var required = new HashSet<string>(def.Tiers.Select(t => t.Required.ToString()));
        if (new HashSet<string>(tiersBody.Keys).SetEquals(required) == false)
            throw new AdminApiException("套装档位不完整或包含未知档位");

        if (allowSlots && AdminJson.Get(body, "slots") != null)
        {
            var validated = ValidateSlots(AdminJson.GetList(body, "slots"));
            if (SameSlots(validated, def.Slots)) slotOverrides.Remove(def.Id);
            else slotOverrides[def.Id] = validated;
        }

        // 先清掉本套装旧差量，再按新值写入
        string prefix = def.Id + ":";
        foreach (var key in bonusStore.Keys.ToList()) if (key.StartsWith(prefix, StringComparison.Ordinal)) bonusStore.Remove(key);

        foreach (var tier in def.Tiers)
        {
            var values = AdminJson.AsDict(tiersBody[tier.Required.ToString()]);
            if (values == null) throw new AdminApiException("套装档位属性不能为空");
            if (values.Count > 24) throw new AdminApiException("每档最多支持 24 项属性");

            var diffs = new Dictionary<string, int>();
            foreach (var kv in values)
            {
                if (!SupportedStats.Contains(kv.Key)) throw new AdminApiException("不支持的套装属性: " + kv.Key);
                int value = Convert.ToInt32(kv.Value);
                ValidateValue(kv.Key, value);
                if (!tier.Stats.TryGetValue(kv.Key, out int dflt) || dflt != value)
                    diffs[kv.Key] = value;
            }
            // 默认有、请求里没提 → 视为删除
            foreach (var dflt in tier.Stats)
                if (!values.ContainsKey(dflt.Key)) diffs[dflt.Key] = RemovedValue;

            if (diffs.Count > 0) bonusStore[def.Id + ":" + tier.Required] = diffs;
        }
    }

    static List<List<int>> ValidateSlots(List<object> raw)
    {
        if (raw == null || raw.Count == 0 || raw.Count > 20) throw new AdminApiException("槽位数量须在 1 到 20 之间");
        var seen = new HashSet<int>();
        var result = new List<List<int>>();
        foreach (var slotObj in raw)
        {
            var items = new List<int>();
            foreach (var idObj in AdminJson.AsList(slotObj))
            {
                int id = Convert.ToInt32(idObj);
                if (id <= 0 || !seen.Add(id)) throw new AdminApiException("槽位道具无效或重复: " + id);
                items.Add(id);
            }
            if (items.Count == 0 || items.Count > 50) throw new AdminApiException("每槽道具数量须在 1 到 50 之间");
            result.Add(items);
        }
        return result;
    }

    static bool SameSlots(List<List<int>> a, List<List<int>> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Count != b[i].Count) return false;
            for (int k = 0; k < a[i].Count; k++) if (a[i][k] != b[i][k]) return false;
        }
        return true;
    }

    static void ValidateValue(string stat, int value)
    {
        int max = IsRate(stat) ? 10000 : 1000000;
        if (value < 0 || value > max) throw new AdminApiException(stat + " 必须在 0 到 " + max + " 之间");
    }

    static bool IsRate(string stat)
    {
        string s = stat.ToLowerInvariant();
        return s.EndsWith("rate") || s.EndsWith("pct") || s.EndsWith("damage")
            || s.EndsWith("chance") || s == "ignoredefense"
            || s == "statusres" || s == "buffduration";
    }

    // ——————————— 重置 ———————————
    static object Reset(AdminCtx ctx)
    {
        int id = (int)ctx.ParamLong("definitionId");
        return AdminMainThread.Run(() =>
        {
            Require(id);
            RemoveDefConfig(id);
            Persist();
            return (long)1;
        });
    }

    static void RemoveDefConfig(int id)
    {
        string prefix = id + ":";
        foreach (var key in bonusStore.Keys.ToList()) if (key.StartsWith(prefix, StringComparison.Ordinal)) bonusStore.Remove(key);
        slotOverrides.Remove(id);
    }

    // ——————————— 内置启停 ———————————
    static object SetEnabled(AdminCtx ctx)
    {
        int id = (int)ctx.ParamLong("definitionId");
        bool enabled = AdminJson.GetBool(ctx.Body, "enabled", true);
        return AdminMainThread.Run(() =>
        {
            var def = Require(id);
            if (!def.BuiltIn) throw new AdminApiException("只有内置套装可以停用或恢复");
            if (enabled) disabledIds.Remove(id); else disabledIds.Add(id);
            Persist();
            return (long)1;
        });
    }

    // ——————————— 自定义套装 ———————————
    static object Create(AdminCtx ctx)
    {
        var body = ctx.Body;
        return AdminMainThread.Run(() =>
        {
            EnsureLoaded();
            string name = AdminJson.GetString(body, "name");
            if (string.IsNullOrEmpty(name) || name.Length > 64) throw new AdminApiException("套装名称须为 1 到 64 个字符");

            int id = FirstCustomId;
            foreach (var d in customDefs) id = Math.Max(id, d.Id + 1);

            var def = new Definition { Id = id, JobIndex = AdminJson.GetInt(body, "jobIndex", -1), Name = name, BuiltIn = false };
            var slotsRaw = AdminJson.GetList(body, "slots");
            if (slotsRaw == null || slotsRaw.Count == 0) throw new AdminApiException("套装槽位不能为空");
            def.Slots = ValidateSlots(slotsRaw);
            var tiersBody = AdminJson.GetDict(body, "tiers");
            if (tiersBody == null || tiersBody.Count == 0) throw new AdminApiException("套装档位不能为空");
            foreach (var kv in tiersBody)
            {
                if (!int.TryParse(kv.Key, out int req) || req <= 0 || req > def.Slots.Count)
                    throw new AdminApiException("档位件数无效: " + kv.Key);
                var tier = new Tier { Required = req };
                var stats = AdminJson.AsDict(kv.Value);
                if (stats == null || stats.Count > 24) throw new AdminApiException("每档最多支持 24 项属性");
                foreach (var s in stats)
                {
                    if (!SupportedStats.Contains(s.Key)) throw new AdminApiException("不支持的套装属性: " + s.Key);
                    int value = Convert.ToInt32(s.Value);
                    ValidateValue(s.Key, value);
                    tier.Stats[s.Key] = value;
                }
                def.Tiers.Add(tier);
            }
            def.Tiers.Sort((a, b) => a.Required.CompareTo(b.Required));
            if (customDefs.Any(d => d.Id == id)) throw new AdminApiException("自定义套装ID无效或重复");
            customDefs.Add(def);
            Persist();
            return (long)id;
        });
    }

    static object DeleteCustom(AdminCtx ctx)
    {
        int id = (int)ctx.ParamLong("definitionId");
        return AdminMainThread.Run(() =>
        {
            var def = customDefs.FirstOrDefault(d => d.Id == id);
            if (def == null) throw new AdminApiException("自定义套装不存在: " + id);
            customDefs.Remove(def);
            RemoveDefConfig(id);
            Persist();
            return (long)1;
        });
    }

    // ——————————— 装备搜索（复用装备图鉴的关键词排序） ———————————
    static object SearchEquipment(AdminCtx ctx)
    {
        string keyword = ctx.Query("keyword", "");
        var page = AdminCatalogData.EquipmentCatalog(keyword, "", 1, 20, null, "", null, null, null);
        var records = page.Records;
        var result = new List<object>();
        if (records != null)
            foreach (var r in records)
            {
                var d = AdminJson.AsDict(r);
                result.Add(AdminJson.Obj("id", AdminJson.GetLong(d, "id"), "name", AdminJson.GetString(d, "name")));
            }
        return result;
    }

    // ——————————— 持久化 ———————————
    static void Persist()
    {
        Invalidate();   // 玩法侧（EquipLoadout/CombatRules）下一次结算重读覆盖层
        var db = AdminBackend.RequireDb();
        var bonuses = new Dictionary<string, object>();
        foreach (var kv in bonusStore)
        {
            var stats = new Dictionary<string, object>();
            foreach (var s in kv.Value) stats[s.Key] = (long)s.Value;
            bonuses[kv.Key] = stats;
        }
        var customs = new List<object>();
        foreach (var d in customDefs)
        {
            var slots = new List<object>();
            foreach (var slot in d.Slots) slots.Add(slot.Select(i => (long)i).ToList());
            var tiers = new Dictionary<string, object>();
            foreach (var t in d.Tiers)
            {
                var stats = new Dictionary<string, object>();
                foreach (var s in t.Stats) stats[s.Key] = (long)s.Value;
                tiers[t.Required.ToString()] = stats;
            }
            customs.Add(AdminJson.Obj(
                "id", (long)d.Id, "name", d.Name, "jobIndex", (long)d.JobIndex,
                "slots", slots, "tiers", tiers));
        }
        var slotsOv = new Dictionary<string, object>();
        foreach (var kv in slotOverrides)
            slotsOv[kv.Key.ToString()] = kv.Value.Select(slot => slot.Select(i => (long)i).ToList()).ToList();

        Upsert(db, BonusCode, AdminJson.Write(bonuses));
        Upsert(db, CatalogCode, AdminJson.Write(AdminJson.Obj(
            "customDefinitions", customs,
            "disabledBuiltInIds", disabledIds.Select(i => (long)i).ToList(),
            "slotOverrides", slotsOv)));
    }

    static void Upsert(DbStore db, string code, string value)
    {
        var table = db.Table("game_config");
        var row = table.Find(r => DbStore.Str(r, "config_code") == code);
        string now = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        if (row != null)
            table.Update(r => ReferenceEquals(r, row), new DbRow { ["config_value"] = value, ["update_time"] = now });
        else
            table.Insert(new DbRow
            {
                ["config_type"] = "server",
                ["config_sub_type"] = "Set Items",
                ["config_clazz"] = "java.lang.String",
                ["config_code"] = code,
                ["config_value"] = value,
                ["config_desc"] = code,
                ["update_time"] = now,
            });
    }

    // ——————————— 玩法侧运行时（≡ 源端 SetItemManager.compute/Bonus/applyDamage） ———————————
    // EquipLoadout（面板属性）、CombatRules（伤害%）、FieldDirector（经验/掉落/金币倍率）从这里读。
    // 源端角色身上是 SetItemManager.Bonus，我们用 Dictionary<string,int> 等价承载。

    // 源端 getJobIndex：Job.getJobStyleInternal(niche 1..5)-1。GMS083 主职业系按 id 百位映射。
    public static int JobIndexOf(int jobId)
    {
        if (jobId < 100) return -1;                                  // 新手/特殊
        int baseJob = jobId >= 1000 ? (jobId / 100) % 10 : jobId / 100; // 1100 骑士团等取百位
        switch (baseJob)
        {
            case 1: return 0;   // 战士(100)/魂骑士(1100)
            case 2: return 2;   // 法师(200)/炎术士(1200)
            case 3: return 1;   // 弓手(300)/风灵使者(1300)
            case 4: return 3;   // 飞侠(400)/夜行者(1400)
            case 5: return 4;   // 海盗(500)/奇袭者(1500)
            default: return -1;
        }
    }

    // 计件口径（282）：客户端每套装备信息里的 setItemID 指向本套装，穿着件数按「穿了几件
    // 属于本套装的道具」计，重复戒指/同类武器各算一件 —— 这正是 282 里 completeCount=5
    // 但 ItemID 只有一个戒指 id 的套装能达成 5 档的原因。旧 catalog 与后台 slotOverrides
    // 仍走「逐槽组一组只计一件」的原口径。
    // 返回 (定义, 达成件数)，ComputeBonus 与 ActiveSkillsFor 共用，避免两套计数漂移。
    static IEnumerable<KeyValuePair<Definition, int>> AchievedDefs(IEnumerable<int> wornIds, int jobIndex)
    {
        EnsureLoaded();
        var worn = new List<int>();
        if (wornIds != null) foreach (int id in wornIds) if (id > 0) worn.Add(id);
        var equipped = new HashSet<int>(worn);
        foreach (var def in AllDefs())
        {
            if (def.JobIndex >= 0 && def.JobIndex != jobIndex) continue;
            if (def.BuiltIn && disabledIds.Contains(def.Id)) continue;
            bool overridden = slotOverrides.ContainsKey(def.Id);
            var slots = overridden ? slotOverrides[def.Id] : def.Slots;
            int count;
            if (def.ItemIds != null && !overridden)
            {
                count = 0;
                foreach (int id in worn) if (def.ItemIds.Contains(id)) count++;
            }
            else
            {
                count = 0;
                foreach (var slot in slots)
                    foreach (int itemId in slot)
                        if (equipped.Contains(itemId)) { count++; break; }
            }
            if (count > 0) yield return new KeyValuePair<Definition, int>(def, count);
        }
    }

    // ≡ SetItemManager.compute：档位**累加**（count >= requiredCount 的每一档都算），
    // 另把 282 Effect/<档>/Option 展开的属性并入。返回的 map 同时供面板、伤害与倍率读取。
    public static Dictionary<string, int> ComputeBonus(IEnumerable<int> wornIds, int jobIndex)
    {
        var bonus = new Dictionary<string, int>();
        void Add(Dictionary<string, int> stats)
        {
            foreach (var kv in stats)
                bonus[kv.Key] = bonus.TryGetValue(kv.Key, out int cur)
                    ? (int)Math.Min(int.MaxValue, (long)cur + kv.Value) : kv.Value;
        }
        try
        {
            foreach (var achieved in AchievedDefs(wornIds, jobIndex))
            {
                var def = achieved.Key;
                foreach (var tier in def.Tiers)
                {
                    if (achieved.Value < tier.Required) continue;
                    var stats = new Dictionary<string, int>(tier.Stats);
                    // 差量覆盖（SetItemBonusOverrides.withOverrides；RemovedValue=删除该属性）
                    if (bonusStore.TryGetValue(def.Id + ":" + tier.Required, out var diff))
                        foreach (var kv in diff)
                        {
                            if (kv.Value == RemovedValue) stats.Remove(kv.Key);
                            else stats[kv.Key] = kv.Value;
                        }
                    Add(stats);
                    // Option 是 282 复用 ItemOption 的通用属性键，后台不提供差量编辑。
                    Add(tier.OptionStats);
                }
            }
        }
        catch { /* 无库/WZ 缺文件时无加成（源端 compute 异常同样回 NONE） */ }
        return bonus;
    }

    // ——————————— 282 activeSkill 授予接口 ———————————
    public sealed class GrantedSkill
    {
        public int Id, Level;
        public string Name, Desc;
    }

    /// <summary>
    /// 当前穿着下所有已达成套装档位授予的 8000xxxx 技能。同 id 取最高等级。
    /// 边界：EXE 的「装备变更 → 档位重算 → 技能增删」触发链未逐指令定位，
    /// 本工程在 EquipLoadout.CurrentSetBonus() 里按当前穿着重算并写 NativeSetItemSkills，
    /// 卸下装备后下一次重算即自动移除（不写存档）。
    /// </summary>
    public static List<GrantedSkill> ActiveSkillsFor(IEnumerable<int> wornIds, int jobIndex)
    {
        var result = new List<GrantedSkill>();
        try
        {
            var byId = new Dictionary<int, GrantedSkill>();
            foreach (var achieved in AchievedDefs(wornIds, jobIndex))
                foreach (var tier in achieved.Key.Tiers)
                {
                    if (achieved.Value < tier.Required) continue;
                    foreach (var skill in tier.Skills)
                    {
                        if (!byId.TryGetValue(skill.Id, out var g))
                        {
                            g = new GrantedSkill
                            {
                                Id = skill.Id, Level = skill.Level, Name = skill.Name,
                                Desc = skillDescs282.TryGetValue(skill.Id, out var d) ? d : "",
                            };
                            byId[skill.Id] = g;
                        }
                        else if (skill.Level > g.Level) g.Level = skill.Level;
                    }
                }
            result.AddRange(byId.Values);
            result.Sort((a, b) => a.Id.CompareTo(b.Id));
        }
        catch { /* 同 ComputeBonus：缺资源时没有授予技能 */ }
        return result;
    }

    // ≡ SetItemManager.Bonus.apply(key, base)：flat 先加，再按 (key+"Pct")% 乘
    public static int ApplyStat(Dictionary<string, int> bonus, string key, int baseValue)
    {
        bonus.TryGetValue(key, out int flat);
        bonus.TryGetValue(key + "Pct", out int pct);
        long v = Math.Max(0L, (long)baseValue + flat);
        return (int)Math.Min(int.MaxValue, v + v * pct / 100L);
    }

    // ≡ SetItemManager.applyDamage（MapleMap 收到攻击后调用）：
    // percent = Damage% + (Boss? BossDamage% : NormalDamage%)，再叠 FinalDamage%
    // 伤害是 long：不再按 int.MaxValue 截断（单段伤害允许超过 21 亿）。
    public static long ApplyDamage(Dictionary<string, int> bonus, bool boss, long damage, double additionalPercent = 0)
    {
        if (damage <= 0 || damage == long.MaxValue) return damage;
        bonus.TryGetValue("Damage", out int percent);
        bonus.TryGetValue(boss ? "BossDamage" : "NormalDamage", out int extra);
        percent += extra;
        bonus.TryGetValue("FinalDamage", out int finalPct);
        double scaled = Math.Round(damage * (100.0 + percent + additionalPercent) / 100.0 * (100.0 + finalPct) / 100.0);
        if (scaled >= long.MaxValue) return long.MaxValue;
        return Math.Max(1L, (long)scaled);
    }

    // 倍率类（getExpRate/getDropRate/getMesoRate 的套装部分）：rate × (100 + X) / 100
    public static double ApplyRate(Dictionary<string, int> bonus, string key, double rate)
    {
        bonus.TryGetValue(key, out int v);
        return rate * (100 + v) / 100.0;
    }

    // ——————————— 装备提示的套装面板 ———————————
    // 2026-11-06 起按 282 原生 frame/set 面板供数（298 宽、itemPartPos/itemNamePos 两列、
    // 状态文案装备中 / 未装备 / (无)、件数 "%d / %d"）。完整推导见 inventory-and-tooltip.md。
    // 数据来自 282 SetItemInfo.img，与玩法结算同源，后台改动两边同步。
    public sealed class SetPanelAlt
    {
        public int ItemId;
        public string Name, SlotName;
        public bool Equipped;
    }

    public sealed class SetPanelSlot
    {
        public string Part, Represent;
        public readonly List<SetPanelAlt> Alts = new List<SetPanelAlt>();
        public bool AnyWorn { get { foreach (var a in Alts) if (a.Equipped) return true; return false; } }
    }

    public sealed class SetPanelTier
    {
        public int Required;
        public readonly List<KeyValuePair<string, int>> Stats = new List<KeyValuePair<string, int>>();
        public readonly List<string> Options = new List<string>();   // ItemOption 文案（简体）
        public readonly List<string> Skills = new List<string>();    // activeSkill 授予技能
        public readonly List<string> Unmapped = new List<string>();  // 无机制对应，标注「未接入」
    }

    public sealed class SetPanel
    {
        public int Id;
        public string Name;
        public int CompleteCount;   // 282 completeCount
        public int EquippedCount;
        public int JokerPossible;
        public readonly List<SetPanelSlot> Slots = new List<SetPanelSlot>();
        public readonly List<SetPanelTier> Tiers = new List<SetPanelTier>();
    }

    // Desc/<key> 是「选择一件…」这类替代文案，键是英文部件名。
    static readonly Dictionary<string, string> DescKeyOfPart = new Dictionary<string, string>
    {
        { "武器", "weapon" }, { "副武器", "subweapon" }, { "口袋", "pocket" },
        { "纹章", "emblem" }, { "帽子", "cap" },
    };

    static string UnmappedLabel(string key)
    {
        switch (key)
        {
            case "incPQEXPr": return "组队任务经验获得";
            default: return key;
        }
    }

    // 源端 PacketCreator.setItemSlotName
    public static string SlotNameOf(int itemId)
    {
        switch (itemId / 10000)
        {
            case 100: return "帽子";
            case 101: return "脸饰";
            case 102: return "眼饰";
            case 103: return "耳环";
            case 109: return "盾牌";
            case 104: return "上衣";
            case 105: return "套服";
            case 106: return "裤子";
            case 107: return "鞋子";
            case 108: return "手套";
            case 110: return "披风";
            case 111: return "戒指";
            case 112: return "吊坠";
            case 113: return "腰带";
            case 114: return "勋章";
            case 115: return "肩章";
            default: return itemId / 1000000 == 1 ? "武器" : "装备";
        }
    }

    // 悬停装备所属的套装（按已穿件数降序）。走 defsByItem 反向索引，避免每次悬停扫 877 套。
    public static List<SetPanel> PanelsFor(int itemId, IEnumerable<int> wornIds)
    {
        var result = new List<SetPanel>();
        if (itemId <= 0) return result;
        try
        {
            EnsureLoaded();
            // 重复道具按件计（282 戒指套装可同时戴多枚同 id 戒指）。
            var wornRemaining = new Dictionary<int, int>();
            if (wornIds != null)
                foreach (int id in wornIds)
                    if (id > 0) wornRemaining[id] = wornRemaining.TryGetValue(id, out int n) ? n + 1 : 1;
            var wornKeys = new HashSet<int>(wornRemaining.Keys);

            var candidates = new List<Definition>();
            if (defsByItem.TryGetValue(itemId, out var indexed)) candidates.AddRange(indexed);
            // 后台 slotOverrides 可能把悬停道具塞进原本不含它的套装。
            foreach (var kv in slotOverrides)
                foreach (var slot in kv.Value)
                    if (slot.Contains(itemId))
                    {
                        var d = builtins.FirstOrDefault(b => b.Id == kv.Key)
                             ?? customDefs.FirstOrDefault(c => c.Id == kv.Key);
                        if (d != null && !candidates.Contains(d)) candidates.Add(d);
                        break;
                    }

            foreach (var def in candidates)
            {
                if (def.BuiltIn && disabledIds.Contains(def.Id)) continue;
                var slots = slotOverrides.TryGetValue(def.Id, out var ov) ? ov : def.Slots;
                int complete = def.CompleteCount > 0 ? def.CompleteCount : slots.Count;
                if (complete <= 0) continue;
                var panel = new SetPanel
                {
                    Id = def.Id,
                    Name = string.IsNullOrEmpty(def.Name) ? ("套装 " + def.Id) : def.Name,
                    CompleteCount = complete,
                    JokerPossible = def.JokerPossible,
                };
                // 282 的 ItemID 是扁平表，completeCount 可能大于推断出的槽组数（戒指/同类武器
                // 套装）。多出来的行复用最后一槽；每次消耗一份 wornRemaining，于是多枚同 id
                // 戒指会分别点亮多行，件数与结算的重复计件口径一致。
                var remaining = new Dictionary<int, int>(wornRemaining);
                int wornRows = 0;
                int rowCount = Math.Max(complete, slots.Count);
                for (int i = 0; i < rowCount; i++)
                {
                    int slotIndex = i < slots.Count ? i : slots.Count - 1;
                    if (slotIndex < 0) break;
                    var slot = slots[slotIndex];
                    var meta = slotIndex < def.Metas.Count ? def.Metas[slotIndex] : null;
                    var ps = new SetPanelSlot
                    {
                        Part = meta != null && !string.IsNullOrEmpty(meta.Part) ? meta.Part
                            : slot.Count > 0 ? SlotNameOf(slot[0]) : "装备",
                        Represent = meta != null ? meta.Represent : "",
                    };
                    if (string.IsNullOrEmpty(ps.Represent) && DescKeyOfPart.TryGetValue(ps.Part, out var descKey))
                        def.Desc.TryGetValue(descKey, out ps.Represent);
                    foreach (int id in slot)
                    {
                        string name = ItemCatalog.Name(id);
                        if (string.IsNullOrEmpty(name)) name = EquipName282(id);
                        if (string.IsNullOrEmpty(name)) name = ps.Represent;
                        if (string.IsNullOrEmpty(name)) name = id.ToString();
                        ps.Alts.Add(new SetPanelAlt
                        {
                            ItemId = id, Name = name, SlotName = SlotNameOf(id),
                            Equipped = wornKeys.Contains(id),
                        });
                    }
                    // 这一行是否「装备中」：按剩余份数消费，避免一枚戒指点亮全部重复行。
                    bool equipped = false;
                    foreach (var alt in ps.Alts)
                        if (remaining.TryGetValue(alt.ItemId, out int n) && n > 0)
                        {
                            remaining[alt.ItemId] = n - 1;
                            equipped = true;
                            break;
                        }
                    foreach (var alt in ps.Alts) alt.Equipped = equipped;
                    ps.Represent = ps.Represent ?? "";
                    if (equipped) wornRows++;
                    panel.Slots.Add(ps);
                }
                panel.EquippedCount = wornRows;

                foreach (var t in def.Tiers)
                {
                    var pt = new SetPanelTier { Required = t.Required };
                    foreach (var key in SupportedStats)
                        if (t.Stats.TryGetValue(key, out int v) && v != 0)
                            pt.Stats.Add(new KeyValuePair<string, int>(key, v));
                    foreach (var o in t.Options) if (!string.IsNullOrEmpty(o.Text)) pt.Options.Add(o.Text);
                    foreach (var s in t.Skills) pt.Skills.Add("Lv." + s.Level + " " + s.Name);
                    foreach (var kv in t.Unmapped)
                        pt.Unmapped.Add(UnmappedLabel(kv.Key) + " +" + kv.Value + "（未接入）");
                    panel.Tiers.Add(pt);
                }
                result.Add(panel);
            }
            result.Sort((a, b) => b.EquippedCount.CompareTo(a.EquippedCount));
        }
        catch { /* 无库/缺 catalog：不显示套装块 */ }
        return result;
    }

    // 后台改配置后让玩法侧下一次结算重新装载（等价源端 refreshSetItemBonuses 的失效半边）
    public static void Invalidate() => builtins = null;
}
