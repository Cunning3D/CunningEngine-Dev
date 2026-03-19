using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Net.Sockets;
using System.Text;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using CunningEngine.CDA;

namespace CunningEngine.Editor {
    public static class CunningBridgeDebugMenu {
        const string MENU_ROOT = "Procedural/Cunning Engine/Debug/";
        const string MENU_INPUT = MENU_ROOT + "Input/";
        static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        const int BridgeIpcSessionProbeAttempts = 30;
        const int BridgeIpcSessionProbeDelayMs = 150;
        const int BridgeIpcSendAttempts = 4;
        const int BridgeIpcSendDelayMs = 120;

        [Serializable]
        sealed class BridgeIpcSession {
            public int version;
            public int pid;
            public int port;
            public string token;
        }

        sealed class BridgeSharedMemoryLease : IDisposable {
            public string name;
            public int byteLength;
            public DateTime createdUtc;
            public MemoryMappedFile mapping;

            public void Dispose() {
                try { mapping?.Dispose(); } catch { }
                mapping = null;
            }
        }

        sealed class BridgeSharedMemoryBlobBuilder {
            readonly List<byte[]> _chunks = new List<byte[]>();
            int _byteLength;

            public int ByteLength => _byteLength;

            public int Append(byte[] bytes) {
                if (bytes == null || bytes.Length == 0) return -1;
                var alignedOffset = Align8(_byteLength);
                var padding = alignedOffset - _byteLength;
                if (padding > 0) {
                    _chunks.Add(new byte[padding]);
                    _byteLength += padding;
                }
                _chunks.Add(bytes);
                var offset = _byteLength;
                _byteLength += bytes.Length;
                return offset;
            }

            public void CopyTo(byte[] destination, int destinationOffset) {
                var cursor = destinationOffset;
                for (int i = 0; i < _chunks.Count; i++) {
                    var chunk = _chunks[i];
                    Buffer.BlockCopy(chunk, 0, destination, cursor, chunk.Length);
                    cursor += chunk.Length;
                }
            }
        }

        static readonly Dictionary<string, BridgeSharedMemoryLease> SharedBridgeLeases = new Dictionary<string, BridgeSharedMemoryLease>();
        static readonly TimeSpan SharedBridgeLeaseLifetime = TimeSpan.FromMinutes(2);
        static readonly byte[] SharedMemorySessionMagic = new byte[] { (byte)'C', (byte)'3', (byte)'D', (byte)'S', (byte)'H', (byte)'M', (byte)'1', 0 };
        static readonly bool DetailedTimingLogs = true;
        const double DetailedTimingLogMinMs = 0.25;

        sealed class BridgeSharedMemoryBuildStats {
            public int recordCount;
            public int inputCount;
            public int snapshotCount;
            public long snapshotBytes;
            public int jsonBytes;
            public int blobBytes;
            public int payloadBytes;
        }

        sealed class BridgeSelectionPayload {
            public readonly List<CunningCDAInstance> cdaInstances = new List<CunningCDAInstance>();
            public readonly List<MonoBehaviour> standaloneInputs = new List<MonoBehaviour>();

            public int Count => cdaInstances.Count + standaloneInputs.Count;
        }

        sealed class BridgeInputRecordData {
            public string kind;
            public uint sourceBasis;
            public string sourceName;
            public string materialPath;
            public string snapshotBase64;
            public int snapshotShmOffset = -1;
            public int snapshotShmSize;
            public ulong sourceInstanceId;
            public string sourceOutputName;
            public ulong handle;
            public float? cubeSize;
            public int? cubeDiv;
            public float? sphereRadius;
            public int? sphereRings;
            public int? sphereSegments;
        }

        static bool ShouldLogTiming(double elapsedMs) => DetailedTimingLogs && elapsedMs >= DetailedTimingLogMinMs;

        static void LogTiming(string stage, double elapsedMs, string extra = null) {
            if (!ShouldLogTiming(elapsedMs)) return;
            if (string.IsNullOrWhiteSpace(extra)) UnityEngine.Debug.Log($"Cunning Bridge Timing: {stage}, total={elapsedMs:F3} ms");
            else UnityEngine.Debug.Log($"Cunning Bridge Timing: {stage}, total={elapsedMs:F3} ms, {extra}");
        }

        [MenuItem(MENU_ROOT + "Open Selected in Cunning3D", false, 190)]
        static void OpenSelectedInCunning3D() { BridgeSelection_SharedMemory(GetSelectedBridgeSelection(), true); }

        [MenuItem(MENU_INPUT + "Create Test Inputs (Spline+Mesh)", false, 10)]
        static void CreateTestInputs() {
            var root = new GameObject("__CunningBridgeTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Cunning Bridge Test");

            // Spline input
            var goSpline = new GameObject("SplineInput");
            goSpline.transform.SetParent(root.transform, false);
            var sc = goSpline.GetComponent<UnityEngine.Splines.SplineContainer>() ?? goSpline.AddComponent<UnityEngine.Splines.SplineContainer>();
            if (sc.Splines.Count > 0 && sc.Splines[0].Count == 0) {
                var s = sc.Splines[0];
                s.Add(new UnityEngine.Splines.BezierKnot(new Vector3(0, 0, 0)));
                s.Add(new UnityEngine.Splines.BezierKnot(new Vector3(2, 0, 0)));
            } else if (sc.Splines.Count == 0) {
                // Newer Splines API exposes Splines as IReadOnlyList; keep test object valid even without direct list mutation.
                // If container starts empty, we leave it empty instead of forcing a list write.
            }
            var inSpline = goSpline.AddComponent<CunningInputSpline>();

            // Mesh input: create native cube so it has a real handle.
            var goMesh = new GameObject("MeshInput");
            Undo.RegisterCreatedObjectUndo(goMesh, "Create Mesh Input");
            goMesh.transform.SetParent(root.transform, false);
            goMesh.transform.localPosition = new Vector3(0, 0, 2);
            goMesh.AddComponent<MeshFilter>();
            goMesh.AddComponent<MeshRenderer>();
            var cm = goMesh.AddComponent<CunningMesh>();
            var h = NativeMethods.cunning_geo_create_cube(1f, 1, 1, 1);
            cm.LoadFromHandle(h);
            var inMesh = goMesh.AddComponent<CunningInputMesh>();

            Selection.activeGameObject = root;
            UnityEngine.Debug.Log("Cunning Bridge: created __CunningBridgeTest with CunningInputSpline + CunningInputMesh. Assign these into your CDA instance inputs, then run Open Selected in Cunning3D.");
        }

