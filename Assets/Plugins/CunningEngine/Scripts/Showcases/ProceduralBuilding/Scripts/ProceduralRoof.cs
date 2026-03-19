using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;


namespace DevStuffs
{
public class ProceduralRoof : MonoBehaviour
{
    
    /// <summary>
    /// Triangles are created by checking how many one and the same point can create triangles in the play, if you do not create a triangle, then jump to the next point
    ///if a point cannot create a triangle with another point, then it will mark the last face as incomplete
    ///incomplete faces should be checked for joint points, whether they can collapse
    //There are always 2 fewer triangles than the edges around the perimeter, that is, until the triangles are ready to the end, you need to make a file
    /// </summary>
    /// <param name="edges"></param>
    /// <param name="y"></param>
    /// <param name="mat"></param>
    /// <returns></returns>
    public static GameObject GenerateRoofMesh(List<Edge> edges, float y, Material mat)
    {
        GameObject roof = new GameObject("RoofMesh");
        MeshRenderer mr = roof.AddComponent<MeshRenderer>();
        MeshFilter mf = roof.AddComponent<MeshFilter>();

        List<Triangle> triangles = new List<Triangle>();

        int trianglesShouldBe = edges.Count - 2;

        //New edges for non triangle edges
        List<Edge> innerEdges = new List<Edge>();
        List<Vertex> doneVertices = new List<Vertex>();
        List<Edge> mEdges = new List<Edge>();
        mEdges.AddRange(edges);

        int breaker = 300;
        
        int I = 0;
        int I2 = 0;

        while(mEdges.Count != 0 && breaker > 0)
        {
            Edge e1 = mEdges[I];
            I2 = I+1;
            if (mEdges.Count <= I2) I2 = 0;
            if (I2 == I)
            {
                Debug.LogError($"I2 == I");
                I2++;
            }

            Edge e2 = mEdges[I2];

            bool isDotEdgeMatch = Vector3.Dot((e2.p2 - e1.p1).normalized, e2.normalDirrection) > 0;

            if (IsPointLineCastToOther(e1.p1, e2.p2, edges) && isDotEdgeMatch)
            {
                triangles.Add(new Triangle(e1, e2));

                e1 = new Edge(e1.p1, e2.p2, Vector3.zero, 0);
                e1.p2 = e2.p2;
                e1.normalDirrection = Quaternion.AngleAxis(-90f, Vector3.up) * e1.Dir.normalized;
                
                mEdges.RemoveAt(I);
                if (I >= mEdges.Count) I = 0;
                mEdges.RemoveAt(I);
                mEdges.Insert(I, e1);

                if (mEdges.Count == 3)
                {
                    triangles.Add(new Triangle(mEdges[0], mEdges[1]));
                    break;
                }
            }
            else 
            {
                I++;
                if (mEdges.Count <= I) I = 0;
            }
            breaker--;
            if (breaker <=0) break;
        }

        // 生成顶点 - 使用局部坐标
        List<Vector3> vertices = new List<Vector3>();
        foreach (var edge in edges)
        {
            // 将世界坐标转换为局部坐标
            vertices.Add(new Vector3(edge.p1.x, 0, edge.p1.z));
        }

        List<int> meshTriangles = new List<int>();
        List<Vector3> normals = new List<Vector3>();

        foreach(Vector3 ver in vertices)
        {
            normals.Add(Vector3.up);
        }

        int FindIndex(Vector3 vertex)
        {
            return vertices.FindIndex(v => v == vertex);
        }

        int vc = vertices.Count;
        // 创建几何体
        foreach (var tri in triangles)
        {
            Vector3 p1 = new Vector3(tri.edges[0].p1.x, 0, tri.edges[0].p1.z);
            Vector3 p2 = new Vector3(tri.edges[1].p1.x, 0, tri.edges[1].p1.z);
            Vector3 p3 = new Vector3(tri.edges[1].p2.x, 0, tri.edges[1].p2.z);
            
            int i1 = FindIndex(p1);
            int i2 = FindIndex(p2);
            int i3 = FindIndex(p3);
            
            if (i3 == -1)
            {
                Debug.Log("Cant find vertex: " + p3);
                continue;
            }

            meshTriangles.Add(i1);
            meshTriangles.Add(i2);
            meshTriangles.Add(i3);
        }

        Mesh roofMesh = new Mesh();
        roofMesh.vertices = vertices.ToArray();
        roofMesh.triangles = meshTriangles.ToArray();
        roofMesh.name = $"RoofMesh_tr[{meshTriangles.Count}]";
        roofMesh.normals = normals.ToArray();

        mr.material = mat;
        mf.mesh = roofMesh;
        
        return roof;
    }

    static Vector2 p3, p4;
    
