using System;
using System.Collections.Generic;
using Server.Bot.Environment;
using Server.Bot.Social;
using Server.Field;

namespace Server.Bot.Types.Pq
{
    // 组队任务大厅（入口社交假人）的编排者。四个招募图各一摊：
    //   废弃都市 103000000 / 玩具塔101 221024500 / 毒雾森林 300030100 / 海盗船小港口 251010404。
    //
    // 职责（对应需求的入口部分）：
    //   · 玩家走进招募图 → 铺 10~15 个假人；离开且没队伍挂着 → 收摊。
    //   · 假人在入口闲逛、喊组队任务的话、做表情、放技能、丢东西。
    //   · 玩家发言命中关键词（求组/缺一/缺二/q1/q2/开个牧师…）→ 假人接话并开始凑队。
    //   · 玩家自己当队长 → 假人喊「求组」，玩家右键/双击面板邀请照常走组队邀请流程；
    //     为了避免「玩家不动手就永远满不了员」，还有一条限速的自动补员（每 1.6s 进一个）。
    //   · 玩家不是队长 / 没队伍 → 假人开队邀请玩家，玩家点确定就进队，随即把队长让给玩家
    //     （源端 startInstance 只认「队长是本地真实角色」，见 NativeEventScriptService:272）。
    //   · 满 6 人 → 静默找招募 NPC 开副本（脚本照跑），进本后交给 PqRun。
    //
    // 这一层只做「凑队 + 开本 + 收队」，副本内的活全在 PqRun，假人行为在 PqBot。
    public static class PqDirector
    {
        // ————— 注入（Field 层接线；Server/ 不引用 UnityEngine）—————
        /// <summary>把本地角色送到目标图（接 FieldDirector.WarpTo）。</summary>
        public static Action<int> WarpLocalPlayer;
        /// <summary>按需装载地图几何（接 BotRuntime.TryGetMap，非阻塞）。Server 不读 WZ。</summary>
        public static Func<int,bool> LoadMap;
        /// <summary>加载页把某张图所属分片排进后台解析（接 BotRuntime）。一次一张，别在同一帧读 7 个 TextAsset。</summary>
        public static Action<int> PrefetchMap;
        public static Func<int> LocalPlayerId = () => 0;
        public static Func<int> LocalMapId = () => 0;
        /// <summary>(itemId, count) → 本地角色背包（接 Profile.AddItem）。</summary>
        public static Action<int,int> GrantItem;
        /// <summary>静默代点 NPC：(npcId, openOnly, prefer, done)（接 PqNpcDriver.Begin）。</summary>
        public static Action<int,bool,string[],Action<bool>> TalkNpc;
        /// <summary>
        /// 假人自己点 NPC（以队员身份跑脚本）：(bot, npcId, out 最后一页原文)（接 PqBotNpcDriver.Talk）。
        /// 源端的队员分支（废都第一关领题/换证）只能走这条，队长代点永远进的是队长分支。
        /// </summary>
        public delegate bool BotTalkHandler(BotCharacter bot, int npcId, out string say);
        public static BotTalkHandler BotTalkNpc;
        /// <summary>给玩家发一条系统提示（粉色聊天行）。</summary>
        public static Action<string> Notice;

        const int PartySize = 6;              // BotPartyService.Join 的硬上限也是 6
        const long TickMs = 500;
        const long FillIntervalMs = 1600;     // 自动补员节奏
        const long EnterFallbackMs = 30000;   // 一直凑不满也要能进（不至于永远卡在入口）
        const long EnterHoldMs = 2200;        // 满员之后先喊两句再开本
        const long ChatMinMs = 6000, ChatMaxMs = 15000;

        sealed class Lobby
        {
            public PqDef def;
            public readonly List<int> bots = new List<int>();
            public int want;
            public int anchorX = int.MinValue, anchorY;
            public bool spawning;
            public long nextChatAt, fillAt, enterAt, fallbackAt;
            public int hostBot;                   // 开队的假人（队长让渡后就归零）
            public bool leadershipGiven;
        }

