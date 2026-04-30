using System;
using System.Collections.Generic;
using UnityEngine;

namespace CunningEngine.CDA {
    [Serializable] public sealed class CdaPort { public string name; public string data_type; }
    [Serializable] public sealed class CdaParamBinding { public string node; public string param; public uint? channel; }
    [Serializable] public sealed class CdaParamChoice { public string label; public int value; }
    [Serializable] public sealed class CdaPromotedParam {
        public string name, label, group, param_type;
        public int order;
        public string default_value_json;
        public float? min, max; // Optional range constraints
        public string folder;
        public string tooltip;
        public List<CdaParamChoice> choices = new();
        public List<CdaParamBinding> bindings = new();
    }

    [CreateAssetMenu(fileName = "NewCDA", menuName = "Cunning Engine/CDA Asset")]
    public sealed class CDAAssetObject : ScriptableObject {
        [HideInInspector] public string sourcePath;
        [HideInInspector] public string sourceJson;
        [HideInInspector] public uint sourceJsonHash;
        [HideInInspector] public string access_mode = "WhiteBox";
        public List<CdaPort> inputs = new(), outputs = new();
        public List<CdaPromotedParam> promoted_params = new();

        public bool IsBlackBox() {
            return string.Equals(access_mode, "BlackBox", StringComparison.OrdinalIgnoreCase);
        }
    }
}
