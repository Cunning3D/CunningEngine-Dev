using System;
using System.Collections.Generic;
using System.Linq;
using CunningEngine;
using UnityEditor;
using UnityEngine;
using Unity.Splines.Examples;

namespace UnityEditor.Splines.Extension
{
    public partial class RoadGeneratorTool
    {
        private const float OfficialChipMinWidth = 84f;
        private const float OfficialChipMaxWidth = 196f;
        private const float OfficialChipMinHeight = 22f;
        private const float OfficialChipHorizontalPadding = 8f;
        private const float OfficialChipTopPadding = 3f;
        private const float OfficialChipBottomPadding = 3f;
        private const float OfficialChipInlineGap = 4f;
        private const float OfficialChipSpacing = 6f;

        internal const string OfficialWindowDisplayName = "Cunning Road Sample";
        private const string OfficialActiveTabPrefKey = "RoadGeneratorTool_OfficialActiveTab";

        private static readonly OfficialUiState officialUiState = new OfficialUiState();
        private static readonly Dictionary<RoadInfo, Texture2D> customRoadInfoThumbnailCache = new Dictionary<RoadInfo, Texture2D>();

        private static GUIStyle officialSectionStyle;
        private static GUIStyle officialSectionTitleStyle;
        private static GUIStyle officialSectionSubtitleStyle;
        private static GUIStyle officialSectionMetricStyle;
        private static GUIStyle officialInlineHintStyle;
        private static GUIStyle officialListRowStyle;
        private static GUIStyle officialBadgeStyle;
        private static GUIStyle officialChipStyle;
        private static GUIStyle officialChipKeyStyle;
        private static GUIStyle officialChipValueStyle;
        private static GUIStyle officialEmptyStateStyle;
        private static GUIStyle officialActionButtonStyle;
        private static GUIStyle officialPrimaryActionButtonStyle;
        private static GUIStyle officialQuietActionButtonStyle;
        private static GUIStyle officialDangerActionButtonStyle;
        private static GUIStyle officialActionButtonLabelStyle;
        private static GUIStyle officialActionChevronStyle;
        private static string officialStyleCacheKey;

        private enum OfficialActionButtonVariant
        {
            Standard = 0,
            Primary = 1,
            Quiet = 2,
            Danger = 3
        }

        internal enum OfficialTab
        {
            Road = 0,
            Junction = 1,
            Segment = 2,
            Tools = 3,
            Debug = 4
        }

        internal enum OfficialPreviewState
        {
            None = 0,
            Ready = 1,
            Dirty = 2
        }

        internal readonly struct OfficialTabTheme
        {
            public readonly OfficialTab Tab;
            public readonly string TabKey;
            public readonly string IconName;
            public readonly string ShortLabel;
            public readonly string DisplayName;
            public readonly Color AccentColor;

            public OfficialTabTheme(OfficialTab tab, string tabKey, string iconName, string shortLabel, string displayName, Color accentColor)
            {
                Tab = tab;
                TabKey = tabKey;
                IconName = iconName;
                ShortLabel = shortLabel;
                DisplayName = displayName;
                AccentColor = accentColor;
            }
        }

        internal readonly struct OfficialHandleStatus
        {
            public readonly bool HasHandle;
            public readonly string TargetName;
            public readonly ulong Handle;
            public readonly ulong DirtyId;
            public readonly string SyncState;

            public OfficialHandleStatus(bool hasHandle, string targetName, ulong handle, ulong dirtyId, string syncState)
            {
                HasHandle = hasHandle;
                TargetName = targetName;
                Handle = handle;
                DirtyId = dirtyId;
                SyncState = syncState;
            }
        }

        internal readonly struct OfficialUiSnapshot
        {
            public readonly OfficialTab ActiveTab;
            public readonly OfficialTabTheme Theme;
            public readonly RoadGeneratorOfficialTheme VisualTheme;
            public readonly bool HasSelection;
            public readonly string SelectedObjectName;
            public readonly string RoadTypeLabel;
            public readonly string CustomRoadLabel;
            public readonly bool AutoPreview;
            public readonly bool AutoSamplePoints;
            public readonly int SelectedConnectionCount;
            public readonly OfficialPreviewState PreviewState;
            public readonly bool HasPreviewObjects;
            public readonly OfficialHandleStatus HandleStatus;

            public OfficialUiSnapshot(
                OfficialTab activeTab,
                OfficialTabTheme theme,
                RoadGeneratorOfficialTheme visualTheme,
                bool hasSelection,
                string selectedObjectName,
                string roadTypeLabel,
                string customRoadLabel,
                bool autoPreview,
                bool autoSamplePoints,
                int selectedConnectionCount,
                OfficialPreviewState previewState,
                bool hasPreviewObjects,
                OfficialHandleStatus handleStatus)
            {
                ActiveTab = activeTab;
                Theme = theme;
                VisualTheme = visualTheme;
                HasSelection = hasSelection;
                SelectedObjectName = selectedObjectName;
                RoadTypeLabel = roadTypeLabel;
                CustomRoadLabel = customRoadLabel;
                AutoPreview = autoPreview;
                AutoSamplePoints = autoSamplePoints;
                SelectedConnectionCount = selectedConnectionCount;
                PreviewState = previewState;
                HasPreviewObjects = hasPreviewObjects;
                HandleStatus = handleStatus;
            }
        }

        private readonly struct OfficialChipLayoutItem
        {
            public readonly string Key;
            public readonly string Value;
            public readonly float Width;
            public readonly float Height;
            public readonly float KeyWidth;
            public readonly float ValueWidth;

            public OfficialChipLayoutItem(string key, string value, float width, float height, float keyWidth, float valueWidth)
            {
                Key = key;
                Value = value;
                Width = width;
                Height = height;
                KeyWidth = keyWidth;
                ValueWidth = valueWidth;
            }
        }

        private sealed class OfficialUiState
        {
            public OfficialTab ActiveTab
            {
                get => (OfficialTab)Mathf.Clamp(EditorPrefs.GetInt(OfficialActiveTabPrefKey, 0), 0, 4);
                set => EditorPrefs.SetInt(OfficialActiveTabPrefKey, (int)value);
            }

            public bool AutoPreview => autoPreviewJunctions;
            public bool AutoUpdateSamplePoints => autoUpdateSamplePoints;
            public int SelectedConnectionCount => selectedConnections?.Count ?? 0;
            public RoadType SelectedRoadType => selectedRoadType;
            public RoadInfo SelectedCustomRoadInfo => selectedCustomRoadInfo;
            public OfficialPreviewState PreviewState => ComputePreviewState();
        }

        private sealed class CustomRoadInfoSelectionPopup : PopupWindowContent
        {
            private Vector2 scrollPosition;

            public override Vector2 GetWindowSize()
            {
                return new Vector2(430f, 540f);
            }

