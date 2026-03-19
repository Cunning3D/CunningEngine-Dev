using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Linq;

namespace DevStuffs
{
    
[CustomEditor(typeof(ProceduralBuilding)), CanEditMultipleObjects]
public class ProceduralBuildingEditor : Editor
{
    // 全局编辑模式控制变量（静态，所有建筑共享）
    private static bool isEditModeEnabled = false;
    
    // 简化模式控制变量
    private static bool isSimplifiedModeEnabled = false;
    
    ProceduralBuilding building;
    public override void OnInspectorGUI()
    {
        // 添加编辑模式开关
        GUILayout.BeginVertical("box");
        EditorGUI.BeginChangeCheck();
        
        // 设置不同的按钮颜色，视觉上更明显
        GUI.backgroundColor = isEditModeEnabled ? Color.green : Color.grey;
        string buttonText = isEditModeEnabled ? "编辑模式：已开启" : "编辑模式：已关闭";
        
        if (GUILayout.Button(buttonText, GUILayout.Height(30)))
        {
            isEditModeEnabled = !isEditModeEnabled;
            SceneView.RepaintAll();
        }
        
        GUI.backgroundColor = Color.white;
        
        // 添加说明信息
        EditorGUILayout.HelpBox(isEditModeEnabled ? 
            "编辑模式已开启，可以调整建筑形状、材质和楼层" : 
            "编辑模式已关闭，场景优化中，无法调整建筑", MessageType.Info);
        
        GUILayout.EndVertical();
        
        // 添加简化模式开关
        GUILayout.BeginVertical("box");
        EditorGUI.BeginChangeCheck();
        
        building = target as ProceduralBuilding;
        
        // 设置按钮颜色表示简化模式状态
        GUI.backgroundColor = building.simplifiedMode ? new Color(0.2f, 0.6f, 1f) : Color.grey;
        string simpleModeText = building.simplifiedMode ? "简化模式：已开启" : "简化模式：已关闭";
        
        if (GUILayout.Button(simpleModeText, GUILayout.Height(30)))
        {
            Undo.RecordObject(building, "Toggle Simplified Mode");
            building.simplifiedMode = !building.simplifiedMode;
            
            if (building.gameObject.GetComponentInChildren<MeshFilter>() != null)
            {
                // 询问用户是否立即应用更改
                if (EditorUtility.DisplayDialog(
                    "是否重新生成建筑?", 
                    $"切换到{(building.simplifiedMode ? "简化" : "标准")}模式。是否立即重新生成建筑?", 
                    "是", "否"))
                {
                    building.RecalculateBuilding();
                }
            }
            
            EditorUtility.SetDirty(building);
            SceneView.RepaintAll();
        }
        
        GUI.backgroundColor = Color.white;
        
        // 简化模式说明信息
        if (building.simplifiedMode)
        {
            EditorGUILayout.HelpBox("简化模式生成更简单的建筑几何体，使用面片表示门窗和楼层", MessageType.Info);
            
            // 简化模式参数
            GUILayout.Space(10);
            EditorGUILayout.LabelField("简化模式参数", EditorStyles.boldLabel);
            
            // 添加预设选择下拉框
            EditorGUI.BeginChangeCheck();
            // 使用EditorGUILayout.EnumPopup创建漂亮的枚举下拉框
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("建筑预设类型", EditorStyles.boldLabel);
            GUI.backgroundColor = new Color(0.6f, 0.8f, 1.0f);
            building.simplifiedPreset = (SimplifiedBuildingPreset)EditorGUILayout.EnumPopup("选择预设", building.simplifiedPreset);
            GUI.backgroundColor = Color.white;
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(building);
                // 如果启用了自动重建，立即应用预设
                if (Application.isPlaying || building.autoRebuildInEditor)
                {
                    building.RecalculateBuilding();
                }
            }

            // 显示预设描述
            EditorGUILayout.BeginVertical("box");
            string presetDescription = GetPresetDescription(building.simplifiedPreset);
            EditorGUILayout.HelpBox(presetDescription, MessageType.Info);
            EditorGUILayout.EndVertical();

            // 添加选择预设后的提示信息
            if (building.simplifiedPreset != SimplifiedBuildingPreset.Custom)
            {
                EditorGUILayout.HelpBox("使用预设后，部分参数将根据预设自动设置，但屋顶类型仍可单独设置。", MessageType.Info);
            }
            
            // 是否生成简化窗户
            EditorGUI.BeginChangeCheck();
            building.generateSimpleWindows = EditorGUILayout.Toggle("生成简化窗户", building.generateSimpleWindows);
            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(building);
            
            // 是否生成简化楼层
            EditorGUI.BeginChangeCheck();
            building.generateSimpleFloors = EditorGUILayout.Toggle("生成简化楼层", building.generateSimpleFloors);
            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(building);
            
            // 窗户密度
            EditorGUI.BeginChangeCheck();
            building.simplifiedWindowDensity = EditorGUILayout.Slider("窗户密度", building.simplifiedWindowDensity, 0.1f, 1.0f);
            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(building);
            
            // 楼层高度
            EditorGUI.BeginChangeCheck();
            building.simplifiedFloorHeight = EditorGUILayout.FloatField("楼层高度", building.simplifiedFloorHeight);
            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(building);
            
            // 添加楼层高度控制选项
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("楼层高度控制", EditorStyles.boldLabel);
            
            // 高度乘数
            EditorGUI.BeginChangeCheck();
            building.stageHeightMultiplier = EditorGUILayout.Slider("高度乘数", building.stageHeightMultiplier, 0.1f, 10.0f);
            if (EditorGUI.EndChangeCheck()) 
            {
                EditorUtility.SetDirty(building);
                if (Application.isPlaying || building.autoRebuildInEditor)
                {
                    building.RecalculateBuilding();
                }
            }
            
            // 是否使用固定高度
            EditorGUI.BeginChangeCheck();
            building.useFixedHeight = EditorGUILayout.Toggle("使用固定高度", building.useFixedHeight);
            if (EditorGUI.EndChangeCheck()) 
            {
                EditorUtility.SetDirty(building);
                if (Application.isPlaying || building.autoRebuildInEditor)
                {
                    building.RecalculateBuilding();
                }
            }
            
            // 固定高度（只在启用固定高度时显示）
            if (building.useFixedHeight)
            {
                EditorGUI.BeginChangeCheck();
                building.fixedHeight = EditorGUILayout.Slider("固定楼层高度", building.fixedHeight, 1.0f, 10.0f);
                if (EditorGUI.EndChangeCheck()) 
                {
                    EditorUtility.SetDirty(building);
                    if (Application.isPlaying || building.autoRebuildInEditor)
                    {
                        building.RecalculateBuilding();
                    }
                }
            }
            
            // 添加屋顶类型选择
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("屋顶设置", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            // 创建屋顶类型枚举下拉框
            building.roofType = (ProceduralToolkit.Buildings.RoofType)EditorGUILayout.EnumPopup("屋顶类型", building.roofType);
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(building);
                if (Application.isPlaying || building.autoRebuildInEditor)
                {
                    building.RecalculateBuilding();
                }
            }
            
            // Houdini HDA资源
            EditorGUI.BeginChangeCheck();
            building.houdiniHDAAsset = EditorGUILayout.ObjectField("Houdini HDA资源", building.houdiniHDAAsset, typeof(Object), false);
            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(building);
            
            // 添加自动重建开关
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("实时更新设置", EditorStyles.boldLabel);
            
            EditorGUI.BeginChangeCheck();
            building.autoRebuildInEditor = EditorGUILayout.Toggle("修改参数时自动重建", building.autoRebuildInEditor);
            if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(building);
            
            // 手动重建按钮
            if (!building.autoRebuildInEditor)
            {
                if (GUILayout.Button("手动重建建筑", GUILayout.Height(30)))
                {
                    building.RecalculateBuilding();
                }
            }
            
            if (building.houdiniHDAAsset != null)
            {
                EditorGUILayout.HelpBox("Houdini HDA将用于生成高级简化模型", MessageType.Info);
            }
        }
        
        GUILayout.EndVertical();
        
        // 添加Gizmo显示控制区域
        if (!building.simplifiedMode) // 只在标准模式下显示
        {
            GUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Gizmo 显示控制", EditorStyles.boldLabel);
            
            // 设置不同的按钮颜色，视觉上更明显
            GUI.backgroundColor = building.showFloorGizmos ? new Color(0.2f, 0.8f, 1f) : Color.grey;
            string floorGizmoText = building.showFloorGizmos ? "楼层Gizmo：已开启" : "楼层Gizmo：已关闭";
            
            if (GUILayout.Button(floorGizmoText, GUILayout.Height(25)))
            {
                Undo.RecordObject(building, "Toggle Floor Gizmos");
                building.showFloorGizmos = !building.showFloorGizmos;
                EditorUtility.SetDirty(building);
                SceneView.RepaintAll();
            }
            
            GUI.backgroundColor = building.showEdgeGizmos ? new Color(1f, 0.5f, 0f) : Color.grey;
            string edgeGizmoText = building.showEdgeGizmos ? "边缘Gizmo：已开启" : "边缘Gizmo：已关闭";
            
            if (GUILayout.Button(edgeGizmoText, GUILayout.Height(25)))
            {
                Undo.RecordObject(building, "Toggle Edge Gizmos");
                building.showEdgeGizmos = !building.showEdgeGizmos;
                EditorUtility.SetDirty(building);
                SceneView.RepaintAll();
            }
            
            // 恢复按钮颜色
            GUI.backgroundColor = Color.white;
            
            // Gizmo颜色控制
            if (building.showFloorGizmos || building.showEdgeGizmos)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Gizmo 颜色设置", EditorStyles.boldLabel);
                
                if (building.showFloorGizmos)
                {
                    EditorGUI.BeginChangeCheck();
                    building.floorGizmoColor = EditorGUILayout.ColorField("楼层颜色", building.floorGizmoColor);
                    if (EditorGUI.EndChangeCheck()) 
                    {
                        EditorUtility.SetDirty(building);
                        SceneView.RepaintAll();
                    }
                }
                
                if (building.showEdgeGizmos)
                {
                    EditorGUI.BeginChangeCheck();
                    building.edgeGizmoColor = EditorGUILayout.ColorField("边缘颜色", building.edgeGizmoColor);
                    if (EditorGUI.EndChangeCheck()) 
                    {
                        EditorUtility.SetDirty(building);
                        SceneView.RepaintAll();
                    }
                }
            }
            
            // 添加说明信息
            EditorGUILayout.HelpBox("Gizmo显示能帮助你可视化建筑的楼层和边缘结构，便于调整和检查", MessageType.Info);
            
            GUILayout.EndVertical();
        }
        
        GUILayout.BeginVertical("box");
        string info = $"Shift - Add new points \nShift + Cntrl/CMD - Remove points on cursor\nALT/Control + LMB - Drag closest to cursor edge";
        GUIStyle newStyle = new GUIStyle();
        newStyle.fontSize = 12;
        newStyle.fontStyle = FontStyle.Bold;
        newStyle.normal.textColor = Color.grey;
        GUILayout.Label(info, newStyle );
        GUILayout.EndVertical();
        base.OnInspectorGUI();
        building = target as ProceduralBuilding;

