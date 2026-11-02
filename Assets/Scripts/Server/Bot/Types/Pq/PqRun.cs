using System;
using System.Collections.Generic;
using Server.Bot.Environment;
using Server.Field;

namespace Server.Bot.Types.Pq
{
    // 一次组队任务跑图的执行者（一个队伍一个实例）。
    //
    // 分工：
    //   · 假人负责「看得见的活」——打怪、砸反应堆、站绳子/平台/木箱、把道具交给队长；
    //   · 真实角色只负责点 NPC 和踩传送门，而这两件事都由本类代劳：
    //       NPC → PqDirector.Talk（PqNpcDriver 静默点完，窗口不闪）；
    //       传送门 → 走不通（没有 NPC 可点、脚本没把队伍送走）就直接把角色送到下一张图。
    //   之所以必须由真实角色来点：EventManager.startInstance 只认「队长是本地真实角色」
    //   （NativeEventScriptService:272 用 LocalPlayerId 判定），所以 PqDirector 在开本前
    //   一定会把队长让给玩家。
    //
    // 阶段推进的权威信号仍然是「玩家自己在哪张图」：脚本把队伍送走、或者玩家自己跑过去，
    // mapId 一变就重新对齐，绝不硬按步数推进（同 OpqBot 的 map-derived rehome）。
    public sealed class PqRun
    {
        const long ActionIntervalMs = 420;     // 假人每 420ms 做一次动作（砸/打/走）
        const long StepSayGapMs = 4500;        // 「自言自语」的最小间隔
        const long StepTimeoutMs = 70000;      // 单个动作步骤的上限，超了就往下走
        const long StageIdleMs = 1500;         // 本阶段活干完 → 先站一会儿
        const long TalkRetryMs = 6000;         // NPC 点完没换图 → 直接送过去
        const long StartTimeoutMs = 20000;     // 招募 NPC 点完 20s 还没进本 → 放弃
        const long RewardWaitMs = 6000;        // 出本后在出口图站一会儿再解散
        const long OffMapGraceMs = 8000;       // 玩家离开阶段图（奖励关/回城）后的宽限

        public readonly PqDef def;
        public readonly int partyId, leaderId;
        readonly List<int> bots = new List<int>();
        readonly Random rng = new Random();
        readonly Dictionary<int,long> nextSwing = new Dictionary<int,long>();

        public int stageIndex;
        public int stepIndex;
        public bool finished;
        public string lastNote = "";

        long stepStartedAt, stepDeadline, nextActionAt, nextSayAt, nextChatterAt, offMapAt;
        long stageClearedAt, startDeadline, rewardUntil;
        bool started, stageWorkDone, talking, talkSent, talkDone;
        int talkStage = -1;

        // ——— 队员分支（废都第一关）的每个假人状态 ———
        // couponNeed：领题后脚本要求收几张优惠券（题目号 → PqCatalog.Stage1Answer）
        // passReady：已经用券换到 1 张通行证
        readonly Dictionary<int,int> couponNeed = new Dictionary<int,int>();
        readonly HashSet<int> passReady = new HashSet<int>();
        readonly HashSet<int> asked = new HashSet<int>();
        bool questScriptFailed;      // 队员脚本跑不动：退回「队长直接补证」，别卡死
        int checkAttempts;           // 2/3/4 关队长核对次数（超限走源端人数不足放行）
        bool botsRegistered;

        public PqRun(PqDef def, int partyId, int leaderId)
        {
            this.def = def; this.partyId = partyId; this.leaderId = leaderId;
        }

        public bool Contains(int botId) => bots.Contains(botId);
        public IReadOnlyList<int> Bots => bots;

        // =====================================================================
        // 开本
        // =====================================================================

