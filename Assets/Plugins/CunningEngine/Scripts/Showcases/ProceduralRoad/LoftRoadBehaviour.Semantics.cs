#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

namespace Unity.Splines.Examples
{
    public enum RoadMarkerKind
    {
        Junction = 0,
        ManualCut = 1,
    }

    public enum SegmentBoundaryKind
    {
        Endpoint = 0,
        Junction = 1,
        ManualCut = 2,
    }

    public enum RoadMarkerBoundaryRole
    {
        None = 0,
        LowerCurveU = 1,
        UpperCurveU = 2,
        Endpoint = 3,
    }

    [Serializable]
    public sealed class RoadMarker
    {
        public long markerId;
        public RoadMarkerKind kind;
        public int splineIndex;
        public int preferredKnotIndex;
        public float curveU;
        public float arcLength;
        public bool isPinnedToKnot;
        public JunctionData junctionRef;
        public bool isEndpointBoundary;
        public bool isGeneratedBoundaryControl;
        public RoadMarkerBoundaryRole boundaryRole;
    }

    [Serializable]
    public sealed class RoadBoundaryRef
    {
        public long markerId;
        public float curveU;
        public int preferredKnotIndex;
        public SegmentBoundaryKind kind;
        public bool isExplicitMarker;
        public bool isEndpointBoundary;
        public bool isPinnedToKnot;
        public JunctionData junctionRef;
    }

    [Serializable]
    public sealed class LogicalSegmentDef
    {
        public int segmentId;
        public int splineIndex;
        public long startMarkerId;
        public long endMarkerId;
        public float startCurveU;
        public float endCurveU;
        public SegmentBoundaryKind startBoundaryKind;
        public SegmentBoundaryKind endBoundaryKind;
        public int localSegmentIndex;

        public bool IsValid => endCurveU > startCurveU;
    }

    [Serializable]
    public sealed class SplineSemanticCache
    {
        public int splineIndex;
        public ulong semanticFingerprint;
        public readonly List<RoadMarker> markers = new List<RoadMarker>();
        public readonly List<RoadBoundaryRef> boundaries = new List<RoadBoundaryRef>();
        public readonly List<LogicalSegmentDef> segments = new List<LogicalSegmentDef>();

        public void Clear()
        {
            markers.Clear();
            boundaries.Clear();
            segments.Clear();
            semanticFingerprint = 0UL;
        }
    }

