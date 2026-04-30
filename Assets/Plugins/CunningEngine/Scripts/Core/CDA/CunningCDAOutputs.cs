using UnityEngine;

namespace CunningEngine {
    public sealed class CunningParamOutput : MonoBehaviour {
        [TextArea(3, 16)] public string json;
        public void Apply(string valueJson) { json = valueJson ?? ""; }
    }

    public sealed class CunningDiagnosticsOutput : MonoBehaviour {
        [TextArea(3, 16)] public string json;
        public void Apply(string valueJson) { json = valueJson ?? ""; }
    }

    public sealed class CunningUnsupportedOutput : MonoBehaviour {
        public uint kind;
        public string reason;
        public void Apply(uint valueKind, string message) {
            kind = valueKind;
            reason = message ?? "";
        }
    }
}
