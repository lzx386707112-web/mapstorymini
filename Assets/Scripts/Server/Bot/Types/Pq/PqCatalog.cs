using System;
using System.Collections.Generic;

namespace Server.Bot.Types.Pq
{
    // 组队任务的阶段动作。假人只会做这几种「看得见」的事，具体数量/对象来自数据表，
    // 逻辑一律回源端脚本核对（event/KerningPQ.js、LudiPQ.js、EllinPQ.js、PiratePQ.js
    // 以及对应的 npc/portal/reactor 脚本），这里不自行发明条件。
    public enum PqAction
    {
        /// <summary>清光这张图上的怪（源端 portal 脚本 getMonsters().size()==0）。</summary>
        ClearMobs,
        /// <summary>只打指定怪，打到 need 只为止。</summary>
        KillMobs,
        /// <summary>砸反应堆（按模板 id 列表或 id 区间）。</summary>
        BreakReactors,
        /// <summary>站到指定矩形中心（废都 2/3/4 阶段的绳子/平台/木桶）。</summary>
        Stand,
        /// <summary>把物品补给队长（源端是队员把通行证交给队长，这里直接落到队长背包）。</summary>
        GrantItem,
        /// <summary>打 BOSS。</summary>
        KillBoss,
        /// <summary>只能由队长触发的 NPC 对话（静默代点）。</summary>
        Talk,
        /// <summary>假人以队员身份找 NPC 领题（废都第一关：gridCheck → 随机题目）。</summary>
        BotAsk,
        /// <summary>假人打怪 + 自己捡地上的券，直到每人手上凑齐题目要求的张数。</summary>
        BotCollect,
        /// <summary>假人拿券回 NPC 换成通行证（队员分支）。</summary>
        BotExchange,
        /// <summary>假人站到 NPC 旁边等队长。</summary>
        BotWait,
        /// <summary>假人把凑到的证交给队长（从假人背包扣，落到队长背包）。</summary>
        HandOver,
        /// <summary>按阶段随机组合站位，再由队长核对（废都 2/3/4）；核对通过才继续。</summary>
        StageCheck,
    }

    public sealed class PqStep
    {
        public PqAction action;
        public int[] ids = System.Array.Empty<int>();      // 怪 / 反应堆模板 id
        public int[] reactors = System.Array.Empty<int>(); // BotCollect：要砸的箱子模板 id
        public int[] maps = System.Array.Empty<int>();     // 要分散到的图（空 = 本阶段主图）
        public int minId, maxId;                           // 反应堆 id 区间（0 = 不用）
        public int need;                                   // 数量要求（BotCollect：全队合计）
        public int itemId, itemCount;                      // GrantItem / HandOver / BotCollect 的目标物品
        public int npc;                                    // Talk
        public bool openOnly;                              // 只开一次对话（海盗船 2094002 / 各关引言页）
        public int rounds = 1;                             // Talk 重复轮数
        public string[] prefer;                            // 对话选项偏好关键字
        public List<PqRect> stands = new List<PqRect>();   // Stand / StageCheck（必须初始化：数据表用集合初始化器写）
        public string comboProperty;                       // StageCheck：阶段随机组合写在哪个实例属性上
        /// <summary>对话后要等实例属性变成这个值才算真正过关（null = 只要不为 null）。</summary>
        public string awaitProperty, awaitValue;
        public string say;                                 // 做这件事时假人说的话
        public long timeoutMs;                             // 0 = 用默认步骤超时；攒券这类要放宽
    }

    public struct PqRect
    {
        public int x, y, w, h;
        public PqRect(int x, int y, int w, int h) { this.x = x; this.y = y; this.w = w; this.h = h; }
        public int CenterX => x + w / 2;
        public int CenterY => y + h / 2;
    }

    public sealed class PqStage
    {
        public int mapId;
        public int stageNo;              // 从 1 开始，对应源端 "Nstageclear" 属性
        public string title;             // 假人喊话/日志用
        public List<PqStep> steps = new List<PqStep>();
        /// <summary>本阶段的 NPC（静默代点），0 = 本阶段不靠 NPC。</summary>
        public int npc;
        /// <summary>队长持有这个物品才算过关（源端 haveItem 判定）；0 = 不看物品。</summary>
        public int requireItem, requireCount;
        /// <summary>
        /// 活干完之后等多久再强制推进（毫秒）。有些阶段要玩家自己踩脚本传送门
        /// （海盗船 next00 会顺手刷 BOSS），多给点时间；0 = 默认 1.5s。
        /// </summary>
        public long gateMs;
    }

