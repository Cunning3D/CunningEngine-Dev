using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Miao.RoadSystem.Culling
{
    public struct FrustumCullingJob : IJobParallelFor
    {
        public NativeArray<Bounds> worldBoundsNA;
        public NativeArray<byte> cullResultsNA;


        public Plane plane0, plane1, plane2, plane3, plane4, plane5;
        public Vector3 cameraPosition;

        public void Execute(int i)
        {
            Bounds bounds = worldBoundsNA[i];
            byte cullFlag = CullUtils.TestFrustumAABB(ref plane0, ref plane1, ref plane2, ref plane3, ref plane4, ref plane5, ref bounds);


            float distance = Vector3.SqrMagnitude(cameraPosition - bounds.center);


            if (distance > 600f * 600F)
            {
                cullFlag = CullFlags.CULLED;
            }

            cullResultsNA[i] = cullFlag;
        }
    }

    [ExecuteInEditMode]
    public class RoadCullingMultithreaded : MonoBehaviour
    {
        private NativeArray<Bounds> worldBoundsNA;
        private NativeArray<byte> cullResultsNA;

        private Bounds[] worldBounds;
        private byte[] cullResults;
        private Plane[] frustumPlanes = new Plane[6];
        private List<GameObject> targetObjects;
        private Camera sceneViewCamera;

        private JobHandle cullingJobHandle;

        private void OnEnable()
        {

            targetObjects = new List<GameObject>();
            Transform root = GameObject.Find("Root")?.transform;

            if (root != null)
            {
                Transform roadLane = root.Find("Road Lane");
                Transform roadIntersection = root.Find("Road Intersection");

                if (roadLane != null)
                {
                    foreach (Transform child in roadLane)
                    {
                        BoxCollider boxCollider = child.GetComponent<BoxCollider>();
                        if (boxCollider != null)
                        {
                            targetObjects.Add(child.gameObject);
                        }
                    }
                }

                if (roadIntersection != null)
                {
                    foreach (Transform child in roadIntersection)
                    {
                        BoxCollider boxCollider = child.GetComponent<BoxCollider>();
                        if (boxCollider != null)
                        {
                            targetObjects.Add(child.gameObject);
                        }
                    }
                }
            }

            int count = targetObjects.Count;
            worldBoundsNA = new NativeArray<Bounds>(count, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            cullResultsNA = new NativeArray<byte>(count, Allocator.Persistent, NativeArrayOptions.ClearMemory);

            for (int i = 0; i < count; ++i)
            {
                BoxCollider boxCollider = targetObjects[i].GetComponent<BoxCollider>();
                if (boxCollider != null)
                {
                    worldBoundsNA[i] = boxCollider.bounds;
                }
            }


            worldBounds = new Bounds[count];
            cullResults = new byte[count];
        }

        private void OnDisable()
        {

            cullingJobHandle.Complete();

            if (worldBoundsNA.IsCreated)
            {
                worldBoundsNA.Dispose();
            }
            if (cullResultsNA.IsCreated)
            {
                cullResultsNA.Dispose();
            }

            if (sceneViewCamera != null)
            {
                DestroyImmediate(sceneViewCamera.gameObject);
            }
        }

        private void UpdateEditorCamera()
        {

            if (sceneViewCamera == null)
            {
                GameObject go = new GameObject("SceneViewCamera");
                go.hideFlags = HideFlags.HideAndDontSave;
                sceneViewCamera = go.AddComponent<Camera>();
                sceneViewCamera.enabled = false; // This camera is only used for culling calculation
            }

#if UNITY_EDITOR
            if (SceneView.lastActiveSceneView != null)
            {
                sceneViewCamera.transform.position = SceneView.lastActiveSceneView.camera.transform.position;
                sceneViewCamera.transform.rotation = SceneView.lastActiveSceneView.camera.transform.rotation;
                sceneViewCamera.orthographic = SceneView.lastActiveSceneView.camera.orthographic;
                sceneViewCamera.fieldOfView = SceneView.lastActiveSceneView.camera.fieldOfView;
                sceneViewCamera.nearClipPlane = SceneView.lastActiveSceneView.camera.nearClipPlane;
                sceneViewCamera.farClipPlane = SceneView.lastActiveSceneView.camera.farClipPlane;
                sceneViewCamera.orthographicSize = SceneView.lastActiveSceneView.camera.orthographicSize;
            }
#endif
        }

        public void Update()
        {
            // Update editor camera every frame to match the SceneView camera
            UpdateEditorCamera();

            // Wait for the previously scheduled job to be completed before copying data to managed side
            if (cullingJobHandle.IsCompleted)
            {
                // Make sure we can access the native data before copying
                cullingJobHandle.Complete();
                worldBoundsNA.CopyTo(worldBounds);
                cullResultsNA.CopyTo(cullResults);

                GeometryUtility.CalculateFrustumPlanes(sceneViewCamera, frustumPlanes);
                FrustumCullingJob job = new FrustumCullingJob()
                {
                    worldBoundsNA = this.worldBoundsNA,
                    cullResultsNA = this.cullResultsNA,
                    plane0 = frustumPlanes[0],
                    plane1 = frustumPlanes[1],
                    plane2 = frustumPlanes[2],
                    plane3 = frustumPlanes[3],
                    plane4 = frustumPlanes[4],
                    plane5 = frustumPlanes[5],
                    cameraPosition = sceneViewCamera.transform.position
                };


                cullingJobHandle = job.Schedule(worldBoundsNA.Length, 200);
            }

            for (int i = 0; i < targetObjects.Count; ++i)
            {
                GameObject go = targetObjects[i];
                byte cullFlag = cullResults[i];

                if (cullFlag == CullFlags.CULLED)
                {
                    go.SetActive(false);
                }
                else
                {
                    go.SetActive(true);
                }
            }
        }

        private void OnDrawGizmos()
        {
            if (worldBounds == null || cullResults == null)
                return;

            for (int i = 0; i < worldBounds.Length; ++i)
            {

                if (cullResults[i] == CullFlags.CULLED)
                    continue;

                Bounds bounds = worldBounds[i];
                byte cullFlag = cullResults[i];

                Gizmos.color = cullFlag == CullFlags.CULLED ? Color.red :
                            cullFlag == CullFlags.PARTIALLY_VISIBLE ? Color.yellow : Color.green;
                Gizmos.DrawWireCube(bounds.center, bounds.size);
            }
        }
    }
}