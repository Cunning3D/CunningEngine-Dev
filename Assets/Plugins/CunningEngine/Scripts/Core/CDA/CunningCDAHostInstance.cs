using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using CunningEngine.CDA;

namespace CunningEngine {
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class CunningCDAHostInstance : MonoBehaviour {
        public enum CunningCdaGpuBackendMode {
            StandaloneWgpuBackend = 0,
            EngineHostedBackend = 1,
        }

        public CDAAssetObject asset;
        public List<UnityEngine.Object> inputs = new();
        public bool autoCook = true;
        public bool cookInEditMode = true;
        public bool cookInPlayMode = true;
        public bool enableInstancing = true;
        public CunningCdaGpuBackendMode gpuBackendMode = CunningCdaGpuBackendMode.EngineHostedBackend;
        public Vector3 defaultTerrainSize = new Vector3(1000f, 256f, 1000f);
        public ulong cpuDeadlineNs = 4_000_000;
        public ulong gpuDeadlineNs = 4_000_000;
        public ulong gpuBudgetBytes = 256UL * 1024UL * 1024UL;
        public bool validateGpuBackendParity;
        public float gpuParityEpsilon = 0.02f;

        [Header("Debug")]
        public ulong instanceId;
        public ulong cdaId;
        public ulong generation;
        public ulong jobId;
        public string lastStatus;
        public string outputStatus;
        public string gpuParityStatus;

        [NonSerialized] public readonly Dictionary<string, CdaParamValue> paramValues = new();
        readonly Dictionary<uint, OutputSlot> slots = new();
        readonly List<ulong> inputValues = new();
        readonly List<PendingOutput> pendingApplyValues = new();
        readonly List<ulong> lastInputDirty = new();
        CunningRuntimeContext runtime;
        CunningRuntimeContext parityRuntime;
        NativeMethods.CunningRuntimeBackendMode runtimeBackendMode;
        uint assetSig;
        uint loadedAssetSig;
        bool cookQueued;
        bool tickingRuntime;
        bool gpuParityQueued;
        uint gpuParityOutputIndex;
        ulong gpuParityJobId;
        ulong gpuParityGeneration;
#if UNITY_EDITOR
        bool editorUpdateRegistered;
#endif

        public sealed class OutputSlot {
            public uint index;
            public string name;
            public NativeMethods.CunningValueKind kind;
            public GameObject child;
            public ulong value;
            public ulong geometryHandle;
        }

        readonly struct PendingOutput {
            public readonly uint index;
            public readonly ulong value;

            public PendingOutput(uint index, ulong value) {
                this.index = index;
                this.value = value;
            }
        }

        readonly struct TerrainHeightMapping {
            public readonly bool fieldXUsesTerrainX;
            public readonly bool fieldYUsesTerrainX;
            public readonly bool flipFieldX;
            public readonly bool flipFieldY;
            public readonly float originX;
            public readonly float originY;
            public readonly float originZ;
            public readonly float sizeX;
            public readonly float heightScaleY;
            public readonly float sizeZ;

            public TerrainHeightMapping(bool fieldXUsesTerrainX, bool fieldYUsesTerrainX, bool flipFieldX, bool flipFieldY, float originX, float originY, float originZ, float sizeX, float heightScaleY, float sizeZ) {
                this.fieldXUsesTerrainX = fieldXUsesTerrainX;
                this.fieldYUsesTerrainX = fieldYUsesTerrainX;
                this.flipFieldX = flipFieldX;
                this.flipFieldY = flipFieldY;
                this.originX = originX;
                this.originY = originY;
                this.originZ = originZ;
                this.sizeX = sizeX;
                this.heightScaleY = heightScaleY;
                this.sizeZ = sizeZ;
            }

            public float FieldX(float terrainX, float terrainZ, int srcWidth) {
                var t = fieldXUsesTerrainX ? terrainX : terrainZ;
                if (flipFieldX) t = 1f - t;
                return Mathf.Clamp01(t) * Mathf.Max(0, srcWidth - 1);
            }

            public float FieldY(float terrainX, float terrainZ, int srcHeight) {
                var t = fieldYUsesTerrainX ? terrainX : terrainZ;
                if (flipFieldY) t = 1f - t;
                return Mathf.Clamp01(t) * Mathf.Max(0, srcHeight - 1);
            }
        }

        void Reset() {
            if (instanceId == 0) instanceId = ((ulong)UnityEngine.Random.Range(int.MinValue, int.MaxValue) << 32) ^ (ulong)DateTime.UtcNow.Ticks;
        }

        void OnEnable() {
#if UNITY_EDITOR
            RegisterEditorUpdate();
#endif
            if (autoCook) cookQueued = true;
        }

        void OnDisable() {
#if UNITY_EDITOR
            UnregisterEditorUpdate();
#endif
            Shutdown();
        }

        void OnDestroy() {
#if UNITY_EDITOR
            UnregisterEditorUpdate();
#endif
            Shutdown();
        }

        void Update() { TickRuntime(); }

#if UNITY_EDITOR
        void RegisterEditorUpdate() {
            if (editorUpdateRegistered) return;
            UnityEditor.EditorApplication.update += EditorUpdate;
            editorUpdateRegistered = true;
        }

        void UnregisterEditorUpdate() {
            if (!editorUpdateRegistered) return;
            UnityEditor.EditorApplication.update -= EditorUpdate;
            editorUpdateRegistered = false;
        }

        void EditorUpdate() {
            if (Application.isPlaying) return;
            TickRuntime();
        }
#endif

        void TickRuntime() {
            if (tickingRuntime) return;
            if (!ShouldRunHere()) return;
            tickingRuntime = true;
            try {
                EnsureRuntime();
                runtime.PumpHostGpu();
                runtime.BeginFrame(Application.isPlaying, cpuDeadlineNs, gpuDeadlineNs, gpuBudgetBytes);
                runtime.PumpHostGpu();
                SyncAsset();
                SyncAssetDefaults();
                if (autoCook && jobId == 0 && InputsChanged()) Cook();
                if (autoCook && jobId == 0 && cookQueued) Cook();
                runtime.PumpHostGpu();
                PollJob();
                runtime.PumpHostGpu();
            } finally {
                runtime?.EndFrame();
                tickingRuntime = false;
            }
            ApplyPendingOutputs();
            runtime?.PumpHostGpu();
            TickGpuBackendParity();
        }

        public void Cook() {
            if (!ShouldRunHere()) return;
            EnsureRuntime();
            SyncAsset();
            if (cdaId == 0) return;
            CancelJob();
            ReleaseInputValues();
            if (!ImportInputValues()) {
                cookQueued = false;
                return;
            }
            generation++;
            var json = BuildParamsJson();
            jobId = NativeMethods.cunning_cda_submit_values(runtime.Handle, cdaId, instanceId, generation, json, inputValues.ToArray(), (uint)inputValues.Count);
            cookQueued = false;
            lastStatus = jobId == 0 ? "submit failed: " + runtime.LastError() : "submitted " + jobId;
        }

        public void QueueCook() { cookQueued = true; }

        public void CancelCook() { CancelJob(); }

        public void SetGpuBackendMode(CunningCdaGpuBackendMode mode) {
            if (gpuBackendMode == mode) return;
            gpuBackendMode = mode;
            ReleaseRuntimeOwnedState();
            cookQueued = true;
            lastStatus = "gpu backend switched to " + GpuBackendDisplayName;
        }

        public string GpuBackendDisplayName => gpuBackendMode == CunningCdaGpuBackendMode.StandaloneWgpuBackend ? "StandaloneWgpuBackend" : "EngineHostedBackend";

        bool ShouldRunHere() => Application.isPlaying ? cookInPlayMode : cookInEditMode;

        void EnsureRuntime() {
            var desired = NativeRuntimeBackendMode();
            if (runtime != null && runtime.Handle != 0 && runtimeBackendMode == desired) return;
            if (runtime != null || runtimeBackendMode != 0) ReleaseRuntimeOwnedState();
            runtime = new CunningRuntimeContext(desired);
            runtimeBackendMode = desired;
        }

        NativeMethods.CunningRuntimeBackendMode NativeRuntimeBackendMode() {
            return gpuBackendMode == CunningCdaGpuBackendMode.StandaloneWgpuBackend
                ? NativeMethods.CunningRuntimeBackendMode.StandaloneOwnedDevice
                : NativeMethods.CunningRuntimeBackendMode.EngineHostedAdapter;
        }

        void SyncAsset() {
            if (asset == null || string.IsNullOrEmpty(asset.sourcePath)) {
                ClearLoadedAssetState();
                lastStatus = "missing CDA asset";
                return;
            }
            var sig = AssetSignature();
            if (sig != loadedAssetSig) {
                ClearLoadedAssetState();
            }
            if (cdaId != 0) return;
            var path = GetAssetAbsPath();
            cdaId = NativeMethods.cunning_cda_load(path);
            if (cdaId == 0) {
                var sb = new StringBuilder(2048);
                NativeMethods.cunning_cda_get_last_error(sb, (uint)sb.Capacity);
                lastStatus = "cda load failed: " + sb;
            } else {
                loadedAssetSig = sig;
            }
        }

        void ClearLoadedAssetState() {
            CancelJob();
            cdaId = 0;
            loadedAssetSig = 0;
            ReleaseInputValues();
            ReleasePendingApplyValues();
            foreach (var kv in slots) DestroySlot(kv.Value);
            slots.Clear();
        }

        string GetAssetAbsPath() {
            var p = asset.sourcePath.Replace('\\', '/');
            if (p.StartsWith("Assets/", StringComparison.Ordinal) || p == "Assets") {
                return System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", p));
            }
            return System.IO.Path.GetFullPath(p);
        }

        void SyncAssetDefaults() {
            if (asset == null) return;
            var sig = AssetSignature();
            if (sig == assetSig) return;
            assetSig = sig;
            paramValues.Clear();
            if (asset.promoted_params != null) {
                foreach (var p in asset.promoted_params) {
                    if (p == null || string.IsNullOrEmpty(p.name)) continue;
                    paramValues[p.name] = CdaParamValue.FromDefaultJson(p.default_value_json);
                }
            }
            EnsureInputSlotCount();
            cookQueued = true;
        }

        void EnsureInputSlotCount() {
            int inputCount = asset != null && asset.inputs != null ? asset.inputs.Count : 0;
            while (inputs.Count < inputCount) inputs.Add(null);
            if (inputs.Count > inputCount) inputs.RemoveRange(inputCount, inputs.Count - inputCount);
            while (lastInputDirty.Count < inputs.Count) lastInputDirty.Add(0);
            if (lastInputDirty.Count > inputs.Count) lastInputDirty.RemoveRange(inputs.Count, lastInputDirty.Count - inputs.Count);
        }

        uint AssetSignature() {
            if (asset == null) return 0;
            uint jsonHash = asset.sourceJsonHash;
            if (jsonHash == 0 && !string.IsNullOrEmpty(asset.sourceJson)) {
                jsonHash = Hash32(asset.sourceJson);
                asset.sourceJsonHash = jsonHash;
            }
            return Hash32(asset.sourcePath) ^ jsonHash;
        }

        string BuildParamsJson() {
            if (paramValues.Count == 0) return "{}";
            var sb = new StringBuilder(256);
            sb.Append('{');
            var first = true;
            foreach (var kv in paramValues) {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(CdaMiniJson.EscapeString(kv.Key)).Append("\":");
                kv.Value.WriteSerdeJson(sb);
            }
            sb.Append('}');
            return sb.ToString();
        }

        void SyncInputDirtyCache() {
            EnsureInputSlotCount();
            lastInputDirty.Clear();
            for (int i = 0; i < inputs.Count; i++) lastInputDirty.Add(0);
        }

        bool InputsChanged() {
            EnsureInputSlotCount();
            var changed = false;
            for (int i = 0; i < inputs.Count; i++) {
                var d = GetInputDirtySafe(inputs[i]);
                if (d != lastInputDirty[i]) {
                    lastInputDirty[i] = d;
                    changed = true;
                }
            }
            return changed;
        }

        static MonoBehaviour ResolveInputBehaviour(UnityEngine.Object obj) {
            if (obj == null) return null;
            return CunningInputUtility.Resolve(obj);
        }

        static ulong GetInputDirtySafe(UnityEngine.Object obj) {
            var mb = ResolveInputBehaviour(obj);
            if (mb == null) return 0;
            try {
                var valueProvider = ResolveInputValueProvider(mb);
                if (valueProvider != null) return valueProvider.CurrentInputDirtyId;
                var handle = GetInputHandleSafe(mb);
                return handle != 0 ? NativeMethods.cunning_geo_get_dirty_id(handle) : 0;
            } catch { return 0; }
        }

        static ulong GetInputHandleSafe(MonoBehaviour mb) {
            if (mb == null) return 0;
            try {
                if (mb is ICunningInputHandle direct) return direct.CurrentHandle;
                var c = mb.GetComponent<ICunningInputHandle>();
                return c != null ? c.CurrentHandle : 0;
            } catch { return 0; }
        }

        bool ImportInputValues() {
            EnsureInputSlotCount();
            for (int i = 0; i < inputs.Count; i++) {
                var resolved = ResolveInputBehaviour(inputs[i]);
                if (TryImportInputValue(resolved, i, out var importedValue)) {
                    inputValues.Add(importedValue);
                    continue;
                }
                if (InputIsValueProvider(resolved)) return false;
                var handle = GetInputHandleSafe(resolved);
                var releaseEmptyHandle = false;
                if (handle == 0) {
                    handle = NativeMethods.cunning_geo_create();
                    releaseEmptyHandle = handle != 0;
                }
                if (handle == 0) return false;
                var desc = new NativeMethods.CunningHostGeometryDesc { handle = handle };
                var value = NativeMethods.cunning_value_import_geometry(runtime.Handle, ref desc);
                if (releaseEmptyHandle) NativeMethods.cunning_release_handle(handle);
                if (value == 0) return false;
                inputValues.Add(value);
            }
            return true;
        }

        bool TryImportInputValue(MonoBehaviour mb, int index, out ulong value) {
            value = 0;
            var provider = ResolveInputValueProvider(mb);
            if (provider == null) return false;
            value = provider.ImportCunningValue(runtime);
            if (value == 0) {
                lastStatus = $"input {index} value import failed: {runtime.LastError()}";
                return false;
            }
            return true;
        }

        static bool InputIsValueProvider(MonoBehaviour mb) => ResolveInputValueProvider(mb) != null;

        static ICunningInputValueHandle ResolveInputValueProvider(MonoBehaviour mb) {
            if (mb == null) return null;
            if (mb is ICunningInputValueHandle direct) return direct;
            return mb.GetComponent<ICunningInputValueHandle>();
        }

        void ReleaseInputValues() {
            for (int i = 0; i < inputValues.Count; i++) {
                var value = inputValues[i];
                runtime?.ReleaseValue(ref value);
            }
            inputValues.Clear();
        }

        void PollJob() {
            if (jobId == 0) return;
            var status = NativeMethods.cunning_job_poll_runtime(runtime.Handle, jobId, out var snapshot);
            lastStatus = "job " + jobId + " " + status;
            if (status == NativeMethods.CunningJobStatus.Ready) {
                var outputCount = NativeMethods.cunning_job_output_count(runtime.Handle, jobId);
                CaptureReadyOutputs(outputCount);
                lastStatus = "job " + jobId + " Ready, outputs " + outputCount;
                jobId = 0;
            } else if (status == NativeMethods.CunningJobStatus.Cancelled || status == NativeMethods.CunningJobStatus.Failed || status == NativeMethods.CunningJobStatus.Invalid) {
                lastStatus += ": " + runtime.LastError();
                jobId = 0;
            }
        }

        void CaptureReadyOutputs(uint outputCount) {
            ReleasePendingApplyValues();
            outputStatus = "capturing " + outputCount + " output(s)";
            var seen = new HashSet<uint>();
            for (uint i = 0; i < outputCount; i++) {
                seen.Add(i);
                if (NativeMethods.cunning_job_output_value(runtime.Handle, jobId, i, out var value) != NativeMethods.CunningStatus.Ok || value == 0) {
                    var error = "failed to acquire output value: " + runtime.LastError();
                    EnsureUnsupportedSlot(i, OutputName(i), 0, error);
                    ReportOutputStatus(i, error);
                    continue;
                }
                pendingApplyValues.Add(new PendingOutput(i, value));
            }
            RemoveMissingSlots(seen);
        }

        void ApplyPendingOutputs() {
            if (pendingApplyValues.Count == 0) return;
            for (int i = 0; i < pendingApplyValues.Count; i++) {
                var pending = pendingApplyValues[i];
                ApplyOutputValue(pending.index, pending.value);
            }
            pendingApplyValues.Clear();
        }

        void ApplyOutputValue(uint index, ulong value) {
            var kind = NativeMethods.cunning_value_kind(runtime.Handle, value);
            var name = OutputName(index);
            var port = asset != null && asset.outputs != null && index < (uint)asset.outputs.Count ? asset.outputs[(int)index] : null;
            var context = new CunningValueOutputContext(this, runtime, index, name, port, kind, value);
            try {
                CunningValueOutputAdapterRegistry.Apply(context);
                if (validateGpuBackendParity && kind == NativeMethods.CunningValueKind.GpuField) QueueGpuBackendParity(index);
            } catch (Exception e) {
                EnsureUnsupportedSlot(index, name, (uint)kind, "output adapter failed: " + e.Message);
                runtime.ReleaseValue(ref value);
            }
        }

        string OutputName(uint index) {
            if (asset != null && asset.outputs != null && index < (uint)asset.outputs.Count) {
                var n = asset.outputs[(int)index]?.name;
                if (!string.IsNullOrEmpty(n)) return n;
            }
            return "output_" + index;
        }

        public OutputSlot PrepareSlot(uint index, string outputName, NativeMethods.CunningValueKind kind) {
            if (slots.TryGetValue(index, out var slot) && slot.kind != kind) {
                DestroySlot(slot);
                slots.Remove(index);
            }
            if (!slots.TryGetValue(index, out slot)) {
                slot = new OutputSlot { index = index };
                slots[index] = slot;
            }
            slot.name = outputName;
            slot.kind = kind;
            if (slot.child == null) {
                slot.child = new GameObject("out_" + index + "_" + outputName);
                slot.child.transform.SetParent(transform, false);
            } else {
                slot.child.name = "out_" + index + "_" + outputName;
            }
            return slot;
        }

        public void WriteTerrainHeights(OutputSlot slot, NativeMethods.CunningGpuFieldInfo info, float[] heightsFlat) {
            var child = EnsureTerrainSlotChild(slot);
            var terrain = child.GetComponent<Terrain>();
            var collider = child.GetComponent<TerrainCollider>();
            var srcWidth = Mathf.Max(1, (int)info.dim_x);
            var srcHeight = Mathf.Max(1, (int)info.dim_y);
            var resolution = ValidTerrainResolution(Mathf.Max(srcWidth, srcHeight));
            var range = ComputeHeightRange(heightsFlat);
            var mapping = BuildTerrainHeightMapping(info, srcWidth, srcHeight);
            var data = terrain.terrainData;
            if (data == null) {
                data = new TerrainData { name = child.name + "_TerrainData" };
                terrain.terrainData = data;
            }
            if (collider == null) collider = child.AddComponent<TerrainCollider>();
            if (data.heightmapResolution != resolution) data.heightmapResolution = resolution;
            var heights = new float[resolution, resolution];
            for (var y = 0; y < resolution; y++) {
                var terrainZ = resolution <= 1 ? 0f : (float)y / (resolution - 1);
                for (var x = 0; x < resolution; x++) {
                    var terrainX = resolution <= 1 ? 0f : (float)x / (resolution - 1);
                    var sample = SampleHeightBilinear(
                        heightsFlat,
                        srcWidth,
                        srcHeight,
                        mapping.FieldX(terrainX, terrainZ, srcWidth),
                        mapping.FieldY(terrainX, terrainZ, srcHeight)
                    );
                    heights[y, x] = Mathf.Clamp01(mapping.heightScaleY >= 0f
                        ? (sample - range.min) / range.span
                        : (range.max - sample) / range.span);
                }
            }
            data.SetHeightsDelayLOD(0, 0, heights);
            var worldMinY = mapping.originY + Mathf.Min(range.min * mapping.heightScaleY, range.max * mapping.heightScaleY);
            var worldMaxY = mapping.originY + Mathf.Max(range.min * mapping.heightScaleY, range.max * mapping.heightScaleY);
            var worldSpanY = Mathf.Max(1e-5f, worldMaxY - worldMinY);
            data.size = new Vector3(mapping.sizeX, worldSpanY, mapping.sizeZ);
            data.SyncHeightmap();
            collider.terrainData = data;
            child.transform.localPosition = new Vector3(mapping.originX, worldMinY, mapping.originZ);
            outputStatus = child.name + " terrain " + srcWidth + "x" + srcHeight + " -> " + resolution + "x" + resolution
                + " height min=" + range.min.ToString("0.###")
                + " max=" + range.max.ToString("0.###")
                + " range=" + range.span.ToString("0.###")
                + " yScale=" + mapping.heightScaleY.ToString("0.###");
        }

        static (float min, float max, float span) ComputeHeightRange(float[] heights) {
            var min = float.PositiveInfinity;
            var max = float.NegativeInfinity;
            for (int i = 0; i < heights.Length; i++) {
                var h = heights[i];
                if (float.IsNaN(h) || float.IsInfinity(h)) continue;
                if (h < min) min = h;
                if (h > max) max = h;
            }
            if (float.IsInfinity(min) || float.IsInfinity(max)) return (0f, 0f, 600f);
            var span = max - min;
            if (span <= 1e-5f) span = 600f;
            span = Mathf.Min(span, 65536f);
            return (min, max, span);
        }

        TerrainHeightMapping BuildTerrainHeightMapping(NativeMethods.CunningGpuFieldInfo info, int srcWidth, int srcHeight) {
            var basisX = CunningUnityCoordinates.ToUnityDirection(new Vector3(info.world_transform.basis_x_x, info.world_transform.basis_x_y, info.world_transform.basis_x_z));
            var basisY = CunningUnityCoordinates.ToUnityDirection(new Vector3(info.world_transform.basis_y_x, info.world_transform.basis_y_y, info.world_transform.basis_y_z));
            var basisZ = CunningUnityCoordinates.ToUnityDirection(new Vector3(info.world_transform.basis_z_x, info.world_transform.basis_z_y, info.world_transform.basis_z_z));
            var origin = CunningUnityCoordinates.ToUnityPosition(new Vector3(info.world_transform.origin_x, info.world_transform.origin_y, info.world_transform.origin_z));
            var axisX = DominantHorizontalAxis(basisX);
            var axisY = DominantHorizontalAxis(basisY);
            var validAxisPair = axisX != axisY;
            var fieldXUsesTerrainX = validAxisPair ? axisX == 0 : true;
            var fieldYUsesTerrainX = validAxisPair ? axisY == 0 : false;
            var flipFieldX = AxisComponent(basisX, fieldXUsesTerrainX ? 0 : 2) < 0f;
            var flipFieldY = AxisComponent(basisY, fieldYUsesTerrainX ? 0 : 2) < 0f;
            var planeOrigin = origin + (basisX + basisY) * 0.5f;
            var p00 = planeOrigin;
            var p10 = planeOrigin + basisX * Mathf.Max(0, srcWidth - 1);
            var p01 = planeOrigin + basisY * Mathf.Max(0, srcHeight - 1);
            var p11 = p10 + basisY * Mathf.Max(0, srcHeight - 1);
            var minX = Mathf.Min(Mathf.Min(p00.x, p10.x), Mathf.Min(p01.x, p11.x));
            var maxX = Mathf.Max(Mathf.Max(p00.x, p10.x), Mathf.Max(p01.x, p11.x));
            var minZ = Mathf.Min(Mathf.Min(p00.z, p10.z), Mathf.Min(p01.z, p11.z));
            var maxZ = Mathf.Max(Mathf.Max(p00.z, p10.z), Mathf.Max(p01.z, p11.z));
            var sizeX = maxX - minX;
            var sizeZ = maxZ - minZ;
            if (sizeX <= 1e-5f) sizeX = Mathf.Max(1e-5f, defaultTerrainSize.x);
            if (sizeZ <= 1e-5f) sizeZ = Mathf.Max(1e-5f, defaultTerrainSize.z);
            var heightScaleY = basisZ.y;
            if (Mathf.Abs(heightScaleY) <= 1e-5f) heightScaleY = basisZ.magnitude;
            if (Mathf.Abs(heightScaleY) <= 1e-5f) heightScaleY = 1f;
            return new TerrainHeightMapping(fieldXUsesTerrainX, fieldYUsesTerrainX, flipFieldX, flipFieldY, minX, planeOrigin.y, minZ, sizeX, heightScaleY, sizeZ);
        }

        static int DominantHorizontalAxis(Vector3 value) {
            return Mathf.Abs(value.x) >= Mathf.Abs(value.z) ? 0 : 2;
        }

        static float AxisComponent(Vector3 value, int axis) {
            return axis == 0 ? value.x : value.z;
        }

        static float SampleHeightBilinear(float[] values, int width, int height, float x, float y) {
            if (values == null || values.Length == 0 || width <= 0 || height <= 0) return 0f;
            x = Mathf.Clamp(x, 0f, Mathf.Max(0, width - 1));
            y = Mathf.Clamp(y, 0f, Mathf.Max(0, height - 1));
            var x0 = Mathf.FloorToInt(x);
            var y0 = Mathf.FloorToInt(y);
            var x1 = Mathf.Min(x0 + 1, width - 1);
            var y1 = Mathf.Min(y0 + 1, height - 1);
            var tx = x - x0;
            var ty = y - y0;
            var v00 = values[Mathf.Min(values.Length - 1, y0 * width + x0)];
            var v10 = values[Mathf.Min(values.Length - 1, y0 * width + x1)];
            var v01 = values[Mathf.Min(values.Length - 1, y1 * width + x0)];
            var v11 = values[Mathf.Min(values.Length - 1, y1 * width + x1)];
            return Mathf.Lerp(Mathf.Lerp(v00, v10, tx), Mathf.Lerp(v01, v11, tx), ty);
        }

        GameObject EnsureTerrainSlotChild(OutputSlot slot) {
            var childName = "out_" + slot.index + "_" + slot.name;
            if (slot.child != null && slot.child.GetComponent<Terrain>() != null) {
                slot.child.name = childName;
                return slot.child;
            }

            if (slot.child != null) {
#if UNITY_EDITOR
                if (!Application.isPlaying) DestroyImmediate(slot.child);
                else Destroy(slot.child);
#else
                Destroy(slot.child);
#endif
            }

            var data = new TerrainData { name = childName + "_TerrainData" };
            slot.child = Terrain.CreateTerrainGameObject(data);
            slot.child.name = childName;
            slot.child.transform.SetParent(transform, false);
            return slot.child;
        }

        public void ReportOutputStatus(uint index, string status) {
            outputStatus = "output_" + index + ": " + status;
        }

        public void QueueGpuBackendParity(uint outputIndex = 0) {
            gpuParityOutputIndex = outputIndex;
            gpuParityQueued = true;
            gpuParityStatus = "queued output_" + outputIndex;
        }

        void TickGpuBackendParity() {
            if (gpuParityQueued && gpuParityJobId == 0) StartGpuBackendParity();
            if (gpuParityJobId != 0) PollGpuBackendParity();
        }

        void StartGpuBackendParity() {
            gpuParityQueued = false;
            if (inputs.Count != 0) {
                gpuParityStatus = "skipped: parity with external inputs needs matching import values";
                return;
            }
            if (!slots.TryGetValue(gpuParityOutputIndex, out var slot) || slot.value == 0 || slot.kind != NativeMethods.CunningValueKind.GpuField) {
                gpuParityStatus = "skipped: no current GpuField output_" + gpuParityOutputIndex;
                return;
            }
            EnsureRuntime();
            SyncAsset();
            if (cdaId == 0) {
                gpuParityStatus = "skipped: CDA not loaded";
                return;
            }
            CancelGpuBackendParity();
            parityRuntime = new CunningRuntimeContext(NativeMethods.CunningRuntimeBackendMode.StandaloneOwnedDevice);
            parityRuntime.BeginFrame(Application.isPlaying, cpuDeadlineNs, gpuDeadlineNs, gpuBudgetBytes);
            gpuParityGeneration++;
            var paramsJson = BuildParamsJson();
            gpuParityJobId = NativeMethods.cunning_cda_submit_values(
                parityRuntime.Handle,
                cdaId,
                instanceId ^ 0x9e3779b97f4a7c15UL,
                gpuParityGeneration,
                paramsJson,
                Array.Empty<ulong>(),
                0
            );
            parityRuntime.EndFrame();
            gpuParityStatus = gpuParityJobId == 0 ? "submit failed: " + parityRuntime.LastError() : "submitted standalone wgpu job " + gpuParityJobId;
            if (gpuParityJobId == 0) {
                parityRuntime.Dispose();
                parityRuntime = null;
            }
        }

        void PollGpuBackendParity() {
            if (parityRuntime == null || parityRuntime.Handle == 0 || gpuParityJobId == 0) return;
            parityRuntime.BeginFrame(Application.isPlaying, cpuDeadlineNs, gpuDeadlineNs, gpuBudgetBytes);
            var status = NativeMethods.cunning_job_poll_runtime(parityRuntime.Handle, gpuParityJobId, out _);
            if (status == NativeMethods.CunningJobStatus.Ready) {
                CompleteGpuBackendParity();
            } else if (status == NativeMethods.CunningJobStatus.Cancelled || status == NativeMethods.CunningJobStatus.Failed || status == NativeMethods.CunningJobStatus.Invalid) {
                gpuParityStatus = "standalone wgpu job " + gpuParityJobId + " " + status + ": " + parityRuntime.LastError();
                gpuParityJobId = 0;
                parityRuntime.EndFrame();
                parityRuntime.Dispose();
                parityRuntime = null;
            } else {
                gpuParityStatus = "standalone wgpu job " + gpuParityJobId + " " + status;
                parityRuntime.EndFrame();
            }
        }

        void CompleteGpuBackendParity() {
            if (!slots.TryGetValue(gpuParityOutputIndex, out var slot) || slot.value == 0) {
                gpuParityStatus = "failed: host output disappeared";
                FinishGpuBackendParityFrame();
                return;
            }
            if (NativeMethods.cunning_job_output_value(parityRuntime.Handle, gpuParityJobId, gpuParityOutputIndex, out var referenceValue) != NativeMethods.CunningStatus.Ok || referenceValue == 0) {
                gpuParityStatus = "failed: standalone output missing " + parityRuntime.LastError();
                FinishGpuBackendParityFrame();
                return;
            }
            try {
                if (!CunningGpuFieldDebug.TryReadHeightLayer(runtime, slot.value, out var hostSnapshot, out var hostError)) {
                    gpuParityStatus = "failed: host readback " + hostError;
                    return;
                }
                if (!CunningGpuFieldDebug.TryReadHeightLayer(parityRuntime, referenceValue, out var referenceSnapshot, out var referenceError)) {
                    gpuParityStatus = "failed: standalone readback " + referenceError;
                    return;
                }
                var report = CunningGpuFieldDebug.Compare(hostSnapshot, referenceSnapshot, Mathf.Max(1e-6f, gpuParityEpsilon));
                gpuParityStatus = "output_" + gpuParityOutputIndex + " " + report.summary;
            } finally {
                parityRuntime.ReleaseValue(ref referenceValue);
                FinishGpuBackendParityFrame();
            }
        }

        void FinishGpuBackendParityFrame() {
            gpuParityJobId = 0;
            parityRuntime.EndFrame();
            parityRuntime.Dispose();
            parityRuntime = null;
        }

        static int ValidTerrainResolution(int desired) {
            desired = Mathf.Clamp(desired, 33, 4097);
            var p = 32;
            while (p + 1 < desired && p < 4096) p <<= 1;
            return Mathf.Clamp(p + 1, 33, 4097);
        }

        public void EnsureUnsupportedSlot(uint index, string outputName, uint kind, string reason) {
            var slot = PrepareSlot(index, outputName, NativeMethods.CunningValueKind.Invalid);
            var output = slot.child.GetComponent<CunningUnsupportedOutput>() ?? slot.child.AddComponent<CunningUnsupportedOutput>();
            output.Apply(kind, reason);
        }

        public void RemoveSlot(uint index) {
            if (!slots.TryGetValue(index, out var slot)) return;
            DestroySlot(slot);
            slots.Remove(index);
        }

        void RemoveMissingSlots(HashSet<uint> seen) {
            var remove = new List<uint>();
            foreach (var kv in slots) if (!seen.Contains(kv.Key)) remove.Add(kv.Key);
            for (int i = 0; i < remove.Count; i++) {
                DestroySlot(slots[remove[i]]);
                slots.Remove(remove[i]);
            }
        }

        void CancelJob() {
            if (jobId == 0) return;
            if (runtime != null && runtime.Handle != 0) NativeMethods.cunning_job_cancel_runtime(runtime.Handle, jobId);
            jobId = 0;
        }

        void CancelGpuBackendParity() {
            gpuParityQueued = false;
            if (gpuParityJobId != 0 && parityRuntime != null && parityRuntime.Handle != 0) NativeMethods.cunning_job_cancel_runtime(parityRuntime.Handle, gpuParityJobId);
            gpuParityJobId = 0;
            parityRuntime?.Dispose();
            parityRuntime = null;
        }

        void ReleasePendingApplyValues() {
            for (int i = 0; i < pendingApplyValues.Count; i++) {
                var value = pendingApplyValues[i].value;
                runtime?.ReleaseValue(ref value);
            }
            pendingApplyValues.Clear();
        }

        void DestroySlot(OutputSlot slot) {
            if (slot == null) return;
            if (slot.value != 0) runtime?.ReleaseValue(ref slot.value);
            if (slot.geometryHandle != 0) {
                NativeMethods.cunning_release_handle(slot.geometryHandle);
                slot.geometryHandle = 0;
            }
            ReleaseSlotUnityResources(slot);
            if (slot.child != null) {
#if UNITY_EDITOR
                if (!Application.isPlaying) DestroyImmediate(slot.child);
                else Destroy(slot.child);
#else
                Destroy(slot.child);
#endif
            }
        }

        void ReleaseSlotUnityResources(OutputSlot slot) {
            if (slot.child == null) return;
            var terrain = slot.child.GetComponent<Terrain>();
            if (terrain == null || terrain.terrainData == null) return;
            var data = terrain.terrainData;
            terrain.terrainData = null;
            var collider = slot.child.GetComponent<TerrainCollider>();
            if (collider != null && collider.terrainData == data) collider.terrainData = null;
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(data);
            else Destroy(data);
#else
            Destroy(data);
#endif
        }

        void Shutdown() {
            ReleaseRuntimeOwnedState();
        }

        void ReleaseRuntimeOwnedState() {
            CancelJob();
            CancelGpuBackendParity();
            ReleasePendingApplyValues();
            ReleaseInputValues();
            foreach (var kv in slots) DestroySlot(kv.Value);
            slots.Clear();
            runtime?.Dispose();
            runtime = null;
            runtimeBackendMode = 0;
        }

        static uint Hash32(string s) {
            if (string.IsNullOrEmpty(s)) return 0;
            unchecked {
                uint h = 2166136261u;
                for (int i = 0; i < s.Length; i++) {
                    h ^= s[i];
                    h *= 16777619u;
                }
                return h;
            }
        }
    }
}
