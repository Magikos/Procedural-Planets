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
            if (int.TryParse(tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) { v = n; return true; }
            error = $"expected integer, got '{tok}'";
            return false;
        }
    }

    sealed class FloatParser : IConsoleArgumentParser
    {
        public bool CanParse(Type t) => t == typeof(float);
        public bool TryParse(IReadOnlyList<string> tokens, int start, Type t, out object v, out int consumed, out string error)
        {
            v = null;
            if (!Single(tokens, start, out string tok, out consumed, out error)) return false;
            if (float.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out float n)) { v = n; return true; }
            error = $"expected float, got '{tok}'";
            return false;
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

            switch (tok.ToLowerInvariant())
            {
                case "red": v = Color.red; return true;
                case "green": v = Color.green; return true;
                case "blue": v = Color.blue; return true;
                case "yellow": v = Color.yellow; return true;
                case "cyan": v = Color.cyan; return true;
                case "magenta": v = Color.magenta; return true;
                case "white": v = Color.white; return true;
                case "black": v = Color.black; return true;
                case "grey": case "gray": v = Color.grey; return true;
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
