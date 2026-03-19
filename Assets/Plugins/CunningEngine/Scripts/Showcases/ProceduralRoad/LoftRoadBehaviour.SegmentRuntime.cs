#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using UnityEditor.Splines.Extension;

namespace Unity.Splines.Examples
{
    public partial class LoftRoadBehaviour
    {
        private const float SegmentRuntimeCurveUEpsilon = 0.0001f;

        private void BuildSplineGeometryFromLogicalSegments(Spline spline, int splineIndex, LoftRoadExtensionData roadData)
        {
            EnsureRoadId();
            m_CurrentProcessingSplineIndex = splineIndex;

            IReadOnlyList<LogicalSegmentDef> logicalSegments = GetLogicalSegments(splineIndex);
            if (roadData != null && roadData.roadTypeEnum == RoadType.GreenBelt_Define)
            {
                roadData.ClearPoints();
            }

            if (logicalSegments == null || logicalSegments.Count == 0)
            {
                var fallbackSegment = new LogicalSegmentDef
                {
                    splineIndex = splineIndex,
                    localSegmentIndex = 0,
                    segmentId = ComputeFallbackSegmentId(splineIndex),
                    startCurveU = 0f,
                    endCurveU = 1f,
                    startBoundaryKind = SegmentBoundaryKind.Endpoint,
                    endBoundaryKind = SegmentBoundaryKind.Endpoint,
                };

                SetCurrentLogicalSegmentContext(spline, fallbackSegment);
                try
                {
                    if (roadData != null && roadData.roadTypeEnum == RoadType.GreenBelt_Define)
                    {
                        BuildGreenbeltGeometryForSegment(spline, roadData, fallbackSegment);
                    }
                    else
                    {
                        BuildRoadGeometryForSegment(spline, roadData, fallbackSegment);
                    }
                }
                finally
                {
                    ClearCurrentLogicalSegmentContext();
                }

                return;
            }

            for (int index = 0; index < logicalSegments.Count; index++)
            {
                LogicalSegmentDef segmentDef = logicalSegments[index];
                if (!segmentDef.IsValid)
                {
                    continue;
                }

                SetCurrentLogicalSegmentContext(spline, segmentDef);
                try
                {
                    if (roadData != null && roadData.roadTypeEnum == RoadType.GreenBelt_Define)
                    {
                        BuildGreenbeltGeometryForSegment(spline, roadData, segmentDef);
                    }
                    else
                    {
                        BuildRoadGeometryForSegment(spline, roadData, segmentDef);
                    }
                }
                finally
                {
                    ClearCurrentLogicalSegmentContext();
                }
            }
        }

