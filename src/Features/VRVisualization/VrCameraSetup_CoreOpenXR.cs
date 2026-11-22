using System.Runtime.InteropServices;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityVRMod.Config;
using UnityVRMod.Core;
using UnityVRMod.Features.Util;
using UnityVRMod.Features.VRVisualization.OpenXR;
using UnityVRMod.Mimesis;

namespace UnityVRMod.Features.VrVisualization
{
    internal class VrCameraSetup_CoreOpenXR : IVrCameraSetup
    {
        private ulong _xrInstance = OpenXRConstants.XR_NULL_HANDLE;
        private ulong _xrSystemId = OpenXRConstants.XR_NULL_SYSTEM_ID;
        private ulong _xrSession = OpenXRConstants.XR_NULL_HANDLE;

        public bool IsVrAvailable { get; private set; } = false;

        private GameObject _vrRig = null;
        private float _currentAppliedRigScale = 1.0f;

        private XrFrameState _xrFrameState;
        private XrSessionState _currentSessionState = XrSessionState.XR_SESSION_STATE_UNKNOWN;
        private readonly XrViewConfigurationType _viewConfigType = XrViewConfigurationType.XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO;
        private readonly List<XrViewConfigurationView> _viewConfigViews = [];

        private readonly List<long> _supportedSwapchainFormats = [];
        private long _selectedSwapchainFormat = 0;
        private readonly List<ulong> _eyeSwapchains = [];
        private readonly List<List<IntPtr>> _eyeSwapchainImages = [];
        private ulong _appSpace = OpenXRConstants.XR_NULL_HANDLE;
        private bool _isSessionRunning = false;
        private XrReferenceSpaceType _appSpaceType = XrReferenceSpaceType.XR_REFERENCE_SPACE_TYPE_LOCAL;
        private XrView[] _locatedViews;
        private XrViewState _locatedViewState;

        private Camera _leftVrCamera = null;
        private GameObject _leftVrCameraGO = null;
        private Camera _rightVrCamera = null;
        private GameObject _rightVrCameraGO = null;

        private RenderTexture _leftEyeIntermediateRT = null;
        private RenderTexture _rightEyeIntermediateRT = null;

        private IntPtr _d3d11Device = IntPtr.Zero;

        private XrCompositionLayerProjectionView[] _projectionLayerViews;
        private IntPtr _pProjectionLayerViews = IntPtr.Zero;
        private XrCompositionLayerProjection _projectionLayer;
        private IntPtr _pProjectionLayer = IntPtr.Zero;
        private IntPtr _pLayersForSubmit = IntPtr.Zero;

        private CameraClearFlags _mainCameraClearFlags;
        private Color _mainCameraBackgroundColor;
        private int _mainCameraCullingMask;

        private GameObject _currentlyTrackedOriginalCameraGO = null;
        private float _lastCalculatedVerticalOffset;
        private static CommandBuffer _flushCommandBuffer;

        private readonly List<List<IntPtr>> _eyeSwapchainSRVs = [];

        public bool InitializeVr(string applicationKey)
        {
            VRModCore.Log("Attempting to initialize OpenXR via P/Invoke...");

            try
            {
                if (!OpenXRNativeLoader.LoadOpenXRLibrary())
                    throw new Exception("Failed to load OpenXR native library (openxr_loader.dll).");

                if (!OpenXRAPI.InitializeCoreFunctions(OpenXRNativeLoader.xrGetInstanceProcAddr_ptr_delegate))
                    throw new Exception("Failed to initialize core OpenXR functions.");

                var appInfo = new XrApplicationInfo
                {
                    applicationName = "UnityVRMod",
                    applicationVersion = 1,
                    engineName = "Unity",
                    engineVersion = 1,
                    apiVersion = OpenXRConstants.XR_API_VERSION_1_1
                };

                string[] requestedExtensions = [OpenXRConstants.XR_KHR_D3D11_ENABLE_EXTENSION_NAME];
                IntPtr pRequestedExtensions = MarshallStringUtils.MarshalStringArrayToAnsi(requestedExtensions);

                var instanceCreateInfo = new XrInstanceCreateInfo
                {
                    type = XrStructureType.XR_TYPE_INSTANCE_CREATE_INFO,
                    applicationInfo = appInfo,
                    enabledExtensionCount = (uint)requestedExtensions.Length,
                    enabledExtensionNames = pRequestedExtensions
                };

                OpenXRHelper.CheckResult(OpenXRAPI.xrCreateInstance(in instanceCreateInfo, out _xrInstance), "xrCreateInstance");
                MarshallStringUtils.FreeMarshalledStringArray(pRequestedExtensions, requestedExtensions.Length);
                if (_xrInstance == OpenXRConstants.XR_NULL_HANDLE) throw new Exception("xrCreateInstance returned a null handle.");

                VRModCore.Log($"OpenXR Instance created. Handle: {_xrInstance}");

                if (!OpenXRAPI.InitializeInstanceFunctions(_xrInstance))
                    throw new Exception("Failed to initialize instance-specific OpenXR functions.");

                _d3d11Device = NativeBridge.GetD3D11DevicePointer(Texture2D.whiteTexture);
                if (_d3d11Device == IntPtr.Zero)
                    throw new Exception("Failed to get D3D11 device pointer.");

                NativeBridge.SetDevicePointerFromCSharp(_d3d11Device);
                RtSubmit.Init(_d3d11Device, _xrInstance);

                var systemGetInfo = new XrSystemGetInfo { type = XrStructureType.XR_TYPE_SYSTEM_GET_INFO, formFactor = XrFormFactor.XR_FORM_FACTOR_HEAD_MOUNTED_DISPLAY };
                OpenXRHelper.CheckResult(OpenXRAPI.xrGetSystem(_xrInstance, in systemGetInfo, out _xrSystemId), "xrGetSystem");
                if (_xrSystemId == OpenXRConstants.XR_NULL_SYSTEM_ID) throw new Exception("xrGetSystem failed for HMD.");
                VRModCore.Log($"OpenXR SystemId obtained: {_xrSystemId}");

                var d3d11GraphicsRequirements = new XrGraphicsRequirementsD3D11KHR { type = XrStructureType.XR_TYPE_GRAPHICS_REQUIREMENTS_D3D11_KHR };
                OpenXRHelper.CheckResult(OpenXRAPI.xrGetD3D11GraphicsRequirementsKHR(_xrInstance, _xrSystemId, out d3d11GraphicsRequirements), "xrGetD3D11GraphicsRequirementsKHR");

                var graphicsBinding = new XrGraphicsBindingD3D11KHR { type = XrStructureType.XR_TYPE_GRAPHICS_BINDING_D3D11_KHR, device = _d3d11Device };
                IntPtr pGraphicsBinding = Marshal.AllocHGlobal(Marshal.SizeOf(graphicsBinding));
                Marshal.StructureToPtr(graphicsBinding, pGraphicsBinding, false);
                var sessionCreateInfo = new XrSessionCreateInfo { type = XrStructureType.XR_TYPE_SESSION_CREATE_INFO, next = pGraphicsBinding, systemId = _xrSystemId };
                OpenXRHelper.CheckResult(OpenXRAPI.xrCreateSession(_xrInstance, in sessionCreateInfo, out _xrSession), "xrCreateSession");
                Marshal.FreeHGlobal(pGraphicsBinding);
                if (_xrSession == OpenXRConstants.XR_NULL_HANDLE) throw new Exception("xrCreateSession returned a null handle.");
                VRModCore.Log($"OpenXR Session created. Handle: {_xrSession}");

                var sessionBeginInfo = new XrSessionBeginInfo { type = XrStructureType.XR_TYPE_SESSION_BEGIN_INFO, primaryViewConfigurationType = _viewConfigType };
                OpenXRHelper.CheckResult(OpenXRAPI.xrBeginSession(_xrSession, in sessionBeginInfo), "xrBeginSession");
                _currentSessionState = XrSessionState.XR_SESSION_STATE_READY;
                _isSessionRunning = true;
                VRModCore.Log("OpenXR Session begun.");

                _controllers = new XrControllerInput();
                if (_controllers.Initialize(_xrInstance, _xrSession)) MimesisInput.Controllers = _controllers;

                var refSpaceCreateInfo = new XrReferenceSpaceCreateInfo { type = XrStructureType.XR_TYPE_REFERENCE_SPACE_CREATE_INFO, referenceSpaceType = XrReferenceSpaceType.XR_REFERENCE_SPACE_TYPE_STAGE, poseInReferenceSpace = new XrPosef { orientation = new XrQuaternionf { w = 1f } } };
                if (OpenXRAPI.xrCreateReferenceSpace(_xrSession, in refSpaceCreateInfo, out _appSpace) < 0)
                {
                    VRModCore.LogWarning("Failed to create STAGE space. Trying LOCAL.");
                    _appSpaceType = XrReferenceSpaceType.XR_REFERENCE_SPACE_TYPE_LOCAL;
                    refSpaceCreateInfo.referenceSpaceType = _appSpaceType;
                    OpenXRHelper.CheckResult(OpenXRAPI.xrCreateReferenceSpace(_xrSession, in refSpaceCreateInfo, out _appSpace), "xrCreateReferenceSpace_Fallback");
                }
                else
                {
                    _appSpaceType = XrReferenceSpaceType.XR_REFERENCE_SPACE_TYPE_STAGE;
                }
                if (_appSpace == OpenXRConstants.XR_NULL_HANDLE) throw new Exception("Failed to create any reference space.");
                VRModCore.Log($"Created {_appSpaceType} space.");

                InitializeViews();
                InitializeSwapchains();

                if (_viewConfigViews.Count > 0)
                {
                    _pProjectionLayerViews = Marshal.AllocHGlobal(Marshal.SizeOf<XrCompositionLayerProjectionView>() * _viewConfigViews.Count);
                    _projectionLayer = new XrCompositionLayerProjection { type = XrStructureType.XR_TYPE_COMPOSITION_LAYER_PROJECTION };
                    _pProjectionLayer = Marshal.AllocHGlobal(Marshal.SizeOf<XrCompositionLayerProjection>());
                    _pLayersForSubmit = Marshal.AllocHGlobal(Marshal.SizeOf<IntPtr>() * 12);
                    Marshal.WriteIntPtr(_pLayersForSubmit, _pProjectionLayer);
                }

                _flushCommandBuffer ??= new CommandBuffer
                    {
                        name = "VRModFlush"
                    };

                VRModCore.Log("OpenXR fully initialized.");
                IsVrAvailable = true;
                return true;
            }
            catch (Exception ex)
            {
                VRModCore.LogError("Exception during OpenXR initialization:", ex);
                TeardownVr();
                return false;
            }
        }

