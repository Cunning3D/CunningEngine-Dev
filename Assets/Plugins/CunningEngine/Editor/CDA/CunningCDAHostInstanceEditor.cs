using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CunningEngine.CDA;

namespace CunningEngine.Editor.CDA {
    [CustomEditor(typeof(CunningCDAHostInstance))]
    public sealed class CunningCDAHostInstanceEditor : UnityEditor.Editor {
        bool _foldInputs = true, _foldParams = true, _foldOutputs = true, _foldRuntime = false;
        readonly Dictionary<int, string> _inputErrors = new();
        readonly Dictionary<string, bool> _groupFolds = new();
        double _cookStartTime;
        const int BlockCount = 20;
        const float CookAnimDuration = 2.5f;

        public override void OnInspectorGUI() {
            var t = (CunningCDAHostInstance)target;
            serializedObject.Update();

            string name = t.asset != null ? t.asset.name : "No CDA Asset";
            string path = t.asset != null ? t.asset.sourcePath : "";
            string uuid = ExtractUuid(t.asset);
            CdaEditorStyles.DrawHeader(name, path, uuid);
            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            var nextAsset = (CDAAssetObject)EditorGUILayout.ObjectField("CDA Asset", t.asset, typeof(CDAAssetObject), false);
            if (EditorGUI.EndChangeCheck()) {
                Undo.RecordObject(t, "Assign CDA Asset");
                t.asset = nextAsset;
                t.QueueCook();
                EditorUtility.SetDirty(t);
            }

            DrawCookBar(t);
            DrawStatus(t);

            if (t.asset == null) {
                EditorGUILayout.HelpBox("Drag a CDA asset into the Scene or assign one here. One host cooks every CDA output and emits typed child outputs.", MessageType.Info);
                return;
            }

            DrawInputs(t);
            DrawParameters(t);
            DrawOutputs(t);
            DrawRuntime(t);

            serializedObject.ApplyModifiedProperties();
        }

