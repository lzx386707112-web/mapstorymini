using System;
using System.Collections.Generic;
using System.Text;

namespace MapleDash.Js
{
    public enum JsTokenKind
    {
        Eof,
        Number,
        String,
        Template,
        Regex,
        Identifier,
        Keyword,
        Punct,
    }

    public struct JsToken
    {
        public JsTokenKind Kind;
        public string Text;
        public double Number;
        public string Flags;
        public int Line;
        public int Column;

        public override string ToString() => Kind + " '" + Text + "' @" + Line + ":" + Column;
    }

    // 原脚本词法器：包含模板、正则字面量及数字分隔符。
    public sealed class JsLexer
    {
        static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "var", "function", "if", "else", "for", "while", "do", "return", "break", "continue",
            "new", "delete", "typeof", "instanceof", "in", "this", "null", "true", "false",
            "switch", "case", "default", "try", "catch", "finally", "throw", "void", "with",
        };

        readonly string src;
        int pos;
        int line = 1;
        int column = 1;

        public JsLexer(string source)
        {
            src = source ?? "";
            // 去掉 UTF-8 BOM
            if (src.Length > 0 && src[0] == '\uFEFF') pos = 1;
        }

        char Cur => pos < src.Length ? src[pos] : '\0';
        char Peek(int n) => pos + n < src.Length ? src[pos + n] : '\0';

        void Advance()
        {
            if (pos >= src.Length) return;
            if (src[pos] == '\n') { line++; column = 1; }
            else column++;
            pos++;
        }

        public List<JsToken> Tokenize()
        {
            var list = new List<JsToken>();
            while (true)
            {
                SkipTrivia();
                bool expressionStart = list.Count == 0 || ExpectsExpression(list[list.Count - 1]);
                var t = Cur == '/' && Peek(1) != '=' && expressionStart ? ReadRegex() : Next();
                list.Add(t);
                if (t.Kind == JsTokenKind.Eof) break;
            }
            return list;
        }

        static bool ExpectsExpression(JsToken token)
        {
            if (token.Kind == JsTokenKind.Keyword)
                return token.Text == "return" || token.Text == "throw" || token.Text == "case" || token.Text == "typeof" || token.Text == "void" || token.Text == "delete" || token.Text == "in";
            return token.Kind == JsTokenKind.Punct && token.Text != ")" && token.Text != "]" && token.Text != "}" && token.Text != "." && token.Text != "++" && token.Text != "--";
        }

        JsToken ReadRegex()
        {
            int l = line, c = column;
            Advance();
            var pattern = new StringBuilder();
            bool inClass = false, closed = false;
            while (pos < src.Length && Cur != '\n' && Cur != '\r')
            {
                char ch = Cur; Advance();
                if (ch == '\\')
                {
                    pattern.Append(ch);
                    if (pos < src.Length) { pattern.Append(Cur); Advance(); }
                    continue;
                }
                if (ch == '[') inClass = true;
                else if (ch == ']') inClass = false;
                else if (ch == '/' && !inClass) { closed = true; break; }
                pattern.Append(ch);
            }
            if (!closed) throw new JsRuntimeException("SyntaxError: unterminated regular expression (line " + l + ")");
            var flags = new StringBuilder();
            while (char.IsLetter(Cur)) { flags.Append(Cur); Advance(); }
            return new JsToken { Kind = JsTokenKind.Regex, Text = pattern.ToString(), Flags = flags.ToString(), Line = l, Column = c };
        }

        bool SkipTrivia()
        {
            bool skipped = false;
            while (pos < src.Length)
            {
                char c = Cur;
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\f' || c == '\v' || c == '\u00A0')
                {
                    Advance(); skipped = true; continue;
                }
                if (c == '/' && Peek(1) == '/')
                {
                    while (pos < src.Length && Cur != '\n') Advance();
                    skipped = true; continue;
                }
                if (c == '/' && Peek(1) == '*')
                {
                    Advance(); Advance();
                    while (pos < src.Length && !(Cur == '*' && Peek(1) == '/')) Advance();
                    if (pos < src.Length) { Advance(); Advance(); }
                    skipped = true; continue;
                }
                break;
            }
            return skipped;
        }

        JsToken Next()
        {
            SkipTrivia();
            int l = line, c0 = column;
            if (pos >= src.Length) return new JsToken { Kind = JsTokenKind.Eof, Text = "", Line = l, Column = c0 };

            char ch = Cur;

            if (char.IsDigit(ch) || (ch == '.' && char.IsDigit(Peek(1)))) return ReadNumber(l, c0);
            if (ch == '"' || ch == '\'') return ReadString(l, c0);
            if (ch == '`') return ReadTemplate(l, c0);
            if (IsIdentifierStart(ch)) return ReadIdentifier(l, c0);
            return ReadPunct(l, c0);
        }

        static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_' || c == '$';
        static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';

        JsToken ReadIdentifier(int l, int c0)
        {
            var sb = new StringBuilder();
            while (pos < src.Length && IsIdentifierPart(Cur)) { sb.Append(Cur); Advance(); }
            string text = sb.ToString();
            return new JsToken
            {
                Kind = Keywords.Contains(text) ? JsTokenKind.Keyword : JsTokenKind.Identifier,
                Text = text, Line = l, Column = c0,
            };
        }

