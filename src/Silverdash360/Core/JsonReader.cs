using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Silverdash360.Core
{
    /// <summary>
    /// Reads JSON, such as a GitHub API answer, into dictionaries (objects),
    /// lists (arrays), strings, doubles, bools and nulls. The runtime has no
    /// JSON reader of its own.
    /// </summary>
    public static class JsonReader
    {
        // Objects and arrays within each other at most this deep: each level
        // is a call, so a document nested deeper, made to overflow the
        // stack, is refused instead.
        private const int MaxDepth = 64;

        public static object Parse(string text)
        {
            int at = 0;
            object value = Value(text, ref at, 0);
            SkipSpace(text, ref at);
            if (at != text.Length)
            {
                throw new FormatException("JSON goes on after its value at " + at + ".");
            }
            return value;
        }

        /// <summary>An object's member as a string, or null when it's missing or isn't one.</summary>
        public static string String(IDictionary<string, object> json, string name)
        {
            object value;
            return json.TryGetValue(name, out value) ? value as string : null;
        }

        /// <summary>An object's member as a number, or the fallback.</summary>
        public static double Number(IDictionary<string, object> json, string name, double fallback)
        {
            object value;
            return json.TryGetValue(name, out value) && value is double ? (double)value : fallback;
        }

        private static object Value(string text, ref int at, int depth)
        {
            SkipSpace(text, ref at);
            if (at >= text.Length)
            {
                throw new FormatException("JSON ends where a value should be.");
            }
            char c = text[at];
            switch (c)
            {
                case '{': return Object(text, ref at, Deeper(depth, at));
                case '[': return Array(text, ref at, Deeper(depth, at));
                case '"': return String(text, ref at);
                case 't': Literal(text, ref at, "true"); return true;
                case 'f': Literal(text, ref at, "false"); return false;
                case 'n': Literal(text, ref at, "null"); return null;
                default: return Number(text, ref at);
            }
        }

        private static Dictionary<string, object> Object(string text, ref int at, int depth)
        {
            Dictionary<string, object> members = new Dictionary<string, object>();
            at++;
            SkipSpace(text, ref at);
            if (at < text.Length && text[at] == '}')
            {
                at++;
                return members;
            }
            while (true)
            {
                SkipSpace(text, ref at);
                string name = String(text, ref at);
                SkipSpace(text, ref at);
                Expect(text, ref at, ':');
                members[name] = Value(text, ref at, depth);
                SkipSpace(text, ref at);
                if (at < text.Length && text[at] == ',')
                {
                    at++;
                    continue;
                }
                Expect(text, ref at, '}');
                return members;
            }
        }

        private static List<object> Array(string text, ref int at, int depth)
        {
            List<object> items = new List<object>();
            at++;
            SkipSpace(text, ref at);
            if (at < text.Length && text[at] == ']')
            {
                at++;
                return items;
            }
            while (true)
            {
                items.Add(Value(text, ref at, depth));
                SkipSpace(text, ref at);
                if (at < text.Length && text[at] == ',')
                {
                    at++;
                    continue;
                }
                Expect(text, ref at, ']');
                return items;
            }
        }

        private static int Deeper(int depth, int at)
        {
            if (depth >= MaxDepth)
            {
                throw new FormatException("JSON is nested more than " + MaxDepth + " deep, at " + at + ".");
            }
            return depth + 1;
        }

        private static string String(string text, ref int at)
        {
            Expect(text, ref at, '"');
            StringBuilder value = new StringBuilder();
            while (at < text.Length)
            {
                char c = text[at++];
                if (c == '"')
                {
                    return value.ToString();
                }
                if (c != '\\')
                {
                    value.Append(c);
                    continue;
                }
                if (at >= text.Length)
                {
                    break;
                }
                char escaped = text[at++];
                switch (escaped)
                {
                    case 'b': value.Append('\b'); break;
                    case 'f': value.Append('\f'); break;
                    case 'n': value.Append('\n'); break;
                    case 'r': value.Append('\r'); break;
                    case 't': value.Append('\t'); break;
                    case 'u':
                        if (at + 4 > text.Length)
                        {
                            throw new FormatException("JSON ends inside a \\u escape.");
                        }
                        value.Append((char)int.Parse(text.Substring(at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        at += 4;
                        break;
                    default: value.Append(escaped); break;
                }
            }
            throw new FormatException("JSON ends inside a string.");
        }

        private static double Number(string text, ref int at)
        {
            int start = at;
            while (at < text.Length && "+-0123456789.eE".IndexOf(text[at]) >= 0)
            {
                at++;
            }
            if (at == start)
            {
                throw new FormatException("JSON has '" + text[at] + "' where a value should be, at " + at + ".");
            }
            return double.Parse(text.Substring(start, at - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static void Literal(string text, ref int at, string literal)
        {
            if (string.CompareOrdinal(text, at, literal, 0, literal.Length) != 0)
            {
                throw new FormatException("JSON has something other than " + literal + " at " + at + ".");
            }
            at += literal.Length;
        }

        private static void Expect(string text, ref int at, char c)
        {
            if (at >= text.Length || text[at] != c)
            {
                throw new FormatException("JSON has no '" + c + "' where one should be, at " + at + ".");
            }
            at++;
        }

        private static void SkipSpace(string text, ref int at)
        {
            while (at < text.Length && char.IsWhiteSpace(text[at]))
            {
                at++;
            }
        }
    }
}