    public sealed class PqDef
    {
        public string key;               // KPQ / LPQ / EPQ / PPQ
        public string name;              // 中文名（喊话/提示）
        public string eventName;         // 事件脚本名（em.startInstance 用的管理器名）
        public int recruitMap;           // 招募图（假人常驻、组队的地方）
        public int entryMap;             // 进本第一张图
        public int exitMap;              // 出口图
        public int recruitNpc;           // 招募图上的组队任务 NPC（假人围着他站）
        public int minLevel;
        public string[] keywords;        // 触发组队的喊话关键词
        public List<PqStage> stages = new List<PqStage>();
        /// <summary>不在阶段链上、但假人要去干活的子图（玩具塔的分层子图、黑暗塔那五间）。</summary>
        public List<int> extraMaps = new List<int>();

        public PqStage Stage(int mapId)
        {
            foreach (var stage in stages) if (stage.mapId == mapId) return stage;
            return null;
        }
    }

    // 四个副本的静态数据。数值全部来自源端脚本，勿凭印象改：
    //   KPQ  event/KerningPQ.js:28-35（3~6 人、21 级、入口 103000800、出口 103000890、奖励 103000805）
    //   LPQ  event/LudiPQ.js:26-33（1~6 人、35 级、入口 922010100、出口 922010000、奖励 922011000/922011100）
    //   EPQ  event/EllinPQ.js:29-33（45 级、入口 930000000、出口 930000800）
    //   PPQ  event/PiratePQ.js:30-35（55 级、入口 925100000、出口 925100700、奖励 925100600）
    public static class PqCatalog
    {
        public const int PassCoupon = 4001007;   // 废都：优惠券（换通行证用）
        public const int PassTicket = 4001008;   // 废都：通行证（交给 Cloto）
        public const int LudiPass = 4001022;     // 玩具塔：次元的通行证
        public const int LudiKey = 4001023;      // 玩具塔：次元的钥匙（BOSS 掉）
        public const int EllinStone = 4001163;   // 毒雾森林：紫色魔力石
        public const int PirateLow = 4001120, PirateMid = 4001121, PirateHigh = 4001122;

        static readonly Dictionary<string, PqDef> all = new Dictionary<string, PqDef>();

        // ————— 废都第一关的题库（npc/9020001.js:30-38，题目号从 1 开始）—————
        // 假人领题后 eim.gridCheck 拿到题目号，按这张表才知道要收几张优惠券。
        // 移植端不重写脚本，这张表只是把脚本里的 stage1Answers 抄给 C# 侧读。
        static readonly int[] Stage1Answers = { 10, 35, 20, 25, 25, 30, 8 };
        public static int Stage1Answer(int question)
            => question >= 1 && question <= Stage1Answers.Length ? Stage1Answers[question - 1] : 0;

        // ————— 废都 2/3/4 关的随机组合（npc/9020001.js:49-60）—————
        // 1 = 这个位置要站 1 个人，0 = 不能站。脚本把抽中的行号写进 stgNProperty，
        // 假人按行号摆位，队长再点一次 NPC 核对。
        static readonly int[][] Stage2Combos =
        {
            new[] { 0, 1, 1, 1 }, new[] { 1, 0, 1, 1 }, new[] { 1, 1, 0, 1 }, new[] { 1, 1, 1, 0 },
        };
        static readonly int[][] Stage3Combos =
        {
            new[] { 0, 0, 1, 1, 1 }, new[] { 0, 1, 0, 1, 1 }, new[] { 0, 1, 1, 0, 1 }, new[] { 0, 1, 1, 1, 0 },
            new[] { 1, 0, 0, 1, 1 }, new[] { 1, 0, 1, 0, 1 }, new[] { 1, 0, 1, 1, 0 }, new[] { 1, 1, 0, 0, 1 },
            new[] { 1, 1, 0, 1, 0 }, new[] { 1, 1, 1, 0, 0 },
        };
        static readonly int[][] Stage4Combos =
        {
            new[] { 0, 0, 0, 1, 1, 1 }, new[] { 0, 0, 1, 0, 1, 1 }, new[] { 0, 0, 1, 1, 0, 1 }, new[] { 0, 0, 1, 1, 1, 0 },
            new[] { 0, 1, 0, 0, 1, 1 }, new[] { 0, 1, 0, 1, 0, 1 }, new[] { 0, 1, 0, 1, 1, 0 }, new[] { 0, 1, 1, 0, 0, 1 },
            new[] { 0, 1, 1, 0, 1, 0 }, new[] { 0, 1, 1, 1, 0, 0 }, new[] { 1, 0, 0, 0, 1, 1 }, new[] { 1, 0, 0, 1, 0, 1 },
            new[] { 1, 0, 0, 1, 1, 0 }, new[] { 1, 0, 1, 0, 0, 1 }, new[] { 1, 0, 1, 0, 1, 0 }, new[] { 1, 0, 1, 1, 0, 0 },
            new[] { 1, 1, 0, 0, 0, 1 }, new[] { 1, 1, 0, 0, 1, 0 }, new[] { 1, 1, 0, 1, 0, 0 }, new[] { 1, 1, 1, 0, 0, 0 },
        };
        public static int[] Combo(string property, int index)
        {
            int[][] table = property == "stg2Property" ? Stage2Combos
                : property == "stg3Property" ? Stage3Combos
                : property == "stg4Property" ? Stage4Combos : null;
            if (table == null || index < 0 || index >= table.Length) return null;
            return table[index];
        }

