using System.Collections.Generic;
using UnityEngine;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
using System;
using ProceduralToolkit;
using ProceduralToolkit.Buildings;
using Random = UnityEngine.Random;
using Object = UnityEngine.Object;

namespace DevStuffs
{
/// <summary>
/// Main class for calculate procedural buiilding at editor or runtime
/// </summary>
[ExecuteAlways,SelectionBase]
public class ProceduralBuilding : MonoBehaviour
{

    //Used for calculate different building types
    public BuildPreset preset;

    //Current stage data
    private List<ProceduralStageData> stagesData = new List<ProceduralStageData>();

    [SerializeField,HideInInspector]public List<Vector3> points = new List<Vector3>();
    
    // 控制编辑器中点编辑界面的显示状态
    [HideInInspector]public bool showPointsEditor = false;
    
    // 添加对点的直接管理方法
    public void AddBuildingPoint(Vector3 position, int insertAt = -1)
    {
        // 记录添加前的点数，用于后续只更新新点对应的边
        int originalPointCount = points.Count;
        
        // 将世界坐标转换为局部坐标
        Vector3 localPosition = transform.InverseTransformPoint(position);
        
        // 添加点（以局部坐标存储）
        if (insertAt == -1)
            points.Add(localPosition);
        else
            points.Insert(insertAt, localPosition);
            
        // 只为新点创建边，而不重新计算所有边
        isDirty = true;
        
        // 通知需要更新但不立即进行全量重建
        rebuildInUpdate = true;
    }
    
    internal bool isDirty;
    public void SetDirty()
    {
        // 确保仅在需要时才设置dirty标志
        isDirty = true;
        
        // 只重新计算边缘，不修改点位置
        m_edges.Clear();
        var wallsPoints = GetWorldPoints(); // 使用世界坐标计算边缘
        if (wallsPoints != null && wallsPoints.Count > 0)
        {
            for (int i = 0; i < wallsPoints.Count; i++)
            {
                if (!closeEdges && wallsPoints.Count > 2 && i == wallsPoints.Count - 1) continue;
    
                Edge line = new Edge();
                m_edges.Add(line);
                
                if (i >= wallsPoints.Count - 1)
                {
                    line.p1 = wallsPoints[wallsPoints.Count - 1];
                    line.p2 = wallsPoints[0];
                    line.normalDirrection = Vector3.Cross(line.p2 - line.p1, Vector3.up);
                    if (reverseNormal) line.normalDirrection *= -1;
                }
                else
                {
                    line.p2 = wallsPoints[i + 1];
                    line.p1 = wallsPoints[i];
                    line.normalDirrection = Vector3.Cross((line.p2 - line.p1).normalized, Vector3.up);
                    if (reverseNormal) line.normalDirrection *= -1;
                }
            }
        }
    }
    /// <summary>
    /// All object created/instantiated by script
    /// </summary>
    /// <typeparam name="GameObject"></typeparam>
    /// <returns></returns>
    [SerializeField,HideInInspector]List<GameObject> buildingObjects = new List<GameObject>();

    /// <summary>
    /// Material property blocks for custom windows emission
    /// </summary>
    /// <typeparam name="MeshRenderer"></typeparam>
    /// <returns></returns>
    [SerializeField] MaterialPropertyBlock windowsPropertyBlock;
    [SerializeField, HideInInspector]List<MeshRenderer> emittedWindows = new List<MeshRenderer>();
    

    [HideInInspector]public bool debugLabes = false;
    [Header("Help lines and distances")]
    [Range(0 , 1000)]public int seed = 300;
    [Range(0 , 1f)] public float windowEmissions = 0.2f;

    [Header("Emission field in shader for windows")]
    public string windowEmissionFieldName = "_EmissionColor";

    [ColorUsage(true , true)]public Color windowEmissionColor = Color.white;
    [ColorUsage(false, true)] public Color firstFloorLampColor = Color.white;

    // 添加Gizmo显示控制
    [Header("Gizmo 显示设置")]
    public bool showFloorGizmos = false;
    public bool showEdgeGizmos = false;
    public Color floorGizmoColor = new Color(0.2f, 0.8f, 1f, 0.8f);
    public Color edgeGizmoColor = new Color(1f, 0.5f, 0f, 0.8f);

    [Header("Custom light prefab for first floor")]
    private GameObject lightPrefab;

    private bool rebuildInUpdate;
    public bool randomEachStage = false; 
    
    //Create area light only for baking in first floor
    public bool lightsOnFirstFloor;
    [Header("Grid settings")]
    public bool useGrid = false;
    public float gridDev = 0.8f;
    public bool showControlPoints = true;


    private float proceduralRoofOffset = -0.02f;

    [Header("Procedural low poly wall")]
    public float ProceduralWallsUVScale = 10f;
    
    public HideFlags _hideFlags = HideFlags.None;

    [HideInInspector]public Material WallsMaterial;
    public Material roofMaterial;

    public bool isAllWallsStatic = false;
    [Header("楼层高度设置")]
    [Range(0.1f, 10.0f)]public float stageHeightMultiplier = 1.0f; // 楼层高度乘数
    public bool useFixedHeight = false; // 是否使用固定高度
    [Range(1.0f, 10.0f)]public float fixedHeight = 3.5f; // 固定楼层高度
    
    [Range(0, 40), HideInInspector]public int collisionStagesCount;
    [Range(0f,6f), HideInInspector]public float quadWallsheigh = 0f;
    [Range(0, 40), HideInInspector]public int stageQuadWalls = 0;
    [Range(1, 40)]public int stagesCount = 7;

    
    private bool closeEdges = true;
    private bool reverseNormal = false;
    [HideInInspector]public GameObject roofObject;


    //For editor access 
    [HideInInspector]public List<Edge> m_edges = new List<Edge>();
    
    // 添加上一帧的位置和旋转，用于检测变化
    private Vector3 lastPosition;
    private Quaternion lastRotation;
    
    private void LateUpdate()
    {
        // 检查位置或旋转是否变化
        if (transform.position != lastPosition || transform.rotation != lastRotation)
        {
            // 位置或旋转变化时更新m_edges
            RecalculateEdges();
            
            // 更新上一帧的位置和旋转
            lastPosition = transform.position;
            lastRotation = transform.rotation;
        }
        
        // 其他LateUpdate代码...
        if (preset == null) return;
#if UNITY_EDITOR
        // 只在真正需要更新时才更新，避免频繁重建导致抖动
        if (isDirty || rebuildInUpdate)
        {
            // 添加额外检查以减少不必要的重建
            if (Selection.activeGameObject == gameObject)
            {
                RecalculateBuilding();
                isDirty = false;
                rebuildInUpdate = false;
            }
            else
            {
                // 如果不是当前选中对象，延迟处理以减少性能影响
                rebuildInUpdate = true;
                isDirty = false;
            }
        }
#endif
    }

    // 添加Gizmos绘制方法
    private void OnDrawGizmos()
    {
        #if UNITY_EDITOR
        // 确保边缘与建筑同步
        if (m_edges != null && m_edges.Count > 0)
        {
            // 在编辑模式之外也确保边缘更新
            if (transform.position != lastPosition || transform.rotation != lastRotation)
            {
                RecalculateEdges();
                lastPosition = transform.position;
                lastRotation = transform.rotation;
            }
            
            // 如果启用了边缘Gizmo，绘制所有边缘
            if (showEdgeGizmos && !simplifiedMode)
            {
                Gizmos.color = edgeGizmoColor;
                for (int i = 0; i < m_edges.Count; i++)
                {
                    Gizmos.DrawLine(m_edges[i].p1, m_edges[i].p2);
                    
                    // 绘制法线方向
                    Vector3 midPoint = (m_edges[i].p1 + m_edges[i].p2) / 2;
                    Gizmos.DrawLine(midPoint, midPoint + m_edges[i].normalDirrection.normalized * 0.5f);
                }
            }
            
            // 如果启用了楼层Gizmo，绘制每层楼的轮廓
            if (showFloorGizmos && !simplifiedMode)
            {
                Gizmos.color = floorGizmoColor;
                float floorHeight = 0;
                
                // 为每层绘制楼层轮廓
                for (int floor = 0; floor < stagesCount; floor++)
                {
                    float stageMaxHeight = 0;
                    
                    // 使用实际生成的楼层高度数据
                    if (preset != null)
                    {
                        ProceduralStage stage = GetStageByIndex(floor);
                        ProceduralStageData stageData = null;
                        
                        // 尝试获取具体的楼层数据
                        if (stagesData != null && stagesData.Count > 0)
                        {
                            foreach (var data in stagesData)
                            {
                                if (data.m_stage == stage)
                                {
                                    stageData = data;
                                    break;
                                }
                            }
                        }
                        
                        // 如果有楼层数据，计算实际高度
                        if (stageData != null && stageData.m_constructionWalls.Count > 0)
                        {
                            // 找出最高的墙体高度
                            foreach (var wall in stageData.m_constructionWalls)
                            {
                                if (wall.wallSize.y > stageMaxHeight)
                                {
                                    stageMaxHeight = wall.wallSize.y;
                                }
                            }
                        }
                        else if (stage != null)
                        {
                            // 估算楼层高度
                            if (stage.stageWallsPrefabs.Count > 0 && 
                                stage.stageWallsPrefabs[0] != null && 
                                stage.stageWallsPrefabs[0].GetComponentInChildren<MeshFilter>() != null)
                            {
                                MeshFilter mf = stage.stageWallsPrefabs[0].GetComponentInChildren<MeshFilter>();
                                stageMaxHeight = mf.sharedMesh.bounds.size.y;
                            }
                            else
                            {
                                stageMaxHeight = 3f; // 默认高度
                            }
                        }
                        else
                        {
                            stageMaxHeight = 3f; // 默认高度
                        }
                    }
                    else
                    {
                        stageMaxHeight = 3f; // 默认高度，当preset为null时
                    }
                    
                    // 绘制当前楼层的轮廓
                    Vector3 heightOffset = Vector3.up * floorHeight;
                    
                    // 绘制楼层标签
                    Vector3 labelPosition = transform.position + Vector3.up * (floorHeight + stageMaxHeight / 2);
                    Handles.color = floorGizmoColor;
                    Handles.Label(labelPosition, $"楼层 {floor + 1}\n高度: {stageMaxHeight:F2}m");
                    
                    for (int i = 0; i < m_edges.Count; i++)
                    {
                        // 绘制底部轮廓
                        Gizmos.DrawLine(m_edges[i].p1 + heightOffset, m_edges[i].p2 + heightOffset);
                        
                        // 绘制顶部轮廓
                        Gizmos.DrawLine(
                            m_edges[i].p1 + heightOffset + Vector3.up * stageMaxHeight,
                            m_edges[i].p2 + heightOffset + Vector3.up * stageMaxHeight
                        );
                        
                        // 绘制连接底部和顶部的线
                        Gizmos.DrawLine(
                            m_edges[i].p1 + heightOffset,
                            m_edges[i].p1 + heightOffset + Vector3.up * stageMaxHeight
                        );
                        Gizmos.DrawLine(
                            m_edges[i].p2 + heightOffset,
                            m_edges[i].p2 + heightOffset + Vector3.up * stageMaxHeight
                        );
                    }
                    
                    // 更新下一层的起始高度
                    floorHeight += stageMaxHeight;
                }
            }
        }
        #endif
    }

