using System;
using System.Collections.Generic;
using System.Globalization;

namespace MapleDash.Js
{
    // 显式 Java 标准库兼容，不需要 JVM、JIT、反射或修改原脚本。
    // 游戏类交给本地服务端注册；未迁移的类明确报错，禁止返回假对象。
    public static class JsJavaInterop
    {
        static JsFunction Fn(string name, Func<JsCall, JsValue> body) => JsFunction.FromNative(name, body);
        static JsNumber N(double value) => new JsNumber(value);
        static string Text(JsCall c, int n = 0) => JsConvert.ToString(c.Arg(n));
        static int Int(JsCall c, int n = 0) => JsConvert.ToInt32(c.Arg(n));

        public static void Install(JsInterpreter it, JsScope scope)
        {
            var java = new JsObject();
            java.Set("type", Fn("type", c => Resolve(it, Text(c))));
            java.Set("from", Fn("from", c => new JsArray(JsPatterns.Iterate(c.Arg(0)))));
            java.Set("to", Fn("to", c => c.Arg(0)));
            scope.Declare("Java", java);
            foreach (string root in new[] { "java", "javax", "org", "net", "com", "Packages" })
                scope.Declare(root, Package(it, root == "Packages" ? "" : root));
            scope.Declare("importClass", Fn("importClass", c =>
            {
                foreach (var argument in c.Args)
                {
                    string name = JsConvert.ToString(it.GetMember(argument, "__javaClassName"));
                    if (name == "undefined") throw new JsRuntimeException("Invalid importClass argument");
                    scope.Declare(name.Substring(name.LastIndexOf('.') + 1), Resolve(it, name));
                }
                return JsUndefined.Value;
            }));
            var imports = new List<string>();
            scope.ResolveImport = name =>
            {
                if (name.Length == 0 || !char.IsUpper(name[0])) return null;
                JsValue found = null;
                foreach (string package in imports)
                {
                    string fullName = package + "." + name;
                    JsValue value;
                    try { value = Resolve(it, fullName); }
                    catch (JsRuntimeException e) when (e.Message == "Java interop: class not migrated: " + fullName) { continue; }
                    if (found != null && !ReferenceEquals(found, value)) throw new JsRuntimeException("Ambiguous Java import: " + name);
                    found = value;
                }
                return found;
            };
            scope.Declare("importPackage", Fn("importPackage", c =>
            {
                foreach (var argument in c.Args)
                {
                    if (!(argument is JsHostObject package) || !(package.Host is string name))
                        throw new JsRuntimeException("Invalid importPackage argument");
                    if (!imports.Contains(name)) imports.Add(name);
                }
                return JsUndefined.Value;
            }));
        }

        static JsHostObject Package(JsInterpreter it, string name)
        {
            var package = new JsHostObject(name);
            package.Missing = key =>
            {
                string child = name.Length == 0 ? key : name + "." + key;
                if (key.Length > 0 && char.IsUpper(key[0])) return Resolve(it, child);
                return Package(it, child);
            };
            return package;
        }

        public static JsValue Resolve(JsInterpreter it, string name)
        {
            if (it.JavaTypes.TryGetValue(name, out var cached)) return cached;
            var value = ResolveType(it, name);
            if (value is JsFunction function) function.Members.Set("__javaClassName", new JsString(name));
            else if (value is JsHostObject host) host.Set("__javaClassName", new JsString(name));
            else if (value is JsObject obj) obj.Set("__javaClassName", new JsString(name));
            it.JavaTypes[name] = value;
            return value;
        }