        private void BuildRoadGeometryForSegment(Spline spline, LoftRoadExtensionData roadData, LogicalSegmentDef segmentDef)
        {
            if (spline == null || roadData == null)
            {
                return;
            }

            if (!TryBuildSegmentPathLocal(
                    spline,
                    segmentDef.startCurveU,
                    segmentDef.endCurveU,
                    SegmentUnitLength,
                    out List<Vector3> pathPoints,
                    out List<Vector3> pathNormals,
                    out List<Vector3> pathTangents,
                    out _,
                    out _))
            {
                return;
            }

            int segmentCount = pathPoints.Count - 1;
            if (segmentCount <= 0)
            {
                return;
            }

            float totalLaneCount = roadData.leftLaneCount + roadData.rightLaneCount;
            float laneWidth = roadData.laneWidth.DefaultValue;
            float totalWidth = totalLaneCount * laneWidth;
            float halfWidth = totalWidth / 2f;
            float leftSidewalkWidth = roadData.leftSidewalkWidth.DefaultValue;
            float rightSidewalkWidth = roadData.rightSidewalkWidth.DefaultValue;

            int vertexOffset = m_Positions.Count;
            float currentOffset = -halfWidth;
            for (int lane = 0; lane < totalLaneCount; lane++)
            {
                if (lane == 0)
                {
                    float extendedStartOffset = currentOffset * 1.2f;
                    GenerateLaneMesh(pathPoints, pathNormals, pathTangents, extendedStartOffset, currentOffset + laneWidth, vertexOffset, segmentCount, roadData.meshOffset);
                }
                else
                {
                    GenerateLaneMesh(pathPoints, pathNormals, pathTangents, currentOffset, currentOffset + laneWidth, vertexOffset, segmentCount, roadData.meshOffset);
                }

                currentOffset += laneWidth;
                vertexOffset = m_Positions.Count;
            }

            if (leftSidewalkWidth > 0f)
            {
                if (roadData.enableCurb)
                {
                    float curbInnerOffset = -halfWidth;
                    float curbOuterOffset = -halfWidth - roadData.curbWidth;
                    GenerateCurbMesh(
                        pathPoints,
                        pathNormals,
                        pathTangents,
                        curbInnerOffset,
                        curbOuterOffset,
                        m_CurbLeftPositions.Count,
                        segmentCount,
                        roadData.meshOffset,
                        roadData.curbHeight,
                        m_CurbLeftPositions,
                        m_CurbLeftNormals,
                        m_CurbLeftTextures,
                        m_CurbLeftIndices,
                        true);

                    if (roadData.enableRoadEdge)
                    {
                        float roadEdgeInnerOffset = -halfWidth - roadData.curbWidth;
                        float roadEdgeOuterOffset = roadEdgeInnerOffset - roadData.roadEdgeWidth;
                        GenerateRoadEdgeMeshWithChamfer(
                            pathPoints,
                            pathNormals,
                            pathTangents,
                            roadEdgeInnerOffset,
                            roadEdgeOuterOffset,
                            m_RoadEdgeLeftPositions.Count,
                            segmentCount,
                            roadData.meshOffset,
                            roadData.roadEdgeHeight,
                            m_RoadEdgeLeftPositions,
                            m_RoadEdgeLeftNormals,
                            m_RoadEdgeLeftTextures,
                            m_RoadEdgeLeftIndices,
                            true);

                        GenerateSidewalkMesh(
                            pathPoints,
                            pathNormals,
                            pathTangents,
                            roadEdgeOuterOffset - leftSidewalkWidth,
                            roadEdgeOuterOffset,
                            m_SidewalkLeftPositions.Count,
                            segmentCount,
                            roadData.meshOffset,
                            m_SidewalkLeftPositions,
                            m_SidewalkLeftNormals,
                            m_SidewalkLeftTextures,
                            m_SidewalkLeftIndices,
                            false);

                        float outerRoadEdgeInnerOffset = roadEdgeOuterOffset - leftSidewalkWidth;
                        float outerRoadEdgeOuterOffset = outerRoadEdgeInnerOffset - roadData.roadEdgeWidth;
                        GenerateOuterRoadEdgeMeshWithChamfer(
                            pathPoints,
                            pathNormals,
                            pathTangents,
                            outerRoadEdgeInnerOffset,
                            outerRoadEdgeOuterOffset,
                            m_RoadEdgeOuterLeftPositions.Count,
                            segmentCount,
                            roadData.meshOffset,
                            roadData.roadEdgeHeight,
                            m_RoadEdgeOuterLeftPositions,
                            m_RoadEdgeOuterLeftNormals,
                            m_RoadEdgeOuterLeftTextures,
                            m_RoadEdgeOuterLeftIndices,
                            true);
                    }
                    else
                    {
                        GenerateSidewalkMesh(
                            pathPoints,
                            pathNormals,
                            pathTangents,
                            curbOuterOffset - leftSidewalkWidth,
                            curbOuterOffset,
                            m_SidewalkLeftPositions.Count,
                            segmentCount,
                            roadData.meshOffset,
                            m_SidewalkLeftPositions,
                            m_SidewalkLeftNormals,
                            m_SidewalkLeftTextures,
                            m_SidewalkLeftIndices,
                            false);
                    }
                }
                else
                {
                    GenerateSidewalkMesh(
                        pathPoints,
                        pathNormals,
                        pathTangents,
                        -halfWidth - leftSidewalkWidth,
                        -halfWidth,
                        m_SidewalkLeftPositions.Count,
                        segmentCount,
                        roadData.meshOffset,
                        m_SidewalkLeftPositions,
                        m_SidewalkLeftNormals,
                        m_SidewalkLeftTextures,
                        m_SidewalkLeftIndices,
                        false);
                }
            }

            if (rightSidewalkWidth > 0f)
            {
                if (roadData.enableCurb)
                {
                    float curbInnerOffset = halfWidth;
                    float curbOuterOffset = halfWidth + roadData.curbWidth;
                    GenerateCurbMesh(
                        pathPoints,
                        pathNormals,
                        pathTangents,
                        curbInnerOffset,
                        curbOuterOffset,
                        m_CurbRightPositions.Count,
                        segmentCount,
                        roadData.meshOffset,
                        roadData.curbHeight,
                        m_CurbRightPositions,
                        m_CurbRightNormals,
                        m_CurbRightTextures,
                        m_CurbRightIndices,
                        false);

                    if (roadData.enableRoadEdge)
                    {
                        float roadEdgeInnerOffset = halfWidth + roadData.curbWidth;
                        float roadEdgeOuterOffset = roadEdgeInnerOffset + roadData.roadEdgeWidth;
                        GenerateRoadEdgeMeshWithChamfer(
                            pathPoints,
                            pathNormals,
                            pathTangents,
                            roadEdgeInnerOffset,
                            roadEdgeOuterOffset,
                            m_RoadEdgeRightPositions.Count,
                            segmentCount,
                            roadData.meshOffset,
                            roadData.roadEdgeHeight,
                            m_RoadEdgeRightPositions,
                            m_RoadEdgeRightNormals,
                            m_RoadEdgeRightTextures,
                            m_RoadEdgeRightIndices,
                            false);

                        GenerateSidewalkMesh(
                            pathPoints,
                            pathNormals,
                            pathTangents,
                            roadEdgeOuterOffset,
                            roadEdgeOuterOffset + rightSidewalkWidth,
                            m_SidewalkRightPositions.Count,
                            segmentCount,
                            roadData.meshOffset,
                            m_SidewalkRightPositions,
                            m_SidewalkRightNormals,
                            m_SidewalkRightTextures,
                            m_SidewalkRightIndices,
                            true);

                        float outerRoadEdgeInnerOffset = roadEdgeOuterOffset + rightSidewalkWidth;
                        float outerRoadEdgeOuterOffset = outerRoadEdgeInnerOffset + roadData.roadEdgeWidth;
                        GenerateOuterRoadEdgeMeshWithChamfer(
                            pathPoints,
                            pathNormals,
                            pathTangents,
                            outerRoadEdgeInnerOffset,
                            outerRoadEdgeOuterOffset,
                            m_RoadEdgeOuterRightPositions.Count,
                            segmentCount,
                            roadData.meshOffset,
                            roadData.roadEdgeHeight,
                            m_RoadEdgeOuterRightPositions,
                            m_RoadEdgeOuterRightNormals,
                            m_RoadEdgeOuterRightTextures,
                            m_RoadEdgeOuterRightIndices,
                            false);
                    }
                    else
                    {
                        GenerateSidewalkMesh(
                            pathPoints,
                            pathNormals,
                            pathTangents,
                            curbOuterOffset,
                            curbOuterOffset + rightSidewalkWidth,
                            m_SidewalkRightPositions.Count,
                            segmentCount,
                            roadData.meshOffset,
                            m_SidewalkRightPositions,
                            m_SidewalkRightNormals,
                            m_SidewalkRightTextures,
                            m_SidewalkRightIndices,
                            true);
                    }
                }
                else
                {
                    GenerateSidewalkMesh(
                        pathPoints,
                        pathNormals,
                        pathTangents,
                        halfWidth,
                        halfWidth + rightSidewalkWidth,
                        m_SidewalkRightPositions.Count,
                        segmentCount,
                        roadData.meshOffset,
                        m_SidewalkRightPositions,
                        m_SidewalkRightNormals,
                        m_SidewalkRightTextures,
                        m_SidewalkRightIndices,
                        true);
                }
            }
        }