        static readonly Dictionary<string,Lobby> lobbies = new Dictionary<string,Lobby>();
        static readonly Dictionary<int,PqRun> runs = new Dictionary<int,PqRun>();
        static readonly Random rng = new Random();
        static BotTasks.Handle tickHandle;
        static int localMap = int.MinValue;
        static int pendingEnterParty;
        static long pendingEnterAt;
        // 加载页预热队列：四个副本共 37 张图，散在 7 个分片里。一次推一张，避免同一帧
        // 连续 PlatformRes.LoadText 七个 1MB 级资源把加载页也顶出卡顿。
        static List<int> prefetchIds;
        static int prefetchIndex;

        static DbStore Db => BotClient.GetBotClient()?.db;
        static BotPartyService Parties => BotTypeManager.Parties;
        public static int TemplateMapId(int mapId)
        {
            var map = MapService.Get(mapId);
            return map != null && map.templateMapId > 0 ? map.templateMapId : mapId;
        }

        public static bool Enabled => BotConfig.PqLobbyBotsEnabled(Db);

        // =====================================================================
        // 生命周期
        // =====================================================================

        public static void Reset()
        {
            tickHandle?.Cancel(); tickHandle = null;
            lobbies.Clear(); runs.Clear();
            localMap = int.MinValue; pendingEnterParty = 0; pendingEnterAt = 0;
            prefetchIds = null; prefetchIndex = 0;
        }

        /// <summary>
        /// 加载页每帧推一张组队任务地图的分片进后台解析队列。
        /// 玩家真的走到入口时分片已经 Ready，TryGetMap 直接命中，不会有任何主线程等待。
        /// </summary>
        public static void PrefetchStep()
        {
            if (PrefetchMap == null) return;
            if (prefetchIds == null)
            {
                prefetchIds = new List<int>();
                var seen = new HashSet<int>();
                void Add(int id) { if (id > 0 && seen.Add(id)) prefetchIds.Add(id); }
                foreach (var def in PqCatalog.All)
                {
                    Add(def.recruitMap); Add(def.entryMap); Add(def.exitMap);
                    foreach (var stage in def.stages) Add(stage.mapId);
                }
            }
            if (prefetchIndex >= prefetchIds.Count) return;
            int mapId = prefetchIds[prefetchIndex++];
            try { PrefetchMap(mapId); }
            catch (Exception) { /* 分片缺失：让进图时的 Missing 分支自然跳过 */ }
        }

        /// <summary>
        /// 加载页退场前把剩余分片一次排完。逐帧 PrefetchStep 可能因为加载页结束得太快
        /// （IsMarketStartupComplete 提前置位 / 玩家秒点进游戏）而没跑完 37 张；
        /// 这里兜底保证 7 个分片全部 Start + Pin，进图后不会再有主线程读 TextAsset。
        /// 只在加载页调用——那一帧本来就在转圈，读资源不会被感知成「游戏卡顿」。
        /// </summary>
        public static void PrefetchAll()
        {
            if (PrefetchMap == null) return;
            if (prefetchIds == null) PrefetchStep();
            while (prefetchIds != null && prefetchIndex < prefetchIds.Count)
            {
                int mapId = prefetchIds[prefetchIndex++];
                try { PrefetchMap(mapId); }
                catch (Exception) { /* 同上：缺的分片交给进图后的 Missing 分支 */ }
            }
        }

        static void EnsureTick()
        {
            if (tickHandle != null && !tickHandle.Cancelled && !tickHandle.Completed) return;
            tickHandle = BotTasks.ScheduleFixedDelay(Tick, TickMs, TickMs);
        }