        void DrawCookBar(CunningCDAHostInstance t) {
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f);
            if (GUILayout.Button("Cook All Outputs", GUILayout.Height(36))) {
                _cookStartTime = EditorApplication.timeSinceStartup;
                t.Cook();
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = t.jobId != 0;
            if (GUILayout.Button("Cancel", GUILayout.Height(36))) t.CancelCook();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        void DrawStatus(CunningCDAHostInstance t) {
            bool cooking = t.jobId != 0;
            string status = cooking ? "Cooking" : "Ready";
            var style = cooking ? CdaEditorStyles.StatusCooking : CdaEditorStyles.StatusReady;
            EditorGUILayout.LabelField("Status: " + status, style);
            EditorGUILayout.LabelField("GPU Backend", t.GpuBackendDisplayName);
            if (!string.IsNullOrEmpty(t.lastStatus)) EditorGUILayout.LabelField("Native", t.lastStatus);
            if (!string.IsNullOrEmpty(t.outputStatus)) EditorGUILayout.LabelField("Output", t.outputStatus);
            DrawBlockProgressBar(cooking);
        }

        void DrawInputs(CunningCDAHostInstance t) {
            int inputCount = t.asset.inputs != null ? t.asset.inputs.Count : 0;
            if (!CdaEditorStyles.DrawSection("INPUTS", inputCount, ref _foldInputs)) return;
            while (t.inputs.Count < inputCount) t.inputs.Add(null);
            if (t.inputs.Count > inputCount) t.inputs.RemoveRange(inputCount, t.inputs.Count - inputCount);
            EditorGUI.indentLevel++;
            for (int i = 0; i < inputCount; i++) {
                var p = t.asset.inputs[i];
                string label = string.IsNullOrEmpty(p.name) ? "Input " + i : p.name;
                EditorGUI.BeginChangeCheck();
                UnityEngine.Object picked = EditorGUILayout.ObjectField(label, t.inputs[i], typeof(UnityEngine.Object), true);
                if (EditorGUI.EndChangeCheck()) {
                    var resolved = CunningInputUtility.Resolve(picked);
                    if (picked != null && resolved == null) {
                        _inputErrors[i] = "Unsupported input. " + CunningInputUtility.GetSupportedTypesHint();
                    } else {
                        _inputErrors.Remove(i);
                        Undo.RecordObject(t, "Assign CDA Input");
                        t.inputs[i] = resolved;
                        t.QueueCook();
                        EditorUtility.SetDirty(t);
                    }
                }
                if (_inputErrors.TryGetValue(i, out var err) && !string.IsNullOrEmpty(err)) EditorGUILayout.HelpBox(err, MessageType.Warning);
            }
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
        }

        void DrawParameters(CunningCDAHostInstance t) {
            int paramCount = t.asset.promoted_params != null ? t.asset.promoted_params.Count : 0;
            if (!CdaEditorStyles.DrawSection("PARAMETERS", paramCount, ref _foldParams)) return;
            if (paramCount == 0) return;

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reset All to Defaults", EditorStyles.miniButton)) {
                Undo.RecordObject(t, "Reset CDA Parameters");
                t.paramValues.Clear();
                foreach (var p in t.asset.promoted_params) if (p != null && !string.IsNullOrEmpty(p.name)) t.paramValues[p.name] = CdaParamValue.FromDefaultJson(p.default_value_json);
                t.QueueCook();
                EditorUtility.SetDirty(t);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel++;
            var groups = t.asset.promoted_params.GroupBy(p => p != null ? (p.group ?? "") : "").OrderBy(g => g.Key);
            foreach (var group in groups) {
                bool open = true;
                if (!string.IsNullOrEmpty(group.Key)) {
                    if (!_groupFolds.ContainsKey(group.Key)) _groupFolds[group.Key] = true;
                    _groupFolds[group.Key] = EditorGUILayout.Foldout(_groupFolds[group.Key], group.Key, true, EditorStyles.foldoutHeader);
                    open = _groupFolds[group.Key];
                }
                if (!open) continue;
                foreach (var p in group.OrderBy(x => x != null ? x.order : 0)) if (p != null && !string.IsNullOrEmpty(p.name)) DrawParam(t, p);
            }
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
        }

        void DrawOutputs(CunningCDAHostInstance t) {
            int outputCount = t.asset.outputs != null ? t.asset.outputs.Count : 0;
            if (!CdaEditorStyles.DrawSection("OUTPUTS", outputCount, ref _foldOutputs)) return;
            EditorGUI.indentLevel++;
            for (int i = 0; i < outputCount; i++) {
                var p = t.asset.outputs[i];
                string label = string.IsNullOrEmpty(p.name) ? "Output " + i : p.name;
                string type = string.IsNullOrEmpty(p.data_type) ? "RuntimeValue" : p.data_type;
                EditorGUILayout.LabelField(label, type);
            }
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
        }

        void DrawRuntime(CunningCDAHostInstance t) {
            if (!CdaEditorStyles.DrawSection("RUNTIME", -1, ref _foldRuntime)) return;
            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();
            var gpuBackendMode = (CunningCDAHostInstance.CunningCdaGpuBackendMode)EditorGUILayout.EnumPopup("GPU Backend", t.gpuBackendMode);
            EditorGUILayout.HelpBox(GpuBackendHelp(gpuBackendMode), MessageType.None);
            bool autoCook = EditorGUILayout.Toggle("Auto Cook", t.autoCook);
            bool cookInEditMode = EditorGUILayout.Toggle("Cook In Edit Mode", t.cookInEditMode);
            bool cookInPlayMode = EditorGUILayout.Toggle("Cook In Play Mode", t.cookInPlayMode);
            bool enableInstancing = EditorGUILayout.Toggle("Enable Instancing", t.enableInstancing);
            bool validateGpuBackendParity = EditorGUILayout.Toggle("Validate GPU Backend Parity", t.validateGpuBackendParity);
            float gpuParityEpsilon = EditorGUILayout.FloatField("GPU Parity Epsilon", t.gpuParityEpsilon);
            Vector3 defaultTerrainSize = EditorGUILayout.Vector3Field("Default Terrain Size", t.defaultTerrainSize);
            ulong cpuDeadlineNs = ULongField("CPU Deadline Ns", t.cpuDeadlineNs);
            ulong gpuDeadlineNs = ULongField("GPU Deadline Ns", t.gpuDeadlineNs);
            ulong gpuBudgetBytes = ULongField("GPU Budget Bytes", t.gpuBudgetBytes);
            if (EditorGUI.EndChangeCheck()) {
                Undo.RecordObject(t, "Edit CDA Runtime Settings");
                bool gpuBackendChanged = t.gpuBackendMode != gpuBackendMode;
                t.autoCook = autoCook;
                t.cookInEditMode = cookInEditMode;
                t.cookInPlayMode = cookInPlayMode;
                t.enableInstancing = enableInstancing;
                t.validateGpuBackendParity = validateGpuBackendParity;
                t.gpuParityEpsilon = Mathf.Max(1e-6f, gpuParityEpsilon);
                t.defaultTerrainSize = defaultTerrainSize;
                t.cpuDeadlineNs = cpuDeadlineNs;
                t.gpuDeadlineNs = gpuDeadlineNs;
                t.gpuBudgetBytes = gpuBudgetBytes;
                if (gpuBackendChanged) t.SetGpuBackendMode(gpuBackendMode);
                else t.QueueCook();
                EditorUtility.SetDirty(t);
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Run GPU Backend Parity")) t.QueueGpuBackendParity(0);
            if (!string.IsNullOrEmpty(t.gpuParityStatus)) EditorGUILayout.LabelField(t.gpuParityStatus);
            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel--;
        }

        static string GpuBackendHelp(CunningCDAHostInstance.CunningCdaGpuBackendMode mode) {
            return mode == CunningCDAHostInstance.CunningCdaGpuBackendMode.StandaloneWgpuBackend
                ? "StandaloneWgpuBackend: Cunning owns GPU execution for the most stable cross-host semantics and scheduling control."
                : "EngineHostedBackend: Unity owns GPU execution, enabling the path toward zero-copy renderer, Terrain, CommandBuffer, and packaged-engine integration.";
        }

        void DrawParam(CunningCDAHostInstance t, CdaPromotedParam p) {
            if (!t.paramValues.TryGetValue(p.name, out var v)) {
                v = CdaParamValue.FromDefaultJson(p.default_value_json);
                t.paramValues[p.name] = v;
            }

            EditorGUI.BeginChangeCheck();
            string pt = NormalizeControlKind(p.param_type ?? v.kind ?? "Float");
            GUIContent label = new GUIContent(p.label ?? p.name, p.tooltip ?? "");
            bool hasRange = p.min.HasValue && p.max.HasValue;
            float minRange = p.min ?? 0f;
            float maxRange = p.max ?? 1f;

            switch (pt) {
                case "Int":
                case "IntSlider":
                    v.kind = "Int";
                    v.i = pt == "IntSlider" && hasRange ? EditorGUILayout.IntSlider(label, v.i, Mathf.RoundToInt(minRange), Mathf.RoundToInt(maxRange)) : EditorGUILayout.IntField(label, v.i);
                    break;
                case "Dropdown":
                    v.kind = "Int";
                    if (p.choices != null && p.choices.Count > 0) {
                        var selected = Mathf.Max(0, p.choices.FindIndex(c => c != null && c.value == v.i));
                        var labels = p.choices.Select(c => c != null && !string.IsNullOrEmpty(c.label) ? c.label : "").ToArray();
                        selected = EditorGUILayout.Popup(label, selected, labels);
                        v.i = p.choices[Mathf.Clamp(selected, 0, p.choices.Count - 1)].value;
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
                case "Vec2":
                    v.SetVec2(EditorGUILayout.Vector2Field(label, v.AsVec2()));
                    break;
                case "Vec3":
                    v.SetVec3(EditorGUILayout.Vector3Field(label, v.AsVec3()));
                    break;
                case "Vec4":
                    v.SetVec4(EditorGUILayout.Vector4Field(label, v.AsVec4()));
                    break;
                case "Color":
                    v.SetColor(EditorGUILayout.ColorField(label, v.AsColor(false)), false);
                    break;
                case "Color4":
                    v.SetColor(EditorGUILayout.ColorField(label, v.AsColor(true)), true);
                    break;
                case "Float":
                case "FloatSlider":
                default:
                    v.kind = "Float";
                    v.f = pt == "FloatSlider" && hasRange ? EditorGUILayout.Slider(label, v.f, minRange, maxRange) : EditorGUILayout.FloatField(label, v.f);
                    break;
            }

            if (EditorGUI.EndChangeCheck()) {
                Undo.RecordObject(t, "Edit CDA Parameter");
                t.paramValues[p.name] = v;
                t.QueueCook();
                EditorUtility.SetDirty(t);
            }
        }

        static string NormalizeControlKind(string kind) {
            if (string.IsNullOrWhiteSpace(kind)) return "Float";
            switch (kind.Trim().ToLowerInvariant()) {
                case "float": case "f32": return "Float";
                case "floatslider": case "slider": return "FloatSlider";
                case "int": case "i32": return "Int";
                case "intslider": return "IntSlider";
                case "dropdown": case "enum": return "Dropdown";
                case "bool": case "toggle": return "Bool";
                case "string": return "String";
                case "code": return "Code";
                case "filepath": case "file": return "FilePath";
                case "vec2": case "vector2": return "Vec2";
                case "vec3": case "vector3": return "Vec3";
                case "vec4": case "vector4": return "Vec4";
                case "color": return "Color";
                case "color4": return "Color4";
                default: return kind;
            }
        }

        static ulong ULongField(string label, ulong value) {
            string next = EditorGUILayout.TextField(label, value.ToString(CultureInfo.InvariantCulture));
            return ulong.TryParse(next, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : value;
        }

        static string ExtractUuid(CDAAssetObject asset) {
            if (asset == null || string.IsNullOrEmpty(asset.sourceJson)) return "";
            var match = System.Text.RegularExpressions.Regex.Match(asset.sourceJson, "\"uuid\"\\s*:\\s*\"([^\"]+)\"");
            return match.Success ? match.Groups[1].Value : "";
        }

        void DrawBlockProgressBar(bool isCooking) {
            Rect rect = GUILayoutUtility.GetRect(0, 18, GUILayout.ExpandWidth(true));
            float blockW = rect.width / BlockCount;
            int filled = 0;
            if (isCooking) {
                float elapsed = (float)(EditorApplication.timeSinceStartup - _cookStartTime);
                filled = Mathf.FloorToInt(Mathf.Repeat(elapsed / CookAnimDuration, 1f) * (BlockCount + 1));
                Repaint();
            }
            Color empty = new Color(0.15f, 0.17f, 0.20f);
            Color full = new Color(0.2f, 0.85f, 0.45f);
            for (int i = 0; i < BlockCount; i++) {
                var r = new Rect(rect.x + i * blockW + 1, rect.y + 2, blockW - 2, rect.height - 4);
                EditorGUI.DrawRect(r, i < filled ? full : empty);
            }
        }
    }
}
