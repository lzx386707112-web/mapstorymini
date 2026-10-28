using System;
using System.Collections.Generic;
using System.Text;

namespace MapleDash.Js
{
    // 源端脚本实际用到的内置对象子集（ncps/quests 统计：length/push/join/substring/forEach/Math.floor/random…）。
    public static class JsBuiltins
    {
        public static void Install(JsInterpreter it, JsScope scope)
        {
            scope.Declare("undefined", JsUndefined.Value);
            scope.Declare("NaN", new JsNumber(double.NaN));
            scope.Declare("Infinity", new JsNumber(double.PositiveInfinity));

            scope.Declare("parseInt", JsFunction.FromNative("parseInt", c => new JsNumber(ParseInt(JsConvert.ToString(c.Arg(0)), c.ArgCount > 1 ? JsConvert.ToInt32(c.Arg(1)) : 0))));
            scope.Declare("parseFloat", JsFunction.FromNative("parseFloat", c => new JsNumber(ParseFloat(JsConvert.ToString(c.Arg(0))))));
            scope.Declare("isNaN", JsFunction.FromNative("isNaN", c => JsBoolean.Of(double.IsNaN(JsConvert.ToNumber(c.Arg(0))))));
            scope.Declare("isFinite", JsFunction.FromNative("isFinite", c =>
            {
                double d = JsConvert.ToNumber(c.Arg(0));
                return JsBoolean.Of(!double.IsNaN(d) && !double.IsInfinity(d));
            }));

            var numberFn = JsFunction.FromNative("Number", c => new JsNumber(JsConvert.ToNumber(c.Arg(0))));
            scope.Declare("Number", numberFn);

            var stringFn = JsFunction.FromNative("String", c => new JsString(JsConvert.ToString(c.Arg(0))));
            stringFn.Members.Set("fromCharCode", JsFunction.FromNative("fromCharCode", c =>
            {
                var sb = new StringBuilder();
                for (int i = 0; i < c.ArgCount; i++) sb.Append((char)JsConvert.ToInt32(c.Arg(i)));
                return new JsString(sb.ToString());
            }));
            scope.Declare("String", stringFn);

            var boolFn = JsFunction.FromNative("Boolean", c => JsBoolean.Of(JsConvert.ToBoolean(c.Arg(0))));
            scope.Declare("Boolean", boolFn);

            scope.Declare("Date", JsDate.Constructor());
            scope.Declare("Math", BuildMath(it));
            scope.Declare("JSON", BuildJson(it));
            var objectFn = JsFunction.FromNative("Object", c => c.ArgCount == 0 || c.Arg(0) is JsNull || c.Arg(0) is JsUndefined ? new JsObject() : c.Arg(0));
            foreach (var method in new[] { "keys", "values", "entries" })
                objectFn.Members.Set(method, JsFunction.FromNative(method, c =>
                {
                    var result = new JsArray();
                    foreach (var key in JsPatterns.Keys(c.Arg(0)))
                    {
                        var value = it.GetMember(c.Arg(0), key);
                        result.Items.Add(method == "keys" ? (JsValue)new JsString(key) : method == "values" ? value : new JsArray(new JsValue[] { new JsString(key), value }));
                    }
                    return result;
                }));
            scope.Declare("Object", objectFn);
            scope.Declare("Set", JsFunction.FromNative("Set", c =>
            {
                var values = new List<JsValue>();
                if (c.ArgCount > 0) foreach (var value in JsPatterns.Iterate(c.Arg(0)))
                    if (!values.Exists(v => JsConvert.StrictEquals(v, value))) values.Add(value);
                var set = new JsHostObject(values);
                set.Set("has", JsFunction.FromNative("has", a => JsBoolean.Of(values.Exists(v => JsConvert.StrictEquals(v, a.Arg(0))))));
                set.Set("add", JsFunction.FromNative("add", a => { if (!values.Exists(v => JsConvert.StrictEquals(v, a.Arg(0)))) values.Add(a.Arg(0)); return set; }));
                return set;
            }));

            // new Array(...) 的三种语义（ES5 §15.4.2）：
            //   无参       → 空数组
            //   单个非负整数 n → length=n 的稀疏数组（脚本里很常见：new Array(8) 再按下标填）
            //   其余       → 元素列表 new Array("武器","上衣", …)
            // 之前这里无条件返回空数组，BeiDouSpecial/时尚点装.js 的
            //   var options = new Array("武器", "上衣", …); for (i=0;i<options.length;i++)
            // 因此一个部位都渲染不出来，玩家看到的就是「装备列表是空的」。
            var arrayFn = JsFunction.FromNative("Array", c =>
            {
                if (c.ArgCount == 1 && c.Arg(0) is JsNumber length)
                {
                    double n = length.Value;
                    if (n >= 0 && n <= int.MaxValue && Math.Floor(n) == n)
                    {
                        var sized = new JsArray();
                        for (int i = 0; i < (int)n; i++) sized.Items.Add(JsUndefined.Value);
                        return sized;
                    }
                }
                return new JsArray(c.Args);
            });
            var arrayProto = new JsObject();
            foreach (var name in ArrayMethodNames) arrayProto.Set(name, ArrayMethod(it, name));
            arrayFn.Members.Set("prototype", arrayProto);
            arrayFn.Members.Set("isArray", JsFunction.FromNative("isArray", c => JsBoolean.Of(c.Arg(0) is JsArray)));
            scope.Declare("Array", arrayFn);

            var stringProto = new JsObject();
            foreach (var name in StringMethodNames) stringProto.Set(name, StringMethod(it, name));
            stringFn.Members.Set("prototype", stringProto);

            JsJavaInterop.Install(it, scope);

            // 少量脚本用 console.log 打调试信息；客户端没有控制台，全部吞掉。
            var console = new JsObject();
            foreach (var name in new[] { "log", "info", "warn", "error", "debug", "println", "print" })
                console.Set(name, JsFunction.FromNative(name, c => JsUndefined.Value));
            scope.Declare("console", console);
        }