        [MenuItem(MENU_INPUT + "Create Test CDA Instance (uses selected CDAAssetObject)", false, 11)]
        static void CreateTestCdaInstance() {
            var a = Selection.activeObject as CDAAssetObject;
            if (a == null) { UnityEngine.Debug.LogWarning("Cunning Bridge: select a CDAAssetObject in Project window first."); return; }
            var root = GameObject.Find("__CunningBridgeTest") ?? new GameObject("__CunningBridgeTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Cunning Bridge Test");
            var go = new GameObject("CDAInstance");
            go.transform.SetParent(root.transform, false);
            Undo.RegisterCreatedObjectUndo(go, "Create CDA Instance");
            var inst = go.AddComponent<CunningCDAInstance>();
            inst.asset = a;
            if (inst.instanceId == 0) inst.instanceId = (ulong)UnityEngine.Random.Range(int.MinValue, int.MaxValue) ^ ((ulong)DateTime.UtcNow.Ticks << 1);
            // Best-effort auto-bind from children
            var spline = root.GetComponentInChildren<CunningInputSpline>(true);
            var mesh = root.GetComponentInChildren<CunningInputMesh>(true);
            if (inst.inputs == null) inst.inputs = new List<MonoBehaviour>();
            while (inst.inputs.Count < (a.inputs != null ? a.inputs.Count : 0)) inst.inputs.Add(null);
            if (inst.inputs.Count > 0) inst.inputs[0] = spline;
            if (inst.inputs.Count > 1) inst.inputs[1] = mesh;
            Selection.activeGameObject = go;
            UnityEngine.Debug.Log("Cunning Bridge: created test CunningCDAInstance and auto-bound inputs[0]=Spline, inputs[1]=Mesh. Now run Open Selected in Cunning3D.");
        }

        [MenuItem(MENU_INPUT + "Create Empty Bridge CDA (drag Spline/Mesh into inputs)", false, 12)]
        static void CreateEmptyBridgeCda() {
            // Creates a transient CDAAssetObject with GAME-style sourceJson only (no file path).
            // Cunning3D side will reconstruct a full in-memory CDA asset from this runtime json.
            var so = ScriptableObject.CreateInstance<CDAAssetObject>();
            so.name = "UnityEmptyBridgeCDA";
            so.sourcePath = ""; // important: forces bridge to use sourceJson

            // Default ports: 2 inputs (spline+mesh), 0 outputs (bridge test focuses on inputs).
            so.inputs = new List<CdaPort> {
                new CdaPort { name = "input_0", data_type = "Geometry" },
                new CdaPort { name = "input_1", data_type = "Geometry" },
            };
            so.outputs = new List<CdaPort>();
            so.promoted_params = new List<CdaPromotedParam>();
            so.sourceJson = BuildRuntimeDefJson(so.name, so.inputs, so.outputs);

            var root = GameObject.Find("__CunningBridgeTest") ?? new GameObject("__CunningBridgeTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Cunning Bridge Test");
            var go = new GameObject("EmptyBridgeCDAInstance");
            go.transform.SetParent(root.transform, false);
            Undo.RegisterCreatedObjectUndo(go, "Create Empty Bridge CDA Instance");
            var inst = go.AddComponent<CunningCDAInstance>();
            inst.asset = so;
            if (inst.instanceId == 0) inst.instanceId = (ulong)UnityEngine.Random.Range(int.MinValue, int.MaxValue) ^ ((ulong)DateTime.UtcNow.Ticks << 1);
            if (inst.inputs == null) inst.inputs = new List<MonoBehaviour>();
            while (inst.inputs.Count < so.inputs.Count) inst.inputs.Add(null);

            Selection.activeGameObject = go;
            UnityEngine.Debug.Log("Cunning Bridge: created EmptyBridgeCDAInstance.\n- Drag a GameObject with SplineContainer into inputs[0] (it will auto-add CunningInputSpline)\n- Drag a GameObject with CunningMesh into inputs[1]\nThen run Debug/Open Selected in Cunning3D.");
        }

        static string BuildRuntimeDefJson(string name, List<CdaPort> ins, List<CdaPort> outs) {
            // Minimal RuntimeDefinition JSON (GAME chunk schema).
            // We intentionally omit nodes/connections to keep this an 'empty CDA' for bridge testing.
            var uuid = Guid.NewGuid().ToString();
            var sb = new StringBuilder(512);
            sb.Append('{');
            sb.Append("\"meta\":{");
            sb.Append("\"format_version\":1,");
            sb.Append("\"min_engine_version\":\"0.1.0\",");
            sb.Append("\"uuid\":\"").Append(uuid).Append("\",");
            sb.Append("\"name\":\"").Append(CdaMiniJson.EscapeString(name ?? "CDA")).Append("\",");
            sb.Append("\"author\":null,");
            sb.Append("\"license\":null");
            sb.Append("},");
            sb.Append("\"inputs\":[");
            for (int i = 0; i < (ins != null ? ins.Count : 0); i++) {
                if (i != 0) sb.Append(',');
                sb.Append('{');
                sb.Append("\"id\":\"").Append(Guid.NewGuid().ToString()).Append("\",");
                sb.Append("\"name\":\"").Append(CdaMiniJson.EscapeString(ins[i].name ?? $"input_{i}")).Append("\",");
                sb.Append("\"data_type\":\"").Append(CdaMiniJson.EscapeString(ins[i].data_type ?? "Geometry")).Append("\"");
                sb.Append('}');
            }
            sb.Append("],");
            sb.Append("\"outputs\":[");
            for (int i = 0; i < (outs != null ? outs.Count : 0); i++) {
                if (i != 0) sb.Append(',');
                sb.Append('{');
                sb.Append("\"id\":\"").Append(Guid.NewGuid().ToString()).Append("\",");
                sb.Append("\"name\":\"").Append(CdaMiniJson.EscapeString(outs[i].name ?? $"output_{i}")).Append("\",");
                sb.Append("\"data_type\":\"").Append(CdaMiniJson.EscapeString(outs[i].data_type ?? "Geometry")).Append("\"");
                sb.Append('}');
            }
            sb.Append("],");
            sb.Append("\"nodes\":[],\"connections\":[],\"promoted_params\":[],\"hud_units\":[],\"coverlay_units\":[],");
            sb.Append("\"access_mode\":\"WhiteBox\",\"exports\":[]");
            sb.Append('}');
            return sb.ToString();
        }