        private void InitializeViews()
        {
            OpenXRAPI.xrEnumerateViewConfigurations(_xrInstance, _xrSystemId, 0, out uint viewConfigCountOutput, IntPtr.Zero);
            if (viewConfigCountOutput == 0) throw new Exception("No view configurations available.");

            OpenXRAPI.xrEnumerateViewConfigurationViews(_xrInstance, _xrSystemId, _viewConfigType, 0, out uint viewCountOutput, IntPtr.Zero);
            if (viewCountOutput == 0) throw new Exception("No views for primary stereo config.");

            _viewConfigViews.Clear();
            IntPtr pViewStructs = Marshal.AllocHGlobal((int)(viewCountOutput * Marshal.SizeOf<XrViewConfigurationView>()));
            try
            {
                for (int i = 0; i < viewCountOutput; ++i) Marshal.StructureToPtr(new XrViewConfigurationView { type = XrStructureType.XR_TYPE_VIEW_CONFIGURATION_VIEW }, pViewStructs + (i * Marshal.SizeOf<XrViewConfigurationView>()), false);
                OpenXRAPI.xrEnumerateViewConfigurationViews(_xrInstance, _xrSystemId, _viewConfigType, viewCountOutput, out viewCountOutput, pViewStructs);
                for (int i = 0; i < viewCountOutput; ++i) _viewConfigViews.Add(Marshal.PtrToStructure<XrViewConfigurationView>(pViewStructs + (i * Marshal.SizeOf<XrViewConfigurationView>())));
            }
            finally { Marshal.FreeHGlobal(pViewStructs); }

            if (_viewConfigViews.Count > 0)
            {
                _locatedViews = new XrView[_viewConfigViews.Count];
                for (int i = 0; i < _locatedViews.Length; i++) _locatedViews[i].type = XrStructureType.XR_TYPE_VIEW;

                _projectionLayerViews = new XrCompositionLayerProjectionView[_viewConfigViews.Count];
                for (int i = 0; i < _projectionLayerViews.Length; i++) _projectionLayerViews[i].type = XrStructureType.XR_TYPE_COMPOSITION_LAYER_PROJECTION_VIEW;
            }
            VRModCore.LogRuntimeDebug("View configurations enumerated.");
        }

