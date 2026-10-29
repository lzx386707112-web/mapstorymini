// BotTypes/GachaBot.java（255 行）对齐移植：扭蛋机旁的常驻假人。
// 循环：定站位 → 喷一轮扭蛋掉落 → 两个空转 tick → 把自己喷出来的东西捡回去 →
//       消费事件队列（升级/上卷/扭蛋中奖的反应）→ 处理奖励（源端是空实现）→ 回到喷掉落。
//
// CustomReactor 的出生、四次状态更新、喷出掉落均走地图权威服务。
// 阻塞 sleep 改为带活动代数守卫的延时链；原奖池及事件订阅保持不变。
using System;
using System.Collections.Generic;
using System.Globalization;
using Server.Bot.Environment;
using Server.Field;
using Server.Bot.Events;
using Server.Bot.Social;

namespace Server.Bot.Types
{
    public sealed class GachaBot : BotStateMachine
    {
        // ≡ GachaBot.GachaBotState（9 态）
        public enum GachaBotState
        {
            Reset, SetPosition, RunRoulette, StandBy1, StandBy2, PickupItem, StandBy3, StandBy4, ProcessReward
        }

        // ≡ CustomReactor 的喷溅参数
        const int DropSprayLength = 2;
        const int DropSprayFullWidth = DropSprayLength * 2;
        const int ItemDropOffset = 30;

        // ≡ GachaFillerSystem.DEFAULT_LIST_SIZE / DEFAULT_ITEM_REPLACE_CHANCE
        internal const int GachaListSize = 10;
        internal const double ItemReplaceChance = 0.30;

        // ≡ todo get prize based on map：源端写死的 scg（1082223）
        const int PrizeItemId = 1082223;

        const string DialoguePack = "GachaBotDialogue.yaml";
        const string LevelUpRecordingFirst = "rightleft45";
        const string LevelUpRecordingSecond = "leftright70";
        const long LevelUpPauseMs = 1500;

        // ≡ getRandomMesoGachaFiller：13 个候选值（故意重复做权重）
        static readonly int[] MesoFillerAmounts = { 10, 10, 10, 10, 10, 69, 69, 69, 69, 420, 420, 420, 1337 };

        readonly Random random;
        readonly BotDialogue dialogue;
        readonly GachaFillerPool pool;

        public GachaBotState gachaBotState { get; private set; } = GachaBotState.Reset;

        int baseX, baseY;
        bool scripted;
        int generation;
        BotTasks.Handle sequenceTask;

        public GachaBot(BotCharacter c, EventBus events = null, BotDialogue dialogue = null, Random random = null) : base(c)
        {
            this.dialogue = dialogue ?? BotTypeManager.Dialogue;
            this.random = random ?? new Random();
            pool = GachaFillerPool.Shared;
            botType = "GachaBot";
            Subscribe(events ?? BotTypeManager.Events, EventType.LEVEL_UP, EventType.SCROLLING);
        }

        // =========================================================================
        // MAIN STATE MACHINE
        // =========================================================================
        public override void UpdateState()
        {
            base.UpdateState();
            if (CheckIfNotRunningOrPaused()) return;
            DebugLog(chr.name + " GachaBotState: " + gachaBotState);
            if (scripted) return;

            switch (gachaBotState)
            {
                case GachaBotState.Reset:
                    ResetGachaBotState();
                    SetState(GachaBotState.SetPosition);
                    break;
                case GachaBotState.SetPosition:
                    SetPosition();
                    SetState(GachaBotState.RunRoulette);
                    break;
                case GachaBotState.RunRoulette:
                    RunRoulette();
                    SetState(GachaBotState.StandBy1);
                    break;
                case GachaBotState.StandBy1:
                    SetState(GachaBotState.StandBy2);
                    break;
                case GachaBotState.StandBy2:
                    SetState(GachaBotState.PickupItem);
                    break;
                case GachaBotState.PickupItem:
                    PickupItem();
                    SetState(GachaBotState.StandBy3);
                    break;
                case GachaBotState.StandBy3:
                    ExecuteEventQueueGacha();
                    break;
                case GachaBotState.StandBy4:
                    SetState(GachaBotState.ProcessReward);
                    break;
                case GachaBotState.ProcessReward:
                    ProcessReward();
                    SetState(GachaBotState.RunRoulette);
                    break;
                default:
                    DebugLog("Unexpected GachaBot state: " + gachaBotState);
                    state = State.FINISHED;
                    ResetGachaBotState();
                    break;
            }
        }

