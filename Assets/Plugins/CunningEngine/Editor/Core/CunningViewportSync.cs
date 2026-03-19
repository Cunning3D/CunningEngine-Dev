using System;
using UnityEditor;
using UnityEngine;

namespace CunningEngine.Editor {
    [InitializeOnLoad]
    static class CunningViewportSync {
        static ulong _lastBlob;
        static double _lastPush;
        static bool _subscribed;

        static CunningViewportSync() { EditorApplication.update += Tick; SceneView.duringSceneGui += OnSceneGUI; _subscribed = true; }

        static void Tick() {
            if (!CunningSyncState.Enabled || !CunningSyncState.SyncCamera) return;
            if (!CunningSyncSharedMemory.IsReady) return;
            var sv = SceneView.lastActiveSceneView; if (sv == null || sv.camera == null) return;
            var now = EditorApplication.timeSinceStartup;
            if (now - _lastPush < 0.05) return;
            _lastPush = now;
            PushUnityViewport(sv.camera);
        }

        static void OnSceneGUI(SceneView sv) {
            if (!_subscribed || sv == null || sv.camera == null) return;
            if (!CunningSyncState.Enabled || !CunningSyncState.SyncCamera) return;
            if (!CunningSyncSharedMemory.IsReady) return;
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (IsUserInteracting()) return;
            PullCunningViewport(sv);
        }

        static bool IsUserInteracting() {
            // Best-effort: avoid fighting the user while orbiting/panning/zooming.
            try {
                var e = Event.current;
                if (e == null) return false;
                if (e.alt) return true;
                if (e.type == EventType.ScrollWheel) return true;
                if (e.type == EventType.MouseDrag || e.type == EventType.MouseDown || e.type == EventType.MouseUp) return true;
                return false;
            } catch { return false; }
        }

        static void PushUnityViewport(Camera cam) {
            // Binary payload (64 bytes): u32 ver=1, u32 basis=1(unity), u32 flags, u32 pad, f32 fov, f32 orthoSize, f32 near, f32 far, vec3 pos, quat rot, pad(4)
            var b = new byte[64];
            WU(b, 0, 1); WU(b, 4, 1);
            WU(b, 8, cam.orthographic ? 1u : 0u);
            WF(b, 16, cam.fieldOfView);
            WF(b, 20, cam.orthographicSize);
            WF(b, 24, cam.nearClipPlane);
            WF(b, 28, cam.farClipPlane);
            var p = cam.transform.position; var q = cam.transform.rotation;
            WF(b, 32, p.x); WF(b, 36, p.y); WF(b, 40, p.z);
            WF(b, 44, q.x); WF(b, 48, q.y); WF(b, 52, q.z); WF(b, 56, q.w);
            var h = Hash64(b); if (h == _lastBlob) return;
            _lastBlob = h;
            CunningSyncSharedMemory.TryWriteUnityViewport(b);
        }

        static void PullCunningViewport(SceneView sv) {
            var b = new byte[CunningSyncSharedMemory.PayloadSize];
            if (!CunningSyncSharedMemory.TryReadC3DViewport(b)) return;

            var ver = RU(b, 0); if (ver != 1) return;
            var basis = RU(b, 4); // 0=internal
            var flags = RU(b, 8);
            var fov = RF(b, 16);
            var orthoSize = RF(b, 20);
            var near = RF(b, 24);
            var far = RF(b, 28);
            var pos = new Vector3(RF(b, 32), RF(b, 36), RF(b, 40));
            var rot = new Quaternion(RF(b, 44), RF(b, 48), RF(b, 52), RF(b, 56));

            // Internal(Bevy RH) -> Unity(LH): mirror Z on vectors and do M*R*M via forward/up.
            if (basis == 0) {
                pos.z = -pos.z;
                var f = rot * Vector3.forward; f.z = -f.z;
                var u = rot * Vector3.up; u.z = -u.z;
                if (f.sqrMagnitude > 1e-12f && u.sqrMagnitude > 1e-12f) rot = Quaternion.LookRotation(f, u);
            }

            var cam = sv.camera;
            if (cam != null) {
                cam.nearClipPlane = near;
                cam.farClipPlane = far;
                cam.orthographic = (flags & 1u) != 0u;
                if (cam.orthographic) cam.orthographicSize = Mathf.Max(1e-4f, orthoSize);
                else cam.fieldOfView = Mathf.Clamp(fov, 1f, 179f);
            }

            // Keep orbit pivot stable-ish by projecting current pivot onto new forward ray.
            var forward = rot * Vector3.forward;
            var size = Mathf.Clamp(Vector3.Dot(sv.pivot - pos, forward), 0.01f, 100000f);
            var pivot = pos + forward * size;
            sv.pivot = pivot;
            sv.rotation = rot;
            sv.size = size;
            sv.orthographic = (flags & 1u) != 0u;
            sv.Repaint();
        }

        static ulong Hash64(byte[] b) { unchecked { ulong h = 14695981039346656037UL; for (int i = 0; i < b.Length; i++) { h ^= b[i]; h *= 1099511628211UL; } return h; } }
        static void WU(byte[] b, int o, uint v) { var t = BitConverter.GetBytes(v); Buffer.BlockCopy(t, 0, b, o, 4); }
        static void WF(byte[] b, int o, float v) { var t = BitConverter.GetBytes(v); Buffer.BlockCopy(t, 0, b, o, 4); }
        static uint RU(byte[] b, int o) { return BitConverter.ToUInt32(b, o); }
        static float RF(byte[] b, int o) { return BitConverter.ToSingle(b, o); }
    }
}

