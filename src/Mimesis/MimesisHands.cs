using System.Linq;
using UnityVRMod.Core;

namespace UnityVRMod.Mimesis
{
    /// <summary>
    /// MIMESIS VR: the character's own arms follow the controllers.
    /// Runs in Application.onBeforeRender - after the game's animation and before Unity skins the meshes, so the
    /// skinned body really shows the new pose (changing bones later, at end of frame, had no visible effect).
    /// The animator writes the arm bones again next frame, so nothing has to be restored.
    /// Build 28: the hand ALWAYS follows the controller (with or without an item). A held item sits in its game socket
    /// under the hand (RightHand/Weapon_r/hold*Socket); Weapon_r is held in its rest pose plus a per-item correction
    /// (MimesisItemGrips), so the game's draw/hold animation can no longer turn the item.
    /// Fingers curl with trigger (index) and grip (the rest).
    /// </summary>
    internal static class MimesisHands
    {
        public enum Mode { Bars, Character, None }
        private static Mode _mode = Mode.Character;
        public static Mode HandMode
        {
            get => _mode;
            set { if (value == Mode.Character && _mode != Mode.Character) { _bound = false; _fails = 0; } _mode = value; }
        }
        // Calibration (degrees / metres)
        public static float Pitch = 0f, Roll = 0f, Yaw = 0f;
        public static float WristBack = 0.07f;
        public static float CurlSign = -1f;
        /// <summary>Relaxed bend of the fingers with no button pressed (degrees), so they do not look bent backwards.</summary>
        public static float FingerRest = 25f;
        /// <summary>Where the "straight" finger pose comes from: true = the model's own bind pose (stable), false = the game animation at bind time (old).</summary>
        public static bool FingerBaseFromModel = true;
        /// <summary>Base pose of the item socket: 0 = model rest pose, 1 = game pose frozen 0.6 s after the item is in hand, 2 = as the game animates (no fix).</summary>
        public static int SocketBase = 0;
        // Grip adjustment, applied to the controller pose for hands AND held items (controller space, metres/degrees)
        public static float GripTilt = 50f, GripX = 0f, GripY = 0f, GripZ = 0f;
        // Build 29: arm size (scales) and shoulder offset (metres, body axes: + up / + forward / + outward)
        public static float ArmThickness = 1f, UpperLength = 1f, LowerLength = 1f, HandSize = 1f;
        public static float ShoulderUp = 0f, ShoulderForward = 0f, ShoulderOut = 0f;
        // Fingers: squeeze with an item (0..1 of the full bend), scale of the full bend, spread (degrees), touch reaction
        public static float HoldSqueeze = 0.7f, FingerMax = 1f, FingerSpread = 0f;
        /// <summary>Extra spread per finger (degrees): index, middle, ring, little.</summary>
        public static readonly float[] SpreadPer = new float[4];
        /// <summary>Share of the hand's roll around the forearm that the forearm twist bones take (0..1).</summary>
        public static float ForearmTwist = 0.6f;
        /// <summary>0 = on the animated body, 1 = fixed under the head, 2 = on the body but smoothed (animation jerks damped).</summary>
        public static int ShoulderMode = 0;
        public static bool ShoulderFromHead => ShoulderMode == 1;
        /// <summary>Smoothing speed of the shoulders in mode 2 (1/s, lower = smoother).</summary>
        public static float ShoulderSmooth = 6f;
        /// <summary>Fingers on a held item: true = the game's own hold pose (fits the item), false = the mod's fist.</summary>
        public static bool GameGripFingers = true;
        /// <summary>Hide the held item while it is not in the hand yet (it flies from the back when switching items).</summary>
        public static bool HideItemInTransit = true;
        /// <summary>Moves the whole character model (local view only) forward / up, so the body is under you, not behind.</summary>
        public static float BodyForward = 0f, BodyUp = 0f;
        /// <summary>Tilts the whole model back around the head (degrees): the feet come forward much more than the chest,
        /// nothing stretches (only in your own view; the shadow leans too).</summary>
        public static float BodyTilt = 0f;
        private static Quaternion _bodyRestRot = Quaternion.identity;
        private static int _bodyFor;
        /// <summary>Yaw frame for the arms this frame: head yaw with shoulders on the head, else the body.</summary>
        private static Quaternion _frame = Quaternion.identity;
        /// <summary>Moves only the legs and pelvis forward (the spine and arms stay): the model keeps its feet far behind.</summary>
        public static float LegsForward = 0f;
        private static Transform _hips, _spine;
        private static Vector3 _hipsWritten, _spineWritten, _hipsBase, _spineBase;
        private static bool _legsInit;
        private static Vector3 _bodyRestLocal;
        private static bool _bodyMoved;
        public static bool TouchFingers = true;
        /// <summary>Straightens the spine and neck after the game's animation (0 = as animated, 1 = upright along the
        /// model's vertical): walking leans and looking down no longer bend the chest and neck into the view.
        /// Only in your own view - the others see the animation as before.</summary>
        public static float TorsoUpright = 0.8f;
        /// <summary>With "Body: tilt" the model is lifted / lowered so the lowest foot stays where the animation has it.</summary>
        public static bool BodyAutoLift = true;
        private static readonly List<Transform> _chain = new(), _feet = new();
        private static Vector3 _modelUp = Vector3.up;
        private static float _lift, _nextLiftLog;
        /// <summary>The local player is dead: arms, body offsets and the forearm mounts are left alone.</summary>
        private static bool _deadSkip;
        public static bool LocalDead => _deadSkip;
        private static bool _emoteSkip;
        /// <summary>The local character plays an emote (build 44).</summary>
        public static bool LocalEmote => _emoteSkip;

        /// <summary>Emotes that move the body forward (build 45): 0 = the view follows the character's head
        /// sideways (not up and down), 1 = the view stays, 2 = the own body is hidden while the emote plays.</summary>
        public static int EmoteView;
        /// <summary>World offset added to the rig while an emote plays (EmoteView 0).</summary>
        public static Vector3 EmoteCamOffset;
        private static Vector3 _emoteHead0;
        private static bool _emoteHeadSet;

