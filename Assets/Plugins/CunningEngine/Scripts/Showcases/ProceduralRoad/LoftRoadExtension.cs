#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Splines;
using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using Sirenix.OdinInspector;
using System.Linq;

namespace UnityEditor.Splines.Extension
{
    public enum RoadType
    {
        Road_A,
        Road_B,
        Road_C,
        Road_D,
        Road_E,
        Road_F,
        Road_G,
        Road_H,
        Road_I,
        Road_J,
        Road_K,


        Road_AA,
        Road_BB,
        Road_CC,
        Road_DD,
        Road_EE,
        Byway_A,
        Byway_B,
        Byway_C,
        Byway_D,
        Byway_E,
        Overpass_MainA,
        Overpass_MainB,
        Overpass_MainC,
        Overpass_RampA,
        Overpass_RampB,
        Road_Exten_A,
        GreenBelt_Define,
        Traffic_Pedestrian,
        Traffic_ZebraCross,
        Train_A,
        Train_B,
        Train_C,
        TRoad_A,
        TRoad_B,
        TRoad_C,
        TRoad_D,
        TRoad_E,
        TRoad_F,
        TRoad_G,
        River_A,
        River_B,
    }

    public enum RoadMarkType
    {
        BrokenWhiteLine = 1,
        BrokenYellowLine,
        DirectionalArrow_TurnLeft,
        DirectionalArrow_TurnRight,
        DirectionalArrow_Straight,
        DirectionalArrow_StraightAndTurnLeft,
        DirectionalArrow_StraightAndTurnRight,

    }

    public static class RoadDefaultsInfors
    {
        //定义车道宽

        public const float LaneWidth_Road_A = 5.5f;
        public const float LaneWidth_Road_C = 4.3f;
        public const float LaneWidth_Road_B = 5.5f;
        public const float LaneWidth_Road_D = 5.5f;
        public const float LaneWidth_Road_E = 5.5f;
        public const float LaneWidth_Road_F = 5.5f;
        public const float LaneWidth_Road_G = 5.5f;
        public const float LaneWidth_Road_H = 5.5f;
        public const float LaneWidth_Road_I = 7f;
        public const float LaneWidth_Road_J = 5.5f;
        public const float LaneWidth_Road_K = 4.3f;
        public const float Road_Exten_A = 5.5f;


        //定义特殊道路总宽
        public const float Road_B_TotalWidth = 26f;
        public const float Road_C_TotalWidth = 16.5f;
        public const float Road_D_TotalWidth = 8.5f; 
        public const float Road_E_TotalWidth = 7f;
        public const float Road_AA_TotalWidth = 34f;
        public const float Road_BB_TotalWidth = 26f;
        public const float Road_CC_TotalWidth = 16.5f;
        public const float Road_DD_TotalWidth = 8.5f;
        public const float Road_EE_TotalWidth = 7f;
        public const float Byway_A_TotalWidth = 6f;
        public const float Byway_B_TotalWidth = 6f;
        public const float Byway_C_TotalWidth = 6f;
        public const float Byway_D_TotalWidth = 6f;
        public const float Byway_E_TotalWidth = 6f;
        public const float Overpass_MainA_TotalWidth = 35f;
        public const float Overpass_MainB_TotalWidth = 27f;
        public const float Overpass_MainC_TotalWidth = 27f;
        public const float Overpass_RampA_TotalWidth = 11.5f;
        public const float Overpass_RampB_TotalWidth = 7.5f;
        public const float Traffic_Pedestrian = 5f;
        public const float Traffic_ZebraCross = 5f;
        public const float Train_A_TotalWidth = 10;
        public const float Train_B_TotalWidth = 7;
        public const float Train_C_TotalWidth = 4;
        public const float TRoad_A_TotalWidth = 25.5f;
        public const float TRoad_B_TotalWidth = 16.5f;
        public const float TRoad_C_TotalWidth = 8.5f;
        public const float TRoad_D_TotalWidth = 8f;
        public const float TRoad_E_TotalWidth = 6f;
        public const float TRoad_F_TotalWidth = 4f;
        public const float TRoad_G_TotalWidth = 4f;
        public const float River_A = 50f;
        public const float River_B = 50f;
        // 左侧人行道宽度
        public const float Sidewalk_Road_A_Left = 5.5f;
        public const float Sidewalk_Road_B_Left = 5.5f;
        public const float Sidewalk_Road_C_Left = 5.5f;
        public const float Sidewalk_Road_D_Left = 5.5f;
        public const float Sidewalk_Road_E_Left = 5.5f;
        public const float Sidewalk_Road_F_Left = 5.5f;
        public const float Sidewalk_Road_G_Left = 5.5f;
        public const float Sidewalk_Road_H_Left = 5.5f;
        public const float Sidewalk_Road_I_Left = 5.5f;
        public const float Sidewalk_Road_J_Left = 5.5f;
        public const float Sidewalk_Road_K_Left = 5.5f;
        public const float Sidewalk_Road_AA_Left = 5.5f;
        public const float Sidewalk_Road_BB_Left = 5.5f;
        public const float Sidewalk_Road_CC_Left = 5.5f;
        public const float Sidewalk_Road_DD_Left = 5.5f;
        public const float Sidewalk_Road_EE_Left = 5.5f;
        
        // 右侧人行道宽度
        public const float Sidewalk_Road_A_Right = 5.5f;
        public const float Sidewalk_Road_B_Right = 5.5f;
        public const float Sidewalk_Road_C_Right = 5.5f;
        public const float Sidewalk_Road_D_Right = 5.5f;
        public const float Sidewalk_Road_E_Right = 5.5f;
        public const float Sidewalk_Road_F_Right = 5.5f;
        public const float Sidewalk_Road_G_Right = 5.5f;
        public const float Sidewalk_Road_H_Right = 5.5f;
        public const float Sidewalk_Road_I_Right = 5.5f;
        public const float Sidewalk_Road_J_Right = 5.5f;
        public const float Sidewalk_Road_K_Right = 5.5f;
        public const float Sidewalk_Road_AA_Right = 5.5f;
        public const float Sidewalk_Road_BB_Right = 5.5f;
        public const float Sidewalk_Road_CC_Right = 5.5f;
        public const float Sidewalk_Road_DD_Right = 5.5f;
        public const float Sidewalk_Road_EE_Right = 5.5f;
        
