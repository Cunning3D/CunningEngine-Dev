using System;
using System.Collections.Generic;
using UnityEngine;

namespace CunningEngine.CDA {
    [Serializable]
    public sealed class CdaParamValue {
        public string kind = "Float";
        public float f;
        public int i;
        public bool b;
        public string s;
        public float[] a;

        public static CdaParamValue FromDefaultJson(string json) {
            if (string.IsNullOrEmpty(json)) json = "{\"Float\":0}";
            if (!CdaMiniJson.TryParseSingleKeyObject(json, out var k, out var vRaw)) return new CdaParamValue { kind = "Float", f = 0f };
            if (k.Equals("Float", StringComparison.OrdinalIgnoreCase)) return new CdaParamValue { kind = "Float", f = ToFloat(vRaw) };
            if (k.Equals("Int", StringComparison.OrdinalIgnoreCase)) return new CdaParamValue { kind = "Int", i = ToInt(vRaw) };
            if (k.Equals("Bool", StringComparison.OrdinalIgnoreCase)) return new CdaParamValue { kind = "Bool", b = ToBool(vRaw) };
            if (k.Equals("String", StringComparison.OrdinalIgnoreCase)) return new CdaParamValue { kind = "String", s = ToStringValue(vRaw) ?? "" };
            if (k.Equals("Vec2", StringComparison.OrdinalIgnoreCase) || k.Equals("Vec3", StringComparison.OrdinalIgnoreCase) || k.Equals("Vec4", StringComparison.OrdinalIgnoreCase) || k.Equals("Color", StringComparison.OrdinalIgnoreCase) || k.Equals("Color4", StringComparison.OrdinalIgnoreCase)) return new CdaParamValue { kind = k, a = ToFloatArray(vRaw) };
            return new CdaParamValue { kind = k, s = ToStringValue(vRaw) };
            return new CdaParamValue { kind = "Float", f = 0f };
        }

        public void WriteSerdeJson(System.Text.StringBuilder sb) {
            var k = kind ?? "Float";
            sb.Append('{').Append('"').Append(CdaMiniJson.EscapeString(k)).Append('"').Append(':');
            if (k.Equals("Float", StringComparison.OrdinalIgnoreCase)) sb.Append(CdaMiniJson.FloatToJson(f));
            else if (k.Equals("Int", StringComparison.OrdinalIgnoreCase)) sb.Append(i);
            else if (k.Equals("Bool", StringComparison.OrdinalIgnoreCase)) sb.Append(b ? "true" : "false");
            else if (k.Equals("String", StringComparison.OrdinalIgnoreCase)) sb.Append('"').Append(CdaMiniJson.EscapeString(s ?? "")).Append('"');
            else if (k.Equals("Vec2", StringComparison.OrdinalIgnoreCase) || k.Equals("Vec3", StringComparison.OrdinalIgnoreCase) || k.Equals("Vec4", StringComparison.OrdinalIgnoreCase) || k.Equals("Color", StringComparison.OrdinalIgnoreCase) || k.Equals("Color4", StringComparison.OrdinalIgnoreCase)) {
                sb.Append('[');
                var arr = a ?? Array.Empty<float>();
                for (var ix = 0; ix < arr.Length; ix++) { if (ix != 0) sb.Append(','); sb.Append(CdaMiniJson.FloatToJson(arr[ix])); }
                sb.Append(']');
            } else sb.Append('"').Append(CdaMiniJson.EscapeString(s ?? "")).Append('"');
            sb.Append('}');
        }

        public Vector2 AsVec2() { var x = (a != null && a.Length > 0) ? a[0] : 0f; var y = (a != null && a.Length > 1) ? a[1] : 0f; return new Vector2(x, y); }
        public Vector3 AsVec3() { var x = (a != null && a.Length > 0) ? a[0] : 0f; var y = (a != null && a.Length > 1) ? a[1] : 0f; var z = (a != null && a.Length > 2) ? a[2] : 0f; return new Vector3(x, y, z); }
        public Vector4 AsVec4() { var x = (a != null && a.Length > 0) ? a[0] : 0f; var y = (a != null && a.Length > 1) ? a[1] : 0f; var z = (a != null && a.Length > 2) ? a[2] : 0f; var w = (a != null && a.Length > 3) ? a[3] : 0f; return new Vector4(x, y, z, w); }
        public void SetVec2(Vector2 v) { kind = "Vec2"; a = new[] { v.x, v.y }; }
        public void SetVec3(Vector3 v) { kind = "Vec3"; a = new[] { v.x, v.y, v.z }; }
        public void SetVec4(Vector4 v) { kind = "Vec4"; a = new[] { v.x, v.y, v.z, v.w }; }
        public Color AsColor(bool alpha) { var v = AsVec4(); return alpha ? new Color(v.x, v.y, v.z, v.w) : new Color(v.x, v.y, v.z, 1f); }
        public void SetColor(Color c, bool alpha) { kind = alpha ? "Color4" : "Color"; a = alpha ? new[] { c.r, c.g, c.b, c.a } : new[] { c.r, c.g, c.b }; }

        static float ToFloat(string raw) { float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r); return r; }
        static int ToInt(string raw) { int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var r); return r; }
        static bool ToBool(string raw) { return raw != null && (raw.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) || raw.Trim().Equals("1", StringComparison.Ordinal)); }
        static string ToStringValue(string raw) {
            if (string.IsNullOrEmpty(raw)) return null;
            raw = raw.Trim();
            if (raw.Length >= 2 && raw[0] == '"' && raw[raw.Length - 1] == '"') return raw.Substring(1, raw.Length - 2).Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t");
            return raw;
        }
        static float[] ToFloatArray(string rawArr) {
            var xs = new List<float>();
            if (string.IsNullOrEmpty(rawArr)) return Array.Empty<float>();
            rawArr = rawArr.Trim();
            if (!rawArr.StartsWith("[") || !rawArr.EndsWith("]")) return Array.Empty<float>();
            var inner = rawArr.Substring(1, rawArr.Length - 2);
            var parts = inner.Split(',');
            for (var i = 0; i < parts.Length; i++) { var t = parts[i].Trim(); if (t.Length == 0) continue; if (float.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)) xs.Add(f); }
            return xs.ToArray();
        }
    }
}

