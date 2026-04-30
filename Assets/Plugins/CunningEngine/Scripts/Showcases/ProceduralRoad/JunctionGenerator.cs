#if UNITY_EDITOR
using UnityEngine;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Splines.Examples;
using UnityEditor;

namespace Unity.Splines.Examples
{
    public static class JunctionGenerator 
    {
        private static readonly bool VerboseRoadRefreshLogging = false;
        private const string ZebraCrossingPrefabPath = "Assets/ArtResources/PcgAsset/Prefabs/Pcg_Sidewalk.prefab";
        private static GameObject s_ZebraCrossingPrefab;
        private static Bounds s_ZebraCrossingPrefabBounds;
        private static bool s_HasZebraCrossingPrefabBounds;

        public static GameObject CreateJunction(List<(LoftRoadBehaviour road, int splineIndex, int knotIndex)> connections)
        {
            if (connections.Count < 2)
            {
                Debug.LogWarning("需要至少两个连接点来创建路口");
                return null;
            }
            
            // 查找或创建Road_Data对象
            GameObject roadDataRoot = GameObject.Find("Road_Data");
            if (roadDataRoot == null)
            {
                roadDataRoot = new GameObject("Road_Data");
                Debug.Log("已创建Road_Data对象");
            }
            
            // 创建Junction对象
            var junctionObj = new GameObject("Road_Junction");
            var junctionData = junctionObj.AddComponent<JunctionData>();
            
            // 设置Junction对象为Road_Data的子对象
            junctionObj.transform.SetParent(roadDataRoot.transform, true);
            
            // 添加连接信息
            foreach(var conn in connections)
            {
                if (conn.road != null)
                {
                    long markerId = conn.road.EnsureJunctionMarker(conn.splineIndex, conn.knotIndex, junctionData);
                    float cachedCurveU = 0f;
                    int cachedPreferredKnotIndex = conn.knotIndex;
                    if (conn.road.TryGetRoadMarker(markerId, out var marker))
                    {
                        cachedCurveU = marker.curveU;
                        cachedPreferredKnotIndex = marker.preferredKnotIndex;
                    }

                    junctionData.connectedRoads.Add(new JunctionData.ConnectedRoad
                    {
                        roadBehaviour = conn.road,
                        splineIndex = conn.splineIndex,
                        knotIndex = conn.knotIndex,
                        markerId = markerId,
                        cachedCurveU = cachedCurveU,
                        cachedPreferredKnotIndex = cachedPreferredKnotIndex,
                        width = conn.road.RoadExtensionDatas != null && conn.road.RoadExtensionDatas.Count > conn.splineIndex 
                            ? conn.road.RoadExtensionDatas[conn.splineIndex].laneWidth.DefaultValue * 
                              (conn.road.RoadExtensionDatas[conn.splineIndex].leftLaneCount + 
                               conn.road.RoadExtensionDatas[conn.splineIndex].rightLaneCount)
                            : 5f // 默认宽度
                    });
                    
                    // 将Junction添加到道路的连接列表中
                    if (!conn.road.connectedJunctions.Contains(junctionData))
                    {
                        conn.road.connectedJunctions.Add(junctionData);
                    }
                }
            }
            
            // 设置位置并生成网格
            junctionObj.transform.position = junctionData.GetJunctionCenter();
            junctionData.UpdateJunctionMesh();
            
            // 为每个连接的道路添加斑马线
            UpdateZebraCrossings(junctionData);
            
            // 强制所有连接的道路重新生成车道数据，确保关联到新路口
            RefreshRoadLaneData(connections, junctionData);
            
            // 在创建完路口后立即刷新所有连接道路的数据
            RefreshJunctionRoadData(junctionData);
            
            Debug.Log($"成功创建路口: {junctionObj.name}，连接道路数量: {connections.Count}");
            
            return junctionObj;
        }