        /// <summary>
        /// 开跑。openViaNpc=false 用于「玩家自己已经带着队伍进本了」——
        /// 这时候再去点招募 NPC 等于重开一次副本，只做接管。
        /// </summary>
        public void Begin(bool openViaNpc = true)
        {
            bots.Clear();
            // partyId 是队伍编号（1000000001 起），BotPartyService.For()/Get() 收的是角色 id，
            // 拿队伍编号去查必然拿到 null —— 四个副本都变成了「只有真人进本、假人留在原地」。
            var parties = BotTypeManager.Parties;
            var party = parties?.ById(partyId) ?? parties?.For(leaderId);
            if (party != null)
                foreach (int member in party.members)
                    if (BotRegistry.Get(member) != null) bots.Add(member);

            long now = BotScheduler.Now();
            started = true;
            stageIndex = -1;
            stepIndex = 0;
            startDeadline = now + StartTimeoutMs;
            if (!openViaNpc)
            {
                // 接管已经在副本里的队伍：阶段由 Tick 里的 mapId 对齐自动认出来。
                startDeadline = long.MaxValue;
                return;
            }
            // 招募 NPC 的 start() 里就是 em.startInstance(party, map, 1)：四个副本的第一项都是
            // 「我想参加(这个)组队任务」，用「我想参加」兜住全部四种文案（海盗船多了个「这个」）。
            PqDirector.Talk(def.recruitNpc, false, new[] { "我想参加", "参加组队任务" }, ok =>
            {
                if (!ok) PqDirector.Notify(def.name + "：招募 NPC 没有正常回应，尝试直接进本。");
            });
            PqDirector.Notify(def.name + " 组队任务开始，假人正在进本。");
        }

        // =====================================================================
        // 主循环（PqDirector 每 500ms 调一次）
        // =====================================================================

        public void Tick(long now)
        {
            if (finished) return;
            PruneBots();
            var player = PqDirector.LocalPlayer();
            if (player == null || playersPartyGone())
            {
                End(false);
                return;
            }
            int map = PqDirector.TemplateMapId(player.mapId);

            // —— 还没进本：等招募脚本把队伍送进去 ——
            if (stageIndex < 0)
            {
                int entry = def.stages.FindIndex(s => s.mapId == map);
                if (entry >= 0) { EnterStage(entry, now); return; }
                if (now >= startDeadline)
                {
                    // 招募失败不能裸传公共模板图：它没有 setup/实例怪物与通行证状态。
                    // 只能由原招募脚本成功建立实例后进入。
                    PqDirector.Notify(def.name + "：招募未成功，请重新确认 NPC 的入场条件。");
                    End(false);
                }
                return;
            }

            // —— 换图是权威信号 ——
            int derived = def.stages.FindIndex(s => s.mapId == map);
            if (map == def.exitMap || map == def.recruitMap)
            {
                End(true);
                return;
            }
            if (derived >= 0 && derived != stageIndex)
            {
                EnterStage(derived, now);
                return;
            }
            if (derived < 0 && map != PqDirector.TemplateMapId(CurrentMap))
            {
                // 玩家跑到了四阶段之外的图：可能是脚本发的奖励关（废都 103000805 那种），
                // 也可能是自己回城了。给一个宽限窗口，过了就按「本趟结束」收尾，
                // 不在这里硬判失败——阶段全清完了才可能走到这一步。
                if (offMapAt == 0) offMapAt = now + OffMapGraceMs;
                else if (now >= offMapAt) End(true);
                return;
            }
            offMapAt = 0;

            var stage = CurrentStage;
            if (stage == null) { End(true); return; }

            // 通关路上一直有人在说话：大部分是「我负责什么」，偶尔是闲聊（用户要求的「说自己负责的逻辑」）。
            if (!stageWorkDone && now >= nextChatterAt)
            {
                nextChatterAt = now + 8000 + rng.Next(7000);
                PqDirector.Say(RandomBot(), rng.Next(100) < 65
                    ? PqChatLines.Role(def.key, rng) : PqChatLines.Idle(rng));
            }

            // —— 站好之后等下一阶段 ——
            if (stageWorkDone)
            {
                if (now - stageClearedAt < StageIdleMs) return;
                AdvanceStage(now);
                return;
            }

            // —— 干活 ——
            DriveWork(stage, now);
        }

        int CurrentMap
        {
            get
            {
                var stage = CurrentStage;
                if (stage == null) return 0;
                var player = PqDirector.LocalPlayer();
                return player != null && PqDirector.TemplateMapId(player.mapId) == stage.mapId
                    ? player.mapId : stage.mapId;
            }
        }
        public PqStage CurrentStage => stageIndex >= 0 && stageIndex < def.stages.Count ? def.stages[stageIndex] : null;

        void PruneBots()
        {
            for (int i = bots.Count - 1; i >= 0; i--)
            {
                var bot = BotRegistry.Get(bots[i]);
                if (bot == null) { bots.RemoveAt(i); continue; }
                // 只留队里的人：被踢/退队的假人不再听本 run 指挥。
                var party = BotTypeManager.Parties?.For(bots[i]);
                if (party == null || party.id != partyId) bots.RemoveAt(i);
            }
        }

        bool playersPartyGone()
        {
            var party = BotTypeManager.Parties?.For(leaderId);
            return party == null || party.id != partyId || !party.members.Contains(leaderId);
        }

