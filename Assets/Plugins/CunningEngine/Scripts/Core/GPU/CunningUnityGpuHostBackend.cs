using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CunningEngine {
    public sealed class CunningUnityGpuHostBackend : IDisposable {
        sealed class KernelRecord {
            public string label;
            public string entryPoint;
            public string source;
            public ComputeShader shader;
            public int kernelIndex;
            public bool compileAttempted;
            public readonly Dictionary<uint, string> bindingNames = new();
        }

        sealed class BufferRecord {
            public ComputeBuffer buffer;
            public ulong sizeBytes;
            public uint strideBytes;
            public int count;
            public byte[] initialData;
            public bool createAttempted;
        }

        sealed class FieldRecord {
            public ComputeBuffer buffer;
            public Texture sourceTexture;
            public uint dimX;
            public uint dimY;
            public uint dimZ;
            public uint strideF32;
            public int floatCount;
            public float[] initialData;
            public bool createAttempted;
            public bool ownsBuffer;
            public bool external;
            public bool textureDirty;
        }

        readonly Dictionary<ulong, KernelRecord> kernels = new();
        readonly Dictionary<ulong, BufferRecord> buffers = new();
        readonly Dictionary<ulong, FieldRecord> fields = new();
        readonly GCHandle selfHandle;
        readonly IntPtr callbackTablePtr;
        readonly List<Delegate> callbackRoots = new();
        readonly object mainThreadGate = new();
        readonly Queue<MainThreadRequest> mainThreadRequests = new();
        readonly HashSet<ulong> completedFences = new();
        readonly HashSet<ulong> failedFences = new();
        readonly int mainThreadId;
        ulong nextKernel = 1;
        ulong nextResource = 1;
        ulong nextFence = 1;
        bool disposed;
        static ComputeShader textureFieldCopyShader;
        static int textureFieldCopyKernel = -1;
        static bool textureFieldCopyShaderAttempted;

        public IntPtr CallbackTablePtr => callbackTablePtr;

        public CunningUnityGpuHostBackend() {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            selfHandle = GCHandle.Alloc(this);
            var callbacks = new NativeMethods.CunningHostGpuBackendCallbacks {
                user_data = GCHandle.ToIntPtr(selfHandle),
                execution_model = (uint)NativeMethods.CunningGpuExecutionModel.EngineHostedQueueInterlocked,
                capabilities = DefaultCapabilities(),
                initial_memory_budget = DefaultMemoryBudget(),
                memory_budget = Root(new NativeMethods.HostGpuMemoryBudgetFn(MemoryBudget)),
                resolve_rust_wgpu_device_queue_bridge = IntPtr.Zero,
                resolve_rust_relax_v7_host_recording_bridge = IntPtr.Zero,
                resolve_rust_relax_v7_execution_object_bridge = IntPtr.Zero,
                compile_module = Root(new NativeMethods.HostGpuCompileModuleFn(CompileModule)),
                create_buffer = Root(new NativeMethods.HostGpuCreateBufferFn(CreateBuffer)),
                write_buffer = Root(new NativeMethods.HostGpuWriteBufferFn(WriteBuffer)),
                create_field = Root(new NativeMethods.HostGpuCreateFieldFn(CreateField)),
                import_buffer = Root(new NativeMethods.HostGpuImportBufferFn(ImportBuffer)),
                export_buffer = Root(new NativeMethods.HostGpuExportBufferFn(ExportBuffer)),
                import_field = Root(new NativeMethods.HostGpuImportFieldFn(ImportField)),
                export_field = Root(new NativeMethods.HostGpuExportFieldFn(ExportField)),
                evict_resource = Root(new NativeMethods.HostGpuResidencyFn(ResourceNoop)),
                rehydrate_resource = Root(new NativeMethods.HostGpuResidencyFn(ResourceNoop)),
                create_fence = Root(new NativeMethods.HostGpuCreateFenceFn(CreateFence)),
                submit = Root(new NativeMethods.HostGpuSubmitFn(Submit)),
                record_into_host = Root(new NativeMethods.HostGpuRecordIntoHostFn(RecordIntoHost)),
                readback = Root(new NativeMethods.HostGpuReadbackFn(Readback)),
                poll_fence = Root(new NativeMethods.HostGpuPollFenceFn(PollFence)),
                await_fence = Root(new NativeMethods.HostGpuAwaitFenceFn(AwaitFence)),
            };
            callbackTablePtr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.CunningHostGpuBackendCallbacks>());
            Marshal.StructureToPtr(callbacks, callbackTablePtr, false);
        }

        sealed class MainThreadRequest {
            readonly Func<NativeMethods.CunningStatus> body;
            public NativeMethods.CunningStatus Status;
            public Exception Exception;
            public bool Done;

            public MainThreadRequest(Func<NativeMethods.CunningStatus> body) {
                this.body = body;
            }

            public void Execute() {
                try {
                    Status = body();
                } catch (Exception e) {
                    Exception = e;
                    Status = NativeMethods.CunningStatus.Unsupported;
                } finally {
                    Done = true;
                }
            }
        }

        public ulong RegisterExternalField(ComputeBuffer buffer, uint dimX, uint dimY, uint dimZ, uint strideF32) {
            if (buffer == null || dimX == 0 || dimY == 0 || dimZ == 0 || strideF32 == 0) return 0;
            ulong required = checked((ulong)dimX * dimY * dimZ * strideF32);
            if ((ulong)buffer.count < required) return 0;
            ulong handle;
            lock (mainThreadGate) {
                handle = nextResource++;
                fields[handle] = new FieldRecord {
                    buffer = buffer,
                    dimX = dimX,
                    dimY = dimY,
                    dimZ = dimZ,
                    strideF32 = strideF32,
                    floatCount = buffer.count,
                    createAttempted = true,
                    ownsBuffer = false,
                    external = true,
                };
            }
            return handle;
        }

        public ulong RegisterExternalTextureField(Texture texture, uint dimX, uint dimY, uint dimZ, uint strideF32) {
            if (texture == null || dimX == 0 || dimY == 0 || dimZ != 1 || strideF32 != 1) return 0;
            var floatCount = checked((int)((ulong)dimX * dimY * dimZ * strideF32));
            ulong handle;
            lock (mainThreadGate) {
                handle = nextResource++;
                fields[handle] = new FieldRecord {
                    sourceTexture = texture,
                    dimX = dimX,
                    dimY = dimY,
                    dimZ = dimZ,
                    strideF32 = strideF32,
                    floatCount = floatCount,
                    ownsBuffer = true,
                    external = true,
                    textureDirty = true,
                };
            }
            return handle;
        }

        public bool UpdateExternalTextureField(ulong handle, Texture texture, uint dimX, uint dimY, uint dimZ, uint strideF32) {
            if (handle == 0 || texture == null || dimX == 0 || dimY == 0 || dimZ != 1 || strideF32 != 1) return false;
            var floatCount = checked((int)((ulong)dimX * dimY * dimZ * strideF32));
            lock (mainThreadGate) {
                if (!fields.TryGetValue(handle, out var field) || !field.external) return false;
                bool changedKind = field.sourceTexture == null || !field.ownsBuffer;
                bool resized = field.dimX != dimX || field.dimY != dimY || field.dimZ != dimZ || field.strideF32 != strideF32 || field.floatCount != floatCount;
                if (changedKind || resized) {
                    if (field.ownsBuffer) field.buffer?.Dispose();
                    field.buffer = null;
                    field.createAttempted = false;
                }
                field.sourceTexture = texture;
                field.dimX = dimX;
                field.dimY = dimY;
                field.dimZ = dimZ;
                field.strideF32 = strideF32;
                field.floatCount = floatCount;
                field.ownsBuffer = true;
                field.textureDirty = true;
            }
            return true;
        }

        public void UnregisterExternalField(ulong handle) {
            if (handle == 0) return;
            lock (mainThreadGate) {
                if (fields.TryGetValue(handle, out var field) && field.external) {
                    if (field.ownsBuffer) field.buffer?.Dispose();
                    fields.Remove(handle);
                }
            }
        }

        sealed class AsyncReadbackResult {
            public NativeMethods.CunningStatus Status = NativeMethods.CunningStatus.Unsupported;
            public byte[] Bytes;
            public Exception Exception;
        }

        public void PumpMainThreadWork() {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId) return;
            while (true) {
                MainThreadRequest request;
                lock (mainThreadGate) {
                    if (mainThreadRequests.Count == 0) return;
                    request = mainThreadRequests.Dequeue();
                }
                request.Execute();
                if (request.Exception != null) Debug.LogError("Cunning Unity GPU main-thread request failed: " + request.Exception);
                lock (mainThreadGate) Monitor.PulseAll(mainThreadGate);
            }
        }

        NativeMethods.CunningStatus RunOnMainThread(Func<NativeMethods.CunningStatus> body) {
            if (Thread.CurrentThread.ManagedThreadId == mainThreadId) return body();
            var request = new MainThreadRequest(body);
            lock (mainThreadGate) {
                mainThreadRequests.Enqueue(request);
                Monitor.PulseAll(mainThreadGate);
                while (!request.Done && !disposed) Monitor.Wait(mainThreadGate, 8);
            }
            if (request.Exception != null) Debug.LogError("Cunning Unity GPU callback failed: " + request.Exception);
            return request.Done ? request.Status : NativeMethods.CunningStatus.Unsupported;
        }

        void QueueMainThread(Func<NativeMethods.CunningStatus> body) {
            lock (mainThreadGate) {
                mainThreadRequests.Enqueue(new MainThreadRequest(body));
                Monitor.PulseAll(mainThreadGate);
            }
        }

        IntPtr Root(Delegate callback) {
            callbackRoots.Add(callback);
            return Marshal.GetFunctionPointerForDelegate(callback);
        }

        static CunningUnityGpuHostBackend Self(IntPtr userData) {
            return (CunningUnityGpuHostBackend)GCHandle.FromIntPtr(userData).Target;
        }

        static NativeMethods.CunningGpuCapabilities DefaultCapabilities() {
            return new NativeMethods.CunningGpuCapabilities {
                max_workgroup_size_x = 1024,
                max_workgroup_size_y = 1024,
                max_workgroup_size_z = 64,
                max_bind_groups = 4,
                max_storage_buffers_per_stage = 16,
                max_shared_memory_size = 32 * 1024,
                subgroup_size = 32,
            };
        }

        static NativeMethods.CunningGpuMemoryBudget DefaultMemoryBudget() {
            const ulong budget = 512UL * 1024UL * 1024UL;
            return new NativeMethods.CunningGpuMemoryBudget {
                budget_bytes = budget,
                available_bytes = budget,
                dedicated_bytes = 0,
                shared_bytes = budget,
            };
        }

        static NativeMethods.CunningStatus MemoryBudget(IntPtr userData, IntPtr outBudget) {
            if (outBudget == IntPtr.Zero) return NativeMethods.CunningStatus.InvalidArgument;
            Marshal.StructureToPtr(DefaultMemoryBudget(), outBudget, false);
            return NativeMethods.CunningStatus.Ok;
        }

        static NativeMethods.CunningStatus CompileModule(IntPtr userData, IntPtr descPtr, IntPtr outHandle) {
            var self = Self(userData);
            try {
                if (descPtr == IntPtr.Zero || outHandle == IntPtr.Zero) return NativeMethods.CunningStatus.InvalidArgument;
                var desc = Marshal.PtrToStructure<NativeMethods.CunningKernelModuleDesc>(descPtr);
                if ((NativeMethods.CunningKernelSourceKind)desc.source_kind != NativeMethods.CunningKernelSourceKind.Hlsl) {
                    return NativeMethods.CunningStatus.Unsupported;
                }
                var label = PtrString(desc.label, "cunning_kernel");
                var entry = PtrString(desc.entry_point, "");
                var source = PtrString(desc.source_utf8, "");
                if (string.IsNullOrEmpty(entry) || string.IsNullOrEmpty(source)) return NativeMethods.CunningStatus.InvalidArgument;
                var kernel = new KernelRecord {
                    label = label,
                    entryPoint = entry,
                    source = source,
                };
                ReadBindingNames(source, desc, kernel.bindingNames);
                ulong handle;
                lock (self.mainThreadGate) {
                    handle = self.nextKernel++;
                    self.kernels[handle] = kernel;
                }
                Marshal.WriteInt64(outHandle, unchecked((long)handle));
                return NativeMethods.CunningStatus.Ok;
            } catch (Exception e) {
                Debug.LogError("Cunning Unity GPU compile_module failed: " + e);
                return NativeMethods.CunningStatus.Unsupported;
            }
        }

        static NativeMethods.CunningStatus CreateBuffer(IntPtr userData, IntPtr descPtr, IntPtr outHandle) {
            var self = Self(userData);
            try {
                if (descPtr == IntPtr.Zero || outHandle == IntPtr.Zero) return NativeMethods.CunningStatus.InvalidArgument;
                var desc = Marshal.PtrToStructure<NativeMethods.CunningGpuBufferDesc>(descPtr);
                var stride = 4u;
                var count = checked((int)Math.Max(1UL, (desc.size_bytes + stride - 1) / stride));
                ulong handle;
                lock (self.mainThreadGate) {
                    handle = self.nextResource++;
                    self.buffers[handle] = new BufferRecord {
                        sizeBytes = desc.size_bytes,
                        strideBytes = stride,
                        count = count,
                    };
                }
                Marshal.WriteInt64(outHandle, unchecked((long)handle));
                return NativeMethods.CunningStatus.Ok;
            } catch (Exception e) {
                Debug.LogError("Cunning Unity GPU create_buffer failed: " + e);
                return NativeMethods.CunningStatus.Unsupported;
            }
        }

        static NativeMethods.CunningStatus WriteBuffer(IntPtr userData, ulong handle, ulong offset, IntPtr bytesPtr, ulong byteLen) {
            var self = Self(userData);
            try {
                if (bytesPtr == IntPtr.Zero && byteLen != 0) return NativeMethods.CunningStatus.InvalidArgument;
                var bytes = new byte[checked((int)byteLen)];
                if (bytes.Length > 0) Marshal.Copy(bytesPtr, bytes, 0, bytes.Length);
                return self.RunOnMainThread(() => self.WriteBufferMain(handle, offset, bytes));
            } catch (Exception e) {
                Debug.LogError("Cunning Unity GPU write_buffer failed: " + e);
                return NativeMethods.CunningStatus.Unsupported;
            }
        }

        NativeMethods.CunningStatus WriteBufferMain(ulong handle, ulong offset, byte[] bytes) {
            BufferRecord record;
            lock (mainThreadGate) {
                if (!buffers.TryGetValue(handle, out record)) return NativeMethods.CunningStatus.InvalidArgument;
                var end = checked(offset + (ulong)bytes.Length);
                if (end > record.sizeBytes) return NativeMethods.CunningStatus.InvalidArgument;
            }
            if (record.buffer == null) {
                var data = record.initialData ?? new byte[checked((int)record.sizeBytes)];
                Buffer.BlockCopy(bytes, 0, data, checked((int)offset), bytes.Length);
                record.initialData = data;
                return NativeMethods.CunningStatus.Ok;
            }
            return WriteBufferBytes(record, offset, bytes);
        }

        static NativeMethods.CunningStatus CreateField(IntPtr userData, IntPtr descPtr, IntPtr outHandle) {
            var self = Self(userData);
            try {
                if (descPtr == IntPtr.Zero || outHandle == IntPtr.Zero) return NativeMethods.CunningStatus.InvalidArgument;
                var desc = Marshal.PtrToStructure<NativeMethods.CunningGpuFieldDesc>(descPtr);
                if ((NativeMethods.CunningGpuFieldStorageMode)desc.storage_mode != NativeMethods.CunningGpuFieldStorageMode.Dense) {
                    return NativeMethods.CunningStatus.Unsupported;
                }
                var stride = Math.Max(1u, desc.layer_count != 0 ? desc.layer_count : desc.channels);
                var cellCount = checked((ulong)desc.dim_x * Math.Max(1u, desc.dim_y) * Math.Max(1u, desc.dim_z));
                var floatCount = checked((int)(cellCount * stride));
                var initial = new float[floatCount];
                WriteLayerDefaults(initial, stride, desc.layers, desc.layer_count, cellCount);
                ulong handle;
                lock (self.mainThreadGate) {
                    handle = self.nextResource++;
                    self.fields[handle] = new FieldRecord {
                        dimX = desc.dim_x,
                        dimY = desc.dim_y,
                        dimZ = Math.Max(1u, desc.dim_z),
                        strideF32 = stride,
                        floatCount = floatCount,
                        initialData = initial,
                        ownsBuffer = true,
                    };
                }
                Marshal.WriteInt64(outHandle, unchecked((long)handle));
                return NativeMethods.CunningStatus.Ok;
            } catch (Exception e) {
                Debug.LogError("Cunning Unity GPU create_field failed: " + e);
                return NativeMethods.CunningStatus.Unsupported;
            }
        }

        static NativeMethods.CunningStatus Submit(IntPtr userData, IntPtr descPtr, IntPtr outFence) {
            var self = Self(userData);
            try {
                if (descPtr == IntPtr.Zero || outFence == IntPtr.Zero) return NativeMethods.CunningStatus.InvalidArgument;
                var desc = Marshal.PtrToStructure<NativeMethods.CunningGpuSubmissionDesc>(descPtr);
                var bufferHandles = ReadHandles(desc.bindings.buffers, desc.bindings.buffer_count);
                var fieldHandles = ReadHandles(desc.bindings.fields, desc.bindings.field_count);
                ulong fence;
                lock (self.mainThreadGate) {
                    if (!self.kernels.ContainsKey(desc.kernel)) return NativeMethods.CunningStatus.InvalidArgument;
                    fence = self.nextFence++;
                }
                var kernelHandle = desc.kernel;
                var dimX = desc.dim_x;
                var dimY = desc.dim_y;
                var dimZ = desc.dim_z;
                self.QueueMainThread(() => self.ExecuteSubmission(kernelHandle, bufferHandles, fieldHandles, dimX, dimY, dimZ, fence));
                Marshal.WriteInt64(outFence, unchecked((long)fence));
                return NativeMethods.CunningStatus.Ok;
            } catch (Exception e) {
                Debug.LogError("Cunning Unity GPU submit failed: " + e);
                return NativeMethods.CunningStatus.Unsupported;
            }
        }

        static NativeMethods.CunningStatus Readback(IntPtr userData, IntPtr descPtr, IntPtr outBytes, IntPtr inoutLen, IntPtr outFence, IntPtr outHasFence) {
            var self = Self(userData);
            if (Thread.CurrentThread.ManagedThreadId == self.mainThreadId) {
                return ReadbackMain(self, descPtr, outBytes, inoutLen, outFence, outHasFence);
            }
            return self.ReadbackWorkerAsync(descPtr, outBytes, inoutLen, outFence, outHasFence);
        }

        static NativeMethods.CunningStatus ReadbackMain(CunningUnityGpuHostBackend self, IntPtr descPtr, IntPtr outBytes, IntPtr inoutLen, IntPtr outFence, IntPtr outHasFence) {
            try {
                self.PumpMainThreadWork();
                if (descPtr == IntPtr.Zero || inoutLen == IntPtr.Zero) return NativeMethods.CunningStatus.InvalidArgument;
                var desc = Marshal.PtrToStructure<NativeMethods.CunningGpuReadbackRequest>(descPtr);
                var bytes = self.ReadResourceBytes(desc);
                return WriteReadbackBytes(desc, bytes, outBytes, inoutLen, outFence, outHasFence);
            } catch (Exception e) {
                Debug.LogError("Cunning Unity GPU readback failed: " + e);
                return NativeMethods.CunningStatus.Unsupported;
            }
        }

        NativeMethods.CunningStatus ReadbackWorkerAsync(IntPtr descPtr, IntPtr outBytes, IntPtr inoutLen, IntPtr outFence, IntPtr outHasFence) {
            var done = new ManualResetEventSlim(false);
            try {
                var result = new AsyncReadbackResult();
                var submitStatus = RunOnMainThread(() => BeginAsyncReadbackMain(descPtr, result, done));
                if (submitStatus != NativeMethods.CunningStatus.Ok) return submitStatus;
                while (!disposed && !done.Wait(2)) { }
                if (!done.IsSet) return NativeMethods.CunningStatus.Unsupported;
                if (result.Exception != null) {
                    Debug.LogError("Cunning Unity GPU async readback failed: " + result.Exception);
                    return NativeMethods.CunningStatus.Unsupported;
                }
                if (result.Status != NativeMethods.CunningStatus.Ok) return result.Status;
                var desc = Marshal.PtrToStructure<NativeMethods.CunningGpuReadbackRequest>(descPtr);
                return WriteReadbackBytes(desc, result.Bytes ?? Array.Empty<byte>(), outBytes, inoutLen, outFence, outHasFence);
            } finally {
                done.Dispose();
            }
        }

        NativeMethods.CunningStatus BeginAsyncReadbackMain(IntPtr descPtr, AsyncReadbackResult result, ManualResetEventSlim done) {
            try {
                if (descPtr == IntPtr.Zero) {
                    result.Status = NativeMethods.CunningStatus.InvalidArgument;
                    done.Set();
                    return NativeMethods.CunningStatus.InvalidArgument;
                }
                var desc = Marshal.PtrToStructure<NativeMethods.CunningGpuReadbackRequest>(descPtr);
                var resource = ResolveReadbackBuffer(desc);
                if (resource == null) {
                    result.Status = NativeMethods.CunningStatus.InvalidArgument;
                    done.Set();
                    return NativeMethods.CunningStatus.InvalidArgument;
                }
                AsyncGPUReadback.Request(resource.buffer, request => {
                    try {
                        if (request.hasError) {
                            result.Status = NativeMethods.CunningStatus.Unsupported;
                            return;
                        }
                        var data = request.GetData<float>();
                        var floats = new float[data.Length];
                        data.CopyTo(floats);
                        var bytes = new byte[floats.Length * sizeof(float)];
                        Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
                        result.Bytes = bytes;
                        result.Status = NativeMethods.CunningStatus.Ok;
                    } catch (Exception e) {
                        result.Exception = e;
                        result.Status = NativeMethods.CunningStatus.Unsupported;
                    } finally {
                        done.Set();
                    }
                });
                return NativeMethods.CunningStatus.Ok;
            } catch (Exception e) {
                result.Exception = e;
                result.Status = NativeMethods.CunningStatus.Unsupported;
                done.Set();
                return NativeMethods.CunningStatus.Unsupported;
            }
        }

        byte[] ReadResourceBytes(NativeMethods.CunningGpuReadbackRequest desc) {
            var resource = ResolveReadbackBuffer(desc);
            if (resource == null) throw new InvalidOperationException("Unknown readback resource " + desc.resource_handle);
            var floats = new float[resource.count];
            resource.buffer.GetData(floats);
            var bytes = new byte[floats.Length * sizeof(float)];
            Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        BufferRecord ResolveReadbackBuffer(NativeMethods.CunningGpuReadbackRequest desc) {
            if ((NativeMethods.CunningGpuResourceKind)desc.resource_kind == NativeMethods.CunningGpuResourceKind.Buffer) {
                BufferRecord buffer;
                lock (mainThreadGate) {
                    if (!buffers.TryGetValue(desc.resource_handle, out buffer)) return null;
                }
                return EnsureBuffer(buffer) ? buffer : null;
            }
            if ((NativeMethods.CunningGpuResourceKind)desc.resource_kind == NativeMethods.CunningGpuResourceKind.Field) {
                FieldRecord field;
                lock (mainThreadGate) {
                    if (!fields.TryGetValue(desc.resource_handle, out field)) return null;
                }
                if (!EnsureFieldBuffer(field)) return null;
                return new BufferRecord { buffer = field.buffer, sizeBytes = (ulong)field.floatCount * sizeof(float), strideBytes = 4, count = field.floatCount };
            }
            return null;
        }

        static NativeMethods.CunningStatus WriteReadbackBytes(NativeMethods.CunningGpuReadbackRequest desc, byte[] bytes, IntPtr outBytes, IntPtr inoutLen, IntPtr outFence, IntPtr outHasFence) {
            if (inoutLen == IntPtr.Zero) return NativeMethods.CunningStatus.InvalidArgument;
            var offset = checked((int)desc.offset);
            var requested = checked((int)Math.Min(desc.size_bytes, (ulong)Math.Max(0, bytes.Length - offset)));
            var cap = Marshal.ReadInt64(inoutLen);
            var capBytes = cap <= 0 ? 0 : cap > int.MaxValue ? int.MaxValue : (int)cap;
            var copyLen = Math.Min(requested, capBytes);
            if (outBytes != IntPtr.Zero && copyLen > 0) Marshal.Copy(bytes, offset, outBytes, copyLen);
            Marshal.WriteInt64(inoutLen, copyLen);
            if (outFence != IntPtr.Zero) Marshal.WriteInt64(outFence, 0);
            if (outHasFence != IntPtr.Zero) Marshal.WriteInt32(outHasFence, 0);
            return NativeMethods.CunningStatus.Ok;
        }

        static NativeMethods.CunningStatus CreateFence(IntPtr userData, IntPtr label, IntPtr outFence) {
            var self = Self(userData);
            if (outFence == IntPtr.Zero) return NativeMethods.CunningStatus.InvalidArgument;
            lock (self.mainThreadGate) {
                var fence = self.nextFence++;
                self.completedFences.Add(fence);
                Marshal.WriteInt64(outFence, unchecked((long)fence));
            }
            return NativeMethods.CunningStatus.Ok;
        }

        static NativeMethods.CunningStatus PollFence(IntPtr userData, ulong fence, IntPtr outCompleted) {
            if (outCompleted == IntPtr.Zero) return NativeMethods.CunningStatus.InvalidArgument;
            var self = Self(userData);
            lock (self.mainThreadGate) Marshal.WriteInt32(outCompleted, self.completedFences.Contains(fence) ? 1 : 0);
            return NativeMethods.CunningStatus.Ok;
        }

        static NativeMethods.CunningStatus AwaitFence(IntPtr userData, ulong fence) {
            var self = Self(userData);
            while (!self.disposed) {
                lock (self.mainThreadGate) {
                    if (self.completedFences.Contains(fence)) return NativeMethods.CunningStatus.Ok;
                    if (self.failedFences.Contains(fence)) return NativeMethods.CunningStatus.Unsupported;
                }
                if (Thread.CurrentThread.ManagedThreadId == self.mainThreadId) {
                    self.PumpMainThreadWork();
                } else {
                    lock (self.mainThreadGate) Monitor.Wait(self.mainThreadGate, 2);
                }
            }
            return NativeMethods.CunningStatus.Unsupported;
        }

        static NativeMethods.CunningStatus ResourceNoop(IntPtr userData, IntPtr desc) => NativeMethods.CunningStatus.Ok;

        static NativeMethods.CunningStatus ImportBuffer(IntPtr userData, IntPtr desc, IntPtr outHandle) => NativeMethods.CunningStatus.Unsupported;
        static NativeMethods.CunningStatus ImportField(IntPtr userData, IntPtr descPtr, IntPtr outHandle) {
            var self = Self(userData);
            try {
                if (descPtr == IntPtr.Zero || outHandle == IntPtr.Zero) return NativeMethods.CunningStatus.InvalidArgument;
                var desc = Marshal.PtrToStructure<NativeMethods.CunningHostGpuImportFieldDesc>(descPtr);
                FieldRecord field;
                lock (self.mainThreadGate) {
                    if (!self.fields.TryGetValue(desc.host_resource, out field)) return NativeMethods.CunningStatus.InvalidArgument;
                }
                if (field == null || (field.buffer == null && field.sourceTexture == null)) return NativeMethods.CunningStatus.InvalidArgument;
                if (field.dimX != desc.dim_x || field.dimY != desc.dim_y || field.dimZ != Math.Max(1u, desc.dim_z)) {
                    return NativeMethods.CunningStatus.InvalidArgument;
                }
                Marshal.WriteInt64(outHandle, unchecked((long)desc.host_resource));
                return NativeMethods.CunningStatus.Ok;
            } catch (Exception e) {
                Debug.LogError("Cunning Unity GPU import_field failed: " + e);
                return NativeMethods.CunningStatus.Unsupported;
            }
        }
        static NativeMethods.CunningStatus RecordIntoHost(IntPtr userData, IntPtr desc, IntPtr outFence) => NativeMethods.CunningStatus.Unsupported;

        static NativeMethods.CunningStatus ExportBuffer(IntPtr userData, ulong handle, IntPtr desc, IntPtr outExport) {
            if (outExport == IntPtr.Zero) return NativeMethods.CunningStatus.InvalidArgument;
            Marshal.StructureToPtr(new NativeMethods.CunningHostGpuExportBuffer { host_resource = handle }, outExport, false);
            return NativeMethods.CunningStatus.Ok;
        }

        static NativeMethods.CunningStatus ExportField(IntPtr userData, ulong handle, IntPtr desc, IntPtr outExport) {
            if (outExport == IntPtr.Zero) return NativeMethods.CunningStatus.InvalidArgument;
            Marshal.StructureToPtr(new NativeMethods.CunningHostGpuExportField { host_resource = handle }, outExport, false);
            return NativeMethods.CunningStatus.Ok;
        }

        NativeMethods.CunningStatus ExecuteSubmission(ulong kernelHandle, ulong[] bufferHandles, ulong[] fieldHandles, uint dimX, uint dimY, uint dimZ, ulong fence) {
            try {
                KernelRecord kernel;
                lock (mainThreadGate) {
                    if (!kernels.TryGetValue(kernelHandle, out kernel)) return CompleteFence(fence, false);
                }
                if (!EnsureKernel(kernel)) return CompleteFence(fence, false);
                BindResources(kernel, bufferHandles, fieldHandles);
                kernel.shader.Dispatch(kernel.kernelIndex, checked((int)dimX), checked((int)dimY), checked((int)dimZ));
                return CompleteFence(fence, true);
            } catch (Exception e) {
                Debug.LogError("Cunning Unity GPU submit failed: " + e);
                return CompleteFence(fence, false);
            }
        }

        NativeMethods.CunningStatus CompleteFence(ulong fence, bool ok) {
            lock (mainThreadGate) {
                if (ok) completedFences.Add(fence);
                else failedFences.Add(fence);
                Monitor.PulseAll(mainThreadGate);
            }
            return ok ? NativeMethods.CunningStatus.Ok : NativeMethods.CunningStatus.Unsupported;
        }

        bool EnsureKernel(KernelRecord kernel) {
            if (kernel.shader != null) return true;
            if (kernel.compileAttempted) return false;
            kernel.compileAttempted = true;
            kernel.shader = LoadOrCreateComputeShader(kernel.label, kernel.entryPoint, kernel.source);
            if (kernel.shader == null) return false;
            kernel.kernelIndex = kernel.shader.FindKernel(kernel.entryPoint);
            return true;
        }

        bool EnsureBuffer(BufferRecord record) {
            if (record.buffer != null) return true;
            if (record.createAttempted) return false;
            record.createAttempted = true;
            record.buffer = new ComputeBuffer(record.count, checked((int)record.strideBytes), ComputeBufferType.Structured);
            if (record.initialData != null) {
                var status = WriteBufferBytes(record, 0, record.initialData);
                record.initialData = null;
                if (status != NativeMethods.CunningStatus.Ok) return false;
            }
            return true;
        }

        static NativeMethods.CunningStatus WriteBufferBytes(BufferRecord record, ulong offset, byte[] bytes) {
            if (record.strideBytes != 4) return NativeMethods.CunningStatus.Unsupported;
            if ((offset & 3UL) != 0 || (bytes.Length & 3) != 0) return NativeMethods.CunningStatus.InvalidArgument;
            if (record.buffer == null) return NativeMethods.CunningStatus.InvalidArgument;
            var floats = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
            record.buffer.SetData(floats, 0, checked((int)(offset / 4UL)), floats.Length);
            return NativeMethods.CunningStatus.Ok;
        }

        bool EnsureFieldBuffer(FieldRecord record) {
            if (record.sourceTexture != null) return EnsureTextureFieldBuffer(record);
            if (record.buffer != null) return true;
            if (record.createAttempted) return false;
            record.createAttempted = true;
            record.buffer = new ComputeBuffer(record.floatCount, sizeof(float), ComputeBufferType.Structured);
            if (record.initialData != null) {
                record.buffer.SetData(record.initialData);
                record.initialData = null;
            }
            return true;
        }

        bool EnsureTextureFieldBuffer(FieldRecord record) {
            if (record.sourceTexture == null) return false;
            if (record.buffer == null) {
                record.buffer = new ComputeBuffer(record.floatCount, sizeof(float), ComputeBufferType.Structured);
                record.createAttempted = true;
                record.textureDirty = true;
            }
            if (!record.textureDirty) return true;
            if (!EnsureTextureFieldCopyShader(out var shader, out var kernel)) return false;
            shader.SetTexture(kernel, "_CunningSource", record.sourceTexture);
            shader.SetBuffer(kernel, "_CunningDst", record.buffer);
            shader.SetInt("_CunningWidth", checked((int)record.dimX));
            shader.SetInt("_CunningHeight", checked((int)record.dimY));
            shader.SetInt("_CunningStride", checked((int)record.strideF32));
            shader.Dispatch(kernel, Mathf.CeilToInt(record.dimX / 8f), Mathf.CeilToInt(record.dimY / 8f), 1);
            record.textureDirty = false;
            return true;
        }

        void BindResources(KernelRecord kernel, ulong[] bufferHandles, ulong[] fieldHandles) {
            for (int i = 0; i < bufferHandles.Length; i++) {
                BufferRecord buffer;
                lock (mainThreadGate) {
                    if (!buffers.TryGetValue(bufferHandles[i], out buffer)) continue;
                }
                if (!EnsureBuffer(buffer)) continue;
                var name = BindingName(kernel, (uint)i, "buffer_" + i);
                kernel.shader.SetBuffer(kernel.kernelIndex, name, buffer.buffer);
            }
            for (int i = 0; i < fieldHandles.Length; i++) {
                FieldRecord field;
                lock (mainThreadGate) {
                    if (!fields.TryGetValue(fieldHandles[i], out field)) continue;
                }
                if (!EnsureFieldBuffer(field)) continue;
                var binding = checked((uint)(bufferHandles.Length + i));
                var name = BindingName(kernel, binding, i == 0 ? "src_field" : "dst_field");
                kernel.shader.SetBuffer(kernel.kernelIndex, name, field.buffer);
            }
        }

        static string BindingName(KernelRecord kernel, uint binding, string fallback) {
            return kernel.bindingNames.TryGetValue(binding, out var name) && !string.IsNullOrEmpty(name) ? name : fallback;
        }

        static ulong[] ReadHandles(IntPtr ptr, uint count) {
            if (ptr == IntPtr.Zero || count == 0) return Array.Empty<ulong>();
            var values = new ulong[count];
            for (int i = 0; i < values.Length; i++) {
                values[i] = unchecked((ulong)Marshal.ReadInt64(ptr, i * sizeof(ulong)));
            }
            return values;
        }

        static void WriteLayerDefaults(float[] initial, uint stride, IntPtr layers, uint layerCount, ulong cellCount) {
            if (layers == IntPtr.Zero || layerCount == 0) return;
            var layerSize = Marshal.SizeOf<NativeMethods.CunningGpuFieldLayerDesc>();
            for (uint layer = 0; layer < layerCount; layer++) {
                var layerDesc = Marshal.PtrToStructure<NativeMethods.CunningGpuFieldLayerDesc>(IntPtr.Add(layers, checked((int)(layer * (uint)layerSize))));
                if ((NativeMethods.CunningGpuFieldChannelFormat)layerDesc.format != NativeMethods.CunningGpuFieldChannelFormat.R32Float) continue;
                var clear = layerDesc.default_clear_x;
                for (ulong cell = 0; cell < cellCount; cell++) {
                    initial[checked((int)(cell * stride + layer))] = clear;
                }
            }
        }

        static void ReadBindingNames(string source, NativeMethods.CunningKernelModuleDesc desc, Dictionary<uint, string> names) {
            var matches = Regex.Matches(source, @"(?:(?:RW)?StructuredBuffer|(?:RW)?ByteAddressBuffer)\s*(?:<[^>]+>)?\s+([A-Za-z_][A-Za-z0-9_]*)\s*:\s*register\s*\(\s*[tu](\d+)", RegexOptions.CultureInvariant);
            foreach (Match match in matches) {
                if (!match.Success || match.Groups.Count < 3) continue;
                if (uint.TryParse(match.Groups[2].Value, out var binding)) names[binding] = match.Groups[1].Value;
            }
            if (desc.bindings == IntPtr.Zero) return;
            var stride = Marshal.SizeOf<NativeMethods.CunningKernelBindingDesc>();
            for (int i = 0; i < desc.binding_count; i++) {
                var binding = Marshal.PtrToStructure<NativeMethods.CunningKernelBindingDesc>(IntPtr.Add(desc.bindings, i * stride));
                if (!names.ContainsKey(binding.binding)) names[binding.binding] = "binding_" + binding.binding;
            }
        }

        static string PtrString(IntPtr ptr, string fallback) {
            return ptr == IntPtr.Zero ? fallback : (Marshal.PtrToStringAnsi(ptr) ?? fallback);
        }

        const string TextureFieldCopyEntry = "CunningCopyHeightmap";
        const string TextureFieldCopySource = @"
Texture2D<float4> _CunningSource;
RWStructuredBuffer<float> _CunningDst;
int _CunningWidth;
int _CunningHeight;
int _CunningStride;

[numthreads(8, 8, 1)]
void CunningCopyHeightmap(uint3 id : SV_DispatchThreadID) {
    if (id.x >= (uint)_CunningWidth || id.y >= (uint)_CunningHeight) return;
    uint index = (id.y * (uint)_CunningWidth + id.x) * (uint)_CunningStride;
    _CunningDst[index] = _CunningSource.Load(int3(int2(id.xy), 0)).r;
}
";

        static bool EnsureTextureFieldCopyShader(out ComputeShader shader, out int kernel) {
            if (textureFieldCopyShader == null && !textureFieldCopyShaderAttempted) {
                textureFieldCopyShaderAttempted = true;
                textureFieldCopyShader = LoadOrCreateComputeShader("cunning_texture_field_copy", TextureFieldCopyEntry, TextureFieldCopySource);
                if (textureFieldCopyShader != null) textureFieldCopyKernel = textureFieldCopyShader.FindKernel(TextureFieldCopyEntry);
            }
            shader = textureFieldCopyShader;
            kernel = textureFieldCopyKernel;
            return shader != null && kernel >= 0;
        }

        static ComputeShader LoadOrCreateComputeShader(string label, string entry, string source) {
#if UNITY_EDITOR
            source = NormalizeUnityComputeSource(source);
            var assetPath = KernelAssetPath(label, entry, source);
            var fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            var text = "#pragma kernel " + entry + "\n" + source;
            if (!File.Exists(fullPath) || File.ReadAllText(fullPath) != text) {
                File.WriteAllText(fullPath, text, new UTF8Encoding(false));
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }
            return AssetDatabase.LoadAssetAtPath<ComputeShader>(assetPath);
#else
            return null;
#endif
        }

        static string NormalizeUnityComputeSource(string source) {
            return Regex.Replace(source, @":\s*register\s*\(\s*([tu]\d+)\s*,\s*space\d+\s*\)", ": register($1)", RegexOptions.CultureInvariant);
        }

        static string KernelAssetPath(string label, string entry, string source) {
            string hash;
            using (var sha = SHA256.Create()) {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(entry + "\n" + source));
                var builder = new StringBuilder(32);
                for (int i = 0; i < 8 && i < bytes.Length; i++) builder.Append(bytes[i].ToString("x2"));
                hash = builder.ToString();
            }
            var safe = Regex.Replace(label + "_" + entry, @"[^A-Za-z0-9_]+", "_").Trim('_');
            if (string.IsNullOrEmpty(safe)) safe = "kernel";
            return "Assets/Plugins/CunningEngine/Generated/Kernels/" + hash + "_" + safe + ".compute";
        }

        public void Dispose() {
            if (disposed) return;
            disposed = true;
            PumpMainThreadWork();
            if (Thread.CurrentThread.ManagedThreadId == mainThreadId) {
                foreach (var field in fields.Values) if (field.ownsBuffer) field.buffer?.Dispose();
                foreach (var buffer in buffers.Values) buffer.buffer?.Dispose();
                fields.Clear();
                buffers.Clear();
                kernels.Clear();
            }
            lock (mainThreadGate) Monitor.PulseAll(mainThreadGate);
            if (callbackTablePtr != IntPtr.Zero) Marshal.FreeHGlobal(callbackTablePtr);
            if (selfHandle.IsAllocated) selfHandle.Free();
        }
    }
}
