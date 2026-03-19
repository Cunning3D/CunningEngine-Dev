 using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif
public class ExampleProceduralBuilding : MonoBehaviour
{

    public List<Transform> points = new List<Transform>();
    public DevStuffs.BuildPreset preset;
    public Material roofMaterial;
    public int stagesCount = 9;
    // Start is called before the first frame update
    public void CreateBuilding()
    {
        var dataStruc = new DevStuffs.ProceduralBuilding.BuildingData()
        {
            roofMaterial = this.roofMaterial,
            stagesCount = this.stagesCount,
            windowEmissionColor = Color.white * 2f,
            windowEmissionValue01 = 0.6f,
            preset = this.preset,
            //
            points = points.Select(p => p.position).ToList(),
        };

        var myNewBuilding = DevStuffs.ProceduralBuilding.CreateNewBuilding(dataStruc);
    }
}


#if UNITY_EDITOR
[CustomEditor(typeof(ExampleProceduralBuilding))]
public class ExampleProceduralBuildingEditor : Editor {
    public override void OnInspectorGUI() {
        base.OnInspectorGUI();
        if (GUILayout.Button("Create"))
        {
            (target as ExampleProceduralBuilding).CreateBuilding();
        }
    }
}
#endif