        private void BuildGreenbeltGeometryForSegment(Spline spline, LoftRoadExtensionData roadData, LogicalSegmentDef segmentDef)
        {
            if (spline == null || roadData == null)
            {
                return;
            }

            if (!TryBuildSegmentPathLocal(
                    spline,
                    segmentDef.startCurveU,
                    segmentDef.endCurveU,
                    SegmentUnitLength,
                    out List<Vector3> pathPoints,
                    out List<Vector3> pathNormals,
                    out _,
                    out List<float> pathCurveUs,
                    out _))
            {
                return;
            }

            float totalWidth = roadData.WidthValue;
            float inWidth = roadData.inWidth;
            float outWidth = roadData.outWidth;
            float inOffset = roadData.inOffset;
            float outOffset = roadData.outOffset;
            AnimationCurve inCurve = roadData.inCurve;
            AnimationCurve outCurve = roadData.outCurve;

            int prevVertexCount = m_Positions.Count;
            for (int pointIndex = 0; pointIndex < pathPoints.Count; pointIndex++)
            {
                Vector3 pos = pathPoints[pointIndex];
                Vector3 up = pathNormals[pointIndex];
                Vector3 tangent = pointIndex < pathPoints.Count - 1
                    ? (pathPoints[Mathf.Min(pathPoints.Count - 1, pointIndex + 1)] - pathPoints[Mathf.Max(0, pointIndex - (pointIndex > 0 ? 1 : 0))]).normalized
                    : (pathPoints[pointIndex] - pathPoints[Mathf.Max(0, pointIndex - 1)]).normalized;
                if (tangent.sqrMagnitude <= 1e-6f)
                {
                    tangent = Vector3.forward;
                }

                float progress = pathCurveUs[pointIndex];
                float leftWidth = totalWidth * 0.5f;
                float rightWidth = totalWidth * 0.5f;
                float leftOffset = 0f;
                float rightOffset = 0f;

                if (progress < inOffset && inOffset > SegmentRuntimeCurveUEpsilon)
                {
                    float lerpT = Mathf.Clamp01(progress / inOffset);
                    leftWidth = Mathf.Lerp(inWidth * 0.5f, totalWidth * 0.5f, inCurve.Evaluate(lerpT));
                    leftOffset = totalWidth * 0.5f - leftWidth;
                }
                else if (progress > (1f - outOffset) && outOffset > SegmentRuntimeCurveUEpsilon)
                {
                    float lerpT = Mathf.Clamp01((progress - (1f - outOffset)) / outOffset);
                    rightWidth = Mathf.Lerp(outWidth * 0.5f, totalWidth * 0.5f, 1f - outCurve.Evaluate(lerpT));
                    rightOffset = totalWidth * 0.5f - rightWidth;
                }

                leftWidth = Mathf.Max(leftWidth, 0.01f);
                rightWidth = Mathf.Max(rightWidth, 0.01f);

                roadData.AddPoint(pos);

                Vector3 binormal = Vector3.Cross(up, tangent).normalized;
                Vector3 offsetPos = pos + Vector3.up * roadData.meshOffset;
                m_Positions.Add(offsetPos - binormal * leftWidth + binormal * leftOffset);
                m_Positions.Add(offsetPos + binormal * rightWidth - binormal * rightOffset);

                m_Normals.Add(up);
                m_Normals.Add(up);

                float globalV = pathCurveUs[pointIndex] * m_TextureScale;
                m_Textures.Add(new Vector2(0f, globalV));
                m_Textures.Add(new Vector2(1f, globalV));
            }

            RegisterCunningRoadStrip(prevVertexCount, pathPoints.Count * 2, m_CurrentProcessingSplineIndex, GetPrimaryRoadMaterial());
        }