        public static IEnumerable<PqDef> All => all.Values;
        public static PqDef Get(string key) => key != null && all.TryGetValue(key, out var def) ? def : null;
        public static PqDef ForMap(int mapId)
        {
            foreach (var def in all.Values) if (def.recruitMap == mapId) return def;
            return null;
        }

        /// <summary>这张图是不是某个副本的阶段图（玩家自己跑进副本时用它找回对应的副本）。</summary>
        public static PqDef ByStageMap(int mapId)
        {
            foreach (var def in all.Values)
                foreach (var stage in def.stages)
                    if (stage.mapId == mapId) return def;
            return null;
        }
        static PqCatalog() { Add(Kerning()); Add(Ludi()); Add(Ellin()); Add(Pirate()); }

        static void Add(PqDef def) { if (def != null) all[def.key] = def; }

        static PqStep Clear(string say) => new PqStep { action = PqAction.ClearMobs, say = say };
        static PqStep Stand(string say, params PqRect[] rects)
            => new PqStep { action = PqAction.Stand, stands = new List<PqRect>(rects), need = Math.Min(3, rects.Length), say = say };
        static PqStep Give(int item, int count, string say)
            => new PqStep { action = PqAction.GrantItem, itemId = item, itemCount = count, say = say };

        static int[] Ids(params int[] ids) => ids ?? Array.Empty<int>();

        /// <summary>队长点一次 NPC。openOnly = 只走引言页（脚本的 statusStgN: -1 → 0），不做判定。</summary>
        static PqStep Talk(int npc, string say, bool openOnly = false, string await = null, string value = null)
            => new PqStep { action = PqAction.Talk, npc = npc, openOnly = openOnly, say = say,
                            awaitProperty = await, awaitValue = value, timeoutMs = 60000 };

        /// <summary>
        /// 假人自己去把 item 凑够 need 张：打 mobs 里的怪或砸 reactors 里的箱子。
        /// 箱子砸开掉什么、刷什么怪，全由反应堆自己的 reactor/&lt;id&gt;.js 决定
        /// （2202003 dropItems 掉券、2201001 spawnMonster 刷怪），这里不另设掉落。
        /// maps 非空时按人头分散到这些图（玩具塔 5 关的六个子图那种）。
        /// </summary>
        static PqStep Collect(int item, int need, int[] mobs, int[] reactors, int[] maps, string say,
            long timeoutMs = 240000)
            => new PqStep { action = PqAction.BotCollect, itemId = item, need = need,
                            ids = mobs ?? Array.Empty<int>(), reactors = reactors ?? Array.Empty<int>(),
                            maps = maps ?? Array.Empty<int>(), say = say, timeoutMs = timeoutMs };

        /// <summary>队员把攒到的物品交给队长（源端是当面交易，这里直接落到队长背包）。</summary>
        static PqStep HandOver(int item, int count, string say)
            => new PqStep { action = PqAction.HandOver, itemId = item, itemCount = count, say = say };

        /// <summary>阶段过关标记（"Nstageclear"）：NPC 真的 clearStage 之后才算这一步完成。</summary>
        static string Cleared(int stageNo) => stageNo + "stageclear";

