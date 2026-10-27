using System;
using Server.Field;
using Point = Server.Bot.Environment.BotNavigationGraph.Point;

namespace Server.Bot.Environment
{
    // 图构建预测使用固定水平步长，原端不会在预测中调用运行时空气阻力/方向输入。
    public sealed class BotTrajectory
    {
        public sealed class Landing
        {
            public readonly Point point;
            public readonly FootholdSegment foothold;
            public readonly int deltaX, deltaY, ticks;
            public int TimeMs => ticks * 50;
            public Landing(Point point, FootholdSegment foothold, int dx, int dy, int ticks)
            { this.point = point; this.foothold = foothold; deltaX = dx; deltaY = dy; this.ticks = ticks; }
        }
        public sealed class RopeGrab
        {
            public readonly Point point;
            public readonly int ticks;
            public int TimeMs => ticks * 50;
            public RopeGrab(Point point, int ticks) { this.point = point; this.ticks = ticks; }
        }
        public sealed class StableLanding
        {
            public readonly Landing landing;
            public readonly Point point;
            public readonly FootholdSegment foothold;
            public readonly bool lostGround;
            public StableLanding(Landing landing, Point point, FootholdSegment foothold, bool lost)
            { this.landing = landing; this.point = point; this.foothold = foothold; lostGround = lost; }
        }
        readonly MapRuntime map;
        readonly BotAirCollision collision;
        readonly BotMovementProfile profile;
        public BotTrajectory(MapRuntime map, BotAirCollision collision, BotMovementProfile profile)
        { this.map = map; this.collision = collision; this.profile = profile ?? BotMovementProfile.Base; }
        public Landing Jump(Point start, int stepX) => Simulate(start, -profile.JumpSpeed(555) * .05f, stepX, 0, null, out _);
        public Landing Fall(Point start, int stepX) => Simulate(start, 0, stepX, 0, null, out _);
        public Landing DownJump(Point start) => Simulate(start, -196 * .05f, 0, 350, null, out _);
        public Landing RopeJump(Point start, int stepX) => Simulate(start, -profile.JumpSpeed(375) * .05f, stepX, 0, null, out _);
        public StableLanding PostLanding(Landing landing, int direction, BotGroundMotion motion, BotGroundPhysics physics, int ticks = 3)
        {
            if (landing == null) return null;
            var point = landing.point; var foothold = landing.foothold;
            double speed = BotAirMotion.LandingSpeed(foothold, landing.deltaX, landing.deltaY, physics, profile);
            var state = new BotGroundPhysics.Travel(point.x, speed);
            bool ice = physics.SlipScale(map.footholdSpeed, profile) < 1;
            for (int i = 0; i < (ice ? 240 : ticks); i++)
            {
                int dir = ice ? motion.SlipperyStopDirection(point, foothold, state, profile) : direction;
                if (ice && dir == 0) return new StableLanding(landing, point, foothold, false);
                var step = motion.Step(point, foothold, dir, state, profile);
                if (step.lostGround) return new StableLanding(landing, step.point, step.foothold, true);
                point = step.point; foothold = step.foothold; state = step.travel;
            }
            return new StableLanding(landing, point, foothold, ice);
        }
        public RopeGrab Grab(Point start, int stepX, MapRope rope, bool ropeJump = false, bool downJump = false)
        {
            Simulate(start, downJump ? -196 * .05f : -profile.JumpSpeed(ropeJump ? 375 : 555) * .05f,
                stepX, downJump ? 350 : 0, rope, out var grab);
            return grab;
        }
        Landing Simulate(Point start, float velocityY, int stepX, int graceMs, MapRope rope, out RopeGrab grab)
        {
            grab = null; double x = start.x, y = start.y; int previousY = start.y;
            float gravity = 2000 * .05f * .05f, maxFall = 670 * .05f;
            int floor = map.bottom > map.top ? map.bottom + 600 : int.MaxValue;
            for (int tick = 0; tick < 2000; tick++)
            {
                if (rope != null)
                {
                    var current = new Point(BotGroundPhysics.JavaRound(x), BotGroundPhysics.JavaRound(y));
                    if (BotRopeMotion.CanGrab(current, rope)) { grab = new RopeGrab(new Point(rope.x, current.y), tick); return null; }
                }
                if (graceMs > 0) graceMs = Math.Max(0, graceMs - 50);
                x += stepX; y += velocityY + .5f * gravity; velocityY = Math.Min(velocityY + gravity, maxFall);
                var previous = new Point(BotGroundPhysics.JavaRound(x - stepX), previousY);
                var next = new Point(BotGroundPhysics.JavaRound(x), BotGroundPhysics.JavaRound(y));
                var hit = collision.Resolve(previous, next);
                if (hit.kind == BotAirCollision.Kind.Wall)
                { x = hit.point.x; y = hit.point.y; stepX = 0; previousY = hit.point.y; }
                else if (hit.kind == BotAirCollision.Kind.Ceiling)
                { x = hit.point.x; y = hit.point.y; velocityY = 0; previousY = hit.point.y; }
                else
                {
                    if (hit.kind == BotAirCollision.Kind.Land && (graceMs == 0 || hit.foothold.forbidFallDown))
                        return rope != null ? null : new Landing(hit.point, hit.foothold, next.x - previous.x, next.y - previous.y, tick + 1);
                    if (next.y > floor) return null;
                    previousY = next.y;
                }
            }
            return null;
        }
    }
}
