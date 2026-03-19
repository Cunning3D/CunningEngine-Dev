using System.Collections.Generic;
using UnityEngine;

public static class PointSimplification
{
    public static List<Vector3> RamerDouglasPeucker(List<Vector3> points, float epsilon)
    {
        if (points.Count < 3)
            return points;

        int firstIndex = 0;
        int lastIndex = points.Count - 1;
        List<int> pointIndexesToKeep = new List<int>();

        pointIndexesToKeep.Add(firstIndex);
        pointIndexesToKeep.Add(lastIndex);

        while (points[firstIndex].Equals(points[lastIndex]))
        {
            lastIndex--;
        }

        RamerDouglasPeuckerStep(points, firstIndex, lastIndex, epsilon, ref pointIndexesToKeep);

        List<Vector3> simplifiedPoints = new List<Vector3>();
        pointIndexesToKeep.Sort();
        foreach (int index in pointIndexesToKeep)
        {
            simplifiedPoints.Add(points[index]);
        }

        return simplifiedPoints;
    }

    private static void RamerDouglasPeuckerStep(List<Vector3> points, int firstIndex, int lastIndex, float epsilon, ref List<int> pointIndexesToKeep)
    {
        float maxDistance = 0;
        int indexFarthest = 0;
        Vector3 firstPoint = points[firstIndex];
        Vector3 lastPoint = points[lastIndex];

        for (int i = firstIndex + 1; i < lastIndex; i++)
        {
            float distance = PerpendicularDistance(points[i], firstPoint, lastPoint);
            if (distance > maxDistance)
            {
                maxDistance = distance;
                indexFarthest = i;
            }
        }

        if (maxDistance > epsilon && indexFarthest != 0)
        {
            pointIndexesToKeep.Add(indexFarthest);

            RamerDouglasPeuckerStep(points, firstIndex, indexFarthest, epsilon, ref pointIndexesToKeep);
            RamerDouglasPeuckerStep(points, indexFarthest, lastIndex, epsilon, ref pointIndexesToKeep);
        }
    }

    private static float PerpendicularDistance(Vector3 point, Vector3 lineStart, Vector3 lineEnd)
    {
        float area = Mathf.Abs(0.5f * (lineStart.x * lineEnd.z + lineEnd.x * point.z + point.x * lineStart.z - lineEnd.x * lineStart.z - point.x * lineEnd.z - lineStart.x * point.z));
        float bottom = Mathf.Sqrt(Mathf.Pow(lineStart.x - lineEnd.x, 2) + Mathf.Pow(lineStart.z - lineEnd.z, 2));
        float height = area / bottom * 2;

        return height;
    }
}