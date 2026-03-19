using UnityEditor;
using UnityEngine;

namespace CunningEngine.Editor {
    public static class CunningEngineSettingsMenu {
        [MenuItem("Procedural/Cunning Engine/Settings...", false, 90)]
        static void Open() {
            var w = EditorWindow.GetWindow<CunningEngineSettingsWindow>(true, "Cunning Engine Settings");
            w.minSize = new Vector2(520, 210);
            w.Show();
        }
    }

    sealed class CunningEngineSettingsWindow : EditorWindow {
        void OnGUI() {
            EditorGUILayout.LabelField("Cunning3D", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            var p = CunningEngineSettings.Cunning3DExePath;
            var np = EditorGUILayout.TextField("Executable Path", p);
            if (np != p) CunningEngineSettings.Cunning3DExePath = np;
            if (GUILayout.Button("Browse", GUILayout.Width(80))) {
                var picked = EditorUtility.OpenFilePanel("Locate Cunning3D Executable", Application.dataPath, Application.platform == RuntimePlatform.WindowsEditor ? "exe" : "");
                if (!string.IsNullOrEmpty(picked)) CunningEngineSettings.Cunning3DExePath = picked;
            }
            EditorGUILayout.EndHorizontal();

            CunningEngineSettings.AutoLaunchCunning3D = EditorGUILayout.Toggle("Auto Launch After Bridge", CunningEngineSettings.AutoLaunchCunning3D);

            if (!string.IsNullOrEmpty(CunningEngineSettings.Cunning3DExePath) && !System.IO.File.Exists(CunningEngineSettings.Cunning3DExePath))
                EditorGUILayout.HelpBox("Executable path does not exist.", MessageType.Warning);

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Scene View", EditorStyles.boldLabel);
            CunningEngineSettings.SelectionNgonWireframe = EditorGUILayout.Toggle("Enable NGon Wire Overlay", CunningEngineSettings.SelectionNgonWireframe);
            EditorGUILayout.HelpBox("Enables Cunning SceneView draw modes. Use the SceneView toolbar overlay 'Cunning' to switch Off / Selected / All wire overlay.", MessageType.None);
        }
    }
}

