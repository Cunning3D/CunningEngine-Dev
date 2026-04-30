#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Splines.Extension;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using Object = UnityEngine.Object;

namespace Unity.Splines.Examples
{
    internal static class ContinuousSplineImplicitJunctionManager
    {
        private const string RoadDataRootName = "Road_Data";
        private const string ImplicitJunctionObjectName = "Road_Junction_Implicit";
        private const float IntersectionHeightThreshold = 2f;
        private const float IntersectionMergeDistance = 4f;
        private const float SignatureMatchDistance = 12f;
        private const float CurveUMergeTolerance = 0.05f;
        private const float CurveUEpsilon = 0.0001f;
        private const float ImplicitJunctionShapeRangeScale = 1f;
        private const float ImplicitJunctionMinCutback = 2f;
        private const float ImplicitJunctionCutbackMargin = 0.25f;
        private const float ImplicitJunctionMinCrossSin = 0.35f;
        private const float ImplicitJunctionEndpointDistanceEpsilon = 0.05f;
        private const float BoundaryAlignmentWarningDistance = 0.02f;
        private const int BoundaryTransitionRefineIterations = 7;

        private static readonly Dictionary<LoftRoadBehaviour, HashSet<int>> s_DirtyRoads =
            new Dictionary<LoftRoadBehaviour, HashSet<int>>();

        private static bool s_Registered;
        private static bool s_FlushPending;

        internal static bool IsApplyingChanges { get; private set; }

        private readonly struct RoadSplineHandle : IEquatable<RoadSplineHandle>
        {
            public readonly long roadId;
            public readonly int splineIndex;

            public RoadSplineHandle(long roadId, int splineIndex)
            {
                this.roadId = roadId;
                this.splineIndex = splineIndex;
            }

            public bool Equals(RoadSplineHandle other)
            {
                return roadId == other.roadId && splineIndex == other.splineIndex;
            }

            public override bool Equals(object obj)
            {
                return obj is RoadSplineHandle other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (roadId.GetHashCode() * 397) ^ splineIndex;
                }
            }

            public override string ToString()
            {
                return $"{roadId}:{splineIndex}";
            }
        }

        private sealed class AuthoringSplinePolyline
        {
            public LoftRoadBehaviour road;
            public int splineIndex;
            public Spline spline;
            public float width;
            public readonly List<Vector3> positions = new List<Vector3>();
            public readonly List<float> curveUs = new List<float>();
            public Rect boundsXZ;
        }

        private sealed class CandidateAnchor
        {
            public LoftRoadBehaviour road;
            public int splineIndex;
            public float curveU;
            public Vector3 position;
            public float width;

            public RoadSplineHandle Handle => new RoadSplineHandle(road.StableRoadId, splineIndex);
        }

        private sealed class BoundaryAnchor
        {
            public LoftRoadBehaviour road;
            public int splineIndex;
            public float curveU;
            public Vector3 position;
            public float width;
            public bool includeInConnectedRoads;
            public long markerId;
            public RoadMarkerBoundaryRole boundaryRole;
            public int resolvedCurveIndex = -1;
            public float resolvedLocalT = -1f;

            public RoadSplineHandle Handle => new RoadSplineHandle(road.StableRoadId, splineIndex);
        }

        private sealed class RawIntersectionCandidate
        {
            public Vector3 center;
            public readonly CandidateAnchor[] anchors = new CandidateAnchor[2];
            public float mergeRadius;
        }

        private sealed class ImplicitJunctionCandidate
        {
            public Vector3 center;
            public readonly Dictionary<RoadSplineHandle, CandidateAnchor> anchors =
                new Dictionary<RoadSplineHandle, CandidateAnchor>();

            public string Signature =>
                string.Join("|", anchors.Keys.OrderBy(key => key.roadId).ThenBy(key => key.splineIndex).Select(key => key.ToString()));
        }

        private sealed class ExplicitJunctionCoverage
        {
            public Vector3 center;
            public float radius;
            public readonly HashSet<RoadSplineHandle> connectedSplines = new HashSet<RoadSplineHandle>();
        }

        private sealed class SplineArcProfile
        {
            public readonly List<float> curveUs = new List<float>();
            public readonly List<float> cumulativeLengths = new List<float>();
            public float totalLength;
        }

        private sealed class BoundaryProbePoint
        {
            public float curveU;
            public float arcDistance;
            public Vector3 position;
        }

        private sealed class BoundaryProbeLine
        {
            public CandidateAnchor anchor;
            public LoftRoadExtensionData roadData;
            public SplineArcProfile profile;
            public float centerDistance;
            public float cutbackDistance;
            public float probeStep;
            public readonly List<BoundaryProbePoint> points = new List<BoundaryProbePoint>();
        }

        internal static void EnqueueRoad(LoftRoadBehaviour road, int splineIndex)
        {
            if (IsApplyingChanges)
            {
                return;
            }

            if (road != null)
            {
                if (!s_DirtyRoads.TryGetValue(road, out HashSet<int> dirtySplineIndices))
                {
                    dirtySplineIndices = new HashSet<int>();
                    s_DirtyRoads.Add(road, dirtySplineIndices);
                }

                if (splineIndex >= 0)
                {
                    dirtySplineIndices.Add(splineIndex);
                }
            }

            s_FlushPending = true;
            Register();
        }

        internal static void NotifyRoadDestroyed(LoftRoadBehaviour road)
        {
            if (road != null)
            {
                s_DirtyRoads.Remove(road);
            }

            s_FlushPending = true;
            Register();
        }

        private static void Register()
        {
            if (s_Registered)
            {
                return;
            }

            EditorApplication.update += FlushOnEditorUpdate;
            s_Registered = true;
        }

        private static void FlushOnEditorUpdate()
        {
            if (IsApplyingChanges || !s_FlushPending)
            {
                return;
            }

            s_FlushPending = false;
            Flush();
        }

        private static void Flush()
        {
            if (IsApplyingChanges)
            {
                return;
            }

            try
            {
                IsApplyingChanges = true;
                FlushImpl();
            }
            finally
            {
                IsApplyingChanges = false;
                s_DirtyRoads.Clear();
            }
        }

        private static void FlushImpl()
        {
            List<LoftRoadBehaviour> roads = CollectEligibleRoads();
            List<JunctionData> implicitJunctions = Object.FindObjectsByType<JunctionData>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(junction => junction != null && junction.IsImplicitAutoJunction && !junction.IsPreview)
                .ToList();

            if (roads.Count == 0)
            {
                DestroyImplicitJunctions(implicitJunctions, new Dictionary<LoftRoadBehaviour, HashSet<int>>());
                return;
            }

            List<AuthoringSplinePolyline> polylines = BuildAuthoringSplinePolylines(roads);
            List<ExplicitJunctionCoverage> explicitCoverages = BuildExplicitJunctionCoverages();
            List<ImplicitJunctionCandidate> candidates = BuildImplicitJunctionCandidates(polylines, explicitCoverages);
            ApplyCandidates(candidates, implicitJunctions);
        }

        private static List<LoftRoadBehaviour> CollectEligibleRoads()
        {
            GameObject roadDataRoot = GameObject.Find(RoadDataRootName);
            if (roadDataRoot == null)
            {
                return new List<LoftRoadBehaviour>();
            }

            Transform roadDataTransform = roadDataRoot.transform;
            LoftRoadBehaviour[] allRoads = Object.FindObjectsByType<LoftRoadBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            List<LoftRoadBehaviour> eligibleRoads = new List<LoftRoadBehaviour>(allRoads.Length);
            foreach (LoftRoadBehaviour road in allRoads)
            {
                if (road == null || road.transform == null || !road.transform.IsChildOf(roadDataTransform))
                {
                    continue;
                }

                if (road.Container == null || road.Container.Splines == null || road.Container.Splines.Count == 0)
                {
                    continue;
                }

                _ = road.StableRoadId;
                eligibleRoads.Add(road);
            }

            return eligibleRoads;
        }

        private static List<AuthoringSplinePolyline> BuildAuthoringSplinePolylines(List<LoftRoadBehaviour> roads)
        {
            List<AuthoringSplinePolyline> polylines = new List<AuthoringSplinePolyline>();
            foreach (LoftRoadBehaviour road in roads)
            {
                IReadOnlyList<Spline> splines = road.Container?.Splines;
                if (splines == null)
                {
                    continue;
                }

                for (int splineIndex = 0; splineIndex < splines.Count; splineIndex++)
                {
                    Spline spline = splines[splineIndex];
                    if (spline == null || spline.Count < 2)
                    {
                        continue;
                    }

                    if (TryBuildAuthoredSplinePolyline(road, splineIndex, spline, out AuthoringSplinePolyline polyline))
                    {
                        polylines.Add(polyline);
                    }
                }
            }

            return polylines;
        }

