using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using CunningEngine.CDA;

namespace CunningEngine {
    public readonly struct CunningValueOutputContext {
        public readonly CunningCDAHostInstance host;
        public readonly CunningRuntimeContext runtime;
        public readonly uint index;
        public readonly string outputName;
        public readonly CdaPort port;
        public readonly NativeMethods.CunningValueKind kind;
        public readonly ulong value;

        public CunningValueOutputContext(
            CunningCDAHostInstance host,
            CunningRuntimeContext runtime,
            uint index,
            string outputName,
            CdaPort port,
            NativeMethods.CunningValueKind kind,
            ulong value) {
            this.host = host;
            this.runtime = runtime;
            this.index = index;
            this.outputName = outputName;
            this.port = port;
            this.kind = kind;
            this.value = value;
        }
    }

    public interface ICunningValueOutputAdapter {
        bool CanHandle(CunningValueOutputContext context);
        void Apply(CunningValueOutputContext context);
    }

    public static class CunningValueOutputAdapterRegistry {
        static readonly List<ICunningValueOutputAdapter> Adapters = new() {
            new GeometryOutputAdapter(),
            new GpuFieldTerrainOutputAdapter(),
            new ParamOutputAdapter(),
            new DiagnosticsOutputAdapter(),
            new UnsupportedOutputAdapter(),
        };

        public static void Register(ICunningValueOutputAdapter adapter) {
            if (adapter == null) return;
            var adapterType = adapter.GetType();
            for (int i = 0; i < Adapters.Count; i++) {
                if (Adapters[i].GetType() == adapterType) return;
            }
            Adapters.Insert(Mathf.Max(0, Adapters.Count - 1), adapter);
        }

        public static void Apply(CunningValueOutputContext context) {
            for (int i = 0; i < Adapters.Count; i++) {
                if (!Adapters[i].CanHandle(context)) continue;
                Adapters[i].Apply(context);
                return;
            }
        }
    }

    sealed class GeometryOutputAdapter : ICunningValueOutputAdapter {
        public bool CanHandle(CunningValueOutputContext context) =>
            context.kind == NativeMethods.CunningValueKind.Geometry ||
            context.kind == NativeMethods.CunningValueKind.GpuGeometry;

        public void Apply(CunningValueOutputContext context) {
            var slot = context.host.PrepareSlot(context.index, context.outputName, context.kind);
            var handle = MaterializeGeometry(context);
            if (handle == 0) {
                context.host.RemoveSlot(context.index);
                context.host.EnsureUnsupportedSlot(context.index, context.outputName, (uint)context.kind, "geometry materialize failed: " + context.runtime.LastError());
                context.runtime.ReleaseValueHandle(context.value);
                return;
            }

            if (slot.value != 0) context.runtime.ReleaseValueHandle(slot.value);
            if (slot.geometryHandle != 0 && slot.geometryHandle != handle) NativeMethods.cunning_release_handle(slot.geometryHandle);
            slot.value = context.value;
            slot.geometryHandle = handle;

            if (slot.child.GetComponent<MeshRenderer>() == null) slot.child.AddComponent<MeshRenderer>();
            var mesh = slot.child.GetComponent<CunningMesh>() ?? slot.child.AddComponent<CunningMesh>();
            mesh.ownsHandle = false;
            mesh.LoadFromHandle(handle);

            var instancer = slot.child.GetComponent<CunningInstancer>() ?? slot.child.AddComponent<CunningInstancer>();
            instancer.enabled = context.host.enableInstancing;
            instancer.LoadFromHandle(handle);
            context.host.ReportOutputStatus(context.index, "geometry applied, handle " + handle);
        }

        static ulong MaterializeGeometry(CunningValueOutputContext context) {
            ulong handle = 0;
            var pinned = GCHandle.Alloc(new[] { handle }, GCHandleType.Pinned);
            try {
                var desc = new NativeMethods.CunningMaterializeHostDesc {
                    kind = NativeMethods.CunningMaterializeHostKind.GeometryHandle,
                    out_handle = pinned.AddrOfPinnedObject(),
                    out_buf = IntPtr.Zero,
                    cap = 0,
                    out_len = IntPtr.Zero,
                };
                var status = NativeMethods.cunning_value_materialize_host(context.runtime.Handle, context.value, ref desc);
                return status == NativeMethods.CunningStatus.Ok ? ((ulong[])pinned.Target)[0] : 0;
            } finally {
                pinned.Free();
            }
        }
    }

    sealed class GpuFieldTerrainOutputAdapter : ICunningValueOutputAdapter {
        public bool CanHandle(CunningValueOutputContext context) => context.kind == NativeMethods.CunningValueKind.GpuField;

        public void Apply(CunningValueOutputContext context) {
            context.runtime.PumpHostGpu();
            var slot = context.host.PrepareSlot(context.index, context.outputName, NativeMethods.CunningValueKind.GpuField);
            if (!TryApplyGpuFieldToTerrain(context, slot, out var error)) {
                context.host.RemoveSlot(context.index);
                context.host.EnsureUnsupportedSlot(context.index, context.outputName, (uint)NativeMethods.CunningValueKind.GpuField, error);
                context.runtime.ReleaseValueHandle(context.value);
                return;
            }

            if (slot.value != 0) context.runtime.ReleaseValueHandle(slot.value);
            slot.value = context.value;
            context.host.ReportOutputStatus(context.index, "gpu field terrain applied");
        }

