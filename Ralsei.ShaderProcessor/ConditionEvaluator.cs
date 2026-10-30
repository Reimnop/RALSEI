using System.Globalization;

namespace Ralsei.ShaderProcessor;

internal readonly record struct Macro(bool IsFunctionLike, string Body);

internal sealed class ConditionException(string message) : Exception(message);

/// <summary>Evaluates the expression of an #if or #elif like a C preprocessor: macros are expanded, defined() is resolved, the rest is an integer expression.</summary>
internal static class ConditionEvaluator
{
    private enum TokenKind { Number, Identifier, Punctuator }

    private readonly record struct Token(TokenKind Kind, string Text, long Value = 0);

    /// <param name="lookup">Finds a macro by name.</param>
    /// <param name="isKnownUndefined">Whether a compiler macro (see <see cref="ThrowIfCompilerMacro"/>) is certain to be undefined, e.g. GL_ES in a desktop shader.</param>
    public static bool IsTrue(string expression, Func<string, Macro?> lookup, Func<string, bool> isKnownUndefined)
    {
        var tokens = Expand(Tokenize(expression), lookup, isKnownUndefined, []);
        if (tokens.Count == 0)
            throw new ConditionException("#if with no expression");

        var parser = new Parser(tokens);
        var value = parser.ParseExpression(evaluate: true);
        if (!parser.AtEnd)
            throw new ConditionException($"unexpected '{parser.Current.Text}' in expression");
        return value != 0;
    }

    /// <summary>The compiler defines macros of its own (GL_ARB_..., __LINE__) the preprocessor can't know; treating one as undefined would be a silent lie, so asking about an unknown one is an error.</summary>
    public static void ThrowIfCompilerMacro(string name)
    {
        if (name.StartsWith("GL_", StringComparison.Ordinal) || name.StartsWith("__", StringComparison.Ordinal))
            throw new ConditionException($"'{name}' is defined by the GLSL compiler, so it can't be used in a condition");
    }

    public static bool IsIdentifier(string text)
    {
        if (text.Length == 0 || !IsIdentifierStart(text[0]))
            return false;
        foreach (var c in text)
            if (!IsIdentifierPart(c))
                return false;
        return true;
    }

