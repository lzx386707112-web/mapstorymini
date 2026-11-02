// 单段伤害上限的起点（源端 DamageCapService 的个人初始上限）。
// 这个文件不含 UnityEngine：客户端 CombatRules 与 Server 侧（DamageCapService / 角色行）
// 共用同一个常量，避免两边各写一份 19999999。
// 宽度是 long：上限本身可以靠突破石成长，不能再被 int 的 21 亿限制住。
public static class DamageCap
{
    public const long Initial = 19999999;
}
