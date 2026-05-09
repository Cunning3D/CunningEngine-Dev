using System.Collections.Generic;
using UnityEngine;

namespace CunningEngine {
    // Interface for input resolvers (priority-based, higher wins)
    public interface ICunningInputResolver { int Priority { get; } bool CanResolve(Object obj); MonoBehaviour Resolve(Object obj); }

    // Central registry for input resolvers
    public static class CunningInputUtility {
        static readonly List<ICunningInputResolver> R = new();
        static bool _initialized;

        public static void Ensure() {
            if (_initialized) return;
            _initialized = true;
            Register(new PolylineInputResolver());
            Register(new HeightmapTextureInputResolver());
            Register(new TerrainInputResolver());
            Register(new CunningMeshInputResolver());
            Register(new UnityMeshInputResolver());
            Register(new SplineInputResolver());
        }

        public static void Register(ICunningInputResolver r) {
            if (r == null || R.Contains(r)) return;
            R.Add(r);
            R.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        public static MonoBehaviour Resolve(Object obj) {
            Ensure();
            if (obj is MonoBehaviour mb && (mb is ICunningInputHandle || mb is ICunningInputValueHandle)) return mb;
            foreach (var r in R) if (r.CanResolve(obj)) return r.Resolve(obj);
            return null;
        }

        public static string GetSupportedTypesHint() => "Supported: Terrain, GameObject with Terrain, ICunningPolylineSource, CunningMesh, MeshFilter, SkinnedMeshRenderer, or SplineContainer.";
    }
}
