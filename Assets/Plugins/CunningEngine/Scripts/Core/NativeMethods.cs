using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace CunningEngine {
    public static class NativeMethods {
        // Name of the DLL. Ensure 'cunning_core_ffi.dll' is in Plugins/x86_64/
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
            var data = Application.dataPath;
            var cand = new[] {
                Path.GetFullPath(Path.Combine(data, "Plugins", "x86_64", DLL_NAME + ".dll")),
                Path.GetFullPath(Path.Combine(data, "Plugins", "CunningEngine", "Plugins", "x86_64", DLL_NAME + ".dll")),
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
            throw new DllNotFoundException($"Failed to load native DLL '{DLL_NAME}.dll'.\nTried:\n{tried}\nTip: move it under Assets/Plugins/x86_64/ or fix Plugin Importer settings.");
#else
            // Non-Windows platforms: rely on Unity native plugin importer paths.
            s_loaded = 1;
#endif
        }

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern void cunning_init();

        // --- Debug Bridge (redb-backed capture store) ---
        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_bridge_open(string path, uint create);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_bridge_put_blob(IntPtr ptr, uint len);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_bridge_get_blob_size(ulong blob_id);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_bridge_copy_blob(ulong blob_id, IntPtr out_ptr, uint out_cap);

        // --- Bridge State Channels (key -> latest blob id) ---
        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_state_get_latest(byte[] key_utf8, uint key_len);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_state_set_latest(byte[] key_utf8, uint key_len, ulong blob_id);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_state_put_latest(byte[] key_utf8, uint key_len, IntPtr ptr, uint len);

        // --- Atomic Modeling (Unity-side N-gon authoring) ---
        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_geo_create();

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_geo_add_point(ulong handle, float x, float y, float z);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_geo_add_poly(ulong handle, ulong[] point_ids, uint count);

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

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_job_get_stats(ulong job_id, out JobStats out_stats);

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
        public static extern ulong cunning_op_poly_bevel(ulong handle, float distance, uint divisions);

        // --- Geometry Access ---
        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_get_point_count(ulong handle);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_get_vertex_count(ulong handle);
        
        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_get_prim_count(ulong handle);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern void cunning_geo_copy_points(ulong handle, IntPtr out_ptr);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern void cunning_geo_copy_vertices(ulong handle, IntPtr out_ptr);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_copy_indices(ulong handle, IntPtr out_ptr);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_copy_lines(ulong handle, IntPtr out_ptr);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern ulong cunning_geo_get_dirty_id(ulong handle);

        // --- Geometry Attributes (host reads what it needs) ---
        // GeoAttrClass: 0=Detail,1=Point,2=Vertex,3=Primitive,4=Edge
        // GeoAttrType: 0=Unknown,1=F32,2=Vec2,3=Vec3,4=Vec4,5=I32,6=IVec2,7=BoolU8
        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_get_attr_len(ulong handle, uint attr_class, string name);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_get_attr_type(ulong handle, uint attr_class, string name);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_copy_attr_f32(ulong handle, uint attr_class, string name, IntPtr out_ptr, uint out_cap_f32);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_copy_attr_i32(ulong handle, uint attr_class, string name, IntPtr out_ptr, uint out_cap_i32);

        [DllImport(DLL_NAME, CallingConvention = CC, CharSet = CharSet.Ansi)]
        public static extern uint cunning_geo_copy_attr_u8(ulong handle, uint attr_class, string name, IntPtr out_ptr, uint out_cap_u8);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_copy_prim_point_offsets(ulong handle, IntPtr out_ptr);

        [DllImport(DLL_NAME, CallingConvention = CC)]
        public static extern uint cunning_geo_copy_prim_point_indices(ulong handle, IntPtr out_ptr);

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