        public const float Sidewalk_Default = 0f;
        public const float GreenBelt_Road_A = 2.5f;
        public const float GreenBelt_Road_B = 2.5f;
        public const float GreenBelt_Road_C = 2.5f;
        public const float GreenBelt_Road_D = 2.5f;
        public const float GreenBelt_Road_E = 2.5f;
        public const float GreenBelt_Road_F = 2.5f;
        public const float GreenBelt_Road_G = 2.5f;
        public const float GreenBelt_Road_H = 2.5f;
        public const float GreenBelt_Road_I = 2.5f;
        public const float GreenBelt_Road_J = 2.5f;
        public const float GreenBelt_Road_K = 2.5f;
        public const float GreenBelt_Default = 0f;
        public const float GreenBelt_Define = 5.5f;

        //定义中文按钮
        private static readonly Dictionary<RoadType, string> RoadNames = new Dictionary<RoadType, string>
        {

            { RoadType.Road_A, "城市双向六车道（4+2）(5.5)+绿化带(地块型)"  },
            { RoadType.Road_B, "城市双向六车道（3+3）(5.5)(地块型)" },
            { RoadType.Road_C, "城市道路五车道（3+2）(4.3)(地块型)" },
            { RoadType.Road_D, "城市双向五车道（3+2）(5.5)+ 绿化带（GTA）(地块型)" },
            { RoadType.Road_E, "城市双向四车道（2+2）(5.5)(地块型)" },
            { RoadType.Road_F, "城市双向四车道（3+1）(5.5)+ 停车带(地块型)" },
            { RoadType.Road_G, "城市双向三车道(2+1)（5.5）(地块型)" },

            { RoadType.Road_H, "城市双向三车道（5.5） + 绿化带(地块型)" },
            { RoadType.Road_I, "城市双向二车道（7）(地块型)" },
            { RoadType.Road_J, "城市双向二车道（5.5）(地块型)" },
            { RoadType.Road_K, "城市双向二车道（4.3）(地块型)" },

                    
            { RoadType.Road_AA, "城市道路八车道(非地块型)" },
            { RoadType.Road_BB, "城市道路六车道（非地块型)" },
            { RoadType.Road_CC, "城市道路四车道(非地块型)" },
            { RoadType.Road_DD, "城市道路二车道（非地块型)" },
            { RoadType.Road_EE, "城市道路单车道(非地块型)" },
            { RoadType.GreenBelt_Define, "自定义绿化带" },
                    
                    
            { RoadType.Byway_A, "沿海步道(地块内)" },
            { RoadType.Byway_B, "巷道B(地块内)" },
            { RoadType.Byway_C, "巷道C(地块内)" },
            { RoadType.Byway_D, "巷道D(地块内)" },
            { RoadType.Byway_E, "巷道E(地块内)" },
            { RoadType.Overpass_MainA, "高速路/高架桥八车道" },
            { RoadType.Overpass_MainB, "高速路/高架桥六车道" },
            { RoadType.Overpass_MainC, "高速路/高架桥六车道样式2" },
            { RoadType.Overpass_RampA, "高架桥匝道双车道" },
            { RoadType.Overpass_RampB, "高架桥匝道单车道" },
            { RoadType.Road_Exten_A, "道路附加/单车道补充道" },
            
            { RoadType.Train_A, "铁轨三轨道" },
            { RoadType.Train_B, "铁轨二轨道" },
            { RoadType.Train_C, "铁轨一轨道" },
            { RoadType.TRoad_A, "山路柏油六车道" },
            { RoadType.TRoad_B, "山路柏油四车道" },
            { RoadType.TRoad_C, "山路柏油二车道" },
            { RoadType.TRoad_D, "山路土路二车道" },
            { RoadType.TRoad_E, "山路土路单行道" },
            { RoadType.TRoad_F, "行人土路" },
            { RoadType.TRoad_G, "栈道" },
            { RoadType.River_A, "人工运河A" },
            { RoadType.River_B, "人工运河B" },
            { RoadType.Traffic_Pedestrian, "交通编辑/行人交通" },
            { RoadType.Traffic_ZebraCross, "交通编辑/斑马线" },
        };

        // 左右车道数量
        private static readonly Dictionary<RoadType, (int leftLaneCount, int rightLaneCount)> LaneCounts = new Dictionary<RoadType, (int, int)>
        {
            { RoadType.Road_A, (4, 2) },
            { RoadType.Road_B, (3, 3) },
            { RoadType.Road_C, (3, 2) },
            { RoadType.Road_D, (3, 2) },
            { RoadType.Road_E, (2, 2) },
            { RoadType.Road_F, (3, 1) },
            { RoadType.Road_G, (2, 1) },
            { RoadType.Road_H, (2, 1) },
            { RoadType.Road_I, (1, 1) },
            { RoadType.Road_J, (1, 1) },
            { RoadType.Road_K, (1, 1) },
            { RoadType.Road_AA, (3, 1) },
            { RoadType.Road_BB, (3, 3) },
            { RoadType.Road_CC, (2, 2) },
            { RoadType.Road_DD, (1, 1) },
            { RoadType.Road_EE, (1, 1) },
            { RoadType.Byway_A, (1, 0) },
            { RoadType.Byway_B, (1, 1) },
            { RoadType.Byway_C, (1, 1) },
            { RoadType.Byway_D, (1, 1) },
            { RoadType.Byway_E, (1, 1) },
            { RoadType.Overpass_MainA, (4, 4) },
            { RoadType.Overpass_MainB, (3, 3) },
            { RoadType.Overpass_MainC, (3, 3) },
            { RoadType.Overpass_RampA, (1, 1) },
            { RoadType.Overpass_RampB, (1, 1) },
            { RoadType.Road_Exten_A, (0, 1) },
            { RoadType.GreenBelt_Define, (0, 1) },
            { RoadType.Train_A, (3, 0) },
            { RoadType.Train_B, (2, 0) },
            { RoadType.Train_C, (1, 0) },
            { RoadType.TRoad_A, (3, 3) },
            { RoadType.TRoad_B, (2, 2) },
            { RoadType.TRoad_C, (1, 1) },
            { RoadType.TRoad_D, (1, 1) },
            { RoadType.TRoad_E, (1, 0) },
            { RoadType.TRoad_F, (0, 0) },
            { RoadType.TRoad_G, (0, 0) },
            { RoadType.River_A, (0, 0) },
            { RoadType.River_B, (0, 0) },
            { RoadType.Traffic_Pedestrian, (0,0) },
            { RoadType.Traffic_ZebraCross, (0,0) },
            
        };

