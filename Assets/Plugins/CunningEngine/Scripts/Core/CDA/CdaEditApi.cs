using System;
using System.Collections.Generic;
using System.Text;

namespace CunningEngine.CDA {
    /// <summary>
    /// Host-side command model for CDA graph editing transactions.
    /// No UI dependency; usable by Unity/UE adapters.
    /// </summary>
    public static class CdaEditTransaction {
        public static string NewId() {
            return Guid.NewGuid().ToString();
        }

        public abstract class Command {
            internal abstract string Op { get; }
            internal abstract void WriteBody(StringBuilder sb);

            internal void WriteJson(StringBuilder sb) {
                sb.Append('{');
                sb.Append("\"op\":\"").Append(CdaMiniJson.EscapeString(Op)).Append('"');
                WriteBody(sb);
                sb.Append('}');
            }
        }

        public sealed class AddNodeCommand : Command {
            public string nodeId;
            public string typeId;
            public string name;
            public string paramsJson;

            internal override string Op => "add_node";

            internal override void WriteBody(StringBuilder sb) {
                if (string.IsNullOrWhiteSpace(nodeId)) throw new InvalidOperationException("AddNodeCommand.nodeId is required.");
                sb.Append(",\"node_id\":\"").Append(CdaMiniJson.EscapeString(nodeId)).Append('"');
                sb.Append(",\"type_id\":\"").Append(CdaMiniJson.EscapeString(typeId ?? "")).Append('"');
                if (!string.IsNullOrWhiteSpace(name)) {
                    sb.Append(",\"name\":\"").Append(CdaMiniJson.EscapeString(name)).Append('"');
                }
                if (!string.IsNullOrWhiteSpace(paramsJson)) {
                    sb.Append(",\"params_json\":").Append('\"').Append(CdaMiniJson.EscapeString(paramsJson)).Append('\"');
                }
            }
        }

        public sealed class RemoveNodeCommand : Command {
            public string nodeId;
            internal override string Op => "remove_node";
            internal override void WriteBody(StringBuilder sb) {
                sb.Append(",\"node_id\":\"").Append(CdaMiniJson.EscapeString(nodeId ?? "")).Append('"');
            }
        }

        public sealed class SetNodeNameCommand : Command {
            public string nodeId;
            public string name;
            internal override string Op => "set_node_name";
            internal override void WriteBody(StringBuilder sb) {
                sb.Append(",\"node_id\":\"").Append(CdaMiniJson.EscapeString(nodeId ?? "")).Append('"');
                sb.Append(",\"name\":\"").Append(CdaMiniJson.EscapeString(name ?? "")).Append('"');
            }
        }

        public sealed class SetNodeParamJsonCommand : Command {
            public string nodeId;
            public string param;
            public string valueJson;
            internal override string Op => "set_node_param_json";
            internal override void WriteBody(StringBuilder sb) {
                sb.Append(",\"node_id\":\"").Append(CdaMiniJson.EscapeString(nodeId ?? "")).Append('"');
                sb.Append(",\"param\":\"").Append(CdaMiniJson.EscapeString(param ?? "")).Append('"');
                sb.Append(",\"value_json\":\"").Append(CdaMiniJson.EscapeString(valueJson ?? "{}")).Append('"');
            }
        }

        public sealed class RemoveNodeParamCommand : Command {
            public string nodeId;
            public string param;
            internal override string Op => "remove_node_param";
            internal override void WriteBody(StringBuilder sb) {
                sb.Append(",\"node_id\":\"").Append(CdaMiniJson.EscapeString(nodeId ?? "")).Append('"');
                sb.Append(",\"param\":\"").Append(CdaMiniJson.EscapeString(param ?? "")).Append('"');
            }
        }

        public sealed class AddConnectionCommand : Command {
            public string connId;
            public string fromNode;
            public uint fromPort;
            public string toNode;
            public uint toPort;
            public int? order;

            internal override string Op => "add_connection";
            internal override void WriteBody(StringBuilder sb) {
                if (string.IsNullOrWhiteSpace(connId)) throw new InvalidOperationException("AddConnectionCommand.connId is required.");
                sb.Append(",\"conn_id\":\"").Append(CdaMiniJson.EscapeString(connId)).Append('"');
                sb.Append(",\"from_node\":\"").Append(CdaMiniJson.EscapeString(fromNode ?? "")).Append('"');
                sb.Append(",\"from_port\":").Append(fromPort);
                sb.Append(",\"to_node\":\"").Append(CdaMiniJson.EscapeString(toNode ?? "")).Append('"');
                sb.Append(",\"to_port\":").Append(toPort);
                if (order.HasValue) {
                    sb.Append(",\"order\":").Append(order.Value);
                }
            }
        }