        static BridgeSelectionPayload GetSelectedBridgeSelection() {
            var selection = new BridgeSelectionPayload();
            var selectedCdaIds = new HashSet<int>();
            var standaloneInputIds = new HashSet<int>();
            var selectedObjects = Selection.objects ?? Array.Empty<UnityEngine.Object>();

            for (int i = 0; i < selectedObjects.Length; i++) {
                var selectedObject = selectedObjects[i];
                if (TryGetDirectSelectedCdaInstance(selectedObject, out var instance)) {
                    if (selectedCdaIds.Add(instance.GetInstanceID())) {
                        selection.cdaInstances.Add(instance);
                    }
                    continue;
                }

                var resolved = TryResolveSelectedStandaloneInput(selectedObject);
                if (resolved) {
                    if (standaloneInputIds.Add(resolved.GetInstanceID())) {
                        selection.standaloneInputs.Add(resolved);
                    }
                    continue;
                }

                if (TryGetSelectedCdaContainer(selectedObject, out instance)) {
                    if (selectedCdaIds.Add(instance.GetInstanceID())) {
                        selection.cdaInstances.Add(instance);
                    }
                }
            }

            return selection;
        }

        static bool TryGetDirectSelectedCdaInstance(UnityEngine.Object selectedObject, out CunningCDAInstance instance) {
            instance = null;
            if (!selectedObject) return false;

            if (selectedObject is CunningCDAInstance directInstance) {
                instance = directInstance;
                return instance != null;
            }

            return false;
        }

        static MonoBehaviour TryResolveSelectedStandaloneInput(UnityEngine.Object selectedObject) {
            if (!selectedObject) return null;

            if (selectedObject is MonoBehaviour monoBehaviour && monoBehaviour is ICunningInputHandle) {
                return monoBehaviour;
            }

            return CunningInputUtility.Resolve(selectedObject);
        }

        static bool TryGetSelectedCdaContainer(UnityEngine.Object selectedObject, out CunningCDAInstance instance) {
            instance = null;
            if (!selectedObject) return false;

            GameObject go = selectedObject as GameObject;
            if (!go && selectedObject is Component component) {
                go = component.gameObject;
            }
            if (!go) return false;

            instance = go.GetComponent<CunningCDAInstance>();
            return instance != null;
        }

        static bool TryBuildBridgeSharedMemoryPayload(BridgeSelectionPayload selection, out byte[] payloadBytes, out BridgeSharedMemoryBuildStats stats) {
            payloadBytes = null;
            stats = null;
            if (selection == null || selection.Count == 0) return false;

            var totalSw = Stopwatch.StartNew();
            var records = new List<string>();
            var blobs = new BridgeSharedMemoryBlobBuilder();
            stats = new BridgeSharedMemoryBuildStats();
            for (int i = 0; i < selection.cdaInstances.Count; i++) {
                var x = selection.cdaInstances[i];
                if (!x || !x.asset) continue;
                var recordSw = Stopwatch.StartNew();
                records.Add(BuildBridgeRecordJson(x, blobs, stats));
                recordSw.Stop();
                LogTiming(
                    "build-record",
                    recordSw.Elapsed.TotalMilliseconds,
                    $"index={records.Count - 1}, asset={(x.asset ? x.asset.name : "<null>")}, inputs={(x.inputs != null ? x.inputs.Count : 0)}, instance_id={x.instanceId}"
                );
            }
            for (int i = 0; i < selection.standaloneInputs.Count; i++) {
                var input = selection.standaloneInputs[i];
                if (!input) continue;
                var recordSw = Stopwatch.StartNew();
                if (TryBuildStandaloneInputBridgeRecordJson(input, blobs, stats, out var recordJson)) {
                    records.Add(recordJson);
                }
                recordSw.Stop();
                LogTiming(
                    "build-record",
                    recordSw.Elapsed.TotalMilliseconds,
                    $"index={records.Count - 1}, standalone={(input ? input.name : "<null>")}"
                );
            }
            if (records.Count == 0) return false;

            var jsonSw = Stopwatch.StartNew();
            var bridgeJson = BuildBridgeFileJson(records);
            var jsonBytes = Utf8NoBom.GetBytes(bridgeJson);
            jsonSw.Stop();
            var headerSize = 16;
            payloadBytes = new byte[headerSize + jsonBytes.Length + blobs.ByteLength];
            Buffer.BlockCopy(SharedMemorySessionMagic, 0, payloadBytes, 0, SharedMemorySessionMagic.Length);
            Buffer.BlockCopy(BitConverter.GetBytes(1), 0, payloadBytes, 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(jsonBytes.Length), 0, payloadBytes, 12, 4);
            var packSw = Stopwatch.StartNew();
            Buffer.BlockCopy(jsonBytes, 0, payloadBytes, headerSize, jsonBytes.Length);
            blobs.CopyTo(payloadBytes, headerSize + jsonBytes.Length);
            packSw.Stop();
            totalSw.Stop();

            stats.recordCount = records.Count;
            stats.jsonBytes = jsonBytes.Length;
            stats.blobBytes = blobs.ByteLength;
            stats.payloadBytes = payloadBytes.Length;

            LogTiming("build-json", jsonSw.Elapsed.TotalMilliseconds, $"records={records.Count}, json_bytes={jsonBytes.Length}");
            LogTiming("pack-payload", packSw.Elapsed.TotalMilliseconds, $"json_bytes={jsonBytes.Length}, blob_bytes={blobs.ByteLength}, payload_bytes={payloadBytes.Length}");
            LogTiming(
                "build-shm-payload",
                totalSw.Elapsed.TotalMilliseconds,
                $"records={stats.recordCount}, inputs={stats.inputCount}, snapshots={stats.snapshotCount}, snapshot_bytes={stats.snapshotBytes}, json_bytes={stats.jsonBytes}, blob_bytes={stats.blobBytes}, payload_bytes={stats.payloadBytes}"
            );
            return true;
        }

