using System;
using Server.Bot.Environment;
using Server.Bot.Social;
using Server.Field;

namespace Server.Bot.Types
{
    // ≡ BotTypes/HenesysBot.java（364 行）。射手村常驻假人：站桩 + 游荡 + 表情 + 偶尔串图。
    //
    // 与源端的两处已知差异（都是我们的能力边界，不改变可见行为）：
    //  1. 源端 wanderPlatforms 在「未被观察」分支会用 getMainPlatformIds 按平台名选点；
    //     我们的 AOI 是整图级，没人看的图整张都停，永远走不到那个分支，所以只保留
    //     「被观察 → BotWander 漫游 / 否则停」这条主路径（和 TownWandererBot 同构）。
    //  2. 源端 PetPark 有 1/4 概率转成 HENESYS_JQ_BOT；JQ 类型尚未移植，这里保留判定
    //     与冷却，但转换目标先落回自身（避免生成一个没有行为类的空壳）。
    public sealed class HenesysBot : BotStateMachine
    {
        public enum HenesysState { Reset, Idle, Wander, Emote, ChangeMap }

        const int HenesysMain = 100000000, HenesysMarket = 100000100;
        const int HenesysPark = 100000200, PetPark = 100000202;
        // ≡ initPortalConnections：双向注册，四项覆盖 Main/Market/Park/PetPark 全连通
        static readonly int[,] Links =
        {
            { HenesysMain, 23, HenesysMarket, 14 },   // Main <-> Market
            { HenesysMain, 24, HenesysPark, 18 },     // Main <-> Park
            { HenesysPark, 19, HenesysMarket, 15 },   // Park <-> Market
            { HenesysPark, 13, PetPark, 5 },          // Park <-> PetPark
        };
        const long MapChangeCooldownMs = 3 * 60 * 1000;
        const long JqConversionCooldownMs = 10 * 60 * 1000;

        readonly Random random;
        readonly BotDialogue dialogue;
        readonly BotFlavor flavor;
        readonly bool mapRoaming;
        long lastMapChangeTime, lastJqConversionTime;
        public void SetLastJqConversionTime(long value) => lastJqConversionTime = value;
        int lifetime;

        public HenesysBot(BotCharacter actor, BotDialogue dialogue, BotFlavor flavor,
            bool mapRoaming, Random random = null) : base(actor)
        {
            this.dialogue = dialogue; this.flavor = flavor; this.mapRoaming = mapRoaming;
            this.random = random ?? new Random();
            botType = "HenesysBot";
        }

        public HenesysState henesysState { get; private set; } = HenesysState.Reset;

        // ≡ isAvailableForAmbientActions：交易中不接氛围动作（其余恒 true）
        public override bool IsAvailableForAmbientActions() => state != State.TRADING;

        public override void UpdateState()
        {
            base.UpdateState();
            if (CheckIfNotRunningOrPaused() || chr == null || MapService.Get(chr.mapId) == null) return;
            switch (henesysState)
            {
                case HenesysState.Reset:
                    henesysState = HenesysState.Idle; break;
                case HenesysState.Idle:
                    DecideNextAction(); break;
                case HenesysState.Wander:
                    WanderPlatforms(); RandomEmote(); RandomChat("WanderChat");
                    henesysState = HenesysState.Idle; break;
                case HenesysState.Emote:
                    RandomEmote(); RandomChat("EmoteReaction");
                    henesysState = HenesysState.Idle; break;
                case HenesysState.ChangeMap:
                    RandomChat("MapTransition");
                    ChangeMap();
                    lastMapChangeTime = BotScheduler.Now();
                    RandomEmote();
                    EnsureRoaming();
                    henesysState = HenesysState.Idle; break;
            }
        }

        // ≡ decideNextAction：JQ 转化 → 串图(1/10) → 游荡(1/3) → 表情(1/5) → 发呆
        void DecideNextAction()
        {
            long now = BotScheduler.Now();
            if (chr.mapId == PetPark && now - lastJqConversionTime > JqConversionCooldownMs && Chance(4))
            {
                random.Next(); // 源端 convertToJQBot 先说话再换类型；JQ 未移植，只消耗一次掷骰
                lastJqConversionTime = now;
                return;
            }
            flavor.MaybeExpress(this);
            if (mapRoaming && now - lastMapChangeTime > MapChangeCooldownMs && Chance(10))
            { henesysState = HenesysState.ChangeMap; return; }
            if (Chance(3)) { henesysState = HenesysState.Wander; return; }
            if (Chance(5)) { henesysState = HenesysState.Emote; return; }
            EnsureRoaming();
        }

        // 源端 rollChanceInverse(n) = 1/n 命中
        bool Chance(int n) => random.Next(n) == 0;

        // 被观察才动：观察时交给通用漫游器，没人看就停下（省 CPU，也避免"看不见的地方在动"）
        void EnsureRoaming()
        {
            if (!BotMovementService.Observed(chr.mapId)) { BotWander.Stop(chr); return; }
            if (!BotWander.IsWandering(chr)) BotWander.Start(chr, false, 0, 0);
        }
        void WanderPlatforms() => EnsureRoaming();

        void RandomEmote()
        {
            if (Chance(10)) output.Emote(chr, Chance(50) ? 2 : 3);
        }
        void RandomChat(string node)
        {
            if (!Chance(20)) return;
            string line = Line(node);
            if (line != null) output.Speak(chr, line);
        }
        string Line(string node)
        {
            try { return dialogue.RandomLine("HenesysBotDialogue.yaml", "HenesysBot", node); }
            catch (Exception) { return null; }
        }

        // ≡ changeMap：按 1/(1+同图人数) 加权挑相邻图，再走到对应传送门（移动系统接管换图）
        void ChangeMap()
        {
            int from = chr.mapId;
            var candidates = new int[4];
            int n = 0;
            for (int i = 0; i < Links.GetLength(0); i++)
            {
                int a = Links[i, 0], b = Links[i, 2];
                if (a == from) candidates[n++] = b;
                else if (b == from) candidates[n++] = a;
            }
            if (n == 0) return;

            double total = 0;
            var weights = new double[n];
            for (int i = 0; i < n; i++)
            {
                weights[i] = 1.0 / (1 + BotRegistry.OnMap(candidates[i]).Count);
                total += weights[i];
            }
            double roll = random.NextDouble() * total, cumulative = 0;
            int chosen = candidates[n - 1];
            for (int i = 0; i < n; i++)
            {
                cumulative += weights[i];
                if (roll < cumulative) { chosen = candidates[i]; break; }
            }

            int portalId = PortalForRoute(from, chosen);
            if (portalId < 0) return;
            var map = MapService.Get(from);
            if (map == null) return;
            MapPortal portal = null;
            foreach (var p in map.portals) if (p.id == portalId) { portal = p; break; }
            if (portal == null) return;

            BotWander.Stop(chr);
            BotMovementService.Move(chr, portal.x, portal.y);
        }

        // ≡ portalConnection.getRoute：只认直接相邻（源端 market<->park 需经 main，没有直达）
        static int PortalForRoute(int from, int to)
        {
            for (int i = 0; i < Links.GetLength(0); i++)
            {
                if (Links[i, 0] == from && Links[i, 2] == to) return Links[i, 1];
                if (Links[i, 2] == from && Links[i, 0] == to) return Links[i, 3];
            }
            return -1;
        }

        public override void StopScheduledTask()
        {
            lifetime++;
            flavor.Forget(chr); BotWander.Stop(chr); BotMovementService.Disable(chr);
            base.StopScheduledTask();
        }
    }
}
