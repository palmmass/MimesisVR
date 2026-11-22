using System.Runtime.InteropServices;
using UnityVRMod.Core;
using UnityVRMod.Features.VRVisualization.OpenXR;

namespace UnityVRMod.Mimesis
{
    /// <summary>MIMESIS VR: one OpenXR quad composition layer fed from a Unity RenderTexture.</summary>
    internal sealed class XrQuadLayer
    {
        public const ulong FLAG_BLEND_SOURCE_ALPHA = 0x2;

        private readonly string _name;
        private ulong _swapchain;
        private int _w, _h;
        private long _format;
        private readonly List<IntPtr> _images = new();
        public IntPtr LayerPtr { get; private set; }
        public bool Failed { get; private set; }

        public XrQuadLayer(string name)
        {
            _name = name;
            LayerPtr = Marshal.AllocHGlobal(Marshal.SizeOf<XrCompositionLayerQuad>());
        }

        private bool Ensure(ulong session, long format, int w, int h)
        {
            if (_swapchain != 0 && _w == w && _h == h && _format == format) return true;
            DestroySwapchain();
            var ci = new XrSwapchainCreateInfo
            {
                type = XrStructureType.XR_TYPE_SWAPCHAIN_CREATE_INFO,
                usageFlags = XrSwapchainUsageFlags.XR_SWAPCHAIN_USAGE_COLOR_ATTACHMENT_BIT | XrSwapchainUsageFlags.XR_SWAPCHAIN_USAGE_SAMPLED_BIT,
                format = format, sampleCount = 1, width = (uint)w, height = (uint)h, faceCount = 1, arraySize = 1, mipCount = 1
            };
            var r = OpenXRAPI.xrCreateSwapchain(session, in ci, out _swapchain);
            if (r != XrResult.XR_SUCCESS) { VRModCore.LogError($"[MimesisVR] {_name} swapchain failed: {r}"); _swapchain = 0; return false; }
            OpenXRAPI.xrEnumerateSwapchainImages(_swapchain, 0, out uint n, IntPtr.Zero);
            int sz = Marshal.SizeOf<XrSwapchainImageD3D11KHR>();
            IntPtr buf = Marshal.AllocHGlobal((int)(n * sz));
            try
            {
                for (int j = 0; j < n; j++) Marshal.StructureToPtr(new XrSwapchainImageD3D11KHR { type = XrStructureType.XR_TYPE_SWAPCHAIN_IMAGE_D3D11_KHR }, buf + j * sz, false);
                OpenXRAPI.xrEnumerateSwapchainImages(_swapchain, n, out n, buf);
                for (int j = 0; j < n; j++) _images.Add(Marshal.PtrToStructure<XrSwapchainImageD3D11KHR>(buf + j * sz).texture);
            }
            finally { Marshal.FreeHGlobal(buf); }
            _w = w; _h = h; _format = format;
            VRModCore.Log($"[MimesisVR] {_name} swapchain {w}x{h}, {n} images.");
            return true;
        }

        /// <summary>Copy the texture into the swapchain and fill the layer struct. Pose/size in tracking space (Unity handedness).</summary>
        public bool Submit(ulong session, ulong space, long format, RenderTexture tex, Vector3 pos, Quaternion rot, Vector2 size, ulong flags, int rectW = 0, int rectH = 0)
        {
            if (Failed || tex == null || !tex.IsCreated()) return false;
            try
            {
                if (!Ensure(session, format, tex.width, tex.height)) { Failed = true; return false; }
                if (RtSubmit.NativeActive)
                {
                    RtSubmit.AddSwapchainCopy(_swapchain, _images, tex.GetNativeTexturePtr());
                }
                else
                {
                    var acq = new XrSwapchainImageAcquireInfo { type = XrStructureType.XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO };
                    if (OpenXRAPI.xrAcquireSwapchainImage(_swapchain, in acq, out uint idx) != XrResult.XR_SUCCESS) return false;
                    var wi = new XrSwapchainImageWaitInfo { type = XrStructureType.XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO, timeout = OpenXRConstants.XR_INFINITE_DURATION };
                    OpenXRAPI.xrWaitSwapchainImage(_swapchain, in wi);
                    RtSubmit.AddCopy(_images[(int)idx], tex.GetNativeTexturePtr());
                    RtSubmit.AddRelease(_swapchain);
                }

                var quad = new XrCompositionLayerQuad
                {
                    type = 36, layerFlags = flags, space = space, eyeVisibility = 0,
                    subImage = new XrSwapchainSubImage { swapchain = _swapchain, imageRect = new XrRect2Di { offset = new XrOffset2Di(), extent = new XrExtent2Di { width = rectW > 0 ? Math.Min(rectW, tex.width) : tex.width, height = rectH > 0 ? Math.Min(rectH, tex.height) : tex.height } }, imageArrayIndex = 0 },
                    pose = new XrPosef { position = new XrVector3f { x = pos.x, y = pos.y, z = -pos.z }, orientation = new XrQuaternionf { x = rot.x, y = rot.y, z = -rot.z, w = -rot.w } },
                    size = new XrExtent2Df { width = size.x, height = size.y }
                };
                Marshal.StructureToPtr(quad, LayerPtr, false);
                return true;
            }
            catch (Exception ex) { VRModCore.LogError($"[MimesisVR] {_name} layer error:", ex); Failed = true; return false; }
        }

        private void DestroySwapchain()
        {
            if (_swapchain != 0) { try { OpenXRAPI.xrDestroySwapchain(_swapchain); } catch { } _swapchain = 0; }
            _images.Clear();
        }

        public void Dispose()
        {
            DestroySwapchain();
            if (LayerPtr != IntPtr.Zero) { Marshal.FreeHGlobal(LayerPtr); LayerPtr = IntPtr.Zero; }
        }
    }
}
