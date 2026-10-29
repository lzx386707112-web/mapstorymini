// DropGameBot.java（611 行）+ DropGameSpectatorSystem.java 对齐移植。
//
// 与源端的差异（全部源于「没有独立线程 / 没有阻塞 sleep」）：
//   1. 源端 tradeInitialized() 里接管交易（不创建 BotTradeSM）。C# 基类没有这个钩子，
//      本类改成：在 IDLE 自己接受排队中的交易邀请（tradeLogic.CheckQueue），并立刻
//      tradeHandler.Reset() 清掉 confirmedPartner —— 基类 State.RUNNING 只有在
//      VerifyPartner() 为真时才会 new BotTradeStateMachine，于是永远走不进去；
//      交易流程完全由本类 9 态机驱动（与源端等价）。
//   2. 源端 sleepAmountSeconds(x) 阻塞 tick；这里一律换成 BotTasks.Schedule 续接（Later()），
//      用 scripted 标记在这段时间内不推进内部状态，等价于「这个 tick 卡在这里」。
//   3. 源端 2 分钟移动播放开 ExecutorService 线程；这里 BotMovementPlayback 本身就是
//      非阻塞的分帧续播，直接 Play。
//   4. 源端掉落用 ScheduledExecutorService 递归 schedule；这里用 BotTasks.Schedule 递归
//      挂单次任务（间隔 4~6s 随机，语义完全相同）。
//   5. Haste 由宿主接现有 NativeBuffs，原技能动作仍走 BotOutput。
using System;
using System.Collections.Generic;
using Server.Bot.Environment;
using Server.Bot.Social;
using Server.Bot.Trade;

namespace Server.Bot.Types
{
    public sealed class DropGameBot : BotStateMachine
    {
        // ——————————————— 配置（逐条对齐源端常量）———————————————
        const int MediumTierCost = 10_000_000;
        const int EliteTierCost = 50_000_000;
        const int GameDurationMs = 120_000;
        const int DropIntervalMinMs = 4000;
        const int DropIntervalMaxMs = 6000;
        const int PartyTimeoutMs = 15_000;
        const int HasteSkillId = 4101004;
        const string MovementRecordingName = "dg_potshop_1";
        const int DropInitialDelayMinMs = 3000;
        const int DropInitialDelayMaxMs = 6000;
        const int MediumDespawnDelayMs = 1400;
        const int EliteDespawnDelayMs = 1150;

        static readonly int[] FallbackDropItemIds =
        { 2000004, 2000005, 2022000, 2022179, 2040002, 2040505, 2049000, 2070005 };

        const string DialoguePack = "DropGameBotDialogue.yaml";
        const string SpectatorDialoguePack = "DropGameSpectatorDialogue.yaml";
        const string SpectatorBotType = "DropGameSpectator";

        // 源端枚举：RESET/IDLE_ADVERTISE/TRADE_WAIT/TRADE_VALIDATE/PARTY_SETUP/
        //           PARTY_WAIT/PRE_GAME/ACTIVE_GAME/END_GAME
        public enum DropGameState
        {
            Reset, IdleAdvertise, TradeWait, TradeValidate, PartySetup, PartyWait, PreGame, ActiveGame, EndGame
        }

        readonly Random random = new Random();
        readonly BotPartyService parties;
        readonly BotDialogue dialogue;

        public DropGameState GameState { get; private set; } = DropGameState.Reset;
        public string SelectedTier { get; private set; }
        public BotActor Player { get; private set; }
        public DropGameLootPool LootPool { get; private set; }

        long stateEndTime;
        BotTasks.Handle dropTask, sequenceTask;
        // 源端在这些地方是阻塞 sleep：期间整条 tick 不推进，这里用同义标记。
        bool scripted;
        bool paymentSucceeded;

        public DropGameBot(BotCharacter c, BotPartyService parties = null, BotDialogue dialogue = null) : base(c)
        {
            this.parties = parties;
            this.dialogue = dialogue ?? BotTypeManager.Dialogue;
            botType = "DropGameBot";
        }