            public override void OnGUI(Rect rect)
            {
                EnsureRoadTypeSelectorStyles();
                EnsureOfficialUiStyles();

                GUILayout.BeginArea(rect);
                GUILayout.Space(10f);
                GUILayout.Label("选择自定义道路信息", roadTypePopupHeaderStyle);
                GUILayout.Label("保持与道路预设一致的卡片式选择体验，方便直接切换官方示例配置。", roadTypePopupSubHeaderStyle);
                GUILayout.Space(8f);

                scrollPosition = GUILayout.BeginScrollView(scrollPosition);

                DrawCustomRoadInfoPopupEntry(null, selectedCustomRoadInfo == null, "使用道路预设默认参数");
                GUILayout.Space(6f);

                if (customRoadInfos != null)
                {
                    foreach (RoadInfo roadInfo in customRoadInfos.Where(info => info != null))
                    {
                        bool isSelected = roadInfo == selectedCustomRoadInfo;
                        DrawCustomRoadInfoPopupEntry(roadInfo, isSelected, isSelected ? "当前已选" : "点击切换");
                        GUILayout.Space(6f);
                    }
                }

                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }

            private void DrawCustomRoadInfoPopupEntry(RoadInfo roadInfo, bool isSelected, string footerText)
            {
                Rect cardRect = GUILayoutUtility.GetRect(0f, RoadTypePopupCardHeight, GUILayout.ExpandWidth(true));
                if (!DrawCustomRoadInfoCard(cardRect, roadInfo, isSelected, isCompact: true, footerText))
                {
                    return;
                }

                selectedCustomRoadInfo = roadInfo;
                if (roadInfo != null)
                {
                    selectedRoadType = RoadType.Road_A;
                }

                SetDefaultWidth();
                GUI.changed = true;
                editorWindow.Close();
                SceneView.RepaintAll();
            }
        }

        private static readonly OfficialTabTheme[] officialTabThemes =
        {
            new OfficialTabTheme(OfficialTab.Road, "road", "road", "Road", "Road", ParseHexColor("#59AAFF")),
            new OfficialTabTheme(OfficialTab.Junction, "junction", "junction", "Junction", "Junction", ParseHexColor("#FFB14A")),
            new OfficialTabTheme(OfficialTab.Segment, "segment", "segment", "Segment", "Segment", ParseHexColor("#45C08A")),
            new OfficialTabTheme(OfficialTab.Tools, "tools", "tools", "Tools", "Tools", ParseHexColor("#A47CFF")),
            new OfficialTabTheme(OfficialTab.Debug, "debug", "debug", "Debug", "Debug", ParseHexColor("#FF6A6A"))
        };

        internal static IReadOnlyList<OfficialTabTheme> GetOfficialTabThemes()
        {
            return officialTabThemes;
        }

        internal static OfficialUiSnapshot CaptureOfficialUiSnapshot()
        {
            OfficialTab activeTab = officialUiState.ActiveTab;
            OfficialTabTheme tabTheme = GetOfficialTheme(activeTab);
            RoadGeneratorOfficialTheme visualTheme = RoadGeneratorOfficialTheme.Resolve(tabTheme);
            GameObject selectedObject = Selection.activeGameObject;
            return new OfficialUiSnapshot(
                activeTab,
                tabTheme,
                visualTheme,
                selectedObject != null,
                selectedObject != null ? selectedObject.name : "None",
                RoadDefaultsInfors.GetRoadName(officialUiState.SelectedRoadType),
                GetSelectedCustomRoadDisplayName(),
                officialUiState.AutoPreview,
                officialUiState.AutoUpdateSamplePoints,
                officialUiState.SelectedConnectionCount,
                officialUiState.PreviewState,
                HasPreviewObjects(),
                CaptureSelectedHandleStatus());
        }

        internal static void SetOfficialTab(OfficialTab tab)
        {
            if (officialUiState.ActiveTab == tab)
            {
                return;
            }

            officialUiState.ActiveTab = tab;
            SceneView.RepaintAll();
        }

        internal static void DrawOfficialTabContent()
        {
            OfficialTabTheme tabTheme = GetOfficialTheme(officialUiState.ActiveTab);
            RoadGeneratorOfficialTheme visualTheme = RoadGeneratorOfficialTheme.Resolve(tabTheme);
            EnsureOfficialUiStyles(visualTheme);

            switch (officialUiState.ActiveTab)
            {
                case OfficialTab.Road:
                    DrawOfficialRoadTab(visualTheme);
                    break;
                case OfficialTab.Junction:
                    DrawOfficialJunctionTab(visualTheme);
                    break;
                case OfficialTab.Segment:
                    DrawOfficialSegmentTab(visualTheme);
                    break;
                case OfficialTab.Tools:
                    DrawOfficialToolsTab(visualTheme);
                    break;
                case OfficialTab.Debug:
                    DrawOfficialDebugTab(visualTheme);
                    break;
            }
        }

        internal static void ApplyPreviewFromOfficialUi()
        {
            ApplyPreviewJunctions();
        }

        internal static void ClearPreviewFromOfficialUi()
        {
            ClearPreviewObjects();
            SceneView.RepaintAll();
        }

        internal static void ToggleOfficialWindow()
        {
            showWindow = !showWindow;
            EditorPrefs.SetBool(ShowWindowPrefKey, showWindow);
            if (showWindow)
            {
                RoadGeneratorSvgIconService.WarmUp();
            }

            SceneView.RepaintAll();
        }

        [MenuItem("Procedural/道路系统/刷新官方道路示例图标缓存")]
        private static void RefreshOfficialRoadUiIcons()
        {
            RoadGeneratorSvgIconService.RefreshGeneratedLayers();
            SceneView.RepaintAll();
        }

