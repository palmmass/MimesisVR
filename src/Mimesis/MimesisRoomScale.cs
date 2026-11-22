using HarmonyLib;
using Mimic.Actors;
using Mimic.Animation;
using UnityVRMod.Core;

namespace UnityVRMod.Mimesis
{
    /// <summary>
    /// MIMESIS VR: walking around the room moves the character. Each frame the horizontal offset of the headset from
    /// the point the view was centred on is applied to the character with its own CharacterController (walls and
    /// steps work as in the game, the game syncs the position to other players), and the centring point is shifted by
    /// the distance actually moved - so the view stays where your head is and the body comes along.
    /// If a wall stops the character, the rest of the offset stays (you can still lean).
    /// </summary>
    internal static class MimesisRoomScale
    {
        public static bool Enabled = true;
        /// <summary>The legs walk when you walk in the room (the game would otherwise keep them standing).</summary>
        public static bool WalkAnimation = true;
        private static Vector3 _vel;          // smoothed room-scale velocity, world
        private static float _velTime;
        private static FieldInfo _puppetField;
        private static PropertyInfo _walkProp, _runProp;

        public static void Install(HarmonyLib.Harmony h)
        {
            var m = AccessTools.Method(typeof(PuppetScript), "Stop");
            if (m == null) { VRModCore.LogWarning("[MimesisVR] PuppetScript.Stop not found - no walk animation for room-scale."); return; }
            h.Patch(m, prefix: new HarmonyMethod(typeof(MimesisRoomScale), nameof(Stop_Prefix)));
            VRModCore.Log("[MimesisVR] Room-scale walk animation patched (PuppetScript.Stop).");
        }