        // 源端 tick 靠 checkPrioritySpeed 在无人时降到 10s，交易握手会明显变钝；
        // 这里钉死 1s（与源端动辄 1s 的 sleep 粒度一致）。
        protected override long DefaultPeriodMs() => 1000;
        public override void CheckPrioritySpeed() => UpdateScheduleDelay(1000);

        // 只有真正「空闲等客」时才对围观动作开放（源端由各类型自行实现）。
        public override bool IsAvailableForAmbientActions() => GameState == DropGameState.IdleAdvertise;

        void DebugPrint(string msg) => DebugLog("DropGameBot[" + chr.name + "]: " + msg);

        void SetState(DropGameState next)
        {
            if (GameState != next) DebugPrint("STATE " + GameState + " -> " + next);
            GameState = next;
        }

        // 源端 sleepAmountSeconds(x) 的替代：可取消的延时续接。
        void Later(long ms, Action action)
        {
            sequenceTask?.Cancel();
            sequenceTask = BotTasks.Schedule(() =>
            {
                if (!running || !chr.online || !ReferenceEquals(BotStore.GetBotById(chr.id), this)) return;
                try { action(); }
                catch (Exception e) { DebugPrint("sequence error: " + e.Message); scripted = false; }
            }, ms);
        }

        // =========================================================================
        // MAIN STATE MACHINE
        // =========================================================================
        public override void UpdateState()
        {
            base.UpdateState();
            if (CheckIfNotRunningOrPaused()) return;

            // 基类的 TRADING 分支会解引用 tradeStateMachine（本类从不创建它）。
            // 源端同样在这里把状态强制拉回 RUNNING。
            if (state == State.TRADING) state = State.RUNNING;

            // 正在跑 sleep 等价的续接序列：这一轮 tick 不推进（= 源端线程卡在 sleep 里）。
            if (scripted) return;

            // 已经锁定玩家时，清掉其他玩家的交易请求（源端 rejectOtherTradeRequests）。
            if (GameState != DropGameState.Reset && GameState != DropGameState.IdleAdvertise)
                RejectOtherTradeRequests();

            switch (GameState)
            {
                case DropGameState.Reset:
                    ResetDropGameState();
                    SetState(DropGameState.IdleAdvertise);
                    break;
                case DropGameState.IdleAdvertise: IdleAdvertise(); break;
                case DropGameState.TradeWait: TradeWait(); break;
                case DropGameState.TradeValidate: TradeValidate(); break;
                case DropGameState.PartySetup: PartySetup(); break;
                case DropGameState.PartyWait: PartyWait(); break;
                case DropGameState.PreGame: PreGame(); break;
                case DropGameState.ActiveGame: ActiveGame(); break;
                case DropGameState.EndGame: EndGame(); break;
                default:
                    DebugPrint("unexpected state: " + GameState);
                    state = State.FINISHED;
                    ResetDropGameState();
                    break;
            }
        }

        // =========================================================================
        // TRADE OVERRIDE
        // =========================================================================
        // 源端 tradeInitialized：已和某人进行中就拒绝新来的交易。
        // C# 里交易邀请走 tradeLogic.queue，交易期间 service.Invite 会因 trade 占用而失败，
        // 所以这里只处理「排队里躺着一个不是我当前玩家的人」这一种情况。
        void RejectOtherTradeRequests()
        {
            if (!tradeLogic.queue.HasPending(chr)) return;
            var requester = tradeLogic.queue.Get(chr);
            if (requester == null) return;
            if (Player != null && ReferenceEquals(requester, Player)) return;

            tradeLogic.queue.Remove(chr);
            if (chr.trade == null || !ReferenceEquals(chr.trade.partner?.actor, requester)) return;

            string busyWith = Player?.name ?? "someone";
            DebugPrint("REJECT trade from " + requester.name + " — already engaged with " + busyWith);
            BotTradeCommands.WriteChat(chr, "Busy with " + busyWith + "! Try again after this game.");
            var session = chr.trade;
            // 源端 sleepAmountSeconds(1500) 后才 cancelTrade
            BotTasks.Schedule(() =>
            {
                if (chr.trade != null && ReferenceEquals(chr.trade, session)) BotTradeCommands.Decline(chr);
            }, 1500);
        }

