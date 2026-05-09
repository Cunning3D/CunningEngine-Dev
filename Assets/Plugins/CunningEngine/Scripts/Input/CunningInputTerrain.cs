using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CunningEngine {
    [ExecuteAlways, RequireComponent(typeof(Terrain))]
    public sealed class CunningInputTerrain : MonoBehaviour, ICunningInputValueHandle {
        const string HeightLayerName = "height";
        const uint UnityBackendIdentity = 1;

        [Header("Sampling")]
        [Tooltip("0 keeps the source heightmap resolution. A cap is an explicit lossy import.")]
        [Min(0)] public int maxSampleResolution = 0;
        [Tooltip("Use TerrainData.heightmapTexture as the source and let the host backend materialize it on GPU.")]
        public bool useHeightmapTexture = true;
        [Tooltip("Only enable when texture-backed import is unavailable and a CPU GetHeights fallback is acceptable.")]
        public bool allowCpuHeightmapFallback;
        public bool detectHeightChanges;
        [Min(0f)] public float heightCheckIntervalSeconds = 0.25f;

        [Header("Debug")]
        public bool detailedTimingLogs = true;
        public float detailedTimingLogMinMs = 1f;

        public ulong CurrentInputDirtyId => dirtyId;

        ComputeBuffer heightBuffer;
        Texture heightTexture;
        int bufferResolution;
        int fieldWidth;
        int fieldHeight;
        int lastHash;
        int lastStaticHash;
        float nextHeightCheckTime;
        ulong dirtyId = 1;
        ulong registeredHostField;
        ulong registeredRuntimeHandle;
        CunningRuntimeContext registeredRuntime;
        bool registeredTextureSource;
        bool usingTextureSource;
        bool warnedBackend;
        bool warnedTextureFallback;

        void OnEnable() { TryUpload(true); }
        void OnDisable() { ReleaseBuffer(); }
        void Update() { TryUpload(false); }

        public static CunningInputTerrain GetOrAdd(GameObject go) => go ? go.GetComponent<CunningInputTerrain>() ?? go.AddComponent<CunningInputTerrain>() : null;

        public ulong ImportCunningValue(CunningRuntimeContext runtime) {
            if (runtime == null || runtime.Handle == 0) return 0;
            if (!runtime.SupportsExternalGpuFieldImport) {
                if (!warnedBackend) {
                    warnedBackend = true;
                    Debug.LogWarning($"CunningInputTerrain[{name}]: Terrain imports are GpuField inputs and require EngineHostedBackend.");
                }
                return 0;
            }
            if (!TryUpload(true) || fieldWidth < 2 || fieldHeight < 2) return 0;
            if (registeredRuntime != runtime || registeredRuntimeHandle != runtime.Handle) {
                registeredRuntime?.UnregisterExternalGpuField(ref registeredHostField);
                registeredRuntime = runtime;
                registeredRuntimeHandle = runtime.Handle;
            }
            if (registeredHostField != 0 && registeredTextureSource != usingTextureSource) {
                registeredRuntime?.UnregisterExternalGpuField(ref registeredHostField);
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
                ulong imported = usingTextureSource
                    ? runtime.ImportExternalGpuField(heightTexture, ref registeredHostField, ref desc)
                    : runtime.ImportExternalGpuField(heightBuffer, ref registeredHostField, ref desc);
                if (imported != 0) registeredTextureSource = usingTextureSource;
                return imported;
            } finally {
                layerHandle.Free();
                Marshal.FreeHGlobal(layerName);
            }
        }

        bool TryUpload(bool force) {
            var terrain = GetComponent<Terrain>();
            var data = terrain ? terrain.terrainData : null;
            if (!terrain || !data) {
                ReleaseBuffer();
                return false;
            }

            if (useHeightmapTexture && TryBindHeightmapTexture(terrain, data, force)) return true;
            if (useHeightmapTexture && !allowCpuHeightmapFallback) {
                if (!warnedTextureFallback) {
                    warnedTextureFallback = true;
                    Debug.LogWarning($"CunningInputTerrain[{name}]: heightmapTexture import unavailable; CPU fallback is disabled.");
                }
                ReleaseBuffer();
                return false;
            }
            return TryUploadCpuHeightmap(terrain, data, force);
        }

        bool TryBindHeightmapTexture(Terrain terrain, TerrainData data, bool force) {
            var texture = data.heightmapTexture;
            if (!texture || texture.width < 2 || texture.height < 2) return false;
            if (maxSampleResolution > 0 && (texture.width > maxSampleResolution || texture.height > maxSampleResolution) && !warnedTextureFallback) {
                warnedTextureFallback = true;
                Debug.LogWarning($"CunningInputTerrain[{name}]: maxSampleResolution is ignored for texture-backed heightmapTexture import; disable useHeightmapTexture for explicit resampling.");
            }
            int staticHash = ComputeStaticHash(terrain, data, texture.width, texture.height, texture.GetInstanceID(), true);
            bool staticChanged = staticHash != lastStaticHash || !usingTextureSource || heightTexture != texture || fieldWidth != texture.width || fieldHeight != texture.height || heightBuffer != null;
            bool periodicDirty = !force && !staticChanged && ShouldCheckHeights();
            if (!staticChanged && !periodicDirty) return true;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            ReleaseCpuBuffer();
            heightTexture = texture;
            fieldWidth = texture.width;
            fieldHeight = texture.height;
            bufferResolution = 0;
            lastStaticHash = staticHash;
            lastHash = 0;
            usingTextureSource = true;
            dirtyId++;
            sw.Stop();
            if (ShouldLogTiming(sw.Elapsed.TotalMilliseconds)) {
                Debug.Log($"CunningInputTerrain[{name}]: bound texture-backed GpuField {fieldWidth}x{fieldHeight}, ms={sw.Elapsed.TotalMilliseconds:F3}");
            }
            return true;
        }

        bool TryUploadCpuHeightmap(Terrain terrain, TerrainData data, bool force) {
            int resolution = ComputeSampleResolution(data.heightmapResolution);
            if (resolution < 2) {
                ReleaseBuffer();
                return false;
            }

            int staticHash = ComputeStaticHash(terrain, data, resolution, resolution, 0, false);
            bool staticChanged = staticHash != lastStaticHash;
            if (!force && !staticChanged && heightBuffer != null && !ShouldCheckHeights()) return true;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var values = BuildHeightValues(data, resolution, staticHash, out var hash);
            if (!force && !staticChanged && hash == lastHash && heightBuffer != null) return true;
            if (hash == lastHash && heightBuffer != null && bufferResolution == resolution) return true;

            EnsureBuffer(resolution, values.Length);
            heightBuffer.SetData(values);
            bufferResolution = resolution;
            fieldWidth = resolution;
            fieldHeight = resolution;
            lastStaticHash = staticHash;
            lastHash = hash;
            heightTexture = null;
            usingTextureSource = false;
            dirtyId++;
            sw.Stop();
            if (ShouldLogTiming(sw.Elapsed.TotalMilliseconds)) {
                Debug.Log($"CunningInputTerrain[{name}]: uploaded CPU fallback GpuField res={resolution}, floats={values.Length}, ms={sw.Elapsed.TotalMilliseconds:F3}");
            }
            return true;
        }

        void EnsureBuffer(int resolution, int count) {
            if (heightBuffer != null && heightBuffer.count == count) return;
            ReleaseBuffer();
            heightBuffer = new ComputeBuffer(count, sizeof(float), ComputeBufferType.Structured);
            bufferResolution = resolution;
        }

        void ReleaseBuffer() {
            registeredRuntime?.UnregisterExternalGpuField(ref registeredHostField);
            registeredRuntime = null;
            ReleaseCpuBuffer();
            heightTexture = null;
            fieldWidth = 0;
            fieldHeight = 0;
            registeredRuntimeHandle = 0;
            registeredTextureSource = false;
            usingTextureSource = false;
            lastHash = 0;
            lastStaticHash = 0;
            dirtyId++;
        }

        void ReleaseCpuBuffer() {
            if (heightBuffer != null) {
                heightBuffer.Dispose();
                heightBuffer = null;
            }
            bufferResolution = 0;
        }

        bool ShouldCheckHeights() {
            if (!detectHeightChanges) return false;
            if (heightCheckIntervalSeconds <= 0f) return true;
            float now = Time.realtimeSinceStartup;
            if (now < nextHeightCheckTime) return false;
            nextHeightCheckTime = now + heightCheckIntervalSeconds;
            return true;
        }

        bool ShouldLogTiming(double totalMs) => detailedTimingLogs && totalMs >= detailedTimingLogMinMs;

        int ComputeSampleResolution(int sourceResolution) {
            if (sourceResolution < 2) return 0;
            if (maxSampleResolution <= 0) return sourceResolution;
            return Mathf.Clamp(maxSampleResolution, 2, sourceResolution);
        }

        int ComputeStaticHash(Terrain terrain, TerrainData data, int width, int height, int sourceId, bool textureBacked) {
            unchecked {
                int hash = 17;
                hash = hash * 31 + data.GetInstanceID();
                hash = hash * 31 + data.heightmapResolution;
                hash = hash * 31 + width;
                hash = hash * 31 + height;
                hash = hash * 31 + sourceId;
                hash = hash * 31 + (textureBacked ? 1 : 0);
                hash = hash * 31 + data.size.GetHashCode();
                hash = hash * 31 + terrain.transform.localToWorldMatrix.GetHashCode();
                hash = hash * 31 + maxSampleResolution;
                hash = hash * 31 + (useHeightmapTexture ? 1 : 0);
                return hash;
            }
        }

        float[] BuildHeightValues(TerrainData data, int resolution, int staticHash, out int hash) {
            var values = new float[resolution * resolution];
            hash = staticHash;
            unchecked {
                if (resolution == data.heightmapResolution) {
                    var heights = data.GetHeights(0, 0, resolution, resolution);
                    for (int z = 0; z < resolution; z++) {
                        for (int x = 0; x < resolution; x++) {
                            float h = heights[z, x];
                            values[z * resolution + x] = h;
                            hash = hash * 31 + h.GetHashCode();
                        }
                    }
                    return values;
                }

                float invHeight = Mathf.Abs(data.size.y) > 1e-6f ? 1f / data.size.y : 0f;
                for (int z = 0; z < resolution; z++) {
                    float v = (float)z / (resolution - 1);
                    for (int x = 0; x < resolution; x++) {
                        float u = (float)x / (resolution - 1);
                        float h = Mathf.Clamp01(data.GetInterpolatedHeight(u, v) * invHeight);
                        values[z * resolution + x] = h;
                        hash = hash * 31 + h.GetHashCode();
                    }
                }
                return values;
            }
        }

        NativeMethods.CunningHostGpuImportFieldDesc BuildImportDesc(IntPtr layers, uint layerCount) {
            var terrain = GetComponent<Terrain>();
            var data = terrain.terrainData;
            var size = data.size;
            float invX = fieldWidth > 1 ? 1f / (fieldWidth - 1) : 1f;
            float invY = fieldHeight > 1 ? 1f / (fieldHeight - 1) : 1f;
            var basisX = CunningUnityCoordinates.ToCunningDirection(terrain.transform.TransformVector(new Vector3(size.x * invX, 0f, 0f)));
            var basisY = CunningUnityCoordinates.ToCunningDirection(terrain.transform.TransformVector(new Vector3(0f, 0f, size.z * invY)));
            var basisZ = CunningUnityCoordinates.ToCunningDirection(terrain.transform.TransformVector(new Vector3(0f, size.y, 0f)));
            var sampleOrigin = CunningUnityCoordinates.ToCunningPosition(terrain.transform.TransformPoint(Vector3.zero));
            var origin = sampleOrigin - (basisX + basisY) * 0.5f;

            return new NativeMethods.CunningHostGpuImportFieldDesc {
                backend_identity = UnityBackendIdentity,
                queue_token = 0,
                dim_x = (uint)fieldWidth,
                dim_y = (uint)fieldHeight,
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

    sealed class TerrainInputResolver : ICunningInputResolver {
        public int Priority => 6;

        public bool CanResolve(UnityEngine.Object obj) {
            var go = obj as GameObject ?? (obj as Component)?.gameObject;
            if (!go) return false;
            var terrain = go.GetComponent<Terrain>();
            return terrain && terrain.terrainData;
        }

        public MonoBehaviour Resolve(UnityEngine.Object obj) {
            var go = obj as GameObject ?? (obj as Component)?.gameObject;
            return go ? CunningInputTerrain.GetOrAdd(go) : null;
        }
    }
}
