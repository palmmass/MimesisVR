using Bifrost.ConstEnum;
using Bifrost.Cooked;
using Mimic.Actors;
using UnityVRMod.Core;

namespace UnityVRMod.Mimesis
{
    /// <summary>
    /// MIMESIS VR: melee hit by a swing of the right hand. A fast hand movement with a melee weapon in hand presses the
    /// game's own attack button for one frame - so the game's wind-up delay, input block while the skill runs and the
    /// server's rules stay as they are (no faster hitting than with the button). The swing is caught early (wind-up),
    /// so the hit lands about when the hand comes forward. Vibration: short pulse when a hit starts, a soft one when
    /// you can hit again.
    /// </summary>
    internal static class MimesisSwing
    {
        /// <summary>0 = button only, 1 = swing only, 2 = both.</summary>
        public static int Mode = 2;
        /// <summary>Hand speed that counts as a swing (m/s).</summary>
        public static float Threshold = 1.8f;
        public static bool Haptics = true;

        private static Vector3 _prevPos;
        private static bool _hasPrev, _releaseNext, _wasBlocked, _waitingReady;
        private static float _cooldownUntil, _speed;
        private static int _logged;
        // impact feedback: strong on a hit, medium at the expected impact time when nothing was hit
        private static float _swingAt = -1f, _hitDelay = 0.6f;
        private static bool _impactDone = true;
        // vibration test sequence (settings button)
        private static readonly List<(float at, float sec, float amp, float freq, string label)> _test = new();
        private static float _testStart;

        /// <summary>Settings button: four pulses on the right hand, 0.9 s apart, with different parameters.</summary>
        public static void StartVibrationTest()
        {
            _test.Clear();
            _test.Add((0.0f, 0.4f, 1f, 0f, "1: 0.4 s, default frequency"));
            _test.Add((0.9f, 0.4f, 1f, 160f, "2: 0.4 s, 160 Hz"));
            _test.Add((1.8f, 0.4f, 1f, 320f, "3: 0.4 s, 320 Hz"));
            _test.Add((2.7f, -1f, 1f, 0f, "4: shortest pulse"));
            _testStart = Time.unscaledTime;
            var c = MimesisInput.Controllers; if (c != null) c._hapticLogged = 0;
            VRModCore.Log("[MimesisVR] Vibration test started (right controller): 4 pulses.");
        }

        // Pulse queue: a new pulse would cut the running one short (that is how the "ready" pulse got lost), so
        // pulses wait for the previous one plus a gap. The headset gives every pulse the same strength, so
        // the kinds differ by count and length: swing 1, miss 2, hit 3, ready 1 short.
        private static readonly Queue<(float sec, float amp)> _pulses = new();
        private static float _pulseFree;
        private static void Pulse(float sec, float amp, int count = 1)
        {
            for (int i = 0; i < count; i++) _pulses.Enqueue((sec, amp));
        }
        public static void PulseSwing() => Pulse(0.06f, 0.5f);
        public static void PulseHit() => Pulse(0.08f, 1f, 3);
        public static void PulseMiss() => Pulse(0.08f, 0.55f, 2);
        public static void PulseReady() => Pulse(0.04f, 0.3f);

        /// <summary>Runs every input update, also while the settings are open.</summary>
        public static void Update()
        {
            if (_pulses.Count > 0 && Time.unscaledTime >= _pulseFree)
            {
                var c0 = MimesisInput.Controllers;
                var (sec, amp) = _pulses.Dequeue();
                c0?.Vibrate(true, sec, amp);
                _pulseFree = Time.unscaledTime + sec + 0.12f;
            }
            if (_test.Count == 0) return;
            var c = MimesisInput.Controllers;
            if (c == null) { _test.Clear(); return; }
            float t = Time.unscaledTime - _testStart;
            while (_test.Count > 0 && t >= _test[0].at)
            {
                var x = _test[0]; _test.RemoveAt(0);
                VRModCore.Log("[MimesisVR] Vibration test pulse " + x.label);
                c.Vibrate(true, x.sec, x.amp, x.freq);
            }
        }