        static void BridgeSelection_SharedMemory(BridgeSelectionPayload selection, bool launch) {
            if (selection == null || selection.Count == 0) {
                UnityEngine.Debug.LogWarning("Cunning Bridge: no supported Cunning selection found.");
                return;
            }
            CunningBridge.Instance.Init();
            var totalSw = Stopwatch.StartNew();
            var buildSw = Stopwatch.StartNew();
            if (!TryBuildBridgeSharedMemoryPayload(selection, out var payloadBytes, out var buildStats)) { UnityEngine.Debug.LogWarning("Cunning Bridge: nothing to write."); return; }
            buildSw.Stop();

            var writeSw = Stopwatch.StartNew();
            if (!TryWriteBridgeSharedMemory(payloadBytes, out var shmName, out var shmSize)) return;
            writeSw.Stop();

            UnityEngine.Debug.Log($"Cunning Bridge: wrote {buildStats.recordCount} record(s) to shared memory {shmName} ({shmSize} bytes)");
            LogTiming(
                "bridge-open-current-shm.build+write",
                buildSw.Elapsed.TotalMilliseconds + writeSw.Elapsed.TotalMilliseconds,
                $"records={buildStats.recordCount}, inputs={buildStats.inputCount}, payload_bytes={shmSize}, build={buildSw.Elapsed.TotalMilliseconds:F3} ms, write={writeSw.Elapsed.TotalMilliseconds:F3} ms"
            );

            if (!launch) return;
            if (!CunningEngineSettings.EnsureCunning3DConfigured()) return;
            var sendSw = Stopwatch.StartNew();
            if (TrySendOpenBridgeSharedMemoryToRunningInstance(shmName, shmSize)) {
                sendSw.Stop();
                totalSw.Stop();
                UnityEngine.Debug.Log("Cunning Bridge: sent shared memory session to running Cunning3D instance.");
                LogTiming("bridge-open-current-shm.total", totalSw.Elapsed.TotalMilliseconds, $"mode=ipc, payload_bytes={shmSize}, send={sendSw.Elapsed.TotalMilliseconds:F3} ms");
                return;
            }
            sendSw.Stop();
            LogTiming("bridge-open-current-shm.ipc-fallback", sendSw.Elapsed.TotalMilliseconds, $"payload_bytes={shmSize}, running={IsCunning3DRunning()}");

            if (IsCunning3DRunning()) {
                var ok = EditorUtility.DisplayDialog(
                    "Cunning3D Already Running",
                    "Cunning3D appears to already be running.\n\nThe running instance did not accept the shared memory open request.\n\nLaunch another instance?",
                    "Launch Another",
                    "Cancel"
                );
                if (!ok) return;
            }

            LaunchCunning3D_SharedMemory(shmName, shmSize);
            totalSw.Stop();
            LogTiming("bridge-open-current-shm.total", totalSw.Elapsed.TotalMilliseconds, $"mode=launch, payload_bytes={shmSize}");
        }

        static string BuildParamsJson(CunningCDAInstance t) {
            if (t.paramValues == null) t.paramValues = new Dictionary<string, CdaParamValue>();
            if (t.asset != null && t.asset.promoted_params != null) foreach (var p in t.asset.promoted_params) if (!t.paramValues.ContainsKey(p.name)) t.paramValues[p.name] = CdaParamValue.FromDefaultJson(p.default_value_json);
            var sb = new StringBuilder(256);
            sb.Append('{');
            var first = true;
            foreach (var kv in t.paramValues) {
                if (kv.Value == null) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(CdaMiniJson.EscapeString(kv.Key)).Append('"').Append(':');
                kv.Value.WriteSerdeJson(sb);
            }
            sb.Append('}');
            return sb.ToString();
        }

        static string BuildBridgeRecordJson(CunningCDAInstance t, BridgeSharedMemoryBlobBuilder sharedMemoryBlobs = null, BridgeSharedMemoryBuildStats stats = null) {
            var asset = t.asset;
            var absPath = GetAssetAbsPath(asset);
            var paramsJson = BuildParamsJson(t);
            var inputs = t.inputs ?? new List<MonoBehaviour>();
            var sb = new StringBuilder(1024);
            sb.Append('{');
            sb.Append("\"type\":\"CdaBridgeRecord\",");
            sb.Append("\"time_utc\":\"").Append(DateTime.UtcNow.ToString("O")).Append("\",");
            sb.Append("\"instance_id\":").Append(t.instanceId).Append(',');
            sb.Append("\"asset_name\":\"").Append(CdaMiniJson.EscapeString(asset.name ?? "")).Append("\",");
            sb.Append("\"asset_source_path\":\"").Append(CdaMiniJson.EscapeString(absPath ?? "")).Append("\",");
            sb.Append("\"asset_source_json\":\"").Append(CdaMiniJson.EscapeString(asset.sourceJson ?? "")).Append("\",");
            sb.Append("\"params_json\":").Append(paramsJson).Append(',');
            sb.Append("\"inputs\":[");
            for (int i = 0; i < inputs.Count; i++) {
                if (i != 0) sb.Append(',');
                var mb = inputs[i];
                var liveInput = mb ? mb : null;
                var inputData = CaptureBridgeInputRecordData(
                    liveInput,
                    sharedMemoryBlobs,
                    stats,
                    $"asset={(asset ? asset.name : "<null>")}, slot={i}");
                WriteBridgeInputJson(sb, inputData, true);
            }
            sb.Append(']');
            sb.Append('}');
            return sb.ToString();
        }

