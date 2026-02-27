using UnityEditor;
using UnityEngine;

namespace CunningEngine.Editor.Demos {
    public sealed class CunningDemosWindow : EditorWindow {
        [MenuItem("Procedural/Cunning Engine/Demos", false, 120)]
        public static void ShowWindow() { GetWindow<CunningDemosWindow>("Cunning Demos"); }

        float cubeSize = 1f, sphereRadius = 1f, pentagonExtrude = 2f;
        int sphereRings = 12, sphereSegments = 24, cubeDiv = 1;
        int extrudePrim = 0, extrudeDiv = 1;
        float extrudeDist = 0.2f, extrudeInset = 0f;

        void OnGUI() {
            if (GUILayout.Button("Init Engine")) CunningMenu.Init();
            GUILayout.Space(8);
            GUILayout.Label("Basic Shapes", EditorStyles.boldLabel);
            cubeSize = EditorGUILayout.FloatField("Cube Size", cubeSize);
            cubeDiv = Mathf.Max(1, EditorGUILayout.IntField("Cube Div", cubeDiv));
            if (GUILayout.Button("Create Cube")) Create(CunningDemoShape.ShapeKind.Cube, cubeSize, cubeDiv, sphereRadius, sphereRings, sphereSegments, pentagonExtrude);
            GUILayout.Space(6);
            sphereRadius = EditorGUILayout.FloatField("Sphere Radius", sphereRadius);
            sphereRings = Mathf.Max(3, EditorGUILayout.IntField("Sphere Rings", sphereRings));
            sphereSegments = Mathf.Max(3, EditorGUILayout.IntField("Sphere Segments", sphereSegments));
            if (GUILayout.Button("Create Sphere")) Create(CunningDemoShape.ShapeKind.Sphere, cubeSize, cubeDiv, sphereRadius, sphereRings, sphereSegments, pentagonExtrude);
            GUILayout.Space(6);
            pentagonExtrude = EditorGUILayout.FloatField("Pentagon Extrude", pentagonExtrude);
            if (GUILayout.Button("Create Pentagon")) Create(CunningDemoShape.ShapeKind.Pentagon, cubeSize, cubeDiv, sphereRadius, sphereRings, sphereSegments, pentagonExtrude);

            GUILayout.Space(10);
            GUILayout.Label("Poly Extrude (Selected Prim, Ngon)", EditorStyles.boldLabel);
            var cm = Selection.activeGameObject ? Selection.activeGameObject.GetComponent<CunningMesh>() : null;
            if (cm == null || cm.currentHandle == 0) {
                EditorGUILayout.HelpBox("Select a CunningMesh with valid handle, then extrude a primitive by its index (ngon prim order).", MessageType.Info);
            } else {
                var primCount = (int)NativeMethods.cunning_geo_get_prim_count(cm.currentHandle);
                EditorGUILayout.LabelField("Prim Count", primCount.ToString());
                extrudePrim = Mathf.Clamp(EditorGUILayout.IntField("Prim Index", extrudePrim), 0, Mathf.Max(0, primCount - 1));
                extrudeDist = EditorGUILayout.FloatField("Distance", extrudeDist);
                extrudeInset = EditorGUILayout.FloatField("Inset", extrudeInset);
                extrudeDiv = Mathf.Clamp(EditorGUILayout.IntField("Divisions", extrudeDiv), 1, 128);
                using (new EditorGUI.DisabledScope(primCount <= 0)) {
                    if (GUILayout.Button("Extrude Selected Prim")) {
                        try {
                            var outH = NativeMethods.cunning_op_poly_extrude_prim(cm.currentHandle, (uint)extrudePrim, extrudeDist, extrudeInset, (uint)extrudeDiv);
                            if (outH != 0) cm.LoadFromHandle(outH);
                        } catch (System.EntryPointNotFoundException) {
                            Debug.LogError("Cunning: missing FFI symbol cunning_op_poly_extrude_prim. Rebuild and replace cunning_core_ffi.dll.");
                        }
                    }
                }
            }
        }

        static void Create(CunningDemoShape.ShapeKind kind, float cubeSize, int cubeDiv, float sphereRadius, int sphereRings, int sphereSegments, float pentagonExtrude) {
            var go = new GameObject("Cunning_" + kind);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            go.AddComponent<CunningMesh>();
            var d = go.AddComponent<CunningDemoShape>();
            d.kind = kind; d.cubeSize = cubeSize; d.cubeDiv = cubeDiv; d.sphereRadius = sphereRadius; d.sphereRings = sphereRings; d.sphereSegments = sphereSegments; d.pentagonExtrude = pentagonExtrude;
            d.Rebuild();
            Selection.activeGameObject = go;
            SceneView.lastActiveSceneView?.FrameSelected();
        }
    }
}

