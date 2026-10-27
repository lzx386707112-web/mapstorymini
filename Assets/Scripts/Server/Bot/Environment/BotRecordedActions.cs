using System;
using System.Collections.Generic;
using Server.Field;

namespace Server.Bot.Environment
{
    // MovementCommands.walkAlongX及微转身；保持源端10px/160ms封包步行，不以物理AI替换。
    public static class BotRecordedActions
    {
        sealed class Walk
        {
            public BotCharacter actor;
            public int mapId, targetX, remaining;
            public bool left;
            public Action completed;
            public BotTasks.Handle task;
        }
        static readonly Dictionary<int, Walk> walks = new Dictionary<int, Walk>();
        public static bool IsMoving(BotCharacter actor) => actor != null && walks.ContainsKey(actor.id);
        public static void Cancel(BotCharacter actor)
        { if (actor != null && walks.TryGetValue(actor.id, out var walk)) { walk.task?.Cancel(); walks.Remove(actor.id); } }
        static void Send(BotCharacter actor, params BotMovementRecording.Command[] commands)
        {
            byte[] packet = BotMovementPacket.Apply(BotMovementPacket.Reconstruct(commands), actor);
            BotMovementPlayback.Broadcast?.Invoke(actor, packet);
        }
        static short Floor(BotCharacter actor, int x, int y)
        {
            var map = MapService.Get(actor.mapId); var fh = map == null ? null : new BotGroundIndex(map).Below(x, y);
            return unchecked((short)(fh?.id ?? 0));
        }
        static BotMovementRecording.Command Normal(int x, int y, int vx, int vy, int fh, int stance, int duration)
            => new BotMovementRecording.Command(0, unchecked((short)x), unchecked((short)y), unchecked((short)vx),
                unchecked((short)vy), unchecked((short)fh), (sbyte)stance, (short)duration);
        public static void Turn(BotCharacter actor, bool left)
        {
            // 源facingLeft只把stance1/3/5视为左向，不含坐姿。
            bool facingLeft = actor.stance == 1 || actor.stance == 3 || actor.stance == 5;
            if (facingLeft == left) return;
            try
            {
                var bytes = BotResources.Load().ReadBytes(BotMovementRecording.ResourcePath(0, left ? "turnaroundtoleft" : "turnaroundtoright", "bin"));
                BotMovementPlayback.Play(actor, BotMovementRecording.ReadBinary(bytes), true);
            }
            catch (Exception) { /* 原端缺录制不打断调用者。 */ }
        }
        public static void Jump(BotCharacter actor)
        {
            int x = (int)actor.x, y = (int)actor.y, side = actor.stance & 1; short fh = Floor(actor, x, y);
            Send(actor, Normal(x, y - 80, 0, 300, fh, 2 + side, 180), Normal(x, y, 0, 0, fh, 4 + side, 180));
        }
        public static bool WalkAlongX(BotCharacter actor, int destX, int maxSteps, Action completed)
        {
            if (actor == null || maxSteps <= 0 || MapService.Get(actor.mapId) == null || IsMoving(actor)) return false;
            int x = (int)actor.x, y = (int)actor.y;
            int grounded = (int)MapService.SnapToGround(actor.mapId, x, y);
            if (Math.Abs(grounded - y) > 6) Send(actor, Normal(x, grounded, 0, 0, Floor(actor, x, y), actor.stance, 200));
            var walk = new Walk { actor = actor, mapId = actor.mapId, targetX = destX, remaining = maxSteps,
                left = destX < actor.x, completed = completed };
            walks.Add(actor.id, walk); Step(walk); return true;
        }
        static void Step(Walk walk)
        {
            var actor = walk.actor;
            if (!ReferenceEquals(BotRegistry.Get(actor.id), actor) || actor.mapId != walk.mapId)
            { Cancel(actor); return; }
            if (actor.trade != null) { Complete(walk, false); return; }
            if (walk.remaining-- <= 0 || Math.Abs(actor.x - walk.targetX) <= 20) { Complete(walk, true); return; }
            int x = (int)actor.x + (walk.targetX >= actor.x ? 10 : -10);
            int y = (int)MapService.SnapToGround(actor.mapId, x, actor.y);
            Send(actor, Normal(x, y, walk.left ? -100 : 100, 0, Floor(actor, x, y), walk.left ? 1 : 0, 160));
            walk.task = BotTasks.Schedule(() => Step(walk), 160);
        }
        static void Complete(Walk walk, bool stop)
        {
            var actor = walk.actor; Cancel(actor);
            if (stop)
            {
                var command = Normal((int)actor.x, (int)actor.y, 0, 0, 0, 4 + (actor.stance & 1), 250);
                Send(actor, command, command);
            }
            walk.completed?.Invoke();
        }
    }
}
