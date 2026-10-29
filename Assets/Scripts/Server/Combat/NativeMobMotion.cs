using System;

// 67DB9A/67DC1C/67DC99 add 100, clamp the template to 0..140;
// 664380 clamps template+runtime correction to 10..140.
public static class NativeMobMotion
{
    public static double Multiplier(int raw,int correction=0)
        => Math.Max(10,Math.Min(140,Math.Max(0,Math.Min(140,100L+raw))+(long)correction))*.01;

    // 0x66E5CC..0x66E61A: integer abs(dx)/10 + abs(dy)/3, not a radius.
    public static bool AcquisitionRange(int dx, int dy)
        => Math.Abs((long)dx)/10 + Math.Abs((long)dy)/3 <= 40;
}

// 0x9BBD88 / 0x9BCA2A: target lifetime is initialized only on assignment.
public sealed class NativeMobPursuit
{
    public bool Active { get; private set; }
    long remaining;
    public void Acquire(int randomBelow60000)
    {
        if (Active) return;
        if (randomBelow60000<0 || randomBelow60000>=60000) throw new ArgumentOutOfRangeException(nameof(randomBelow60000));
        Active=true;remaining=180000L+randomBelow60000;
    }
    public void Clear() { Active=false;remaining=0; }
    public void Tick(long milliseconds, bool targetAlive)
    {
        if (!targetAlive) { Clear();return; }
        if (!Active) return;
        remaining-=milliseconds;
        if (remaining<0) Clear();
    }
}