        // 源端 checkForTrades() 的等价物，但**不**写 tradeHandler.confirmedPartner，
        // 否则基类会 new BotTradeStateMachine 抢走交易流程。
        // 注意：C# 在「发出邀请」时就把 session 挂到 chr.trade 上（源端是接受后才有 Trade），
        // 所以这里用 FullTrade 区分「已有邀请」和「真在交易」。
        bool TryAcceptTrade()
        {
            if (!tradeLogic.queue.HasPending(chr)) return false;
            if (chr.trade is BotTradeService.Session pending && pending.FullTrade) return false;
            if (!tradeLogic.CheckQueue(chr)) return false;
            // CheckQueue 的返回只代表「队列里有请求」，Accept 是否真的成立要看 FullTrade。
            if (!(chr.trade is BotTradeService.Session accepted) || !accepted.FullTrade)
            {
                tradeLogic.Clear(chr);
                return false;
            }
            tradeHandler.Reset();
            var incoming = chr.trade?.partner?.actor;
            if (incoming == null) { BotTradeCommands.Decline(chr); return false; }
            paymentSucceeded = false;
            accepted.ResultCallback = result => paymentSucceeded = result == TradeResult.SUCCESSFUL;
            Player = incoming;
            DebugPrint("trade accepted — partner=" + incoming.name);
            SetState(DropGameState.TradeWait);
            return true;
        }

        // 邀请过期（TickInvitationTimeouts 只清 InvitationPending，session 还挂着）时要主动
        // 收回，否则 service.Invite 会一直以「交易对象正忙」拒绝下一位玩家。
        void PurgeStaleInvitation()
        {
            if (chr.trade is BotTradeService.Session session && !session.FullTrade && !session.InvitationPending)
                BotTradeCommands.Decline(chr);
        }

        // --- IDLE / ADVERTISE ---
        void IdleAdvertise()
        {
            PurgeStaleInvitation();
            if (TryAcceptTrade()) return;
            // 源端 random.nextInt(100) < 8
            if (random.Next(100) < 8)
            {
                DebugPrint("advertising");
                Flavor("Advertise");
            }
        }

        // --- TRADE WAIT ---
        void TradeWait()
        {
            if (!IsTradeActive())
            {
                DebugPrint("TRADE_WAIT: trade no longer active, cancelling");
                CancelAndReset("Trade cancelled.");
                return;
            }
            DebugPrint("TRADE_WAIT: writing rules, starting 60s timer");
            BotTradeCommands.WriteChat(chr, "Drop Game! Medium: 10m / Elite: 50m");
            BotTradeCommands.WriteChat(chr, "Put in your mesos and confirm!");
            StartTimer(60_000);
            SetState(DropGameState.TradeValidate);
        }

        // --- TRADE VALIDATE ---
        void TradeValidate()
        {
            if (!IsTradeActive()) { CancelAndReset("Trade cancelled."); return; }

            if (!BotTradeCommands.IsPartnerLocked(chr))
            {
                if (BotScheduler.Now() > stateEndTime)
                {
                    DebugPrint("TRADE_VALIDATE: partner lock timed out");
                    BotTradeCommands.WriteChat(chr, "Too slow! Trade timed out.");
                    scripted = true;
                    Later(2000, () =>
                    {
                        BotTradeCommands.Decline(chr);
                        CleanupTradeAndReset();
                    });
                }
                return;
            }

            int offeredMesos = BotTradeCommands.ReadPartnerMeso(chr);
            DebugPrint("TRADE_VALIDATE: partner locked, offered=" + offeredMesos);

            if (offeredMesos == MediumTierCost) SelectedTier = "medium";
            else if (offeredMesos == EliteTierCost) SelectedTier = "elite";
            else
            {
                DebugPrint("TRADE_VALIDATE: invalid meso amount, rejecting");
                BotTradeCommands.WriteChat(chr, "Wrong amount! 10m or 50m only.");
                scripted = true;
                Later(2000, () =>
                {
                    BotTradeCommands.Decline(chr);
                    Flavor("InvalidMeso");
                    CleanupTradeAndReset();
                });
                return;
            }

            DebugPrint("TRADE_VALIDATE: tier=" + SelectedTier + ", confirming trade");
            BotTradeCommands.WriteChat(chr, SelectedTier.ToUpperInvariant() + " tier locked in!");
            scripted = true;
            Later(1000, () =>
            {
                BotTradeCommands.Confirm(chr);
                Later(2000, TradeConfirmed);
            });
        }

