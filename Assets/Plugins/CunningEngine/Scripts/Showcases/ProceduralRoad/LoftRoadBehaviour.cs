#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Splines;
using UnityEditor.Splines.Extension;
using UnityEditor.SceneManagement;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.Rendering.HighDefinition;
using Interpolators = UnityEngine.Splines.Interpolators;
using Unity.Collections;
using Sirenix.OdinInspector;
using System.Collections;
using CunningEngine;

//该脚本仅生成mesh和数据结构，同步数据内容在编辑器脚本中

namespace Unity.Splines.Examples
{
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SplineContainer), typeof(MeshRenderer), typeof(MeshFilter))]
    public partial class LoftRoadBehaviour : MonoBehaviour
    {
        [SerializeField]
        [HideInInspector]
        List<SplineData<float>> m_Widths = new List<SplineData<float>>();

        [SerializeField]
        [OnValueChanged("LoftAllRoads", true)]
        [LabelText("道路数据")]
        [ListDrawerSettings(ShowIndexLabels = true, ShowPaging = true, ShowItemCount = true)]
        private List<LoftRoadExtensionData> m_RoadDatas = new List<LoftRoadExtensionData>();

        [SerializeField]
        [LabelText("绿化带偏移")]
        float m_GreenBeltOffset = 0.5f;

        public static class SimpleInterpolators
        {
            public static float LerpFloat(float start, float end, float t)
            {
                return Mathf.Lerp(start, end, t);
            }
        }
        public static class CustomInterpolators
        {
            public static float LerpFloat(float start, float end, float t)
            {
                return Mathf.Lerp(start, end, t);
            }
        }
        public List<SplineData<float>> Widths
        {
            get
            {
                for (int i = 0; i < m_RoadDatas.Count; i++)
                {
                    if (m_Widths.Count <= i)
                        m_Widths.Add(m_RoadDatas[i].width);
                    else
                        m_Widths[i] = m_RoadDatas[i].width;

                    if (m_Widths[i].DefaultValue == 0)
                        m_Widths[i].DefaultValue = 1f;
                }
                return m_Widths;
            }
        }

        [LabelText("道路数据")]
        public List<LoftRoadExtensionData> RoadExtensionDatas
        {
            get
            {
                foreach (var data in m_RoadDatas)
                {
                    data.Cook();
                }
                return m_RoadDatas;
            }
        }

        [SerializeField]
        [LabelText("样条容器")]
        SplineContainer m_Spline;

        public SplineContainer Container
        {
            get
            {
                if (this == null) return null;
                
                if (m_Spline == null)
                    m_Spline = GetComponent<SplineContainer>();

                return m_Spline;
            }
            set => m_Spline = value;
        }

        [SerializeField]
        [LabelText("段单位长度")]
        float m_SegmentUnitLength = 15f;

        [SerializeField]
        [LabelText("道路网格")]
        Mesh m_Mesh;

        [SerializeField]
        [HideInInspector]
        private CunningRoadGeometryHandle m_CunningGeometryHandle;

        [SerializeField]
        [HideInInspector]
        private CunningMesh m_CunningMesh;

        [SerializeField]
        [HideInInspector]
        private GameObject m_RoadMainObject;

        [SerializeField]
        [HideInInspector]
        private GameObject m_SidewalkBundleObject;

        [SerializeField]
        [HideInInspector]
        private CunningRoadGeometryHandle m_SidewalkGeometryHandle;

        [SerializeField]
        [HideInInspector]
        private CunningMesh m_SidewalkCunningMesh;

        [SerializeField]
        [HideInInspector]
        private GameObject m_GreenbeltBundleObject;

        [SerializeField]
        [HideInInspector]
        private CunningRoadGeometryHandle m_GreenbeltGeometryHandle;

        [SerializeField]
        [HideInInspector]
        private CunningMesh m_GreenbeltCunningMesh;

        [NonSerialized]
        private float[] m_CunningFlatPositionsBuffer = Array.Empty<float>();

        [NonSerialized]
        private float[] m_CunningFlatNormalsBuffer = Array.Empty<float>();

        [NonSerialized]
        private float[] m_CunningFlatUvBuffer = Array.Empty<float>();

        [NonSerialized]
        private ulong[] m_CunningPointIdBuffer = Array.Empty<ulong>();

        [NonSerialized]
        private ulong[] m_CunningPolyPointIdBuffer = Array.Empty<ulong>();

        [NonSerialized]
        private uint[] m_CunningPolyOffsetBuffer = Array.Empty<uint>();

        [NonSerialized]
        private int[] m_CunningPrimitiveMaterialIndexBuffer = Array.Empty<int>();

        [NonSerialized]
        private int[] m_CunningPrimitiveAreaIndexBuffer = Array.Empty<int>();

        [NonSerialized]
        private int[] m_CunningPrimitiveRoadIdBuffer = Array.Empty<int>();

        [NonSerialized]
        private int[] m_CunningPrimitiveSegmentIdBuffer = Array.Empty<int>();

        [NonSerialized]
        private int[] m_CunningPrimitiveSourceStripIndexBuffer = Array.Empty<int>();

        [NonSerialized]
        private int[] m_CunningPrimitiveSourceSideKindBuffer = Array.Empty<int>();

        [NonSerialized]
        private uint[] m_CunningDirtyRangeStartBuffer = Array.Empty<uint>();

        [NonSerialized]
        private uint[] m_CunningDirtyRangeCountBuffer = Array.Empty<uint>();

        [NonSerialized]
        private readonly HashSet<int> m_SynchronousSplineRefreshIndices = new HashSet<int>();

        private static readonly bool s_EnableRoadHotPathDiagnostics = false;

        [SerializeField]
        [LabelText("纹理缩放")]
        float m_TextureScale = 1f;

        [SerializeField]
        [LabelText("绿化带网格")]
        Mesh m_GreenBeltMesh;

        [SerializeField]
        [LabelText("左侧人行道网格")]
        Mesh m_SidewalkLeftMesh;

        [SerializeField]
        [LabelText("右侧人行道网格")]
        Mesh m_SidewalkRightMesh;

        // 添加路缘网格
        [SerializeField]
        Mesh m_CurbLeftMesh;
        
        [SerializeField]
        Mesh m_CurbRightMesh;

        [LabelText("显示道路标记")]
        public bool m_ShowRoadMarkers = true;

        // 添加Lane Gizmo显示控制
        private static bool s_ShowLaneGizmos = true;
        public static bool ShowLaneGizmos
        {
            get { return s_ShowLaneGizmos; }
            set { s_ShowLaneGizmos = value; }
        }

        // 添加Lane中心线显示控制
        private static bool s_ShowLaneCenterLines = true;
        public static bool ShowLaneCenterLines
        {
            get { return s_ShowLaneCenterLines; }
            set { s_ShowLaneCenterLines = value; }
        }

        // 添加Lane边界线显示控制
        private static bool s_ShowLaneBoundaryLines = false; // 修改默认值为false
        public static bool ShowLaneBoundaryLines
        {
            get { return s_ShowLaneBoundaryLines; }
            set { s_ShowLaneBoundaryLines = value; }
        }

        // 添加Lane标签显示控制
        private static bool s_ShowLaneLabels = true;
        public static bool ShowLaneLabels
        {
            get { return s_ShowLaneLabels; }
            set { s_ShowLaneLabels = value; }
        }

        public IReadOnlyList<Spline> splines => LoftSplines;

        public IReadOnlyList<Spline> LoftSplines
        {
            get
            {
                if (Container == null)
                    return null;

                return Container.Splines;
            }
        }

        public Mesh LoftMesh
        {
            get
            {
                if (m_Mesh == null)
                    m_Mesh = new Mesh();
                return m_Mesh;
            }
        }

        public Mesh GreenBeltMesh
        {
            get
            {
                if (m_GreenBeltMesh == null)
                    m_GreenBeltMesh = new Mesh();
                return m_GreenBeltMesh;
            }
        }

        public Mesh SidewalkLeftMesh
        {
            get
            {
                if (m_SidewalkLeftMesh == null)
                    m_SidewalkLeftMesh = new Mesh();
                return m_SidewalkLeftMesh;
            }
        }

        public Mesh SidewalkRightMesh
        {
            get
            {
                if (m_SidewalkRightMesh == null)
                    m_SidewalkRightMesh = new Mesh();
                return m_SidewalkRightMesh;
            }
        }
        
        // 添加路缘网格属性
        public Mesh CurbLeftMesh
        {
            get
            {
                if (m_CurbLeftMesh == null)
                    m_CurbLeftMesh = new Mesh();
                return m_CurbLeftMesh;
            }
        }
        
        public Mesh CurbRightMesh
        {
            get
            {
                if (m_CurbRightMesh == null)
                    m_CurbRightMesh = new Mesh();
                return m_CurbRightMesh;
            }
        }

        // 添加马路牙子网格属性
        [SerializeField]
        Mesh m_RoadEdgeLeftMesh;
        
        [SerializeField]
        Mesh m_RoadEdgeRightMesh;

        public bool ShowRoadMarkers
        {
            get => m_ShowRoadMarkers;
            set => m_ShowRoadMarkers = value;
        }

        public float SegmentUnitLength => Mathf.Max(1f, m_SegmentUnitLength);

        List<Vector3> m_Positions = new List<Vector3>();
        List<Vector3> m_Normals = new List<Vector3>();
        List<Vector2> m_Textures = new List<Vector2>();
        List<int> m_Indices = new List<int>();

        private sealed class CunningRoadStripRange
        {
            public int vertexStart;
            public int vertexCount;
            public int splineIndex;
            public int localSegmentIndex;
            public int segmentId;
            public Material material;
        }

        private enum CunningSideStripSource
        {
            SidewalkLeft,
            SidewalkRight,
            CurbLeft,
            CurbRight,
            RoadEdgeLeft,
            RoadEdgeRight,
            RoadEdgeOuterLeft,
            RoadEdgeOuterRight,
        }

        private enum CunningAreaType
        {
            RoadSurface = 0,
            Sidewalk = 1,
            Curb = 2,
            RoadEdge = 3,
            RoadEdgeOuter = 4,
            Greenbelt = 5,
        }

        [Flags]
        private enum CunningGeometryDomainMask
        {
            None = 0,
            RoadMain = 1 << 0,
            SidewalkBundle = 1 << 1,
            GreenbeltBundle = 1 << 2,
            All = RoadMain | SidewalkBundle | GreenbeltBundle,
        }

        private sealed class CunningSideStripRange
        {
            public CunningSideStripSource source;
            public int vertexStart;
            public int vertexCount;
            public int splineIndex;
            public int localSegmentIndex;
            public int segmentId;
            public Material material;
            public CunningAreaType areaType;
            public int ringSize;
        }

        private sealed class DomainMaterialPalette
        {
            public readonly List<Material> materials = new List<Material>();

            public int GetSlot(Material material)
            {
                int index = materials.IndexOf(material);
                if (index >= 0)
                {
                    return index;
                }

                materials.Add(material);
                return materials.Count - 1;
            }

            public void Clear()
            {
                materials.Clear();
            }
        }

        private sealed class DomainGeometryData
        {
            public readonly List<Vector3> positions = new List<Vector3>();
            public readonly List<int> primitivePointIndices = new List<int>();
            public readonly List<Vector3> vertexNormals = new List<Vector3>();
            public readonly List<Vector2> vertexUv = new List<Vector2>();
            public readonly List<int> primitiveMaterialIndices = new List<int>();
            public readonly List<int> primitiveAreaIndices = new List<int>();
            public readonly List<int> primitiveRoadIds = new List<int>();
            public readonly List<int> primitiveSegmentIds = new List<int>();
            public readonly List<int> primitiveSourceStripIndices = new List<int>();
            public readonly List<int> primitiveSourceSideKinds = new List<int>();
            public readonly List<uint> primitiveOffsets = new List<uint> { 0u };
            public readonly DomainMaterialPalette materialPalette = new DomainMaterialPalette();
            public ulong topologyFingerprint;
            public ulong contentFingerprint;

            public int PrimitiveCount => primitiveMaterialIndices.Count;
            public bool HasGeometry => positions.Count > 0 && primitivePointIndices.Count > 0 && PrimitiveCount > 0;

            public void Clear(bool clearMaterialPalette = true)
            {
                positions.Clear();
                primitivePointIndices.Clear();
                vertexNormals.Clear();
                vertexUv.Clear();
                primitiveMaterialIndices.Clear();
                primitiveAreaIndices.Clear();
                primitiveRoadIds.Clear();
                primitiveSegmentIds.Clear();
                primitiveSourceStripIndices.Clear();
                primitiveSourceSideKinds.Clear();
                primitiveOffsets.Clear();
                primitiveOffsets.Add(0u);
                if (clearMaterialPalette)
                {
                    materialPalette.Clear();
                }

                topologyFingerprint = 0UL;
                contentFingerprint = 0UL;
            }
        }

        private sealed class PackedDomainBuffers
        {
            public float[] positions = Array.Empty<float>();
            public int[] primitivePointIndices = Array.Empty<int>();
            public uint[] primitiveOffsets = Array.Empty<uint>();
            public float[] vertexNormals = Array.Empty<float>();
            public float[] vertexUv = Array.Empty<float>();
            public int[] primitiveMaterialIndices = Array.Empty<int>();
            public int[] primitiveAreaIndices = Array.Empty<int>();
            public int[] primitiveRoadIds = Array.Empty<int>();
            public int[] primitiveSegmentIds = Array.Empty<int>();
            public Material[] renderMaterials = Array.Empty<Material>();

            public int PointCount => positions.Length / 3;
            public int VertexCount => vertexNormals.Length / 3;
            public int PrimitiveCount => primitiveMaterialIndices.Length;
            public bool HasTopology => positions.Length > 0 && primitivePointIndices.Length > 0 && primitiveOffsets.Length > 1;

            public void Clear()
            {
                positions = Array.Empty<float>();
                primitivePointIndices = Array.Empty<int>();
                primitiveOffsets = Array.Empty<uint>();
                vertexNormals = Array.Empty<float>();
                vertexUv = Array.Empty<float>();
                primitiveMaterialIndices = Array.Empty<int>();
                primitiveAreaIndices = Array.Empty<int>();
                primitiveRoadIds = Array.Empty<int>();
                primitiveSegmentIds = Array.Empty<int>();
                renderMaterials = Array.Empty<Material>();
            }
        }

        private sealed class SplineDomainContributionCache
        {
            public readonly DomainGeometryData roadMain = new DomainGeometryData();
            public readonly DomainGeometryData sidewalkBundle = new DomainGeometryData();
            public readonly DomainGeometryData greenbeltBundle = new DomainGeometryData();

            public DomainGeometryData Get(CunningGeometryDomainMask domain)
            {
                switch (domain)
                {
                    case CunningGeometryDomainMask.RoadMain:
                        return roadMain;
                    case CunningGeometryDomainMask.SidewalkBundle:
                        return sidewalkBundle;
                    case CunningGeometryDomainMask.GreenbeltBundle:
                        return greenbeltBundle;
                    default:
                        return null;
                }
            }
        }

        private sealed class SegmentDomainContributionCache
        {
            public SegmentSlotKey key;
            public int segmentId;
            public readonly DomainGeometryData roadMain = new DomainGeometryData();
            public readonly DomainGeometryData sidewalkBundle = new DomainGeometryData();
            public readonly DomainGeometryData greenbeltBundle = new DomainGeometryData();

            public DomainGeometryData Get(CunningGeometryDomainMask domain)
            {
                switch (domain)
                {
                    case CunningGeometryDomainMask.RoadMain:
                        return roadMain;
                    case CunningGeometryDomainMask.SidewalkBundle:
                        return sidewalkBundle;
                    case CunningGeometryDomainMask.GreenbeltBundle:
                        return greenbeltBundle;
                    default:
                        return null;
                }
            }

            public void Clear()
            {
                roadMain.Clear();
                sidewalkBundle.Clear();
                greenbeltBundle.Clear();
            }
        }

        private sealed class DomainAssemblySlot
        {
            public SegmentSlotKey key;
            public int segmentId;
            public bool hasGeometry;
            public int pointStart;
            public int pointCount;
            public int vertexStart;
            public int vertexCount;
            public int primitiveStart;
            public int primitiveCount;
            public ulong topologyFingerprint;
            public ulong contentFingerprint;

            public void Reset()
            {
                key = default;
                segmentId = 0;
                hasGeometry = false;
                pointStart = 0;
                pointCount = 0;
                vertexStart = 0;
                vertexCount = 0;
                primitiveStart = 0;
                primitiveCount = 0;
                topologyFingerprint = 0UL;
                contentFingerprint = 0UL;
            }
        }

        private readonly struct DirtyRange
        {
            public readonly int start;
            public readonly int count;

            public DirtyRange(int start, int count)
            {
                this.start = start;
                this.count = count;
            }
        }

        private sealed class DomainAssemblyCache
        {
            public readonly DomainGeometryData assembled = new DomainGeometryData();
            public readonly PackedDomainBuffers packed = new PackedDomainBuffers();
            public readonly List<DomainAssemblySlot> slots = new List<DomainAssemblySlot>();
            public readonly List<SegmentSlotKey> orderedKeys = new List<SegmentSlotKey>();
            public readonly List<DirtyRange> pointDirtyRanges = new List<DirtyRange>();
            public readonly List<DirtyRange> vertexDirtyRanges = new List<DirtyRange>();
            public readonly List<DirtyRange> primitiveDirtyRanges = new List<DirtyRange>();
            public bool isBuilt;
            public bool needsFullUpload = true;
            public bool hasContentChanges;
            public bool renderMaterialsChanged;

            public void EnsureSlotCapacity(int slotCount)
            {
                while (slots.Count < slotCount)
                {
                    slots.Add(new DomainAssemblySlot());
                }

                if (slots.Count > slotCount)
                {
                    slots.RemoveRange(slotCount, slots.Count - slotCount);
                    if (orderedKeys.Count > slotCount)
                    {
                        orderedKeys.RemoveRange(slotCount, orderedKeys.Count - slotCount);
                    }
                    isBuilt = false;
                    needsFullUpload = true;
                }
            }

            public void ClearDirtyRanges()
            {
                pointDirtyRanges.Clear();
                vertexDirtyRanges.Clear();
                primitiveDirtyRanges.Clear();
                hasContentChanges = false;
                renderMaterialsChanged = false;
            }
        }

        private sealed class BulkExtrudeStripDescriptor
        {
            public CunningSideStripRange strip;
            public List<Vector3> sourcePositions;
            public List<Vector2> sourceTextures;
            public Vector3 origin;
            public Vector3 alongAxis;
            public float uvScaleU;
            public float uvScaleV;
            public float uvOffsetU;
            public float extrudeDistance;
            public float extrudeInset;
            public int sourceStripIndex;
            public float sourceAlongStartDistance;
            public float sourceAlongEndDistance;
            public StripUvFrame uvFrame;
        }

        private readonly struct StripUvFrame
        {
            public readonly bool alongInU;
            public readonly bool flip;
            public readonly float alongStart;
            public readonly float alongEnd;
            public readonly float sideMinAcross;
            public readonly float topInnerAcross;
            public readonly float topOuterAcross;
            public readonly float sideMaxAcross;

            public StripUvFrame(
                bool alongInU,
                bool flip,
                float alongStart,
                float alongEnd,
                float sideMinAcross,
                float topInnerAcross,
                float topOuterAcross,
                float sideMaxAcross)
            {
                this.alongInU = alongInU;
                this.flip = flip;
                this.alongStart = alongStart;
                this.alongEnd = alongEnd;
                this.sideMinAcross = sideMinAcross;
                this.topInnerAcross = topInnerAcross;
                this.topOuterAcross = topOuterAcross;
                this.sideMaxAcross = sideMaxAcross;
            }
        }

        private sealed class SplineGeometryCache
        {
            public readonly List<Vector3> roadPositions = new List<Vector3>();
            public readonly List<Vector3> roadNormals = new List<Vector3>();
            public readonly List<Vector2> roadTextures = new List<Vector2>();
            public readonly List<int> roadIndices = new List<int>();
            public readonly List<CunningRoadStripRange> roadStripRanges = new List<CunningRoadStripRange>();
            public readonly List<CunningSideStripRange> sideStripRanges = new List<CunningSideStripRange>();

            public readonly List<Vector3> sidewalkLeftPositions = new List<Vector3>();
            public readonly List<Vector3> sidewalkLeftNormals = new List<Vector3>();
            public readonly List<Vector2> sidewalkLeftTextures = new List<Vector2>();
            public readonly List<int> sidewalkLeftIndices = new List<int>();

            public readonly List<Vector3> sidewalkRightPositions = new List<Vector3>();
            public readonly List<Vector3> sidewalkRightNormals = new List<Vector3>();
            public readonly List<Vector2> sidewalkRightTextures = new List<Vector2>();
            public readonly List<int> sidewalkRightIndices = new List<int>();

            public readonly List<Vector3> curbLeftPositions = new List<Vector3>();
            public readonly List<Vector3> curbLeftNormals = new List<Vector3>();
            public readonly List<Vector2> curbLeftTextures = new List<Vector2>();
            public readonly List<int> curbLeftIndices = new List<int>();

            public readonly List<Vector3> curbRightPositions = new List<Vector3>();
            public readonly List<Vector3> curbRightNormals = new List<Vector3>();
            public readonly List<Vector2> curbRightTextures = new List<Vector2>();
            public readonly List<int> curbRightIndices = new List<int>();

            public readonly List<Vector3> roadEdgeLeftPositions = new List<Vector3>();
            public readonly List<Vector3> roadEdgeLeftNormals = new List<Vector3>();
            public readonly List<Vector2> roadEdgeLeftTextures = new List<Vector2>();
            public readonly List<int> roadEdgeLeftIndices = new List<int>();

            public readonly List<Vector3> roadEdgeRightPositions = new List<Vector3>();
            public readonly List<Vector3> roadEdgeRightNormals = new List<Vector3>();
            public readonly List<Vector2> roadEdgeRightTextures = new List<Vector2>();
            public readonly List<int> roadEdgeRightIndices = new List<int>();

            public readonly List<Vector3> roadEdgeOuterLeftPositions = new List<Vector3>();
            public readonly List<Vector3> roadEdgeOuterLeftNormals = new List<Vector3>();
            public readonly List<Vector2> roadEdgeOuterLeftTextures = new List<Vector2>();
            public readonly List<int> roadEdgeOuterLeftIndices = new List<int>();

            public readonly List<Vector3> roadEdgeOuterRightPositions = new List<Vector3>();
            public readonly List<Vector3> roadEdgeOuterRightNormals = new List<Vector3>();
            public readonly List<Vector2> roadEdgeOuterRightTextures = new List<Vector2>();
            public readonly List<int> roadEdgeOuterRightIndices = new List<int>();

            public bool contributesRoadMain;
            public bool contributesGreenbelt;
            public bool contributesSidewalk;
            public ulong inputFingerprint;
            public bool isBuilt;

            public void Clear()
            {
                roadPositions.Clear();
                roadNormals.Clear();
                roadTextures.Clear();
                roadIndices.Clear();
                roadStripRanges.Clear();
                sideStripRanges.Clear();

                sidewalkLeftPositions.Clear();
                sidewalkLeftNormals.Clear();
                sidewalkLeftTextures.Clear();
                sidewalkLeftIndices.Clear();

                sidewalkRightPositions.Clear();
                sidewalkRightNormals.Clear();
                sidewalkRightTextures.Clear();
                sidewalkRightIndices.Clear();

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

                roadEdgeOuterLeftPositions.Clear();
                roadEdgeOuterLeftNormals.Clear();
                roadEdgeOuterLeftTextures.Clear();
                roadEdgeOuterLeftIndices.Clear();

                roadEdgeOuterRightPositions.Clear();
                roadEdgeOuterRightNormals.Clear();
                roadEdgeOuterRightTextures.Clear();
                roadEdgeOuterRightIndices.Clear();

                contributesRoadMain = false;
                contributesGreenbelt = false;
                contributesSidewalk = false;
                isBuilt = false;
            }
        }

#if UNITY_EDITOR
        private static class DeferredDownstreamRefreshScheduler
        {
            private static readonly HashSet<LoftRoadBehaviour> s_PendingRoads = new HashSet<LoftRoadBehaviour>();
            private static bool s_Registered;

            public static void Enqueue(LoftRoadBehaviour road)
            {
                if (road == null)
                {
                    return;
                }

                s_PendingRoads.Add(road);
                if (s_Registered)
                {
                    return;
                }

                EditorApplication.update += Flush;
                s_Registered = true;
            }

            public static void Remove(LoftRoadBehaviour road)
            {
                if (road != null)
                {
                    s_PendingRoads.Remove(road);
                }

                if (s_Registered && s_PendingRoads.Count == 0)
                {
                    EditorApplication.update -= Flush;
                    s_Registered = false;
                }
            }

            private static void Flush()
            {
                if (s_PendingRoads.Count == 0)
                {
                    if (s_Registered)
                    {
                        EditorApplication.update -= Flush;
                        s_Registered = false;
                    }
                    return;
                }

                LoftRoadBehaviour[] pending = s_PendingRoads.ToArray();
                s_PendingRoads.Clear();
                for (int index = 0; index < pending.Length; index++)
                {
                    LoftRoadBehaviour road = pending[index];
                    if (road == null)
                    {
                        continue;
                    }

                    road.FlushDeferredDownstreamRefresh();
                }
            }
        }
#endif

        List<CunningRoadStripRange> m_CunningRoadStripRanges = new List<CunningRoadStripRange>();
        List<CunningSideStripRange> m_CunningSideStripRanges = new List<CunningSideStripRange>();
        readonly List<SplineGeometryCache> m_SplineGeometryCaches = new List<SplineGeometryCache>();
        readonly List<SplineDomainContributionCache> m_SplineDomainContributionCaches = new List<SplineDomainContributionCache>();
        readonly List<List<SegmentDomainContributionCache>> m_SegmentDomainContributionCaches = new List<List<SegmentDomainContributionCache>>();
        readonly HashSet<int> m_DirtySplineIndices = new HashSet<int>();
        readonly HashSet<int> m_DirtyDownstreamSplineIndices = new HashSet<int>();
        readonly List<int> m_LastChangedSplineIndices = new List<int>();
        readonly List<int> m_DownstreamRefreshSplineIndices = new List<int>();
        CunningGeometryDomainMask m_LastChangedDomainMask = CunningGeometryDomainMask.All;
        readonly HashSet<JunctionData> m_DirtyJunctions = new HashSet<JunctionData>();
        bool m_UsePreciseDirtyJunctionSelection = false;
        bool m_ForceFullSplineRebuild = true;
        bool m_HasBuiltSplineGeometryCaches = false;
        static int s_SuppressConnectedJunctionUpdatesDepth = 0;
        readonly DomainAssemblyCache m_RoadMainAssemblyCache = new DomainAssemblyCache();
        readonly DomainAssemblyCache m_SidewalkBundleAssemblyCache = new DomainAssemblyCache();
        readonly DomainAssemblyCache m_GreenbeltAssemblyCache = new DomainAssemblyCache();

        List<Vector3> m_GreenBeltPositions = new List<Vector3>();
        List<Vector3> m_GreenBeltNormals = new List<Vector3>();
        List<Vector2> m_GreenBeltTextures = new List<Vector2>();
        List<int> m_GreenBeltIndices = new List<int>();

        List<Vector3> m_SidewalkLeftPositions = new List<Vector3>();
        List<Vector3> m_SidewalkLeftNormals = new List<Vector3>();
        List<Vector2> m_SidewalkLeftTextures = new List<Vector2>();
        List<int> m_SidewalkLeftIndices = new List<int>();

        List<Vector3> m_SidewalkRightPositions = new List<Vector3>();
        List<Vector3> m_SidewalkRightNormals = new List<Vector3>();
        List<Vector2> m_SidewalkRightTextures = new List<Vector2>();
        List<int> m_SidewalkRightIndices = new List<int>();
        
        // 添加路缘网格数据列表
        List<Vector3> m_CurbLeftPositions = new List<Vector3>();
        List<Vector3> m_CurbLeftNormals = new List<Vector3>();
        List<Vector2> m_CurbLeftTextures = new List<Vector2>();
        List<int> m_CurbLeftIndices = new List<int>();
        
        List<Vector3> m_CurbRightPositions = new List<Vector3>();
        List<Vector3> m_CurbRightNormals = new List<Vector3>();
        List<Vector2> m_CurbRightTextures = new List<Vector2>();
        List<int> m_CurbRightIndices = new List<int>();

        // 添加马路牙子网格列表
        List<Vector3> m_RoadEdgeLeftPositions = new List<Vector3>();
        List<Vector3> m_RoadEdgeLeftNormals = new List<Vector3>();
        List<Vector2> m_RoadEdgeLeftTextures = new List<Vector2>();
        List<int> m_RoadEdgeLeftIndices = new List<int>();
        
        List<Vector3> m_RoadEdgeRightPositions = new List<Vector3>();
        List<Vector3> m_RoadEdgeRightNormals = new List<Vector3>();
        List<Vector2> m_RoadEdgeRightTextures = new List<Vector2>();
        List<int> m_RoadEdgeRightIndices = new List<int>();
        
        // 人行道外侧马路牙子的网格列表
        List<Vector3> m_RoadEdgeOuterLeftPositions = new List<Vector3>();
        List<Vector3> m_RoadEdgeOuterLeftNormals = new List<Vector3>();
        List<Vector2> m_RoadEdgeOuterLeftTextures = new List<Vector2>();
        List<int> m_RoadEdgeOuterLeftIndices = new List<int>();
        
        List<Vector3> m_RoadEdgeOuterRightPositions = new List<Vector3>();
        List<Vector3> m_RoadEdgeOuterRightNormals = new List<Vector3>();
        List<Vector2> m_RoadEdgeOuterRightTextures = new List<Vector2>();
        List<int> m_RoadEdgeOuterRightIndices = new List<int>();

        private bool initialized = false;
        private int m_CurrentProcessingSplineIndex = 0;

        public List<JunctionData> connectedJunctions = new List<JunctionData>();

        [SerializeField]
        [LabelText("采样点数据")]
        public List<RoadSamplePoint> samplePoints = new List<RoadSamplePoint>();

        [SerializeField]
        [LabelText("连接数据")]
        public List<RoadConnection> connections = new List<RoadConnection>();

        // 车道Spline相关
        [SerializeField]
        [HideInInspector]
        private GameObject m_LanesContainer; // 车道容器对象

        [SerializeField]
        [HideInInspector]
        private List<SplineContainer> m_LaneSplines = new List<SplineContainer>(); // 车道中线的SplineContainer列表

        // 车道容器对象的访问器
        public GameObject LanesContainer
        {
            get
            {
                if (m_LanesContainer == null)
                {
                    CreateLanesContainer();
                }
                return m_LanesContainer;
            }
        }

        // 车道Spline列表的访问器
        public List<SplineContainer> LaneSplines => m_LaneSplines;
                
        [FoldoutGroup("算法参数")]
        [SerializeField]
        public RoadAlgorithmParameters algorithmParameters = new RoadAlgorithmParameters();

        public void OnEnable()
        {
            InitializeMeshes();

            if (m_Spline == null)
                m_Spline = GetComponent<SplineContainer>();

            // 确保车道容器已创建
            CreateLanesContainer();

            // 加载并设置道路材质
            Material roadMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/ArtResources/PcgAsset/Materials/PcgToolsMat/im_road_001.mat");
            if (roadMaterial != null)
            {
                GetComponent<MeshRenderer>().sharedMaterial = roadMaterial;
            }
            else
            {
                Debug.LogError("无法加载道路材质");
            }

            // 初始化道路数据
            if (m_RoadDatas == null || m_RoadDatas.Count == 0)
            {
                m_RoadDatas.Add(CreateDefaultRoadData(RoadType.Road_A));
            }

            EnsureRoadId();
            EnsureAllJunctionMarkersMigrated();
            RebuildAllSplineSemanticCaches();
            EnsureCunningRoadComponents();
            ResetCunningRuntimeState();
            LoftAllRoads();

#if UNITY_EDITOR

            EditorSplineUtility.AfterSplineWasModified += OnAfterSplineWasModified;
            EditorSplineUtility.RegisterSplineDataChanged<float>(OnAfterSplineDataWasModified);
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
#endif

            SplineContainer.SplineAdded += OnSplineContainerAdded;
            SplineContainer.SplineRemoved += OnSplineContainerRemoved;
            SplineContainer.SplineReordered += OnSplineContainerReordered;
            Spline.Changed += OnSplineChanged;
        }

        public void OnDisable()
        {
#if UNITY_EDITOR
            EditorSplineUtility.AfterSplineWasModified -= OnAfterSplineWasModified;
            EditorSplineUtility.UnregisterSplineDataChanged<float>(OnAfterSplineDataWasModified);
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            DeferredDownstreamRefreshScheduler.Remove(this);
#endif

            ClearCunningRuntimeState();
            DestroyMeshes();

            SplineContainer.SplineAdded -= OnSplineContainerAdded;
            SplineContainer.SplineRemoved -= OnSplineContainerRemoved;
            SplineContainer.SplineReordered -= OnSplineContainerReordered;
            Spline.Changed -= OnSplineChanged;
        }

        private void ResetCunningRuntimeState()
        {
            ResetCunningDomainRuntimeState(m_CunningGeometryHandle, m_CunningMesh);
            ResetCunningDomainRuntimeState(m_SidewalkGeometryHandle, m_SidewalkCunningMesh);
            ResetCunningDomainRuntimeState(m_GreenbeltGeometryHandle, m_GreenbeltCunningMesh);

            ResetDomainAssemblyCache(m_RoadMainAssemblyCache);
            ResetDomainAssemblyCache(m_SidewalkBundleAssemblyCache);
            ResetDomainAssemblyCache(m_GreenbeltAssemblyCache);

            m_DirtySplineIndices.Clear();
            m_DirtyDownstreamSplineIndices.Clear();
            m_LastChangedSplineIndices.Clear();
            m_DownstreamRefreshSplineIndices.Clear();
            m_LastChangedDomainMask = CunningGeometryDomainMask.All;
            m_ForceFullSplineRebuild = true;
            m_HasBuiltSplineGeometryCaches = false;

            for (int splineIndex = 0; splineIndex < m_SplineGeometryCaches.Count; splineIndex++)
            {
                m_SplineGeometryCaches[splineIndex]?.Clear();
            }

            for (int splineIndex = 0; splineIndex < m_SplineDomainContributionCaches.Count; splineIndex++)
            {
                SplineDomainContributionCache contributionCache = m_SplineDomainContributionCaches[splineIndex];
                contributionCache?.roadMain.Clear();
                contributionCache?.sidewalkBundle.Clear();
                contributionCache?.greenbeltBundle.Clear();
            }

            for (int splineIndex = 0; splineIndex < m_SegmentDomainContributionCaches.Count; splineIndex++)
            {
                List<SegmentDomainContributionCache> segmentCaches = m_SegmentDomainContributionCaches[splineIndex];
                if (segmentCaches == null)
                {
                    continue;
                }

                for (int segmentIndex = 0; segmentIndex < segmentCaches.Count; segmentIndex++)
                {
                    segmentCaches[segmentIndex]?.Clear();
                }

                segmentCaches.Clear();
            }

            m_SplineSemanticCaches.Clear();
        }

        private void ClearCunningRuntimeState()
        {
            ResetCunningDomainRuntimeState(m_CunningGeometryHandle, m_CunningMesh);
            ResetCunningDomainRuntimeState(m_SidewalkGeometryHandle, m_SidewalkCunningMesh);
            ResetCunningDomainRuntimeState(m_GreenbeltGeometryHandle, m_GreenbeltCunningMesh);
        }

        private static void ResetCunningDomainRuntimeState(CunningRoadGeometryHandle geometryHandle, CunningMesh cunningMesh)
        {
            if (cunningMesh != null)
            {
                cunningMesh.LoadFromHandle(0);
            }

            geometryHandle?.Dispose();
        }

        private static void ResetDomainAssemblyCache(DomainAssemblyCache assembly)
        {
            if (assembly == null)
            {
                return;
            }

            assembly.assembled.Clear();
            assembly.slots.Clear();
            assembly.orderedKeys.Clear();
            assembly.isBuilt = false;
            assembly.needsFullUpload = true;
            assembly.ClearDirtyRanges();
        }

        private void OnDestroy()
        {
            // 当道路被销毁时，更新所有连接的Junction
            UpdateConnectedJunctionsForDestroyedRoad();
#if UNITY_EDITOR
            if (!ContinuousSplineImplicitJunctionManager.IsApplyingChanges)
            {
                ContinuousSplineImplicitJunctionManager.NotifyRoadDestroyed(this);
            }
#endif
            
            // 销毁车道Spline
            ClearLaneSplines();
        }
        
        // 清除所有车道Spline
        private void ClearLaneSplines()
        {
            if (m_LaneSplines != null)
            {
                foreach (var laneSpline in m_LaneSplines)
                {
                    if (laneSpline != null)
                    {
#if UNITY_EDITOR
                        DestroyImmediate(laneSpline.gameObject);
#else
                        Destroy(laneSpline.gameObject);
#endif
                    }
                }
                m_LaneSplines.Clear();
            }
            
            // 清除车道容器的所有子对象
            if (m_LanesContainer != null)
            {
                for (int i = m_LanesContainer.transform.childCount - 1; i >= 0; i--)
                {
#if UNITY_EDITOR
                    DestroyImmediate(m_LanesContainer.transform.GetChild(i).gameObject);
#else
                    Destroy(m_LanesContainer.transform.GetChild(i).gameObject);
#endif
                }
            }
        }

        // 当整个道路被销毁时，更新所有连接的Junction
        private void UpdateConnectedJunctionsForDestroyedRoad()
        {
            // 创建一个临时列表，因为我们会修改connectedJunctions
            var junctionsToUpdate = new List<JunctionData>(connectedJunctions);
            
            foreach (var junction in junctionsToUpdate)
            {
                if (junction == null)
                    continue;
                
                bool junctionModified = false;
                
                // 检查并更新Junction中的连接点信息
                for (int i = 0; i < junction.connectedRoads.Count; i++)
                {
                    var connectedRoad = junction.connectedRoads[i];
                    
                    // 如果是被销毁的道路
                    if (connectedRoad.roadBehaviour == this)
                    {
                        // 移除这个连接点
                        junction.connectedRoads.RemoveAt(i);
                        i--; // 调整索引
                        junctionModified = true;
                    }
                }
                
                // 如果Junction的连接点数量小于2，可能需要销毁这个Junction
                if (junction.connectedRoads.Count < 2)
                {
                    Debug.LogWarning("Junction连接点数量不足，可能需要移除: " + junction.name);
                    // 这里可以添加销毁Junction的逻辑，或者让用户手动处理
                }
                else if (junctionModified)
                {
                    // 只有当Junction被修改时才更新网格
                    junction.UpdateJunctionMesh();
                }
                
                // 从Junction的连接列表中移除这个道路
                if (junction.connectedRoads.Any(r => r.roadBehaviour == this))
                {
                    Debug.LogError("仍有连接点引用被销毁的道路，这不应该发生");
                }
            }
            
            // 清空连接的Junction列表
            connectedJunctions.Clear();
        }

        private void InitializeMeshes()
        {
            if (m_Mesh == null)
                m_Mesh = new Mesh();

            if (m_GreenBeltMesh == null)
                m_GreenBeltMesh = new Mesh();

            if (m_SidewalkLeftMesh == null)
                m_SidewalkLeftMesh = new Mesh();

            if (m_SidewalkRightMesh == null)
                m_SidewalkRightMesh = new Mesh();
                
            // 初始化路缘网格
            if (m_CurbLeftMesh == null)
                m_CurbLeftMesh = new Mesh();
                
            if (m_CurbRightMesh == null)
                m_CurbRightMesh = new Mesh();
                
            // 初始化马路牙子网格
            if (m_RoadEdgeLeftMesh == null)
                m_RoadEdgeLeftMesh = new Mesh();
                
            if (m_RoadEdgeRightMesh == null)
                m_RoadEdgeRightMesh = new Mesh();
                
            // 初始化人行道外侧马路牙子网格
            if (m_RoadEdgeOuterLeftMesh == null)
                m_RoadEdgeOuterLeftMesh = new Mesh();
                
            if (m_RoadEdgeOuterRightMesh == null)
                m_RoadEdgeOuterRightMesh = new Mesh();
        }

        private void DestroyMeshes()
        {
            if (m_Mesh != null)
#if UNITY_EDITOR
                DestroyImmediate(m_Mesh);
#else
                Destroy(m_Mesh);
#endif

            if (m_GreenBeltMesh != null)
#if UNITY_EDITOR
                DestroyImmediate(m_GreenBeltMesh);
#else
                Destroy(m_GreenBeltMesh);
#endif

            if (m_SidewalkLeftMesh != null)
#if UNITY_EDITOR
                DestroyImmediate(m_SidewalkLeftMesh);
#else
                Destroy(m_SidewalkLeftMesh);
#endif

            if (m_SidewalkRightMesh != null)
#if UNITY_EDITOR
                DestroyImmediate(m_SidewalkRightMesh);
#else
                Destroy(m_SidewalkRightMesh);
#endif
                
            // 销毁路缘网格
            if (m_CurbLeftMesh != null)
#if UNITY_EDITOR
                DestroyImmediate(m_CurbLeftMesh);
#else
                Destroy(m_CurbLeftMesh);
#endif

            if (m_CurbRightMesh != null)
#if UNITY_EDITOR
                DestroyImmediate(m_CurbRightMesh);
#else
                Destroy(m_CurbRightMesh);
#endif

            // 销毁马路牙子网格
            if (m_RoadEdgeLeftMesh != null)
#if UNITY_EDITOR
                DestroyImmediate(m_RoadEdgeLeftMesh);
#else
                Destroy(m_RoadEdgeLeftMesh);
#endif

            if (m_RoadEdgeRightMesh != null)
#if UNITY_EDITOR
                DestroyImmediate(m_RoadEdgeRightMesh);
#else
                Destroy(m_RoadEdgeRightMesh);
#endif
                
            // 销毁人行道外侧马路牙子网格
            if (m_RoadEdgeOuterLeftMesh != null)
#if UNITY_EDITOR
                DestroyImmediate(m_RoadEdgeOuterLeftMesh);
#else
                Destroy(m_RoadEdgeOuterLeftMesh);
#endif

            if (m_RoadEdgeOuterRightMesh != null)
#if UNITY_EDITOR
                DestroyImmediate(m_RoadEdgeOuterRightMesh);
#else
                Destroy(m_RoadEdgeOuterRightMesh);
#endif
        }

        private void OnSplineContainerAdded(SplineContainer container, int index)
        {
            if (container != m_Spline)
                return;

            SyncRoadExtension(container, index);
            MarkAllSplinesDirty();
            LoftAllRoads();
        }

        private void SyncRoadExtension(SplineContainer container, int index)
        {
            if (container != m_Spline)
                return;

            if (m_RoadDatas.Count < LoftSplines.Count)
            {
                if (m_RoadDatas.Count == 0)
                {
                    m_RoadDatas.Add(CreateDefaultRoadData(RoadType.Road_A));
                }

                var delta = LoftSplines.Count - m_RoadDatas.Count;

                for (var i = 0; i < delta; i++)
                {
#if UNITY_EDITOR
                    Undo.RecordObject(this, "Modifying Road Data");
#endif
                    var existingData = m_RoadDatas[0];
                    var newRoadData = CreateDefaultRoadData(existingData.roadTypeEnum);
                    m_RoadDatas.Add(newRoadData);
                }
            }
        }

        private LoftRoadExtensionData CreateDefaultRoadData(RoadType roadType)
        {
            var laneCounts = RoadDefaultsInfors.GetDefaultLaneCounts(roadType);
            float leftSidewalkWidth = RoadDefaultsInfors.GetDefaultSidewalkWidth(roadType);
            float rightSidewalkWidth = RoadDefaultsInfors.GetDefaultRightSidewalkWidth(roadType);
            
            // 创建一个新的LoftRoadExtensionData实例
            var roadData = new LoftRoadExtensionData
            {
                roadTypeEnum = roadType,
                laneWidth = new SplineData<float> { DefaultValue = RoadDefaultsInfors.GetDefaultLaneWidth(roadType) },
                leftSidewalkWidth = new SplineData<float> { DefaultValue = leftSidewalkWidth },
                rightSidewalkWidth = new SplineData<float> { DefaultValue = rightSidewalkWidth },
                greenBeltWidth = new SplineData<float> { DefaultValue = RoadDefaultsInfors.GetDefaultGreenBeltWidth(roadType) },
                leftLaneCount = laneCounts.leftLaneCount,
                rightLaneCount = laneCounts.rightLaneCount,
                inOffset = 0.0f,
                outOffset = 0.0f,
                meshOffset = 1.0f, // 初始化网格高度偏移为1
                uvHorizontalOffset = 0.082f,
                
                // 路缘参数
                enableCurb = true, // 默认启用路缘
                curbHeight = 0.1f, // 默认路缘高度为0.1
                curbWidth = 1.0f,  // 默认路缘宽度为1.0
                curbUVScaleU = 0.5f, // 默认路缘U方向纹理缩放
                curbUVScaleV = 1.0f, // 默认路缘V方向纹理缩放
                curbChamferWidth = 0.05f, // 默认路缘内侧倒角宽度
                curbChamferHeight = 0.05f, // 默认路缘内侧倒角高度
                
                // 马路牙子参数
                enableRoadEdge = true, // 默认启用马路牙子
                roadEdgeHeight = 0.15f, // 默认马路牙子高度为0.15
                roadEdgeWidth = 0.3f,   // 默认马路牙子宽度为0.3
                roadEdgeUVScaleU = 0.5f, // 默认马路牙子U方向纹理缩放
                roadEdgeUVScaleV = 1.0f,  // 默认马路牙子V方向纹理缩放
                roadEdgeChamferWidth = 0.05f, // 默认马路牙子内侧倒角宽度
                roadEdgeChamferHeight = 0.05f // 默认马路牙子内侧倒角高度
            };
            
            // 只为特殊指定宽度的道路类型设置宽度值
            switch (roadType)
            {
                case RoadType.Overpass_MainA:
                case RoadType.Overpass_MainB:
                case RoadType.Overpass_MainC:
                case RoadType.Overpass_RampA:
                case RoadType.Overpass_RampB:
                case RoadType.Traffic_Pedestrian:
                case RoadType.Traffic_ZebraCross:
                case RoadType.Train_A:
                case RoadType.Train_B:
                case RoadType.Train_C:
                case RoadType.TRoad_A:
                case RoadType.TRoad_B:
                case RoadType.TRoad_C:
                case RoadType.TRoad_D:
                case RoadType.TRoad_E:
                case RoadType.River_A:
                case RoadType.River_B:
                    roadData.width = new SplineData<float> { DefaultValue = RoadDefaultsInfors.GetDefaultWidth(roadType) };
                    break;
                default:
                    // 对于其他道路类型，初始化一个空的SplineData，宽度将通过计算得出
                    roadData.width = new SplineData<float> { DefaultValue = 0f };
                    break;
            }
            
            return roadData;
        }

        private void OnSplineContainerRemoved(SplineContainer container, int index)
        {
            if (container != m_Spline)
                return;

            RemapRoadMarkersForRemovedSpline(index);
            // 更新所有连接的Junction，移除与被删除的spline相关的连接点
            UpdateConnectedJunctionsForRemovedSpline(index);

            if (index < m_RoadDatas.Count)
            {
#if UNITY_EDITOR
                Undo.RecordObject(this, "Modifying Widths SplineData");
#endif
                m_RoadDatas.RemoveAt(index);
            }

            MarkAllSplinesDirty();
            LoftAllRoads();
        }

        // 当整个spline被删除时，更新所有连接的Junction
        private void UpdateConnectedJunctionsForRemovedSpline(int removedSplineIndex)
        {
            // 更新所有连接的Junction
            foreach (var junction in connectedJunctions)
            {
                if (junction == null)
                    continue;
                
                bool junctionModified = false;
                
                // 检查并更新Junction中的连接点信息
                for (int i = 0; i < junction.connectedRoads.Count; i++)
                {
                    var connectedRoad = junction.connectedRoads[i];
                    
                    // 如果是同一条路的被删除的spline
                    if (connectedRoad.roadBehaviour == this && connectedRoad.splineIndex == removedSplineIndex)
                    {
                        // 移除这个连接点
                        junction.connectedRoads.RemoveAt(i);
                        i--; // 调整索引
                        junctionModified = true;
                    }
                    // 如果是同一条路的更高索引的spline，需要减少splineIndex
                    else if (connectedRoad.roadBehaviour == this && connectedRoad.splineIndex > removedSplineIndex)
                    {
                        connectedRoad.splineIndex--;
                        junction.connectedRoads[i] = connectedRoad;
                        junctionModified = true;
                    }
                }
                
                // 如果Junction的连接点数量小于2，可能需要销毁这个Junction
                if (junction.connectedRoads.Count < 2)
                {
                    Debug.LogWarning("Junction连接点数量不足，可能需要移除: " + junction.name);
                    // 这里可以添加销毁Junction的逻辑，或者让用户手动处理
                }
                else if (junctionModified)
                {
                    MarkJunctionDirty(junction);
                }
            }
        }

        private void OnSplineContainerReordered(SplineContainer container, int previousIndex, int newIndex)
        {
            if (container != m_Spline)
                return;

            RemapRoadMarkersForReorderedSpline(previousIndex, newIndex);
            SynchronizeConnectedRoadSplineIndicesFromMarkers();
            MarkAllSplinesDirty();
            LoftAllRoads();
        }

        private void OnAfterSplineWasModified(Spline s)
        {
            int splineIndex = FindSplineIndex(s);
            bool requiresSynchronousRefresh = splineIndex < 0 || m_SynchronousSplineRefreshIndices.Contains(splineIndex);
            MarkSplineDirty(splineIndex >= 0 ? splineIndex : FindSplineIndex(s));
#if UNITY_EDITOR
            if (ContinuousSplineImplicitJunctionManager.IsApplyingChanges)
            {
                if (splineIndex >= 0)
                {
                    MarkSplineDownstreamDirty(splineIndex);
                }

                return;
            }
#endif
            if (requiresSynchronousRefresh)
            {
                if (splineIndex >= 0)
                {
                    m_SynchronousSplineRefreshIndices.Remove(splineIndex);
                }

                LoftAllRoads();
                return;
            }

            if (TryRefreshImmediateGeometry())
            {
                if (splineIndex >= 0)
                {
                    MarkSplineDownstreamDirty(splineIndex);
                }
#if UNITY_EDITOR
                DeferredDownstreamRefreshScheduler.Enqueue(this);
                if (!ContinuousSplineImplicitJunctionManager.IsApplyingChanges)
                {
                    ContinuousSplineImplicitJunctionManager.EnqueueRoad(this, splineIndex);
                }
#endif
            }
        }

        private void OnSplineChanged(Spline spline, int knotIndex, SplineModification modification)
        {
            int splineIndex = FindSplineIndex(spline);
            if (modification != SplineModification.KnotModified && splineIndex >= 0)
            {
                m_SynchronousSplineRefreshIndices.Add(splineIndex);
            }

            UpdateConnectedJunctionsKnotIndices(spline, knotIndex, modification);
        }

        // 更新所有连接的Junction的连接点信息
        private void UpdateConnectedJunctionsKnotIndices(Spline spline, int affectedKnotIndex, SplineModification modification)
        {
            int splineIndex = -1;
            
            // 找到被修改的spline的索引
            for (int i = 0; i < LoftSplines.Count; i++)
            {
                if (LoftSplines[i] == spline)
                {
                    splineIndex = i;
                    break;
                }
            }
            
            if (splineIndex == -1)
                return;

            RebindRoadMarkersAfterSplineChange(splineIndex);
            PropagateMarkerChangesToConnectedJunctions(splineIndex);
            m_UsePreciseDirtyJunctionSelection = true;
        }

        private void OnAfterSplineDataWasModified(SplineData<float> splineData)
        {
            for (int splineIndex = 0; splineIndex < m_RoadDatas.Count; splineIndex++)
            {
                if (m_RoadDatas[splineIndex].width == splineData)
                {
                    MarkSplineDirty(splineIndex);
                    LoftAllRoads();
                    break;
                }
            }
        }

        private void OnUndoRedoPerformed()
        {
            MarkAllSplinesDirty();
            LoftAllRoads();
        }

        private bool TryRefreshImmediateGeometry()
        {
            try
            {
                if (m_RoadDatas == null || LoftSplines == null)
                {
                    return false;
                }

                bool geometryChanged = RebuildDirtySplineGeometryCachesIfNeeded();
                if (!geometryChanged)
                {
                    return false;
                }

                RefreshCunningDomainObjects(m_LastChangedDomainMask);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error refreshing immediate Cunning road geometry: {e.Message}\n{e.StackTrace}");
                return false;
            }
        }

        private void FlushDeferredDownstreamRefresh()
        {
            try
            {
                BuildDownstreamRefreshSplineIndices();
                if (m_DownstreamRefreshSplineIndices.Count == 0)
                {
                    return;
                }

                CreateLaneSplines(m_DownstreamRefreshSplineIndices);
                CollectSamplePoints(m_DownstreamRefreshSplineIndices);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error refreshing deferred road downstream data: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                m_DirtyDownstreamSplineIndices.Clear();
                m_DownstreamRefreshSplineIndices.Clear();
            }
        }

        private void EnsureCunningRoadComponents()
        {
            EnsureCunningDomainHost(
                ref m_RoadMainObject,
                ref m_CunningGeometryHandle,
                ref m_CunningMesh,
                "RoadMain",
                CunningRoadGeometryHandle.GeometryDomain.RoadMain,
                useRootObject: true);

            EnsureCunningDomainHost(
                ref m_SidewalkBundleObject,
                ref m_SidewalkGeometryHandle,
                ref m_SidewalkCunningMesh,
                "SidewalkBundle",
                CunningRoadGeometryHandle.GeometryDomain.SidewalkBundle,
                useRootObject: false);

            EnsureCunningDomainHost(
                ref m_GreenbeltBundleObject,
                ref m_GreenbeltGeometryHandle,
                ref m_GreenbeltCunningMesh,
                "GreenbeltBundle",
                CunningRoadGeometryHandle.GeometryDomain.GreenbeltBundle,
                useRootObject: false);

            CleanupLegacyRoadMeshChildren();
            ConfigureRootRoadRenderer();
        }

        private void EnsureCunningDomainHost(
            ref GameObject domainObject,
            ref CunningRoadGeometryHandle geometryHandle,
            ref CunningMesh cunningMesh,
            string childName,
            CunningRoadGeometryHandle.GeometryDomain domain,
            bool useRootObject)
        {
            domainObject = ResolveCunningDomainHost(domainObject, childName, useRootObject);

            ResetDomainTransform(domainObject.transform);

            if (domainObject.GetComponent<MeshFilter>() == null)
            {
                domainObject.AddComponent<MeshFilter>();
            }

            if (domainObject.GetComponent<MeshRenderer>() == null)
            {
                domainObject.AddComponent<MeshRenderer>();
            }
            else
            {
                domainObject.GetComponent<MeshRenderer>().enabled = true;
            }

            geometryHandle = domainObject.GetComponent<CunningRoadGeometryHandle>();
            if (geometryHandle == null)
            {
                geometryHandle = domainObject.AddComponent<CunningRoadGeometryHandle>();
            }
            geometryHandle.Domain = domain;

            cunningMesh = domainObject.GetComponent<CunningMesh>();
            if (cunningMesh == null)
            {
                cunningMesh = domainObject.AddComponent<CunningMesh>();
            }

            cunningMesh.ownsHandle = false;
            cunningMesh.displayMode = CunningMesh.DisplayMode.Solid;
            cunningMesh.preserveExistingSolidMaterial = true;
        }

        private GameObject ResolveCunningDomainHost(GameObject currentObject, string childName, bool useRootObject)
        {
            if (useRootObject)
            {
                DestroyDuplicateCunningDomainObject(currentObject, childName);
                DestroyDuplicateCunningDomainObject(transform.Find(childName)?.gameObject, childName);
                return gameObject;
            }

            if (currentObject != null && currentObject != gameObject)
            {
                return currentObject;
            }

            GameObject childObject = transform.Find(childName)?.gameObject;
            if (childObject != null)
            {
                return childObject;
            }

            childObject = new GameObject(childName);
            childObject.transform.SetParent(transform);
            return childObject;
        }

        private void DestroyDuplicateCunningDomainObject(GameObject domainObject, string childName)
        {
            if (domainObject == null || domainObject == gameObject)
            {
                return;
            }

            if (domainObject.transform.parent != transform && domainObject.name != childName)
            {
                return;
            }

#if UNITY_EDITOR
            DestroyImmediate(domainObject);
#else
            Destroy(domainObject);
#endif
        }

        private static void ResetDomainTransform(Transform domainTransform)
        {
            domainTransform.localPosition = Vector3.zero;
            domainTransform.localRotation = Quaternion.identity;
            domainTransform.localScale = Vector3.one;
        }

        private void CleanupLegacyRoadMeshChildren()
        {
            string[] legacyNames =
            {
                "GreenBeltMesh",
                "SidewalkLeftMesh",
                "SidewalkRightMesh",
                "CurbLeftMesh",
                "CurbRightMesh",
                "RoadEdgeLeftMesh",
                "RoadEdgeRightMesh",
                "RoadEdgeOuterLeftMesh",
                "RoadEdgeOuterRightMesh",
            };

            foreach (string legacyName in legacyNames)
            {
                var legacyChild = transform.Find(legacyName);
                if (legacyChild == null)
                {
                    continue;
                }

#if UNITY_EDITOR
                DestroyImmediate(legacyChild.gameObject);
#else
                Destroy(legacyChild.gameObject);
#endif
            }
        }

        private void ConfigureRootRoadRenderer()
        {
            bool rootHostsRoadMain = m_RoadMainObject == gameObject && m_CunningMesh != null;
            var meshFilter = GetComponent<MeshFilter>();
            if (meshFilter != null && !rootHostsRoadMain)
            {
                meshFilter.sharedMesh = null;
            }

            var meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.enabled = rootHostsRoadMain;
            }
        }

        private Material GetPrimaryRoadMaterial()
        {
            var roadRenderer = GetComponent<MeshRenderer>();
            return roadRenderer != null ? roadRenderer.sharedMaterial : null;
        }

        private LoftRoadExtensionData GetRoadDataOrNull(int splineIndex)
        {
            return splineIndex >= 0 && splineIndex < m_RoadDatas.Count ? m_RoadDatas[splineIndex] : null;
        }

        private void RegisterCunningRoadStrip(int vertexStart, int vertexCount, int splineIndex, Material material)
        {
            if (vertexCount < 4 || (vertexCount & 1) != 0)
            {
                return;
            }

            m_CunningRoadStripRanges.Add(new CunningRoadStripRange
            {
                vertexStart = vertexStart,
                vertexCount = vertexCount,
                splineIndex = splineIndex,
                localSegmentIndex = m_CurrentProcessingLocalSegmentIndex,
                segmentId = m_CurrentProcessingSegmentId,
                material = material,
            });
        }

        private void RegisterCunningSideStrip(
            CunningSideStripSource source,
            int vertexStart,
            int vertexCount,
            int splineIndex,
            Material material,
            CunningAreaType areaType,
            int ringSize)
        {
            if (vertexCount < ringSize * 2 || ringSize <= 1 || (vertexCount % ringSize) != 0)
            {
                return;
            }

            m_CunningSideStripRanges.Add(new CunningSideStripRange
            {
                source = source,
                vertexStart = vertexStart,
                vertexCount = vertexCount,
                splineIndex = splineIndex,
                localSegmentIndex = m_CurrentProcessingLocalSegmentIndex,
                segmentId = m_CurrentProcessingSegmentId,
                material = material,
                areaType = areaType,
                ringSize = ringSize,
            });
        }

        private void EnsureSplineGeometryCacheCapacity()
        {
            int splineCount = LoftSplines != null ? LoftSplines.Count : 0;
            while (m_SplineGeometryCaches.Count < splineCount)
            {
                m_SplineGeometryCaches.Add(new SplineGeometryCache());
            }

            while (m_SplineDomainContributionCaches.Count < splineCount)
            {
                m_SplineDomainContributionCaches.Add(new SplineDomainContributionCache());
            }

            while (m_SegmentDomainContributionCaches.Count < splineCount)
            {
                m_SegmentDomainContributionCaches.Add(new List<SegmentDomainContributionCache>());
            }

            if (m_SplineGeometryCaches.Count > splineCount)
            {
                m_SplineGeometryCaches.RemoveRange(splineCount, m_SplineGeometryCaches.Count - splineCount);
                m_SplineDomainContributionCaches.RemoveRange(splineCount, m_SplineDomainContributionCaches.Count - splineCount);
                m_SegmentDomainContributionCaches.RemoveRange(splineCount, m_SegmentDomainContributionCaches.Count - splineCount);
                m_ForceFullSplineRebuild = true;
                m_HasBuiltSplineGeometryCaches = false;
            }
        }

        private static void AddDirtyRange(List<DirtyRange> ranges, int start, int count)
        {
            if (ranges == null || count <= 0)
            {
                return;
            }

            int newStart = start;
            int newEnd = start + count;
            for (int index = ranges.Count - 1; index >= 0; index--)
            {
                DirtyRange existing = ranges[index];
                int existingStart = existing.start;
                int existingEnd = existing.start + existing.count;
                if (newEnd < existingStart || newStart > existingEnd)
                {
                    continue;
                }

                newStart = Mathf.Min(newStart, existingStart);
                newEnd = Mathf.Max(newEnd, existingEnd);
                ranges.RemoveAt(index);
            }

            ranges.Add(new DirtyRange(newStart, newEnd - newStart));
        }

        private static void ReplaceVector3Range(List<Vector3> destination, int start, List<Vector3> source)
        {
            for (int index = 0; index < source.Count; index++)
            {
                destination[start + index] = source[index];
            }
        }

        private static void ReplaceVector2Range(List<Vector2> destination, int start, List<Vector2> source)
        {
            for (int index = 0; index < source.Count; index++)
            {
                destination[start + index] = source[index];
            }
        }

        private static void ReplaceIntRange(List<int> destination, int start, List<int> source)
        {
            for (int index = 0; index < source.Count; index++)
            {
                destination[start + index] = source[index];
            }
        }

        private static void RepackDomainGeometryData(DomainGeometryData source, PackedDomainBuffers destination)
        {
            if (destination == null)
            {
                return;
            }

            if (source == null || !source.HasGeometry)
            {
                destination.Clear();
                return;
            }

            CopyVector3ListToExactArray(source.positions, ref destination.positions);
            CopyIntListToExactArray(source.primitivePointIndices, ref destination.primitivePointIndices);
            CopyUIntListToExactArray(source.primitiveOffsets, ref destination.primitiveOffsets);
            CopyVector3ListToExactArray(source.vertexNormals, ref destination.vertexNormals);
            CopyVector2ListToExactArray(source.vertexUv, ref destination.vertexUv);
            CopyIntListToExactArray(source.primitiveMaterialIndices, ref destination.primitiveMaterialIndices);
            CopyIntListToExactArray(source.primitiveAreaIndices, ref destination.primitiveAreaIndices);
            CopyIntListToExactArray(source.primitiveRoadIds, ref destination.primitiveRoadIds);
            CopyIntListToExactArray(source.primitiveSegmentIds, ref destination.primitiveSegmentIds);
            CopyMaterialListToArray(source.materialPalette.materials, ref destination.renderMaterials);
        }

        private static void RepackVector3DirtyRanges(List<Vector3> source, float[] destination, IReadOnlyList<DirtyRange> dirtyRanges)
        {
            if (source == null || destination == null || dirtyRanges == null)
            {
                return;
            }

            for (int rangeIndex = 0; rangeIndex < dirtyRanges.Count; rangeIndex++)
            {
                DirtyRange range = dirtyRanges[rangeIndex];
                int start = Mathf.Max(0, range.start);
                int end = Mathf.Min(source.Count, range.start + range.count);
                for (int index = start; index < end; index++)
                {
                    int packedIndex = index * 3;
                    Vector3 value = source[index];
                    destination[packedIndex] = value.x;
                    destination[packedIndex + 1] = value.y;
                    destination[packedIndex + 2] = value.z;
                }
            }
        }

        private static void RepackVector2DirtyRanges(List<Vector2> source, float[] destination, IReadOnlyList<DirtyRange> dirtyRanges)
        {
            if (source == null || destination == null || dirtyRanges == null)
            {
                return;
            }

            for (int rangeIndex = 0; rangeIndex < dirtyRanges.Count; rangeIndex++)
            {
                DirtyRange range = dirtyRanges[rangeIndex];
                int start = Mathf.Max(0, range.start);
                int end = Mathf.Min(source.Count, range.start + range.count);
                for (int index = start; index < end; index++)
                {
                    int packedIndex = index * 2;
                    Vector2 value = source[index];
                    destination[packedIndex] = value.x;
                    destination[packedIndex + 1] = value.y;
                }
            }
        }

        private static void RepackIntDirtyRanges(List<int> source, int[] destination, IReadOnlyList<DirtyRange> dirtyRanges)
        {
            if (source == null || destination == null || dirtyRanges == null)
            {
                return;
            }

            for (int rangeIndex = 0; rangeIndex < dirtyRanges.Count; rangeIndex++)
            {
                DirtyRange range = dirtyRanges[rangeIndex];
                int start = Mathf.Max(0, range.start);
                int end = Mathf.Min(source.Count, range.start + range.count);
                for (int index = start; index < end; index++)
                {
                    destination[index] = source[index];
                }
            }
        }

        private static void CopyVector3ListToExactArray(List<Vector3> source, ref float[] destination)
        {
            int requiredLength = source != null ? source.Count * 3 : 0;
            EnsureExactArrayLength(ref destination, requiredLength);
            if (source == null)
            {
                return;
            }

            for (int index = 0; index < source.Count; index++)
            {
                int packedIndex = index * 3;
                Vector3 value = source[index];
                destination[packedIndex] = value.x;
                destination[packedIndex + 1] = value.y;
                destination[packedIndex + 2] = value.z;
            }
        }

        private static void CopyVector2ListToExactArray(List<Vector2> source, ref float[] destination)
        {
            int requiredLength = source != null ? source.Count * 2 : 0;
            EnsureExactArrayLength(ref destination, requiredLength);
            if (source == null)
            {
                return;
            }

            for (int index = 0; index < source.Count; index++)
            {
                int packedIndex = index * 2;
                Vector2 value = source[index];
                destination[packedIndex] = value.x;
                destination[packedIndex + 1] = value.y;
            }
        }

        private static void CopyIntListToExactArray(List<int> source, ref int[] destination)
        {
            int requiredLength = source != null ? source.Count : 0;
            EnsureExactArrayLength(ref destination, requiredLength);
            if (source == null)
            {
                return;
            }

            for (int index = 0; index < source.Count; index++)
            {
                destination[index] = source[index];
            }
        }

        private static void CopyUIntListToExactArray(List<uint> source, ref uint[] destination)
        {
            int requiredLength = source != null ? source.Count : 0;
            EnsureExactArrayLength(ref destination, requiredLength);
            if (source == null)
            {
                return;
            }

            for (int index = 0; index < source.Count; index++)
            {
                destination[index] = source[index];
            }
        }

        private static void CopyMaterialListToArray(List<Material> source, ref Material[] destination)
        {
            int requiredLength = source != null ? source.Count : 0;
            EnsureExactArrayLength(ref destination, requiredLength);
            if (source == null)
            {
                return;
            }

            for (int index = 0; index < source.Count; index++)
            {
                destination[index] = source[index];
            }
        }

        private static void EnsureExactArrayLength<T>(ref T[] buffer, int requiredLength)
        {
            if (requiredLength <= 0)
            {
                buffer = Array.Empty<T>();
                return;
            }

            if (buffer == null || buffer.Length != requiredLength)
            {
                buffer = new T[requiredLength];
            }
        }

        private static DomainAssemblyCache GetDomainAssemblyCache(
            CunningGeometryDomainMask domain,
            DomainAssemblyCache roadMainAssemblyCache,
            DomainAssemblyCache sidewalkBundleAssemblyCache,
            DomainAssemblyCache greenbeltAssemblyCache)
        {
            switch (domain)
            {
                case CunningGeometryDomainMask.RoadMain:
                    return roadMainAssemblyCache;
                case CunningGeometryDomainMask.SidewalkBundle:
                    return sidewalkBundleAssemblyCache;
                case CunningGeometryDomainMask.GreenbeltBundle:
                    return greenbeltAssemblyCache;
                default:
                    return null;
            }
        }

        private DomainAssemblyCache GetDomainAssemblyCache(CunningGeometryDomainMask domain)
        {
            return GetDomainAssemblyCache(domain, m_RoadMainAssemblyCache, m_SidewalkBundleAssemblyCache, m_GreenbeltAssemblyCache);
        }

        private DomainGeometryData GetSplineDomainContribution(int splineIndex, CunningGeometryDomainMask domain)
        {
            if (splineIndex < 0 || splineIndex >= m_SplineDomainContributionCaches.Count)
            {
                return null;
            }

            return m_SplineDomainContributionCaches[splineIndex]?.Get(domain);
        }

        private static void FinalizeDomainGeometryFingerprints(DomainGeometryData domainData)
        {
            if (domainData == null)
            {
                return;
            }

            ulong topologyHash = HashInit();
            topologyHash = HashAdd(topologyHash, domainData.positions.Count);
            topologyHash = HashAdd(topologyHash, domainData.primitivePointIndices.Count);
            topologyHash = HashAdd(topologyHash, domainData.primitiveOffsets.Count);
            topologyHash = HashAdd(topologyHash, domainData.vertexNormals.Count);
            topologyHash = HashAdd(topologyHash, domainData.vertexUv.Count);
            topologyHash = HashAdd(topologyHash, domainData.primitiveMaterialIndices.Count);
            topologyHash = HashAdd(topologyHash, domainData.primitiveAreaIndices.Count);
            topologyHash = HashAdd(topologyHash, domainData.primitiveRoadIds.Count);
            topologyHash = HashAdd(topologyHash, domainData.primitiveSegmentIds.Count);
            for (int index = 0; index < domainData.primitivePointIndices.Count; index++)
            {
                topologyHash = HashAdd(topologyHash, domainData.primitivePointIndices[index]);
            }

            for (int index = 0; index < domainData.primitiveOffsets.Count; index++)
            {
                topologyHash = HashAdd(topologyHash, (int)domainData.primitiveOffsets[index]);
            }

            for (int index = 0; index < domainData.primitiveRoadIds.Count; index++)
            {
                topologyHash = HashAdd(topologyHash, domainData.primitiveRoadIds[index]);
            }

            for (int index = 0; index < domainData.primitiveSegmentIds.Count; index++)
            {
                topologyHash = HashAdd(topologyHash, domainData.primitiveSegmentIds[index]);
            }

            ulong contentHash = topologyHash;
            for (int index = 0; index < domainData.positions.Count; index++)
            {
                contentHash = HashAdd(contentHash, domainData.positions[index]);
            }

            for (int index = 0; index < domainData.vertexNormals.Count; index++)
            {
                contentHash = HashAdd(contentHash, domainData.vertexNormals[index]);
            }

            for (int index = 0; index < domainData.vertexUv.Count; index++)
            {
                Vector2 uv = domainData.vertexUv[index];
                contentHash = HashAdd(contentHash, uv.x);
                contentHash = HashAdd(contentHash, uv.y);
            }

            for (int index = 0; index < domainData.primitiveMaterialIndices.Count; index++)
            {
                contentHash = HashAdd(contentHash, domainData.primitiveMaterialIndices[index]);
            }

            for (int index = 0; index < domainData.primitiveAreaIndices.Count; index++)
            {
                contentHash = HashAdd(contentHash, domainData.primitiveAreaIndices[index]);
            }

            for (int index = 0; index < domainData.materialPalette.materials.Count; index++)
            {
                contentHash = HashAdd(contentHash, domainData.materialPalette.materials[index]);
            }

            domainData.topologyFingerprint = topologyHash;
            domainData.contentFingerprint = contentHash;
        }

        private static ulong HashInit()
        {
            return 14695981039346656037UL;
        }

        private static ulong HashAdd(ulong hash, int value)
        {
            unchecked
            {
                return (hash ^ (uint)value) * 1099511628211UL;
            }
        }

        private static ulong HashAdd(ulong hash, bool value)
        {
            return HashAdd(hash, value ? 1 : 0);
        }

        private static ulong HashAdd(ulong hash, float value)
        {
            return HashAdd(hash, BitConverter.SingleToInt32Bits(value));
        }

        private static ulong HashAdd(ulong hash, Material material)
        {
            return HashAdd(hash, material != null ? material.GetInstanceID() : 0);
        }

        private static ulong HashAdd(ulong hash, Vector3 value)
        {
            hash = HashAdd(hash, value.x);
            hash = HashAdd(hash, value.y);
            hash = HashAdd(hash, value.z);
            return hash;
        }

        private static ulong HashAdd(ulong hash, Quaternion value)
        {
            hash = HashAdd(hash, value.x);
            hash = HashAdd(hash, value.y);
            hash = HashAdd(hash, value.z);
            hash = HashAdd(hash, value.w);
            return hash;
        }

        private static ulong HashAdd(ulong hash, AnimationCurve curve)
        {
            if (curve == null)
            {
                return HashAdd(hash, -1);
            }

            hash = HashAdd(hash, curve.preWrapMode.GetHashCode());
            hash = HashAdd(hash, curve.postWrapMode.GetHashCode());
            hash = HashAdd(hash, curve.length);
            foreach (Keyframe key in curve.keys)
            {
                hash = HashAdd(hash, key.time);
                hash = HashAdd(hash, key.value);
                hash = HashAdd(hash, key.inTangent);
                hash = HashAdd(hash, key.outTangent);
                hash = HashAdd(hash, key.inWeight);
                hash = HashAdd(hash, key.outWeight);
                hash = HashAdd(hash, (int)key.weightedMode);
            }

            return hash;
        }

        private ulong ComputeSplineInputFingerprint(int splineIndex)
        {
            ulong hash = HashInit();
            hash = HashAdd(hash, splineIndex);
            hash = HashAdd(hash, SegmentUnitLength);
            hash = HashAdd(hash, m_TextureScale);
            hash = HashAdd(hash, PrimitiveRoadId);

            var roadData = GetRoadDataOrNull(splineIndex);
            if (roadData != null)
            {
                hash = HashAdd(hash, (int)roadData.roadTypeEnum);
                hash = HashAdd(hash, roadData.leftLaneCount);
                hash = HashAdd(hash, roadData.rightLaneCount);
                hash = HashAdd(hash, roadData.WidthValue);
                hash = HashAdd(hash, roadData.laneWidth != null ? roadData.laneWidth.DefaultValue : 0f);
                hash = HashAdd(hash, roadData.leftSidewalkWidth != null ? roadData.leftSidewalkWidth.DefaultValue : 0f);
                hash = HashAdd(hash, roadData.rightSidewalkWidth != null ? roadData.rightSidewalkWidth.DefaultValue : 0f);
                hash = HashAdd(hash, roadData.greenBeltWidth != null ? roadData.greenBeltWidth.DefaultValue : 0f);
                hash = HashAdd(hash, roadData.meshOffset);
                hash = HashAdd(hash, roadData.uvHorizontalOffset);
                hash = HashAdd(hash, roadData.enableCurb);
                hash = HashAdd(hash, roadData.curbHeight);
                hash = HashAdd(hash, roadData.curbWidth);
                hash = HashAdd(hash, roadData.curbUVScaleU);
                hash = HashAdd(hash, roadData.curbUVScaleV);
                hash = HashAdd(hash, roadData.curbChamferWidth);
                hash = HashAdd(hash, roadData.curbChamferHeight);
                hash = HashAdd(hash, roadData.enableRoadEdge);
                hash = HashAdd(hash, roadData.roadEdgeHeight);
                hash = HashAdd(hash, roadData.roadEdgeWidth);
                hash = HashAdd(hash, roadData.roadEdgeUVScaleU);
                hash = HashAdd(hash, roadData.roadEdgeUVScaleV);
                hash = HashAdd(hash, roadData.roadEdgeChamferWidth);
                hash = HashAdd(hash, roadData.roadEdgeChamferHeight);
                hash = HashAdd(hash, roadData.isOneWay);
                hash = HashAdd(hash, roadData.inOffset);
                hash = HashAdd(hash, roadData.outOffset);
                hash = HashAdd(hash, roadData.inWidth);
                hash = HashAdd(hash, roadData.outWidth);
                hash = HashAdd(hash, roadData.inConsWidth);
                hash = HashAdd(hash, roadData.outConsWidth);
                hash = HashAdd(hash, roadData.algorithmParameters != null ? roadData.algorithmParameters.sampleInterval : 0f);
                hash = HashAdd(hash, roadData.algorithmParameters != null ? roadData.algorithmParameters.widthExpand : 0f);
                hash = HashAdd(hash, roadData.sidewalkMaterial);
                hash = HashAdd(hash, roadData.greenBeltMaterial);
                hash = HashAdd(hash, roadData.curbMaterial);
                hash = HashAdd(hash, roadData.roadEdgeMaterial);
                hash = HashAdd(hash, roadData.customRoadInfo != null ? roadData.customRoadInfo.GetInstanceID() : 0);
                hash = HashAdd(hash, roadData.inCurve);
                hash = HashAdd(hash, roadData.outCurve);
            }

            if (splineIndex < 0 || splineIndex >= LoftSplines.Count)
            {
                return HashAdd(hash, -1);
            }

            Spline spline = LoftSplines[splineIndex];
            if (spline == null)
            {
                return HashAdd(hash, -2);
            }

            hash = HashAdd(hash, spline.Count);
            for (int knotIndex = 0; knotIndex < spline.Count; knotIndex++)
            {
                BezierKnot knot = spline[knotIndex];
                hash = HashAdd(hash, knot.Position);
                hash = HashAdd(hash, knot.TangentIn);
                hash = HashAdd(hash, knot.TangentOut);
                hash = HashAdd(hash, knot.Rotation);
            }

            EnsureSplineSemanticCacheCapacity();
            RebuildSplineSemanticCache(splineIndex);
            if (splineIndex >= 0 && splineIndex < m_SplineSemanticCaches.Count)
            {
                ulong semanticFingerprint = m_SplineSemanticCaches[splineIndex].semanticFingerprint;
                hash = HashAdd(hash, (int)(semanticFingerprint & 0xffffffff));
                hash = HashAdd(hash, (int)(semanticFingerprint >> 32));
            }

            return hash;
        }

        private int FindSplineIndex(Spline spline)
        {
            if (spline == null || LoftSplines == null)
            {
                return -1;
            }

            for (int splineIndex = 0; splineIndex < LoftSplines.Count; splineIndex++)
            {
                if (LoftSplines[splineIndex] == spline)
                {
                    return splineIndex;
                }
            }

            return -1;
        }

        private void MarkSplineDirty(Spline spline)
        {
            int splineIndex = FindSplineIndex(spline);
            if (splineIndex >= 0)
            {
                m_DirtySplineIndices.Add(splineIndex);
            }
            else
            {
                MarkAllSplinesDirty();
            }
        }

        private void MarkSplineDirty(int splineIndex)
        {
            if (LoftSplines == null || splineIndex < 0 || splineIndex >= LoftSplines.Count)
            {
                MarkAllSplinesDirty();
                return;
            }

            m_DirtySplineIndices.Add(splineIndex);
        }

        private void MarkAllSplinesDirty()
        {
            m_DirtySplineIndices.Clear();
            m_DirtyDownstreamSplineIndices.Clear();
            m_SynchronousSplineRefreshIndices.Clear();
            m_ForceFullSplineRebuild = true;
            ClearDirtyJunctionState();
        }

        internal void MarkSplineDownstreamDirty(int splineIndex)
        {
            if (LoftSplines == null || splineIndex < 0 || splineIndex >= LoftSplines.Count)
            {
                MarkAllSplinesDownstreamDirty();
                return;
            }

            m_DirtyDownstreamSplineIndices.Add(splineIndex);
        }

        internal void MarkSplineGeometryAndDownstreamDirty(int splineIndex)
        {
            MarkSplineDirty(splineIndex);
            MarkSplineDownstreamDirty(splineIndex);
        }

        internal void MarkAllSplinesDownstreamDirty()
        {
            if (LoftSplines == null)
            {
                return;
            }

            for (int splineIndex = 0; splineIndex < LoftSplines.Count; splineIndex++)
            {
                m_DirtyDownstreamSplineIndices.Add(splineIndex);
            }
        }

        private void MarkJunctionDirty(JunctionData junction)
        {
            if (junction != null)
            {
                m_DirtyJunctions.Add(junction);
            }
        }

        private bool JunctionUsesSpline(JunctionData junction, int splineIndex)
        {
            if (junction == null || junction.connectedRoads == null)
            {
                return false;
            }

            for (int roadIndex = 0; roadIndex < junction.connectedRoads.Count; roadIndex++)
            {
                var connectedRoad = junction.connectedRoads[roadIndex];
                if (connectedRoad != null &&
                    connectedRoad.roadBehaviour == this &&
                    connectedRoad.splineIndex == splineIndex)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsConnectedRoadAffectedByKnotChange(
            JunctionData.ConnectedRoad connectedRoad,
            Spline spline,
            int affectedKnotIndex,
            SplineModification modification)
        {
            if (connectedRoad == null || spline == null)
            {
                return false;
            }

            if (modification != SplineModification.KnotModified)
            {
                return true;
            }

            if (connectedRoad.knotIndex == affectedKnotIndex)
            {
                return true;
            }

            if (connectedRoad.knotIndex == 0)
            {
                return spline.Count > 1 && affectedKnotIndex == 1;
            }

            return connectedRoad.knotIndex - 1 == affectedKnotIndex;
        }

        private HashSet<JunctionData> CollectJunctionsToUpdate(IReadOnlyList<int> changedSplineIndices)
        {
            var junctionsToUpdate = new HashSet<JunctionData>();

            if (m_UsePreciseDirtyJunctionSelection)
            {
                foreach (var junction in m_DirtyJunctions)
                {
                    if (junction != null)
                    {
                        junctionsToUpdate.Add(junction);
                    }
                }

                return junctionsToUpdate;
            }

            if (changedSplineIndices != null)
            {
                for (int listIndex = 0; listIndex < changedSplineIndices.Count; listIndex++)
                {
                    int splineIndex = changedSplineIndices[listIndex];
                    foreach (var junction in connectedJunctions)
                    {
                        if (junction != null && JunctionUsesSpline(junction, splineIndex))
                        {
                            junctionsToUpdate.Add(junction);
                        }
                    }
                }
            }

            foreach (var junction in m_DirtyJunctions)
            {
                if (junction != null)
                {
                    junctionsToUpdate.Add(junction);
                }
            }

            return junctionsToUpdate;
        }

        private void ClearDirtyJunctionState()
        {
            m_DirtyJunctions.Clear();
            m_UsePreciseDirtyJunctionSelection = false;
        }

        private void BuildDownstreamRefreshSplineIndices()
        {
            m_DownstreamRefreshSplineIndices.Clear();
            m_DownstreamRefreshSplineIndices.AddRange(m_LastChangedSplineIndices);

            foreach (int splineIndex in m_DirtyDownstreamSplineIndices)
            {
                if (LoftSplines != null && splineIndex >= 0 && splineIndex < LoftSplines.Count)
                {
                    m_DownstreamRefreshSplineIndices.Add(splineIndex);
                }
            }

            SortAndUniqueIndices(m_DownstreamRefreshSplineIndices);
        }

        private static void SortAndUniqueIndices(List<int> indices)
        {
            if (indices == null || indices.Count <= 1)
            {
                return;
            }

            indices.Sort();
            int writeIndex = 1;
            int lastValue = indices[0];
            for (int readIndex = 1; readIndex < indices.Count; readIndex++)
            {
                int currentValue = indices[readIndex];
                if (currentValue == lastValue)
                {
                    continue;
                }

                indices[writeIndex++] = currentValue;
                lastValue = currentValue;
            }

            if (writeIndex < indices.Count)
            {
                indices.RemoveRange(writeIndex, indices.Count - writeIndex);
            }
        }

        internal static IDisposable SuppressConnectedJunctionUpdatesScope()
        {
            s_SuppressConnectedJunctionUpdatesDepth++;
            return new ConnectedJunctionUpdateSuppressionScope();
        }

        private static bool AreConnectedJunctionUpdatesSuppressed => s_SuppressConnectedJunctionUpdatesDepth > 0;

        private readonly struct ConnectedJunctionUpdateSuppressionScope : IDisposable
        {
            public void Dispose()
            {
                if (s_SuppressConnectedJunctionUpdatesDepth > 0)
                {
                    s_SuppressConnectedJunctionUpdatesDepth--;
                }
            }
        }

        private static bool HasDomain(CunningGeometryDomainMask mask, CunningGeometryDomainMask domain)
        {
            return (mask & domain) != 0;
        }

        private static CunningGeometryDomainMask GetSplineCacheDomainMask(SplineGeometryCache cache)
        {
            if (cache == null)
            {
                return CunningGeometryDomainMask.None;
            }

            CunningGeometryDomainMask mask = CunningGeometryDomainMask.None;
            if (cache.contributesRoadMain)
            {
                mask |= CunningGeometryDomainMask.RoadMain;
            }

            if (cache.contributesSidewalk)
            {
                mask |= CunningGeometryDomainMask.SidewalkBundle;
            }

            if (cache.contributesGreenbelt)
            {
                mask |= CunningGeometryDomainMask.GreenbeltBundle;
            }

            return mask;
        }

        private static void RefreshSplineCacheDomainContributions(SplineGeometryCache cache, LoftRoadExtensionData roadData)
        {
            if (cache == null)
            {
                return;
            }

            bool hasRoadGeometry = cache.roadStripRanges.Count > 0;
            bool isGreenbelt = roadData != null && roadData.roadTypeEnum == RoadType.GreenBelt_Define;

            cache.contributesRoadMain = hasRoadGeometry && !isGreenbelt;
            cache.contributesGreenbelt = hasRoadGeometry && isGreenbelt;
            cache.contributesSidewalk = cache.sideStripRanges.Count > 0;
        }

        private void RebuildSplineDomainContributions(int splineIndex, SplineGeometryCache cache, LoftRoadExtensionData roadData)
        {
            if (splineIndex < 0 || splineIndex >= m_SplineDomainContributionCaches.Count)
            {
                return;
            }

            SplineDomainContributionCache contributionCache = m_SplineDomainContributionCaches[splineIndex];
            contributionCache.roadMain.Clear();
            contributionCache.sidewalkBundle.Clear();
            contributionCache.greenbeltBundle.Clear();

            if (cache == null || !cache.isBuilt || roadData == null)
            {
                return;
            }

            BuildRoadDomainContribution(cache, greenbeltOnly: false, contributionCache.roadMain);
            BuildSidewalkDomainContribution(cache, contributionCache.sidewalkBundle);
            BuildRoadDomainContribution(cache, greenbeltOnly: true, contributionCache.greenbeltBundle);
        }

        private void BuildRoadDomainContribution(SplineGeometryCache cache, bool greenbeltOnly, DomainGeometryData domainData)
        {
            domainData?.Clear();
            if (cache == null || domainData == null || !cache.isBuilt || cache.roadPositions.Count == 0 || cache.roadStripRanges.Count == 0)
            {
                return;
            }

            for (int stripIndex = 0; stripIndex < cache.roadStripRanges.Count; stripIndex++)
            {
                CunningRoadStripRange strip = cache.roadStripRanges[stripIndex];
                LoftRoadExtensionData roadData = GetRoadDataOrNull(strip.splineIndex);
                bool isGreenbeltStrip = roadData != null && roadData.roadTypeEnum == RoadType.GreenBelt_Define;
                if (isGreenbeltStrip != greenbeltOnly)
                {
                    continue;
                }

                Material material = greenbeltOnly
                    ? (roadData != null ? roadData.greenBeltMaterial : strip.material)
                    : strip.material;
                AppendStripRangeAsQuads(
                    domainData,
                    cache.roadPositions,
                    cache.roadNormals,
                    cache.roadTextures,
                    strip.vertexStart,
                    strip.vertexCount,
                    ringSize: 2,
                    material,
                    greenbeltOnly ? CunningAreaType.Greenbelt : CunningAreaType.RoadSurface,
                    new[] { 0, 1 },
                    PrimitiveRoadId,
                    strip.segmentId);
            }

            FinalizeDomainGeometryFingerprints(domainData);
        }

        private void BuildSidewalkDomainContribution(SplineGeometryCache cache, DomainGeometryData domainData)
        {
            domainData?.Clear();
            if (cache == null || domainData == null || !cache.isBuilt || cache.sideStripRanges.Count == 0)
            {
                return;
            }

            List<BulkExtrudeStripDescriptor> bulkExtrudeStrips = null;
            for (int stripIndex = 0; stripIndex < cache.sideStripRanges.Count; stripIndex++)
            {
                CunningSideStripRange strip = cache.sideStripRanges[stripIndex];
                GetSideStripSourceLists(
                    cache,
                    strip.source,
                    out var positions,
                    out var normals,
                    out var textures);

                if (positions == null || positions.Count == 0)
                {
                    continue;
                }

                if (strip.areaType == CunningAreaType.Curb
                    || strip.areaType == CunningAreaType.RoadEdge
                    || strip.areaType == CunningAreaType.RoadEdgeOuter)
                {
                    if (TryCreateBulkExtrudeStripDescriptor(positions, textures, strip, stripIndex, out BulkExtrudeStripDescriptor descriptor))
                    {
                        bulkExtrudeStrips ??= new List<BulkExtrudeStripDescriptor>();
                        bulkExtrudeStrips.Add(descriptor);
                    }
                    continue;
                }

                AppendStripRangeAsQuads(
                    domainData,
                    positions,
                    normals,
                    textures,
                    strip.vertexStart,
                    strip.vertexCount,
                    strip.ringSize,
                    strip.material,
                    strip.areaType,
                    GetBandPairsForSideStrip(strip),
                    PrimitiveRoadId,
                    strip.segmentId);
            }

            if (bulkExtrudeStrips != null && bulkExtrudeStrips.Count > 0)
            {
                AppendBulkExtrudedSideStrips(domainData, bulkExtrudeStrips);
            }

            FinalizeDomainGeometryFingerprints(domainData);
        }

        private void RebuildSegmentDomainContributions(int splineIndex, SplineGeometryCache cache, LoftRoadExtensionData roadData)
        {
            if (splineIndex < 0 || splineIndex >= m_SegmentDomainContributionCaches.Count)
            {
                return;
            }

            EnsureSegmentContributionCacheCapacity(splineIndex);
            List<SegmentDomainContributionCache> segmentCaches = m_SegmentDomainContributionCaches[splineIndex];
            IReadOnlyList<LogicalSegmentDef> logicalSegments = GetLogicalSegments(splineIndex);
            int segmentCount = logicalSegments != null ? logicalSegments.Count : 0;
            for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
            {
                LogicalSegmentDef logicalSegment = logicalSegments[segmentIndex];
                SegmentDomainContributionCache segmentCache = segmentCaches[segmentIndex];
                segmentCache.Clear();
                segmentCache.key = new SegmentSlotKey(splineIndex, logicalSegment.localSegmentIndex);
                segmentCache.segmentId = logicalSegment.segmentId;

                if (cache == null || !cache.isBuilt || roadData == null)
                {
                    continue;
                }

                BuildRoadDomainContributionForSegment(cache, logicalSegment.localSegmentIndex, greenbeltOnly: false, segmentCache.roadMain);
                BuildSidewalkDomainContributionForSegment(cache, logicalSegment.localSegmentIndex, segmentCache.sidewalkBundle);
                BuildRoadDomainContributionForSegment(cache, logicalSegment.localSegmentIndex, greenbeltOnly: true, segmentCache.greenbeltBundle);
            }
        }

        private void BuildRoadDomainContributionForSegment(
            SplineGeometryCache cache,
            int localSegmentIndex,
            bool greenbeltOnly,
            DomainGeometryData domainData)
        {
            domainData?.Clear();
            if (cache == null || domainData == null || !cache.isBuilt || cache.roadPositions.Count == 0 || cache.roadStripRanges.Count == 0)
            {
                return;
            }

            for (int stripIndex = 0; stripIndex < cache.roadStripRanges.Count; stripIndex++)
            {
                CunningRoadStripRange strip = cache.roadStripRanges[stripIndex];
                if (strip.localSegmentIndex != localSegmentIndex)
                {
                    continue;
                }

                LoftRoadExtensionData roadData = GetRoadDataOrNull(strip.splineIndex);
                bool isGreenbeltStrip = roadData != null && roadData.roadTypeEnum == RoadType.GreenBelt_Define;
                if (isGreenbeltStrip != greenbeltOnly)
                {
                    continue;
                }

                Material material = greenbeltOnly
                    ? (roadData != null ? roadData.greenBeltMaterial : strip.material)
                    : strip.material;
                AppendStripRangeAsQuads(
                    domainData,
                    cache.roadPositions,
                    cache.roadNormals,
                    cache.roadTextures,
                    strip.vertexStart,
                    strip.vertexCount,
                    ringSize: 2,
                    material,
                    greenbeltOnly ? CunningAreaType.Greenbelt : CunningAreaType.RoadSurface,
                    new[] { 0, 1 },
                    PrimitiveRoadId,
                    strip.segmentId);
            }

            FinalizeDomainGeometryFingerprints(domainData);
        }

        private void BuildSidewalkDomainContributionForSegment(
            SplineGeometryCache cache,
            int localSegmentIndex,
            DomainGeometryData domainData)
        {
            domainData?.Clear();
            if (cache == null || domainData == null || !cache.isBuilt || cache.sideStripRanges.Count == 0)
            {
                return;
            }

            List<BulkExtrudeStripDescriptor> bulkExtrudeStrips = null;
            for (int stripIndex = 0; stripIndex < cache.sideStripRanges.Count; stripIndex++)
            {
                CunningSideStripRange strip = cache.sideStripRanges[stripIndex];
                if (strip.localSegmentIndex != localSegmentIndex)
                {
                    continue;
                }

                GetSideStripSourceLists(
                    cache,
                    strip.source,
                    out var positions,
                    out var normals,
                    out var textures);

                if (positions == null || positions.Count == 0)
                {
                    continue;
                }

                if (strip.areaType == CunningAreaType.Curb
                    || strip.areaType == CunningAreaType.RoadEdge
                    || strip.areaType == CunningAreaType.RoadEdgeOuter)
                {
                    if (TryCreateBulkExtrudeStripDescriptor(positions, textures, strip, stripIndex, out BulkExtrudeStripDescriptor descriptor))
                    {
                        bulkExtrudeStrips ??= new List<BulkExtrudeStripDescriptor>();
                        bulkExtrudeStrips.Add(descriptor);
                    }
                    continue;
                }

                AppendStripRangeAsQuads(
                    domainData,
                    positions,
                    normals,
                    textures,
                    strip.vertexStart,
                    strip.vertexCount,
                    strip.ringSize,
                    strip.material,
                    strip.areaType,
                    GetBandPairsForSideStrip(strip),
                    PrimitiveRoadId,
                    strip.segmentId);
            }

            if (bulkExtrudeStrips != null && bulkExtrudeStrips.Count > 0)
            {
                AppendBulkExtrudedSideStrips(domainData, bulkExtrudeStrips);
            }

            FinalizeDomainGeometryFingerprints(domainData);
        }

        private bool RebuildDirtySplineGeometryCachesIfNeeded()
        {
            EnsureSplineGeometryCacheCapacity();
            m_LastChangedSplineIndices.Clear();
            m_LastChangedDomainMask = CunningGeometryDomainMask.None;

            if (LoftSplines == null || m_RoadDatas == null)
            {
                return false;
            }

            if (m_ForceFullSplineRebuild || !m_HasBuiltSplineGeometryCaches)
            {
                m_DirtySplineIndices.Clear();
                for (int splineIndex = 0; splineIndex < LoftSplines.Count; splineIndex++)
                {
                    m_DirtySplineIndices.Add(splineIndex);
                }
            }
            else if (m_DirtySplineIndices.Count == 0)
            {
                for (int splineIndex = 0; splineIndex < LoftSplines.Count; splineIndex++)
                {
                    ulong fingerprint = ComputeSplineInputFingerprint(splineIndex);
                    SplineGeometryCache cache = m_SplineGeometryCaches[splineIndex];
                    if (!cache.isBuilt || cache.inputFingerprint != fingerprint)
                    {
                        m_DirtySplineIndices.Add(splineIndex);
                    }
                }
            }

            if (m_DirtySplineIndices.Count == 0)
            {
                return false;
            }

            m_LastChangedSplineIndices.AddRange(m_DirtySplineIndices);
            m_LastChangedSplineIndices.Sort();
            foreach (int splineIndex in m_LastChangedSplineIndices)
            {
                m_LastChangedDomainMask |= RebuildSplineGeometryCache(splineIndex);
            }

            m_DirtySplineIndices.Clear();
            m_ForceFullSplineRebuild = false;
            m_HasBuiltSplineGeometryCaches = true;
            return true;
        }

        private CunningGeometryDomainMask RebuildSplineGeometryCache(int splineIndex)
        {
            if (splineIndex < 0 || splineIndex >= m_SplineGeometryCaches.Count)
            {
                return CunningGeometryDomainMask.None;
            }

            SplineGeometryCache cache = m_SplineGeometryCaches[splineIndex];
            CunningGeometryDomainMask previousDomainMask = GetSplineCacheDomainMask(cache);
            cache.Clear();
            cache.inputFingerprint = ComputeSplineInputFingerprint(splineIndex);

            if (splineIndex >= LoftSplines.Count || splineIndex >= m_RoadDatas.Count)
            {
                RebuildSplineDomainContributions(splineIndex, cache, null);
                RebuildSegmentDomainContributions(splineIndex, cache, null);
                cache.isBuilt = true;
                return previousDomainMask;
            }

            Spline spline = LoftSplines[splineIndex];
            LoftRoadExtensionData roadData = m_RoadDatas[splineIndex];
            if (spline == null || roadData == null)
            {
                RebuildSplineDomainContributions(splineIndex, cache, roadData);
                RebuildSegmentDomainContributions(splineIndex, cache, roadData);
                cache.isBuilt = true;
                return previousDomainMask;
            }

            RunSplineBuildIntoCache(
                cache,
                () => BuildSplineGeometryFromLogicalSegments(spline, splineIndex, roadData));

            cache.isBuilt = true;
            RefreshSplineCacheDomainContributions(cache, roadData);
            RebuildSplineDomainContributions(splineIndex, cache, roadData);
            RebuildSegmentDomainContributions(splineIndex, cache, roadData);
            return previousDomainMask | GetSplineCacheDomainMask(cache);
        }

        private void RunSplineBuildIntoCache(SplineGeometryCache cache, Action buildAction)
        {
            List<Vector3> savedPositions = m_Positions;
            List<Vector3> savedNormals = m_Normals;
            List<Vector2> savedTextures = m_Textures;
            List<int> savedIndices = m_Indices;
            List<CunningRoadStripRange> savedRoadStripRanges = m_CunningRoadStripRanges;
            List<CunningSideStripRange> savedSideStripRanges = m_CunningSideStripRanges;
            List<Vector3> savedSidewalkLeftPositions = m_SidewalkLeftPositions;
            List<Vector3> savedSidewalkLeftNormals = m_SidewalkLeftNormals;
            List<Vector2> savedSidewalkLeftTextures = m_SidewalkLeftTextures;
            List<int> savedSidewalkLeftIndices = m_SidewalkLeftIndices;
            List<Vector3> savedSidewalkRightPositions = m_SidewalkRightPositions;
            List<Vector3> savedSidewalkRightNormals = m_SidewalkRightNormals;
            List<Vector2> savedSidewalkRightTextures = m_SidewalkRightTextures;
            List<int> savedSidewalkRightIndices = m_SidewalkRightIndices;
            List<Vector3> savedCurbLeftPositions = m_CurbLeftPositions;
            List<Vector3> savedCurbLeftNormals = m_CurbLeftNormals;
            List<Vector2> savedCurbLeftTextures = m_CurbLeftTextures;
            List<int> savedCurbLeftIndices = m_CurbLeftIndices;
            List<Vector3> savedCurbRightPositions = m_CurbRightPositions;
            List<Vector3> savedCurbRightNormals = m_CurbRightNormals;
            List<Vector2> savedCurbRightTextures = m_CurbRightTextures;
            List<int> savedCurbRightIndices = m_CurbRightIndices;
            List<Vector3> savedRoadEdgeLeftPositions = m_RoadEdgeLeftPositions;
            List<Vector3> savedRoadEdgeLeftNormals = m_RoadEdgeLeftNormals;
            List<Vector2> savedRoadEdgeLeftTextures = m_RoadEdgeLeftTextures;
            List<int> savedRoadEdgeLeftIndices = m_RoadEdgeLeftIndices;
            List<Vector3> savedRoadEdgeRightPositions = m_RoadEdgeRightPositions;
            List<Vector3> savedRoadEdgeRightNormals = m_RoadEdgeRightNormals;
            List<Vector2> savedRoadEdgeRightTextures = m_RoadEdgeRightTextures;
            List<int> savedRoadEdgeRightIndices = m_RoadEdgeRightIndices;
            List<Vector3> savedRoadEdgeOuterLeftPositions = m_RoadEdgeOuterLeftPositions;
            List<Vector3> savedRoadEdgeOuterLeftNormals = m_RoadEdgeOuterLeftNormals;
            List<Vector2> savedRoadEdgeOuterLeftTextures = m_RoadEdgeOuterLeftTextures;
            List<int> savedRoadEdgeOuterLeftIndices = m_RoadEdgeOuterLeftIndices;
            List<Vector3> savedRoadEdgeOuterRightPositions = m_RoadEdgeOuterRightPositions;
            List<Vector3> savedRoadEdgeOuterRightNormals = m_RoadEdgeOuterRightNormals;
            List<Vector2> savedRoadEdgeOuterRightTextures = m_RoadEdgeOuterRightTextures;
            List<int> savedRoadEdgeOuterRightIndices = m_RoadEdgeOuterRightIndices;

            try
            {
                m_Positions = cache.roadPositions;
                m_Normals = cache.roadNormals;
                m_Textures = cache.roadTextures;
                m_Indices = cache.roadIndices;
                m_CunningRoadStripRanges = cache.roadStripRanges;
                m_CunningSideStripRanges = cache.sideStripRanges;
                m_SidewalkLeftPositions = cache.sidewalkLeftPositions;
                m_SidewalkLeftNormals = cache.sidewalkLeftNormals;
                m_SidewalkLeftTextures = cache.sidewalkLeftTextures;
                m_SidewalkLeftIndices = cache.sidewalkLeftIndices;
                m_SidewalkRightPositions = cache.sidewalkRightPositions;
                m_SidewalkRightNormals = cache.sidewalkRightNormals;
                m_SidewalkRightTextures = cache.sidewalkRightTextures;
                m_SidewalkRightIndices = cache.sidewalkRightIndices;
                m_CurbLeftPositions = cache.curbLeftPositions;
                m_CurbLeftNormals = cache.curbLeftNormals;
                m_CurbLeftTextures = cache.curbLeftTextures;
                m_CurbLeftIndices = cache.curbLeftIndices;
                m_CurbRightPositions = cache.curbRightPositions;
                m_CurbRightNormals = cache.curbRightNormals;
                m_CurbRightTextures = cache.curbRightTextures;
                m_CurbRightIndices = cache.curbRightIndices;
                m_RoadEdgeLeftPositions = cache.roadEdgeLeftPositions;
                m_RoadEdgeLeftNormals = cache.roadEdgeLeftNormals;
                m_RoadEdgeLeftTextures = cache.roadEdgeLeftTextures;
                m_RoadEdgeLeftIndices = cache.roadEdgeLeftIndices;
                m_RoadEdgeRightPositions = cache.roadEdgeRightPositions;
                m_RoadEdgeRightNormals = cache.roadEdgeRightNormals;
                m_RoadEdgeRightTextures = cache.roadEdgeRightTextures;
                m_RoadEdgeRightIndices = cache.roadEdgeRightIndices;
                m_RoadEdgeOuterLeftPositions = cache.roadEdgeOuterLeftPositions;
                m_RoadEdgeOuterLeftNormals = cache.roadEdgeOuterLeftNormals;
                m_RoadEdgeOuterLeftTextures = cache.roadEdgeOuterLeftTextures;
                m_RoadEdgeOuterLeftIndices = cache.roadEdgeOuterLeftIndices;
                m_RoadEdgeOuterRightPositions = cache.roadEdgeOuterRightPositions;
                m_RoadEdgeOuterRightNormals = cache.roadEdgeOuterRightNormals;
                m_RoadEdgeOuterRightTextures = cache.roadEdgeOuterRightTextures;
                m_RoadEdgeOuterRightIndices = cache.roadEdgeOuterRightIndices;

                buildAction?.Invoke();
            }
            finally
            {
                m_Positions = savedPositions;
                m_Normals = savedNormals;
                m_Textures = savedTextures;
                m_Indices = savedIndices;
                m_CunningRoadStripRanges = savedRoadStripRanges;
                m_CunningSideStripRanges = savedSideStripRanges;
                m_SidewalkLeftPositions = savedSidewalkLeftPositions;
                m_SidewalkLeftNormals = savedSidewalkLeftNormals;
                m_SidewalkLeftTextures = savedSidewalkLeftTextures;
                m_SidewalkLeftIndices = savedSidewalkLeftIndices;
                m_SidewalkRightPositions = savedSidewalkRightPositions;
                m_SidewalkRightNormals = savedSidewalkRightNormals;
                m_SidewalkRightTextures = savedSidewalkRightTextures;
                m_SidewalkRightIndices = savedSidewalkRightIndices;
                m_CurbLeftPositions = savedCurbLeftPositions;
                m_CurbLeftNormals = savedCurbLeftNormals;
                m_CurbLeftTextures = savedCurbLeftTextures;
                m_CurbLeftIndices = savedCurbLeftIndices;
                m_CurbRightPositions = savedCurbRightPositions;
                m_CurbRightNormals = savedCurbRightNormals;
                m_CurbRightTextures = savedCurbRightTextures;
                m_CurbRightIndices = savedCurbRightIndices;
                m_RoadEdgeLeftPositions = savedRoadEdgeLeftPositions;
                m_RoadEdgeLeftNormals = savedRoadEdgeLeftNormals;
                m_RoadEdgeLeftTextures = savedRoadEdgeLeftTextures;
                m_RoadEdgeLeftIndices = savedRoadEdgeLeftIndices;
                m_RoadEdgeRightPositions = savedRoadEdgeRightPositions;
                m_RoadEdgeRightNormals = savedRoadEdgeRightNormals;
                m_RoadEdgeRightTextures = savedRoadEdgeRightTextures;
                m_RoadEdgeRightIndices = savedRoadEdgeRightIndices;
                m_RoadEdgeOuterLeftPositions = savedRoadEdgeOuterLeftPositions;
                m_RoadEdgeOuterLeftNormals = savedRoadEdgeOuterLeftNormals;
                m_RoadEdgeOuterLeftTextures = savedRoadEdgeOuterLeftTextures;
                m_RoadEdgeOuterLeftIndices = savedRoadEdgeOuterLeftIndices;
                m_RoadEdgeOuterRightPositions = savedRoadEdgeOuterRightPositions;
                m_RoadEdgeOuterRightNormals = savedRoadEdgeOuterRightNormals;
                m_RoadEdgeOuterRightTextures = savedRoadEdgeOuterRightTextures;
                m_RoadEdgeOuterRightIndices = savedRoadEdgeOuterRightIndices;
            }
        }

        private void ComposeSplineGeometryCachesToGlobalState()
        {
            ClearAllMeshData();
            EnsureCombinedGeometryCacheCapacity();
            for (int splineIndex = 0; splineIndex < m_SplineGeometryCaches.Count; splineIndex++)
            {
                SplineGeometryCache cache = m_SplineGeometryCaches[splineIndex];
                if (cache == null || !cache.isBuilt)
                {
                    continue;
                }

                AppendSplineGeometryCache(cache);
            }
        }

        private void AppendSplineGeometryCache(SplineGeometryCache cache)
        {
            int roadVertexBase = m_Positions.Count;
            AppendList(m_Positions, cache.roadPositions);
            AppendList(m_Normals, cache.roadNormals);
            AppendList(m_Textures, cache.roadTextures);
            AppendIndices(m_Indices, cache.roadIndices, roadVertexBase);

            foreach (CunningRoadStripRange strip in cache.roadStripRanges)
            {
                m_CunningRoadStripRanges.Add(new CunningRoadStripRange
                {
                    vertexStart = roadVertexBase + strip.vertexStart,
                    vertexCount = strip.vertexCount,
                    splineIndex = strip.splineIndex,
                    material = strip.material,
                });
            }

            int sidewalkLeftBase = m_SidewalkLeftPositions.Count;
            int sidewalkRightBase = m_SidewalkRightPositions.Count;
            int curbLeftBase = m_CurbLeftPositions.Count;
            int curbRightBase = m_CurbRightPositions.Count;
            int roadEdgeLeftBase = m_RoadEdgeLeftPositions.Count;
            int roadEdgeRightBase = m_RoadEdgeRightPositions.Count;
            int roadEdgeOuterLeftBase = m_RoadEdgeOuterLeftPositions.Count;
            int roadEdgeOuterRightBase = m_RoadEdgeOuterRightPositions.Count;

            AppendList(m_SidewalkLeftPositions, cache.sidewalkLeftPositions);
            AppendList(m_SidewalkLeftNormals, cache.sidewalkLeftNormals);
            AppendList(m_SidewalkLeftTextures, cache.sidewalkLeftTextures);
            AppendIndices(m_SidewalkLeftIndices, cache.sidewalkLeftIndices, sidewalkLeftBase);

            AppendList(m_SidewalkRightPositions, cache.sidewalkRightPositions);
            AppendList(m_SidewalkRightNormals, cache.sidewalkRightNormals);
            AppendList(m_SidewalkRightTextures, cache.sidewalkRightTextures);
            AppendIndices(m_SidewalkRightIndices, cache.sidewalkRightIndices, sidewalkRightBase);

            AppendList(m_CurbLeftPositions, cache.curbLeftPositions);
            AppendList(m_CurbLeftNormals, cache.curbLeftNormals);
            AppendList(m_CurbLeftTextures, cache.curbLeftTextures);
            AppendIndices(m_CurbLeftIndices, cache.curbLeftIndices, curbLeftBase);

            AppendList(m_CurbRightPositions, cache.curbRightPositions);
            AppendList(m_CurbRightNormals, cache.curbRightNormals);
            AppendList(m_CurbRightTextures, cache.curbRightTextures);
            AppendIndices(m_CurbRightIndices, cache.curbRightIndices, curbRightBase);

            AppendList(m_RoadEdgeLeftPositions, cache.roadEdgeLeftPositions);
            AppendList(m_RoadEdgeLeftNormals, cache.roadEdgeLeftNormals);
            AppendList(m_RoadEdgeLeftTextures, cache.roadEdgeLeftTextures);
            AppendIndices(m_RoadEdgeLeftIndices, cache.roadEdgeLeftIndices, roadEdgeLeftBase);

            AppendList(m_RoadEdgeRightPositions, cache.roadEdgeRightPositions);
            AppendList(m_RoadEdgeRightNormals, cache.roadEdgeRightNormals);
            AppendList(m_RoadEdgeRightTextures, cache.roadEdgeRightTextures);
            AppendIndices(m_RoadEdgeRightIndices, cache.roadEdgeRightIndices, roadEdgeRightBase);

            AppendList(m_RoadEdgeOuterLeftPositions, cache.roadEdgeOuterLeftPositions);
            AppendList(m_RoadEdgeOuterLeftNormals, cache.roadEdgeOuterLeftNormals);
            AppendList(m_RoadEdgeOuterLeftTextures, cache.roadEdgeOuterLeftTextures);
            AppendIndices(m_RoadEdgeOuterLeftIndices, cache.roadEdgeOuterLeftIndices, roadEdgeOuterLeftBase);

            AppendList(m_RoadEdgeOuterRightPositions, cache.roadEdgeOuterRightPositions);
            AppendList(m_RoadEdgeOuterRightNormals, cache.roadEdgeOuterRightNormals);
            AppendList(m_RoadEdgeOuterRightTextures, cache.roadEdgeOuterRightTextures);
            AppendIndices(m_RoadEdgeOuterRightIndices, cache.roadEdgeOuterRightIndices, roadEdgeOuterRightBase);

            foreach (CunningSideStripRange strip in cache.sideStripRanges)
            {
                m_CunningSideStripRanges.Add(new CunningSideStripRange
                {
                    source = strip.source,
                    vertexStart = GetSideStripBaseOffset(
                        strip.source,
                        sidewalkLeftBase,
                        sidewalkRightBase,
                        curbLeftBase,
                        curbRightBase,
                        roadEdgeLeftBase,
                        roadEdgeRightBase,
                        roadEdgeOuterLeftBase,
                        roadEdgeOuterRightBase) + strip.vertexStart,
                    vertexCount = strip.vertexCount,
                    splineIndex = strip.splineIndex,
                    material = strip.material,
                    areaType = strip.areaType,
                    ringSize = strip.ringSize,
                });
            }
        }

        private void EnsureCombinedGeometryCacheCapacity()
        {
            int roadPositionCount = 0;
            int roadNormalCount = 0;
            int roadTextureCount = 0;
            int roadIndexCount = 0;
            int roadStripCount = 0;
            int sideStripCount = 0;
            int sidewalkLeftPositionCount = 0;
            int sidewalkLeftNormalCount = 0;
            int sidewalkLeftTextureCount = 0;
            int sidewalkLeftIndexCount = 0;
            int sidewalkRightPositionCount = 0;
            int sidewalkRightNormalCount = 0;
            int sidewalkRightTextureCount = 0;
            int sidewalkRightIndexCount = 0;
            int curbLeftPositionCount = 0;
            int curbLeftNormalCount = 0;
            int curbLeftTextureCount = 0;
            int curbLeftIndexCount = 0;
            int curbRightPositionCount = 0;
            int curbRightNormalCount = 0;
            int curbRightTextureCount = 0;
            int curbRightIndexCount = 0;
            int roadEdgeLeftPositionCount = 0;
            int roadEdgeLeftNormalCount = 0;
            int roadEdgeLeftTextureCount = 0;
            int roadEdgeLeftIndexCount = 0;
            int roadEdgeRightPositionCount = 0;
            int roadEdgeRightNormalCount = 0;
            int roadEdgeRightTextureCount = 0;
            int roadEdgeRightIndexCount = 0;
            int roadEdgeOuterLeftPositionCount = 0;
            int roadEdgeOuterLeftNormalCount = 0;
            int roadEdgeOuterLeftTextureCount = 0;
            int roadEdgeOuterLeftIndexCount = 0;
            int roadEdgeOuterRightPositionCount = 0;
            int roadEdgeOuterRightNormalCount = 0;
            int roadEdgeOuterRightTextureCount = 0;
            int roadEdgeOuterRightIndexCount = 0;

            for (int splineIndex = 0; splineIndex < m_SplineGeometryCaches.Count; splineIndex++)
            {
                SplineGeometryCache cache = m_SplineGeometryCaches[splineIndex];
                if (cache == null || !cache.isBuilt)
                {
                    continue;
                }

                roadPositionCount += cache.roadPositions.Count;
                roadNormalCount += cache.roadNormals.Count;
                roadTextureCount += cache.roadTextures.Count;
                roadIndexCount += cache.roadIndices.Count;
                roadStripCount += cache.roadStripRanges.Count;
                sideStripCount += cache.sideStripRanges.Count;
                sidewalkLeftPositionCount += cache.sidewalkLeftPositions.Count;
                sidewalkLeftNormalCount += cache.sidewalkLeftNormals.Count;
                sidewalkLeftTextureCount += cache.sidewalkLeftTextures.Count;
                sidewalkLeftIndexCount += cache.sidewalkLeftIndices.Count;
                sidewalkRightPositionCount += cache.sidewalkRightPositions.Count;
                sidewalkRightNormalCount += cache.sidewalkRightNormals.Count;
                sidewalkRightTextureCount += cache.sidewalkRightTextures.Count;
                sidewalkRightIndexCount += cache.sidewalkRightIndices.Count;
                curbLeftPositionCount += cache.curbLeftPositions.Count;
                curbLeftNormalCount += cache.curbLeftNormals.Count;
                curbLeftTextureCount += cache.curbLeftTextures.Count;
                curbLeftIndexCount += cache.curbLeftIndices.Count;
                curbRightPositionCount += cache.curbRightPositions.Count;
                curbRightNormalCount += cache.curbRightNormals.Count;
                curbRightTextureCount += cache.curbRightTextures.Count;
                curbRightIndexCount += cache.curbRightIndices.Count;
                roadEdgeLeftPositionCount += cache.roadEdgeLeftPositions.Count;
                roadEdgeLeftNormalCount += cache.roadEdgeLeftNormals.Count;
                roadEdgeLeftTextureCount += cache.roadEdgeLeftTextures.Count;
                roadEdgeLeftIndexCount += cache.roadEdgeLeftIndices.Count;
                roadEdgeRightPositionCount += cache.roadEdgeRightPositions.Count;
                roadEdgeRightNormalCount += cache.roadEdgeRightNormals.Count;
                roadEdgeRightTextureCount += cache.roadEdgeRightTextures.Count;
                roadEdgeRightIndexCount += cache.roadEdgeRightIndices.Count;
                roadEdgeOuterLeftPositionCount += cache.roadEdgeOuterLeftPositions.Count;
                roadEdgeOuterLeftNormalCount += cache.roadEdgeOuterLeftNormals.Count;
                roadEdgeOuterLeftTextureCount += cache.roadEdgeOuterLeftTextures.Count;
                roadEdgeOuterLeftIndexCount += cache.roadEdgeOuterLeftIndices.Count;
                roadEdgeOuterRightPositionCount += cache.roadEdgeOuterRightPositions.Count;
                roadEdgeOuterRightNormalCount += cache.roadEdgeOuterRightNormals.Count;
                roadEdgeOuterRightTextureCount += cache.roadEdgeOuterRightTextures.Count;
                roadEdgeOuterRightIndexCount += cache.roadEdgeOuterRightIndices.Count;
            }

            EnsureListCapacity(m_Positions, roadPositionCount);
            EnsureListCapacity(m_Normals, roadNormalCount);
            EnsureListCapacity(m_Textures, roadTextureCount);
            EnsureListCapacity(m_Indices, roadIndexCount);
            EnsureListCapacity(m_CunningRoadStripRanges, roadStripCount);
            EnsureListCapacity(m_CunningSideStripRanges, sideStripCount);
            EnsureListCapacity(m_SidewalkLeftPositions, sidewalkLeftPositionCount);
            EnsureListCapacity(m_SidewalkLeftNormals, sidewalkLeftNormalCount);
            EnsureListCapacity(m_SidewalkLeftTextures, sidewalkLeftTextureCount);
            EnsureListCapacity(m_SidewalkLeftIndices, sidewalkLeftIndexCount);
            EnsureListCapacity(m_SidewalkRightPositions, sidewalkRightPositionCount);
            EnsureListCapacity(m_SidewalkRightNormals, sidewalkRightNormalCount);
            EnsureListCapacity(m_SidewalkRightTextures, sidewalkRightTextureCount);
            EnsureListCapacity(m_SidewalkRightIndices, sidewalkRightIndexCount);
            EnsureListCapacity(m_CurbLeftPositions, curbLeftPositionCount);
            EnsureListCapacity(m_CurbLeftNormals, curbLeftNormalCount);
            EnsureListCapacity(m_CurbLeftTextures, curbLeftTextureCount);
            EnsureListCapacity(m_CurbLeftIndices, curbLeftIndexCount);
            EnsureListCapacity(m_CurbRightPositions, curbRightPositionCount);
            EnsureListCapacity(m_CurbRightNormals, curbRightNormalCount);
            EnsureListCapacity(m_CurbRightTextures, curbRightTextureCount);
            EnsureListCapacity(m_CurbRightIndices, curbRightIndexCount);
            EnsureListCapacity(m_RoadEdgeLeftPositions, roadEdgeLeftPositionCount);
            EnsureListCapacity(m_RoadEdgeLeftNormals, roadEdgeLeftNormalCount);
            EnsureListCapacity(m_RoadEdgeLeftTextures, roadEdgeLeftTextureCount);
            EnsureListCapacity(m_RoadEdgeLeftIndices, roadEdgeLeftIndexCount);
            EnsureListCapacity(m_RoadEdgeRightPositions, roadEdgeRightPositionCount);
            EnsureListCapacity(m_RoadEdgeRightNormals, roadEdgeRightNormalCount);
            EnsureListCapacity(m_RoadEdgeRightTextures, roadEdgeRightTextureCount);
            EnsureListCapacity(m_RoadEdgeRightIndices, roadEdgeRightIndexCount);
            EnsureListCapacity(m_RoadEdgeOuterLeftPositions, roadEdgeOuterLeftPositionCount);
            EnsureListCapacity(m_RoadEdgeOuterLeftNormals, roadEdgeOuterLeftNormalCount);
            EnsureListCapacity(m_RoadEdgeOuterLeftTextures, roadEdgeOuterLeftTextureCount);
            EnsureListCapacity(m_RoadEdgeOuterLeftIndices, roadEdgeOuterLeftIndexCount);
            EnsureListCapacity(m_RoadEdgeOuterRightPositions, roadEdgeOuterRightPositionCount);
            EnsureListCapacity(m_RoadEdgeOuterRightNormals, roadEdgeOuterRightNormalCount);
            EnsureListCapacity(m_RoadEdgeOuterRightTextures, roadEdgeOuterRightTextureCount);
            EnsureListCapacity(m_RoadEdgeOuterRightIndices, roadEdgeOuterRightIndexCount);
        }

        private static int GetSideStripBaseOffset(
            CunningSideStripSource source,
            int sidewalkLeftBase,
            int sidewalkRightBase,
            int curbLeftBase,
            int curbRightBase,
            int roadEdgeLeftBase,
            int roadEdgeRightBase,
            int roadEdgeOuterLeftBase,
            int roadEdgeOuterRightBase)
        {
            switch (source)
            {
                case CunningSideStripSource.SidewalkLeft:
                    return sidewalkLeftBase;
                case CunningSideStripSource.SidewalkRight:
                    return sidewalkRightBase;
                case CunningSideStripSource.CurbLeft:
                    return curbLeftBase;
                case CunningSideStripSource.CurbRight:
                    return curbRightBase;
                case CunningSideStripSource.RoadEdgeLeft:
                    return roadEdgeLeftBase;
                case CunningSideStripSource.RoadEdgeRight:
                    return roadEdgeRightBase;
                case CunningSideStripSource.RoadEdgeOuterLeft:
                    return roadEdgeOuterLeftBase;
                case CunningSideStripSource.RoadEdgeOuterRight:
                    return roadEdgeOuterRightBase;
                default:
                    return 0;
            }
        }

        private static void EnsureListCapacity<T>(List<T> list, int requiredCapacity)
        {
            if (list == null || list.Capacity >= requiredCapacity)
            {
                return;
            }

            list.Capacity = requiredCapacity;
        }

        private static void AppendList<T>(List<T> destination, List<T> source)
        {
            if (destination == null || source == null || source.Count == 0)
            {
                return;
            }

            destination.AddRange(source);
        }

        private static void AppendIndices(List<int> destination, List<int> source, int vertexOffset)
        {
            if (destination == null || source == null || source.Count == 0)
            {
                return;
            }

            for (int index = 0; index < source.Count; index++)
            {
                destination.Add(source[index] + vertexOffset);
            }
        }

        private DomainAssemblyCache BuildOrUpdateDomainAssembly(CunningGeometryDomainMask domain)
        {
            DomainAssemblyCache assembly = GetDomainAssemblyCache(domain);
            if (assembly == null)
            {
                return null;
            }

            List<SegmentDomainContributionCache> orderedContributions = CollectOrderedSegmentDomainContributions(domain);
            assembly.ClearDirtyRanges();

            bool requiresFullRebuild = !assembly.isBuilt || assembly.orderedKeys.Count != orderedContributions.Count;
            assembly.EnsureSlotCapacity(orderedContributions.Count);
            if (!requiresFullRebuild)
            {
                for (int slotIndex = 0; slotIndex < orderedContributions.Count; slotIndex++)
                {
                    SegmentDomainContributionCache contributionCache = orderedContributions[slotIndex];
                    if (contributionCache == null || !assembly.orderedKeys[slotIndex].Equals(contributionCache.key))
                    {
                        requiresFullRebuild = true;
                        break;
                    }
                }
            }

            if (!requiresFullRebuild)
            {
                for (int index = 0; index < orderedContributions.Count; index++)
                {
                    SegmentDomainContributionCache contributionCache = orderedContributions[index];
                    if (contributionCache == null || !m_LastChangedSplineIndices.Contains(contributionCache.key.splineIndex))
                    {
                        continue;
                    }

                    DomainGeometryData contribution = contributionCache.Get(domain);
                    if (ContributionRequiresTopologyRebuild(assembly.slots[index], contribution))
                    {
                        requiresFullRebuild = true;
                        break;
                    }
                }
            }

            if (requiresFullRebuild)
            {
                ReassembleDomainAssembly(domain, assembly, orderedContributions);
            }
            else
            {
                PatchDomainAssemblyContent(domain, assembly, orderedContributions);
            }

            assembly.isBuilt = true;
            return assembly;
        }

        private static bool ContributionRequiresTopologyRebuild(DomainAssemblySlot slot, DomainGeometryData contribution)
        {
            bool hasGeometry = contribution != null && contribution.HasGeometry;
            if (slot == null)
            {
                return hasGeometry;
            }

            if (slot.hasGeometry != hasGeometry)
            {
                return true;
            }

            if (!hasGeometry)
            {
                return false;
            }

            return slot.pointCount != contribution.positions.Count
                || slot.vertexCount != contribution.vertexNormals.Count
                || slot.primitiveCount != contribution.PrimitiveCount
                || slot.topologyFingerprint != contribution.topologyFingerprint;
        }

        private List<SegmentDomainContributionCache> CollectOrderedSegmentDomainContributions(CunningGeometryDomainMask domain)
        {
            var ordered = new List<SegmentDomainContributionCache>();
            if (LoftSplines == null)
            {
                return ordered;
            }

            EnsureSplineGeometryCacheCapacity();
            for (int splineIndex = 0; splineIndex < LoftSplines.Count; splineIndex++)
            {
                EnsureSegmentContributionCacheCapacity(splineIndex);
                List<SegmentDomainContributionCache> segmentCaches = splineIndex < m_SegmentDomainContributionCaches.Count
                    ? m_SegmentDomainContributionCaches[splineIndex]
                    : null;
                if (segmentCaches == null || segmentCaches.Count == 0)
                {
                    continue;
                }

                for (int localSegmentIndex = 0; localSegmentIndex < segmentCaches.Count; localSegmentIndex++)
                {
                    SegmentDomainContributionCache segmentCache = segmentCaches[localSegmentIndex];
                    if (segmentCache == null)
                    {
                        continue;
                    }

                    ordered.Add(segmentCache);
                }
            }

            return ordered;
        }

        private void EnsureSegmentContributionCacheCapacity(int splineIndex)
        {
            if (splineIndex < 0 || splineIndex >= m_SegmentDomainContributionCaches.Count)
            {
                return;
            }

            IReadOnlyList<LogicalSegmentDef> logicalSegments = GetLogicalSegments(splineIndex);
            int segmentCount = logicalSegments != null ? logicalSegments.Count : 0;
            List<SegmentDomainContributionCache> segmentCaches = m_SegmentDomainContributionCaches[splineIndex];
            if (segmentCaches == null)
            {
                segmentCaches = new List<SegmentDomainContributionCache>();
                m_SegmentDomainContributionCaches[splineIndex] = segmentCaches;
            }

            while (segmentCaches.Count < segmentCount)
            {
                segmentCaches.Add(new SegmentDomainContributionCache());
            }

            if (segmentCaches.Count > segmentCount)
            {
                segmentCaches.RemoveRange(segmentCount, segmentCaches.Count - segmentCount);
            }
        }

        private void ReassembleDomainAssembly(
            CunningGeometryDomainMask domain,
            DomainAssemblyCache assembly,
            IReadOnlyList<SegmentDomainContributionCache> orderedContributions)
        {
            assembly.assembled.Clear(clearMaterialPalette: false);
            assembly.ClearDirtyRanges();
            assembly.orderedKeys.Clear();

            int contributionCount = orderedContributions != null ? orderedContributions.Count : 0;
            assembly.EnsureSlotCapacity(contributionCount);
            for (int contributionIndex = 0; contributionIndex < contributionCount; contributionIndex++)
            {
                SegmentDomainContributionCache contributionCache = orderedContributions[contributionIndex];
                assembly.orderedKeys.Add(contributionCache != null ? contributionCache.key : default);
                AppendContributionToAssembly(
                    assembly,
                    contributionIndex,
                    contributionCache != null ? contributionCache.key : default,
                    contributionCache != null ? contributionCache.segmentId : 0,
                    contributionCache?.Get(domain));
            }

            FinalizeDomainGeometryFingerprints(assembly.assembled);
            RepackDomainGeometryData(assembly.assembled, assembly.packed);
            assembly.needsFullUpload = true;
            assembly.hasContentChanges = assembly.assembled.HasGeometry;
            assembly.renderMaterialsChanged = true;
        }

        private void AppendContributionToAssembly(
            DomainAssemblyCache assembly,
            int slotIndex,
            SegmentSlotKey slotKey,
            int segmentId,
            DomainGeometryData contribution)
        {
            DomainAssemblySlot slot = assembly.slots[slotIndex];
            slot.Reset();
            slot.key = slotKey;
            slot.segmentId = segmentId;

            if (contribution == null || !contribution.HasGeometry)
            {
                if (contribution != null)
                {
                    slot.topologyFingerprint = contribution.topologyFingerprint;
                    slot.contentFingerprint = contribution.contentFingerprint;
                }

                return;
            }

            DomainGeometryData assembled = assembly.assembled;
            int pointStart = assembled.positions.Count;
            int vertexStart = assembled.vertexNormals.Count;
            int primitiveStart = assembled.primitiveMaterialIndices.Count;
            int primitivePointBase = assembled.primitivePointIndices.Count;

            AppendList(assembled.positions, contribution.positions);
            AppendList(assembled.vertexNormals, contribution.vertexNormals);
            AppendList(assembled.vertexUv, contribution.vertexUv);

            for (int pointIndex = 0; pointIndex < contribution.primitivePointIndices.Count; pointIndex++)
            {
                assembled.primitivePointIndices.Add(pointStart + contribution.primitivePointIndices[pointIndex]);
            }

            for (int offsetIndex = 1; offsetIndex < contribution.primitiveOffsets.Count; offsetIndex++)
            {
                assembled.primitiveOffsets.Add((uint)(primitivePointBase + contribution.primitiveOffsets[offsetIndex]));
            }

            for (int primitiveIndex = 0; primitiveIndex < contribution.PrimitiveCount; primitiveIndex++)
            {
                int localMaterialSlot = contribution.primitiveMaterialIndices[primitiveIndex];
                Material material = localMaterialSlot >= 0 && localMaterialSlot < contribution.materialPalette.materials.Count
                    ? contribution.materialPalette.materials[localMaterialSlot]
                    : null;
                assembled.primitiveMaterialIndices.Add(assembled.materialPalette.GetSlot(material));
                assembled.primitiveAreaIndices.Add(contribution.primitiveAreaIndices[primitiveIndex]);
                assembled.primitiveRoadIds.Add(contribution.primitiveRoadIds[primitiveIndex]);
                assembled.primitiveSegmentIds.Add(contribution.primitiveSegmentIds[primitiveIndex]);
            }

            slot.hasGeometry = true;
            slot.pointStart = pointStart;
            slot.pointCount = contribution.positions.Count;
            slot.vertexStart = vertexStart;
            slot.vertexCount = contribution.vertexNormals.Count;
            slot.primitiveStart = primitiveStart;
            slot.primitiveCount = contribution.PrimitiveCount;
            slot.topologyFingerprint = contribution.topologyFingerprint;
            slot.contentFingerprint = contribution.contentFingerprint;
        }

        private void PatchDomainAssemblyContent(
            CunningGeometryDomainMask domain,
            DomainAssemblyCache assembly,
            IReadOnlyList<SegmentDomainContributionCache> orderedContributions)
        {
            DomainGeometryData assembled = assembly.assembled;
            for (int slotIndex = 0; slotIndex < orderedContributions.Count; slotIndex++)
            {
                SegmentDomainContributionCache contributionCache = orderedContributions[slotIndex];
                if (contributionCache == null || !m_LastChangedSplineIndices.Contains(contributionCache.key.splineIndex))
                {
                    continue;
                }

                DomainAssemblySlot slot = assembly.slots[slotIndex];
                if (!slot.key.Equals(contributionCache.key))
                {
                    ReassembleDomainAssembly(domain, assembly, orderedContributions);
                    return;
                }

                DomainGeometryData contribution = contributionCache.Get(domain);
                bool hasGeometry = contribution != null && contribution.HasGeometry;
                if (!slot.hasGeometry && !hasGeometry)
                {
                    if (contribution != null)
                    {
                        slot.topologyFingerprint = contribution.topologyFingerprint;
                        slot.contentFingerprint = contribution.contentFingerprint;
                    }

                    continue;
                }

                if (!slot.hasGeometry || !hasGeometry)
                {
                    ReassembleDomainAssembly(domain, assembly, orderedContributions);
                    return;
                }

                if (slot.contentFingerprint == contribution.contentFingerprint)
                {
                    continue;
                }

                ReplaceVector3Range(assembled.positions, slot.pointStart, contribution.positions);
                ReplaceVector3Range(assembled.vertexNormals, slot.vertexStart, contribution.vertexNormals);
                ReplaceVector2Range(assembled.vertexUv, slot.vertexStart, contribution.vertexUv);

                bool renderMaterialsChanged = false;
                for (int primitiveIndex = 0; primitiveIndex < contribution.PrimitiveCount; primitiveIndex++)
                {
                    int localMaterialSlot = contribution.primitiveMaterialIndices[primitiveIndex];
                    Material material = localMaterialSlot >= 0 && localMaterialSlot < contribution.materialPalette.materials.Count
                        ? contribution.materialPalette.materials[localMaterialSlot]
                        : null;
                    int previousMaterialCount = assembled.materialPalette.materials.Count;
                    int assembledMaterialSlot = assembled.materialPalette.GetSlot(material);
                    if (assembled.materialPalette.materials.Count != previousMaterialCount)
                    {
                        renderMaterialsChanged = true;
                    }

                    assembled.primitiveMaterialIndices[slot.primitiveStart + primitiveIndex] = assembledMaterialSlot;
                    assembled.primitiveAreaIndices[slot.primitiveStart + primitiveIndex] = contribution.primitiveAreaIndices[primitiveIndex];
                    assembled.primitiveRoadIds[slot.primitiveStart + primitiveIndex] = contribution.primitiveRoadIds[primitiveIndex];
                    assembled.primitiveSegmentIds[slot.primitiveStart + primitiveIndex] = contribution.primitiveSegmentIds[primitiveIndex];
                }

                AddDirtyRange(assembly.pointDirtyRanges, slot.pointStart, slot.pointCount);
                AddDirtyRange(assembly.vertexDirtyRanges, slot.vertexStart, slot.vertexCount);
                AddDirtyRange(assembly.primitiveDirtyRanges, slot.primitiveStart, slot.primitiveCount);
                assembly.hasContentChanges = true;
                assembly.renderMaterialsChanged |= renderMaterialsChanged;
                slot.contentFingerprint = contribution.contentFingerprint;
                slot.topologyFingerprint = contribution.topologyFingerprint;
            }

            FinalizeDomainGeometryFingerprints(assembled);
            if (assembly.hasContentChanges)
            {
                RepackVector3DirtyRanges(assembled.positions, assembly.packed.positions, assembly.pointDirtyRanges);
                RepackVector3DirtyRanges(assembled.vertexNormals, assembly.packed.vertexNormals, assembly.vertexDirtyRanges);
                RepackVector2DirtyRanges(assembled.vertexUv, assembly.packed.vertexUv, assembly.vertexDirtyRanges);
                RepackIntDirtyRanges(assembled.primitiveMaterialIndices, assembly.packed.primitiveMaterialIndices, assembly.primitiveDirtyRanges);
                RepackIntDirtyRanges(assembled.primitiveAreaIndices, assembly.packed.primitiveAreaIndices, assembly.primitiveDirtyRanges);
                RepackIntDirtyRanges(assembled.primitiveRoadIds, assembly.packed.primitiveRoadIds, assembly.primitiveDirtyRanges);
                RepackIntDirtyRanges(assembled.primitiveSegmentIds, assembly.packed.primitiveSegmentIds, assembly.primitiveDirtyRanges);
            }

            if (assembly.renderMaterialsChanged
                || assembly.packed.renderMaterials.Length != assembled.materialPalette.materials.Count)
            {
                CopyMaterialListToArray(assembled.materialPalette.materials, ref assembly.packed.renderMaterials);
            }
        }

        internal void RebuildCunningRoadGeometry(CunningRoadGeometryHandle geometryHandle)
        {
            if (geometryHandle == null)
            {
                return;
            }

            switch (geometryHandle.Domain)
            {
                case CunningRoadGeometryHandle.GeometryDomain.RoadMain:
                    RebuildRoadDomainGeometry(geometryHandle, greenbeltOnly: false);
                    break;
                case CunningRoadGeometryHandle.GeometryDomain.SidewalkBundle:
                    RebuildSidewalkBundleGeometry(geometryHandle);
                    break;
                case CunningRoadGeometryHandle.GeometryDomain.GreenbeltBundle:
                    RebuildRoadDomainGeometry(geometryHandle, greenbeltOnly: true);
                    break;
            }
        }

        private void RebuildRoadDomainGeometry(CunningRoadGeometryHandle geometryHandle, bool greenbeltOnly)
        {
            CunningGeometryDomainMask domain = greenbeltOnly
                ? CunningGeometryDomainMask.GreenbeltBundle
                : CunningGeometryDomainMask.RoadMain;
            DomainAssemblyCache assembly = BuildOrUpdateDomainAssembly(domain);
            DomainGeometryData domainData = assembly?.assembled;
            if (domainData == null || !domainData.HasGeometry)
            {
                geometryHandle.Dispose();
                return;
            }

            bool hadExistingHandle = geometryHandle.GeometryHandle != 0;
            ulong handle = geometryHandle.EnsureGeometryCreated();
            if (handle == 0)
            {
                Debug.LogError($"Failed to create Cunning geometry for road {name}");
                return;
            }

            if (!PrepareRoadGeometryHandleForRebuild(geometryHandle, hadExistingHandle, ref handle))
            {
                Debug.LogError($"Failed to recreate Cunning geometry for road {name}");
                return;
            }

            try
            {
                UploadDomainGeometryToHandle(handle, geometryHandle, domainData, assembly);
                if (assembly != null)
                {
                    assembly.needsFullUpload = false;
                    assembly.ClearDirtyRanges();
                }
            }
            catch
            {
                geometryHandle.Dispose();
                throw;
            }
        }

        private void RebuildSidewalkBundleGeometry(CunningRoadGeometryHandle geometryHandle)
        {
            DomainAssemblyCache assembly = BuildOrUpdateDomainAssembly(CunningGeometryDomainMask.SidewalkBundle);
            DomainGeometryData domainData = assembly?.assembled;
            if (domainData == null || !domainData.HasGeometry)
            {
                geometryHandle.Dispose();
                return;
            }

            bool hadExistingHandle = geometryHandle.GeometryHandle != 0;
            ulong handle = geometryHandle.EnsureGeometryCreated();
            if (handle == 0)
            {
                Debug.LogError($"Failed to create Cunning geometry for sidewalk bundle on road {name}");
                return;
            }

            if (!PrepareRoadGeometryHandleForRebuild(geometryHandle, hadExistingHandle, ref handle))
            {
                Debug.LogError($"Failed to recreate Cunning geometry for sidewalk bundle on road {name}");
                return;
            }

            try
            {
                UploadDomainGeometryToHandle(handle, geometryHandle, domainData, assembly);
                if (assembly != null)
                {
                    assembly.needsFullUpload = false;
                    assembly.ClearDirtyRanges();
                }
            }
            catch
            {
                geometryHandle.Dispose();
                throw;
            }
        }

        private bool PrepareRoadGeometryHandleForRebuild(CunningRoadGeometryHandle geometryHandle, bool hadExistingHandle, ref ulong handle)
        {
            return true;
        }

        private DomainGeometryData BuildRoadDomainGeometry(bool greenbeltOnly)
        {
            var domainData = new DomainGeometryData();
            if (m_SplineGeometryCaches.Count == 0)
            {
                return domainData;
            }

            for (int cacheIndex = 0; cacheIndex < m_SplineGeometryCaches.Count; cacheIndex++)
            {
                SplineGeometryCache cache = m_SplineGeometryCaches[cacheIndex];
                if (cache == null || !cache.isBuilt || cache.roadPositions.Count == 0 || cache.roadStripRanges.Count == 0)
                {
                    continue;
                }

                foreach (var strip in cache.roadStripRanges)
                {
                    var roadData = GetRoadDataOrNull(strip.splineIndex);
                    bool isGreenbeltStrip = roadData != null && roadData.roadTypeEnum == RoadType.GreenBelt_Define;
                    if (isGreenbeltStrip != greenbeltOnly)
                    {
                        continue;
                    }

                    Material material = greenbeltOnly
                        ? (roadData != null ? roadData.greenBeltMaterial : strip.material)
                        : strip.material;
                    AppendStripRangeAsQuads(
                        domainData,
                        cache.roadPositions,
                        cache.roadNormals,
                        cache.roadTextures,
                        strip.vertexStart,
                        strip.vertexCount,
                        ringSize: 2,
                        material,
                        greenbeltOnly ? CunningAreaType.Greenbelt : CunningAreaType.RoadSurface,
                        new[] { 0, 1 },
                        PrimitiveRoadId,
                        strip.segmentId);
                }
            }

            return domainData;
        }

        private DomainGeometryData BuildSidewalkBundleGeometry()
        {
            var domainData = new DomainGeometryData();
            for (int cacheIndex = 0; cacheIndex < m_SplineGeometryCaches.Count; cacheIndex++)
            {
                SplineGeometryCache cache = m_SplineGeometryCaches[cacheIndex];
                if (cache == null || !cache.isBuilt || cache.sideStripRanges.Count == 0)
                {
                    continue;
                }

                foreach (var strip in cache.sideStripRanges)
                {
                    GetSideStripSourceLists(
                        cache,
                        strip.source,
                        out var positions,
                        out var normals,
                        out var textures);

                    if (positions == null || positions.Count == 0)
                    {
                        continue;
                    }

                    if (strip.areaType == CunningAreaType.Curb
                        || strip.areaType == CunningAreaType.RoadEdge
                        || strip.areaType == CunningAreaType.RoadEdgeOuter)
                    {
                        AppendPolyExtrudedSideStrip(domainData, positions, strip);
                        continue;
                    }

                    AppendStripRangeAsQuads(
                        domainData,
                        positions,
                        normals,
                        textures,
                        strip.vertexStart,
                        strip.vertexCount,
                        strip.ringSize,
                        strip.material,
                        strip.areaType,
                        GetBandPairsForSideStrip(strip),
                        PrimitiveRoadId,
                        strip.segmentId);
                }
            }

            return domainData;
        }

        private bool TryCreateBulkExtrudeStripDescriptor(
            List<Vector3> sourcePositions,
            List<Vector2> sourceTextures,
            CunningSideStripRange strip,
            int sourceStripIndex,
            out BulkExtrudeStripDescriptor descriptor)
        {
            descriptor = null;
            if (sourcePositions == null || strip == null)
            {
                return false;
            }

            LoftRoadExtensionData roadData = GetRoadDataOrNull(strip.splineIndex);
            if (roadData == null)
            {
                return false;
            }

            int baseInnerBandIndex = GetPolyExtrudeBaseInnerBandIndex(strip);
            if (!TryGetStripFrame(sourcePositions, strip.vertexStart, strip.vertexCount, strip.ringSize, baseInnerBandIndex, out Vector3 stripOrigin, out Vector3 stripAlongAxis))
            {
                return false;
            }

            Vector3 normalizedAlongAxis = stripAlongAxis.sqrMagnitude > 1e-6f ? stripAlongAxis.normalized : Vector3.forward;
            int sampleCount = strip.ringSize > 0 ? strip.vertexCount / strip.ringSize : 0;
            if (sampleCount < 2)
            {
                return false;
            }

            int firstSampleIndex = strip.vertexStart + baseInnerBandIndex;
            int lastSampleIndex = strip.vertexStart + (sampleCount - 1) * strip.ringSize + baseInnerBandIndex;
            float alongStartDistance = Vector3.Dot(sourcePositions[firstSampleIndex] - stripOrigin, normalizedAlongAxis);
            float alongEndDistance = Vector3.Dot(sourcePositions[lastSampleIndex] - stripOrigin, normalizedAlongAxis);

            StripUvFrame uvFrame = TryBuildStripUvFrame(sourceTextures, strip, out StripUvFrame derivedFrame)
                ? derivedFrame
                : BuildFallbackStripUvFrame(roadData, strip);

            descriptor = new BulkExtrudeStripDescriptor
            {
                strip = strip,
                sourcePositions = sourcePositions,
                sourceTextures = sourceTextures,
                origin = stripOrigin,
                alongAxis = normalizedAlongAxis,
                uvScaleU = GetAreaUvScaleU(roadData, strip.areaType),
                uvScaleV = GetAreaUvScaleV(roadData, strip.areaType),
                uvOffsetU = GetAreaUvOffsetU(roadData, strip.areaType),
                extrudeDistance = GetPolyExtrudeSignedDistance(roadData, strip),
                extrudeInset = GetPolyExtrudeInset(roadData, strip.areaType),
                sourceStripIndex = sourceStripIndex,
                sourceAlongStartDistance = alongStartDistance,
                sourceAlongEndDistance = alongEndDistance,
                uvFrame = uvFrame,
            };
            return true;
        }

        private static float GetUvAlongComponent(Vector2 uv, bool alongInU)
        {
            return alongInU ? uv.x : uv.y;
        }

        private static float GetUvAcrossComponent(Vector2 uv, bool alongInU)
        {
            return alongInU ? uv.y : uv.x;
        }

        private static int GetPreferredTopInnerBandIndex(CunningSideStripRange strip)
        {
            if (strip == null || strip.ringSize <= 0)
            {
                return 0;
            }

            return Mathf.Clamp(strip.ringSize - 2, 0, strip.ringSize - 1);
        }

        private static int GetPreferredTopOuterBandIndex(CunningSideStripRange strip)
        {
            if (strip == null || strip.ringSize <= 0)
            {
                return 0;
            }

            return Mathf.Clamp(strip.ringSize - 1, 0, strip.ringSize - 1);
        }

        private static bool TryBuildStripUvFrame(
            List<Vector2> sourceTextures,
            CunningSideStripRange strip,
            out StripUvFrame frame)
        {
            frame = default;
            if (sourceTextures == null
                || strip == null
                || strip.ringSize <= 0
                || strip.vertexCount < strip.ringSize * 2
                || strip.vertexStart < 0
                || strip.vertexStart + strip.vertexCount > sourceTextures.Count)
            {
                return false;
            }

            bool alongInU = strip.areaType == CunningAreaType.RoadEdge || strip.areaType == CunningAreaType.RoadEdgeOuter;
            int sampleCount = strip.vertexCount / strip.ringSize;
            int topInnerBandIndex = GetPreferredTopInnerBandIndex(strip);
            int topOuterBandIndex = GetPreferredTopOuterBandIndex(strip);
            int firstSampleBase = strip.vertexStart;
            int lastSampleBase = strip.vertexStart + (sampleCount - 1) * strip.ringSize;

            float alongStart = GetUvAlongComponent(sourceTextures[firstSampleBase + topInnerBandIndex], alongInU);
            float alongEnd = GetUvAlongComponent(sourceTextures[lastSampleBase + topInnerBandIndex], alongInU);
            if (Mathf.Abs(alongEnd - alongStart) <= 1e-5f)
            {
                alongEnd = alongStart + 1e-4f;
            }

            float sideMinAcross = GetUvAcrossComponent(sourceTextures[firstSampleBase], alongInU);
            float topInnerAcross = GetUvAcrossComponent(sourceTextures[firstSampleBase + topInnerBandIndex], alongInU);
            float topOuterAcross = GetUvAcrossComponent(sourceTextures[firstSampleBase + topOuterBandIndex], alongInU);
            float sideSpan = topInnerAcross - sideMinAcross;
            float sideMaxAcross = topOuterAcross + sideSpan;
            bool flip = topOuterAcross < topInnerAcross;

            frame = new StripUvFrame(
                alongInU,
                flip,
                alongStart,
                alongEnd,
                sideMinAcross,
                topInnerAcross,
                topOuterAcross,
                sideMaxAcross);
            return true;
        }

        private static StripUvFrame BuildFallbackStripUvFrame(LoftRoadExtensionData roadData, CunningSideStripRange strip)
        {
            float uvScaleU = GetAreaUvScaleU(roadData, strip.areaType);
            bool alongInU = strip.areaType == CunningAreaType.RoadEdge || strip.areaType == CunningAreaType.RoadEdgeOuter;
            return new StripUvFrame(
                alongInU,
                flip: false,
                alongStart: 0.0f,
                alongEnd: 1.0f,
                sideMinAcross: 0.0f,
                topInnerAcross: 0.3f * uvScaleU,
                topOuterAcross: 0.7f * uvScaleU,
                sideMaxAcross: 1.0f * uvScaleU);
        }

        private void AppendBulkExtrudedSideStrips(DomainGeometryData domainData, List<BulkExtrudeStripDescriptor> descriptors)
        {
            if (domainData == null || descriptors == null || descriptors.Count == 0)
            {
                return;
            }

            List<List<BulkExtrudeStripDescriptor>> batches = PartitionBulkExtrudeStrips(descriptors);
            for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
            {
                AppendBulkExtrudedSideStripBatch(domainData, batches[batchIndex]);
            }
        }

        private static List<List<BulkExtrudeStripDescriptor>> PartitionBulkExtrudeStrips(List<BulkExtrudeStripDescriptor> descriptors)
        {
            var batches = new List<List<BulkExtrudeStripDescriptor>>();
            for (int descriptorIndex = 0; descriptorIndex < descriptors.Count; descriptorIndex++)
            {
                BulkExtrudeStripDescriptor descriptor = descriptors[descriptorIndex];
                bool appended = false;
                for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
                {
                    List<BulkExtrudeStripDescriptor> batch = batches[batchIndex];
                    BulkExtrudeStripDescriptor seed = batch[0];
                    if (Mathf.Approximately(seed.extrudeDistance, descriptor.extrudeDistance)
                        && Mathf.Approximately(seed.extrudeInset, descriptor.extrudeInset))
                    {
                        batch.Add(descriptor);
                        appended = true;
                        break;
                    }
                }

                if (!appended)
                {
                    batches.Add(new List<BulkExtrudeStripDescriptor> { descriptor });
                }
            }

            return batches;
        }

        private void AppendBulkExtrudedSideStripBatch(DomainGeometryData domainData, List<BulkExtrudeStripDescriptor> descriptors)
        {
            if (domainData == null || descriptors == null || descriptors.Count == 0)
            {
                return;
            }

            DomainGeometryData baseGeometry = BuildBulkBottomStripGeometry(descriptors);
            if (!baseGeometry.HasGeometry)
            {
                return;
            }

            ulong baseHandle = 0;
            ulong extrudedHandle = 0;
            try
            {
                baseHandle = CreateTemporaryGeometryHandle(baseGeometry);
                if (baseHandle == 0)
                {
                    return;
                }

                uint[] primitiveIndices = new uint[baseGeometry.PrimitiveCount];
                for (int primitiveIndex = 0; primitiveIndex < primitiveIndices.Length; primitiveIndex++)
                {
                    primitiveIndices[primitiveIndex] = (uint)primitiveIndex;
                }

                extrudedHandle = NativeMethods.cunning_op_poly_extrude_prims(
                    baseHandle,
                    primitiveIndices,
                    (uint)primitiveIndices.Length,
                    descriptors[0].extrudeDistance,
                    descriptors[0].extrudeInset);
                if (extrudedHandle == 0)
                {
                    return;
                }

                AppendBulkExtrudedHandleGeometryToDomain(
                    domainData,
                    extrudedHandle,
                    descriptors,
                    baseGeometry.materialPalette.materials);
            }
            finally
            {
                if (extrudedHandle != 0)
                {
                    NativeMethods.cunning_release_handle(extrudedHandle);
                }

                if (baseHandle != 0)
                {
                    NativeMethods.cunning_release_handle(baseHandle);
                }
            }
        }

        private DomainGeometryData BuildBulkBottomStripGeometry(List<BulkExtrudeStripDescriptor> descriptors)
        {
            var domainData = new DomainGeometryData();
            for (int descriptorIndex = 0; descriptorIndex < descriptors.Count; descriptorIndex++)
            {
                BulkExtrudeStripDescriptor descriptor = descriptors[descriptorIndex];
                AppendBottomStripGeometry(
                    domainData,
                    descriptor.sourcePositions,
                    descriptor.strip,
                    descriptorIndex,
                    (int)descriptor.strip.source);
            }

            return domainData;
        }

        private void AppendBottomStripGeometry(
            DomainGeometryData domainData,
            List<Vector3> sourcePositions,
            CunningSideStripRange strip,
            int sourceStripIndex,
            int sourceSideKind)
        {
            if (domainData == null
                || sourcePositions == null
                || strip == null
                || strip.ringSize <= 1
                || strip.vertexCount < strip.ringSize * 2
                || strip.vertexStart < 0
                || strip.vertexStart + strip.vertexCount > sourcePositions.Count
                || (strip.vertexCount % strip.ringSize) != 0)
            {
                return;
            }

            int sampleCount = strip.vertexCount / strip.ringSize;
            int materialSlot = domainData.materialPalette.GetSlot(strip.material);
            bool isLeftSideStrip = IsLeftSideStrip(strip.source);
            int basePointOffset = domainData.positions.Count;
            int innerBandIndex = GetPolyExtrudeBaseInnerBandIndex(strip);
            int outerBandIndex = GetPolyExtrudeBaseOuterBandIndex(strip);

            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                int sourceBaseIndex = strip.vertexStart + sampleIndex * strip.ringSize;
                domainData.positions.Add(sourcePositions[sourceBaseIndex + innerBandIndex]);
                domainData.positions.Add(sourcePositions[sourceBaseIndex + outerBandIndex]);
            }

            for (int sampleIndex = 0; sampleIndex < sampleCount - 1; sampleIndex++)
            {
                int current = sampleIndex * 2;
                if (isLeftSideStrip)
                {
                    domainData.primitivePointIndices.Add(basePointOffset + current);
                    domainData.primitivePointIndices.Add(basePointOffset + current + 1);
                    domainData.primitivePointIndices.Add(basePointOffset + current + 3);
                    domainData.primitivePointIndices.Add(basePointOffset + current + 2);
                }
                else
                {
                    domainData.primitivePointIndices.Add(basePointOffset + current);
                    domainData.primitivePointIndices.Add(basePointOffset + current + 2);
                    domainData.primitivePointIndices.Add(basePointOffset + current + 3);
                    domainData.primitivePointIndices.Add(basePointOffset + current + 1);
                }

                domainData.primitiveOffsets.Add((uint)domainData.primitivePointIndices.Count);
                domainData.primitiveMaterialIndices.Add(materialSlot);
                domainData.primitiveAreaIndices.Add((int)strip.areaType);
                domainData.primitiveRoadIds.Add(PrimitiveRoadId);
                domainData.primitiveSegmentIds.Add(strip.segmentId);
                domainData.primitiveSourceStripIndices.Add(sourceStripIndex);
                domainData.primitiveSourceSideKinds.Add(sourceSideKind);
            }
        }

        private void AppendBulkExtrudedHandleGeometryToDomain(
            DomainGeometryData domainData,
            ulong handle,
            IReadOnlyList<BulkExtrudeStripDescriptor> descriptors,
            IReadOnlyList<Material> sourceMaterialPalette)
        {
            if (domainData == null || handle == 0 || descriptors == null || descriptors.Count == 0)
            {
                return;
            }

            Vector3[] handlePoints = CopyPointPositionsFromHandle(handle);
            int[] primitiveOffsets = CopyHandleU32Array(handle, NativeMethods.cunning_geo_copy_prim_point_offsets);
            int[] primitivePointIndices = CopyHandleU32Array(handle, NativeMethods.cunning_geo_copy_prim_point_indices);
            int[] primitiveMaterialIndices = CopyHandleIntAttribute(handle, 3u, "@unity_material_index");
            int[] primitiveAreaIndices = CopyHandleIntAttribute(handle, 3u, "@area_type_index");
            int[] primitiveSourceStripIndices = CopyHandleIntAttribute(handle, 3u, "@source_strip_index");
            if (handlePoints == null
                || handlePoints.Length == 0
                || primitiveOffsets == null
                || primitiveOffsets.Length < 2
                || primitivePointIndices == null
                || primitiveMaterialIndices == null
                || primitiveAreaIndices == null
                || primitiveSourceStripIndices == null)
            {
                return;
            }

            int basePointOffset = domainData.positions.Count;
            domainData.positions.AddRange(handlePoints);

            for (int primitiveIndex = 0; primitiveIndex < primitiveOffsets.Length - 1; primitiveIndex++)
            {
                int start = primitiveOffsets[primitiveIndex];
                int end = primitiveOffsets[primitiveIndex + 1];
                int polygonPointCount = end - start;
                if (polygonPointCount < 3 || primitiveIndex >= primitiveSourceStripIndices.Length)
                {
                    continue;
                }

                int sourceStripIndex = primitiveSourceStripIndices[primitiveIndex];
                if (sourceStripIndex < 0 || sourceStripIndex >= descriptors.Count)
                {
                    continue;
                }

                BulkExtrudeStripDescriptor descriptor = descriptors[sourceStripIndex];
                Material material = descriptor.strip.material;
                if (primitiveIndex < primitiveMaterialIndices.Length)
                {
                    int materialIndex = primitiveMaterialIndices[primitiveIndex];
                    if (materialIndex >= 0 && materialIndex < sourceMaterialPalette.Count)
                    {
                        material = sourceMaterialPalette[materialIndex];
                    }
                }

                CunningAreaType areaType = descriptor.strip.areaType;
                if (primitiveIndex < primitiveAreaIndices.Length && Enum.IsDefined(typeof(CunningAreaType), primitiveAreaIndices[primitiveIndex]))
                {
                    areaType = (CunningAreaType)primitiveAreaIndices[primitiveIndex];
                }

                Vector3[] polygonPoints = new Vector3[polygonPointCount];
                float sumAcross = 0.0f;
                Vector3 alongAxis = descriptor.alongAxis.sqrMagnitude > 1e-6f ? descriptor.alongAxis.normalized : Vector3.forward;
                Vector3 upAxis = Vector3.up;
                Vector3 acrossAxis = Vector3.Cross(upAxis, alongAxis);
                if (acrossAxis.sqrMagnitude <= 1e-8f)
                {
                    acrossAxis = Vector3.right;
                }
                acrossAxis.Normalize();

                float globalMinAcross = float.PositiveInfinity;
                float globalMaxAcross = float.NegativeInfinity;
                float globalMinHeight = float.PositiveInfinity;
                float globalMaxHeight = float.NegativeInfinity;
                for (int pointIndex = 0; pointIndex < descriptor.strip.vertexCount; pointIndex++)
                {
                    Vector3 basePoint = descriptor.sourcePositions[descriptor.strip.vertexStart + pointIndex];
                    Vector3 delta = basePoint - descriptor.origin;
                    float across = Vector3.Dot(delta, acrossAxis);
                    float height = Vector3.Dot(delta, upAxis);
                    float extrudedHeight = height + descriptor.extrudeDistance;
                    globalMinAcross = Mathf.Min(globalMinAcross, across);
                    globalMaxAcross = Mathf.Max(globalMaxAcross, across);
                    globalMinHeight = Mathf.Min(globalMinHeight, Mathf.Min(height, extrudedHeight));
                    globalMaxHeight = Mathf.Max(globalMaxHeight, Mathf.Max(height, extrudedHeight));
                }

                float globalAcrossSpan = Mathf.Max(0.0001f, globalMaxAcross - globalMinAcross);
                float globalHeightSpan = Mathf.Max(0.0001f, globalMaxHeight - globalMinHeight);

                for (int pointIndex = 0; pointIndex < polygonPointCount; pointIndex++)
                {
                    int localPointIndex = primitivePointIndices[start + pointIndex];
                    polygonPoints[pointIndex] = handlePoints[localPointIndex];
                    sumAcross += Vector3.Dot(handlePoints[localPointIndex] - descriptor.origin, acrossAxis);
                }

                Vector3 polygonNormal = ComputePolygonNormal(polygonPoints);
                if (polygonNormal.sqrMagnitude <= 1e-8f)
                {
                    continue;
                }

                bool isTopFace = Mathf.Abs(Vector3.Dot(polygonNormal.normalized, upAxis)) > 0.8f;
                Vector3 faceAcrossAxis = Vector3.Cross(polygonNormal, alongAxis);
                if (faceAcrossAxis.sqrMagnitude <= 1e-8f && polygonPointCount >= 2)
                {
                    Vector3 fallbackEdge = polygonPoints[1] - polygonPoints[0];
                    faceAcrossAxis = Vector3.Cross(polygonNormal, fallbackEdge.normalized);
                }

                if (faceAcrossAxis.sqrMagnitude <= 1e-8f)
                {
                    faceAcrossAxis = Vector3.right;
                }
                faceAcrossAxis.Normalize();

                float minAcross = float.PositiveInfinity;
                float maxAcross = float.NegativeInfinity;
                for (int pointIndex = 0; pointIndex < polygonPointCount; pointIndex++)
                {
                    float across = Vector3.Dot(polygonPoints[pointIndex] - descriptor.origin, faceAcrossAxis);
                    minAcross = Mathf.Min(minAcross, across);
                    maxAcross = Mathf.Max(maxAcross, across);
                }

                float acrossSpan = Mathf.Max(0.0001f, maxAcross - minAcross);
                float averageAcross = sumAcross / polygonPointCount;
                bool isLongitudinalSide = !isTopFace && IsFaceLongitudinal(polygonPoints, alongAxis);
                bool isOuterSide = Mathf.Abs(averageAcross) > (globalAcrossSpan * 0.5f);
                int materialSlot = domainData.materialPalette.GetSlot(material);

                for (int pointIndex = 0; pointIndex < polygonPointCount; pointIndex++)
                {
                    int localPointIndex = primitivePointIndices[start + pointIndex];
                    Vector3 point = handlePoints[localPointIndex];
                    float across = Vector3.Dot(point - descriptor.origin, faceAcrossAxis);
                    float along = Vector3.Dot(point - descriptor.origin, alongAxis);
                    float localAcross01 = Mathf.Clamp01((across - minAcross) / acrossSpan);
                    float along01 = Mathf.InverseLerp(descriptor.sourceAlongStartDistance, descriptor.sourceAlongEndDistance, along);
                    float height01 = Mathf.Clamp01((Vector3.Dot(point - descriptor.origin, upAxis) - globalMinHeight) / globalHeightSpan);

                    domainData.primitivePointIndices.Add(basePointOffset + localPointIndex);
                    domainData.vertexNormals.Add(polygonNormal);
                    domainData.vertexUv.Add(
                        BuildExtrudedAreaUv(
                            descriptor.uvFrame,
                            isTopFace,
                            isLongitudinalSide,
                            isOuterSide,
                            along01,
                            localAcross01,
                            localAcross01,
                            height01));
                }

                domainData.primitiveOffsets.Add((uint)domainData.primitivePointIndices.Count);
                domainData.primitiveMaterialIndices.Add(materialSlot);
                domainData.primitiveAreaIndices.Add((int)areaType);
                domainData.primitiveRoadIds.Add(PrimitiveRoadId);
                domainData.primitiveSegmentIds.Add(descriptor.strip.segmentId);
            }
        }

        private void AppendPolyExtrudedSideStrip(
            DomainGeometryData domainData,
            List<Vector3> sourcePositions,
            CunningSideStripRange strip)
        {
            if (domainData == null || sourcePositions == null)
            {
                return;
            }

            LoftRoadExtensionData roadData = GetRoadDataOrNull(strip.splineIndex);
            if (roadData == null)
            {
                return;
            }

            DomainGeometryData baseGeometry = BuildBottomStripGeometry(sourcePositions, strip);
            if (!baseGeometry.HasGeometry)
            {
                return;
            }

            int baseInnerBandIndex = GetPolyExtrudeBaseInnerBandIndex(strip);
            if (!TryGetStripFrame(sourcePositions, strip.vertexStart, strip.vertexCount, strip.ringSize, baseInnerBandIndex, out Vector3 stripOrigin, out Vector3 stripAlongAxis))
            {
                return;
            }

            ulong baseHandle = 0;
            ulong extrudedHandle = 0;

            try
            {
                baseHandle = CreateTemporaryGeometryHandle(baseGeometry);
                if (baseHandle == 0)
                {
                    return;
                }

                float extrudeDistance = GetPolyExtrudeSignedDistance(roadData, strip);
                float extrudeInset = GetPolyExtrudeInset(roadData, strip.areaType);
                extrudedHandle = NativeMethods.cunning_op_poly_extrude(baseHandle, extrudeDistance, extrudeInset);
                if (extrudedHandle == 0)
                {
                    return;
                }

                AppendHandleGeometryToDomain(
                    domainData,
                    extrudedHandle,
                    baseGeometry.PrimitiveCount,
                    strip.material,
                    strip.areaType,
                    stripOrigin,
                    stripAlongAxis,
                    GetAreaUvScaleU(roadData, strip.areaType),
                    GetAreaUvScaleV(roadData, strip.areaType),
                    GetAreaUvOffsetU(roadData, strip.areaType));
            }
            finally
            {
                if (extrudedHandle != 0)
                {
                    NativeMethods.cunning_release_handle(extrudedHandle);
                }

                if (baseHandle != 0)
                {
                    NativeMethods.cunning_release_handle(baseHandle);
                }
            }
        }

        private DomainGeometryData BuildBottomStripGeometry(List<Vector3> sourcePositions, CunningSideStripRange strip)
        {
            var domainData = new DomainGeometryData();
            AppendBottomStripGeometry(domainData, sourcePositions, strip, 0, (int)strip.source);
            return domainData;
        }

        private static bool IsLeftSideStrip(CunningSideStripSource source)
        {
            switch (source)
            {
                case CunningSideStripSource.CurbLeft:
                case CunningSideStripSource.RoadEdgeLeft:
                case CunningSideStripSource.RoadEdgeOuterLeft:
                case CunningSideStripSource.SidewalkLeft:
                    return true;
                default:
                    return false;
            }
        }

        private ulong CreateTemporaryGeometryHandle(DomainGeometryData domainData)
        {
            if (domainData == null || !domainData.HasGeometry)
            {
                return 0;
            }

            ulong handle = NativeMethods.cunning_geo_create();
            if (handle == 0)
            {
                return 0;
            }

            try
            {
                EnsureBufferCapacity(ref m_CunningPointIdBuffer, domainData.positions.Count);
                uint addedPoints = NativeMethods.cunning_geo_add_points_bulk_nosync(
                    handle,
                    CopyVector3ListToBuffer(domainData.positions, ref m_CunningFlatPositionsBuffer),
                    (uint)domainData.positions.Count,
                    m_CunningPointIdBuffer);
                if (addedPoints != domainData.positions.Count)
                {
                    NativeMethods.cunning_release_handle(handle);
                    return 0;
                }

                EnsureBufferCapacity(ref m_CunningPolyPointIdBuffer, domainData.primitivePointIndices.Count);
                EnsureBufferCapacity(ref m_CunningPolyOffsetBuffer, domainData.primitiveOffsets.Count);
                for (int pointIndex = 0; pointIndex < domainData.primitivePointIndices.Count; pointIndex++)
                {
                    m_CunningPolyPointIdBuffer[pointIndex] = m_CunningPointIdBuffer[domainData.primitivePointIndices[pointIndex]];
                }

                for (int offsetIndex = 0; offsetIndex < domainData.primitiveOffsets.Count; offsetIndex++)
                {
                    m_CunningPolyOffsetBuffer[offsetIndex] = domainData.primitiveOffsets[offsetIndex];
                }

                uint addedPrimitives = NativeMethods.cunning_geo_add_polys_bulk_nosync(
                    handle,
                    m_CunningPolyPointIdBuffer,
                    m_CunningPolyOffsetBuffer,
                    (uint)domainData.PrimitiveCount);
                if (addedPrimitives != domainData.PrimitiveCount)
                {
                    NativeMethods.cunning_release_handle(handle);
                    return 0;
                }

                EnsureBufferCapacity(ref m_CunningPrimitiveMaterialIndexBuffer, domainData.primitiveMaterialIndices.Count);
                EnsureBufferCapacity(ref m_CunningPrimitiveAreaIndexBuffer, domainData.primitiveAreaIndices.Count);
                for (int primitiveIndex = 0; primitiveIndex < domainData.primitiveMaterialIndices.Count; primitiveIndex++)
                {
                    m_CunningPrimitiveMaterialIndexBuffer[primitiveIndex] = domainData.primitiveMaterialIndices[primitiveIndex];
                    m_CunningPrimitiveAreaIndexBuffer[primitiveIndex] = domainData.primitiveAreaIndices[primitiveIndex];
                }

                if (domainData.primitiveMaterialIndices.Count > 0)
                {
                    uint setMaterialIndices = NativeMethods.cunning_geo_set_primitive_attr_i32_nosync(
                        handle,
                        "@unity_material_index",
                        m_CunningPrimitiveMaterialIndexBuffer,
                        (uint)domainData.primitiveMaterialIndices.Count);
                    if (setMaterialIndices == 0)
                    {
                        NativeMethods.cunning_release_handle(handle);
                        return 0;
                    }
                }

                if (domainData.primitiveAreaIndices.Count > 0)
                {
                    uint setAreaIndices = NativeMethods.cunning_geo_set_primitive_attr_i32_nosync(
                        handle,
                        "@area_type_index",
                        m_CunningPrimitiveAreaIndexBuffer,
                        (uint)domainData.primitiveAreaIndices.Count);
                    if (setAreaIndices == 0)
                    {
                        NativeMethods.cunning_release_handle(handle);
                        return 0;
                    }
                }

                if (domainData.primitiveSourceStripIndices.Count > 0)
                {
                    EnsureBufferCapacity(ref m_CunningPrimitiveSourceStripIndexBuffer, domainData.primitiveSourceStripIndices.Count);
                    for (int primitiveIndex = 0; primitiveIndex < domainData.primitiveSourceStripIndices.Count; primitiveIndex++)
                    {
                        m_CunningPrimitiveSourceStripIndexBuffer[primitiveIndex] = domainData.primitiveSourceStripIndices[primitiveIndex];
                    }

                    uint setSourceStripIndices = NativeMethods.cunning_geo_set_primitive_attr_i32_nosync(
                        handle,
                        "@source_strip_index",
                        m_CunningPrimitiveSourceStripIndexBuffer,
                        (uint)domainData.primitiveSourceStripIndices.Count);
                    if (setSourceStripIndices == 0)
                    {
                        NativeMethods.cunning_release_handle(handle);
                        return 0;
                    }
                }

                if (domainData.primitiveSourceSideKinds.Count > 0)
                {
                    EnsureBufferCapacity(ref m_CunningPrimitiveSourceSideKindBuffer, domainData.primitiveSourceSideKinds.Count);
                    for (int primitiveIndex = 0; primitiveIndex < domainData.primitiveSourceSideKinds.Count; primitiveIndex++)
                    {
                        m_CunningPrimitiveSourceSideKindBuffer[primitiveIndex] = domainData.primitiveSourceSideKinds[primitiveIndex];
                    }

                    uint setSourceSideKinds = NativeMethods.cunning_geo_set_primitive_attr_i32_nosync(
                        handle,
                        "@source_side_kind",
                        m_CunningPrimitiveSourceSideKindBuffer,
                        (uint)domainData.primitiveSourceSideKinds.Count);
                    if (setSourceSideKinds == 0)
                    {
                        NativeMethods.cunning_release_handle(handle);
                        return 0;
                    }
                }

                if (NativeMethods.cunning_geo_sync_cache(handle) == 0)
                {
                    NativeMethods.cunning_release_handle(handle);
                    return 0;
                }

                return handle;
            }
            catch
            {
                NativeMethods.cunning_release_handle(handle);
                throw;
            }
        }

        private void AppendHandleGeometryToDomain(
            DomainGeometryData domainData,
            ulong handle,
            int frontPrimitiveCount,
            Material material,
            CunningAreaType areaType,
            Vector3 stripOrigin,
            Vector3 stripAlongAxis,
            float uvScaleU,
            float uvScaleV,
            float uvOffsetU)
        {
            if (domainData == null || handle == 0)
            {
                return;
            }

            Vector3[] handlePoints = CopyPointPositionsFromHandle(handle);
            int[] primitiveOffsets = CopyHandleU32Array(handle, NativeMethods.cunning_geo_copy_prim_point_offsets);
            int[] primitivePointIndices = CopyHandleU32Array(handle, NativeMethods.cunning_geo_copy_prim_point_indices);
            if (handlePoints == null
                || handlePoints.Length == 0
                || primitiveOffsets == null
                || primitiveOffsets.Length < 2
                || primitivePointIndices == null)
            {
                return;
            }

            int basePointOffset = domainData.positions.Count;
            domainData.positions.AddRange(handlePoints);
            int materialSlot = domainData.materialPalette.GetSlot(material);

            Vector3 alongAxis = stripAlongAxis.sqrMagnitude > 1e-6f ? stripAlongAxis.normalized : Vector3.forward;
            Vector3 upAxis = Vector3.up;
            Vector3 acrossAxis = Vector3.Cross(upAxis, alongAxis);
            if (acrossAxis.sqrMagnitude <= 1e-8f)
            {
                acrossAxis = Vector3.right;
            }
            acrossAxis.Normalize();

            float globalMinAcross = float.PositiveInfinity;
            float globalMaxAcross = float.NegativeInfinity;
            float globalMinHeight = float.PositiveInfinity;
            float globalMaxHeight = float.NegativeInfinity;
            for (int pointIndex = 0; pointIndex < handlePoints.Length; pointIndex++)
            {
                Vector3 delta = handlePoints[pointIndex] - stripOrigin;
                float across = Vector3.Dot(delta, acrossAxis);
                float height = Vector3.Dot(delta, upAxis);
                globalMinAcross = Mathf.Min(globalMinAcross, across);
                globalMaxAcross = Mathf.Max(globalMaxAcross, across);
                globalMinHeight = Mathf.Min(globalMinHeight, height);
                globalMaxHeight = Mathf.Max(globalMaxHeight, height);
            }

            float globalAcrossSpan = Mathf.Max(0.0001f, globalMaxAcross - globalMinAcross);
            float globalHeightSpan = Mathf.Max(0.0001f, globalMaxHeight - globalMinHeight);

            for (int primitiveIndex = 0; primitiveIndex < primitiveOffsets.Length - 1; primitiveIndex++)
            {
                int start = primitiveOffsets[primitiveIndex];
                int end = primitiveOffsets[primitiveIndex + 1];
                int polygonPointCount = end - start;
                if (polygonPointCount < 3)
                {
                    continue;
                }

                Vector3[] polygonPoints = new Vector3[polygonPointCount];
                float sumAcross = 0.0f;
                for (int pointIndex = 0; pointIndex < polygonPointCount; pointIndex++)
                {
                    int localPointIndex = primitivePointIndices[start + pointIndex];
                    polygonPoints[pointIndex] = handlePoints[localPointIndex];
                    sumAcross += Vector3.Dot(handlePoints[localPointIndex] - stripOrigin, acrossAxis);
                }

                Vector3 polygonNormal = ComputePolygonNormal(polygonPoints);
                if (polygonNormal.sqrMagnitude <= 1e-8f)
                {
                    continue;
                }

                bool isTopFace = primitiveIndex < frontPrimitiveCount || Mathf.Abs(Vector3.Dot(polygonNormal.normalized, upAxis)) > 0.8f;

                Vector3 faceAcrossAxis = Vector3.Cross(polygonNormal, alongAxis);
                if (faceAcrossAxis.sqrMagnitude <= 1e-8f && polygonPointCount >= 2)
                {
                    Vector3 fallbackEdge = polygonPoints[1] - polygonPoints[0];
                    faceAcrossAxis = Vector3.Cross(polygonNormal, fallbackEdge.normalized);
                }

                if (faceAcrossAxis.sqrMagnitude <= 1e-8f)
                {
                    faceAcrossAxis = Vector3.right;
                }
                faceAcrossAxis.Normalize();

                float minAcross = float.PositiveInfinity;
                float maxAcross = float.NegativeInfinity;
                for (int pointIndex = 0; pointIndex < polygonPointCount; pointIndex++)
                {
                    float across = Vector3.Dot(polygonPoints[pointIndex] - stripOrigin, faceAcrossAxis);
                    minAcross = Mathf.Min(minAcross, across);
                    maxAcross = Mathf.Max(maxAcross, across);
                }

                float acrossSpan = Mathf.Max(0.0001f, maxAcross - minAcross);
                float averageAcross = sumAcross / polygonPointCount;
                bool isLongitudinalSide = !isTopFace && IsFaceLongitudinal(polygonPoints, alongAxis);
                bool isOuterSide = Mathf.Abs(averageAcross) > (globalAcrossSpan * 0.5f);

                for (int pointIndex = 0; pointIndex < polygonPointCount; pointIndex++)
                {
                    int localPointIndex = primitivePointIndices[start + pointIndex];
                    Vector3 point = handlePoints[localPointIndex];
                    float across = Vector3.Dot(point - stripOrigin, faceAcrossAxis);
                    float along = Vector3.Dot(point - stripOrigin, alongAxis);
                    float topAcross = Vector3.Dot(point - stripOrigin, acrossAxis);
                    float topAcross01 = Mathf.Clamp01((topAcross - globalMinAcross) / globalAcrossSpan);
                    float height01 = Mathf.Clamp01((Vector3.Dot(point - stripOrigin, upAxis) - globalMinHeight) / globalHeightSpan);

                    domainData.primitivePointIndices.Add(basePointOffset + localPointIndex);
                    domainData.vertexNormals.Add(polygonNormal);
                    domainData.vertexUv.Add(
                        BuildExtrudedAreaUv(
                            areaType,
                            isTopFace,
                            isLongitudinalSide,
                            isOuterSide,
                            along,
                            topAcross01,
                            (across - minAcross) / acrossSpan,
                            height01,
                            uvScaleU,
                            uvScaleV,
                            uvOffsetU));
                }

                domainData.primitiveOffsets.Add((uint)domainData.primitivePointIndices.Count);
                domainData.primitiveMaterialIndices.Add(materialSlot);
                domainData.primitiveAreaIndices.Add((int)areaType);
                domainData.primitiveRoadIds.Add(PrimitiveRoadId);
                domainData.primitiveSegmentIds.Add(m_CurrentProcessingSegmentId);
            }
        }

        private Vector3[] CopyPointPositionsFromHandle(ulong handle)
        {
            int pointCount = (int)NativeMethods.cunning_geo_get_point_count(handle);
            if (pointCount <= 0)
            {
                return Array.Empty<Vector3>();
            }

            float[] rawPoints = new float[pointCount * 3];
            GCHandle rawHandle = GCHandle.Alloc(rawPoints, GCHandleType.Pinned);
            try
            {
                NativeMethods.cunning_geo_copy_points(handle, rawHandle.AddrOfPinnedObject());
            }
            finally
            {
                rawHandle.Free();
            }

            var result = new Vector3[pointCount];
            for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                int baseIndex = pointIndex * 3;
                result[pointIndex] = new Vector3(rawPoints[baseIndex], rawPoints[baseIndex + 1], rawPoints[baseIndex + 2]);
            }

            return result;
        }

        private static int[] CopyHandleU32Array(ulong handle, Func<ulong, IntPtr, uint> copyFunc)
        {
            int count = (int)copyFunc(handle, IntPtr.Zero);
            if (count <= 0)
            {
                return Array.Empty<int>();
            }

            uint[] rawValues = new uint[count];
            GCHandle rawHandle = GCHandle.Alloc(rawValues, GCHandleType.Pinned);
            try
            {
                copyFunc(handle, rawHandle.AddrOfPinnedObject());
            }
            finally
            {
                rawHandle.Free();
            }

            var result = new int[count];
            for (int index = 0; index < count; index++)
            {
                result[index] = (int)rawValues[index];
            }

            return result;
        }

        private static int[] CopyHandleIntAttribute(ulong handle, uint attrClass, string attrName)
        {
            uint count = NativeMethods.cunning_geo_get_attr_len(handle, attrClass, attrName);
            if (count == 0)
            {
                return Array.Empty<int>();
            }

            int[] rawValues = new int[count];
            GCHandle rawHandle = GCHandle.Alloc(rawValues, GCHandleType.Pinned);
            try
            {
                uint copied = NativeMethods.cunning_geo_copy_attr_i32(
                    handle,
                    attrClass,
                    attrName,
                    rawHandle.AddrOfPinnedObject(),
                    count);
                if (copied == 0)
                {
                    return Array.Empty<int>();
                }
            }
            finally
            {
                rawHandle.Free();
            }

            return rawValues;
        }

        private static Vector3 ComputePolygonNormal(IReadOnlyList<Vector3> points)
        {
            Vector3 normal = Vector3.zero;
            if (points == null || points.Count < 3)
            {
                return normal;
            }

            for (int pointIndex = 0; pointIndex < points.Count; pointIndex++)
            {
                Vector3 current = points[pointIndex];
                Vector3 next = points[(pointIndex + 1) % points.Count];
                normal.x += (current.y - next.y) * (current.z + next.z);
                normal.y += (current.z - next.z) * (current.x + next.x);
                normal.z += (current.x - next.x) * (current.y + next.y);
            }

            if (normal.sqrMagnitude <= 1e-8f)
            {
                Vector3 edge0 = points[1] - points[0];
                Vector3 edge1 = points[2] - points[0];
                normal = Vector3.Cross(edge0, edge1);
            }

            return normal.normalized;
        }

        private static bool TryGetStripFrame(
            List<Vector3> sourcePositions,
            int vertexStart,
            int vertexCount,
            int ringSize,
            int bandIndex,
            out Vector3 origin,
            out Vector3 alongAxis)
        {
            origin = Vector3.zero;
            alongAxis = Vector3.forward;

            if (sourcePositions == null
                || ringSize <= 1
                || vertexCount < ringSize * 2
                || (vertexCount % ringSize) != 0
                || vertexStart < 0
                || vertexStart + vertexCount > sourcePositions.Count)
            {
                return false;
            }

            int sampleCount = vertexCount / ringSize;
            int firstIndex = vertexStart + bandIndex;
            int secondIndex = vertexStart + ringSize + bandIndex;
            if (secondIndex >= sourcePositions.Count)
            {
                return false;
            }

            origin = sourcePositions[firstIndex];
            alongAxis = sourcePositions[secondIndex] - sourcePositions[firstIndex];
            if (alongAxis.sqrMagnitude <= 1e-8f)
            {
                int lastIndex = vertexStart + (sampleCount - 1) * ringSize + bandIndex;
                alongAxis = sourcePositions[lastIndex] - sourcePositions[firstIndex];
            }

            if (alongAxis.sqrMagnitude <= 1e-8f)
            {
                return false;
            }

            alongAxis.Normalize();
            return true;
        }

        private static float GetPolyExtrudeDistance(LoftRoadExtensionData roadData, CunningAreaType areaType)
        {
            switch (areaType)
            {
                case CunningAreaType.Curb:
                    return Mathf.Max(0.0f, roadData.curbHeight);
                case CunningAreaType.RoadEdge:
                case CunningAreaType.RoadEdgeOuter:
                    return Mathf.Max(0.0f, roadData.roadEdgeHeight);
                default:
                    return 0.0f;
            }
        }

        private static bool ShouldUseTopCapForPolyExtrude(CunningSideStripRange strip)
        {
            if (strip == null || strip.ringSize < 4)
            {
                return false;
            }

            switch (strip.areaType)
            {
                case CunningAreaType.Curb:
                case CunningAreaType.RoadEdge:
                case CunningAreaType.RoadEdgeOuter:
                    return true;
                default:
                    return false;
            }
        }

        private static int GetPolyExtrudeBaseInnerBandIndex(CunningSideStripRange strip)
        {
            return ShouldUseTopCapForPolyExtrude(strip)
                ? GetPreferredTopInnerBandIndex(strip)
                : 0;
        }

        private static int GetPolyExtrudeBaseOuterBandIndex(CunningSideStripRange strip)
        {
            return ShouldUseTopCapForPolyExtrude(strip)
                ? GetPreferredTopOuterBandIndex(strip)
                : 1;
        }

        private static float GetPolyExtrudeSignedDistance(LoftRoadExtensionData roadData, CunningSideStripRange strip)
        {
            if (strip == null)
            {
                return 0.0f;
            }

            float distance = GetPolyExtrudeDistance(roadData, strip.areaType);
            return ShouldUseTopCapForPolyExtrude(strip) ? -distance : distance;
        }

        private static float GetPolyExtrudeInset(LoftRoadExtensionData roadData, CunningAreaType areaType)
        {
            switch (areaType)
            {
                case CunningAreaType.Curb:
                case CunningAreaType.RoadEdge:
                case CunningAreaType.RoadEdgeOuter:
                default:
                    return 0.0f;
            }
        }

        private static float GetAreaUvScaleU(LoftRoadExtensionData roadData, CunningAreaType areaType)
        {
            switch (areaType)
            {
                case CunningAreaType.Curb:
                    return roadData.curbUVScaleU;
                case CunningAreaType.RoadEdge:
                case CunningAreaType.RoadEdgeOuter:
                    return roadData.roadEdgeUVScaleU;
                default:
                    return 1.0f;
            }
        }

        private static float GetAreaUvOffsetU(LoftRoadExtensionData roadData, CunningAreaType areaType)
        {
            switch (areaType)
            {
                case CunningAreaType.Sidewalk:
                    return roadData.uvHorizontalOffset;
                default:
                    return 0.0f;
            }
        }

        private static float GetAreaUvScaleV(LoftRoadExtensionData roadData, CunningAreaType areaType)
        {
            switch (areaType)
            {
                case CunningAreaType.Curb:
                    return roadData.curbUVScaleV;
                case CunningAreaType.RoadEdge:
                case CunningAreaType.RoadEdgeOuter:
                    return roadData.roadEdgeUVScaleV;
                default:
                    return 1.0f;
            }
        }

        private static Vector2 BuildExtrudedAreaUv(
            StripUvFrame frame,
            bool isTopFace,
            bool isLongitudinalSide,
            bool isOuterSide,
            float along01,
            float topAcross01,
            float localAcross01,
            float height01)
        {
            float alongCoord = Mathf.Lerp(frame.alongStart, frame.alongEnd, along01);
            float topAcrossCoord = Mathf.Lerp(
                frame.topInnerAcross,
                frame.topOuterAcross,
                frame.flip ? 1.0f - topAcross01 : topAcross01);

            if (isTopFace)
            {
                return frame.alongInU
                    ? new Vector2(alongCoord, topAcrossCoord)
                    : new Vector2(topAcrossCoord, alongCoord);
            }

            if (isLongitudinalSide)
            {
                float sideStart = isOuterSide ? frame.topOuterAcross : frame.sideMinAcross;
                float sideEnd = isOuterSide ? frame.sideMaxAcross : frame.topInnerAcross;
                float sideAcrossCoord = Mathf.Lerp(sideStart, sideEnd, height01);
                return frame.alongInU
                    ? new Vector2(alongCoord, sideAcrossCoord)
                    : new Vector2(sideAcrossCoord, alongCoord);
            }

            float capAcrossCoord = Mathf.Lerp(
                frame.sideMinAcross,
                frame.sideMaxAcross,
                frame.flip ? 1.0f - localAcross01 : localAcross01);
            float capHeightCoord = Mathf.Lerp(frame.sideMinAcross, frame.topOuterAcross, height01);
            return new Vector2(capAcrossCoord, capHeightCoord);
        }

        private static Vector2 BuildExtrudedAreaUv(
            CunningAreaType areaType,
            bool isTopFace,
            bool isLongitudinalSide,
            bool isOuterSide,
            float along,
            float topAcross01,
            float localAcross01,
            float height01,
            float uvScaleU,
            float uvScaleV,
            float uvOffsetU)
        {
            bool alongInU = areaType == CunningAreaType.RoadEdge || areaType == CunningAreaType.RoadEdgeOuter;
            float alongCoord = along * 0.1f * uvScaleV;

            if (isTopFace)
            {
                float profile = Mathf.Lerp(0.3f, 0.7f, topAcross01) * uvScaleU;
                return alongInU
                    ? new Vector2(alongCoord, profile)
                    : new Vector2(profile + uvOffsetU, alongCoord);
            }

            if (isLongitudinalSide)
            {
                float bandStart = isOuterSide ? 0.7f : 0.0f;
                float bandEnd = isOuterSide ? 1.0f : 0.3f;
                float profile = Mathf.Lerp(bandStart, bandEnd, height01) * uvScaleU;
                return alongInU
                    ? new Vector2(alongCoord, profile)
                    : new Vector2(profile + uvOffsetU, alongCoord);
            }

            float capAcross = localAcross01 * uvScaleU;
            float capHeight = height01 * uvScaleV;
            return alongInU
                ? new Vector2(capAcross, capHeight)
                : new Vector2(capAcross + uvOffsetU, capHeight);
        }

        private static bool IsFaceLongitudinal(IReadOnlyList<Vector3> polygonPoints, Vector3 alongAxis)
        {
            if (polygonPoints == null || polygonPoints.Count < 2)
            {
                return false;
            }

            Vector3 edge = Vector3.zero;
            for (int pointIndex = 0; pointIndex < polygonPoints.Count; pointIndex++)
            {
                Vector3 current = polygonPoints[pointIndex];
                Vector3 next = polygonPoints[(pointIndex + 1) % polygonPoints.Count];
                Vector3 candidate = next - current;
                if (candidate.sqrMagnitude > edge.sqrMagnitude)
                {
                    edge = candidate;
                }
            }

            if (edge.sqrMagnitude <= 1e-8f)
            {
                return false;
            }

            return Mathf.Abs(Vector3.Dot(edge.normalized, alongAxis.normalized)) > 0.6f;
        }

        private static int[] GetBandPairsForSideStrip(CunningSideStripRange strip)
        {
            switch (strip.areaType)
            {
                case CunningAreaType.Sidewalk:
                    return new[] { 0, 1 };
                case CunningAreaType.Curb:
                case CunningAreaType.RoadEdge:
                    return new[] { 0, 1, 3, 4, 0, 2, 2, 3, 1, 4 };
                case CunningAreaType.RoadEdgeOuter:
                    return new[] { 0, 1, 3, 4, 0, 3, 1, 2, 2, 4 };
                default:
                    return new[] { 0, 1 };
            }
        }

        private void GetSideStripSourceLists(
            CunningSideStripSource source,
            out List<Vector3> positions,
            out List<Vector3> normals,
            out List<Vector2> textures)
        {
            switch (source)
            {
                case CunningSideStripSource.SidewalkLeft:
                    positions = m_SidewalkLeftPositions;
                    normals = m_SidewalkLeftNormals;
                    textures = m_SidewalkLeftTextures;
                    break;
                case CunningSideStripSource.SidewalkRight:
                    positions = m_SidewalkRightPositions;
                    normals = m_SidewalkRightNormals;
                    textures = m_SidewalkRightTextures;
                    break;
                case CunningSideStripSource.CurbLeft:
                    positions = m_CurbLeftPositions;
                    normals = m_CurbLeftNormals;
                    textures = m_CurbLeftTextures;
                    break;
                case CunningSideStripSource.CurbRight:
                    positions = m_CurbRightPositions;
                    normals = m_CurbRightNormals;
                    textures = m_CurbRightTextures;
                    break;
                case CunningSideStripSource.RoadEdgeLeft:
                    positions = m_RoadEdgeLeftPositions;
                    normals = m_RoadEdgeLeftNormals;
                    textures = m_RoadEdgeLeftTextures;
                    break;
                case CunningSideStripSource.RoadEdgeRight:
                    positions = m_RoadEdgeRightPositions;
                    normals = m_RoadEdgeRightNormals;
                    textures = m_RoadEdgeRightTextures;
                    break;
                case CunningSideStripSource.RoadEdgeOuterLeft:
                    positions = m_RoadEdgeOuterLeftPositions;
                    normals = m_RoadEdgeOuterLeftNormals;
                    textures = m_RoadEdgeOuterLeftTextures;
                    break;
                case CunningSideStripSource.RoadEdgeOuterRight:
                    positions = m_RoadEdgeOuterRightPositions;
                    normals = m_RoadEdgeOuterRightNormals;
                    textures = m_RoadEdgeOuterRightTextures;
                    break;
                default:
                    positions = null;
                    normals = null;
                    textures = null;
                    break;
            }
        }

        private static void GetSideStripSourceLists(
            SplineGeometryCache cache,
            CunningSideStripSource source,
            out List<Vector3> positions,
            out List<Vector3> normals,
            out List<Vector2> textures)
        {
            if (cache == null)
            {
                positions = null;
                normals = null;
                textures = null;
                return;
            }

            switch (source)
            {
                case CunningSideStripSource.SidewalkLeft:
                    positions = cache.sidewalkLeftPositions;
                    normals = cache.sidewalkLeftNormals;
                    textures = cache.sidewalkLeftTextures;
                    break;
                case CunningSideStripSource.SidewalkRight:
                    positions = cache.sidewalkRightPositions;
                    normals = cache.sidewalkRightNormals;
                    textures = cache.sidewalkRightTextures;
                    break;
                case CunningSideStripSource.CurbLeft:
                    positions = cache.curbLeftPositions;
                    normals = cache.curbLeftNormals;
                    textures = cache.curbLeftTextures;
                    break;
                case CunningSideStripSource.CurbRight:
                    positions = cache.curbRightPositions;
                    normals = cache.curbRightNormals;
                    textures = cache.curbRightTextures;
                    break;
                case CunningSideStripSource.RoadEdgeLeft:
                    positions = cache.roadEdgeLeftPositions;
                    normals = cache.roadEdgeLeftNormals;
                    textures = cache.roadEdgeLeftTextures;
                    break;
                case CunningSideStripSource.RoadEdgeRight:
                    positions = cache.roadEdgeRightPositions;
                    normals = cache.roadEdgeRightNormals;
                    textures = cache.roadEdgeRightTextures;
                    break;
                case CunningSideStripSource.RoadEdgeOuterLeft:
                    positions = cache.roadEdgeOuterLeftPositions;
                    normals = cache.roadEdgeOuterLeftNormals;
                    textures = cache.roadEdgeOuterLeftTextures;
                    break;
                case CunningSideStripSource.RoadEdgeOuterRight:
                    positions = cache.roadEdgeOuterRightPositions;
                    normals = cache.roadEdgeOuterRightNormals;
                    textures = cache.roadEdgeOuterRightTextures;
                    break;
                default:
                    positions = null;
                    normals = null;
                    textures = null;
                    break;
            }
        }

        private void AppendStripRangeAsQuads(
            DomainGeometryData domainData,
            List<Vector3> sourcePositions,
            List<Vector3> sourceNormals,
            List<Vector2> sourceTextures,
            int vertexStart,
            int vertexCount,
            int ringSize,
            Material material,
            CunningAreaType areaType,
            int[] bandPairs,
            int roadId,
            int segmentId)
        {
            if (domainData == null || sourcePositions == null || vertexCount < ringSize * 2 || ringSize <= 1)
            {
                return;
            }

            if (vertexStart < 0 || vertexStart + vertexCount > sourcePositions.Count || (vertexCount % ringSize) != 0)
            {
                return;
            }

            int sampleCount = vertexCount / ringSize;
            int basePointOffset = domainData.positions.Count;
            for (int sourceIndex = 0; sourceIndex < vertexCount; sourceIndex++)
            {
                domainData.positions.Add(sourcePositions[vertexStart + sourceIndex]);
            }

            int materialSlot = domainData.materialPalette.GetSlot(material);
            for (int sampleIndex = 0; sampleIndex < sampleCount - 1; sampleIndex++)
            {
                int currentSampleOffset = sampleIndex * ringSize;
                int nextSampleOffset = currentSampleOffset + ringSize;
                for (int bandIndex = 0; bandIndex < bandPairs.Length; bandIndex += 2)
                {
                    AppendQuadFace(
                        domainData,
                        basePointOffset,
                        vertexStart,
                        sourceNormals,
                        sourceTextures,
                        currentSampleOffset + bandPairs[bandIndex],
                        nextSampleOffset + bandPairs[bandIndex],
                        nextSampleOffset + bandPairs[bandIndex + 1],
                        currentSampleOffset + bandPairs[bandIndex + 1],
                        materialSlot,
                        areaType,
                        roadId,
                        segmentId);
                }
            }
        }

        private void AppendQuadFace(
            DomainGeometryData domainData,
            int basePointOffset,
            int sourceStart,
            List<Vector3> sourceNormals,
            List<Vector2> sourceTextures,
            int point0,
            int point1,
            int point2,
            int point3,
            int materialSlot,
            CunningAreaType areaType,
            int roadId,
            int segmentId)
        {
            AppendVertex(domainData, basePointOffset, sourceStart, sourceNormals, sourceTextures, point0);
            AppendVertex(domainData, basePointOffset, sourceStart, sourceNormals, sourceTextures, point1);
            AppendVertex(domainData, basePointOffset, sourceStart, sourceNormals, sourceTextures, point2);
            AppendVertex(domainData, basePointOffset, sourceStart, sourceNormals, sourceTextures, point3);
            domainData.primitiveOffsets.Add((uint)domainData.primitivePointIndices.Count);
            domainData.primitiveMaterialIndices.Add(materialSlot);
            domainData.primitiveAreaIndices.Add((int)areaType);
            domainData.primitiveRoadIds.Add(roadId);
            domainData.primitiveSegmentIds.Add(segmentId);
        }

        private void AppendVertex(
            DomainGeometryData domainData,
            int basePointOffset,
            int sourceStart,
            List<Vector3> sourceNormals,
            List<Vector2> sourceTextures,
            int localPointIndex)
        {
            int sourceIndex = sourceStart + localPointIndex;
            domainData.primitivePointIndices.Add(basePointOffset + localPointIndex);
            domainData.vertexNormals.Add(sourceNormals != null && sourceIndex < sourceNormals.Count ? sourceNormals[sourceIndex] : Vector3.up);
            domainData.vertexUv.Add(sourceTextures != null && sourceIndex < sourceTextures.Count ? sourceTextures[sourceIndex] : Vector2.zero);
        }

        private void UploadDomainGeometryToHandle(
            ulong handle,
            CunningRoadGeometryHandle geometryHandle,
            DomainGeometryData domainData,
            DomainAssemblyCache assembly = null)
        {
            PackedDomainBuffers packed = assembly?.packed;
            if (packed == null)
            {
                packed = new PackedDomainBuffers();
                RepackDomainGeometryData(domainData, packed);
            }

            Material[] renderMaterials = packed.renderMaterials ?? Array.Empty<Material>();
            bool topologyMatches = HasMatchingDomainTopology(geometryHandle.UploadSnapshot, packed);
            bool uploaded = false;
            bool geometryChanged = true;
            bool snapshotUpdatedInPlace = false;

            if (topologyMatches)
            {
                if (assembly != null && assembly.isBuilt && !assembly.needsFullUpload)
                {
                    uploaded = TryIncrementalUploadDomainGeometryToHandle(
                        handle,
                        geometryHandle,
                        packed,
                        assembly,
                        renderMaterials,
                        out geometryChanged,
                        out snapshotUpdatedInPlace);
                }

                if (!uploaded)
                {
                    uploaded = TryIncrementalUploadDomainGeometryToHandle(handle, geometryHandle, packed, out geometryChanged);
                }
            }

            if (!uploaded)
            {
                if (NativeMethods.cunning_geo_clear(handle) == 0)
                {
                    throw new InvalidOperationException($"Failed to clear Cunning geometry for road {name}");
                }

                FullUploadDomainGeometryToHandle(handle, packed);
                geometryChanged = true;
            }

            if (!snapshotUpdatedInPlace && (!topologyMatches || geometryChanged || (assembly != null && assembly.renderMaterialsChanged)))
            {
                geometryHandle.CaptureUploadSnapshot(
                    packed.positions,
                    packed.primitivePointIndices,
                    packed.primitiveOffsets,
                    packed.vertexNormals,
                    packed.vertexUv,
                    packed.primitiveMaterialIndices,
                    packed.primitiveAreaIndices,
                    renderMaterials);
            }
            geometryHandle.ReplaceGeometry(handle, renderMaterials);
        }

        private void FullUploadDomainGeometryToHandle(ulong handle, PackedDomainBuffers packed)
        {
            EnsureBufferCapacity(ref m_CunningPointIdBuffer, packed.PointCount);
            uint addedPoints = NativeMethods.cunning_geo_add_points_bulk_nosync(
                handle,
                packed.positions,
                (uint)packed.PointCount,
                m_CunningPointIdBuffer);

            if (addedPoints != packed.PointCount)
            {
                throw new InvalidOperationException($"Expected {packed.PointCount} points, got {addedPoints}");
            }

            EnsureBufferCapacity(ref m_CunningPolyPointIdBuffer, packed.primitivePointIndices.Length);
            EnsureBufferCapacity(ref m_CunningPolyOffsetBuffer, packed.primitiveOffsets.Length);
            for (int pointIndex = 0; pointIndex < packed.primitivePointIndices.Length; pointIndex++)
            {
                int localPointIndex = packed.primitivePointIndices[pointIndex];
                m_CunningPolyPointIdBuffer[pointIndex] = m_CunningPointIdBuffer[localPointIndex];
            }

            for (int offsetIndex = 0; offsetIndex < packed.primitiveOffsets.Length; offsetIndex++)
            {
                m_CunningPolyOffsetBuffer[offsetIndex] = packed.primitiveOffsets[offsetIndex];
            }

            uint addedPrimitives = NativeMethods.cunning_geo_add_polys_bulk_nosync(
                handle,
                m_CunningPolyPointIdBuffer,
                m_CunningPolyOffsetBuffer,
                (uint)packed.PrimitiveCount);
            if (addedPrimitives != packed.PrimitiveCount)
            {
                throw new InvalidOperationException($"Expected {packed.PrimitiveCount} primitives, got {addedPrimitives}");
            }

            if (packed.vertexNormals.Length > 0)
            {
                uint setNormals = NativeMethods.cunning_geo_set_vertex_attr_vec3_nosync(
                    handle,
                    "@N",
                    packed.vertexNormals,
                    (uint)packed.VertexCount);
                if (setNormals == 0)
                {
                    throw new InvalidOperationException($"Failed to upload vertex normals for road {name}");
                }
            }

            if (packed.vertexUv.Length > 0)
            {
                uint setUv = NativeMethods.cunning_geo_set_vertex_attr_vec2_nosync(
                    handle,
                    "@uv",
                    packed.vertexUv,
                    (uint)(packed.vertexUv.Length / 2));
                if (setUv == 0)
                {
                    throw new InvalidOperationException($"Failed to upload vertex uv for road {name}");
                }
            }

            if (packed.primitiveMaterialIndices.Length > 0)
            {
                uint setMaterialIndices = NativeMethods.cunning_geo_set_primitive_attr_i32_nosync(
                    handle,
                    "@unity_material_index",
                    packed.primitiveMaterialIndices,
                    (uint)packed.primitiveMaterialIndices.Length);
                if (setMaterialIndices == 0)
                {
                    throw new InvalidOperationException($"Failed to upload primitive material indices for road {name}");
                }
            }

            if (packed.primitiveAreaIndices.Length > 0)
            {
                uint setAreaIndices = NativeMethods.cunning_geo_set_primitive_attr_i32_nosync(
                    handle,
                    "@area_type_index",
                    packed.primitiveAreaIndices,
                    (uint)packed.primitiveAreaIndices.Length);
                if (setAreaIndices == 0)
                {
                    throw new InvalidOperationException($"Failed to upload primitive area indices for road {name}");
                }
            }

            if (packed.primitiveRoadIds.Length > 0)
            {
                uint setRoadIds = NativeMethods.cunning_geo_set_primitive_attr_i32_nosync(
                    handle,
                    "@road_id",
                    packed.primitiveRoadIds,
                    (uint)packed.primitiveRoadIds.Length);
                if (setRoadIds == 0)
                {
                    throw new InvalidOperationException($"Failed to upload primitive road ids for road {name}");
                }
            }

            if (packed.primitiveSegmentIds.Length > 0)
            {
                uint setSegmentIds = NativeMethods.cunning_geo_set_primitive_attr_i32_nosync(
                    handle,
                    "@segment_id",
                    packed.primitiveSegmentIds,
                    (uint)packed.primitiveSegmentIds.Length);
                if (setSegmentIds == 0)
                {
                    throw new InvalidOperationException($"Failed to upload primitive segment ids for road {name}");
                }
            }

            if (NativeMethods.cunning_geo_sync_cache(handle) == 0)
            {
                throw new InvalidOperationException($"Failed to sync Cunning domain cache for road {name}");
            }
        }

        private bool TryIncrementalUploadDomainGeometryToHandle(
            ulong handle,
            CunningRoadGeometryHandle geometryHandle,
            PackedDomainBuffers packed,
            out bool geometryChanged)
        {
            geometryChanged = false;
            var snapshot = geometryHandle.UploadSnapshot;
            if (snapshot == null || !snapshot.HasTopology)
            {
                return false;
            }

            var pointDirtyRanges = new List<DirtyRange>(1);
            var vertexDirtyRanges = new List<DirtyRange>(1);
            var primitiveDirtyRanges = new List<DirtyRange>(1);

            if (!TryGetPackedFloatDirtyRange(snapshot.positions, packed.positions, 3, out int pointStart, out int pointCount))
            {
                return false;
            }

            if (!TryGetPackedFloatDirtyRange(snapshot.vertexNormals, packed.vertexNormals, 3, out int vertexNormalStart, out int vertexNormalCount))
            {
                return false;
            }

            if (!TryGetPackedFloatDirtyRange(snapshot.vertexUv, packed.vertexUv, 2, out int vertexUvStart, out int vertexUvCount))
            {
                return false;
            }

            if (!TryGetDirtyRange(snapshot.primitiveMaterialIndices, packed.primitiveMaterialIndices, out int primitiveMaterialStart, out int primitiveMaterialCount))
            {
                return false;
            }

            if (!TryGetDirtyRange(snapshot.primitiveAreaIndices, packed.primitiveAreaIndices, out int primitiveAreaStart, out int primitiveAreaCount))
            {
                return false;
            }

            if (pointCount > 0)
            {
                pointDirtyRanges.Add(new DirtyRange(pointStart, pointCount));
            }

            AddDirtyRange(vertexDirtyRanges, vertexNormalStart, vertexNormalCount);
            AddDirtyRange(vertexDirtyRanges, vertexUvStart, vertexUvCount);
            AddDirtyRange(primitiveDirtyRanges, primitiveMaterialStart, primitiveMaterialCount);
            AddDirtyRange(primitiveDirtyRanges, primitiveAreaStart, primitiveAreaCount);

            var fallbackAssembly = new DomainAssemblyCache();
            fallbackAssembly.pointDirtyRanges.AddRange(pointDirtyRanges);
            fallbackAssembly.vertexDirtyRanges.AddRange(vertexDirtyRanges);
            fallbackAssembly.primitiveDirtyRanges.AddRange(primitiveDirtyRanges);
            fallbackAssembly.needsFullUpload = false;
            fallbackAssembly.renderMaterialsChanged = !SequenceEqual(snapshot.renderMaterials, packed.renderMaterials);

            bool snapshotUpdated;
            bool uploaded = TryIncrementalUploadDomainGeometryToHandle(
                handle,
                geometryHandle,
                packed,
                fallbackAssembly,
                packed.renderMaterials,
                out geometryChanged,
                out snapshotUpdated);

            if (uploaded && snapshotUpdated)
            {
                geometryHandle.UploadSnapshot.SetRenderMaterials(packed.renderMaterials);
            }

            return uploaded;
        }

        private bool TryIncrementalUploadDomainGeometryToHandle(
            ulong handle,
            CunningRoadGeometryHandle geometryHandle,
            PackedDomainBuffers packed,
            DomainAssemblyCache assembly,
            Material[] renderMaterials,
            out bool geometryChanged,
            out bool snapshotUpdated)
        {
            geometryChanged = false;
            snapshotUpdated = false;

            var snapshot = geometryHandle.UploadSnapshot;
            if (snapshot == null
                || !snapshot.HasTopology
                || assembly == null
                || assembly.needsFullUpload
                || snapshot.positions.Length != packed.positions.Length
                || snapshot.vertexNormals.Length != packed.vertexNormals.Length
                || snapshot.vertexUv.Length != packed.vertexUv.Length
                || snapshot.primitiveMaterialIndices.Length != packed.primitiveMaterialIndices.Length
                || snapshot.primitiveAreaIndices.Length != packed.primitiveAreaIndices.Length)
            {
                return false;
            }

            bool anyChange = false;

            if (!TryApplyPackedFloatDirtyRanges(
                    packed.positions,
                    assembly.pointDirtyRanges,
                    3,
                    ref m_CunningFlatPositionsBuffer,
                    (starts, counts, rangeCount, values, totalValueCount) => NativeMethods.cunning_geo_update_point_positions_ranges_nosync(handle, starts, counts, rangeCount, values, totalValueCount),
                    ref anyChange))
            {
                return false;
            }

            if (!TryApplyPackedFloatDirtyRanges(
                    packed.vertexNormals,
                    assembly.vertexDirtyRanges,
                    3,
                    ref m_CunningFlatNormalsBuffer,
                    (starts, counts, rangeCount, values, totalValueCount) => NativeMethods.cunning_geo_update_vertex_attr_vec3_ranges_nosync(handle, "@N", starts, counts, rangeCount, values, totalValueCount),
                    ref anyChange))
            {
                return false;
            }

            if (!TryApplyPackedFloatDirtyRanges(
                    packed.vertexUv,
                    assembly.vertexDirtyRanges,
                    2,
                    ref m_CunningFlatUvBuffer,
                    (starts, counts, rangeCount, values, totalValueCount) => NativeMethods.cunning_geo_update_vertex_attr_vec2_ranges_nosync(handle, "@uv", starts, counts, rangeCount, values, totalValueCount),
                    ref anyChange))
            {
                return false;
            }

            if (!TryApplyPackedIntDirtyRanges(
                    packed.primitiveMaterialIndices,
                    assembly.primitiveDirtyRanges,
                    ref m_CunningPrimitiveMaterialIndexBuffer,
                    (starts, counts, rangeCount, values, totalValueCount) => NativeMethods.cunning_geo_update_primitive_attr_i32_ranges_nosync(handle, "@unity_material_index", starts, counts, rangeCount, values, totalValueCount),
                    ref anyChange))
            {
                return false;
            }

            if (!TryApplyPackedIntDirtyRanges(
                    packed.primitiveAreaIndices,
                    assembly.primitiveDirtyRanges,
                    ref m_CunningPrimitiveAreaIndexBuffer,
                    (starts, counts, rangeCount, values, totalValueCount) => NativeMethods.cunning_geo_update_primitive_attr_i32_ranges_nosync(handle, "@area_type_index", starts, counts, rangeCount, values, totalValueCount),
                    ref anyChange))
            {
                return false;
            }

            if (anyChange && NativeMethods.cunning_geo_sync_cache(handle) == 0)
            {
                return false;
            }

            if (assembly.pointDirtyRanges.Count > 0)
            {
                ApplyPackedFloatDirtyRangesToSnapshot(packed.positions, snapshot.positions, assembly.pointDirtyRanges, 3);
                snapshotUpdated = true;
            }

            if (assembly.vertexDirtyRanges.Count > 0)
            {
                ApplyPackedFloatDirtyRangesToSnapshot(packed.vertexNormals, snapshot.vertexNormals, assembly.vertexDirtyRanges, 3);
                ApplyPackedFloatDirtyRangesToSnapshot(packed.vertexUv, snapshot.vertexUv, assembly.vertexDirtyRanges, 2);
                snapshotUpdated = true;
            }

            if (assembly.primitiveDirtyRanges.Count > 0)
            {
                ApplyIntDirtyRangesToSnapshot(packed.primitiveMaterialIndices, snapshot.primitiveMaterialIndices, assembly.primitiveDirtyRanges);
                ApplyIntDirtyRangesToSnapshot(packed.primitiveAreaIndices, snapshot.primitiveAreaIndices, assembly.primitiveDirtyRanges);
                snapshotUpdated = true;
            }

            if (snapshotUpdated || assembly.renderMaterialsChanged)
            {
                snapshot.SetRenderMaterials(renderMaterials);
                snapshotUpdated = true;
            }

            geometryChanged = anyChange;
            return true;
        }

        private void RefreshCunningDomainObjects(CunningGeometryDomainMask dirtyDomains)
        {
            if (dirtyDomains == CunningGeometryDomainMask.None)
            {
                return;
            }

            EnsureCunningRoadComponents();
            if (HasDomain(dirtyDomains, CunningGeometryDomainMask.RoadMain))
            {
                RefreshCunningDomainObject(m_CunningGeometryHandle, m_CunningMesh);
            }

            if (HasDomain(dirtyDomains, CunningGeometryDomainMask.SidewalkBundle))
            {
                RefreshCunningDomainObject(m_SidewalkGeometryHandle, m_SidewalkCunningMesh);
            }

            if (HasDomain(dirtyDomains, CunningGeometryDomainMask.GreenbeltBundle))
            {
                RefreshCunningDomainObject(m_GreenbeltGeometryHandle, m_GreenbeltCunningMesh);
            }
        }

        private void RefreshCunningDomainObject(CunningRoadGeometryHandle geometryHandle, CunningMesh cunningMesh)
        {
            if (geometryHandle == null || cunningMesh == null)
            {
                return;
            }

            geometryHandle.RebuildGeometryFromRoad();
            bool hasGeometry = geometryHandle.GeometryHandle != 0;
            if (cunningMesh.gameObject.activeSelf != hasGeometry)
            {
                cunningMesh.gameObject.SetActive(hasGeometry);
            }

            if (!hasGeometry)
            {
                cunningMesh.LoadFromHandle(0);
                return;
            }

            uint vertexCount = NativeMethods.cunning_geo_get_vertex_count(geometryHandle.GeometryHandle);
            if (vertexCount == 0)
            {
                NativeMethods.GeoRenderState renderState = default;
                uint hasRenderState = NativeMethods.cunning_geo_get_render_state(geometryHandle.GeometryHandle, out renderState);
                Debug.LogWarning(
                    $"Cunning domain '{geometryHandle.Domain}' on road '{name}' rebuilt handle {geometryHandle.GeometryHandle}, but vertex_count=0. " +
                    $"render_state_ok={hasRenderState != 0}, render_vertices={renderState.vertex_count}, prims={renderState.prim_count}, tris={renderState.tri_index_count}");
            }

            cunningMesh.solidMaterialOverride = geometryHandle.PrimaryMaterial;
            cunningMesh.solidMaterialOverrides = geometryHandle.RenderMaterials;
            cunningMesh.LoadFromHandle(geometryHandle.GeometryHandle);
        }

        private static void EnsureBufferCapacity<T>(ref T[] buffer, int requiredLength)
        {
            if (requiredLength <= 0)
            {
                if (buffer == null)
                {
                    buffer = Array.Empty<T>();
                }
                return;
            }

            if (buffer == null || buffer.Length < requiredLength)
            {
                buffer = new T[Mathf.NextPowerOfTwo(requiredLength)];
            }
        }

        private static float[] CopyVector3ListToBuffer(List<Vector3> values, ref float[] buffer)
        {
            int requiredLength = values.Count * 3;
            EnsureBufferCapacity(ref buffer, requiredLength);
            for (int index = 0; index < values.Count; index++)
            {
                int flatIndex = index * 3;
                Vector3 value = values[index];
                buffer[flatIndex] = value.x;
                buffer[flatIndex + 1] = value.y;
                buffer[flatIndex + 2] = value.z;
            }

            return buffer;
        }

        private static float[] CopyVector3RangeToBuffer(List<Vector3> values, int start, int count, ref float[] buffer)
        {
            int requiredLength = count * 3;
            EnsureBufferCapacity(ref buffer, requiredLength);
            for (int index = 0; index < count; index++)
            {
                int flatIndex = index * 3;
                Vector3 value = values[start + index];
                buffer[flatIndex] = value.x;
                buffer[flatIndex + 1] = value.y;
                buffer[flatIndex + 2] = value.z;
            }

            return buffer;
        }

        private static float[] CopyVector2ListToBuffer(List<Vector2> values, ref float[] buffer)
        {
            int requiredLength = values.Count * 2;
            EnsureBufferCapacity(ref buffer, requiredLength);
            for (int index = 0; index < values.Count; index++)
            {
                int flatIndex = index * 2;
                Vector2 value = values[index];
                buffer[flatIndex] = value.x;
                buffer[flatIndex + 1] = value.y;
            }

            return buffer;
        }

        private static float[] CopyVector2RangeToBuffer(List<Vector2> values, int start, int count, ref float[] buffer)
        {
            int requiredLength = count * 2;
            EnsureBufferCapacity(ref buffer, requiredLength);
            for (int index = 0; index < count; index++)
            {
                int flatIndex = index * 2;
                Vector2 value = values[start + index];
                buffer[flatIndex] = value.x;
                buffer[flatIndex + 1] = value.y;
            }

            return buffer;
        }

        private static int[] CopyIntRangeToBuffer(List<int> values, int start, int count, ref int[] buffer)
        {
            EnsureBufferCapacity(ref buffer, count);
            for (int index = 0; index < count; index++)
            {
                buffer[index] = values[start + index];
            }

            return buffer;
        }

        private static bool HasMatchingDomainTopology(CunningRoadGeometryHandle.DomainUploadSnapshot snapshot, PackedDomainBuffers packed)
        {
            if (snapshot == null || !snapshot.HasTopology)
            {
                return false;
            }

            return snapshot.positions.Length == packed.positions.Length
                && snapshot.primitivePointIndices.Length == packed.primitivePointIndices.Length
                && snapshot.primitiveOffsets.Length == packed.primitiveOffsets.Length
                && snapshot.vertexNormals.Length == packed.vertexNormals.Length
                && snapshot.vertexUv.Length == packed.vertexUv.Length
                && snapshot.primitiveMaterialIndices.Length == packed.primitiveMaterialIndices.Length
                && snapshot.primitiveAreaIndices.Length == packed.primitiveAreaIndices.Length
                && SequenceEqual(snapshot.primitivePointIndices, packed.primitivePointIndices)
                && SequenceEqual(snapshot.primitiveOffsets, packed.primitiveOffsets);
        }

        private bool TryApplyPackedFloatDirtyRanges(
            float[] current,
            IReadOnlyList<DirtyRange> dirtyRanges,
            int componentCount,
            ref float[] buffer,
            Func<IntPtr, IntPtr, uint, IntPtr, uint, uint> nativeUpdate,
            ref bool anyChange)
        {
            if (dirtyRanges == null)
            {
                return false;
            }

            PrepareDirtyRangeBuffers(dirtyRanges, out int rangeCount, out int totalValueCount);
            if (rangeCount <= 0 || totalValueCount <= 0)
            {
                return true;
            }

            float[] valueBuffer = CopyPackedFloatDirtyRangesToBuffer(current, dirtyRanges, componentCount, ref buffer, totalValueCount);

            GCHandle startsHandle = default;
            GCHandle countsHandle = default;
            GCHandle valuesHandle = default;
            try
            {
                startsHandle = GCHandle.Alloc(m_CunningDirtyRangeStartBuffer, GCHandleType.Pinned);
                countsHandle = GCHandle.Alloc(m_CunningDirtyRangeCountBuffer, GCHandleType.Pinned);
                valuesHandle = GCHandle.Alloc(valueBuffer, GCHandleType.Pinned);

                uint updated = nativeUpdate(
                    startsHandle.AddrOfPinnedObject(),
                    countsHandle.AddrOfPinnedObject(),
                    (uint)rangeCount,
                    valuesHandle.AddrOfPinnedObject(),
                    (uint)totalValueCount);

                if (updated != totalValueCount)
                {
                    return false;
                }
            }
            finally
            {
                if (valuesHandle.IsAllocated)
                {
                    valuesHandle.Free();
                }

                if (countsHandle.IsAllocated)
                {
                    countsHandle.Free();
                }

                if (startsHandle.IsAllocated)
                {
                    startsHandle.Free();
                }
            }

            anyChange = true;
            return true;
        }

        private bool TryApplyPackedIntDirtyRanges(
            int[] current,
            IReadOnlyList<DirtyRange> dirtyRanges,
            ref int[] buffer,
            Func<IntPtr, IntPtr, uint, IntPtr, uint, uint> nativeUpdate,
            ref bool anyChange)
        {
            if (dirtyRanges == null)
            {
                return false;
            }

            PrepareDirtyRangeBuffers(dirtyRanges, out int rangeCount, out int totalValueCount);
            if (rangeCount <= 0 || totalValueCount <= 0)
            {
                return true;
            }

            int[] valueBuffer = CopyPackedIntDirtyRangesToBuffer(current, dirtyRanges, ref buffer, totalValueCount);

            GCHandle startsHandle = default;
            GCHandle countsHandle = default;
            GCHandle valuesHandle = default;
            try
            {
                startsHandle = GCHandle.Alloc(m_CunningDirtyRangeStartBuffer, GCHandleType.Pinned);
                countsHandle = GCHandle.Alloc(m_CunningDirtyRangeCountBuffer, GCHandleType.Pinned);
                valuesHandle = GCHandle.Alloc(valueBuffer, GCHandleType.Pinned);

                uint updated = nativeUpdate(
                    startsHandle.AddrOfPinnedObject(),
                    countsHandle.AddrOfPinnedObject(),
                    (uint)rangeCount,
                    valuesHandle.AddrOfPinnedObject(),
                    (uint)totalValueCount);

                if (updated != totalValueCount)
                {
                    return false;
                }
            }
            finally
            {
                if (valuesHandle.IsAllocated)
                {
                    valuesHandle.Free();
                }

                if (countsHandle.IsAllocated)
                {
                    countsHandle.Free();
                }

                if (startsHandle.IsAllocated)
                {
                    startsHandle.Free();
                }
            }

            anyChange = true;
            return true;
        }

        private void PrepareDirtyRangeBuffers(IReadOnlyList<DirtyRange> dirtyRanges, out int rangeCount, out int totalValueCount)
        {
            rangeCount = 0;
            totalValueCount = 0;
            if (dirtyRanges == null)
            {
                return;
            }

            EnsureBufferCapacity(ref m_CunningDirtyRangeStartBuffer, dirtyRanges.Count);
            EnsureBufferCapacity(ref m_CunningDirtyRangeCountBuffer, dirtyRanges.Count);
            for (int rangeIndex = 0; rangeIndex < dirtyRanges.Count; rangeIndex++)
            {
                DirtyRange range = dirtyRanges[rangeIndex];
                if (range.count <= 0)
                {
                    continue;
                }

                m_CunningDirtyRangeStartBuffer[rangeCount] = (uint)range.start;
                m_CunningDirtyRangeCountBuffer[rangeCount] = (uint)range.count;
                totalValueCount += range.count;
                rangeCount++;
            }
        }

        private static float[] CopyPackedFloatDirtyRangesToBuffer(
            float[] source,
            IReadOnlyList<DirtyRange> dirtyRanges,
            int componentCount,
            ref float[] buffer,
            int totalValueCount)
        {
            EnsureBufferCapacity(ref buffer, totalValueCount * componentCount);
            if (source == null || dirtyRanges == null)
            {
                return buffer;
            }

            int writeOffset = 0;
            for (int rangeIndex = 0; rangeIndex < dirtyRanges.Count; rangeIndex++)
            {
                DirtyRange range = dirtyRanges[rangeIndex];
                if (range.count <= 0)
                {
                    continue;
                }

                int sourceOffset = range.start * componentCount;
                int sourceCount = range.count * componentCount;
                Array.Copy(source, sourceOffset, buffer, writeOffset, sourceCount);
                writeOffset += sourceCount;
            }

            return buffer;
        }

        private static int[] CopyPackedIntDirtyRangesToBuffer(
            int[] source,
            IReadOnlyList<DirtyRange> dirtyRanges,
            ref int[] buffer,
            int totalValueCount)
        {
            EnsureBufferCapacity(ref buffer, totalValueCount);
            if (source == null || dirtyRanges == null)
            {
                return buffer;
            }

            int writeOffset = 0;
            for (int rangeIndex = 0; rangeIndex < dirtyRanges.Count; rangeIndex++)
            {
                DirtyRange range = dirtyRanges[rangeIndex];
                if (range.count <= 0)
                {
                    continue;
                }

                Array.Copy(source, range.start, buffer, writeOffset, range.count);
                writeOffset += range.count;
            }

            return buffer;
        }

        private static void ApplyPackedFloatDirtyRangesToSnapshot(
            float[] source,
            float[] destination,
            IReadOnlyList<DirtyRange> dirtyRanges,
            int componentCount)
        {
            if (source == null || destination == null || dirtyRanges == null)
            {
                return;
            }

            for (int rangeIndex = 0; rangeIndex < dirtyRanges.Count; rangeIndex++)
            {
                DirtyRange range = dirtyRanges[rangeIndex];
                int start = Mathf.Max(0, range.start * componentCount);
                int copyLength = Mathf.Min(range.count * componentCount, Mathf.Min(source.Length - start, destination.Length - start));
                if (copyLength > 0)
                {
                    Array.Copy(source, start, destination, start, copyLength);
                }
            }
        }

        private static void ApplyIntDirtyRangesToSnapshot(int[] source, int[] destination, IReadOnlyList<DirtyRange> dirtyRanges)
        {
            if (source == null || destination == null || dirtyRanges == null)
            {
                return;
            }

            for (int rangeIndex = 0; rangeIndex < dirtyRanges.Count; rangeIndex++)
            {
                DirtyRange range = dirtyRanges[rangeIndex];
                int start = Mathf.Max(0, range.start);
                int copyLength = Mathf.Min(range.count, Mathf.Min(source.Length - start, destination.Length - start));
                if (copyLength > 0)
                {
                    Array.Copy(source, start, destination, start, copyLength);
                }
            }
        }

        private static bool TryGetPackedFloatDirtyRange(float[] previous, float[] current, int componentCount, out int start, out int count)
        {
            start = 0;
            count = 0;
            if (previous == null || current == null || previous.Length != current.Length || componentCount <= 0)
            {
                return false;
            }

            int first = -1;
            int elementCount = current.Length / componentCount;
            for (int elementIndex = 0; elementIndex < elementCount; elementIndex++)
            {
                int componentStart = elementIndex * componentCount;
                bool differs = false;
                for (int componentIndex = 0; componentIndex < componentCount; componentIndex++)
                {
                    if (!Mathf.Approximately(previous[componentStart + componentIndex], current[componentStart + componentIndex]))
                    {
                        differs = true;
                        break;
                    }
                }

                if (differs)
                {
                    first = elementIndex;
                    break;
                }
            }

            if (first < 0)
            {
                return true;
            }

            int last = first;
            for (int elementIndex = elementCount - 1; elementIndex >= first; elementIndex--)
            {
                int componentStart = elementIndex * componentCount;
                bool differs = false;
                for (int componentIndex = 0; componentIndex < componentCount; componentIndex++)
                {
                    if (!Mathf.Approximately(previous[componentStart + componentIndex], current[componentStart + componentIndex]))
                    {
                        differs = true;
                        break;
                    }
                }

                if (differs)
                {
                    last = elementIndex;
                    break;
                }
            }

            start = first;
            count = last - first + 1;
            return true;
        }

        private static bool TryGetDirtyRange<T>(T[] previous, T[] current, out int start, out int count)
        {
            start = 0;
            count = 0;
            if (previous == null || current == null || previous.Length != current.Length)
            {
                return false;
            }

            var comparer = EqualityComparer<T>.Default;
            int first = -1;
            for (int index = 0; index < current.Length; index++)
            {
                if (!comparer.Equals(previous[index], current[index]))
                {
                    first = index;
                    break;
                }
            }

            if (first < 0)
            {
                return true;
            }

            int last = first;
            for (int index = current.Length - 1; index >= first; index--)
            {
                if (!comparer.Equals(previous[index], current[index]))
                {
                    last = index;
                    break;
                }
            }

            start = first;
            count = last - first + 1;
            return true;
        }

        private static bool SequenceEqual(IReadOnlyList<int> previous, int[] current)
        {
            if (previous == null || current == null || previous.Count != current.Length)
            {
                return false;
            }

            for (int index = 0; index < current.Length; index++)
            {
                if (previous[index] != current[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SequenceEqual(IReadOnlyList<uint> previous, uint[] current)
        {
            if (previous == null || current == null || previous.Count != current.Length)
            {
                return false;
            }

            for (int index = 0; index < current.Length; index++)
            {
                if (previous[index] != current[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SequenceEqual(Material[] previous, Material[] current)
        {
            if (previous == null || current == null || previous.Length != current.Length)
            {
                return false;
            }

            for (int index = 0; index < current.Length; index++)
            {
                if (previous[index] != current[index])
                {
                    return false;
                }
            }

            return true;
        }


        public void LoftAllRoads()
        {
            try
            {
#if UNITY_EDITOR
                DeferredDownstreamRefreshScheduler.Remove(this);
#endif
                m_SynchronousSplineRefreshIndices.Clear();
                // 检查必要的组件和数据
                if (m_RoadDatas == null)
                {
                    Debug.LogError("Road data list is null");
                    return;
                }

                if (LoftSplines == null)
                {
                    Debug.LogError("Spline list is null");
                    return;
                }

                bool geometryChanged = RebuildDirtySplineGeometryCachesIfNeeded();
                BuildDownstreamRefreshSplineIndices();
                if (!geometryChanged && m_DownstreamRefreshSplineIndices.Count == 0)
                {
                    return;
                }

                if (geometryChanged)
                {
                    try
                    {
                        RefreshCunningDomainObjects(m_LastChangedDomainMask);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"Error rebuilding Cunning road geometry: {e.Message}\n{e.StackTrace}");
                        return;
                    }
                }

                try
                {
                    // 生成车道中线Spline
                    CreateLaneSplines(m_DownstreamRefreshSplineIndices);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error creating lane splines: {e.Message}\n{e.StackTrace}");
                }

                // 更新受影响的路口
                if (geometryChanged && !AreConnectedJunctionUpdatesSuppressed)
                {
                    var junctionsToUpdate = CollectJunctionsToUpdate(m_LastChangedSplineIndices);
                    foreach (var junction in junctionsToUpdate)
                    {
                        if (junction != null)
                        {
                            try
                            {
                                junction.UpdateJunctionMesh();
                            }
                            catch (System.Exception e)
                            {
                                Debug.LogError($"Error updating junction mesh: {e.Message}\n{e.StackTrace}");
                            }
                        }
                    }
                }
                ClearDirtyJunctionState();

                // 收集采样点
                try
                {
                    CollectSamplePoints(m_DownstreamRefreshSplineIndices);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Error collecting sample points: {e.Message}\n{e.StackTrace}");
                }

                m_DirtyDownstreamSplineIndices.Clear();
                m_DownstreamRefreshSplineIndices.Clear();
#if UNITY_EDITOR
                if (!ContinuousSplineImplicitJunctionManager.IsApplyingChanges)
                {
                    ContinuousSplineImplicitJunctionManager.EnqueueRoad(this, -1);
                }
#endif
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error in LoftAllRoads: {e.Message}\n{e.StackTrace}");
            }
        }
        
        // 创建车道中线Spline
        private void CreateLaneSplines()
        {
            if (LoftSplines == null)
            {
                return;
            }

            ClearLaneSplines();
            m_LaneMeshDataList.Clear();
            // 确保车道容器已创建
            CreateLanesContainer();

            List<int> splineIndices = new List<int>(LoftSplines.Count);
            for (int splineIndex = 0; splineIndex < LoftSplines.Count; splineIndex++)
            {
                splineIndices.Add(splineIndex);
            }

            CreateLaneSplines(splineIndices);
        }

        private void CreateLaneSplines(IReadOnlyList<int> splineIndices)
        {
            if (LoftSplines == null || m_RoadDatas == null || splineIndices == null || splineIndices.Count == 0)
            {
                return;
            }

            CreateLanesContainer();
            for (int listIndex = 0; listIndex < splineIndices.Count; listIndex++)
            {
                ClearLaneMeshDataForSpline(splineIndices[listIndex]);
            }

            for (int listIndex = 0; listIndex < splineIndices.Count; listIndex++)
            {
                int splineIndex = splineIndices[listIndex];
                if (splineIndex < 0 || splineIndex >= LoftSplines.Count || splineIndex >= m_RoadDatas.Count)
                {
                    continue;
                }

                var roadData = m_RoadDatas[splineIndex];
                var mainSpline = LoftSplines[splineIndex];
                if (roadData.roadTypeEnum == RoadType.GreenBelt_Define)
                {
                    continue;
                }

                int totalLanes = roadData.leftLaneCount + roadData.rightLaneCount;
                if (totalLanes <= 0)
                {
                    continue;
                }

                IReadOnlyList<LogicalSegmentDef> logicalSegments = GetLogicalSegments(splineIndex);
                for (int logicalSegmentIndex = 0; logicalSegmentIndex < logicalSegments.Count; logicalSegmentIndex++)
                {
                    try
                    {
                        BuildLaneDataForSegment(splineIndex, roadData, mainSpline, logicalSegments[logicalSegmentIndex]);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"Error processing spline {splineIndex} segment {logicalSegmentIndex}: {e.Message}\n{e.StackTrace}");
                    }
                }
            }
        }

        private void ClearLaneMeshDataForSpline(int splineIndex)
        {
            if (m_LaneMeshDataList == null || m_LaneMeshDataList.Count == 0)
            {
                return;
            }

            m_LaneMeshDataList.RemoveAll(laneData => laneData != null && laneData.splineIndex == splineIndex);
        }

        private void GetSplineEndpointJunctions(int splineIndex, out JunctionData startJunction, out JunctionData endJunction)
        {
            startJunction = null;
            endJunction = null;

            if (connectedJunctions == null || connectedJunctions.Count == 0)
            {
                return;
            }

            foreach (var junction in connectedJunctions)
            {
                if (junction == null || junction.connectedRoads == null)
                {
                    continue;
                }

                foreach (var connectedRoad in junction.connectedRoads)
                {
                    if (connectedRoad == null || connectedRoad.roadBehaviour != this || connectedRoad.splineIndex != splineIndex)
                    {
                        continue;
                    }

                    if (connectedRoad.knotIndex == 0)
                    {
                        startJunction = junction;
                    }
                    else if (connectedRoad.knotIndex == LoftSplines[splineIndex].Count - 1)
                    {
                        endJunction = junction;
                    }

                    if (startJunction != null && endJunction != null)
                    {
                        return;
                    }
                }
            }
        }

        private void ClearAllMeshData()
        {
            m_Positions.Clear();
            m_Normals.Clear();
            m_Textures.Clear();
            m_Indices.Clear();
            m_CunningRoadStripRanges.Clear();
            m_CunningSideStripRanges.Clear();
            m_GreenBeltPositions.Clear();
            m_GreenBeltNormals.Clear();
            m_GreenBeltTextures.Clear();
            m_GreenBeltIndices.Clear();
            m_SidewalkLeftPositions.Clear();
            m_SidewalkLeftNormals.Clear();
            m_SidewalkLeftTextures.Clear();
            m_SidewalkLeftIndices.Clear();
            m_SidewalkRightPositions.Clear();
            m_SidewalkRightNormals.Clear();
            m_SidewalkRightTextures.Clear();
            m_SidewalkRightIndices.Clear();
            m_CurbLeftPositions.Clear();
            m_CurbLeftNormals.Clear();
            m_CurbLeftTextures.Clear();
            m_CurbLeftIndices.Clear();
            m_CurbRightPositions.Clear();
            m_CurbRightNormals.Clear();
            m_CurbRightTextures.Clear();
            m_CurbRightIndices.Clear();
            m_RoadEdgeLeftPositions.Clear();
            m_RoadEdgeLeftNormals.Clear();
            m_RoadEdgeLeftTextures.Clear();
            m_RoadEdgeLeftIndices.Clear();
            m_RoadEdgeRightPositions.Clear();
            m_RoadEdgeRightNormals.Clear();
            m_RoadEdgeRightTextures.Clear();
            m_RoadEdgeRightIndices.Clear();
            m_RoadEdgeOuterLeftPositions.Clear();
            m_RoadEdgeOuterLeftNormals.Clear();
            m_RoadEdgeOuterLeftTextures.Clear();
            m_RoadEdgeOuterLeftIndices.Clear();
            m_RoadEdgeOuterRightPositions.Clear();
            m_RoadEdgeOuterRightNormals.Clear();
            m_RoadEdgeOuterRightTextures.Clear();
            m_RoadEdgeOuterRightIndices.Clear();
        }

        private void UpdateAllMeshes(bool includeMainRoadMesh)
        {
            if (includeMainRoadMesh)
            {
                LoftMesh.SetVertices(m_Positions);
                LoftMesh.SetNormals(m_Normals);
                LoftMesh.SetUVs(0, m_Textures);
                LoftMesh.subMeshCount = 1;
                LoftMesh.SetIndices(m_Indices, MeshTopology.Triangles, 0);
                LoftMesh.UploadMeshData(false);
            }

            GreenBeltMesh.SetVertices(m_GreenBeltPositions);
            GreenBeltMesh.SetNormals(m_GreenBeltNormals);
            GreenBeltMesh.SetUVs(0, m_GreenBeltTextures);
            GreenBeltMesh.subMeshCount = 1;
            GreenBeltMesh.SetIndices(m_GreenBeltIndices, MeshTopology.Triangles, 0);
            GreenBeltMesh.UploadMeshData(false);

            SidewalkLeftMesh.SetVertices(m_SidewalkLeftPositions);
            SidewalkLeftMesh.SetNormals(m_SidewalkLeftNormals);
            SidewalkLeftMesh.SetUVs(0, m_SidewalkLeftTextures);
            SidewalkLeftMesh.subMeshCount = 1;
            SidewalkLeftMesh.SetIndices(m_SidewalkLeftIndices, MeshTopology.Triangles, 0);
            SidewalkLeftMesh.UploadMeshData(false);

            SidewalkRightMesh.SetVertices(m_SidewalkRightPositions);
            SidewalkRightMesh.SetNormals(m_SidewalkRightNormals);
            SidewalkRightMesh.SetUVs(0, m_SidewalkRightTextures);
            SidewalkRightMesh.subMeshCount = 1;
            SidewalkRightMesh.SetIndices(m_SidewalkRightIndices, MeshTopology.Triangles, 0);
            SidewalkRightMesh.UploadMeshData(false);
            
            CurbLeftMesh.SetVertices(m_CurbLeftPositions);
            CurbLeftMesh.SetNormals(m_CurbLeftNormals);
            CurbLeftMesh.SetUVs(0, m_CurbLeftTextures);
            CurbLeftMesh.subMeshCount = 1;
            CurbLeftMesh.SetIndices(m_CurbLeftIndices, MeshTopology.Triangles, 0);
            CurbLeftMesh.UploadMeshData(false);
            
            CurbRightMesh.SetVertices(m_CurbRightPositions);
            CurbRightMesh.SetNormals(m_CurbRightNormals);
            CurbRightMesh.SetUVs(0, m_CurbRightTextures);
            CurbRightMesh.subMeshCount = 1;
            CurbRightMesh.SetIndices(m_CurbRightIndices, MeshTopology.Triangles, 0);
            CurbRightMesh.UploadMeshData(false);
            
            RoadEdgeLeftMesh.SetVertices(m_RoadEdgeLeftPositions);
            RoadEdgeLeftMesh.SetNormals(m_RoadEdgeLeftNormals);
            RoadEdgeLeftMesh.SetUVs(0, m_RoadEdgeLeftTextures);
            RoadEdgeLeftMesh.subMeshCount = 1;
            RoadEdgeLeftMesh.SetIndices(m_RoadEdgeLeftIndices, MeshTopology.Triangles, 0);
            RoadEdgeLeftMesh.UploadMeshData(false);
            
            RoadEdgeRightMesh.SetVertices(m_RoadEdgeRightPositions);
            RoadEdgeRightMesh.SetNormals(m_RoadEdgeRightNormals);
            RoadEdgeRightMesh.SetUVs(0, m_RoadEdgeRightTextures);
            RoadEdgeRightMesh.subMeshCount = 1;
            RoadEdgeRightMesh.SetIndices(m_RoadEdgeRightIndices, MeshTopology.Triangles, 0);
            RoadEdgeRightMesh.UploadMeshData(false);
            
            RoadEdgeOuterLeftMesh.SetVertices(m_RoadEdgeOuterLeftPositions);
            RoadEdgeOuterLeftMesh.SetNormals(m_RoadEdgeOuterLeftNormals);
            RoadEdgeOuterLeftMesh.SetUVs(0, m_RoadEdgeOuterLeftTextures);
            RoadEdgeOuterLeftMesh.subMeshCount = 1;
            RoadEdgeOuterLeftMesh.SetIndices(m_RoadEdgeOuterLeftIndices, MeshTopology.Triangles, 0);
            RoadEdgeOuterLeftMesh.UploadMeshData(false);
            
            RoadEdgeOuterRightMesh.SetVertices(m_RoadEdgeOuterRightPositions);
            RoadEdgeOuterRightMesh.SetNormals(m_RoadEdgeOuterRightNormals);
            RoadEdgeOuterRightMesh.SetUVs(0, m_RoadEdgeOuterRightTextures);
            RoadEdgeOuterRightMesh.subMeshCount = 1;
            RoadEdgeOuterRightMesh.SetIndices(m_RoadEdgeOuterRightIndices, MeshTopology.Triangles, 0);
            RoadEdgeOuterRightMesh.UploadMeshData(false);
        }

        private void EnsureChildGameObjects()
        {
            // 检查m_RoadDatas是否有效
            if (m_RoadDatas == null || m_RoadDatas.Count == 0)
            {
                Debug.LogWarning("No road data available in EnsureChildGameObjects");
                return;
            }

            var roadData = m_RoadDatas[0];
            if (roadData == null)
            {
                Debug.LogWarning("First road data is null in EnsureChildGameObjects");
                return;
            }

            try
            {
                GameObject greenBeltGO = EnsureChildGameObjectExists("GreenBeltMesh", GreenBeltMesh, roadData.greenBeltMaterial);
                GameObject sidewalkLeftGO = EnsureChildGameObjectExists("SidewalkLeftMesh", SidewalkLeftMesh, roadData.sidewalkMaterial);
                GameObject sidewalkRightGO = EnsureChildGameObjectExists("SidewalkRightMesh", SidewalkRightMesh, roadData.sidewalkMaterial);
            
            // 处理路缘网格子物体
            GameObject curbLeftGO = transform.Find("CurbLeftMesh")?.gameObject;
            GameObject curbRightGO = transform.Find("CurbRightMesh")?.gameObject;
            
            // 处理马路牙子网格子物体
            GameObject roadEdgeLeftGO = transform.Find("RoadEdgeLeftMesh")?.gameObject;
            GameObject roadEdgeRightGO = transform.Find("RoadEdgeRightMesh")?.gameObject;
            
            // 处理人行道外侧马路牙子网格子物体
            GameObject roadEdgeOuterLeftGO = transform.Find("RoadEdgeOuterLeftMesh")?.gameObject;
            GameObject roadEdgeOuterRightGO = transform.Find("RoadEdgeOuterRightMesh")?.gameObject;
            
                if (roadData.enableCurb)
            {
                // 启用路缘时，确保路缘网格存在并可见
                    curbLeftGO = EnsureChildGameObjectExists("CurbLeftMesh", CurbLeftMesh, roadData.curbMaterial);
                    curbRightGO = EnsureChildGameObjectExists("CurbRightMesh", CurbRightMesh, roadData.curbMaterial);
                
                if (curbLeftGO != null) curbLeftGO.SetActive(true);
                if (curbRightGO != null) curbRightGO.SetActive(true);
                
                // 如果启用了马路牙子，确保马路牙子网格存在并可见
                    if (roadData.enableRoadEdge)
                {
                        roadEdgeLeftGO = EnsureChildGameObjectExists("RoadEdgeLeftMesh", RoadEdgeLeftMesh, roadData.roadEdgeMaterial);
                        roadEdgeRightGO = EnsureChildGameObjectExists("RoadEdgeRightMesh", RoadEdgeRightMesh, roadData.roadEdgeMaterial);
                    
                    if (roadEdgeLeftGO != null) roadEdgeLeftGO.SetActive(true);
                    if (roadEdgeRightGO != null) roadEdgeRightGO.SetActive(true);
                    
                    // 创建并激活人行道外侧马路牙子网格子物体
                        roadEdgeOuterLeftGO = EnsureChildGameObjectExists("RoadEdgeOuterLeftMesh", RoadEdgeOuterLeftMesh, roadData.roadEdgeMaterial);
                        roadEdgeOuterRightGO = EnsureChildGameObjectExists("RoadEdgeOuterRightMesh", RoadEdgeOuterRightMesh, roadData.roadEdgeMaterial);
                    
                    if (roadEdgeOuterLeftGO != null) roadEdgeOuterLeftGO.SetActive(true);
                    if (roadEdgeOuterRightGO != null) roadEdgeOuterRightGO.SetActive(true);
                }
                else
                {
                    // 不启用马路牙子时，隐藏马路牙子网格
                    if (roadEdgeLeftGO != null) roadEdgeLeftGO.SetActive(false);
                    if (roadEdgeRightGO != null) roadEdgeRightGO.SetActive(false);
                    
                    // 禁用人行道外侧马路牙子网格
                    if (roadEdgeOuterLeftGO != null) roadEdgeOuterLeftGO.SetActive(false);
                    if (roadEdgeOuterRightGO != null) roadEdgeOuterRightGO.SetActive(false);
                }
            }
            else
            {
                // 不启用路缘时，隐藏路缘网格和马路牙子网格
                if (curbLeftGO != null) curbLeftGO.SetActive(false);
                if (curbRightGO != null) curbRightGO.SetActive(false);
                if (roadEdgeLeftGO != null) roadEdgeLeftGO.SetActive(false);
                if (roadEdgeRightGO != null) roadEdgeRightGO.SetActive(false);
                // 隐藏人行道外侧马路牙子网格
                if (roadEdgeOuterLeftGO != null) roadEdgeOuterLeftGO.SetActive(false);
                if (roadEdgeOuterRightGO != null) roadEdgeOuterRightGO.SetActive(false);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error in EnsureChildGameObjects: {e.Message}\n{e.StackTrace}");
            }
        }

        private GameObject EnsureChildGameObjectExists(string name, Mesh mesh, Material material)
        {
            GameObject childGO = transform.Find(name)?.gameObject;
            if (childGO == null)
            {
                childGO = new GameObject(name);
                childGO.transform.SetParent(this.transform);
                childGO.AddComponent<MeshFilter>();
                childGO.AddComponent<MeshRenderer>();
            }

            var meshFilter = childGO.GetComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;

            var renderer = childGO.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            return childGO;
        }

        public List<(Vector3 start, Vector3 end)> GetOuterSidewalkEdgeLines()
        {
            var edgeLines = new List<(Vector3 start, Vector3 end)>();

            if (Container == null || m_RoadDatas == null)
                return edgeLines;

            for (int splineIndex = 0; splineIndex < Container.Splines.Count; splineIndex++)
            {
                if (splineIndex >= m_RoadDatas.Count)
                    break;

                var spline = Container.Splines[splineIndex];
                var roadData = m_RoadDatas[splineIndex];
                if (spline == null || roadData == null || spline.Count < 2)
                    continue;

                IReadOnlyList<LogicalSegmentDef> logicalSegments = GetLogicalSegments(splineIndex);
                for (int logicalSegmentIndex = 0; logicalSegmentIndex < logicalSegments.Count; logicalSegmentIndex++)
                {
                    AppendOuterSidewalkEdgeLinesForSegment(edgeLines, splineIndex, roadData, spline, logicalSegments[logicalSegmentIndex]);
                }
            }

            return edgeLines;
        }
        public void Loft(Spline spline, int splineIndex, int vertexIndexOffset)
        {
            if (spline == null || spline.Count < 2)
                return;

            // 设置当前处理的splineIndex
            m_CurrentProcessingSplineIndex = splineIndex;

            var roadData = m_RoadDatas[splineIndex];
            float totalLaneCount = roadData.leftLaneCount + roadData.rightLaneCount;
            float laneWidth = roadData.laneWidth.DefaultValue;
            float totalWidth = totalLaneCount * laneWidth;
            float halfWidth = totalWidth / 2f;
            float leftSidewalkWidth = roadData.leftSidewalkWidth.DefaultValue;
            float rightSidewalkWidth = roadData.rightSidewalkWidth.DefaultValue;
            
            // 计算路径上的采样点
            float splineLength = spline.GetLength();
            int segmentCount = Mathf.Max(2, Mathf.CeilToInt(splineLength / SegmentUnitLength));
            float step = 1f / segmentCount;
            
            // 存储每个采样点的信息
            List<Vector3> pathPoints = new List<Vector3>();
            List<Vector3> pathNormals = new List<Vector3>();
            List<Vector3> pathTangents = new List<Vector3>();
            
            // 计算路径上的点和方向
            for (int i = 0; i <= segmentCount; i++)
            {
                float t = i * step;
                Vector3 position = spline.EvaluatePosition(t);
                Vector3 tangent = spline.EvaluateTangent(t);
                Vector3 normal = Vector3.up;
                
                pathPoints.Add(position);
                pathNormals.Add(normal);
                pathTangents.Add(tangent.normalized);
            }

            // 从中心线开始,向两侧生成车道
            int vertexOffset = vertexIndexOffset;
            float currentOffset = -halfWidth; // 从最左侧开始

            // 生成所有车道
            for (int lane = 0; lane < totalLaneCount; lane++)
            {
                if (lane == 0) // 最左侧的车道
                {
                    // 向左延长1.2倍，但保持右侧边界不变
                    float extendedStartOffset = currentOffset * 1.2f; // 向左延长1.2倍
                    GenerateLaneMesh(pathPoints, pathNormals, pathTangents, extendedStartOffset, currentOffset + laneWidth, 
                        vertexOffset, segmentCount, roadData.meshOffset);
                }
                else
                {
                    // 其他车道保持不变
                    GenerateLaneMesh(pathPoints, pathNormals, pathTangents, currentOffset, currentOffset + laneWidth, 
                        vertexOffset, segmentCount, roadData.meshOffset);
                }
                currentOffset += laneWidth;
                vertexOffset = m_Positions.Count;
            }
            
            // 生成左侧人行道
            if (leftSidewalkWidth > 0)
            {
                // 如果启用了路缘，先生成左侧路缘
                if (roadData.enableCurb)
                {
                    // 路缘的位置在道路边缘
                    float curbInnerOffset = -halfWidth;
                    float curbOuterOffset = -halfWidth - roadData.curbWidth;
                    
                    // 生成左侧路缘
                    GenerateCurbMesh(pathPoints, pathNormals, pathTangents, curbInnerOffset, curbOuterOffset,
                        m_CurbLeftPositions.Count, segmentCount, roadData.meshOffset, roadData.curbHeight,
                        m_CurbLeftPositions, m_CurbLeftNormals, m_CurbLeftTextures, m_CurbLeftIndices,
                        true); // 左侧路缘
                    
                    // 如果启用了马路牙子，生成左侧马路牙子
                    if (roadData.enableRoadEdge)
                    {
                        // 马路牙子的位置在路缘外侧
                        float roadEdgeInnerOffset = -halfWidth - roadData.curbWidth;
                        float roadEdgeOuterOffset = -halfWidth - roadData.curbWidth - roadData.roadEdgeWidth;
                        
                        // 生成左侧马路牙子（使用带倒角的方法）
                        GenerateRoadEdgeMeshWithChamfer(pathPoints, pathNormals, pathTangents, roadEdgeInnerOffset, roadEdgeOuterOffset,
                            m_RoadEdgeLeftPositions.Count, segmentCount, roadData.meshOffset, roadData.roadEdgeHeight,
                            m_RoadEdgeLeftPositions, m_RoadEdgeLeftNormals, m_RoadEdgeLeftTextures, m_RoadEdgeLeftIndices,
                            true); // 左侧马路牙子
                        
                        // 人行道的起始位置需要考虑路缘和马路牙子宽度
                        GenerateSidewalkMesh(pathPoints, pathNormals, pathTangents, 
                            -halfWidth - roadData.curbWidth - roadData.roadEdgeWidth - leftSidewalkWidth, 
                            -halfWidth - roadData.curbWidth - roadData.roadEdgeWidth, 
                            m_SidewalkLeftPositions.Count, segmentCount, roadData.meshOffset, 
                            m_SidewalkLeftPositions, m_SidewalkLeftNormals, m_SidewalkLeftTextures, m_SidewalkLeftIndices,
                            false); // 左侧人行道不翻转UV
                            
                        // 在人行道外侧添加马路牙子
                        float outerRoadEdgeInnerOffset = -halfWidth - roadData.curbWidth - roadData.roadEdgeWidth - leftSidewalkWidth;
                        float outerRoadEdgeOuterOffset = outerRoadEdgeInnerOffset - roadData.roadEdgeWidth;
                        
                        // 生成左侧人行道外侧的马路牙子（使用带倒角的方法，倒角在最外侧）
                        GenerateOuterRoadEdgeMeshWithChamfer(pathPoints, pathNormals, pathTangents, outerRoadEdgeInnerOffset, outerRoadEdgeOuterOffset,
                            m_RoadEdgeOuterLeftPositions.Count, segmentCount, roadData.meshOffset, roadData.roadEdgeHeight,
                            m_RoadEdgeOuterLeftPositions, m_RoadEdgeOuterLeftNormals, m_RoadEdgeOuterLeftTextures, m_RoadEdgeOuterLeftIndices,
                            true); // 左侧外部马路牙子，倒角在最外侧
                    }
                    else
                    {
                        // 不启用马路牙子，直接生成人行道
                        GenerateSidewalkMesh(pathPoints, pathNormals, pathTangents, -halfWidth - roadData.curbWidth - leftSidewalkWidth, -halfWidth - roadData.curbWidth, 
                            m_SidewalkLeftPositions.Count, segmentCount, roadData.meshOffset, 
                            m_SidewalkLeftPositions, m_SidewalkLeftNormals, m_SidewalkLeftTextures, m_SidewalkLeftIndices,
                            false); // 左侧人行道不翻转UV
                    }
                }
                else
                {
                    // 不启用路缘，直接生成人行道
                    GenerateSidewalkMesh(pathPoints, pathNormals, pathTangents, -halfWidth - leftSidewalkWidth, -halfWidth, 
                        m_SidewalkLeftPositions.Count, segmentCount, roadData.meshOffset, 
                        m_SidewalkLeftPositions, m_SidewalkLeftNormals, m_SidewalkLeftTextures, m_SidewalkLeftIndices,
                        false); // 左侧人行道不翻转UV
                }
            }
            
            // 生成右侧人行道
            if (rightSidewalkWidth > 0)
            {
                // 如果启用了路缘，先生成右侧路缘
                if (roadData.enableCurb)
                {
                    // 路缘的位置在道路边缘
                    float curbInnerOffset = halfWidth;
                    float curbOuterOffset = halfWidth + roadData.curbWidth;
                    
                    // 生成右侧路缘
                    GenerateCurbMesh(pathPoints, pathNormals, pathTangents, curbInnerOffset, curbOuterOffset,
                        m_CurbRightPositions.Count, segmentCount, roadData.meshOffset, roadData.curbHeight,
                        m_CurbRightPositions, m_CurbRightNormals, m_CurbRightTextures, m_CurbRightIndices,
                        false); // 右侧路缘
                    
                    // 如果启用了马路牙子，生成右侧马路牙子
                    if (roadData.enableRoadEdge)
                    {
                        // 马路牙子的位置在路缘外侧
                        float roadEdgeInnerOffset = halfWidth + roadData.curbWidth;
                        float roadEdgeOuterOffset = halfWidth + roadData.curbWidth + roadData.roadEdgeWidth;
                        
                        // 生成右侧马路牙子（使用带倒角的方法）
                        GenerateRoadEdgeMeshWithChamfer(pathPoints, pathNormals, pathTangents, roadEdgeInnerOffset, roadEdgeOuterOffset,
                            m_RoadEdgeRightPositions.Count, segmentCount, roadData.meshOffset, roadData.roadEdgeHeight,
                            m_RoadEdgeRightPositions, m_RoadEdgeRightNormals, m_RoadEdgeRightTextures, m_RoadEdgeRightIndices,
                            false); // 右侧马路牙子
                        
                        // 人行道的起始位置需要考虑路缘和马路牙子宽度
                        GenerateSidewalkMesh(pathPoints, pathNormals, pathTangents, 
                            halfWidth + roadData.curbWidth + roadData.roadEdgeWidth, 
                            halfWidth + roadData.curbWidth + roadData.roadEdgeWidth + rightSidewalkWidth, 
                            m_SidewalkRightPositions.Count, segmentCount, roadData.meshOffset, 
                            m_SidewalkRightPositions, m_SidewalkRightNormals, m_SidewalkRightTextures, m_SidewalkRightIndices,
                            true); // 右侧人行道翻转UV
                            
                        // 在人行道外侧添加马路牙子
                        float outerRoadEdgeInnerOffset = halfWidth + roadData.curbWidth + roadData.roadEdgeWidth + rightSidewalkWidth;
                        float outerRoadEdgeOuterOffset = outerRoadEdgeInnerOffset + roadData.roadEdgeWidth;
                        
                        // 生成右侧人行道外侧的马路牙子（使用带倒角的方法，倒角在最外侧）
                        GenerateOuterRoadEdgeMeshWithChamfer(pathPoints, pathNormals, pathTangents, outerRoadEdgeInnerOffset, outerRoadEdgeOuterOffset,
                            m_RoadEdgeOuterRightPositions.Count, segmentCount, roadData.meshOffset, roadData.roadEdgeHeight,
                            m_RoadEdgeOuterRightPositions, m_RoadEdgeOuterRightNormals, m_RoadEdgeOuterRightTextures, m_RoadEdgeOuterRightIndices,
                            false); // 右侧外部马路牙子，倒角在最外侧
                    }
                    else
                    {
                        // 不启用马路牙子，直接生成人行道
                        GenerateSidewalkMesh(pathPoints, pathNormals, pathTangents, halfWidth + roadData.curbWidth, halfWidth + roadData.curbWidth + rightSidewalkWidth, 
                            m_SidewalkRightPositions.Count, segmentCount, roadData.meshOffset, 
                            m_SidewalkRightPositions, m_SidewalkRightNormals, m_SidewalkRightTextures, m_SidewalkRightIndices,
                            true); // 右侧人行道翻转UV
                    }
                }
                else
                {
                    // 不启用路缘，直接生成人行道
                    GenerateSidewalkMesh(pathPoints, pathNormals, pathTangents, halfWidth, halfWidth + rightSidewalkWidth, 
                        m_SidewalkRightPositions.Count, segmentCount, roadData.meshOffset, 
                        m_SidewalkRightPositions, m_SidewalkRightNormals, m_SidewalkRightTextures, m_SidewalkRightIndices,
                        true); // 右侧人行道翻转UV
                }
            }
        }

        // 生成单个车道的网格
        private void GenerateLaneMesh(List<Vector3> pathPoints, List<Vector3> pathNormals, List<Vector3> pathTangents, 
            float startOffset, float endOffset, int vertexOffset, int segmentCount, float meshOffset)
        {
            // 获取当前正在处理的roadData
            var roadData = m_RoadDatas[m_CurrentProcessingSplineIndex];
            
            // 计算道路总长度
            float totalLength = 0;
            for(int i = 0; i < pathPoints.Count - 1; i++) {
                totalLength += Vector3.Distance(pathPoints[i], pathPoints[i + 1]);
            }
            
            // 根据道路长度计算UV的缩放比例
            float uvScale = totalLength / 10.0f; // 每10个单位长度重复一次纹理
            
            float accumulatedLength = 0;
            float baseV = m_CurrentProcessingSegmentStartArcLength * 0.1f;
            
            for (int i = 0; i <= segmentCount; i++)
            {
                Vector3 position = pathPoints[i];
                Vector3 normal = pathNormals[i];
                Vector3 tangent = pathTangents[i];
                Vector3 biNormal = Vector3.Cross(normal, tangent).normalized;

                // 添加车道两侧的顶点，确保应用meshOffset
                Vector3 leftPoint = position + biNormal * startOffset + Vector3.up * roadData.meshOffset;
                Vector3 rightPoint = position + biNormal * endOffset + Vector3.up * roadData.meshOffset;
                
                // 计算当前点到起点的累积长度
                if(i > 0) {
                    accumulatedLength += Vector3.Distance(pathPoints[i], pathPoints[i-1]);
                }
                
                // 添加顶点
                m_Positions.Add(leftPoint);
                m_Positions.Add(rightPoint);

                // 添加法线
                m_Normals.Add(normal);
                m_Normals.Add(normal);

                // 添加UV（根据累积长度进行映射）
                float v = baseV + accumulatedLength * 0.1f;
                // 应用水平UV偏移
                m_Textures.Add(new Vector2(0 + roadData.uvHorizontalOffset, v));
                m_Textures.Add(new Vector2(1 + roadData.uvHorizontalOffset, v));
            }

            RegisterCunningRoadStrip(vertexOffset, (segmentCount + 1) * 2, m_CurrentProcessingSplineIndex, GetPrimaryRoadMaterial());
        }

        // 生成人行道网格
        public void BeltLoft(Spline spline, int splineIndex, int vertexIndexOffset)
        {
            if (spline == null || spline.Count < 2) return;

            float length = spline.GetLength();
            if (length <= 0.001f) return;

            if (splineIndex < m_RoadDatas.Count)
                SyncRoadExtension(m_Spline, splineIndex);

            m_RoadDatas[splineIndex].ClearPoints();
            float totalWidth = m_RoadDatas[splineIndex].WidthValue;

            var roadData = m_RoadDatas[splineIndex];
            float inWidth = roadData.inWidth;
            float outWidth = roadData.outWidth;
            float inOffset = roadData.inOffset;
            float outOffset = roadData.outOffset;
            var inCurve = roadData.inCurve;
            var outCurve = roadData.outCurve;

            var segmentsPerLength = length / SegmentUnitLength;
            var segments = Mathf.CeilToInt(segmentsPerLength);
            var segmentStepT = SegmentUnitLength / length;
            var steps = segments + 1;
            var vertexCount = steps * 2;
            var prevVertexCount = vertexIndexOffset;
            m_Positions.Capacity += vertexCount;
            m_Normals.Capacity += vertexCount;
            m_Textures.Capacity += vertexCount;

            var t = 0f;
            for (int i = 0; i < steps; i++)
            {
                SplineUtility.Evaluate(spline, t, out var pos, out var dir, out var _);
                var up = Vector3.up;

                var scale = transform.lossyScale;
                var tangent = math.normalizesafe(math.cross(up, dir)) * new float3(1f / scale.x, 1f / scale.y, 1f / scale.z);

                float leftWidth = totalWidth * 0.5f;
                float rightWidth = totalWidth * 0.5f;
                float leftOffset = 0f;
                float rightOffset = 0f;

                float progress = t;

                if (progress < inOffset)
                {
                    float lerpT = Mathf.Clamp01(progress / inOffset);
                    leftWidth = Mathf.Lerp(inWidth * 0.5f, totalWidth * 0.5f, inCurve.Evaluate(lerpT));
                    leftOffset = totalWidth * 0.5f - leftWidth;
                }
                else if (progress > (1f - outOffset))
                {
                    float lerpT = Mathf.Clamp01((progress - (1f - outOffset)) / outOffset);
                    rightWidth = Mathf.Lerp(outWidth * 0.5f, totalWidth * 0.5f, 1f - outCurve.Evaluate(lerpT));
                    rightOffset = totalWidth * 0.5f - rightWidth;
                }

                leftWidth = Mathf.Max(leftWidth, 0.01f);
                rightWidth = Mathf.Max(rightWidth, 0.01f);

                m_RoadDatas[splineIndex].AddPoint(pos);

                var offsetPos = (Vector3)pos + Vector3.up * m_RoadDatas[splineIndex].meshOffset;
                m_Positions.Add(offsetPos - (Vector3)(tangent * leftWidth) + (Vector3)(tangent * leftOffset));
                m_Positions.Add(offsetPos + (Vector3)(tangent * rightWidth) - (Vector3)(tangent * rightOffset));

                m_Normals.Add(up);
                m_Normals.Add(up);
                m_Textures.Add(new Vector2(0f, t * m_TextureScale));
                m_Textures.Add(new Vector2(1f, t * m_TextureScale));

                t = math.min(1f, t + segmentStepT);
            }

            RegisterCunningRoadStrip(prevVertexCount, vertexCount, splineIndex, GetPrimaryRoadMaterial());
        }

        // 生成人行道网格
        private void GenerateSidewalkMesh(List<Vector3> pathPoints, List<Vector3> pathNormals, List<Vector3> pathTangents, 
            float startOffset, float endOffset, int vertexOffset, int segmentCount, float meshOffset,
            List<Vector3> positions, List<Vector3> normals, List<Vector2> textures, List<int> indices,
            bool flipUV = false)
        {
            // 获取当前正在处理的roadData
            var roadData = m_RoadDatas[m_CurrentProcessingSplineIndex];
            
            // 计算道路总长度
            float totalLength = 0;
            for(int i = 0; i < pathPoints.Count - 1; i++) {
                totalLength += Vector3.Distance(pathPoints[i], pathPoints[i + 1]);
            }
            
            // 根据道路长度计算UV的缩放比例
            float uvScale = totalLength / 10.0f; // 每10个单位长度重复一次纹理
            
            float accumulatedLength = 0;
            float sidewalkHeight = 0.15f; // 人行道高度，略高于道路
            float baseV = m_CurrentProcessingSegmentStartArcLength * 0.1f;
            
            for (int i = 0; i <= segmentCount; i++)
            {
                Vector3 position = pathPoints[i];
                Vector3 normal = pathNormals[i];
                Vector3 tangent = pathTangents[i];
                Vector3 biNormal = Vector3.Cross(normal, tangent).normalized;

                // 添加人行道两侧的顶点，稍微抬高一点
                Vector3 innerPoint = position + biNormal * startOffset + Vector3.up * (roadData.meshOffset + sidewalkHeight);
                Vector3 outerPoint = position + biNormal * endOffset + Vector3.up * (roadData.meshOffset + sidewalkHeight);

                // 计算当前点到起点的累积长度
                if(i > 0) {
                    accumulatedLength += Vector3.Distance(pathPoints[i], pathPoints[i-1]);
                }
                
                // 添加顶点
                positions.Add(innerPoint);
                positions.Add(outerPoint);

                // 添加法线
                normals.Add(normal);
                normals.Add(normal);

                // 添加UV（根据累积长度进行映射）
                float v = baseV + accumulatedLength * 0.1f;
                
                if (flipUV)
                {
                    // 翻转UV坐标，应用水平偏移
                    textures.Add(new Vector2(1 + roadData.uvHorizontalOffset, v));
                    textures.Add(new Vector2(0 + roadData.uvHorizontalOffset, v));
                }
                else
                {
                    // 正常UV坐标，应用水平偏移
                    textures.Add(new Vector2(0 + roadData.uvHorizontalOffset, v));
                    textures.Add(new Vector2(1 + roadData.uvHorizontalOffset, v));
                }

                // 添加三角形（除了最后一组顶点）
                if (i < segmentCount)
                {
                    int baseIndex = vertexOffset + i * 2;
                    // 第一个三角形
                    indices.Add(baseIndex);
                    indices.Add(baseIndex + 2);
                    indices.Add(baseIndex + 1);
                    // 第二个三角形
                    indices.Add(baseIndex + 1);
                    indices.Add(baseIndex + 2);
                    indices.Add(baseIndex + 3);
                }
            }

            RegisterCunningSideStrip(
                flipUV ? CunningSideStripSource.SidewalkRight : CunningSideStripSource.SidewalkLeft,
                vertexOffset,
                positions.Count - vertexOffset,
                m_CurrentProcessingSplineIndex,
                roadData.sidewalkMaterial,
                CunningAreaType.Sidewalk,
                ringSize: 2);
        }

        // 生成路缘网格
        private void GenerateCurbMesh(List<Vector3> pathPoints, List<Vector3> pathNormals, List<Vector3> pathTangents, 
            float startOffset, float endOffset, int vertexOffset, int segmentCount, float meshOffset, float curbHeight,
            List<Vector3> positions, List<Vector3> normals, List<Vector2> textures, List<int> indices,
            bool isLeft = true)
        {
            // 获取当前正在处理的roadData
            var roadData = m_RoadDatas[m_CurrentProcessingSplineIndex];
            
            // 计算道路总长度
            float totalLength = 0;
            for(int i = 0; i < pathPoints.Count - 1; i++) {
                totalLength += Vector3.Distance(pathPoints[i], pathPoints[i + 1]);
            }
            
            // 根据道路长度计算UV的缩放比例，并应用自定义UV缩放
            float uvScale = totalLength / 10.0f; // 每10个单位长度重复一次纹理
            
            float accumulatedLength = 0;
            float baseAlong = m_CurrentProcessingSegmentStartArcLength * 0.1f * roadData.curbUVScaleV;
            for (int i = 0; i <= segmentCount; i++)
            {
                Vector3 position = pathPoints[i];
                Vector3 normal = pathNormals[i];
                Vector3 tangent = pathTangents[i];
                Vector3 biNormal = Vector3.Cross(normal, tangent).normalized;

                // 计算侧面法线方向
                Vector3 sideNormal = isLeft ? -biNormal : biNormal; // 侧面法线方向，左侧朝左，右侧朝右

                // 获取倒角参数
                float chamferWidth = roadData.curbChamferWidth;
                float chamferHeight = roadData.curbChamferHeight;
                
                // 添加路缘底部和顶部的顶点
                Vector3 bottomInner = position + biNormal * startOffset + Vector3.up * (roadData.meshOffset);
                Vector3 bottomOuter = position + biNormal * endOffset + Vector3.up * (roadData.meshOffset);
                
                // 添加倒角顶点 - 内侧顶部有倒角
                Vector3 topInner;
                Vector3 chamferPoint;
                
                if (isLeft)
                {
                    // 左侧：倒角向右（向道路内侧）
                    topInner = position + biNormal * (startOffset - chamferWidth) + Vector3.up * (roadData.meshOffset + curbHeight);
                    chamferPoint = position + biNormal * startOffset + Vector3.up * (roadData.meshOffset + curbHeight - chamferHeight);
                }
                else
                {
                    // 右侧：倒角向左（向道路内侧）
                    topInner = position + biNormal * (startOffset + chamferWidth) + Vector3.up * (roadData.meshOffset + curbHeight);
                    chamferPoint = position + biNormal * startOffset + Vector3.up * (roadData.meshOffset + curbHeight - chamferHeight);
                }
                
                Vector3 topOuter = position + biNormal * endOffset + Vector3.up * (roadData.meshOffset + curbHeight);

                // 计算当前点到起点的累积长度
                if(i > 0) {
                    accumulatedLength += Vector3.Distance(pathPoints[i], pathPoints[i-1]);
                }
                
                // 添加顶点 - 现在有5个顶点
                positions.Add(bottomInner); // 0 - 底部内侧
                positions.Add(bottomOuter); // 1 - 底部外侧
                positions.Add(chamferPoint); // 2 - 倒角点
                positions.Add(topInner);    // 3 - 顶部内侧
                positions.Add(topOuter);    // 4 - 顶部外侧

                // 设置法线 - 为每个顶点设置正确的法线方向
                Vector3 upNormal = normal;                // 顶面法线向上
                Vector3 downNormal = -normal;             // 底面法线向下
                Vector3 chamferNormal = Vector3.Normalize(upNormal + sideNormal); // 倒角法线 - 45度角
                
                // 添加法线
                normals.Add(downNormal);    // 底部内侧 - 朝下
                normals.Add(downNormal);    // 底部外侧 - 朝下
                normals.Add(chamferNormal); // 倒角点 - 45度角
                normals.Add(upNormal);      // 顶部内侧 - 向上
                normals.Add(upNormal);      // 顶部外侧 - 向上

                // 添加UV（根据累积长度进行映射），并应用自定义UV缩放
                float v = baseAlong + accumulatedLength * 0.1f * roadData.curbUVScaleV;
                
                // 使用不同的U坐标来确保顶面能够正确展开
                float sideU = 0.0f * roadData.curbUVScaleU;     // 侧面U坐标
                float chamferU = 0.2f * roadData.curbUVScaleU;  // 倒角U坐标
                float topInnerU = 0.3f * roadData.curbUVScaleU; // 顶面内侧U坐标
                float topOuterU = 0.7f * roadData.curbUVScaleU; // 顶面外侧U坐标
                
                // 添加UV
                textures.Add(new Vector2(sideU, v));     // 底部内侧
                textures.Add(new Vector2(sideU, v));     // 底部外侧 - 与内侧使用相同的U坐标，确保重叠
                textures.Add(new Vector2(chamferU, v));  // 倒角点
                textures.Add(new Vector2(topInnerU, v)); // 顶部内侧
                textures.Add(new Vector2(topOuterU, v)); // 顶部外侧 - 使用不同的U坐标，确保顶面展开

                // 添加三角形（除了最后一组顶点）
                if (i < segmentCount)
                {
                    int baseIndex = vertexOffset + i * 5; // 现在每个段有5个顶点
                    
                    // 添加底面三角形生成代码
                    if (isLeft)
                    {
                        // 左侧路缘底面三角形顺序
                        indices.Add(baseIndex);
                        indices.Add(baseIndex + 5);
                        indices.Add(baseIndex + 1);
                        
                        indices.Add(baseIndex + 1);
                        indices.Add(baseIndex + 5);
                        indices.Add(baseIndex + 6);
                    }
                    else
                    {
                        // 右侧路缘底面三角形顺序（反向）
                        indices.Add(baseIndex);
                        indices.Add(baseIndex + 1);
                        indices.Add(baseIndex + 5);
                        
                        indices.Add(baseIndex + 1);
                        indices.Add(baseIndex + 6);
                        indices.Add(baseIndex + 5);
                    }
                    
                    // 顶面 - 法线向上
                    if (isLeft)
                    {
                        // 左侧路缘顶面三角形顺序
                        indices.Add(baseIndex + 3);
                        indices.Add(baseIndex + 4);
                        indices.Add(baseIndex + 8);
                        
                        indices.Add(baseIndex + 4);
                        indices.Add(baseIndex + 9);
                        indices.Add(baseIndex + 8);
                    }
                    else
                    {
                        // 右侧路缘顶面三角形顺序（反向）
                        indices.Add(baseIndex + 3);
                        indices.Add(baseIndex + 8);
                        indices.Add(baseIndex + 4);
                        
                        indices.Add(baseIndex + 4);
                        indices.Add(baseIndex + 8);
                        indices.Add(baseIndex + 9);
                    }
                    
                    // 内侧面 - 现在分为两部分：底部到倒角点，倒角点到顶部
                    if (isLeft)
                    {
                        // 左侧路缘内侧面底部三角形顺序
                        indices.Add(baseIndex);
                        indices.Add(baseIndex + 2);
                        indices.Add(baseIndex + 5);
                        
                        indices.Add(baseIndex + 2);
                        indices.Add(baseIndex + 7);
                        indices.Add(baseIndex + 5);
                        
                        // 左侧路缘内侧面顶部三角形顺序
                        indices.Add(baseIndex + 2);
                        indices.Add(baseIndex + 3);
                        indices.Add(baseIndex + 7);
                        
                        indices.Add(baseIndex + 3);
                        indices.Add(baseIndex + 8);
                        indices.Add(baseIndex + 7);
                    }
                    else
                    {
                        // 右侧路缘内侧面底部三角形顺序（反向）
                        indices.Add(baseIndex);
                        indices.Add(baseIndex + 5);
                        indices.Add(baseIndex + 2);
                        
                        indices.Add(baseIndex + 2);
                        indices.Add(baseIndex + 5);
                        indices.Add(baseIndex + 7);
                        
                        // 右侧路缘内侧面顶部三角形顺序（反向）
                        indices.Add(baseIndex + 2);
                        indices.Add(baseIndex + 7);
                        indices.Add(baseIndex + 3);
                        
                        indices.Add(baseIndex + 3);
                        indices.Add(baseIndex + 7);
                        indices.Add(baseIndex + 8);
                    }
                    
                    // 外侧面 - 法线朝向人行道
                    if (isLeft)
                    {
                        // 左侧路缘外侧面三角形顺序
                        indices.Add(baseIndex + 1);
                        indices.Add(baseIndex + 6);
                        indices.Add(baseIndex + 4);
                        
                        indices.Add(baseIndex + 4);
                        indices.Add(baseIndex + 6);
                        indices.Add(baseIndex + 9);
                    }
                    else
                    {
                        // 右侧路缘外侧面三角形顺序（反向）
                        indices.Add(baseIndex + 1);
                        indices.Add(baseIndex + 4);
                        indices.Add(baseIndex + 6);
                        
                        indices.Add(baseIndex + 4);
                        indices.Add(baseIndex + 9);
                        indices.Add(baseIndex + 6);
                    }
                }
            }

            RegisterCunningSideStrip(
                isLeft ? CunningSideStripSource.CurbLeft : CunningSideStripSource.CurbRight,
                vertexOffset,
                positions.Count - vertexOffset,
                m_CurrentProcessingSplineIndex,
                roadData.curbMaterial,
                CunningAreaType.Curb,
                ringSize: 5);
        }

        private float EaseInOut(float t)
        {
            return t * t * (3f - 2f * t);
        }

        public void ResetBezierKnotRotation()
        {
            if (LoftSplines == null)
                return;

            var progress = 0f;
            var total = LoftSplines.Count;
            foreach (var spline in LoftSplines)
            {
                for (int i = 0; i < spline.Count; i++)
                {
                    var knot = spline[i];
                    if (knot is BezierKnot bezierKnot)
                    {
                        Debug.Log("Reset Rot " + bezierKnot.Rotation);
                        bezierKnot.Rotation = quaternion.identity;
                        Debug.Log("Set Rot " + bezierKnot.Rotation);
                    }
                }
                progress++;
                EditorUtility.DisplayProgressBar("Reset Bezier Knot Rotation", $"Progress: {progress}/{total}", progress / total);
            }
            EditorUtility.ClearProgressBar();
        }

        private void CreateRoadMarkings(Spline spline, int splineIndex)
        {
            if (splineIndex >= m_RoadDatas.Count)
                return;

            var roadData = m_RoadDatas[splineIndex];
            var markingData = roadData.roadMarking;

            if (markingData == null) return;

            gameObject.GetComponentsInChildren<Transform>()
                .Where(t => t.name == "RoadMarkings")
                .ToList()
                .ForEach(t => DestroyImmediate(t.gameObject));
            
            if (!ShowRoadMarkers) return;

            GameObject parentGO = new GameObject("RoadMarkings");
            var markingsParent = parentGO.transform;
            if (markingsParent.parent == null)
                markingsParent.SetParent(this.transform);

            // add new Vector3(0, 0.01f, 0) to avoid z-fighting
            markingsParent.localPosition = new Vector3(0, 0.01f, 0);
            markingsParent.localRotation = Quaternion.identity;
            markingsParent.localScale = Vector3.one;

            var laneInfo = RoadDefaultsInfors.GetDefaultLaneCounts(roadData.roadTypeEnum);
            List<int> laneIds = new List<int>();
            for (int laneId = 1; laneId < laneInfo.rightLaneCount + laneInfo.leftLaneCount; laneId++)
                laneIds.Add(laneId);
            CreateLanesMarks(spline, splineIndex, markingsParent, laneIds);
        }

        private void CreateLanesMarks(Spline spline, int splineIndex, Transform markingsParent, List<int> laneIds)
        {
            var roadData = m_RoadDatas[splineIndex];
            var markingData = roadData.roadMarking;
            float totalLength = spline.GetLength();
            float stepLength = markingData.length + markingData.interval;
            // except for step into road insection corner
            int steps = Mathf.FloorToInt(totalLength / stepLength) - 3;

            float laneWidth = RoadDefaultsInfors.GetDefaultLaneWidth(roadData.roadTypeEnum);
            foreach (var laneId in laneIds)
            {
                var w = m_RoadDatas[splineIndex].WidthValue * 0.5f;
                float laneOffset = -w + laneWidth * laneId;
                // float laneOffset = 0;
                for (int i = 0; i < steps; i++)
                {
                    float t = i * stepLength / totalLength;
                    SplineUtility.Evaluate(spline, t, out var pos, out var dir, out _);

                    if (math.length(dir) == 0)
                    {
                        float3 nextPos = spline.GetPointAtLinearDistance(t, 0.01f, out _);
                        dir = math.normalizesafe(nextPos - pos);

                        if (math.length(dir) == 0)
                        {
                            nextPos = spline.GetPointAtLinearDistance(t, -0.01f, out _);
                            dir = -math.normalizesafe(nextPos - pos);
                        }

                        if (math.length(dir) == 0)
                            dir = new float3(0, 0, 1);
                    }

                    Vector3 up = Vector3.up;
                    var scale = transform.lossyScale;
                    Vector3 tangent = math.normalizesafe(math.cross(up, dir)) * new float3(1f / scale.x, 1f / scale.y, 1f / scale.z);

                    Vector3 markingPosition = (Vector3)pos + tangent * laneOffset + transform.position + Vector3.up * (m_RoadDatas[splineIndex].meshOffset + 0.1f);
                    Quaternion rotation = Quaternion.LookRotation(dir, up);

                    if (i == steps - 2)
                    // if (i == steps)
                    {
                        var arrowPos = markingPosition + tangent * laneWidth * 0.5f;
                        InstantiateRoadDirectionalArrowMark(roadData.roadTypeEnum, laneId, markingsParent, arrowPos, rotation, i);
                    }
                    InstantiateRoadLineMark(roadData.roadTypeEnum, laneId, markingsParent, markingPosition, rotation, i);
                }
            }
        }

        private void InstantiateRoadLineMark(RoadType roadType, int laneId, Transform parentTrans, Vector3 position, Quaternion rotation, int id)
        {
            string prefabPath = RoadDefaultsInfors.GetRoadMarkingLinePrefabPath(roadType, laneId);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            InstantiateGameObjectAndAlignToSurface(prefab, parentTrans, position, rotation, id, laneId);
        }

        private void InstantiateRoadDirectionalArrowMark(RoadType roadType, int laneId, Transform parentTrans, Vector3 position, Quaternion rotation, int id)
        {
            string prefabPath = RoadDefaultsInfors.GetDirectionalArrowPrefabPath(roadType, laneId);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            InstantiateGameObjectAndAlignToSurface(prefab, parentTrans, position, rotation, id, laneId);
        }

        private void InstantiateGameObjectAndAlignToSurface(GameObject prefab, Transform parentTrans, Vector3 position, Quaternion rotation, int id, int laneId)
        {
            if (prefab != null)
            {
                GameObject go = GameObject.Instantiate(prefab, parentTrans);
                go.name = $"{prefab.name}_{laneId}_{id}";
                go.transform.position = position;
                go.transform.rotation = rotation;
                // get raycast hight of Y
                Ray ray = new(position + Vector3.up * 100, Vector3.down);
                if (UnityEngine.Physics.Raycast(ray, out RaycastHit hit, 2000f))
                {
                    go.transform.position = new Vector3(position.x, hit.point.y + 0.01f, position.z);
                }
            }
        }

        private bool IsPointBetween(Vector3 A, Vector3 judgePoint, Vector3 C)
        {
            Vector3 AB = judgePoint - A;
            Vector3 AC = C - A;
            Vector3 BC = C - judgePoint;

            float dotProduct = Vector3.Dot(AB, AC);

            if (dotProduct > 0 && AB.magnitude + BC.magnitude >= AC.magnitude)
            {
                return true;
            }
            return false;
        }

        public void SwitchRoadMarkersStatus()
        {
            ShowRoadMarkers = !ShowRoadMarkers;
        }

        private void OnDrawGizmos()
        {
            // 检查是否应该显示Gizmos
            if (m_Spline == null || LoftSplines == null || LoftSplines.Count == 0 || !s_ShowLaneGizmos)
                return;
            
            // 如果使用了外部控制，可以在这里添加额外检查
            #if UNITY_EDITOR
            // 编辑器专用代码，通过静态变量控制
            #endif

            // 绘制车道网格的连线
            DrawLaneMeshLines();

            // 设置边缘线的颜色
            Color sidewalkInnerEdgeColor = Color.yellow; // 内边缘线用黄色表示
            Color sidewalkOuterEdgeColor = new Color(0.5f, 0f, 0.5f); // 外边缘线用紫色表示
            Color roadEndWidthColor = Color.green; // 道路端点宽度线用绿色表示
            Color sidewalkEndWidthColor = new Color(0.5f, 0f, 0.5f); // 人行道端点宽度线用紫色表示
            
            for (int splineIndex = 0; splineIndex < LoftSplines.Count; splineIndex++)
            {
                if (splineIndex >= m_RoadDatas.Count)
                    continue;
                
                var spline = LoftSplines[splineIndex];
                var roadData = m_RoadDatas[splineIndex];
                
                // 获取车道宽度和人行道宽度
                float totalLaneCount = roadData.leftLaneCount + roadData.rightLaneCount;
                float laneWidth = roadData.laneWidth.DefaultValue;
                float totalWidth = totalLaneCount * laneWidth;
                float halfWidth = totalWidth / 2f;
                float leftSidewalkWidth = roadData.leftSidewalkWidth.DefaultValue;
                float rightSidewalkWidth = roadData.rightSidewalkWidth.DefaultValue;
                
                // 绘制道路起点和终点的宽度线
                // 起点（t=0）
                Vector3 startPos = spline.EvaluatePosition(0);
                Vector3 startTangent = (Vector3)spline.EvaluateTangent(0);
                startTangent.Normalize();
                Vector3 startBiNormal = Vector3.Cross(Vector3.up, startTangent).normalized;
                
                // 道路宽度线（绿色）
                Vector3 startLeftRoad = startPos + startBiNormal * (-halfWidth) + Vector3.up * (roadData.meshOffset + 0.15f);
                Vector3 startRightRoad = startPos + startBiNormal * (halfWidth) + Vector3.up * (roadData.meshOffset + 0.15f);
                Gizmos.color = roadEndWidthColor;
                Gizmos.DrawLine(startLeftRoad, startRightRoad);
                
                // 人行道宽度线（紫色）
                if (leftSidewalkWidth > 0)
                {
                    Vector3 startLeftSidewalk = startPos + startBiNormal * (-halfWidth - leftSidewalkWidth) + Vector3.up * (roadData.meshOffset + 0.15f);
                    Gizmos.color = sidewalkEndWidthColor;
                    Gizmos.DrawLine(startLeftRoad, startLeftSidewalk);
                }
                if (rightSidewalkWidth > 0)
                {
                    Vector3 startRightSidewalk = startPos + startBiNormal * (halfWidth + rightSidewalkWidth) + Vector3.up * (roadData.meshOffset + 0.15f);
                    Gizmos.color = sidewalkEndWidthColor;
                    Gizmos.DrawLine(startRightRoad, startRightSidewalk);
                }
                
                // 终点（t=1）
                Vector3 endPos = spline.EvaluatePosition(1);
                Vector3 endTangent = (Vector3)spline.EvaluateTangent(1);
                endTangent.Normalize();
                Vector3 endBiNormal = Vector3.Cross(Vector3.up, endTangent).normalized;
                
                // 道路宽度线（绿色）
                Vector3 endLeftRoad = endPos + endBiNormal * (-halfWidth) + Vector3.up * (roadData.meshOffset + 0.15f);
                Vector3 endRightRoad = endPos + endBiNormal * (halfWidth) + Vector3.up * (roadData.meshOffset + 0.15f);
                Gizmos.color = roadEndWidthColor;
                Gizmos.DrawLine(endLeftRoad, endRightRoad);
                
                // 人行道宽度线（紫色）
                if (leftSidewalkWidth > 0)
                {
                    Vector3 endLeftSidewalk = endPos + endBiNormal * (-halfWidth - leftSidewalkWidth) + Vector3.up * (roadData.meshOffset + 0.15f);
                    Gizmos.color = sidewalkEndWidthColor;
                    Gizmos.DrawLine(endLeftRoad, endLeftSidewalk);
                }
                if (rightSidewalkWidth > 0)
                {
                    Vector3 endRightSidewalk = endPos + endBiNormal * (halfWidth + rightSidewalkWidth) + Vector3.up * (roadData.meshOffset + 0.15f);
                    Gizmos.color = sidewalkEndWidthColor;
                    Gizmos.DrawLine(endRightRoad, endRightSidewalk);
                }
                
                // 原有的边缘线绘制代码保持不变
                if (leftSidewalkWidth <= 0 && rightSidewalkWidth <= 0)
                    continue;
                
                // 计算路径上的采样点
                float splineLength = spline.GetLength();
                int segmentCount = Mathf.Max(2, Mathf.CeilToInt(splineLength / SegmentUnitLength));
                float step = 1f / segmentCount;
                
                // 绘制左侧人行道边缘线
                if (leftSidewalkWidth > 0)
                {
                    Vector3 prevLeftInner = Vector3.zero;
                    Vector3 prevLeftOuter = Vector3.zero;
                    
                    for (int i = 0; i <= segmentCount; i++)
                    {
                        float t = i * step;
                        Vector3 position = spline.EvaluatePosition(t);
                        Vector3 tangent = spline.EvaluateTangent(t);
                        tangent = tangent.normalized;
                        Vector3 normal = Vector3.up;
                        Vector3 biNormal = Vector3.Cross(normal, tangent).normalized;
                        
                        // 计算左侧人行道内外边缘点
                        Vector3 leftInner = position + biNormal * (-halfWidth) + Vector3.up * (roadData.meshOffset + 0.15f);
                        Vector3 leftOuter = position + biNormal * (-halfWidth - leftSidewalkWidth) + Vector3.up * (roadData.meshOffset + 0.15f);
                        
                        // 绘制线段（从第二个点开始）
                        if (i > 0)
                        {
                            // 绘制内侧边缘线
                            Gizmos.color = sidewalkInnerEdgeColor;
                            Gizmos.DrawLine(prevLeftInner, leftInner);
                            
                            // 绘制外侧边缘线
                            Gizmos.color = sidewalkOuterEdgeColor;
                            Gizmos.DrawLine(prevLeftOuter, leftOuter);
                        }
                        
                        prevLeftInner = leftInner;
                        prevLeftOuter = leftOuter;
                    }
                }
                
                // 绘制右侧人行道边缘线
                if (rightSidewalkWidth > 0)
                {
                    Vector3 prevRightInner = Vector3.zero;
                    Vector3 prevRightOuter = Vector3.zero;
                    
                    for (int i = 0; i <= segmentCount; i++)
                    {
                        float t = i * step;
                        Vector3 position = spline.EvaluatePosition(t);
                        Vector3 tangent = spline.EvaluateTangent(t);
                        tangent = tangent.normalized;
                        Vector3 normal = Vector3.up;
                        Vector3 biNormal = Vector3.Cross(normal, tangent).normalized;
                        
                        // 计算右侧人行道内外边缘点
                        Vector3 rightInner = position + biNormal * (halfWidth) + Vector3.up * (roadData.meshOffset + 0.15f);
                        Vector3 rightOuter = position + biNormal * (halfWidth + rightSidewalkWidth) + Vector3.up * (roadData.meshOffset + 0.15f);
                        
                        // 绘制线段（从第二个点开始）
                        if (i > 0)
                        {
                            // 绘制内侧边缘线
                            Gizmos.color = sidewalkInnerEdgeColor;
                            Gizmos.DrawLine(prevRightInner, rightInner);
                            
                            // 绘制外侧边缘线
                            Gizmos.color = sidewalkOuterEdgeColor;
                            Gizmos.DrawLine(prevRightOuter, rightOuter);
                        }
                        
                        prevRightInner = rightInner;
                        prevRightOuter = rightOuter;
                    }
                }
            }
        }

        /*
                public void ExportAllRoads()
                {
                    var path = EditorUtility.SaveFilePanel("导出曲线", "", "曲线数据", "json");
                    if (string.IsNullOrEmpty(path))
                        return;

                    SplineDataContainer splineDataContainer = new SplineDataContainer
                    {
                        splines = new List<Spline>(m_Spline.Splines),
                        roadDatas = RoadExtensionDatas,
                    };
                    string json = JsonUtility.ToJson(splineDataContainer, true);
                    System.IO.File.WriteAllText(path, json);
                }
        */

        public Mesh RoadEdgeLeftMesh
        {
            get
            {
                if (m_RoadEdgeLeftMesh == null)
                    m_RoadEdgeLeftMesh = new Mesh();
                return m_RoadEdgeLeftMesh;
            }
        }
        
        public Mesh RoadEdgeRightMesh
        {
            get
            {
                if (m_RoadEdgeRightMesh == null)
                {
                    m_RoadEdgeRightMesh = new Mesh();
                    m_RoadEdgeRightMesh.name = "RoadEdgeRightMesh";
                }
                return m_RoadEdgeRightMesh;
            }
        }

        // 生成马路牙子网格
        private void GenerateRoadEdgeMesh(List<Vector3> pathPoints, List<Vector3> pathNormals, List<Vector3> pathTangents, 
            float startOffset, float endOffset, int vertexOffset, int segmentCount, float meshOffset, float roadEdgeHeight,
            List<Vector3> positions, List<Vector3> normals, List<Vector2> textures, List<int> indices,
            bool isLeft = true)
        {
            // 获取当前正在处理的roadData
            var roadData = m_RoadDatas[m_CurrentProcessingSplineIndex];
            
            // 计算道路总长度
            float totalLength = 0;
            for(int i = 0; i < pathPoints.Count - 1; i++) {
                totalLength += Vector3.Distance(pathPoints[i], pathPoints[i + 1]);
            }
            
            // 根据道路长度计算UV的缩放比例，并应用自定义UV缩放
            float uvScale = totalLength / 10.0f; // 每10个单位长度重复一次纹理
            
            float accumulatedLength = 0;
            float baseAlong = m_CurrentProcessingSegmentStartArcLength * 0.1f * roadData.roadEdgeUVScaleV;
            for (int i = 0; i <= segmentCount; i++)
            {
                Vector3 position = pathPoints[i];
                Vector3 normal = pathNormals[i];
                Vector3 tangent = pathTangents[i];
                Vector3 biNormal = Vector3.Cross(normal, tangent).normalized;

                // 计算侧面法线方向
                Vector3 sideNormal = isLeft ? -biNormal : biNormal; // 侧面法线方向，左侧朝左，右侧朝右

                // 获取倒角参数
                float chamferWidth = roadData.roadEdgeChamferWidth;
                float chamferHeight = roadData.roadEdgeChamferHeight;
                
                // 添加马路牙子底部和顶部的顶点
                Vector3 bottomInner = position + biNormal * startOffset + Vector3.up * (roadData.meshOffset);
                Vector3 bottomOuter = position + biNormal * endOffset + Vector3.up * (roadData.meshOffset);
                
                // 添加倒角顶点 - 内侧顶部有倒角
                Vector3 topInner = position + biNormal * (startOffset + chamferWidth) + Vector3.up * (roadData.meshOffset + roadEdgeHeight);
                Vector3 topOuter = position + biNormal * endOffset + Vector3.up * (roadData.meshOffset + roadEdgeHeight);
                
                // 添加倒角中间点 - 内侧倒角的连接点
                Vector3 chamferPoint = position + biNormal * startOffset + Vector3.up * (roadData.meshOffset + roadEdgeHeight - chamferHeight);

                // 计算当前点到起点的累积长度
                if(i > 0) {
                    accumulatedLength += Vector3.Distance(pathPoints[i], pathPoints[i-1]);
                }
                
                // 添加顶点 - 现在有5个顶点
                positions.Add(bottomInner); // 0 - 底部内侧
                positions.Add(bottomOuter); // 1 - 底部外侧
                positions.Add(chamferPoint); // 2 - 倒角点
                positions.Add(topInner);    // 3 - 顶部内侧
                positions.Add(topOuter);    // 4 - 顶部外侧

                // 设置法线 - 为每个顶点设置正确的法线方向
                Vector3 upNormal = normal;                // 顶面法线向上
                Vector3 downNormal = -normal;             // 底面法线向下
                Vector3 chamferNormal = Vector3.Normalize(upNormal + sideNormal); // 倒角法线 - 45度角
                
                // 添加法线
                normals.Add(downNormal);    // 底部内侧 - 朝下
                normals.Add(downNormal);    // 底部外侧 - 朝下
                normals.Add(chamferNormal); // 倒角点 - 45度角
                normals.Add(upNormal);      // 顶部内侧 - 向上
                normals.Add(upNormal);      // 顶部外侧 - 向上

                // 添加UV（根据累积长度进行映射），并应用自定义UV缩放
                float v = baseAlong + accumulatedLength * 0.1f * roadData.roadEdgeUVScaleV;
                
                // 旋转UV 90度，交换U和V坐标，并确保顶面能够正确展开
                float sideV = 0.0f * roadData.roadEdgeUVScaleU;     // 侧面V坐标
                float chamferV = 0.2f * roadData.roadEdgeUVScaleU;  // 倒角V坐标
                float topInnerV = 0.3f * roadData.roadEdgeUVScaleU; // 顶面内侧V坐标
                float topOuterV = 0.7f * roadData.roadEdgeUVScaleU; // 顶面外侧V坐标
                
                // 添加UV
                textures.Add(new Vector2(v, sideV));     // 底部内侧
                textures.Add(new Vector2(v, sideV));     // 底部外侧 - 与内侧使用相同的V坐标，确保重叠
                textures.Add(new Vector2(v, chamferV));  // 倒角点
                textures.Add(new Vector2(v, topInnerV)); // 顶部内侧
                textures.Add(new Vector2(v, topOuterV)); // 顶部外侧 - 使用不同的V坐标，确保顶面展开

                // 添加三角形（除了最后一组顶点）
                if (i < segmentCount)
                {
                    int baseIndex = vertexOffset + i * 5; // 现在每个段有5个顶点
                    
                    // 添加底面三角形生成代码
                    if (isLeft)
                    {
                        // 左侧马路牙子底面三角形顺序
                        indices.Add(baseIndex);
                        indices.Add(baseIndex + 5);
                        indices.Add(baseIndex + 1);
                        
                        indices.Add(baseIndex + 1);
                        indices.Add(baseIndex + 5);
                        indices.Add(baseIndex + 6);
                    }
                    else
                    {
                        // 右侧马路牙子底面三角形顺序（反向）
                        indices.Add(baseIndex);
                        indices.Add(baseIndex + 1);
                        indices.Add(baseIndex + 5);
                        
                        indices.Add(baseIndex + 1);
                        indices.Add(baseIndex + 6);
                        indices.Add(baseIndex + 5);
                    }
                    
                    // 顶面 - 法线向上
                    if (isLeft)
                    {
                        // 左侧马路牙子顶面三角形顺序
                        indices.Add(baseIndex + 3);
                        indices.Add(baseIndex + 4);
                        indices.Add(baseIndex + 8);
                        
                        indices.Add(baseIndex + 4);
                        indices.Add(baseIndex + 9);
                        indices.Add(baseIndex + 8);
                    }
                    else
                    {
                        // 右侧马路牙子顶面三角形顺序（反向）
                        indices.Add(baseIndex + 3);
                        indices.Add(baseIndex + 8);
                        indices.Add(baseIndex + 4);
                        
                        indices.Add(baseIndex + 4);
                        indices.Add(baseIndex + 8);
                        indices.Add(baseIndex + 9);
                    }
                    
                    // 内侧面 - 现在分为两部分：底部到倒角点，倒角点到顶部
                    if (isLeft)
                    {
                        // 左侧马路牙子内侧面底部三角形顺序
                        indices.Add(baseIndex);
                        indices.Add(baseIndex + 2);
                        indices.Add(baseIndex + 5);
                        
                        indices.Add(baseIndex + 2);
                        indices.Add(baseIndex + 7);
                        indices.Add(baseIndex + 5);
                        
                        // 左侧马路牙子内侧面顶部三角形顺序
                        indices.Add(baseIndex + 2);
                        indices.Add(baseIndex + 3);
                        indices.Add(baseIndex + 7);
                        
                        indices.Add(baseIndex + 3);
                        indices.Add(baseIndex + 8);
                        indices.Add(baseIndex + 7);
                    }
                    else
                    {
                        // 右侧马路牙子内侧面底部三角形顺序（反向）
                        indices.Add(baseIndex);
                        indices.Add(baseIndex + 5);
                        indices.Add(baseIndex + 2);
                        
                        indices.Add(baseIndex + 2);
                        indices.Add(baseIndex + 5);
                        indices.Add(baseIndex + 7);
                        
                        // 右侧马路牙子内侧面顶部三角形顺序（反向）
                        indices.Add(baseIndex + 2);
                        indices.Add(baseIndex + 7);
                        indices.Add(baseIndex + 3);
                        
                        indices.Add(baseIndex + 3);
                        indices.Add(baseIndex + 7);
                        indices.Add(baseIndex + 8);
                    }
                    
                    // 外侧面 - 法线朝向人行道
                    if (isLeft)
                    {
                        // 左侧马路牙子外侧面三角形顺序
                        indices.Add(baseIndex + 1);
                        indices.Add(baseIndex + 6);
                        indices.Add(baseIndex + 4);
                        
                        indices.Add(baseIndex + 4);
                        indices.Add(baseIndex + 6);
                        indices.Add(baseIndex + 9);
                    }
                    else
                    {
                        // 右侧马路牙子外侧面三角形顺序（反向）
                        indices.Add(baseIndex + 1);
                        indices.Add(baseIndex + 4);
                        indices.Add(baseIndex + 6);
                        
                        indices.Add(baseIndex + 4);
                        indices.Add(baseIndex + 9);
                        indices.Add(baseIndex + 6);
                    }
                }
            }

            RegisterCunningSideStrip(
                isLeft ? CunningSideStripSource.RoadEdgeOuterLeft : CunningSideStripSource.RoadEdgeOuterRight,
                vertexOffset,
                positions.Count - vertexOffset,
                m_CurrentProcessingSplineIndex,
                roadData.roadEdgeMaterial,
                CunningAreaType.RoadEdgeOuter,
                ringSize: 5);
        }

        // 生成带倒角的马路牙子网格
        private void GenerateRoadEdgeMeshWithChamfer(List<Vector3> pathPoints, List<Vector3> pathNormals, List<Vector3> pathTangents, 
            float startOffset, float endOffset, int vertexOffset, int segmentCount, float meshOffset, float roadEdgeHeight,
            List<Vector3> positions, List<Vector3> normals, List<Vector2> textures, List<int> indices,
            bool isLeft = true)
        {
            // 获取当前正在处理的roadData
            var roadData = m_RoadDatas[m_CurrentProcessingSplineIndex];
            
            // 计算道路总长度
            float totalLength = 0;
            for(int i = 0; i < pathPoints.Count - 1; i++) {
                totalLength += Vector3.Distance(pathPoints[i], pathPoints[i + 1]);
            }
            
            // 根据道路长度计算UV的缩放比例，并应用自定义UV缩放
            float uvScale = totalLength / 10.0f; // 每10个单位长度重复一次纹理
            
            float accumulatedLength = 0;
            float baseAlong = m_CurrentProcessingSegmentStartArcLength * 0.1f * roadData.roadEdgeUVScaleV;
            for (int i = 0; i <= segmentCount; i++)
            {
                Vector3 position = pathPoints[i];
                Vector3 normal = pathNormals[i];
                Vector3 tangent = pathTangents[i];
                Vector3 biNormal = Vector3.Cross(normal, tangent).normalized;

                // 计算侧面法线方向
                Vector3 sideNormal = isLeft ? -biNormal : biNormal; // 侧面法线方向，左侧朝左，右侧朝右

                // 获取倒角参数
                float chamferWidth = roadData.roadEdgeChamferWidth;
                float chamferHeight = roadData.roadEdgeChamferHeight;
                
                // 添加马路牙子底部和顶部的顶点
                Vector3 bottomInner = position + biNormal * startOffset + Vector3.up * (roadData.meshOffset);
                Vector3 bottomOuter = position + biNormal * endOffset + Vector3.up * (roadData.meshOffset);
                
                // 添加倒角顶点 - 内侧顶部有倒角
                Vector3 topInner;
                Vector3 chamferPoint;
                
                if (isLeft)
                {
                    // 左侧：倒角向右（向道路内侧）
                    topInner = position + biNormal * (startOffset - chamferWidth) + Vector3.up * (roadData.meshOffset + roadEdgeHeight);
                    chamferPoint = position + biNormal * startOffset + Vector3.up * (roadData.meshOffset + roadEdgeHeight - chamferHeight);
                }
                else
                {
                    // 右侧：倒角向左（向道路内侧）
                    topInner = position + biNormal * (startOffset + chamferWidth) + Vector3.up * (roadData.meshOffset + roadEdgeHeight);
                    chamferPoint = position + biNormal * startOffset + Vector3.up * (roadData.meshOffset + roadEdgeHeight - chamferHeight);
                }
                
                Vector3 topOuter = position + biNormal * endOffset + Vector3.up * (roadData.meshOffset + roadEdgeHeight);

                // 计算当前点到起点的累积长度
                if(i > 0) {
                    accumulatedLength += Vector3.Distance(pathPoints[i], pathPoints[i-1]);
                }
                
                // 添加顶点 - 现在有5个顶点
                positions.Add(bottomInner); // 0 - 底部内侧
                positions.Add(bottomOuter); // 1 - 底部外侧
                positions.Add(chamferPoint); // 2 - 倒角点
                positions.Add(topInner);    // 3 - 顶部内侧
                positions.Add(topOuter);    // 4 - 顶部外侧

                // 设置法线 - 为每个顶点设置正确的法线方向
                Vector3 upNormal = normal;                // 顶面法线向上
                Vector3 downNormal = -normal;             // 底面法线向下
                Vector3 chamferNormal = Vector3.Normalize(upNormal + sideNormal); // 倒角法线 - 45度角
                
                // 添加法线
                normals.Add(downNormal);    // 底部内侧 - 朝下
                normals.Add(downNormal);    // 底部外侧 - 朝下
                normals.Add(chamferNormal); // 倒角点 - 45度角
                normals.Add(upNormal);      // 顶部内侧 - 向上
                normals.Add(upNormal);      // 顶部外侧 - 向上

                // 添加UV（根据累积长度进行映射），并应用自定义UV缩放
                float v = baseAlong + accumulatedLength * 0.1f * roadData.roadEdgeUVScaleV;
                
                // 旋转UV 90度，交换U和V坐标，并确保顶面能够正确展开
                float sideV = 0.0f * roadData.roadEdgeUVScaleU;     // 侧面V坐标
                float chamferV = 0.2f * roadData.roadEdgeUVScaleU;  // 倒角V坐标
                float topInnerV = 0.3f * roadData.roadEdgeUVScaleU; // 顶面内侧V坐标
                float topOuterV = 0.7f * roadData.roadEdgeUVScaleU; // 顶面外侧V坐标
                
                // 添加UV
                textures.Add(new Vector2(v, sideV));     // 底部内侧
                textures.Add(new Vector2(v, sideV));     // 底部外侧 - 与内侧使用相同的V坐标，确保重叠
                textures.Add(new Vector2(v, chamferV));  // 倒角点
                textures.Add(new Vector2(v, topInnerV)); // 顶部内侧
                textures.Add(new Vector2(v, topOuterV)); // 顶部外侧 - 使用不同的V坐标，确保顶面展开

                // 添加三角形（除了最后一组顶点）
                if (i < segmentCount)
                {
                    int baseIndex = vertexOffset + i * 5; // 现在每个段有5个顶点
                    
                    // 添加底面三角形生成代码
                    if (isLeft)
                    {
                        // 左侧马路牙子底面三角形顺序
                        indices.Add(baseIndex);
                        indices.Add(baseIndex + 5);
                        indices.Add(baseIndex + 1);
                        
                        indices.Add(baseIndex + 1);
                        indices.Add(baseIndex + 5);
                        indices.Add(baseIndex + 6);
                    }
                    else
                    {
                        // 右侧马路牙子底面三角形顺序（反向）
                        indices.Add(baseIndex);
                        indices.Add(baseIndex + 1);
                        indices.Add(baseIndex + 5);
                        
                        indices.Add(baseIndex + 1);
                        indices.Add(baseIndex + 6);
                        indices.Add(baseIndex + 5);
                    }
                    
                    // 顶面 - 法线向上
                    if (isLeft)
                    {
                        // 左侧马路牙子顶面三角形顺序
                        indices.Add(baseIndex + 3);
                        indices.Add(baseIndex + 4);
                        indices.Add(baseIndex + 8);
                        
                        indices.Add(baseIndex + 4);
                        indices.Add(baseIndex + 9);
                        indices.Add(baseIndex + 8);
                    }
                    else
                    {
                        // 右侧马路牙子顶面三角形顺序（反向）
                        indices.Add(baseIndex + 3);
                        indices.Add(baseIndex + 8);
                        indices.Add(baseIndex + 4);
                        
                        indices.Add(baseIndex + 4);
                        indices.Add(baseIndex + 8);
                        indices.Add(baseIndex + 9);
                    }
                    
                    // 内侧面 - 现在分为两部分：底部到倒角点，倒角点到顶部
                    if (isLeft)
                    {
                        // 左侧马路牙子内侧面底部三角形顺序
                        indices.Add(baseIndex);
                        indices.Add(baseIndex + 2);
                        indices.Add(baseIndex + 5);
                        
                        indices.Add(baseIndex + 2);
                        indices.Add(baseIndex + 7);
                        indices.Add(baseIndex + 5);
                        
                        // 左侧马路牙子内侧面顶部三角形顺序
                        indices.Add(baseIndex + 2);
                        indices.Add(baseIndex + 3);
                        indices.Add(baseIndex + 7);
                        
                        indices.Add(baseIndex + 3);
                        indices.Add(baseIndex + 8);
                        indices.Add(baseIndex + 7);
                    }
                    else
                    {
                        // 右侧马路牙子内侧面底部三角形顺序（反向）
                        indices.Add(baseIndex);
                        indices.Add(baseIndex + 5);
                        indices.Add(baseIndex + 2);
                        
                        indices.Add(baseIndex + 2);
                        indices.Add(baseIndex + 5);
                        indices.Add(baseIndex + 7);
                        
                        // 右侧马路牙子内侧面顶部三角形顺序（反向）
                        indices.Add(baseIndex + 2);
                        indices.Add(baseIndex + 7);
                        indices.Add(baseIndex + 3);
                        
                        indices.Add(baseIndex + 3);
                        indices.Add(baseIndex + 7);
                        indices.Add(baseIndex + 8);
                    }
                    
                    // 外侧面 - 法线朝向人行道
                    if (isLeft)
                    {
                        // 左侧马路牙子外侧面三角形顺序
                        indices.Add(baseIndex + 1);
                        indices.Add(baseIndex + 6);
                        indices.Add(baseIndex + 4);
                        
                        indices.Add(baseIndex + 4);
                        indices.Add(baseIndex + 6);
                        indices.Add(baseIndex + 9);
                    }
                    else
                    {
                        // 右侧马路牙子外侧面三角形顺序（反向）
                        indices.Add(baseIndex + 1);
                        indices.Add(baseIndex + 4);
                        indices.Add(baseIndex + 6);
                        
                        indices.Add(baseIndex + 4);
                        indices.Add(baseIndex + 9);
                        indices.Add(baseIndex + 6);
                    }
                }
            }

            RegisterCunningSideStrip(
                isLeft ? CunningSideStripSource.RoadEdgeLeft : CunningSideStripSource.RoadEdgeRight,
                vertexOffset,
                positions.Count - vertexOffset,
                m_CurrentProcessingSplineIndex,
                roadData.roadEdgeMaterial,
                CunningAreaType.RoadEdge,
                ringSize: 5);
        }

        // 人行道外侧马路牙子的网格属性
        [SerializeField]
        private Mesh m_RoadEdgeOuterLeftMesh;
        
        public Mesh RoadEdgeOuterLeftMesh
        {
            get
            {
                if (m_RoadEdgeOuterLeftMesh == null)
                {
                    m_RoadEdgeOuterLeftMesh = new Mesh();
                    m_RoadEdgeOuterLeftMesh.name = "RoadEdgeOuterLeftMesh";
                }
                return m_RoadEdgeOuterLeftMesh;
            }
        }
        
        [SerializeField]
        private Mesh m_RoadEdgeOuterRightMesh;
        
        public Mesh RoadEdgeOuterRightMesh
        {
            get
            {
                if (m_RoadEdgeOuterRightMesh == null)
                {
                    m_RoadEdgeOuterRightMesh = new Mesh();
                    m_RoadEdgeOuterRightMesh.name = "RoadEdgeOuterRightMesh";
                }
                return m_RoadEdgeOuterRightMesh;
            }
        }

        // 生成人行道外侧马路牙子的网格，倒角在最外侧
        private void GenerateOuterRoadEdgeMeshWithChamfer(List<Vector3> pathPoints, List<Vector3> pathNormals, List<Vector3> pathTangents, 
            float startOffset, float endOffset, int vertexOffset, int segmentCount, float meshOffset, float roadEdgeHeight,
            List<Vector3> positions, List<Vector3> normals, List<Vector2> textures, List<int> indices,
            bool isLeft = true)
        {
            // 获取当前正在处理的roadData
            var roadData = m_RoadDatas[m_CurrentProcessingSplineIndex];
            
            // 计算道路总长度
            float totalLength = 0;
            for(int i = 0; i < pathPoints.Count - 1; i++) {
                totalLength += Vector3.Distance(pathPoints[i], pathPoints[i + 1]);
            }
            
            // 根据道路长度计算UV的缩放比例，并应用自定义UV缩放
            float uvScale = totalLength / 10.0f; // 每10个单位长度重复一次纹理
            
            float accumulatedLength = 0;
            for (int i = 0; i <= segmentCount; i++)
            {
                Vector3 position = pathPoints[i];
                Vector3 normal = pathNormals[i];
                Vector3 tangent = pathTangents[i];
                Vector3 biNormal = Vector3.Cross(normal, tangent).normalized;

                // 计算侧面法线方向
                Vector3 sideNormal = isLeft ? -biNormal : biNormal; // 侧面法线方向，左侧朝左，右侧朝右

                // 获取倒角参数
                float chamferWidth = roadData.roadEdgeChamferWidth;
                float chamferHeight = roadData.roadEdgeChamferHeight;
                
                // 添加马路牙子底部和顶部的顶点
                Vector3 bottomInner = position + biNormal * startOffset + Vector3.up * (roadData.meshOffset);
                Vector3 bottomOuter = position + biNormal * endOffset + Vector3.up * (roadData.meshOffset);
                
                // 添加倒角顶点 - 外侧顶部有倒角
                Vector3 topInner = position + biNormal * startOffset + Vector3.up * (roadData.meshOffset + roadEdgeHeight);
                Vector3 topOuter;
                Vector3 chamferPoint;
                
                if (isLeft)
                {
                    // 左侧：倒角向左（向最外侧）
                    topOuter = position + biNormal * (endOffset + chamferWidth) + Vector3.up * (roadData.meshOffset + roadEdgeHeight);
                    chamferPoint = position + biNormal * endOffset + Vector3.up * (roadData.meshOffset + roadEdgeHeight - chamferHeight);
                }
                else
                {
                    // 右侧：倒角向右（向最外侧）
                    topOuter = position + biNormal * (endOffset - chamferWidth) + Vector3.up * (roadData.meshOffset + roadEdgeHeight);
                    chamferPoint = position + biNormal * endOffset + Vector3.up * (roadData.meshOffset + roadEdgeHeight - chamferHeight);
                }

                // 计算当前点到起点的累积长度
                if(i > 0) {
                    accumulatedLength += Vector3.Distance(pathPoints[i], pathPoints[i-1]);
                }
                
                // 添加顶点 - 现在有5个顶点
                positions.Add(bottomInner); // 0 - 底部内侧
                positions.Add(bottomOuter); // 1 - 底部外侧
                positions.Add(chamferPoint); // 2 - 倒角点
                positions.Add(topInner);    // 3 - 顶部内侧
                positions.Add(topOuter);    // 4 - 顶部外侧

                // 设置法线 - 为每个顶点设置正确的法线方向
                Vector3 upNormal = normal;                // 顶面法线向上
                Vector3 downNormal = -normal;             // 底面法线向下
                Vector3 chamferNormal = Vector3.Normalize(upNormal + sideNormal); // 倒角法线 - 45度角
                
                // 添加法线
                normals.Add(downNormal);    // 底部内侧 - 朝下
                normals.Add(downNormal);    // 底部外侧 - 朝下
                normals.Add(chamferNormal); // 倒角点 - 45度角
                normals.Add(upNormal);      // 顶部内侧 - 向上
                normals.Add(upNormal);      // 顶部外侧 - 向上

                // 添加UV（根据累积长度进行映射），并应用自定义UV缩放
                float v = (accumulatedLength / totalLength) * uvScale * roadData.roadEdgeUVScaleV;
                
                // 旋转UV 90度，交换U和V坐标，并确保顶面能够正确展开
                float sideV = 0.0f * roadData.roadEdgeUVScaleU;     // 侧面V坐标
                float chamferV = 0.2f * roadData.roadEdgeUVScaleU;  // 倒角V坐标
                float topInnerV = 0.3f * roadData.roadEdgeUVScaleU; // 顶面内侧V坐标
                float topOuterV = 0.7f * roadData.roadEdgeUVScaleU; // 顶面外侧V坐标
                
                // 添加UV
                textures.Add(new Vector2(v, sideV));     // 底部内侧
                textures.Add(new Vector2(v, sideV));     // 底部外侧 - 与内侧使用相同的V坐标，确保重叠
                textures.Add(new Vector2(v, chamferV));  // 倒角点
                textures.Add(new Vector2(v, topInnerV)); // 顶部内侧
                textures.Add(new Vector2(v, topOuterV)); // 顶部外侧 - 使用不同的V坐标，确保顶面展开

                // 添加三角形（除了最后一组顶点）
                if (i < segmentCount)
                {
                    int baseIndex = vertexOffset + i * 5; // 现在每个段有5个顶点
                    
                    // 添加底面三角形生成代码
                    if (isLeft)
                    {
                        // 左侧马路牙子底面三角形顺序
                        indices.Add(baseIndex);
                        indices.Add(baseIndex + 5);
                        indices.Add(baseIndex + 1);
                        
                        indices.Add(baseIndex + 1);
                        indices.Add(baseIndex + 5);
                        indices.Add(baseIndex + 6);
                    }
                    else
                    {
                        // 右侧马路牙子底面三角形顺序（反向）
                        indices.Add(baseIndex);
                        indices.Add(baseIndex + 1);
                        indices.Add(baseIndex + 5);
                        
                        indices.Add(baseIndex + 1);
                        indices.Add(baseIndex + 6);
                        indices.Add(baseIndex + 5);
                    }
                    
                    // 顶面 - 法线向上
                    if (isLeft)
                    {
                        // 左侧马路牙子顶面三角形顺序
                        indices.Add(baseIndex + 3);
                        indices.Add(baseIndex + 4);
                        indices.Add(baseIndex + 8);
                        
                        indices.Add(baseIndex + 4);
                        indices.Add(baseIndex + 9);
                        indices.Add(baseIndex + 8);
                    }
                    else
                    {
                        // 右侧马路牙子顶面三角形顺序（反向）
                        indices.Add(baseIndex + 3);
                        indices.Add(baseIndex + 8);
                        indices.Add(baseIndex + 4);
                        
                        indices.Add(baseIndex + 4);
                        indices.Add(baseIndex + 8);
                        indices.Add(baseIndex + 9);
                    }
                    
                    // 内侧面 - 垂直面
                    if (isLeft)
                    {
                        // 左侧马路牙子内侧面三角形顺序
                        indices.Add(baseIndex);
                        indices.Add(baseIndex + 3);
                        indices.Add(baseIndex + 5);
                        
                        indices.Add(baseIndex + 3);
                        indices.Add(baseIndex + 8);
                        indices.Add(baseIndex + 5);
                    }
                    else
                    {
                        // 右侧马路牙子内侧面三角形顺序（反向）
                        indices.Add(baseIndex);
                        indices.Add(baseIndex + 5);
                        indices.Add(baseIndex + 3);
                        
                        indices.Add(baseIndex + 3);
                        indices.Add(baseIndex + 5);
                        indices.Add(baseIndex + 8);
                    }
                    
                    // 外侧面 - 现在分为两部分：底部到倒角点，倒角点到顶部
                    if (isLeft)
                    {
                        // 左侧马路牙子外侧面底部三角形顺序
                        indices.Add(baseIndex + 1);
                        indices.Add(baseIndex + 6);
                        indices.Add(baseIndex + 2);
                        
                        indices.Add(baseIndex + 2);
                        indices.Add(baseIndex + 6);
                        indices.Add(baseIndex + 7);
                        
                        // 左侧马路牙子外侧面顶部三角形顺序
                        indices.Add(baseIndex + 2);
                        indices.Add(baseIndex + 7);
                        indices.Add(baseIndex + 4);
                        
                        indices.Add(baseIndex + 4);
                        indices.Add(baseIndex + 7);
                        indices.Add(baseIndex + 9);
                    }
                    else
                    {
                        // 右侧马路牙子外侧面底部三角形顺序（反向）
                        indices.Add(baseIndex + 1);
                        indices.Add(baseIndex + 2);
                        indices.Add(baseIndex + 6);
                        
                        indices.Add(baseIndex + 2);
                        indices.Add(baseIndex + 7);
                        indices.Add(baseIndex + 6);
                        
                        // 右侧马路牙子外侧面顶部三角形顺序（反向）
                        indices.Add(baseIndex + 2);
                        indices.Add(baseIndex + 4);
                        indices.Add(baseIndex + 7);
                        
                        indices.Add(baseIndex + 4);
                        indices.Add(baseIndex + 9);
                        indices.Add(baseIndex + 7);
                    }
                }
            }
        }

        public void CollectSamplePoints(bool _)
        {
            CollectSamplePoints();
        }

        // 采样点收集相关的新方法
        public void CollectSamplePoints()
        {
            if (Container == null || Container.Splines == null || m_RoadDatas == null)
            {
                return;
            }

            List<int> splineIndices = new List<int>(Container.Splines.Count);
            for (int splineIndex = 0; splineIndex < Container.Splines.Count; splineIndex++)
            {
                splineIndices.Add(splineIndex);
            }

            CollectSamplePoints(splineIndices);
        }

        private float GetCurveU(Spline spline, int curveIndex, float localT)
        {
            int curveCount = Mathf.Max(1, spline.Count - 1);
            return Mathf.Clamp01((curveIndex + Mathf.Clamp01(localT)) / curveCount);
        }

        private void BuildCurveArcLengthTable(
            Spline spline,
            int curveIndex,
            float sampleInterval,
            List<float> curveTs,
            List<float> cumulativeLengths)
        {
            curveTs.Clear();
            cumulativeLengths.Clear();

            if (spline == null || curveIndex < 0 || curveIndex >= spline.Count - 1)
            {
                return;
            }

            Vector3 startPos = transform.TransformPoint(spline[curveIndex].Position);
            Vector3 endPos = transform.TransformPoint(spline[curveIndex + 1].Position);
            float chordLength = Vector3.Distance(startPos, endPos);
            float resolutionStep = Mathf.Max(0.25f, sampleInterval * 0.25f);
            int resolution = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(chordLength, sampleInterval) / resolutionStep), 8, 96);

            curveTs.Add(0f);
            cumulativeLengths.Add(0f);

            Vector3 previous = transform.TransformPoint(spline.EvaluatePosition(GetCurveU(spline, curveIndex, 0f)));
            float accumulated = 0f;
            for (int stepIndex = 1; stepIndex <= resolution; stepIndex++)
            {
                float localT = stepIndex / (float)resolution;
                Vector3 current = transform.TransformPoint(spline.EvaluatePosition(GetCurveU(spline, curveIndex, localT)));
                accumulated += Vector3.Distance(previous, current);
                curveTs.Add(localT);
                cumulativeLengths.Add(accumulated);
                previous = current;
            }
        }

        private static float EvaluateCurveTAtArcDistance(List<float> curveTs, List<float> cumulativeLengths, float targetDistance)
        {
            if (curveTs == null || cumulativeLengths == null || curveTs.Count == 0 || cumulativeLengths.Count != curveTs.Count)
            {
                return 0f;
            }

            if (targetDistance <= 0f)
            {
                return curveTs[0];
            }

            float totalLength = cumulativeLengths[cumulativeLengths.Count - 1];
            if (targetDistance >= totalLength)
            {
                return curveTs[curveTs.Count - 1];
            }

            int upperIndex = 1;
            while (upperIndex < cumulativeLengths.Count && cumulativeLengths[upperIndex] < targetDistance)
            {
                upperIndex++;
            }

            int lowerIndex = Mathf.Max(0, upperIndex - 1);
            float lowerDistance = cumulativeLengths[lowerIndex];
            float upperDistance = cumulativeLengths[upperIndex];
            float lerp = upperDistance - lowerDistance > 1e-5f
                ? Mathf.InverseLerp(lowerDistance, upperDistance, targetDistance)
                : 0f;
            return Mathf.Lerp(curveTs[lowerIndex], curveTs[upperIndex], lerp);
        }

        private void CollectSamplePoints(IReadOnlyList<int> splineIndices)
        {
            if (Container == null || Container.Splines == null || m_RoadDatas == null || splineIndices == null)
            {
                return;
            }

            int roadIndex = GetRoadIndexInHierarchy();
            samplePoints.Clear();
            for (int listIndex = 0; listIndex < splineIndices.Count; listIndex++)
            {
                int splineIndex = splineIndices[listIndex];
                if (splineIndex < 0 || splineIndex >= Container.Splines.Count || splineIndex >= m_RoadDatas.Count)
                {
                    continue;
                }

                var roadData = m_RoadDatas[splineIndex];
                roadData.samplePoints.Clear();

                var spline = Container.Splines[splineIndex];
                if (spline == null || spline.Count < 1)
                {
                    continue;
                }
                BuildSegmentSamplePointsForSpline(roadIndex, splineIndex, roadData, spline);
                samplePoints.AddRange(roadData.samplePoints);
                if (s_EnableRoadHotPathDiagnostics)
                {
                    Debug.Log($"Road {roadIndex}, Spline {splineIndex}: Generated {roadData.samplePoints.Count} points " +
                              $"({roadData.samplePoints.Count(p => p.isOriginalKnot)} knots, " +
                              $"{roadData.samplePoints.Count(p => !p.isOriginalKnot)} samples)");
                }
            }
        }

        // 获取道路在大纲视图中的索引
        public int GetRoadIndexInHierarchy()
        {
            // 查找"Road_Data"游戏对象
            Transform roadDataTransform = GameObject.Find("Road_Data")?.transform;
            if (roadDataTransform == null) return -1;

            // 获取所有带有LoftRoadBehaviour的子对象
            var roadComponents = new List<LoftRoadBehaviour>();
            foreach (Transform child in roadDataTransform)
            {
                var roadComponent = child.GetComponent<LoftRoadBehaviour>();
                if (roadComponent != null)
                {
                    roadComponents.Add(roadComponent);
                }
            }

            // 按照在大纲视图中的顺序排序
            roadComponents.Sort((a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));

            // 查找当前组件的索引
            return roadComponents.IndexOf(this);
        }

        // 更新所有道路的采样点
        public static void UpdateAllRoadsSamplePoints()
        {
            var roadDataObj = GameObject.Find("Road_Data");
            if (roadDataObj == null) return;

            var allRoads = roadDataObj.GetComponentsInChildren<LoftRoadBehaviour>();
            foreach (var road in allRoads)
            {
                road.CollectSamplePoints();
            }
        }

        // 获取道路在大纲视图中的索引
        public int GetOutlineIndex()
        {
            // 查找Road_Data父对象
            GameObject roadDataObj = GameObject.Find("Road_Data");
            if (roadDataObj == null)
            {
                Debug.LogWarning($"场景中没有找到Road_Data对象，将使用实例ID作为索引");
                return GetInstanceID();
            }
            
            // 收集所有道路对象
            List<GameObject> roadObjects = new List<GameObject>();
            foreach (Transform child in roadDataObj.transform)
            {
                var roadComponent = child.GetComponent<LoftRoadBehaviour>();
                if (roadComponent != null)
                {
                    roadObjects.Add(child.gameObject);
                }
            }
            
            // 使用siblingIndex排序，确保与大纲视图顺序一致
            roadObjects.Sort((a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
            
            // 查找当前对象的索引
            for (int i = 0; i < roadObjects.Count; i++)
            {
                var roadComponent = roadObjects[i].GetComponent<LoftRoadBehaviour>();
                if (roadComponent == this)
                {
                    return i;
                }
            }
            
            // 如果在大纲视图中未找到此对象，返回实例ID
            Debug.LogWarning($"道路 {name} 未在Road_Data下找到，将使用实例ID作为索引");
            return GetInstanceID();
        }

        // 创建车道容器对象
        private void CreateLanesContainer()
        {
            if (m_LanesContainer == null)
            {
                m_LanesContainer = new GameObject("Lanes");
                m_LanesContainer.transform.SetParent(transform);
                m_LanesContainer.transform.localPosition = Vector3.zero;
                m_LanesContainer.transform.localRotation = Quaternion.identity;
                m_LanesContainer.transform.localScale = Vector3.one;
            }
        }

        // 存储车道网格的顶点数据，用于Gizmo绘制
        [System.Serializable]
        private class LaneMeshData
        {
            public int laneIndex;
            public int localLaneIndex;
            public int localSegmentIndex;
            public int segmentId;
            public List<Vector3> points = new List<Vector3>();
            public List<Vector3> leftBoundaryPoints = new List<Vector3>();  // 左边界点
            public List<Vector3> rightBoundaryPoints = new List<Vector3>();  // 右边界点
            
            // 添加端点所属信息
            public int splineIndex;                 // 所属样条索引
            public int startKnotIndex;              // 起点所属knot索引
            public int endKnotIndex;                // 终点所属knot索引
            public long startMarkerId;             // 起点marker id
            public long endMarkerId;               // 终点marker id
            public JunctionData startJunction;      // 起点所属路口
            public JunctionData endJunction;        // 终点所属路口
            public bool isRightLane;                // 是否是右侧车道（用于确定In/Out）
        }

        internal readonly struct LaneJunctionEndpointInfo
        {
            public readonly Vector3 point;
            public readonly Vector3 inwardNormal;
            public readonly int localLaneIndex;
            public readonly int splineIndex;
            public readonly int localSegmentIndex;
            public readonly int segmentId;
            public readonly long boundaryMarkerId;
            public readonly bool isRightLane;
            public readonly bool isInboundLane;
            public readonly bool isStartBoundary;

            public LaneJunctionEndpointInfo(
                Vector3 point,
                Vector3 inwardNormal,
                int localLaneIndex,
                int splineIndex,
                int localSegmentIndex,
                int segmentId,
                long boundaryMarkerId,
                bool isRightLane,
                bool isInboundLane,
                bool isStartBoundary)
            {
                this.point = point;
                this.inwardNormal = inwardNormal;
                this.localLaneIndex = localLaneIndex;
                this.splineIndex = splineIndex;
                this.localSegmentIndex = localSegmentIndex;
                this.segmentId = segmentId;
                this.boundaryMarkerId = boundaryMarkerId;
                this.isRightLane = isRightLane;
                this.isInboundLane = isInboundLane;
                this.isStartBoundary = isStartBoundary;
            }
        }

        [SerializeField]
        [HideInInspector]
        private List<LaneMeshData> m_LaneMeshDataList = new List<LaneMeshData>();

        internal void CollectLaneEndpointsForJunction(
            JunctionData junction,
            int splineIndex,
            long markerId,
            List<LaneJunctionEndpointInfo> destination)
        {
            if (junction == null || destination == null || m_LaneMeshDataList == null || m_LaneMeshDataList.Count == 0)
            {
                return;
            }

            for (int laneIndex = 0; laneIndex < m_LaneMeshDataList.Count; laneIndex++)
            {
                LaneMeshData laneData = m_LaneMeshDataList[laneIndex];
                if (laneData == null || laneData.points == null || laneData.points.Count == 0 || laneData.splineIndex != splineIndex)
                {
                    continue;
                }

                bool matchesStart = laneData.startJunction == junction && MarkerMatches(markerId, laneData.startMarkerId);
                bool matchesEnd = laneData.endJunction == junction && MarkerMatches(markerId, laneData.endMarkerId);
                if (!matchesStart && !matchesEnd)
                {
                    continue;
                }

                if (matchesStart)
                {
                    AppendLaneEndpointInfo(laneData, junction, useStartBoundary: true, destination);
                }

                if (matchesEnd && (!matchesStart || laneData.startMarkerId != laneData.endMarkerId))
                {
                    AppendLaneEndpointInfo(laneData, junction, useStartBoundary: false, destination);
                }
            }
        }

        private static bool MarkerMatches(long requestedMarkerId, long boundaryMarkerId)
        {
            return requestedMarkerId == 0 || requestedMarkerId == boundaryMarkerId;
        }

        private void AppendLaneEndpointInfo(
            LaneMeshData laneData,
            JunctionData junction,
            bool useStartBoundary,
            List<LaneJunctionEndpointInfo> destination)
        {
            if (laneData == null || laneData.points == null || laneData.points.Count == 0)
            {
                return;
            }

            int pointIndex = useStartBoundary ? 0 : laneData.points.Count - 1;
            int neighborIndex = useStartBoundary
                ? Mathf.Min(1, laneData.points.Count - 1)
                : Mathf.Max(laneData.points.Count - 2, 0);

            Vector3 point = transform.TransformPoint(laneData.points[pointIndex]);
            Vector3 inwardNormal;
            if (laneData.points.Count > 1)
            {
                Vector3 neighborPoint = transform.TransformPoint(laneData.points[neighborIndex]);
                inwardNormal = useStartBoundary ? (neighborPoint - point) : (point - neighborPoint);
            }
            else
            {
                inwardNormal = transform.forward;
            }

            inwardNormal.y = 0f;
            if (inwardNormal.sqrMagnitude <= 1e-5f)
            {
                inwardNormal = transform.forward;
                inwardNormal.y = 0f;
            }

            if (inwardNormal.sqrMagnitude > 1e-5f)
            {
                inwardNormal.Normalize();
            }

            Vector3 toCenter = junction.GetJunctionCenter() - point;
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude > 1e-5f && Vector3.Dot(inwardNormal, toCenter.normalized) < 0f)
            {
                inwardNormal = -inwardNormal;
            }

            bool isInboundLane = useStartBoundary ? !laneData.isRightLane : laneData.isRightLane;
            destination.Add(new LaneJunctionEndpointInfo(
                point,
                inwardNormal,
                laneData.localLaneIndex,
                laneData.splineIndex,
                laneData.localSegmentIndex,
                laneData.segmentId,
                useStartBoundary ? laneData.startMarkerId : laneData.endMarkerId,
                laneData.isRightLane,
                isInboundLane,
                useStartBoundary));
        }

        // 在生成车道网格时，同时收集顶点数据
        private void CollectLaneMeshData(
            int splineIndex,
            int localSegmentIndex,
            int segmentId,
            int localLaneIndex,
            bool isRightLane,
            List<Vector3> points,
            List<Vector3> leftBoundaryPoints = null,
            List<Vector3> rightBoundaryPoints = null,
            int startKnotIndex = 0,
            int endKnotIndex = 0,
            long startMarkerId = 0,
            long endMarkerId = 0,
            JunctionData startJunction = null,
            JunctionData endJunction = null)
        {
            if (splineIndex >= m_RoadDatas.Count || splineIndex >= LoftSplines.Count)
            {
                Debug.LogWarning($"Invalid splineIndex {splineIndex} for lane segment {localSegmentIndex}");
                return;
            }

            int laneIndex = ((splineIndex & 0x3ff) << 20) ^ ((localSegmentIndex & 0x3ff) << 10) ^ (localLaneIndex & 0x3ff);
            var laneData = new LaneMeshData
            {
                laneIndex = laneIndex,
                localLaneIndex = localLaneIndex,
                localSegmentIndex = localSegmentIndex,
                segmentId = segmentId,
            };
            m_LaneMeshDataList.Add(laneData);

            laneData.points.Clear();
            laneData.points.AddRange(points);
            
            // 保存边界线数据（如果有提供）
            if (leftBoundaryPoints != null)
            {
                laneData.leftBoundaryPoints.Clear();
                laneData.leftBoundaryPoints.AddRange(leftBoundaryPoints);
            }
            
            if (rightBoundaryPoints != null)
            {
                laneData.rightBoundaryPoints.Clear();
                laneData.rightBoundaryPoints.AddRange(rightBoundaryPoints);
            }
            
            laneData.splineIndex = splineIndex;
            laneData.isRightLane = isRightLane;
            laneData.startKnotIndex = startKnotIndex;
            laneData.endKnotIndex = endKnotIndex;
            laneData.startMarkerId = startMarkerId;
            laneData.endMarkerId = endMarkerId;
            laneData.startJunction = startJunction;
            laneData.endJunction = endJunction;
        }

        // 绘制车道网格的连线和序号
        private void DrawLaneMeshLines()
        {
            if (m_LaneMeshDataList == null || m_LaneMeshDataList.Count == 0)
                return;

            // 获取当前场景视图相机，用于计算标签显示位置
            Camera sceneCamera = GetSceneViewCamera();

            // 遍历每条车道的网格数据
            foreach (var laneData in m_LaneMeshDataList)
            {
                int splineIndex = laneData.splineIndex;
                int localLaneIndex = laneData.localLaneIndex;
                
                // 确保splineIndex在有效范围内
                if (splineIndex >= m_RoadDatas.Count)
                {
                    Debug.LogWarning($"Invalid splineIndex {splineIndex} for lane {laneData.laneIndex}");
                    continue;
                }
                
                // 获取对应的RoadData
                var roadData = m_RoadDatas[splineIndex];
                bool isRightLane = localLaneIndex >= roadData.leftLaneCount;

                // 如果没有足够的点，跳过
                if (laneData.points.Count < 2)
                    continue;

                // 设置每条车道的唯一颜色（基于车道索引生成颜色）
                float hue = (localLaneIndex * 0.1f) % 1.0f;
                Gizmos.color = Color.HSVToRGB(hue, 0.8f, 0.8f);

                // 获取该车道的总长度，用于显示百分比标记
                float totalLength = 0;
                for (int i = 0; i < laneData.points.Count - 1; i++)
                {
                    totalLength += Vector3.Distance(laneData.points[i], laneData.points[i + 1]);
                }
                float runningLength = 0;

                // 连接顶点，绘制车道中心线和边界线
                for (int i = 0; i < laneData.points.Count - 1; i++)
                {
                    // 获取原始点并应用变换
                    Vector3 startPoint = transform.TransformPoint(laneData.points[i]);
                    Vector3 endPoint = transform.TransformPoint(laneData.points[i + 1]);
                    
                    // 确保线条始终比路面网格高出0.01单位
                    float extraHeight = 0.01f;
                    
                    // 调整点的高度，确保高出路面
                    startPoint.y += extraHeight;
                    endPoint.y += extraHeight;
                    
                    // 获取左右边界线的点（如果存在）
                    bool hasBoundaryPoints = laneData.leftBoundaryPoints.Count > i+1 && 
                                          laneData.rightBoundaryPoints.Count > i+1;
                    
                    Vector3 startLeftBoundary, startRightBoundary, endLeftBoundary, endRightBoundary;
                    
                    if (hasBoundaryPoints)
                    {
                        // 使用存储的边界线点
                        startLeftBoundary = transform.TransformPoint(laneData.leftBoundaryPoints[i]);
                        startRightBoundary = transform.TransformPoint(laneData.rightBoundaryPoints[i]);
                        endLeftBoundary = transform.TransformPoint(laneData.leftBoundaryPoints[i+1]);
                        endRightBoundary = transform.TransformPoint(laneData.rightBoundaryPoints[i+1]);
                        
                        // 应用额外高度
                        startLeftBoundary.y += extraHeight;
                        startRightBoundary.y += extraHeight;
                        endLeftBoundary.y += extraHeight;
                        endRightBoundary.y += extraHeight;
                    }
                    else
                    {
                        // 如果没有存储边界线点，则动态计算（兼容旧数据）
                        float laneWidth = roadData.laneWidth.DefaultValue;
                        Vector3 lineDirection = (endPoint - startPoint).normalized;
                        Vector3 perpendicular = Vector3.Cross(Vector3.up, lineDirection).normalized;
                        
                        startLeftBoundary = startPoint + perpendicular * (laneWidth / 2);
                        startRightBoundary = startPoint - perpendicular * (laneWidth / 2);
                        endLeftBoundary = endPoint + perpendicular * (laneWidth / 2);
                        endRightBoundary = endPoint - perpendicular * (laneWidth / 2);
                    }
                    
                    // 绘制中心线
                    if (s_ShowLaneCenterLines)
                    {
                        Gizmos.DrawLine(startPoint, endPoint);
                    }
                    
                    // 绘制车道边界线
                    if (s_ShowLaneBoundaryLines)
                    {
                        Gizmos.color = Color.white;
                        Gizmos.DrawLine(startLeftBoundary, endLeftBoundary);
                        Gizmos.DrawLine(startRightBoundary, endRightBoundary);
                        
                        // 恢复原始颜色
                        Gizmos.color = Color.HSVToRGB(hue, 0.8f, 0.8f);
                    }
                    
                    // 计算当前线段的长度
                    float segmentLength = Vector3.Distance(startPoint, endPoint);
                    runningLength += segmentLength;
                    float percentComplete = runningLength / totalLength * 100f;
                    
                    // 只在头尾部分显示标签
                    bool isFirstSegment = (i == 0);
                    bool isLastSegment = (i == laneData.points.Count - 2);
                    bool shouldDrawLabel = isFirstSegment || isLastSegment;
                    
                    if (shouldDrawLabel && sceneCamera != null && s_ShowLaneLabels)
                    {
                        // 计算标签位置
                        Vector3 labelPos;
                        string labelText;
                        
                        // 确保标签在路面上方足够高度可见
                        float labelHeight = 0.5f;
                        
                        // 计算实际的车道编号（从左到右）
                        string lanePrefix;
                        int displayLaneIndex;
                        
                        if (isRightLane)
                        {
                            // 右侧车道（Forward）
                            lanePrefix = "F";
                            displayLaneIndex = localLaneIndex - roadData.leftLaneCount;
                            
                            // 获取这个端点的路口和knot信息
                            JunctionData targetJunction = isFirstSegment ? laneData.startJunction : laneData.endJunction;
                            int knotIndex = isFirstSegment ? laneData.startKnotIndex : laneData.endKnotIndex;
                            
                            // 右侧车道永远是In
                            string junctionInfo = targetJunction != null ? $"J:{targetJunction.GetInstanceID()},K:{knotIndex}" : "J:None";
                            
                            if (isFirstSegment)
                            {
                                labelPos = startPoint + Vector3.up * labelHeight;
                                labelText = $"{lanePrefix}Lane{displayLaneIndex}, In, {junctionInfo}";
                            }
                            else
                            {
                                labelPos = endPoint + Vector3.up * labelHeight;
                                labelText = $"{lanePrefix}Lane{displayLaneIndex}, In, {junctionInfo}";
                            }
                        }
                        else
                        {
                            // 左侧车道（Backward）
                            lanePrefix = "B";
                            displayLaneIndex = localLaneIndex;
                            
                            // 获取这个端点的路口和knot信息
                            JunctionData targetJunction = isFirstSegment ? laneData.startJunction : laneData.endJunction;
                            int knotIndex = isFirstSegment ? laneData.startKnotIndex : laneData.endKnotIndex;
                            
                            // 左侧车道永远是Out
                            string junctionInfo = targetJunction != null ? $"J:{targetJunction.GetInstanceID()},K:{knotIndex}" : "J:None";
                            
                            if (isFirstSegment)
                            {
                                labelPos = startPoint + Vector3.up * labelHeight;
                                labelText = $"{lanePrefix}Lane{displayLaneIndex}, Out, {junctionInfo}";
                            }
                            else
                            {
                                labelPos = endPoint + Vector3.up * labelHeight;
                                labelText = $"{lanePrefix}Lane{displayLaneIndex}, Out, {junctionInfo}";
                            }
                        }
                        
                        // 绘制3D文本标签
                        DrawLabel(labelPos, labelText, sceneCamera);
                    }
                }
                
                // 在起点和终点处绘制特殊标记
                if (laneData.points.Count > 0)
                {
                    // 获取起点和终点
                    Vector3 startPoint = transform.TransformPoint(laneData.points[0]);
                    Vector3 endPoint = transform.TransformPoint(laneData.points[laneData.points.Count - 1]);
                    
                    // 确保标记始终比路面网格高出0.01单位
                    float extraHeight = 0.01f;
                    startPoint.y += extraHeight;
                    endPoint.y += extraHeight;
                    
                    // 计算箭头方向
                    Vector3 startDirection, endDirection;
                    if (laneData.points.Count > 1)
                    {
                        // 起点方向：指向终点
                        startDirection = (transform.TransformPoint(laneData.points[laneData.points.Count - 1]) - startPoint).normalized;
                        // 终点方向：延续最后一段方向指向外部
                        endDirection = (transform.TransformPoint(laneData.points[laneData.points.Count - 1]) - 
                                     transform.TransformPoint(laneData.points[laneData.points.Count - 2])).normalized;
                    }
                    else
                    {
                        startDirection = Vector3.forward;
                        endDirection = Vector3.forward;
                    }
                    
                    // 箭头和球体参数
                    float arrowLength = 2.0f;    // 箭头总长度
                    float arrowWidth = 0.8f;     // 箭头头部宽度
                    float arrowHeight = 0.8f;    // 箭头头部长度
                    float sphereRadius = 0.4f;   // 球体半径
                    
                    if (isRightLane)
                    {
                        // 右侧车道（backward）
                        // 终点（红色）
                        Gizmos.color = Color.red;
                        Gizmos.DrawSphere(startPoint, sphereRadius);
                        DrawArrowGizmo(startPoint + Vector3.up * 0.1f, startDirection, arrowLength, arrowWidth, arrowHeight); // 翻转方向

                        // 起点（绿色）
                        Gizmos.color = Color.green;
                        Gizmos.DrawSphere(endPoint, sphereRadius);
                        DrawArrowGizmo(endPoint + Vector3.up * 0.1f, endDirection, arrowLength, arrowWidth, arrowHeight); // 翻转方向
                    }
                    else
                    {
                        // 左侧车道（forward）
                        // 起点（绿色）
                        Gizmos.color = Color.green;
                        Gizmos.DrawSphere(startPoint, sphereRadius);
                        DrawArrowGizmo(startPoint + Vector3.up * 0.1f, -startDirection, arrowLength, arrowWidth, arrowHeight); // 翻转方向

                        // 终点（红色）
                        Gizmos.color = Color.red;
                        Gizmos.DrawSphere(endPoint, sphereRadius);
                        DrawArrowGizmo(endPoint + Vector3.up * 0.1f, -endDirection, arrowLength, arrowWidth, arrowHeight); // 翻转方向
                    }
                }
            }
        }

        // 绘制箭头Gizmo
        private void DrawArrowGizmo(Vector3 pos, Vector3 direction, float length, float width, float height)
        {
            direction = direction.normalized;
            Vector3 up = Vector3.up;
            Vector3 right = Vector3.Cross(up, direction).normalized;
            
            // 箭头主体的起点和终点
            Vector3 arrowStart = pos;
            Vector3 arrowEnd = pos + direction * (length - height); // 减去箭头头部的长度
            
            // 绘制加粗的箭头主体
            float shaftWidth = width * 0.2f; // 箭头主体宽度
            Vector3[] shaftPoints = new Vector3[]
            {
                arrowStart + right * shaftWidth,
                arrowStart - right * shaftWidth,
                arrowEnd + right * shaftWidth,
                arrowEnd - right * shaftWidth
            };
            
            // 绘制箭头主体的四条边
            Gizmos.DrawLine(shaftPoints[0], shaftPoints[2]); // 上边
            Gizmos.DrawLine(shaftPoints[1], shaftPoints[3]); // 下边
            Gizmos.DrawLine(shaftPoints[0], shaftPoints[1]); // 起点连接
            Gizmos.DrawLine(shaftPoints[2], shaftPoints[3]); // 终点连接
            
            // 计算箭头头部的点
            Vector3 arrowTip = pos + direction * length;
            Vector3 arrowBase1 = arrowEnd + right * width;
            Vector3 arrowBase2 = arrowEnd - right * width;
            
            // 绘制箭头头部
            Gizmos.DrawLine(arrowTip, arrowBase1);
            Gizmos.DrawLine(arrowTip, arrowBase2);
            Gizmos.DrawLine(arrowBase1, arrowBase2);
            
            // 添加填充线使箭头头部看起来更实心
            int subdivisions = 5;
            for (int i = 1; i < subdivisions; i++)
            {
                float t = i / (float)subdivisions;
                Vector3 left = Vector3.Lerp(arrowEnd, arrowBase1, t);
                Vector3 rightPoint = Vector3.Lerp(arrowEnd, arrowBase2, t);
                Gizmos.DrawLine(left, rightPoint);
                
                // 连接到箭头尖端
                Vector3 midPoint = Vector3.Lerp(left, rightPoint, 0.5f);
                Gizmos.DrawLine(midPoint, arrowTip);
            }
        }

        // 获取场景视图相机
        private Camera GetSceneViewCamera()
        {
#if UNITY_EDITOR
            if (UnityEditor.SceneView.currentDrawingSceneView != null)
            {
                return UnityEditor.SceneView.currentDrawingSceneView.camera;
            }
#endif
            return null;
        }

        // 绘制3D文本标签
        private void DrawLabel(Vector3 position, string text, Camera camera)
        {
#if UNITY_EDITOR
            UnityEditor.Handles.BeginGUI();
            
            // 将3D位置转换为屏幕位置
            Vector3 screenPos = camera.WorldToScreenPoint(position);
            
            // 如果在相机背面或屏幕外，不绘制
            if (screenPos.z < 0 || screenPos.x < 0 || screenPos.x > Screen.width || 
                screenPos.y < 0 || screenPos.y > Screen.height)
            {
                UnityEditor.Handles.EndGUI();
                return;
            }
            
            // 创建标签样式
            GUIStyle style = new GUIStyle();
            style.normal.textColor = Color.white;
            style.alignment = TextAnchor.MiddleCenter;
            style.fontSize = 12;
            style.fontStyle = FontStyle.Bold;
            
            // 计算标签背景大小
            Vector2 size = style.CalcSize(new GUIContent(text));
            Rect rect = new Rect(screenPos.x - size.x / 2, Screen.height - screenPos.y - size.y / 2, size.x + 4, size.y + 2);
            
            // 绘制标签背景
            Color backgroundColor = new Color(0, 0, 0, 0.7f);
            EditorGUI.DrawRect(rect, backgroundColor);
            
            // 绘制标签文本
            GUI.Label(rect, text, style);
            
            UnityEditor.Handles.EndGUI();
#endif
        }

    }

    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class CunningRoadGeometryHandle : MonoBehaviour
    {
        [System.Serializable]
        public sealed class DomainUploadSnapshot
        {
            public float[] positions = System.Array.Empty<float>();
            public int[] primitivePointIndices = System.Array.Empty<int>();
            public uint[] primitiveOffsets = System.Array.Empty<uint>();
            public float[] vertexNormals = System.Array.Empty<float>();
            public float[] vertexUv = System.Array.Empty<float>();
            public int[] primitiveMaterialIndices = System.Array.Empty<int>();
            public int[] primitiveAreaIndices = System.Array.Empty<int>();
            public Material[] renderMaterials = System.Array.Empty<Material>();

            public bool HasTopology => positions != null && primitivePointIndices != null && primitiveOffsets != null && positions.Length > 0;

            public void Clear()
            {
                positions = System.Array.Empty<float>();
                primitivePointIndices = System.Array.Empty<int>();
                primitiveOffsets = System.Array.Empty<uint>();
                vertexNormals = System.Array.Empty<float>();
                vertexUv = System.Array.Empty<float>();
                primitiveMaterialIndices = System.Array.Empty<int>();
                primitiveAreaIndices = System.Array.Empty<int>();
                renderMaterials = System.Array.Empty<Material>();
            }

            public void Capture(
                IReadOnlyList<float> sourcePositions,
                IReadOnlyList<int> sourcePrimitivePointIndices,
                IReadOnlyList<uint> sourcePrimitiveOffsets,
                IReadOnlyList<float> sourceVertexNormals,
                IReadOnlyList<float> sourceVertexUv,
                IReadOnlyList<int> sourcePrimitiveMaterialIndices,
                IReadOnlyList<int> sourcePrimitiveAreaIndices,
                Material[] sourceRenderMaterials)
            {
                CopySnapshot(sourcePositions, ref positions);
                CopySnapshot(sourcePrimitivePointIndices, ref primitivePointIndices);
                CopySnapshot(sourcePrimitiveOffsets, ref primitiveOffsets);
                CopySnapshot(sourceVertexNormals, ref vertexNormals);
                CopySnapshot(sourceVertexUv, ref vertexUv);
                CopySnapshot(sourcePrimitiveMaterialIndices, ref primitiveMaterialIndices);
                CopySnapshot(sourcePrimitiveAreaIndices, ref primitiveAreaIndices);
                CopySnapshot(sourceRenderMaterials, ref renderMaterials);
            }

            public void SetRenderMaterials(Material[] sourceRenderMaterials)
            {
                CopySnapshot(sourceRenderMaterials, ref renderMaterials);
            }

            private static void CopySnapshot<T>(IReadOnlyList<T> source, ref T[] destination)
            {
                if (source == null || source.Count == 0)
                {
                    destination = System.Array.Empty<T>();
                    return;
                }

                if (destination == null || destination.Length != source.Count)
                {
                    destination = new T[source.Count];
                }

                for (int index = 0; index < source.Count; index++)
                {
                    destination[index] = source[index];
                }
            }
        }

        public enum GeometryDomain
        {
            RoadMain = 0,
            SidewalkBundle = 1,
            GreenbeltBundle = 2,
        }

        [SerializeField]
        private GeometryDomain m_Domain = GeometryDomain.RoadMain;

        [SerializeField]
        [HideInInspector]
        private ulong m_GeometryHandle;

        [SerializeField]
        [HideInInspector]
        private ulong m_LastDirtyId;

        [SerializeField]
        [HideInInspector]
        private Material m_PrimaryMaterial;

        [SerializeField]
        [HideInInspector]
        private Material[] m_RenderMaterials = System.Array.Empty<Material>();

        [NonSerialized]
        private DomainUploadSnapshot m_UploadSnapshot = new DomainUploadSnapshot();

        public GeometryDomain Domain
        {
            get => m_Domain;
            set => m_Domain = value;
        }

        public ulong GeometryHandle => m_GeometryHandle;
        public ulong LastDirtyId => m_LastDirtyId;
        public Material PrimaryMaterial => m_PrimaryMaterial;
        public Material[] RenderMaterials => m_RenderMaterials ?? System.Array.Empty<Material>();
        public DomainUploadSnapshot UploadSnapshot => m_UploadSnapshot ??= new DomainUploadSnapshot();

        public ulong EnsureGeometryCreated()
        {
            if (m_GeometryHandle == 0)
            {
                m_GeometryHandle = NativeMethods.cunning_geo_create();
                m_LastDirtyId = GetDirtyId();
            }

            return m_GeometryHandle;
        }

        public void RebuildGeometryFromRoad()
        {
            var road = GetComponentInParent<LoftRoadBehaviour>();
            if (road == null)
            {
                return;
            }

            road.RebuildCunningRoadGeometry(this);
        }

        public ulong GetDirtyId()
        {
            return m_GeometryHandle == 0 ? 0UL : NativeMethods.cunning_geo_get_dirty_id(m_GeometryHandle);
        }

        public void ReplaceGeometry(ulong newHandle, Material primaryMaterial)
        {
            ReplaceGeometry(newHandle, primaryMaterial != null ? new[] { primaryMaterial } : System.Array.Empty<Material>());
        }

        public void ReplaceGeometry(ulong newHandle, Material[] renderMaterials)
        {
            if (m_GeometryHandle != 0 && m_GeometryHandle != newHandle)
            {
                NativeMethods.cunning_release_handle(m_GeometryHandle);
            }

            m_GeometryHandle = newHandle;
            m_RenderMaterials = renderMaterials ?? System.Array.Empty<Material>();
            m_PrimaryMaterial = m_RenderMaterials.Length > 0 ? m_RenderMaterials[0] : null;
            m_LastDirtyId = GetDirtyId();
        }

        public void CaptureUploadSnapshot(
            IReadOnlyList<float> positions,
            IReadOnlyList<int> primitivePointIndices,
            IReadOnlyList<uint> primitiveOffsets,
            IReadOnlyList<float> vertexNormals,
            IReadOnlyList<float> vertexUv,
            IReadOnlyList<int> primitiveMaterialIndices,
            IReadOnlyList<int> primitiveAreaIndices,
            Material[] renderMaterials)
        {
            if (m_UploadSnapshot == null)
            {
                m_UploadSnapshot = new DomainUploadSnapshot();
            }

            m_UploadSnapshot.Capture(
                positions,
                primitivePointIndices,
                primitiveOffsets,
                vertexNormals,
                vertexUv,
                primitiveMaterialIndices,
                primitiveAreaIndices,
                renderMaterials);
        }

        public void Dispose()
        {
            if (m_GeometryHandle != 0)
            {
                NativeMethods.cunning_release_handle(m_GeometryHandle);
                m_GeometryHandle = 0;
            }

            m_LastDirtyId = 0;
            m_PrimaryMaterial = null;
            m_RenderMaterials = System.Array.Empty<Material>();
            m_UploadSnapshot?.Clear();
        }

        private void OnDestroy()
        {
            Dispose();
        }
    }
}
#endif
