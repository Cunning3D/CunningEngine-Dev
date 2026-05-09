using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CunningEngine {
    [ExecuteAlways]
    public sealed class CunningInputHeightmapTexture : MonoBehaviour, ICunningInputValueHandle {
        const string HeightLayerName = "height";
        const uint UnityBackendIdentity = 1;

        [Header("Source")]
        public Texture heightmapTexture;
        public Vector3 worldSize = new Vector3(1000f, 256f, 1000f);
        public bool detectTextureChanges;
        [Min(0f)] public float textureCheckIntervalSeconds = 0.25f;

        [Header("Debug")]
        public bool detailedTimingLogs = true;
        public float detailedTimingLogMinMs = 1f;

        public ulong CurrentInputDirtyId => dirtyId;

        ulong dirtyId = 1;
        ulong registeredHostField;
        ulong registeredRuntimeHandle;
        CunningRuntimeContext registeredRuntime;
        int lastHash;
        float nextTextureCheckTime;
        bool warnedBackend;

        void OnEnable() { Refresh(false); }
        void OnDisable() { Release(); }
        void Update() { Refresh(false); }

        public ulong ImportCunningValue(CunningRuntimeContext runtime) {
            if (runtime == null || runtime.Handle == 0) return 0;
            if (!runtime.SupportsExternalGpuFieldImport) {
                if (!warnedBackend) {
                    warnedBackend = true;
                    Debug.LogWarning($"CunningInputHeightmapTexture[{name}]: Texture-backed field inputs require EngineHostedBackend.");
                }
                return 0;
            }
            if (!Refresh(false) || !heightmapTexture || heightmapTexture.width < 2 || heightmapTexture.height < 2) return 0;
            if (registeredRuntime != runtime || registeredRuntimeHandle != runtime.Handle) {
                registeredRuntime?.UnregisterExternalGpuField(ref registeredHostField);
                registeredRuntime = runtime;
                registeredRuntimeHandle = runtime.Handle;
            }

            var layerName = Marshal.StringToHGlobalAnsi(HeightLayerName);
            var layers = new[] {
                new NativeMethods.CunningGpuFieldLayerDesc {
                    name = layerName,
                    semantic = (uint)NativeMethods.CunningGpuFieldLayerSemantic.Height,
                    format = (uint)NativeMethods.CunningGpuFieldChannelFormat.R32Float,
                    default_clear_x = 0f,
                    default_clear_y = 0f,
                    default_clear_z = 0f,
                    default_clear_w = 0f,
                }
            };
            var layerHandle = GCHandle.Alloc(layers, GCHandleType.Pinned);
            try {
                var desc = BuildImportDesc(layerHandle.AddrOfPinnedObject(), 1);
                return runtime.ImportExternalGpuField(heightmapTexture, ref registeredHostField, ref desc);
            } finally {
                layerHandle.Free();
                Marshal.FreeHGlobal(layerName);
            }
        }

        bool Refresh(bool force) {
            if (!heightmapTexture || heightmapTexture.width < 2 || heightmapTexture.height < 2) return false;
            int hash = ComputeStaticHash();
            bool periodicDirty = !force && hash == lastHash && ShouldCheckTexture();
            if (!force && hash == lastHash && !periodicDirty) return true;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            lastHash = hash;
            dirtyId++;
            sw.Stop();
            if (ShouldLogTiming(sw.Elapsed.TotalMilliseconds)) {
                Debug.Log($"CunningInputHeightmapTexture[{name}]: bound texture-backed GpuField {heightmapTexture.width}x{heightmapTexture.height}, ms={sw.Elapsed.TotalMilliseconds:F3}");
            }
            return true;
        }

        void Release() {
            registeredRuntime?.UnregisterExternalGpuField(ref registeredHostField);
            registeredRuntime = null;
            registeredRuntimeHandle = 0;
            lastHash = 0;
            dirtyId++;
        }

        int ComputeStaticHash() {
            unchecked {
                int hash = 17;
                hash = hash * 31 + heightmapTexture.GetInstanceID();
                hash = hash * 31 + heightmapTexture.width;
                hash = hash * 31 + heightmapTexture.height;
                hash = hash * 31 + worldSize.GetHashCode();
                hash = hash * 31 + transform.localToWorldMatrix.GetHashCode();
                return hash;
            }
        }

        bool ShouldLogTiming(double totalMs) => detailedTimingLogs && totalMs >= detailedTimingLogMinMs;

        bool ShouldCheckTexture() {
            if (!detectTextureChanges) return false;
            if (textureCheckIntervalSeconds <= 0f) return true;
            float now = Time.realtimeSinceStartup;
            if (now < nextTextureCheckTime) return false;
            nextTextureCheckTime = now + textureCheckIntervalSeconds;
            return true;
        }

        NativeMethods.CunningHostGpuImportFieldDesc BuildImportDesc(IntPtr layers, uint layerCount) {
            int width = heightmapTexture.width;
            int height = heightmapTexture.height;
            float invX = width > 1 ? 1f / (width - 1) : 1f;
            float invY = height > 1 ? 1f / (height - 1) : 1f;
            var basisX = CunningUnityCoordinates.ToCunningDirection(transform.TransformVector(new Vector3(worldSize.x * invX, 0f, 0f)));
            var basisY = CunningUnityCoordinates.ToCunningDirection(transform.TransformVector(new Vector3(0f, 0f, worldSize.z * invY)));
            var basisZ = CunningUnityCoordinates.ToCunningDirection(transform.TransformVector(new Vector3(0f, worldSize.y, 0f)));
            var sampleOrigin = CunningUnityCoordinates.ToCunningPosition(transform.TransformPoint(Vector3.zero));
            var origin = sampleOrigin - (basisX + basisY) * 0.5f;

            return new NativeMethods.CunningHostGpuImportFieldDesc {
                backend_identity = UnityBackendIdentity,
                queue_token = 0,
                dim_x = (uint)width,
                dim_y = (uint)height,
                dim_z = 1,
                channels = 1,
                bytes_per_channel = sizeof(float),
                dimension = (uint)NativeMethods.CunningGpuFieldDimension.D2,
                tile_x = 16,
                tile_y = 16,
                tile_z = 1,
                channel_format = (uint)NativeMethods.CunningGpuFieldChannelFormat.R32Float,
                storage_mode = (uint)NativeMethods.CunningGpuFieldStorageMode.Dense,
                world_transform = new NativeMethods.CunningGpuFieldWorldTransform {
                    origin_x = origin.x,
                    origin_y = origin.y,
                    origin_z = origin.z,
                    basis_x_x = basisX.x,
                    basis_x_y = basisX.y,
                    basis_x_z = basisX.z,
                    basis_y_x = basisY.x,
                    basis_y_y = basisY.y,
                    basis_y_z = basisY.z,
                    basis_z_x = basisZ.x,
                    basis_z_y = basisZ.y,
                    basis_z_z = basisZ.z,
                },
                layers = layers,
                layer_count = layerCount,
                ownership = (uint)NativeMethods.CunningHostGpuOwnership.ImportShared,
                state = (uint)NativeMethods.CunningHostGpuResourceState.ReadWrite,
                wait_sync_points = IntPtr.Zero,
                wait_sync_count = 0,
            };
        }
    }

    sealed class HeightmapTextureInputResolver : ICunningInputResolver {
        public int Priority => 5;
        public bool CanResolve(UnityEngine.Object obj) {
            var go = obj as GameObject ?? (obj as Component)?.gameObject;
            var input = go ? go.GetComponent<CunningInputHeightmapTexture>() : null;
            return input && input.heightmapTexture;
        }
        public MonoBehaviour Resolve(UnityEngine.Object obj) {
            var go = obj as GameObject ?? (obj as Component)?.gameObject;
            return go ? go.GetComponent<CunningInputHeightmapTexture>() : null;
        }
    }
}
