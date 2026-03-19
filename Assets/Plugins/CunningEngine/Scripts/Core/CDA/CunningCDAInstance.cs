using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using CunningEngine.CDA;

namespace CunningEngine {
    [Serializable]
    public sealed class CdaSelectTarget {
        public string name;
        public string kind = "NodeParam"; // NodeParam only (NodeOutput geometry must go through CDA outputs)
        public string node_id;
        public string node_name;
        public string param;
        public uint? channel;
    }

    [ExecuteAlways]
    public sealed class CunningCDAInstance : MonoBehaviour {
        [HideInInspector] public CDAAssetObject asset;
        public List<MonoBehaviour> inputs = new();
        public bool autoCook = true;
        public bool enableInstancing = true;
        public bool autoSyncGeometrySheet = true; // editor inspector auto-refresh for selected node geometry
        public List<CdaSelectTarget> selectedTargets = new(); // optional NodeParam targets (prefer node_name)

        [Header("Debug")]
        public ulong instanceId;
        public ulong cdaId;
        public ulong gen;
        public ulong jobId;
        [NonSerialized] public NativeMethods.JobStats lastMainJobStats;
        [NonSerialized] public ulong lastMainJobId;
        [NonSerialized] public NativeMethods.JobStats lastSelectJobStats;
        [NonSerialized] public ulong lastSelectJobId;
        public bool detailedTimingLogs = true;
        public float detailedTimingLogMinMs = 1f;

        [NonSerialized] public Dictionary<string, CdaParamValue> paramValues = new(); // edited by custom inspector
        [NonSerialized] public Dictionary<string, string> selectedTargetOutputsJson = new(); // key -> param json

        readonly Dictionary<string, CunningMesh> outMeshes = new();
        readonly Dictionary<string, CunningInstancer> outInstancers = new();
        readonly Dictionary<string, ulong> outHandles = new();
        static bool s_nativeInited;
        static bool s_cacheClearApiAvailable = true;
        static bool s_cacheClearApiWarned;
        static bool s_cacheEnableApiAvailable = true;
        static bool s_cacheEnableApiWarned;
        uint lastJobStatus;
        uint lastSelectJobStatus;
        uint assetSig;
        readonly List<ulong> lastInputDirty = new();
        ulong selectJobId;
        string[] selectJobOutputKeys;

        void Reset() { if (instanceId == 0) instanceId = (ulong)UnityEngine.Random.Range(int.MinValue, int.MaxValue) ^ ((ulong)DateTime.UtcNow.Ticks << 1); }

        void OnEnable() { EnsureNativeInit(); SyncAsset(); EnsureOutputs(); if (autoCook) Cook(); }
        void OnDisable() {
            if (jobId != 0) {
                NativeMethods.cunning_job_cancel(jobId);
                jobId = 0;
            }
            if (selectJobId != 0) {
                NativeMethods.cunning_job_cancel(selectJobId);
                selectJobId = 0;
            }
            SetCachedNodeGeoEnabledSafe(false);
            ClearCachedNodeGeoSafe();
            ClearOutputs();
        }

        static void EnsureNativeInit() {
            if (s_nativeInited) return;
            try { NativeMethods.EnsureLoaded(); NativeMethods.cunning_init(); s_nativeInited = true; }
            catch (Exception e) { Debug.LogWarning($"CunningCDAInstance: Native init failed: {e.Message}"); }
        }

