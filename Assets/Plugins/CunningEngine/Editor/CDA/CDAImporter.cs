using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using CunningEngine.CDA;

namespace CunningEngine.Editor.CDA {
    [ScriptedImporter(2, "cda")]
    public sealed class CDAImporter : ScriptedImporter {
        public override void OnImportAsset(AssetImportContext ctx) {
            var json = ReadGameEngineChunk(ctx.assetPath);
            var a = ScriptableObject.CreateInstance<CDAAssetObject>();
            a.sourcePath = ctx.assetPath;
            a.sourceJson = json;
            a.sourceJsonHash = Fnv1a32(json);
            TryParse(json, a);
            ctx.AddObjectToAsset("CDA", a);
            ctx.SetMainObject(a);
        }

        static uint Fnv1a32(string s) {
            if (string.IsNullOrEmpty(s)) return 0;
            unchecked { uint h = 2166136261u; for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619u; } return h; }
        }

        static string ReadGameEngineChunk(string path) {
            var bytes = System.IO.File.ReadAllBytes(path);
            if (bytes.Length < 12) throw new Exception("CDA too small");
            if (bytes[0] != (byte)'C' || bytes[1] != (byte)'D' || bytes[2] != (byte)'A' || bytes[3] != 0) throw new Exception("Not CDA");
            var ver = BitConverter.ToUInt32(bytes, 4); if (ver != 1) throw new Exception("CDA version mismatch");
            var n = BitConverter.ToUInt32(bytes, 8);
            var at = 12;
            uint gameId = BitConverter.ToUInt32(new byte[] { (byte)'G', (byte)'A', (byte)'M', (byte)'E' }, 0);
            for (var i = 0u; i < n; i++) {
                var id = BitConverter.ToUInt32(bytes, at); at += 4;
                var off = BitConverter.ToUInt64(bytes, at); at += 8;
                var size = BitConverter.ToUInt64(bytes, at); at += 8;
                if (id != gameId) continue;
                var o = (int)off; var s = (int)size;
                return System.Text.Encoding.UTF8.GetString(bytes, o, s);
            }
            throw new Exception("GameEngineChunk not found");
        }

        static void TryParse(string json, CDAAssetObject a) {
            try {
                if (CdaMiniJson.TryGetTopLevelString(json, "access_mode", out var am) && !string.IsNullOrEmpty(am)) a.access_mode = am;
                else if (CdaMiniJson.TryGetTopLevelString(json, "exports_mode", out var em) && !string.IsNullOrEmpty(em)) a.access_mode = string.Equals(em, "BlackBox", StringComparison.OrdinalIgnoreCase) ? "BlackBox" : "WhiteBox";

                var ins = CdaMiniJson.GetTopLevelRaw(json, "inputs");
                var outs = CdaMiniJson.GetTopLevelRaw(json, "outputs");
                var ps = CdaMiniJson.GetTopLevelRaw(json, "promoted_params");
                if (!string.IsNullOrEmpty(ins)) a.inputs = ReadPorts(ins);
                if (!string.IsNullOrEmpty(outs)) a.outputs = ReadPorts(outs);
                if (!string.IsNullOrEmpty(ps)) a.promoted_params = ReadParams(ps);
            } catch (Exception e) {
                Debug.LogWarning($"CDAImporter parse failed: {e.Message}");
            }
        }

        static List<CdaPort> ReadPorts(string arrRaw) {
            var r = new List<CdaPort>();
            foreach (var p in CdaMiniJson.SplitArrayElements(arrRaw)) {
                r.Add(new CdaPort {
                    name = CdaMiniJson.TryGetObjectString(p, "name", out var n) ? n : "",
                    data_type = CdaMiniJson.TryGetObjectString(p, "data_type", out var t) ? t : "Geometry",
                });
            }
            return r;
        }

        static List<CdaPromotedParam> ReadParams(string arrRaw) {
            var r = new List<CdaPromotedParam>();
            foreach (var p in CdaMiniJson.SplitArrayElements(arrRaw)) {
                var pp = new CdaPromotedParam {
                    name = CdaMiniJson.TryGetObjectString(p, "name", out var n) ? n : "",
                    label = CdaMiniJson.TryGetObjectString(p, "label", out var l) ? l : "",
                    group = CdaMiniJson.TryGetObjectString(p, "group", out var g) ? g : "",
                    order = CdaMiniJson.TryGetObjectInt(p, "order", out var o) ? o : 0,
                    param_type = CdaMiniJson.TryGetObjectString(p, "param_type", out var t) ? t : "Float",
                    default_value_json = CdaMiniJson.TryGetObjectRaw(p, "default_value", out var dv) ? dv : "{\"Float\":0}",
                };
                if (CdaMiniJson.TryGetObjectRaw(p, "bindings", out var bs)) {
                    foreach (var b in CdaMiniJson.SplitArrayElements(bs)) {
                        pp.bindings.Add(new CdaParamBinding {
                            node = CdaMiniJson.TryGetObjectString(b, "node", out var nn) ? nn : "",
                            param = CdaMiniJson.TryGetObjectString(b, "param", out var pn) ? pn : "",
                            channel = CdaMiniJson.TryGetObjectInt(b, "channel", out var ch) ? (uint?)ch : null,
                        });
                    }
                }
                r.Add(pp);
            }
            return r;
        }
    }
}