        static JsValue ResolveType(JsInterpreter it, string name)
        {
            switch (name)
            {
                case "java.awt.Point": return Fn("Point", c => Point(c));
                case "java.awt.Rectangle": return Fn("Rectangle", c => Rectangle(c));
                case "java.util.ArrayList": case "java.util.LinkedList":
                    return Fn("ArrayList", c => c.Arg(0) is JsArray a ? new JsArray(a.Items) : new JsArray());
                case "java.util.HashMap": case "java.util.LinkedHashMap": case "java.util.TreeMap": return Fn("HashMap", c => Map());
                case "java.lang.Integer": case "java.lang.Long": case "java.lang.Short": case "java.lang.Byte":
                    var number = Fn("Number", c => N(JsConvert.ToNumber(c.Arg(0))));
                    number.Members.Set("MAX_VALUE", N(name.EndsWith("Integer", StringComparison.Ordinal) ? int.MaxValue : name.EndsWith("Short", StringComparison.Ordinal) ? short.MaxValue : name.EndsWith("Byte", StringComparison.Ordinal) ? sbyte.MaxValue : long.MaxValue));
                    number.Members.Set("parseInt", Fn("parseInt", c => N(JsBuiltins.ParseInt(Text(c), c.ArgCount > 1 ? Int(c, 1) : 10))));
                    number.Members.Set("valueOf", Fn("valueOf", c => N(JsConvert.ToNumber(c.Arg(0)))));
                    return number;
                case "java.lang.String": return Fn("String", c => new JsString(Text(c)));
                case "java.lang.System":
                    var system = new JsObject();
                    system.Set("currentTimeMillis", Fn("currentTimeMillis", c => N(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())));
                    foreach (string stream in new[] { "out", "err" })
                    {
                        var writer = stream == "out" ? Console.Out : Console.Error;
                        var output = new JsObject();
                        output.Set("println", Fn("println", c => { writer.WriteLine(c.ArgCount == 0 ? "" : Text(c)); return JsUndefined.Value; }));
                        output.Set("print", Fn("print", c => { writer.Write(c.ArgCount == 0 ? "" : Text(c)); return JsUndefined.Value; }));
                        system.Set(stream, output);
                    }
                    return system;
                case "java.util.Date": return JsDate.Constructor();
                case "java.util.Calendar": return Calendar();
                case "java.text.SimpleDateFormat":
                    return Fn("SimpleDateFormat", c =>
                    {
                        // Source 9390220 uses yyyyMMdd. Reject patterns whose Java semantics are not migrated.
                        string pattern = Text(c);
                        if (pattern != "yyyyMMdd") throw new JsRuntimeException("Unmigrated SimpleDateFormat pattern: " + pattern);
                        var format = new JsObject();
                        format.Set("format", Fn("format", a =>
                        {
                            if (!(a.Arg(0) is JsDate date)) throw new JsRuntimeException("SimpleDateFormat.format requires a Date");
                            return new JsString(DateTimeOffset.FromUnixTimeMilliseconds((long)date.Milliseconds).ToLocalTime().ToString(pattern, CultureInfo.InvariantCulture));
                        }));
                        return format;
                    });
                case "java.util.Arrays":
                    var arrays = new JsObject();
                    arrays.Set("asList", Fn("asList", c => c.ArgCount == 1 && c.Arg(0) is JsArray a ? new JsArray(a.Items) : new JsArray(c.Args)));
                    return arrays;
            }
            var value = it.ResolveHostType?.Invoke(name);
            if (value != null && !(value is JsNull) && !(value is JsUndefined)) return value;
            throw new JsRuntimeException("Java interop: class not migrated: " + name);
        }

        static JsObject Calendar()
        {
            var type = new JsObject();
            foreach (var pair in new Dictionary<string, int> { ["YEAR"] = 1, ["MONTH"] = 2, ["DATE"] = 5, ["DAY_OF_MONTH"] = 5,
                ["DAY_OF_WEEK"] = 7, ["HOUR_OF_DAY"] = 11, ["MINUTE"] = 12, ["SECOND"] = 13, ["MILLISECOND"] = 14,
                ["SUNDAY"] = 1, ["MONDAY"] = 2, ["TUESDAY"] = 3, ["WEDNESDAY"] = 4, ["THURSDAY"] = 5, ["FRIDAY"] = 6, ["SATURDAY"] = 7 })
                type.Set(pair.Key, N(pair.Value));
            type.Set("getInstance", Fn("getInstance", c =>
            {
                var now = DateTimeOffset.Now;
                var calendar = new JsObject();
                calendar.Set("get", Fn("get", a =>
                {
                    switch (Int(a))
                    {
                        case 1: return N(now.Year); case 2: return N(now.Month - 1); case 5: return N(now.Day);
                        case 7: return N((int)now.DayOfWeek + 1); case 11: return N(now.Hour);
                        case 12: return N(now.Minute); case 13: return N(now.Second); case 14: return N(now.Millisecond);
                        default: throw new JsRuntimeException("Unmigrated Calendar field: " + Int(a));
                    }
                }));
                calendar.Set("getTime", Fn("getTime", a => new JsDate(now.ToUnixTimeMilliseconds())));
                calendar.Set("getTimeInMillis", Fn("getTimeInMillis", a => N(now.ToUnixTimeMilliseconds())));
                calendar.Set("setTime", Fn("setTime", a =>
                {
                    if (!(a.Arg(0) is JsDate date)) throw new JsRuntimeException("Calendar.setTime requires a Date");
                    now = DateTimeOffset.FromUnixTimeMilliseconds((long)date.Milliseconds).ToLocalTime();
                    return JsUndefined.Value;
                }));
                return calendar;
            }));
            return type;
        }