        /// <summary>The game says "stand" every frame without stick input; while you walk in the room the local
        /// character's legs get the room-scale speed instead (footstep sounds come from this animation too).</summary>
        private static bool Stop_Prefix(PuppetScript __instance)
        {
            try
            {
                if (!WalkAnimation || !Enabled || !MimesisInput.VrActive) return true;
                if (Time.unscaledTime - _velTime > 0.35f) return true;
                float sp = _vel.magnitude;
                if (sp < 0.05f) return true; // slow steps in a small room still walk
                var actor = MimesisInput.LocalActor;
                if (actor == null) return true;
                _puppetField ??= typeof(ProtoActor).GetField("puppet", BindingFlags.Instance | BindingFlags.NonPublic);
                if (!ReferenceEquals(_puppetField?.GetValue(actor), __instance)) return true;
                _walkProp ??= typeof(ProtoActor).GetProperty("walkSpeed", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                _runProp ??= typeof(ProtoActor).GetProperty("runSpeed", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                float walk = _walkProp?.GetValue(actor) is float w && w > 0.1f ? w : 2.5f;
                float run = _runProp?.GetValue(actor) is float r && r > 0.1f ? r : 5f;
                __instance.Move(_vel, walk, run);
                return false;
            }
            catch { return true; }
        }
        private static int _lastFrame = -1;
        private static float _movedTotal, _nextLog;
        private static int _loggedAnchor;

        private static void Decay()
        {
            float dt = Mathf.Max(1e-3f, Time.unscaledDeltaTime);
            _vel = Vector3.Lerp(_vel, Vector3.zero, 1f - Mathf.Exp(-dt * 4f));
            _velTime = Time.unscaledTime;
        }

        // Split in two halves so the move happens BEFORE the game camera follows the character (Cinemachine, LateUpdate):
        //  - at render time Step() takes what was really moved (shifts the centring point) and asks for the rest;
        //  - in the next Update MoveNow() moves the character by that amount.
        private static Vector3 _want, _moved;
        private static bool _hasWant;
        private static int _wantFrames, _moveFrames, _noCc, _pausedFrames;
        private static float _nextDiag;
        private static string _pauseWhy;

        private static void Diag()
        {
            if (Time.unscaledTime < _nextDiag) return;
            _nextDiag = Time.unscaledTime + 10f;
            if (_wantFrames > 0 || _pausedFrames > 0 || _noCc > 0)
                VRModCore.Log($"[MimesisVR] Room-scale 10s: asked to move in {_wantFrames} frames, moved in {_moveFrames}, no controller {_noCc}, paused {_pausedFrames}{(_pauseWhy != null ? " (" + _pauseWhy + ")" : "")}.");
            _wantFrames = _moveFrames = _noCc = _pausedFrames = 0; _pauseWhy = null;
        }

        public static void Step(Transform anchor, Quaternion rigRot, float scale, ref Vector3 recenterHeadLocal, Vector3 head)
        {
            if (Time.frameCount == _lastFrame) return;
            _lastFrame = Time.frameCount;
            // 1. what the character has moved since the last frame -> the view stays where the head is
            if (_moved.sqrMagnitude > 0f)
            {
                Vector3 local = Quaternion.Inverse(rigRot) * _moved / Mathf.Max(1e-4f, scale);
                recenterHeadLocal += new Vector3(local.x, 0f, local.z);
                _moved = Vector3.zero;
            }
            _hasWant = false;
            if (!Enabled || anchor == null) return;
            var actor = MimesisInput.LocalActor;
            if (actor == null) return;
            try { if (actor.dead) return; } catch { } // death camera: never move the body
            // 2. only while the game camera follows OUR character (it is a separate object that follows its camera root)
            Diag();
            var root = actor.FpvCameraRoot;
            Vector3 flat = anchor.position - actor.transform.position; flat.y = 0f;
            float dRoot = root != null ? (anchor.position - root.position).magnitude : -1f;
            if (flat.magnitude > 1.0f && (dRoot < 0f || dRoot > 0.6f))
            {
                _pausedFrames++; _pauseWhy = $"camera '{anchor.name}' {flat.magnitude:F2} m from the character, {dRoot:F2} m from its camera root";
                if (anchor.GetInstanceID() != _loggedAnchor) { _loggedAnchor = anchor.GetInstanceID(); VRModCore.Log("[MimesisVR] Room-scale paused: " + _pauseWhy); }
                return;
            }
            _loggedAnchor = 0;
            // 3. the rest of the head offset is requested for the next Update
            Vector3 d = head - recenterHeadLocal; d.y = 0f;
            if (d.sqrMagnitude < 0.0001f) return; // under 1 cm: wait
            _want = rigRot * (d * scale);
            if (_want.magnitude > 0.5f) _want = _want.normalized * 0.5f;
            _hasWant = true;
            _wantFrames++;
        }

        /// <summary>Called from the game input update (before the camera follows the character).</summary>
        public static void MoveNow()
        {
            float dt = Mathf.Max(1e-3f, Time.unscaledDeltaTime);
            if (!_hasWant) { Decay(); return; }
            _hasWant = false;
            if (MimesisHands.LocalEmote) { Decay(); return; } // build 45: stepping in the room must not cancel an emote
            var actor = MimesisInput.LocalActor;
            if (actor == null || !MimesisInput.VrActive) return;
            try
            {
                // ProtoActor.cc is a property over GetComponent<CharacterController>() (build 31-33 looked for a field and silently did nothing)
                var cc = actor.GetComponent<CharacterController>();
                if (cc == null || !cc.enabled) { Decay(); _noCc++; return; }
                Vector3 before = actor.transform.position;
                cc.Move(_want);
                Vector3 moved = actor.transform.position - before; moved.y = 0f;
                _vel = Vector3.Lerp(_vel, moved / dt, 1f - Mathf.Exp(-dt * 8f));
                _velTime = Time.unscaledTime;
                if (moved.sqrMagnitude < 1e-8f) return;
                _moved += moved;
                _moveFrames++;
                _movedTotal += moved.magnitude;
                if (Time.unscaledTime >= _nextLog) { _nextLog = Time.unscaledTime + 30f; VRModCore.Log($"[MimesisVR] Room-scale: character moved {_movedTotal:F2} m by walking in the room (last 30 s)."); _movedTotal = 0f; }
            }
            catch (Exception ex) { if (Time.unscaledTime >= _nextLog) { _nextLog = Time.unscaledTime + 30f; VRModCore.LogWarning("[MimesisVR] Room-scale move failed: " + ex.Message); } }
        }
    }
}
