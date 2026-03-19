using System;
using System.IO.MemoryMappedFiles;
using UnityEngine;

namespace CunningEngine.Editor {
    static class CunningSyncSharedMemory {
        const uint Version = 1;
        const long SeqOffset = 4;
        const long ByteLengthOffset = 8;
        const long PayloadOffset = 16;

        sealed class Channel : IDisposable {
            public uint lastReadSeq;
            public uint localWriteSeq;
            public MemoryMappedFile mapping;
            public MemoryMappedViewAccessor view;

            public bool TryWrite(byte[] payload) {
                if (view == null || payload == null || payload.Length < PayloadSize) return false;
                var seq = unchecked(localWriteSeq + 1u);
                if (seq == 0u) seq = 1u;
                localWriteSeq = seq;
                view.Write(0, Version);
                view.Write(ByteLengthOffset, (uint)PayloadSize);
                view.Write(12, 0u);
                view.WriteArray(PayloadOffset, payload, 0, PayloadSize);
                view.Write(SeqOffset, seq);
                return true;
            }

            public bool TryRead(byte[] payload) {
                if (view == null || payload == null || payload.Length < PayloadSize) return false;
                var seqBefore = view.ReadUInt32(SeqOffset);
                if (seqBefore == 0u || seqBefore == lastReadSeq) return false;
                var version = view.ReadUInt32(0);
                var byteLength = view.ReadUInt32(ByteLengthOffset);
                if (version != Version || byteLength < PayloadSize) return false;
                view.ReadArray(PayloadOffset, payload, 0, PayloadSize);
                var seqAfter = view.ReadUInt32(SeqOffset);
                if (seqAfter != seqBefore) return false;
                lastReadSeq = seqAfter;
                return true;
            }

            public void Dispose() {
                try { view?.Dispose(); } catch { }
                try { mapping?.Dispose(); } catch { }
                view = null;
                mapping = null;
            }
        }

        public const int PayloadSize = 64;
        public const int PacketSize = 80;

        static Channel _unityToC3D;
        static Channel _c3dToUnity;

        public static bool IsReady {
            get {
                EnsureReadyFromState();
                return _unityToC3D != null && _c3dToUnity != null;
            }
        }

        public static void Open(string unityToC3DMapName, string c3dToUnityMapName) {
            Close();
            _unityToC3D = OpenChannel(unityToC3DMapName);
            _c3dToUnity = OpenChannel(c3dToUnityMapName);
        }

        public static void Close() {
            try { _unityToC3D?.Dispose(); } catch { }
            try { _c3dToUnity?.Dispose(); } catch { }
            _unityToC3D = null;
            _c3dToUnity = null;
        }

        public static bool TryWriteUnityViewport(byte[] payload) {
            EnsureReadyFromState();
            return _unityToC3D != null && _unityToC3D.TryWrite(payload);
        }

        public static bool TryReadC3DViewport(byte[] payload) {
            EnsureReadyFromState();
            return _c3dToUnity != null && _c3dToUnity.TryRead(payload);
        }

        public static void EnsureReadyFromState() {
            if (_unityToC3D != null && _c3dToUnity != null) return;
            if (!CunningSyncState.Enabled) return;
            if (string.IsNullOrEmpty(CunningSyncState.UnityToC3DMapName) || string.IsNullOrEmpty(CunningSyncState.C3DToUnityMapName)) return;

            try {
                Open(CunningSyncState.UnityToC3DMapName, CunningSyncState.C3DToUnityMapName);
            } catch (Exception e) {
                Close();
                Debug.LogWarning($"Cunning Sync: shared memory attach failed: {e.Message}");
            }
        }

        static Channel OpenChannel(string mapName) {
            if (string.IsNullOrWhiteSpace(mapName)) throw new ArgumentException("Shared memory name is empty.", nameof(mapName));
            var mapping = MemoryMappedFile.CreateOrOpen(mapName, PacketSize, MemoryMappedFileAccess.ReadWrite);
            var view = mapping.CreateViewAccessor(0, PacketSize, MemoryMappedFileAccess.ReadWrite);
            return new Channel {
                mapping = mapping,
                view = view
            };
        }
    }
}
