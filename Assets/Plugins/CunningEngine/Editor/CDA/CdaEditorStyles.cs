using UnityEngine;
using UnityEditor;

namespace CunningEngine.Editor.CDA {
    // Unified styling for CDA Inspector UI
    public static class CdaEditorStyles {
        static GUIStyle _headerTitle, _sectionHeader, _statusReady, _statusError, _statusCooking, _foldoutBold, _brandTitle;
        static Texture2D _logo, _headerBg, _sectionBg, _brandBg;
        static bool _initialized;

        public static Texture2D Logo { get { Init(); return _logo; } }
        public static Texture2D HeaderBg { get { Init(); return _headerBg; } }
        public static Texture2D SectionBg { get { Init(); return _sectionBg; } }
        public static Texture2D BrandBg { get { Init(); return _brandBg; } }

        public static GUIStyle HeaderTitle { get { Init(); return _headerTitle; } }
        public static GUIStyle SectionHeader { get { Init(); return _sectionHeader; } }
        public static GUIStyle StatusReady { get { Init(); return _statusReady; } }
        public static GUIStyle StatusError { get { Init(); return _statusError; } }
        public static GUIStyle StatusCooking { get { Init(); return _statusCooking; } }
        public static GUIStyle FoldoutBold { get { Init(); return _foldoutBold; } }
        public static GUIStyle BrandTitle { get { Init(); return _brandTitle; } }

        static void Init() {
            if (_initialized) return;
            _initialized = true;

            _logo = Resources.Load<Texture2D>("CunningLogo");
            _headerBg = MakeTex(2, 2, new Color(0.15f, 0.18f, 0.22f));
            _sectionBg = MakeTex(2, 2, new Color(0.22f, 0.24f, 0.28f));
            _brandBg = MakeTex(2, 2, new Color(0.08f, 0.10f, 0.14f));

            _headerTitle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, normal = { textColor = new Color(0.9f, 0.95f, 1f) }, alignment = TextAnchor.MiddleLeft };
            _sectionHeader = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.7f, 0.85f, 1f) } };
            _foldoutBold = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
            _brandTitle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.4f, 0.85f, 1f) }, alignment = TextAnchor.MiddleCenter };

            _statusReady = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.4f, 0.9f, 0.5f) } };
            _statusError = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(1f, 0.4f, 0.4f) } };
            _statusCooking = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(1f, 0.8f, 0.3f) } };
        }

        static Texture2D MakeTex(int w, int h, Color c) {
            var pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++) pix[i] = c;
            var t = new Texture2D(w, h); t.SetPixels(pix); t.Apply(); return t;
        }

        public static void DrawHeader(string name, string path, string uuid) {
            // Brand title
            EditorGUILayout.BeginHorizontal(new GUIStyle { normal = { background = BrandBg }, padding = new RectOffset(4, 4, 14, 14) });
            GUILayout.FlexibleSpace();
            GUILayout.Label("CUNNING ENGINE", BrandTitle);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            // Node info
            EditorGUILayout.BeginHorizontal(new GUIStyle { normal = { background = HeaderBg }, padding = new RectOffset(8, 8, 6, 6) });
            if (Logo) GUILayout.Label(Logo, GUILayout.Width(32), GUILayout.Height(32));
            EditorGUILayout.BeginVertical();
            GUILayout.Label(name ?? "Untitled CDA", HeaderTitle);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(TruncatePath(path, 45), EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
            if (!string.IsNullOrEmpty(path) && GUILayout.Button("Open", EditorStyles.miniButton, GUILayout.Width(40))) Application.OpenURL("file://" + path);
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(uuid)) GUILayout.Label("UUID: " + uuid, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        static string TruncatePath(string p, int max) {
            if (string.IsNullOrEmpty(p) || p.Length <= max) return p ?? "";
            return "..." + p.Substring(p.Length - max + 3);
        }

        public static bool DrawSection(string title, int count, ref bool foldout) {
            EditorGUILayout.BeginHorizontal(new GUIStyle { normal = { background = SectionBg }, padding = new RectOffset(4, 4, 2, 2) });
            foldout = EditorGUILayout.Foldout(foldout, title + (count >= 0 ? $" ({count})" : ""), true, FoldoutBold);
            EditorGUILayout.EndHorizontal();
            return foldout;
        }
    }
}