    // 添加一个OnDrawGizmosSelected方法，确保在选中对象时始终能看到Gizmo
    private void OnDrawGizmosSelected()
    {
        #if UNITY_EDITOR
        // 在对象被选中时强制显示Gizmo，无论开关状态如何
        if (!simplifiedMode && m_edges != null && m_edges.Count > 0)
        {
            // 绘制边缘
            Gizmos.color = edgeGizmoColor;
            for (int i = 0; i < m_edges.Count; i++)
            {
                Gizmos.DrawLine(m_edges[i].p1, m_edges[i].p2);
                
                // 绘制法线方向
                Vector3 midPoint = (m_edges[i].p1 + m_edges[i].p2) / 2;
                Gizmos.DrawLine(midPoint, midPoint + m_edges[i].normalDirrection.normalized * 0.5f);
            }
            
            // 绘制楼层
            Gizmos.color = floorGizmoColor;
            float floorHeight = 0;
            
            // 为每层绘制轮廓
            for (int floor = 0; floor < stagesCount; floor++)
            {
                // 简化版本：使用固定楼层高度
                float stageMaxHeight = 3f;
                
                // 如果有生成数据，则尝试获取实际高度
                if (preset != null) {
                    ProceduralStage stage = GetStageByIndex(floor);
                    if (stage != null && stagesData != null && stagesData.Count > 0)
                    {
                        ProceduralStageData stageData = stagesData.Find(sd => sd.m_stage == stage);
                        if (stageData != null && stageData.m_constructionWalls.Count > 0)
                        {
                            // 找出最高的墙体高度
                            foreach (var wall in stageData.m_constructionWalls)
                            {
                                if (wall.wallSize.y > stageMaxHeight)
                                {
                                    stageMaxHeight = wall.wallSize.y;
                                }
                            }
                        }
                    }
                }
                
                // 应用全局高度乘数
                stageMaxHeight *= stageHeightMultiplier;
                
                // 绘制当前楼层的轮廓
                Vector3 heightOffset = Vector3.up * floorHeight;
                
                for (int i = 0; i < m_edges.Count; i++)
                {
                    // 绘制底部轮廓
                    Gizmos.DrawLine(m_edges[i].p1 + heightOffset, m_edges[i].p2 + heightOffset);
                    
                    // 绘制顶部轮廓
                    Gizmos.DrawLine(
                        m_edges[i].p1 + heightOffset + Vector3.up * stageMaxHeight,
                        m_edges[i].p2 + heightOffset + Vector3.up * stageMaxHeight
                    );
                    
                    // 绘制连接线
                    Gizmos.DrawLine(
                        m_edges[i].p1 + heightOffset,
                        m_edges[i].p1 + heightOffset + Vector3.up * stageMaxHeight
                    );
                    Gizmos.DrawLine(
                        m_edges[i].p2 + heightOffset,
                        m_edges[i].p2 + heightOffset + Vector3.up * stageMaxHeight
                    );
                }
                
                // 更新下一层的起始高度
                floorHeight += stageMaxHeight;
            }
        }
        else if (!simplifiedMode && (m_edges == null || m_edges.Count == 0))
        {
            // 如果边缘为空，尝试重新计算
            RecalculateEdges();
        }
        #endif
    }

    /// <summary>
    /// Control points for manipalate walls (返回局部坐标的点)
    /// </summary>
    /// <returns></returns>
    public List<Vector3> GetPoints()
    {
        return points;
    }
    
    /// <summary>
    /// 返回世界坐标系中的点位置
    /// </summary>
    /// <returns></returns>
    public List<Vector3> GetWorldPoints()
    {
        List<Vector3> worldPoints = new List<Vector3>();
        foreach (var localPoint in points)
        {
            worldPoints.Add(transform.TransformPoint(localPoint));
        }
        return worldPoints;
    }

    public void Awake()
    {
        ApplyWindowsPropertyBlock();
    }



    /// <summary>
    /// Recalculate building and points
    /// </summary>
    private void OnValidate()
    {
        // 检查名称为空的情况
        if (string.IsNullOrEmpty(gameObject.name) || gameObject.name == "GameObject")
        {
            gameObject.name = "ProceduralBuilding";
        }
        
        #if UNITY_EDITOR

        if (points.Count == 0)
        {
            CalculateDefaultPoints();
        }
        
        if (preset == null) return;
        if (preset.buildings.Contains(this)==false) preset.buildings.Add(this);
        //Debug.Log($"Validate: {name}");
        
        // 只应用窗户属性，不重新计算边缘
        ApplyWindowsPropertyBlock();
        
        // 检查是否需要重建（例如楼层数变化时）
        bool shouldRebuild = false;
        
        if (lastStagesCount != stagesCount)
        {
            lastStagesCount = stagesCount;
            shouldRebuild = true;
        }
        
        // 自动重建逻辑 - 如果启用了自动重建，立即重新生成建筑
        if (shouldRebuild && autoRebuildInEditor)
        {
            isDirty = true;
            RecalculateBuilding();
        }
        
        #endif
    }


    /// <summary>
    /// First time calculate points if they are null
    /// </summary>
    void CalculateDefaultPoints()
    {
        for (int i = 0; i < 4; i++)
        {
            Vector3 pointPosition = transform.position + Quaternion.AngleAxis(90f * (i + 1) - 45, Vector3.up) * transform.forward * 10f;
            AddBuildingPoint(pointPosition);
        }

        // 调整第一个和第二个点的位置
        points[0] += transform.right * 10f;
        points[1] += transform.right * 10f;
    }


    /// <summary>
    /// Main method for create building with all stages, walls, roofs and etc...
    /// </summary>
    public void RecalculateBuilding()
    {
        isDirty = false;
        rebuildInUpdate = false;

        m_edges.Clear();
        m_edges = new List<Edge>(points.Count);

        RecalculateStageData();
        RecalculateEdges();

        // 根据当前模式选择不同的生成方式
        if (simplifiedMode)
        {
            GenerateSimplifiedGeometry();
        }
        else
        {
            GenerateGeometry();
        }

        if (roofObject) Destroy(roofObject);
        
        // 只在非简化模式下生成屋顶
        if (!simplifiedMode)
        {
            roofObject = ProceduralRoof.GenerateRoofMesh(m_edges, transform.position.y, roofMaterial);
            roofObject.transform.parent = transform;
            roofObject.hideFlags = hideFlags;
            roofObject.transform.localPosition = new Vector3(0, 0, 0);
        }
    }
    
    /// <summary>
    /// 生成简化的几何体，使用ProceduralToolkit生成系统
    /// </summary>
    private void GenerateSimplifiedGeometry()
    {
        // 清理现有几何体
        ClearAllGeometry();
        
        // 如果存在简化Mesh对象，先销毁
        if (simplifiedMeshObject != null)
        {
            DestroyImmediate(simplifiedMeshObject);
        }
        
        // 创建新的简化Mesh对象
        simplifiedMeshObject = new GameObject("SimplifiedMesh");
        simplifiedMeshObject.transform.parent = transform;
        simplifiedMeshObject.transform.localPosition = Vector3.zero;
        
        // 获取世界坐标中的点位置
        List<Vector3> worldPoints = GetWorldPoints();
        if (worldPoints.Count < 3) return;
        
        // 计算建筑高度（考虑楼层高度乘数和固定高度设置）
        float actualFloorHeight = simplifiedFloorHeight;
        if (useFixedHeight)
        {
            actualFloorHeight = fixedHeight;
        }
        actualFloorHeight *= stageHeightMultiplier;
        
        float buildingHeight = stagesCount * actualFloorHeight;
        currentBuildingHeight = buildingHeight;
        
        // 如果PT简化生成器不存在，创建一个新的
        if (ptSimplifiedGenerator == null)
        {
            ptSimplifiedGenerator = new PTSimplifiedBuildingGenerator();
        }
        
        // 根据预设类型设置参数
        ConfigurePresetParameters();
        
        // 使用ProceduralToolkit生成简化建筑
        Transform buildingTransform = ptSimplifiedGenerator.GenerateBuilding(
            worldPoints,
            simplifiedMeshObject.transform,
            stagesCount - 1, // 减1，因为底层商铺算第一层
            simplifiedFloorHeight,
            useFixedHeight,
            fixedHeight,
            stageHeightMultiplier,
            roofType, // 使用选择的屋顶类型
            true, // 有底层商铺
            ptSimplifiedGenerator.shopHeight, // 使用预设中的商铺高度
            new Color(0.3f, 0.3f, 0.3f, windowEmissions > 0 ? 0.7f : 0.3f), // 窗户颜色
            ptSimplifiedGenerator.buildingConfig.palette.wallColor, // 墙体颜色
            ptSimplifiedGenerator.buildingConfig.palette.roofColor, // 屋顶颜色
            ptSimplifiedGenerator.shopWallColor, // 商铺颜色
            ptSimplifiedGenerator.basementHeight // 底座高度
        );
        
        // 如果需要显示窗户发光，添加窗户发光处理
        if (windowEmissions > 0)
        {
            ApplyWindowsEmission(simplifiedMeshObject);
        }
        
        // 添加到建筑对象列表中进行管理
        buildingObjects.Add(simplifiedMeshObject);
    }
    
    /// <summary>
    /// 应用窗户发光效果
    /// </summary>
    private void ApplyWindowsEmission(GameObject buildingObject)
    {
        // 查找所有使用窗户材质的渲染器
        MeshRenderer[] renderers = buildingObject.GetComponentsInChildren<MeshRenderer>();
        
        foreach (MeshRenderer renderer in renderers)
        {
            // 检查材质数组中是否有窗户材质（通常是第二个材质）
            if (renderer.sharedMaterials.Length > 1)
            {
                Material windowMaterial = renderer.sharedMaterials[1];
                
                // 克隆材质以防止修改共享材质
                Material emissiveMaterial = new Material(windowMaterial);
                
                // 设置发光属性
                emissiveMaterial.EnableKeyword("_EMISSION");
                emissiveMaterial.SetColor("_EmissionColor", windowEmissionColor * windowEmissions);
                
                // 替换窗户材质
                Material[] materials = renderer.sharedMaterials;
                materials[1] = emissiveMaterial;
                renderer.sharedMaterials = materials;
                
                // 添加到发光窗户列表中
                emittedWindows.Add(renderer);
            }
        }
    }
    
