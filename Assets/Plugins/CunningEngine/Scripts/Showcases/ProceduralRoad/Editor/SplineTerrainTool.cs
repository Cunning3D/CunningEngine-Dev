using UnityEngine;
using UnityEditor;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEngine.Splines;
using Unity.Splines.Examples;
using Unity.Mathematics;

public class SplineTerrainTool : OdinEditorWindow
{
    [FoldoutGroup("道路示意线")]
    public GameObject splineObj;
    
    [FoldoutGroup("道路示意线")]
    [ProgressBar(0, 1)]
    [LabelText("贴合强度")]
    public float heightAdjustStrength = 0.5f;


    [FoldoutGroup("道路示意线")]
    [LabelText("路面之外调整半径")]
    public float adjustmentRadius = 1f;

    [FoldoutGroup("道路示意线")]
    [LabelText("最大调整差")]
    [MinValue(0)]
    public float maxAdjustAbsHeight = 20f;

    public enum BlendType
    {
        None,
        Min,
        Max
    }

    public enum OverMaxAngleType
    {
        None,
        Clamp,
        Flat
    }

    public enum BrushStatus
    {
        Enable,
        Disable
    }

    private const float radiusMin = 0.1f;
    private const float radiusMax = 50.0f;
    private const float falloffMin = 0.01f;
    private const float falloffMax = 0.99f;
    private const float spacingMin = 0.01f;
    private const float spacingMax = 2.0f;
    private const float maxAngleMin = 10.0f;
    private const float maxAngleMax = 80.0f;
    private const float offsetYMin = -1.0f;
    private const float offsetYMax = 1.0f;


    [SerializeField] 
    [FoldoutGroup("预览道路")]
    [TitleGroup("预览道路/Terrain数据")]
    [LabelText("目标Terrain")]
    [OnValueChanged("OnTerrainChanged")]
    private Terrain tRender;
    
    [SerializeField] 
    [FoldoutGroup("预览道路")]
    [TitleGroup("预览道路/Terrain数据")]
    [LabelText("目标Terrain的TerrainData")]
    private TerrainData tData;

    [SerializeField] 
    [FoldoutGroup("预览道路")]
    [TitleGroup("预览道路/Terrain数据")]
    [LabelText("目标Terrain的TerrainCollider")]
    private TerrainCollider tCollider;

    [LabelText("Terrian To Collider笔刷状态")]
    [FoldoutGroup("预览道路")]
    [TitleGroup("预览道路/Terrain数据")]
    [EnumToggleButtons]
    public BrushStatus paintActive = BrushStatus.Disable;

    private void OnTerrainChanged()
    {
        if (tRender != null)
        {
            tCollider = tRender.GetComponent<TerrainCollider>();
            if (tCollider != null)
                tData = tCollider.terrainData;
        }
    }

    [SerializeField] 
    [FoldoutGroup("预览道路")]
    [TitleGroup("预览道路/笔刷设置")]
    [Tooltip("Set the brush radius in meters.")]
    [LabelText("笔刷半径")]
    [ProgressBar(0, 50)]
    private float radius = 3.0f;

    [SerializeField] 
    [FoldoutGroup("预览道路")]
    [TitleGroup("预览道路/笔刷设置")]
    [ProgressBar(0, 1)]
    [Tooltip("Set the fall of the brush relative to the Radius.")]
    [LabelText("平滑率")]
    private float falloff = 0.6f;

    [SerializeField] 
    [FoldoutGroup("预览道路")]
    [TitleGroup("预览道路/笔刷设置")]
    [Tooltip("Set the brush stroke frequency in meters")]
    [LabelText("笔刷效果间距")]
    [ProgressBar(0, 2)]
    private float spacing = 0.5f;

    [SerializeField]
    [FoldoutGroup("预览道路")]
    [TitleGroup("预览道路/笔刷设置")]
    [ProgressBar(0, 90)]
    [Tooltip("Limits the maximum working angle of the brush.")]
    [LabelText("面法线与Y轴方向的角度差")]
    private float maxAngle = 60.0f;
    
    [SerializeField] 
    [FoldoutGroup("预览道路")]
    [TitleGroup("预览道路/笔刷设置")]
    [ProgressBar(-1, 1)]
    [Tooltip("Offset the brush adjust height in meters.")]
    [LabelText("Terrain额外适应高度")]
    private float offsetY = -0.01f;