        private void InitializeSwapchains()
        {
            OpenXRAPI.xrEnumerateSwapchainFormats(_xrSession, 0, out uint formatCount, null);
            if (formatCount == 0) throw new Exception("No swapchain formats.");
            long[] formatsArray = new long[formatCount];
            OpenXRAPI.xrEnumerateSwapchainFormats(_xrSession, formatCount, out formatCount, formatsArray);
            _supportedSwapchainFormats.Clear();
            _supportedSwapchainFormats.AddRange(formatsArray);

            long DXGI_FORMAT_B8G8R8A8_UNORM_SRGB = 91;
            if (_supportedSwapchainFormats.Contains(DXGI_FORMAT_B8G8R8A8_UNORM_SRGB))
                _selectedSwapchainFormat = DXGI_FORMAT_B8G8R8A8_UNORM_SRGB;
            else if (_supportedSwapchainFormats.Count > 0)
                _selectedSwapchainFormat = _supportedSwapchainFormats[0];
            else
                throw new Exception("No suitable swapchain format found.");

            VRModCore.Log($"Selected Swapchain Format (DXGI): {_selectedSwapchainFormat}");

            _eyeSwapchains.Clear();
            _eyeSwapchainImages.Clear();
            _eyeSwapchainSRVs.Clear();

            for (int i = 0; i < _viewConfigViews.Count; i++)
            {
                var view = _viewConfigViews[i];
                var swapchainCreateInfo = new XrSwapchainCreateInfo
                {
                    type = XrStructureType.XR_TYPE_SWAPCHAIN_CREATE_INFO,
                    usageFlags = XrSwapchainUsageFlags.XR_SWAPCHAIN_USAGE_COLOR_ATTACHMENT_BIT | XrSwapchainUsageFlags.XR_SWAPCHAIN_USAGE_SAMPLED_BIT,
                    format = _selectedSwapchainFormat,
                    sampleCount = view.recommendedSwapchainSampleCount,
                    width = view.recommendedImageRectWidth,
                    height = view.recommendedImageRectHeight,
                    faceCount = 1,
                    arraySize = 1,
                    mipCount = 1
                };
                OpenXRHelper.CheckResult(OpenXRAPI.xrCreateSwapchain(_xrSession, in swapchainCreateInfo, out ulong scHandle), "xrCreateSwapchain");
                _eyeSwapchains.Add(scHandle);

                OpenXRAPI.xrEnumerateSwapchainImages(scHandle, 0, out uint imgCount, IntPtr.Zero);
                IntPtr scImagesPtr = Marshal.AllocHGlobal((int)(imgCount * Marshal.SizeOf<XrSwapchainImageD3D11KHR>()));
                List<IntPtr> currentEyeTexList = [];
                List<IntPtr> currentEyeSrvList = [];
                try
                {
                    for (int j = 0; j < imgCount; ++j) Marshal.StructureToPtr(new XrSwapchainImageD3D11KHR { type = XrStructureType.XR_TYPE_SWAPCHAIN_IMAGE_D3D11_KHR }, scImagesPtr + (j * Marshal.SizeOf<XrSwapchainImageD3D11KHR>()), false);
                    OpenXRAPI.xrEnumerateSwapchainImages(scHandle, imgCount, out imgCount, scImagesPtr);
                    for (int j = 0; j < imgCount; j++)
                    {
                        var swapchainImage = Marshal.PtrToStructure<XrSwapchainImageD3D11KHR>(scImagesPtr + (j * Marshal.SizeOf<XrSwapchainImageD3D11KHR>()));
                        currentEyeTexList.Add(swapchainImage.texture);
                        int hResultSrv = NativeBridge.CreateAndRegisterSRV_Internal(swapchainImage.texture, (int)_selectedSwapchainFormat, out IntPtr srvPtr);
                        if (hResultSrv == 0 && srvPtr != IntPtr.Zero)
                            currentEyeSrvList.Add(srvPtr);
                        else
                            currentEyeSrvList.Add(IntPtr.Zero);
                    }
                }
                finally { Marshal.FreeHGlobal(scImagesPtr); }

                _eyeSwapchainImages.Add(currentEyeTexList);
                _eyeSwapchainSRVs.Add(currentEyeSrvList);
                VRModCore.Log($"Swapchain for view {i} created. Images: {currentEyeTexList.Count}");
            }
        }

