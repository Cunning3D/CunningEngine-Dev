using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CunningEngine.CDA {
    public static class CdaMiniJson {
        public static string GetTopLevelRaw(string json, string prop) {
            if (!TryGetTopLevelRaw(json, prop, out var v)) return null;
            return v;
        }

        public static bool TryGetTopLevelRaw(string json, string prop, out string raw) {
            raw = null;
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(prop)) return false;
            var i = 0; SkipWs(json, ref i);
            if (i >= json.Length || json[i] != '{') return false;
            i++;
            while (i < json.Length) {
                SkipWs(json, ref i);
                if (i < json.Length && json[i] == '}') return false;
                if (!TryReadString(json, ref i, out var key)) return false;
                SkipWs(json, ref i);
                if (i >= json.Length || json[i] != ':') return false;
                i++;
                SkipWs(json, ref i);
                var v0 = i;
                if (!TrySkipValue(json, ref i)) return false;
                var v1 = i;
                if (string.Equals(key, prop, StringComparison.Ordinal)) { raw = json.Substring(v0, v1 - v0); return true; }
                SkipWs(json, ref i);
                if (i < json.Length && json[i] == ',') { i++; continue; }
                if (i < json.Length && json[i] == '}') return false;
            }
            return false;
        }

        public static List<string> SplitArrayElements(string rawArray) {
            var r = new List<string>();
            if (string.IsNullOrEmpty(rawArray)) return r;
            var i = 0; SkipWs(rawArray, ref i);
            if (i >= rawArray.Length || rawArray[i] != '[') return r;
            i++;
            while (i < rawArray.Length) {
                SkipWs(rawArray, ref i);
                if (i < rawArray.Length && rawArray[i] == ']') break;
                var v0 = i;
                if (!TrySkipValue(rawArray, ref i)) break;
                var v1 = i;
                r.Add(rawArray.Substring(v0, v1 - v0));
                SkipWs(rawArray, ref i);
                if (i < rawArray.Length && rawArray[i] == ',') { i++; continue; }
                if (i < rawArray.Length && rawArray[i] == ']') break;
            }
            return r;
        }

        public static bool TryGetObjectString(string objRaw, string prop, out string value) {
            value = null;
            if (!TryGetObjectRaw(objRaw, prop, out var raw)) return false;
            var i = 0; SkipWs(raw, ref i);
            if (!TryReadString(raw, ref i, out value)) return false;
            return true;
        }

        public static bool TryGetObjectInt(string objRaw, string prop, out int value) {
            value = 0;
            if (!TryGetObjectRaw(objRaw, prop, out var raw)) return false;
            var i = 0; SkipWs(raw, ref i);
            if (!TryReadInt(raw, ref i, out value)) return false;
            return true;
        }

        public static bool TryGetObjectRaw(string objRaw, string prop, out string raw) {
            raw = null;
            if (string.IsNullOrEmpty(objRaw) || string.IsNullOrEmpty(prop)) return false;
            var i = 0; SkipWs(objRaw, ref i);
            if (i >= objRaw.Length || objRaw[i] != '{') return false;
            i++;
            while (i < objRaw.Length) {
                SkipWs(objRaw, ref i);
                if (i < objRaw.Length && objRaw[i] == '}') return false;
                if (!TryReadString(objRaw, ref i, out var key)) return false;
                SkipWs(objRaw, ref i);
                if (i >= objRaw.Length || objRaw[i] != ':') return false;
                i++;
                SkipWs(objRaw, ref i);
                var v0 = i;
                if (!TrySkipValue(objRaw, ref i)) return false;
                var v1 = i;
                if (string.Equals(key, prop, StringComparison.Ordinal)) { raw = objRaw.Substring(v0, v1 - v0); return true; }
                SkipWs(objRaw, ref i);
                if (i < objRaw.Length && objRaw[i] == ',') { i++; continue; }
                if (i < objRaw.Length && objRaw[i] == '}') return false;
            }
            return false;
        }

        public static bool TryParseSingleKeyObject(string rawObj, out string key, out string valueRaw) {
            key = null; valueRaw = null;
            if (string.IsNullOrEmpty(rawObj)) return false;
            var i = 0; SkipWs(rawObj, ref i);
            if (i >= rawObj.Length || rawObj[i] != '{') return false;
            i++;
            SkipWs(rawObj, ref i);
            if (!TryReadString(rawObj, ref i, out key)) return false;
            SkipWs(rawObj, ref i);
            if (i >= rawObj.Length || rawObj[i] != ':') return false;
            i++;
            SkipWs(rawObj, ref i);
            var v0 = i;
            if (!TrySkipValue(rawObj, ref i)) return false;
            var v1 = i;
            valueRaw = rawObj.Substring(v0, v1 - v0);
            return true;
        }

        public static string EscapeString(string s) {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length + 8);
            for (var i = 0; i < s.Length; i++) {
                var c = s[i];
                if (c == '\\') sb.Append("\\\\");
                else if (c == '"') sb.Append("\\\"");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static string FloatToJson(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        static void SkipWs(string s, ref int i) { while (i < s.Length) { var c = s[i]; if (c == ' ' || c == '\n' || c == '\r' || c == '\t') { i++; continue; } break; } }

        static bool TryReadString(string s, ref int i, out string value) {
            value = null;
            SkipWs(s, ref i);
            if (i >= s.Length || s[i] != '"') return false;
            i++;
            var sb = new StringBuilder();
            while (i < s.Length) {
                var c = s[i++];
                if (c == '"') { value = sb.ToString(); return true; }
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) return false;
                var e = s[i++];
                if (e == '"' || e == '\\' || e == '/') sb.Append(e);
                else if (e == 'n') sb.Append('\n');
                else if (e == 'r') sb.Append('\r');
                else if (e == 't') sb.Append('\t');
                else return false;
            }
            return false;
        }

        static bool TryReadInt(string s, ref int i, out int v) {
            v = 0;
            SkipWs(s, ref i);
            var sign = 1;
            if (i < s.Length && s[i] == '-') { sign = -1; i++; }
            var any = false;
            while (i < s.Length) {
                var c = s[i];
                if (c < '0' || c > '9') break;
                any = true;
                v = (v * 10) + (c - '0');
                i++;
            }
            v *= sign;
            return any;
        }

        static bool TrySkipValue(string s, ref int i) {
            SkipWs(s, ref i);
            if (i >= s.Length) return false;
            var c = s[i];
            if (c == '"') { return TryReadString(s, ref i, out _); }
            if (c == '{') return TrySkipContainer(s, ref i, '{', '}');
            if (c == '[') return TrySkipContainer(s, ref i, '[', ']');
            if (c == 't') return TryConsume(s, ref i, "true");
            if (c == 'f') return TryConsume(s, ref i, "false");
            if (c == 'n') return TryConsume(s, ref i, "null");
            return TrySkipNumber(s, ref i);
        }

        static bool TryConsume(string s, ref int i, string lit) {
            if (i + lit.Length > s.Length) return false;
            for (var k = 0; k < lit.Length; k++) if (s[i + k] != lit[k]) return false;
            i += lit.Length;
            return true;
        }

        static bool TrySkipNumber(string s, ref int i) {
            SkipWs(s, ref i);
            var start = i;
            if (i < s.Length && s[i] == '-') i++;
            var any = false;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') { any = true; i++; }
            if (i < s.Length && s[i] == '.') { i++; while (i < s.Length && s[i] >= '0' && s[i] <= '9') { any = true; i++; } }
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E')) { i++; if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++; while (i < s.Length && s[i] >= '0' && s[i] <= '9') { any = true; i++; } }
            return any && i > start;
        }

        static bool TrySkipContainer(string s, ref int i, char open, char close) {
            SkipWs(s, ref i);
            if (i >= s.Length || s[i] != open) return false;
            var depth = 0;
            var inStr = false;
            while (i < s.Length) {
                var c = s[i++];
                if (inStr) {
                    if (c == '\\') { if (i < s.Length) i++; continue; }
                    if (c == '"') inStr = false;
                    continue;
                }
                if (c == '"') { inStr = true; continue; }
                if (c == open) depth++;
                else if (c == close) { depth--; if (depth == 0) return true; }
            }
            return false;
        }
    }
}