    [SerializeField] 
    [FoldoutGroup("预览道路")]
    [TitleGroup("预览道路/Terrain混合设置")]
    [EnumToggleButtons]
    [LabelText("混合模式(取调整前后高or低)")]
    private BlendType blend = BlendType.None;

    [SerializeField] 
    [FoldoutGroup("预览道路")]
    [TitleGroup("预览道路/Terrain混合设置")]
    [EnumToggleButtons]
    [LabelText("超过最大角度后的处理模式")]
    private OverMaxAngleType overMaxAngle = OverMaxAngleType.None;
    
    [SerializeField] 
    [FoldoutGroup("预览道路")]
    [TitleGroup("预览道路/被检测Collider设置")]
    [LabelText("目标Collider Layer")]
    private LayerMask targetMask = ~0;

    [SerializeField] 
    [FoldoutGroup("预览道路")]
    [TitleGroup("预览道路/被检测Collider设置")]
    [Tooltip("Collider最大检测高度")]
    [LabelText("Collider最大检测高度")]
    [SuffixLabel("Meter")]
    private float maxDistance = 1000.0f;

    [SerializeField] 
    [FoldoutGroup("预览道路")]
    [TitleGroup("预览道路/被检测Collider设置")]
    [LabelText("忽略Terrain Collider")]
    private bool excludeTerrains = true;

    private AnimationCurve brushCurve;
    private Vector3 adjustPos;
    private Vector3 adjustNormal;
    private Vector3 lastAdjustPos;


    private Vector2 viewScroll;
    private bool foldoutBrush;
    private bool foldoutTarget;
    private bool foldoutSettings;
    private Vector2 shortcutMousePos;


    #region INITIALIZATION