        static JsObject BuildMath(JsInterpreter it)
        {
            var math = new JsObject();
            math.Set("PI", new JsNumber(Math.PI));
            math.Set("E", new JsNumber(Math.E));
            math.Set("floor", JsFunction.FromNative("floor", c => new JsNumber(Math.Floor(JsConvert.ToNumber(c.Arg(0))))));
            math.Set("ceil", JsFunction.FromNative("ceil", c => new JsNumber(Math.Ceiling(JsConvert.ToNumber(c.Arg(0))))));
            math.Set("round", JsFunction.FromNative("round", c =>
            {
                double d = JsConvert.ToNumber(c.Arg(0));
                if (double.IsNaN(d) || double.IsInfinity(d)) return new JsNumber(double.NaN);
                return new JsNumber(Math.Floor(d + 0.5));      // JS round 向 +∞ 取整，不是银行家舍入
            }));
            math.Set("abs", JsFunction.FromNative("abs", c => new JsNumber(Math.Abs(JsConvert.ToNumber(c.Arg(0))))));
            math.Set("trunc", JsFunction.FromNative("trunc", c => new JsNumber(Math.Truncate(JsConvert.ToNumber(c.Arg(0))))));
            math.Set("sqrt", JsFunction.FromNative("sqrt", c => new JsNumber(Math.Sqrt(JsConvert.ToNumber(c.Arg(0))))));
            math.Set("pow", JsFunction.FromNative("pow", c => new JsNumber(Math.Pow(JsConvert.ToNumber(c.Arg(0)), JsConvert.ToNumber(c.Arg(1))))));
            math.Set("sin", JsFunction.FromNative("sin", c => new JsNumber(Math.Sin(JsConvert.ToNumber(c.Arg(0))))));
            math.Set("cos", JsFunction.FromNative("cos", c => new JsNumber(Math.Cos(JsConvert.ToNumber(c.Arg(0))))));
            math.Set("tan", JsFunction.FromNative("tan", c => new JsNumber(Math.Tan(JsConvert.ToNumber(c.Arg(0))))));
            math.Set("log", JsFunction.FromNative("log", c => new JsNumber(Math.Log(JsConvert.ToNumber(c.Arg(0))))));
            math.Set("exp", JsFunction.FromNative("exp", c => new JsNumber(Math.Exp(JsConvert.ToNumber(c.Arg(0))))));
            math.Set("random", JsFunction.FromNative("random", c => new JsNumber(it.Random())));
            math.Set("max", JsFunction.FromNative("max", c =>
            {
                if (c.ArgCount == 0) return new JsNumber(double.NegativeInfinity);
                double m = double.NegativeInfinity;
                for (int i = 0; i < c.ArgCount; i++) { double d = JsConvert.ToNumber(c.Arg(i)); if (double.IsNaN(d)) return new JsNumber(double.NaN); if (d > m) m = d; }
                return new JsNumber(m);
            }));
            math.Set("min", JsFunction.FromNative("min", c =>
            {
                if (c.ArgCount == 0) return new JsNumber(double.PositiveInfinity);
                double m = double.PositiveInfinity;
                for (int i = 0; i < c.ArgCount; i++) { double d = JsConvert.ToNumber(c.Arg(i)); if (double.IsNaN(d)) return new JsNumber(double.NaN); if (d < m) m = d; }
                return new JsNumber(m);
            }));
            return math;
        }

