using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CunningEngine.Editor.Coverlay {
    /// <summary>Launches the official Cunning Player (WASM) to view CDA coverlays inside Unity workflow.</summary>
    public sealed class CunningCoverlayPlayerWindow : EditorWindow {
        static CunningLocalHttpServer s_server;
        static string s_lastError;
        static string s_lastUrl;
        static string s_lastCdaRel = "Procedural Project/Assets/Voxel Editor.cda";

        [MenuItem("Procedural/Cunning Engine/Coverlay Player", false, 40)]
        public static void Open() {
            var w = GetWindow<CunningCoverlayPlayerWindow>("Cunning Coverlay Player");
            w.minSize = new Vector2(420, 180);
            w.Show();
        }

        void OnEnable() {
            EnsureServer();
        }

        void OnDisable() { }

        static string WorkspaceRoot() {
            // Assets -> project -> workspace (Procedural Project sibling to Cunning3D_1.0)
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        }

        static void EnsureServer() {
            if (s_server != null && s_server.Running) return;
            try {
                s_lastError = null;
                s_server?.Dispose();
                s_server = new CunningLocalHttpServer(WorkspaceRoot());
                s_server.Start(0);
            } catch (Exception e) {
                s_lastError = e.GetType().Name + ": " + e.Message;
                try { s_server?.Dispose(); } catch { }
                s_server = null;
            }
        }

        static string EscapePathForQuery(string relPath) {
            relPath = (relPath ?? "").Replace('\\', '/').TrimStart('/');
            var parts = relPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++) parts[i] = Uri.EscapeDataString(parts[i]);
            return "/" + string.Join("/", parts);
        }

        static string BuildPlayerUrl(string cdaRelPath) {
            EnsureServer();
            if (s_server == null || !s_server.Running) return null;
            var player = "/Cunning3D_1.0/crates/cunning_player/web_output/index.html";
            var cdaQ = EscapePathForQuery(cdaRelPath);
            return $"http://127.0.0.1:{s_server.Port}{player}?cda={Uri.EscapeDataString(cdaQ)}";
        }

        static void OpenInBrowser(string cdaRel) {
            var url = BuildPlayerUrl(cdaRel);
            if (string.IsNullOrEmpty(url)) return;
            s_lastUrl = url;
            Application.OpenURL(url);
        }

        void OnGUI() {
            EditorGUILayout.LabelField("This window launches the official Cunning Player (WASM) to view CDA coverlays.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(6);

            if (!string.IsNullOrEmpty(s_lastError)) {
                EditorGUILayout.HelpBox("Failed to start local server: " + s_lastError, MessageType.Error);
            } else if (s_server != null && s_server.Running) {
                EditorGUILayout.LabelField("Local Server", $"http://127.0.0.1:{s_server.Port}/");
            } else {
                EditorGUILayout.HelpBox("Local server is not running.", MessageType.Warning);
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("CDA (workspace-relative path)");
            s_lastCdaRel = EditorGUILayout.TextField(s_lastCdaRel);

            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Open Coverlay (Browser)", GUILayout.Height(28))) OpenInBrowser(s_lastCdaRel);
                if (GUILayout.Button("Restart Server", GUILayout.Height(28))) { try { s_server?.Dispose(); } catch { } s_server = null; EnsureServer(); }
            }

            var so = Selection.activeObject;
            var ap = so != null ? AssetDatabase.GetAssetPath(so) : null;
            if (!string.IsNullOrEmpty(ap) && ap.EndsWith(".cda", StringComparison.OrdinalIgnoreCase)) {
                var abs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ap));
                var root = WorkspaceRoot().Replace('\\', '/');
                var rel = abs.Replace('\\', '/');
                if (rel.StartsWith(root, StringComparison.OrdinalIgnoreCase)) rel = rel.Substring(root.Length).TrimStart('/');
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Selected .cda", ap);
                if (GUILayout.Button("Open Selected CDA", GUILayout.Height(24))) OpenInBrowser(rel);
            }

            if (!string.IsNullOrEmpty(s_lastUrl)) {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Last URL", s_lastUrl, EditorStyles.wordWrappedMiniLabel);
            }
        }
    }
}

