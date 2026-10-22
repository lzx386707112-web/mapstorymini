using System;
using System.Collections.Generic;

// 值模型：只保留源端脚本真正需要的 ECMAScript 子集。
// 设计约束：纯 C#、不引用 UnityEngine —— 这样 dotnet 测试工程可以原样编译本目录，
// IL2CPP 下也没有 JIT/反射依赖（踩过 Encoding.GetEncoding(1252) 的坑，这里不再引入运行时动态代码）。
namespace MapleDash.Js
{
    public abstract class JsValue
    {
        public virtual string TypeName
        {
            get
            {
                if (this is JsUndefined) return "undefined";
                if (this is JsNull) return "null";
                if (this is JsNumber) return "number";
                if (this is JsString) return "string";
                if (this is JsBoolean) return "boolean";
                if (this is JsFunction) return "function";
                return "object";
            }
        }
    }

    public sealed class JsUndefined : JsValue
    {
        public static readonly JsUndefined Value = new JsUndefined();
        JsUndefined() { }
        public override string ToString() => "undefined";
    }

    public sealed class JsNull : JsValue
    {
        public static readonly JsNull Value = new JsNull();
        JsNull() { }
        public override string ToString() => "null";
    }

    public sealed class JsNumber : JsValue
    {
        public readonly double Value;
        public JsNumber(double v) { Value = v; }
        public override string ToString() => JsConvert.NumberToString(Value);
    }

    public sealed class JsString : JsValue
    {
        public readonly string Value;
        public JsString(string v) { Value = v ?? ""; }
        public override string ToString() => Value;
    }

    public sealed class JsBoolean : JsValue
    {
        public static readonly JsBoolean True = new JsBoolean(true);
        public static readonly JsBoolean False = new JsBoolean(false);
        public readonly bool Value;
        JsBoolean(bool v) { Value = v; }
        public static JsBoolean Of(bool v) => v ? True : False;
        public override string ToString() => Value ? "true" : "false";
    }

    // 普通对象：属性用有序字典，保证 for-in / 调试日志稳定。
    public sealed class JsObject : JsValue
    {
        public readonly Dictionary<string, JsValue> Props = new Dictionary<string, JsValue>(StringComparer.Ordinal);
        public JsObject() { }
        public JsObject(IDictionary<string, JsValue> init)
        {
            if (init == null) return;
            foreach (var kv in init) Props[kv.Key] = kv.Value ?? JsNull.Value;
        }
        public JsValue Get(string key) => Props.TryGetValue(key, out var v) ? v : JsUndefined.Value;
        public void Set(string key, JsValue v) => Props[key] = v ?? JsNull.Value;
        public bool Has(string key) => Props.ContainsKey(key);
        public void Remove(string key) => Props.Remove(key);
        public override string ToString() => "[object Object]";
    }

    // 数组：index 属性直接落到 Items，其余属性（如自定义字段）留在 Props。
    public sealed class JsArray : JsValue
    {
        public readonly List<JsValue> Items = new List<JsValue>();
        public readonly Dictionary<string, JsValue> Props = new Dictionary<string, JsValue>(StringComparer.Ordinal);

        public JsArray() { }
        public JsArray(IEnumerable<JsValue> items) { if (items != null) Items.AddRange(items); }

        public int Length => Items.Count;

        public JsValue GetIndex(int i) => i >= 0 && i < Items.Count ? Items[i] : JsUndefined.Value;

        public void SetIndex(int i, JsValue v)
        {
            v = v ?? JsNull.Value;
            if (i < 0) return;
            while (Items.Count <= i) Items.Add(JsUndefined.Value);
            Items[i] = v;
        }

        public JsValue Get(string key)
        {
            if (key == "length") return new JsNumber(Items.Count);
            if (int.TryParse(key, out var i)) return GetIndex(i);
            return Props.TryGetValue(key, out var v) ? v : JsUndefined.Value;
        }

        public void Set(string key, JsValue v)
        {
            if (key == "length")
            {
                int n = (int)JsConvert.ToNumber(v);
                if (n < 0) n = 0;
                while (Items.Count < n) Items.Add(JsUndefined.Value);
                while (Items.Count > n) Items.RemoveAt(Items.Count - 1);
                return;
            }
            if (int.TryParse(key, out var i)) { SetIndex(i, v); return; }
            Props[key] = v ?? JsNull.Value;
        }

        public override string ToString() => "[object Array]";
    }

    public sealed class JsFunction : JsValue
    {
        public string Name = "";
        public string[] Parameters = Array.Empty<string>();
        public JsBlock Body;                   // 函数体块；原生函数为 null
        public JsScope Closure;
        public Func<JsCall, JsValue> Native;
        public readonly JsObject Members = new JsObject();   // String.fromCharCode / Array.prototype / fn.call
        public bool IsNative => Native != null;

        public static JsFunction FromNative(string name, Func<JsCall, JsValue> fn)
            => new JsFunction { Name = name ?? "", Native = fn };

        public override string ToString() => "function " + Name + "() { [native code] }";
    }

    // 宿主对象（客户端 cm/player 等）：属性可读写，方法用 JsFunction 挂在属性上。
    public sealed class JsHostObject : JsValue
    {
        public readonly object Host;
        public readonly JsObject Members = new JsObject();
        public Func<string, JsValue> Missing;      // 仅测试替身使用：未知成员按需生成
        public JsHostObject(object host) { Host = host; }
        public JsValue Get(string key)
        {
            if (Members.Has(key)) return Members.Get(key);
            return Missing != null ? Missing(key) : JsUndefined.Value;
        }
        public void Set(string key, JsValue v) => Members.Set(key, v);
        public override string ToString() => "[host " + (Host == null ? "null" : Host.GetType().Name) + "]";
    }