        private static bool TryBuildAuthoredSplinePolyline(
            LoftRoadBehaviour road,
            int splineIndex,
            Spline spline,
            out AuthoringSplinePolyline polyline)
        {
            polyline = null;
            float width = ResolveRoadWidth(road, splineIndex);
            TryGetRoadData(road, splineIndex, out LoftRoadExtensionData roadData);
            float desiredSpacing = ResolveImplicitJunctionSampleInterval(roadData);
            List<float> denseCurveUs = new List<float>(128);
            List<float> denseLengths = new List<float>(128);
            List<float> curveUs = new List<float>(Mathf.Max(32, spline.Count * 2));
            BuildArcLengthResampledCurveUs(road, splineIndex, spline, desiredSpacing, curveUs, denseCurveUs, denseLengths);

            polyline = new AuthoringSplinePolyline
            {
                road = road,
                splineIndex = splineIndex,
                spline = spline,
                width = width,
            };

            float minX = float.PositiveInfinity;
            float minZ = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxZ = float.NegativeInfinity;

            float previousCurveU = float.NegativeInfinity;
            Vector3 previousPosition = default;
            bool hasPrevious = false;
            for (int index = 0; index < curveUs.Count; index++)
            {
                float curveU = Mathf.Clamp01(curveUs[index]);
                if (hasPrevious && Mathf.Abs(curveU - previousCurveU) <= CurveUEpsilon)
                {
                    continue;
                }

                Vector3 position = road.EvaluateSplineWorldPosition(splineIndex, curveU);
                if (hasPrevious && Vector3.Distance(previousPosition, position) <= 0.05f)
                {
                    previousCurveU = curveU;
                    continue;
                }

                polyline.curveUs.Add(curveU);
                polyline.positions.Add(position);
                minX = Mathf.Min(minX, position.x);
                minZ = Mathf.Min(minZ, position.z);
                maxX = Mathf.Max(maxX, position.x);
                maxZ = Mathf.Max(maxZ, position.z);
                previousCurveU = curveU;
                previousPosition = position;
                hasPrevious = true;
            }

            if (polyline.positions.Count < 2)
            {
                polyline = null;
                return false;
            }

            polyline.boundsXZ = Rect.MinMaxRect(minX, minZ, maxX, maxZ);
            return true;
        }

        private static float ResolveImplicitJunctionSampleInterval(LoftRoadExtensionData roadData)
        {
            return Mathf.Max(0.05f, roadData?.algorithmParameters != null ? roadData.algorithmParameters.sampleInterval : 1f);
        }

        private static void BuildArcLengthResampledCurveUs(
            LoftRoadBehaviour road,
            int splineIndex,
            Spline spline,
            float sampleInterval,
            List<float> curveUs,
            List<float> denseCurveUs,
            List<float> denseLengths)
        {
            curveUs.Clear();
            curveUs.Add(0f);
            curveUs.Add(1f);

            for (int knotIndex = 0; knotIndex < spline.Count; knotIndex++)
            {
                curveUs.Add(LoftRoadBehaviour.ComputeKnotCurveU(spline, knotIndex));
            }

            BuildWholeSplineArcLengthTable(road, splineIndex, Mathf.Max(0.01f, sampleInterval * 0.25f), denseCurveUs, denseLengths);
            float totalLength = denseLengths.Count > 0 ? denseLengths[denseLengths.Count - 1] : 0f;
            for (float targetDistance = sampleInterval; targetDistance < totalLength - sampleInterval * 0.5f; targetDistance += sampleInterval)
            {
                curveUs.Add(EvaluateCurveUAtArcDistance(denseCurveUs, denseLengths, targetDistance));
            }

            curveUs.Sort();
        }

        private static void BuildWholeSplineArcLengthTable(
            LoftRoadBehaviour road,
            int splineIndex,
            float desiredStepLength,
            List<float> curveUs,
            List<float> cumulativeLengths)
        {
            curveUs.Clear();
            cumulativeLengths.Clear();

            if (road == null)
            {
                return;
            }

            float estimatedLength = EstimateSplineLength(road, splineIndex, 96);
            int resolution = Mathf.Clamp(
                Mathf.CeilToInt(Mathf.Max(estimatedLength, desiredStepLength) / Mathf.Max(0.1f, desiredStepLength)),
                24,
                512);

            curveUs.Add(0f);
            cumulativeLengths.Add(0f);

            Vector3 previous = road.EvaluateSplineWorldPosition(splineIndex, 0f);
            float accumulated = 0f;
            for (int stepIndex = 1; stepIndex <= resolution; stepIndex++)
            {
                float curveU = stepIndex / (float)resolution;
                Vector3 current = road.EvaluateSplineWorldPosition(splineIndex, curveU);
                accumulated += Vector3.Distance(previous, current);
                curveUs.Add(curveU);
                cumulativeLengths.Add(accumulated);
                previous = current;
            }
        }

        private static List<ExplicitJunctionCoverage> BuildExplicitJunctionCoverages()
        {
            JunctionData[] junctions = Object.FindObjectsByType<JunctionData>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            List<ExplicitJunctionCoverage> coverages = new List<ExplicitJunctionCoverage>();
            foreach (JunctionData junction in junctions)
            {
                if (junction == null || junction.IsPreview || junction.IsImplicitAutoJunction || junction.connectedRoads == null || junction.connectedRoads.Count == 0)
                {
                    continue;
                }

                ExplicitJunctionCoverage coverage = new ExplicitJunctionCoverage
                {
                    center = junction.GetJunctionCenter(),
                    radius = 0f,
                };

                foreach (JunctionData.ConnectedRoad connectedRoad in junction.connectedRoads)
                {
                    if (connectedRoad?.roadBehaviour == null)
                    {
                        continue;
                    }

                    coverage.connectedSplines.Add(new RoadSplineHandle(connectedRoad.roadBehaviour.StableRoadId, connectedRoad.splineIndex));
                    float radius = Vector3.Distance(coverage.center, connectedRoad.GetConnectionPoint()) + Mathf.Max(connectedRoad.width, 3f);
                    coverage.radius = Mathf.Max(coverage.radius, radius);
                }

                if (coverage.connectedSplines.Count > 0)
                {
                    coverage.radius = Mathf.Max(coverage.radius, 4f);
                    coverages.Add(coverage);
                }
            }

            return coverages;
        }

        private static List<ImplicitJunctionCandidate> BuildImplicitJunctionCandidates(
            List<AuthoringSplinePolyline> polylines,
            List<ExplicitJunctionCoverage> explicitCoverages)
        {
            List<RawIntersectionCandidate> rawCandidates = new List<RawIntersectionCandidate>();
            for (int polylineIndex = 0; polylineIndex < polylines.Count; polylineIndex++)
            {
                AuthoringSplinePolyline a = polylines[polylineIndex];
                for (int otherIndex = polylineIndex + 1; otherIndex < polylines.Count; otherIndex++)
                {
                    AuthoringSplinePolyline b = polylines[otherIndex];
                    if (a.road == b.road && a.splineIndex == b.splineIndex)
                    {
                        continue;
                    }

                    if (!a.boundsXZ.Overlaps(b.boundsXZ))
                    {
                        continue;
                    }

                    CollectPairIntersections(a, b, rawCandidates);
                }
            }

            List<ImplicitJunctionCandidate> groupedCandidates = GroupCandidates(rawCandidates);
            groupedCandidates.RemoveAll(candidate => candidate.anchors.Count < 2 || IsCoveredByExplicitJunction(candidate, explicitCoverages));
            return groupedCandidates;
        }

        private static void CollectPairIntersections(
            AuthoringSplinePolyline a,
            AuthoringSplinePolyline b,
            List<RawIntersectionCandidate> rawCandidates)
        {
            for (int segmentIndexA = 0; segmentIndexA < a.positions.Count - 1; segmentIndexA++)
            {
                Vector3 a0 = a.positions[segmentIndexA];
                Vector3 a1 = a.positions[segmentIndexA + 1];
                for (int segmentIndexB = 0; segmentIndexB < b.positions.Count - 1; segmentIndexB++)
                {
                    Vector3 b0 = b.positions[segmentIndexB];
                    Vector3 b1 = b.positions[segmentIndexB + 1];
                    if (!TryIntersectSegmentsXZ(a0, a1, b0, b1, out float tA, out float tB))
                    {
                        continue;
                    }

                    float curveUA = Mathf.Lerp(a.curveUs[segmentIndexA], a.curveUs[segmentIndexA + 1], tA);
                    float curveUB = Mathf.Lerp(b.curveUs[segmentIndexB], b.curveUs[segmentIndexB + 1], tB);
                    if (IsEndpointCurveU(curveUA) && IsEndpointCurveU(curveUB))
                    {
                        continue;
                    }

                    Vector3 positionA = Vector3.Lerp(a0, a1, tA);
                    Vector3 positionB = Vector3.Lerp(b0, b1, tB);
                    if (Mathf.Abs(positionA.y - positionB.y) > IntersectionHeightThreshold)
                    {
                        continue;
                    }

                    RawIntersectionCandidate rawCandidate = new RawIntersectionCandidate
                    {
                        center = (positionA + positionB) * 0.5f,
                        mergeRadius = Mathf.Max(IntersectionMergeDistance, Mathf.Max(a.width, b.width)),
                    };
                    rawCandidate.anchors[0] = new CandidateAnchor
                    {
                        road = a.road,
                        splineIndex = a.splineIndex,
                        curveU = curveUA,
                        position = positionA,
                        width = a.width,
                    };
                    rawCandidate.anchors[1] = new CandidateAnchor
                    {
                        road = b.road,
                        splineIndex = b.splineIndex,
                        curveU = curveUB,
                        position = positionB,
                        width = b.width,
                    };
                    rawCandidates.Add(rawCandidate);
                }
            }
        }

        private static List<ImplicitJunctionCandidate> GroupCandidates(List<RawIntersectionCandidate> rawCandidates)
        {
            List<ImplicitJunctionCandidate> grouped = new List<ImplicitJunctionCandidate>();
            foreach (RawIntersectionCandidate rawCandidate in rawCandidates)
            {
                ImplicitJunctionCandidate target = null;
                foreach (ImplicitJunctionCandidate existing in grouped)
                {
                    if (!CanMerge(existing, rawCandidate))
                    {
                        continue;
                    }

                    target = existing;
                    break;
                }

                if (target == null)
                {
                    target = new ImplicitJunctionCandidate();
                    grouped.Add(target);
                }

                MergeCandidate(target, rawCandidate);
            }

            return grouped;
        }

