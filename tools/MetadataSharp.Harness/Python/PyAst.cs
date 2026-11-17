using System.Globalization;
using System.Text;

namespace MetadataSharp.Harness.Python;

// SyntaxError from ast.parse: the message reads as str(SyntaxError) does, "msg (<unknown>, line N)".
internal sealed class PySyntaxError(string message, int line) : Exception($"{message} (<unknown>, line {line})");

// The part of Python's ast module the ICC gate reads exiv2's own test files with: ast.parse into the
// statements (modules, classes, functions, for loops, single-name assignments and every other compound
// statement with its bodies), ast.walk in its breadth-first order, and of an expression the shapes the
// gate inspects: str, int and other constants, list and tuple displays, list comprehensions, and
// ast.unparse as far as a containment test needs it. Nothing is executed.
internal static class PyAst
{
    // ----- tokens -----

    public enum TokenKind
    {
        Name,
        Number,
        String,
        Op,
        Newline,
        Indent,
        Dedent,
        End,
    }

    public sealed record Token(TokenKind Kind, string Text, int Line)
    {
        public bool Is(string text) => Kind is TokenKind.Op or TokenKind.Name && Text == text;
    }

    private static readonly string[] Operators =
    [
        "**=", "//=", ">>=", "<<=", "...", "->", ":=", "**", "//", "<<", ">>", "<=", ">=", "==", "!=", "+=", "-=", "*=", "/=",
        "%=", "&=", "|=", "^=", "@=", "+", "-", "*", "/", "%", "&", "|", "^", "~", "<", ">", "(", ")", "[", "]", "{", "}",
        ",", ":", ";", ".", "=", "@", "!",
    ];