        public void UpdatePoses()
        {
            if (!IsVrAvailable || _xrSession == OpenXRConstants.XR_NULL_HANDLE) return;

            // --- RESTORED: Full event polling and session state management ---
            IntPtr eventBufferPtr = IntPtr.Zero;
            try
            {
                int eventBufferSize = XrEventDataBuffer.GetSize();
                eventBufferPtr = Marshal.AllocHGlobal(eventBufferSize);
                while (true)
                {
                    var headerForInput = new XrEventDataBaseHeader { type = XrStructureType.XR_TYPE_EVENT_DATA_BUFFER };
                    Marshal.StructureToPtr(headerForInput, eventBufferPtr, false);
                    XrResult pollResult = OpenXRAPI.xrPollEvent(_xrInstance, eventBufferPtr);
                    if (pollResult == XrResult.XR_EVENT_UNAVAILABLE) break;

                    if (pollResult < 0) { IsVrAvailable = false; break; }

                    var actualEventType = (XrStructureType)Marshal.ReadInt32(eventBufferPtr);
                    if ((int)actualEventType == 40) { VRModCore.Log("[MimesisVR] Reference space change (system recenter) -> recentering."); RequestRecenter(); }
                    if (actualEventType == XrStructureType.XR_TYPE_EVENT_DATA_SESSION_STATE_CHANGED)
                    {
                        var stateEvent = Marshal.PtrToStructure<XrEventDataSessionStateChanged>(eventBufferPtr);
                        if (stateEvent.state != _currentSessionState)
                        {
                            VRModCore.Log($"OpenXR Session State Changed: {_currentSessionState} -> {stateEvent.state}");
                            _currentSessionState = stateEvent.state;

                            switch (_currentSessionState)
                            {
                                case XrSessionState.XR_SESSION_STATE_EXITING:
                                case XrSessionState.XR_SESSION_STATE_LOSS_PENDING:
                                    _isSessionRunning = false;
                                    IsVrAvailable = false;
                                    break;
                                case XrSessionState.XR_SESSION_STATE_STOPPING:
                                    if (_isSessionRunning)
                                    {
                                        VRModCore.Log("Session is stopping, calling xrEndSession.");
                                        RtSubmit.WaitPrevious(500);
                                        RtSubmit.ResetFrame();
                                        OpenXRAPI.xrEndSession(_xrSession);
                                        _isSessionRunning = false;
                                    }
                                    break;
                                case XrSessionState.XR_SESSION_STATE_READY:
                                    if (!_isSessionRunning)
                                    {
                                        VRModCore.Log("Session is ready, calling xrBeginSession to resume.");
                                        var beginInfo = new XrSessionBeginInfo { type = XrStructureType.XR_TYPE_SESSION_BEGIN_INFO, primaryViewConfigurationType = _viewConfigType };
                                        if (OpenXRAPI.xrBeginSession(_xrSession, in beginInfo) == XrResult.XR_SUCCESS)
                                        {
                                            _isSessionRunning = true;
                                        }
                                    }
                                    break;
                            }
                        }
                    }
                }
            }
            finally { if (eventBufferPtr != IntPtr.Zero) Marshal.FreeHGlobal(eventBufferPtr); }

            if (!_isSessionRunning) return;

            MimesisWatchdog.Start();
            MimesisWatchdog.Armed = true;
            MimesisWatchdog.MainStage = 1;
            var perfSw = System.Diagnostics.Stopwatch.StartNew();
            RtSubmit.WaitPrevious();
            if (!RtSubmit.ReadyForNextFrame) return;
            MimesisWatchdog.MainStage = 2;
            if (RtSubmit.NativeActive)
            {
                // xrWaitFrame/xrBeginFrame run on the render thread right after the previous xrEndFrame,
                // so the main thread never calls into the runtime's D3D11 code while Unity renders.
                if (!RtSubmit.FrameBegun) { RtSubmit.SubmitBootstrap(_xrSession); return; }
                _xrFrameState = RtSubmit.LastFrameState;
                MimesisWatchdog.MainStage = 3;
            }
            else
            {
                var frameWaitInfo = new XrFrameWaitInfo { type = XrStructureType.XR_TYPE_FRAME_WAIT_INFO };
                _xrFrameState.type = XrStructureType.XR_TYPE_FRAME_STATE;
                OpenXRAPI.xrWaitFrame(_xrSession, in frameWaitInfo, out _xrFrameState);

                MimesisWatchdog.MainStage = 3;
                var frameBeginInfo = new XrFrameBeginInfo { type = XrStructureType.XR_TYPE_FRAME_BEGIN_INFO };
                if (OpenXRAPI.xrBeginFrame(_xrSession, in frameBeginInfo) == XrResult.XR_FRAME_DISCARDED)
                {
                    var discardedFrameEndInfo = new XrFrameEndInfo { type = XrStructureType.XR_TYPE_FRAME_END_INFO, displayTime = _xrFrameState.predictedDisplayTime, layerCount = 0, layers = IntPtr.Zero, environmentBlendMode = XrEnvironmentBlendMode.XR_ENVIRONMENT_BLEND_MODE_OPAQUE };
                    OpenXRAPI.xrEndFrame(_xrSession, in discardedFrameEndInfo);
                    return;
                }
            }

            var frameEndInfo = new XrFrameEndInfo { type = XrStructureType.XR_TYPE_FRAME_END_INFO, displayTime = _xrFrameState.predictedDisplayTime, environmentBlendMode = XrEnvironmentBlendMode.XR_ENVIRONMENT_BLEND_MODE_OPAQUE, layerCount = 0, layers = IntPtr.Zero };

            if (_xrFrameState.shouldRender == XrBool32.XR_TRUE)
            {
                MimesisWatchdog.MainStage = 4;
                var viewLocateInfo = new XrViewLocateInfo { type = XrStructureType.XR_TYPE_VIEW_LOCATE_INFO, viewConfigurationType = _viewConfigType, displayTime = _xrFrameState.predictedDisplayTime, space = _appSpace };
                _locatedViewState.type = XrStructureType.XR_TYPE_VIEW_STATE;

                IntPtr pLocatedViews = Marshal.AllocHGlobal(Marshal.SizeOf<XrView>() * _locatedViews.Length);
                try
                {
                    for (int i = 0; i < _locatedViews.Length; i++) Marshal.StructureToPtr(_locatedViews[i], pLocatedViews + (i * Marshal.SizeOf<XrView>()), false);
                    OpenXRAPI.xrLocateViews(_xrSession, in viewLocateInfo, out _locatedViewState, (uint)_locatedViews.Length, out _, pLocatedViews);
                    for (int i = 0; i < _locatedViews.Length; i++) _locatedViews[i] = Marshal.PtrToStructure<XrView>(pLocatedViews + (i * Marshal.SizeOf<XrView>()));
                }
                finally { Marshal.FreeHGlobal(pLocatedViews); }

                bool poseIsValid = (_locatedViewState.viewStateFlags & XrViewStateFlags.XR_VIEW_STATE_ORIENTATION_VALID_BIT) != 0;
                if (_controllers != null && _controllers.Ready && _currentSessionState == XrSessionState.XR_SESSION_STATE_FOCUSED)
                    _controllers.Update(_appSpace, _xrFrameState.predictedDisplayTime);
                if (poseIsValid) FollowGameCamera();
                MimesisWatchdog.MainStage = 5;
                MimesisVrExtras.BeforeEyeRender(this);
                MimesisPerf.TPre = (float)perfSw.Elapsed.TotalMilliseconds;

                if (poseIsValid)
                {
                    uint acquiredIdx0 = 0, acquiredIdx1 = 0;
                    if (!RtSubmit.NativeActive)
                    {
                        var acquireInfo = new XrSwapchainImageAcquireInfo { type = XrStructureType.XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO };
                        OpenXRAPI.xrAcquireSwapchainImage(_eyeSwapchains[0], in acquireInfo, out acquiredIdx0);
                        OpenXRAPI.xrAcquireSwapchainImage(_eyeSwapchains[1], in acquireInfo, out acquiredIdx1);

                        var waitInfo = new XrSwapchainImageWaitInfo { type = XrStructureType.XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO, timeout = OpenXRConstants.XR_INFINITE_DURATION };
                        OpenXRAPI.xrWaitSwapchainImage(_eyeSwapchains[0], in waitInfo);
                        OpenXRAPI.xrWaitSwapchainImage(_eyeSwapchains[1], in waitInfo);
                    }

                    MimesisVrExtras.PreEyes(_leftVrCamera, _rightVrCamera);
                    try
                    {
                        MimesisWatchdog.MainStage = 6;
                        RenderEye(0, acquiredIdx0);
                        MimesisPerf.TLeft = (float)perfSw.Elapsed.TotalMilliseconds;
                        MimesisWatchdog.MainStage = 7;
                        RenderEye(1, acquiredIdx1);
                        MimesisPerf.TRight = (float)perfSw.Elapsed.TotalMilliseconds;
                    }
                    finally { MimesisVrExtras.PostEyes(); }

                    RtSubmit.AddRelease(_eyeSwapchains[0]);
                    RtSubmit.AddRelease(_eyeSwapchains[1]);

                    PopulateProjectionLayer();
                    frameEndInfo.layerCount = 1;
                    frameEndInfo.layers = _pLayersForSubmit;
                    MimesisWatchdog.MainStage = 8;
                    frameEndInfo.layerCount = SubmitQuadLayers();
                    // build 45: after the layers, so the monitor can show them too (stream)
                    bool mirrorRight = MimesisVrExtras.MirrorRightEye && _rightEyeIntermediateRT != null && _rightVrCamera != null; // build 45: right eye by default
                    MimesisVrExtras.MirrorToScreen(mirrorRight ? _rightEyeIntermediateRT : _leftEyeIntermediateRT, mirrorRight ? _rightVrCamera : _leftVrCamera, _vrRig != null ? _vrRig.transform : null);
                }
            }

            MimesisWatchdog.MainStage = 9;
            RtSubmit.Submit(_xrSession, frameEndInfo);
            MimesisWatchdog.MainStage = 10;
            MimesisWatchdog.Frames++;
            MimesisPerf.FrameDone((float)perfSw.Elapsed.TotalMilliseconds, _xrFrameState.predictedDisplayPeriod);
        }