        // 是否为单行道
        private static readonly Dictionary<RoadType, bool> IsOneWay = new Dictionary<RoadType, bool>
        {
            { RoadType.Road_A, false },
            { RoadType.Road_B, false },
            { RoadType.Road_C, false },
            { RoadType.Road_D, false },
            { RoadType.Road_E, false },
            { RoadType.Road_AA, false },
            { RoadType.Road_BB, false },
            { RoadType.Road_CC, false },
            { RoadType.Road_DD, false },
            { RoadType.Road_EE, false },
            { RoadType.Byway_A, false },
            { RoadType.Byway_B, false },
            { RoadType.Byway_C, false },
            { RoadType.Byway_D, false },
            { RoadType.Byway_E, false },
            { RoadType.Overpass_MainA, false },
            { RoadType.Overpass_MainB, false },
            { RoadType.Overpass_MainC, false },
            { RoadType.Overpass_RampA, false },
            { RoadType.Overpass_RampB, false },
            { RoadType.Road_Exten_A, false },
            { RoadType.Train_A, true },
            { RoadType.Train_B, true },
            { RoadType.Train_C, true },
            { RoadType.TRoad_A, false },
            { RoadType.TRoad_B, false },
            { RoadType.TRoad_C, false },
            { RoadType.TRoad_D, false },
            { RoadType.TRoad_E, true },
            { RoadType.TRoad_F, false },
            { RoadType.TRoad_G, false },
            { RoadType.River_A, false },
            { RoadType.River_B, false },
            { RoadType.Traffic_Pedestrian, false },
            { RoadType.Traffic_ZebraCross, false },
            
        };

        //车道，方向
        public static (int leftLaneCount, int rightLaneCount) GetDefaultLaneCounts(RoadType type, RoadInfo customRoadInfo = null)
        {
            if (customRoadInfo != null)
            {
                return (customRoadInfo.leftLaneCount, customRoadInfo.rightLaneCount);
            }
            return LaneCounts[type];
        }

        public static bool GetIsOneWay(RoadType type, RoadInfo customRoadInfo = null)
        {
            if (customRoadInfo != null)
            {
                return customRoadInfo.isOneWay;
            }
            return IsOneWay.ContainsKey(type) ? IsOneWay[type] : false;
        }




        // 宽度，有归纳的用归纳方法，没有的直接用特殊宽度。
        public static float GetDefaultWidth(RoadType type, RoadInfo customRoadInfo = null)
        {
            var laneCounts = LaneCounts[type];
            float laneWidth = GetDefaultLaneWidth(type);
            
            if (customRoadInfo != null)
            {
                return customRoadInfo.defaultWidth;
            }
            
            // 特殊指定宽度的道路类型保持不变
            switch (type)
            {
                case RoadType.Overpass_MainA: return Overpass_MainA_TotalWidth;
                case RoadType.Overpass_MainB: return Overpass_MainB_TotalWidth;
                case RoadType.Overpass_MainC: return Overpass_MainC_TotalWidth;
                case RoadType.Overpass_RampA: return Overpass_RampA_TotalWidth;
                case RoadType.Overpass_RampB: return Overpass_RampB_TotalWidth;
                case RoadType.Traffic_Pedestrian: return Traffic_Pedestrian;
                case RoadType.Traffic_ZebraCross: return Traffic_ZebraCross;
                case RoadType.Train_A: return Train_A_TotalWidth;
                case RoadType.Train_B: return Train_B_TotalWidth;
                case RoadType.Train_C: return Train_C_TotalWidth;
                case RoadType.TRoad_A: return TRoad_A_TotalWidth;
                case RoadType.TRoad_B: return TRoad_B_TotalWidth;
                case RoadType.TRoad_C: return TRoad_C_TotalWidth;
                case RoadType.TRoad_D: return TRoad_D_TotalWidth;
                case RoadType.TRoad_E: return TRoad_E_TotalWidth;
                case RoadType.River_A: return River_A;
                case RoadType.River_B: return River_B;
                default:
                    // 新的计算方式：车道宽 * 车道数 + 路缘宽 + 马路牙子宽
                    float totalLaneWidth = (laneCounts.leftLaneCount + laneCounts.rightLaneCount) * laneWidth;
                    float curbWidth = 1.0f; // 默认路缘宽度
                    float roadEdgeWidth = 0.3f; // 默认马路牙子宽度
                    
                    // 计算路缘和马路牙子的总宽度（两侧）
                    float curbTotalWidth = 2.0f * curbWidth; // 两侧路缘的总宽度
                    float roadEdgeTotalWidth = 2.0f * roadEdgeWidth; // 两侧马路牙子的总宽度
                    
                    return totalLaneWidth + curbTotalWidth + roadEdgeTotalWidth;
            }
        }
        public static float GetDefaultLaneWidth(RoadType type)
        {
            switch (type)
            {
                case RoadType.Road_A: return LaneWidth_Road_A;
                case RoadType.Road_B: return LaneWidth_Road_B;
                case RoadType.Road_C: return LaneWidth_Road_C;
                case RoadType.Road_D: return LaneWidth_Road_D;
                case RoadType.Road_E: return LaneWidth_Road_E;
                case RoadType.Road_F: return LaneWidth_Road_F;
                case RoadType.Road_G: return LaneWidth_Road_G;
                case RoadType.Road_H: return LaneWidth_Road_H;
                case RoadType.Road_I: return LaneWidth_Road_I;
                case RoadType.Road_J: return LaneWidth_Road_J;


                default: return 5.5f;
            }
        }
        public static float GetDefaultSidewalkWidth(RoadType type)
        {
            switch (type)
            {
                case RoadType.Road_A: return Sidewalk_Road_A_Left;
                case RoadType.Road_B: return Sidewalk_Road_B_Left;
                case RoadType.Road_C: return Sidewalk_Road_C_Left;
                case RoadType.Road_D: return Sidewalk_Road_D_Left;
                case RoadType.Road_E: return Sidewalk_Road_E_Left;
                case RoadType.Road_F: return Sidewalk_Road_F_Left;
                case RoadType.Road_G: return Sidewalk_Road_G_Left;
                case RoadType.Road_H: return Sidewalk_Road_H_Left;
                case RoadType.Road_I: return Sidewalk_Road_I_Left;
                case RoadType.Road_J: return Sidewalk_Road_J_Left;
                case RoadType.Road_K: return Sidewalk_Road_K_Left;


                case RoadType.Road_AA: return Sidewalk_Road_AA_Left;
                case RoadType.Road_BB: return Sidewalk_Road_BB_Left;
                case RoadType.Road_CC: return Sidewalk_Road_CC_Left;
                case RoadType.Road_DD: return Sidewalk_Road_DD_Left;
                case RoadType.Road_EE: return Sidewalk_Road_EE_Left;
                default: return Sidewalk_Default;
            }
        }
        
