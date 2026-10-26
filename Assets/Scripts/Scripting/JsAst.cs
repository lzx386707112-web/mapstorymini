using System;
using System.Collections.Generic;

namespace MapleDash.Js
{
    // ——————————————————————— 作用域 ———————————————————————
    public sealed class JsScope
    {
        readonly Dictionary<string, JsValue> vars = new Dictionary<string, JsValue>(StringComparer.Ordinal);
        public readonly JsScope Parent;
        public readonly JsScope FunctionScope;

        public JsScope(JsScope parent, bool functionBoundary = false)
        {
            Parent = parent;
            FunctionScope = functionBoundary || parent == null ? this : parent.FunctionScope;
        }

        public bool HasOwn(string name) => vars.ContainsKey(name);

        public void Declare(string name, JsValue value) => vars[name] = value ?? JsUndefined.Value;

        public JsValue Get(string name)
        {
            for (var s = this; s != null; s = s.Parent)
                if (s.vars.TryGetValue(name, out var v)) return v;
            throw new JsRuntimeException("ReferenceError: " + name + " is not defined");
        }

        public bool TryGet(string name, out JsValue value)
        {
            for (var s = this; s != null; s = s.Parent)
                if (s.vars.TryGetValue(name, out value)) return true;
            value = JsUndefined.Value;
            return false;
        }

        public void Set(string name, JsValue value)
        {
            value = value ?? JsNull.Value;
            for (var s = this; s != null; s = s.Parent)
            {
                if (!s.vars.ContainsKey(name)) continue;
                s.vars[name] = value;
                return;
            }
            // 未声明赋值 → 隐式全局。必须落到根作用域，否则在 start() 里
            // 写 `status = -1`、再到 action() 里读就会 ReferenceError（源端 60+ 个 NPC 这么写）。
            var root = this;
            while (root.Parent != null) root = root.Parent;
            root.vars[name] = value;
        }

        public void DeclareVar(string name, JsValue value)
            => FunctionScope.vars[name] = value ?? JsUndefined.Value;

        public IEnumerable<string> OwnNames => vars.Keys;
    }

    // ——————————————————————— AST ———————————————————————
    public abstract class JsNode
    {
        public int Line;
        public int Column;
        public string Label;

        public virtual JsValue Eval(JsInterpreter it, JsScope scope)
            => throw new JsRuntimeException("Cannot evaluate statement as expression");

        public virtual void Exec(JsInterpreter it, JsScope scope)
            => throw new JsRuntimeException("Cannot execute expression as statement");
    }

    public sealed class JsProgram : JsNode
    {
        public readonly List<JsNode> Body = new List<JsNode>();

        public void Run(JsInterpreter it, JsScope scope)
        {
            it.Hoist(Body, scope);
            for (int i = 0; i < Body.Count; i++) Body[i].Exec(it, scope);
        }

        public override void Exec(JsInterpreter it, JsScope scope) => Run(it, scope);
    }

    public sealed class JsBlock : JsNode
    {
        public readonly List<JsNode> Body = new List<JsNode>();
        public override void Exec(JsInterpreter it, JsScope scope)
        {
            for (int i = 0; i < Body.Count; i++) Body[i].Exec(it, scope);
        }
    }

    public sealed class JsLabel : JsNode
    {
        public JsNode Body;
        public override void Exec(JsInterpreter it, JsScope scope)
        {
            try { Body.Exec(it, scope); }
            catch (JsBreakSignal signal) when (signal.Label == Label) { }
        }
    }

    public sealed class JsEmpty : JsNode
    {
        public override void Exec(JsInterpreter it, JsScope scope) { }
    }

    public sealed class JsVarDeclarator
    {
        public string Name;
        public JsNode Init;
        public JsNode Pattern;
    }

    public sealed class JsVarDecl : JsNode
    {
        public readonly List<JsVarDeclarator> Decls = new List<JsVarDeclarator>();
        public override void Exec(JsInterpreter it, JsScope scope)
        {
            for (int i = 0; i < Decls.Count; i++)
            {
                var d = Decls[i];
                if (d.Init == null)
                {
                    if (d.Pattern != null) throw new JsRuntimeException("SyntaxError: destructuring declaration requires initializer");
                    if (!scope.FunctionScope.HasOwn(d.Name)) scope.DeclareVar(d.Name, JsUndefined.Value);
                    continue;
                }
                if (d.Pattern != null) JsPatterns.Bind(d.Pattern, d.Init.Eval(it, scope), it, scope, true);
                else scope.DeclareVar(d.Name, d.Init.Eval(it, scope));
            }
        }
    }

