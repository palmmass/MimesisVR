using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine.Rendering;
using UnityVRMod.Core;
using UnityVRMod.Features.VRVisualization.OpenXR;

namespace UnityVRMod.Mimesis
{
    /// <summary>
    /// MIMESIS VR: swapchain copies, swapchain releases and xrEndFrame are executed on Unity's render thread
    /// by a small native helper (MimesisVRNative.dll, plain C - no managed code on the render thread).
    /// If the helper is unavailable everything runs synchronously on the main thread (needs -force-gfx-direct).
    /// </summary>
    internal static class RtSubmit
    {
        private const string Native = "MimesisVRNative";
        [DllImport(Native)] private static extern int MVR_Init2(IntPtr d3d11Device, IntPtr pfnAcquire, IntPtr pfnWait, IntPtr pfnRelease, IntPtr pfnEndFrame);
        [DllImport(Native)] private static extern int MVR_JobSize2();
        [DllImport(Native)] private static extern int MVR_SetFrameFuncs(IntPtr pfnWaitFrame, IntPtr pfnBeginFrame);
        [DllImport(Native)] private static extern IntPtr MVR_GetRenderEventFunc();
        [DllImport(Native)] private static extern IntPtr MVR_GetShared();
        [DllImport(Native, CharSet = CharSet.Unicode)] private static extern int MVR_StartWatchdog(string path);

        /// <summary>Shared block read by the native watchdog: mainStage, mainFrames, renderFrames, armed.</summary>
        public static IntPtr Shared { get; private set; }

        // MvrJob layout (pack 8) - must match mvr_native.c
        private const int MaxCopies = 12, MaxImages = 4, CopySize = 56;
        private const int OffCopyCount = 672, OffStage = 676, OffSession = 680, OffEndInfo = 688, OffDoEnd = 696, OffDone = 700, OffResult = 704, OffSwErr = 708, OffDoWaitBegin = 712, OffBeginResult = 716, OffFrameState = 720, OffUsCopies = 728, OffUsEnd = 732, OffUsWait = 736, OffUsBegin = 740, JobSize = 744;

        private static bool _native;
        private static IntPtr _eventFunc, _job, _endInfo, _frameState;
        private static bool _frameBegun, _loggedFrameLoop;
        private static XrFrameState _lastFrameState;
        private static int _beginFails;

        /// <summary>Native path: xrWaitFrame + xrBeginFrame of the next frame already ran on the render thread.</summary>
        public static bool FrameBegun => _native && _frameBegun;
        public static XrFrameState LastFrameState => _lastFrameState;
        public static void ResetFrame() { _frameBegun = false; }
        private static readonly CommandBuffer _cb = new() { name = "MimesisVR Submit" };
        private static int _copies;
        private static bool _pending;
        private static int _timeouts;
        /// <summary>Diagnostics: main-thread wait for the previous frame and render-thread timings of it (ms).</summary>
        public static float LastWaitMs, RtCopiesMs, RtEndMs, RtWaitFrameMs, RtBeginMs;

        // Fallback (synchronous) storage
        private static readonly List<IntPtr> _fbDst = new(), _fbSrc = new();
        private static readonly List<ulong> _fbRel = new();

        public static bool NativeActive => _native;
        public static bool Pending => _pending;
        public static int NativeStage => (_native && _job != IntPtr.Zero) ? Marshal.ReadInt32(_job, OffStage) : -1;

