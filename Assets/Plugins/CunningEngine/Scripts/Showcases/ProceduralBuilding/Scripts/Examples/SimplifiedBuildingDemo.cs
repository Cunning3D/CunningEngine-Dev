using UnityEngine;
using System.Collections.Generic;
using ProceduralToolkit.Buildings;

namespace DevStuffs
{
    /// <summary>
    /// 简化建筑生成系统演示脚本
    /// </summary>
    public class SimplifiedBuildingDemo : MonoBehaviour
    {
        [Header("生成控制")]
        public bool generateOnStart = true;
        public bool useExistingBuildings = false;
        public int buildingCount = 9;
        public float buildingSpacing = 20f;
        
        [Header("建筑参数")]
        [Range(3, 30)]
        public int minFloors = 5;
        [Range(3, 50)]
        public int maxFloors = 15;
        [Range(0.5f, 1.5f)]
        public float heightMultiplier = 1.0f;
        
        [Header("外观控制")]
        [Range(0, 1)]
        public float windowEmission = 0.2f;
        public Color windowEmissionColor = new Color(1f, 0.8f, 0.6f, 1f);
        
        [Header("屋顶设置")]
        public RoofType[] roofTypes;
        
        // 已生成的建筑列表
        private List<ProceduralBuilding> buildings = new List<ProceduralBuilding>();
        
        private void Start()
        {
            if (generateOnStart)
            {
                GenerateBuildings();
            }
        }
        
        /// <summary>
        /// 生成建筑
        /// </summary>
        [ContextMenu("生成建筑")]
        public void GenerateBuildings()
        {
            // 如果不使用现有建筑，则清除场景中的建筑
            if (!useExistingBuildings)
            {
                ClearBuildings();
            }
            
            // 初始化屋顶类型数组，如果未设置
            if (roofTypes == null || roofTypes.Length == 0)
            {
                roofTypes = new RoofType[] { RoofType.Flat, RoofType.Hipped, RoofType.Gabled };
            }
            
            // 计算网格布局尺寸
            int gridSize = Mathf.CeilToInt(Mathf.Sqrt(buildingCount));
            
            // 生成建筑
            for (int i = 0; i < buildingCount; i++)
            {
                // 计算网格位置
                int row = i / gridSize;
                int col = i % gridSize;
                
                // 添加一些随机偏移使布局更自然
                float xOffset = Random.Range(-2f, 2f);
                float zOffset = Random.Range(-2f, 2f);
                
                // 计算世界坐标
                float x = (col - gridSize / 2) * buildingSpacing + xOffset;
                float z = (row - gridSize / 2) * buildingSpacing + zOffset;
                
                // 创建建筑
                CreateBuilding(new Vector3(x, 0, z), i);
            }
        }
        
        /// <summary>
        /// 创建单个建筑
        /// </summary>
        private void CreateBuilding(Vector3 position, int index)
        {
            // 创建建筑游戏对象
            GameObject buildingObj = new GameObject($"Building_{index}");
            buildingObj.transform.parent = transform;
            buildingObj.transform.position = position;
            
            // 添加ProceduralBuilding组件
            ProceduralBuilding building = buildingObj.AddComponent<ProceduralBuilding>();
            
            // 设置为简化模式
            building.simplifiedMode = true;
            
            // 设置发光属性
            building.windowEmissions = windowEmission;
            building.windowEmissionColor = windowEmissionColor;
            
            // 设置楼层数量
            building.stagesCount = Random.Range(minFloors, maxFloors + 1);
            
            // 设置楼层高度和乘数
            building.stageHeightMultiplier = heightMultiplier * Random.Range(0.9f, 1.1f);
            
            // 随机使用固定高度
            building.useFixedHeight = Random.value > 0.7f;
            if (building.useFixedHeight)
            {
                building.fixedHeight = Random.Range(3.0f, 4.5f);
            }
            
            // 设置随机屋顶类型
            building.roofType = roofTypes[Random.Range(0, roofTypes.Length)];
            
            // 创建随机建筑平面形状
            GenerateBuildingShape(building);
            
            // 添加到建筑列表
            buildings.Add(building);
            
            // 触发建筑生成
            building.RecalculateBuilding();
        }
        
