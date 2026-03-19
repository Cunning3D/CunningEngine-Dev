using System.Collections.Generic;
using ParadoxNotion.Design;
using UnityEditor;
using UnityEngine;

namespace UnityEditor.Splines.Extension
{
    public partial class RoadGeneratorTool
    {
        private const float OfficialWindowMinWidth = 500f;
        private const float OfficialWindowMinHeight = 720f;
        private const float OfficialWindowRootPaddingX = 12f;
        private const float OfficialWindowRootPaddingTop = 14f;
        private const float OfficialWindowRootPaddingBottom = 12f;
        private const float OfficialWindowChromeHeight = 22f;
        private const float OfficialWindowSectionGap = 10f;
        private const float OfficialWindowPanelPadding = 14f;
        private const float OfficialWindowHeaderTitleGap = 4f;
        private const float OfficialWindowHeaderChipGap = 8f;
        private const float OfficialWindowRailMinWidth = 128f;
        private const float OfficialWindowRailMaxWidth = 144f;
        private const float OfficialWindowRailInset = 10f;
        private const float OfficialWindowContentInset = 14f;
        private const float OfficialWindowContentGap = 10f;
        private const float OfficialWindowContentDividerGap = 10f;
        private const float OfficialWindowFooterButtonGap = 8f;
        private const float OfficialWindowButtonWidth = 116f;
        private const float OfficialWindowButtonHeight = 22f;
        private const float OfficialWindowMinimumBodyHeight = 180f;

        private static readonly Dictionary<OfficialTab, Vector2> officialWindowScrollPositions = new Dictionary<OfficialTab, Vector2>();
        private static GUIStyle officialWindowTitleStyle;
        private static GUIStyle officialWindowSubtitleStyle;
        private static GUIStyle officialWindowSectionTitleStyle;
        private static GUIStyle officialWindowNavLabelStyle;
        private static GUIStyle officialWindowNavFallbackStyle;
        private static GUIStyle officialWindowButtonStyle;
        private static GUIStyle officialWindowRootStyle;
        private static GUIStyle officialWindowPanelStyle;
        private static GUIStyle officialWindowCloseButtonStyle;
        private static GUIStyle officialWindowChromeStyle;
        private static GUIStyle officialWindowChromeTitleStyle;
        private static GUIStyle officialWindowChromeBadgeStyle;
        private static readonly Dictionary<string, GUIStyle> officialWindowRoundedStyleCache = new Dictionary<string, GUIStyle>();
        private static string officialWindowStyleCacheKey;

        internal static void DrawOfficialWindow(int windowID)
        {
            RoadGeneratorSvgIconService.WarmUp();
            OfficialUiSnapshot snapshot = CaptureOfficialUiSnapshot();
            RoadGeneratorOfficialTheme visualTheme = snapshot.VisualTheme;
            EnsureOfficialUiStyles(visualTheme);
            EnsureOfficialWindowStyles(visualTheme);

            windowRect.width = Mathf.Max(windowRect.width, OfficialWindowMinWidth);
            windowRect.height = Mathf.Max(windowRect.height, OfficialWindowMinHeight);

            Rect windowBounds = new Rect(0f, 0f, windowRect.width, windowRect.height);
            DrawOfficialWindowRoot(windowBounds, visualTheme);

            Rect clientRect = new Rect(
                OfficialWindowRootPaddingX,
                OfficialWindowRootPaddingTop,
                windowRect.width - OfficialWindowRootPaddingX * 2f,
                windowRect.height - OfficialWindowRootPaddingTop - OfficialWindowRootPaddingBottom);

            float headerHeight = MeasureOfficialWindowHeaderHeight(clientRect.width, snapshot);
            float footerHeight = MeasureOfficialWindowFooterHeight(clientRect.width, snapshot);
            float availableBodyHeight = clientRect.height - headerHeight - footerHeight - OfficialWindowSectionGap * 2f;
            float bodyHeight = Mathf.Max(OfficialWindowMinimumBodyHeight, availableBodyHeight);

            Rect headerRect = new Rect(clientRect.x, clientRect.y, clientRect.width, headerHeight);
            Rect bodyRect = new Rect(clientRect.x, headerRect.yMax + OfficialWindowSectionGap, clientRect.width, bodyHeight);
            Rect footerRect = new Rect(clientRect.x, bodyRect.yMax + OfficialWindowSectionGap, clientRect.width, footerHeight);

            if (footerRect.yMax > clientRect.yMax)
            {
                float overflow = footerRect.yMax - clientRect.yMax;
                bodyRect.height = Mathf.Max(OfficialWindowMinimumBodyHeight, bodyRect.height - overflow);
                footerRect.y = bodyRect.yMax + OfficialWindowSectionGap;
            }

            DrawOfficialWindowHeader(headerRect, snapshot, visualTheme);
            DrawOfficialWindowBody(bodyRect, snapshot, visualTheme);
            DrawOfficialWindowFooter(footerRect, snapshot, visualTheme);
            DrawOfficialWindowCloseButton();
            GUI.DragWindow(new Rect(0f, 0f, windowRect.width - 36f, 30f));
        }

