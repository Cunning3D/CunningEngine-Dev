using System;
using UnityEngine;

namespace CunningEngine {
    [ExecuteAlways]
    public sealed class CunningInputUnityMesh : MonoBehaviour, ICunningInputHandle {
        struct UploadStats {
            public int vertexCount;
            public int subMeshCount;
            public int triangleSubMeshCount;
            public int polygonCount;
            public int indexCount;
            public int uploadedAttrCount;
            public double pointUploadMs;
            public double polyUploadMs;
            public double attrUploadMs;
        }

        public ulong currentHandle;
        public ulong CurrentHandle => this ? currentHandle : 0;
        [Header("Debug")]
        public bool detailedTimingLogs = true;
        public float detailedTimingLogMinMs = 1f;
        static bool s_pointAttrApiAvailable = true;
        static bool s_pointAttrApiWarned;

        int _lastHash;
        Mesh _bakedMesh;

        void OnEnable() { TryUpload(); }
        void OnDisable() { Release(); ReleaseBakedMesh(); }
        void Update() { TryUpload(); }

        public static CunningInputUnityMesh GetOrAdd(GameObject go) => go ? go.GetComponent<CunningInputUnityMesh>() ?? go.AddComponent<CunningInputUnityMesh>() : null;

        void Release() {
            if (currentHandle == 0) return;
            NativeMethods.cunning_release_handle(currentHandle);
            currentHandle = 0;
        }

        void ReleaseBakedMesh() {
            if (!_bakedMesh) return;
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(_bakedMesh);
            else Destroy(_bakedMesh);
#else
            Destroy(_bakedMesh);
#endif
            _bakedMesh = null;
        }

        void TryUpload() {
            var totalSw = System.Diagnostics.Stopwatch.StartNew();
            var mesh = GetSourceMesh(out var isDynamicSkin, out var sourceKind, out var sourcePrepMs);
            if (!mesh) {
                bool hadSource = currentHandle != 0 || _lastHash != 0;
                Release();
                _lastHash = 0;
                if (hadSource && ShouldLogTiming(totalSw.Elapsed.TotalMilliseconds)) {
                    Debug.Log($"CunningInputUnityMesh[{name}]: no source mesh ({sourceKind}), total={totalSw.Elapsed.TotalMilliseconds:F3} ms");
                }
                return;
            }

            int hash = ComputeMeshHash(mesh, isDynamicSkin);
            if (hash == _lastHash && currentHandle != 0) return;
            _lastHash = hash;

            ulong handle;
            UploadStats stats = default;
            var buildSw = System.Diagnostics.Stopwatch.StartNew();
            try {
                handle = BuildHandle(mesh, out stats);
            } catch (Exception e) {
                buildSw.Stop();
                Debug.LogWarning($"CunningInputUnityMesh[{name}]: upload failed, source={sourceKind}, prep={sourcePrepMs:F3} ms, build={buildSw.Elapsed.TotalMilliseconds:F3} ms, total={totalSw.Elapsed.TotalMilliseconds:F3} ms, error={e.Message}");
                return;
            }
            buildSw.Stop();

            if (handle == 0) {
                Debug.LogWarning($"CunningInputUnityMesh[{name}]: native upload returned 0, source={sourceKind}, verts={mesh.vertexCount}, subMeshes={mesh.subMeshCount}, prep={sourcePrepMs:F3} ms, build={buildSw.Elapsed.TotalMilliseconds:F3} ms, total={totalSw.Elapsed.TotalMilliseconds:F3} ms");
                return;
            }

            Release();
            currentHandle = handle;
            totalSw.Stop();
            if (ShouldLogTiming(totalSw.Elapsed.TotalMilliseconds)) {
                Debug.Log(
                    $"CunningInputUnityMesh[{name}]: upload ok " +
                    $"source={sourceKind}, mesh={mesh.name}, verts={stats.vertexCount}, subMeshes={stats.subMeshCount}, " +
                    $"triSubMeshes={stats.triangleSubMeshCount}, polys={stats.polygonCount}, indices={stats.indexCount}, " +
                    $"attrs={stats.uploadedAttrCount}, prep={sourcePrepMs:F3} ms, points={stats.pointUploadMs:F3} ms, polysMs={stats.polyUploadMs:F3} ms, attrsMs={stats.attrUploadMs:F3} ms, " +
                    $"build={buildSw.Elapsed.TotalMilliseconds:F3} ms, total={totalSw.Elapsed.TotalMilliseconds:F3} ms"
                );
            }
        }

