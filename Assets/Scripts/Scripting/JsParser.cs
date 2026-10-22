using System;
using System.Collections.Generic;

namespace MapleDash.Js
{
    // 递归下降 + 优先级爬升。只覆盖 ES5 子集（源端 npc/quest 脚本实际使用的语法）。
    public sealed class JsParser
    {
        readonly List<JsToken> tokens;
        int i;

        public JsParser(string source)
        {
            tokens = new JsLexer(source).Tokenize();
        }

        JsToken Cur => tokens[i];
        JsToken Peek(int n) => tokens[Math.Min(i + n, tokens.Count - 1)];
        bool AtEnd => Cur.Kind == JsTokenKind.Eof;

        bool Is(string text) => Cur.Text == text && (Cur.Kind == JsTokenKind.Punct || Cur.Kind == JsTokenKind.Keyword);
        bool IsKeyword(string text) => Cur.Kind == JsTokenKind.Keyword && Cur.Text == text;
        bool IsIdentifier => Cur.Kind == JsTokenKind.Identifier;

        JsToken Take() => tokens[i++];

        JsToken Expect(string text)
        {
            if (!Is(text)) throw Error("expected '" + text + "' but found '" + Cur.Text + "'");
            return Take();
        }

        JsRuntimeException Error(string message)
            => new JsRuntimeException("SyntaxError: " + message + " (line " + Cur.Line + ")");

        public JsProgram ParseProgram()
        {
            var p = new JsProgram();
            while (!AtEnd)
            {
                if (Is(";")) { Take(); continue; }
                p.Body.Add(ParseStatement());
            }
            return p;
        }

        // ————————————————— 语句 —————————————————

        JsNode ParseStatement()
        {
            int line = Cur.Line, col = Cur.Column;
            JsNode node;

            if (Is("{")) node = ParseBlock();
            else if (IsKeyword("var") || IsVarLike()) node = ParseVarStatement();
            else if (IsKeyword("function")) node = ParseFunctionDecl();
            else if (IsKeyword("if")) node = ParseIf();
            else if (IsKeyword("for")) node = ParseFor();
            else if (IsKeyword("while")) node = ParseWhile();
            else if (IsKeyword("do")) node = ParseDoWhile();
            else if (IsKeyword("return"))
            {
                Take();
                JsNode v = null;
                if (!Is(";") && !Is("}") && !AtEnd) v = ParseExpression();
                MatchSemicolon();
                node = new JsReturn { Value = v };
            }
            else if (IsKeyword("break")) { Take(); MatchSemicolon(); node = new JsBreak(); }
            else if (IsKeyword("continue")) { Take(); MatchSemicolon(); node = new JsContinue(); }
            else if (IsKeyword("throw")) { Take(); node = new JsThrow { Value = ParseExpression() }; MatchSemicolon(); }
            else if (IsKeyword("switch")) node = ParseSwitch();
            else if (IsKeyword("try")) node = ParseTry();
            else if (Is(";")) { Take(); node = new JsEmpty(); }
            else
            {
                var e = ParseExpression();
                MatchSemicolon();
                node = new JsExprStmt { Expr = e };
            }

            node.Line = line;
            node.Column = col;
            return node;
        }

        // let/const 当作 var：源端 12 个脚本用到，语义差异对本项目无影响。
        bool IsVarLike() => IsIdentifier && (Cur.Text == "let" || Cur.Text == "const") && Peek(1).Kind == JsTokenKind.Identifier;

        void MatchSemicolon()
        {
            if (Is(";")) Take();
        }

        JsBlock ParseBlock()
        {
            var block = new JsBlock { Line = Cur.Line };
            Expect("{");
            while (!AtEnd && !Is("}"))
            {
                if (Is(";")) { Take(); continue; }
                block.Body.Add(ParseStatement());
            }
            Expect("}");
            return block;
        }

        JsNode ParseVarStatement()
        {
            var decl = ParseVarDecl(true);
            MatchSemicolon();
            return decl;
        }

        JsVarDecl ParseVarDecl(bool consumeKeyword)
        {
            var decl = new JsVarDecl { Line = Cur.Line };
            if (consumeKeyword) Take();           // var / let / const
            do
            {
                var nameToken = Take();
                if (nameToken.Kind != JsTokenKind.Identifier) throw Error("invalid variable name '" + nameToken.Text + "'");
                JsNode init = null;
                if (Is("=")) { Take(); init = ParseAssignment(); }
                decl.Decls.Add(new JsVarDeclarator { Name = nameToken.Text, Init = init });
            }
            while (Is(",") && (Take().Text == ","));
            return decl;
        }

