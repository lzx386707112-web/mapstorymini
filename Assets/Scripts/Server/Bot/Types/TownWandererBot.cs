using System;
using Server.Bot.Environment;
using Server.Bot.Events;
using Server.Bot.Social;

namespace Server.Bot.Types
{
    public sealed class TownWandererBot : BotStateMachine
    {
        public enum WanderState { Reset, Idle, Wander, Emote, ChangeMap }
        public WanderState wanderState { get; private set; }
        readonly Random random;
        readonly BotDialogue dialogue;
        readonly BotFlavor flavor;
        int homeMapId = -1;
        int lifetime;
        public TownWandererBot(BotCharacter actor, BotDialogue dialogue, BotFlavor flavor, EventBus events, Random random = null) : base(actor)
        {
            this.dialogue = dialogue; this.flavor = flavor; this.random = random ?? new Random();
            botType = "TownWandererBot"; Subscribe(events, EventType.LEVEL_UP);
        }
        // 原BotChatter.isEngaged恒false，discoverTownFamily只返回home，不添加不存在的聊天/跨图AI。
        public override bool IsAvailableForAmbientActions() => true;
        public override void UpdateState()
        {
            base.UpdateState();
            if (CheckIfNotRunningOrPaused() || chr == null || Server.Field.MapService.Get(chr.mapId) == null) return;
            ProcessQueuedEvents();
            switch (wanderState)
            {
                case WanderState.Reset:
                    if (homeMapId < 0) homeMapId = chr.mapId;
                    EnsureRoaming(); wanderState = WanderState.Idle; break;
                case WanderState.Idle:
                    flavor.MaybeExpress(this); EnsureRoaming();
                    if (random.Next(3) == 0) wanderState = WanderState.Wander;
                    else if (random.Next(5) == 0) wanderState = WanderState.Emote;
                    break;
                case WanderState.Wander:
                    EnsureRoaming(); RandomEmote(); RandomChat("WanderChat"); wanderState = WanderState.Idle; break;
                case WanderState.Emote:
                    RandomEmote(); RandomChat("EmoteReaction"); wanderState = WanderState.Idle; break;
                case WanderState.ChangeMap:
                    // discoverTownFamily(home)原实现只有home，pickNextMap永远无候选。
                    wanderState = WanderState.Idle; break;
            }
        }
        void EnsureRoaming()
        {
            if (!BotMovementService.Observed(chr.mapId)) { BotWander.Stop(chr); return; }
            if (!BotWander.IsWandering(chr)) BotWander.Start(chr, false, 0, 0);
        }
        void RandomEmote() { if (random.Next(10) == 0) output.Emote(chr, random.Next(2) == 0 ? 2 : 3); }
        string Line(string node)
        {
            // 原TownWandererBot同样捕获文件/节点解析异常；原文件字节保持不变。
            try { return dialogue.RandomLine("TownWandererDialogue.yaml", "TownWandererBot", node); }
            catch (Exception) { return null; }
        }
        void RandomChat(string node)
        { if (random.Next(20) == 0) { string line = Line(node); if (line != null) output.Speak(chr, line); } }
        public override void HandleEvent(GameEvent ev)
        {
            var actor = ev?.actor;
            if (ev == null || ev.type != EventType.LEVEL_UP || actor == null || actor.id == chr.id || actor.mapId != chr.mapId || !IsAvailableForAmbientActions()) return;
            double dx = actor.x - chr.x, dy = actor.y - chr.y;
            if (dx * dx + dy * dy > 350 * 350 || random.NextDouble() >= .3) return;
            int generation = lifetime;
            bool Valid() => generation == lifetime && BotStore.GetBotById(chr.id) == this && actor.mapId == chr.mapId && IsAvailableForAmbientActions();
            BotTiming.AfterRandom(5000, 9000, () =>
            {
                if (!Valid()) return;
                string spoken = Line("CongratsLevelUp"); int emote = random.Next(2) == 0 ? 2 : 6;
                BotTiming.CreateChain().StopUnless(Valid).Run(() =>
                {
                    chr.facing = actor.x < chr.x ? -1 : 1;
                    chr.stance = chr.facing < 0 ? 5 : 4; output.IdleStanding(chr);
                }).PauseRandom(300, 700).Run(() => { if (spoken != null) output.Speak(chr, spoken); })
                    .Pause(400).Run(() => output.Emote(chr, emote)).Start();
            });
        }
        public override void StopScheduledTask()
        {
            lifetime++; flavor.Forget(chr); BotWander.Stop(chr); BotMovementService.Disable(chr);
            base.StopScheduledTask();
        }
    }
}
