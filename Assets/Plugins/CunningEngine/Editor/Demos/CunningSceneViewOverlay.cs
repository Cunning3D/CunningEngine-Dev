using System;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CunningEngine.Editor.Demos {
    static class CunningSceneViewModeState {
        const string KEnabled = "CunningEngine.SceneView.Enabled";
        const string KMode = "CunningEngine.SceneView.Mode";

        public static event Action Changed;
        static void Touch() { Changed?.Invoke(); SceneView.RepaintAll(); }

        public static bool Enabled { get => EditorPrefs.GetBool(KEnabled, true); set { EditorPrefs.SetBool(KEnabled, value); Touch(); } }
        public static int Mode { get => EditorPrefs.GetInt(KMode, 0); set { EditorPrefs.SetInt(KMode, value); Touch(); } } // 0=None,1=Prim#,2=PrimN,3=Point#,4=Normal
        public static bool NormalIsVertex { get => false; set { } } // compat: normals auto-select (vertex if possible else point)

        public const int ModeNone = 0, ModePrimNumber = 1, ModePrimNormal = 2, ModePointNumber = 3, ModeNormal = 4;

        public static bool HasSelectedCunningMesh() {
            var gos = Selection.gameObjects;
            if (gos == null || gos.Length == 0) return false;
            foreach (var go in gos) if (go && (go.GetComponent<CunningMesh>() || go.GetComponentInParent<CunningMesh>())) return true;
            return false;
        }
    }

    [Overlay(typeof(SceneView), "Cunning", true)]
    public sealed class CunningSceneViewOverlay : Overlay {
        static bool TryShowOverlay(object overlay) {
            try {
                var p = overlay.GetType().GetProperty("displayed") ?? overlay.GetType().GetProperty("Displayed");
                if (p != null && p.PropertyType == typeof(bool) && p.CanWrite) { p.SetValue(overlay, true); return true; }
            } catch { }
            return false;
        }

        public override VisualElement CreatePanelContent() {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;

            var enabled = new ToolbarToggle { text = "Cunning", tooltip = "Ngon debug overlay (Cunning data, not Unity triangles)" };
            enabled.style.marginBottom = 2;
            enabled.RegisterValueChangedCallback(e => CunningSceneViewModeState.Enabled = e.newValue);
            root.Add(enabled);

            ToolbarToggle MakeRadio(string text, int mode) {
                var t = new ToolbarToggle { text = text };
                t.RegisterValueChangedCallback(e => { if (e.newValue) { CunningSceneViewModeState.Enabled = true; CunningSceneViewModeState.Mode = mode; } else if (CunningSceneViewModeState.Mode == mode) CunningSceneViewModeState.Mode = CunningSceneViewModeState.ModeNone; });
                return t;
            }
            void Sync(ToolbarToggle en, ToolbarToggle primN, ToolbarToggle primNor, ToolbarToggle pNum, ToolbarToggle n) {
                en.SetValueWithoutNotify(CunningSceneViewModeState.Enabled);
                primN.SetValueWithoutNotify(CunningSceneViewModeState.Mode == CunningSceneViewModeState.ModePrimNumber);
                primNor.SetValueWithoutNotify(CunningSceneViewModeState.Mode == CunningSceneViewModeState.ModePrimNormal);
                pNum.SetValueWithoutNotify(CunningSceneViewModeState.Mode == CunningSceneViewModeState.ModePointNumber);
                n.SetValueWithoutNotify(CunningSceneViewModeState.Mode == CunningSceneViewModeState.ModeNormal);
            }

            var primNum = MakeRadio("Prim#", CunningSceneViewModeState.ModePrimNumber);
            primNum.style.marginBottom = 2;
            var primNor = MakeRadio("PrimN", CunningSceneViewModeState.ModePrimNormal);
            primNor.style.marginBottom = 2;
            var pointNum = MakeRadio("Point#", CunningSceneViewModeState.ModePointNumber);
            pointNum.style.marginBottom = 2;
            var normal = MakeRadio("Normal", CunningSceneViewModeState.ModeNormal);
            normal.style.marginBottom = 2;
            root.Add(primNum); root.Add(primNor); root.Add(pointNum); root.Add(normal);

            void OnChanged() => Sync(enabled, primNum, primNor, pointNum, normal);
            CunningSceneViewModeState.Changed += OnChanged;
            root.RegisterCallback<DetachFromPanelEvent>(_ => CunningSceneViewModeState.Changed -= OnChanged);
            OnChanged();

            void UpdateVisible() {
                var has = CunningSceneViewModeState.HasSelectedCunningMesh();
                root.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
                if (has) TryShowOverlay(this); // only force-show; never force-hide (Unity remembers hide state)
            }
            UpdateVisible();
            Selection.selectionChanged += UpdateVisible;
            root.RegisterCallback<DetachFromPanelEvent>(_ => Selection.selectionChanged -= UpdateVisible);

            return root;
        }
    }
}