    public sealed class JsFunctionDecl : JsNode
    {
        public string Name;
        public string[] Parameters;
        public JsNode[] Patterns;
        public JsBlock Body;

        public override void Exec(JsInterpreter it, JsScope scope)
        {
            // 函数声明在 Hoist 阶段已经绑定；这里不再重复创建闭包（否则会丢掉后被覆盖的状态）。
        }
    }

    public sealed class JsExprStmt : JsNode
    {
        public JsNode Expr;
        public override void Exec(JsInterpreter it, JsScope scope)
        {
            if (Expr != null) Expr.Eval(it, scope);
        }
    }

    public sealed class JsIf : JsNode
    {
        public JsNode Test, Then, Else;
        public override void Exec(JsInterpreter it, JsScope scope)
        {
            if (JsConvert.ToBoolean(Test.Eval(it, scope))) Then?.Exec(it, scope);
            else Else?.Exec(it, scope);
        }
    }

    public sealed class JsFor : JsNode
    {
        public JsNode Init, Test, Update, Body;
        public override void Exec(JsInterpreter it, JsScope scope)
        {
            if (Init != null)
            {
                if (Init is JsVarDecl || Init is JsExprStmt || Init is JsBlock) Init.Exec(it, scope);
                else Init.Eval(it, scope);
            }
            while (Test == null || JsConvert.ToBoolean(Test.Eval(it, scope)))
            {
                it.Step();
                try { Body?.Exec(it, scope); }
                catch (JsBreakSignal signal) when (signal.Label == null || signal.Label == Label) { break; }
                catch (JsContinueSignal signal) when (signal.Label == null || signal.Label == Label) { }
                Update?.Eval(it, scope);
            }
        }
    }

    public sealed class JsForIn : JsNode
    {
        public string VarName;          // for (var k in o)
        public JsNode Target;           // for (k in o)
        public JsNode ObjectExpr;
        public JsNode Body;
        public bool Values;
        public JsNode Pattern;

        public override void Exec(JsInterpreter it, JsScope scope)
        {
            var obj = ObjectExpr.Eval(it, scope);
            if (Values)
            {
                foreach (var value in JsPatterns.Iterate(obj))
                {
                    it.Step();
                    if (Pattern != null) JsPatterns.Bind(Pattern, value, it, scope, true);
                    else if (VarName != null) scope.DeclareVar(VarName, value);
                    else JsPatterns.Bind(Target, value, it, scope, false);
                    try { Body?.Exec(it, scope); }
                    catch (JsBreakSignal signal) when (signal.Label == null || signal.Label == Label) { break; }
                    catch (JsContinueSignal signal) when (signal.Label == null || signal.Label == Label) { }
                }
                return;
            }
            var keys = new List<string>();
            if (obj is JsObject o) keys.AddRange(o.Props.Keys);
            else if (obj is JsArray a)
            {
                for (int i = 0; i < a.Items.Count; i++) keys.Add(i.ToString());
                keys.AddRange(a.Props.Keys);
            }
            else if (obj is JsHostObject h) keys.AddRange(h.Members.Props.Keys);
            else if (obj is JsString s) for (int i = 0; i < s.Value.Length; i++) keys.Add(i.ToString());

            for (int i = 0; i < keys.Count; i++)
            {
                it.Step();
                var kv = new JsString(keys[i]);
                if (VarName != null) scope.DeclareVar(VarName, kv);
                else if (Target is JsIdentifier id) scope.Set(id.Name, kv);
                else if (Target is JsMember mem)
                {
                    var host = mem.Object.Eval(it, scope);
                    it.SetMember(host, mem.PropertyName(it, scope), kv);
                }
                try { Body?.Exec(it, scope); }
                catch (JsBreakSignal signal) when (signal.Label == null || signal.Label == Label) { break; }
                catch (JsContinueSignal signal) when (signal.Label == null || signal.Label == Label) { }
            }
        }
    }

    public sealed class JsWhile : JsNode
    {
        public JsNode Test, Body;
        public override void Exec(JsInterpreter it, JsScope scope)
        {
            while (JsConvert.ToBoolean(Test.Eval(it, scope)))
            {
                it.Step();
                try { Body?.Exec(it, scope); }
                catch (JsBreakSignal signal) when (signal.Label == null || signal.Label == Label) { break; }
                catch (JsContinueSignal signal) when (signal.Label == null || signal.Label == Label) { }
            }
        }
    }