        JsFunctionDecl ParseFunctionDecl()
        {
            var node = new JsFunctionDecl { Line = Cur.Line };
            Expect("function");
            var name = Take();
            if (name.Kind != JsTokenKind.Identifier) throw Error("function name expected");
            node.Name = name.Text;
            node.Parameters = ParseParameters();
            node.Body = ParseBlock();
            return node;
        }

        string[] ParseParameters()
        {
            var list = new List<string>();
            Expect("(");
            while (!AtEnd && !Is(")"))
            {
                var t = Take();
                if (t.Kind != JsTokenKind.Identifier) throw Error("parameter name expected");
                list.Add(t.Text);
                if (Is(",")) Take();
            }
            Expect(")");
            return list.ToArray();
        }

        JsNode ParseIf()
        {
            Expect("if");
            Expect("(");
            var test = ParseExpression();
            Expect(")");
            var then = ParseStatement();
            JsNode els = null;
            if (IsKeyword("else")) { Take(); els = ParseStatement(); }
            return new JsIf { Test = test, Then = then, Else = els };
        }

        JsNode ParseFor()
        {
            Expect("for");
            Expect("(");

            JsNode init = null;
            if (IsKeyword("var") || IsVarLike())
            {
                var decl = ParseVarDecl(true);
                if (IsKeyword("in"))
                {
                    Take();
                    var obj = ParseExpression();
                    Expect(")");
                    var body = ParseStatement();
                    return new JsForIn
                    {
                        VarName = decl.Decls.Count > 0 ? decl.Decls[0].Name : "i",
                        ObjectExpr = obj, Body = body,
                    };
                }
                init = decl;
            }
            else if (!Is(";"))
            {
                var e = ParseExpression();
                if (IsKeyword("in"))
                {
                    Take();
                    var obj = ParseExpression();
                    Expect(")");
                    var body = ParseStatement();
                    return new JsForIn { Target = e, ObjectExpr = obj, Body = body };
                }
                init = new JsExprStmt { Expr = e };
            }

            Expect(";");
            JsNode test = Is(";") ? null : ParseExpression();
            Expect(";");
            JsNode update = Is(")") ? null : ParseExpression();
            Expect(")");
            var loopBody = ParseStatement();
            return new JsFor { Init = init, Test = test, Update = update, Body = loopBody };
        }

        JsNode ParseWhile()
        {
            Expect("while"); Expect("(");
            var test = ParseExpression();
            Expect(")");
            return new JsWhile { Test = test, Body = ParseStatement() };
        }

        JsNode ParseDoWhile()
        {
            Expect("do");
            var body = ParseStatement();
            Expect("while"); Expect("(");
            var test = ParseExpression();
            Expect(")");
            MatchSemicolon();
            return new JsDoWhile { Test = test, Body = body };
        }

        JsNode ParseSwitch()
        {
            Expect("switch"); Expect("(");
            var disc = ParseExpression();
            Expect(")"); Expect("{");
            var node = new JsSwitch { Discriminant = disc };
            JsSwitchCase current = null;
            while (!AtEnd && !Is("}"))
            {
                if (IsKeyword("case"))
                {
                    Take();
                    var test = ParseExpression();
                    Expect(":");
                    current = new JsSwitchCase { Test = test };
                    node.Cases.Add(current);
                }
                else if (IsKeyword("default"))
                {
                    Take();
                    Expect(":");
                    current = new JsSwitchCase { Test = null };
                    node.Cases.Add(current);
                }
                else
                {
                    if (current == null) throw Error("case expected");
                    if (Is(";")) { Take(); continue; }
                    current.Body.Add(ParseStatement());
                }
            }
            Expect("}");
            return node;
        }

        JsNode ParseTry()
        {
            Expect("try");
            var node = new JsTry { TryBlock = ParseBlock() };
            if (IsKeyword("catch"))
            {
                Take(); Expect("(");
                var name = Take();
                node.CatchName = name.Text;
                Expect(")");
                node.CatchBlock = ParseBlock();
            }
            if (IsKeyword("finally"))
            {
                Take();
                node.FinallyBlock = ParseBlock();
            }
            return node;
        }

        // ————————————————— 表达式 —————————————————

        public JsNode ParseExpression()
        {
            var first = ParseAssignment();
            if (!Is(",")) return first;
            var seq = new JsSequence();
            seq.Items.Add(first);
            while (Is(",")) { Take(); seq.Items.Add(ParseAssignment()); }
            return seq;
        }

        JsNode ParseAssignment()
        {
            var left = ParseConditional();
            string op = Cur.Text;
            if (Cur.Kind == JsTokenKind.Punct &&
                (op == "=" || op == "+=" || op == "-=" || op == "*=" || op == "/=" || op == "%=" ||
                 op == "<<=" || op == ">>=" || op == ">>>=" || op == "&=" || op == "|=" || op == "^="))
            {
                Take();
                var right = ParseAssignment();
                return new JsAssign { Op = op, Target = left, Value = right };
            }
            return left;
        }

