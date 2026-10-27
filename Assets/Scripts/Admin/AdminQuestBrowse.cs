// 后台管理 —— 任务浏览模块（/quest/v1/*）。
//
// 算法复刻源端 gms-server 的 QuestBrowseService（照抄语义），数据源是本地 WZ：
//   任务元数据   Quest/QuestInfo.img —— 任务名 name / 三段文案 0·1·2 / 链组名 parent / 链内序号 order；
//                Quest/Check.img     —— Check/0 的 npc（起始 NPC）、lvmin、lvmax；Check/1 的 npc（结束 NPC）；
//                Quest/Act.img       —— 各段 nextQuest（后继任务，取首个非空）。
//   NPC 中文名   String/Npc.img/name。
//   地区归属     起始 NPC → 该 NPC 出现过的地图 → 该地图在 String/Map.img 里的大地区/街道。
//                与源端 attribute() 一致的细则：
//                  1) 起始 NPC 为空、或该 NPC 没出现在任何地图上 → 不归属（输出成 unknown/「未定位」）；
//                  2) 出现在多张地图时：优先「街道名明确」（streetName 非空）的地图，
//                     同级再取 mapId 数值最小的那张（确定性选取，与遍历顺序无关）；
//                  3) 地图不在 String/Map.img 目录里 → 跳过（源端 mapLookup 只认 String/Map.img）；
//                  4) 街道名为空 → "(未知街道)"；region 缺失 → "unknown"（标签「未定位」）。
//   任务链      按 QuestInfo/parent 聚合链组，组内按 order（缺省 0）→ questId 数值升序；
//               inChain = 有 parent 或 有 nextQuest 或 被别人的 nextQuest 指到（三者其一）。
//               注意：源端的 chain 只按 parent 组展开（无组时仅含自身），nextQuest 仅参与 inChain 判断。
//
// 为什么 NPC→地图走离线索引：归属要扫 6000+ 张 Map/Map/**/*.img 的 life 子树，源端首次请求
// 现扫 XML 都要 1~2 秒；本工程后台 HTTP 跑在 ThreadPool 线程上，在请求线程里扫图会把游戏卡死。
// 所以 tools/export_quest_index.py 用 wzpy 多进程预扫成 Resources/Quest/QuestBrowseIndex.json
// （npcMaps + mapInfo，mapInfo 只存 region/town 两个短字符串控制体积），运行时只解析这份 JSON；
// Quest/*.img 是纯属性树（无 Canvas），现读很便宜。
//
// 纯 C#：不直接引用 UnityEngine（Unity 侧读 Resources 用 #if 包住，与 AdminBackend 同款约定）。
// WZ 访问统一经 AdminMainThread.Run 回游戏主线程：WzAssets 的根节点缓存不是线程安全的，
// 后台线程不能与游戏主线程同时首载 IMG（与 AdminBackend 把 DB 访问放回主线程是同一个理由）。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MapleDash.Wz;

public static class AdminQuestBrowse
{
    // —————————— 与源端 QuestBrowseService 对齐的常量 ——————————
    const string UnknownRegion = "unknown";
    const string UnknownRegionLabel = "未定位";
    const string UnknownStreet = "(未知街道)";