        void SetState(GachaBotState next) => gachaBotState = next;

        bool Alive() => running && chr.online && ReferenceEquals(BotStore.GetBotById(chr.id), this);

        void ResetGachaBotState()
        {
            SetState(GachaBotState.Reset);
            scripted = false;
            sequenceTask?.Cancel();
            sequenceTask = null;
        }

        // ≡ 源端 sleepAmountSeconds 的可取消等价物（≡ TutorialBot.After）
        void After(long ms, Action next)
        {
            scripted = true;
            sequenceTask?.Cancel();
            sequenceTask = BotTasks.Schedule(() =>
            {
                if (!Alive()) return;
                try { next(); }
                catch (Exception e) { DebugLog(chr.name + " sequence error: " + e.Message); scripted = false; }
            }, ms);
        }

        // ——— SET_POSITION ———
        void SetPosition()
        {
            // ≡ this.basePosition = getChr().getPosition()
            baseX = (int)chr.x;
            baseY = (int)chr.y;
            output.Send(BotOutput.Kind.Bubble, chr, "Position set!");
        }

        // ——— RUN_ROULETTE ———
        void RunRoulette()
        {
            if (!CheckMainPlayersOnMap()) return;
            // ≡ createReactorDropList(createGachaListWithPrize(prize_id)) + gachaPop(getChr(), popDrops)
            GachaPop(pool.CreateGachaListWithPrize(PrizeItemId, random));
        }

        // CustomReactor.gachaPop / delayedReactorDrops.
        void GachaPop(List<int> itemIds)
        {
            if (itemIds == null || itemIds.Count == 0) return;
            int mapId = chr.mapId;
            MapReactorService.Spawn(mapId, 2202004, (int)chr.x, (int)chr.y);
            MapReactor reactor = null;
            foreach (var candidate in MapReactorService.Get(mapId))
                if (candidate.Hittable && (candidate.x - chr.x) * (candidate.x - chr.x) +
                    (candidate.y - chr.y) * (candidate.y - chr.y) <= 25 * 25)
                { reactor = candidate; break; }
            if (reactor == null) return;
            for (int hit = 0; hit < 3; hit++) MapReactorService.ForceHit(mapId, reactor.objectId);
            int centerX = reactor.x - (itemIds.Count % 2 == 0 ? 12 : 0), centerY = reactor.y;
            for (int i = 0; i < itemIds.Count; i++)
            {
                int itemId = itemIds[i];
                // 源端 center2 是累加的（Point 被就地修改），这里同样累加
                var pos = BotHelpers.AdjustCenterPositionXAxis(centerX, centerY, i, DropSprayLength, DropSprayFullWidth, ItemDropOffset);
                centerX = pos.x;
                int throwX = pos.x, throwY = pos.y;
                // The shipped MapleMap overloads discard their delay argument for
                // both items and mesos. Preserve that behavior instead of adding timers.
                if (itemId == 0)
                {
                    MapItemService.CalcDropPos(mapId,throwX,throwY,reactor.x,reactor.y,out float mesoX,out float mesoY);
                    MapItemService.SpawnMesoDrop(mapId, MesoFillerAmounts[random.Next(MesoFillerAmounts.Length)], mesoX, mesoY,
                        reactor.objectId, chr.id, MapItemService.PartyOf?.Invoke(chr.id) ?? -1, false, 2);
                    continue;
                }
                MapItemService.DropFromReactor(mapId,reactor.objectId,chr.id,
                    BotLogic.GenerateCleanItem(itemId),throwX,throwY,reactor.x,reactor.y);
            }
            MapReactorService.ForceHit(mapId, reactor.objectId);
        }

