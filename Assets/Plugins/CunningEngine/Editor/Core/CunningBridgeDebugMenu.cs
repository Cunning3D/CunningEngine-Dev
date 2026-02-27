using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using CunningEngine.CDA;

namespace CunningEngine.Editor {
    public static class CunningBridgeDebugMenu {
        const string MENU_ROOT = "Procedural/Cunning Engine/Debug/";
        const string MENU_INPUT = MENU_ROOT + "Input/";

        [MenuItem(MENU_ROOT + "Bridge Selected", false, 200)]
        static void BridgeSelected() { Bridge(GetSelectedInstances(), true); }

        [MenuItem(MENU_ROOT + "Bridge All", false, 201)]
        static void BridgeAll() { Bridge(FindAllInstances(), true); }

        [MenuItem(MENU_ROOT + "Save C3D File To...", false, 210)]
        static void SaveC3dTo() { Bridge(GetSelectedInstances(), false); }

        [MenuItem(MENU_INPUT + "Create Test Inputs (Spline+Mesh)", false, 10)]
        static void CreateTestInputs() {
            var root = new GameObject("__CunningBridgeTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Cunning Bridge Test");

            // Spline input
            var goSpline = new GameObject("SplineInput");
            goSpline.transform.SetParent(root.transform, false);
            var sc = goSpline.GetComponent<UnityEngine.Splines.SplineContainer>() ?? goSpline.AddComponent<UnityEngine.Splines.SplineContainer>();
            if (sc.Splines.Count == 0) {
                var s = new UnityEngine.Splines.Spline();
                s.Add(new UnityEngine.Splines.BezierKnot(new Vector3(0, 0, 0)));
                s.Add(new UnityEngine.Splines.BezierKnot(new Vector3(2, 0, 0)));
                sc.Splines.Add(s);
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
            Debug.Log("Cunning Bridge: created __CunningBridgeTest with CunningInputSpline + CunningInputMesh. Assign these into your CDA instance inputs, then run Bridge Selected.");
        }

        [MenuItem(MENU_INPUT + "Create Test CDA Instance (uses selected CDAAssetObject)", false, 11)]
        static void CreateTestCdaInstance() {
            var a = Selection.activeObject as CDAAssetObject;
            if (a == null) { Debug.LogWarning("Cunning Bridge: select a CDAAssetObject in Project window first."); return; }
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
            Debug.Log("Cunning Bridge: created test CunningCDAInstance and auto-bound inputs[0]=Spline, inputs[1]=Mesh. Now run Bridge Selected.");
        }

        [MenuItem(MENU_INPUT + "Create Empty Bridge CDA (drag Spline/Mesh into inputs)", false, 12)]
        static void CreateEmptyBridgeCda() {
            // Creates a transient CDAAssetObject with GAME-style sourceJson only (no file path).
            // Cunning3D side will reconstruct a minimal in-memory CDA from this json for bridging.
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
            Debug.Log("Cunning Bridge: created EmptyBridgeCDAInstance.\n- Drag a GameObject with SplineContainer into inputs[0] (it will auto-add CunningInputSpline)\n- Drag a GameObject with CunningMesh into inputs[1]\nThen run Debug/Bridge Selected.");
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
            sb.Append("\"exports_mode\":\"BlackBox\",\"exports\":[]");
            sb.Append('}');
            return sb.ToString();
        }

        static List<CunningCDAInstance> GetSelectedInstances() {
            var r = new List<CunningCDAInstance>();
            foreach (var go in Selection.gameObjects) if (go) r.AddRange(go.GetComponentsInChildren<CunningCDAInstance>(true));
            return r;
        }

        static List<CunningCDAInstance> FindAllInstances() {
            var r = new List<CunningCDAInstance>();
            r.AddRange(UnityEngine.Object.FindObjectsOfType<CunningCDAInstance>(true));
            return r;
        }

        static void Bridge(List<CunningCDAInstance> xs, bool ephemeral) {
            if (xs == null || xs.Count == 0) { UnityEngine.Debug.LogWarning("Cunning Bridge: no CDA instances found."); return; }
            CunningBridge.Instance.Init();
            var path = ephemeral ? MakeTempC3dPath() : PickC3dSavePath();
            if (string.IsNullOrEmpty(path)) return;
            var dbPath = MakeTempDbPath(path);
            if (!CunningBridge.Instance.OpenDb(dbPath, true)) UnityEngine.Debug.LogWarning("Cunning Bridge: failed to open bridge db (viewport sync disabled).");
            var records = new List<string>();
            foreach (var x in xs) if (x && x.asset) records.Add(BuildBridgeRecordJson(x));
            if (records.Count == 0) { UnityEngine.Debug.LogWarning("Cunning Bridge: nothing to write."); return; }
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            System.IO.File.WriteAllText(path, BuildBridgeFileJson(records), Encoding.UTF8);
            UnityEngine.Debug.Log($"Cunning Bridge: wrote {records.Count} record(s) to {path}");
            if (ephemeral && !CunningEngineSettings.AutoLaunchCunning3D) return;
            if (!CunningEngineSettings.EnsureCunning3DConfigured()) return;
            LaunchCunning3D(path, dbPath, ephemeral);
        }

        static string PickC3dSavePath() {
            var picked = EditorUtility.SaveFilePanel("Save Cunning3D Project", Application.dataPath, "bridge.c3d", "c3d");
            return string.IsNullOrEmpty(picked) ? null : picked;
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

        static string BuildBridgeRecordJson(CunningCDAInstance t) {
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
                var kind = mb is CunningInputSpline ? "spline" : (mb is CunningInputMesh ? "mesh" : "unknown");
                var h = mb is ICunningInputHandle ih ? ih.CurrentHandle : 0UL;
                var d = h != 0 ? NativeMethods.cunning_geo_get_dirty_id(h) : 0UL;
                uint basis = mb is CunningInputSpline sp ? (uint)sp.sourceBasis : 1u;
                string blobKey = "";
                ulong blobId = 0;
                if (mb is CunningInputSpline) {
                    try {
                        var sc = mb.GetComponent<UnityEngine.Splines.SplineContainer>();
                        if (sc) {
                            var fbs = CunningSplineSnapshotFbs.BuildFbs(sc);
                            if (fbs != null && fbs.Length > 0) {
                                blobKey = $"spline.{t.instanceId}.{i}";
                                var keyUtf8 = Encoding.UTF8.GetBytes(blobKey);
                                var gch = GCHandle.Alloc(fbs, GCHandleType.Pinned);
                                var gchKey = GCHandle.Alloc(keyUtf8, GCHandleType.Pinned);
                                try {
                                    blobId = NativeMethods.cunning_state_put_latest(keyUtf8, (uint)keyUtf8.Length, gch.AddrOfPinnedObject(), (uint)fbs.Length);
                                } finally {
                                    gch.Free();
                                    gchKey.Free();
                                }
                            }
                        }
                    } catch (Exception e) { UnityEngine.Debug.LogWarning($"Cunning Bridge: spline snapshot failed: {e.Message}"); }
                }
                sb.Append('{');
                sb.Append("\"kind\":\"").Append(kind).Append("\",");
                sb.Append("\"handle\":").Append(h).Append(',');
                sb.Append("\"dirty\":").Append(d);
                sb.Append(",\"source_basis\":").Append(basis);
                sb.Append(",\"blob_key\":\"").Append(CdaMiniJson.EscapeString(blobKey)).Append('"');
                sb.Append(",\"blob_id\":").Append(blobId);
                sb.Append('}');
            }
            sb.Append(']');
            sb.Append('}');
            return sb.ToString();
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
            sb.Append("\"version\":\"bridge-1\",");
            sb.Append("\"created_at\":\"").Append(DateTime.UtcNow.ToString("O")).Append("\",");
            sb.Append("\"source\":\"unity\"");
            sb.Append("},");
            sb.Append("\"bridge_records\":[");
            for (int i = 0; i < recordJsonObjects.Count; i++) { if (i != 0) sb.Append(','); sb.Append(recordJsonObjects[i]); }
            sb.Append("]}");
            return sb.ToString();
        }

        static string MakeTempC3dPath() {
            var dir = System.IO.Path.Combine(Application.dataPath, "..", "Library", "CunningEngine", "Bridge");
            var name = "bridge_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".c3d";
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, name));
        }

