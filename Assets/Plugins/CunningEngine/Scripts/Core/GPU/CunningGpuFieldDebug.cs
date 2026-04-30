using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CunningEngine {
    public sealed class CunningGpuFieldSnapshot {
        public NativeMethods.CunningGpuFieldInfo info;
        public int layerIndex;
        public float[] values = Array.Empty<float>();
        public float min;
        public float max;
        public double mean;
        public ulong sampleHash;
    }

    public sealed class CunningGpuFieldParityReport {
        public bool matched;
        public string summary;
        public float maxAbsDiff;
        public double meanAbsDiff;
        public uint hostWidth;
        public uint hostHeight;
        public uint referenceWidth;
        public uint referenceHeight;
        public ulong hostHash;
        public ulong referenceHash;
    }

    public static class CunningGpuFieldDebug {
        public static bool TryReadHeightLayer(CunningRuntimeContext runtime, ulong value, out CunningGpuFieldSnapshot snapshot, out string error) {
            snapshot = null;
            error = "";
            if (runtime == null || runtime.Handle == 0 || value == 0) {
                error = "invalid runtime or value";
                return false;
            }
            runtime.PumpHostGpu();
            var status = NativeMethods.cunning_value_gpu_field_get_info(runtime.Handle, value, out var info);
            if (status != NativeMethods.CunningStatus.Ok) {
                error = "gpu field info failed: " + status + " " + runtime.LastError();
                return false;
            }
            var layer = FindHeightLayer(runtime, value, info.layer_count);
            if (layer < 0) {
                error = "gpu field has no R32Float height layer";
                return false;
            }
            if (!ReadLayer(runtime, value, info, (uint)layer, out var values, out error)) return false;
            snapshot = new CunningGpuFieldSnapshot {
                info = info,
                layerIndex = layer,
                values = values,
            };
            ComputeStats(snapshot);
            return true;
        }

        public static CunningGpuFieldParityReport Compare(CunningGpuFieldSnapshot host, CunningGpuFieldSnapshot reference, float epsilon) {
            var report = new CunningGpuFieldParityReport {
                matched = false,
                summary = "missing snapshot",
            };
            if (host == null || reference == null) return report;
            report.hostWidth = host.info.dim_x;
            report.hostHeight = host.info.dim_y;
            report.referenceWidth = reference.info.dim_x;
            report.referenceHeight = reference.info.dim_y;
            report.hostHash = host.sampleHash;
            report.referenceHash = reference.sampleHash;
            if (host.info.dim_x != reference.info.dim_x || host.info.dim_y != reference.info.dim_y || host.values.Length != reference.values.Length) {
                report.summary = "dimension mismatch host=" + host.info.dim_x + "x" + host.info.dim_y + " ref=" + reference.info.dim_x + "x" + reference.info.dim_y;
                return report;
            }
            var maxAbs = 0f;
            double sumAbs = 0.0;
            for (int i = 0; i < host.values.Length; i++) {
                var d = Mathf.Abs(host.values[i] - reference.values[i]);
                if (d > maxAbs) maxAbs = d;
                sumAbs += d;
            }
            report.maxAbsDiff = maxAbs;
            report.meanAbsDiff = host.values.Length == 0 ? 0.0 : sumAbs / host.values.Length;
            var minDiff = Mathf.Max(Mathf.Abs(host.min - reference.min), Mathf.Abs(host.max - reference.max));
            report.matched = maxAbs <= epsilon && minDiff <= epsilon;
            report.summary = (report.matched ? "PASS" : "FAIL")
                + " dim=" + host.info.dim_x + "x" + host.info.dim_y
                + " max_abs=" + maxAbs.ToString("0.######")
                + " mean_abs=" + report.meanAbsDiff.ToString("0.######")
                + " host_minmax=" + host.min.ToString("0.######") + "/" + host.max.ToString("0.######")
                + " ref_minmax=" + reference.min.ToString("0.######") + "/" + reference.max.ToString("0.######")
                + " host_hash=0x" + host.sampleHash.ToString("x16")
                + " ref_hash=0x" + reference.sampleHash.ToString("x16");
            return report;
        }

        static int FindHeightLayer(CunningRuntimeContext runtime, ulong value, uint layerCount) {
            for (uint i = 0; i < layerCount; i++) {
                if (NativeMethods.cunning_value_gpu_field_get_layer_info(runtime.Handle, value, i, out var layer) != NativeMethods.CunningStatus.Ok) continue;
                if (layer.semantic == (uint)NativeMethods.CunningGpuFieldLayerSemantic.Height && layer.format == (uint)NativeMethods.CunningGpuFieldChannelFormat.R32Float) return (int)i;
            }
            return -1;
        }

        static bool ReadLayer(CunningRuntimeContext runtime, ulong value, NativeMethods.CunningGpuFieldInfo info, uint layer, out float[] values, out string error) {
            values = Array.Empty<float>();
            error = "";
            uint required = 0;
            var requiredHandle = GCHandle.Alloc(new[] { required }, GCHandleType.Pinned);
            var query = new NativeMethods.CunningGpuFieldLayerReadbackDesc {
                layer_index = layer,
                origin_x = 0,
                origin_y = 0,
                origin_z = 0,
                extent_x = info.dim_x,
                extent_y = info.dim_y,
                extent_z = info.dim_z == 0 ? 1u : info.dim_z,
                out_f32 = IntPtr.Zero,
                out_count = 0,
                out_required_count = requiredHandle.AddrOfPinnedObject(),
            };
            try {
                var status = NativeMethods.cunning_value_materialize_gpu_field_layer_f32(runtime.Handle, value, ref query);
                runtime.PumpHostGpu();
                required = ((uint[])requiredHandle.Target)[0];
                if (status != NativeMethods.CunningStatus.Ok || required == 0) {
                    error = "height query failed: " + status + " " + runtime.LastError();
                    return false;
                }
            } finally {
                requiredHandle.Free();
            }

            values = new float[required];
            var valuesHandle = GCHandle.Alloc(values, GCHandleType.Pinned);
            requiredHandle = GCHandle.Alloc(new[] { required }, GCHandleType.Pinned);
            try {
                query.out_f32 = valuesHandle.AddrOfPinnedObject();
                query.out_count = required;
                query.out_required_count = requiredHandle.AddrOfPinnedObject();
                var status = NativeMethods.cunning_value_materialize_gpu_field_layer_f32(runtime.Handle, value, ref query);
                runtime.PumpHostGpu();
                if (status != NativeMethods.CunningStatus.Ok) {
                    error = "height copy failed: " + status + " " + runtime.LastError();
                    return false;
                }
                return true;
            } finally {
                valuesHandle.Free();
                requiredHandle.Free();
            }
        }

        static void ComputeStats(CunningGpuFieldSnapshot snapshot) {
            var min = float.PositiveInfinity;
            var max = float.NegativeInfinity;
            double sum = 0.0;
            var hash = 1469598103934665603UL;
            for (int i = 0; i < snapshot.values.Length; i++) {
                var value = snapshot.values[i];
                if (float.IsNaN(value) || float.IsInfinity(value)) value = 0f;
                if (value < min) min = value;
                if (value > max) max = value;
                sum += value;
                var bits = BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);
                hash ^= bits;
                hash *= 1099511628211UL;
            }
            snapshot.min = float.IsInfinity(min) ? 0f : min;
            snapshot.max = float.IsInfinity(max) ? 0f : max;
            snapshot.mean = snapshot.values.Length == 0 ? 0.0 : sum / snapshot.values.Length;
            snapshot.sampleHash = hash;
        }
    }
}
