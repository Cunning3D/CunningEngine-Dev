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
        static bool s_jobStatsApiAvailable = true;
        static bool s_jobStatsApiWarned;
        static bool s_extractPackedApiAvailable = true;
        static bool s_extractPackedApiWarned;

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

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_cda_submit(ulong cda_id, ulong instance_id, ulong gen, string params_json, ulong[] input_handles, uint input_count);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_cda_submit_select(ulong cda_id, ulong instance_id, ulong gen, string params_json, ulong[] input_handles, uint input_count, string exports_json);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_job_poll(ulong job_id);

        [StructLayout(LayoutKind.Sequential)]
        public struct JobStats {
            public uint status;
            public ulong submitted_ms, started_ms, finished_ms, compute_ms;
            public ulong out_handle;
        }

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
        public static extern uint cunning_job_get_stats(ulong job_id, out JobStats out_stats);

        public static bool TryGetJobStats(ulong jobId, out JobStats stats) {
            stats = default;
            if (jobId == 0 || !s_jobStatsApiAvailable) return false;
            try {
                return cunning_job_get_stats(jobId, out stats) != 0;
            } catch (EntryPointNotFoundException) {
                s_jobStatsApiAvailable = false;
                if (!s_jobStatsApiWarned) {
                    s_jobStatsApiWarned = true;
                    Debug.LogWarning("CunningEngine: Native API missing: cunning_job_get_stats (rebuild native DLL).");
                }
                return false;
            }
        }

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_job_cancel(ulong job_id);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_job_get_output_count(ulong job_id);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_job_get_output_handle(ulong job_id, uint index);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_job_get_output_kind(ulong job_id, uint index);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_job_get_output_geo_handle(ulong job_id, uint index);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_job_get_output_param_json(ulong job_id, uint index, StringBuilder out_buf, uint cap);

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
            if (handle == 0 || string.IsNullOrEmpty(packedId) || !s_extractPackedApiAvailable) return false;
            try {
                outHandle = cunning_geo_extract_packed(handle, packedId);
                return outHandle != 0;
            } catch (EntryPointNotFoundException) {
                s_extractPackedApiAvailable = false;
                if (!s_extractPackedApiWarned) {
                    s_extractPackedApiWarned = true;
                    Debug.LogWarning("CunningEngine: Native API missing: cunning_geo_extract_packed (rebuild native DLL).");
                }
                return false;
            }
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