        public static float GetDefaultRightSidewalkWidth(RoadType type)
        {
            switch (type)
            {
                case RoadType.Road_A: return Sidewalk_Road_A_Right;
                case RoadType.Road_B: return Sidewalk_Road_B_Right;
                case RoadType.Road_C: return Sidewalk_Road_C_Right;
                case RoadType.Road_D: return Sidewalk_Road_D_Right;
                case RoadType.Road_E: return Sidewalk_Road_E_Right;
                case RoadType.Road_F: return Sidewalk_Road_F_Right;
                case RoadType.Road_G: return Sidewalk_Road_G_Right;
                case RoadType.Road_H: return Sidewalk_Road_H_Right;
                case RoadType.Road_I: return Sidewalk_Road_I_Right;
                case RoadType.Road_J: return Sidewalk_Road_J_Right;
                case RoadType.Road_K: return Sidewalk_Road_K_Right;


                case RoadType.Road_AA: return Sidewalk_Road_AA_Right;
                case RoadType.Road_BB: return Sidewalk_Road_BB_Right;
                case RoadType.Road_CC: return Sidewalk_Road_CC_Right;
                case RoadType.Road_DD: return Sidewalk_Road_DD_Right;
                case RoadType.Road_EE: return Sidewalk_Road_EE_Right;
                default: return Sidewalk_Default;
            }
        }

        public static float GetDefaultGreenBeltWidth(RoadType type)
        {
            switch (type)
            {
                case RoadType.Road_A: return GreenBelt_Road_A;
                case RoadType.Road_B: return GreenBelt_Road_B;
                case RoadType.Road_C: return GreenBelt_Road_C;
                case RoadType.Road_D: return GreenBelt_Road_C;
                case RoadType.Road_E: return GreenBelt_Road_C;
                case RoadType.Road_F: return GreenBelt_Road_C;
                case RoadType.Road_G: return GreenBelt_Road_C;
                case RoadType.Road_H: return GreenBelt_Road_C;
                case RoadType.Road_I: return GreenBelt_Road_C;
                case RoadType.Road_J: return GreenBelt_Road_C;
                case RoadType.Road_K: return GreenBelt_Road_C;

                default: return GreenBelt_Default;
            }
        }

        public static string GetMaterialPath(RoadType roadType, RoadInfo customRoadInfo = null)
        {
            if (customRoadInfo != null)
            {
                return AssetDatabase.GetAssetPath(customRoadInfo.roadMaterial);
            }
            // 所有道路类型都使用同一个材质
            return "Assets/ArtResources/PcgAsset/Materials/PcgToolsMat/im_road_001.mat";
        }

        public static string GetRoadMarkPrefabPathByType(RoadMarkType type)
        {
            switch (type)
            {
                case RoadMarkType.BrokenWhiteLine: return "Assets/ArtResources/Hdas/RoadSystem/Prefabs/RoadBrokenLine/YellowBrokenMark.prefab";
                case RoadMarkType.BrokenYellowLine: return "Assets/ArtResources/Hdas/RoadSystem/Prefabs/RoadBrokenLine/WhiteBrokenMark.prefab";
                case RoadMarkType.DirectionalArrow_TurnLeft: return "Assets/ArtResources/Scene/SceneObject/Traffic/RoadLine/General_LeftTurnArrow_01.prefab";
                case RoadMarkType.DirectionalArrow_TurnRight: return "Assets/ArtResources/Scene/SceneObject/Traffic/RoadLine/General_RightTurnArrow_01.prefab";
                case RoadMarkType.DirectionalArrow_Straight: return "Assets/ArtResources/Scene/SceneObject/Traffic/RoadLine/General_StraightArrow_01.prefab";
                case RoadMarkType.DirectionalArrow_StraightAndTurnLeft: return "Assets/ArtResources/Scene/SceneObject/Traffic/RoadLine/General_StraightAndLeftTurnArrow_01.prefab";
                case RoadMarkType.DirectionalArrow_StraightAndTurnRight: return "Assets/ArtResources/Scene/SceneObject/Traffic/RoadLine/General_StraightAndRightTurnArrow_01.prefab";
                default: return string.Empty;
            }
        }

        public static string GetSidewalkMaterialPath()
        {
            return "Assets/ArtResources/PcgAsset/Materials/PcgToolsMat/Road_Sidewalk01.mat";
        }

        public static string GetGreenBeltMaterialPath()
        {
            return "Assets/Hdas/RoadSystem/Materials/GreenBelt.mat";
        }

        public static string GetBrokenWhiteLineMaterialPath()
        {
            return "Assets/ArtResources/Hdas/RoadSystem/Materials/BrokenWhiteLine.mat";
        }
        public static string GetRoadMarkingLinePrefabPath(RoadType roadType, int laneId)
        {
            var laneCounts = LaneCounts[roadType];
            // has green belt
            bool hasGreenBelt = roadType == RoadType.Road_A || roadType == RoadType.Road_D || roadType == RoadType.Road_H;
            if (hasGreenBelt && (laneId == (laneCounts.leftLaneCount + laneCounts.rightLaneCount + 1) / 2 || laneId == (laneCounts.leftLaneCount + laneCounts.rightLaneCount - 1) / 2))
            {
                return "Assets/ArtResources/Hdas/RoadSystem/Prefabs/RoadBrokenLine/YellowBrokenMark.prefab";
            }
            return "Assets/ArtResources/Hdas/RoadSystem/Prefabs/RoadBrokenLine/WhiteBrokenMark.prefab";
        }

        public static string GetDirectionalArrowPrefabPath(RoadType roadType, int laneId)
        {
            var laneCounts = LaneCounts[roadType];
            string path = string.Empty;
            int rightLaneCount = laneCounts.rightLaneCount;
            int leftLaneCount = laneCounts.leftLaneCount;
            int arrowID = laneId - leftLaneCount + 1;
            if (rightLaneCount == 3)
            {
                switch (arrowID)
                {
                    case 1:
                        path = GetRoadMarkPrefabPathByType(RoadMarkType.DirectionalArrow_TurnLeft);
                        break;
                    case 2:
                        path = GetRoadMarkPrefabPathByType(RoadMarkType.DirectionalArrow_Straight);
                        break;
                    case 3:
                        path = GetRoadMarkPrefabPathByType(RoadMarkType.DirectionalArrow_StraightAndTurnRight);
                        break;
                }
            }
            if (rightLaneCount == 2)
            {
                switch (arrowID)
                {
                    case 1:
                        path = GetRoadMarkPrefabPathByType(RoadMarkType.DirectionalArrow_Straight);
                        break;
                    case 2:
                        path = GetRoadMarkPrefabPathByType(RoadMarkType.DirectionalArrow_TurnRight);
                        break;
                }
            }

            return path;
        }