    [Serializable]
    public readonly struct SegmentSlotKey : IEquatable<SegmentSlotKey>
    {
        public readonly int splineIndex;
        public readonly int localSegmentIndex;

        public SegmentSlotKey(int splineIndex, int localSegmentIndex)
        {
            this.splineIndex = splineIndex;
            this.localSegmentIndex = localSegmentIndex;
        }

        public bool Equals(SegmentSlotKey other)
        {
            return splineIndex == other.splineIndex && localSegmentIndex == other.localSegmentIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is SegmentSlotKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (splineIndex * 397) ^ localSegmentIndex;
            }
        }
    }

    public partial class LoftRoadBehaviour
    {
        private const float SegmentCurveUEpsilon = 0.0001f;

        [SerializeField]
        [HideInInspector]
        private long m_RoadId;

        [SerializeField]
        [HideInInspector]
        private List<RoadMarker> m_RoadMarkers = new List<RoadMarker>();

        [NonSerialized]
        private readonly List<SplineSemanticCache> m_SplineSemanticCaches = new List<SplineSemanticCache>();

        [NonSerialized]
        private int m_CurrentProcessingLocalSegmentIndex = -1;

        [NonSerialized]
        private int m_CurrentProcessingSegmentId;

        [NonSerialized]
        private float m_CurrentProcessingSegmentStartCurveU;

        [NonSerialized]
        private float m_CurrentProcessingSegmentEndCurveU = 1f;

        [NonSerialized]
        private float m_CurrentProcessingSegmentStartArcLength;

        [NonSerialized]
        private float m_CurrentProcessingSegmentEndArcLength;

        internal int PrimitiveRoadId => FoldStableIdToInt(m_RoadId);

        internal long StableRoadId
        {
            get
            {
                EnsureRoadId();
                return m_RoadId;
            }
        }

        public IReadOnlyList<RoadMarker> RoadMarkers => m_RoadMarkers;

        internal IReadOnlyList<LogicalSegmentDef> GetLogicalSegments(int splineIndex)
        {
            EnsureSplineSemanticCacheCapacity();
            RebuildSplineSemanticCache(splineIndex);
            if (splineIndex < 0 || splineIndex >= m_SplineSemanticCaches.Count)
            {
                return Array.Empty<LogicalSegmentDef>();
            }

            return m_SplineSemanticCaches[splineIndex].segments;
        }

        internal bool TryGetRoadMarker(long markerId, out RoadMarker marker)
        {
            marker = null;
            if (markerId == 0 || m_RoadMarkers == null)
            {
                return false;
            }

            for (int index = 0; index < m_RoadMarkers.Count; index++)
            {
                RoadMarker candidate = m_RoadMarkers[index];
                if (candidate != null && candidate.markerId == markerId)
                {
                    marker = candidate;
                    return true;
                }
            }

            return false;
        }

        internal int ResolveMarkerPreferredKnotIndex(RoadMarker marker, Spline spline)
        {
            if (spline == null || spline.Count <= 0)
            {
                return -1;
            }

            if (marker == null)
            {
                return 0;
            }

            if (marker.isEndpointBoundary)
            {
                return marker.curveU >= 0.5f ? spline.Count - 1 : 0;
            }

            if (marker.isPinnedToKnot)
            {
                return Mathf.Clamp(marker.preferredKnotIndex, 0, spline.Count - 1);
            }

            return ResolveKnotIndexAtCurveU(spline, marker.curveU, Mathf.Clamp(marker.preferredKnotIndex, 0, spline.Count - 1));
        }

        internal bool TryResolveConnectedRoadBinding(
            long markerId,
            int splineIndex,
            int fallbackKnotIndex,
            float fallbackCurveU,
            int fallbackPreferredKnotIndex,
            out Spline spline,
            out RoadMarker marker,
            out float curveU,
            out int preferredKnotIndex)
        {
            spline = null;
            marker = null;
            curveU = Mathf.Clamp01(fallbackCurveU);
            preferredKnotIndex = fallbackPreferredKnotIndex;

            if (Container == null || Container.Splines == null || splineIndex < 0 || splineIndex >= Container.Splines.Count)
            {
                return false;
            }

            spline = Container.Splines[splineIndex];
            if (spline == null || spline.Count <= 0)
            {
                return false;
            }

            if (TryGetRoadMarker(markerId, out marker))
            {
                curveU = Mathf.Clamp01(marker.curveU);
                preferredKnotIndex = ResolveMarkerPreferredKnotIndex(marker, spline);
                return true;
            }

            int clampedFallbackKnot = Mathf.Clamp(fallbackKnotIndex, 0, spline.Count - 1);
            if (preferredKnotIndex < 0 || preferredKnotIndex >= spline.Count)
            {
                preferredKnotIndex = clampedFallbackKnot;
            }

            curveU = ComputeKnotCurveU(spline, preferredKnotIndex);
            return true;
        }

        internal Vector3 EvaluateSplineWorldPosition(int splineIndex, float curveU)
        {
            if (Container == null || Container.Splines == null || splineIndex < 0 || splineIndex >= Container.Splines.Count)
            {
                return transform.position;
            }

            Spline spline = Container.Splines[splineIndex];
            if (spline == null || spline.Count <= 0)
            {
                return transform.position;
            }

            return transform.TransformPoint(spline.EvaluatePosition(Mathf.Clamp01(curveU)));
        }

        internal Vector3 EvaluateSplineWorldTangent(int splineIndex, float curveU)
        {
            if (Container == null || Container.Splines == null || splineIndex < 0 || splineIndex >= Container.Splines.Count)
            {
                return transform.forward;
            }

            Spline spline = Container.Splines[splineIndex];
            if (spline == null || spline.Count <= 1)
            {
                return transform.forward;
            }

            Vector3 tangent = transform.TransformDirection(spline.EvaluateTangent(Mathf.Clamp01(curveU)));
            if (tangent.sqrMagnitude <= Mathf.Epsilon)
            {
                float delta = 0.001f;
                float clampedU = Mathf.Clamp01(curveU);
                float back = Mathf.Max(0f, clampedU - delta);
                float forward = Mathf.Min(1f, clampedU + delta);
                tangent = EvaluateSplineWorldPosition(splineIndex, forward) - EvaluateSplineWorldPosition(splineIndex, back);
            }

            tangent.y = 0f;
            return tangent.sqrMagnitude > Mathf.Epsilon ? tangent.normalized : transform.forward;
        }

        internal long EnsureJunctionMarker(int splineIndex, int knotIndex, JunctionData junction)
        {
            if (LoftSplines == null || splineIndex < 0 || splineIndex >= LoftSplines.Count)
            {
                return 0;
            }

            Spline spline = LoftSplines[splineIndex];
            if (spline == null || spline.Count <= 0)
            {
                return 0;
            }

            knotIndex = Mathf.Clamp(knotIndex, 0, spline.Count - 1);
            float curveU = ComputeKnotCurveU(spline, knotIndex);
            bool isEndpoint = knotIndex == 0 || knotIndex == spline.Count - 1;
            return EnsureJunctionMarkerInternal(
                splineIndex,
                knotIndex,
                curveU,
                true,
                isEndpoint,
                junction,
                false,
                isEndpoint ? RoadMarkerBoundaryRole.Endpoint : RoadMarkerBoundaryRole.None);
        }

        internal long EnsureJunctionMarkerAtCurveU(
            int splineIndex,
            float curveU,
            JunctionData junction,
            bool isGeneratedBoundaryControl = false,
            RoadMarkerBoundaryRole boundaryRole = RoadMarkerBoundaryRole.None)
        {
            if (LoftSplines == null || splineIndex < 0 || splineIndex >= LoftSplines.Count)
            {
                return 0;
            }

            Spline spline = LoftSplines[splineIndex];
            if (spline == null || spline.Count <= 0)
            {
                return 0;
            }

            float clampedCurveU = Mathf.Clamp01(curveU);
            int preferredKnotIndex = ResolveFallbackKnotIndexForCurveU(spline, clampedCurveU);
            bool isEndpoint = clampedCurveU <= SegmentCurveUEpsilon || clampedCurveU >= 1f - SegmentCurveUEpsilon;
            bool isPinnedToKnot = isEndpoint;
            if (isPinnedToKnot)
            {
                preferredKnotIndex = clampedCurveU >= 0.5f ? spline.Count - 1 : 0;
                clampedCurveU = ComputeKnotCurveU(spline, preferredKnotIndex);
            }

            return EnsureJunctionMarkerInternal(
                splineIndex,
                preferredKnotIndex,
                clampedCurveU,
                isPinnedToKnot,
                isEndpoint,
                junction,
                isGeneratedBoundaryControl,
                ResolveMarkerBoundaryRole(isEndpoint, boundaryRole, clampedCurveU));
        }

        internal long EnsureJunctionMarkerAtWorldPosition(
            int splineIndex,
            Vector3 worldPosition,
            float fallbackCurveU,
            JunctionData junction,
            bool requirePinnedKnot = false,
            bool isGeneratedBoundaryControl = false,
            RoadMarkerBoundaryRole boundaryRole = RoadMarkerBoundaryRole.None)
        {
            if (LoftSplines == null || splineIndex < 0 || splineIndex >= LoftSplines.Count)
            {
                return 0;
            }

            Spline spline = LoftSplines[splineIndex];
            if (spline == null || spline.Count <= 0)
            {
                return 0;
            }

            if (TryResolveBoundaryKnotAtWorldPosition(splineIndex, worldPosition, out int knotIndex))
            {
                knotIndex = Mathf.Clamp(knotIndex, 0, spline.Count - 1);
                bool isEndpoint = knotIndex == 0 || knotIndex == spline.Count - 1;
                return EnsureJunctionMarkerInternal(
                    splineIndex,
                    knotIndex,
                    ComputeKnotCurveU(spline, knotIndex),
                    true,
                    isEndpoint,
                    junction,
                    isGeneratedBoundaryControl,
                    ResolveMarkerBoundaryRole(isEndpoint, boundaryRole, ComputeKnotCurveU(spline, knotIndex)));
            }

            if (requirePinnedKnot)
            {
                Vector3 fallbackWorldPosition = transform.TransformPoint(spline.EvaluatePosition(Mathf.Clamp01(fallbackCurveU)));
                if (TryResolveBoundaryKnotAtWorldPosition(splineIndex, fallbackWorldPosition, out int fallbackKnotIndex))
                {
                    fallbackKnotIndex = Mathf.Clamp(fallbackKnotIndex, 0, spline.Count - 1);
                    bool isEndpoint = fallbackKnotIndex == 0 || fallbackKnotIndex == spline.Count - 1;
                    return EnsureJunctionMarkerInternal(
                        splineIndex,
                        fallbackKnotIndex,
                        ComputeKnotCurveU(spline, fallbackKnotIndex),
                        true,
                        isEndpoint,
                        junction,
                        isGeneratedBoundaryControl,
                        ResolveMarkerBoundaryRole(isEndpoint, boundaryRole, ComputeKnotCurveU(spline, fallbackKnotIndex)));
                }

                return 0;
            }

            return EnsureJunctionMarkerAtCurveU(splineIndex, fallbackCurveU, junction, isGeneratedBoundaryControl, boundaryRole);
        }

        internal bool TryResolveSplineTargetAtCurveU(
            int splineIndex,
            float resolvedCurveU,
            out int existingKnotIndex,
            out int curveIndex,
            out float localT)
        {
            existingKnotIndex = -1;
            curveIndex = -1;
            localT = -1f;
            if (LoftSplines == null || splineIndex < 0 || splineIndex >= LoftSplines.Count)
            {
                return false;
            }

            Spline spline = LoftSplines[splineIndex];
            return spline != null && TryResolveSplineTargetAtCurveU(spline, resolvedCurveU, out existingKnotIndex, out curveIndex, out localT);
        }

        internal long EnsureJunctionMarkerAtResolvedSplineTarget(
            int splineIndex,
            float resolvedCurveU,
            int resolvedCurveIndex,
            float resolvedLocalT,
            JunctionData junction,
            bool isGeneratedBoundaryControl = false,
            RoadMarkerBoundaryRole boundaryRole = RoadMarkerBoundaryRole.None)
        {
            if (LoftSplines == null || splineIndex < 0 || splineIndex >= LoftSplines.Count)
            {
                return 0;
            }

            Spline spline = LoftSplines[splineIndex];
            if (spline == null || spline.Count <= 0)
            {
                return 0;
            }

            if (!TryResolveBoundaryKnotAtCurveU(splineIndex, resolvedCurveU, resolvedCurveIndex, resolvedLocalT, out int knotIndex))
            {
                return 0;
            }

            knotIndex = Mathf.Clamp(knotIndex, 0, spline.Count - 1);
            bool isEndpoint = knotIndex == 0 || knotIndex == spline.Count - 1;
            float knotCurveU = ComputeKnotCurveU(spline, knotIndex);
            return EnsureJunctionMarkerInternal(
                splineIndex,
                knotIndex,
                knotCurveU,
                true,
                isEndpoint,
                junction,
                isGeneratedBoundaryControl,
                ResolveMarkerBoundaryRole(isEndpoint, boundaryRole, knotCurveU));
        }

        internal void RemoveJunctionMarkersForJunction(JunctionData junction, int splineIndex = -1)
        {
            if (junction == null || m_RoadMarkers == null || m_RoadMarkers.Count == 0)
            {
                return;
            }

            HashSet<int> dirtySplineIndices = null;
            for (int markerIndex = m_RoadMarkers.Count - 1; markerIndex >= 0; markerIndex--)
            {
                RoadMarker marker = m_RoadMarkers[markerIndex];
                if (marker == null
                    || marker.kind != RoadMarkerKind.Junction
                    || marker.junctionRef != junction
                    || (splineIndex >= 0 && marker.splineIndex != splineIndex))
                {
                    continue;
                }

                dirtySplineIndices ??= new HashSet<int>();
                dirtySplineIndices.Add(marker.splineIndex);
                m_RoadMarkers.RemoveAt(markerIndex);
            }

            if (dirtySplineIndices == null || dirtySplineIndices.Count == 0)
            {
                return;
            }

            foreach (int dirtySplineIndex in dirtySplineIndices)
            {
                RebuildSplineSemanticCache(dirtySplineIndex);
            }

            EditorUtility.SetDirty(this);
        }

        internal void RemoveUnusedJunctionMarkersForJunction(
            JunctionData junction,
            int splineIndex,
            IReadOnlyCollection<long> usedMarkerIds)
        {
            if (junction == null || splineIndex < 0 || m_RoadMarkers == null || m_RoadMarkers.Count == 0)
            {
                return;
            }

            bool removedAny = false;
            for (int markerIndex = m_RoadMarkers.Count - 1; markerIndex >= 0; markerIndex--)
            {
                RoadMarker marker = m_RoadMarkers[markerIndex];
                if (marker == null
                    || marker.kind != RoadMarkerKind.Junction
                    || marker.junctionRef != junction
                    || marker.splineIndex != splineIndex)
                {
                    continue;
                }

                bool isUsedMarker = false;
                if (usedMarkerIds != null)
                {
                    foreach (long usedMarkerId in usedMarkerIds)
                    {
                        if (usedMarkerId != marker.markerId)
                        {
                            continue;
                        }

                        isUsedMarker = true;
                        break;
                    }
                }

                if (isUsedMarker)
                {
                    continue;
                }

                m_RoadMarkers.RemoveAt(markerIndex);
                removedAny = true;
            }

            if (!removedAny)
            {
                return;
            }

            RebuildSplineSemanticCache(splineIndex);
            EditorUtility.SetDirty(this);
        }

        private long EnsureJunctionMarkerInternal(
            int splineIndex,
            int preferredKnotIndex,
            float curveU,
            bool isPinnedToKnot,
            bool isEndpoint,
            JunctionData junction,
            bool isGeneratedBoundaryControl,
            RoadMarkerBoundaryRole boundaryRole)
        {
            if (LoftSplines == null || splineIndex < 0 || splineIndex >= LoftSplines.Count)
            {
                return 0;
            }

            Spline spline = LoftSplines[splineIndex];
            if (spline == null || spline.Count <= 0)
            {
                return 0;
            }

            EnsureRoadId();
            preferredKnotIndex = Mathf.Clamp(preferredKnotIndex, 0, spline.Count - 1);
            curveU = isPinnedToKnot ? ComputeKnotCurveU(spline, preferredKnotIndex) : Mathf.Clamp01(curveU);
            boundaryRole = ResolveMarkerBoundaryRole(isEndpoint, boundaryRole, curveU);

            RoadMarker marker = null;
            for (int index = 0; index < m_RoadMarkers.Count; index++)
            {
                RoadMarker candidate = m_RoadMarkers[index];
                if (candidate == null || candidate.kind != RoadMarkerKind.Junction || candidate.splineIndex != splineIndex)
                {
                    continue;
                }

                if (candidate.junctionRef == junction)
                {
                    if (candidate.isGeneratedBoundaryControl != isGeneratedBoundaryControl)
                    {
                        continue;
                    }

                    if (isGeneratedBoundaryControl)
                    {
                        bool sameBoundaryRole = candidate.boundaryRole == boundaryRole;
                        if (sameBoundaryRole
                            && (boundaryRole != RoadMarkerBoundaryRole.Endpoint
                                || (candidate.curveU >= 0.5f) == (curveU >= 0.5f)))
                        {
                            marker = candidate;
                            break;
                        }

                        continue;
                    }

                    bool sameEndpointSide = candidate.isEndpointBoundary
                        && isEndpoint
                        && (candidate.curveU >= 0.5f) == (curveU >= 0.5f);
                    if ((isPinnedToKnot && candidate.isPinnedToKnot && candidate.preferredKnotIndex == preferredKnotIndex)
                        || sameEndpointSide
                        || Mathf.Abs(candidate.curveU - curveU) <= SegmentCurveUEpsilon)
                    {
                        marker = candidate;
                        break;
                    }

                    continue;
                }

                if (candidate.junctionRef != null)
                {
                    continue;
                }

                if (candidate.isGeneratedBoundaryControl != isGeneratedBoundaryControl)
                {
                    continue;
                }

                if (isPinnedToKnot && candidate.isPinnedToKnot && candidate.preferredKnotIndex == preferredKnotIndex)
                {
                    marker = candidate;
                    break;
                }

                if (Mathf.Abs(candidate.curveU - curveU) <= SegmentCurveUEpsilon)
                {
                    marker = candidate;
                    break;
                }
            }

            if (marker == null)
            {
                marker = new RoadMarker
                {
                    markerId = AllocateMarkerId(),
                    kind = RoadMarkerKind.Junction,
                    splineIndex = splineIndex,
                };
                m_RoadMarkers.Add(marker);
            }

            marker.kind = RoadMarkerKind.Junction;
            marker.splineIndex = splineIndex;
            marker.preferredKnotIndex = isPinnedToKnot
                ? preferredKnotIndex
                : ResolveKnotIndexAtCurveU(spline, curveU, preferredKnotIndex);
            marker.curveU = curveU;
            marker.arcLength = EstimateSplineArcLengthAt(spline, curveU);
            marker.isPinnedToKnot = isPinnedToKnot;
            marker.junctionRef = junction;
            marker.isEndpointBoundary = isEndpoint;
            marker.isGeneratedBoundaryControl = isGeneratedBoundaryControl;
            marker.boundaryRole = boundaryRole;

            RebuildSplineSemanticCache(splineIndex);
            EditorUtility.SetDirty(this);
            return marker.markerId;
        }

        private static RoadMarkerBoundaryRole ResolveMarkerBoundaryRole(
            bool isEndpoint,
            RoadMarkerBoundaryRole requestedRole,
            float curveU)
        {
            if (requestedRole == RoadMarkerBoundaryRole.LowerCurveU
                || requestedRole == RoadMarkerBoundaryRole.UpperCurveU)
            {
                return requestedRole;
            }

            if (isEndpoint)
            {
                return RoadMarkerBoundaryRole.Endpoint;
            }

            if (requestedRole == RoadMarkerBoundaryRole.Endpoint)
            {
                return curveU >= 0.5f
                    ? RoadMarkerBoundaryRole.UpperCurveU
                    : RoadMarkerBoundaryRole.LowerCurveU;
            }

            return requestedRole;
        }

        internal bool IsJunctionInteriorSegment(LogicalSegmentDef segmentDef)
        {
            if (segmentDef == null || !segmentDef.IsValid)
            {
                return false;
            }

            if (!TryGetRoadMarker(segmentDef.startMarkerId, out RoadMarker startMarker)
                || !TryGetRoadMarker(segmentDef.endMarkerId, out RoadMarker endMarker))
            {
                return false;
            }

            return startMarker.kind == RoadMarkerKind.Junction
                && endMarker.kind == RoadMarkerKind.Junction
                && startMarker.junctionRef != null
                && startMarker.junctionRef == endMarker.junctionRef;
        }

        internal void EnsureRoadId()
        {
            if (m_RoadId != 0)
            {
                return;
            }

            long generated = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0);
            if (generated == 0)
            {
                generated = DateTime.UtcNow.Ticks;
            }

            m_RoadId = generated;
            EditorUtility.SetDirty(this);
        }

        internal void EnsureAllJunctionMarkersMigrated()
        {
            JunctionData[] allJunctions = UnityEngine.Object.FindObjectsByType<JunctionData>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int junctionIndex = 0; junctionIndex < allJunctions.Length; junctionIndex++)
            {
                JunctionData junction = allJunctions[junctionIndex];
                if (junction == null || junction.connectedRoads == null)
                {
                    continue;
                }

                for (int roadIndex = 0; roadIndex < junction.connectedRoads.Count; roadIndex++)
                {
                    JunctionData.ConnectedRoad connectedRoad = junction.connectedRoads[roadIndex];
                    if (connectedRoad == null || connectedRoad.roadBehaviour != this)
                    {
                        continue;
                    }

                    if (connectedRoad.markerId == 0 && connectedRoad.splineIndex >= 0 && connectedRoad.knotIndex >= 0)
                    {
                        long markerId = EnsureJunctionMarker(connectedRoad.splineIndex, connectedRoad.knotIndex, junction);
                        connectedRoad.markerId = markerId;
                        connectedRoad.cachedCurveU = TryGetRoadMarker(markerId, out RoadMarker marker) ? marker.curveU : connectedRoad.cachedCurveU;
                        connectedRoad.cachedPreferredKnotIndex = TryGetRoadMarker(markerId, out marker) ? marker.preferredKnotIndex : connectedRoad.knotIndex;
                    }

                    if (connectedRoad.markerId != 0 && !connectedJunctions.Contains(junction))
                    {
                        connectedJunctions.Add(junction);
                    }
                }
            }
        }

        internal void RemapRoadMarkersForRemovedSpline(int removedSplineIndex)
        {
            if (m_RoadMarkers == null || m_RoadMarkers.Count == 0)
            {
                return;
            }

            bool changed = false;
            for (int markerIndex = m_RoadMarkers.Count - 1; markerIndex >= 0; markerIndex--)
            {
                RoadMarker marker = m_RoadMarkers[markerIndex];
                if (marker == null)
                {
                    m_RoadMarkers.RemoveAt(markerIndex);
                    changed = true;
                    continue;
                }

                if (marker.splineIndex == removedSplineIndex)
                {
                    m_RoadMarkers.RemoveAt(markerIndex);
                    changed = true;
                    continue;
                }

                if (marker.splineIndex > removedSplineIndex)
                {
                    marker.splineIndex--;
                    changed = true;
                }
            }

            if (changed)
            {
                EditorUtility.SetDirty(this);
            }

            RebuildAllSplineSemanticCaches();
        }

        internal void RemapRoadMarkersForReorderedSpline(int previousIndex, int newIndex)
        {
            if (m_RoadMarkers == null || m_RoadMarkers.Count == 0 || previousIndex == newIndex)
            {
                return;
            }

            for (int markerIndex = 0; markerIndex < m_RoadMarkers.Count; markerIndex++)
            {
                RoadMarker marker = m_RoadMarkers[markerIndex];
                if (marker == null)
                {
                    continue;
                }

                if (marker.splineIndex == previousIndex)
                {
                    marker.splineIndex = newIndex;
                }
                else if (previousIndex < newIndex && marker.splineIndex > previousIndex && marker.splineIndex <= newIndex)
                {
                    marker.splineIndex--;
                }
                else if (previousIndex > newIndex && marker.splineIndex >= newIndex && marker.splineIndex < previousIndex)
                {
                    marker.splineIndex++;
                }
            }

            EditorUtility.SetDirty(this);
            RebuildAllSplineSemanticCaches();
        }

        internal void SynchronizeConnectedRoadSplineIndicesFromMarkers()
        {
            if (connectedJunctions == null)
            {
                return;
            }

            for (int junctionIndex = 0; junctionIndex < connectedJunctions.Count; junctionIndex++)
            {
                JunctionData junction = connectedJunctions[junctionIndex];
                if (junction == null || junction.connectedRoads == null)
                {
                    continue;
                }

                bool junctionDirty = false;
                for (int roadIndex = 0; roadIndex < junction.connectedRoads.Count; roadIndex++)
                {
                    JunctionData.ConnectedRoad connectedRoad = junction.connectedRoads[roadIndex];
                    if (connectedRoad == null || connectedRoad.roadBehaviour != this || connectedRoad.markerId == 0)
                    {
                        continue;
                    }

                    if (!TryGetRoadMarker(connectedRoad.markerId, out RoadMarker marker))
                    {
                        continue;
                    }

                    if (connectedRoad.splineIndex != marker.splineIndex)
                    {
                        connectedRoad.splineIndex = marker.splineIndex;
                        junctionDirty = true;
                    }
                }

                if (junctionDirty)
                {
                    EditorUtility.SetDirty(junction);
                    MarkJunctionDirty(junction);
                }
            }
        }

        internal void RebindRoadMarkersAfterSplineChange(int splineIndex)
        {
            if (LoftSplines == null || splineIndex < 0 || splineIndex >= LoftSplines.Count)
            {
                return;
            }

            Spline spline = LoftSplines[splineIndex];
            if (spline == null || spline.Count <= 0 || m_RoadMarkers == null)
            {
                return;
            }

            bool changed = false;
            for (int markerIndex = 0; markerIndex < m_RoadMarkers.Count; markerIndex++)
            {
                RoadMarker marker = m_RoadMarkers[markerIndex];
                if (marker == null || marker.splineIndex != splineIndex)
                {
                    continue;
                }

                if (marker.isEndpointBoundary)
                {
                    marker.preferredKnotIndex = marker.curveU >= 0.5f ? spline.Count - 1 : 0;
                    marker.curveU = ComputeKnotCurveU(spline, marker.preferredKnotIndex);
                    marker.isPinnedToKnot = true;
                    marker.arcLength = EstimateSplineArcLengthAt(spline, marker.curveU);
                    changed = true;
                    continue;
                }

                if (marker.isPinnedToKnot)
                {
                    int clampedKnotIndex = Mathf.Clamp(marker.preferredKnotIndex, 0, spline.Count - 1);
                    if (clampedKnotIndex != marker.preferredKnotIndex)
                    {
                        marker.preferredKnotIndex = clampedKnotIndex;
                        changed = true;
                    }

                    float curveU = ComputeKnotCurveU(spline, marker.preferredKnotIndex);
                    if (!Mathf.Approximately(marker.curveU, curveU))
                    {
                        marker.curveU = curveU;
                        changed = true;
                    }
                }
                else
                {
                    float clampedCurveU = Mathf.Clamp01(marker.curveU);
                    if (!Mathf.Approximately(marker.curveU, clampedCurveU))
                    {
                        marker.curveU = clampedCurveU;
                        changed = true;
                    }

                    int resolvedKnotIndex = ResolveMarkerPreferredKnotIndex(marker, spline);
                    if (resolvedKnotIndex != marker.preferredKnotIndex)
                    {
                        marker.preferredKnotIndex = resolvedKnotIndex;
                        changed = true;
                    }
                }

                float arcLength = EstimateSplineArcLengthAt(spline, marker.curveU);
                if (!Mathf.Approximately(marker.arcLength, arcLength))
                {
                    marker.arcLength = arcLength;
                    changed = true;
                }
            }

            RebuildSplineSemanticCache(splineIndex);
            if (changed)
            {
                EditorUtility.SetDirty(this);
            }
        }

        internal void PropagateMarkerChangesToConnectedJunctions(int splineIndex)
        {
            if (connectedJunctions == null || connectedJunctions.Count == 0)
            {
                return;
            }

            bool roadDirty = false;
            for (int junctionIndex = 0; junctionIndex < connectedJunctions.Count; junctionIndex++)
            {
                JunctionData junction = connectedJunctions[junctionIndex];
                if (junction == null || junction.connectedRoads == null)
                {
                    continue;
                }

                bool junctionDirty = false;
                for (int roadIndex = 0; roadIndex < junction.connectedRoads.Count; roadIndex++)
                {
                    JunctionData.ConnectedRoad connectedRoad = junction.connectedRoads[roadIndex];
                    if (connectedRoad == null || connectedRoad.roadBehaviour != this || connectedRoad.splineIndex != splineIndex)
                    {
                        continue;
                    }

                    if (!TryGetRoadMarker(connectedRoad.markerId, out RoadMarker marker))
                    {
                        continue;
                    }

                    int preferredKnotIndex = marker.preferredKnotIndex;
                    if (connectedRoad.cachedPreferredKnotIndex != preferredKnotIndex || !Mathf.Approximately(connectedRoad.cachedCurveU, marker.curveU))
                    {
                        connectedRoad.cachedPreferredKnotIndex = preferredKnotIndex;
                        connectedRoad.cachedCurveU = marker.curveU;
                        connectedRoad.knotIndex = preferredKnotIndex;
                        junctionDirty = true;
                        roadDirty = true;
                    }
                }

                if (junctionDirty)
                {
                    MarkJunctionDirty(junction);
                    EditorUtility.SetDirty(junction);
                }
            }

            if (roadDirty)
            {
                EditorUtility.SetDirty(this);
            }
        }

        internal void RebuildAllSplineSemanticCaches()
        {
            EnsureSplineSemanticCacheCapacity();
            int splineCount = LoftSplines != null ? LoftSplines.Count : 0;
            for (int splineIndex = 0; splineIndex < splineCount; splineIndex++)
            {
                RebuildSplineSemanticCache(splineIndex);
            }
        }

        private void EnsureSplineSemanticCacheCapacity()
        {
            int splineCount = LoftSplines != null ? LoftSplines.Count : 0;
            while (m_SplineSemanticCaches.Count < splineCount)
            {
                m_SplineSemanticCaches.Add(new SplineSemanticCache());
            }

            if (m_SplineSemanticCaches.Count > splineCount)
            {
                m_SplineSemanticCaches.RemoveRange(splineCount, m_SplineSemanticCaches.Count - splineCount);
            }
        }

        private void RebuildSplineSemanticCache(int splineIndex)
        {
            EnsureSplineSemanticCacheCapacity();
            if (splineIndex < 0 || splineIndex >= m_SplineSemanticCaches.Count)
            {
                return;
            }

            SplineSemanticCache cache = m_SplineSemanticCaches[splineIndex];
            cache.Clear();
            cache.splineIndex = splineIndex;

            if (LoftSplines == null || splineIndex >= LoftSplines.Count)
            {
                return;
            }

            Spline spline = LoftSplines[splineIndex];
            if (spline == null || spline.Count <= 0)
            {
                return;
            }

            List<RoadMarker> explicitMarkers = new List<RoadMarker>();
            for (int markerIndex = 0; markerIndex < m_RoadMarkers.Count; markerIndex++)
            {
                RoadMarker marker = m_RoadMarkers[markerIndex];
                if (marker != null && marker.splineIndex == splineIndex)
                {
                    explicitMarkers.Add(marker);
                    cache.markers.Add(marker);
                }
            }

            explicitMarkers.Sort((a, b) => a.curveU.CompareTo(b.curveU));
            BuildSplineBoundaryRefs(cache, explicitMarkers, spline);
            BuildLogicalSegments(cache);
            cache.semanticFingerprint = ComputeSemanticFingerprint(cache);
        }

        private void BuildSplineBoundaryRefs(SplineSemanticCache cache, List<RoadMarker> explicitMarkers, Spline spline)
        {
            RoadMarker startMarker = FindEndpointMarker(explicitMarkers, 0f);
            cache.boundaries.Add(CreateBoundaryRef(startMarker, SegmentBoundaryKind.Endpoint, 0f, 0, startMarker != null));

            for (int markerIndex = 0; markerIndex < explicitMarkers.Count; markerIndex++)
            {
                RoadMarker marker = explicitMarkers[markerIndex];
                float markerCurveU = Mathf.Clamp01(marker.curveU);
                if (markerCurveU <= SegmentCurveUEpsilon || markerCurveU >= 1f - SegmentCurveUEpsilon)
                {
                    continue;
                }

                cache.boundaries.Add(CreateBoundaryRef(
                    marker,
                    ToBoundaryKind(marker),
                    markerCurveU,
                    ResolveMarkerPreferredKnotIndex(marker, spline),
                    true));
            }

            RoadMarker endMarker = FindEndpointMarker(explicitMarkers, 1f);
            cache.boundaries.Add(CreateBoundaryRef(endMarker, SegmentBoundaryKind.Endpoint, 1f, spline.Count - 1, endMarker != null));
            cache.boundaries.Sort((a, b) => a.curveU.CompareTo(b.curveU));
            DeduplicateBoundaries(cache.boundaries);
        }

        private void BuildLogicalSegments(SplineSemanticCache cache)
        {
            cache.segments.Clear();
            for (int boundaryIndex = 0; boundaryIndex + 1 < cache.boundaries.Count; boundaryIndex++)
            {
                RoadBoundaryRef start = cache.boundaries[boundaryIndex];
                RoadBoundaryRef end = cache.boundaries[boundaryIndex + 1];
                float startCurveU = Mathf.Clamp01(start.curveU);
                float endCurveU = Mathf.Clamp01(end.curveU);
                if (endCurveU - startCurveU <= SegmentCurveUEpsilon)
                {
                    continue;
                }

                cache.segments.Add(new LogicalSegmentDef
                {
                    splineIndex = cache.splineIndex,
                    localSegmentIndex = cache.segments.Count,
                    startMarkerId = start.markerId,
                    endMarkerId = end.markerId,
                    startCurveU = startCurveU,
                    endCurveU = endCurveU,
                    startBoundaryKind = start.kind,
                    endBoundaryKind = end.kind,
                    segmentId = ComputeLogicalSegmentId(cache.splineIndex, start, end),
                });
            }

            if (cache.segments.Count == 0)
            {
                cache.segments.Add(new LogicalSegmentDef
                {
                    splineIndex = cache.splineIndex,
                    localSegmentIndex = 0,
                    startCurveU = 0f,
                    endCurveU = 1f,
                    startBoundaryKind = SegmentBoundaryKind.Endpoint,
                    endBoundaryKind = SegmentBoundaryKind.Endpoint,
                    segmentId = ComputeLogicalSegmentId(
                        cache.splineIndex,
                        new RoadBoundaryRef { curveU = 0f, preferredKnotIndex = 0, kind = SegmentBoundaryKind.Endpoint },
                        new RoadBoundaryRef { curveU = 1f, preferredKnotIndex = 1, kind = SegmentBoundaryKind.Endpoint }),
                });
            }
        }

        private static RoadBoundaryRef CreateBoundaryRef(
            RoadMarker marker,
            SegmentBoundaryKind fallbackKind,
            float fallbackCurveU,
            int fallbackPreferredKnotIndex,
            bool hasExplicitMarker)
        {
            if (marker == null)
            {
                return new RoadBoundaryRef
                {
                    markerId = 0,
                    curveU = Mathf.Clamp01(fallbackCurveU),
                    preferredKnotIndex = fallbackPreferredKnotIndex,
                    kind = fallbackKind,
                    isExplicitMarker = false,
                    isEndpointBoundary = fallbackKind == SegmentBoundaryKind.Endpoint,
                    isPinnedToKnot = true,
                    junctionRef = null,
                };
            }

            return new RoadBoundaryRef
            {
                markerId = marker.markerId,
                curveU = Mathf.Clamp01(marker.curveU),
                preferredKnotIndex = marker.preferredKnotIndex,
                kind = ToBoundaryKind(marker),
                isExplicitMarker = hasExplicitMarker,
                isEndpointBoundary = marker.isEndpointBoundary,
                isPinnedToKnot = marker.isPinnedToKnot,
                junctionRef = marker.junctionRef,
            };
        }

        private static SegmentBoundaryKind ToBoundaryKind(RoadMarker marker)
        {
            if (marker == null)
            {
                return SegmentBoundaryKind.Endpoint;
            }

            switch (marker.kind)
            {
                case RoadMarkerKind.Junction:
                    return SegmentBoundaryKind.Junction;
                case RoadMarkerKind.ManualCut:
                    return SegmentBoundaryKind.ManualCut;
                default:
                    return SegmentBoundaryKind.Endpoint;
            }
        }

        private static RoadMarker FindEndpointMarker(List<RoadMarker> markers, float endpointCurveU)
        {
            if (markers == null)
            {
                return null;
            }

            for (int index = 0; index < markers.Count; index++)
            {
                RoadMarker marker = markers[index];
                if (marker == null)
                {
                    continue;
                }

                if (marker.isEndpointBoundary && Mathf.Abs(marker.curveU - endpointCurveU) <= SegmentCurveUEpsilon)
                {
                    return marker;
                }
            }

            return null;
        }

        private static void DeduplicateBoundaries(List<RoadBoundaryRef> boundaries)
        {
            if (boundaries == null || boundaries.Count <= 1)
            {
                return;
            }

            for (int index = boundaries.Count - 2; index >= 0; index--)
            {
                RoadBoundaryRef current = boundaries[index];
                RoadBoundaryRef next = boundaries[index + 1];
                if (Mathf.Abs(current.curveU - next.curveU) > SegmentCurveUEpsilon)
                {
                    continue;
                }

                bool keepNext = !current.isExplicitMarker && next.isExplicitMarker;
                boundaries.RemoveAt(keepNext ? index : index + 1);
            }
        }

        private ulong ComputeSemanticFingerprint(SplineSemanticCache cache)
        {
            ulong hash = HashInit();
            hash = HashAdd(hash, cache.splineIndex);
            hash = HashAdd(hash, cache.boundaries.Count);
            for (int index = 0; index < cache.boundaries.Count; index++)
            {
                RoadBoundaryRef boundary = cache.boundaries[index];
                hash = HashAdd(hash, (int)(boundary.markerId & 0x7fffffff));
                hash = HashAdd(hash, boundary.curveU);
                hash = HashAdd(hash, boundary.preferredKnotIndex);
                hash = HashAdd(hash, (int)boundary.kind);
            }

            return hash;
        }

        private int ComputeLogicalSegmentId(int splineIndex, RoadBoundaryRef start, RoadBoundaryRef end)
        {
            ulong hash = HashInit();
            hash = HashAdd(hash, PrimitiveRoadId);
            hash = HashAdd(hash, splineIndex);
            hash = HashAdd(hash, (int)(start.markerId & 0x7fffffff));
            hash = HashAdd(hash, (int)(end.markerId & 0x7fffffff));
            hash = HashAdd(hash, start.curveU);
            hash = HashAdd(hash, end.curveU);
            hash = HashAdd(hash, (int)start.kind);
            hash = HashAdd(hash, (int)end.kind);
            return unchecked((int)(hash ^ (hash >> 32)));
        }

        private static int FoldStableIdToInt(long id)
        {
            return unchecked((int)(id ^ (id >> 32)));
        }

        private long AllocateMarkerId()
        {
            long nextId = 1;
            for (int index = 0; index < m_RoadMarkers.Count; index++)
            {
                RoadMarker marker = m_RoadMarkers[index];
                if (marker != null && marker.markerId >= nextId)
                {
                    nextId = marker.markerId + 1;
                }
            }

            return nextId;
        }

        private bool TryResolveBoundaryKnotAtWorldPosition(int splineIndex, Vector3 worldPosition, out int knotIndex)
        {
            knotIndex = -1;
            if (LoftSplines == null || splineIndex < 0 || splineIndex >= LoftSplines.Count)
            {
                return false;
            }

            Spline spline = LoftSplines[splineIndex];
            if (spline == null || spline.Count <= 0)
            {
                return false;
            }

            if (!TryResolveSplinePointTarget(spline, worldPosition, out int existingKnotIndex, out int curveIndex, out float localT))
            {
                return false;
            }

            if (existingKnotIndex >= 0)
            {
                knotIndex = existingKnotIndex;
                return true;
            }

            if (Container != null)
            {
                Undo.RecordObject(Container, "Insert Implicit Junction Boundary Knot");
            }

            int insertedKnotIndex = InsertKnotOnCurveSegment(spline, curveIndex, localT);
            if (insertedKnotIndex < 0 || insertedKnotIndex >= spline.Count)
            {
                return false;
            }

            if (Container != null)
            {
                EditorUtility.SetDirty(Container);
            }

            knotIndex = insertedKnotIndex;
            return true;
        }

        private bool TryResolveBoundaryKnotAtCurveU(
            int splineIndex,
            float resolvedCurveU,
            int resolvedCurveIndex,
            float resolvedLocalT,
            out int knotIndex)
        {
            knotIndex = -1;
            if (LoftSplines == null || splineIndex < 0 || splineIndex >= LoftSplines.Count)
            {
                return false;
            }

            Spline spline = LoftSplines[splineIndex];
            if (spline == null || spline.Count <= 0)
            {
                return false;
            }

            if (!TryResolveSplineTargetAtCurveU(
                    spline,
                    resolvedCurveU,
                    resolvedCurveIndex,
                    resolvedLocalT,
                    out int existingKnotIndex,
                    out int curveIndex,
                    out float localT))
            {
                return false;
            }

            if (existingKnotIndex >= 0)
            {
                knotIndex = existingKnotIndex;
                return true;
            }

            if (Container != null)
            {
                Undo.RecordObject(Container, "Insert Implicit Junction Boundary Knot");
            }

            int insertedKnotIndex = InsertKnotOnCurveSegment(spline, curveIndex, localT);
            if (insertedKnotIndex < 0 || insertedKnotIndex >= spline.Count)
            {
                return false;
            }

            if (Container != null)
            {
                EditorUtility.SetDirty(Container);
            }

            knotIndex = insertedKnotIndex;
            return true;
        }

        private bool TryResolveSplinePointTarget(
            Spline spline,
            Vector3 worldPosition,
            out int existingKnotIndex,
            out int curveIndex,
            out float localT)
        {
            existingKnotIndex = -1;
            curveIndex = -1;
            localT = -1f;

            if (spline == null || spline.Count == 0)
            {
                return false;
            }

            if (spline.Count == 1)
            {
                existingKnotIndex = 0;
                curveIndex = 0;
                localT = 0f;
                return true;
            }

            float3 targetLocalPosition = transform.InverseTransformPoint(worldPosition);
            SplineUtility.GetNearestPoint(
                spline,
                targetLocalPosition,
                out _,
                out float splineT,
                SplineUtility.PickResolutionMax,
                4);

            curveIndex = spline.SplineToCurveT(splineT, out float resolvedLocalT);
            if (curveIndex < 0)
            {
                return false;
            }

            localT = Mathf.Clamp01(resolvedLocalT);
            if (localT <= 0.0001f)
            {
                existingKnotIndex = curveIndex;
                localT = 0f;
                return true;
            }

            if (localT >= 0.9999f)
            {
                existingKnotIndex = curveIndex + 1;
                localT = 1f;
                return true;
            }

            return true;
        }

        private static bool TryResolveSplineTargetAtCurveU(
            Spline spline,
            float resolvedCurveU,
            out int existingKnotIndex,
            out int curveIndex,
            out float localT)
        {
            return TryResolveSplineTargetAtCurveU(
                spline,
                resolvedCurveU,
                -1,
                -1f,
                out existingKnotIndex,
                out curveIndex,
                out localT);
        }

        private static bool TryResolveSplineTargetAtCurveU(
            Spline spline,
            float resolvedCurveU,
            int preferredCurveIndex,
            float preferredLocalT,
            out int existingKnotIndex,
            out int curveIndex,
            out float localT)
        {
            existingKnotIndex = -1;
            curveIndex = -1;
            localT = -1f;
            if (spline == null || spline.Count == 0)
            {
                return false;
            }

            if (spline.Count == 1)
            {
                existingKnotIndex = 0;
                curveIndex = 0;
                localT = 0f;
                return true;
            }

            float clampedCurveU = Mathf.Clamp01(resolvedCurveU);
            for (int knotIndex = 0; knotIndex < spline.Count; knotIndex++)
            {
                float knotCurveU = ComputeKnotCurveU(spline, knotIndex);
                if (Mathf.Abs(knotCurveU - clampedCurveU) > SegmentCurveUEpsilon)
                {
                    continue;
                }

                existingKnotIndex = knotIndex;
                curveIndex = knotIndex >= spline.Count - 1 ? spline.Count - 2 : knotIndex;
                localT = knotIndex >= spline.Count - 1 ? 1f : 0f;
                return true;
            }

            curveIndex = preferredCurveIndex;
            localT = preferredLocalT;
            if (curveIndex < 0 || curveIndex >= spline.Count - 1 || localT < 0f || localT > 1f)
            {
                curveIndex = spline.SplineToCurveT(clampedCurveU, out float resolvedLocalT);
                localT = resolvedLocalT;
            }

            if (curveIndex < 0)
            {
                return false;
            }

            curveIndex = Mathf.Clamp(curveIndex, 0, spline.Count - 2);
            localT = Mathf.Clamp01(localT);
            if (localT <= BoundaryKnotEpsilon)
            {
                existingKnotIndex = curveIndex;
                localT = 0f;
                return true;
            }

            if (localT >= 1f - BoundaryKnotEpsilon)
            {
                existingKnotIndex = curveIndex + 1;
                localT = 1f;
                return true;
            }

            return true;
        }

        private static int InsertKnotOnCurveSegment(Spline spline, int curveIndex, float curveT)
        {
            if (spline == null || spline.Count < 2)
            {
                return -1;
            }

            int insertIndex = Mathf.Clamp(curveIndex + 1, 1, spline.Count - 1);
            float localT = Mathf.Clamp01(curveT);
            if (localT <= 0.0001f)
            {
                return insertIndex - 1;
            }

            if (localT >= 0.9999f)
            {
                return insertIndex;
            }

            int previousIndex = insertIndex - 1;
            BezierKnot previous = spline[previousIndex];
            BezierKnot next = spline[insertIndex];
            BezierCurve curve = new BezierCurve(previous, next);
            CurveUtility.Split(curve, localT, out BezierCurve leftCurve, out BezierCurve rightCurve);

            if (spline.GetTangentMode(previousIndex) == TangentMode.Mirrored)
            {
                spline.SetTangentMode(previousIndex, TangentMode.Continuous);
            }

            if (spline.GetTangentMode(insertIndex) == TangentMode.Mirrored)
            {
                spline.SetTangentMode(insertIndex, TangentMode.Continuous);
            }

            if (IsTangentModeEditable(spline.GetTangentMode(previousIndex)))
            {
                previous.TangentOut = math.mul(math.inverse(previous.Rotation), leftCurve.Tangent0);
            }

            if (IsTangentModeEditable(spline.GetTangentMode(insertIndex)))
            {
                next.TangentIn = math.mul(math.inverse(next.Rotation), rightCurve.Tangent1);
            }

            spline.SetKnotNoNotify(previousIndex, previous);
            spline.SetKnotNoNotify(insertIndex, next);

            float3 up = EvaluateUpVectorLocal(
                curve,
                localT,
                math.rotate(previous.Rotation, math.up()),
                math.rotate(next.Rotation, math.up()));
            quaternion rotation = quaternion.LookRotationSafe(math.normalizesafe(rightCurve.Tangent0), up);
            quaternion inverseRotation = math.inverse(rotation);
            BezierKnot newKnot = new BezierKnot(
                leftCurve.P3,
                math.mul(inverseRotation, leftCurve.Tangent1),
                math.mul(inverseRotation, rightCurve.Tangent0),
                rotation);

            spline.Insert(insertIndex, newKnot, TangentMode.Broken);
            return insertIndex;
        }

        private const int BoundaryNormalsPerCurve = 16;
        private const float BoundaryKnotEpsilon = 0.0001f;

        private struct FrenetFrame
        {
            public float3 origin;
            public float3 tangent;
            public float3 normal;
            public float3 binormal;
        }

        private static bool IsTangentModeEditable(TangentMode tangentMode)
        {
            return tangentMode == TangentMode.Broken
                || tangentMode == TangentMode.Continuous
                || tangentMode == TangentMode.Mirrored;
        }

        private static bool Approximately(float a, float b)
        {
            return math.abs(b - a) < math.max(0.000001f * math.max(math.abs(a), math.abs(b)), BoundaryKnotEpsilon * 8);
        }

        private static float3 GetExplicitLinearTangent(float3 point, float3 to)
        {
            return (to - point) / 3f;
        }

        private static FrenetFrame GetNextRotationMinimizingFrame(BezierCurve curve, FrenetFrame previousFrame, float nextT)
        {
            FrenetFrame nextFrame;
            nextFrame.origin = CurveUtility.EvaluatePosition(curve, nextT);
            nextFrame.tangent = CurveUtility.EvaluateTangent(curve, nextT);

            float3 toCurrentFrame = nextFrame.origin - previousFrame.origin;
            float c1 = math.dot(toCurrentFrame, toCurrentFrame);
            float3 riL = previousFrame.binormal - toCurrentFrame * 2f / c1 * math.dot(toCurrentFrame, previousFrame.binormal);
            float3 tiL = previousFrame.tangent - toCurrentFrame * 2f / c1 * math.dot(toCurrentFrame, previousFrame.tangent);

            float3 v2 = nextFrame.tangent - tiL;
            float c2 = math.dot(v2, v2);

            nextFrame.binormal = math.normalize(riL - v2 * 2f / c2 * math.dot(v2, riL));
            nextFrame.normal = math.normalize(math.cross(nextFrame.binormal, nextFrame.tangent));
            return nextFrame;
        }

        private static float3 EvaluateUpVectorLocal(BezierCurve curve, float t, float3 startUp, float3 endUp)
        {
            float linearTangentLen = math.length(GetExplicitLinearTangent(curve.P0, curve.P3));
            float3 linearTangentOut = math.normalize(curve.P3 - curve.P0) * linearTangentLen;
            if (Approximately(math.length(curve.P1 - curve.P0), 0f))
            {
                curve.P1 = curve.P0 + linearTangentOut;
            }

            if (Approximately(math.length(curve.P2 - curve.P3), 0f))
            {
                curve.P2 = curve.P3 - linearTangentOut;
            }

            float3[] normalBuffer = new float3[BoundaryNormalsPerCurve];

            FrenetFrame frame;
            frame.origin = curve.P0;
            frame.tangent = curve.P1 - curve.P0;
            frame.normal = startUp;
            frame.binormal = math.normalize(math.cross(frame.tangent, frame.normal));
            if (float.IsNaN(frame.binormal.x))
            {
                return float3.zero;
            }

            normalBuffer[0] = frame.normal;

            float stepSize = 1f / (BoundaryNormalsPerCurve - 1);
            float currentT = stepSize;
            float prevT = 0f;
            float3 upVector = float3.zero;
            for (int index = 1; index < BoundaryNormalsPerCurve; ++index)
            {
                FrenetFrame prevFrame = frame;
                frame = GetNextRotationMinimizingFrame(curve, prevFrame, currentT);
                normalBuffer[index] = frame.normal;

                if (prevT <= t && currentT >= t)
                {
                    float lerpT = (t - prevT) / stepSize;
                    upVector = (float3)Vector3.Slerp(prevFrame.normal, frame.normal, lerpT);
                }

                prevT = currentT;
                currentT += stepSize;
            }

            if (prevT <= t && currentT >= t)
            {
                upVector = endUp;
            }

            float3 lastFrameNormal = normalBuffer[BoundaryNormalsPerCurve - 1];
            float angleBetweenNormals = math.acos(math.clamp(math.dot(lastFrameNormal, endUp), -1f, 1f));
            if (angleBetweenNormals == 0f)
            {
                return upVector;
            }

            float3 lastNormalTangent = math.normalize(frame.tangent);
            quaternion positiveRotation = quaternion.AxisAngle(lastNormalTangent, angleBetweenNormals);
            quaternion negativeRotation = quaternion.AxisAngle(lastNormalTangent, -angleBetweenNormals);
            float positiveRotationResult = math.acos(math.clamp(math.dot(math.rotate(positiveRotation, endUp), lastFrameNormal), -1f, 1f));
            float negativeRotationResult = math.acos(math.clamp(math.dot(math.rotate(negativeRotation, endUp), lastFrameNormal), -1f, 1f));
            if (positiveRotationResult > negativeRotationResult)
            {
                angleBetweenNormals *= -1f;
            }

            currentT = stepSize;
            prevT = 0f;
            for (int index = 1; index < normalBuffer.Length; index++)
            {
                float3 normal = normalBuffer[index];
                float adjustmentAngle = math.lerp(0f, angleBetweenNormals, currentT);
                float3 tangent = math.normalize(CurveUtility.EvaluateTangent(curve, currentT));
                float3 adjustedNormal = math.rotate(quaternion.AxisAngle(tangent, -adjustmentAngle), normal);
                normalBuffer[index] = adjustedNormal;

                if (prevT <= t && currentT >= t)
                {
                    float lerpT = (t - prevT) / stepSize;
                    upVector = (float3)Vector3.Slerp(normalBuffer[index - 1], normalBuffer[index], lerpT);
                    return upVector;
                }

                prevT = currentT;
                currentT += stepSize;
            }

            return endUp;
        }

        internal static float ComputeKnotCurveU(Spline spline, int knotIndex)
        {
            if (spline == null || spline.Count <= 1)
            {
                return 0f;
            }

            knotIndex = Mathf.Clamp(knotIndex, 0, spline.Count - 1);
            return Mathf.Clamp01(SplineUtility.GetNormalizedInterpolation(spline, knotIndex, PathIndexUnit.Knot));
        }

        private static int ResolveFallbackKnotIndexForCurveU(Spline spline, float curveU)
        {
            if (spline == null || spline.Count <= 0)
            {
                return 0;
            }

            if (TryResolveSplineTargetAtCurveU(spline, curveU, out int existingKnotIndex, out int curveIndex, out float localT))
            {
                if (existingKnotIndex >= 0)
                {
                    return existingKnotIndex;
                }

                int leftIndex = Mathf.Clamp(curveIndex, 0, spline.Count - 1);
                int rightIndex = Mathf.Clamp(curveIndex + 1, 0, spline.Count - 1);
                return localT >= 0.5f ? rightIndex : leftIndex;
            }

            return Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(curveU) * Mathf.Max(1, spline.Count - 1)), 0, spline.Count - 1);
        }

        private static float EstimateSplineArcLengthAt(Spline spline, float targetCurveU)
        {
            if (spline == null || spline.Count <= 1)
            {
                return 0f;
            }

            targetCurveU = Mathf.Clamp01(targetCurveU);
            const int resolution = 32;
            Vector3 previous = spline.EvaluatePosition(0f);
            float total = 0f;
            for (int stepIndex = 1; stepIndex <= resolution; stepIndex++)
            {
                float curveU = targetCurveU * (stepIndex / (float)resolution);
                Vector3 current = spline.EvaluatePosition(curveU);
                total += Vector3.Distance(previous, current);
                previous = current;
            }

            return total;
        }
    }
}
#endif