        private static void DrawOfficialWindowHeader(Rect rect, OfficialUiSnapshot snapshot, RoadGeneratorOfficialTheme visualTheme)
        {
            DrawWindowPanel(rect, visualTheme.SurfaceBackgroundColor, visualTheme.SurfaceBorderColor);

            Rect innerRect = InsetRect(rect, OfficialWindowPanelPadding);
            Rect chromeRect = new Rect(innerRect.x, innerRect.y, innerRect.width, OfficialWindowChromeHeight);
            GUI.Box(chromeRect, GUIContent.none, officialWindowChromeStyle);
            GUI.Label(
                new Rect(chromeRect.x + 8f, chromeRect.y, Mathf.Max(120f, chromeRect.width - 116f), chromeRect.height),
                OfficialWindowDisplayName,
                officialWindowChromeTitleStyle);

            string badgeText = snapshot.Theme.DisplayName;
            Vector2 badgeSize = officialWindowChromeBadgeStyle.CalcSize(new GUIContent(badgeText));
            float badgeWidth = Mathf.Clamp(badgeSize.x + 18f, 68f, 120f);
            Rect badgeRect = new Rect(chromeRect.xMax - badgeWidth - 8f, chromeRect.y + 3f, badgeWidth, chromeRect.height - 6f);
            GUI.Box(badgeRect, badgeText, officialWindowChromeBadgeStyle);

            float contentY = chromeRect.yMax + OfficialWindowHeaderChipGap;

            (string key, string value)[] primaryRow = GetOfficialHeaderPrimaryChips(snapshot);
            float primaryHeight = MeasureOfficialChipFlowHeight(innerRect.width, primaryRow);
            DrawOfficialChipFlow(new Rect(innerRect.x, contentY, innerRect.width, primaryHeight), visualTheme, primaryRow);
            contentY += primaryHeight + OfficialWindowHeaderChipGap;

            (string key, string value)[] secondaryRow = GetOfficialHeaderSecondaryChips(snapshot);
            float secondaryHeight = MeasureOfficialChipFlowHeight(innerRect.width, secondaryRow);
            DrawOfficialChipFlow(new Rect(innerRect.x, contentY, innerRect.width, secondaryHeight), visualTheme, secondaryRow);
        }

