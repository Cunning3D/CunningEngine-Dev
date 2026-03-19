using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CunningEngine {
    public interface ICunningPolylineSource {
        int GetCunningPolylineCount();
        bool TryGetCunningPolyline(int index, List<Vector3> points, List<Vector3> normals, out bool closed, out int kindIndex);
    }

    public interface ICunningPolylinePrimitiveIntTagSource {
        bool TryGetCunningPolylinePrimitiveIntTag(int index, out string attrName, out int attrValue);
    }

    [ExecuteAlways]
    public sealed class CunningInputPolyline : MonoBehaviour, ICunningInputHandle {
        const string PointNormalAttr = "@N";
        const string PrimitiveKindAttr = "@polyline_kind";
        const string PrimitiveIndexAttr = "@polyline_index";

        [SerializeField] MonoBehaviour sourceComponent;

        public ulong currentHandle;
        public ulong CurrentHandle => this ? currentHandle : 0;

        readonly List<PolylineSnapshot> snapshots = new();
        readonly List<Vector3> workingPoints = new();
        readonly List<Vector3> workingNormals = new();
        int lastHash;

        sealed class PolylineSnapshot {
            public Vector3[] points;
            public Vector3[] normals;
            public bool closed;
            public int kindIndex;
            public string primitiveIntTagName;
            public int primitiveIntTagValue;
        }

        void OnEnable() { TryUpload(); }
        void OnDisable() { Release(); }
        void Update() { TryUpload(); }

        public static CunningInputPolyline GetOrAdd(GameObject go) => go ? go.GetComponent<CunningInputPolyline>() ?? go.AddComponent<CunningInputPolyline>() : null;

        void Release() {
            if (currentHandle == 0) return;
            NativeMethods.cunning_release_handle(currentHandle);
            currentHandle = 0;
        }

        ICunningPolylineSource ResolveSource() {
            if (sourceComponent is ICunningPolylineSource explicitSource) {
                return explicitSource;
            }

            var components = GetComponents<MonoBehaviour>();
            for (int i = 0; i < components.Length; i++) {
                var component = components[i];
                if (!component || ReferenceEquals(component, this)) continue;
                if (component is ICunningPolylineSource source) {
                    if (sourceComponent == null) sourceComponent = component;
                    return source;
                }
            }

            return null;
        }

        void EnsureHandle() {
            if (currentHandle != 0) return;
            currentHandle = NativeMethods.cunning_geo_create();
        }

        void TryUpload() {
            var source = ResolveSource();
            if (source == null) {
                Release();
                lastHash = 0;
                return;
            }

            int totalPointCount;
            int hash = CollectSnapshots(source, out totalPointCount);
            if (hash == lastHash && currentHandle != 0) {
                return;
            }

            EnsureHandle();
            if (currentHandle == 0) return;

            if (NativeMethods.cunning_geo_clear(currentHandle) == 0) {
                Release();
                EnsureHandle();
                if (currentHandle == 0) return;
            }

            lastHash = hash;

            if (snapshots.Count == 0 || totalPointCount == 0) {
                NativeMethods.cunning_geo_sync_cache(currentHandle);
                return;
            }

            UploadSnapshots(totalPointCount);
        }

        int CollectSnapshots(ICunningPolylineSource source, out int totalPointCount) {
            snapshots.Clear();
            totalPointCount = 0;

            int polylineCount = Mathf.Max(0, source.GetCunningPolylineCount());
            var primitiveTagSource = source as ICunningPolylinePrimitiveIntTagSource;
            int hash = 17;
            hash = hash * 31 + polylineCount;

            for (int polylineIndex = 0; polylineIndex < polylineCount; polylineIndex++) {
                workingPoints.Clear();
                workingNormals.Clear();

                bool closed;
                int kindIndex;
                if (!source.TryGetCunningPolyline(polylineIndex, workingPoints, workingNormals, out closed, out kindIndex)) {
                    continue;
                }

                if (workingPoints.Count < 2) {
                    continue;
                }

                NormalizeNormals(workingPoints, workingNormals, closed);

                var snapshot = new PolylineSnapshot {
                    points = workingPoints.ToArray(),
                    normals = workingNormals.ToArray(),
                    closed = closed,
                    kindIndex = kindIndex,
                };

                if (primitiveTagSource != null &&
                    primitiveTagSource.TryGetCunningPolylinePrimitiveIntTag(polylineIndex, out var attrName, out var attrValue) &&
                    !string.IsNullOrWhiteSpace(attrName))
                {
                    snapshot.primitiveIntTagName = attrName;
                    snapshot.primitiveIntTagValue = attrValue;
                    hash = hash * 31 + attrName.GetHashCode();
                    hash = hash * 31 + attrValue;
                }

                snapshots.Add(snapshot);
                totalPointCount += snapshot.points.Length;

                hash = hash * 31 + snapshot.points.Length;
                hash = hash * 31 + (snapshot.closed ? 1 : 0);
                hash = hash * 31 + snapshot.kindIndex;

                for (int i = 0; i < snapshot.points.Length; i++) {
                    hash = hash * 31 + snapshot.points[i].GetHashCode();
                    hash = hash * 31 + snapshot.normals[i].GetHashCode();
                }
            }

            return hash;
        }

        static void NormalizeNormals(List<Vector3> points, List<Vector3> normals, bool closed) {
            if (normals.Count == points.Count) {
                for (int i = 0; i < normals.Count; i++) {
                    normals[i] = SafeNormalize(normals[i], Vector3.forward);
                }
                return;
            }

            normals.Clear();
            for (int i = 0; i < points.Count; i++) {
                Vector3 tangent;
                if (points.Count == 2) {
                    tangent = points[1] - points[0];
                } else if (i == 0) {
                    tangent = closed ? points[1] - points[points.Count - 1] : points[1] - points[0];
                } else if (i == points.Count - 1) {
                    tangent = closed ? points[0] - points[i - 1] : points[i] - points[i - 1];
                } else {
                    tangent = points[i + 1] - points[i - 1];
                }

                tangent.y = 0f;
                Vector3 fallback = Vector3.Cross(Vector3.up, SafeNormalize(tangent, Vector3.right));
                normals.Add(SafeNormalize(fallback, Vector3.forward));
            }
        }

        static Vector3 SafeNormalize(Vector3 value, Vector3 fallback) {
            return value.sqrMagnitude > 1e-8f ? value.normalized : fallback;
        }

        void UploadSnapshots(int totalPointCount) {
            int primitiveCount = snapshots.Count;
            var positions = new float[totalPointCount * 3];
            var normals = new float[totalPointCount * 3];
            var pointIds = new ulong[totalPointCount];
            var primitiveKinds = new int[primitiveCount];
            var primitiveIndices = new int[primitiveCount];
            var primitiveIntAttrValues = new Dictionary<string, int[]>(StringComparer.Ordinal);

            int pointOffset = 0;
            for (int primitiveIndex = 0; primitiveIndex < primitiveCount; primitiveIndex++) {
                var snapshot = snapshots[primitiveIndex];
                primitiveKinds[primitiveIndex] = snapshot.kindIndex;
                primitiveIndices[primitiveIndex] = primitiveIndex;

                if (!string.IsNullOrWhiteSpace(snapshot.primitiveIntTagName)) {
                    if (!primitiveIntAttrValues.TryGetValue(snapshot.primitiveIntTagName, out var values)) {
                        values = new int[primitiveCount];
                        primitiveIntAttrValues.Add(snapshot.primitiveIntTagName, values);
                    }
                    values[primitiveIndex] = snapshot.primitiveIntTagValue;
                }

                for (int i = 0; i < snapshot.points.Length; i++) {
                    int flatIndex = (pointOffset + i) * 3;
                    var point = snapshot.points[i];
                    var normal = snapshot.normals[i];
                    positions[flatIndex] = point.x;
                    positions[flatIndex + 1] = point.y;
                    positions[flatIndex + 2] = point.z;
                    normals[flatIndex] = normal.x;
                    normals[flatIndex + 1] = normal.y;
                    normals[flatIndex + 2] = normal.z;
                }

                pointOffset += snapshot.points.Length;
            }

            if (NativeMethods.cunning_geo_add_points_bulk_nosync(currentHandle, positions, (uint)totalPointCount, pointIds) == 0) {
                Debug.LogWarning($"CunningInputPolyline[{name}]: failed to upload points");
                return;
            }

            var pointIdsHandle = GCHandle.Alloc(pointIds, GCHandleType.Pinned);
            try {
                IntPtr basePtr = pointIdsHandle.AddrOfPinnedObject();
                pointOffset = 0;

                for (int primitiveIndex = 0; primitiveIndex < primitiveCount; primitiveIndex++) {
                    var snapshot = snapshots[primitiveIndex];
                    IntPtr pointPtr = IntPtr.Add(basePtr, pointOffset * sizeof(ulong));
                    if (NativeMethods.cunning_geo_add_polyline(currentHandle, pointPtr, (uint)snapshot.points.Length, snapshot.closed ? 1u : 0u) == 0) {
                        Debug.LogWarning($"CunningInputPolyline[{name}]: failed to upload polyline {primitiveIndex}");
                    }
                    pointOffset += snapshot.points.Length;
                }
            } finally {
                pointIdsHandle.Free();
            }

            NativeMethods.cunning_geo_set_point_attr_vec3_nosync(currentHandle, PointNormalAttr, normals, (uint)totalPointCount);
            NativeMethods.cunning_geo_set_primitive_attr_i32_nosync(currentHandle, PrimitiveKindAttr, primitiveKinds, (uint)primitiveCount);
            NativeMethods.cunning_geo_set_primitive_attr_i32_nosync(currentHandle, PrimitiveIndexAttr, primitiveIndices, (uint)primitiveCount);
            foreach (var primitiveIntAttr in primitiveIntAttrValues) {
                NativeMethods.cunning_geo_set_primitive_attr_i32_nosync(currentHandle, primitiveIntAttr.Key, primitiveIntAttr.Value, (uint)primitiveCount);
            }
            NativeMethods.cunning_geo_sync_cache(currentHandle);
        }
    }

    sealed class PolylineInputResolver : ICunningInputResolver {
        public int Priority => 5;

        public bool CanResolve(UnityEngine.Object obj) {
            var go = obj as GameObject ?? (obj as Component)?.gameObject;
            if (!go) return false;

            var components = go.GetComponents<MonoBehaviour>();
            for (int i = 0; i < components.Length; i++) {
                if (components[i] is ICunningPolylineSource) {
                    return true;
                }
            }

            return false;
        }

        public MonoBehaviour Resolve(UnityEngine.Object obj) {
            var go = obj as GameObject ?? (obj as Component)?.gameObject;
            return go ? CunningInputPolyline.GetOrAdd(go) : null;
        }
    }
}
