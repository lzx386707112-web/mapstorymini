using System;

// Exercise the actual decision and ground integrator at different render rates.
// Flat-ground values are from the project's original Map/Physics.img.
static class AndroidFollowCheck
{
    static readonly NativeMovementPhysics Physics = new NativeMovementPhysics {
        WalkForce = 140000, WalkSpeed = 125, WalkDrag = 80000,
        MinFriction = .05, MaxFriction = 2
    };
    sealed class Motion
    {
        public double owner, robot, velocity;
        int previousX, direction;
        public bool moving;
        public int transitions;
        public void Step(int sign, double speed, double dt, bool walk)
        {
            if (walk) owner += sign * speed * dt;
            bool left = sign < 0;
            direction = NativeAndroidService.FollowDirection(left, left, (int)owner,
                (int)robot, previousX, direction);
            previousX = (int)robot;
            double before = velocity;
            velocity = Physics.Ground(velocity, direction, 0, 1, speed * 1.15 / Physics.WalkSpeed, 0, dt);
            double displacement = (before + velocity) * .5 * dt;
            robot += displacement;
            bool next = displacement != 0;
            if (moving != next) transitions++;
            moving = next;
        }
    }
    static void Require(bool pass, string message)
    { if (!pass) throw new Exception(message); }
    static void Main()
    {
        foreach (int fps in new[] { 30, 60, 120, 240 })
        foreach (double speed in new[] { 125d, 175d })
        {
            var right = new Motion(); var left = new Motion();
            double dt = 1d / fps;
            for (int i = 0; i < fps * 10; i++)
            {
                right.Step(1, speed, dt, true); left.Step(-1, speed, dt, true);
                Require(Math.Abs(right.robot + left.robot) < 1e-8, "left/right positions diverged");
            }
            Require(right.transitions == 1 && left.transitions == 1, $"walk/stand chatter at {fps}fps, speed {speed}: {right.transitions}");
            Require(right.owner - right.robot > 40 && right.owner - right.robot < 90, "overlap or failure to catch up");
            for (int i = 0; i < fps * 3; i++)
            { right.Step(1, speed, dt, false); left.Step(-1, speed, dt, false); }
            Require(!right.moving && !left.moving && right.velocity == 0 && left.velocity == 0, "does not settle after stop");
            for (int i = 0; i < fps * 10; i++)
            {
                right.Step(-1, speed, dt, true); left.Step(1, speed, dt, true);
                Require(Math.Abs(right.robot + left.robot) < 1e-8, "turning symmetry failed");
            }
            Require(right.robot - right.owner > 40 && right.robot - right.owner < 90, "turning keeps wrong spawn mode");
            Console.WriteLine($"PASS {fps}fps, {speed}px/s: symmetric walk/stop/turn; no continuous-walk animation chatter");
        }
    }
}
