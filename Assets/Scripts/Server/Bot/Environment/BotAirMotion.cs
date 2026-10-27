using System;
using Server.Field;
using Point = Server.Bot.Environment.BotNavigationGraph.Point;

namespace Server.Bot.Environment
{
    // 原50ms空中物理；速度使用每tick像素，不能混入前端的px/s。
    public sealed class BotAirMotion
    {
        public sealed class State
        {
            public double x, y, steerVelocity, peakY, groundSpeed, fallDistance;
            public float velocityY;
            public int velocityX, direction, facing = 1, landingGraceMs;
            public bool fixedArc, pendingFlash, flashFired, airborne = true;
            public float flashScale = 1;
            public Point Position => new Point(BotGroundPhysics.JavaRound(x), BotGroundPhysics.JavaRound(y));
        }
        readonly MapRuntime map;
        readonly BotAirCollision collision;
        readonly BotGroundPhysics groundPhysics;
        readonly BotMovementProfile profile;
        const float Tick = .05f;
        public BotAirMotion(MapRuntime map, BotAirCollision collision, BotGroundPhysics groundPhysics, BotMovementProfile profile)
        { this.map = map; this.collision = collision; this.groundPhysics = groundPhysics; this.profile = profile ?? BotMovementProfile.Base; }
        public State Launch(Point from, int horizontalStep, bool ropeJump = false, bool downJump = false)
            => new State { x = from.x, y = from.y, peakY = from.y, velocityX = horizontalStep,
                velocityY = downJump ? -196 * Tick : -profile.JumpSpeed(ropeJump ? 375 : 555) * Tick,
                landingGraceMs = downJump ? 350 : 0 };
        public static void AdvanceTimers(State state) => state.landingGraceMs = Math.Max(0, state.landingGraceMs - 50);
        public BotAirCollision.Kind Step(State state)
        {
            if (!state.airborne) return BotAirCollision.Kind.None;
            double fs = groundPhysics.SlipScale(map.footholdSpeed, profile);
            double total = state.velocityX + state.steerVelocity;
            if (state.direction != 0)
            {
                double band = profile.WalkVelocity(125) * Tick * fs / 14, inDir = total * state.direction;
                if (inDir < band) state.steerVelocity = Math.Min(band, inDir + 200 * fs * Tick * Tick) * state.direction - state.velocityX;
                state.facing = state.direction > 0 ? 1 : -1;
            }
            else if (!state.fixedArc && total != 0)
            {
                double drag = (state.velocityY >= 670 * Tick ? 100 : 1) * fs * Tick * Tick;
                double next = total > 0 ? Math.Max(0, total - drag) : Math.Min(0, total + drag);
                state.steerVelocity = next - state.velocityX;
            }
            if (state.pendingFlash && state.velocityY >= -(1 - state.flashScale) * profile.JumpSpeed(555) * Tick)
            {
                int dir = state.velocityX != 0 ? Math.Sign(state.velocityX) : state.facing >= 0 ? 1 : -1;
                state.velocityX = BotGroundPhysics.JavaRound(dir * 550 * Tick * state.flashScale);
                state.steerVelocity = 0; state.velocityY = -350 * Tick * state.flashScale;
                state.fixedArc = true; state.pendingFlash = false; state.flashFired = true;
            }
            var previous = state.Position;
            state.x += state.velocityX + state.steerVelocity;
            float gravity = 2000 * Tick * Tick;
            state.y += state.velocityY + .5f * gravity;
            state.velocityY = Math.Min(state.velocityY + gravity, 670 * Tick);
            if (state.y < state.peakY) state.peakY = state.y;
            var nextPosition = state.Position;
            var hit = collision.Resolve(previous, nextPosition);
            if (hit.kind == BotAirCollision.Kind.Wall)
            {
                state.velocityX = 0; state.steerVelocity = 0; state.fixedArc = false;
                state.x = hit.point.x; state.y = hit.point.y;
            }
            else if (hit.kind == BotAirCollision.Kind.Ceiling)
            {
                state.velocityY = 0; state.fixedArc = false; state.x = hit.point.x; state.y = hit.point.y;
            }
            else if (hit.kind == BotAirCollision.Kind.Land && (state.landingGraceMs == 0 || hit.foothold.forbidFallDown))
            {
                state.groundSpeed = LandingSpeed(hit.foothold, nextPosition.x - previous.x, nextPosition.y - previous.y, groundPhysics, profile);
                state.fallDistance = double.IsInfinity(state.peakY) ? 0 : Math.Max(0, hit.point.y - state.peakY);
                state.x = hit.point.x; state.y = hit.point.y; state.airborne = false;
                state.velocityY = 0; state.velocityX = 0; state.steerVelocity = 0; state.fixedArc = false;
                state.pendingFlash = false; state.flashFired = false; state.landingGraceMs = 0;
                state.peakY = double.PositiveInfinity;
                return BotAirCollision.Kind.Land;
            }
            else return BotAirCollision.Kind.None;
            return hit.kind;
        }
        public static double LandingSpeed(FootholdSegment foothold, double dx, double dy, BotGroundPhysics physics, BotMovementProfile profile)
        {
            if (foothold != null && !foothold.Wall && foothold.Slope != 0)
            {
                double tx = foothold.x2 - foothold.x1, ty = foothold.y2 - foothold.y1;
                double length = Math.Sqrt(tx * tx + ty * ty);
                if (length > 0) { tx /= length; ty /= length; dx = tx * (dx * tx + dy * ty); }
            }
            double maxDelta = Math.Max(1, physics.WalkStep(profile));
            double delta = Math.Max(-maxDelta, Math.Min(maxDelta, dx * .5));
            double maxSpeed = physics.MaxSpeed(profile);
            return Math.Max(-maxSpeed, Math.Min(maxSpeed, delta / Math.Max(1, physics.TickMs / 8.0)));
        }
    }
}