        JsNode ParseConditional()
        {
            var test = ParseBinary(1);
            if (!Is("?")) return test;
            Take();
            var then = ParseAssignment();
            Expect(":");
            var els = ParseAssignment();
            return new JsConditional { Test = test, Then = then, Else = els };
        }

        static int Precedence(string op)
        {
            switch (op)
            {
                case "||": return 1;
                case "&&": return 2;
                case "|": return 3;
                case "^": return 4;
                case "&": return 5;
                case "==": case "!=": case "===": case "!==": return 6;
                case "<": case ">": case "<=": case ">=": case "instanceof": case "in": return 7;
                case "<<": case ">>": case ">>>": return 8;
                case "+": case "-": return 9;
                case "*": case "/": case "%": return 10;
            }
            return -1;
        }

        JsNode ParseBinary(int minPrecedence)
        {
            var left = ParseUnary();
            while (true)
            {
                string op = Cur.Text;
                bool isOp = Cur.Kind == JsTokenKind.Punct || (Cur.Kind == JsTokenKind.Keyword && (op == "instanceof" || op == "in"));
                int prec = isOp ? Precedence(op) : -1;
                if (prec < minPrecedence || prec < 0) return left;
                Take();
                var right = ParseBinary(prec + 1);
                left = (op == "&&" || op == "||")
                    ? (JsNode)new JsLogical { Op = op, Left = left, Right = right }
                    : new JsBinary { Op = op, Left = left, Right = right };
            }
        }

        JsNode ParseUnary()
        {
            if (Cur.Kind == JsTokenKind.Keyword &&
                (Cur.Text == "typeof" || Cur.Text == "void" || Cur.Text == "delete"))
            {
                string op = Take().Text;
                return new JsUnary { Op = op, Operand = ParseUnary() };
            }
            if (Cur.Kind == JsTokenKind.Punct && (Cur.Text == "!" || Cur.Text == "~" || Cur.Text == "+" || Cur.Text == "-"))
            {
                string op = Take().Text;
                return new JsUnary { Op = op, Operand = ParseUnary() };
            }
            if (Cur.Kind == JsTokenKind.Punct && (Cur.Text == "++" || Cur.Text == "--"))
            {
                string op = Take().Text;
                return new JsUpdate { Increment = op == "++", Prefix = true, Target = ParseUnary() };
            }
            if (IsKeyword("new")) return ParseNew();
            return ParsePostfix();
        }

        JsNode ParseNew()
        {
            Expect("new");
            JsNode callee = ParsePrimary();
            while (!AtEnd && (Is(".") || Is("[")))
            {
                if (Is(".")) { Take(); var name = Take(); callee = new JsMember { Object = callee, Name = name.Text }; }
                else { Take(); var prop = ParseExpression(); Expect("]"); callee = new JsMember { Object = callee, Property = prop }; }
            }
            var node = new JsNew { Callee = callee };
            if (Is("("))
            {
                Take();
                while (!AtEnd && !Is(")"))
                {
                    node.Args.Add(ParseAssignment());
                    if (Is(",")) Take();
                }
                Expect(")");
            }
            // new X().y / new X()[0] 之类的后续链
            return ParsePostfixTail(node);
        }

        JsNode ParsePostfix()
        {
            var expr = ParsePrimary();
            expr = ParsePostfixTail(expr);
            if (Cur.Kind == JsTokenKind.Punct && (Cur.Text == "++" || Cur.Text == "--"))
            {
                string op = Take().Text;
                return new JsUpdate { Increment = op == "++", Prefix = false, Target = expr };
            }
            return expr;
        }

        JsNode ParsePostfixTail(JsNode expr)
        {
            while (!AtEnd)
            {
                if (Is("."))
                {
                    Take();
                    var name = Take();
                    expr = new JsMember { Object = expr, Name = name.Text };
                }
                else if (Is("["))
                {
                    Take();
                    var prop = ParseExpression();
                    Expect("]");
                    expr = new JsMember { Object = expr, Property = prop };
                }
                else if (Is("("))
                {
                    Take();
                    var call = new JsCallExpr { Callee = expr };
                    while (!AtEnd && !Is(")"))
                    {
                        call.Args.Add(ParseAssignment());
                        if (Is(",")) Take();
                    }
                    Expect(")");
                    expr = call;
                }
                else break;
            }
            return expr;
        }

