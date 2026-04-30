using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CunningEngine {
    [ExecuteAlways]
    public sealed class CunningInstancer : MonoBehaviour {
        public ulong currentHandle;
        public ulong CurrentHandle => currentHandle;

        [Header("Instance Attrs (Houdini-style)")]
        public string instanceAttr = "@instance";
        public string unityInstanceAttr = "@unity_instance";
        public string packedIdAttr = "@c3d_packed_id";
        public string pAttr = "@P";
        public string pscaleAttr = "@pscale";
        public string orientAttr = "@orient";
        public string nAttr = "@N";
        public string upAttr = "@up";

        [Header("Behaviour")]
        public bool enablePackedMeshInstancing = true;
        public bool enableUnityPrefabInstancing = true;
        public bool hideMeshRendererWhenActive = true;

        [Header("Packed Mesh Instancing")]
        public Material packedMaterialOverride;

        [Header("Unity Prefab Instancing")]
        public Transform prefabRoot;
        public bool allowAssetDatabaseInEditor = true;

        ulong lastDirtyId;
        bool lastHadAnyInstances;

        readonly StringBuilder s_sb = new StringBuilder(4096);

        sealed class PackedProto {
            public Mesh mesh;
            public Material material;
            public Matrix4x4[] world;
            public Matrix4x4[] scratch1023;
        }

        sealed class PrefabGroup {
            public GameObject prefab;
            public readonly List<GameObject> instances = new List<GameObject>();
            public bool warnedMissing;
        }

        readonly Dictionary<string, PackedProto> packed = new Dictionary<string, PackedProto>();
        readonly Dictionary<string, PrefabGroup> prefabs = new Dictionary<string, PrefabGroup>();

        public void LoadFromHandle(ulong handle) {
            if (currentHandle == handle) return;
            currentHandle = handle;
            lastDirtyId = 0;
            Rebuild();
        }

        void OnDisable() { ClearAll(); }

        void LateUpdate() {
            if (currentHandle == 0) {
                if (lastHadAnyInstances) ClearAll();
                return;
            }

            var dirty = NativeMethods.cunning_geo_get_dirty_id(currentHandle);
            if (dirty != lastDirtyId || transform.hasChanged) {
                lastDirtyId = dirty;
                transform.hasChanged = false;
                Rebuild();
            }

            DrawPacked();
        }

        void ClearAll() {
            ClearPacked();
            ClearPrefabs();
            lastHadAnyInstances = false;
            ApplyMeshVisibility(false);
        }

        void ClearPacked() {
            foreach (var kv in packed) {
                var p = kv.Value;
                if (p.mesh != null) {
#if UNITY_EDITOR
                    if (!Application.isPlaying) DestroyImmediate(p.mesh);
                    else Destroy(p.mesh);
#else
                    Destroy(p.mesh);
#endif
                }
            }
            packed.Clear();
        }

        void ClearPrefabs() {
            foreach (var kv in prefabs) {
                var g = kv.Value;
                for (int i = 0; i < g.instances.Count; i++) {
                    var go = g.instances[i];
                    if (go == null) continue;
#if UNITY_EDITOR
                    if (!Application.isPlaying) DestroyImmediate(go);
                    else Destroy(go);
#else
                    Destroy(go);
#endif
                }
            }
            prefabs.Clear();
        }

        void Rebuild() {
            ClearPacked();
            ClearPrefabs();

            if (currentHandle == 0) return;
            var ptCount = (int)NativeMethods.cunning_geo_get_point_count(currentHandle);
            if (ptCount <= 0) {
                ApplyMeshVisibility(false);
                return;
            }

            var rootM = transform.localToWorldMatrix;
            bool any = false;

            if (enableUnityPrefabInstancing) {
                var unityCount = (int)NativeMethods.cunning_geo_get_attr_string_count(currentHandle, 1, unityInstanceAttr);
                if (unityCount > 0) {
                    any |= BuildPrefabInstances(rootM, ptCount, unityInstanceAttr);
                }
            }

            if (enablePackedMeshInstancing) {
                var primPackedCount = (int)NativeMethods.cunning_geo_get_attr_string_count(currentHandle, 3, packedIdAttr);
                var instCount = (int)NativeMethods.cunning_geo_get_attr_string_count(currentHandle, 1, instanceAttr);
                if (primPackedCount > 0 && instCount > 0) {
                    any |= BuildPackedInstances(rootM, ptCount);
                }
            }

            lastHadAnyInstances = any;
            ApplyMeshVisibility(any);
        }

        void ApplyMeshVisibility(bool hasInstances) {
            if (!hideMeshRendererWhenActive) return;
            var r = GetComponent<MeshRenderer>();
            if (r == null) return;
            r.enabled = !hasInstances;
        }

        bool BuildPackedInstances(Matrix4x4 rootM, int ptCount) {
            var ids = ReadStringAttr(1, instanceAttr, ptCount);
            if (ids == null) return false;

            var pos = ReadVec3Attr(pAttr, ptCount);
            if (pos == null) return false;
            var pscale = ReadF32Attr(pscaleAttr, ptCount);
            var orient = ReadVec4Attr(orientAttr, ptCount);
            var n = orient == null ? ReadVec3Attr(nAttr, ptCount) : null;
            var up = orient == null ? ReadVec3Attr(upAttr, ptCount) : null;

            var pointsById = new Dictionary<string, List<int>>(16);
            var nIds = Mathf.Min(ptCount, ids.Length);
            for (int i = 0; i < nIds; i++) {
                var id = ids[i];
                if (string.IsNullOrEmpty(id)) continue;
                if (!pointsById.TryGetValue(id, out var list)) {
                    list = new List<int>();
                    pointsById[id] = list;
                }
                list.Add(i);
            }
            if (pointsById.Count == 0) return false;

            var mat = ResolvePackedMaterial();
            if (mat != null) mat.enableInstancing = true;

            bool any = false;
            foreach (var kv in pointsById) {
                var id = kv.Key;
                var pts = kv.Value;
                if (pts == null || pts.Count == 0) continue;

                if (!NativeMethods.TryGeoExtractPacked(currentHandle, id, out var protoHandle) || protoHandle == 0) continue;
                try {
                    var mesh = CunningNativeMeshBuilder.BuildMeshFromHandle(protoHandle, "PackedProto_" + id);
                    if (mesh == null) continue;

                    var p = new PackedProto();
                    p.mesh = mesh;
                    p.material = mat;
                    p.world = new Matrix4x4[pts.Count];
                    p.scratch1023 = new Matrix4x4[Mathf.Min(1023, pts.Count)];

                    for (int ii = 0; ii < pts.Count; ii++) {
                        var pi = pts[ii];
                        var t = CunningUnityCoordinates.ToUnityPosition(pos[pi]);
                        var s = (pscale != null && pi < pscale.Length) ? pscale[pi] : 1f;
                        var q = orient != null
                            ? CunningUnityCoordinates.ToUnityRotation(new Quaternion(orient[pi].x, orient[pi].y, orient[pi].z, orient[pi].w))
                            : ComputeRotationFromNUp(n, up, pi);
                        p.world[ii] = rootM * Matrix4x4.TRS(t, q, Vector3.one * s);
                    }

                    packed[id] = p;
                    any = true;
                } finally {
                    NativeMethods.cunning_release_handle(protoHandle);
                }
            }
            return any;
        }

        bool BuildPrefabInstances(Matrix4x4 rootM, int ptCount, string attrName) {
            var ids = ReadStringAttr(1, attrName, ptCount);
            if (ids == null) return false;

            var pos = ReadVec3Attr(pAttr, ptCount);
            if (pos == null) return false;
            var pscale = ReadF32Attr(pscaleAttr, ptCount);
            var orient = ReadVec4Attr(orientAttr, ptCount);
            var n = orient == null ? ReadVec3Attr(nAttr, ptCount) : null;
            var up = orient == null ? ReadVec3Attr(upAttr, ptCount) : null;

            var pointsByPrefab = new Dictionary<string, List<int>>(16);
            var nIds = Mathf.Min(ptCount, ids.Length);
            for (int i = 0; i < nIds; i++) {
                var k = ids[i];
                if (string.IsNullOrEmpty(k)) continue;
                if (!pointsByPrefab.TryGetValue(k, out var list)) {
                    list = new List<int>();
                    pointsByPrefab[k] = list;
                }
                list.Add(i);
            }
            if (pointsByPrefab.Count == 0) return false;

            var root = prefabRoot != null ? prefabRoot : transform;
            bool any = false;
            foreach (var kv in pointsByPrefab) {
                var key = kv.Key;
                var pts = kv.Value;
                if (pts == null || pts.Count == 0) continue;

                if (!prefabs.TryGetValue(key, out var group)) {
                    group = new PrefabGroup();
                    group.prefab = ResolvePrefab(key);
                    prefabs[key] = group;
                }

                if (group.prefab == null) {
                    if (!group.warnedMissing) {
                        group.warnedMissing = true;
                        Debug.LogWarning($"CunningInstancer: prefab not found for '{key}' (attr {attrName}).");
                    }
                    continue;
                }

                // Resize pool.
                while (group.instances.Count < pts.Count) {
                    var go = Instantiate(group.prefab, root);
                    go.name = $"{group.prefab.name}_inst";
                    group.instances.Add(go);
                }
                while (group.instances.Count > pts.Count) {
                    var last = group.instances[group.instances.Count - 1];
                    group.instances.RemoveAt(group.instances.Count - 1);
                    if (last != null) {
#if UNITY_EDITOR
                        if (!Application.isPlaying) DestroyImmediate(last);
                        else Destroy(last);
#else
                        Destroy(last);
#endif
                    }
                }

                // Apply transforms.
                for (int ii = 0; ii < pts.Count; ii++) {
                    var pi = pts[ii];
                    var go = group.instances[ii];
                    if (go == null) continue;
                    var t = CunningUnityCoordinates.ToUnityPosition(pos[pi]);
                    var s = (pscale != null && pi < pscale.Length) ? pscale[pi] : 1f;
                    var q = orient != null
                        ? CunningUnityCoordinates.ToUnityRotation(new Quaternion(orient[pi].x, orient[pi].y, orient[pi].z, orient[pi].w))
                        : ComputeRotationFromNUp(n, up, pi);
                    var m = rootM * Matrix4x4.TRS(t, q, Vector3.one * s);
                    ApplyWorldMatrix(go.transform, m);
                }

                any = true;
            }
            return any;
        }

        void DrawPacked() {
            if (packed.Count == 0) return;
            foreach (var kv in packed) {
                var p = kv.Value;
                if (p == null || p.mesh == null || p.material == null || p.world == null) continue;

                int n = p.world.Length;
                if (n <= 0) continue;
                int i = 0;
                while (i < n) {
                    int batch = Mathf.Min(1023, n - i);
                    if (p.scratch1023 == null || p.scratch1023.Length < batch) p.scratch1023 = new Matrix4x4[batch];
                    Array.Copy(p.world, i, p.scratch1023, 0, batch);
                    Graphics.DrawMeshInstanced(p.mesh, 0, p.material, p.scratch1023, batch);
                    i += batch;
                }
            }
        }

        Material ResolvePackedMaterial() {
            if (packedMaterialOverride != null) return packedMaterialOverride;
            var r = GetComponent<MeshRenderer>();
            if (r != null && r.sharedMaterials != null && r.sharedMaterials.Length > 0 && r.sharedMaterials[0] != null) return r.sharedMaterials[0];
            var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
            return new Material(lit);
        }

        GameObject ResolvePrefab(string key) {
            if (string.IsNullOrEmpty(key)) return null;
#if UNITY_EDITOR
            if (allowAssetDatabaseInEditor && !Application.isPlaying) {
                if (key.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) && key.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) {
                    return AssetDatabase.LoadAssetAtPath<GameObject>(key);
                }
            }
#endif
            return Resources.Load<GameObject>(key);
        }

        static void ApplyWorldMatrix(Transform t, Matrix4x4 m) {
            var pos = m.GetColumn(3);
            t.position = new Vector3(pos.x, pos.y, pos.z);
            t.rotation = m.rotation;
            var ws = m.lossyScale;
            if (t.parent != null) {
                var ps = t.parent.lossyScale;
                t.localScale = new Vector3(
                    Mathf.Abs(ps.x) > 1e-12f ? (ws.x / ps.x) : ws.x,
                    Mathf.Abs(ps.y) > 1e-12f ? (ws.y / ps.y) : ws.y,
                    Mathf.Abs(ps.z) > 1e-12f ? (ws.z / ps.z) : ws.z
                );
            } else {
                t.localScale = ws;
            }
        }

        Quaternion ComputeRotationFromNUp(Vector3[] n, Vector3[] up, int i) {
            if (n == null || i < 0 || i >= n.Length) return Quaternion.identity;
            var forward = n[i];
            if (forward.sqrMagnitude < 1e-12f) return Quaternion.identity;
            var u = (up != null && i < up.Length) ? up[i] : Vector3.up;
            if (u.sqrMagnitude < 1e-12f) u = Vector3.up;
            return CunningUnityCoordinates.ToUnityRotation(Quaternion.LookRotation(forward.normalized, u.normalized));
        }

        Vector3[] ReadVec3Attr(string name, int count) {
            if (string.IsNullOrEmpty(name) || count <= 0) return null;
            var buf = new Vector3[count];
            var h = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try {
                var n = NativeMethods.cunning_geo_copy_attr_f32(currentHandle, 1, name, h.AddrOfPinnedObject(), (uint)(count * 3));
                if (n == 0) return null;
                return buf;
            } finally {
                h.Free();
            }
        }

        Vector4[] ReadVec4Attr(string name, int count) {
            if (string.IsNullOrEmpty(name) || count <= 0) return null;
            var buf = new Vector4[count];
            var h = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try {
                var n = NativeMethods.cunning_geo_copy_attr_f32(currentHandle, 1, name, h.AddrOfPinnedObject(), (uint)(count * 4));
                if (n == 0) return null;
                return buf;
            } finally {
                h.Free();
            }
        }

        float[] ReadF32Attr(string name, int count) {
            if (string.IsNullOrEmpty(name) || count <= 0) return null;
            var buf = new float[count];
            var h = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try {
                var n = NativeMethods.cunning_geo_copy_attr_f32(currentHandle, 1, name, h.AddrOfPinnedObject(), (uint)count);
                if (n == 0) return null;
                return buf;
            } finally {
                h.Free();
            }
        }

        string[] ReadStringAttr(uint attrClass, string name, int count) {
            if (string.IsNullOrEmpty(name) || count <= 0) return null;
            var n = (int)NativeMethods.cunning_geo_get_attr_string_count(currentHandle, attrClass, name);
            if (n <= 0) return null;
            var outArr = new string[Mathf.Min(n, count)];
            for (int i = 0; i < outArr.Length; i++) {
                s_sb.Clear();
                NativeMethods.cunning_geo_get_attr_string(currentHandle, attrClass, name, (uint)i, s_sb, (uint)s_sb.Capacity);
                var s = s_sb.ToString();
                outArr[i] = string.IsNullOrEmpty(s) ? null : s;
            }
            return outArr;
        }

    }
}
