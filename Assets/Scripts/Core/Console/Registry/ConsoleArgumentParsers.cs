using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public static class ConsoleArgumentParsers
{
    static readonly List<IConsoleArgumentParser> _parsers = new()
    {
        new IntParser(),
        new FloatParser(),
        new BoolParser(),
        new StringParser(),
        new Vector2Parser(),
        new Vector3Parser(),
        new ColorParser(),
        new EnumParser(),
    };

    public static bool TryParse(IReadOnlyList<string> tokens, int startIndex, Type type,
        out object value, out int consumed, out string error)
    {
        // Nullable<T>: unwrap to T and parse. Boxed value satisfies a Nullable<T> parameter.
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            type = type.GetGenericArguments()[0];

        foreach (var parser in _parsers)
        {
            if (!parser.CanParse(type)) continue;
            return parser.TryParse(tokens, startIndex, type, out value, out consumed, out error);
        }

        value = null;
        consumed = 0;
        error = $"No parser registered for type {type.Name}";
        return false;
    }

    static bool Single(IReadOnlyList<string> tokens, int startIndex, out string token, out int consumed, out string error)
    {
        if (startIndex >= tokens.Count)
        {
            token = null;
            consumed = 0;
            error = "missing value";
            return false;
        }

        token = tokens[startIndex];
        consumed = 1;
        error = null;
        return true;
    }

    sealed class IntParser : IConsoleArgumentParser
    {
        public bool CanParse(Type t) => t == typeof(int);
        public bool TryParse(IReadOnlyList<string> tokens, int start, Type t, out object v, out int consumed, out string error)
        {
            v = null;
            if (!Single(tokens, start, out string tok, out consumed, out error)) return false;
            if (!ExpressionEvaluator.TryEvaluate(tok, out double d, out string exprError))
            {
                error = $"expected integer (or expression), got '{tok}': {exprError}";
                return false;
            }
            double rounded = System.Math.Round(d);
            if (System.Math.Abs(d - rounded) > 0.0000001)
            {
                error = $"expected integer, expression evaluates to {d}";
                return false;
            }
            v = (int)rounded;
            return true;
        }
    }

    sealed class FloatParser : IConsoleArgumentParser
    {
        public bool CanParse(Type t) => t == typeof(float);
        public bool TryParse(IReadOnlyList<string> tokens, int start, Type t, out object v, out int consumed, out string error)
        {
            v = null;
            if (!Single(tokens, start, out string tok, out consumed, out error)) return false;
            if (!ExpressionEvaluator.TryEvaluate(tok, out double d, out string exprError))
            {
                error = $"expected float (or expression), got '{tok}': {exprError}";
                return false;
            }
            v = (float)d;
            return true;
        }
    }

    sealed class BoolParser : IConsoleArgumentParser
    {
        public bool CanParse(Type t) => t == typeof(bool);
        public bool TryParse(IReadOnlyList<string> tokens, int start, Type t, out object v, out int consumed, out string error)
        {
            v = null;
            if (!Single(tokens, start, out string tok, out consumed, out error)) return false;
            string lower = tok.ToLowerInvariant();
            if (lower is "true" or "1" or "yes" or "on") { v = true; return true; }
            if (lower is "false" or "0" or "no" or "off") { v = false; return true; }
            error = $"expected bool (true/false/1/0/yes/no/on/off), got '{tok}'";
            return false;
        }
    }

    sealed class StringParser : IConsoleArgumentParser
    {
        public bool CanParse(Type t) => t == typeof(string);
        public bool TryParse(IReadOnlyList<string> tokens, int start, Type t, out object v, out int consumed, out string error)
        {
            v = null;
            if (!Single(tokens, start, out string tok, out consumed, out error)) return false;
            v = tok;
            return true;
        }
    }

    sealed class Vector2Parser : IConsoleArgumentParser
    {
        public bool CanParse(Type t) => t == typeof(Vector2);
        public bool TryParse(IReadOnlyList<string> tokens, int start, Type t, out object v, out int consumed, out string error)
        {
            v = null; consumed = 0;
            if (start >= tokens.Count) { error = "missing Vector2 (need 2 numbers or 'x,y')"; return false; }

            // Try comma form first.
            string first = tokens[start];
            if (first.Contains(','))
            {
                string[] parts = first.Split(',');
                if (parts.Length == 2 &&
                    float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                {
                    v = new Vector2(x, y); consumed = 1; error = null; return true;
                }
                error = $"could not parse Vector2 from '{first}'";
                return false;
            }

            // Two-token form.
            if (start + 1 >= tokens.Count) { error = "Vector2 needs 2 numbers"; return false; }
            if (float.TryParse(tokens[start], NumberStyles.Float, CultureInfo.InvariantCulture, out float ax) &&
                float.TryParse(tokens[start + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float ay))
            {
                v = new Vector2(ax, ay); consumed = 2; error = null; return true;
            }
            error = $"could not parse Vector2 from '{tokens[start]} {tokens[start + 1]}'";
            return false;
        }
    }

    sealed class Vector3Parser : IConsoleArgumentParser
    {
        public bool CanParse(Type t) => t == typeof(Vector3);
        public bool TryParse(IReadOnlyList<string> tokens, int start, Type t, out object v, out int consumed, out string error)
        {
            v = null; consumed = 0;
            if (start >= tokens.Count) { error = "missing Vector3 (need 3 numbers or 'x,y,z')"; return false; }

            string first = tokens[start];
            if (first.Contains(','))
            {
                string[] parts = first.Split(',');
                if (parts.Length == 3 &&
                    float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                    float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                {
                    v = new Vector3(x, y, z); consumed = 1; error = null; return true;
                }
                error = $"could not parse Vector3 from '{first}'";
                return false;
            }

            if (start + 2 >= tokens.Count) { error = "Vector3 needs 3 numbers"; return false; }
            if (float.TryParse(tokens[start], NumberStyles.Float, CultureInfo.InvariantCulture, out float ax) &&
                float.TryParse(tokens[start + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float ay) &&
                float.TryParse(tokens[start + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out float az))
            {
                v = new Vector3(ax, ay, az); consumed = 3; error = null; return true;
            }
            error = "could not parse Vector3";
            return false;
        }
    }

    sealed class ColorParser : IConsoleArgumentParser
    {
        public bool CanParse(Type t) => t == typeof(Color);
        public bool TryParse(IReadOnlyList<string> tokens, int start, Type t, out object v, out int consumed, out string error)
        {
            v = null;
            if (!Single(tokens, start, out string tok, out consumed, out error)) return false;

            if (tok.StartsWith("#"))
            {
                if (ColorUtility.TryParseHtmlString(tok, out Color hex)) { v = hex; return true; }
                error = $"invalid hex color '{tok}'";
                return false;
            }

            if (ConsoleColors.Named.TryGetValue(tok, out Color named))
            {
                v = named;
                return true;
            }

            error = $"unknown color '{tok}' (use a name or #rrggbb)";
            return false;
        }
    }

    sealed class EnumParser : IConsoleArgumentParser
    {
        public bool CanParse(Type t) => t.IsEnum;
        public bool TryParse(IReadOnlyList<string> tokens, int start, Type t, out object v, out int consumed, out string error)
        {
            v = null;
            if (!Single(tokens, start, out string tok, out consumed, out error)) return false;
            try
            {
                v = Enum.Parse(t, tok, ignoreCase: true);
                return true;
            }
            catch (ArgumentException)
            {
                error = $"expected one of [{string.Join("|", Enum.GetNames(t))}], got '{tok}'";
                return false;
            }
        }
    }
}
