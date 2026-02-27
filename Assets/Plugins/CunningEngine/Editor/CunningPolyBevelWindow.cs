using UnityEditor;
using UnityEngine;

namespace CunningEngine.Editor {
    public sealed class CunningPolyBevelWindow : EditorWindow {
        float amount = 0.1f;
        int segments = 1;

        [MenuItem("Procedural/Cunning Engine/Poly Bevel", false, 130)]
        public static void ShowWindow() => GetWindow<CunningPolyBevelWindow>("Cunning PolyBevel");

        void OnGUI() {
            EditorGUILayout.HelpBox("Select a CunningMesh, then bevel (ngon-based, not Unity triangles).", MessageType.Info);
            amount = EditorGUILayout.Slider("Amount", amount, 0f, 2f);
            segments = Mathf.Clamp(EditorGUILayout.IntSlider("Segments", segments, 1, 16), 1, 16);
            using (new EditorGUI.DisabledScope(Selection.GetFiltered<CunningMesh>(SelectionMode.Editable | SelectionMode.ExcludePrefab).Length == 0)) {
                if (GUILayout.Button("Apply To Selected")) Apply();
            }
        }

        static void Apply() {
            CunningBridge.Instance.Init();
            var cms = Selection.GetFiltered<CunningMesh>(SelectionMode.Editable | SelectionMode.ExcludePrefab);
            if (cms == null || cms.Length == 0) { Debug.LogWarning("Cunning: select a CunningMesh first."); return; }
            var w = GetWindow<CunningPolyBevelWindow>();
            foreach (var cm in cms) {
                if (!cm || cm.currentHandle == 0) continue;
                ulong outH;
                try { outH = NativeMethods.cunning_op_poly_bevel(cm.currentHandle, w.amount, (uint)w.segments); }
                catch (System.EntryPointNotFoundException) { Debug.LogError("Cunning: missing FFI symbol cunning_op_poly_bevel. Update cunning_core_ffi.dll."); return; }
                if (outH != 0) cm.LoadFromHandle(outH);
            }
        }
    }
}