        private void RenderEye(int eyeIndex, uint swapchainImageIndex)
        {
            Camera currentEyeCamera = (eyeIndex == 0) ? _leftVrCamera : _rightVrCamera;
            // MIMESIS VR: draw distance and post-processing options (CPU/GPU savers)
            {
                float baseFar = _refFar;
                if (baseFar <= 0f && MimesisInput.GameCamera != null && MimesisInput.GameCamera.TryGetComponent<Camera>(out var gcam)) baseFar = gcam.farClipPlane;
                if (baseFar > 0f)
                {
                    float far = MimesisImage.DrawDistance > 0f ? Mathf.Min(baseFar, MimesisImage.DrawDistance) : baseFar;
                    far = Mathf.Max(currentEyeCamera.nearClipPlane * 20f, far);
                    if (!Mathf.Approximately(currentEyeCamera.farClipPlane, far))
                    {
                        currentEyeCamera.farClipPlane = far;
                        if (eyeIndex == 0) VRModCore.Log($"[MimesisVR] Eye far clip {far:F0} m (game {baseFar:F0} m)");
                    }
                }
            }
            var eyeData = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(currentEyeCamera);
            if (eyeData != null) { bool post = _gamePost && MimesisImage.EyePost; if (eyeData.renderPostProcessing != post) eyeData.renderPostProcessing = post; }
            RenderTexture currentIntermediateRT = (eyeIndex == 0) ? _leftEyeIntermediateRT : _rightEyeIntermediateRT;
            IntPtr nativeTextureResourcePtr = _eyeSwapchainImages[eyeIndex][(int)swapchainImageIndex];
            XrViewConfigurationView viewConfig = _viewConfigViews[eyeIndex];

            int wantMsaa = MimesisVrExtras.LegacyFlip ? 1 : MimesisVrExtras.MsaaSamples;
            float rs = MimesisVrExtras.LegacyFlip ? 1f : Mathf.Clamp(MimesisImage.RenderScale, 0.4f, 1f);
            int fullW = (int)viewConfig.recommendedImageRectWidth, fullH = (int)viewConfig.recommendedImageRectHeight;
            int wantW = Mathf.Max(64, Mathf.RoundToInt(fullW * rs)), wantH = Mathf.Max(64, Mathf.RoundToInt(fullH * rs));
            bool rtNeedsRecreation = (currentIntermediateRT == null || !currentIntermediateRT.IsCreated() || currentIntermediateRT.width != wantW || currentIntermediateRT.height != wantH || currentIntermediateRT.antiAliasing != wantMsaa);
            if (rtNeedsRecreation)
            {
                if (currentIntermediateRT != null) { currentIntermediateRT.Release(); UnityEngine.Object.Destroy(currentIntermediateRT); }

                var renderTextureFormat = QualitySettings.activeColorSpace == ColorSpace.Linear
                    ? GraphicsFormat.B8G8R8A8_SRGB
                    : GraphicsFormat.B8G8R8A8_UNorm;

                VRModCore.LogSpammyDebug($"Creating intermediate RenderTexture with format: {renderTextureFormat}");
                currentIntermediateRT = new RenderTexture(wantW, wantH, 24, renderTextureFormat);
                currentIntermediateRT.antiAliasing = wantMsaa;
                VRModCore.Log($"[MimesisVR] Eye {eyeIndex} RT {currentIntermediateRT.width}x{currentIntermediateRT.height}, MSAA x{wantMsaa}");

                if (eyeIndex == 0) _leftEyeIntermediateRT = currentIntermediateRT; else _rightEyeIntermediateRT = currentIntermediateRT;
            }

            currentEyeCamera.targetTexture = currentIntermediateRT;
            currentEyeCamera.clearFlags = _mainCameraClearFlags;
            if (_mainCameraClearFlags == CameraClearFlags.SolidColor) { currentEyeCamera.backgroundColor = _mainCameraBackgroundColor; }
            if (MimesisVrExtras.MenuBackdrop) { currentEyeCamera.clearFlags = CameraClearFlags.SolidColor; currentEyeCamera.backgroundColor = new Color(0.035f, 0.035f, 0.045f, 1f); }
            currentEyeCamera.cullingMask = _mainCameraCullingMask | MimesisVrExtras.VrOnlyLayerMask;
            currentEyeCamera.enabled = true;

            XrPosef eyePose = _locatedViews[eyeIndex].pose;
            Vector3 position = new(eyePose.position.x, eyePose.position.y, -eyePose.position.z);
            Quaternion rotation = new(eyePose.orientation.x, eyePose.orientation.y, -eyePose.orientation.z, -eyePose.orientation.w);
#if MONO
            currentEyeCamera.transform.localPosition = position;
            currentEyeCamera.transform.localRotation = rotation;
#elif CPP
            currentEyeCamera.transform.SetLocalPositionAndRotation(position, rotation);
#endif

            Matrix4x4 projM = CreateProjectionMatrixFromFovUsingFrustum(_locatedViews[eyeIndex].fov, currentEyeCamera.nearClipPlane, currentEyeCamera.farClipPlane);
            bool legacyFlip = MimesisVrExtras.LegacyFlip;
            if (legacyFlip) projM = Matrix4x4.Scale(new Vector3(1, -1, 1)) * projM;
            currentEyeCamera.projectionMatrix = projM;

            bool originalInvertCulling = GL.invertCulling;
            if (legacyFlip) GL.invertCulling = true;
            currentEyeCamera.Render();
            GL.invertCulling = originalInvertCulling;
            if (!legacyFlip)
            {
                var fov = _locatedViews[eyeIndex].fov;
                MimesisHud.CompositeFx(currentIntermediateRT, fov.angleLeft, fov.angleRight, fov.angleUp, fov.angleDown);
                MimesisWatches.CompositeDamage(currentIntermediateRT);
            }

            RenderTexture sourceRT = currentIntermediateRT;
            if (!legacyFlip && currentIntermediateRT != null)
            {
                RenderTexture flipRT = (eyeIndex == 0) ? _leftFlipRT : _rightFlipRT;
                // Always the swapchain size: a lower render scale is stretched up by this blit
                if (flipRT == null || flipRT.width != fullW || flipRT.height != fullH)
                {
                    if (flipRT != null) { flipRT.Release(); UnityEngine.Object.Destroy(flipRT); }
                    flipRT = new RenderTexture(fullW, fullH, 0, currentIntermediateRT.graphicsFormat);
                    flipRT.Create();
                    if (eyeIndex == 0) _leftFlipRT = flipRT; else _rightFlipRT = flipRT;
                }
                Graphics.Blit(currentIntermediateRT, flipRT, new Vector2(1f, -1f), new Vector2(0f, 1f));
                sourceRT = flipRT;
            }
            Graphics.ExecuteCommandBuffer(_flushCommandBuffer);
            RenderTexture.active = null;

            if (sourceRT != null && sourceRT.IsCreated())
            {
                IntPtr sourceNativePtr = sourceRT.GetNativeTexturePtr();
                if (sourceNativePtr != IntPtr.Zero && (RtSubmit.NativeActive || nativeTextureResourcePtr != IntPtr.Zero))
                {
                    if (RtSubmit.NativeActive) RtSubmit.AddSwapchainCopy(_eyeSwapchains[eyeIndex], _eyeSwapchainImages[eyeIndex], sourceNativePtr);
                    else RtSubmit.AddCopy(nativeTextureResourcePtr, sourceNativePtr);
                }
            }
        }

        private void PopulateProjectionLayer()
        {
            for (int i = 0; i < 2; i++)
            {
                _projectionLayerViews[i].pose = _locatedViews[i].pose;
                _projectionLayerViews[i].fov = _locatedViews[i].fov;
                _projectionLayerViews[i].subImage.swapchain = _eyeSwapchains[i];
                _projectionLayerViews[i].subImage.imageRect.offset = new XrOffset2Di { x = 0, y = 0 };
                _projectionLayerViews[i].subImage.imageRect.extent.width = (int)_viewConfigViews[i].recommendedImageRectWidth;
                _projectionLayerViews[i].subImage.imageRect.extent.height = (int)_viewConfigViews[i].recommendedImageRectHeight;
                _projectionLayerViews[i].subImage.imageArrayIndex = 0;
                Marshal.StructureToPtr(_projectionLayerViews[i], _pProjectionLayerViews + (i * Marshal.SizeOf<XrCompositionLayerProjectionView>()), false);
            }
            _projectionLayer.layerFlags = XrCompositionLayerFlags.None;
            _projectionLayer.space = _appSpace;
            _projectionLayer.viewCount = (uint)_viewConfigViews.Count;
            _projectionLayer.views = _pProjectionLayerViews;
            Marshal.StructureToPtr(_projectionLayer, _pProjectionLayer, false);
        }

        // ---- MIMESIS VR: rig follows the game camera ----
        private XrControllerInput _controllers;
        private RenderTexture _leftFlipRT, _rightFlipRT;
        public XrControllerInput Controllers => _controllers;
        public Transform RigTransform => _vrRig != null ? _vrRig.transform : null;
        private bool _hasRecenter;
        private Vector3 _recenterHeadLocal;
        private float _recenterYaw;

        public void RequestRecenter() { _hasRecenter = false; MimesisVrExtras.RequestPanelReplace(); }

        // ---- MIMESIS VR: menu panel and HUD as OpenXR quad layers ----
        private XrQuadLayer _panelLayer, _hudLayer, _laserLayer, _gearLayer, _settingsLayer, _fpsLayer, _watchLayer;
        private readonly Dictionary<string, XrQuadLayer> _segLayers = new();