        void EnterStage(int index, long now)
        {
            stageIndex = index;
            stepIndex = 0;
            stageWorkDone = false;
            stageClearedAt = 0;
            talking = false; talkSent = false; talkDone = false; talkStage = -1;
            couponNeed.Clear(); passReady.Clear(); asked.Clear();
            questScriptFailed = false; checkAttempts = 0;
            // 0 = 「这一步还没开跑」：DriveWork 会用第一次 tick 的时间把截止时间算出来，
            // 并顺手把这一步的台词说出去。
            stepStartedAt = 0; stepDeadline = 0;
            nextActionAt = now; nextSayAt = now; nextChatterAt = now + 4000;
            lastNote = CurrentStage?.title ?? "";
            // 假人跟着队长进这一张图（脚本只送真实角色，假人得自己走）。
            foreach (int id in bots)
            {
                var bot = BotRegistry.Get(id);
                if (bot == null || bot.mapId == CurrentMap) continue;
                PqDirector.WarpBot(bot, CurrentMap);
            }
            // 源端 registerParty 会把全队登记进事件实例；移植端以前只登记本地角色，
            // 于是 eim.getPlayerCount()-1 恒为 0。这里补上队员，队长的 NPC 对话才按真人数算。
            RegisterBots();
            PqDirector.Notify(def.name + " · " + lastNote);
        }

        void RegisterBots()
        {
            botsRegistered = false;
            foreach (int id in bots) if (PqDirector.EnterEvent(id)) botsRegistered = true;
        }

        void ReleaseBots()
        {
            if (!botsRegistered) return;
            botsRegistered = false;
            foreach (int id in bots) PqDirector.LeaveEvent(id);
        }

        // =====================================================================
        // 阶段内的工作
        // =====================================================================

        void DriveWork(PqStage stage, long now)
        {
            if (stage.steps.Count == 0) { stageWorkDone = true; stageClearedAt = now; return; }
            if (stepIndex >= stage.steps.Count)
            {
                // 活干完了：有 NPC 就静默点一次，脚本负责放行。
                if (!talking && stage.npc != 0 && talkStage != stageIndex)
                {
                    talking = true; talkStage = stageIndex;
                    stepStartedAt = now;
                    GrantRequirement(stage);
                    string[] prefer = { "确定", "是", "下一步", "继续", "好" };
                    PqDirector.Talk(stage.npc, false, prefer, ok =>
                    {
                        talking = false;
                        if (!ok) PqDirector.Notify(def.name + "：" + stage.title + " 的 NPC 没有回应，准备直接放行。");
                    });
                    return;
                }
                if (talking) return;
                stageWorkDone = true; stageClearedAt = now;
                return;
            }

            var step = stage.steps[stepIndex];
            if (stepStartedAt == 0)
            {
                stepStartedAt = now;
                stepDeadline = now + (step.timeoutMs > 0 ? step.timeoutMs : StepTimeoutMs);
                Say(step, now);
            }

            if (now >= stepDeadline) { NextStep(now, "超时跳过"); return; }

            bool done = step.action switch
            {
                PqAction.ClearMobs => WorkMobs(step, now, boss: false),
                PqAction.KillMobs => WorkMobs(step, now, boss: false),
                PqAction.KillBoss => WorkMobs(step, now, boss: true),
                PqAction.BreakReactors => WorkReactors(step, now),
                PqAction.Stand => WorkStands(step, now),
                PqAction.GrantItem => WorkGrant(step),
                PqAction.Talk => WorkTalk(step),
                PqAction.BotAsk => WorkBotAsk(step, now),
                PqAction.BotCollect => WorkBotCollect(step, now),
                PqAction.BotExchange => WorkBotExchange(step, now),
                PqAction.BotWait => WorkBotWait(step, now),
                PqAction.HandOver => WorkHandOver(step),
                PqAction.StageCheck => WorkStageCheck(step, now),
                _ => true,
            };
            if (done) NextStep(now, null);
        }

        /// <summary>
        /// 阶段要求队长持有某些道具（源端 cm.hasItem/haveItem 判定）时先把数量补齐。
        /// 数据表里通常已经有一条显式的 GrantItem 步骤，重复补会把数量翻倍，
        /// 所以只在「本阶段没有任何一条交这个道具的步骤」时补。
        /// </summary>
        void GrantRequirement(PqStage stage)
        {
            if (stage.requireItem <= 0 || stage.requireCount <= 0) return;
            foreach (var step in stage.steps)
                if (step.action == PqAction.GrantItem && step.itemId == stage.requireItem) return;
            PqDirector.Give(stage.requireItem, stage.requireCount);
        }