        /// <summary>
        /// 为建筑生成随机多边形形状
        /// </summary>
        private void GenerateBuildingShape(ProceduralBuilding building)
        {
            // 清除现有点位
            building.points.Clear();
            
            // 决定形状类型（矩形、多边形或L形）
            int shapeType = Random.Range(0, 3);
            
            // 形状基础大小
            float baseSize = Random.Range(5f, 10f);
            
            if (shapeType == 0) // 矩形
            {
                float width = baseSize * Random.Range(0.8f, 1.5f);
                float depth = baseSize * Random.Range(0.8f, 1.5f);
                
                // 添加4个角点
                building.AddBuildingPoint(new Vector3(-width/2, 0, -depth/2));
                building.AddBuildingPoint(new Vector3(width/2, 0, -depth/2));
                building.AddBuildingPoint(new Vector3(width/2, 0, depth/2));
                building.AddBuildingPoint(new Vector3(-width/2, 0, depth/2));
            }
            else if (shapeType == 1) // 多边形
            {
                // 随机点数（5-8）
                int pointCount = Random.Range(5, 9);
                
                // 生成多边形
                for (int i = 0; i < pointCount; i++)
                {
                    float angle = i * (360f / pointCount) * Mathf.Deg2Rad;
                    float radius = baseSize * Random.Range(0.8f, 1.2f);
                    
                    Vector3 point = new Vector3(
                        Mathf.Cos(angle) * radius,
                        0,
                        Mathf.Sin(angle) * radius
                    );
                    
                    building.AddBuildingPoint(point);
                }
            }
            else // L形
            {
                float width = baseSize * Random.Range(1.0f, 1.5f);
                float depth = baseSize * Random.Range(1.0f, 1.5f);
                float cutWidth = width * Random.Range(0.3f, 0.6f);
                float cutDepth = depth * Random.Range(0.3f, 0.6f);
                
                // 添加L形建筑的6个角点
                building.AddBuildingPoint(new Vector3(-width/2, 0, -depth/2));
                building.AddBuildingPoint(new Vector3(width/2, 0, -depth/2));
                building.AddBuildingPoint(new Vector3(width/2, 0, -depth/2 + cutDepth));
                building.AddBuildingPoint(new Vector3(-width/2 + cutWidth, 0, -depth/2 + cutDepth));
                building.AddBuildingPoint(new Vector3(-width/2 + cutWidth, 0, depth/2));
                building.AddBuildingPoint(new Vector3(-width/2, 0, depth/2));
            }
        }
        
        /// <summary>
        /// 清理已生成的建筑
        /// </summary>
        [ContextMenu("清除建筑")]
        public void ClearBuildings()
        {
            // 删除现有建筑
            foreach (ProceduralBuilding building in buildings)
            {
                if (building != null)
                {
                    DestroyImmediate(building.gameObject);
                }
            }
            
            // 清空列表
            buildings.Clear();
            
            // 查找场景中可能仍存在的其他建筑
            ProceduralBuilding[] remainingBuildings = FindObjectsOfType<ProceduralBuilding>();
            foreach (ProceduralBuilding building in remainingBuildings)
            {
                if (building.transform.parent == transform)
                {
                    DestroyImmediate(building.gameObject);
                }
            }
        }
        
        /// <summary>
        /// 更新所有建筑的窗户发光
        /// </summary>
        [ContextMenu("更新窗户发光")]
        public void UpdateWindowEmissions()
        {
            foreach (ProceduralBuilding building in buildings)
            {
                if (building != null)
                {
                    building.windowEmissions = windowEmission;
                    building.windowEmissionColor = windowEmissionColor;
                    building.RecalculateBuilding();
                }
            }
        }
        
        /// <summary>
        /// 随机建筑高度
        /// </summary>
        [ContextMenu("随机建筑高度")]
        public void RandomizeBuildingHeights()
        {
            foreach (ProceduralBuilding building in buildings)
            {
                if (building != null)
                {
                    building.stagesCount = Random.Range(minFloors, maxFloors + 1);
                    building.RecalculateBuilding();
                }
            }
        }
        
        /// <summary>
        /// 随机屋顶类型
        /// </summary>
        [ContextMenu("随机屋顶类型")]
        public void RandomizeRoofTypes()
        {
            // 确保屋顶类型数组已初始化
            if (roofTypes == null || roofTypes.Length == 0)
            {
                roofTypes = new RoofType[] { RoofType.Flat, RoofType.Hipped, RoofType.Gabled };
            }
            
            foreach (ProceduralBuilding building in buildings)
            {
                if (building != null)
                {
                    building.roofType = roofTypes[Random.Range(0, roofTypes.Length)];
                    building.RecalculateBuilding();
                }
            }
        }
    }
} 