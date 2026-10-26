using System;
using System.Collections.Generic;

namespace MapleDash.Js
{
    public sealed class JsInterpreter
    {
        public JsScope Global { get; }

        long steps;
        public long MaxSteps = 4000000;
        int depth;
        public int MaxDepth = 96;

        readonly Random rng = new Random();
        public Action<string> Trace;
        public Func<string, JsValue> ResolveHostType;

        public JsInterpreter()
        {
            Global = new JsScope(null, true);
            JsBuiltins.Install(this, Global);
        }

        public void Step()
        {
            if (++steps <= MaxSteps) return;
            throw new JsRuntimeException("Script exceeded execution budget (possible infinite loop)");
        }

        public void ResetSteps() => steps = 0;

        public double Random() => rng.NextDouble();

        // ————— 提升：函数声明/var 必须在执行前就位（源端脚本大量依赖这个行为） —————
        public void Hoist(List<JsNode> body, JsScope scope)
        {
            for (int i = 0; i < body.Count; i++) HoistNode(body[i], scope);
        }

        void HoistNode(JsNode n, JsScope scope)
        {
            if (n == null) return;
            if (n is JsFunctionDecl f)
            {
                scope.DeclareVar(f.Name, new JsFunction
                {
                    Name = f.Name, Parameters = f.Parameters, Patterns = f.Patterns, Body = f.Body, Closure = scope,
                });
                return;
            }
            if (n is JsVarDecl v)
            {
                for (int i = 0; i < v.Decls.Count; i++)
                    if (v.Decls[i].Pattern != null) JsPatterns.Hoist(v.Decls[i].Pattern, scope);
                    else if (!scope.FunctionScope.HasOwn(v.Decls[i].Name))
                        scope.DeclareVar(v.Decls[i].Name, JsUndefined.Value);
                return;
            }
            if (n is JsLabel label) { HoistNode(label.Body, scope); return; }
            if (n is JsBlock b) { Hoist(b.Body, scope); return; }
            if (n is JsIf f2) { HoistNode(f2.Then, scope); HoistNode(f2.Else, scope); return; }
            if (n is JsFor f3) { HoistNode(f3.Init, scope); HoistNode(f3.Body, scope); return; }
            if (n is JsForIn f4)
            {
                if (f4.Pattern != null) JsPatterns.Hoist(f4.Pattern, scope);
                if (!string.IsNullOrEmpty(f4.VarName) && !scope.FunctionScope.HasOwn(f4.VarName))
                    scope.DeclareVar(f4.VarName, JsUndefined.Value);
                HoistNode(f4.Body, scope);
                return;
            }
            if (n is JsWhile f5) { HoistNode(f5.Body, scope); return; }
            if (n is JsDoWhile f6) { HoistNode(f6.Body, scope); return; }
            if (n is JsSwitch f7)
            {
                for (int i = 0; i < f7.Cases.Count; i++)
                    for (int k = 0; k < f7.Cases[i].Body.Count; k++)
                        HoistNode(f7.Cases[i].Body[k], scope);
                return;
            }
            if (n is JsTry f8)
            {
                if (f8.TryBlock != null) Hoist(f8.TryBlock.Body, scope);
                if (f8.CatchBlock != null) Hoist(f8.CatchBlock.Body, scope);
                if (f8.FinallyBlock != null) Hoist(f8.FinallyBlock.Body, scope);
            }
        }

        // ————— 调用 —————
        public JsValue Call(JsValue fnValue, JsValue thisValue, JsValue[] args)
        {
            if (!(fnValue is JsFunction fn))
                throw new JsRuntimeException("TypeError: " + JsConvert.ToString(fnValue) + " is not a function");

            if (fn.IsNative)
                return fn.Native(new JsCall { This = thisValue, Args = args ?? Array.Empty<JsValue>(), Interp = this });

            if (++depth > MaxDepth)
            {
                depth--;
                throw new JsRuntimeException("RangeError: call stack exceeded in " + fn.Name);
            }
            try
            {
                var scope = new JsScope(fn.Closure ?? Global, true);
                scope.Declare("this", fn.Arrow && fn.Closure.TryGet("this", out var lexicalThis) ? lexicalThis : thisValue ?? JsUndefined.Value);
                if (fn.Patterns != null)
                    for (int n = 0; n < fn.Patterns.Length; n++)
                        JsPatterns.Bind(fn.Patterns[n], args != null && n < args.Length ? args[n] : JsUndefined.Value, this, scope, true);
                var ps = fn.Parameters ?? Array.Empty<string>();
                for (int i = 0; i < ps.Length; i++)
                    scope.Declare(ps[i], args != null && i < args.Length ? args[i] : JsUndefined.Value);
                scope.Declare("arguments", new JsArray(args ?? Array.Empty<JsValue>()));

                if (fn.Body != null)
                {
                    Hoist(fn.Body.Body, scope);
                    fn.Body.Exec(this, scope);
                }
                return JsUndefined.Value;
            }
            catch (JsReturnSignal ret)
            {
                return ret.Value;
            }
            finally
            {
                depth--;
            }
        }

