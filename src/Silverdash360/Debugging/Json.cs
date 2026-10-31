using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Silverdash360.Debugging
{
    /// <summary>A JSON object with its keys in the order they were added.</summary>
    public sealed class JsonObject : IEnumerable
    {
        internal readonly List<KeyValuePair<string, object>> Fields = new List<KeyValuePair<string, object>>();

        /// <summary>Lets an object be written as an initializer: new JsonObject { { "a", 1 } }.</summary>
        public void Add(string key, object value)
        {
            Fields.Add(new KeyValuePair<string, object>(key, value));
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return Fields.GetEnumerator();
        }
    }

    /// <summary>
    /// Writes replies as JSON: JsonObject and dictionaries as objects, other
    /// collections as arrays, numbers, booleans, null, and anything else as
    /// a string. Silverlight 3 on the console has no JSON serializer.
    /// </summary>
    public static class Json
    {
        public static string Write(object value)
        {
            StringBuilder s = new StringBuilder();
            Append(s, value);
            return s.ToString();
        }

        private static void Append(StringBuilder s, object value)
        {
            if (value == null)
            {
                s.Append("null");
            }
            else if (value is string)
            {
                AppendString(s, (string)value);
            }
            else if (value is bool)
            {
                s.Append((bool)value ? "true" : "false");
            }
            else if (value is double || value is float)
            {
                double d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                s.Append(double.IsNaN(d) || double.IsInfinity(d) ? "null" : d.ToString("R", CultureInfo.InvariantCulture));
            }
            else if (value is int || value is uint || value is long || value is ulong || value is short ||
                     value is ushort || value is byte || value is sbyte)
            {
                s.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
            else if (value is DateTime)
            {
                AppendString(s, ((DateTime)value).ToString("o", CultureInfo.InvariantCulture));
            }
            else if (value is JsonObject)
            {
                AppendObject(s, ((JsonObject)value).Fields);
            }
            else if (value is IDictionary)
            {
                List<KeyValuePair<string, object>> fields = new List<KeyValuePair<string, object>>();
                foreach (DictionaryEntry entry in (IDictionary)value)
                {
                    fields.Add(new KeyValuePair<string, object>(Convert.ToString(entry.Key, CultureInfo.InvariantCulture), entry.Value));
                }
                AppendObject(s, fields);
            }
            else if (value is IEnumerable)
            {
                s.Append('[');
                bool first = true;
                foreach (object item in (IEnumerable)value)
                {
                    if (!first)
                    {
                        s.Append(',');
                    }
                    first = false;
                    Append(s, item);
                }
                s.Append(']');
            }
            else
            {
                AppendString(s, Convert.ToString(value, CultureInfo.InvariantCulture));
            }
        }

        private static void AppendObject(StringBuilder s, List<KeyValuePair<string, object>> fields)
        {
            s.Append('{');
            for (int i = 0; i < fields.Count; i++)
            {
                if (i > 0)
                {
                    s.Append(',');
                }
                AppendString(s, fields[i].Key);
                s.Append(':');
                Append(s, fields[i].Value);
            }
            s.Append('}');
        }

        // What a string needs escaped. Most strings need nothing, and a loop
        // per character is interpreted, so the framework looks first.
        private static readonly char[] Escaped = MakeEscaped();

        private static char[] MakeEscaped()
        {
            char[] chars = new char[34];
            for (int i = 0; i < 32; i++)
            {
                chars[i] = (char)i;
            }
            chars[32] = '"';
            chars[33] = '\\';
            return chars;
        }

        private static void AppendString(StringBuilder s, string text)
        {
            s.Append('"');
            if (text.IndexOfAny(Escaped) < 0)
            {
                s.Append(text).Append('"');
                return;
            }
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': s.Append("\\\""); break;
                    case '\\': s.Append("\\\\"); break;
                    case '\n': s.Append("\\n"); break;
                    case '\r': s.Append("\\r"); break;
                    case '\t': s.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            s.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            s.Append(c);
                        }
                        break;
                }
            }
            s.Append('"');
        }
    }
}