    /// <summary>
    /// 验证所有简化模式下生成的面片法线方向是否正确
    /// </summary>
    private void ValidateSimplifiedMeshNormals()
    {
        if (simplifiedMeshObject == null) return;
        
        // 获取所有网格过滤器
        MeshFilter[] meshFilters = simplifiedMeshObject.GetComponentsInChildren<MeshFilter>();
        int fixedCount = 0;
        
        foreach (MeshFilter meshFilter in meshFilters)
        {
            Mesh mesh = meshFilter.sharedMesh;
            if (mesh == null) continue;
            
            string objName = meshFilter.gameObject.name;
            string parentName = meshFilter.transform.parent != null ? meshFilter.transform.parent.name : "";
            bool isMeshFixed = false;
            
            // 获取对象的中心点和边界
            Vector3 meshCenter = meshFilter.transform.position + mesh.bounds.center;
            
            // 对于楼层面片，确保法线朝上（优先检查，因为这是最常见的问题）
            if (objName.Contains("Floor") || parentName.Contains("Floor"))
            {
                // 计算当前网格的平均法线
                Vector3 avgNormal = CalculateAverageMeshNormal(mesh);
                
                // 检查平均法线是否朝上
                if (Vector3.Dot(avgNormal, Vector3.up) < 0.9f) // 法线应该接近(0,1,0)
                {
                    Debug.LogWarning($"楼层面片 {objName} 法线方向错误: {avgNormal}，正在修复");
                    FlipMeshTriangles(mesh);
                    isMeshFixed = true;
                }
            }
            // 对于墙体，确保法线朝外
            else if (objName.Contains("wall") || parentName.Contains("Wall"))
            {
                // 计算从建筑中心到墙体的方向(预期法线方向)
                Vector3 fromCenter = (meshCenter - transform.position).normalized;
                fromCenter.y = 0; // 忽略垂直分量，只考虑水平方向
                
                // 计算当前网格的平均法线
                Vector3 avgNormal = CalculateAverageMeshNormal(mesh);
                
                // 确保法线水平分量与从中心出发的方向基本一致(点积为正)
                Vector3 normalHorizontal = new Vector3(avgNormal.x, 0, avgNormal.z).normalized;
                float dotProduct = Vector3.Dot(normalHorizontal, fromCenter);
                
                if (dotProduct < 0.5f) // 法线与预期方向差异较大
                {
                    Debug.LogWarning($"墙体 {objName} 法线方向错误，正在修复。当前:{avgNormal}, 预期方向:{fromCenter}");
                    FlipMeshTriangles(mesh);
                    isMeshFixed = true;
                }
            }
            // 对于窗户面片，确保法线朝外
            else if (objName.Contains("window") || parentName.Contains("Window"))
            {
                // 计算从建筑中心到窗户的方向(预期法线方向)
                Vector3 fromCenter = (meshCenter - transform.position).normalized;
                fromCenter.y = 0; // 忽略垂直分量，只考虑水平方向
                
                // 计算当前网格的平均法线
                Vector3 avgNormal = CalculateAverageMeshNormal(mesh);
                
                // 确保法线水平分量与从中心出发的方向基本一致(点积为正)
                Vector3 normalHorizontal = new Vector3(avgNormal.x, 0, avgNormal.z).normalized;
                float dotProduct = Vector3.Dot(normalHorizontal, fromCenter);
                
                if (dotProduct < 0.5f) // 法线与预期方向差异较大
                {
                    Debug.LogWarning($"窗户 {objName} 法线方向错误，正在修复。当前:{avgNormal}, 预期方向:{fromCenter}");
                    FlipMeshTriangles(mesh);
                    isMeshFixed = true;
                }
            }
            // 对于所有其他类型的面片
            else
            {
                // 尝试基于位置判断预期法线方向
                Vector3 fromCenter = (meshCenter - transform.position).normalized;
                fromCenter.y = 0; // 忽略垂直分量，只考虑水平方向
                Vector3 avgNormal = CalculateAverageMeshNormal(mesh);
                Vector3 normalHorizontal = new Vector3(avgNormal.x, 0, avgNormal.z).normalized;
                
                // 检查法线是否指向外部
                float dotProduct = Vector3.Dot(normalHorizontal, fromCenter);
                if (dotProduct < 0.3f) // 法线与从中心指向外部的方向几乎垂直或相反
                {
                    Debug.LogWarning($"未知类型面片 {objName} 法线可能方向错误: {avgNormal}，尝试修复");
                    FlipMeshTriangles(mesh);
                    isMeshFixed = true;
                }
            }
            
            if (isMeshFixed)
            {
                fixedCount++;
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
            }
        }
        
        if (fixedCount > 0)
        {
            Debug.Log($"已自动修复 {fixedCount} 个面片的法线方向问题");
        }
    }
    
    /// <summary>
    /// 计算网格的平均法线方向
    /// </summary>
    private Vector3 CalculateAverageMeshNormal(Mesh mesh)
    {
        Vector3 avgNormal = Vector3.zero;
        Vector3[] normals = mesh.normals;
        
        if (normals == null || normals.Length == 0)
            return Vector3.up; // 默认朝上
            
        for (int i = 0; i < normals.Length; i++)
        {
            avgNormal += normals[i];
        }
        
        if (avgNormal.magnitude < 0.001f)
            return Vector3.up; // 默认朝上
            
        return avgNormal.normalized;
    }
    
    /// <summary>
    /// 翻转网格的所有三角形
    /// </summary>
    private void FlipMeshTriangles(Mesh mesh)
    {
        int[] triangles = mesh.triangles;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            // 交换第二个和第三个索引来翻转三角形
            int temp = triangles[i + 1];
            triangles[i + 1] = triangles[i + 2];
            triangles[i + 2] = temp;
        }
        
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
    }
    
    /// <summary>
    /// 生成简化的墙体几何体
    /// </summary>
    private void GenerateSimplifiedWalls(GameObject parent, List<Vector3> points, float height)
    {
        GameObject wallsObject = new GameObject("Walls");
        wallsObject.transform.parent = parent.transform;
        wallsObject.transform.localPosition = Vector3.zero;
        
        // 计算建筑中心
        Vector3 buildingCenter = Vector3.zero;
        foreach (var p in points)
        {
            buildingCenter += p;
        }
        buildingCenter /= points.Count;
        buildingCenter.y = 0; // 保持水平
        
        // 为每条边创建一个面片
        for (int i = 0; i < points.Count; i++)
        {
            int nextIndex = (i < points.Count - 1) ? i + 1 : 0;
            Vector3 p1 = points[i];
            Vector3 p2 = points[nextIndex];
            
            // 计算法线方向 - 确保朝外
            Vector3 edgeCenter = (p1 + p2) / 2;
            Vector3 fromCenter = (edgeCenter - buildingCenter).normalized;
            fromCenter.y = 0; // 保持水平方向
            
            // 边的方向
            Vector3 edgeDir = (p2 - p1).normalized;
            
            // 通过叉乘获取垂直于边的法线
            Vector3 edgeNormal = Vector3.Cross(Vector3.up, edgeDir).normalized;
            
            // 确保法线朝外 - 与从中心指向边的方向比较
            if (Vector3.Dot(edgeNormal, fromCenter) < 0)
            {
                // 如果法线朝内，交换点顺序
                Vector3 temp = p1;
                p1 = p2;
                p2 = temp;
            }
            
            // 创建墙面 - 顶点顺序确保法线朝外
            // 注意：Unity中，三角形法线方向由顶点的逆时针顺序决定
            // 使用正确的顶点顺序，确保法线朝外（与建筑中心相反）
            GameObject wallFace = ProceduralFace.GenerateFace(new Vector3[4] {
                p1,                        // 左下
                p1 + Vector3.up * height,  // 左上
                p2 + Vector3.up * height,  // 右上
                p2                         // 右下
            }, transform, WallsMaterial, ProceduralWallsUVScale);
            
            wallFace.transform.parent = wallsObject.transform;
            wallFace.isStatic = isAllWallsStatic;
            
            // 添加碰撞器
            if (collisionStagesCount > 0)
            {
                wallFace.AddComponent<BoxCollider>();
            }
            
            // 添加到对象列表中
            buildingObjects.Add(wallFace);
        }
    }
    
