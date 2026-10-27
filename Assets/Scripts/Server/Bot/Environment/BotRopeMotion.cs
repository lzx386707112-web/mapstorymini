using System;
using Server.Field;
using Point = Server.Bot.Environment.BotNavigationGraph.Point;

namespace Server.Bot.Environment
{
    public sealed class BotRopeMotion
    {
        readonly BotGroundIndex ground;
        public BotRopeMotion(BotGroundIndex ground) { this.ground = ground; }
        public enum Result { Hold, Climb, Land, Fall }
        public sealed class State
        {
            public MapRope rope;
            public Point position;
            public int direction;
            public bool climbing;
        }
        public static int FirstClimbableY(MapRope rope) => Math.Min(rope.Bottom, rope.Top + 1);
        public static bool CanGrab(Point position, MapRope rope) => rope != null && Math.Abs(position.x - rope.x) <= 8 &&
            position.y >= FirstClimbableY(rope) && position.y <= rope.Bottom;
        public static State Attach(MapRope rope, int y) => new State { rope = rope, climbing = true,
            position = new Point(rope.x, Math.Max(FirstClimbableY(rope), Math.Min(rope.Bottom, y))) };
        public Point? TopExitLanding(MapRope rope)
        {
            if (rope == null) return null;
            Point? best = null; int bestDelta = int.MaxValue;
            foreach (int x in new[] { rope.x, rope.x - 8, rope.x + 8 })
            {
                var point = ground.PointBelow(x, rope.Top - 25);
                if (!point.HasValue || point.Value.y < rope.Top - 24 || point.Value.y > rope.Top + 20) continue;
                int delta = Math.Abs(point.Value.y - rope.Top);
                if (delta < bestDelta) { best = point; bestDelta = delta; }
            }
            return best;
        }
        public Result Step(State state)
        {
            if (state.rope == null) { state.climbing = false; return Result.Fall; }
            int dir = Math.Sign(state.direction), y = state.position.y + dir * 5;
            if (y <= state.rope.Top)
            {
                var landing = TopExitLanding(state.rope);
                if (landing.HasValue) { state.position = landing.Value; state.climbing = false; state.rope = null; return Result.Land; }
                state.position = new Point(state.rope.x, FirstClimbableY(state.rope));
                return Result.Hold;
            }
            if (y > state.rope.Bottom) { state.climbing = false; state.rope = null; return Result.Fall; }
            if (dir == 0) return Result.Hold;
            state.position = new Point(state.rope.x, y);
            return Result.Climb;
        }
    }
}
