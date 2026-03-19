using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityEditor.Splines.Extension
{
    internal readonly struct RoadGeneratorOfficialTheme
    {
        public readonly Color AccentColor;
        public readonly Color RootBackgroundColor;
        public readonly Color SurfaceBackgroundColor;
        public readonly Color SurfaceBorderColor;
        public readonly Color CardBackgroundColor;
        public readonly Color CardBorderColor;
        public readonly Color PrimaryTextColor;
        public readonly Color SecondaryTextColor;
        public readonly Color MutedTextColor;
        public readonly Color DividerColor;
        public readonly Color NavIdleFillColor;
        public readonly Color NavHoverFillColor;
        public readonly Color NavPressedFillColor;
        public readonly Color NavActiveFillColor;
        public readonly Color NavIdleBorderColor;
        public readonly Color NavActiveBorderColor;
        public readonly Color ChipFillColor;
        public readonly Color ChipBorderColor;
        public readonly Color EmptyStateFillColor;
        public readonly Color EmptyStateBorderColor;
        public readonly Color PrimaryButtonFillColor;
        public readonly Color PrimaryButtonHoverFillColor;
        public readonly Color PrimaryButtonPressedFillColor;
        public readonly Color SecondaryButtonFillColor;
        public readonly Color DisabledFillColor;
        public readonly Color DisabledBorderColor;
        public readonly float RootPadding;
        public readonly float CardSpacing;
        public readonly float CardPadding;
        public readonly float SmallRadius;
        public readonly float MediumRadius;
        public readonly float LargeRadius;
        public readonly float NavButtonHeight;
        public readonly float NavActiveWidth;
        public readonly float NavInactiveWidth;
        public readonly float IconSize;

        public RoadGeneratorOfficialTheme(
            Color accentColor,
            Color rootBackgroundColor,
            Color surfaceBackgroundColor,
            Color surfaceBorderColor,
            Color cardBackgroundColor,
            Color cardBorderColor,
            Color primaryTextColor,
            Color secondaryTextColor,
            Color mutedTextColor,
            Color dividerColor,
            Color navIdleFillColor,
            Color navHoverFillColor,
            Color navPressedFillColor,
            Color navActiveFillColor,
            Color navIdleBorderColor,
            Color navActiveBorderColor,
            Color chipFillColor,
            Color chipBorderColor,
            Color emptyStateFillColor,
            Color emptyStateBorderColor,
            Color primaryButtonFillColor,
            Color primaryButtonHoverFillColor,
            Color primaryButtonPressedFillColor,
            Color secondaryButtonFillColor,
            Color disabledFillColor,
            Color disabledBorderColor,
            float rootPadding,
            float cardSpacing,
            float cardPadding,
            float smallRadius,
            float mediumRadius,
            float largeRadius,
            float navButtonHeight,
            float navActiveWidth,
            float navInactiveWidth,
            float iconSize)
        {
            AccentColor = accentColor;
            RootBackgroundColor = rootBackgroundColor;
            SurfaceBackgroundColor = surfaceBackgroundColor;
            SurfaceBorderColor = surfaceBorderColor;
            CardBackgroundColor = cardBackgroundColor;
            CardBorderColor = cardBorderColor;
            PrimaryTextColor = primaryTextColor;
            SecondaryTextColor = secondaryTextColor;
            MutedTextColor = mutedTextColor;
            DividerColor = dividerColor;
            NavIdleFillColor = navIdleFillColor;
            NavHoverFillColor = navHoverFillColor;
            NavPressedFillColor = navPressedFillColor;
            NavActiveFillColor = navActiveFillColor;
            NavIdleBorderColor = navIdleBorderColor;
            NavActiveBorderColor = navActiveBorderColor;
            ChipFillColor = chipFillColor;
            ChipBorderColor = chipBorderColor;
            EmptyStateFillColor = emptyStateFillColor;
            EmptyStateBorderColor = emptyStateBorderColor;
            PrimaryButtonFillColor = primaryButtonFillColor;
            PrimaryButtonHoverFillColor = primaryButtonHoverFillColor;
            PrimaryButtonPressedFillColor = primaryButtonPressedFillColor;
            SecondaryButtonFillColor = secondaryButtonFillColor;
            DisabledFillColor = disabledFillColor;
            DisabledBorderColor = disabledBorderColor;
            RootPadding = rootPadding;
            CardSpacing = cardSpacing;
            CardPadding = cardPadding;
            SmallRadius = smallRadius;
            MediumRadius = mediumRadius;
            LargeRadius = largeRadius;
            NavButtonHeight = navButtonHeight;
            NavActiveWidth = navActiveWidth;
            NavInactiveWidth = navInactiveWidth;
            IconSize = iconSize;
        }

        public Color GetNavPrimaryIconTint(bool active)
        {
            return active ? PrimaryTextColor : SecondaryTextColor;
        }

        public Color GetNavAccentIconTint(bool active)
        {
            if (active)
            {
                return AccentColor;
            }

            return new Color(AccentColor.r, AccentColor.g, AccentColor.b, EditorGUIUtility.isProSkin ? 0.56f : 0.70f);
        }

        public static RoadGeneratorOfficialTheme Resolve(RoadGeneratorTool.OfficialTabTheme tabTheme)
        {
            Color accent = tabTheme.AccentColor;
            if (EditorGUIUtility.isProSkin)
            {
                return new RoadGeneratorOfficialTheme(
                    accent,
                    new Color(0.17f, 0.18f, 0.20f, 1f),
                    new Color(0.19f, 0.20f, 0.22f, 0.98f),
                    new Color(1f, 1f, 1f, 0.08f),
                    new Color(0.18f, 0.19f, 0.21f, 0.98f),
                    new Color(1f, 1f, 1f, 0.05f),
                    new Color(0.92f, 0.93f, 0.95f, 1f),
                    new Color(0.74f, 0.76f, 0.79f, 1f),
                    new Color(0.60f, 0.63f, 0.67f, 1f),
                    new Color(1f, 1f, 1f, 0.08f),
                    new Color(1f, 1f, 1f, 0.00f),
                    new Color(1f, 1f, 1f, 0.04f),
                    new Color(1f, 1f, 1f, 0.06f),
                    new Color(0.18f, 0.36f, 0.62f, 0.82f),
                    new Color(1f, 1f, 1f, 0.00f),
                    new Color(1f, 1f, 1f, 0.00f),
                    new Color(1f, 1f, 1f, 0.04f),
                    new Color(1f, 1f, 1f, 0.08f),
                    new Color(1f, 1f, 1f, 0.03f),
                    new Color(1f, 1f, 1f, 0.06f),
                    new Color(1f, 1f, 1f, 0.04f),
                    new Color(1f, 1f, 1f, 0.08f),
                    new Color(1f, 1f, 1f, 0.10f),
                    new Color(1f, 1f, 1f, 0.02f),
                    new Color(1f, 1f, 1f, 0.02f),
                    new Color(1f, 1f, 1f, 0.06f),
                    8f,
                    8f,
                    10f,
                    8f,
                    12f,
                    999f,
                    26f,
                    120f,
                    92f,
                    16f);
            }

            return new RoadGeneratorOfficialTheme(
                accent,
                new Color(0.76f, 0.78f, 0.82f, 1f),
                new Color(0.94f, 0.95f, 0.96f, 1f),
                new Color(0f, 0f, 0f, 0.10f),
                new Color(0.96f, 0.97f, 0.98f, 1f),
                new Color(0f, 0f, 0f, 0.06f),
                new Color(0.16f, 0.18f, 0.20f, 1f),
                new Color(0.34f, 0.38f, 0.42f, 1f),
                new Color(0.47f, 0.51f, 0.56f, 1f),
                new Color(0f, 0f, 0f, 0.08f),
                new Color(0f, 0f, 0f, 0.00f),
                new Color(0f, 0f, 0f, 0.04f),
                new Color(0f, 0f, 0f, 0.06f),
                new Color(0.31f, 0.52f, 0.84f, 0.82f),
                new Color(0f, 0f, 0f, 0.00f),
                new Color(0f, 0f, 0f, 0.00f),
                new Color(0f, 0f, 0f, 0.04f),
                new Color(0f, 0f, 0f, 0.10f),
                new Color(0f, 0f, 0f, 0.02f),
                new Color(0f, 0f, 0f, 0.08f),
                new Color(0f, 0f, 0f, 0.04f),
                new Color(0f, 0f, 0f, 0.08f),
                new Color(0f, 0f, 0f, 0.12f),
                new Color(0f, 0f, 0f, 0.02f),
                new Color(0f, 0f, 0f, 0.02f),
                new Color(0f, 0f, 0f, 0.08f),
                8f,
                8f,
                10f,
                8f,
                12f,
                999f,
                26f,
                120f,
                92f,
                16f);
        }
    }

    internal static class RoadGeneratorOfficialThemeResources
    {
        private static readonly Dictionary<string, Texture2D> solidTextureCache = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, Texture2D> roundedTextureCache = new Dictionary<string, Texture2D>();

        public static Texture2D GetSolidTexture(Color color)
        {
            string key = ColorUtility.ToHtmlStringRGBA(color);
            if (solidTextureCache.TryGetValue(key, out Texture2D cachedTexture) && cachedTexture != null)
            {
                return cachedTexture;
            }

            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            solidTextureCache[key] = texture;
            return texture;
        }

        public static Texture2D GetRoundedRectTexture(Color fillColor, Color borderColor, int radius, int borderThickness)
        {
            int safeRadius = Mathf.Max(2, radius);
            int safeBorderThickness = Mathf.Max(1, borderThickness);
            string key = $"{ColorUtility.ToHtmlStringRGBA(fillColor)}|{ColorUtility.ToHtmlStringRGBA(borderColor)}|{safeRadius}|{safeBorderThickness}";
            if (roundedTextureCache.TryGetValue(key, out Texture2D cachedTexture) && cachedTexture != null)
            {
                return cachedTexture;
            }

            int size = Mathf.Max(32, safeRadius * 4);
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                alphaIsTransparency = true
            };

            float width = size;
            float height = size;
            float innerWidth = Mathf.Max(1f, width - safeBorderThickness * 2f);
            float innerHeight = Mathf.Max(1f, height - safeBorderThickness * 2f);
            float innerRadius = Mathf.Max(0f, safeRadius - safeBorderThickness);
            Color clear = new Color(0f, 0f, 0f, 0f);
            float[] sampleOffsets = { 0.125f, 0.375f, 0.625f, 0.875f };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Color accumulated = clear;
                    float sampleCount = 0f;

                    for (int sy = 0; sy < sampleOffsets.Length; sy++)
                    {
                        for (int sx = 0; sx < sampleOffsets.Length; sx++)
                        {
                            float px = x + sampleOffsets[sx];
                            float py = y + sampleOffsets[sy];
                            bool insideOuter = IsInsideRoundedRect(px, py, width, height, safeRadius);
                            if (!insideOuter)
                            {
                                sampleCount += 1f;
                                continue;
                            }

                            bool insideInner = IsInsideRoundedRect(
                                px - safeBorderThickness,
                                py - safeBorderThickness,
                                innerWidth,
                                innerHeight,
                                innerRadius);

                            accumulated += insideInner ? fillColor : borderColor;
                            sampleCount += 1f;
                        }
                    }

                    texture.SetPixel(x, y, sampleCount > 0f ? accumulated / sampleCount : clear);
                }
            }

            texture.Apply();
            roundedTextureCache[key] = texture;
            return texture;
        }

        private static bool IsInsideRoundedRect(float x, float y, float width, float height, float radius)
        {
            if (width <= 0f || height <= 0f)
            {
                return false;
            }

            float safeRadius = Mathf.Min(radius, Mathf.Min(width, height) * 0.5f);
            float halfWidth = width * 0.5f;
            float halfHeight = height * 0.5f;
            float qx = Mathf.Abs(x - halfWidth) - (halfWidth - safeRadius);
            float qy = Mathf.Abs(y - halfHeight) - (halfHeight - safeRadius);
            float outerDistance = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            float signedDistance = outerDistance + Mathf.Min(Mathf.Max(qx, qy), 0f) - safeRadius;
            return signedDistance <= 0f;
        }
    }
}