    /// <summary>
    /// 生成简化的窗户标记（使用面片）
    /// </summary>
    private void GenerateSimplifiedWindows(GameObject parent, List<Vector3> points, float height)
    {
        GameObject windowsObject = new GameObject("Windows");
        windowsObject.transform.parent = parent.transform;
        windowsObject.transform.localPosition = Vector3.zero;
        
        // 创建窗户材质
        Material windowMaterial = new Material(Shader.Find("Standard"));
        windowMaterial.SetFloat("_Mode", 3); // 透明模式
        windowMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        windowMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        windowMaterial.SetInt("_ZWrite", 0);
        windowMaterial.DisableKeyword("_ALPHATEST_ON");
        windowMaterial.EnableKeyword("_ALPHABLEND_ON");
        windowMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        windowMaterial.renderQueue = 3000;
        windowMaterial.color = new Color(0.1f, 0.1f, 0.1f, 0.5f);
        windowMaterial.EnableKeyword("_EMISSION");
        windowMaterial.SetColor("_EmissionColor", windowEmissionColor * windowEmissions);
        
        // 计算建筑中心
        Vector3 buildingCenter = Vector3.zero;
        foreach (var p in points)
        {
            buildingCenter += p;
        }
        buildingCenter /= points.Count;
        buildingCenter.y = 0; // 保持水平
        
        // 为每条边创建窗户
        for (int i = 0; i < points.Count; i++)
        {
            int nextIndex = (i < points.Count - 1) ? i + 1 : 0;
            Vector3 p1 = points[i];
            Vector3 p2 = points[nextIndex];
            
            // 计算边长
            float edgeLength = Vector3.Distance(p1, p2);
            
            // 计算窗户数量（基于密度）
            int windowCount = Mathf.Max(1, Mathf.FloorToInt(edgeLength * simplifiedWindowDensity));
            
            // 计算楼层数量
            int floorCount = stagesCount;
            
            // 窗户尺寸
            float windowWidth = edgeLength / (windowCount * 2);
            float windowHeight = height / (floorCount * 2);
            
            // 窗户间距
            float windowSpacingX = edgeLength / windowCount;
            float windowSpacingY = height / floorCount;
            
            // 边的方向
            Vector3 edgeDir = (p2 - p1).normalized;
            
            // 窗户的法线方向（垂直于边，指向外部）
            // 确保法线朝外
            Vector3 normal = Vector3.Cross(Vector3.up, edgeDir).normalized;
            
            // 检查法线是否朝外 - 计算从建筑中心到边中点的方向
            Vector3 edgeCenter = (p1 + p2) / 2;
            Vector3 fromCenter = (edgeCenter - buildingCenter).normalized;
            fromCenter.y = 0; // 保持水平方向
            
            // 如果法线与从中心出发的方向点积为负，则表示法线朝内，需要反转
            if (Vector3.Dot(normal, fromCenter) < 0)
            {
                normal = -normal;
                // 也需要交换点的顺序，以保持正确的面朝向
                Vector3 temp = p1;
                p1 = p2;
                p2 = temp;
                edgeDir = -edgeDir; // 边的方向也需要反转
            }
            
            // 创建窗户
            for (int w = 0; w < windowCount; w++)
            {
                for (int f = 0; f < floorCount; f++)
                {
                    // 窗户位置
                    Vector3 windowCenter = p1 + edgeDir * (windowSpacingX * (w + 0.5f)) + Vector3.up * (windowSpacingY * (f + 0.5f));
                    
                    // 向外移动一点以避免z-fighting
                    windowCenter += normal * 0.05f;
                    
                    // 创建窗户面片 - 使用确定朝外的法线方向
                    // 注意：Unity中，三角形法线方向由顶点的逆时针顺序决定
                    // 使用正确的顶点顺序，确保法线朝外
                    GameObject window = ProceduralFace.GenerateFace(new Vector3[4] {
                        windowCenter - edgeDir * windowWidth + Vector3.down * windowHeight, // 左下
                        windowCenter - edgeDir * windowWidth + Vector3.up * windowHeight,   // 左上
                        windowCenter + edgeDir * windowWidth + Vector3.up * windowHeight,   // 右上
                        windowCenter + edgeDir * windowWidth + Vector3.down * windowHeight  // 右下
                    }, transform, windowMaterial, 1.0f);
                    
                    window.transform.parent = windowsObject.transform;
                    
                    // 添加到对象列表和发光窗户列表
                    buildingObjects.Add(window);
                    emittedWindows.Add(window.GetComponent<MeshRenderer>());
                }
            }
        }
        
        // 应用发光属性
        ApplyWindowsPropertyBlock();
    }

    /// <summary>
    /// 生成简化的楼层标记
    /// </summary>
    private void GenerateSimplifiedFloors(GameObject parent, List<Vector3> points, float height)
    {
        GameObject floorsObject = new GameObject("Floors");
        floorsObject.transform.parent = parent.transform;
        floorsObject.transform.localPosition = Vector3.zero;
        
        // 楼层间距
        float floorSpacing = height / stagesCount;
        
        // 创建楼层材质
        Material floorMaterial = new Material(Shader.Find("Standard"));
        floorMaterial.color = new Color(0.7f, 0.7f, 0.7f, 1.0f);
        
        // 确保点的顺序是顺时针的，这样法线才会朝上
        List<Vector3> orderedPoints = EnsurePointsOrderClockwise(points);
        
        // 为每个楼层创建一个面片
        for (int f = 0; f < stagesCount; f++)
        {
            float floorY = f * floorSpacing;
            
            // 创建顶点列表
            List<Vector3> floorPoints = new List<Vector3>();
            foreach (var p in orderedPoints)
            {
                floorPoints.Add(new Vector3(p.x, floorY, p.z));
            }
            
            // 创建楼层面片
            GameObject floor = new GameObject($"Floor_{f}");
            floor.transform.parent = floorsObject.transform;
            floor.transform.localPosition = Vector3.zero;
            
            MeshFilter meshFilter = floor.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = floor.AddComponent<MeshRenderer>();
            
            // 创建Mesh
            Mesh mesh = new Mesh();
            meshFilter.mesh = mesh;
            
            // 设置顶点
            mesh.vertices = floorPoints.ToArray();
            
            // 创建三角形索引 - 使用三角扇形，确保法线朝上
            List<int> triangles = new List<int>();
            
            // 重要：楼层面片需要使用逆时针顶点顺序才能使法线朝上
            // Unity中默认逆时针顶点顺序的面法线朝向观察者
            for (int i = 1; i < floorPoints.Count - 1; i++)
            {
                triangles.Add(0);
                triangles.Add(i);
                triangles.Add(i + 1);
            }
            
            // 设置三角形索引
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            
            // 设置材质
            meshRenderer.material = floorMaterial;
            
            // 添加到对象列表
            buildingObjects.Add(floor);
        }
    }
    
    /// <summary>
    /// 确保点列表是顺时针排序的，以使法线朝上
    /// </summary>
    private List<Vector3> EnsurePointsOrderClockwise(List<Vector3> points)
    {
        if (points.Count < 3)
            return new List<Vector3>(points);
            
        // 计算多边形中心
        Vector3 center = Vector3.zero;
        foreach (var p in points)
        {
            center += p;
        }
        center /= points.Count;
        
        // 保持y坐标一致，只考虑水平面
        center.y = points[0].y;
        
        // 计算每个点相对于中心点的角度
        List<(Vector3 point, float angle)> pointsWithAngles = new List<(Vector3, float)>();
        
        foreach (var p in points)
        {
            Vector3 dir = p - center;
            dir.y = 0; // 确保在水平面内
            
            // 计算角度 (0-360度)
            float angle = Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg;
            if (angle < 0) angle += 360f;
            
            pointsWithAngles.Add((p, angle));
        }
        
        // 按角度排序（升序），这是顺时针方向（在Unity的坐标系中）
        pointsWithAngles.Sort((a, b) => b.angle.CompareTo(a.angle));
        
        // 返回排序后的点
        return pointsWithAngles.Select(pa => pa.point).ToList();
    }
    
    /// <summary>
    /// 尝试使用Houdini HDA生成建筑
    /// </summary>
    private void TryGenerateWithHoudiniHDA(GameObject parent, List<Vector3> points, float height)
    {
        // 此方法需要与Houdini Engine插件集成
        // 这里是一个占位方法，实际实现需要根据Houdini Engine API进行
        
#if UNITY_EDITOR && HEU_HOUDINIENGINE
        // Houdini Engine API调用部分，取决于Houdini Engine for Unity的API
        if (houdiniHDAAsset != null)
        {
            // 创建HDA资源实例
            HEU_HoudiniAssetRoot assetRoot = HEU_HoudiniAsset.InstantiateAsset(houdiniHDAAsset as HEU_HoudiniAsset, Vector3.zero, parent.transform);
            if (assetRoot != null)
            {
                // 设置HDA参数
                HEU_HoudiniAsset hdaAsset = assetRoot.GetAsset();
                
                // 传递点数据
                HEU_ParameterData pointsParam = hdaAsset.GetParameter("points");
                if (pointsParam != null)
                {
                    // 转换点数据格式并传递
                    // ...具体实现依赖Houdini Engine插件
                }
                
                // 设置高度参数
                HEU_ParameterData heightParam = hdaAsset.GetParameter("height");
                if (heightParam != null)
                {
                    heightParam.SetFloatValue(height);
                }
                
                // 设置其他参数
                // ...
                
                // 重新烘焙HDA
                hdaAsset.RequestCook();
                
                // 将生成的对象添加到建筑对象列表
                // ...
            }
        }
#endif
    }
    
    /// <summary>
    /// Return ProceduralStage data by index  or higher stage data if index out of range
    /// </summary>
    /// <param name="stageIndex"></param>
    /// <returns></returns>
    public ProceduralStage GetStageByIndex(int stageIndex)
    {
        // 首先检查preset是否为null
        if (preset == null)
        {
            Debug.LogWarning("[CONFIGURATION] preset is null, cannot get stage data");
            return null;
        }

        if (preset.buildingStages.Count == 0)
        {
            Debug.LogError("[CONFIGURATION] no stages configured");
            return null;
        }
        if (stageIndex >= stagesCount-1) return preset.buildingStages[preset.buildingStages.Count - 1];

        int currentSize = 0;
        foreach (var stage in preset.buildingStages)
        {
            if (stage == preset.buildingStages[preset.buildingStages.Count -1] && stage.stageType == ProceduralStage.EStageType.CorniceSingleMeshScaledByEdge) continue;
            if (stageIndex >= currentSize && stageIndex < currentSize + stage.repeatCount)
            {
                return stage;
            }
            currentSize += stage.repeatCount;
        }
        if (preset.buildingStages.Count == 1) return preset.buildingStages[preset.buildingStages.Count - 1];
        else if (preset.buildingStages[preset.buildingStages.Count -1 ].stageType == ProceduralStage.EStageType.CorniceSingleMeshScaledByEdge) return preset.buildingStages[preset.buildingStages.Count - 2];
        else return preset.buildingStages[preset.buildingStages.Count - 1];
    }

    /// <summary>
    /// Get procedural stage data by index
    /// </summary>
    /// <param name="index"></param>
    /// <returns></returns>
    public ProceduralStageData GetStageDataByIndex(int index)
    {
        // 首先检查preset是否为null
        if (preset == null)
        {
            Debug.LogWarning("[CONFIGURATION] preset is null, cannot get stage data");
            return null;
        }

        if (stagesData.Count == 0)
        {
            Debug.LogError("[CONFIGURATION] no stages configured");
            return null;
        }

        if (stagesData.Count > index) return stagesData[index];
        else
        {
            // 确保索引有效
            int validIndex = Mathf.Min(preset.buildingStages.Count - 1, stagesData.Count - 1);
            if (validIndex >= 0)
                return stagesData[validIndex];
            else
                return null;
        }
    }

    // 修改BuildingData结构以支持简化模式
    public struct BuildingData
    {
        public Material roofMaterial;
        public int stagesCount;
        public float windowEmissionValue01;
        public Color windowEmissionColor;
        public BuildPreset preset;
        // 添加简化模式标记
        public bool simplifiedMode;
        public Object houdiniHDAAsset;
        //
        public List<Vector3> points;
    }

