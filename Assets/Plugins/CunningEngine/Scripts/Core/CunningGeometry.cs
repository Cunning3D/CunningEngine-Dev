using System;
using UnityEngine;

namespace CunningEngine {
    public sealed class CunningGeometry : IDisposable {
        public ulong Handle { get; private set; }
        public CunningGeometry() { Handle = NativeMethods.cunning_geo_create(); }
        internal CunningGeometry(ulong h) { Handle = h; }
        public ulong AddPoint(Vector3 p) => NativeMethods.cunning_geo_add_point(Handle, p.x, p.y, p.z);
        public void AddPoly(params ulong[] pointIds) { if (pointIds != null && pointIds.Length >= 3) NativeMethods.cunning_geo_add_poly(Handle, pointIds, (uint)pointIds.Length); }
        public CunningGeometry PolyExtrude(float distance, float inset = 0f) => new CunningGeometry(NativeMethods.cunning_op_poly_extrude(Handle, distance, inset));
        public void Dispose() { if (Handle != 0) { NativeMethods.cunning_release_handle(Handle); Handle = 0; } }
        ~CunningGeometry() { Dispose(); }
    }
}