        static void LaunchCunning3D(string c3dPath, string dbPath, bool ephemeral) {
            try {
                var exe = CunningEngineSettings.Cunning3DExePath;
                var args = new StringBuilder(256);
                args.Append("--bridge ").Append('"').Append(c3dPath.Replace("\"", "")).Append('"');
                if (!string.IsNullOrEmpty(dbPath)) args.Append(" --bridge-db ").Append('"').Append(dbPath.Replace("\"", "")).Append('"');
                if (ephemeral) args.Append(" --ephemeral");
                var wd = GuessCunning3DWorkingDir(exe);
                UnityEngine.Debug.Log($"Cunning Bridge: launch\n  exe={exe}\n  wd={wd}\n  args={args}");
                Process.Start(new ProcessStartInfo { FileName = exe, Arguments = args.ToString(), UseShellExecute = true, WorkingDirectory = wd });
            } catch (Exception e) { UnityEngine.Debug.LogWarning($"Cunning Bridge: launch failed: {e.Message}"); }
        }

        static string GuessCunning3DWorkingDir(string exePath) {
            try {
                var repo = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "Cunning3D_1.0"));
                if (System.IO.Directory.Exists(System.IO.Path.Combine(repo, "assets"))) return repo;
            } catch { }
            try { return System.IO.Path.GetDirectoryName(exePath); } catch { return null; }
        }

        static string MakeTempDbPath(string c3dPath) {
            try {
                var dir = System.IO.Path.GetDirectoryName(c3dPath);
                var name = System.IO.Path.GetFileNameWithoutExtension(c3dPath) + ".redb";
                return System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, name));
            } catch { return null; }
        }
    }
}

