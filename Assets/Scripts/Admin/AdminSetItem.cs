// 套装管理（源端 SetItemController + SetItemConfigService，URL 与 DTO 字段对齐）。
//
// 内置套装来自源端 gms-server/src/main/resources/set-item/catalog.json（55 套，
// 已拷为 Resources/Admin/SetItemCatalog.json）。两类持久化都进 game_config：
//   set_item_bonus_overrides        {"套装id:档位件数": {"STAT": 差量}}（REMOVED_VALUE=int.MinValue 表示删除该属性）
//   set_item_catalog_customizations {"customDefinitions":[...],"disabledBuiltInIds":[...],"slotOverrides":{...}}
// 语义逐条对齐源端：覆盖层只存与默认值的差量；槽位与默认一致时删覆盖；自定义套装 id 从 20000 起。
//
// 已知边界：本工程玩法侧尚未消费套装加成（源端 refreshOnlineCharacters 在此为空操作），
// 后台配置先落库，玩法接入时直接读这两条 game_config。
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

    // 源端 SetItemManager.SUPPORTED_STAT_KEYS
    static readonly string[] SupportedStats =
    {
        "STR", "DEX", "INT", "LUK", "PAD", "MAD", "PDD", "MDD", "ACC", "EVA",
        "SPD", "JMP", "HP", "MP", "FinalDamage", "BossDamage", "NormalDamage", "Damage",
        "ExpRate", "DropRate", "MesoRate",
        "STRPct", "DEXPct", "INTPct", "LUKPct", "PADPct", "MADPct",
        "PDDPct", "MDDPct", "ACCPct", "EVAPct", "HPPct", "MPPct",
    };

    sealed class Tier { public int Required; public Dictionary<string, int> Stats = new Dictionary<string, int>(); }
    sealed class Definition
    {
        public int Id, JobIndex;
        public string Name;
        public List<List<int>> Slots = new List<List<int>>();
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

    static void EnsureLoaded()
    {
        if (builtins != null) return;
        builtins = new List<Definition>();

        string json = PlatformRes.LoadText("Admin/SetItemCatalog");
        var rows = AdminJson.AsList(AdminJson.Parse(json));
        if (rows != null)
            foreach (var row in rows)
            {
                var d = AdminJson.AsDict(row);
                var def = new Definition
                {
                    Id = AdminJson.GetInt(d, "id"),
                    JobIndex = AdminJson.GetInt(d, "jobIndex", -1),
                    Name = AdminJson.GetString(d, "name"),
                    BuiltIn = true,
                };
                foreach (var slot in AdminJson.AsList(d["slots"]))
                {
                    var items = new List<int>();
                    foreach (var id in AdminJson.AsList(slot)) items.Add(Convert.ToInt32(id));
                    def.Slots.Add(items);
                }
                foreach (var t in AdminJson.AsList(d["tiers"]))
                {
                    var td = AdminJson.AsDict(t);
                    var tier = new Tier { Required = AdminJson.GetInt(td, "requiredCount") };
                    foreach (var kv in AdminJson.AsDict(td["stats"]))
                        tier.Stats[kv.Key] = Convert.ToInt32(kv.Value);
                    def.Tiers.Add(tier);
                }
                def.Tiers.Sort((a, b) => a.Required.CompareTo(b.Required));
                builtins.Add(def);
            }

        ReloadConfig();
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
                "completeCount", (long)slots.Count,
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
        return s.EndsWith("rate") || s.EndsWith("pct") || s.EndsWith("damage") || s == "statusres" || s == "buffduration";
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

    // ≡ SetItemManager.compute：逐槽组匹配（一组只计一件）、档位**累加**、jobEligible 才给加成。
    // 返回的 map 同时供面板（STR/HP/PAD…）、伤害（Damage/FinalDamage…）与倍率（ExpRate…）读取。
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
            EnsureLoaded();
            var equipped = new HashSet<int>(wornIds);
            foreach (var def in AllDefs())
            {
                bool jobEligible = def.JobIndex < 0 || def.JobIndex == jobIndex;
                if (!jobEligible) continue;
                if (def.BuiltIn && disabledIds.Contains(def.Id)) continue;

                // 槽位差量生效的槽组（Catalog 同一条路）
                var slots = slotOverrides.TryGetValue(def.Id, out var ov) ? ov : def.Slots;
                int count = 0;
                foreach (var slot in slots)
                {
                    foreach (int itemId in slot)
                    {
                        if (equipped.Contains(itemId)) { count++; break; }
                    }
                }
                // 源端：count >= requiredCount 的每一档都累加（不是只取最高档）
                foreach (var tier in def.Tiers)
                {
                    if (count < tier.Required) continue;
                    var stats = new Dictionary<string, int>(tier.Stats);
                    // 差量覆盖（SetItemBonusOverrides.withOverrides；RemovedValue=删除该属性）
                    if (bonusStore.TryGetValue(def.Id + ":" + tier.Required, out var diff))
                        foreach (var kv in diff)
                        {
                            if (kv.Value == RemovedValue) stats.Remove(kv.Key);
                            else stats[kv.Key] = kv.Value;
                        }
                    Add(stats);
                }
            }
        }
        catch { /* 无库/WZ 缺文件时无加成（源端 compute 异常同样回 NONE） */ }
        return bonus;
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
    public static int ApplyDamage(Dictionary<string, int> bonus, bool boss, int damage)
    {
        if (damage <= 0 || damage == int.MaxValue) return damage;
        bonus.TryGetValue("Damage", out int percent);
        bonus.TryGetValue(boss ? "BossDamage" : "NormalDamage", out int extra);
        percent += extra;
        bonus.TryGetValue("FinalDamage", out int finalPct);
        long result = (long)Math.Round(damage * (100.0 + percent) / 100.0 * (100.0 + finalPct) / 100.0);
        return (int)Math.Min(int.MaxValue, Math.Max(1L, result));
    }

    // 倍率类（getExpRate/getDropRate/getMesoRate 的套装部分）：rate × (100 + X) / 100
    public static double ApplyRate(Dictionary<string, int> bonus, string key, double rate)
    {
        bonus.TryGetValue(key, out int v);
        return rate * (100 + v) / 100.0;
    }

    // ——————————— 装备提示的套装面板 ———————————
    // 源端是 BeiDouSetItemCompat.dll 自己建的 native layer（0x65C82F00 段）：解 0x17A 包后
    // 按「套装名 (已穿/完整件数) + 每件装备(槽位名) + 套装效果 + [N件] 属性」排版，宽 236。
    // 本工程不跑那个 DLL 的包，直接用同一份 catalog 现算，保证与后台改动一致。
    public sealed class SetPanelAlt
    {
        public int ItemId;
        public string Name;
        public string SlotName;
        public bool Equipped;
    }

    public sealed class SetPanelSlot
    {
        public readonly List<SetPanelAlt> Alts = new List<SetPanelAlt>();
        public bool AnyWorn { get { foreach (var a in Alts) if (a.Equipped) return true; return false; } }
    }

    public sealed class SetPanelTier
    {
        public int Required;
        public readonly List<KeyValuePair<string, int>> Stats = new List<KeyValuePair<string, int>>();
    }

    public sealed class SetPanel
    {
        public int Id;
        public string Name;
        public int CompleteCount;   // 套装完整件数（= 槽位数）
        public int EquippedCount;   // 当前穿着的件数
        public readonly List<SetPanelSlot> Slots = new List<SetPanelSlot>();
        public readonly List<SetPanelTier> Tiers = new List<SetPanelTier>();
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

    // 悬停装备所属的套装（源端按已穿件数降序，同一件装备通常只落一个套装）
    public static List<SetPanel> PanelsFor(int itemId, IEnumerable<int> wornIds)
    {
        var result = new List<SetPanel>();
        if (itemId <= 0) return result;
        try
        {
            EnsureLoaded();
            var worn = new HashSet<int>();
            if (wornIds != null) foreach (int id in wornIds) if (id > 0) worn.Add(id);

            foreach (var def in AllDefs())
            {
                if (def.BuiltIn && disabledIds.Contains(def.Id)) continue;
                var slots = slotOverrides.TryGetValue(def.Id, out var ov) ? ov : def.Slots;
                bool belongs = false;
                foreach (var slot in slots) if (slot.Contains(itemId)) { belongs = true; break; }
                if (!belongs) continue;

                var panel = new SetPanel
                {
                    Id = def.Id,
                    Name = string.IsNullOrEmpty(def.Name) ? ("套装 " + def.Id) : def.Name,
                    CompleteCount = slots.Count,
                };
                int wornCount = 0;
                foreach (var slot in slots)
                {
                    var ps = new SetPanelSlot();
                    foreach (int id in slot)
                        ps.Alts.Add(new SetPanelAlt
                        {
                            ItemId = id,
                            Name = ItemCatalog.Name(id),
                            SlotName = SlotNameOf(id),
                            Equipped = worn.Contains(id),
                        });
                    if (ps.AnyWorn) wornCount++;
                    panel.Slots.Add(ps);
                }
                panel.EquippedCount = wornCount;
                foreach (var t in def.Tiers)
                {
                    var pt = new SetPanelTier { Required = t.Required };
                    // 与源端一致：按 STAT_KEYS 顺序输出（服务端也是这个顺序写包）
                    foreach (var key in SupportedStats)
                        if (t.Stats.TryGetValue(key, out int v) && v != 0)
                            pt.Stats.Add(new KeyValuePair<string, int>(key, v));
                    panel.Tiers.Add(pt);
                }
                result.Add(panel);
            }
            // 源端 setItemUpdate 按 equippedCount 降序
            result.Sort((a, b) => b.EquippedCount.CompareTo(a.EquippedCount));
        }
        catch { /* 无库/缺 catalog：不显示套装块 */ }
        return result;
    }

    // 后台改配置后让玩法侧下一次结算重新装载（等价源端 refreshSetItemBonuses 的失效半边）
    public static void Invalidate() => builtins = null;
}