    /// <summary>The tokens of a module, with NEWLINE, INDENT and DEDENT as Python's tokenizer emits them.</summary>
    public static List<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        // The indentation of each open block, with tabs to the next multiple of 8 and, as CPython checks
        // for consistency, with tabs counted as one column.
        var indents = new Stack<(int Width, int Alternative)>();
        indents.Push((0, 0));
        var brackets = new Stack<(char Bracket, int Line)>();
        int i = 0, line = 1;
        bool lineStart = true;
        while (true)
        {
            if (lineStart)
            {
                lineStart = false;
                if (brackets.Count == 0)
                {
                    int width = 0, alternative = 0, j = i;
                    while (j < source.Length && source[j] is ' ' or '\t' or '\f')
                    {
                        width = source[j] switch { ' ' => width + 1, '\t' => (width / 8 + 1) * 8, _ => 0 };
                        alternative = source[j] switch { ' ' or '\t' => alternative + 1, _ => 0 };
                        j++;
                    }
                    if (j >= source.Length)
                    {
                        i = j;
                        break;
                    }
                    // A blank or comment-only line ends no statement and changes no indentation.
                    if (source[j] is '\n' or '#')
                    {
                        while (j < source.Length && source[j] != '\n') j++;
                        i = j < source.Length ? j + 1 : j;
                        if (j < source.Length) line++;
                        lineStart = true;
                        continue;
                    }
                    i = j;
                    const string inconsistent = "inconsistent use of tabs and spaces in indentation";
                    if (width == indents.Peek().Width)
                    {
                        if (alternative != indents.Peek().Alternative) throw new PySyntaxError(inconsistent, line);
                    }
                    else if (width > indents.Peek().Width)
                    {
                        if (alternative <= indents.Peek().Alternative) throw new PySyntaxError(inconsistent, line);
                        indents.Push((width, alternative));
                        tokens.Add(new Token(TokenKind.Indent, "", line));
                    }
                    else
                    {
                        while (indents.Count > 1 && width < indents.Peek().Width)
                        {
                            indents.Pop();
                            tokens.Add(new Token(TokenKind.Dedent, "", line));
                        }
                        if (width != indents.Peek().Width) throw new PySyntaxError("unindent does not match any outer indentation level", line);
                        if (alternative != indents.Peek().Alternative) throw new PySyntaxError(inconsistent, line);
                    }
                }
            }
            if (i >= source.Length) break;
            char c = source[i];
            if (c is ' ' or '\t' or '\f')
            {
                i++;
                continue;
            }
            if (c == '#')
            {
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }
            if (c == '\\')
            {
                if (i + 1 < source.Length && source[i + 1] == '\n')
                {
                    i += 2;
                    line++;
                    continue;
                }
                throw new PySyntaxError("unexpected character after line continuation character", line);
            }
            if (c == '\n')
            {
                if (brackets.Count == 0 && tokens.Count > 0 && tokens[^1].Kind is not (TokenKind.Newline or TokenKind.Indent or TokenKind.Dedent))
                    tokens.Add(new Token(TokenKind.Newline, "", line));
                i++;
                line++;
                lineStart = true;
                continue;
            }
            int start = i, startLine = line;
            if (StringStart(source, i, out int quoteAt))
            {
                i = ScanString(source, quoteAt, ref line);
                tokens.Add(new Token(TokenKind.String, source[start..i], startLine));
                continue;
            }
            if (char.IsDigit(c) || (c == '.' && i + 1 < source.Length && char.IsDigit(source[i + 1])))
            {
                bool hex = c == '0' && i + 1 < source.Length && source[i + 1] is 'x' or 'X';
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] is '_' or '.'
                                             || (!hex && source[i] is '+' or '-' && source[i - 1] is 'e' or 'E')))
                    i++;
                tokens.Add(new Token(TokenKind.Number, source[start..i], startLine));
                continue;
            }
            if (char.IsLetter(c) || c == '_' || c > 0x7F)
            {
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_' || (source[i] > 0x7F && !char.IsWhiteSpace(source[i])))) i++;
                tokens.Add(new Token(TokenKind.Name, source[start..i], startLine));
                continue;
            }
            string? op = Operators.FirstOrDefault(candidate => string.CompareOrdinal(source, i, candidate, 0, candidate.Length) == 0);
            if (op is null) throw new PySyntaxError($"invalid character '{c}' (U+{(int)c:X4})", line);
            if (op is "(" or "[" or "{") brackets.Push((op[0], line));
            else if (op is ")" or "]" or "}")
            {
                char open = op == ")" ? '(' : op == "]" ? '[' : '{';
                if (brackets.Count == 0) throw new PySyntaxError($"unmatched '{op}'", line);
                if (brackets.Peek().Bracket != open)
                {
                    string where = brackets.Peek().Line != line ? $" on line {brackets.Peek().Line}" : "";
                    throw new PySyntaxError($"closing parenthesis '{op}' does not match opening parenthesis '{brackets.Peek().Bracket}'{where}", line);
                }
                brackets.Pop();
            }
            tokens.Add(new Token(TokenKind.Op, op, startLine));
            i += op.Length;
        }
        if (brackets.Count > 0) throw new PySyntaxError($"'{brackets.Peek().Bracket}' was never closed", brackets.Peek().Line);
        if (tokens.Count > 0 && tokens[^1].Kind is not (TokenKind.Newline or TokenKind.Dedent or TokenKind.Indent))
            tokens.Add(new Token(TokenKind.Newline, "", line));
        while (indents.Count > 1)
        {
            indents.Pop();
            tokens.Add(new Token(TokenKind.Dedent, "", line));
        }
        tokens.Add(new Token(TokenKind.End, "", line));
        return tokens;
    }

    // Whether a string literal (with its prefix) starts at i; quoteAt is the index of its first quote.
    private static bool StringStart(string source, int i, out int quoteAt)
    {
        int j = i;
        while (j < source.Length && j - i < 2 && "rRbBuUfF".Contains(source[j])) j++;
        quoteAt = j;
        if (j >= source.Length || source[j] is not ('\'' or '"')) return false;
        string prefix = source[i..j].ToLowerInvariant();
        return prefix is "" or "r" or "u" or "b" or "f" or "br" or "rb" or "fr" or "rf";
    }

    private static int ScanString(string source, int quoteAt, ref int line)
    {
        string prefix = source[..quoteAt].Length > 0 ? PrefixOf(source, quoteAt) : "";
        bool formatted = prefix.Contains('f');
        char quote = source[quoteAt];
        bool triple = quoteAt + 2 < source.Length && source[quoteAt + 1] == quote && source[quoteAt + 2] == quote;
        int startLine = line;
        int i = quoteAt + (triple ? 3 : 1);
        int braces = 0;
        while (true)
        {
            if (i >= source.Length)
            {
                // The tokenizer counts the lines it has read: a final line end starts no line of its own.
                int detected = source.EndsWith('\n') ? line - 1 : line;
                throw triple
                    ? new PySyntaxError($"unterminated triple-quoted string literal (detected at line {detected})", startLine)
                    : new PySyntaxError($"unterminated string literal (detected at line {startLine})", startLine);
            }
            char c = source[i];
            if (formatted && braces > 0)
            {
                // Inside a replacement field of an f-string, a nested literal may use any quote.
                if (StringStart(source, i, out int nested) && (i == 0 || !char.IsLetterOrDigit(source[i - 1]) || nested == i))
                {
                    i = ScanString(source, nested, ref line);
                    continue;
                }
                if (c == '{') braces++;
                else if (c == '}') braces--;
                else if (c == '\n') line++;
                i++;
                continue;
            }
            if (c == '\\')
            {
                if (i + 1 < source.Length && source[i + 1] == '\n') line++;
                i += 2;
                continue;
            }
            if (c == '\n')
            {
                if (!triple) throw new PySyntaxError($"unterminated string literal (detected at line {startLine})", startLine);
                line++;
                i++;
                continue;
            }
            if (formatted && c == '{')
            {
                if (i + 1 < source.Length && source[i + 1] == '{') { i += 2; continue; }
                braces++;
                i++;
                continue;
            }
            if (c == quote && (!triple || (i + 2 < source.Length && source[i + 1] == quote && source[i + 2] == quote)))
                return i + (triple ? 3 : 1);
            i++;
        }
    }

    private static string PrefixOf(string source, int quoteAt)
    {
        int j = quoteAt;
        while (j > 0 && quoteAt - j < 2 && "rRbBuUfF".Contains(source[j - 1])) j--;
        return source[j..quoteAt].ToLowerInvariant();
    }

    // ----- statements -----

    // A node of the statement tree; ast.walk visits these in breadth-first order.
    public abstract class Node
    {
        public List<Node> Children { get; } = [];
    }

    public sealed class Module : Node;

    public sealed class ClassDef(string name) : Node
    {
        public string Name { get; } = name;
    }

    public sealed class FunctionDef(string name) : Node
    {
        public string Name { get; } = name;
    }

    public sealed class For(Expr target, Expr iter) : Node
    {
        public Expr Target { get; } = target;

        public Expr Iter { get; } = iter;
    }

    /// <summary>An assignment to one name, the only kind the gate looks at.</summary>
    public sealed class Assign(string target, Expr value) : Node
    {
        public string Target { get; } = target;

        public Expr Value { get; } = value;
    }

    /// <summary>Any other statement: if, while, with, try and its handlers, and every simple statement.</summary>
    public sealed class Other : Node;

    /// <summary>
    /// ast.parse(text): the statement tree, or PySyntaxError. As CPython's tokenizer does for source given as a
    /// string, "\r\n" and a lone "\r" end a line like "\n" (translate_newlines), so a file checked out with
    /// Windows line endings parses as it does elsewhere.
    /// </summary>
    public static Module Parse(string text)
    {
        var parser = new StatementParser(Tokenize(text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')));
        var module = new Module();
        parser.Block(module, untilDedent: false);
        return module;
    }

    /// <summary>ast.walk(node): the node and everything below it, breadth first.</summary>
    public static IEnumerable<Node> Walk(Node node)
    {
        var todo = new Queue<Node>();
        todo.Enqueue(node);
        while (todo.Count > 0)
        {
            var current = todo.Dequeue();
            foreach (var child in current.Children) todo.Enqueue(child);
            yield return current;
        }
    }

    private sealed class StatementParser(List<Token> tokens)
    {
        private int position;

        private Token Peek => tokens[position];

        public void Block(Node parent, bool untilDedent)
        {
            while (true)
            {
                var token = Peek;
                if (token.Kind == TokenKind.End) return;
                if (token.Kind == TokenKind.Dedent)
                {
                    if (!untilDedent) throw new PySyntaxError("unexpected unindent", token.Line);
                    position++;
                    return;
                }
                if (token.Kind == TokenKind.Indent) throw new PySyntaxError("unexpected indent", token.Line);
                if (token.Kind == TokenKind.Newline)
                {
                    position++;
                    continue;
                }
                Statement(parent.Children);
            }
        }

        private void Statement(List<Node> into)
        {
            var first = Peek;
            if (first.Kind == TokenKind.Op && first.Text == "@")
            {
                // Decorators: expressions on lines of their own before a def or class.
                while (Peek.Is("@")) SkipLine();
                Statement(into);
                return;
            }
            if (first.Kind == TokenKind.Name)
            {
                switch (first.Text)
                {
                    case "if":
                        into.Add(IfStatement());
                        return;
                    case "while":
                    {
                        var node = new Other();
                        Clause(node.Children);
                        if (Peek.Is("else")) Clause(node.Children);
                        into.Add(node);
                        return;
                    }
                    case "for":
                    {
                        var header = Header();
                        int inAt = TopLevelIndex(header, 1, token => token.Is("in"));
                        if (inAt < 0) throw new PySyntaxError("invalid syntax", first.Line);
                        var node = new For(Expression(header[1..inAt], allowTuple: true), Expression(header[(inAt + 1)..], allowTuple: true));
                        Body(node.Children);
                        if (Peek.Is("else")) Clause(node.Children);
                        into.Add(node);
                        return;
                    }
                    case "try":
                    {
                        var node = new Other();
                        Clause(node.Children);
                        while (Peek.Is("except"))
                        {
                            var handler = new Other();
                            Clause(handler.Children);
                            node.Children.Add(handler);
                        }
                        if (Peek.Is("else")) Clause(node.Children);
                        if (Peek.Is("finally")) Clause(node.Children);
                        into.Add(node);
                        return;
                    }
                    case "with":
                    case "async":
                    {
                        var node = new Other();
                        Clause(node.Children);
                        into.Add(node);
                        return;
                    }
                    case "def":
                    {
                        var header = Header();
                        var node = new FunctionDef(header.Count > 1 ? header[1].Text : "");
                        Body(node.Children);
                        into.Add(node);
                        return;
                    }
                    case "class":
                    {
                        var header = Header();
                        var node = new ClassDef(header.Count > 1 ? header[1].Text : "");
                        Body(node.Children);
                        into.Add(node);
                        return;
                    }
                }
            }
            SimpleStatements(into);
        }

        // if / elif / else: an elif is an if statement of its own, inside the else branch.
        private Other IfStatement()
        {
            var node = new Other();
            Clause(node.Children);
            if (Peek.Is("elif")) node.Children.Add(IfStatement());
            else if (Peek.Is("else")) Clause(node.Children);
            return node;
        }

        private void Clause(List<Node> into)
        {
            Header();
            Body(into);
        }

        // The tokens of a compound statement's header, up to the colon that ends it (consumed).
        private List<Token> Header()
        {
            var header = new List<Token>();
            int depth = 0, lambdas = 0;
            while (true)
            {
                var token = Peek;
                if (token.Kind is TokenKind.Newline or TokenKind.End) throw new PySyntaxError("expected ':'", token.Line);
                position++;
                if (token.Kind == TokenKind.Op)
                {
                    if (token.Text is "(" or "[" or "{") depth++;
                    else if (token.Text is ")" or "]" or "}") depth--;
                    else if (token.Text == ":" && depth == 0)
                    {
                        if (lambdas == 0) return header;
                        lambdas--;
                    }
                }
                else if (token.Is("lambda") && depth == 0)
                {
                    lambdas++;
                }
                header.Add(token);
            }
        }

        // The body after a header's colon: an indented block, or simple statements on the same line.
        private void Body(List<Node> into)
        {
            if (Peek.Kind == TokenKind.Newline)
            {
                position++;
                if (Peek.Kind != TokenKind.Indent) throw new PySyntaxError("expected an indented block", Peek.Line);
                position++;
                var holder = new Other();
                Block(holder, untilDedent: true);
                into.AddRange(holder.Children);
                return;
            }
            SimpleStatements(into);
        }

        private void SkipLine()
        {
            while (Peek.Kind is not (TokenKind.Newline or TokenKind.End)) position++;
            if (Peek.Kind == TokenKind.Newline) position++;
        }

        private void SimpleStatements(List<Node> into)
        {
            var line = new List<Token>();
            while (Peek.Kind is not (TokenKind.Newline or TokenKind.End)) line.Add(tokens[position++]);
            if (Peek.Kind == TokenKind.Newline) position++;
            int depth = 0, start = 0;
            for (int i = 0; i <= line.Count; i++)
            {
                if (i < line.Count)
                {
                    var token = line[i];
                    if (token.Kind == TokenKind.Op && token.Text is "(" or "[" or "{") depth++;
                    else if (token.Kind == TokenKind.Op && token.Text is ")" or "]" or "}") depth--;
                    if (!(token.Is(";") && depth == 0)) continue;
                }
                if (i > start) into.Add(Simple(line[start..i]));
                start = i + 1;
            }
        }

        private static Node Simple(List<Token> statement)
        {
            // An assignment to exactly one name: NAME = value, with no further target before a lambda.
            if (statement.Count >= 3 && statement[0].Kind == TokenKind.Name && !Keywords.Contains(statement[0].Text) && statement[1].Is("="))
            {
                var value = statement[2..];
                int more = TopLevelIndex(value, 0, token => token.Is("="), stopAtLambda: true);
                if (more < 0) return new Assign(statement[0].Text, Expression(value, allowTuple: true));
            }
            return new Other();
        }
    }

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "False", "None", "True", "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif",
        "else", "except", "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "nonlocal", "not", "or",
        "pass", "raise", "return", "try", "while", "with", "yield",
    };

    // The index of the first token at bracket depth zero (from start) that matches, or -1.
    private static int TopLevelIndex(List<Token> tokens, int start, Func<Token, bool> match, bool stopAtLambda = false)
    {
        int depth = 0;
        for (int i = start; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Kind == TokenKind.Op && token.Text is "(" or "[" or "{") depth++;
            else if (token.Kind == TokenKind.Op && token.Text is ")" or "]" or "}") depth--;
            else if (depth == 0 && stopAtLambda && token.Is("lambda")) return -1;
            else if (depth == 0 && match(token)) return i;
        }
        return -1;
    }

    // ----- expressions -----

    // An expression, as far as the gate looks into one.
    public abstract class Expr(List<Token> tokens)
    {
        public List<Token> Tokens { get; } = tokens;
    }

    /// <summary>ast.Constant: a str, bytes, int, float, bool, None or Ellipsis value.</summary>
    public sealed class Constant(List<Token> tokens, object? value) : Expr(tokens)
    {
        public object? Value { get; } = value;
    }

    /// <summary>A bare name (ast.Name).</summary>
    public sealed class NameExpr(List<Token> tokens, string id) : Expr(tokens)
    {
        public string Id { get; } = id;
    }

    /// <summary>ast.List or ast.Tuple.</summary>
    public sealed class Sequence(List<Token> tokens, bool isList, List<Expr> elements) : Expr(tokens)
    {
        public bool IsList { get; } = isList;

        public List<Expr> Elements { get; } = elements;
    }

    /// <summary>ast.ListComp, with the iterable of each generator.</summary>
    public sealed class ListComp(List<Token> tokens, List<Expr> iterables) : Expr(tokens)
    {
        public List<Expr> Iterables { get; } = iterables;
    }

    /// <summary>Every other expression.</summary>
    public sealed class OtherExpr(List<Token> tokens) : Expr(tokens);

    /// <summary>The ellipsis constant.</summary>
    public static readonly object Ellipsis = new();

    private static List<List<Token>> SplitTopLevel(List<Token> tokens, out bool trailingComma)
    {
        var parts = new List<List<Token>>();
        int depth = 0, start = 0;
        trailingComma = false;
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Kind == TokenKind.Op && token.Text is "(" or "[" or "{") depth++;
            else if (token.Kind == TokenKind.Op && token.Text is ")" or "]" or "}") depth--;
            else if (depth == 0 && token.Is(","))
            {
                parts.Add(tokens[start..i]);
                start = i + 1;
            }
        }
        if (start < tokens.Count) parts.Add(tokens[start..]);
        else if (parts.Count > 0) trailingComma = true;
        return parts;
    }

    public static Expr Expression(List<Token> tokens, bool allowTuple)
    {
        if (tokens.Count == 0) throw new PySyntaxError("invalid syntax", 0);
        if (allowTuple)
        {
            var parts = SplitTopLevel(tokens, out bool trailing);
            if (parts.Count > 1 || trailing) return new Sequence(tokens, false, parts.Select(part => Expression(part, false)).ToList());
        }
        if (tokens.All(token => token.Kind == TokenKind.String))
        {
            object? value = StringValue(tokens);
            return value is FormattedString ? new OtherExpr(tokens) : new Constant(tokens, value);
        }
        if (tokens.Count == 1)
        {
            var token = tokens[0];
            if (token.Kind == TokenKind.Number) return new Constant(tokens, NumberValue(token));
            if (token.Is("True")) return new Constant(tokens, true);
            if (token.Is("False")) return new Constant(tokens, false);
            if (token.Is("None")) return new Constant(tokens, null);
            if (token.Is("...")) return new Constant(tokens, Ellipsis);
            if (token.Kind == TokenKind.Name && !Keywords.Contains(token.Text)) return new NameExpr(tokens, token.Text);
        }
        if (tokens[0].Kind == TokenKind.Op && tokens[0].Text is "(" or "[" && Matching(tokens, 0) == tokens.Count - 1)
        {
            var inner = tokens[1..^1];
            bool isList = tokens[0].Text == "[";
            if (inner.Count == 0) return new Sequence(tokens, isList, []);
            int forAt = TopLevelIndex(inner, 0, token => token.Is("for"));
            if (forAt >= 0)
            {
                if (!isList) return new OtherExpr(tokens); // a generator expression
                return new ListComp(tokens, Generators(inner, forAt));
            }
            if (isList)
            {
                var parts = SplitTopLevel(inner, out _);
                return new Sequence(tokens, true, parts.Select(part => Expression(part, false)).ToList());
            }
            return Expression(inner, allowTuple: true); // parentheses add no node
        }
        return new OtherExpr(tokens);
    }

    // The iterables of a comprehension's generators: "for target in iterable [if condition]..." repeated.
    private static List<Expr> Generators(List<Token> inner, int forAt)
    {
        var iterables = new List<Expr>();
        int at = forAt;
        while (at >= 0 && at < inner.Count)
        {
            int inAt = TopLevelIndex(inner, at + 1, token => token.Is("in"));
            if (inAt < 0) throw new PySyntaxError("invalid syntax", inner[at].Line);
            int end = TopLevelIndex(inner, inAt + 1, token => token.Is("for") || token.Is("if") || token.Is("async"));
            int next = TopLevelIndex(inner, inAt + 1, token => token.Is("for"));
            if (next > 0 && inner[next - 1].Is("async")) next--;
            iterables.Add(Expression(inner[(inAt + 1)..(end < 0 ? inner.Count : end)], allowTuple: false));
            at = next;
        }
        return iterables;
    }

    private static int Matching(List<Token> tokens, int open)
    {
        int depth = 0;
        for (int i = open; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Kind != TokenKind.Op) continue;
            if (token.Text is "(" or "[" or "{") depth++;
            else if (token.Text is ")" or "]" or "}" && --depth == 0) return i;
        }
        return -1;
    }

    private static object NumberValue(Token token)
    {
        string text = token.Text.Replace("_", "", StringComparison.Ordinal);
        if (text.EndsWith('j') || text.EndsWith('J')) return text;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return long.Parse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (text.StartsWith("0o", StringComparison.OrdinalIgnoreCase)) return Convert.ToInt64(text[2..], 8);
        if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) return Convert.ToInt64(text[2..], 2);
        if (text.Contains('.') || text.Contains('e') || text.Contains('E')) return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        return long.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    // The value of adjacent string literals: one str (or bytes) constant; an f-string makes it no constant.
    private static object? StringValue(List<Token> tokens)
    {
        var text = new StringBuilder();
        var bytes = new List<byte>();
        bool? isBytes = null;
        bool formatted = false;
        foreach (var token in tokens)
        {
            int quoteAt = 0;
            while (token.Text[quoteAt] is not ('\'' or '"')) quoteAt++;
            string prefix = token.Text[..quoteAt].ToLowerInvariant();
            bool tokenBytes = prefix.Contains('b');
            if (isBytes is not null && isBytes != tokenBytes) throw new PySyntaxError("cannot mix bytes and nonbytes literals", token.Line);
            isBytes = tokenBytes;
            if (prefix.Contains('f')) formatted = true;
            string body = token.Text[quoteAt..];
            int quoteLength = body.Length >= 6 && body[1] == body[0] && body[2] == body[0] ? 3 : 1;
            body = body[quoteLength..^quoteLength];
            string value = prefix.Contains('r') ? body : Unescape(body, tokenBytes, token.Line);
            if (tokenBytes)
            {
                foreach (char c in value)
                {
                    if (c > 0xFF) throw new PySyntaxError("bytes can only contain ASCII literal characters", token.Line);
                    bytes.Add((byte)c);
                }
            }
            else text.Append(value);
        }
        if (formatted) return new FormattedString();
        return isBytes == true ? bytes.ToArray() : text.ToString();
    }

    /// <summary>The value of an f-string: not a constant.</summary>
    public sealed class FormattedString;

    private static string Unescape(string body, bool isBytes, int line)
    {
        var builder = new StringBuilder(body.Length);
        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            if (c != '\\' || i + 1 >= body.Length)
            {
                builder.Append(c);
                continue;
            }
            char next = body[++i];
            switch (next)
            {
                case '\n': break;
                case '\\': builder.Append('\\'); break;
                case '\'': builder.Append('\''); break;
                case '"': builder.Append('"'); break;
                case 'a': builder.Append('\a'); break;
                case 'b': builder.Append('\b'); break;
                case 'f': builder.Append('\f'); break;
                case 'n': builder.Append('\n'); break;
                case 'r': builder.Append('\r'); break;
                case 't': builder.Append('\t'); break;
                case 'v': builder.Append('\v'); break;
                case >= '0' and <= '7':
                {
                    int value = 0, digits = 0;
                    while (digits < 3 && i < body.Length && body[i] is >= '0' and <= '7')
                    {
                        value = value * 8 + (body[i] - '0');
                        i++;
                        digits++;
                    }
                    i--;
                    builder.Append(Py.FromCodePoint(value));
                    break;
                }
                case 'x':
                case 'u' when !isBytes:
                case 'U' when !isBytes:
                {
                    int length = next == 'x' ? 2 : next == 'u' ? 4 : 8;
                    if (i + length >= body.Length || !body.Substring(i + 1, length).All(Uri.IsHexDigit))
                        throw new PySyntaxError($"(unicode error) truncated \\{next}{new string('X', length)} escape", line);
                    int value = int.Parse(body.Substring(i + 1, length), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    builder.Append(Py.FromCodePoint(value));
                    i += length;
                    break;
                }
                default:
                    builder.Append('\\').Append(next);
                    break;
            }
        }
        return builder.ToString();
    }

    /// <summary>ast.unparse(expression), as far as a test for a word in it needs: the tokens, each run of string literals as one repr().</summary>
    public static string Unparse(Expr expression)
    {
        var parts = new List<string>();
        var tokens = expression.Tokens;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != TokenKind.String)
            {
                parts.Add(tokens[i].Text);
                continue;
            }
            int end = i;
            while (end + 1 < tokens.Count && tokens[end + 1].Kind == TokenKind.String) end++;
            object? value = StringValue(tokens[i..(end + 1)]);
            parts.Add(value is FormattedString ? string.Join(" ", tokens[i..(end + 1)].Select(token => token.Text)) : Py.Repr(value));
            i = end;
        }
        return string.Join(" ", parts);
    }
}
