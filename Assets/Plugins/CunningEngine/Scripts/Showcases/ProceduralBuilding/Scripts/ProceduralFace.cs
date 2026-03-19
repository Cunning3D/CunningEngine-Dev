using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// Class for custom procedural quads
/// </summary>
public class ProceduralFace 
{

    /// <summary>
    /// Generate procedural low poly wall for building
    /// </summary>
    /// <param name="vertices"></param>
    /// <param name="parent"></param>
    /// <param name="mat"></param>
    /// <param name="UVScale"></param>
    /// <returns></returns>
    public static GameObject GenerateFace(Vector3[] vertices , Transform parent, Material mat, float UVScale )
    {
        GameObject newFace = new GameObject("Face: " + vertices[0]);
        newFace.transform.position = vertices[0];
        //newFace.transform.position = vertices[0];
        
        for(int i=0;i<vertices.Length;i++)
        {
            vertices[i] -= newFace.transform.position;
        }

        MeshRenderer mr = newFace.AddComponent<MeshRenderer>();
        mr.material = mat;
        MeshFilter mf = newFace.AddComponent<MeshFilter>();
        var mesh = new Mesh();
        mesh.name = ("Wall_" + (vertices[1] - vertices[0]).magnitude.ToString("f1") );
        mf.mesh = mesh;

        Debug.DrawLine(vertices[0], vertices[1], Color.red, 4f);

        mesh.vertices = vertices;

        int[] tri = new int[6];

        tri[0] = 0;
        tri[1] = 2;
        tri[2] = 1;

        tri[3] = 2;
        tri[4] = 3;
        tri[5] = 1;

        mesh.triangles = tri;

        Vector3[] normals = new Vector3[4];

        Vector3 normal = Vector3.Cross((vertices[1] - vertices[0]).normalized, Vector3.up);

        normals[0] = -normal;
        normals[1] = -normal;
        normals[2] = -normal;
        normals[3] = -normal;

        mesh.normals = normals;

        Vector2[] uv = new Vector2[4];

        //Тангенты нахуй не нужны, наверно
        Vector2 xy = new Vector2((vertices[1] - vertices[0]).magnitude / UVScale, (vertices[2] - vertices[0]).magnitude / UVScale);

        uv[0] = new Vector2(0, 0);
        uv[1] = new Vector2(xy.x, 0);
        uv[2] = new Vector2(0, xy.y);
        uv[3] = new Vector2(xy.x, xy.y);

        mesh.uv = uv;


        newFace.transform.parent = parent;
        return newFace;
    }

    public static GameObject GenerateFace(Vector3[] vector3, Transform transform, object proceduralCornersMat, float proceduralWallsUVScale)
    {
        throw new NotImplementedException();
    }
}
