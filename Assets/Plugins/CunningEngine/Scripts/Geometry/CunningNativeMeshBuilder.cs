using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace CunningEngine {
    internal static class CunningNativeMeshBuilder {
        const string UnityMaterialIndexAttr = "@unity_material_index";
        const uint PrimitiveAttrClass = 3;
        const uint RenderFlagTopologyDirty = 1u << 0;
        const uint RenderFlagVertexBufferDirty = 1u << 1;
        const uint RenderFlagMaterialDirty = 1u << 2;
        const uint RenderFlagLineBufferDirty = 1u << 3;

        static readonly VertexAttributeDescriptor[] s_vertexLayout = {
            new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
            new(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 0),
            new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 0),
        };

        static bool s_renderStateApiAvailable = true;
        static bool s_renderStateApiWarned;

        internal sealed class MeshBuildData {
            internal int[][] solidSubmeshIndices = Array.Empty<int[]>();
            internal int[] solidMaterialSlots = Array.Empty<int>();
            internal int[] lines = Array.Empty<int>();
        }

        internal sealed class MeshRuntimeCache {
            internal bool initialized;
            internal NativeMethods.GeoRenderState lastRenderState;
            internal NativeMethods.GeoRenderVertex[] renderVertices = Array.Empty<NativeMethods.GeoRenderVertex>();
            internal NativeMethods.GeoRenderVertex[] scratchVertices = Array.Empty<NativeMethods.GeoRenderVertex>();
            internal int[] lines = Array.Empty<int>();
            internal int[] vertexOffsets = Array.Empty<int>();
            internal int[] vertexIndices = Array.Empty<int>();
            internal int[] primitiveMaterialIndices = Array.Empty<int>();
            internal int[][] solidSubmeshIndices = Array.Empty<int[]>();
            internal int[] solidMaterialSlots = Array.Empty<int>();

            internal void Reset() {
                initialized = false;
                lastRenderState = default;
                renderVertices = Array.Empty<NativeMethods.GeoRenderVertex>();
                scratchVertices = Array.Empty<NativeMethods.GeoRenderVertex>();
                lines = Array.Empty<int>();
                vertexOffsets = Array.Empty<int>();
                vertexIndices = Array.Empty<int>();
                primitiveMaterialIndices = Array.Empty<int>();
                solidSubmeshIndices = Array.Empty<int[]>();
                solidMaterialSlots = Array.Empty<int>();
            }
        }

        internal static bool TryFillMeshFromHandle(
            ulong handle,
            Mesh targetMesh,
            string meshName,
            bool includeLines,
            out MeshBuildData buildData
        ) {
            return TryFillMeshFromHandle(handle, targetMesh, meshName, includeLines, runtimeCache: null, out buildData);
        }

        internal static bool TryFillMeshFromHandle(
            ulong handle,
            Mesh targetMesh,
            string meshName,
            bool includeLines,
            MeshRuntimeCache runtimeCache,
            out MeshBuildData buildData
        ) {
            buildData = new MeshBuildData();
            if (handle == 0 || targetMesh == null) {
                return false;
            }

            if (runtimeCache != null && s_renderStateApiAvailable && TryGetRenderState(handle, out var renderState) && renderState.vertex_count > 0) {
                if (!runtimeCache.initialized
                    || (renderState.flags & RenderFlagTopologyDirty) != 0
                    || runtimeCache.lastRenderState.topology_version != renderState.topology_version
                    || runtimeCache.renderVertices.Length != (int)renderState.vertex_count) {
                    return TryRebuildFull(handle, targetMesh, meshName, includeLines, runtimeCache, renderState, out buildData);
                }

                return TryApplyIncremental(handle, targetMesh, includeLines, runtimeCache, renderState, out buildData);
            }

            return TryFillMeshFromHandleLegacy(handle, targetMesh, meshName, includeLines, out buildData);
        }

        static bool TryApplyIncremental(
            ulong handle,
            Mesh targetMesh,
            bool includeLines,
            MeshRuntimeCache runtimeCache,
            NativeMethods.GeoRenderState renderState,
            out MeshBuildData buildData
        ) {
            buildData = BuildDataFromCache(runtimeCache, includeLines);

            if ((renderState.flags & RenderFlagVertexBufferDirty) != 0 && renderState.dirty_vertex_count > 0) {
                int start = (int)renderState.dirty_vertex_start;
                int count = (int)renderState.dirty_vertex_count;
                EnsureVertexScratch(runtimeCache, count);
                if (!TryCopyRenderVerticesRange(handle, start, count, runtimeCache.scratchVertices)) {
                    return TryRebuildFull(handle, targetMesh, targetMesh.name, includeLines, runtimeCache, renderState, out buildData);
                }

                CunningUnityCoordinates.ConvertRenderVertices(runtimeCache.scratchVertices, count);
                Array.Copy(runtimeCache.scratchVertices, 0, runtimeCache.renderVertices, start, count);
                targetMesh.SetVertexBufferData(
                    runtimeCache.scratchVertices,
                    0,
                    start,
                    count,
                    0,
                    MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontValidateIndices);
            }

            if ((renderState.flags & RenderFlagMaterialDirty) != 0) {
                runtimeCache.primitiveMaterialIndices = ReadPrimitiveMaterialIndices(handle, (int)renderState.prim_count);
                RebuildSolidSubmeshes(runtimeCache);
                ApplyIndices(targetMesh, runtimeCache, includeLines);
            }

            if (includeLines && (renderState.flags & RenderFlagLineBufferDirty) != 0) {
                runtimeCache.lines = TryReadU32Array(handle, NativeMethods.cunning_geo_copy_lines) ?? Array.Empty<int>();
                ApplyIndices(targetMesh, runtimeCache, includeLines);
            }

            targetMesh.RecalculateBounds();
            runtimeCache.lastRenderState = renderState;
            buildData = BuildDataFromCache(runtimeCache, includeLines);
            return true;
        }

        static bool TryRebuildFull(
            ulong handle,
            Mesh targetMesh,
            string meshName,
            bool includeLines,
            MeshRuntimeCache runtimeCache,
            NativeMethods.GeoRenderState renderState,
            out MeshBuildData buildData
        ) {
            buildData = new MeshBuildData();
            int vertexCount = (int)renderState.vertex_count;
            if (vertexCount <= 0) {
                return false;
            }

            EnsureVertexCapacity(runtimeCache, vertexCount);
            if (!TryCopyRenderVertices(handle, runtimeCache.renderVertices)) {
                return false;
            }
            CunningUnityCoordinates.ConvertRenderVertices(runtimeCache.renderVertices, vertexCount);

            runtimeCache.vertexOffsets = TryReadU32Array(handle, NativeMethods.cunning_geo_copy_prim_vertex_offsets) ?? Array.Empty<int>();
            runtimeCache.vertexIndices = TryReadU32Array(handle, NativeMethods.cunning_geo_copy_prim_vertex_indices) ?? Array.Empty<int>();
            runtimeCache.primitiveMaterialIndices = ReadPrimitiveMaterialIndices(handle, (int)renderState.prim_count);
            runtimeCache.lines = includeLines ? (TryReadU32Array(handle, NativeMethods.cunning_geo_copy_lines) ?? Array.Empty<int>()) : Array.Empty<int>();
            RebuildSolidSubmeshes(runtimeCache);

            targetMesh.Clear();
            targetMesh.name = meshName;
            if (vertexCount > 65000) {
                targetMesh.indexFormat = IndexFormat.UInt32;
            }
            targetMesh.SetVertexBufferParams(vertexCount, s_vertexLayout);
            targetMesh.SetVertexBufferData(
                runtimeCache.renderVertices,
                0,
                0,
                vertexCount,
                0,
                MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontValidateIndices);
            ApplyIndices(targetMesh, runtimeCache, includeLines);
            targetMesh.RecalculateBounds();

            runtimeCache.initialized = true;
            runtimeCache.lastRenderState = renderState;
            buildData = BuildDataFromCache(runtimeCache, includeLines);
            return true;
        }

        static void ApplyIndices(Mesh targetMesh, MeshRuntimeCache runtimeCache, bool includeLines) {
            int solidSubmeshCount = Mathf.Max(1, runtimeCache.solidSubmeshIndices.Length);
            targetMesh.subMeshCount = includeLines ? solidSubmeshCount + 1 : solidSubmeshCount;
            for (int submeshIndex = 0; submeshIndex < solidSubmeshCount; submeshIndex++) {
                var indices = submeshIndex < runtimeCache.solidSubmeshIndices.Length
                    ? runtimeCache.solidSubmeshIndices[submeshIndex] ?? Array.Empty<int>()
                    : Array.Empty<int>();
                targetMesh.SetIndices(indices, MeshTopology.Triangles, submeshIndex, false);
            }

            if (includeLines) {
                targetMesh.SetIndices(
                    (runtimeCache.lines.Length >= 2 && (runtimeCache.lines.Length % 2) == 0) ? runtimeCache.lines : Array.Empty<int>(),
                    MeshTopology.Lines,
                    solidSubmeshCount,
                    false);
            }
        }

        static MeshBuildData BuildDataFromCache(MeshRuntimeCache runtimeCache, bool includeLines) {
            return new MeshBuildData {
                solidSubmeshIndices = runtimeCache.solidSubmeshIndices ?? Array.Empty<int[]>(),
                solidMaterialSlots = runtimeCache.solidMaterialSlots ?? Array.Empty<int>(),
                lines = includeLines ? (runtimeCache.lines ?? Array.Empty<int>()) : Array.Empty<int>(),
            };
        }

        static void RebuildSolidSubmeshes(MeshRuntimeCache runtimeCache) {
            if (runtimeCache.vertexOffsets == null
                || runtimeCache.vertexIndices == null
                || runtimeCache.vertexOffsets.Length == 0
                || runtimeCache.vertexOffsets.Length != runtimeCache.primitiveMaterialIndices.Length + 1) {
                runtimeCache.solidSubmeshIndices = new[] { Array.Empty<int>() };
                runtimeCache.solidMaterialSlots = new[] { 0 };
                return;
            }

            var grouped = new SortedDictionary<int, List<int>>();
            for (int primIndex = 0; primIndex < runtimeCache.primitiveMaterialIndices.Length; primIndex++) {
                int start = runtimeCache.vertexOffsets[primIndex];
                int end = runtimeCache.vertexOffsets[primIndex + 1];
                int count = end - start;
                if (count < 3) {
                    continue;
                }

                int materialSlot = runtimeCache.primitiveMaterialIndices[primIndex];
                if (!grouped.TryGetValue(materialSlot, out var triangles)) {
                    triangles = new List<int>(count * 3);
                    grouped.Add(materialSlot, triangles);
                }

                int v0 = runtimeCache.vertexIndices[start];
                for (int index = start + 1; index < end - 1; index++) {
                    CunningUnityCoordinates.AddTriangle(
                        triangles,
                        v0,
                        runtimeCache.vertexIndices[index],
                        runtimeCache.vertexIndices[index + 1]);
                }
            }

            if (grouped.Count == 0) {
                runtimeCache.solidSubmeshIndices = new[] { Array.Empty<int>() };
                runtimeCache.solidMaterialSlots = new[] { 0 };
                return;
            }

            runtimeCache.solidSubmeshIndices = new int[grouped.Count][];
            runtimeCache.solidMaterialSlots = new int[grouped.Count];
            int slot = 0;
            foreach (var pair in grouped) {
                runtimeCache.solidMaterialSlots[slot] = pair.Key;
                runtimeCache.solidSubmeshIndices[slot] = pair.Value.ToArray();
                slot++;
            }
        }

        static void EnsureVertexCapacity(MeshRuntimeCache runtimeCache, int vertexCount) {
            if (runtimeCache.renderVertices == null || runtimeCache.renderVertices.Length != vertexCount) {
                runtimeCache.renderVertices = new NativeMethods.GeoRenderVertex[vertexCount];
            }
        }

        static void EnsureVertexScratch(MeshRuntimeCache runtimeCache, int vertexCount) {
            if (runtimeCache.scratchVertices == null || runtimeCache.scratchVertices.Length < vertexCount) {
                runtimeCache.scratchVertices = new NativeMethods.GeoRenderVertex[vertexCount];
            }
        }

        static bool TryGetRenderState(ulong handle, out NativeMethods.GeoRenderState renderState) {
            renderState = default;
            if (!s_renderStateApiAvailable || handle == 0) {
                return false;
            }

            try {
                return NativeMethods.cunning_geo_get_render_state(handle, out renderState) != 0;
            } catch (EntryPointNotFoundException) {
                s_renderStateApiAvailable = false;
                if (!s_renderStateApiWarned) {
                    s_renderStateApiWarned = true;
                    Debug.LogWarning("CunningEngine: Native API missing render-state exports; falling back to full mesh rebuilds.");
                }
                return false;
            }
        }

        static bool TryCopyRenderVertices(ulong handle, NativeMethods.GeoRenderVertex[] dst) {
            if (dst == null || dst.Length == 0) {
                return false;
            }
            var pinned = GCHandle.Alloc(dst, GCHandleType.Pinned);
            try {
                return NativeMethods.cunning_geo_copy_render_vertices(handle, pinned.AddrOfPinnedObject()) >= dst.Length;
            } catch (EntryPointNotFoundException) {
                s_renderStateApiAvailable = false;
                return false;
            } finally {
                pinned.Free();
            }
        }

        static bool TryCopyRenderVerticesRange(ulong handle, int start, int count, NativeMethods.GeoRenderVertex[] dst) {
            if (dst == null || count <= 0 || dst.Length < count) {
                return false;
            }
            var pinned = GCHandle.Alloc(dst, GCHandleType.Pinned);
            try {
                return NativeMethods.cunning_geo_copy_render_vertices_range(handle, (uint)start, (uint)count, pinned.AddrOfPinnedObject()) >= count;
            } catch (EntryPointNotFoundException) {
                s_renderStateApiAvailable = false;
                return false;
            } finally {
                pinned.Free();
            }
        }

        static int[] ReadPrimitiveMaterialIndices(ulong handle, int primitiveCount) {
            if (primitiveCount <= 0) {
                return Array.Empty<int>();
            }

            if (s_renderStateApiAvailable) {
                var values = new int[primitiveCount];
                var pinned = GCHandle.Alloc(values, GCHandleType.Pinned);
                try {
                    var copied = NativeMethods.cunning_geo_copy_render_material_indices(handle, pinned.AddrOfPinnedObject());
                    if (copied >= primitiveCount) {
                        return values;
                    }
                } catch (EntryPointNotFoundException) {
                    s_renderStateApiAvailable = false;
                } finally {
                    pinned.Free();
                }
            }

            return TryReadPrimitiveMaterialIndicesLegacy(handle, primitiveCount) ?? new int[primitiveCount];
        }

        static bool TryFillMeshFromHandleLegacy(
            ulong handle,
            Mesh targetMesh,
            string meshName,
            bool includeLines,
            out MeshBuildData buildData
        ) {
            buildData = new MeshBuildData();
            if (handle == 0 || targetMesh == null) return false;

            int vertexCount = (int)NativeMethods.cunning_geo_get_vertex_count(handle);
            int lineIndexCount = includeLines ? (int)NativeMethods.cunning_geo_copy_lines(handle, IntPtr.Zero) : 0;

            if (vertexCount <= 0) return false;

            var vertices = new Vector3[vertexCount];
            Vector3[] normals = null;
            Vector2[] uv0 = null;
            buildData.lines = includeLines ? new int[lineIndexCount] : Array.Empty<int>();

            var vertexHandle = GCHandle.Alloc(vertices, GCHandleType.Pinned);
            GCHandle lineHandle = default;
            if (includeLines) lineHandle = GCHandle.Alloc(buildData.lines, GCHandleType.Pinned);

            try {
                NativeMethods.cunning_geo_copy_vertices(handle, vertexHandle.AddrOfPinnedObject());
                if (includeLines && lineIndexCount > 0) {
                    NativeMethods.cunning_geo_copy_lines(handle, lineHandle.AddrOfPinnedObject());
                }
            } finally {
                vertexHandle.Free();
                if (includeLines && lineHandle.IsAllocated) lineHandle.Free();
            }
            CunningUnityCoordinates.ConvertPositions(vertices);

            targetMesh.Clear();
            targetMesh.name = meshName;
            if (vertexCount > 65000) {
                targetMesh.indexFormat = IndexFormat.UInt32;
            }

            targetMesh.vertices = vertices;
            if (TryCopyRenderNormalsLegacy(handle, vertexCount, out normals)) {
                targetMesh.normals = normals;
            }
            if (TryCopyRenderUv0Legacy(handle, vertexCount, out uv0)) {
                targetMesh.uv = uv0;
            }

            var lineIndices = buildData.lines;
            buildData = BuildSolidSubmeshesLegacy(handle);
            buildData.lines = lineIndices;
            var solidSubmeshCount = Mathf.Max(1, buildData.solidSubmeshIndices.Length);
            targetMesh.subMeshCount = solidSubmeshCount;
            for (int submeshIndex = 0; submeshIndex < solidSubmeshCount; submeshIndex++) {
                var indices = submeshIndex < buildData.solidSubmeshIndices.Length
                    ? buildData.solidSubmeshIndices[submeshIndex]
                    : Array.Empty<int>();
                targetMesh.SetIndices(indices, MeshTopology.Triangles, submeshIndex, true);
            }

            bool hasAnyTriangles = false;
            for (int i = 0; i < buildData.solidSubmeshIndices.Length; i++) {
                if (buildData.solidSubmeshIndices[i] != null && buildData.solidSubmeshIndices[i].Length >= 3) {
                    hasAnyTriangles = true;
                    break;
                }
            }

            if (hasAnyTriangles && normals == null) {
                targetMesh.RecalculateNormals();
            }

            if (includeLines) {
                targetMesh.subMeshCount = solidSubmeshCount + 1;
                targetMesh.SetIndices(
                    (buildData.lines.Length >= 2 && (buildData.lines.Length % 2) == 0) ? buildData.lines : Array.Empty<int>(),
                    MeshTopology.Lines,
                    solidSubmeshCount);
            }

            targetMesh.RecalculateBounds();
            return true;
        }

        static MeshBuildData BuildSolidSubmeshesLegacy(ulong handle) {
            var buildData = new MeshBuildData();
            int primitiveCount = (int)NativeMethods.cunning_geo_get_prim_count(handle);
            if (primitiveCount <= 0) {
                buildData.solidSubmeshIndices = new[] { Array.Empty<int>() };
                buildData.solidMaterialSlots = new[] { 0 };
                return buildData;
            }

            var materialByPrimitive = TryReadPrimitiveMaterialIndicesLegacy(handle, primitiveCount);
            int[] vertexOffsets = TryReadU32Array(handle, NativeMethods.cunning_geo_copy_prim_vertex_offsets);
            int[] vertexIndices = TryReadU32Array(handle, NativeMethods.cunning_geo_copy_prim_vertex_indices);

            if (materialByPrimitive == null || vertexOffsets == null || vertexOffsets.Length != primitiveCount + 1 || vertexIndices == null) {
                int[] fallbackTriangles = TryReadTriangleIndices(handle);
                buildData.solidSubmeshIndices = new[] { fallbackTriangles ?? Array.Empty<int>() };
                buildData.solidMaterialSlots = new[] { 0 };
                return buildData;
            }

            var grouped = new SortedDictionary<int, List<int>>();
            for (int primIndex = 0; primIndex < primitiveCount; primIndex++) {
                int start = vertexOffsets[primIndex];
                int end = vertexOffsets[primIndex + 1];
                int count = end - start;
                if (count < 3) {
                    continue;
                }

                int materialSlot = primIndex < materialByPrimitive.Length ? materialByPrimitive[primIndex] : 0;
                if (!grouped.TryGetValue(materialSlot, out var triangleList)) {
                    triangleList = new List<int>(count * 3);
                    grouped.Add(materialSlot, triangleList);
                }

                int v0 = vertexIndices[start];
                for (int index = start + 1; index < end - 1; index++) {
                    CunningUnityCoordinates.AddTriangle(
                        triangleList,
                        v0,
                        vertexIndices[index],
                        vertexIndices[index + 1]);
                }
            }

            if (grouped.Count == 0) {
                buildData.solidSubmeshIndices = new[] { Array.Empty<int>() };
                buildData.solidMaterialSlots = new[] { 0 };
                return buildData;
            }

            buildData.solidSubmeshIndices = new int[grouped.Count][];
            buildData.solidMaterialSlots = new int[grouped.Count];
            int submeshSlot = 0;
            foreach (var kv in grouped) {
                buildData.solidMaterialSlots[submeshSlot] = kv.Key;
                buildData.solidSubmeshIndices[submeshSlot] = kv.Value.ToArray();
                submeshSlot++;
            }

            return buildData;
        }

        static int[] TryReadPrimitiveMaterialIndicesLegacy(ulong handle, int primitiveCount) {
            if (primitiveCount <= 0) return Array.Empty<int>();
            if (NativeMethods.cunning_geo_get_attr_len(handle, PrimitiveAttrClass, UnityMaterialIndexAttr) == 0) {
                return null;
            }

            var values = new int[primitiveCount];
            var valueHandle = GCHandle.Alloc(values, GCHandleType.Pinned);
            try {
                uint copied = NativeMethods.cunning_geo_copy_attr_i32(
                    handle,
                    PrimitiveAttrClass,
                    UnityMaterialIndexAttr,
                    valueHandle.AddrOfPinnedObject(),
                    (uint)primitiveCount);
                return copied >= primitiveCount ? values : null;
            } finally {
                valueHandle.Free();
            }
        }

        static int[] TryReadTriangleIndices(ulong handle) {
            int triangleIndexCount = (int)NativeMethods.cunning_geo_copy_indices(handle, IntPtr.Zero);
            if (triangleIndexCount <= 0) {
                return Array.Empty<int>();
            }

            var triangles = new int[triangleIndexCount];
            var triangleHandle = GCHandle.Alloc(triangles, GCHandleType.Pinned);
            try {
                NativeMethods.cunning_geo_copy_indices(handle, triangleHandle.AddrOfPinnedObject());
            } finally {
                triangleHandle.Free();
            }
            CunningUnityCoordinates.FlipTriangleWindingInPlace(triangles);
            return triangles;
        }

        delegate uint CopyU32BufferDelegate(ulong handle, IntPtr outPtr);

        static int[] TryReadU32Array(ulong handle, CopyU32BufferDelegate copyDelegate) {
            int count = (int)copyDelegate(handle, IntPtr.Zero);
            if (count <= 0) {
                return null;
            }

            var buffer = new uint[count];
            var bufferHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try {
                copyDelegate(handle, bufferHandle.AddrOfPinnedObject());
            } finally {
                bufferHandle.Free();
            }

            var result = new int[count];
            for (int index = 0; index < count; index++) {
                result[index] = (int)buffer[index];
            }
            return result;
        }

        static bool TryCopyRenderNormalsLegacy(ulong handle, int vertexCount, out Vector3[] normals) {
            normals = null;
            if (handle == 0 || vertexCount <= 0) return false;

            var flat = new float[vertexCount * 3];
            var flatHandle = GCHandle.Alloc(flat, GCHandleType.Pinned);
            try {
                var copied = NativeMethods.cunning_geo_copy_vertex_normals(handle, flatHandle.AddrOfPinnedObject());
                if (copied != flat.Length) return false;
            } catch (EntryPointNotFoundException) {
                return false;
            } finally {
                flatHandle.Free();
            }

            normals = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++) {
                int baseIndex = i * 3;
                normals[i] = new Vector3(flat[baseIndex], flat[baseIndex + 1], flat[baseIndex + 2]);
            }
            CunningUnityCoordinates.ConvertNormals(normals);
            return true;
        }

        static bool TryCopyRenderUv0Legacy(ulong handle, int vertexCount, out Vector2[] uv0) {
            uv0 = null;
            if (handle == 0 || vertexCount <= 0) return false;

            var flat = new float[vertexCount * 2];
            var flatHandle = GCHandle.Alloc(flat, GCHandleType.Pinned);
            try {
                var copied = NativeMethods.cunning_geo_copy_vertex_uv0(handle, flatHandle.AddrOfPinnedObject());
                if (copied != flat.Length) return false;
            } catch (EntryPointNotFoundException) {
                return false;
            } finally {
                flatHandle.Free();
            }

            uv0 = new Vector2[vertexCount];
            for (int i = 0; i < vertexCount; i++) {
                int baseIndex = i * 2;
                uv0[i] = new Vector2(flat[baseIndex], flat[baseIndex + 1]);
            }
            return true;
        }

        internal static Mesh BuildMeshFromHandle(ulong handle, string meshName) {
            var mesh = new Mesh();
            if (!TryFillMeshFromHandle(handle, mesh, meshName, includeLines: false, out _)) {
                if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
                else UnityEngine.Object.DestroyImmediate(mesh);
                return null;
            }
            return mesh;
        }
    }
}
