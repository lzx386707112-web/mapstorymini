using System;
using System.Collections.Generic;
using Server.Bot.Environment;
using Server.Field;

namespace Server.Bot.Types
{
    // 原AmbientMapBot的可达行为；旧walkToward/walkSmallSteps没有调用者。
    public sealed class AmbientMapBot : BotStateMachine
    {
        readonly DbStore db;
        readonly Random random;
        readonly Func<IEnumerable<BotActor>> players;
        long nextActionAt;
        public AmbientMapBot(BotCharacter actor, DbStore db, Func<IEnumerable<BotActor>> players, Random random = null) : base(actor)
        {
            this.db = db; this.players = players; this.random = random ?? new Random();
            botType = "AmbientMapBot"; ScheduleNext();
        }
        public override bool IsAvailableForAmbientActions() => state != State.TRADING;
        public override void CheckPrioritySpeed() => UpdateScheduleDelay(CheckMainPlayersOnMap() ? BotConfig.AmbientActionMinMs(db) : 2500);
        public override void UpdateState()
        {
            base.UpdateState();
            if (!BotConfig.AmbientBehaviorEnabled(db) || !BotConfig.AmbientHasAnyActionEnabled(db))
            { StopScheduledTask(); return; }
            if (CheckIfNotRunningOrPaused() || BotScheduler.Now() < nextActionAt) return;
            BotTypeManager.Flavor.MaybeExpress(this);
            int move = BotConfig.AmbientMoveEnabled(db) ? 85 : 0;
            int face = BotConfig.AmbientFacePlayerEnabled(db) ? 5 : 0;
            int emote = BotConfig.AmbientEmoteEnabled(db) ? 5 : 0;
            int chat = BotConfig.AmbientChatEnabled(db) ? 5 : 0;
            int total = move + face + emote + chat;
            if (total > 0)
            {
                int roll = random.Next(total);
                if (roll < move) Stroll();
                else if (roll < move + face) FaceNearbyPlayerOrTurn();
                else if (roll < move + face + emote) output.Emote(chr, random.Next(1, 23));
                else output.Speak(chr, marketFlavor.Chat());
            }
            ScheduleNext();
        }
        void ScheduleNext()
        {
            long min = Math.Min(BotConfig.AmbientActionMinMs(db), 800);
            long max = Math.Max(min, Math.Min(BotConfig.AmbientActionMaxMs(db), 1600));
            nextActionAt = BotScheduler.Now() + min + (long)(random.NextDouble() * (max - min + 1));
        }
        void Stroll()
        {
            var map = MapService.Get(chr.mapId);
            if (map == null || map.footholds.Count == 0 || map.maxX <= map.minX) return;
            BotPoint? target = null;
            for (int i = 0; i < 24; i++)
            {
                int x = random.Next((int)map.minX, (int)map.maxX + 1);
                var y = map.PointBelow(x, map.minY);
                if (y == null) continue;
                target = new BotPoint(x, (int)y.Value);
                if (Math.Abs(x - chr.x) >= 180) break;
            }
            if (target == null || !BotMovementService.Observed(chr.mapId)) return;
            BotMovementService.Enable(chr);
            BotMovementService.Move(chr, target.Value.x, target.Value.y);
        }
        void FaceNearbyPlayerOrTurn()
        {
            BotActor target = null; float distance = float.MaxValue;
            if (players != null) foreach (var actor in players())
            {
                if (actor.mapId != chr.mapId || actor.id == chr.id || actor.id > 20000 || actor.id == 999) continue;
                float dx = Math.Abs(actor.x - chr.x);
                if (dx < distance) { distance = dx; target = actor; }
            }
            bool left = target != null && distance < 500 ? target.x < chr.x : chr.facing >= 0;
            if (left == (chr.facing < 0)) return;
            var recording = BotMovementRecording.ReadBinary(BotResources.Load().ReadBytes(
                BotMovementRecording.ResourcePath(0, left ? "turnaroundtoleft" : "turnaroundtoright", "bin")));
            BotMovementPlayback.Play(chr, recording, offset: true);
        }
        public override void StopScheduledTask()
        {
            BotTypeManager.Flavor.Forget(chr); BotMovementService.Disable(chr);
            base.StopScheduledTask();
        }
    }
}