        void NextStep(long now, string reason)
        {
            if (!string.IsNullOrEmpty(reason)) lastNote = (CurrentStage?.title ?? "") + "（" + reason + "）";
            stepIndex++;
            stepStartedAt = 0; stepDeadline = 0;
            // 一关里可能有多次 Talk（废都 2/3/4 第一次抽组合、第二次核对），
            // 所以 talkSent/talkDone 必须跟着步骤重置，否则第二次对话永远不发。
            talkSent = false; talkDone = false;
            nextActionAt = now; nextSayAt = now;
        }

        void Say(PqStep step, long now)
        {
            if (string.IsNullOrEmpty(step.say) || now < nextSayAt) return;
            nextSayAt = now + StepSayGapMs;
            var bot = RandomBot();
            PqDirector.Say(bot, step.say);
        }

        // ———————————————— 打怪 ————————————————

        // 怪物实例由 Unity 侧提供（BotCombatField → BotPartyGrindService），
        // 这里只做选择与出手，伤害结算仍走同一条 Hit 通路。
        bool WorkMobs(PqStep step, long now, bool boss)
        {
            var grind = BotTypeManager.PartyGrind;
            if (grind == null) return true;
            var player = PqDirector.LocalPlayer();
            if (player == null) return false;
            List<BotGrindMob> mobs;
            try { mobs = grind.Monsters?.Invoke(CurrentMap, player.world, player.channel); }
            catch (Exception) { mobs = null; }
            if (mobs == null) return true;

            var live = new List<BotGrindMob>();
            foreach (var mob in mobs)
            {
                if (mob == null || !mob.alive || mob.fake) continue;
                if (boss != mob.boss) continue;
                live.Add(mob);
            }
            if (live.Count == 0) return true;
            if (now < nextActionAt) return false;
            nextActionAt = now + ActionIntervalMs;

            foreach (int id in bots)
            {
                var bot = BotRegistry.Get(id);
                if (bot == null || bot.mapId != CurrentMap) continue;
                var target = Nearest(bot, live);
                if (target == null) continue;
                bot.equips.TryGetValue(-11, out int weapon);
                var range = BotAttack.EngageRange(bot.job, weapon);
                if (Math.Abs(target.x - bot.x) > range.x || Math.Abs(target.y - bot.y) > range.y)
                {
                    MoveToward(bot, target.x, target.y);
                    continue;
                }
                BotMovementService.Stop(bot);
                if (nextSwing.TryGetValue(id, out long ready) && now < ready) continue;
                bot.stance = target.x < bot.x ? 5 : 4;
                bot.facing = target.x < bot.x ? -1 : 1;
                try { grind.Strike(bot, target); nextSwing[id] = now + 800; }
                catch (Exception) { /* 目标在这一帧被真人打死：下一 tick 重选 */ }
            }
            return false;
        }

        BotGrindMob Nearest(BotCharacter bot, List<BotGrindMob> mobs)
        {
            BotGrindMob best = null; float dist = float.MaxValue;
            foreach (var mob in mobs)
            {
                if (!mob.alive) continue;
                float d = Math.Abs(mob.x - bot.x) + Math.Abs(mob.y - bot.y) * 2f;
                if (d < dist) { dist = d; best = mob; }
            }
            return best;
        }

        // ———————————————— 砸反应堆 ————————————————

        bool WorkReactors(PqStep step, long now)
        {
            var list = MapReactorService.Get(CurrentMap);
            var live = new List<MapReactor>();
            foreach (var reactor in list)
            {
                if (!InStep(step, reactor.id)) continue;
                if (reactor.Hittable) live.Add(reactor);
            }
            if (live.Count == 0) return true;
            if (now < nextActionAt) return false;
            nextActionAt = now + ActionIntervalMs;

            foreach (int id in bots)
            {
                var bot = BotRegistry.Get(id);
                if (bot == null || bot.mapId != CurrentMap) continue;
                var target = NearestReactor(bot, live);
                if (target == null) continue;
                if (Math.Abs(target.x - bot.x) > 60)
                {
                    MoveToward(bot, target.x, target.y);
                    continue;
                }
                bot.stance = target.x < bot.x ? 5 : 4;
                bot.facing = target.x < bot.x ? -1 : 1;
                BotAttack.BasicSwing(bot, rng);
                try { MapReactorService.Hit(CurrentMap, target.objectId, null, bot.id, partyId); }
                catch (Exception) { /* 反应堆这一帧被拆了 */ }
            }
            return false;
        }