        public static string GetRoadName(RoadType type)
        {
            return RoadNames.TryGetValue(type, out string name) ? name : "Error ：道路类型中文枚举未定义，请查看svn后联系代码改动者";
        }
    }






    [Serializable]
    public class LoftRoadExtensionData
    {
        [LabelText("道路类型")]
        public RoadType roadTypeEnum;
        [LabelText("自定义道路信息")]
        public RoadInfo customRoadInfo;

        [SerializeField]
        [LabelText("左侧车道数")]
        public int leftLaneCount;

        [SerializeField]
        [LabelText("右侧车道数")]
        public int rightLaneCount;

        [LabelText("内侧曲线")]
        public AnimationCurve inCurve = AnimationCurve.Linear(0, 0, 1, 1);
        [LabelText("外侧曲线")]
        public AnimationCurve outCurve = AnimationCurve.Linear(1,1,0,0);
        [LabelText("道路类型名称")]
        public string roadType;
        [LabelText("启用绿化带")]
        public bool greenBelt = false;

        [LabelText("道路宽度")]
        public SplineData<float> width;

        [HideInInspector]
        public float[,] terrainHeights;

        [ShowIf("ContainBridgeType")]
        [LabelText("桥起始结束点对")]
        [TableList(HideToolbar = false, NumberOfItemsPerPage = 10, ShowIndexLabels = true, ShowPaging = true)]
        public List<BridgePair> bridgePairs = new List<BridgePair>();

        [SerializeField]
        [LabelText("PCG道路引导点")]
        [TableList(HideToolbar = false, NumberOfItemsPerPage = 10, ShowIndexLabels = true, ShowPaging = true)]
        public List<RoadPointData> points = new List<RoadPointData>();

        [LabelText("车道宽度")]
        public SplineData<float> laneWidth;
        [LabelText("左侧人行道宽度")]
        public SplineData<float> leftSidewalkWidth;
        [LabelText("右侧人行道宽度")]
        public SplineData<float> rightSidewalkWidth;
        [LabelText("绿化带宽度")]
        public SplineData<float> greenBeltWidth;
        
        // 添加路缘相关参数
        [LabelText("启用路缘")]
        public bool enableCurb = true;
        [LabelText("路缘高度")]
        [ShowIf("enableCurb")]
        [Range(0f, 5f)]
        public float curbHeight = 0.1f;
        [LabelText("路缘宽度")]
        [ShowIf("enableCurb")]
        [Range(0f, 5f)]
        public float curbWidth = 1.0f;
        [LabelText("路缘材质")]
        [ShowIf("enableCurb")]
        public Material curbMaterial;
        
        [LabelText("路缘U方向缩放")]
        [ShowIf("enableCurb")]
        [Range(0.1f, 10f)]
        public float curbUVScaleU = 0.5f;
        
        [LabelText("路缘V方向缩放")]
        [ShowIf("enableCurb")]
        [Range(0.1f, 10f)]
        public float curbUVScaleV = 1.0f;
        
        [LabelText("路缘内侧倒角宽度")]
        [ShowIf("enableCurb")]
        [Range(0.01f, 0.5f)]
        public float curbChamferWidth = 0.05f;
        
        [LabelText("路缘内侧倒角高度")]
        [ShowIf("enableCurb")]
        [Range(0.01f, 0.5f)]
        public float curbChamferHeight = 0.05f;
        
        // 添加马路牙子相关参数
        [LabelText("启用马路牙子")]
        public bool enableRoadEdge = true;
        [LabelText("马路牙子高度")]
        [ShowIf("enableRoadEdge")]
        [Range(0.05f, 1f)]
        public float roadEdgeHeight = 0.15f;
        [LabelText("马路牙子宽度")]
        [ShowIf("enableRoadEdge")]
        [Range(0.1f, 2f)]
        public float roadEdgeWidth = 0.3f;
        [LabelText("马路牙子材质")]
        [ShowIf("enableRoadEdge")]
        public Material roadEdgeMaterial;
        [LabelText("马路牙子U方向缩放")]
        [ShowIf("enableRoadEdge")]
        [Range(0.1f, 10f)]
        public float roadEdgeUVScaleU = 0.5f;
        [LabelText("马路牙子V方向缩放")]
        [ShowIf("enableRoadEdge")]
        [Range(0.1f, 10f)]
        public float roadEdgeUVScaleV = 1.0f;
        
        [LabelText("马路牙子内侧倒角宽度")]
        [ShowIf("enableRoadEdge")]
        [Range(0.01f, 0.5f)]
        public float roadEdgeChamferWidth = 0.05f;
        
        [LabelText("马路牙子内侧倒角高度")]
        [ShowIf("enableRoadEdge")]
        [Range(0.01f, 0.5f)]
        public float roadEdgeChamferHeight = 0.05f;
        
        [LabelText("人行道材质")]
        public Material sidewalkMaterial;
        [LabelText("绿化带材质")]
        public Material greenBeltMaterial;

        [SerializeField]
        [LabelText("网格高度偏移")]
        public float meshOffset = 1f;
        
        [SerializeField]
        [LabelText("UV水平偏移")]
        public float uvHorizontalOffset = 0.082f;

        [LabelText("单向道路")]
        public bool isOneWay;
        [LabelText("道路标记数据")]
        public RoadMarkingData roadMarking;

        [ShowIf("@this.roadTypeEnum == RoadType.GreenBelt_Define")]
        [LabelText("内侧偏移")]
        public float inOffset;

        [ShowIf("@this.roadTypeEnum == RoadType.GreenBelt_Define")]
        [LabelText("外侧偏移")]
        public float outOffset;

        [ShowIf("@this.roadTypeEnum == RoadType.GreenBelt_Define")]
        [LabelText("内侧宽度")]
        public float inWidth;

        [ShowIf("@this.roadTypeEnum == RoadType.GreenBelt_Define")]
        [LabelText("外侧宽度")]
        public float outWidth;

        [ShowIf("@this.roadTypeEnum == RoadType.GreenBelt_Define")]
        [LabelText("内侧约束宽度")]
        public float inConsWidth;

