using System;
using System.Globalization;

/// <summary>
/// Tiny recursive-descent evaluator for numeric console arguments. Supports the four
/// arithmetic operators (<c>+ - * /</c>), parentheses, and unary +/-. Used by the
/// <c>int</c> and <c>float</c> argument parsers so commands can take expressions
/// like <c>time.speed 60*60</c> (Quantum-style).
///
/// Grammar:
///   expression := term (('+' | '-') term)*
///   term       := factor (('*' | '/') factor)*
///   factor     := '(' expression ')' | ('+' | '-') factor | number
///   number     := digits ('.' digits)?
/// </summary>
public static class ExpressionEvaluator
{
    public static bool TryEvaluate(string input, out double result, out string error)
    {
        result = 0;
        error = null;
        if (string.IsNullOrEmpty(input)) { error = "empty expression"; return false; }

        var p = new Parser(input);
        try
        {
            result = p.ParseExpression();
            p.SkipWhitespace();
            if (!p.AtEnd)
            {
                error = $"unexpected '{p.Peek()}' at position {p.Position}";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    sealed class Parser
    {
        readonly string _input;
        int _pos;

        public Parser(string input) { _input = input; }
        public int Position => _pos;
        public bool AtEnd => _pos >= _input.Length;
        public char Peek() => AtEnd ? '\0' : _input[_pos];

        public void SkipWhitespace()
        {
            while (!AtEnd && char.IsWhiteSpace(Peek())) _pos++;
        }

        public double ParseExpression()
        {
            double left = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (AtEnd) break;
                char c = Peek();
                if (c != '+' && c != '-') break;
                _pos++;
                double right = ParseTerm();
                left = c == '+' ? left + right : left - right;
            }
            return left;
        }

        double ParseTerm()
        {
            double left = ParseFactor();
            while (true)
            {
                SkipWhitespace();
                if (AtEnd) break;
                char c = Peek();
                if (c != '*' && c != '/') break;
                _pos++;
                double right = ParseFactor();
                if (c == '*') left *= right;
                else
                {
                    if (right == 0) throw new Exception("division by zero");
                    left /= right;
                }
            }
            return left;
        }

        double ParseFactor()
        {
            SkipWhitespace();
            if (AtEnd) throw new Exception("expected number");
            char c = Peek();

            if (c == '(')
            {
                _pos++;
                double v = ParseExpression();
                SkipWhitespace();
                if (AtEnd || Peek() != ')') throw new Exception("expected ')'");
                _pos++;
                return v;
            }
            if (c == '-')
            {
                _pos++;
                return -ParseFactor();
            }
            if (c == '+')
            {
                _pos++;
                return ParseFactor();
            }

            return ParseNumber();
        }

        double ParseNumber()
        {
            int start = _pos;
            while (!AtEnd && (char.IsDigit(Peek()) || Peek() == '.')) _pos++;
            if (_pos == start) throw new Exception($"expected number at position {_pos}");
            string num = _input.Substring(start, _pos - start);
            if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                throw new Exception($"invalid number '{num}'");
            return v;
        }
    }
}