        bool InStep(PqStep step, int reactorId)
        {
            if (step.ids.Length > 0)
            {
                foreach (int id in step.ids) if (id == reactorId) return true;
                if (step.minId <= 0 && step.maxId <= 0) return false;
            }
            if (step.minId > 0 || step.maxId > 0)
                return reactorId >= Math.Min(step.minId, step.maxId) && reactorId <= Math.Max(step.minId, step.maxId);
            return step.ids.Length == 0;
        }

        MapReactor NearestReactor(BotCharacter bot, List<MapReactor> list)
        {
            MapReactor best = null; float dist = float.MaxValue;
            foreach (var reactor in list)
            {
                float d = Math.Abs(reactor.x - bot.x) + Math.Abs(reactor.y - bot.y) * .4f;
                if (d < dist) { dist = d; best = reactor; }
            }
            return best;
        }

        // ———————————————— 站位（绳子 / 平台 / 木箱）————————————————

        bool WorkStands(PqStep step, long now)
        {
            if (step.stands == null || step.stands.Count == 0) return true;
            if (now < nextActionAt) return false;
            nextActionAt = now + ActionIntervalMs;
            bool allThere = true;
            int index = 0;
            foreach (int id in bots)
            {
                var bot = BotRegistry.Get(id);
                if (bot == null || bot.mapId != CurrentMap) continue;
                var spot = step.stands[index % step.stands.Count];
                index++;
                int x = spot.CenterX, y = spot.CenterY;
                if (Math.Abs(bot.x - x) <= 26 && Math.Abs(bot.y - y) <= 40) continue;
                allThere = false;
                MoveToward(bot, x, y);
            }
            return index > 0 && allThere;
        }

        // ———————————————— 交物品 / 点 NPC ————————————————

        bool WorkGrant(PqStep step)
        {
            // 源端是队员把券交给队长；单机里直接落进真实角色的背包。
            PqDirector.Give(step.itemId, step.itemCount);
            return true;
        }

        bool WorkTalk(PqStep step)
        {
            if (step.npc == 0) return true;
            if (!talkSent)
            {
                talkSent = true;
                string[] prefer = step.prefer ?? new[] { "确定", "是", "下一步", "继续", "好" };
                PqDirector.Talk(step.npc, step.openOnly, prefer, ok =>
                {
                    talkDone = true;
                    if (!ok) PqDirector.Notify(def.name + "：NPC " + step.npc + " 没有正常回应。");
                });
            }
            // 等这一段对话真的走完再进下一步：脚本的副作用（抽组合、clearStage）都在对话里，
            // 提前返回会出现「还没抽组合就让假人去站位」。
            return talkDone;
        }

        // =====================================================================
        // 队员分支（废都第一关 / 第五关）
        // 源端 npc/9020001.js:154-177：队员先领题（gridCheck=-1 → 随机题目），
        // 收够题目要求的券再点一次换 1 张通行证，然后把证交给队长。
        // =====================================================================

        const float NpcTalkRange = 110f;

        static int Held(BotCharacter bot, int itemId)
        {
            if (bot == null || itemId <= 0) return 0;
            int total = 0;
            foreach (var entry in bot.inventory)
                if (entry != null && entry.itemId == itemId) total += entry.quantity;
            return total;
        }

        MapNpcPosition NpcOf(int npcId)
        {
            var map = MapService.Get(CurrentMap);
            return map?.npcs.Find(n => n.id == npcId);
        }

        bool NearNpc(BotCharacter bot, int npcId, float range = NpcTalkRange)
        {
            var npc = NpcOf(npcId);
            if (npc == null) return true;   // 地图里找不到 NPC：不拿位置卡住流程
            return Math.Abs(bot.x - npc.x) <= range && Math.Abs(bot.y - npc.y) <= range * 2.2f;
        }

        void WalkToNpc(BotCharacter bot, int npcId)
        {
            var npc = NpcOf(npcId);
            if (npc == null) return;
            // 站到 NPC 侧面留一个身位，别和真人重叠在同一像素上。
            int x = npc.x + (bot.x < npc.x ? -36 : 36);
            MoveToward(bot, x, npc.y);
        }

