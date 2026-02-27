using System;
using UnityEngine;

namespace CunningEngine {
    public class CunningBridge {
        static CunningBridge _instance;
        public static CunningBridge Instance => _instance ??= new CunningBridge();
        bool _initialized;
        bool _dbOpened;
        public bool DbOpened => _dbOpened;
        public string DbPath { get; private set; }

        public void Init() {
            if (_initialized) return;
            try { NativeMethods.EnsureLoaded(); NativeMethods.cunning_init(); _initialized = true; Debug.Log("CunningBridge: Native initialized."); }
            catch (System.Exception e) { Debug.LogError($"CunningBridge: Init failed: {e.Message}"); }
        }

        public bool OpenDb(string absPath, bool create) {
            Init();
            if (string.IsNullOrEmpty(absPath)) return false;
            uint ok;
            try { ok = NativeMethods.cunning_bridge_open(absPath, create ? 1u : 0u); }
            catch (Exception e) { Debug.LogWarning($"CunningBridge: cunning_bridge_open failed ({e.GetType().Name}): {e.Message}"); return false; }
            if (ok != 0) { _dbOpened = true; DbPath = absPath; }
            return ok != 0;
        }
    }
}