        public sealed class RemoveConnectionCommand : Command {
            public string connId;
            public string fromNode;
            public uint? fromPort;
            public string toNode;
            public uint? toPort;

            internal override string Op => "remove_connection";
            internal override void WriteBody(StringBuilder sb) {
                if (!string.IsNullOrWhiteSpace(connId)) {
                    sb.Append(",\"conn_id\":\"").Append(CdaMiniJson.EscapeString(connId)).Append('"');
                }
                if (!string.IsNullOrWhiteSpace(fromNode)) {
                    sb.Append(",\"from_node\":\"").Append(CdaMiniJson.EscapeString(fromNode)).Append('"');
                }
                if (fromPort.HasValue) {
                    sb.Append(",\"from_port\":").Append(fromPort.Value);
                }
                if (!string.IsNullOrWhiteSpace(toNode)) {
                    sb.Append(",\"to_node\":\"").Append(CdaMiniJson.EscapeString(toNode)).Append('"');
                }
                if (toPort.HasValue) {
                    sb.Append(",\"to_port\":").Append(toPort.Value);
                }
            }
        }

        public static string BuildCommandsJson(IReadOnlyList<Command> commands) {
            if (commands == null || commands.Count == 0) return "[]";
            var sb = new StringBuilder(512);
            sb.Append('[');
            bool first = true;
            for (int i = 0; i < commands.Count; i++) {
                var c = commands[i];
                if (c == null) continue;
                if (!first) sb.Append(',');
                first = false;
                c.WriteJson(sb);
            }
            sb.Append(']');
            return sb.ToString();
        }
    }

    /// <summary>
    /// Native bridge for CDA graph editing transactions.
    /// Foundation layer for future Unity/UE node editor adapters.
    /// </summary>
    public static class CdaEditApi {
        static string GetLastError(string fallback) {
            var sb = new StringBuilder(2048);
            NativeMethods.cunning_cda_get_last_error(sb, (uint)sb.Capacity);
            var msg = sb.ToString();
            return string.IsNullOrWhiteSpace(msg) ? fallback : msg;
        }

        public static bool TryGetRevision(ulong cdaId, out ulong revision) {
            revision = NativeMethods.cunning_cda_get_revision(cdaId);
            return revision != 0;
        }

        public static bool TryGetDefinitionJson(ulong cdaId, out string definitionJson, out string error) {
            definitionJson = "";
            error = null;
            uint payloadLen = NativeMethods.cunning_cda_get_definition_json_len(cdaId);
            if (payloadLen == 0) {
                error = GetLastError("cda get definition json length failed");
                return false;
            }
            if (payloadLen >= int.MaxValue) {
                error = $"cda definition json too large: {payloadLen} bytes";
                return false;
            }
            int cap = checked((int)payloadLen + 1);
            var sb = new StringBuilder(cap);
            uint rv = NativeMethods.cunning_cda_get_definition_json(cdaId, sb, (uint)cap);
            if (rv != 0) {
                definitionJson = sb.ToString();
                return true;
            }
            error = GetLastError("cda get definition json failed");
            return false;
        }

        public static bool TrySetDefinitionJson(ulong cdaId, string definitionJson, out ulong revision, out string error) {
            revision = 0;
            error = null;
            uint ok = NativeMethods.cunning_cda_set_definition_json(cdaId, definitionJson ?? "");
            if (ok == 0) {
                error = GetLastError("cda set definition json failed");
                return false;
            }
            revision = NativeMethods.cunning_cda_get_revision(cdaId);
            return true;
        }

        public static bool TryApplyCommands(ulong cdaId, IReadOnlyList<CdaEditTransaction.Command> commands, out ulong revision, out string error) {
            revision = 0;
            error = null;
            string json = CdaEditTransaction.BuildCommandsJson(commands);
            uint ok = NativeMethods.cunning_cda_apply_edit_commands(cdaId, json);
            if (ok == 0) {
                error = GetLastError("cda apply edit commands failed");
                return false;
            }
            revision = NativeMethods.cunning_cda_get_revision(cdaId);
            return true;
        }
    }
}
