using System;
using System.Collections.Generic;
using System.Text;

namespace CunningEngine.CDA {
    [Serializable]
    public sealed class CdaNodeParamSnapshot {
        public string name;
        public string valueJson;
        public string uiJson;
    }

    [Serializable]
    public sealed class CdaNodeSnapshot {
        public string id;
        public string name;
        public string typeId;
        public List<CdaNodeParamSnapshot> parameters = new();
    }

    /// <summary>
    /// Runtime-native CDA introspection helper for editor tools and future host adapters.
    /// Centralizes node/param/UI metadata reads so callers don't duplicate P/Invoke glue.
    /// </summary>
    public static class CdaIntrospectionApi {
        static string ReadNativeString(Func<StringBuilder, uint, uint> reader, uint cap) {
            var sb = new StringBuilder((int)cap);
            uint ok = reader(sb, (uint)sb.Capacity);
            return ok == 0 ? "" : sb.ToString();
        }

        static string LastErrorOr(string fallback) {
            var sb = new StringBuilder(2048);
            NativeMethods.cunning_cda_get_last_error(sb, (uint)sb.Capacity);
            var msg = sb.ToString();
            return string.IsNullOrWhiteSpace(msg) ? fallback : msg;
        }

        public static bool TryFindNodeIdByName(ulong cdaId, string nodeName, out string nodeId, out string error) {
            nodeId = "";
            error = null;
            if (cdaId == 0) {
                error = "invalid cda id";
                return false;
            }
            if (string.IsNullOrWhiteSpace(nodeName)) {
                error = "node name is empty";
                return false;
            }
            nodeId = ReadNativeString((sb, cap) => NativeMethods.cunning_cda_find_node_id_by_name(cdaId, nodeName, sb, cap), 256);
            if (!string.IsNullOrEmpty(nodeId)) return true;
            error = LastErrorOr($"node not found by name: {nodeName}");
            return false;
        }

        public static bool TryGetNodeSnapshots(
            ulong cdaId,
            out List<CdaNodeSnapshot> nodes,
            out string error,
            bool includeParams = true,
            bool includeUiJson = true) {
            nodes = new List<CdaNodeSnapshot>();
            error = null;
            if (cdaId == 0) {
                error = "invalid cda id";
                return false;
            }

            uint nodeCount = NativeMethods.cunning_cda_get_node_count(cdaId);
            for (uint i = 0; i < nodeCount; i++) {
                string nodeId = ReadNativeString((sb, cap) => NativeMethods.cunning_cda_get_node_id(cdaId, i, sb, cap), 128);
                if (string.IsNullOrEmpty(nodeId)) continue;
                string nodeName = ReadNativeString((sb, cap) => NativeMethods.cunning_cda_get_node_name(cdaId, i, sb, cap), 512);
                string typeId = ReadNativeString((sb, cap) => NativeMethods.cunning_cda_get_node_type_id(cdaId, i, sb, cap), 512);

                var node = new CdaNodeSnapshot {
                    id = nodeId,
                    name = nodeName,
                    typeId = typeId,
                };

                if (includeParams) {
                    uint paramCount = NativeMethods.cunning_cda_get_node_param_count(cdaId, nodeId);
                    for (uint pi = 0; pi < paramCount; pi++) {
                        string pname = ReadNativeString((sb, cap) => NativeMethods.cunning_cda_get_node_param_name(cdaId, nodeId, pi, sb, cap), 256);
                        if (string.IsNullOrEmpty(pname)) continue;
                        string valueJson = ReadNativeString((sb, cap) => NativeMethods.cunning_cda_get_node_param_json(cdaId, nodeId, pname, sb, cap), 8192);
                        string uiJson = "";
                        if (includeUiJson) {
                            try {
                                uiJson = ReadNativeString((sb, cap) => NativeMethods.cunning_cda_get_node_param_ui_json(cdaId, nodeId, pname, sb, cap), 8192);
                            } catch (EntryPointNotFoundException) {
                                uiJson = "";
                            }
                        }
                        node.parameters.Add(new CdaNodeParamSnapshot {
                            name = pname,
                            valueJson = valueJson,
                            uiJson = uiJson,
                        });
                    }
                }

                nodes.Add(node);
            }
            return true;
        }
    }
}