    public sealed class JsDoWhile : JsNode
    {
        public JsNode Test, Body;
        public override void Exec(JsInterpreter it, JsScope scope)
        {
            do
            {
                it.Step();
                try { Body?.Exec(it, scope); }
                catch (JsBreakSignal signal) when (signal.Label == null || signal.Label == Label) { break; }
                catch (JsContinueSignal signal) when (signal.Label == null || signal.Label == Label) { }
            }
            while (JsConvert.ToBoolean(Test.Eval(it, scope)));
        }
    }

    public sealed class JsReturn : JsNode
    {
        public JsNode Value;
        public override void Exec(JsInterpreter it, JsScope scope)
            => throw new JsReturnSignal(Value == null ? JsUndefined.Value : Value.Eval(it, scope));
    }

    public sealed class JsBreak : JsNode
    {
        public override void Exec(JsInterpreter it, JsScope scope) => throw new JsBreakSignal(Label);
    }

    public sealed class JsContinue : JsNode
    {
        public override void Exec(JsInterpreter it, JsScope scope) => throw new JsContinueSignal(Label);
    }

    public sealed class JsThrow : JsNode
    {
        public JsNode Value;
        public override void Exec(JsInterpreter it, JsScope scope)
        {
            var v = Value.Eval(it, scope);
            throw new JsRuntimeException(JsConvert.ToString(v), v);
        }
    }

    public sealed class JsTry : JsNode
    {
        public JsBlock TryBlock;
        public string CatchName;
        public JsBlock CatchBlock;
        public JsBlock FinallyBlock;

        public override void Exec(JsInterpreter it, JsScope scope)
        {
            try
            {
                TryBlock?.Exec(it, scope);
            }
            catch (JsRuntimeException ex)
            {
                if (CatchBlock == null) throw;
                var catchScope = new JsScope(scope);
                if (!string.IsNullOrEmpty(CatchName)) catchScope.Declare(CatchName, ex.Value);
                CatchBlock.Exec(it, catchScope);
            }
            finally
            {
                FinallyBlock?.Exec(it, scope);
            }
        }
    }

    public sealed class JsSwitchCase
    {
        public JsNode Test;              // null = default
        public readonly List<JsNode> Body = new List<JsNode>();
    }

    public sealed class JsSwitch : JsNode
    {
        public JsNode Discriminant;
        public readonly List<JsSwitchCase> Cases = new List<JsSwitchCase>();

        public override void Exec(JsInterpreter it, JsScope scope)
        {
            var value = Discriminant.Eval(it, scope);
            int start = -1, def = -1;
            for (int i = 0; i < Cases.Count; i++)
            {
                var c = Cases[i];
                if (c.Test == null) { def = i; continue; }
                if (JsConvert.StrictEquals(value, c.Test.Eval(it, scope))) { start = i; break; }
            }
            if (start < 0) start = def;
            if (start < 0) return;
            try
            {
                for (int i = start; i < Cases.Count; i++)
                    for (int k = 0; k < Cases[i].Body.Count; k++) Cases[i].Body[k].Exec(it, scope);
            }
            catch (JsBreakSignal signal) when (signal.Label == null || signal.Label == Label) { }
        }
    }

    // ——————————————————————— 表达式 ———————————————————————
    public sealed class JsLiteral : JsNode
    {
        public JsValue Value;
        public override JsValue Eval(JsInterpreter it, JsScope scope) => Value;
    }

    public sealed class JsIdentifier : JsNode
    {
        public string Name;
        public override JsValue Eval(JsInterpreter it, JsScope scope) => scope.Get(Name);
    }

    public sealed class JsThisExpr : JsNode
    {
        public override JsValue Eval(JsInterpreter it, JsScope scope)
            => scope.TryGet("this", out var v) ? v : JsUndefined.Value;
    }

    public sealed class JsArrayLiteral : JsNode
    {
        public readonly List<JsNode> Items = new List<JsNode>();
        public override JsValue Eval(JsInterpreter it, JsScope scope)
        {
            var a = new JsArray();
            for (int i = 0; i < Items.Count; i++)
                if (Items[i] is JsSpread spread) a.Items.AddRange(JsPatterns.Iterate(spread.Value.Eval(it, scope)));
                else a.Items.Add(Items[i] == null ? JsUndefined.Value : Items[i].Eval(it, scope));
            return a;
        }
    }

