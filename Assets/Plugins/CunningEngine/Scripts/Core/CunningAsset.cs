using UnityEngine;
using System.IO;

namespace CunningEngine {
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class CunningAsset : MonoBehaviour {
        [Tooltip("Path relative to StreamingAssets, or absolute path")]
        public string cgraphPath; 
        
        [Header("Runtime")]
        public bool loadOnStart = true;
        
        private CunningMesh meshComponent;

        void Start() {
            meshComponent = GetComponent<CunningMesh>();
            if (meshComponent == null) meshComponent = gameObject.AddComponent<CunningMesh>();

            if (loadOnStart) {
                LoadGraph();
                Cook();
            }
        }

        [ContextMenu("Load Graph")]
        public void LoadGraph() {
            if (string.IsNullOrEmpty(cgraphPath)) return;

            string fullPath = Path.Combine(Application.streamingAssetsPath, cgraphPath);
            if (!File.Exists(fullPath)) {
                fullPath = cgraphPath; // Try as absolute
            }

            if (File.Exists(fullPath)) {
                try {
                    NativeMethods.cunning_load_graph_asset(fullPath);
                    Debug.Log($"Loaded CGraph: {fullPath}");
                } catch (System.Exception e) {
                    Debug.LogError($"Failed to load graph: {e.Message}");
                }
            } else {
                Debug.LogError($"CGraph file not found: {fullPath}");
            }
        }

        [ContextMenu("Cook Now")]
        public void Cook() {
            try {
                NativeMethods.cunning_cook_async();
                // For MVP, cook is synchronous in FFI for now
                ulong handle = NativeMethods.cunning_get_output_geo();
                
                if (meshComponent == null) meshComponent = GetComponent<CunningMesh>();
                meshComponent.LoadFromHandle(handle);
                
                Debug.Log("Cooked and Mesh Updated.");
            } catch (System.Exception e) {
                Debug.LogError($"Cook failed: {e.Message}");
            }
        }
    }
}