        static bool TryBuildStandaloneInputBridgeRecordJson(
            MonoBehaviour liveInput,
            BridgeSharedMemoryBlobBuilder sharedMemoryBlobs,
            BridgeSharedMemoryBuildStats stats,
            out string recordJson)
        {
            recordJson = null;
            var inputData = CaptureBridgeInputRecordData(
                liveInput,
                sharedMemoryBlobs,
                stats,
                $"standalone={(liveInput ? liveInput.name : "<null>")}");
            if (!CanBridgeStandaloneInput(inputData)) {
                return false;
            }

            var sb = new StringBuilder(512);
            sb.Append('{');
            sb.Append("\"type\":\"InputBridgeRecord\",");
            sb.Append("\"time_utc\":\"").Append(DateTime.UtcNow.ToString("O")).Append("\",");
            WriteBridgeInputJsonFields(sb, inputData, false);
            sb.Append('}');
            recordJson = sb.ToString();
            return true;
        }

        static BridgeInputRecordData CaptureBridgeInputRecordData(
            MonoBehaviour liveInput,
            BridgeSharedMemoryBlobBuilder sharedMemoryBlobs,
            BridgeSharedMemoryBuildStats stats,
            string logContext)
        {
            var inputSw = Stopwatch.StartNew();
            var data = new BridgeInputRecordData {
                kind = "unknown",
                sourceName = GetInputSourceNameSafe(liveInput),
                materialPath = GetInputMaterialPathSafe(liveInput),
                snapshotBase64 = "",
                sourceOutputName = "",
                handle = GetInputHandleSafe(liveInput),
            };

            var spline = liveInput as CunningInputSpline;
            var demoShape = liveInput ? liveInput.GetComponent<CunningDemoShape>() : null;
            data.kind =
                spline ? "spline" :
                demoShape && demoShape.kind == CunningDemoShape.ShapeKind.Cube ? "cunning_cube" :
                demoShape && demoShape.kind == CunningDemoShape.ShapeKind.Sphere ? "cunning_sphere" :
                (liveInput is ICunningInputHandle ? "mesh" : "unknown");
            data.sourceBasis = spline ? (uint)spline.sourceBasis : 0u;
            TryGetInputSourceCdaOutputSafe(liveInput, out data.sourceInstanceId, out data.sourceOutputName);

            if (demoShape && data.kind == "cunning_cube") {
                data.cubeSize = demoShape.cubeSize;
                data.cubeDiv = demoShape.cubeDiv;
            } else if (demoShape && data.kind == "cunning_sphere") {
                data.sphereRadius = demoShape.sphereRadius;
                data.sphereRings = demoShape.sphereRings;
                data.sphereSegments = demoShape.sphereSegments;
            }

            if (spline) {
                try {
                    var sc = spline.GetComponent<UnityEngine.Splines.SplineContainer>();
                    if (sc) {
                        var fbs = CunningSplineSnapshotFbs.BuildFbs(sc);
                        if (fbs != null && fbs.Length > 0) {
                            if (sharedMemoryBlobs != null) {
                                data.snapshotShmOffset = sharedMemoryBlobs.Append(fbs);
                                data.snapshotShmSize = fbs.Length;
                            } else {
                                data.snapshotBase64 = Convert.ToBase64String(fbs);
                            }
                        }
                    }
                } catch (Exception e) {
                    UnityEngine.Debug.LogWarning($"Cunning Bridge: spline snapshot failed: {e.Message}");
                }
            } else if (data.kind == "mesh") {
                try {
                    if (data.handle != 0) {
                        ulong blobId;
                        try {
                            blobId = NativeMethods.cunning_geo_snapshot_bin_zstd_to_blob(data.handle, 1);
                        } catch (EntryPointNotFoundException) {
                            blobId = NativeMethods.cunning_geo_snapshot_json_to_blob(data.handle);
                        }
                        if (TryGetBlobBytes(blobId, out var blobBytes) && blobBytes != null && blobBytes.Length > 0) {
                            if (sharedMemoryBlobs != null) {
                                data.snapshotShmOffset = sharedMemoryBlobs.Append(blobBytes);
                                data.snapshotShmSize = blobBytes.Length;
                            } else {
                                data.snapshotBase64 = Convert.ToBase64String(blobBytes);
                            }
                        }
                    }
                } catch (Exception e) {
                    UnityEngine.Debug.LogWarning($"Cunning Bridge: mesh snapshot failed: {e.Message}");
                }
            }

            inputSw.Stop();
            if (stats != null) {
                stats.inputCount++;
                if (data.snapshotShmSize > 0 || !string.IsNullOrEmpty(data.snapshotBase64)) {
                    stats.snapshotCount++;
                    stats.snapshotBytes += data.snapshotShmSize > 0 ? data.snapshotShmSize : data.snapshotBase64.Length;
                }
            }
            if (DetailedTimingLogs && (data.snapshotShmSize > 0 || !string.IsNullOrEmpty(data.snapshotBase64) || ShouldLogTiming(inputSw.Elapsed.TotalMilliseconds))) {
                var snapshotBytes = data.snapshotShmSize > 0 ? data.snapshotShmSize : (!string.IsNullOrEmpty(data.snapshotBase64) ? data.snapshotBase64.Length : 0);
                UnityEngine.Debug.Log(
                    $"Cunning Bridge Timing: build-input, total={inputSw.Elapsed.TotalMilliseconds:F3} ms, " +
                    $"{logContext}, kind={data.kind}, source={data.sourceName}, snapshot_bytes={snapshotBytes}, " +
                    $"shm={(data.snapshotShmSize > 0 ? 1 : 0)}, handle={data.handle}, source_instance_id={data.sourceInstanceId}"
                );
            }

            return data;
        }

        static bool CanBridgeStandaloneInput(BridgeInputRecordData inputData) {
            if (inputData == null) return false;
            switch (inputData.kind) {
                case "spline":
                case "mesh":
                case "cunning_cube":
                case "cunning_sphere":
                    break;
                default:
                    return false;
            }

            if (inputData.kind == "spline" || inputData.kind == "mesh") {
                return inputData.snapshotShmSize > 0 || !string.IsNullOrEmpty(inputData.snapshotBase64);
            }

            return true;
        }

        static void WriteBridgeInputJson(StringBuilder sb, BridgeInputRecordData inputData, bool includeSourceLink) {
            if (sb == null || inputData == null) return;
            sb.Append('{');
            WriteBridgeInputJsonFields(sb, inputData, includeSourceLink);
            sb.Append('}');
        }