        public JsValue CallGlobal(string name, params JsValue[] args)
        {
            var fn = Global.Get(name);
            return Call(fn, JsUndefined.Value, args);
        }

        // ————— 属性 —————
        public JsValue GetMember(JsValue obj, string key)
        {
            if (obj == null) obj = JsUndefined.Value;

            if (obj is JsDate date) return date.Get(key);
            if (obj is JsRegExp regex) return regex.Get(key);
            if (obj is JsHostObject h) return h.Get(key);
            if (obj is JsObject o) return o.Get(key);
            if (obj is JsArray a)
            {
                if (key == "length") return new JsNumber(a.Items.Count);
                if (int.TryParse(key, out var idx)) return a.GetIndex(idx);
                if (a.Props.TryGetValue(key, out var pv)) return pv;
                if (JsBuiltins.HasArrayMethod(key)) return JsBuiltins.ArrayMethod(this, key);
                return JsUndefined.Value;
            }
            if (obj is JsString s)
            {
                if (key == "length") return new JsNumber(s.Value.Length);
                if (int.TryParse(key, out var idx2)) return idx2 >= 0 && idx2 < s.Value.Length ? new JsString(s.Value[idx2].ToString()) : JsUndefined.Value;
                if (JsBuiltins.HasStringMethod(key)) return JsBuiltins.StringMethod(this, key);
                return JsUndefined.Value;
            }
            if (obj is JsNumber)
            {
                if (JsBuiltins.HasNumberMethod(key)) return JsBuiltins.NumberMethod(this, key);
                return JsUndefined.Value;
            }
            if (obj is JsBoolean)
            {
                if (key == "toString") return JsFunction.FromNative("toString", c => new JsString(JsConvert.ToString(c.This)));
                return JsUndefined.Value;
            }
            if (obj is JsFunction fn)
            {
                if (fn.Members.Has(key)) return fn.Members.Get(key);
                if (key == "call") return JsFunction.FromNative("call", c =>
                {
                    var args = new JsValue[Math.Max(0, c.ArgCount - 1)];
                    for (int i = 1; i < c.ArgCount; i++) args[i - 1] = c.Arg(i);
                    return Call(c.This, c.Arg(0), args);
                });
                if (key == "apply") return JsFunction.FromNative("apply", c =>
                {
                    var argArray = c.Arg(1);
                    var args = new List<JsValue>();
                    if (argArray is JsArray aa) args.AddRange(aa.Items);
                    return Call(c.This, c.Arg(0), args.ToArray());
                });
                if (key == "length") return new JsNumber(fn.Parameters == null ? 0 : fn.Parameters.Length);
                return JsUndefined.Value;
            }
            if (obj is JsNull)
            {
                if (StrictNullMembers) throw new JsRuntimeException("TypeError: cannot read property '" + key + "' of null");
                return NullMember(key);
            }
            if (obj is JsUndefined)
            {
                if (StrictNullMembers) throw new JsRuntimeException("TypeError: cannot read property '" + key + "' of undefined");
                return NullMember(key);
            }
            return JsUndefined.Value;
        }

        // 源端脚本会去访问单机里根本不存在的系统（组队、远征队、仓库、宠物、公会…），
        // 拿回一个 null 之后接着 .size() / .get(i) / .getId()。服务端那些调用返回的是
        // "空集合"或"空引用"，脚本靠这些返回值走进「你没有队伍」「没有可复活宠物」这类分支。
        // 如果这里直接抛错，整段脚本就断在第一步，玩家看到的是一句"脚本出错"。所以对
        // null/undefined 上的读取返回一个空收件人：size()=0、isEmpty()=true、其余调用=null。
        // 需要排查真实空指针时把 StrictNullMembers 打开（验收工具就是用它）。
        public static bool StrictNullMembers = true;

