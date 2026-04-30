#if UNITY_EDITOR
using System;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Splines;
using Unity.Mathematics;
using Unity.Splines.Examples;
using UnityEditor;
using UnityEditor.Splines;
using UnityEditor.Splines.Extension;
using System.Linq;
using CunningEngine;

namespace Unity.Splines.Examples
{
    [ExecuteInEditMode]
    public class JunctionData : MonoBehaviour, ICunningPolylineSource, ICunningPolylinePrimitiveIntTagSource
    {
        const int JunctionRoadPolylineKind = 0;

        // Gizmo显示控制
        public static bool ShowGizmos { get; set; } = true;
        public static bool ShowCenterPoints { get; set; } = true;      // 显示路口中心点
        public static bool ShowEdgePoints { get; set; } = true;        // 显示路口边缘点
        public static bool ShowEdgeLines { get; set; } = true;         // 显示路口边缘线
        public static bool ShowSidewalkLines { get; set; } = true;     // 显示人行道线
        public static bool ShowNormals { get; set; } = true;           // 显示法线
        public static bool ShowLabels { get; set; } = true;            // 显示标签
        public static bool ShowCurves { get; set; } = true;            // 显示曲线
        [SerializeField]
        public bool editMode = false;                                  // 编辑模式开关
        private bool ignoreRoadUpdates = false;                        // 是否忽略来自道路的更新
        [SerializeField]
        [HideInInspector]
        private bool isPreview = false;

        [SerializeField]
        [HideInInspector]
        private bool m_IsImplicitAutoJunction = false;

        [SerializeField]
        [HideInInspector]
        private string m_ImplicitAutoSignature = string.Empty;

        public bool IsPreview => isPreview;
        public bool IsImplicitAutoJunction => m_IsImplicitAutoJunction;
        public string ImplicitAutoSignature => m_ImplicitAutoSignature;

        // 添加自定义参数开关
        [SerializeField]
        [Tooltip("启用自定义参数而不从连接的道路继承参数")]
        public bool useCustomParameters = false;                       // 是否使用自定义参数

        // 添加路缘相关参数(独立参数)
        [SerializeField]
        [Tooltip("启用路缘")]
        public bool enableCurb = true;
        
        [SerializeField]
        [Tooltip("路缘高度")]
        [Range(0f, 0.5f)]
        public float curbHeight = 0.1f;
        
        [SerializeField]
        [Tooltip("路缘宽度")]
        [Range(0.1f, 2f)]
        public float curbWidth = 0.3f;
        
        [SerializeField]
        [Tooltip("路缘倒角宽度")]
        [Range(0.01f, 0.5f)]
        public float curbChamferWidth = 0.05f;
        
        [SerializeField]
        [Tooltip("路缘倒角高度")]
        [Range(0.01f, 0.5f)]
        public float curbChamferHeight = 0.05f;
        
        // 添加马路牙子相关参数(独立参数)
        [SerializeField]
        [Tooltip("启用马路牙子")]
        public bool enableRoadEdge = true;
        
        [SerializeField]
        [Tooltip("马路牙子高度")]
        [Range(0.05f, 0.5f)]
        public float roadEdgeHeight = 0.15f;
        
        [SerializeField]
        [Tooltip("马路牙子宽度")]
        [Range(0.1f, 2f)]
        public float roadEdgeWidth = 0.3f;
        
        [SerializeField]
        [Tooltip("马路牙子倒角宽度")]
        [Range(0.01f, 0.5f)]
        public float roadEdgeChamferWidth = 0.05f;
        
        [SerializeField]
        [Tooltip("马路牙子倒角高度")]
        [Range(0.01f, 0.5f)]
        public float roadEdgeChamferHeight = 0.05f;

        // 斑马线缩放系数
        [SerializeField]
        [Tooltip("斑马线宽度缩放系数")]
        [Range(0.5f, 2.0f)]
        public float zebraCrossingScaleX = 1.0f;                       // 斑马线X轴缩放系数
        
        [SerializeField]
        [Tooltip("斑马线长度缩放系数")]
        [Range(0.5f, 2.0f)]
        public float zebraCrossingScaleZ = 1.0f;                       // 斑马线Z轴缩放系数

        public List<ConnectedRoad> connectedRoads = new List<ConnectedRoad>();
        public float curveParameter = 0.3f;
        public int curveSegments = 10; // 每条曲线的分段数
        private float maxWidth = 10f; // UV计算用的最大宽度
        
        // 添加选中状态追踪
        [HideInInspector]
        public int selectedRoadIndex = -1;
        [HideInInspector]
        public bool isPoint0Selected = false;
        
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh mesh;
        private Material sidewalkMaterial; // 人行道材质
        private Material roadEdgeMaterial; // 马路牙子材质
        private Material curbMaterial; // 路缘材质
        
        private bool hasLoggedConnectionCount = false;
        
        // 添加子对象的引用
        private GameObject sidewalkObject;
        private GameObject curbObject;
        private GameObject roadEdgeObject;
        private GameObject roadEdgeOuterObject; // 添加人行道外侧马路牙子对象引用

        [SerializeField]
        [HideInInspector]
        private CunningMesh m_CunningRoadMesh;

        [SerializeField]
        [HideInInspector]
        private CunningMesh m_SidewalkCunningMesh;

        [NonSerialized]
        private ulong m_CunningRoadHandle;

        [NonSerialized]
        private ulong m_CunningSidewalkHandle;

        private float[] m_CunningFlatPositionsBuffer = Array.Empty<float>();
        private float[] m_CunningFlatNormalsBuffer = Array.Empty<float>();
        private float[] m_CunningFlatUvBuffer = Array.Empty<float>();
        private ulong[] m_CunningPointIdBuffer = Array.Empty<ulong>();
        private ulong[] m_CunningPolyPointIdBuffer = Array.Empty<ulong>();
        private uint[] m_CunningPolyOffsetBuffer = Array.Empty<uint>();
        private int[] m_CunningPrimitiveMaterialIndexBuffer = Array.Empty<int>();
        private int[] m_CunningPrimitiveAreaIndexBuffer = Array.Empty<int>();

        private enum JunctionCunningAreaType
        {
            RoadSurface = 0,
            Sidewalk = 1,
            Curb = 2,
            RoadEdge = 3,
            RoadEdgeOuter = 4,
        }

        private enum JunctionSidewalkMaterialSlot
        {
            Sidewalk = 0,
            Curb = 1,
            RoadEdge = 2,
            RoadEdgeOuter = 3,
        }

        private static readonly int[][] s_SidewalkQuadPolygons =
        {
            new[] { 0, 1, 3, 2 },
        };

        private static readonly int[][] s_ChamferStripPolygons =
        {
            new[] { 0, 1, 4, 3 },
            new[] { 1, 2, 5, 4 },
            new[] { 6, 8, 9, 7 },
            new[] { 0, 3, 8, 6 },
            new[] { 2, 7, 9, 5 },
        };
        
        // 添加缺失的UV属性
        [SerializeField]
        [Range(0.1f, 10f)]
        [Tooltip("路缘U方向缩放值")]
        public float curbUVScaleU = 0.5f;

        [SerializeField]
        [Range(0.1f, 10f)]
        [Tooltip("路缘V方向缩放值")]
        public float curbUVScaleV = 1.0f;

        [SerializeField]
        [Range(-1f, 1f)]
        [Tooltip("路缘UV水平偏移")]
        public float curbUVOffset = 0f;
        
        // 添加马路牙子UV属性
        [SerializeField]
        [Range(0.1f, 10f)]
        [Tooltip("马路牙子U方向缩放值")]
        public float roadEdgeUVScaleU = 0.5f;

        [SerializeField]
        [Range(0.1f, 10f)]
        [Tooltip("马路牙子V方向缩放值")]
        public float roadEdgeUVScaleV = 1.0f;

        [SerializeField]
        [Range(-1f, 1f)]
        [Tooltip("马路牙子UV水平偏移")]
        public float roadEdgeUVOffset = 0f;

        [SerializeField]
        [Tooltip("路缘顶面平滑组ID")]
        public int curbTopSmoothingGroup = 1;

        [SerializeField]
        [Tooltip("路缘倒角平滑组ID")]
        public int curbChamferSmoothingGroup = 2;

        [SerializeField]
        [Tooltip("路缘侧面平滑组ID")]
        public int curbSideSmoothingGroup = 3;

        [SerializeField]
        [Tooltip("马路牙子顶面平滑组ID")]
        public int roadEdgeTopSmoothingGroup = 4;

        [SerializeField]
        [Tooltip("马路牙子倒角平滑组ID")]
        public int roadEdgeChamferSmoothingGroup = 5;

        [SerializeField]
        [Tooltip("马路牙子侧面平滑组ID")]
        public int roadEdgeSideSmoothingGroup = 6;

        // 添加车道连接控制器
        private JunctionLaneConnector laneConnector;
        public static bool ShowLaneConnections { get; set; } = true;

        // 添加LtRVector相关字段
        private Dictionary<ConnectedRoad, (Vector3 ltRVector, float length)> roadLtRVectors = new Dictionary<ConnectedRoad, (Vector3, float)>();

        public void SetPreviewMode(bool preview)
        {
            isPreview = preview;
            editMode = preview;
            ignoreRoadUpdates = preview;
        }

        internal void SetImplicitAutoState(bool isImplicitAutoJunction, string signature)
        {
            m_IsImplicitAutoJunction = isImplicitAutoJunction;
            m_ImplicitAutoSignature = signature ?? string.Empty;
        }

        private void OnValidate()
        {
            // 如果在编辑模式下且设置为忽略更新，则不执行更新
            if (editMode && ignoreRoadUpdates && !isPreview)
            {
                return;
            }

            // 更新UV参数（从连接的道路中获取）
            UpdateUVParametersFromConnectedRoads();
            
            // 确保在编辑器中且游戏未运行时才更新
            if (!Application.isPlaying)
            {
                // 使用延迟调用来更新斑马线，避免在OnValidate中直接销毁GameObjects
                EditorApplication.delayCall += () => {
                    if (this != null) // 检查对象是否仍然存在
                    {
                JunctionGenerator.UpdateZebraCrossings(this);
                
                // 强制刷新场景视图
                UnityEditor.SceneView.RepaintAll();
                    }
                };
            }
        }

        // 添加从连接道路更新UV参数的方法
        private void UpdateUVParametersFromConnectedRoads()
        {
            // 如果使用自定义参数，不从道路同步参数
            if (useCustomParameters)
                return;
                
            if (connectedRoads == null || connectedRoads.Count == 0)
                return;

            // 用于计算平均值的临时变量
            float totalCurbUVScaleU = 0f;
            float totalCurbUVScaleV = 0f;
            float totalCurbUVOffset = 0f;
            float totalRoadEdgeUVScaleU = 0f;
            float totalRoadEdgeUVScaleV = 0f;
            float totalRoadEdgeUVOffset = 0f;
            float totalCurbHeight = 0f;
            float totalCurbWidth = 0f;
            float totalCurbChamferWidth = 0f;
            float totalCurbChamferHeight = 0f;
            float totalRoadEdgeHeight = 0f;
            float totalRoadEdgeWidth = 0f;
            float totalRoadEdgeChamferWidth = 0f;
            float totalRoadEdgeChamferHeight = 0f;
            bool totalEnableCurb = false;
            bool totalEnableRoadEdge = false;
            int validRoadCount = 0;
            int curbEnabledCount = 0;
            int roadEdgeEnabledCount = 0;

            // 遍历所有连接的道路
            foreach (var road in connectedRoads)
            {
                if (road == null || !road.TryGetRoadData(out var roadData))
                    continue;

                // 收集路缘参数
                if (roadData.enableCurb)
                {
                    totalCurbUVScaleU += roadData.curbUVScaleU;
                    totalCurbUVScaleV += roadData.curbUVScaleV;
                    totalCurbUVOffset += roadData.uvHorizontalOffset;
                    totalCurbHeight += roadData.curbHeight;
                    totalCurbWidth += roadData.curbWidth;
                    totalCurbChamferWidth += roadData.curbChamferWidth;
                    totalCurbChamferHeight += roadData.curbChamferHeight;
                    totalEnableCurb = true;
                    curbEnabledCount++;
                }
                
                // 收集马路牙子参数
                if (roadData.enableRoadEdge)
                {
                    totalRoadEdgeUVScaleU += roadData.roadEdgeUVScaleU;
                    totalRoadEdgeUVScaleV += roadData.roadEdgeUVScaleV;
                    totalRoadEdgeUVOffset += roadData.uvHorizontalOffset;
                    totalRoadEdgeHeight += roadData.roadEdgeHeight;
                    totalRoadEdgeWidth += roadData.roadEdgeWidth;
                    totalRoadEdgeChamferWidth += roadData.roadEdgeChamferWidth;
                    totalRoadEdgeChamferHeight += roadData.roadEdgeChamferHeight;
                    totalEnableRoadEdge = true;
                    roadEdgeEnabledCount++;
                }
                
                validRoadCount++;
            }

            // 确保至少有一条有效的道路
            if (validRoadCount > 0)
            {
                // 计算并设置平均UV参数
                if (curbEnabledCount > 0)
                {
                    curbUVScaleU = totalCurbUVScaleU / curbEnabledCount;
                    curbUVScaleV = totalCurbUVScaleV / curbEnabledCount;
                    curbUVOffset = totalCurbUVOffset / curbEnabledCount;
                    curbHeight = totalCurbHeight / curbEnabledCount;
                    curbWidth = totalCurbWidth / curbEnabledCount;
                    curbChamferWidth = totalCurbChamferWidth / curbEnabledCount;
                    curbChamferHeight = totalCurbChamferHeight / curbEnabledCount;
                }
                
                if (roadEdgeEnabledCount > 0)
                {
                    roadEdgeUVScaleU = totalRoadEdgeUVScaleU / roadEdgeEnabledCount;
                    roadEdgeUVScaleV = totalRoadEdgeUVScaleV / roadEdgeEnabledCount;
                    roadEdgeUVOffset = totalRoadEdgeUVOffset / roadEdgeEnabledCount;
                    roadEdgeHeight = totalRoadEdgeHeight / roadEdgeEnabledCount;
                    roadEdgeWidth = totalRoadEdgeWidth / roadEdgeEnabledCount;
                    roadEdgeChamferWidth = totalRoadEdgeChamferWidth / roadEdgeEnabledCount;
                    roadEdgeChamferHeight = totalRoadEdgeChamferHeight / roadEdgeEnabledCount;
                }
                
                enableCurb = totalEnableCurb;
                enableRoadEdge = totalEnableRoadEdge;
            }
        }
        
        [System.Serializable]
        public struct EdgePoints
        {
            public Vector3 point0;
            public Vector3 point1;
            public Vector3 normal;
            public Vector3 normal0;
            public Vector3 normal1;
            public Vector3 point0CurbExtended;
            public Vector3 point1CurbExtended;
            public Vector3 point0RoadEdgeExtended;
            public Vector3 point1RoadEdgeExtended;
            public Vector3 point0SidewalkExtended;
            public Vector3 point1SidewalkExtended;
            public List<Vector3> leftSidewalkPoints;   // 左侧人行道点列表
            public List<Vector3> rightSidewalkPoints;  // 右侧人行道点列表

            public EdgePoints(Vector3 p0, Vector3 p1, Vector3 n, Vector3 n0, Vector3 n1,
                             Vector3 p0c, Vector3 p1c, Vector3 p0r, Vector3 p1r, Vector3 p0s, Vector3 p1s)
            {
                point0 = p0;
                point1 = p1;
                normal = n;
                normal0 = n0;
                normal1 = n1;
                point0CurbExtended = p0c;
                point1CurbExtended = p1c;
                point0RoadEdgeExtended = p0r;
                point1RoadEdgeExtended = p1r;
                point0SidewalkExtended = p0s;
                point1SidewalkExtended = p1s;
                leftSidewalkPoints = new List<Vector3> { p0s, p0r };   // 初始化左侧人行道点列表
                rightSidewalkPoints = new List<Vector3> { p1s, p1r };  // 初始化右侧人行道点列表
            }
        }
        
        [System.Serializable]
        public class ConnectedRoad
        {
            public LoftRoadBehaviour roadBehaviour;
            public int splineIndex;
            public int knotIndex;
            public long markerId;
            public float cachedCurveU;
            public int cachedPreferredKnotIndex = -1;
            public float width;
            public float point0NormalAngle = 0f; // 左侧点(0点)的法线旋转角度
            public float point1NormalAngle = 0f; // 右侧点(1点)的法线旋转角度
            
            public bool HasValidSplineReference()
            {
                return roadBehaviour != null &&
                    roadBehaviour.Container != null &&
                    roadBehaviour.Container.Splines != null &&
                    splineIndex >= 0 &&
                    splineIndex < roadBehaviour.Container.Splines.Count &&
                    roadBehaviour.TryResolveConnectedRoadBinding(
                        markerId,
                        splineIndex,
                        knotIndex,
                        cachedCurveU,
                        cachedPreferredKnotIndex,
                        out _,
                        out _,
                        out _,
                        out _);
            }

            public bool TryGetRoadData(out LoftRoadExtensionData roadData)
            {
                roadData = null;
                return roadBehaviour != null &&
                    roadBehaviour.RoadExtensionDatas != null &&
                    splineIndex >= 0 &&
                    splineIndex < roadBehaviour.RoadExtensionDatas.Count &&
                    (roadData = roadBehaviour.RoadExtensionDatas[splineIndex]) != null;
            }