        public static GameObject CreatePreviewJunction(
            List<(LoftRoadBehaviour road, int splineIndex, int knotIndex)> connections,
            Material previewMaterial,
            int groupIndex = -1)
        {
            if (connections == null || connections.Count < 2)
            {
                Debug.LogWarning("需要至少两个连接点来创建预览路口");
                return null;
            }

            string junctionName = groupIndex >= 0 ? $"Road_Junction_Preview_{groupIndex}" : "Road_Junction_Preview";
            var junctionObj = new GameObject(junctionName);
            junctionObj.hideFlags = HideFlags.DontSave;
            var previewMarker = junctionObj.AddComponent<PreviewObjectMarker>();
            previewMarker.previewType = PreviewObjectType.Junction;
            previewMarker.groupIndex = groupIndex;

            var junctionData = junctionObj.AddComponent<JunctionData>();
            junctionData.SetPreviewMode(true);

            Transform previewParent = null;
            foreach (var conn in connections)
            {
                if (conn.road == null)
                    continue;

                previewParent = conn.road.transform;
                break;
            }

            if (previewParent != null)
            {
                junctionObj.transform.SetParent(previewParent, true);
            }
            else
            {
                GameObject roadDataRoot = GameObject.Find("Road_Data");
                if (roadDataRoot == null)
                {
                    roadDataRoot = new GameObject("Road_Data");
                    Debug.Log("已创建Road_Data对象");
                }

                junctionObj.transform.SetParent(roadDataRoot.transform, true);
            }

            foreach (var conn in connections)
            {
                if (conn.road == null)
                    continue;

                float cachedCurveU = 0f;
                int cachedPreferredKnotIndex = conn.knotIndex;
                if (conn.road.Container != null
                    && conn.splineIndex >= 0
                    && conn.splineIndex < conn.road.Container.Splines.Count
                    && conn.road.Container.Splines[conn.splineIndex] != null
                    && conn.road.Container.Splines[conn.splineIndex].Count > 0)
                {
                    cachedCurveU = LoftRoadBehaviour.ComputeKnotCurveU(
                        conn.road.Container.Splines[conn.splineIndex],
                        conn.knotIndex);
                    cachedPreferredKnotIndex = Mathf.Clamp(conn.knotIndex, 0, conn.road.Container.Splines[conn.splineIndex].Count - 1);
                }

                junctionData.connectedRoads.Add(new JunctionData.ConnectedRoad
                {
                    roadBehaviour = conn.road,
                    splineIndex = conn.splineIndex,
                    knotIndex = conn.knotIndex,
                    markerId = 0,
                    cachedCurveU = cachedCurveU,
                    cachedPreferredKnotIndex = cachedPreferredKnotIndex,
                    width = conn.road.RoadExtensionDatas != null && conn.road.RoadExtensionDatas.Count > conn.splineIndex
                        ? conn.road.RoadExtensionDatas[conn.splineIndex].laneWidth.DefaultValue *
                          (conn.road.RoadExtensionDatas[conn.splineIndex].leftLaneCount +
                           conn.road.RoadExtensionDatas[conn.splineIndex].rightLaneCount)
                        : 5f
                });
            }

            junctionObj.transform.position = junctionData.GetJunctionCenter();
            junctionData.UpdateJunctionMesh();

            return junctionObj;
        }
        
        public static void UpdateAllJunctions()
        {
            var junctions = Object.FindObjectsByType<JunctionData>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var roadRefreshTargets = new Dictionary<LoftRoadBehaviour, HashSet<int>>();
            foreach (var junction in junctions)
            {
                if (junction != null && !junction.IsPreview)
                {
                    junction.UpdateJunctionMesh();
                    
                    // 更新斑马线
                    UpdateZebraCrossings(junction);
                    
                    // 累积需要刷新的道路，最后统一批量刷新
                    CollectRoadRefreshTargets(junction, roadRefreshTargets);
                }
            }

            RefreshRoadsForJunction(roadRefreshTargets, null);
        }
        
