#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine.Splines;
using Miao.RoadSystem.Culling;
public class RoadTrafficDataContainer
{
    public RoadNetworksContainer Traffic { get; set; }
}

public class RoadNetworksContainer
{
    public List<RoadSegmentInfo> road_lane { get; set; }
    public List<RoadSegmentInfo> road_inter { get; set; }
}

public class RoadSegmentInfo
{
    public int primnum { get; set; }
    public List<PointInfo> points { get; set; }
    public string direction { get; set; }
    public string roadType { get; set; }
}

public class PointInfo
{
    public float x { get; set; }
    public float y { get; set; }
    public float z { get; set; }
}

public class SplineExporterWindow : EditorWindow
{
    private string exportFilePath = "Assets/PackResources/Config/Traffic/FL/road_traffic.json";

    [MenuItem("Procedural/道路系统/导出交通")]
    public static void ShowWindow()
    {
        GetWindow<SplineExporterWindow>("Road Spline Exporter");
    }

    private void OnGUI()
    {
        GUILayout.Label("Export Splines to JSON", EditorStyles.boldLabel);
        exportFilePath = EditorGUILayout.TextField("Export File Path", exportFilePath);

        if (GUILayout.Button("Export"))
        {
            ExportSplinesToJson(exportFilePath);
        }
    }

    private void ExportSplinesToJson(string filePath)
    {
        var roadTrafficData = new RoadTrafficDataContainer
        {
            Traffic = new RoadNetworksContainer
            {
                road_lane = new List<RoadSegmentInfo>(),
                road_inter = new List<RoadSegmentInfo>()
            }
        };

        GameObject roadLaneNode = GameObject.Find("Road Lane");
        if (roadLaneNode != null)
        {
            foreach (Transform laneIdNode in roadLaneNode.transform)
            {
                foreach (Transform splineObject in laneIdNode)
                {
                    var splineContainer = splineObject.GetComponent<SplineContainer>();
                    if (splineContainer == null) continue;

                    var segment = new RoadSegmentInfo
                    {
                        primnum = int.Parse(splineObject.name.Split(' ')[2]),
                        points = new List<PointInfo>(),
                        direction = "forward",
                        roadType = laneIdNode.name
                    };

                    var spline = splineContainer.Spline;
                    foreach (var knot in spline)
                    {
                        segment.points.Add(new PointInfo { x = knot.Position.x  / -1 , y = knot.Position.y, z = knot.Position.z });
                    }

                    roadTrafficData.Traffic.road_lane.Add(segment);
                }
            }
        }

        GameObject roadInterNode = GameObject.Find("Road Intersection");
        if (roadInterNode != null)
        {
            foreach (Transform interIdNode in roadInterNode.transform)
            {
                foreach (Transform splineObject in interIdNode)
                {
                    var splineContainer = splineObject.GetComponent<SplineContainer>();
                    if (splineContainer == null) continue;

                    var segment = new RoadSegmentInfo
                    {
                        primnum = int.Parse(splineObject.name.Split(' ')[2]),
                        points = new List<PointInfo>(),
                        direction = "",
                        roadType = interIdNode.name
                    };

                    var spline = splineContainer.Spline;
                    foreach (var knot in spline)
                    {
                        segment.points.Add(new PointInfo { x = knot.Position.x / -1 , y = knot.Position.y, z = knot.Position.z });
                    }

                    roadTrafficData.Traffic.road_inter.Add(segment);
                }
            }
        }

        string jsonData = JsonConvert.SerializeObject(roadTrafficData, Formatting.Indented);
        File.WriteAllText(filePath, jsonData);

        Debug.Log("Spline data exported successfully to " + filePath);
    }
}
#endif