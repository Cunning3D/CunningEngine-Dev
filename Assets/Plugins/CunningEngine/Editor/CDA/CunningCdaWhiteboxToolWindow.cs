using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEngine;
using CunningEngine;
using CunningEngine.CDA;
using ParadoxNotion.Design;

namespace CunningEngine.Editor.CDA {
    public sealed class CunningCdaWhiteboxToolWindow : EditorWindow {
        const string MENU_PATH = "Procedural/Cunning Engine/Debug/CDA/CDA Tools";
        static bool s_cacheEnableApiAvailable = true;
        static bool s_cacheEnableApiWarned;

        [Serializable]
        sealed class WhiteParamUiChoice {
            public string label;
            public int value;
        }

        [Serializable]
        sealed class WhiteParamUi {
            public string kind;
            public float? min;
            public float? max;
            public bool? showAlpha;
            public List<WhiteParamUiChoice> choices = new();
        }

        [Serializable]
        sealed class WhitePromotedBinding {
            public string promotedName;
            public string promotedDefaultJson;
            public uint? channel;
        }

        [Serializable]
        sealed class WhiteParamInfo {
            public string name;
            public string baseJson;
            public string currentJson;
            public WhiteParamUi ui;
            public bool isModified;
            public List<WhitePromotedBinding> promotedBindings = new();
        }

        [Serializable]
        sealed class WhiteNodeInfo {
            public string id;
            public string name;
            public string typeId;
            public List<WhiteParamInfo> parameters = new();
            public Vector2 graphPos;
            public List<string> inputPorts = new();
            public List<string> outputPorts = new();
        }

        [Serializable]
        sealed class WhiteConnInfo {
            public string fromNodeId;
            public string toNodeId;
            public string fromPort;
            public string toPort;
        }

        [MenuItem(MENU_PATH, false, 220)]
        static void OpenWindow() {
            var win = GetWindow<CunningCdaWhiteboxToolWindow>("CDA Tools");
            win.minSize = new Vector2(1140, 620);
            win.Show();
        }

        CunningCDAInstance _target;
        bool _followSelection = true;
        string _search = "";
        int _selectedNode = -1;
        readonly List<WhiteNodeInfo> _nodes = new();
        readonly List<WhiteConnInfo> _connections = new();
        readonly Dictionary<string, Rect> _graphNodeRects = new();
        Vector2 _leftScroll;
        Vector2 _rightScroll;
        Vector2 _targetsScroll;
        Vector2 _graphPan = new Vector2(36f, 28f);
        float _graphZoom = 1f;
        bool _graphAutoFramePending = true;
        bool _graphFrameRequested;
        bool _graphUserAdjusted;
        bool _panningGraph;
        Vector2 _graphPanMouseStart;
        Vector2 _graphPanStart;
        int _tabIndex;
        ulong _nodesCdaId;
        ulong _nodesRevision;

        enum GeoAttrClass : uint { Detail = 0, Point = 1, Vertex = 2, Primitive = 3, Edge = 4 }
        enum GeoAttrType : uint {
            Unknown = 0, F32 = 1, Vec2 = 2, Vec3 = 3, Vec4 = 4, I32 = 5, IVec2 = 6,
            BoolU8 = 7, F64 = 8, DVec2 = 9, DVec3 = 10, DVec4 = 11, StringUtf8 = 12, BytesU8 = 13,
        }
        [Serializable]
        sealed class GeoAttrInfo {
            public string name;
            public GeoAttrType type;
            public uint len;
        }
        sealed class GeoTableColumn {
            public string title;
            public string attrName;
            public GeoAttrType type;
            public int component = -1;
        }
        readonly List<GeoAttrInfo> _geoAttrs = new();
        readonly List<string> _geoPreview = new();
        readonly List<GeoTableColumn> _geoTableColumns = new();
        readonly List<string[]> _geoTableRows = new();
        Vector2 _geoAttrScroll;
        Vector2 _geoValueScroll;
        Vector2 _geoTableScroll;
        int _geoSelectedAttr = -1;
        int _geoTableSelectedRow = -1;
        string _geoAttrSearch = "";
        bool _geoTableDirty = true;
        ulong _geoTableCacheHandle;
        ulong _geoTableCacheDirty;
        GeoAttrClass _geoTableCacheClass = GeoAttrClass.Point;
        int _geoTableCacheRows;
        string _geoTableCacheFilter = "";
        GeoAttrClass _geoSelectedClass = GeoAttrClass.Point;
        int _geoPreviewRows = 24;
        ulong _geoActiveHandle;
        string _geoLastSyncedNodeId;
        ulong _geoLastSyncedGen;
        bool _geoAutoRefreshPending;
        ulong _geoJobId;
        uint _geoJobLastStatus;
        string _geoJobNodeId;
        ulong _geoJobTargetGen;
        bool _geoScenePreviewEnabled = true;
        int _geoScenePreviewMax = 120;
        Color _geoScenePreviewColor = new Color(0.96f, 0.90f, 0.30f, 1f);
        bool _geoScenePreviewArmed;
        string _geoScenePreviewAttrName;
        GeoAttrClass _geoScenePreviewAttrClass;
        ulong _geoSceneCacheHandle;
        ulong _geoSceneCacheDirty;
        string _geoSceneCacheAttr;
        int _geoSceneCacheMax;
        Vector3[] _geoScenePoints = Array.Empty<Vector3>();
        string[] _geoSceneTexts = Array.Empty<string>();

        string _status;
        MessageType _statusType = MessageType.Info;

        GUIStyle _cardStyle;
        GUIStyle _subCardStyle;
        GUIStyle _heroStyle;
        GUIStyle _heroTitleStyle;
        GUIStyle _subtleStyle;
        GUIStyle _nodeButtonStyle;
        GUIStyle _nodeButtonActiveStyle;
        GUIStyle _modifiedParamNameStyle;
        GUIStyle _modifiedBadgeStyle;
        GUIStyle _tabButtonLeftStyle;
        GUIStyle _tabButtonMidStyle;
        GUIStyle _tabButtonRightStyle;
        GUIStyle _searchFieldStyle;
        GUIStyle _searchCancelStyle;
        GUIStyle _toolbarButtonStyle;
        GUIStyle _graphNodeTitleStyle;
        GUIStyle _graphNodeTypeStyle;
        GUIStyle _chipStyle;
        GUIStyle _heroStatLabelStyle;
        GUIStyle _heroStatValueStyle;
        GUIStyle _tableHeaderStyle;
        GUIStyle _tableCellStyle;
        GUIStyle _tableIndexStyle;
        static readonly Color ModifiedOrange = new Color(0.96f, 0.60f, 0.18f);

        void OnEnable() {
            Selection.selectionChanged += OnSelectionChanged;
            SceneView.duringSceneGui += OnSceneGui;
            InitStyles();
            if (_target == null && _followSelection) {
                _target = ResolveFromSelection();
            }
            SetNodeGeoCacheEnabledSafe(_target, true);
        }

        void OnDisable() {
            Selection.selectionChanged -= OnSelectionChanged;
            SceneView.duringSceneGui -= OnSceneGui;
            SetNodeGeoCacheEnabledSafe(_target, false);
            ClearGeometryActiveHandle();
        }

        void OnSelectionChanged() {
            if (!_followSelection) return;
            var next = ResolveFromSelection();
            if (next == _target) return;
            SetNodeGeoCacheEnabledSafe(_target, false);
            _target = next;
            SetNodeGeoCacheEnabledSafe(_target, true);
            ResetNodeDataAndSelection();
            ClearGeometryActiveHandle();
            Repaint();
        }

        void InitStyles() {
            var baseCardStyle = Styles.roundedBox ?? EditorStyles.helpBox;
            _cardStyle ??= new GUIStyle(baseCardStyle) {
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(0, 0, 0, 8),
            };
            _subCardStyle ??= new GUIStyle(baseCardStyle) {
                padding = new RectOffset(8, 8, 6, 6),
                margin = new RectOffset(0, 0, 0, 6),
            };
            _heroStyle ??= new GUIStyle(baseCardStyle) {
                padding = new RectOffset(12, 12, 10, 10),
                margin = new RectOffset(0, 0, 0, 8),
            };
            _heroTitleStyle ??= new GUIStyle(Styles.leftLabel) {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
            };
            _subtleStyle ??= new GUIStyle(Styles.leftLabel) {
                fontSize = 10,
                wordWrap = true,
                normal = { textColor = EditorGUIUtility.isProSkin ? Colors.Grey(0.70f) : Colors.Grey(0.35f) },
            };
            _nodeButtonStyle ??= new GUIStyle(Styles.buttonMid) {
                alignment = TextAnchor.MiddleLeft,
                fixedHeight = 24f,
                padding = new RectOffset(8, 8, 3, 3),
            };
            _nodeButtonActiveStyle ??= new GUIStyle(_nodeButtonStyle);
            _nodeButtonActiveStyle.normal.textColor = EditorGUIUtility.isProSkin ? Colors.lightOrange : new Color(0.12f, 0.36f, 0.68f);
            _nodeButtonActiveStyle.fontStyle = FontStyle.Bold;
            _modifiedParamNameStyle ??= new GUIStyle(EditorStyles.boldLabel) {
                normal = { textColor = ModifiedOrange }
            };
            _modifiedBadgeStyle ??= new GUIStyle(EditorStyles.miniBoldLabel) {
                normal = { textColor = ModifiedOrange }
            };
            _tabButtonLeftStyle ??= new GUIStyle(Styles.buttonLeft) { fixedHeight = 24, fontStyle = FontStyle.Bold };
            _tabButtonMidStyle ??= new GUIStyle(Styles.buttonMid) { fixedHeight = 24, fontStyle = FontStyle.Bold };
            _tabButtonRightStyle ??= new GUIStyle(Styles.buttonRight) { fixedHeight = 24, fontStyle = FontStyle.Bold };
            _searchFieldStyle ??= Styles.toolbarSearchTextField != null ? new GUIStyle(Styles.toolbarSearchTextField) : new GUIStyle("ToolbarSearchTextField");
            _searchCancelStyle ??= Styles.toolbarSearchCancelButton != null ? new GUIStyle(Styles.toolbarSearchCancelButton) : new GUIStyle("ToolbarSearchCancelButton");
            _toolbarButtonStyle ??= new GUIStyle(EditorStyles.toolbarButton) { fixedHeight = 22f };
            _graphNodeTitleStyle ??= new GUIStyle(EditorStyles.boldLabel) {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
            };
            _graphNodeTypeStyle ??= new GUIStyle(_subtleStyle) {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
            };
            _chipStyle ??= new GUIStyle(EditorStyles.miniButtonMid) {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fixedHeight = 20f,
                padding = new RectOffset(8, 8, 2, 2),
            };
            _heroStatLabelStyle ??= new GUIStyle(_subtleStyle) {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 9,
            };
            _heroStatValueStyle ??= new GUIStyle(EditorStyles.boldLabel) {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 12,
            };
            _tableHeaderStyle ??= new GUIStyle(EditorStyles.miniBoldLabel) {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
            };
            _tableCellStyle ??= new GUIStyle(EditorStyles.miniLabel) {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
            };
            _tableIndexStyle ??= new GUIStyle(_tableCellStyle) {
                alignment = TextAnchor.MiddleRight,
                fontStyle = FontStyle.Bold,
            };
        }

        void OnGUI() {
            InitStyles();

            DrawHero();
            DrawTopToolbar();

            if (_target == null) {
                EditorGUILayout.HelpBox("Select a CDA instance in Hierarchy, or assign one above.", MessageType.Info);
                return;
            }

            AutoSyncNodesAndGeometry();
            DrawStatusBar();

            bool isBlackBox = IsBlackBox(_target);
            EditorGUILayout.Space(4);

            if (_tabIndex == 0) {
                DrawReadonlyNodeEditorAndGeometry(isBlackBox);
                return;
            }
            if (_tabIndex == 1) {
                if (isBlackBox) {
                    EditorGUILayout.HelpBox("Current CDA is BlackBox. Internal node/param inspection is disabled.", MessageType.Info);
                    return;
                }
                DrawMainContent();
                return;
            }

            DrawDataTargetsPanel();
        }

