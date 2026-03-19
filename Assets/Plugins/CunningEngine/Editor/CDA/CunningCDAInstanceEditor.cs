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

namespace CunningEngine.Editor.CDA {
    [CustomEditor(typeof(CunningCDAInstance))]
    public sealed class CunningCDAInstanceEditor : UnityEditor.Editor {
        enum GeoAttrClass : uint { Detail = 0, Point = 1, Vertex = 2, Primitive = 3, Edge = 4 }
        enum GeoAttrType : uint {
            Unknown = 0, F32 = 1, Vec2 = 2, Vec3 = 3, Vec4 = 4, I32 = 5, IVec2 = 6,
            BoolU8 = 7, F64 = 8, DVec2 = 9, DVec3 = 10, DVec4 = 11, StringUtf8 = 12, BytesU8 = 13,
        }

        sealed class GeoNodeInfo {
            public string id;
            public string name;
            public string typeId;
        }

        sealed class GeoAttrInfo {
            public string name;
            public GeoAttrType type;
            public uint len;
        }

        sealed class PromotedUiChoice {
            public string label;
            public int value;
        }

        sealed class PromotedUiMeta {
            public string kind;
            public float? min;
            public float? max;
            public bool? showAlpha;
            public readonly List<PromotedUiChoice> choices = new();
        }

        bool _foldInputs = true, _foldParams = true, _foldOutputs = true, _foldDebug = true, _foldOptions = false, _foldGeometry = true;
        Dictionary<string, bool> _groupFolds = new();
        readonly Dictionary<int, string> _inputErrors = new();
        readonly Dictionary<string, PromotedUiMeta> _promotedUiByName = new();
        uint _promotedUiAssetSig;
        bool _promotedUiLoaded;
        bool _promotedUiApiAvailable = true;
        bool _promotedUiApiWarned;
        ulong _geoJobId;
        uint _geoJobLastStatus;
        string _geoJobNodeId;
        ulong _geoJobTargetGen;
        ulong _geoLastJobId;
        NativeMethods.JobStats _geoLastJobStats;
        string _lastError;
        string _debugMessage;
        bool _debugMessageIsError;
        double _cookStartTime;
        const int BLOCK_COUNT = 20;
        const float COOK_ANIM_DURATION = 2.5f; // seconds for full bar animation

        readonly List<GeoNodeInfo> _geoNodes = new();
        readonly List<GeoAttrInfo> _geoAttrs = new();
        readonly List<string> _geoPreview = new();
        Vector2 _geoNodeScroll;
        Vector2 _geoAttrScroll;
        Vector2 _geoValueScroll;
        int _geoSelectedNode = -1;
        int _geoSelectedAttr = -1;
        string _geoNodeSearch = "";
        GeoAttrClass _geoSelectedClass = GeoAttrClass.Point;
        int _geoPreviewRows = 24;
        ulong _geoNodesCdaId;
        ulong _geoNodesRevision;
        ulong _geoActiveHandle;
        string _geoLastCookedNodeLabel;
        string _geoLastSyncedNodeId;
        ulong _geoLastSyncedGen;
        bool _geoAutoRefreshPending;
        string _geoStatus;
        MessageType _geoStatusType = MessageType.Info;

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

        void OnEnable() {
            SceneView.duringSceneGui += OnSceneGui;
        }

        void OnDisable() {
            SceneView.duringSceneGui -= OnSceneGui;
            ClearGeometryState();
        }

