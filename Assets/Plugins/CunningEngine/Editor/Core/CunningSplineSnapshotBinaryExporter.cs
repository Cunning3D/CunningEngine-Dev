using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace CunningEngine.Editor
{
    public static class CunningSplineSnapshotBinaryExporter
    {
        [MenuItem("Cunning/Splines/Export Snapshot (FBS)")]
        static void ExportSnapshotFbs()
        {
            var go = Selection.activeGameObject;
            if (go == null) { EditorUtility.DisplayDialog("Spline Snapshot", "Select a GameObject with SplineContainer.", "OK"); return; }
            var c = go.GetComponent<SplineContainer>();
            if (c == null) { EditorUtility.DisplayDialog("Spline Snapshot", "Selected GameObject has no SplineContainer.", "OK"); return; }

            var path = EditorUtility.SaveFilePanel("Export Spline Snapshot (FBS)", Application.dataPath, "spline_snapshot.fbs", "fbs");
            if (string.IsNullOrEmpty(path)) return;

            File.WriteAllBytes(path, CunningSplineSnapshotFbs.BuildFbs(c));
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Spline Snapshot", $"Exported:\n{path}", "OK");
        }
    }
}