        // ——— PICKUP_ITEM ———
        void PickupItem()
        {
            if (!CheckMainPlayersOnMap()) return;
            output.Emote(chr, 3);
            // ≡ botLootOwnerItems(getChr(), getChr().getPosition(), 12000)
            DropCommands.BotLootOwnerItems(chr, (int)chr.x, (int)chr.y, 12000);
        }

        // ——— STAND_BY_3 ———
        // ≡ executeEventQueueGacha：先把排队的事件消费掉，队列空了才往下走
        void ExecuteEventQueueGacha()
        {
            ProcessQueuedEvents();
            if (HasQueuedEvents) return;
            SetState(GachaBotState.StandBy4);
        }

        // ——— PROCESS_REWARD ———
        void ProcessReward()
        {
            // 源端这里直接 return（reactToReward 整段被注释掉），保持空实现。
        }

        // =========================================================================
        // EVENTS
        // =========================================================================
        public override void HandleEvent(GameEvent ev)
        {
            if (ev == null) return;
            switch (ev.type)
            {
                case EventType.GACHAPON_REWARD: HandleGachaponEvent(ev); break;
                case EventType.SCROLLING: HandleScrollingEvent(ev); break;
                case EventType.LEVEL_UP: HandleLevelUpEvent(ev); break;
                default: throw new InvalidOperationException("Unknown Event Type: " + ev.type);
            }
        }

        void HandleGachaponEvent(GameEvent ev)
        {
            output.Emote(chr, 2);
            output.Send(BotOutput.Kind.Bubble, chr, "Lucky " + ev.playerName + "!");
        }

        void HandleScrollingEvent(GameEvent ev)
        {
            if (ev.pass == true)
            {
                output.Emote(chr, 3);
                output.Send(BotOutput.Kind.Bubble, chr, "Nice scroll " + ev.playerName + "!");
            }
            else
            {
                output.Emote(chr, 4);
                output.Send(BotOutput.Kind.Bubble, chr, "Ooof... unlucky D:");
            }
        }

        void HandleLevelUpEvent(GameEvent ev)
        {
            int token = generation;
            var chain = BotTiming.CreateChain().StopUnless(() => Alive() && token == generation);
            chain.Run(() => PlayRecording(LevelUpRecordingFirst))
                .Pause(LevelUpPauseMs)
                .Run(() =>
                {
                    output.Emote(chr, 2);
                    output.Send(BotOutput.Kind.Bubble, chr, "Ayy Congrats " + ev.playerName + "!");
                })
                .Pause(LevelUpPauseMs)
                .Run(() => PlayRecording(LevelUpRecordingSecond))
                .Run(() => scripted = false)
                .Start();
            scripted = true;
        }

        // ≡ BotMoveStreamOffset(getMovementRecording(0, recName), getChr())
        void PlayRecording(string name)
        {
            var frames = LoadMovementRecording(name);
            if (frames == null || frames.Count == 0)
            {
                DebugLog("[GachaBot] movement recording " + name + " missing for map " + chr.mapId + ", skipping");
                return;
            }
            BotMovementPlayback.Play(chr, frames);
        }

