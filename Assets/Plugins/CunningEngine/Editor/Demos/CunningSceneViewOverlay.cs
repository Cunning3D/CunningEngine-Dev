using System;
using UnityEditor;

namespace CunningEngine.Editor.Demos {
    static class CunningSceneViewModeState {
        const string KEnabled = "CunningEngine.SceneView.Enabled";
        const string KMode = "CunningEngine.SceneView.Mode";
        const string KWireMode = "CunningEngine.SceneView.WireMode";

        public static event Action Changed;
        static void Touch() { Changed?.Invoke(); SceneView.RepaintAll(); }

        public static bool Enabled { get => EditorPrefs.GetBool(KEnabled, true); set { EditorPrefs.SetBool(KEnabled, value); Touch(); } }
        public static int Mode { get => EditorPrefs.GetInt(KMode, 0); set { EditorPrefs.SetInt(KMode, value); Touch(); } } // 0=None,1=Prim#,2=PrimN,3=Point#,4=Normal
        public static int WireMode { get => EditorPrefs.GetInt(KWireMode, WireModeSelected); set { EditorPrefs.SetInt(KWireMode, value); Touch(); } } // 0=Off,1=Selected,2=All
        public static bool NormalIsVertex { get => false; set { } } // compat: normals auto-select (vertex if possible else point)

        public const int ModeNone = 0, ModePrimNumber = 1, ModePrimNormal = 2, ModePointNumber = 3, ModeNormal = 4;
        public const int WireModeOff = 0, WireModeSelected = 1, WireModeAll = 2;

        public static bool HasSelectedCunningMesh() {
            var gos = Selection.gameObjects;
            if (gos == null || gos.Length == 0) return false;
            foreach (var go in gos) if (go && (go.GetComponent<CunningMesh>() || go.GetComponentInParent<CunningMesh>())) return true;
            return false;
        }

        public static string GetWireModeLabel() {
            return WireMode switch {
                WireModeOff => "Off",
                WireModeAll => "All",
                _ => "Selected",
            };
        }

        public static string GetWireModeShortLabel() {
            return WireMode switch {
                WireModeOff => "Off",
                WireModeAll => "All",
                _ => "Sel",
            };
        }
    }
}
