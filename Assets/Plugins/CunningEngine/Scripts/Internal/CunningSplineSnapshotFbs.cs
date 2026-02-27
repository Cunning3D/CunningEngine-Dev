using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Splines;
using CunningEngine.FlatBuffersLite;

namespace CunningEngine
{
    // FlatBuffers writer for crates/cunning_core/schemas/cunning_spline_snapshot.fbs.
    public static class CunningSplineSnapshotFbs
    {
        static void PutVec3(FbsBuilderLite b, Vector3 v) { b.PutFloat(v.z); b.PutFloat(v.y); b.PutFloat(v.x); }
        static void PutVec4(FbsBuilderLite b, Vector4 v) { b.PutFloat(v.w); b.PutFloat(v.z); b.PutFloat(v.y); b.PutFloat(v.x); }
        static void PutQuat(FbsBuilderLite b, Quaternion q) { b.PutFloat(q.w); b.PutFloat(q.z); b.PutFloat(q.y); b.PutFloat(q.x); }
        static void PutMat4(FbsBuilderLite b, Matrix4x4 m) { for (int c = 3; c >= 0; --c) PutVec4(b, m.GetColumn(c)); }
        static void PutKnotRef(FbsBuilderLite b, int spline, int knot) { b.PutInt(knot); b.PutInt(spline); }

        static List<(int spline, int knot)[]> GetLinkGroups(SplineContainer c)
        {
            var groups = c.Splines
                .SelectMany((s, si) => Enumerable.Range(0, s.Count).Select(ki => new SplineKnotIndex(si, ki)))
                .Select(idx => c.KnotLinkCollection.GetKnotLinks(idx).ToArray())
                .Where(g => g.Length > 1)
                .Select(g => g.Select(x => (x.Spline, x.Knot)).OrderBy(x => x.Spline).ThenBy(x => x.Knot).ToArray())
                .GroupBy(g => string.Join(";", g.Select(x => $"{x.Spline}:{x.Knot}")))
                .Select(g => g.First())
                .OrderBy(g => g[0].Spline).ThenBy(g => g[0].Knot)
                .Select(g => g.Select(x => (x.Spline, x.Knot)).ToArray())
                .ToList();
            return groups;
        }

        public static byte[] BuildFbs(SplineContainer c)
        {
            var b = new FbsBuilderLite(256 * 1024);
            var splineOffsets = new int[c.Splines.Count];
            for (int si = 0; si < c.Splines.Count; ++si)
            {
                var s = c.Splines[si];
                var knotOffsets = new int[s.Count];
                for (int ki = 0; ki < s.Count; ++ki)
                {
                    var k = s[ki];
                    b.StartTable(6);
                    b.AddFieldStruct(0, () => PutVec3(b, k.Position));
                    b.AddFieldStruct(1, () => PutVec3(b, k.TangentIn));
                    b.AddFieldStruct(2, () => PutVec3(b, k.TangentOut));
                    b.AddFieldStruct(3, () => PutQuat(b, k.Rotation));
                    b.AddFieldByte(4, (byte)s.GetTangentMode(ki));
                    b.AddFieldFloat(5, s.GetAutoSmoothTension(ki));
                    knotOffsets[ki] = b.EndTable();
                }
                b.StartVector(4, knotOffsets.Length, 4);
                for (int i = knotOffsets.Length - 1; i >= 0; --i) b.PutOffset(knotOffsets[i]);
                var knotsVec = b.EndVector();
                b.StartTable(2);
                b.AddFieldBool(0, s.Closed);
                b.AddFieldOffset(1, knotsVec);
                splineOffsets[si] = b.EndTable();
            }

            b.StartVector(4, splineOffsets.Length, 4);
            for (int i = splineOffsets.Length - 1; i >= 0; --i) b.PutOffset(splineOffsets[i]);
            var splinesVec = b.EndVector();

            var linkGroups = GetLinkGroups(c);
            var linkOffsets = new int[linkGroups.Count];
            for (int gi = 0; gi < linkGroups.Count; ++gi)
            {
                var g = linkGroups[gi];
                b.StartVector(8, g.Length, 4);
                for (int i = g.Length - 1; i >= 0; --i) PutKnotRef(b, g[i].spline, g[i].knot);
                var knotsVec = b.EndVector();
                b.StartTable(1);
                b.AddFieldOffset(0, knotsVec);
                linkOffsets[gi] = b.EndTable();
            }

            b.StartVector(4, linkOffsets.Length, 4);
            for (int i = linkOffsets.Length - 1; i >= 0; --i) b.PutOffset(linkOffsets[i]);
            var linksVec = b.EndVector();

            b.StartTable(4);
            b.AddFieldUInt(0, 1u, 1u);
            b.AddFieldStruct(1, () => PutMat4(b, c.transform.localToWorldMatrix));
            b.AddFieldOffset(2, splinesVec);
            b.AddFieldOffset(3, linksVec);
            var root = b.EndTable();
            return b.Finish(root);
        }
    }
}

