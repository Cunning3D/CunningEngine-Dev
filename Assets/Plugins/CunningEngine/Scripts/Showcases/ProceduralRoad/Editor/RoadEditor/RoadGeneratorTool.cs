using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Splines.Examples;
using Unity.Mathematics;
using UnityEditor.Formats.Fbx.Exporter;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

namespace Unity.Splines.Examples
{
    public static class RoadGizmoUtility
    {
        public static bool ShouldShowGizmos()
        {
            return UnityEditor.Splines.Extension.RoadGeneratorTool.ShouldShowGizmos();
        }
    }
}

namespace UnityEditor.Splines.Extension
{
    [InitializeOnLoad]
    public partial class RoadGeneratorTool
    {
        private const string ShowWindowPrefKey = "RoadGeneratorTool_ShowWindow";
        private static bool showWindow;
        private static Rect windowRect = new Rect(16, 16, 560, 920);
        private static RoadType selectedRoadType = RoadType.Road_A;
        private static RoadInfo selectedCustomRoadInfo;
        private static List<RoadInfo> customRoadInfos;
        private static bool showControlsa = true; // 控制按钮折叠
        private static bool showControlsb = true; // 控制按钮折叠
        private static bool showControlsc = true; // 控制按钮折叠
        private static bool showDebugControls = true; // DEBUG控制折叠
        private static bool showSamplePointsDebug = false; // 显示采样点调试
        private static bool showOrderedPointsDebug = false; // 显示有序点列表调试
        
        // 路口编辑器相关字段
        private static List<(LoftRoadBehaviour road, int splineIndex, int knotIndex)> selectedConnections = 
            new List<(LoftRoadBehaviour, int, int)>();
        private static List<(LoftRoadBehaviour road, int splineIndex, int knotIndex)> availableConnections = 
            new List<(LoftRoadBehaviour, int, int)>();
        private static bool autoUpdateJunction = true;
        private static bool showJunctionControls = true;
        private static bool showGizmos = true; // 添加显示Gizmo的控制变量
        private static bool showKnotPoints = true; // 添加显示knot点的控制变量
        private static bool showBoundaryControlKnots = true;
        private const float GIZMO_SIZE = 7f;
        private static bool autoUpdateSamplePoints = false; // 控制是否自动更新采样点
        private static bool autoPreviewJunctions = false; // 自动预览路口
        private static bool previewRefreshRequested = false;
        private static double lastPreviewUpdateTime = 0d;
        private const double PreviewUpdateInterval = 0.15d;
        private static bool showAlgorithmParameters = true; // 控制算法参数折叠
        private static float globalHeightThreshold = 2f;    // 全局高度阈值参数
        private static List<JunctionGroup> junctionGroups = new List<JunctionGroup>();
        private static readonly Color junctionGroupRangeColor = new Color(1f, 0.15f, 1f, 0.95f);
        private static readonly Color junctionGroupFillColor = new Color(1f, 0.15f, 1f, 0.2f);
        private static readonly Color junctionShapeRangeColor = new Color(0.2f, 1f, 0.75f, 0.95f);
        private static readonly Color junctionShapeFillColor = new Color(0.2f, 1f, 0.75f, 0.18f);
        private static Material previewMaterial;
        private static HashSet<int> lastKnownRoadInstanceIds = new HashSet<int>();
        private static DebugJunctionVisualizationSnapshot frozenJunctionDebugSnapshot;
        private static readonly Dictionary<RoadType, Texture2D> roadTypeThumbnailCache = new Dictionary<RoadType, Texture2D>();
        private static GUIStyle roadTypeSelectorLabelStyle;
        private static GUIStyle roadTypeCardTitleStyle;
        private static GUIStyle roadTypeCardSummaryStyle;
        private static GUIStyle roadTypeCardHintStyle;
        private static GUIStyle roadTypeCardBadgeStyle;
        private static GUIStyle roadTypeCardChevronStyle;
        private static GUIStyle roadTypePopupHeaderStyle;
        private static GUIStyle roadTypePopupSubHeaderStyle;
        private const float SelectedRoadTypeCardHeight = 86f;
        private const float RoadTypePopupCardHeight = 78f;
        private static readonly Color RoadTypeAccentColor = new Color(0.28f, 0.66f, 0.96f, 1f);
        private static readonly Color RoadTypeSelectedBackgroundColor = new Color(0.13f, 0.19f, 0.25f, 1f);
        private static readonly Color RoadTypeHoverBackgroundColor = new Color(0.16f, 0.17f, 0.19f, 1f);
        private static readonly Color RoadTypeBackgroundColor = new Color(0.13f, 0.14f, 0.15f, 1f);
        private static readonly Color RoadTypeBorderColor = new Color(1f, 1f, 1f, 0.08f);
        private static readonly Color BoundaryControlRingColor = new Color(1f, 0.82f, 0.22f, 0.98f);
        private static readonly Color BoundaryControlFillColor = new Color(1f, 0.78f, 0.18f, 0.2f);
        private static readonly Color BoundaryControlLabelColor = new Color(1f, 0.95f, 0.75f, 0.98f);

        // 添加路段控制面板相关字段
        private static bool showRoadSegmentControls = true; // 路段控制面板折叠状态
        private static bool showRoadSegmentGizmos = true;   // 显示路段Gizmo总开关
        
        // 添加变化检测相关的字段
        private static Dictionary<LoftRoadBehaviour, (Vector3[][] positions, Quaternion[][] rotations)> lastRoadState = 
            new Dictionary<LoftRoadBehaviour, (Vector3[][] positions, Quaternion[][] rotations)>();

        private const float JunctionConnectionRangeScale = 1.3f;
        private const float JunctionShapeRangeScale = 1f;
        private const float ExistingJunctionCoveragePadding = 1.5f;

        private sealed class JunctionRangePlan
        {
            public JunctionGroup Group;
            public LoftRoadBehaviour Road;
            public int SplineIndex;
            public List<float> HitCurveUs = new List<float>();
            public List<RoadSamplePoint> HitPoints = new List<RoadSamplePoint>();
            public List<int> HitKnotIndices = new List<int>();
            public int StartKnotIndex = -1;
            public int EndKnotIndex = -1;
            public JunctionConnectionRef StartConnection;
            public JunctionConnectionRef EndConnection;
        }

        private sealed class JunctionGroupSplineBoundary
        {
            public int RoadIndex;
            public int SplineIndex;
            public List<float> HitCurveUs = new List<float>();
            public List<RoadSamplePoint> HitPoints = new List<RoadSamplePoint>();
            public Vector3 StartPosition;
            public Vector3 EndPosition;
        }

        private struct JunctionBoundaryTarget
        {
            public int RoadIndex;
            public int SplineIndex;
            public Vector3 Position;
        }

        private struct JunctionConnectionRef
        {
            public LoftRoadBehaviour Road;
            public int SplineIndex;
            public int KnotIndex;

            public bool IsValid =>
                Road != null &&
                SplineIndex >= 0 &&
                KnotIndex >= 0;
        }

        private sealed class DebugJunctionVisualizationSnapshot
        {
            public readonly List<Vector3> CrossPoints = new List<Vector3>();
            public readonly List<DebugConnectionSnapshot> Connections = new List<DebugConnectionSnapshot>();
            public readonly List<DebugJunctionGroupSnapshot> Groups = new List<DebugJunctionGroupSnapshot>();

            public bool HasData =>
                CrossPoints.Count > 0 ||
                Connections.Count > 0 ||
                Groups.Count > 0;
        }

        private sealed class RoadTypeSelectionPopup : PopupWindowContent
        {
            private Vector2 scrollPosition;

            public override Vector2 GetWindowSize()
            {
                return new Vector2(430f, 540f);
            }

            public override void OnGUI(Rect rect)
            {
                EnsureRoadTypeSelectorStyles();

                GUILayout.BeginArea(rect);
                GUILayout.Space(10f);
                GUILayout.Label("选择道路类型预设", roadTypePopupHeaderStyle);
                GUILayout.Label("名称保持原样，增加缩略图预览，方便直接识别道路结构。", roadTypePopupSubHeaderStyle);
                GUILayout.Space(8f);

                scrollPosition = GUILayout.BeginScrollView(scrollPosition);

                foreach (RoadType roadType in Enum.GetValues(typeof(RoadType)))
                {
                    Rect cardRect = GUILayoutUtility.GetRect(0f, RoadTypePopupCardHeight, GUILayout.ExpandWidth(true));
                    bool isSelected = roadType == selectedRoadType;

                    if (DrawRoadTypeCard(cardRect, roadType, isSelected, isCompact: true, footerText: isSelected ? "当前已选" : "点击切换"))
                    {
                        selectedRoadType = roadType;
                        selectedCustomRoadInfo = null;
                        SetDefaultWidth();
                        GUI.changed = true;
                        editorWindow.Close();
                        SceneView.RepaintAll();
                    }

                    GUILayout.Space(6f);
                }

                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }
        }

        private struct DebugConnectionSnapshot
        {
            public Vector3 Start;
            public Vector3 End;
        }

        private sealed class DebugJunctionGroupSnapshot
        {
            public readonly List<Vector3> OutlinePoints = new List<Vector3>();
            public readonly List<Vector3> ShapeOutlinePoints = new List<Vector3>();
            public Vector3 CenterPosition;
            public int PointCount;
            public int ShapePointCount;
        }

        private sealed class ExistingJunctionCoverage
        {
            public JunctionData Junction;
            public Vector3 CenterPosition;
            public float Radius;
            public readonly HashSet<(int roadInstanceId, int splineIndex)> ConnectedSplineKeys =
                new HashSet<(int roadInstanceId, int splineIndex)>();
        }

        // 添加公共静态方法来访问showGizmos
        public static bool ShouldShowGizmos()
        {
            return IsToolUiVisible && showGizmos;
        }

        private static bool IsToolUiVisible => showWindow;

        static RoadGeneratorTool()
        {
            showWindow = EditorPrefs.GetBool(ShowWindowPrefKey, false);
            Unity.Splines.Examples.JunctionData.ShowGizmos = showGizmos;
            
            // 初始化路段Gizmo控制状态
            Unity.Splines.Examples.LoftRoadBehaviour.ShowLaneGizmos = showRoadSegmentGizmos;
            
            SceneView.duringSceneGui += OnSceneGUI;
            LoadAllCustomRoadInfos();
        }

        private static void LoadPcgTool()
        {
            const string worldToolMenuPath = "Procedural/地形编辑/地编程序化工具箱";
            if (EditorApplication.ExecuteMenuItem(worldToolMenuPath))
            {
                return;
            }

            Debug.Log("未找到大世界生成工具菜单，已跳过打开。");
        }


















        [MenuItem("Procedural/道路系统/打开道路生成器")]
        private static void ToggleOfficialWindowMenu()
        {
            ToggleOfficialWindow();
            SceneView.RepaintAll();
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (!IsToolUiVisible || sceneView == null)
                return;

            Event e = Event.current;
            if (e == null) return;
            
            try
            {
                var roads = FindSceneRoads();
                SyncDeletedRoadGizmoState(roads);

                // 更新可用连接点
                if (e.type == EventType.Layout)
                {
                    UpdateAvailableConnections();
                }
                
                if (showGizmos && showKnotPoints) // 修改显示控制条件
                {
                    // 绘制可用连接点
                    Handles.color = Color.green;
                    if (availableConnections != null)
                    {
                        foreach (var road in roads)
                        {
                            if (road == null || road.Container == null) continue;
                            
                            for (int splineIndex = 0; splineIndex < road.Container.Splines.Count; splineIndex++)
                            {
                                var spline = road.Container.Splines[splineIndex];
                                if (spline == null || spline.Count == 0) continue;
                                
                                // 只显示头尾点
                                var startKnot = spline[0];
                                var endKnot = spline[spline.Count - 1];
                                
                                var startWorldPos = road.transform.TransformPoint(startKnot.Position);
                                var endWorldPos = road.transform.TransformPoint(endKnot.Position);
                                
                                bool isStartSelected = selectedConnections.Any(conn => 
                                    conn.road == road && conn.splineIndex == splineIndex && conn.knotIndex == 0);
                                bool isEndSelected = selectedConnections.Any(conn => 
                                    conn.road == road && conn.splineIndex == splineIndex && conn.knotIndex == spline.Count - 1);
                                
                                // 根据选中状态设置颜色
                                Handles.color = isStartSelected ? Color.yellow : Color.green;
                                Handles.SphereHandleCap(0, startWorldPos, Quaternion.identity, GIZMO_SIZE, EventType.Repaint);
                                
                                Handles.color = isEndSelected ? Color.yellow : Color.green;
                                Handles.SphereHandleCap(0, endWorldPos, Quaternion.identity, GIZMO_SIZE, EventType.Repaint);
                            }
                        }
                    }
                }

                if (showGizmos && showBoundaryControlKnots)
                {
                    DrawBoundaryControlKnotOverlay(roads);
                }
                
                // 绘制已选择的连接点
                Handles.color = Color.yellow;
                if (selectedConnections != null)
                {
                    for (int i = selectedConnections.Count - 1; i >= 0; i--)
                    {
                        var conn = selectedConnections[i];
                        if (conn.road == null || conn.road.Container == null || 
                            conn.splineIndex >= conn.road.Container.Splines.Count)
                        {
                            selectedConnections.RemoveAt(i);
                            continue;
                        }
                        
                        var spline = conn.road.Container.Splines[conn.splineIndex];
                        if (spline != null && conn.knotIndex < spline.Count)
                        {
                            var worldPos = conn.road.transform.TransformPoint(spline[conn.knotIndex].Position);
                            Handles.SphereHandleCap(0, worldPos, Quaternion.identity, GIZMO_SIZE, EventType.Repaint);
                        }
                    }
                }
                
                // 处理鼠标点击事件
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    // 只有在显示状态下才能选中knot点
                    if(showGizmos && showKnotPoints)
                    {
                        var closestKnot = FindClosestKnot(sceneView, e.mousePosition);
                        if (closestKnot.HasValue && closestKnot.Value.road != null)
                        {
                            if (e.shift) // 按住Shift键
                            {
                                // 如果点击的节点不在已选择列表中,则添加
                                if (!selectedConnections.Any(conn => 
                                    conn.road != null && 
                                    conn.road == closestKnot.Value.road && 
                                    conn.splineIndex == closestKnot.Value.splineIndex && 
                                    conn.knotIndex == closestKnot.Value.knotIndex))
                                {
                                    selectedConnections.Add(closestKnot.Value);
                                }
                            }
                            else // 未按Shift键
                            {
                                // 清除之前的选择并选择新的节点
                                selectedConnections.Clear();
                                selectedConnections.Add(closestKnot.Value);
                            }
                            e.Use();
                            sceneView.Repaint();
                        }
                    }
                }
                
                bool useFrozenJunctionDebugSnapshot = HasFrozenJunctionDebugSnapshot() && !autoUpdateSamplePoints;

                // 绘制检测到的连接线
                if (IsToolUiVisible)
                {
                    if (useFrozenJunctionDebugSnapshot)
                        DrawFrozenJunctionDebugSnapshot();
                    else
                        DrawLiveJunctionDebug(roads);
                }
                
                // 绘制路口组
                if (IsToolUiVisible)
                {
                    if (useFrozenJunctionDebugSnapshot)
                        DrawFrozenJunctionGroups();
                    else if (junctionGroups != null)
                        DrawJunctionGroups();
                }
                
                // 绘制窗口
                if (showWindow)
                {
                    Handles.BeginGUI();
            windowRect = GUI.Window(123456, windowRect, DrawOfficialWindow, GUIContent.none, GUIStyle.none);
                    Handles.EndGUI();
                }
                
                // 如果启用了自动更新，更新所有路口
                if(autoUpdateJunction && e.type == EventType.MouseUp)
                {
                    JunctionGenerator.UpdateAllJunctions();
                }

                // 自动更新采样点与路口预览
                if (IsToolUiVisible && (autoUpdateSamplePoints || autoPreviewJunctions))
                {
                    var changedRoads = CollectChangedRoads(roads);
                    bool hasChangedRoads = changedRoads.Count > 0;
                    bool analysisUpdated = false;

                    if (autoUpdateSamplePoints && hasChangedRoads)
                    {
                        UpdateChangedRoads(changedRoads, roads);
                        analysisUpdated = true;
                    }

                    bool previewTriggered = autoPreviewJunctions &&
                                            (previewRefreshRequested ||
                                             (hasChangedRoads && ShouldUpdatePreviewOnEvent(e)));

                    if (previewTriggered)
                    {
                        if (!analysisUpdated)
                        {
                            UpdateJunctionAnalysisForPreview(roads, changedRoads, previewRefreshRequested);
                        }

                        RefreshAutomaticJunctionPreview(roads);
                        previewRefreshRequested = false;
                        lastPreviewUpdateTime = EditorApplication.timeSinceStartup;
                    }
                }

                // 添加采样点调试绘制
                if (showSamplePointsDebug)
                {
                    DrawSamplePointsDebug();
                }

                // 添加有序点列表调试绘制
                if (showOrderedPointsDebug)
                {
                    DrawOrderedPointsDebug();
                }

                sceneView.Repaint();
            }
            catch (System.Exception ex)
            {
                // 记录错误但不中断编辑器
                Debug.LogError($"OnSceneGUI 发生错误: {ex.Message}");
            }
        }