        // 更新路口的斑马线
        public static void UpdateZebraCrossings(JunctionData junctionData)
        {
            if (junctionData == null || junctionData.IsPreview)
                return;
                
            // 查找现有的斑马线父物体
            Transform zebraCrossingsParent = junctionData.transform.Find("ZebraCrossings");
            
            // 如果存在，则删除所有子物体
            if (zebraCrossingsParent != null)
            {
                for (int i = zebraCrossingsParent.childCount - 1; i >= 0; i--)
                {
                    GameObject child = zebraCrossingsParent.GetChild(i).gameObject;
                    // 在编辑模式下使用EditorApplication.delayCall来延迟销毁
                    if (!Application.isPlaying)
                    {
                        // 隐藏对象避免在销毁前看到
                        child.SetActive(false);
                        
                        // 延迟销毁
                        EditorApplication.delayCall += () => {
                            if (child != null)
                            {
                                GameObject.DestroyImmediate(child);
                            }
                        };
                    }
                    else
                    {
                        // 在运行模式使用正常的Destroy
                        GameObject.Destroy(child);
                    }
                }
            }
            else
            {
                // 如果不存在，则创建新的父物体
                GameObject newParent = new GameObject("ZebraCrossings");
                newParent.transform.SetParent(junctionData.transform);
                newParent.transform.localPosition = Vector3.zero;
                zebraCrossingsParent = newParent.transform;
            }
            
            if (!TryGetZebraCrossingPrefab(out GameObject zebraCrossingPrefab, out Bounds prefabBounds))
            {
                return;
            }
            
            Vector3 junctionCenter = junctionData.GetJunctionCenter();
            
            // 为每个连接的道路添加斑马线
            for (int i = 0; i < junctionData.connectedRoads.Count; i++)
            {
                var road = junctionData.connectedRoads[i];
                var edgePoints = road.GetEdgePoints(junctionCenter);
                
                // 计算道路宽度
                float roadWidth = Vector3.Distance(edgePoints.point0, edgePoints.point1);
                
                // 计算斑马线的基础缩放比例
                float baseScaleX = roadWidth / prefabBounds.size.x;
                
                // 应用JunctionData中的自定义缩放系数
                float finalScaleX = baseScaleX * junctionData.zebraCrossingScaleX;
                float finalScaleZ = junctionData.zebraCrossingScaleZ;
                
                // 计算斑马线的位置（距离路口边缘一定距离）
                float distanceFromJunction = 2.0f; // 可以根据需要调整
                Vector3 zebraCrossingPosition = road.GetConnectionPoint() + edgePoints.normal * distanceFromJunction;
                
                // 获取道路的网格高度偏移
                float meshOffset = road.roadBehaviour.RoadExtensionDatas[road.splineIndex].meshOffset;
                
                // 创建斑马线实例
                GameObject zebraCrossing = GameObject.Instantiate(zebraCrossingPrefab, zebraCrossingPosition, Quaternion.identity);
                zebraCrossing.name = $"ZebraCrossing_{i}";
                zebraCrossing.transform.SetParent(zebraCrossingsParent);
                
                // 设置斑马线的旋转，使其与道路垂直
                Quaternion rotation = Quaternion.LookRotation(edgePoints.normal);
                zebraCrossing.transform.rotation = rotation;
                
                // 设置斑马线的缩放，应用自定义缩放系数
                zebraCrossing.transform.localScale = new Vector3(finalScaleX, 1.0f, finalScaleZ);
                
                // 应用网格高度偏移，仅比路面抬高0.1单位，避免z-fighting
                zebraCrossing.transform.position = new Vector3(
                    zebraCrossingPosition.x,
                    zebraCrossingPosition.y + meshOffset + 0.1f,
                    zebraCrossingPosition.z
                );
            }
        }
        
        // 刷新当前路口所有连接道路的车道数据
        private static void RefreshJunctionRoadData(JunctionData junction)
        {
            if (junction == null) return;

            var refreshTargets = new Dictionary<LoftRoadBehaviour, HashSet<int>>();
            CollectRoadRefreshTargets(junction, refreshTargets);
            RefreshRoadsForJunction(refreshTargets, junction);
        }