        private static void DrawOfficialRoadTab(RoadGeneratorOfficialTheme visualTheme)
        {
            bool selectionChanged = false;

            DrawOfficialSection(visualTheme, "配置卡", "保留现有预设与自定义资产逻辑，只升级工作台组织方式。", contentWidth =>
            {
                EditorGUI.BeginChangeCheck();
                selectedRoadType = CustomEnumPopup("道路类型", selectedRoadType);
                selectedCustomRoadInfo = DrawCustomRoadInfoSelector("自定义道路信息", selectedCustomRoadInfo);
                if (EditorGUI.EndChangeCheck())
                {
                    selectionChanged = true;
                }

                DrawOfficialChipRow(visualTheme, contentWidth,
                    ("默认宽度", $"{GetDefaultWidth():0.##} m"),
                    ("当前预设", RoadDefaultsInfors.GetRoadName(selectedRoadType)),
                    ("当前自定义", GetSelectedCustomRoadDisplayName()));

                GUILayout.Space(10f);
                GUILayout.Label("主动作", officialInlineHintStyle);
                DrawOfficialActionButton(visualTheme, "创建道路", () => GenerateRoad(selectedRoadType, GetDefaultWidth(), selectedCustomRoadInfo), iconName: "d_CreateAddNew", variant: OfficialActionButtonVariant.Primary);
                DrawOfficialActionButton(visualTheme, "替换选定道路", () => ReplaceRoadType(selectedRoadType, GetDefaultWidth(), selectedCustomRoadInfo), iconName: "d_RotateTool");

                GUILayout.Space(8f);
                GUILayout.Label("参数维护", officialInlineHintStyle);
                DrawOfficialActionButton(visualTheme, "重置当前选择道路参数", UpdateRoadParameters, iconName: "d_Settings");
                DrawOfficialActionButton(visualTheme, "重置所有道路参数（慎用）", UpdateRoadParameters, iconName: "d_Settings");
            });

            if (selectionChanged)
            {
                SetDefaultWidth();
            }

            DrawOfficialSection(visualTheme, "创建卡", "道路创建与替换入口继续保留在独立卡片里。", () =>
            {
                DrawOfficialActionButton(visualTheme, "创建道路", () => GenerateRoad(selectedRoadType, GetDefaultWidth(), selectedCustomRoadInfo), iconName: "d_CreateAddNew", variant: OfficialActionButtonVariant.Primary);
                DrawOfficialActionButton(visualTheme, "替换选定道路", () => ReplaceRoadType(selectedRoadType, GetDefaultWidth(), selectedCustomRoadInfo), iconName: "d_RotateTool");
            });

            DrawOfficialSection(visualTheme, "数据资产卡", "复用现有 HDA、保存、导入导出动作。", () =>
            {
                DrawOfficialActionButton(visualTheme, "上传到道路HDA", UploadToRoadHDA, iconName: "d_Import");
                DrawOfficialActionButton(visualTheme, "保存道路信息", SaveRoadDataAsPrefabWithCleanupAndUpdate, iconName: "d_SaveAs");
                DrawOfficialActionButton(visualTheme, "读取道路信息", LoadRoadDataFromPrefabWithCleanupAndUpdate, iconName: "d_Import");
                DrawOfficialActionButton(visualTheme, "导出道路信息（JSON）", ExportRoadDataToJson);
                DrawOfficialActionButton(visualTheme, "保存隔离带网格", ExportGreenBeltMeshesToOBJ, iconName: "d_SaveAs");
            });

            DrawOfficialSection(visualTheme, "几何整理卡", "继续使用现有地形贴合、标线和清理逻辑。", () =>
            {
                DrawOfficialActionButton(visualTheme, "一键贴地", SnapToGround);
                DrawOfficialActionButton(visualTheme, "标线贴路面", () =>
                {
                    RecalculateRoadMeshInfo();
                    AlignRoadMarksToRoadSurface();
                });
                DrawOfficialActionButton(visualTheme, "清理曲线", CleanUpSplines, iconName: "TreeEditor.Trash", variant: OfficialActionButtonVariant.Quiet);
            });

            DrawOfficialSection(visualTheme, "特殊标记卡", "桥梁区域和引导线标线仍然作为正式业务入口保留。", () =>
            {
                DrawOfficialActionButton(visualTheme, "设置为桥梁区域", () =>
                {
                    SetBridgeKnot(true);
                    EditorUtility.DisplayDialog("提示", "设置完成", "确定");
                });
                DrawOfficialActionButton(visualTheme, "移除桥梁区域", () =>
                {
                    SetBridgeKnot(false);
                    EditorUtility.DisplayDialog("提示", "移除完成", "确定");
                }, variant: OfficialActionButtonVariant.Danger);
                DrawOfficialActionButton(visualTheme, "显隐引导线标线", SwitchRoadMarkersStatus);
            });
        }

        private static void DrawOfficialJunctionTab(RoadGeneratorOfficialTheme visualTheme)
        {
            DrawOfficialSection(visualTheme, "预览状态卡", "自动采样和自动预览的入口集中到这里。", contentWidth =>
            {
                GUILayout.Label("算法参数", officialInlineHintStyle);
                float newHeight = EditorGUILayout.Slider(
                    new GUIContent("高度检测阈值", "检测路口连接时的最大允许高度差"),
                    globalHeightThreshold,
                    0f,
                    10f);
                if (Math.Abs(newHeight - globalHeightThreshold) > 0.001f)
                {
                    globalHeightThreshold = newHeight;
                    if (autoUpdateSamplePoints)
                    {
                        SceneView.RepaintAll();
                    }
                }

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
                    {
                        ClearPreviewObjects();
                    }

                    SceneView.RepaintAll();
                }

                DrawOfficialChipRow(visualTheme, contentWidth,
                    ("高度阈值", $"{globalHeightThreshold:0.##} m"),
                    ("预览状态", officialUiState.PreviewState.ToString()),
                    ("预览对象", HasPreviewObjects() ? "Exists" : "None"));
            });

            DrawOfficialSection(visualTheme, "动作卡", "分析、创建、更新流程保持原样。", () =>
            {
                DrawOfficialActionButton(visualTheme, "自动分析创建路口", () =>
                {
                    if (EditorUtility.DisplayDialog("确认", "是否要自动分析并创建所有可能的路口？", "确定", "取消"))
                    {
                        AutoAnalyzeAndCreateJunctions();
                    }
                }, iconName: "d_AutoLightmap", variant: OfficialActionButtonVariant.Primary);

                DrawOfficialActionButton(visualTheme, "创建路口", () =>
                {
                    if (selectedConnections.Count >= 2)
                    {
                        SortConnectionsClockwise();
                        var junctionObject = JunctionGenerator.CreateJunction(selectedConnections);
                        if (junctionObject != null)
                        {
                            junctionGroups.Clear();
                            SceneView.RepaintAll();
                        }

                        selectedConnections.Clear();
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("错误", "创建路口需要至少选择两个连接点", "确定");
                    }
                });

                DrawOfficialActionButton(visualTheme, "更新所有路口", () =>
                {
                    JunctionGenerator.UpdateAllJunctions();
                    SceneView.RepaintAll();
                }, variant: OfficialActionButtonVariant.Primary);

                DrawOfficialActionButton(visualTheme, "创建凹包Knot", CreateBoundaryKnots, iconName: "d_CreateAddNew");
                DrawOfficialActionButton(visualTheme, "清除连接点选择", () =>
                {
                    selectedConnections.Clear();
                    SceneView.RepaintAll();
                }, enabled: selectedConnections.Count > 0, variant: OfficialActionButtonVariant.Quiet);
            });

            DrawOfficialSection(visualTheme, "连接点卡", "SceneView 中的连接点选择直接反映到这里。", () =>
            {
                if (selectedConnections.Count == 0)
                {
                    DrawOfficialEmptyState(visualTheme, "当前没有选中的连接点。按住 Shift 可继续追加选择。");
                    return;
                }

                for (int index = 0; index < selectedConnections.Count; index++)
                {
                    var connection = selectedConnections[index];
                    EditorGUILayout.BeginHorizontal(officialListRowStyle);
                    string roadName = connection.road != null ? connection.road.name : "已删除的道路";
                    GUILayout.Label($"{roadName} · S{connection.splineIndex} · K{connection.knotIndex}", officialSectionMetricStyle);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("移除", GUILayout.Width(52f)))
                    {
                        selectedConnections.RemoveAt(index);
                        SceneView.RepaintAll();
                        EditorGUILayout.EndHorizontal();
                        break;
                    }

                    EditorGUILayout.EndHorizontal();
                }
            });

