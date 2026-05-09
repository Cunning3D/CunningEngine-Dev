using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace CunningEngine {
    public static class NativeMethods {
        // Name of the DLL. Editor/dev canonical location:
        // Assets/Plugins/CunningEngine/Scripts/CAssemblies/x86_64/cunning_core_ffi.dll
        const string DLL_NAME = "cunning_core_ffi";

        const CallingConvention CC = CallingConvention.Cdecl;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr LoadLibraryW(string lpFileName);
#endif

        static int s_loaded; // 0=unknown,1=ok,-1=failed
        static string s_loadDiag;

        static NativeMethods() {
            try { EnsureLoaded(); } catch (Exception e) { Debug.LogWarning("CunningEngine: native preload failed: " + e.Message); }
        }

        public static void EnsureLoaded() {
            if (s_loaded == 1) return;
            if (s_loaded == -1) throw new DllNotFoundException(s_loadDiag ?? (DLL_NAME + " preload failed"));
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
#if !UNITY_EDITOR
            // Player: rely on Unity's native plugin loader/search path. (Avoid hardcoding build-time paths.)
            s_loaded = 1;
            return;
#else
            var data = Application.dataPath;
            var cand = new[] {
                // Single canonical DLL location for Editor.
                Path.GetFullPath(Path.Combine(data, "Plugins", "CunningEngine", "Scripts", "CAssemblies", "x86_64", DLL_NAME + ".dll")),
            };
            var tried = new StringBuilder(512);
            foreach (var p in cand) {
                tried.AppendLine(p);
                if (!File.Exists(p)) continue;
                var h = LoadLibraryW(p);
                if (h != IntPtr.Zero) { s_loaded = 1; s_loadDiag = "OK: " + p; return; }
                var err = Marshal.GetLastWin32Error();
                s_loadDiag = $"LoadLibrary failed ({err}) for: {p}\nTried:\n{tried}";
            }
            s_loaded = -1;
            throw new DllNotFoundException($"Failed to load native DLL '{DLL_NAME}.dll'.\nTried:\n{tried}\nTip: ensure it exists under Assets/Plugins/CunningEngine/Scripts/CAssemblies/x86_64/ or fix Plugin Importer settings.");
#endif
#else
            // Non-Windows platforms: rely on Unity native plugin importer paths.
            s_loaded = 1;
#endif
        }

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern void cunning_init();

        // --- Bridge blob helpers ---
        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_bridge_put_blob(IntPtr ptr, uint len);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_bridge_get_blob_size(ulong blob_id);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_bridge_copy_blob(ulong blob_id, IntPtr out_ptr, uint out_cap);

        // --- Geometry Snapshot (Handle -> JSON blob) ---
        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_geo_snapshot_json_to_blob(ulong handle);

        // --- Geometry Snapshot (Handle -> Binary(Zstd+Bincode) blob) ---
        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_geo_snapshot_bin_zstd_to_blob(ulong handle, int zstd_level);

        // --- Atomic Modeling (Unity-side N-gon authoring) ---
        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_geo_create();

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_clear(ulong handle);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_geo_add_point(ulong handle, float x, float y, float z);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_add_points_bulk(ulong handle, float[] positions, uint count, ulong[] out_point_ids);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_add_points_bulk_nosync(ulong handle, float[] positions, uint count, ulong[] out_point_ids);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_geo_add_poly(ulong handle, ulong[] point_ids, uint count);

        [DllImport(DLL_NAME, CallingConvention = CC, EntryPoint = "cunning_geo_add_polyline")]
        public static extern ulong cunning_geo_add_polyline(ulong handle, IntPtr point_ids, uint count, uint closed);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_add_polys_bulk(ulong handle, ulong[] point_ids, uint[] point_offsets, uint prim_count);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_add_polys_bulk_nosync(ulong handle, ulong[] point_ids, uint[] point_offsets, uint prim_count);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_set_point_attr_vec2(ulong handle, string name, float[] values, uint count);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_set_point_attr_vec2_nosync(ulong handle, string name, float[] values, uint count);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_set_point_attr_vec3(ulong handle, string name, float[] values, uint count);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_set_point_attr_vec3_nosync(ulong handle, string name, float[] values, uint count);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_update_point_positions_range(ulong handle, uint start_index, float[] values, uint count);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_update_point_positions_range_nosync(ulong handle, uint start_index, float[] values, uint count);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_update_point_positions_ranges_nosync(
            ulong handle,
            IntPtr starts,
            IntPtr counts,
            uint range_count,
            IntPtr values,
            uint total_value_count);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_update_point_attr_vec2_range(ulong handle, string name, uint start_index, float[] values, uint count);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_update_point_attr_vec2_range_nosync(ulong handle, string name, uint start_index, float[] values, uint count);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_update_point_attr_vec3_range(ulong handle, string name, uint start_index, float[] values, uint count);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_update_point_attr_vec3_range_nosync(ulong handle, string name, uint start_index, float[] values, uint count);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_set_vertex_attr_vec2(ulong handle, string name, float[] values, uint count);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_set_vertex_attr_vec2_nosync(ulong handle, string name, float[] values, uint count);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_set_vertex_attr_vec3(ulong handle, string name, float[] values, uint count);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_set_vertex_attr_vec3_nosync(ulong handle, string name, float[] values, uint count);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_update_vertex_attr_vec2_range(ulong handle, string name, uint start_index, float[] values, uint count);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_update_vertex_attr_vec2_range_nosync(ulong handle, string name, uint start_index, float[] values, uint count);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_update_vertex_attr_vec2_ranges_nosync(
            ulong handle,
            string name,
            IntPtr starts,
            IntPtr counts,
            uint range_count,
            IntPtr values,
            uint total_value_count);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_update_vertex_attr_vec3_range(ulong handle, string name, uint start_index, float[] values, uint count);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_update_vertex_attr_vec3_range_nosync(ulong handle, string name, uint start_index, float[] values, uint count);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_update_vertex_attr_vec3_ranges_nosync(
            ulong handle,
            string name,
            IntPtr starts,
            IntPtr counts,
            uint range_count,
            IntPtr values,
            uint total_value_count);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_set_primitive_attr_i32(ulong handle, string name, int[] values, uint count);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_set_primitive_attr_i32_nosync(ulong handle, string name, int[] values, uint count);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_update_primitive_attr_i32_range(ulong handle, string name, uint start_index, int[] values, uint count);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_update_primitive_attr_i32_range_nosync(ulong handle, string name, uint start_index, int[] values, uint count);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_update_primitive_attr_i32_ranges_nosync(
            ulong handle,
            string name,
            IntPtr starts,
            IntPtr counts,
            uint range_count,
            IntPtr values,
            uint total_value_count);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_set_point_attr_vec4(ulong handle, string name, float[] values, uint count);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_sync_cache(ulong handle);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_geo_create_cube(float size, uint div_x, uint div_y, uint div_z);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_geo_create_sphere(float radius, uint rings, uint segments);

        // --- CDA (Async) ---
        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_cda_load(string path);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_cda_get_last_error(StringBuilder out_buf, uint cap);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_cda_get_revision(ulong cda_id);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_cda_get_definition_json_len(ulong cda_id);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_cda_get_definition_json(ulong cda_id, StringBuilder out_buf, uint cap);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_cda_set_definition_json(ulong cda_id, string definition_json);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_cda_apply_edit_commands(ulong cda_id, string commands_json);

        public enum CunningStatus : uint {
            Ok = 0,
            InvalidArgument = 1,
            InvalidContext = 2,
            InvalidGraph = 3,
            InvalidJob = 4,
            InvalidValue = 5,
            TypeMismatch = 6,
            BufferTooSmall = 7,
            Unsupported = 8,
            ParseError = 9,
            DeferredByBudget = 10,
        }

        public enum CunningJobStatus : uint {
            Invalid = 0,
            Pending = 1,
            Running = 2,
            Ready = 3,
            Cancelled = 4,
            Failed = 5,
        }

        public enum CunningRuntimeBackendMode : uint {
            HostValueRegistry = 0,
            StandaloneOwnedDevice = 1,
            EngineHostedAdapter = 2,
        }

        public enum CunningCpuJitPolicy : uint {
            Disabled = 0,
            EditorAllowed = 1,
            EditorRequired = 2,
        }

        public enum CunningFrameMode : uint {
            Editor = 0,
            GameRuntime = 1,
        }

        public enum CunningGpuExecutionModel : uint {
            StandaloneBringUp = 0,
            EngineHostedQueueInterlocked = 1,
            EngineHostedCommandRecording = 2,
        }

        public enum CunningKernelSourceKind : uint {
            Wgsl = 1,
            Hlsl = 2,
        }

        public enum CunningGpuResourceKind : uint {
            Buffer = 1,
            Field = 2,
        }

        public enum CunningGpuFieldDimension : uint {
            D1 = 0,
            D2 = 1,
            D3 = 2,
        }

        public enum CunningGpuFieldStorageMode : uint {
            Dense = 0,
            SparseTiled = 1,
        }

        public enum CunningValueKind : uint {
            Invalid = 0,
            Geometry = 1,
            Param = 2,
            GpuBuffer = 3,
            GpuField = 4,
            GpuGeometry = 5,
            Diagnostics = 6,
        }

        public enum CunningMaterializeHostKind : uint {
            GeometryHandle = 1,
            Utf8Json = 2,
        }

        public enum CunningGpuFieldLayerSemantic : uint {
            Height = 0,
            Mask = 1,
            Water = 2,
            Sediment = 3,
            Debris = 4,
            Velocity = 5,
            Temperature = 6,
            Density = 7,
            Custom = 8,
        }

        public enum CunningGpuFieldChannelFormat : uint {
            R8Uint = 0,
            R16Uint = 1,
            R16Float = 2,
            R32Uint = 3,
            R32Float = 4,
            Rg16Float = 5,
            Rg32Float = 6,
            Rgba16Float = 7,
            Rgba32Float = 8,
        }

        public enum CunningHostGpuOwnership : uint {
            BorrowReadOnly = 0,
            BorrowMutable = 1,
            ImportShared = 2,
            TransferOwnership = 3,
        }

        public enum CunningHostGpuResourceState : uint {
            Unknown = 0,
            ReadOnly = 1,
            ReadWrite = 2,
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningRuntimeCreateDesc {
            public uint backend_mode;
            public IntPtr host_gpu_backend;
            public uint cpu_jit_policy;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningGpuCapabilities {
            public uint max_workgroup_size_x;
            public uint max_workgroup_size_y;
            public uint max_workgroup_size_z;
            public uint max_bind_groups;
            public uint max_storage_buffers_per_stage;
            public uint max_shared_memory_size;
            public uint subgroup_size;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningGpuMemoryBudget {
            public ulong budget_bytes;
            public ulong available_bytes;
            public ulong dedicated_bytes;
            public ulong shared_bytes;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningGpuSyncPoint {
            public uint queue_class;
            public ulong timeline_value;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningKernelBindingDesc {
            public uint binding;
            public uint read_only;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningKernelModuleDesc {
            public IntPtr label;
            public IntPtr entry_point;
            public uint source_kind;
            public IntPtr source_utf8;
            public IntPtr bindings;
            public uint binding_count;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningGpuBufferDesc {
            public IntPtr label;
            public ulong size_bytes;
            public uint stride_bytes;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningGpuFieldLayerDesc {
            public IntPtr name;
            public uint semantic;
            public uint format;
            public float default_clear_x;
            public float default_clear_y;
            public float default_clear_z;
            public float default_clear_w;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningGpuFieldDesc {
            public IntPtr label;
            public uint dim_x;
            public uint dim_y;
            public uint dim_z;
            public uint channels;
            public uint bytes_per_channel;
            public uint dimension;
            public uint tile_x;
            public uint tile_y;
            public uint tile_z;
            public uint channel_format;
            public uint storage_mode;
            public CunningGpuFieldWorldTransform world_transform;
            public IntPtr layers;
            public uint layer_count;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningGpuBindingSetDesc {
            public IntPtr buffers;
            public uint buffer_count;
            public IntPtr fields;
            public uint field_count;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningGpuSubmissionDesc {
            public ulong kernel;
            public CunningGpuBindingSetDesc bindings;
            public uint dim_x;
            public uint dim_y;
            public uint dim_z;
            public IntPtr wait_sync_points;
            public uint wait_sync_count;
            public uint has_signal;
            public CunningGpuSyncPoint signal;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningGpuRecordingDesc {
            public ulong kernel;
            public CunningGpuBindingSetDesc bindings;
            public uint dim_x;
            public uint dim_y;
            public uint dim_z;
            public ulong host_recording_token;
            public IntPtr wait_sync_points;
            public uint wait_sync_count;
            public uint has_signal;
            public CunningGpuSyncPoint signal;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningGpuReadbackRequest {
            public uint resource_kind;
            public ulong resource_handle;
            public ulong offset;
            public ulong size_bytes;
            public uint has_fence;
            public ulong fence;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningGpuResidencyTransitionDesc {
            public uint resource_kind;
            public ulong resource_handle;
            public uint has_field_range;
            public uint layer_start;
            public uint layer_count;
            public uint mip_start;
            public uint mip_count;
            public uint region_origin_x;
            public uint region_origin_y;
            public uint region_origin_z;
            public uint region_extent_x;
            public uint region_extent_y;
            public uint region_extent_z;
            public ulong bytes;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningHostGpuImportBufferDesc {
            public ulong host_resource;
            public uint backend_identity;
            public ulong queue_token;
            public ulong size_bytes;
            public uint stride_bytes;
            public uint ownership;
            public uint state;
            public IntPtr wait_sync_points;
            public uint wait_sync_count;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningHostGpuImportFieldDesc {
            public ulong host_resource;
            public uint backend_identity;
            public ulong queue_token;
            public uint dim_x;
            public uint dim_y;
            public uint dim_z;
            public uint channels;
            public uint bytes_per_channel;
            public uint dimension;
            public uint tile_x;
            public uint tile_y;
            public uint tile_z;
            public uint channel_format;
            public uint storage_mode;
            public CunningGpuFieldWorldTransform world_transform;
            public IntPtr layers;
            public uint layer_count;
            public uint ownership;
            public uint state;
            public IntPtr wait_sync_points;
            public uint wait_sync_count;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningHostGpuExportBufferDesc {
            public uint backend_identity;
            public ulong queue_token;
            public uint state;
            public uint ownership;
            public IntPtr wait_sync_points;
            public uint wait_sync_count;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningHostGpuExportFieldDesc {
            public uint backend_identity;
            public ulong queue_token;
            public uint state;
            public uint ownership;
            public uint layer_start;
            public uint layer_count;
            public uint mip_start;
            public uint mip_count;
            public uint region_origin_x;
            public uint region_origin_y;
            public uint region_origin_z;
            public uint region_extent_x;
            public uint region_extent_y;
            public uint region_extent_z;
            public IntPtr wait_sync_points;
            public uint wait_sync_count;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningHostGpuExportBuffer {
            public ulong host_resource;
            public uint has_signal;
            public CunningGpuSyncPoint signal;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningHostGpuExportField {
            public ulong host_resource;
            public uint has_signal;
            public CunningGpuSyncPoint signal;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate CunningStatus HostGpuMemoryBudgetFn(IntPtr userData, IntPtr outBudget);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate CunningStatus HostGpuResolveBridgeFn(IntPtr userData, IntPtr outBridgeHandle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate CunningStatus HostGpuResolveTokenBridgeFn(IntPtr userData, ulong token, IntPtr outBridgeHandle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate CunningStatus HostGpuCompileModuleFn(IntPtr userData, IntPtr desc, IntPtr outHandle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate CunningStatus HostGpuCreateBufferFn(IntPtr userData, IntPtr desc, IntPtr outHandle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate CunningStatus HostGpuWriteBufferFn(IntPtr userData, ulong handle, ulong offset, IntPtr bytes, ulong byteLen);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate CunningStatus HostGpuCreateFieldFn(IntPtr userData, IntPtr desc, IntPtr outHandle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate CunningStatus HostGpuImportBufferFn(IntPtr userData, IntPtr desc, IntPtr outHandle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate CunningStatus HostGpuExportBufferFn(IntPtr userData, ulong handle, IntPtr desc, IntPtr outExport);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate CunningStatus HostGpuImportFieldFn(IntPtr userData, IntPtr desc, IntPtr outHandle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate CunningStatus HostGpuExportFieldFn(IntPtr userData, ulong handle, IntPtr desc, IntPtr outExport);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate CunningStatus HostGpuResidencyFn(IntPtr userData, IntPtr desc);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate CunningStatus HostGpuCreateFenceFn(IntPtr userData, IntPtr label, IntPtr outFence);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate CunningStatus HostGpuSubmitFn(IntPtr userData, IntPtr desc, IntPtr outFence);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate CunningStatus HostGpuRecordIntoHostFn(IntPtr userData, IntPtr desc, IntPtr outFence);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate CunningStatus HostGpuReadbackFn(IntPtr userData, IntPtr desc, IntPtr outBytes, IntPtr inoutLen, IntPtr outFence, IntPtr outHasFence);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate CunningStatus HostGpuPollFenceFn(IntPtr userData, ulong fence, IntPtr outCompleted);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate CunningStatus HostGpuAwaitFenceFn(IntPtr userData, ulong fence);

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningHostGpuBackendCallbacks {
            public IntPtr user_data;
            public uint execution_model;
            public CunningGpuCapabilities capabilities;
            public CunningGpuMemoryBudget initial_memory_budget;
            public IntPtr memory_budget;
            public IntPtr resolve_rust_wgpu_device_queue_bridge;
            public IntPtr resolve_rust_relax_v7_host_recording_bridge;
            public IntPtr resolve_rust_relax_v7_execution_object_bridge;
            public IntPtr compile_module;
            public IntPtr create_buffer;
            public IntPtr write_buffer;
            public IntPtr create_field;
            public IntPtr import_buffer;
            public IntPtr export_buffer;
            public IntPtr import_field;
            public IntPtr export_field;
            public IntPtr evict_resource;
            public IntPtr rehydrate_resource;
            public IntPtr create_fence;
            public IntPtr submit;
            public IntPtr record_into_host;
            public IntPtr readback;
            public IntPtr poll_fence;
            public IntPtr await_fence;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningFrameDesc {
            public ulong frame_index;
            public ulong cpu_deadline_ns;
            public ulong gpu_deadline_ns;
            public ulong gpu_budget_bytes;
            public ulong latency_lane_cpu_ns;
            public ulong latency_lane_gpu_ns;
            public ulong latency_lane_reserved_bytes;
            public ulong throughput_lane_cpu_ns;
            public ulong throughput_lane_gpu_ns;
            public ulong throughput_lane_budget_bytes;
            public ulong background_lane_cpu_ns;
            public ulong background_lane_gpu_ns;
            public ulong background_lane_budget_bytes;
            public uint mode;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningJobSnapshot {
            public uint status;
            public ulong submitted_ms;
            public ulong started_ms;
            public ulong finished_ms;
            public ulong compute_ms;
            public ulong primary_output_geometry_handle;
            public uint output_count;
            public uint reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningGpuFieldWorldTransform {
            public float origin_x, origin_y, origin_z;
            public float basis_x_x, basis_x_y, basis_x_z;
            public float basis_y_x, basis_y_y, basis_y_z;
            public float basis_z_x, basis_z_y, basis_z_z;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningGpuFieldInfo {
            public uint dim_x;
            public uint dim_y;
            public uint dim_z;
            public uint channels;
            public uint bytes_per_channel;
            public uint dimension;
            public uint tile_x;
            public uint tile_y;
            public uint tile_z;
            public uint channel_format;
            public uint storage_mode;
            public CunningGpuFieldWorldTransform world_transform;
            public uint layer_count;
            public ulong estimated_bytes;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningGpuFieldLayerInfo {
            public uint semantic;
            public uint format;
            public float default_clear_x;
            public float default_clear_y;
            public float default_clear_z;
            public float default_clear_w;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningGpuFieldLayerReadbackDesc {
            public uint layer_index;
            public uint origin_x, origin_y, origin_z;
            public uint extent_x, extent_y, extent_z;
            public IntPtr out_f32;
            public uint out_count;
            public IntPtr out_required_count;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningHostGeometryDesc {
            public ulong handle;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CunningMaterializeHostDesc {
            public CunningMaterializeHostKind kind;
            public IntPtr out_handle;
            public IntPtr out_buf;
            public uint cap;
            public IntPtr out_len;
        }

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_runtime_create(ref CunningRuntimeCreateDesc desc);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern void cunning_runtime_destroy(ulong ctx);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern CunningStatus cunning_runtime_begin_frame(ulong ctx, ref CunningFrameDesc desc);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern void cunning_runtime_end_frame(ulong ctx);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_runtime_get_last_error(StringBuilder out_buf, uint cap);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_cda_submit_values(ulong ctx, ulong cda_id, ulong instance_id, ulong gen, string params_json, ulong[] input_values, uint input_count);

        [DllImport(DLL_NAME, CallingConvention = CC, EntryPoint = "cunning_job_poll_runtime")]
        public static extern CunningJobStatus cunning_job_poll_runtime(ulong ctx, ulong job_id, out CunningJobSnapshot out_snapshot);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern void cunning_job_cancel_runtime(ulong ctx, ulong job_id);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_job_output_count(ulong ctx, ulong job_id);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern CunningStatus cunning_job_output_value(ulong ctx, ulong job_id, uint index, out ulong out_value);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_value_import_geometry(ulong ctx, ref CunningHostGeometryDesc desc);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_value_import_gpu_field(ulong ctx, ref CunningHostGpuImportFieldDesc desc);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern CunningValueKind cunning_value_kind(ulong ctx, ulong value);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern void cunning_value_release(ulong ctx, ulong value);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern CunningStatus cunning_value_materialize_host(ulong ctx, ulong value, ref CunningMaterializeHostDesc desc);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern CunningStatus cunning_value_gpu_field_get_info(ulong ctx, ulong value, out CunningGpuFieldInfo out_info);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern CunningStatus cunning_value_gpu_field_get_layer_info(ulong ctx, ulong value, uint layer_index, out CunningGpuFieldLayerInfo out_info);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern CunningStatus cunning_value_materialize_gpu_field_layer_f32(ulong ctx, ulong value, ref CunningGpuFieldLayerReadbackDesc desc);

        [StructLayout(LayoutKind.Sequential)]
        public struct GeoRenderVertex {
            public float px, py, pz;
            public float nx, ny, nz;
            public float u, v;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct GeoRenderState {
            public ulong dirty_id;
            public ulong topology_version;
            public ulong point_domain_version;
            public ulong vertex_domain_version;
            public ulong primitive_domain_version;
            public ulong detail_domain_version;
            public uint vertex_count;
            public uint prim_count;
            public uint tri_index_count;
            public uint line_index_count;
            public uint flags;
            public uint dirty_vertex_start;
            public uint dirty_vertex_count;
        }

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_cda_get_export_count(ulong cda_id);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_cda_get_access_mode(ulong cda_id); // 0=WhiteBox, 1=BlackBox

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_cda_get_node_count(ulong cda_id);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_cda_get_node_id(ulong cda_id, uint index, StringBuilder out_buf, uint cap);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_cda_get_node_name(ulong cda_id, uint index, StringBuilder out_buf, uint cap);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_cda_get_node_type_id(ulong cda_id, uint index, StringBuilder out_buf, uint cap);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_cda_find_node_id_by_name(ulong cda_id, string node_name, StringBuilder out_buf, uint cap);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_cda_get_node_param_count(ulong cda_id, string node_id);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_cda_get_node_param_name(ulong cda_id, string node_id, uint index, StringBuilder out_buf, uint cap);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_cda_get_node_param_json(ulong cda_id, string node_id, string param_name, StringBuilder out_buf, uint cap);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_cda_get_node_param_ui_json(ulong cda_id, string node_id, string param_name, StringBuilder out_buf, uint cap);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern ulong cunning_cda_get_cached_node_geo_handle(ulong instance_id, ulong gen, string node_id);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_cda_set_cached_node_geo_enabled(ulong instance_id, uint enabled);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_cda_is_cached_node_geo_enabled(ulong instance_id);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_cda_clear_cached_node_geo(ulong instance_id);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_cda_get_export_kind(ulong cda_id, uint index);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_cda_get_export_name(ulong cda_id, uint index, StringBuilder out_buf, uint cap);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_load_graph_asset(string path);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern void cunning_cook_async();

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_get_output_geo();

        // --- Operators (Unity Direct Call) ---
        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_op_boolean(ulong handle_a, ulong handle_b, uint op_type);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_op_poly_extrude(ulong handle, float distance, float inset);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_op_poly_extrude_prim(ulong handle, uint prim_index, float distance, float inset, uint divisions);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_op_poly_extrude_prims(ulong handle, uint[] prim_indices, uint prim_count, float distance, float inset);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_op_poly_bevel(ulong handle, float distance, uint divisions);

        // --- Geometry Access ---
        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_get_point_count(ulong handle);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_get_vertex_count(ulong handle);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_get_edge_count(ulong handle);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_copy_edge_point_indices(ulong handle, IntPtr out_ptr);
        
        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_get_prim_count(ulong handle);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern void cunning_geo_copy_points(ulong handle, IntPtr out_ptr);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern void cunning_geo_copy_vertices(ulong handle, IntPtr out_ptr);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_copy_vertex_normals(ulong handle, IntPtr out_ptr);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_copy_vertex_uv0(ulong handle, IntPtr out_ptr);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_copy_indices(ulong handle, IntPtr out_ptr);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_copy_lines(ulong handle, IntPtr out_ptr);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_geo_get_dirty_id(ulong handle);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_get_render_state(ulong handle, out GeoRenderState out_state);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_copy_render_vertices(ulong handle, IntPtr out_ptr);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_copy_render_vertices_range(ulong handle, uint start_vertex, uint vertex_count, IntPtr out_ptr);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_copy_render_material_indices(ulong handle, IntPtr out_ptr);

        // --- Packed Prototype Extraction (instancing support) ---
        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern ulong cunning_geo_extract_packed(ulong handle, string packed_id);

        public static bool TryGeoExtractPacked(ulong handle, string packedId, out ulong outHandle) {
            outHandle = 0;
            if (handle == 0 || string.IsNullOrEmpty(packedId)) return false;
            outHandle = cunning_geo_extract_packed(handle, packedId);
            return outHandle != 0;
        }

        // --- Geometry Attributes (host reads what it needs) ---
        // GeoAttrClass: 0=Detail,1=Point,2=Vertex,3=Primitive,4=Edge
        // GeoAttrType:
        // 0=Unknown,1=F32,2=Vec2,3=Vec3,4=Vec4,5=I32,6=IVec2,7=BoolU8,
        // 8=F64,9=DVec2,10=DVec3,11=DVec4,12=StringUtf8,13=BytesU8
        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_get_attr_count(ulong handle, uint attr_class);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_get_attr_name(ulong handle, uint attr_class, uint index, StringBuilder out_buf, uint cap);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_get_group_count(ulong handle, uint attr_class);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_get_group_name(ulong handle, uint attr_class, uint index, StringBuilder out_buf, uint cap);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_get_group_len(ulong handle, uint attr_class, string name);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_copy_group_mask_u64(ulong handle, uint attr_class, string name, IntPtr out_ptr, uint out_cap_u64);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_get_attr_len(ulong handle, uint attr_class, string name);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_get_attr_type(ulong handle, uint attr_class, string name);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_copy_attr_f32(ulong handle, uint attr_class, string name, IntPtr out_ptr, uint out_cap_f32);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_copy_attr_i32(ulong handle, uint attr_class, string name, IntPtr out_ptr, uint out_cap_i32);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_copy_attr_f64(ulong handle, uint attr_class, string name, IntPtr out_ptr, uint out_cap_f64);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_copy_attr_u8(ulong handle, uint attr_class, string name, IntPtr out_ptr, uint out_cap_u8);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_copy_attr_bytes(ulong handle, uint attr_class, string name, IntPtr out_ptr, uint out_cap_u8);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_get_attr_string_count(ulong handle, uint attr_class, string name);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_get_attr_string(ulong handle, uint attr_class, string name, uint index, StringBuilder out_buf, uint cap);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_copy_prim_point_offsets(ulong handle, IntPtr out_ptr);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_copy_prim_point_indices(ulong handle, IntPtr out_ptr);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_copy_prim_vertex_offsets(ulong handle, IntPtr out_ptr);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_copy_prim_vertex_indices(ulong handle, IntPtr out_ptr);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern void cunning_release_handle(ulong handle);

        // --- Zstd (Binary Transport) ---
        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_zstd_compress_bound(uint src_len);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_zstd_compress(IntPtr src_ptr, uint src_len, int level, IntPtr out_ptr, uint out_cap);

        // --- Spline Snapshot (JSON -> FlatBuffers) ---
        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_spline_snapshot_json_to_fbs(IntPtr json_ptr, uint json_len);

        // --- Spline Snapshot (JSON -> FlatBuffers -> Zstd) ---
        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_spline_snapshot_json_to_fbs_zstd(IntPtr json_ptr, uint json_len, int zstd_level);

        // --- Spline Snapshot (FBS -> Geometry Handle) ---
        // source_basis: 0=internal(bevy), 1=unity
        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_spline_snapshot_fbs_to_geo(IntPtr fbs_ptr, uint fbs_len, uint source_basis);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_blob_get_size(ulong blob_handle);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_blob_copy(ulong blob_handle, IntPtr out_ptr, uint out_cap);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern void cunning_blob_release(ulong blob_handle);
    }
}
