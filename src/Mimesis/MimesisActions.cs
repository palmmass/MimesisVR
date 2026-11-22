using HarmonyLib;
using Mimic.Actors;
using ReluProtocol;
using UnityVRMod.Core;

namespace UnityVRMod.Mimesis
{
    /// <summary>
    /// MIMESIS VR: item actions (melee swing, shot, throw...) go where the right hand points instead of where you look.
    /// The client tells the server its position + yaw + pitch when it starts a skill (ProtoActor.MyPosWithAngle, used
    /// only by SkillHelper.SendUseSkill); the server computes hits from that. The postfix swaps yaw/pitch for the
    /// direction of the right controller's pointer. Works for the host and for guests (the server trusts the request).
    /// </summary>
    internal static class MimesisActions
    {
        /// <summary>true = by the right hand pointer, false = by gaze (game default).</summary>
        public static bool ByHand = true;
        /// <summary>Share of the game's jump camera lift kept in VR (the game lifts the view ~65 cm; the jump itself is
        /// only that lift plus the body animation - the character does not leave the ground).</summary>
        public static float JumpLift = 0.45f;
        /// <summary>In VR the game's camera shift with head pitch (forward / down when looking down, made for the flat
        /// screen) is removed - the view moves only with your head.</summary>
        public static bool NoPitchShift = true;
        private static AccessTools.FieldRef<FPSCameraInterpolation, float> _yOrigin, _zOrigin, _jumpY;
        private static AccessTools.FieldRef<FPSCameraInterpolation, GameObject> _camRoot; // a GameObject in the game
        private static FieldInfo _jumpScale;
        private static float _jumpScaleOrig = -1f;
        private static int _logged;

        public static void Install(HarmonyLib.Harmony h)
        {
            var m = AccessTools.Method(typeof(ProtoActor), "MyPosWithAngle");
            if (m == null) { VRModCore.LogWarning("[MimesisVR] ProtoActor.MyPosWithAngle not found - actions stay by gaze."); return; }
            h.Patch(m, postfix: new HarmonyMethod(typeof(MimesisActions), nameof(MyPosWithAngle_Postfix)));
            VRModCore.Log("[MimesisVR] Item actions follow the hand (MyPosWithAngle patched).");
            var j = AccessTools.Method(typeof(FPSCameraInterpolation), "StartJumpOffset");
            if (j != null) { h.Patch(j, prefix: new HarmonyMethod(typeof(MimesisActions), nameof(StartJumpOffset_Prefix))); VRModCore.Log("[MimesisVR] Jump camera lift patched."); }
            try
            {
                _yOrigin = AccessTools.FieldRefAccess<FPSCameraInterpolation, float>("yOrigin");
                _zOrigin = AccessTools.FieldRefAccess<FPSCameraInterpolation, float>("zOrigin");
                _jumpY = AccessTools.FieldRefAccess<FPSCameraInterpolation, float>("currentJumpYOffset");
                _camRoot = AccessTools.FieldRefAccess<FPSCameraInterpolation, GameObject>("cameraRoot");
                var u = AccessTools.Method(typeof(FPSCameraInterpolation), "Update");
                if (u != null) { h.Patch(u, postfix: new HarmonyMethod(typeof(MimesisActions), nameof(CamUpdate_Postfix))); VRModCore.Log("[MimesisVR] Camera pitch shift patched."); }
            }
            catch (Exception ex) { VRModCore.LogWarning("[MimesisVR] Camera pitch shift patch failed: " + ex.Message); }
        }

        private static void CamUpdate_Postfix(FPSCameraInterpolation __instance)
        {
            try
            {
                if (!NoPitchShift || !MimesisInput.VrActive) return;
                var go = _camRoot(__instance);
                if (go == null) return;
                var root = go.transform;
                var lp = root.localPosition;
                root.localPosition = new Vector3(lp.x, _yOrigin(__instance) + _jumpY(__instance), _zOrigin(__instance));
            }
            catch { }
        }

        private static void StartJumpOffset_Prefix(FPSCameraInterpolation __instance)
        {
            try
            {
                _jumpScale ??= AccessTools.Field(typeof(FPSCameraInterpolation), "jumpHeightScale");
                if (_jumpScale == null) return;
                if (_jumpScaleOrig < 0f) _jumpScaleOrig = (float)_jumpScale.GetValue(__instance);
                _jumpScale.SetValue(__instance, MimesisInput.VrActive ? _jumpScaleOrig * JumpLift : _jumpScaleOrig);
            }
            catch { }
        }

        private static void MyPosWithAngle_Postfix(ProtoActor __instance, PosWithRot __result)
        {
            try
            {
                if (!ByHand || __result == null || !MimesisInput.VrActive || __instance != MimesisInput.LocalActor) return;
                var c = MimesisInput.Controllers; var rig = MimesisHands.Rig;
                if (c == null || !c.Ready || !c.RightPoseValid || rig == null) return;
                Vector3 d = rig.TransformDirection(c.RightAimRot * Vector3.forward).normalized;
                float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                float pitch = -Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg; // + = down, like the camera's euler x
                float oldYaw = __result.yaw, oldPitch = __result.pitch;
                __result.yaw = Mathf.Repeat(yaw, 360f);
                __result.pitch = Mathf.Repeat(pitch, 360f);
                if (_logged < 20) { _logged++; VRModCore.Log($"[MimesisVR] Action by hand: yaw {oldYaw:F0} -> {__result.yaw:F0}, pitch {oldPitch:F0} -> {__result.pitch:F0}"); }
            }
            catch (Exception ex) { if (_logged < 20) { _logged++; VRModCore.LogWarning("[MimesisVR] Action by hand failed: " + ex.Message); } }
        }
    }
}
