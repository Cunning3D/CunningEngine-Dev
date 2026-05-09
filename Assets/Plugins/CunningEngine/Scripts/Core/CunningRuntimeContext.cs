using System;
using System.Text;
using UnityEngine;

namespace CunningEngine {
    public sealed class CunningRuntimeContext : IDisposable {
        public ulong Handle { get; private set; }
        ulong frameIndex;
        bool frameOpen;
        CunningUnityGpuHostBackend hostGpuBackend;

        public CunningRuntimeContext(NativeMethods.CunningRuntimeBackendMode backendMode = NativeMethods.CunningRuntimeBackendMode.StandaloneOwnedDevice, CunningUnityGpuHostBackend hostGpuBackend = null) {
            NativeMethods.EnsureLoaded();
            NativeMethods.cunning_init();
            if (backendMode == NativeMethods.CunningRuntimeBackendMode.EngineHostedAdapter) {
                this.hostGpuBackend = hostGpuBackend ?? new CunningUnityGpuHostBackend();
            }
            var desc = new NativeMethods.CunningRuntimeCreateDesc {
                backend_mode = (uint)backendMode,
                host_gpu_backend = this.hostGpuBackend != null ? this.hostGpuBackend.CallbackTablePtr : IntPtr.Zero,
                cpu_jit_policy = (uint)NativeMethods.CunningCpuJitPolicy.EditorAllowed,
            };
            try {
                Handle = NativeMethods.cunning_runtime_create(ref desc);
                if (Handle == 0) throw new InvalidOperationException("cunning_runtime_create failed: " + LastError());
            } catch {
                this.hostGpuBackend?.Dispose();
                this.hostGpuBackend = null;
                throw;
            }
        }

        public void BeginFrame(bool gameRuntime, ulong cpuDeadlineNs, ulong gpuDeadlineNs, ulong gpuBudgetBytes) {
            if (Handle == 0) return;
            hostGpuBackend?.PumpMainThreadWork();
            if (frameOpen) EndFrame();
            var desc = new NativeMethods.CunningFrameDesc {
                frame_index = ++frameIndex,
                cpu_deadline_ns = cpuDeadlineNs,
                gpu_deadline_ns = gpuDeadlineNs,
                gpu_budget_bytes = gpuBudgetBytes,
                latency_lane_cpu_ns = cpuDeadlineNs,
                latency_lane_gpu_ns = gpuDeadlineNs,
                latency_lane_reserved_bytes = gpuBudgetBytes / 4,
                throughput_lane_cpu_ns = cpuDeadlineNs,
                throughput_lane_gpu_ns = gpuDeadlineNs,
                throughput_lane_budget_bytes = gpuBudgetBytes,
                background_lane_cpu_ns = cpuDeadlineNs,
                background_lane_gpu_ns = gpuDeadlineNs,
                background_lane_budget_bytes = gpuBudgetBytes,
                mode = (uint)(gameRuntime ? NativeMethods.CunningFrameMode.GameRuntime : NativeMethods.CunningFrameMode.Editor),
            };
            var status = NativeMethods.cunning_runtime_begin_frame(Handle, ref desc);
            if (status != NativeMethods.CunningStatus.Ok) throw new InvalidOperationException("cunning_runtime_begin_frame failed: " + status + " " + LastError());
            frameOpen = true;
            hostGpuBackend?.PumpMainThreadWork();
        }

        public void EndFrame() {
            if (Handle == 0 || !frameOpen) return;
            hostGpuBackend?.PumpMainThreadWork();
            NativeMethods.cunning_runtime_end_frame(Handle);
            frameOpen = false;
            hostGpuBackend?.PumpMainThreadWork();
        }

        public void PumpHostGpu() {
            hostGpuBackend?.PumpMainThreadWork();
        }

        public bool SupportsExternalGpuFieldImport => hostGpuBackend != null;

        public ulong ImportExternalGpuField(ComputeBuffer buffer, ref ulong hostFieldHandle, ref NativeMethods.CunningHostGpuImportFieldDesc desc) {
            if (Handle == 0 || hostGpuBackend == null || buffer == null) return 0;
            hostGpuBackend.PumpMainThreadWork();
            if (hostFieldHandle == 0) {
                hostFieldHandle = hostGpuBackend.RegisterExternalField(buffer, desc.dim_x, desc.dim_y, Math.Max(1u, desc.dim_z), Math.Max(1u, desc.layer_count));
            }
            if (hostFieldHandle == 0) return 0;
            desc.host_resource = hostFieldHandle;
            return NativeMethods.cunning_value_import_gpu_field(Handle, ref desc);
        }

        public ulong ImportExternalGpuField(Texture texture, ref ulong hostFieldHandle, ref NativeMethods.CunningHostGpuImportFieldDesc desc) {
            if (Handle == 0 || hostGpuBackend == null || texture == null) return 0;
            hostGpuBackend.PumpMainThreadWork();
            var dimZ = Math.Max(1u, desc.dim_z);
            var stride = Math.Max(1u, desc.layer_count);
            if (hostFieldHandle == 0) {
                hostFieldHandle = hostGpuBackend.RegisterExternalTextureField(texture, desc.dim_x, desc.dim_y, dimZ, stride);
            } else if (!hostGpuBackend.UpdateExternalTextureField(hostFieldHandle, texture, desc.dim_x, desc.dim_y, dimZ, stride)) {
                hostGpuBackend.UnregisterExternalField(hostFieldHandle);
                hostFieldHandle = hostGpuBackend.RegisterExternalTextureField(texture, desc.dim_x, desc.dim_y, dimZ, stride);
            }
            if (hostFieldHandle == 0) return 0;
            desc.host_resource = hostFieldHandle;
            return NativeMethods.cunning_value_import_gpu_field(Handle, ref desc);
        }

        public void UnregisterExternalGpuField(ref ulong hostFieldHandle) {
            if (hostFieldHandle == 0) return;
            hostGpuBackend?.UnregisterExternalField(hostFieldHandle);
            hostFieldHandle = 0;
        }

        public void ReleaseValue(ref ulong value) {
            if (Handle != 0 && value != 0) NativeMethods.cunning_value_release(Handle, value);
            value = 0;
        }

        public void ReleaseValueHandle(ulong value) {
            if (Handle != 0 && value != 0) NativeMethods.cunning_value_release(Handle, value);
        }

        public string LastError() {
            var sb = new StringBuilder(2048);
            NativeMethods.cunning_runtime_get_last_error(sb, (uint)sb.Capacity);
            return sb.ToString();
        }

        public void Dispose() {
            EndFrame();
            if (Handle != 0) {
                NativeMethods.cunning_runtime_destroy(Handle);
                Handle = 0;
            }
            hostGpuBackend?.Dispose();
            hostGpuBackend = null;
        }
    }
}