            public bool TryGetMeshOffset(out float meshOffset)
            {
                meshOffset = 0f;
                if (!TryGetRoadData(out var roadData))
                    return false;

                meshOffset = roadData.meshOffset;
                return true;
            }

            public Vector3 GetConnectionPoint()
            {
                if (!roadBehaviour.TryResolveConnectedRoadBinding(
                    markerId,
                    splineIndex,
                    knotIndex,
                    cachedCurveU,
                    cachedPreferredKnotIndex,
                    out _,
                    out _,
                    out float curveU,
                    out _))
                {
                    return Vector3.zero;
                }

                return roadBehaviour.EvaluateSplineWorldPosition(splineIndex, curveU);
            }
            
            public Vector3 GetNormalDirection(Vector3 junctionCenter)
            {
                if (!roadBehaviour.TryResolveConnectedRoadBinding(
                    markerId,
                    splineIndex,
                    knotIndex,
                    cachedCurveU,
                    cachedPreferredKnotIndex,
                    out var spline,
                    out var marker,
                    out float curveU,
                    out int preferredKnotIndex))
                {
                    return Vector3.forward;
                }

                Vector3 currentPos = roadBehaviour.EvaluateSplineWorldPosition(splineIndex, curveU);
                if (marker != null && marker.isPinnedToKnot && preferredKnotIndex >= 0 && preferredKnotIndex < spline.Count)
                {
                    Quaternion knotRotation = spline[preferredKnotIndex].Rotation;
                    if (knotRotation != Quaternion.identity)
                    {
                        Vector3 rotatedDirection = roadBehaviour.transform.TransformDirection(knotRotation * Vector3.forward);
                        rotatedDirection.y = 0;
                        rotatedDirection = rotatedDirection.normalized;

                        Vector3 centerToRotatedPoint = (currentPos - junctionCenter).normalized;
                        centerToRotatedPoint.y = 0;
                        if (Vector3.Dot(rotatedDirection, centerToRotatedPoint) > 0)
                        {
                            rotatedDirection = -rotatedDirection;
                        }

                        return rotatedDirection;
                    }
                }

                Vector3 roadDirection = roadBehaviour.EvaluateSplineWorldTangent(splineIndex, curveU);
                if (roadDirection.sqrMagnitude <= Mathf.Epsilon)
                {
                    return (junctionCenter - currentPos).normalized;
                }

                Vector3 centerToPoint = (currentPos - junctionCenter).normalized;
                centerToPoint.y = 0;
                if (Vector3.Dot(roadDirection, centerToPoint) > 0)
                {
                    roadDirection = -roadDirection;
                }
                
                return roadDirection.normalized;
            }

            public EdgePoints GetEdgePoints(Vector3 junctionCenter)
            {
                Vector3 center = GetConnectionPoint();
                Vector3 normal = GetNormalDirection(junctionCenter);
                Vector3 right = Vector3.Cross(normal, Vector3.up).normalized;
                
                // 计算两侧点
                Vector3 point0 = center - right * (width * 0.5f);  // 左边点(0点)
                Vector3 point1 = center + right * (width * 0.5f);   // 右边点(1点)
                
                // 获取道路数据和各种宽度
                float curbWidth = 0f;
                float roadEdgeWidth = 0f;
                float leftSidewalkWidth = 0f;
                float rightSidewalkWidth = 0f;

                if (TryGetRoadData(out var roadData))
                {
                    // 路缘宽度
                    if (roadData.enableCurb)
                        curbWidth = roadData.curbWidth;
                        
                    // 马路牙子宽度
                    if (roadData.enableRoadEdge)
                        roadEdgeWidth = roadData.roadEdgeWidth;
                        
                    // 人行道宽度
                    leftSidewalkWidth = roadData.leftSidewalkWidth.DefaultValue;
                    rightSidewalkWidth = roadData.rightSidewalkWidth.DefaultValue;
                }

                // 计算各个延伸点
                // 1. 路缘延伸点
                Vector3 point0CurbExtended = point0 - right * curbWidth;
                Vector3 point1CurbExtended = point1 + right * curbWidth;

                // 2. 马路牙子延伸点
                Vector3 point0RoadEdgeExtended = point0CurbExtended - right * roadEdgeWidth;
                Vector3 point1RoadEdgeExtended = point1CurbExtended + right * roadEdgeWidth;

                // 3. 人行道延伸点
                Vector3 point0SidewalkExtended = point0RoadEdgeExtended - right * leftSidewalkWidth;
                Vector3 point1SidewalkExtended = point1RoadEdgeExtended + right * rightSidewalkWidth;
                
                // 计算从中心指向两侧点的方向
                Vector3 centerToPoint0 = (point0 - junctionCenter).normalized;
                Vector3 centerToPoint1 = (point1 - junctionCenter).normalized;
                centerToPoint0.y = 0;
                centerToPoint1.y = 0;
                
                // 如果两侧点的方向与法线方向一致,则调整法线
                if (Vector3.Dot(normal, centerToPoint0) > 0)
                {
                    normal = -normal;
                    right = -right;
                    // 重新计算所有点
                    point0 = center - right * (width * 0.5f);
                    point1 = center + right * (width * 0.5f);
                    
                    point0CurbExtended = point0 - right * curbWidth;
                    point1CurbExtended = point1 + right * curbWidth;
                    
                    point0RoadEdgeExtended = point0CurbExtended - right * roadEdgeWidth;
                    point1RoadEdgeExtended = point1CurbExtended + right * roadEdgeWidth;
                    
                    point0SidewalkExtended = point0RoadEdgeExtended - right * leftSidewalkWidth;
                    point1SidewalkExtended = point1RoadEdgeExtended + right * rightSidewalkWidth;
                }
                
                // 应用自定义旋转到边缘点的法线
                Quaternion rotation0 = Quaternion.AngleAxis(point0NormalAngle, Vector3.up);
                Quaternion rotation1 = Quaternion.AngleAxis(point1NormalAngle, Vector3.up);
                Vector3 normal0 = rotation0 * normal;
                Vector3 normal1 = rotation1 * normal;
                
                return new EdgePoints(point0, point1, normal, normal0, normal1,
                                    point0CurbExtended, point1CurbExtended,
                                    point0RoadEdgeExtended, point1RoadEdgeExtended,
                                    point0SidewalkExtended, point1SidewalkExtended);
            }
        }
        
        [System.Serializable]
        public class MeshVertex
        {
            public Vector3 position;
            public Vector3 normal;
            public Vector2 uv;
            public bool isSelected;

            public MeshVertex(Vector3 pos, Vector3 norm, Vector2 texCoord)
            {
                position = pos;
                normal = norm;
                uv = texCoord;
                isSelected = false;
            }
        }

        [SerializeField]
        private List<MeshVertex> meshVertices = new List<MeshVertex>();
        private List<int> meshTriangles = new List<int>();
        
        // 道路网格数据
        private List<Vector3> roadVertices = new List<Vector3>();
        private List<Vector3> roadNormals = new List<Vector3>();
        private List<Vector2> roadUvs = new List<Vector2>();
        private List<int> roadTriangles = new List<int>();

        // 路缘网格数据
        private List<Vector3> curbLeftPositions = new List<Vector3>();
        private List<Vector3> curbLeftNormals = new List<Vector3>();
        private List<Vector2> curbLeftTextures = new List<Vector2>();
        private List<int> curbLeftIndices = new List<int>();

        private List<Vector3> curbRightPositions = new List<Vector3>();
        private List<Vector3> curbRightNormals = new List<Vector3>();
        private List<Vector2> curbRightTextures = new List<Vector2>();
        private List<int> curbRightIndices = new List<int>();

        // 马路牙子网格数据
        private List<Vector3> roadEdgeLeftPositions = new List<Vector3>();
        private List<Vector3> roadEdgeLeftNormals = new List<Vector3>();
        private List<Vector2> roadEdgeLeftTextures = new List<Vector2>();
        private List<int> roadEdgeLeftIndices = new List<int>();

        private List<Vector3> roadEdgeRightPositions = new List<Vector3>();
        private List<Vector3> roadEdgeRightNormals = new List<Vector3>();
        private List<Vector2> roadEdgeRightTextures = new List<Vector2>();
        private List<int> roadEdgeRightIndices = new List<int>();

        // 人行道网格数据
        private List<Vector3> sidewalkVertices = new List<Vector3>();
        private List<Vector3> sidewalkNormals = new List<Vector3>();
        private List<Vector2> sidewalkUvs = new List<Vector2>();
        private List<int> sidewalkTriangles = new List<int>();

        // 人行道外侧马路牙子网格数据
        private List<Vector3> roadEdgeOuterLeftPositions = new List<Vector3>();
        private List<Vector3> roadEdgeOuterLeftNormals = new List<Vector3>();
        private List<Vector2> roadEdgeOuterLeftTextures = new List<Vector2>();
        private List<int> roadEdgeOuterLeftIndices = new List<int>();

        private List<Vector3> roadEdgeOuterRightPositions = new List<Vector3>();
        private List<Vector3> roadEdgeOuterRightNormals = new List<Vector3>();
        private List<Vector2> roadEdgeOuterRightTextures = new List<Vector2>();
        private List<int> roadEdgeOuterRightIndices = new List<int>();
        
        public List<MeshVertex> MeshVertices => meshVertices;
        public List<int> MeshTriangles => meshTriangles;

        private void CacheRootRoadOutputComponents()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
        }

        private void DisableRootRoadMeshOutput(bool destroyRootMesh = true)
        {
            CacheRootRoadOutputComponents();

            Mesh rootMesh = mesh != null ? mesh : meshFilter != null ? meshFilter.sharedMesh : null;
            if (meshFilter != null)
            {
                meshFilter.sharedMesh = null;
            }

            if (destroyRootMesh && rootMesh != null && !AssetDatabase.Contains(rootMesh))
            {
                if (Application.isPlaying)
                {
                    Destroy(rootMesh);
                }
                else
                {
                    DestroyImmediate(rootMesh);
                }
            }

            mesh = null;

            if (meshRenderer != null)
            {
                meshRenderer.sharedMaterial = null;
                meshRenderer.enabled = false;
            }
        }
        
        private void OnEnable()
        {
            CacheRootRoadOutputComponents();
            
            // 初始化车道连接控制器
            InitializeLaneConnector();
            
            // 初始化所有列表
            roadVertices = new List<Vector3>();
            roadNormals = new List<Vector3>();
            roadUvs = new List<Vector2>();
            roadTriangles = new List<int>();

            curbLeftPositions = new List<Vector3>();
            curbLeftNormals = new List<Vector3>();
            curbLeftTextures = new List<Vector2>();
            curbLeftIndices = new List<int>();

            curbRightPositions = new List<Vector3>();
            curbRightNormals = new List<Vector3>();
            curbRightTextures = new List<Vector2>();
            curbRightIndices = new List<int>();

            roadEdgeLeftPositions = new List<Vector3>();
            roadEdgeLeftNormals = new List<Vector3>();
            roadEdgeLeftTextures = new List<Vector2>();
            roadEdgeLeftIndices = new List<int>();

            roadEdgeRightPositions = new List<Vector3>();
            roadEdgeRightNormals = new List<Vector3>();
            roadEdgeRightTextures = new List<Vector2>();
            roadEdgeRightIndices = new List<int>();

            sidewalkVertices = new List<Vector3>();
            sidewalkNormals = new List<Vector3>();
            sidewalkUvs = new List<Vector2>();
            sidewalkTriangles = new List<int>();

            roadEdgeOuterLeftPositions = new List<Vector3>();
            roadEdgeOuterLeftNormals = new List<Vector3>();
            roadEdgeOuterLeftTextures = new List<Vector2>();
            roadEdgeOuterLeftIndices = new List<int>();

            roadEdgeOuterRightPositions = new List<Vector3>();
            roadEdgeOuterRightNormals = new List<Vector3>();
            roadEdgeOuterRightTextures = new List<Vector2>();
            roadEdgeOuterRightIndices = new List<int>();

            // 获取人行道材质
            sidewalkMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/ArtResources/PcgAsset/Materials/PcgToolsMat/Road_Sidewalk01.mat");
            if (sidewalkMaterial == null)
            {
                Debug.LogError("无法加载人行道材质");
            }

            // 确保子对象存在
            EnsureChildObjects();
        }

        private void OnDisable()
        {
            ReleaseCunningRenderState();
            DisableRootRoadMeshOutput();

            // 清理所有列表
            roadVertices?.Clear();
            roadNormals?.Clear();
            roadUvs?.Clear();
            roadTriangles?.Clear();

            curbLeftPositions?.Clear();
            curbLeftNormals?.Clear();
            curbLeftTextures?.Clear();
            curbLeftIndices?.Clear();

            curbRightPositions?.Clear();
            curbRightNormals?.Clear();
            curbRightTextures?.Clear();
            curbRightIndices?.Clear();

            roadEdgeLeftPositions?.Clear();
            roadEdgeLeftNormals?.Clear();
            roadEdgeLeftTextures?.Clear();
            roadEdgeLeftIndices?.Clear();

            roadEdgeRightPositions?.Clear();
            roadEdgeRightNormals?.Clear();
            roadEdgeRightTextures?.Clear();
            roadEdgeRightIndices?.Clear();

            sidewalkVertices?.Clear();
            sidewalkNormals?.Clear();
            sidewalkUvs?.Clear();
            sidewalkTriangles?.Clear();

            roadEdgeOuterLeftPositions?.Clear();
            roadEdgeOuterLeftNormals?.Clear();
            roadEdgeOuterLeftTextures?.Clear();
            roadEdgeOuterLeftIndices?.Clear();

            roadEdgeOuterRightPositions?.Clear();
            roadEdgeOuterRightNormals?.Clear();
            roadEdgeOuterRightTextures?.Clear();
            roadEdgeOuterRightIndices?.Clear();
        }
        
        public Vector3 GetJunctionCenter()
        {
            if (IsImplicitAutoJunction)
            {
                return transform.position;
            }

            if(connectedRoads == null || connectedRoads.Count == 0) return transform.position;
            
            Vector3 center = Vector3.zero;
            int validRoadCount = 0;
            foreach(var road in connectedRoads)
            {
                if (road == null || !road.HasValidSplineReference())
                    continue;

                center += road.GetConnectionPoint();
                validRoadCount++;
            }
            return validRoadCount > 0 ? center / validRoadCount : transform.position;
        }

        private bool TryCollectValidConnectedRoadData(
            Vector3 center,
            out List<ConnectedRoad> validConnectedRoads,
            out List<EdgePoints> edgePoints,
            out List<float> meshOffsets)
        {
            validConnectedRoads = new List<ConnectedRoad>();
            edgePoints = new List<EdgePoints>();
            meshOffsets = new List<float>();

            if (connectedRoads == null)
                return false;

            foreach (var road in connectedRoads)
            {
                if (road == null || !road.HasValidSplineReference() || !road.TryGetMeshOffset(out float meshOffset))
                    continue;

                validConnectedRoads.Add(road);
                edgePoints.Add(road.GetEdgePoints(center));
                meshOffsets.Add(meshOffset);
            }

            return validConnectedRoads.Count > 0 &&
                validConnectedRoads.Count == edgePoints.Count &&
                edgePoints.Count == meshOffsets.Count;
        }

        private void DrawPointMarker(Vector3 position, float size, Color color, float duration)
        {
            // 画十字线标记点
            Vector3 up = Vector3.up * size;
            Vector3 right = Vector3.right * size;
            Vector3 forward = Vector3.forward * size;

            // 垂直线
            Debug.DrawLine(position - up, position + up, color, duration);
            // 水平线
            Debug.DrawLine(position - right, position + right, color, duration);
            // 前后线
            Debug.DrawLine(position - forward, position + forward, color, duration);
        }

