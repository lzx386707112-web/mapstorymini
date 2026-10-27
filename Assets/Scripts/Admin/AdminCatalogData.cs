// 图鉴模块数据层：目录缓存 + WZ 枚举 + 查询过滤（物品 / 装备 / 怪物 / 技能）。
//
// 与源端（Spring Boot + 预生成 catalog.json）不同，本项目运行时直读 WZ（MapleDashResources/Data）：
//   物品  枚举 String/{Consume,Etc,Ins,Cash}.img/<id>（名称/描述），Special 枚举
//         Item/Special/*.img 子节点（String.wz 里没有 Special 条目，名称兜底
//         "Item <节点名>"——与源端 catalog.json 的兜底输出一致）。
//         specs = Item/<分类>/<前缀>.img/<id> 的 info+spec 两个节点的标量属性合并
//         （实测 info 有 price/slotMax、spec 有 hp/mp 等，源端 catalog 的 specs 是两者并集）。
//         装备不进物品图鉴；宠物（String/Pet.img）也不进——源端 Pet 分类计数就是 0，
//         但 categories 分面仍固定列出全部 6 个分类（含空分类）。
//   装备  枚举 Character/<类别>/<8位id>.img（类别 = Cap/Weapon/… 共 14 个目录），
//         名称/描述查 String/Eqp.img/Eqp/<子类>/<id>，stats = info 节点的数字属性
//         （inc*/req*/tuc/cash/gender…），category = 目录名，cash = info/cash==1。
//   怪物  枚举 String/Mob.img 子节点（名称），stats 取 Mob/<7位id>.img/info 的固定
//         11 个数字键（level/exp/maxHP/maxMP/PADamage/PDDamage/MADamage/MDDamage/
//         acc/eva/speed，与源端 mob-catalog/catalog.json 一致），info/link 的
//         链接怪沿用 WzAssets.MobClip 的跟链语义。
//   技能  枚举 Skill/<职业>.img/skill/<id>，名称/描述/逐级文本取 String/Skill.img
//         （h1..h30），maxLevel = level 子节点数，attributes = level 首级的标量属性
//         + 技能节点上的 elemAttr/skillType。
//
// 性能取舍（懒加载 + 进程内缓存）：
//   全量扫 3 万+ IMG 太慢，所以每个目录只在【首次查询】时构建一次：
//   物品 ≈30 个前缀 img、装备 ≈1.8 万个 Character img（只留 info 数字属性，
//   其余节点立刻丢弃）、怪物 ≈2400 个 info、技能 ≈66 个职业 img + 1 个 String。
//   之后的分页/过滤/排序都是纯内存操作。装备文件清单走 NativeAssetIndex 的 files
//   列表（纯 JSON，比扫目录快），清单缺失时退回目录扫描。图标 PNG 单独按 id 编码并缓存。
//
// 线程：ImgFile 解析是实例状态（AES 密钥表自带锁），可在 HTTP 线程构建；
// WzAssets.FileRoot/Index 等共享缓存一律经 AdminMainThread.Run 在游戏主线程访问。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MapleDash.Wz;

// catalog 类分页形状：records / pageNo / pageSize / total（区别于 mybatis-flex 的
// pageNumber/totalRow，前端 AdminUi/src/api/*.ts 就按这几个字段取值），可挂分面字段。
public sealed class CatalogPage
{
    public readonly List<object> Records;
    public readonly int PageNo;
    public readonly int PageSize;
    public readonly long Total;
    readonly List<KeyValuePair<string, object>> extras = new List<KeyValuePair<string, object>>();

    public CatalogPage(List<object> records, int pageNo, int pageSize, long total)
    {
        Records = records ?? new List<object>();
        PageNo = pageNo;
        PageSize = pageSize;
        Total = total;
    }

    public CatalogPage Extra(string key, object value)
    {
        extras.Add(new KeyValuePair<string, object>(key, value));
        return this;
    }

    public Dictionary<string, object> ToDict()
    {
        var d = new Dictionary<string, object>
        {
            { "records", Records },
            { "pageNo", PageNo },
            { "pageSize", PageSize },
            { "total", Total },
        };
        foreach (var kv in extras) d[kv.Key] = kv.Value;
        return d;
    }
}

public static class AdminCatalogData
{
    // 服务启动后的后台预热：装备图鉴 3.2 万 IMG 首次构建要数秒，提前在后台线程建好，
    // 后台页面首次打开不再卡顿。重复调用安全（Ensure* 自带幂等缓存）。
    public static void Prewarm()
    {
        var t = new System.Threading.Thread(() =>
        {
            try { EnsureItems(); } catch { }
            try { EnsureEquips(); } catch { }
            try { EnsureMobs(); } catch { }
            try { EnsureSkills(); } catch { }
        }) { IsBackground = true, Name = "AdminCatalogPrewarm" };
        t.Start();
    }

    // —————————— 关键词匹配（源端 ItemCatalogService 的 matches/matchRank 语义） ——————————
    // 精确(=0) > id 前缀(=1) > name 前缀(=2) > 其余 contains(=3)；同 rank 再 id 升序。
    public static string Normalize(string keyword)
        => string.IsNullOrEmpty(keyword) ? "" : keyword.Trim().ToLowerInvariant();

    public static bool Matches(long id, string name, string query)
    {
        if (query.Length == 0) return true;
        return id.ToString(CultureInfo.InvariantCulture).IndexOf(query, StringComparison.Ordinal) >= 0
            || (name ?? "").ToLowerInvariant().IndexOf(query, StringComparison.Ordinal) >= 0;
    }

    public static int MatchRank(long id, string name, string query)
    {
        if (query.Length == 0) return 0;
        string text = id.ToString(CultureInfo.InvariantCulture);
        string lower = (name ?? "").ToLowerInvariant();
        if (text == query || lower == query) return 0;
        if (text.StartsWith(query, StringComparison.Ordinal)) return 1;
        return lower.StartsWith(query, StringComparison.Ordinal) ? 2 : 3;
    }