    public static ProceduralBuilding CreateNewBuilding(BuildingData data)
    {
        GameObject newBuilding = new GameObject($"Building_{Random.Range(int.MinValue, int.MaxValue)}");
        var bc = newBuilding.AddComponent<ProceduralBuilding>();
        
        bc.points.Clear();

        foreach (var bp in data.points)
        {
            bc.AddBuildingPoint(bp);
            Debug.DrawRay(bp, Vector3.up * 100f, Color.red, 3f);
        }
        bc.roofMaterial = data.roofMaterial;
        bc.stagesCount = data.stagesCount;
        bc.windowEmissions = data.windowEmissionValue01;
        bc.windowEmissionColor = data.windowEmissionColor;
        bc.preset = data.preset;
        // 设置简化模式
        bc.simplifiedMode = data.simplifiedMode;
        bc.houdiniHDAAsset = data.houdiniHDAAsset;
        //bc.roofMaterial = new Material(Shader.Find("Default"));
        bc.RecalculateBuilding();

        return bc;
    }

    /// <summary>
    /// Recalculate building on preset change
    /// </summary>
    /// <param name="obj"></param>
    private void OnPresetChanged(BuildPreset obj)
    {
        if (obj == preset && preset != null)RecalculateBuilding();
    }

    /// <summary>
    /// Windows emmission stored property blocks for runtime applying or editor
    /// </summary>
    void ApplyWindowsPropertyBlock()
    {
        if (windowsPropertyBlock == null)
        {
            windowsPropertyBlock = new MaterialPropertyBlock();
        }
        windowsPropertyBlock.SetColor(windowEmissionFieldName, windowEmissionColor);
        foreach (var window in emittedWindows)
        {
            if (window != null)window.SetPropertyBlock(windowsPropertyBlock);
        }
    }

    /// <summary>
    /// Calculate each wall types for each stages, right now just 2 wall types
    /// </summary>
    /// <param name="pStage"></param>
    void GenerateWallSettings(ProceduralStage pStage) 
    {
        for (int i = 0; i < pStage.stageWallsPrefabs.Count; i++) CalculatePrefabDataFroStage(pStage, pStage.stageWallsPrefabs[i], WallData.EWallType.Wall);
        for (int i = 0; i < pStage.stageSpecialWalls.Count; i++) CalculatePrefabDataFroStage(pStage, pStage.stageSpecialWalls[i], WallData.EWallType.SpecialWall);
    }

    //This method gather info for placing walls around the edge, and store wall height and length correct for adjust walls along building
    /// <summary>
    /// Gather prefab information, bounds, mesh and other components for applying in precedural
    /// </summary>
    /// <param name="pStage"></param>
    /// <param name="stageWall"></param>
    /// <param name="wallType"></param>
    public void CalculatePrefabDataFroStage(ProceduralStage pStage, GameObject stageWall , WallData.EWallType wallType)
    {
        if (stageWall == null) return;
        bool isPrefabfounded = false;
        ProceduralStageData stageData = stagesData[preset.buildingStages.IndexOf(pStage)];
        
        stageData.m_stage = pStage;
        //посмотрим есть ли такая стена в массиве всех стен, если нет то создадим для неё экземпляр
        for (int j = 0; j < stageData.m_constructionWalls.Count; j++)
        {
            if (stageData.m_constructionWalls[j].m_GameObject == stageWall)
            {
                //Мы нашли стену с похожим префабом, поставим ему что он обнаружен
                stageData.m_constructionWalls[j].isDirty = false;
                isPrefabfounded = true;
            }
        }
        if (isPrefabfounded) return;
        WallData newWall = new WallData();
        newWall.m_GameObject = stageWall;
        newWall.m_meshFilter = newWall.m_GameObject.GetComponent<MeshFilter>();
        if (newWall.m_meshFilter == null)
        {
            newWall.m_meshFilter = newWall.m_GameObject.GetComponentInChildren<MeshFilter>();
        }
        if (newWall.m_meshFilter == null)
        {
            Debug.Log(newWall.m_GameObject.name + " has no mesh filter");
            return;
        }
        newWall.wallSize.x = newWall.m_meshFilter.sharedMesh.bounds.size.x;
        newWall.wallSize.y = newWall.m_meshFilter.sharedMesh.bounds.size.y;
        newWall.stageIndex = pStage.stageIndex;
        newWall.wallType = wallType;
        //Скалькулировали стену, теперь добавим её в массив
        switch (wallType)
        {
            case WallData.EWallType.Wall:
                pStage.lengthOfAllWallsX += newWall.wallSize.x;
                stageData.m_constructionWalls.Add(newWall);
                break;
            case WallData.EWallType.ProceduralCorner:
                //pStage.lengthOfAllCornersX += newWall.wallSize.x;
                //stageData.m_constructionCorners.Add(newWall);
                break;
            case WallData.EWallType.SpecialWall:
                pStage.lengthOfAllWallsX += newWall.wallSize.x;
                stageData.m_constructionWalls.Add(newWall);
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// For create just stages form currently configured procedural stages data
    /// </summary>
    void GenerateGeometry()
    {
        if (preset == null) return;
        
        // 注释掉这一行，防止在添加点时自动重置所有点位置
        // ReposeParentAndPointsToCenter();
        
        ClearAllGeometry();
        RecalculateStageData();
        currentBuildingHeight = 0f;
        if (preset.buildingStages.Count == 0)
        {
            Debug.Log("[CONFIG] Cant create the building with empty stages");
            return;
        }
        emittedWindows = new List<MeshRenderer>();
        debugTexts.Clear();
        for (int i = 0; i < stagesCount; i++)
        {
            GenerateStage(this, i, currentBuildingHeight, out float stageHeight);
            currentBuildingHeight += stageHeight;
        }
    }

    [HideInInspector]public float currentBuildingHeight = 0f;

    /// <summary>
    /// Move parent, like pivot point from bad position to nice position
    /// </summary>
    public void ReposeParentAndPointsToCenter()
    {
        // 计算局部点的平均位置
        Vector3 localCenter = Vector3.zero;
        foreach (var point in points)
        {
            localCenter += point;
        }
        localCenter /= points.Count;
        
        // 转换为世界坐标
        Vector3 worldCenter = transform.TransformPoint(localCenter);
        
        // 保存原始位置
        Vector3 oldPosition = transform.position;
        
        // 移动对象到点的中心位置
        transform.position = worldCenter;
        
        // 由于对象位置变化，需要重新计算所有点的局部坐标
        List<Vector3> newLocalPoints = new List<Vector3>();
        foreach (var localPoint in points)
        {
            // 计算点在原始对象空间中的世界坐标
            Vector3 worldPoint = new Vector3(
                oldPosition.x + localPoint.x,
                oldPosition.y + localPoint.y,
                oldPosition.z + localPoint.z
            );
            
            // 将世界坐标转换为新对象空间中的局部坐标
            newLocalPoints.Add(transform.InverseTransformPoint(worldPoint));
        }
        
        // 替换点列表
        points = newLocalPoints;
        
        // 更新边缘
        SetDirty();
    }

    /// <summary>
    /// Clear all created geometry 
    /// </summary>
    public void ClearAllGeometry()
    {
        for (int w = 0; w < buildingObjects.Count; w++)
        {
            DestroyImmediate(buildingObjects[w]);
        }
        buildingObjects.Clear();
        
        // 清除所有子对象（不再需要检查是否为点，因为点现在是Vector3）
        if (transform.childCount > 0)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(transform.GetChild(i).gameObject);
            }
        }
    }


    /// <summary>
    /// Recalculate stages data like wall length, wall heights in stage and store it in proceduralStageData list
    /// </summary>
    void RecalculateStageData()
    {
        //Установим все стены как undefined
        stagesData.Clear();
        for (int i = 0; i < preset.buildingStages.Count; i++)
        {
            stagesData.Add(new ProceduralStageData());
        }
        foreach (ProceduralStage s in preset.buildingStages)
            GenerateWallSettings(s);
    }

    /// <summary>
    /// Recalculate all edges by control points for wall placing, like edge length 
    /// </summary>
    void RecalculateEdges()
    {
        m_edges.Clear();
        // 获取世界坐标中的点位置
        var worldPoints = GetWorldPoints();
        if (worldPoints == null || worldPoints.Count == 0)
        {
            Debug.LogError("Points null");
            return;
        }

        for (int i = 0; i < worldPoints.Count; i++)
        {
            if (!closeEdges && worldPoints.Count > 2 && i == worldPoints.Count - 1) continue;

            Edge line = new Edge();
            m_edges.Add(line);
            //Если это последняя точка в массиве то рисуем линию от последней точки к первой
            if (i >= worldPoints.Count - 1)
            {
                //Debug.DrawLine(worldPoints[worldPoints.Count - 1], worldPoints[0]);

                line.p1 = worldPoints[worldPoints.Count - 1];
                line.p2 = worldPoints[0];
                //Normal
                line.normalDirrection = Vector3.Cross(line.p2 - line.p1, Vector3.up);
                if (reverseNormal) line.normalDirrection *= -1;
            }
            else
            {
                line.p2 = worldPoints[i + 1];
                line.p1 = worldPoints[i];

                //Debug.DrawLine(line.p1, line.p2);
                //Normal
                line.normalDirrection = Vector3.Cross((line.p2 - line.p1).normalized, Vector3.up);
                if (reverseNormal) line.normalDirrection *= -1;
            }
        }
    }

    /// <summary>
    /// Debug information for testing 
    /// </summary>
    /// <typeparam name="DebugTextLabel"></typeparam>
    [HideInInspector]public List<DebugTextLabel> debugTexts = new List<DebugTextLabel>();
    public class DebugTextLabel
    {
        public string text;
        public Vector3 position;
        public Color color;

        public DebugTextLabel(string _s, Vector3 _v , Color _C)
        {
            text = _s;
            position = _v;
            color = _C;
        }
    }