    public struct JsCall
    {
        public JsValue This;
        public JsValue[] Args;
        public JsInterpreter Interp;

        public JsValue Arg(int i) => Args != null && i < Args.Length ? Args[i] : JsUndefined.Value;
        public int ArgCount => Args == null ? 0 : Args.Length;
    }

    public sealed class JsRuntimeException : Exception
    {
        public readonly JsValue Value;
        public JsRuntimeException(string message, JsValue value = null) : base(message)
        {
            Value = value ?? new JsString(message);
        }
    }

    public sealed class JsReturnSignal : Exception
    {
        public readonly JsValue Value;
        public JsReturnSignal(JsValue v) { Value = v; }
    }

    public sealed class JsBreakSignal : Exception { }
    public sealed class JsContinueSignal : Exception { }

    // JS 类型转换规则（ECMA-262 相关章节的实用子集）
    public static class JsConvert
    {
        public static bool ToBoolean(JsValue v)
        {
            if (v == null || v is JsUndefined || v is JsNull) return false;
            if (v is JsBoolean b) return b.Value;
            if (v is JsNumber n) return n.Value != 0 && !double.IsNaN(n.Value);
            if (v is JsString s) return s.Value.Length > 0;
            return true;
        }

        public static double ToNumber(JsValue v)
        {
            if (v == null || v is JsUndefined) return double.NaN;
            if (v is JsNull) return 0;
            if (v is JsNumber n) return n.Value;
            if (v is JsBoolean b) return b.Value ? 1 : 0;
            if (v is JsString s) return StringToNumber(s.Value);
            return double.NaN;
        }

        public static double StringToNumber(string s)
        {
            if (s == null) return 0;
            s = s.Trim();
            if (s.Length == 0) return 0;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(s.Substring(2), System.Globalization.NumberStyles.HexNumber,
                              System.Globalization.CultureInfo.InvariantCulture, out var hex))
                return hex;
            if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
            if (s == "Infinity" || s == "+Infinity") return double.PositiveInfinity;
            if (s == "-Infinity") return double.NegativeInfinity;
            return double.NaN;
        }

        public static string ToString(JsValue v)
        {
            if (v == null || v is JsUndefined) return "undefined";
            if (v is JsNull) return "null";
            if (v is JsNumber n) return NumberToString(n.Value);
            if (v is JsString s) return s.Value;
            if (v is JsBoolean b) return b.Value ? "true" : "false";
            if (v is JsArray a)
            {
                var parts = new List<string>(a.Items.Count);
                for (int i = 0; i < a.Items.Count; i++)
                {
                    var item = a.Items[i];
                    parts.Add(item == null || item is JsUndefined || item is JsNull ? "" : ToString(item));
                }
                return string.Join(",", parts);
            }
            if (v is JsFunction f) return f.ToString();
            return v.ToString();
        }

        public static string NumberToString(double d)
        {
            if (double.IsNaN(d)) return "NaN";
            if (double.IsPositiveInfinity(d)) return "Infinity";
            if (double.IsNegativeInfinity(d)) return "-Infinity";
            if (d == 0) return "0";
            if (d == Math.Floor(d) && Math.Abs(d) < 1e21)
            {
                // JS 整数不带小数点；long 覆盖常见任务数值（经验/金币）。
                if (Math.Abs(d) <= 9.007199254740992E15) return ((long)d).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static int ToInt32(JsValue v)
        {
            double d = ToNumber(v);
            if (double.IsNaN(d) || double.IsInfinity(d)) return 0;
            d = Math.Truncate(d) % 4294967296.0;
            if (d < 0) d += 4294967296.0;
            return unchecked((int)(uint)d);
        }

        // JS 宽松相等的实用实现
        public static bool LooseEquals(JsValue a, JsValue b)
        {
            if (a == null) a = JsUndefined.Value;
            if (b == null) b = JsUndefined.Value;
            if (SameType(a, b)) return StrictEquals(a, b);
            bool aNullish = a is JsUndefined || a is JsNull;
            bool bNullish = b is JsUndefined || b is JsNull;
            if (aNullish || bNullish) return aNullish && bNullish;
            if (a is JsBoolean) return LooseEquals(new JsNumber(ToNumber(a)), b);
            if (b is JsBoolean) return LooseEquals(a, new JsNumber(ToNumber(b)));
            if (a is JsNumber && b is JsString) return ToNumber(a) == ToNumber(b);
            if (a is JsString && b is JsNumber) return ToNumber(a) == ToNumber(b);
            return StrictEquals(a, b);
        }

        public static bool StrictEquals(JsValue a, JsValue b)
        {
            if (a == null) a = JsUndefined.Value;
            if (b == null) b = JsUndefined.Value;
            if (a is JsNumber na && b is JsNumber nb) return na.Value == nb.Value;
            if (a is JsString sa && b is JsString sb) return sa.Value == sb.Value;
            if (a is JsBoolean ba && b is JsBoolean bb) return ba.Value == bb.Value;
            return ReferenceEquals(a, b);
        }

        static bool SameType(JsValue a, JsValue b)
        {
            if (a is JsNumber && b is JsNumber) return true;
            if (a is JsString && b is JsString) return true;
            if (a is JsBoolean && b is JsBoolean) return true;
            if ((a is JsUndefined) && (b is JsUndefined)) return true;
            if ((a is JsNull) && (b is JsNull)) return true;
            return false;
        }
    }
}