        bool ShouldLogTiming(double totalMs) => detailedTimingLogs && totalMs >= detailedTimingLogMinMs;

        Mesh GetSourceMesh(out bool isDynamicSkin, out string sourceKind, out double sourcePrepMs) {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            isDynamicSkin = false;
            sourceKind = "none";
            var mf = GetComponent<MeshFilter>();
            if (mf && mf.sharedMesh) {
                sw.Stop();
                sourcePrepMs = sw.Elapsed.TotalMilliseconds;
                sourceKind = "MeshFilter";
                return mf.sharedMesh;
            }

            var smr = GetComponent<SkinnedMeshRenderer>();
            if (!smr || !smr.sharedMesh) {
                sw.Stop();
                sourcePrepMs = sw.Elapsed.TotalMilliseconds;
                sourceKind = smr ? "SkinnedMeshRenderer(no-sharedMesh)" : "missing";
                return null;
            }

            isDynamicSkin = true;
            sourceKind = "SkinnedMeshRenderer";
            if (!_bakedMesh) {
                _bakedMesh = new Mesh {
                    name = $"{name}_CunningInputUnityMesh_Baked"
                };
                _bakedMesh.MarkDynamic();
            } else {
                _bakedMesh.Clear();
            }
            smr.BakeMesh(_bakedMesh);
            sw.Stop();
            sourcePrepMs = sw.Elapsed.TotalMilliseconds;
            return _bakedMesh;
        }

        int ComputeMeshHash(Mesh mesh, bool isDynamicSkin) {
            if (!mesh) return 0;
            unchecked {
                int hash = 17;
                hash = hash * 31 + mesh.GetInstanceID();
                hash = hash * 31 + mesh.vertexCount;
                hash = hash * 31 + mesh.subMeshCount;
                hash = hash * 31 + mesh.bounds.GetHashCode();
                if (isDynamicSkin) {
                    var smr = GetComponent<SkinnedMeshRenderer>();
                    if (smr) {
                        hash = hash * 31 + smr.localBounds.GetHashCode();
                        hash = hash * 31 + smr.quality.GetHashCode();
                        var bones = smr.bones;
                        if (bones != null) {
                            hash = hash * 31 + bones.Length;
                            for (int i = 0; i < bones.Length; i++) {
                                var bone = bones[i];
                                hash = hash * 31 + (bone ? bone.localToWorldMatrix.GetHashCode() : 0);
                            }
                        }
                        int blendShapeCount = mesh.blendShapeCount;
                        hash = hash * 31 + blendShapeCount;
                        for (int i = 0; i < blendShapeCount; i++) {
                            hash = hash * 31 + smr.GetBlendShapeWeight(i).GetHashCode();
                        }
                    }
                }
                return hash;
            }
        }

        static ulong BuildHandle(Mesh mesh, out UploadStats stats) {
            stats = default;
            if (!mesh) return 0;
            var vertices = mesh.vertices;
            if (vertices == null || vertices.Length == 0) return 0;
            stats.vertexCount = vertices.Length;
            stats.subMeshCount = mesh.subMeshCount;

            var handle = NativeMethods.cunning_geo_create();
            if (handle == 0) return 0;

            try {
                var pointIds = new ulong[vertices.Length];
                var pointSw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < vertices.Length; i++) {
                    var p = vertices[i];
                    pointIds[i] = NativeMethods.cunning_geo_add_point(handle, p.x, p.y, p.z);
                }
                pointSw.Stop();
                stats.pointUploadMs = pointSw.Elapsed.TotalMilliseconds;

                int polyCount = 0;
                int indexCount = 0;
                int triangleSubMeshCount = 0;
                var polySw = System.Diagnostics.Stopwatch.StartNew();
                for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++) {
                    if (mesh.GetTopology(subMesh) != MeshTopology.Triangles) continue;
                    triangleSubMeshCount++;
                    var indices = mesh.GetIndices(subMesh);
                    if (indices == null || indices.Length < 3) continue;
                    indexCount += indices.Length;
                    for (int i = 0; i + 2 < indices.Length; i += 3) {
                        var tri = new[] {
                            pointIds[indices[i]],
                            pointIds[indices[i + 1]],
                            pointIds[indices[i + 2]],
                        };
                        NativeMethods.cunning_geo_add_poly(handle, tri, 3);
                        polyCount++;
                    }
                }
                polySw.Stop();
                stats.polyUploadMs = polySw.Elapsed.TotalMilliseconds;
                stats.triangleSubMeshCount = triangleSubMeshCount;
                stats.polygonCount = polyCount;
                stats.indexCount = indexCount;