    /// <summary>
    /// Generate stage for building by preset, thats all. 
    /// </summary>
    /// <param name="b">target building</param>
    /// <param name="stageIndex">index of stage</param>
    /// <param name="stageHeight">startStageHeight</param>
    /// <param name="stageMaxHeight">return max wall height in current stage, use only same height walls for stage</param>
    void GenerateStage(ProceduralBuilding b, int stageIndex, float stageHeight, out float stageMaxHeight)
    {
        stageMaxHeight = 0f;
        
        // 检查b和preset是否为null
        if (b == null || b.preset == null)
        {
            Debug.LogWarning($"Building or preset is null in GenerateStage, stageIndex: {stageIndex}");
            return;
        }
        
        ProceduralStage currentStage = b.GetStageByIndex(stageIndex);
        ProceduralStageData proceduralStageData = stagesData.Find(_sd => _sd.m_stage == currentStage);

        if (proceduralStageData == null)
        {
            Debug.LogError($"Cant find procedural stage data for: {currentStage?.nameOfStage ?? "null"}, stages: {stagesData.Count}" );
            return;
        }
        GameObject stageObject = new GameObject($"{currentStage.nameOfStage}[{stageIndex + 1}]");
        stageObject.transform.parent = b.transform;
        stageObject.transform.localPosition = Vector3.zero;

        for (int i =0; i < m_edges.Count; i++) m_edges[i].index = i;
        int windowIndex = 0;
        for (int i = 0; i < m_edges.Count; i++)
        {
            if (randomEachStage)Random.InitState(seed + stageIndex + i);
            
            else Random.InitState(seed);
            Edge line = m_edges[i];

            float lineLength = (line.p2 - line.p1).magnitude;
            float cornersLength = currentStage.CornersLength;
            if (currentStage.stageType == ProceduralStage.EStageType.LayoutRealSize) cornersLength = 0f;

            //добавим стены в линию до тех пор, пока кол-во стен не будет удовлетворять наши условия
            List<WallData> wallsInEdge = new List<WallData>();
            float totalWallsLength = 0f;
            
            float cornersSize = 0f;
            if (currentStage.stageType == ProceduralStage.EStageType.LayoutFloatSize && currentStage.cornersType == ProceduralStage.ECornersType.ProceduralCorners)
            {
                //The length of procedural corners to avoid walls stretch if prcedural corners exists
                cornersSize = currentStage.CornersLength * 2f;
            }
            int specialWallIndex = 0;
            int totalWallsCount = 0;

            List<WallData> walls = proceduralStageData.m_constructionWalls.Where(w => w.wallType == WallData.EWallType.Wall).ToList();
            List<WallData> specialWalls = proceduralStageData.m_constructionWalls.Where(w => w.wallType == WallData.EWallType.SpecialWall).ToList();
            int wallsCanBePlacedPrognose = (int)(lineLength / (walls.OrderBy(w => w.wallSize.x).ToList()[0].wallSize.x * 0.85f));
            while (true)
            {
                //For roof - place just one peace of geometry, then stretch it by edge length
                if (currentStage.stageType == ProceduralStage.EStageType.CorniceSingleMeshScaledByEdge)
                {
                    wallsInEdge.Add(walls[0]);
                    totalWallsLength += walls[0].wallSize.x;
                    break;
                }
                //Попробуем найти рандомную стену
                int random = Random.Range(0, walls.Count);
                //Выбираем стену или если очередь падика выбираем его
                WallData randomWall = walls[random];
                totalWallsCount++;
                specialWallIndex++;
                if (specialWallIndex >= currentStage.specialWallsEach && specialWalls.Count > 0 && currentStage.minimumWallsShouldBe <= wallsCanBePlacedPrognose)
                {
                    //рандомная стена как специальная стена
                    random = Random.Range(0, specialWalls.Count);
                    randomWall = specialWalls[random];
                    specialWallIndex = 0;
                }
                //На какую дистанцию блоки могут заехать за длинну стены
                float awailableOversizeLength = 0f;
                if (currentStage.stageType == ProceduralStage.EStageType.LayoutFloatSize) awailableOversizeLength = randomWall.wallSize.x / 2f;
                if (totalWallsLength + cornersSize + randomWall.wallSize.x <= lineLength + awailableOversizeLength)
                {
                    wallsInEdge.Add(randomWall);
                    totalWallsLength += randomWall.wallSize.x;
                }else{
                    //Не подошло, попробуем впихнуть самую мелкую стену
                    WallData smallestWall = walls.OrderBy(cw => cw.wallSize.x).Reverse().ToList()[0];
                    if (totalWallsLength + cornersSize + randomWall.wallSize.x <= lineLength + awailableOversizeLength)
                    {
                        wallsInEdge.Add(smallestWall);
                        totalWallsLength+= smallestWall.wallSize.x;
                        break;
                    }else
                    {
                        //Не влазит даже мелкая стена, вставили стен сколько смогли
                        break;
                    }
                }
            }

            bool isRoof =  preset.buildingStages[preset.buildingStages.Count - 1] == currentStage;
            foreach (var wall in wallsInEdge)
            {
                if (stageMaxHeight < wall.wallSize.y) stageMaxHeight = wall.wallSize.y;
            }
            
            // 应用楼层高度调整
            // 优先使用楼层的固定高度，如果启用的话
            if (currentStage.useFixedHeight)
            {
                stageMaxHeight = currentStage.fixedHeight;
            }
            else
            {
                // 应用全局高度乘数
                stageMaxHeight *= b.stageHeightMultiplier;
            }
            
            //Base height for start draw polygons
            Vector3 buildingEdgePosition = currentBuildingHeight * Vector3.up;

            #region CORNERSLOGIC

            //First floors lightning 
            if (b.lightsOnFirstFloor && stageIndex == 1)
            {
                #if UNITY_EDITOR
                Vector3 midPoint = line.p1 + line.Dir / 2f;
                GameObject light = null;
                if (lightPrefab != null)
                {
                    #if UNITY_EDITOR
                        light = PrefabUtility.InstantiatePrefab(lightPrefab) as GameObject;
                    #else
                        light = Instantiate(lightPrefab);
                    #endif
                    
                }else light = new GameObject("FloorLight");
                light.transform.position = midPoint + Vector3.up * stageHeight + line.normalDirrection.normalized * 1.2f;

                light.transform.rotation = Quaternion.LookRotation(line.normalDirrection);
                
                if (lightPrefab == null)
                {
                    var l = light.AddComponent<Light>();
                    l.type = LightType.Rectangle;
                    l.areaSize = new Vector2(line.Dir.magnitude , 1f);
                    l.color= firstFloorLampColor;
                    l.intensity = 2f;
                    l.shadows = LightShadows.Soft;
                }else
                {
                    var l = light.GetComponent<Light>();
                    l.color = firstFloorLampColor;
                }

                light.transform.Rotate(Vector3.right , -90f , Space.Self);
                b.buildingObjects.Add(light);
                light.transform.parent = transform;
                #endif
            }
            //Calculate procedural corners
            if (currentStage.cornersType == ProceduralStage.ECornersType.ProceduralCorners && currentStage.stageType != ProceduralStage.EStageType.CorniceSingleMeshScaledByEdge)
            {
                WallData newCorner = new WallData();
                newCorner.wallType = WallData.EWallType.ProceduralCorner;
                newCorner.wallSize.x = cornersLength;
                newCorner.wallSize.y = stageHeight;

                wallsInEdge.Insert(0, newCorner);
                wallsInEdge.Add(newCorner);

                totalWallsLength += cornersLength * 2f;
            }
            #endregion
            Vector3 lineDirrection = (line.p2 - line.p1).normalized;
            float wallLengthMultiplayer = 1f;

            #region GENERATE LOW POLY STAGE
            if (stageIndex >= b.stagesCount && stageIndex < b.stagesCount -1 )
            {
                GameObject wallFace = null;
                if (!b.reverseNormal)
                {
                    wallFace = ProceduralFace.GenerateFace(new Vector3[4] {
                    line.p2 + buildingEdgePosition,
                    line.p1 + buildingEdgePosition,
                    line.p2 + Vector3.up * (stageHeight + quadWallsheigh),
                    line.p1 + Vector3.up * (stageHeight + quadWallsheigh),
                     }, b.transform, b.WallsMaterial, b.ProceduralWallsUVScale);
                }
                else
                {
                    wallFace = ProceduralFace.GenerateFace(new Vector3[4] {
                    line.p1 + buildingEdgePosition,
                    line.p2 + buildingEdgePosition,
                    line.p1 + Vector3.up * (stageHeight + quadWallsheigh) ,
                    line.p2 + Vector3.up * (stageHeight + quadWallsheigh),
                     }, b.transform, b.WallsMaterial, b.ProceduralWallsUVScale);
                }
                wallFace.isStatic = b.isAllWallsStatic;
                wallFace.hideFlags = b._hideFlags;

                if (stageIndex <= b.collisionStagesCount)
                {
                    wallFace.AddComponent<BoxCollider>();
                }
                //Debug.Log($"stage height: {stageHeight:f2} , prevStageHeigh: {prevStageHeight:f2}"  );
                stageMaxHeight = b.quadWallsheigh;
                b.buildingObjects.Add(wallFace);
                continue;
            }
            #endregion

            //Gather all objects to single edge/line group and then stretch it by size if needed
            Transform lineGroup = new GameObject("Line_" + i + "_grp").transform;
            lineGroup.rotation = Quaternion.LookRotation(line.normalDirrection);
            lineGroup.transform.parent = stageObject.transform;
            lineGroup.transform.position = line.p1;
            b.buildingObjects.Add(lineGroup.gameObject);
            lineGroup.hideFlags = b._hideFlags;

            int corners = 0;
            //wall length multiplayer
            float fixedLineLength = totalWallsLength;
            //for place walls, we should know how many free space left for walls
            float spaceTakenByWalls = 0f;
            if (fixedLineLength <= 0f || fixedLineLength > 999999) fixedLineLength = 1;
            wallLengthMultiplayer = 1f / fixedLineLength * lineLength;
            if (currentStage.stageType != ProceduralStage.EStageType.LayoutFloatSize)wallLengthMultiplayer = 1;


            for (int wallIndex = 0; wallIndex < wallsInEdge.Count; wallIndex++)
            {
                var currentWall = wallsInEdge[wallIndex];
                //Priorityze corner if we can
                GameObject wallInstance = null;

                if (currentStage.stageType == ProceduralStage.EStageType.LayoutRealSize)cornersLength = (lineLength - totalWallsLength) / 2f;

                #region Corners
                if (currentWall.wallType == WallData.EWallType.ProceduralCorner)
                {
                    corners++;
                    Vector3 startPoint = line.p1 + lineDirrection * spaceTakenByWalls + (Vector3.up * stageHeight);

                    wallInstance = ProceduralFace.GenerateFace(new Vector3[4] {
                    startPoint + lineDirrection * cornersLength,
                    startPoint,
                    startPoint + (Vector3.up * stageMaxHeight)+ lineDirrection * cornersLength,
                    startPoint + (Vector3.up * stageMaxHeight),
                     }, b.transform, currentStage.proceduralCornersMat, b.ProceduralWallsUVScale);
                    spaceTakenByWalls += cornersLength;
                }
                
                #endregion
                if (wallInstance == null)
                {
                    Vector3 startPoint = line.p1;
                    float addWallSize = 0f;
                    if (!reverseNormal){
                        startPoint = line.p1;
                        addWallSize = currentWall.wallSize.x;
                    } 
                    wallInstance = Instantiate(currentWall.m_GameObject, startPoint + lineDirrection * spaceTakenByWalls + lineDirrection * currentWall.wallSize.x + (Vector3.up * stageHeight), Quaternion.LookRotation(line.normalDirrection), b.transform);
                    if (currentWall.m_meshFilter == null) currentWall.m_meshFilter = currentWall.m_GameObject.GetComponent<MeshFilter>();
                    spaceTakenByWalls += currentWall.wallSize.x;
                    
                }
                wallInstance.hideFlags = b._hideFlags;
                foreach (Transform child in wallInstance.transform)
                {
                    child.gameObject.isStatic = b.isAllWallsStatic;
                }
                wallInstance.isStatic = b.isAllWallsStatic;
                wallInstance.transform.parent = lineGroup;
                
                MeshFilter mf = wallInstance.GetComponent<MeshFilter>();
                MeshRenderer mr = wallInstance.GetComponent<MeshRenderer>();
                
                
                float randomValue = Mathf.PerlinNoise(mf.transform.position.x * 100, mf.transform.position.y * 100);
                windowIndex++;
                
                if (randomValue <= windowEmissions)
                {
                    if (windowsPropertyBlock == null){
                        windowsPropertyBlock = new MaterialPropertyBlock();
                    } 
                    windowsPropertyBlock.SetColor(windowEmissionFieldName , windowEmissionColor);
                    mr.SetPropertyBlock(windowsPropertyBlock);
                    emittedWindows.Add(mr);
                }

                /// <summary>
                /// This part of code will rotate vertices of extreme objects, to weld same objects with other edges of building 
                /// </summary>
                /// <param name="corners"></param>
                /// <returns></returns>
                #region ROTATE CORNER VERTICES
                if (currentStage.connectCornerVertices && corners == 0 && mf.sharedMesh.isReadable)
                {
                    Edge prevEdge = null;
                    Edge curEdge = m_edges[i];
                    if (i == 0) prevEdge = m_edges[m_edges.Count -1];
                    else prevEdge = m_edges[i - 1];

                    Edge nextEdge = null;
                    if (i >= m_edges.Count -1) nextEdge = m_edges[0];
                    else nextEdge = m_edges[i + 1];
                    
                    Mesh newMesh = Instantiate( mf.sharedMesh);
                    List<Vector3> vertices = newMesh.vertices.ToList();
                    Vector3 vert;
                    bool skipFirstWalls = closeEdges == false && i == 0;

                    //Rotate first wall vertices 
                    if (wallIndex == 0 && !skipFirstWalls)
                    {
                        newMesh.name += $"prevEdge:[{prevEdge.index}]";
                        for (int v = 0; v < vertices.Count ; v++)
                        {
                            if (Mathf.Abs( vertices[v].x) > newMesh.bounds.size.x - 0.01f)
                            {
                                
                                float angle = Vector3.Angle(prevEdge.Dir.normalized , curEdge.Dir.normalized);

                                //Если угол стены внутренний, то двигаем в другую сторону
                                float lineDot = Vector3.Dot(prevEdge.Dir.normalized, curEdge.normalDirrection);
                                float cornerDotMultiplayer = 1f;
                                if (lineDot < 0) cornerDotMultiplayer -= 1f;

                                vert = vertices[v];
                                float b_len = vert.z;
                                float add = b_len * -Mathf.Tan(Mathf.Deg2Rad * (angle / 2f )) * cornerDotMultiplayer;

                                vert.x +=  add / wallLengthMultiplayer;

                                vertices[v] = vert;
                            }
                        }
                    }
                    //Rotate last wall vertices
                    if (wallIndex == wallsInEdge.Count - 1)
                    {
                        newMesh.name += $"|NextEdge: {nextEdge.index}";
                        Vector3 add = Vector3.zero;
                        for (int v = 0; v < vertices.Count ; v++)
                        {
                            //точки, ближние к p1
                            if (Mathf.Abs( vertices[v].x) < 0.01f)
                            {
                                float angle = Vector3.Angle(nextEdge.Dir.normalized , curEdge.Dir.normalized);
                                if (closeEdges == false && i == m_edges.Count - 1) angle = 0;
                                vert =  vertices[v];
                                float needLength = lineLength - Mathf.Abs( newMesh.bounds.size.x);


                                //Если угол стены внутренний, то двигаем в другую сторону
                                float lineDot = Vector3.Dot(nextEdge.Dir.normalized  , curEdge.normalDirrection);
                                float cornerDotMultiplayer = 1f;
                                if (lineDot > 0 ) cornerDotMultiplayer -= 1f;
                                
                                if (currentStage.stageType == ProceduralStage.EStageType.CorniceSingleMeshScaledByEdge)
                                {
                                    add = curEdge.Dir.normalized * -needLength;
                                }
                                float b_len = vert.z;
                                vert.x += (b_len * Mathf.Tan(Mathf.Deg2Rad * (angle / 2f))) / wallLengthMultiplayer * cornerDotMultiplayer;

                                vertices[v] = vert - mf.transform.InverseTransformDirection( add );
                            }
                        }
                        if (i == m_edges.Count - 1)
                        {
                            Debug.DrawRay(m_edges[i].p1, m_edges[i].normalDirrection.normalized * 4f, Color.magenta , 0.1f);
                            Debug.DrawLine(m_edges[i].p1 + m_edges[i].normalDirrection.normalized * 4f,  nextEdge.p1, Color.magenta , 0.1f);
                        }
                    }
                    newMesh.vertices = vertices.ToArray();

                    newMesh.RecalculateTangents();
                    newMesh.RecalculateBounds();
                    mf.mesh = newMesh;
                }else if (mf.sharedMesh.isReadable == false && corners == 0)
                {
                    Debug.LogError($"Cant connect corner vertices of mesh: {mf.sharedMesh.name}, set -> Read/Write Enabled in import settings");
                } 
                #endregion

                if (stageIndex < b.collisionStagesCount)
                {
                    if (stageIndex == 0)wallInstance.AddComponent<MeshCollider>();
                    else wallInstance.AddComponent<BoxCollider>();
                }
            }

            //Scale line group to match edge length with total geometry length in edge
            if (isRoof == false)
            {
                lineGroup.transform.localScale = new Vector3(wallLengthMultiplayer, 1f, 1f);
            }
        }
    }