    static bool IsPointLineCastToOther(Vector3 p1, Vector3 p2, List<Edge> edges)
    {
        Vector2 v2p1 = new Vector2(p1.x , p1.z);
        Vector2 v2p2 = new Vector2(p2.x , p2.z);

        foreach (var edge in edges)
        {
            if (edge.p1 == p1) continue;
            if (edge.p2 == p1) continue;
            //для аргумента проверки пересечений нужна линия из еще двух вертексов
            p3 = new Vector2(edge.p1.x , edge.p1.z);
            p4 = new Vector2(edge.p2.x , edge.p2.z);


            if (LineSegmentsIntersection(v2p1, v2p2,p3,p4, out Vector2 inter))
            {
                
                if (Vector3.Dot((edge.p2 - p1).normalized, edge.normalDirrection) < 0f)
                {
                    Debug.DrawLine(p1, new Vector3(inter.x, edges[0].p1.y, inter.y), Color.red, 2f);
                    //Debug.LogError($"Iteration [??]->p1 {p1:f4} cant cast to: p2:[{p2:f4}] , normal dot wrong");
                    //return false;
                }
                if ((inter - v2p2).magnitude < 0.01f) continue;

                //Debug.LogError($"Iteration [??]->p1 {p1:f4} cant cast to: p2:[{p2:f4}] , inter:[{inter:f4}]");
                //Debug.DrawRay(new Vector3(inter.x, edges[0].p1.y, inter.y) , Vector3.up * 100f,  Color.red , 2f);
                //Debug.DrawLine(edge.p1, edge.p2, Color.red, 2f);
                return false;
            } 
        }
        return true;
    }
    /// <summary>
    /// Visualize roof edges
    /// </summary>
    /// <param name="edges"></param>
    /// <param name="roofY"></param>
    public static void DrawRoofEdgesDebug(List<Edge> edges, float roofY)
    {
        // edges参数中的点已经是世界坐标，所以直接使用
        // 屋顶是位于建筑高度处的
        foreach (var edge in edges)
        {
            // 将屋顶线条颜色更改为更明显的色调
            Debug.DrawLine(edge.p1 + Vector3.up * roofY, edge.p2 + Vector3.up * roofY, new Color(1f, 0.3f, 0f, 0.8f));
        }
    }
    
    ///Special thanx for: https://github.com/setchi/Unity-LineSegmentsIntersection
    public static bool LineSegmentsIntersection(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, out Vector2 intersection)
    {
        intersection = Vector2.zero;

        var d = (p2.x - p1.x) * (p4.y - p3.y) - (p2.y - p1.y) * (p4.x - p3.x);

        if (d == 0.0f)
        {
            return false;
        }

        var u = ((p3.x - p1.x) * (p4.y - p3.y) - (p3.y - p1.y) * (p4.x - p3.x)) / d;
        var v = ((p3.x - p1.x) * (p2.y - p1.y) - (p3.y - p1.y) * (p2.x - p1.x)) / d;

        if (u < 0.0f || u > 1.0f || v < 0.0f || v > 1.0f)
        {
            return false;
        }

        intersection.x = p1.x + u * (p2.x - p1.x);
        intersection.y = p1.y + u * (p2.y - p1.y);

        return true;
    }

    //Calculate the intersection point of two lines. Returns true if lines intersect, otherwise false.
    //Note that in 3d, two lines do not intersect most of the time. So if the two lines are not in the 
    //same plane, use ClosestPointsOnTwoLines() instead.
    public static bool LineLineIntersection(out Vector3 intersection, Vector3 linePoint1, Vector3 lineVec1, Vector3 linePoint2, Vector3 lineVec2)
    {

        Vector3 lineVec3 = linePoint2 - linePoint1;
        Vector3 crossVec1and2 = Vector3.Cross(lineVec1, lineVec2);
        Vector3 crossVec3and2 = Vector3.Cross(lineVec3, lineVec2);

        float planarFactor = Vector3.Dot(lineVec3, crossVec1and2);

        //is coplanar, and not parrallel
        if (Mathf.Abs(planarFactor) < 0.0001f && crossVec1and2.sqrMagnitude > 0.0001f)
        {
            float s = Vector3.Dot(crossVec3and2, crossVec1and2) / crossVec1and2.sqrMagnitude;
            intersection = linePoint1 + (lineVec1 * s);
            return true;
        }
        else
        {
            intersection = Vector3.zero;
            return false;
        }
    }
}
struct Vertex
{
    Vector3 pos;
}
class Triangle
{
    public List<Vector3> vertices = new List<Vector3>();
    public List<Edge> edges = new List<Edge>();

    public Triangle(Edge e1, Edge e2)
    {
        vertices.Add(e1.p1);
        vertices.Add(e2.p1);
        vertices.Add(e2.p2);

        edges.Add(e1);
        edges.Add(e2);
    }
}
}