using System;

// BeiDou.exe 0x504BFF / 0x5064FF / 0x4416DB. Coordinates are source pixels, Y down.
public static class NativeDropMotion
{
    public static int MesoTier(int amount) => amount < 50 ? 0 : amount < 100 ? 1 : amount < 1000 ? 2 : 3;

    public static int MesoFrame(int amount, int elapsedMs)
    {
        int tier = MesoTier(amount);
        if (tier < 2) return elapsedMs / 80 % 4;
        if (tier == 2) return elapsedMs / 200 % 4;
        int time = elapsedMs % 4360;
        return time < 4000 ? 0 : 1 + (time - 4000) / 120;
    }

    public static int FlightDuration(int sourceY, int targetY)
    {
        if (sourceY <= targetY) return 1000;
        // x87 sqrt -> int64 truncation. NaN's integer-indefinite low DWORD is zero.
        double root = Math.Sqrt((targetY + 100.0 - sourceY) * 2.5);
        return double.IsNaN(root) ? 530 : Math.Min(1000, ((int)root + 1) * 30 + 500);
    }

    static int Round(double value) => (int)(value >= 0 ? value + 0.5 : value - 0.499999999);

    public static void Flight(int sourceX, int sourceY, int targetX, int targetY, int elapsedMs,
        out int x, out int y, out bool landed)
    {
        int duration = FlightDuration(sourceY, targetY);
        int ticks = Math.Max(0, elapsedMs / 30);
        int flight = Math.Min(ticks * 30, ((duration + 29) / 30) * 30);
        double t = flight * 0.001;
        double first = Math.Min(flight * 0.002, 1.0);
        double second = flight <= 500 ? 0 : Math.Min((flight - 500.0) / (duration - 500), 1.0);
        x = Round(sourceX + (targetX - sourceX) * 0.5 * (first + second));
        y = Round(sourceY - 400 * t + 400 * t * t);
        landed = flight >= duration && sourceY >= targetY;
        if (flight >= duration && sourceY < targetY)
        {
            // State 2 starts on the following 30ms update, falling at 400px/s.
            int fallMs = Math.Max(0, ticks * 30 - flight);
            y = Round(sourceY + fallMs * 0.4);
            landed = y >= targetY;
            x = targetX;
        }
        if (landed) { x = targetX; y = targetY; }
    }

    public static double Bob(int ticks) => Math.Sin(ticks * 0.0942477) * 3;

    public static int LandingTick(int sourceY, int targetY)
        => (FlightDuration(sourceY, targetY) + 29) / 30 +
            (sourceY < targetY ? (targetY - sourceY + 11) / 12 : 0);

    public static bool InPickupRange(int feetX, int feetY, int dropX, int dropY)
        => dropX >= feetX - 25 && dropX < feetX + 25 && dropY >= feetY - 50 && dropY < feetY + 10;

    public static bool Pickup(int sourceX, int sourceY, int targetX, int targetY, int elapsedMs,
        out int x, out int y, out int alpha)
    {
        int t = Math.Max(0, Math.Min(elapsedMs, 700));
        x = ((700 - t) * sourceX + t * targetX) / 700;
        y = ((700 - t) * sourceY + t * targetY) / 700 + 160 * (t - 350) * (t - 350) / 490000 - 40;
        alpha = t <= 420 ? 255 : 255 + (420 - t) * 192 / 280;
        return elapsedMs < 700;
    }
}
