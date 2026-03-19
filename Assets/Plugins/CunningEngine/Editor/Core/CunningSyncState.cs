using UnityEditor;

namespace CunningEngine.Editor {
    // Editor-only sync state (persisted via EditorPrefs)
    static class CunningSyncState {
        const string KEnabled = "CunningEngine.Sync.Enabled";
        const string KSyncCamera = "CunningEngine.Sync.Camera";
        const string KSessionName = "CunningEngine.Sync.SessionName";
        const string KUnityToC3DMapName = "CunningEngine.Sync.UnityToC3DMapName";
        const string KC3DToUnityMapName = "CunningEngine.Sync.C3DToUnityMapName";
        const string KPid = "CunningEngine.Sync.Cunning3D.Pid";
        const string KLegacyDbPath = "CunningEngine.Sync.DbPath";

        public static bool Enabled { get => EditorPrefs.GetBool(KEnabled, false); set => EditorPrefs.SetBool(KEnabled, value); }
        public static bool SyncCamera { get => EditorPrefs.GetBool(KSyncCamera, true); set => EditorPrefs.SetBool(KSyncCamera, value); }
        public static string SessionName { get => EditorPrefs.GetString(KSessionName, ""); set => EditorPrefs.SetString(KSessionName, value ?? ""); }
        public static string UnityToC3DMapName { get => EditorPrefs.GetString(KUnityToC3DMapName, ""); set => EditorPrefs.SetString(KUnityToC3DMapName, value ?? ""); }
        public static string C3DToUnityMapName { get => EditorPrefs.GetString(KC3DToUnityMapName, ""); set => EditorPrefs.SetString(KC3DToUnityMapName, value ?? ""); }
        public static int Cunning3DPid { get => EditorPrefs.GetInt(KPid, 0); set => EditorPrefs.SetInt(KPid, value); }

        public static void ClearLegacyDbState() {
            EditorPrefs.DeleteKey(KLegacyDbPath);
        }

        public static void ClearConnectionState() {
            SessionName = "";
            UnityToC3DMapName = "";
            C3DToUnityMapName = "";
            Cunning3DPid = 0;
            ClearLegacyDbState();
        }
    }
}