        static JsObject BuildJson(JsInterpreter it)
        {
            var json = new JsObject();
            json.Set("stringify", JsFunction.FromNative("stringify", c => new JsString(Stringify(c.Arg(0)))));
            json.Set("parse", JsFunction.FromNative("parse", c => throw new JsRuntimeException("JSON.parse is not supported")));
            return json;
        }

        static string Stringify(JsValue v)
        {
            if (v == null || v is JsUndefined || v is JsNull) return "null";
            if (v is JsNumber n) return JsConvert.NumberToString(n.Value);
            if (v is JsBoolean b) return b.Value ? "true" : "false";
            if (v is JsString s) return "\"" + s.Value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "\"";
            if (v is JsArray a)
            {
                var parts = new List<string>();
                for (int i = 0; i < a.Items.Count; i++) parts.Add(Stringify(a.Items[i]));
                return "[" + string.Join(",", parts) + "]";
            }
            if (v is JsObject o)
            {
                var parts = new List<string>();
                foreach (var kv in o.Props) parts.Add(Stringify(new JsString(kv.Key)) + ":" + Stringify(kv.Value));
                return "{" + string.Join(",", parts) + "}";
            }
            return "null";
        }

        // ————— 字符串方法 —————
        static readonly string[] StringMethodNames =
        {
            "charAt", "charCodeAt", "indexOf", "lastIndexOf", "substring", "substr", "slice",
            "split", "replace", "toLowerCase", "toUpperCase", "trim", "concat", "toString",
            "startsWith", "endsWith", "includes", "repeat", "valueOf", "match", "padEnd", "padStart",
        };

        public static bool HasStringMethod(string name)
        {
            for (int i = 0; i < StringMethodNames.Length; i++) if (StringMethodNames[i] == name) return true;
            return false;
        }