        void DrawHero() {
            EditorGUILayout.BeginVertical(_heroStyle);
            var accentRect = GUILayoutUtility.GetRect(0f, 3f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(accentRect, EditorGUIUtility.isProSkin ? new Color(0.20f, 0.55f, 0.98f, 0.95f) : new Color(0.18f, 0.48f, 0.90f, 0.95f));
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("CDA Tools", _heroTitleStyle);
            GUILayout.FlexibleSpace();
            DrawChip($"Nodes: {_nodes.Count}");
            DrawChip($"Conns: {_connections.Count}");
            string sel = _selectedNode >= 0 && _selectedNode < _nodes.Count ? (_nodes[_selectedNode].name ?? _nodes[_selectedNode].id ?? "-") : "-";
            DrawChip($"Selected: {sel}");
            EditorGUILayout.EndHorizontal();
            GUILayout.Label("Read-only node graph + geometry sheet + white-box parameter inspection.", _subtleStyle);
            EditorGUILayout.EndVertical();
        }

        void DrawChip(string text) {
            if (string.IsNullOrWhiteSpace(text)) return;
            GUILayout.Box(text, _chipStyle, GUILayout.Height(20), GUILayout.MinWidth(120));
        }

        void DrawTopToolbar() {
            EditorGUILayout.BeginVertical(_cardStyle);

            // Row 1: target selection
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Instance", _subtleStyle, GUILayout.Width(54));
            var next = (CunningCDAInstance)EditorGUILayout.ObjectField(_target, typeof(CunningCDAInstance), true, GUILayout.MinWidth(220));
            if (next != _target) {
                SetNodeGeoCacheEnabledSafe(_target, false);
                _target = next;
                SetNodeGeoCacheEnabledSafe(_target, true);
                ResetNodeDataAndSelection();
                ClearGeometryActiveHandle();
            }

            if (GUILayout.Button("Use Selected", _toolbarButtonStyle, GUILayout.Width(96))) {
                var sel = ResolveFromSelection();
                if (sel != null) {
                    SetNodeGeoCacheEnabledSafe(_target, false);
                    _target = sel;
                    SetNodeGeoCacheEnabledSafe(_target, true);
                    ResetNodeDataAndSelection();
                    ClearGeometryActiveHandle();
                } else {
                    SetStatus("No CDA instance found in current selection.", MessageType.Warning);
                }
            }

            _followSelection = GUILayout.Toggle(_followSelection, "Follow", _toolbarButtonStyle, GUILayout.Width(64));
            GUILayout.FlexibleSpace();
            if (_target != null && _target.asset != null) {
                GUILayout.Label(_target.asset.name, _subtleStyle);
            }
            EditorGUILayout.EndHorizontal();

            // Row 2: mode + last cook summary
            if (_target != null && _target.asset != null) {
                EditorGUILayout.BeginHorizontal();
                string hostMode = string.IsNullOrEmpty(_target.asset.access_mode) ? "WhiteBox" : _target.asset.access_mode;
                uint nativeAccess = _target.cdaId != 0 ? NativeMethods.cunning_cda_get_access_mode(_target.cdaId) : 0;
                string nativeMode = _target.cdaId != 0 ? (nativeAccess == 1 ? "BlackBox" : "WhiteBox") : "-";
                bool effectiveBlackBox = (_target.cdaId != 0 && nativeAccess == 1) || _target.asset.IsBlackBox();
                string effectiveMode = effectiveBlackBox ? "BlackBox" : "WhiteBox";
                GUILayout.Label($"Access: {effectiveMode} (host {hostMode}, native {nativeMode})", _subtleStyle);
                GUILayout.FlexibleSpace();
                var st = _target.lastMainJobStats;
                if (_target.lastMainJobId != 0 || st.submitted_ms != 0 || st.compute_ms != 0 || st.finished_ms != 0) {
                    string status = st.status switch {
                        0 => "Pending",
                        1 => "Running",
                        2 => "Ready",
                        3 => "Cancelled",
                        4 => "Failed",
                        _ => st.status.ToString(CultureInfo.InvariantCulture),
                    };
                    ulong queueMs = (st.started_ms > st.submitted_ms) ? (st.started_ms - st.submitted_ms) : 0;
                    ulong totalMs = (st.finished_ms > st.submitted_ms) ? (st.finished_ms - st.submitted_ms) : 0;
                    GUILayout.Label($"Last Cook: {status} · {st.compute_ms}ms compute · {queueMs}ms queue · {totalMs}ms total", _subtleStyle);
                }
                EditorGUILayout.EndHorizontal();
            }

            // Row 3: actions + search
            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_target == null)) {
                GUI.backgroundColor = new Color(0.24f, 0.78f, 0.43f);
                if (GUILayout.Button("Cook", _toolbarButtonStyle, GUILayout.Width(64))) {
                    _target.Cook();
                    _geoAutoRefreshPending = true;
                    SetStatus("Cook submitted.", MessageType.Info);
                }
                GUI.backgroundColor = Color.white;
                if (GUILayout.Button("Refresh Graph", _toolbarButtonStyle, GUILayout.Width(104))) {
                    RefreshNodes();
                    _geoAutoRefreshPending = true;
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label("Search", _subtleStyle, GUILayout.Width(46));
            _search = GUILayout.TextField(_search ?? "", _searchFieldStyle, GUILayout.MinWidth(200));
            if (GUILayout.Button(GUIContent.none, _searchCancelStyle)) _search = "";
            EditorGUILayout.EndHorizontal();

            // Row 4: tabs
            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            int nextTab = _tabIndex;
            if (GUILayout.Toggle(_tabIndex == 0, "Node Editor", _tabButtonLeftStyle, GUILayout.Width(132))) nextTab = 0;
            if (GUILayout.Toggle(_tabIndex == 1, "White Box", _tabButtonMidStyle, GUILayout.Width(132))) nextTab = 1;
            if (GUILayout.Toggle(_tabIndex == 2, "Data Targets", _tabButtonRightStyle, GUILayout.Width(132))) nextTab = 2;
            _tabIndex = nextTab;
            GUILayout.FlexibleSpace();
            var badge = _target != null && _target.jobId != 0
                ? "Cooking..."
                : (_nodes.Count > 0 ? $"Cached Nodes: {_nodes.Count}" : "No Cached Nodes");
            GUILayout.Box(badge, _chipStyle, GUILayout.Height(20), GUILayout.MinWidth(140));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        void DrawStatusBar() {
            if (string.IsNullOrEmpty(_status)) return;
            EditorGUILayout.HelpBox(_status, _statusType);
        }

        void AutoSyncNodesAndGeometry() {
            if (_target == null || _target.asset == null) return;
            EnsureNodesLoadedAuto();
            ProcessGeometryAutoSync();
        }

        void EnsureNodesLoadedAuto() {
            if (!EnsureCdaLoaded(true)) return;
            if (_target == null) return;
            if (IsBlackBox(_target)) {
                if (_nodes.Count > 0 || _connections.Count > 0) {
                    ResetNodeDataAndSelection();
                    ClearGeometryActiveHandle();
                }
                return;
            }
            ulong rev = NativeMethods.cunning_cda_get_revision(_target.cdaId);
            bool stale = _nodes.Count == 0 || _nodesCdaId != _target.cdaId || _nodesRevision != rev;
            if (!stale) return;
            RefreshNodes();
        }

        void DrawReadonlyNodeEditorAndGeometry(bool isBlackBox) {
            bool stack = position.width < 1120f;

            if (stack) {
                EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));

                EditorGUILayout.BeginVertical(_cardStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(360), GUILayout.ExpandHeight(true));
                DrawNodeEditorHeader();
                GUILayout.Label("Middle mouse / Alt+Left drag to pan. Mouse wheel to zoom. Click node to drive geometry preview.", _subtleStyle);
                if (isBlackBox) {
                    EditorGUILayout.HelpBox("BlackBox CDA: internal graph data is hidden.", MessageType.Info);
                } else {
                    DrawGraphCanvas();
                }
                EditorGUILayout.EndVertical();

                GUILayout.Space(8);

                EditorGUILayout.BeginVertical(_cardStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(300), GUILayout.ExpandHeight(true));
                GUILayout.Label("Geometry Sheet", _heroTitleStyle);
                DrawWindowGeometrySheet(isBlackBox);
                EditorGUILayout.EndVertical();

                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            EditorGUILayout.BeginVertical(_cardStyle, GUILayout.ExpandWidth(true), GUILayout.MinWidth(520), GUILayout.MinHeight(460), GUILayout.ExpandHeight(true));
            DrawNodeEditorHeader();
            GUILayout.Label("Middle mouse / Alt+Left drag to pan. Mouse wheel to zoom. Click node to drive geometry preview.", _subtleStyle);
            if (isBlackBox) {
                EditorGUILayout.HelpBox("BlackBox CDA: internal graph data is hidden.", MessageType.Info);
            } else {
                DrawGraphCanvas();
            }
            EditorGUILayout.EndVertical();

            float rightWidth = Mathf.Clamp(position.width * 0.38f, 420f, 680f);
            EditorGUILayout.BeginVertical(_cardStyle, GUILayout.Width(rightWidth), GUILayout.MinHeight(460), GUILayout.ExpandHeight(true));
            GUILayout.Label("Geometry Sheet", _heroTitleStyle);
            DrawWindowGeometrySheet(isBlackBox);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        void DrawNodeEditorHeader() {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Read-only Node Editor", _heroTitleStyle);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(_nodes.Count == 0)) {
                if (GUILayout.Button("Frame", _toolbarButtonStyle, GUILayout.Width(72))) {
                    _graphFrameRequested = true;
                    _graphUserAdjusted = false;
                    Repaint();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        void DrawGraphCanvas() {
            var canvasRect = GUILayoutUtility.GetRect(0f, 100000f, 260f, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            var bg = EditorGUIUtility.isProSkin ? new Color(0.09f, 0.11f, 0.13f, 1f) : new Color(0.86f, 0.88f, 0.91f, 1f);
            EditorGUI.DrawRect(canvasRect, bg);
            HandleGraphPan(canvasRect);

            GUI.BeginClip(canvasRect);
            try {
                var localCanvas = new Rect(0f, 0f, canvasRect.width, canvasRect.height);
                if ((_graphFrameRequested || (_graphAutoFramePending && !_graphUserAdjusted)) && _nodes.Count > 0) {
                    FrameGraphToView(localCanvas);
                    _graphFrameRequested = false;
                    _graphAutoFramePending = false;
                }

                RebuildGraphNodeRects(localCanvas);
                DrawGraphGrid(localCanvas, 20f, EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.035f) : new Color(0f, 0f, 0f, 0.045f));
                DrawGraphGrid(localCanvas, 100f, EditorGUIUtility.isProSkin ? new Color(0.20f, 0.36f, 0.60f, 0.22f) : new Color(0.24f, 0.38f, 0.58f, 0.20f));
                DrawGraphConnections(localCanvas);
                DrawGraphNodes(localCanvas);

                var zoomRect = new Rect(8f, 6f, 86f, 20f);
                EditorGUI.DrawRect(zoomRect, EditorGUIUtility.isProSkin ? new Color(0f, 0f, 0f, 0.36f) : new Color(1f, 1f, 1f, 0.72f));
                GUI.Label(zoomRect, $"{_graphZoom * 100f:0}% ", _tableHeaderStyle);
            } finally {
                GUI.EndClip();
            }
        }

        void FrameGraphToView(Rect canvasRect) {
            if (_nodes.Count == 0) return;

            const float nodeW = 208f;
            const float nodeH = 74f;
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
            for (int i = 0; i < _nodes.Count; i++) {
                var n = _nodes[i];
                if (n == null) continue;
                float x0 = n.graphPos.x;
                float y0 = n.graphPos.y;
                float x1 = x0 + nodeW;
                float y1 = y0 + nodeH;
                if (x0 < minX) minX = x0;
                if (y0 < minY) minY = y0;
                if (x1 > maxX) maxX = x1;
                if (y1 > maxY) maxY = y1;
            }
            if (float.IsInfinity(minX) || float.IsInfinity(minY) || float.IsInfinity(maxX) || float.IsInfinity(maxY)) return;

            float boundsW = Mathf.Max(1f, maxX - minX);
            float boundsH = Mathf.Max(1f, maxY - minY);
            float pad = Mathf.Clamp(Mathf.Min(canvasRect.width, canvasRect.height) * 0.08f, 18f, 64f);
            float availW = Mathf.Max(1f, canvasRect.width - pad * 2f);
            float availH = Mathf.Max(1f, canvasRect.height - pad * 2f);
            float targetZoom = Mathf.Min(availW / boundsW, availH / boundsH);
            _graphZoom = Mathf.Clamp(targetZoom, 0.35f, 2.6f);

            var graphCenter = new Vector2(minX + boundsW * 0.5f, minY + boundsH * 0.5f);
            var canvasCenter = new Vector2(canvasRect.width * 0.5f, canvasRect.height * 0.5f);
            _graphPan = canvasCenter - graphCenter * _graphZoom;
        }

        void RebuildGraphNodeRects(Rect canvasRect) {
            _graphNodeRects.Clear();
            if (_nodes.Count == 0) return;
            for (int i = 0; i < _nodes.Count; i++) {
                var n = _nodes[i];
                if (n == null || string.IsNullOrWhiteSpace(n.id)) continue;
                var rect = GraphRectToCanvas(canvasRect, new Rect(n.graphPos.x, n.graphPos.y, 208f, 74f));
                var localRect = new Rect(rect.x - canvasRect.x, rect.y - canvasRect.y, rect.width, rect.height);
                _graphNodeRects[n.id] = localRect;
            }
        }

        void DrawGraphGrid(Rect canvasRect, float spacing, Color color) {
            if (spacing <= 0f) return;
            float scaledSpacing = spacing * Mathf.Max(0.01f, _graphZoom);
            if (scaledSpacing < 8f) return;
            Handles.BeginGUI();
            var prev = Handles.color;
            Handles.color = color;
            float xOffset = ((_graphPan.x % scaledSpacing) + scaledSpacing) % scaledSpacing;
            float yOffset = ((_graphPan.y % scaledSpacing) + scaledSpacing) % scaledSpacing;
            for (float x = canvasRect.x + xOffset; x < canvasRect.xMax; x += scaledSpacing) {
                Handles.DrawLine(new Vector3(x, canvasRect.y, 0f), new Vector3(x, canvasRect.yMax, 0f));
            }
            for (float y = canvasRect.y + yOffset; y < canvasRect.yMax; y += scaledSpacing) {
                Handles.DrawLine(new Vector3(canvasRect.x, y, 0f), new Vector3(canvasRect.xMax, y, 0f));
            }
            Handles.color = prev;
            Handles.EndGUI();
        }

        void HandleGraphPan(Rect canvasRect) {
            var evt = Event.current;
            if (evt == null) return;

            if (canvasRect.Contains(evt.mousePosition) && evt.type == EventType.ScrollWheel) {
                float oldZoom = _graphZoom;
                float zoomMul = Mathf.Pow(1.08f, -evt.delta.y);
                float newZoom = Mathf.Clamp(oldZoom * zoomMul, 0.35f, 2.6f);
                if (!Mathf.Approximately(newZoom, oldZoom)) {
                    var canvasLocal = evt.mousePosition - new Vector2(canvasRect.x, canvasRect.y);
                    var graphAtMouse = (canvasLocal - _graphPan) / oldZoom;
                    _graphZoom = newZoom;
                    _graphPan = canvasLocal - graphAtMouse * _graphZoom;
                    _graphUserAdjusted = true;
                    _graphAutoFramePending = false;
                    Repaint();
                }
                evt.Use();
                return;
            }

            bool canStartPan = canvasRect.Contains(evt.mousePosition) &&
                               ((evt.button == 2 && evt.type == EventType.MouseDown) ||
                                (evt.button == 0 && evt.alt && evt.type == EventType.MouseDown));
            if (canStartPan) {
                _panningGraph = true;
                _graphPanMouseStart = evt.mousePosition;
                _graphPanStart = _graphPan;
                evt.Use();
                return;
            }
            if (_panningGraph && evt.type == EventType.MouseDrag) {
                _graphPan = _graphPanStart + (evt.mousePosition - _graphPanMouseStart);
                _graphUserAdjusted = true;
                _graphAutoFramePending = false;
                Repaint();
                evt.Use();
                return;
            }
            if (_panningGraph && (evt.type == EventType.MouseUp || evt.type == EventType.Ignore)) {
                _panningGraph = false;
            }
        }

        void DrawGraphConnections(Rect canvasRect) {
            if (_connections.Count == 0 || _nodes.Count == 0) return;
            Handles.BeginGUI();
            var prevColor = Handles.color;
            var selectedNodeId = _selectedNode >= 0 && _selectedNode < _nodes.Count ? _nodes[_selectedNode].id : null;
            for (int i = 0; i < _connections.Count; i++) {
                var c = _connections[i];
                if (c == null) continue;
                if (!_graphNodeRects.TryGetValue(c.fromNodeId ?? "", out var fromRect)) continue;
                if (!_graphNodeRects.TryGetValue(c.toNodeId ?? "", out var toRect)) continue;
                bool activeEdge = !string.IsNullOrWhiteSpace(selectedNodeId) &&
                                  (string.Equals(selectedNodeId, c.fromNodeId, StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(selectedNodeId, c.toNodeId, StringComparison.OrdinalIgnoreCase));
                var p0 = new Vector3(canvasRect.x + fromRect.xMax, canvasRect.y + fromRect.center.y, 0f);
                var p1 = new Vector3(canvasRect.x + toRect.xMin, canvasRect.y + toRect.center.y, 0f);
                float tan = Mathf.Clamp(Mathf.Abs(p1.x - p0.x) * 0.45f, 30f, 120f);
                var t0 = p0 + Vector3.right * tan;
                var t1 = p1 + Vector3.left * tan;
                Handles.color = activeEdge
                    ? new Color(0.35f, 0.76f, 1f, 0.95f)
                    : (EditorGUIUtility.isProSkin ? new Color(0.55f, 0.62f, 0.70f, 0.72f) : new Color(0.30f, 0.40f, 0.52f, 0.65f));
                float edgeWidth = (activeEdge ? 2.8f : 1.8f) * Mathf.Clamp(_graphZoom, 0.7f, 1.35f);
                Handles.DrawBezier(p0, p1, t0, t1, Handles.color, null, edgeWidth);
            }
            Handles.color = prevColor;
            Handles.EndGUI();
        }

        void DrawGraphNodes(Rect canvasRect) {
            if (_nodes.Count == 0) {
                GUI.Label(new Rect(canvasRect.x + 12, canvasRect.y + 12, canvasRect.width - 24, 20), "No nodes loaded yet (cook will auto-populate).", _subtleStyle);
                return;
            }

            for (int i = 0; i < _nodes.Count; i++) {
                var n = _nodes[i];
                if (n == null) continue;
                var rect = GraphRectToCanvas(canvasRect, new Rect(n.graphPos.x, n.graphPos.y, 208f, 74f));
                var localRect = new Rect(rect.x - canvasRect.x, rect.y - canvasRect.y, rect.width, rect.height);
                if (!string.IsNullOrWhiteSpace(n.id)) _graphNodeRects[n.id] = localRect;
                if (!canvasRect.Overlaps(rect)) continue;

                bool active = (i == _selectedNode);
                bool modified = n.parameters != null && n.parameters.Any(x => x != null && x.isModified);
                float shadowOffset = Mathf.Max(1f, 2f * _graphZoom);
                var shadowRect = new Rect(rect.x + shadowOffset, rect.y + shadowOffset, rect.width, rect.height);
                EditorGUI.DrawRect(shadowRect, EditorGUIUtility.isProSkin ? new Color(0f, 0f, 0f, 0.28f) : new Color(0f, 0f, 0f, 0.12f));

                var bodyColor = active
                    ? (EditorGUIUtility.isProSkin ? new Color(0.19f, 0.26f, 0.34f, 1f) : new Color(0.72f, 0.84f, 0.97f, 1f))
                    : (EditorGUIUtility.isProSkin ? new Color(0.16f, 0.19f, 0.23f, 1f) : new Color(0.92f, 0.94f, 0.97f, 1f));
                var headColor = active
                    ? (EditorGUIUtility.isProSkin ? new Color(0.27f, 0.52f, 0.86f, 0.95f) : new Color(0.34f, 0.58f, 0.90f, 0.95f))
                    : (EditorGUIUtility.isProSkin ? new Color(0.22f, 0.24f, 0.29f, 1f) : new Color(0.82f, 0.86f, 0.92f, 1f));
                EditorGUI.DrawRect(rect, bodyColor);
                float headHeight = Mathf.Max(16f, 22f * _graphZoom);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, headHeight), headColor);
                if (modified) {
                    EditorGUI.DrawRect(new Rect(rect.xMax - 6f, rect.y + 1f, 5f, rect.height - 2f), ModifiedOrange);
                }

                var nodeName = string.IsNullOrWhiteSpace(n.name) ? "(Unnamed)" : n.name;
                if (_graphZoom >= 0.65f) {
                    var nameRect = new Rect(rect.x + 8, rect.y + 2, rect.width - 16, 18);
                    var typeRect = new Rect(rect.x + 8, rect.y + 28 * _graphZoom, rect.width - 16, 16);
                    var infoRect = new Rect(rect.x + 8, rect.y + 48 * _graphZoom, rect.width - 16, 16);
                    GUI.Label(nameRect, nodeName, _graphNodeTitleStyle);
                    GUI.Label(typeRect, string.IsNullOrWhiteSpace(n.typeId) ? "Node" : n.typeId, _graphNodeTypeStyle);
                    GUI.Label(infoRect, BuildNodeInfoSummary(n, modified), _subtleStyle);
                } else {
                    var compactRect = new Rect(rect.x + 6, rect.y + 1, rect.width - 12, headHeight - 2);
                    GUI.Label(compactRect, nodeName, _graphNodeTypeStyle);
                }

                if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) {
                    _selectedNode = i;
                    _geoAutoRefreshPending = true;
                    Repaint();
                }
            }
        }

        Rect GraphRectToCanvas(Rect canvasRect, Rect graphRect) {
            var p = GraphToCanvas(canvasRect, graphRect.position);
            return new Rect(p.x, p.y, graphRect.width * _graphZoom, graphRect.height * _graphZoom);
        }

        Vector2 GraphToCanvas(Rect canvasRect, Vector2 graphPos) {
            return new Vector2(
                canvasRect.x + _graphPan.x + graphPos.x * _graphZoom,
                canvasRect.y + _graphPan.y + graphPos.y * _graphZoom
            );
        }

        static string BuildNodeInfoSummary(WhiteNodeInfo node, bool modified) {
            if (node == null) return modified ? "modified" : "";
            var parts = new List<string> {
                $"{node.parameters?.Count ?? 0} params"
            };
            if (node.inputPorts != null && node.inputPorts.Count > 0) parts.Add($"In {FormatPortList(node.inputPorts, 1)}");
            if (node.outputPorts != null && node.outputPorts.Count > 0) parts.Add($"Out {FormatPortList(node.outputPorts, 1)}");
            if (modified) parts.Add("modified");
            return string.Join("  ·  ", parts);
        }

        static string FormatPortList(List<string> ports, int maxCount) {
            if (ports == null || ports.Count == 0) return "-";
            int take = Mathf.Clamp(maxCount, 1, ports.Count);
            string text = string.Join(", ", ports.Take(take));
            if (ports.Count > take) text += $" (+{ports.Count - take})";
            return text;
        }

        void DrawMainContent() {
            var filtered = GetFilteredNodeIndices();
            if (_selectedNode < 0 || _selectedNode >= _nodes.Count || !filtered.Contains(_selectedNode)) {
                _selectedNode = filtered.Count > 0 ? filtered[0] : -1;
            }

            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));

            // Left: node list
            EditorGUILayout.BeginVertical(_cardStyle, GUILayout.Width(320), GUILayout.ExpandHeight(true));
            GUILayout.Label($"Nodes ({filtered.Count})", EditorStyles.boldLabel);
            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);
            if (filtered.Count == 0) {
                GUILayout.Space(8);
                GUILayout.Label("No nodes to show yet. Nodes auto-refresh after cook.", _subtleStyle);
            } else {
                for (int i = 0; i < filtered.Count; i++) {
                    int nodeIndex = filtered[i];
                    var n = _nodes[nodeIndex];
                    bool active = nodeIndex == _selectedNode;
                    bool nodeHasModified = n.parameters != null && n.parameters.Any(x => x != null && x.isModified);
                    var displayName = string.IsNullOrWhiteSpace(n.name) ? "(Unnamed Node)" : n.name;
                    var label = string.IsNullOrWhiteSpace(n.typeId) ? displayName : $"{displayName}  ·  {n.typeId}";
                    var prevColor = GUI.contentColor;
                    if (nodeHasModified) GUI.contentColor = ModifiedOrange;
                    if (GUILayout.Button(label, active ? _nodeButtonActiveStyle : _nodeButtonStyle)) {
                        _selectedNode = nodeIndex;
                        _geoAutoRefreshPending = true;
                    }
                    GUI.contentColor = prevColor;
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            // Right: node detail
            EditorGUILayout.BeginVertical(_cardStyle, GUILayout.ExpandHeight(true));
            if (_selectedNode < 0 || _selectedNode >= _nodes.Count) {
                GUILayout.Space(8);
                GUILayout.Label("Select a node to view parameters.", _subtleStyle);
            } else {
                DrawNodeDetail(_nodes[_selectedNode]);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        void DrawDataTargetsPanel() {
            if (_target == null) return;
            _target.selectedTargets ??= new List<CdaSelectTarget>();
            _target.selectedTargetOutputsJson ??= new Dictionary<string, string>();

            EditorGUILayout.BeginVertical(_cardStyle);
            GUILayout.Label("Data Targets (NodeParam)", _heroTitleStyle);
            GUILayout.Label("Add targets from parameter cards. Fetch to request JSON output for each target (param-only job).", _subtleStyle);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Fetch Targets", _toolbarButtonStyle, GUILayout.Width(96))) {
                if (_target.TrySubmitSelectedTargetsJob(out var submittedJobId, out var error)) {
                    SetStatus($"Targets job submitted: {submittedJobId}", MessageType.Info);
                } else {
                    SetStatus(string.IsNullOrWhiteSpace(error) ? "Failed to submit targets job." : error, MessageType.Warning);
                }
            }
            GUI.enabled = _target.selectedTargets.Count > 0;
            if (GUILayout.Button("Clear Targets", _toolbarButtonStyle, GUILayout.Width(96))) {
                Undo.RecordObject(_target, "Clear CDA Data Targets");
                _target.selectedTargets.Clear();
                _target.selectedTargetOutputsJson.Clear();
                EditorUtility.SetDirty(_target);
                SetStatus("Cleared data targets.", MessageType.Info);
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            GUILayout.Label("Use Node Editor tab to inspect node geometry and attributes.", _subtleStyle);

            _targetsScroll = EditorGUILayout.BeginScrollView(_targetsScroll, GUILayout.MinHeight(120), GUILayout.MaxHeight(260));
            if (_target.selectedTargets.Count == 0) {
                GUILayout.Space(4);
                GUILayout.Label("No targets yet. Use \"Add Data Target\" on a node parameter.", _subtleStyle);
            } else {
                for (int i = 0; i < _target.selectedTargets.Count; i++) {
                    var t = _target.selectedTargets[i];
                    if (t == null) continue;
                    DrawSingleTargetCard(i, t);
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void DrawSingleTargetCard(int index, CdaSelectTarget t) {
            EditorGUILayout.BeginVertical(_subCardStyle);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(BuildTargetOutputKey(t), EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(70))) {
                Undo.RecordObject(_target, "Remove CDA Data Target");
                _target.selectedTargets.RemoveAt(index);
                EditorUtility.SetDirty(_target);
                SetStatus("Removed data target.", MessageType.Info);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }
            EditorGUILayout.EndHorizontal();

            string nodeRef = !string.IsNullOrWhiteSpace(t.node_name)
                ? t.node_name
                : (string.IsNullOrWhiteSpace(t.node_id) ? "(missing node)" : t.node_id);
            string paramRef = string.IsNullOrWhiteSpace(t.param) ? "(missing param)" : t.param;
            GUILayout.Label($"Node: {nodeRef}   Param: {paramRef}", _subtleStyle);

            var outKey = BuildTargetOutputKey(t);
            if (_target.selectedTargetOutputsJson.TryGetValue(outKey, out var json) && !string.IsNullOrWhiteSpace(json)) {
                EditorGUILayout.SelectableLabel(json, EditorStyles.textField, GUILayout.Height(36));
            } else {
                GUILayout.Label("Output: (none yet, click Fetch Targets)", _subtleStyle);
            }
            EditorGUILayout.EndVertical();
        }

        void DrawNodeDetail(WhiteNodeInfo node) {
            GUILayout.Label(string.IsNullOrEmpty(node.name) ? "(Unnamed Node)" : node.name, _heroTitleStyle);
            EditorGUILayout.LabelField("Node ID", node.id);
            EditorGUILayout.LabelField("Type", node.typeId ?? "");
            EditorGUILayout.LabelField("Inputs", FormatPortList(node.inputPorts, 12));
            EditorGUILayout.LabelField("Outputs", FormatPortList(node.outputPorts, 12));
            EditorGUILayout.Space(6);

            if (node.parameters == null || node.parameters.Count == 0) {
                EditorGUILayout.HelpBox("Selected node has no parameters.", MessageType.None);
                return;
            }

            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);
            for (int i = 0; i < node.parameters.Count; i++) {
                DrawParamCard(node, node.parameters[i]);
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawWindowGeometrySheet(bool isBlackBox) {
            if (_target == null || _target.asset == null) {
                EditorGUILayout.HelpBox("No CDA instance/asset.", MessageType.Warning);
                return;
            }
            if (isBlackBox) {
                EditorGUILayout.HelpBox("BlackBox CDA does not expose internal node geometry.", MessageType.Info);
                return;
            }
            if (_selectedNode < 0 || _selectedNode >= _nodes.Count) {
                EditorGUILayout.HelpBox("Select a node in the read-only graph.", MessageType.Info);
                return;
            }
            var node = _nodes[_selectedNode];
            GUILayout.Label($"Selected: {(string.IsNullOrWhiteSpace(node.name) ? node.id : node.name)}", _heroTitleStyle);
            GUILayout.Label($"Node Type: {node.typeId ?? ""}", _subtleStyle);

            if (_geoActiveHandle == 0) {
                EditorGUILayout.HelpBox(_geoJobId != 0 ? "Cooking selected node geometry (debug job)..." : "Waiting for selected node geometry.", MessageType.Info);
                DrawGeometryScenePreviewOptions();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            int cls = GUILayout.Toolbar((int)_geoSelectedClass, new[] { "Detail", "Point", "Vertex", "Primitive", "Edge" }, GUILayout.Height(22));
            if (cls != (int)_geoSelectedClass) {
                _geoSelectedClass = (GeoAttrClass)cls;
                _geoScenePreviewArmed = false;
                _geoScenePreviewAttrName = null;
                RefreshGeometryAttrs();
                InvalidateGeometrySceneCache();
            }
            if (GUILayout.Button("Refresh Attrs", GUILayout.Width(116), GUILayout.Height(22))) RefreshGeometryAttrs();
            EditorGUILayout.EndHorizontal();

            _geoPreviewRows = EditorGUILayout.IntSlider("Preview Rows", _geoPreviewRows, 4, 256);
            uint p = NativeMethods.cunning_geo_get_point_count(_geoActiveHandle);
            uint v = NativeMethods.cunning_geo_get_vertex_count(_geoActiveHandle);
            uint pr = NativeMethods.cunning_geo_get_prim_count(_geoActiveHandle);
            EditorGUILayout.BeginHorizontal();
            DrawGeometrySummaryStat("Points", p, new Color(0.25f, 0.67f, 0.96f, 1f));
            DrawGeometrySummaryStat("Vertices", v, new Color(0.24f, 0.76f, 0.52f, 1f));
            DrawGeometrySummaryStat("Primitives", pr, new Color(0.95f, 0.56f, 0.26f, 1f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Attr Filter", _subtleStyle, GUILayout.Width(70));
            string prevAttrSearch = _geoAttrSearch;
            _geoAttrSearch = GUILayout.TextField(_geoAttrSearch ?? "", _searchFieldStyle, GUILayout.MinWidth(180));
            if (GUILayout.Button(GUIContent.none, _searchCancelStyle)) _geoAttrSearch = "";
            if (!string.Equals(prevAttrSearch ?? "", _geoAttrSearch ?? "", StringComparison.Ordinal)) {
                _geoTableDirty = true;
            }
            GUILayout.Space(8);
            GUILayout.Label("Label Attr", _subtleStyle, GUILayout.Width(66));
            var attrOptions = _geoAttrs.Select(a => a.name).ToArray();
            EditorGUI.BeginChangeCheck();
            int nextSelectedAttr = attrOptions.Length == 0
                ? -1
                : EditorGUILayout.Popup(Mathf.Clamp(_geoSelectedAttr, 0, Math.Max(0, attrOptions.Length - 1)), attrOptions, GUILayout.MinWidth(180));
            if (EditorGUI.EndChangeCheck() && nextSelectedAttr != _geoSelectedAttr) {
                _geoSelectedAttr = nextSelectedAttr;
                BuildGeometryPreview();
                ArmScenePreviewForSelectedAttr();
                InvalidateGeometrySceneCache();
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();

            DrawGeometrySpreadsheetTable();

            if (_geoSelectedAttr >= 0 && _geoSelectedAttr < _geoAttrs.Count) {
                var selected = _geoAttrs[_geoSelectedAttr];
                GUILayout.Label($"Active Attribute: {selected.name}  ({selected.type}, len={selected.len})", _subtleStyle);
                _geoValueScroll = EditorGUILayout.BeginScrollView(_geoValueScroll, GUILayout.MinHeight(58), GUILayout.MaxHeight(120));
                int maxRows = Math.Min(_geoPreview.Count, 6);
                for (int i = 0; i < maxRows; i++) {
                    EditorGUILayout.SelectableLabel(_geoPreview[i], EditorStyles.textField, GUILayout.Height(18));
                }
                if (_geoPreview.Count == 0) GUILayout.Label("No preview rows for current attribute.", _subtleStyle);
                EditorGUILayout.EndScrollView();
            }

            DrawGeometryScenePreviewOptions();
        }

        void DrawGeometrySummaryStat(string label, uint value, Color tint) {
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = tint;
            GUILayout.Box($"{label}: {value}", _chipStyle, GUILayout.ExpandWidth(true), GUILayout.Height(20));
            GUI.backgroundColor = prevBg;
        }

        List<int> GetFilteredGeometryAttrIndices() {
            var result = new List<int>();
            string s = (_geoAttrSearch ?? "").Trim();
            bool hasFilter = !string.IsNullOrEmpty(s);
            for (int i = 0; i < _geoAttrs.Count; i++) {
                var a = _geoAttrs[i];
                if (!hasFilter) {
                    result.Add(i);
                    continue;
                }
                if ((a.name ?? "").IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    a.type.ToString().IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) {
                    result.Add(i);
                }
            }
            return result;
        }

        void DrawGeometrySpreadsheetTable() {
            EnsureGeometrySpreadsheetTable();
            EditorGUILayout.BeginVertical(_subCardStyle);
            GUILayout.Label("Geometry Spreadsheet", _heroTitleStyle);
            if (_geoTableColumns.Count == 0 || _geoTableRows.Count == 0) {
                GUILayout.Label("No rows/columns to display for this class. Try switching class or cooking node output.", _subtleStyle);
                EditorGUILayout.EndVertical();
                return;
            }

            const float headerHeight = 22f;
            const float rowHeight = 20f;
            const float indexWidth = 58f;
            const float colWidth = 118f;

            float contentWidth = indexWidth + (_geoTableColumns.Count - 1) * colWidth;
            float contentHeight = _geoTableRows.Count * rowHeight;
            float viewportHeight = Mathf.Clamp(_geoTableRows.Count * rowHeight + headerHeight + 8f, 160f, 300f);
            var outer = GUILayoutUtility.GetRect(10f, viewportHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(outer, EditorGUIUtility.isProSkin ? new Color(0.08f, 0.10f, 0.13f, 0.88f) : new Color(0.94f, 0.95f, 0.97f, 1f));

            var headerRect = new Rect(outer.x, outer.y, outer.width, headerHeight);
            var bodyRect = new Rect(outer.x, outer.y + headerHeight, outer.width, outer.height - headerHeight);
            var viewRect = new Rect(0, 0, Mathf.Max(contentWidth, bodyRect.width - 1f), Mathf.Max(contentHeight, bodyRect.height - 1f));

            _geoTableScroll = GUI.BeginScrollView(bodyRect, _geoTableScroll, viewRect, true, true);
            for (int r = 0; r < _geoTableRows.Count; r++) {
                float y = r * rowHeight;
                var rowRect = new Rect(0, y, viewRect.width, rowHeight);
                bool selected = r == _geoTableSelectedRow;
                var rowColor = selected
                    ? (EditorGUIUtility.isProSkin ? new Color(0.24f, 0.46f, 0.72f, 0.35f) : new Color(0.35f, 0.58f, 0.88f, 0.28f))
                    : (r % 2 == 0
                        ? (EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.018f) : new Color(0f, 0f, 0f, 0.02f))
                        : (EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.05f) : new Color(0f, 0f, 0f, 0.04f)));
                EditorGUI.DrawRect(rowRect, rowColor);
                if (GUI.Button(rowRect, GUIContent.none, GUIStyle.none)) {
                    _geoTableSelectedRow = r;
                }

                var cells = _geoTableRows[r];
                float x = 0f;
                for (int c = 0; c < _geoTableColumns.Count; c++) {
                    float w = c == 0 ? indexWidth : colWidth;
                    var textRect = new Rect(x + 4f, y + 2f, w - 8f, rowHeight - 4f);
                    GUI.Label(textRect, c < cells.Length ? (cells[c] ?? "") : "", c == 0 ? _tableIndexStyle : _tableCellStyle);
                    x += w;
                }
            }
            GUI.EndScrollView();

            GUI.BeginGroup(headerRect);
            EditorGUI.DrawRect(new Rect(0, 0, headerRect.width, headerRect.height), EditorGUIUtility.isProSkin ? new Color(0.16f, 0.20f, 0.26f, 0.95f) : new Color(0.82f, 0.88f, 0.96f, 0.98f));
            float hx = -_geoTableScroll.x;
            for (int c = 0; c < _geoTableColumns.Count; c++) {
                float w = c == 0 ? indexWidth : colWidth;
                var cellRect = new Rect(hx, 0, w, headerHeight);
                if (cellRect.xMax >= 0 && cellRect.x <= headerRect.width) {
                    EditorGUI.DrawRect(new Rect(cellRect.xMax - 1f, 0, 1f, headerHeight), EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.08f) : new Color(0f, 0f, 0f, 0.08f));
                    GUI.Label(new Rect(cellRect.x + 4f, 2f, cellRect.width - 8f, headerHeight - 4f), _geoTableColumns[c].title, _tableHeaderStyle);
                }
                hx += w;
            }
            GUI.EndGroup();

            GUILayout.Label($"Rows: {_geoTableRows.Count}    Columns: {_geoTableColumns.Count - 1}", _subtleStyle);
            EditorGUILayout.EndVertical();
        }

        void EnsureGeometrySpreadsheetTable() {
            if (_geoActiveHandle == 0) {
                _geoTableColumns.Clear();
                _geoTableRows.Clear();
                _geoTableSelectedRow = -1;
                _geoTableDirty = true;
                return;
            }
            ulong dirty = NativeMethods.cunning_geo_get_dirty_id(_geoActiveHandle);
            string filter = (_geoAttrSearch ?? "").Trim();
            bool needsRebuild = _geoTableDirty
                || _geoTableCacheHandle != _geoActiveHandle
                || _geoTableCacheDirty != dirty
                || _geoTableCacheClass != _geoSelectedClass
                || _geoTableCacheRows != _geoPreviewRows
                || !string.Equals(_geoTableCacheFilter ?? "", filter ?? "", StringComparison.Ordinal);
            if (!needsRebuild) return;
            RebuildGeometrySpreadsheetTable();
            _geoTableCacheHandle = _geoActiveHandle;
            _geoTableCacheDirty = dirty;
            _geoTableCacheClass = _geoSelectedClass;
            _geoTableCacheRows = _geoPreviewRows;
            _geoTableCacheFilter = filter;
            _geoTableDirty = false;
        }

        void RebuildGeometrySpreadsheetTable() {
            _geoTableColumns.Clear();
            _geoTableRows.Clear();
            _geoTableSelectedRow = -1;
            if (_geoActiveHandle == 0) return;

            var filteredAttrs = GetFilteredGeometryAttrIndices();
            int classCount = GetGeometryClassRowCount(_geoSelectedClass);
            if (classCount <= 0 && filteredAttrs.Count > 0) {
                classCount = (int)filteredAttrs.Max(i => _geoAttrs[i].len);
            }
            int rows = Mathf.Max(0, Mathf.Min(_geoPreviewRows, classCount));
            if (rows <= 0) return;

            _geoTableColumns.Add(new GeoTableColumn { title = "#", attrName = "", type = GeoAttrType.Unknown, component = -1 });

            int dynamicColumnCount = 1;
            for (int i = 0; i < filteredAttrs.Count; i++) {
                var a = _geoAttrs[filteredAttrs[i]];
                int dim = GetTableComponentCount(a.type);
                if (dim <= 1) {
                    _geoTableColumns.Add(new GeoTableColumn {
                        title = a.name,
                        attrName = a.name,
                        type = a.type,
                        component = -1
                    });
                    dynamicColumnCount++;
                    continue;
                }
                for (int c = 0; c < dim; c++) {
                    _geoTableColumns.Add(new GeoTableColumn {
                        title = $"{a.name}.{GetComponentSuffix(c)}",
                        attrName = a.name,
                        type = a.type,
                        component = c
                    });
                    dynamicColumnCount++;
                }
            }

            if (dynamicColumnCount <= 1) return;

            for (int r = 0; r < rows; r++) {
                var row = new string[_geoTableColumns.Count];
                row[0] = r.ToString(CultureInfo.InvariantCulture);
                for (int c = 1; c < row.Length; c++) row[c] = "-";
                _geoTableRows.Add(row);
            }

            int colCursor = 1;
            uint cls = (uint)_geoSelectedClass;
            for (int i = 0; i < filteredAttrs.Count; i++) {
                var a = _geoAttrs[filteredAttrs[i]];
                int dim = GetTableComponentCount(a.type);
                if (a.type == GeoAttrType.F32 || a.type == GeoAttrType.Vec2 || a.type == GeoAttrType.Vec3 || a.type == GeoAttrType.Vec4) {
                    int cap = rows * Math.Max(dim, 1);
                    var data = new float[Math.Max(cap, 1)];
                    var h = GCHandle.Alloc(data, GCHandleType.Pinned);
                    try {
                        uint copied = NativeMethods.cunning_geo_copy_attr_f32(_geoActiveHandle, cls, a.name, h.AddrOfPinnedObject(), (uint)cap);
                        int rowCount = Math.Min(rows, (int)copied / Math.Max(dim, 1));
                        for (int r = 0; r < rowCount; r++) {
                            for (int c = 0; c < Math.Max(dim, 1); c++) {
                                int cc = dim <= 1 ? 0 : c;
                                _geoTableRows[r][colCursor + cc] = data[r * Math.Max(dim, 1) + c].ToString("0.###", CultureInfo.InvariantCulture);
                            }
                        }
                    } finally { h.Free(); }
                    colCursor += Math.Max(dim, 1);
                    continue;
                }
                if (a.type == GeoAttrType.I32 || a.type == GeoAttrType.IVec2) {
                    int cap = rows * Math.Max(dim, 1);
                    var data = new int[Math.Max(cap, 1)];
                    var h = GCHandle.Alloc(data, GCHandleType.Pinned);
                    try {
                        uint copied = NativeMethods.cunning_geo_copy_attr_i32(_geoActiveHandle, cls, a.name, h.AddrOfPinnedObject(), (uint)cap);
                        int rowCount = Math.Min(rows, (int)copied / Math.Max(dim, 1));
                        for (int r = 0; r < rowCount; r++) {
                            for (int c = 0; c < Math.Max(dim, 1); c++) {
                                int cc = dim <= 1 ? 0 : c;
                                _geoTableRows[r][colCursor + cc] = data[r * Math.Max(dim, 1) + c].ToString(CultureInfo.InvariantCulture);
                            }
                        }
                    } finally { h.Free(); }
                    colCursor += Math.Max(dim, 1);
                    continue;
                }
                if (a.type == GeoAttrType.BoolU8) {
                    var data = new byte[Math.Max(rows, 1)];
                    var h = GCHandle.Alloc(data, GCHandleType.Pinned);
                    try {
                        uint copied = NativeMethods.cunning_geo_copy_attr_u8(_geoActiveHandle, cls, a.name, h.AddrOfPinnedObject(), (uint)rows);
                        int rowCount = Math.Min(rows, (int)copied);
                        for (int r = 0; r < rowCount; r++) _geoTableRows[r][colCursor] = data[r] == 0 ? "False" : "True";
                    } finally { h.Free(); }
                    colCursor += 1;
                    continue;
                }
                if (a.type == GeoAttrType.F64 || a.type == GeoAttrType.DVec2 || a.type == GeoAttrType.DVec3 || a.type == GeoAttrType.DVec4) {
                    int cap = rows * Math.Max(dim, 1);
                    var data = new double[Math.Max(cap, 1)];
                    var h = GCHandle.Alloc(data, GCHandleType.Pinned);
                    try {
                        uint copied = NativeMethods.cunning_geo_copy_attr_f64(_geoActiveHandle, cls, a.name, h.AddrOfPinnedObject(), (uint)cap);
                        int rowCount = Math.Min(rows, (int)copied / Math.Max(dim, 1));
                        for (int r = 0; r < rowCount; r++) {
                            for (int c = 0; c < Math.Max(dim, 1); c++) {
                                int cc = dim <= 1 ? 0 : c;
                                _geoTableRows[r][colCursor + cc] = data[r * Math.Max(dim, 1) + c].ToString("0.###", CultureInfo.InvariantCulture);
                            }
                        }
                    } finally { h.Free(); }
                    colCursor += Math.Max(dim, 1);
                    continue;
                }
                if (a.type == GeoAttrType.StringUtf8) {
                    uint strCount = NativeMethods.cunning_geo_get_attr_string_count(_geoActiveHandle, cls, a.name);
                    int rowCount = Math.Min(rows, (int)strCount);
                    for (int r = 0; r < rowCount; r++) _geoTableRows[r][colCursor] = ReadAttrString(_geoActiveHandle, cls, a.name, (uint)r);
                    colCursor += 1;
                    continue;
                }
                if (a.type == GeoAttrType.BytesU8) {
                    var data = new byte[Math.Max(rows, 1)];
                    var h = GCHandle.Alloc(data, GCHandleType.Pinned);
                    try {
                        uint copied = NativeMethods.cunning_geo_copy_attr_bytes(_geoActiveHandle, cls, a.name, h.AddrOfPinnedObject(), (uint)rows);
                        int rowCount = Math.Min(rows, (int)copied);
                        for (int r = 0; r < rowCount; r++) _geoTableRows[r][colCursor] = $"0x{data[r]:X2}";
                    } finally { h.Free(); }
                    colCursor += 1;
                    continue;
                }

                colCursor += Math.Max(dim, 1);
            }
        }

        int GetGeometryClassRowCount(GeoAttrClass cls) {
            return cls switch {
                GeoAttrClass.Point => (int)NativeMethods.cunning_geo_get_point_count(_geoActiveHandle),
                GeoAttrClass.Vertex => (int)NativeMethods.cunning_geo_get_vertex_count(_geoActiveHandle),
                GeoAttrClass.Primitive => (int)NativeMethods.cunning_geo_get_prim_count(_geoActiveHandle),
                GeoAttrClass.Detail => 1,
                _ => 0,
            };
        }

        static int GetTableComponentCount(GeoAttrType t) {
            return t switch {
                GeoAttrType.Vec2 => 2,
                GeoAttrType.Vec3 => 3,
                GeoAttrType.Vec4 => 4,
                GeoAttrType.IVec2 => 2,
                GeoAttrType.DVec2 => 2,
                GeoAttrType.DVec3 => 3,
                GeoAttrType.DVec4 => 4,
                GeoAttrType.F32 => 1,
                GeoAttrType.I32 => 1,
                GeoAttrType.F64 => 1,
                _ => 1,
            };
        }

        static string GetComponentSuffix(int i) {
            return i switch {
                0 => "x",
                1 => "y",
                2 => "z",
                3 => "w",
                _ => i.ToString(CultureInfo.InvariantCulture),
            };
        }

        void ProcessGeometryAutoSync() {
            if (_target == null || _target.asset == null) return;
            if (_selectedNode < 0 || _selectedNode >= _nodes.Count) return;
            string nodeId = _nodes[_selectedNode]?.id ?? "";
            bool nodeChanged = !string.Equals(_geoLastSyncedNodeId ?? "", nodeId, StringComparison.Ordinal);
            bool genChanged = _geoLastSyncedGen != _target.gen;
            if (_geoJobId != 0 || _geoAutoRefreshPending || nodeChanged || genChanged) {
                TrySyncSelectedNodeFromJob(true);
            }
        }

        bool TrySyncSelectedNodeFromJob(bool quietOnMiss) {
            if (_target == null || _target.asset == null) return false;
            if (_selectedNode < 0 || _selectedNode >= _nodes.Count) return false;
            if (!EnsureCdaLoaded(quietOnMiss)) return false;
            var n = _nodes[_selectedNode];
            if (string.IsNullOrWhiteSpace(n.id)) return false;

            // If a node geometry job is already in flight, poll it (even if the main cook job is running).
            if (_geoJobId != 0) {
                if (!string.Equals(_geoJobNodeId ?? "", n.id ?? "", StringComparison.Ordinal) || _geoJobTargetGen != _target.gen) {
                    NativeMethods.cunning_job_cancel(_geoJobId);
                    _geoJobId = 0;
                    _geoAutoRefreshPending = true;
                    return false;
                }

                var st = NativeMethods.cunning_job_poll(_geoJobId);
                if (st != _geoJobLastStatus) _geoJobLastStatus = st;
                if (st != 2) { // Not ready yet
                    _geoAutoRefreshPending = true;
                    Repaint();
                    return false;
                }

                NativeMethods.JobStats jobStats = default;
                bool hasJobStats = NativeMethods.TryGetJobStats(_geoJobId, out jobStats);

                ulong handle = 0;
                try {
                    var outCount = NativeMethods.cunning_job_get_output_count(_geoJobId);
                    for (uint i = 0; i < outCount; i++) {
                        var k = NativeMethods.cunning_job_get_output_kind(_geoJobId, i);
                        if (k != 1) continue;
                        handle = NativeMethods.cunning_job_get_output_geo_handle(_geoJobId, i);
                        break;
                    }
                } catch (Exception e) {
                    if (!quietOnMiss) SetStatus($"Failed to read node geometry output: {e.Message}", MessageType.Warning);
                } finally {
                    _geoJobId = 0;
                }

                if (handle == 0) {
                    _geoAutoRefreshPending = true;
                    return false;
                }

                ClearGeometryActiveHandle();
                _geoActiveHandle = handle;
                _geoLastSyncedNodeId = n.id ?? "";
                _geoLastSyncedGen = _target.gen;
                _geoAutoRefreshPending = false;
                RefreshGeometryAttrs();
                if (hasJobStats) {
                    ulong queueMs = (jobStats.started_ms > jobStats.submitted_ms) ? (jobStats.started_ms - jobStats.submitted_ms) : 0;
                    ulong totalMs = (jobStats.finished_ms > jobStats.submitted_ms) ? (jobStats.finished_ms - jobStats.submitted_ms) : 0;
                    SetStatus($"Loaded node geometry ({jobStats.compute_ms}ms compute, {queueMs}ms queue, {totalMs}ms total).", MessageType.Info);
                } else {
                    SetStatus("Loaded node geometry.", MessageType.Info);
                }
                Repaint();
                SceneView.RepaintAll();
                return true;
            }

            // Don't submit a new debug job while the main cook job is running.
            if (_target.jobId != 0) {
                _geoAutoRefreshPending = true;
                return false;
            }

            if (IsBlackBox(_target)) return false;

            // Prefer native cached node geometry (only populated when CDA Tools enables it).
            try {
                if (_target.instanceId != 0 && !string.IsNullOrWhiteSpace(n.id)) {
                    ulong cachedHandle = NativeMethods.cunning_cda_get_cached_node_geo_handle(_target.instanceId, _target.gen, n.id);
                    if (cachedHandle != 0) {
                        ClearGeometryActiveHandle();
                        _geoActiveHandle = cachedHandle;
                        _geoLastSyncedNodeId = n.id ?? "";
                        _geoLastSyncedGen = _target.gen;
                        _geoAutoRefreshPending = false;
                        RefreshGeometryAttrs();
                        SetStatus("Loaded node geometry (cached).", MessageType.Info);
                        Repaint();
                        SceneView.RepaintAll();
                        return true;
                    }
                }
            } catch (EntryPointNotFoundException) {
                // Older native DLL; fall back to debug job.
            }

            // Submit a fresh job for selected node geometry.
            if (!_target.TrySubmitNodeGeometryJob(null, out var submittedJobId, out var error, nodeId: n.id)) {
                if (!quietOnMiss && !string.IsNullOrWhiteSpace(error)) SetStatus(error, MessageType.Warning);
                _geoAutoRefreshPending = true;
                return false;
            }

            _geoJobId = submittedJobId;
            _geoJobLastStatus = 0;
            _geoJobNodeId = n.id ?? "";
            _geoJobTargetGen = _target.gen;
            _geoAutoRefreshPending = true;
            Repaint();
            return false;
        }

        void RefreshGeometryAttrs() {
            string prevSelectedName = (_geoSelectedAttr >= 0 && _geoSelectedAttr < _geoAttrs.Count)
                ? _geoAttrs[_geoSelectedAttr].name
                : null;
            bool hadArmedPreview = _geoScenePreviewArmed;

            _geoAttrs.Clear();
            _geoPreview.Clear();
            _geoSelectedAttr = -1;
            InvalidateGeometryTableCache();
            if (_geoActiveHandle == 0) return;

            uint ac = NativeMethods.cunning_geo_get_attr_count(_geoActiveHandle, (uint)_geoSelectedClass);
            for (uint i = 0; i < ac; i++) {
                string name = ReadAttrName(_geoActiveHandle, _geoSelectedClass, i);
                if (string.IsNullOrWhiteSpace(name)) continue;
                var type = (GeoAttrType)NativeMethods.cunning_geo_get_attr_type(_geoActiveHandle, (uint)_geoSelectedClass, name);
                uint len = NativeMethods.cunning_geo_get_attr_len(_geoActiveHandle, (uint)_geoSelectedClass, name);
                _geoAttrs.Add(new GeoAttrInfo { name = name, type = type, len = len });
            }
            if (!string.IsNullOrWhiteSpace(prevSelectedName) && _geoAttrs.Count > 0) {
                int idx = _geoAttrs.FindIndex(x => string.Equals(x.name, prevSelectedName, StringComparison.Ordinal));
                if (idx >= 0) {
                    _geoSelectedAttr = idx;
                    BuildGeometryPreview();
                    if (hadArmedPreview) ArmScenePreviewForSelectedAttr();
                }
            } else {
                if (_geoAttrs.Count > 0) {
                    _geoSelectedAttr = 0;
                    BuildGeometryPreview();
                }
                _geoScenePreviewArmed = false;
                _geoScenePreviewAttrName = null;
            }
            _geoTableDirty = true;
        }

        void BuildGeometryPreview() {
            _geoPreview.Clear();
            if (_geoSelectedAttr < 0 || _geoSelectedAttr >= _geoAttrs.Count) return;
            var a = _geoAttrs[_geoSelectedAttr];
            int rows = Mathf.Max(0, Mathf.Min(_geoPreviewRows, (int)a.len));
            uint cls = (uint)_geoSelectedClass;

            if (a.type == GeoAttrType.F32 || a.type == GeoAttrType.Vec2 || a.type == GeoAttrType.Vec3 || a.type == GeoAttrType.Vec4) {
                int dim = ComponentCount(a.type), cap = rows * dim;
                var data = new float[Math.Max(cap, 1)];
                var h = GCHandle.Alloc(data, GCHandleType.Pinned);
                try {
                    uint copied = NativeMethods.cunning_geo_copy_attr_f32(_geoActiveHandle, cls, a.name, h.AddrOfPinnedObject(), (uint)cap);
                    int rc = Math.Min(rows, (int)copied / Math.Max(dim, 1));
                    for (int i = 0; i < rc; i++) _geoPreview.Add($"{i}: {FormatRowF32(data, i * dim, dim)}");
                } finally { h.Free(); }
                return;
            }
            if (a.type == GeoAttrType.I32 || a.type == GeoAttrType.IVec2) {
                int dim = ComponentCount(a.type), cap = rows * dim;
                var data = new int[Math.Max(cap, 1)];
                var h = GCHandle.Alloc(data, GCHandleType.Pinned);
                try {
                    uint copied = NativeMethods.cunning_geo_copy_attr_i32(_geoActiveHandle, cls, a.name, h.AddrOfPinnedObject(), (uint)cap);
                    int rc = Math.Min(rows, (int)copied / Math.Max(dim, 1));
                    for (int i = 0; i < rc; i++) _geoPreview.Add($"{i}: {FormatRowI32(data, i * dim, dim)}");
                } finally { h.Free(); }
                return;
            }
            if (a.type == GeoAttrType.BoolU8) {
                var data = new byte[Math.Max(rows, 1)];
                var h = GCHandle.Alloc(data, GCHandleType.Pinned);
                try {
                    uint copied = NativeMethods.cunning_geo_copy_attr_u8(_geoActiveHandle, cls, a.name, h.AddrOfPinnedObject(), (uint)rows);
                    int rc = Math.Min(rows, (int)copied);
                    for (int i = 0; i < rc; i++) _geoPreview.Add($"{i}: {(data[i] == 0 ? "False" : "True")}");
                } finally { h.Free(); }
                return;
            }
            if (a.type == GeoAttrType.F64 || a.type == GeoAttrType.DVec2 || a.type == GeoAttrType.DVec3 || a.type == GeoAttrType.DVec4) {
                int dim = ComponentCount(a.type), cap = rows * dim;
                var data = new double[Math.Max(cap, 1)];
                var h = GCHandle.Alloc(data, GCHandleType.Pinned);
                try {
                    uint copied = NativeMethods.cunning_geo_copy_attr_f64(_geoActiveHandle, cls, a.name, h.AddrOfPinnedObject(), (uint)cap);
                    int rc = Math.Min(rows, (int)copied / Math.Max(dim, 1));
                    for (int i = 0; i < rc; i++) _geoPreview.Add($"{i}: {FormatRowF64(data, i * dim, dim)}");
                } finally { h.Free(); }
                return;
            }
            if (a.type == GeoAttrType.StringUtf8) {
                uint strCount = NativeMethods.cunning_geo_get_attr_string_count(_geoActiveHandle, cls, a.name);
                int rc = Math.Min(rows, (int)strCount);
                for (int i = 0; i < rc; i++) _geoPreview.Add($"{i}: {ReadAttrString(_geoActiveHandle, cls, a.name, (uint)i)}");
                return;
            }
            if (a.type == GeoAttrType.BytesU8) {
                int cap = Math.Max(rows, 1);
                var data = new byte[cap];
                var h = GCHandle.Alloc(data, GCHandleType.Pinned);
                try {
                    uint copied = NativeMethods.cunning_geo_copy_attr_bytes(_geoActiveHandle, cls, a.name, h.AddrOfPinnedObject(), (uint)cap);
                    int rc = Math.Min(rows, (int)copied);
                    for (int i = 0; i < rc; i++) _geoPreview.Add($"{i}: 0x{data[i]:X2}");
                } finally { h.Free(); }
                return;
            }

            _geoPreview.Add("Preview not implemented for this type yet.");
        }

        void DrawGeometryScenePreviewOptions() {
            _geoScenePreviewEnabled = EditorGUILayout.Toggle("Scene Point Labels", _geoScenePreviewEnabled);
            _geoScenePreviewMax = EditorGUILayout.IntSlider("Scene Max Labels", _geoScenePreviewMax, 16, 320);
            _geoScenePreviewColor = EditorGUILayout.ColorField("Scene Label Color", _geoScenePreviewColor);
            GUILayout.Label("Labels appear only after you click an attribute row.", _subtleStyle);
        }

        void OnSceneGui(SceneView sv) {
            if (!_geoScenePreviewEnabled || _geoActiveHandle == 0) return;
            if (_geoSelectedClass != GeoAttrClass.Point) return;
            if (_geoSelectedAttr < 0 || _geoSelectedAttr >= _geoAttrs.Count) return;
            if (!_geoScenePreviewArmed) return;
            if (_geoScenePreviewAttrClass != _geoSelectedClass) return;
            var selectedAttr = _geoAttrs[_geoSelectedAttr];
            if (!string.Equals(_geoScenePreviewAttrName ?? "", selectedAttr.name ?? "", StringComparison.Ordinal)) return;
            if (Event.current.type != EventType.Repaint) return;
            if (_target == null) return;
            var cam = sv != null ? sv.camera : null;
            if (cam == null) return;

            RebuildGeometrySceneCacheIfNeeded(_target);
            if (_geoScenePoints.Length == 0 || _geoSceneTexts.Length == 0) return;
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);

            var old = Handles.color;
            Handles.color = _geoScenePreviewColor;
            int n = Math.Min(_geoScenePoints.Length, _geoSceneTexts.Length);
            for (int i = 0; i < n; i++) {
                var pos = _geoScenePoints[i];
                if (!IsPointInsideFrustum(planes, pos)) continue;
                Handles.Label(pos, _geoSceneTexts[i]);
            }
            Handles.color = old;
        }

        void RebuildGeometrySceneCacheIfNeeded(CunningCDAInstance t) {
            if (_geoSelectedAttr < 0 || _geoSelectedAttr >= _geoAttrs.Count) return;
            var a = _geoAttrs[_geoSelectedAttr];
            ulong dirty = NativeMethods.cunning_geo_get_dirty_id(_geoActiveHandle);
            if (_geoSceneCacheHandle == _geoActiveHandle &&
                _geoSceneCacheDirty == dirty &&
                _geoSceneCacheAttr == a.name &&
                _geoSceneCacheMax == _geoScenePreviewMax) return;

            _geoSceneCacheHandle = _geoActiveHandle;
            _geoSceneCacheDirty = dirty;
            _geoSceneCacheAttr = a.name;
            _geoSceneCacheMax = _geoScenePreviewMax;

            int pointCount = (int)NativeMethods.cunning_geo_get_point_count(_geoActiveHandle);
            if (pointCount <= 0 || pointCount > 20000) {
                _geoScenePoints = Array.Empty<Vector3>();
                _geoSceneTexts = Array.Empty<string>();
                return;
            }
            int n = Math.Min(pointCount, _geoScenePreviewMax);

            var pRaw = new float[pointCount * 3];
            var hp = GCHandle.Alloc(pRaw, GCHandleType.Pinned);
            try { NativeMethods.cunning_geo_copy_points(_geoActiveHandle, hp.AddrOfPinnedObject()); }
            finally { hp.Free(); }

            _geoScenePoints = new Vector3[n];
            var l2w = t.transform.localToWorldMatrix;
            for (int i = 0; i < n; i++) {
                int j = i * 3;
                var local = new Vector3(pRaw[j], pRaw[j + 1], pRaw[j + 2]);
                _geoScenePoints[i] = l2w.MultiplyPoint3x4(local);
            }
            _geoSceneTexts = BuildPointAttrTexts(a, n);
        }

        string[] BuildPointAttrTexts(GeoAttrInfo a, int n) {
            var outText = new string[n];
            for (int i = 0; i < n; i++) outText[i] = "...";
            uint cls = (uint)GeoAttrClass.Point;
            if (a.type == GeoAttrType.F32 || a.type == GeoAttrType.Vec2 || a.type == GeoAttrType.Vec3 || a.type == GeoAttrType.Vec4) {
                int dim = ComponentCount(a.type), cap = n * dim;
                var data = new float[Math.Max(cap, 1)];
                var h = GCHandle.Alloc(data, GCHandleType.Pinned);
                try {
                    uint copied = NativeMethods.cunning_geo_copy_attr_f32(_geoActiveHandle, cls, a.name, h.AddrOfPinnedObject(), (uint)cap);
                    int rows = Math.Min(n, (int)copied / Math.Max(dim, 1));
                    for (int i = 0; i < rows; i++) outText[i] = FormatRowF32(data, i * dim, dim);
                } finally { h.Free(); }
                return outText;
            }
            if (a.type == GeoAttrType.I32 || a.type == GeoAttrType.IVec2) {
                int dim = ComponentCount(a.type), cap = n * dim;
                var data = new int[Math.Max(cap, 1)];
                var h = GCHandle.Alloc(data, GCHandleType.Pinned);
                try {
                    uint copied = NativeMethods.cunning_geo_copy_attr_i32(_geoActiveHandle, cls, a.name, h.AddrOfPinnedObject(), (uint)cap);
                    int rows = Math.Min(n, (int)copied / Math.Max(dim, 1));
                    for (int i = 0; i < rows; i++) outText[i] = FormatRowI32(data, i * dim, dim);
                } finally { h.Free(); }
                return outText;
            }
            if (a.type == GeoAttrType.BoolU8) {
                var data = new byte[Math.Max(n, 1)];
                var h = GCHandle.Alloc(data, GCHandleType.Pinned);
                try {
                    uint copied = NativeMethods.cunning_geo_copy_attr_u8(_geoActiveHandle, cls, a.name, h.AddrOfPinnedObject(), (uint)n);
                    int rows = Math.Min(n, (int)copied);
                    for (int i = 0; i < rows; i++) outText[i] = data[i] == 0 ? "False" : "True";
                } finally { h.Free(); }
                return outText;
            }
            if (a.type == GeoAttrType.F64 || a.type == GeoAttrType.DVec2 || a.type == GeoAttrType.DVec3 || a.type == GeoAttrType.DVec4) {
                int dim = ComponentCount(a.type), cap = n * dim;
                var data = new double[Math.Max(cap, 1)];
                var h = GCHandle.Alloc(data, GCHandleType.Pinned);
                try {
                    uint copied = NativeMethods.cunning_geo_copy_attr_f64(_geoActiveHandle, cls, a.name, h.AddrOfPinnedObject(), (uint)cap);
                    int rows = Math.Min(n, (int)copied / Math.Max(dim, 1));
                    for (int i = 0; i < rows; i++) outText[i] = FormatRowF64(data, i * dim, dim);
                } finally { h.Free(); }
                return outText;
            }
            if (a.type == GeoAttrType.StringUtf8) {
                uint strCount = NativeMethods.cunning_geo_get_attr_string_count(_geoActiveHandle, cls, a.name);
                int rows = Math.Min(n, (int)strCount);
                for (int i = 0; i < rows; i++) outText[i] = ReadAttrString(_geoActiveHandle, cls, a.name, (uint)i);
                return outText;
            }
            return outText;
        }

        void ArmScenePreviewForSelectedAttr() {
            if (_geoSelectedAttr < 0 || _geoSelectedAttr >= _geoAttrs.Count) {
                _geoScenePreviewArmed = false;
                _geoScenePreviewAttrName = null;
                return;
            }
            _geoScenePreviewArmed = true;
            _geoScenePreviewAttrClass = _geoSelectedClass;
            _geoScenePreviewAttrName = _geoAttrs[_geoSelectedAttr].name;
        }

        void InvalidateGeometrySceneCache() {
            _geoSceneCacheHandle = 0;
            _geoSceneCacheDirty = 0;
            _geoSceneCacheAttr = null;
            _geoSceneCacheMax = 0;
            _geoScenePoints = Array.Empty<Vector3>();
            _geoSceneTexts = Array.Empty<string>();
        }

        void InvalidateGeometryTableCache() {
            _geoTableDirty = true;
            _geoTableCacheHandle = 0;
            _geoTableCacheDirty = 0;
            _geoTableCacheRows = 0;
            _geoTableCacheFilter = "";
            _geoTableColumns.Clear();
            _geoTableRows.Clear();
            _geoTableSelectedRow = -1;
        }

        void ClearGeometryActiveHandle() {
            if (_geoJobId != 0) {
                NativeMethods.cunning_job_cancel(_geoJobId);
                _geoJobId = 0;
            }
            _geoJobLastStatus = 0;
            _geoJobNodeId = null;
            _geoJobTargetGen = 0;
            if (_geoActiveHandle != 0) {
                NativeMethods.cunning_release_handle(_geoActiveHandle);
                _geoActiveHandle = 0;
            }
            _geoAttrs.Clear();
            _geoPreview.Clear();
            _geoSelectedAttr = -1;
            _geoScenePreviewArmed = false;
            _geoScenePreviewAttrName = null;
            InvalidateGeometrySceneCache();
            InvalidateGeometryTableCache();
        }

        static bool IsPointInsideFrustum(Plane[] planes, Vector3 p) {
            if (planes == null || planes.Length == 0) return true;
            for (int i = 0; i < planes.Length; i++) {
                if (planes[i].GetDistanceToPoint(p) < 0f) return false;
            }
            return true;
        }

        void DrawParamCard(WhiteNodeInfo node, WhiteParamInfo p) {
            bool isPromoted = p.promotedBindings != null && p.promotedBindings.Count > 0;

            EditorGUILayout.BeginVertical(_subCardStyle);
            GUILayout.Label(p.name, p.isModified ? _modifiedParamNameStyle : EditorStyles.boldLabel);
            if (p.isModified) GUILayout.Label("MODIFIED (driven by promoted parameters)", _modifiedBadgeStyle);
            if (isPromoted) {
                var promotedNames = string.Join(", ", p.promotedBindings.Select(x => x.promotedName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
                GUILayout.Label($"Promoted Sync: {promotedNames}", _subtleStyle);
            }

            string controlKind = ResolveControlKind(p);
            GUILayout.Label($"Type: {controlKind}", _subtleStyle);
            GUILayout.Label($"Current: {FormatValueSummary(p.currentJson)}", _subtleStyle);
            GUILayout.Label($"Default: {FormatValueSummary(p.baseJson)}", _subtleStyle);
            if (!IsNullLikeNodeType(node.typeId)) {
                EditorGUILayout.HelpBox("Recommended: expose data from a Null node as a stable anchor for programmer access.", MessageType.Warning);
            }
            if (isPromoted) {
                EditorGUILayout.HelpBox("This value is driven by promoted parameters (editable in Inspector PARAMETERS).", MessageType.None);
            } else {
                EditorGUILayout.HelpBox("Internal node parameter is read-only in Unity. Expose/promote it in Cunning3D if runtime tuning is needed.", MessageType.None);
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Add Data Target", EditorStyles.miniButton, GUILayout.Width(128))) {
                AddDataTarget(node, p);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        List<int> GetFilteredNodeIndices() {
            var result = new List<int>();
            if (_nodes.Count == 0) return result;
            if (string.IsNullOrWhiteSpace(_search)) {
                for (int i = 0; i < _nodes.Count; i++) result.Add(i);
                return result;
            }
            string s = _search.Trim();
            for (int i = 0; i < _nodes.Count; i++) {
                var n = _nodes[i];
                if ((!string.IsNullOrEmpty(n.name) && n.name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(n.id) && n.id.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(n.typeId) && n.typeId.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)) {
                    result.Add(i);
                }
            }
            return result;
        }

        void RefreshNodes() {
            if (_target == null || _target.asset == null) {
                SetStatus("No CDA instance/asset.", MessageType.Warning);
                return;
            }
            if (!EnsureCdaLoaded(false)) return;

            if (IsBlackBox(_target)) {
                SetStatus("CDA is BlackBox. Internal node/param access is disabled.", MessageType.Info);
                ResetNodeDataAndSelection();
                ClearGeometryActiveHandle();
                return;
            }

            string prevSelectedId = (_selectedNode >= 0 && _selectedNode < _nodes.Count)
                ? _nodes[_selectedNode].id
                : null;
            bool hadNodes = _nodes.Count > 0;
            _nodes.Clear();
            _connections.Clear();
            _selectedNode = -1;
            uint nodeCount = NativeMethods.cunning_cda_get_node_count(_target.cdaId);
            for (uint i = 0; i < nodeCount; i++) {
                string nodeId = ReadNativeString((sb, cap) => NativeMethods.cunning_cda_get_node_id(_target.cdaId, i, sb, cap), 128);
                if (string.IsNullOrWhiteSpace(nodeId)) continue;
                string nodeName = ReadNativeString((sb, cap) => NativeMethods.cunning_cda_get_node_name(_target.cdaId, i, sb, cap), 512);
                string typeId = ReadNativeString((sb, cap) => NativeMethods.cunning_cda_get_node_type_id(_target.cdaId, i, sb, cap), 512);

                var node = new WhiteNodeInfo {
                    id = nodeId,
                    name = nodeName,
                    typeId = typeId,
                };

                uint paramCount = NativeMethods.cunning_cda_get_node_param_count(_target.cdaId, nodeId);
                for (uint pi = 0; pi < paramCount; pi++) {
                    string paramName = ReadNativeString((sb, cap) => NativeMethods.cunning_cda_get_node_param_name(_target.cdaId, nodeId, pi, sb, cap), 256);
                    if (string.IsNullOrWhiteSpace(paramName)) continue;
                    string baseJson = ReadNativeString((sb, cap) => NativeMethods.cunning_cda_get_node_param_json(_target.cdaId, nodeId, paramName, sb, cap), 8192);
                    string uiJson = ReadNodeParamUiJson(_target.cdaId, nodeId, paramName);

                    var promotedBindings = GetPromotedBindings(nodeId, paramName);
                    string currJson = BuildCurrentJsonForParam(nodeId, paramName, baseJson, promotedBindings);
                    if (string.IsNullOrEmpty(currJson)) currJson = baseJson;

                    bool isModified = promotedBindings.Count > 0
                        && !string.Equals(baseJson ?? "", currJson ?? "", StringComparison.Ordinal);

                    node.parameters.Add(new WhiteParamInfo {
                        name = paramName,
                        baseJson = baseJson ?? "",
                        currentJson = currJson ?? "",
                        ui = ParseUiMeta(uiJson),
                        isModified = isModified,
                        promotedBindings = promotedBindings,
                    });
                }
                _nodes.Add(node);
            }

            TryApplyNodePortsFromSourceJson();
            ParseConnectionsFromSourceJson();
            ComputeGraphLayout();
            TryApplyGraphPositionsFromSourceJson();
            _nodesCdaId = _target.cdaId;
            _nodesRevision = NativeMethods.cunning_cda_get_revision(_target.cdaId);

            if (!string.IsNullOrWhiteSpace(prevSelectedId)) {
                _selectedNode = _nodes.FindIndex(x => string.Equals(x.id, prevSelectedId, StringComparison.OrdinalIgnoreCase));
            }
            if (_selectedNode < 0 && _nodes.Count > 0) _selectedNode = 0;
            if (!hadNodes && _nodes.Count > 0) {
                _graphAutoFramePending = true;
                _graphUserAdjusted = false;
            }
            _geoAutoRefreshPending = _nodes.Count > 0;
            SetStatus($"Loaded {_nodes.Count} node(s).", MessageType.Info);
        }

        void ParseConnectionsFromSourceJson() {
            _connections.Clear();
            if (_target == null || _target.asset == null || string.IsNullOrWhiteSpace(_target.asset.sourceJson)) return;

            var nodeIds = new HashSet<string>(_nodes.Select(x => x.id ?? ""), StringComparer.OrdinalIgnoreCase);
            bool parsedInnerGraph = ParseConnectionsFromInnerGraph(nodeIds);
            if (!parsedInnerGraph) {
                ParseConnectionsFromTopLevel(nodeIds);
            }
            ResolveConnectionPortDisplayNames();
        }

        bool ParseConnectionsFromInnerGraph(HashSet<string> nodeIds) {
            var innerGraphRaw = CdaMiniJson.GetTopLevelRaw(_target.asset.sourceJson, "inner_graph");
            if (string.IsNullOrWhiteSpace(innerGraphRaw)) return false;
            var connsRaw = CdaMiniJson.GetTopLevelRaw(innerGraphRaw, "connections");
            if (string.IsNullOrWhiteSpace(connsRaw)) return false;

            bool any = false;
            string trimmed = connsRaw.TrimStart();
            if (trimmed.StartsWith("{", StringComparison.Ordinal)) {
                var entries = SplitObjectMembersRaw(connsRaw);
                for (int i = 0; i < entries.Count; i++) {
                    if (TryAddConnection(entries[i].Value, nodeIds)) any = true;
                }
            } else if (trimmed.StartsWith("[", StringComparison.Ordinal)) {
                var elems = CdaMiniJson.SplitArrayElements(connsRaw);
                for (int i = 0; i < elems.Count; i++) {
                    if (TryAddConnection(elems[i], nodeIds)) any = true;
                }
            }
            return any;
        }

        void ParseConnectionsFromTopLevel(HashSet<string> nodeIds) {
            var arr = CdaMiniJson.GetTopLevelRaw(_target.asset.sourceJson, "connections");
            if (string.IsNullOrWhiteSpace(arr)) return;
            string trimmed = arr.TrimStart();
            if (!trimmed.StartsWith("[", StringComparison.Ordinal)) return;
            var elems = CdaMiniJson.SplitArrayElements(arr);
            for (int i = 0; i < elems.Count; i++) {
                TryAddConnection(elems[i], nodeIds);
            }
        }

        bool TryAddConnection(string rawConnObj, HashSet<string> nodeIds) {
            if (string.IsNullOrWhiteSpace(rawConnObj)) return false;
            if (!CdaMiniJson.TryGetObjectString(rawConnObj, "from_node", out var from)) return false;
            if (!CdaMiniJson.TryGetObjectString(rawConnObj, "to_node", out var to)) return false;
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to)) return false;
            if (!nodeIds.Contains(from) || !nodeIds.Contains(to)) return false;
            string fromPort = ReadConnectionPortToken(rawConnObj, "from_port");
            string toPort = ReadConnectionPortToken(rawConnObj, "to_port");
            if (string.IsNullOrWhiteSpace(fromPort)) fromPort = ReadConnectionPortToken(rawConnObj, "from_socket");
            if (string.IsNullOrWhiteSpace(toPort)) toPort = ReadConnectionPortToken(rawConnObj, "to_socket");
            _connections.Add(new WhiteConnInfo {
                fromNodeId = from,
                toNodeId = to,
                fromPort = fromPort,
                toPort = toPort
            });
            return true;
        }

        static string ReadConnectionPortToken(string objRaw, string prop) {
            if (CdaMiniJson.TryGetObjectString(objRaw, prop, out var strPort)) {
                return string.IsNullOrWhiteSpace(strPort) ? null : strPort.Trim();
            }
            if (!CdaMiniJson.TryGetObjectRaw(objRaw, prop, out var raw)) return null;
            string t = (raw ?? "").Trim();
            if (t.Length == 0 || t.Equals("null", StringComparison.OrdinalIgnoreCase)) return null;
            return t;
        }

        void ResolveConnectionPortDisplayNames() {
            if (_connections.Count == 0 || _nodes.Count == 0) return;
            var byId = new Dictionary<string, WhiteNodeInfo>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _nodes.Count; i++) {
                var n = _nodes[i];
                if (n == null || string.IsNullOrWhiteSpace(n.id) || byId.ContainsKey(n.id)) continue;
                byId[n.id] = n;
            }
            for (int i = 0; i < _connections.Count; i++) {
                var c = _connections[i];
                if (c == null) continue;
                c.fromPort = byId.TryGetValue(c.fromNodeId ?? "", out var fromNode)
                    ? NormalizeConnectionPort(c.fromPort, fromNode.outputPorts, "out")
                    : NormalizeConnectionPort(c.fromPort, null, "out");
                c.toPort = byId.TryGetValue(c.toNodeId ?? "", out var toNode)
                    ? NormalizeConnectionPort(c.toPort, toNode.inputPorts, "in")
                    : NormalizeConnectionPort(c.toPort, null, "in");
            }
        }

        static string NormalizeConnectionPort(string rawPort, List<string> knownPorts, string defaultPrefix) {
            string token = string.IsNullOrWhiteSpace(rawPort) ? null : rawPort.Trim();
            if (knownPorts != null && knownPorts.Count > 0) {
                if (string.IsNullOrWhiteSpace(token)) return knownPorts[0];
                for (int i = 0; i < knownPorts.Count; i++) {
                    if (string.Equals(knownPorts[i], token, StringComparison.OrdinalIgnoreCase)) return knownPorts[i];
                }
                if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx)) {
                    string indexed = $"{defaultPrefix}:{idx}";
                    for (int i = 0; i < knownPorts.Count; i++) {
                        if (string.Equals(knownPorts[i], indexed, StringComparison.OrdinalIgnoreCase)) return knownPorts[i];
                    }
                    if (idx >= 0 && idx < knownPorts.Count) return knownPorts[idx];
                    return indexed;
                }
                return token;
            }
            if (string.IsNullOrWhiteSpace(token)) return "";
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fallbackIdx)) {
                return $"{defaultPrefix}:{fallbackIdx}";
            }
            return token;
        }

        void TryApplyNodePortsFromSourceJson() {
            if (_target == null || _target.asset == null || string.IsNullOrWhiteSpace(_target.asset.sourceJson)) return;
            var innerGraphRaw = CdaMiniJson.GetTopLevelRaw(_target.asset.sourceJson, "inner_graph");
            if (string.IsNullOrWhiteSpace(innerGraphRaw)) return;
            var nodesRaw = CdaMiniJson.GetTopLevelRaw(innerGraphRaw, "nodes");
            if (string.IsNullOrWhiteSpace(nodesRaw)) return;

            var byId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _nodes.Count; i++) {
                var id = _nodes[i]?.id;
                if (string.IsNullOrWhiteSpace(id) || byId.ContainsKey(id)) continue;
                byId[id] = i;
            }

            var entries = SplitObjectMembersRaw(nodesRaw);
            for (int i = 0; i < entries.Count; i++) {
                var nodeId = entries[i].Key;
                var nodeObj = entries[i].Value;
                if (!byId.TryGetValue(nodeId ?? "", out var idx)) continue;

                var n = _nodes[idx];
                n.inputPorts.Clear();
                n.outputPorts.Clear();

                if (CdaMiniJson.TryGetObjectRaw(nodeObj, "inputs", out var inputsRaw)) {
                    var inputEntries = SplitObjectMembersRaw(inputsRaw);
                    for (int k = 0; k < inputEntries.Count; k++) {
                        var key = inputEntries[k].Key;
                        if (!string.IsNullOrWhiteSpace(key)) n.inputPorts.Add(key);
                    }
                }
                if (CdaMiniJson.TryGetObjectRaw(nodeObj, "outputs", out var outputsRaw)) {
                    var outputEntries = SplitObjectMembersRaw(outputsRaw);
                    for (int k = 0; k < outputEntries.Count; k++) {
                        var key = outputEntries[k].Key;
                        if (!string.IsNullOrWhiteSpace(key)) n.outputPorts.Add(key);
                    }
                }
            }
        }

        void TryApplyGraphPositionsFromSourceJson() {
            if (_target == null || _target.asset == null || string.IsNullOrWhiteSpace(_target.asset.sourceJson)) return;

            var innerGraphRaw = CdaMiniJson.GetTopLevelRaw(_target.asset.sourceJson, "inner_graph");
            if (string.IsNullOrWhiteSpace(innerGraphRaw)) return;
            var nodesRaw = CdaMiniJson.GetTopLevelRaw(innerGraphRaw, "nodes");
            if (string.IsNullOrWhiteSpace(nodesRaw)) return;

            var byId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _nodes.Count; i++) {
                var id = _nodes[i]?.id;
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (!byId.ContainsKey(id)) byId[id] = i;
            }

            var entries = SplitObjectMembersRaw(nodesRaw);
            for (int i = 0; i < entries.Count; i++) {
                var nodeId = entries[i].Key;
                var nodeObj = entries[i].Value;
                if (!byId.TryGetValue(nodeId ?? "", out var idx)) continue;

                if (!CdaMiniJson.TryGetObjectRaw(nodeObj, "position", out var posRaw)) continue;
                if (!CdaMiniJson.TryGetObjectRaw(posRaw, "x", out var xRaw)) continue;
                if (!CdaMiniJson.TryGetObjectRaw(posRaw, "y", out var yRaw)) continue;
                if (!float.TryParse((xRaw ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) continue;
                if (!float.TryParse((yRaw ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) continue;

                _nodes[idx].graphPos = new Vector2(x, y);
            }
        }

        static List<KeyValuePair<string, string>> SplitObjectMembersRaw(string rawObj) {
            var result = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrWhiteSpace(rawObj)) return result;

            int i = 0;
            SkipWsRaw(rawObj, ref i);
            if (i >= rawObj.Length || rawObj[i] != '{') return result;
            i++;
            while (i < rawObj.Length) {
                SkipWsRaw(rawObj, ref i);
                if (i < rawObj.Length && rawObj[i] == '}') break;

                if (!TryReadStringRaw(rawObj, ref i, out var key)) break;
                SkipWsRaw(rawObj, ref i);
                if (i >= rawObj.Length || rawObj[i] != ':') break;
                i++;
                SkipWsRaw(rawObj, ref i);
                int v0 = i;
                if (!TrySkipValueRaw(rawObj, ref i)) break;
                int v1 = i;
                result.Add(new KeyValuePair<string, string>(key, rawObj.Substring(v0, v1 - v0)));

                SkipWsRaw(rawObj, ref i);
                if (i < rawObj.Length && rawObj[i] == ',') {
                    i++;
                    continue;
                }
                if (i < rawObj.Length && rawObj[i] == '}') break;
            }

            return result;
        }

        static void SkipWsRaw(string s, ref int i) {
            while (i < s.Length) {
                char c = s[i];
                if (c == ' ' || c == '\n' || c == '\r' || c == '\t') {
                    i++;
                    continue;
                }
                break;
            }
        }

        static bool TryReadStringRaw(string s, ref int i, out string value) {
            value = null;
            SkipWsRaw(s, ref i);
            if (i >= s.Length || s[i] != '"') return false;
            i++;
            var sb = new StringBuilder();
            while (i < s.Length) {
                char c = s[i++];
                if (c == '"') {
                    value = sb.ToString();
                    return true;
                }
                if (c != '\\') {
                    sb.Append(c);
                    continue;
                }
                if (i >= s.Length) return false;
                char e = s[i++];
                if (e == '"' || e == '\\' || e == '/') sb.Append(e);
                else if (e == 'n') sb.Append('\n');
                else if (e == 'r') sb.Append('\r');
                else if (e == 't') sb.Append('\t');
                else return false;
            }
            return false;
        }

        static bool TrySkipValueRaw(string s, ref int i) {
            SkipWsRaw(s, ref i);
            if (i >= s.Length) return false;
            char c = s[i];
            if (c == '"') return TryReadStringRaw(s, ref i, out _);
            if (c == '{') return TrySkipContainerRaw(s, ref i, '{', '}');
            if (c == '[') return TrySkipContainerRaw(s, ref i, '[', ']');
            if (c == 't') return TryConsumeRaw(s, ref i, "true");
            if (c == 'f') return TryConsumeRaw(s, ref i, "false");
            if (c == 'n') return TryConsumeRaw(s, ref i, "null");
            return TrySkipNumberRaw(s, ref i);
        }

        static bool TryConsumeRaw(string s, ref int i, string lit) {
            if (i + lit.Length > s.Length) return false;
            for (int k = 0; k < lit.Length; k++) {
                if (s[i + k] != lit[k]) return false;
            }
            i += lit.Length;
            return true;
        }

        static bool TrySkipNumberRaw(string s, ref int i) {
            SkipWsRaw(s, ref i);
            int start = i;
            if (i < s.Length && s[i] == '-') i++;
            bool any = false;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') {
                any = true;
                i++;
            }
            if (i < s.Length && s[i] == '.') {
                i++;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') {
                    any = true;
                    i++;
                }
            }
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E')) {
                i++;
                if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') {
                    any = true;
                    i++;
                }
            }
            return any && i > start;
        }

        static bool TrySkipContainerRaw(string s, ref int i, char open, char close) {
            SkipWsRaw(s, ref i);
            if (i >= s.Length || s[i] != open) return false;
            int depth = 0;
            bool inStr = false;
            while (i < s.Length) {
                char c = s[i++];
                if (inStr) {
                    if (c == '\\') {
                        if (i < s.Length) i++;
                        continue;
                    }
                    if (c == '"') inStr = false;
                    continue;
                }
                if (c == '"') {
                    inStr = true;
                    continue;
                }
                if (c == open) depth++;
                else if (c == close) {
                    depth--;
                    if (depth == 0) return true;
                }
            }
            return false;
        }

        void ComputeGraphLayout() {
            if (_nodes.Count == 0) return;
            var nodeById = new Dictionary<string, WhiteNodeInfo>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _nodes.Count; i++) {
                var id = _nodes[i].id ?? "";
                if (!string.IsNullOrEmpty(id) && !nodeById.ContainsKey(id)) nodeById[id] = _nodes[i];
            }

            var indeg = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var adj = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in nodeById.Keys) {
                indeg[id] = 0;
                adj[id] = new List<string>();
            }
            for (int i = 0; i < _connections.Count; i++) {
                var c = _connections[i];
                if (c == null) continue;
                if (!nodeById.ContainsKey(c.fromNodeId ?? "") || !nodeById.ContainsKey(c.toNodeId ?? "")) continue;
                adj[c.fromNodeId].Add(c.toNodeId);
                indeg[c.toNodeId] = indeg[c.toNodeId] + 1;
            }