        JsNode ParsePrimary()
        {
            var t = Cur;
            if (t.Kind == JsTokenKind.Number)
            {
                Take();
                return new JsLiteral { Value = new JsNumber(t.Number) };
            }
            if (t.Kind == JsTokenKind.String)
            {
                Take();
                return new JsLiteral { Value = new JsString(t.Text) };
            }
            if (t.Kind == JsTokenKind.Template)
            {
                Take();
                return BuildTemplate(t.Text);
            }
            if (t.Kind == JsTokenKind.Keyword)
            {
                switch (t.Text)
                {
                    case "true": Take(); return new JsLiteral { Value = JsBoolean.True };
                    case "false": Take(); return new JsLiteral { Value = JsBoolean.False };
                    case "null": Take(); return new JsLiteral { Value = JsNull.Value };
                    case "this": Take(); return new JsThisExpr();
                    case "function": return ParseFunctionExpr();
                }
            }
            if (t.Kind == JsTokenKind.Identifier)
            {
                Take();
                return new JsIdentifier { Name = t.Text };
            }
            if (t.Kind == JsTokenKind.Punct)
            {
                if (t.Text == "(")
                {
                    Take();
                    var e = ParseExpression();
                    Expect(")");
                    return e;
                }
                if (t.Text == "[")
                {
                    Take();
                    var arr = new JsArrayLiteral();
                    while (!AtEnd && !Is("]"))
                    {
                        if (Is(",")) { Take(); arr.Items.Add(null); continue; }
                        arr.Items.Add(ParseAssignment());
                        if (Is(",")) Take();
                    }
                    Expect("]");
                    return arr;
                }
                if (t.Text == "{") return ParseObjectLiteral();
            }
            throw Error("unexpected token '" + t.Text + "'");
        }

        JsNode ParseFunctionExpr()
        {
            Expect("function");
            string name = null;
            if (Cur.Kind == JsTokenKind.Identifier) name = Take().Text;
            var parameters = ParseParameters();
            var body = ParseBlock();
            return new JsFunctionExpr { Name = name, Parameters = parameters, Body = body };
        }

        // 模板字符串 → 字符串拼接（与原端 Nashorn 的取值顺序一致）
        JsNode BuildTemplate(string raw)
        {
            JsNode chain = null;
            var literal = new System.Text.StringBuilder();
            int i = 0;
            while (i < raw.Length)
            {
                if (raw[i] == '$' && i + 1 < raw.Length && raw[i + 1] == '{')
                {
                    chain = ConcatNode(chain, new JsLiteral { Value = new JsString(UnescapeTemplate(literal.ToString())) });
                    literal.Length = 0;
                    i += 2;
                    int depth = 1, start = i;
                    while (i < raw.Length && depth > 0)
                    {
                        if (raw[i] == '{') depth++;
                        else if (raw[i] == '}') { depth--; if (depth == 0) break; }
                        i++;
                    }
                    string expr = raw.Substring(start, i - start);
                    chain = ConcatNode(chain, new JsParser(expr).ParseExpression());
                    i++;                       // 跳过 }
                }
                else
                {
                    literal.Append(raw[i]);
                    i++;
                }
            }
            if (literal.Length > 0 || chain == null)
                chain = ConcatNode(chain, new JsLiteral { Value = new JsString(UnescapeTemplate(literal.ToString())) });
            return chain;
        }

        static JsNode ConcatNode(JsNode left, JsNode right)
            => left == null ? right : new JsBinary { Op = "+", Left = left, Right = right };

        static string UnescapeTemplate(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
                char n = s[++i];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case '`': sb.Append('`'); break;
                    case '$': sb.Append('$'); break;
                    default: sb.Append(n); break;
                }
            }
            return sb.ToString();
        }

        JsNode ParseObjectLiteral()
        {
            Expect("{");
            var obj = new JsObjectLiteral();
            while (!AtEnd && !Is("}"))
            {
                if (Is(",")) { Take(); continue; }
                var key = Take();
                string name;
                if (key.Kind == JsTokenKind.String || key.Kind == JsTokenKind.Number) name = key.Text;
                else if (key.Kind == JsTokenKind.Identifier || key.Kind == JsTokenKind.Keyword) name = key.Text;
                else throw Error("invalid object key");

                JsNode value;
                if (Is(":"))
                {
                    Take();
                    value = ParseAssignment();
                }
                else
                {
                    // ES5 简写不合法，但个别脚本会写 { foo } —— 按变量引用处理
                    if (key.Kind != JsTokenKind.Identifier) throw Error("'=>' expected in object literal");
                    value = new JsIdentifier { Name = name };
                }
                obj.Props.Add(new KeyValuePair<string, JsNode>(name, value));
                if (Is(",")) Take();
            }
            Expect("}");
            return obj;
        }
    }
}
