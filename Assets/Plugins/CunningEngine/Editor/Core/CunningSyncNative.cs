using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CunningEngine.Editor {
    static class CunningSyncNative {
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryW(string lpFileName);

        [DllImport("kernel32", SetLastError = true)]
        static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void FnInit();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint FnBridgeOpen(IntPtr path, uint create);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate ulong FnStateGetLatest(IntPtr keyPtr, uint keyLen);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate ulong FnStatePutLatest(IntPtr keyPtr, uint keyLen, IntPtr ptr, uint len);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint FnBridgeGetBlobSize(ulong blobId);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint FnBridgeCopyBlob(ulong blobId, IntPtr outPtr, uint outCap);

        static IntPtr _lib;
        static FnInit _init;
        static FnBridgeOpen _bridgeOpen;
        static FnStateGetLatest _getLatest;
        static FnStatePutLatest _putLatest;
        static FnBridgeGetBlobSize _getBlobSize;
        static FnBridgeCopyBlob _copyBlob;
        static bool _tried;
        static string _loadedPath;
        static string _lastError;

        public static string LoadedPath => _loadedPath;
        public static string LastError => _lastError;

        static string DllDir => Path.Combine(Application.dataPath, "Plugins", "CunningEngine", "Scripts", "CAssemblies", "x86_64");
        static string NewDll => Path.Combine(DllDir, "cunning_core_ffi.NEW.dll");
        static string MainDll => Path.Combine(DllDir, "cunning_core_ffi.dll");
        static string CacheDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "CunningEngine", "DllCache"));

        static T GetFn<T>(string name) where T : class {
            var p = GetProcAddress(_lib, name);
            return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }

        static string CopyToUniqueLoadPath(string src) {
            Directory.CreateDirectory(CacheDir);
            try {
                foreach (var f in Directory.GetFiles(CacheDir, "cunning_core_ffi.load.*.dll")) {
                    try { var fi = new FileInfo(f); if (fi.Exists && fi.CreationTimeUtc < DateTime.UtcNow.AddHours(-6)) fi.Delete(); } catch { }
                }
            } catch { }
            var dst = Path.Combine(CacheDir, "cunning_core_ffi.load." + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".dll");
            File.Copy(src, dst, true);
            return dst;
        }

        public static bool EnsureLoaded() {
            if (_tried) return _lib != IntPtr.Zero && _bridgeOpen != null;
            _tried = true;
            _lastError = "";
            _loadedPath = "";
            var src = File.Exists(NewDll) ? NewDll : MainDll;
            if (!File.Exists(src)) { _lastError = "DLL not found: " + src; return false; }
            var path = src;
            try { path = CopyToUniqueLoadPath(src); }
            catch (Exception e) { _lastError = "DLL copy failed: " + e.Message + " (src=" + src + ")"; return false; }
            _lib = LoadLibraryW(path);
            if (_lib == IntPtr.Zero) { _lastError = "LoadLibrary failed: " + path + " (win32=" + Marshal.GetLastWin32Error() + ")"; return false; }
            _loadedPath = path;

            _init = GetFn<FnInit>("cunning_init");
            _bridgeOpen = GetFn<FnBridgeOpen>("cunning_bridge_open");
            _getLatest = GetFn<FnStateGetLatest>("cunning_state_get_latest");
            _putLatest = GetFn<FnStatePutLatest>("cunning_state_put_latest");
            _getBlobSize = GetFn<FnBridgeGetBlobSize>("cunning_bridge_get_blob_size");
            _copyBlob = GetFn<FnBridgeCopyBlob>("cunning_bridge_copy_blob");

            var miss = "";
            if (_init == null) miss += "cunning_init ";
            if (_bridgeOpen == null) miss += "cunning_bridge_open ";
            if (_getLatest == null) miss += "cunning_state_get_latest ";
            if (_putLatest == null) miss += "cunning_state_put_latest ";
            if (_getBlobSize == null) miss += "cunning_bridge_get_blob_size ";
            if (_copyBlob == null) miss += "cunning_bridge_copy_blob ";
            if (!string.IsNullOrEmpty(miss)) { _lastError = "Missing exports: " + miss.Trim() + " (dll=" + path + ")"; return false; }
            return true;
        }

        public static bool OpenDb(string absPath, bool create) {
            if (!EnsureLoaded()) return false;
            if (_init != null) _init();
            var p = Marshal.StringToHGlobalAnsi(absPath ?? "");
            try { return _bridgeOpen(p, create ? 1u : 0u) != 0; }
            finally { Marshal.FreeHGlobal(p); }
        }

        public static ulong GetLatest(byte[] keyUtf8) {
            if (!EnsureLoaded() || _getLatest == null || keyUtf8 == null || keyUtf8.Length == 0) return 0;
            var gch = GCHandle.Alloc(keyUtf8, GCHandleType.Pinned);
            try { return _getLatest(gch.AddrOfPinnedObject(), (uint)keyUtf8.Length); }
            finally { gch.Free(); }
        }

        public static ulong PutLatest(byte[] keyUtf8, IntPtr ptr, uint len) {
            if (!EnsureLoaded() || _putLatest == null || keyUtf8 == null || keyUtf8.Length == 0 || ptr == IntPtr.Zero || len == 0) return 0;
            var gch = GCHandle.Alloc(keyUtf8, GCHandleType.Pinned);
            try { return _putLatest(gch.AddrOfPinnedObject(), (uint)keyUtf8.Length, ptr, len); }
            finally { gch.Free(); }
        }

        public static uint GetBlobSize(ulong blobId) => (!EnsureLoaded() || _getBlobSize == null) ? 0 : _getBlobSize(blobId);
        public static uint CopyBlob(ulong blobId, IntPtr outPtr, uint outCap) => (!EnsureLoaded() || _copyBlob == null) ? 0 : _copyBlob(blobId, outPtr, outCap);
    }
}

