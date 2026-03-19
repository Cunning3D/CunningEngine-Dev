using System;
using UnityEngine;

namespace CunningEngine {
    public class CunningBridge {
        static CunningBridge _instance;
        public static CunningBridge Instance => _instance ??= new CunningBridge();
        bool _initialized;

        public void Init() {
            if (_initialized) return;
            try { NativeMethods.EnsureLoaded(); NativeMethods.cunning_init(); _initialized = true; Debug.Log("CunningBridge: Native initialized."); }
            catch (System.Exception e) { Debug.LogError($"CunningBridge: Init failed: {e.Message}"); }
        }
    }
}