        bool WorkBotAsk(PqStep step, long now)
        {
            if (step.npc == 0 || bots.Count == 0) return true;
            if (now < nextActionAt) return false;
            nextActionAt = now + ActionIntervalMs;

            int seen = 0;
            foreach (int id in bots)
            {
                var bot = BotRegistry.Get(id);
                if (bot == null || bot.mapId != CurrentMap) continue;
                seen++;
                if (asked.Contains(id)) continue;
                if (!NearNpc(bot, step.npc)) { WalkToNpc(bot, step.npc); continue; }
                BotMovementService.Stop(bot);
                if (!PqDirector.BotTalk(bot, step.npc, out string say))
                {
                    // 脚本跑不动（实例没建 / 缺能力）：别把整趟副本卡死，后面直接补证。
                    questScriptFailed = true;
                    asked.Add(id);
                    continue;
                }
                asked.Add(id);
                int question = PqDirector.GridOf(id);
                int need = PqCatalog.Stage1Answer(question);
                if (need > 0)
                {
                    // 上一趟没跑完会留下零散的券，脚本按 == 判定，多余的要先丢掉。
                    int surplus = Held(bot, PqCatalog.PassCoupon) - need;
                    if (surplus > 0) PqDirector.BotGain(bot, PqCatalog.PassCoupon, -surplus);
                    couponNeed[id] = need;
                    // 脚本这一页就是题目原文，直接念出来；再补一句「我要几张」。
                    if (!string.IsNullOrEmpty(say)) PqDirector.Say(bot, say);
                    PqDirector.Say(bot, "我这题要 " + need + " 张优惠券，收够了回 Cloto 换证");
                }
                else if (!string.IsNullOrEmpty(say))
                {
                    // 题目号读不到（实例没登记这个假人）：把脚本原文念出来即可。
                    PqDirector.Say(bot, say);
                }
            }
            return seen == 0 || asked.Count >= seen;
        }

        bool WorkBotCollect(PqStep step, long now)
        {
            int item = step.itemId > 0 ? step.itemId : PqCatalog.PassCoupon;
            int totalHeld = 0;
            if (step.itemId > 0)
                foreach (int id in bots) { var b = BotRegistry.Get(id); if (b != null) totalHeld += Held(b, item); }
            foreach (int id in bots)
            {
                var bot = BotRegistry.Get(id);
                if (bot == null || bot.mapId != CurrentMap) continue;
                int held = Held(bot, item);
                // NPC 是 itemQuantity == answer 严格相等：多捡一张反而交不掉，按「还差几张」封顶。
                int missing = step.itemId > 0
                    ? Math.Max(0, Math.Max(1, step.need) - totalHeld)
                    : Math.Max(0, (couponNeed.TryGetValue(id, out int n) ? n : 0) - held);
                if (missing > 0) PqDirector.Pickup(bot, item, missing);
            }
            if (Collected(step, item)) return true;
            // 第一关的券是打鳄鱼掉的：不够就继续打。第五关的证由 BOSS 直接掉，只捡不打。
            if (step.itemId > 0) return false;
            return WorkMobs(step, now, boss: false);
        }

        bool Collected(PqStep step, int item)
        {
            if (bots.Count == 0) return true;
            int total = 0;
            foreach (int id in bots)
            {
                var bot = BotRegistry.Get(id);
                if (bot == null) continue;
                int held = Held(bot, item);
                total += held;
                if (step.itemId > 0) continue;                 // 第五关按「全队凑满 N 张」算
                int need = couponNeed.TryGetValue(id, out int n) ? n : 0;
                if (need > 0 && held < need) return false;      // 第一关每人一张不落
            }
            return step.itemId > 0 ? total >= Math.Max(1, step.need) : true;
        }