        static bool TryApplyGpuFieldToTerrain(CunningValueOutputContext context, CunningCDAHostInstance.OutputSlot slot, out string error) {
            error = "";
            var status = NativeMethods.cunning_value_gpu_field_get_info(context.runtime.Handle, context.value, out var info);
            if (status != NativeMethods.CunningStatus.Ok) {
                error = "gpu field info failed: " + status + " " + context.runtime.LastError();
                return false;
            }

            var layer = FindHeightLayer(context, info.layer_count);
            if (layer < 0) {
                error = "gpu field has no R32Float height layer";
                return false;
            }

            if (!CunningGpuFieldDebug.TryReadHeightLayer(context.runtime, context.value, out var snapshot, out error)) return false;
            context.host.WriteTerrainHeights(slot, info, snapshot.values);
            return true;
        }

        static int FindHeightLayer(CunningValueOutputContext context, uint layerCount) {
            for (uint i = 0; i < layerCount; i++) {
                if (NativeMethods.cunning_value_gpu_field_get_layer_info(context.runtime.Handle, context.value, i, out var layer) != NativeMethods.CunningStatus.Ok) continue;
                if (layer.semantic == (uint)NativeMethods.CunningGpuFieldLayerSemantic.Height && layer.format == (uint)NativeMethods.CunningGpuFieldChannelFormat.R32Float) return (int)i;
            }
            return -1;
        }

    }

    sealed class ParamOutputAdapter : ICunningValueOutputAdapter {
        public bool CanHandle(CunningValueOutputContext context) => context.kind == NativeMethods.CunningValueKind.Param;

        public void Apply(CunningValueOutputContext context) {
            var json = JsonOutputMaterializer.Materialize(context);
            var slot = context.host.PrepareSlot(context.index, context.outputName, NativeMethods.CunningValueKind.Param);
            if (slot.value != 0) context.runtime.ReleaseValueHandle(slot.value);
            slot.value = context.value;
            var output = slot.child.GetComponent<CunningParamOutput>() ?? slot.child.AddComponent<CunningParamOutput>();
            output.Apply(json);
            context.host.ReportOutputStatus(context.index, "param applied");
        }
    }

    sealed class DiagnosticsOutputAdapter : ICunningValueOutputAdapter {
        public bool CanHandle(CunningValueOutputContext context) => context.kind == NativeMethods.CunningValueKind.Diagnostics;

        public void Apply(CunningValueOutputContext context) {
            var json = JsonOutputMaterializer.Materialize(context);
            var slot = context.host.PrepareSlot(context.index, context.outputName, NativeMethods.CunningValueKind.Diagnostics);
            if (slot.value != 0) context.runtime.ReleaseValueHandle(slot.value);
            slot.value = context.value;
            var output = slot.child.GetComponent<CunningDiagnosticsOutput>() ?? slot.child.AddComponent<CunningDiagnosticsOutput>();
            output.Apply(json);
            context.host.ReportOutputStatus(context.index, "diagnostics applied");
        }
    }

    sealed class UnsupportedOutputAdapter : ICunningValueOutputAdapter {
        public bool CanHandle(CunningValueOutputContext context) => true;

        public void Apply(CunningValueOutputContext context) {
            context.host.EnsureUnsupportedSlot(context.index, context.outputName, (uint)context.kind, "unsupported output kind");
            context.runtime.ReleaseValueHandle(context.value);
            context.host.ReportOutputStatus(context.index, "unsupported output kind " + context.kind);
        }
    }

    static class JsonOutputMaterializer {
        public static string Materialize(CunningValueOutputContext context) {
            uint len = 0;
            var lenHandle = GCHandle.Alloc(new[] { len }, GCHandleType.Pinned);
            try {
                var query = new NativeMethods.CunningMaterializeHostDesc {
                    kind = NativeMethods.CunningMaterializeHostKind.Utf8Json,
                    out_handle = IntPtr.Zero,
                    out_buf = IntPtr.Zero,
                    cap = 0,
                    out_len = lenHandle.AddrOfPinnedObject(),
                };
                var status = NativeMethods.cunning_value_materialize_host(context.runtime.Handle, context.value, ref query);
                len = ((uint[])lenHandle.Target)[0];
                if (status != NativeMethods.CunningStatus.Ok) return "";
            } finally {
                lenHandle.Free();
            }

            var bytes = new byte[len + 1];
            var bytesHandle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            lenHandle = GCHandle.Alloc(new[] { len }, GCHandleType.Pinned);
            try {
                var copy = new NativeMethods.CunningMaterializeHostDesc {
                    kind = NativeMethods.CunningMaterializeHostKind.Utf8Json,
                    out_handle = IntPtr.Zero,
                    out_buf = bytesHandle.AddrOfPinnedObject(),
                    cap = (uint)bytes.Length,
                    out_len = lenHandle.AddrOfPinnedObject(),
                };
                var status = NativeMethods.cunning_value_materialize_host(context.runtime.Handle, context.value, ref copy);
                if (status != NativeMethods.CunningStatus.Ok) return "";
                var end = Array.IndexOf(bytes, (byte)0);
                if (end < 0) end = bytes.Length;
                return Encoding.UTF8.GetString(bytes, 0, end);
            } finally {
                bytesHandle.Free();
                lenHandle.Free();
            }
        }
    }
}
