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
    }

    public sealed class PqStep
    {
        public PqAction action;
        public int[] ids = System.Array.Empty<int>();   // 怪 / 反应堆模板 id
        public int minId, maxId;                        // 反应堆 id 区间（0 = 不用）
        public int need;                                // 数量要求
        public int itemId, itemCount;                   // GrantItem
        public int npc;                                 // Talk
        public bool openOnly;                           // 只开一次对话（海盗船 2094002）
        public int rounds = 1;                          // Talk 重复轮数
        public string[] prefer;                         // 对话选项偏好关键字
        public List<PqRect> stands;                     // Stand
        public string say;                              // 做这件事时假人说的话
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
            // 阶段 1：源端 9020001 的队长分支要「队员数-1 张通行证」。单机实例只登记本地角色，
            // 所以数量恒为 0；假人仍然照脚本把券攒出来（看得见的活），再让队长去交。
            def.stages.Add(new PqStage
            {
                mapId = 103000800, stageNo = 1, npc = 9020001, title = "第一阶段：收集通行证",
                steps = { Clear("第一阶段：先把路上的怪清掉，券我来捡") },
            });
            // 阶段 2/3/4：绳子 / 平台 / 木桶三选三，判定矩形照 npc/9020001.js:41-47。
            def.stages.Add(new PqStage
            {
                mapId = 103000801, stageNo = 2, npc = 9020001, title = "第二阶段：挂绳子",
                steps = { Stand("第二阶段我去挂绳子", new PqRect(-755, -132, 4, 218), new PqRect(-721, -340, 4, 166),
                    new PqRect(-586, -326, 4, 150), new PqRect(-483, -181, 4, 222)) },
            });
            def.stages.Add(new PqStage
            {
                mapId = 103000802, stageNo = 3, npc = 9020001, title = "第三阶段：站平台",
                steps = { Stand("第三阶段我站平台", new PqRect(608, -180, 140, 50), new PqRect(791, -117, 140, 45),
                    new PqRect(958, -180, 140, 50), new PqRect(876, -238, 140, 45), new PqRect(702, -238, 140, 45)) },
            });
            def.stages.Add(new PqStage
            {
                mapId = 103000803, stageNo = 4, npc = 9020001, title = "第四阶段：踩木桶",
                steps = { Stand("第四阶段踩木桶", new PqRect(910, -236, 35, 5), new PqRect(877, -184, 35, 5),
                    new PqRect(946, -184, 35, 5), new PqRect(845, -132, 35, 5), new PqRect(910, -132, 35, 5),
                    new PqRect(981, -132, 35, 5)) },
            });
            // 阶段 5：源端要 10 张通行证（npc/9020001.js:260），先打 BOSS 再凑券。
            def.stages.Add(new PqStage
            {
                mapId = 103000804, stageNo = 5, npc = 9020001, title = "最后阶段：BOSS",
                requireItem = PassTicket, requireCount = 10,
                steps = { new PqStep { action = PqAction.KillBoss, say = "BOSS 我来拉，你们清小怪" },
                          Give(PassTicket, 10, "券够了，队长去交") },
            });
            // 奖励阶段 103000805 没有 NPC 判定，直接由 PqRun 走出口。
            return def;
        }

        // ————————————————————— 玩具塔101（LudiPQ）—————————————————————
        static PqDef Ludi()
        {
            var def = new PqDef
            {
                key = "LPQ", name = "玩具塔101", eventName = "LudiPQ",
                recruitMap = 221024500, entryMap = 922010100, exitMap = 922010000,
                recruitNpc = 2040034, minLevel = 35,
                keywords = new[] { "求组", "缺一", "缺二", "缺三", "q1", "q2", "组队", "玩具", "101", "塔" },
            };
            // 阶段 1：25 张券（9300005 ×15 + 9300006 ×10，npc/2040036.js:75）
            def.stages.Add(new PqStage
            {
                mapId = 922010100, stageNo = 1, npc = 2040036, title = "第1阶段：25 张通行证",
                requireItem = LudiPass, requireCount = 25,
                steps = { new PqStep { action = PqAction.KillMobs, ids = new[] { 9300005, 9300006 }, need = 25, say = "白鼠黑鼠我包了，券归队长" },
                          Give(LudiPass, 25, "25 张，够了") },
            });
            // 阶段 2：11 个箱子 2202003（陷阱图还有 4 个）。这里用「砸完本图箱子 + 补足 15 张」。
            def.stages.Add(new PqStage
            {
                mapId = 922010200, stageNo = 2, npc = 2040037, title = "第2阶段：15 张通行证",
                requireItem = LudiPass, requireCount = 15,
                steps = { new PqStep { action = PqAction.BreakReactors, ids = new[] { 2202003 }, need = 11, say = "箱子我来砸" },
                          Give(LudiPass, 15, "15 张，走") },
            });
            // 阶段 3：8 只 9300007 + 8 个 2201001 = 32 张
            def.stages.Add(new PqStage
            {
                mapId = 922010300, stageNo = 3, npc = 2040038, title = "第3阶段：32 张通行证",
                requireItem = LudiPass, requireCount = 32,
                steps = { new PqStep { action = PqAction.BreakReactors, ids = new[] { 2201001 }, need = 8, say = "双鱼出来了，先打双鱼" },
                          Clear("清场"),
                          Give(LudiPass, 32, "32 张齐了") },
            });
            // 阶段 4：6 只（在黑暗塔子图），这里按「补券 + 清本图」处理
            def.stages.Add(new PqStage
            {
                mapId = 922010400, stageNo = 4, npc = 2040039, title = "第4阶段：6 张通行证",
                requireItem = LudiPass, requireCount = 6,
                steps = { Clear("黑暗塔的怪我来清"), Give(LudiPass, 6, "6 张") },
            });
            // 阶段 5：24 张（6 张子图共 25 个箱子）
            def.stages.Add(new PqStage
            {
                mapId = 922010500, stageNo = 5, npc = 2040040, title = "第5阶段：24 张通行证",
                requireItem = LudiPass, requireCount = 24,
                steps = { new PqStep { action = PqAction.BreakReactors, ids = new[] { 2202003 }, need = 6, say = "我在 5 层砸箱子" },
                          Give(LudiPass, 24, "24 张齐") },
            });
            // 阶段 6 是纯传送门迷宫（922010600），没有判定：由 PqRun 直接送到下一阶段。
            def.stages.Add(new PqStage { mapId = 922010600, stageNo = 6, title = "第6阶段：爬塔" });
            // 阶段 7：3 只 9300010（由 2201002 反应堆刷出来）
            def.stages.Add(new PqStage
            {
                mapId = 922010700, stageNo = 7, npc = 2040042, title = "第7阶段：3 张通行证",
                requireItem = LudiPass, requireCount = 3,
                steps = { new PqStep { action = PqAction.BreakReactors, ids = new[] { 2201002 }, need = 3, say = "战甲鱼我来打" },
                          Clear("补刀"), Give(LudiPass, 3, "3 张") },
            });
            // 阶段 8：9 个区域站 5 个人 —— 源端 npc/2040043.js:128 在 size()<7 时直接放行，
            // 单机实例只有 1 个人，所以这关脚本必过；假人仍按矩形站好，免得看起来像跳过了。
            def.stages.Add(new PqStage
            {
                mapId = 922010800, stageNo = 8, npc = 2040043, title = "第8阶段：站箱子",
                steps = { Stand("我站左边那个箱子", new PqRect(-247, -232, 61, 25), new PqRect(-180, -233, 61, 25),
                    new PqRect(-112, -196, 59, 26), new PqRect(-42, -157, 59, 25)) },
            });
            // 阶段 9：砸 2201003 出阿丽莎乐，打死掉钥匙
            def.stages.Add(new PqStage
            {
                mapId = 922010900, stageNo = 9, npc = 2040044, title = "第9阶段：BOSS 阿丽莎乐",
                requireItem = LudiKey, requireCount = 1,
                steps = { new PqStep { action = PqAction.BreakReactors, ids = new[] { 2201003 }, need = 1, minId = 0, maxId = 0, say = "我把祭坛砸开" },
                          new PqStep { action = PqAction.KillBoss, say = "BOSS 交给我" },
                          Give(LudiKey, 1, "钥匙拿到了，队长去交") },
            });
            return def;
        }

        // ————————————————————— 毒雾森林（EllinPQ）—————————————————————
        static PqDef Ellin()
        {
            var def = new PqDef
            {
                key = "EPQ", name = "毒雾森林", eventName = "EllinPQ",
                recruitMap = 300030100, entryMap = 930000000, exitMap = 930000800,
                recruitNpc = 2133000, minLevel = 45,
                keywords = new[] { "求组", "缺一", "缺二", "缺三", "q1", "q2", "组队", "毒雾", "森林", "艾琳" },
            };
            // 第一张图 930000000 只是入口，east00 直接放行。
            def.stages.Add(new PqStage { mapId = 930000000, stageNo = 1, title = "森林入口" });
            // 930000100：源端 portal/party6_stage.js:8 要求把 50 只 9300172 清空。
            def.stages.Add(new PqStage
            {
                mapId = 930000100, stageNo = 2, title = "森林初入：清 50 只毒苔藓木妖",
                steps = { Clear("这片苔藓木妖我来清") },
            });
            // 930000200：反应堆 3009000（spine）要打到 state>=4。
            def.stages.Add(new PqStage
            {
                mapId = 930000200, stageNo = 3, title = "变质的森林：砸毒刺",
                steps = { new PqStep { action = PqAction.BreakReactors, ids = new[] { 3009000 }, need = 1, say = "毒刺我来敲" } },
            });
            // 930000300：16 格迷宫，走到 16st 找 2133001 对话进 930000500。
            def.stages.Add(new PqStage
            {
                mapId = 930000300, stageNo = 4, npc = 2133001, title = "雾之丛：找出口",
            });
            // 930000500：9 个反应堆，其中一个 3002001 必掉紫色魔力石；队长持石去 2133004。
            def.stages.Add(new PqStage
            {
                mapId = 930000500, stageNo = 5, npc = 2133004, title = "森林空地：拿魔力石",
                requireItem = EllinStone, requireCount = 1,
                steps = { new PqStep { action = PqAction.BreakReactors, ids = new[] { 3002001, 3002002 }, need = 9, say = "石堆我来砸，魔力石给队长" },
                          Give(EllinStone, 1, "魔力石给队长") },
            });
            // 930000600：祭坛 3001000 需要 reactItem 4001163，召出 9300180 -> 181 -> 182。
            def.stages.Add(new PqStage
            {
                mapId = 930000600, stageNo = 6, title = "毒树丛：石头人三连",
                steps = { new PqStep { action = PqAction.BreakReactors, ids = new[] { 3001000 }, need = 1, say = "祭坛我来开" },
                          new PqStep { action = PqAction.KillMobs, ids = new[] { 9300180, 9300181, 9300182 }, need = 3, say = "石头人我来扛" },
                          Clear("清尾巴") },
            });
            // 930000700：NPC 2133001 发奖（yes）→ warp 930000800。
            def.stages.Add(new PqStage
            {
                mapId = 930000700, stageNo = 7, npc = 2133001, title = "艾琳森林：领奖",
            });
            return def;
        }

        // ————————————————————— 老海盗 / 海盗船（PiratePQ）—————————————————————
        static PqDef Pirate()
        {
            var def = new PqDef
            {
                key = "PPQ", name = "老海盗", eventName = "PiratePQ",
                recruitMap = 251010404, entryMap = 925100000, exitMap = 925100700,
                recruitNpc = 2094000, minLevel = 55,
                keywords = new[] { "求组", "缺一", "缺二", "缺三", "q1", "q2", "组队", "海盗", "船" },
            };
            // 925100000：清 26 只（9300108~9300111）
            def.stages.Add(new PqStage
            {
                mapId = 925100000, stageNo = 1, title = "抵达海盗船的路：清怪",
                steps = { CleanMobs() },
            });
            // 925100100：2094002 交 3 轮，每轮 20 个（4001120/21/22）。
            // 这个 NPC 只要多点一次就把队长传回 251010404（npc/2094002.js:20-23），
            // 所以必须是 OpenOnly：开一次触发交付，然后 ESC。
            def.stages.Add(new PqStage
            {
                mapId = 925100100, stageNo = 2, npc = 2094002, title = "突破船头：三轮勋章",
                steps = {
                    new PqStep { action = PqAction.GrantItem, itemId = PirateLow, itemCount = 20, say = "初级勋章凑够了" },
                    new PqStep { action = PqAction.Talk, npc = 2094002, openOnly = true, rounds = 1, say = "第一轮交了" },
                    new PqStep { action = PqAction.GrantItem, itemId = PirateMid, itemCount = 20, say = "中级勋章也够了" },
                    new PqStep { action = PqAction.Talk, npc = 2094002, openOnly = true, rounds = 1, say = "第二轮交了" },
                    new PqStep { action = PqAction.GrantItem, itemId = PirateHigh, itemCount = 20, say = "高级勋章最后 20 个" },
                    new PqStep { action = PqAction.Talk, npc = 2094002, openOnly = true, rounds = 1, say = "三轮交完，开门吧" },
                },
            });
            def.stages.Add(new PqStage { mapId = 925100200, stageNo = 3, title = "突破船甲1：清怪", steps = { CleanMobs() } });
            def.stages.Add(new PqStage { mapId = 925100300, stageNo = 4, title = "突破船甲2：清怪", steps = { CleanMobs() } });
            // 925100400：4 个门反应堆 2519000~2519003 都要打到 state>=1
            def.stages.Add(new PqStage
            {
                mapId = 925100400, stageNo = 5, title = "击退海盗：四个门",
                steps = { new PqStep { action = PqAction.BreakReactors, ids = new[] { 2519000, 2519001, 2519002, 2519003 }, need = 4, say = "四个门我一个个来" },
                          CleanMobs() },
            });
            // 925100500：BOSS（开 0 个宝箱 → 9300119 老海盗）
            def.stages.Add(new PqStage
            {
                mapId = 925100500, stageNo = 6, npc = 2094001, title = "海盗船长的威严：BOSS",
                steps = { new PqStep { action = PqAction.KillBoss, say = "船长交给我" } },
            });
            // 925100600：发奖 NPC（sendYesNo）
            def.stages.Add(new PqStage
            {
                mapId = 925100600, stageNo = 7, npc = 2094001, title = "无恙的感谢：领奖",
            });
            return def;
        }

        static PqStep CleanMobs() => new PqStep { action = PqAction.ClearMobs, say = "小怪我来清" };
    }
}
