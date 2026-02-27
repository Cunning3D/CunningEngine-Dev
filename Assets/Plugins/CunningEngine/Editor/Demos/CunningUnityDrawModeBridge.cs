using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CunningEngine.Editor.Demos {
    /// Bridge Unity SceneView Draw Modes -> CunningMesh rendering.
    /// Goal: when user clicks Unity's wireframe icons, CunningMesh shows true n-gon wire (and optional solid),
    /// while other renderers keep Unity's default behavior.
    [InitializeOnLoad]
    public static class CunningUnityDrawModeBridge {
        static Material litMat, unlitSolidMat, depthMat, wireMat;
        static readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        static readonly Color unityWireGrey = new Color(0.22f, 0.22f, 0.22f, 1f);
        static readonly Color unityWireBlack = new Color(0f, 0f, 0f, 1f);
        const float wireWidth = 2.0f;
        static int prevAA = -2;
        static Material defMat;
        static bool triedDefMat;

        static CunningUnityDrawModeBridge() {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        static bool IsUnityWireMode(DrawCameraMode dm) => dm == DrawCameraMode.Wireframe || dm == DrawCameraMode.TexturedWire;

        static void EnsureMats() {
            if (!triedDefMat) { triedDefMat = true; defMat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat"); }
            var solidColor = defMat != null ? defMat.color : new Color(0.78f, 0.78f, 0.78f, 1f);
            if (litMat == null) {
                var s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
                litMat = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
                litMat.color = solidColor;
                if (litMat.HasProperty("_BaseColor")) litMat.SetColor("_BaseColor", solidColor);
                if (litMat.HasProperty("_Color")) litMat.SetColor("_Color", solidColor);
                if (litMat.HasProperty("_Metallic")) litMat.SetFloat("_Metallic", (defMat != null && defMat.HasProperty("_Metallic")) ? defMat.GetFloat("_Metallic") : 0f);
                if (litMat.HasProperty("_Smoothness")) litMat.SetFloat("_Smoothness", (defMat != null && defMat.HasProperty("_Smoothness")) ? defMat.GetFloat("_Smoothness") : 0.1f);
            }
            if (unlitSolidMat == null) {
                var s = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color");
                unlitSolidMat = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
                unlitSolidMat.color = solidColor;
                if (unlitSolidMat.HasProperty("_BaseColor")) unlitSolidMat.SetColor("_BaseColor", solidColor);
                if (unlitSolidMat.HasProperty("_Color")) unlitSolidMat.SetColor("_Color", solidColor);
            }
            if (depthMat == null) {
                var s = Shader.Find("Hidden/CunningDepthOnly")
                    ?? Shader.Find("Hidden/Universal Render Pipeline/DepthOnly")
                    ?? Shader.Find("Hidden/Internal-DepthOnly");
                if (s != null) depthMat = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (wireMat == null) {
                var s = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                wireMat = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
                if (wireMat.HasProperty("_SrcBlend")) wireMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                if (wireMat.HasProperty("_DstBlend")) wireMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                if (wireMat.HasProperty("_Cull")) wireMat.SetInt("_Cull", (int)CullMode.Off);
                if (wireMat.HasProperty("_ZWrite")) wireMat.SetInt("_ZWrite", 0);
                if (wireMat.HasProperty("_ZTest")) wireMat.SetInt("_ZTest", (int)CompareFunction.Always);
                wireMat.renderQueue = 4000; // overlay
            }
        }

        static void OnSceneGUI(SceneView sv) {
            if (sv == null || Event.current.type != EventType.Repaint) return;
            var dm = sv.cameraMode.drawMode;
            var cms = Object.FindObjectsByType<CunningMesh>(FindObjectsSortMode.None);

            // Normal modes: keep Unity draw modes intact, but ensure Cunning meshes are solid-only.
            if (!IsUnityWireMode(dm)) {
                RestoreAA(sv.camera);
                foreach (var cm in cms) {
                    if (cm == null) continue;
                    cm.displayMode = CunningMesh.DisplayMode.Solid;
                    cm.solidUnlit = (sv.cameraMode.name ?? "").ToLowerInvariant().Contains("unlit");
                    cm.ApplyDisplayMode();
                    var r0 = cm.GetComponent<Renderer>(); if (r0 != null) r0.enabled = true;
                }
                return;
            }

            ApplyAA4x(sv.camera);
            foreach (var cm in cms) {
                if (cm == null) continue;
                cm.displayMode = CunningMesh.DisplayMode.Solid; // keep triangle indices alive for custom solid draw
                cm.solidUnlit = (sv.cameraMode.name ?? "").ToLowerInvariant().Contains("unlit");
                cm.ApplyDisplayMode();
                var r = cm.GetComponent<Renderer>(); if (r != null) r.enabled = false; // block Unity's triangle wire overlay
                if (dm == DrawCameraMode.TexturedWire) DrawSolid(cm);
                else DrawDepth(cm); // wire-only needs depth to hide backfaces
                DrawWire(cm, sv.camera, dm == DrawCameraMode.Wireframe);
            }
        }

        static void ApplyAA4x(Camera cam) {
            if (prevAA == -2) prevAA = QualitySettings.antiAliasing;
            if (QualitySettings.antiAliasing != 4) QualitySettings.antiAliasing = 4;
            if (cam != null) cam.allowMSAA = true;
        }

        static void RestoreAA(Camera cam) {
            if (prevAA != -2 && QualitySettings.antiAliasing != prevAA) QualitySettings.antiAliasing = prevAA;
            if (cam != null) cam.allowMSAA = false;
        }

        static void DrawDepth(CunningMesh cm) {
            EnsureMats();
            if (depthMat == null) return;
            var mf = cm.GetComponent<MeshFilter>(); if (mf == null) return;
            var mesh = mf.sharedMesh; if (mesh == null || mesh.subMeshCount < 1) return;
            if (!depthMat.SetPass(0)) return;
            Graphics.DrawMeshNow(mesh, cm.transform.localToWorldMatrix, 0);
        }

        static void DrawSolid(CunningMesh cm) {
            EnsureMats();
            var mf = cm.GetComponent<MeshFilter>(); if (mf == null) return;
            var mesh = mf.sharedMesh; if (mesh == null || mesh.subMeshCount < 1) return;
            var r = cm.GetComponent<Renderer>();
            var mat = (r != null && r.sharedMaterials != null && r.sharedMaterials.Length > 0 && r.sharedMaterials[0] != null)
                ? r.sharedMaterials[0]
                : (cm.solidUnlit ? unlitSolidMat : litMat);
            if (mat == null || !mat.SetPass(0)) return;
            Graphics.DrawMeshNow(mesh, cm.transform.localToWorldMatrix, 0);
        }

        static void DrawWire(CunningMesh cm, Camera cam, bool pureWire) {
            EnsureMats();
            if (cam == null) return;
            var mf = cm.GetComponent<MeshFilter>(); if (mf == null) return;
            var mesh = mf.sharedMesh; if (mesh == null || mesh.subMeshCount < 2) return;
            var idx = cm.LineIndices; if (idx == null || idx.Length < 2) return;
            var v = mesh.vertices; if (v == null || v.Length == 0) return;
                var mtx = cm.transform.localToWorldMatrix;
            var pts = new Vector3[idx.Length];
            var eps = Mathf.Max(1e-5f, mesh.bounds.size.magnitude * 1e-4f);
            for (var i = 0; i < idx.Length; i++) { var vi = idx[i]; if ((uint)vi < (uint)v.Length) { var p = mtx.MultiplyPoint3x4(v[vi]); var d = cam.transform.position - p; if (d.sqrMagnitude > 1e-12f) p += d.normalized * eps; pts[i] = p; } }
            Handles.zTest = CompareFunction.LessEqual; // match Unity wireframe (no back/hidden lines)
            Handles.color = pureWire ? unityWireBlack : unityWireGrey;
            for (var i = 0; i + 1 < pts.Length; i += 2) Handles.DrawAAPolyLine(wireWidth, pts[i], pts[i + 1]);
            }
        }
    }