        private void DrawCurvedLine(Vector3 start, Vector3 end, Vector3 startNormal, Vector3 endNormal, Color color)
        {
            int segments = 10;
            Vector3 lastPoint = start;
            
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                
                // 计算当前点的法线方向（插值两个法线）
                Vector3 currentNormal = Vector3.Lerp(startNormal, endNormal, t).normalized;
                
                // 计算控制点
                float curveStrength = curveParameter * Vector3.Distance(start, end);
                Vector3 startControl = start + startNormal * curveStrength;
                Vector3 endControl = end + endNormal * curveStrength;
                
                // 使用贝塞尔曲线计算当前点
                Vector3 currentPoint = BezierPoint(start, startControl, endControl, end, t);
                
                // 绘制线段
                Debug.DrawLine(lastPoint, currentPoint, color, 0f);
                lastPoint = currentPoint;
            }
        }

        private Vector3 BezierPoint(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1 - t;
            float tt = t * t;
            float uu = u * u;
            float uuu = uu * u;
            float ttt = tt * t;
            
            Vector3 p = uuu * p0;
            p += 3 * uu * t * p1;
            p += 3 * u * tt * p2;
            p += ttt * p3;
            
            return p;
        }
        
        private List<Vector3> GenerateCurvePoints(Vector3 start, Vector3 end, Vector3 startNormal, Vector3 endNormal)
        {
            List<Vector3> points = new List<Vector3>();
            
            for (int i = 0; i <= curveSegments; i++)
            {
                float t = i / (float)curveSegments;
                
                // 计算控制点
                float curveStrength = curveParameter * Vector3.Distance(start, end);
                Vector3 startControl = start + startNormal * curveStrength;
                Vector3 endControl = end + endNormal * curveStrength;
                
                // 计算贝塞尔曲线上的点
                Vector3 point = BezierPoint(start, startControl, endControl, end, t);
                points.Add(point - transform.position); // 转换到局部坐标
            }
            
            return points;
        }
        
        private void MergeSidewalkVertices(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs, List<Vector3> normals)
        {
            float mergeThreshold = 0.01f; // 合并阈值
            Dictionary<Vector3, int> uniqueVertices = new Dictionary<Vector3, int>();
            Dictionary<int, int> oldToNewIndexMap = new Dictionary<int, int>();
            List<Vector3> newVertices = new List<Vector3>();
            List<Vector2> newUVs = new List<Vector2>();
            List<Vector3> newNormals = new List<Vector3>();
            
            // 第一遍:找出所有唯一顶点并建立索引映射
            for(int i = 0; i < vertices.Count; i++)
            {
                Vector3 currentVertex = vertices[i];
                bool foundMatch = false;
                
                // 查找已存在的相近顶点
                foreach(var kvp in uniqueVertices)
                {
                    if(Vector3.Distance(currentVertex, kvp.Key) < mergeThreshold)
                    {
                        oldToNewIndexMap[i] = kvp.Value;
                        foundMatch = true;
                        break;
                    }
                }
                
                if(!foundMatch)
                {
                    // 添加新顶点
                    int newIndex = newVertices.Count;
                    newVertices.Add(currentVertex);
                    newUVs.Add(uvs[i]);
                    newNormals.Add(normals[i]);
                    oldToNewIndexMap[i] = newIndex;
                    uniqueVertices[currentVertex] = newIndex;
                }
            }
            
            // 第二遍:重新映射三角形索引,保持原有的三角形结构
            List<int> newTriangles = new List<int>();
            for(int i = 0; i < triangles.Count; i++)
            {
                int oldIndex = triangles[i];
                if(oldToNewIndexMap.TryGetValue(oldIndex, out int newIndex))
                {
                    newTriangles.Add(newIndex);
                }
            }
            
            // 更新数据
            vertices.Clear();
            vertices.AddRange(newVertices);
            uvs.Clear();
            uvs.AddRange(newUVs);
            normals.Clear();
            normals.AddRange(newNormals);
            triangles.Clear();
            triangles.AddRange(newTriangles);
            
            // 移除退化的三角形(三个顶点相同或共线的三角形)
            RemoveDegenerateTriangles(vertices, triangles);
        }

        private void RemoveDegenerateTriangles(List<Vector3> vertices, List<int> triangles)
        {
            for(int i = triangles.Count - 3; i >= 0; i -= 3)
            {
                Vector3 v1 = vertices[triangles[i]];
                Vector3 v2 = vertices[triangles[i + 1]];
                Vector3 v3 = vertices[triangles[i + 2]];
                
                // 检查三角形是否退化(三点共线或重合)
                Vector3 edge1 = v2 - v1;
                Vector3 edge2 = v3 - v1;
                float area = Vector3.Cross(edge1, edge2).magnitude * 0.5f;
                
                if(area < 0.0001f) // 面积接近于0
                {
                    triangles.RemoveRange(i, 3);
                }
            }
        }

        private void EnsureChildObjects()
        {
            CacheRootRoadOutputComponents();

            if (meshFilter != null)
            {
                meshFilter.sharedMesh = null;
            }

            if (meshRenderer != null)
            {
                meshRenderer.enabled = true;
            }

            m_CunningRoadMesh = GetComponent<CunningMesh>();
            if (m_CunningRoadMesh == null)
            {
                m_CunningRoadMesh = gameObject.AddComponent<CunningMesh>();
            }

            m_CunningRoadMesh.ownsHandle = false;
            m_CunningRoadMesh.displayMode = CunningMesh.DisplayMode.Solid;
            m_CunningRoadMesh.preserveExistingSolidMaterial = true;

            if (sidewalkObject == null)
            {
                sidewalkObject = transform.Find("Sidewalk")?.gameObject;
                if (sidewalkObject == null)
                {
                    sidewalkObject = new GameObject("Sidewalk");
                    sidewalkObject.transform.SetParent(transform);
                }
            }

            ResetDomainTransform(sidewalkObject.transform);

            if (sidewalkObject.GetComponent<MeshFilter>() == null)
            {
                sidewalkObject.AddComponent<MeshFilter>();
            }

            MeshRenderer sidewalkRenderer = sidewalkObject.GetComponent<MeshRenderer>();
            if (sidewalkRenderer == null)
            {
                sidewalkRenderer = sidewalkObject.AddComponent<MeshRenderer>();
            }

            sidewalkRenderer.enabled = true;

            m_SidewalkCunningMesh = sidewalkObject.GetComponent<CunningMesh>();
            if (m_SidewalkCunningMesh == null)
            {
                m_SidewalkCunningMesh = sidewalkObject.AddComponent<CunningMesh>();
            }

            m_SidewalkCunningMesh.ownsHandle = false;
            m_SidewalkCunningMesh.displayMode = CunningMesh.DisplayMode.Solid;
            m_SidewalkCunningMesh.preserveExistingSolidMaterial = true;

            CleanupLegacyJunctionChild("Curb", ref curbObject);
            CleanupLegacyJunctionChild("RoadEdge", ref roadEdgeObject);
            CleanupLegacyJunctionChild("RoadEdgeOuter", ref roadEdgeOuterObject);

            if (curbMaterial == null)
            {
                string curbPath = "Assets/ArtResources/PcgAsset/Models/GtaSample/hw1_rd_04_21_tex/mat_2.mat";
                curbMaterial = AssetDatabase.LoadAssetAtPath<Material>(curbPath);
            }

            if (roadEdgeMaterial == null)
            {
                string roadEdgePath = "Assets/ArtResources/PcgAsset/Models/GtaSample/hw1_rd_04_21_tex/mat_1.mat";
                roadEdgeMaterial = AssetDatabase.LoadAssetAtPath<Material>(roadEdgePath);
                if (roadEdgeMaterial == null && curbMaterial != null)
                {
                    roadEdgeMaterial = curbMaterial;
                }
            }

            if (sidewalkMaterial == null)
            {
                string sidewalkPath = "Assets/ArtResources/PcgAsset/Materials/PcgToolsMat/Road_Sidewalk01.mat";
                sidewalkMaterial = AssetDatabase.LoadAssetAtPath<Material>(sidewalkPath);
            }
        }

        private static void ResetDomainTransform(Transform domainTransform)
        {
            domainTransform.localPosition = Vector3.zero;
            domainTransform.localRotation = Quaternion.identity;
            domainTransform.localScale = Vector3.one;
        }

        private void CleanupLegacyJunctionChild(string childName, ref GameObject childObject)
        {
            childObject = childObject != null ? childObject : transform.Find(childName)?.gameObject;
            if (childObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(childObject);
            }
            else
            {
                DestroyImmediate(childObject);
            }

            childObject = null;
        }

        private void ReleaseCunningRenderState()
        {
            if (m_CunningRoadMesh != null)
            {
                m_CunningRoadMesh.LoadFromHandle(0);
            }

            if (m_SidewalkCunningMesh != null)
            {
                m_SidewalkCunningMesh.LoadFromHandle(0);
            }

            ReleaseGeometryHandle(ref m_CunningRoadHandle);
            ReleaseGeometryHandle(ref m_CunningSidewalkHandle);
        }

        private static void ReleaseGeometryHandle(ref ulong handle)
        {
            if (handle == 0)
            {
                return;
            }

            NativeMethods.cunning_release_handle(handle);
            handle = 0;
        }

        private static bool EnsureGeometryHandle(ref ulong handle)
        {
            if (handle != 0)
            {
                return true;
            }

            handle = NativeMethods.cunning_geo_create();
            return handle != 0;
        }

        private static void EnsureBufferCapacity<T>(ref T[] buffer, int requiredLength)
        {
            if (buffer == null || buffer.Length < requiredLength)
            {
                buffer = new T[requiredLength];
            }
        }

        private float[] CopyVector3ListToBuffer(List<Vector3> values, ref float[] buffer)
        {
            int requiredLength = values.Count * 3;
            EnsureBufferCapacity(ref buffer, requiredLength);
            for (int index = 0; index < values.Count; index++)
            {
                int bufferIndex = index * 3;
                Vector3 value = values[index];
                buffer[bufferIndex] = value.x;
                buffer[bufferIndex + 1] = value.y;
                buffer[bufferIndex + 2] = value.z;
            }

            return buffer;
        }

        private float[] CopyVector2ListToBuffer(List<Vector2> values, ref float[] buffer)
        {
            int requiredLength = values.Count * 2;
            EnsureBufferCapacity(ref buffer, requiredLength);
            for (int index = 0; index < values.Count; index++)
            {
                int bufferIndex = index * 2;
                Vector2 value = values[index];
                buffer[bufferIndex] = value.x;
                buffer[bufferIndex + 1] = value.y;
            }

            return buffer;
        }

        private void AppendTriangleSection(
            List<Vector3> positions,
            List<int> primitivePointIndices,
            List<Vector3> vertexNormals,
            List<Vector2> vertexUvs,
            List<int> primitiveMaterialIndices,
            List<int> primitiveAreaIndices,
            List<uint> primitiveOffsets,
            List<Vector3> sourcePositions,
            List<Vector3> sourceNormals,
            List<Vector2> sourceUvs,
            List<int> sourceTriangles,
            int materialIndex,
            int areaIndex)
        {
            if (sourcePositions == null || sourceTriangles == null || sourceTriangles.Count < 3)
            {
                return;
            }

            int pointBase = positions.Count;
            positions.AddRange(sourcePositions);

            int triangleCount = sourceTriangles.Count / 3;
            for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
            {
                int triStart = triangleIndex * 3;
                for (int corner = 0; corner < 3; corner++)
                {
                    int sourceVertexIndex = sourceTriangles[triStart + corner];
                    primitivePointIndices.Add(pointBase + sourceVertexIndex);
                    vertexNormals.Add(sourceVertexIndex >= 0 && sourceVertexIndex < sourceNormals.Count ? sourceNormals[sourceVertexIndex] : Vector3.up);
                    vertexUvs.Add(sourceVertexIndex >= 0 && sourceVertexIndex < sourceUvs.Count ? sourceUvs[sourceVertexIndex] : Vector2.zero);
                }

                primitiveMaterialIndices.Add(materialIndex);
                primitiveAreaIndices.Add(areaIndex);
                primitiveOffsets.Add((uint)primitivePointIndices.Count);
            }
        }

        private bool AppendBlockPolygons(
            List<Vector3> positions,
            List<int> primitivePointIndices,
            List<Vector3> vertexNormals,
            List<Vector2> vertexUvs,
            List<int> primitiveMaterialIndices,
            List<int> primitiveAreaIndices,
            List<uint> primitiveOffsets,
            List<Vector3> sourcePositions,
            List<Vector3> sourceNormals,
            List<Vector2> sourceUvs,
            int verticesPerBlock,
            int[][] polygons,
            int materialIndex,
            int areaIndex)
        {
            if (sourcePositions == null || sourcePositions.Count == 0)
            {
                return true;
            }

            if (verticesPerBlock <= 0
                || polygons == null
                || polygons.Length == 0
                || sourcePositions.Count % verticesPerBlock != 0
                || sourceNormals == null
                || sourceNormals.Count < sourcePositions.Count
                || sourceUvs == null
                || sourceUvs.Count < sourcePositions.Count)
            {
                return false;
            }

            int pointBase = positions.Count;
            positions.AddRange(sourcePositions);
            int blockCount = sourcePositions.Count / verticesPerBlock;

            for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
            {
                int localBlockStart = blockIndex * verticesPerBlock;
                foreach (int[] polygon in polygons)
                {
                    if (polygon == null || polygon.Length < 3)
                    {
                        continue;
                    }

                    for (int polygonCornerIndex = 0; polygonCornerIndex < polygon.Length; polygonCornerIndex++)
                    {
                        int localVertexOffset = polygon[polygonCornerIndex];
                        if (localVertexOffset < 0 || localVertexOffset >= verticesPerBlock)
                        {
                            return false;
                        }

                        int sourceVertexIndex = localBlockStart + localVertexOffset;
                        primitivePointIndices.Add(pointBase + sourceVertexIndex);
                        vertexNormals.Add(sourceNormals[sourceVertexIndex]);
                        vertexUvs.Add(sourceUvs[sourceVertexIndex]);
                    }

                    primitiveMaterialIndices.Add(materialIndex);
                    primitiveAreaIndices.Add(areaIndex);
                    primitiveOffsets.Add((uint)primitivePointIndices.Count);
                }
            }

            return true;
        }

        private bool UploadTriangleDomainToHandle(
            ref ulong handle,
            List<Vector3> positions,
            List<int> primitivePointIndices,
            List<Vector3> vertexNormals,
            List<Vector2> vertexUvs,
            List<int> primitiveMaterialIndices,
            List<int> primitiveAreaIndices,
            List<uint> primitiveOffsets)
        {
            if (positions == null || positions.Count == 0 || primitivePointIndices == null || primitivePointIndices.Count == 0 || primitiveMaterialIndices == null || primitiveMaterialIndices.Count == 0)
            {
                if (handle != 0)
                {
                    NativeMethods.cunning_geo_clear(handle);
                    NativeMethods.cunning_geo_sync_cache(handle);
                }

                return false;
            }

            if (!EnsureGeometryHandle(ref handle))
            {
                return false;
            }

            if (NativeMethods.cunning_geo_clear(handle) == 0)
            {
                return false;
            }

            EnsureBufferCapacity(ref m_CunningPointIdBuffer, positions.Count);
            uint addedPoints = NativeMethods.cunning_geo_add_points_bulk_nosync(
                handle,
                CopyVector3ListToBuffer(positions, ref m_CunningFlatPositionsBuffer),
                (uint)positions.Count,
                m_CunningPointIdBuffer);
            if (addedPoints != positions.Count)
            {
                return false;
            }

            EnsureBufferCapacity(ref m_CunningPolyPointIdBuffer, primitivePointIndices.Count);
            for (int pointIndex = 0; pointIndex < primitivePointIndices.Count; pointIndex++)
            {
                int sourcePointIndex = primitivePointIndices[pointIndex];
                if (sourcePointIndex < 0 || sourcePointIndex >= positions.Count)
                {
                    return false;
                }

                m_CunningPolyPointIdBuffer[pointIndex] = m_CunningPointIdBuffer[sourcePointIndex];
            }

            EnsureBufferCapacity(ref m_CunningPolyOffsetBuffer, primitiveOffsets.Count);
            for (int offsetIndex = 0; offsetIndex < primitiveOffsets.Count; offsetIndex++)
            {
                m_CunningPolyOffsetBuffer[offsetIndex] = primitiveOffsets[offsetIndex];
            }

            uint addedPrimitives = NativeMethods.cunning_geo_add_polys_bulk_nosync(
                handle,
                m_CunningPolyPointIdBuffer,
                m_CunningPolyOffsetBuffer,
                (uint)primitiveMaterialIndices.Count);
            if (addedPrimitives != primitiveMaterialIndices.Count)
            {
                return false;
            }

            if (vertexNormals.Count > 0)
            {
                uint setNormals = NativeMethods.cunning_geo_set_vertex_attr_vec3_nosync(
                    handle,
                    "@N",
                    CopyVector3ListToBuffer(vertexNormals, ref m_CunningFlatNormalsBuffer),
                    (uint)vertexNormals.Count);
                if (setNormals == 0)
                {
                    return false;
                }
            }

            if (vertexUvs.Count > 0)
            {
                uint setUvs = NativeMethods.cunning_geo_set_vertex_attr_vec2_nosync(
                    handle,
                    "@uv",
                    CopyVector2ListToBuffer(vertexUvs, ref m_CunningFlatUvBuffer),
                    (uint)vertexUvs.Count);
                if (setUvs == 0)
                {
                    return false;
                }
            }

            EnsureBufferCapacity(ref m_CunningPrimitiveMaterialIndexBuffer, primitiveMaterialIndices.Count);
            for (int primitiveIndex = 0; primitiveIndex < primitiveMaterialIndices.Count; primitiveIndex++)
            {
                m_CunningPrimitiveMaterialIndexBuffer[primitiveIndex] = primitiveMaterialIndices[primitiveIndex];
            }

            uint setMaterialIndices = NativeMethods.cunning_geo_set_primitive_attr_i32_nosync(
                handle,
                "@unity_material_index",
                m_CunningPrimitiveMaterialIndexBuffer,
                (uint)primitiveMaterialIndices.Count);
            if (setMaterialIndices == 0)
            {
                return false;
            }

            EnsureBufferCapacity(ref m_CunningPrimitiveAreaIndexBuffer, primitiveAreaIndices.Count);
            for (int primitiveIndex = 0; primitiveIndex < primitiveAreaIndices.Count; primitiveIndex++)
            {
                m_CunningPrimitiveAreaIndexBuffer[primitiveIndex] = primitiveAreaIndices[primitiveIndex];
            }

            uint setAreaIndices = NativeMethods.cunning_geo_set_primitive_attr_i32_nosync(
                handle,
                "@area_type_index",
                m_CunningPrimitiveAreaIndexBuffer,
                (uint)primitiveAreaIndices.Count);
            if (setAreaIndices == 0)
            {
                return false;
            }

            return NativeMethods.cunning_geo_sync_cache(handle) != 0;
        }

        private bool BuildAndUploadRoadDomain()
        {
            if (roadVertices == null || roadVertices.Count == 0 || roadTriangles == null || roadTriangles.Count == 0)
            {
                if (m_CunningRoadHandle != 0)
                {
                    NativeMethods.cunning_geo_clear(m_CunningRoadHandle);
                    NativeMethods.cunning_geo_sync_cache(m_CunningRoadHandle);
                }

                return false;
            }

            List<Vector3> positions = new List<Vector3>(roadVertices.Count);
            List<int> primitivePointIndices = new List<int>(roadTriangles.Count);
            List<Vector3> vertexDomainNormals = new List<Vector3>(roadTriangles.Count);
            List<Vector2> vertexDomainUvs = new List<Vector2>(roadTriangles.Count);
            List<int> primitiveMaterialIndices = new List<int>(roadTriangles.Count / 3);
            List<int> primitiveAreaIndices = new List<int>(roadTriangles.Count / 3);
            List<uint> primitiveOffsets = new List<uint>(roadTriangles.Count / 3 + 1) { 0u };

            AppendTriangleSection(
                positions,
                primitivePointIndices,
                vertexDomainNormals,
                vertexDomainUvs,
                primitiveMaterialIndices,
                primitiveAreaIndices,
                primitiveOffsets,
                roadVertices,
                roadNormals,
                roadUvs,
                roadTriangles,
                0,
                (int)JunctionCunningAreaType.RoadSurface);

            return UploadTriangleDomainToHandle(
                ref m_CunningRoadHandle,
                positions,
                primitivePointIndices,
                vertexDomainNormals,
                vertexDomainUvs,
                primitiveMaterialIndices,
                primitiveAreaIndices,
                primitiveOffsets);
        }

        private bool BuildAndUploadSidewalkBundleDomain()
        {
            List<Vector3> positions = new List<Vector3>(
                sidewalkVertices.Count
                + curbLeftPositions.Count + curbRightPositions.Count
                + roadEdgeLeftPositions.Count + roadEdgeRightPositions.Count
                + roadEdgeOuterLeftPositions.Count + roadEdgeOuterRightPositions.Count);
            List<int> primitivePointIndices = new List<int>(
                sidewalkTriangles.Count
                + curbLeftIndices.Count + curbRightIndices.Count
                + roadEdgeLeftIndices.Count + roadEdgeRightIndices.Count
                + roadEdgeOuterLeftIndices.Count + roadEdgeOuterRightIndices.Count);
            List<Vector3> vertexDomainNormals = new List<Vector3>(primitivePointIndices.Capacity);
            List<Vector2> vertexDomainUvs = new List<Vector2>(primitivePointIndices.Capacity);
            List<int> primitiveMaterialIndices = new List<int>();
            List<int> primitiveAreaIndices = new List<int>();
            List<uint> primitiveOffsets = new List<uint> { 0u };

            if (!AppendBlockPolygons(
                positions,
                primitivePointIndices,
                vertexDomainNormals,
                vertexDomainUvs,
                primitiveMaterialIndices,
                primitiveAreaIndices,
                primitiveOffsets,
                sidewalkVertices,
                sidewalkNormals,
                sidewalkUvs,
                4,
                s_SidewalkQuadPolygons,
                (int)JunctionSidewalkMaterialSlot.Sidewalk,
                (int)JunctionCunningAreaType.Sidewalk))
            {
                return false;
            }

            if (!AppendBlockPolygons(
                positions,
                primitivePointIndices,
                vertexDomainNormals,
                vertexDomainUvs,
                primitiveMaterialIndices,
                primitiveAreaIndices,
                primitiveOffsets,
                curbLeftPositions,
                curbLeftNormals,
                curbLeftTextures,
                10,
                s_ChamferStripPolygons,
                (int)JunctionSidewalkMaterialSlot.Curb,
                (int)JunctionCunningAreaType.Curb))
            {
                return false;
            }

            if (!AppendBlockPolygons(
                positions,
                primitivePointIndices,
                vertexDomainNormals,
                vertexDomainUvs,
                primitiveMaterialIndices,
                primitiveAreaIndices,
                primitiveOffsets,
                curbRightPositions,
                curbRightNormals,
                curbRightTextures,
                10,
                s_ChamferStripPolygons,
                (int)JunctionSidewalkMaterialSlot.Curb,
                (int)JunctionCunningAreaType.Curb))
            {
                return false;
            }

            if (!AppendBlockPolygons(
                positions,
                primitivePointIndices,
                vertexDomainNormals,
                vertexDomainUvs,
                primitiveMaterialIndices,
                primitiveAreaIndices,
                primitiveOffsets,
                roadEdgeLeftPositions,
                roadEdgeLeftNormals,
                roadEdgeLeftTextures,
                10,
                s_ChamferStripPolygons,
                (int)JunctionSidewalkMaterialSlot.RoadEdge,
                (int)JunctionCunningAreaType.RoadEdge))
            {
                return false;
            }

            if (!AppendBlockPolygons(
                positions,
                primitivePointIndices,
                vertexDomainNormals,
                vertexDomainUvs,
                primitiveMaterialIndices,
                primitiveAreaIndices,
                primitiveOffsets,
                roadEdgeRightPositions,
                roadEdgeRightNormals,
                roadEdgeRightTextures,
                10,
                s_ChamferStripPolygons,
                (int)JunctionSidewalkMaterialSlot.RoadEdge,
                (int)JunctionCunningAreaType.RoadEdge))
            {
                return false;
            }

            if (!AppendBlockPolygons(
                positions,
                primitivePointIndices,
                vertexDomainNormals,
                vertexDomainUvs,
                primitiveMaterialIndices,
                primitiveAreaIndices,
                primitiveOffsets,
                roadEdgeOuterLeftPositions,
                roadEdgeOuterLeftNormals,
                roadEdgeOuterLeftTextures,
                10,
                s_ChamferStripPolygons,
                (int)JunctionSidewalkMaterialSlot.RoadEdgeOuter,
                (int)JunctionCunningAreaType.RoadEdgeOuter))
            {
                return false;
            }

            if (!AppendBlockPolygons(
                positions,
                primitivePointIndices,
                vertexDomainNormals,
                vertexDomainUvs,
                primitiveMaterialIndices,
                primitiveAreaIndices,
                primitiveOffsets,
                roadEdgeOuterRightPositions,
                roadEdgeOuterRightNormals,
                roadEdgeOuterRightTextures,
                10,
                s_ChamferStripPolygons,
                (int)JunctionSidewalkMaterialSlot.RoadEdgeOuter,
                (int)JunctionCunningAreaType.RoadEdgeOuter))
            {
                return false;
            }

            return UploadTriangleDomainToHandle(
                ref m_CunningSidewalkHandle,
                positions,
                primitivePointIndices,
                vertexDomainNormals,
                vertexDomainUvs,
                primitiveMaterialIndices,
                primitiveAreaIndices,
                primitiveOffsets);
        }

        private void RefreshRoadCunningMesh(bool hasGeometry)
        {
            if (m_CunningRoadMesh == null)
            {
                return;
            }

            if (meshRenderer != null)
            {
                meshRenderer.enabled = hasGeometry;
            }

            if (!hasGeometry)
            {
                m_CunningRoadMesh.LoadFromHandle(0);
                return;
            }

            Material roadMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/ArtResources/PcgAsset/Materials/PcgToolsMat/im_road_002.mat");
            m_CunningRoadMesh.solidMaterialOverride = roadMaterial;
            m_CunningRoadMesh.solidMaterialOverrides = roadMaterial != null ? new[] { roadMaterial } : Array.Empty<Material>();
            m_CunningRoadMesh.LoadFromHandle(m_CunningRoadHandle);
        }

        private void RefreshSidewalkCunningMesh(bool hasGeometry)
        {
            if (sidewalkObject == null || m_SidewalkCunningMesh == null)
            {
                return;
            }

            if (sidewalkObject.activeSelf != hasGeometry)
            {
                sidewalkObject.SetActive(hasGeometry);
            }

            if (!hasGeometry)
            {
                m_SidewalkCunningMesh.LoadFromHandle(0);
                return;
            }

            m_SidewalkCunningMesh.solidMaterialOverrides = new[]
            {
                sidewalkMaterial,
                curbMaterial,
                roadEdgeMaterial,
                roadEdgeMaterial,
            };
            m_SidewalkCunningMesh.LoadFromHandle(m_CunningSidewalkHandle);
        }

        public void UpdateJunctionMesh()
        {
            // 如果在编辑模式下且设置为忽略更新，则不执行更新
            if (editMode && ignoreRoadUpdates)
            {
                return;
            }

            if (connectedRoads == null || connectedRoads.Count < 2)
            {
                return;
            }

            SortConnectedRoads();

            // 设置位置到路口中心
            Vector3 center = GetJunctionCenter();
            transform.position = center;

            if (!TryCollectValidConnectedRoadData(center, out var validConnectedRoads, out var edgePoints, out var meshOffsets) ||
                validConnectedRoads.Count < 2)
            {
                return;
            }

            // 只在第一次更新时记录连接点数量
            if (!hasLoggedConnectionCount)
            {
                Debug.Log($"路口连接点数量: {validConnectedRoads.Count}");
                hasLoggedConnectionCount = true;
            }
            
            // 计算最大宽度和平均meshOffset
            maxWidth = 0f;
            float avgMeshOffset = 0f;
            foreach(var road in validConnectedRoads)
            {
                maxWidth = Mathf.Max(maxWidth, road.width);
            }

            foreach (var meshOffset in meshOffsets)
            {
                avgMeshOffset += meshOffset;
            }
            maxWidth = Mathf.Max(maxWidth, 10f);
            avgMeshOffset /= validConnectedRoads.Count;
            
            // 收集所有顶点 - 分成道路和人行道两部分
            roadVertices.Clear();
            roadNormals.Clear();
            roadUvs.Clear();
            roadTriangles.Clear();
            
            curbLeftPositions.Clear();
            curbLeftNormals.Clear();
            curbLeftTextures.Clear();
            curbLeftIndices.Clear();
            
            curbRightPositions.Clear();
            curbRightNormals.Clear();
            curbRightTextures.Clear();
            curbRightIndices.Clear();
            
            roadEdgeLeftPositions.Clear();
            roadEdgeLeftNormals.Clear();
            roadEdgeLeftTextures.Clear();
            roadEdgeLeftIndices.Clear();
            
            roadEdgeRightPositions.Clear();
            roadEdgeRightNormals.Clear();
            roadEdgeRightTextures.Clear();
            roadEdgeRightIndices.Clear();
            
            sidewalkVertices.Clear();
            sidewalkNormals.Clear();
            sidewalkUvs.Clear();
            sidewalkTriangles.Clear();
            
            roadEdgeOuterLeftPositions.Clear();
            roadEdgeOuterLeftNormals.Clear();
            roadEdgeOuterLeftTextures.Clear();
            roadEdgeOuterLeftIndices.Clear();
            
            roadEdgeOuterRightPositions.Clear();
            roadEdgeOuterRightNormals.Clear();
            roadEdgeOuterRightTextures.Clear();
            roadEdgeOuterRightIndices.Clear();
            
            // 生成边缘顶点
            for (int i = 0; i < edgePoints.Count; i++)
            {
                int nextIndex = (i + 1) % edgePoints.Count;
                var currentRoad = validConnectedRoads[i];
                var currentEdge = edgePoints[i];
                var nextEdge = edgePoints[nextIndex];
                currentRoad.TryGetRoadData(out var currentRoadData);
                
                float currentMeshOffset = meshOffsets[i];
                float nextMeshOffset = meshOffsets[nextIndex];
                
                // 首先生成所有需要的曲线点
                List<Vector3> curbInnerCurvePoints = GenerateCurvePoints(
                    currentEdge.point1,
                    nextEdge.point0,
                    currentEdge.normal1,
                    nextEdge.normal0
                );
                
                List<Vector3> curbOuterCurvePoints = GenerateCurvePoints(
                    currentEdge.point1CurbExtended,
                    nextEdge.point0CurbExtended,
                    currentEdge.normal1,
                    nextEdge.normal0
                );
                
                List<Vector3> roadEdgeInnerCurvePoints = GenerateCurvePoints(
                    currentEdge.point1CurbExtended,
                    nextEdge.point0CurbExtended,
                    currentEdge.normal1,
                    nextEdge.normal0
                );
                
                List<Vector3> roadEdgeOuterCurvePoints = GenerateCurvePoints(
                    currentEdge.point1RoadEdgeExtended,
                    nextEdge.point0RoadEdgeExtended,
                    currentEdge.normal1,
                    nextEdge.normal0
                );

                List<Vector3> sidewalkInnerCurvePoints = GenerateCurvePoints(
                    currentEdge.point1RoadEdgeExtended,
                    nextEdge.point0RoadEdgeExtended,
                    currentEdge.normal1,
                    nextEdge.normal0
                );

                List<Vector3> sidewalkOuterCurvePoints = GenerateCurvePoints(
                    currentEdge.point1SidewalkExtended,
                    nextEdge.point0SidewalkExtended,
                    currentEdge.normal1,
                    nextEdge.normal0
                );
                
                // 生成路缘网格
                // 1. 添加直线段顶点
                Vector3 curbPoint0Inner = currentEdge.point0 - transform.position;
                Vector3 curbPoint1Inner = currentEdge.point1 - transform.position;
                Vector3 curbPoint0Outer = currentEdge.point0CurbExtended - transform.position;
                Vector3 curbPoint1Outer = currentEdge.point1CurbExtended - transform.position;
                
                float curbWidth = Vector3.Distance(curbPoint0Inner, curbPoint0Outer);
                float curbHeight = 0.15f; // 默认路缘高度
                
                // 获取路缘参数
                if (currentRoadData != null)
                {
                    // 根据是否使用自定义参数来决定使用哪组参数
                    if (!useCustomParameters)
                    {
                        if (currentRoadData.enableCurb)
                        {
                            curbHeight = currentRoadData.curbHeight;
                            curbWidth = currentRoadData.curbWidth;
                        }
                    }
                    // 使用自定义参数时不从道路读取参数，使用JunctionData自身的参数
                }
                
                float curbSegmentLength = Vector3.Distance(curbPoint0Inner, curbPoint1Inner);
                
                // 获取路缘UV缩放和偏移参数
                float localCurbUVScaleU = curbUVScaleU;  // 优先使用Junction自己的参数
                float localCurbUVScaleV = curbUVScaleV;
                float localCurbUVOffset = curbUVOffset;  // 使用Junction自己的UV偏移参数
                
                // 路缘倒角参数
                float localCurbChamferWidth = curbChamferWidth;
                float localCurbChamferHeight = curbChamferHeight;
                
                if (!useCustomParameters && currentRoadData != null)
                {
                    if (currentRoadData.enableCurb)
                    {
                        // 获取倒角参数
                        localCurbChamferWidth = currentRoadData.curbChamferWidth;
                        localCurbChamferHeight = currentRoadData.curbChamferHeight;
                        
                        // 获取UV缩放
                        localCurbUVScaleU = currentRoadData.curbUVScaleU;
                        localCurbUVScaleV = currentRoadData.curbUVScaleV;
                        
                        // 获取UV偏移
                        localCurbUVOffset = currentRoadData.uvHorizontalOffset;
                    }
                }
                
                // 生成路缘网格 - 只保留曲线段部分
                // 添加曲线段顶点
                for(int j = 0; j < curbInnerCurvePoints.Count - 1; j++)
                {
                    float t = j / (float)(curbInnerCurvePoints.Count - 1);
                    float interpolatedMeshOffset = Mathf.Lerp(currentMeshOffset, nextMeshOffset, t);
                    
                    int curbBaseIndex = curbLeftPositions.Count;
                    
                    Vector3 innerPoint1 = curbInnerCurvePoints[j];
                    Vector3 innerPoint2 = curbInnerCurvePoints[j + 1];
                    Vector3 outerPoint1 = curbOuterCurvePoints[j];
                    Vector3 outerPoint2 = curbOuterCurvePoints[j + 1];
                    
                    // 计算方向向量，用于创建倒角
                    Vector3 curveRight1 = (outerPoint1 - innerPoint1).normalized;
                    Vector3 curveRight2 = (outerPoint2 - innerPoint2).normalized;
                    Vector3 chamferOffset1 = curveRight1 * localCurbChamferWidth;
                    Vector3 chamferOffset2 = curveRight2 * localCurbChamferWidth;
                    
                    // 添加顶面顶点（包括倒角）
                    curbLeftPositions.Add(innerPoint1 + Vector3.up * (interpolatedMeshOffset + curbHeight - localCurbChamferHeight)); // 内侧边缘点（高度减去倒角高度）
                    curbLeftPositions.Add(innerPoint1 + chamferOffset1 + Vector3.up * (interpolatedMeshOffset + curbHeight)); // 倒角点
                    curbLeftPositions.Add(outerPoint1 + Vector3.up * (interpolatedMeshOffset + curbHeight)); // 外侧边缘点
                    curbLeftPositions.Add(innerPoint2 + Vector3.up * (interpolatedMeshOffset + curbHeight - localCurbChamferHeight)); // 内侧边缘点（下一段）
                    curbLeftPositions.Add(innerPoint2 + chamferOffset2 + Vector3.up * (interpolatedMeshOffset + curbHeight)); // 下一段倒角点
                    curbLeftPositions.Add(outerPoint2 + Vector3.up * (interpolatedMeshOffset + curbHeight)); // 外侧边缘点（下一段）
                    
                    // 添加底面顶点
                    curbLeftPositions.Add(innerPoint1 + Vector3.up * interpolatedMeshOffset);
                    curbLeftPositions.Add(outerPoint1 + Vector3.up * interpolatedMeshOffset);
                    curbLeftPositions.Add(innerPoint2 + Vector3.up * interpolatedMeshOffset);
                    curbLeftPositions.Add(outerPoint2 + Vector3.up * interpolatedMeshOffset);
                    
                    // 添加法线
                    // 顶面法线 - 根据倒角调整
                    curbLeftNormals.Add(Vector3.up);
                    
                    // 计算倒角处的法线 - 45度角
                    float angleRad = Mathf.PI / 4; // 45度的弧度值
                    Vector3 chamferNormal1 = new Vector3(-curveRight1.x * Mathf.Sin(angleRad), Mathf.Cos(angleRad), -curveRight1.z * Mathf.Sin(angleRad)).normalized;
                    Vector3 chamferNormal2 = new Vector3(-curveRight2.x * Mathf.Sin(angleRad), Mathf.Cos(angleRad), -curveRight2.z * Mathf.Sin(angleRad)).normalized;
                    
                    curbLeftNormals.Add(chamferNormal1);
                    curbLeftNormals.Add(Vector3.up);
                    curbLeftNormals.Add(Vector3.up);
                    curbLeftNormals.Add(chamferNormal2);
                    curbLeftNormals.Add(Vector3.up);
                    
                    // 底面法线
                    for(int k = 0; k < 4; k++)
                    {
                        curbLeftNormals.Add(Vector3.down);
                    }
                    
                    float curveCurbSegmentLength = Vector3.Distance(innerPoint1, innerPoint2);
                    float uvStartV = j * curveCurbSegmentLength / 10.0f;
                    float uvEndV = (j + 1) * curveCurbSegmentLength / 10.0f;
                    
                    // 应用UV缩放和水平偏移 - 顶面UV
                    curbLeftTextures.Add(new Vector2(0 * localCurbUVScaleU + localCurbUVOffset, uvStartV * localCurbUVScaleV));
                    curbLeftTextures.Add(new Vector2(0.2f * localCurbUVScaleU + localCurbUVOffset, uvStartV * localCurbUVScaleV));
                    curbLeftTextures.Add(new Vector2(1 * localCurbUVScaleU + localCurbUVOffset, uvStartV * localCurbUVScaleV));
                    curbLeftTextures.Add(new Vector2(0 * localCurbUVScaleU + localCurbUVOffset, uvEndV * localCurbUVScaleV));
                    curbLeftTextures.Add(new Vector2(0.2f * localCurbUVScaleU + localCurbUVOffset, uvEndV * localCurbUVScaleV));
                    curbLeftTextures.Add(new Vector2(1 * localCurbUVScaleU + localCurbUVOffset, uvEndV * localCurbUVScaleV));
                    
                    // 底面UV
                    curbLeftTextures.Add(new Vector2(0 * localCurbUVScaleU + localCurbUVOffset, uvStartV * localCurbUVScaleV));
                    curbLeftTextures.Add(new Vector2(1 * localCurbUVScaleU + localCurbUVOffset, uvStartV * localCurbUVScaleV));
                    curbLeftTextures.Add(new Vector2(0 * localCurbUVScaleU + localCurbUVOffset, uvEndV * localCurbUVScaleV));
                    curbLeftTextures.Add(new Vector2(1 * localCurbUVScaleU + localCurbUVOffset, uvEndV * localCurbUVScaleV));
                    
                    // 添加三角形索引 - 顶面三角形（包括倒角）
                    curbLeftIndices.Add(curbBaseIndex);
                    curbLeftIndices.Add(curbBaseIndex + 1);
                    curbLeftIndices.Add(curbBaseIndex + 3);
                    curbLeftIndices.Add(curbBaseIndex + 3);
                    curbLeftIndices.Add(curbBaseIndex + 1);
                    curbLeftIndices.Add(curbBaseIndex + 4);
                    
                    curbLeftIndices.Add(curbBaseIndex + 1);
                    curbLeftIndices.Add(curbBaseIndex + 2);
                    curbLeftIndices.Add(curbBaseIndex + 4);
                    curbLeftIndices.Add(curbBaseIndex + 4);
                    curbLeftIndices.Add(curbBaseIndex + 2);
                    curbLeftIndices.Add(curbBaseIndex + 5);
                    
                    // 底面三角形
                    curbLeftIndices.Add(curbBaseIndex + 6);
                    curbLeftIndices.Add(curbBaseIndex + 8);
                    curbLeftIndices.Add(curbBaseIndex + 7);
                    curbLeftIndices.Add(curbBaseIndex + 7);
                    curbLeftIndices.Add(curbBaseIndex + 8);
                    curbLeftIndices.Add(curbBaseIndex + 9);
                    
                    // 内侧面三角形
                    curbLeftIndices.Add(curbBaseIndex);
                    curbLeftIndices.Add(curbBaseIndex + 3);
                    curbLeftIndices.Add(curbBaseIndex + 6);
                    curbLeftIndices.Add(curbBaseIndex + 6);
                    curbLeftIndices.Add(curbBaseIndex + 3);
                    curbLeftIndices.Add(curbBaseIndex + 8);
                    
                    // 外侧面三角形
                    curbLeftIndices.Add(curbBaseIndex + 2);
                    curbLeftIndices.Add(curbBaseIndex + 7);
                    curbLeftIndices.Add(curbBaseIndex + 5);
                    curbLeftIndices.Add(curbBaseIndex + 5);
                    curbLeftIndices.Add(curbBaseIndex + 7);
                    curbLeftIndices.Add(curbBaseIndex + 9);
                }
                
                // 生成马路牙子网格
                // 1. 添加直线段顶点
                Vector3 roadEdgePoint0Inner = currentEdge.point0CurbExtended - transform.position;
                Vector3 roadEdgePoint1Inner = currentEdge.point1CurbExtended - transform.position;
                Vector3 roadEdgePoint0Outer = currentEdge.point0RoadEdgeExtended - transform.position;
                Vector3 roadEdgePoint1Outer = currentEdge.point1RoadEdgeExtended - transform.position;
                
                int roadEdgeBaseIndex = roadEdgeLeftPositions.Count;
                float roadEdgeHeight = 0.15f;
                float chamferWidth = 0.05f;
                float chamferHeight = 0.05f;
                float chamferAngle = 45f; // 马路牙子内侧倒角角度，默认45度

                // 获取马路牙子参数
                if (currentRoadData != null)
                {
                    // 根据是否使用自定义参数来决定使用哪组参数
                    if (!useCustomParameters)
                    {
                        if (currentRoadData.enableRoadEdge)
                        {
                            chamferWidth = currentRoadData.roadEdgeChamferWidth;
                            chamferHeight = currentRoadData.roadEdgeChamferHeight;
                            chamferAngle = 45f; // 固定内侧倒角角度为45度，因为LoftRoadExtensionData中没有此属性
                            roadEdgeHeight = currentRoadData.roadEdgeHeight;
                        }
                    }
                    // 使用自定义参数时不从道路读取参数，使用JunctionData自身的参数
                }
                
                // 根据倒角角度调整倒角宽度和高度
                if (chamferAngle != 45f) // 如果角度不是默认的45度，则需要重新计算
                {
                    // 将角度转换为弧度
                    float angleRad = Mathf.Deg2Rad * chamferAngle;
                    // 根据角度和高度计算宽度 (tan(angle) = height/width)
                    if (chamferAngle != 90f) // 防止除以零
                    {
                        chamferWidth = chamferHeight / Mathf.Tan(angleRad);
                    }
                    else
                    {
                        chamferWidth = 0f; // 如果是90度，则宽度为0
                    }
                }
                
                // 生成马路牙子网格 - 只保留曲线段部分
                // 1. 添加曲线段顶点
                for(int j = 0; j < roadEdgeInnerCurvePoints.Count - 1; j++)
                {
                    float t = j / (float)(roadEdgeInnerCurvePoints.Count - 1);
                    float interpolatedMeshOffset = Mathf.Lerp(currentMeshOffset, nextMeshOffset, t);
                    
                    roadEdgeBaseIndex = roadEdgeLeftPositions.Count;
                    
                    Vector3 innerPoint1 = roadEdgeInnerCurvePoints[j];
                    Vector3 innerPoint2 = roadEdgeInnerCurvePoints[j + 1];
                    Vector3 outerPoint1 = roadEdgeOuterCurvePoints[j];
                    Vector3 outerPoint2 = roadEdgeOuterCurvePoints[j + 1];
                    
                    // 计算曲线段的倒角点
                    Vector3 curveRight1 = (outerPoint1 - innerPoint1).normalized;
                    Vector3 curveRight2 = (outerPoint2 - innerPoint2).normalized;
                    Vector3 chamferOffset1 = curveRight1 * chamferWidth;
                    Vector3 chamferOffset2 = curveRight2 * chamferWidth;
                    

                    // 添加顶面顶点（包括倒角）
                    roadEdgeLeftPositions.Add(innerPoint1 + Vector3.up * (interpolatedMeshOffset + roadEdgeHeight - chamferHeight)); // 内侧边缘点（高度减去倒角高度）
                    roadEdgeLeftPositions.Add(innerPoint1 + chamferOffset1 + Vector3.up * (interpolatedMeshOffset + roadEdgeHeight)); // 倒角点
                    roadEdgeLeftPositions.Add(outerPoint1 + Vector3.up * (interpolatedMeshOffset + roadEdgeHeight)); // 外侧边缘点
                    roadEdgeLeftPositions.Add(innerPoint2 + Vector3.up * (interpolatedMeshOffset + roadEdgeHeight - chamferHeight)); // 内侧边缘点（下一段）
                    roadEdgeLeftPositions.Add(innerPoint2 + chamferOffset2 + Vector3.up * (interpolatedMeshOffset + roadEdgeHeight)); // 下一段倒角点
                    roadEdgeLeftPositions.Add(outerPoint2 + Vector3.up * (interpolatedMeshOffset + roadEdgeHeight)); // 外侧边缘点（下一段）
                    
                    // 添加底面顶点
                    roadEdgeLeftPositions.Add(innerPoint1 + Vector3.up * interpolatedMeshOffset);
                    roadEdgeLeftPositions.Add(outerPoint1 + Vector3.up * interpolatedMeshOffset);
                    roadEdgeLeftPositions.Add(innerPoint2 + Vector3.up * interpolatedMeshOffset);
                    roadEdgeLeftPositions.Add(outerPoint2 + Vector3.up * interpolatedMeshOffset);
                    
                    // 添加法线
                    // 顶面法线 - 根据倒角角度调整倒角处的法线
                    roadEdgeLeftNormals.Add(Vector3.up);
                    
                    // 计算倒角处的法线 - 根据倒角角度调整
                    float angleRad = Mathf.Deg2Rad * chamferAngle;
                    Vector3 chamferNormal1 = new Vector3(-curveRight1.x * Mathf.Sin(angleRad), Mathf.Cos(angleRad), -curveRight1.z * Mathf.Sin(angleRad)).normalized;
                    Vector3 chamferNormal2 = new Vector3(-curveRight2.x * Mathf.Sin(angleRad), Mathf.Cos(angleRad), -curveRight2.z * Mathf.Sin(angleRad)).normalized;
                    
                    roadEdgeLeftNormals.Add(chamferNormal1);
                    roadEdgeLeftNormals.Add(Vector3.up);
                    roadEdgeLeftNormals.Add(Vector3.up);
                    roadEdgeLeftNormals.Add(chamferNormal2);
                    roadEdgeLeftNormals.Add(Vector3.up);
                    
                    // 底面法线
                    for(int k = 0; k < 4; k++)
                    {
                        roadEdgeLeftNormals.Add(Vector3.down);
                    }
                    
                    float curveRoadEdgeSegmentLength = Vector3.Distance(innerPoint1, innerPoint2);
                    float uvStartV = j * curveRoadEdgeSegmentLength / 10.0f;
                    float uvEndV = (j + 1) * curveRoadEdgeSegmentLength / 10.0f;
                    
                    // 获取马路牙子UV缩放参数
                    float localRoadEdgeUVScaleU = roadEdgeUVScaleU;  // 优先使用Junction自己的参数
                    float localRoadEdgeUVScaleV = roadEdgeUVScaleV;
                    float localRoadEdgeUVOffset = roadEdgeUVOffset;  // 使用Junction自己的UV偏移参数
                    
                    if (!useCustomParameters && currentRoadData != null)
                    {
                        if (currentRoadData.enableRoadEdge)
                        {
                            // 获取UV缩放
                            localRoadEdgeUVScaleU = currentRoadData.roadEdgeUVScaleU;
                            localRoadEdgeUVScaleV = currentRoadData.roadEdgeUVScaleV;
                            
                            // 获取UV偏移
                            localRoadEdgeUVOffset = currentRoadData.uvHorizontalOffset;
                        }
                    }
                    
                    // 马路牙子顶面UV坐标旋转90度，交换U和V坐标，并应用UV缩放参数和水平偏移
                    roadEdgeLeftTextures.Add(new Vector2(uvStartV * localRoadEdgeUVScaleU + localRoadEdgeUVOffset, 1 * localRoadEdgeUVScaleV));
                    roadEdgeLeftTextures.Add(new Vector2(uvStartV * localRoadEdgeUVScaleU + localRoadEdgeUVOffset, 0.8f * localRoadEdgeUVScaleV));
                    roadEdgeLeftTextures.Add(new Vector2(uvStartV * localRoadEdgeUVScaleU + localRoadEdgeUVOffset, 0 * localRoadEdgeUVScaleV));
                    roadEdgeLeftTextures.Add(new Vector2(uvEndV * localRoadEdgeUVScaleU + localRoadEdgeUVOffset, 1 * localRoadEdgeUVScaleV));
                    roadEdgeLeftTextures.Add(new Vector2(uvEndV * localRoadEdgeUVScaleU + localRoadEdgeUVOffset, 0.8f * localRoadEdgeUVScaleV));
                    roadEdgeLeftTextures.Add(new Vector2(uvEndV * localRoadEdgeUVScaleU + localRoadEdgeUVOffset, 0 * localRoadEdgeUVScaleV));
                    
                    // 底面UV坐标旋转90度，并应用UV缩放参数和水平偏移
                    roadEdgeLeftTextures.Add(new Vector2(uvStartV * localRoadEdgeUVScaleU + localRoadEdgeUVOffset, 1 * localRoadEdgeUVScaleV));
                    roadEdgeLeftTextures.Add(new Vector2(uvStartV * localRoadEdgeUVScaleU + localRoadEdgeUVOffset, 0 * localRoadEdgeUVScaleV));
                    roadEdgeLeftTextures.Add(new Vector2(uvEndV * localRoadEdgeUVScaleU + localRoadEdgeUVOffset, 1 * localRoadEdgeUVScaleV));
                    roadEdgeLeftTextures.Add(new Vector2(uvEndV * localRoadEdgeUVScaleU + localRoadEdgeUVOffset, 0 * localRoadEdgeUVScaleV));
                    
                    // 顶面三角形（包括倒角）
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex);
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 1);
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 3);
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 3);
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 1);
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 4);
                    
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 1);
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 2);
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 4);
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 4);
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 2);
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 5);
                    
                    // 底面三角形
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 6);
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 8);
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 7);
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 7);
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 8);
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 9);
                    
                    // 内侧面三角形（包括倒角）
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex);
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 3);
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 6);
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 6);
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 3);
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 8);
                    
                    // 外侧面三角形
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 2);
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 7);
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 5);
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 5);
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 7);
                    roadEdgeLeftIndices.Add(roadEdgeBaseIndex + 9);
                }

                // 生成人行道网格
                // 1. 添加直线段顶点
                Vector3 sidewalkPoint0Inner = currentEdge.point0RoadEdgeExtended - transform.position;
                Vector3 sidewalkPoint1Inner = currentEdge.point1RoadEdgeExtended - transform.position;
                Vector3 sidewalkPoint0Outer = currentEdge.point0SidewalkExtended - transform.position;
                Vector3 sidewalkPoint1Outer = currentEdge.point1SidewalkExtended - transform.position;
                
                int sidewalkBaseIndex = sidewalkVertices.Count;
                float sidewalkHeight = 0.15f;
                
                // 获取人行道高度参数
                if (currentRoadData != null)
                {
                    if (currentRoadData.enableRoadEdge)
                    {
                        // 通常人行道高度与马路牙子高度相同
                        sidewalkHeight = currentRoadData.roadEdgeHeight;
                    }
                }
                
                sidewalkVertices.Add(sidewalkPoint0Inner + Vector3.up * (currentMeshOffset + sidewalkHeight));
                sidewalkVertices.Add(sidewalkPoint0Outer + Vector3.up * (currentMeshOffset + sidewalkHeight));
                sidewalkVertices.Add(sidewalkPoint1Inner + Vector3.up * (currentMeshOffset + sidewalkHeight));
                sidewalkVertices.Add(sidewalkPoint1Outer + Vector3.up * (currentMeshOffset + sidewalkHeight));
                
                for(int j = 0; j < 4; j++)
                {
                    sidewalkNormals.Add(Vector3.up);
                }
                
                float sidewalkWidth = Vector3.Distance(sidewalkPoint0Inner, sidewalkPoint0Outer);
                float sidewalkSegmentLength = Vector3.Distance(sidewalkPoint0Inner, sidewalkPoint1Inner);
                
                sidewalkUvs.Add(new Vector2(1, 0));
                sidewalkUvs.Add(new Vector2(0, 0));
                sidewalkUvs.Add(new Vector2(1, sidewalkSegmentLength / 10.0f));
                sidewalkUvs.Add(new Vector2(0, sidewalkSegmentLength / 10.0f));
                
                sidewalkTriangles.Add(sidewalkBaseIndex);
                sidewalkTriangles.Add(sidewalkBaseIndex + 1);
                sidewalkTriangles.Add(sidewalkBaseIndex + 2);
                sidewalkTriangles.Add(sidewalkBaseIndex + 2);
                sidewalkTriangles.Add(sidewalkBaseIndex + 1);
                sidewalkTriangles.Add(sidewalkBaseIndex + 3);
                
                // 2. 添加曲线段顶点
                for(int j = 0; j < sidewalkInnerCurvePoints.Count - 1; j++)
                {
                    float t = j / (float)(sidewalkInnerCurvePoints.Count - 1);
                    float interpolatedMeshOffset = Mathf.Lerp(currentMeshOffset, nextMeshOffset, t);
                    
                    sidewalkBaseIndex = sidewalkVertices.Count;
                    
                    Vector3 innerPoint1 = sidewalkInnerCurvePoints[j] + Vector3.up * (interpolatedMeshOffset + sidewalkHeight);
                    Vector3 innerPoint2 = sidewalkInnerCurvePoints[j + 1] + Vector3.up * (interpolatedMeshOffset + sidewalkHeight);
                    Vector3 outerPoint1 = sidewalkOuterCurvePoints[j] + Vector3.up * (interpolatedMeshOffset + sidewalkHeight);
                    Vector3 outerPoint2 = sidewalkOuterCurvePoints[j + 1] + Vector3.up * (interpolatedMeshOffset + sidewalkHeight);
                    
                    sidewalkVertices.Add(innerPoint1);
                    sidewalkVertices.Add(outerPoint1);
                    sidewalkVertices.Add(innerPoint2);
                    sidewalkVertices.Add(outerPoint2);
                    
                    for(int k = 0; k < 4; k++)
                    {
                        sidewalkNormals.Add(Vector3.up);
                    }
                    
                    float curveSidewalkSegmentLength = Vector3.Distance(innerPoint1, innerPoint2);
                    float uvStartV = sidewalkSegmentLength / 10.0f + j * curveSidewalkSegmentLength / 10.0f;
                    float uvEndV = sidewalkSegmentLength / 10.0f + (j + 1) * curveSidewalkSegmentLength / 10.0f;
                    
                    sidewalkUvs.Add(new Vector2(1, uvStartV));
                    sidewalkUvs.Add(new Vector2(0, uvStartV));
                    sidewalkUvs.Add(new Vector2(1, uvEndV));
                    sidewalkUvs.Add(new Vector2(0, uvEndV));
                    
                    sidewalkTriangles.Add(sidewalkBaseIndex);
                    sidewalkTriangles.Add(sidewalkBaseIndex + 1);
                    sidewalkTriangles.Add(sidewalkBaseIndex + 2);
                    sidewalkTriangles.Add(sidewalkBaseIndex + 2);
                    sidewalkTriangles.Add(sidewalkBaseIndex + 1);
                    sidewalkTriangles.Add(sidewalkBaseIndex + 3);
                }
                
                // 生成人行道外侧马路牙子网格
                float roadEdgeOuterHeight = 0.15f; // 外侧马路牙子高度
                float roadEdgeOuterWidth = 0.2f; // 外侧马路牙子宽度
                float chamferOuterWidth = 0.05f;  // 外侧马路牙子倒角宽度
                float chamferOuterHeight = 0.05f; // 外侧马路牙子倒角高度
                
                // 获取高度和宽度参数
                if (currentRoadData != null)
                {
                    if (currentRoadData.enableRoadEdge)
                    {
                        roadEdgeOuterHeight = currentRoadData.roadEdgeHeight;
                        roadEdgeOuterWidth = currentRoadData.roadEdgeWidth;
                        chamferOuterWidth = currentRoadData.roadEdgeChamferWidth;
                        chamferOuterHeight = currentRoadData.roadEdgeChamferHeight;
                    }
                }

                // 生成人行道外侧马路牙子(只处理曲线段)
                for(int j = 0; j < sidewalkOuterCurvePoints.Count - 1; j++)
                {
                    float t = j / (float)(sidewalkOuterCurvePoints.Count - 1);
                    float interpolatedMeshOffset = Mathf.Lerp(currentMeshOffset, nextMeshOffset, t);
                    
                    int roadEdgeOuterBaseIndex = roadEdgeOuterLeftPositions.Count;
                    
                    // 获取内外侧点(对于外侧马路牙子，内侧是人行道边缘，外侧是马路牙子外边)
                    Vector3 innerPoint1 = sidewalkOuterCurvePoints[j]; 
                    Vector3 innerPoint2 = sidewalkOuterCurvePoints[j + 1];
                    
                    // 计算外侧点(向外延伸roadEdgeOuterWidth)
                    Vector3 direction1 = (innerPoint1 - sidewalkInnerCurvePoints[j]).normalized;
                    Vector3 direction2 = (innerPoint2 - sidewalkInnerCurvePoints[j+1]).normalized;
                    Vector3 outerPoint1 = innerPoint1 + direction1 * roadEdgeOuterWidth;
                    Vector3 outerPoint2 = innerPoint2 + direction2 * roadEdgeOuterWidth;
                    
                    // 添加顶面顶点(带倒角，倒角在外侧)
                    float finalHeight = interpolatedMeshOffset + sidewalkHeight;
                    
                    // 位移点，用于构成倒角卡边。
                    Vector3 chamferPoint1 = outerPoint1 - direction1 * chamferOuterWidth + Vector3.up * (finalHeight);
                    Vector3 chamferPoint2 = outerPoint2 - direction2 * chamferOuterWidth + Vector3.up * (finalHeight);
                    
                    // 添加顶面顶点(内侧边缘点、倒角点、外侧点)
                    roadEdgeOuterLeftPositions.Add(innerPoint1 + Vector3.up * finalHeight); // 内侧边缘点
                    roadEdgeOuterLeftPositions.Add(chamferPoint1); // 倒角点
                    //倒角高度，用于形成倒角
                    roadEdgeOuterLeftPositions.Add(outerPoint1 + Vector3.up * (interpolatedMeshOffset + roadEdgeOuterHeight - chamferOuterHeight)); // 外侧边缘点(倒角高度)
                    roadEdgeOuterLeftPositions.Add(innerPoint2 + Vector3.up * finalHeight); // 下一段内侧边缘点
                    roadEdgeOuterLeftPositions.Add(chamferPoint2); // 下一段倒角点
                    roadEdgeOuterLeftPositions.Add(outerPoint2 + Vector3.up * (interpolatedMeshOffset + roadEdgeOuterHeight - chamferOuterHeight)); // 下一段外侧边缘点(倒角高度)
                    
                    // 添加底面顶点
                    roadEdgeOuterLeftPositions.Add(innerPoint1 + Vector3.up * interpolatedMeshOffset);
                    roadEdgeOuterLeftPositions.Add(outerPoint1 + Vector3.up * interpolatedMeshOffset);
                    roadEdgeOuterLeftPositions.Add(innerPoint2 + Vector3.up * interpolatedMeshOffset);
                    roadEdgeOuterLeftPositions.Add(outerPoint2 + Vector3.up * interpolatedMeshOffset);
                    
                    // 添加法线
                    // 顶面法线 - 根据倒角角度调整倒角处的法线
                    roadEdgeOuterLeftNormals.Add(Vector3.up); // 内侧边缘点
                    
                    // 计算倒角处的法线 - 45度角指向外侧上方
                    float angleRad = Mathf.Deg2Rad * 45f;
                    Vector3 chamferNormal1 = new Vector3(direction1.x * Mathf.Sin(angleRad), Mathf.Cos(angleRad), direction1.z * Mathf.Sin(angleRad)).normalized;
                    Vector3 chamferNormal2 = new Vector3(direction2.x * Mathf.Sin(angleRad), Mathf.Cos(angleRad), direction2.z * Mathf.Sin(angleRad)).normalized;
                    
                    roadEdgeOuterLeftNormals.Add(chamferNormal1); // 倒角点
                    roadEdgeOuterLeftNormals.Add(Vector3.up); // 外侧边缘点
                    roadEdgeOuterLeftNormals.Add(Vector3.up); // 下一段内侧边缘点
                    roadEdgeOuterLeftNormals.Add(chamferNormal2); // 下一段倒角点
                    roadEdgeOuterLeftNormals.Add(Vector3.up); // 下一段外侧边缘点
                    
                    // 底面法线
                    for(int k = 0; k < 4; k++)
                    {
                        roadEdgeOuterLeftNormals.Add(Vector3.down);
                    }
                    
                    // 计算UV
                    float curveRoadEdgeOuterSegmentLength = Vector3.Distance(innerPoint1, innerPoint2);
                    float uvStartV = j * curveRoadEdgeOuterSegmentLength / 10.0f;
                    float uvEndV = (j + 1) * curveRoadEdgeOuterSegmentLength / 10.0f;
                    
                    // 获取马路牙子UV缩放参数
                    float localRoadEdgeUVScaleU = roadEdgeUVScaleU;  // 优先使用Junction自己的参数
                    float localRoadEdgeUVScaleV = roadEdgeUVScaleV;
                    float localRoadEdgeUVOffset = roadEdgeUVOffset;  // 使用Junction自己的UV偏移参数
                    
                    if (!useCustomParameters && currentRoadData != null)
                    {
                        if (currentRoadData.enableRoadEdge)
                        {
                            localRoadEdgeUVScaleU = currentRoadData.roadEdgeUVScaleU;
                            localRoadEdgeUVScaleV = currentRoadData.roadEdgeUVScaleV;
                            
                            // 获取UV偏移
                            localRoadEdgeUVOffset = currentRoadData.uvHorizontalOffset;
                        }
                    }
                    
                    // 外侧马路牙子顶面UV坐标旋转90度，交换U和V坐标，并应用UV缩放参数和水平偏移
                    roadEdgeOuterLeftTextures.Add(new Vector2(uvStartV * localRoadEdgeUVScaleU + localRoadEdgeUVOffset, 0 * localRoadEdgeUVScaleV)); // 内侧
                    roadEdgeOuterLeftTextures.Add(new Vector2(uvStartV * localRoadEdgeUVScaleU + localRoadEdgeUVOffset, 0.2f * localRoadEdgeUVScaleV)); // 倒角
                    roadEdgeOuterLeftTextures.Add(new Vector2(uvStartV * localRoadEdgeUVScaleU + localRoadEdgeUVOffset, 1 * localRoadEdgeUVScaleV)); // 外侧
                    roadEdgeOuterLeftTextures.Add(new Vector2(uvEndV * localRoadEdgeUVScaleU + localRoadEdgeUVOffset, 0 * localRoadEdgeUVScaleV)); // 下一段内侧
                    roadEdgeOuterLeftTextures.Add(new Vector2(uvEndV * localRoadEdgeUVScaleU + localRoadEdgeUVOffset, 0.2f * localRoadEdgeUVScaleV)); // 下一段倒角
                    roadEdgeOuterLeftTextures.Add(new Vector2(uvEndV * localRoadEdgeUVScaleU + localRoadEdgeUVOffset, 1 * localRoadEdgeUVScaleV)); // 下一段外侧
                    
                    // 底面UV坐标旋转90度，并应用UV缩放参数和水平偏移
                    roadEdgeOuterLeftTextures.Add(new Vector2(uvStartV * localRoadEdgeUVScaleU + localRoadEdgeUVOffset, 0 * localRoadEdgeUVScaleV));
                    roadEdgeOuterLeftTextures.Add(new Vector2(uvStartV * localRoadEdgeUVScaleU + localRoadEdgeUVOffset, 1 * localRoadEdgeUVScaleV));
                    roadEdgeOuterLeftTextures.Add(new Vector2(uvEndV * localRoadEdgeUVScaleU + localRoadEdgeUVOffset, 0 * localRoadEdgeUVScaleV));
                    roadEdgeOuterLeftTextures.Add(new Vector2(uvEndV * localRoadEdgeUVScaleU + localRoadEdgeUVOffset, 1 * localRoadEdgeUVScaleV));
                    
                    // 顶面三角形(包含倒角)
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex); // 内侧到倒角的第一个三角形
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 1);
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 3);
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 3);
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 1);
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 4);
                    
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 1); // 倒角到外侧的第二个三角形
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 2);
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 4);
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 4);
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 2);
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 5);
                    
                    // 底面三角形(两个三角形)
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 6);
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 8);
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 7);
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 7);
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 8);
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 9);
                    
                    // 内侧面三角形(连接内侧的顶面和底面)
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex);
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 3);
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 6);
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 6);
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 3);
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 8);
                    
                    // 外侧面三角形(连接外侧的顶面(包括倒角)和底面)
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 2); // 倒角点到外侧底部
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 7);
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 5);
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 5);
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 7);
                    roadEdgeOuterLeftIndices.Add(roadEdgeOuterBaseIndex + 9);
                }
            }
            
            // 确保所有子对象存在
            EnsureChildObjects();

            bool roadUploadOk = BuildAndUploadRoadDomain();
            bool sidewalkUploadOk = BuildAndUploadSidewalkBundleDomain();
            if (!roadUploadOk && roadVertices.Count > 0 && roadTriangles.Count > 0)
            {
                Debug.LogError($"Failed to upload junction road geometry for {name}.");
            }

            if (!sidewalkUploadOk
                && (sidewalkTriangles.Count > 0
                    || curbLeftIndices.Count > 0
                    || curbRightIndices.Count > 0
                    || roadEdgeLeftIndices.Count > 0
                    || roadEdgeRightIndices.Count > 0
                    || roadEdgeOuterLeftIndices.Count > 0
                    || roadEdgeOuterRightIndices.Count > 0))
            {
                Debug.LogError($"Failed to upload junction sidewalk bundle geometry for {name}.");
            }

            RefreshRoadCunningMesh(roadUploadOk);
            RefreshSidewalkCunningMesh(sidewalkUploadOk);
            
            // Debug绘制
            for (int i = 0; i < edgePoints.Count; i++)
            {
                int nextIndex = (i + 1) % edgePoints.Count;
                var currentRoad = validConnectedRoads[i];
                var currentEdge = edgePoints[i];
                var nextEdge = edgePoints[nextIndex];
                
                // 获取当前道路的meshOffset
                float currentMeshOffset = meshOffsets[i];
                float nextMeshOffset = meshOffsets[nextIndex];
                
                // 绘制当前点
                DrawPointMarker(currentRoad.GetConnectionPoint(), 0.5f, Color.red, 0f);
                
                // 绘制原始边缘线
                Vector3 point0WithOffset = currentEdge.point0 + Vector3.up * currentMeshOffset;
                Vector3 point1WithOffset = currentEdge.point1 + Vector3.up * currentMeshOffset;
                Debug.DrawLine(point0WithOffset, point1WithOffset, Color.green, 0f);
                
                // 路缘延伸线（青色）
                Vector3 point0CurbExtendedWithOffset = currentEdge.point0CurbExtended + Vector3.up * currentMeshOffset;
                Vector3 point1CurbExtendedWithOffset = currentEdge.point1CurbExtended + Vector3.up * currentMeshOffset;
                Debug.DrawLine(point0WithOffset, point0CurbExtendedWithOffset, Color.cyan, 0f);
                Debug.DrawLine(point1WithOffset, point1CurbExtendedWithOffset, Color.cyan, 0f);
                
                // 马路牙子延伸线（橙色）
                Vector3 point0RoadEdgeExtendedWithOffset = currentEdge.point0RoadEdgeExtended + Vector3.up * currentMeshOffset;
                Vector3 point1RoadEdgeExtendedWithOffset = currentEdge.point1RoadEdgeExtended + Vector3.up * currentMeshOffset;
                Debug.DrawLine(point0CurbExtendedWithOffset, point0RoadEdgeExtendedWithOffset, new Color(1f, 0.5f, 0f), 0f);
                Debug.DrawLine(point1CurbExtendedWithOffset, point1RoadEdgeExtendedWithOffset, new Color(1f, 0.5f, 0f), 0f);
                
                // 人行道延伸线（紫色）
                Vector3 point0SidewalkExtendedWithOffset = currentEdge.point0SidewalkExtended + Vector3.up * currentMeshOffset;
                Vector3 point1SidewalkExtendedWithOffset = currentEdge.point1SidewalkExtended + Vector3.up * currentMeshOffset;
                Debug.DrawLine(point0RoadEdgeExtendedWithOffset, point0SidewalkExtendedWithOffset, new Color(0.5f, 0f, 0.5f), 0f);
                Debug.DrawLine(point1RoadEdgeExtendedWithOffset, point1SidewalkExtendedWithOffset, new Color(0.5f, 0f, 0.5f), 0f);
                
                // 绘制法线方向
                float normalLength = 2f;
                // 中心点法线
                Vector3 centerPoint = (point0WithOffset + point1WithOffset) * 0.5f;
                Debug.DrawLine(centerPoint, centerPoint + currentEdge.normal * normalLength, Color.blue, 0f);
                // 左侧点法线（使用旋转后的法线）
                Debug.DrawLine(point0WithOffset, point0WithOffset + currentEdge.normal0 * normalLength, Color.blue, 0f);
                // 右侧点法线（使用旋转后的法线）
                Debug.DrawLine(point1WithOffset, point1WithOffset + currentEdge.normal1 * normalLength, Color.blue, 0f);
                
                // 连接当前边的1点到下一个边的0点（使用曲线）
                DrawCurvedLine(
                    point1WithOffset,
                    nextEdge.point0 + Vector3.up * nextMeshOffset,
                    currentEdge.normal1,  // 使用旋转后的法线
                    nextEdge.normal0,     // 使用旋转后的法线
                    Color.yellow
                );

                // 路缘延伸点曲线（青色）
                DrawCurvedLine(
                    point1CurbExtendedWithOffset,
                    nextEdge.point0CurbExtended + Vector3.up * nextMeshOffset,
                    currentEdge.normal1,
                    nextEdge.normal0,
                    Color.cyan
                );
                
                // 马路牙子延伸点曲线（橙色）
                DrawCurvedLine(
                    point1RoadEdgeExtendedWithOffset,
                    nextEdge.point0RoadEdgeExtended + Vector3.up * nextMeshOffset,
                    currentEdge.normal1,
                    nextEdge.normal0,
                    new Color(1f, 0.5f, 0f)
                );
                
                // 人行道延伸点曲线（紫色）
                DrawCurvedLine(
                    point1SidewalkExtendedWithOffset,
                    nextEdge.point0SidewalkExtended + Vector3.up * nextMeshOffset,
                    currentEdge.normal1,
                    nextEdge.normal0,
                    new Color(0.5f, 0f, 0.5f)
                );
            }
            
            // 更新斑马线
            JunctionGenerator.UpdateZebraCrossings(this);

            // 更新车道连接
            if (!isPreview)
            {
                UpdateLaneConnections();
            }
        }

        private void OnDrawGizmos()
        {
            // 检查是否应该显示Gizmos
            if (!ShowGizmos)
            {
                return;
            }

            if (isPreview)
            {
                return;
            }

            Vector3 center = GetJunctionCenter();
            if (!TryCollectValidConnectedRoadData(center, out var validConnectedRoads, out var edgePoints, out var meshOffsets))
                return;

            // 显示中心点
            if (ShowCenterPoints)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(center, 0.5f);
            }
            
            for (int i = 0; i < validConnectedRoads.Count; i++)
            {
                var currentEdge = edgePoints[i];
                int nextIndex = (i + 1) % edgePoints.Count;
                var nextEdge = edgePoints[nextIndex];
                
                float currentMeshOffset = meshOffsets[i];
                float nextMeshOffset = meshOffsets[nextIndex];

                Vector3 point0WithOffset = currentEdge.point0 + Vector3.up * currentMeshOffset;
                Vector3 point1WithOffset = currentEdge.point1 + Vector3.up * currentMeshOffset;
                
                // 路缘延伸点
                Vector3 point0CurbExtendedWithOffset = currentEdge.point0CurbExtended + Vector3.up * currentMeshOffset;
                Vector3 point1CurbExtendedWithOffset = currentEdge.point1CurbExtended + Vector3.up * currentMeshOffset;
                
                // 马路牙子延伸点
                Vector3 point0RoadEdgeExtendedWithOffset = currentEdge.point0RoadEdgeExtended + Vector3.up * currentMeshOffset;
                Vector3 point1RoadEdgeExtendedWithOffset = currentEdge.point1RoadEdgeExtended + Vector3.up * currentMeshOffset;
                
                // 人行道延伸点
                Vector3 point0SidewalkExtendedWithOffset = currentEdge.point0SidewalkExtended + Vector3.up * currentMeshOffset;
                Vector3 point1SidewalkExtendedWithOffset = currentEdge.point1SidewalkExtended + Vector3.up * currentMeshOffset;
                
                // 显示边缘线
                if (ShowEdgeLines)
                {
                    // 道路边缘线（绿色）
                    Gizmos.color = Color.green;
                    Gizmos.DrawLine(point0WithOffset, point1WithOffset);
                }
                
                // 显示人行道线
                if (ShowSidewalkLines)
                {
                    // 路缘延伸线（青色）
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawLine(point0WithOffset, point0CurbExtendedWithOffset);
                    Gizmos.DrawLine(point1WithOffset, point1CurbExtendedWithOffset);

                    // 马路牙子延伸线（橙色）
                    Gizmos.color = new Color(1f, 0.5f, 0f);
                    Gizmos.DrawLine(point0CurbExtendedWithOffset, point0RoadEdgeExtendedWithOffset);
                    Gizmos.DrawLine(point1CurbExtendedWithOffset, point1RoadEdgeExtendedWithOffset);

                    // 人行道延伸线（紫色）
                    Gizmos.color = new Color(0.5f, 0f, 0.5f);
                    Gizmos.DrawLine(point0RoadEdgeExtendedWithOffset, point0SidewalkExtendedWithOffset);
                    Gizmos.DrawLine(point1RoadEdgeExtendedWithOffset, point1SidewalkExtendedWithOffset);
                }
                
                // 显示边缘点
                if (ShowEdgePoints)
                {
                    Color handleColor0 = (selectedRoadIndex == i && isPoint0Selected) ? Color.yellow : Color.white;
                    Color handleColor1 = (selectedRoadIndex == i && !isPoint0Selected) ? Color.yellow : Color.white;
                    
                    Gizmos.color = handleColor0;
                    Gizmos.DrawSphere(point0WithOffset, 1.5f);
                    
                    Gizmos.color = handleColor1;
                    Gizmos.DrawSphere(point1WithOffset, 1.5f);
                }
                
                // 显示标签
                if (ShowLabels)
                {
                    UnityEditor.Handles.color = Color.white;
                    UnityEditor.Handles.Label(point0WithOffset + Vector3.up * 0.3f, $"{i}-0");
                    UnityEditor.Handles.Label(point1WithOffset + Vector3.up * 0.3f, $"{i}-1");
                    UnityEditor.Handles.Label(point0CurbExtendedWithOffset + Vector3.up * 0.3f, $"{i}-0C");
                    UnityEditor.Handles.Label(point1CurbExtendedWithOffset + Vector3.up * 0.3f, $"{i}-1C");
                    UnityEditor.Handles.Label(point0RoadEdgeExtendedWithOffset + Vector3.up * 0.3f, $"{i}-0R");
                    UnityEditor.Handles.Label(point1RoadEdgeExtendedWithOffset + Vector3.up * 0.3f, $"{i}-1R");
                    UnityEditor.Handles.Label(point0SidewalkExtendedWithOffset + Vector3.up * 0.3f, $"{i}-0S");
                    UnityEditor.Handles.Label(point1SidewalkExtendedWithOffset + Vector3.up * 0.3f, $"{i}-1S");
                }
                
                // 显示法线
                if (ShowNormals)
                {
                    float normalLength = 2f;
                    UnityEditor.Handles.color = Color.blue;
                    
                    // 中心点法线
                    Vector3 centerPoint = (point0WithOffset + point1WithOffset) * 0.5f;
                    UnityEditor.Handles.DrawLine(centerPoint, centerPoint + currentEdge.normal * normalLength);
                    
                    // 左侧点法线
                    UnityEditor.Handles.DrawLine(point0WithOffset, point0WithOffset + currentEdge.normal0 * normalLength);
                    
                    // 右侧点法线
                    UnityEditor.Handles.DrawLine(point1WithOffset, point1WithOffset + currentEdge.normal1 * normalLength);
                }
                
                // 显示曲线
                if (ShowCurves)
                {
                    // 道路边缘曲线（黄色）
                    DrawCurvedLine(
                        point1WithOffset,
                        nextEdge.point0 + Vector3.up * nextMeshOffset,
                        currentEdge.normal1,
                        nextEdge.normal0,
                        Color.yellow
                    );
                    
                    // 路缘延伸点曲线（青色）
                    DrawCurvedLine(
                        point1CurbExtendedWithOffset,
                        nextEdge.point0CurbExtended + Vector3.up * nextMeshOffset,
                        currentEdge.normal1,
                        nextEdge.normal0,
                        Color.cyan
                    );
                    
                    // 马路牙子延伸点曲线（橙色）
                    DrawCurvedLine(
                        point1RoadEdgeExtendedWithOffset,
                        nextEdge.point0RoadEdgeExtended + Vector3.up * nextMeshOffset,
                        currentEdge.normal1,
                        nextEdge.normal0,
                        new Color(1f, 0.5f, 0f)
                    );
                    
                    // 人行道延伸点曲线（紫色）
                    DrawCurvedLine(
                        point1SidewalkExtendedWithOffset,
                        nextEdge.point0SidewalkExtended + Vector3.up * nextMeshOffset,
                        currentEdge.normal1,
                        nextEdge.normal0,
                        new Color(0.5f, 0f, 0.5f)
                    );
                }
                
                // 如果该路段被选中，显示旋转控制
                if (selectedRoadIndex == i)
                {
                    Vector3 selectedPoint = isPoint0Selected ? point0WithOffset : point1WithOffset;
                    Vector3 selectedNormal = isPoint0Selected ? currentEdge.normal0 : currentEdge.normal1;
                    
                    // 绘制旋转控制环
                    UnityEditor.Handles.color = Color.cyan;
                    UnityEditor.Handles.DrawWireDisc(selectedPoint, Vector3.up, 2f);
                    
                    // 绘制当前角度
                    if (ShowNormals)
                    {
                        UnityEditor.Handles.color = Color.blue;
                        UnityEditor.Handles.DrawLine(selectedPoint, selectedPoint + selectedNormal * 3f);
                    }
                }
            }

            // 绘制车道连接
            DrawLaneConnectionGizmos();
        }

        public void UpdateMeshData()
        {
            if(mesh == null)
            {
                meshVertices.Clear();
                meshTriangles.Clear();
                return;
            }
            
            // 进入编辑模式时，设置忽略来自道路的更新
            ignoreRoadUpdates = true;
            
            // 更新网格顶点数据
            meshVertices.Clear();
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uvs = mesh.uv;
            
            for(int i = 0; i < vertices.Length; i++)
            {
                Vector3 normal = i < normals.Length ? normals[i] : Vector3.up;
                Vector2 uv = i < uvs.Length ? uvs[i] : Vector2.zero;
                meshVertices.Add(new MeshVertex(vertices[i], normal, uv));
            }
            
            // 更新三角形索引
            meshTriangles.Clear();
            meshTriangles.AddRange(mesh.triangles);
        }

        public void ApplyMeshChanges()
        {
            if(mesh == null) return;
            
            Vector3[] vertices = new Vector3[meshVertices.Count];
            Vector3[] normals = new Vector3[meshVertices.Count];
            Vector2[] uvs = new Vector2[meshVertices.Count];
            
            for(int i = 0; i < meshVertices.Count; i++)
            {
                vertices[i] = meshVertices[i].position;
                normals[i] = meshVertices[i].normal;
                uvs[i] = meshVertices[i].uv;
            }
            
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = meshTriangles.ToArray();
            
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
        }

        public void SetIgnoreRoadUpdates(bool ignore)
        {
            ignoreRoadUpdates = ignore;
        }

        internal void SortConnectedRoads()
        {
            if (connectedRoads == null || connectedRoads.Count < 2)
            {
                return;
            }

            Vector3 center = GetJunctionCenter();
            var validRoads = new List<(ConnectedRoad road, Vector3 position)>(connectedRoads.Count);
            var invalidRoads = new List<ConnectedRoad>();

            foreach (var road in connectedRoads)
            {
                if (road == null || !road.HasValidSplineReference())
                {
                    invalidRoads.Add(road);
                    continue;
                }

                validRoads.Add((road, road.GetConnectionPoint()));
            }

            if (validRoads.Count < 2)
            {
                return;
            }

            validRoads.Sort((left, right) =>
                GetClockwiseAngleXZ(right.position, center).CompareTo(GetClockwiseAngleXZ(left.position, center)));

            connectedRoads = validRoads.Select(entry => entry.road).Concat(invalidRoads).ToList();
        }

        private static float GetClockwiseAngleXZ(Vector3 point, Vector3 center)
        {
            return Mathf.Atan2(point.z - center.z, point.x - center.x);
        }

        private void InitializeLaneConnector()
        {
            laneConnector = new JunctionLaneConnector(this);
        }

        private void DrawLaneConnectionGizmos()
        {
            if (ShowLaneConnections && laneConnector != null)
            {
                laneConnector.DrawGizmos();
            }
        }

        private void UpdateLaneConnections()
        {
            SortConnectedRoads();
            if (laneConnector != null)
            {
                laneConnector.GenerateConnections();
            }
        }

        // 这个类负责处理路口的车道连接
        private class JunctionLaneConnector
        {
            private readonly struct JunctionLaneEndpoint
            {
                public readonly Vector3 pos;
                public readonly Vector3 normal;
                public readonly int laneIndex;
                public readonly ConnectedRoad road;
                public readonly long markerId;

                public JunctionLaneEndpoint(Vector3 pos, Vector3 normal, int laneIndex, ConnectedRoad road, long markerId)
                {
                    this.pos = pos;
                    this.normal = normal;
                    this.laneIndex = laneIndex;
                    this.road = road;
                    this.markerId = markerId;
                }
            }

            // 车道连接数据结构
            [System.Serializable]
            public class LaneConnection
            {
                public Vector3 startPoint;      // 入口点
                public Vector3 endPoint;        // 出口点
                public Vector3 startNormal;     // 入口法线
                public Vector3 endNormal;       // 出口法线
                public int startLaneIndex;      // 入口车道索引
                public int endLaneIndex;        // 出口车道索引
                public List<Vector3> curvePoints = new List<Vector3>();  // 曲线上的点
                
                // 添加路口和knot信息
                public int startRoadID;         // 起点道路实例ID
                public int endRoadID;           // 终点道路实例ID
                public int startSplineIndex;    // 起点spline索引
                public int endSplineIndex;      // 终点spline索引
                public int startKnotIndex;      // 起点knot索引
                public int endKnotIndex;        // 终点knot索引
                public JunctionData startJunction; // 起点所属路口
                public JunctionData endJunction;   // 终点所属路口
            }

            private JunctionData junctionData;
            private List<LaneConnection> laneConnections = new List<LaneConnection>();
            
            public JunctionLaneConnector(JunctionData junction)
            {
                this.junctionData = junction;
            }

            // 生成车道连接
            public void GenerateConnections()
            {
                if (junctionData == null)
                {
                    return;
                }

                laneConnections.Clear();

                junctionData.CalculateLtRVectors();
                var inLanes = new List<JunctionLaneEndpoint>();
                var outLanes = new List<JunctionLaneEndpoint>();
                var endpointBuffer = new List<LoftRoadBehaviour.LaneJunctionEndpointInfo>();
                var collectedEndpointKeys = new HashSet<(int roadId, int splineIndex, long markerId, int laneIndex, bool isInbound)>();

                foreach (var road in junctionData.connectedRoads)
                {
                    if (road == null || !road.HasValidSplineReference() || !road.TryGetRoadData(out var roadData))
                    {
                        continue;
                    }

                    endpointBuffer.Clear();
                    road.roadBehaviour.CollectLaneEndpointsForJunction(junctionData, road.splineIndex, road.markerId, endpointBuffer);
                    for (int endpointIndex = 0; endpointIndex < endpointBuffer.Count; endpointIndex++)
                    {
                        LoftRoadBehaviour.LaneJunctionEndpointInfo endpoint = endpointBuffer[endpointIndex];
                        var endpointKey = (
                            roadId: road.roadBehaviour != null ? road.roadBehaviour.GetInstanceID() : 0,
                            splineIndex: endpoint.splineIndex,
                            markerId: endpoint.boundaryMarkerId,
                            laneIndex: endpoint.localLaneIndex,
                            isInbound: endpoint.isInboundLane);
                        if (!collectedEndpointKeys.Add(endpointKey))
                        {
                            continue;
                        }

                        var laneEndpoint = new JunctionLaneEndpoint(
                            endpoint.point,
                            endpoint.inwardNormal,
                            endpoint.localLaneIndex,
                            road,
                            endpoint.boundaryMarkerId);

                        if (endpoint.isInboundLane)
                        {
                            inLanes.Add(laneEndpoint);
                        }
                        else
                        {
                            outLanes.Add(laneEndpoint);
                        }
                    }
                }

                Debug.Log($"找到 {inLanes.Count} 个入口车道和 {outLanes.Count} 个出口车道");

                foreach (var outLane in outLanes)
                {
                    var possibleInLanes = inLanes.Where(inLane => 
                    {
                        if (inLane.road == null || outLane.road == null)
                        {
                            return false;
                        }

                        if (inLane.road.roadBehaviour == outLane.road.roadBehaviour &&
                            inLane.road.splineIndex == outLane.road.splineIndex &&
                            inLane.markerId == outLane.markerId)
                        {
                            return false;
                        }

                        if (!junctionData.roadLtRVectors.TryGetValue(inLane.road, out var ltRData)) return false;

                        Vector3 connectionDirection = (outLane.pos - inLane.pos).normalized;
                        float angle = Vector3.Angle(connectionDirection, ltRData.ltRVector);

                        if (outLane.road == null || !outLane.road.TryGetRoadData(out var outRoadData))
                            return false;

                        if (outLane.laneIndex >= outRoadData.leftLaneCount)
                        {
                            int forwardLaneIndex = outLane.laneIndex - outRoadData.leftLaneCount;
                            int forwardLaneCount = outRoadData.rightLaneCount;

                            if (forwardLaneCount > 3 && forwardLaneIndex >= forwardLaneCount - 2)
                            {
                                return true;
                            }

                            return angle <= 100;
                        }

                        return angle <= 100;
                    }).ToList();

                    possibleInLanes.Sort((a, b) =>
                    {
                        float angleA = Vector3.Angle(outLane.normal, a.normal);
                        float angleB = Vector3.Angle(outLane.normal, b.normal);
                        float distanceA = Vector3.Distance(outLane.pos, a.pos);
                        float distanceB = Vector3.Distance(outLane.pos, b.pos);
                        
                        // 计算综合得分（角度权重更大）
                        float scoreA = angleA * 2 + distanceA;
                        float scoreB = angleB * 2 + distanceB;
                        return scoreA.CompareTo(scoreB);
                    });

                    foreach (var inLane in possibleInLanes)
                    {
                        var connection = new LaneConnection
                        {
                            startPoint = outLane.pos,
                            endPoint = inLane.pos,
                            startNormal = outLane.normal,
                            endNormal = inLane.normal,
                            startLaneIndex = outLane.laneIndex,
                            endLaneIndex = inLane.laneIndex,
                            startRoadID = outLane.road.roadBehaviour.GetInstanceID(),
                            endRoadID = inLane.road.roadBehaviour.GetInstanceID(),
                            startSplineIndex = outLane.road.splineIndex,
                            endSplineIndex = inLane.road.splineIndex,
                            startKnotIndex = outLane.road.cachedPreferredKnotIndex >= 0 ? outLane.road.cachedPreferredKnotIndex : outLane.road.knotIndex,
                            endKnotIndex = inLane.road.cachedPreferredKnotIndex >= 0 ? inLane.road.cachedPreferredKnotIndex : inLane.road.knotIndex,
                            startJunction = junctionData,
                            endJunction = junctionData
                        };

                        connection.curvePoints = GenerateCurvePoints(
                            connection.startPoint,
                            connection.endPoint,
                            connection.startNormal,
                            connection.endNormal
                        );

                        laneConnections.Add(connection);
                    }
                }
            }

            // 生成曲线点
            private List<Vector3> GenerateCurvePoints(Vector3 start, Vector3 end, Vector3 startNormal, Vector3 endNormal)
            {
                List<Vector3> points = new List<Vector3>();
                float curveStrength = junctionData.curveParameter * Vector3.Distance(start, end);
                
                for (int i = 0; i <= junctionData.curveSegments; i++)
                {
                    float t = i / (float)junctionData.curveSegments;
                    
                    // 计算控制点
                    Vector3 startControl = start + startNormal * curveStrength;
                    Vector3 endControl = end + endNormal * curveStrength;
                    
                    // 计算贝塞尔曲线上的点
                    Vector3 point = BezierPoint(start, startControl, endControl, end, t);
                    points.Add(point);
                }
                
                return points;
            }

            private Vector3 BezierPoint(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
            {
                float u = 1 - t;
                float tt = t * t;
                float uu = u * u;
                float uuu = uu * u;
                float ttt = tt * t;
                
                Vector3 p = uuu * p0;
                p += 3 * uu * t * p1;
                p += 3 * u * tt * p2;
                p += ttt * p3;
                
                return p;
            }

            // 绘制车道连接的Gizmos
            public void DrawGizmos()
            {
                if (junctionData == null || laneConnections == null || laneConnections.Count == 0)
                    return;
                
                int currentJunctionID = junctionData.GetInstanceID();
                
                foreach (var connection in laneConnections)
                {
                    // 确保连接点至少有一端属于当前路口
                    bool startBelongsToThisJunction = connection.startJunction == null || connection.startJunction.GetInstanceID() == currentJunctionID;
                    bool endBelongsToThisJunction = connection.endJunction == null || connection.endJunction.GetInstanceID() == currentJunctionID;
                    
                    if (!startBelongsToThisJunction && !endBelongsToThisJunction)
                    {
                        // 如果连接的两端都不属于当前路口，则跳过
                        continue;
                    }
                    
                    // 根据连接端点的路口信息选择不同的颜色
                    Color curveColor;
                    
                    if (startBelongsToThisJunction && endBelongsToThisJunction)
                    {
                        // 连接完全属于当前路口 - 使用绿色
                        curveColor = Color.green;
                    }
                    else if (startBelongsToThisJunction)
                    {
                        // 只有起点属于当前路口 - 使用蓝色
                        curveColor = Color.blue;
                    }
                    else
                    {
                        // 只有终点属于当前路口 - 使用红色
                        curveColor = Color.red;
                    }
                    
                    // 设置线条颜色和宽度
                    Gizmos.color = curveColor;
                    
                    // 绘制曲线
                    if (connection.curvePoints.Count >= 2)
                    {
                        for (int i = 0; i < connection.curvePoints.Count - 1; i++)
                        {
                            Gizmos.DrawLine(connection.curvePoints[i], connection.curvePoints[i + 1]);
                        }
                    }
                    else
                    {
                        // 如果没有曲线点，就直接绘制一条直线
                        Gizmos.DrawLine(connection.startPoint, connection.endPoint);
                    }
                    
                    // 在曲线起点和终点绘制小球体
                    float sphereRadius = 0.2f;
                    
                    if (startBelongsToThisJunction)
                    {
                        Gizmos.color = Color.yellow;
                        Gizmos.DrawSphere(connection.startPoint, sphereRadius);
                    }
                    
                    if (endBelongsToThisJunction)
                    {
                        Gizmos.color = Color.cyan;
                        Gizmos.DrawSphere(connection.endPoint, sphereRadius);
                    }
                }
            }
        }

        // 计算LtRVector的方法
        private void CalculateLtRVectors()
        {
            roadLtRVectors.Clear();
            Vector3 center = GetJunctionCenter();
            if (!TryCollectValidConnectedRoadData(center, out var validConnectedRoads, out var edgePoints, out _))
                return;

            for (int i = 0; i < validConnectedRoads.Count; i++)
            {
                var road = validConnectedRoads[i];
                var currentEdgePoints = edgePoints[i];
                
                // 获取人行道点列表
                var leftPoints = currentEdgePoints.leftSidewalkPoints;
                var rightPoints = currentEdgePoints.rightSidewalkPoints;

                if (leftPoints.Count >= 2 && rightPoints.Count >= 2)
                {
                    // 使用-1S到0S的连线长度
                    float length = Vector3.Distance(leftPoints[0], leftPoints[1]);
                    
                    // 使用0S到1S的方向作为LtRVector（从左侧指向右侧）
                    Vector3 ltRVector = (leftPoints[1] - rightPoints[1]).normalized;

                    roadLtRVectors[road] = (ltRVector, length);
                    
                    // Debug输出
                    Debug.Log($"Road {road.roadBehaviour.name} LtRVector: {ltRVector}, Length: {length}");
                }
            }
        }

        // 添加一个方法来获取路口的边缘线段
        public List<(Vector3, Vector3)> GetJunctionEdgeLines()
        {
            List<(Vector3, Vector3)> edgeLines = new List<(Vector3, Vector3)>();
            Vector3 center = GetJunctionCenter();
            if (!TryCollectValidConnectedRoadData(center, out _, out var edgePoints, out _))
                return edgeLines;

            foreach (var currentEdgePoints in edgePoints)
            {
                // 使用路口边缘的扩展点，这些是绿色Gizmo绘制的点
                edgeLines.Add((currentEdgePoints.point0RoadEdgeExtended, currentEdgePoints.point1RoadEdgeExtended));
            }
            return edgeLines;
        }

        // 判断点是否在路口边缘线范围内
        public bool 
        IsPointNearJunctionEdge(Vector3 point, float tolerance = 0.1f)
        {
            var edgeLines = GetJunctionEdgeLines();
            foreach (var (start, end) in edgeLines)
            {
                // 计算点到线段的最短距离
                Vector3 lineDirection = end - start;
                float lineLength = lineDirection.magnitude;
                if (lineLength <= Mathf.Epsilon)
                    continue;

                lineDirection /= lineLength;

                Vector3 pointVector = point - start;
                float dot = Vector3.Dot(pointVector, lineDirection);

                // 如果点在线段投影范围内
                if (dot >= 0 && dot <= lineLength)
                {
                    Vector3 projection = start + lineDirection * dot;
                    float distance = Vector3.Distance(point, projection);
                    if (distance <= tolerance)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // 在Scene视图中显示边缘线检测范围
        private void OnDrawGizmosSelected()
        {
            var edgeLines = GetJunctionEdgeLines();
            Gizmos.color = Color.yellow;
            
            foreach (var (start, end) in edgeLines)
            {
                // 绘制边缘线
                Gizmos.DrawLine(start, end);
                
                // 绘制检测范围
                float tolerance = 0.1f;
                Vector3 direction = (end - start).normalized;
                Vector3 perpendicular = Vector3.Cross(direction, Vector3.up).normalized * tolerance;
                
                // 绘制检测范围的边界
                Gizmos.color = new Color(1, 1, 0, 0.2f);
                Vector3[] bounds = new Vector3[]
                {
                    start + perpendicular,
                    end + perpendicular,
                    end - perpendicular,
                    start - perpendicular
                };
                
                for (int i = 0; i < bounds.Length; i++)
                {
                    Gizmos.DrawLine(bounds[i], bounds[(i + 1) % bounds.Length]);
                }
            }
        }

        // 新增方法：获取路口人行道最外侧的边缘线段
        public List<(Vector3, Vector3)> GetOuterSidewalkEdgeLines()
        {
            var edgeLines = new List<(Vector3, Vector3)>();
            if (connectedRoads.Count < 2) return edgeLines;

            Vector3 center = GetJunctionCenter();
            if (!TryCollectValidConnectedRoadData(center, out var validConnectedRoads, out var edgePointsList, out var meshOffsets) ||
                validConnectedRoads.Count < 2)
            {
                return edgeLines;
            }

            for (int i = 0; i < validConnectedRoads.Count; i++)
            {
                var currentEdge = edgePointsList[i];
                int nextIndex = (i + 1) % validConnectedRoads.Count;
                var nextEdge = edgePointsList[nextIndex];
                
                float currentMeshOffset = meshOffsets[i];
                float nextMeshOffset = meshOffsets[nextIndex];

                // 获取人行道最外侧的点
                Vector3 p1 = currentEdge.point1SidewalkExtended + Vector3.up * currentMeshOffset;
                Vector3 p2 = nextEdge.point0SidewalkExtended + Vector3.up * nextMeshOffset;
                
                // 这两点构成了一条曲线，我们需要将其近似为一系列短线段
                var curvePoints = GenerateCurvePoints(p1, p2, currentEdge.normal1, nextEdge.normal0);
                for(int j = 0; j < curvePoints.Count - 1; j++)
                {
                    edgeLines.Add((transform.TransformPoint(curvePoints[j]), transform.TransformPoint(curvePoints[j+1])));
                }
            }

            return edgeLines;
        }

        public int GetCunningPolylineCount()
        {
            if (!TryGetJunctionPolylineContext(out _, out _, out _))
            {
                return 0;
            }

            return 1;
        }

        public bool TryGetCunningPolyline(int index, List<Vector3> points, List<Vector3> normals, out bool closed, out int kindIndex)
        {
            closed = false;
            kindIndex = JunctionRoadPolylineKind;

            if (points == null || normals == null || index < 0)
            {
                return false;
            }

            if (index != 0)
            {
                return false;
            }

            closed = true;
            kindIndex = JunctionRoadPolylineKind;
            return TryBuildRoadBoundaryPolyline(points, normals);
        }

        public bool TryGetCunningPolylinePrimitiveIntTag(int index, out string attrName, out int attrValue)
        {
            attrName = "junction_side";
            attrValue = 0;
            return index == 0 && GetCunningPolylineCount() > 0;
        }

        private bool TryBuildRoadBoundaryPolyline(List<Vector3> points, List<Vector3> normals)
        {
            points.Clear();
            normals.Clear();

            Vector3 center = GetJunctionCenter();
            if (!TryCollectValidConnectedRoadData(center, out var validConnectedRoads, out var edgePoints, out var meshOffsets) ||
                validConnectedRoads.Count < 2)
            {
                return false;
            }

            for (int i = 0; i < edgePoints.Count; i++)
            {
                int nextIndex = (i + 1) % edgePoints.Count;
                var currentEdge = edgePoints[i];
                var nextEdge = edgePoints[nextIndex];
                float currentMeshOffset = meshOffsets[i];
                float nextMeshOffset = meshOffsets[nextIndex];

                AppendPolylinePoint(points, normals, currentEdge.point0 + Vector3.up * currentMeshOffset, currentEdge.normal0);
                AppendPolylinePoint(points, normals, currentEdge.point1 + Vector3.up * currentMeshOffset, currentEdge.normal1);

                var curvePoints = GenerateCurvePointsWorld(
                    currentEdge.point1,
                    nextEdge.point0,
                    currentEdge.normal1,
                    nextEdge.normal0);

                for (int j = 1; j < curvePoints.Count - 1; j++)
                {
                    float t = j / (float)(curvePoints.Count - 1);
                    Vector3 curvePoint = curvePoints[j] + Vector3.up * Mathf.Lerp(currentMeshOffset, nextMeshOffset, t);
                    Vector3 curveNormal = Vector3.Slerp(currentEdge.normal1, nextEdge.normal0, t).normalized;
                    AppendPolylinePoint(points, normals, curvePoint, curveNormal);
                }
            }

            return points.Count >= 3;
        }

        private bool TryBuildSideBoundaryPolyline(int sideIndex, List<Vector3> points, List<Vector3> normals)
        {
            points.Clear();
            normals.Clear();

            if (!TryGetJunctionPolylineContext(out _, out var edgePoints, out var meshOffsets) ||
                sideIndex < 0 ||
                sideIndex >= edgePoints.Count)
            {
                return false;
            }

            int nextIndex = (sideIndex + 1) % edgePoints.Count;
            var currentEdge = edgePoints[sideIndex];
            var nextEdge = edgePoints[nextIndex];
            float currentMeshOffset = meshOffsets[sideIndex];
            float nextMeshOffset = meshOffsets[nextIndex];

            AppendPolylinePoint(
                points,
                normals,
                currentEdge.point0SidewalkExtended + Vector3.up * currentMeshOffset,
                currentEdge.normal0);
            AppendPolylinePoint(
                points,
                normals,
                currentEdge.point1SidewalkExtended + Vector3.up * currentMeshOffset,
                currentEdge.normal1);

            var curvePoints = GenerateCurvePointsWorld(
                currentEdge.point1SidewalkExtended,
                nextEdge.point0SidewalkExtended,
                currentEdge.normal1,
                nextEdge.normal0);

            for (int j = 1; j < curvePoints.Count; j++)
            {
                float t = j / (float)(curvePoints.Count - 1);
                Vector3 curvePoint = curvePoints[j] + Vector3.up * Mathf.Lerp(currentMeshOffset, nextMeshOffset, t);
                Vector3 curveNormal = Vector3.Slerp(currentEdge.normal1, nextEdge.normal0, t).normalized;
                AppendPolylinePoint(points, normals, curvePoint, curveNormal);
            }

            return points.Count >= 2;
        }

        private bool TryGetJunctionPolylineContext(
            out List<ConnectedRoad> validConnectedRoads,
            out List<EdgePoints> edgePoints,
            out List<float> meshOffsets)
        {
            Vector3 center = GetJunctionCenter();
            if (!TryCollectValidConnectedRoadData(center, out validConnectedRoads, out edgePoints, out meshOffsets) ||
                validConnectedRoads.Count < 2)
            {
                validConnectedRoads = null;
                edgePoints = null;
                meshOffsets = null;
                return false;
            }

            return true;
        }

        private static void AppendPolylinePoint(List<Vector3> points, List<Vector3> normals, Vector3 point, Vector3 normal)
        {
            const float duplicateThreshold = 0.0001f;
            if (points.Count > 0 && Vector3.Distance(points[points.Count - 1], point) <= duplicateThreshold)
            {
                normals[normals.Count - 1] = normal.sqrMagnitude > 1e-8f ? normal.normalized : normals[normals.Count - 1];
                return;
            }

            points.Add(point);
            normals.Add(normal.sqrMagnitude > 1e-8f ? normal.normalized : Vector3.forward);
        }

        private List<Vector3> GenerateCurvePointsWorld(Vector3 start, Vector3 end, Vector3 startNormal, Vector3 endNormal)
        {
            var points = new List<Vector3>();
            float curveStrength = curveParameter * Vector3.Distance(start, end);
            Vector3 startControl = start + startNormal * curveStrength;
            Vector3 endControl = end + endNormal * curveStrength;

            for (int i = 0; i <= curveSegments; i++)
            {
                float t = i / (float)curveSegments;
                points.Add(BezierPoint(start, startControl, endControl, end, t));
            }

            return points;
        }
    }
}
#endif 
