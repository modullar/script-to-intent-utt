using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NahuatlIntent
{
    /// <summary>
    /// A small hand-written JSON reader. Objects become
    /// Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;, numbers double,
    /// plus string, bool and null. No reflection, no dynamic, no LINQ, so it
    /// compiles unchanged under IL2CPP. Only what phrases.json needs.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var p = new Parser(json);
            p.SkipWhitespace();
            object value = p.ParseValue();
            p.SkipWhitespace();
            if (!p.AtEnd) throw p.Error("trailing characters");
            return value;
        }

        // ---- typed helpers for mapping a parsed object -------------------

        public static string GetString(Dictionary<string, object> obj, string key, string fallback)
        {
            object v;
            if (obj != null && obj.TryGetValue(key, out v) && v is string s) return s;
            return fallback;
        }

        public static bool GetBool(Dictionary<string, object> obj, string key, bool fallback)
        {
            object v;
            if (obj != null && obj.TryGetValue(key, out v) && v is bool b) return b;
            return fallback;
        }

        public static double GetDouble(Dictionary<string, object> obj, string key, double fallback)
        {
            object v;
            if (obj != null && obj.TryGetValue(key, out v) && v is double d) return d;
            return fallback;
        }

        public static int GetInt(Dictionary<string, object> obj, string key, int fallback)
        {
            object v;
            if (obj != null && obj.TryGetValue(key, out v) && v is double d) return (int)d;
            return fallback;
        }

        public static string[] GetStringArray(Dictionary<string, object> obj, string key)
        {
            object v;
            if (obj == null || !obj.TryGetValue(key, out v) || !(v is List<object> list))
                return new string[0];
            var result = new List<string>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is string s) result.Add(s);
            }
            return result.ToArray();
        }

        public static List<object> GetList(Dictionary<string, object> obj, string key)
        {
            object v;
            if (obj != null && obj.TryGetValue(key, out v) && v is List<object> list) return list;
            return new List<object>();
        }

        // ---- the parser ----------------------------------------------------

        private sealed class Parser
        {
            private readonly string s;
            private int i;

            public Parser(string text) { s = text; i = 0; }

            public bool AtEnd { get { return i >= s.Length; } }

            public FormatException Error(string message)
            {
                return new FormatException("MiniJson: " + message + " at position " + i);
            }

            public void SkipWhitespace()
            {
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') i++;
                    else break;
                }
            }

            public object ParseValue()
            {
                if (AtEnd) throw Error("unexpected end of input");
                char c = s[i];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': ExpectLiteral("true"); return true;
                    case 'f': ExpectLiteral("false"); return false;
                    case 'n': ExpectLiteral("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber();
                        throw Error("unexpected character '" + c + "'");
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                var obj = new Dictionary<string, object>();
                i++; // '{'
                SkipWhitespace();
                if (!AtEnd && s[i] == '}') { i++; return obj; }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || s[i] != '"') throw Error("expected object key");
                    string key = ParseString();
                    SkipWhitespace();
                    if (AtEnd || s[i] != ':') throw Error("expected ':'");
                    i++;
                    SkipWhitespace();
                    obj[key] = ParseValue();
                    SkipWhitespace();
                    if (AtEnd) throw Error("unterminated object");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return obj; }
                    throw Error("expected ',' or '}'");
                }
            }

            private List<object> ParseArray()
            {
                var list = new List<object>();
                i++; // '['
                SkipWhitespace();
                if (!AtEnd && s[i] == ']') { i++; return list; }
                while (true)
                {
                    SkipWhitespace();
                    list.Add(ParseValue());
                    SkipWhitespace();
                    if (AtEnd) throw Error("unterminated array");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return list; }
                    throw Error("expected ',' or ']'");
                }
            }

            private string ParseString()
            {
                i++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Error("unterminated string");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw Error("bad escape");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw Error("bad \\u escape");
                            sb.Append((char)ParseHex4());
                            break;
                        default: throw Error("bad escape '\\" + e + "'");
                    }
                }
            }

            private int ParseHex4()
            {
                int value = 0;
                for (int k = 0; k < 4; k++)
                {
                    char c = s[i++];
                    int d;
                    if (c >= '0' && c <= '9') d = c - '0';
                    else if (c >= 'a' && c <= 'f') d = c - 'a' + 10;
                    else if (c >= 'A' && c <= 'F') d = c - 'A' + 10;
                    else throw Error("bad hex digit");
                    value = (value << 4) | d;
                }
                return value;
            }

            private double ParseNumber()
            {
                int start = i;
                if (s[i] == '-') i++;
                while (i < s.Length)
                {
                    char c = s[i];
                    if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') i++;
                    else break;
                }
                string text = s.Substring(start, i - start);
                double value;
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    throw Error("bad number '" + text + "'");
                return value;
            }

            private void ExpectLiteral(string literal)
            {
                if (string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0)
                    throw Error("expected '" + literal + "'");
                i += literal.Length;
            }
        }
    }
}