    public sealed class JsObjectLiteral : JsNode
    {
        public readonly List<KeyValuePair<string, JsNode>> Props = new List<KeyValuePair<string, JsNode>>();
        public override JsValue Eval(JsInterpreter it, JsScope scope)
        {
            var o = new JsObject();
            for (int i = 0; i < Props.Count; i++)
                if (Props[i].Value is JsSpread spread)
                {
                    var source = spread.Value.Eval(it, scope);
                    foreach (var key in JsPatterns.Keys(source)) o.Set(key, it.GetMember(source, key));
                }
                else o.Set(Props[i].Key, Props[i].Value.Eval(it, scope));
            return o;
        }
    }

    public sealed class JsFunctionExpr : JsNode
    {
        public string Name;
        public string[] Parameters;
        public JsBlock Body;
        public JsNode[] Patterns;
        public bool Arrow;
        public override JsValue Eval(JsInterpreter it, JsScope scope)
            => new JsFunction { Name = Name ?? "", Parameters = Parameters, Patterns = Patterns, Arrow = Arrow, Body = Body, Closure = scope };
    }

    public sealed class JsUnary : JsNode
    {
        public string Op;
        public JsNode Operand;

        public override JsValue Eval(JsInterpreter it, JsScope scope)
        {
            if (Op == "typeof")
            {
                if (Operand is JsIdentifier id)
                    return new JsString(scope.TryGet(id.Name, out var uv) ? uv.TypeName : "undefined");
                return new JsString(Operand.Eval(it, scope).TypeName);
            }
            if (Op == "delete")
            {
                if (Operand is JsMember m)
                {
                    var target = m.Object.Eval(it, scope);
                    var key = m.PropertyName(it, scope);
                    if (target is JsObject o) { o.Remove(key); return JsBoolean.True; }
                    if (target is JsArray a) { a.Props.Remove(key); return JsBoolean.True; }
                }
                return JsBoolean.False;
            }

            var v = Operand.Eval(it, scope);
            switch (Op)
            {
                case "!": return JsBoolean.Of(!JsConvert.ToBoolean(v));
                case "-": return new JsNumber(-JsConvert.ToNumber(v));
                case "+": return new JsNumber(JsConvert.ToNumber(v));
                case "~": return new JsNumber(~JsConvert.ToInt32(v));
                case "void": return JsUndefined.Value;
            }
            throw new JsRuntimeException("Unknown unary operator " + Op);
        }
    }

    public sealed class JsUpdate : JsNode
    {
        public bool Increment, Prefix;
        public JsNode Target;

        public override JsValue Eval(JsInterpreter it, JsScope scope)
        {
            JsValue old;
            Action<JsValue> write;

            if (Target is JsIdentifier id)
            {
                old = scope.Get(id.Name);
                write = v => scope.Set(id.Name, v);
            }
            else if (Target is JsMember m)
            {
                var host = m.Object.Eval(it, scope);
                var key = m.PropertyName(it, scope);
                old = it.GetMember(host, key);
                write = v => it.SetMember(host, key, v);
            }
            else throw new JsRuntimeException("Invalid update target");

            double n = JsConvert.ToNumber(old);
            double next = Increment ? n + 1 : n - 1;
            write(new JsNumber(next));
            return Prefix ? new JsNumber(next) : new JsNumber(n);
        }
    }

    public sealed class JsBinary : JsNode
    {
        public string Op;
        public JsNode Left, Right;

        public override JsValue Eval(JsInterpreter it, JsScope scope)
            => JsInterpreter.Binary(Op, Left.Eval(it, scope), Right.Eval(it, scope));
    }

    public sealed class JsLogical : JsNode
    {
        public string Op;
        public JsNode Left, Right;

        public override JsValue Eval(JsInterpreter it, JsScope scope)
        {
            var l = Left.Eval(it, scope);
            if (Op == "??") return l is JsNull || l is JsUndefined ? Right.Eval(it, scope) : l;
            if (Op == "&&") return JsConvert.ToBoolean(l) ? Right.Eval(it, scope) : l;
            return JsConvert.ToBoolean(l) ? l : Right.Eval(it, scope);
        }
    }