        static JsHostObject Point(JsCall c)
        {
            var point = new JsHostObject(null);
            point.Set("x", N(Int(c))); point.Set("y", N(Int(c, 1)));
            point.Set("getX", Fn("getX", a => point.Get("x")));
            point.Set("getY", Fn("getY", a => point.Get("y")));
            point.Set("setLocation", Fn("setLocation", a => { point.Set("x", a.Arg(0)); point.Set("y", a.Arg(1)); return JsUndefined.Value; }));
            return point;
        }
        static JsHostObject Rectangle(JsCall c)
        {
            var rectangle = Point(c);
            rectangle.Set("width", N(Int(c, 2))); rectangle.Set("height", N(Int(c, 3)));
            rectangle.Set("contains", Fn("contains", a =>
            {
                double x = a.Arg(0) is JsHostObject p ? JsConvert.ToNumber(p.Get("x")) : JsConvert.ToNumber(a.Arg(0));
                double y = a.Arg(0) is JsHostObject p2 ? JsConvert.ToNumber(p2.Get("y")) : JsConvert.ToNumber(a.Arg(1));
                double rx = JsConvert.ToNumber(rectangle.Get("x")), ry = JsConvert.ToNumber(rectangle.Get("y"));
                return JsBoolean.Of(x >= rx && y >= ry && x < rx + JsConvert.ToNumber(rectangle.Get("width")) && y < ry + JsConvert.ToNumber(rectangle.Get("height")));
            }));
            return rectangle;
        }
        static JsHostObject Map()
        {
            // Java 键按 JS primitive identity 对齐，不能用 ToString 把 1 和 "1" 合并。
            var keys = new List<JsValue>(); var values = new List<JsValue>();
            var map = new JsHostObject(null);
            Func<JsValue, int> find = key => keys.FindIndex(k => JsConvert.StrictEquals(k, key));
            map.Set("get", Fn("get", c => { int index = find(c.Arg(0)); return index < 0 ? (JsValue)JsNull.Value : values[index]; }));
            map.Set("put", Fn("put", c => { int index = find(c.Arg(0)); var old = index < 0 ? (JsValue)JsNull.Value : values[index]; if (index < 0) { keys.Add(c.Arg(0)); values.Add(c.Arg(1)); } else values[index] = c.Arg(1); return old; }));
            map.Set("containsKey", Fn("containsKey", c => JsBoolean.Of(find(c.Arg(0)) >= 0)));
            map.Set("remove", Fn("remove", c => { int index = find(c.Arg(0)); if (index < 0) return JsNull.Value; var old = values[index]; keys.RemoveAt(index); values.RemoveAt(index); return old; }));
            map.Set("size", Fn("size", c => N(keys.Count)));
            map.Set("isEmpty", Fn("isEmpty", c => JsBoolean.Of(keys.Count == 0)));
            map.Set("keySet", Fn("keySet", c => new JsArray(keys)));
            map.Set("values", Fn("values", c => new JsArray(values)));
            map.Set("clear", Fn("clear", c => { keys.Clear(); values.Clear(); return JsUndefined.Value; }));
            return map;
        }
    }
}
