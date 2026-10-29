using System;

// 67DB9A/67DC1C/67DC99 add 100, clamp the template to 0..140;
// 664380 clamps template+runtime correction to 10..140.
public static class NativeMobMotion
{
    public static double Multiplier(int raw,int correction=0)
        => Math.Max(10,Math.Min(140,Math.Max(0,Math.Min(140,100L+raw))+(long)correction))*.01;
}