    /// <summary>
    /// Calculate average length of all walls to predict total wall length
    /// Right now it not working well for large walls, use similar walls or not too different
    /// </summary>
    /// <param name="b"></param>
    /// <param name="stageIndex"></param>
    /// <returns></returns>
    float AverageLengthOfAllWalls(ProceduralBuilding b, int stageIndex)
    {
        return b.preset. buildingStages[stageIndex].lengthOfAllWallsX / b.stagesData[stageIndex].m_constructionWalls.Count;
    }

    // 简化模式标记 - 用于切换到简略Mesh生成模式
    [Header("生成模式设置")]
    public bool simplifiedMode = false;
    
    // Houdini HDA资源引用
    public Object houdiniHDAAsset;
    
    // 简化模式的参数
    [Header("简化模式参数")]
    public SimplifiedBuildingPreset simplifiedPreset = SimplifiedBuildingPreset.Office;
    public bool generateSimpleWindows = true;
    public bool generateSimpleFloors = true;
    public float simplifiedWindowDensity = 0.5f;
    public float simplifiedFloorHeight = 3.5f;
    public RoofType roofType = RoofType.Flat; // 屋顶类型
    
    // 存储简化模式的Mesh对象
    [SerializeField, HideInInspector] private GameObject simplifiedMeshObject;

    // PT简化建筑生成器
    private PTSimplifiedBuildingGenerator ptSimplifiedGenerator;

    // 自动重建控制
    public bool autoRebuildInEditor = false;

    // 缓存上一次的楼层数，用于检测变化
    private int lastStagesCount = 0;