        /// <summary>本地角色换图时调用（FieldDirector 每帧比对，值变了才进来）。</summary>
        public static void OnLocalMap(int mapId)
        {
            if (mapId == localMap) return;
            localMap = mapId;
            if (!Enabled) return;
            EnsureTick();
            var def = PqCatalog.ForMap(mapId);
            if (def != null) { EnsureLobby(def); return; }
            // 离开招募图：收掉这一摊里没进队的假人（进队的跟着 PqRun 走，不动）。
            // 例外是「玩家正带着队伍进副本」——新图是某副本的阶段图时大厅这一摊先留着，
            // PqDirector.Tick → AdoptStageEntry 还要从这里拉人补进队；等玩家真的离场
            // （换到既不是阶段图也不是招募图的图）再收。
            var entering = PqCatalog.ByStageMap(TemplateMapId(mapId));
            foreach (var lobby in new List<Lobby>(lobbies.Values))
                if (lobby.def.recruitMap != mapId && lobby.def != entering) RetireIdle(lobby);
        }

        // =====================================================================
        // 大厅
        // =====================================================================

        static Lobby LobbyOf(PqDef def)
        {
            if (def == null) return null;
            if (!lobbies.TryGetValue(def.key, out var lobby))
                lobbies[def.key] = lobby = new Lobby { def = def, want = BotConfig.PqLobbyBotCount(Db) };
            return lobby;
        }

        static void EnsureLobby(PqDef def, int attempt = 0)
        {
            var lobby = LobbyOf(def);
            if (lobby == null || lobby.spawning) return;
            if (lobby.want <= 0) return;
            // 地图几何没装载就没法算落点（假人会全部叠在 (0,0) 然后掉下去）。
            // LoadMap 是非阻塞的：分片还在后台解析就返回 false，这里下一帧再问，
            // 绝不在主线程 Join 解析线程。等够 180 帧（≈3s）仍不就绪就按「图不在包里」放弃。
            if (!MapService.Has(def.recruitMap))
            {
                bool loaded = false;
                try { loaded = LoadMap != null && LoadMap(def.recruitMap); }
                catch (ArgumentException) { loaded = false; }
                if (!loaded)
                {
                    // 玩家可能已经走开了：别在他离开之后还往旧图铺人。
                    if (attempt < 180 && localMap == def.recruitMap)
                        BotEnvironment.PostNextFrame(() => EnsureLobby(def, attempt + 1));
                    return;
                }
            }
            lobby.bots.RemoveAll(id => BotRegistry.Get(id) == null);
            if (lobby.bots.Count >= lobby.want) return;
            lobby.spawning = true;
            int index = lobby.bots.Count;
            BotEnvironment.Post(() => SpawnOne(lobby, index));
        }

        // 一个假人一帧：12 个假人的外观/纸娃娃一起合成会把进图那帧撑爆。
        static void SpawnOne(Lobby lobby, int index)
        {
            if (lobby.def == null) return;
            // A queued continuation may outlive the player's visit. Do not repopulate
            // an already retired lobby after a warp (or after this director resets).
            if (localMap != lobby.def.recruitMap || !Enabled ||
                !lobbies.TryGetValue(lobby.def.key, out var current) || !ReferenceEquals(current, lobby))
            { lobby.spawning = false; return; }
            var (x, y) = Seat(lobby, index);
            BotCharacter bot = null;
            try
            {
                bot = BotGeneration.CreateBotPollReadiness(x, y, lobby.def.recruitMap, true, false);
            }
            catch (Exception) { bot = null; }
            if (bot == null) { lobby.spawning = false; return; }
            // 等级卡在副本门槛之上一点：看起来像真的来打这个副本的人。
            bot.level = Math.Max(1, lobby.def.minLevel + rng.Next(0, 13));
            if (index == 0 && lobby.anchorX == int.MinValue) { lobby.anchorX = (int)bot.x; lobby.anchorY = (int)bot.y; }
            lobby.bots.Add(bot.id);
            BotTypeManager.SetAndStartBots(new List<int> { bot.id }, BotType.PQ_BOT);
            if (index + 1 < lobby.want) BotEnvironment.PostNextFrame(() => SpawnOne(lobby, index + 1));
            else lobby.spawning = false;
        }

