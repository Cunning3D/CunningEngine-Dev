using UnityEditor;

namespace CunningEngine.Editor {
    // Legacy shim: keep old file path/name, forward to the new Demos window implementation.
    public sealed class CunningDemoEditorWindow : EditorWindow {
        public static void ShowWindow() { Demos.CunningDemosWindow.ShowWindow(); }
    }
}