        bool ShouldLogTiming(double totalMs) => detailedTimingLogs && totalMs >= detailedTimingLogMinMs;
        void LogTiming(string phase, string detail, double totalMs) {
            if (!ShouldLogTiming(totalMs)) return;
            Debug.Log($"CunningCDAInstance[{name}]: {phase} {detail}");
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            UnityEngine.Profiling.Profiler.BeginSample("CDA.SyncAsset");
            try {
#endif
            if (asset == null) return;
            uint jsonHash = asset.sourceJsonHash;
            if (jsonHash == 0 && !string.IsNullOrEmpty(asset.sourceJson)) {
                jsonHash = Hash32(asset.sourceJson);
                asset.sourceJsonHash = jsonHash;
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(asset);
#endif
            }
            uint sig = Hash32(asset.sourcePath) ^ jsonHash;
            if (sig != assetSig) {
                assetSig = sig;
                cdaId = 0;
                if (jobId != 0) {
                    NativeMethods.cunning_job_cancel(jobId);
                    jobId = 0;
                }
                if (selectJobId != 0) {
                    NativeMethods.cunning_job_cancel(selectJobId);
                    selectJobId = 0;
                }
                ClearCachedNodeGeoSafe();
            }
            // Keep input slots aligned with CDA inputs for a predictable workflow.
            int want = asset.inputs != null ? asset.inputs.Count : 0;
            while (inputs.Count < want) inputs.Add(null);
            if (inputs.Count > want) inputs.RemoveRange(want, inputs.Count - want);
            while (lastInputDirty.Count < want) lastInputDirty.Add(0);
            if (lastInputDirty.Count > want) lastInputDirty.RemoveRange(want, lastInputDirty.Count - want);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            } finally {
                UnityEngine.Profiling.Profiler.EndSample();
            }
#endif
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
                cm.ownsHandle = false; // handle is owned by this CDA instance (may be shared by multiple consumers)
                outMeshes[key] = cm;

                var ci = go.AddComponent<CunningInstancer>();
                ci.enabled = enableInstancing;
                outInstancers[key] = ci;
            }
        }

        static ulong GetInputHandleSafe(MonoBehaviour mb) {
            if (!mb) return 0;
            try {
                return mb is ICunningInputHandle ih ? ih.CurrentHandle : 0;
            } catch (MissingReferenceException) {
                return 0;
            } catch (NullReferenceException) {
                return 0;
            }
        }

