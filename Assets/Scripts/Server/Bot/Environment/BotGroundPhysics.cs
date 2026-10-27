using System;

namespace Server.Bot.Environment
{
    // BotPhysicsEngine.java 2047..2230：50ms决策步内部保留8ms地面物理余数。
    public sealed class BotGroundPhysics
    {
        public int TickMs = 50;
        public double GroundSlip = 3, Friction = .3, SlopeFactor = .1;
        public double BaseHorizontalForce = 16.667;
        public double SlipWalkAcceleration = 1400, SlipGlideDeceleration = 400;
        public readonly struct Travel
        {
            public readonly double X, Speed, CarryMs;
            public Travel(double x, double speed = 0, double carryMs = 0) { X = x; Speed = speed; CarryMs = carryMs; }
        }
        public double SlipScale(float mapFootholdSpeed, BotMovementProfile profile)
            => (profile ?? BotMovementProfile.Base).snowShoes || mapFootholdSpeed <= 0 || mapFootholdSpeed >= 1 ? 1 : mapFootholdSpeed;
        double Force(BotMovementProfile profile) => (profile ?? BotMovementProfile.Base).HorizontalForce(BaseHorizontalForce) * .008;
        public double MaxSpeed(BotMovementProfile profile) => Force(profile) * GroundSlip / (Friction + SlopeFactor);
        public int WalkStep(BotMovementProfile profile) => Math.Max(1, JavaRound(MaxSpeed(profile) * TickMs / 8));
        public int LaunchRunway(float mapFootholdSpeed, BotMovementProfile profile)
        {
            int step = WalkStep(profile); double fs = SlipScale(mapFootholdSpeed, profile);
            if (fs >= 1) return Math.Max(40, step * 6);
            double speed = MaxSpeed(profile) / .008;
            double distance = speed * speed / (2 * SlipWalkAcceleration * fs);
            return Math.Max(40, JavaRound(step * 6 + distance));
        }
        public Travel Advance(Travel state, int direction, double slope, float mapFootholdSpeed, BotMovementProfile profile)
        {
            double carry = state.CarryMs + TickMs;
            int steps = (int)(carry / 8);
            if (steps == 0) return state;
            double x = state.X, speed = state.Speed, slip = SlipScale(mapFootholdSpeed, profile);
            for (int i = 0; i < steps; i++) { speed = Step(speed, direction, slope, slip, profile); x += speed; }
            return new Travel(x, speed, carry - steps * 8);
        }
        public double Step(double speed, int direction, double slope, double slip, BotMovementProfile profile)
        {
            double force = direction * Force(profile);
            if (force == 0 && Math.Abs(speed) < .1) return 0;
            if (slip < 1)
            {
                if (direction != 0)
                {
                    double dv = SlipWalkAcceleration * slip * .008 * .008, cap = MaxSpeed(profile);
                    return Clamp(speed + direction * dv, -cap, cap);
                }
                double decel = SlipGlideDeceleration * slip * .008 * .008;
                return speed - Math.Sign(speed) * Math.Min(Math.Abs(speed), decel);
            }
            double inertia = speed / GroundSlip;
            double drag = (Friction + SlopeFactor * (1 + Clamp(slope, -.5, .5) * -inertia)) * inertia;
            return speed + (force - drag) * slip;
        }
        public int CounterStrafe(float mapFootholdSpeed, BotMovementProfile profile, double speed)
        {
            double fs = SlipScale(mapFootholdSpeed, profile);
            if (fs >= 1) return 0;
            double brake = SlipWalkAcceleration * fs * .008 * .008 * Math.Max(1, TickMs / 8.0);
            return Math.Abs(speed) > brake ? (speed > 0 ? -1 : 1) : 0;
        }
        public int Approach(float mapFootholdSpeed, BotMovementProfile profile, double speed, int targetDelta, int overshootSlack = 0)
        {
            int toward = Math.Sign(targetDelta); double fs = SlipScale(mapFootholdSpeed, profile);
            if (toward == 0 || fs >= 1) return toward;
            double brake = SlipWalkAcceleration * fs * .008 * .008, glide = SlipGlideDeceleration * fs * .008 * .008;
            int steps = Math.Max(1, (int)Math.Ceiling(TickMs / 8.0));
            double release = brake * steps, v = speed * toward, traveled = 0, cap = MaxSpeed(profile);
            for (int i = 0; i < steps; i++) { v = Math.Min(v + brake, cap); traveled += v; }
            while (v > release) { v -= brake; traveled += v; }
            while (v > 0) { v -= glide; traveled += Math.Max(v, 0); }
            return traveled < Math.Abs(targetDelta) + Math.Max(0, overshootSlack) ? toward : CounterStrafe(mapFootholdSpeed, profile, speed);
        }
        public static bool WalkableEndpoint(int dx, int dy) => dx <= 12 && dy <= 16 && dy >= -26;
        public static int JavaRound(double value) => (int)Math.Floor(value + .5);
        static double Clamp(double v, double min, double max) => Math.Max(min, Math.Min(max, v));
    }
}
