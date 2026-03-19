using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;


namespace DevStuffs
{
    [CreateAssetMenu(fileName = "NewBuildPreset", menuName = "ProceduralBuild/CreatePreset", order = 51)]
    public class BuildPreset : ScriptableObject
    {
        public List<ProceduralStage> buildingStages = new List<ProceduralStage>();

        internal List<ProceduralBuilding> buildings = new List<ProceduralBuilding>();

        private void OnValidate() 
        {
            #if UNITY_EDITOR
            if(UnityEditor.Selection.activeObject != this)return; 
            buildings.Where(b => b != null).ToList().ForEach(b => b.SetDirty());
            #endif
        }
    }
}