    /// <summary>
    /// 根据选择的预设配置参数
    /// </summary>
    private void ConfigurePresetParameters()
    {
        switch (simplifiedPreset)
        {
            case SimplifiedBuildingPreset.Office:
                // 写字楼预设
                ptSimplifiedGenerator.buildingConfig.floors = stagesCount - 1;
                ptSimplifiedGenerator.buildingConfig.entranceInterval = 10f;
                ptSimplifiedGenerator.buildingConfig.hasAttic = false;
                ptSimplifiedGenerator.buildingConfig.roofConfig.type = roofType;
                ptSimplifiedGenerator.buildingConfig.palette.wallColor = new Color(0.8f, 0.8f, 0.85f);
                ptSimplifiedGenerator.buildingConfig.palette.frameColor = new Color(0.3f, 0.3f, 0.35f);
                ptSimplifiedGenerator.buildingConfig.palette.glassColor = new Color(0.1f, 0.3f, 0.5f, 0.7f);
                ptSimplifiedGenerator.buildingConfig.palette.roofColor = new Color(0.4f, 0.4f, 0.45f);
                ptSimplifiedGenerator.shopHeight = 4.5f;
                ptSimplifiedGenerator.shopWallColor = new Color(0.75f, 0.75f, 0.8f);
                ptSimplifiedGenerator.shopCeilingColor = new Color(0.7f, 0.7f, 0.75f);
                ptSimplifiedGenerator.upperWallColor = new Color(0.8f, 0.8f, 0.85f);
                ptSimplifiedGenerator.basementHeight = 0.6f;
                break;
                
            case SimplifiedBuildingPreset.Residential:
                // 住宅楼预设
                ptSimplifiedGenerator.buildingConfig.floors = stagesCount - 1;
                ptSimplifiedGenerator.buildingConfig.entranceInterval = 8f;
                ptSimplifiedGenerator.buildingConfig.hasAttic = true;
                ptSimplifiedGenerator.buildingConfig.roofConfig.type = RoofType.Hipped;
                ptSimplifiedGenerator.buildingConfig.palette.wallColor = new Color(0.9f, 0.85f, 0.8f);
                ptSimplifiedGenerator.buildingConfig.palette.frameColor = new Color(0.6f, 0.5f, 0.4f);
                ptSimplifiedGenerator.buildingConfig.palette.glassColor = new Color(0.2f, 0.3f, 0.4f, 0.6f);
                ptSimplifiedGenerator.buildingConfig.palette.roofColor = new Color(0.7f, 0.4f, 0.3f);
                ptSimplifiedGenerator.shopHeight = 4.0f;
                ptSimplifiedGenerator.shopWallColor = new Color(0.85f, 0.8f, 0.75f);
                ptSimplifiedGenerator.shopCeilingColor = new Color(0.75f, 0.7f, 0.65f);
                ptSimplifiedGenerator.upperWallColor = new Color(0.9f, 0.85f, 0.8f);
                ptSimplifiedGenerator.basementHeight = 0.4f;
                break;
                
            case SimplifiedBuildingPreset.Commercial:
                // 商业建筑预设
                ptSimplifiedGenerator.buildingConfig.floors = stagesCount - 1;
                ptSimplifiedGenerator.buildingConfig.entranceInterval = 6f;
                ptSimplifiedGenerator.buildingConfig.hasAttic = false;
                ptSimplifiedGenerator.buildingConfig.roofConfig.type = RoofType.Flat;
                ptSimplifiedGenerator.buildingConfig.palette.wallColor = new Color(0.85f, 0.85f, 0.9f);
                ptSimplifiedGenerator.buildingConfig.palette.frameColor = new Color(0.3f, 0.3f, 0.35f);
                ptSimplifiedGenerator.buildingConfig.palette.glassColor = new Color(0.1f, 0.2f, 0.4f, 0.8f);
                ptSimplifiedGenerator.buildingConfig.palette.roofColor = new Color(0.3f, 0.3f, 0.35f);
                ptSimplifiedGenerator.shopHeight = 5.0f;
                ptSimplifiedGenerator.shopWallColor = new Color(0.7f, 0.7f, 0.8f);
                ptSimplifiedGenerator.shopCeilingColor = new Color(0.65f, 0.65f, 0.7f);
                ptSimplifiedGenerator.upperWallColor = new Color(0.85f, 0.85f, 0.9f);
                ptSimplifiedGenerator.basementHeight = 0.8f;
                break;
                
            case SimplifiedBuildingPreset.Factory:
                // 工厂预设
                ptSimplifiedGenerator.buildingConfig.floors = Mathf.Max(1, stagesCount - 1);
                ptSimplifiedGenerator.buildingConfig.entranceInterval = 15f;
                ptSimplifiedGenerator.buildingConfig.hasAttic = false;
                ptSimplifiedGenerator.buildingConfig.roofConfig.type = RoofType.Gabled;
                ptSimplifiedGenerator.buildingConfig.palette.wallColor = new Color(0.7f, 0.7f, 0.65f);
                ptSimplifiedGenerator.buildingConfig.palette.frameColor = new Color(0.4f, 0.4f, 0.35f);
                ptSimplifiedGenerator.buildingConfig.palette.glassColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);
                ptSimplifiedGenerator.buildingConfig.palette.roofColor = new Color(0.5f, 0.5f, 0.45f);
                ptSimplifiedGenerator.shopHeight = 6.0f;
                ptSimplifiedGenerator.shopWallColor = new Color(0.65f, 0.65f, 0.6f);
                ptSimplifiedGenerator.shopCeilingColor = new Color(0.6f, 0.6f, 0.55f);
                ptSimplifiedGenerator.upperWallColor = new Color(0.7f, 0.7f, 0.65f);
                ptSimplifiedGenerator.basementHeight = 0.5f;
                break;
                
            case SimplifiedBuildingPreset.School:
                // 学校预设
                ptSimplifiedGenerator.buildingConfig.floors = stagesCount - 1;
                ptSimplifiedGenerator.buildingConfig.entranceInterval = 12f;
                ptSimplifiedGenerator.buildingConfig.hasAttic = false;
                ptSimplifiedGenerator.buildingConfig.roofConfig.type = RoofType.Flat;
                ptSimplifiedGenerator.buildingConfig.palette.wallColor = new Color(0.95f, 0.9f, 0.85f);
                ptSimplifiedGenerator.buildingConfig.palette.frameColor = new Color(0.6f, 0.55f, 0.5f);
                ptSimplifiedGenerator.buildingConfig.palette.glassColor = new Color(0.3f, 0.5f, 0.7f, 0.6f);
                ptSimplifiedGenerator.buildingConfig.palette.roofColor = new Color(0.6f, 0.55f, 0.5f);
                ptSimplifiedGenerator.shopHeight = 4.5f;
                ptSimplifiedGenerator.shopWallColor = new Color(0.9f, 0.85f, 0.8f);
                ptSimplifiedGenerator.shopCeilingColor = new Color(0.8f, 0.75f, 0.7f);
                ptSimplifiedGenerator.upperWallColor = new Color(0.95f, 0.9f, 0.85f);
                ptSimplifiedGenerator.basementHeight = 0.5f;
                break;
                
            case SimplifiedBuildingPreset.Hospital:
                // 医院预设
                ptSimplifiedGenerator.buildingConfig.floors = stagesCount - 1;
                ptSimplifiedGenerator.buildingConfig.entranceInterval = 14f;
                ptSimplifiedGenerator.buildingConfig.hasAttic = false;
                ptSimplifiedGenerator.buildingConfig.roofConfig.type = RoofType.Flat;
                ptSimplifiedGenerator.buildingConfig.palette.wallColor = new Color(0.9f, 0.9f, 0.95f);
                ptSimplifiedGenerator.buildingConfig.palette.frameColor = new Color(0.5f, 0.5f, 0.55f);
                ptSimplifiedGenerator.buildingConfig.palette.glassColor = new Color(0.3f, 0.5f, 0.7f, 0.7f);
                ptSimplifiedGenerator.buildingConfig.palette.roofColor = new Color(0.6f, 0.6f, 0.65f);
                ptSimplifiedGenerator.shopHeight = 5.0f;
                ptSimplifiedGenerator.shopWallColor = new Color(0.85f, 0.85f, 0.9f);
                ptSimplifiedGenerator.shopCeilingColor = new Color(0.75f, 0.75f, 0.8f);
                ptSimplifiedGenerator.upperWallColor = new Color(0.9f, 0.9f, 0.95f);
                ptSimplifiedGenerator.basementHeight = 0.7f;
                break;
                
            case SimplifiedBuildingPreset.Custom:
            default:
                // 自定义设置，使用当前设置
                ptSimplifiedGenerator.buildingConfig.floors = stagesCount - 1;
                ptSimplifiedGenerator.buildingConfig.roofConfig.type = roofType;
                // 这里不做其他修改，保留当前设置
                break;
        }
        
        // 根据用户选择强制设置屋顶类型
        if (simplifiedPreset != SimplifiedBuildingPreset.Custom) {
            // 如果用户明确选择了屋顶类型，覆盖预设的屋顶类型
            ptSimplifiedGenerator.buildingConfig.roofConfig.type = roofType;
        }
    }
}

/// <summary>
/// Class for each walls prefabs to store their data for fast access
/// </summary>
[System.Serializable]
public class WallData
{
    public MeshFilter m_meshFilter;
    public GameObject m_GameObject;
    public Vector2 wallSize;
    public int stageIndex;
    public EWallType wallType = EWallType.Wall;
    public enum EWallType
    {
        Wall,
        ProceduralCorner,
        SpecialWall
    }
    public bool isDirty;
}

/// <summary>
/// Procedural stage contains all raw data from inspector, then that data will be collected for procedural data to fast access
/// </summary>
[System.Serializable]
public class ProceduralStage
{
    public string nameOfStage = "stage";
    public List<GameObject> stageWallsPrefabs = new List<GameObject>();
    public List<GameObject> stageSpecialWalls = new List<GameObject>();
    [HideInInspector]public List<GameObject> stageCornersPrefabs = new List<GameObject>();

    // 编辑器相关字段
    [HideInInspector] public bool editorFoldout = false;
    
    [Header("Specail walls settings, like Entrance in first floor")]
    [Range(1, 10)]public int specialWallsEach = 2;
    public int minimumWallsShouldBe = 3;
    [Range(0.1f,3f)]public float proceduralCornersLength = 0.8f;
    internal float CornersLength
    {
        get
        {
            if (cornersType == ECornersType.ProceduralCorners) return proceduralCornersLength;
            else return 0f;
        }
    }
    public ECornersType cornersType = ECornersType.ProceduralCorners;
    public EStageType stageType = EStageType.LayoutRealSize;

    /// <summary>
    /// Procedural corners will generate quads in both sides of wall
    /// NoCorners means only walls will be placed along edge
    /// </summary>
    public enum ECornersType
    {
        ProceduralCorners,
        NoCorners
    }
    public bool connectCornerVertices = true;

    [Range(1 , 100)]public int repeatCount = 1;
    [HideInInspector]public int stageIndex;


    [HideInInspector]public float lengthOfAllWallsX = 0f;
    [HideInInspector]public float lengthOfAllCornersX =0f;
    
    /// <summary>
    /// Real size - no stretch for walls,
    /// FloatSize - stratch walls along edge to fill the wall
    /// Single mesh, scaled like cornise along all edge/wall
    /// </summary>
    public enum EStageType
    {
        LayoutRealSize,
        LayoutFloatSize,
        CorniceSingleMeshScaledByEdge,
    }
    public Material proceduralCornersMat;
    public Material proceduralRoofMaterial;
    
    [Header("楼层高度设置")]
    [Tooltip("是否使用固定高度而非自动计算")]
    public bool useFixedHeight = false;
    [Range(1.0f, 10.0f)]
    [Tooltip("当启用固定高度时使用的楼层高度值")]
    public float fixedHeight = 3.0f;
}

/// <summary>
/// Procedural stages data collect all serializable building walls and other data for fast access and fast calculate whole building 
/// </summary>
[System.Serializable]
public class ProceduralStageData
{
    public ProceduralStage m_stage;

    public List<WallData> m_constructionWalls = new List<WallData>();
    public List<WallData> m_constructionCorners = new List<WallData>();

    /// <summary>
    /// Building will be regenerated for dirty stages and walls 
    /// </summary>
    public void SetAllWallsDirty()
    {
        foreach (WallData w in m_constructionWalls) w.isDirty = true;
        foreach (WallData w in m_constructionCorners) w.isDirty = true;
    }
}

/// <summary>
/// Class contains data for single edge of walls 
/// </summary>
public class Edge
{
    public Vector3 p1;
    public Vector3 p2;
    public Vector3 normalDirrection;

    public Vector3 Dir => p2 - p1;

    public int index;

    public Edge(Vector3 _p1, Vector2 _p2 , Vector3 normal , int index)
    {
        normalDirrection = normal;
        p1 = _p1;
        p2 = _p2;
        this.index = index;
    }
    public Edge()
    {

    }
}

// 在RoofType枚举声明之前添加建筑预设枚举
[System.Serializable]
public enum SimplifiedBuildingPreset
{
    Custom,     // 自定义设置
    Office,     // 写字楼
    Residential,// 住宅楼
    Commercial, // 商业建筑
    Factory,    // 工厂
    School,     // 学校
    Hospital    // 医院
}
}