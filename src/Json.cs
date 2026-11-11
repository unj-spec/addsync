using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WowAddonManager
{
    /// <summary>
    /// 의존성 없는 최소 JSON 읽기/쓰기.
    /// 객체는 Dictionary&lt;string, object&gt;, 배열은 List&lt;object&gt; 로 읽는다.
    /// </summary>
    public static class Json
    {
        // ── 읽기 ────────────────────────────────────────────────
        public static object Parse(string text)
        {
            int i = 0;
            object v = ParseValue(text, ref i);
            SkipWs(text, ref i);
            return v;
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }

        static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON 이 중간에 끊겼습니다.");

            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return ParseString(s, ref i);
            if (c == 't' && s.Length - i >= 4 && s.Substring(i, 4) == "true") { i += 4; return true; }
            if (c == 'f' && s.Length - i >= 5 && s.Substring(i, 5) == "false") { i += 5; return false; }
            if (c == 'n' && s.Length - i >= 4 && s.Substring(i, 4) == "null") { i += 4; return null; }
            return ParseNumber(s, ref i);
        }

        static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            Dictionary<string, object> map = new Dictionary<string, object>();
            i++;                                   // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return map; }

            while (true)
            {
                SkipWs(s, ref i);
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("JSON: ':' 가 없습니다.");
                i++;
                map[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON: 객체가 닫히지 않았습니다.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return map; }
                throw new FormatException("JSON: 객체 안에서 예상치 못한 글자 '" + s[i] + "'");
            }
        }

        static List<object> ParseArray(string s, ref int i)
        {
            List<object> list = new List<object>();
            i++;                                   // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }

            while (true)
            {
                list.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON: 배열이 닫히지 않았습니다.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return list; }
                throw new FormatException("JSON: 배열 안에서 예상치 못한 글자 '" + s[i] + "'");
            }
        }

        static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("JSON: 문자열이 아닙니다.");
            i++;
            StringBuilder b = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return b.ToString();
                if (c != '\\') { b.Append(c); continue; }

                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': b.Append('"'); break;
                    case '\\': b.Append('\\'); break;
                    case '/': b.Append('/'); break;
                    case 'b': b.Append('\b'); break;
                    case 'f': b.Append('\f'); break;
                    case 'n': b.Append('\n'); break;
                    case 'r': b.Append('\r'); break;
                    case 't': b.Append('\t'); break;
                    case 'u':
                        if (i + 4 <= s.Length)
                        {
                            b.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                            i += 4;
                        }
                        break;
                    default: b.Append(e); break;
                }
            }
            throw new FormatException("JSON: 문자열이 닫히지 않았습니다.");
        }

        static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            string t = s.Substring(start, i - start);
            long l;
            if (long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return l;
            double d;
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            throw new FormatException("JSON: 숫자가 아닙니다 '" + t + "'");
        }

        // ── 꺼내기 도우미 ───────────────────────────────────────
        public static Dictionary<string, object> Obj(object v)
        {
            return v as Dictionary<string, object>;
        }

        public static List<object> Arr(object v)
        {
            return v as List<object>;
        }

        public static string Str(Dictionary<string, object> o, string key, string fallback)
        {
            object v;
            if (o != null && o.TryGetValue(key, out v) && v != null) return v.ToString();
            return fallback;
        }

        public static long Num(Dictionary<string, object> o, string key, long fallback)
        {
            object v;
            if (o == null || !o.TryGetValue(key, out v) || v == null) return fallback;
            if (v is long) return (long)v;
            if (v is double) return (long)(double)v;
            long l;
            if (long.TryParse(v.ToString(), out l)) return l;
            return fallback;
        }

        public static bool Bool(Dictionary<string, object> o, string key, bool fallback)
        {
            object v;
            if (o == null || !o.TryGetValue(key, out v) || v == null) return fallback;
            if (v is bool) return (bool)v;
            return v.ToString() == "true" || v.ToString() == "1";
        }

        // ── 쓰기 ────────────────────────────────────────────────
        public class Writer
        {
            readonly StringBuilder b = new StringBuilder();
            readonly Stack<bool> first = new Stack<bool>();
            int indent;

            void Comma()
            {
                if (first.Count == 0) return;
                if (first.Peek()) { first.Pop(); first.Push(false); }
                else b.Append(',');
                b.AppendLine();
                b.Append(' ', indent * 2);
            }

            public Writer StartObject(string key)
            {
                Comma();
                if (key != null) b.Append(Quote(key)).Append(": ");
                b.Append('{');
                first.Push(true);
                indent++;
                return this;
            }

            public Writer EndObject()
            {
                indent--;
                if (!first.Pop()) { b.AppendLine(); b.Append(' ', indent * 2); }
                b.Append('}');
                return this;
            }

            public Writer StartArray(string key)
            {
                Comma();
                if (key != null) b.Append(Quote(key)).Append(": ");
                b.Append('[');
                first.Push(true);
                indent++;
                return this;
            }

            public Writer EndArray()
            {
                indent--;
                if (!first.Pop()) { b.AppendLine(); b.Append(' ', indent * 2); }
                b.Append(']');
                return this;
            }

            public Writer Put(string key, string value)
            {
                Comma();
                b.Append(Quote(key)).Append(": ").Append(Quote(value ?? ""));
                return this;
            }

            public Writer Put(string key, long value)
            {
                Comma();
                b.Append(Quote(key)).Append(": ").Append(value.ToString(CultureInfo.InvariantCulture));
                return this;
            }

            public Writer Put(string key, bool value)
            {
                Comma();
                b.Append(Quote(key)).Append(": ").Append(value ? "true" : "false");
                return this;
            }

            public override string ToString() { return b.ToString(); }
        }

        public static string Quote(string s)
        {
            StringBuilder b = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"') b.Append("\\\"");
                else if (c == '\\') b.Append("\\\\");
                else if (c == '\n') b.Append("\\n");
                else if (c == '\r') b.Append("\\r");
                else if (c == '\t') b.Append("\\t");
                else if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4"));
                else b.Append(c);
            }
            return b.Append('"').ToString();
        }
    }
}