        [ShowIf("@this.roadTypeEnum == RoadType.GreenBelt_Define")]
        [LabelText("外侧约束宽度")]
        public float outConsWidth;
        public int Count => width.Count;
        public float WidthValue 
        { 
            get 
            {
                // 如果是特殊指定宽度的道路类型，直接返回默认值
                switch (roadTypeEnum)
                {
                    case RoadType.Overpass_MainA:
                    case RoadType.Overpass_MainB:
                    case RoadType.Overpass_MainC:
                    case RoadType.Overpass_RampA:
                    case RoadType.Overpass_RampB:
                    case RoadType.Traffic_Pedestrian:
                    case RoadType.Traffic_ZebraCross:
                    case RoadType.Train_A:
                    case RoadType.Train_B:
                    case RoadType.Train_C:
                    case RoadType.TRoad_A:
                    case RoadType.TRoad_B:
                    case RoadType.TRoad_C:
                    case RoadType.TRoad_D:
                    case RoadType.TRoad_E:
                    case RoadType.River_A:
                    case RoadType.River_B:
                        // 如果width.DefaultValue已设置（不为0），则使用它，否则使用GetDefaultWidth方法计算
                        return width.DefaultValue != 0 ? width.DefaultValue : RoadDefaultsInfors.GetDefaultWidth(roadTypeEnum, customRoadInfo);
                    default:
                        // 新的计算方式：车道宽 * 车道数 + 路缘宽 + 马路牙子宽
                        float totalLaneWidth = (leftLaneCount + rightLaneCount) * laneWidth.DefaultValue;
                        float curbTotalWidth = enableCurb ? curbWidth * 2 : 0f;
                        float roadEdgeTotalWidth = enableRoadEdge ? roadEdgeWidth * 2 : 0f;
                        return totalLaneWidth + curbTotalWidth + roadEdgeTotalWidth;
                }
            }
        }
        public float LaneWidthValue => laneWidth.DefaultValue;
        public float SidewalkWidthValue => leftSidewalkWidth.DefaultValue + rightSidewalkWidth.DefaultValue;
        public float GreenBeltWidthValue => greenBeltWidth.DefaultValue;
        // 添加路缘总宽度计算属性
        public float CurbTotalWidth => enableCurb ? curbWidth * 2 : 0f;

        [SerializeField]
        [LabelText("采样点数据")]
        public List<RoadSamplePoint> samplePoints = new List<RoadSamplePoint>();

        [SerializeField]
        [LabelText("路口分析后的有序点列表")]
        public List<RoadSamplePoint> orderedJunctionPoints = new List<RoadSamplePoint>();

        [SerializeField]
        [LabelText("连接数据")]
        public List<RoadConnection> connections = new List<RoadConnection>();

        [FoldoutGroup("算法参数")]
        [SerializeField]
        public RoadAlgorithmParameters algorithmParameters = new RoadAlgorithmParameters();

        public void Cook()
        {
            if (customRoadInfo != null)
            {
                roadType = customRoadInfo.roadName;
                leftLaneCount = customRoadInfo.leftLaneCount;
                rightLaneCount = customRoadInfo.rightLaneCount;
                isOneWay = customRoadInfo.isOneWay;
                
                // 从自定义道路信息中获取路缘参数
                enableCurb = customRoadInfo.enableCurb;
                curbHeight = customRoadInfo.curbHeight;
                curbWidth = customRoadInfo.curbWidth;
                curbUVScaleU = customRoadInfo.curbUVScaleU;
                curbUVScaleV = customRoadInfo.curbUVScaleV;
                curbChamferWidth = customRoadInfo.curbChamferWidth;
                curbChamferHeight = customRoadInfo.curbChamferHeight;
                
                // 从自定义道路信息中获取马路牙子参数
                enableRoadEdge = customRoadInfo.enableRoadEdge;
                roadEdgeHeight = customRoadInfo.roadEdgeHeight;
                roadEdgeWidth = customRoadInfo.roadEdgeWidth;
                roadEdgeUVScaleU = customRoadInfo.roadEdgeUVScaleU;
                roadEdgeUVScaleV = customRoadInfo.roadEdgeUVScaleV;
                roadEdgeChamferWidth = customRoadInfo.roadEdgeChamferWidth;
                roadEdgeChamferHeight = customRoadInfo.roadEdgeChamferHeight;
            }
            else
            {
                roadType = roadTypeEnum.ToString();
                // 只在车道数为0时设置默认值
                if (leftLaneCount == 0 || rightLaneCount == 0)
                {
                    var laneCounts = RoadDefaultsInfors.GetDefaultLaneCounts(roadTypeEnum);
                    leftLaneCount = laneCounts.leftLaneCount;
                    rightLaneCount = laneCounts.rightLaneCount;
                }
                isOneWay = RoadDefaultsInfors.GetIsOneWay(roadTypeEnum);
                
                // 使用默认的路缘参数
                enableCurb = true;
                curbHeight = 0.1f;
                curbWidth = 1.0f;
                curbUVScaleU = 0.5f;
                curbUVScaleV = 1.0f;
                curbChamferWidth = 0.05f;
                curbChamferHeight = 0.05f;
                
                // 使用默认的马路牙子参数
                enableRoadEdge = true;
                roadEdgeHeight = 0.15f;
                roadEdgeWidth = 0.3f;
                roadEdgeUVScaleU = 0.5f;
                roadEdgeUVScaleV = 1.0f;
                roadEdgeChamferWidth = 0.05f;
                roadEdgeChamferHeight = 0.05f;
            }
        }

        public void AddPoint(float3 point, bool isBridge = false)
        {
            points.Add(new RoadPointData { point = point, isBridge = isBridge });
        }
        
        public void ClearPoints()
        {
            points.Clear();
        }

        public void SetWidthValue(float width)
        {
            // 只在特殊指定宽度的道路类型上设置宽度值
            switch (roadTypeEnum)
            {
                case RoadType.Overpass_MainA:
                case RoadType.Overpass_MainB:
                case RoadType.Overpass_MainC:
                case RoadType.Overpass_RampA:
                case RoadType.Overpass_RampB:
                case RoadType.Traffic_Pedestrian:
                case RoadType.Traffic_ZebraCross:
                case RoadType.Train_A:
                case RoadType.Train_B:
                case RoadType.Train_C:
                case RoadType.TRoad_A:
                case RoadType.TRoad_B:
                case RoadType.TRoad_C:
                case RoadType.TRoad_D:
                case RoadType.TRoad_E:
                case RoadType.River_A:
                case RoadType.River_B:
                    this.width.DefaultValue = width;
                    break;
                default:
                    // 对于其他道路类型，不直接设置宽度，因为宽度是通过计算得出的
                    // 可以在这里添加日志，提示用户宽度是自动计算的
                    Debug.Log($"道路类型 {roadTypeEnum} 的宽度是自动计算的，不能直接设置。");
                    break;
            }
        }
        public void SetLaneWidthValue(float width)
        {
            this.laneWidth.DefaultValue = width;
        }
        public void SetSidewalkWidthValue(float width)
        {
            // 将总宽度平均分配给左右人行道
            this.leftSidewalkWidth.DefaultValue = width / 2;
            this.rightSidewalkWidth.DefaultValue = width / 2;
        }

