using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CunningEngine;
using CunningEngine.CDA;

namespace CunningEngine.Editor.CDA {
    [CustomEditor(typeof(CunningCDAInstance))]
    public sealed class CunningCDAInstanceEditor : UnityEditor.Editor {
        bool _foldInputs = true, _foldParams = true, _foldOutputs = true, _foldOptions = false;
        Dictionary<string, bool> _groupFolds = new();
        readonly Dictionary<int, string> _inputErrors = new();
        string _lastError;
        double _cookStartTime;
        const int BLOCK_COUNT = 20;
        const float COOK_ANIM_DURATION = 2.5f; // seconds for full bar animation

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

            if (t.asset == null) { EditorGUILayout.HelpBox("Drag a .cda file onto the Hierarchy or Scene to create an instance.", MessageType.Info); return; }

            // === COOK BAR (right after header) ===
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f);
            if (GUILayout.Button("Cook", GUILayout.Height(36))) { _lastError = null; _cookStartTime = EditorApplication.timeSinceStartup; t.Cook(); }
            GUI.backgroundColor = Color.white;
            GUI.enabled = t.jobId != 0;
            if (GUILayout.Button("Cancel", GUILayout.Height(36))) { NativeMethods.cunning_job_cancel(t.jobId); t.jobId = 0; }
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
                            _inputErrors[i] = $"Unsupported input: {picked.GetType().Name}. Supported: GameObject/Component with CunningMesh or SplineContainer, or any MonoBehaviour implementing ICunningInputHandle.";
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
                    } else GUILayout.Label("(not generated)", EditorStyles.miniLabel);
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.Space(4);

            // === OPTIONS SECTION ===
            if (CdaEditorStyles.DrawSection("OPTIONS", -1, ref _foldOptions)) {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                t.autoCook = EditorGUILayout.Toggle("Auto Cook on Change", t.autoCook);
                if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(t);
                EditorGUI.indentLevel--;
            }
            serializedObject.ApplyModifiedProperties();
        }

        void DrawParam(CunningCDAInstance t, CdaPromotedParam p) {
            if (!t.paramValues.TryGetValue(p.name, out var v)) { v = CdaParamValue.FromDefaultJson(p.default_value_json); t.paramValues[p.name] = v; }
            
            EditorGUI.BeginChangeCheck();
            string pt = (p.param_type ?? "Float").ToLowerInvariant();
            string labelStr = p.label ?? p.name;
            GUIContent label = new GUIContent(labelStr, p.tooltip ?? "");
            bool hasRange = p.min.HasValue && p.max.HasValue;

            switch (pt) {
                case "int":
                    v.kind = "Int";
                    v.i = hasRange ? EditorGUILayout.IntSlider(label, v.i, (int)p.min.Value, (int)p.max.Value) : EditorGUILayout.IntField(label, v.i);
                    break;
                case "bool": case "toggle": v.kind = "Bool"; v.b = EditorGUILayout.Toggle(label, v.b); break;
                case "string": v.kind = "String"; v.s = EditorGUILayout.TextField(label, v.s ?? ""); break;
                case "vec2": { var nv = EditorGUILayout.Vector2Field(label, v.AsVec2()); v.SetVec2(nv); } break;
                case "vec3": { var nv = EditorGUILayout.Vector3Field(label, v.AsVec3()); v.SetVec3(nv); } break;
                case "vec4": { var nv = EditorGUILayout.Vector4Field(label, v.AsVec4()); v.SetVec4(nv); } break;
                case "color": { var nv = EditorGUILayout.ColorField(label, v.AsColor(false)); v.SetColor(nv, false); } break;
                case "color4": { var nv = EditorGUILayout.ColorField(label, v.AsColor(true)); v.SetColor(nv, true); } break;
                default:
                    v.kind = "Float";
                    v.f = hasRange ? EditorGUILayout.Slider(label, v.f, p.min.Value, p.max.Value) : EditorGUILayout.FloatField(label, v.f);
                    break;
            }

            if (EditorGUI.EndChangeCheck()) { t.paramValues[p.name] = v; EditorUtility.SetDirty(t); if (t.autoCook) t.Cook(); }
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
            for (int s = 0; s < m.subMeshCount; s++) if (m.GetTopology(s) == MeshTopology.Triangles) idx += (long)m.GetIndexCount(s);
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
                        Mesh m = Object.Instantiate(mf.sharedMesh);
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
                if (root) Object.DestroyImmediate(root);
            }
        }

        // Houdini-style resolver registry lives in CunningInputUtility.
    }
}
