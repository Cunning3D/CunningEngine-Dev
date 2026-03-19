using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityEditor.Splines.Extension
{
    internal static class RoadGeneratorSvgIconService
    {
        static RoadGeneratorSvgIconService()
        {
            iconsNeedRefresh = true;
            EditorApplication.delayCall += RefreshGeneratedLayersIfNeeded;
            EditorApplication.projectChanged += OnProjectChanged;
        }

        internal readonly struct IconPresentation
        {
            public readonly VectorImage PrimaryVector;
            public readonly VectorImage AccentVector;
            public readonly Texture2D PrimaryTexture;
            public readonly Texture2D AccentTexture;
            public readonly Color PrimaryTint;
            public readonly Color AccentTint;

            public IconPresentation(
                VectorImage primaryVector,
                VectorImage accentVector,
                Texture2D primaryTexture,
                Texture2D accentTexture,
                Color primaryTint,
                Color accentTint)
            {
                PrimaryVector = primaryVector;
                AccentVector = accentVector;
                PrimaryTexture = primaryTexture;
                AccentTexture = accentTexture;
                PrimaryTint = primaryTint;
                AccentTint = accentTint;
            }
        }

        private sealed class IconLayerAssets
        {
            public VectorImage PrimaryVector;
            public VectorImage AccentVector;
            public Texture2D PrimaryTexture;
            public Texture2D AccentTexture;
            public VectorImage SourceVector;
            public Texture2D SourceTexture;
            public UnityEngine.Object PrimaryAsset;
            public UnityEngine.Object AccentAsset;
            public UnityEngine.Object SourceAsset;
        }

        private const string SourceFolder = "Assets/Plugins/CunningEngine/Editor/RoadGeneratorOfficial/Icons";
        private const string GeneratedFolder = "Assets/Plugins/CunningEngine/Editor/RoadGeneratorOfficial/Icons/Generated";
        private const string PrimarySlot = "primary";
        private const string AccentSlot = "accent";

        private static readonly string[] SlotAttributeNames = { "data-cgui-slot", "data-slot", "data-icon-slot" };
        private static readonly Dictionary<string, IconLayerAssets> layerCache = new Dictionary<string, IconLayerAssets>();
        private static readonly Dictionary<string, IconPresentation> presentationCache = new Dictionary<string, IconPresentation>();
        private static bool iconsNeedRefresh;
        private static string lastSourceSignature;

        public static IconPresentation GetPresentation(string iconName, RoadGeneratorOfficialTheme theme, bool active, float dpiScale)
        {
            string key = $"{iconName}|{ColorUtility.ToHtmlStringRGBA(theme.AccentColor)}|{ColorUtility.ToHtmlStringRGBA(theme.PrimaryTextColor)}|{ColorUtility.ToHtmlStringRGBA(theme.SecondaryTextColor)}|{active}|{dpiScale:0.##}";
            if (presentationCache.TryGetValue(key, out IconPresentation cached))
            {
                return cached;
            }

            IconLayerAssets layers = LoadLayers(iconName);
            Color primaryTint = theme.GetNavPrimaryIconTint(active);
            Color accentTint = theme.GetNavAccentIconTint(active);

            VectorImage primaryVector = layers.PrimaryVector != null ? layers.PrimaryVector : layers.SourceVector;
            Texture2D primaryTexture = layers.PrimaryTexture != null ? layers.PrimaryTexture : layers.SourceTexture;
            IconPresentation presentation = new IconPresentation(
                primaryVector,
                layers.AccentVector,
                primaryTexture,
                layers.AccentTexture,
                primaryTint,
                accentTint);

            if (presentation.PrimaryTexture != null || presentation.AccentTexture != null)
            {
                presentationCache[key] = presentation;
            }

            return presentation;
        }

        public static void WarmUp()
        {
            RefreshGeneratedLayersIfNeeded();
        }

        public static void RefreshGeneratedLayersIfNeeded()
        {
            string currentSignature = BuildSourceSignature();
            if (!iconsNeedRefresh && currentSignature == lastSourceSignature)
            {
                return;
            }

            RefreshGeneratedLayers();
            lastSourceSignature = currentSignature;
            iconsNeedRefresh = false;
        }

        public static void RefreshGeneratedLayers()
        {
            Directory.CreateDirectory(ToFullPath(GeneratedFolder));

            string[] sourceFiles = Directory.Exists(ToFullPath(SourceFolder))
                ? Directory.GetFiles(ToFullPath(SourceFolder), "*.svg", SearchOption.TopDirectoryOnly)
                : new string[0];
            var expectedGeneratedAssets = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            bool changed = false;

            foreach (string sourceFile in sourceFiles)
            {
                string iconName = Path.GetFileNameWithoutExtension(sourceFile);
                expectedGeneratedAssets.Add(ToFullPath(GetGeneratedAssetPath(iconName, PrimarySlot)));
                expectedGeneratedAssets.Add(ToFullPath(GetGeneratedAssetPath(iconName, AccentSlot)));
                changed |= WriteGeneratedLayerAsset(iconName, PrimarySlot);
                changed |= WriteGeneratedLayerAsset(iconName, AccentSlot);
            }

            changed |= DeleteStaleGeneratedAssets(expectedGeneratedAssets);
            layerCache.Clear();
            presentationCache.Clear();
            lastSourceSignature = BuildSourceSignature();
            iconsNeedRefresh = false;
            if (changed)
            {
                SceneView.RepaintAll();
            }
        }

        public static void MarkGeneratedLayersDirty()
        {
            iconsNeedRefresh = true;
            layerCache.Clear();
            presentationCache.Clear();
        }

        private static IconLayerAssets LoadLayers(string iconName)
        {
            if (layerCache.TryGetValue(iconName, out IconLayerAssets cached))
            {
                RefreshLayerTextures(cached);
                return cached;
            }

            string primaryAssetPath = GetGeneratedAssetPath(iconName, PrimarySlot);
            string accentAssetPath = GetGeneratedAssetPath(iconName, AccentSlot);
            string sourceAssetPath = $"{SourceFolder}/{iconName}.svg";

            IconLayerAssets assets = new IconLayerAssets
            {
                PrimaryVector = AssetDatabase.LoadAssetAtPath<VectorImage>(primaryAssetPath),
                AccentVector = AssetDatabase.LoadAssetAtPath<VectorImage>(accentAssetPath),
                SourceVector = AssetDatabase.LoadAssetAtPath<VectorImage>(sourceAssetPath),
                PrimaryAsset = AssetDatabase.LoadMainAssetAtPath(primaryAssetPath),
                AccentAsset = AssetDatabase.LoadMainAssetAtPath(accentAssetPath),
                SourceAsset = AssetDatabase.LoadMainAssetAtPath(sourceAssetPath)
            };
            RefreshLayerTextures(assets);

            layerCache[iconName] = assets;
            return assets;
        }

        private static void RefreshLayerTextures(IconLayerAssets assets)
        {
            assets.PrimaryTexture = ResolveAssetPreviewTexture(assets.PrimaryTexture, assets.PrimaryAsset);
            assets.AccentTexture = ResolveAssetPreviewTexture(assets.AccentTexture, assets.AccentAsset);
            assets.SourceTexture = ResolveAssetPreviewTexture(assets.SourceTexture, assets.SourceAsset);
        }

        private static Texture2D ResolveAssetPreviewTexture(Texture2D currentTexture, UnityEngine.Object asset)
        {
            if (currentTexture != null || asset == null)
            {
                return currentTexture;
            }

            Texture2D preview = AssetPreview.GetAssetPreview(asset);
            if (preview != null)
            {
                return preview;
            }

            return null;
        }

        private static bool WriteGeneratedLayerAsset(string iconName, string slot)
        {
            string sourceAssetPath = $"{SourceFolder}/{iconName}.svg";
            string generatedAssetPath = GetGeneratedAssetPath(iconName, slot);
            string sourceFullPath = ToFullPath(sourceAssetPath);
            string generatedFullPath = ToFullPath(generatedAssetPath);

            if (!File.Exists(sourceFullPath))
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(generatedFullPath));

            string generatedSvg = GenerateLayerSvg(File.ReadAllText(sourceFullPath), slot);
            bool shouldWrite = !File.Exists(generatedFullPath) || File.ReadAllText(generatedFullPath) != generatedSvg;
            if (!shouldWrite)
            {
                return false;
            }

            File.WriteAllText(generatedFullPath, generatedSvg);
            AssetDatabase.ImportAsset(generatedAssetPath, ImportAssetOptions.ForceUpdate);
            return true;
        }

        private static string GenerateLayerSvg(string sourceSvg, string targetSlot)
        {
            XDocument document = XDocument.Parse(sourceSvg, LoadOptions.PreserveWhitespace);
            XElement filteredRoot = CloneFilteredElement(document.Root, null, targetSlot, true);
            if (filteredRoot == null)
            {
                filteredRoot = new XElement(document.Root.Name, document.Root.Attributes());
            }

            XDocument output = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), filteredRoot);
            return output.ToString(SaveOptions.DisableFormatting);
        }

        private static XElement CloneFilteredElement(XElement source, string inheritedSlot, string targetSlot, bool isRoot = false)
        {
            string explicitSlot = GetSlotValue(source);
            string resolvedSlot = explicitSlot ?? inheritedSlot ?? PrimarySlot;
            bool keepSelf = isRoot || SlotMatches(resolvedSlot, targetSlot);

            var childElements = source.Elements()
                .Select(child => CloneFilteredElement(child, resolvedSlot, targetSlot))
                .Where(child => child != null)
                .ToList();

            if (!keepSelf && childElements.Count == 0)
            {
                return null;
            }

            XElement clone = new XElement(source.Name);
            foreach (XAttribute attribute in source.Attributes())
            {
                if (IsSlotAttribute(attribute))
                {
                    continue;
                }

                clone.SetAttributeValue(attribute.Name, NormalizePaintAttribute(attribute));
            }

            if (keepSelf)
            {
                NormalizeElementPaint(clone);
            }

            foreach (XElement child in childElements)
            {
                clone.Add(child);
            }

            return clone;
        }

        private static bool SlotMatches(string slot, string targetSlot)
        {
            if (string.IsNullOrWhiteSpace(slot))
            {
                slot = PrimarySlot;
            }

            return slot == targetSlot;
        }

        private static string GetSlotValue(XElement element)
        {
            foreach (string slotAttribute in SlotAttributeNames)
            {
                XAttribute attribute = element.Attributes().FirstOrDefault(attr => attr.Name.LocalName == slotAttribute);
                if (attribute != null && !string.IsNullOrWhiteSpace(attribute.Value))
                {
                    return attribute.Value.Trim().ToLowerInvariant();
                }
            }

            return null;
        }

        private static bool IsSlotAttribute(XAttribute attribute)
        {
            return SlotAttributeNames.Contains(attribute.Name.LocalName);
        }

        private static string NormalizePaintAttribute(XAttribute attribute)
        {
            if (attribute.Name.LocalName == "fill" || attribute.Name.LocalName == "stroke")
            {
                return string.Equals(attribute.Value, "none", System.StringComparison.OrdinalIgnoreCase)
                    ? "none"
                    : "#ffffff";
            }

            return attribute.Value;
        }

        private static void NormalizeElementPaint(XElement element)
        {
            XAttribute fill = element.Attribute("fill");
            if (fill != null && !string.Equals(fill.Value, "none", System.StringComparison.OrdinalIgnoreCase))
            {
                fill.Value = "#ffffff";
            }

            XAttribute stroke = element.Attribute("stroke");
            if (stroke != null && !string.Equals(stroke.Value, "none", System.StringComparison.OrdinalIgnoreCase))
            {
                stroke.Value = "#ffffff";
            }
        }

        private static string GetGeneratedAssetPath(string iconName, string slot)
        {
            return $"{GeneratedFolder}/{iconName}.{slot}.svg";
        }

        private static string BuildSourceSignature()
        {
            string sourceDirectory = ToFullPath(SourceFolder);
            if (!Directory.Exists(sourceDirectory))
            {
                return string.Empty;
            }

            return string.Join("|",
                Directory.GetFiles(sourceDirectory, "*.svg", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path)
                    .Select(path =>
                    {
                        FileInfo info = new FileInfo(path);
                        return $"{Path.GetFileName(path)}:{info.LastWriteTimeUtc.Ticks}:{info.Length}";
                    }));
        }

        private static void OnProjectChanged()
        {
            iconsNeedRefresh = HasMissingGeneratedAssets() || BuildSourceSignature() != lastSourceSignature;
            if (!iconsNeedRefresh)
            {
                return;
            }

            layerCache.Clear();
            presentationCache.Clear();
        }

        private static bool HasMissingGeneratedAssets()
        {
            string sourceDirectory = ToFullPath(SourceFolder);
            if (!Directory.Exists(sourceDirectory))
            {
                return false;
            }

            foreach (string sourceFile in Directory.GetFiles(sourceDirectory, "*.svg", SearchOption.TopDirectoryOnly))
            {
                string iconName = Path.GetFileNameWithoutExtension(sourceFile);
                if (!File.Exists(ToFullPath(GetGeneratedAssetPath(iconName, PrimarySlot))) ||
                    !File.Exists(ToFullPath(GetGeneratedAssetPath(iconName, AccentSlot))))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool DeleteStaleGeneratedAssets(HashSet<string> expectedGeneratedAssets)
        {
            string generatedDirectory = ToFullPath(GeneratedFolder);
            if (!Directory.Exists(generatedDirectory))
            {
                return false;
            }

            bool deleted = false;
            foreach (string generatedFile in Directory.GetFiles(generatedDirectory, "*.svg", SearchOption.TopDirectoryOnly))
            {
                string fullPath = Path.GetFullPath(generatedFile);
                if (expectedGeneratedAssets.Contains(fullPath))
                {
                    continue;
                }

                string assetPath = ToAssetPath(fullPath);
                if (AssetDatabase.DeleteAsset(assetPath))
                {
                    deleted = true;
                    continue;
                }

                File.Delete(fullPath);
                string metaPath = $"{fullPath}.meta";
                if (File.Exists(metaPath))
                {
                    File.Delete(metaPath);
                }

                deleted = true;
            }

            return deleted;
        }

        private static string ToFullPath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string ToAssetPath(string fullPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string normalizedProjectRoot = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedFullPath = Path.GetFullPath(fullPath);
            if (!normalizedFullPath.StartsWith(normalizedProjectRoot, System.StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Replace('\\', '/');
            }

            string relativePath = normalizedFullPath.Substring(normalizedProjectRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return relativePath.Replace('\\', '/');
        }
    }
}