        // ————————————————————— 废弃都市组队任务（KPQ）—————————————————————
        static PqDef Kerning()
        {
            var def = new PqDef
            {
                key = "KPQ", name = "废弃都市", eventName = "KerningPQ",
                recruitMap = 103000000, entryMap = 103000800, exitMap = 103000890,
                recruitNpc = 9020000, minLevel = 21,
                keywords = new[] { "求组", "缺一", "缺二", "缺三", "q1", "q2", "组队", "废都", "下水道", "通行证" },
            };
            // 阶段 1：完全按 npc/9020001.js 的队员分支走 ——
            //   领题（gridCheck=-1 → 随机题目）→ 打鳄鱼捡券 → 回 Cloto 换 1 张通行证
            //   → 站 NPC 旁边等队长 → 把证交给队长。
            // 队长点 Cloto 时脚本自己算 numpasses = eim.getPlayerCount()-1（假人已登记进实例），
            // 所以这里不预设数量：队员真实交上来几张，队长手上就有几张，不够脚本会拦。
            def.stages.Add(new PqStage
            {
                mapId = 103000800, stageNo = 1, npc = 9020001, title = "第一阶段：收集通行证",
                steps = {
                    new PqStep { action = PqAction.BotAsk, npc = 9020001, timeoutMs = 20000, say = "我先去 Cloto 那里领题" },
                    new PqStep { action = PqAction.BotCollect, ids = new[] { 9300001 }, timeoutMs = 300000,
                        say = "券我来捡，题目要几张就收几张" },
                    new PqStep { action = PqAction.BotExchange, npc = 9020001, timeoutMs = 90000,
                        say = "券齐了，回 Cloto 换通行证" },
                    new PqStep { action = PqAction.BotWait, npc = 9020001, say = "我在 Cloto 旁边等队长来收" },
                    new PqStep { action = PqAction.HandOver, itemId = PassTicket, say = "通行证交给队长" },
                },
            });
            // 阶段 2/3/4：源端第一次对话只发规则 + 抽组合，第二次才核对（npc/9020001.js:181-257）。
            // 假人按抽中的组合摆位，队长核对；核对不过就重摆重试，仍不过就按源端
            // 「人数不足 3 人直接放行」的单机口径退出实例成员身份。
            def.stages.Add(new PqStage
            {
                mapId = 103000801, stageNo = 2, title = "第二阶段：挂绳子",
                steps = {
                    new PqStep { action = PqAction.Talk, npc = 9020001, say = "队长去听第二阶段的规则" },
                    new PqStep { action = PqAction.StageCheck, npc = 9020001, comboProperty = "stg2Property",
                        say = "我按绳子组合挂上去",
                        stands = {
                            new PqRect(-755, -132, 4, 218), new PqRect(-721, -340, 4, 166),
                            new PqRect(-586, -326, 4, 150), new PqRect(-483, -181, 4, 222) } },
                },
            });
            def.stages.Add(new PqStage
            {
                mapId = 103000802, stageNo = 3, title = "第三阶段：站平台",
                steps = {
                    new PqStep { action = PqAction.Talk, npc = 9020001, say = "队长去听第三阶段的规则" },
                    new PqStep { action = PqAction.StageCheck, npc = 9020001, comboProperty = "stg3Property",
                        say = "平台我站中间那一格",
                        stands = {
                            new PqRect(608, -180, 140, 50), new PqRect(791, -117, 140, 45),
                            new PqRect(958, -180, 140, 50), new PqRect(876, -238, 140, 45),
                            new PqRect(702, -238, 140, 45) } },
                },
            });
            def.stages.Add(new PqStage
            {
                mapId = 103000803, stageNo = 4, title = "第四阶段：踩木桶",
                steps = {
                    new PqStep { action = PqAction.Talk, npc = 9020001, say = "队长去听第四阶段的规则" },
                    new PqStep { action = PqAction.StageCheck, npc = 9020001, comboProperty = "stg4Property",
                        say = "我踩木桶",
                        stands = {
                            new PqRect(910, -236, 35, 5), new PqRect(877, -184, 35, 5),
                            new PqRect(946, -184, 35, 5), new PqRect(845, -132, 35, 5),
                            new PqRect(910, -132, 35, 5), new PqRect(981, -132, 35, 5) } },
                },
            });
            // 阶段 5：BOSS 掉 4001008（9300000/2/3 各 999999/999999 必掉），队员捡满 10 张交给队长，
            // 队长点 Cloto 交 10 张 → clearStage + clearPQ → 再点一页领奖并传送到 103000805。
            def.stages.Add(new PqStage
            {
                mapId = 103000804, stageNo = 5, npc = 9020001, title = "最后阶段：BOSS",
                steps = {
                    new PqStep { action = PqAction.KillBoss, say = "BOSS 我来拉，你们清小怪" },
                    new PqStep { action = PqAction.BotCollect, ids = new[] { 9300000, 9300002, 9300003 },
                        itemId = PassTicket, need = 10, timeoutMs = 240000, say = "通行证我来捡，凑 10 张" },
                    new PqStep { action = PqAction.HandOver, itemId = PassTicket, itemCount = 10, say = "10 张都交给队长" },
                },
            });
            // 奖励阶段：由 9020002 发奖 + 记录进入次数 + 传回 103000000。
            def.stages.Add(new PqStage
            {
                mapId = 103000805, stageNo = 6, title = "奖励阶段",
                steps = { new PqStep { action = PqAction.Talk, npc = 9020002, say = "领完奖就回城" } },
            });
            return def;
        }