        public static JsFunction StringMethod(JsInterpreter it, string name)
            => JsFunction.FromNative(name, c =>
            {
                string s = c.This is JsString js ? js.Value : JsConvert.ToString(c.This);
                switch (name)
                {
                    case "charAt":
                        {
                            int i = JsConvert.ToInt32(c.Arg(0));
                            return i >= 0 && i < s.Length ? new JsString(s[i].ToString()) : new JsString("");
                        }
                    case "charCodeAt":
                        {
                            int i = JsConvert.ToInt32(c.Arg(0));
                            return new JsNumber(i >= 0 && i < s.Length ? s[i] : double.NaN);
                        }
                    case "indexOf":
                        return new JsNumber(s.IndexOf(JsConvert.ToString(c.Arg(0)), ClampIndex(JsConvert.ToInt32(c.Arg(1)), 0, s.Length), StringComparison.Ordinal));
                    case "lastIndexOf":
                        return new JsNumber(s.LastIndexOf(JsConvert.ToString(c.Arg(0)), StringComparison.Ordinal));
                    case "substring":
                        {
                            int a = ClampIndex(JsConvert.ToInt32(c.Arg(0)), 0, s.Length);
                            int b = c.ArgCount > 1 && !(c.Arg(1) is JsUndefined) ? ClampIndex(JsConvert.ToInt32(c.Arg(1)), 0, s.Length) : s.Length;
                            if (a > b) { int t = a; a = b; b = t; }
                            return new JsString(s.Substring(a, b - a));
                        }
                    case "substr":
                        {
                            int start = JsConvert.ToInt32(c.Arg(0));
                            if (start < 0) start = Math.Max(0, s.Length + start);
                            if (start > s.Length) return new JsString("");
                            int len = c.ArgCount > 1 && !(c.Arg(1) is JsUndefined) ? JsConvert.ToInt32(c.Arg(1)) : s.Length - start;
                            if (len < 0) len = 0;
                            return new JsString(s.Substring(start, Math.Min(len, s.Length - start)));
                        }
                    case "slice":
                        {
                            int a = NormalizeSlice(JsConvert.ToInt32(c.Arg(0)), s.Length);
                            int b = c.ArgCount > 1 && !(c.Arg(1) is JsUndefined) ? NormalizeSlice(JsConvert.ToInt32(c.Arg(1)), s.Length) : s.Length;
                            if (b < a) b = a;
                            return new JsString(s.Substring(a, b - a));
                        }
                    case "split":
                        {
                            var sep = c.Arg(0);
                            var arr = new JsArray();
                            if (sep is JsUndefined) { arr.Items.Add(new JsString(s)); return arr; }
                            if (sep is JsRegExp splitRegex)
                            {
                                foreach (var part in splitRegex.Regex.Split(s)) arr.Items.Add(new JsString(part));
                                return arr;
                            }
                            string sp = JsConvert.ToString(sep);
                            if (sp.Length == 0)
                            {
                                for (int i = 0; i < s.Length; i++) arr.Items.Add(new JsString(s[i].ToString()));
                                return arr;
                            }
                            foreach (var part in s.Split(new[] { sp }, StringSplitOptions.None)) arr.Items.Add(new JsString(part));
                            return arr;
                        }
                    case "replace":
                        {
                            if (c.Arg(0) is JsRegExp replaceRegex)
                            {
                                string replacement = JsConvert.ToString(c.Arg(1));
                                return new JsString(replaceRegex.Regex.Replace(s, replacement, replaceRegex.Global ? int.MaxValue : 1));
                            }
                            string find = JsConvert.ToString(c.Arg(0));
                            string repl = JsConvert.ToString(c.Arg(1));
                            int idx = s.IndexOf(find, StringComparison.Ordinal);
                            return idx < 0 ? new JsString(s) : new JsString(s.Substring(0, idx) + repl + s.Substring(idx + find.Length));
                        }
                    case "match":
                        {
                            var regex = c.Arg(0) as JsRegExp ?? new JsRegExp(JsConvert.ToString(c.Arg(0)), "");
                            var result = new JsArray();
                            if (!regex.Global) return it.Call(regex.Get("exec"), regex, new JsValue[] { new JsString(s) });
                            foreach (System.Text.RegularExpressions.Match match in regex.Regex.Matches(s)) result.Items.Add(new JsString(match.Value));
                            return result.Items.Count == 0 ? (JsValue)JsNull.Value : result;
                        }
                    case "padEnd": case "padStart":
                        {
                            int target = JsConvert.ToInt32(c.Arg(0));
                            if (target > 100000) throw new JsRuntimeException("RangeError: padded string exceeds budget");
                            string fill = c.ArgCount < 2 ? " " : JsConvert.ToString(c.Arg(1));
                            if (fill.Length == 0 || target <= s.Length) return new JsString(s);
                            var padding = new StringBuilder();
                            while (padding.Length < target - s.Length) padding.Append(fill);
                            string pad = padding.ToString(0, target - s.Length);
                            return new JsString(name == "padEnd" ? s + pad : pad + s);
                        }
                    case "toLowerCase": return new JsString(s.ToLowerInvariant());
                    case "toUpperCase": return new JsString(s.ToUpperInvariant());
                    case "trim": return new JsString(s.Trim());
                    case "concat": return new JsString(s + JsConvert.ToString(c.Arg(0)));
                    case "toString":
                    case "valueOf": return new JsString(s);
                    case "startsWith": return JsBoolean.Of(s.StartsWith(JsConvert.ToString(c.Arg(0)), StringComparison.Ordinal));
                    case "endsWith": return JsBoolean.Of(s.EndsWith(JsConvert.ToString(c.Arg(0)), StringComparison.Ordinal));
                    case "includes": return JsBoolean.Of(s.IndexOf(JsConvert.ToString(c.Arg(0)), StringComparison.Ordinal) >= 0);
                    case "repeat":
                        {
                            int n = JsConvert.ToInt32(c.Arg(0));
                            if (n <= 0) return new JsString("");
                            var sb = new StringBuilder();
                            for (int i = 0; i < n && sb.Length < 100000; i++) sb.Append(s);
                            return new JsString(sb.ToString());
                        }
                }
                return JsUndefined.Value;
            });

