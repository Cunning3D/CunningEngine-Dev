using System.Collections.Generic;
using UnityEngine;

namespace CunningEngine {
    internal static class CunningUnityCoordinates {
        // Mirrors Cunning InternalBevy into Unity's left-handed scene basis.
        internal const bool ReversesWinding = true;

        internal static Vector3 ToUnityPosition(Vector3 value) {
            return new Vector3(value.x, value.y, -value.z);
        }

        internal static Vector3 ToUnityDirection(Vector3 value) {
            return new Vector3(value.x, value.y, -value.z);
        }

        internal static Quaternion ToUnityRotation(Quaternion value) {
            var mapped = new Quaternion(-value.x, -value.y, value.z, value.w);
            var lenSq = mapped.x * mapped.x + mapped.y * mapped.y + mapped.z * mapped.z + mapped.w * mapped.w;
            if (lenSq <= 1e-12f) return Quaternion.identity;
            var invLen = 1.0f / Mathf.Sqrt(lenSq);
            return new Quaternion(mapped.x * invLen, mapped.y * invLen, mapped.z * invLen, mapped.w * invLen);
        }

        internal static void ConvertPositions(Vector3[] values) {
            if (values == null) return;
            for (int i = 0; i < values.Length; i++) {
                values[i] = ToUnityPosition(values[i]);
            }
        }

        internal static void ConvertNormals(Vector3[] values) {
            if (values == null) return;
            for (int i = 0; i < values.Length; i++) {
                values[i] = ToUnityDirection(values[i]);
            }
        }

        internal static void ConvertRenderVertices(NativeMethods.GeoRenderVertex[] values, int count) {
            if (values == null || count <= 0) return;
            var n = Mathf.Min(count, values.Length);
            for (int i = 0; i < n; i++) {
                var vertex = values[i];
                var position = ToUnityPosition(new Vector3(vertex.px, vertex.py, vertex.pz));
                var normal = ToUnityDirection(new Vector3(vertex.nx, vertex.ny, vertex.nz));
                vertex.px = position.x;
                vertex.py = position.y;
                vertex.pz = position.z;
                vertex.nx = normal.x;
                vertex.ny = normal.y;
                vertex.nz = normal.z;
                values[i] = vertex;
            }
        }

        internal static void AddTriangle(List<int> triangles, int a, int b, int c) {
            if (triangles == null) return;
            if (ReversesWinding) {
                triangles.Add(a);
                triangles.Add(c);
                triangles.Add(b);
                return;
            }
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        internal static void FlipTriangleWindingInPlace(int[] triangles) {
            if (!ReversesWinding || triangles == null) return;
            for (int i = 0; i + 2 < triangles.Length; i += 3) {
                var tmp = triangles[i + 1];
                triangles[i + 1] = triangles[i + 2];
                triangles[i + 2] = tmp;
            }
        }
    }
}
