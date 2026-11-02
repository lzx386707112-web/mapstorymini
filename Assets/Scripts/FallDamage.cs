// 跌落伤害（纯 C#，client 与自检共用，不引用 UnityEngine）。
//
// 口径来源：源端 BeiDou 服务端
//   soloMapling/ArtificialPlayer/GCMoveSystem/BotContactDamage.java
//     FALL_DIST_THRESHOLD_PX = 890.0f
//     FALL_DMG_SAT          = 28.0f
//     FALL_KNEE_SHARPNESS   = 0.013f
//     FALL_DMG_PER_PX_TAIL  = 0.0024f
//   fallDamageFromDistance():
//     u = dist - 890; dmg = 28*(1-e^(-0.013u)) + 0.0024u; Math.round，且最少 1
//   landOnGround(): fallDistance = 落点 y - fallPeakPhysY（地图坐标 y 向下为正，
//   所以起跳的上升段也算进总落差）。
//
// 注意常量：Java 侧是 float 再宽化成 double，(double)0.013f = 0.013000000268220901、
// (double)0.0024f = 0.002400000113993883，这里是它们的原值，不要改写成十进制字面量。
public static class FallDamage
{
    public const float ThresholdPx = 890f;

    const double Saturation = 28.0;
    const double KneeSharpness = 0.013000000268220901;
    const double PerPixelTail = 0.002400000113993883;

    /// <summary>按下落距离（原端像素）算跌落伤害；不到阈值返回 0。</summary>
    public static int FromDistance(double distancePx)
    {
        if (distancePx <= ThresholdPx) return 0;
        double u = distancePx - ThresholdPx;
        double dmg = Saturation * (1.0 - System.Math.Exp(-KneeSharpness * u)) + PerPixelTail * u;
        // Java Math.round = floor(x + 0.5)
        return (int)System.Math.Max(1L, (long)System.Math.Floor(dmg + 0.5));
    }
}