        // ————— 数组方法 —————
        static readonly string[] ArrayMethodNames =
        {
            "push", "pop", "shift", "unshift", "splice", "slice", "join", "indexOf",
            "lastIndexOf", "reverse", "sort", "concat", "forEach", "map", "filter", "toString",
            // Java List/Set 风格：源端脚本对 cm 返回的集合直接调 size()/get()/isEmpty()
            "size", "get", "isEmpty", "contains", "add", "toArray", "reduce", "some", "every", "find", "findIndex", "includes",
        };

        public static bool HasArrayMethod(string name)
        {
            for (int i = 0; i < ArrayMethodNames.Length; i++) if (ArrayMethodNames[i] == name) return true;
            return false;
        }

        public static JsFunction ArrayMethod(JsInterpreter it, string name)
            => JsFunction.FromNative(name, c =>
            {
                var a = c.This as JsArray;
                if (a == null)
                {
                    if (name == "indexOf" || name == "lastIndexOf") return new JsNumber(-1);
                    return JsUndefined.Value;
                }
                switch (name)
                {
                    case "toArray":
                        return a;
                    case "size":
                        return new JsNumber(a.Items.Count);
                    case "isEmpty":
                        return JsBoolean.Of(a.Items.Count == 0);
                    case "get":
                        {
                            int gi = JsConvert.ToInt32(c.Arg(0));
                            return gi >= 0 && gi < a.Items.Count ? a.Items[gi] : JsNull.Value;
                        }
                    case "contains":
                        {
                            string cneedle = JsConvert.ToString(c.Arg(0));
                            for (int i = 0; i < a.Items.Count; i++)
                                if (JsConvert.ToString(a.Items[i]) == cneedle) return JsBoolean.Of(true);
                            return JsBoolean.Of(false);
                        }
                    case "add":
                        for (int i = 0; i < c.ArgCount; i++) a.Items.Add(c.Arg(i) ?? JsUndefined.Value);
                        return JsBoolean.Of(true);
                    case "push":
                        for (int i = 0; i < c.ArgCount; i++) a.Items.Add(c.Arg(i) ?? JsUndefined.Value);
                        return new JsNumber(a.Items.Count);
                    case "pop":
                        {
                            if (a.Items.Count == 0) return JsUndefined.Value;
                            var v = a.Items[a.Items.Count - 1];
                            a.Items.RemoveAt(a.Items.Count - 1);
                            return v;
                        }
                    case "shift":
                        {
                            if (a.Items.Count == 0) return JsUndefined.Value;
                            var v = a.Items[0];
                            a.Items.RemoveAt(0);
                            return v;
                        }
                    case "unshift":
                        for (int i = c.ArgCount - 1; i >= 0; i--) a.Items.Insert(0, c.Arg(i));
                        return new JsNumber(a.Items.Count);
                    case "splice":
                        {
                            int start = NormalizeSlice(JsConvert.ToInt32(c.Arg(0)), a.Items.Count);
                            int del = c.ArgCount > 1 ? Math.Max(0, Math.Min(JsConvert.ToInt32(c.Arg(1)), a.Items.Count - start)) : a.Items.Count - start;
                            var removed = new JsArray();
                            for (int i = 0; i < del; i++) removed.Items.Add(a.Items[start + i]);
                            a.Items.RemoveRange(start, del);
                            for (int i = c.ArgCount - 1; i >= 2; i--) a.Items.Insert(start, c.Arg(i));
                            return removed;
                        }
                    case "slice":
                        {
                            int start = NormalizeSlice(JsConvert.ToInt32(c.Arg(0)), a.Items.Count);
                            int end = c.ArgCount > 1 && !(c.Arg(1) is JsUndefined) ? NormalizeSlice(JsConvert.ToInt32(c.Arg(1)), a.Items.Count) : a.Items.Count;
                            var res = new JsArray();
                            for (int i = start; i < end && i < a.Items.Count; i++) res.Items.Add(a.Items[i]);
                            return res;
                        }
                    case "join":
                        {
                            string sep = c.ArgCount > 0 && !(c.Arg(0) is JsUndefined) ? JsConvert.ToString(c.Arg(0)) : ",";
                            var parts = new List<string>();
                            for (int i = 0; i < a.Items.Count; i++)
                            {
                                var v = a.Items[i];
                                parts.Add(v == null || v is JsUndefined || v is JsNull ? "" : JsConvert.ToString(v));
                            }
                            return new JsString(string.Join(sep, parts));
                        }
                    case "indexOf":
                        {
                            string needle = JsConvert.ToString(c.Arg(0));
                            for (int i = 0; i < a.Items.Count; i++) if (JsConvert.ToString(a.Items[i]) == needle) return new JsNumber(i);
                            return new JsNumber(-1);
                        }
                    case "lastIndexOf":
                        {
                            string needle = JsConvert.ToString(c.Arg(0));
                            for (int i = a.Items.Count - 1; i >= 0; i--) if (JsConvert.ToString(a.Items[i]) == needle) return new JsNumber(i);
                            return new JsNumber(-1);
                        }
                    case "reverse":
                        a.Items.Reverse();
                        return a;
                    case "sort":
                        {
                            var cmp = c.Arg(0) as JsFunction;
                            var items = a.Items;
                            items.Sort((x, y) =>
                            {
                                if (cmp != null)
                                {
                                    double d = JsConvert.ToNumber(it.Call(cmp, JsUndefined.Value, new[] { x, y }));
                                    return d < 0 ? -1 : d > 0 ? 1 : 0;
                                }
                                return string.CompareOrdinal(JsConvert.ToString(x), JsConvert.ToString(y));
                            });
                            return a;
                        }
                    case "concat":
                        {
                            var res = new JsArray(a.Items);
                            for (int i = 0; i < c.ArgCount; i++)
                            {
                                var v = c.Arg(i);
                                if (v is JsArray other) res.Items.AddRange(other.Items);
                                else res.Items.Add(v);
                            }
                            return res;
                        }
                    case "reduce":
                        {
                            int index = 0;
                            JsValue accumulator;
                            if (c.ArgCount > 1) accumulator = c.Arg(1);
                            else if (a.Items.Count > 0) accumulator = a.Items[index++];
                            else throw new JsRuntimeException("TypeError: reduce of empty array with no initial value");
                            for (; index < a.Items.Count; index++) accumulator = it.Call(c.Arg(0), JsUndefined.Value, new JsValue[] { accumulator, a.Items[index], new JsNumber(index), a });
                            return accumulator;
                        }
                    case "some": case "every": case "find": case "findIndex":
                        {
                            for (int index = 0; index < a.Items.Count; index++)
                            {
                                bool match = JsConvert.ToBoolean(it.Call(c.Arg(0), c.Arg(1), new JsValue[] { a.Items[index], new JsNumber(index), a }));
                                if (match && name == "find") return a.Items[index];
                                if (match && name == "findIndex") return new JsNumber(index);
                                if (match && name == "some") return JsBoolean.True;
                                if (!match && name == "every") return JsBoolean.False;
                            }
                            if (name == "find") return JsUndefined.Value;
                            if (name == "findIndex") return new JsNumber(-1);
                            return JsBoolean.Of(name == "every");
                        }
                    case "includes": return JsBoolean.Of(a.Items.Exists(v => JsConvert.StrictEquals(v, c.Arg(0))));
                    case "forEach":
                        {
                            var fn = c.Arg(0) as JsFunction;
                            if (fn == null) throw new JsRuntimeException("TypeError: forEach callback is not a function");
                            for (int i = 0; i < a.Items.Count; i++) it.Call(fn, JsUndefined.Value, new[] { a.Items[i], new JsNumber(i), a });
                            return JsUndefined.Value;
                        }
                    case "map":
                        {
                            var fn = c.Arg(0) as JsFunction;
                            var res = new JsArray();
                            for (int i = 0; i < a.Items.Count; i++) res.Items.Add(it.Call(fn, JsUndefined.Value, new[] { a.Items[i], new JsNumber(i), a }));
                            return res;
                        }
                    case "filter":
                        {
                            var fn = c.Arg(0) as JsFunction;
                            var res = new JsArray();
                            for (int i = 0; i < a.Items.Count; i++)
                                if (JsConvert.ToBoolean(it.Call(fn, JsUndefined.Value, new[] { a.Items[i], new JsNumber(i), a }))) res.Items.Add(a.Items[i]);
                            return res;
                        }
                    case "toString": return new JsString(JsConvert.ToString(a));
                }
                return JsUndefined.Value;
            });