        // 落点：以招募 NPC 为锚点铺开；没有 NPC 就用地图中线。
        static (int x,int y) Seat(Lobby lobby, int index)
        {
            var map = MapService.Get(lobby.def.recruitMap);
            int anchorX = lobby.anchorX;
            if (anchorX == int.MinValue)
            {
                var npc = map?.npcs.Find(n => n.id == lobby.def.recruitNpc);
                if (npc != null) { anchorX = npc.x; lobby.anchorY = npc.y; }
                else if (map != null && map.maxX > map.minX) anchorX = (int)((map.minX + map.maxX) / 2);
                else anchorX = 0;
                lobby.anchorX = anchorX;
            }
            int count = Math.Max(1, lobby.want);
            // 两人一排、横跨 ±420px：既围在 NPC 附近，又不会站成一条线。
            int row = index / 2, col = index % 2;
            int x = anchorX + (col == 0 ? -1 : 1) * (28 + row * 34) + rng.Next(-10, 11);
            int y = lobby.anchorY == 0 ? 0 : lobby.anchorY;
            return (x, (int)MapService.SnapToGround(lobby.def.recruitMap, x, y));
        }

        /// <summary>把没进队的假人清出去（换图/关开关时）。</summary>
        static void RetireIdle(Lobby lobby)
        {
            var party = Parties?.For(LocalPlayerId());
            for (int i = lobby.bots.Count - 1; i >= 0; i--)
            {
                var bot = BotRegistry.Get(lobby.bots[i]);
                if (bot == null) { lobby.bots.RemoveAt(i); continue; }
                if (party != null && party.members.Contains(bot.id)) continue;
                if (bot.mapId != lobby.def.recruitMap) { lobby.bots.RemoveAt(i); continue; }
                BotTypeManager.ManuallyStopBot(bot);
                BotGeneration.RemoveBotFromServer(bot);
                lobby.bots.RemoveAt(i);
            }
        }

        // =====================================================================
        // 关键词 → 组队
        // =====================================================================

        /// <summary>玩家在公共频道发言（BotRuntime.ReceiveChat 转进来）。</summary>
        public static void OnChat(BotActor player, string text)
        {
            if (!Enabled || player == null || player.IsBot() || string.IsNullOrEmpty(text)) return;
            var def = PqCatalog.ForMap(player.mapId);
            if (def == null) return;
            if (!Matches(def, text)) return;
            var lobby = LobbyOf(def);
            if (lobby == null) return;
            EnsureTick();
            React(lobby, player);
        }