        private static void EmoteCamera(Mimic.Actors.ProtoActor actor, bool playing)
        {
            Transform head = _chain.Count > 0 ? _chain[_chain.Count - 1] : null;
            if (playing && EmoteView == 0 && head != null && actor != null)
            {
                Vector3 d = head.position - actor.transform.position; d.y = 0f;
                if (!_emoteHeadSet) { _emoteHead0 = d; _emoteHeadSet = true; }
                Vector3 want = d - _emoteHead0;
                EmoteCamOffset = Vector3.Lerp(EmoteCamOffset, want, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 12f));
            }
            else
            {
                if (!playing) _emoteHeadSet = false;
                if (EmoteCamOffset.sqrMagnitude < 1e-8f) { EmoteCamOffset = Vector3.zero; return; }
                EmoteCamOffset = Vector3.Lerp(EmoteCamOffset, Vector3.zero, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 8f));
            }
            if (playing) Refollow?.Invoke(); // this frame's view (the normal path re-follows below)
        }

        /// <summary>Applies the grip adjustment: tilt around the controller's left-right axis (positive = "barrel" down) and offset.</summary>
        public static void AdjustGrip(ref Vector3 pos, ref Quaternion rot, bool right)
        {
            pos += rot * new Vector3(right ? GripX : -GripX, GripY, GripZ);
            if (GripTilt != 0f) rot = rot * Quaternion.Euler(GripTilt, 0f, 0f);
        }

        /// <summary>Set by the camera setup: re-places the VR rig on the current game camera.</summary>
        public static Action Refollow;
        public static Transform Rig;

        private sealed class Arm
        {
            public Transform Upper, Lower, Hand;
            public Vector3 UpperRest, LowerRest, HandRest; // local positions in the model's bind pose
            public Quaternion HandRestRot = Quaternion.identity;
            public float Twist; public bool TwistInit;          // continuous (unwrapped) twist angle
            public Vector3 ShoulderLocal; public bool ShoulderInit; // smoothed shoulder, actor-local
            public readonly List<(Transform t, Quaternion rest)> Bases = new();
            // twist bones (children of the upper / lower arm): transform, bind local pos, bind local rot, share of the twist
            public readonly List<(Transform t, Vector3 pos, Quaternion rot, float share)> LowerTwist = new(), UpperTwist = new();
            public readonly List<(Transform t, Quaternion rest, float spread)> Index = new(), Thumb = new(), Others = new();
        }

        private static Component _puppet;
        private static readonly Arm _left = new(), _right = new();
        private static bool _hooked, _bound;
        private static FieldInfo _puppetField;
        private static float _curlL, _curlR, _gripL, _gripR;
        private static int _fails;
        private static float _nextFailLog;
        // Weapon socket root under the right hand and its rest pose
        private static Transform _weaponR;
        private static Vector3 _weaponRestPos;
        private static Quaternion _weaponRestRot = Quaternion.identity;
        private static string _lastItemName, _notInHandLogged;
        private static int _frozenItem;
        private static float _frozenAt;
        private static bool _frozen;
        private static Vector3 _frozenPos;
        private static Quaternion _frozenRot = Quaternion.identity;

        public static bool CharacterArms => HandMode == Mode.Character;
        private static bool _sizeApplied;

        public static void Ensure()
        {
            if (_hooked) return;
            _hooked = true;
            Application.onBeforeRender += OnBeforeRender;
        }

        /// <summary>True when the held item is a child of the right hand bone (then it follows the hand by itself).</summary>
        public static bool ItemFollowsHand(Transform item)
        {
            return item != null && _bound && _right.Hand != null && item.IsChildOf(_right.Hand);
        }

        private static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform ch in root) { var r = Find(ch, name); if (r != null) return r; }
            return null;
        }

        // ---------------------------------------------------------------- bind pose of the skinned body
        private static readonly Dictionary<Transform, Matrix4x4> _bindPose = new();

        private static void ReadBindPose(Transform puppetRoot)
        {
            _bindPose.Clear();
            try
            {
                SkinnedMeshRenderer best = null;
                foreach (var sm in puppetRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (sm != null && sm.sharedMesh != null && sm.bones != null && (best == null || sm.bones.Length > best.bones.Length)) best = sm;
                if (best == null) return;
                var bones = best.bones; var bp = best.sharedMesh.bindposes;
                for (int i = 0; i < bones.Length && i < bp.Length; i++) if (bones[i] != null) _bindPose[bones[i]] = bp[i];
            }
            catch (Exception ex) { VRModCore.LogWarning("[MimesisVR] Bind pose read failed: " + ex.Message); }
        }

        /// <summary>Local pose of a bone in the model's bind pose (needs the bone and its parent to be skin bones).</summary>
        private static bool BindLocal(Transform t, out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = Quaternion.identity;
            if (t == null || t.parent == null) return false;
            if (!_bindPose.TryGetValue(t, out var bc) || !_bindPose.TryGetValue(t.parent, out var bpar)) return false;
            Matrix4x4 m = bpar * bc.inverse; // parent-from-child at bind time
            pos = m.GetColumn(3);
            rot = m.rotation;
            return true;
        }

        /// <summary>Finger bones: only real skin bones of the model, never anything inside the weapon socket or a held item.</summary>
        private static void Fingers(Arm a)
        {
            a.Index.Clear(); a.Thumb.Clear(); a.Others.Clear(); a.Bases.Clear();
            if (a.Hand == null) return;
            int fromModel = 0, fromAnim = 0;
            void Walk(Transform p)
            {
                foreach (Transform t in p)
                {
                    string n = t.name.ToLowerInvariant();
                    if (n.Contains("socket") || n.StartsWith("weapon")) continue; // do not descend into sockets / items
                    if (_bindPose.Count > 0 && !_bindPose.ContainsKey(t)) continue; // not a bone of the body mesh
                    if (n.Contains("base"))
                    {
                        // metacarpal: not bent, but held in the model pose (hold animations squeeze / spread them)
                        if (BindLocal(t, out _, out Quaternion bb)) a.Bases.Add((t, bb));
                    }
                    else // finger bone
                    {
                        Quaternion rest = t.localRotation;
                        if (FingerBaseFromModel && BindLocal(t, out _, out Quaternion br)) { rest = br; fromModel++; } else fromAnim++;
                        // spread only on the first bone of a finger (its parent is the hand or a metacarpal)
                        bool first = t.parent == a.Hand || t.parent.name.ToLowerInvariant().Contains("base");
                        float sp = !first ? 0f : n.Contains("index") ? 1f : n.Contains("middle") ? 0.35f : n.Contains("ring") ? -0.35f : (n.Contains("pinky") || n.Contains("little")) ? -1f : 0f;
                        if (first) _fingerOf[t] = n.Contains("index") ? 0 : n.Contains("middle") ? 1 : n.Contains("ring") ? 2 : 3;
                        if (n.Contains("index")) a.Index.Add((t, rest, sp));
                        else if (n.Contains("thumb")) a.Thumb.Add((t, rest, 0f));
                        else if (n.Contains("middle") || n.Contains("ring") || n.Contains("pinky") || n.Contains("little")) a.Others.Add((t, rest, sp));
                    }
                    Walk(t);
                }
            }
            Walk(a.Hand);
            VRModCore.Log($"[MimesisVR] Fingers {a.Hand.name}: index {a.Index.Count}, thumb {a.Thumb.Count}, other {a.Others.Count}; rest pose from model {fromModel}, from animation {fromAnim}.");
        }

        private static readonly Dictionary<Transform, int> _fingerOf = new();

        private static void Twists(Arm a)
        {
            a.LowerTwist.Clear(); a.UpperTwist.Clear();
            void Collect(Transform bone, List<(Transform, Vector3, Quaternion, float)> list, float len)
            {
                foreach (Transform ch in bone)
                {
                    if (ch.name.IndexOf("twist", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Vector3 p = ch.localPosition; Quaternion r = ch.localRotation;
                    if (BindLocal(ch, out var bp, out var br)) { p = bp; r = br; }
                    // the closer to the far joint, the larger the share of the twist
                    float share = len > 1e-4f ? Mathf.Clamp01(p.magnitude / len) : 0.5f;
                    list.Add((ch, p, r, share));
                }
            }
            Collect(a.Lower, a.LowerTwist, a.HandRest.magnitude);
            Collect(a.Upper, a.UpperTwist, a.LowerRest.magnitude);
            VRModCore.Log($"[MimesisVR] Twist bones {a.Hand.name}: forearm {string.Join(", ", a.LowerTwist.Select(x => $"{x.t.name} {x.share:F2}"))}; upper arm {string.Join(", ", a.UpperTwist.Select(x => $"{x.t.name} {x.share:F2}"))}");
        }

        /// <summary>Forearm twist bones take a share of the hand's roll around the forearm, so the wrist does not
        /// wring like a wet cloth; their positions follow the forearm length. Upper arm twist bones follow the length only.</summary>
        private static void ApplyTwist(Arm a)
        {
            if (a.LowerTwist.Count > 0)
            {
                // hand rotation relative to the forearm, compared with the bind pose; twist part around the forearm axis (local Y)
                Quaternion q = Quaternion.Inverse(a.Lower.rotation * a.HandRestRot) * a.Hand.rotation;
                float raw = Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(q.y, q.w) * Mathf.Rad2Deg);
                // continuous angle: no jump to the other side when the twist passes 180 degrees
                float ang = a.TwistInit ? a.Twist + Mathf.DeltaAngle(a.Twist, raw) : raw;
                if (Mathf.Abs(ang) > 270f) ang = raw; // wound up too far (e.g. after recentering) - start over
                a.Twist = ang; a.TwistInit = true;
                foreach (var (t, pos, rot, share) in a.LowerTwist)
                {
                    if (t == null) continue;
                    t.localPosition = pos * (LowerLength / Mathf.Max(0.2f, ArmThickness)); // spread evenly along the longer forearm
                    t.localRotation = Quaternion.AngleAxis(ang * ForearmTwist * share, Vector3.up) * rot;
                }
            }
            foreach (var (t, pos, rot, share) in a.UpperTwist)
            {
                if (t == null) continue;
                t.localPosition = pos * (UpperLength / Mathf.Max(0.2f, ArmThickness));
                t.localRotation = rot;
            }
        }

        private static void ArmRest(Arm a)
        {
            a.HandRestRot = BindLocal(a.Hand, out _, out var hrr) ? hrr : a.Hand.localRotation;
            a.UpperRest = BindLocal(a.Upper, out var p0, out _) ? p0 : a.Upper.localPosition;
            a.LowerRest = BindLocal(a.Lower, out var p1, out _) ? p1 : a.Lower.localPosition;
            a.HandRest = BindLocal(a.Hand, out var p2, out _) ? p2 : a.Hand.localPosition;
        }

        /// <summary>Arm size: uniform scales only (no skew) - the upper arm is scaled by the thickness, lengths are put
        /// back by moving the child bones, the hand gets its own size. Set every frame from the bind pose values.</summary>
        private static void ApplySize(Arm a, bool right, Transform body)
        {
            float th = Mathf.Max(0.2f, ArmThickness);
            a.Upper.localScale = Vector3.one * th;
            Vector3 off = body.rotation * new Vector3(right ? ShoulderOut : -ShoulderOut, ShoulderUp, ShoulderForward);
            var par = a.Upper.parent;
            if (ShoulderFromHead && Rig != null)
            {
                // fixed relative to the head (body yaw): animations of the body no longer move the arms
                Vector3 head = Rig.TransformPoint(MimesisInput.HeadLocalPos);
                Quaternion yaw = _frame; // head yaw of THIS frame (the body turns a frame later after a snap turn)
                a.Upper.position = head + yaw * new Vector3(right ? 0.17f + ShoulderOut : -0.17f - ShoulderOut, -0.22f + ShoulderUp, -0.06f + ShoulderForward);
            }
            else
            {
                a.Upper.localPosition = a.UpperRest + (par != null ? Quaternion.Inverse(par.rotation) * off / Mathf.Max(1e-4f, par.lossyScale.x) : off);
                if (ShoulderMode == 2)
                {
                    // smooth in the actor's own space: walking / turning are not delayed, animation jerks are
                    Vector3 l = body.InverseTransformPoint(a.Upper.position);
                    if (!a.ShoulderInit || (l - a.ShoulderLocal).sqrMagnitude > 0.25f) { a.ShoulderLocal = l; a.ShoulderInit = true; }
                    else a.ShoulderLocal = Vector3.Lerp(a.ShoulderLocal, l, 1f - Mathf.Exp(-Mathf.Max(0.5f, ShoulderSmooth) * Time.unscaledDeltaTime));
                    a.Upper.position = body.TransformPoint(a.ShoulderLocal);
                }
            }
            a.Lower.localScale = Vector3.one;
            a.Lower.localPosition = a.LowerRest * (UpperLength / th);
            a.Hand.localPosition = a.HandRest * (LowerLength / th);
            a.Hand.localScale = Vector3.one * (Mathf.Max(0.2f, HandSize) / th);
            _sizeApplied = true;
        }

        /// <summary>Body offset: the model's rest place under the character is taken once per model, then shifted.</summary>
        private static void ApplyBodyOffset(Transform body)
        {
            if (_puppet == null) return;
            var pt = _puppet.transform;
            if (pt == body) return;
            int id = pt.GetInstanceID();
            if (id != _bodyFor)
            {
                _bodyFor = id;
                _bodyRestLocal = pt.parent == body ? pt.localPosition : body.InverseTransformPoint(pt.position);
                _bodyRestRot = Quaternion.Inverse(body.rotation) * pt.rotation;
            }
            _modelUp = body.up;
            if (BodyForward == 0f && BodyUp == 0f && BodyTilt == 0f && !_bodyMoved) return;
            Vector3 p0 = body.TransformPoint(_bodyRestLocal + new Vector3(0f, 0f, BodyForward));
            Quaternion r0 = body.rotation * _bodyRestRot;
            _lift = 0f;
            if (BodyTilt != 0f)
            {
                // pivot at the camera root (the head): feet swing forward, the chest only a little
                var actor = MimesisInput.LocalActor;
                Vector3 pivot = actor != null && actor.FpvCameraRoot != null ? actor.FpvCameraRoot.position : body.position + body.up * 1.6f;
                Quaternion q = Quaternion.AngleAxis(-BodyTilt, body.right);
                p0 = pivot + q * (p0 - pivot);
                r0 = q * r0;
                _modelUp = q * body.up;
                if (BodyAutoLift && _feet.Count > 0)
                {
                    // the lowest foot as animated (model at rest) vs. after the tilt: lift by the difference
                    Vector3 pRest = body.TransformPoint(_bodyRestLocal);
                    Quaternion rRest = body.rotation * _bodyRestRot;
                    Vector3 sc = pt.lossyScale;
                    float minRest = float.MaxValue, minNew = float.MaxValue;
                    foreach (var f in _feet)
                    {
                        if (f == null) continue;
                        Vector3 l = Vector3.Scale(pt.InverseTransformPoint(f.position), sc);
                        minRest = Mathf.Min(minRest, Vector3.Dot(pRest + rRest * l, body.up));
                        minNew = Mathf.Min(minNew, Vector3.Dot(p0 + r0 * l, body.up));
                    }
                    if (minRest < float.MaxValue && minNew < float.MaxValue)
                    {
                        _lift = Mathf.Clamp(minRest - minNew, -0.5f, 0.5f);
                        p0 += body.up * _lift;
                        if (Time.unscaledTime >= _nextLiftLog) { _nextLiftLog = Time.unscaledTime + 30f; VRModCore.Log($"[MimesisVR] Body tilt {BodyTilt:F0}°: feet kept on the ground, model moved {_lift * 100f:+0;-0;0} cm (plus 'Body: down/up' {BodyUp * 100f:+0;-0;0} cm)."); }
                    }
                }
            }
            p0 += body.up * BodyUp;
            pt.SetPositionAndRotation(p0, r0);
            _bodyMoved = BodyForward != 0f || BodyUp != 0f || BodyTilt != 0f;
        }

        /// <summary>Pose on the back of the model's left forearm (rig-local), like a watch; false without character arms.</summary>
        public static bool LeftForearm(Transform rig, out Vector3 pos, out Quaternion rot) => Forearm(false, rig, out pos, out rot);

        /// <summary>Pose on the back of the model's forearm (rig-local): +Y along the arm to the hand, +Z into the arm,
        /// +X to the side; false without character arms (or when the local player is dead).
        /// Build 41: the side facing up turns with the forearm skin, not with the whole hand - the skin under a watch
        /// twists only by the share of the twist bones there (about ForearmTwist x the place along the forearm), so a
        /// watch that turned with the hand had the skin's edges poking through it. twistFollow scales that share,
        /// alongShift is where the watch sits (from the middle of the forearm) - used for the share.</summary>
        public static bool Forearm(bool right, Transform rig, out Vector3 pos, out Quaternion rot, float twistFollow = 1f, float alongShift = 0f)
        {
            pos = default; rot = Quaternion.identity;
            var arm = right ? _right : _left;
            if (!CharacterArms || !_bound || rig == null || arm.Lower == null || arm.Hand == null || _deadSkip || _emoteSkip) return false;
            Vector3 a = arm.Lower.position, b = arm.Hand.position;
            Vector3 along = b - a;
            float len = along.magnitude;
            if (len < 1e-3f) return false;
            along /= len;
            // back of the hand without any turn of the hand against the forearm (bind pose), and the hand as it is now
            Quaternion restFrame = arm.Lower.rotation * arm.HandRestRot;
            Vector3 dRest = restFrame * Vector3.forward, dHand = arm.Hand.rotation * Vector3.forward;
            dRest -= Vector3.Project(dRest, along); dHand -= Vector3.Project(dHand, along);
            Vector3 dorsal;
            if (dRest.sqrMagnitude < 1e-6f) dorsal = dHand;
            else if (dHand.sqrMagnitude < 1e-6f) dorsal = dRest;
            else
            {
                float f = Mathf.Clamp01(0.55f + alongShift / len);
                float k = Mathf.Clamp(ForearmTwist * f * twistFollow, 0f, 1.5f);
                // continuous twist angle where available (no flip past 180 degrees), else the plain angle
                float ang = arm.TwistInit ? arm.Twist : Vector3.SignedAngle(dRest, dHand, restFrame * Vector3.up);
                dorsal = Quaternion.AngleAxis(ang * k, restFrame * Vector3.up) * dRest;
                dorsal -= Vector3.Project(dorsal, along);
            }
            if (dorsal.sqrMagnitude < 1e-6f) return false;
            dorsal.Normalize();
            Vector3 p = Vector3.Lerp(a, b, 0.55f) + dorsal * 0.035f;
            Quaternion r = Quaternion.LookRotation(-dorsal, along);
            pos = rig.InverseTransformPoint(p);
            rot = Quaternion.Inverse(rig.rotation) * r;
            return true;
        }

        /// <summary>Head yaw from the rig this frame (stable when looking straight down / up).</summary>
        private static Quaternion HeadYaw(Transform rig)
        {
            Quaternion hw = rig.rotation * MimesisInput.HeadLocalRot;
            Vector3 f = hw * Vector3.forward, u = hw * Vector3.up;
            Vector3 d = new Vector3(f.x, 0f, f.z) - new Vector3(u.x, 0f, u.z) * f.y;
            return d.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(d.normalized, Vector3.up) : Quaternion.Euler(0f, rig.eulerAngles.y, 0f);
        }

        /// <summary>Build 42: the model's hands stop at walls and objects instead of going through them (a sphere cast
        /// from the head to the hand; your own body, what you hold and loose physics items are ignored).</summary>
        public static bool BlockByWalls = true;
        private static readonly RaycastHit[] _wallHits = new RaycastHit[16];
        private static float _nextWallLog;

        /// <summary>Build 43: the fingertips too, not only the controller point - the model's hand is longer than the
        /// controller, so the whole hand is moved back by what the fingertips would go into the wall.</summary>
        private static Vector3 KeepHandOut(Vector3 gp, Quaternion handRot, Transform rig, Mimic.Actors.ProtoActor actor, Transform item)
        {
            if (!BlockByWalls) return gp;
            Vector3 tip = gp + (handRot * Vector3.up) * (0.17f * HandSize);
            Vector3 tip2 = KeepOutOfWalls(tip, rig, actor, item);
            gp += tip2 - tip;
            return KeepOutOfWalls(gp, rig, actor, item);
        }

        private static Vector3 KeepOutOfWalls(Vector3 target, Transform rig, Mimic.Actors.ProtoActor actor, Transform item)
        {
            if (!BlockByWalls || rig == null || actor == null) return target;
            Vector3 origin = rig.TransformPoint(MimesisInput.HeadLocalPos);
            Vector3 d = target - origin;
            float len = d.magnitude;
            if (len < 0.05f) return target;
            d /= len;
            const float radius = 0.035f;
            int n = Physics.SphereCastNonAlloc(origin, radius, d, _wallHits, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = len; Collider by = null;
            for (int i = 0; i < n; i++)
            {
                var h = _wallHits[i];
                var col = h.collider;
                if (col == null || h.distance <= 0f) continue; // started inside: never trap the hand
                var t = col.transform;
                if (t.IsChildOf(actor.transform) || (_puppet != null && t.IsChildOf(_puppet.transform)) || (item != null && t.IsChildOf(item))) continue;
                var rb = col.attachedRigidbody;
                if (rb != null && !rb.isKinematic) continue; // loose items: the hand may go into them
                if (h.distance < best) { best = h.distance; by = col; }
            }
            if (by == null) return target;
            if (Time.unscaledTime >= _nextWallLog) { _nextWallLog = Time.unscaledTime + 20f; VRModCore.Log($"[MimesisVR] Hand stopped by '{by.name}' (layer {LayerMask.LayerToName(by.gameObject.layer)}), {(len - best) * 100f:F0} cm short."); }
            return origin + d * best;
        }

        private static float _deadSince = -1f, _nextDeadLog;
        private static readonly List<Renderer> _hiddenDead = new();

        /// <summary>Build 41: while dead the game's camera sits at the head of your own body - turning the head took the
        /// eyes into the model. Your own body (and what it holds) is not drawn then; only in your view.</summary>
        private static void HideOwnBody(Mimic.Actors.ProtoActor actor, bool hide)
        {
            try
            {
                if (hide)
                {
                    if (_hiddenDead.Count > 0 || actor == null) return;
                    foreach (var r in actor.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r == null || r.forceRenderingOff) continue;
                        r.forceRenderingOff = true; _hiddenDead.Add(r);
                    }
                    VRModCore.Log($"[MimesisVR] Dead: own body hidden in your view ({_hiddenDead.Count} renderers).");
                }
                else
                {
                    foreach (var r in _hiddenDead) if (r != null) r.forceRenderingOff = false;
                    _hiddenDead.Clear();
                }
            }
            catch { }
        }
        /// <summary>First 10 s after death: where the game camera and the rig are (once a second).</summary>
        private static void DeadDiag(Mimic.Actors.ProtoActor actor)
        {
            if (_deadSince < 0f) { _deadSince = Time.unscaledTime; _nextDeadLog = 0f; }
            if (Time.unscaledTime - _deadSince > 10f || Time.unscaledTime < _nextDeadLog) return;
            _nextDeadLog = Time.unscaledTime + 1f;
            try
            {
                var cam = MimesisInput.GameCamera;
                var root = actor.FpvCameraRoot;
                VRModCore.Log($"[MimesisVR] Death camera +{Time.unscaledTime - _deadSince:F0}s: camera '{(cam != null ? cam.name : "-")}' at {(cam != null ? cam.position.ToString("F2") : "-")}, " +
                              $"actor at {actor.transform.position:F2}, camera root {(root != null ? root.position.ToString("F2") : "-")}, rig {(Rig != null ? Rig.position.ToString("F2") : "-")}, height offset {MimesisSettings.HeightOffset:F2}.");
            }
            catch { }
        }

        /// <summary>Torso upright: each spine / neck segment is turned towards the model's vertical (share TorsoUpright).
        /// The game's animation writes these bones every frame, so nothing accumulates.</summary>
        private static void ApplyUpright()
        {
            if (TorsoUpright <= 0f || _chain.Count < 2) return;
            Vector3 up = _modelUp.sqrMagnitude > 1e-6f ? _modelUp.normalized : Vector3.up;
            float k = Mathf.Clamp01(TorsoUpright);
            for (int i = 0; i < _chain.Count - 1; i++)
            {
                Transform b = _chain[i], n = _chain[i + 1];
                if (b == null || n == null) continue;
                Vector3 d = n.position - b.position;
                if (d.sqrMagnitude < 1e-8f) continue;
                Quaternion q = Quaternion.FromToRotation(d, up);
                b.rotation = Quaternion.Slerp(Quaternion.identity, q, k) * b.rotation;
            }
        }

        /// <summary>Pelvis and legs forward, the spine moved back by the same amount (only the waist stretches).
        /// Works whether or not the animation writes these bones every frame.</summary>
        private static void ApplyLegs(Transform body)
        {
            if (_hips == null || _spine == null) return;
            if (!_legsInit) { _hipsBase = _hips.localPosition; _spineBase = _spine.localPosition; _legsInit = true; _hipsWritten = new Vector3(float.NaN, 0, 0); }
            // if the animation did not write the bone since our last write, start from the saved base, not our value
            Vector3 h = _hips.localPosition == _hipsWritten ? _hipsBase : _hips.localPosition;
            Vector3 sp = _spine.localPosition == _spineWritten ? _spineBase : _spine.localPosition;
            _hipsBase = h; _spineBase = sp;
            if (LegsForward == 0f) { _hips.localPosition = h; _spine.localPosition = sp; _hipsWritten = h; _spineWritten = sp; return; }
            _hips.localPosition = h;
            _spine.localPosition = sp;
            Vector3 world = body.forward * LegsForward;
            _hips.position += world;
            _spine.position -= world;
            _hipsWritten = _hips.localPosition; _spineWritten = _spine.localPosition;
        }

        private static void RestoreBody()
        {
            if (_legsInit && _hips != null && _spine != null && LegsForward != 0f) { _hips.localPosition = _hipsBase; _spine.localPosition = _spineBase; _legsInit = false; }
            if (!_bodyMoved || _puppet == null) return;
            var pt = _puppet.transform;
            var body = pt.parent;
            if (body != null) { pt.localPosition = _bodyRestLocal; pt.localRotation = _bodyRestRot; }
            _bodyMoved = false;
        }

        private static void RestoreSize()
        {
            foreach (var a in new[] { _left, _right })
            {
                if (a.Upper == null || a.Lower == null || a.Hand == null) continue;
                a.Upper.localScale = Vector3.one; a.Lower.localScale = Vector3.one; a.Hand.localScale = Vector3.one;
                a.Upper.localPosition = a.UpperRest; a.Lower.localPosition = a.LowerRest; a.Hand.localPosition = a.HandRest;
                foreach (var (t, pos, rot, _) in a.LowerTwist) if (t != null) { t.localPosition = pos; t.localRotation = rot; }
                foreach (var (t, pos, rot, _) in a.UpperTwist) if (t != null) { t.localPosition = pos; t.localRotation = rot; }
            }
            if (_weaponR != null) _weaponR.localScale = Vector3.one;
            _sizeApplied = false;
        }

        private static bool Bind(Component puppet)
        {
            if (puppet == _puppet && _bound) return true;
            _puppet = puppet; _bound = false;
            if (puppet == null) return false;
            var t = puppet.transform;
            _hips = Find(t, "Hips"); _spine = Find(t, "Spine"); _legsInit = false;
            _chain.Clear();
            // each bone is looked for under the previous one ("Head" is also the name of a renderer object)
            Transform cur = _hips != null ? _hips : t;
            foreach (var bn in new[] { "Spine", "Chest", "UpperChest", "Neck", "Head" }) { var bt = Find(cur, bn); if (bt != null) { _chain.Add(bt); cur = bt; } }
            _feet.Clear();
            foreach (var bn in new[] { "LeftFoot", "RightFoot", "LeftToes", "RightToes" }) { var bt = Find(t, bn); if (bt != null) _feet.Add(bt); }
            if (_feet.Count == 0 && _hips != null)
                foreach (var bt in _hips.GetComponentsInChildren<Transform>(true))
                {
                    string ln = bt.name.ToLowerInvariant();
                    if ((ln.Contains("foot") || ln.Contains("toe")) && !ln.Contains("socket")) _feet.Add(bt);
                }
            VRModCore.Log($"[MimesisVR] Body: spine chain {string.Join("/", _chain.Select(x => x.name))}, feet {string.Join(", ", _feet.Select(x => x.name))}.");
            _left.Upper = Find(t, "LeftUpperArm"); _left.Lower = Find(t, "LeftLowerArm"); _left.Hand = Find(t, "LeftHand");
            _right.Upper = Find(t, "RightUpperArm"); _right.Lower = Find(t, "RightLowerArm"); _right.Hand = Find(t, "RightHand");
            _bound = _left.Hand != null && _right.Hand != null && _left.Upper != null && _right.Upper != null && _left.Lower != null && _right.Lower != null;
            ReadBindPose(t);
            if (_bound) { ArmRest(_left); ArmRest(_right); _fingerOf.Clear(); }
            _left.TwistInit = _right.TwistInit = false; _left.ShoulderInit = _right.ShoulderInit = false;
            Fingers(_left); Fingers(_right);
            if (_bound) { Twists(_left); Twists(_right); }
            _weaponR = null;
            if (_right.Hand != null)
                foreach (Transform ch in _right.Hand) if (ch.name.StartsWith("Weapon", StringComparison.OrdinalIgnoreCase)) { _weaponR = ch; break; }
            string src = "none";
            if (_weaponR != null)
            {
                if (BindLocal(_weaponR, out _weaponRestPos, out _weaponRestRot)) src = "model bind pose";
                else { _weaponRestPos = _weaponR.localPosition; _weaponRestRot = _weaponR.localRotation; src = "current pose"; }
            }
            VRModCore.Log($"[MimesisVR] Character arms: bones {(_bound ? "found" : "NOT found")} on '{puppet.name}', bind pose bones {_bindPose.Count}; " +
                          $"weapon socket '{(_weaponR != null ? _weaponR.name : "-")}' rest from {src}: pos {_weaponRestPos} rot {_weaponRestRot.eulerAngles}" +
                          (_weaponR != null ? $" (now pos {_weaponR.localPosition} rot {_weaponR.localRotation.eulerAngles})" : ""));
            return _bound;
        }

        private static void OnBeforeRender()
        {
            MimesisWatches.Watchdog();
            if (!CharacterArms || !MimesisInput.VrActive)
            {
                if (_sizeApplied) { try { RestoreSize(); } catch { _sizeApplied = false; } }
                try { RestoreBody(); } catch { _bodyMoved = false; }
                return;
            }
            var actor = MimesisInput.LocalActor;
            var c = MimesisInput.Controllers;
            bool dead = false;
            try { dead = actor != null && actor.dead; } catch { }
            if (dead != _deadSkip)
            {
                _deadSkip = dead;
                VRModCore.Log(dead ? "[MimesisVR] Local player dead: arms and body offsets off (the game's death camera is left alone)." : "[MimesisVR] Local player alive again: arms and body back.");
            }
            if (dead)
            {
                if (_sizeApplied) { try { RestoreSize(); } catch { _sizeApplied = false; } }
                try { RestoreBody(); } catch { _bodyMoved = false; }
                HideOwnBody(actor, true);
                DeadDiag(actor);
                return;
            }
            _deadSince = -1f;
            // Build 44: an emote plays - arms, body turn and offsets are left to the animation
            bool emote = MimesisBinds.LocalEmote(actor);
            if (_hiddenDead.Count > 0 && !(emote && EmoteView == 2)) HideOwnBody(actor, false);
            if (emote != _emoteSkip)
            {
                _emoteSkip = emote;
                VRModCore.Log(emote ? "[MimesisVR] Emote playing: arms and body follow the animation." : "[MimesisVR] Emote over: arms and body back.");
            }
            if (emote)
            {
                if (_sizeApplied) { try { RestoreSize(); } catch { _sizeApplied = false; } }
                try { RestoreBody(); } catch { _bodyMoved = false; }
                EmoteCamera(actor, true);
                if (EmoteView == 2) HideOwnBody(actor, true);
                return;
            }
            EmoteCamera(actor, false);
            if (actor == null || c == null || !c.Ready) { Diag(actor == null ? "no local actor" : "controllers not ready"); return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                Refollow?.Invoke(); // rig on this frame's camera position, so the arms do not lag one frame
                var rig = Rig;
                if (rig == null) { Diag("no rig"); return; }
                // the body turns with the head in THIS frame (the game turns it a frame later - a jerk after a snap turn)
                if (MimesisInput.HeadValid) actor.transform.rotation = HeadYaw(rig);
                _puppetField ??= typeof(Mimic.Actors.ProtoActor).GetField("puppet", BindingFlags.Instance | BindingFlags.NonPublic);
                if (!Bind(_puppetField?.GetValue(actor) as Component)) { Diag("bones not bound"); return; }
                ApplyBodyOffset(actor.transform);
                _frame = ShoulderFromHead ? HeadYaw(rig) : Quaternion.Euler(0f, actor.transform.eulerAngles.y, 0f);
                ApplyLegs(actor.transform);
                ApplyUpright();
                Diag(c.RightPoseValid && c.LeftPoseValid ? null : "controller not tracked");

                float dt = Time.unscaledDeltaTime * 14f;
                _curlR = Mathf.Lerp(_curlR, c.Axes[(int)Features.VRVisualization.OpenXR.XrControllerInput.F.RTrigger], Mathf.Clamp01(dt));
                _gripR = Mathf.Lerp(_gripR, c.Axes[(int)Features.VRVisualization.OpenXR.XrControllerInput.F.RGrip], Mathf.Clamp01(dt));
                _curlL = Mathf.Lerp(_curlL, c.Axes[(int)Features.VRVisualization.OpenXR.XrControllerInput.F.LTrigger], Mathf.Clamp01(dt));
                _gripL = Mathf.Lerp(_gripL, c.Axes[(int)Features.VRVisualization.OpenXR.XrControllerInput.F.LGrip], Mathf.Clamp01(dt));

                if (c.RightPoseValid)
                {
                    Transform item = null;
                    try { var hi = actor.GetHandheldItem(); if (hi != null) item = hi.Transform; } catch { }
                    bool inHand = item != null && item.IsChildOf(_right.Hand);

                    Vector3 gp = rig.TransformPoint(c.RightPos); Quaternion gq = rig.rotation * c.RightRot;
                    AdjustGrip(ref gp, ref gq, true);
                    Quaternion hr = HandRotation(gq, true);
                    gp = KeepHandOut(gp, hr, rig, actor, item);
                    ApplySize(_right, true, actor.transform);
                    Solve(_right, gp - (hr * Vector3.up) * (WristBack * HandSize), hr, actor.transform, true);
                    ApplyTwist(_right);

                    if (inHand)
                    {
                        string key = MimesisItemGrips.KeyOf(item);
                        MimesisItemGrips.Current = key;
                        if (key != _lastItemName)
                        {
                            _lastItemName = key;
                            if (_weaponR != null)
                                VRModCore.Log($"[MimesisVR] In hand '{key}' (parent '{item.parent?.name}'): weapon socket from game pos {_weaponR.localPosition} rot {_weaponR.localRotation.eulerAngles}, rest pos {_weaponRestPos} rot {_weaponRestRot.eulerAngles}");
                        }
                        // Socket base pose (see SocketBase)
                        Vector3 basePos = _weaponRestPos; Quaternion baseRot = _weaponRestRot;
                        if (_weaponR != null && SocketBase == 1)
                        {
                            int iid = item.GetInstanceID();
                            if (iid != _frozenItem) { _frozenItem = iid; _frozenAt = Time.unscaledTime; _frozen = false; }
                            if (!_frozen)
                            {
                                // until frozen, follow the game's pose (it is written again by the animator every frame)
                                _frozenPos = _weaponR.localPosition; _frozenRot = _weaponR.localRotation;
                                if (Time.unscaledTime - _frozenAt > 0.6f) { _frozen = true; VRModCore.Log($"[MimesisVR] Socket pose frozen for '{key}': pos {_frozenPos} rot {_frozenRot.eulerAngles}"); }
                            }
                            basePos = _frozenPos; baseRot = _frozenRot;
                        }
                        bool waiting = SocketBase == 1 && !_frozen; // do not write while sampling the game pose
                        if (_weaponR != null && MimesisVrExtras.ItemFollowsController && SocketBase != 2 && !waiting)
                        {
                            var g = MimesisItemGrips.Get(key);
                            // Correction is given in CONTROLLER axes (X right, Y up, Z forward; pitch + = barrel down)
                            // and converted into the hand bone's space (the parent of the weapon socket)
                            Quaternion toCtrl = Quaternion.Inverse(hr) * gq;
                            Quaternion corr = toCtrl * Quaternion.Euler(g.Pitch, g.Yaw, g.Roll) * Quaternion.Inverse(toCtrl);
                            _weaponR.localRotation = corr * baseRot;
                            float hs = Mathf.Max(0.2f, HandSize);
                            _weaponR.localPosition = basePos + toCtrl * new Vector3(g.X, g.Y, g.Z) / hs;
                            _weaponR.localScale = Vector3.one / hs; // the item keeps its real size whatever the hand size
                        }
                        if (!GameGripFingers) Curl(_right, HoldSqueeze, HoldSqueeze, HoldSqueeze, HoldSqueeze, true); // mod fist
                        // else: the game's hold animation already posed the fingers for this item
                    }
                    else
                    {
                        if (item != null)
                        {
                            string key = MimesisItemGrips.KeyOf(item);
                            if (key != _notInHandLogged) { _notInHandLogged = key; VRModCore.Log($"[MimesisVR] Held item '{key}' is not under the right hand (parent '{item.parent?.name}') - left where the game puts it."); }
                        }
                        MimesisItemGrips.Current = null;
                        _lastItemName = null;
                        _frozenItem = 0;
                        if (_weaponR != null) _weaponR.localScale = Vector3.one;
                        Curl(_right, _curlR, _gripR, Touch(c, true, true), Touch(c, true, false), true);
                    }
                }
                if (c.LeftPoseValid)
                {
                    Vector3 gp = rig.TransformPoint(c.LeftPos); Quaternion gq = rig.rotation * c.LeftRot;
                    AdjustGrip(ref gp, ref gq, false);
                    Quaternion hl = HandRotation(gq, false);
                    gp = KeepHandOut(gp, hl, rig, actor, null);
                    ApplySize(_left, false, actor.transform);
                    Solve(_left, gp - (hl * Vector3.up) * (WristBack * HandSize), hl, actor.transform, false);
                    ApplyTwist(_left);
                    Curl(_left, _curlL, _gripL, Touch(c, false, true), Touch(c, false, false), false);
                }
                _fails = 0;
            }
            catch (Exception ex)
            {
                // Re-bind next frame instead of giving up; only many failures in a row switch to bars
                _bound = false;
                _fails++;
                if (Time.unscaledTime >= _nextFailLog) { _nextFailLog = Time.unscaledTime + 5f; VRModCore.LogError($"[MimesisVR] Arm IK failed ({_fails} in a row), re-binding:", ex); }
                if (_fails >= 120) { VRModCore.LogError("[MimesisVR] Arm IK keeps failing, switching to bars (choose 'Character arms' in settings to retry)."); _mode = Mode.Bars; _fails = 0; }
            }
            MimesisPerf.AddHands((float)sw.Elapsed.TotalMilliseconds);
        }

        /// <summary>Hand orientation for a controller grip pose: fingers along the controller, thumb on top.</summary>
        private static Quaternion HandRotation(Quaternion grip, bool right)
        {
            Quaternion g = grip * Quaternion.Euler(Pitch, right ? Yaw : -Yaw, right ? Roll : -Roll);
            Vector3 fingers = g * Vector3.forward;               // hand bone +Y
            Vector3 thumb = g * Vector3.up;                      // thumb side
            Vector3 x = right ? thumb : -thumb;                  // right hand: thumb on +X, left hand: on -X
            x = (x - Vector3.Project(x, fingers)).normalized;
            Vector3 z = Vector3.Cross(x, fingers);
            return Quaternion.LookRotation(z, fingers);
        }

        /// <summary>Touch sensors: thumb on buttons / stick / thumbrest, index finger on the trigger.</summary>
        private static float Touch(Features.VRVisualization.OpenXR.XrControllerInput c, bool right, bool thumb)
        {
            if (!TouchFingers || c == null) return 0f;
            var b = thumb ? (right ? Features.VRVisualization.OpenXR.XrControllerInput.B.RThumbTouch : Features.VRVisualization.OpenXR.XrControllerInput.B.LThumbTouch)
                          : (right ? Features.VRVisualization.OpenXR.XrControllerInput.B.RIndexTouch : Features.VRVisualization.OpenXR.XrControllerInput.B.LIndexTouch);
            return c.Buttons[(int)b] ? 1f : 0f;
        }

        private static readonly float[] _touchT = new float[4]; // smoothed touch: L thumb, R thumb, L index, R index

        private static float Spread(Transform t, float factor)
        {
            if (factor == 0f && !_fingerOf.ContainsKey(t)) return 0f;
            float v = FingerSpread * factor;
            if (_fingerOf.TryGetValue(t, out int f)) v += SpreadPer[f];
            return v;
        }

        private static void Curl(Arm a, float index, float grip, float thumbTouch, float indexTouch, bool right)
        {
            float k = Mathf.Max(0.1f, FingerMax);
            float maxIndex = 70f * k, maxOther = 80f * k, maxThumb = 35f * k;
            float r = FingerRest;
            float dt = Mathf.Clamp01(Time.unscaledDeltaTime * 12f);
            int ti = right ? 1 : 0, ii = right ? 3 : 2;
            _touchT[ti] = Mathf.Lerp(_touchT[ti], thumbTouch, dt);
            _touchT[ii] = Mathf.Lerp(_touchT[ii], indexTouch, dt);
            float idx = Mathf.Max(index, _touchT[ii] * 0.25f);                    // finger on the trigger: slightly bent
            float th = Mathf.Max(grip, Mathf.Max(index * 0.5f, _touchT[ti] * 0.6f)); // thumb on a button: bent down
            float side = right ? 1f : -1f;
            bool lost = false;
            foreach (var (t, rest) in a.Bases) { if (t == null) { lost = true; continue; } t.localRotation = rest; }
            foreach (var (t, rest, sp) in a.Index) { if (t == null) { lost = true; continue; } t.localRotation = rest * Quaternion.Euler(0f, 0f, side * Spread(t, sp)) * Quaternion.Euler(CurlSign * Mathf.Lerp(r, maxIndex, idx), 0f, 0f); }
            foreach (var (t, rest, sp) in a.Others) { if (t == null) { lost = true; continue; } t.localRotation = rest * Quaternion.Euler(0f, 0f, side * Spread(t, sp)) * Quaternion.Euler(CurlSign * Mathf.Lerp(r, maxOther, grip), 0f, 0f); }
            foreach (var (t, rest, sp) in a.Thumb) { if (t == null) { lost = true; continue; } t.localRotation = rest * Quaternion.Euler(CurlSign * Mathf.Lerp(r * 0.5f, maxThumb, th), 0f, 0f); }
            if (lost) { _bound = false; VRModCore.LogWarning("[MimesisVR] A finger bone disappeared - re-binding the arms."); }
        }

        /// <summary>Two-bone IK: wrist (hand bone) to wristPos, then the hand gets handRot.</summary>
        private static void Solve(Arm a, Vector3 wristPos, Quaternion handRot, Transform body, bool right)
        {
            Vector3 s = a.Upper.position;
            float la = Vector3.Distance(a.Upper.position, a.Lower.position);
            float lb = Vector3.Distance(a.Lower.position, a.Hand.position);
            Vector3 toT = wristPos - s;
            float dist = toT.magnitude;
            if (dist < 1e-4f || la < 1e-4f || lb < 1e-4f) return;
            Vector3 dir = toT / dist;
            float d = Mathf.Clamp(dist, Mathf.Abs(la - lb) + 0.001f, la + lb - 0.001f);
            float shortBy = dist - (la + lb);
            if (shortBy > 0.03f) { _shortFrames++; _shortMax = Mathf.Max(_shortMax, shortBy); }

            // Elbow goes down, a bit outward and back
            Vector3 bu = _frame * Vector3.up, br = _frame * Vector3.right, bf = _frame * Vector3.forward;
            Vector3 hint = -bu * 1f + (right ? br : -br) * 0.6f - bf * 0.3f;
            Vector3 perp = hint - Vector3.Project(hint, dir);
            if (perp.sqrMagnitude < 1e-6f) perp = -bu;
            perp.Normalize();
            float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);
            Vector3 elbow = s + dir * (la * cosA) + perp * (la * sinA);

            // Bones point along their local +Y
            a.Upper.rotation = Quaternion.FromToRotation(a.Upper.rotation * Vector3.up, elbow - s) * a.Upper.rotation;
            Vector3 wrist = s + dir * d;
            a.Lower.rotation = Quaternion.FromToRotation(a.Lower.rotation * Vector3.up, wrist - a.Lower.position) * a.Lower.rotation;
            a.Hand.rotation = handRot;
        }

        // Diagnostics: frames where the arm could not reach the controller / frames the IK was skipped
        private static int _shortFrames, _skipFrames, _frames;
        private static float _shortMax, _nextDiag;
        private static string _skipReason;
        private static void Diag(string skip)
        {
            _frames++;
            if (skip != null) { _skipFrames++; _skipReason = skip; }
            if (Time.unscaledTime < _nextDiag) return;
            _nextDiag = Time.unscaledTime + 10f;
            if (_shortFrames > 0 || _skipFrames > 0)
                VRModCore.Log($"[MimesisVR] Arms 10s: {_frames} frames, could not reach the controller in {_shortFrames} (max {_shortMax * 100f:F0} cm short), IK skipped in {_skipFrames}{(_skipReason != null ? " (last: " + _skipReason + ")" : "")}.");
            _shortFrames = 0; _skipFrames = 0; _frames = 0; _shortMax = 0f; _skipReason = null;
        }

        /// <summary>Forces a re-bind (used when the finger rest-pose source changes).</summary>
        public static void Rebind() { _bound = false; }
    }
}
