using UnityEditor;
using UnityEngine;

namespace CunningEngine.Editor {
    public static class CunningGeometryMenu {
        static GameObject Spawn(string name, CunningDemoShape.ShapeKind kind, MenuCommand cmd) {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            if (cmd != null && cmd.context is GameObject parent) GameObjectUtility.SetParentAndAlign(go, parent);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            go.AddComponent<CunningMesh>();
            var demo = go.AddComponent<CunningDemoShape>();
            demo.kind = kind;
            demo.Rebuild();
            Selection.activeGameObject = go;
            SceneView.lastActiveSceneView?.FrameSelected();
            return go;
        }

        [MenuItem("GameObject/Cunning Geometry/Box", false, 10)]
        static void Box(MenuCommand cmd) => Spawn("Cunning_Box", CunningDemoShape.ShapeKind.Cube, cmd);

        [MenuItem("GameObject/Cunning Geometry/Sphere", false, 11)]
        static void Sphere(MenuCommand cmd) => Spawn("Cunning_Sphere", CunningDemoShape.ShapeKind.Sphere, cmd);

        [MenuItem("GameObject/Cunning Geometry/Pentagon", false, 12)]
        static void Pentagon(MenuCommand cmd) => Spawn("Cunning_Pentagon", CunningDemoShape.ShapeKind.Pentagon, cmd);

        [MenuItem("GameObject/Cunning Geometry/Poly Bevel...", false, 50)]
        static void PolyBevel(MenuCommand cmd) => CunningPolyBevelWindow.ShowWindow();
    }
}
