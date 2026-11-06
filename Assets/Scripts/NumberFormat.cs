using System.Globalization;

// ————————————————————————————————————————————————————————————
// 大额数值的紧凑写法：过了阈值就不再逐位显示，改成 万 / 亿。
//
// 依据 282 客户端（不是自创格式）：
//   · 282 实拍（装备框图标右侧）：`戰鬥力提升量` 下一行是 `+ 220萬 4312`
//     —— 2,204,312 拆成「220万 + 空格 + 4312」，余数照抄、不补零、没有千分位。
//   · String/StringPoolWz2.img 的 MasterDisciple/UI/NumberUnit1..4 = 京 / 兆 / 億 / 萬，
//     即客户端的单位阶梯是十进制中文单位，不是 k/m/b。
//   · 装备提示框的战斗力图数字形 UI/_Canvas/UIToolTip.img/Item/Equip/imgFont/atkPow/
//     {plus,minus} 只有 sign + 0..9 + a + b 共 12 个字形，**没有小数点**；
//     a/b 就是「万」「亿」两个字（两位数字形宽 19~20px，数字只有 10px）。
//     ⇒ 客户端只能用整数单位，写不出 1.2亿，所以这里也按整数截断。
//
// 只用在「展示」位置（背包金币框、战斗力、称号门槛提示）。
// 交易/输入框必须保留精确值，那里继续用 ToString("N0")。
// ————————————————————————————————————————————————————————————
public static class NumberFormat
{
    public const long Wan = 10000L;
    public const long Yi = 100000000L;
    public const long Zhao = 1000000000000L;          // 兆 10^12
    public const long Jing = 10000000000000000L;      // 京 10^16（对应 282 NumberUnit1）

    /// <summary>
    /// 1 万以上按 282 的写法拆单位：`220万 4312`（单位 + 空格 + 余数，余数不补零、不用千分位），
    /// 再往上是 `1亿 2345万 6789` / `3兆 4567亿 …` / `1京 …`，与 StringPoolWz2 的
    /// MasterDisciple/UI/NumberUnit1..4（京 / 兆 / 億 / 萬）同一阶梯。余数为 0 只留单位。
    /// </summary>
    public static string Compact(long value)
    {
        if (value > -Wan && value < Wan) return value.ToString("N0", CultureInfo.InvariantCulture);
        bool negative = value < 0;
        // long.MinValue 的绝对值装不进 long，先夹到上限（游戏里到不了）。
        long abs = negative ? (value == long.MinValue ? long.MaxValue : -value) : value;
        string text = Units(abs);
        return negative ? "-" + text : text;
    }

    static string Units(long abs)
    {
        if (abs < Wan) return abs.ToString(CultureInfo.InvariantCulture);
        long size; string unit;
        if (abs >= Jing) { size = Jing; unit = "京"; }
        else if (abs >= Zhao) { size = Zhao; unit = "兆"; }
        else if (abs >= Yi) { size = Yi; unit = "亿"; }
        else { size = Wan; unit = "万"; }
        long chunk = abs / size, rest = abs % size;
        return rest == 0 ? chunk + unit : chunk + unit + " " + Units(rest);
    }
}
