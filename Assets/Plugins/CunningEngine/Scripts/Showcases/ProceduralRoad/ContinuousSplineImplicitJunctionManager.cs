#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Splines.Extension;
using UnityEngine;
using UnityEngine.Splines;
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
            float estimatedLength = EstimateSplineLength(road, splineIndex, 48);
            int uniformSteps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(estimatedLength, width * 2f) / Mathf.Max(1f, width * 0.5f)), 12, 256);

            List<float> curveUs = new List<float>(uniformSteps + spline.Count + 2) { 0f, 1f };
            for (int knotIndex = 0; knotIndex < spline.Count; knotIndex++)
            {
                curveUs.Add(spline.Count <= 1 ? 0f : knotIndex / (float)(spline.Count - 1));
            }

            for (int stepIndex = 1; stepIndex < uniformSteps; stepIndex++)
            {
                curveUs.Add(stepIndex / (float)uniformSteps);
            }

            curveUs.Sort();

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
                    existingAnchor.curveU = (existingAnchor.curveU + incomingAnchor.curveU) * 0.5f;
                    existingAnchor.position = (existingAnchor.position + incomingAnchor.position) * 0.5f;
                    existingAnchor.width = Mathf.Max(existingAnchor.width, incomingAnchor.width);
                    continue;
                }

                candidate.anchors.Add(handle, new CandidateAnchor
                {
                    road = incomingAnchor.road,
                    splineIndex = incomingAnchor.splineIndex,
                    curveU = incomingAnchor.curveU,
                    position = incomingAnchor.position,
                    width = incomingAnchor.width,
                });
            }

            if (candidate.anchors.Count > 0)
            {
                Vector3 center = Vector3.zero;
                foreach (CandidateAnchor anchor in candidate.anchors.Values)
                {
                    center += anchor.position;
                }

                candidate.center = center / candidate.anchors.Count;
            }
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

            List<CandidateAnchor> orderedAnchors = candidate.anchors.Values
                .OrderBy(anchor => Mathf.Atan2(anchor.position.z - candidate.center.z, anchor.position.x - candidate.center.x))
                .ToList();

            junction.connectedRoads = new List<JunctionData.ConnectedRoad>(orderedAnchors.Count);
            HashSet<RoadSplineHandle> newHandles = new HashSet<RoadSplineHandle>();
            foreach (CandidateAnchor anchor in orderedAnchors)
            {
                long markerId = anchor.road.EnsureJunctionMarkerAtCurveU(anchor.splineIndex, anchor.curveU, junction);
                float cachedCurveU = anchor.curveU;
                int cachedPreferredKnotIndex = 0;
                if (anchor.road.TryGetRoadMarker(markerId, out RoadMarker marker))
                {
                    cachedCurveU = marker.curveU;
                    cachedPreferredKnotIndex = marker.preferredKnotIndex;
                }

                junction.connectedRoads.Add(new JunctionData.ConnectedRoad
                {
                    roadBehaviour = anchor.road,
                    splineIndex = anchor.splineIndex,
                    knotIndex = cachedPreferredKnotIndex,
                    markerId = markerId,
                    cachedCurveU = cachedCurveU,
                    cachedPreferredKnotIndex = cachedPreferredKnotIndex,
                    width = ResolveRoadWidth(anchor.road, anchor.splineIndex)
                });

                newHandles.Add(anchor.Handle);
                if (!anchor.road.connectedJunctions.Contains(junction))
                {
                    anchor.road.connectedJunctions.Add(junction);
                }

                AddDirtyRoad(dirtyRoads, anchor.road, anchor.splineIndex);
            }

            foreach (JunctionData.ConnectedRoad previousConnection in previousConnections)
            {
                if (previousConnection?.roadBehaviour == null)
                {
                    continue;
                }

                RoadSplineHandle handle = new RoadSplineHandle(previousConnection.roadBehaviour.StableRoadId, previousConnection.splineIndex);
                if (newHandles.Contains(handle))
                {
                    continue;
                }

                previousConnection.roadBehaviour.RemoveJunctionMarkersForJunction(junction, previousConnection.splineIndex);
                previousConnection.roadBehaviour.connectedJunctions.Remove(junction);
                AddDirtyRoad(dirtyRoads, previousConnection.roadBehaviour, previousConnection.splineIndex);
            }

            junction.transform.position = candidate.center;
            junction.SetImplicitAutoState(true, candidate.Signature);
            EditorUtility.SetDirty(junction);
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
