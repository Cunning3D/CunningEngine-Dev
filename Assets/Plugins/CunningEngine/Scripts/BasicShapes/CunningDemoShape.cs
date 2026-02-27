using UnityEngine;

namespace CunningEngine {
    [ExecuteAlways, RequireComponent(typeof(CunningMesh), typeof(MeshFilter), typeof(MeshRenderer))]
    public class CunningDemoShape : MonoBehaviour {
        public enum ShapeKind { Cube, Sphere, Pentagon }
        public ShapeKind kind = ShapeKind.Cube;
        public float cubeSize = 1f; public int cubeDiv = 1;
        public float sphereRadius = 1f; public int sphereRings = 12; public int sphereSegments = 24;
        public float pentagonExtrude = 2f;
        int lastHash;

        void OnEnable() { Rebuild(); }
        void OnValidate() { if (!Application.isPlaying) Rebuild(); }

        static int HashMix(int h, int v) { unchecked { return h * 31 + v; } }
        static int HashF(float v) { unchecked { return (int)(v * 100000f); } }

        public void Rebuild() {
            CunningBridge.Instance.Init();
            var h = 17;
            h = HashMix(h, (int)kind);
            h = HashMix(h, HashF(cubeSize));
            h = HashMix(h, cubeDiv);
            h = HashMix(h, HashF(sphereRadius));
            h = HashMix(h, sphereRings);
            h = HashMix(h, sphereSegments);
            h = HashMix(h, HashF(pentagonExtrude));
            if (h == lastHash) return; lastHash = h;
            var mesh = GetComponent<CunningMesh>(); if (mesh == null) return;
            ulong handle = 0;
            if (kind == ShapeKind.Cube) handle = NativeMethods.cunning_geo_create_cube(cubeSize, (uint)cubeDiv, (uint)cubeDiv, (uint)cubeDiv);
            else if (kind == ShapeKind.Sphere) handle = NativeMethods.cunning_geo_create_sphere(sphereRadius, (uint)sphereRings, (uint)sphereSegments);
            else {
                var baseH = NativeMethods.cunning_geo_create();
                var p0 = NativeMethods.cunning_geo_add_point(baseH, 0, 0, 0);
                var p1 = NativeMethods.cunning_geo_add_point(baseH, 1, 0, 0);
                var p2 = NativeMethods.cunning_geo_add_point(baseH, 1.5f, 1, 0);
                var p3 = NativeMethods.cunning_geo_add_point(baseH, 0.5f, 1.5f, 0);
                var p4 = NativeMethods.cunning_geo_add_point(baseH, -0.5f, 1, 0);
                NativeMethods.cunning_geo_add_poly(baseH, new ulong[] { p0, p1, p2, p3, p4 }, 5);
                handle = NativeMethods.cunning_op_poly_extrude(baseH, pentagonExtrude, 0f);
                NativeMethods.cunning_release_handle(baseH);
            }
            mesh.LoadFromHandle(handle);
        }
    }
}

