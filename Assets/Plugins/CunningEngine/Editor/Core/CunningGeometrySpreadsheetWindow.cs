using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CunningEngine.Editor {
    public sealed class CunningGeometrySpreadsheetWindow : EditorWindow {
        const string MenuPath = "Procedural/Cunning Engine/Geometry Spreadsheet";
        const float HeaderHeight = 22f;
        const float RowHeight = 20f;
        const float FooterHeight = 22f;
        const double RefreshIntervalSeconds = 0.15d;

        static readonly string[] DomainLabels = { "Points", "Vertices", "Primitives", "Edges", "Detail" };
        static readonly Color ProHeaderColor = new Color(0.17f, 0.18f, 0.20f, 1f);
        static readonly Color PersonalHeaderColor = new Color(0.78f, 0.80f, 0.83f, 1f);
        static readonly Color ProRowEvenColor = new Color(0.15f, 0.15f, 0.16f, 1f);
        static readonly Color ProRowOddColor = new Color(0.13f, 0.13f, 0.14f, 1f);
        static readonly Color PersonalRowEvenColor = new Color(0.95f, 0.95f, 0.95f, 1f);
        static readonly Color PersonalRowOddColor = new Color(0.92f, 0.92f, 0.92f, 0.92f);
        static readonly Color ProSelectionColor = new Color(0.18f, 0.36f, 0.62f, 0.90f);
        static readonly Color PersonalSelectionColor = new Color(0.31f, 0.52f, 0.84f, 0.90f);
        static readonly Color ProGridColor = new Color(0.23f, 0.24f, 0.26f, 1f);
        static readonly Color PersonalGridColor = new Color(0.80f, 0.80f, 0.80f, 1f);

        enum SpreadsheetDomain {
            Points = 0,
            Vertices = 1,
            Primitives = 2,
            Edges = 3,
            Detail = 4,
        }

        enum GeoAttrClass : uint {
            Detail = 0,
            Point = 1,
            Vertex = 2,
            Primitive = 3,
            Edge = 4,
        }

        enum GeoAttrType : uint {
            Unknown = 0,
            F32 = 1,
            Vec2 = 2,
            Vec3 = 3,
            Vec4 = 4,
            I32 = 5,
            IVec2 = 6,
            BoolU8 = 7,
            F64 = 8,
            DVec2 = 9,
            DVec3 = 10,
            DVec4 = 11,
            StringUtf8 = 12,
            BytesU8 = 13,
        }

        enum BuiltinColumnKind {
            None = 0,
            PointIndex,
            VertexIndex,
            VertexPoint,
            PrimitiveIndex,
            PrimitiveVertices,
            EdgeIndex,
            EdgeP0,
            EdgeP1,
            DetailAttribute,
            DetailValue,
        }

        sealed class AttributeInfo {
            public string name;
            public GeoAttrType type;
            public int length;
        }

        sealed class SpreadsheetColumn {
            public string title;
            public string attrName;
            public GeoAttrType attrType;
            public BuiltinColumnKind builtinKind;
            public int component = -1;
            public bool isGroup;
            public float width;
        }

        sealed class GroupBuffer {
            public string name;
            public int length;
            public ulong[] bits = Array.Empty<ulong>();

            public bool Contains(int index) {
                if (index < 0 || index >= length || bits == null || bits.Length == 0) {
                    return false;
                }

                int block = index >> 6;
                int bit = index & 63;
                if (block < 0 || block >= bits.Length) {
                    return false;
                }

                return (bits[block] & (1UL << bit)) != 0UL;
            }
        }

        sealed class AttributeBuffer {
            public string name;
            public GeoAttrType type;
            public int length;
            public float[] f32;
            public int[] i32;
            public double[] f64;
            public byte[] u8;
            public string[] strings;

            public string FormatComponent(int rowIndex, int component) {
                if (rowIndex < 0 || rowIndex >= length) {
                    return string.Empty;
                }

                switch (type) {
                    case GeoAttrType.F32:
                        return FormatFloat(GetValue(f32, rowIndex));
                    case GeoAttrType.Vec2:
                    case GeoAttrType.Vec3:
                    case GeoAttrType.Vec4:
                        return FormatFloat(GetValue(f32, rowIndex * GetStride(type) + component));
                    case GeoAttrType.I32:
                        return FormatInt(GetValue(i32, rowIndex));
                    case GeoAttrType.IVec2:
                        return FormatInt(GetValue(i32, rowIndex * GetStride(type) + component));
                    case GeoAttrType.BoolU8:
                        return GetValue(u8, rowIndex) != 0 ? "true" : "false";
                    case GeoAttrType.F64:
                        return FormatDouble(GetValue(f64, rowIndex));
                    case GeoAttrType.DVec2:
                    case GeoAttrType.DVec3:
                    case GeoAttrType.DVec4:
                        return FormatDouble(GetValue(f64, rowIndex * GetStride(type) + component));
                    case GeoAttrType.StringUtf8:
                        return GetValue(strings, rowIndex);
                    case GeoAttrType.BytesU8:
                        return u8 != null && rowIndex < u8.Length ? u8[rowIndex].ToString(CultureInfo.InvariantCulture) : string.Empty;
                    default:
                        return string.Empty;
                }
            }

            public string FormatSummary() {
                switch (type) {
                    case GeoAttrType.F32: return "F32[" + length.ToString(CultureInfo.InvariantCulture) + "]";
                    case GeoAttrType.Vec2: return "Vec2[" + length.ToString(CultureInfo.InvariantCulture) + "]";
                    case GeoAttrType.Vec3: return "Vec3[" + length.ToString(CultureInfo.InvariantCulture) + "]";
                    case GeoAttrType.Vec4: return "Vec4[" + length.ToString(CultureInfo.InvariantCulture) + "]";
                    case GeoAttrType.I32: return "I32[" + length.ToString(CultureInfo.InvariantCulture) + "]";
                    case GeoAttrType.IVec2: return "IVec2[" + length.ToString(CultureInfo.InvariantCulture) + "]";
                    case GeoAttrType.BoolU8: return "Bool[" + length.ToString(CultureInfo.InvariantCulture) + "]";
                    case GeoAttrType.F64: return "F64[" + length.ToString(CultureInfo.InvariantCulture) + "]";
                    case GeoAttrType.DVec2: return "DVec2[" + length.ToString(CultureInfo.InvariantCulture) + "]";
                    case GeoAttrType.DVec3: return "DVec3[" + length.ToString(CultureInfo.InvariantCulture) + "]";
                    case GeoAttrType.DVec4: return "DVec4[" + length.ToString(CultureInfo.InvariantCulture) + "]";
                    case GeoAttrType.StringUtf8: return "String[" + length.ToString(CultureInfo.InvariantCulture) + "]";
                    case GeoAttrType.BytesU8: return "Bytes[" + (u8 != null ? u8.Length : 0).ToString(CultureInfo.InvariantCulture) + "]";
                    default: return "Unknown";
                }
            }

            static int GetValue(int[] values, int index) {
                return values != null && index >= 0 && index < values.Length ? values[index] : 0;
            }

            static float GetValue(float[] values, int index) {
                return values != null && index >= 0 && index < values.Length ? values[index] : 0f;
            }

            static double GetValue(double[] values, int index) {
                return values != null && index >= 0 && index < values.Length ? values[index] : 0d;
            }

            static byte GetValue(byte[] values, int index) {
                return values != null && index >= 0 && index < values.Length ? values[index] : (byte)0;
            }

            static string GetValue(string[] values, int index) {
                return values != null && index >= 0 && index < values.Length ? values[index] ?? string.Empty : string.Empty;
            }

            static string FormatFloat(float value) {
                return value.ToString("0.###", CultureInfo.InvariantCulture);
            }

            static string FormatDouble(double value) {
                return value.ToString("0.###", CultureInfo.InvariantCulture);
            }

            static string FormatInt(int value) {
                return value.ToString(CultureInfo.InvariantCulture);
            }
        }

        sealed class SpreadsheetSnapshot {
            public SpreadsheetDomain domain;
            public int rowCount;
            public string emptyMessage;
            public string infoMessage;
            public string warningMessage;
            public readonly List<SpreadsheetColumn> columns = new List<SpreadsheetColumn>();
            public readonly Dictionary<string, AttributeBuffer> attributes = new Dictionary<string, AttributeBuffer>(StringComparer.Ordinal);
            public readonly Dictionary<string, GroupBuffer> groups = new Dictionary<string, GroupBuffer>(StringComparer.Ordinal);
            public int[] vertexToPoint = Array.Empty<int>();
            public int[] primitiveVertexCounts = Array.Empty<int>();
            public int[] edgePointPairs = Array.Empty<int>();
            public string[] detailNames = Array.Empty<string>();
            public string[] detailValues = Array.Empty<string>();

            public string GetCellText(int rowIndex, int columnIndex) {
                if (columnIndex < 0 || columnIndex >= columns.Count) {
                    return string.Empty;
                }

                var column = columns[columnIndex];
                if (column.builtinKind != BuiltinColumnKind.None) {
                    return GetBuiltinCellText(rowIndex, column.builtinKind);
                }

                if (column.isGroup) {
                    GroupBuffer group;
                    if (groups.TryGetValue(column.attrName, out group)) {
                        return group.Contains(rowIndex) ? "1" : "0";
                    }
                    return string.Empty;
                }

                AttributeBuffer attribute;
                if (!attributes.TryGetValue(column.attrName, out attribute)) {
                    return string.Empty;
                }

                return attribute.FormatComponent(rowIndex, column.component);
            }

            string GetBuiltinCellText(int rowIndex, BuiltinColumnKind kind) {
                switch (kind) {
                    case BuiltinColumnKind.PointIndex:
                    case BuiltinColumnKind.VertexIndex:
                    case BuiltinColumnKind.PrimitiveIndex:
                    case BuiltinColumnKind.EdgeIndex:
                        return rowIndex.ToString(CultureInfo.InvariantCulture);
                    case BuiltinColumnKind.VertexPoint:
                        return rowIndex >= 0 && rowIndex < vertexToPoint.Length && vertexToPoint[rowIndex] >= 0
                            ? vertexToPoint[rowIndex].ToString(CultureInfo.InvariantCulture)
                            : string.Empty;
                    case BuiltinColumnKind.PrimitiveVertices:
                        return rowIndex >= 0 && rowIndex < primitiveVertexCounts.Length
                            ? primitiveVertexCounts[rowIndex].ToString(CultureInfo.InvariantCulture)
                            : string.Empty;
                    case BuiltinColumnKind.EdgeP0:
                        return rowIndex >= 0 && (rowIndex * 2) < edgePointPairs.Length
                            ? edgePointPairs[rowIndex * 2].ToString(CultureInfo.InvariantCulture)
                            : string.Empty;
                    case BuiltinColumnKind.EdgeP1:
                        return rowIndex >= 0 && (rowIndex * 2 + 1) < edgePointPairs.Length
                            ? edgePointPairs[rowIndex * 2 + 1].ToString(CultureInfo.InvariantCulture)
                            : string.Empty;
                    case BuiltinColumnKind.DetailAttribute:
                        return rowIndex >= 0 && rowIndex < detailNames.Length ? detailNames[rowIndex] ?? string.Empty : string.Empty;
                    case BuiltinColumnKind.DetailValue:
                        return rowIndex >= 0 && rowIndex < detailValues.Length ? detailValues[rowIndex] ?? string.Empty : string.Empty;
                    default:
                        return string.Empty;
                }
            }
        }

        MonoBehaviour _targetBehaviour;
        ICunningInputHandle _targetHandle;
        SpreadsheetDomain _domain;
        SpreadsheetSnapshot _snapshot;
        Vector2 _tableScroll;
        int _selectedRow = -1;
        bool _followSelection = true;
        bool _pinned;
        string _columnFilter = string.Empty;
        string _statusMessage = string.Empty;
        MessageType _statusType = MessageType.Info;
        ulong _cachedHandle;
        ulong _cachedDirtyId;
        SpreadsheetDomain _cachedDomain;
        string _cachedFilter = string.Empty;
        int _pointCount;
        int _vertexCount;
        int _primitiveCount;
        double _lastRefreshTime;
        static bool s_groupApiAvailable = true;
        static bool s_edgeApiAvailable = true;
        GUIStyle _panelStyle;
        GUIStyle _titleStyle;
        GUIStyle _subtleStyle;
        GUIStyle _countLabelStyle;
        GUIStyle _countValueStyle;
        GUIStyle _headerCellStyle;
        GUIStyle _cellStyle;
        GUIStyle _indexCellStyle;
        GUIStyle _footerStyle;
        GUIStyle _toolbarSearchFieldStyle;
        GUIStyle _toolbarSearchCancelStyle;
        GUIStyle _emptyStyle;

        [MenuItem(MenuPath, false, 112)]
        static void OpenWindow() {
            var window = GetWindow<CunningGeometrySpreadsheetWindow>("Geometry Spreadsheet");
            window.minSize = new Vector2(920f, 420f);
            window.Show();
        }

        void OnEnable() {
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.update += OnEditorUpdate;
            EnsureStyles();
            SyncTargetFromSelection(true);
            ForceRefresh();
        }

        void OnDisable() {
            Selection.selectionChanged -= OnSelectionChanged;
            EditorApplication.update -= OnEditorUpdate;
        }

        void OnSelectionChanged() {
            if (_pinned || !_followSelection) {
                return;
            }

            SyncTargetFromSelection(true);
            ForceRefresh();
            Repaint();
        }

        void OnEditorUpdate() {
            if (EditorApplication.timeSinceStartup - _lastRefreshTime < RefreshIntervalSeconds) {
                return;
            }

            _lastRefreshTime = EditorApplication.timeSinceStartup;

            if (_followSelection && !_pinned) {
                SyncTargetFromSelection(false);
            }

            if (RefreshIfNeeded(false)) {
                Repaint();
            }
        }

        void OnGUI() {
            EnsureStyles();
            DrawTopPanel();

            if (!string.IsNullOrEmpty(_statusMessage)) {
                EditorGUILayout.HelpBox(_statusMessage, _statusType);
            }

            if (_snapshot != null && !string.IsNullOrEmpty(_snapshot.warningMessage)) {
                EditorGUILayout.HelpBox(_snapshot.warningMessage, MessageType.Warning);
            }

            if (_snapshot != null && !string.IsNullOrEmpty(_snapshot.infoMessage)) {
                EditorGUILayout.HelpBox(_snapshot.infoMessage, MessageType.Info);
            }

            var tableRect = GUILayoutUtility.GetRect(10f, 100000f, 100f, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawTableArea(tableRect);
        }

        void DrawTopPanel() {
            EditorGUILayout.BeginVertical(_panelStyle);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Geometry Spreadsheet", _titleStyle);
            GUILayout.FlexibleSpace();

            bool followSelection = GUILayout.Toggle(_followSelection, "Follow Selection", EditorStyles.toolbarButton, GUILayout.Width(110f));
            if (followSelection != _followSelection) {
                _followSelection = followSelection;
                if (_followSelection && !_pinned) {
                    SyncTargetFromSelection(true);
                    ForceRefresh();
                }
            }

            bool pinRequested = GUILayout.Toggle(_pinned, _pinned ? "Pinned" : "Pin", EditorStyles.toolbarButton, GUILayout.Width(60f));
            if (pinRequested != _pinned) {
                _pinned = pinRequested;
                if (!_pinned && _followSelection) {
                    SyncTargetFromSelection(true);
                    ForceRefresh();
                }
            }

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(64f))) {
                ForceRefresh();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(3f);

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(true)) {
                EditorGUILayout.ObjectField("Target", _targetBehaviour, typeof(MonoBehaviour), true);
            }
            if (_targetBehaviour != null && GUILayout.Button("Ping", GUILayout.Width(48f))) {
                EditorGUIUtility.PingObject(_targetBehaviour);
                Selection.activeObject = _targetBehaviour;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Handle", _countLabelStyle, GUILayout.Width(44f));
            GUILayout.Label(_targetHandle != null ? _targetHandle.CurrentHandle.ToString(CultureInfo.InvariantCulture) : "0", _countValueStyle, GUILayout.Width(120f));
            GUILayout.Space(6f);
            GUILayout.Label("Dirty", _countLabelStyle, GUILayout.Width(32f));
            GUILayout.Label(_cachedHandle != 0 ? _cachedDirtyId.ToString(CultureInfo.InvariantCulture) : "-", _countValueStyle, GUILayout.Width(120f));
            GUILayout.Space(12f);
            DrawCountBadge("P", _pointCount);
            DrawCountBadge("V", _vertexCount);
            DrawCountBadge("Prim", _primitiveCount);
            GUILayout.FlexibleSpace();
            GUILayout.Label(_targetBehaviour != null ? _targetBehaviour.GetType().Name : CunningInputUtility.GetSupportedTypesHint(), _subtleStyle);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            int nextDomain = GUILayout.Toolbar((int)_domain, DomainLabels, EditorStyles.toolbarButton, GUILayout.MinHeight(20f));
            if (nextDomain != (int)_domain) {
                _domain = (SpreadsheetDomain)nextDomain;
                _selectedRow = -1;
                ForceRefresh();
            }

            GUILayout.FlexibleSpace();

            string nextFilter = DrawToolbarSearchField(_columnFilter, GUILayout.Width(240f));
            if (!string.Equals(nextFilter, _columnFilter, StringComparison.Ordinal)) {
                _columnFilter = nextFilter;
                ForceRefresh();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        void DrawCountBadge(string label, int value) {
            GUILayout.Label(label, _countLabelStyle, GUILayout.Width(label == "Prim" ? 36f : 18f));
            GUILayout.Label(value.ToString(CultureInfo.InvariantCulture), _countValueStyle, GUILayout.Width(56f));
            GUILayout.Space(6f);
        }

        void DrawTableArea(Rect rect) {
            if (_targetBehaviour == null || _targetHandle == null || _targetHandle.CurrentHandle == 0) {
                DrawCenteredMessage(rect, "Select a Cunning target to inspect its live geometry.");
                return;
            }

            if (_snapshot == null) {
                DrawCenteredMessage(rect, "Geometry data is not ready yet.");
                return;
            }

            if (_snapshot.rowCount <= 0) {
                DrawCenteredMessage(rect, string.IsNullOrEmpty(_snapshot.emptyMessage) ? "No data in this domain." : _snapshot.emptyMessage);
                return;
            }

            float bodyHeight = Mathf.Max(0f, rect.height - HeaderHeight - FooterHeight);
            float totalWidth = CalculateTableWidth(_snapshot.columns, rect.width);
            float totalHeight = _snapshot.rowCount * RowHeight;

            var headerRect = new Rect(rect.x, rect.y, rect.width, HeaderHeight);
            var bodyRect = new Rect(rect.x, rect.y + HeaderHeight, rect.width, bodyHeight);
            var footerRect = new Rect(rect.x, rect.yMax - FooterHeight, rect.width, FooterHeight);

            DrawHeader(headerRect, totalWidth);
            DrawBody(bodyRect, totalWidth, totalHeight);
            DrawFooter(footerRect);
            HandleBodyClick(bodyRect);
        }

        void DrawHeader(Rect rect, float totalWidth) {
            GUI.BeginGroup(rect);
            EditorGUI.DrawRect(new Rect(0f, 0f, rect.width, rect.height), EditorGUIUtility.isProSkin ? ProHeaderColor : PersonalHeaderColor);

            float x = -_tableScroll.x;
            for (int i = 0; i < _snapshot.columns.Count; i++) {
                var column = _snapshot.columns[i];
                var columnRect = new Rect(x, 0f, column.width, rect.height);
                if (columnRect.xMax >= 0f && columnRect.xMin <= rect.width) {
                    GUI.Label(columnRect, column.title, _headerCellStyle);
                    DrawVerticalSeparator(columnRect.xMax - 0.5f, rect.height);
                }
                x += column.width;
            }

            GUI.EndGroup();
        }

        void DrawBody(Rect rect, float totalWidth, float totalHeight) {
            var viewRect = new Rect(0f, 0f, totalWidth, totalHeight);
            _tableScroll = GUI.BeginScrollView(rect, _tableScroll, viewRect);

            int firstRow = Mathf.Max(0, Mathf.FloorToInt(_tableScroll.y / RowHeight));
            int visibleRowCount = Mathf.CeilToInt(rect.height / RowHeight) + 2;
            int lastRow = Mathf.Min(_snapshot.rowCount - 1, firstRow + visibleRowCount);

            for (int rowIndex = firstRow; rowIndex <= lastRow; rowIndex++) {
                float rowY = rowIndex * RowHeight;
                var rowRect = new Rect(0f, rowY, totalWidth, RowHeight);
                bool isSelected = rowIndex == _selectedRow;
                bool isEven = (rowIndex & 1) == 0;
                var rowColor = isSelected
                    ? (EditorGUIUtility.isProSkin ? ProSelectionColor : PersonalSelectionColor)
                    : (isEven ? (EditorGUIUtility.isProSkin ? ProRowEvenColor : PersonalRowEvenColor) : (EditorGUIUtility.isProSkin ? ProRowOddColor : PersonalRowOddColor));
                EditorGUI.DrawRect(rowRect, rowColor);

                float x = 0f;
                for (int columnIndex = 0; columnIndex < _snapshot.columns.Count; columnIndex++) {
                    var column = _snapshot.columns[columnIndex];
                    var cellRect = new Rect(x, rowY, column.width, RowHeight);
                    string text = _snapshot.GetCellText(rowIndex, columnIndex);
                    GUI.Label(cellRect, text, column.builtinKind != BuiltinColumnKind.None ? _indexCellStyle : _cellStyle);
                    DrawVerticalSeparator(cellRect.xMax - 0.5f, cellRect.height, cellRect.xMax - 0.5f, cellRect.yMin);
                    x += column.width;
                }

                DrawHorizontalSeparator(rowRect.yMax - 0.5f, totalWidth);
            }

            GUI.EndScrollView();
        }

        void DrawFooter(Rect rect) {
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? ProHeaderColor : PersonalHeaderColor);
            GUI.BeginGroup(rect);
            string leftText = "Rows: " + _snapshot.rowCount.ToString(CultureInfo.InvariantCulture);
            if (_selectedRow >= 0 && _selectedRow < _snapshot.rowCount) {
                leftText += "    Selected: " + _selectedRow.ToString(CultureInfo.InvariantCulture);
            }

            GUI.Label(new Rect(8f, 0f, rect.width - 16f, rect.height), leftText, _footerStyle);
            GUI.EndGroup();
        }

        void HandleBodyClick(Rect bodyRect) {
            var current = Event.current;
            if (current.type != EventType.MouseDown || current.button != 0 || !bodyRect.Contains(current.mousePosition)) {
                return;
            }

            float localY = current.mousePosition.y - bodyRect.y + _tableScroll.y;
            int rowIndex = Mathf.FloorToInt(localY / RowHeight);
            if (rowIndex >= 0 && rowIndex < _snapshot.rowCount) {
                _selectedRow = rowIndex;
                Repaint();
                current.Use();
            }
        }

        void DrawCenteredMessage(Rect rect, string message) {
            GUI.Label(rect, message, _emptyStyle);
        }

        void DrawVerticalSeparator(float x, float height, float absoluteX = float.NaN, float y = 0f) {
            float drawX = float.IsNaN(absoluteX) ? x : absoluteX;
            EditorGUI.DrawRect(new Rect(drawX, y, 1f, height), EditorGUIUtility.isProSkin ? ProGridColor : PersonalGridColor);
        }

        void DrawHorizontalSeparator(float y, float width) {
            EditorGUI.DrawRect(new Rect(0f, y, width, 1f), EditorGUIUtility.isProSkin ? ProGridColor : PersonalGridColor);
        }

        string DrawToolbarSearchField(string value, params GUILayoutOption[] layout) {
            EnsureStyles();
            Rect rect = GUILayoutUtility.GetRect(1f, 18f, layout);
            Rect fieldRect = rect;
            fieldRect.width -= 18f;
            Rect cancelRect = new Rect(fieldRect.xMax, rect.y, 18f, rect.height);

            value = EditorGUI.TextField(fieldRect, value ?? string.Empty, _toolbarSearchFieldStyle);
            if (GUI.Button(cancelRect, GUIContent.none, _toolbarSearchCancelStyle)) {
                value = string.Empty;
                GUI.FocusControl(null);
            }

            return value;
        }

        void EnsureStyles() {
            if (_panelStyle == null) {
                _panelStyle = new GUIStyle(EditorStyles.helpBox) {
                    padding = new RectOffset(10, 10, 8, 8),
                    margin = new RectOffset(0, 0, 0, 8),
                };
            }

            if (_titleStyle == null) {
                _titleStyle = new GUIStyle(EditorStyles.boldLabel) {
                    fontSize = 14,
                    alignment = TextAnchor.MiddleLeft,
                };
            }

            if (_subtleStyle == null) {
                _subtleStyle = new GUIStyle(EditorStyles.miniLabel) {
                    alignment = TextAnchor.MiddleRight,
                    clipping = TextClipping.Clip,
                };
            }

            if (_countLabelStyle == null) {
                _countLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel) {
                    alignment = TextAnchor.MiddleLeft,
                };
            }

            if (_countValueStyle == null) {
                _countValueStyle = new GUIStyle(EditorStyles.miniLabel) {
                    alignment = TextAnchor.MiddleLeft,
                };
            }

            if (_headerCellStyle == null) {
                _headerCellStyle = new GUIStyle(EditorStyles.miniBoldLabel) {
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Clip,
                    padding = new RectOffset(6, 6, 2, 2),
                };
            }

            if (_cellStyle == null) {
                _cellStyle = new GUIStyle(EditorStyles.label) {
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Clip,
                    padding = new RectOffset(6, 6, 1, 1),
                };
            }

            if (_indexCellStyle == null) {
                _indexCellStyle = new GUIStyle(_cellStyle) {
                    normal = {
                        textColor = EditorGUIUtility.isProSkin
                            ? new Color(0.84f, 0.84f, 0.84f)
                            : new Color(0.16f, 0.16f, 0.16f)
                    },
                };
            }

            if (_footerStyle == null) {
                _footerStyle = new GUIStyle(EditorStyles.miniLabel) {
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Clip,
                    padding = new RectOffset(4, 4, 2, 2),
                };
            }

            if (_emptyStyle == null) {
                _emptyStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel) {
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true,
                    fontSize = 12,
                };
            }

            if (_toolbarSearchFieldStyle == null) {
                _toolbarSearchFieldStyle = GUI.skin.FindStyle("ToolbarSeachTextField");
                if (_toolbarSearchFieldStyle == null) {
                    _toolbarSearchFieldStyle = GUI.skin.FindStyle("ToolbarSearchTextField");
                }
                if (_toolbarSearchFieldStyle == null) {
                    _toolbarSearchFieldStyle = new GUIStyle(EditorStyles.toolbarTextField);
                }
            }

            if (_toolbarSearchCancelStyle == null) {
                _toolbarSearchCancelStyle = GUI.skin.FindStyle("ToolbarSeachCancelButton");
                if (_toolbarSearchCancelStyle == null) {
                    _toolbarSearchCancelStyle = GUI.skin.FindStyle("ToolbarSearchCancelButton");
                }
                if (_toolbarSearchCancelStyle == null) {
                    _toolbarSearchCancelStyle = new GUIStyle(EditorStyles.toolbarButton);
                }
            }
        }

        void ForceRefresh() {
            _cachedHandle = 0;
            _cachedDirtyId = 0;
            _cachedFilter = string.Empty;
            _cachedDomain = SpreadsheetDomain.Points;
            RefreshIfNeeded(true);
        }

        bool RefreshIfNeeded(bool force) {
            if (_targetBehaviour == null) {
                ClearSnapshot();
                SetStatus(string.Empty, MessageType.Info);
                return false;
            }

            try {
                NativeMethods.EnsureLoaded();
            } catch (Exception exception) {
                ClearSnapshot();
                SetStatus("Cunning native bridge is not available: " + exception.Message, MessageType.Error);
                return true;
            }

            _targetHandle = _targetBehaviour as ICunningInputHandle;
            ulong handle = _targetHandle != null ? _targetHandle.CurrentHandle : 0UL;
            if (handle == 0UL) {
                ClearSnapshot();
                SetStatus("Selected target does not currently own a live Cunning geometry handle.", MessageType.Warning);
                return true;
            }

            ulong dirtyId;
            try {
                dirtyId = NativeMethods.cunning_geo_get_dirty_id(handle);
                _pointCount = (int)NativeMethods.cunning_geo_get_point_count(handle);
                _vertexCount = (int)NativeMethods.cunning_geo_get_vertex_count(handle);
                _primitiveCount = (int)NativeMethods.cunning_geo_get_prim_count(handle);
            } catch (Exception exception) {
                ClearSnapshot();
                SetStatus("Failed to inspect selected geometry: " + exception.Message, MessageType.Error);
                return true;
            }

            if (!force
                && handle == _cachedHandle
                && dirtyId == _cachedDirtyId
                && _domain == _cachedDomain
                && string.Equals(_columnFilter, _cachedFilter, StringComparison.Ordinal)) {
                return false;
            }

            try {
                _snapshot = BuildSnapshot(handle, _domain, _columnFilter);
                _cachedHandle = handle;
                _cachedDirtyId = dirtyId;
                _cachedDomain = _domain;
                _cachedFilter = _columnFilter ?? string.Empty;
                SetStatus(string.Empty, MessageType.Info);
            } catch (Exception exception) {
                ClearSnapshot();
                SetStatus("Geometry Spreadsheet rebuild failed: " + exception.Message, MessageType.Error);
            }

            return true;
        }

        void ClearSnapshot() {
            _snapshot = null;
            _selectedRow = -1;
            _pointCount = 0;
            _vertexCount = 0;
            _primitiveCount = 0;
        }

        void SetStatus(string message, MessageType type) {
            _statusMessage = message ?? string.Empty;
            _statusType = type;
        }

        void SyncTargetFromSelection(bool clearWhenUnsupported) {
            var resolved = ResolveSelectionTarget();
            if (resolved == _targetBehaviour) {
                if (resolved != null) {
                    _targetHandle = resolved as ICunningInputHandle;
                }
                return;
            }

            if (resolved == null && !clearWhenUnsupported) {
                return;
            }

            _targetBehaviour = resolved;
            _targetHandle = resolved as ICunningInputHandle;
            _selectedRow = -1;
        }

        MonoBehaviour ResolveSelectionTarget() {
            UnityEngine.Object selected = Selection.activeObject;
            if (selected == null && Selection.activeGameObject != null) {
                selected = Selection.activeGameObject;
            }

            if (selected == null) {
                return null;
            }

            var resolved = CunningInputUtility.Resolve(selected);
            if (resolved != null) {
                return resolved;
            }

            if (Selection.activeGameObject != null && selected != Selection.activeGameObject) {
                return CunningInputUtility.Resolve(Selection.activeGameObject);
            }

            return null;
        }

        SpreadsheetSnapshot BuildSnapshot(ulong handle, SpreadsheetDomain domain, string filter) {
            var snapshot = new SpreadsheetSnapshot();
            snapshot.domain = domain;

            switch (domain) {
                case SpreadsheetDomain.Points:
                    BuildPointSnapshot(snapshot, handle, filter);
                    break;
                case SpreadsheetDomain.Vertices:
                    BuildVertexSnapshot(snapshot, handle, filter);
                    break;
                case SpreadsheetDomain.Primitives:
                    BuildPrimitiveSnapshot(snapshot, handle, filter);
                    break;
                case SpreadsheetDomain.Edges:
                    BuildEdgeSnapshot(snapshot, handle, filter);
                    break;
                case SpreadsheetDomain.Detail:
                    BuildDetailSnapshot(snapshot, handle, filter);
                    break;
            }

            return snapshot;
        }

        void BuildPointSnapshot(SpreadsheetSnapshot snapshot, ulong handle, string filter) {
            snapshot.rowCount = _pointCount;
            if (snapshot.rowCount <= 0) {
                snapshot.emptyMessage = "No Points in this geometry.";
                return;
            }

            snapshot.columns.Add(CreateBuiltinColumn("Point", BuiltinColumnKind.PointIndex, 68f));
            var attrs = ReadAttributeInfos(handle, GeoAttrClass.Point);
            attrs.Sort(ComparePointAttributes);
            AddAttributeColumns(snapshot, handle, GeoAttrClass.Point, attrs, filter);
            AddGroupColumns(snapshot, handle, GeoAttrClass.Point, snapshot.rowCount, filter);
        }

        void BuildVertexSnapshot(SpreadsheetSnapshot snapshot, ulong handle, string filter) {
            snapshot.rowCount = _vertexCount;
            if (snapshot.rowCount <= 0) {
                snapshot.emptyMessage = "No Vertices in this geometry.";
                return;
            }

            snapshot.columns.Add(CreateBuiltinColumn("Vertex", BuiltinColumnKind.VertexIndex, 72f));
            snapshot.columns.Add(CreateBuiltinColumn("Point", BuiltinColumnKind.VertexPoint, 72f));
            snapshot.vertexToPoint = BuildVertexToPointMap(handle, _primitiveCount, _vertexCount);

            var attrs = ReadAttributeInfos(handle, GeoAttrClass.Vertex);
            attrs.Sort(CompareAlphabeticalAttributes);
            AddAttributeColumns(snapshot, handle, GeoAttrClass.Vertex, attrs, filter);
            AddGroupColumns(snapshot, handle, GeoAttrClass.Vertex, snapshot.rowCount, filter);
        }

        void BuildPrimitiveSnapshot(SpreadsheetSnapshot snapshot, ulong handle, string filter) {
            snapshot.rowCount = _primitiveCount;
            if (snapshot.rowCount <= 0) {
                snapshot.emptyMessage = "No Primitives in this geometry.";
                return;
            }

            snapshot.columns.Add(CreateBuiltinColumn("Primitive", BuiltinColumnKind.PrimitiveIndex, 84f));
            snapshot.columns.Add(CreateBuiltinColumn("Vertices", BuiltinColumnKind.PrimitiveVertices, 76f));
            snapshot.primitiveVertexCounts = BuildPrimitiveVertexCounts(handle, _primitiveCount);

            var attrs = ReadAttributeInfos(handle, GeoAttrClass.Primitive);
            attrs.Sort(CompareAlphabeticalAttributes);
            AddAttributeColumns(snapshot, handle, GeoAttrClass.Primitive, attrs, filter);
            AddGroupColumns(snapshot, handle, GeoAttrClass.Primitive, snapshot.rowCount, filter);
        }

        void BuildEdgeSnapshot(SpreadsheetSnapshot snapshot, ulong handle, string filter) {
            if (!s_edgeApiAvailable) {
                snapshot.emptyMessage = "Explicit edge export is not available in the current native bridge.";
                snapshot.infoMessage = "Edges in Cunning are implicit by default. This tab will light up automatically once the explicit-edge exports are present.";
                return;
            }

            snapshot.rowCount = GetEdgeCountSafe(handle);
            if (snapshot.rowCount <= 0) {
                snapshot.emptyMessage = "No explicit edges found (geometry may be implicit).";
                return;
            }

            snapshot.columns.Add(CreateBuiltinColumn("Edge", BuiltinColumnKind.EdgeIndex, 68f));
            snapshot.columns.Add(CreateBuiltinColumn("P0", BuiltinColumnKind.EdgeP0, 68f));
            snapshot.columns.Add(CreateBuiltinColumn("P1", BuiltinColumnKind.EdgeP1, 68f));
            snapshot.edgePointPairs = ReadEdgePointPairsSafe(handle, snapshot.rowCount);

            var attrs = ReadAttributeInfos(handle, GeoAttrClass.Edge);
            attrs.Sort(CompareAlphabeticalAttributes);
            AddAttributeColumns(snapshot, handle, GeoAttrClass.Edge, attrs, filter);
            AddGroupColumns(snapshot, handle, GeoAttrClass.Edge, snapshot.rowCount, filter);
        }

        void BuildDetailSnapshot(SpreadsheetSnapshot snapshot, ulong handle, string filter) {
            snapshot.columns.Add(CreateBuiltinColumn("Attribute", BuiltinColumnKind.DetailAttribute, 240f));
            snapshot.columns.Add(CreateBuiltinColumn("Value", BuiltinColumnKind.DetailValue, 320f));

            var attrs = ReadAttributeInfos(handle, GeoAttrClass.Detail);
            attrs.Sort(CompareAlphabeticalAttributes);

            var filtered = new List<AttributeInfo>();
            for (int i = 0; i < attrs.Count; i++) {
                if (MatchesFilter(attrs[i].name, filter)) {
                    filtered.Add(attrs[i]);
                }
            }

            if (filtered.Count <= 0) {
                snapshot.emptyMessage = attrs.Count <= 0 ? "No detail attributes." : "No detail attributes match the current filter.";
                return;
            }

            snapshot.rowCount = filtered.Count;
            snapshot.detailNames = new string[filtered.Count];
            snapshot.detailValues = new string[filtered.Count];

            for (int i = 0; i < filtered.Count; i++) {
                var info = filtered[i];
                var buffer = ReadAttributeBuffer(handle, GeoAttrClass.Detail, info);
                snapshot.detailNames[i] = info.name;
                snapshot.detailValues[i] = buffer != null ? buffer.FormatSummary() : "Unknown";
            }
        }

        void AddAttributeColumns(SpreadsheetSnapshot snapshot, ulong handle, GeoAttrClass attrClass, List<AttributeInfo> attrs, string filter) {
            for (int i = 0; i < attrs.Count; i++) {
                var attr = attrs[i];
                if (!MatchesFilter(attr.name, filter)) {
                    continue;
                }

                var buffer = ReadAttributeBuffer(handle, attrClass, attr);
                if (buffer == null) {
                    continue;
                }

                snapshot.attributes[attr.name] = buffer;
                int componentCount = GetDisplayComponentCount(attr.type);
                if (componentCount <= 1) {
                    snapshot.columns.Add(new SpreadsheetColumn {
                        title = BuildColumnTitle(attr.name, -1),
                        attrName = attr.name,
                        attrType = attr.type,
                        width = GetSuggestedWidth(attr.type, false),
                    });
                } else {
                    for (int component = 0; component < componentCount; component++) {
                        snapshot.columns.Add(new SpreadsheetColumn {
                            title = BuildColumnTitle(attr.name, component),
                            attrName = attr.name,
                            attrType = attr.type,
                            component = component,
                            width = GetSuggestedWidth(attr.type, true),
                        });
                    }
                }
            }
        }

        void AddGroupColumns(SpreadsheetSnapshot snapshot, ulong handle, GeoAttrClass attrClass, int rowCount, string filter) {
            if (!s_groupApiAvailable) {
                return;
            }

            var groups = ReadGroupsSafe(handle, (uint)attrClass, rowCount);
            if (groups.Count <= 0) {
                return;
            }

            groups.Sort(delegate(GroupBuffer a, GroupBuffer b) {
                return string.Compare(a.name, b.name, StringComparison.Ordinal);
            });

            for (int i = 0; i < groups.Count; i++) {
                var group = groups[i];
                if (!MatchesFilter(group.name, filter)) {
                    continue;
                }

                snapshot.groups[group.name] = group;
                snapshot.columns.Add(new SpreadsheetColumn {
                    title = TrimAttributeMarker(group.name),
                    attrName = group.name,
                    attrType = GeoAttrType.BoolU8,
                    isGroup = true,
                    width = 84f,
                });
            }
        }

        static SpreadsheetColumn CreateBuiltinColumn(string title, BuiltinColumnKind kind, float width) {
            return new SpreadsheetColumn {
                title = title,
                builtinKind = kind,
                width = width,
            };
        }

        static int ComparePointAttributes(AttributeInfo left, AttributeInfo right) {
            int rankCompare = GetKnownAttributeRank(left.name).CompareTo(GetKnownAttributeRank(right.name));
            if (rankCompare != 0) {
                return rankCompare;
            }
            return string.Compare(left.name, right.name, StringComparison.Ordinal);
        }

        static int CompareAlphabeticalAttributes(AttributeInfo left, AttributeInfo right) {
            int rankCompare = GetKnownAttributeRank(left.name).CompareTo(GetKnownAttributeRank(right.name));
            if (rankCompare != 0) {
                return rankCompare;
            }
            return string.Compare(left.name, right.name, StringComparison.Ordinal);
        }

        static int GetKnownAttributeRank(string name) {
            switch (name) {
                case "@P": return 0;
                case "@N": return 1;
                case "@uv":
                case "@uv0": return 2;
                case "@uv2": return 3;
                case "@Cd": return 4;
                default: return 32;
            }
        }

        static string BuildColumnTitle(string attrName, int component) {
            string name = TrimAttributeMarker(attrName);
            if (component < 0) {
                return name;
            }

            switch (component) {
                case 0: return name + "[x]";
                case 1: return name + "[y]";
                case 2: return name + "[z]";
                case 3: return name + "[w]";
                default: return name;
            }
        }

        static string TrimAttributeMarker(string name) {
            return string.IsNullOrEmpty(name) ? string.Empty : name.Replace("@", string.Empty);
        }

        static bool MatchesFilter(string candidate, string filter) {
            if (string.IsNullOrWhiteSpace(filter)) {
                return true;
            }

            if (string.IsNullOrEmpty(candidate)) {
                return false;
            }

            string normalizedCandidate = TrimAttributeMarker(candidate);
            return normalizedCandidate.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || candidate.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static int GetDisplayComponentCount(GeoAttrType type) {
            switch (type) {
                case GeoAttrType.Vec2:
                case GeoAttrType.IVec2:
                case GeoAttrType.DVec2:
                    return 2;
                case GeoAttrType.Vec3:
                case GeoAttrType.DVec3:
                    return 3;
                case GeoAttrType.Vec4:
                case GeoAttrType.DVec4:
                    return 4;
                default:
                    return 1;
            }
        }

        static int GetStride(GeoAttrType type) {
            switch (type) {
                case GeoAttrType.Vec2:
                case GeoAttrType.IVec2:
                case GeoAttrType.DVec2:
                    return 2;
                case GeoAttrType.Vec3:
                case GeoAttrType.DVec3:
                    return 3;
                case GeoAttrType.Vec4:
                case GeoAttrType.DVec4:
                    return 4;
                default:
                    return 1;
            }
        }

        static float GetSuggestedWidth(GeoAttrType type, bool isComponentColumn) {
            if (isComponentColumn) {
                return 86f;
            }

            switch (type) {
                case GeoAttrType.StringUtf8:
                    return 220f;
                case GeoAttrType.BytesU8:
                    return 120f;
                default:
                    return 104f;
            }
        }

        static float CalculateTableWidth(List<SpreadsheetColumn> columns, float availableWidth) {
            float total = 0f;
            for (int i = 0; i < columns.Count; i++) {
                total += columns[i].width;
            }

            if (total < availableWidth && columns.Count > 0) {
                columns[columns.Count - 1].width += availableWidth - total;
                total = availableWidth;
            }

            return Mathf.Max(total, availableWidth);
        }

        List<AttributeInfo> ReadAttributeInfos(ulong handle, GeoAttrClass attrClass) {
            int count = (int)NativeMethods.cunning_geo_get_attr_count(handle, (uint)attrClass);
            var attrs = new List<AttributeInfo>(Mathf.Max(0, count));
            var builder = new StringBuilder(256);
            for (int index = 0; index < count; index++) {
                builder.Clear();
                NativeMethods.cunning_geo_get_attr_name(handle, (uint)attrClass, (uint)index, builder, (uint)builder.Capacity);
                string name = builder.ToString();
                if (string.IsNullOrEmpty(name)) {
                    continue;
                }

                attrs.Add(new AttributeInfo {
                    name = name,
                    type = (GeoAttrType)NativeMethods.cunning_geo_get_attr_type(handle, (uint)attrClass, name),
                    length = (int)NativeMethods.cunning_geo_get_attr_len(handle, (uint)attrClass, name),
                });
            }

            return attrs;
        }

        AttributeBuffer ReadAttributeBuffer(ulong handle, GeoAttrClass attrClass, AttributeInfo info) {
            if (info == null || string.IsNullOrEmpty(info.name)) {
                return null;
            }

            var buffer = new AttributeBuffer {
                name = info.name,
                type = info.type,
                length = Mathf.Max(0, info.length),
            };

            switch (info.type) {
                case GeoAttrType.F32:
                case GeoAttrType.Vec2:
                case GeoAttrType.Vec3:
                case GeoAttrType.Vec4:
                    buffer.f32 = ReadFloatArray(handle, attrClass, info.name, buffer.length * GetStride(info.type));
                    return buffer;
                case GeoAttrType.I32:
                case GeoAttrType.IVec2:
                    buffer.i32 = ReadIntArray(handle, attrClass, info.name, buffer.length * GetStride(info.type));
                    return buffer;
                case GeoAttrType.BoolU8:
                    buffer.u8 = ReadByteArray(handle, attrClass, info.name, buffer.length);
                    return buffer;
                case GeoAttrType.F64:
                case GeoAttrType.DVec2:
                case GeoAttrType.DVec3:
                case GeoAttrType.DVec4:
                    buffer.f64 = ReadDoubleArray(handle, attrClass, info.name, buffer.length * GetStride(info.type));
                    return buffer;
                case GeoAttrType.StringUtf8:
                    buffer.length = (int)NativeMethods.cunning_geo_get_attr_string_count(handle, (uint)attrClass, info.name);
                    buffer.strings = ReadStringArray(handle, attrClass, info.name, buffer.length);
                    return buffer;
                case GeoAttrType.BytesU8:
                    buffer.u8 = ReadBytes(handle, attrClass, info.name, Mathf.Max(buffer.length, 1));
                    return buffer;
                default:
                    return buffer;
            }
        }

        static float[] ReadFloatArray(ulong handle, GeoAttrClass attrClass, string name, int count) {
            if (count <= 0) {
                return Array.Empty<float>();
            }

            var values = new float[count];
            var pinned = GCHandle.Alloc(values, GCHandleType.Pinned);
            try {
                uint copied = NativeMethods.cunning_geo_copy_attr_f32(handle, (uint)attrClass, name, pinned.AddrOfPinnedObject(), (uint)count);
                if (copied <= 0) {
                    return Array.Empty<float>();
                }
                if (copied < count) {
                    Array.Resize(ref values, (int)copied);
                }
                return values;
            } finally {
                pinned.Free();
            }
        }

        static int[] ReadIntArray(ulong handle, GeoAttrClass attrClass, string name, int count) {
            if (count <= 0) {
                return Array.Empty<int>();
            }

            var values = new int[count];
            var pinned = GCHandle.Alloc(values, GCHandleType.Pinned);
            try {
                uint copied = NativeMethods.cunning_geo_copy_attr_i32(handle, (uint)attrClass, name, pinned.AddrOfPinnedObject(), (uint)count);
                if (copied <= 0) {
                    return Array.Empty<int>();
                }
                if (copied < count) {
                    Array.Resize(ref values, (int)copied);
                }
                return values;
            } finally {
                pinned.Free();
            }
        }

        static double[] ReadDoubleArray(ulong handle, GeoAttrClass attrClass, string name, int count) {
            if (count <= 0) {
                return Array.Empty<double>();
            }

            var values = new double[count];
            var pinned = GCHandle.Alloc(values, GCHandleType.Pinned);
            try {
                uint copied = NativeMethods.cunning_geo_copy_attr_f64(handle, (uint)attrClass, name, pinned.AddrOfPinnedObject(), (uint)count);
                if (copied <= 0) {
                    return Array.Empty<double>();
                }
                if (copied < count) {
                    Array.Resize(ref values, (int)copied);
                }
                return values;
            } finally {
                pinned.Free();
            }
        }

        static byte[] ReadByteArray(ulong handle, GeoAttrClass attrClass, string name, int count) {
            if (count <= 0) {
                return Array.Empty<byte>();
            }

            var values = new byte[count];
            var pinned = GCHandle.Alloc(values, GCHandleType.Pinned);
            try {
                uint copied = NativeMethods.cunning_geo_copy_attr_u8(handle, (uint)attrClass, name, pinned.AddrOfPinnedObject(), (uint)count);
                if (copied <= 0) {
                    return Array.Empty<byte>();
                }
                if (copied < count) {
                    Array.Resize(ref values, (int)copied);
                }
                return values;
            } finally {
                pinned.Free();
            }
        }

        static byte[] ReadBytes(ulong handle, GeoAttrClass attrClass, string name, int count) {
            if (count <= 0) {
                return Array.Empty<byte>();
            }

            var values = new byte[count];
            var pinned = GCHandle.Alloc(values, GCHandleType.Pinned);
            try {
                uint copied = NativeMethods.cunning_geo_copy_attr_bytes(handle, (uint)attrClass, name, pinned.AddrOfPinnedObject(), (uint)count);
                if (copied <= 0) {
                    return Array.Empty<byte>();
                }
                if (copied < count) {
                    Array.Resize(ref values, (int)copied);
                }
                return values;
            } finally {
                pinned.Free();
            }
        }

        static string[] ReadStringArray(ulong handle, GeoAttrClass attrClass, string name, int count) {
            if (count <= 0) {
                return Array.Empty<string>();
            }

            var values = new string[count];
            var builder = new StringBuilder(512);
            for (int index = 0; index < count; index++) {
                builder.Clear();
                NativeMethods.cunning_geo_get_attr_string(handle, (uint)attrClass, name, (uint)index, builder, (uint)builder.Capacity);
                values[index] = builder.ToString();
            }

            return values;
        }

        static int[] BuildVertexToPointMap(ulong handle, int primitiveCount, int vertexCount) {
            if (primitiveCount <= 0 || vertexCount <= 0) {
                return Array.Empty<int>();
            }

            int[] vertexOffsets = ReadUIntArray(handle, NativeMethods.cunning_geo_copy_prim_vertex_offsets, primitiveCount + 1);
            int[] pointOffsets = ReadUIntArray(handle, NativeMethods.cunning_geo_copy_prim_point_offsets, primitiveCount + 1);
            if (vertexOffsets.Length != primitiveCount + 1 || pointOffsets.Length != primitiveCount + 1) {
                return Array.Empty<int>();
            }

            int vertexIndexCount = vertexOffsets[primitiveCount];
            int pointIndexCount = pointOffsets[primitiveCount];
            if (vertexIndexCount <= 0 || pointIndexCount <= 0) {
                return Array.Empty<int>();
            }

            int[] vertexIndices = ReadUIntArray(handle, NativeMethods.cunning_geo_copy_prim_vertex_indices, vertexIndexCount);
            int[] pointIndices = ReadUIntArray(handle, NativeMethods.cunning_geo_copy_prim_point_indices, pointIndexCount);
            if (vertexIndices.Length <= 0 || pointIndices.Length <= 0) {
                return Array.Empty<int>();
            }

            var map = new int[vertexCount];
            for (int i = 0; i < map.Length; i++) {
                map[i] = -1;
            }

            for (int primitiveIndex = 0; primitiveIndex < primitiveCount; primitiveIndex++) {
                int vertexStart = vertexOffsets[primitiveIndex];
                int vertexEnd = vertexOffsets[primitiveIndex + 1];
                int pointStart = pointOffsets[primitiveIndex];
                int pointEnd = pointOffsets[primitiveIndex + 1];
                int pairCount = Mathf.Min(vertexEnd - vertexStart, pointEnd - pointStart);
                for (int localIndex = 0; localIndex < pairCount; localIndex++) {
                    int vertexDenseIndex = vertexIndices[vertexStart + localIndex];
                    int pointDenseIndex = pointIndices[pointStart + localIndex];
                    if (vertexDenseIndex >= 0 && vertexDenseIndex < map.Length) {
                        map[vertexDenseIndex] = pointDenseIndex;
                    }
                }
            }

            return map;
        }

        static int[] BuildPrimitiveVertexCounts(ulong handle, int primitiveCount) {
            if (primitiveCount <= 0) {
                return Array.Empty<int>();
            }

            int[] vertexOffsets = ReadUIntArray(handle, NativeMethods.cunning_geo_copy_prim_vertex_offsets, primitiveCount + 1);
            if (vertexOffsets.Length != primitiveCount + 1) {
                return Array.Empty<int>();
            }

            var counts = new int[primitiveCount];
            for (int primitiveIndex = 0; primitiveIndex < primitiveCount; primitiveIndex++) {
                counts[primitiveIndex] = Mathf.Max(0, vertexOffsets[primitiveIndex + 1] - vertexOffsets[primitiveIndex]);
            }

            return counts;
        }

        delegate uint NativeUIntCopy(ulong handle, IntPtr outPtr);

        static int[] ReadUIntArray(ulong handle, NativeUIntCopy copy, int count) {
            if (copy == null || count <= 0) {
                return Array.Empty<int>();
            }

            var values = new int[count];
            var pinned = GCHandle.Alloc(values, GCHandleType.Pinned);
            try {
                uint copied = copy(handle, pinned.AddrOfPinnedObject());
                if (copied <= 0) {
                    return Array.Empty<int>();
                }
                if (copied < count) {
                    Array.Resize(ref values, (int)copied);
                }
                return values;
            } finally {
                pinned.Free();
            }
        }

        static int GetEdgeCountSafe(ulong handle) {
            if (!s_edgeApiAvailable || handle == 0UL) {
                return 0;
            }

            try {
                return (int)NativeMethods.cunning_geo_get_edge_count(handle);
            } catch (EntryPointNotFoundException) {
                s_edgeApiAvailable = false;
                return 0;
            }
        }

        static int[] ReadEdgePointPairsSafe(ulong handle, int edgeCount) {
            if (!s_edgeApiAvailable || handle == 0UL || edgeCount <= 0) {
                return Array.Empty<int>();
            }

            int valueCount = edgeCount * 2;
            var values = new int[valueCount];
            var pinned = GCHandle.Alloc(values, GCHandleType.Pinned);
            try {
                uint copied = NativeMethods.cunning_geo_copy_edge_point_indices(handle, pinned.AddrOfPinnedObject());
                if (copied <= 0) {
                    return Array.Empty<int>();
                }
                if (copied < valueCount) {
                    Array.Resize(ref values, (int)copied);
                }
                return values;
            } catch (EntryPointNotFoundException) {
                s_edgeApiAvailable = false;
                return Array.Empty<int>();
            } finally {
                pinned.Free();
            }
        }

        static List<GroupBuffer> ReadGroupsSafe(ulong handle, uint attrClass, int rowCount) {
            var output = new List<GroupBuffer>();
            if (!s_groupApiAvailable || handle == 0UL) {
                return output;
            }

            try {
                int count = (int)NativeMethods.cunning_geo_get_group_count(handle, attrClass);
                var builder = new StringBuilder(256);
                for (int index = 0; index < count; index++) {
                    builder.Clear();
                    NativeMethods.cunning_geo_get_group_name(handle, attrClass, (uint)index, builder, (uint)builder.Capacity);
                    string name = builder.ToString();
                    if (string.IsNullOrEmpty(name)) {
                        continue;
                    }

                    int length = Mathf.Max(0, (int)NativeMethods.cunning_geo_get_group_len(handle, attrClass, name));
                    if (length <= 0) {
                        length = rowCount;
                    }

                    int blockCount = (length + 63) >> 6;
                    var bits = new ulong[Mathf.Max(0, blockCount)];
                    if (bits.Length > 0) {
                        var pinned = GCHandle.Alloc(bits, GCHandleType.Pinned);
                        try {
                            NativeMethods.cunning_geo_copy_group_mask_u64(handle, attrClass, name, pinned.AddrOfPinnedObject(), (uint)bits.Length);
                        } finally {
                            pinned.Free();
                        }
                    }

                    output.Add(new GroupBuffer {
                        name = name,
                        length = length,
                        bits = bits,
                    });
                }
            } catch (EntryPointNotFoundException) {
                s_groupApiAvailable = false;
            }

            return output;
        }
    }
}