            var queue = new Queue<string>();
            var layer = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in indeg) if (kv.Value == 0) queue.Enqueue(kv.Key);
            while (queue.Count > 0) {
                var id = queue.Dequeue();
                int d = layer.TryGetValue(id, out var lv) ? lv : 0;
                var outs = adj.TryGetValue(id, out var ns) ? ns : null;
                if (outs == null) continue;
                for (int i = 0; i < outs.Count; i++) {
                    var next = outs[i];
                    int nd = d + 1;
                    if (!layer.TryGetValue(next, out var old) || nd > old) layer[next] = nd;
                    indeg[next] = indeg[next] - 1;
                    if (indeg[next] == 0) queue.Enqueue(next);
                }
            }
            foreach (var id in nodeById.Keys) {
                if (!layer.ContainsKey(id)) layer[id] = 0;
            }

            var groups = new Dictionary<int, List<WhiteNodeInfo>>();
            for (int i = 0; i < _nodes.Count; i++) {
                var n = _nodes[i];
                int l = layer.TryGetValue(n.id ?? "", out var lv) ? lv : 0;
                if (!groups.TryGetValue(l, out var g)) {
                    g = new List<WhiteNodeInfo>();
                    groups[l] = g;
                }
                g.Add(n);
            }
            foreach (var kv in groups) {
                kv.Value.Sort((a, b) => string.Compare(a.name ?? a.id ?? "", b.name ?? b.id ?? "", StringComparison.OrdinalIgnoreCase));
                for (int i = 0; i < kv.Value.Count; i++) {
                    kv.Value[i].graphPos = new Vector2(42f + kv.Key * 248f, 34f + i * 94f);
                }
            }
        }

        void ResetNodeDataAndSelection() {
            _nodes.Clear();
            _connections.Clear();
            _graphNodeRects.Clear();
            _selectedNode = -1;
            _nodesCdaId = 0;
            _nodesRevision = 0;
            _geoLastSyncedNodeId = null;
            _geoLastSyncedGen = 0;
            _geoAutoRefreshPending = false;
            if (_geoJobId != 0) {
                NativeMethods.cunning_job_cancel(_geoJobId);
            }
            _geoJobId = 0;
            _geoJobLastStatus = 0;
            _geoJobNodeId = null;
            _geoJobTargetGen = 0;
        }

        bool EnsureCdaLoaded(bool quiet) {
            if (_target == null || _target.asset == null) {
                if (!quiet) SetStatus("No CDA instance/asset.", MessageType.Warning);
                return false;
            }
            if (_target.cdaId != 0) return true;
            if (!quiet) {
                SetStatus(_target.jobId != 0
                    ? "CDA is cooking. Node graph will populate automatically."
                    : "CDA is not loaded yet.", MessageType.Info);
            }
            return false;
        }

        bool IsBlackBox(CunningCDAInstance t) {
            if (t == null || t.asset == null) return false;
            bool nativeIsBlackBox = (t.cdaId != 0 && NativeMethods.cunning_cda_get_access_mode(t.cdaId) == 1);
            bool hostWantsBlackBox = t.asset.IsBlackBox();
            return nativeIsBlackBox || hostWantsBlackBox;
        }

        static void SetNodeGeoCacheEnabledSafe(CunningCDAInstance t, bool enabled) {
            if (t == null || t.instanceId == 0 || !s_cacheEnableApiAvailable) return;
            try {
                NativeMethods.cunning_cda_set_cached_node_geo_enabled(t.instanceId, enabled ? 1u : 0u);
            } catch (EntryPointNotFoundException) {
                s_cacheEnableApiAvailable = false;
                if (!s_cacheEnableApiWarned) {
                    s_cacheEnableApiWarned = true;
                    Debug.LogWarning("CDA Tools: Native API missing: cunning_cda_set_cached_node_geo_enabled (rebuild native DLL).");
                }
            } catch {
                // Keep Tools responsive even if native throws.
            }
        }

        static string GetLastNativeError(string fallback) {
            var sb = new StringBuilder(2048);
            NativeMethods.cunning_cda_get_last_error(sb, (uint)sb.Capacity);
            string msg = sb.ToString();
            return string.IsNullOrWhiteSpace(msg) ? fallback : msg;
        }

        static string ReadNativeString(Func<StringBuilder, uint, uint> reader, int capacity) {
            var sb = new StringBuilder(Math.Max(64, capacity));
            uint ok = reader(sb, (uint)sb.Capacity);
            return ok == 0 ? "" : sb.ToString();
        }

        static string ReadNodeParamUiJson(ulong cdaId, string nodeId, string paramName) {
            try {
                return ReadNativeString((sb, cap) => NativeMethods.cunning_cda_get_node_param_ui_json(cdaId, nodeId, paramName, sb, cap), 8192);
            } catch (EntryPointNotFoundException) {
                return "";
            }
        }

        static CunningCDAInstance ResolveFromSelection() {
            if (Selection.activeGameObject != null) {
                var c = Selection.activeGameObject.GetComponent<CunningCDAInstance>();
                if (c != null) return c;
            }
            if (Selection.activeObject is GameObject go) {
                var c = go.GetComponent<CunningCDAInstance>();
                if (c != null) return c;
            }
            return null;
        }

        void AddDataTarget(WhiteNodeInfo node, WhiteParamInfo param) {
            if (_target == null || node == null || param == null || string.IsNullOrWhiteSpace(param.name)) return;

            _target.selectedTargets ??= new List<CdaSelectTarget>();

            string nodeName = string.IsNullOrWhiteSpace(node.name) ? null : node.name.Trim();
            string nodeId = string.IsNullOrWhiteSpace(node.id) ? null : node.id.Trim();
            if (string.IsNullOrEmpty(nodeName) && string.IsNullOrEmpty(nodeId)) {
                SetStatus("Cannot add target: node has no valid name/id.", MessageType.Warning);
                return;
            }

            if (_target.ContainsSelectedTarget(nodeName, nodeId, param.name)) {
                SetStatus("Data target already exists for this node param.", MessageType.Info);
                return;
            }

            string displayNode = !string.IsNullOrEmpty(nodeName) ? nodeName : nodeId;
            Undo.RecordObject(_target, "Add CDA Data Target");
            bool added = _target.AddSelectedTarget(new CdaSelectTarget {
                name = $"{displayNode}.{param.name}",
                kind = "NodeParam",
                node_name = nodeName,
                node_id = nodeId,
                param = param.name,
            });
            EditorUtility.SetDirty(_target);
            if (added && !IsNullLikeNodeType(node.typeId)) {
                SetStatus($"Added data target: {displayNode}.{param.name} (note: non-Null node; Null is recommended for maintainability)", MessageType.Warning);
            } else {
                SetStatus(added
                    ? $"Added data target: {displayNode}.{param.name}"
                    : $"Data target already exists: {displayNode}.{param.name}", MessageType.Info);
            }
        }

        static string BuildTargetOutputKey(CdaSelectTarget t) {
            return CunningCDAInstance.BuildTargetKey(t);
        }

        static bool IsNullLikeNodeType(string typeId) {
            if (string.IsNullOrWhiteSpace(typeId)) return false;
            var t = typeId.Trim().ToLowerInvariant();
            return t == "null" || t == "cunning.null" || t == "cunning.utility.null" || t.EndsWith(".null", StringComparison.Ordinal);
        }

        void SetStatus(string text, MessageType type) {
            _status = text;
            _statusType = type;
            Repaint();
        }

        static int ComponentCount(GeoAttrType t) {
            return t switch {
                GeoAttrType.F32 => 1,
                GeoAttrType.Vec2 => 2,
                GeoAttrType.Vec3 => 3,
                GeoAttrType.Vec4 => 4,
                GeoAttrType.I32 => 1,
                GeoAttrType.IVec2 => 2,
                GeoAttrType.F64 => 1,
                GeoAttrType.DVec2 => 2,
                GeoAttrType.DVec3 => 3,
                GeoAttrType.DVec4 => 4,
                _ => 1,
            };
        }

        static string FormatRowF32(float[] data, int start, int dim) {
            if (dim <= 1) return data[start].ToString("0.###", CultureInfo.InvariantCulture);
            var sb = new StringBuilder(64);
            sb.Append('(');
            for (int i = 0; i < dim; i++) {
                if (i != 0) sb.Append(", ");
                sb.Append(data[start + i].ToString("0.###", CultureInfo.InvariantCulture));
            }
            sb.Append(')');
            return sb.ToString();
        }

        static string FormatRowI32(int[] data, int start, int dim) {
            if (dim <= 1) return data[start].ToString(CultureInfo.InvariantCulture);
            var sb = new StringBuilder(64);
            sb.Append('(');
            for (int i = 0; i < dim; i++) {
                if (i != 0) sb.Append(", ");
                sb.Append(data[start + i].ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(')');
            return sb.ToString();
        }

        static string FormatRowF64(double[] data, int start, int dim) {
            if (dim <= 1) return data[start].ToString("0.###", CultureInfo.InvariantCulture);
            var sb = new StringBuilder(64);
            sb.Append('(');
            for (int i = 0; i < dim; i++) {
                if (i != 0) sb.Append(", ");
                sb.Append(data[start + i].ToString("0.###", CultureInfo.InvariantCulture));
            }
            sb.Append(')');
            return sb.ToString();
        }

        static string ReadAttrName(ulong handle, GeoAttrClass cls, uint idx) {
            var sb = new StringBuilder(512);
            uint ok = NativeMethods.cunning_geo_get_attr_name(handle, (uint)cls, idx, sb, (uint)sb.Capacity);
            return ok == 0 ? "" : sb.ToString();
        }

        static string ReadAttrString(ulong handle, uint attrClass, string attrName, uint index) {
            var sb = new StringBuilder(1024);
            uint ok = NativeMethods.cunning_geo_get_attr_string(handle, attrClass, attrName, index, sb, (uint)sb.Capacity);
            return ok == 0 ? "" : sb.ToString();
        }

        static string ResolveControlKind(WhiteParamInfo p) {
            if (!string.IsNullOrWhiteSpace(p?.ui?.kind)) return NormalizeKind(p.ui.kind);
            var sourceJson = string.IsNullOrWhiteSpace(p?.currentJson) ? p?.baseJson : p.currentJson;
            var value = CdaParamValue.FromDefaultJson(sourceJson);
            return NormalizeKind(value?.kind ?? "Float");
        }

        static string SerializeParamValue(CdaParamValue v) {
            var sb = new StringBuilder(256);
            v.WriteSerdeJson(sb);
            return sb.ToString();
        }

        static string FormatValueSummary(string json) {
            if (string.IsNullOrWhiteSpace(json)) return "(empty)";
            var v = CdaParamValue.FromDefaultJson(json);
            if (v == null) return json;
            if (KindEquals(v.kind, "Float")) return v.f.ToString("0.###", CultureInfo.InvariantCulture);
            if (KindEquals(v.kind, "Int")) return v.i.ToString(CultureInfo.InvariantCulture);
            if (KindEquals(v.kind, "Bool")) return v.b ? "True" : "False";
            if (KindEquals(v.kind, "String")) return v.s ?? "";
            if (KindEquals(v.kind, "Vec2")) { var x = v.AsVec2(); return $"({x.x:0.###}, {x.y:0.###})"; }
            if (KindEquals(v.kind, "Vec3")) { var x = v.AsVec3(); return $"({x.x:0.###}, {x.y:0.###}, {x.z:0.###})"; }
            if (KindEquals(v.kind, "Vec4")) { var x = v.AsVec4(); return $"({x.x:0.###}, {x.y:0.###}, {x.z:0.###}, {x.w:0.###})"; }
            if (KindEquals(v.kind, "Color")) { var c = v.AsColor(false); return $"RGB({c.r:0.###}, {c.g:0.###}, {c.b:0.###})"; }
            if (KindEquals(v.kind, "Color4")) { var c = v.AsColor(true); return $"RGBA({c.r:0.###}, {c.g:0.###}, {c.b:0.###}, {c.a:0.###})"; }
            return json;
        }

        static WhiteParamUi ParseUiMeta(string json) {
            if (string.IsNullOrWhiteSpace(json)) return null;
            if (!CdaMiniJson.TryGetTopLevelString(json, "kind", out var kind)) return null;
            var ui = new WhiteParamUi { kind = kind };

            if (CdaMiniJson.TryGetTopLevelRaw(json, "min", out var minRaw) && TryParseRawFloat(minRaw, out var minV)) ui.min = minV;
            if (CdaMiniJson.TryGetTopLevelRaw(json, "max", out var maxRaw) && TryParseRawFloat(maxRaw, out var maxV)) ui.max = maxV;
            if (CdaMiniJson.TryGetTopLevelRaw(json, "show_alpha", out var alphaRaw) && TryParseRawBool(alphaRaw, out var alphaV)) ui.showAlpha = alphaV;

            if (CdaMiniJson.TryGetTopLevelRaw(json, "choices", out var choicesRaw)) {
                var elems = CdaMiniJson.SplitArrayElements(choicesRaw);
                for (int i = 0; i < elems.Count; i++) {
                    string elem = elems[i];
                    if (!CdaMiniJson.TryGetObjectInt(elem, "value", out var v)) continue;
                    if (!CdaMiniJson.TryGetObjectString(elem, "label", out var label)) label = v.ToString(CultureInfo.InvariantCulture);
                    ui.choices.Add(new WhiteParamUiChoice { label = label, value = v });
                }
            }
            return ui;
        }

        static bool TryParseRawFloat(string raw, out float value) {
            value = 0f;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            return float.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        static bool TryParseRawBool(string raw, out bool value) {
            value = false;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var t = raw.Trim();
            if (t.Equals("true", StringComparison.OrdinalIgnoreCase)) { value = true; return true; }
            if (t.Equals("false", StringComparison.OrdinalIgnoreCase)) { value = false; return true; }
            return false;
        }

        static bool KindEquals(string lhs, string rhs) {
            return string.Equals(lhs ?? "", rhs ?? "", StringComparison.OrdinalIgnoreCase);
        }

        static string NormalizeKind(string kind) {
            if (string.IsNullOrWhiteSpace(kind)) return "Float";
            var k = kind.Trim().ToLowerInvariant();
            return k switch {
                "float" => "Float",
                "floatslider" => "FloatSlider",
                "int" => "Int",
                "intslider" => "IntSlider",
                "bool" => "Bool",
                "toggle" => "Toggle",
                "string" => "String",
                "code" => "Code",
                "filepath" => "FilePath",
                "vec2" => "Vec2",
                "vec2drag" => "Vec2Drag",
                "vec3" => "Vec3",
                "vec3drag" => "Vec3Drag",
                "vec4" => "Vec4",
                "vec4drag" => "Vec4Drag",
                "color" => "Color",
                "color4" => "Color4",
                "dropdown" => "Dropdown",
                _ => kind,
            };
        }

        List<WhitePromotedBinding> GetPromotedBindings(string nodeId, string paramName) {
            var result = new List<WhitePromotedBinding>();
            if (_target == null || _target.asset == null || _target.asset.promoted_params == null) return result;
            for (int i = 0; i < _target.asset.promoted_params.Count; i++) {
                var pp = _target.asset.promoted_params[i];
                if (pp == null || string.IsNullOrEmpty(pp.name) || pp.bindings == null) continue;
                for (int j = 0; j < pp.bindings.Count; j++) {
                    var b = pp.bindings[j];
                    if (b == null) continue;
                    if (!string.Equals(b.node ?? "", nodeId, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.Equals(b.param ?? "", paramName, StringComparison.Ordinal)) continue;
                    result.Add(new WhitePromotedBinding {
                        promotedName = pp.name,
                        promotedDefaultJson = pp.default_value_json,
                        channel = b.channel,
                    });
                }
            }
            return result;
        }

        string BuildCurrentJsonForParam(string nodeId, string paramName, string baseJson, List<WhitePromotedBinding> promotedBindings = null) {
            if (_target == null) return baseJson ?? "";
            promotedBindings ??= GetPromotedBindings(nodeId, paramName);
            if (promotedBindings == null || promotedBindings.Count == 0) {
                return baseJson ?? "";
            }

            var outValue = CdaParamValue.FromDefaultJson(baseJson);
            if (outValue == null) outValue = new CdaParamValue { kind = "Float", f = 0f };
            _target.paramValues ??= new Dictionary<string, CdaParamValue>();
            for (int i = 0; i < promotedBindings.Count; i++) {
                var pb = promotedBindings[i];
                if (pb == null || string.IsNullOrEmpty(pb.promotedName)) continue;
                if (!_target.paramValues.TryGetValue(pb.promotedName, out var src) || src == null) {
                    src = CdaParamValue.FromDefaultJson(pb.promotedDefaultJson);
                }
                if (src == null) continue;
                if (pb.channel.HasValue) {
                    float scalar = ExtractScalar(src, (int)pb.channel.Value);
                    ApplyScalarByChannel(outValue, (int)pb.channel.Value, scalar);
                } else {
                    var cloned = CloneParamValue(src);
                    if (cloned != null) outValue = cloned;
                }
            }
            return SerializeParamValue(outValue);
        }


        static CdaParamValue CloneParamValue(CdaParamValue v) {
            if (v == null) return null;
            return CdaParamValue.FromDefaultJson(SerializeParamValue(v));
        }

        static float ExtractScalar(CdaParamValue v, int preferredChannel) {
            if (v == null) return 0f;
            if (KindEquals(v.kind, "Float")) return v.f;
            if (KindEquals(v.kind, "Int")) return v.i;
            if (KindEquals(v.kind, "Bool")) return v.b ? 1f : 0f;
            if (KindEquals(v.kind, "Vec2") || KindEquals(v.kind, "Vec3") || KindEquals(v.kind, "Vec4") || KindEquals(v.kind, "Color") || KindEquals(v.kind, "Color4")) {
                if (v.a == null || v.a.Length == 0) return 0f;
                int idx = Mathf.Clamp(preferredChannel, 0, v.a.Length - 1);
                return v.a[idx];
            }
            return 0f;
        }

        static bool ApplyScalarByChannel(CdaParamValue dst, int channel, float scalar) {
            if (dst == null) return false;
            if (KindEquals(dst.kind, "Float")) {
                if (!Mathf.Approximately(dst.f, scalar)) {
                    dst.f = scalar;
                    return true;
                }
                return false;
            }
            if (KindEquals(dst.kind, "Int")) {
                int iv = Mathf.RoundToInt(scalar);
                if (dst.i != iv) {
                    dst.i = iv;
                    return true;
                }
                return false;
            }
            if (KindEquals(dst.kind, "Bool")) {
                bool bv = scalar > 0.5f;
                if (dst.b != bv) {
                    dst.b = bv;
                    return true;
                }
                return false;
            }

            int need = 0;
            if (KindEquals(dst.kind, "Vec2")) need = 2;
            else if (KindEquals(dst.kind, "Vec3") || KindEquals(dst.kind, "Color")) need = 3;
            else if (KindEquals(dst.kind, "Vec4") || KindEquals(dst.kind, "Color4")) need = 4;
            if (need == 0) return false;

            dst.a ??= new float[need];
            if (dst.a.Length < need) Array.Resize(ref dst.a, need);
            int idx = Mathf.Clamp(channel, 0, need - 1);
            if (Mathf.Approximately(dst.a[idx], scalar)) return false;
            dst.a[idx] = scalar;
            return true;
        }
    }
}