        static void WriteBridgeInputJsonFields(StringBuilder sb, BridgeInputRecordData inputData, bool includeSourceLink) {
            if (sb == null || inputData == null) return;
            sb.Append("\"kind\":\"").Append(inputData.kind).Append("\",");
            sb.Append("\"source_basis\":").Append(inputData.sourceBasis);
            sb.Append(",\"source_name\":\"").Append(CdaMiniJson.EscapeString(inputData.sourceName ?? "")).Append('"');
            sb.Append(",\"material_path\":\"").Append(CdaMiniJson.EscapeString(inputData.materialPath ?? "")).Append('"');
            sb.Append(",\"snapshot_base64\":\"").Append(CdaMiniJson.EscapeString(inputData.snapshotBase64 ?? "")).Append('"');
            if (inputData.snapshotShmOffset >= 0 && inputData.snapshotShmSize > 0) {
                sb.Append(",\"snapshot_shm_offset\":").Append(inputData.snapshotShmOffset);
                sb.Append(",\"snapshot_shm_size\":").Append(inputData.snapshotShmSize);
            }
            if (includeSourceLink && inputData.sourceInstanceId != 0UL) sb.Append(",\"source_instance_id\":").Append(inputData.sourceInstanceId);
            if (includeSourceLink && !string.IsNullOrWhiteSpace(inputData.sourceOutputName)) {
                sb.Append(",\"source_output_name\":\"").Append(CdaMiniJson.EscapeString(inputData.sourceOutputName)).Append('"');
            }
            if (inputData.kind == "cunning_cube" && inputData.cubeSize.HasValue) {
                sb.Append(",\"cube_size\":").Append(CdaMiniJson.FloatToJson(inputData.cubeSize.Value));
                if (inputData.cubeDiv.HasValue) sb.Append(",\"cube_div\":").Append(inputData.cubeDiv.Value);
            } else if (inputData.kind == "cunning_sphere" && inputData.sphereRadius.HasValue) {
                sb.Append(",\"sphere_radius\":").Append(CdaMiniJson.FloatToJson(inputData.sphereRadius.Value));
                if (inputData.sphereRings.HasValue) sb.Append(",\"sphere_rings\":").Append(inputData.sphereRings.Value);
                if (inputData.sphereSegments.HasValue) sb.Append(",\"sphere_segments\":").Append(inputData.sphereSegments.Value);
            }
        }

        static ulong GetInputHandleSafe(MonoBehaviour mb) {
            if (!mb) return 0;
            try {
                return mb is ICunningInputHandle ih ? ih.CurrentHandle : 0UL;
            } catch (MissingReferenceException) {
                return 0;
            } catch (NullReferenceException) {
                return 0;
            }
        }

        static string GetInputSourceNameSafe(MonoBehaviour mb) {
            if (!mb) return "";
            try {
                return mb.gameObject ? (mb.gameObject.name ?? "") : "";
            } catch (MissingReferenceException) {
                return "";
            } catch (NullReferenceException) {
                return "";
            }
        }

        static string GetInputMaterialPathSafe(MonoBehaviour mb) {
            if (!mb) return "";
            try {
                var renderer = mb.GetComponent<MeshRenderer>();
                if (!renderer) return "";
                var material = renderer.sharedMaterial;
                if (!material) return "";
                return AssetDatabase.GetAssetPath(material) ?? "";
            } catch (MissingReferenceException) {
                return "";
            } catch (NullReferenceException) {
                return "";
            }
        }

        static bool TryGetInputSourceCdaOutputSafe(MonoBehaviour mb, out ulong sourceInstanceId, out string sourceOutputName) {
            sourceInstanceId = 0UL;
            sourceOutputName = "";
            if (!mb) return false;
            try {
                var mesh = mb as CunningMesh;
                if (!mesh) return false;
                var tr = mesh.transform;
                if (!tr) return false;
                var parent = tr.parent;
                if (!parent) return false;
                var owner = parent.GetComponent<CunningCDAInstance>();
                if (!owner) return false;
                sourceInstanceId = owner.instanceId;
                sourceOutputName = GetCdaOutputKeySafe(owner, mesh);
                return sourceInstanceId != 0UL;
            } catch (MissingReferenceException) {
                sourceInstanceId = 0UL;
                sourceOutputName = "";
                return false;
            } catch (NullReferenceException) {
                sourceInstanceId = 0UL;
                sourceOutputName = "";
                return false;
            }
        }

        static string GetCdaOutputKeySafe(CunningCDAInstance owner, CunningMesh mesh) {
            if (!mesh) return "";
            try {
                var go = mesh.gameObject;
                var rawName = go ? (go.name ?? "") : "";
                if (rawName.StartsWith("out_", StringComparison.Ordinal)) return rawName.Substring(4);
                if (owner != null && owner.asset != null && owner.asset.outputs != null) {
                    for (int i = 0; i < owner.asset.outputs.Count; i++) {
                        var output = owner.asset.outputs[i];
                        var key = !string.IsNullOrEmpty(output.name) ? output.name : $"output_{i}";
                        if (string.Equals(rawName, key, StringComparison.Ordinal)) return key;
                    }
                    if (owner.asset.outputs.Count == 1) {
                        var only = owner.asset.outputs[0];
                        return !string.IsNullOrEmpty(only.name) ? only.name : "output_0";
                    }
                }
                return rawName;
            } catch (MissingReferenceException) {
                return "";
            } catch (NullReferenceException) {
                return "";
            }
        }

        static bool TryGetBlobBytes(ulong blobId, out byte[] bytes) {
            bytes = null;
            if (blobId == 0) return false;
            try {
                var len = NativeMethods.cunning_bridge_get_blob_size(blobId);
                if (len == 0) return false;
                bytes = new byte[(int)len];
                var gch = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                try {
                    var copied = NativeMethods.cunning_bridge_copy_blob(blobId, gch.AddrOfPinnedObject(), (uint)bytes.Length);
                    return copied == (uint)bytes.Length;
                } finally {
                    gch.Free();
                }
            } catch {
                bytes = null;
                return false;
            }
        }