        private static bool CanMerge(ImplicitJunctionCandidate candidate, RawIntersectionCandidate rawCandidate)
        {
            if (candidate.anchors.Count == 0)
            {
                return true;
            }

            if (Vector3.Distance(candidate.center, rawCandidate.center) > rawCandidate.mergeRadius)
            {
                return false;
            }

            for (int anchorIndex = 0; anchorIndex < rawCandidate.anchors.Length; anchorIndex++)
            {
                CandidateAnchor anchor = rawCandidate.anchors[anchorIndex];
                if (anchor == null)
                {
                    continue;
                }

                if (candidate.anchors.TryGetValue(anchor.Handle, out CandidateAnchor existingAnchor)
                    && Mathf.Abs(existingAnchor.curveU - anchor.curveU) > CurveUMergeTolerance)
                {
                    return false;
                }
            }

            return true;
        }

        private static void MergeCandidate(ImplicitJunctionCandidate candidate, RawIntersectionCandidate rawCandidate)
        {
            for (int anchorIndex = 0; anchorIndex < rawCandidate.anchors.Length; anchorIndex++)
            {
                CandidateAnchor incomingAnchor = rawCandidate.anchors[anchorIndex];
                if (incomingAnchor == null)
                {
                    continue;
                }

                RoadSplineHandle handle = incomingAnchor.Handle;
                if (candidate.anchors.TryGetValue(handle, out CandidateAnchor existingAnchor))
                {
                    if (ShouldReplaceMergedAnchor(existingAnchor, incomingAnchor, rawCandidate.center))
                    {
                        existingAnchor.curveU = incomingAnchor.curveU;
                        existingAnchor.position = incomingAnchor.road.EvaluateSplineWorldPosition(incomingAnchor.splineIndex, incomingAnchor.curveU);
                    }
                    else
                    {
                        existingAnchor.position = existingAnchor.road.EvaluateSplineWorldPosition(existingAnchor.splineIndex, existingAnchor.curveU);
                    }

                    existingAnchor.width = Mathf.Max(existingAnchor.width, incomingAnchor.width);
                    continue;
                }

                candidate.anchors.Add(handle, new CandidateAnchor
                {
                    road = incomingAnchor.road,
                    splineIndex = incomingAnchor.splineIndex,
                    curveU = incomingAnchor.curveU,
                    position = incomingAnchor.road.EvaluateSplineWorldPosition(incomingAnchor.splineIndex, incomingAnchor.curveU),
                    width = incomingAnchor.width,
                });
            }

            candidate.center = ComputeCandidateCenter(candidate);
        }

        private static bool ShouldReplaceMergedAnchor(CandidateAnchor existingAnchor, CandidateAnchor incomingAnchor, Vector3 rawCenter)
        {
            float existingDistance = (existingAnchor.position - rawCenter).sqrMagnitude;
            float incomingDistance = (incomingAnchor.position - rawCenter).sqrMagnitude;
            return incomingDistance + 1e-6f < existingDistance;
        }

        private static Vector3 ComputeCandidateCenter(ImplicitJunctionCandidate candidate)
        {
            if (candidate == null || candidate.anchors.Count == 0)
            {
                return Vector3.zero;
            }

            Vector3 center = Vector3.zero;
            foreach (CandidateAnchor anchor in candidate.anchors.Values)
            {
                center += anchor.position;
            }

            return center / candidate.anchors.Count;
        }