        // ————— 数字方法 —————
        static readonly string[] NumberMethodNames = { "toString", "toFixed", "valueOf" };
        public static bool HasNumberMethod(string name)
        {
            for (int i = 0; i < NumberMethodNames.Length; i++) if (NumberMethodNames[i] == name) return true;
            return false;
        }

        public static JsFunction NumberMethod(JsInterpreter it, string name)
            => JsFunction.FromNative(name, c =>
            {
                double d = JsConvert.ToNumber(c.This);
                switch (name)
                {
                    case "toString": return new JsString(JsConvert.NumberToString(d));
                    case "toFixed":
                        {
                            int digits = c.ArgCount > 0 ? JsConvert.ToInt32(c.Arg(0)) : 0;
                            return new JsString(d.ToString("F" + Math.Max(0, Math.Min(20, digits)), System.Globalization.CultureInfo.InvariantCulture));
                        }
                    case "valueOf": return new JsNumber(d);
                }
                return JsUndefined.Value;
            });

        // ————— 数值解析 —————
        public static double ParseInt(string s, int radix)
        {
            if (string.IsNullOrEmpty(s)) return double.NaN;
            s = s.TrimStart();
            int sign = 1, i = 0;
            if (s.Length > 0 && (s[0] == '+' || s[0] == '-')) { if (s[0] == '-') sign = -1; i = 1; }
            if (radix == 0)
            {
                radix = 10;
                if (i + 1 < s.Length && s[i] == '0' && (s[i + 1] == 'x' || s[i + 1] == 'X')) { radix = 16; i += 2; }
            }
            else if (radix == 16 && i + 1 < s.Length && s[i] == '0' && (s[i + 1] == 'x' || s[i + 1] == 'X')) i += 2;
            if (radix < 2 || radix > 36) return double.NaN;

            double value = 0;
            bool any = false;
            for (; i < s.Length; i++)
            {
                int digit = DigitValue(s[i]);
                if (digit < 0 || digit >= radix) break;
                value = value * radix + digit;
                any = true;
            }
            return any ? sign * value : double.NaN;
        }

        static int DigitValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'z') return c - 'a' + 10;
            if (c >= 'A' && c <= 'Z') return c - 'A' + 10;
            return -1;
        }

        public static double ParseFloat(string s)
        {
            if (string.IsNullOrEmpty(s)) return double.NaN;
            s = s.TrimStart();
            int end = 0;
            while (end < s.Length && "0123456789+-.eE".IndexOf(s[end]) >= 0) end++;
            while (end > 0)
            {
                if (double.TryParse(s.Substring(0, end), System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
                end--;
            }
            return double.NaN;
        }

        static int ClampIndex(int i, int min, int max) => i < min ? min : i > max ? max : i;

        static int NormalizeSlice(int i, int len)
        {
            if (i < 0) return Math.Max(0, len + i);
            return Math.Min(i, len);
        }
    }
}
