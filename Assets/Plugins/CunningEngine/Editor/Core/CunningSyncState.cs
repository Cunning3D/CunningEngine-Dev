using UnityEditor;

namespace CunningEngine.Editor {
    // Editor-only sync state (persisted via EditorPrefs)
    static class CunningSyncState {
        const string KEnabled = "CunningEngine.Sync.Enabled";
        const string KSyncCamera = "CunningEngine.Sync.Camera";
        const string KDbPath = "CunningEngine.Sync.DbPath";
        const string KPid = "CunningEngine.Sync.Cunning3D.Pid";

        public static bool Enabled { get => EditorPrefs.GetBool(KEnabled, false); set => EditorPrefs.SetBool(KEnabled, value); }
        public static bool SyncCamera { get => EditorPrefs.GetBool(KSyncCamera, true); set => EditorPrefs.SetBool(KSyncCamera, value); }
        public static string DbPath { get => EditorPrefs.GetString(KDbPath, ""); set => EditorPrefs.SetString(KDbPath, value ?? ""); }
        public static int Cunning3DPid { get => EditorPrefs.GetInt(KPid, 0); set => EditorPrefs.SetInt(KPid, value); }
    }
}

