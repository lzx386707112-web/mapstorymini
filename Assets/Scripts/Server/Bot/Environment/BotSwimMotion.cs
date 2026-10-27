using System;
using Point = Server.Bot.Environment.BotNavigationGraph.Point;

namespace Server.Bot.Environment
{
    public sealed class BotSwimMotion
    {
        public sealed class State
        {
            public double x, y, horizontalSpeed, groundSpeed;
            public float verticalSpeed;
            public int direction, verticalHold, facing = 1;
            public bool swimming, jumpRequested;
            public Point position;
        }
        readonly BotAirCollision collision;
        readonly BotGroundPhysics physics;
        readonly BotMovementProfile profile;
        public BotSwimMotion(BotAirCollision collision, BotGroundPhysics physics, BotMovementProfile profile)
        { this.collision = collision; this.physics = physics; this.profile = profile ?? BotMovementProfile.Base; }
        public bool Step(State state)
        {
            const float friction = 4.21f, gravity = 590f, accel = 600f, thrustUp = 412f, thrustDown = 295f;
            const double t = (double).05f;
            if (!state.swimming || Math.Abs(state.x - state.position.x) > 2 || Math.Abs(state.y - state.position.y) > 2)
            { state.x = state.position.x; state.y = state.position.y; }
            double vx = state.horizontalSpeed, vy = state.verticalSpeed;
            if (state.jumpRequested) { vy = -(float)(1000f * (float)profile.SpeedMultiplier); state.jumpRequested = false; }
            if (state.direction != 0) vx += accel * t * Math.Sign(state.direction);
            double retention = Math.Max(0, 1 - friction * t);
            vx *= retention; vy = vy * retention + gravity * t;
            if (state.verticalHold < 0) vy -= thrustUp * t;
            else if (state.verticalHold > 0) vy += thrustDown * t;
            vx = Math.Max(-800, Math.Min(800, vx));
            if (state.direction > 0 && vx > 140) vx = 140;
            if (state.direction < 0 && vx < -140) vx = -140;
            double sinkCap = state.verticalHold < 0 ? 42 : state.verticalHold > 0 ? 210 : 140;
            vy = Math.Max(-800, Math.Min(sinkCap, vy));
            double nextX = state.x + vx * t, nextY = state.y + vy * t;
            var previous = new Point(BotGroundPhysics.JavaRound(state.x), BotGroundPhysics.JavaRound(state.y));
            var next = new Point(BotGroundPhysics.JavaRound(nextX), BotGroundPhysics.JavaRound(nextY));
            var hit = collision.Resolve(previous, next);
            if (state.direction != 0) state.facing = Math.Sign(state.direction);
            if (hit.kind == BotAirCollision.Kind.Land)
            {
                state.x = hit.point.x; state.y = hit.point.y; state.position = hit.point;
                state.swimming = false; state.verticalSpeed = 0;
                state.groundSpeed = BotAirMotion.LandingSpeed(hit.foothold, hit.point.x - previous.x, hit.point.y - previous.y, physics, profile);
                state.horizontalSpeed = state.groundSpeed;
                return true;
            }
            if (hit.kind == BotAirCollision.Kind.Wall) { nextX = hit.point.x; vx = 0; }
            state.horizontalSpeed = vx; state.verticalSpeed = (float)vy;
            state.x = nextX; state.y = nextY; state.swimming = true;
            state.position = new Point(BotGroundPhysics.JavaRound(nextX), BotGroundPhysics.JavaRound(nextY));
            return false;
        }
    }
}