        private static float EstimatePolylineDistance(List<Vector3> points, int inclusiveEndIndex)
        {
            if (points == null || points.Count == 0 || inclusiveEndIndex <= 0)
            {
                return 0f;
            }

            int clampedEnd = Mathf.Min(inclusiveEndIndex, points.Count - 1);
            float distance = 0f;
            for (int index = 1; index <= clampedEnd; index++)
            {
                distance += Vector3.Distance(points[index - 1], points[index]);
            }

            return distance;
        }

        private void SetCurrentLogicalSegmentContext(Spline spline, LogicalSegmentDef segmentDef)
        {
            m_CurrentProcessingLocalSegmentIndex = segmentDef.localSegmentIndex;
            m_CurrentProcessingSegmentId = segmentDef.segmentId;
            m_CurrentProcessingSegmentStartCurveU = segmentDef.startCurveU;
            m_CurrentProcessingSegmentEndCurveU = segmentDef.endCurveU;
            m_CurrentProcessingSegmentStartArcLength = EstimateSplineArcLengthAt(spline, segmentDef.startCurveU);
            m_CurrentProcessingSegmentEndArcLength = EstimateSplineArcLengthAt(spline, segmentDef.endCurveU);
        }

        private void ClearCurrentLogicalSegmentContext()
        {
            m_CurrentProcessingLocalSegmentIndex = -1;
            m_CurrentProcessingSegmentId = 0;
            m_CurrentProcessingSegmentStartCurveU = 0f;
            m_CurrentProcessingSegmentEndCurveU = 1f;
            m_CurrentProcessingSegmentStartArcLength = 0f;
            m_CurrentProcessingSegmentEndArcLength = 0f;
        }

        private bool TryBuildSegmentPathLocal(
            Spline spline,
            float startCurveU,
            float endCurveU,
            float desiredSpacing,
            out List<Vector3> pathPoints,
            out List<Vector3> pathNormals,
            out List<Vector3> pathTangents,
            out List<float> pathCurveUs,
            out float totalLength)
        {
            pathPoints = new List<Vector3>();
            pathNormals = new List<Vector3>();
            pathTangents = new List<Vector3>();
            pathCurveUs = new List<float>();
            totalLength = 0f;

            if (spline == null || spline.Count < 2)
            {
                return false;
            }

            startCurveU = Mathf.Clamp01(startCurveU);
            endCurveU = Mathf.Clamp01(endCurveU);
            if (endCurveU - startCurveU <= SegmentRuntimeCurveUEpsilon)
            {
                return false;
            }

            var denseCurveUs = new List<float>(64);
            var denseLengths = new List<float>(64);
            BuildSplineArcLengthTable(spline, startCurveU, endCurveU, Mathf.Max(0.5f, desiredSpacing * 0.25f), denseCurveUs, denseLengths);
            if (denseCurveUs.Count == 0 || denseLengths.Count == 0)
            {
                return false;
            }

            totalLength = denseLengths[denseLengths.Count - 1];
            if (totalLength <= 1e-5f)
            {
                return false;
            }

            int stepCount = Mathf.Max(1, Mathf.CeilToInt(totalLength / Mathf.Max(0.5f, desiredSpacing)));
            for (int stepIndex = 0; stepIndex <= stepCount; stepIndex++)
            {
                float targetDistance = totalLength * (stepIndex / (float)stepCount);
                float curveU = EvaluateCurveUAtArcDistance(denseCurveUs, denseLengths, targetDistance);
                Vector3 position = spline.EvaluatePosition(curveU);
                Vector3 tangent = EvaluateLocalSplineTangent(spline, curveU);

                pathPoints.Add(position);
                pathNormals.Add(Vector3.up);
                pathTangents.Add(tangent);
                pathCurveUs.Add(curveU);
            }

            if (pathCurveUs.Count > 0)
            {
                pathCurveUs[0] = startCurveU;
                pathPoints[0] = spline.EvaluatePosition(startCurveU);
                pathTangents[0] = EvaluateLocalSplineTangent(spline, startCurveU);

                int lastIndex = pathCurveUs.Count - 1;
                pathCurveUs[lastIndex] = endCurveU;
                pathPoints[lastIndex] = spline.EvaluatePosition(endCurveU);
                pathTangents[lastIndex] = EvaluateLocalSplineTangent(spline, endCurveU);
            }

            return pathPoints.Count >= 2;
        }

