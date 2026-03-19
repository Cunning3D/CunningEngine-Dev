using UnityEngine;
using System.Collections.Generic;
using ProceduralToolkit.Buildings;

namespace DevStuffs
{
    /// <summary>
    /// 演示如何使用基于ProceduralToolkit的简化建筑生成系统
    /// </summary>
    public class PTSimplifiedBuildingExample : MonoBehaviour
    {
        [Header("生成设置")]
        public int buildingCount = 5;
        public float gridSize = 20f;
        
        [Header("建筑参数")]
        [Range(1, 20)]
        public int minFloors = 3;
        [Range(1, 50)]
        public int maxFloors = 15;
        
        [Range(0.5f, 5f)]
        public float minSize = 8f;
        [Range(0.5f, 30f)]
        public float maxSize = 16f;
        
        [Range(2.0f, 5.0f)]
        public float floorHeight = 3.5f;
        
        [Header("屋顶设置")]
        public RoofType[] roofTypes;
        
        private void Start()
        {
            GenerateBuildings();
        }
        
        [ContextMenu("生成建筑")]
        public void GenerateBuildings()
        {
            // 清理现有建筑
            foreach (Transform child in transform)
            {
                Destroy(child.gameObject);
            }
            
            // 初始化屋顶类型数组，如果未设置
            if (roofTypes == null || roofTypes.Length == 0)
            {
                roofTypes = new RoofType[] { RoofType.Flat, RoofType.Hipped, RoofType.Gabled };
            }
            
            // 生成网格状的建筑群
            int gridDimension = Mathf.CeilToInt(Mathf.Sqrt(buildingCount));
            
            for (int i = 0; i < buildingCount; i++)
            {
                int row = i / gridDimension;
                int col = i % gridDimension;
                
                // 计算位置，添加一些随机偏移
                float xPos = col * gridSize + Random.Range(-gridSize * 0.2f, gridSize * 0.2f);
                float zPos = row * gridSize + Random.Range(-gridSize * 0.2f, gridSize * 0.2f);
                
                Vector3 position = new Vector3(xPos, 0, zPos);
                
                // 创建建筑游戏对象
                GameObject buildingObj = new GameObject($"Building_{i}");
                buildingObj.transform.parent = transform;
                buildingObj.transform.position = position;
                
                // 添加ProceduralBuilding组件
                ProceduralBuilding building = buildingObj.AddComponent<ProceduralBuilding>();
                
                // 设置为简化模式
                building.simplifiedMode = true;
                
                // 设置楼层数量
                building.stagesCount = Random.Range(minFloors, maxFloors + 1);
                
                // 设置楼层高度
                building.simplifiedFloorHeight = floorHeight;
                building.stageHeightMultiplier = Random.Range(0.8f, 1.2f);
                
                // 随机是否使用固定高度
                building.useFixedHeight = Random.value > 0.7f;
                if (building.useFixedHeight)
                {
                    building.fixedHeight = Random.Range(3.0f, 4.5f);
                }
                
                // 创建随机多边形建筑基础
                int pointCount = Random.Range(3, 7);
                float size = Random.Range(minSize, maxSize);
                
                // 清空现有点位
                building.points.Clear();
                
                // 创建点位
                for (int p = 0; p < pointCount; p++)
                {
                    float angle = (p * (360f / pointCount)) * Mathf.Deg2Rad;
                    // 添加一些随机性，使建筑形状不规则
                    float radius = size * (1f + Random.Range(-0.3f, 0.3f));
                    Vector3 point = new Vector3(
                        Mathf.Cos(angle) * radius,
                        0,
                        Mathf.Sin(angle) * radius
                    );
                    
                    building.AddBuildingPoint(point);
                }
                
                // 执行建筑生成
                building.RecalculateBuilding();
            }
        }
    }
} 