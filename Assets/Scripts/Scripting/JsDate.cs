using System;
using System.Globalization;

namespace MapleDash.Js
{
    // 原脚本使用的 Date/getTime/setHours，时间值为 Unix 毫秒。
    public sealed class JsDate : JsValue
    {
        public override string TypeName => "object";
        public double Milliseconds { get; private set; }
        // 事件脚本大量用 Date.now() 算时间差（起源之塔的 3 分钟限时、补油波、落石预警、
        // 护送距离计时）。生产走真实 UTC；编辑器回归要「快进」这些差，所以时钟留成可注入的
        // （与 NativeEventScripts.Clock 同一范式，回归里用反射替换，见 SeedTowerChecks）。
        internal static Func<long> Clock = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        public JsDate(double milliseconds) { Milliseconds = milliseconds; }
        public static JsFunction Constructor()
        {
            var date = JsFunction.FromNative("Date", c => {
                if (c.ArgCount == 0) return new JsDate(Clock());
                if (c.Arg(0) is JsDate existing) return new JsDate(existing.Milliseconds);
                if (c.Arg(0) is JsString) throw new JsRuntimeException("Unmigrated Date string constructor");
                if (c.ArgCount > 1) throw new JsRuntimeException("Unmigrated Date component constructor");
                return new JsDate(JsConvert.ToNumber(c.Arg(0)));
            });
            date.Members.Set("now", JsFunction.FromNative("now", c => new JsNumber(Clock())));
            return date;
        }
        public JsValue Get(string key)
        {
            if (key == "getTime" || key == "valueOf") return JsFunction.FromNative(key, c => new JsNumber(Milliseconds));
            if (key == "getHours" || key == "getMinutes") return JsFunction.FromNative(key, c =>
            {
                if (double.IsNaN(Milliseconds) || double.IsInfinity(Milliseconds)) return new JsNumber(double.NaN);
                try
                {
                    var local=DateTimeOffset.FromUnixTimeMilliseconds((long)Milliseconds).LocalDateTime;
                    return new JsNumber(key=="getHours"?local.Hour:local.Minute);
                }
                catch(ArgumentOutOfRangeException) { return new JsNumber(double.NaN); }
            });
            if (key == "setHours") return JsFunction.FromNative(key, c =>
            {
                if (double.IsNaN(Milliseconds)) return new JsNumber(double.NaN);
                var local = DateTimeOffset.FromUnixTimeMilliseconds((long)Milliseconds).LocalDateTime;
                double hour = JsConvert.ToNumber(c.Arg(0));
                double minute = c.ArgCount > 1 ? JsConvert.ToNumber(c.Arg(1)) : local.Minute;
                double second = c.ArgCount > 2 ? JsConvert.ToNumber(c.Arg(2)) : local.Second;
                double ms = c.ArgCount > 3 ? JsConvert.ToNumber(c.Arg(3)) : local.Millisecond;
                if (double.IsNaN(hour + minute + second + ms) || double.IsInfinity(hour + minute + second + ms))
                    Milliseconds = double.NaN;
                else
                {
                    try
                    {
                        var result = local.Date.AddHours(Math.Truncate(hour)).AddMinutes(Math.Truncate(minute)).AddSeconds(Math.Truncate(second)).AddMilliseconds(Math.Truncate(ms));
                        Milliseconds = new DateTimeOffset(result).ToUnixTimeMilliseconds();
                    }
                    catch (ArgumentOutOfRangeException) { Milliseconds = double.NaN; }
                }
                return new JsNumber(Milliseconds);
            });
            return JsUndefined.Value;
        }
        public override string ToString() => double.IsNaN(Milliseconds) ? "Invalid Date" :
            DateTimeOffset.FromUnixTimeMilliseconds((long)Milliseconds).ToLocalTime().ToString("r", CultureInfo.InvariantCulture);
    }
}