        /// <summary>Appends quad layers after the projection layer. Returns total layer count.</summary>
        private uint SubmitQuadLayers()
        {
            if (_pLayersForSubmit == IntPtr.Zero) return 1;
            _panelLayer ??= new XrQuadLayer("Panel");
            _hudLayer ??= new XrQuadLayer("HUD");
            int count = 1;
            MimesisStream.BeginFrame();
            if (MimesisVrExtras.PanelLayerActive)
            {
                if (_panelLayer.Submit(_xrSession, _appSpace, _selectedSwapchainFormat, MimesisVrExtras.PanelOutput, MimesisVrExtras.PanelLocalPos, MimesisVrExtras.PanelLocalRot, MimesisVrExtras.PanelSize, MimesisVrExtras.PanelLayerFlags))
                { Marshal.WriteIntPtr(_pLayersForSubmit + IntPtr.Size * count, _panelLayer.LayerPtr); count++; }
                else if (_panelLayer.Failed) MimesisVrExtras.QuadLayerUnavailable = true;
            }
            if (MimesisHud.Active)
            {
                if (_hudLayer.Submit(_xrSession, _appSpace, _selectedSwapchainFormat, MimesisHud.Output, MimesisHud.LocalPos, MimesisHud.LocalRot, MimesisHud.Size, XrQuadLayer.FLAG_BLEND_SOURCE_ALPHA))
                { Marshal.WriteIntPtr(_pLayersForSubmit + IntPtr.Size * count, _hudLayer.LayerPtr); count++; }
            }
            foreach (var seg in MimesisHudSegments.All)
            {
                if (!seg.Active || seg.Tex == null) continue;
                if (!_segLayers.TryGetValue(seg.Name, out var layer)) { layer = new XrQuadLayer("HUD-" + seg.Name); _segLayers[seg.Name] = layer; }
                var segTex = seg.DrawTex != null ? seg.DrawTex : seg.Tex;
                int segW = seg.DrawTex != null ? seg.DrawW : seg.W, segH = seg.DrawTex != null ? seg.DrawH : seg.H;
                if (count < 9 && layer.Submit(_xrSession, _appSpace, _selectedSwapchainFormat, segTex, seg.LocalPos, seg.LocalRot, seg.Size, XrQuadLayer.FLAG_BLEND_SOURCE_ALPHA, segW, segH))
                { Marshal.WriteIntPtr(_pLayersForSubmit + IntPtr.Size * count, layer.LayerPtr); count++; MimesisStream.Add(segTex, seg.LocalPos, seg.LocalRot, seg.Size, segW, segH); }
            }
            if (MimesisWatches.ScreenActive && count < 10)
            {
                _watchLayer ??= new XrQuadLayer("WatchR");
                if (_watchLayer.Submit(_xrSession, _appSpace, _selectedSwapchainFormat, MimesisWatches.ScreenTex, MimesisWatches.ScreenPos, MimesisWatches.ScreenRot, MimesisWatches.ScreenSize, XrQuadLayer.FLAG_BLEND_SOURCE_ALPHA))
                { Marshal.WriteIntPtr(_pLayersForSubmit + IntPtr.Size * count, _watchLayer.LayerPtr); count++; MimesisStream.Add(MimesisWatches.ScreenTex, MimesisWatches.ScreenPos, MimesisWatches.ScreenRot, MimesisWatches.ScreenSize); }
            }
            if (MimesisSettingsUI.GearActive && count < 11)
            {
                _gearLayer ??= new XrQuadLayer("Gear");
                if (_gearLayer.Submit(_xrSession, _appSpace, _selectedSwapchainFormat, MimesisSettingsUI.GearTex, MimesisSettingsUI.GearLocalPos, MimesisSettingsUI.GearLocalRot, MimesisSettingsUI.GearSize, XrQuadLayer.FLAG_BLEND_SOURCE_ALPHA))
                { Marshal.WriteIntPtr(_pLayersForSubmit + IntPtr.Size * count, _gearLayer.LayerPtr); count++; MimesisStream.Add(MimesisSettingsUI.GearTex, MimesisSettingsUI.GearLocalPos, MimesisSettingsUI.GearLocalRot, MimesisSettingsUI.GearSize); }
            }
            if (MimesisSettingsUI.PanelActive && count < 11)
            {
                _settingsLayer ??= new XrQuadLayer("Settings");
                if (_settingsLayer.Submit(_xrSession, _appSpace, _selectedSwapchainFormat, MimesisSettingsUI.PanelTex, MimesisSettingsUI.PanelLocalPos, MimesisSettingsUI.PanelLocalRot, MimesisSettingsUI.PanelSize, XrQuadLayer.FLAG_BLEND_SOURCE_ALPHA))
                { Marshal.WriteIntPtr(_pLayersForSubmit + IntPtr.Size * count, _settingsLayer.LayerPtr); count++; MimesisStream.Add(MimesisSettingsUI.PanelTex, MimesisSettingsUI.PanelLocalPos, MimesisSettingsUI.PanelLocalRot, MimesisSettingsUI.PanelSize); }
            }
            if (MimesisFps.Active && count < 11)
            {
                _fpsLayer ??= new XrQuadLayer("Fps");
                if (_fpsLayer.Submit(_xrSession, _appSpace, _selectedSwapchainFormat, MimesisFps.Tex, MimesisFps.LocalPos, MimesisFps.LocalRot, MimesisFps.Size, XrQuadLayer.FLAG_BLEND_SOURCE_ALPHA))
                { Marshal.WriteIntPtr(_pLayersForSubmit + IntPtr.Size * count, _fpsLayer.LayerPtr); count++; MimesisStream.Add(MimesisFps.Tex, MimesisFps.LocalPos, MimesisFps.LocalRot, MimesisFps.Size); }
            }
            if (MimesisVrExtras.LaserLayerActive && count < 12)
            {
                _laserLayer ??= new XrQuadLayer("Laser");
                if (_laserLayer.Submit(_xrSession, _appSpace, _selectedSwapchainFormat, MimesisVrExtras.LaserTex, MimesisVrExtras.LaserLocalPos, MimesisVrExtras.LaserLocalRot, MimesisVrExtras.LaserSize, XrQuadLayer.FLAG_BLEND_SOURCE_ALPHA))
                { Marshal.WriteIntPtr(_pLayersForSubmit + IntPtr.Size * count, _laserLayer.LayerPtr); count++; MimesisStream.Add(MimesisVrExtras.LaserTex, MimesisVrExtras.LaserLocalPos, MimesisVrExtras.LaserLocalRot, MimesisVrExtras.LaserSize); }
            }
            return (uint)count;
        }

        /// <summary>Re-place the rig on the game camera now (used before rendering so the character arms match).</summary>
        public void RefollowNow()
        {
            try { if (_vrRig != null && _isSessionRunning) FollowGameCamera(); } catch { }
        }

