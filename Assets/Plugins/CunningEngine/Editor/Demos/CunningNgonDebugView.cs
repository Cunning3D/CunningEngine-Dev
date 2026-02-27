using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CunningEngine.Editor.Demos {
    [InitializeOnLoad]
    static class CunningNgonDebugView {
        sealed class Cache {
            public ulong dirty;
            public Vector3[] points = Array.Empty<Vector3>();
            public int[] primOffsets = Array.Empty<int>();
            public int[] primPointIdx = Array.Empty<int>();
            public Vector3[] primC = Array.Empty<Vector3>();
            public Vector3[] primN = Array.Empty<Vector3>();
            public Vector3[] pointN = Array.Empty<Vector3>();
        }

        static readonly Dictionary<ulong, Cache> caches = new Dictionary<ulong, Cache>(128);

        static CunningNgonDebugView() => SceneView.duringSceneGui += OnSceneGUI;

        static bool HasPrimFFI() {
            try { NativeMethods.cunning_geo_get_dirty_id(0); return true; }
            catch (EntryPointNotFoundException) { return false; }
        }

        static void OnSceneGUI(SceneView sv) {
            if (sv == null || !CunningSceneViewModeState.Enabled || Event.current.type != EventType.Repaint) return;
            var mode = CunningSceneViewModeState.Mode;
            if (mode == CunningSceneViewModeState.ModeNone) return;
            if (mode != CunningSceneViewModeState.ModePointNumber && !HasPrimFFI()) { Hint("Cunning: 需要更新 cunning_core_ffi.dll 才能显示 Prim/Normal（目前只能用 Point#）"); return; }
            var selected = GetSelectedCunningMeshes();
            if (selected.Length == 0) { Hint("Cunning: 请先选中一个 CunningMesh"); return; }
            var withHandle = 0;
            var drawn = 0;
            foreach (var cm in selected) {
                if (cm == null || cm.currentHandle == 0) continue;
                withHandle++;
                if (mode == CunningSceneViewModeState.ModePointNumber && TryGetPointsOnly(cm.currentHandle, out var c0)) { Draw(cm.transform.localToWorldMatrix, c0, mode); drawn++; continue; }
                if (!TryGet(cm.currentHandle, out var c)) continue;
                Draw(cm.transform.localToWorldMatrix, c, mode);
                drawn++;
            }
            if (withHandle == 0) Hint("Cunning: 选中的 CunningMesh currentHandle=0（请先 LoadFromHandle/生成几何）");
            else if (drawn == 0) Hint("Cunning: 未能读取几何数据（检查 DLL/handle 是否有效）");
        }

        static CunningMesh[] GetSelectedCunningMeshes() {
            var gos = Selection.gameObjects;
            if (gos == null || gos.Length == 0) return Array.Empty<CunningMesh>();
            var set = new HashSet<CunningMesh>();
            foreach (var go in gos) {
                if (!go) continue;
                var cm = go.GetComponent<CunningMesh>() ?? go.GetComponentInParent<CunningMesh>();
                if (cm) set.Add(cm);
            }
            var r = new CunningMesh[set.Count];
            set.CopyTo(r);
            return r;
        }

        static readonly HashSet<string> hinted = new HashSet<string>();
        static void Hint(string msg) {
            if (hinted.Add(msg)) Debug.LogWarning(msg);
            Handles.BeginGUI();
            var r = new Rect(10, 10, 560, 22);
            GUI.Box(r, GUIContent.none);
            GUI.Label(r, msg, EditorStyles.label);
            Handles.EndGUI();
        }

        static bool TryGetPointsOnly(ulong h, out Cache c) {
            if (!caches.TryGetValue(h, out c)) caches[h] = c = new Cache();
            var pc = (int)NativeMethods.cunning_geo_get_point_count(h);
            if (pc <= 0) return false;
            var pf = new float[pc * 3];
            var gch = GCHandle.Alloc(pf, GCHandleType.Pinned);
            try { NativeMethods.cunning_geo_copy_points(h, gch.AddrOfPinnedObject()); }
            finally { gch.Free(); }
            c.points = new Vector3[pc];
            for (var i = 0; i < pc; i++) c.points[i] = new Vector3(pf[i * 3 + 0], pf[i * 3 + 1], pf[i * 3 + 2]);
            c.primOffsets = Array.Empty<int>(); c.primPointIdx = Array.Empty<int>(); c.primC = Array.Empty<Vector3>(); c.primN = Array.Empty<Vector3>(); c.pointN = Array.Empty<Vector3>();
            return true;
        }

        static bool TryGet(ulong h, out Cache c) {
            if (!caches.TryGetValue(h, out c)) caches[h] = c = new Cache();
            ulong dirty;
            try { dirty = NativeMethods.cunning_geo_get_dirty_id(h); }
            catch (EntryPointNotFoundException) { return false; }
            if (dirty == 0 || dirty == c.dirty) return dirty != 0;
            c.dirty = dirty;

            var pc = (int)NativeMethods.cunning_geo_get_point_count(h);
            if (pc <= 0) return false;
            var pf = new float[pc * 3];
            var gch = GCHandle.Alloc(pf, GCHandleType.Pinned);
            try { NativeMethods.cunning_geo_copy_points(h, gch.AddrOfPinnedObject()); }
            finally { gch.Free(); }
            c.points = new Vector3[pc];
            for (var i = 0; i < pc; i++) c.points[i] = new Vector3(pf[i * 3 + 0], pf[i * 3 + 1], pf[i * 3 + 2]);

            int oc, ic;
            try {
                oc = (int)NativeMethods.cunning_geo_copy_prim_point_offsets(h, IntPtr.Zero);
                ic = (int)NativeMethods.cunning_geo_copy_prim_point_indices(h, IntPtr.Zero);
            } catch (EntryPointNotFoundException) { ResetDerivedNoPrim(c); return true; }
            if (oc <= 1 || ic <= 0) { ResetDerivedNoPrim(c); return true; }

            c.primOffsets = new int[oc];
            c.primPointIdx = new int[ic];
            var hO = GCHandle.Alloc(c.primOffsets, GCHandleType.Pinned);
            var hI = GCHandle.Alloc(c.primPointIdx, GCHandleType.Pinned);
            try {
                NativeMethods.cunning_geo_copy_prim_point_offsets(h, hO.AddrOfPinnedObject());
                NativeMethods.cunning_geo_copy_prim_point_indices(h, hI.AddrOfPinnedObject());
            } finally { hO.Free(); hI.Free(); }

            if (!ValidatePrimPointStream(c)) { ResetDerivedNoPrim(c); return true; }
            RebuildDerived(c);
            return true;
        }

        static void ResetDerivedNoPrim(Cache c) {
            c.primOffsets = Array.Empty<int>(); c.primPointIdx = Array.Empty<int>();
            c.primC = Array.Empty<Vector3>(); c.primN = Array.Empty<Vector3>();
            c.pointN = (c.points != null) ? new Vector3[c.points.Length] : Array.Empty<Vector3>();
        }

        static bool ValidatePrimPointStream(Cache c) {
            if (c.primOffsets == null || c.primOffsets.Length < 2 || c.primPointIdx == null) return false;
            if (c.primOffsets[0] != 0) return false;
            var last = c.primPointIdx.Length;
            var prev = 0;
            for (var i = 1; i < c.primOffsets.Length; i++) {
                var v = c.primOffsets[i];
                if (v < prev || v > last) return false;
                prev = v;
            }
            return c.primOffsets[c.primOffsets.Length - 1] == last;
        }

        static void RebuildDerived(Cache c) {
            var primCount = Math.Max(0, c.primOffsets.Length - 1);
            c.primC = new Vector3[primCount];
            c.primN = new Vector3[primCount];
            c.pointN = new Vector3[c.points.Length];
            var pnCount = new int[c.points.Length];

            for (var pi = 0; pi < primCount; pi++) {
                var a = c.primOffsets[pi];
                var b = c.primOffsets[pi + 1];
                var n = b - a;
                if (n < 3) continue;
                Vector3 cen = Vector3.zero, normal = Vector3.zero;
                for (var i = 0; i < n; i++) cen += c.points[c.primPointIdx[a + i]];
                cen /= n;
                for (var i = 0; i < n; i++) {
                    var p0 = c.points[c.primPointIdx[a + i]];
                    var p1 = c.points[c.primPointIdx[a + (i + 1) % n]];
                    normal.x += (p0.y - p1.y) * (p0.z + p1.z);
                    normal.y += (p0.z - p1.z) * (p0.x + p1.x);
                    normal.z += (p0.x - p1.x) * (p0.y + p1.y);
                }
                if (normal.sqrMagnitude > 1e-12f) normal.Normalize();
                c.primC[pi] = cen;
                c.primN[pi] = normal;
                for (var i = 0; i < n; i++) {
                    var p = c.primPointIdx[a + i];
                    c.pointN[p] += normal;
                    pnCount[p]++;
                }
            }
            for (var i = 0; i < c.pointN.Length; i++) if (pnCount[i] > 0) { c.pointN[i] /= pnCount[i]; if (c.pointN[i].sqrMagnitude > 1e-12f) c.pointN[i].Normalize(); }
        }

        static void Draw(Matrix4x4 l2w, Cache c, int mode) {
            Handles.zTest = CompareFunction.LessEqual;
            if (mode == CunningSceneViewModeState.ModePointNumber) {
                for (var i = 0; i < c.points.Length; i++) Handles.Label(l2w.MultiplyPoint3x4(c.points[i]), i.ToString());
                return;
            }

            if (mode == CunningSceneViewModeState.ModePrimNumber) {
                for (var i = 0; i < c.primC.Length; i++) Handles.Label(l2w.MultiplyPoint3x4(c.primC[i]), i.ToString());
                return;
            }

            if (mode == CunningSceneViewModeState.ModePrimNormal) {
                Handles.color = new Color(1f, 0.85f, 0.1f, 1f);
                var n = Math.Min(c.primC.Length, c.primN.Length);
                for (var i = 0; i < n; i++) {
                    var p = l2w.MultiplyPoint3x4(c.primC[i]);
                    var nn = l2w.MultiplyVector(c.primN[i]).normalized;
                    var s = HandleUtility.GetHandleSize(p) * 0.4f;
                    Handles.DrawAAPolyLine(2f, p, p + nn * s);
                }
                return;
            }

            if (mode == CunningSceneViewModeState.ModeNormal) {
                var hasVertex = c.primOffsets != null && c.primOffsets.Length > 1 && c.primN != null && c.primN.Length >= c.primOffsets.Length - 1 && c.primPointIdx != null && c.primPointIdx.Length > 0;
                if (!hasVertex) {
                    if (c.pointN == null || c.pointN.Length != c.points.Length) return;
                    Handles.color = Color.blue;
                    for (var i = 0; i < c.points.Length; i++) {
                        var p = l2w.MultiplyPoint3x4(c.points[i]);
                        var nn = l2w.MultiplyVector(c.pointN[i]).normalized;
                        var s = HandleUtility.GetHandleSize(p) * 0.25f;
                        Handles.DrawAAPolyLine(2f, p, p + nn * s);
                    }
                    return;
                }
                Handles.color = Color.green;
                var primCount = c.primOffsets.Length - 1;
                for (var pi = 0; pi < primCount; pi++) {
                    var a = Mathf.Clamp(c.primOffsets[pi], 0, c.primPointIdx.Length);
                    var b = Mathf.Clamp(c.primOffsets[pi + 1], 0, c.primPointIdx.Length);
                    var nn = l2w.MultiplyVector(c.primN[pi]).normalized;
                    for (var i = a; i < b; i++) {
                        var pidx = c.primPointIdx[i];
                        if ((uint)pidx >= (uint)c.points.Length) continue;
                        var p = l2w.MultiplyPoint3x4(c.points[pidx]);
                        var s = HandleUtility.GetHandleSize(p) * 0.25f;
                        Handles.DrawAAPolyLine(2f, p, p + nn * s);
                    }
                }
            }
        }
    }
}

