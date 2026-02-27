using System;
using System.Diagnostics;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CunningEngine.Editor {
    public sealed class CunningSyncWindow : EditorWindow {
        [MenuItem("Procedural/Cunning Engine/Sync...", false, 105)]
        static void Open() { GetWindow<CunningSyncWindow>("Cunning Sync"); }

        void OnGUI() {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Sync", EditorStyles.boldLabel);
            CunningSyncState.SyncCamera = EditorGUILayout.ToggleLeft("Sync Camera", CunningSyncState.SyncCamera);
            EditorGUILayout.Space(4);

            using (new EditorGUI.DisabledScope(CunningSyncState.Enabled)) {
                if (GUILayout.Button("Sync", GUILayout.Height(28))) StartSync();
            }
            using (new EditorGUI.DisabledScope(!CunningSyncState.Enabled)) {
                if (GUILayout.Button("Disconnect", GUILayout.Height(28))) StopSync();
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Enabled", CunningSyncState.Enabled ? "Yes" : "No");
            EditorGUILayout.LabelField("DB", string.IsNullOrEmpty(CunningSyncState.DbPath) ? "(none)" : CunningSyncState.DbPath);
            EditorGUILayout.LabelField("Cunning3D PID", CunningSyncState.Cunning3DPid > 0 ? CunningSyncState.Cunning3DPid.ToString() : "(none)");
        }

        static void StartSync() {
            try {
                var dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Library", "CunningEngine", "Sync"));
                System.IO.Directory.CreateDirectory(dir);
                var name = "sync_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".redb";
                var dbPath = System.IO.Path.Combine(dir, name);

                if (!CunningSyncNative.OpenDb(dbPath, true)) { UnityEngine.Debug.LogWarning($"Cunning Sync: open db failed\n  err={CunningSyncNative.LastError}\n  dll={CunningSyncNative.LoadedPath}"); return; }

                if (!CunningEngineSettings.EnsureCunning3DConfigured()) { UnityEngine.Debug.LogWarning("Cunning Sync: Cunning3D not configured."); return; }

                var exe = CunningEngineSettings.Cunning3DExePath;
                var args = new StringBuilder(256).Append("--bridge-db ").Append('"').Append(dbPath.Replace("\"", "")).Append('"');
                var p = Process.Start(new ProcessStartInfo { FileName = exe, Arguments = args.ToString(), UseShellExecute = true });

                CunningSyncState.DbPath = dbPath;
                CunningSyncState.Cunning3DPid = p != null ? p.Id : 0;
                CunningSyncState.Enabled = true;
                UnityEngine.Debug.Log($"Cunning Sync: started (db={dbPath})");
            } catch (Exception e) { UnityEngine.Debug.LogWarning($"Cunning Sync: start failed: {e.Message}"); }
        }

        static void StopSync() {
            CunningSyncState.Enabled = false;
            try {
                var pid = CunningSyncState.Cunning3DPid;
                if (pid > 0) {
                    try { var p = Process.GetProcessById(pid); if (p != null && !p.HasExited) p.Kill(); }
                    catch { }
                }
                CunningSyncState.Cunning3DPid = 0;
            } catch { }

            try {
                var db = CunningSyncState.DbPath;
                CunningSyncState.DbPath = "";
                if (!string.IsNullOrEmpty(db) && System.IO.File.Exists(db)) System.IO.File.Delete(db);
            } catch { }
        }
    }
}

