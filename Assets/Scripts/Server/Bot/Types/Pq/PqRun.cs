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
        const int TalkRetries = 3;             // NPC 没认账时的重试次数

        public readonly PqDef def;
        public readonly int partyId, leaderId;
        readonly List<int> bots = new List<int>();
        readonly Random rng = new Random();
        readonly Dictionary<int,long> nextSwing = new Dictionary<int,long>();
        readonly Dictionary<int,int> combatClaims = new Dictionary<int,int>();

        public int stageIndex;
        public int stepIndex;
        public bool finished;
        public string lastNote = "";

        long stepStartedAt, stepDeadline, nextActionAt, nextSayAt, nextChatterAt, offMapAt;
        long stageClearedAt, startDeadline, rewardUntil, talkRetryAt;
        readonly Dictionary<int,long> nextSweepAt = new Dictionary<int,long>();
        long noTargetSince;   // 看不到可打目标时的扫图/诊断节流
        bool noTargetNoted;
        bool started, stageWorkDone, talking, talkSent, talkDone;
        int talkStage = -1, talkRetries;

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
                // gateMs：有些阶段要玩家自己踩脚本传送门（海盗船的 next00 会顺手刷 BOSS、
                // 玩具塔第 6 关是纯传送门迷宫），多给一点时间，别一清完就把人硬传走。
                long gate = stage.gateMs > 0 ? stage.gateMs : StageIdleMs;
                if (now - stageClearedAt < gate) return;
                AdvanceStage(now);
                return;
            }

            // —— 干活 ——
            DriveWork(stage, now);
        }

        /// <summary>
        /// 本阶段的目标图（真实 id）。客户端在副本里用的是每实例一份的 Scope 负 id，
        /// 两套 id 只在问战斗快照 / 地面掉落 / 反应堆时经 <see cref="Live"/> 转换一次。
        /// </summary>
        int CurrentMap => CurrentStage?.mapId ?? 0;
        public PqStage CurrentStage => stageIndex >= 0 && stageIndex < def.stages.Count ? def.stages[stageIndex] : null;

        /// <summary>真实图 id → 刷怪/掉落/反应堆实际使用的图 id（副本内是 Scope）。</summary>
        static int Live(int mapId) => PqDirector.Live(mapId);

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
            talkRetries = 0; talkRetryAt = 0;
            couponNeed.Clear(); passReady.Clear(); asked.Clear();
            questScriptFailed = false; checkAttempts = 0;
            // 0 = 「这一步还没开跑」：DriveWork 会用第一次 tick 的时间把截止时间算出来，
            // 并顺手把这一步的台词说出去。
            stepStartedAt = 0; stepDeadline = 0;
            nextSweepAt.Clear(); noTargetSince = 0; noTargetNoted = false;
            combatClaims.Clear();
            nextActionAt = now; nextSayAt = now; nextChatterAt = now + 4000;
            lastNote = CurrentStage?.title ?? "";
            // 进图先把这一关的箱子恢复到初始状态：源端实例图是 resetFully 过的，
            // 移植端的反应堆挂在真实图 id 下、reactorTime<=0 的破坏后不会自己长回来，
            // 不重置的话同一趟会话里第二遍跑玩具塔会一个箱子都没有。
            // 本关要跨的子图（陷阱房/黑暗塔/六个子图/子舱）一并重置。
            var stage = CurrentStage;
            if (stage != null)
            {
                MapReactorService.RespawnAll(Live(stage.mapId));
                foreach (var each in stage.steps)
                    foreach (int map in each.maps)
                        if (map != stage.mapId) MapReactorService.RespawnAll(Live(map));
            }
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
                PqAction.Talk => WorkTalk(step, now),
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
            nextSweepAt.Clear(); noTargetSince = 0; noTargetNoted = false;
            combatClaims.Clear();
            // 一关里可能有多次 Talk（废都 2/3/4 第一次抽组合、第二次核对），
            // 所以 talkSent/talkDone 必须跟着步骤重置，否则第二次对话永远不发。
            talkSent = false; talkDone = false;
            talkRetries = 0; talkRetryAt = 0;
            nextActionAt = now; nextSayAt = now;
        }

        void Say(PqStep step, long now)
        {
            if (string.IsNullOrEmpty(step.say) || now < nextSayAt) return;
            nextSayAt = now + StepSayGapMs;
            var bot = RandomBot();
            PqDirector.Say(bot, step.say);
        }

        // ———————————————— 打怪 / 砸箱子（可跨子图）————————————————

        // 一个步骤可能要横跨好几张图（玩具塔第 4 关的黑暗塔五间、第 5 关的六个子图、
        // 海盗船的子舱）。假人按「哪张图还有活」分头走，人多的图多分几个。
        List<int> StepMaps(PqStep step)
        {
            var list = new List<int>();
            if (step.maps != null)
                foreach (int map in step.maps) if (map > 0) list.Add(map);
            if (list.Count == 0) list.Add(CurrentMap);
            return list;
        }

        /// <summary>把假人分到「还有活」的图上：已经在干活的原地不动，其余补到活最多、人最少的图。</summary>
        void AssignMaps(List<int> maps, Func<int,int> remainingOf)
        {
            var remaining = new Dictionary<int,int>();
            var load = new Dictionary<int,int>();
            foreach (int map in maps) { remaining[map] = remainingOf(map); load[map] = 0; }
            foreach (int id in bots)
            {
                var bot = BotRegistry.Get(id);
                if (bot == null) continue;
                if (remaining.TryGetValue(bot.mapId, out int left) && left > 0) load[bot.mapId]++;
            }
            foreach (int id in bots)
            {
                var bot = BotRegistry.Get(id);
                if (bot == null) continue;
                if (remaining.TryGetValue(bot.mapId, out int here) && here > 0) continue;
                int pick = 0;
                foreach (int map in maps)
                {
                    if (remaining[map] <= 0) continue;
                    if (pick == 0) { pick = map; continue; }
                    // 活多、人少的优先（交叉相乘比较，避免除零）
                    if ((long)load[map] * remaining[pick] < (long)load[pick] * remaining[map]) pick = map;
                }
                if (pick == 0) continue;
                if (bot.mapId != pick && !PqDirector.WarpBot(bot, pick)) continue;
                load[pick]++;
            }
        }

        // 怪物实例由 Unity 侧提供（BotCombatField → BotPartyGrindService），
        // 这里只做选择与出手，伤害结算仍走同一条 Hit 通路。
        bool WorkMobs(PqStep step, long now, bool boss)
        {
            var grind = BotTypeManager.PartyGrind;
            if (grind == null) return true;
            var player = PqDirector.LocalPlayer();
            if (player == null) return false;
            var maps = StepMaps(step);
            var live = new Dictionary<int,List<BotGrindMob>>();
            int total = 0;
            foreach (int map in maps)
            {
                List<BotGrindMob> mobs;
                try { mobs = grind.Monsters?.Invoke(Live(map), player.world, player.channel); }
                catch (Exception) { mobs = null; }
                var list = new List<BotGrindMob>();
                if (mobs != null)
                    foreach (var mob in mobs)
                    {
                        if (mob == null || !mob.alive || mob.fake) continue;
                        if (boss != mob.boss) continue;
                        // KillMobs 只认表里点名的怪（玩具塔第 4 关的 9300008/9300014 就靠它）。
                        if (!IsWanted(step, mob)) continue;
                        list.Add(mob);
                    }
                live[map] = list;
                total += list.Count;
            }
            if (total == 0) return true;
            if (now < nextActionAt) return false;
            nextActionAt = now + ActionIntervalMs;
            AssignMaps(maps, map => live[map].Count);

            foreach (int id in bots)
            {
                var bot = BotRegistry.Get(id);
                if (bot == null) continue;
                if (!live.TryGetValue(bot.mapId, out var targets) || targets.Count == 0) continue;
                var target = Nearest(bot, targets);
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
            var taken = new HashSet<int>();
            foreach (int id in bots)
                if (id != bot.id && BotRegistry.Get(id)?.mapId == bot.mapId &&
                    combatClaims.TryGetValue(id, out int claim)) taken.Add(claim);
            // 保留活目标，避免每拍在不同平台之间改道；空闲队友优先找未认领目标。
            combatClaims.TryGetValue(bot.id, out int held);
            BotGrindMob best = null; float dist = float.MaxValue;
            foreach (var mob in mobs)
            {
                if (mob == null || !mob.alive || taken.Contains(mob.id)) continue;
                if (mob.id == held) return mob;
                float d = Math.Abs(mob.x - bot.x) + Math.Abs(mob.y - bot.y) * 2f;
                // 不同怪也可能挤在同一小块平台；把队友已认领目标周围的拥挤成本
                // 算进去，让后续队员优先承担别处的怪。已认领目标仍保持，不每拍改道。
                foreach (var other in mobs)
                    if (other != null && other.alive && taken.Contains(other.id))
                        d += Math.Max(0, 1400 - Math.Abs(mob.x - other.x) - Math.Abs(mob.y - other.y) * 2);
                if (d < dist) { dist = d; best = mob; }
            }
            // 怪少于队员时允许合击，不让多余队员站桩。
            if (best == null)
                foreach (var mob in mobs)
                {
                    if (mob == null || !mob.alive) continue;
                    float d = Math.Abs(mob.x - bot.x) + Math.Abs(mob.y - bot.y) * 2f;
                    if (d < dist) { dist = d; best = mob; }
                }
            if (best != null) combatClaims[bot.id] = best.id;
            else combatClaims.Remove(bot.id);
            return best;
        }

        /// <summary>
        /// 阶段表里 step.ids 写的是**怪物模板 id**（9300001 这种），BotGrindMob.mobId 才是它；
        /// BotGrindMob.id 是 Unity 运行时句柄。以前拿 id 比模板 id，永远不相等，
        /// 于是「按怪种过滤」的步骤（废都第一关的鳄鱼、玩具塔第 4 关）一只怪都选不出来，
        /// 假人走到 NPC 旁边就站着不动。这里统一走 mobId，自检里没填 mobId 时才退回 id。
        /// </summary>
        public static bool IsWanted(PqStep step, BotGrindMob mob)
        {
            if (step.ids.Length == 0) return true;
            int key = mob.mobId > 0 ? mob.mobId : mob.id;
            return Array.IndexOf(step.ids, key) >= 0;
        }

        // ———————————————— 砸反应堆 ————————————————

        /// <summary>
        /// 砸反应堆。砸开那一下由 MapReactorService.Hit → ReactorScriptManager.act 跑
        /// reactor/&lt;id&gt;.js：2202003 自己 dropItems 掉券、2201001 刷 3 只 9300007、
        /// 3002001 掉魔力石、3001000 召石头人。这里不另配掉落，免得和脚本各掉一份。
        /// </summary>
        bool WorkReactors(PqStep step, long now)
        {
            var maps = StepMaps(step);
            var live = new Dictionary<int,List<MapReactor>>();
            int total = 0;
            foreach (int map in maps)
            {
                var list = new List<MapReactor>();
                foreach (var reactor in MapReactorService.Get(Live(map)))
                {
                    if (!InStep(step, reactor.id)) continue;
                    if (reactor.Hittable) list.Add(reactor);
                }
                live[map] = list;
                total += list.Count;
            }
            if (total == 0) return true;
            if (now < nextActionAt) return false;
            nextActionAt = now + ActionIntervalMs;
            AssignMaps(maps, map => live[map].Count);

            foreach (int id in bots)
            {
                var bot = BotRegistry.Get(id);
                if (bot == null) continue;
                if (!live.TryGetValue(bot.mapId, out var targets) || targets.Count == 0) continue;
                var target = NearestReactor(bot, targets);
                if (target == null) continue;
                if (Math.Abs(target.x - bot.x) > 60)
                {
                    MoveToward(bot, target.x, target.y);
                    continue;
                }
                bot.stance = target.x < bot.x ? 5 : 4;
                bot.facing = target.x < bot.x ? -1 : 1;
                BotAttack.BasicSwing(bot, rng);
                try { MapReactorService.Hit(Live(bot.mapId), target.objectId, null, bot.id, partyId); }
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

        /// <summary>
        /// 队长点 NPC。openOnly 只走引言页（脚本的 statusStgN: -1 → 0），不做判定；
        /// 带 awaitProperty 的步骤要等实例属性真的变过来才算完成，否则隔一会儿重点一次
        /// （物品差一点 / 站位偏一点都会被脚本打回，重试比硬往下走更接近真人）。
        /// </summary>
        bool WorkTalk(PqStep step, long now)
        {
            if (step.npc == 0) return true;
            if (talkDone)
            {
                if (string.IsNullOrEmpty(step.awaitProperty)) return true;
                string value = PqDirector.PropertyOf(step.awaitProperty);
                bool ok = step.awaitValue != null ? value == step.awaitValue : value != null;
                if (ok) return true;
                if (talkRetries >= TalkRetries)
                {
                    lastNote = (CurrentStage?.title ?? "") + "（NPC 没认账，先往下走）";
                    PqDirector.Notify(def.name + "：" + step.awaitProperty + " 还是 "
                        + (value ?? "空") + "，NPC 没有放行。");
                    return true;
                }
                talkRetries++;
                talkSent = false; talkDone = false;
                talkRetryAt = now + 2500;
                return false;
            }
            if (now < talkRetryAt) return false;
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
            return false;
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

        /// <summary>
        /// 假人自己去凑 item：优先捡地上已经掉出来的，其次砸箱子 / 打怪让它掉。
        /// step.need 是「全队合计」，凑够就交给队长（下一拍 HandOver）。
        /// 可以跨子图（玩具塔第 4/5 关、海盗船子舱）：哪张图还有活就往哪张图派人。
        /// </summary>
        bool WorkBotCollect(PqStep step, long now)
        {
            int item = step.itemId > 0 ? step.itemId : PqCatalog.PassCoupon;
            if (bots.Count == 0 || item <= 0) return true;
            if (CollectDone(step, item)) return true;
            if (now < nextActionAt) return false;
            nextActionAt = now + ActionIntervalMs;

            // 每张图还剩多少活：地上没人捡的 + 还没打掉的目标。
            var maps = StepMaps(step);
            var work = new Dictionary<int,int>();
            foreach (int map in maps) work[map] = GroundCount(map, item) + TargetCount(step, map);
            AssignMaps(maps, map => work[map]);

            foreach (int id in bots)
            {
                var bot = BotRegistry.Get(id);
                if (bot == null) continue;
                int shortfall = CollectShortfall(step, item, bot);
                if (shortfall <= 0) { combatClaims.Remove(id); continue; }
                // 先捡：脚本判定是 itemQuantity == answer 严格相等（废都）/ haveItem（玩具塔），
                // 按「还差多少」封顶，多捡一张反而交不掉。
                if (PqDirector.Pickup(bot, item, shortfall) > 0) continue;
                // 已在射程内就出手，不能被邻层/远处一张券每拍抢走攻击机会。
                if (step.reactors.Length == 0 && StrikeNearest(bot, step, now, inRangeOnly: true)) continue;
                // 券掉在拾取半径外：先走过去。不然怪打完了人还杵在原地，券永远进不了包。
                if (WalkToDrop(bot, item)) continue;
                // 地上没东西了就干活：有箱子砸箱子，没箱子打怪；
                // 一只可打的都找不到就自己扫图，不要站在原地等（刷怪点是分段的）。
                if (step.reactors.Length > 0) BreakNearest(bot, step);
                else if (!StrikeNearest(bot, step, now) && !PqDirector.WalkToDrop(bot, item)) Sweep(bot, now);
            }
            return false;
        }

        /// <summary>
        /// 这一步算不算收齐了。
        /// 表里写了 need 的是「全队合计」（玩具塔/毒雾/海盗）；
        /// 废都第一关每人题目不同，需要各自凑够自己那份，合计没有意义。
        /// </summary>
        bool CollectDone(PqStep step, int item)
        {
            if (step.need > 0) return HeldAll(item) >= step.need;
            if (couponNeed.Count == 0) return HeldAll(item) >= 1;   // 题目没读回来：先收一张别卡死
            int seen = 0;
            foreach (int id in bots)
            {
                var bot = BotRegistry.Get(id);
                if (bot == null) continue;
                if (!couponNeed.TryGetValue(id, out int need) || need <= 0) continue;
                seen++;
                if (Held(bot, item) < need) return false;
            }
            return seen > 0 || HeldAll(item) >= 1;
        }

        /// <summary>这个假人还差几张（表里有 need 时是全队口径）。</summary>
        int CollectShortfall(PqStep step, int item, BotCharacter bot)
        {
            if (step.need > 0) return step.need - HeldAll(item);
            if (couponNeed.TryGetValue(bot.id, out int need) && need > 0) return need - Held(bot, item);
            return Held(bot, item) > 0 ? 0 : 1;
        }

        /// <summary>
        /// 地上还有这个道具、但不在拾取半径内：朝最近的那件走过去。
        /// 返回 true 表示这一拍已经在赶路（别再切去打怪）。
        /// 掉落有两套（权威掉落挂在 Scope 图 id 下、老式掉落根本没有实体），
        /// Server 层只看得见前者，所以这个判断交给 Unity 侧注入的实现。
        /// </summary>
        bool WalkToDrop(BotCharacter bot, int item)
            => PqDirector.WalkToDrop(bot, item, 340f);

        int HeldAll(int itemId)
        {
            int total = 0;
            foreach (int id in bots)
            {
                var bot = BotRegistry.Get(id);
                if (bot != null) total += Held(bot, itemId);
            }
            return total;
        }

        static int GroundCount(int mapId, int itemId)
        {
            int count = 0;
            try
            {
                foreach (var drop in MapItemService.DroppedItems(Live(mapId)))
                    if (drop != null && !drop.pickedUp && !drop.IsMeso && drop.ItemId == itemId) count++;
            }
            catch (Exception) { return 0; }
            return count;
        }

        int TargetCount(PqStep step, int mapId)
        {
            int count = 0;
            if (step.reactors.Length > 0)
            {
                foreach (var reactor in MapReactorService.Get(Live(mapId)))
                    if (reactor.Hittable && InStep(step, reactor.id)) count++;
            }
            if (step.ids.Length > 0 || step.reactors.Length == 0)
            {
                var grind = BotTypeManager.PartyGrind;
                var player = PqDirector.LocalPlayer();
                if (grind != null && player != null)
                {
                    List<BotGrindMob> mobs = null;
                    try { mobs = grind.Monsters?.Invoke(Live(mapId), player.world, player.channel); }
                    catch (Exception) { mobs = null; }
                    if (mobs != null)
                        foreach (var mob in mobs)
                        {
                            if (mob == null || !mob.alive || mob.fake) continue;
                            if (!IsWanted(step, mob)) continue;
                            count++;
                        }
                }
            }
            return count;
        }

        void BreakNearest(BotCharacter bot, PqStep step)
        {
            if (bot == null) return;
            MapReactor best = null; float dist = float.MaxValue;
            foreach (var reactor in MapReactorService.Get(Live(bot.mapId)))
            {
                if (!reactor.Hittable || !InStep(step, reactor.id)) continue;
                float d = Math.Abs(reactor.x - bot.x) + Math.Abs(reactor.y - bot.y) * .4f;
                if (d < dist) { dist = d; best = reactor; }
            }
            if (best == null) return;
            if (Math.Abs(best.x - bot.x) > 60) { MoveToward(bot, best.x, best.y); return; }
            bot.stance = best.x < bot.x ? 5 : 4;
            bot.facing = best.x < bot.x ? -1 : 1;
            BotAttack.BasicSwing(bot, rng);
            try { MapReactorService.Hit(Live(bot.mapId), best.objectId, null, bot.id, partyId); }
            catch (Exception) { /* 反应堆这一帧被拆了 */ }
        }

        /// <summary>打这一架（走过去或出手）。返回 false = 视野里没有可选目标。</summary>
        bool StrikeNearest(BotCharacter bot, PqStep step, long now, bool inRangeOnly = false)
        {
            var grind = BotTypeManager.PartyGrind;
            var player = PqDirector.LocalPlayer();
            if (grind == null || player == null || bot == null) return true;
            List<BotGrindMob> mobs = null;
            // 副本里怪挂在每实例一份的 Scope 图 id 下，假人手上是真实图 id，必须过 Live()
            // （BotCombatField.Snapshot 的 field.StateId != mapId 会直接返回空表 ⇒ 假人看不到怪）。
            try { mobs = grind.Monsters?.Invoke(Live(bot.mapId), player.world, player.channel); }
            catch (Exception) { mobs = null; }
            if (mobs == null) { NoteNoTarget(now, bot, 0); return false; }
            var candidates = new List<BotGrindMob>();
            int onMap = 0;
            foreach (var mob in mobs)
            {
                if (mob == null || !mob.alive || mob.fake || mob.boss) continue;
                onMap++;
                if (IsWanted(step, mob)) candidates.Add(mob);
            }
            var best = Nearest(bot, candidates);
            if (best == null) { NoteNoTarget(now, bot, onMap); return false; }
            bot.equips.TryGetValue(-11, out int weapon);
            var range = BotAttack.EngageRange(bot.job, weapon);
            if (Math.Abs(best.x - bot.x) > range.x || Math.Abs(best.y - bot.y) > range.y)
            {
                if (inRangeOnly) return false;
                MoveToward(bot, best.x, best.y);
                return true;
            }
            BotMovementService.Stop(bot);
            bot.stance = best.x < bot.x ? 5 : 4;
            bot.facing = best.x < bot.x ? -1 : 1;
            if (nextSwing.TryGetValue(bot.id, out long ready) && now < ready) return true;
            try { grind.Strike(bot, best); nextSwing[bot.id] = now + 800; }
            catch (Exception) { /* 目标在这帧被真人打死 */ }
            return true;
        }

        /// <summary>
        /// 视野里一只可打的目标都没有时不要站桩：每 2.6 秒换一个方向扫一段本层。
        /// 刷怪点是分段的（废都的鳄鱼、玩具塔的分层），假人守着 NPC 那一段永远等不到怪；
        /// 另外这也是「过滤条件写错」时的可见症状替代品——以前会静静地站着，看不出哪里错。
        /// </summary>
        void Sweep(BotCharacter bot, long now)
        {
            if (nextSweepAt.TryGetValue(bot.id, out long ready) && now < ready) return;
            nextSweepAt[bot.id] = now + 2600;
            var map = MapService.Get(bot.mapId);
            int lo = map != null && map.maxX > map.minX ? (int)map.minX + 60 : (int)bot.x - 400;
            int hi = map != null && map.maxX > map.minX ? (int)map.maxX - 60 : (int)bot.x + 400;
            if (hi <= lo) return;
            int mid = (lo + hi) / 2;
            // 一半地图的步长，走到另一侧再折回；贴着边界时反向。
            int span = Math.Max(120, (hi - lo) / 2);
            int dir = bot.x >= mid ? -1 : 1;
            int x = (int)bot.x + dir * span;
            if (x < lo || x > hi) { dir = -dir; x = (int)bot.x + dir * span; }
            x = Math.Max(lo, Math.Min(hi, x));
            MoveToward(bot, x, (int)bot.y);
        }

        void NoteNoTarget(long now, BotCharacter bot, int visible)
        {
            if (noTargetSince == 0) noTargetSince = now;
            if (noTargetNoted || now - noTargetSince < 8000) return;
            noTargetNoted = true;
            PqDirector.Notify(def.name + "：这一步在图 " + (bot != null ? bot.mapId : 0) +
                "（实例 " + (bot != null ? Live(bot.mapId) : 0) + "）里看不到可打的怪" +
                (visible > 0 ? "（场上有 " + visible + " 只，但都不是这一步要的）" : "") +
                "，假人先自己扫图找。");
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
            // 有人没凑够：继续打怪捡券，不在这里干等，也别把这一步判成完成
            // （WorkMobs 在「一张图都没怪」时会返回 true，直接 return 就会带着空手进交证步）。
            if (shortAny)
            {
                // 没怪可打时同样别站着：自己扫图去这一段之外找券的来源。
                if (WorkMobs(step, now, boss: false))
                    foreach (int each in bots)
                    {
                        var bot = BotRegistry.Get(each);
                        if (bot == null || bot.mapId != CurrentMap) continue;
                        int rest = couponNeed.TryGetValue(each, out int n2) ? n2 : 0;
                        if (rest > 0 && Held(bot, PqCatalog.PassCoupon) >= rest) continue;
                        Sweep(bot, now);
                    }
                return false;
            }

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
            if (!BotAllowedHere(bot))
            {
                if (CurrentMap != 0) PqDirector.WarpBot(bot, CurrentMap);
                return;
            }
            if (stageWorkDone || now < nextActionAt) return;
            // 具体动作在 DriveWork 里按步骤统一驱动；这里只补一个「别站着发呆」的动作，
            // 让玩家看到假人确实在忙（挥手/技能/碎碎念）。
            if (rng.Next(100) < 12) BotAttack.BasicSwing(bot, rng);
        }

        /// <summary>
        /// 这个假人现在该不该待在这张图：当前步骤点了名的子图（玩具塔的分层、海盗船的子舱）
        /// 也算「该在的地方」，否则一律拉回主图。
        /// </summary>
        bool BotAllowedHere(BotCharacter bot)
        {
            if (bot == null) return false;
            if (bot.mapId == CurrentMap) return true;
            var stage = CurrentStage;
            if (stage == null || stepIndex < 0 || stepIndex >= stage.steps.Count) return false;
            var step = stage.steps[stepIndex];
            if (step.maps == null || step.maps.Length == 0) return false;
            return Array.IndexOf(step.maps, bot.mapId) >= 0;
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