        public override void OnInspectorGUI() {
            var t = (CunningCDAInstance)target;
            serializedObject.Update();

            // === HEADER ===
            string name = t.asset?.name ?? "No CDA Asset";
            string path = t.asset?.sourcePath ?? "";
            string uuid = "";
            if (t.asset != null && !string.IsNullOrEmpty(t.asset.sourceJson)) {
                var m = System.Text.RegularExpressions.Regex.Match(t.asset.sourceJson, "\"uuid\"\\s*:\\s*\"([^\"]+)\"");
                if (m.Success) uuid = m.Groups[1].Value;
            }
            CdaEditorStyles.DrawHeader(name, path, uuid);
            EditorGUILayout.Space(4);

            if (t.asset == null) {
                EditorGUILayout.HelpBox("Drag a .cda file onto the Hierarchy or Scene to create an instance.", MessageType.Info);
                return;
            }

            // === COOK BAR (right after header) ===
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f);
            if (GUILayout.Button("Cook", GUILayout.Height(36))) {
                _lastError = null;
                _cookStartTime = EditorApplication.timeSinceStartup;
                t.Cook();
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = t.jobId != 0;
            if (GUILayout.Button("Cancel", GUILayout.Height(36))) {
                NativeMethods.cunning_job_cancel(t.jobId);
                t.jobId = 0;
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            // === BAKE BAR ===
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Bake to Prefab", GUILayout.Height(36))) Bake(t, false);
            if (GUILayout.Button("Bake & Replace", GUILayout.Height(36))) Bake(t, true);
            EditorGUILayout.EndHorizontal();

            string status = t.jobId != 0 ? "Cooking..." : (_lastError != null ? "Error" : "Ready");
            var statusStyle = t.jobId != 0 ? CdaEditorStyles.StatusCooking : (_lastError != null ? CdaEditorStyles.StatusError : CdaEditorStyles.StatusReady);
            EditorGUILayout.LabelField("Status: " + status, statusStyle);
            string hostMode = string.IsNullOrEmpty(t.asset.access_mode) ? "WhiteBox" : t.asset.access_mode;
            uint nativeAccess = t.cdaId != 0 ? NativeMethods.cunning_cda_get_access_mode(t.cdaId) : 0;
            string nativeMode = t.cdaId != 0 ? (nativeAccess == 1 ? "BlackBox" : "WhiteBox") : "-";
            bool effectiveBlackBox = (t.cdaId != 0 && nativeAccess == 1) || t.asset.IsBlackBox();
            string effectiveMode = effectiveBlackBox ? "BlackBox" : "WhiteBox";
            EditorGUILayout.LabelField("Access Mode", hostMode);
            EditorGUILayout.LabelField("Native Mode", nativeMode);
            EditorGUILayout.LabelField("Effective", effectiveMode);

            // === PROGRESS BAR (block style) ===
            DrawBlockProgressBar(t.jobId != 0);

            if (!string.IsNullOrEmpty(_lastError)) EditorGUILayout.HelpBox(_lastError, MessageType.Error);

            EditorGUILayout.Space(6);

            // === INPUTS SECTION ===
            int inputCount = t.asset.inputs?.Count ?? 0;
            if (CdaEditorStyles.DrawSection("INPUTS", inputCount, ref _foldInputs) && inputCount > 0) {
                EditorGUI.indentLevel++;
                while (t.inputs.Count < inputCount) t.inputs.Add(null);
                if (t.inputs.Count > inputCount) t.inputs.RemoveRange(inputCount, t.inputs.Count - inputCount);
                for (int i = 0; i < inputCount; i++) {
                    var p = t.asset.inputs[i];
                    string label = string.IsNullOrEmpty(p.name) ? $"Input {i}" : p.name;
                    EditorGUI.BeginChangeCheck();
                    UnityEngine.Object picked = EditorGUILayout.ObjectField(label, t.inputs[i], typeof(UnityEngine.Object), true);
                    if (EditorGUI.EndChangeCheck()) {
                        var nv = CunningInputUtility.Resolve(picked);
                        if (picked != null && nv == null) {
                            _inputErrors[i] = $"Unsupported input: {picked.GetType().Name}. Supported: GameObject/Component with CunningMesh, MeshFilter, SkinnedMeshRenderer, or SplineContainer, or any MonoBehaviour implementing ICunningInputHandle.";
                            // Bounce: keep previous value (don't mutate slot).
                        } else {
                            _inputErrors.Remove(i);
                            if (nv != t.inputs[i]) {
                                t.inputs[i] = nv;
                                EditorUtility.SetDirty(t);
                                if (t.autoCook) t.Cook();
                            }
                        }
                    }
                    if (_inputErrors.TryGetValue(i, out var err) && !string.IsNullOrEmpty(err)) EditorGUILayout.HelpBox(err, MessageType.Warning);
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            EnsurePromotedUiMetadata(t);

            // === PARAMETERS SECTION ===
            int paramCount = t.asset.promoted_params?.Count ?? 0;
            if (CdaEditorStyles.DrawSection("PARAMETERS", paramCount, ref _foldParams) && paramCount > 0) {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Reset All to Defaults", EditorStyles.miniButton)) {
                    if (t.asset != null && t.asset.promoted_params != null) {
                        Undo.RecordObject(t, "Reset All Parameters");
                        foreach (var p in t.asset.promoted_params) t.paramValues[p.name] = CdaParamValue.FromDefaultJson(p.default_value_json);
                        EditorUtility.SetDirty(t);
                        if (t.autoCook) t.Cook();
                    }
                }
                EditorGUILayout.EndHorizontal();

                EditorGUI.indentLevel++;
                t.paramValues ??= new Dictionary<string, CdaParamValue>();
                var groups = t.asset.promoted_params.GroupBy(p => p.group ?? "").OrderBy(g => g.Key);
                foreach (var g in groups) {
                    bool inGroup = !string.IsNullOrEmpty(g.Key);
                    bool groupOpen = true;
                    if (inGroup) {
                        if (!_groupFolds.ContainsKey(g.Key)) _groupFolds[g.Key] = true;
                        _groupFolds[g.Key] = EditorGUILayout.Foldout(_groupFolds[g.Key], g.Key, true, EditorStyles.foldoutHeader);
                        groupOpen = _groupFolds[g.Key];
                        if (groupOpen) EditorGUI.indentLevel++;
                    }
                    if (groupOpen) foreach (var p in g.OrderBy(x => x.order)) DrawParam(t, p);
                    if (inGroup && groupOpen) EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // === OUTPUTS SECTION ===
            int outputCount = t.asset.outputs?.Count ?? 0;
            if (CdaEditorStyles.DrawSection("OUTPUTS", outputCount, ref _foldOutputs) && outputCount > 0) {
                EditorGUI.indentLevel++;
                for (int i = 0; i < outputCount; i++) {
                    var o = t.asset.outputs[i];
                    string label = string.IsNullOrEmpty(o.name) ? $"Output {i}" : o.name;
                    Transform child = i < t.transform.childCount ? t.transform.GetChild(i) : null;
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(label, GUILayout.Width(120));
                    if (child != null) {
                        var mf = child.GetComponent<MeshFilter>();
                        string info = mf?.sharedMesh != null ? $"{GetTriangleCountSafe(mf.sharedMesh)} tris" : "(empty)";
                        GUILayout.Label(info, EditorStyles.miniLabel);
                        if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(50))) Selection.activeGameObject = child.gameObject;
                    } else {
                        GUILayout.Label("(not generated)", EditorStyles.miniLabel);
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // === CDA TOOLS ENTRY ===
            if (CdaEditorStyles.DrawSection("CDA TOOLS", -1, ref _foldGeometry)) {
                EditorGUILayout.HelpBox("Read-only Node Editor and Geometry Sheet are now in CDA Tools window.", MessageType.Info);
                if (GUILayout.Button("Open CDA Tools", GUILayout.Height(24))) {
                    EditorApplication.ExecuteMenuItem("Procedural/Cunning Engine/Debug/CDA/CDA Tools");
                }
            }
            EditorGUILayout.Space(4);

            // === OPTIONS SECTION ===
            if (CdaEditorStyles.DrawSection("OPTIONS", -1, ref _foldOptions)) {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                t.autoCook = EditorGUILayout.Toggle("Auto Cook on Change", t.autoCook);
                bool prevAutoSyncGeo = t.autoSyncGeometrySheet;
                t.autoSyncGeometrySheet = EditorGUILayout.Toggle("Auto Sync Geometry Sheet", t.autoSyncGeometrySheet);
                if (EditorGUI.EndChangeCheck()) {
                    EditorUtility.SetDirty(t);
                    if (!prevAutoSyncGeo && t.autoSyncGeometrySheet) _geoAutoRefreshPending = true;
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // === DEBUG SECTION ===
            if (CdaEditorStyles.DrawSection("DEBUG", -1, ref _foldDebug)) {
                DrawDebugSection(t);
            }

            serializedObject.ApplyModifiedProperties();
        }

        void DrawGeometrySheetSection(CunningCDAInstance t) {
            EditorGUI.indentLevel++;

            if (!string.IsNullOrEmpty(_geoStatus)) {
                EditorGUILayout.HelpBox(_geoStatus, _geoStatusType);
            }

            if (t == null || t.asset == null) {
                EditorGUILayout.HelpBox("No CDA instance/asset.", MessageType.Warning);
                EditorGUI.indentLevel--;
                return;
            }

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = _geoActiveHandle != 0;
            if (GUILayout.Button("Clear Preview", GUILayout.Height(22), GUILayout.Width(110))) {
                ClearGeometryActiveHandle();
                _geoLastCookedNodeLabel = null;
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            GUILayout.Label(t.autoSyncGeometrySheet
                ? "Auto-sync is ON. Geometry Sheet reads selected node data directly from current cook cache."
                : "Auto-sync is OFF. Enable it in Options to update geometry sheet automatically.", EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Search", GUILayout.Width(46));
            _geoNodeSearch = EditorGUILayout.TextField(_geoNodeSearch ?? "");
            if (GUILayout.Button("x", EditorStyles.miniButton, GUILayout.Width(24))) _geoNodeSearch = "";
            EditorGUILayout.EndHorizontal();

            var filtered = GetFilteredGeometryNodeIndices();
            _geoNodeScroll = EditorGUILayout.BeginScrollView(_geoNodeScroll, GUILayout.MinHeight(90), GUILayout.MaxHeight(180));
            if (filtered.Count == 0) {
                if (t.jobId != 0) GUILayout.Label("CDA is cooking... nodes will appear automatically.", EditorStyles.miniLabel);
                else if (t.cdaId == 0) GUILayout.Label("Waiting for CDA load...", EditorStyles.miniLabel);
                else GUILayout.Label("No nodes loaded.", EditorStyles.miniLabel);
            }
            for (int i = 0; i < filtered.Count; i++) {
                int idx = filtered[i];
                var n = _geoNodes[idx];
                string dn = string.IsNullOrWhiteSpace(n.name) ? "(Unnamed)" : n.name;
                string label = $"{dn}  ·  {n.typeId}";
                if (GUILayout.Button(label, idx == _geoSelectedNode ? EditorStyles.miniButtonMid : EditorStyles.miniButton)) {
                    if (_geoSelectedNode != idx) {
                        _geoSelectedNode = idx;
                        ClearGeometryActiveHandle();
                        _geoAutoRefreshPending = true;
                        SetGeometryStatus("Node changed. Waiting for auto-sync.", MessageType.Info);
                    }
                }
            }
            EditorGUILayout.EndScrollView();

            if (_geoSelectedNode >= 0 && _geoSelectedNode < _geoNodes.Count) {
                var n = _geoNodes[_geoSelectedNode];
                EditorGUILayout.LabelField("Selected", string.IsNullOrWhiteSpace(n.name) ? n.id : n.name);
                EditorGUILayout.LabelField("Type", n.typeId ?? "");
                EditorGUILayout.LabelField("Node ID", n.id ?? "");
            }
            if (!string.IsNullOrWhiteSpace(_geoLastCookedNodeLabel)) {
                EditorGUILayout.LabelField("Preview Source", _geoLastCookedNodeLabel, EditorStyles.miniLabel);
            }

            if (_geoActiveHandle == 0) {
                EditorGUILayout.HelpBox("No cached geometry loaded for selected node/gen yet.", MessageType.Info);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.BeginVertical(GUILayout.Width(360));
                GUILayout.Label("Attributes (0)", EditorStyles.boldLabel);
                _geoAttrScroll = EditorGUILayout.BeginScrollView(_geoAttrScroll, GUILayout.MinHeight(110), GUILayout.MaxHeight(220));
                GUILayout.Label("No geometry data yet.", EditorStyles.miniLabel);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();

                EditorGUILayout.BeginVertical();
                GUILayout.Label("Values", EditorStyles.boldLabel);
                _geoValueScroll = EditorGUILayout.BeginScrollView(_geoValueScroll, GUILayout.MinHeight(110), GUILayout.MaxHeight(220));
                GUILayout.Label("No rows.", EditorStyles.miniLabel);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
                DrawGeometryScenePreviewOptions();
                EditorGUI.indentLevel--;
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
            if (GUILayout.Button("Refresh Attrs", GUILayout.Width(110), GUILayout.Height(22))) {
                RefreshGeometryAttrs();
            }
            EditorGUILayout.EndHorizontal();

            _geoPreviewRows = EditorGUILayout.IntSlider("Preview Rows", _geoPreviewRows, 4, 256);

            uint p = NativeMethods.cunning_geo_get_point_count(_geoActiveHandle);
            uint v = NativeMethods.cunning_geo_get_vertex_count(_geoActiveHandle);
            uint pr = NativeMethods.cunning_geo_get_prim_count(_geoActiveHandle);
            EditorGUILayout.LabelField("Handle", _geoActiveHandle.ToString(CultureInfo.InvariantCulture));
            EditorGUILayout.LabelField($"Points {p}   Vertices {v}   Primitives {pr}", EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical(GUILayout.Width(360));
            GUILayout.Label($"Attributes ({_geoAttrs.Count})", EditorStyles.boldLabel);
            _geoAttrScroll = EditorGUILayout.BeginScrollView(_geoAttrScroll, GUILayout.MinHeight(110), GUILayout.MaxHeight(220));
            if (_geoAttrs.Count == 0) GUILayout.Label("No attributes loaded.", EditorStyles.miniLabel);
            for (int i = 0; i < _geoAttrs.Count; i++) {
                var a = _geoAttrs[i];
                string label = $"{a.name}  ·  {a.type}  ·  len={a.len}";
                bool isSelected = i == _geoSelectedAttr;
                var prevBg = GUI.backgroundColor;
                if (isSelected) GUI.backgroundColor = new Color(1f, 0.62f, 0.24f, 1f);
                if (GUILayout.Button(label, isSelected ? EditorStyles.miniButtonMid : EditorStyles.miniButton)) {
                    _geoSelectedAttr = i;
                    BuildGeometryPreview();
                    ArmScenePreviewForSelectedAttr();
                    InvalidateGeometrySceneCache();
                    SceneView.RepaintAll();
                }
                GUI.backgroundColor = prevBg;
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical();
            if (_geoSelectedAttr < 0 || _geoSelectedAttr >= _geoAttrs.Count) {
                GUILayout.Label("Select an attribute.", EditorStyles.miniLabel);
            } else {
                var a = _geoAttrs[_geoSelectedAttr];
                GUILayout.Label(a.name, EditorStyles.boldLabel);
                GUILayout.Label($"Type: {a.type} · Length: {a.len}", EditorStyles.miniLabel);
                _geoValueScroll = EditorGUILayout.BeginScrollView(_geoValueScroll, GUILayout.MinHeight(110), GUILayout.MaxHeight(220));
                for (int i = 0; i < _geoPreview.Count; i++) {
                    EditorGUILayout.SelectableLabel(_geoPreview[i], EditorStyles.textField, GUILayout.Height(18));
                }
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            DrawGeometryScenePreviewOptions();
            EditorGUI.indentLevel--;
        }

        void DrawGeometryScenePreviewOptions() {
            _geoScenePreviewEnabled = EditorGUILayout.Toggle("Scene Point Labels", _geoScenePreviewEnabled);
            _geoScenePreviewMax = EditorGUILayout.IntSlider("Scene Max Labels", _geoScenePreviewMax, 16, 320);
            _geoScenePreviewColor = EditorGUILayout.ColorField("Scene Label Color", _geoScenePreviewColor);
            GUILayout.Label("Labels appear only after you click an attribute row.", EditorStyles.miniLabel);
        }

        void EnsureGeometryNodesAuto(CunningCDAInstance t) {
            if (t == null || t.asset == null) return;
            if (!EnsureCdaLoadedForGeometry(t, true)) return;
            if (NativeMethods.cunning_cda_get_access_mode(t.cdaId) == 1 || t.asset.IsBlackBox()) {
                if (_geoNodes.Count > 0) {
                    _geoNodes.Clear();
                    _geoSelectedNode = -1;
                    _geoNodesCdaId = t.cdaId;
                    _geoNodesRevision = NativeMethods.cunning_cda_get_revision(t.cdaId);
                    ClearGeometryActiveHandle();
                }
                return;
            }

            ulong rev = NativeMethods.cunning_cda_get_revision(t.cdaId);
            bool stale = _geoNodes.Count == 0 || _geoNodesCdaId != t.cdaId || _geoNodesRevision != rev;
            if (!stale) return;

            RefreshGeometryNodes(t);
        }

        void RefreshGeometryNodes(CunningCDAInstance t) {
            if (!EnsureCdaLoadedForGeometry(t, true)) return;
            if (NativeMethods.cunning_cda_get_access_mode(t.cdaId) == 1 || t.asset.IsBlackBox()) {
                _geoNodes.Clear();
                _geoSelectedNode = -1;
                _geoNodesCdaId = t.cdaId;
                _geoNodesRevision = NativeMethods.cunning_cda_get_revision(t.cdaId);
                ClearGeometryActiveHandle();
                SetGeometryStatus("CDA is BlackBox. Node geometry/attrs are unavailable.", MessageType.Info);
                return;
            }

            string prevSelectedId = (_geoSelectedNode >= 0 && _geoSelectedNode < _geoNodes.Count)
                ? _geoNodes[_geoSelectedNode].id
                : null;

            _geoNodes.Clear();
            uint count = NativeMethods.cunning_cda_get_node_count(t.cdaId);
            for (uint i = 0; i < count; i++) {
                string id = ReadCdaNativeString((sb, cap) => NativeMethods.cunning_cda_get_node_id(t.cdaId, i, sb, cap), 128);
                if (string.IsNullOrWhiteSpace(id)) continue;
                string name = ReadCdaNativeString((sb, cap) => NativeMethods.cunning_cda_get_node_name(t.cdaId, i, sb, cap), 512);
                string typeId = ReadCdaNativeString((sb, cap) => NativeMethods.cunning_cda_get_node_type_id(t.cdaId, i, sb, cap), 512);
                _geoNodes.Add(new GeoNodeInfo { id = id, name = name, typeId = typeId });
            }

            _geoNodesCdaId = t.cdaId;
            _geoNodesRevision = NativeMethods.cunning_cda_get_revision(t.cdaId);

            int nextSelected = -1;
            if (!string.IsNullOrWhiteSpace(prevSelectedId)) {
                nextSelected = _geoNodes.FindIndex(x => string.Equals(x.id, prevSelectedId, StringComparison.OrdinalIgnoreCase));
            }
            if (nextSelected < 0 && _geoNodes.Count > 0) nextSelected = 0;

            bool selectionChanged = nextSelected != _geoSelectedNode;
            _geoSelectedNode = nextSelected;
            if (selectionChanged) {
                ClearGeometryActiveHandle();
                _geoLastCookedNodeLabel = null;
            }

            _geoAutoRefreshPending = _geoNodes.Count > 0;
            if (_geoNodes.Count == 0) {
                _geoLastSyncedNodeId = null;
                _geoLastSyncedGen = 0;
                ClearGeometryActiveHandle();
            }
            SetGeometryStatus($"Loaded {_geoNodes.Count} nodes.", MessageType.Info);
        }

        bool EnsureCdaLoadedForGeometry(CunningCDAInstance t, bool quiet) {
            if (t == null || t.asset == null) {
                if (!quiet) SetGeometryStatus("No CDA instance/asset.", MessageType.Warning);
                return false;
            }
            if (t.cdaId != 0) return true;
            if (!quiet) {
                SetGeometryStatus(
                    t.jobId != 0
                        ? "CDA is cooking. Geometry nodes will be loaded automatically."
                        : "CDA is not loaded yet.",
                    MessageType.Info);
            }
            return false;
        }

        void ProcessGeometryAutoSync(CunningCDAInstance t) {
            if (t == null || t.asset == null) return;
            EnsureGeometryNodesAuto(t);
            if (!t.autoSyncGeometrySheet) return;
            if (_geoSelectedNode < 0 || _geoSelectedNode >= _geoNodes.Count) return;

            string nodeId = _geoNodes[_geoSelectedNode]?.id ?? "";
            bool nodeChanged = !string.Equals(_geoLastSyncedNodeId ?? "", nodeId, StringComparison.Ordinal);
            bool genChanged = _geoLastSyncedGen != t.gen;
            if (_geoJobId != 0 || _geoAutoRefreshPending || nodeChanged || genChanged) {
                TrySyncSelectedNodeFromJob(t, true);
            }
        }

        bool TrySyncSelectedNodeFromJob(CunningCDAInstance t, bool quietOnMiss) {
            if (t == null || t.asset == null) {
                if (!quietOnMiss) SetGeometryStatus("No CDA instance/asset.", MessageType.Warning);
                return false;
            }
            if (_geoSelectedNode < 0 || _geoSelectedNode >= _geoNodes.Count) {
                if (!quietOnMiss) SetGeometryStatus("Select a node first.", MessageType.Warning);
                return false;
            }
            if (!EnsureCdaLoadedForGeometry(t, quietOnMiss)) return false;

            var n = _geoNodes[_geoSelectedNode];
            if (string.IsNullOrWhiteSpace(n.id)) {
                if (!quietOnMiss) SetGeometryStatus("Selected node has empty node id.", MessageType.Warning);
                return false;
            }

            // If a node geometry job is already in flight, poll it (even if the main cook job is running).
            if (_geoJobId != 0) {
                if (!string.Equals(_geoJobNodeId ?? "", n.id ?? "", StringComparison.Ordinal) || _geoJobTargetGen != t.gen) {
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
                _geoLastJobId = _geoJobId;
                if (hasJobStats) _geoLastJobStats = jobStats;

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
                    if (!quietOnMiss) SetGeometryStatus($"Failed to read node geometry output: {e.Message}", MessageType.Warning);
                } finally {
                    _geoJobId = 0;
                }

                if (handle == 0) {
                    _geoAutoRefreshPending = true;
                    return false;
                }

                ClearGeometryActiveHandle();
                _geoActiveHandle = handle;
                _geoLastCookedNodeLabel = string.IsNullOrWhiteSpace(n.name) ? n.id : n.name;
                _geoLastSyncedNodeId = n.id ?? "";
                _geoLastSyncedGen = t.gen;
                _geoAutoRefreshPending = false;
                RefreshGeometryAttrs();
                if (hasJobStats) {
                    ulong queueMs = (jobStats.started_ms > jobStats.submitted_ms) ? (jobStats.started_ms - jobStats.submitted_ms) : 0;
                    ulong totalMs = (jobStats.finished_ms > jobStats.submitted_ms) ? (jobStats.finished_ms - jobStats.submitted_ms) : 0;
                    SetGeometryStatus($"Loaded node data: {_geoLastCookedNodeLabel} (gen {t.gen}). compute {jobStats.compute_ms}ms, queue {queueMs}ms, total {totalMs}ms.", MessageType.Info);
                } else {
                    SetGeometryStatus($"Loaded node data: {_geoLastCookedNodeLabel} (gen {t.gen}).", MessageType.Info);
                }
                SceneView.RepaintAll();
                Repaint();
                return true;
            }

            // Don't submit a new debug job while the main cook job is running.
            if (t.jobId != 0) {
                _geoAutoRefreshPending = true;
                if (!quietOnMiss) SetGeometryStatus("CDA is cooking. Node geometry will refresh after cook.", MessageType.Info);
                return false;
            }

            bool nativeIsBlackBox = (t.cdaId != 0 && NativeMethods.cunning_cda_get_access_mode(t.cdaId) == 1);
            bool hostWantsBlackBox = t.asset.IsBlackBox();
            if (nativeIsBlackBox || hostWantsBlackBox) {
                if (!quietOnMiss) SetGeometryStatus("CDA is BlackBox. Node geometry/attrs are unavailable.", MessageType.Info);
                return false;
            }

            if (!t.TrySubmitNodeGeometryJob(null, out var submittedJobId, out var error, nodeId: n.id)) {
                if (!quietOnMiss) SetGeometryStatus(string.IsNullOrWhiteSpace(error) ? "Failed to submit node geometry job." : error, MessageType.Warning);
                _geoAutoRefreshPending = true;
                return false;
            }

            _geoJobId = submittedJobId;
            _geoJobLastStatus = 0;
            _geoJobNodeId = n.id ?? "";
            _geoJobTargetGen = t.gen;
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
                _geoScenePreviewArmed = false;
                _geoScenePreviewAttrName = null;
            }
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

        void OnSceneGui(SceneView sv) {
            if (!_geoScenePreviewEnabled || _geoActiveHandle == 0) return;
            if (_geoSelectedClass != GeoAttrClass.Point) return;
            if (_geoSelectedAttr < 0 || _geoSelectedAttr >= _geoAttrs.Count) return;
            if (!_geoScenePreviewArmed) return;
            if (_geoScenePreviewAttrClass != _geoSelectedClass) return;
            var selectedAttr = _geoAttrs[_geoSelectedAttr];
            if (!string.Equals(_geoScenePreviewAttrName ?? "", selectedAttr.name ?? "", StringComparison.Ordinal)) return;
            if (Event.current.type != EventType.Repaint) return;

            var t = target as CunningCDAInstance;
            if (t == null) return;
            var cam = sv != null ? sv.camera : null;
            if (cam == null) return;

            RebuildGeometrySceneCacheIfNeeded(t);
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

        List<int> GetFilteredGeometryNodeIndices() {
            var result = new List<int>();
            string s = (_geoNodeSearch ?? "").Trim();
            bool all = string.IsNullOrWhiteSpace(s);
            for (int i = 0; i < _geoNodes.Count; i++) {
                var n = _geoNodes[i];
                if (all ||
                    (!string.IsNullOrEmpty(n.name) && n.name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(n.id) && n.id.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(n.typeId) && n.typeId.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)) {
                    result.Add(i);
                }
            }
            return result;
        }

        void InvalidateGeometrySceneCache() {
            _geoSceneCacheHandle = 0;
            _geoSceneCacheDirty = 0;
            _geoSceneCacheAttr = null;
            _geoSceneCacheMax = 0;
            _geoScenePoints = Array.Empty<Vector3>();
            _geoSceneTexts = Array.Empty<string>();
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

        static bool IsPointInsideFrustum(Plane[] planes, Vector3 p) {
            if (planes == null || planes.Length == 0) return true;
            for (int i = 0; i < planes.Length; i++) {
                if (planes[i].GetDistanceToPoint(p) < 0f) return false;
            }
            return true;
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
        }

        void ClearGeometryState() {
            _geoNodes.Clear();
            _geoSelectedNode = -1;
            _geoNodesCdaId = 0;
            _geoNodesRevision = 0;
            _geoLastCookedNodeLabel = null;
            _geoLastSyncedNodeId = null;
            _geoLastSyncedGen = 0;
            _geoAutoRefreshPending = false;
            ClearGeometryActiveHandle();
        }

        void SetGeometryStatus(string msg, MessageType type) {
            _geoStatus = msg;
            _geoStatusType = type;
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

        static string ReadCdaNativeString(Func<StringBuilder, uint, uint> reader, int capacity) {
            var sb = new StringBuilder(Math.Max(64, capacity));
            uint ok = reader(sb, (uint)sb.Capacity);
            return ok == 0 ? "" : sb.ToString();
        }

        static string FormatJobStatus(uint status) {
            return status switch {
                0 => "Pending",
                1 => "Running",
                2 => "Ready",
                3 => "Cancelled",
                4 => "Failed",
                _ => status.ToString(CultureInfo.InvariantCulture),
            };
        }

        static string FormatJobStatsSummary(ulong jobId, NativeMethods.JobStats st) {
            if (jobId == 0 && st.submitted_ms == 0 && st.compute_ms == 0 && st.finished_ms == 0) return "-";
            ulong queueMs = (st.started_ms > st.submitted_ms) ? (st.started_ms - st.submitted_ms) : 0;
            ulong totalMs = (st.finished_ms > st.submitted_ms) ? (st.finished_ms - st.submitted_ms) : 0;
            string id = jobId != 0 ? jobId.ToString(CultureInfo.InvariantCulture) : "?";
            return $"{FormatJobStatus(st.status)}  ·  compute {st.compute_ms}ms  ·  queue {queueMs}ms  ·  total {totalMs}ms  ·  id {id}";
        }

        void DrawDebugSection(CunningCDAInstance t) {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Instance ID", t.instanceId.ToString());
            EditorGUILayout.LabelField("CDA ID", t.cdaId.ToString());
            EditorGUILayout.LabelField("Generation", t.gen.ToString());
            EditorGUILayout.LabelField("Job ID", t.jobId.ToString());
            EditorGUILayout.LabelField("Last Main Job", FormatJobStatsSummary(t.lastMainJobId, t.lastMainJobStats));
            EditorGUILayout.LabelField("Last Select Job", FormatJobStatsSummary(t.lastSelectJobId, t.lastSelectJobStats));
            EditorGUILayout.LabelField("Data Targets", (t.selectedTargets != null ? t.selectedTargets.Count : 0).ToString());
            EditorGUILayout.LabelField("Target Outputs", (t.selectedTargetOutputsJson != null ? t.selectedTargetOutputsJson.Count : 0).ToString());

            if (!string.IsNullOrEmpty(_debugMessage)) {
                EditorGUILayout.HelpBox(_debugMessage, _debugMessageIsError ? MessageType.Warning : MessageType.Info);
            }
            EditorGUI.indentLevel--;
        }

        bool EnsureCdaLoadedForDebug(CunningCDAInstance t) {
            if (t == null || t.asset == null) {
                _debugMessage = "No CDA instance/asset.";
                _debugMessageIsError = true;
                return false;
            }
            if (t.cdaId != 0) return true;
            t.Cook(); // ensures cdaId load path runs
            if (t.cdaId != 0) return true;
            _debugMessage = GetLastNativeError("failed to load CDA");
            _debugMessageIsError = true;
            return false;
        }

        static string GetLastNativeError(string fallback) {
            var sb = new StringBuilder(2048);
            NativeMethods.cunning_cda_get_last_error(sb, (uint)sb.Capacity);
            string msg = sb.ToString();
            return string.IsNullOrWhiteSpace(msg) ? fallback : msg;
        }

        void EnsurePromotedUiMetadata(CunningCDAInstance t) {
            if (t == null || t.asset == null) return;
            uint jsonHash = t.asset.sourceJsonHash;
            if (jsonHash == 0 && !string.IsNullOrEmpty(t.asset.sourceJson)) {
                jsonHash = Hash32(t.asset.sourceJson);
                t.asset.sourceJsonHash = jsonHash;
                EditorUtility.SetDirty(t.asset);
            }
            uint sig = Hash32(t.asset.sourcePath) ^ jsonHash;
            if (sig != _promotedUiAssetSig) {
                _promotedUiAssetSig = sig;
                _promotedUiByName.Clear();
                _promotedUiLoaded = false;
            }
            if (_promotedUiLoaded) return;
            if (t.cdaId == 0 || t.asset.promoted_params == null || t.asset.promoted_params.Count == 0) return;

            foreach (var p in t.asset.promoted_params) {
                if (p == null || string.IsNullOrEmpty(p.name) || p.bindings == null || p.bindings.Count == 0) continue;
                var b = p.bindings[0];
                if (b == null || string.IsNullOrEmpty(b.node) || string.IsNullOrEmpty(b.param)) continue;
                var uiJson = ReadNodeParamUiJson(t.cdaId, b.node, b.param);
                var ui = ParseUiMeta(uiJson);
                if (ui != null) _promotedUiByName[p.name] = ui;
            }
            _promotedUiLoaded = true;
        }

        string ReadNodeParamUiJson(ulong cdaId, string nodeId, string paramName) {
            if (!_promotedUiApiAvailable) return "";
            try {
                var sb = new StringBuilder(8192);
                uint ok = NativeMethods.cunning_cda_get_node_param_ui_json(cdaId, nodeId, paramName, sb, (uint)sb.Capacity);
                return ok == 0 ? "" : sb.ToString();
            } catch (EntryPointNotFoundException) {
                _promotedUiApiAvailable = false;
                if (!_promotedUiApiWarned) {
                    _promotedUiApiWarned = true;
                    _debugMessage = "Native param UI metadata API unavailable. Inspector uses fallback controls.";
                    _debugMessageIsError = false;
                }
                return "";
            }
        }

        static PromotedUiMeta ParseUiMeta(string json) {
            if (string.IsNullOrWhiteSpace(json)) return null;
            if (!CdaMiniJson.TryGetTopLevelString(json, "kind", out var kind)) return null;
            var m = new PromotedUiMeta { kind = kind };

            if (CdaMiniJson.TryGetTopLevelRaw(json, "min", out var minRaw) && float.TryParse(minRaw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var minv)) m.min = minv;
            if (CdaMiniJson.TryGetTopLevelRaw(json, "max", out var maxRaw) && float.TryParse(maxRaw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var maxv)) m.max = maxv;
            if (CdaMiniJson.TryGetTopLevelRaw(json, "show_alpha", out var aRaw)) {
                var t = aRaw.Trim();
                if (t.Equals("true", StringComparison.OrdinalIgnoreCase)) m.showAlpha = true;
                else if (t.Equals("false", StringComparison.OrdinalIgnoreCase)) m.showAlpha = false;
            }

            if (CdaMiniJson.TryGetTopLevelRaw(json, "choices", out var csRaw)) {
                var arr = CdaMiniJson.SplitArrayElements(csRaw);
                for (int i = 0; i < arr.Count; i++) {
                    var item = arr[i];
                    if (!CdaMiniJson.TryGetObjectInt(item, "value", out var iv)) continue;
                    if (!CdaMiniJson.TryGetObjectString(item, "label", out var lbl)) lbl = iv.ToString(CultureInfo.InvariantCulture);
                    m.choices.Add(new PromotedUiChoice { label = lbl, value = iv });
                }
            }
            return m;
        }

        static uint Hash32(string s) {
            if (string.IsNullOrEmpty(s)) return 0;
            unchecked {
                uint h = 2166136261u;
                for (int i = 0; i < s.Length; i++) {
                    h ^= s[i];
                    h *= 16777619u;
                }
                return h;
            }
        }

        static string NormalizeControlKind(string kind) {
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
                "vec2drag" => "Vec2",
                "vec3" => "Vec3",
                "vec3drag" => "Vec3",
                "vec4" => "Vec4",
                "vec4drag" => "Vec4",
                "color" => "Color",
                "color4" => "Color4",
                "dropdown" => "Dropdown",
                _ => kind,
            };
        }

        void DrawParam(CunningCDAInstance t, CdaPromotedParam p) {
            if (!t.paramValues.TryGetValue(p.name, out var v)) {
                v = CdaParamValue.FromDefaultJson(p.default_value_json);
                t.paramValues[p.name] = v;
            }

            EditorGUI.BeginChangeCheck();
            _promotedUiByName.TryGetValue(p.name, out var uiMeta);
            string pt = NormalizeControlKind(uiMeta?.kind ?? p.param_type ?? v.kind ?? "Float");
            string labelStr = p.label ?? p.name;
            GUIContent label = new GUIContent(labelStr, p.tooltip ?? "");
            bool hasRange = p.min.HasValue && p.max.HasValue;
            if (uiMeta != null && uiMeta.min.HasValue && uiMeta.max.HasValue) hasRange = true;
            float minRange = uiMeta?.min ?? p.min ?? 0f;
            float maxRange = uiMeta?.max ?? p.max ?? 1f;

            switch (pt) {
                case "Int":
                case "IntSlider":
                case "Dropdown":
                    v.kind = "Int";
                    if (pt == "Dropdown" && uiMeta != null && uiMeta.choices.Count > 0) {
                        int idx = 0;
                        for (int i = 0; i < uiMeta.choices.Count; i++) {
                            if (uiMeta.choices[i].value == v.i) { idx = i; break; }
                        }
                        string[] opts = uiMeta.choices.Select(x => string.IsNullOrWhiteSpace(x.label) ? x.value.ToString(CultureInfo.InvariantCulture) : x.label).ToArray();
                        int next = EditorGUILayout.Popup(label, idx, opts);
                        if (next >= 0 && next < uiMeta.choices.Count) v.i = uiMeta.choices[next].value;
                    } else if ((pt == "IntSlider" || pt == "Dropdown") && hasRange) {
                        int mn = Mathf.RoundToInt(minRange);
                        int mx = Mathf.RoundToInt(maxRange);
                        if (mn > mx) { int tmp = mn; mn = mx; mx = tmp; }
                        v.i = EditorGUILayout.IntSlider(label, v.i, mn, mx);
                    } else {
                        v.i = EditorGUILayout.IntField(label, v.i);
                    }
                    break;
                case "Bool":
                case "Toggle":
                    v.kind = "Bool";
                    v.b = EditorGUILayout.Toggle(label, v.b);
                    break;
                case "String":
                case "Code":
                case "FilePath":
                    v.kind = "String";
                    v.s = EditorGUILayout.TextField(label, v.s ?? "");
                    break;
                case "Vec2": {
                    var nv = EditorGUILayout.Vector2Field(label, v.AsVec2());
                    v.SetVec2(nv);
                    break;
                }
                case "Vec3": {
                    var nv = EditorGUILayout.Vector3Field(label, v.AsVec3());
                    v.SetVec3(nv);
                    break;
                }
                case "Vec4": {
                    var nv = EditorGUILayout.Vector4Field(label, v.AsVec4());
                    v.SetVec4(nv);
                    break;
                }
                case "Color": {
                    var nv = EditorGUILayout.ColorField(label, v.AsColor(false));
                    v.SetColor(nv, false);
                    break;
                }
                case "Color4": {
                    var nv = EditorGUILayout.ColorField(label, v.AsColor(true));
                    v.SetColor(nv, true);
                    break;
                }
                case "Float":
                case "FloatSlider":
                default:
                    v.kind = "Float";
                    v.f = (pt == "FloatSlider" && hasRange) ? EditorGUILayout.Slider(label, v.f, minRange, maxRange) : EditorGUILayout.FloatField(label, v.f);
                    break;
            }

            if (EditorGUI.EndChangeCheck()) {
                t.paramValues[p.name] = v;
                EditorUtility.SetDirty(t);
                if (t.autoCook) t.Cook();
            }
        }

        void DrawBlockProgressBar(bool isCooking) {
            Rect rect = GUILayoutUtility.GetRect(0, 18, GUILayout.ExpandWidth(true));
            float blockW = rect.width / BLOCK_COUNT;
            int filledBlocks = 0;

            if (isCooking) {
                float elapsed = (float)(EditorApplication.timeSinceStartup - _cookStartTime);
                float progress = Mathf.Repeat(elapsed / COOK_ANIM_DURATION, 1f); // 0 → 1 loop
                filledBlocks = Mathf.FloorToInt(progress * (BLOCK_COUNT + 1));
                Repaint(); // animate
            }

            Color emptyColor = new Color(0.15f, 0.17f, 0.20f);
            Color fillColor = new Color(0.25f, 0.85f, 0.45f);
            Color glowColor = new Color(0.4f, 1f, 0.6f);

            for (int i = 0; i < BLOCK_COUNT; i++) {
                Rect blockRect = new Rect(rect.x + i * blockW + 1, rect.y + 3, blockW - 2, rect.height - 6);
                bool filled = i < filledBlocks;
                bool isHead = isCooking && i == filledBlocks - 1;
                Color c = filled ? (isHead ? glowColor : fillColor) : emptyColor;
                EditorGUI.DrawRect(blockRect, c);
            }
        }

        static int GetTriangleCountSafe(Mesh m) {
            if (m == null || m.subMeshCount <= 0) return 0;
            long idx = 0;
            for (int s = 0; s < m.subMeshCount; s++) {
                if (m.GetTopology(s) == MeshTopology.Triangles) idx += (long)m.GetIndexCount(s);
            }
            return (int)(idx / 3);
        }

        void Bake(CunningCDAInstance t, bool replace) {
            string path = EditorUtility.SaveFilePanelInProject("Save Prefab", t.asset.name + "_Baked", "prefab", "Choose location for baked prefab");
            if (string.IsNullOrEmpty(path)) return;

            string dir = System.IO.Path.GetDirectoryName(path);
            string meshDir = System.IO.Path.Combine(dir, "Meshes");
            if (!System.IO.Directory.Exists(meshDir)) System.IO.Directory.CreateDirectory(meshDir);

            GameObject root = new GameObject(t.name + "_Baked");
            root.transform.position = t.transform.position;
            root.transform.rotation = t.transform.rotation;
            root.transform.localScale = t.transform.localScale;

            try {
                int childCount = t.transform.childCount;
                for (int i = 0; i < childCount; i++) {
                    var child = t.transform.GetChild(i);
                    var mf = child.GetComponent<MeshFilter>();
                    var mr = child.GetComponent<MeshRenderer>();
                    if (mf && mf.sharedMesh) {
                        GameObject go = new GameObject(child.name);
                        go.transform.SetParent(root.transform, false);
                        go.transform.localPosition = child.localPosition;
                        go.transform.localRotation = child.localRotation;
                        go.transform.localScale = child.localScale;

                        var nmf = go.AddComponent<MeshFilter>();
                        var nmr = go.AddComponent<MeshRenderer>();

                        // Save Mesh
                        Mesh m = UnityEngine.Object.Instantiate(mf.sharedMesh);
                        m.name = child.name + "_" + System.Guid.NewGuid().ToString().Substring(0, 6);
                        string meshPath = System.IO.Path.Combine(meshDir, m.name + ".asset").Replace('\\', '/');
                        AssetDatabase.CreateAsset(m, meshPath);
                        nmf.sharedMesh = m;

                        // Materials - keep reference
                        nmr.sharedMaterials = mr.sharedMaterials;
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);

                if (replace) {
                    var inst = PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path)) as GameObject;
                    if (inst != null) {
                        inst.transform.position = t.transform.position;
                        inst.transform.rotation = t.transform.rotation;
                        inst.transform.localScale = t.transform.localScale;
                        Undo.RegisterCreatedObjectUndo(inst, "Bake & Replace");
                        Undo.DestroyObjectImmediate(t.gameObject);
                    }
                }
            } finally {
                if (root) UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // Houdini-style resolver registry lives in CunningInputUtility.
    }
}