        /// <summary>这句话是不是在喊这个副本（关键词命中任意一个即可）。回归脚本 PqChecks 也用它。</summary>
        public static bool Matches(PqDef def, string text)
        {
            if (def == null || string.IsNullOrEmpty(text)) return false;
            foreach (string keyword in def.keywords)
                if (!string.IsNullOrEmpty(keyword) && text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static void React(Lobby lobby, BotActor player)
        {
            var candidates = FreeBots(lobby, 3);
            if (candidates.Count == 0) return;
            // 先接话：让玩家看到「有人听到了」。
            foreach (var bot in candidates)
                BotTypeManager.Output.Speak(bot, PqChatLines.Answer(rng));

            var party = Parties?.For(player.id);
            if (party == null)
            {
                BotStartsParty(lobby, player, candidates[0]);
                return;
            }
            if (party.leader != player.id)
            {
                // 玩家在别人的队里还求组：假人照实回一句，不乱塞人。
                BotTypeManager.Output.Speak(candidates[0], "你不是队长呀，让队长来开。");
                return;
            }
            // 玩家自己是队长：假人喊求组，玩家右键/双击面板邀请照常走原生流程；
            // 这里再顺手塞一个进来，免得玩家一直不动手永远凑不齐。
            foreach (var bot in candidates)
            {
                if (party.members.Count >= PartySize) break;
                if (TryJoin(bot, party)) { lobby.enterAt = 0; break; }
            }
        }

        static void BotStartsParty(Lobby lobby, BotActor player, BotCharacter host)
        {
            if (Parties == null || host == null) return;
            if (!Parties.Create(host)) { BotTypeManager.Output.Speak(host, "我这边开不了队，等我一下。"); return; }
            var party = Parties.For(host.id);
            if (party == null) return;
            lobby.hostBot = host.id;
            lobby.leadershipGiven = false;
            BotTypeManager.Output.Speak(host, "我开个队，缺人，来一个。" + lobby.def.name + "的");
            Parties.Invite(host, player);
        }

        static bool TryJoin(BotCharacter bot, BotPartyService.Party party)
        {
            if (bot == null || party == null || Parties == null) return false;
            if (party.members.Count >= PartySize) return false;
            if (Parties.For(bot.id) != null) return false;
            if (!Parties.Join(bot, party.id)) return false;
            BotTypeManager.Output.Speak(bot, PqChatLines.Role(KeyOf(party), rng));
            return true;
        }

        static string KeyOf(BotPartyService.Party party)
        {
            if (party == null) return null;
            foreach (var lobby in lobbies.Values)
                if (lobby.bots.Contains(party.leader) || party.members.Exists(m => lobby.bots.Contains(m))) return lobby.def.key;
            return null;
        }

        static List<BotCharacter> FreeBots(Lobby lobby, int max)
        {
            var picked = new List<BotCharacter>();
            foreach (int id in lobby.bots)
            {
                if (picked.Count >= max) break;
                var bot = BotRegistry.Get(id);
                if (bot == null || bot.mapId != lobby.def.recruitMap) continue;
                if (Parties != null && Parties.For(id) != null) continue;
                if (partiesRunContains(id)) continue;
                picked.Add(bot);
            }
            return picked;
        }

        static bool partiesRunContains(int botId)
        {
            foreach (var run in runs.Values) if (run.Contains(botId)) return true;
            return false;
        }

        // =====================================================================
        // 定时推进
        // =====================================================================

        static void Tick()
        {
            if (!Enabled) return;
            long ms = BotScheduler.Now();
            foreach (var run in new List<PqRun>(runs.Values)) run.Tick(ms);
            var player = LocalPlayer();
            if (player == null) return;
            var def = PqCatalog.ForMap(player.mapId);
            if (def == null) { AdoptStageEntry(player, ms); return; }
            var lobby = LobbyOf(def);
            if (lobby == null) return;

            AnswerInvitations(lobby);
            HandOverLeadership(lobby, player, ms);
            ChatAmbient(lobby, ms);
            AutoFill(lobby, player, ms);
            CheckEnter(lobby, player, ms);
        }

        /// <summary>
        /// 玩家没走「假人凑队」这条路，自己带着队伍跑进了副本（点招募 NPC / 走传送门）：
        /// 这里把跑图接上，不然队里的假人会一直待在大厅不动。
        /// </summary>
        static void AdoptStageEntry(BotActor player, long ms)
        {
            var def = PqCatalog.ByStageMap(TemplateMapId(player.mapId));
            if (def == null || Parties == null) return;
            var party = Parties.For(player.id);
            if (party == null || runs.ContainsKey(party.id)) return;
            // 队长还挂在假人身上（假人开的队、玩家自己先进了本）：队长必须立刻让过来，
            // 否则 startInstance 认不到队长、阶段 NPC 全走「请让队长来」分支。
            if (party.leader != player.id)
            {
                var leader = Parties.Get(party.leader);
                if (leader == null || !leader.IsBot()) return;
                if (!Parties.ChangeLeader(party.leader, player.id)) return;
            }
            // 玩家自己开队、没邀请假人就点 NPC 进本时队伍里一个假人都没有：从这一摊大厅
            // 拉人补进队，不然 PqRun.Begin 收不到成员，副本里永远只有真人。
            var lobby = LobbyOf(def);
            if (lobby != null)
                foreach (int id in new List<int>(lobby.bots))
                {
                    if (party.members.Count >= PartySize) break;
                    var bot = BotRegistry.Get(id);
                    if (bot == null || bot.mapId != def.recruitMap) continue;
                    TryJoin(bot, party);
                }
            Start(def, party.id, player.id, openViaNpc: false);
        }

        /// <summary>玩家右键 / 双击面板发来的组队邀请：同意，或者给出拒绝理由。</summary>
        static void AnswerInvitations(Lobby lobby)
        {
            if (Parties == null) return;
            int playerId = LocalPlayerId();
            foreach (int id in lobby.bots)
            {
                var invite = Parties.Pending(id);
                if (invite == null) continue;
                var bot = BotRegistry.Get(id);
                if (bot == null) continue;
                // 只处理玩家自己发的邀请；假人之间的邀请由 Invite 的调用方直接处理。
                if (invite.inviter == null || invite.inviter.id != playerId) continue;
                // invite.party 是队伍编号，For() 收角色 id —— 用错会恒为 null，
                // 于是「玩家右键邀请假人」被一律判成队伍不存在而拒绝。
                var party = Parties.ById(invite.party);
                bool accept = party != null && party.members.Count < PartySize && Parties.For(id) == null;
                Parties.Answer(bot, invite.party, accept);
                if (accept)
                {
                    BotTypeManager.Output.Speak(bot, "谢了，我进队了。" + PqChatLines.Role(lobby.def.key, rng));
                    lobby.hostBot = 0; lobby.enterAt = 0; lobby.fallbackAt = 0;
                }
                else
                {
                    BotTypeManager.Output.Speak(bot, PqChatLines.Decline(rng));
                }
            }
        }

        /// <summary>
        /// 假人开的队，等玩家进来之后把队长让给玩家。
        /// 源端 startInstance 只认「队长是本地真实角色」（EventManager.java:738 → 
        /// NativeEventScriptService:272 用 LocalPlayerId 判定），队长留在假人身上副本永远开不起来。
        /// </summary>
        static void HandOverLeadership(Lobby lobby, BotActor player, long ms)
        {
            if (lobby.leadershipGiven || Parties == null) return;
            var party = Parties.For(player.id);
            if (party == null || party.leader == player.id) { lobby.leadershipGiven = lobby.hostBot == 0; return; }
            if (lobby.hostBot == 0 || party.leader != lobby.hostBot) return;
            if (!Parties.ChangeLeader(party.leader, player.id)) return;
            lobby.leadershipGiven = true;
            var host = BotRegistry.Get(lobby.hostBot);
            if (host != null) BotTypeManager.Output.Speak(host, "队长给你，NPC 只有队长点得动。");
            // 换队长之后 PartyService 的 leader 已经变了，复制一份给 Run 用。
            lobby.hostBot = 0;
            lobby.fallbackAt = ms + EnterFallbackMs;
        }

        static void ChatAmbient(Lobby lobby, long ms)
        {
            if (ms < lobby.nextChatAt) return;
            lobby.nextChatAt = ms + ChatMinMs + rng.Next((int)(ChatMaxMs - ChatMinMs));
            var bots = lobby.bots;
            if (bots.Count == 0) return;
            var bot = BotRegistry.Get(bots[rng.Next(bots.Count)]);
            if (bot == null || bot.mapId != lobby.def.recruitMap) return;
            bool recruit = rng.Next(100) < 55;
            BotTypeManager.Output.Speak(bot, recruit ? PqChatLines.Recruit(lobby.def.key, rng) : PqChatLines.Idle(rng));
        }

        /// <summary>玩家是队长且队里还有空位 → 限速补员（每 1.6s 进一个）。</summary>
        static void AutoFill(Lobby lobby, BotActor player, long ms)
        {
            if (Parties == null) return;
            var party = Parties.For(player.id);
            if (party == null || party.leader != player.id) { lobby.fillAt = 0; return; }
            if (party.members.Count >= PartySize) return;
            if (lobby.fallbackAt == 0) lobby.fallbackAt = ms + EnterFallbackMs;
            if (ms < lobby.fillAt) return;
            lobby.fillAt = ms + FillIntervalMs;
            var free = FreeBots(lobby, 1);
            if (free.Count == 0) return;
            TryJoin(free[0], party);
        }

        /// <summary>满员（或等了太久）就让招募 NPC 静默开本。</summary>
        static void CheckEnter(Lobby lobby, BotActor player, long ms)
        {
            if (Parties == null || lobby.def == null) return;
            if (pendingEnterParty != 0) return;
            var party = Parties.For(player.id);
            if (party == null || party.leader != player.id) { lobby.enterAt = 0; return; }
            if (runs.ContainsKey(party.id)) return;
            bool full = party.members.Count >= PartySize;
            bool late = lobby.fallbackAt != 0 && ms >= lobby.fallbackAt && party.members.Count >= 3;
            if (!full && !late) return;
            if (lobby.enterAt == 0)
            {
                lobby.enterAt = ms + EnterHoldMs;
                foreach (int id in lobby.bots)
                {
                    var bot = BotRegistry.Get(id);
                    if (bot != null && party.members.Contains(id))
                        BotTypeManager.Output.Speak(bot, PqChatLines.beforeEnter[rng.Next(PqChatLines.beforeEnter.Length)]);
                }
                return;
            }
            if (ms < lobby.enterAt) return;
            lobby.enterAt = 0;
            Start(def: lobby.def, partyId: party.id, leaderId: player.id);
        }

        /// <summary>开本：静默找招募 NPC 说话，脚本自己把队伍送进副本。</summary>
        public static PqRun Start(PqDef def, int partyId, int leaderId, bool openViaNpc = true)
        {
            if (def == null || partiesRunHas(partyId)) return null;
            var run = new PqRun(def, partyId, leaderId);
            runs[partyId] = run;
            run.Begin(openViaNpc);
            return run;
        }

        static bool partiesRunHas(int partyId) => runs.ContainsKey(partyId);

        public static PqRun RunOf(int partyId) => runs.TryGetValue(partyId, out var run) ? run : null;

        public static PqRun RunOfBot(int botId)
        {
            foreach (var run in runs.Values) if (run.Contains(botId)) return run;
            return null;
        }

        public static void Finish(PqRun run)
        {
            if (run == null) return;
            runs.Remove(run.partyId);
            // 出本之后自动解散：Leave 收的是「角色 id」，队长走了整队一起散
            // （BotPartyService.Leave 里 leader 分支会清空所有成员的 membership）。
            Parties?.Leave(run.leaderId);
            foreach (var lobby in lobbies.Values) { lobby.enterAt = 0; lobby.fillAt = 0; lobby.fallbackAt = 0; lobby.hostBot = 0; lobby.leadershipGiven = false; }
        }

        // =====================================================================
        // 工具
        // =====================================================================

        public static BotActor LocalPlayer()
        {
            int id = LocalPlayerId();
            if (id == 0) return null;
            var actor = Parties?.Get(id);
            return actor != null && actor.online && !actor.IsBot() ? actor : null;
        }

        /// <summary>把假人送到目标图（点由地图的传送门/中线决定）。</summary>
        public static bool WarpBot(BotCharacter bot, int mapId)
        {
            if (bot == null || mapId == 0 || mapId < 0 && !MapService.Has(mapId)) return false;
            if (bot.mapId != mapId)
            {
                if (!MapService.Has(mapId))
                {
                    bool loaded = false;
                    try { loaded = LoadMap != null && LoadMap(mapId); }
                    catch (ArgumentException) { loaded = false; }
                    if (!loaded) return false;
                }
                BotRegistry.EnterMap(bot, mapId);
            }
            var (x, y) = EntryPoint(mapId);
            Environment.BotMovementService.Stop(bot);
            bot.x = x; bot.y = y;
            return true;
        }

        /// <summary>目标图的落点：优先 0 号传送门，退到地图中线。</summary>
        public static (int x,int y) EntryPoint(int mapId)
        {
            var map = MapService.Get(mapId);
            if (map == null) return (0, 0);
            foreach (var portal in map.portals)
                if (portal.id == 0) return (portal.x, (int)MapService.SnapToGround(mapId, portal.x, portal.y));
            if (map.portals.Count > 0)
            {
                var portal = map.portals[0];
                return (portal.x, (int)MapService.SnapToGround(mapId, portal.x, portal.y));
            }
            int x = map.maxX > map.minX ? (int)((map.minX + map.maxX) / 2) : 0;
            return (x, (int)MapService.SnapToGround(mapId, x, map.minY));
        }

        /// <summary>让本地角色走到某张图（副本内的自动推进用）。</summary>
        public static void WarpPlayer(int mapId)
        {
            if (mapId <= 0) return;
            try { WarpLocalPlayer?.Invoke(mapId); }
            catch (Exception) { /* 前端还没准备好：下一 tick 再试 */ }
        }

        /// <summary>静默点一次 NPC；没有接线（离线自检）时按成功处理，避免整条流程卡死。</summary>
        public static void Talk(int npcId, bool openOnly, string[] prefer, Action<bool> done)
        {
            if (TalkNpc == null) { done?.Invoke(true); return; }
            try { TalkNpc(npcId, openOnly, prefer, done); }
            catch (Exception) { done?.Invoke(false); }
        }

        public static void Give(int itemId, int count)
        {
            if (itemId <= 0 || count <= 0) return;
            GrantItem?.Invoke(itemId, count);
        }

        /// <summary>假人去找 NPC 说话（队员分支）。没接线或脚本出错返回 false，调用方据此回退。</summary>
        public static bool BotTalk(BotCharacter bot, int npcId, out string say)
        {
            say = null;
            if (bot == null || npcId == 0 || BotTalkNpc == null) return false;
            try { return BotTalkNpc(bot, npcId, out say); }
            catch (Exception) { return false; }
        }

        // ——— 事件实例里的「队员身份」与地面拾取（接 NativeEventScripts / BotGroundPickup）———
        /// <summary>把假人登记进当前事件实例（源端 EventManager.registerParty 会登记全队）。</summary>
        public static Action<int> EnterInstance;
        public static Action<int> LeaveInstance;
        /// <summary>实例里这个角色抽到的题目号（eim.gridCheck 的直读版），不在实例返回 -1。</summary>
        public static Func<int,int> InstanceGrid;
        /// <summary>实例属性直读（"2stageclear"、阶段随机组合用的 "stg2Property"）。</summary>
        public static Func<string,string> InstanceProperty;
        /// <summary>假人自己捡脚下的道具：(bot, itemId, max) → 捡到的件数。</summary>
        public static Func<BotCharacter,int,int,int> PickupItem;

        public static bool EnterEvent(int botId)
        {
            if (EnterInstance == null) return false;
            try { EnterInstance(botId); return true; }
            catch (Exception) { return false; }
        }

        public static void LeaveEvent(int botId)
        {
            try { LeaveInstance?.Invoke(botId); }
            catch (Exception) { /* 实例已经 dispose：摘人失败不影响收尾 */ }
        }

        public static int GridOf(int botId)
        {
            try { return InstanceGrid != null ? InstanceGrid(botId) : -1; }
            catch (Exception) { return -1; }
        }

        public static string PropertyOf(string key)
        {
            try { return InstanceProperty != null ? InstanceProperty(key) : null; }
            catch (Exception) { return null; }
        }

        public static int Pickup(BotCharacter bot, int itemId, int max = int.MaxValue)
        {
            if (bot == null || itemId <= 0 || max <= 0 || PickupItem == null) return 0;
            try { return PickupItem(bot, itemId, max); }
            catch (Exception) { return 0; }
        }

        /// <summary>改假人自己的背包（队员换证/交证时用，落在 BotCharacter.inventory）。</summary>
        public static Action<BotCharacter,int,int> BotItem;

        public static void BotGain(BotCharacter bot, int itemId, int count)
        {
            if (bot == null || itemId <= 0 || count == 0 || BotItem == null) return;
            try { BotItem(bot, itemId, count); }
            catch (Exception) { /* 假人背包不是权威状态：写失败也不能影响真人流程 */ }
        }

        public static void Say(BotCharacter bot, string text)
        {
            if (bot == null || string.IsNullOrEmpty(text)) return;
            BotTypeManager.Output.Speak(bot, text);
        }

        public static void Notify(string text)
        {
            if (!string.IsNullOrEmpty(text)) Notice?.Invoke(text);
        }

        internal static Random Random => rng;
    }
}