        bool WorkBotExchange(PqStep step, long now)
        {
            if (step.npc == 0 || bots.Count == 0) return true;
            // 有人券还没凑够：继续打怪捡券，不在这里干等（脚本用 == 判定，差一张就换不到）。
            bool shortAny = false;
            foreach (int id in bots)
            {
                var bot = BotRegistry.Get(id);
                if (bot == null || bot.mapId != CurrentMap) continue;
                int need = couponNeed.TryGetValue(id, out int n) ? n : 0;
                if (need <= 0) continue;
                int held = Held(bot, PqCatalog.PassCoupon);
                if (held > need) { PqDirector.BotGain(bot, PqCatalog.PassCoupon, -(held - need)); continue; }
                if (held >= need) continue;
                shortAny = true;
                PqDirector.Pickup(bot, PqCatalog.PassCoupon, need - held);
            }
            if (shortAny) return WorkMobs(step, now, boss: false);

            if (now < nextActionAt) return false;
            nextActionAt = now + ActionIntervalMs;

            int seen = 0, ready = 0;
            foreach (int id in bots)
            {
                var bot = BotRegistry.Get(id);
                if (bot == null || bot.mapId != CurrentMap) continue;
                seen++;
                if (passReady.Contains(id)) { ready++; continue; }
                int need = couponNeed.TryGetValue(id, out int n) ? n : 0;
                if (!NearNpc(bot, step.npc)) { WalkToNpc(bot, step.npc); continue; }
                BotMovementService.Stop(bot);
                if (!PqDirector.BotTalk(bot, step.npc, out string say))
                {
                    questScriptFailed = true;
                    continue;
                }
                if (Held(bot, PqCatalog.PassTicket) > 0)
                {
                    passReady.Add(id);
                    ready++;
                    PqDirector.Say(bot, "换到通行证了，我去 Cloto 旁边等队长");
                }
                else
                {
                    // 领题那一页没读回题目号（实例里还没登记这个假人）：这一次对话会把题号补上。
                    if (need <= 0)
                    {
                        int late = PqCatalog.Stage1Answer(PqDirector.GridOf(id));
                        if (late > 0) couponNeed[id] = late;
                    }
                    if (!string.IsNullOrEmpty(say)) PqDirector.Say(bot, say);   // 券数不对，脚本会重念题目
                }
            }
            if (questScriptFailed) return true;
            return seen == 0 || ready >= seen;
        }

        bool WorkBotWait(PqStep step, long now)
        {
            if (bots.Count == 0) return true;
            if (now < nextActionAt) return false;
            nextActionAt = now + ActionIntervalMs;
            int index = 0;
            bool allThere = true;
            foreach (int id in bots)
            {
                var bot = BotRegistry.Get(id);
                if (bot == null || bot.mapId != CurrentMap) continue;
                index++;
                if (NearNpc(bot, step.npc, 70f)) { BotMovementService.Stop(bot); continue; }
                allThere = false;
                WalkToNpc(bot, step.npc);
            }
            return index > 0 && allThere;
        }

        bool WorkHandOver(PqStep step)
        {
            int item = step.itemId;
            if (item <= 0) return true;
            // 第一关：一人一张证（队长点 NPC 时 numpasses = 实例人数-1，正好等于队员数）。
            int want = step.itemCount > 0 ? step.itemCount : Math.Max(1, bots.Count);
            if (questScriptFailed)
            {
                // 队员脚本跑不动时的保底：直接落进队长背包，副本还能继续。
                PqDirector.Give(item, want);
                return true;
            }
            int given = 0;
            foreach (int id in bots)
            {
                if (given >= want) break;
                var bot = BotRegistry.Get(id);
                if (bot == null) continue;
                int held = Held(bot, item);
                if (held <= 0) continue;
                int take = Math.Min(held, want - given);
                PqDirector.BotGain(bot, item, -take);
                PqDirector.Give(item, take);
                given += take;
                PqDirector.Say(bot, "通行证给你，去点 Cloto 吧");
            }
            if (given < want)
            {
                PqDirector.Give(item, want - given);
                PqDirector.Notify(def.name + "：还差 " + (want - given) + " 张证，先由队长补上");
            }
            return true;
        }

        // =====================================================================
        // 2/3/4 关：按抽中的组合站位 + 队长核对
        // =====================================================================

        bool WorkStageCheck(PqStep step, long now)
        {
            string clearKey = (CurrentStage?.stageNo ?? 0) + "stageclear";
            if (PqDirector.PropertyOf(clearKey) != null) return true;
            if (now < nextActionAt) return false;
            nextActionAt = now + 2600;

            // 前两轮按组合认真摆；两次都没过就按源端的「队伍人数不足 3 人直接放行」收尾，
            // 免得像素级站位误差把整趟副本锁死。
            if (checkAttempts < 2) PlaceByCombo(step);
            else ReleaseBots();

            checkAttempts++;
            PqDirector.Talk(step.npc, false, new[] { "确定", "是", "下一步", "继续" }, _ => { });
            return checkAttempts >= 5;
        }

        void PlaceByCombo(PqStep step)
        {
            string raw = PqDirector.PropertyOf(step.comboProperty);
            int.TryParse(raw, out int index);
            var combo = PqCatalog.Combo(step.comboProperty, index);
            var (homeX, homeY) = PqDirector.EntryPoint(CurrentMap);
            var spots = new List<int>();
            if (combo != null && step.stands != null)
                for (int i = 0; i < combo.Length && i < step.stands.Count; i++)
                    if (combo[i] == 1) spots.Add(i);
            int slot = 0;
            foreach (int id in bots)
            {
                var bot = BotRegistry.Get(id);
                if (bot == null || bot.mapId != CurrentMap) continue;
                if (slot < spots.Count)
                {
                    var rect = step.stands[spots[slot++]];
                    MoveToward(bot, rect.CenterX, rect.CenterY);
                }
                else if (Math.Abs(bot.x - homeX) > 40) MoveToward(bot, homeX, homeY);
            }
        }