        void TradeConfirmed()
        {
            if (!paymentSucceeded) { CleanupTradeAndReset(); return; }
            LootPool = DropGameLootPool.Load(SelectedTier, random);
            DebugPrint("TRADE_VALIDATE: loot pool loaded, size=" + LootPool.Size);
            if (LootPool.IsEmpty)
            {
                DebugPrint("TRADE_VALIDATE: loot pool empty for tier=" + SelectedTier);
                output.Speak(chr, "Loot pool error. Refunding and resetting.");
                CleanupTradeAndReset();
                return;
            }
            Flavor(SelectedTier == "elite" ? "TierConfirmElite" : "TierConfirmMedium");
            CleanupTrade();
            scripted = false;
            SetState(DropGameState.PartySetup);
        }

        // --- PARTY SETUP ---
        void PartySetup()
        {
            if (!IsPlayerOnMap()) { DebugPrint("PARTY_SETUP: player off-map, cancelling"); CancelAndReset("Player left the map."); return; }
            if (parties == null)
            {
                // 本类没有拿到队伍服务：源端的组队前置条件无法满足，退回招揽。
                DebugPrint("PARTY_SETUP: BotPartyService not wired, back to IDLE_ADVERTISE");
                output.Speak(chr, "Couldn't send party invite. Resetting.");
                ForfeitAndReset();
                return;
            }

            Flavor("PartyInvite");
            bool invited = parties.Invite(chr, Player);
            DebugPrint("PARTY_SETUP: Invite -> " + invited);
            if (!invited)
            {
                output.Speak(chr, "Couldn't send party invite. Resetting.");
                ForfeitAndReset();
                return;
            }
            StartTimer(PartyTimeoutMs);
            SetState(DropGameState.PartyWait);
        }

        // --- PARTY WAIT ---
        void PartyWait()
        {
            if (!IsPlayerOnMap()) { CancelAndReset("Player left the map."); return; }
            if (parties == null) { ForfeitAndReset(); return; }

            // 玩家接受邀请后，双方 party 指向同一个对象。
            var botParty = parties.For(chr.id);
            if (botParty != null && ReferenceEquals(botParty, parties.For(Player.id)))
            {
                DebugPrint("PARTY_WAIT: player joined, advancing to PRE_GAME");
                SetState(DropGameState.PreGame);
                return;
            }
            if (BotScheduler.Now() > stateEndTime)
            {
                DebugPrint("PARTY_WAIT: timed out waiting for player to accept");
                output.Speak(chr, "Party invite timed out — mesos forfeited. Don't waste my time next round!");
                scripted = true;
                Later(2000, () =>
                {
                    scripted = false;
                    ForfeitAndReset();
                });
            }
        }

        // --- PRE-GAME BUFF ---
        void PreGame()
        {
            if (!IsPlayerOnMap()) { CancelAndReset("Player left the map."); return; }

            DebugPrint("PRE_GAME: casting Haste");
            scripted = true;
            Flavor("PreGame");
            Later(1000, () =>
            {
                CastHaste();
                Later(2000, () =>
                {
                    Flavor("GameStart");
                    stateEndTime = BotScheduler.Now() + GameDurationMs;
                    DebugPrint("PRE_GAME: starting movement playback + drop scheduler, gameDuration=" + GameDurationMs + "ms");
                    StartMovementPlayback();
                    // 错开：让假人先离开交易点再开始掉东西（源端同款随机初次延迟）
                    int initialDropDelay = DropInitialDelayMinMs + random.Next(DropInitialDelayMaxMs - DropInitialDelayMinMs);
                    ScheduleDrop(initialDropDelay);
                    scripted = false;
                    SetState(DropGameState.ActiveGame);
                });
            });
        }