        private void FollowGameCamera()
        {
            if (_vrRig == null || _currentlyTrackedOriginalCameraGO == null) return;
            Transform anchor = _currentlyTrackedOriginalCameraGO.transform;

            var p0 = _locatedViews[0].pose; var p1 = _locatedViews[1].pose;
            Vector3 head = (new Vector3(p0.position.x, p0.position.y, -p0.position.z) + new Vector3(p1.position.x, p1.position.y, -p1.position.z)) * 0.5f;
            Quaternion headRot = new(p0.orientation.x, p0.orientation.y, -p0.orientation.z, -p0.orientation.w);

            if (!_hasRecenter && head.sqrMagnitude > 1e-6f)
            {
                _recenterHeadLocal = head;
                _recenterYaw = headRot.eulerAngles.y;
                _hasRecenter = true;
                MimesisInput.RigYaw = anchor.eulerAngles.y - _recenterYaw;
                MimesisInput.RigYawInitialized = true;
                VRModCore.Log($"[MimesisVR] Recentered. Head local pos {head}, yaw {_recenterYaw:F1}. Anchor '{anchor.name}' at {anchor.position}.");
            }

            float s = _currentAppliedRigScale;
            Quaternion rigRot = Quaternion.Euler(0f, MimesisInput.RigYaw, 0f);
            if (_hasRecenter) MimesisRoomScale.Step(anchor, rigRot, s, ref _recenterHeadLocal, head);
            MimesisInput.HeadWorldRot = rigRot * headRot;
            MimesisInput.GameCamera = anchor;
            MimesisInput.HeadLocalPos = head;
            MimesisInput.HeadLocalRot = headRot;
            MimesisInput.HeadValid = true;
            MimesisInput.VrActive = true;
            Vector3 pos = anchor.position - rigRot * (_recenterHeadLocal * s);
            pos.y += (ConfigManager.VrUserEyeHeightOffset.Value + MimesisSettings.HeightOffset) * s;
            pos += MimesisHands.EmoteCamOffset; // build 45: emotes that move the body - the view follows the head sideways
            _vrRig.transform.SetPositionAndRotation(pos, rigRot);
        }

        private static void CopyUrpCameraData(Camera src, Camera dst)
        {
            try
            {
                var s = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(src);
                var d = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(dst);
                if (s == null || d == null) { VRModCore.LogWarning("[MimesisVR] No URP camera data to copy."); return; }
                d.renderPostProcessing = s.renderPostProcessing;
                _gamePost = s.renderPostProcessing;
                d.antialiasing = s.antialiasing;
                d.antialiasingQuality = s.antialiasingQuality;
                d.renderShadows = s.renderShadows;
                d.volumeLayerMask = s.volumeLayerMask;
                d.volumeTrigger = s.volumeTrigger != null ? s.volumeTrigger : src.transform;
                d.stopNaN = s.stopNaN;
                d.dithering = s.dithering;
                d.requiresColorOption = s.requiresColorOption;
                d.requiresDepthOption = s.requiresDepthOption;
                var f = typeof(UnityEngine.Rendering.Universal.UniversalAdditionalCameraData).GetField("m_RendererIndex", BindingFlags.Instance | BindingFlags.NonPublic);
                int idx = f != null ? (int)f.GetValue(s) : -1;
                d.SetRenderer(idx);
                dst.allowHDR = src.allowHDR;
                dst.allowMSAA = src.allowMSAA;
                dst.useOcclusionCulling = src.useOcclusionCulling;
                dst.depthTextureMode = src.depthTextureMode;
                VRModCore.Log($"[MimesisVR] URP data copied to {dst.name}: post={s.renderPostProcessing}, renderer={idx}, aa={s.antialiasing}, shadows={s.renderShadows}, volumeMask={s.volumeLayerMask.value}");
            }
            catch (Exception ex) { VRModCore.LogError("[MimesisVR] CopyUrpCameraData failed:", ex); }
        }

        public void SetupCameraRig(Camera mainCamera)
        {
            _hasRecenter = false;
            if (mainCamera == null)
            {
                VRModCore.LogError("SetupCameraRig FAILED: mainCamera is null.");
                return;
            }
            VRModCore.LogRuntimeDebug($"SetupCameraRig for camera '{mainCamera.name}'.");

            if (_vrRig != null) TeardownCameraRig();

            _vrRig = new GameObject("UnityVRMod_XrRig");
            _currentlyTrackedOriginalCameraGO = mainCamera.gameObject;

            Vector3 targetPosition = mainCamera.transform.position;
            Quaternion targetRotation = Quaternion.Euler(0, mainCamera.transform.eulerAngles.y, 0);

            var poseOverrides = PoseParser.Parse(ConfigManager.ScenePoseOverrides.Value);
            string currentSceneName = mainCamera.gameObject.scene.name;

            if (poseOverrides.TryGetValue(currentSceneName, out PoseOverride poseOverride))
            {
                VRModCore.Log($"Applying pose override for scene '{currentSceneName}'");
                var p = poseOverride.Position;
                var r = poseOverride.Rotation;
                var originalPos = mainCamera.transform.position;
                var originalRot = mainCamera.transform.eulerAngles;

                Vector3 finalPos = new(float.IsNaN(p.x) ? originalPos.x : p.x, float.IsNaN(p.y) ? originalPos.y : p.y, float.IsNaN(p.z) ? originalPos.z : p.z);
                Vector3 finalRot = new(float.IsNaN(r.x) ? originalRot.x : r.x, float.IsNaN(r.y) ? originalRot.y : r.y, float.IsNaN(r.z) ? originalRot.z : r.z);
                targetPosition = finalPos;
                targetRotation = Quaternion.Euler(finalRot);
            }
            _vrRig.transform.SetPositionAndRotation(targetPosition, targetRotation);

            _mainCameraClearFlags = mainCamera.clearFlags;
            _mainCameraBackgroundColor = mainCamera.backgroundColor;
            _mainCameraCullingMask = mainCamera.cullingMask;

            _currentAppliedRigScale = 1.0f / Mathf.Max(0.01f, ConfigManager.VrWorldScale.Value);
            _vrRig.transform.localScale = new Vector3(_currentAppliedRigScale, _currentAppliedRigScale, _currentAppliedRigScale);

            _leftVrCameraGO = new GameObject("XrVrCamera_Left");
            _leftVrCameraGO.transform.SetParent(_vrRig.transform, false);
            _leftVrCamera = _leftVrCameraGO.AddComponent<Camera>();
            ConfigureVrCamera(_leftVrCamera, mainCamera, "Left");
            CopyUrpCameraData(mainCamera, _leftVrCamera);

            _rightVrCameraGO = new GameObject("XrVrCamera_Right");
            _rightVrCameraGO.transform.SetParent(_vrRig.transform, false);
            _rightVrCamera = _rightVrCameraGO.AddComponent<Camera>();
            ConfigureVrCamera(_rightVrCamera, mainCamera, "Right");
            CopyUrpCameraData(mainCamera, _rightVrCamera);
            MimesisDiagnostics.DumpCameras(mainCamera);
            MimesisVrExtras.Setup(_vrRig.transform);

            _lastCalculatedVerticalOffset = 0f;
            UpdateVerticalOffset();

            VRModCore.Log("OpenXR: VR Camera Rig setup complete.");
        }

        private void ConfigureVrCamera(Camera vrCam, Camera mainCamRef, string eyeName)
        {
            VRModCore.LogRuntimeDebug($"Configuring VR Camera properties for: {eyeName}");
            vrCam.stereoTargetEye = StereoTargetEyeMask.None;
            vrCam.enabled = false;

            vrCam.clearFlags = _mainCameraClearFlags;
            vrCam.cullingMask = _mainCameraCullingMask;
            if (_mainCameraClearFlags == CameraClearFlags.SolidColor)
            {
                vrCam.backgroundColor = _mainCameraBackgroundColor;
            }

            float nearClipPlaneUserValue = ConfigManager.VrCameraNearClipPlane.Value;
            vrCam.nearClipPlane = Mathf.Max(0.001f, nearClipPlaneUserValue * _currentAppliedRigScale);

            float referenceFarClip = 1000f;
            if (mainCamRef != null)
            {
                referenceFarClip = mainCamRef.farClipPlane;
            }
            else if (_currentlyTrackedOriginalCameraGO != null)
            {
                if (_currentlyTrackedOriginalCameraGO.TryGetComponent<Camera>(out var trackedCam)) referenceFarClip = trackedCam.farClipPlane;
            }
            vrCam.farClipPlane = referenceFarClip * _currentAppliedRigScale;
            _refFar = vrCam.farClipPlane;
        }

