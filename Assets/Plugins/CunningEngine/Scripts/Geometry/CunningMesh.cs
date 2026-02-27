using UnityEngine;
using System;
using System.Runtime.InteropServices;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CunningEngine {
    [RequireComponent(typeof(MeshFilter))]
    public class CunningMesh : MonoBehaviour, ICunningInputHandle {
        public ulong currentHandle = 0; // Public for debug inspection
        public ulong CurrentHandle => currentHandle;
        public enum DisplayMode { Solid, SolidAndWire, Wire }
        [HideInInspector] public DisplayMode displayMode = DisplayMode.Solid;
        [HideInInspector] public bool solidUnlit = false;
        [HideInInspector] public Color wireframeColor = Color.cyan;
        Mesh mesh;
        int[] tri = Array.Empty<int>(), lines = Array.Empty<int>();
        public int[] LineIndices => lines; // editor wire draw
        static Material s_defMat;
        static bool s_triedDefMat;

        static Material TryGetDefaultMat() {
            if (!s_triedDefMat) {
                s_triedDefMat = true;
#if UNITY_EDITOR
                s_defMat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
#endif
            }
            return s_defMat;
        }

        public void LoadFromHandle(ulong handle) {
            if (currentHandle != 0 && currentHandle != handle) {
                NativeMethods.cunning_release_handle(currentHandle);
            }
            currentHandle = handle;
            UpdateMesh();
        }

        public void ApplyMaterials() {
            var r = GetComponent<MeshRenderer>(); if (r == null) return;
            var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color");
            var wire = Shader.Find("Hidden/Internal-Colored") ?? unlit;
            var solidShader = solidUnlit ? unlit : lit;
            var m0 = (r.sharedMaterials != null && r.sharedMaterials.Length > 0 && r.sharedMaterials[0] != null && r.sharedMaterials[0].shader == solidShader)
                ? r.sharedMaterials[0]
                : new Material(solidShader);
            var m1 = (r.sharedMaterials != null && r.sharedMaterials.Length > 1 && r.sharedMaterials[1] != null && r.sharedMaterials[1].shader == wire)
                ? r.sharedMaterials[1]
                : new Material(wire);
            var def = TryGetDefaultMat();
            var solidColor = def != null ? def.color : new Color(0.78f, 0.78f, 0.78f, 1f);
            m0.color = solidColor;
            if (m0.HasProperty("_BaseColor")) m0.SetColor("_BaseColor", solidColor);
            if (m0.HasProperty("_Color")) m0.SetColor("_Color", solidColor);
            if (m0.HasProperty("_Metallic")) m0.SetFloat("_Metallic", (def != null && def.HasProperty("_Metallic")) ? def.GetFloat("_Metallic") : 0f);
            if (m0.HasProperty("_Smoothness")) m0.SetFloat("_Smoothness", (def != null && def.HasProperty("_Smoothness")) ? def.GetFloat("_Smoothness") : 0.1f);
            if (m1.HasProperty("_SrcBlend")) m1.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m1.HasProperty("_DstBlend")) m1.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m1.HasProperty("_Cull")) m1.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            if (m1.HasProperty("_ZWrite")) m1.SetInt("_ZWrite", 0);
            if (m1.HasProperty("_ZTest")) m1.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            m1.color = wireframeColor;
            if (m1.HasProperty("_BaseColor")) m1.SetColor("_BaseColor", wireframeColor);
            if (m1.HasProperty("_Color")) m1.SetColor("_Color", wireframeColor);
            m1.renderQueue = 4000; // overlay
            r.sharedMaterials = displayMode == DisplayMode.Wire ? new Material[] { m1, m1 } : (displayMode == DisplayMode.Solid ? new Material[] { m0, m0 } : new Material[] { m0, m1 });
        }

        public void ApplyDisplayMode() {
            if (mesh == null) mesh = GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) return;
            mesh.subMeshCount = 2;
            mesh.SetIndices(displayMode == DisplayMode.Wire ? Array.Empty<int>() : tri, MeshTopology.Triangles, 0);
            mesh.SetIndices(displayMode == DisplayMode.Solid ? Array.Empty<int>() : lines, MeshTopology.Lines, 1);
            ApplyMaterials();
        }

        private void UpdateMesh() {
            if (currentHandle == 0) return;

            int vCount = (int)NativeMethods.cunning_geo_get_vertex_count(currentHandle);
            int iCount = (int)NativeMethods.cunning_geo_copy_indices(currentHandle, IntPtr.Zero);
            int lCount = (int)NativeMethods.cunning_geo_copy_lines(currentHandle, IntPtr.Zero);

            if (vCount == 0) {
                Debug.LogWarning("CunningMesh: 0 vertices received.");
                return;
            }

            Vector3[] vertices = new Vector3[vCount];
            int[] indices = new int[iCount];
            int[] lns = new int[lCount];

            GCHandle hV = GCHandle.Alloc(vertices, GCHandleType.Pinned);
            GCHandle hI = GCHandle.Alloc(indices, GCHandleType.Pinned);
            GCHandle hL = GCHandle.Alloc(lns, GCHandleType.Pinned);

            try {
                NativeMethods.cunning_geo_copy_vertices(currentHandle, hV.AddrOfPinnedObject());
                if (iCount > 0) {
                    NativeMethods.cunning_geo_copy_indices(currentHandle, hI.AddrOfPinnedObject());
                }
                if (lCount > 0) {
                    NativeMethods.cunning_geo_copy_lines(currentHandle, hL.AddrOfPinnedObject());
                }
            } finally {
                hV.Free();
                hI.Free();
                hL.Free();
            }

            if (mesh == null) mesh = new Mesh();
            else mesh.Clear();
            mesh.name = "CunningMesh_" + currentHandle;
            if (vCount > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            
            mesh.vertices = vertices;
            tri = indices;
            lines = lns;
            var hasTris = tri.Length >= 3 && (tri.Length % 3) == 0;
            mesh.subMeshCount = 1;
            mesh.SetTriangles(hasTris ? tri : Array.Empty<int>(), 0, true);
            if (hasTris) mesh.RecalculateNormals(); // avoid Unity warning: submesh lines/points
            mesh.subMeshCount = 2;
            mesh.SetIndices((lines.Length >= 2 && (lines.Length % 2) == 0) ? lines : Array.Empty<int>(), MeshTopology.Lines, 1);
            mesh.RecalculateBounds();

            GetComponent<MeshFilter>().mesh = mesh;
            ApplyDisplayMode();
        }

        void OnDestroy() {
            if (currentHandle != 0) {
                NativeMethods.cunning_release_handle(currentHandle);
                currentHandle = 0;
            }
        }
    }
}