        // --- ACTIVE GAME ---
        void ActiveGame()
        {
            if (!IsPlayerOnMap())
            {
                DebugPrint("ACTIVE_GAME: player off-map, ending early");
                StopDropScheduler();
                Flavor("PlayerDisconnect");
                EndGameCleanup();
                return;
            }
            if (BotScheduler.Now() >= stateEndTime)
            {
                DebugPrint("ACTIVE_GAME: time up, advancing to END_GAME");
                SetState(DropGameState.EndGame);
            }
            // 掉落由 BotTasks 定时器推进，tick 只做结束条件判断。
        }

        // --- END GAME ---
        void EndGame()
        {
            DebugPrint("END_GAME: stopping scheduler, cleaning up");
            StopDropScheduler();
            Flavor("GameEnd");
            output.Emote(chr, 2);
            scripted = true;
            Later(3000, () =>
            {
                scripted = false;
                EndGameCleanup();
            });
        }

        // =========================================================================
        // MOVE PLAYBACK / DROP SCHEDULER（源端线程 → BotTasks 时间戳续接）
        // =========================================================================
        void StartMovementPlayback()
        {
            var frames = LoadMovementRecording(chr.mapId);
            if (frames != null && frames.Count > 0)
            {
                BotMovementPlayback.Play(chr, frames);
                return;
            }
            // 源端录制失败只记日志，不启动额外游荡。
            DebugPrint("movement recording missing for map " + chr.mapId);
        }

        static List<BotMovementRecording.Packet> LoadMovementRecording(int mapId)
        {
            try
            {
                var bytes = BotResources.Load().ReadBytes(
                    BotMovementRecording.ResourcePath(mapId, MovementRecordingName, "bin"));
                if (bytes == null) return null;
                return BotMovementRecording.ReadBinary(bytes);
            }
            catch (Exception)
            {
                return null;
            }
        }

        void ScheduleDrop(long delayMs)
        {
            dropTask?.Cancel();
            dropTask = BotTasks.Schedule(() =>
            {
                if (!running || !chr.online || !ReferenceEquals(BotStore.GetBotById(chr.id), this)) return;
                PerformDrop();
            }, delayMs);
        }

        void ScheduleNextDrop()
        {
            // 源端 nextInt(max-min) 上界是开区间
            int delay = DropIntervalMinMs + random.Next(DropIntervalMaxMs - DropIntervalMinMs);
            ScheduleDrop(delay);
        }

        void PerformDrop()
        {
            if (BotScheduler.Now() >= stateEndTime) return;   // 时间到，不再掉

            int itemId;
            bool isEquip = false;
            DropGameLootPool.LootEntry? entry =
                LootPool != null && !LootPool.IsEmpty ? LootPool.RollItem() : (DropGameLootPool.LootEntry?)null;
            if (entry.HasValue)
            {
                itemId = entry.Value.itemId;
                isEquip = entry.Value.isEquip;
            }
            else itemId = FallbackDropItemIds[random.Next(FallbackDropItemIds.Length)];

            DebugPrint("DROP itemId=" + itemId + " equip=" + isEquip);
            long despawnMs = SelectedTier == "elite" ? EliteDespawnDelayMs : MediumDespawnDelayMs;
            try
            {
                DropCommands.BotDropItemWithExpiry(chr, itemId, isEquip, despawnMs);
                SpectatorReact(itemId);
            }
            catch (Exception e)
            {
                // 源端单次掉落失败也要继续排下一次
                DebugPrint("Drop error: " + e.Message);
            }
            ScheduleNextDrop();
        }

