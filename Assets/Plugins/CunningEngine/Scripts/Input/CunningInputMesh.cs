using UnityEngine;

namespace CunningEngine {
    // Component: proxy for CunningMesh handle (optional, for unified Input interface)
    [ExecuteAlways]
    public sealed class CunningInputMesh : MonoBehaviour, ICunningInputHandle {
        public ulong CurrentHandle {
            get {
                if (!this) return 0;
                var mesh = GetComponent<CunningMesh>();
                return mesh ? mesh.CurrentHandle : 0;
            }
        }
        public static CunningInputMesh GetOrAdd(GameObject go) => go ? go.GetComponent<CunningInputMesh>() ?? go.AddComponent<CunningInputMesh>() : null;
    }

    // Resolver: auto-detects CunningMesh and attaches CunningInputMesh
    sealed class CunningMeshInputResolver : ICunningInputResolver {
        public int Priority => 0;
        public bool CanResolve(Object obj) { var go = obj as GameObject ?? (obj as Component)?.gameObject; return go && go.GetComponent<CunningMesh>(); }
        public MonoBehaviour Resolve(Object obj) { var go = obj as GameObject ?? (obj as Component)?.gameObject; return go ? CunningInputMesh.GetOrAdd(go) : null; }
    }
}