        private static void DrawOfficialWindowBody(Rect rect, OfficialUiSnapshot snapshot, RoadGeneratorOfficialTheme visualTheme)
        {
            float railWidth = Mathf.Clamp(visualTheme.NavActiveWidth + 8f, OfficialWindowRailMinWidth, OfficialWindowRailMaxWidth);
            Rect navRect = new Rect(rect.x, rect.y, railWidth, rect.height);
            Rect contentRect = new Rect(
                navRect.xMax + visualTheme.CardSpacing,
                rect.y,
                Mathf.Max(220f, rect.width - railWidth - visualTheme.CardSpacing),
                rect.height);

            DrawWindowPanel(navRect, visualTheme.CardBackgroundColor, visualTheme.CardBorderColor);
            DrawWindowPanel(contentRect, visualTheme.SurfaceBackgroundColor, visualTheme.SurfaceBorderColor);
            DrawOfficialWindowNavRail(InsetRect(navRect, OfficialWindowRailInset), snapshot);

            Rect contentInnerRect = InsetRect(contentRect, OfficialWindowContentInset);
            string title = GetOfficialContentTitle(snapshot.ActiveTab);
            string subtitle = GetOfficialContentSubtitle(snapshot);
            float titleHeight = officialWindowSectionTitleStyle.CalcHeight(new GUIContent(title), contentInnerRect.width);
            float subtitleHeight = officialWindowSubtitleStyle.CalcHeight(new GUIContent(subtitle), contentInnerRect.width);
            float headerHeight = titleHeight + OfficialWindowHeaderTitleGap + subtitleHeight;

            GUI.Label(new Rect(contentInnerRect.x, contentInnerRect.y, contentInnerRect.width, titleHeight), title, officialWindowSectionTitleStyle);
            GUI.Label(new Rect(contentInnerRect.x, contentInnerRect.y + titleHeight + OfficialWindowHeaderTitleGap, contentInnerRect.width, subtitleHeight), subtitle, officialWindowSubtitleStyle);

            Rect dividerRect = new Rect(contentInnerRect.x, contentInnerRect.y + headerHeight + OfficialWindowContentGap, contentInnerRect.width, 1f);
            EditorGUI.DrawRect(dividerRect, visualTheme.DividerColor);

            Rect scrollRect = new Rect(
                contentInnerRect.x,
                dividerRect.yMax + OfficialWindowContentDividerGap,
                contentInnerRect.width,
                Mathf.Max(120f, contentInnerRect.yMax - dividerRect.yMax - OfficialWindowContentDividerGap));
            DrawOfficialContentScroll(scrollRect, snapshot.ActiveTab);
        }

        private static void DrawOfficialWindowNavRail(Rect rect, OfficialUiSnapshot snapshot)
        {
            float cursorY = rect.y;
            foreach (OfficialTabTheme tabTheme in GetOfficialTabThemes())
            {
                RoadGeneratorOfficialTheme tabThemeVisual = RoadGeneratorOfficialTheme.Resolve(tabTheme);
                bool isActive = tabTheme.Tab == snapshot.ActiveTab;
                bool isHovered;
                bool isPressed;
                Rect buttonRect = GetOfficialWindowNavButtonRect(rect, cursorY, tabThemeVisual, out isHovered, out isPressed);
                Color fillColor = isActive
                    ? tabThemeVisual.NavActiveFillColor
                    : isPressed ? tabThemeVisual.NavPressedFillColor : isHovered ? tabThemeVisual.NavHoverFillColor : tabThemeVisual.NavIdleFillColor;
                if (fillColor.a > 0.001f)
                {
                    GUI.Box(
                        buttonRect,
                        GUIContent.none,
                        GetOfficialWindowRoundedStyle(
                            fillColor,
                            isActive ? tabThemeVisual.NavActiveBorderColor : tabThemeVisual.NavIdleBorderColor,
                            8));
                }

                if (isActive)
                {
                    EditorGUI.DrawRect(new Rect(buttonRect.x, buttonRect.y + 1f, 2f, buttonRect.height - 2f), tabThemeVisual.AccentColor);
                }

                DrawOfficialWindowNavIcon(buttonRect, tabTheme, tabThemeVisual, isActive);

                Rect labelRect = new Rect(buttonRect.x + 34f, buttonRect.y, Mathf.Max(0f, buttonRect.width - 42f), buttonRect.height);
                GUI.Label(labelRect, tabTheme.ShortLabel, officialWindowNavLabelStyle);

                EditorGUIUtility.AddCursorRect(buttonRect, MouseCursor.Link);
                if (GUI.Button(buttonRect, GUIContent.none, GUIStyle.none))
                {
                    SetOfficialTab(tabTheme.Tab);
                }

                cursorY += tabThemeVisual.NavButtonHeight + 8f;
            }
        }