        void StopDropScheduler()
        {
            dropTask?.Cancel();
            dropTask = null;
            StopMovementPlayback();
        }

        void StopMovementPlayback()
        {
            BotMovementPlayback.Cancel(chr);
        }

        // =========================================================================
        // SPECTATOR（源端 DropGameSpectatorSystem：找围观假人发对白）
        // =========================================================================
        const int SpectatorCooldownMinDrops = 7;
        const int SpectatorCooldownMaxDrops = 12;
        static readonly Random spectatorRandom = new Random();
        static readonly string[] SpectatorCategories = { "Excitement", "Envy", "Hype", "Question", "Cheer" };
        static readonly int[] SpectatorCategoryWeights = { 30, 20, 20, 15, 15 };
        const int SpectatorWeightTotal = 100;
        // 源端是 static 计数器：所有 DropGameBot 共用一个冷却。
        static int spectatorDropCounter;
        static int spectatorNextReactionAt;
        static int spectatorLastReactorId = -1;

        static DropGameBot()
        {
            spectatorNextReactionAt = SpectatorCooldownMinDrops
                + spectatorRandom.Next(SpectatorCooldownMaxDrops - SpectatorCooldownMinDrops + 1);
        }

        void SpectatorReact(int itemId)
        {
            spectatorDropCounter++;
            if (spectatorDropCounter < spectatorNextReactionAt) return;
            spectatorDropCounter = 0;
            spectatorNextReactionAt = SpectatorCooldownMinDrops
                + spectatorRandom.Next(SpectatorCooldownMaxDrops - SpectatorCooldownMinDrops + 1);

            string itemName = ItemName(itemId);
            if (string.IsNullOrEmpty(itemName) || itemName == "NULL") return;

            var spectators = new List<BotCharacter>();
            foreach (var other in BotRegistry.OnMap(chr.mapId))
            {
                if (other == null || other.id == chr.id) continue;
                var sm = BotStore.GetBotById(other.id);
                if (sm == null || !sm.IsAvailableForAmbientActions()) continue;
                spectators.Add(other);
            }
            if (spectators.Count == 0) return;

            for (int i = spectators.Count - 1; i > 0; i--)
            {
                int j = spectatorRandom.Next(i + 1);
                var swap = spectators[i]; spectators[i] = spectators[j]; spectators[j] = swap;
            }
            var reactor = PickDifferentSpectator(spectators);
            if (reactor == null) return;

            spectatorLastReactorId = reactor.id;
            int delay = 1500 + spectatorRandom.Next(3500);
            int mapId = chr.mapId;
            var watcher = Player;
            BotTasks.Schedule(() =>
            {
                if (!BotRegistry.Contains(reactor.id) || reactor.mapId != mapId) return;
                // 源端 botFaceTowardsPoint(bot, player.getPosition())
                if (watcher != null) reactor.facing = watcher.x >= reactor.x ? 1 : -1;
                ReactWithDialogue(reactor, itemName);
            }, delay);
        }

        static BotCharacter PickDifferentSpectator(List<BotCharacter> spectators)
        {
            if (spectators.Count == 0) return null;
            if (spectators.Count == 1) return spectators[0];
            foreach (var c in spectators)
                if (c.id != spectatorLastReactorId) return c;
            return spectators[0];
        }

        void ReactWithDialogue(BotCharacter reactor, string itemName)
        {
            try
            {
                string category = SelectWeightedCategory();
                var line = dialogue?.Get(SpectatorDialoguePack, SpectatorBotType, category);
                if (line?.text == null || line.text.Count == 0) return;
                string text = line.text[spectatorRandom.Next(line.text.Count)].Replace("{item}", itemName);
                output.Speak(reactor, text);
                if (line.emotes != null && line.emotes.Count > 0)
                {
                    int emoteId = line.emotes[0];
                    if (emoteId > 0 && spectatorRandom.Next(100) < 50) output.Emote(reactor, emoteId);
                }
            }
            catch (Exception) { /* 对白资源缺失时静默（源端同样 return） */ }
        }

