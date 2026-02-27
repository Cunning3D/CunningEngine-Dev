using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using CunningEngine.CDA;

namespace CunningEngine {
    [ExecuteAlways]
    public sealed class CunningCDAInstance : MonoBehaviour {
        [HideInInspector] public CDAAssetObject asset;
        public List<MonoBehaviour> inputs = new();
        public bool autoCook = true;
        public List<string> selectedExports = new(); // optional: names in CDA exports list

        [Header("Debug")]
        public ulong instanceId;
        public ulong cdaId;
        public ulong gen;
        public ulong jobId;

        [NonSerialized] public Dictionary<string, CdaParamValue> paramValues = new(); // edited by custom inspector

        readonly Dictionary<string, CunningMesh> outMeshes = new();
        static bool s_nativeInited;
        uint lastJobStatus;
        uint assetSig;
        readonly List<ulong> lastInputDirty = new();

        void Reset() { if (instanceId == 0) instanceId = (ulong)UnityEngine.Random.Range(int.MinValue, int.MaxValue) ^ ((ulong)DateTime.UtcNow.Ticks << 1); }

        void OnEnable() { EnsureNativeInit(); SyncAsset(); EnsureOutputs(); if (autoCook) Cook(); }
        void OnDisable() { if (jobId != 0) { NativeMethods.cunning_job_cancel(jobId); jobId = 0; } }

        static void EnsureNativeInit() {
            if (s_nativeInited) return;
            try { NativeMethods.EnsureLoaded(); NativeMethods.cunning_init(); s_nativeInited = true; }
            catch (Exception e) { Debug.LogWarning($"CunningCDAInstance: Native init failed: {e.Message}"); }
        }

        static uint Hash32(string s) {
            if (string.IsNullOrEmpty(s)) return 0;
            unchecked { uint h = 2166136261u; for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619u; } return h; }
        }

        string GetAssetAbsPath() {
            if (asset == null || string.IsNullOrEmpty(asset.sourcePath)) return "";
            var p = asset.sourcePath.Replace('\\', '/');
            if (p.StartsWith("Assets/") || p == "Assets") return System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", p));
            return System.IO.Path.GetFullPath(p);
        }

        void SyncAsset() {
            if (asset == null) return;
            uint sig = Hash32(asset.sourcePath) ^ Hash32(asset.sourceJson);
            if (sig != assetSig) { assetSig = sig; cdaId = 0; if (jobId != 0) { NativeMethods.cunning_job_cancel(jobId); jobId = 0; } }
            // Keep input slots aligned with CDA inputs for a predictable workflow.
            int want = asset.inputs != null ? asset.inputs.Count : 0;
            while (inputs.Count < want) inputs.Add(null);
            if (inputs.Count > want) inputs.RemoveRange(want, inputs.Count - want);
            while (lastInputDirty.Count < want) lastInputDirty.Add(0);
            if (lastInputDirty.Count > want) lastInputDirty.RemoveRange(want, lastInputDirty.Count - want);
        }

        void EnsureOutputs() {
            if (asset == null) return;
            for (int i = 0; i < asset.outputs.Count; i++) {
                var p = asset.outputs[i];
                var key = !string.IsNullOrEmpty(p.name) ? p.name : $"output_{i}";
                if (outMeshes.ContainsKey(key)) continue;
                var go = new GameObject($"out_{key}");
                go.transform.SetParent(transform, false);
                if (go.GetComponent<MeshRenderer>() == null) go.AddComponent<MeshRenderer>();
                var cm = go.AddComponent<CunningMesh>();
                outMeshes[key] = cm;
            }
        }