        private static void BuildSplineArcLengthTable(
            Spline spline,
            float startCurveU,
            float endCurveU,
            float desiredStepLength,
            List<float> curveUs,
            List<float> cumulativeLengths)
        {
            curveUs.Clear();
            cumulativeLengths.Clear();

            if (spline == null || spline.Count < 2)
            {
                return;
            }

            Vector3 startPos = spline.EvaluatePosition(startCurveU);
            Vector3 endPos = spline.EvaluatePosition(endCurveU);
            float chordLength = Vector3.Distance(startPos, endPos);
            int resolution = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(chordLength, desiredStepLength) / Mathf.Max(0.1f, desiredStepLength)), 8, 128);

            curveUs.Add(startCurveU);
            cumulativeLengths.Add(0f);

            Vector3 previous = startPos;
            float accumulated = 0f;
            for (int index = 1; index <= resolution; index++)
            {
                float curveU = Mathf.Lerp(startCurveU, endCurveU, index / (float)resolution);
                Vector3 current = spline.EvaluatePosition(curveU);
                accumulated += Vector3.Distance(previous, current);
                curveUs.Add(curveU);
                cumulativeLengths.Add(accumulated);
                previous = current;
            }
        }

        private static float EvaluateCurveUAtArcDistance(List<float> curveUs, List<float> cumulativeLengths, float targetDistance)
        {
            if (curveUs == null || cumulativeLengths == null || curveUs.Count == 0 || curveUs.Count != cumulativeLengths.Count)
            {
                return 0f;
            }

            if (targetDistance <= 0f)
            {
                return curveUs[0];
            }

            float totalLength = cumulativeLengths[cumulativeLengths.Count - 1];
            if (targetDistance >= totalLength)
            {
                return curveUs[curveUs.Count - 1];
            }

            int upperIndex = 1;
            while (upperIndex < cumulativeLengths.Count && cumulativeLengths[upperIndex] < targetDistance)
            {
                upperIndex++;
            }

            int lowerIndex = Mathf.Max(0, upperIndex - 1);
            float lowerDistance = cumulativeLengths[lowerIndex];
            float upperDistance = cumulativeLengths[upperIndex];
            float lerp = upperDistance - lowerDistance > 1e-5f
                ? Mathf.InverseLerp(lowerDistance, upperDistance, targetDistance)
                : 0f;
            return Mathf.Lerp(curveUs[lowerIndex], curveUs[upperIndex], lerp);
        }

        private static Vector3 EvaluateLocalSplineTangent(Spline spline, float curveU)
        {
            if (spline == null || spline.Count < 2)
            {
                return Vector3.forward;
            }

            Vector3 tangent = spline.EvaluateTangent(curveU);
            if (tangent.sqrMagnitude > 1e-6f)
            {
                return tangent.normalized;
            }

            float delta = 0.001f;
            float back = Mathf.Max(0f, curveU - delta);
            float forward = Mathf.Min(1f, curveU + delta);
            Vector3 direction = spline.EvaluatePosition(forward) - spline.EvaluatePosition(back);
            return direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
        }

        private int ComputeFallbackSegmentId(int splineIndex)
        {
            ulong hash = HashInit();
            hash = HashAdd(hash, PrimitiveRoadId);
            hash = HashAdd(hash, splineIndex);
            hash = HashAdd(hash, 0f);
            hash = HashAdd(hash, 1f);
            return unchecked((int)(hash ^ (hash >> 32)));
        }

        private void ResolveSegmentBoundaryJunctions(LogicalSegmentDef segmentDef, out JunctionData startJunction, out JunctionData endJunction)
        {
            startJunction = null;
            endJunction = null;

            if (TryGetRoadMarker(segmentDef.startMarkerId, out RoadMarker startMarker))
            {
                startJunction = startMarker.junctionRef;
            }

            if (TryGetRoadMarker(segmentDef.endMarkerId, out RoadMarker endMarker))
            {
                endJunction = endMarker.junctionRef;
            }
        }

        private void ResolveSegmentBoundaryKnotIndices(int splineIndex, LogicalSegmentDef segmentDef, out int startKnotIndex, out int endKnotIndex)
        {
            startKnotIndex = 0;
            endKnotIndex = 0;

            if (LoftSplines == null || splineIndex < 0 || splineIndex >= LoftSplines.Count)
            {
                return;
            }

            Spline spline = LoftSplines[splineIndex];
            if (spline == null || spline.Count <= 0)
            {
                return;
            }

            startKnotIndex = ResolveBoundaryKnotIndex(spline, segmentDef.startMarkerId, segmentDef.startCurveU, 0);
            endKnotIndex = ResolveBoundaryKnotIndex(spline, segmentDef.endMarkerId, segmentDef.endCurveU, spline.Count - 1);
        }

        private int ResolveBoundaryKnotIndex(Spline spline, long markerId, float fallbackCurveU, int fallbackIndex)
        {
            if (TryGetRoadMarker(markerId, out RoadMarker marker))
            {
                return ResolveMarkerPreferredKnotIndex(marker, spline);
            }

            return ResolveKnotIndexAtCurveU(spline, fallbackCurveU, fallbackIndex);
        }

        private static int ResolveKnotIndexAtCurveU(Spline spline, float curveU, int fallbackIndex)
        {
            if (spline == null || spline.Count <= 0)
            {
                return fallbackIndex;
            }

            for (int knotIndex = 0; knotIndex < spline.Count; knotIndex++)
            {
                float knotCurveU = spline.Count <= 1 ? 0f : knotIndex / (float)(spline.Count - 1);
                if (Mathf.Abs(knotCurveU - curveU) <= SegmentRuntimeCurveUEpsilon)
                {
                    return knotIndex;
                }
            }

            return Mathf.Clamp(fallbackIndex, 0, spline.Count - 1);
        }

        private static int ResolveCurveIndexFromCurveU(Spline spline, float curveU)
        {
            if (spline == null || spline.Count < 2)
            {
                return 0;
            }

            int curveCount = spline.Count - 1;
            return Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(curveU) * curveCount), 0, curveCount - 1);
        }

        private void BuildLaneDataForSegment(int splineIndex, LoftRoadExtensionData roadData, Spline spline, LogicalSegmentDef segmentDef)
        {
            if (roadData == null || spline == null || !segmentDef.IsValid)
            {
                return;
            }

            float laneWidth = roadData.laneWidth.DefaultValue;
            if (laneWidth <= 0f)
            {
                laneWidth = 3.5f;
            }

            int totalLanes = roadData.leftLaneCount + roadData.rightLaneCount;
            if (totalLanes <= 0)
            {
                return;
            }

            if (!TryBuildSegmentPathLocal(
                    spline,
                    segmentDef.startCurveU,
                    segmentDef.endCurveU,
                    SegmentUnitLength,
                    out List<Vector3> pathPoints,
                    out _,
                    out List<Vector3> pathTangents,
                    out _,
                    out _))
            {
                return;
            }

            float totalWidth = totalLanes * laneWidth;
            float halfWidth = totalWidth * 0.5f;
            ResolveSegmentBoundaryJunctions(segmentDef, out JunctionData startJunction, out JunctionData endJunction);
            ResolveSegmentBoundaryKnotIndices(splineIndex, segmentDef, out int startKnotIndex, out int endKnotIndex);

            for (int laneIndex = 0; laneIndex < totalLanes; laneIndex++)
            {
                float startOffset;
                float endOffset;
                bool isRightLane = laneIndex >= roadData.leftLaneCount;

                if (!isRightLane)
                {
                    int leftLaneNumber = roadData.leftLaneCount - laneIndex - 1;
                    float centerOffset = halfWidth - (leftLaneNumber * laneWidth + laneWidth * 0.5f);
                    startOffset = -centerOffset - laneWidth * 0.5f;
                    endOffset = -centerOffset + laneWidth * 0.5f;
                }
                else
                {
                    int rightLaneNumber = laneIndex - roadData.leftLaneCount;
                    float centerOffset = -(halfWidth - (rightLaneNumber * laneWidth + laneWidth * 0.5f));
                    startOffset = -centerOffset - laneWidth * 0.5f;
                    endOffset = -centerOffset + laneWidth * 0.5f;
                }

                List<Vector3> lanePoints = new List<Vector3>(pathPoints.Count);
                List<Vector3> laneLeftBoundaryPoints = new List<Vector3>(pathPoints.Count);
                List<Vector3> laneRightBoundaryPoints = new List<Vector3>(pathPoints.Count);
                float meshOffset = roadData.meshOffset;

                for (int pointIndex = 0; pointIndex < pathPoints.Count; pointIndex++)
                {
                    Vector3 position = pathPoints[pointIndex];
                    Vector3 tangent = pathTangents[pointIndex];
                    Vector3 normal = Vector3.up;
                    Vector3 binormal = Vector3.Cross(normal, tangent.normalized).normalized;

                    Vector3 leftPosition = position + binormal * startOffset;
                    Vector3 rightPosition = position + binormal * endOffset;
                    Vector3 centerPosition = position + binormal * ((startOffset + endOffset) * 0.5f);

                    leftPosition.y += meshOffset;
                    rightPosition.y += meshOffset;
                    centerPosition.y += meshOffset;

                    lanePoints.Add(centerPosition);
                    laneLeftBoundaryPoints.Add(leftPosition);
                    laneRightBoundaryPoints.Add(rightPosition);
                }

                CollectLaneMeshData(
                    splineIndex,
                    segmentDef.localSegmentIndex,
                    segmentDef.segmentId,
                    laneIndex,
                    isRightLane,
                    lanePoints,
                    laneLeftBoundaryPoints,
                    laneRightBoundaryPoints,
                    startKnotIndex,
                    endKnotIndex,
                    segmentDef.startMarkerId,
                    segmentDef.endMarkerId,
                    startJunction,
                    endJunction);
            }
        }

        private void BuildSegmentSamplePointsForSpline(int roadIndex, int splineIndex, LoftRoadExtensionData roadData, Spline spline)
        {
            if (roadData == null || spline == null || spline.Count <= 0)
            {
                return;
            }

            roadData.samplePoints.Clear();

            roadData.CalculateAllWidth();
            float width = roadData.algorithmParameters.allWidth * 0.5f;
            float sampleInterval = Mathf.Max(0.5f, roadData.algorithmParameters.sampleInterval);
            IReadOnlyList<LogicalSegmentDef> logicalSegments = GetLogicalSegments(splineIndex);
            var tempPoints = new List<RoadSamplePoint>();
            var curveUs = new List<float>(64);
            var cumulativeLengths = new List<float>(64);

            for (int segmentIndex = 0; segmentIndex < logicalSegments.Count; segmentIndex++)
            {
                LogicalSegmentDef segmentDef = logicalSegments[segmentIndex];
                if (!segmentDef.IsValid)
                {
                    continue;
                }

                BuildSplineArcLengthTable(spline, segmentDef.startCurveU, segmentDef.endCurveU, sampleInterval * 0.25f, curveUs, cumulativeLengths);
                float totalLength = cumulativeLengths.Count > 0 ? cumulativeLengths[cumulativeLengths.Count - 1] : 0f;
                if (totalLength <= 0f)
                {
                    continue;
                }

                AppendSamplePoint(tempPoints, roadIndex, splineIndex, spline, width, segmentDef, segmentDef.startCurveU, true);
                for (int knotIndex = 0; knotIndex < spline.Count; knotIndex++)
                {
                    float knotCurveU = spline.Count <= 1 ? 0f : knotIndex / (float)(spline.Count - 1);
                    if (knotCurveU <= segmentDef.startCurveU + SegmentRuntimeCurveUEpsilon
                        || knotCurveU >= segmentDef.endCurveU - SegmentRuntimeCurveUEpsilon)
                    {
                        continue;
                    }

                    AppendSamplePoint(tempPoints, roadIndex, splineIndex, spline, width, segmentDef, knotCurveU, true);
                }

                for (float targetDistance = sampleInterval; targetDistance < totalLength - sampleInterval * 0.5f; targetDistance += sampleInterval)
                {
                    float curveU = EvaluateCurveUAtArcDistance(curveUs, cumulativeLengths, targetDistance);
                    AppendSamplePoint(tempPoints, roadIndex, splineIndex, spline, width, segmentDef, curveU, false);
                }

                AppendSamplePoint(tempPoints, roadIndex, splineIndex, spline, width, segmentDef, segmentDef.endCurveU, true);
            }

            tempPoints.Sort((a, b) => a.curveU.CompareTo(b.curveU));
            for (int index = tempPoints.Count - 2; index >= 0; index--)
            {
                RoadSamplePoint current = tempPoints[index];
                RoadSamplePoint next = tempPoints[index + 1];
                if (Mathf.Abs(current.curveU - next.curveU) > SegmentRuntimeCurveUEpsilon)
                {
                    continue;
                }

                bool keepNext = !current.isSegmentBoundary && next.isSegmentBoundary;
                if (!keepNext && current.isSegmentBoundary == next.isSegmentBoundary)
                {
                    keepNext = !current.isOriginalKnot && next.isOriginalKnot;
                }

                tempPoints.RemoveAt(keepNext ? index : index + 1);
            }

            for (int index = 0; index < tempPoints.Count; index++)
            {
                tempPoints[index].globalIndex = index;
            }

            roadData.samplePoints.AddRange(tempPoints);
        }

        private void AppendSamplePoint(
            List<RoadSamplePoint> destination,
            int roadIndex,
            int splineIndex,
            Spline spline,
            float width,
            LogicalSegmentDef segmentDef,
            float curveU,
            bool preferBoundary)
        {
            curveU = Mathf.Clamp01(curveU);
            int originalKnotIndex = ResolveKnotIndexAtCurveU(spline, curveU, -1);
            bool isOriginalKnot = originalKnotIndex >= 0 && originalKnotIndex < spline.Count
                && Mathf.Abs((spline.Count <= 1 ? 0f : originalKnotIndex / (float)(spline.Count - 1)) - curveU) <= SegmentRuntimeCurveUEpsilon;

            bool isSegmentBoundary = preferBoundary
                || Mathf.Abs(curveU - segmentDef.startCurveU) <= SegmentRuntimeCurveUEpsilon
                || Mathf.Abs(curveU - segmentDef.endCurveU) <= SegmentRuntimeCurveUEpsilon;

            destination.Add(new RoadSamplePoint
            {
                position = transform.TransformPoint(spline.EvaluatePosition(curveU)),
                roadIndex = roadIndex,
                splineIndex = splineIndex,
                curveU = curveU,
                width = width,
                isOriginalKnot = isOriginalKnot,
                originalKnotIndex = isOriginalKnot ? originalKnotIndex : -1,
                curveIndex = ResolveCurveIndexFromCurveU(spline, curveU),
                segmentId = segmentDef.segmentId,
                localSegmentIndex = segmentDef.localSegmentIndex,
                startMarkerId = segmentDef.startMarkerId,
                endMarkerId = segmentDef.endMarkerId,
                isSegmentBoundary = isSegmentBoundary,
            });
        }

        private void AppendOuterSidewalkEdgeLinesForSegment(List<(Vector3 start, Vector3 end)> edgeLines, int splineIndex, LoftRoadExtensionData roadData, Spline spline, LogicalSegmentDef segmentDef)
        {
            if (edgeLines == null || roadData == null || spline == null || !segmentDef.IsValid)
            {
                return;
            }

            float laneWidth = roadData.laneWidth.DefaultValue;
            float totalLaneCount = roadData.leftLaneCount + roadData.rightLaneCount;
            float totalWidth = totalLaneCount * laneWidth;
            float halfWidth = totalWidth * 0.5f;
            float leftSidewalkWidth = roadData.leftSidewalkWidth.DefaultValue;
            float rightSidewalkWidth = roadData.rightSidewalkWidth.DefaultValue;

            if (leftSidewalkWidth <= 0f && rightSidewalkWidth <= 0f)
            {
                return;
            }

            if (!TryBuildSegmentPathLocal(
                    spline,
                    segmentDef.startCurveU,
                    segmentDef.endCurveU,
                    SegmentUnitLength,
                    out List<Vector3> pathPoints,
                    out _,
                    out List<Vector3> pathTangents,
                    out _,
                    out _))
            {
                return;
            }

            float leftOuterOffset = -halfWidth;
            if (leftSidewalkWidth > 0f)
            {
                if (roadData.enableCurb)
                {
                    leftOuterOffset -= roadData.curbWidth;
                }

                if (roadData.enableRoadEdge)
                {
                    leftOuterOffset -= roadData.roadEdgeWidth;
                }

                leftOuterOffset -= leftSidewalkWidth;
            }

            float rightOuterOffset = halfWidth;
            if (rightSidewalkWidth > 0f)
            {
                if (roadData.enableCurb)
                {
                    rightOuterOffset += roadData.curbWidth;
                }

                if (roadData.enableRoadEdge)
                {
                    rightOuterOffset += roadData.roadEdgeWidth;
                }

                rightOuterOffset += rightSidewalkWidth;
            }

            Vector3? previousLeft = null;
            Vector3? previousRight = null;
            for (int pointIndex = 0; pointIndex < pathPoints.Count; pointIndex++)
            {
                Vector3 position = pathPoints[pointIndex];
                Vector3 tangent = pathTangents[pointIndex];
                Vector3 binormal = Vector3.Cross(Vector3.up, tangent).normalized;

                if (leftSidewalkWidth > 0f)
                {
                    Vector3 currentLeft = transform.TransformPoint(position + binormal * leftOuterOffset + Vector3.up * roadData.meshOffset);
                    if (previousLeft.HasValue)
                    {
                        edgeLines.Add((previousLeft.Value, currentLeft));
                    }

                    previousLeft = currentLeft;
                }

                if (rightSidewalkWidth > 0f)
                {
                    Vector3 currentRight = transform.TransformPoint(position + binormal * rightOuterOffset + Vector3.up * roadData.meshOffset);
                    if (previousRight.HasValue)
                    {
                        edgeLines.Add((previousRight.Value, currentRight));
                    }

                    previousRight = currentRight;
                }
            }
        }
    }
}
#endif
