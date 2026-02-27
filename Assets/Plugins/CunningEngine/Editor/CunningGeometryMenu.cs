using UnityEditor;
using UnityEngine;

namespace CunningEngine.Editor {
    public static class CunningGeometryMenu {
        static GameObject Spawn(string name, System.Func<ulong> makeHandle, MenuCommand cmd) {
            CunningBridge.Instance.Init();
            var handle = makeHandle();
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            if (cmd != null && cmd.context is GameObject parent) GameObjectUtility.SetParentAndAlign(go, parent);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            var cm = go.AddComponent<CunningMesh>();
            cm.LoadFromHandle(handle);
            Selection.activeGameObject = go;
            SceneView.lastActiveSceneView?.FrameSelected();
            return go;
        }

        [MenuItem("GameObject/Cunning Geometry/Box", false, 10)]
        static void Box(MenuCommand cmd) => Spawn("Cunning_Box", () => NativeMethods.cunning_geo_create_cube(1f, 1, 1, 1), cmd);

        [MenuItem("GameObject/Cunning Geometry/Sphere", false, 11)]
        static void Sphere(MenuCommand cmd) => Spawn("Cunning_Sphere", () => NativeMethods.cunning_geo_create_sphere(1f, 12, 24), cmd);

        [MenuItem("GameObject/Cunning Geometry/Pentagon", false, 12)]
        static void Pentagon(MenuCommand cmd) => Spawn("Cunning_Pentagon", () => {
            var baseH = NativeMethods.cunning_geo_create();
            var p0 = NativeMethods.cunning_geo_add_point(baseH, 0, 0, 0);
            var p1 = NativeMethods.cunning_geo_add_point(baseH, 1, 0, 0);
            var p2 = NativeMethods.cunning_geo_add_point(baseH, 1.5f, 1, 0);
            var p3 = NativeMethods.cunning_geo_add_point(baseH, 0.5f, 1.5f, 0);
            var p4 = NativeMethods.cunning_geo_add_point(baseH, -0.5f, 1, 0);
            NativeMethods.cunning_geo_add_poly(baseH, new ulong[] { p0, p1, p2, p3, p4 }, 5);
            var h = NativeMethods.cunning_op_poly_extrude(baseH, 2f, 0f);
            NativeMethods.cunning_release_handle(baseH);
            return h;
        }, cmd);

        [MenuItem("GameObject/Cunning Geometry/Poly Bevel...", false, 50)]
        static void PolyBevel(MenuCommand cmd) => CunningPolyBevelWindow.ShowWindow();
    }
}

