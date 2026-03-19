using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Splines;

namespace CunningEngine {
    // Component: uploads SplineContainer as geometry handle
    [ExecuteAlways, RequireComponent(typeof(SplineContainer))]
    public sealed class CunningInputSpline : MonoBehaviour, ICunningInputHandle {
        public enum SourceBasis : uint { InternalBevy = 0, Unity = 1 }
        public SourceBasis sourceBasis = SourceBasis.Unity;
        public ulong currentHandle;
        public ulong CurrentHandle => this ? currentHandle : 0;
        int _lastHash;

        void OnEnable() { TryUpload(); }
        void OnDisable() { Release(); }
        void Update() { TryUpload(); }

        public static CunningInputSpline GetOrAdd(GameObject go) => go ? go.GetComponent<CunningInputSpline>() ?? go.AddComponent<CunningInputSpline>() : null;
        void Release() { if (currentHandle != 0) { NativeMethods.cunning_release_handle(currentHandle); currentHandle = 0; } }

        void TryUpload() {
            var c = GetComponent<SplineContainer>(); if (!c) return;
            int h = unchecked(c.Splines.Count * 486187739 ^ c.KnotLinkCollection.Count * 16777619 ^ transform.localToWorldMatrix.GetHashCode());
            if (h == _lastHash && currentHandle != 0) return;
            _lastHash = h;

            byte[] fbs;
            try { fbs = CunningSplineSnapshotFbs.BuildFbs(c); } catch (Exception e) { Debug.LogWarning($"CunningInputSpline: BuildFbs failed: {e.Message}"); return; }
            if (fbs == null || fbs.Length == 0) return;

            var gch = GCHandle.Alloc(fbs, GCHandleType.Pinned);
            try {
                var nh = NativeMethods.cunning_spline_snapshot_fbs_to_geo(gch.AddrOfPinnedObject(), (uint)fbs.Length, (uint)sourceBasis);
                if (nh == 0) { Debug.LogWarning("CunningInputSpline: native upload returned 0"); return; }
                Release();
                currentHandle = nh;
            } finally { gch.Free(); }
        }
    }

    // Resolver: auto-detects SplineContainer and attaches CunningInputSpline
    sealed class SplineInputResolver : ICunningInputResolver {
        public int Priority => 0;
        public bool CanResolve(UnityEngine.Object obj) { var go = obj as GameObject ?? (obj as Component)?.gameObject; return go && go.GetComponent<SplineContainer>(); }
        public MonoBehaviour Resolve(UnityEngine.Object obj) { var go = obj as GameObject ?? (obj as Component)?.gameObject; return go ? CunningInputSpline.GetOrAdd(go) : null; }
    }
}