        static string GetAssetAbsPath(CDAAssetObject asset) {
            if (asset == null || string.IsNullOrEmpty(asset.sourcePath)) return "";
            var p = asset.sourcePath.Replace('\\', '/');
            if (p.StartsWith("Assets/") || p == "Assets") return System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", p));
            return System.IO.Path.GetFullPath(p);
        }

        static string BuildBridgeFileJson(List<string> recordJsonObjects) {
            var sb = new StringBuilder(1024 + recordJsonObjects.Count * 512);
            sb.Append('{');
            sb.Append("\"header\":{");
            sb.Append("\"version\":\"bridge-2\",");
            sb.Append("\"created_at\":\"").Append(DateTime.UtcNow.ToString("O")).Append("\",");
            sb.Append("\"source\":\"unity\"");
            sb.Append("},");
            sb.Append("\"bridge_records\":[");
            for (int i = 0; i < recordJsonObjects.Count; i++) { if (i != 0) sb.Append(','); sb.Append(recordJsonObjects[i]); }
            sb.Append("]}");
            return sb.ToString();
        }

        static int Align8(int value) {
            if (value <= 0) return 0;
            var rem = value & 7;
            return rem == 0 ? value : (value + (8 - rem));
        }

        static bool IsCunning3DRunning() {
            try {
                var exe = CunningEngineSettings.Cunning3DExePath;
                if (string.IsNullOrEmpty(exe)) return false;
                var procName = System.IO.Path.GetFileNameWithoutExtension(exe);
                if (string.IsNullOrEmpty(procName)) return false;
                return System.Diagnostics.Process.GetProcessesByName(procName).Length > 0;
            } catch {
                return false;
            }
        }

        static string GetBridgeIpcSessionPath() {
            return Path.Combine(Path.GetTempPath(), "cunning3d_bridge_ipc.json");
        }

        static bool TrySendOpenBridgeSharedMemoryToRunningInstance(string shmName, int shmSize) {
            try {
                if (!TryReadLiveBridgeIpcSession(out var session, true)) return false;
                var payload = BuildBridgeIpcOpenSharedMemoryJson(session.token, shmName, shmSize);
                return TrySendBridgePayloadToRunningInstance(session, payload, "open_session_shm");
            } catch (Exception e) {
                UnityEngine.Debug.Log($"Cunning Bridge: running-instance shared-memory open failed: {e.Message}");
                return false;
            }
        }

        static bool TryReadLiveBridgeIpcSession(out BridgeIpcSession session, bool waitForReady) {
            session = null;
            var sessionPath = GetBridgeIpcSessionPath();
            var attempts = waitForReady ? BridgeIpcSessionProbeAttempts : 1;
            var sawRunning = false;
            for (int attempt = 0; attempt < attempts; attempt++) {
                sawRunning = sawRunning || IsCunning3DRunning();
                if (TryReadBridgeIpcSessionFile(sessionPath, out session)) return true;
                if (!waitForReady) break;
                if (!sawRunning && !File.Exists(sessionPath)) break;
                System.Threading.Thread.Sleep(BridgeIpcSessionProbeDelayMs);
            }
            session = null;
            return false;
        }

        static bool TryReadBridgeIpcSessionFile(string sessionPath, out BridgeIpcSession session) {
            session = null;
            try {
                if (string.IsNullOrEmpty(sessionPath) || !File.Exists(sessionPath)) return false;
                var json = File.ReadAllText(sessionPath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) {
                    TryDeleteBridgeIpcSessionFile(sessionPath);
                    return false;
                }
                session = JsonUtility.FromJson<BridgeIpcSession>(json);
                if (!IsBridgeIpcSessionStructValid(session)) {
                    TryDeleteBridgeIpcSessionFile(sessionPath);
                    session = null;
                    return false;
                }
                if (!IsBridgeIpcSessionProcessAlive(session)) {
                    UnityEngine.Debug.Log($"Cunning Bridge: stale IPC session file removed (pid={session.pid}, port={session.port}).");
                    TryDeleteBridgeIpcSessionFile(sessionPath);
                    session = null;
                    return false;
                }
                return true;
            } catch (Exception e) {
                UnityEngine.Debug.Log($"Cunning Bridge: IPC session read failed: {e.Message}");
                session = null;
                return false;
            }
        }

        static bool IsBridgeIpcSessionStructValid(BridgeIpcSession session) {
            return session != null && session.version == 1 && session.pid > 0 && session.port > 0 && !string.IsNullOrEmpty(session.token);
        }

        static bool IsBridgeIpcSessionProcessAlive(BridgeIpcSession session) {
            if (!IsBridgeIpcSessionStructValid(session)) return false;
            try {
                var process = System.Diagnostics.Process.GetProcessById(session.pid);
                if (process == null || process.HasExited) return false;
                var exe = CunningEngineSettings.Cunning3DExePath;
                var expected = string.IsNullOrEmpty(exe) ? "" : Path.GetFileNameWithoutExtension(exe);
                if (string.IsNullOrEmpty(expected)) return true;
                return string.Equals(process.ProcessName, expected, StringComparison.OrdinalIgnoreCase);
            } catch {
                return false;
            }
        }

        static void TryDeleteBridgeIpcSessionFile(string sessionPath) {
            try {
                if (!string.IsNullOrEmpty(sessionPath) && File.Exists(sessionPath)) File.Delete(sessionPath);
            } catch { }
        }