        // ————————————————————— 玩具塔101（LudiPQ）—————————————————————
        // ————————————————————— 玩具塔101（LudiPQ）—————————————————————
        // 九关一个套路（npc/2040036~2040044.js）：
        //   ① 队长点 NPC 听规则（脚本把 statusStgN 从 -1 置 0，只发一页说明）
        //   ② 队员去打怪 / 砸箱子，把 4001022 攒够（通行证 100% 掉落，箱子走 reactordrops）
        //   ③ 队员把券全部交给队长（源端是当面交易）
        //   ④ 队长再点一次 NPC → cm.haveItem(4001022, N) → 扣券 + clearStage + linkToNextStage
        // 张数照抄脚本：25 / 15 / 32 / 6 / 24 / 3；第九关要的是 4001023 钥匙。
        static PqDef Ludi()
        {
            var def = new PqDef
            {
                key = "LPQ", name = "玩具塔101", eventName = "LudiPQ",
                recruitMap = 221024500, entryMap = 922010100, exitMap = 922010000,
                recruitNpc = 2040034, minLevel = 35,
                keywords = new[] { "求组", "缺一", "缺二", "缺三", "q1", "q2", "组队", "玩具", "101", "塔" },
                extraMaps = {
                    922010201,                                                          // 第 2 关陷阱房（多 4 个箱子）
                    922010401, 922010402, 922010403, 922010404, 922010405,              // 第 4 关黑暗塔五间
                    922010501, 922010502, 922010503, 922010504, 922010505, 922010506,   // 第 5 关六个子图
                },
            };
            // 第 1 关：15 只 9300005 + 10 只 9300006，各掉 1 张，正好 25。
            def.stages.Add(new PqStage
            {
                mapId = 922010100, stageNo = 1, title = "第1阶段：25 张通行证",
                steps = {
                    Talk(2040036, "队长先听红气球讲规则", openOnly: true),
                    Collect(LudiPass, 25, Ids(9300005, 9300006), null, null,
                        "白鼠黑鼠我包了，通行证我来捡", 300000),
                    HandOver(LudiPass, 25, "25 张都交给队长"),
                    Talk(2040036, "队长去交 25 张", await: Cleared(1)),
                },
            });
            // 第 2 关：本图 11 个箱子 + 陷阱房 4 个，每个 1 张 = 15。
            def.stages.Add(new PqStage
            {
                mapId = 922010200, stageNo = 2, title = "第2阶段：15 张通行证",
                steps = {
                    Talk(2040037, "队长去听橙气球讲规则", openOnly: true),
                    Collect(LudiPass, 15, null, Ids(2202003), Ids(922010200, 922010201),
                        "箱子我来砸，砸开就有券", 300000),
                    HandOver(LudiPass, 15, "15 张给队长"),
                    Talk(2040037, "队长去交 15 张", await: Cleared(2)),
                },
            });
            // 第 3 关：8 只 9300007 先在地图上；砸开 8 个 2201001 每个再刷 3 只（reactor/2201001.js），
            // 8 + 8×3 = 32 只，正好 32 张。注意 2201001 本身不掉券（脚本只 spawnMonster），
            // 所以这里不能给箱子配掉落，否则会多出 8 张。
            def.stages.Add(new PqStage
            {
                mapId = 922010300, stageNo = 3, title = "第3阶段：32 张通行证",
                steps = {
                    Talk(2040038, "队长去听黄气球讲规则", openOnly: true),
                    Collect(LudiPass, 32, Ids(9300007), Ids(2201001), null,
                        "泡泡鱼和箱子我一起收拾", 360000),
                    HandOver(LudiPass, 32, "32 张齐了"),
                    Talk(2040038, "队长去交 32 张", await: Cleared(3)),
                },
            });
            // 第 4 关：黑暗塔五间，物理/魔法各打一半，一共 6 只（9300008 ×3 + 9300014 ×3）。
            def.stages.Add(new PqStage
            {
                mapId = 922010400, stageNo = 4, title = "第4阶段：6 张通行证",
                steps = {
                    Talk(2040039, "队长去听青气球讲规则", openOnly: true),
                    Collect(LudiPass, 6, Ids(9300008, 9300014), null,
                        Ids(922010401, 922010402, 922010403, 922010404, 922010405),
                        "黑暗塔五间我分开跑", 300000),
                    HandOver(LudiPass, 6, "6 张给队长"),
                    Talk(2040039, "队长去交 6 张", await: Cleared(4)),
                },
            });
            // 第 5 关：六个子图各 4 个箱子，24 张。假人按人头分图，砸完再汇合。
            def.stages.Add(new PqStage
            {
                mapId = 922010500, stageNo = 5, title = "第5阶段：24 张通行证",
                steps = {
                    Talk(2040040, "队长去听绿气球讲规则", openOnly: true),
                    Collect(LudiPass, 24, null, Ids(2202003),
                        Ids(922010501, 922010502, 922010503, 922010504, 922010505, 922010506),
                        "六个子图我们分开砸箱子", 360000),
                    HandOver(LudiPass, 24, "24 张齐了"),
                    Talk(2040040, "队长去交 24 张", await: Cleared(5)),
                },
            });
            // 第 6 关是纯传送门迷宫（922010600），Aqua Balloon 只说一句话，没有判定。
            def.stages.Add(new PqStage { mapId = 922010600, stageNo = 6, title = "第6阶段：爬到塔顶", gateMs = 20000 });
            // 第 7 关：砸开 3 个 2201002 各刷 1 只战甲鱼（reactor/2201002.js），打掉凑 3 张。
            // 2201002 同样只刷怪不掉券。
            def.stages.Add(new PqStage
            {
                mapId = 922010700, stageNo = 7, title = "第7阶段：3 张通行证",
                steps = {
                    Talk(2040042, "队长去听天蓝气球讲规则", openOnly: true),
                    Collect(LudiPass, 3, Ids(9300010), Ids(2201002), null,
                        "战甲鱼我来打", 300000),
                    HandOver(LudiPass, 3, "3 张给队长"),
                    Talk(2040042, "队长去交 3 张", await: Cleared(7)),
                },
            });
            // 第 8 关：五个人站到箱子上（npc/2040043.js:128 判定 getAreas() 的 9 个矩形）。
            // 脚本对 size()<7 的队伍直接放行，所以这里只要人真的站上箱子、队长点两次即可。
            def.stages.Add(new PqStage
            {
                mapId = 922010800, stageNo = 8, title = "第8阶段：五个箱子占位",
                steps = {
                    Talk(2040043, "队长先听蓝气球讲规则", openOnly: true),
                    new PqStep { action = PqAction.Stand, say = "我们一人站一个箱子",
                        need = 5, stands = {
                            new PqRect(-247, -232, 61, 25), new PqRect(-180, -233, 61, 25),
                            new PqRect(-246, -194, 60, 25), new PqRect(-180, -195, 61, 26),
                            new PqRect(-112, -196, 59, 26) } },
                    Talk(2040043, "队长去按确定", await: Cleared(8)),
                },
            });
            // 第 9 关：砸祭坛 2201003 召出阿丽莎乐（9300012），打死掉 4001023；
            // 队长带钥匙点紫气球 → 发阶段经验 + clearPQ → 传送到奖励关 922011000。
            def.stages.Add(new PqStage
            {
                mapId = 922010900, stageNo = 9, title = "第9阶段：BOSS 阿丽莎乐",
                steps = {
                    Talk(2040044, "队长先听紫气球讲规则", openOnly: true),
                    Collect(LudiKey, 1, Ids(9300012), Ids(2201003), null, "祭坛我来砸，BOSS 交给我", 300000),
                    HandOver(LudiKey, 1, "钥匙给队长"),
                    Talk(2040044, "队长去交钥匙", await: Cleared(9)),
                },
            });
            // 奖励关：922011000 等一分钟 → 922011100 砸箱子拿奖，然后回出口图。
            def.stages.Add(new PqStage { mapId = 922011000, stageNo = 10, title = "奖励关：等开门", gateMs = 6000 });
            def.stages.Add(new PqStage
            {
                mapId = 922011100, stageNo = 11, title = "奖励关：砸完箱子就撤",
                steps = { new PqStep { action = PqAction.BreakReactors, say = "箱子我来砸，东西大家分" } },
            });
            return def;
        }

