using UnityEditor;
using UnityEngine;

namespace CunningEngine.Editor.Demos {
    [InitializeOnLoad]
    static class CunningSceneHUD {
        const float W = 150f, H = 150f, P = 8f, BH = 22f;
        const string KX = "CunningEngine.SceneHUD.X";
        const string KY = "CunningEngine.SceneHUD.Y";
        const string KEnabled = "CunningEngine.SceneHUD.Enabled";
        static bool dragging;
        static Vector2 dragOff;
        static readonly GUIStyle box = new GUIStyle("HelpBox") { padding = new RectOffset(8, 8, 8, 8) };

        static CunningSceneHUD() => SceneView.duringSceneGui += OnSceneGUI;

        [MenuItem("Cunning/Scene HUD (Fallback)", false, 20)]
        static void ToggleHud() { EditorPrefs.SetBool(KEnabled, !EditorPrefs.GetBool(KEnabled, false)); SceneView.RepaintAll(); }

        static void OnSceneGUI(SceneView sv) {
            if (sv == null) return;
            if (!EditorPrefs.GetBool(KEnabled, false)) return;
            if (!CunningSceneViewModeState.HasSelectedCunningMesh()) return;

            Handles.BeginGUI();
            var x = EditorPrefs.GetFloat(KX, sv.position.width - W - 14f);
            var y = EditorPrefs.GetFloat(KY, 10f);
            var r = new Rect(x, y, W, H);
            r.x = Mathf.Clamp(r.x, 4f, Mathf.Max(4f, sv.position.width - r.width - 4f));
            r.y = Mathf.Clamp(r.y, 4f, Mathf.Max(4f, sv.position.height - r.height - 4f));
            GUI.Box(r, GUIContent.none, box);
            var px = r.x + P; var py = r.y + P; var w = r.width - P * 2;
            var e = Event.current;

            // Drag header
            var hdr = new Rect(px, py, w, BH);
            if (e.type == EventType.MouseDown && e.button == 0 && hdr.Contains(e.mousePosition)) { dragging = true; dragOff = e.mousePosition - new Vector2(r.x, r.y); e.Use(); }
            if (e.type == EventType.MouseUp && e.button == 0) dragging = false;
            if (dragging && e.type == EventType.MouseDrag) {
                var np = e.mousePosition - dragOff;
                EditorPrefs.SetFloat(KX, np.x);
                EditorPrefs.SetFloat(KY, np.y);
                sv.Repaint();
                e.Use();
            }

            var en = GUI.Toggle(hdr, CunningSceneViewModeState.Enabled, "Cunning", "Button");
            if (en != CunningSceneViewModeState.Enabled) CunningSceneViewModeState.Enabled = en;
            py += BH + 6;

            if (CunningSceneViewModeState.Enabled) {
                Button(ref py, px, w, "Prim#", CunningSceneViewModeState.ModePrimNumber);
                Button(ref py, px, w, "PrimN", CunningSceneViewModeState.ModePrimNormal);
                Button(ref py, px, w, "Point#", CunningSceneViewModeState.ModePointNumber);
                Button(ref py, px, w, "Normal", CunningSceneViewModeState.ModeNormal);
            }
            Handles.EndGUI();
        }

        static void Button(ref float y, float x, float w, string label, int mode) {
            var on = CunningSceneViewModeState.Mode == mode;
            if (GUI.Toggle(new Rect(x, y, w, BH), on, label, "Button") && !on) CunningSceneViewModeState.Mode = mode;
            y += BH + 4;
        }
    }
}

