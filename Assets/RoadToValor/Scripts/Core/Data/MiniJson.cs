using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RoadToValor.Core
{
    /// <summary>
    /// Minimal JSON reader (objects -> Dictionary&lt;string,object&gt;, arrays -> List&lt;object&gt;,
    /// numbers -> double, strings, bool, null). Engine independent so data files load identically
    /// in Unity, in tests and on a future dedicated server.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var p = new Parser(json);
            object v = p.ParseValue();
            p.SkipWs();
            if (!p.End) throw new FormatException("Unexpected trailing JSON content at " + p.Pos);
            return v;
        }

        private sealed class Parser
        {
            private readonly string _s;
            public int Pos;
            public Parser(string s) { _s = s; Pos = 0; }
            public bool End => Pos >= _s.Length;

            public void SkipWs()
            {
                while (Pos < _s.Length)
                {
                    char c = _s[Pos];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '﻿') { Pos++; continue; }
                    break;
                }
            }

            public object ParseValue()
            {
                SkipWs();
                if (End) throw new FormatException("Unexpected end of JSON");
                char c = _s[Pos];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ParseNumber();
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_s, Pos, word, 0, word.Length) != 0)
                    throw new FormatException("Expected '" + word + "' at " + Pos);
                Pos += word.Length;
            }

            private Dictionary<string, object> ParseObject()
            {
                var d = new Dictionary<string, object>();
                Pos++; // {
                SkipWs();
                if (_s[Pos] == '}') { Pos++; return d; }
                while (true)
                {
                    SkipWs();
                    string key = ParseString();
                    SkipWs();
                    if (_s[Pos] != ':') throw new FormatException("Expected ':' at " + Pos);
                    Pos++;
                    d[key] = ParseValue();
                    SkipWs();
                    char c = _s[Pos++];
                    if (c == ',') continue;
                    if (c == '}') return d;
                    throw new FormatException("Expected ',' or '}' at " + (Pos - 1));
                }
            }

            private List<object> ParseArray()
            {
                var list = new List<object>();
                Pos++; // [
                SkipWs();
                if (_s[Pos] == ']') { Pos++; return list; }
                while (true)
                {
                    list.Add(ParseValue());
                    SkipWs();
                    char c = _s[Pos++];
                    if (c == ',') continue;
                    if (c == ']') return list;
                    throw new FormatException("Expected ',' or ']' at " + (Pos - 1));
                }
            }

            private string ParseString()
            {
                if (_s[Pos] != '"') throw new FormatException("Expected string at " + Pos);
                Pos++;
                var sb = new StringBuilder();
                while (Pos < _s.Length)
                {
                    char c = _s[Pos++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\')
                    {
                        char e = _s[Pos++];
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
                                sb.Append((char)int.Parse(_s.Substring(Pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                                Pos += 4;
                                break;
                            default: throw new FormatException("Bad escape at " + Pos);
                        }
                    }
                    else sb.Append(c);
                }
                throw new FormatException("Unterminated string");
            }

            private double ParseNumber()
            {
                int start = Pos;
                while (Pos < _s.Length)
                {
                    char c = _s[Pos];
                    if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') Pos++;
                    else break;
                }
                if (start == Pos) throw new FormatException("Unexpected character '" + _s[Pos] + "' at " + Pos);
                return double.Parse(_s.Substring(start, Pos - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>Typed accessors for MiniJson output.</summary>
    public static class J
    {
        public static Dictionary<string, object> Obj(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            if (d != null && d.TryGetValue(key, out var v)) return v as Dictionary<string, object>;
            return null;
        }

        public static List<object> Arr(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            if (d != null && d.TryGetValue(key, out var v)) return v as List<object>;
            return null;
        }

        public static float F(object o, string key, float def = 0f)
        {
            var d = o as Dictionary<string, object>;
            if (d != null && d.TryGetValue(key, out var v) && v is double dv) return (float)dv;
            return def;
        }

        public static int I(object o, string key, int def = 0)
        {
            var d = o as Dictionary<string, object>;
            if (d != null && d.TryGetValue(key, out var v) && v is double dv) return (int)Math.Round(dv);
            return def;
        }

        public static bool B(object o, string key, bool def = false)
        {
            var d = o as Dictionary<string, object>;
            if (d != null && d.TryGetValue(key, out var v) && v is bool bv) return bv;
            return def;
        }

        public static string S(object o, string key, string def = "")
        {
            var d = o as Dictionary<string, object>;
            if (d != null && d.TryGetValue(key, out var v) && v is string sv) return sv;
            return def;
        }

        public static int[] IntArray(object o, string key)
        {
            var a = Arr(o, key);
            if (a == null) return new int[0];
            var r = new int[a.Count];
            for (int i = 0; i < a.Count; i++) r[i] = a[i] is double d ? (int)Math.Round(d) : 0;
            return r;
        }

        public static Vec2 Point(object arrayOf2)
        {
            var a = arrayOf2 as List<object>;
            if (a == null || a.Count < 2) return Vec2.Zero;
            return new Vec2((float)(double)a[0], (float)(double)a[1]);
        }

        public static List<Vec2> Points(object listOfPairs)
        {
            var r = new List<Vec2>();
            var a = listOfPairs as List<object>;
            if (a == null) return r;
            foreach (var p in a) r.Add(Point(p));
            return r;
        }
    }
}
