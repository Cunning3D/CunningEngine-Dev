using UnityEngine;

[CreateAssetMenu(fileName = "NewRoadInfo", menuName = "RoadSystem/Custom Road Info")]
public class RoadInfo : ScriptableObject
{
    public string roadName;
    public string roadCName;
    public float defaultWidth;
    public bool isOneWay;
    public int leftLaneCount;
    public int rightLaneCount;
    public Material roadMaterial;
    public Material sidewalkMaterial;
    public Material greenBeltMaterial;
    
    // 路缘（Curb）参数
    public Material curbMaterial;
    [Range(0.05f, 1f)]
    [Tooltip("路缘高度")]
    public float curbHeight = 0.1f;
    [Range(0.1f, 2f)]
    [Tooltip("路缘宽度")]
    public float curbWidth = 1.0f;
    public bool enableCurb = true;
    [Range(0.1f, 10f)]
    [Tooltip("路缘U方向纹理缩放")]
    public float curbUVScaleU = 0.5f;
    [Range(0.1f, 10f)]
    [Tooltip("路缘V方向纹理缩放")]
    public float curbUVScaleV = 1.0f;
    [Range(0.01f, 0.5f)]
    [Tooltip("路缘内侧倒角宽度")]
    public float curbChamferWidth = 0.05f;
    [Range(0.01f, 0.5f)]
    [Tooltip("路缘内侧倒角高度")]
    public float curbChamferHeight = 0.05f;
    
    // 马路牙子（Road Edge）参数
    public bool enableRoadEdge = true;
    [Range(0.05f, 1f)]
    [Tooltip("马路牙子高度")]
    public float roadEdgeHeight = 0.15f;
    [Range(0.1f, 2f)]
    [Tooltip("马路牙子宽度")]
    public float roadEdgeWidth = 0.3f;
    public Material roadEdgeMaterial;
    [Range(0.1f, 10f)]
    [Tooltip("马路牙子U方向纹理缩放")]
    public float roadEdgeUVScaleU = 0.5f;
    [Range(0.1f, 10f)]
    [Tooltip("马路牙子V方向纹理缩放")]
    public float roadEdgeUVScaleV = 1.0f;
    [Range(0.01f, 0.5f)]
    [Tooltip("马路牙子内侧倒角宽度")]
    public float roadEdgeChamferWidth = 0.05f;
    [Range(0.01f, 0.5f)]
    [Tooltip("马路牙子内侧倒角高度")]
    public float roadEdgeChamferHeight = 0.05f;
    
    public float defaultSidewalkWidth;
    public float defaultLaneWidth;
    public float defaultGreenBeltWidth;
}