    // —————————— 分页切片 ——————————
    static CatalogPage Slice<T>(List<T> matched, int pageNo, int pageSize, Func<T, object> map)
    {
        int size = pageSize <= 0 ? 20 : pageSize;
        int page = pageNo <= 0 ? 1 : pageNo;
        var records = new List<object>();
        long offset = (long)(page - 1) * size;
        for (long i = offset; i < matched.Count && i < offset + size; i++)
            records.Add(map(matched[(int)i]));
        return new CatalogPage(records, page, size, matched.Count);
    }

    // —————————— WZ 小工具 ——————————
    // 直接用 ImgFile 解析（不走 WzAssets.FileRoot）：装备一次性要开上万个 IMG，
    // WzAssets.roots 会把整棵树永久缓存下来（几百 MB），这里取完 info 就丢。
    // ImgFile 构造时已把节点树全部物化，立即 Dispose 只是关掉内存流。
    static ImgNode OpenRoot(string relative)
    {
        var img = new ImgFile(Path.Combine(WzAssets.DataRoot, relative));
        try { return img.Root; }
        finally { img.Dispose(); }
    }

    static bool FileReady(string relative)
    {
        WarmUpHasFile();
        return WzAssets.HasFile(relative);
    }

    // WzAssets.HasFile 在 Android/WebGL 上懒建文件清单（HashSet，无锁），
    // 与游戏线程并发首触有竞态。预热一次放在游戏主线程，之后只有并发读（安全）。
    static bool hasFileWarmed;
    static void WarmUpHasFile()
    {
        if (hasFileWarmed) return;
        AdminMainThread.Run(() => WzAssets.HasFile("String/Mob.img"));
        hasFileWarmed = true;
    }

    // 收集节点下的标量属性（跳过 Canvas/Property/UOL 等容器）。numbersOnly 时只留数字。
    static void CollectScalars(ImgNode node, Dictionary<string, object> into, bool numbersOnly)
    {
        if (node == null) return;
        foreach (var c in node.Children)
        {
            if (c.Kind != null || c.Value == null) continue;
            object v = c.Value;
            if (numbersOnly && !(v is int || v is long || v is short || v is byte || v is sbyte || v is float || v is double))
                continue;
            into[c.Name] = v;
        }
    }

    static int ScalarInt(ImgNode node, string key, int fallback = 0)
    {
        var v = node.Get(key)?.Value;
        if (v == null) return fallback;
        try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
        catch { return fallback; }
    }

    // 文件清单：优先 NativeAssetIndex 的 files（WzAssets.Index 共享缓存，主线程取一次），
    // 缺清单（离线自检等）退回目录扫描。返回 <dir>/<file>.img 相对路径。
    static List<string> fileIndex;
    static List<string> FileIndex()
    {
        if (fileIndex != null) return fileIndex;
        var list = new List<string>();
        try
        {
            var files = AdminMainThread.Run(() => WzAssets.Index["files"] as List<object>);
            foreach (var f in files)
            {
                var d = f as Dictionary<string, object>;
                if (d != null && d.TryGetValue("path", out var p) && p is string s) list.Add(s);
            }
        }
        catch { list = null; }
        fileIndex = list ?? new List<string>();
        return fileIndex;
    }

    // 枚举 Data/<dir>/ 下的 .img 文件名（可选只要纯数字名，如 Skill/<job>.img）。
    static List<string> EnumerateImgNames(string dir, bool numericOnly)
    {
        var names = new List<string>();
        string prefix = dir + "/";
        foreach (var path in FileIndex())
        {
            if (!path.StartsWith(prefix, StringComparison.Ordinal)) continue;
            string name = path.Substring(prefix.Length);
            if (name.IndexOf('/') >= 0 || !name.EndsWith(".img", StringComparison.OrdinalIgnoreCase)) continue;
            if (numericOnly && !IsNumericName(name, 0, name.Length - 4)) continue;
            names.Add(name);
        }
        if (names.Count == 0)
        {
            // 清单缺失时兜底扫目录（WzAssets.DataRoot 在所有平台都是真实路径）
            try
            {
                foreach (var file in Directory.GetFiles(Path.Combine(WzAssets.DataRoot, dir), "*.img"))
                {
                    string name = Path.GetFileName(file);
                    if (numericOnly && !IsNumericName(name, 0, name.Length - 4)) continue;
                    names.Add(name);
                }
            }
            catch { /* 目录不存在：返回空 */ }
        }
        return names;
    }

    static bool IsNumericName(string s, int start, int end)
    {
        if (end <= start) return false;
        for (int i = start; i < end; i++) if (s[i] < '0' || s[i] > '9') return false;
        return true;
    }

    // —————————— 物品图鉴 ——————————
    // 分类固定 6 键（源端 catalog.json 的 categories 顺序），Pet 恒为 0（空分类也返回）。
    static readonly string[] ItemCategoryKeys = { "Consume", "Etc", "Install", "Cash", "Pet", "Special" };

    sealed class ItemEntry
    {
        public long Id;
        public string Name, Description, Category;
        public Dictionary<string, object> Specs = new Dictionary<string, object>();
        public bool Icon;
    }

    static readonly object buildGate = new object();
    static List<ItemEntry> itemEntries;
    static List<object> itemCategoryFacet;

