using UnityEngine;
using UnityEditor;

namespace CunningEngine.Editor {
    public static class CunningMenu {
        [MenuItem("Procedural/Cunning Engine/Init")]
        public static void Init() {
            try {
                NativeMethods.EnsureLoaded();
                NativeMethods.cunning_init();
                Debug.Log("<color=cyan>Cunning Engine</color> Initialized Successfully!");
            } catch (System.Exception e) {
                Debug.LogError("Failed to init Cunning Engine: " + e.GetType().FullName + " :: " + e.Message);
                Debug.LogError(e.ToString());
                Debug.LogError("Did you put 'cunning_core_ffi.dll' in Assets/Plugins/CunningEngine/Plugins/x86_64/?");
            }
        }

        [MenuItem("Procedural/Cunning Engine/Test FFI Load")]
        public static void TestLoad() {
            // Simple test to see if DLL is reachable
            Init();
        }

        [MenuItem("Procedural/Cunning Engine/Demos")]
        public static void OpenDemos() { global::CunningEngine.Editor.Demos.CunningDemosWindow.ShowWindow(); }
    }
}
