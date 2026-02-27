using UnityEditor;
using UnityEngine;
using CunningEngine;

namespace CunningEngine.Editor {
    [CustomEditor(typeof(CunningMesh))]
    public sealed class CunningMeshEditor : UnityEditor.Editor {
        bool _adv;
        public override void OnInspectorGUI() {
            var t = (CunningMesh)target;
            using (new EditorGUI.DisabledScope(true)) {
                EditorGUILayout.ObjectField("Script", MonoScript.FromMonoBehaviour(t), typeof(MonoScript), false);
                EditorGUILayout.LongField("Current Handle", unchecked((long)t.currentHandle));
            }
            if (GUILayout.Button("Select Owner")) Selection.activeGameObject = t.gameObject;
            _adv = EditorGUILayout.Foldout(_adv, "Advanced (legacy)", true);
            if (_adv) {
                using (new EditorGUI.IndentLevelScope()) {
                    t.displayMode = (CunningMesh.DisplayMode)EditorGUILayout.EnumPopup("Display Mode", t.displayMode);
                    t.solidUnlit = EditorGUILayout.Toggle("Solid Unlit", t.solidUnlit);
                    using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ColorField("Wireframe Color (fixed)", t.wireframeColor);
                    if (GUILayout.Button("Apply Display")) { t.ApplyDisplayMode(); EditorUtility.SetDirty(t); }
                }
            }
        }
    }
}