        // 仅在编辑模式开启时显示建筑点编辑区域
        if (isEditModeEnabled && building.points.Count > 0)
        {
            GUILayout.Space(10);
            GUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("建筑控制点", EditorStyles.boldLabel);
            
            // 控制是否展开点编辑区域
            building.showPointsEditor = EditorGUILayout.Foldout(building.showPointsEditor, "显示点编辑器");
            
            if (building.showPointsEditor)
            {
                EditorGUI.BeginChangeCheck();
                
                for (int i = 0; i < building.points.Count; i++)
                {
                    GUILayout.BeginHorizontal();
                    
                    // 显示点索引
                    EditorGUILayout.LabelField($"点 {i}", GUILayout.Width(40));
                    
                    // 编辑点位置 - 使用EditorGUI.Vector3Field以便检测变化
                    EditorGUI.BeginChangeCheck();
                    Vector3 newPosition = EditorGUILayout.Vector3Field("", building.points[i], GUILayout.ExpandWidth(true));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(building, "Edit Building Point");
                        // 只有当变化足够大时才更新
                        if (Vector3.Distance(building.points[i], newPosition) > 0.001f)
                        {
                            building.points[i] = newPosition;
                            EditorUtility.SetDirty(building);
                            building.SetDirty(); // 使用SetDirty而不是直接重建
                            SceneView.RepaintAll();
                        }
                    }
                    
                    // 上移按钮
                    GUI.enabled = i > 0;
                    if (GUILayout.Button("↑", GUILayout.Width(25)))
                    {
                        Undo.RecordObject(building, "Move Building Point Up");
                        Vector3 temp = building.points[i];
                        building.points[i] = building.points[i-1];
                        building.points[i-1] = temp;
                        EditorUtility.SetDirty(building);
                        building.SetDirty(); // 使用SetDirty而不是直接重建
                    }
                    
                    // 下移按钮
                    GUI.enabled = i < building.points.Count - 1;
                    if (GUILayout.Button("↓", GUILayout.Width(25)))
                    {
                        Undo.RecordObject(building, "Move Building Point Down");
                        Vector3 temp = building.points[i];
                        building.points[i] = building.points[i+1];
                        building.points[i+1] = temp;
                        EditorUtility.SetDirty(building);
                        building.SetDirty(); // 使用SetDirty而不是直接重建
                    }
                    
                    // 删除按钮
                    GUI.enabled = building.points.Count > 3; // 确保至少保留3个点
                    if (GUILayout.Button("×", GUILayout.Width(25)))
                    {
                        Undo.RecordObject(building, "Remove Building Point");
                        building.points.RemoveAt(i);
                        EditorUtility.SetDirty(building);
                        building.SetDirty(); // 使用SetDirty而不是直接重建
                        i--; // 调整索引以避免跳过下一个点
                    }
                    
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                }
                
                // 添加新点按钮
                if (GUILayout.Button("添加新点"))
                {
                    Undo.RecordObject(building, "Add Building Point");
                    // 如果已有点，则在最后两个点之间插入
                    if (building.points.Count >= 2)
                    {
                        Vector3 lastPoint = building.points[building.points.Count - 1];
                        Vector3 firstPoint = building.points[0];
                        Vector3 newPoint = (lastPoint + firstPoint) / 2;
                        building.AddBuildingPoint(newPoint);
                    }
                    else if (building.points.Count == 1)
                    {
                        // 如果只有一个点，在其附近添加
                        building.AddBuildingPoint(building.points[0] + new Vector3(1, 0, 0));
                    }
                    else
                    {
                        // 没有点，添加一个在原点
                        building.AddBuildingPoint(Vector3.zero);
                    }
                    EditorUtility.SetDirty(building);
                    building.SetDirty(); // 使用SetDirty而不是直接重建
                    SceneView.RepaintAll(); // 强制场景视图刷新
                }
                
                // 应用更改并重建
                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(building);
                    building.SetDirty(); // 使用SetDirty而不是直接重建
                    SceneView.RepaintAll(); // 强制场景视图刷新
                }
            }
            
            GUILayout.EndVertical();
        }
        
        // 添加批量点操作功能
        if (isEditModeEnabled && building.points.Count > 2)
        {
            GUILayout.Space(10);
            GUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("点批量操作", EditorStyles.boldLabel);
            
            // 旋转所有点
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("旋转所有点", GUILayout.Width(100));
            if (GUILayout.Button("90°", GUILayout.Width(50)))
            {
                RotateAllPoints(90);
            }
            if (GUILayout.Button("45°", GUILayout.Width(50)))
            {
                RotateAllPoints(45);
            }
            if (GUILayout.Button("-45°", GUILayout.Width(50)))
            {
                RotateAllPoints(-45);
            }
            if (GUILayout.Button("-90°", GUILayout.Width(50)))
            {
                RotateAllPoints(-90);
            }
            GUILayout.EndHorizontal();
            
            // 缩放所有点
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("缩放所有点", GUILayout.Width(100));
            if (GUILayout.Button("1.5x", GUILayout.Width(50)))
            {
                ScaleAllPoints(1.5f);
            }
            if (GUILayout.Button("1.2x", GUILayout.Width(50)))
            {
                ScaleAllPoints(1.2f);
            }
            if (GUILayout.Button("0.8x", GUILayout.Width(50)))
            {
                ScaleAllPoints(0.8f);
            }
            if (GUILayout.Button("0.5x", GUILayout.Width(50)))
            {
                ScaleAllPoints(0.5f);
            }
            GUILayout.EndHorizontal();
            
            // 翻转点顺序
            if (GUILayout.Button("翻转点顺序"))
            {
                Undo.RecordObject(building, "Reverse Building Points");
                building.points.Reverse();
                EditorUtility.SetDirty(building);
                building.SetDirty(); // 使用SetDirty而不是直接重建
            }
            
            // 使所有点Y坐标相同
            if (GUILayout.Button("使所有点Y坐标相同"))
            {
                Undo.RecordObject(building, "Make Y Coordinates Uniform");
                float avgY = 0;
                foreach (var point in building.points)
                {
                    avgY += point.y;
                }
                avgY /= building.points.Count;
                
                for (int i = 0; i < building.points.Count; i++)
                {
                    Vector3 p = building.points[i];
                    building.points[i] = new Vector3(p.x, avgY, p.z);
                }
                
                EditorUtility.SetDirty(building);
                building.SetDirty(); // 使用SetDirty而不是直接重建
            }
            
            GUILayout.EndVertical();
        }
        
        // 添加楼层高度控制部分
        if (isEditModeEnabled && !building.simplifiedMode)
        {
            GUILayout.Space(10);
            GUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("楼层高度设置", EditorStyles.boldLabel);
            
            // 添加实时更新开关
            bool enableLiveUpdate = EditorPrefs.GetBool("ProceduralBuilding_LiveUpdate", true);
            bool newLiveUpdate = EditorGUILayout.Toggle("实时更新", enableLiveUpdate);
            if (newLiveUpdate != enableLiveUpdate)
            {
                EditorPrefs.SetBool("ProceduralBuilding_LiveUpdate", newLiveUpdate);
                enableLiveUpdate = newLiveUpdate;
            }
            
            // 全局楼层高度乘数
            EditorGUI.BeginChangeCheck();
            float newHeightMultiplier = EditorGUILayout.Slider("全局高度乘数", building.stageHeightMultiplier, 0.5f, 2.0f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(building, "Change Stage Height Multiplier");
                building.stageHeightMultiplier = newHeightMultiplier;
                EditorUtility.SetDirty(building);
                
                // 如果启用了实时更新，立即重建
                if (enableLiveUpdate)
                {
                    building.RecalculateBuilding();
                }
            }
            
            // 添加楼层数量控制
            EditorGUI.BeginChangeCheck();
            int newStagesCount = EditorGUILayout.IntSlider("楼层数量", building.stagesCount, 1, 40);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(building, "Change Stages Count");
                building.stagesCount = newStagesCount;
                EditorUtility.SetDirty(building);
                
                // 如果启用了实时更新，立即重建
                if (enableLiveUpdate)
                {
                    building.RecalculateBuilding();
                }
            }
            
            // 添加楼层预设固定高度编辑
            GUILayout.Space(5);
            EditorGUILayout.LabelField("楼层预设高度设置", EditorStyles.boldLabel);
            
            if (building.preset != null && building.preset.buildingStages.Count > 0)
            {
                // 使用折叠面板显示每个楼层预设的设置
                for (int i = 0; i < building.preset.buildingStages.Count; i++)
                {
                    var stage = building.preset.buildingStages[i];
                    // 显示折叠面板
                    stage.editorFoldout = EditorGUILayout.Foldout(stage.editorFoldout, $"楼层预设: {stage.nameOfStage}");
                    if (stage.editorFoldout)
                    {
                        EditorGUI.indentLevel++;
                        
                        // 固定高度开关
                        EditorGUI.BeginChangeCheck();
                        bool useFixed = EditorGUILayout.Toggle("使用固定高度", stage.useFixedHeight);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(building.preset, "Toggle Fixed Height");
                            stage.useFixedHeight = useFixed;
                            EditorUtility.SetDirty(building.preset);
                            
                            // 如果启用了实时更新，立即重建
                            if (enableLiveUpdate)
                            {
                                building.RecalculateBuilding();
                            }
                        }
                        
                        // 固定高度值
                        EditorGUI.BeginChangeCheck();
                        float height = EditorGUILayout.Slider("固定高度", stage.fixedHeight, 1.0f, 10.0f);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(building.preset, "Change Fixed Height");
                            stage.fixedHeight = height;
                            EditorUtility.SetDirty(building.preset);
                            
                            // 如果启用了实时更新，立即重建
                            if (enableLiveUpdate)
                            {
                                building.RecalculateBuilding();
                            }
                        }
                        
                        // 重复次数
                        EditorGUI.BeginChangeCheck();
                        int repeat = EditorGUILayout.IntSlider("重复次数", stage.repeatCount, 1, 20);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(building.preset, "Change Repeat Count");
                            stage.repeatCount = repeat;
                            EditorUtility.SetDirty(building.preset);
                            
                            // 如果启用了实时更新，立即重建
                            if (enableLiveUpdate)
                            {
                                building.RecalculateBuilding();
                            }
                        }
                        
                        EditorGUI.indentLevel--;
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox("请先设置BuildPreset", MessageType.Warning);
            }
            
            // 仅在非实时更新模式下显示手动更新按钮
            if (!enableLiveUpdate)
            {
                if (GUILayout.Button("应用高度设置并重建"))
                {
                    building.RecalculateBuilding();
                }
            }
            else
            {
                EditorGUILayout.HelpBox("实时更新已开启，调整参数时会自动重建", MessageType.Info);
            }
            
            GUILayout.EndVertical();
        }

        // 根据编辑模式状态启用/禁用底部按钮
        GUI.enabled = isEditModeEnabled;
        
        // 仅在整个Inspector编辑完成后再重建
        GUILayout.Space(5);
        if (GUILayout.Button("应用更改"))
        {
            building.RecalculateBuilding();
            SceneView.RepaintAll();
        }
        
        // 添加手动重置建筑中心的按钮
        GUILayout.Space(5);
        if (GUILayout.Button("重置建筑中心"))
        {
            Undo.RecordObject(building, "Reset Building Center");
            Undo.RecordObject(building.transform, "Reset Building Position");
            building.ReposeParentAndPointsToCenter();
            building.SetDirty();
            EditorUtility.SetDirty(building.transform);
            SceneView.RepaintAll();
        }
        
        // 恢复GUI状态
        GUI.enabled = true;
        
        // 编辑模式未开启时显示提示
        if (!isEditModeEnabled)
        {
            EditorGUILayout.HelpBox("请开启编辑模式以进行建筑编辑", MessageType.Info);
        }
    }

    public float thicknessMultiply = 1.3f;
    
    // 自定义颜色和大小
    private readonly Color activePointColor = new Color(1f, 0.2f, 0.2f, 0.9f);
    private readonly Color hoverPointColor = new Color(0.2f, 1f, 0.2f, 0.9f);
    private readonly Color normalPointColor = new Color(1f, 1f, 1f, 0.8f);
    
    // 将线条颜色修改为更高级的蓝色系
    private readonly Color normalLineColor = new Color(0.3f, 0.5f, 0.9f, 0.6f);
    private readonly Color selectedLineColor = new Color(0.1f, 0.8f, 1f, 0.9f);
    private readonly Color activeLineColor = new Color(0.0f, 0.6f, 1f, 1f);
    
    private readonly Color handleTextColor = new Color(1f, 1f, 1f, 0.9f);
    private readonly Color indicatorColor = new Color(0.1f, 0.7f, 1f, 0.8f);
    
    // 楼层编辑颜色
    private readonly Color floorHandleColor = new Color(0.0f, 0.8f, 1f, 0.8f);
    private readonly Color floorHighlightColor = new Color(0.0f, 1f, 1f, 1f);
    private readonly Color floorTextColor = new Color(1f, 1f, 1f, 0.9f);
    // 添加点法线指示器颜色
    private readonly Color pointNormalColor = new Color(0.8f, 0.2f, 1f, 0.9f);
    
    private float pointSize = 0.3f;
    private float lineWidth = 3f;
    private float selectedLineWidth = 5f;
    private float hoverDistance = 0.65f;
    
    // 楼层编辑变量
    private bool isEditingFloors = false; // 是否处于楼层编辑模式
    private int selectedFloor = -1; // 当前选中的楼层
    private bool isDraggingFloor = false; // 是否正在拖拽楼层高度
    private float floorHandleSize = 0.8f; // 楼层控制柄大小

    //-----------------------------------------------------------------------------
    /// <summary>
    /// Точка, которую мы сейчас передвигаем
    /// </summary>
    public int currentEditedPoint = -1;
    Vector3 closestPointOnLine;
    public int currentEditedLine = -1;
    public int currentClosestLine = -1;
    public int currentClosestPoint = -1;
    Vector3 cursor;
    Vector3 rawCursor;
    ProceduralBuilding b;
    bool isRaycasted;
    List<ProceduralBuilding> otherBuildings = new List<ProceduralBuilding>();
    float closestLineDistance;
    private void OnSceneGUI()
    {
        try
    {
        b = target as ProceduralBuilding;
            if (b == null)
            {
                Debug.LogWarning("Building reference is null in OnSceneGUI");
                return;
            }
                
            //Манипулятор точкой
        currentClosestLine = -1;
        currentClosestPoint = -1;

            try
            {
        MouseToWorldPosition();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Error in MouseToWorldPosition: " + e.Message);
                return;
            }

        rawCursor = cursor;
            
            // 安全地应用网格对齐
            try 
            {
        if (b.useGrid) cursor = GetPointOnGridPosition(cursor);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Error applying grid position: " + e.Message);
            }
            
            // 强制Gizmo实时更新
            try
            {
                HandleUtility.Repaint();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Error repainting GUI: " + e.Message);
            }
            
            // 处理键盘输入事件
            try
            {
                ProcessKeyboardEvents();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Error processing keyboard events: " + e.Message);
            }
            
            // 如果编辑模式未开启，只显示基本信息，不执行编辑功能
            if (!isEditModeEnabled)
            {
                try
                {
                    // 绘制模式状态信息
                    DrawEditModeInfo();
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("Error drawing edit mode info: " + e.Message);
                }
            return;
        }

            // 更新其他建筑列表，用于点链接功能
            try
            {
                otherBuildings = RecalculateOtherBuildingsList();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Error recalculating other buildings list: " + e.Message);
                otherBuildings = new List<ProceduralBuilding>(); // 确保不是null
            }
            
            // 计算最近的线，在编辑模式下执行
            try
            {
                CalculateClosestLine();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Error calculating closest line: " + e.Message);
            }

            // 根据编辑模式绘制不同内容
            if (isEditingFloors)
            {
                try
                {
                    // 楼层编辑模式
                    DrawFloorsEditingUI(b);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("Error drawing floors editing UI: " + e.Message);
                }
            }
            else
            {
                // 只在点编辑模式下绘制边缘
                try
                {
                    if (b.m_edges != null && b.m_edges.Count > 0)
                    {
                        // 绘制所有线段，带有渐变效果
                        DrawAllEdges();
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("Error drawing edges: " + e.Message);
                }
                
                // 点编辑模式 - 下面的代码
        if (Event.current.shift == false || currentEditedPoint != -1)
                {
                    try
        {
            SelectClosestLineLogic();
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning("Error in SelectClosestLineLogic: " + e.Message);
                    }

                    try
                    {
                        if (currentEditedLine != -1 && b.m_edges != null && b.m_edges.Count > 0 && currentEditedPoint == -1)
            {
                MoveClosestLineLogic();
            }
            else currentEditedLine = -1;
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning("Error in MoveClosestLineLogic: " + e.Message);
                        currentEditedLine = -1;
                    }
                    
                    try
                    {
                        // 处理点选择和编辑
                        HandlePointSelection();
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning("Error in HandlePointSelection: " + e.Message);
                    }
                    
                    // 检查是否按下Alt键，进入删除模式
                    try
                    {
                        if (Event.current.alt && b.points != null && b.points.Count > 0)
                        {
                            // 显示删除模式提示
                            DrawDeletionModeIndicator();
                            RemovePointLogic();
                        }
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning("Error in deletion mode: " + e.Message);
                    }
                }
                
                try
                {
                    if (Event.current.shift)
                    {
                        // 绘制添加点的指示器
                        DrawPointAdditionIndicator();
                        
                        // 仍然绘制红色区域作为参考
                        DrawBuildingBounds();
                        
                        if (isRaycasted)
                        {
                            InsertPointLogic();
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("Error processing shift mode: " + e.Message);
                }
            }

            try
            {
                if (b.useGrid)
                {
                    DrawGrid();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Error drawing grid: " + e.Message);
            }
            
            try
            {
                // 显示调试标签
                DrawDebugLabels();
            }
            catch (System.Exception) { }
            
            try
            {
                // 只在点编辑模式下绘制屋顶边缘
                if (!isEditingFloors && b.m_edges != null && b.m_edges.Count > 0)
                {
                    ProceduralRoof.DrawRoofEdgesDebug(b.m_edges, b.currentBuildingHeight);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Error drawing roof edges: " + e.Message);
            }
            
            try
            {
                // 始终绘制模式切换信息
                DrawModeInfo();
                
                // 绘制快捷键提示
                DrawHotkeyHelp();
            }
            catch (System.Exception) { }
        }
        catch (System.Exception e)
        {
            Debug.LogError("Critical error in OnSceneGUI: " + e.Message + "\n" + e.StackTrace);
        }
    }

    // 新方法：处理键盘事件
    private void ProcessKeyboardEvents()
    {
        // 检查是否按下Esc键退出编辑模式
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            // 确保事件被使用，防止Unity继续处理它
            Event.current.Use();
            
            // 如果当前在编辑楼层，先退出楼层编辑模式
            if (isEditingFloors)
            {
                isEditingFloors = false;
                SceneView.lastActiveSceneView.ShowNotification(new GUIContent("已退出楼层编辑模式"));
                SceneView.RepaintAll();
                return;
            }

            // 否则退出编辑模式
            isEditModeEnabled = false;
            SceneView.lastActiveSceneView.ShowNotification(new GUIContent("已退出编辑模式"));
            SceneView.RepaintAll();
            return;
        }

        // 检查F键切换楼层编辑模式 - 无论编辑模式是否开启都允许
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.F)
        {
            if (isEditModeEnabled) // 只有在编辑模式开启时才允许切换
            {
                isEditingFloors = !isEditingFloors;
                Event.current.Use();
                
                // 显示切换通知
                string message = isEditingFloors ? "已进入楼层编辑模式" : "已退出楼层编辑模式";
                SceneView.lastActiveSceneView.ShowNotification(new GUIContent(message));
            }
            else
            {
                // 显示提示信息
                SceneView.lastActiveSceneView.ShowNotification(new GUIContent("请先开启编辑模式"));
            }
        }
    }

    public void MoveClosestLineLogic()
    {
        EditorGUI.BeginChangeCheck();

        Vector3 point1 = b.points[currentEditedLine];
        Vector3 point2 = (currentEditedLine >= b.points.Count - 1) ? b.points[0] : b.points[currentEditedLine + 1];
        #if UNITY_2020_1_OR_NEWER
        Handles.DrawLine(point1, point1 +  Vector3.up * 5f , 4f);
        Handles.DrawLine(point2, point2 +  Vector3.up * 5f, 4f);
        #else
        Handles.DrawAAPolyLine(4f ,point1, point1 +  Vector3.up * 5f );
        Handles.DrawAAPolyLine( 4f , point2, point2 +  Vector3.up * 5f);
        #endif

        Undo.RecordObject(b, "Line position movement");
        Vector3 offset = (cursor - closestPointOnLine) * Time.deltaTime * 5f;

        #if UNITY_2020_1_OR_NEWER
        Handles.DrawLine(cursor , closestPointOnLine, 4f);
        #else
        Handles.DrawAAPolyLine(4f, cursor , closestPointOnLine);
        #endif

        // 应用偏移量
        Vector3 newPoint1 = b.points[currentEditedLine] + offset;
        Vector3 newPoint2;
        if(currentEditedLine >= b.points.Count - 1)
            newPoint2 = b.points[0] + offset;
        else
            newPoint2 = b.points[currentEditedLine + 1] + offset;
        
        // 使用阈值减少更新频率，只有当偏移量足够大时才更新
        if (offset.magnitude > 0.01f)
        {
            b.points[currentEditedLine] = newPoint1;
            if(currentEditedLine >= b.points.Count - 1)
                b.points[0] = newPoint2;
            else
                b.points[currentEditedLine + 1] = newPoint2;
            
            // 减少重建频率，使用SetDirty而不是直接重建
            b.SetDirty();
            SceneView.RepaintAll();
        }

        if (Event.current.type == EventType.MouseUp)
        {
            EditorUtility.SetDirty(b);
            currentEditedLine = -1;
            b.SetDirty(); // 使用SetDirty而不是直接重建
        }
    }


    void DrawGrid()
    {
        // 基础网格参数
        float gridRange = 100f;
        float time = Time.realtimeSinceStartup;
        
        // 动态效果
        float basePulse = (Mathf.Sin(time * 1.5f) + 1f) * 0.5f;
        float quickPulse = (Mathf.Sin(time * 3f) + 1f) * 0.5f;
        float slowPulse = (Mathf.Sin(time * 0.8f) + 1f) * 0.5f;
        float waveEffect = (Mathf.Sin(time * 1.2f) + 1f) * 0.5f;
        
        // 高级颜色定义
        Color primaryGridColor = new Color(0.0f, 0.9f, 1f, 0.3f + 0.1f * basePulse); 
        Color secondaryGridColor = new Color(0.1f, 0.5f, 0.9f, 0.15f);
        Color accentColor = new Color(1f, 0.6f, 0.0f, 0.9f);
        
        // 网格起始位置
        Vector3 gridStartPos = b.transform.position;
        gridStartPos.x = Mathf.Floor(gridStartPos.x / b.gridDev) * b.gridDev;
        gridStartPos.z = Mathf.Floor(gridStartPos.z / b.gridDev) * b.gridDev;
        gridStartPos.x -= b.gridDev * gridRange / 2;
        gridStartPos.z -= b.gridDev * gridRange / 2;
        
        // 原点坐标
        Vector3 originPos = new Vector3(
            Mathf.Round(b.transform.position.x / b.gridDev) * b.gridDev,
            b.transform.position.y,
            Mathf.Round(b.transform.position.z / b.gridDev) * b.gridDev
        );
        
        // 鼠标吸附位置
        Vector3 cursorSnappedPos = GetPointOnGridPosition(cursor);
        
        // 绘制背景平面 - 高级科技感背景
        if (isRaycasted) {
            // 大范围背景阴影 - 渐变效果
            float bgSize = b.gridDev * 30f;
            Vector3[] bgVerts = new Vector3[4] {
                originPos + new Vector3(-bgSize, 0, -bgSize),
                originPos + new Vector3(bgSize, 0, -bgSize),
                originPos + new Vector3(bgSize, 0, bgSize),
                originPos + new Vector3(-bgSize, 0, bgSize)
            };
            
            // 深色科技风背景
            Handles.color = new Color(0.02f, 0.07f, 0.15f, 0.07f + basePulse * 0.01f);
            Handles.DrawSolidRectangleWithOutline(bgVerts, 
                new Color(0.02f, 0.07f, 0.15f, 0.07f + basePulse * 0.01f), 
                new Color(0.05f, 0.2f, 0.4f, 0.03f));
            
            // 同心圆背景 - 类似雷达扫描效果
            int circles = 5;
            for (int i = 0; i < circles; i++) {
                float radius = bgSize * 0.7f * (1.0f - (float)i / circles);
                float alpha = 0.02f - 0.01f * ((float)i / circles);
                
                Handles.color = new Color(0.05f, 0.3f, 0.5f, alpha);
                Handles.DrawWireDisc(originPos, Vector3.up, radius);
            }
            
            // 添加动态扫描线效果
            float scanAngle = (time * 30f) % 360f;
            float scanRadius = bgSize * 0.6f;
            
            // 扫描线起始点和终点
            Vector3 scanCenter = originPos;
            float rad = scanAngle * Mathf.Deg2Rad;
            Vector3 scanDirection = new Vector3(Mathf.Cos(rad), 0, Mathf.Sin(rad));
            Vector3 scanEnd = scanCenter + scanDirection * scanRadius;
            
            // 绘制旋转扫描线
            Handles.color = new Color(0.0f, 0.7f, 1f, 0.15f);
            Handles.DrawAAPolyLine(2f, scanCenter, scanEnd);
            
            // 扫描线尾部效果
            float tailAngleSpan = 30f;
            int tailSegments = 5;
            for (int i = 0; i < tailSegments; i++) {
                float tailAngle = scanAngle - (i + 1) * (tailAngleSpan / tailSegments);
                float tailRad = tailAngle * Mathf.Deg2Rad;
                float tailAlpha = 0.15f * (1.0f - (float)(i + 1) / tailSegments);
                
                Vector3 tailDir = new Vector3(Mathf.Cos(tailRad), 0, Mathf.Sin(tailRad));
                Vector3 tailEnd = scanCenter + tailDir * scanRadius;
                
                Handles.color = new Color(0.0f, 0.7f, 1f, tailAlpha);
                Handles.DrawAAPolyLine(1.5f - i * 0.2f, scanCenter, tailEnd);
            }
            
            // 扫描线指示点
            Handles.color = new Color(0.0f, 0.8f, 1f, 0.6f + basePulse * 0.4f);
            Handles.DrawSolidDisc(scanEnd, Vector3.up, b.gridDev * 0.15f);
            
            // 原点下方辐射底座
            float fadeRadius = b.gridDev * 20f;
            for (int r = 0; r < 4; r++) {
                float radius = fadeRadius - r * fadeRadius * 0.2f;
                float alpha = 0.04f - r * 0.01f;
                
                // 渐变色彩
                Color gradientColor = new Color(
                    0.05f + r * 0.02f, 
                    0.2f + r * 0.03f, 
                    0.4f - r * 0.05f, 
                    alpha);
                
                Handles.color = gradientColor;
                Handles.DrawSolidDisc(originPos, Vector3.up, radius);
            }
        }
        
        // 绘制水平网格线 (X方向)
        for (int x = 0; x <= gridRange; x++) {
            Vector3 p1 = gridStartPos + new Vector3(x * b.gridDev, 0, 0);
            
            // 确定线的重要性
            bool isMajorGrid = x % 10 == 0; // 每10个为主网格
            bool isSecondaryGrid = x % 5 == 0 && !isMajorGrid; // 每5个为次网格
            bool isMinorGrid = !isMajorGrid && !isSecondaryGrid;
            
            // 计算与光标的距离
            float distToCursor = Mathf.Abs(p1.x - cursor.x) / (b.gridDev * 10f);
            float cursorGlow = Mathf.Clamp01(1.2f - distToCursor);
            
            // 计算与原点的距离
            float distToOrigin = Mathf.Abs(p1.x - originPos.x) / (b.gridDev * 10f);
            float originGlow = Mathf.Clamp01(1.5f - distToOrigin);
            
            // 计算动态效果
            float linePhase = (x * 0.1f + time * 0.2f) % 1.0f;
            float linePulse = Mathf.Sin(linePhase * Mathf.PI * 2) * 0.5f + 0.5f;
            
            if (isMajorGrid) {
                // 主网格线 - 使用复合颜色效果
                Color lineBaseColor = new Color(0.0f, 0.9f, 1f, 0.45f);
                Color lineGlowColor = new Color(
                    lineBaseColor.r, 
                    lineBaseColor.g, 
                    lineBaseColor.b,
                    lineBaseColor.a + cursorGlow * 0.25f + originGlow * 0.3f);
                
                // 增加线宽
                float lineWidth = 4f + cursorGlow * 1.5f + (x % 20 == 0 ? 1f : 0f);
                
                // 线条渐变效果
                Handles.color = lineGlowColor;
                Handles.DrawAAPolyLine(lineWidth, p1, p1 + Vector3.forward * gridRange * b.gridDev);
                
                // 主网格交叉点指示
                if (x % 20 == 0) {
                    for (int z = 0; z <= gridRange; z += 20) {
                        Vector3 intersect = p1 + new Vector3(0, 0, z * b.gridDev);
                        float intDist = Vector3.Distance(intersect, cursor);
                        
                        if (intDist < b.gridDev * 50f) {
                            float intensity = Mathf.Clamp01(1f - intDist / (b.gridDev * 50f));
                            Handles.color = new Color(0.1f, 0.7f, 1f, 0.3f * intensity);
                            Handles.DrawSolidDisc(intersect, Vector3.up, b.gridDev * 0.15f);
                            
                            Handles.color = new Color(0.0f, 0.8f, 1f, 0.7f * intensity);
                            Handles.DrawWireDisc(intersect, Vector3.up, b.gridDev * 0.2f * (0.8f + basePulse * 0.2f));
                        }
                    }
                }
                
                // 立体柱效果 - 添加垂直线
                if (distToCursor < 1.0f || distToOrigin < 1.0f || x % 20 == 0) {
                    float height = b.gridDev * (0.7f + basePulse * 0.3f);
                    if (x % 20 == 0) height *= 1.5f;
                    
                    // 渐变柱体
                    for (int h = 0; h < 4; h++) {
                        float segmentHeight = height / 4;
                        float startY = h * segmentHeight;
                        float endY = (h + 1) * segmentHeight;
                        float alpha = 0.6f - h * 0.15f;
                        
                        Handles.color = new Color(0.0f, 0.8f, 1f, alpha * (cursorGlow * 0.5f + originGlow * 0.5f + (x % 20 == 0 ? 0.5f : 0f)));
                        Handles.DrawAAPolyLine(2f, 
                            p1 + Vector3.up * startY,
                            p1 + Vector3.up * endY);
                    }
                    
                    // 坐标标记
                    if (height > b.gridDev * 0.8f) {
                        GUIStyle labelStyle = new GUIStyle();
                        labelStyle.normal.textColor = new Color(0.0f, 0.8f, 1f, 0.7f + basePulse * 0.3f);
                        labelStyle.fontSize = 10;
                        labelStyle.fontStyle = FontStyle.Bold;
                        labelStyle.alignment = TextAnchor.MiddleCenter;
                        
                        Handles.Label(p1 + Vector3.up * (height + 0.1f), $"{Mathf.RoundToInt(p1.x)}", labelStyle);
                    }
                }
            } 
            else if (isSecondaryGrid) {
                // 次网格线 - 适度突出
                float glowFactor = cursorGlow * 0.7f + originGlow * 0.3f;
                if (glowFactor > 0.1f || isRaycasted) {
                    Color lineColor = new Color(
                        0.1f, 
                        0.5f, 
                        0.9f, 
                        0.2f + glowFactor * 0.3f);
                    
                    Handles.color = lineColor;
                    Handles.DrawAAPolyLine(2.5f, p1, p1 + Vector3.forward * gridRange * b.gridDev);
                    
                    // 光标附近的次网格线添加垂直提示
                    if (cursorGlow > 0.5f && isRaycasted) {
                        float height = b.gridDev * 0.3f * cursorGlow;
                        Handles.color = new Color(0.1f, 0.5f, 0.9f, 0.3f * cursorGlow);
                        Handles.DrawAAPolyLine(1.5f, p1, p1 + Vector3.up * height);
                        
                        // 近距离显示坐标
                        if (cursorGlow > 0.7f) {
                            GUIStyle smallLabel = new GUIStyle();
                            smallLabel.normal.textColor = new Color(0.3f, 0.6f, 0.9f, 0.5f * cursorGlow);
                            smallLabel.fontSize = 9;
                            smallLabel.alignment = TextAnchor.MiddleCenter;
                            Handles.Label(p1 + Vector3.up * (height + 0.05f), $"{Mathf.RoundToInt(p1.x)}", smallLabel);
                        }
                    }
                }
            }
            else if (isMinorGrid) {
                // 次要网格线 - 仅在接近光标时显示
                float minorGlow = cursorGlow * 0.6f;
                if (minorGlow > 0.2f && isRaycasted) {
                    Color lineColor = new Color(0.1f, 0.3f, 0.6f, 0.1f + minorGlow * 0.15f);
                    Handles.color = lineColor;
                    Handles.DrawAAPolyLine(1.2f, p1, p1 + Vector3.forward * gridRange * b.gridDev);
                }
            }
        }
        
        // 绘制垂直网格线 (Z方向)
        for (int z = 0; z <= gridRange; z++) {
            Vector3 p1 = gridStartPos + new Vector3(0, 0, z * b.gridDev);
            
            // 确定线的重要性
            bool isMajorGrid = z % 10 == 0; // 每10个为主网格
            bool isSecondaryGrid = z % 5 == 0 && !isMajorGrid; // 每5个为次网格
            bool isMinorGrid = !isMajorGrid && !isSecondaryGrid;
            
            // 计算与光标的距离
            float distToCursor = Mathf.Abs(p1.z - cursor.z) / (b.gridDev * 10f);
            float cursorGlow = Mathf.Clamp01(1.2f - distToCursor);
            
            // 计算与原点的距离
            float distToOrigin = Mathf.Abs(p1.z - originPos.z) / (b.gridDev * 10f);
            float originGlow = Mathf.Clamp01(1.5f - distToOrigin);
            
            // 计算动态效果
            float linePhase = (z * 0.1f + time * 0.2f) % 1.0f;
            float linePulse = Mathf.Sin(linePhase * Mathf.PI * 2) * 0.5f + 0.5f;
            
            if (isMajorGrid) {
                // 主网格线 - 使用复合颜色效果
                Color lineBaseColor = new Color(0.0f, 0.9f, 1f, 0.45f);
                Color lineGlowColor = new Color(
                    lineBaseColor.r, 
                    lineBaseColor.g, 
                    lineBaseColor.b,
                    lineBaseColor.a + cursorGlow * 0.25f + originGlow * 0.3f);
                
                // 增加线宽
                float lineWidth = 4f + cursorGlow * 1.5f + (z % 20 == 0 ? 1f : 0f);
                
                // 线条渐变效果
                Handles.color = lineGlowColor;
                Handles.DrawAAPolyLine(lineWidth, p1, p1 + Vector3.right * gridRange * b.gridDev);
                
                // 立体柱效果 - 添加垂直线
                if (distToCursor < 1.0f || distToOrigin < 1.0f || z % 20 == 0) {
                    float height = b.gridDev * (0.7f + basePulse * 0.3f);
                    if (z % 20 == 0) height *= 1.5f;
                    
                    // 渐变柱体
                    for (int h = 0; h < 4; h++) {
                        float segmentHeight = height / 4;
                        float startY = h * segmentHeight;
                        float endY = (h + 1) * segmentHeight;
                        float alpha = 0.6f - h * 0.15f;
                        
                        Handles.color = new Color(0.0f, 0.8f, 1f, alpha * (cursorGlow * 0.5f + originGlow * 0.5f + (z % 20 == 0 ? 0.5f : 0f)));
                        Handles.DrawAAPolyLine(2f, 
                            p1 + Vector3.up * startY,
                            p1 + Vector3.up * endY);
                    }
                    
                    // 坐标标记
                    if (height > b.gridDev * 0.8f) {
                        GUIStyle labelStyle = new GUIStyle();
                        labelStyle.normal.textColor = new Color(0.0f, 0.8f, 1f, 0.7f + basePulse * 0.3f);
                        labelStyle.fontSize = 10;
                        labelStyle.fontStyle = FontStyle.Bold;
                        labelStyle.alignment = TextAnchor.MiddleCenter;
                        
                        Handles.Label(p1 + Vector3.up * (height + 0.1f), $"{Mathf.RoundToInt(p1.z)}", labelStyle);
                    }
                }
            } 
            else if (isSecondaryGrid) {
                // 次网格线 - 适度突出
                float glowFactor = cursorGlow * 0.7f + originGlow * 0.3f;
                if (glowFactor > 0.1f || isRaycasted) {
                    Color lineColor = new Color(
                        0.1f, 
                        0.5f, 
                        0.9f, 
                        0.2f + glowFactor * 0.3f);
                    
                    Handles.color = lineColor;
                    Handles.DrawAAPolyLine(2.5f, p1, p1 + Vector3.right * gridRange * b.gridDev);
                    
                    // 光标附近的次网格线添加垂直提示
                    if (cursorGlow > 0.5f && isRaycasted) {
                        float height = b.gridDev * 0.3f * cursorGlow;
                        Handles.color = new Color(0.1f, 0.5f, 0.9f, 0.3f * cursorGlow);
                        Handles.DrawAAPolyLine(1.5f, p1, p1 + Vector3.up * height);
                        
                        // 近距离显示坐标
                        if (cursorGlow > 0.7f) {
                            GUIStyle smallLabel = new GUIStyle();
                            smallLabel.normal.textColor = new Color(0.3f, 0.6f, 0.9f, 0.5f * cursorGlow);
                            smallLabel.fontSize = 9;
                            smallLabel.alignment = TextAnchor.MiddleCenter;
                            Handles.Label(p1 + Vector3.up * (height + 0.05f), $"{Mathf.RoundToInt(p1.z)}", smallLabel);
                        }
                    }
                }
            }
            else if (isMinorGrid) {
                // 次要网格线 - 仅在接近光标时显示
                float minorGlow = cursorGlow * 0.6f;
                if (minorGlow > 0.2f && isRaycasted) {
                    Color lineColor = new Color(0.1f, 0.3f, 0.6f, 0.1f + minorGlow * 0.15f);
                    Handles.color = lineColor;
                    Handles.DrawAAPolyLine(1.2f, p1, p1 + Vector3.right * gridRange * b.gridDev);
                }
            }
        }
        
        // 在网格单元格交点处添加特效
        if (isRaycasted) {
            // 获取当前网格单元格
            Vector3 gridCellPos = GetPointOnGridPosition(cursor);
            float cellRange = 4f; // 显示附近的单元格数量
            
            for (int dx = -Mathf.FloorToInt(cellRange); dx <= Mathf.CeilToInt(cellRange); dx++) {
                for (int dz = -Mathf.FloorToInt(cellRange); dz <= Mathf.CeilToInt(cellRange); dz++) {
                    Vector3 cellPos = gridCellPos + new Vector3(dx * b.gridDev, 0, dz * b.gridDev);
                    float distToCursor = Vector3.Distance(cellPos, gridCellPos) / (b.gridDev * cellRange);
                    
                    if (distToCursor <= 1.0f) {
                        float intensity = 1.0f - distToCursor;
                        // 交点指示器
                        Handles.color = new Color(0.1f, 0.6f, 0.9f, 0.05f * intensity);
                        Handles.DrawSolidDisc(cellPos, Vector3.up, b.gridDev * 0.1f);
                        
                        // 根据距离决定是否添加垂直效果
                        if (intensity > 0.6f && (dx + dz) % 2 == 0) {
                            float height = b.gridDev * 0.2f * intensity;
                            Handles.color = new Color(0.0f, 0.7f, 1f, 0.2f * intensity);
                            Handles.DrawAAPolyLine(1f, cellPos, cellPos + Vector3.up * height);
                            
                            // 添加顶部小点
                            Handles.color = new Color(0.0f, 0.8f, 1f, 0.4f * intensity * (0.8f + quickPulse * 0.2f));
                            Handles.DrawSolidDisc(cellPos + Vector3.up * height, Vector3.up, b.gridDev * 0.03f);
                        }
                    }
                }
            }
        }
        
        // 绘制特殊的高级立体全息效果 (仅在足够靠近光标时)
        if (isRaycasted && Vector3.Distance(cursorSnappedPos, originPos) < b.gridDev * 20f)
        {
            // 在光标位置创建一个浮动的3D全息显示
            float holoHeight = b.gridDev * 2.5f;
            Vector3 holoCenter = cursorSnappedPos + Vector3.up * (holoHeight * 0.5f);
            
            // 浮动光环
            for (int i = 0; i < 3; i++)
            {
                float ringRadius = b.gridDev * (0.3f + i * 0.15f);
                float ringHeight = holoHeight * (0.3f + i * 0.2f);
                float ringPhase = (time * (0.5f - i * 0.1f) + i * 0.33f) % 1.0f;
                float ringAlpha = 0.4f * (1.0f - i * 0.2f) * (1.0f - Mathf.Abs(ringPhase - 0.5f) * 2f);
                
                Vector3 ringPos = cursorSnappedPos + Vector3.up * ringHeight;
                Handles.color = new Color(0.0f, 0.8f, 1.0f, ringAlpha);
                Handles.DrawWireDisc(ringPos, Vector3.up, ringRadius * (0.9f + basePulse * 0.1f));
                
                // 环形光斑
                Handles.color = new Color(0.0f, 0.7f, 1.0f, ringAlpha * 0.4f);
                Handles.DrawSolidDisc(ringPos, Vector3.up, ringRadius * 0.1f);
            }
            
            // 垂直连接线
            Handles.color = new Color(0.0f, 0.8f, 1.0f, 0.2f + basePulse * 0.1f);
            Handles.DrawAAPolyLine(2f, cursorSnappedPos, cursorSnappedPos + Vector3.up * holoHeight);
            
            // 顶部悬浮标识
            Vector3 topPos = cursorSnappedPos + Vector3.up * holoHeight;
            float topSize = b.gridDev * 0.4f * (0.9f + quickPulse * 0.1f);
            
            // 旋转菱形
            float rotAngle = time * 30f;
            Vector3[] diamondVerts = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                float angle = rotAngle + i * 90f;
                float rad = angle * Mathf.Deg2Rad;
                diamondVerts[i] = topPos + new Vector3(
                    Mathf.Cos(rad) * topSize * 0.6f,
                    0,
                    Mathf.Sin(rad) * topSize * 0.6f
                );
            }
            
            Handles.color = new Color(0.0f, 0.9f, 1.0f, 0.7f + quickPulse * 0.3f);
            Handles.DrawLines(new Vector3[] {
                diamondVerts[0], diamondVerts[1],
                diamondVerts[1], diamondVerts[2],
                diamondVerts[2], diamondVerts[3],
                diamondVerts[3], diamondVerts[0]
            });
            
            // 中心标记
            Handles.color = new Color(0.2f, 0.9f, 1.0f, 0.7f + quickPulse * 0.3f);
            Handles.DrawWireDisc(topPos, Vector3.up, topSize * 0.3f);
            
            // 数据标记 - 全息坐标文本
            GUIStyle holoLabel = new GUIStyle();
            holoLabel.normal.textColor = new Color(0.0f, 0.9f, 1.0f, 0.8f + quickPulse * 0.2f);
            holoLabel.fontSize = 12;
            holoLabel.fontStyle = FontStyle.Bold;
            holoLabel.alignment = TextAnchor.MiddleCenter;
            
            // 坐标指示
            Handles.Label(topPos + Vector3.up * (topSize * 0.5f), 
                $"P:({Mathf.RoundToInt(cursorSnappedPos.x)},{Mathf.RoundToInt(cursorSnappedPos.z)})", holoLabel);
            
            // 绘制网格导航指南线
            if (Vector3.Distance(cursorSnappedPos, originPos) > b.gridDev * 3f)
            {
                // 绘制从光标到原点的导航线
                Vector3 dirToOrigin = (originPos - cursorSnappedPos).normalized;
                float distToOrigin = Vector3.Distance(cursorSnappedPos, originPos);
                Vector3 navEnd = cursorSnappedPos + dirToOrigin * Mathf.Min(distToOrigin, b.gridDev * 5f);
                
                // 绘制导航箭头
                Handles.color = new Color(0.0f, 0.8f, 1.0f, 0.4f);
                Handles.DrawAAPolyLine(2f, cursorSnappedPos, navEnd);
                
                // 箭头指示
                Vector3 arrowPos = navEnd - dirToOrigin * 0.3f;
                Handles.color = new Color(0.0f, 0.9f, 1.0f, 0.6f);
                Handles.ConeHandleCap(0, navEnd, Quaternion.LookRotation(dirToOrigin), 
                    b.gridDev * 0.2f, EventType.Repaint);
                
                // 距离指示
                GUIStyle distLabel = new GUIStyle();
                distLabel.normal.textColor = new Color(0.2f, 0.8f, 1.0f, 0.6f);
                distLabel.fontSize = 10;
                distLabel.alignment = TextAnchor.MiddleCenter;
                
                Vector3 labelPos = cursorSnappedPos + dirToOrigin * b.gridDev * 2f + Vector3.up * 0.3f;
                Handles.Label(labelPos, $"原点: {distToOrigin:F1}m", distLabel);
            }
        }
    }
    
    // 绘制坐标轴
    void DrawCoordinateAxis(Vector3 origin, float gridDev, float breathingEffect)
    {
        float axisLength = gridDev * 2.0f;
        
        // X轴 (红色)
        Handles.color = new Color(1f, 0.3f, 0.3f, 0.9f);
        Handles.DrawAAPolyLine(3f, origin, origin + Vector3.right * axisLength);
        DrawAxisArrow(origin + Vector3.right * axisLength, Vector3.right, 0.15f * gridDev, new Color(1f, 0.3f, 0.3f, 0.9f));
        
        // Z轴 (蓝色)
        Handles.color = new Color(0.3f, 0.5f, 1f, 0.9f);
        Handles.DrawAAPolyLine(3f, origin, origin + Vector3.forward * axisLength);
        DrawAxisArrow(origin + Vector3.forward * axisLength, Vector3.forward, 0.15f * gridDev, new Color(0.3f, 0.5f, 1f, 0.9f));
        
        // Y轴 (绿色)
        Handles.color = new Color(0.3f, 1f, 0.5f, 0.9f);
        Handles.DrawAAPolyLine(3f, origin, origin + Vector3.up * axisLength * 0.5f);
        DrawAxisArrow(origin + Vector3.up * axisLength * 0.5f, Vector3.up, 0.15f * gridDev, new Color(0.3f, 1f, 0.5f, 0.9f));
        
        // 轴标签
        GUIStyle axisLabelStyle = new GUIStyle();
        axisLabelStyle.fontSize = 11;
        axisLabelStyle.fontStyle = FontStyle.Bold;
        
        axisLabelStyle.normal.textColor = new Color(1f, 0.3f, 0.3f, 0.9f);
        Handles.Label(origin + Vector3.right * axisLength * 1.1f, "X", axisLabelStyle);
        
        axisLabelStyle.normal.textColor = new Color(0.3f, 0.5f, 1f, 0.9f);
        Handles.Label(origin + Vector3.forward * axisLength * 1.1f, "Z", axisLabelStyle);
        
        axisLabelStyle.normal.textColor = new Color(0.3f, 1f, 0.5f, 0.9f);
        Handles.Label(origin + Vector3.up * axisLength * 0.6f, "Y", axisLabelStyle);
    }
    
    // 绘制轴箭头
    void DrawAxisArrow(Vector3 position, Vector3 direction, float size, Color color)
    {
        Handles.color = color;
        
        // 计算垂直于方向的两个向量
        Vector3 normal = Vector3.up;
        if (direction == Vector3.up || direction == Vector3.down)
            normal = Vector3.forward;
        
        Vector3 side = Vector3.Cross(direction, normal).normalized;
        normal = Vector3.Cross(side, direction).normalized;
        
        // 箭头顶点
        Vector3 tip = position;
        Vector3 baseCenter = position - direction * size;
        
        // 箭头底部的四个顶点 (稍微倾斜一点)
        float baseRadius = size * 0.5f;
        Vector3[] baseVerts = new Vector3[]
        {
            baseCenter + side * baseRadius + normal * baseRadius,
            baseCenter + side * baseRadius - normal * baseRadius,
            baseCenter - side * baseRadius - normal * baseRadius,
            baseCenter - side * baseRadius + normal * baseRadius
        };
        
        // 绘制从底部到顶点的四个三角面
        for (int i = 0; i < 4; i++)
        {
            int nextIndex = (i + 1) % 4;
            
            Vector3[] triangleVerts = new Vector3[]
            {
                tip,
                baseVerts[i],
                baseVerts[nextIndex]
            };
            
            Handles.DrawAAConvexPolygon(triangleVerts);
        }
        
        // 绘制底部
        Handles.DrawAAConvexPolygon(baseVerts);
    }
    
    // 绘制光标指示器
    void DrawCursorIndicator(Vector3 position, float gridDev, float basePulse, float quickPulse)
    {
        // 动态大小和角度
        float size = gridDev * 0.3f * (1.0f + 0.2f * basePulse);
        float rotation = Time.realtimeSinceStartup * 50f;
        
        // 外环
        Handles.color = new Color(0.0f, 0.9f, 1f, 0.8f - 0.3f * basePulse);
        Handles.DrawWireDisc(position, Vector3.up, size);
        
        // 正方形框
        Vector3[] squareVerts = new Vector3[4];
        float squareSize = size * 0.7f * (1.0f + 0.2f * quickPulse);
        
        for (int i = 0; i < 4; i++)
        {
            float angle = rotation + i * 90f;
            float rad = angle * Mathf.Deg2Rad;
            squareVerts[i] = position + new Vector3(
                Mathf.Cos(rad) * squareSize,
                0,
                Mathf.Sin(rad) * squareSize
            );
        }
        
        Handles.color = new Color(0.0f, 1f, 1f, 0.9f);
        Handles.DrawLines(new Vector3[] {
            squareVerts[0], squareVerts[1],
            squareVerts[1], squareVerts[2],
            squareVerts[2], squareVerts[3],
            squareVerts[3], squareVerts[0]
        });
        
        // 中心十字
        float crossSize = size * 0.3f;
        Handles.color = new Color(1f, 1f, 1f, 0.9f);
        Handles.DrawLine(
            position + new Vector3(-crossSize, 0, 0),
            position + new Vector3(crossSize, 0, 0)
        );
        Handles.DrawLine(
            position + new Vector3(0, 0, -crossSize),
            position + new Vector3(0, 0, crossSize)
        );
        
        // 正负号标记
        GUIStyle smallLabel = new GUIStyle();
        smallLabel.fontSize = 10;
        smallLabel.alignment = TextAnchor.MiddleCenter;
        smallLabel.fontStyle = FontStyle.Bold;
        
        smallLabel.normal.textColor = new Color(1f, 1f, 1f, 0.8f);
        Handles.Label(position + new Vector3(size * 1.3f, 0, 0), "+X", smallLabel);
        Handles.Label(position + new Vector3(-size * 1.3f, 0, 0), "-X", smallLabel);
        Handles.Label(position + new Vector3(0, 0, size * 1.3f), "+Z", smallLabel);
        Handles.Label(position + new Vector3(0, 0, -size * 1.3f), "-Z", smallLabel);
    }

    public void SnapCurrentPointOverGrid()
    {
        if (b.useGrid == false) return;
        b.points[currentEditedPoint] = GetPointOnGridPosition(b.points[currentEditedPoint]);
    }

    public Vector3 GetPointOnGridPosition(Vector3 point)
    {
        point.x = Mathf.Round(point.x / b.gridDev) * b.gridDev;
        point.z = Mathf.Round(point.z / b.gridDev) * b.gridDev;

        return point;
    }

    float distance;
    Vector3 cl_closestPoint;
    float cl_dot;
    public void CalculateClosestLine()
    {
        // 重置最近线的距离
        closestLineDistance = 99f;
        
        // 基本检查
        if (b == null)
        {
            Debug.LogWarning("Building reference is null in CalculateClosestLine");
            return;
        }
        
        if (b.points == null || b.points.Count < 2)
        {
            // 点太少无法形成线段，正常情况
            return;
        }
        
        if (b.m_edges == null)
        {
            Debug.LogWarning("Edges list is null in CalculateClosestLine");
            return;
        }
        
        // 如果当前正在编辑点，则不重新计算最近的线
        if (currentEditedPoint != -1) 
            return;

        // 如果正在编辑线，则计算与正在编辑的线的距离
        if (currentEditedLine != -1 && currentEditedLine < b.m_edges.Count)
        {
            try
        {
            closestPointOnLine = GetClosestPointOnLine(b.m_edges[currentEditedLine].p1, b.m_edges[currentEditedLine].p2, out distance, out cl_dot);
                // 绘制从光标到线上最近点的连接线
                Handles.DrawLine(closestPointOnLine, cursor);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Error calculating closest point on edited line: " + e.Message);
                currentEditedLine = -1;
            }
            return;
        }

        // 计算与每条线的距离，找出最近的线
        List<int> closestLine = new List<int>();
        List<Vector3> closestPoint = new List<Vector3>();

        for (int i = 0; i < b.m_edges.Count; i++)
        {
            try
            {
                // 确保边缘索引有效
                if (i >= b.m_edges.Count)
                {
                    Debug.LogWarning($"Edge index {i} out of range ({b.m_edges.Count})");
                    continue;
                }
                
                // 计算线上最近的点
                cl_closestPoint = GetClosestPointOnLine(b.m_edges[i].p1, b.m_edges[i].p2, out distance, out cl_dot);
                
                // 如果点在线段上（不在延长线上），并且距离在合理范围内
            if (cl_dot > 0f && cl_dot < distance)
            {
                closestLine.Add(i);
                closestPoint.Add(cl_closestPoint);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Error calculating closest point on line {i}: " + e.Message);
            }
        }
        
        // 如果没有找到任何符合条件的线，退出
        if (closestLine.Count == 0) 
            return;
        
        // 找出距离最近的线
        int ci = -1;
        currentClosestLine = -1;

        for (int i = 0; i < closestPoint.Count; i++)
        {
            try
        {
            float distance = Vector3.Distance(closestPoint[i], cursor);
                if (closestLineDistance == 99f || distance < closestLineDistance)
            {
                closestLineDistance = distance;
                currentClosestLine = closestLine[i];
                ci = i;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Error calculating distance to line {i}: " + e.Message);
            }
        }

        // 如果找到了最近的线，绘制指示线
        if (ci >= 0 && ci < closestPoint.Count)
        {
            try
            {
        closestPointOnLine = closestPoint[ci];
        Handles.DrawLine(cursor, closestPointOnLine);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Error drawing line to closest point: " + e.Message);
            }
        }
    }

    void SelectClosestLineLogic()
    {
        try
        {
            // 基本检查
            if (b == null)
            {
                Debug.LogWarning("Building reference is null in SelectClosestLineLogic");
                return;
            }
            
            if (b.m_edges == null)
            {
                Debug.LogWarning("Edges list is null in SelectClosestLineLogic");
                return;
            }
            
            // 检查currentClosestLine是否有效
            if (currentClosestLine < 0 || currentClosestLine >= b.m_edges.Count)
            {
                // 没有找到最近的线，这是正常的
                return;
            }
            
            // 添加对事件的处理
        var e = Event.current;
            if (e == null)
            {
                Debug.LogWarning("Event.current is null in SelectClosestLineLogic");
                return;
            }
            
            // 设置事件控制
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            
            // 按下Control+鼠标左键选择线段
        if (e.control && e.type == EventType.MouseDown && e.button == 0)
        {
            currentEditedLine = currentClosestLine;
                Debug.Log("Started editing line: " + currentEditedLine);
                e.Use(); // 重要：阻止事件继续传播
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Error in SelectClosestLineLogic: " + ex.Message + "\n" + ex.StackTrace);
        }
    }


    Vector3 dir;
    Vector3 toCursor;
    public Vector3 GetClosestPointOnLine(Vector3 p1, Vector3 p2, out float _dist, out float _dot)
    {
        dir = p2 - p1;
        toCursor = cursor - p1;
        _dot = Vector3.Dot(dir.normalized, toCursor);
        var point = p1 + (p2 - p1).normalized * _dot;
        _dist = Vector3.Distance(p1, p2);

        return point;
    }

    List<ProceduralBuilding> RecalculateOtherBuildingsList()
    {
        return GameObject.FindObjectsOfType<ProceduralBuilding>().Where(p => p != b).ToList();
    }

    public bool LinkDragedPointToOtherBuildingsPoints()
    {
        // 获取当前编辑点的世界坐标
        Vector3 currentWorldPos = b.transform.TransformPoint(b.points[currentEditedPoint]);
        
        foreach (var build in otherBuildings)
        {
            if (build.points.Count == 0) continue;
            
            // 获取其他建筑点的世界坐标
            List<Vector3> otherBuildWorldPoints = build.GetWorldPoints();
            
            foreach (var worldPoint in otherBuildWorldPoints)
            {
                Handles.color = new Color(0.2f,0.2f,1f,1f);
                Handles.SphereHandleCap(0, worldPoint, Quaternion.identity, 1.3f, EventType.Layout);
                
                if (Vector3.Distance(worldPoint, currentWorldPos) < 1.3f)
                {
                    // 找到了距离足够近的点，将当前点吸附到这个点
                    // 但需要先转换为局部坐标
                    b.points[currentEditedPoint] = b.transform.InverseTransformPoint(worldPoint);
                    return true;
                }
            }
        }
        return false;
    }



    public void MouseToWorldPosition()
    {
        //Debug.Log($"can: {Camera.main.name}");
        var mousePosition = Event.current.mousePosition;
        var ray = HandleUtility.GUIPointToWorldRay(mousePosition);
        Plane plane = new Plane(Vector3.up, b.transform.position);
        isRaycasted = plane.Raycast(ray, out float enter);

        cursor = ray.origin + ray.direction.normalized * enter;
    }

    public void DrawRedArea()
    {
        Vector3 pos = b.transform.position;
        Vector3[] verts = new Vector3[] { pos + Vector3.right * 50f + Vector3.forward * 50f, pos + Vector3.right * 50f - Vector3.forward * 50f, pos + Vector3.right * -50f - Vector3.forward * 50f, pos + Vector3.forward * 50f - Vector3.right * 50f };
        Handles.DrawSolidRectangleWithOutline(verts, new Color(0.5f, 0.1f, 0.1f, .5f), Color.red);
        Handles.DrawAAPolyLine(8, verts);
    }

    void RemovePointLogic()
    {
        // 只有在点数量大于3时才允许删除，否则建筑会变成线或点
        if (b.points.Count <= 3)
        {
            // 显示警告
            if (Event.current.type == EventType.MouseDown && Event.current.alt)
            {
                SceneView.lastActiveSceneView.ShowNotification(
                    new GUIContent("建筑至少需要3个点"));
                Event.current.Use();
            }
            return;
        }
        
        int closestPointIndex = -1;
        float minDistance = 0.5f; // 设置一个阈值，确保只有鼠标足够靠近点时才能删除
        
        for(int i = 0; i < b.points.Count; i++)
        {
            // 转换为世界坐标进行比较
            Vector3 worldPoint = b.transform.TransformPoint(b.points[i]);
            float distance = Vector3.Distance(worldPoint, cursor);
            
            if(distance < minDistance)
            {
                minDistance = distance;
                closestPointIndex = i;
            }
        }
        
        // 如果没有找到足够近的点，退出
        if (closestPointIndex == -1) return;
        
        Vector3 closestPoint = b.transform.TransformPoint(b.points[closestPointIndex]);
        
        // 使用闪烁效果表示删除目标
        float pulseValue = Mathf.Sin(Time.realtimeSinceStartup * 8f) * 0.5f + 0.5f;
        Color deleteColor = new Color(1f, 0.2f, 0.2f, 0.8f + pulseValue * 0.2f);
        Handles.color = deleteColor;
        
        // 绘制从光标到点的连接线
        Handles.DrawAAPolyLine(4f, new Vector3[] { closestPoint, cursor });
        
        // 绘制十字表示删除
        float crossSize = 0.4f * thicknessMultiply;
        Handles.DrawAAPolyLine(3f, 
            closestPoint + new Vector3(-crossSize, 0, -crossSize),
            closestPoint + new Vector3(crossSize, 0, crossSize));
        Handles.DrawAAPolyLine(3f, 
            closestPoint + new Vector3(-crossSize, 0, crossSize),
            closestPoint + new Vector3(crossSize, 0, -crossSize));
            
        // 绘制圆圈表示选中，并添加脉动效果
        float radiusPulse = 0.8f + pulseValue * 0.4f;
        Handles.DrawWireDisc(closestPoint, Vector3.up, crossSize * radiusPulse * 1.2f);
        
        // 显示删除提示
        GUIStyle style = new GUIStyle();
        style.normal.textColor = deleteColor;
        style.fontSize = 14;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.MiddleCenter;
        Handles.Label(closestPoint + Vector3.up * 0.8f, "删除", style);

        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        
        // 检测鼠标点击事件
        if (Event.current.type == EventType.MouseDown && Event.current.alt)
        {
            // 记录撤销
            Undo.RecordObject(b, "Remove Building Point");
            
            // 删除点
            b.points.RemoveAt(closestPointIndex);
            b.SetDirty(); // 使用SetDirty而不是直接重建
            
            // 显示删除成功通知
            SceneView.lastActiveSceneView.ShowNotification(
                new GUIContent($"已删除点 {closestPointIndex}"));
            
            // 确保事件被处理
            Event.current.Use();
            
            // 强制场景视图刷新
            SceneView.RepaintAll();
            HandleUtility.Repaint();
            
            EditorUtility.SetDirty(b);
        }
    }

    Vector3 GetPointByIndex(int index)
    {
        // 返回世界坐标中的点位置
        return b.transform.TransformPoint(b.points[index % b.points.Count]);
    }

    void InsertPointLogic()
    {
        int insertIndex = GetIndexToInsertNewPoint(cursor, b);
        if (insertIndex == -1) return;

        // 获取插入点连接的两个点（世界坐标）
        Vector3 point1 = GetPointByIndex(insertIndex);
        Vector3 point2 = GetPointByIndex(insertIndex + 1);
        
        // 绘制连接线，使用虚线效果更适合预览
        Color lineColor = new Color(0.2f, 1f, 0.5f, 0.8f);
        
        Handles.color = lineColor;
        // 使用虚线替代实线
        Handles.DrawDottedLine(point1, cursor, 5f);
        Handles.DrawDottedLine(point2, cursor, 5f);
        
        // 在插入点绘制漂亮的球体
        Handles.color = new Color(0.2f, 1f, 0.7f, 0.8f);
        Handles.SphereHandleCap(1, cursor, Quaternion.identity, 0.3f * thicknessMultiply, EventType.Repaint);
        
        // 绘制新点的插入指示器
        float pulseScale = Mathf.Sin(Time.realtimeSinceStartup * 5f) * 0.2f + 1f;
        Handles.color = new Color(0.2f, 1f, 0.7f, 0.3f);
        Handles.DrawWireDisc(cursor, Vector3.up, 0.5f * pulseScale * thicknessMultiply);
        
        // 绘制箭头指示插入位置
        Vector3 midPoint = (point1 + point2) * 0.5f;
        Vector3 direction = (cursor - midPoint).normalized;
        Vector3 arrowPos = cursor + direction * 0.7f * thicknessMultiply;
        Handles.color = new Color(1f, 1f, 0.2f, 0.8f);
        Handles.DrawAAPolyLine(3f, cursor, arrowPos);
        Handles.ConeHandleCap(0, arrowPos, Quaternion.LookRotation(direction), 0.3f * thicknessMultiply, EventType.Repaint);
        
        // 显示插入位置的信息
        GUIStyle labelStyle = new GUIStyle();
        labelStyle.normal.textColor = new Color(1f, 1f, 0.2f, 1f);
        labelStyle.fontSize = 12;
        labelStyle.fontStyle = FontStyle.Bold;
        labelStyle.alignment = TextAnchor.MiddleCenter;
        
        string insertText = (insertIndex == -1) ? "插入到开始" : 
                           (insertIndex >= b.points.Count) ? "插入到末尾" : 
                           $"插入到位置 {insertIndex} 之后";
        
        Handles.Label(cursor + Vector3.up * 0.7f, insertText, labelStyle);

        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        if (Event.current.type == EventType.MouseDown)
        {
            // 注意：AddBuildingPoint现在会自动处理世界坐标到局部坐标的转换
            if (insertIndex == -1) b.AddBuildingPoint(cursor, 0);
            else if (insertIndex < b.points.Count) b.AddBuildingPoint(cursor, insertIndex + 1);
            else if (insertIndex >= b.points.Count) b.AddBuildingPoint(cursor);

            b.SetDirty(); // 使用SetDirty而不是直接重建
            currentEditedPoint = insertIndex + 1;

            Selection.activeGameObject = b.gameObject;
            
            // 强制刷新视图
            SceneView.RepaintAll();
            HandleUtility.Repaint();

            EditorUtility.SetDirty(b);
        }
    }

    public int GetIndexToInsertNewPoint(Vector3 point, ProceduralBuilding b)
    {
        if (b.points.Count == 0) return 0;
        else if (b.points.Count == 1) return 1;
        List<float> closestDots = new List<float>();
        
        //Debug.Log($"Current closest line: {currentClosestLine}");
        if (currentClosestLine != -1)
        {
            return currentClosestLine;
        }

        for (int i = 0; i < b.points.Count - 1; i++)
        {
            //Дстанция до точки
            Vector3 pointDir = point - b.points[i];
            //длинна линии
            Vector3 lineDir = b.points[i + 1] - b.points[i];
            if (lineDir.magnitude < pointDir.magnitude || pointDir.magnitude < lineDir.magnitude * 0.2f) closestDots.Add(0f); //Провал результата
            else
            {
                closestDots.Add(Vector3.Dot(lineDir.normalized, pointDir.normalized));
            }
            Handles.Label(b.points[i] + pointDir / 2f, closestDots[i].ToString("f2"));
        }

        ///Может так быть что толчка дальше всех, тогда вернет это значение и это означает что ее надо не инсерт а адд в массив, и линия
        ///линия будет дебажить не от 2 точек а от ближайшей точки первой или последней, так как точка не ставится между, подстрахуемся заранее
        int closestIndex = Vector3.Distance(b.points[0], point) < Vector3.Distance(b.points[b.points.Count -1], point)? -1: b.points.Count;
        float closestDistance = 0;

        for (int i = 0; i < closestDots.Count; i++)
        {
            if (closestDots[i] <= 0) continue;

            if (closestDots[i] > closestDistance)
            {
                closestDistance = closestDots[i];
                closestIndex = i;
            }
        }
        return closestIndex;
    }

    // 旋转所有点
    private void RotateAllPoints(float degrees)
    {
        if (building.points.Count == 0) return;
        
        Undo.RecordObject(building, "Rotate Building Points");
        
        // 计算中心点
        Vector3 center = Vector3.zero;
        foreach (var point in building.points)
        {
            center += point;
        }
        center /= building.points.Count;
        
        // 旋转所有点
        for (int i = 0; i < building.points.Count; i++)
        {
            Vector3 pointRelativeToCenter = building.points[i] - center;
            float angle = degrees * Mathf.Deg2Rad;
            
            // 绕Y轴旋转点
            float newX = pointRelativeToCenter.x * Mathf.Cos(angle) - pointRelativeToCenter.z * Mathf.Sin(angle);
            float newZ = pointRelativeToCenter.x * Mathf.Sin(angle) + pointRelativeToCenter.z * Mathf.Cos(angle);
            
            building.points[i] = new Vector3(newX, pointRelativeToCenter.y, newZ) + center;
        }
        
        EditorUtility.SetDirty(building);
        building.SetDirty(); // 使用SetDirty而不是直接重建
    }
    
    // 缩放所有点
    private void ScaleAllPoints(float scale)
    {
        if (building.points.Count == 0) return;
        
        Undo.RecordObject(building, "Scale Building Points");
        
        // 计算中心点
        Vector3 center = Vector3.zero;
        foreach (var point in building.points)
        {
            center += point;
        }
        center /= building.points.Count;
        
        // 缩放所有点
        for (int i = 0; i < building.points.Count; i++)
        {
            Vector3 pointRelativeToCenter = building.points[i] - center;
            building.points[i] = center + pointRelativeToCenter * scale;
        }
        
        EditorUtility.SetDirty(building);
        building.SetDirty(); // 使用SetDirty而不是直接重建
    }

    // 绘制所有边缘线段
    private void DrawAllEdges()
    {
        for (int i = 0; i < b.m_edges.Count; i++)
        {
            // 自定义颜色基于线段状态
            if (i == currentClosestLine) 
                Handles.color = selectedLineColor;
            else if (i == currentEditedLine)
                Handles.color = activeLineColor;
            else
                Handles.color = normalLineColor;
            
            float width = (i == currentClosestLine || i == currentEditedLine) ? selectedLineWidth : lineWidth;
            
            // 获取边缘的世界坐标点
            Edge currentEdge = b.m_edges[i];
            
            // 绘制线段
            Handles.DrawAAPolyLine(width * thicknessMultiply, currentEdge.p1, currentEdge.p2);
            
            // 线段标签
            if (i == currentClosestLine || i == currentEditedLine)
            {
                Vector3 midPoint = (currentEdge.p1 + currentEdge.p2) * 0.5f;
                GUIStyle labelStyle = new GUIStyle();
                labelStyle.normal.textColor = handleTextColor;
                labelStyle.fontSize = 12;
                labelStyle.fontStyle = FontStyle.Bold;
                labelStyle.alignment = TextAnchor.MiddleCenter;
                
                // 在线段中间显示索引
                Handles.Label(midPoint + Vector3.up * 0.5f, $"线段 {i}", labelStyle);
                
                // 显示线段长度
                float length = Vector3.Distance(currentEdge.p1, currentEdge.p2);
                Handles.Label(midPoint + Vector3.up * 0.2f, $"长度: {length:F2}", labelStyle);
                
                // 绘制法线指示器 - 高级蓝色光效
                Vector3 normal = currentEdge.normalDirrection.normalized;
                
                // 绘制渐变线
                Color startColor = new Color(0.2f, 0.8f, 1f, 0.9f);
                Color endColor = new Color(0.0f, 0.4f, 1f, 0.3f);
                
                // 主法线线段
                Handles.color = startColor;
                Handles.DrawAAPolyLine(4f, midPoint, midPoint + normal * 1.5f);
                
                // 法线箭头
                Handles.color = startColor;
                float arrowSize = 0.3f * thicknessMultiply;
                Handles.ConeHandleCap(0, midPoint + normal * 1.5f, 
                    Quaternion.LookRotation(normal), 
                    arrowSize, EventType.Repaint);
                
                // 绘制辅助线 - 法线周围的光效
                Handles.color = endColor;
                float angle = 30f;
                for (int j = 0; j < 3; j++) {
                    Vector3 offset = Quaternion.Euler(0, j * angle, 0) * normal * 1.2f;
                    Handles.DrawAAPolyLine(2f, midPoint, midPoint + offset);
                    
                    offset = Quaternion.Euler(0, -j * angle, 0) * normal * 1.2f;
                    Handles.DrawAAPolyLine(2f, midPoint, midPoint + offset);
                }
            }
        }
    }
    
    // 处理点的选择和编辑
    private void HandlePointSelection()
    {
        // 添加更多错误检查和日志
        if (b == null)
        {
            Debug.LogWarning("Building reference is null in HandlePointSelection");
            return;
        }
        
        if (b.points == null)
        {
            Debug.LogWarning("Points list is null in HandlePointSelection");
            return;
        }
        
        if (b.points.Count == 0)
        {
            // 这是正常情况，没有点可以选择
            return;
        }
        
        // 处理当前正在编辑的点
        if (currentEditedPoint >= 0 && currentEditedPoint < b.points.Count)
        {
            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(b, "Point position movement");
            
            try
            {
                // 获取当前编辑点的世界坐标
                Vector3 currentWorldPos = b.transform.TransformPoint(b.points[currentEditedPoint]);
                
                // 计算新位置，但暂不直接应用
                Vector3 newWorldPosition = cursor;
                bool positionChanged = Vector3.Distance(currentWorldPos, newWorldPosition) > 0.01f;
                
                if (positionChanged)
                {
                    // 将世界坐标转换回局部坐标
                    b.points[currentEditedPoint] = b.transform.InverseTransformPoint(newWorldPosition);
                    
                    var isLinked = LinkDragedPointToOtherBuildingsPoints();
                    if (isLinked == false) SnapCurrentPointOverGrid();
                    
                    // 使用SetDirty而不是直接重建，减少抖动
                    b.SetDirty();
                }

                // 绘制当前编辑点的信息
                DrawPointInfo(currentEditedPoint, b.transform.TransformPoint(b.points[currentEditedPoint]));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Error handling edited point: " + e.Message);
            }
            
            // 仅在编辑结束时强制更新
            if (Event.current.type == EventType.MouseUp)
            {
                b.SetDirty(); // 使用SetDirty而不是直接重建
                currentEditedPoint = -1;
                Debug.Log("Point editing finished");
            }
        }
        else if (currentEditedPoint != -1)
        {
            // 修正无效的点索引
            Debug.LogWarning("Invalid point index: " + currentEditedPoint + ", resetting");
            currentEditedPoint = -1;
        }
        
        // 检测最近的点，用于悬停效果
        try
        {
            float closestDistance = hoverDistance;
            currentClosestPoint = -1;
            
            for (int i = 0; i < b.points.Count; i++)
            {
                Vector3 worldPoint = b.transform.TransformPoint(b.points[i]);
                float distance = Vector3.Distance(worldPoint, cursor);
                
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    currentClosestPoint = i;
                }
            }
            
            // 检测点击事件开始点拖拽
            if (currentClosestPoint != -1 && Event.current.type == EventType.MouseDown && Event.current.button == 0 && !Event.current.alt)
            {
                currentEditedPoint = currentClosestPoint;
                Debug.Log("Started editing point: " + currentEditedPoint);
                Event.current.Use(); // 重要：阻止事件继续传播
                HandleUtility.Repaint(); // 强制重绘
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("Error detecting closest point: " + e.Message);
        }
        
        // 绘制所有点
        for (int i = 0; i < b.points.Count; i++)
        {
            try
            {
                DrawBuilderPoint(i);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Error drawing point {i}: " + e.Message);
            }
        }
    }
    
    // 绘制建筑点
    private void DrawBuilderPoint(int index)
    {
        if (b == null || index < 0 || index >= b.points.Count)
            return;
        
        List<Vector3> worldPoints;
        try
        {
            worldPoints = b.GetWorldPoints();
            if (worldPoints == null || index >= worldPoints.Count)
                return;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("Error getting world points: " + e.Message);
            return;
        }
        
        Vector3 point = worldPoints[index];
        Handles.color = index == currentEditedPoint ? activePointColor : (index == currentClosestPoint ? hoverPointColor : normalPointColor);
        
        float actualPointSize = pointSize;
        if (index == currentEditedPoint || index == currentClosestPoint)
            actualPointSize *= 1.3f; // 选中或悬停时放大显示
        
        Handles.SphereHandleCap(0, point, Quaternion.identity, actualPointSize, EventType.Repaint);

        // 计算并绘制点的法线方向指示器
        if (worldPoints.Count > 1 && (index == currentEditedPoint || index == currentClosestPoint))
        {
            try
            {
                Vector3 pointNormal = CalculatePointNormal(index);
                
                // 调整紫色为更好看的色调
                Color normalColor = new Color(0.7f, 0.4f, 1f, 0.9f);
                
                // 固定箭头长度，移除脉动效果
                float normalArrowLength = 1.4f;
                
                // 绘制更粗的线条
                Handles.color = new Color(normalColor.r, normalColor.g, normalColor.b, 0.8f);
                Handles.DrawAAPolyLine(3.5f, point, point + pointNormal * normalArrowLength);
                
                // 绘制更粗的锥形箭头，固定大小
                Handles.color = new Color(normalColor.r, normalColor.g, normalColor.b, 0.9f);
                Vector3 arrowTip = point + pointNormal * normalArrowLength;
                Handles.ConeHandleCap(0, arrowTip, Quaternion.LookRotation(pointNormal), 
                    0.28f, EventType.Repaint);
                
                // 显示加粗的"N"字母标识
                GUIStyle normalLabelStyle = new GUIStyle();
                normalLabelStyle.normal.textColor = normalColor;
                normalLabelStyle.fontSize = 16; // 较大字体
                normalLabelStyle.fontStyle = FontStyle.Bold; // 加粗
                normalLabelStyle.alignment = TextAnchor.MiddleCenter; // 居中对齐
                Handles.Label(arrowTip + pointNormal * 0.2f, "N", normalLabelStyle);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Error drawing normal indicator: " + e.Message);
            }
        }

        // 显示点的序号
        try
        {
            DrawPointInfo(index, point);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("Error drawing point info: " + e.Message);
        }
    }

    /// <summary>
    /// 计算点的法线方向
    /// </summary>
    private Vector3 CalculatePointNormal(int pointIndex)
    {
        try
        {
            // 基本检查
            if (b == null)
            {
                Debug.LogWarning("Building reference is null in CalculatePointNormal");
                return Vector3.forward;
            }
            
            List<Vector3> worldPoints;
            try
            {
                worldPoints = b.GetWorldPoints();
                
                // 检查worldPoints是否有效
                if (worldPoints == null)
                {
                    Debug.LogWarning("GetWorldPoints returned null in CalculatePointNormal");
                    return Vector3.forward;
                }
                
                // 点数量检查
                if (worldPoints.Count < 2)
                {
                    Debug.LogWarning("Not enough points to calculate normal (need at least 2)");
                    return Vector3.forward;
                }
                
                // 索引检查
                if (pointIndex < 0 || pointIndex >= worldPoints.Count)
                {
                    Debug.LogWarning($"Point index {pointIndex} out of range (0-{worldPoints.Count-1})");
                    return Vector3.forward;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("Error getting world points: " + e.Message);
                return Vector3.forward;
            }
            
            // 法线计算
            Vector3 normal = Vector3.zero;
            int count = 0;
            
            try
            {
                // 获取点所在的两条边的法线，然后取平均值
                int prevIndex = pointIndex - 1;
                int nextIndex = pointIndex + 1;
                
                // 如果是闭合图形，处理首尾点
                if (prevIndex < 0) prevIndex = worldPoints.Count - 1;
                if (nextIndex >= worldPoints.Count) nextIndex = 0;
                
                // 安全检查索引范围
                if (prevIndex < 0 || prevIndex >= worldPoints.Count || 
                    nextIndex < 0 || nextIndex >= worldPoints.Count)
                {
                    Debug.LogWarning($"Invalid prev/next indices: prev={prevIndex}, next={nextIndex}, count={worldPoints.Count}");
                    return Vector3.forward;
                }
                
                // 计算与前一个点形成的边的法线
                Vector3 prevEdge = worldPoints[pointIndex] - worldPoints[prevIndex];
                if (prevEdge.magnitude > 0.001f) // 避免零长度边
                {
                    Vector3 prevNormal = Vector3.Cross(prevEdge, Vector3.up).normalized;
                    normal += prevNormal;
                    count++;
                }
                
                // 计算与后一个点形成的边的法线
                Vector3 nextEdge = worldPoints[nextIndex] - worldPoints[pointIndex];
                if (nextEdge.magnitude > 0.001f) // 避免零长度边
                {
                    Vector3 nextNormal = Vector3.Cross(nextEdge, Vector3.up).normalized;
                    normal += nextNormal;
                    count++;
                }
                
                // 计算平均法线
                if (count > 0)
                    normal = (normal / count).normalized;
                else
                    return Vector3.forward; // 默认前向
                    
                // 确保法线朝向建筑物外部
                // 计算建筑中心点
                Vector3 center = Vector3.zero;
                foreach (var point in worldPoints)
                {
                    center += point;
                }
                center /= worldPoints.Count;
                center.y = worldPoints[pointIndex].y; // 保持相同的Y坐标
                
                // 从中心到当前点的向量
                Vector3 fromCenter = (worldPoints[pointIndex] - center);
                
                // 确保fromCenter不是零向量
                if (fromCenter.magnitude < 0.001f)
                    return normal;
                    
                fromCenter = fromCenter.normalized;
                fromCenter.y = 0; // 忽略Y轴，只考虑水平方向
                
                // 如果法线与从中心出发的向量点积为负，说明法线朝内，需要反转
                if (Vector3.Dot(normal, fromCenter) < 0)
                {
                    normal = -normal;
                }
                
                return normal;
            }
            catch (System.Exception e)
            {
                Debug.LogError("Error calculating point normal: " + e.Message + "\n" + e.StackTrace);
                return Vector3.forward; // 默认前向
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("Critical error in CalculatePointNormal: " + e.Message + "\n" + e.StackTrace);
            return Vector3.forward;
        }
    }
    
    // 绘制点的详细信息
    private void DrawPointInfo(int index, Vector3 position)
    {
        GUIStyle style = new GUIStyle();
        style.normal.textColor = handleTextColor;
        style.fontSize = 12;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.MiddleLeft;
        
        // 在点上方显示详细信息
        Handles.BeginGUI();
        Vector3 screenPos = HandleUtility.WorldToGUIPoint(position + Vector3.up * 0.7f);
        GUI.Box(new Rect(screenPos.x - 5, screenPos.y - 5, 120, 70), "", EditorStyles.helpBox);
        GUI.Label(new Rect(screenPos.x, screenPos.y, 110, 20), $"点 {index}", style);
        GUI.Label(new Rect(screenPos.x, screenPos.y + 20, 110, 20), $"X: {position.x:F2}", style);
        GUI.Label(new Rect(screenPos.x, screenPos.y + 40, 110, 20), $"Z: {position.z:F2}", style);
        Handles.EndGUI();
        
        // 绘制连接线
        Handles.color = new Color(1f, 1f, 1f, 0.5f);
        
        // 连接到相邻点的线
        int prevIndex = (index > 0) ? index - 1 : b.points.Count - 1;
        int nextIndex = (index < b.points.Count - 1) ? index + 1 : 0;
        
        // 使用世界坐标而非本地坐标
        List<Vector3> worldPoints = b.GetWorldPoints();
        
        // 确保索引在有效范围内
        if (prevIndex >= 0 && prevIndex < worldPoints.Count && 
            nextIndex >= 0 && nextIndex < worldPoints.Count)
        {
            Handles.DrawDottedLine(position, worldPoints[prevIndex], 2f);
            Handles.DrawDottedLine(position, worldPoints[nextIndex], 2f);
        }
    }
    
    // 绘制点添加指示器
    private void DrawPointAdditionIndicator()
    {
        // 绘制十字指示器 - 蓝色调
        Handles.color = new Color(0.0f, 0.7f, 1f, 0.8f);
        float crossSize = 0.5f;
        Handles.DrawAAPolyLine(2f, cursor - Vector3.forward * crossSize, cursor + Vector3.forward * crossSize);
        Handles.DrawAAPolyLine(2f, cursor - Vector3.right * crossSize, cursor + Vector3.right * crossSize);
        
        // 绘制圆形指示器
        Handles.color = new Color(0.1f, 0.6f, 1f, 0.3f);
        Handles.DrawSolidDisc(cursor, Vector3.up, 0.3f);
        
        // 绘制脉冲效果
        float pulse = (Mathf.Sin(Time.realtimeSinceStartup * 4f) + 1f) * 0.5f;
        Handles.color = new Color(0.0f, 0.8f, 1f, 0.3f - pulse * 0.2f);
        Handles.DrawWireDisc(cursor, Vector3.up, 0.6f + pulse * 0.3f);
        
        // 绘制指示文字
        GUIStyle style = new GUIStyle();
        style.normal.textColor = new Color(0.0f, 0.8f, 1f, 1f);
        style.fontSize = 12;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.MiddleCenter;
        
        Handles.Label(cursor + Vector3.up * 0.7f, "点击添加点", style);
    }

    // 绘制调试标签
    private void DrawDebugLabels()
    {
        if (b.debugLabes)
        {
            GUIStyle style = new GUIStyle();
            
            foreach (var debug in b.debugTexts)
            {
                style.normal.textColor = debug.color;
                Handles.Label(debug.position, debug.text, style);
            }
        }
    }
    
    // 绘制建筑边界区域，替代原来的DrawRedArea
    private void DrawBuildingBounds()
    {
        if (b.points.Count < 3) return;
        
        // 计算建筑边界
        // 使用世界坐标而不是局部坐标
        List<Vector3> worldPoints = b.GetWorldPoints();
        
        Vector3 center = Vector3.zero;
        foreach (var point in worldPoints)
        {
            center += point;
        }
        center /= worldPoints.Count;
        
        float radius = 0;
        foreach (var point in worldPoints)
        {
            float dist = Vector3.Distance(center, point);
            if (dist > radius) radius = dist;
        }
        
        // 绘制建筑边界圆 - 使用蓝色调
        Handles.color = new Color(0.1f, 0.3f, 0.8f, 0.15f);
        Handles.DrawSolidDisc(center, Vector3.up, radius);
        
        // 绘制边界轮廓
        Handles.color = new Color(0.1f, 0.6f, 1f, 0.5f);
        Handles.DrawWireDisc(center, Vector3.up, radius);
        
        // 绘制径向辅助线
        Handles.color = new Color(0.2f, 0.4f, 0.9f, 0.3f);
        int segments = 12;
        for (int i = 0; i < segments; i++)
        {
            float angle = (i * 360f / segments) * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            Handles.DrawLine(center, center + direction * radius);
        }
        
        // 连接所有点形成轮廓
        Handles.color = new Color(0.0f, 0.7f, 1f, 0.6f);
        for (int i = 0; i < worldPoints.Count; i++)
        {
            int nextIndex = (i < worldPoints.Count - 1) ? i + 1 : 0;
            Handles.DrawLine(worldPoints[i], worldPoints[nextIndex]);
        }
    }

    // 绘制模式切换信息
    private void DrawModeInfo()
    {
        Handles.BeginGUI();
        GUIStyle style = new GUIStyle();
        style.normal.textColor = Color.white;
        style.fontSize = 12;
        style.fontStyle = FontStyle.Bold;
        
        string modeText = isEditingFloors ? "楼层编辑模式 (按F切换)" : "点编辑模式 (按F切换)";
        GUI.Box(new Rect(10, 10, 200, 25), modeText, EditorStyles.helpBox);
        
        Handles.EndGUI();
    }
    
    // 绘制楼层编辑UI
    private void DrawFloorsEditingUI(ProceduralBuilding building)
    {
        if (building == null) return;
        
        // 计算建筑中心点 - 使用世界坐标
        Vector3 buildingCenter = Vector3.zero;
        List<Vector3> worldPoints = building.GetWorldPoints();
        
        if (worldPoints.Count > 0)
        {
            foreach (var p in worldPoints)
                buildingCenter += p;
            buildingCenter /= worldPoints.Count;
        }
        else
        {
            buildingCenter = building.transform.position;
        }
        
        float floorHeight = building.currentBuildingHeight / (building.stagesCount > 0 ? building.stagesCount : 1);
        
        // 增加虚线粗度 - 绘制当前建筑高度指示器
        Handles.color = floorHandleColor;
        Handles.DrawDottedLine(buildingCenter, buildingCenter + Vector3.up * building.currentBuildingHeight, 8f);
        
        // 绘制顶部控制手柄
        Handles.color = floorHighlightColor;
        Vector3 topPosition = buildingCenter + Vector3.up * building.currentBuildingHeight;
        
        // 检测是否悬停在顶部手柄上
        float handleDistance = HandleUtility.DistanceToCircle(topPosition, 1f);
        bool isHoveringTopHandle = handleDistance < 10f; // 降低阈值，因为DistanceToCircle值不同
        
        float handleSize = isHoveringTopHandle ? floorHandleSize * 1.2f : floorHandleSize;
        
        // 绘制控制手柄，并允许拖拽
        EditorGUI.BeginChangeCheck();
        var fmh_1173_70_638827749444353574 = Quaternion.identity; Vector3 newTopPosition = Handles.FreeMoveHandle(topPosition, 
            handleSize, Vector3.zero, Handles.CylinderHandleCap);
        
        if (EditorGUI.EndChangeCheck())
        {
            // 用户拖拽了高度控制手柄
            Undo.RecordObject(building, "Change Building Height");
            
            // 记录原始高度，用于计算变化量
            float originalHeight = building.currentBuildingHeight;
            
            // 计算新的楼层数
            float newHeight = newTopPosition.y - buildingCenter.y;
            if (newHeight < 0.5f) newHeight = 0.5f; // 最小高度
            
            // 根据拖拽方式决定是调整楼层数还是楼层高度
            if (Event.current.control)
            {
                // 按住Ctrl调整楼层高度
                building.currentBuildingHeight = newHeight;
            }
            else
            {
                // 默认调整楼层数
                int newFloorCount = Mathf.Max(1, Mathf.RoundToInt(newHeight / floorHeight));
                building.stagesCount = newFloorCount;
            }
            
            // 高度变化
            float heightChange = building.currentBuildingHeight - originalHeight;
            
            // 如果是简化模式，需要重新生成建筑以更新楼层
            if (building.simplifiedMode)
            {
                building.RecalculateBuilding();
            }
            else
            {
                building.SetDirty();
            }
            
            SceneView.RepaintAll();
        }
        
        // 在顶部绘制高度信息
        GUIStyle topLabelStyle = new GUIStyle();
        topLabelStyle.normal.textColor = floorTextColor;
        topLabelStyle.fontSize = 12;
        topLabelStyle.fontStyle = FontStyle.Bold;
        topLabelStyle.alignment = TextAnchor.MiddleCenter;
        
        Handles.Label(topPosition + Vector3.up * 0.5f, 
            $"高度: {building.currentBuildingHeight:F1}m\n楼层: {building.stagesCount}层(含商铺)", 
            topLabelStyle);
        
        // 绘制拖拽提示
        if (isHoveringTopHandle)
        {
            GUIStyle tipStyle = new GUIStyle();
            tipStyle.normal.textColor = new Color(1f, 1f, 0.7f, 1f);
            tipStyle.fontSize = 10;
            tipStyle.alignment = TextAnchor.MiddleCenter;
            
            Handles.Label(topPosition + Vector3.up * 1f, 
                "拖拽调整楼层数\n按住Ctrl拖拽调整高度", 
                tipStyle);
        }
        
        // 绘制楼层分隔线
        for (int i = 0; i < building.stagesCount; i++)
        {
            float y = buildingCenter.y + i * floorHeight;
            Vector3 floorPos = new Vector3(buildingCenter.x, y, buildingCenter.z);
            
            // 检测是否悬停在楼层上
            handleDistance = HandleUtility.DistanceToCircle(floorPos, 0.8f);
            bool isHoveringFloor = handleDistance < 10f;
            
            Handles.color = isHoveringFloor ? floorHighlightColor : floorHandleColor;
            
            // 绘制楼层平面
            float radius = 0;
            foreach (var point in worldPoints)
            {
                float dist = Vector2.Distance(
                    new Vector2(point.x, point.z), 
                    new Vector2(buildingCenter.x, buildingCenter.z));
                if (dist > radius) radius = dist;
            }
            
            // 只绘制楼层指示线，不绘制圆盘
            if (isHoveringFloor)
            {
                // 绘制高亮线，但不画完整圆
                Handles.color = floorHighlightColor;
                
                // 只在指定点之间绘制线段而不是整个圆
                for (int j = 0; j < worldPoints.Count; j++)
                {
                    int nextIndex = (j < worldPoints.Count - 1) ? j + 1 : 0;
                    Vector3 p1 = new Vector3(worldPoints[j].x, floorPos.y, worldPoints[j].z);
                    Vector3 p2 = new Vector3(worldPoints[nextIndex].x, floorPos.y, worldPoints[nextIndex].z);
                    Handles.DrawLine(p1, p2);
                }
            }
            else
            {
                // 仍然保留一些指示，但用虚线
                Handles.color = floorHandleColor;
                for (int j = 0; j < worldPoints.Count; j++)
                {
                    int nextIndex = (j < worldPoints.Count - 1) ? j + 1 : 0;
                    Vector3 p1 = new Vector3(worldPoints[j].x, floorPos.y, worldPoints[j].z);
                    Vector3 p2 = new Vector3(worldPoints[nextIndex].x, floorPos.y, worldPoints[nextIndex].z);
                    Handles.DrawDottedLine(p1, p2, 4f);
                }
            }
            
            // 如果悬停在楼层上，显示楼层信息和控制按钮
            if (isHoveringFloor)
            {
                // 楼层信息
                GUIStyle floorLabelStyle = new GUIStyle();
                floorLabelStyle.normal.textColor = floorTextColor;
                floorLabelStyle.fontSize = 12;
                floorLabelStyle.fontStyle = FontStyle.Bold;
                
                // 根据楼层位置显示不同文本
                string floorText = (i == 0) ? "1层 (商铺)" : $"{i+1}层";
                Handles.Label(floorPos + Vector3.up * 0.2f, floorText, floorLabelStyle);
                
                // 绘制楼层控制按钮
                Handles.BeginGUI();
                Vector3 screenPos = HandleUtility.WorldToGUIPoint(floorPos);
                
                // 按钮布局
                Rect buttonRect = new Rect(screenPos.x + 30, screenPos.y - 12, 20, 20);
                
                // 增加楼层按钮
                if (GUI.Button(buttonRect, "+"))
                {
                    Undo.RecordObject(building, "Add Floor");
                    building.stagesCount++;
                    
                    // 如果是简化模式，直接重建
                    if (building.simplifiedMode)
                    {
                        building.RecalculateBuilding();
                    }
                    else
                    {
                        building.SetDirty();
                    }
                    
                    SceneView.RepaintAll();
                }
                
                // 减少楼层按钮
                buttonRect.x += 25;
                if (GUI.Button(buttonRect, "-"))
                {
                    if (building.stagesCount > 1)
                    {
                        Undo.RecordObject(building, "Remove Floor");
                        building.stagesCount--;
                        
                        // 如果是简化模式，直接重建
                        if (building.simplifiedMode)
                        {
                            building.RecalculateBuilding();
                        }
                        else
                        {
                            building.SetDirty();
                        }
                        
                        SceneView.RepaintAll();
                    }
                    else
                    {
                        // 至少保留一层（商铺层）
                        SceneView.lastActiveSceneView.ShowNotification(new GUIContent("至少需要保留1层(商铺层)"));
                    }
                }
                
                Handles.EndGUI();
            }
        }
        
        // 绘制建筑材质选择界面
        DrawBuildingMaterialsUI(buildingCenter);
    }
    
    // 绘制建筑材质选择界面
    private void DrawBuildingMaterialsUI(Vector3 buildingCenter)
    {
        // 在屏幕左上角绘制材质选择面板
        Handles.BeginGUI();
        
        // 固定在左上角
        Rect panelRect = new Rect(20, 50, 200, 130);
        GUI.Box(panelRect, "建筑材质", EditorStyles.helpBox);
        
        // 墙体材质
        Rect labelRect = new Rect(panelRect.x + 10, panelRect.y + 25, 70, 20);
        GUI.Label(labelRect, "墙体材质:");
        
        Rect fieldRect = new Rect(panelRect.x + 85, panelRect.y + 25, 105, 20);
        EditorGUI.BeginChangeCheck();
        Material newWallMat = (Material)EditorGUI.ObjectField(fieldRect, building.WallsMaterial, typeof(Material), false);
        if (EditorGUI.EndChangeCheck() && newWallMat != building.WallsMaterial)
        {
            Undo.RecordObject(building, "Change Wall Material");
            building.WallsMaterial = newWallMat;
            building.SetDirty();
        }
        
        // 屋顶材质
        labelRect.y += 25;
        GUI.Label(labelRect, "屋顶材质:");
        
        fieldRect.y += 25;
        EditorGUI.BeginChangeCheck();
        Material newRoofMat = (Material)EditorGUI.ObjectField(fieldRect, building.roofMaterial, typeof(Material), false);
        if (EditorGUI.EndChangeCheck() && newRoofMat != building.roofMaterial)
        {
            Undo.RecordObject(building, "Change Roof Material");
            building.roofMaterial = newRoofMat;
            building.SetDirty();
        }
        
        // 窗户发光颜色
        labelRect.y += 25;
        GUI.Label(labelRect, "窗户发光:");
        
        fieldRect.y += 25;
        fieldRect.width = 60;
        EditorGUI.BeginChangeCheck();
        float newEmission = EditorGUI.Slider(fieldRect, building.windowEmissions, 0f, 1f);
        if (EditorGUI.EndChangeCheck() && newEmission != building.windowEmissions)
        {
            Undo.RecordObject(building, "Change Window Emission");
            building.windowEmissions = newEmission;
            building.SetDirty();
        }
        
        // 发光颜色
        labelRect.y += 25;
        GUI.Label(labelRect, "发光颜色:");
        
        fieldRect.y += 25;
        fieldRect.width = 105;
        EditorGUI.BeginChangeCheck();
        Color newColor = EditorGUI.ColorField(fieldRect, building.windowEmissionColor);
        if (EditorGUI.EndChangeCheck() && newColor != building.windowEmissionColor)
        {
            Undo.RecordObject(building, "Change Emission Color");
            building.windowEmissionColor = newColor;
            building.SetDirty();
        }
        
        Handles.EndGUI();
    }

    // 新增：绘制删除模式指示器
    private void DrawDeletionModeIndicator()
    {
        Handles.BeginGUI();
        
        // 创建闪烁效果
        float pulse = Mathf.Sin(Time.realtimeSinceStartup * 5f) * 0.3f + 0.7f;
        Color deleteColor = new Color(1f, 0.3f, 0.3f, pulse);
        
        // 绘制删除模式指示器
        GUIStyle style = new GUIStyle(EditorStyles.helpBox);
        style.fontSize = 12;
        style.normal.textColor = Color.white;
        style.alignment = TextAnchor.MiddleCenter;
        style.fontStyle = FontStyle.Bold;
        
        // 使用脉冲颜色
        Color oldColor = GUI.backgroundColor;
        GUI.backgroundColor = deleteColor;
        
        // 绘制在屏幕顶部
        GUI.Box(new Rect(Screen.width/2 - 100, 10, 200, 30), "删除模式 - 点击删除点", style);
        
        // 恢复颜色
        GUI.backgroundColor = oldColor;
        
        Handles.EndGUI();
    }
    
    // 新增：绘制快捷键帮助
    private void DrawHotkeyHelp()
    {
        Handles.BeginGUI();
        
        // 绘制在屏幕右下角
        GUIStyle style = new GUIStyle(EditorStyles.helpBox);
        style.fontSize = 11;
        style.normal.textColor = Color.white;
        
        string hotkeyText = 
            "快捷键:\n" +
            "Shift+点击: 添加点\n" +
            "Alt+点击: 删除点\n" +
            "F: 切换楼层编辑\n" +
            "Esc: 退出编辑模式";
        
        // 使用半透明背景
        GUI.backgroundColor = new Color(0.1f, 0.1f, 0.2f, 0.8f);
        GUI.Box(new Rect(Screen.width - 150, Screen.height - 110, 140, 100), hotkeyText, style);
        
        Handles.EndGUI();
    }

    // 绘制编辑模式信息
    private void DrawEditModeInfo()
    {
        Handles.BeginGUI();
        GUIStyle style = new GUIStyle();
        style.normal.textColor = Color.white;
        style.fontSize = 14;
        style.fontStyle = FontStyle.Bold;
        
        GUI.Box(new Rect(10, 10, 250, 30), "编辑模式已关闭 (Inspector中可开启)", EditorStyles.helpBox);
        
        Handles.EndGUI();
    }

    /// <summary>
    /// 根据预设类型返回描述文本
    /// </summary>
    /// <param name="preset">建筑预设类型</param>
    /// <returns>描述文本</returns>
    private string GetPresetDescription(SimplifiedBuildingPreset preset)
    {
        switch (preset)
        {
            case SimplifiedBuildingPreset.Office:
                return "写字楼：现代商务建筑，拥有大面积玻璃幕墙，适合商业区域布置。特点是简洁、专业的外观，多层规律的窗户排布。";
                
            case SimplifiedBuildingPreset.Residential:
                return "住宅楼：典型的城市住宅建筑，温馨的色调，适合居民区。特点是朴实的外观，小而密集的窗户，通常有斜顶屋顶。";
                
            case SimplifiedBuildingPreset.Commercial:
                return "商业建筑：适合购物中心、超市等商业用途，底层大面积橱窗设计。特点是时尚的外观，醒目的底层商铺区域。";
                
            case SimplifiedBuildingPreset.Factory:
                return "工厂：工业风格建筑，简单实用的设计。特点是朴素的外墙，较少的窗户，宽敞的车间空间和人字形屋顶。";
                
            case SimplifiedBuildingPreset.School:
                return "学校：教育机构建筑，明亮友好的外观。特点是宽敞的窗户设计，整洁的外墙，适合校园环境。";
                
            case SimplifiedBuildingPreset.Hospital:
                return "医院：医疗机构建筑，干净、专业的外观。特点是规整的布局，明亮的色调，注重功能性的设计。";
                
            case SimplifiedBuildingPreset.Custom:
            default:
                return "自定义：完全根据您的设置生成建筑，不应用任何预设样式，可以自由调整所有参数。";
        }
    }
}
}