        public void Cook() {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            UnityEngine.Profiling.Profiler.BeginSample("CDA.Cook");
            try {
#endif
            var totalSw = System.Diagnostics.Stopwatch.StartNew();
            double syncMs = 0d, ensureOutputsMs = 0d, loadMs = 0d, cancelMs = 0d, gatherInputsMs = 0d, paramsJsonMs = 0d, submitMs = 0d;
            bool loadedNow = false, cancelledMain = false, cancelledSelect = false;
            int zeroInputHandles = 0, jsonBytes = 0;
            if (asset == null) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            SyncAsset();
            sw.Stop();
            syncMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            EnsureOutputs();
            sw.Stop();
            ensureOutputsMs = sw.Elapsed.TotalMilliseconds;
            selectedTargetOutputsJson.Clear();
            if (string.IsNullOrEmpty(asset.sourcePath)) { Debug.LogWarning("CunningCDAInstance: asset.sourcePath is empty"); return; }
            var abs = GetAssetAbsPath();
            sw.Restart();
            if (cdaId == 0) {
                loadedNow = true;
                cdaId = NativeMethods.cunning_cda_load(abs);
                if (cdaId == 0) {
                    var sb = new StringBuilder(1024);
                    NativeMethods.cunning_cda_get_last_error(sb, (uint)sb.Capacity);
                    Debug.LogWarning($"CunningCDAInstance: cunning_cda_load failed: {abs}\n{sb}");
                    return;
                }
            }
            sw.Stop();
            loadMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            if (jobId != 0) { NativeMethods.cunning_job_cancel(jobId); jobId = 0; cancelledMain = true; }
            if (selectJobId != 0) { NativeMethods.cunning_job_cancel(selectJobId); selectJobId = 0; cancelledSelect = true; }
            sw.Stop();
            cancelMs = sw.Elapsed.TotalMilliseconds;
            gen++;
            sw.Restart();
            var inputHandles = new List<ulong>();
            foreach (var mb in inputs) {
                var handle = GetInputHandleSafe(mb);
                if (handle == 0) zeroInputHandles++;
                inputHandles.Add(handle);
            }
            sw.Stop();
            gatherInputsMs = sw.Elapsed.TotalMilliseconds;
            string json;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            UnityEngine.Profiling.Profiler.BeginSample("CDA.BuildParamsJson");
            try {
                sw.Restart();
                json = BuildParamsJson();
                sw.Stop();
                paramsJsonMs = sw.Elapsed.TotalMilliseconds;
            } finally {
                UnityEngine.Profiling.Profiler.EndSample();
            }
#else
            sw.Restart();
            json = BuildParamsJson();
            sw.Stop();
            paramsJsonMs = sw.Elapsed.TotalMilliseconds;
#endif
            jsonBytes = string.IsNullOrEmpty(json) ? 0 : Encoding.UTF8.GetByteCount(json);
            sw.Restart();
            jobId = NativeMethods.cunning_cda_submit(cdaId, instanceId, gen, json, inputHandles.ToArray(), (uint)inputHandles.Count);
            sw.Stop();
            submitMs = sw.Elapsed.TotalMilliseconds;
            lastJobStatus = 0;
            if (jobId == 0) Debug.LogWarning("CunningCDAInstance: cunning_cda_submit returned 0 (failed)");
            totalSw.Stop();
            LogTiming(
                "Cook",
                $"asset={(asset ? asset.name : "<null>")}, inputs={inputHandles.Count}, zeroInputs={zeroInputHandles}, " +
                $"loadedNow={loadedNow}, cancelledMain={cancelledMain}, cancelledSelect={cancelledSelect}, jsonBytes={jsonBytes}, " +
                $"sync={syncMs:F3} ms, ensureOutputs={ensureOutputsMs:F3} ms, load={loadMs:F3} ms, cancel={cancelMs:F3} ms, " +
                $"gatherInputs={gatherInputsMs:F3} ms, paramsJson={paramsJsonMs:F3} ms, submit={submitMs:F3} ms, total={totalSw.Elapsed.TotalMilliseconds:F3} ms",
                totalSw.Elapsed.TotalMilliseconds
            );
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            } finally {
                UnityEngine.Profiling.Profiler.EndSample();
            }
#endif
        }