        private static Rect GetOfficialWindowNavButtonRect(Rect railRect, float y, RoadGeneratorOfficialTheme visualTheme, out bool isHovered, out bool isPressed)
        {
            float fullWidth = Mathf.Max(96f, railRect.width);
            Rect buttonRect = new Rect(railRect.x, y, fullWidth, visualTheme.NavButtonHeight);
            Vector2 mousePosition = Event.current.mousePosition;
            isHovered = buttonRect.Contains(mousePosition);
            isPressed = isHovered && Event.current.type == EventType.MouseDown && Event.current.button == 0;
            return buttonRect;
        }

        private static void DrawOfficialWindowNavIcon(Rect buttonRect, OfficialTabTheme tabTheme, RoadGeneratorOfficialTheme visualTheme, bool isActive)
        {
            RoadGeneratorSvgIconService.IconPresentation presentation = RoadGeneratorSvgIconService.GetPresentation(
                tabTheme.IconName,
                visualTheme,
                isActive,
                EditorGUIUtility.pixelsPerPoint);

            float iconSize = visualTheme.IconSize;
            Rect iconRect = new Rect(buttonRect.x + 10f, buttonRect.y + (buttonRect.height - iconSize) * 0.5f, iconSize, iconSize);
            bool hasTexture = presentation.PrimaryTexture != null || presentation.AccentTexture != null;
            if (!hasTexture)
            {
                GUI.Label(iconRect, tabTheme.ShortLabel.Substring(0, 1), officialWindowNavFallbackStyle);
                return;
            }

            Color previousColor = GUI.color;
            if (presentation.PrimaryTexture != null)
            {
                GUI.color = presentation.PrimaryTint;
                GUI.DrawTexture(iconRect, presentation.PrimaryTexture, ScaleMode.ScaleToFit, true);
            }

            if (presentation.AccentTexture != null)
            {
                GUI.color = presentation.AccentTint;
                GUI.DrawTexture(iconRect, presentation.AccentTexture, ScaleMode.ScaleToFit, true);
            }

            GUI.color = previousColor;
        }

