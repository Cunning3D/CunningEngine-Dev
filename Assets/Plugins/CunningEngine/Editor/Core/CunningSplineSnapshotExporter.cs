using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace CunningEngine.Editor
{
    public static class CunningSplineSnapshotExporter
    {
        static string F(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        static void V3(StringBuilder sb, Vector3 v) => sb.Append('[').Append(F(v.x)).Append(',').Append(F(v.y)).Append(',').Append(F(v.z)).Append(']');
        static void Q4(StringBuilder sb, Quaternion q) => sb.Append('[').Append(F(q.x)).Append(',').Append(F(q.y)).Append(',').Append(F(q.z)).Append(',').Append(F(q.w)).Append(']');
        static void M4(StringBuilder sb, Matrix4x4 m)
        {
            sb.Append('[');
            for (int c = 0; c < 4; ++c)
            {
                var col = m.GetColumn(c);
                sb.Append('[').Append(F(col.x)).Append(',').Append(F(col.y)).Append(',').Append(F(col.z)).Append(',').Append(F(col.w)).Append(']');
                if (c != 3) sb.Append(',');
            }
            sb.Append(']');
        }

        [MenuItem("Cunning/Splines/Export Snapshot (JSON)")]
        static void ExportSnapshot()
        {
            var go = Selection.activeGameObject;
            if (go == null) { EditorUtility.DisplayDialog("Spline Snapshot", "Select a GameObject with SplineContainer.", "OK"); return; }
            var c = go.GetComponent<SplineContainer>();
            if (c == null) { EditorUtility.DisplayDialog("Spline Snapshot", "Selected GameObject has no SplineContainer.", "OK"); return; }

            var path = EditorUtility.SaveFilePanel("Export Spline Snapshot", Application.dataPath, "spline_snapshot.json", "json");
            if (string.IsNullOrEmpty(path)) return;

            var sb = new StringBuilder(256 * 1024);
            sb.Append('{');
            sb.Append("\"local_to_world\":"); M4(sb, c.transform.localToWorldMatrix); sb.Append(',');
            sb.Append("\"splines\":[");
            for (int si = 0; si < c.Splines.Count; ++si)
            {
                var s = c.Splines[si];
                sb.Append('{');
                sb.Append("\"closed\":").Append(s.Closed ? "true" : "false").Append(',');
                sb.Append("\"knots\":[");
                for (int ki = 0; ki < s.Count; ++ki)
                {
                    var k = s[ki];
                    sb.Append('{');
                    sb.Append("\"position\":"); V3(sb, k.Position); sb.Append(',');
                    sb.Append("\"tangent_in\":"); V3(sb, k.TangentIn); sb.Append(',');
                    sb.Append("\"tangent_out\":"); V3(sb, k.TangentOut); sb.Append(',');
                    sb.Append("\"rotation\":"); Q4(sb, k.Rotation); sb.Append(',');
                    sb.Append("\"mode\":").Append((int)s.GetTangentMode(ki)).Append(',');
                    sb.Append("\"tension\":").Append(F(s.GetAutoSmoothTension(ki)));
                    sb.Append('}');
                    if (ki + 1 < s.Count) sb.Append(',');
                }
                sb.Append(']');
                sb.Append('}');
                if (si + 1 < c.Splines.Count) sb.Append(',');
            }
            sb.Append("],");

            sb.Append("\"links\":[");
            var groups = c.Splines
                .SelectMany((s, si) => Enumerable.Range(0, s.Count).Select(ki => new SplineKnotIndex(si, ki)))
                .Select(idx => c.KnotLinkCollection.GetKnotLinks(idx).ToArray())
                .Where(g => g.Length > 1)
                .Select(g => g.OrderBy(x => x.Spline).ThenBy(x => x.Knot).ToArray())
                .GroupBy(g => string.Join(";", g.Select(x => $"{x.Spline}:{x.Knot}")))
                .Select(g => g.First())
                .OrderBy(g => g[0].Spline).ThenBy(g => g[0].Knot)
                .ToArray();
            for (int gi = 0; gi < groups.Length; ++gi)
            {
                var g = groups[gi];
                sb.Append('[');
                for (int i = 0; i < g.Length; ++i)
                {
                    sb.Append('[').Append(g[i].Spline).Append(',').Append(g[i].Knot).Append(']');
                    if (i + 1 < g.Length) sb.Append(',');
                }
                sb.Append(']');
                if (gi + 1 < groups.Length) sb.Append(',');
            }
            sb.Append(']');
            sb.Append('}');

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Spline Snapshot", $"Exported:\n{path}", "OK");
        }
    }
}

