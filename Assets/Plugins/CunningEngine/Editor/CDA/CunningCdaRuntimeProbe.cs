using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CunningEngine.Editor.CDA {
    [InitializeOnLoad]
    public static class CunningCdaRuntimeProbe {
        const string RequestPath = "Temp/CunningCdaProbe.request";
        const string ResultPath = "Temp/CunningCdaProbeResult.txt";
        const double TimeoutSeconds = 12.0;

        static readonly MethodInfo HostUpdate = typeof(CunningCDAHostInstance).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
        static CunningCDAHostInstance[] runningHosts = Array.Empty<CunningCDAHostInstance>();
        static double startedAt;

        static CunningCdaRuntimeProbe() {
            EditorApplication.delayCall += RunIfRequested;
        }

        [MenuItem("Procedural/Cunning Engine/Diagnostics/Run CDA Scene Probe")]
        public static void RunMenuProbe() {
            BeginProbe();
        }

        static void RunIfRequested() {
            if (!File.Exists(RequestPath)) return;
            File.Delete(RequestPath);
            BeginProbe();
        }

        static void BeginProbe() {
            runningHosts = Resources.FindObjectsOfTypeAll<CunningCDAHostInstance>()
                .Where(host => host != null && host.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(host))
                .ToArray();

            var sb = new StringBuilder();
            sb.AppendLine("Cunning CDA scene probe started " + DateTime.Now.ToString("O"));
            sb.AppendLine("hosts=" + runningHosts.Length);
            WriteResult(sb.ToString());

            if (runningHosts.Length == 0) {
                AppendResult("FAILED: no CunningCDAHostInstance found in open scenes");
                return;
            }

            for (int i = 0; i < runningHosts.Length; i++) {
                var host = runningHosts[i];
                AppendResult("cook host[" + i + "] name=" + host.name + " asset=" + (host.asset != null ? host.asset.name : "<null>"));
                try {
                    host.Cook();
                } catch (Exception e) {
                    AppendResult("cook exception host[" + i + "]: " + e);
                }
            }

            startedAt = EditorApplication.timeSinceStartup;
            EditorApplication.update -= ProbeUpdate;
            EditorApplication.update += ProbeUpdate;
        }

        static void ProbeUpdate() {
            foreach (var host in runningHosts) {
                if (host == null) continue;
                try {
                    HostUpdate?.Invoke(host, null);
                } catch (Exception e) {
                    AppendResult("pump exception host=" + host.name + ": " + e.GetBaseException());
                }
            }

            var allSettled = runningHosts.All(host => host == null || host.jobId == 0);
            if (!allSettled && EditorApplication.timeSinceStartup - startedAt < TimeoutSeconds) return;

            EditorApplication.update -= ProbeUpdate;
            FinishProbe(!allSettled);
        }

        static void FinishProbe(bool timedOut) {
            var sb = new StringBuilder();
            sb.AppendLine("Cunning CDA scene probe finished " + DateTime.Now.ToString("O"));
            sb.AppendLine("timed_out=" + timedOut);
            for (int i = 0; i < runningHosts.Length; i++) {
                var host = runningHosts[i];
                if (host == null) {
                    sb.AppendLine("host[" + i + "]=<destroyed>");
                    continue;
                }

                var terrains = host.GetComponentsInChildren<Terrain>(true);
                var children = host.transform.Cast<Transform>().Select(t => t.name).ToArray();
                sb.AppendLine("host[" + i + "] name=" + host.name);
                sb.AppendLine("  jobId=" + host.jobId);
                sb.AppendLine("  cdaId=" + host.cdaId);
                sb.AppendLine("  lastStatus=" + host.lastStatus);
                sb.AppendLine("  outputStatus=" + host.outputStatus);
                sb.AppendLine("  children=" + string.Join(",", children));
                sb.AppendLine("  terrains=" + terrains.Length);
                for (int t = 0; t < terrains.Length; t++) {
                    var data = terrains[t].terrainData;
                    sb.AppendLine("  terrain[" + t + "] name=" + terrains[t].name + " resolution=" + (data != null ? data.heightmapResolution.ToString() : "<null>") + " size=" + (data != null ? data.size.ToString("F3") : "<null>"));
                }
            }
            AppendResult(sb.ToString());
            Debug.Log("Cunning CDA scene probe wrote " + Path.GetFullPath(ResultPath));
        }

        static void WriteResult(string text) {
            Directory.CreateDirectory(Path.GetDirectoryName(ResultPath));
            File.WriteAllText(ResultPath, text, Encoding.UTF8);
        }

        static void AppendResult(string text) {
            Directory.CreateDirectory(Path.GetDirectoryName(ResultPath));
            File.AppendAllText(ResultPath, text + Environment.NewLine, Encoding.UTF8);
        }
    }
}
