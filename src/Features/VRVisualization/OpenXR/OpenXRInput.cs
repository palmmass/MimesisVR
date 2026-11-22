using System.Runtime.InteropServices;
using UnityVRMod.Core;

namespace UnityVRMod.Features.VRVisualization.OpenXR
{
    // ---- MIMESIS VR: OpenXR action (controller) input ----

    public enum XrActionType { BOOLEAN_INPUT = 1, FLOAT_INPUT = 2, VECTOR2F_INPUT = 3, POSE_INPUT = 4, VIBRATION_OUTPUT = 100 }

    internal static class XrInputTypes
    {
        public const int ACTION_STATE_BOOLEAN = 23;
        public const int ACTION_STATE_FLOAT = 24;
        public const int ACTION_STATE_VECTOR2F = 25;
        public const int ACTION_STATE_POSE = 27;
        public const int ACTION_SET_CREATE_INFO = 28;
        public const int ACTION_CREATE_INFO = 29;
        public const int ACTION_SPACE_CREATE_INFO = 38;
        public const int SPACE_LOCATION = 42;
        public const int INTERACTION_PROFILE_SUGGESTED_BINDING = 51;
        public const int ACTION_STATE_GET_INFO = 58;
        public const int SESSION_ACTION_SETS_ATTACH_INFO = 60;
        public const int ACTIONS_SYNC_INFO = 61;
        public const int HAPTIC_ACTION_INFO = 59;
        public const int HAPTIC_VIBRATION = 13; // XR_TYPE_HAPTIC_VIBRATION (build 32-34 had 88: runtime ignored it and still said XR_SUCCESS)
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct XrActionSetCreateInfo
    {
        public int type; public IntPtr next;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string actionSetName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string localizedActionSetName;
        public uint priority;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct XrActionCreateInfo
    {
        public int type; public IntPtr next;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string actionName;
        public XrActionType actionType;
        public uint countSubactionPaths; public IntPtr subactionPaths;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string localizedActionName;
    }

    [StructLayout(LayoutKind.Sequential)] public struct XrActionSuggestedBinding { public ulong action; public ulong binding; }
    [StructLayout(LayoutKind.Sequential)] public struct XrInteractionProfileSuggestedBinding { public int type; public IntPtr next; public ulong interactionProfile; public uint countSuggestedBindings; public IntPtr suggestedBindings; }
    [StructLayout(LayoutKind.Sequential)] public struct XrSessionActionSetsAttachInfo { public int type; public IntPtr next; public uint countActionSets; public IntPtr actionSets; }
    [StructLayout(LayoutKind.Sequential)] public struct XrActiveActionSet { public ulong actionSet; public ulong subactionPath; }
    [StructLayout(LayoutKind.Sequential)] public struct XrActionsSyncInfo { public int type; public IntPtr next; public uint countActiveActionSets; public IntPtr activeActionSets; }
    [StructLayout(LayoutKind.Sequential)] public struct XrActionStateGetInfo { public int type; public IntPtr next; public ulong action; public ulong subactionPath; }
    [StructLayout(LayoutKind.Sequential)] public struct XrActionStateBoolean { public int type; public IntPtr next; public uint currentState; public uint changedSinceLastSync; public long lastChangeTime; public uint isActive; }
    [StructLayout(LayoutKind.Sequential)] public struct XrActionStateFloat { public int type; public IntPtr next; public float currentState; public uint changedSinceLastSync; public long lastChangeTime; public uint isActive; }
    [StructLayout(LayoutKind.Sequential)] public struct XrVector2f { public float x; public float y; }
    [StructLayout(LayoutKind.Sequential)] public struct XrActionStateVector2f { public int type; public IntPtr next; public XrVector2f currentState; public uint changedSinceLastSync; public long lastChangeTime; public uint isActive; }
    [StructLayout(LayoutKind.Sequential)] public struct XrActionSpaceCreateInfo { public int type; public IntPtr next; public ulong action; public ulong subactionPath; public XrPosef poseInActionSpace; }
    [StructLayout(LayoutKind.Sequential)] public struct XrExtent2Df { public float width; public float height; }
    [StructLayout(LayoutKind.Sequential)] public struct XrCompositionLayerQuad { public int type; public IntPtr next; public ulong layerFlags; public ulong space; public int eyeVisibility; public XrSwapchainSubImage subImage; public XrPosef pose; public XrExtent2Df size; }
    [StructLayout(LayoutKind.Sequential)] public struct XrSpaceLocation { public int type; public IntPtr next; public ulong locationFlags; public XrPosef pose; }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrStringToPath(ulong instance, [MarshalAs(UnmanagedType.LPStr)] string pathString, out ulong path);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrCreateActionSet(ulong instance, in XrActionSetCreateInfo createInfo, out ulong actionSet);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrCreateAction(ulong actionSet, in XrActionCreateInfo createInfo, out ulong action);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrSuggestInteractionProfileBindings(ulong instance, in XrInteractionProfileSuggestedBinding suggestedBindings);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrAttachSessionActionSets(ulong session, in XrSessionActionSetsAttachInfo attachInfo);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrSyncActions(ulong session, in XrActionsSyncInfo syncInfo);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrGetActionStateBoolean(ulong session, in XrActionStateGetInfo getInfo, ref XrActionStateBoolean state);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrGetActionStateFloat(ulong session, in XrActionStateGetInfo getInfo, ref XrActionStateFloat state);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrGetActionStateVector2f(ulong session, in XrActionStateGetInfo getInfo, ref XrActionStateVector2f state);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrCreateActionSpace(ulong session, in XrActionSpaceCreateInfo createInfo, out ulong space);
    [StructLayout(LayoutKind.Sequential)] public struct XrHapticActionInfo { public int type; public IntPtr next; public ulong action; public ulong subactionPath; }
    [StructLayout(LayoutKind.Sequential)] public struct XrHapticVibration { public int type; public IntPtr next; public long duration; public float frequency; public float amplitude; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrApplyHapticFeedback(ulong session, in XrHapticActionInfo hapticActionInfo, in XrHapticVibration hapticFeedback);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate XrResult PFN_xrLocateSpace(ulong space, ulong baseSpace, long time, ref XrSpaceLocation location);

    /// <summary>Controller actions for Oculus Touch (Quest 3 via VDXR).</summary>
    internal sealed class XrControllerInput
    {
        public enum B { A, B, X, Y, LStick, RStick, Menu, LThumbTouch, RThumbTouch, LIndexTouch, RIndexTouch, Count }
        public enum F { LTrigger, RTrigger, LGrip, RGrip, Count }

        private PFN_xrStringToPath _toPath;
        private PFN_xrCreateActionSet _createActionSet;
        private PFN_xrCreateAction _createAction;
        private PFN_xrSuggestInteractionProfileBindings _suggest;
        private PFN_xrAttachSessionActionSets _attach;
        private PFN_xrSyncActions _sync;
        private PFN_xrGetActionStateBoolean _getBool;
        private PFN_xrGetActionStateFloat _getFloat;
        private PFN_xrGetActionStateVector2f _getVec2;
        private PFN_xrCreateActionSpace _createActionSpace;
        private PFN_xrLocateSpace _locateSpace;
        private PFN_xrApplyHapticFeedback _haptic;
        private ulong _leftHaptic, _rightHaptic;
        internal int _hapticLogged;
        /// <summary>True when the vibration outputs were accepted by the runtime.</summary>
        public bool HapticsBound { get; private set; }

        /// <summary>Vibrates one controller (seconds, 0..1). Silently does nothing without haptics.</summary>
        /// <summary>Vibration frequency (Hz, 0 = runtime default) used for every pulse.</summary>
        public static float HapticFrequency = 0f;

        public void Vibrate(bool right, float seconds, float amplitude) => Vibrate(right, seconds, amplitude, HapticFrequency);

        public void Vibrate(bool right, float seconds, float amplitude, float frequency)
        {
            if (!HapticsBound || _haptic == null || _session == 0) return;
            try
            {
                var info = new XrHapticActionInfo { type = XrInputTypes.HAPTIC_ACTION_INFO, action = right ? _rightHaptic : _leftHaptic };
                var v = new XrHapticVibration { type = XrInputTypes.HAPTIC_VIBRATION, duration = seconds < 0f ? -1L : (long)(Mathf.Max(0.005f, seconds) * 1e9), frequency = frequency, amplitude = Mathf.Clamp01(amplitude) };
                var r = _haptic(_session, in info, in v);
                if (_hapticLogged < 30) { _hapticLogged++; VRModCore.Log($"[MimesisVR] Vibration {(right ? "right" : "left")} {seconds:F2}s x{amplitude:F2} {frequency:0}Hz -> {r}"); }
            }
            catch (Exception ex) { if (_hapticLogged < 6) { _hapticLogged++; VRModCore.LogWarning("[MimesisVR] Vibration failed: " + ex.Message); } }
        }

        private ulong _instance, _session, _actionSet;
        private readonly ulong[] _bool = new ulong[(int)B.Count];
        private readonly ulong[] _float = new ulong[(int)F.Count];
        private ulong _leftStick, _rightStick, _leftPose, _rightPose, _leftAim, _rightAim;
        private ulong _leftPoseSpace, _rightPoseSpace, _leftAimSpace, _rightAimSpace;

        public bool Ready { get; private set; }

        // Latest state (Unity coordinates, tracking space)
        public readonly bool[] Buttons = new bool[(int)B.Count];
        public readonly float[] Axes = new float[(int)F.Count];
        public Vector2 LeftStick, RightStick;
        public bool LeftPoseValid, RightPoseValid;
        public Vector3 LeftPos, RightPos, LeftAimPos, RightAimPos;
        public Quaternion LeftRot = Quaternion.identity, RightRot = Quaternion.identity, LeftAimRot = Quaternion.identity, RightAimRot = Quaternion.identity;

        private static T Load<T>(ulong instance) where T : Delegate
        {
            string name = typeof(T).Name.Substring("PFN_".Length);
            var gipa = OpenXRNativeLoader.xrGetInstanceProcAddr_ptr_delegate;
            var r = gipa(instance, name, out IntPtr fn);
            if (r != XrResult.XR_SUCCESS || fn == IntPtr.Zero) throw new EntryPointNotFoundException($"OpenXR function {name}: {r}");
            return Marshal.GetDelegateForFunctionPointer<T>(fn);
        }

        private ulong P(string s)
        {
            var r = _toPath(_instance, s, out ulong p);
            if (r != XrResult.XR_SUCCESS) throw new Exception($"xrStringToPath('{s}') -> {r}");
            return p;
        }

        /// <summary>True when the touch sensors were accepted by the runtime.</summary>
        public bool TouchBound { get; private set; }

        private XrResult Suggest(ulong instance, List<XrActionSuggestedBinding> b)
        {
            int sz = Marshal.SizeOf<XrActionSuggestedBinding>();
            IntPtr pB = Marshal.AllocHGlobal(sz * b.Count);
            try
            {
                for (int i = 0; i < b.Count; i++) Marshal.StructureToPtr(b[i], pB + i * sz, false);
                var sugg = new XrInteractionProfileSuggestedBinding
                {
                    type = XrInputTypes.INTERACTION_PROFILE_SUGGESTED_BINDING,
                    interactionProfile = P("/interaction_profiles/oculus/touch_controller"),
                    countSuggestedBindings = (uint)b.Count,
                    suggestedBindings = pB
                };
                return _suggest(instance, in sugg);
            }
            finally { Marshal.FreeHGlobal(pB); }
        }

        private ulong MakeAction(string name, XrActionType type)
        {
            var info = new XrActionCreateInfo { type = XrInputTypes.ACTION_CREATE_INFO, actionName = name, localizedActionName = name, actionType = type };
            var r = _createAction(_actionSet, in info, out ulong a);
            if (r != XrResult.XR_SUCCESS) throw new Exception($"xrCreateAction('{name}') -> {r}");
            return a;
        }

        public bool Initialize(ulong instance, ulong session)
        {
            try
            {
                _instance = instance; _session = session;
                _toPath = Load<PFN_xrStringToPath>(instance);
                _createActionSet = Load<PFN_xrCreateActionSet>(instance);
                _createAction = Load<PFN_xrCreateAction>(instance);
                _suggest = Load<PFN_xrSuggestInteractionProfileBindings>(instance);
                _attach = Load<PFN_xrAttachSessionActionSets>(instance);
                _sync = Load<PFN_xrSyncActions>(instance);
                _getBool = Load<PFN_xrGetActionStateBoolean>(instance);
                _getFloat = Load<PFN_xrGetActionStateFloat>(instance);
                _getVec2 = Load<PFN_xrGetActionStateVector2f>(instance);
                _createActionSpace = Load<PFN_xrCreateActionSpace>(instance);
                _locateSpace = Load<PFN_xrLocateSpace>(instance);
                try { _haptic = Load<PFN_xrApplyHapticFeedback>(instance); } catch { _haptic = null; }

                var setInfo = new XrActionSetCreateInfo { type = XrInputTypes.ACTION_SET_CREATE_INFO, actionSetName = "mimesisvr", localizedActionSetName = "MIMESIS VR", priority = 0 };
                var r = _createActionSet(instance, in setInfo, out _actionSet);
                if (r != XrResult.XR_SUCCESS) throw new Exception($"xrCreateActionSet -> {r}");

                _bool[(int)B.A] = MakeAction("button_a", XrActionType.BOOLEAN_INPUT);
                _bool[(int)B.B] = MakeAction("button_b", XrActionType.BOOLEAN_INPUT);
                _bool[(int)B.X] = MakeAction("button_x", XrActionType.BOOLEAN_INPUT);
                _bool[(int)B.Y] = MakeAction("button_y", XrActionType.BOOLEAN_INPUT);
                _bool[(int)B.LStick] = MakeAction("left_stick_click", XrActionType.BOOLEAN_INPUT);
                _bool[(int)B.RStick] = MakeAction("right_stick_click", XrActionType.BOOLEAN_INPUT);
                _bool[(int)B.Menu] = MakeAction("menu", XrActionType.BOOLEAN_INPUT);
                _bool[(int)B.LThumbTouch] = MakeAction("left_thumb_touch", XrActionType.BOOLEAN_INPUT);
                _bool[(int)B.RThumbTouch] = MakeAction("right_thumb_touch", XrActionType.BOOLEAN_INPUT);
                _bool[(int)B.LIndexTouch] = MakeAction("left_index_touch", XrActionType.BOOLEAN_INPUT);
                _bool[(int)B.RIndexTouch] = MakeAction("right_index_touch", XrActionType.BOOLEAN_INPUT);
                _leftHaptic = MakeAction("left_haptic", XrActionType.VIBRATION_OUTPUT);
                _rightHaptic = MakeAction("right_haptic", XrActionType.VIBRATION_OUTPUT);
                _float[(int)F.LTrigger] = MakeAction("left_trigger", XrActionType.FLOAT_INPUT);
                _float[(int)F.RTrigger] = MakeAction("right_trigger", XrActionType.FLOAT_INPUT);
                _float[(int)F.LGrip] = MakeAction("left_grip", XrActionType.FLOAT_INPUT);
                _float[(int)F.RGrip] = MakeAction("right_grip", XrActionType.FLOAT_INPUT);
                _leftStick = MakeAction("left_stick", XrActionType.VECTOR2F_INPUT);
                _rightStick = MakeAction("right_stick", XrActionType.VECTOR2F_INPUT);
                _leftPose = MakeAction("left_grip_pose", XrActionType.POSE_INPUT);
                _rightPose = MakeAction("right_grip_pose", XrActionType.POSE_INPUT);
                _leftAim = MakeAction("left_aim_pose", XrActionType.POSE_INPUT);
                _rightAim = MakeAction("right_aim_pose", XrActionType.POSE_INPUT);

                var b = new List<XrActionSuggestedBinding>
                {
                    new() { action = _bool[(int)B.A], binding = P("/user/hand/right/input/a/click") },
                    new() { action = _bool[(int)B.B], binding = P("/user/hand/right/input/b/click") },
                    new() { action = _bool[(int)B.X], binding = P("/user/hand/left/input/x/click") },
                    new() { action = _bool[(int)B.Y], binding = P("/user/hand/left/input/y/click") },
                    new() { action = _bool[(int)B.LStick], binding = P("/user/hand/left/input/thumbstick/click") },
                    new() { action = _bool[(int)B.RStick], binding = P("/user/hand/right/input/thumbstick/click") },
                    new() { action = _bool[(int)B.Menu], binding = P("/user/hand/left/input/menu/click") },
                    new() { action = _float[(int)F.LTrigger], binding = P("/user/hand/left/input/trigger/value") },
                    new() { action = _float[(int)F.RTrigger], binding = P("/user/hand/right/input/trigger/value") },
                    new() { action = _float[(int)F.LGrip], binding = P("/user/hand/left/input/squeeze/value") },
                    new() { action = _float[(int)F.RGrip], binding = P("/user/hand/right/input/squeeze/value") },
                    new() { action = _leftStick, binding = P("/user/hand/left/input/thumbstick") },
                    new() { action = _rightStick, binding = P("/user/hand/right/input/thumbstick") },
                    new() { action = _leftPose, binding = P("/user/hand/left/input/grip/pose") },
                    new() { action = _rightPose, binding = P("/user/hand/right/input/grip/pose") },
                    new() { action = _leftAim, binding = P("/user/hand/left/input/aim/pose") },
                    new() { action = _rightAim, binding = P("/user/hand/right/input/aim/pose") },
                };

                // Touch sensors (finger resting on buttons / stick / thumbrest / trigger) - only to pose the fingers.
                // Suggested together; if the runtime refuses them, the bindings are suggested again without them.
                var touch = new List<XrActionSuggestedBinding>();
                void T(B a, string path) { touch.Add(new XrActionSuggestedBinding { action = _bool[(int)a], binding = P(path) }); }
                T(B.RThumbTouch, "/user/hand/right/input/a/touch"); T(B.RThumbTouch, "/user/hand/right/input/b/touch");
                T(B.RThumbTouch, "/user/hand/right/input/thumbstick/touch"); T(B.RThumbTouch, "/user/hand/right/input/thumbrest/touch");
                T(B.LThumbTouch, "/user/hand/left/input/x/touch"); T(B.LThumbTouch, "/user/hand/left/input/y/touch");
                T(B.LThumbTouch, "/user/hand/left/input/thumbstick/touch"); T(B.LThumbTouch, "/user/hand/left/input/thumbrest/touch");
                T(B.RIndexTouch, "/user/hand/right/input/trigger/touch"); T(B.LIndexTouch, "/user/hand/left/input/trigger/touch");
                touch.Add(new XrActionSuggestedBinding { action = _leftHaptic, binding = P("/user/hand/left/output/haptic") });
                touch.Add(new XrActionSuggestedBinding { action = _rightHaptic, binding = P("/user/hand/right/output/haptic") });
                var all = new List<XrActionSuggestedBinding>(b); all.AddRange(touch);
                r = Suggest(instance, all);
                if (r != XrResult.XR_SUCCESS)
                {
                    VRModCore.LogWarning($"[MimesisVR] Touch bindings refused ({r}) - fingers will not react to touch.");
                    r = Suggest(instance, b);
                }
                else { TouchBound = true; HapticsBound = _haptic != null; }
                if (r != XrResult.XR_SUCCESS) throw new Exception($"xrSuggestInteractionProfileBindings -> {r}");

                IntPtr pSet = Marshal.AllocHGlobal(8);
                try
                {
                    Marshal.WriteInt64(pSet, (long)_actionSet);
                    var att = new XrSessionActionSetsAttachInfo { type = XrInputTypes.SESSION_ACTION_SETS_ATTACH_INFO, countActionSets = 1, actionSets = pSet };
                    r = _attach(session, in att);
                    if (r != XrResult.XR_SUCCESS) throw new Exception($"xrAttachSessionActionSets -> {r}");
                }
                finally { Marshal.FreeHGlobal(pSet); }

                _leftPoseSpace = MakeSpace(_leftPose);
                _rightPoseSpace = MakeSpace(_rightPose);
                _leftAimSpace = MakeSpace(_leftAim);
                _rightAimSpace = MakeSpace(_rightAim);

                Ready = true;
                VRModCore.Log($"[MimesisVR] Controller actions ready (oculus/touch_controller), touch sensors {(TouchBound ? "on" : "off")}, vibration {(HapticsBound ? "on" : "off")}.");
                return true;
            }
            catch (Exception ex)
            {
                VRModCore.LogError("[MimesisVR] Controller input init failed:", ex);
                Ready = false;
                return false;
            }
        }

        private ulong MakeSpace(ulong action)
        {
            var info = new XrActionSpaceCreateInfo { type = XrInputTypes.ACTION_SPACE_CREATE_INFO, action = action, poseInActionSpace = new XrPosef { orientation = new XrQuaternionf { w = 1f } } };
            var r = _createActionSpace(_session, in info, out ulong space);
            if (r != XrResult.XR_SUCCESS) throw new Exception($"xrCreateActionSpace -> {r}");
            return space;
        }

        private bool GetBool(ulong action)
        {
            var gi = new XrActionStateGetInfo { type = XrInputTypes.ACTION_STATE_GET_INFO, action = action };
            var st = new XrActionStateBoolean { type = XrInputTypes.ACTION_STATE_BOOLEAN };
            return _getBool(_session, in gi, ref st) == XrResult.XR_SUCCESS && st.isActive != 0 && st.currentState != 0;
        }

        private float GetFloat(ulong action)
        {
            var gi = new XrActionStateGetInfo { type = XrInputTypes.ACTION_STATE_GET_INFO, action = action };
            var st = new XrActionStateFloat { type = XrInputTypes.ACTION_STATE_FLOAT };
            return _getFloat(_session, in gi, ref st) == XrResult.XR_SUCCESS && st.isActive != 0 ? st.currentState : 0f;
        }

        private Vector2 GetVec2(ulong action)
        {
            var gi = new XrActionStateGetInfo { type = XrInputTypes.ACTION_STATE_GET_INFO, action = action };
            var st = new XrActionStateVector2f { type = XrInputTypes.ACTION_STATE_VECTOR2F };
            return _getVec2(_session, in gi, ref st) == XrResult.XR_SUCCESS && st.isActive != 0 ? new Vector2(st.currentState.x, st.currentState.y) : Vector2.zero;
        }

        private bool Locate(ulong space, ulong baseSpace, long time, out Vector3 pos, out Quaternion rot)
        {
            var loc = new XrSpaceLocation { type = XrInputTypes.SPACE_LOCATION };
            pos = Vector3.zero; rot = Quaternion.identity;
            if (_locateSpace(space, baseSpace, time, ref loc) != XrResult.XR_SUCCESS) return false;
            const ulong ORIENT_VALID = 1, POS_VALID = 2;
            if ((loc.locationFlags & (ORIENT_VALID | POS_VALID)) != (ORIENT_VALID | POS_VALID)) return false;
            pos = new Vector3(loc.pose.position.x, loc.pose.position.y, -loc.pose.position.z);
            rot = new Quaternion(loc.pose.orientation.x, loc.pose.orientation.y, -loc.pose.orientation.z, -loc.pose.orientation.w);
            return true;
        }

        private long _failCount;

        /// <summary>Sync actions and read state. Call once per frame while the session is focused.</summary>
        public void Update(ulong baseSpace, long displayTime)
        {
            if (!Ready) return;
            IntPtr pActive = Marshal.AllocHGlobal(Marshal.SizeOf<XrActiveActionSet>());
            try
            {
                Marshal.StructureToPtr(new XrActiveActionSet { actionSet = _actionSet, subactionPath = 0 }, pActive, false);
                var si = new XrActionsSyncInfo { type = XrInputTypes.ACTIONS_SYNC_INFO, countActiveActionSets = 1, activeActionSets = pActive };
                var r = _sync(_session, in si);
                if ((int)r < 0)
                {
                    if (_failCount++ % 300 == 0) VRModCore.LogWarning($"[MimesisVR] xrSyncActions -> {r}");
                    return;
                }
                if ((int)r == 8) return; // XR_SESSION_NOT_FOCUSED: keep last state
            }
            finally { Marshal.FreeHGlobal(pActive); }

            for (int i = 0; i < (int)B.Count; i++) Buttons[i] = GetBool(_bool[i]);
            for (int i = 0; i < (int)F.Count; i++) Axes[i] = GetFloat(_float[i]);
            LeftStick = GetVec2(_leftStick);
            RightStick = GetVec2(_rightStick);
            LeftPoseValid = Locate(_leftPoseSpace, baseSpace, displayTime, out LeftPos, out LeftRot);
            RightPoseValid = Locate(_rightPoseSpace, baseSpace, displayTime, out RightPos, out RightRot);
            Locate(_leftAimSpace, baseSpace, displayTime, out LeftAimPos, out LeftAimRot);
            Locate(_rightAimSpace, baseSpace, displayTime, out RightAimPos, out RightAimRot);
        }
    }
}