        private static void DrawOfficialContentScroll(Rect rect, OfficialTab activeTab)
        {
            if (!officialWindowScrollPositions.TryGetValue(activeTab, out Vector2 scrollPosition))
            {
                scrollPosition = Vector2.zero;
            }

            GUI.BeginGroup(rect);
            GUILayout.BeginArea(new Rect(0f, 0f, rect.width, rect.height));
            scrollPosition = GUILayout.BeginScrollView(
                scrollPosition,
                false,
                true,
                GUIStyle.none,
                GUI.skin.verticalScrollbar,
                GUIStyle.none,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            DrawOfficialTabContent();
            GUILayout.Space(4f);
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.EndGroup();

            officialWindowScrollPositions[activeTab] = scrollPosition;
        }

        private static void DrawOfficialWindowFooter(Rect rect, OfficialUiSnapshot snapshot, RoadGeneratorOfficialTheme visualTheme)
        {
            DrawWindowPanel(rect, visualTheme.SurfaceBackgroundColor, visualTheme.SurfaceBorderColor);

            Rect innerRect = InsetRect(rect, OfficialWindowPanelPadding);
            (string key, string value)[] footerChips = GetOfficialFooterChips(snapshot);
            float chipHeight = MeasureOfficialChipFlowHeight(innerRect.width, footerChips);
            DrawOfficialChipFlow(new Rect(innerRect.x, innerRect.y, innerRect.width, chipHeight), visualTheme, footerChips);

            float buttonY = innerRect.y + chipHeight + OfficialWindowFooterButtonGap;
            Rect applyRect = new Rect(innerRect.xMax - OfficialWindowButtonWidth * 2f - 8f, buttonY, OfficialWindowButtonWidth, OfficialWindowButtonHeight);
            Rect clearRect = new Rect(innerRect.xMax - OfficialWindowButtonWidth, buttonY, OfficialWindowButtonWidth, OfficialWindowButtonHeight);
            DrawOfficialWindowButton(applyRect, "应用预览", snapshot.HasPreviewObjects, ApplyPreviewFromOfficialUi);
            DrawOfficialWindowButton(clearRect, "清除预览", snapshot.HasPreviewObjects || snapshot.PreviewState != OfficialPreviewState.None, ClearPreviewFromOfficialUi);
        }

        private static void DrawOfficialWindowButton(Rect rect, string label, bool enabled, System.Action action)
        {
            bool previousEnabled = GUI.enabled;
            GUI.enabled = enabled;
            if (GUI.Button(rect, label, officialWindowButtonStyle) && enabled)
            {
                action?.Invoke();
            }
            GUI.enabled = previousEnabled;
        }

        private static void DrawOfficialWindowCloseButton()
        {
            Rect closeButtonRect = new Rect(windowRect.width - 36f, 7f, 26f, 20f);
            GUIContent closeContent = EditorGUIUtility.IconContent(EditorGUIUtility.isProSkin ? "d_winbtn_win_close" : "winbtn_win_close");
            if (closeContent == null || closeContent.image == null)
            {
                closeContent = new GUIContent("×", "Close");
            }
            else
            {
                closeContent.tooltip = "Close";
            }

            if (!GUI.Button(closeButtonRect, closeContent, officialWindowCloseButtonStyle))
            {
                return;
            }

            showWindow = false;
            EditorPrefs.SetBool(ShowWindowPrefKey, false);
            SceneView.RepaintAll();
        }

        private static void DrawOfficialWindowRoot(Rect rect, RoadGeneratorOfficialTheme visualTheme)
        {
            GUI.Box(rect, GUIContent.none, officialWindowRootStyle);
            EditorGUI.DrawRect(new Rect(12f, 1f, Mathf.Max(0f, rect.width - 24f), 2f), visualTheme.AccentColor);
        }

        private static void DrawWindowPanel(Rect rect, Color fillColor, Color borderColor)
        {
            GUI.Box(rect, GUIContent.none, GetOfficialWindowRoundedStyle(fillColor, borderColor, 10));
        }

        private static Rect InsetRect(Rect rect, float padding)
        {
            return new Rect(rect.x + padding, rect.y + padding, rect.width - padding * 2f, rect.height - padding * 2f);
        }

        private static float MeasureOfficialWindowHeaderHeight(float width, OfficialUiSnapshot snapshot)
        {
            float contentWidth = Mathf.Max(220f, width - OfficialWindowPanelPadding * 2f);
            float primaryHeight = MeasureOfficialChipFlowHeight(contentWidth, GetOfficialHeaderPrimaryChips(snapshot));
            float secondaryHeight = MeasureOfficialChipFlowHeight(contentWidth, GetOfficialHeaderSecondaryChips(snapshot));
            return OfficialWindowPanelPadding * 2f
                + OfficialWindowChromeHeight
                + OfficialWindowHeaderChipGap
                + primaryHeight
                + OfficialWindowHeaderChipGap
                + secondaryHeight;
        }

        private static float MeasureOfficialWindowFooterHeight(float width, OfficialUiSnapshot snapshot)
        {
            float contentWidth = Mathf.Max(220f, width - OfficialWindowPanelPadding * 2f);
            float chipHeight = MeasureOfficialChipFlowHeight(contentWidth, GetOfficialFooterChips(snapshot));
            return OfficialWindowPanelPadding * 2f + chipHeight + OfficialWindowFooterButtonGap + OfficialWindowButtonHeight;
        }

        private static (string key, string value)[] GetOfficialHeaderPrimaryChips(OfficialUiSnapshot snapshot)
        {
            return new (string key, string value)[]
            {
                ("Tab", snapshot.Theme.DisplayName),
                ("Road", Ellipsize(snapshot.RoadTypeLabel, 28)),
                ("Custom", Ellipsize(snapshot.CustomRoadLabel, 28))
            };
        }

        private static (string key, string value)[] GetOfficialHeaderSecondaryChips(OfficialUiSnapshot snapshot)
        {
            return new (string key, string value)[]
            {
                ("Selection", Ellipsize(snapshot.SelectedObjectName, 22)),
                ("Auto Preview", snapshot.AutoPreview ? "On" : "Off"),
                ("Realtime Sample", snapshot.AutoSamplePoints ? "On" : "Off"),
                ("Connections", snapshot.SelectedConnectionCount.ToString())
            };
        }

        private static (string key, string value)[] GetOfficialFooterChips(OfficialUiSnapshot snapshot)
        {
            if (snapshot.HandleStatus.HasHandle)
            {
                return new (string key, string value)[]
                {
                    ("Preview", snapshot.PreviewState.ToString()),
                    ("Preview Object", snapshot.HasPreviewObjects ? "Exists" : "None"),
                    ("Target", Ellipsize(snapshot.HandleStatus.TargetName, 18)),
                    ("Handle", snapshot.HandleStatus.Handle.ToString()),
                    ("Dirty", snapshot.HandleStatus.DirtyId.ToString()),
                    ("Sync", snapshot.HandleStatus.SyncState)
                };
            }

            return new (string key, string value)[]
            {
                ("Preview", snapshot.PreviewState.ToString()),
                ("Preview Object", snapshot.HasPreviewObjects ? "Exists" : "None"),
                ("Handle", snapshot.HasSelection ? "None" : "No Selection"),
                ("Sync", snapshot.HandleStatus.SyncState)
            };
        }

        private static void EnsureOfficialWindowStyles(RoadGeneratorOfficialTheme visualTheme)
        {
            string styleKey = $"{ColorUtility.ToHtmlStringRGBA(visualTheme.AccentColor)}|{EditorGUIUtility.isProSkin}|window";
            if (officialWindowTitleStyle != null && officialWindowStyleCacheKey == styleKey)
            {
                return;
            }

            officialWindowStyleCacheKey = styleKey;
            officialWindowRoundedStyleCache.Clear();
            GUIStyle baseRoundedStyle = Styles.roundedBox ?? EditorStyles.helpBox;
            officialWindowPanelStyle = new GUIStyle(baseRoundedStyle)
            {
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                overflow = new RectOffset(0, 0, 0, 0)
            };

            int rootRadius = 12;
            Color rootBorderColor = Color.Lerp(visualTheme.SurfaceBorderColor, visualTheme.AccentColor, EditorGUIUtility.isProSkin ? 0.18f : 0.12f);
            officialWindowRootStyle = CreateRoundedWindowStyle(
                RoadGeneratorOfficialThemeResources.GetRoundedRectTexture(visualTheme.RootBackgroundColor, rootBorderColor, rootRadius, 1),
                rootRadius);
            officialWindowPanelStyle = new GUIStyle(baseRoundedStyle)
            {
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            officialWindowChromeStyle = CreateRoundedWindowStyle(
                RoadGeneratorOfficialThemeResources.GetRoundedRectTexture(
                    EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.035f) : new Color(0f, 0f, 0f, 0.035f),
                    visualTheme.DividerColor,
                    8,
                    1),
                8);

            officialWindowTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                clipping = TextClipping.Clip,
                normal = { textColor = visualTheme.PrimaryTextColor }
            };
            officialWindowChromeTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal = { textColor = visualTheme.PrimaryTextColor }
            };
            officialWindowChromeBadgeStyle = new GUIStyle(EditorStyles.miniButtonMid)
            {
                fontSize = 9,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(8, 8, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                border = new RectOffset(7, 7, 7, 7),
                normal = { textColor = visualTheme.PrimaryTextColor },
                hover = { textColor = visualTheme.PrimaryTextColor },
                active = { textColor = visualTheme.PrimaryTextColor },
                focused = { textColor = visualTheme.PrimaryTextColor }
            };
            ApplyBackgroundToStyleState(
                officialWindowChromeBadgeStyle,
                RoadGeneratorOfficialThemeResources.GetRoundedRectTexture(
                    new Color(visualTheme.AccentColor.r, visualTheme.AccentColor.g, visualTheme.AccentColor.b, EditorGUIUtility.isProSkin ? 0.16f : 0.12f),
                    new Color(visualTheme.AccentColor.r, visualTheme.AccentColor.g, visualTheme.AccentColor.b, EditorGUIUtility.isProSkin ? 0.32f : 0.24f),
                    7,
                    1),
                RoadGeneratorOfficialThemeResources.GetRoundedRectTexture(
                    new Color(visualTheme.AccentColor.r, visualTheme.AccentColor.g, visualTheme.AccentColor.b, EditorGUIUtility.isProSkin ? 0.22f : 0.16f),
                    new Color(visualTheme.AccentColor.r, visualTheme.AccentColor.g, visualTheme.AccentColor.b, EditorGUIUtility.isProSkin ? 0.40f : 0.28f),
                    7,
                    1),
                RoadGeneratorOfficialThemeResources.GetRoundedRectTexture(
                    new Color(visualTheme.AccentColor.r, visualTheme.AccentColor.g, visualTheme.AccentColor.b, EditorGUIUtility.isProSkin ? 0.28f : 0.20f),
                    new Color(visualTheme.AccentColor.r, visualTheme.AccentColor.g, visualTheme.AccentColor.b, EditorGUIUtility.isProSkin ? 0.48f : 0.34f),
                    7,
                    1));

            officialWindowSectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                clipping = TextClipping.Clip,
                normal = { textColor = visualTheme.PrimaryTextColor }
            };

            officialWindowSubtitleStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                fontSize = 10,
                wordWrap = true,
                normal = { textColor = visualTheme.SecondaryTextColor }
            };

            officialWindowNavLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal = { textColor = visualTheme.PrimaryTextColor }
            };

            officialWindowNavFallbackStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = visualTheme.PrimaryTextColor }
            };

            officialWindowButtonStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 11,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(8, 8, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                border = new RectOffset(8, 8, 8, 8),
                normal = { textColor = visualTheme.PrimaryTextColor },
                hover = { textColor = visualTheme.PrimaryTextColor },
                active = { textColor = visualTheme.PrimaryTextColor },
                focused = { textColor = visualTheme.PrimaryTextColor }
            };
            ApplyBackgroundToStyleState(
                officialWindowButtonStyle,
                RoadGeneratorOfficialThemeResources.GetRoundedRectTexture(visualTheme.SecondaryButtonFillColor, visualTheme.SurfaceBorderColor, 8, 1),
                RoadGeneratorOfficialThemeResources.GetRoundedRectTexture(visualTheme.PrimaryButtonHoverFillColor, visualTheme.SurfaceBorderColor, 8, 1),
                RoadGeneratorOfficialThemeResources.GetRoundedRectTexture(visualTheme.PrimaryButtonPressedFillColor, visualTheme.SurfaceBorderColor, 8, 1));

            officialWindowCloseButtonStyle = new GUIStyle(EditorStyles.toolbarButton)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                fixedWidth = 26f,
                fixedHeight = 20f,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                border = new RectOffset(7, 7, 7, 7),
                alignment = TextAnchor.MiddleCenter,
                contentOffset = new Vector2(0f, -1f),
                normal = { textColor = visualTheme.SecondaryTextColor },
                hover = { textColor = visualTheme.PrimaryTextColor },
                active = { textColor = visualTheme.PrimaryTextColor },
                focused = { textColor = visualTheme.PrimaryTextColor }
            };
            Color closeHoverColor = EditorGUIUtility.isProSkin
                ? new Color(0.86f, 0.24f, 0.24f, 0.55f)
                : new Color(0.86f, 0.24f, 0.24f, 0.18f);
            Color closeActiveColor = EditorGUIUtility.isProSkin
                ? new Color(0.86f, 0.24f, 0.24f, 0.72f)
                : new Color(0.86f, 0.24f, 0.24f, 0.28f);
            ApplyBackgroundToStyleState(
                officialWindowCloseButtonStyle,
                RoadGeneratorOfficialThemeResources.GetRoundedRectTexture(new Color(1f, 1f, 1f, EditorGUIUtility.isProSkin ? 0.03f : 0.05f), visualTheme.SurfaceBorderColor, 7, 1),
                RoadGeneratorOfficialThemeResources.GetRoundedRectTexture(closeHoverColor, closeHoverColor, 7, 1),
                RoadGeneratorOfficialThemeResources.GetRoundedRectTexture(closeActiveColor, closeActiveColor, 7, 1));
        }

        private static GUIStyle GetOfficialWindowRoundedStyle(Color fillColor, Color borderColor, int radius)
        {
            string key = $"{ColorUtility.ToHtmlStringRGBA(fillColor)}|{ColorUtility.ToHtmlStringRGBA(borderColor)}|{radius}";
            if (officialWindowRoundedStyleCache.TryGetValue(key, out GUIStyle cachedStyle) && cachedStyle != null)
            {
                return cachedStyle;
            }

            GUIStyle style = CreateRoundedWindowStyle(
                RoadGeneratorOfficialThemeResources.GetRoundedRectTexture(fillColor, borderColor, radius, 1),
                radius);
            officialWindowRoundedStyleCache[key] = style;
            return style;
        }

        private static GUIStyle CreateRoundedWindowStyle(Texture2D backgroundTexture, int radius)
        {
            GUIStyle style = new GUIStyle(officialWindowPanelStyle ?? GUIStyle.none)
            {
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                border = new RectOffset(radius + 1, radius + 1, radius + 1, radius + 1),
                overflow = new RectOffset(0, 0, 0, 0)
            };

            style.normal.background = backgroundTexture;
            style.hover.background = backgroundTexture;
            style.active.background = backgroundTexture;
            style.focused.background = backgroundTexture;
            style.onNormal.background = backgroundTexture;
            style.onHover.background = backgroundTexture;
            style.onActive.background = backgroundTexture;
            style.onFocused.background = backgroundTexture;
            return style;
        }

        private static void ApplyBackgroundToStyleState(GUIStyle style, Texture2D normalTexture, Texture2D hoverTexture, Texture2D activeTexture)
        {
            style.normal.background = normalTexture;
            style.hover.background = hoverTexture;
            style.active.background = activeTexture;
            style.focused.background = hoverTexture;
            style.onNormal.background = normalTexture;
            style.onHover.background = hoverTexture;
            style.onActive.background = activeTexture;
            style.onFocused.background = hoverTexture;
        }

        private static string GetOfficialContentTitle(OfficialTab tab)
        {
            switch (tab)
            {
                case OfficialTab.Road:
                    return "Road Workspace";
                case OfficialTab.Junction:
                    return "Junction Workspace";
                case OfficialTab.Segment:
                    return "Segment Workspace";
                case OfficialTab.Tools:
                    return "Tools Workspace";
                default:
                    return "Debug Workspace";
            }
        }

        private static string GetOfficialContentSubtitle(OfficialUiSnapshot snapshot)
        {
            switch (snapshot.ActiveTab)
            {
                case OfficialTab.Road:
                    return $"当前道路类型：{snapshot.RoadTypeLabel} · 当前自定义：{snapshot.CustomRoadLabel}";
                case OfficialTab.Junction:
                    return $"连接点：{snapshot.SelectedConnectionCount} · 预览状态：{snapshot.PreviewState}";
                case OfficialTab.Segment:
                    return "车道 Gizmo、批量更新和路段级调试入口。";
                case OfficialTab.Tools:
                    return "生态区域、大世界生成和地形贴合工具入口。";
                default:
                    return $"实时采样：{(snapshot.AutoSamplePoints ? "On" : "Off")} · 自动预览：{(snapshot.AutoPreview ? "On" : "Off")}";
            }
        }

        private static string Ellipsize(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
            {
                return value;
            }

            return $"{value.Substring(0, Mathf.Max(1, maxLength - 1))}…";
        }
    }
}