    static void EnsureItems()
    {
        if (itemEntries != null) return;
        lock (buildGate)
        {
            if (itemEntries != null) return;
            var entries = new List<ItemEntry>();
            // String 侧：Consume/Etc(多一层 Etc)/Ins/Cash 的子节点名 = 道具 id
            AddStringItems(entries, "String/Consume.img", null, "Consume");
            AddStringItems(entries, "String/Etc.img", "Etc", "Etc");
            AddStringItems(entries, "String/Ins.img", null, "Install");
            AddStringItems(entries, "String/Cash.img", null, "Cash");
            // Special 在 String.wz 没有条目，直接枚举 Item/Special/*.img（含 MaplePoint.img）
            foreach (var name in EnumerateImgNames("Item/Special", false))
            {
                ImgNode root;
                try { root = OpenRoot("Item/Special/" + name); }
                catch { continue; }
                foreach (var node in root.Children)
                {
                    if (!long.TryParse(node.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
                    var entry = new ItemEntry
                    {
                        Id = id,
                        // 源端 catalog.json 对 Special 的兜底名就是 "Item <节点名>"（保留原始补零）
                        Name = "Item " + node.Name,
                        Description = "",
                        Category = "Special",
                        Icon = node.Get("info/icon") != null || node.Get("icon") != null,
                    };
                    CollectScalars(node.Get("info"), entry.Specs, false);
                    CollectScalars(node.Get("spec"), entry.Specs, false);
                    entries.Add(entry);
                }
            }

            entries.Sort((a, b) => a.Id.CompareTo(b.Id));
            // 一次把 spec 图标/属性缺省打好，后面纯内存过滤
            var counts = new Dictionary<string, int>();
            foreach (var key in ItemCategoryKeys) counts[key] = 0;
            foreach (var e in entries) counts[e.Category] = counts[e.Category] + 1;
            var facet = new List<object>();
            foreach (var key in ItemCategoryKeys)
                facet.Add(AdminJson.Obj("key", key, "count", counts[key]));
            itemCategoryFacet = facet;
            itemEntries = entries;
        }
    }

    static void AddStringItems(List<ItemEntry> entries, string file, string subNode, string category)
    {
        ImgNode root;
        try { root = OpenRoot(file); }
        catch { return; }
        var parent = subNode == null ? root : root.Get(subNode);
        if (parent == null) return;

        // 先按 id 前缀分组，Item/<分类>/<前缀>.img 一个 IMG 装上万道具，按前缀一次读完
        var nodes = new List<KeyValuePair<long, ImgNode>>();
        foreach (var node in parent.Children)
        {
            if (!long.TryParse(node.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
            nodes.Add(new KeyValuePair<long, ImgNode>(id, node));
        }

        string folder = category == "Consume" ? "Consume" : category == "Etc" ? "Etc"
                      : category == "Install" ? "Install" : "Cash";
        var byPrefix = new Dictionary<long, ImgNode>();
        foreach (var kv in nodes)
        {
            long prefix = kv.Key / 10000;
            if (!byPrefix.TryGetValue(prefix, out var itemRoot))
            {
                string path = "Item/" + folder + "/" + prefix.ToString("D4", CultureInfo.InvariantCulture) + ".img";
                itemRoot = null;
                if (FileReady(path))
                {
                    try { itemRoot = OpenRoot(path); }
                    catch { itemRoot = null; }
                }
                byPrefix[prefix] = itemRoot;
            }

            var entry = new ItemEntry
            {
                Id = kv.Key,
                Name = kv.Value.Text("name", "Item " + kv.Value.Name),
                Description = kv.Value.Text("desc"),
                Category = category,
            };
            if (itemRoot != null)
            {
                // IMG 内节点名 8 位补零（02000000），个别礼包是 7 位原样，两种都试
                var itemNode = itemRoot.Get(kv.Key.ToString("D8", CultureInfo.InvariantCulture))
                            ?? itemRoot.Get(kv.Key.ToString(CultureInfo.InvariantCulture));
                if (itemNode != null)
                {
                    CollectScalars(itemNode.Get("info"), entry.Specs, false);
                    CollectScalars(itemNode.Get("spec"), entry.Specs, false);
                    entry.Icon = itemNode.Get("info/icon") != null || itemNode.Get("icon") != null;
                }
            }
            entries.Add(entry);
        }
    }

    public static CatalogPage ItemCatalog(string keyword, string category, int pageNo, int pageSize)
    {
        EnsureItems();
        string query = Normalize(keyword);
        string filter = string.IsNullOrEmpty(category) ? "" : category.Trim();
        var matched = new List<ItemEntry>();
        foreach (var e in itemEntries)
        {
            if (filter.Length > 0 && e.Category != filter) continue;
            if (!Matches(e.Id, e.Name, query)) continue;
            matched.Add(e);
        }
        // keyword 空 → id 升序（构建时已按 id 排好）；否则 matchRank 再 id 升序
        if (query.Length > 0)
            matched.Sort((a, b) =>
            {
                int r = MatchRank(a.Id, a.Name, query).CompareTo(MatchRank(b.Id, b.Name, query));
                return r != 0 ? r : a.Id.CompareTo(b.Id);
            });
        return Slice(matched, pageNo, Clamp(pageSize, 60, 120), ItemDto)
            .Extra("categories", itemCategoryFacet);
    }

    static Dictionary<string, object> ItemDto(ItemEntry e) => AdminJson.Obj(
        "id", e.Id,
        "name", e.Name,
        "description", e.Description,
        "category", e.Category,
        "specs", e.Specs,
        "iconAvailable", e.Icon);

    static int Clamp(int value, int fallback, int max)
    {
        if (value <= 0) return fallback;
        return value > max ? max : value;
    }

    // —————————— 装备图鉴 ——————————
    sealed class EquipEntry
    {
        public long Id;
        public string Name, Description, Category, Kind;
        public Dictionary<string, object> Stats = new Dictionary<string, object>();
        public bool Icon, Cash;
        public int ReqJob, ReqLevel;
    }

    static List<EquipEntry> equipEntries;
    static List<object> equipCategoryFacet;

    // 装备种类（源端 EquipmentCatalogService.EQUIP_KIND_NAMES）：id/10000 前缀 → 中文名。
    // weaponTypes 分面用的是这些 key（不是 Character 目录名）。
    static readonly Dictionary<int, string> EquipKinds = new Dictionary<int, string>
    {
        { 100, "帽子" }, { 101, "脸饰" }, { 102, "眼饰" }, { 103, "耳环" }, { 104, "上衣" },
        { 105, "套服" }, { 106, "裤子" }, { 107, "鞋子" }, { 108, "手套" }, { 109, "盾牌" },
        { 110, "披风" }, { 111, "戒指" }, { 112, "项环" }, { 113, "腰带" }, { 114, "勋章" },
        { 115, "护肩" }, { 118, "纹章" }, { 119, "徽章" },
        { 130, "单手剑" }, { 131, "单手斧" }, { 132, "单手钝器" }, { 133, "短刀" }, { 134, "副刀" },
        { 137, "短杖" }, { 138, "长杖" },
        { 140, "双手剑" }, { 141, "双手斧" }, { 142, "双手钝器" }, { 143, "长枪" }, { 144, "枪戟" },
        { 145, "弓" }, { 146, "弩" }, { 147, "拳套" }, { 148, "指虎" }, { 149, "短枪" },
        { 167, "心脏" }, { 170, "扇子" }, { 180, "头饰" },
    };

    static readonly string[] EquipDirs =
    {
        "Cap", "Coat", "Longcoat", "Pants", "Shoes", "Glove", "Shield", "Cape",
        "Ring", "Accessory", "PetEquip", "Taming", "Dragon", "Weapon",
    };

    static void EnsureEquips()
    {
        if (equipEntries != null) return;
        lock (buildGate)
        {
            if (equipEntries != null) return;
            var strings = LoadEquipStrings();
            var entries = new List<EquipEntry>();
            foreach (var kv in EnumerateEquipFiles())
            {
                string dir = kv.Key;
                long id = kv.Value;
                ImgNode root = null, info = null;
                try
                {
                    root = OpenRoot("Character/" + dir + "/" + id.ToString("D8", CultureInfo.InvariantCulture) + ".img");
                    info = root.Get("info");
                }
                catch { continue; }
                if (info == null) continue;

                var entry = new EquipEntry
                {
                    Id = id,
                    Name = "",
                    Description = "",
                    Category = dir,
                    ReqJob = ScalarInt(info, "reqJob"),
                    ReqLevel = ScalarInt(info, "reqLevel"),
                    Cash = ScalarInt(info, "cash") == 1,
                    Icon = info.Get("icon") != null || root.Get("icon") != null,
                };
                strings.TryGetValue(id, out var nameDesc);
                if (nameDesc != null) { entry.Name = nameDesc[0]; entry.Description = nameDesc[1]; }
                CollectScalars(info, entry.Stats, true);
                EquipKinds.TryGetValue((int)(id / 10000), out entry.Kind);
                entries.Add(entry);
            }

            entries.Sort((a, b) => a.Id.CompareTo(b.Id));
            var counts = new Dictionary<string, int>();
            foreach (var e in entries) counts[e.Category] = counts.TryGetValue(e.Category, out var n) ? n + 1 : 1;
            var facet = new List<object>();
            foreach (var key in counts.Keys)
                facet.Add(AdminJson.Obj("key", key, "count", counts[key]));
            facet.Sort((a, b) => string.CompareOrdinal(
                (string)AdminJson.Get(a, "key"), (string)AdminJson.Get(b, "key")));
            equipCategoryFacet = facet;
            equipEntries = entries;
        }
    }

    // id → [name, desc]：String/Eqp.img/Eqp/<子类>/<id>，子类名随版本变化，整表一次读平。
    static Dictionary<long, string[]> LoadEquipStrings()
    {
        var map = new Dictionary<long, string[]>();
        ImgNode root;
        try { root = OpenRoot("String/Eqp.img"); }
        catch { return map; }
        var eqp = root.Get("Eqp") ?? root;
        foreach (var group in eqp.Children)
        {
            foreach (var node in group.Children)
            {
                if (!long.TryParse(node.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
                if (map.ContainsKey(id)) continue;
                map[id] = new[] { node.Text("name"), node.Text("desc") };
            }
        }
        return map;
    }

    // Character/<类别>/<8位id>.img 枚举：优先 NativeAssetIndex files（快），退回扫目录。
    static List<KeyValuePair<string, long>> EnumerateEquipFiles()
    {
        var result = new List<KeyValuePair<string, long>>();
        var dirSet = new HashSet<string>(EquipDirs);
        foreach (var path in FileIndex())
        {
            if (!path.StartsWith("Character/", StringComparison.Ordinal)) continue;
            string rest = path.Substring("Character/".Length);
            int slash = rest.IndexOf('/');
            if (slash <= 0 || !rest.EndsWith(".img", StringComparison.OrdinalIgnoreCase)) continue;
            string dir = rest.Substring(0, slash);
            string name = rest.Substring(slash + 1);
            if (!dirSet.Contains(dir)) continue;
            if (name.Length != 12 || !IsNumericName(name, 0, 8)) continue;
            if (!long.TryParse(name.Substring(0, 8), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
            result.Add(new KeyValuePair<string, long>(dir, id));
        }
        if (result.Count == 0)
        {
            foreach (var dir in EquipDirs)
            {
                string abs = Path.Combine(WzAssets.DataRoot, "Character", dir);
                string[] files;
                try { files = Directory.GetFiles(abs, "*.img"); }
                catch { continue; }
                foreach (var file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (name.Length != 8 || !IsNumericName(name, 0, 8)) continue;
                    if (long.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                        result.Add(new KeyValuePair<string, long>(dir, id));
                }
            }
        }
        return result;
    }

    public static CatalogPage EquipmentCatalog(string keyword, string category, int pageNo, int pageSize,
        bool? cash, string weaponType, int? job, int? minLevel, int? maxLevel)
    {
        EnsureEquips();
        string query = Normalize(keyword);
        string filter = string.IsNullOrEmpty(category) ? "" : category.Trim();
        string kindFilter = string.IsNullOrEmpty(weaponType) ? "" : weaponType.Trim();

        // base = category + keyword（分面的公共底座）
        var baseList = new List<EquipEntry>();
        foreach (var e in equipEntries)
        {
            if (filter.Length > 0 && e.Category != filter) continue;
            if (!Matches(e.Id, e.Name, query)) continue;
            baseList.Add(e);
        }

        // weaponTypes 分面：受 cash 过滤影响、不受自身选择影响（源端注释如此）；按 key 升序
        var kindCounts = new Dictionary<string, int>();
        foreach (var e in baseList)
        {
            if (cash.HasValue && e.Cash != cash.Value) continue;
            if (e.Kind == null) continue; // 没有种类映射的不进 weaponTypes 分面
            kindCounts[e.Kind] = kindCounts.TryGetValue(e.Kind, out var n) ? n + 1 : 1;
        }
        var weaponTypes = new List<object>();
        foreach (var kv in kindCounts)
            weaponTypes.Add(AdminJson.Obj("key", kv.Key, "count", kv.Value));
        weaponTypes.Sort((a, b) => string.CompareOrdinal(
            (string)AdminJson.Get(a, "key"), (string)AdminJson.Get(b, "key")));

        // cashCount/nonCashCount：受 weaponType 过滤影响、不受 cash 自身影响
        long cashCount = 0, nonCashCount = 0;
        foreach (var e in baseList)
        {
            if (kindFilter.Length > 0 && e.Kind != kindFilter) continue;
            if (e.Cash) cashCount++;
            else nonCashCount++;
        }

        // 最终匹配：叠加 cash/weaponType/job/等级 过滤
        var matched = new List<EquipEntry>();
        foreach (var e in baseList)
        {
            if (cash.HasValue && e.Cash != cash.Value) continue;
            if (kindFilter.Length > 0 && e.Kind != kindFilter) continue;
            if (job.HasValue && job.Value != 0 && e.ReqJob != 0 && (e.ReqJob & job.Value) == 0) continue;
            if (minLevel.HasValue && e.ReqLevel < minLevel.Value) continue;
            if (maxLevel.HasValue && e.ReqLevel > maxLevel.Value) continue;
            matched.Add(e);
        }
        if (query.Length > 0)
            matched.Sort((a, b) =>
            {
                int r = MatchRank(a.Id, a.Name, query).CompareTo(MatchRank(b.Id, b.Name, query));
                return r != 0 ? r : a.Id.CompareTo(b.Id);
            });

        return Slice(matched, pageNo, Clamp(pageSize, 60, 120), EquipDto)
            .Extra("categories", equipCategoryFacet)
            .Extra("weaponTypes", weaponTypes)
            .Extra("cashCount", cashCount)
            .Extra("nonCashCount", nonCashCount);
    }

    static Dictionary<string, object> EquipDto(EquipEntry e) => AdminJson.Obj(
        "id", e.Id,
        "name", e.Name,
        "description", e.Description,
        "category", e.Category,
        "stats", e.Stats,
        "iconAvailable", e.Icon);

    // —————————— 怪物图鉴 ——————————
    // stats 只取源端 mob-catalog 一致的 11 个数字键（info 里还有 bodyAttack/undead/fs 等，源端没进目录）
    static readonly string[] MobStatKeys =
    {
        "level", "exp", "maxHP", "maxMP", "PADamage", "PDDamage", "MADamage", "MDDamage", "acc", "eva", "speed",
    };

    sealed class MobEntry
    {
        public long Id;
        public string Name;
        public Dictionary<string, object> Stats = new Dictionary<string, object>();
        public bool Icon;
        public int Level;
    }

    static List<MobEntry> mobEntries;

    static void EnsureMobs()
    {
        if (mobEntries != null) return;
        lock (buildGate)
        {
            if (mobEntries != null) return;
            var entries = new List<MobEntry>();
            ImgNode root;
            try { root = OpenRoot("String/Mob.img"); }
            catch { root = null; }
            if (root != null)
            {
                foreach (var node in root.Children)
                {
                    if (!long.TryParse(node.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
                    var entry = new MobEntry { Id = id, Name = node.Text("name", "Mob " + id) };
                    var info = MobInfo(id, out var mobRoot);
                    if (info != null)
                    {
                        var numbers = new Dictionary<string, object>();
                        CollectScalars(info, numbers, true);
                        foreach (var key in MobStatKeys)
                            if (numbers.TryGetValue(key, out var v)) entry.Stats[key] = v;
                        entry.Level = ScalarInt(info, "level");
                        entry.Icon = mobRoot != null && mobRoot.Get("stand/0") != null;
                    }
                    entries.Add(entry);
                }
            }
            entries.Sort((a, b) => a.Id.CompareTo(b.Id));
            mobEntries = entries;
        }
    }

    // Mob/<7位id>.img 的 info；info/link 的怪跟链到目标（同 WzAssets.MobClip 语义），
    // 同时把最终 root 交出去（图标取 stand/0）。
    static ImgNode MobInfo(long id, out ImgNode resolvedRoot)
    {
        resolvedRoot = null;
        string file = "Mob/" + id.ToString("D7", CultureInfo.InvariantCulture) + ".img";
        if (!FileReady(file)) return null;
        ImgNode root;
        try { root = OpenRoot(file); }
        catch { return null; }
        for (int i = 0; i < 64; i++)
        {
            var link = root.Get("info/link");
            if (link == null || link.Value == null) break;
            long target;
            try { target = Convert.ToInt64(link.Value, CultureInfo.InvariantCulture); }
            catch { break; }
            string next = "Mob/" + target.ToString("D7", CultureInfo.InvariantCulture) + ".img";
            if (!FileReady(next)) return null;
            try { root = OpenRoot(next); }
            catch { return null; }
        }
        resolvedRoot = root;
        return root.Get("info");
    }

    public static CatalogPage MobCatalog(string keyword, int pageNo, int pageSize, int? minLevel, int? maxLevel)
    {
        EnsureMobs();
        string query = Normalize(keyword);
        var matched = new List<MobEntry>();
        foreach (var e in mobEntries)
        {
            if (!Matches(e.Id, e.Name, query)) continue;
            if (minLevel.HasValue && e.Level < minLevel.Value) continue;
            if (maxLevel.HasValue && e.Level > maxLevel.Value) continue;
            matched.Add(e);
        }
        if (query.Length > 0)
            matched.Sort((a, b) =>
            {
                int r = MatchRank(a.Id, a.Name, query).CompareTo(MatchRank(b.Id, b.Name, query));
                return r != 0 ? r : a.Id.CompareTo(b.Id);
            });
        return Slice(matched, pageNo, Clamp(pageSize, 60, 120), MobDto);
    }

    static Dictionary<string, object> MobDto(MobEntry e) => AdminJson.Obj(
        "id", e.Id,
        "name", e.Name,
        "stats", e.Stats,
        "iconAvailable", e.Icon);

    // —————————— 技能图鉴 ——————————
    // 职业名 / 转职次数表照抄源端 SkillCatalogService（String/Skill.img 的 bookName
    // 是「冒险的技术」这类技能书名，不是职业名，所以用内置表）。
    static readonly Dictionary<int, string> JobNames = new Dictionary<int, string>
    {
        { 0, "初心者" },
        { 100, "战士" }, { 110, "剑客" }, { 111, "勇士" }, { 112, "英雄" },
        { 120, "准骑士" }, { 121, "骑士" }, { 122, "圣骑士" },
        { 130, "枪战士" }, { 131, "龙骑士" }, { 132, "黑骑士" },
        { 200, "魔法师" }, { 210, "法师(火毒)" }, { 211, "魔法师(火毒)" }, { 212, "大魔导师(火毒)" },
        { 220, "法师(冰雷)" }, { 221, "魔法师(冰雷)" }, { 222, "大魔导师(冰雷)" },
        { 230, "牧师" }, { 231, "祭司" }, { 232, "主教" }, { 233, "主教(扩展)" },
        { 300, "弓箭手" }, { 310, "猎人" }, { 311, "射手" }, { 312, "箭神" },
        { 320, "弩弓手" }, { 321, "游侠" }, { 322, "神射手" },
        { 400, "飞侠" }, { 410, "刺客" }, { 411, "无影人" }, { 412, "夜使者" },
        { 420, "侠客" }, { 421, "独行客" }, { 422, "暗影神偷" },
        { 430, "见习刀客" }, { 431, "双刀客" }, { 432, "血刀" }, { 433, "暗影双刀" }, { 434, "暗影双刀" },
        { 500, "海盗" }, { 510, "拳手" }, { 511, "斗士" }, { 512, "冲锋队长" },
        { 520, "枪手" }, { 521, "大副" }, { 522, "船长" },
        { 1000, "初心者(骑士团)" },
        { 1100, "魂骑士" }, { 1110, "魂骑士" }, { 1111, "魂骑士" }, { 1112, "魂骑士" },
        { 1200, "炎术士" }, { 1210, "炎术士" }, { 1211, "炎术士" }, { 1212, "炎术士" },
        { 1300, "风行者" }, { 1310, "风行者" }, { 1311, "风行者" }, { 1312, "风行者" },
        { 1400, "夜行者" }, { 1410, "夜行者" }, { 1411, "夜行者" }, { 1412, "夜行者" },
        { 1500, "奇袭者" }, { 1510, "奇袭者" }, { 1511, "奇袭者" }, { 1512, "奇袭者" },
        { 2000, "战神(初心者)" }, { 2100, "战神" }, { 2110, "战神" }, { 2111, "战神" }, { 2112, "战神" },
        { 2200, "龙神(初心者)" },
        { 2210, "龙神" }, { 2211, "龙神" }, { 2212, "龙神" }, { 2213, "龙神" }, { 2214, "龙神" },
        { 2215, "龙神" }, { 2216, "龙神" }, { 2217, "龙神" }, { 2218, "龙神" },
        { 3000, "双弩精灵(初心者)" }, { 3100, "双弩精灵" }, { 3110, "双弩精灵" },
        { 3111, "双弩精灵" }, { 3112, "双弩精灵" },
        { 4000, "幻影(初心者)" }, { 4100, "幻影" }, { 4110, "幻影" }, { 4111, "幻影" }, { 4112, "幻影" },
        { 5000, "隐月(初心者)" }, { 5100, "隐月" }, { 5110, "隐月" }, { 5111, "隐月" }, { 5112, "隐月" },
        { 800, "管理员" }, { 900, "GM" }, { 910, "超级GM" },
    };

    static readonly Dictionary<int, int> JobAdvancement = new Dictionary<int, int>
    {
        { 0, 0 },
        { 100, 1 }, { 200, 1 }, { 300, 1 }, { 400, 1 }, { 500, 1 },
        { 110, 2 }, { 120, 2 }, { 130, 2 }, { 210, 2 }, { 220, 2 }, { 230, 2 },
        { 310, 2 }, { 320, 2 }, { 410, 2 }, { 420, 2 }, { 430, 2 }, { 431, 2 }, { 510, 2 }, { 520, 2 },
        { 111, 3 }, { 121, 3 }, { 131, 3 }, { 211, 3 }, { 221, 3 }, { 231, 3 },
        { 311, 3 }, { 321, 3 }, { 411, 3 }, { 421, 3 }, { 432, 3 }, { 433, 3 }, { 511, 3 }, { 521, 3 },
        { 112, 4 }, { 122, 4 }, { 132, 4 }, { 212, 4 }, { 222, 4 }, { 232, 4 },
        { 312, 4 }, { 322, 4 }, { 412, 4 }, { 422, 4 }, { 434, 4 }, { 512, 4 }, { 522, 4 },
        { 1000, 0 },
        { 1100, 1 }, { 1200, 1 }, { 1300, 1 }, { 1400, 1 }, { 1500, 1 },
        { 1110, 2 }, { 1210, 2 }, { 1310, 2 }, { 1410, 2 }, { 1510, 2 },
        { 1111, 3 }, { 1211, 3 }, { 1311, 3 }, { 1411, 3 }, { 1511, 3 },
        { 1112, 4 }, { 1212, 4 }, { 1312, 4 }, { 1412, 4 }, { 1512, 4 },
        { 2000, 0 }, { 2100, 1 }, { 2110, 2 }, { 2111, 3 }, { 2112, 4 },
        { 2200, 0 },
        { 2210, 1 }, { 2211, 2 }, { 2212, 3 }, { 2213, 4 }, { 2214, 5 }, { 2215, 6 }, { 2216, 7 }, { 2217, 8 }, { 2218, 9 },
        { 3000, 0 }, { 3100, 1 }, { 3110, 2 }, { 3111, 3 }, { 3112, 4 },
        { 4000, 0 }, { 4100, 1 }, { 4110, 2 }, { 4111, 3 }, { 4112, 4 },
        { 5000, 0 }, { 5100, 1 }, { 5110, 2 }, { 5111, 3 }, { 5112, 4 },
        { 800, 0 }, { 900, 0 }, { 910, 0 }, { 233, 4 },
    };

    static int AdvancementOf(int jobId)
        => JobAdvancement.TryGetValue(jobId, out var n) ? n : 0;

    static string JobNameOf(int jobId)
        => JobNames.TryGetValue(jobId, out var name) ? name : "职业 " + jobId;

    sealed class SkillEntry
    {
        public long Id;
        public string Name, Desc, JobName;
        public int JobId, FileJob, MaxLevel;
        public bool Icon;
        public Dictionary<string, object> Attributes = new Dictionary<string, object>();
        public List<object> LevelDescs = new List<object>();
    }

    static Dictionary<long, SkillEntry> skillEntries;
    static List<SkillEntry> skillSorted;
    static List<object> skillJobFacet;
    static string skillLoadError;

    static void EnsureSkills()
    {
        if (skillEntries != null) return;
        lock (buildGate)
        {
            if (skillEntries != null) return;
            skillLoadError = null;
            try { BuildSkills(); }
            catch (Exception e)
            {
                skillLoadError = e.GetType().Name + ": " + e.Message;
                skillEntries = skillEntries ?? new Dictionary<long, SkillEntry>();
                skillSorted = skillSorted ?? new List<SkillEntry>();
                skillJobFacet = skillJobFacet ?? new List<object>();
            }
        }
    }

    static void BuildSkills()
    {
        // String/Skill.img：<7位技能id> 子节点有 name/desc/h1..h30；3 位职业节点只有 bookName
        var strings = new Dictionary<long, ImgNode>();
        try
        {
            var root = OpenRoot("String/Skill.img");
            foreach (var node in root.Children)
            {
                if (node.Name.Length != 7 || !IsNumericName(node.Name, 0, 7)) continue;
                if (long.TryParse(node.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                    strings[id] = node;
            }
        }
        catch { /* 名称缺失只影响展示 */ }

        var entries = new Dictionary<long, SkillEntry>();
        foreach (var name in EnumerateImgNames("Skill", true))
        {
            string jobText = name.Substring(0, name.Length - 4);
            if (!int.TryParse(jobText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fileJob)) continue;
            ImgNode root;
            try { root = OpenRoot("Skill/" + name); }
            catch { continue; }
            var skillNode = root.Get("skill");
            if (skillNode == null) continue;

            foreach (var node in skillNode.Children)
            {
                if (!long.TryParse(node.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var skillId)) continue;
                if (entries.ContainsKey(skillId)) continue; // 与源端 putIfAbsent 一致：先到先得

                var entry = new SkillEntry
                {
                    Id = skillId,
                    JobId = (int)(skillId / 10000),
                    FileJob = fileJob,
                    Icon = node.Get("icon") != null,
                };

                strings.TryGetValue(skillId, out var text);
                if (text != null)
                {
                    entry.Name = text.Text("name");
                    entry.Desc = text.Text("desc");
                    for (int i = 1; i <= 30; i++)
                    {
                        string h = text.Text("h" + i).Trim();
                        if (h.Length > 0) entry.LevelDescs.Add(h);
                    }
                }
                else
                {
                    entry.Name = "";
                    entry.Desc = "";
                }

                var level = node.Get("level");
                if (level != null)
                {
                    int max = 0;
                    ImgNode first = null;
                    foreach (var lv in level.Children)
                    {
                        if (!int.TryParse(lv.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) continue;
                        if (n > max) max = n;
                        if (n == 1) first = lv;
                    }
                    if (first == null)
                        foreach (var lv in level.Children)
                            if (int.TryParse(lv.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                            { first = lv; break; }
                    entry.MaxLevel = max;
                    if (first != null) CollectScalars(first, entry.Attributes, false);
                }

                // 技能节点上的元素属性/类型（源端 readAttributes 也带这两个）
                var elem = node.Get("elemAttr");
                if (elem != null && elem.Value is string s && s.Length > 0) entry.Attributes["elemAttr"] = s;
                var skillType = node.Get("skillType");
                if (skillType != null && skillType.Value != null && skillType.Kind == null)
                    entry.Attributes["skillType"] = skillType.Value;

                entry.JobName = JobNameOf(entry.JobId);
                entries[skillId] = entry;
            }
        }

        var sorted = new List<SkillEntry>(entries.Values);
        sorted.Sort((a, b) =>
        {
            int c = a.JobId.CompareTo(b.JobId);
            return c != 0 ? c : a.Id.CompareTo(b.Id);
        });
        var counts = new Dictionary<int, int>();
        foreach (var e in sorted) counts[e.JobId] = counts.TryGetValue(e.JobId, out var n) ? n + 1 : 1;
        var jobKeys = new List<int>(counts.Keys);
        jobKeys.Sort();
        var facet = new List<object>();
        foreach (var jobId in jobKeys)
            facet.Add(AdminJson.Obj("jobId", jobId, "jobName", JobNameOf(jobId), "count", counts[jobId]));

        skillJobFacet = facet;
        skillSorted = sorted;
        skillEntries = entries;
    }

    public static CatalogPage SkillCatalog(string keyword, int? jobId, int? advancement, int pageNo, int pageSize)
    {
        EnsureSkills();
        string query = string.IsNullOrEmpty(keyword) ? "" : keyword.Trim().ToLowerInvariant();
        var matched = new List<SkillEntry>();
        foreach (var e in skillSorted)
        {
            if (jobId.HasValue && e.JobId != jobId.Value) continue;
            if (advancement.HasValue && AdvancementOf(e.JobId) != advancement.Value) continue;
            if (query.Length > 0 &&
                e.Id.ToString(CultureInfo.InvariantCulture).IndexOf(query, StringComparison.Ordinal) < 0 &&
                (e.Name ?? "").ToLowerInvariant().IndexOf(query, StringComparison.Ordinal) < 0 &&
                (e.JobName ?? "").ToLowerInvariant().IndexOf(query, StringComparison.Ordinal) < 0)
                continue;
            matched.Add(e);
        }
        // 源端技能目录按 jobId 再 id 排序（不做 matchRank）
        return Slice(matched, pageNo, Clamp(pageSize, 120, 500), SkillDto)
            .Extra("jobs", skillJobFacet)
            .Extra("loaded", skillLoadError == null)
            .Extra("message", skillLoadError ?? "");
    }

    public static Dictionary<string, object> SkillDetail(long skillId)
    {
        EnsureSkills();
        if (!skillEntries.TryGetValue(skillId, out var e))
            throw new AdminApiException("技能不存在: " + skillId);
        return SkillDto(e);
    }

    public static List<object> SkillJobs()
    {
        EnsureSkills();
        return skillJobFacet;
    }

    // 重建技能缓存（源端 SkillCatalogService.reload），返回技能总数
    public static int SkillReload()
    {
        lock (buildGate)
        {
            skillEntries = null;
            skillSorted = null;
            skillJobFacet = null;
            skillLoadError = null;
        }
        EnsureSkills();
        return skillSorted.Count;
    }

    static Dictionary<string, object> SkillDto(SkillEntry e) => AdminJson.Obj(
        "id", e.Id,
        "name", e.Name,
        "desc", e.Desc,
        "jobName", e.JobName,
        "jobId", e.JobId,
        "maxLevel", e.MaxLevel,
        "iconAvailable", e.Icon,
        "attributes", e.Attributes,
        "levelDescs", e.LevelDescs);

    // —————————— 任务名（掉落表用） ——————————
    static readonly Dictionary<long, string> questNames = new Dictionary<long, string>();

    // Quest/QuestInfo.img/<questId>/name。走 WzAssets.FileRoot（共享缓存），调用方在主线程执行。
    public static string QuestName(long questId)
    {
        if (questId <= 0) return "";
        if (questNames.TryGetValue(questId, out var cached)) return cached;
        string name = "";
        try { name = WzAssets.FileRoot("Quest/QuestInfo.img").Get(questId.ToString(CultureInfo.InvariantCulture))?.Text("name") ?? ""; }
        catch { name = ""; }
        questNames[questId] = name;
        return name;
    }

    // —————————— 图标 PNG ——————————
    // 前端 <img src="/assets/*-icons/{id}.png"> 直接吃 PNG 字节。画布来源：
    //   道具/装备  NativeItemSource.IconNode（info/icon 或 icon，含 UOL）
    //   怪物      Mob/<7位id>.img/stand/0（info/link 跟链；实测 info 下没有 icon 节点）
    //   技能      Skill/<职业>.img/skill/<id>/icon
    // 解码走 WzAssets.Canvas/Pixels（共享缓存），必须在游戏主线程执行；PNG 字节按 id 缓存。
    static readonly object iconGate = new object();
    static readonly Dictionary<string, byte[]> iconCache = new Dictionary<string, byte[]>();
    const int IconCacheMax = 512;

    public static byte[] ItemIconPng(long id) => IconPng("item:" + id, () => NativeItemSource.IconNode((int)id));
    public static byte[] EquipmentIconPng(long id) => IconPng("equip:" + id, () => NativeItemSource.IconNode((int)id));

    public static byte[] MobIconPng(long id) => IconPng("mob:" + id, () =>
    {
        string file = "Mob/" + id.ToString("D7", CultureInfo.InvariantCulture) + ".img";
        if (!WzAssets.HasFile(file)) return null;
        var root = WzAssets.FileRoot(file);
        for (int i = 0; i < 64; i++)
        {
            var link = root.Get("info/link");
            if (link == null || link.Value == null) break;
            string next = "Mob/" + Convert.ToInt64(link.Value, CultureInfo.InvariantCulture)
                .ToString("D7", CultureInfo.InvariantCulture) + ".img";
            if (!WzAssets.HasFile(next)) return null;
            root = WzAssets.FileRoot(next);
        }
        return root.Get("stand/0");
    });

    public static byte[] SkillIconPng(long id) => IconPng("skill:" + id, () =>
    {
        EnsureSkills();
        if (!skillEntries.TryGetValue(id, out var e)) return null;
        return WzAssets.Node("Skill/" + e.FileJob.ToString(CultureInfo.InvariantCulture)
            + ".img/skill/" + id.ToString(CultureInfo.InvariantCulture) + "/icon");
    });

    static byte[] IconPng(string key, Func<ImgNode> resolve)
    {
        lock (iconGate)
        {
            if (iconCache.TryGetValue(key, out var cached)) return cached;
        }
        var png = AdminMainThread.Run(() =>
        {
            try
            {
                var node = resolve();
                if (node == null) return null;
                var canvas = WzAssets.Canvas(node);
                if (canvas == null || canvas.Width <= 0 || canvas.Height <= 0) return null;
                return AdminPng.Encode(canvas.Width, canvas.Height, WzAssets.Pixels(node));
            }
            catch { return null; }
        });
        if (png == null) return null;
        lock (iconGate)
        {
            if (iconCache.Count >= IconCacheMax) iconCache.Clear(); // 简单上限，够后台用
            iconCache[key] = png;
        }
        return png;
    }
}