        public void SetLeftSidewalkWidthValue(float width)
        {
            this.leftSidewalkWidth.DefaultValue = width;
        }

        public void SetRightSidewalkWidthValue(float width)
        {
            this.rightSidewalkWidth.DefaultValue = width;
        }

        public float GetLeftSidewalkWidthValue()
        {
            return this.leftSidewalkWidth.DefaultValue;
        }

        public float GetRightSidewalkWidthValue()
        {
            return this.rightSidewalkWidth.DefaultValue;
        }

        public void SetGreenBeltWidthValue(float width)
        {
            this.greenBeltWidth.DefaultValue = width;
        }

        public void InitializeMaterials()
        {
            if (customRoadInfo != null)
            {
                this.sidewalkMaterial = customRoadInfo.sidewalkMaterial;
                this.greenBeltMaterial = customRoadInfo.greenBeltMaterial;
                
                // 初始化路缘参数和材质
                this.enableCurb = customRoadInfo.enableCurb;
                this.curbHeight = customRoadInfo.curbHeight;
                this.curbWidth = customRoadInfo.curbWidth;
                this.curbUVScaleU = customRoadInfo.curbUVScaleU;
                this.curbUVScaleV = customRoadInfo.curbUVScaleV;
                this.curbChamferWidth = customRoadInfo.curbChamferWidth;
                this.curbChamferHeight = customRoadInfo.curbChamferHeight;
                // 如果自定义道路信息中有路缘材质，则使用它
                if (customRoadInfo.curbMaterial != null)
                {
                    this.curbMaterial = customRoadInfo.curbMaterial;
                }
                else
                {
                    // 使用指定的路缘材质
                    string curbPath = "Assets/ArtResources/PcgAsset/Models/GtaSample/hw1_rd_04_21_tex/mat_2.mat";
                    this.curbMaterial = AssetDatabase.LoadAssetAtPath<Material>(curbPath);
                    
                    // 如果指定材质加载失败，则使用人行道材质作为备选
                    if (this.curbMaterial == null)
                    {
                        Debug.LogWarning("无法加载指定的路缘材质，使用人行道材质作为备选");
                        string sidewalkPath = RoadDefaultsInfors.GetSidewalkMaterialPath();
                        this.curbMaterial = AssetDatabase.LoadAssetAtPath<Material>(sidewalkPath);
                    }
                }
                
                // 初始化马路牙子参数和材质
                this.enableRoadEdge = customRoadInfo.enableRoadEdge;
                this.roadEdgeHeight = customRoadInfo.roadEdgeHeight;
                this.roadEdgeWidth = customRoadInfo.roadEdgeWidth;
                this.roadEdgeUVScaleU = customRoadInfo.roadEdgeUVScaleU;
                this.roadEdgeUVScaleV = customRoadInfo.roadEdgeUVScaleV;
                this.roadEdgeChamferWidth = customRoadInfo.roadEdgeChamferWidth;
                this.roadEdgeChamferHeight = customRoadInfo.roadEdgeChamferHeight;
                
                // 如果自定义道路信息中有马路牙子材质，则使用它
                if (customRoadInfo.roadEdgeMaterial != null)
                {
                    this.roadEdgeMaterial = customRoadInfo.roadEdgeMaterial;
                }
                else
                {
                    // 使用指定的马路牙子材质
                    string roadEdgePath = "Assets/ArtResources/PcgAsset/Models/GtaSample/hw1_rd_04_21_tex/mat_1.mat";
                    this.roadEdgeMaterial = AssetDatabase.LoadAssetAtPath<Material>(roadEdgePath);
                    
                    // 如果指定材质加载失败，则使用路缘材质作为备选
                    if (this.roadEdgeMaterial == null)
                    {
                        Debug.LogWarning("无法加载指定的马路牙子材质，使用路缘材质作为备选");
                        this.roadEdgeMaterial = this.curbMaterial;
                    }
                }
            }
            else
            {
                string sidewalkPath = RoadDefaultsInfors.GetSidewalkMaterialPath();
                this.sidewalkMaterial = AssetDatabase.LoadAssetAtPath<Material>(sidewalkPath);

                string greenBeltPath = RoadDefaultsInfors.GetGreenBeltMaterialPath();
                this.greenBeltMaterial = AssetDatabase.LoadAssetAtPath<Material>(greenBeltPath);
                
                // 初始化默认路缘参数
                this.enableCurb = true;
                this.curbHeight = 0.1f;
                this.curbWidth = 1.0f;
                this.curbUVScaleU = 0.5f;
                this.curbUVScaleV = 1.0f;
                this.curbChamferWidth = 0.05f;
                this.curbChamferHeight = 0.05f;
                
                // 使用指定的路缘材质
                string curbPath = "Assets/ArtResources/PcgAsset/Models/GtaSample/hw1_rd_04_21_tex/mat_2.mat";
                this.curbMaterial = AssetDatabase.LoadAssetAtPath<Material>(curbPath);
                
                // 如果指定材质加载失败，则使用人行道材质作为备选
                if (this.curbMaterial == null)
                {
                    Debug.LogWarning("无法加载指定的路缘材质，使用人行道材质作为备选");
                    this.curbMaterial = AssetDatabase.LoadAssetAtPath<Material>(sidewalkPath);
                }
                
                // 初始化默认马路牙子参数
                this.enableRoadEdge = true;
                this.roadEdgeHeight = 0.15f;
                this.roadEdgeWidth = 0.3f;
                this.roadEdgeUVScaleU = 0.5f;
                this.roadEdgeUVScaleV = 1.0f;
                
                // 初始化马路牙子材质
                string roadEdgePath = "Assets/ArtResources/PcgAsset/Models/GtaSample/hw1_rd_04_21_tex/mat_1.mat";
                this.roadEdgeMaterial = AssetDatabase.LoadAssetAtPath<Material>(roadEdgePath);
                
                // 如果指定材质加载失败，则使用路缘材质作为备选
                if (this.roadEdgeMaterial == null)
                {
                    Debug.LogWarning("无法加载指定的马路牙子材质，使用路缘材质作为备选");
                    this.roadEdgeMaterial = this.curbMaterial;
                }
            }
        }