        static JsValue NullMember(string key)
        {
            switch (key)
            {
                case "length": return new JsNumber(0);        // 当属性用（for 循环边界）
                case "size": return JsFunction.FromNative(key, c => new JsNumber(0));
                case "isEmpty": return JsFunction.FromNative(key, c => JsBoolean.Of(true));
                case "toString": return JsFunction.FromNative(key, c => new JsString("null"));
                default: return JsFunction.FromNative(key, c => JsNull.Value);
            }
        }

        public void SetMember(JsValue obj, string key, JsValue value)
        {
            value = value ?? JsNull.Value;
            if (obj is JsRegExp regex && key == "lastIndex") { regex.SetLastIndex(value); return; }
            if (obj is JsHostObject h) { h.Set(key, value); return; }
            if (obj is JsObject o) { o.Set(key, value); return; }
            if (obj is JsArray a) { a.Set(key, value); return; }
            if (obj is JsNull || obj is JsUndefined)
            {
                if (StrictNullMembers)
                    throw new JsRuntimeException("TypeError: cannot set property '" + key + "' of " + (obj is JsNull ? "null" : "undefined"));
                return;
            }
            // 基本类型的属性赋值在非严格模式下是空操作
        }

        // ————— 运算符 —————
        public static JsValue Binary(string op, JsValue l, JsValue r)
        {
            switch (op)
            {
                case "+":
                    if (l is JsString || r is JsString) return new JsString(JsConvert.ToString(l) + JsConvert.ToString(r));
                    return new JsNumber(JsConvert.ToNumber(l) + JsConvert.ToNumber(r));
                case "-": return new JsNumber(JsConvert.ToNumber(l) - JsConvert.ToNumber(r));
                case "*": return new JsNumber(JsConvert.ToNumber(l) * JsConvert.ToNumber(r));
                case "/": return new JsNumber(JsConvert.ToNumber(l) / JsConvert.ToNumber(r));
                case "%": return new JsNumber(JsConvert.ToNumber(l) % JsConvert.ToNumber(r));
                case "==": return JsBoolean.Of(JsConvert.LooseEquals(l, r));
                case "!=": return JsBoolean.Of(!JsConvert.LooseEquals(l, r));
                case "===": return JsBoolean.Of(JsConvert.StrictEquals(l, r));
                case "!==": return JsBoolean.Of(!JsConvert.StrictEquals(l, r));
                case "<": return JsBoolean.Of(Relational(op, l, r));
                case ">": return JsBoolean.Of(Relational(op, l, r));
                case "<=": return JsBoolean.Of(Relational(op, l, r));
                case ">=": return JsBoolean.Of(Relational(op, l, r));
                case "&": return new JsNumber(JsConvert.ToInt32(l) & JsConvert.ToInt32(r));
                case "|": return new JsNumber(JsConvert.ToInt32(l) | JsConvert.ToInt32(r));
                case "^": return new JsNumber(JsConvert.ToInt32(l) ^ JsConvert.ToInt32(r));
                case "<<": return new JsNumber(JsConvert.ToInt32(l) << (JsConvert.ToInt32(r) & 31));
                case ">>": return new JsNumber(JsConvert.ToInt32(l) >> (JsConvert.ToInt32(r) & 31));
                case ">>>": return new JsNumber((uint)JsConvert.ToInt32(l) >> (JsConvert.ToInt32(r) & 31));
                case "in":
                    {
                        string key = JsConvert.ToString(l);
                        if (r is JsObject o) return JsBoolean.Of(o.Has(key));
                        if (r is JsArray a) return JsBoolean.Of(key == "length" || (int.TryParse(key, out var ix) && ix >= 0 && ix < a.Items.Count) || a.Props.ContainsKey(key));
                        if (r is JsHostObject h) return JsBoolean.Of(h.Members.Has(key));
                        return JsBoolean.False;
                    }
                case "instanceof": return JsBoolean.False;
            }
            throw new JsRuntimeException("Unknown operator " + op);
        }

        static bool Relational(string op, JsValue l, JsValue r)
        {
            if (l is JsString ls && r is JsString rs)
            {
                int c = string.CompareOrdinal(ls.Value, rs.Value);
                switch (op)
                {
                    case "<": return c < 0;
                    case ">": return c > 0;
                    case "<=": return c <= 0;
                    default: return c >= 0;
                }
            }
            double a = JsConvert.ToNumber(l), b = JsConvert.ToNumber(r);
            if (double.IsNaN(a) || double.IsNaN(b)) return false;   // JS：NaN 参与的任何比较都为 false
            switch (op)
            {
                case "<": return a < b;
                case ">": return a > b;
                case "<=": return a <= b;
                default: return a >= b;
            }
        }
    }
}
