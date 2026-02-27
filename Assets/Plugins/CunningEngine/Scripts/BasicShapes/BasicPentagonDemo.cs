using UnityEngine;

namespace CunningEngine.Demos {
    [RequireComponent(typeof(CunningEngine.CunningMesh))]
    public class BasicPentagonDemo : MonoBehaviour {
        public float extrude = 2f;
        void Start() {
            CunningEngine.CunningBridge.Instance.Init();
            using var geo = new CunningEngine.CunningGeometry();
            var p0 = geo.AddPoint(new Vector3(0, 0, 0));
            var p1 = geo.AddPoint(new Vector3(1, 0, 0));
            var p2 = geo.AddPoint(new Vector3(1.5f, 1, 0));
            var p3 = geo.AddPoint(new Vector3(0.5f, 1.5f, 0));
            var p4 = geo.AddPoint(new Vector3(-0.5f, 1, 0));
            geo.AddPoly(p0, p1, p2, p3, p4);
            using var outGeo = geo.PolyExtrude(extrude);
            GetComponent<CunningEngine.CunningMesh>().LoadFromHandle(outGeo.Handle);
        }
    }
}