        // =====================================================================
        // 阶段推进
        // =====================================================================

        void AdvanceStage(long now)
        {
            int next = stageIndex + 1;
            if (next >= def.stages.Count) { Clear(now); return; }
            var stage = def.stages[next];
            // 脚本通常会自己把队伍送到下一张图（linkToNextStage / warpEventTeam）；
            // 没送走就由这里补一脚，免得卡死。
            if (PqDirector.TemplateMapId(PqDirector.LocalMapId()) != stage.mapId) PqDirector.WarpPlayer(stage.mapId);
            EnterStage(next, now);
        }

        /// <summary>通关结算：说句话、回出口图、散队。</summary>
        void Clear(long now)
        {
            if (rewardUntil == 0)
            {
                rewardUntil = now + RewardWaitMs;
                foreach (int id in bots)
                {
                    var bot = BotRegistry.Get(id);
                    if (bot == null) continue;
                    PqDirector.Say(bot, PqChatLines.afterClear[rng.Next(PqChatLines.afterClear.Length)]);
                }
                PqDirector.Notify(def.name + " 组队任务完成，队伍即将解散。");
                int exit = def.exitMap > 0 ? def.exitMap : def.recruitMap;
                if (PqDirector.LocalMapId() != exit) PqDirector.WarpPlayer(exit);
                foreach (int id in bots) { var bot = BotRegistry.Get(id); if (bot != null) PqDirector.WarpBot(bot, exit); }
                return;
            }
            if (now >= rewardUntil) End(true);
        }

        /// <summary>结束一次跑图：假人送回招募图，队伍解散。</summary>
        public void End(bool cleared)
        {
            if (finished) return;
            finished = true;
            // 先把假人从事件实例里摘出去：实例 dispose 只处理登记过的人，
            // 留着会让下一趟的 getPlayerCount 把上一趟的队员也算进来。
            ReleaseBots();
            int home = def.recruitMap;
            foreach (int id in bots)
            {
                var bot = BotRegistry.Get(id);
                if (bot == null) continue;
                if (!cleared) PqDirector.Say(bot, "这次没过，下次再来。");
                if (bot.mapId != home) PqDirector.WarpBot(bot, home);
            }
            if (def.exitMap > 0 && PqDirector.LocalMapId() != def.exitMap && PqDirector.LocalMapId() != home)
                PqDirector.WarpPlayer(def.exitMap);
            PqDirector.Finish(this);
        }

        // =====================================================================
        // 单个假人的行为入口（PqBot 每 tick 调）
        // =====================================================================

        /// <summary>
        /// 假人在副本里的行为：跑图期间只做「本 run 当前步骤要求的事」，
        /// 不自己找怪打（会和真人抢、也会架空阶段逻辑）。
        /// </summary>
        public void DriveBot(BotCharacter bot, long now)
        {
            if (bot == null || finished) return;
            if (bot.mapId != CurrentMap)
            {
                if (CurrentMap != 0) PqDirector.WarpBot(bot, CurrentMap);
                return;
            }
            if (stageWorkDone || now < nextActionAt) return;
            // 具体动作在 DriveWork 里按步骤统一驱动；这里只补一个「别站着发呆」的动作，
            // 让玩家看到假人确实在忙（挥手/技能/碎碎念）。
            if (rng.Next(100) < 12) BotAttack.BasicSwing(bot, rng);
        }

        // =====================================================================
        // 工具
        // =====================================================================

        BotCharacter RandomBot()
        {
            if (bots.Count == 0) return null;
            return BotRegistry.Get(bots[rng.Next(bots.Count)]);
        }

        static void MoveToward(BotCharacter bot, int x, int y)
        {
            if (bot == null) return;
            if (!BotMovementService.Observed(bot.mapId)) { bot.x = x; bot.y = y; return; }
            if (!BotMovementService.PrepareNavigation(bot)) return;
            int groundY = (int)MapService.SnapToGround(bot.mapId, x, y);
            if (BotMovementService.IsMovingToward(bot, x, groundY)) return;
            BotMovementService.Move(bot, x, groundY);
        }
    }
}