        List<BotMovementRecording.Packet> LoadMovementRecording(string name)
        {
            try
            {
                var bytes = BotResources.Load().ReadBytes(BotMovementRecording.ResourcePath(chr.mapId, name, "bin"));
                return bytes == null ? null : BotMovementRecording.ReadBinary(bytes);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public override void DisplayCommands(BotActor player)
        {
            if (player == null) return;
            output.ShowOptions(player, new[] { chr.name });
        }

        public override void StopScheduledTask()
        {
            generation++;
            BotMovementPlayback.Cancel(chr);
            sequenceTask?.Cancel();
            sequenceTask = null;
            scripted = false;
            base.StopScheduledTask();
        }
    }

    // ≡ soloMapling.itemPool.GachaFillerSystem：gachaFiller.yaml 的奖池读法。
    // YAML 在打包时已转成纯文本（Assets/Resources/Bot/SoloMapling.json，
    // 键 = soloMapling/itemPool/itemConfig/gachaFiller.yaml），照 MarketItemPool 的套路
    // 取回文本后按行解析成 type → tier → itemIds 的字典，不引第三方 YAML 库。
    internal sealed class GachaFillerPool
    {
        public const string ResourcePath = "soloMapling/itemPool/itemConfig/gachaFiller.yaml";

        // ≡ GachaFillerSystem.TYPE_PROBABILITIES / TIER_PROBABILITIES
        static readonly KeyValuePair<string, double>[] TypeWeights =
        {
            new KeyValuePair<string, double>("useables", 0.50),
            new KeyValuePair<string, double>("ores", 0.20),
            new KeyValuePair<string, double>("jewels", 0.20),
            new KeyValuePair<string, double>("crystals", 0.10),
        };
        static readonly KeyValuePair<string, double>[] TierWeights =
        {
            new KeyValuePair<string, double>("s_tier", 0.20),
            new KeyValuePair<string, double>("a_tier", 0.30),
            new KeyValuePair<string, double>("b_tier", 0.50),
        };

        readonly Dictionary<string, Dictionary<string, List<int>>> data = new Dictionary<string, Dictionary<string, List<int>>>();

        public int Count { get; private set; }

        // 奖池是只读配置：所有 GachaBot 共用一份（≡ 源端 static 的 gachaData）
        static GachaFillerPool shared;
        public static GachaFillerPool Shared => shared ??= Load();

        static GachaFillerPool Load()
        {
            var pool = new GachaFillerPool();
            try { pool.Parse(BotResources.Load().ReadText(ResourcePath)); }
            catch (Exception) { /* 资源缺失时保持空池：填充位会退化成 0（= 掉零钱） */ }
            return pool;
        }

        void Parse(string yaml)
        {
            if (string.IsNullOrEmpty(yaml)) return;
            string currentType = null;
            foreach (var rawLine in yaml.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string key = line.Substring(0, colon).Trim();
                string value = line.Substring(colon + 1).Trim();
                if (value.Length == 0)
                {
                    // 形如 "useables:"：开启一个新的 type 段
                    currentType = key;
                    data[key] = new Dictionary<string, List<int>>();
                    continue;
                }
                if (currentType == null) continue;
                var ids = new List<int>();
                foreach (var token in value.Trim('[', ']').Split(','))
                {
                    string text = token.Trim();
                    if (int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int id)) ids.Add(id);
                }
                data[currentType][key] = ids;
                Count += ids.Count;
            }
        }

        /// <summary>≡ createGachaListWithPrize：先造填充列表，再把奖品插到 1/4 之后的随机位置。</summary>
        public List<int> CreateGachaListWithPrize(int prizeId, Random random)
        {
            var list = CreateGachaFillerList(random);
            int halfwayPoint = list.Count / 4;
            int insertPosition = halfwayPoint + random.Next(list.Count - halfwayPoint + 1);
            list.Insert(insertPosition, prizeId);
            return list;
        }

        /// <summary>≡ createGachaFillerList(size, itemReplaceChance)：默认全是 0（= 掉零钱），按概率替换成道具。</summary>
        public List<int> CreateGachaFillerList(Random random)
        {
            var filler = new List<int>(GachaBot.GachaListSize);
            for (int i = 0; i < GachaBot.GachaListSize; i++) filler.Add(0);
            for (int i = 0; i < filler.Count; i++)
            {
                if (random.NextDouble() >= GachaBot.ItemReplaceChance) continue;
                filler[i] = RandomItem(RollWeighted(TypeWeights, random), RollWeighted(TierWeights, random), random);
            }
            return filler;
        }

        /// <summary>≡ GachaFillerSystem.getRandomItem：取不到就返回 0（源端同款兜底）。</summary>
        public int RandomItem(string type, string tier, Random random)
        {
            if (!data.TryGetValue(type, out var tiers)) return 0;
            if (!tiers.TryGetValue(tier, out var items) || items.Count == 0) return 0;
            return items[random.Next(items.Count)];
        }

        static string RollWeighted(KeyValuePair<string, double>[] weights, Random random)
        {
            double roll = random.NextDouble(), cumulative = 0;
            foreach (var pair in weights)
            {
                cumulative += pair.Value;
                if (roll < cumulative) return pair.Key;
            }
            return weights[weights.Length - 1].Key;
        }
    }
}
