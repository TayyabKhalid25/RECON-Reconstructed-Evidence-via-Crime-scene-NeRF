// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Gsplat.Formats
{
    /// <summary>
    /// Small allocation-conscious JSON reader used for SOG and glTF metadata. Keeping this
    /// inside the package avoids adding a JSON package to player builds and works under IL2CPP.
    /// </summary>
    internal static class MiniJson
    {
        public static object Deserialize(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var parser = new Parser(json);
            object result = parser.ParseValue();
            parser.SkipWhitespace();
            if (!parser.End)
                throw new FormatException($"Unexpected JSON content at character {parser.Position}.");
            return result;
        }

        sealed class Parser
        {
            readonly string m_json;
            int m_index;

            public Parser(string json) => m_json = json;
            public int Position => m_index;
            public bool End => m_index >= m_json.Length;

            public void SkipWhitespace()
            {
                while (!End && char.IsWhiteSpace(m_json[m_index])) m_index++;
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (End) throw Error("Unexpected end of JSON");
                return m_json[m_index] switch
                {
                    '{' => ParseObject(),
                    '[' => ParseArray(),
                    '"' => ParseString(),
                    't' => ParseLiteral("true", true),
                    'f' => ParseLiteral("false", false),
                    'n' => ParseLiteral("null", null),
                    _ => ParseNumber(),
                };
            }

            Dictionary<string, object> ParseObject()
            {
                Expect('{');
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                SkipWhitespace();
                if (TryConsume('}')) return result;

                while (true)
                {
                    SkipWhitespace();
                    if (End || m_json[m_index] != '"') throw Error("Expected an object key");
                    string key = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    result[key] = ParseValue();
                    SkipWhitespace();
                    if (TryConsume('}')) return result;
                    Expect(',');
                }
            }

            List<object> ParseArray()
            {
                Expect('[');
                var result = new List<object>();
                SkipWhitespace();
                if (TryConsume(']')) return result;

                while (true)
                {
                    result.Add(ParseValue());
                    SkipWhitespace();
                    if (TryConsume(']')) return result;
                    Expect(',');
                }
            }

            string ParseString()
            {
                Expect('"');
                var result = new StringBuilder();
                while (!End)
                {
                    char c = m_json[m_index++];
                    if (c == '"') return result.ToString();
                    if (c != '\\')
                    {
                        result.Append(c);
                        continue;
                    }

                    if (End) throw Error("Unterminated string escape");
                    char escaped = m_json[m_index++];
                    switch (escaped)
                    {
                        case '"': result.Append('"'); break;
                        case '\\': result.Append('\\'); break;
                        case '/': result.Append('/'); break;
                        case 'b': result.Append('\b'); break;
                        case 'f': result.Append('\f'); break;
                        case 'n': result.Append('\n'); break;
                        case 'r': result.Append('\r'); break;
                        case 't': result.Append('\t'); break;
                        case 'u':
                            if (m_index + 4 > m_json.Length) throw Error("Invalid Unicode escape");
                            if (!ushort.TryParse(m_json.AsSpan(m_index, 4), NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture, out ushort code))
                                throw Error("Invalid Unicode escape");
                            result.Append((char)code);
                            m_index += 4;
                            break;
                        default: throw Error($"Unsupported string escape '\\{escaped}'");
                    }
                }
                throw Error("Unterminated string");
            }

            object ParseLiteral(string literal, object value)
            {
                if (m_index + literal.Length > m_json.Length ||
                    !string.Equals(m_json.Substring(m_index, literal.Length), literal,
                        StringComparison.Ordinal))
                    throw Error($"Expected '{literal}'");
                m_index += literal.Length;
                return value;
            }

            object ParseNumber()
            {
                int start = m_index;
                if (!End && m_json[m_index] == '-') m_index++;
                while (!End && char.IsDigit(m_json[m_index])) m_index++;
                bool floating = false;
                if (!End && m_json[m_index] == '.')
                {
                    floating = true;
                    m_index++;
                    while (!End && char.IsDigit(m_json[m_index])) m_index++;
                }
                if (!End && (m_json[m_index] == 'e' || m_json[m_index] == 'E'))
                {
                    floating = true;
                    m_index++;
                    if (!End && (m_json[m_index] == '+' || m_json[m_index] == '-')) m_index++;
                    while (!End && char.IsDigit(m_json[m_index])) m_index++;
                }

                if (start == m_index) throw Error("Expected a JSON value");
                string token = m_json.Substring(start, m_index - start);
                if (!floating && long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out long integer))
                    return integer;
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out double number))
                    return number;
                throw Error($"Invalid number '{token}'");
            }

            void Expect(char expected)
            {
                SkipWhitespace();
                if (End || m_json[m_index] != expected) throw Error($"Expected '{expected}'");
                m_index++;
            }

            bool TryConsume(char expected)
            {
                SkipWhitespace();
                if (End || m_json[m_index] != expected) return false;
                m_index++;
                return true;
            }

            FormatException Error(string message) =>
                new($"{message} at character {m_index}.");
        }
    }

    internal static class JsonValues
    {
        public static Dictionary<string, object> Object(object value, string name) =>
            value as Dictionary<string, object> ??
            throw new FormatException($"JSON member '{name}' must be an object.");

        public static Dictionary<string, object> OptionalObject(
            this Dictionary<string, object> value, string name)
        {
            return value.TryGetValue(name, out object child) && child != null
                ? Object(child, name)
                : null;
        }

        public static List<object> Array(object value, string name) =>
            value as List<object> ?? throw new FormatException($"JSON member '{name}' must be an array.");

        public static string String(object value, string name) =>
            value as string ?? throw new FormatException($"JSON member '{name}' must be a string.");

        public static int Int(object value, string name)
        {
            long number = value switch
            {
                long i => i,
                double d when Math.Abs(d - Math.Round(d)) < 1e-9 => (long)Math.Round(d),
                _ => throw new FormatException($"JSON member '{name}' must be an integer."),
            };
            if (number < int.MinValue || number > int.MaxValue)
                throw new FormatException($"JSON member '{name}' is outside Int32 range.");
            return (int)number;
        }

        public static float Float(object value, string name)
        {
            double number = value switch
            {
                long i => i,
                double d => d,
                _ => throw new FormatException($"JSON member '{name}' must be numeric."),
            };
            if (double.IsNaN(number) || double.IsInfinity(number) ||
                number < -float.MaxValue || number > float.MaxValue)
                throw new FormatException($"JSON member '{name}' is not a finite float.");
            return (float)number;
        }

        public static int RequiredInt(this Dictionary<string, object> value, string name) =>
            value.TryGetValue(name, out object child)
                ? Int(child, name)
                : throw new FormatException($"Required JSON member '{name}' is missing.");

        public static int OptionalInt(this Dictionary<string, object> value, string name, int fallback) =>
            value.TryGetValue(name, out object child) && child != null ? Int(child, name) : fallback;

        public static bool OptionalBool(this Dictionary<string, object> value, string name, bool fallback) =>
            value.TryGetValue(name, out object child) && child != null
                ? child is bool b ? b : throw new FormatException($"JSON member '{name}' must be Boolean.")
                : fallback;

        public static List<object> RequiredArray(this Dictionary<string, object> value, string name) =>
            value.TryGetValue(name, out object child)
                ? Array(child, name)
                : throw new FormatException($"Required JSON member '{name}' is missing.");

        public static float[] FloatArray(this Dictionary<string, object> value, string name, int expected = -1)
        {
            List<object> array = value.RequiredArray(name);
            if (expected >= 0 && array.Count != expected)
                throw new FormatException($"JSON member '{name}' must contain {expected} values.");
            var result = new float[array.Count];
            for (int i = 0; i < result.Length; i++) result[i] = Float(array[i], $"{name}[{i}]");
            return result;
        }

        public static string[] StringArray(this Dictionary<string, object> value, string name)
        {
            List<object> array = value.RequiredArray(name);
            var result = new string[array.Count];
            for (int i = 0; i < result.Length; i++) result[i] = String(array[i], $"{name}[{i}]");
            return result;
        }
    }
}
