using UnityEditor;
using UnityEngine;
using CunningEngine;
using CunningEngine.CDA;

namespace CunningEngine.Editor.CDA {
    [InitializeOnLoad]
    public static class CdaDragDropSpawner {
        static CdaDragDropSpawner() {
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
        }

        static CDAAssetObject GetDraggedAsset() {
            var objs = DragAndDrop.objectReferences;
            if (objs == null) return null;
            for (int i = 0; i < objs.Length; i++) {
                var a = objs[i] as CDAAssetObject;
                if (a != null) return a;
            }
            return null;
        }

        static void SpawnInstance(CDAAssetObject asset) {
            var go = new GameObject(asset != null ? asset.name : "CDA Instance");
            go.SetActive(false);
            Undo.RegisterCreatedObjectUndo(go, "Create CDA Instance");
            var inst = go.AddComponent<CunningCDAHostInstance>();
            inst.asset = asset;
            go.SetActive(true);
            EditorUtility.SetDirty(inst);
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
            Selection.activeGameObject = go;
        }

        static void HandleDrag(Event e) {
            var asset = GetDraggedAsset();
            if (asset == null) return;

            if (e.type == EventType.DragUpdated || e.type == EventType.DragPerform) {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (e.type == EventType.DragPerform) {
                    DragAndDrop.AcceptDrag();
                    SpawnInstance(asset);
                }
                e.Use();
            }
        }

        static void OnSceneGUI(SceneView view) { HandleDrag(Event.current); }
        static void OnHierarchyGUI(int instanceId, Rect selectionRect) { HandleDrag(Event.current); }

        [MenuItem("GameObject/Cunning/CDA Instance", false, 10)]
        static void CreateEmpty(MenuCommand _) { SpawnInstance(null); }
    }
}
