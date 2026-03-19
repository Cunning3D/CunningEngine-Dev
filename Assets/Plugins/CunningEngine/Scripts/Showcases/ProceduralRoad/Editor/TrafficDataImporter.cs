#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine.Splines;
using Miao.RoadSystem.Culling;

// 定义道路交通数据类
public class RoadTrafficData
{
    public RoadTraffic Traffic { get; set; }
}

public class RoadTraffic
{
    public Dictionary<string, List<RoadPrimData>> road_lane { get; set; }
    public Dictionary<string, List<RoadPrimData>> road_inter { get; set; }
}

public class RoadPrimData
{
    public int primnum { get; set; }
    public List<RoadPointData> points { get; set; }
    public string direction { get; set; }
    public string road_type { get; set; }
}

public class RoadPointData
{
    public float x { get; set; }
    public float y { get; set; }
    public float z { get; set; }
}


public class RoadSplineImporterWindow : EditorWindow
{
    private string jsonFilePath = "Assets/ArtResources/Hdas/RoadSystem/Caches/road_traffic_ta.json";

    [MenuItem("Procedural/道路系统/导入交通", false, 10)]
    public static void ShowWindow()
    {
        GetWindow<RoadSplineImporterWindow>("Road Spline Importer");
    }

    private void OnGUI()
    {
        GUILayout.Label("Import Splines from JSON", EditorStyles.boldLabel);
        jsonFilePath = EditorGUILayout.TextField("JSON File Path", jsonFilePath);

        if (GUILayout.Button("Import"))
        {
            ImportSplinesFromJson(jsonFilePath);
        }
    }

    private void ImportSplinesFromJson(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Debug.LogError("The JSON file path does not exist.");
            return;
        }

        string jsonData = File.ReadAllText(filePath);
        RoadTrafficData roadTrafficData = JsonConvert.DeserializeObject<RoadTrafficData>(jsonData);

        GameObject root = new GameObject("Root");

        GameObject roadLaneNode = new GameObject("Road Lane");
        roadLaneNode.transform.parent = root.transform;

        GameObject roadInterNode = new GameObject("Road Intersection");
        roadInterNode.transform.parent = root.transform;

        foreach (var laneKvp in roadTrafficData.Traffic.road_lane)
        {
            string laneId = laneKvp.Key;
            List<RoadPrimData> primList = laneKvp.Value;

            GameObject laneIdNode = new GameObject(laneId);
            laneIdNode.transform.parent = roadLaneNode.transform;

            Bounds laneBounds = new Bounds(Vector3.zero, Vector3.zero);

            foreach (var prim in primList)
            {
                GameObject splineObject = new GameObject($"Lane Prim {prim.primnum}");
                splineObject.transform.parent = laneIdNode.transform;

                var splineContainer = splineObject.AddComponent<SplineContainer>();
                var spline = splineContainer.Spline;

                foreach (var pt in prim.points)
                {
                    Vector3 point = new Vector3(pt.x * -1, pt.y, pt.z);
                    spline.Insert(spline.Count, new BezierKnot(point));
                }

                if (spline.Count > 0)
                {
                    Bounds primBounds = new Bounds(spline[0].Position, Vector3.zero);
                    for (int i = 1; i < spline.Count; i++)
                    {
                        primBounds.Encapsulate(spline[i].Position);
                    }
                    if (laneBounds.size == Vector3.zero)
                        laneBounds = primBounds;
                    else
                        laneBounds.Encapsulate(primBounds);
                }
            }

            var boxCollider = laneIdNode.AddComponent<BoxCollider>();
            boxCollider.size = laneBounds.size;
            boxCollider.center = laneBounds.center;
        }

        foreach (var interKvp in roadTrafficData.Traffic.road_inter)
        {
            string interId = interKvp.Key;
            List<RoadPrimData> primList = interKvp.Value;

            GameObject interIdNode = new GameObject(interId);
            interIdNode.transform.parent = roadInterNode.transform;

            Bounds interBounds = new Bounds(Vector3.zero, Vector3.zero);

            foreach (var prim in primList)
            {
                GameObject splineObject = new GameObject($"Inter Prim {prim.primnum}");
                splineObject.transform.parent = interIdNode.transform;

                var splineContainer = splineObject.AddComponent<SplineContainer>();
                var spline = splineContainer.Spline;

                foreach (var pt in prim.points)
                {
                    Vector3 point = new Vector3(pt.x * -1, pt.y, pt.z);
                    spline.Insert(spline.Count, new BezierKnot(point));
                }

                if (spline.Count > 0)
                {
                    Bounds primBounds = new Bounds(spline[0].Position, Vector3.zero);
                    for (int i = 1; i < spline.Count; i++)
                    {
                        primBounds.Encapsulate(spline[i].Position);
                    }
                    if (interBounds.size == Vector3.zero)
                        interBounds = primBounds;
                    else
                        interBounds.Encapsulate(primBounds);
                }
            }

            var boxCollider = interIdNode.AddComponent<BoxCollider>();
            boxCollider.size = interBounds.size;
            boxCollider.center = interBounds.center;
        }

        EditorApplication.delayCall += () =>
        {
            if (root != null)
            {
                root.AddComponent<RoadCullingMultithreaded>();
                Debug.Log("FrustumCullingMultithreadedExample component added successfully!");
            }
        };

        Debug.Log("Spline creation completed successfully!");
    }
}
#endif