        private static void DrawLegacyWindow(int windowID)
        {
            EditorGUI.BeginChangeCheck();

            showControlsa = EditorGUILayout.Foldout(showControlsa, "路网编辑工具"); // 创建折叠式面板标题

            if (showControlsa)
            {
                // 自定义显示中文名称的枚举选择
                selectedRoadType = CustomEnumPopup("道路类型", selectedRoadType);

                // 显示自定义道路信息的下拉菜单
                selectedCustomRoadInfo = CustomRoadInfoPopup("自定义道路信息", selectedCustomRoadInfo);

                if (EditorGUI.EndChangeCheck())
                {
                    SetDefaultWidth();
                }

                GUILayout.Label($"输入道路宽度 (默认宽度: {GetDefaultWidth()})", EditorStyles.boldLabel);
                float customWidth = GetDefaultWidth();
                if (GUILayout.Button(new GUIContent(" 保存隔离带网格", EditorGUIUtility.IconContent("d_SaveAs").image)))
                {
                    ExportGreenBeltMeshesToOBJ();
                }
                // 生成按钮
                if (GUILayout.Button(new GUIContent(" 生成道路", EditorGUIUtility.IconContent("d_CreateAddNew").image)))
                {
                    GenerateRoad(selectedRoadType, customWidth, selectedCustomRoadInfo);
                }







                // 上传到道路HDA按钮
                if (GUILayout.Button(new GUIContent(" 上传到道路HDA", EditorGUIUtility.IconContent("d_Import").image)))
                {
                    UploadToRoadHDA();
                }

                // 保存道路信息按钮
                if (GUILayout.Button(new GUIContent(" 保存道路信息", EditorGUIUtility.IconContent("d_SaveAs").image)))
                {
                    SaveRoadDataAsPrefabWithCleanupAndUpdate();
                }

                // 读取道路信息按钮
                if (GUILayout.Button(new GUIContent(" 读取道路信息", EditorGUIUtility.IconContent("d_Import").image)))
                {
                    LoadRoadDataFromPrefabWithCleanupAndUpdate();
                }

                // 导出道路信息按钮
                if (GUILayout.Button(new GUIContent(" 导出道路信息（JSON）")))
                {
                    ExportRoadDataToJson();
                }

                // 替换选定对象的道路类型按钮
                if (GUILayout.Button(new GUIContent(" 替换选定对象的道路类型", EditorGUIUtility.IconContent("d_CreateAddNew").image)))
                {
                    ReplaceRoadType(selectedRoadType, customWidth, selectedCustomRoadInfo);
                }

                // 一键贴地按钮
                if (GUILayout.Button(new GUIContent(" 一键贴地")))
                {
                    SnapToGround();
                }

                if (GUILayout.Button(new GUIContent(" 标线贴路面")))
                {
                    RecalculateRoadMeshInfo();
                    AlignRoadMarksToRoadSurface();
                }

                // 清理曲线按钮
                if (GUILayout.Button(new GUIContent(" 清理曲线", EditorGUIUtility.IconContent("TreeEditor.Trash").image)))
                {
                    CleanUpSplines();
                }

                // 更新道路参数按钮
                if (GUILayout.Button(new GUIContent(" 重置当前选择道路参数", EditorGUIUtility.IconContent("d_Settings").image)))
                {
                    UpdateRoadParameters();
                }

                if (GUILayout.Button(new GUIContent(" 重置所有道路参数（慎用）", EditorGUIUtility.IconContent("d_Settings").image)))
                {
                    UpdateRoadParameters();
                }

                if (GUILayout.Button(new GUIContent("设置为桥梁区域")))
                {
                    SetBridgeKnot(true);
                    EditorUtility.DisplayDialog("提示", "设置完成", "确定");
                }
                if (GUILayout.Button(new GUIContent("移除桥梁区域")))
                {
                    SetBridgeKnot(false);
                    EditorUtility.DisplayDialog("提示", "移除完成", "确定");
                }
                if (GUILayout.Button(new GUIContent("显隐引导线标线")))
                {
                    SwitchRoadMarkersStatus();
                }
            }
            showControlsb = EditorGUILayout.Foldout(showControlsb, "编辑生态区域"); // 创建折叠式面板标题、



            if (showControlsb) 
            {
                if (GUILayout.Button(new GUIContent(" 生成生态区域", EditorGUIUtility.IconContent("d_CreateAddNew").image)))
                {
                    GenerateRegion();
                }


            }











            showControlsc = EditorGUILayout.Foldout(showControlsc, "自动化生成工具"); // 创建折叠式面板标题

            if (showControlsc)
            {
                if (GUILayout.Button(new GUIContent(" 打开大世界生成工具", EditorGUIUtility.IconContent("d_CreateAddNew").image)))
                {
                   LoadPcgTool();
                }
                if (GUILayout.Button(new GUIContent(" 路网地形贴合工具", EditorGUIUtility.IconContent("d_CreateAddNew").image)))
                {
                    SplineTerrainTool.ShowWindow();
                }


            }

            // 路口控制
            EditorGUILayout.Space(10);
            showJunctionControls = EditorGUILayout.Foldout(showJunctionControls, "路口控制");
            if (showJunctionControls)
            {
                EditorGUI.indentLevel++;
                
                // 添加算法参数控制
                showAlgorithmParameters = EditorGUILayout.Foldout(showAlgorithmParameters, "算法参数");
                if (showAlgorithmParameters)
                {
                    EditorGUI.indentLevel++;
                    
                    // 全局高度阈值控制
                    float newHeight = EditorGUILayout.Slider(
                        new GUIContent("高度检测阈值", "检测路口连接时的最大允许高度差"), 
                        globalHeightThreshold, 
                        0f, 
                        10f
                    );
                    
                    if (Math.Abs(newHeight - globalHeightThreshold) > 0.001f)
                    {
                        globalHeightThreshold = newHeight;
                        // 如果启用了自动更新，立即更新采样点
                        if (autoUpdateSamplePoints)
                        {
                            SceneView.RepaintAll();
                        }
                    }
                    
                    EditorGUI.indentLevel--;
                }
                
                // 添加自动更新采样点的选项
                bool newAutoUpdateSamplePoints = EditorGUILayout.Toggle(
                    new GUIContent("实时更新采样点", "每帧更新采样点位置和连接"),
                    autoUpdateSamplePoints);
                if (newAutoUpdateSamplePoints != autoUpdateSamplePoints)
                {
                    autoUpdateSamplePoints = newAutoUpdateSamplePoints;
                    if (autoUpdateSamplePoints)
                    {
                        lastRoadState.Clear();
                        ClearFrozenJunctionDebugSnapshot();
                    }

                    SceneView.RepaintAll();
                }

                bool newAutoPreviewJunctions = EditorGUILayout.Toggle(
                    new GUIContent("自动预览实时路口", "拖动或释放节点时自动生成路口预览"),
                    autoPreviewJunctions);
                if (newAutoPreviewJunctions != autoPreviewJunctions)
                {
                    autoPreviewJunctions = newAutoPreviewJunctions;
                    previewRefreshRequested = autoPreviewJunctions;
                    lastPreviewUpdateTime = 0d;

                    if (!autoPreviewJunctions)
                        ClearPreviewObjects();

                    SceneView.RepaintAll();
                }

                if (autoPreviewJunctions)
                {
                    GUILayout.BeginHorizontal();
                    GUI.enabled = HasPreviewObjects();
                    if (GUILayout.Button("应用预览"))
                    {
                        ApplyPreviewJunctions();
                    }
                    if (GUILayout.Button("清除预览"))
                    {
                        ClearPreviewObjects();
                        SceneView.RepaintAll();
                    }
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                }
                
                // 添加自动分析创建路口按钮
                if(GUILayout.Button(new GUIContent(" 自动分析创建路口", EditorGUIUtility.IconContent("d_AutoLightmap").image)))
                {
                    if(EditorUtility.DisplayDialog("确认", "是否要自动分析并创建所有可能的路口？", "确定", "取消"))
                    {
                        AutoAnalyzeAndCreateJunctions();
                    }
                }
                
                // Gizmo显示控制
                EditorGUILayout.LabelField("Gizmo显示控制", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                
                showGizmos = EditorGUILayout.Toggle("显示所有Gizmo", showGizmos);
                showKnotPoints = EditorGUILayout.Toggle("显示路段端点", showKnotPoints);
                showBoundaryControlKnots = EditorGUILayout.Toggle("显示路口边界控制点", showBoundaryControlKnots);
                
                GUI.enabled = showGizmos;
                JunctionData.ShowGizmos = EditorGUILayout.Toggle("显示路口Gizmo", JunctionData.ShowGizmos);
                
                GUI.enabled = JunctionData.ShowGizmos;
                JunctionData.ShowCenterPoints = EditorGUILayout.Toggle("显示中心点", JunctionData.ShowCenterPoints);
                JunctionData.ShowEdgePoints = EditorGUILayout.Toggle("显示边缘点", JunctionData.ShowEdgePoints);
                JunctionData.ShowEdgeLines = EditorGUILayout.Toggle("显示边缘线", JunctionData.ShowEdgeLines);
                JunctionData.ShowSidewalkLines = EditorGUILayout.Toggle("显示人行道线", JunctionData.ShowSidewalkLines);
                JunctionData.ShowNormals = EditorGUILayout.Toggle("显示法线", JunctionData.ShowNormals);
                JunctionData.ShowLabels = EditorGUILayout.Toggle("显示标签", JunctionData.ShowLabels);
                JunctionData.ShowCurves = EditorGUILayout.Toggle("显示曲线", JunctionData.ShowCurves);
                GUI.enabled = true;
                
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(5);

                // 其他路口控制选项
                autoUpdateJunction = EditorGUILayout.Toggle("自动更新路口", autoUpdateJunction);
                
                if (GUILayout.Button("创建路口"))
                {
                    if (selectedConnections.Count >= 2)
                    {
                        // 在创建路口前对连接点进行顺时针排序
                        SortConnectionsClockwise();

                        var junctionObject = JunctionGenerator.CreateJunction(selectedConnections);
                        if (junctionObject != null)
                        {
                            junctionGroups.Clear();
                            SceneView.RepaintAll();
                        }

                        selectedConnections.Clear(); // 创建完路口后清空选择
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("错误", "创建路口需要至少选择两个连接点", "确定");
                    }
                }
                
                EditorGUILayout.LabelField("已选择的连接点:", EditorStyles.boldLabel);
                for (int i = 0; i < selectedConnections.Count; i++)
                {
                    var conn = selectedConnections[i];
                    EditorGUILayout.BeginHorizontal();
                    
                    // 添加空检查
                    string roadName = conn.road != null ? conn.road.name : "已删除的道路";
                    EditorGUILayout.LabelField($"道路: {roadName}, 样条: {conn.splineIndex}, 节点: {conn.knotIndex}");
                    
                    if (GUILayout.Button("移除", GUILayout.Width(60)))
                    {
                        selectedConnections.RemoveAt(i);
                        i--;
                    }
                    EditorGUILayout.EndHorizontal();
                    
                    // 如果道路对象已被销毁，自动从列表中移除
                    if (conn.road == null)
                    {
                        selectedConnections.RemoveAt(i);
                        i--;
                        SceneView.RepaintAll();
                    }
                }
                
                GUI.enabled = selectedConnections.Count >= 2;
                if(GUILayout.Button("清除选择"))
                {
                    selectedConnections.Clear();
                    SceneView.RepaintAll();
                }
                GUI.enabled = true;
                
                if(GUILayout.Button("更新所有路口"))
                {
                    JunctionGenerator.UpdateAllJunctions();
                    SceneView.RepaintAll();
                }
                
                EditorGUI.indentLevel--;

                if(GUILayout.Button(new GUIContent(" 创建凹包Knot", EditorGUIUtility.IconContent("d_CreateAddNew").image)))
                {
                    CreateBoundaryKnots();
                }
            }

            // 添加路段控制面板
            EditorGUILayout.Space(10);
            showRoadSegmentControls = EditorGUILayout.Foldout(showRoadSegmentControls, "路段控制");
            if (showRoadSegmentControls)
            {
                EditorGUI.indentLevel++;
                
                // 路段Gizmo总开关
                bool prevShowRoadSegmentGizmos = showRoadSegmentGizmos;
                showRoadSegmentGizmos = EditorGUILayout.Toggle("显示路段Gizmo", showRoadSegmentGizmos);
                
                // 如果总开关改变，更新LoftRoadBehaviour中的静态变量
                if (prevShowRoadSegmentGizmos != showRoadSegmentGizmos)
                {
                    Unity.Splines.Examples.LoftRoadBehaviour.ShowLaneGizmos = showRoadSegmentGizmos;
                    SceneView.RepaintAll();
                }
                
                // 只有在总开关打开时才启用子选项
                GUI.enabled = showRoadSegmentGizmos;
                
                // 车道中心线显示控制
                bool showLaneCenterLines = Unity.Splines.Examples.LoftRoadBehaviour.ShowLaneCenterLines;
                bool newShowLaneCenterLines = EditorGUILayout.Toggle("显示车道中心线", showLaneCenterLines);
                if (showLaneCenterLines != newShowLaneCenterLines)
                {
                    Unity.Splines.Examples.LoftRoadBehaviour.ShowLaneCenterLines = newShowLaneCenterLines;
                    SceneView.RepaintAll();
                }
                
                // 车道边界线显示控制
                bool showLaneBoundaryLines = Unity.Splines.Examples.LoftRoadBehaviour.ShowLaneBoundaryLines;
                bool newShowLaneBoundaryLines = EditorGUILayout.Toggle("显示车道边界线", showLaneBoundaryLines);
                if (showLaneBoundaryLines != newShowLaneBoundaryLines)
                {
                    Unity.Splines.Examples.LoftRoadBehaviour.ShowLaneBoundaryLines = newShowLaneBoundaryLines;
                    SceneView.RepaintAll();
                }
                
                // 车道标签显示控制
                bool showLaneLabels = Unity.Splines.Examples.LoftRoadBehaviour.ShowLaneLabels;
                bool newShowLaneLabels = EditorGUILayout.Toggle("显示车道标签", showLaneLabels);
                if (showLaneLabels != newShowLaneLabels)
                {
                    Unity.Splines.Examples.LoftRoadBehaviour.ShowLaneLabels = newShowLaneLabels;
                    SceneView.RepaintAll();
                }
                
                // 重新启用GUI
                GUI.enabled = true;
                
                if (GUILayout.Button("更新所有路段"))
                {
                    // 查找所有LoftRoadBehaviour组件并调用LoftAllRoads方法
                    var roads = GameObject.FindObjectsOfType<Unity.Splines.Examples.LoftRoadBehaviour>();
                    foreach (var road in roads)
                    {
                        try
                        {
                            road.LoftAllRoads();
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogError($"更新路段 {road.name} 时出错: {ex.Message}");
                        }
                    }
                    SceneView.RepaintAll();
                }
                
                EditorGUI.indentLevel--;
            }

            GUILayout.FlexibleSpace();

            // 在原有的折叠面板后添加DEBUG面板
            EditorGUILayout.Space(10);
            showDebugControls = EditorGUILayout.Foldout(showDebugControls, "DEBUG工具");
            if (showDebugControls)
            {
                EditorGUI.indentLevel++;
                
                bool prevSamplePoints = showSamplePointsDebug;
                showSamplePointsDebug = EditorGUILayout.Toggle("显示采样点顺序", showSamplePointsDebug);
                if (prevSamplePoints != showSamplePointsDebug)
                {
                    SceneView.RepaintAll();
                }
                
                bool prevOrderedPoints = showOrderedPointsDebug;
                showOrderedPointsDebug = EditorGUILayout.Toggle("显示有序点列表", showOrderedPointsDebug);
                if (prevOrderedPoints != showOrderedPointsDebug)
                {
                    SceneView.RepaintAll();
                }
                
                EditorGUI.indentLevel--;
            }

            // 绘制关闭按钮在右上角
            Rect closeButtonRect = new Rect(windowRect.width - 20, 5, 15, 15);
            if (GUI.Button(closeButtonRect, EditorGUIUtility.IconContent("d_winbtn_win_close").image))
            {
                showWindow = false;
                EditorPrefs.SetBool(ShowWindowPrefKey, showWindow);
                SceneView.RepaintAll();
            }

            GUI.DragWindow();
        }

        private static RoadType CustomEnumPopup(string label, RoadType selectedType)
        {
            EnsureRoadTypeSelectorStyles();

            EditorGUILayout.LabelField(label, roadTypeSelectorLabelStyle);
            Rect cardRect = GUILayoutUtility.GetRect(0f, SelectedRoadTypeCardHeight, GUILayout.ExpandWidth(true));

            if (DrawRoadTypeCard(cardRect, selectedType, isSelected: true, isCompact: false, footerText: "点击选择其他预设"))
            {
                Rect popupRect = GUIUtility.GUIToScreenRect(cardRect);
                PopupWindow.Show(popupRect, new RoadTypeSelectionPopup());
            }

            GUILayout.Space(4f);
            return selectedRoadType;
        }

        private static void DrawBoundaryControlKnotOverlay(IEnumerable<LoftRoadBehaviour> roads)
        {
            if (roads == null)
            {
                return;
            }

            CompareFunction previousZTest = Handles.zTest;
            Color previousColor = Handles.color;
            Handles.zTest = CompareFunction.LessEqual;

            foreach (LoftRoadBehaviour road in roads)
            {
                IReadOnlyList<RoadMarker> markers = road?.RoadMarkers;
                if (markers == null)
                {
                    continue;
                }

                for (int markerIndex = 0; markerIndex < markers.Count; markerIndex++)
                {
                    RoadMarker marker = markers[markerIndex];
                    if (!ShouldDrawBoundaryControlMarker(marker)
                        || !TryGetBoundaryControlMarkerWorldPosition(road, marker, out Vector3 worldPosition))
                    {
                        continue;
                    }

                    float size = HandleUtility.GetHandleSize(worldPosition) * 0.18f;
                    Vector3[] diamond = new[]
                    {
                        worldPosition + Vector3.forward * size,
                        worldPosition + Vector3.right * size,
                        worldPosition - Vector3.forward * size,
                        worldPosition - Vector3.right * size,
                    };

                    Handles.color = BoundaryControlFillColor;
                    Handles.DrawAAConvexPolygon(diamond);

                    Handles.color = BoundaryControlRingColor;
                    Handles.DrawWireDisc(worldPosition, Vector3.up, size * 1.05f);
                    Handles.DrawAAPolyLine(3f, new[]
                    {
                        diamond[0],
                        diamond[1],
                        diamond[2],
                        diamond[3],
                        diamond[0],
                    });

                    GUIStyle labelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                    {
                        normal = { textColor = BoundaryControlLabelColor },
                        alignment = TextAnchor.MiddleCenter,
                    };
                    Handles.Label(worldPosition + Vector3.up * (size * 0.9f), GetBoundaryControlMarkerLabel(marker), labelStyle);
                }
            }

            Handles.color = previousColor;
            Handles.zTest = previousZTest;
        }

        private static bool ShouldDrawBoundaryControlMarker(RoadMarker marker)
        {
            return marker != null
                && marker.kind == RoadMarkerKind.Junction
                && marker.isPinnedToKnot
                && marker.isGeneratedBoundaryControl;
        }

        private static bool TryGetBoundaryControlMarkerWorldPosition(
            LoftRoadBehaviour road,
            RoadMarker marker,
            out Vector3 worldPosition)
        {
            worldPosition = Vector3.zero;
            if (road?.Container?.Splines == null
                || marker == null
                || marker.splineIndex < 0
                || marker.splineIndex >= road.Container.Splines.Count)
            {
                return false;
            }

            Spline spline = road.Container.Splines[marker.splineIndex];
            if (spline == null || spline.Count == 0)
            {
                return false;
            }

            if (marker.isPinnedToKnot
                && marker.preferredKnotIndex >= 0
                && marker.preferredKnotIndex < spline.Count)
            {
                worldPosition = road.transform.TransformPoint(spline[marker.preferredKnotIndex].Position);
                return true;
            }

            worldPosition = road.transform.TransformPoint(spline.EvaluatePosition(Mathf.Clamp01(marker.curveU)));
            return true;
        }

        private static string GetBoundaryControlMarkerLabel(RoadMarker marker)
        {
            switch (marker.boundaryRole)
            {
                case RoadMarkerBoundaryRole.LowerCurveU:
                    return "J-L";
                case RoadMarkerBoundaryRole.UpperCurveU:
                    return "J-U";
                case RoadMarkerBoundaryRole.Endpoint:
                    return "J-E";
                default:
                    return "J";
            }
        }

        private static void EnsureRoadTypeSelectorStyles()
        {
            if (roadTypeSelectorLabelStyle != null)
            {
                return;
            }

            Color primaryTextColor = EditorGUIUtility.isProSkin
                ? new Color(0.93f, 0.95f, 0.97f, 1f)
                : new Color(0.18f, 0.20f, 0.22f, 1f);
            Color secondaryTextColor = EditorGUIUtility.isProSkin
                ? new Color(0.69f, 0.73f, 0.77f, 1f)
                : new Color(0.37f, 0.41f, 0.46f, 1f);

            roadTypeSelectorLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                normal = { textColor = primaryTextColor }
            };

            roadTypeCardTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = primaryTextColor }
            };

            roadTypeCardSummaryStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                wordWrap = false,
                clipping = TextClipping.Clip,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = secondaryTextColor }
            };

            roadTypeCardHintStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = secondaryTextColor }
            };

            roadTypeCardBadgeStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                fontSize = 9,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            roadTypeCardChevronStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = secondaryTextColor }
            };

            roadTypePopupHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = primaryTextColor }
            };

            roadTypePopupSubHeaderStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                fontSize = 10,
                normal = { textColor = secondaryTextColor }
            };
        }

        private static bool DrawRoadTypeCard(Rect rect, RoadType roadType, bool isSelected, bool isCompact, string footerText)
        {
            EnsureRoadTypeSelectorStyles();

            bool isHovered = rect.Contains(Event.current.mousePosition);
            Color backgroundColor = isSelected
                ? (EditorGUIUtility.isProSkin ? new Color(0.18f, 0.36f, 0.62f, 0.18f) : new Color(0.31f, 0.52f, 0.84f, 0.14f))
                : isHovered
                    ? (EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.03f) : new Color(0f, 0f, 0f, 0.03f))
                    : Color.clear;
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            if (backgroundColor.a > 0.001f)
            {
                Rect fillRect = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
                EditorGUI.DrawRect(fillRect, backgroundColor);
            }

            if (isSelected)
            {
                EditorGUI.DrawRect(new Rect(rect.x + 1f, rect.y + 1f, 2f, rect.height - 2f), RoadTypeAccentColor);
            }

            Rect thumbnailRect = new Rect(rect.x + 8f, rect.y + 8f, isCompact ? 94f : 106f, rect.height - 16f);
            Texture2D thumbnail = GetRoadTypeThumbnail(roadType);
            if (thumbnail != null)
            {
                GUI.DrawTexture(thumbnailRect, thumbnail, ScaleMode.StretchToFill, false);
            }

            DrawRectOutline(thumbnailRect, EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.06f) : new Color(0f, 0f, 0f, 0.08f), 1f);

            Rect contentRect = new Rect(thumbnailRect.xMax + 10f, rect.y + 8f, rect.width - thumbnailRect.width - 34f, rect.height - 16f);
            Rect titleRect = new Rect(contentRect.x, contentRect.y, contentRect.width, isCompact ? 34f : 38f);
            Rect summaryRect = new Rect(contentRect.x, rect.yMax - 34f, contentRect.width, 14f);
            Rect footerRect = new Rect(contentRect.x, rect.yMax - 18f, contentRect.width, 14f);

            GUI.Label(titleRect, RoadDefaultsInfors.GetRoadName(roadType), roadTypeCardTitleStyle);
            GUI.Label(summaryRect, GetRoadTypeSummary(roadType), roadTypeCardSummaryStyle);

            if (!string.IsNullOrEmpty(footerText))
            {
                GUI.Label(footerRect, footerText, roadTypeCardHintStyle);
            }

            if (isSelected)
            {
                Rect badgeRect = new Rect(rect.xMax - 58f, rect.y + 8f, 46f, 18f);
                GUI.Label(badgeRect, "已选", roadTypeCardHintStyle);
            }
            else
            {
                Rect chevronRect = new Rect(rect.xMax - 22f, rect.y, 12f, rect.height);
                GUI.Label(chevronRect, "›", roadTypeCardChevronStyle);
            }

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        private static void DrawRectOutline(Rect rect, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private static Texture2D GetRoadTypeThumbnail(RoadType roadType)
        {
            if (roadTypeThumbnailCache.TryGetValue(roadType, out Texture2D cachedTexture) && cachedTexture != null)
            {
                return cachedTexture;
            }

            Texture2D texture = CreateRoadTypeThumbnail(roadType);
            roadTypeThumbnailCache[roadType] = texture;
            return texture;
        }

        private static Texture2D CreateRoadTypeThumbnail(RoadType roadType)
        {
            const int textureWidth = 192;
            const int textureHeight = 112;

            Texture2D texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false)
            {
                name = $"RoadTypeThumbnail_{roadType}",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color[] pixels = Enumerable.Repeat(new Color(0.09f, 0.10f, 0.11f, 1f), textureWidth * textureHeight).ToArray();
            RectInt contentRect = new RectInt(8, 8, textureWidth - 16, textureHeight - 16);
            FillRect(pixels, textureWidth, textureHeight, contentRect, new Color(0.12f, 0.13f, 0.14f, 1f));

            if (IsRiverRoadType(roadType))
            {
                DrawRiverThumbnail(pixels, textureWidth, textureHeight, contentRect, roadType);
            }
            else if (IsTrainRoadType(roadType))
            {
                DrawTrainThumbnail(pixels, textureWidth, textureHeight, contentRect, roadType);
            }
            else if (roadType == RoadType.Traffic_Pedestrian)
            {
                DrawPedestrianThumbnail(pixels, textureWidth, textureHeight, contentRect);
            }
            else if (roadType == RoadType.Traffic_ZebraCross)
            {
                DrawZebraCrossThumbnail(pixels, textureWidth, textureHeight, contentRect);
            }
            else if (IsPathRoadType(roadType))
            {
                DrawPathThumbnail(pixels, textureWidth, textureHeight, contentRect, roadType);
            }
            else
            {
                DrawRoadThumbnail(pixels, textureWidth, textureHeight, contentRect, roadType);
            }

            StrokeRect(pixels, textureWidth, textureHeight, contentRect, new Color(1f, 1f, 1f, 0.06f));
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static void DrawRoadThumbnail(Color[] pixels, int textureWidth, int textureHeight, RectInt rect, RoadType roadType)
        {
            float laneWidth = Mathf.Max(1f, RoadDefaultsInfors.GetDefaultLaneWidth(roadType));
            float leftSidewalk = Mathf.Max(0f, RoadDefaultsInfors.GetDefaultSidewalkWidth(roadType));
            float rightSidewalk = Mathf.Max(0f, RoadDefaultsInfors.GetDefaultRightSidewalkWidth(roadType));
            float greenBelt = Mathf.Max(0f, RoadDefaultsInfors.GetDefaultGreenBeltWidth(roadType));
            (int leftLaneCount, int rightLaneCount) = RoadDefaultsInfors.GetDefaultLaneCounts(roadType);

            float totalUnits = leftSidewalk + rightSidewalk + greenBelt + ((leftLaneCount + rightLaneCount) * laneWidth);
            if (totalUnits <= 0.01f)
            {
                DrawPathThumbnail(pixels, textureWidth, textureHeight, rect, roadType);
                return;
            }

            RectInt roadBody = new RectInt(rect.x + 4, rect.y + 4, rect.width - 8, rect.height - 8);
            float cursorUnits = 0f;

            if (leftSidewalk > 0.01f)
            {
                RectInt sidewalkRect = GetProportionalRect(roadBody, totalUnits, cursorUnits, leftSidewalk);
                DrawSidewalkBand(pixels, textureWidth, textureHeight, sidewalkRect);
                cursorUnits += leftSidewalk;
            }

            for (int laneIndex = 0; laneIndex < leftLaneCount; laneIndex++)
            {
                RectInt laneRect = GetProportionalRect(roadBody, totalUnits, cursorUnits, laneWidth);
                DrawLaneBand(pixels, textureWidth, textureHeight, laneRect, drawArrowUp: false);
                cursorUnits += laneWidth;

                if (laneIndex < leftLaneCount - 1)
                {
                    DrawDashedVerticalLine(pixels, textureWidth, textureHeight, laneRect.xMax, roadBody.y + 8, roadBody.yMax - 8, new Color(1f, 1f, 1f, 0.65f), 2, 7, 5);
                }
            }

            if (greenBelt > 0.01f)
            {
                RectInt greenBeltRect = GetProportionalRect(roadBody, totalUnits, cursorUnits, greenBelt);
                DrawGreenBeltBand(pixels, textureWidth, textureHeight, greenBeltRect);
                cursorUnits += greenBelt;
            }
            else if (leftLaneCount > 0 && rightLaneCount > 0)
            {
                int centerX = roadBody.x + Mathf.RoundToInt(cursorUnits / totalUnits * roadBody.width);
                DrawSolidVerticalLine(pixels, textureWidth, textureHeight, centerX - 1, roadBody.y + 6, roadBody.yMax - 6, new Color(0.96f, 0.79f, 0.22f, 0.92f), 2);
                DrawSolidVerticalLine(pixels, textureWidth, textureHeight, centerX + 2, roadBody.y + 6, roadBody.yMax - 6, new Color(0.96f, 0.79f, 0.22f, 0.92f), 2);
            }

            for (int laneIndex = 0; laneIndex < rightLaneCount; laneIndex++)
            {
                RectInt laneRect = GetProportionalRect(roadBody, totalUnits, cursorUnits, laneWidth);
                DrawLaneBand(pixels, textureWidth, textureHeight, laneRect, drawArrowUp: true);
                cursorUnits += laneWidth;

                if (laneIndex < rightLaneCount - 1)
                {
                    DrawDashedVerticalLine(pixels, textureWidth, textureHeight, laneRect.xMax, roadBody.y + 8, roadBody.yMax - 8, new Color(1f, 1f, 1f, 0.65f), 2, 7, 5);
                }
            }

            if (rightSidewalk > 0.01f)
            {
                RectInt sidewalkRect = GetProportionalRect(roadBody, totalUnits, cursorUnits, rightSidewalk);
                DrawSidewalkBand(pixels, textureWidth, textureHeight, sidewalkRect);
            }
        }

        private static void DrawTrainThumbnail(Color[] pixels, int textureWidth, int textureHeight, RectInt rect, RoadType roadType)
        {
            FillRect(pixels, textureWidth, textureHeight, rect, new Color(0.20f, 0.18f, 0.16f, 1f));

            int trackCount = Mathf.Max(1, RoadDefaultsInfors.GetDefaultLaneCounts(roadType).leftLaneCount);
            RectInt trackArea = new RectInt(rect.x + 8, rect.y + 8, rect.width - 16, rect.height - 16);
            float totalUnits = trackCount;

            for (int index = 0; index < trackCount; index++)
            {
                RectInt trackRect = GetProportionalRect(trackArea, totalUnits, index, 1f);
                FillRect(pixels, textureWidth, textureHeight, trackRect, new Color(0.24f, 0.21f, 0.18f, 1f));

                for (int y = trackRect.y + 6; y < trackRect.yMax - 6; y += 10)
                {
                    FillRect(pixels, textureWidth, textureHeight, new RectInt(trackRect.x + 1, y, Mathf.Max(1, trackRect.width - 2), 3), new Color(0.41f, 0.31f, 0.22f, 1f));
                }

                int leftRailX = trackRect.x + Mathf.Max(2, trackRect.width / 3);
                int rightRailX = trackRect.xMax - Mathf.Max(3, trackRect.width / 3);
                DrawSolidVerticalLine(pixels, textureWidth, textureHeight, leftRailX, trackRect.y + 2, trackRect.yMax - 2, new Color(0.82f, 0.84f, 0.86f, 1f), 2);
                DrawSolidVerticalLine(pixels, textureWidth, textureHeight, rightRailX, trackRect.y + 2, trackRect.yMax - 2, new Color(0.82f, 0.84f, 0.86f, 1f), 2);
            }
        }

        private static void DrawRiverThumbnail(Color[] pixels, int textureWidth, int textureHeight, RectInt rect, RoadType roadType)
        {
            FillRect(pixels, textureWidth, textureHeight, rect, new Color(0.20f, 0.24f, 0.19f, 1f));

            RectInt waterRect = new RectInt(rect.x + 10, rect.y + 6, rect.width - 20, rect.height - 12);
            FillRect(pixels, textureWidth, textureHeight, waterRect, new Color(0.16f, 0.45f, 0.66f, 1f));
            FillRect(pixels, textureWidth, textureHeight, new RectInt(waterRect.x, waterRect.y + 6, waterRect.width, 4), new Color(1f, 1f, 1f, 0.08f));
            FillRect(pixels, textureWidth, textureHeight, new RectInt(waterRect.x, waterRect.yMax - 16, waterRect.width, 3), new Color(1f, 1f, 1f, 0.05f));

            FillRect(pixels, textureWidth, textureHeight, new RectInt(rect.x + 2, rect.y + 2, 8, rect.height - 4), new Color(0.43f, 0.36f, 0.25f, 1f));
            FillRect(pixels, textureWidth, textureHeight, new RectInt(rect.xMax - 10, rect.y + 2, 8, rect.height - 4), new Color(0.43f, 0.36f, 0.25f, 1f));
        }

        private static void DrawPedestrianThumbnail(Color[] pixels, int textureWidth, int textureHeight, RectInt rect)
        {
            FillRect(pixels, textureWidth, textureHeight, rect, new Color(0.46f, 0.48f, 0.50f, 1f));

            for (int x = rect.x + 6; x < rect.xMax - 4; x += 18)
            {
                DrawSolidVerticalLine(pixels, textureWidth, textureHeight, x, rect.y + 4, rect.yMax - 4, new Color(1f, 1f, 1f, 0.08f), 2);
            }

            for (int y = rect.y + 8; y < rect.yMax - 4; y += 16)
            {
                FillRect(pixels, textureWidth, textureHeight, new RectInt(rect.x + 4, y, rect.width - 8, 2), new Color(1f, 1f, 1f, 0.06f));
            }
        }

        private static void DrawZebraCrossThumbnail(Color[] pixels, int textureWidth, int textureHeight, RectInt rect)
        {
            FillRect(pixels, textureWidth, textureHeight, rect, new Color(0.19f, 0.20f, 0.22f, 1f));

            for (int x = rect.x + 10; x < rect.xMax - 10; x += 18)
            {
                FillRect(pixels, textureWidth, textureHeight, new RectInt(x, rect.y + 8, 10, rect.height - 16), new Color(0.94f, 0.94f, 0.92f, 1f));
            }
        }

        private static void DrawPathThumbnail(Color[] pixels, int textureWidth, int textureHeight, RectInt rect, RoadType roadType)
        {
            bool isBoardwalk = roadType == RoadType.TRoad_G;
            Color baseColor = isBoardwalk
                ? new Color(0.40f, 0.28f, 0.18f, 1f)
                : new Color(0.36f, 0.28f, 0.20f, 1f);
            Color lineColor = isBoardwalk
                ? new Color(1f, 1f, 1f, 0.10f)
                : new Color(1f, 1f, 1f, 0.05f);

            FillRect(pixels, textureWidth, textureHeight, rect, baseColor);

            int step = isBoardwalk ? 12 : 18;
            for (int y = rect.y + 6; y < rect.yMax - 4; y += step)
            {
                FillRect(pixels, textureWidth, textureHeight, new RectInt(rect.x + 4, y, rect.width - 8, 2), lineColor);
            }
        }

        private static RectInt GetProportionalRect(RectInt sourceRect, float totalUnits, float startUnits, float segmentUnits)
        {
            int xMin = sourceRect.x + Mathf.RoundToInt((startUnits / totalUnits) * sourceRect.width);
            int xMax = sourceRect.x + Mathf.RoundToInt(((startUnits + segmentUnits) / totalUnits) * sourceRect.width);
            return new RectInt(xMin, sourceRect.y, Mathf.Max(1, xMax - xMin), sourceRect.height);
        }

        private static void DrawLaneBand(Color[] pixels, int textureWidth, int textureHeight, RectInt rect, bool drawArrowUp)
        {
            FillRect(pixels, textureWidth, textureHeight, rect, new Color(0.20f, 0.21f, 0.23f, 1f));
            FillRect(pixels, textureWidth, textureHeight, new RectInt(rect.x + 1, rect.y + 3, Mathf.Max(1, rect.width - 2), 2), new Color(1f, 1f, 1f, 0.03f));

            int arrowCenterX = rect.x + rect.width / 2;
            int arrowCenterY = rect.y + rect.height / 2;
            DrawLaneArrow(pixels, textureWidth, textureHeight, arrowCenterX, arrowCenterY, Mathf.Clamp(rect.width / 2, 4, 8), Mathf.Clamp(rect.height / 4, 8, 14), drawArrowUp, new Color(1f, 1f, 1f, 0.28f));
        }

        private static void DrawSidewalkBand(Color[] pixels, int textureWidth, int textureHeight, RectInt rect)
        {
            FillRect(pixels, textureWidth, textureHeight, rect, new Color(0.50f, 0.52f, 0.54f, 1f));

            for (int y = rect.y + 4; y < rect.yMax - 3; y += 12)
            {
                FillRect(pixels, textureWidth, textureHeight, new RectInt(rect.x + 2, y, Mathf.Max(1, rect.width - 4), 2), new Color(1f, 1f, 1f, 0.08f));
            }
        }

        private static void DrawGreenBeltBand(Color[] pixels, int textureWidth, int textureHeight, RectInt rect)
        {
            FillRect(pixels, textureWidth, textureHeight, rect, new Color(0.21f, 0.45f, 0.27f, 1f));

            for (int y = rect.y + 5; y < rect.yMax - 3; y += 14)
            {
                FillRect(pixels, textureWidth, textureHeight, new RectInt(rect.x + 2, y, Mathf.Max(1, rect.width - 4), 2), new Color(1f, 1f, 1f, 0.07f));
            }
        }

        private static void DrawLaneArrow(Color[] pixels, int textureWidth, int textureHeight, int centerX, int centerY, int halfWidth, int halfHeight, bool pointUp, Color color)
        {
            int stemHalfWidth = Mathf.Max(1, halfWidth / 3);
            int stemHeight = Mathf.Max(4, halfHeight);

            if (pointUp)
            {
                FillRect(pixels, textureWidth, textureHeight, new RectInt(centerX - stemHalfWidth, centerY - stemHeight / 2, stemHalfWidth * 2, stemHeight), color);
                for (int offset = 0; offset < halfHeight; offset++)
                {
                    int lineHalfWidth = Mathf.RoundToInt((1f - (float)offset / halfHeight) * halfWidth);
                    FillRect(pixels, textureWidth, textureHeight, new RectInt(centerX - lineHalfWidth, centerY + stemHeight / 2 + offset, lineHalfWidth * 2, 1), color);
                }
            }
            else
            {
                FillRect(pixels, textureWidth, textureHeight, new RectInt(centerX - stemHalfWidth, centerY - stemHeight / 2, stemHalfWidth * 2, stemHeight), color);
                for (int offset = 0; offset < halfHeight; offset++)
                {
                    int lineHalfWidth = Mathf.RoundToInt((1f - (float)offset / halfHeight) * halfWidth);
                    FillRect(pixels, textureWidth, textureHeight, new RectInt(centerX - lineHalfWidth, centerY - stemHeight / 2 - offset, lineHalfWidth * 2, 1), color);
                }
            }
        }

        private static void DrawDashedVerticalLine(Color[] pixels, int textureWidth, int textureHeight, int x, int yMin, int yMax, Color color, int lineWidth, int dashLength, int gapLength)
        {
            for (int y = yMin; y < yMax; y += dashLength + gapLength)
            {
                FillRect(pixels, textureWidth, textureHeight, new RectInt(x, y, lineWidth, Mathf.Min(dashLength, yMax - y)), color);
            }
        }

        private static void DrawSolidVerticalLine(Color[] pixels, int textureWidth, int textureHeight, int x, int yMin, int yMax, Color color, int lineWidth)
        {
            FillRect(pixels, textureWidth, textureHeight, new RectInt(x, yMin, lineWidth, Mathf.Max(1, yMax - yMin)), color);
        }

        private static void FillRect(Color[] pixels, int textureWidth, int textureHeight, RectInt rect, Color color)
        {
            int xMin = Mathf.Clamp(rect.x, 0, textureWidth);
            int xMax = Mathf.Clamp(rect.xMax, 0, textureWidth);
            int yMin = Mathf.Clamp(rect.y, 0, textureHeight);
            int yMax = Mathf.Clamp(rect.yMax, 0, textureHeight);

            for (int y = yMin; y < yMax; y++)
            {
                int rowOffset = y * textureWidth;
                for (int x = xMin; x < xMax; x++)
                {
                    pixels[rowOffset + x] = color;
                }
            }
        }

        private static void StrokeRect(Color[] pixels, int textureWidth, int textureHeight, RectInt rect, Color color)
        {
            FillRect(pixels, textureWidth, textureHeight, new RectInt(rect.x, rect.y, rect.width, 1), color);
            FillRect(pixels, textureWidth, textureHeight, new RectInt(rect.x, rect.yMax - 1, rect.width, 1), color);
            FillRect(pixels, textureWidth, textureHeight, new RectInt(rect.x, rect.y, 1, rect.height), color);
            FillRect(pixels, textureWidth, textureHeight, new RectInt(rect.xMax - 1, rect.y, 1, rect.height), color);
        }

        private static bool IsTrainRoadType(RoadType roadType)
        {
            return roadType == RoadType.Train_A ||
                   roadType == RoadType.Train_B ||
                   roadType == RoadType.Train_C;
        }

        private static bool IsRiverRoadType(RoadType roadType)
        {
            return roadType == RoadType.River_A ||
                   roadType == RoadType.River_B;
        }

        private static bool IsPathRoadType(RoadType roadType)
        {
            return roadType == RoadType.TRoad_F ||
                   roadType == RoadType.TRoad_G;
        }

        private static string GetRoadTypeSummary(RoadType roadType)
        {
            float width = RoadDefaultsInfors.GetDefaultWidth(roadType);

            if (IsTrainRoadType(roadType))
            {
                int trackCount = Mathf.Max(1, RoadDefaultsInfors.GetDefaultLaneCounts(roadType).leftLaneCount);
                return $"{trackCount}轨道 · {width:0.#}m";
            }

            if (IsRiverRoadType(roadType))
            {
                return $"水体断面 · {width:0.#}m";
            }

            if (roadType == RoadType.Traffic_Pedestrian)
            {
                return $"行人通行面 · {width:0.#}m";
            }

            if (roadType == RoadType.Traffic_ZebraCross)
            {
                return $"斑马线编辑 · {width:0.#}m";
            }

            if (IsPathRoadType(roadType))
            {
                return $"步道断面 · {width:0.#}m";
            }

            (int leftLaneCount, int rightLaneCount) = RoadDefaultsInfors.GetDefaultLaneCounts(roadType);
            int totalLaneCount = leftLaneCount + rightLaneCount;
            bool isOneWay = RoadDefaultsInfors.GetIsOneWay(roadType);
            bool hasGreenBelt = RoadDefaultsInfors.GetDefaultGreenBeltWidth(roadType) > 0.01f;
            string direction = isOneWay ? "单向" : "双向";
            string feature = hasGreenBelt ? " · 绿化带" : string.Empty;
            return $"{direction} {totalLaneCount}车道 · {width:0.#}m{feature}";
        }

        private static bool IsPreviewRoad(LoftRoadBehaviour road)
        {
            if (road == null)
                return false;

            var marker = road.GetComponent<PreviewObjectMarker>();
            return marker != null && marker.previewType == PreviewObjectType.Road;
        }

        private static bool IsPreviewJunction(JunctionData junction)
        {
            if (junction == null)
                return false;

            var marker = junction.GetComponent<PreviewObjectMarker>();
            return marker != null && marker.previewType == PreviewObjectType.Junction;
        }

        private static LoftRoadBehaviour[] FindSceneRoads()
        {
            return GameObject.FindObjectsOfType<LoftRoadBehaviour>()
                .Where(road => road != null && !IsPreviewRoad(road))
                .ToArray();
        }

        private static List<LoftRoadBehaviour> CollectChangedRoads(LoftRoadBehaviour[] roads)
        {
            var changedRoads = new List<LoftRoadBehaviour>();
            if (roads == null)
                return changedRoads;

            foreach (var road in roads)
            {
                if (HasRoadChanged(road))
                {
                    changedRoads.Add(road);
                    UpdateRoadState(road);
                }
            }

            return changedRoads;
        }

        private static bool ShouldUpdatePreviewOnEvent(Event e)
        {
            if (e == null)
                return false;

            if (e.type == EventType.MouseUp)
                return true;

            if (e.type != EventType.MouseDrag)
                return false;

            return EditorApplication.timeSinceStartup - lastPreviewUpdateTime >= PreviewUpdateInterval;
        }

        private static bool HasPreviewObjects()
        {
            return GameObject.FindObjectsOfType<PreviewObjectMarker>()
                .Any(marker => marker != null &&
                               (marker.previewType == PreviewObjectType.Road ||
                                marker.previewType == PreviewObjectType.Junction));
        }

        private static bool TryGetSelectedPreviewMarker(out PreviewObjectMarker marker)
        {
            marker = null;
            var selected = Selection.activeGameObject;
            if (selected == null)
                return false;

            marker = selected.GetComponentInParent<PreviewObjectMarker>();
            return marker != null &&
                   (marker.previewType == PreviewObjectType.Road ||
                    marker.previewType == PreviewObjectType.Junction);
        }

        private static void ClearPreviewObjects()
        {
            var markers = GameObject.FindObjectsOfType<PreviewObjectMarker>();
            if (markers == null || markers.Length == 0)
                return;

            foreach (var marker in markers)
            {
                if (marker == null)
                    continue;

                if (marker.previewType != PreviewObjectType.Road &&
                    marker.previewType != PreviewObjectType.Junction)
                    continue;

                if (marker.gameObject != null)
                    GameObject.DestroyImmediate(marker.gameObject);
            }
        }

        private static void ApplyPreviewJunctions()
        {
            ClearPreviewObjects();
            ApplyAutomaticJunctionTopology(true);
            SceneView.RepaintAll();
        }

        private static void DrawPreviewOverlay()
        {
            if (!autoPreviewJunctions)
                return;

            if (!TryGetSelectedPreviewMarker(out _))
                return;

            const float overlayWidth = 180f;
            const float overlayHeight = 70f;
            var rect = new Rect(windowRect.xMax + 10f, windowRect.y, overlayWidth, overlayHeight);

            Handles.BeginGUI();
            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Label("路口预览");
            GUILayout.BeginHorizontal();
            GUI.enabled = HasPreviewObjects();
            if (GUILayout.Button("应用"))
            {
                ApplyPreviewJunctions();
            }
            if (GUILayout.Button("取消"))
            {
                ClearPreviewObjects();
                SceneView.RepaintAll();
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            Handles.EndGUI();
        }

        private static Dictionary<int, LoftRoadBehaviour> BuildRoadIndexMap(IEnumerable<LoftRoadBehaviour> roads)
        {
            Dictionary<int, LoftRoadBehaviour> roadIndexMap = new Dictionary<int, LoftRoadBehaviour>();
            foreach (var road in roads)
            {
                if (road == null)
                    continue;

                int roadIndex = road.GetRoadIndexInHierarchy();
                if (roadIndex >= 0)
                    roadIndexMap[roadIndex] = road;
            }

            return roadIndexMap;
        }

        private static bool TryGetRoadDataForPoint(
            IReadOnlyDictionary<int, LoftRoadBehaviour> roadIndexMap,
            RoadSamplePoint point,
            out LoftRoadExtensionData roadData)
        {
            roadData = null;
            if (point == null || roadIndexMap == null)
                return false;

            if (!roadIndexMap.TryGetValue(point.roadIndex, out var road) ||
                road == null ||
                road.RoadExtensionDatas == null ||
                point.splineIndex < 0 ||
                point.splineIndex >= road.RoadExtensionDatas.Count)
            {
                return false;
            }

            roadData = road.RoadExtensionDatas[point.splineIndex];
            return roadData != null;
        }

        private static float GetJunctionConnectionRange(
            LoftRoadExtensionData roadData,
            float rangeScale = JunctionConnectionRangeScale)
        {
            if (roadData == null)
                return 0f;

            float roadWidth = Mathf.Max(0f, roadData.WidthValue);
            float sidewalkWidth = Mathf.Max(0f, roadData.SidewalkWidthValue);
            return Mathf.Max(0.001f, (roadWidth + sidewalkWidth) * Mathf.Max(0.001f, rangeScale));
        }

        private static HashSet<(int roadInstanceId, int splineIndex)> BuildLockedJunctionSplineKeys()
        {
            HashSet<(int roadInstanceId, int splineIndex)> lockedSplineKeys =
                new HashSet<(int roadInstanceId, int splineIndex)>();

            foreach (var junction in GameObject.FindObjectsOfType<JunctionData>())
            {
                if (IsPreviewJunction(junction) || (junction != null && junction.IsImplicitAutoJunction))
                    continue;

                if (junction?.connectedRoads == null)
                    continue;

                foreach (var connectedRoad in junction.connectedRoads)
                {
                    if (connectedRoad?.roadBehaviour == null || connectedRoad.splineIndex < 0)
                        continue;

                    lockedSplineKeys.Add((connectedRoad.roadBehaviour.GetInstanceID(), connectedRoad.splineIndex));
                }
            }

            return lockedSplineKeys;
        }

        private static bool IsLockedJunctionSpline(
            LoftRoadBehaviour road,
            int splineIndex,
            ISet<(int roadInstanceId, int splineIndex)> lockedSplineKeys)
        {
            if (road == null || splineIndex < 0 || lockedSplineKeys == null || lockedSplineKeys.Count == 0)
                return false;

            return lockedSplineKeys.Contains((road.GetInstanceID(), splineIndex));
        }

        private static bool IsGroupUsingLockedJunctionSpline(
            JunctionGroup group,
            IReadOnlyDictionary<int, LoftRoadBehaviour> roadIndexMap,
            ISet<(int roadInstanceId, int splineIndex)> lockedSplineKeys)
        {
            if (group?.points == null || group.points.Count == 0 ||
                roadIndexMap == null ||
                lockedSplineKeys == null ||
                lockedSplineKeys.Count == 0)
            {
                return false;
            }

            foreach (var point in group.points)
            {
                if (point == null)
                    continue;

                if (!roadIndexMap.TryGetValue(point.roadIndex, out var road) || road == null)
                    continue;

                if (IsLockedJunctionSpline(road, point.splineIndex, lockedSplineKeys))
                    return true;
            }

            return false;
        }

        private static List<ExistingJunctionCoverage> BuildExistingJunctionCoverages()
        {
            List<ExistingJunctionCoverage> coverages = new List<ExistingJunctionCoverage>();
            foreach (var junction in GameObject.FindObjectsOfType<JunctionData>())
            {
                if (junction != null && junction.IsImplicitAutoJunction)
                    continue;

                if (TryBuildExistingJunctionCoverage(junction, out var coverage))
                    coverages.Add(coverage);
            }

            return coverages;
        }

        private static bool TryBuildExistingJunctionCoverage(
            JunctionData junction,
            out ExistingJunctionCoverage coverage)
        {
            coverage = null;
            if (junction == null || junction.IsImplicitAutoJunction || junction.connectedRoads == null || junction.connectedRoads.Count == 0)
                return false;

            coverage = new ExistingJunctionCoverage
            {
                Junction = junction,
                CenterPosition = junction.GetJunctionCenter()
            };

            float maxRadius = 0f;
            foreach (var connectedRoad in junction.connectedRoads)
            {
                if (connectedRoad?.roadBehaviour == null)
                    continue;

                coverage.ConnectedSplineKeys.Add((connectedRoad.roadBehaviour.GetInstanceID(), connectedRoad.splineIndex));

                Vector3 connectionPoint = connectedRoad.GetConnectionPoint();
                float coverageRadius = Mathf.Max(connectedRoad.width, 3f);
                if (connectedRoad.TryGetRoadData(out var roadData))
                {
                    coverageRadius = Mathf.Max(
                        coverageRadius,
                        GetJunctionConnectionRange(roadData, JunctionShapeRangeScale));
                }

                maxRadius = Mathf.Max(
                    maxRadius,
                    Vector3.Distance(coverage.CenterPosition, connectionPoint) + coverageRadius + ExistingJunctionCoveragePadding);
            }

            if (coverage.ConnectedSplineKeys.Count == 0)
            {
                coverage = null;
                return false;
            }

            coverage.Radius = Mathf.Max(maxRadius, 6f);
            return true;
        }

        private static HashSet<RoadSamplePoint> BuildExcludedSamplePointsForExistingJunctions(
            IEnumerable<ExistingJunctionCoverage> existingJunctionCoverages)
        {
            HashSet<RoadSamplePoint> excludedPoints = new HashSet<RoadSamplePoint>();
            if (existingJunctionCoverages == null)
                return excludedPoints;

            foreach (var coverage in existingJunctionCoverages)
            {
                if (coverage?.Junction?.connectedRoads == null)
                    continue;

                float sqrRadius = coverage.Radius * coverage.Radius;
                foreach (var connectedRoad in coverage.Junction.connectedRoads)
                {
                    if (connectedRoad == null || !connectedRoad.TryGetRoadData(out var roadData) || roadData.samplePoints == null)
                        continue;

                    foreach (var samplePoint in roadData.samplePoints)
                    {
                        if (samplePoint == null)
                            continue;

                        if ((ToVector3(samplePoint.position) - coverage.CenterPosition).sqrMagnitude <= sqrRadius)
                            excludedPoints.Add(samplePoint);
                    }
                }
            }

            return excludedPoints;
        }

        private static Dictionary<LoftRoadExtensionData, List<RoadSamplePoint>> BuildAnalysisCandidatePointMap(
            LoftRoadBehaviour[] roads,
            ISet<(int roadInstanceId, int splineIndex)> lockedSplineKeys)
        {
            Dictionary<LoftRoadExtensionData, List<RoadSamplePoint>> candidatePointMap =
                new Dictionary<LoftRoadExtensionData, List<RoadSamplePoint>>();

            if (roads == null)
                return candidatePointMap;

            foreach (var road in roads)
            {
                if (road?.RoadExtensionDatas == null)
                    continue;

                for (int splineIndex = 0; splineIndex < road.RoadExtensionDatas.Count; splineIndex++)
                {
                    var roadData = road.RoadExtensionDatas[splineIndex];
                    if (roadData == null)
                        continue;

                    List<RoadSamplePoint> candidatePoints = new List<RoadSamplePoint>();
                    if (!IsLockedJunctionSpline(road, splineIndex, lockedSplineKeys) &&
                        roadData.samplePoints != null)
                    {
                        foreach (var samplePoint in roadData.samplePoints)
                        {
                            if (samplePoint == null)
                                continue;

                            candidatePoints.Add(samplePoint);
                        }
                    }

                    candidatePointMap[roadData] = candidatePoints;
                }
            }

            return candidatePointMap;
        }

        private static bool IsGroupCoveredByExistingJunction(
            JunctionGroup group,
            IReadOnlyDictionary<int, LoftRoadBehaviour> roadIndexMap,
            IReadOnlyList<ExistingJunctionCoverage> existingJunctionCoverages)
        {
            if (group?.points == null || group.points.Count == 0 ||
                roadIndexMap == null ||
                existingJunctionCoverages == null ||
                existingJunctionCoverages.Count == 0)
            {
                return false;
            }

            HashSet<(int roadInstanceId, int splineIndex)> groupSplineKeys =
                new HashSet<(int roadInstanceId, int splineIndex)>();

            foreach (var point in group.points)
            {
                if (point == null)
                    continue;

                if (!roadIndexMap.TryGetValue(point.roadIndex, out var road) || road == null)
                    continue;

                groupSplineKeys.Add((road.GetInstanceID(), point.splineIndex));
            }

            if (groupSplineKeys.Count == 0)
                return false;

            foreach (var coverage in existingJunctionCoverages)
            {
                if (coverage == null)
                    continue;

                if (!groupSplineKeys.All(key => coverage.ConnectedSplineKeys.Contains(key)))
                    continue;

                float effectiveRadius = coverage.Radius + ExistingJunctionCoveragePadding;
                float sqrRadius = effectiveRadius * effectiveRadius;
                int insideCount = 0;
                foreach (var point in group.points)
                {
                    if (point == null)
                        continue;

                    if ((ToVector3(point.position) - coverage.CenterPosition).sqrMagnitude <= sqrRadius)
                        insideCount++;
                }

                int requiredInsideCount = Mathf.Max(2, Mathf.CeilToInt(group.points.Count * 0.75f));
                if (insideCount < requiredInsideCount)
                    continue;

                if ((group.centerPosition - coverage.CenterPosition).sqrMagnitude > sqrRadius)
                    continue;

                return true;
            }

            return false;
        }

        private static bool ShouldConnectSamplePoints(
            RoadSamplePoint point,
            LoftRoadExtensionData roadData,
            RoadSamplePoint otherPoint,
            LoftRoadExtensionData otherRoadData,
            float rangeScale = JunctionConnectionRangeScale)
        {
            if (point == null || otherPoint == null || roadData == null || otherRoadData == null)
                return false;

            if (point.roadIndex == otherPoint.roadIndex)
                return false;

            Vector3 dir = otherPoint.position - point.position;
            if (math.abs(dir.y) >= globalHeightThreshold)
                return false;

            float connectionRange = math.max(
                GetJunctionConnectionRange(roadData, rangeScale),
                GetJunctionConnectionRange(otherRoadData, rangeScale));

            return math.length(dir) < connectionRange;
        }

        private static List<RoadSamplePoint> BuildGroupShapePoints(
            IReadOnlyDictionary<int, LoftRoadBehaviour> roadIndexMap,
            IEnumerable<RoadSamplePoint> componentPoints)
        {
            List<RoadSamplePoint> orderedPoints = componentPoints == null
                ? new List<RoadSamplePoint>()
                : componentPoints
                    .Where(point => point != null)
                    .Distinct()
                    .OrderBy(point => point.roadIndex)
                    .ThenBy(point => point.splineIndex)
                    .ThenBy(point => point.curveU)
                    .ToList();

            if (orderedPoints.Count == 0)
                return new List<RoadSamplePoint>();

            HashSet<RoadSamplePoint> shapePoints = new HashSet<RoadSamplePoint>();
            for (int pointIndex = 0; pointIndex < orderedPoints.Count; pointIndex++)
            {
                var point = orderedPoints[pointIndex];
                if (!TryGetRoadDataForPoint(roadIndexMap, point, out var roadData))
                    continue;

                for (int otherPointIndex = pointIndex + 1; otherPointIndex < orderedPoints.Count; otherPointIndex++)
                {
                    var otherPoint = orderedPoints[otherPointIndex];
                    if (!TryGetRoadDataForPoint(roadIndexMap, otherPoint, out var otherRoadData))
                        continue;

                    if (!ShouldConnectSamplePoints(point, roadData, otherPoint, otherRoadData, JunctionShapeRangeScale))
                        continue;

                    shapePoints.Add(point);
                    shapePoints.Add(otherPoint);
                }
            }

            return orderedPoints
                .Where(shapePoints.Contains)
                .ToList();
        }

        private static List<JunctionRangePlan> BuildJunctionRangePlans(LoftRoadBehaviour[] allRoadObjects)
        {
            List<JunctionRangePlan> plans = new List<JunctionRangePlan>();
            if (junctionGroups == null || junctionGroups.Count == 0)
                return plans;

            var roadIndexMap = BuildRoadIndexMap(allRoadObjects);
            var lockedSplineKeys = BuildLockedJunctionSplineKeys();

            foreach (var group in junctionGroups)
            {
                if (group?.shapePoints == null || group.shapePoints.Count == 0)
                    continue;

                if (IsGroupUsingLockedJunctionSpline(group, roadIndexMap, lockedSplineKeys))
                    continue;

                foreach (var boundary in BuildGroupShapeSplineBoundaries(group))
                {
                    if (!roadIndexMap.TryGetValue(boundary.RoadIndex, out var road) || road == null || road.Container == null)
                        continue;

                    if (boundary.SplineIndex < 0 || boundary.SplineIndex >= road.Container.Splines.Count)
                        continue;

                    if (IsLockedJunctionSpline(road, boundary.SplineIndex, lockedSplineKeys))
                        continue;

                    if (boundary.HitCurveUs.Count == 0)
                        continue;

                    plans.Add(new JunctionRangePlan
                    {
                        Group = group,
                        Road = road,
                        SplineIndex = boundary.SplineIndex,
                        HitCurveUs = new List<float>(boundary.HitCurveUs),
                        HitPoints = new List<RoadSamplePoint>(boundary.HitPoints)
                    });
                }
            }

            return plans;
        }

        private static bool TryCollectAutomaticJunctionPlans(
            out LoftRoadBehaviour[] allRoadObjects,
            out List<JunctionRangePlan> plans)
        {
            allRoadObjects = FindSceneRoads();
            plans = new List<JunctionRangePlan>();

            if (allRoadObjects == null || allRoadObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("提示", "场景中没有找到道路对象", "确定");
                return false;
            }

            plans = BuildJunctionRangePlans(allRoadObjects);
            if (plans.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "当前没有可处理的路口范围，请先分析路口", "确定");
                return false;
            }

            return true;
        }

        private static bool IsTangentModeEditable(TangentMode tangentMode)
        {
            return tangentMode == TangentMode.Broken
                || tangentMode == TangentMode.Continuous
                || tangentMode == TangentMode.Mirrored;
        }

        private static bool HasFrozenJunctionDebugSnapshot()
        {
            return frozenJunctionDebugSnapshot != null && frozenJunctionDebugSnapshot.HasData;
        }

        private static void ClearFrozenJunctionDebugSnapshot()
        {
            frozenJunctionDebugSnapshot = null;
        }

        private static void CaptureFrozenJunctionDebugSnapshot(IEnumerable<LoftRoadBehaviour> roads)
        {
            var snapshot = new DebugJunctionVisualizationSnapshot();

            if (roads != null)
            {
                foreach (var road in roads)
                {
                    if (road?.RoadExtensionDatas == null)
                        continue;

                    foreach (var roadData in road.RoadExtensionDatas)
                    {
                        if (roadData == null)
                            continue;

                        if (roadData.samplePoints != null)
                        {
                            foreach (var point in roadData.samplePoints)
                            {
                                if (point == null || !point.isCross)
                                    continue;

                                snapshot.CrossPoints.Add(ToVector3(point.position));
                            }
                        }

                        if (roadData.connections == null)
                            continue;

                        foreach (var connection in roadData.connections)
                        {
                            if (connection?.start == null || connection.end == null)
                                continue;

                            snapshot.Connections.Add(new DebugConnectionSnapshot
                            {
                                Start = ToVector3(connection.start.position),
                                End = ToVector3(connection.end.position)
                            });
                        }
                    }
                }
            }

            if (junctionGroups != null)
            {
                foreach (var group in junctionGroups)
                {
                    if (group?.points == null || group.points.Count == 0)
                        continue;

                    var groupSnapshot = new DebugJunctionGroupSnapshot
                    {
                        CenterPosition = group.centerPosition,
                        PointCount = group.points.Count,
                        ShapePointCount = group.shapePoints?.Count ?? 0
                    };

                    groupSnapshot.OutlinePoints.AddRange(GetGroupOutlinePoints(group));
                    groupSnapshot.ShapeOutlinePoints.AddRange(GetGroupShapeOutlinePoints(group));
                    snapshot.Groups.Add(groupSnapshot);
                }
            }

            frozenJunctionDebugSnapshot = snapshot.HasData ? snapshot : null;
        }

        private static Vector3 ToVector3(float3 position)
        {
            return new Vector3(position.x, position.y, position.z);
        }

        private static bool TryResolveSplinePointTarget(
            LoftRoadBehaviour road,
            Spline spline,
            RoadSamplePoint point,
            out int existingKnotIndex,
            out int curveIndex,
            out float localT)
        {
            existingKnotIndex = -1;
            curveIndex = -1;
            localT = -1f;

            if (road == null || spline == null || point == null || spline.Count == 0)
                return false;

            if (spline.Count == 1)
            {
                existingKnotIndex = 0;
                curveIndex = 0;
                localT = 0f;
                return true;
            }

            if (point.isOriginalKnot &&
                point.originalKnotIndex >= 0 &&
                point.originalKnotIndex < spline.Count)
            {
                existingKnotIndex = point.originalKnotIndex;
                curveIndex = Mathf.Clamp(existingKnotIndex, 0, spline.Count - 2);
                localT = existingKnotIndex <= 0 ? 0f : 1f;
                return true;
            }

            float3 targetLocalPosition = road.transform.InverseTransformPoint(ToVector3(point.position));
            SplineUtility.GetNearestPoint(
                spline,
                targetLocalPosition,
                out _,
                out float splineT,
                SplineUtility.PickResolutionMax,
                4);

            curveIndex = spline.SplineToCurveT(splineT, out float resolvedLocalT);
            if (curveIndex < 0)
                return false;

            localT = Mathf.Clamp01(resolvedLocalT);
            if (localT <= 0.0001f)
            {
                existingKnotIndex = curveIndex;
                localT = 0f;
                return true;
            }

            if (localT >= 0.9999f)
            {
                existingKnotIndex = curveIndex + 1;
                localT = 1f;
                return true;
            }

            return true;
        }

        private static int InsertKnotOnCurveSegment(Spline spline, int curveIndex, float curveT)
        {
            if (spline == null || spline.Count < 2)
                return -1;

            int insertIndex = Mathf.Clamp(curveIndex + 1, 1, spline.Count - 1);
            float localT = Mathf.Clamp01(curveT);
            if (localT <= 0.0001f)
                return insertIndex - 1;
            if (localT >= 0.9999f)
                return insertIndex;

            int previousIndex = insertIndex - 1;
            var previous = spline[previousIndex];
            var next = spline[insertIndex];
            var curve = new BezierCurve(previous, next);
            CurveUtility.Split(curve, localT, out var leftCurve, out var rightCurve);

            if (spline.GetTangentMode(previousIndex) == TangentMode.Mirrored)
                spline.SetTangentMode(previousIndex, TangentMode.Continuous);
            if (spline.GetTangentMode(insertIndex) == TangentMode.Mirrored)
                spline.SetTangentMode(insertIndex, TangentMode.Continuous);

            if (IsTangentModeEditable(spline.GetTangentMode(previousIndex)))
            {
                previous.TangentOut = math.mul(math.inverse(previous.Rotation), leftCurve.Tangent0);
            }

            if (IsTangentModeEditable(spline.GetTangentMode(insertIndex)))
            {
                next.TangentIn = math.mul(math.inverse(next.Rotation), rightCurve.Tangent1);
            }

            spline.SetKnotNoNotify(previousIndex, previous);
            spline.SetKnotNoNotify(insertIndex, next);

            var up = EvaluateUpVectorLocal(
                curve,
                localT,
                math.rotate(previous.Rotation, math.up()),
                math.rotate(next.Rotation, math.up()));
            var rotation = quaternion.LookRotationSafe(math.normalizesafe(rightCurve.Tangent0), up);
            var inverseRotation = math.inverse(rotation);
            var newKnot = new BezierKnot(
                leftCurve.P3,
                math.mul(inverseRotation, leftCurve.Tangent1),
                math.mul(inverseRotation, rightCurve.Tangent0),
                rotation);

            spline.Insert(insertIndex, newKnot, TangentMode.Broken);
            return insertIndex;
        }

        private static int FindNearestKnotIndex(LoftRoadBehaviour road, Spline spline, Vector3 worldPosition)
        {
            int bestIndex = -1;
            float bestDistance = float.MaxValue;
            Vector3 localPosition = road.transform.InverseTransformPoint(worldPosition);

            for (int knotIndex = 0; knotIndex < spline.Count; knotIndex++)
            {
                float distance = Vector3.Distance(spline[knotIndex].Position, localPosition);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = knotIndex;
                }
            }

            return bestIndex;
        }

        private static string FormatVector3(Vector3 worldPosition)
        {
            return $"({worldPosition.x:F2}, {worldPosition.y:F2}, {worldPosition.z:F2})";
        }

        private static string FormatFloatList(IEnumerable<float> values)
        {
            if (values == null)
                return string.Empty;

            return string.Join(", ", values.Select(value => value.ToString("F4")));
        }

        private static string FormatIntList(IEnumerable<int> values)
        {
            if (values == null)
                return string.Empty;

            return string.Join(", ", values);
        }

        private static int GetJunctionGroupDebugIndex(JunctionGroup group)
        {
            if (group == null || junctionGroups == null)
                return -1;

            return junctionGroups.IndexOf(group);
        }

        private static bool TryGetKnotWorldPosition(
            LoftRoadBehaviour road,
            int splineIndex,
            int knotIndex,
            out Vector3 worldPosition)
        {
            worldPosition = Vector3.zero;
            if (road == null ||
                road.Container == null ||
                splineIndex < 0 ||
                splineIndex >= road.Container.Splines.Count)
            {
                return false;
            }

            var spline = road.Container.Splines[splineIndex];
            if (spline == null || knotIndex < 0 || knotIndex >= spline.Count)
                return false;

            worldPosition = road.transform.TransformPoint(spline[knotIndex].Position);
            return true;
        }

        private static JunctionGroupSplineBoundary TryGetPlanShapeBoundary(JunctionRangePlan plan)
        {
            if (plan?.Group == null || plan.Road == null)
                return null;

            int roadIndex = plan.Road.GetRoadIndexInHierarchy();
            return BuildGroupShapeSplineBoundaries(plan.Group)
                .FirstOrDefault(boundary =>
                    boundary.RoadIndex == roadIndex &&
                    boundary.SplineIndex == plan.SplineIndex);
        }

        private static string DescribeConnection(JunctionConnectionRef connection)
        {
            if (!connection.IsValid)
                return "invalid";

            string prefix = $"{connection.Road.name}[s{connection.SplineIndex} k{connection.KnotIndex}]";
            if (!TryGetKnotWorldPosition(connection.Road, connection.SplineIndex, connection.KnotIndex, out var worldPosition))
                return $"{prefix}@invalid";

            return $"{prefix}@{FormatVector3(worldPosition)}";
        }

        private static string DescribeConnection(
            (LoftRoadBehaviour road, int splineIndex, int knotIndex) connection)
        {
            if (connection.road == null)
                return "null";

            string prefix = $"{connection.road.name}[s{connection.splineIndex} k{connection.knotIndex}]";
            if (!TryGetKnotWorldPosition(connection.road, connection.splineIndex, connection.knotIndex, out var worldPosition))
                return $"{prefix}@invalid";

            return $"{prefix}@{FormatVector3(worldPosition)}";
        }

        private static void LogResolvedShapeTarget(
            JunctionRangePlan plan,
            RoadSamplePoint point,
            string resolutionType,
            int knotIndex,
            Vector3 resolvedWorldPosition,
            int curveIndex = -1,
            float localT = -1f,
            float adjustedT = -1f)
        {
            if (plan?.Road == null)
                return;

            int groupIndex = GetJunctionGroupDebugIndex(plan.Group);
            float curveU = point != null ? point.curveU : -1f;
            string sourcePositionText = point != null
                ? FormatVector3(ToVector3(point.position))
                : "n/a";

            string segmentText = curveIndex >= 0
                ? $" curveIndex={curveIndex} localT={localT:F4} adjustedT={adjustedT:F4}"
                : string.Empty;

            Debug.Log(
                $"[JunctionShapeTarget{resolutionType}] group=G{groupIndex} road={plan.Road.name} spline={plan.SplineIndex} " +
                $"curveU={curveU:F4} source={sourcePositionText} knot={knotIndex} resolved={FormatVector3(resolvedWorldPosition)}{segmentText}");
        }

        private static void LogPlanKnotResolution(JunctionRangePlan plan)
        {
            if (plan?.Road == null)
                return;

            int groupIndex = GetJunctionGroupDebugIndex(plan.Group);
            var boundary = TryGetPlanShapeBoundary(plan);
            bool hasStartWorld = TryGetKnotWorldPosition(plan.Road, plan.SplineIndex, plan.StartKnotIndex, out var startWorldPosition);
            bool hasEndWorld = TryGetKnotWorldPosition(plan.Road, plan.SplineIndex, plan.EndKnotIndex, out var endWorldPosition);

            string shapeStartText = boundary != null ? FormatVector3(boundary.StartPosition) : "n/a";
            string shapeEndText = boundary != null ? FormatVector3(boundary.EndPosition) : "n/a";
            string knotStartText = hasStartWorld ? FormatVector3(startWorldPosition) : "n/a";
            string knotEndText = hasEndWorld ? FormatVector3(endWorldPosition) : "n/a";

            float distSS = boundary != null && hasStartWorld ? Vector3.Distance(boundary.StartPosition, startWorldPosition) : -1f;
            float distSE = boundary != null && hasEndWorld ? Vector3.Distance(boundary.StartPosition, endWorldPosition) : -1f;
            float distES = boundary != null && hasStartWorld ? Vector3.Distance(boundary.EndPosition, startWorldPosition) : -1f;
            float distEE = boundary != null && hasEndWorld ? Vector3.Distance(boundary.EndPosition, endWorldPosition) : -1f;

            Debug.Log(
                $"[JunctionShapeKnotRange] group=G{groupIndex} road={plan.Road.name} spline={plan.SplineIndex} " +
                $"curveUs=[{FormatFloatList(plan.HitCurveUs)}] hitKnots=[{FormatIntList(plan.HitKnotIndices)}] " +
                $"shapeStart={shapeStartText} shapeEnd={shapeEndText} " +
                $"startKnot={plan.StartKnotIndex}@{knotStartText} endKnot={plan.EndKnotIndex}@{knotEndText} " +
                $"distSS={distSS:F3} distSE={distSE:F3} distES={distES:F3} distEE={distEE:F3}");
        }

        private static void LogPlanSplitResolution(JunctionRangePlan plan)
        {
            if (plan?.Road == null)
                return;

            Debug.Log(
                $"[JunctionShapeSplitRef] group=G{GetJunctionGroupDebugIndex(plan.Group)} road={plan.Road.name} sourceSpline={plan.SplineIndex} " +
                $"startKnot={plan.StartKnotIndex} endKnot={plan.EndKnotIndex} " +
                $"startRef={DescribeConnection(plan.StartConnection)} endRef={DescribeConnection(plan.EndConnection)}");
        }

        private static void LogCreateJunctionInput(
            JunctionGroup group,
            List<JunctionRangePlan> groupedPlans,
            List<(LoftRoadBehaviour road, int splineIndex, int knotIndex)> connections)
        {
            if (groupedPlans == null || connections == null)
                return;

            int groupIndex = GetJunctionGroupDebugIndex(group);
            Debug.Log(
                $"[JunctionCreateInput] group=G{groupIndex} shapePoints={group?.shapePoints?.Count ?? 0} " +
                $"plans={groupedPlans.Count} connections={connections.Count}");

            foreach (var plan in groupedPlans)
            {
                Debug.Log(
                    $"[JunctionCreateInputPlan] group=G{groupIndex} road={plan.Road?.name ?? "null"} sourceSpline={plan.SplineIndex} " +
                    $"startRef={DescribeConnection(plan.StartConnection)} endRef={DescribeConnection(plan.EndConnection)}");
            }

            for (int connectionIndex = 0; connectionIndex < connections.Count; connectionIndex++)
            {
                Debug.Log(
                    $"[JunctionCreateInputConn] group=G{groupIndex} index={connectionIndex} {DescribeConnection(connections[connectionIndex])}");
            }
        }

        private const int kNormalsPerCurve = 16;
        private const float kEpsilon = 0.0001f;

        private struct FrenetFrame
        {
            public float3 origin;
            public float3 tangent;
            public float3 normal;
            public float3 binormal;
        }

        private static bool Approximately(float a, float b)
        {
            return math.abs(b - a) < math.max(0.000001f * math.max(math.abs(a), math.abs(b)), kEpsilon * 8);
        }

        private static float3 GetExplicitLinearTangent(float3 point, float3 to)
        {
            return (to - point) / 3f;
        }

        private static FrenetFrame GetNextRotationMinimizingFrame(BezierCurve curve, FrenetFrame previousFrame, float nextT)
        {
            FrenetFrame nextFrame;
            nextFrame.origin = CurveUtility.EvaluatePosition(curve, nextT);
            nextFrame.tangent = CurveUtility.EvaluateTangent(curve, nextT);

            float3 toCurrentFrame = nextFrame.origin - previousFrame.origin;
            float c1 = math.dot(toCurrentFrame, toCurrentFrame);
            float3 riL = previousFrame.binormal - toCurrentFrame * 2f / c1 * math.dot(toCurrentFrame, previousFrame.binormal);
            float3 tiL = previousFrame.tangent - toCurrentFrame * 2f / c1 * math.dot(toCurrentFrame, previousFrame.tangent);

            float3 v2 = nextFrame.tangent - tiL;
            float c2 = math.dot(v2, v2);

            nextFrame.binormal = math.normalize(riL - v2 * 2f / c2 * math.dot(v2, riL));
            nextFrame.normal = math.normalize(math.cross(nextFrame.binormal, nextFrame.tangent));
            return nextFrame;
        }

        private static float3 EvaluateUpVectorLocal(BezierCurve curve, float t, float3 startUp, float3 endUp)
        {
            var linearTangentLen = math.length(GetExplicitLinearTangent(curve.P0, curve.P3));
            var linearTangentOut = math.normalize(curve.P3 - curve.P0) * linearTangentLen;
            if (Approximately(math.length(curve.P1 - curve.P0), 0f))
                curve.P1 = curve.P0 + linearTangentOut;
            if (Approximately(math.length(curve.P2 - curve.P3), 0f))
                curve.P2 = curve.P3 - linearTangentOut;

            var normalBuffer = new float3[kNormalsPerCurve];

            FrenetFrame frame;
            frame.origin = curve.P0;
            frame.tangent = curve.P1 - curve.P0;
            frame.normal = startUp;
            frame.binormal = math.normalize(math.cross(frame.tangent, frame.normal));
            if (float.IsNaN(frame.binormal.x))
                return float3.zero;

            normalBuffer[0] = frame.normal;

            float stepSize = 1f / (kNormalsPerCurve - 1);
            float currentT = stepSize;
            float prevT = 0f;
            float3 upVector = float3.zero;
            FrenetFrame prevFrame;
            for (int i = 1; i < kNormalsPerCurve; ++i)
            {
                prevFrame = frame;
                frame = GetNextRotationMinimizingFrame(curve, prevFrame, currentT);
                normalBuffer[i] = frame.normal;

                if (prevT <= t && currentT >= t)
                {
                    float lerpT = (t - prevT) / stepSize;
                    upVector = (float3)Vector3.Slerp(prevFrame.normal, frame.normal, lerpT);
                }

                prevT = currentT;
                currentT += stepSize;
            }

            if (prevT <= t && currentT >= t)
                upVector = endUp;

            float3 lastFrameNormal = normalBuffer[kNormalsPerCurve - 1];
            float angleBetweenNormals = math.acos(math.clamp(math.dot(lastFrameNormal, endUp), -1f, 1f));
            if (angleBetweenNormals == 0f)
                return upVector;

            float3 lastNormalTangent = math.normalize(frame.tangent);
            quaternion positiveRotation = quaternion.AxisAngle(lastNormalTangent, angleBetweenNormals);
            quaternion negativeRotation = quaternion.AxisAngle(lastNormalTangent, -angleBetweenNormals);
            float positiveRotationResult = math.acos(math.clamp(math.dot(math.rotate(positiveRotation, endUp), lastFrameNormal), -1f, 1f));
            float negativeRotationResult = math.acos(math.clamp(math.dot(math.rotate(negativeRotation, endUp), lastFrameNormal), -1f, 1f));
            if (positiveRotationResult > negativeRotationResult)
                angleBetweenNormals *= -1f;

            currentT = stepSize;
            prevT = 0f;
            for (int i = 1; i < normalBuffer.Length; i++)
            {
                float3 normal = normalBuffer[i];
                float adjustmentAngle = math.lerp(0f, angleBetweenNormals, currentT);
                float3 tangent = math.normalize(CurveUtility.EvaluateTangent(curve, currentT));
                float3 adjustedNormal = math.rotate(quaternion.AxisAngle(tangent, -adjustmentAngle), normal);
                normalBuffer[i] = adjustedNormal;

                if (prevT <= t && currentT >= t)
                {
                    float lerpT = (t - prevT) / stepSize;
                    upVector = (float3)Vector3.Slerp(normalBuffer[i - 1], normalBuffer[i], lerpT);
                    return upVector;
                }

                prevT = currentT;
                currentT += stepSize;
            }

            return endUp;
        }

        private static LoftRoadExtensionData CloneRoadExtensionData(LoftRoadExtensionData source)
        {
            var clone = new LoftRoadExtensionData();
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(source), clone);
            clone.samplePoints = new List<RoadSamplePoint>();
            clone.orderedJunctionPoints = new List<RoadSamplePoint>();
            clone.connections = new List<RoadConnection>();
            return clone;
        }

        private static void EnsureRoadDataSlot(LoftRoadBehaviour road, int splineIndex, LoftRoadExtensionData sourceTemplate)
        {
            while (road.RoadExtensionDatas.Count <= splineIndex)
                road.RoadExtensionDatas.Add(CloneRoadExtensionData(sourceTemplate));
        }

        private static int InsertBoundaryKnots(List<JunctionRangePlan> plans, bool recordUndo = true)
        {
            int insertedKnots = 0;
            var plansBySpline = plans
                .GroupBy(plan => (plan.Road, plan.SplineIndex))
                .ToList();

            foreach (var splinePlan in plansBySpline)
            {
                var road = splinePlan.Key.Road;
                int splineIndex = splinePlan.Key.SplineIndex;
                if (road == null || road.Container == null || splineIndex < 0 || splineIndex >= road.Container.Splines.Count)
                    continue;

                var spline = road.Container.Splines[splineIndex];
                if (recordUndo)
                    Undo.RecordObject(road.Container, "Insert Junction Boundary Knots");

                var targetsByCurve = new Dictionary<int, List<(RoadSamplePoint point, float localT)>>();
                var resolvedTargetPositions = new Dictionary<RoadSamplePoint, Vector3>();
                foreach (var plan in splinePlan)
                {
                    if (plan?.HitPoints == null)
                        continue;

                    foreach (var point in plan.HitPoints)
                    {
                        if (!TryResolveSplinePointTarget(road, spline, point, out var existingKnotIndex, out var curveIndex, out var localT))
                            continue;

                        if (existingKnotIndex >= 0)
                        {
                            Vector3 existingWorldPosition = road.transform.TransformPoint(spline[existingKnotIndex].Position);
                            resolvedTargetPositions[point] = existingWorldPosition;
                            LogResolvedShapeTarget(plan, point, "Existing", existingKnotIndex, existingWorldPosition);
                            continue;
                        }

                        if (localT <= 0.0001f || localT >= 0.9999f)
                            continue;

                        if (!targetsByCurve.TryGetValue(curveIndex, out var localTs))
                        {
                            localTs = new List<(RoadSamplePoint point, float localT)>();
                            targetsByCurve[curveIndex] = localTs;
                        }

                        if (!localTs.Any(existing =>
                                ReferenceEquals(existing.point, point) ||
                                Mathf.Abs(existing.localT - localT) < 0.0001f))
                        {
                            localTs.Add((point, localT));
                        }
                    }
                }

                foreach (var curveEntry in targetsByCurve.OrderByDescending(entry => entry.Key))
                {
                    float previousT = 1f;
                    foreach (var target in curveEntry.Value.OrderByDescending(value => value.localT))
                    {
                        float adjustedT = Mathf.Clamp01(target.localT / previousT);
                        int insertedIndex = InsertKnotOnCurveSegment(spline, curveEntry.Key, adjustedT);
                        if (insertedIndex >= 0 && insertedIndex < spline.Count)
                        {
                            Vector3 insertedWorldPosition = road.transform.TransformPoint(spline[insertedIndex].Position);
                            resolvedTargetPositions[target.point] = insertedWorldPosition;
                            foreach (var plan in splinePlan.Where(plan => plan.HitPoints != null && plan.HitPoints.Contains(target.point)))
                            {
                                LogResolvedShapeTarget(
                                    plan,
                                    target.point,
                                    "Inserted",
                                    insertedIndex,
                                    insertedWorldPosition,
                                    curveEntry.Key,
                                    target.localT,
                                    adjustedT);
                            }
                        }
                        insertedKnots++;
                        previousT = target.localT;
                    }
                }

                foreach (var plan in splinePlan)
                {
                    plan.HitKnotIndices.Clear();
                    plan.StartKnotIndex = -1;
                    plan.EndKnotIndex = -1;
                    plan.StartConnection = default;
                    plan.EndConnection = default;
                    if (plan?.HitPoints == null)
                    {
                        LogPlanKnotResolution(plan);
                        continue;
                    }

                    foreach (var point in plan.HitPoints)
                    {
                        if (point == null ||
                            !resolvedTargetPositions.TryGetValue(point, out var resolvedWorldPosition))
                        {
                            continue;
                        }

                        int knotIndex = FindNearestKnotIndex(road, spline, resolvedWorldPosition);
                        if (knotIndex >= 0 && !plan.HitKnotIndices.Contains(knotIndex))
                            plan.HitKnotIndices.Add(knotIndex);
                    }

                    plan.HitKnotIndices.Sort();
                    if (plan.HitKnotIndices.Count == 0)
                    {
                        LogPlanKnotResolution(plan);
                        continue;
                    }

                    plan.StartKnotIndex = plan.HitKnotIndices[0];
                    plan.EndKnotIndex = plan.HitKnotIndices[plan.HitKnotIndices.Count - 1];
                    LogPlanKnotResolution(plan);
                }

                if (recordUndo)
                    EditorUtility.SetDirty(road.Container);
            }

            return insertedKnots;
        }

        private static int SplitRoadsAtJunctionRanges(List<JunctionRangePlan> plans, bool recordUndo = true)
        {
            int splitSegmentCount = 0;
            var plansByRoad = plans
                .GroupBy(plan => plan.Road)
                .ToList();

            foreach (var roadPlan in plansByRoad)
            {
                var road = roadPlan.Key;
                if (road == null || road.Container == null || road.RoadExtensionDatas == null)
                    continue;

                foreach (var splinePlan in roadPlan.GroupBy(plan => plan.SplineIndex).OrderByDescending(group => group.Key))
                {
                    var splinePlans = splinePlan.ToList();
                    int splineIndex = splinePlan.Key;
                    if (splineIndex < 0 || splineIndex >= road.Container.Splines.Count || splineIndex >= road.RoadExtensionDatas.Count)
                        continue;

                    var spline = road.Container.Splines[splineIndex];
                    if (spline == null || spline.Count < 2)
                        continue;

                    var sourceRoadData = road.RoadExtensionDatas[splineIndex];
                    var ranges = splinePlans
                        .Select(plan =>
                        {
                            int startIndex = Mathf.Clamp(plan.StartKnotIndex, 0, spline.Count - 1);
                            int endIndex = Mathf.Clamp(plan.EndKnotIndex, startIndex, spline.Count - 1);
                            return new Vector2Int(startIndex, endIndex);
                        })
                        .Where(range => range.y > range.x)
                        .OrderBy(range => range.x)
                        .ToList();

                    if (ranges.Count == 0)
                        continue;

                    List<Vector2Int> mergedRanges = new List<Vector2Int>();
                    foreach (var range in ranges)
                    {
                        if (mergedRanges.Count == 0)
                        {
                            mergedRanges.Add(range);
                            continue;
                        }

                        var lastRange = mergedRanges[mergedRanges.Count - 1];
                        if (range.x <= lastRange.y)
                            mergedRanges[mergedRanges.Count - 1] = new Vector2Int(lastRange.x, Mathf.Max(lastRange.y, range.y));
                        else
                            mergedRanges.Add(range);
                    }

                    List<Vector2Int> keepRanges = new List<Vector2Int>();
                    int segmentStart = 0;
                    foreach (var range in mergedRanges)
                    {
                        if (range.x - segmentStart >= 1)
                            keepRanges.Add(new Vector2Int(segmentStart, range.x));

                        segmentStart = range.y;
                    }

                    if (spline.Count - 1 - segmentStart >= 1)
                        keepRanges.Add(new Vector2Int(segmentStart, spline.Count - 1));

                    if (keepRanges.Count == 0)
                        continue;

                    if (recordUndo)
                    {
                        Undo.RecordObject(road.Container, "Split Road At Junction");
                        Undo.RecordObject(road, "Split Road At Junction");
                    }

                    foreach (var keepRange in keepRanges)
                    {
                        road.Container.DuplicateSpline(
                            new SplineKnotIndex(splineIndex, keepRange.x),
                            new SplineKnotIndex(splineIndex, keepRange.y),
                            out int newSplineIndex);
                        EnsureRoadDataSlot(road, newSplineIndex, sourceRoadData);
                        road.RoadExtensionDatas[newSplineIndex] = CloneRoadExtensionData(sourceRoadData);

                        var duplicatedSpline = road.Container.Splines[newSplineIndex];
                        int duplicatedLastKnotIndex = duplicatedSpline != null ? duplicatedSpline.Count - 1 : -1;
                        foreach (var plan in splinePlans)
                        {
                            if (plan.StartKnotIndex == keepRange.y && duplicatedLastKnotIndex >= 0)
                            {
                                plan.StartConnection = new JunctionConnectionRef
                                {
                                    Road = road,
                                    SplineIndex = newSplineIndex,
                                    KnotIndex = duplicatedLastKnotIndex
                                };
                            }

                            if (plan.EndKnotIndex == keepRange.x)
                            {
                                plan.EndConnection = new JunctionConnectionRef
                                {
                                    Road = road,
                                    SplineIndex = newSplineIndex,
                                    KnotIndex = 0
                                };
                            }
                        }

                        splitSegmentCount++;
                    }

                    road.Container.RemoveSplineAt(splineIndex);
                    foreach (var plan in splinePlans)
                    {
                        plan.StartConnection = AdjustConnectionAfterSplineRemoval(plan.StartConnection, road, splineIndex);
                        plan.EndConnection = AdjustConnectionAfterSplineRemoval(plan.EndConnection, road, splineIndex);
                        LogPlanSplitResolution(plan);
                    }
                    if (recordUndo)
                    {
                        EditorUtility.SetDirty(road);
                        EditorUtility.SetDirty(road.Container);
                    }
                }
            }

            return splitSegmentCount;
        }

        private static JunctionConnectionRef AdjustConnectionAfterSplineRemoval(
            JunctionConnectionRef connection,
            LoftRoadBehaviour road,
            int removedSplineIndex)
        {
            if (!connection.IsValid || connection.Road != road)
                return connection;

            if (connection.SplineIndex == removedSplineIndex)
                return default;

            if (connection.SplineIndex > removedSplineIndex)
                connection.SplineIndex--;

            return connection;
        }

        private static void SortConnectionsClockwise(List<(LoftRoadBehaviour road, int splineIndex, int knotIndex)> connections)
        {
            if (connections == null || connections.Count < 2)
                return;

            List<((LoftRoadBehaviour road, int splineIndex, int knotIndex) connection, Vector3 worldPosition)> positionedConnections =
                new List<((LoftRoadBehaviour road, int splineIndex, int knotIndex) connection, Vector3 worldPosition)>();

            foreach (var connection in connections)
            {
                if (TryGetConnectionWorldPosition(connection, out var worldPosition))
                    positionedConnections.Add((connection, worldPosition));
            }

            if (positionedConnections.Count < 2)
                return;

            Vector3 center = Vector3.zero;
            foreach (var entry in positionedConnections)
            {
                center += entry.worldPosition;
            }

            center /= positionedConnections.Count;
            positionedConnections.Sort((left, right) =>
            {
                return GetClockwiseAngleXZ(right.worldPosition, center)
                    .CompareTo(GetClockwiseAngleXZ(left.worldPosition, center));
            });

            connections.Clear();
            foreach (var entry in positionedConnections)
            {
                connections.Add(entry.connection);
            }
        }

        private static bool TryGetConnectionWorldPosition(
            (LoftRoadBehaviour road, int splineIndex, int knotIndex) connection,
            out Vector3 worldPosition)
        {
            worldPosition = Vector3.zero;
            if (connection.road == null ||
                connection.road.Container == null ||
                connection.splineIndex < 0 ||
                connection.splineIndex >= connection.road.Container.Splines.Count)
            {
                return false;
            }

            var spline = connection.road.Container.Splines[connection.splineIndex];
            if (spline == null || connection.knotIndex < 0 || connection.knotIndex >= spline.Count)
                return false;

            worldPosition = connection.road.transform.TransformPoint(spline[connection.knotIndex].Position);
            return true;
        }

        private static float GetClockwiseAngleXZ(Vector3 point, Vector3 center)
        {
            return Mathf.Atan2(point.z - center.z, point.x - center.x);
        }

        private static List<Vector3> SortWorldPositionsClockwise(IEnumerable<Vector3> positions)
        {
            List<Vector3> orderedPoints = new List<Vector3>();
            if (positions == null)
                return orderedPoints;

            foreach (var position in positions)
            {
                if (!orderedPoints.Any(existing => Vector3.Distance(existing, position) <= 0.01f))
                    orderedPoints.Add(position);
            }

            if (orderedPoints.Count < 2)
                return orderedPoints;

            Vector3 center = Vector3.zero;
            foreach (var point in orderedPoints)
            {
                center += point;
            }

            center /= orderedPoints.Count;
            orderedPoints.Sort((left, right) =>
                GetClockwiseAngleXZ(right, center).CompareTo(GetClockwiseAngleXZ(left, center)));
            return orderedPoints;
        }

        private static List<JunctionBoundaryTarget> BuildGroupBoundaryTargets(JunctionGroup group)
        {
            return BuildBoundaryTargets(BuildGroupSplineBoundaries(group));
        }

        private static List<JunctionBoundaryTarget> BuildGroupShapeBoundaryTargets(JunctionGroup group)
        {
            return BuildBoundaryTargets(BuildGroupShapeSplineBoundaries(group));
        }

        private static List<JunctionBoundaryTarget> BuildBoundaryTargets(
            IEnumerable<JunctionGroupSplineBoundary> boundaries)
        {
            List<JunctionBoundaryTarget> targets = new List<JunctionBoundaryTarget>();
            if (boundaries == null)
                return targets;

            foreach (var boundary in boundaries)
            {
                AddBoundaryTarget(targets, boundary.RoadIndex, boundary.SplineIndex, boundary.StartPosition);
                AddBoundaryTarget(targets, boundary.RoadIndex, boundary.SplineIndex, boundary.EndPosition);
            }

            return targets;
        }

        private static List<JunctionGroupSplineBoundary> BuildGroupSplineBoundaries(JunctionGroup group)
        {
            return BuildGroupSplineBoundaries(group?.points);
        }

        private static List<JunctionGroupSplineBoundary> BuildGroupShapeSplineBoundaries(JunctionGroup group)
        {
            return BuildGroupSplineBoundaries(group?.shapePoints);
        }

        private static List<JunctionGroupSplineBoundary> BuildGroupSplineBoundaries(
            IEnumerable<RoadSamplePoint> sourcePoints)
        {
            List<JunctionGroupSplineBoundary> boundaries = new List<JunctionGroupSplineBoundary>();
            if (sourcePoints == null)
                return boundaries;

            foreach (var splinePoints in sourcePoints
                         .Where(point => point != null)
                         .GroupBy(point => (point.roadIndex, point.splineIndex)))
            {
                var orderedPoints = splinePoints
                    .OrderBy(point => point.curveU)
                    .ToList();

                if (orderedPoints.Count == 0)
                    continue;

                List<float> uniqueCurveUs = new List<float>();
                List<RoadSamplePoint> uniquePoints = new List<RoadSamplePoint>();
                List<Vector3> uniqueWorldPositions = new List<Vector3>();
                foreach (var point in orderedPoints)
                {
                    float clampedU = Mathf.Clamp01(point.curveU);
                    if (uniqueCurveUs.Count > 0 &&
                        Mathf.Abs(uniqueCurveUs[uniqueCurveUs.Count - 1] - clampedU) < 0.0001f)
                    {
                        continue;
                    }

                    uniqueCurveUs.Add(clampedU);
                    uniquePoints.Add(point);
                    uniqueWorldPositions.Add(new Vector3(point.position.x, point.position.y, point.position.z));
                }

                if (uniqueCurveUs.Count == 0)
                    continue;

                boundaries.Add(new JunctionGroupSplineBoundary
                {
                    RoadIndex = splinePoints.Key.roadIndex,
                    SplineIndex = splinePoints.Key.splineIndex,
                    HitCurveUs = uniqueCurveUs,
                    HitPoints = uniquePoints,
                    StartPosition = uniqueWorldPositions[0],
                    EndPosition = uniqueWorldPositions[uniqueWorldPositions.Count - 1]
                });
            }

            return boundaries;
        }

        private static void AddBoundaryTarget(
            List<JunctionBoundaryTarget> targets,
            int roadIndex,
            int splineIndex,
            Vector3 worldPosition)
        {
            bool alreadyExists = targets.Any(existing =>
                existing.RoadIndex == roadIndex &&
                existing.SplineIndex == splineIndex &&
                Vector3.Distance(existing.Position, worldPosition) <= 0.01f);

            if (alreadyExists)
                return;

            targets.Add(new JunctionBoundaryTarget
            {
                RoadIndex = roadIndex,
                SplineIndex = splineIndex,
                Position = worldPosition
            });
        }

        private static void AddPlanConnection(
            List<(LoftRoadBehaviour road, int splineIndex, int knotIndex)> connections,
            HashSet<(int roadInstanceId, int splineIndex, int knotIndex)> usedKeys,
            JunctionConnectionRef connection)
        {
            if (!connection.IsValid)
                return;

            var key = (connection.Road.GetInstanceID(), connection.SplineIndex, connection.KnotIndex);
            if (!usedKeys.Add(key))
                return;

            connections.Add((connection.Road, connection.SplineIndex, connection.KnotIndex));
        }

        private static int CreateJunctionObjectsFromPlans(List<JunctionRangePlan> plans)
        {
            int createdCount = 0;
            var existingJunctions = GameObject.FindObjectsOfType<JunctionData>()
                .Where(junction => junction != null && !junction.IsImplicitAutoJunction)
                .ToList();
            if (plans == null || plans.Count == 0)
                return 0;

            foreach (var groupedPlans in plans
                         .Where(plan => plan?.Group != null)
                         .GroupBy(plan => plan.Group))
            {
                var group = groupedPlans.Key;
                var groupedPlanList = groupedPlans.ToList();
                if (group?.shapePoints == null || group.shapePoints.Count < 2)
                    continue;

                float detectionRadius = Mathf.Max(group.radius + 2f, 5f);
                bool hasExistingJunction = existingJunctions.Any(junction =>
                    junction != null &&
                    Vector3.Distance(junction.transform.position, group.centerPosition) <= detectionRadius * 0.35f);

                if (hasExistingJunction)
                    continue;

                List<(LoftRoadBehaviour road, int splineIndex, int knotIndex)> connections =
                    new List<(LoftRoadBehaviour road, int splineIndex, int knotIndex)>();
                HashSet<(int roadInstanceId, int splineIndex, int knotIndex)> usedKeys =
                    new HashSet<(int roadInstanceId, int splineIndex, int knotIndex)>();
                foreach (var plan in groupedPlanList)
                {
                    AddPlanConnection(connections, usedKeys, plan.StartConnection);
                    AddPlanConnection(connections, usedKeys, plan.EndConnection);
                }

                if (connections.Count < 2)
                    continue;

                SortConnectionsClockwise(connections);
                LogCreateJunctionInput(group, groupedPlanList, connections);

                var junctionObject = JunctionGenerator.CreateJunction(connections);
                if (junctionObject != null)
                {
                    var junctionData = junctionObject.GetComponent<JunctionData>();
                    if (junctionData != null)
                        existingJunctions.Add(junctionData);
                    createdCount++;
                }
            }

            if (createdCount > 0)
            {
                junctionGroups.Clear();
                SceneView.RepaintAll();
            }

            return createdCount;
        }

        private static void RebuildRoadsAfterTopologyChange(LoftRoadBehaviour[] allRoadObjects)
        {
            foreach (var road in allRoadObjects)
            {
                if (road == null)
                    continue;

                road.CollectSamplePoints();
                road.LoftAllRoads();
                EditorUtility.SetDirty(road);
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            SceneView.RepaintAll();
        }

        private static void CreateBoundaryKnots()
        {
            try
            {
                if (!TryCollectAutomaticJunctionPlans(out var allRoadObjects, out var plans))
                    return;

                int insertedKnots = InsertBoundaryKnots(plans);
                Debug.Log($"======= 路口边界 Knot 创建完成，共插入 {insertedKnots} 个边界 Knot =======");
                if (insertedKnots > 0)
                {
                    RebuildRoadsAfterTopologyChange(allRoadObjects);
                    AssetDatabase.SaveAssets();
                }
                SceneView.RepaintAll();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"创建路口组Knot时出错: {ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog("错误", $"创建路口组Knot时出错: {ex.Message}", "确定");
            }
        }

        private static void ApplyAutomaticJunctionTopology(bool createJunctionObjects)
        {
            if (!TryCollectAutomaticJunctionPlans(out var allRoadObjects, out var plans))
                return;

            int insertedKnots = InsertBoundaryKnots(plans);
            int splitSegments = SplitRoadsAtJunctionRanges(plans);
            RebuildRoadsAfterTopologyChange(allRoadObjects);

            int createdJunctions = 0;
            if (createJunctionObjects)
                createdJunctions = CreateJunctionObjectsFromPlans(plans);

            AssetDatabase.SaveAssets();
            Debug.Log(
                $"自动路口拓扑完成: boundary_knots={insertedKnots}, split_segments={splitSegments}, created_junctions={createdJunctions}");
        }

        private static void SetBridgeKnot(bool enable)
        {
            var selectedObj = Selection.activeGameObject;
            var splineContainer = selectedObj.GetComponentInChildren<SplineContainer>();
            if (splineContainer != null)
            {
                LoftRoadBehaviour loftRoadBehaviour = selectedObj.GetComponentInChildren<LoftRoadBehaviour>();
                if (loftRoadBehaviour != null)
                {
                    List<SplineInfo> splineInfos = splineContainer.Splines
                        .Select((spline, index) => new SplineInfo(splineContainer, index))
                        .ToList();
                    foreach (var splineInfo in splineInfos)
                    {
                        var elements = new List<ISelectableElement>();
                        SplineSelection.GetElements<ISelectableElement>(new List<SplineInfo> { splineInfo }, elements);
                        if (enable)
                            loftRoadBehaviour.RoadExtensionDatas[0].AddBridgePair(elements);
                        else
                            loftRoadBehaviour.RoadExtensionDatas[0].RemoveBridgePair(elements);
                    }
                }
            }
        }

        private static void SwitchRoadMarkersStatus()
        {
            var selectedObj = Selection.activeGameObject;
            var loftRoadBehaviours = selectedObj.GetComponentsInChildren<LoftRoadBehaviour>();
            foreach (var loftRoadBehaviour in loftRoadBehaviours)
            {
                loftRoadBehaviour.SwitchRoadMarkersStatus();
            }
        }

        private static void ExportGreenBeltMeshesToOBJ()
        {
            string directoryPath = "Assets/ArtResources/Hdas/RoadSystem/Caches/BeltInput";
            if (!System.IO.Directory.Exists(directoryPath))
            {
                System.IO.Directory.CreateDirectory(directoryPath);
            }

            var roadDataRoot = GameObject.Find("Road_Data");
            if (roadDataRoot == null)
            {
                Debug.LogError("找不到名为 'Road_Data' 的根对象。");
                return;
            }

            var loftRoadBehaviours = roadDataRoot.GetComponentsInChildren<LoftRoadBehaviour>();
            List<MeshFilter> greenBeltMeshFilters = new List<MeshFilter>();

            foreach (var loftRoadBehaviour in loftRoadBehaviours)
            {
                var meshFilter = loftRoadBehaviour.GetComponent<MeshFilter>();
                if (meshFilter == null)
                {
                    Debug.LogWarning($"对象 {loftRoadBehaviour.gameObject.name} 没有 MeshFilter 组件。");
                    continue;
                }

                foreach (var roadData in loftRoadBehaviour.RoadExtensionDatas)
                {
                    if (roadData.roadTypeEnum == RoadType.GreenBelt_Define)
                    {
                        greenBeltMeshFilters.Add(meshFilter);
                    }
                }
            }

            if (greenBeltMeshFilters.Count > 0)
            {
                string objPath = $"{directoryPath}/CombinedGreenBelt.obj";
                using (System.IO.StreamWriter sw = new System.IO.StreamWriter(objPath))
                {
                    sw.Write(MeshesToOBJ(greenBeltMeshFilters));
                }
                Debug.Log($"所有网格已导出为 OBJ 格式并保存到 {objPath}");
            }
            else
            {
                Debug.LogWarning("没有找到需要导出的 GreenBelt_Define 网格。");
            }
        }

        private static string MeshesToOBJ(List<MeshFilter> meshFilters)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            int vertexOffset = 0;
            foreach (MeshFilter meshFilter in meshFilters)
            {
                Mesh mesh = meshFilter.sharedMesh;
                if (mesh == null) continue;

                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                Vector2[] uvs = mesh.uv;

                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 worldVertex = meshFilter.transform.TransformPoint(vertices[i]);
                    sb.AppendLine($"v {-worldVertex.x} {worldVertex.y} {worldVertex.z}"); // X axis inverted
                }
                sb.AppendLine();

                for (int i = 0; i < normals.Length; i++)
                {
                    Vector3 worldNormal = meshFilter.transform.TransformDirection(normals[i]);
                    sb.AppendLine($"vn {-worldNormal.x} {worldNormal.y} {worldNormal.z}"); // Normals X axis inverted
                }
                sb.AppendLine();

                for (int i = 0; i < uvs.Length; i++)
                {
                    sb.AppendLine($"vt {uvs[i].x} {uvs[i].y}");
                }
                sb.AppendLine();

                // 获取三角形索引
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    int[] indices = mesh.GetTriangles(submesh);
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        sb.AppendLine($"f {indices[i] + 1 + vertexOffset}/{indices[i] + 1 + vertexOffset}/{indices[i] + 1 + vertexOffset} " +
                                      $"{indices[i + 1] + 1 + vertexOffset}/{indices[i + 1] + 1 + vertexOffset}/{indices[i + 1] + 1 + vertexOffset} " +
                                      $"{indices[i + 2] + 1 + vertexOffset}/{indices[i + 2] + 1 + vertexOffset}/{indices[i + 2] + 1 + vertexOffset}");
                    }
                }

                vertexOffset += vertices.Length;
            }

            return sb.ToString();
        }

        private static RoadInfo CustomRoadInfoPopup(string label, RoadInfo selectedInfo)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label);

            if (GUILayout.Button(selectedInfo != null ? selectedInfo.roadName : "选择自定义道路", "Popup"))
            {
                GenericMenu menu = new GenericMenu();
                foreach (var roadInfo in customRoadInfos)
                {
                    RoadInfo localCopy = roadInfo;
                    menu.AddItem(new GUIContent(localCopy.roadName), localCopy == selectedInfo, () =>
                    {
                        selectedCustomRoadInfo = localCopy;
                        selectedRoadType = RoadType.Road_A; // 默认选择一个基础的 RoadType。
                    });
                }
                menu.ShowAsContext();
            }

            GUILayout.EndHorizontal();
            return selectedInfo;
        }

        private static void LoadAllCustomRoadInfos()
        {
            customRoadInfos = AssetDatabase.FindAssets("t:RoadInfo")
                .Select(guid => AssetDatabase.LoadAssetAtPath<RoadInfo>(AssetDatabase.GUIDToAssetPath(guid)))
                .ToList();
        }

        private static float GetDefaultWidth()
        {
            if (selectedCustomRoadInfo != null)
            {
                return selectedCustomRoadInfo.defaultWidth;
            }
            return RoadDefaultsInfors.GetDefaultWidth(selectedRoadType);
        }

        private static void SetDefaultWidth()
        {
            // 单独留空，按需实现宽度设置
        }
        private static void GenerateRegion()
        {
            var roadDataRoot = GameObject.Find("Road_Data");
            if (roadDataRoot == null)
            {
                roadDataRoot = new GameObject("Road_Data");
                Undo.RegisterCreatedObjectUndo(roadDataRoot, "创建 Road_Data");

            }

            EditorApplication.ExecuteMenuItem("GameObject/Spline/Draw Splines Tool...");

            EditorApplication.delayCall += () =>
            {
                var roadGO = Selection.activeGameObject;
                if (roadGO == null || !roadGO.GetComponent<SplineContainer>())
                {
                    Debug.LogError("创建样条线失败。");
                    return;
                }

                Selection.activeGameObject = roadGO;
            };
        }
        private static void GenerateRoad(RoadType roadType, float width, RoadInfo customRoadInfo = null)
        {
            var roadDataRoot = GameObject.Find("Road_Data");
            if (roadDataRoot == null)
            {
                roadDataRoot = new GameObject("Road_Data");
                Undo.RegisterCreatedObjectUndo(roadDataRoot, "创建 Road_Data");

                EditorApplication.ExecuteMenuItem("GameObject/Spline/Circle");
                var avoidErrorGO = Selection.activeGameObject;

                if (avoidErrorGO != null && avoidErrorGO.GetComponent<SplineContainer>())
                {
                    avoidErrorGO.name = "Avoid_Error";
                    avoidErrorGO.transform.SetParent(roadDataRoot.transform);
                }
            }

            EditorApplication.ExecuteMenuItem("GameObject/Spline/Draw Splines Tool...");

            EditorApplication.delayCall += () =>
            {
                var roadGO = Selection.activeGameObject;
                if (roadGO == null || !roadGO.GetComponent<SplineContainer>())
                {
                    Debug.LogError("创建样条线失败。");
                    return;
                }

                roadGO.name = customRoadInfo != null ? customRoadInfo.roadName : $"{roadType}";
                roadGO.transform.SetParent(roadDataRoot.transform);

                var loftRoadBehaviour = roadGO.AddComponent<LoftRoadBehaviour>();

                var roadData = new LoftRoadExtensionData
                {
                    roadTypeEnum = roadType,
                    customRoadInfo = customRoadInfo,
                    width = new SplineData<float> { DefaultValue = width },

                    leftLaneCount = RoadDefaultsInfors.GetDefaultLaneCounts(roadType).leftLaneCount,
                    rightLaneCount = RoadDefaultsInfors.GetDefaultLaneCounts(roadType).rightLaneCount,

                    laneWidth = new SplineData<float> { DefaultValue = RoadDefaultsInfors.GetDefaultLaneWidth(roadType) },
                    leftSidewalkWidth = new SplineData<float> { DefaultValue = RoadDefaultsInfors.GetDefaultSidewalkWidth(roadType) },
                    rightSidewalkWidth = new SplineData<float> { DefaultValue = RoadDefaultsInfors.GetDefaultRightSidewalkWidth(roadType) },
                    greenBeltWidth = new SplineData<float> { DefaultValue = RoadDefaultsInfors.GetDefaultGreenBeltWidth(roadType) },
                };

                if (customRoadInfo != null)
                {
                    roadData.width.DefaultValue = customRoadInfo.defaultWidth;
                    roadData.laneWidth.DefaultValue = customRoadInfo.defaultLaneWidth;
                    roadData.greenBeltWidth.DefaultValue = customRoadInfo.defaultGreenBeltWidth;
                }

                loftRoadBehaviour.RoadExtensionDatas.Clear();
                loftRoadBehaviour.RoadExtensionDatas.Add(roadData);

                string materialPath = RoadDefaultsInfors.GetMaterialPath(roadType, customRoadInfo);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    Debug.LogError($"找不到材料路径为 {materialPath} 的材质。");
                    return;
                }

                var meshRenderer = roadGO.GetComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = material;

                roadData.InitializeMaterials();

                loftRoadBehaviour.LoftAllRoads();
                Selection.activeGameObject = roadGO;
            };
        }

        private static void ReplaceRoadType(RoadType newRoadType, float newWidth, RoadInfo customRoadInfo = null)
        {
            var selectedGameObjects = Selection.gameObjects;
            foreach (var selectedGameObject in selectedGameObjects)
            {
                var loftRoadBehaviour = selectedGameObject.GetComponent<LoftRoadBehaviour>();
                if (loftRoadBehaviour == null)
                {
                    Debug.LogError($"选定的对象 {selectedGameObject.name} 上没有 LoftRoadBehaviour 组件。");
                    continue;
                }

                foreach (var roadData in loftRoadBehaviour.RoadExtensionDatas)
                {
                    roadData.roadTypeEnum = newRoadType;
                    roadData.customRoadInfo = customRoadInfo;
                    roadData.SetWidthValue(newWidth);

                    if (customRoadInfo != null)
                    {
                        roadData.SetLaneWidthValue(customRoadInfo.defaultLaneWidth);
                        roadData.SetSidewalkWidthValue(customRoadInfo.defaultSidewalkWidth);
                        roadData.SetGreenBeltWidthValue(customRoadInfo.defaultGreenBeltWidth);
                    }
                    else
                    {
                        roadData.leftLaneCount = RoadDefaultsInfors.GetDefaultLaneCounts(newRoadType).leftLaneCount;
                        roadData.rightLaneCount = RoadDefaultsInfors.GetDefaultLaneCounts(newRoadType).rightLaneCount;
                        roadData.SetLaneWidthValue(RoadDefaultsInfors.GetDefaultLaneWidth(newRoadType));
                        roadData.SetSidewalkWidthValue(RoadDefaultsInfors.GetDefaultSidewalkWidth(newRoadType));
                        roadData.SetGreenBeltWidthValue(RoadDefaultsInfors.GetDefaultGreenBeltWidth(newRoadType));
                    }
                }

                string materialPath = RoadDefaultsInfors.GetMaterialPath(newRoadType, customRoadInfo);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    Debug.LogError($"找不到材料路径为 {materialPath} 的材质。");
                    continue;
                }

                var meshRenderer = selectedGameObject.GetComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = material;

                selectedGameObject.name = customRoadInfo != null ? customRoadInfo.roadName : $"{newRoadType}";

                loftRoadBehaviour.LoftAllRoads();

                Debug.Log($"已替换选定对象 {selectedGameObject.name} 的道路类型为 {newRoadType}，并设置宽度为 {newWidth}");
            }
        }

        private static void SnapToGround()
        {
            var selectedGameObjects = Selection.gameObjects;
            foreach (var selectedGameObject in selectedGameObjects)
            {
                var splineContainer = selectedGameObject.GetComponent<SplineContainer>();
                if (splineContainer == null)
                {
                    Debug.LogError($"选定的对象 {selectedGameObject.name} 上没有 SplineContainer 组件。");
                    continue;
                }

                foreach (var spline in splineContainer.Splines)
                {
                    for (int i = 0; i < spline.Count; i++)
                    {
                        var knot = spline[i];

                        Ray ray = new Ray(selectedGameObject.transform.TransformPoint(knot.Position) + Vector3.up * 1000, Vector3.down);
                        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity))
                        {
                            knot.Position = selectedGameObject.transform.InverseTransformPoint(hit.point);
                            spline[i] = knot;
                        }
                    }
                }

                Debug.Log($"已将选定对象 {selectedGameObject.name} 的样条线点贴附到地面。");
            }

            SceneView.RepaintAll();
        }

        private static void RecalculateRoadMeshInfo()
        {
            var obj = Selection.activeGameObject;
            if (obj == null)
            {
                Debug.LogError("请选择一个物体");
                return;
            }

            var splines = obj.GetComponentsInChildren<SplineContainer>();
            var filters = new List<MeshFilter>();
            foreach (var spline in splines)
            {
                MeshCollider collider = spline.gameObject.GetComponent<MeshCollider>();
                if (collider == null) collider = spline.gameObject.AddComponent<MeshCollider>();
                var filter = spline.gameObject.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null && filter.sharedMesh.vertices != null && filter.sharedMesh.vertexCount > 0)
                    collider.sharedMesh = filter.sharedMesh;

                filters.Add(filter);
            }

            for (int i = 0; i < filters.Count; i++)
            {
                if (filters[i] == null) continue;
                EditorUtility.DisplayProgressBar("Recalculate Mesh Info", $"Recalculating {filters[i].name}...", (float)i / filters.Count);
                filters[i].sharedMesh.RecalculateNormals();
                filters[i].sharedMesh.RecalculateTangents();
                filters[i].sharedMesh.RecalculateBounds();
            }
            EditorUtility.ClearProgressBar();
        }

        public static void AlignRoadMarksToRoadSurface()
        {
            var obj = Selection.activeGameObject;
            if (obj == null)
            {
                Debug.LogError("请选择一个物体");
                return;
            }

            var splines = obj.GetComponentsInChildren<SplineContainer>();
            foreach (var spline in splines)
            {
                MeshCollider collider = spline.gameObject.GetComponent<MeshCollider>();
                if (collider == null) collider = spline.gameObject.AddComponent<MeshCollider>();
                var filter = spline.gameObject.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null && filter.sharedMesh.vertices != null && filter.sharedMesh.vertexCount > 0)
                    collider.sharedMesh = filter.sharedMesh;

                foreach (Transform child in spline.gameObject.transform)
                {
                    if (child.gameObject.name == "RoadMarkings")
                    {
                        foreach (Transform mark in child)
                        {
                            Ray ray = new(mark.position + Vector3.up * 1000, Vector3.down);
                            int layerMask = LayerMask.GetMask("Grounds");
                            if (Physics.Raycast(ray, out RaycastHit hit, 2000f, layerMask))
                            {
                                mark.position = new Vector3(mark.position.x, hit.point.y + 0.01f, mark.position.z);
                                Vector3 forward = Vector3.ProjectOnPlane(mark.transform.forward, hit.normal).normalized;
                                Quaternion lookRotation = Quaternion.LookRotation(forward, hit.normal);
                                mark.transform.rotation = lookRotation;
                            }
                        }
                    }
                }
            }
        }

        private static void CleanUpSplines()
        {
            var roadDataRootNode = GameObject.Find("Road_Data_Root");
            if (roadDataRootNode == null)
            {
                Debug.LogError("找不到名为 'Road_Data_Root' 的根对象。");
                return;
            }

            var roadDataRoot = roadDataRootNode.transform.Find("Road_Data");
            if (roadDataRoot == null)
            {
                Debug.LogError("Road_Data_Root下找不到名为 'Road_Data' 的对象。");
                return;
            }

            var splineContainers = roadDataRoot.GetComponentsInChildren<SplineContainer>();
            var gameObjectsToDelete = new List<GameObject>();

            foreach (var splineContainer in splineContainers)
            {
                bool shouldDeleteGameObject = false;

                for (int i = splineContainer.Splines.Count - 1; i >= 0; i--)
                {
                    var spline = splineContainer.Splines[i];
                    if (spline.Count <= 1)
                    {
                        splineContainer.RemoveSpline(spline);
                        Debug.Log($"已删除样条线 {i}，因为它只有一个节点或节点列表为空。");
                    }
                }

                if (splineContainer.Splines.Count == 0)
                {
                    shouldDeleteGameObject = true;
                }

                if (shouldDeleteGameObject)
                {
                    gameObjectsToDelete.Add(splineContainer.gameObject);
                    Debug.Log($"标记 GameObject {splineContainer.gameObject.name} 以进行删除，因为它的样条线容器为空或只有一个节点。");
                }
            }

            foreach (var gameObject in gameObjectsToDelete)
            {
                Undo.DestroyObjectImmediate(gameObject);
            }

            SceneView.RepaintAll();
        }

        private static void UpdateRoadParameters()
        {
            var roadDataRoot = GameObject.Find("Road_Data");
            if (roadDataRoot == null)
            {
                Debug.LogError("找不到名为 'Road_Data' 的根对象。");
                return;
            }

            var loftRoadBehaviours = roadDataRoot.GetComponentsInChildren<LoftRoadBehaviour>();
            foreach (var loftRoadBehaviour in loftRoadBehaviours)
            {
                var gameObjectName = loftRoadBehaviour.gameObject.name;
                Debug.Log($"处理 GameObject: {gameObjectName}");

                RoadInfo matchedCustomRoadInfo = customRoadInfos.FirstOrDefault(info => info.roadName == gameObjectName);

                if (Enum.TryParse<RoadType>(gameObjectName, out var roadTypeEnum) || matchedCustomRoadInfo != null)
                {
                    Debug.Log($"解析名称 {gameObjectName} 成功，RoadTypeEnum: {roadTypeEnum}");

                    foreach (var roadData in loftRoadBehaviour.RoadExtensionDatas)
                    {
                        roadData.roadTypeEnum = roadTypeEnum;
                        roadData.customRoadInfo = matchedCustomRoadInfo;

                        if (matchedCustomRoadInfo != null)
                        {
                            roadData.width.DefaultValue = matchedCustomRoadInfo.defaultWidth;
                            roadData.laneWidth.DefaultValue = matchedCustomRoadInfo.defaultLaneWidth;
                            roadData.leftSidewalkWidth.DefaultValue = matchedCustomRoadInfo.defaultSidewalkWidth / 2;
                            roadData.rightSidewalkWidth.DefaultValue = matchedCustomRoadInfo.defaultSidewalkWidth / 2;
                            roadData.greenBeltWidth.DefaultValue = matchedCustomRoadInfo.defaultGreenBeltWidth;
                        }
                        else
                        {
                            roadData.leftLaneCount = RoadDefaultsInfors.GetDefaultLaneCounts(roadTypeEnum).leftLaneCount;
                            roadData.rightLaneCount = RoadDefaultsInfors.GetDefaultLaneCounts(roadTypeEnum).rightLaneCount;
                            roadData.width.DefaultValue = RoadDefaultsInfors.GetDefaultWidth(roadTypeEnum);
                            roadData.laneWidth.DefaultValue = RoadDefaultsInfors.GetDefaultLaneWidth(roadTypeEnum);
                            roadData.leftSidewalkWidth.DefaultValue = RoadDefaultsInfors.GetDefaultSidewalkWidth(roadTypeEnum);
                            roadData.rightSidewalkWidth.DefaultValue = RoadDefaultsInfors.GetDefaultRightSidewalkWidth(roadTypeEnum);
                            roadData.greenBeltWidth.DefaultValue = RoadDefaultsInfors.GetDefaultGreenBeltWidth(roadTypeEnum);
                        }
                        Debug.Log($"更新 {loftRoadBehaviour.gameObject.name}: " +
                                  $"RoadTypeEnum = {roadTypeEnum}, " +
                                  $"宽度 = {roadData.width.DefaultValue}, " +
                                  $"车道宽度 = {roadData.laneWidth.DefaultValue}, " +
                                  $"左车道数量={roadData.leftLaneCount}, " +
                                  $"右车道数量 = {roadData.rightLaneCount}, " +
                                  $"左侧人行道宽度 = {roadData.leftSidewalkWidth.DefaultValue}, " +
                                  $"右侧人行道宽度 = {roadData.rightSidewalkWidth.DefaultValue}, " +
                                  $"绿化带宽度 = {roadData.greenBeltWidth.DefaultValue}");
                    }
                    loftRoadBehaviour.LoftAllRoads();
                }
                else
                {
                    Debug.LogError($"GameObject 名称 {gameObjectName} 无法解析为 RoadTypeEnum 或 RoadInfo。请确保命名与道路类型枚举或自定义道路类型匹配。");
                }
            }
        }

        private static void UploadToRoadHDA()
        {
            Debug.Log("调用上传到道路HDA功能。请在此实现上传逻辑。");
        }

        private static void SaveRoadDataAsPrefabWithCleanupAndUpdate()
        {
            CleanUpSplines();
            UpdateRoadParameters();
            SaveRoadDataAsPrefab();
        }

        private static void SaveRoadDataAsPrefab()
        {
            var roadDataRoot = GameObject.Find("Road_Data");
            if (roadDataRoot == null)
            {
                Debug.LogError("找不到名为 'Road_Data' 的根对象。");
                return;
            }

            GameObject roadDataRootNode = GameObject.Find("Road_Data_Root");
            if (roadDataRootNode == null)
            {
                roadDataRootNode = new GameObject("Road_Data_Root");
            }

            roadDataRoot.transform.SetParent(roadDataRootNode.transform);

            string path = EditorUtility.SaveFilePanelInProject("保存道路信息", "Road_Data_Root", "prefab", "请选择保存位置", "Assets/ArtResources/Hdas/RoadSystem/Caches");
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("保存提醒小助手:你刚取消了保存道路信息，记得保存！！");

                roadDataRoot.transform.SetParent(null);
                if (roadDataRootNode.transform.childCount == 0)
                {
                    UnityEngine.Object.DestroyImmediate(roadDataRootNode);
                }

                return;
            }

            PrefabUtility.SaveAsPrefabAssetAndConnect(roadDataRootNode, path, InteractionMode.UserAction);
            Debug.Log($"保存道路信息到 {path}");

            // 恢复原有的层级结构
            roadDataRoot.transform.SetParent(null);
            if (roadDataRootNode.transform.childCount == 0)
            {
                UnityEngine.Object.DestroyImmediate(roadDataRootNode);
            }
        }

        private static void LoadRoadDataFromPrefabWithCleanupAndUpdate()
        {
            LoadRoadDataFromPrefab();
            CleanUpSplines();
            UpdateRoadParameters();
        }

        private static void LoadRoadDataFromPrefab()
        {
            string path = EditorUtility.OpenFilePanel("加载道路信息", "Assets/ArtResources/Hdas/RoadSystem/Caches", "prefab");
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("加载提醒小助手: 你刚取消了加载道路信息，记得加载！！");
                return;
            }

            path = FileUtil.GetProjectRelativePath(path);

            if (!System.IO.File.Exists(path))
            {
                Debug.LogError($"找不到路径为 {path} 的预制件。");
                return;
            }

            var existingRootNode = GameObject.Find("Road_Data_Root");
            if (existingRootNode != null)
            {
                Debug.LogWarning("场景中已存在名为 'Road_Data_Root' 的对象。请先删除或另存当前对象再加载。");
                return;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Debug.Log($"从 {path} 加载道路信息");
        }

        private static void ExportRoadDataToJson()
        {
            var roadDataRoot = GameObject.Find("Road_Data");
            if (roadDataRoot == null)
            {
                Debug.LogError("找不到名为 'Road_Data' 的根对象。");
                return;
            }

            var splineContainers = roadDataRoot.GetComponentsInChildren<SplineContainer>();
            if (splineContainers.Length == 0)
            {
                Debug.LogError("找不到任何样条线容器。");
                return;
            }

            SplineDataContainer splineDataContainer = new SplineDataContainer
            {
                splines = new List<Spline>()
            };

            foreach (var container in splineContainers)
            {
                splineDataContainer.splines.AddRange(container.Splines);
            }

            splineDataContainer.roadDatas = new List<LoftRoadExtensionData>();
            var loftRoadBehaviours = roadDataRoot.GetComponentsInChildren<LoftRoadBehaviour>();
            foreach (var behaviour in loftRoadBehaviours)
            {
                Debug.Log($"正在处理对象：{behaviour.gameObject.name}");

                foreach (var roadData in behaviour.RoadExtensionDatas)
                {
                    Debug.Log("开始导出道路数据。");
                    LoftRoadExtensionData worldRoadData = new LoftRoadExtensionData
                    {
                        roadTypeEnum = roadData.roadTypeEnum,
                        customRoadInfo = roadData.customRoadInfo,
                        roadType = roadData.roadType,
                        greenBelt = roadData.greenBelt,
                        width = roadData.width,
                        laneWidth = roadData.laneWidth,
                        leftSidewalkWidth = roadData.leftSidewalkWidth,
                        rightSidewalkWidth = roadData.rightSidewalkWidth,
                        greenBeltWidth = roadData.greenBeltWidth,
                        sidewalkMaterial = roadData.sidewalkMaterial,
                        greenBeltMaterial = roadData.greenBeltMaterial,
                        leftLaneCount = roadData.leftLaneCount,
                        rightLaneCount = roadData.rightLaneCount,
                        isOneWay = roadData.isOneWay,
                        inOffset = roadData.inOffset,
                        outOffset = roadData.outOffset,
                        points = new List<RoadPointData>()
                    };

                    Debug.Log("正在获取世界坐标点。");
                    var worldPoints = roadData.GetWorldPoints(behaviour.transform);
                    foreach (var point in worldPoints)
                    {
                        worldRoadData.points.Add(point);
                    }

                    Debug.Log($"完成导出 {behaviour.gameObject.name} 的道路数据，并添加到列表中。");
                    splineDataContainer.roadDatas.Add(worldRoadData);
                }
            }

            string json = JsonUtility.ToJson(splineDataContainer, true);
            string path = EditorUtility.SaveFilePanel("导出道路信息 (JSON)", "", "Road_Data", "json");
            if (string.IsNullOrEmpty(path))
                return;

            System.IO.File.WriteAllText(path, json);
            Debug.Log($"导出道路信息到 {path}");
        }

        private static void UpdateAvailableConnections()
        {
            availableConnections.Clear();
            var roads = FindSceneRoads();
            foreach(var road in roads)
            {
                for(int splineIndex = 0; splineIndex < road.Container.Splines.Count; splineIndex++)
                {
                    var spline = road.Container.Splines[splineIndex];
                    for(int knotIndex = 0; knotIndex < spline.Count; knotIndex++)
                    {
                        // 检查这个节点是否已经被选中
                        bool isSelected = selectedConnections.Exists(x => 
                            x.road == road && 
                            x.splineIndex == splineIndex && 
                            x.knotIndex == knotIndex);
                            
                        if(!isSelected)
                        {
                            availableConnections.Add((road, splineIndex, knotIndex));
                        }
                    }
                }
            }
        }

        private static void SyncDeletedRoadGizmoState(LoftRoadBehaviour[] roads)
        {
            HashSet<int> currentRoadIds = new HashSet<int>(
                roads
                    .Where(road => road != null)
                    .Select(road => road.GetInstanceID()));

            bool roadsDeleted = lastKnownRoadInstanceIds.Any(instanceId => !currentRoadIds.Contains(instanceId));

            selectedConnections.RemoveAll(connection =>
                connection.road == null ||
                !currentRoadIds.Contains(connection.road.GetInstanceID()) ||
                connection.road.Container == null ||
                connection.splineIndex < 0 ||
                connection.splineIndex >= connection.road.Container.Splines.Count);

            availableConnections.RemoveAll(connection =>
                connection.road == null ||
                !currentRoadIds.Contains(connection.road.GetInstanceID()) ||
                connection.road.Container == null ||
                connection.splineIndex < 0 ||
                connection.splineIndex >= connection.road.Container.Splines.Count);

            var staleRoads = lastRoadState.Keys
                .Where(road => road == null || !currentRoadIds.Contains(road.GetInstanceID()))
                .ToList();
            foreach (var staleRoad in staleRoads)
            {
                lastRoadState.Remove(staleRoad);
            }

            if (roadsDeleted)
            {
                junctionGroups.Clear();
                availableConnections.Clear();
                ClearFrozenJunctionDebugSnapshot();
            }

            lastKnownRoadInstanceIds = currentRoadIds;
        }

        private static (LoftRoadBehaviour road, int splineIndex, int knotIndex)? FindClosestKnot(SceneView sceneView, Vector2 mousePosition)
        {
            if (sceneView == null) return null;
            
            var mouseRay = HandleUtility.GUIPointToWorldRay(mousePosition);
            float minDistance = float.MaxValue;
            (LoftRoadBehaviour road, int splineIndex, int knotIndex)? closestKnot = null;
            
            try
            {
                var roads = FindSceneRoads();
                if (roads == null || roads.Length == 0) return null;
                
                foreach(var road in roads)
                {
                    if (road == null || road.Container == null) continue;
                    
                    for(int splineIndex = 0; splineIndex < road.Container.Splines.Count; splineIndex++)
                    {
                        var spline = road.Container.Splines[splineIndex];
                        if (spline == null || spline.Count <= 0) continue;
                        
                        // 只检查首尾端点
                        int[] endPoints = spline.Count == 1
                            ? new int[] { 0 }
                            : new int[] { 0, spline.Count - 1 };
                        foreach(int knotIndex in endPoints)
                        {
                            try
                            {
                                var knot = spline[knotIndex];
                                if (road == null) continue;
                                
                                var worldPos = road.transform.TransformPoint(knot.Position);
                                float distance = HandleUtility.DistancePointLine(worldPos, 
                                    mouseRay.origin, mouseRay.origin + mouseRay.direction * 100000);
                                    
                                if(distance < minDistance && distance < 5f)
                                {
                                    minDistance = distance;
                                    closestKnot = (road, splineIndex, knotIndex);
                                }
                            }
                            catch (System.Exception ex)
                            {
                                Debug.LogWarning($"处理节点时出错: {ex.Message}");
                                continue;
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"查找最近节点时出错: {ex.Message}");
                return null;
            }
            
            return closestKnot;
        }

        private static void SortConnectionsClockwise()
        {
            if (selectedConnections.Count < 2)
                return;

            SortConnectionsClockwise(selectedConnections);
        }

        private static void AnalyzeJunctions(LoftRoadBehaviour[] roads)
        {
            Debug.Log("开始分析路口点...");

            var lockedSplineKeys = BuildLockedJunctionSplineKeys();
            var analysisCandidatePointMap = BuildAnalysisCandidatePointMap(roads, lockedSplineKeys);
            int analysisCandidatePointCount = analysisCandidatePointMap.Values.Sum(points => points.Count);
            int lockedSplineCount = 0;
            if (roads != null)
            {
                foreach (var road in roads)
                {
                    if (road?.RoadExtensionDatas == null)
                        continue;

                    for (int splineIndex = 0; splineIndex < road.RoadExtensionDatas.Count; splineIndex++)
                    {
                        if (IsLockedJunctionSpline(road, splineIndex, lockedSplineKeys))
                            lockedSplineCount++;
                    }
                }
            }

            if (lockedSplineCount > 0)
            {
                Debug.Log(
                    $"已有路口锁定生效：锁定 spline {lockedSplineCount} 条，" +
                    $"剩余参与分析采样点 {analysisCandidatePointCount} 个");
            }

            foreach (var road in roads)
            {
                if (road?.RoadExtensionDatas == null)
                    continue;

                foreach (var roadData in road.RoadExtensionDatas)
                {
                    if (roadData == null)
                        continue;

                    if (roadData.samplePoints != null)
                    {
                        foreach (var point in roadData.samplePoints)
                        {
                            if (point != null)
                                point.isCross = false;
                        }
                    }

                    roadData.connections.Clear();
                }
            }

            for (int roadIndex = 0; roadIndex < roads.Length; roadIndex++)
            {
                var road = roads[roadIndex];
                if (road?.RoadExtensionDatas == null)
                    continue;

                for (int otherRoadIndex = roadIndex + 1; otherRoadIndex < roads.Length; otherRoadIndex++)
                {
                    var otherRoad = roads[otherRoadIndex];
                    if (otherRoad?.RoadExtensionDatas == null)
                        continue;

                    foreach (var roadData in road.RoadExtensionDatas)
                    {
                        if (roadData == null)
                            continue;

                        foreach (var otherRoadData in otherRoad.RoadExtensionDatas)
                        {
                            if (otherRoadData == null)
                                continue;

                            if (!analysisCandidatePointMap.TryGetValue(roadData, out var candidatePoints) ||
                                !analysisCandidatePointMap.TryGetValue(otherRoadData, out var otherCandidatePoints))
                            {
                                continue;
                            }

                            CheckCrossPoints(candidatePoints, roadData, otherCandidatePoints, otherRoadData);
                            CheckCrossPoints(otherCandidatePoints, otherRoadData, candidatePoints, roadData);
                        }
                    }
                }
            }

            // 标记需要更新
            foreach (var road in roads)
            {
                EditorUtility.SetDirty(road);
            }

            Debug.Log("路口点分析完成");
        }

        private static void AutoAnalyzeAndCreateJunctions()
        {
            try
            {
                Debug.Log("======= 开始自动分析路口 =======");
                ClearFrozenJunctionDebugSnapshot();
                ClearPreviewObjects();
                
                // 更新所有道路的采样点
                LoftRoadBehaviour.UpdateAllRoadsSamplePoints();
                
                // 查找场景中所有的道路对象
                var allRoadObjects = FindSceneRoads();
                if (allRoadObjects == null || allRoadObjects.Length == 0)
                {
                    Debug.LogError("场景中没有找到任何道路对象");
                    return;
                }
                
                Debug.Log($"场景中共找到 {allRoadObjects.Length} 个道路对象");
                
                // 分析路口
                AnalyzeJunctions(allRoadObjects);
                
                // 对每个道路，生成有序点列表
                foreach (var road in allRoadObjects)
                {
                    if (road.RoadExtensionDatas == null) continue;
                    
                    for (int splineIndex = 0; splineIndex < road.RoadExtensionDatas.Count; splineIndex++)
                    {
                        var roadData = road.RoadExtensionDatas[splineIndex];
                        if (roadData == null) continue;
                        
                        // 清空之前的有序列表
                        roadData.orderedJunctionPoints.Clear();
                        
                        // 保持原有顺序，只过滤需要的点
                        var orderedPoints = roadData.samplePoints
                            .Where(p => p.isOriginalKnot || p.isCross)
                            .ToList();
                        
                        roadData.orderedJunctionPoints.AddRange(orderedPoints);
                        
                        Debug.Log($"道路 {road.name} 样条 {splineIndex} 生成有序点列表: " +
                                $"原始knot点 {orderedPoints.Count(p => p.isOriginalKnot)} 个, " +
                                $"路口点 {orderedPoints.Count(p => p.isCross)} 个");
                    }
                }
                
                // 创建路口组
                GroupJunctionPoints(allRoadObjects.ToList());
                CaptureFrozenJunctionDebugSnapshot(allRoadObjects);
                SceneView.RepaintAll();
                
                // 分析完成后显示结果并询问是否直接切断道路并生成路口
                string resultMessage = $"分析完成！\n找到 {junctionGroups.Count} 个路口组。";
                if (EditorUtility.DisplayDialog("分析完成", resultMessage + "\n是否自动切断相交道路并生成路口？", "是", "否"))
                {
                    ApplyAutomaticJunctionTopology(true);
                }
                
                Debug.Log("======= 路口分析完成 =======");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"分析路口时出错: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void GroupJunctionPoints(List<LoftRoadBehaviour> roads)
        {
            Debug.Log("开始路口分组处理...");
            junctionGroups.Clear();
            var roadIndexMap = BuildRoadIndexMap(roads);
            var lockedSplineKeys = BuildLockedJunctionSplineKeys();
            HashSet<RoadSamplePoint> allCrossPoints = new HashSet<RoadSamplePoint>();
            Dictionary<RoadSamplePoint, HashSet<RoadSamplePoint>> adjacency =
                new Dictionary<RoadSamplePoint, HashSet<RoadSamplePoint>>();

            foreach (var road in roads)
            {
                if (road?.RoadExtensionDatas == null)
                    continue;

                foreach (var roadData in road.RoadExtensionDatas)
                {
                    if (roadData == null)
                        continue;

                    foreach (var point in roadData.samplePoints)
                    {
                        if (point == null || !point.isCross)
                            continue;

                        allCrossPoints.Add(point);
                        if (!adjacency.ContainsKey(point))
                            adjacency[point] = new HashSet<RoadSamplePoint>();
                    }

                    foreach (var connection in roadData.connections)
                    {
                        if (connection?.start == null || connection.end == null)
                            continue;

                        if (!connection.start.isCross || !connection.end.isCross)
                            continue;

                        allCrossPoints.Add(connection.start);
                        allCrossPoints.Add(connection.end);

                        if (!adjacency.TryGetValue(connection.start, out var startNeighbors))
                        {
                            startNeighbors = new HashSet<RoadSamplePoint>();
                            adjacency[connection.start] = startNeighbors;
                        }

                        if (!adjacency.TryGetValue(connection.end, out var endNeighbors))
                        {
                            endNeighbors = new HashSet<RoadSamplePoint>();
                            adjacency[connection.end] = endNeighbors;
                        }

                        startNeighbors.Add(connection.end);
                        endNeighbors.Add(connection.start);
                    }
                }
            }

            Debug.Log($"总共收集到 {allCrossPoints.Count} 个交叉点");

            HashSet<RoadSamplePoint> visited = new HashSet<RoadSamplePoint>();
            int groupCount = 0;
            int skippedLockedSplineGroupCount = 0;

            foreach (var seedPoint in allCrossPoints)
            {
                if (seedPoint == null || visited.Contains(seedPoint))
                    continue;

                Queue<RoadSamplePoint> queue = new Queue<RoadSamplePoint>();
                List<RoadSamplePoint> componentPoints = new List<RoadSamplePoint>();

                queue.Enqueue(seedPoint);
                visited.Add(seedPoint);

                while (queue.Count > 0)
                {
                    var currentPoint = queue.Dequeue();
                    componentPoints.Add(currentPoint);

                    if (!adjacency.TryGetValue(currentPoint, out var neighbors))
                        continue;

                    foreach (var neighbor in neighbors)
                    {
                        if (neighbor == null || visited.Contains(neighbor))
                            continue;

                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }

                var distinctRoads = componentPoints
                    .Select(point => point.roadIndex)
                    .Distinct()
                    .ToList();

                if (distinctRoads.Count < 2)
                    continue;

                componentPoints = componentPoints
                    .Distinct()
                    .OrderBy(point => point.roadIndex)
                    .ThenBy(point => point.splineIndex)
                    .ThenBy(point => point.curveU)
                    .ToList();

                var newGroup = new JunctionGroup();
                newGroup.points.AddRange(componentPoints);
                newGroup.shapePoints.AddRange(BuildGroupShapePoints(roadIndexMap, componentPoints));

                float3 center = float3.zero;
                foreach (var point in newGroup.points)
                {
                    center += point.position;
                }

                center /= newGroup.points.Count;
                newGroup.centerPosition = new Vector3(center.x, center.y, center.z);

                float maxDistance = 0f;
                foreach (var point in newGroup.points)
                {
                    float distance = math.length(point.position - center);
                    maxDistance = math.max(maxDistance, distance);
                }

                newGroup.radius = maxDistance;

                if (IsGroupUsingLockedJunctionSpline(newGroup, roadIndexMap, lockedSplineKeys))
                {
                    skippedLockedSplineGroupCount++;
                    continue;
                }

                junctionGroups.Add(newGroup);
                groupCount++;
            }

            Debug.Log($"分组处理完成：\n" +
                      $"初始交叉点数量: {allCrossPoints.Count}\n" +
                      $"跳过锁定 spline 路口组数量: {skippedLockedSplineGroupCount}\n" +
                      $"创建的组数量: {groupCount}\n" +
                      $"最终组数量: {junctionGroups.Count}\n" +
                      $"每个组的点数(势/形): {string.Join(", ", junctionGroups.Select(g => $"{g.points.Count}/{g.shapePoints.Count}"))}");
        }

        // 检查道路是否发生变化
        private static bool HasRoadChanged(LoftRoadBehaviour road)
        {
            if (road == null || road.Container == null) return false;

            if (!lastRoadState.ContainsKey(road))
            {
                return true; // 新道路，需要更新
            }

            var lastState = lastRoadState[road];
            var currentPositions = new Vector3[road.Container.Splines.Count][];
            var currentRotations = new Quaternion[road.Container.Splines.Count][];

            for (int i = 0; i < road.Container.Splines.Count; i++)
            {
                var spline = road.Container.Splines[i];
                currentPositions[i] = new Vector3[spline.Count];
                currentRotations[i] = new Quaternion[spline.Count];

                for (int j = 0; j < spline.Count; j++)
                {
                    currentPositions[i][j] = road.transform.TransformPoint(spline[j].Position);
                    currentRotations[i][j] = spline[j].Rotation;
                }
            }

            // 比较当前状态和上一次状态
            return !ArraysEqual<Vector3[]>(currentPositions, lastState.positions) || 
                   !ArraysEqual<Quaternion[]>(currentRotations, lastState.rotations);
        }

        // 更新道路状态记录
        private static void UpdateRoadState(LoftRoadBehaviour road)
        {
            if (road == null || road.Container == null) return;

            var positions = new Vector3[road.Container.Splines.Count][];
            var rotations = new Quaternion[road.Container.Splines.Count][];

            for (int i = 0; i < road.Container.Splines.Count; i++)
            {
                var spline = road.Container.Splines[i];
                positions[i] = new Vector3[spline.Count];
                rotations[i] = new Quaternion[spline.Count];

                for (int j = 0; j < spline.Count; j++)
                {
                    positions[i][j] = road.transform.TransformPoint(spline[j].Position);
                    rotations[i][j] = spline[j].Rotation;
                }
            }

            lastRoadState[road] = (positions, rotations);
        }

        // 只更新发生变化的道路的采样点和相关连接
        private static void UpdateChangedRoads(List<LoftRoadBehaviour> changedRoads, LoftRoadBehaviour[] allRoads)
        {
            foreach (var road in changedRoads)
            {
                if (road?.RoadExtensionDatas == null || road.Container == null)
                    continue;

                road.CollectSamplePoints(false);
            }

            AnalyzeJunctions(allRoads);
            GroupJunctionPoints(allRoads.ToList());

            // 强制刷新场景视图
            SceneView.RepaintAll();
        }

        private static void UpdateJunctionAnalysisForPreview(
            LoftRoadBehaviour[] allRoads,
            List<LoftRoadBehaviour> changedRoads,
            bool forceFullUpdate)
        {
            if (allRoads == null || allRoads.Length == 0)
                return;

            if (forceFullUpdate || changedRoads == null || changedRoads.Count == 0)
            {
                foreach (var road in allRoads)
                {
                    if (road?.RoadExtensionDatas == null || road.Container == null)
                        continue;

                    road.CollectSamplePoints(false);
                }
            }
            else
            {
                foreach (var road in changedRoads)
                {
                    if (road?.RoadExtensionDatas == null || road.Container == null)
                        continue;

                    road.CollectSamplePoints(false);
                }
            }

            AnalyzeJunctions(allRoads);
            GroupJunctionPoints(allRoads.ToList());
        }

        private static void RefreshAutomaticJunctionPreview(LoftRoadBehaviour[] allRoads)
        {
            ClearPreviewObjects();

            if (allRoads == null || allRoads.Length == 0)
                return;

            var plans = BuildJunctionRangePlans(allRoads);
            if (plans == null || plans.Count == 0)
                return;

            var previewRoadMap = BuildPreviewRoadMap(plans);
            if (previewRoadMap.Count == 0)
                return;

            var previewPlans = BuildPreviewPlans(plans, previewRoadMap);

            InsertBoundaryKnots(previewPlans, false);
            SplitRoadsAtJunctionRanges(previewPlans, false);

            var previewRoads = previewRoadMap.Values.ToArray();
            RebuildPreviewRoads(previewRoads);

            CreatePreviewJunctionObjectsFromPlans(previewPlans);

            SceneView.RepaintAll();
        }

        private static Dictionary<LoftRoadBehaviour, LoftRoadBehaviour> BuildPreviewRoadMap(
            IEnumerable<JunctionRangePlan> plans)
        {
            var previewRoadMap = new Dictionary<LoftRoadBehaviour, LoftRoadBehaviour>();
            if (plans == null)
                return previewRoadMap;

            foreach (var plan in plans)
            {
                if (plan?.Road == null)
                    continue;

                if (previewRoadMap.ContainsKey(plan.Road))
                    continue;

                var previewRoad = CreatePreviewRoadClone(plan.Road);
                if (previewRoad != null)
                    previewRoadMap[plan.Road] = previewRoad;
            }

            return previewRoadMap;
        }

        private static LoftRoadBehaviour CreatePreviewRoadClone(LoftRoadBehaviour source)
        {
            if (source == null || source.Container == null)
                return null;

            var previewRoot = new GameObject($"{source.name}_Preview");
            previewRoot.transform.SetParent(source.transform, false);
            previewRoot.transform.localPosition = Vector3.zero;
            previewRoot.transform.localRotation = Quaternion.identity;
            previewRoot.transform.localScale = Vector3.one;

            var marker = previewRoot.AddComponent<PreviewObjectMarker>();
            marker.previewType = PreviewObjectType.Road;

            var previewContainer = previewRoot.AddComponent<SplineContainer>();
            while (previewContainer.Splines.Count > 0)
                previewContainer.RemoveSplineAt(0);
            foreach (var spline in source.Container.Splines)
            {
                if (spline != null)
                    previewContainer.AddSpline(new Spline(spline));
            }

            var previewRoad = previewRoot.AddComponent<LoftRoadBehaviour>();
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(source), previewRoad);
            previewRoad.Container = previewContainer;
            previewRoad.connectedJunctions = new List<JunctionData>();

            foreach (var roadData in previewRoad.RoadExtensionDatas)
            {
                if (roadData == null)
                    continue;

                roadData.samplePoints = new List<RoadSamplePoint>();
                roadData.orderedJunctionPoints = new List<RoadSamplePoint>();
                roadData.connections = new List<RoadConnection>();
            }

            ResetPreviewRoadMeshes(previewRoad);
            SetHideFlagsRecursively(previewRoot, HideFlags.DontSave);

            return previewRoad;
        }

        private static List<JunctionRangePlan> BuildPreviewPlans(
            List<JunctionRangePlan> sourcePlans,
            IReadOnlyDictionary<LoftRoadBehaviour, LoftRoadBehaviour> previewRoadMap)
        {
            var previewPlans = new List<JunctionRangePlan>();
            if (sourcePlans == null || previewRoadMap == null)
                return previewPlans;

            foreach (var plan in sourcePlans)
            {
                if (plan == null || plan.Road == null)
                    continue;

                if (!previewRoadMap.TryGetValue(plan.Road, out var previewRoad))
                    continue;

                previewPlans.Add(new JunctionRangePlan
                {
                    Group = plan.Group,
                    Road = previewRoad,
                    SplineIndex = plan.SplineIndex,
                    HitCurveUs = plan.HitCurveUs != null ? new List<float>(plan.HitCurveUs) : new List<float>(),
                    HitPoints = plan.HitPoints != null ? new List<RoadSamplePoint>(plan.HitPoints) : new List<RoadSamplePoint>()
                });
            }

            return previewPlans;
        }

        private static void ResetPreviewRoadMeshes(LoftRoadBehaviour previewRoad)
        {
            if (previewRoad == null)
                return;

            var serializedRoad = new SerializedObject(previewRoad);
            string[] meshFields =
            {
                "m_Mesh",
                "m_GreenBeltMesh",
                "m_SidewalkLeftMesh",
                "m_SidewalkRightMesh",
                "m_CurbLeftMesh",
                "m_CurbRightMesh",
                "m_RoadEdgeLeftMesh",
                "m_RoadEdgeRightMesh",
                "m_RoadEdgeOuterLeftMesh",
                "m_RoadEdgeOuterRightMesh",
                "m_LanesContainer"
            };

            foreach (var fieldName in meshFields)
            {
                var property = serializedRoad.FindProperty(fieldName);
                if (property != null)
                    property.objectReferenceValue = null;
            }

            serializedRoad.ApplyModifiedPropertiesWithoutUndo();

            var meshFilter = previewRoad.GetComponent<MeshFilter>();
            if (meshFilter != null)
                meshFilter.sharedMesh = null;
        }

        private static void RebuildPreviewRoads(IEnumerable<LoftRoadBehaviour> previewRoads)
        {
            if (previewRoads == null)
                return;

            foreach (var road in previewRoads)
            {
                if (road == null)
                    continue;

                road.CollectSamplePoints(false);
                road.LoftAllRoads();
                ApplyPreviewMaterialToHierarchy(road.transform);
                SetHideFlagsRecursively(road.gameObject, HideFlags.DontSave);
            }
        }

        private static int CreatePreviewJunctionObjectsFromPlans(List<JunctionRangePlan> plans)
        {
            int createdCount = 0;
            if (plans == null || plans.Count == 0)
                return 0;

            foreach (var groupedPlans in plans
                         .Where(plan => plan?.Group != null)
                         .GroupBy(plan => plan.Group))
            {
                var group = groupedPlans.Key;
                var groupedPlanList = groupedPlans.ToList();
                if (group?.shapePoints == null || group.shapePoints.Count < 2)
                    continue;

                List<(LoftRoadBehaviour road, int splineIndex, int knotIndex)> connections =
                    new List<(LoftRoadBehaviour road, int splineIndex, int knotIndex)>();
                HashSet<(int roadInstanceId, int splineIndex, int knotIndex)> usedKeys =
                    new HashSet<(int roadInstanceId, int splineIndex, int knotIndex)>();
                foreach (var plan in groupedPlanList)
                {
                    AddPlanConnection(connections, usedKeys, plan.StartConnection);
                    AddPlanConnection(connections, usedKeys, plan.EndConnection);
                }

                if (connections.Count < 2)
                    continue;

                SortConnectionsClockwise(connections);

                int groupIndex = GetJunctionGroupDebugIndex(group);
                var junctionObject = JunctionGenerator.CreatePreviewJunction(connections, GetPreviewMaterial(), groupIndex);
                if (junctionObject != null)
                {
                    ApplyPreviewMaterialToHierarchy(junctionObject.transform);
                    SetHideFlagsRecursively(junctionObject, HideFlags.DontSave);
                    createdCount++;
                }
            }

            return createdCount;
        }

        private static void ApplyPreviewMaterialToHierarchy(Transform root)
        {
            if (root == null)
                return;

            var material = GetPreviewMaterial();
            if (material == null)
                return;

            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer == null)
                    continue;

                var materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    renderer.sharedMaterial = material;
                    continue;
                }

                for (int i = 0; i < materials.Length; i++)
                    materials[i] = material;

                renderer.sharedMaterials = materials;
            }
        }

        private static Material GetPreviewMaterial()
        {
            if (previewMaterial != null)
                return previewMaterial;

            Shader shader = Shader.Find("Unlit/Color") ??
                            Shader.Find("HDRP/Unlit") ??
                            Shader.Find("Standard");
            if (shader == null)
                return null;

            previewMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            Color previewColor = new Color(0.1f, 0.9f, 0.3f, 0.35f);
            if (previewMaterial.HasProperty("_BaseColor"))
                previewMaterial.SetColor("_BaseColor", previewColor);
            if (previewMaterial.HasProperty("_Color"))
                previewMaterial.SetColor("_Color", previewColor);
            if (previewMaterial.HasProperty("_UnlitColor"))
                previewMaterial.SetColor("_UnlitColor", previewColor);

            previewMaterial.renderQueue = 3000;
            return previewMaterial;
        }

        private static void SetHideFlagsRecursively(GameObject root, HideFlags flags)
        {
            if (root == null)
                return;

            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform == null)
                    continue;

                transform.gameObject.hideFlags = flags;
            }
        }

        // 检查两个道路数据之间的交叉点
        private static void CheckCrossPoints(
            List<RoadSamplePoint> candidatePoints1,
            LoftRoadExtensionData roadData1,
            List<RoadSamplePoint> candidatePoints2,
            LoftRoadExtensionData roadData2)
        {
            if (roadData1 == null || roadData2 == null ||
                candidatePoints1 == null || candidatePoints2 == null ||
                candidatePoints1.Count == 0 || candidatePoints2.Count == 0)
            {
                return;
            }

            foreach (var point1 in candidatePoints1)
            {
                if (point1 == null)
                    continue;

                foreach (var point2 in candidatePoints2)
                {
                    if (point2 == null)
                        continue;

                    if (point1 == point2) continue;

                    if (ShouldConnectSamplePoints(point1, roadData1, point2, roadData2))
                    {
                        point1.isCross = true;
                        point2.isCross = true;

                        roadData1.connections.Add(new RoadConnection 
                        { 
                            start = point1, 
                            end = point2 
                        });
                    }
                }
            }
        }

        // 辅助方法：比较数组是否相等
        private static bool ArraysEqual<T>(T[] a1, T[] a2)
        {
            if (ReferenceEquals(a1, a2)) return true;
            if (a1 == null || a2 == null) return false;
            if (a1.Length != a2.Length) return false;
            
            for (int i = 0; i < a1.Length; i++)
            {
                if (!a1[i].Equals(a2[i])) return false;
            }
            return true;
        }

        private static List<Vector3> GetGroupOutlinePoints(JunctionGroup group)
        {
            return SortWorldPositionsClockwise(
                BuildGroupBoundaryTargets(group)
                    .Select(target => target.Position));
        }

        private static List<Vector3> GetGroupShapeOutlinePoints(JunctionGroup group)
        {
            return SortWorldPositionsClockwise(
                BuildGroupShapeBoundaryTargets(group)
                    .Select(target => target.Position));
        }

        private static void DrawLiveJunctionDebug(LoftRoadBehaviour[] roads)
        {
            foreach (var road in roads)
            {
                if (road?.RoadExtensionDatas == null)
                    continue;

                foreach (var roadData in road.RoadExtensionDatas)
                {
                    if (roadData == null)
                        continue;

                    if (roadData.samplePoints != null)
                    {
                        foreach (var point in roadData.samplePoints)
                        {
                            if (point == null || !point.isCross)
                                continue;

                            Handles.color = Color.red;
                            Handles.SphereHandleCap(0, ToVector3(point.position), Quaternion.identity, 0.5f, EventType.Repaint);
                        }
                    }

                    if (roadData.connections == null)
                        continue;

                    Handles.color = Color.yellow;
                    foreach (var connection in roadData.connections)
                    {
                        if (connection?.start == null || connection.end == null)
                            continue;

                        Handles.DrawLine(ToVector3(connection.start.position), ToVector3(connection.end.position));
                    }
                }
            }
        }

        private static void DrawFrozenJunctionDebugSnapshot()
        {
            if (!HasFrozenJunctionDebugSnapshot())
                return;

            Handles.color = Color.red;
            foreach (var point in frozenJunctionDebugSnapshot.CrossPoints)
            {
                Handles.SphereHandleCap(0, point, Quaternion.identity, 0.5f, EventType.Repaint);
            }

            Handles.color = Color.yellow;
            foreach (var connection in frozenJunctionDebugSnapshot.Connections)
            {
                Handles.DrawLine(connection.Start, connection.End);
            }
        }

        private static void DrawGroupOutline(List<Vector3> outlinePoints, float width)
        {
            if (outlinePoints == null || outlinePoints.Count < 2)
                return;

            if (outlinePoints.Count == 2)
            {
                Handles.DrawAAPolyLine(width, outlinePoints[0], outlinePoints[1]);
                return;
            }

            for (int i = 0; i < outlinePoints.Count; i++)
            {
                Vector3 current = outlinePoints[i];
                Vector3 next = outlinePoints[(i + 1) % outlinePoints.Count];
                Handles.DrawAAPolyLine(width, current, next);
            }
        }

        private static void DrawSortedGroupPoints(
            List<Vector3> outlinePoints,
            int groupIndex,
            string labelPrefix,
            Color pointColor,
            Color ringColor,
            bool useRectangleHandle)
        {
            if (outlinePoints == null || outlinePoints.Count == 0)
                return;

            GUIStyle pointLabelStyle = new GUIStyle(EditorStyles.boldLabel);
            pointLabelStyle.fontSize = 14;
            pointLabelStyle.normal.textColor = ringColor;
            pointLabelStyle.alignment = TextAnchor.MiddleCenter;

            for (int i = 0; i < outlinePoints.Count; i++)
            {
                Vector3 point = outlinePoints[i];
                float handleSize = HandleUtility.GetHandleSize(point) * 0.12f;

                Handles.color = pointColor;
                if (useRectangleHandle)
                    Handles.RectangleHandleCap(0, point, Quaternion.identity, handleSize * 1.35f, EventType.Repaint);
                else
                    Handles.SphereHandleCap(0, point, Quaternion.identity, handleSize, EventType.Repaint);

                Handles.color = ringColor;
                Handles.DrawWireDisc(point, Vector3.up, handleSize * 0.75f);

                Handles.Label(
                    point + Vector3.up * (handleSize * 1.6f),
                    $"{labelPrefix}{groupIndex}:{i}",
                    pointLabelStyle);
            }
        }

        private static void DrawJunctionGroups()
        {
            if (junctionGroups == null) return;

            for (int groupIndex = 0; groupIndex < junctionGroups.Count; groupIndex++)
            {
                var group = junctionGroups[groupIndex];
                if (group?.points == null || group.points.Count == 0)
                    continue;

                var outlinePoints = GetGroupOutlinePoints(group);
                var shapeOutlinePoints = GetGroupShapeOutlinePoints(group);
                if (outlinePoints.Count < 2 && shapeOutlinePoints.Count < 2)
                    continue;

                if (outlinePoints.Count >= 2)
                {
                    Handles.color = junctionGroupFillColor;
                    DrawGroupOutline(outlinePoints, 10f);

                    Handles.color = junctionGroupRangeColor;
                    DrawGroupOutline(outlinePoints, 6f);

                    DrawSortedGroupPoints(
                        outlinePoints,
                        groupIndex,
                        "G",
                        new Color(0.35f, 0.1f, 0.45f, 1f),
                        junctionGroupRangeColor,
                        false);
                }

                if (shapeOutlinePoints.Count >= 2)
                {
                    Handles.color = junctionShapeFillColor;
                    DrawGroupOutline(shapeOutlinePoints, 8f);

                    Handles.color = junctionShapeRangeColor;
                    DrawGroupOutline(shapeOutlinePoints, 4f);

                    DrawSortedGroupPoints(
                        shapeOutlinePoints,
                        groupIndex,
                        "S",
                        new Color(0.08f, 0.45f, 0.34f, 1f),
                        junctionShapeRangeColor,
                        true);
                }

                Handles.color = junctionGroupRangeColor;
                Handles.DrawWireDisc(group.centerPosition, Vector3.up, 0.5f);

                GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel);
                labelStyle.fontSize = 18;
                labelStyle.normal.textColor = junctionGroupRangeColor;
                labelStyle.alignment = TextAnchor.MiddleCenter;

                Handles.Label(
                    group.centerPosition + Vector3.up * 2f,
                    $"Group {groupIndex} ({group.points.Count}/{group.shapePoints.Count})",
                    labelStyle);
            }
        }

        private static void DrawFrozenJunctionGroups()
        {
            if (!HasFrozenJunctionDebugSnapshot())
                return;

            for (int groupIndex = 0; groupIndex < frozenJunctionDebugSnapshot.Groups.Count; groupIndex++)
            {
                var group = frozenJunctionDebugSnapshot.Groups[groupIndex];
                if (group == null)
                    continue;

                if (group.OutlinePoints.Count >= 2)
                {
                    Handles.color = junctionGroupFillColor;
                    DrawGroupOutline(group.OutlinePoints, 10f);

                    Handles.color = junctionGroupRangeColor;
                    DrawGroupOutline(group.OutlinePoints, 6f);

                    DrawSortedGroupPoints(
                        group.OutlinePoints,
                        groupIndex,
                        "G",
                        new Color(0.35f, 0.1f, 0.45f, 1f),
                        junctionGroupRangeColor,
                        false);
                }

                if (group.ShapeOutlinePoints.Count >= 2)
                {
                    Handles.color = junctionShapeFillColor;
                    DrawGroupOutline(group.ShapeOutlinePoints, 8f);

                    Handles.color = junctionShapeRangeColor;
                    DrawGroupOutline(group.ShapeOutlinePoints, 4f);

                    DrawSortedGroupPoints(
                        group.ShapeOutlinePoints,
                        groupIndex,
                        "S",
                        new Color(0.08f, 0.45f, 0.34f, 1f),
                        junctionShapeRangeColor,
                        true);
                }

                Handles.color = junctionGroupRangeColor;
                Handles.DrawWireDisc(group.CenterPosition, Vector3.up, 0.5f);

                GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel);
                labelStyle.fontSize = 18;
                labelStyle.normal.textColor = junctionGroupRangeColor;
                labelStyle.alignment = TextAnchor.MiddleCenter;

                Handles.Label(
                    group.CenterPosition + Vector3.up * 2f,
                    $"Group {groupIndex} ({group.PointCount}/{group.ShapePointCount})",
                    labelStyle);
            }
        }

        private static void AutoAnalyzeJunctionInternal()
        {
            try
            {
                var roads = FindSceneRoads();
                if (roads == null || roads.Length == 0)
                {
                    EditorUtility.DisplayDialog("提示", "场景中没有找到道路对象", "确定");
                    return;
                }

                // 清除现有的路口组
                junctionGroups.Clear();

                // 调用已有的分析方法
                AutoAnalyzeAndCreateJunctions();

                // 刷新编辑器视图
                SceneView.RepaintAll();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"自动分析路口时发生错误: {ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog("错误", $"自动分析路口时发生错误: {ex.Message}", "确定");
            }
        }

        [MenuItem("道路工具/自动分析创建路口 %#j")]
        private static void AutoAnalyzeJunctionMenuItem()
        {
            try
            {
                var roads = FindSceneRoads();
                if (roads == null || roads.Length == 0)
                {
                    EditorUtility.DisplayDialog("提示", "场景中没有找到道路对象", "确定");
                    return;
                }

                // 清除现有的路口组
                junctionGroups.Clear();

                // 调用已有的分析方法
                AutoAnalyzeAndCreateJunctions();

                // 刷新编辑器视图
                SceneView.RepaintAll();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"自动分析路口时发生错误: {ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog("错误", $"自动分析路口时发生错误: {ex.Message}", "确定");
            }
        }

        // 注释掉原有的创建凹包Knot菜单项
        // [MenuItem("道路工具/在路口边界创建Knot")]
        // private static void CreateBoundaryKnotsMenuItem()
        // {
        //     CreateBoundaryKnots();
        // }

        private static void DrawSamplePointsDebug()
        {
            var roads = FindSceneRoads();
            foreach (var road in roads)
            {
                if (road.RoadExtensionDatas == null) continue;

                for (int splineIndex = 0; splineIndex < road.RoadExtensionDatas.Count; splineIndex++)
                {
                    var roadData = road.RoadExtensionDatas[splineIndex];
                    if (roadData == null || roadData.samplePoints == null) continue;

                    // 绘制采样点
                    for (int i = 0; i < roadData.samplePoints.Count; i++)
                    {
                        var point = roadData.samplePoints[i];
                        Vector3 pointPosition = new Vector3(point.position.x, point.position.y, point.position.z);
                        
                        // 绘制球体
                        Handles.color = new Color(0, 1, 0, 0.8f);
                        Handles.SphereHandleCap(0, pointPosition, Quaternion.identity, 0.3f, EventType.Repaint);
                        
                        // 绘制序号标签
                        Handles.Label(pointPosition + Vector3.up * 0.5f, i.ToString());
                        
                        // 绘制连线
                        if (i < roadData.samplePoints.Count - 1)
                        {
                            var nextPoint = roadData.samplePoints[i + 1];
                            Vector3 nextPointPosition = new Vector3(nextPoint.position.x, nextPoint.position.y, nextPoint.position.z);
                            Handles.color = new Color(0, 1, 0, 0.5f);
                            Handles.DrawLine(pointPosition, nextPointPosition, 2f);
                        }
                    }
                }
            }
        }

        private static void DrawOrderedPointsDebug()
        {
            var roads = FindSceneRoads();
            foreach (var road in roads)
            {
                if (road.RoadExtensionDatas == null) continue;

                for (int splineIndex = 0; splineIndex < road.RoadExtensionDatas.Count; splineIndex++)
                {
                    var roadData = road.RoadExtensionDatas[splineIndex];
                    if (roadData == null || roadData.orderedJunctionPoints == null) continue;

                    // 绘制有序点列表
                    for (int i = 0; i < roadData.orderedJunctionPoints.Count; i++)
                    {
                        var point = roadData.orderedJunctionPoints[i];
                        Vector3 pointPosition = new Vector3(point.position.x, point.position.y, point.position.z);
                        
                        // 根据点类型使用不同颜色
                        if (point.isOriginalKnot)
                        {
                            Handles.color = new Color(1, 0, 0, 0.8f); // 红色表示原始knot
                        }
                        else if (point.isCross)
                        {
                            Handles.color = new Color(0, 0, 1, 0.8f); // 蓝色表示交叉点
                        }
                        
                        // 绘制球体
                        Handles.SphereHandleCap(0, pointPosition, Quaternion.identity, 0.3f, EventType.Repaint);
                        
                        // 绘制序号标签
                        Handles.Label(pointPosition + Vector3.up * 0.5f, i.ToString());
                        
                        // 绘制连线
                        if (i < roadData.orderedJunctionPoints.Count - 1)
                        {
                            var nextPoint = roadData.orderedJunctionPoints[i + 1];
                            Vector3 nextPointPosition = new Vector3(nextPoint.position.x, nextPoint.position.y, nextPoint.position.z);
                            Handles.color = new Color(1, 1, 0, 0.5f); // 黄色连线
                            Handles.DrawLine(pointPosition, nextPointPosition, 2f);
                        }
                    }
                }
            }
        }

        [MenuItem("道路工具/删除路口区域内的Spline", false, 21)]
        private static void RemoveSplineAtJunctionsMenuItem()
        {
            RemoveSplineAtJunctions();
        }

        private static void RemoveSplineAtJunctions()
        {
            ApplyAutomaticJunctionTopology(false);
        }
    } // RoadGeneratorTool类的结束
} // namespace的结束
