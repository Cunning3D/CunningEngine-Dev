using UnityEngine;
using System;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CunningEngine {
    [RequireComponent(typeof(MeshFilter))]
    public class CunningMesh : MonoBehaviour, ICunningInputHandle {
        public ulong currentHandle = 0; // Public for debug inspection
        public ulong CurrentHandle => currentHandle;
        [HideInInspector] public bool ownsHandle = true; // if false, host manages handle lifetime
        public enum DisplayMode { Solid, SolidAndWire, Wire }
        [HideInInspector] public DisplayMode displayMode = DisplayMode.Solid;
        [HideInInspector] public bool solidUnlit = false;
        [HideInInspector] public Color wireframeColor = Color.cyan;
        [HideInInspector] public bool preserveExistingSolidMaterial = true;
        [HideInInspector] public Material solidMaterialOverride;
        [HideInInspector] public Material[] solidMaterialOverrides = Array.Empty<Material>();
#if UNITY_EDITOR
        [NonSerialized] bool editorSelectionHighlightActive;
        [NonSerialized] Color editorSelectionWireColor = Color.white;
#endif
        ulong lastUploadedDirtyId;
        Mesh mesh;
        readonly CunningNativeMeshBuilder.MeshRuntimeCache meshRuntimeCache = new CunningNativeMeshBuilder.MeshRuntimeCache();
        int[][] solidSubmeshTriangles = Array.Empty<int[]>();
        int[] solidMaterialSlots = Array.Empty<int>();
        int[] lines = Array.Empty<int>();
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

        void OnEnable() {
            if (TryRestoreBuiltinDemoShape()) return;
            if (currentHandle != 0) UpdateMesh();
        }

        bool TryRestoreBuiltinDemoShape() {
#if UNITY_EDITOR
            if (Application.isPlaying) return false;
            if (GetComponent<CunningDemoShape>() != null) return false;

            CunningDemoShape.ShapeKind? kind = null;
            if (gameObject.name == "Cunning_Box") kind = CunningDemoShape.ShapeKind.Cube;
            else if (gameObject.name == "Cunning_Sphere") kind = CunningDemoShape.ShapeKind.Sphere;
            else if (gameObject.name == "Cunning_Pentagon") kind = CunningDemoShape.ShapeKind.Pentagon;
            if (!kind.HasValue) return false;

            var demo = gameObject.AddComponent<CunningDemoShape>();
            demo.kind = kind.Value;
            EditorUtility.SetDirty(gameObject);
            demo.Rebuild();
            return true;
#else
            return false;
#endif
        }

        public void LoadFromHandle(ulong handle) {
            if (ownsHandle && currentHandle != 0 && currentHandle != handle) {
                NativeMethods.cunning_release_handle(currentHandle);
            }
            if (currentHandle != handle) {
                lastUploadedDirtyId = 0;
                meshRuntimeCache.Reset();
            }
            currentHandle = handle;
            if (currentHandle == 0) {
                ClearMeshContents();
                return;
            }
            UpdateMesh();
        }

        public void ApplyMaterials() {
            var r = GetComponent<MeshRenderer>(); if (r == null) return;
            var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color");
            var solidShader = EffectiveSolidUnlit ? unlit : lit;
            var def = TryGetDefaultMat();
            int solidSubmeshCount = Mathf.Max(1, solidSubmeshTriangles != null ? solidSubmeshTriangles.Length : 0);
            var materials = new Material[solidSubmeshCount];

            for (int submeshIndex = 0; submeshIndex < solidSubmeshCount; submeshIndex++) {
                var existingSolid = (r.sharedMaterials != null && r.sharedMaterials.Length > submeshIndex) ? r.sharedMaterials[submeshIndex] : null;
                Material overrideMaterial = ResolveSolidMaterialOverride(submeshIndex);
                bool useProvidedSolidMaterial = overrideMaterial != null || (preserveExistingSolidMaterial && existingSolid != null);
                var solidMaterial = overrideMaterial != null
                    ? overrideMaterial
                    : ((preserveExistingSolidMaterial && existingSolid != null)
                        ? existingSolid
                        : ((existingSolid != null && existingSolid.shader == solidShader) ? existingSolid : new Material(solidShader)));

                if (!useProvidedSolidMaterial) {
                    var solidColor = def != null ? def.color : new Color(0.78f, 0.78f, 0.78f, 1f);
                    solidMaterial.color = solidColor;
                    if (solidMaterial.HasProperty("_BaseColor")) solidMaterial.SetColor("_BaseColor", solidColor);
                    if (solidMaterial.HasProperty("_Color")) solidMaterial.SetColor("_Color", solidColor);
                    if (solidMaterial.HasProperty("_Metallic")) solidMaterial.SetFloat("_Metallic", (def != null && def.HasProperty("_Metallic")) ? def.GetFloat("_Metallic") : 0f);
                    if (solidMaterial.HasProperty("_Smoothness")) solidMaterial.SetFloat("_Smoothness", (def != null && def.HasProperty("_Smoothness")) ? def.GetFloat("_Smoothness") : 0.1f);
                }

                materials[submeshIndex] = solidMaterial;
            }

            r.sharedMaterials = materials;
        }

        public void ApplyDisplayMode() {
            if (mesh == null) mesh = GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) return;
            DisplayMode effectiveDisplayMode = EffectiveDisplayMode;
            int solidSubmeshCount = Mathf.Max(1, solidSubmeshTriangles != null ? solidSubmeshTriangles.Length : 0);
            mesh.subMeshCount = solidSubmeshCount;
            for (int submeshIndex = 0; submeshIndex < solidSubmeshCount; submeshIndex++) {
                var indices = (effectiveDisplayMode == DisplayMode.Wire || solidSubmeshTriangles == null || submeshIndex >= solidSubmeshTriangles.Length)
                    ? Array.Empty<int>()
                    : solidSubmeshTriangles[submeshIndex] ?? Array.Empty<int>();
                mesh.SetIndices(indices, MeshTopology.Triangles, submeshIndex);
            }

            ApplyMaterials();
#if UNITY_EDITOR
            SceneView.RepaintAll();
#endif
        }

        private void UpdateMesh() {
            if (currentHandle == 0) {
                ClearMeshContents();
                return;
            }
            if (mesh == null) mesh = GetComponent<MeshFilter>()?.sharedMesh;
            var dirtyId = NativeMethods.cunning_geo_get_dirty_id(currentHandle);
            if (mesh != null && dirtyId != 0 && dirtyId == lastUploadedDirtyId) {
                ApplyDisplayMode();
                return;
            }
            if ((int)NativeMethods.cunning_geo_get_vertex_count(currentHandle) == 0) {
                ClearMeshContents();
                return;
            }

            if (mesh == null) {
                mesh = new Mesh();
                mesh.MarkDynamic();
            }
            if (!CunningNativeMeshBuilder.TryFillMeshFromHandle(
                currentHandle,
                mesh,
                "CunningMesh_" + currentHandle,
                includeLines: true,
                runtimeCache: meshRuntimeCache,
                out var buildData
            )) {
                Debug.LogWarning("CunningMesh: failed to build mesh from native handle.");
                ClearMeshContents();
                return;
            }

            solidSubmeshTriangles = buildData.solidSubmeshIndices ?? Array.Empty<int[]>();
            solidMaterialSlots = buildData.solidMaterialSlots ?? Array.Empty<int>();
            lines = buildData.lines ?? Array.Empty<int>();

            GetComponent<MeshFilter>().sharedMesh = mesh;
            lastUploadedDirtyId = dirtyId != 0 ? dirtyId : NativeMethods.cunning_geo_get_dirty_id(currentHandle);
            ApplyDisplayMode();
        }

        void ClearMeshContents() {
            solidSubmeshTriangles = Array.Empty<int[]>();
            solidMaterialSlots = Array.Empty<int>();
            lines = Array.Empty<int>();
            lastUploadedDirtyId = 0;
            meshRuntimeCache.Reset();

            if (mesh != null) {
                mesh.Clear();
            }

            var meshFilter = GetComponent<MeshFilter>();
            if (meshFilter != null) {
                meshFilter.sharedMesh = null;
            }
        }

        Material ResolveSolidMaterialOverride(int submeshIndex) {
            if (solidMaterialOverrides != null && solidMaterialOverrides.Length > 0) {
                if (solidMaterialSlots != null && solidMaterialSlots.Length > submeshIndex) {
                    int materialSlot = solidMaterialSlots[submeshIndex];
                    if (materialSlot >= 0 && materialSlot < solidMaterialOverrides.Length && solidMaterialOverrides[materialSlot] != null) {
                        return solidMaterialOverrides[materialSlot];
                    }
                }

                if (submeshIndex < solidMaterialOverrides.Length && solidMaterialOverrides[submeshIndex] != null) {
                    return solidMaterialOverrides[submeshIndex];
                }
            }

            if (submeshIndex == 0) {
                return solidMaterialOverride;
            }

            return null;
        }

        DisplayMode EffectiveDisplayMode {
            get {
#if UNITY_EDITOR
                if (editorSelectionHighlightActive && displayMode == DisplayMode.Solid) {
                    return DisplayMode.SolidAndWire;
                }
#endif
                return displayMode;
            }
        }

        bool EffectiveSolidUnlit => solidUnlit;

        Color EffectiveWireframeColor {
            get {
#if UNITY_EDITOR
                if (editorSelectionHighlightActive) {
                    return editorSelectionWireColor;
                }
#endif
                return wireframeColor;
            }
        }

#if UNITY_EDITOR
        public bool EditorWantsWireOverlay => EffectiveDisplayMode != DisplayMode.Solid && lines != null && lines.Length >= 2;
        public Color EditorWireOverlayColor => EffectiveWireframeColor;

        public void SetEditorSelectionHighlight(bool active, Color wireColor) {
            if (editorSelectionHighlightActive == active && editorSelectionWireColor.Equals(wireColor)) {
                return;
            }

            editorSelectionHighlightActive = active;
            editorSelectionWireColor = wireColor;
            ApplyDisplayMode();
        }
#endif

        void OnDestroy() {
            if (ownsHandle && currentHandle != 0) {
                NativeMethods.cunning_release_handle(currentHandle);
                currentHandle = 0;
            }
            lastUploadedDirtyId = 0;
            meshRuntimeCache.Reset();
        }
    }
}