        private static float _refFar;
        private static bool _gamePost = true;

        public void TeardownCameraRig()
        {
            VRModCore.LogRuntimeDebug("Tearing down VR camera rig.");
            if (_leftEyeIntermediateRT != null) { _leftEyeIntermediateRT.Release(); UnityEngine.Object.Destroy(_leftEyeIntermediateRT); _leftEyeIntermediateRT = null; }
            if (_rightEyeIntermediateRT != null) { _rightEyeIntermediateRT.Release(); UnityEngine.Object.Destroy(_rightEyeIntermediateRT); _rightEyeIntermediateRT = null; }
            if (_leftFlipRT != null) { _leftFlipRT.Release(); UnityEngine.Object.Destroy(_leftFlipRT); _leftFlipRT = null; }
            if (_rightFlipRT != null) { _rightFlipRT.Release(); UnityEngine.Object.Destroy(_rightFlipRT); _rightFlipRT = null; }
            MimesisVrExtras.Teardown();
            MimesisInput.VrActive = false;
            MimesisInput.HeadValid = false;
            if (_vrRig != null) { UnityEngine.Object.Destroy(_vrRig); _vrRig = null; }
            _leftVrCameraGO = null; _leftVrCamera = null; _rightVrCameraGO = null; _rightVrCamera = null;
            _currentlyTrackedOriginalCameraGO = null;
        }

        public void TeardownVr()
        {
            MimesisWatchdog.Armed = false;
            RtSubmit.WaitPrevious(500);
            VRModCore.LogRuntimeDebug("Tearing down OpenXR system.");
            TeardownCameraRig();

            if (_pProjectionLayerViews != IntPtr.Zero) { Marshal.FreeHGlobal(_pProjectionLayerViews); _pProjectionLayerViews = IntPtr.Zero; }
            if (_pProjectionLayer != IntPtr.Zero) { Marshal.FreeHGlobal(_pProjectionLayer); _pProjectionLayer = IntPtr.Zero; }
            if (_pLayersForSubmit != IntPtr.Zero) { Marshal.FreeHGlobal(_pLayersForSubmit); _pLayersForSubmit = IntPtr.Zero; }
            _panelLayer?.Dispose(); _panelLayer = null;
            _hudLayer?.Dispose(); _hudLayer = null;
            _laserLayer?.Dispose(); _laserLayer = null;
            _gearLayer?.Dispose(); _gearLayer = null;
            _fpsLayer?.Dispose(); _fpsLayer = null;
            _watchLayer?.Dispose(); _watchLayer = null;
            _settingsLayer?.Dispose(); _settingsLayer = null;
            foreach (var l in _segLayers.Values) l.Dispose();
            _segLayers.Clear();

            if (_appSpace != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroySpace != null)
                OpenXRAPI.xrDestroySpace(_appSpace);

            if (_eyeSwapchainSRVs != null)
            {
                foreach (var srvList in _eyeSwapchainSRVs)
                    foreach (var srv in srvList)
                        if (srv != IntPtr.Zero) NativeBridge.ReleaseNativeObject_Internal(srv);
            }

            if (_eyeSwapchains.Count > 0)
            {
                foreach (ulong sc in _eyeSwapchains)
                    if (sc != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroySwapchain != null)
                        OpenXRAPI.xrDestroySwapchain(sc);
            }

            if (_xrSession != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroySession != null)
                OpenXRAPI.xrDestroySession(_xrSession);

            if (_xrInstance != OpenXRConstants.XR_NULL_HANDLE && OpenXRAPI.xrDestroyInstance != null)
                OpenXRAPI.xrDestroyInstance(_xrInstance);

            _xrInstance = OpenXRConstants.XR_NULL_HANDLE;
            _xrSession = OpenXRConstants.XR_NULL_HANDLE;
            _appSpace = OpenXRConstants.XR_NULL_HANDLE;

            OpenXRNativeLoader.FreeOpenXRLibrary();
            IsVrAvailable = false;
        }

        private static Matrix4x4 CreateProjectionMatrixFromFovUsingFrustum(XrFovf fov, float nearClip, float farClip)
        {
            float nPR = Mathf.Tan(fov.angleRight) * nearClip;
            float nPL = Mathf.Tan(fov.angleLeft) * nearClip;
            float nPT = Mathf.Tan(fov.angleUp) * nearClip;
            float nPB = Mathf.Tan(fov.angleDown) * nearClip;

            Matrix4x4 m = new();
            m[0, 0] = (2.0f * nearClip) / (nPR - nPL);
            m[0, 2] = (nPR + nPL) / (nPR - nPL);
            m[1, 1] = (2.0f * nearClip) / (nPT - nPB);
            m[1, 2] = (nPT + nPB) / (nPT - nPB);
            m[2, 2] = -(farClip + nearClip) / (farClip - nearClip);
            m[2, 3] = -(2.0f * farClip * nearClip) / (farClip - nearClip);
            m[3, 2] = -1.0f;
            return m;
        }

        public void SetCameraNearClip(float newNearClipBaseValue)
        {
            if (_vrRig == null) return;
            Camera mainCamera = null;
            if (_currentlyTrackedOriginalCameraGO != null)
                mainCamera = _currentlyTrackedOriginalCameraGO.GetComponent<Camera>();

            if (_leftVrCamera != null) ConfigureVrCamera(_leftVrCamera, mainCamera, "Left");
            if (_rightVrCamera != null) ConfigureVrCamera(_rightVrCamera, mainCamera, "Right");
        }

        private void UpdateVerticalOffset()
        {
            if (_vrRig == null) return;
            float newTotalOffset = ConfigManager.VrUserEyeHeightOffset.Value * _currentAppliedRigScale;
            float delta = newTotalOffset - _lastCalculatedVerticalOffset;
            _vrRig.transform.position += new Vector3(0, delta, 0);
            _lastCalculatedVerticalOffset = newTotalOffset;
        }

        public void SetWorldScale(float newWorldScale, Camera mainCameraRef)
        {
            if (_vrRig == null) return;
            _currentAppliedRigScale = 1.0f / Mathf.Max(0.01f, newWorldScale);
            _vrRig.transform.localScale = new Vector3(_currentAppliedRigScale, _currentAppliedRigScale, _currentAppliedRigScale);

            Camera mainCam = mainCameraRef;
            if (mainCam == null && _currentlyTrackedOriginalCameraGO != null)
                mainCam = _currentlyTrackedOriginalCameraGO.GetComponent<Camera>();

            if (_leftVrCamera != null) ConfigureVrCamera(_leftVrCamera, mainCam, "Left");
            if (_rightVrCamera != null) ConfigureVrCamera(_rightVrCamera, mainCam, "Right");

            UpdateVerticalOffset();
        }

        public void SetUserEyeHeightOffset(float newOffset)
        {
            UpdateVerticalOffset();
        }

        public VrCameraRig GetVrCameraGameObjects()
        {
            return new VrCameraRig { LeftEye = _leftVrCameraGO, RightEye = _rightVrCameraGO };
        }
    }
}