            DrawOfficialSection(visualTheme, "Gizmo 卡", "所有路口可视化开关统一集中。", () =>
            {
                showGizmos = EditorGUILayout.Toggle("显示所有Gizmo", showGizmos);
                showKnotPoints = EditorGUILayout.Toggle("显示路段端点", showKnotPoints);

                bool previousEnabled = GUI.enabled;
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
                GUI.enabled = previousEnabled;

                autoUpdateJunction = EditorGUILayout.Toggle("自动更新路口", autoUpdateJunction);
            });

            DrawOfficialSection(visualTheme, "说明卡", "官方示例模式保留高频说明，不把说明散在按钮 tooltip 里。", () =>
            {
                DrawOfficialEmptyState(visualTheme, "Shift 可追加选择连接点；当前官方 UI 只重构交互层，不改变路口生成算法和按钮语义。");
            });
        }

        private static void DrawOfficialSegmentTab(RoadGeneratorOfficialTheme visualTheme)
        {
            DrawOfficialSection(visualTheme, "Gizmo 卡", "车道中心线、边界线和标签的入口保留。", () =>
            {
                bool previousShowRoadSegmentGizmos = showRoadSegmentGizmos;
                showRoadSegmentGizmos = EditorGUILayout.Toggle("显示路段Gizmo", showRoadSegmentGizmos);
                if (previousShowRoadSegmentGizmos != showRoadSegmentGizmos)
                {
                    Unity.Splines.Examples.LoftRoadBehaviour.ShowLaneGizmos = showRoadSegmentGizmos;
                    SceneView.RepaintAll();
                }

                bool previousEnabled = GUI.enabled;
                GUI.enabled = showRoadSegmentGizmos;

                bool showLaneCenterLines = Unity.Splines.Examples.LoftRoadBehaviour.ShowLaneCenterLines;
                bool newShowLaneCenterLines = EditorGUILayout.Toggle("显示车道中心线", showLaneCenterLines);
                if (showLaneCenterLines != newShowLaneCenterLines)
                {
                    Unity.Splines.Examples.LoftRoadBehaviour.ShowLaneCenterLines = newShowLaneCenterLines;
                    SceneView.RepaintAll();
                }

                bool showLaneBoundaryLines = Unity.Splines.Examples.LoftRoadBehaviour.ShowLaneBoundaryLines;
                bool newShowLaneBoundaryLines = EditorGUILayout.Toggle("显示车道边界线", showLaneBoundaryLines);
                if (showLaneBoundaryLines != newShowLaneBoundaryLines)
                {
                    Unity.Splines.Examples.LoftRoadBehaviour.ShowLaneBoundaryLines = newShowLaneBoundaryLines;
                    SceneView.RepaintAll();
                }

                bool showLaneLabels = Unity.Splines.Examples.LoftRoadBehaviour.ShowLaneLabels;
                bool newShowLaneLabels = EditorGUILayout.Toggle("显示车道标签", showLaneLabels);
                if (showLaneLabels != newShowLaneLabels)
                {
                    Unity.Splines.Examples.LoftRoadBehaviour.ShowLaneLabels = newShowLaneLabels;
                    SceneView.RepaintAll();
                }

                GUI.enabled = previousEnabled;
            });

            DrawOfficialSection(visualTheme, "动作卡", "保留原始批量刷新入口。", () =>
            {
                DrawOfficialActionButton(visualTheme, "更新所有路段", () =>
                {
                    var roads = GameObject.FindObjectsOfType<Unity.Splines.Examples.LoftRoadBehaviour>();
                    foreach (var road in roads)
                    {
                        try
                        {
                            road.LoftAllRoads();
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"更新路段 {road.name} 时出错: {ex.Message}");
                        }
                    }

                    SceneView.RepaintAll();
                }, variant: OfficialActionButtonVariant.Primary);
            });
        }

        private static void DrawOfficialToolsTab(RoadGeneratorOfficialTheme visualTheme)
        {
            DrawOfficialSection(visualTheme, "生态区域卡", "生态区域生成能力仍然保留在官方示例里。", () =>
            {
                DrawOfficialActionButton(visualTheme, "生成生态区域", GenerateRegion, iconName: "d_CreateAddNew");
            });

            DrawOfficialSection(visualTheme, "自动化工具卡", "承接大世界生成与路网地形贴合工具。", () =>
            {
                DrawOfficialActionButton(visualTheme, "打开大世界生成工具", LoadPcgTool, iconName: "d_CreateAddNew");
                DrawOfficialActionButton(visualTheme, "路网地形贴合工具", SplineTerrainTool.ShowWindow, iconName: "d_CreateAddNew");
            });
        }

        private static void DrawOfficialDebugTab(RoadGeneratorOfficialTheme visualTheme)
        {
            DrawOfficialSection(visualTheme, "Debug 卡", "只收纳当前已有调试入口，不新增业务按钮。", contentWidth =>
            {
                bool previousSamplePoints = showSamplePointsDebug;
                showSamplePointsDebug = EditorGUILayout.Toggle("显示采样点顺序", showSamplePointsDebug);
                if (previousSamplePoints != showSamplePointsDebug)
                {
                    SceneView.RepaintAll();
                }

                bool previousOrderedPoints = showOrderedPointsDebug;
                showOrderedPointsDebug = EditorGUILayout.Toggle("显示有序点列表", showOrderedPointsDebug);
                if (previousOrderedPoints != showOrderedPointsDebug)
                {
                    SceneView.RepaintAll();
                }

                DrawOfficialChipRow(visualTheme, contentWidth,
                    ("已选连接点", (selectedConnections?.Count ?? 0).ToString()),
                    ("自动预览", autoPreviewJunctions ? "On" : "Off"),
                    ("实时采样", autoUpdateSamplePoints ? "On" : "Off"));
            });
        }

        private static void DrawOfficialSection(RoadGeneratorOfficialTheme visualTheme, string title, string subtitle, Action body)
        {
            DrawOfficialSection(visualTheme, title, subtitle, _ => body?.Invoke());
        }

        private static void DrawOfficialSection(RoadGeneratorOfficialTheme visualTheme, string title, string subtitle, Action<float> body)
        {
            EnsureOfficialUiStyles(visualTheme);

            EditorGUILayout.BeginVertical(officialSectionStyle);
            Rect headerRect = GUILayoutUtility.GetRect(1f, 22f, GUILayout.ExpandWidth(true));
            GUI.Box(headerRect, GUIContent.none, officialChipStyle);
            EditorGUI.DrawRect(new Rect(headerRect.x + 1f, headerRect.y + 1f, 2f, headerRect.height - 2f), visualTheme.AccentColor);
            GUI.Label(new Rect(headerRect.x + 8f, headerRect.y, headerRect.width - 16f, headerRect.height), title, officialSectionTitleStyle);
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                GUILayout.Space(6f);
                GUILayout.Label(subtitle, officialSectionSubtitleStyle);
            }

            GUILayout.Space(8f);
            Rect contentWidthProbe = EditorGUILayout.GetControlRect(false, 0f, GUILayout.ExpandWidth(true));
            body?.Invoke(contentWidthProbe.width);
            EditorGUILayout.EndVertical();
        }

        private static void DrawOfficialActionButton(
            RoadGeneratorOfficialTheme visualTheme,
            string label,
            Action action,
            bool enabled = true,
            string iconName = null,
            OfficialActionButtonVariant variant = OfficialActionButtonVariant.Standard)
        {
            EnsureOfficialUiStyles(visualTheme);

            Rect rect = GUILayoutUtility.GetRect(1f, 24f, GUILayout.ExpandWidth(true));
            GUIStyle buttonStyle = GetOfficialActionButtonStyle(variant);
            using (new EditorGUI.DisabledScope(!enabled))
            {
                if (GUI.Button(rect, GUIContent.none, buttonStyle) && enabled)
                {
                    action?.Invoke();
                }
            }

            Texture iconTexture = string.IsNullOrEmpty(iconName) ? null : EditorGUIUtility.IconContent(iconName).image;
            Rect contentRect = new Rect(rect.x + 8f, rect.y, rect.width - 16f, rect.height);
            Rect iconRect = new Rect(contentRect.x, rect.y + 4f, 14f, 14f);
            Rect labelRect = new Rect(contentRect.x + (iconTexture != null ? 20f : 0f), rect.y, Mathf.Max(0f, contentRect.width - 28f), rect.height);
            Rect chevronRect = new Rect(rect.xMax - 16f, rect.y, 8f, rect.height);

            Color previousColor = GUI.color;
            if (!enabled)
            {
                GUI.color = new Color(previousColor.r, previousColor.g, previousColor.b, previousColor.a * 0.55f);
            }

            if (iconTexture != null)
            {
                GUI.DrawTexture(iconRect, iconTexture, ScaleMode.ScaleToFit, true);
            }

            GUI.Label(labelRect, label, officialActionButtonLabelStyle);
            GUI.Label(chevronRect, "›", officialActionChevronStyle);
            GUI.color = previousColor;
        }

        private static void DrawOfficialChipRow(RoadGeneratorOfficialTheme visualTheme, float availableWidth, params (string key, string value)[] items)
        {
            if (items == null || items.Length == 0)
            {
                return;
            }

            float flowHeight = MeasureOfficialChipFlowHeight(availableWidth, items);
            Rect flowRect = GUILayoutUtility.GetRect(1f, flowHeight, GUILayout.ExpandWidth(true));
            DrawOfficialChipFlow(flowRect, visualTheme, items);
        }

        private static void DrawOfficialEmptyState(RoadGeneratorOfficialTheme visualTheme, string text)
        {
            EditorGUILayout.BeginVertical(officialEmptyStateStyle);
            GUILayout.Label(text, officialSectionSubtitleStyle);
            EditorGUILayout.EndVertical();
        }

        internal static void DrawOfficialChipFlow(Rect rect, RoadGeneratorOfficialTheme visualTheme, params (string key, string value)[] items)
        {
            if (items == null || items.Length == 0)
            {
                return;
            }

            EnsureOfficialUiStyles(visualTheme);
            OfficialChipLayoutItem[] layoutItems = BuildOfficialChipLayout(Mathf.Max(OfficialChipMinWidth, rect.width), items);
            float cursorX = rect.x;
            float cursorY = rect.y;
            float rowHeight = 0f;
            float maxX = rect.xMax;

            for (int index = 0; index < layoutItems.Length; index++)
            {
                OfficialChipLayoutItem item = layoutItems[index];
                if (cursorX + item.Width > maxX && cursorX > rect.x)
                {
                    cursorX = rect.x;
                    cursorY += rowHeight + OfficialChipSpacing;
                    rowHeight = 0f;
                }

                Rect chipRect = new Rect(cursorX, cursorY, item.Width, item.Height);
                EditorGUI.DrawRect(chipRect, visualTheme.ChipFillColor);
                DrawRectOutline(chipRect, visualTheme.ChipBorderColor, 1f);

                float contentY = chipRect.y + Mathf.Floor((chipRect.height - Mathf.Max(officialChipKeyStyle.lineHeight, officialChipValueStyle.lineHeight)) * 0.5f);
                Rect keyRect = new Rect(chipRect.x + OfficialChipHorizontalPadding, contentY, item.KeyWidth, chipRect.height);
                Rect valueRect = new Rect(keyRect.xMax + OfficialChipInlineGap, contentY, Mathf.Max(0f, chipRect.width - (keyRect.xMax - chipRect.x) - OfficialChipInlineGap - OfficialChipHorizontalPadding), chipRect.height);
                GUI.Label(keyRect, item.Key, officialChipKeyStyle);
                GUI.Label(valueRect, item.Value, officialChipValueStyle);

                cursorX += item.Width + OfficialChipSpacing;
                rowHeight = Mathf.Max(rowHeight, item.Height);
            }
        }

        internal static float MeasureOfficialChipFlowHeight(float availableWidth, params (string key, string value)[] items)
        {
            if (items == null || items.Length == 0)
            {
                return 0f;
            }

            EnsureOfficialUiStyles();
            OfficialChipLayoutItem[] layoutItems = BuildOfficialChipLayout(Mathf.Max(OfficialChipMinWidth, availableWidth), items);
            float cursorX = 0f;
            float rowHeight = 0f;
            float totalHeight = 0f;
            float maxWidth = Mathf.Max(OfficialChipMinWidth, availableWidth);

            for (int index = 0; index < layoutItems.Length; index++)
            {
                OfficialChipLayoutItem item = layoutItems[index];
                if (cursorX + item.Width > maxWidth && cursorX > 0f)
                {
                    totalHeight += rowHeight + OfficialChipSpacing;
                    cursorX = 0f;
                    rowHeight = 0f;
                }

                cursorX += item.Width + OfficialChipSpacing;
                rowHeight = Mathf.Max(rowHeight, item.Height);
            }

            return totalHeight + rowHeight;
        }

        private static OfficialChipLayoutItem[] BuildOfficialChipLayout(float availableWidth, IReadOnlyList<(string key, string value)> items)
        {
            if (items == null || items.Count == 0)
            {
                return Array.Empty<OfficialChipLayoutItem>();
            }

            float chipMaxWidth = Mathf.Max(OfficialChipMinWidth, Mathf.Min(OfficialChipMaxWidth, availableWidth));
            OfficialChipLayoutItem[] layoutItems = new OfficialChipLayoutItem[items.Count];
            for (int index = 0; index < items.Count; index++)
            {
                string rawKey = string.IsNullOrWhiteSpace(items[index].key) ? "Info" : items[index].key;
                string rawValue = string.IsNullOrWhiteSpace(items[index].value) ? "None" : items[index].value;
                string displayKey = FitTextToWidth(officialChipKeyStyle, $"{rawKey}:", Mathf.Min(84f, chipMaxWidth * 0.45f));
                float keyWidth = officialChipKeyStyle.CalcSize(new GUIContent(displayKey)).x;
                float maxValueWidth = Mathf.Max(24f, chipMaxWidth - OfficialChipHorizontalPadding * 2f - keyWidth - OfficialChipInlineGap);
                string displayValue = FitTextToWidth(officialChipValueStyle, rawValue, maxValueWidth);
                Vector2 keySize = officialChipKeyStyle.CalcSize(new GUIContent(displayKey));
                Vector2 valueSize = officialChipValueStyle.CalcSize(new GUIContent(displayValue));
                float chipWidth = Mathf.Clamp(
                    keySize.x + OfficialChipInlineGap + valueSize.x + OfficialChipHorizontalPadding * 2f,
                    OfficialChipMinWidth,
                    chipMaxWidth);
                float chipHeight = Mathf.Max(OfficialChipMinHeight, OfficialChipTopPadding + Mathf.Max(keySize.y, valueSize.y) + OfficialChipBottomPadding);
                layoutItems[index] = new OfficialChipLayoutItem(displayKey, displayValue, chipWidth, chipHeight, keySize.x, valueSize.x);
            }

            return layoutItems;
        }

        private static string FitTextToWidth(GUIStyle style, string text, float maxWidth)
        {
            if (string.IsNullOrEmpty(text) || maxWidth <= 0f)
            {
                return text;
            }

            if (style.CalcSize(new GUIContent(text)).x <= maxWidth)
            {
                return text;
            }

            const string ellipsis = "…";
            if (style.CalcSize(new GUIContent(ellipsis)).x > maxWidth)
            {
                return string.Empty;
            }

            int low = 0;
            int high = text.Length;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                string candidate = $"{text.Substring(0, mid)}{ellipsis}";
                if (style.CalcSize(new GUIContent(candidate)).x <= maxWidth)
                {
                    low = mid;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return low <= 0 ? ellipsis : $"{text.Substring(0, low)}{ellipsis}";
        }

        private static RoadInfo DrawCustomRoadInfoSelector(string label, RoadInfo selectedInfo)
        {
            EnsureRoadTypeSelectorStyles();

            EditorGUILayout.LabelField(label, roadTypeSelectorLabelStyle);
            Rect cardRect = GUILayoutUtility.GetRect(0f, SelectedRoadTypeCardHeight, GUILayout.ExpandWidth(true));

            if (DrawCustomRoadInfoCard(cardRect, selectedInfo, isSelected: true, isCompact: false, footerText: "点击选择官方示例的自定义道路信息"))
            {
                PopupWindow.Show(GUIUtility.GUIToScreenRect(cardRect), new CustomRoadInfoSelectionPopup());
            }

            GUILayout.Space(4f);
            return selectedCustomRoadInfo;
        }

        private static bool DrawCustomRoadInfoCard(Rect rect, RoadInfo roadInfo, bool isSelected, bool isCompact, string footerText)
        {
            EnsureRoadTypeSelectorStyles();

            bool isHovered = rect.Contains(Event.current.mousePosition);
            RoadGeneratorOfficialTheme visualTheme = RoadGeneratorOfficialTheme.Resolve(GetOfficialTheme(officialUiState.ActiveTab));
            Color accent = visualTheme.AccentColor;
            Color backgroundColor = isSelected
                ? visualTheme.NavActiveFillColor
                : isHovered ? visualTheme.NavHoverFillColor : visualTheme.CardBackgroundColor;
            Color borderColor = isSelected ? visualTheme.NavActiveBorderColor : visualTheme.CardBorderColor;

            EditorGUI.DrawRect(rect, backgroundColor);
            DrawRectOutline(rect, borderColor, isSelected ? 2f : 1f);

            Rect thumbnailRect = new Rect(rect.x + 8f, rect.y + 8f, isCompact ? 94f : 106f, rect.height - 16f);
            Texture2D thumbnail = GetCustomRoadInfoThumbnail(roadInfo);
            if (thumbnail != null)
            {
                GUI.DrawTexture(thumbnailRect, thumbnail, ScaleMode.StretchToFill, false);
            }

            DrawRectOutline(thumbnailRect, visualTheme.DividerColor, 1f);

            Rect contentRect = new Rect(thumbnailRect.xMax + 10f, rect.y + 8f, rect.width - thumbnailRect.width - 34f, rect.height - 16f);
            Rect titleRect = new Rect(contentRect.x, contentRect.y, contentRect.width, isCompact ? 34f : 38f);
            Rect summaryRect = new Rect(contentRect.x, rect.yMax - 34f, contentRect.width, 14f);
            Rect footerRect = new Rect(contentRect.x, rect.yMax - 18f, contentRect.width, 14f);

            GUI.Label(titleRect, GetCustomRoadInfoDisplayName(roadInfo), roadTypeCardTitleStyle);
            GUI.Label(summaryRect, GetCustomRoadInfoSummary(roadInfo), roadTypeCardSummaryStyle);

            if (!string.IsNullOrEmpty(footerText))
            {
                GUI.Label(footerRect, footerText, roadTypeCardHintStyle);
            }

            if (isSelected)
            {
                Rect badgeRect = new Rect(rect.xMax - 54f, rect.y + 8f, 42f, 18f);
                EditorGUI.DrawRect(badgeRect, new Color(accent.r, accent.g, accent.b, 0.9f));
                GUI.Label(badgeRect, roadInfo == null ? "预设" : "已选", roadTypeCardBadgeStyle);
            }
            else
            {
                Rect chevronRect = new Rect(rect.xMax - 22f, rect.y, 12f, rect.height);
                GUI.Label(chevronRect, "›", roadTypeCardChevronStyle);
            }

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        private static Texture2D GetCustomRoadInfoThumbnail(RoadInfo roadInfo)
        {
            if (roadInfo == null)
            {
                return GetRoadTypeThumbnail(selectedRoadType);
            }

            if (customRoadInfoThumbnailCache.TryGetValue(roadInfo, out Texture2D cachedTexture) && cachedTexture != null)
            {
                return cachedTexture;
            }

            Texture2D texture = CreateCustomRoadInfoThumbnail(roadInfo);
            customRoadInfoThumbnailCache[roadInfo] = texture;
            return texture;
        }

        private static Texture2D CreateCustomRoadInfoThumbnail(RoadInfo roadInfo)
        {
            const int textureWidth = 192;
            const int textureHeight = 112;

            Texture2D texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false)
            {
                name = $"CustomRoadInfoThumbnail_{roadInfo.GetInstanceID()}",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color[] pixels = Enumerable.Repeat(new Color(0.09f, 0.10f, 0.11f, 1f), textureWidth * textureHeight).ToArray();
            RectInt contentRect = new RectInt(8, 8, textureWidth - 16, textureHeight - 16);
            FillRect(pixels, textureWidth, textureHeight, contentRect, new Color(0.12f, 0.13f, 0.14f, 1f));

            float laneWidth = Mathf.Max(1f, roadInfo.defaultLaneWidth);
            float sidewalkWidth = Mathf.Max(0f, roadInfo.defaultSidewalkWidth);
            float greenBeltWidth = Mathf.Max(0f, roadInfo.defaultGreenBeltWidth);
            int leftLaneCount = Mathf.Max(0, roadInfo.leftLaneCount);
            int rightLaneCount = Mathf.Max(0, roadInfo.rightLaneCount);
            float totalUnits = (leftLaneCount + rightLaneCount) * laneWidth + (sidewalkWidth * 2f) + greenBeltWidth;
            RectInt roadBody = new RectInt(contentRect.x + 4, contentRect.y + 4, contentRect.width - 8, contentRect.height - 8);

            if (totalUnits <= 0.01f)
            {
                DrawPathThumbnail(pixels, textureWidth, textureHeight, roadBody, RoadType.TRoad_F);
            }
            else
            {
                float cursorUnits = 0f;
                if (sidewalkWidth > 0.01f)
                {
                    RectInt leftSidewalkRect = GetProportionalRect(roadBody, totalUnits, cursorUnits, sidewalkWidth);
                    DrawSidewalkBand(pixels, textureWidth, textureHeight, leftSidewalkRect);
                    cursorUnits += sidewalkWidth;
                }

                for (int laneIndex = 0; laneIndex < leftLaneCount; laneIndex++)
                {
                    RectInt laneRect = GetProportionalRect(roadBody, totalUnits, cursorUnits, laneWidth);
                    DrawLaneBand(pixels, textureWidth, textureHeight, laneRect, drawArrowUp: false);
                    cursorUnits += laneWidth;
                }

                if (greenBeltWidth > 0.01f)
                {
                    RectInt greenBeltRect = GetProportionalRect(roadBody, totalUnits, cursorUnits, greenBeltWidth);
                    DrawGreenBeltBand(pixels, textureWidth, textureHeight, greenBeltRect);
                    cursorUnits += greenBeltWidth;
                }

                for (int laneIndex = 0; laneIndex < rightLaneCount; laneIndex++)
                {
                    RectInt laneRect = GetProportionalRect(roadBody, totalUnits, cursorUnits, laneWidth);
                    DrawLaneBand(pixels, textureWidth, textureHeight, laneRect, drawArrowUp: true);
                    cursorUnits += laneWidth;
                }

                if (sidewalkWidth > 0.01f)
                {
                    RectInt rightSidewalkRect = GetProportionalRect(roadBody, totalUnits, cursorUnits, sidewalkWidth);
                    DrawSidewalkBand(pixels, textureWidth, textureHeight, rightSidewalkRect);
                }
            }

            StrokeRect(pixels, textureWidth, textureHeight, contentRect, new Color(1f, 1f, 1f, 0.06f));
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static string GetSelectedCustomRoadDisplayName()
        {
            return GetCustomRoadInfoDisplayName(selectedCustomRoadInfo);
        }

        private static string GetCustomRoadInfoDisplayName(RoadInfo roadInfo)
        {
            if (roadInfo == null)
            {
                return "使用道路预设";
            }

            if (!string.IsNullOrWhiteSpace(roadInfo.roadCName))
            {
                return $"{roadInfo.roadCName} · {roadInfo.roadName}";
            }

            return string.IsNullOrWhiteSpace(roadInfo.roadName) ? "未命名道路信息" : roadInfo.roadName;
        }

        private static string GetCustomRoadInfoSummary(RoadInfo roadInfo)
        {
            if (roadInfo == null)
            {
                return $"{RoadDefaultsInfors.GetRoadName(selectedRoadType)} · 跟随预设";
            }

            int totalLaneCount = Mathf.Max(0, roadInfo.leftLaneCount) + Mathf.Max(0, roadInfo.rightLaneCount);
            string direction = roadInfo.isOneWay ? "单向" : "双向";
            string greenBeltText = roadInfo.defaultGreenBeltWidth > 0.01f ? " · 绿化带" : string.Empty;
            return $"{direction} {totalLaneCount}车道 · {roadInfo.defaultWidth:0.#}m{greenBeltText}";
        }

        private static OfficialPreviewState ComputePreviewState()
        {
            bool hasPreview = HasPreviewObjects();
            if (!autoPreviewJunctions && !hasPreview)
            {
                return OfficialPreviewState.None;
            }

            if (previewRefreshRequested || (autoPreviewJunctions && !hasPreview))
            {
                return OfficialPreviewState.Dirty;
            }

            return OfficialPreviewState.Ready;
        }

        private static OfficialHandleStatus CaptureSelectedHandleStatus()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                return new OfficialHandleStatus(false, "None", 0, 0, "Hidden");
            }

            ICunningInputHandle handleOwner = selected.GetComponentsInParent<MonoBehaviour>(true).OfType<ICunningInputHandle>().FirstOrDefault();
            if (handleOwner == null)
            {
                return new OfficialHandleStatus(false, selected.name, 0, 0, "None");
            }

            ulong handle = handleOwner.CurrentHandle;
            ulong dirtyId = handle != 0 ? NativeMethods.cunning_geo_get_dirty_id(handle) : 0;

            var selectedMesh = selected.GetComponentsInParent<CunningMesh>(true).FirstOrDefault();
            string syncState;
            if (handle == 0)
            {
                syncState = "Pending";
            }
            else if (selectedMesh != null && selectedMesh.GetComponent<MeshFilter>()?.sharedMesh != null)
            {
                syncState = "Displayed";
            }
            else
            {
                syncState = "Handle";
            }

            return new OfficialHandleStatus(true, selected.name, handle, dirtyId, syncState);
        }

        private static OfficialTabTheme GetOfficialTheme(OfficialTab tab)
        {
            for (int index = 0; index < officialTabThemes.Length; index++)
            {
                if (officialTabThemes[index].Tab == tab)
                {
                    return officialTabThemes[index];
                }
            }

            return officialTabThemes[0];
        }

        private static void EnsureOfficialUiStyles()
        {
            RoadGeneratorOfficialTheme visualTheme = RoadGeneratorOfficialTheme.Resolve(GetOfficialTheme(officialUiState.ActiveTab));
            EnsureOfficialUiStyles(visualTheme);
        }

        private static void EnsureOfficialUiStyles(RoadGeneratorOfficialTheme visualTheme)
        {
            string themeCacheKey = $"{ColorUtility.ToHtmlStringRGBA(visualTheme.AccentColor)}|{EditorGUIUtility.isProSkin}";
            if (officialSectionStyle != null && officialStyleCacheKey == themeCacheKey)
            {
                return;
            }

            officialStyleCacheKey = themeCacheKey;
            GUIStyle roundedCardStyle = new GUIStyle(ParadoxNotion.Design.Styles.roundedBox ?? EditorStyles.helpBox)
            {
                border = new RectOffset(8, 8, 8, 8)
            };
            officialSectionStyle = new GUIStyle(roundedCardStyle)
            {
                padding = new RectOffset(10, 10, 10, 10),
                margin = new RectOffset(0, 0, 0, 8)
            };

            officialSectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = visualTheme.PrimaryTextColor }
            };

            officialSectionSubtitleStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                wordWrap = true,
                normal = { textColor = visualTheme.SecondaryTextColor }
            };

            officialSectionMetricStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 10,
                normal = { textColor = visualTheme.PrimaryTextColor }
            };

            officialInlineHintStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                fontSize = 10,
                normal = { textColor = visualTheme.SecondaryTextColor }
            };

            officialListRowStyle = new GUIStyle(roundedCardStyle)
            {
                padding = new RectOffset(8, 8, 5, 5),
                margin = new RectOffset(0, 0, 0, 4),
                normal = { textColor = visualTheme.PrimaryTextColor }
            };

            officialBadgeStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 9,
                normal = { textColor = visualTheme.PrimaryTextColor }
            };

            officialChipStyle = new GUIStyle(roundedCardStyle)
            {
                padding = new RectOffset(6, 6, 2, 2),
                margin = new RectOffset(0, 0, 0, 0),
                normal = { textColor = visualTheme.PrimaryTextColor }
            };

            officialChipKeyStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                fontSize = 9,
                normal = { textColor = visualTheme.MutedTextColor }
            };

            officialChipValueStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                fontSize = 10,
                normal = { textColor = visualTheme.PrimaryTextColor }
            };

            officialEmptyStateStyle = new GUIStyle(roundedCardStyle)
            {
                padding = new RectOffset(10, 10, 6, 6),
                margin = new RectOffset(0, 0, 0, 0),
                normal = { textColor = visualTheme.SecondaryTextColor }
            };
            officialActionButtonLabelStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                fontSize = 10,
                fontStyle = FontStyle.Normal,
                normal = { textColor = visualTheme.PrimaryTextColor }
            };
            officialActionChevronStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = visualTheme.MutedTextColor }
            };
            officialActionButtonStyle = CreateOfficialActionButtonStyle(
                visualTheme.SecondaryButtonFillColor,
                visualTheme.SurfaceBorderColor,
                visualTheme.PrimaryButtonHoverFillColor,
                visualTheme.PrimaryButtonPressedFillColor);
            officialPrimaryActionButtonStyle = CreateOfficialActionButtonStyle(
                new Color(visualTheme.AccentColor.r, visualTheme.AccentColor.g, visualTheme.AccentColor.b, EditorGUIUtility.isProSkin ? 0.18f : 0.12f),
                new Color(visualTheme.AccentColor.r, visualTheme.AccentColor.g, visualTheme.AccentColor.b, EditorGUIUtility.isProSkin ? 0.36f : 0.28f),
                new Color(visualTheme.AccentColor.r, visualTheme.AccentColor.g, visualTheme.AccentColor.b, EditorGUIUtility.isProSkin ? 0.25f : 0.17f),
                new Color(visualTheme.AccentColor.r, visualTheme.AccentColor.g, visualTheme.AccentColor.b, EditorGUIUtility.isProSkin ? 0.32f : 0.22f));
            officialQuietActionButtonStyle = CreateOfficialActionButtonStyle(
                new Color(1f, 1f, 1f, EditorGUIUtility.isProSkin ? 0.02f : 0.015f),
                new Color(1f, 1f, 1f, EditorGUIUtility.isProSkin ? 0.05f : 0.04f),
                new Color(1f, 1f, 1f, EditorGUIUtility.isProSkin ? 0.04f : 0.03f),
                new Color(1f, 1f, 1f, EditorGUIUtility.isProSkin ? 0.06f : 0.05f));
            officialDangerActionButtonStyle = CreateOfficialActionButtonStyle(
                EditorGUIUtility.isProSkin ? new Color(0.72f, 0.22f, 0.22f, 0.14f) : new Color(0.72f, 0.22f, 0.22f, 0.08f),
                EditorGUIUtility.isProSkin ? new Color(0.86f, 0.24f, 0.24f, 0.34f) : new Color(0.86f, 0.24f, 0.24f, 0.22f),
                EditorGUIUtility.isProSkin ? new Color(0.86f, 0.24f, 0.24f, 0.22f) : new Color(0.86f, 0.24f, 0.24f, 0.14f),
                EditorGUIUtility.isProSkin ? new Color(0.86f, 0.24f, 0.24f, 0.30f) : new Color(0.86f, 0.24f, 0.24f, 0.18f));
        }

        private static GUIStyle GetOfficialActionButtonStyle(OfficialActionButtonVariant variant)
        {
            switch (variant)
            {
                case OfficialActionButtonVariant.Primary:
                    return officialPrimaryActionButtonStyle;
                case OfficialActionButtonVariant.Quiet:
                    return officialQuietActionButtonStyle;
                case OfficialActionButtonVariant.Danger:
                    return officialDangerActionButtonStyle;
                default:
                    return officialActionButtonStyle;
            }
        }

        private static GUIStyle CreateOfficialActionButtonStyle(Color normalFill, Color borderColor, Color hoverFill, Color activeFill)
        {
            GUIStyle style = new GUIStyle(EditorStyles.miniButton)
            {
                fixedHeight = 24f,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 4),
                border = new RectOffset(8, 8, 8, 8),
                overflow = new RectOffset(0, 0, 0, 0)
            };

            style.normal.background = RoadGeneratorOfficialThemeResources.GetRoundedRectTexture(normalFill, borderColor, 8, 1);
            style.hover.background = RoadGeneratorOfficialThemeResources.GetRoundedRectTexture(hoverFill, borderColor, 8, 1);
            style.active.background = RoadGeneratorOfficialThemeResources.GetRoundedRectTexture(activeFill, borderColor, 8, 1);
            style.focused.background = style.hover.background;
            style.onNormal.background = style.normal.background;
            style.onHover.background = style.hover.background;
            style.onActive.background = style.active.background;
            style.onFocused.background = style.hover.background;
            return style;
        }

        private static Color ParseHexColor(string html)
        {
            if (ColorUtility.TryParseHtmlString(html, out Color color))
            {
                return color;
            }

            return Color.white;
        }
    }
}