        // ————————————————————— 毒雾森林（EllinPQ）—————————————————————
        // 和玩具塔不同，这一路没有「队员交东西」的环节，只有两处需要队长持有道具：
        // 930000500 的 2133004 要看 4001163（紫色魔力石），没带就当场把你传出副本。
        static PqDef Ellin()
        {
            var def = new PqDef
            {
                key = "EPQ", name = "毒雾森林", eventName = "EllinPQ",
                recruitMap = 300030100, entryMap = 930000000, exitMap = 930000800,
                recruitNpc = 2133000, minLevel = 45,
                keywords = new[] { "求组", "缺一", "缺二", "缺三", "q1", "q2", "组队", "毒雾", "森林", "艾琳" },
            };
            // 930000000 只是入口，传送门 party6_stage 直接放行（NPC 2133001 在这里只负责送回城）。
            def.stages.Add(new PqStage { mapId = 930000000, stageNo = 1, title = "森林入口" });
            // 930000100：portal/party6_stage.js 要求 getMonsters().size()==0 —— 50 只 9300172。
            def.stages.Add(new PqStage
            {
                mapId = 930000100, stageNo = 2, title = "森林初入：清掉 50 只毒苔藓木妖",
                steps = { Clear("这片苔藓木妖我来清") },
            });
            // 930000200：spine 反应堆 3009000 要打到 state>=4 才放行，旁边还有 2 只 9300173。
            def.stages.Add(new PqStage
            {
                mapId = 930000200, stageNo = 3, title = "变质的森林：砸断毒刺",
                steps = {
                    new PqStep { action = PqAction.BreakReactors, ids = Ids(3009000), need = 1, say = "毒刺我来敲" },
                    Clear("顺手把守着的怪清掉"),
                },
            });
            // 930000300：16 格迷宫，出口那格的 2133001 会把全队送到 930000500。
            def.stages.Add(new PqStage
            {
                mapId = 930000300, stageNo = 4, title = "雾之丛：找出口",
                steps = { Talk(2133001, "队长去跟艾琳说话", await: null) },
            });
            // 930000500：9 个石堆，只有 3002001 掉 4001163（reactor/3002001.js → dropItems，
            // 源端 reactordrops 表 3002001 → 4001163 概率 1）；3002002 没有脚本，砸了什么都不掉。
            // 队长必须带着石头去点 2133004，否则脚本会把你传回出口图（等于放弃本趟）。
            def.stages.Add(new PqStage
            {
                mapId = 930000500, stageNo = 5, title = "森林空地：拿紫色魔力石",
                steps = {
                    Collect(EllinStone, 1, null, Ids(3002001, 3002002), null,
                        "石堆我来砸，魔力石给队长", 240000),
                    HandOver(EllinStone, 1, "魔力石给队长"),
                    Talk(2133004, "队长带着石头去点艾琳"),
                },
            });
            // 930000600：祭坛 3001000 召出毒液石头人（9300180，WZ revive 链 180→181→182），
            // 打掉 9300182 时事件脚本 monsterKilled 才 clearPQ，出口门 party6_out 才会开。
            def.stages.Add(new PqStage
            {
                mapId = 930000600, stageNo = 6, title = "毒树丛：石头人三连",
                steps = {
                    new PqStep { action = PqAction.BreakReactors, ids = Ids(3001000), need = 1, say = "祭坛我来开" },
                    Clear("石头人我来扛，变一次打一次"),
                },
                gateMs = 8000,
            });
            // 930000700：2133001 的「确定」走发放奖励（前三次有特殊奖励）+ 记次数 + 传回 930000800。
            def.stages.Add(new PqStage
            {
                mapId = 930000700, stageNo = 7, title = "艾琳森林：领奖",
                steps = { Talk(2133001, "队长去领奖，领完就回城") },
            });
            return def;
        }