        private static void CollectRoadRefreshTargets(JunctionData junction, Dictionary<LoftRoadBehaviour, HashSet<int>> refreshTargets)
        {
            if (junction == null || refreshTargets == null)
            {
                return;
            }

            for (int index = 0; index < junction.connectedRoads.Count; index++)
            {
                var conn = junction.connectedRoads[index];
                if (conn?.roadBehaviour == null)
                {
                    continue;
                }

                if (!refreshTargets.TryGetValue(conn.roadBehaviour, out var splineIndices))
                {
                    splineIndices = new HashSet<int>();
                    refreshTargets.Add(conn.roadBehaviour, splineIndices);
                }

                splineIndices.Add(conn.splineIndex);
            }
        }

        private static bool TryGetZebraCrossingPrefab(out GameObject zebraCrossingPrefab, out Bounds prefabBounds)
        {
            if (s_ZebraCrossingPrefab == null)
            {
                s_ZebraCrossingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ZebraCrossingPrefabPath);
                s_HasZebraCrossingPrefabBounds = false;
            }

            zebraCrossingPrefab = s_ZebraCrossingPrefab;
            prefabBounds = default;
            if (zebraCrossingPrefab == null)
            {
                Debug.LogError("无法加载斑马线prefab");
                return false;
            }

            if (!s_HasZebraCrossingPrefabBounds)
            {
                MeshFilter[] meshFilters = zebraCrossingPrefab.GetComponentsInChildren<MeshFilter>();
                if (meshFilters.Length == 0)
                {
                    Debug.LogWarning("斑马线prefab没有MeshFilter组件");
                    return false;
                }

                s_ZebraCrossingPrefabBounds = meshFilters[0].sharedMesh.bounds;
                for (int i = 1; i < meshFilters.Length; i++)
                {
                    if (meshFilters[i].sharedMesh != null)
                    {
                        s_ZebraCrossingPrefabBounds.Encapsulate(meshFilters[i].sharedMesh.bounds);
                    }
                }

                s_HasZebraCrossingPrefabBounds = true;
            }

            prefabBounds = s_ZebraCrossingPrefabBounds;
            return true;
        }

        private static void RefreshRoadsForJunction(Dictionary<LoftRoadBehaviour, HashSet<int>> refreshTargets, JunctionData junction)
        {
            if (refreshTargets == null || refreshTargets.Count == 0)
            {
                return;
            }

            using (LoftRoadBehaviour.SuppressConnectedJunctionUpdatesScope())
            {
                foreach (var pair in refreshTargets)
                {
                    LoftRoadBehaviour road = pair.Key;
                    if (road == null)
                    {
                        continue;
                    }

                    foreach (int splineIndex in pair.Value)
                    {
                        road.MarkSplineDownstreamDirty(splineIndex);
                    }

                    road.LoftAllRoads();

                    if (junction != null)
                    {
                        bool roadHasJunction = road.connectedJunctions.Contains(junction);
                        if (!roadHasJunction)
                        {
                            road.connectedJunctions.Add(junction);
                        }
                    }

                    if (VerboseRoadRefreshLogging)
                    {
                        string junctionLabel = junction != null ? junction.GetInstanceID().ToString() : "batched";
                        Debug.Log($"刷新道路 {road.name} 的车道/采样数据，关联路口ID: {junctionLabel}");
                    }
                }
            }
        }
        
        // 刷新所有连接道路的车道数据，确保正确关联到路口
        private static void RefreshRoadLaneData(List<(LoftRoadBehaviour road, int splineIndex, int knotIndex)> connections, JunctionData junction)
        {
            var refreshTargets = new Dictionary<LoftRoadBehaviour, HashSet<int>>();
            foreach (var conn in connections)
            {
                if (conn.road == null)
                {
                    continue;
                }

                if (!refreshTargets.TryGetValue(conn.road, out var splineIndices))
                {
                    splineIndices = new HashSet<int>();
                    refreshTargets.Add(conn.road, splineIndices);
                }

                splineIndices.Add(conn.splineIndex);
            }

            RefreshRoadsForJunction(refreshTargets, junction);
        }
    }
}
#endif 
