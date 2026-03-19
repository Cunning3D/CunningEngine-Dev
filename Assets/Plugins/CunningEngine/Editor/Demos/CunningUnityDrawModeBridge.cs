using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CunningEngine.Editor.Demos {
    /// SceneView bridge for persistent Cunning draw modes.
    /// Uses triangle ribbons instead of MeshTopology.Lines so the wire overlay can anti-alias like normal mesh rendering.
    [InitializeOnLoad]
    public static class CunningUnityDrawModeBridge {
        sealed class WireOverlayCache {
            public Mesh mesh;
            public ulong dirtyId;
            public int sourceVertexCount;
            public int sourceLineCount;
        }

        static readonly Color UnityTexturedWireColor = new Color(0.22f, 0.22f, 0.22f, 1f);
        static readonly Color UnityWireframeColor = new Color(0f, 0f, 0f, 1f);
        const float UnityTexturedWireWidth = 2.6f;
        const float UnityPureWireWidth = 2.25f;
        static readonly Dictionary<int, WireOverlayCache> WireOverlayCaches = new Dictionary<int, WireOverlayCache>();
        static Material s_wireMaterial;
        static int s_previousAntiAliasing = -1;

        static CunningUnityDrawModeBridge() {
            SceneView.duringSceneGui += OnSceneGUI;
            Selection.selectionChanged += RepaintSceneViews;
            EditorApplication.hierarchyChanged += RepaintSceneViews;
            AssemblyReloadEvents.beforeAssemblyReload += CleanupCaches;
        }

        static void RepaintSceneViews() {
            SceneView.RepaintAll();
        }

        static void CleanupCaches() {
            foreach (var pair in WireOverlayCaches) {
                DestroyCacheMesh(pair.Value);
            }

            WireOverlayCaches.Clear();
        }

        static void OnSceneGUI(SceneView sceneView) {
            if (sceneView == null || Event.current.type != EventType.Repaint) {
                return;
            }

            bool useUnityPureWireframe = sceneView.cameraMode.drawMode == DrawCameraMode.Wireframe;
            bool useShadedOverlayMode = sceneView.cameraMode.drawMode == DrawCameraMode.Textured;
            bool wireOverlayEnabled = CunningEngineSettings.SelectionNgonWireframe;
            int wireMode = CunningSceneViewModeState.WireMode;
            var selectedObjects = Selection.gameObjects;
            var cunningMeshes = Object.FindObjectsByType<CunningMesh>(FindObjectsSortMode.None);
            bool wantsCustomWire = cunningMeshes.Length > 0 && (useUnityPureWireframe || (wireOverlayEnabled && wireMode != CunningSceneViewModeState.WireModeOff && useShadedOverlayMode));

            EnsureWireMaterial();
            CleanupDeadCaches();
            if (wantsCustomWire) {
                ApplySceneViewWireAntiAliasing(sceneView.camera);
            } else {
                RestoreSceneViewWireAntiAliasing(sceneView.camera);
            }

            for (int index = 0; index < cunningMeshes.Length; index++) {
                var cunningMesh = cunningMeshes[index];
                if (cunningMesh == null) {
                    continue;
                }

                cunningMesh.SetEditorSelectionHighlight(false, UnityTexturedWireColor);

                var renderer = cunningMesh.GetComponent<Renderer>();
                if (useUnityPureWireframe) {
                    if (renderer != null && renderer.enabled) {
                        renderer.enabled = false;
                    }

                    DrawNgonWireOverlay(cunningMesh, sceneView.camera, UnityWireframeColor, GetScaledLineWidth(UnityPureWireWidth));
                    continue;
                }

                if (renderer != null && !renderer.enabled) {
                    renderer.enabled = true;
                }

                if (!useShadedOverlayMode || !wireOverlayEnabled || wireMode == CunningSceneViewModeState.WireModeOff) {
                    continue;
                }

                bool shouldDraw = wireMode == CunningSceneViewModeState.WireModeAll
                    || IsSelectionHighlightTarget(cunningMesh.transform, selectedObjects);
                if (!shouldDraw) {
                    continue;
                }

                DrawNgonWireOverlay(cunningMesh, sceneView.camera, UnityTexturedWireColor, GetScaledLineWidth(UnityTexturedWireWidth));
            }
        }

        static void ApplySceneViewWireAntiAliasing(Camera camera) {
            if (s_previousAntiAliasing < 0) {
                s_previousAntiAliasing = QualitySettings.antiAliasing;
            }

            int targetAa = Mathf.Max(QualitySettings.antiAliasing, 4);
            if (QualitySettings.antiAliasing != targetAa) {
                QualitySettings.antiAliasing = targetAa;
            }

            if (camera != null) {
                camera.allowMSAA = true;
            }
        }

        static void RestoreSceneViewWireAntiAliasing(Camera camera) {
            if (camera != null) {
                camera.allowMSAA = false;
            }

            if (s_previousAntiAliasing >= 0 && QualitySettings.antiAliasing != s_previousAntiAliasing) {
                QualitySettings.antiAliasing = s_previousAntiAliasing;
            }

            s_previousAntiAliasing = -1;
        }

        static void EnsureWireMaterial() {
            if (s_wireMaterial != null) {
                return;
            }

            var shader = Shader.Find("Hidden/CunningEditorNgonWire");
            if (shader == null) {
                return;
            }

            s_wireMaterial = new Material(shader) {
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        static float GetScaledLineWidth(float baseWidth) {
            return Mathf.Max(1.0f, baseWidth * EditorGUIUtility.pixelsPerPoint);
        }

        static void DrawNgonWireOverlay(CunningMesh cunningMesh, Camera camera, Color wireColor, float lineWidth) {
            if (cunningMesh == null || camera == null || s_wireMaterial == null) {
                return;
            }

            var meshFilter = cunningMesh.GetComponent<MeshFilter>();
            var sourceMesh = meshFilter != null ? meshFilter.sharedMesh : null;
            var lines = cunningMesh.LineIndices;
            if (sourceMesh == null || lines == null || lines.Length < 2) {
                return;
            }

            var overlayMesh = GetOrBuildWireOverlayMesh(cunningMesh, sourceMesh, lines);
            if (overlayMesh == null || overlayMesh.vertexCount == 0) {
                return;
            }

            s_wireMaterial.SetColor("_Color", wireColor);
            s_wireMaterial.SetFloat("_LineWidth", lineWidth);
            Graphics.DrawMesh(
                overlayMesh,
                cunningMesh.transform.localToWorldMatrix,
                s_wireMaterial,
                cunningMesh.gameObject.layer,
                camera,
                0);
        }

        static Mesh GetOrBuildWireOverlayMesh(CunningMesh cunningMesh, Mesh sourceMesh, int[] lines) {
            int instanceId = cunningMesh.GetInstanceID();
            ulong dirtyId = cunningMesh.CurrentHandle != 0 ? NativeMethods.cunning_geo_get_dirty_id(cunningMesh.CurrentHandle) : 0;
            int sourceVertexCount = sourceMesh.vertexCount;
            int sourceLineCount = lines != null ? lines.Length : 0;

            if (!WireOverlayCaches.TryGetValue(instanceId, out var cache) || cache == null) {
                cache = new WireOverlayCache();
                WireOverlayCaches[instanceId] = cache;
            }

            if (cache.mesh != null
                && cache.dirtyId == dirtyId
                && cache.sourceVertexCount == sourceVertexCount
                && cache.sourceLineCount == sourceLineCount) {
                return cache.mesh;
            }

            DestroyCacheMesh(cache);
            cache.mesh = BuildWireOverlayMesh(sourceMesh, lines);
            cache.dirtyId = dirtyId;
            cache.sourceVertexCount = sourceVertexCount;
            cache.sourceLineCount = sourceLineCount;
            return cache.mesh;
        }

        static Mesh BuildWireOverlayMesh(Mesh sourceMesh, int[] lines) {
            if (sourceMesh == null || lines == null || lines.Length < 2) {
                return null;
            }

            var sourceVertices = sourceMesh.vertices;
            if (sourceVertices == null || sourceVertices.Length == 0) {
                return null;
            }

            var vertices = new List<Vector3>(lines.Length * 2);
            var uv0 = new List<Vector2>(lines.Length * 2);
            var uv1 = new List<Vector4>(lines.Length * 2);
            var triangles = new List<int>(lines.Length * 3);

            for (int lineIndex = 0; lineIndex + 1 < lines.Length; lineIndex += 2) {
                int indexA = lines[lineIndex];
                int indexB = lines[lineIndex + 1];
                if ((uint)indexA >= (uint)sourceVertices.Length || (uint)indexB >= (uint)sourceVertices.Length || indexA == indexB) {
                    continue;
                }

                Vector3 pointA = sourceVertices[indexA];
                Vector3 pointB = sourceVertices[indexB];
                int baseVertex = vertices.Count;

                vertices.Add(pointA);
                vertices.Add(pointA);
                vertices.Add(pointB);
                vertices.Add(pointB);

                uv0.Add(new Vector2(0f, -1f));
                uv0.Add(new Vector2(0f, 1f));
                uv0.Add(new Vector2(1f, -1f));
                uv0.Add(new Vector2(1f, 1f));

                uv1.Add(new Vector4(pointB.x, pointB.y, pointB.z, 1f));
                uv1.Add(new Vector4(pointB.x, pointB.y, pointB.z, 1f));
                uv1.Add(new Vector4(pointA.x, pointA.y, pointA.z, 1f));
                uv1.Add(new Vector4(pointA.x, pointA.y, pointA.z, 1f));

                triangles.Add(baseVertex + 0);
                triangles.Add(baseVertex + 1);
                triangles.Add(baseVertex + 2);
                triangles.Add(baseVertex + 2);
                triangles.Add(baseVertex + 1);
                triangles.Add(baseVertex + 3);
            }

            if (vertices.Count == 0) {
                return null;
            }

            var overlayMesh = new Mesh {
                name = sourceMesh.name + "_CunningWireOverlay",
                hideFlags = HideFlags.HideAndDontSave,
            };

            if (vertices.Count > 65000) {
                overlayMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            overlayMesh.SetVertices(vertices);
            overlayMesh.SetUVs(0, uv0);
            overlayMesh.SetUVs(1, uv1);
            overlayMesh.SetTriangles(triangles, 0, true);

            var bounds = sourceMesh.bounds;
            bounds.Expand(bounds.size.magnitude * 0.02f + 0.05f);
            overlayMesh.bounds = bounds;
            return overlayMesh;
        }

        static void CleanupDeadCaches() {
            if (WireOverlayCaches.Count == 0) {
                return;
            }

            var staleKeys = ListPool<int>.Get();
            foreach (var pair in WireOverlayCaches) {
                if (EditorUtility.InstanceIDToObject(pair.Key) == null) {
                    staleKeys.Add(pair.Key);
                }
            }

            for (int index = 0; index < staleKeys.Count; index++) {
                int key = staleKeys[index];
                if (WireOverlayCaches.TryGetValue(key, out var cache)) {
                    DestroyCacheMesh(cache);
                    WireOverlayCaches.Remove(key);
                }
            }

            ListPool<int>.Release(staleKeys);
        }

        static void DestroyCacheMesh(WireOverlayCache cache) {
            if (cache?.mesh == null) {
                return;
            }

            if (Application.isPlaying) {
                Object.Destroy(cache.mesh);
            } else {
                Object.DestroyImmediate(cache.mesh);
            }

            cache.mesh = null;
        }

        static bool IsSelectionHighlightTarget(Transform target, GameObject[] selectedObjects) {
            if (target == null || selectedObjects == null || selectedObjects.Length == 0) {
                return false;
            }

            for (int index = 0; index < selectedObjects.Length; index++) {
                var selected = selectedObjects[index];
                if (selected == null) {
                    continue;
                }

                var selectedTransform = selected.transform;
                if (selectedTransform == null) {
                    continue;
                }

                if (target == selectedTransform || target.IsChildOf(selectedTransform)) {
                    return true;
                }
            }

            return false;
        }
    }

    static class ListPool<T> {
        static readonly Stack<List<T>> Pool = new Stack<List<T>>();

        public static List<T> Get() {
            return Pool.Count > 0 ? Pool.Pop() : new List<T>();
        }

        public static void Release(List<T> list) {
            if (list == null) {
                return;
            }

            list.Clear();
            Pool.Push(list);
        }
    }
}