        // ————————————————————— 老海盗 / 海盗船（PiratePQ）—————————————————————
        // 这一路的关键差别：2094002 只走**一页**对话（npc/2094002.js:27 再点一次就把队长传回城），
        // 所以三轮勋章都用 OpenOnly；每轮的完成信号是实例属性 stage2 = "1"/"2"/"3"。
        static PqDef Pirate()
        {
            var def = new PqDef
            {
                key = "PPQ", name = "老海盗", eventName = "PiratePQ",
                recruitMap = 251010404, entryMap = 925100000, exitMap = 925100700,
                recruitNpc = 2094000, minLevel = 55,
                keywords = new[] { "求组", "缺一", "缺二", "缺三", "q1", "q2", "组队", "海盗", "船" },
                extraMaps = { 925100201, 925100202, 925100301, 925100302 },
            };
            // 925100000：清光 26 只守卫（9300108~9300111），门 davy_next0 才开。
            def.stages.Add(new PqStage
            {
                mapId = 925100000, stageNo = 1, title = "抵达海盗船的路：清怪",
                steps = { CleanMobs() },
            });
            // 925100100：三轮勋章，每轮 20 个（4001120 → 4001121 → 4001122）。
            // 勋章由 9300114/5/6 各 100% 掉落，队员捡满交给队长，队长点一次 2094002 交掉。
            // 每交一轮脚本会 killAllMonsters，所以要等怪重新刷出来再打下一轮。
            def.stages.Add(new PqStage
            {
                mapId = 925100100, stageNo = 2, title = "突破船头：三轮勋章",
                steps = {
                    Collect(PirateLow, 20, Ids(9300114, 9300115, 9300116), null, null,
                        "新手勋章我来捡", 200000),
                    HandOver(PirateLow, 20, "20 个新手勋章给队长"),
                    Talk(2094002, "队长交第一轮", openOnly: true, await: "stage2", value: "1"),
                    Collect(PirateMid, 20, Ids(9300114, 9300115, 9300116), null, null,
                        "升华勋章也够了", 200000),
                    HandOver(PirateMid, 20, "20 个升华勋章给队长"),
                    Talk(2094002, "队长交第二轮", openOnly: true, await: "stage2", value: "2"),
                    Collect(PirateHigh, 20, Ids(9300114, 9300115, 9300116), null, null,
                        "最后一轮资深勋章", 200000),
                    HandOver(PirateHigh, 20, "20 个资深勋章给队长"),
                    Talk(2094002, "队长交第三轮", openOnly: true, await: "stage2", value: "3"),
                },
            });
            // 925100200：真正要清的是子舱 925100201 那 48 只；清完 davy_next2 才开。
            def.stages.Add(new PqStage
            {
                mapId = 925100200, stageNo = 3, title = "突破船甲1：清舱",
                steps = { new PqStep { action = PqAction.ClearMobs, maps = Ids(925100200, 925100201), say = "子舱的怪我去清" } },
                gateMs = 12000,
            });
            def.stages.Add(new PqStage
            {
                mapId = 925100300, stageNo = 4, title = "突破船甲2：清舱",
                steps = { new PqStep { action = PqAction.ClearMobs, maps = Ids(925100300, 925100301), say = "这层交给我" } },
                gateMs = 12000,
            });
            // 925100400：四个动力门 sMob1~4 各打一下（davy_next4 要求 state>=1），再把杂兵清掉。
            // 清完要玩家自己踩 next00 —— 那扇门的脚本会顺手把 BOSS 刷进 925100500。
            def.stages.Add(new PqStage
            {
                mapId = 925100400, stageNo = 5, title = "击退海盗：四个动力门",
                steps = {
                    new PqStep { action = PqAction.BreakReactors, ids = Ids(2519000, 2519001, 2519002, 2519003),
                        need = 4, say = "四个门我一个个敲" },
                    CleanMobs(),
                },
                gateMs = 20000,
            });
            def.stages.Add(new PqStage
            {
                mapId = 925100500, stageNo = 6, npc = 2094002, title = "海盗船长的威严：BOSS",
                steps = { new PqStep { action = PqAction.KillBoss, say = "船长交给我" } },
            });
            // 925100600：2094001 发奖（sendYesNo），点完就地解散回招募图。
            def.stages.Add(new PqStage
            {
                mapId = 925100600, stageNo = 7, npc = 2094001, title = "无恙的感谢：领奖",
            });
            return def;
        }

        static PqStep CleanMobs() => new PqStep { action = PqAction.ClearMobs, say = "小怪我来清" };
    }
}