        void Update() {
            if (autoCook && asset != null) {
                SyncAsset();
                if (jobId == 0 && inputs.Count > 0) {
                    var scanSw = System.Diagnostics.Stopwatch.StartNew();
                    bool changed = false;
                    int zeroHandles = 0;
                    var sb = detailedTimingLogs ? new StringBuilder(128) : null;
                    for (int i = 0; i < inputs.Count; i++) {
                        var h = GetInputHandleSafe(inputs[i]);
                        if (h == 0) zeroHandles++;
                        var d = h != 0 ? NativeMethods.cunning_geo_get_dirty_id(h) : 0;
                        if (d != lastInputDirty[i]) { lastInputDirty[i] = d; changed = true; }
                        if (sb != null) {
                            if (i != 0) sb.Append(" | ");
                            sb.Append('#').Append(i).Append(":h=").Append(h).Append(",d=").Append(d);
                        }
                    }
                    scanSw.Stop();
                    if (changed || ShouldLogTiming(scanSw.Elapsed.TotalMilliseconds)) {
                        LogTiming(
                            "DirtyScan",
                            $"changed={changed}, inputs={inputs.Count}, zeroHandles={zeroHandles}, scan={scanSw.Elapsed.TotalMilliseconds:F3} ms, slots=[{sb}]",
                            scanSw.Elapsed.TotalMilliseconds
                        );
                    }
                    if (changed) Cook();
                }
            }

            if (jobId != 0) {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                UnityEngine.Profiling.Profiler.BeginSample("CDA.PollMainJob");
                try {
#endif
                var pollSw = System.Diagnostics.Stopwatch.StartNew();
                var st = NativeMethods.cunning_job_poll(jobId);
                pollSw.Stop();
                if (st != lastJobStatus) { lastJobStatus = st; }
                if (st == 2) { // Ready
                    var applySw = System.Diagnostics.Stopwatch.StartNew();
                    uint n = 0;
                    uint geoOutputCount = 0;
                    lastMainJobId = jobId;
                    if (NativeMethods.TryGetJobStats(jobId, out var stats)) lastMainJobStats = stats;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    UnityEngine.Profiling.Profiler.BeginSample("CDA.ApplyOutputs");
                    try {
#endif
                    n = NativeMethods.cunning_job_get_output_count(jobId);
                    for (uint i = 0; i < n; i++) {
                        var k = NativeMethods.cunning_job_get_output_kind(jobId, i);
                        if (k == 1) { // GeoHandle
                            geoOutputCount++;
                            var h = NativeMethods.cunning_job_get_output_geo_handle(jobId, i);
                            var key = (asset != null && i < (uint)asset.outputs.Count && !string.IsNullOrEmpty(asset.outputs[(int)i].name)) ? asset.outputs[(int)i].name : $"output_{i}";
                            outHandles.TryGetValue(key, out var old);
                            outHandles[key] = h;
                            if (outMeshes.TryGetValue(key, out var cm)) cm.LoadFromHandle(h);
                            if (outInstancers.TryGetValue(key, out var ci)) { ci.enabled = enableInstancing; ci.LoadFromHandle(h); }
                            if (old != 0 && old != h) NativeMethods.cunning_release_handle(old);
                        }
                    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    } finally {
                        UnityEngine.Profiling.Profiler.EndSample();
                    }
#endif
                    applySw.Stop();
                    var totalMs = pollSw.Elapsed.TotalMilliseconds + applySw.Elapsed.TotalMilliseconds;
                    LogTiming(
                        "MainJobReady",
                        $"status={st}, outputs={n}, geoOutputs={geoOutputCount}, poll={pollSw.Elapsed.TotalMilliseconds:F3} ms, apply={applySw.Elapsed.TotalMilliseconds:F3} ms, total={totalMs:F3} ms",
                        totalMs
                    );
                    jobId = 0;
                } else if (st == 3 || st == 4) { // Cancelled / Failed
                    lastMainJobId = jobId;
                    if (NativeMethods.TryGetJobStats(jobId, out var stats)) lastMainJobStats = stats;
                    Debug.LogWarning($"CunningCDAInstance[{name}]: MainJobEnd status={st}, poll={pollSw.Elapsed.TotalMilliseconds:F3} ms");
                    jobId = 0;
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                } finally {
                    UnityEngine.Profiling.Profiler.EndSample();
                }
#endif
            }

            if (selectJobId != 0) {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                UnityEngine.Profiling.Profiler.BeginSample("CDA.PollSelectJob");
                try {
#endif
                var pollSw = System.Diagnostics.Stopwatch.StartNew();
                var st = NativeMethods.cunning_job_poll(selectJobId);
                pollSw.Stop();
                if (st != lastSelectJobStatus) { lastSelectJobStatus = st; }
                if (st == 2) { // Ready
                    var readSw = System.Diagnostics.Stopwatch.StartNew();
                    uint n = 0;
                    lastSelectJobId = selectJobId;
                    if (NativeMethods.TryGetJobStats(selectJobId, out var stats)) lastSelectJobStats = stats;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    UnityEngine.Profiling.Profiler.BeginSample("CDA.ReadSelectOutputs");
                    try {
#endif
                    n = NativeMethods.cunning_job_get_output_count(selectJobId);
                    for (uint i = 0; i < n; i++) {
                        var k = NativeMethods.cunning_job_get_output_kind(selectJobId, i);
                        if (k == 1) continue; // selected targets are expected to be param outputs
                        var sb = new StringBuilder(4096);
                        NativeMethods.cunning_job_get_output_param_json(selectJobId, i, sb, (uint)sb.Capacity);
                        var key = ResolveSelectJobOutputKey(i);
                        selectedTargetOutputsJson[key] = sb.ToString();
                    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    } finally {
                        UnityEngine.Profiling.Profiler.EndSample();
                    }
#endif
                    readSw.Stop();
                    var totalMs = pollSw.Elapsed.TotalMilliseconds + readSw.Elapsed.TotalMilliseconds;
                    LogTiming(
                        "SelectJobReady",
                        $"status={st}, outputs={n}, poll={pollSw.Elapsed.TotalMilliseconds:F3} ms, read={readSw.Elapsed.TotalMilliseconds:F3} ms, total={totalMs:F3} ms",
                        totalMs
                    );
                    selectJobId = 0;
                } else if (st == 3 || st == 4) { // Cancelled / Failed
                    lastSelectJobId = selectJobId;
                    if (NativeMethods.TryGetJobStats(selectJobId, out var stats)) lastSelectJobStats = stats;
                    Debug.LogWarning($"CunningCDAInstance[{name}]: SelectJobEnd status={st}, poll={pollSw.Elapsed.TotalMilliseconds:F3} ms");
                    selectJobId = 0;
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                } finally {
                    UnityEngine.Profiling.Profiler.EndSample();
                }
#endif
            }
        }

        void ClearOutputs() {
            foreach (var kv in outMeshes) kv.Value.LoadFromHandle(0);
            foreach (var kv in outInstancers) kv.Value.LoadFromHandle(0);
            foreach (var kv in outHandles) if (kv.Value != 0) NativeMethods.cunning_release_handle(kv.Value);
            outHandles.Clear();
        }

        string ResolveSelectJobOutputKey(uint outputIndex) {
            if (selectJobOutputKeys != null && outputIndex < (uint)selectJobOutputKeys.Length) {
                var k = selectJobOutputKeys[outputIndex];
                if (!string.IsNullOrEmpty(k)) return k;
            }
            return ResolveSelectedTargetOutputKey(outputIndex);
        }

        static bool TryBuildTargetsJsonAndKeys(List<CdaSelectTarget> targets, out string exportsJson, out List<string> outputKeys) {
            exportsJson = "[]";
            outputKeys = null;
            if (targets == null || targets.Count == 0) return false;

            var sb = new StringBuilder(128);
            var keys = new List<string>(Math.Min(16, targets.Count));
            sb.Append('[');
            bool first = true;
            for (int i = 0; i < targets.Count; i++) {
                var t = targets[i];
                if (t == null) continue;
                var hasNodeId = !string.IsNullOrEmpty(t.node_id);
                var hasNodeName = !string.IsNullOrEmpty(t.node_name);
                if (!hasNodeId && !hasNodeName) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append('{');
                var key = BuildTargetKey(t);
                keys.Add(key);
                if (!string.IsNullOrEmpty(key)) {
                    sb.Append("\"name\":\"").Append(CdaMiniJson.EscapeString(key)).Append("\",");
                }
                sb.Append("\"kind\":\"NodeParam\",");
                if (hasNodeName) {
                    sb.Append("\"node_name\":\"").Append(CdaMiniJson.EscapeString(t.node_name)).Append('"');
                } else if (hasNodeId) {
                    sb.Append("\"node_id\":\"").Append(CdaMiniJson.EscapeString(t.node_id)).Append('"');
                }
                if (!string.IsNullOrEmpty(t.param)) {
                    sb.Append(",\"param\":\"").Append(CdaMiniJson.EscapeString(t.param)).Append('"');
                }
                if (t.channel.HasValue) {
                    sb.Append(",\"channel\":").Append(t.channel.Value);
                }
                sb.Append('}');
            }
            sb.Append(']');

            exportsJson = sb.ToString();
            outputKeys = keys;
            return true;
        }

        static string BuildTargetsJson(List<CdaSelectTarget> targets) {
            if (targets == null || targets.Count == 0) return "[]";
            var sb = new StringBuilder(128);
            sb.Append('[');
            bool first = true;
            for (int i = 0; i < targets.Count; i++) {
                var t = targets[i];
                if (t == null) continue;
                var hasNodeId = !string.IsNullOrEmpty(t.node_id);
                var hasNodeName = !string.IsNullOrEmpty(t.node_name);
                if (!hasNodeId && !hasNodeName) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append('{');
                var key = BuildTargetKey(t);
                if (!string.IsNullOrEmpty(key)) {
                    sb.Append("\"name\":\"").Append(CdaMiniJson.EscapeString(key)).Append("\",");
                }
                sb.Append("\"kind\":\"NodeParam\",");
                if (hasNodeName) {
                    sb.Append("\"node_name\":\"").Append(CdaMiniJson.EscapeString(t.node_name)).Append('"');
                } else if (hasNodeId) {
                    sb.Append("\"node_id\":\"").Append(CdaMiniJson.EscapeString(t.node_id)).Append('"');
                }
                if (!string.IsNullOrEmpty(t.param)) {
                    sb.Append(",\"param\":\"").Append(CdaMiniJson.EscapeString(t.param)).Append('"');
                }
                if (t.channel.HasValue) {
                    sb.Append(",\"channel\":").Append(t.channel.Value);
                }
                sb.Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        public static string BuildTargetKey(CdaSelectTarget t) {
            if (t == null) return "";
            if (!string.IsNullOrWhiteSpace(t.name)) return t.name.Trim();
            if (!string.IsNullOrWhiteSpace(t.node_name) && !string.IsNullOrWhiteSpace(t.param)) return $"{t.node_name.Trim()}.{t.param.Trim()}";
            if (!string.IsNullOrWhiteSpace(t.node_name)) return t.node_name.Trim();
            if (!string.IsNullOrWhiteSpace(t.node_id) && !string.IsNullOrWhiteSpace(t.param)) return $"{t.node_id.Trim()}.{t.param.Trim()}";
            if (!string.IsNullOrWhiteSpace(t.node_id)) return t.node_id.Trim();
            return "";
        }

        public bool ContainsSelectedTarget(string nodeName, string nodeId, string param) {
            if (selectedTargets == null || selectedTargets.Count == 0 || string.IsNullOrWhiteSpace(param)) return false;
            var qNodeName = string.IsNullOrWhiteSpace(nodeName) ? null : nodeName.Trim();
            var qNodeId = string.IsNullOrWhiteSpace(nodeId) ? null : nodeId.Trim();
            var qParam = param.Trim();
            return selectedTargets.Exists(x =>
                x != null &&
                string.Equals((x.param ?? "").Trim(), qParam, StringComparison.Ordinal) &&
                (
                    (!string.IsNullOrWhiteSpace(qNodeName) && string.Equals((x.node_name ?? "").Trim(), qNodeName, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(qNodeId) && string.Equals((x.node_id ?? "").Trim(), qNodeId, StringComparison.OrdinalIgnoreCase))
                ));
        }

        public bool AddSelectedTarget(CdaSelectTarget target, bool dedupeByNodeAndParam = true) {
            if (target == null) return false;
            selectedTargets ??= new List<CdaSelectTarget>();
            target.param = target.param?.Trim();
            target.node_name = target.node_name?.Trim();
            target.node_id = target.node_id?.Trim();
            if (!string.IsNullOrWhiteSpace(target.node_name)) target.node_id = null; // prefer name-based addressing
            if (dedupeByNodeAndParam && ContainsSelectedTarget(target.node_name, target.node_id, target.param)) return false;
            target.kind = "NodeParam";
            if (string.IsNullOrWhiteSpace(target.name)) target.name = BuildTargetKey(target);
            selectedTargets.Add(target);
            return true;
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

        public bool TrySubmitSelectedTargetsJob(out ulong submittedJobId, out string error) {
            submittedJobId = 0;
            error = null;

            if (asset == null) {
                error = "No CDA asset.";
                return false;
            }
            if (selectedTargets == null || selectedTargets.Count == 0) {
                error = "No selected targets.";
                return false;
            }

            SyncAsset();
            EnsureOutputs();

            if (string.IsNullOrEmpty(asset.sourcePath)) {
                error = "asset.sourcePath is empty";
                return false;
            }

            if (cdaId == 0) {
                var abs = GetAssetAbsPath();
                cdaId = NativeMethods.cunning_cda_load(abs);
                if (cdaId == 0) {
                    error = GetLastNativeError("cda load failed");
                    return false;
                }
            }

            bool nativeIsBlackBox = (cdaId != 0 && NativeMethods.cunning_cda_get_access_mode(cdaId) == 1);
            bool hostWantsBlackBox = asset.IsBlackBox();
            bool isBlackBox = nativeIsBlackBox || hostWantsBlackBox;
            if (isBlackBox) {
                error = "CDA is BlackBox; selected targets are not allowed.";
                return false;
            }

            if (!TryBuildTargetsJsonAndKeys(selectedTargets, out var exJson, out var outKeys) || exJson == "[]") {
                error = "No valid selected targets.";
                return false;
            }

            if (selectJobId != 0) {
                NativeMethods.cunning_job_cancel(selectJobId);
                selectJobId = 0;
            }
            selectedTargetOutputsJson.Clear();
            selectJobOutputKeys = outKeys?.ToArray();

            var inputHandles = new List<ulong>();
            foreach (var mb in inputs) inputHandles.Add(GetInputHandleSafe(mb));

            ulong selectInstanceId = instanceId ^ 0x5A5A5A5A5A5A5A5AUL;
            ulong selectGen = (ulong)DateTime.UtcNow.Ticks;
            selectJobId = NativeMethods.cunning_cda_submit_select(
                cdaId,
                selectInstanceId,
                selectGen,
                BuildParamsJson(),
                inputHandles.ToArray(),
                (uint)inputHandles.Count,
                exJson);
            lastSelectJobStatus = 0;
            if (selectJobId == 0) {
                error = GetLastNativeError("cda submit select failed");
                return false;
            }
            submittedJobId = selectJobId;
            return true;
        }

        public bool TrySubmitNodeGeometryJob(string nodeName, out ulong submittedJobId, out string error, string nodeId = null) {
            submittedJobId = 0;
            error = null;

            if (asset == null) {
                error = "No CDA asset.";
                return false;
            }

            SyncAsset();
            EnsureOutputs();

            if (string.IsNullOrEmpty(asset.sourcePath)) {
                error = "asset.sourcePath is empty";
                return false;
            }

            if (cdaId == 0) {
                var abs = GetAssetAbsPath();
                cdaId = NativeMethods.cunning_cda_load(abs);
                if (cdaId == 0) {
                    error = GetLastNativeError("cda load failed");
                    return false;
                }
            }

            bool nativeIsBlackBox = (cdaId != 0 && NativeMethods.cunning_cda_get_access_mode(cdaId) == 1);
            bool hostWantsBlackBox = asset.IsBlackBox();
            bool isBlackBox = nativeIsBlackBox || hostWantsBlackBox;
            if (isBlackBox) {
                error = "CDA is BlackBox; node geometry select is not allowed.";
                return false;
            }

            bool hasNodeName = !string.IsNullOrWhiteSpace(nodeName);
            bool hasNodeId = !string.IsNullOrWhiteSpace(nodeId);
            if (!hasNodeName && !hasNodeId) {
                error = "nodeName/nodeId is empty";
                return false;
            }

            var inputHandles = new List<ulong>();
            foreach (var mb in inputs) inputHandles.Add(GetInputHandleSafe(mb));

            var sb = new StringBuilder(192);
            sb.Append("[{\"kind\":\"NodeOutput\",\"name\":\"__node_geo__\",");
            if (hasNodeName) {
                sb.Append("\"node_name\":\"").Append(CdaMiniJson.EscapeString(nodeName.Trim())).Append("\"");
            } else {
                sb.Append("\"node_id\":\"").Append(CdaMiniJson.EscapeString(nodeId.Trim())).Append("\"");
            }
            sb.Append("}]");
            string exportsJson = sb.ToString();

            ulong debugInstanceId = instanceId ^ 0xA5A5A5A5A5A5A5A5UL;
            ulong debugGen = (ulong)DateTime.UtcNow.Ticks;
            submittedJobId = NativeMethods.cunning_cda_submit_select(
                cdaId,
                debugInstanceId,
                debugGen,
                BuildParamsJson(),
                inputHandles.ToArray(),
                (uint)inputHandles.Count,
                exportsJson);
            if (submittedJobId == 0) {
                error = GetLastNativeError("cda submit select failed");
                return false;
            }
            return true;
        }

        string ResolveSelectedTargetOutputKey(uint outputIndex) {
            if (selectedTargets != null && outputIndex < selectedTargets.Count) {
                var t = selectedTargets[(int)outputIndex];
                if (t != null) {
                    var key = BuildTargetKey(t);
                    if (!string.IsNullOrEmpty(key)) return key;
                }
            }
            return $"param_{outputIndex}";
        }

        public bool TryGetSelectedTargetOutputJson(string key, out string json) {
            json = null;
            if (string.IsNullOrEmpty(key)) return false;
            return selectedTargetOutputsJson != null && selectedTargetOutputsJson.TryGetValue(key, out json);
        }

        public bool TryGetSelectedTargetOutputValue(string key, out CdaParamValue value) {
            value = null;
            if (!TryGetSelectedTargetOutputJson(key, out var json) || string.IsNullOrWhiteSpace(json)) return false;
            value = CdaParamValue.FromDefaultJson(json);
            return value != null;
        }

        public bool TryGetSelectedTargetOutputScalar(string key, out float scalar) {
            scalar = 0f;
            if (!TryGetSelectedTargetOutputValue(key, out var v) || v == null) return false;
            if (string.Equals(v.kind, "Float", StringComparison.OrdinalIgnoreCase)) {
                scalar = v.f;
                return true;
            }
            if (string.Equals(v.kind, "Int", StringComparison.OrdinalIgnoreCase)) {
                scalar = v.i;
                return true;
            }
            if (string.Equals(v.kind, "Bool", StringComparison.OrdinalIgnoreCase)) {
                scalar = v.b ? 1f : 0f;
                return true;
            }
            if (v.a != null && v.a.Length > 0) {
                scalar = v.a[0];
                return true;
            }
            return false;
        }

        void SetCachedNodeGeoEnabledSafe(bool enabled) {
            if (instanceId == 0 || !s_cacheEnableApiAvailable) return;
            try {
                NativeMethods.cunning_cda_set_cached_node_geo_enabled(instanceId, enabled ? 1u : 0u);
            } catch (EntryPointNotFoundException) {
                s_cacheEnableApiAvailable = false;
                if (!s_cacheEnableApiWarned) {
                    s_cacheEnableApiWarned = true;
                    Debug.LogWarning("CunningCDAInstance: Native API missing: cunning_cda_set_cached_node_geo_enabled (rebuild native DLL).");
                }
            }
        }

        void ClearCachedNodeGeoSafe() {
            if (instanceId == 0 || !s_cacheClearApiAvailable) return;
            try {
                NativeMethods.cunning_cda_clear_cached_node_geo(instanceId);
            } catch (EntryPointNotFoundException) {
                s_cacheClearApiAvailable = false;
                if (!s_cacheClearApiWarned) {
                    s_cacheClearApiWarned = true;
                    Debug.LogWarning("CunningCDAInstance: Native API missing: cunning_cda_clear_cached_node_geo (rebuild native DLL).");
                }
            }
        }

        static string GetLastNativeError(string fallback) {
            var sb = new StringBuilder(2048);
            NativeMethods.cunning_cda_get_last_error(sb, (uint)sb.Capacity);
            var msg = sb.ToString();
            return string.IsNullOrWhiteSpace(msg) ? fallback : msg;
        }
    }
}