    private static bool IsIdentifierStart(char c) => c == '_' || char.IsAsciiLetter(c);
    private static bool IsIdentifierPart(char c) => c == '_' || char.IsAsciiLetterOrDigit(c);

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (char.IsAsciiDigit(c))
            {
                var start = i;
                while (i < text.Length && IsIdentifierPart(text[i]))
                    i++;
                var literal = text[start..i];
                tokens.Add(new Token(TokenKind.Number, literal, ParseNumber(literal)));
            }
            else if (IsIdentifierStart(c))
            {
                var start = i;
                while (i < text.Length && IsIdentifierPart(text[i]))
                    i++;
                tokens.Add(new Token(TokenKind.Identifier, text[start..i]));
            }
            else
            {
                var two = i + 1 < text.Length ? text.Substring(i, 2) : null;
                if (two is "&&" or "||" or "==" or "!=" or "<=" or ">=" or "<<" or ">>")
                {
                    tokens.Add(new Token(TokenKind.Punctuator, two));
                    i += 2;
                }
                else if ("!~+-*/%<>&^|?:()".Contains(c))
                {
                    tokens.Add(new Token(TokenKind.Punctuator, c.ToString()));
                    i++;
                }
                else
                {
                    throw new ConditionException($"unexpected '{c}' in expression");
                }
            }
        }

        return tokens;
    }

    private static long ParseNumber(string literal)
    {
        var digits = literal.TrimEnd('u', 'U');
        try
        {
            if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return long.Parse(digits[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            if (digits.Length > 1 && digits[0] == '0')
                return Convert.ToInt64(digits, 8);
            return long.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentException)
        {
            throw new ConditionException($"'{literal}' is not a valid integer");
        }
    }

    private static List<Token> Expand(List<Token> tokens, Func<string, Macro?> lookup, Func<string, bool> isKnownUndefined, HashSet<string> hidden)
    {
        var result = new List<Token>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Kind != TokenKind.Identifier)
            {
                result.Add(token);
                continue;
            }

            if (token.Text == "defined")
            {
                var parenthesized = i + 1 < tokens.Count && tokens[i + 1].Text == "(";
                var nameIndex = parenthesized ? i + 2 : i + 1;
                if (nameIndex >= tokens.Count || tokens[nameIndex].Kind != TokenKind.Identifier)
                    throw new ConditionException("defined needs a macro name");
                if (parenthesized && (nameIndex + 1 >= tokens.Count || tokens[nameIndex + 1].Text != ")"))
                    throw new ConditionException("defined( is missing its ')'");

                var isDefined = lookup(tokens[nameIndex].Text) is not null;
                if (!isDefined && !isKnownUndefined(tokens[nameIndex].Text))
                    ThrowIfCompilerMacro(tokens[nameIndex].Text);
                result.Add(new Token(TokenKind.Number, isDefined ? "1" : "0", isDefined ? 1 : 0));
                i = parenthesized ? nameIndex + 1 : nameIndex;
                continue;
            }

            if (hidden.Contains(token.Text))
            {
                result.Add(token);
                continue;
            }

            var macro = lookup(token.Text);
            if (macro is null)
            {
                if (!isKnownUndefined(token.Text))
                    ThrowIfCompilerMacro(token.Text);
                result.Add(new Token(TokenKind.Number, "0"));
                continue;
            }

            if (macro.Value.IsFunctionLike)
                throw new ConditionException($"function-like macro '{token.Text}' can't be used in a condition");

            result.AddRange(Expand(Tokenize(macro.Value.Body), lookup, isKnownUndefined, [..hidden, token.Text]));
        }

        return result;
    }

    private sealed class Parser(List<Token> tokens)
    {
        private static readonly string[][] Levels =
        [
            ["||"], ["&&"], ["|"], ["^"], ["&"], ["==", "!="], ["<", ">", "<=", ">="], ["<<", ">>"], ["+", "-"], ["*", "/", "%"]
        ];

        private int position;

        public bool AtEnd => position >= tokens.Count;
        public Token Current => tokens[position];

        private bool Match(string punctuator)
        {
            if (AtEnd || Current.Kind != TokenKind.Punctuator || Current.Text != punctuator)
                return false;
            position++;
            return true;
        }

        // evaluate == false parses an operand that is not taken (e.g. the right side of 0 && x),
        // where a division by zero is not an error
        public long ParseExpression(bool evaluate)
        {
            var condition = ParseBinary(0, evaluate);
            if (!Match("?"))
                return condition;

            var whenTrue = ParseExpression(evaluate && condition != 0);
            if (!Match(":"))
                throw new ConditionException("'?' is missing its ':'");
            var whenFalse = ParseExpression(evaluate && condition == 0);
            return condition != 0 ? whenTrue : whenFalse;
        }

        private long ParseBinary(int level, bool evaluate)
        {
            if (level == Levels.Length)
                return ParseUnary(evaluate);

            var left = ParseBinary(level + 1, evaluate);
            while (!AtEnd && Current.Kind == TokenKind.Punctuator && Array.IndexOf(Levels[level], Current.Text) >= 0)
            {
                var op = Current.Text;
                position++;

                var leftIsDecisive = op == "||" ? left != 0 : op == "&&" && left == 0;
                var right = ParseBinary(level + 1, evaluate && !leftIsDecisive);
                left = Apply(op, left, right, evaluate);
            }

            return left;
        }

        private static long Apply(string op, long left, long right, bool evaluate)
        {
            switch (op)
            {
                case "||": return left != 0 || right != 0 ? 1 : 0;
                case "&&": return left != 0 && right != 0 ? 1 : 0;
                case "|": return left | right;
                case "^": return left ^ right;
                case "&": return left & right;
                case "==": return left == right ? 1 : 0;
                case "!=": return left != right ? 1 : 0;
                case "<": return left < right ? 1 : 0;
                case ">": return left > right ? 1 : 0;
                case "<=": return left <= right ? 1 : 0;
                case ">=": return left >= right ? 1 : 0;
                case "<<": return left << (int) (right & 63);
                case ">>": return left >> (int) (right & 63);
                case "+": return left + right;
                case "-": return left - right;
                case "*": return left * right;
                default: // "/" and "%"
                    if (right == 0)
                    {
                        if (evaluate)
                            throw new ConditionException("division by zero in expression");
                        return 0;
                    }
                    return op == "/" ? left / right : left % right;
            }
        }

        private long ParseUnary(bool evaluate)
        {
            if (Match("!")) return ParseUnary(evaluate) == 0 ? 1 : 0;
            if (Match("~")) return ~ParseUnary(evaluate);
            if (Match("-")) return -ParseUnary(evaluate);
            if (Match("+")) return ParseUnary(evaluate);

            if (AtEnd)
                throw new ConditionException("expression ends where a value was expected");

            if (Match("("))
            {
                var value = ParseExpression(evaluate);
                if (!Match(")"))
                    throw new ConditionException("missing ')' in expression");
                return value;
            }

            var token = Current;
            if (token.Kind == TokenKind.Number)
            {
                position++;
                return token.Value;
            }

            // An identifier that survived expansion is a macro that was hidden to stop recursion
            throw new ConditionException($"unexpected '{token.Text}' in expression");
        }
    }
}