        public static void Init(IntPtr d3d11Device, ulong instance)
        {
            _native = false;
            try
            {
                int size = MVR_JobSize2();
                if (size != JobSize) { VRModCore.LogError($"[MimesisVR] Native helper job size mismatch ({size} != {JobSize}); using main-thread submit."); return; }
                var gipa = OpenXRNativeLoader.xrGetInstanceProcAddr_ptr_delegate;
                gipa(instance, "xrAcquireSwapchainImage", out IntPtr pAcq);
                gipa(instance, "xrWaitSwapchainImage", out IntPtr pWait);
                gipa(instance, "xrReleaseSwapchainImage", out IntPtr pRel);
                gipa(instance, "xrEndFrame", out IntPtr pEnd);
                gipa(instance, "xrWaitFrame", out IntPtr pWaitFrame);
                gipa(instance, "xrBeginFrame", out IntPtr pBeginFrame);
                LogRuntimeName(instance, gipa);
                if (MVR_Init2(d3d11Device, pAcq, pWait, pRel, pEnd) != 1) { VRModCore.LogError("[MimesisVR] Native helper init failed; using main-thread submit."); return; }
                if (MVR_SetFrameFuncs(pWaitFrame, pBeginFrame) != 1) { VRModCore.LogError("[MimesisVR] Native helper: xrWaitFrame/xrBeginFrame not available; using main-thread submit."); return; }
                _eventFunc = MVR_GetRenderEventFunc();
                if (_job == IntPtr.Zero) _job = Marshal.AllocHGlobal(JobSize);
                if (_endInfo == IntPtr.Zero) _endInfo = Marshal.AllocHGlobal(Marshal.SizeOf<XrFrameEndInfo>());
                if (_frameState == IntPtr.Zero) _frameState = Marshal.AllocHGlobal(Marshal.SizeOf<XrFrameState>());
                Marshal.StructureToPtr(new XrFrameState { type = XrStructureType.XR_TYPE_FRAME_STATE }, _frameState, false);
                _frameBegun = false;
                Clear();
                Marshal.WriteInt32(_job, OffDone, 1);
                _native = true;
                VRModCore.Log("[MimesisVR] Render-thread submit via native helper is active (frame loop: wait/begin/end on the render thread).");
                try
                {
                    Shared = MVR_GetShared();
                    MVR_StartWatchdog(Path.Combine(MvrPaths.DataDir, "mimesisvr_watchdog.log"));
                }
                catch (Exception ex) { VRModCore.LogWarning("[MimesisVR] Native watchdog unavailable: " + ex.Message); }
            }
            catch (Exception ex)
            {
                VRModCore.LogWarning("[MimesisVR] Native helper not available (" + ex.GetType().Name + ": " + ex.Message + "); using main-thread submit (needs -force-gfx-direct).");
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetInstancePropertiesFn(ulong instance, IntPtr props);

        private static void LogRuntimeName(ulong instance, PFN_xrGetInstanceProcAddr gipa)
        {
            IntPtr buf = IntPtr.Zero;
            try
            {
                gipa(instance, "xrGetInstanceProperties", out IntPtr p);
                if (p == IntPtr.Zero) return;
                var fn = Marshal.GetDelegateForFunctionPointer<GetInstancePropertiesFn>(p);
                buf = Marshal.AllocHGlobal(256);
                for (int i = 0; i < 256; i += 4) Marshal.WriteInt32(buf, i, 0);
                Marshal.WriteInt32(buf, 0, 32); // XR_TYPE_INSTANCE_PROPERTIES
                if (fn(instance, buf) < 0) return;
                long ver = Marshal.ReadInt64(buf, 16);
                string name = Marshal.PtrToStringAnsi(buf + 24);
                RuntimeName = name ?? "";
                VRModCore.Log($"[MimesisVR] OpenXR runtime: {name} {(ver >> 48) & 0xffff}.{(ver >> 32) & 0xffff}.{ver & 0xffffffff}");
            }
            catch (Exception ex) { VRModCore.LogWarning("[MimesisVR] Runtime name unavailable: " + ex.Message); }
            finally { if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf); }
        }

        public static string RuntimeName = "";

        private static void Clear()
        {
            for (int i = 0; i < JobSize; i += 4) Marshal.WriteInt32(_job, i, 0);
            _copies = 0;
        }

        /// <summary>Native path: acquire + wait + copy + release a swapchain image on the render thread.</summary>
        public static void AddSwapchainCopy(ulong swapchain, IList<IntPtr> images, IntPtr src)
        {
            if (!_native || swapchain == 0 || src == IntPtr.Zero || images == null || _copies >= MaxCopies) return;
            int b = _copies * CopySize;
            Marshal.WriteInt64(_job, b, (long)swapchain);
            Marshal.WriteIntPtr(_job, b + 8, src);
            int n = Math.Min(images.Count, MaxImages);
            Marshal.WriteInt32(_job, b + 16, n);
            for (int i = 0; i < n; i++) Marshal.WriteIntPtr(_job, b + 24 + i * 8, images[i]);
            _copies++;
        }

        public static void AddCopy(IntPtr dst, IntPtr src)
        {
            if (dst == IntPtr.Zero || src == IntPtr.Zero || _native) return;
            _fbDst.Add(dst); _fbSrc.Add(src);
        }

        public static void AddRelease(ulong swapchain)
        {
            if (swapchain == 0 || _native) return;
            _fbRel.Add(swapchain);
        }

        public static void Submit(ulong session, XrFrameEndInfo endInfo)
        {
            if (!_native)
            {
                for (int i = 0; i < _fbDst.Count; i++) NativeBridge.DirectCopyResource_Internal(_fbDst[i], _fbSrc[i]);
                var rel = new XrSwapchainImageReleaseInfo { type = XrStructureType.XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO };
                foreach (var sc in _fbRel) OpenXRAPI.xrReleaseSwapchainImage(sc, in rel);
                OpenXRAPI.xrEndFrame(session, in endInfo);
                _fbDst.Clear(); _fbSrc.Clear(); _fbRel.Clear();
                return;
            }
            Marshal.StructureToPtr(endInfo, _endInfo, false);
            Marshal.WriteInt32(_job, OffCopyCount, _copies);
            Marshal.WriteInt64(_job, OffSession, (long)session);
            Marshal.WriteIntPtr(_job, OffEndInfo, _endInfo);
            Marshal.WriteInt32(_job, OffDoEnd, 1);
            Marshal.WriteInt32(_job, OffDoWaitBegin, 1);
            Marshal.WriteIntPtr(_job, OffFrameState, _frameState);
            Marshal.WriteInt32(_job, OffDone, 0);
            _pending = true;
            _cb.Clear();
            _cb.IssuePluginEventAndData(_eventFunc, 1, _job);
            Graphics.ExecuteCommandBuffer(_cb);
        }

        /// <summary>Native path: start the frame loop (xrWaitFrame + xrBeginFrame on the render thread, nothing to end).</summary>
        public static void SubmitBootstrap(ulong session)
        {
            if (!_native || _pending) return;
            Clear();
            Marshal.WriteInt64(_job, OffSession, (long)session);
            Marshal.WriteInt32(_job, OffDoEnd, 0);
            Marshal.WriteInt32(_job, OffDoWaitBegin, 1);
            Marshal.WriteIntPtr(_job, OffFrameState, _frameState);
            Marshal.WriteInt32(_job, OffDone, 0);
            _pending = true;
            _cb.Clear();
            _cb.IssuePluginEventAndData(_eventFunc, 1, _job);
            Graphics.ExecuteCommandBuffer(_cb);
        }

        /// <summary>Wait until the previous frame was handed to the runtime (call before xrWaitFrame).</summary>
        public static void WaitPrevious(int timeoutMs = 250)
        {
            if (!_native || !_pending) return;
            var sw = Stopwatch.StartNew();
            while (Marshal.ReadInt32(_job, OffDone) == 0 && sw.ElapsedMilliseconds < timeoutMs) System.Threading.Thread.Sleep(0);
            bool done = Marshal.ReadInt32(_job, OffDone) != 0;
            LastWaitMs = (float)sw.Elapsed.TotalMilliseconds;
            if (done)
            {
                RtCopiesMs = Marshal.ReadInt32(_job, OffUsCopies) / 1000f;
                RtEndMs = Marshal.ReadInt32(_job, OffUsEnd) / 1000f;
                RtWaitFrameMs = Marshal.ReadInt32(_job, OffUsWait) / 1000f;
                RtBeginMs = Marshal.ReadInt32(_job, OffUsBegin) / 1000f;
            }
            if (!done)
            {
                if (_timeouts++ % 60 == 0) VRModCore.LogWarning($"[MimesisVR] Render thread did not submit the previous VR frame within {timeoutMs} ms.");
                return; // keep the job buffer untouched; try again next frame
            }
            int res = Marshal.ReadInt32(_job, OffResult);
            if (res < 0) VRModCore.LogWarning($"[MimesisVR] xrEndFrame (render thread) -> {(XrResult)res}");
            int swErr = Marshal.ReadInt32(_job, OffSwErr);
            if (swErr < 0) VRModCore.LogWarning($"[MimesisVR] swapchain op (render thread) -> {(XrResult)swErr}");
            if (Marshal.ReadInt32(_job, OffDoWaitBegin) != 0)
            {
                int br = Marshal.ReadInt32(_job, OffBeginResult);
                _frameBegun = br >= 0;
                if (_frameBegun)
                {
                    _lastFrameState = Marshal.PtrToStructure<XrFrameState>(_frameState);
                    if (!_loggedFrameLoop) { _loggedFrameLoop = true; VRModCore.Log($"[MimesisVR] Frame loop on render thread running (begin -> {(XrResult)br})."); }
                }
                else if (_beginFails++ % 60 == 0) VRModCore.LogWarning($"[MimesisVR] xrWaitFrame/xrBeginFrame (render thread) -> {(XrResult)br}");
            }
            _pending = false;
            Clear();
            Marshal.WriteInt32(_job, OffDone, 1);
        }

        /// <summary>True when a new frame may be started (previous one finished).</summary>
        public static bool ReadyForNextFrame => !_native || !_pending;
    }
}
