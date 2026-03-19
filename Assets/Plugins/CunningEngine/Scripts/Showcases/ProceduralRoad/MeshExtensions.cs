#if UNITY_EDITOR
using UnityEngine;
using System.Collections.Generic;

namespace Unity.Splines.Examples
{
    public static class MeshExtensions
    {
        public static void SetSmoothingGroups(this Mesh mesh, int[] smoothingGroups)
        {
            // 将平滑组数据存储在UV7通道中
            var smoothingData = new List<Vector2>();
            var triangles = mesh.triangles;
            
            // 预先分配足够的空间
            for (int i = 0; i < mesh.vertexCount; i++)
            {
                smoothingData.Add(Vector2.zero);
            }
            
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int smoothingGroup = smoothingGroups[i / 3];
                smoothingData[triangles[i]] = new Vector2(smoothingGroup, 0);
                smoothingData[triangles[i + 1]] = new Vector2(smoothingGroup, 0);
                smoothingData[triangles[i + 2]] = new Vector2(smoothingGroup, 0);
            }
            
            mesh.SetUVs(6, smoothingData); // UV7 is index 6 (0-based)
            mesh.RecalculateNormals();
        }
    }
}
#endif 