        private static bool IsCoveredByExplicitJunction(ImplicitJunctionCandidate candidate, List<ExplicitJunctionCoverage> explicitCoverages)
        {
            foreach (ExplicitJunctionCoverage coverage in explicitCoverages)
            {
                if (Vector3.Distance(candidate.center, coverage.center) > coverage.radius)
                {
                    continue;
                }

                int overlapCount = 0;
                foreach (RoadSplineHandle handle in candidate.anchors.Keys)
                {
                    if (coverage.connectedSplines.Contains(handle))
                    {
                        overlapCount++;
                    }
                }

                if (overlapCount >= 2)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ApplyCandidates(List<ImplicitJunctionCandidate> candidates, List<JunctionData> existingImplicitJunctions)
        {
            Dictionary<LoftRoadBehaviour, HashSet<int>> dirtyRoads = new Dictionary<LoftRoadBehaviour, HashSet<int>>();
            List<JunctionData> remaining = new List<JunctionData>(existingImplicitJunctions);
            Transform roadDataRoot = GetOrCreateRoadDataRoot();

            foreach (ImplicitJunctionCandidate candidate in candidates)
            {
                JunctionData junction = FindBestExistingMatch(candidate, remaining);
                if (junction == null)
                {
                    GameObject junctionObject = new GameObject(ImplicitJunctionObjectName);
                    junctionObject.transform.SetParent(roadDataRoot, true);
                    junction = junctionObject.AddComponent<JunctionData>();
                }

                remaining.Remove(junction);
                ApplyCandidateToJunction(candidate, junction, dirtyRoads);
            }

            DestroyImplicitJunctions(remaining, dirtyRoads);
            RefreshDirtyRoads(dirtyRoads);
        }

        private static JunctionData FindBestExistingMatch(ImplicitJunctionCandidate candidate, List<JunctionData> existingJunctions)
        {
            JunctionData bestMatch = null;
            float bestDistance = float.PositiveInfinity;
            string signature = candidate.Signature;
            foreach (JunctionData junction in existingJunctions)
            {
                if (junction == null || !string.Equals(junction.ImplicitAutoSignature, signature, StringComparison.Ordinal))
                {
                    continue;
                }

                float distance = Vector3.Distance(junction.transform.position, candidate.center);
                if (distance > SignatureMatchDistance || distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                bestMatch = junction;
            }

            return bestMatch;
        }

        private static void ApplyCandidateToJunction(
            ImplicitJunctionCandidate candidate,
            JunctionData junction,
            Dictionary<LoftRoadBehaviour, HashSet<int>> dirtyRoads)
        {
            List<JunctionData.ConnectedRoad> previousConnections = junction.connectedRoads != null
                ? junction.connectedRoads.Where(connectedRoad => connectedRoad?.roadBehaviour != null).ToList()
                : new List<JunctionData.ConnectedRoad>();

            HashSet<RoadSplineHandle> previousHandles = new HashSet<RoadSplineHandle>();
            foreach (JunctionData.ConnectedRoad previousConnection in previousConnections)
            {
                if (previousConnection?.roadBehaviour == null)
                {
                    continue;
                }

                RoadSplineHandle handle = new RoadSplineHandle(previousConnection.roadBehaviour.StableRoadId, previousConnection.splineIndex);
                if (!previousHandles.Add(handle))
                {
                    continue;
                }

            }

            List<BoundaryAnchor> boundaryAnchors = BuildBoundaryAnchors(candidate, junction);
            List<BoundaryAnchor> connectedRoadAnchors = boundaryAnchors
                .Where(anchor => anchor.includeInConnectedRoads)
                .OrderBy(anchor => Mathf.Atan2(anchor.position.z - candidate.center.z, anchor.position.x - candidate.center.x))
                .ToList();

            if (connectedRoadAnchors.Count == 0)
            {
                connectedRoadAnchors = boundaryAnchors
                    .OrderBy(anchor => Mathf.Atan2(anchor.position.z - candidate.center.z, anchor.position.x - candidate.center.x))
                    .ToList();
            }

            junction.connectedRoads = new List<JunctionData.ConnectedRoad>(connectedRoadAnchors.Count);
            HashSet<RoadSplineHandle> newHandles = new HashSet<RoadSplineHandle>();
            Dictionary<RoadSplineHandle, LoftRoadBehaviour> handleRoads = new Dictionary<RoadSplineHandle, LoftRoadBehaviour>();
            Dictionary<RoadSplineHandle, HashSet<long>> usedMarkerIdsByHandle = new Dictionary<RoadSplineHandle, HashSet<long>>();
            Dictionary<(long roadId, int splineIndex, RoadMarkerBoundaryRole boundaryRole, int endpointSide), (long markerId, float cachedCurveU, int preferredKnotIndex)> markerBindings =
                new Dictionary<(long roadId, int splineIndex, RoadMarkerBoundaryRole boundaryRole, int endpointSide), (long markerId, float cachedCurveU, int preferredKnotIndex)>();

            for (int anchorIndex = 0; anchorIndex < boundaryAnchors.Count; anchorIndex++)
            {
                BoundaryAnchor anchor = boundaryAnchors[anchorIndex];
                long markerId = anchor.markerId;
                if (markerId == 0
                    || !anchor.road.TryGetRoadMarker(markerId, out RoadMarker existingMarker)
                    || existingMarker == null
                    || existingMarker.junctionRef != junction
                    || existingMarker.splineIndex != anchor.splineIndex
                    || !existingMarker.isPinnedToKnot
                    || !existingMarker.isGeneratedBoundaryControl
                    || existingMarker.boundaryRole != anchor.boundaryRole)
                {
                    markerId = EnsureImplicitBoundaryControlMarker(anchor, junction);
                }

                float cachedCurveU = anchor.curveU;
                int cachedPreferredKnotIndex = 0;
                if (anchor.road.TryGetRoadMarker(markerId, out RoadMarker marker))
                {
                    cachedCurveU = marker.curveU;
                    cachedPreferredKnotIndex = marker.preferredKnotIndex;
                    anchor.curveU = marker.curveU;
                    anchor.position = anchor.road.EvaluateSplineWorldPosition(anchor.splineIndex, marker.curveU);
                }

                markerBindings[GetBoundaryBindingKey(anchor)] = (markerId, cachedCurveU, cachedPreferredKnotIndex);
                if (!usedMarkerIdsByHandle.TryGetValue(anchor.Handle, out HashSet<long> usedMarkerIds))
                {
                    usedMarkerIds = new HashSet<long>();
                    usedMarkerIdsByHandle.Add(anchor.Handle, usedMarkerIds);
                }

                usedMarkerIds.Add(markerId);
                handleRoads[anchor.Handle] = anchor.road;
                AddDirtyRoad(dirtyRoads, anchor.road, anchor.splineIndex);
                newHandles.Add(anchor.Handle);
                if (!anchor.road.connectedJunctions.Contains(junction))
                {
                    anchor.road.connectedJunctions.Add(junction);
                }
            }

            for (int anchorIndex = 0; anchorIndex < connectedRoadAnchors.Count; anchorIndex++)
            {
                BoundaryAnchor anchor = connectedRoadAnchors[anchorIndex];
                (long markerId, float cachedCurveU, int preferredKnotIndex) binding =
                    markerBindings[GetBoundaryBindingKey(anchor)];

                junction.connectedRoads.Add(new JunctionData.ConnectedRoad
                {
                    roadBehaviour = anchor.road,
                    splineIndex = anchor.splineIndex,
                    knotIndex = binding.preferredKnotIndex,
                    markerId = binding.markerId,
                    cachedCurveU = binding.cachedCurveU,
                    cachedPreferredKnotIndex = binding.preferredKnotIndex,
                    width = ResolveRoadWidth(anchor.road, anchor.splineIndex)
                });
            }

            foreach (RoadSplineHandle previousHandle in previousHandles)
            {
                if (newHandles.Contains(previousHandle))
                {
                    continue;
                }

                for (int connectionIndex = 0; connectionIndex < previousConnections.Count; connectionIndex++)
                {
                    JunctionData.ConnectedRoad previousConnection = previousConnections[connectionIndex];
                    if (previousConnection?.roadBehaviour == null)
                    {
                        continue;
                    }

                    if (previousConnection.roadBehaviour.StableRoadId != previousHandle.roadId
                        || previousConnection.splineIndex != previousHandle.splineIndex)
                    {
                        continue;
                    }

                    previousConnection.roadBehaviour.connectedJunctions.Remove(junction);
                    break;
                }
            }

            HashSet<RoadSplineHandle> cleanupHandles = new HashSet<RoadSplineHandle>(previousHandles);
            cleanupHandles.UnionWith(newHandles);
            foreach (RoadSplineHandle cleanupHandle in cleanupHandles)
            {
                if (!handleRoads.TryGetValue(cleanupHandle, out LoftRoadBehaviour cleanupRoad))
                {
                    cleanupRoad = previousConnections
                        .Where(connection => connection?.roadBehaviour != null)
                        .Select(connection => connection.roadBehaviour)
                        .FirstOrDefault(road => road != null && road.StableRoadId == cleanupHandle.roadId);
                }

                if (cleanupRoad == null)
                {
                    continue;
                }

                usedMarkerIdsByHandle.TryGetValue(cleanupHandle, out HashSet<long> usedMarkerIds);
                cleanupRoad.RemoveUnusedJunctionMarkersForJunction(junction, cleanupHandle.splineIndex, usedMarkerIds);
                AddDirtyRoad(dirtyRoads, cleanupRoad, cleanupHandle.splineIndex);
            }

            junction.transform.position = candidate.center;
            junction.SortConnectedRoads();
            RealignConnectedRoadCaches(junction);
            junction.SetImplicitAutoState(true, candidate.Signature);
            EditorUtility.SetDirty(junction);
        }

        private static (long roadId, int splineIndex, RoadMarkerBoundaryRole boundaryRole, int endpointSide) GetBoundaryBindingKey(BoundaryAnchor anchor)
        {
            return (
                anchor.road.StableRoadId,
                anchor.splineIndex,
                anchor.boundaryRole,
                ResolveBoundaryEndpointSide(anchor.boundaryRole, anchor.curveU));
        }

        private static int ResolveBoundaryEndpointSide(RoadMarkerBoundaryRole boundaryRole, float curveU)
        {
            if (boundaryRole == RoadMarkerBoundaryRole.LowerCurveU)
            {
                return 0;
            }

            if (boundaryRole == RoadMarkerBoundaryRole.UpperCurveU)
            {
                return 1;
            }

            return curveU >= 0.5f ? 1 : 0;
        }

        private static void RealignConnectedRoadCaches(JunctionData junction)
        {
            if (junction?.connectedRoads == null)
            {
                return;
            }

            for (int index = 0; index < junction.connectedRoads.Count; index++)
            {
                JunctionData.ConnectedRoad connectedRoad = junction.connectedRoads[index];
                if (connectedRoad?.roadBehaviour == null
                    || !connectedRoad.roadBehaviour.TryResolveConnectedRoadBinding(
                        connectedRoad.markerId,
                        connectedRoad.splineIndex,
                        connectedRoad.knotIndex,
                        connectedRoad.cachedCurveU,
                        connectedRoad.cachedPreferredKnotIndex,
                        out _,
                        out RoadMarker marker,
                        out float curveU,
                        out int preferredKnotIndex))
                {
                    continue;
                }

                Vector3 markerPosition = connectedRoad.roadBehaviour.EvaluateSplineWorldPosition(connectedRoad.splineIndex, curveU);
                Vector3 connectionPoint = connectedRoad.GetConnectionPoint();
                if (Vector3.Distance(markerPosition, connectionPoint) > BoundaryAlignmentWarningDistance)
                {
                    Debug.LogWarning(
                        $"Implicit junction boundary misalignment detected on {connectedRoad.roadBehaviour.name} spline {connectedRoad.splineIndex}; forcing marker cache alignment.");
                }

                if (marker != null)
                {
                    connectedRoad.markerId = marker.markerId;
                }

                connectedRoad.cachedCurveU = curveU;
                connectedRoad.cachedPreferredKnotIndex = preferredKnotIndex;
                connectedRoad.knotIndex = preferredKnotIndex;
            }
        }

        private static List<BoundaryAnchor> BuildBoundaryAnchors(ImplicitJunctionCandidate candidate, JunctionData junction)
        {
            List<BoundaryAnchor> anchors = new List<BoundaryAnchor>();
            Dictionary<RoadSplineHandle, SplineArcProfile> arcProfiles = new Dictionary<RoadSplineHandle, SplineArcProfile>();
            Dictionary<RoadSplineHandle, BoundaryProbeLine> probeLines = new Dictionary<RoadSplineHandle, BoundaryProbeLine>();
            foreach (CandidateAnchor candidateAnchor in candidate.anchors.Values)
            {
                if (candidateAnchor?.road == null)
                {
                    continue;
                }

                if (TryAppendPinnedBoundaryAnchors(junction, candidateAnchor, anchors))
                {
                    continue;
                }

                if (TryAppendProbeBoundaryAnchors(candidate, candidateAnchor, anchors, arcProfiles, probeLines))
                {
                    continue;
                }

                AppendCutbackBoundaryAnchors(candidate, candidateAnchor, anchors, arcProfiles);
            }

            return anchors;
        }

        private static bool TryAppendPinnedBoundaryAnchors(
            JunctionData junction,
            CandidateAnchor candidateAnchor,
            List<BoundaryAnchor> anchors)
        {
            if (junction == null || candidateAnchor?.road?.RoadMarkers == null)
            {
                return false;
            }

            List<RoadMarker> pinnedMarkers = candidateAnchor.road.RoadMarkers
                .Where(marker =>
                    marker != null &&
                    marker.kind == RoadMarkerKind.Junction &&
                    marker.junctionRef == junction &&
                    marker.splineIndex == candidateAnchor.splineIndex &&
                    marker.isPinnedToKnot &&
                    marker.isGeneratedBoundaryControl)
                .OrderBy(marker => marker.curveU)
                .ToList();

            if (pinnedMarkers.Count == 0)
            {
                return false;
            }

            List<RoadMarker> selectedMarkers = SelectPinnedBoundaryMarkers(pinnedMarkers, candidateAnchor.curveU);
            if (selectedMarkers.Count == 0)
            {
                return false;
            }

            if (selectedMarkers.Count == 1 && !IsEndpointCurveU(selectedMarkers[0].curveU))
            {
                RoadMarker marker = selectedMarkers[0];
                AddBoundaryAnchor(
                    anchors,
                    candidateAnchor,
                    marker.curveU,
                    candidateAnchor.road.EvaluateSplineWorldPosition(candidateAnchor.splineIndex, marker.curveU),
                    true,
                    marker.markerId,
                    marker.boundaryRole);
                return true;
            }

            RoadMarker firstMarker = selectedMarkers[0];
            RoadMarker lastMarker = selectedMarkers[selectedMarkers.Count - 1];
            bool includeFirst = !IsEndpointCurveU(firstMarker.curveU);
            bool includeLast = !IsEndpointCurveU(lastMarker.curveU);
            if (!includeFirst && !includeLast)
            {
                includeFirst = true;
                includeLast = Mathf.Abs(lastMarker.curveU - firstMarker.curveU) > CurveUEpsilon;
            }

            AddBoundaryAnchor(
                anchors,
                candidateAnchor,
                firstMarker.curveU,
                candidateAnchor.road.EvaluateSplineWorldPosition(candidateAnchor.splineIndex, firstMarker.curveU),
                includeFirst,
                firstMarker.markerId,
                firstMarker.boundaryRole);

            if (Mathf.Abs(lastMarker.curveU - firstMarker.curveU) > CurveUEpsilon)
            {
                AddBoundaryAnchor(
                    anchors,
                    candidateAnchor,
                    lastMarker.curveU,
                    candidateAnchor.road.EvaluateSplineWorldPosition(candidateAnchor.splineIndex, lastMarker.curveU),
                    includeLast,
                    lastMarker.markerId,
                    lastMarker.boundaryRole);
            }

            return true;
        }

        private static List<RoadMarker> SelectPinnedBoundaryMarkers(List<RoadMarker> pinnedMarkers, float centerCurveU)
        {
            List<RoadMarker> selectedMarkers = new List<RoadMarker>(2);
            if (pinnedMarkers == null || pinnedMarkers.Count == 0)
            {
                return selectedMarkers;
            }

            RoadMarker lowerMarker = pinnedMarkers.FirstOrDefault(marker => marker.boundaryRole == RoadMarkerBoundaryRole.LowerCurveU);
            RoadMarker upperMarker = pinnedMarkers.FirstOrDefault(marker => marker.boundaryRole == RoadMarkerBoundaryRole.UpperCurveU);
            List<RoadMarker> endpointMarkers = pinnedMarkers
                .Where(marker => marker.boundaryRole == RoadMarkerBoundaryRole.Endpoint)
                .OrderBy(marker => marker.curveU)
                .ToList();

            if (lowerMarker != null)
            {
                selectedMarkers.Add(lowerMarker);
            }

            if (upperMarker != null && upperMarker != lowerMarker)
            {
                selectedMarkers.Add(upperMarker);
            }

            if (selectedMarkers.Count == 0 && endpointMarkers.Count > 0)
            {
                selectedMarkers.Add(endpointMarkers[0]);
                if (endpointMarkers.Count > 1 && endpointMarkers[endpointMarkers.Count - 1] != endpointMarkers[0])
                {
                    selectedMarkers.Add(endpointMarkers[endpointMarkers.Count - 1]);
                }
            }
            else if (selectedMarkers.Count == 1 && endpointMarkers.Count > 0)
            {
                RoadMarker complement = endpointMarkers
                    .OrderBy(marker => Mathf.Abs(marker.curveU - centerCurveU))
                    .FirstOrDefault(marker => marker != selectedMarkers[0]);
                if (complement != null)
                {
                    selectedMarkers.Add(complement);
                }
            }

            if (selectedMarkers.Count > 0)
            {
                selectedMarkers.Sort((left, right) => left.curveU.CompareTo(right.curveU));
                return selectedMarkers;
            }

            RoadMarker leftMarker = null;
            RoadMarker rightMarker = null;
            int nearestIndex = 0;
            float nearestDistance = float.PositiveInfinity;

            for (int markerIndex = 0; markerIndex < pinnedMarkers.Count; markerIndex++)
            {
                RoadMarker marker = pinnedMarkers[markerIndex];
                if (marker == null)
                {
                    continue;
                }

                float distance = Mathf.Abs(marker.curveU - centerCurveU);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestIndex = markerIndex;
                }

                if (marker.curveU <= centerCurveU + CurveUEpsilon)
                {
                    leftMarker = marker;
                }

                if (rightMarker == null && marker.curveU >= centerCurveU - CurveUEpsilon)
                {
                    rightMarker = marker;
                }
            }

            leftMarker ??= pinnedMarkers[0];
            rightMarker ??= pinnedMarkers[pinnedMarkers.Count - 1];
            if (leftMarker == null && rightMarker == null)
            {
                return selectedMarkers;
            }

            if (leftMarker == rightMarker && pinnedMarkers.Count > 1)
            {
                RoadMarker previousMarker = nearestIndex > 0 ? pinnedMarkers[nearestIndex - 1] : null;
                RoadMarker nextMarker = nearestIndex < pinnedMarkers.Count - 1 ? pinnedMarkers[nearestIndex + 1] : null;
                if (previousMarker == null)
                {
                    rightMarker = nextMarker;
                }
                else if (nextMarker == null)
                {
                    leftMarker = previousMarker;
                }
                else if (Mathf.Abs(previousMarker.curveU - centerCurveU) <= Mathf.Abs(nextMarker.curveU - centerCurveU))
                {
                    leftMarker = previousMarker;
                }
                else
                {
                    rightMarker = nextMarker;
                }
            }

            if (leftMarker != null)
            {
                selectedMarkers.Add(leftMarker);
            }

            if (rightMarker != null && rightMarker != leftMarker)
            {
                selectedMarkers.Add(rightMarker);
            }

            selectedMarkers.Sort((left, right) => left.curveU.CompareTo(right.curveU));
            return selectedMarkers;
        }

        private static bool TryAppendProbeBoundaryAnchors(
            ImplicitJunctionCandidate candidate,
            CandidateAnchor candidateAnchor,
            List<BoundaryAnchor> anchors,
            Dictionary<RoadSplineHandle, SplineArcProfile> arcProfiles,
            Dictionary<RoadSplineHandle, BoundaryProbeLine> probeLines)
        {
            if (!TryGetRoadData(candidateAnchor.road, candidateAnchor.splineIndex, out LoftRoadExtensionData roadData))
            {
                return false;
            }

            BoundaryProbeLine sourceLine = GetOrBuildBoundaryProbeLine(candidate, candidateAnchor, roadData, arcProfiles, probeLines);
            if (sourceLine == null || sourceLine.points.Count < 2)
            {
                return false;
            }

            HashSet<int> connectedIndices = new HashSet<int>();
            int seedIndex = -1;
            float bestSeedDistance = float.PositiveInfinity;
            for (int pointIndex = 0; pointIndex < sourceLine.points.Count; pointIndex++)
            {
                BoundaryProbePoint point = sourceLine.points[pointIndex];
                if (!IsBoundaryProbeConnected(candidate, sourceLine, point, arcProfiles, probeLines))
                {
                    continue;
                }

                connectedIndices.Add(pointIndex);
                float seedDistance = Mathf.Abs(point.arcDistance - sourceLine.centerDistance);
                if (seedDistance < bestSeedDistance)
                {
                    bestSeedDistance = seedDistance;
                    seedIndex = pointIndex;
                }
            }

            if (seedIndex < 0 || connectedIndices.Count == 0)
            {
                return false;
            }

            int leftIndex = seedIndex;
            while (leftIndex > 0
                   && connectedIndices.Contains(leftIndex - 1)
                   && sourceLine.points[leftIndex].arcDistance - sourceLine.points[leftIndex - 1].arcDistance <= sourceLine.probeStep * 1.75f)
            {
                leftIndex--;
            }

            int rightIndex = seedIndex;
            while (rightIndex + 1 < sourceLine.points.Count
                   && connectedIndices.Contains(rightIndex + 1)
                   && sourceLine.points[rightIndex + 1].arcDistance - sourceLine.points[rightIndex].arcDistance <= sourceLine.probeStep * 1.75f)
            {
                rightIndex++;
            }

            BoundaryProbePoint leftConnected = sourceLine.points[leftIndex];
            BoundaryProbePoint rightConnected = sourceLine.points[rightIndex];
            BoundaryProbePoint leftDisconnected = leftIndex > 0 ? sourceLine.points[leftIndex - 1] : null;
            BoundaryProbePoint rightDisconnected = rightIndex + 1 < sourceLine.points.Count ? sourceLine.points[rightIndex + 1] : null;

            bool hasLeftBoundary = TryResolveProbeBoundaryAnchor(
                candidate,
                sourceLine,
                leftDisconnected,
                leftConnected,
                true,
                arcProfiles,
                probeLines,
                out float leftCurveU,
                out Vector3 leftPosition);
            bool hasRightBoundary = TryResolveProbeBoundaryAnchor(
                candidate,
                sourceLine,
                rightDisconnected,
                rightConnected,
                false,
                arcProfiles,
                probeLines,
                out float rightCurveU,
                out Vector3 rightPosition);

            if (!hasLeftBoundary)
            {
                hasLeftBoundary = TryResolveCutbackFallbackBoundary(sourceLine, true, out leftCurveU, out leftPosition);
            }

            if (!hasRightBoundary)
            {
                hasRightBoundary = TryResolveCutbackFallbackBoundary(sourceLine, false, out rightCurveU, out rightPosition);
            }

            if (!hasLeftBoundary && !hasRightBoundary)
            {
                return false;
            }

            bool includeLeft = hasLeftBoundary && !IsEndpointCurveU(leftCurveU);
            bool includeRight = hasRightBoundary && !IsEndpointCurveU(rightCurveU);
            if (!includeLeft && !includeRight)
            {
                includeLeft = hasLeftBoundary;
                includeRight = hasRightBoundary && (!hasLeftBoundary || Mathf.Abs(rightCurveU - leftCurveU) > CurveUEpsilon);
            }

            if (hasLeftBoundary)
            {
                AddBoundaryAnchor(
                    anchors,
                    candidateAnchor,
                    leftCurveU,
                    leftPosition,
                    includeLeft,
                    0,
                    ResolveBoundaryRole(leftCurveU, true));
            }

            if (hasRightBoundary && (!hasLeftBoundary || Mathf.Abs(rightCurveU - leftCurveU) > CurveUEpsilon))
            {
                AddBoundaryAnchor(
                    anchors,
                    candidateAnchor,
                    rightCurveU,
                    rightPosition,
                    includeRight,
                    0,
                    ResolveBoundaryRole(rightCurveU, false));
            }

            return hasLeftBoundary || hasRightBoundary;
        }

        private static bool TryResolveProbeBoundaryAnchor(
            ImplicitJunctionCandidate candidate,
            BoundaryProbeLine sourceLine,
            BoundaryProbePoint disconnectedPoint,
            BoundaryProbePoint connectedPoint,
            bool isLowerBoundary,
            Dictionary<RoadSplineHandle, SplineArcProfile> arcProfiles,
            Dictionary<RoadSplineHandle, BoundaryProbeLine> probeLines,
            out float refinedCurveU,
            out Vector3 refinedPosition)
        {
            refinedCurveU = 0f;
            refinedPosition = Vector3.zero;
            if (sourceLine?.anchor?.road == null || connectedPoint == null)
            {
                return false;
            }

            if (disconnectedPoint == null)
            {
                refinedCurveU = connectedPoint.curveU;
                refinedPosition = connectedPoint.position;
                return IsEndpointCurveU(refinedCurveU);
            }

            float disconnectedDistance = disconnectedPoint.arcDistance;
            float connectedDistance = connectedPoint.arcDistance;
            if (Mathf.Abs(connectedDistance - disconnectedDistance) <= 1e-5f)
            {
                refinedCurveU = connectedPoint.curveU;
                refinedPosition = connectedPoint.position;
                return true;
            }

            for (int iteration = 0; iteration < BoundaryTransitionRefineIterations; iteration++)
            {
                float midDistance = (connectedDistance + disconnectedDistance) * 0.5f;
                float midCurveU = EvaluateCurveUAtArcDistance(sourceLine.profile, midDistance);
                bool midConnected = EvaluateProbeConnectivityAtCurveU(candidate, sourceLine, midCurveU, arcProfiles, probeLines);
                if (midConnected)
                {
                    connectedDistance = midDistance;
                }
                else
                {
                    disconnectedDistance = midDistance;
                }
            }

            float snappedDistance = connectedDistance;
            refinedCurveU = EvaluateCurveUAtArcDistance(sourceLine.profile, snappedDistance);
            refinedPosition = sourceLine.anchor.road.EvaluateSplineWorldPosition(sourceLine.anchor.splineIndex, refinedCurveU);
            return true;
        }

        private static bool TryResolveCutbackFallbackBoundary(
            BoundaryProbeLine sourceLine,
            bool isLowerBoundary,
            out float curveU,
            out Vector3 position)
        {
            curveU = 0f;
            position = Vector3.zero;
            if (sourceLine?.anchor?.road == null || sourceLine.profile == null)
            {
                return false;
            }

            float distance = isLowerBoundary
                ? Mathf.Max(0f, sourceLine.centerDistance - sourceLine.cutbackDistance)
                : Mathf.Min(sourceLine.profile.totalLength, sourceLine.centerDistance + sourceLine.cutbackDistance);
            curveU = EvaluateCurveUAtArcDistance(sourceLine.profile, distance);
            position = sourceLine.anchor.road.EvaluateSplineWorldPosition(sourceLine.anchor.splineIndex, curveU);
            return true;
        }

        private static bool EvaluateProbeConnectivityAtCurveU(
            ImplicitJunctionCandidate candidate,
            BoundaryProbeLine sourceLine,
            float curveU,
            Dictionary<RoadSplineHandle, SplineArcProfile> arcProfiles,
            Dictionary<RoadSplineHandle, BoundaryProbeLine> probeLines)
        {
            BoundaryProbePoint point = new BoundaryProbePoint
            {
                curveU = Mathf.Clamp01(curveU),
                arcDistance = EvaluateArcDistanceAtCurveU(sourceLine.profile, curveU),
                position = sourceLine.anchor.road.EvaluateSplineWorldPosition(sourceLine.anchor.splineIndex, curveU),
            };
            return IsBoundaryProbeConnected(candidate, sourceLine, point, arcProfiles, probeLines);
        }

        private static BoundaryProbeLine GetOrBuildBoundaryProbeLine(
            ImplicitJunctionCandidate candidate,
            CandidateAnchor candidateAnchor,
            LoftRoadExtensionData roadData,
            Dictionary<RoadSplineHandle, SplineArcProfile> arcProfiles,
            Dictionary<RoadSplineHandle, BoundaryProbeLine> probeLines)
        {
            if (candidateAnchor == null)
            {
                return null;
            }

            if (probeLines.TryGetValue(candidateAnchor.Handle, out BoundaryProbeLine existingLine))
            {
                return existingLine;
            }

            SplineArcProfile profile = GetOrBuildArcProfile(candidateAnchor, arcProfiles);
            if (profile == null || profile.totalLength <= 1e-5f)
            {
                return null;
            }

            BoundaryProbeLine line = new BoundaryProbeLine
            {
                anchor = candidateAnchor,
                roadData = roadData,
                profile = profile,
                centerDistance = EvaluateArcDistanceAtCurveU(profile, candidateAnchor.curveU),
                cutbackDistance = ResolveAnchorCutbackDistance(candidate, candidateAnchor),
                probeStep = ResolveBoundaryProbeStep(candidateAnchor, roadData)
            };

            float padding = Mathf.Max(line.probeStep, candidateAnchor.width * 0.1f);
            float startDistance = Mathf.Max(0f, line.centerDistance - line.cutbackDistance - padding);
            float endDistance = Mathf.Min(profile.totalLength, line.centerDistance + line.cutbackDistance + padding);

            AppendBoundaryProbePointAtArcDistance(line, startDistance);
            for (float distance = startDistance + line.probeStep; distance < endDistance - line.probeStep * 0.5f; distance += line.probeStep)
            {
                AppendBoundaryProbePointAtArcDistance(line, distance);
            }

            AppendBoundaryProbePointAtArcDistance(line, line.centerDistance);
            AppendBoundaryProbePointAtArcDistance(line, endDistance);

            IReadOnlyList<Spline> splines = candidateAnchor.road.Container?.Splines;
            if (splines != null && candidateAnchor.splineIndex >= 0 && candidateAnchor.splineIndex < splines.Count)
            {
                Spline spline = splines[candidateAnchor.splineIndex];
                if (spline != null)
                {
                    for (int knotIndex = 0; knotIndex < spline.Count; knotIndex++)
                    {
                        float knotCurveU = LoftRoadBehaviour.ComputeKnotCurveU(spline, knotIndex);
                        float knotDistance = EvaluateArcDistanceAtCurveU(profile, knotCurveU);
                        if (knotDistance + CurveUEpsilon < startDistance || knotDistance - CurveUEpsilon > endDistance)
                        {
                            continue;
                        }

                        AppendBoundaryProbePointAtCurveU(line, knotCurveU);
                    }
                }
            }

            line.points.Sort((left, right) => left.arcDistance.CompareTo(right.arcDistance));
            probeLines[candidateAnchor.Handle] = line;
            return line;
        }

        private static float ResolveBoundaryProbeStep(CandidateAnchor candidateAnchor, LoftRoadExtensionData roadData)
        {
            return ResolveImplicitJunctionSampleInterval(roadData);
        }

        private static void AppendBoundaryProbePointAtArcDistance(BoundaryProbeLine line, float arcDistance)
        {
            if (line?.profile == null)
            {
                return;
            }

            float clampedDistance = Mathf.Clamp(arcDistance, 0f, line.profile.totalLength);
            float curveU = EvaluateCurveUAtArcDistance(line.profile.curveUs, line.profile.cumulativeLengths, clampedDistance);
            AppendBoundaryProbePoint(line, curveU);
        }

        private static void AppendBoundaryProbePointAtCurveU(BoundaryProbeLine line, float curveU)
        {
            AppendBoundaryProbePoint(line, curveU);
        }

        private static void AppendBoundaryProbePoint(BoundaryProbeLine line, float curveU)
        {
            if (line?.anchor?.road == null)
            {
                return;
            }

            float clampedCurveU = Mathf.Clamp01(curveU);
            for (int pointIndex = 0; pointIndex < line.points.Count; pointIndex++)
            {
                if (Mathf.Abs(line.points[pointIndex].curveU - clampedCurveU) <= CurveUEpsilon)
                {
                    return;
                }
            }

            line.points.Add(new BoundaryProbePoint
            {
                curveU = clampedCurveU,
                arcDistance = EvaluateArcDistanceAtCurveU(line.profile, clampedCurveU),
                position = line.anchor.road.EvaluateSplineWorldPosition(line.anchor.splineIndex, clampedCurveU)
            });
        }

        private static bool IsBoundaryProbeConnected(
            ImplicitJunctionCandidate candidate,
            BoundaryProbeLine sourceLine,
            BoundaryProbePoint sourcePoint,
            Dictionary<RoadSplineHandle, SplineArcProfile> arcProfiles,
            Dictionary<RoadSplineHandle, BoundaryProbeLine> probeLines)
        {
            foreach (CandidateAnchor otherAnchor in candidate.anchors.Values)
            {
                if (otherAnchor == null
                    || otherAnchor.Handle.Equals(sourceLine.anchor.Handle)
                    || !TryGetRoadData(otherAnchor.road, otherAnchor.splineIndex, out LoftRoadExtensionData otherRoadData))
                {
                    continue;
                }

                BoundaryProbeLine otherLine = GetOrBuildBoundaryProbeLine(candidate, otherAnchor, otherRoadData, arcProfiles, probeLines);
                if (otherLine == null)
                {
                    continue;
                }

                for (int pointIndex = 0; pointIndex < otherLine.points.Count; pointIndex++)
                {
                    if (ShouldConnectSamplePoints(
                            sourcePoint.position,
                            sourceLine.roadData,
                            otherLine.points[pointIndex].position,
                            otherRoadData,
                            ImplicitJunctionShapeRangeScale))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool AppendBoundaryAnchorsFromOrderedPoints(
            CandidateAnchor candidateAnchor,
            List<(float curveU, Vector3 position)> orderedPoints,
            List<BoundaryAnchor> anchors)
        {
            if (orderedPoints == null || orderedPoints.Count == 0)
            {
                return false;
            }

            if (orderedPoints.Count == 1 && !IsEndpointCurveU(orderedPoints[0].curveU))
            {
                return false;
            }

            (float curveU, Vector3 position) first = orderedPoints[0];
            (float curveU, Vector3 position) last = orderedPoints[orderedPoints.Count - 1];
            bool includeFirst = !IsEndpointCurveU(first.curveU);
            bool includeLast = !IsEndpointCurveU(last.curveU);
            if (!includeFirst && !includeLast)
            {
                includeFirst = true;
                includeLast = Mathf.Abs(last.curveU - first.curveU) > CurveUEpsilon;
            }

            AddBoundaryAnchor(
                anchors,
                candidateAnchor,
                first.curveU,
                first.position,
                includeFirst,
                0,
                ResolveBoundaryRole(first.curveU, true));
            if (Mathf.Abs(last.curveU - first.curveU) > CurveUEpsilon)
            {
                AddBoundaryAnchor(
                    anchors,
                    candidateAnchor,
                    last.curveU,
                    last.position,
                    includeLast,
                    0,
                    ResolveBoundaryRole(last.curveU, false));
            }

            return true;
        }

        private static void AppendCutbackBoundaryAnchors(
            ImplicitJunctionCandidate candidate,
            CandidateAnchor candidateAnchor,
            List<BoundaryAnchor> anchors,
            Dictionary<RoadSplineHandle, SplineArcProfile> arcProfiles)
        {
            float cutbackDistance = ResolveAnchorCutbackDistance(candidate, candidateAnchor);
            SplineArcProfile profile = GetOrBuildArcProfile(candidateAnchor, arcProfiles);
            if (profile == null || profile.totalLength <= 1e-5f)
            {
                AddBoundaryAnchor(
                    anchors,
                    candidateAnchor,
                    candidateAnchor.curveU,
                    candidateAnchor.position,
                    true,
                    0,
                    ResolveBoundaryRole(candidateAnchor.curveU, candidateAnchor.curveU <= 0.5f));
                return;
            }

            float centerDistance = EvaluateArcDistanceAtCurveU(profile, candidateAnchor.curveU);
            float leftDistance = Mathf.Max(0f, centerDistance - cutbackDistance);
            float rightDistance = Mathf.Min(profile.totalLength, centerDistance + cutbackDistance);
            float leftCurveU = EvaluateCurveUAtArcDistance(profile, leftDistance);
            float rightCurveU = EvaluateCurveUAtArcDistance(profile, rightDistance);
            bool leftIsEndpoint = leftDistance <= ImplicitJunctionEndpointDistanceEpsilon || leftCurveU <= CurveUEpsilon;
            bool rightIsEndpoint = rightDistance >= profile.totalLength - ImplicitJunctionEndpointDistanceEpsilon || rightCurveU >= 1f - CurveUEpsilon;

            bool includeLeft = !leftIsEndpoint;
            bool includeRight = !rightIsEndpoint;
            if (!includeLeft && !includeRight)
            {
                includeLeft = true;
                includeRight = Mathf.Abs(rightCurveU - leftCurveU) > CurveUEpsilon;
            }

            AddBoundaryAnchor(
                anchors,
                candidateAnchor,
                leftCurveU,
                candidateAnchor.road.EvaluateSplineWorldPosition(candidateAnchor.splineIndex, leftCurveU),
                includeLeft,
                0,
                ResolveBoundaryRole(leftCurveU, true));

            AddBoundaryAnchor(
                anchors,
                candidateAnchor,
                rightCurveU,
                candidateAnchor.road.EvaluateSplineWorldPosition(candidateAnchor.splineIndex, rightCurveU),
                includeRight,
                0,
                ResolveBoundaryRole(rightCurveU, false));
        }

        private static void AddBoundaryAnchor(
            List<BoundaryAnchor> anchors,
            CandidateAnchor source,
            float curveU,
            Vector3 position,
            bool includeInConnectedRoads,
            long markerId,
            RoadMarkerBoundaryRole boundaryRole)
        {
            curveU = Mathf.Clamp01(curveU);
            int resolvedCurveIndex = -1;
            float resolvedLocalT = -1f;
            if (source?.road != null
                && source.road.TryResolveSplineTargetAtCurveU(source.splineIndex, curveU, out int existingKnotIndex, out int curveIndex, out float localT))
            {
                resolvedCurveIndex = curveIndex;
                resolvedLocalT = localT;
                if (existingKnotIndex >= 0
                    && source.road.Container?.Splines != null
                    && source.splineIndex >= 0
                    && source.splineIndex < source.road.Container.Splines.Count)
                {
                    Spline spline = source.road.Container.Splines[source.splineIndex];
                    curveU = LoftRoadBehaviour.ComputeKnotCurveU(spline, existingKnotIndex);
                }

                position = source.road.EvaluateSplineWorldPosition(source.splineIndex, curveU);
            }

            for (int index = 0; index < anchors.Count; index++)
            {
                BoundaryAnchor existing = anchors[index];
                if (existing.road != source.road || existing.splineIndex != source.splineIndex)
                {
                    continue;
                }

                bool sameBoundaryIdentity = existing.boundaryRole == boundaryRole
                    && (boundaryRole != RoadMarkerBoundaryRole.Endpoint
                        || ResolveBoundaryEndpointSide(existing.boundaryRole, existing.curveU) == ResolveBoundaryEndpointSide(boundaryRole, curveU));
                if (!sameBoundaryIdentity && Mathf.Abs(existing.curveU - curveU) > CurveUEpsilon)
                {
                    continue;
                }

                existing.width = Mathf.Max(existing.width, source.width);
                existing.curveU = curveU;
                existing.position = position;
                existing.includeInConnectedRoads |= includeInConnectedRoads;
                existing.resolvedCurveIndex = resolvedCurveIndex;
                existing.resolvedLocalT = resolvedLocalT;
                if (existing.markerId == 0)
                {
                    existing.markerId = markerId;
                }

                if (existing.boundaryRole == RoadMarkerBoundaryRole.None
                    || (existing.boundaryRole == RoadMarkerBoundaryRole.Endpoint && boundaryRole != RoadMarkerBoundaryRole.None))
                {
                    existing.boundaryRole = boundaryRole;
                }
                return;
            }

            anchors.Add(new BoundaryAnchor
            {
                road = source.road,
                splineIndex = source.splineIndex,
                curveU = curveU,
                position = position,
                width = source.width,
                includeInConnectedRoads = includeInConnectedRoads,
                markerId = markerId,
                boundaryRole = boundaryRole,
                resolvedCurveIndex = resolvedCurveIndex,
                resolvedLocalT = resolvedLocalT,
            });
        }

        private static RoadMarkerBoundaryRole ResolveBoundaryRole(float curveU, bool isLowerBoundary)
        {
            if (IsEndpointCurveU(curveU))
            {
                return RoadMarkerBoundaryRole.Endpoint;
            }

            return isLowerBoundary ? RoadMarkerBoundaryRole.LowerCurveU : RoadMarkerBoundaryRole.UpperCurveU;
        }

        private static long EnsureImplicitBoundaryControlMarker(BoundaryAnchor anchor, JunctionData junction)
        {
            if (anchor?.road == null)
            {
                return 0;
            }

            long markerId = anchor.markerId;
            if (markerId != 0
                && anchor.road.TryGetRoadMarker(markerId, out RoadMarker existingMarker)
                && existingMarker != null
                && existingMarker.junctionRef == junction
                && existingMarker.splineIndex == anchor.splineIndex
                && existingMarker.isPinnedToKnot
                && existingMarker.isGeneratedBoundaryControl
                && existingMarker.boundaryRole == anchor.boundaryRole)
            {
                return markerId;
            }

            if (anchor.resolvedCurveIndex < 0
                || anchor.resolvedLocalT < 0f
                || !anchor.road.TryResolveSplineTargetAtCurveU(
                    anchor.splineIndex,
                    anchor.curveU,
                    out _,
                    out anchor.resolvedCurveIndex,
                    out anchor.resolvedLocalT))
            {
                return 0;
            }

            return anchor.road.EnsureJunctionMarkerAtResolvedSplineTarget(
                anchor.splineIndex,
                anchor.curveU,
                anchor.resolvedCurveIndex,
                anchor.resolvedLocalT,
                junction,
                true,
                anchor.boundaryRole);
        }

        private static float ResolveAnchorCutbackDistance(ImplicitJunctionCandidate candidate, CandidateAnchor anchor)
        {
            float cutbackDistance = Mathf.Max(ImplicitJunctionMinCutback, anchor.width * 0.5f);
            Vector3 anchorTangent = anchor.road.EvaluateSplineWorldTangent(anchor.splineIndex, anchor.curveU);
            anchorTangent.y = 0f;
            if (anchorTangent.sqrMagnitude <= 1e-5f)
            {
                return cutbackDistance + ImplicitJunctionCutbackMargin;
            }

            anchorTangent.Normalize();
            foreach (CandidateAnchor otherAnchor in candidate.anchors.Values)
            {
                if (otherAnchor == null || otherAnchor == anchor)
                {
                    continue;
                }

                Vector3 otherTangent = otherAnchor.road.EvaluateSplineWorldTangent(otherAnchor.splineIndex, otherAnchor.curveU);
                otherTangent.y = 0f;
                if (otherTangent.sqrMagnitude <= 1e-5f)
                {
                    continue;
                }

                otherTangent.Normalize();
                float dot = Mathf.Abs(Vector3.Dot(anchorTangent, otherTangent));
                float sin = Mathf.Sqrt(Mathf.Clamp01(1f - dot * dot));
                sin = Mathf.Max(ImplicitJunctionMinCrossSin, sin);
                cutbackDistance = Mathf.Max(cutbackDistance, otherAnchor.width * 0.5f / sin);
            }

            return cutbackDistance + ImplicitJunctionCutbackMargin;
        }

        private static bool TryGetRoadData(LoftRoadBehaviour road, int splineIndex, out LoftRoadExtensionData roadData)
        {
            roadData = null;
            return road != null
                && road.RoadExtensionDatas != null
                && splineIndex >= 0
                && splineIndex < road.RoadExtensionDatas.Count
                && (roadData = road.RoadExtensionDatas[splineIndex]) != null;
        }

        private static float GetJunctionConnectionRange(
            LoftRoadExtensionData roadData,
            float rangeScale)
        {
            if (roadData == null)
            {
                return 0f;
            }

            float roadWidth = Mathf.Max(0f, roadData.WidthValue);
            float sidewalkWidth = Mathf.Max(0f, roadData.SidewalkWidthValue);
            return Mathf.Max(0.001f, (roadWidth + sidewalkWidth) * Mathf.Max(0.001f, rangeScale));
        }

        private static bool ShouldConnectSamplePoints(
            Vector3 point,
            LoftRoadExtensionData roadData,
            Vector3 otherPoint,
            LoftRoadExtensionData otherRoadData,
            float rangeScale)
        {
            if (roadData == null || otherRoadData == null)
            {
                return false;
            }

            Vector3 dir = otherPoint - point;
            if (Mathf.Abs(dir.y) >= IntersectionHeightThreshold)
            {
                return false;
            }

            float connectionRange = Mathf.Max(
                GetJunctionConnectionRange(roadData, rangeScale),
                GetJunctionConnectionRange(otherRoadData, rangeScale));

            return dir.magnitude < connectionRange;
        }

        private static Vector3 ToVector3(float3 position)
        {
            return new Vector3(position.x, position.y, position.z);
        }

        private static SplineArcProfile GetOrBuildArcProfile(
            CandidateAnchor anchor,
            Dictionary<RoadSplineHandle, SplineArcProfile> cache)
        {
            RoadSplineHandle handle = anchor.Handle;
            if (cache.TryGetValue(handle, out SplineArcProfile profile))
            {
                return profile;
            }

            profile = new SplineArcProfile();
            float estimatedLength = EstimateSplineLength(anchor.road, anchor.splineIndex, 96);
            int resolution = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(estimatedLength, 1f) / 0.5f), 32, 512);
            Vector3 previous = anchor.road.EvaluateSplineWorldPosition(anchor.splineIndex, 0f);
            float accumulatedLength = 0f;
            profile.curveUs.Add(0f);
            profile.cumulativeLengths.Add(0f);

            for (int stepIndex = 1; stepIndex <= resolution; stepIndex++)
            {
                float curveU = stepIndex / (float)resolution;
                Vector3 current = anchor.road.EvaluateSplineWorldPosition(anchor.splineIndex, curveU);
                accumulatedLength += Vector3.Distance(previous, current);
                profile.curveUs.Add(curveU);
                profile.cumulativeLengths.Add(accumulatedLength);
                previous = current;
            }

            profile.totalLength = accumulatedLength;
            cache.Add(handle, profile);
            return profile;
        }

        private static float EvaluateArcDistanceAtCurveU(SplineArcProfile profile, float curveU)
        {
            if (profile == null || profile.curveUs.Count == 0)
            {
                return 0f;
            }

            curveU = Mathf.Clamp01(curveU);
            if (curveU <= 0f)
            {
                return 0f;
            }

            if (curveU >= 1f)
            {
                return profile.totalLength;
            }

            int upperIndex = 1;
            while (upperIndex < profile.curveUs.Count && profile.curveUs[upperIndex] < curveU)
            {
                upperIndex++;
            }

            if (upperIndex >= profile.curveUs.Count)
            {
                return profile.totalLength;
            }

            int lowerIndex = Mathf.Max(0, upperIndex - 1);
            float lowerCurveU = profile.curveUs[lowerIndex];
            float upperCurveU = profile.curveUs[upperIndex];
            float lerp = upperCurveU - lowerCurveU > 1e-5f
                ? Mathf.InverseLerp(lowerCurveU, upperCurveU, curveU)
                : 0f;
            return Mathf.Lerp(profile.cumulativeLengths[lowerIndex], profile.cumulativeLengths[upperIndex], lerp);
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

            if (upperIndex >= cumulativeLengths.Count)
            {
                return curveUs[curveUs.Count - 1];
            }

            int lowerIndex = Mathf.Max(0, upperIndex - 1);
            float lowerDistance = cumulativeLengths[lowerIndex];
            float upperDistance = cumulativeLengths[upperIndex];
            float lerp = upperDistance - lowerDistance > 1e-5f
                ? Mathf.InverseLerp(lowerDistance, upperDistance, targetDistance)
                : 0f;
            return Mathf.Lerp(curveUs[lowerIndex], curveUs[upperIndex], lerp);
        }

        private static float EvaluateCurveUAtArcDistance(SplineArcProfile profile, float targetDistance)
        {
            if (profile == null || profile.curveUs.Count == 0)
            {
                return 0f;
            }

            if (targetDistance <= 0f)
            {
                return profile.curveUs[0];
            }

            if (targetDistance >= profile.totalLength)
            {
                return profile.curveUs[profile.curveUs.Count - 1];
            }

            int upperIndex = 1;
            while (upperIndex < profile.cumulativeLengths.Count && profile.cumulativeLengths[upperIndex] < targetDistance)
            {
                upperIndex++;
            }

            if (upperIndex >= profile.cumulativeLengths.Count)
            {
                return profile.curveUs[profile.curveUs.Count - 1];
            }

            int lowerIndex = Mathf.Max(0, upperIndex - 1);
            float lowerDistance = profile.cumulativeLengths[lowerIndex];
            float upperDistance = profile.cumulativeLengths[upperIndex];
            float lerp = upperDistance - lowerDistance > 1e-5f
                ? Mathf.InverseLerp(lowerDistance, upperDistance, targetDistance)
                : 0f;
            return Mathf.Lerp(profile.curveUs[lowerIndex], profile.curveUs[upperIndex], lerp);
        }

        private static void DestroyImplicitJunctions(
            List<JunctionData> junctions,
            Dictionary<LoftRoadBehaviour, HashSet<int>> dirtyRoads)
        {
            if (junctions == null)
            {
                return;
            }

            foreach (JunctionData junction in junctions)
            {
                if (junction == null)
                {
                    continue;
                }

                if (junction.connectedRoads != null)
                {
                    foreach (JunctionData.ConnectedRoad connectedRoad in junction.connectedRoads)
                    {
                        if (connectedRoad?.roadBehaviour == null)
                        {
                            continue;
                        }

                        connectedRoad.roadBehaviour.RemoveJunctionMarkersForJunction(junction, connectedRoad.splineIndex);
                        connectedRoad.roadBehaviour.connectedJunctions.Remove(junction);
                        AddDirtyRoad(dirtyRoads, connectedRoad.roadBehaviour, connectedRoad.splineIndex);
                    }
                }

                Object.DestroyImmediate(junction.gameObject);
            }
        }

        private static void RefreshDirtyRoads(Dictionary<LoftRoadBehaviour, HashSet<int>> dirtyRoads)
        {
            if (dirtyRoads.Count == 0)
            {
                return;
            }

            using (LoftRoadBehaviour.SuppressConnectedJunctionUpdatesScope())
            {
                foreach (KeyValuePair<LoftRoadBehaviour, HashSet<int>> pair in dirtyRoads)
                {
                    LoftRoadBehaviour road = pair.Key;
                    if (road == null)
                    {
                        continue;
                    }

                    foreach (int splineIndex in pair.Value)
                    {
                        road.MarkSplineGeometryAndDownstreamDirty(splineIndex);
                    }

                    road.LoftAllRoads();
                }
            }
        }

        private static void AddDirtyRoad(
            Dictionary<LoftRoadBehaviour, HashSet<int>> dirtyRoads,
            LoftRoadBehaviour road,
            int splineIndex)
        {
            if (road == null)
            {
                return;
            }

            if (!dirtyRoads.TryGetValue(road, out HashSet<int> dirtySplineIndices))
            {
                dirtySplineIndices = new HashSet<int>();
                dirtyRoads.Add(road, dirtySplineIndices);
            }

            dirtySplineIndices.Add(splineIndex);
        }

        private static Transform GetOrCreateRoadDataRoot()
        {
            GameObject roadDataRoot = GameObject.Find(RoadDataRootName);
            if (roadDataRoot == null)
            {
                roadDataRoot = new GameObject(RoadDataRootName);
            }

            return roadDataRoot.transform;
        }

        private static float ResolveRoadWidth(LoftRoadBehaviour road, int splineIndex)
        {
            if (road?.RoadExtensionDatas == null || splineIndex < 0 || splineIndex >= road.RoadExtensionDatas.Count)
            {
                return 5f;
            }

            LoftRoadExtensionData roadExtensionData = road.RoadExtensionDatas[splineIndex];
            if (roadExtensionData == null)
            {
                return 5f;
            }

            return roadExtensionData.laneWidth.DefaultValue
                   * Mathf.Max(1, roadExtensionData.leftLaneCount + roadExtensionData.rightLaneCount);
        }

        private static float EstimateSplineLength(LoftRoadBehaviour road, int splineIndex, int steps)
        {
            Vector3 previous = road.EvaluateSplineWorldPosition(splineIndex, 0f);
            float total = 0f;
            for (int stepIndex = 1; stepIndex <= steps; stepIndex++)
            {
                float curveU = stepIndex / (float)steps;
                Vector3 current = road.EvaluateSplineWorldPosition(splineIndex, curveU);
                total += Vector3.Distance(previous, current);
                previous = current;
            }

            return total;
        }

        private static bool TryIntersectSegmentsXZ(
            Vector3 a0,
            Vector3 a1,
            Vector3 b0,
            Vector3 b1,
            out float tA,
            out float tB)
        {
            Vector2 p = new Vector2(a0.x, a0.z);
            Vector2 r = new Vector2(a1.x - a0.x, a1.z - a0.z);
            Vector2 q = new Vector2(b0.x, b0.z);
            Vector2 s = new Vector2(b1.x - b0.x, b1.z - b0.z);

            float denominator = Cross(r, s);
            if (Mathf.Abs(denominator) <= 1e-6f)
            {
                tA = 0f;
                tB = 0f;
                return false;
            }

            Vector2 qp = q - p;
            float rawTA = Cross(qp, s) / denominator;
            float rawTB = Cross(qp, r) / denominator;
            if (rawTA < -CurveUEpsilon || rawTA > 1f + CurveUEpsilon || rawTB < -CurveUEpsilon || rawTB > 1f + CurveUEpsilon)
            {
                tA = 0f;
                tB = 0f;
                return false;
            }

            tA = Mathf.Clamp01(rawTA);
            tB = Mathf.Clamp01(rawTB);
            return true;
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private static bool IsEndpointCurveU(float curveU)
        {
            return curveU <= CurveUEpsilon || curveU >= 1f - CurveUEpsilon;
        }
    }
}
#endif