        public List<RoadPointData> GetWorldPoints(Transform baseTransform)
        {
            List<RoadPointData> worldPoints = points.Select(
                p => new RoadPointData{ point = baseTransform.TransformPoint(p.point), isBridge = p.isBridge }
            ).ToList();
            return worldPoints;
        }

        public bool ContainsRoadMarkInEditor()
        {
            return IsHighWayType();
        }

        public bool IsHighWayType()
        {
            return roadTypeEnum == RoadType.Overpass_MainA || 
                   roadTypeEnum == RoadType.Overpass_MainB || 
                   roadTypeEnum == RoadType.Overpass_MainC || 
                   roadTypeEnum == RoadType.Overpass_RampA || 
                   roadTypeEnum == RoadType.Overpass_RampB;
        }

        public bool ContainBridgeType()
        {
            return roadTypeEnum == RoadType.Road_AA ||
                   roadTypeEnum == RoadType.Road_BB ||
                   roadTypeEnum == RoadType.Road_CC ||
                   roadTypeEnum == RoadType.Road_DD ||
                   roadTypeEnum == RoadType.Road_EE;
        }

        public void SyncTerrainData(TerrainData data)
        {
            terrainHeights = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
        }

        public float[,] GetTerrainHeights()
        {
            return terrainHeights;
        }

        public void AddBridgePair(List<ISelectableElement> selectableElements)
        {
            if (selectableElements.Count < 2)
            {
                EditorUtility.DisplayDialog("Error", "Please select two points to create a bridge pair", "OK");
                return;
            }
            var start = selectableElements[0].KnotIndex;
            var end = selectableElements[1].KnotIndex;
            bridgePairs.Add(new BridgePair(start, end));
        }

        public void RemoveBridgePair(List<ISelectableElement> selectableElements)
        {
            if (selectableElements.Count < 2)
            {
                EditorUtility.DisplayDialog("Error", "Please select two points to remove a bridge pair", "OK");
                return;
            }
            var start = selectableElements[0].KnotIndex;
            var end = selectableElements[1].KnotIndex;
            bridgePairs.RemoveAll(pair => (pair.StartIndex == start && pair.EndIndex == end) || (pair.StartIndex == end && pair.EndIndex == start));
        }

        public void CalculateAllWidth()
        {
            float totalWidth = 0;
            
            // 计算车道总宽度
            totalWidth += (leftLaneCount + rightLaneCount) * laneWidth.DefaultValue;
            
            // 添加人行道宽度
            totalWidth += leftSidewalkWidth.DefaultValue;
            totalWidth += rightSidewalkWidth.DefaultValue;
            
            // 添加路缘宽度
            if (enableCurb)
            {
                totalWidth += curbWidth * 2;
            }
            
            // 添加马路牙子宽度
            if (enableRoadEdge)
            {
                totalWidth += roadEdgeWidth * 2;
            }
            
            // 添加宽度扩展值
            totalWidth += algorithmParameters.widthExpand;
            
            algorithmParameters.allWidth = totalWidth;
        }
    }

    [Serializable]
    public class BridgePair
    {
        public BridgePair(int start, int end)
        {
            StartIndex = start;
            EndIndex = end;
        }

        [VerticalGroup("起始Knot下标")]
        [HideLabel]
        public int StartIndex;

        [VerticalGroup("结束Knot下标")]
        [HideLabel]
        public int EndIndex;
    }

    [Serializable]
    public class RoadPointData
    {
        [VerticalGroup("Point")]
        [TableColumnWidth(350)]
        [HideLabel]
        public float3 point;

        [VerticalGroup("IsBridge")]
        [HideLabel]
        public bool isBridge;
    }

    [Serializable]
    public class RoadMarkingData
    {
        [LabelText("标记网格")]
        public Mesh markingMesh;
        [LabelText("缩放")]
        public float scale = 1f;
        [LabelText("长度")]
        public float length = 3f;
        [LabelText("宽度")]
        public float width = 0.3f;
        [LabelText("间隔")]
        public float interval = 2.0f;
    }

    [Serializable]
    public class SplineDataContainer
    {
        [SerializeField]
        public List<Spline> splines;

        public List<LoftRoadExtensionData> roadDatas;
    }

    [Serializable]
    public class RoadSamplePoint
    {
        public float3 position;        // 点的世界空间位置
        public int roadIndex;          // 所属道路索引
        public float curveU;           // 曲线参数
        public float width;            // 道路宽度
        public bool isCross;           // 是否是交叉点
        public int splineIndex;        // 所属样条索引
        public bool isOriginalKnot;    // 是否是原始knot点
        public int originalKnotIndex;  // 原始knot的索引（如果是原始knot）
        public int curveIndex;         // 所属curve段的索引
        public int globalIndex;        // 全局序号
        public int segmentId;          // Logical segment id
        public int localSegmentIndex;  // Segment order inside spline
        public long startMarkerId;     // Segment start marker id
        public long endMarkerId;       // Segment end marker id
        public bool isSegmentBoundary; // Whether this sample is a segment boundary
    }

    [Serializable]
    public class RoadConnection
    {
        public RoadSamplePoint start;
        public RoadSamplePoint end;
    }

    [Serializable]
    public class RoadAlgorithmParameters
    {
        [LabelText("总宽度(自动计算)")]
        [ReadOnly]
        public float allWidth;          // 道路总宽度(自动计算)
        
        [LabelText("宽度扩展值")]
        [Tooltip("可以为正数或负数,用于微调检测宽度")]
        public float widthExpand = 2f;  // 检测宽度扩展值,允许输入正负数

        [LabelText("采样点间隔(米)")]
        [Tooltip("每隔多少米采样一个点，值越小采样越密集")]
        [Range(1f, 50f)]               // 修改范围为1-50米
        public float sampleInterval = 13f;  // 默认值改为13米
    }

    [Serializable]
    public class JunctionGroup
    {
        public List<RoadSamplePoint> points = new List<RoadSamplePoint>();
        public List<RoadSamplePoint> shapePoints = new List<RoadSamplePoint>();
        public Vector3 centerPosition;  // 组的中心点位置
        public float radius;           // 组的半径范围
    }
}
#endif