    public void OnEnable ()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        Undo.undoRedoPerformed += OnUndoRedo;
        InitTerrainConnect();
    }

    public void OnDisable ()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        Undo.undoRedoPerformed -= OnUndoRedo;
    }

    public void OnUndoRedo ()
    {
        if (tData)
        {
            tData.SyncHeightmap();
        }

        Repaint();
    }
    #endregion

    #region MAIN
    // public void OnGUI ()
    // {
    //     base.OnGUI();
    //     if (!tRender || !tData || !tCollider)
    //     {
    //         EditorGUILayout.Space(10);
    //         tRender = (Terrain) EditorGUILayout.ObjectField("Terrain", tRender, typeof(Terrain), true);

    //         if (tRender == null)
    //         {
    //             EditorGUILayout.Space(10);
    //             EditorGUILayout.HelpBox("No Terrain connected!", MessageType.Warning);
    //             return;
    //         }

    //         InitTerrainConnect();

    //         if (tData == null)
    //         {
    //             EditorGUILayout.Space(10);
    //             EditorGUILayout.HelpBox("Terrain Data not available on the target Terrain! Try to fix your Terrain in Debug mode, or select another one!", MessageType.Error);
    //             return;
    //         }

    //         if (tCollider == null)
    //         {
    //             EditorGUILayout.Space(10);
    //             EditorGUILayout.HelpBox("No Terrain Collider component attached to the target Terrain!", MessageType.Warning);
    //             if (GUILayout.Button("Add Terrain Collider"))
    //             {
    //                 FixTerrainConnect();
    //             }
    //             return;
    //         }
    //     }

    //     //PAINT AREA
    //     EditorGUILayout.Space(10);
    //     paintActive = GUILayout.Toggle(paintActive, "Paint", new GUIStyle("Button") { font = EditorStyles.boldFont, fontSize = 14 }, GUILayout.MinHeight(30));

    //     //SCROLL VIEW START
    //     EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
    //     viewScroll = EditorGUILayout.BeginScrollView(viewScroll);

    //     //BRUSH AREA
    //     foldoutBrush = EditorGUILayout.BeginFoldoutHeaderGroup(foldoutBrush, "Brush");
    //     if (foldoutBrush)
    //     {
    //         EditorGUI.BeginChangeCheck();
    //         float _radius = EditorGUILayout.Slider(new GUIContent("        Radius", "Set the brush radius in meters."), radius, radiusMin, radiusMax);
    //         float _falloff = EditorGUILayout.Slider(new GUIContent("        Falloff", "Set the fall of the brush relative to the Radius."), falloff, falloffMin, falloffMax);
    //         float _spacing = EditorGUILayout.Slider(new GUIContent("        Spacing", "Set the brush stroke frequency in meters."), spacing, spacingMin, spacingMax);
    //         float _maxAngle = EditorGUILayout.Slider(new GUIContent("        Max Angle", "Limits the maximum working angle of the brush."), maxAngle, maxAngleMin, maxAngleMax);
    //         float _offsetY = EditorGUILayout.Slider(new GUIContent("        Vertical Offset", "Offset the brush adjust height in meters."), offsetY, offsetYMin, offsetYMax);
    //         if (EditorGUI.EndChangeCheck())
    //         {
    //             Undo.RecordObject(this, "Terrain Adjust Inspector");
    //             radius = _radius;
    //             falloff = _falloff;
    //             spacing = _spacing;
    //             maxAngle = _maxAngle;
    //             offsetY = _offsetY;
    //         }
    //     }
    //     EditorGUILayout.EndFoldoutHeaderGroup();

    //     //BLEND AREA
    //     EditorGUILayout.Space(10);
    //     EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
    //     EditorGUI.BeginChangeCheck();
    //     GUILayout.Label(new GUIContent("Blend", "Limit the blending of height adjust.\n\nNone:\tOverride height.\nMin:\tSet height if original higher.\nMax:\tSet height if original lower."), EditorStyles.boldLabel);
    //     BlendType _blend = (BlendType) GUILayout.Toolbar((int) blend, System.Enum.GetNames(typeof(BlendType)));
    //     if (EditorGUI.EndChangeCheck())
    //     {
    //         Undo.RecordObject(this, "Terrain Adjust Inspector");
    //         blend = _blend;
    //     }

    //     //ANGLE AREA
    //     EditorGUILayout.Space(10);
    //     EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
    //     EditorGUI.BeginChangeCheck();
    //     GUILayout.Label(new GUIContent("Over Max Angle", "Set behavior of angle limitation.\n\nNone:\tForbid the brush usage.\nClamp:\tChange angle to maximum allowed.\nFlat:\tChange angle to zero."), EditorStyles.boldLabel);
    //     OverMaxAngleType _overMaxAngle = (OverMaxAngleType) GUILayout.Toolbar((int) overMaxAngle, System.Enum.GetNames(typeof(OverMaxAngleType)));
    //     if (EditorGUI.EndChangeCheck())
    //     {
    //         Undo.RecordObject(this, "Terrain Adjust Inspector");
    //         overMaxAngle = _overMaxAngle;
    //     }

    //     //TARGET AREA
    //     EditorGUILayout.Space(10);
    //     EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
    //     foldoutTarget = EditorGUILayout.BeginFoldoutHeaderGroup(foldoutTarget, "Target");
    //     if (foldoutTarget)
    //     {
    //         EditorGUI.BeginChangeCheck();
    //         LayerMask _targetMask = InternalEditorUtility.ConcatenatedLayersMaskToLayerMask(EditorGUILayout.MaskField(new GUIContent("        Target Mask", "Masking the target collider selection."), InternalEditorUtility.LayerMaskToConcatenatedLayersMask(targetMask), InternalEditorUtility.layers));
    //         float _maxDistance = EditorGUILayout.FloatField(new GUIContent("        Max Distance", "Limit the distance of the target collider selection in meters."), maxDistance);
    //         bool _excludeTerrains = EditorGUILayout.Toggle(new GUIContent("        Exclude Terrains", "Exclude Terrain Collider type from selection."), excludeTerrains);
    //         if (EditorGUI.EndChangeCheck())
    //         {
    //             Undo.RecordObject(this, "Terrain Adjust Inspector");
    //             targetMask = _targetMask;
    //             maxDistance = Mathf.Max(_maxDistance, 1.0f);
    //             excludeTerrains = _excludeTerrains;
    //         }
    //     }
    //     EditorGUILayout.EndFoldoutHeaderGroup();

    //     //SETTINGS AREA
    //     EditorGUILayout.Space(10);
    //     EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
    //     foldoutSettings = EditorGUILayout.BeginFoldoutHeaderGroup(foldoutSettings, "Settings");
    //     if (foldoutSettings)
    //     {
    //         EditorGUI.BeginChangeCheck();
    //         Terrain _tRender = (Terrain) EditorGUILayout.ObjectField("        Terrain", tRender, typeof(Terrain), true);
    //         TerrainData _tData = (TerrainData) EditorGUILayout.ObjectField("        Data", tData, typeof(TerrainData), false);
    //         TerrainCollider _tCollider = (TerrainCollider) EditorGUILayout.ObjectField("        Collider", tCollider, typeof(TerrainCollider), true);
    //         EditorGUILayout.Space(20);
    //         if (EditorGUI.EndChangeCheck())
    //         {
    //             Undo.RecordObject(this, "Terrain Adjust Inspector");
    //             tRender = _tRender;
    //             tData = _tData;
    //             tCollider = _tCollider;
    //         }
    //     }
    //     EditorGUILayout.EndFoldoutHeaderGroup();

    //     //SCROLL VIEW END
    //     EditorGUILayout.Space(20);
    //     EditorGUILayout.EndScrollView();

    //     //HELP AREA
    //     EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
    //     if(radius * 0.5f < Mathf.Max(tData.size.x, tData.size.z) / tData.heightmapResolution)
    //     {
    //         EditorGUILayout.HelpBox("Terrain heightmap resolution too small\nfor this brush radius! Maybe the brush has no effect.", MessageType.Warning);
    //     }
    //     EditorGUILayout.HelpBox("Radius:\tCtrl + Right Mouse + Horizontal move\nFalloff:\tCtrl + Right Mouse + Vertical move", MessageType.None);
    //     EditorGUILayout.Space(10);
    // }

    public void OnSceneGUI (SceneView sceneView)
    {
        if (paintActive != BrushStatus.Enable)
            return;

        if (Event.current.type == EventType.Layout)
        {
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(GetHashCode(), FocusType.Passive));
        }

        if (!new Rect(0, 0, Camera.current.pixelWidth, Camera.current.pixelHeight).Contains(HandleUtility.GUIPointToScreenPixelCoordinate(Event.current.mousePosition)))
            return;

        if (Event.current.modifiers == EventModifiers.Control)
        {
            if (Event.current.type == EventType.MouseDown)
            {
                shortcutMousePos = Event.current.mousePosition;
                Undo.RegisterCompleteObjectUndo(this, "Terrain Adjust Inspector Shortcut");
            }

            if (Event.current.button == 1 && Event.current.type == EventType.MouseDrag)
            {
                Vector2 delta = ConstraitShortcutVector(shortcutMousePos, Event.current.mousePosition, Event.current.delta);
                radius = Mathf.Clamp(radius + delta.x * 0.02f, radiusMin, radiusMax);
                falloff = Mathf.Clamp(falloff - delta.y * 0.01f, falloffMin, falloffMax);
                Repaint();
                Event.current.Use();
            }

            DrawGizmo(true, adjustPos, adjustNormal);
            sceneView.Repaint();
            return;
        }

        RaycastHit hit;
        if (excludeTerrains)
        {
            if (!RaycastExcludeTerrain(HandleUtility.GUIPointToWorldRay(Event.current.mousePosition), out hit))
                return;
        }
        else
        {
            if (!RaycastIncludeTerrain(HandleUtility.GUIPointToWorldRay(Event.current.mousePosition), out hit))
                return;
        }

        adjustPos = hit.point;
        bool isAvailable = OverAngleCorrection(hit.normal, out adjustNormal);

        DrawGizmo(isAvailable, adjustPos, adjustNormal);
        sceneView.Repaint();

        if (Event.current.button == 0 && Event.current.type == EventType.MouseDown)
        {
            Undo.RegisterCompleteObjectUndo(tData, "Terrain Adjust");

            if (!isAvailable)
            {
                Event.current.Use();
                return;
            }

            AdjustHeight(adjustPos, adjustNormal);
            Event.current.Use();
        }

        if (Event.current.button == 0 && Event.current.type == EventType.MouseDrag)
        {
            if (!isAvailable || Vector3.Distance(adjustPos, lastAdjustPos) < spacing)
            {
                Event.current.Use();
                return;
            }

            AdjustHeight(adjustPos, adjustNormal);
            Event.current.Use();
        }
    }
    #endregion

    #region TERRAIN_CALCULATIONS
    private void AdjustHeight (Vector3 point, Vector3 normal)
    {
        brushCurve = new AnimationCurve(new Keyframe(0.0f, 1.0f), new Keyframe(falloff, 1.0f), new Keyframe(1.0f, 0.0f));

        Vector3 localPosMin = tRender.transform.InverseTransformPoint(point + new Vector3(-radius, 0.0f, -radius));
        Vector3 localPosMax = tRender.transform.InverseTransformPoint(point + new Vector3(radius, 0.0f, radius));

        Vector3 ratePosMin = new Vector3(
            Mathf.Clamp01(localPosMin.x / tData.size.x),
            Mathf.Clamp01(localPosMin.y / tData.size.y),
            Mathf.Clamp01(localPosMin.z / tData.size.z)
        );
        Vector3 ratePosMax = new Vector3(
            Mathf.Clamp01(localPosMax.x / tData.size.x),
            Mathf.Clamp01(localPosMax.y / tData.size.y),
            Mathf.Clamp01(localPosMax.z / tData.size.z)
        );

        Vector2Int coordMin = new Vector2Int(Mathf.FloorToInt(ratePosMin.x * tData.heightmapResolution), Mathf.FloorToInt(ratePosMin.z * tData.heightmapResolution));
        Vector2Int coordMax = new Vector2Int(Mathf.FloorToInt(ratePosMax.x * tData.heightmapResolution), Mathf.FloorToInt(ratePosMax.z * tData.heightmapResolution));

        int sizeX = coordMax.x - coordMin.x;
        int sizeY = coordMax.y - coordMin.y;

        float[,] heights = tData.GetHeights(coordMin.x, coordMin.y, sizeX, sizeY);

        for (int x = 0; x < sizeX; x++)
        {
            for (int y = 0; y < sizeY; y++)
            {
                heights[y, x] = EvaluateBrush(
                    (coordMin.x + x) / (float) (tData.heightmapResolution - 1),
                    (coordMin.y + y) / (float) (tData.heightmapResolution - 1),
                    heights[y, x],
                    point,
                    normal
                );
            }
        }

        tData.SetHeights(coordMin.x, coordMin.y, heights);
        lastAdjustPos = point;
    }

    private float EvaluateBrush (float rateX, float rateY, float lastHeight, Vector3 point, Vector3 normal)
    {
        Vector3 realPos = tRender.transform.TransformPoint(new Vector3(rateX * tData.size.x, lastHeight * tData.size.y, rateY * tData.size.z));
        Vector3 vOriginal = new Vector3(realPos.x, 0.0f, realPos.z) - new Vector3(point.x, 0.0f, point.z);

        float heightX = Mathf.Tan(Mathf.Atan2(-normal.x, normal.y)) * vOriginal.x;
        float heightZ = Mathf.Tan(Mathf.Atan2(normal.z, normal.y)) * vOriginal.z;
        Vector3 vProjected = new Vector3(vOriginal.x, heightX - heightZ, vOriginal.z);

        float targetHeight = (point.y + vProjected.y + offsetY - tRender.transform.position.y) / tData.size.y;

        targetHeight = Mathf.Lerp(lastHeight, targetHeight, brushCurve.Evaluate(Mathf.Clamp01(vProjected.magnitude / radius)));

        if (blend == BlendType.Min)
            return Mathf.Min(lastHeight, targetHeight);

        if (blend == BlendType.Max)
            return Mathf.Max(lastHeight, targetHeight);

        return targetHeight;
    }
    #endregion

    #region GIZMOS
    private void DrawGizmo (bool enabled, Vector3 pos, Vector3 up)
    {
        Handles.color = enabled ? new Color(0.0f, 1.0f, 1.0f, 0.1f) : new Color(1.0f, 0.0f, 0.0f, 0.1f);
        Handles.DrawSolidDisc(pos, up, radius * falloff);
        Handles.color = enabled ? Color.cyan : Color.red;
        Handles.DrawWireDisc(pos, up, radius);
    }
    #endregion

    #region UTILITIES
    private void InitTerrainConnect ()
    {
        //Terrain
        if (tRender == null)
        {
            tRender = FindObjectOfType<Terrain>();
            if (tRender == null)
                return;
        }

        //Terrain data
        tData = tRender.terrainData;
        if (tData == null)
            return;

        //Terrain collider
        tCollider = tRender.GetComponent<TerrainCollider>();
        if (tCollider == null)
            return;

        tCollider.terrainData = tData;
        tCollider.enabled = true;
    }

    private void FixTerrainConnect ()
    {
        if (!tRender || !tData)
            return;

        tCollider = tRender.gameObject.AddComponent<TerrainCollider>();
        tCollider.terrainData = tData;
        tCollider.enabled = true;
    }

    private bool RaycastIncludeTerrain (Ray ray, out RaycastHit hit)
    {
        return Physics.Raycast(ray, out hit, maxDistance, targetMask);
    }

    private bool RaycastExcludeTerrain (Ray ray, out RaycastHit hit)
    {
        RaycastHit[] allHits = Physics.RaycastAll(ray, maxDistance, targetMask);

        if (allHits.Length == 0)
        {
            hit = new RaycastHit();
            return false;
        }

        int nearestIndex = -1;
        float nearestDistance = maxDistance;
        for (int i = 0; i < allHits.Length; i++)
        {
            if (allHits[i].collider == tCollider)
                continue;

            if (allHits[i].distance < nearestDistance)
            {
                nearestIndex = i;
                nearestDistance = allHits[i].distance;
            }
        }

        if (nearestIndex == -1)
        {
            hit = new RaycastHit();
            return false;
        }

        hit = allHits[nearestIndex];
        return true;
    }

    private bool OverAngleCorrection (Vector3 currentNormal, out Vector3 correctedNormal)
    {
        float currentAngle = Vector3.Angle(currentNormal, Vector3.up);

        if (overMaxAngle == OverMaxAngleType.Clamp)
        {
            if (currentAngle > maxAngle)
            {
                Quaternion currentRot = Quaternion.LookRotation(currentNormal, Vector3.forward);
                Quaternion targetRot = Quaternion.LookRotation (Vector3.up, Vector3.forward);
                correctedNormal = Quaternion.Slerp(targetRot, currentRot, maxAngle / currentAngle) * Vector3.forward;
                return true;
            }

            correctedNormal = currentNormal;
            return true;
        }

        if (overMaxAngle == OverMaxAngleType.Flat)
        {
            if (currentAngle > maxAngle)
            {
                correctedNormal = Vector3.up;
                return true;
            }

            correctedNormal = currentNormal;
            return true;
        }

        correctedNormal = currentNormal;
        return currentAngle <= maxAngle;
    }

    private Vector2 ConstraitShortcutVector (Vector2 start, Vector2 current, Vector2 delta)
    {
        Vector2 move = current - start;
        if (Mathf.Abs(move.x) > Mathf.Abs(move.y))
        {
            return new Vector2(delta.x, 0.0f);
        }
        else
        {
            return new Vector2(0.0f, delta.y);
        }
    }
    #endregion
    

    [MenuItem("Tools/美术工具/美术地编工具/路网Terrain适应Tool")]
    public static void ShowWindow()
    {
        var window = GetWindow<SplineTerrainTool>("Spline Terrain Tool");
        window.foldoutBrush = true;
        window.foldoutTarget = true;
        window.foldoutSettings = false;
    }

    [Button("Sync Origin Terrain")]
    [FoldoutGroup("道路示意线")]
    private void SyncTerrain()
    {
        LoftRoadBehaviour roadBehaviour = splineObj.GetComponent<LoftRoadBehaviour>();
        if (roadBehaviour == null)
        {
            Debug.LogWarning("The Spline Container does not have a LoftRoadBehaviour attached.");
            return;
        }
        Terrain[] terrains = Terrain.activeTerrains;

        var pos = splineObj.GetComponent<MeshRenderer>().bounds.center;
        Terrain locatedTerrain = FindTerrainUnderObject(pos, terrains);

        roadBehaviour.RoadExtensionDatas[0].SyncTerrainData(locatedTerrain.terrainData);
    }

    [Button("Reset Terrain To Origin")]
    [FoldoutGroup("道路示意线")]
    private void ResetToOriginTerrain()
    {
        LoftRoadBehaviour roadBehaviour = splineObj.GetComponent<LoftRoadBehaviour>();
        if (roadBehaviour == null)
        {
            Debug.LogWarning("The Spline Container does not have a LoftRoadBehaviour attached.");
            return;
        }
        Terrain[] terrains = Terrain.activeTerrains;

        var pos = splineObj.GetComponent<MeshRenderer>().bounds.center;
        Terrain locatedTerrain = FindTerrainUnderObject(pos, terrains);

        var heights = roadBehaviour.RoadExtensionDatas[0].GetTerrainHeights();
        locatedTerrain.terrainData.SetHeights(0, 0, heights);
    }

    [Button("Adjust Terrain")]
    [FoldoutGroup("道路示意线")]
    void AdjustTerrainToRoad()
    {
        LoftRoadBehaviour roadBehaviour = splineObj.GetComponent<LoftRoadBehaviour>();
        if (roadBehaviour == null)
        {
            Debug.LogWarning("The Spline Container does not have a LoftRoadBehaviour attached.");
            return;
        }

        // highway type do not modify terrain
        if (roadBehaviour.RoadExtensionDatas[0].IsHighWayType()) return;

        Terrain[] terrains = Terrain.activeTerrains;

        var pos = splineObj.GetComponent<MeshRenderer>().bounds.center;
        Terrain locatedTerrain = FindTerrainUnderObject(pos, terrains);
        if (locatedTerrain == null)
        {
            Debug.LogWarning("No terrain found under the object.");
            return;
        }

        float roadWidth = roadBehaviour.RoadExtensionDatas[0].WidthValue;
        TerrainData terrainData = locatedTerrain.terrainData;
        Vector3 terrainPos = locatedTerrain.transform.position;

        int heightMapWidth = terrainData.heightmapResolution;
        int heightMapHeight = terrainData.heightmapResolution;
        float[,] heights = terrainData.GetHeights(0, 0, heightMapWidth, heightMapHeight);

        Undo.RegisterCompleteObjectUndo(terrainData, "Adjust Terrain Heights");

        var splineContainer = splineObj.GetComponent<SplineContainer>();
        if (splineObj == null)
        {
            Debug.LogWarning("The Spline Container does not have a SplineComponent.");
            return;
        }
        var spline = splineContainer.Spline;

        for (float t = 0; t < 1; t += 0.01f)
        {
            EditorUtility.DisplayProgressBar("调整高度", "应用高度", t);
            Vector3 splinePos = (Vector3)spline.EvaluatePosition(t) + splineObj.transform.position;
            // Vector3 tangent = (Vector3)spline.EvaluateTangent(t);
            // Vector3 dir = Vector3.Cross(tangent, Vector3.up).normalized;
            var terrainX = ((splinePos.x - terrainPos.x) / terrainData.size.x * heightMapWidth);
            var terrainZ = ((splinePos.z - terrainPos.z) / terrainData.size.z * heightMapHeight);

            float halfRoadWidth = roadWidth / 2f -3;
            float maxDistance = halfRoadWidth + adjustmentRadius;

            for (float x = -Mathf.CeilToInt(maxDistance); x <= Mathf.CeilToInt(maxDistance); x += 0.2f)
            {
                for (float z = -Mathf.CeilToInt(maxDistance); z <= Mathf.CeilToInt(maxDistance); z+= 0.2f)
                {
                    float distanceFromCenter = new Vector2(x, z).magnitude;
                    float distanceBeyondRoad = distanceFromCenter - halfRoadWidth;

                    int hx = (int)Mathf.Clamp(terrainX + x, 0, heightMapWidth - 1);
                    int hz = (int)Mathf.Clamp(terrainZ + z, 0, heightMapHeight - 1);
                    
                    float weight;
                    if (distanceBeyondRoad <= 0)
                    {
                        weight = 1;
                    }
                    else if (distanceFromCenter <= maxDistance)
                    {
                        weight = Mathf.Clamp01(1 - (distanceBeyondRoad / adjustmentRadius));
                    }
                    else
                    {
                        weight = 0;
                    }
                    var before = heights[hz, hx];
                    var after = Mathf.Lerp(before, (splinePos.y) / terrainData.size.y, weight * heightAdjustStrength);
                    if (math.abs(before - after) * terrainData.size.y <= maxAdjustAbsHeight)
                        heights[hz, hx] = after;
                }
            }
        }

        terrainData.SetHeights(0, 0, heights);
        EditorUtility.ClearProgressBar();
    }

    private Terrain FindTerrainUnderObject(Vector3 position, Terrain[] terrains)
    {
        Debug.Log("FindTerrainUnderObject " + terrains.Length);
        foreach (Terrain terrain in terrains)
        {
            Vector3 terrainPosition = terrain.transform.position;
            TerrainData terrainData = terrain.terrainData;
            Vector3 terrainSize = terrainData.size;

            if (position.x >= terrainPosition.x && position.x <= terrainPosition.x + terrainSize.x &&
                position.z >= terrainPosition.z && position.z <= terrainPosition.z + terrainSize.z)
            {
                    return terrain;
            }
        }

        return null;
    }
}