                if (polyCount == 0) {
                    NativeMethods.cunning_release_handle(handle);
                    return 0;
                }

                UploadPointAttributes(mesh, handle, vertices.Length, ref stats);

                return handle;
            } catch {
                NativeMethods.cunning_release_handle(handle);
                throw;
            }
        }

        static void UploadPointAttributes(Mesh mesh, ulong handle, int pointCount, ref UploadStats stats) {
            if (!mesh || handle == 0 || pointCount <= 0) return;
            var attrSw = System.Diagnostics.Stopwatch.StartNew();
            int uploaded = 0;

            var normals = mesh.normals;
            if (normals != null && normals.Length == pointCount) {
                if (TrySetPointAttrVec3(handle, "@N", FlattenVec3(normals), pointCount)) uploaded++;
            }

            var uv0 = new System.Collections.Generic.List<Vector2>(pointCount);
            mesh.GetUVs(0, uv0);
            if (uv0.Count == pointCount) {
                if (TrySetPointAttrVec2(handle, "@uv", FlattenVec2(uv0), pointCount)) uploaded++;
            }

            var uv2 = new System.Collections.Generic.List<Vector2>(pointCount);
            mesh.GetUVs(1, uv2);
            if (uv2.Count == pointCount) {
                if (TrySetPointAttrVec2(handle, "@uv2", FlattenVec2(uv2), pointCount)) uploaded++;
            }

            var colors32 = mesh.colors32;
            if (colors32 != null && colors32.Length == pointCount) {
                if (TrySetPointAttrVec4(handle, "@Cd", FlattenColor32(colors32), pointCount)) uploaded++;
            } else {
                var colors = mesh.colors;
                if (colors != null && colors.Length == pointCount) {
                    if (TrySetPointAttrVec4(handle, "@Cd", FlattenColor(colors), pointCount)) uploaded++;
                }
            }

            var tangents = mesh.tangents;
            if (tangents != null && tangents.Length == pointCount) {
                if (TrySetPointAttrVec3(handle, "@tangentu", FlattenTangents(tangents), pointCount)) uploaded++;
                if (normals != null && normals.Length == pointCount) {
                    if (TrySetPointAttrVec3(handle, "@tangentv", FlattenBitangents(normals, tangents), pointCount)) uploaded++;
                }
            }

            attrSw.Stop();
            stats.uploadedAttrCount = uploaded;
            stats.attrUploadMs = attrSw.Elapsed.TotalMilliseconds;
        }

        static bool TrySetPointAttrVec2(ulong handle, string name, float[] values, int count) {
            if (!s_pointAttrApiAvailable || handle == 0 || string.IsNullOrWhiteSpace(name) || values == null || values.Length != count * 2) return false;
            try {
                return NativeMethods.cunning_geo_set_point_attr_vec2(handle, name, values, (uint)count) != 0;
            } catch (EntryPointNotFoundException) {
                s_pointAttrApiAvailable = false;
                if (!s_pointAttrApiWarned) {
                    s_pointAttrApiWarned = true;
                    Debug.LogWarning("CunningEngine: Native API missing point-attr setters on cunning_core_ffi.dll; Unity mesh attributes will be skipped until the DLL is rebuilt.");
                }
                return false;
            }
        }

        static bool TrySetPointAttrVec3(ulong handle, string name, float[] values, int count) {
            if (!s_pointAttrApiAvailable || handle == 0 || string.IsNullOrWhiteSpace(name) || values == null || values.Length != count * 3) return false;
            try {
                return NativeMethods.cunning_geo_set_point_attr_vec3(handle, name, values, (uint)count) != 0;
            } catch (EntryPointNotFoundException) {
                s_pointAttrApiAvailable = false;
                if (!s_pointAttrApiWarned) {
                    s_pointAttrApiWarned = true;
                    Debug.LogWarning("CunningEngine: Native API missing point-attr setters on cunning_core_ffi.dll; Unity mesh attributes will be skipped until the DLL is rebuilt.");
                }
                return false;
            }
        }

        static bool TrySetPointAttrVec4(ulong handle, string name, float[] values, int count) {
            if (!s_pointAttrApiAvailable || handle == 0 || string.IsNullOrWhiteSpace(name) || values == null || values.Length != count * 4) return false;
            try {
                return NativeMethods.cunning_geo_set_point_attr_vec4(handle, name, values, (uint)count) != 0;
            } catch (EntryPointNotFoundException) {
                s_pointAttrApiAvailable = false;
                if (!s_pointAttrApiWarned) {
                    s_pointAttrApiWarned = true;
                    Debug.LogWarning("CunningEngine: Native API missing point-attr setters on cunning_core_ffi.dll; Unity mesh attributes will be skipped until the DLL is rebuilt.");
                }
                return false;
            }
        }

        static float[] FlattenVec2(System.Collections.Generic.IList<Vector2> values) {
            var flat = new float[values.Count * 2];
            for (int i = 0; i < values.Count; i++) {
                var value = values[i];
                int baseIndex = i * 2;
                flat[baseIndex] = value.x;
                flat[baseIndex + 1] = value.y;
            }
            return flat;
        }

        static float[] FlattenVec3(Vector3[] values) {
            var flat = new float[values.Length * 3];
            for (int i = 0; i < values.Length; i++) {
                var value = values[i];
                int baseIndex = i * 3;
                flat[baseIndex] = value.x;
                flat[baseIndex + 1] = value.y;
                flat[baseIndex + 2] = value.z;
            }
            return flat;
        }

        static float[] FlattenColor(Color[] values) {
            var flat = new float[values.Length * 4];
            for (int i = 0; i < values.Length; i++) {
                var value = values[i];
                int baseIndex = i * 4;
                flat[baseIndex] = value.r;
                flat[baseIndex + 1] = value.g;
                flat[baseIndex + 2] = value.b;
                flat[baseIndex + 3] = value.a;
            }
            return flat;
        }

        static float[] FlattenColor32(Color32[] values) {
            var flat = new float[values.Length * 4];
            const float inv255 = 1f / 255f;
            for (int i = 0; i < values.Length; i++) {
                var value = values[i];
                int baseIndex = i * 4;
                flat[baseIndex] = value.r * inv255;
                flat[baseIndex + 1] = value.g * inv255;
                flat[baseIndex + 2] = value.b * inv255;
                flat[baseIndex + 3] = value.a * inv255;
            }
            return flat;
        }

        static float[] FlattenTangents(Vector4[] values) {
            var flat = new float[values.Length * 3];
            for (int i = 0; i < values.Length; i++) {
                var value = values[i];
                int baseIndex = i * 3;
                flat[baseIndex] = value.x;
                flat[baseIndex + 1] = value.y;
                flat[baseIndex + 2] = value.z;
            }
            return flat;
        }

        static float[] FlattenBitangents(Vector3[] normals, Vector4[] tangents) {
            int count = Mathf.Min(normals.Length, tangents.Length);
            var flat = new float[count * 3];
            for (int i = 0; i < count; i++) {
                var tangent = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                var bitangent = Vector3.Cross(normals[i], tangent) * tangents[i].w;
                int baseIndex = i * 3;
                flat[baseIndex] = bitangent.x;
                flat[baseIndex + 1] = bitangent.y;
                flat[baseIndex + 2] = bitangent.z;
            }
            return flat;
        }
    }

    sealed class UnityMeshInputResolver : ICunningInputResolver {
        public int Priority => -10;

        public bool CanResolve(UnityEngine.Object obj) {
            var go = obj as GameObject ?? (obj as Component)?.gameObject;
            if (!go) return false;
            if (go.GetComponent<CunningMesh>()) return false;
            var mf = go.GetComponent<MeshFilter>();
            if (mf && mf.sharedMesh) return true;
            var smr = go.GetComponent<SkinnedMeshRenderer>();
            return smr && smr.sharedMesh;
        }

        public MonoBehaviour Resolve(UnityEngine.Object obj) {
            var go = obj as GameObject ?? (obj as Component)?.gameObject;
            return go ? CunningInputUnityMesh.GetOrAdd(go) : null;
        }
    }
}