        public void Cook() {
            if (asset == null) return;
            SyncAsset();
            EnsureOutputs();
            if (string.IsNullOrEmpty(asset.sourcePath)) { Debug.LogWarning("CunningCDAInstance: asset.sourcePath is empty"); return; }
            var abs = GetAssetAbsPath();
            if (cdaId == 0) {
                cdaId = NativeMethods.cunning_cda_load(abs);
                if (cdaId == 0) {
                    var sb = new StringBuilder(1024);
                    NativeMethods.cunning_cda_get_last_error(sb, (uint)sb.Capacity);
                    Debug.LogWarning($"CunningCDAInstance: cunning_cda_load failed: {abs}\n{sb}");
                    return;
                }
            }
            if (jobId != 0) { NativeMethods.cunning_job_cancel(jobId); jobId = 0; }
            gen++;
            var inputHandles = new List<ulong>();
            foreach (var mb in inputs) inputHandles.Add(mb is ICunningInputHandle ih ? ih.CurrentHandle : 0);
            var json = BuildParamsJson();
            if (selectedExports != null && selectedExports.Count > 0) {
                var exJson = BuildExportsJson(selectedExports);
                jobId = NativeMethods.cunning_cda_submit_select(cdaId, instanceId, gen, json, inputHandles.ToArray(), (uint)inputHandles.Count, exJson);
            } else {
                jobId = NativeMethods.cunning_cda_submit(cdaId, instanceId, gen, json, inputHandles.ToArray(), (uint)inputHandles.Count);
            }
            lastJobStatus = 0;
            if (jobId == 0) Debug.LogWarning("CunningCDAInstance: cunning_cda_submit returned 0 (failed)");
        }

        void Update() {
            if (autoCook && asset != null) {
                SyncAsset();
                if (jobId == 0 && inputs.Count > 0) {
                    bool changed = false;
                    for (int i = 0; i < inputs.Count; i++) {
                        var h = inputs[i] is ICunningInputHandle ih ? ih.CurrentHandle : 0;
                        if (h == 0) continue;
                        var d = NativeMethods.cunning_geo_get_dirty_id(h);
                        if (d != lastInputDirty[i]) { lastInputDirty[i] = d; changed = true; }
                    }
                    if (changed) Cook();
                }
            }
            if (jobId == 0) return;
            var st = NativeMethods.cunning_job_poll(jobId);
            if (st != lastJobStatus) { lastJobStatus = st; }
            if (st != 2) return; // Ready
            var n = NativeMethods.cunning_job_get_output_count(jobId);
            for (uint i = 0; i < n; i++) {
                var k = NativeMethods.cunning_job_get_output_kind(jobId, i);
                if (k == 1) { // GeoHandle
                    var h = NativeMethods.cunning_job_get_output_geo_handle(jobId, i);
                    var key = (asset != null && i < (uint)asset.outputs.Count && !string.IsNullOrEmpty(asset.outputs[(int)i].name)) ? asset.outputs[(int)i].name : $"output_{i}";
                    if (outMeshes.TryGetValue(key, out var cm)) cm.LoadFromHandle(h);
                } else {
                    var sb = new StringBuilder(4096);
                    NativeMethods.cunning_job_get_output_param_json(jobId, i, sb, (uint)sb.Capacity);
                    Debug.Log($"CunningCDAInstance: export[{i}] param_json = {sb}");
                }
            }
            jobId = 0;
        }

        static string BuildExportsJson(List<string> names) {
            if (names == null || names.Count == 0) return "[]";
            var sb = new StringBuilder(64);
            sb.Append('[');
            for (int i = 0; i < names.Count; i++) {
                if (i != 0) sb.Append(',');
                sb.Append('"').Append(CdaMiniJson.EscapeString(names[i] ?? "")).Append('"');
            }
            sb.Append(']');
            return sb.ToString();
        }

        string BuildParamsJson() {
            // Fill defaults if missing
            if (asset != null) {
                foreach (var p in asset.promoted_params) if (!paramValues.ContainsKey(p.name)) paramValues[p.name] = CdaParamValue.FromDefaultJson(p.default_value_json);
            }
            var sb = new StringBuilder(256);
            sb.Append('{');
            var first = true;
            foreach (var kv in paramValues) {
                if (kv.Value == null) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(CdaMiniJson.EscapeString(kv.Key)).Append('"').Append(':');
                kv.Value.WriteSerdeJson(sb);
            }
            sb.Append('}');
            return sb.ToString();
        }
    }
}

