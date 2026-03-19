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
            EditorGUILayout.LabelField("Session", string.IsNullOrEmpty(CunningSyncState.SessionName) ? "(none)" : CunningSyncState.SessionName);
            EditorGUILayout.LabelField("Unity -> C3D", string.IsNullOrEmpty(CunningSyncState.UnityToC3DMapName) ? "(none)" : CunningSyncState.UnityToC3DMapName);
            EditorGUILayout.LabelField("C3D -> Unity", string.IsNullOrEmpty(CunningSyncState.C3DToUnityMapName) ? "(none)" : CunningSyncState.C3DToUnityMapName);
            EditorGUILayout.LabelField("Cunning3D PID", CunningSyncState.Cunning3DPid > 0 ? CunningSyncState.Cunning3DPid.ToString() : "(none)");
        }

        static void StartSync() {
            try {
                if (!CunningEngineSettings.EnsureCunning3DConfigured()) { UnityEngine.Debug.LogWarning("Cunning Sync: Cunning3D not configured."); return; }

                var sessionName = "Local\\cunning3d_sync_" + Guid.NewGuid().ToString("N");
                var unityToC3DMapName = sessionName + ".viewport.unity";
                var c3dToUnityMapName = sessionName + ".viewport.c3d";
                CunningSyncSharedMemory.Open(unityToC3DMapName, c3dToUnityMapName);

                var exe = CunningEngineSettings.Cunning3DExePath;
                var args = new StringBuilder(256)
                    .Append("--sync-shm-unity ").Append('"').Append(unityToC3DMapName.Replace("\"", "")).Append('"')
                    .Append(" --sync-shm-c3d ").Append('"').Append(c3dToUnityMapName.Replace("\"", "")).Append('"');
                var wd = GuessCunning3DWorkingDir(exe);
                var p = Process.Start(new ProcessStartInfo { FileName = exe, Arguments = args.ToString(), UseShellExecute = true, WorkingDirectory = wd });

                CunningSyncState.SessionName = sessionName;
                CunningSyncState.UnityToC3DMapName = unityToC3DMapName;
                CunningSyncState.C3DToUnityMapName = c3dToUnityMapName;
                CunningSyncState.Cunning3DPid = p != null ? p.Id : 0;
                CunningSyncState.Enabled = true;
                CunningSyncState.ClearLegacyDbState();
                UnityEngine.Debug.Log($"Cunning Sync: started (shm)\n  session={sessionName}\n  unity_to_c3d={unityToC3DMapName}\n  c3d_to_unity={c3dToUnityMapName}");
            } catch (Exception e) {
                CunningSyncSharedMemory.Close();
                CunningSyncState.Enabled = false;
                CunningSyncState.ClearConnectionState();
                UnityEngine.Debug.LogWarning($"Cunning Sync: start failed: {e.Message}");
            }
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

            CunningSyncSharedMemory.Close();
            CunningSyncState.ClearConnectionState();
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