        static string SelectWeightedCategory()
        {
            int roll = spectatorRandom.Next(SpectatorWeightTotal);
            int cumulative = 0;
            for (int i = 0; i < SpectatorCategories.Length; i++)
            {
                cumulative += SpectatorCategoryWeights[i];
                if (roll < cumulative) return SpectatorCategories[i];
            }
            return SpectatorCategories[SpectatorCategories.Length - 1];
        }

        // =========================================================================
        // HELPERS
        // =========================================================================
        void CastHaste()
        {
            // 源端：SkillFactory.getSkill(4101004).getEffect(max).applyTo(chr / player)
            // 真人效果由宿主读取原 Skill IMG 后交给 NativeBuffs。
            try
            {
                BotTypeManager.ApplySkillEffect?.Invoke(chr, HasteSkillId);
                if (Player != null) BotTypeManager.ApplySkillEffect?.Invoke(Player, HasteSkillId);
                output.Send(BotOutput.Kind.Skill, chr, value: HasteSkillId);
                if (Player != null) output.Send(BotOutput.Kind.Skill, Player, value: HasteSkillId, targetId: Player.id);
            }
            catch (Exception e) { DebugPrint("Failed to cast Haste: " + e.Message); }
        }

        void Flavor(string node)
        {
            try
            {
                var entry = dialogue?.Get(DialoguePack, botType, node);
                if (entry != null) dialogue.Run(chr, entry, output, true);
            }
            catch (Exception) { /* 对白缺失不影响流程 */ }
        }

        // 源端 isTradeActive() = getChr().getTrade() != null；C# 的 session 在邀请阶段就存在，
        // 只有双方都进入交易（FullTrade）才等价于源端「有 Trade 对象」。
        bool IsTradeActive()
            => chr.trade is BotTradeService.Session session ? session.FullTrade : chr.trade != null;

        bool IsPlayerOnMap()
        {
            if (Player == null) return false;
            if (!Player.online) return false;
            return Player.mapId == chr.mapId && Player.channel == chr.channel && Player.world == chr.world;
        }

        void StartTimer(long durationMs) => stateEndTime = BotScheduler.Now() + durationMs;

        void CancelAndReset(string reason)
        {
            output.Speak(chr, reason);
            CleanupTradeAndReset();
        }

        void CleanupTrade()
        {
            tradeLogic.Clear(chr);
            tradeHandler.Reset();
        }

        void CleanupTradeAndReset()
        {
            CleanupTrade();
            ResetDropGameState();
            SetState(DropGameState.IdleAdvertise);
        }

        void ResetDropGameState()
        {
            SetState(DropGameState.Reset);
            SelectedTier = null;
            Player = null;
            LootPool = null;
            paymentSucceeded = false;
            stateEndTime = 0;
            scripted = false;
            sequenceTask?.Cancel();
            sequenceTask = null;
            StopDropScheduler();
        }

        void EndGameCleanup()
        {
            // 假人是队长 → 退队会解散并顺带把玩家移出（源端 botLeaveParty）
            try { parties?.Leave(chr.id); }
            catch (Exception e) { DebugPrint("Party cleanup error: " + e.Message); }
            ResetDropGameState();
            SetState(DropGameState.IdleAdvertise);
        }

        // 不退款直接回到 IDLE（源端 forfeitAndReset）
        void ForfeitAndReset()
        {
            if (parties != null)
            {
                try { parties.Leave(chr.id); }
                catch (Exception e) { DebugPrint("forfeit party cleanup error: " + e.Message); }
            }
            ResetDropGameState();
            SetState(DropGameState.IdleAdvertise);
        }

        public override void DisplayCommands(BotActor player) => output.ShowOptions(player, new[] { chr.name });

        public override void StopScheduledTask()
        {
            dropTask?.Cancel(); dropTask = null;
            sequenceTask?.Cancel(); sequenceTask = null;
            StopMovementPlayback();
            parties?.Leave(chr.id);
            chr.trade?.Decline();
            base.StopScheduledTask();
        }
    }
}