    public sealed class JsConditional : JsNode
    {
        public JsNode Test, Then, Else;
        public override JsValue Eval(JsInterpreter it, JsScope scope)
            => JsConvert.ToBoolean(Test.Eval(it, scope)) ? Then.Eval(it, scope) : Else.Eval(it, scope);
    }

    public sealed class JsAssign : JsNode
    {
        public string Op;
        public JsNode Target, Value;

        public override JsValue Eval(JsInterpreter it, JsScope scope)
        {
            JsValue result;
            if (Op == "=")
            {
                result = Value.Eval(it, scope);
            }
            else
            {
                var cur = Target.Eval(it, scope);
                var rhs = Value.Eval(it, scope);
                result = JsInterpreter.Binary(Op.Substring(0, Op.Length - 1), cur, rhs);
            }

            if (Target is JsArrayLiteral || Target is JsObjectLiteral) JsPatterns.Bind(Target, result, it, scope, false);
            else if (Target is JsIdentifier id) scope.Set(id.Name, result);
            else if (Target is JsMember m)
            {
                var host = m.Object.Eval(it, scope);
                it.SetMember(host, m.PropertyName(it, scope), result);
            }
            else throw new JsRuntimeException("Invalid assignment target");
            return result;
        }
    }

    public sealed class JsMember : JsNode
    {
        public JsNode Object;
        public JsNode Property;          // 计算属性
        public string Name;              // 点号属性
        public bool Optional;

        public string PropertyName(JsInterpreter it, JsScope scope)
            => Name ?? JsConvert.ToString(Property.Eval(it, scope));

        public override JsValue Eval(JsInterpreter it, JsScope scope)
        {
            var host = Object.Eval(it, scope);
            if (Optional && (host is JsNull || host is JsUndefined)) return JsUndefined.Value;
            return it.GetMember(host, PropertyName(it, scope));
        }
    }

    public sealed class JsCallExpr : JsNode
    {
        public JsNode Callee;
        public readonly List<JsNode> Args = new List<JsNode>();

        public override JsValue Eval(JsInterpreter it, JsScope scope)
        {
            JsValue fn, thisVal = JsUndefined.Value;
            if (Callee is JsMember m)
            {
                thisVal = m.Object.Eval(it, scope);
                if (m.Optional && (thisVal is JsNull || thisVal is JsUndefined)) return JsUndefined.Value;
                fn = it.GetMember(thisVal, m.PropertyName(it, scope));
            }
            else if (Callee is JsIdentifier direct && direct.Name == "eval" && !scope.TryGet("eval", out _))
            {
                if (Args.Count == 0) return JsUndefined.Value;
                var input = Args[0].Eval(it, scope);
                if (!(input is JsString text)) return input;
                var program = new JsParser(text.Value).ParseProgram();
                it.Hoist(program.Body, scope);
                JsValue result = JsUndefined.Value;
                foreach (var statement in program.Body)
                {
                    it.Step();
                    if (statement is JsExprStmt expression) result = expression.Expr.Eval(it, scope);
                    else { statement.Exec(it, scope); result = JsUndefined.Value; }
                }
                return result;
            }
            else fn = Callee.Eval(it, scope);

            if (!(fn is JsFunction))
            {
                string label = Callee is JsMember jm && !string.IsNullOrEmpty(jm.Name) ? jm.Name : "expression";
                throw new JsRuntimeException("TypeError: " + label + " is not a function (" + JsConvert.ToString(fn) + ")");
            }
            var args = JsPatterns.Arguments(Args, it, scope);
            return it.Call(fn, thisVal, args);
        }
    }

    public sealed class JsNew : JsNode
    {
        public JsNode Callee;
        public readonly List<JsNode> Args = new List<JsNode>();

        public override JsValue Eval(JsInterpreter it, JsScope scope)
        {
            var fn = Callee.Eval(it, scope);
            var args = JsPatterns.Arguments(Args, it, scope);
            var obj = new JsObject();
            var ret = it.Call(fn, obj, args);
            return ret is JsObject || ret is JsArray || ret is JsHostObject || ret is JsDate || ret is JsRegExp ? ret : obj;
        }
    }

    public sealed class JsSequence : JsNode
    {
        public readonly List<JsNode> Items = new List<JsNode>();
        public override JsValue Eval(JsInterpreter it, JsScope scope)
        {
            JsValue v = JsUndefined.Value;
            for (int i = 0; i < Items.Count; i++) v = Items[i].Eval(it, scope);
            return v;
        }
    }
}
