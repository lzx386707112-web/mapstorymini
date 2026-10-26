using System;
using System.Text.RegularExpressions;

namespace MapleDash.Js
{
    public sealed class JsRegexLiteral : JsNode
    {
        public string Pattern, Flags;
        public override JsValue Eval(JsInterpreter it, JsScope scope) => new JsRegExp(Pattern, Flags);
    }

    // 源脚本的字符类/锚点/分组正则，使用 BCL 解释执行，禁止 RegexOptions.Compiled。
    public sealed class JsRegExp : JsValue
    {
        public override string TypeName => "object";
        public readonly Regex Regex;
        public readonly string Pattern, Flags;
        public readonly bool Global;
        int lastIndex;

        public JsRegExp(string pattern, string flags)
        {
            Pattern = pattern;
            Flags = flags ?? "";
            Global = Flags.Contains("g");
            var options = RegexOptions.CultureInvariant | RegexOptions.ECMAScript;
            if (Flags.Contains("i")) options |= RegexOptions.IgnoreCase;
            if (Flags.Contains("m")) options |= RegexOptions.Multiline;
            foreach (char flag in Flags)
                if ("gim".IndexOf(flag) < 0) throw new JsRuntimeException("Unsupported regular expression flag: " + flag);
            try { Regex = new Regex(pattern, options, TimeSpan.FromSeconds(1)); }
            catch (ArgumentException e) { throw new JsRuntimeException("SyntaxError: " + e.Message); }
        }

        public JsValue Get(string key)
        {
            if (key == "source") return new JsString(Pattern);
            if (key == "global") return JsBoolean.Of(Global);
            if (key == "lastIndex") return new JsNumber(lastIndex);
            if (key == "test" || key == "exec") return JsFunction.FromNative(key, c =>
            {
                string input = JsConvert.ToString(c.Arg(0));
                Match match = Global && lastIndex > input.Length ? Match.Empty : Regex.Match(input, Global ? lastIndex : 0);
                if (Global) lastIndex = match.Success ? match.Index + match.Length : 0;
                if (key == "test") return JsBoolean.Of(match.Success);
                if (!match.Success) return JsNull.Value;
                var result = new JsArray();
                foreach (Group group in match.Groups) result.Items.Add(group.Success ? (JsValue)new JsString(group.Value) : JsUndefined.Value);
                result.Set("index", new JsNumber(match.Index));
                result.Set("input", new JsString(input));
                return result;
            });
            return JsUndefined.Value;
        }

        public void SetLastIndex(JsValue value) => lastIndex = Math.Max(0, JsConvert.ToInt32(value));
    }
}
