using UnityEditor;
using UnityEngine;

namespace CunningEngine.Editor {
    public static class CunningEngineSettings {
        const string K_C3D_EXE = "CunningEngine.Settings.Cunning3DExe";
        const string K_AUTO_LAUNCH = "CunningEngine.Settings.AutoLaunchCunning3D";
        const string K_SELECTION_NGON_WIREFRAME = "CunningEngine.Settings.SelectionNgonWireframe";

        public static string Cunning3DExePath { get => EditorPrefs.GetString(K_C3D_EXE, ""); set => EditorPrefs.SetString(K_C3D_EXE, value ?? ""); }
        public static bool AutoLaunchCunning3D { get => EditorPrefs.GetBool(K_AUTO_LAUNCH, true); set => EditorPrefs.SetBool(K_AUTO_LAUNCH, value); }
        public static bool SelectionNgonWireframe {
            get => EditorPrefs.GetBool(K_SELECTION_NGON_WIREFRAME, true);
            set {
                EditorPrefs.SetBool(K_SELECTION_NGON_WIREFRAME, value);
                SceneView.RepaintAll();
            }
        }

        public static bool EnsureCunning3DConfigured() {
            if (!string.IsNullOrEmpty(Cunning3DExePath) && System.IO.File.Exists(Cunning3DExePath)) return true;
            var picked = EditorUtility.OpenFilePanel("Locate Cunning3D Executable", Application.dataPath, Application.platform == RuntimePlatform.WindowsEditor ? "exe" : "");
            if (string.IsNullOrEmpty(picked)) return false;
            Cunning3DExePath = picked;
            return true;
        }
    }
}

