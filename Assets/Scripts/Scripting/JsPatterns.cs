using System;
using System.Collections.Generic;

namespace MapleDash.Js
{
    // 统一处理函数参数/变量/for-of/赋值解构与展开，不改写源脚本文本。
    public static class JsPatterns
    {
        public static IEnumerable<string> Keys(JsValue value)
        {
            if (value is JsObject obj) return new List<string>(obj.Props.Keys);
            if (value is JsHostObject host) return new List<string>(host.Members.Props.Keys);
            var keys = new List<string>();
            int count = value is JsArray array ? array.Items.Count : value is JsString text ? text.Value.Length : 0;
            for (int n = 0; n < count; n++) keys.Add(n.ToString());
            return keys;
        }

        public static IEnumerable<JsValue> Iterate(JsValue value)
        {
            if (value is JsArray array) return new List<JsValue>(array.Items);
            if (value is JsString text)
            {
                var chars = new List<JsValue>();
                for (int n = 0; n < text.Value.Length; n++) chars.Add(new JsString(text.Value[n].ToString()));
                return chars;
            }
            throw new JsRuntimeException("TypeError: value is not iterable");
        }

        public static JsValue[] Arguments(List<JsNode> nodes, JsInterpreter it, JsScope scope)
        {
            var values = new List<JsValue>();
            foreach (var node in nodes)
                if (node is JsSpread spread) values.AddRange(Iterate(spread.Value.Eval(it, scope)));
                else values.Add(node.Eval(it, scope));
            return values.ToArray();
        }

        public static void Hoist(JsNode node, JsScope scope)
        {
            if (node is JsIdentifier id) { if (!scope.FunctionScope.HasOwn(id.Name)) scope.DeclareVar(id.Name, JsUndefined.Value); }
            else if (node is JsArrayLiteral array) { foreach (var item in array.Items) Hoist(item, scope); }
            else if (node is JsObjectLiteral obj) { foreach (var pair in obj.Props) Hoist(pair.Value, scope); }
            else if (node is JsAssign assign) Hoist(assign.Target, scope);
        }

        public static void Bind(JsNode pattern, JsValue value, JsInterpreter it, JsScope scope, bool declare)
        {
            if (pattern == null) return;
            if (pattern is JsIdentifier id)
            {
                if (declare) scope.DeclareVar(id.Name, value); else scope.Set(id.Name, value);
            }
            else if (pattern is JsAssign fallback)
                Bind(fallback.Target, value is JsUndefined ? fallback.Value.Eval(it, scope) : value, it, scope, declare);
            else if (pattern is JsMember member)
                it.SetMember(member.Object.Eval(it, scope), member.PropertyName(it, scope), value);
            else if (pattern is JsArrayLiteral array)
            {
                var items = new List<JsValue>(Iterate(value));
                for (int n = 0; n < array.Items.Count; n++)
                    Bind(array.Items[n], n < items.Count ? items[n] : JsUndefined.Value, it, scope, declare);
            }
            else if (pattern is JsObjectLiteral obj)
            {
                if (value is JsNull || value is JsUndefined) throw new JsRuntimeException("TypeError: cannot destructure null or undefined");
                foreach (var pair in obj.Props) Bind(pair.Value, it.GetMember(value, pair.Key), it, scope, declare);
            }
            else throw new JsRuntimeException("SyntaxError: invalid binding pattern");
        }
    }

    public sealed class JsSpread : JsNode
    {
        public JsNode Value;
    }
}
