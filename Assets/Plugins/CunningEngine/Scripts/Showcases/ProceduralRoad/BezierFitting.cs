using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

public static class BezierFitting
{
    public static List<BezierKnot> FitBezier(List<Vector3> points)
    {
        var knots = new List<BezierKnot>();

        if (points.Count < 2)
        {
            return knots;
        }

        // 开始点
        var start = points[0];
        var end = points[points.Count - 1];

        // 使用Ramer-Douglas-Peucker简化的点
        var simplifiedPoints = PointSimplification.RamerDouglasPeucker(points, 0.1f);

        // 根据简化点生成贝塞尔控制点
        for (int i = 0; i < simplifiedPoints.Count - 1; i++)
        {
            var p0 = simplifiedPoints[i];
            var p1 = simplifiedPoints[i + 1];
            var tangent = (p1 - p0).normalized * (Vector3.Distance(p0, p1) / 3f);
            knots.Add(new BezierKnot(p0, tangent, -tangent, Quaternion.identity));
        }

        // 最后一个控制点
        knots.Add(new BezierKnot(end, (end - start).normalized * (Vector3.Distance(start, end) / 3f), -(end - start).normalized * (Vector3.Distance(start, end) / 3f), Quaternion.identity));

        return knots;
    }
}