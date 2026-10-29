using System;
using MapleDash.Wz;

// BeiDou.exe 0x9B23F2 / 0x9B2BCB / 0x9B2CCF: pixel units, seconds.
// Keep doubles until the final conversion to Unity coordinates (native uses x87 doubles).
public sealed class NativeMovementPhysics
{
    public double WalkForce, WalkSpeed, WalkDrag, SlipForce, SlipSpeed;
    public double FloatDrag1, FloatDrag2, FloatCoefficient, Gravity, FallSpeed, JumpSpeed;
    public double MaxFriction, MinFriction;
    public double FlyForce, FlySpeed;
    public const double Mass = 100; // CLifeMoveParam ctor 0x4FE8B3, constant 0xAF29E0.
    public const double SlipThreshold = .9; // ctor 0x4FE8FB, constant 0xAF32D8.

    public static NativeMovementPhysics Load()
    {
        var n = WzAssets.FileRoot("Map/Physics.img");
        return new NativeMovementPhysics {
            WalkForce = Convert.ToDouble(n.Get("walkForce").Value), WalkSpeed = Convert.ToDouble(n.Get("walkSpeed").Value),
            WalkDrag = Convert.ToDouble(n.Get("walkDrag").Value), SlipForce = Convert.ToDouble(n.Get("slipForce").Value),
            SlipSpeed = Convert.ToDouble(n.Get("slipSpeed").Value), FloatDrag1 = Convert.ToDouble(n.Get("floatDrag1").Value),
            FloatDrag2 = Convert.ToDouble(n.Get("floatDrag2").Value), FloatCoefficient = Convert.ToDouble(n.Get("floatCoefficient").Value),
            Gravity = Convert.ToDouble(n.Get("gravityAcc").Value), FallSpeed = Convert.ToDouble(n.Get("fallSpeed").Value),
            JumpSpeed = Convert.ToDouble(n.Get("jumpSpeed").Value), MaxFriction = Convert.ToDouble(n.Get("maxFriction").Value),
            MinFriction = Convert.ToDouble(n.Get("minFriction").Value),
            FlyForce = Convert.ToDouble(n.Get("flyForce").Value), FlySpeed = Convert.ToDouble(n.Get("flySpeed").Value)
        };
    }
    static double Approach(double v, double target, double step)
    { return v < target ? Math.Min(target, v + step) : Math.Max(target, v - step); }
    // Original 0x9B2BCB accelerates only toward the signed limit; it does not clamp
    // pre-existing velocity from a faster platform/jump when acceleration points away.
    static double Accelerate(double v, double force, double limit, double dt)
    {
        if (force > 0 && v < limit) return Math.Min(limit, v + force / Mass * dt);
        if (force <= 0 && v > -Math.Abs(limit)) return Math.Max(-Math.Abs(limit), v + force / Mass * dt);
        return v;
    }
    static double Restrict(double v, double limit, double step)
    { return limit >= 0 && Math.Abs(v) > limit ? Approach(v, Math.Sign(v) * limit, step) : v; }

    // Constructor defaults for secured character/map multipliers are 1. Status and
    // equipment multipliers must be supplied by their native attribute pipeline.
    public double Ground(double velocity, int input, double force, double drag, double walk, double slope, double dt)
    {
        double baseForce = WalkForce * walk;
        double drive = input * baseForce;
        double speed = WalkSpeed * walk;
        if (force != 0) {
            // 0xA62018 truncates toward zero before abs; preserve the native conversion.
            double f = Math.Abs((int)force);
            double factor = input == 0 ? force : input * force > 0 ? 2 * f : .2 / f;
            drive = (input == 0 ? 1 : input) * baseForce * factor;
            speed *= input == 0 ? (int)Math.Abs(force) : factor;
        }
        double s2 = slope * slope;
        drive *= slope < 0 ? 1 - s2 : 1 + s2;
        double signedLimit = speed;
        double friction = Math.Max(MinFriction, Math.Min(MaxFriction, drag));
        if (friction < 1) friction *= .5;
        double brake = friction * WalkDrag / Mass * dt;
        if (slope == 0) {
            velocity = Restrict(velocity, signedLimit, brake);
            return input == 0 && force == 0 ? Approach(velocity, 0, brake) : Accelerate(velocity, drive, signedLimit, dt);
        }
        int uphill = slope < 0 ? 1 : -1;
        double low = Math.Max(0, signedLimit), high = Math.Max(0, (1 + s2) * signedLimit);
        velocity = Restrict(velocity, uphill * velocity > 0 ? low : high, brake);
        if (Math.Abs(slope) <= SlipThreshold) {
            if (input == 0 && force == 0) return Approach(velocity, 0, brake);
            return Accelerate(velocity, drive, uphill * drive > 0 ? low : high, dt);
        }
        double slipForce = slope * SlipForce, slipSpeed = Math.Abs(slope) * SlipSpeed;
        if (input * uphill > 0) { slipForce *= .5; slipSpeed *= .5; }
        else if (input != 0 || force != 0) { slipForce += drive; slipSpeed += high; }
        if (uphill * velocity > 0) velocity = Approach(velocity, 0, brake);
        return Accelerate(velocity, slipForce, slipSpeed, dt);
    }

    // Ordinary airborne branch 0x9B2CCF..0x9B2EDE. Swimming/flying are separate native paths.
    public double Air(double velocity, double downwardVelocity, int input, double dt)
    {
        double drag = FloatDrag2 / Mass * dt;
        double limit = WalkSpeed / WalkForce * FloatDrag2;
        if (input == 0) return Approach(velocity, 0, downwardVelocity < FallSpeed ? drag * FloatCoefficient : drag);
        return Accelerate(velocity, input * FloatDrag2 * 2, limit, dt);
    }

    // CMobMove (virtual +8 == 0), 0x9B313D..0x9B3421. Y is positive downward.
    // Upward movement comes from flap(), not a negative vertical input force.
    public void Flight(ref double vx, ref double vy, int horizontal, int vertical, double fly, double dt, double gravity = 1)
    {
        gravity = Math.Abs(gravity);
        double limit = FlySpeed * fly * gravity;
        double brake = FloatDrag1 / Mass * dt;
        vx = Restrict(vx, limit, brake);
        vx = horizontal == 0 ? Approach(vx, 0, brake) : Accelerate(vx, horizontal * FlyForce * gravity, limit, dt);
        vy = Restrict(vy, limit, brake);
        double acceleration = FlyForce / Mass / gravity;
        if (vertical < 0) {
            double target = limit * .3;
            vy = vy > target ? Math.Max(target, vy - acceleration * dt) : Math.Min(target, vy + acceleration * dt * .5);
        }
        // Preserve the original positive-input branch, including its immediate
        // target assignment (0x9B339E..0x9B33EC); do not replace it with smoothing.
        else if (vertical > 0) vy = limit * 1.5;
        else vy = Accelerate(vy, acceleration * Mass, limit, dt);
    }
    // 0x9B20FD..0x9B2131, airborne mob flap replaces, rather than adds to, vy.
    public double Flap(double fly, double gravity = 1) => -FlySpeed * fly * Math.Abs(gravity);
}
