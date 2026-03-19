#if UNITY_EDITOR
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace Unity.Splines.Examples
{
    public static class MeshExtensions
    {
        private const string SMOOTHING_GROUPS = "_SmoothingGroups";

        public static void SetSmoothingGroups(this Mesh mesh, int[] smoothingGroups)
        {
            if (smoothingGroups == null || smoothingGroups.Length == 0)
                return;

            // 将整数数组转换为Vector2列表
            var smoothingGroupData = smoothingGroups.Select(id => new Vector2(id, 0)).ToList();
            mesh.SetUVs(7, smoothingGroupData);

            // 重新计算法线，考虑平滑组
            RecalculateNormalsWithSmoothingGroups(mesh, smoothingGroups);
        }

        private static void RecalculateNormalsWithSmoothingGroups(Mesh mesh, int[] smoothingGroups)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            Vector3[] normals = new Vector3[vertices.Length];

            // 创建一个字典来存储每个顶点在每个平滑组中的法线
            Dictionary<int, Dictionary<int, Vector3>> vertexSmoothingGroups = new Dictionary<int, Dictionary<int, Vector3>>();

            // 初始化字典
            for (int i = 0; i < vertices.Length; i++)
            {
                vertexSmoothingGroups[i] = new Dictionary<int, Vector3>();
            }

            // 计算每个三角形的法线并将其添加到相应的平滑组
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int smoothingGroup = smoothingGroups[i / 3];
                
                // 计算三角形法线
                Vector3 v1 = vertices[triangles[i]];
                Vector3 v2 = vertices[triangles[i + 1]];
                Vector3 v3 = vertices[triangles[i + 2]];
                Vector3 normal = Vector3.Cross(v2 - v1, v3 - v1).normalized;

                // 将法线添加到每个顶点的相应平滑组
                for (int j = 0; j < 3; j++)
                {
                    int vertexIndex = triangles[i + j];
                    if (!vertexSmoothingGroups[vertexIndex].ContainsKey(smoothingGroup))
                    {
                        vertexSmoothingGroups[vertexIndex][smoothingGroup] = normal;
                    }
                    else
                    {
                        vertexSmoothingGroups[vertexIndex][smoothingGroup] += normal;
                    }
                }
            }

            // 计算最终法线
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 finalNormal = Vector3.zero;
                foreach (var smoothingGroup in vertexSmoothingGroups[i])
                {
                    finalNormal += smoothingGroup.Value.normalized;
                }
                normals[i] = (finalNormal.magnitude > 0) ? finalNormal.normalized : Vector3.up;
            }

            mesh.normals = normals;
        }
    }
}
#endif 