    // 大地区 key → 中文显示名（照抄源端 REGION_NAMES；新出现的 key 直接显示 key 本身）
    static readonly Dictionary<string, string> RegionNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        { "victoria", "金银岛" },
        { "ossyria", "神秘岛" },
        { "elin", "艾琳森林" },
        { "china", "东方神州" },
        { "jp", "日本" },
        { "thai", "泰国" },
        { "singapore", "新加坡·马来西亚" },
        { "maple", "彩虹岛" },
        { "MasteriaGL", "马斯特里亚" },
        { "weddingGL", "婚礼村" },
        { "HalloweenGL", "万圣节" },
        { "Episode1GL", "剧情活动" },
        { "event", "活动地图" },
        { "etc", "其他" },
        { "grandis", "格兰蒂斯" },
    };

    // —————————— 内部数据结构 ——————————

    // 单条任务记录（内部结构，聚合后转 DTO）
    sealed class QuestRecord
    {
        public string QuestId;
        public string Name = "";
        public int? LevelMin, LevelMax;
        public string StartNpcId, EndNpcId;
        public string ContentStart, ContentProgress, ContentComplete;
        public string ParentName;
        public int? Order;
        public string NextQuestId;
        public string Region, Town;     // 归属结果；null = 未定位（输出成 unknown / (未知街道)）
    }

    // String/Map.img 里的地图目录项
    sealed class MapEntry
    {
        public string Region, Town;
    }

    // 全量索引（构建一次后缓存）
    sealed class QuestIndex
    {
        public readonly List<QuestRecord> Quests = new List<QuestRecord>();
        public readonly Dictionary<string, QuestRecord> ById = new Dictionary<string, QuestRecord>(StringComparer.Ordinal);
        // 链组名（QuestInfo/parent）→ 按 order→questId 排好序的任务 id 列表
        public readonly Dictionary<string, List<string>> ChainGroups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        // 被别的任务 nextQuest 引用到的任务 id（用于 inChain 判断）
        public readonly HashSet<string> NextQuestTargets = new HashSet<string>(StringComparer.Ordinal);
        // 来自 QuestBrowseIndex.json：NPC→地图集合、地图→地区/街道
        public Dictionary<string, List<string>> NpcMaps = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        public Dictionary<string, MapEntry> MapInfo = new Dictionary<string, MapEntry>(StringComparer.Ordinal);
        // NPC id → 中文名（String/Npc.img）
        public Dictionary<string, string> NpcNames = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    static QuestIndex cache;
    static readonly object gate = new object();

    // —————————— 路由注册 ——————————
    public static void Register(AdminApi api)
    {
        // 地区树（前端 a-cascader 的 options）：大地区 → 城镇/街道 两级，带任务数
        api.Get("/quest/v1/townTree", ctx => TownTree());

        // 任务列表：按 region/town 筛选（空 = 全部），按最低等级（无限制按 0）→ questId 升序
        api.Post("/quest/v1/list", ctx => QuestList(ctx.BodyString("region"), ctx.BodyString("town")));

        // 任务详情：三段文案 + 任务链（parent 组，无组时仅自身）
        api.Post("/quest/v1/detail", ctx => QuestDetail(ctx.BodyString("questId")));
    }

    // —————————— 索引（懒加载、进程内缓存） ——————————

    static QuestIndex Index()
    {
        lock (gate)
        {
            // 20s 上限：首次构建要解析 Quest/*.img + 索引 JSON，慢设备给足余量
            if (cache == null) cache = AdminMainThread.Run(BuildIndex, 20000);
            return cache;
        }
    }

    static QuestIndex BuildIndex()
    {
        var idx = new QuestIndex();
        LoadMapIndex(idx);          // 离线产物：NPC→地图、地图→地区/街道
        LoadNpcNames(idx);          // String/Npc.img
        LoadQuestInfo(idx);         // Quest/QuestInfo.img：全集 + 文案/parent/order
        LoadCheck(idx);             // Quest/Check.img：起止 NPC + 等级
        LoadAct(idx);               // Quest/Act.img：nextQuest
        Attribute(idx);             // 地区归属
        AggregateChains(idx);       // 链组聚合
        return idx;
    }

    // QuestBrowseIndex.json（tools/export_quest_index.py 的产物）：
    //   { "npcMaps": { "<npcId>": [mapId, ...] }, "mapInfo": { "<mapId>": {"region","town"} } }
    // 缺产物时明确报错，而不是让所有任务静默落进「未定位」。
    static void LoadMapIndex(QuestIndex idx)
    {
        string text = LoadIndexText();
        if (string.IsNullOrEmpty(text))
            throw new AdminApiException("缺少任务浏览索引 Quest/QuestBrowseIndex.json，请先运行 tools/export_quest_index.py 生成");
        var root = AdminJson.Parse(text) as Dictionary<string, object>;
        if (root == null) throw new AdminApiException("任务浏览索引无法解析（Quest/QuestBrowseIndex.json 损坏）");

        var rawNpc = AdminJson.GetDict(root, "npcMaps");
        if (rawNpc != null)
        {
            foreach (var kv in rawNpc)
            {
                var maps = new List<string>();
                var list = AdminJson.AsList(kv.Value);
                if (list != null)
                    foreach (var m in list) maps.Add(NormId(Convert.ToString(m, CultureInfo.InvariantCulture)));
                idx.NpcMaps[NormId(kv.Key)] = maps;
            }
        }

        var rawMaps = AdminJson.GetDict(root, "mapInfo");
        if (rawMaps != null)
        {
            foreach (var kv in rawMaps)
            {
                var d = AdminJson.AsDict(kv.Value);
                if (d == null) continue;
                idx.MapInfo[NormId(kv.Key)] = new MapEntry
                {
                    Region = AdminJson.GetString(d, "region"),
                    Town = AdminJson.GetString(d, "town"),
                };
            }
        }
    }

    // NPC id → 中文名。源端合并 zh-CN/base 两份 Npc.img；本工程只有一份 Data，直接读。
    static void LoadNpcNames(QuestIndex idx)
    {
        var root = WzRoot("String/Npc.img");
        foreach (var n in root.Children)
        {
            string name = n.Text("name");
            if (!string.IsNullOrEmpty(name)) idx.NpcNames[NormId(n.Name)] = name;
        }
    }

    // QuestInfo：QuestInfo 中出现的任务就是全集（与源端 loadQuestInfo 一致）。
    static void LoadQuestInfo(QuestIndex idx)
    {
        var root = WzRoot("Quest/QuestInfo.img");
        foreach (var q in root.Children)
        {
            var r = new QuestRecord
            {
                QuestId = q.Name,
                // 源端 name 取 orEmpty(getVal)：缺失时是空串而不是 null
                Name = StrVal(q.Get("name")) ?? "",
                ContentStart = StrVal(q.Get("0")),
                ContentProgress = StrVal(q.Get("1")),
                ContentComplete = StrVal(q.Get("2")),
                ParentName = StrVal(q.Get("parent")),
                Order = TryInt(q.Get("order")),
            };
            idx.Quests.Add(r);
            idx.ById[r.QuestId] = r;
        }
    }

    // Check：Check/0 的 npc(起始)/lvmin/lvmax，Check/1 的 npc(结束)。
    static void LoadCheck(QuestIndex idx)
    {
        var root = WzRoot("Quest/Check.img");
        foreach (var q in root.Children)
        {
            QuestRecord r;
            if (!idx.ById.TryGetValue(q.Name, out r)) continue;
            var start = q.Get("0");
            if (start != null)
            {
                r.StartNpcId = IdVal(start.Get("npc"));
                r.LevelMin = TryInt(start.Get("lvmin"));
                r.LevelMax = TryInt(start.Get("lvmax"));
            }
            var end = q.Get("1");
            if (end != null) r.EndNpcId = IdVal(end.Get("npc"));
        }
    }

    // Act：遍历各奖励段，取首个非空 nextQuest（与源端 loadAct 的「取首次出现」一致）。
    static void LoadAct(QuestIndex idx)
    {
        var root = WzRoot("Quest/Act.img");
        foreach (var q in root.Children)
        {
            QuestRecord r;
            if (!idx.ById.TryGetValue(q.Name, out r)) continue;
            foreach (var act in q.Children)
            {
                string nq = IdVal(act.Get("nextQuest"));
                if (!string.IsNullOrEmpty(nq)) { r.NextQuestId = nq; break; }
            }
        }
    }

    // 地区归属：起始 NPC → 出现地图（优先街道名明确、同级取 mapId 小）→ 地区/街道。
    // 与源端 attribute() 逐条对应；地图不在 mapInfo（= 不在 String/Map.img）里时跳过。
    static void Attribute(QuestIndex idx)
    {
        foreach (var q in idx.Quests)
        {
            if (string.IsNullOrEmpty(q.StartNpcId)) continue;
            List<string> maps;
            if (!idx.NpcMaps.TryGetValue(NormId(q.StartNpcId), out maps) || maps.Count == 0) continue;

            string bestMap = null, bestStreet = null;
            foreach (var mid in maps)
            {
                MapEntry info;
                if (!idx.MapInfo.TryGetValue(mid, out info)) continue;
                string street = string.IsNullOrEmpty(info.Town) ? UnknownStreet : info.Town;
                if (bestMap == null) { bestMap = mid; bestStreet = street; continue; }

                bool curUnknown = street == UnknownStreet;
                bool bestUnknown = bestStreet == UnknownStreet;
                if (bestUnknown && !curUnknown)
                {
                    bestMap = mid; bestStreet = street;
                }
                else if (curUnknown == bestUnknown && QuestNum(mid) < QuestNum(bestMap))
                {
                    // 同属有街道名或同属无街道名：取 mapId 较小者（确定性选取）
                    bestMap = mid; bestStreet = street;
                }
            }
            if (bestMap == null) continue;
            q.Town = bestStreet;
            string region = idx.MapInfo[bestMap].Region;
            q.Region = string.IsNullOrEmpty(region) ? UnknownRegion : region;
        }
    }

    // 链聚合：按 parent 分组（组内 order→questId 排序）；记录被 nextQuest 指到的任务。
    static void AggregateChains(QuestIndex idx)
    {
        var groups = new Dictionary<string, List<QuestRecord>>(StringComparer.Ordinal);
        foreach (var q in idx.Quests)
        {
            if (!string.IsNullOrEmpty(q.ParentName))
            {
                if (!groups.TryGetValue(q.ParentName, out var list)) groups[q.ParentName] = list = new List<QuestRecord>();
                list.Add(q);
            }
            if (!string.IsNullOrEmpty(q.NextQuestId)) idx.NextQuestTargets.Add(q.NextQuestId);
        }
        foreach (var kv in groups)
        {
            var list = kv.Value;
            list.Sort((a, b) =>
            {
                int c = (a.Order ?? 0).CompareTo(b.Order ?? 0);
                return c != 0 ? c : QuestNum(a.QuestId).CompareTo(QuestNum(b.QuestId));
            });
            var ids = new List<string>(list.Count);
            foreach (var q in list) ids.Add(q.QuestId);
            idx.ChainGroups[kv.Key] = ids;
        }
    }

    static bool IsInChain(QuestIndex idx, QuestRecord q)
    {
        if (!string.IsNullOrEmpty(q.ParentName)) return true;
        if (!string.IsNullOrEmpty(q.NextQuestId)) return true;
        return idx.NextQuestTargets.Contains(q.QuestId);
    }

    // —————————— 接口 1：地区树 ——————————

    sealed class TownCount
    {
        public string Town;
        public int Count;
    }

    sealed class RegionCount
    {
        public string Region;
        public int Total;
        public readonly List<TownCount> Towns = new List<TownCount>();
    }

    // 源端 townTree：region → town 两级；region 顺序 = 任务遍历首次出现序（源端用 LinkedHashMap，
    // 注释写「保持 Map.img 顺序」但实现是插入序 —— 这里照抄实现行为），
    // town 按任务数降序（同数保持出现序，源端 stream.sorted 稳定）。
    static List<object> TownTree()
    {
        var idx = Index();
        var regions = new List<RegionCount>();
        var regionAt = new Dictionary<string, RegionCount>(StringComparer.Ordinal);

        foreach (var q in idx.Quests)
        {
            string region = q.Region ?? UnknownRegion;
            string town = q.Town ?? UnknownStreet;
            RegionCount agg;
            // 只在新建时入列，保持「任务首次出现」的插入顺序（源端 LinkedHashMap 语义）
            if (!regionAt.TryGetValue(region, out agg))
            {
                agg = new RegionCount { Region = region };
                regionAt[region] = agg;
                regions.Add(agg);
            }

            var towns = agg.Towns;
            TownCount hit = null;
            foreach (var t in towns)
                if (t.Town == town) { hit = t; break; }
            if (hit == null) towns.Add(hit = new TownCount { Town = town });
            hit.Count++;
            agg.Total++;
        }

        var outList = new List<object>(regions.Count);
        foreach (var r in regions)
        {
            // town 按 count 降序；List.Sort 不稳定，用原始序号兜底保持「同数按出现序」
            var order = new List<int>(r.Towns.Count);
            for (int i = 0; i < r.Towns.Count; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                int c = r.Towns[b].Count.CompareTo(r.Towns[a].Count);
                return c != 0 ? c : a.CompareTo(b);
            });

            var children = new List<object>(r.Towns.Count);
            foreach (var i in order)
            {
                var t = r.Towns[i];
                // 城镇是叶子：isLeaf=true，children 留空（源端 DTO 未设置 → null）
                children.Add(AdminJson.Obj(
                    "value", t.Town,
                    "label", t.Town + " (" + t.Count + ")",
                    "count", t.Count,
                    "isLeaf", true,
                    "children", null));
            }
            outList.Add(AdminJson.Obj(
                "value", r.Region,
                "label", RegionLabel(r.Region) + " (" + r.Total + ")",
                "count", r.Total,
                "isLeaf", null,
                "children", children));
        }
        return outList;
    }

    // —————————— 接口 2：任务列表 ——————————

    static List<object> QuestList(string region, string town)
    {
        var idx = Index();
        bool allRegion = string.IsNullOrEmpty(region);
        bool allTown = string.IsNullOrEmpty(town);
        var matched = new List<QuestRecord>();
        foreach (var q in idx.Quests)
        {
            if (!allRegion && region != (q.Region ?? UnknownRegion)) continue;
            if (!allTown && town != (q.Town ?? UnknownStreet)) continue;
            matched.Add(q);
        }
        // 源端排序：levelMin 升序（null 按 0）→ questId 数值升序
        matched.Sort((a, b) =>
        {
            int c = (a.LevelMin ?? 0).CompareTo(b.LevelMin ?? 0);
            return c != 0 ? c : QuestNum(a.QuestId).CompareTo(QuestNum(b.QuestId));
        });

        var outList = new List<object>(matched.Count);
        foreach (var q in matched) outList.Add(SummaryDto(idx, q));
        return outList;
    }

    // 字段名与 AdminUi/src/api/questBrowse.ts 的 QuestSummary 逐字一致（camelCase，questId 字符串）
    static Dictionary<string, object> SummaryDto(QuestIndex idx, QuestRecord q)
    {
        return AdminJson.Obj(
            "questId", q.QuestId,
            "name", q.Name,
            "levelMin", q.LevelMin,
            "levelMax", q.LevelMax,
            "startNpcId", q.StartNpcId,
            "startNpcName", NpcName(idx, q.StartNpcId),
            "endNpcId", q.EndNpcId,
            "endNpcName", NpcName(idx, q.EndNpcId),
            "inChain", IsInChain(idx, q),
            "region", q.Region ?? UnknownRegion,
            "town", q.Town ?? UnknownStreet);
    }

    // —————————— 接口 3：任务详情 ——————————

    static Dictionary<string, object> QuestDetail(string questId)
    {
        var idx = Index();
        string id = questId == null ? "" : questId.Trim();
        QuestRecord q;
        if (id.Length == 0 || !idx.ById.TryGetValue(id, out q))
            throw new AdminApiException("任务不存在: " + id);

        // 链：parent 组成员（组内已按 order→questId 排序）；没有 parent 组时仅含自身。
        // 与源端 detail() 一致 —— nextQuest 不串链，只影响列表页的 inChain 标记。
        var chain = new List<object>();
        List<string> members = null;
        if (!string.IsNullOrEmpty(q.ParentName)) idx.ChainGroups.TryGetValue(q.ParentName, out members);
        if (members == null || members.Count == 0) members = new List<string> { q.QuestId };
        foreach (var mid in members)
        {
            QuestRecord m;
            if (!idx.ById.TryGetValue(mid, out m)) continue;
            chain.Add(AdminJson.Obj(
                "questId", m.QuestId,
                "name", m.Name,
                "levelMin", m.LevelMin,
                "current", m.QuestId == q.QuestId));
        }

        // 字段名与 questBrowse.ts 的 QuestDetail/QuestChainItem 逐字一致
        return AdminJson.Obj(
            "questId", q.QuestId,
            "name", q.Name,
            "levelMin", q.LevelMin,
            "levelMax", q.LevelMax,
            "startNpcId", q.StartNpcId,
            "startNpcName", NpcName(idx, q.StartNpcId),
            "endNpcId", q.EndNpcId,
            "endNpcName", NpcName(idx, q.EndNpcId),
            "contentStart", q.ContentStart,
            "contentProgress", q.ContentProgress,
            "contentComplete", q.ContentComplete,
            "parentName", q.ParentName,
            "order", q.Order,
            "chain", chain,
            "region", q.Region ?? UnknownRegion,
            "town", q.Town ?? UnknownStreet);
    }

    // —————————— 工具 ——————————

    static string RegionLabel(string key)
    {
        if (key == UnknownRegion) return UnknownRegionLabel;
        string label;
        return RegionNames.TryGetValue(key, out label) ? label : key;
    }

    static string NpcName(QuestIndex idx, string npcId)
    {
        if (string.IsNullOrEmpty(npcId)) return null;
        string name;
        return idx.NpcNames.TryGetValue(NormId(npcId), out name) ? name : null;
    }

    // 去前导零归一 id（非数字原样返回）；NPC/地图 id 在 WZ 里可能是 int 也可能是字符串，
    // 索引 JSON 的 key 是归一后的十进制字符串，两边都过一遍 NormId 才能对上。
    static string NormId(string id)
    {
        if (string.IsNullOrEmpty(id)) return id;
        long v;
        return long.TryParse(id.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)
            ? v.ToString(CultureInfo.InvariantCulture) : id;
    }

    static long QuestNum(string id)
    {
        long v;
        return long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0L;
    }

    // id 类字段（npc / nextQuest）：值是 int 或字符串，统一转成归一十进制字符串；缺失 → null
    static string IdVal(ImgNode node)
    {
        var v = node == null ? null : node.Value;
        if (v == null) return null;
        return NormId(Convert.ToString(v, CultureInfo.InvariantCulture));
    }

    // 文案类字段（name / 0 / 1 / 2 / parent）：缺失 → null（源端 getVal 同语义），存在则转字符串
    static string StrVal(ImgNode node)
    {
        var v = node == null ? null : node.Value;
        if (v == null) return null;
        return v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture);
    }

    // 等级/序号：缺失或不可解析 → null（源端 getInt(def=null) 同语义）
    static int? TryInt(ImgNode node)
    {
        var v = node == null ? null : node.Value;
        if (v == null) return null;
        try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
        catch (Exception) { return null; }
    }

    // —————————— 资源读取 ——————————

    static ImgNode WzRoot(string relative)
    {
#if UNITY_EDITOR || UNITY_5_3_OR_NEWER
        return WzAssets.FileRoot(relative);
#else
        // 离线工具链（无 Unity）直接读磁盘 Data 目录，规则与 WzAssets.DataRoot 一致
        var root = Environment.GetEnvironmentVariable("MAPLEDASH_IMG_ROOT");
        return new ImgFile(Path.Combine(string.IsNullOrEmpty(root) ? "Data" : root, relative)).Root;
#endif
    }

    static string LoadIndexText()
    {
#if UNITY_EDITOR || UNITY_5_3_OR_NEWER
        var asset = UnityEngine.Resources.Load<UnityEngine.TextAsset>("Quest/QuestBrowseIndex");
        if (asset != null && !string.IsNullOrEmpty(asset.text)) return asset.text;
#endif
        // 离线工具链兜底：直接读仓库里的产物
        var path = Path.Combine("Assets", "Resources", "Quest", "QuestBrowseIndex.json");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}