        JsToken ReadNumber(int l, int c0)
        {
            var sb = new StringBuilder();
            bool hex = false;
            if (Cur == '0' && (Peek(1) == 'x' || Peek(1) == 'X'))
            {
                hex = true; sb.Append("0x"); Advance(); Advance();
                while (pos < src.Length && Uri.IsHexDigit(Cur)) { sb.Append(Cur); Advance(); }
            }
            else
            {
                while (pos < src.Length && (char.IsDigit(Cur) || Cur == '_')) { if (Cur != '_') sb.Append(Cur); Advance(); }
                if (Cur == '.') { sb.Append('.'); Advance(); while (pos < src.Length && (char.IsDigit(Cur) || Cur == '_')) { if (Cur != '_') sb.Append(Cur); Advance(); } }
                if (Cur == 'e' || Cur == 'E')
                {
                    sb.Append('e'); Advance();
                    if (Cur == '+' || Cur == '-') { sb.Append(Cur); Advance(); }
                    while (pos < src.Length && (char.IsDigit(Cur) || Cur == '_')) { if (Cur != '_') sb.Append(Cur); Advance(); }
                }
            }
            string text = sb.ToString();
            double value;
            if (hex)
            {
                long v;
                long.TryParse(text.Substring(2), System.Globalization.NumberStyles.HexNumber,
                              System.Globalization.CultureInfo.InvariantCulture, out v);
                value = v;
            }
            else
            {
                double.TryParse(text, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out value);
            }
            return new JsToken { Kind = JsTokenKind.Number, Text = text, Number = value, Line = l, Column = c0 };
        }

        JsToken ReadString(int l, int c0)
        {
            char quote = Cur;
            Advance();
            var sb = new StringBuilder();
            while (pos < src.Length)
            {
                char c = Cur;
                if (c == quote) { Advance(); break; }
                if (c == '\n') break;                    // 未闭合：按行末结束，交给语法检查报错
                if (c == '\\')
                {
                    Advance();
                    if (pos >= src.Length) break;
                    char e = Cur;
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); Advance(); break;
                        case 'r': sb.Append('\r'); Advance(); break;
                        case 't': sb.Append('\t'); Advance(); break;
                        case 'b': sb.Append('\b'); Advance(); break;
                        case 'f': sb.Append('\f'); Advance(); break;
                        case 'v': sb.Append('\v'); Advance(); break;
                        case '0': sb.Append('\0'); Advance(); break;
                        case 'x':
                            {
                                Advance();
                                int v = 0, n = 0;
                                while (n < 2 && pos < src.Length && Uri.IsHexDigit(Cur)) { v = v * 16 + HexVal(Cur); Advance(); n++; }
                                if (n > 0) sb.Append((char)v);
                                break;
                            }
                        case 'u':
                            {
                                Advance();
                                int v = 0, n = 0;
                                while (n < 4 && pos < src.Length && Uri.IsHexDigit(Cur)) { v = v * 16 + HexVal(Cur); Advance(); n++; }
                                if (n > 0) sb.Append((char)v);
                                break;
                            }
                        case '\n': Advance(); break;      // 续行
                        case '\r': Advance(); if (Cur == '\n') Advance(); break;
                        default: sb.Append(e); Advance(); break;
                    }
                    continue;
                }
                sb.Append(c);
                Advance();
            }
            return new JsToken { Kind = JsTokenKind.String, Text = sb.ToString(), Line = l, Column = c0 };
        }

        // ES6 模板字符串：源端有 26 个脚本在用，保留原文交给语法分析器拆 ${...}。
        JsToken ReadTemplate(int l, int c0)
        {
            Advance();                              // 跳过 `
            var sb = new StringBuilder();
            while (pos < src.Length)
            {
                char c = Cur;
                if (c == '`') { Advance(); break; }
                if (c == '\\')
                {
                    sb.Append(c);
                    Advance();
                    if (pos < src.Length) { sb.Append(Cur); Advance(); }
                    continue;
                }
                if (c == '$' && Peek(1) == '{')
                {
                    sb.Append("${");
                    Advance(); Advance();
                    int depth = 1;
                    while (pos < src.Length && depth > 0)
                    {
                        char d = Cur;
                        if (d == '{') depth++;
                        else if (d == '}') depth--;
                        if (depth == 0) { Advance(); break; }
                        sb.Append(d);
                        Advance();
                    }
                    sb.Append('}');
                    continue;
                }
                sb.Append(c);
                Advance();
            }
            return new JsToken { Kind = JsTokenKind.Template, Text = sb.ToString(), Line = l, Column = c0 };
        }

        static int HexVal(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return 0;
        }

        static readonly string[] Puncts3 = { "...", "===", "!==", ">>>", "<<=", ">>=" };
        static readonly string[] Puncts2 =
        {
            "==", "!=", "<=", ">=", "&&", "||", "++", "--", "+=", "-=", "*=", "/=", "%=",
            "&=", "|=", "^=", "<<", ">>", "=>", "??", "?.",
        };

        JsToken ReadPunct(int l, int c0)
        {
            for (int i = 0; i < Puncts3.Length; i++)
            {
                string p = Puncts3[i];
                if (Match(p)) { for (int k = 0; k < p.Length; k++) Advance(); return new JsToken { Kind = JsTokenKind.Punct, Text = p, Line = l, Column = c0 }; }
            }
            for (int i = 0; i < Puncts2.Length; i++)
            {
                string p = Puncts2[i];
                if (Match(p)) { for (int k = 0; k < p.Length; k++) Advance(); return new JsToken { Kind = JsTokenKind.Punct, Text = p, Line = l, Column = c0 }; }
            }
            char c = Cur;
            Advance();
            return new JsToken { Kind = JsTokenKind.Punct, Text = c.ToString(), Line = l, Column = c0 };
        }

        bool Match(string s)
        {
            if (pos + s.Length > src.Length) return false;
            for (int i = 0; i < s.Length; i++) if (src[pos + i] != s[i]) return false;
            return true;
        }
    }
}