        public static void Install(HarmonyLib.Harmony h)
        {
            var m = HarmonyLib.AccessTools.Method(typeof(ProtoActor), "ResolvePacket_HitTargetSig");
            if (m == null) { VRModCore.LogWarning("[MimesisVR] ResolvePacket_HitTargetSig not found - no vibration on impact."); return; }
            h.Patch(m, postfix: new HarmonyLib.HarmonyMethod(typeof(MimesisSwing), nameof(Hit_Postfix)));
        }

        /// <summary>The server reports that a hit of OUR character struck someone: the strongest pulse.</summary>
        private static void Hit_Postfix(HitTargetSig sig)
        {
            try
            {
                if (!Haptics || sig == null || sig.targetHitInfos == null || sig.targetHitInfos.Count == 0) return;
                var actor = MimesisInput.LocalActor;
                if (actor == null || sig.actorID != actor.ActorID) return;
                PulseHit();
                if (_swingAt > 0f && !_impactDone)
                {
                    float d = Time.unscaledTime - _swingAt;
                    if (d > 0.1f && d < 2f) _hitDelay = Mathf.Lerp(_hitDelay, d, 0.5f); // learn when hits land
                }
                _impactDone = true;
                if (_logged < 40) { _logged++; VRModCore.Log($"[MimesisVR] Hit landed on {sig.targetHitInfos.Count} target(s), {Time.unscaledTime - _swingAt:F2} s after the swing."); }
            }
            catch { }
        }

        /// <summary>True while a melee weapon is in the right hand (cached per frame).</summary>
        public static bool MeleeInHand { get; private set; }

        /// <summary>Button presses with a melee weapon are ignored in "swing only".</summary>
        public static bool BlockButton => Mode == 1 && MeleeInHand;

        /// <summary>Called by the input patch every game input update; returns the injected press / release.</summary>
        public static void Tick(out bool press, out bool release)
        {
            press = false; release = false;
            if (_releaseNext) { _releaseNext = false; release = true; }
            var actor = MimesisInput.LocalActor;
            var c = MimesisInput.Controllers;
            MeleeInHand = false;
            if (actor == null || c == null || !c.Ready || !c.RightPoseValid || !MimesisInput.VrActive) { _hasPrev = false; return; }
            try
            {
                var hi = actor.GetHandheldItem();
                if (hi != null && hi.TryCastMasterInfo<ItemEquipmentInfo>(out var info) && info != null && info.WeaponType == WeaponType.MeleeAttack) MeleeInHand = true;
            }
            catch { }

            // Hand speed in room space (walking with the stick does not count)
            float dt = Mathf.Max(1e-3f, Time.unscaledDeltaTime);
            Vector3 p = c.RightPos;
            float sp = _hasPrev ? (p - _prevPos).magnitude / dt : 0f;
            _prevPos = p; _hasPrev = true;
            _speed = Mathf.Lerp(_speed, sp, 0.5f); // a little smoothing against tracking spikes

            bool blocked = false;
            try { blocked = actor.IsInputBlocked(InputBlockReason.CastingSkill); } catch { }
            // miss: medium pulse at the moment a hit would have landed
            if (!_impactDone && _swingAt > 0f && Time.unscaledTime - _swingAt >= _hitDelay + 0.1f)
            {
                _impactDone = true;
                if (Haptics) PulseMiss();
            }
            // "you can hit again"
            if (_waitingReady && _wasBlocked && !blocked) { _waitingReady = false; if (Haptics) PulseReady(); }
            _wasBlocked = blocked;

            if (!MeleeInHand || Mode == 0 || MimesisInput.PanelVisible || MimesisSettingsUI.BlockGame) return;
            if (blocked || Time.unscaledTime < _cooldownUntil) return;
            if (_speed >= Threshold)
            {
                press = true; _releaseNext = true;
                _cooldownUntil = Time.unscaledTime + 0.5f;
                _waitingReady = true;
                _swingAt = Time.unscaledTime; _impactDone = false;
                if (Haptics) PulseSwing(); // wind-up
                if (_logged < 20) { _logged++; VRModCore.Log($"[MimesisVR] Swing: hit started at hand speed {_speed:F1} m/s."); }
            }
        }
    }
}