        static bool TrySendBridgePayloadToRunningInstance(BridgeIpcSession session, string payload, string commandName) {
            var sw = Stopwatch.StartNew();
            var payloadBytes = Encoding.UTF8.GetBytes(payload ?? "");
            Exception lastError = null;
            try {
                for (int attempt = 0; attempt < BridgeIpcSendAttempts; attempt++) {
                    try {
                        using (var client = new TcpClient()) {
                            client.NoDelay = true;
                            client.SendTimeout = 1500;
                            client.ReceiveTimeout = 1500;
                            client.Connect("127.0.0.1", session.port);
                            using (var stream = client.GetStream()) {
                                stream.Write(payloadBytes, 0, payloadBytes.Length);
                                stream.Flush();
                                try { client.Client.Shutdown(SocketShutdown.Send); } catch { }
                                var ackBuffer = new byte[16];
                                var ackLength = stream.Read(ackBuffer, 0, ackBuffer.Length);
                                var ack = ackLength > 0 ? Encoding.UTF8.GetString(ackBuffer, 0, ackLength).Trim() : "";
                                if (!string.Equals(ack, "ok", StringComparison.OrdinalIgnoreCase)) throw new IOException($"unexpected ack '{ack}'");
                            }
                        }
                        sw.Stop();
                        LogTiming("ipc-send", sw.Elapsed.TotalMilliseconds, $"command={commandName}, port={session.port}, payload_bytes={payloadBytes.Length}");
                        return true;
                    } catch (Exception ex) {
                        lastError = ex;
                        if (attempt + 1 < BridgeIpcSendAttempts) System.Threading.Thread.Sleep(BridgeIpcSendDelayMs);
                    }
                }
                sw.Stop();
                LogTiming("ipc-send-failed", sw.Elapsed.TotalMilliseconds, $"command={commandName}, port={session.port}, payload_bytes={payloadBytes.Length}");
                UnityEngine.Debug.Log($"Cunning Bridge: running-instance payload send failed: {(lastError != null ? lastError.Message : "unknown error")}");
                return false;
            } catch (Exception e) {
                sw.Stop();
                LogTiming("ipc-send-failed", sw.Elapsed.TotalMilliseconds, $"command={commandName}, port={session.port}, payload_bytes={payloadBytes.Length}");
                UnityEngine.Debug.Log($"Cunning Bridge: running-instance payload send failed: {e.Message}");
                return false;
            }
        }

        static string BuildBridgeIpcOpenSharedMemoryJson(string token, string shmName, int shmSize) {
            var sb = new StringBuilder(512);
            sb.Append('{');
            sb.Append("\"version\":1,");
            sb.Append("\"token\":\"").Append(CdaMiniJson.EscapeString(token ?? "")).Append("\",");
            sb.Append("\"command\":\"open_session_shm\",");
            sb.Append("\"bridge_shm_name\":\"").Append(CdaMiniJson.EscapeString(shmName ?? "")).Append("\",");
            sb.Append("\"bridge_shm_size\":").Append(Math.Max(0, shmSize));
            sb.Append('}');
            return sb.ToString();
        }

        static void LaunchCunning3D_SharedMemory(string shmName, int shmSize) {
            try {
                var exe = CunningEngineSettings.Cunning3DExePath;
                var args = new StringBuilder(256);
                args.Append("--bridge-shm ").Append('"').Append((shmName ?? "").Replace("\"", "")).Append('"');
                args.Append(" --bridge-shm-size ").Append(Math.Max(0, shmSize));
                var wd = GuessCunning3DWorkingDir(exe);
                UnityEngine.Debug.Log($"Cunning Bridge: launch (shared memory)\n  exe={exe}\n  wd={wd}\n  args={args}");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = exe, Arguments = args.ToString(), UseShellExecute = true, WorkingDirectory = wd });
            } catch (Exception e) { UnityEngine.Debug.LogWarning($"Cunning Bridge: shared-memory launch failed: {e.Message}"); }
        }

        static bool TryWriteBridgeSharedMemory(byte[] payloadBytes, out string shmName, out int shmSize) {
            shmName = null;
            shmSize = 0;
            if (payloadBytes == null || payloadBytes.Length == 0) {
                UnityEngine.Debug.LogWarning("Cunning Bridge: shared memory write skipped because payload is empty.");
                return false;
            }

            CleanupExpiredSharedBridgeLeases();
            var sw = Stopwatch.StartNew();
            try {
                shmSize = payloadBytes.Length;
                shmName = "Local\\cunning3d_bridge_" + Guid.NewGuid().ToString("N");
                var mapping = MemoryMappedFile.CreateOrOpen(shmName, shmSize, MemoryMappedFileAccess.ReadWrite);
                using (var view = mapping.CreateViewAccessor(0, shmSize, MemoryMappedFileAccess.Write)) {
                    view.WriteArray(0, payloadBytes, 0, payloadBytes.Length);
                    view.Flush();
                }

                if (SharedBridgeLeases.TryGetValue(shmName, out var oldLease) && oldLease != null) oldLease.Dispose();
                SharedBridgeLeases[shmName] = new BridgeSharedMemoryLease {
                    name = shmName,
                    byteLength = shmSize,
                    createdUtc = DateTime.UtcNow,
                    mapping = mapping
                };
                sw.Stop();
                LogTiming("write-shm", sw.Elapsed.TotalMilliseconds, $"name={shmName}, payload_bytes={shmSize}");
                return true;
            } catch (Exception e) {
                sw.Stop();
                LogTiming("write-shm-failed", sw.Elapsed.TotalMilliseconds, $"payload_bytes={(payloadBytes != null ? payloadBytes.Length : 0)}");
                UnityEngine.Debug.LogWarning($"Cunning Bridge: shared memory write failed: {e.Message}");
                shmName = null;
                shmSize = 0;
                return false;
            }
        }

        static void CleanupExpiredSharedBridgeLeases() {
            if (SharedBridgeLeases.Count == 0) return;
            var now = DateTime.UtcNow;
            var expired = new List<string>();
            foreach (var kv in SharedBridgeLeases) {
                var lease = kv.Value;
                if (lease == null || now - lease.createdUtc > SharedBridgeLeaseLifetime) expired.Add(kv.Key);
            }
            for (int i = 0; i < expired.Count; i++) {
                var key = expired[i];
                if (!SharedBridgeLeases.TryGetValue(key, out var lease)) continue;
                SharedBridgeLeases.Remove(key);
                try { lease?.Dispose(); } catch { }
            }
        }

        static string GuessCunning3DWorkingDir(string exePath) {
            try {
                var repo = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "Cunning3D_1.0"));
                if (System.IO.Directory.Exists(System.IO.Path.Combine(repo, "assets"))) return repo;
            } catch { }
            try { return System.IO.Path.GetDirectoryName(exePath); } catch { return null; }
        }
    }
}
