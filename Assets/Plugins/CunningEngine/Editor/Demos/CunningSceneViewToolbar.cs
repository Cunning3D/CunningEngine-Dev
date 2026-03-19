using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;

namespace CunningEngine.Editor.Demos {
    [EditorToolbarElement(Id, typeof(SceneView))]
    sealed class CunningDrawModeDropdown : EditorToolbarDropdown {
        public const string Id = "CunningEngine/SceneView/CunningDrawMode";

        public CunningDrawModeDropdown() {
            text = "Cunning";
            tooltip = "Global Cunning SceneView draw and debug modes";
            clicked += ShowMenu;
        }

        void ShowMenu() {
            var menu = new GenericMenu();

            AddWireModeItem(menu, "Wire/Off", CunningSceneViewModeState.WireModeOff);
            AddWireModeItem(menu, "Wire/Selected", CunningSceneViewModeState.WireModeSelected);
            AddWireModeItem(menu, "Wire/All", CunningSceneViewModeState.WireModeAll);
            menu.AddSeparator("");
            AddDebugModeItem(menu, "Debug/Off", CunningSceneViewModeState.ModeNone);
            AddDebugModeItem(menu, "Debug/Prim#", CunningSceneViewModeState.ModePrimNumber);
            AddDebugModeItem(menu, "Debug/PrimN", CunningSceneViewModeState.ModePrimNormal);
            AddDebugModeItem(menu, "Debug/Point#", CunningSceneViewModeState.ModePointNumber);
            AddDebugModeItem(menu, "Debug/Normal", CunningSceneViewModeState.ModeNormal);
            menu.AddSeparator("");
            menu.AddDisabledItem(new GUIContent($"Current Wire: {CunningSceneViewModeState.GetWireModeLabel()}"));

            var rect = worldBound;
            menu.DropDown(new Rect(rect.xMin, rect.yMax, 0f, 0f));
        }

        static void AddWireModeItem(GenericMenu menu, string label, int wireMode) {
            menu.AddItem(
                new GUIContent(label),
                CunningSceneViewModeState.WireMode == wireMode,
                () => CunningSceneViewModeState.WireMode = wireMode);
        }

        static void AddDebugModeItem(GenericMenu menu, string label, int debugMode) {
            menu.AddItem(
                new GUIContent(label),
                CunningSceneViewModeState.Mode == debugMode,
                () => {
                    CunningSceneViewModeState.Enabled = debugMode != CunningSceneViewModeState.ModeNone;
                    CunningSceneViewModeState.Mode = debugMode;
                });
        }
    }

    [Overlay(typeof(SceneView), "Cunning", true)]
    public sealed class CunningDrawToolbarOverlay : ToolbarOverlay {
        public CunningDrawToolbarOverlay() : base(CunningDrawModeDropdown.Id) { }
    }
}
