using HarmonyLib;
using Mimic.Actors;
using Mimic.InputSystem;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityVRMod.Core;
using UnityVRMod.Features.VRVisualization.OpenXR;
using GameInputAction = Mimic.InputSystem.InputAction;

namespace UnityVRMod.Mimesis
{
    /// <summary>
    /// MIMESIS VR: bridges Quest controller state into the game's own input pipeline.
    /// Gameplay: left stick move, right stick snap-turn / slot switch, buttons -> game actions.
    /// Menus (cursor unlocked): floating panel, laser pointer -> mouse, A/B -> UI OK/Cancel.
    /// </summary>
    internal static class MimesisInput
    {
        // Set by the camera setup each rendered frame
        public static bool VrActive;
        public static XrControllerInput Controllers;
        public static Quaternion HeadWorldRot = Quaternion.identity; // HMD orientation in world space
        public static bool HeadValid;

        // Snap turn accumulated yaw (world yaw of the tracking-space rig)
        public static float RigYaw;
        public static bool RigYawInitialized;
        public static float SnapAngle = 45f;
        public static bool SmoothTurn;
        public static float SmoothTurnSpeed = 120f;
        public static float StickDeadzone = 0.15f;
        public static bool WalkByController;
        public static bool RunHold;

        // Panel / laser
        public static bool PanelVisible;
        public static bool LaserOnPanel;
        public static Vector2 LaserScreenPos;

        private static readonly bool[] _prevButtons = new bool[(int)XrControllerInput.B.Count];
        private static readonly bool[] _curButtons = new bool[(int)XrControllerInput.B.Count];
        private static readonly bool[] _prevAxisBtn = new bool[(int)XrControllerInput.F.Count];
        private static readonly bool[] _curAxisBtn = new bool[(int)XrControllerInput.F.Count];
        private static bool _snapLatched, _slotLatched;
        private static bool _runToggle;
        private static int _lastTickFrame = -1;
        private static bool _uiMoveLatched;

        private static AccessTools.FieldRef<InputManager, Dictionary<GameInputAction, InputManager.ActionState>> _statesRef;

        public static void Install()
        {
            try
            {
                MimesisSettings.EnsureLoaded();
                _statesRef = AccessTools.FieldRefAccess<InputManager, Dictionary<GameInputAction, InputManager.ActionState>>("states");
                var h = new HarmonyLib.Harmony("mimesisvr.input");
                h.Patch(AccessTools.Method(typeof(InputManager), "Update"), postfix: new HarmonyMethod(typeof(MimesisInput), nameof(InputManager_Update_Postfix)));
                h.Patch(AccessTools.Method(typeof(ProtoActor), "GetMovementInput"), postfix: new HarmonyMethod(typeof(MimesisInput), nameof(GetMovementInput_Postfix)));
                h.Patch(AccessTools.Method(typeof(ProtoActor), "RotateByInput"), postfix: new HarmonyMethod(typeof(MimesisInput), nameof(RotateByInput_Postfix)));
                var lou = AccessTools.TypeByName("LevelObjectUtility");
                var pick = lou != null ? AccessTools.Method(lou, "TryPick4") : null;
                if (pick != null) h.Patch(pick, prefix: new HarmonyMethod(typeof(MimesisInput), nameof(TryPick4_Prefix)), postfix: new HarmonyMethod(typeof(MimesisInput), nameof(TryPick4_Postfix)));
                else VRModCore.LogWarning("[MimesisVR] LevelObjectUtility.TryPick4 not found; interaction stays gaze-based.");
                MimesisActions.Install(h);
                MimesisRoomScale.Install(h);
                MimesisSettingsUI.WristHud.Install(h);
                MimesisSwing.Install(h);
                MimesisHud.InstallFx(h);
                MimesisBinds.Install(h);
                MimesisGameMenu.Install(h); // build 47
                VRModCore.Log("[MimesisVR] Game input patches installed.");
            }
            catch (Exception ex) { VRModCore.LogError("[MimesisVR] Failed to install input patches:", ex); }
        }

        private static bool Ready => VrActive && Controllers != null && Controllers.Ready;

        private static void Tick()
        {
            if (_lastTickFrame == Time.frameCount) return;
            _lastTickFrame = Time.frameCount;
            for (int i = 0; i < _curButtons.Length; i++) { _prevButtons[i] = _curButtons[i]; _curButtons[i] = Controllers.Buttons[i]; }
            for (int i = 0; i < _curAxisBtn.Length; i++)
            {
                _prevAxisBtn[i] = _curAxisBtn[i];
                float v = Controllers.Axes[i];
                _curAxisBtn[i] = _prevAxisBtn[i] ? v > 0.35f : v > 0.6f; // hysteresis
            }

            PanelVisible = ForcePanel || (Cursor.lockState != CursorLockMode.Locked && !MimesisHud.DeadNoMenu());
            if (PanelVisible || MimesisSettingsUI.Open)
            {
                if (Pressed(XrControllerInput.F.LTrigger)) MenuLeft = true;
                else if (Pressed(XrControllerInput.F.RTrigger)) MenuLeft = false;
            }

            // Snap / smooth turn (only during gameplay, not while the mod settings are open or the emote wheel uses the stick)
            float rx = TurnStick.x;
            if (!PanelVisible && !MimesisSettingsUI.Open && !MimesisBinds.WheelUsesStick(!MimesisBinds.SwapSticks))
            {
                if (SmoothTurn)
                {
                    if (Mathf.Abs(rx) > 0.2f) RigYaw += Mathf.Sign(rx) * Mathf.InverseLerp(0.2f, 1f, Mathf.Abs(rx)) * SmoothTurnSpeed * Time.unscaledDeltaTime;
                }
                else if (!_snapLatched && Mathf.Abs(rx) > 0.7f) { RigYaw += Mathf.Sign(rx) * SnapAngle; _snapLatched = true; }
                else if (_snapLatched && Mathf.Abs(rx) < 0.35f) _snapLatched = false;
            }
        }

        public static bool ForcePanel;
        public static ProtoActor LocalActor;
        public static bool InteractByHand = true; // End toggles
        public static bool AimValid;
        public static bool HasPickTarget;
        public static Vector3 AimOrigin, AimDir = Vector3.forward;
        public static Transform GameCamera;
        public static Vector3 HeadLocalPos = new Vector3(0, 1.6f, 0);
        public static Quaternion HeadLocalRot = Quaternion.identity;
        public static float PanelDistance = 1.0f;
        /// <summary>Menus / mod settings are pointed at with the left controller (last trigger pressed decides).</summary>
        public static bool MenuLeft;
        public static void MenuAim(XrControllerInput c, out bool valid, out Vector3 pos, out Quaternion rot)
        {
            if (MenuLeft && c.LeftPoseValid) { valid = true; pos = c.LeftAimPos; rot = c.LeftAimRot; return; }
            valid = c.RightPoseValid; pos = c.RightAimPos; rot = c.RightAimRot;
        }

        /// <summary>Build 44: sticks can be swapped (left-handed preset).</summary>
        public static Vector2 MoveStick => MimesisBinds.SwapSticks ? Controllers.RightStick : Controllers.LeftStick;
        public static Vector2 TurnStick => MimesisBinds.SwapSticks ? Controllers.LeftStick : Controllers.RightStick;

        /// <summary>State of a bindable button (build 44). Grips held by the watch / gear editing count as released.</summary>
        private static void Btn(MimesisBinds.Btn b, out bool down, out bool pressed, out bool released)
        {
            down = pressed = released = false;
            switch (b)
            {
                case MimesisBinds.Btn.A: B3(XrControllerInput.B.A, out down, out pressed, out released); break;
                case MimesisBinds.Btn.B: B3(XrControllerInput.B.B, out down, out pressed, out released); break;
                case MimesisBinds.Btn.X: B3(XrControllerInput.B.X, out down, out pressed, out released); break;
                case MimesisBinds.Btn.Y: B3(XrControllerInput.B.Y, out down, out pressed, out released); break;
                case MimesisBinds.Btn.LStick: B3(XrControllerInput.B.LStick, out down, out pressed, out released); break;
                case MimesisBinds.Btn.RStick: B3(XrControllerInput.B.RStick, out down, out pressed, out released); break;
                case MimesisBinds.Btn.LTrigger: F3(XrControllerInput.F.LTrigger, out down, out pressed, out released); break;
                case MimesisBinds.Btn.RTrigger: F3(XrControllerInput.F.RTrigger, out down, out pressed, out released); break;
                case MimesisBinds.Btn.LGrip:
                    if (MimesisWatches.GripBlocked(false)) return;
                    F3(XrControllerInput.F.LGrip, out down, out pressed, out released); break;
                case MimesisBinds.Btn.RGrip:
                    if (MimesisWatches.GripBlocked(true)) return;
                    F3(XrControllerInput.F.RGrip, out down, out pressed, out released); break;
            }
        }
        private static void B3(XrControllerInput.B b, out bool d, out bool p, out bool r) { d = Down(b); p = Pressed(b); r = Released(b); }
        private static void F3(XrControllerInput.F f, out bool d, out bool p, out bool r) { d = Down(f); p = Pressed(f); r = Released(f); }

        /// <summary>Puts a game action on the button the player chose for it.</summary>
        private static void ApplyBind(Dictionary<GameInputAction, InputManager.ActionState> st, GameInputAction a)
        {
            var slot = MimesisBinds.Find(a);
            if (slot == null || slot.Cur == MimesisBinds.Btn.None) return;
            Btn(slot.Cur, out bool d, out bool p, out bool r);
            Apply(st, a, d, p, r);
        }

        private static void ApplyBindAs(Dictionary<GameInputAction, InputManager.ActionState> st, GameInputAction a, GameInputAction boundAs)
        {
            var slot = MimesisBinds.Find(boundAs);
            if (slot == null || slot.Cur == MimesisBinds.Btn.None) return;
            Btn(slot.Cur, out bool d, out bool p, out bool r);
            Apply(st, a, d, p, r);
        }

        private static bool EmoteBreak()
        {
            if (MoveStick.magnitude > Mathf.Max(0.3f, StickDeadzone)) return true;
            Vector2 ts = TurnStick;
            if (Mathf.Abs(ts.y) > 0.7f && Mathf.Abs(ts.x) < 0.5f && !MimesisBinds.WheelUsesStick(!MimesisBinds.SwapSticks)) return true; // slot switch
            foreach (var slot in MimesisBinds.Slots)
            {
                if (slot.Cur == MimesisBinds.Btn.None || slot.Action == GameInputAction.PushToTalk || slot.Action == GameInputAction.EmotePanel) continue;
                Btn(slot.Cur, out _, out bool p, out _);
                if (p) return true;
            }
            return false;
        }

        private static bool BindDown(GameInputAction a, out bool pressed)
        {
            pressed = false;
            var slot = MimesisBinds.Find(a);
            if (slot == null || slot.Cur == MimesisBinds.Btn.None) return false;
            Btn(slot.Cur, out bool d, out pressed, out _);
            return d;
        }

        private static bool Down(XrControllerInput.B b) => _curButtons[(int)b];
        private static bool Pressed(XrControllerInput.B b) => _curButtons[(int)b] && !_prevButtons[(int)b];
        private static bool Released(XrControllerInput.B b) => !_curButtons[(int)b] && _prevButtons[(int)b];
        private static bool Down(XrControllerInput.F f) => _curAxisBtn[(int)f];
        private static bool Pressed(XrControllerInput.F f) => _curAxisBtn[(int)f] && !_prevAxisBtn[(int)f];
        private static bool Released(XrControllerInput.F f) => !_curAxisBtn[(int)f] && _prevAxisBtn[(int)f];

        private static void Apply(Dictionary<GameInputAction, InputManager.ActionState> states, GameInputAction a, bool down, bool pressed, bool released)
        {
            if (!states.TryGetValue(a, out var s) || s == null) return;
            s.isPressed |= down;
            s.wasPressedThisFrame |= pressed;
            s.wasRelesedThisFrame |= released;
        }

        private static void ApplyB(Dictionary<GameInputAction, InputManager.ActionState> st, GameInputAction a, XrControllerInput.B b) => Apply(st, a, Down(b), Pressed(b), Released(b));
        private static void ApplyF(Dictionary<GameInputAction, InputManager.ActionState> st, GameInputAction a, XrControllerInput.F f) => Apply(st, a, Down(f), Pressed(f), Released(f));

        public static void InputManager_Update_Postfix(InputManager __instance)
        {
            if (!Ready) return;
            try
            {
                Tick();
                MimesisSwing.Update();
                var st = _statesRef(__instance);
                if (st == null) return;

                if (MimesisSettingsUI.BlockGame)
                {
                    // Mod settings open (or the wrist gear is being pressed): buttons stay with the mod, voice still works
                    ApplyBind(st, GameInputAction.PushToTalk);
                    MimesisBinds.TrackWheel(false, false);
                    return;
                }

                if (PanelVisible)
                {
                    // Menu navigation
                    MimesisBinds.TrackWheel(false, false);
                    Vector2 ls = MoveStick;
                    bool any = ls.magnitude > 0.7f;
                    if (any && !_uiMoveLatched)
                    {
                        _uiMoveLatched = true;
                        GameInputAction dir = Mathf.Abs(ls.x) > Mathf.Abs(ls.y)
                            ? (ls.x > 0 ? GameInputAction.UI_MoveRight : GameInputAction.UI_MoveLeft)
                            : (ls.y > 0 ? GameInputAction.UI_MoveUp : GameInputAction.UI_MoveDown);
                        Apply(st, dir, true, true, false);
                    }
                    else if (!any && ls.magnitude < 0.35f) _uiMoveLatched = false;

                    ApplyB(st, GameInputAction.UI_OK, XrControllerInput.B.A);
                    ApplyB(st, GameInputAction.UI_Cancel, XrControllerInput.B.B);
                    ApplyB(st, GameInputAction.Escape, XrControllerInput.B.Menu);
                    ApplyB(st, GameInputAction.UI_Escape, XrControllerInput.B.Menu);
                    ApplyBind(st, GameInputAction.PushToTalk);
                    // Right stick up/down scrolls long menus; with the right grip held it moves the panel nearer/farther
                    float pry = TurnStick.y;
                    if (Mathf.Abs(pry) > 0.3f)
                    {
                        if (Down(XrControllerInput.F.RGrip)) PanelDistance = Mathf.Clamp(PanelDistance - pry * Time.unscaledDeltaTime * 0.8f, 0.5f, 2.5f);
                        else Scroll(pry);
                    }
                    UpdateLaserMouse();
                    return;
                }

                MimesisRoomScale.MoveNow(); // walking in the room: move before the camera follows the character
                // Melee by swing (MimesisSwing): the swing presses the game's own attack button for one frame
                MimesisSwing.Tick(out bool swPress, out bool swRelease);
                if (MimesisHands.LocalEmote) { swPress = false; swRelease = false; } // a hand swing does not attack during an emote
                if (!MimesisSwing.BlockButton) ApplyBind(st, GameInputAction.FireWeapon);
                if (swPress) Apply(st, GameInputAction.FireWeapon, true, true, false);
                else if (swRelease) Apply(st, GameInputAction.FireWeapon, false, false, true);
                // Build 44: every action on the button chosen on the Buttons tab
                ApplyBind(st, GameInputAction.FireWeapon2);
                ApplyBind(st, GameInputAction.Interact);
                ApplyBind(st, GameInputAction.PushToTalk);
                ApplyBind(st, GameInputAction.Jump);
                ApplyBind(st, GameInputAction.DropItemOnHand);
                ApplyBind(st, GameInputAction.ShowStatus);
                ApplyBind(st, GameInputAction.ReloadWeapon);
                ApplyBind(st, GameInputAction.TramSettings);
                // Watching other players after death: the game's keys are left/right mouse buttons -> the two fire buttons
                ApplyBindAs(st, GameInputAction.PreviousSpectatorTarget, GameInputAction.FireWeapon);
                ApplyBindAs(st, GameInputAction.NextSpectatorTarget, GameInputAction.FireWeapon2);
                ApplyB(st, GameInputAction.Escape, XrControllerInput.B.Menu);
                // An emote stops on buttons, walking with the stick or switching slots - not on turning or moving the hands
                if (MimesisHands.LocalEmote && LocalActor != null && EmoteBreak()) MimesisBinds.StopLocalEmote(LocalActor);
                // Emote wheel: the game's gamepad panel opens while the button is held, the hand picks
                bool emoteHeld = BindDown(GameInputAction.EmotePanel, out bool emotePressed);
                MimesisBinds.TrackWheel(emoteHeld, emotePressed);
                ApplyBind(st, GameInputAction.EmotePanel);

                // Sprint: click toggles, cancelled when the move stick is released (or hold, see RunHold)
                bool wasRun = _runToggle;
                bool runDown = BindDown(GameInputAction.Run, out bool runPressed);
                if (RunHold) _runToggle = runDown;
                else
                {
                    if (runPressed) _runToggle = !_runToggle;
                    if (MoveStick.magnitude < 0.1f) _runToggle = false;
                }
                Apply(st, GameInputAction.Run, _runToggle, _runToggle && !wasRun, !_runToggle && wasRun);

                // Inventory slot: turn stick up/down (not while the emote wheel uses that stick)
                Vector2 ts = MimesisBinds.WheelUsesStick(!MimesisBinds.SwapSticks) ? Vector2.zero : TurnStick;
                float ry = ts.y;
                if (!_slotLatched && Mathf.Abs(ry) > 0.7f && Mathf.Abs(ts.x) < 0.5f)
                {
                    Apply(st, ry > 0 ? GameInputAction.UI_PREV : GameInputAction.UI_NEXT, true, true, false);
                    _slotLatched = true;
                }
                else if (_slotLatched && Mathf.Abs(ry) < 0.35f) _slotLatched = false;
            }
            catch (Exception ex) { VRModCore.LogError("[MimesisVR] InputManager postfix error:", ex); }
        }

        public static void TryPick4_Prefix(ref Vector3 from, ref Vector3 dir)
        {
            if (!Ready || PanelVisible || !InteractByHand || !AimValid) return;
            from = AimOrigin;
            dir = AimDir;
        }

        public static void TryPick4_Postfix(bool __result) { HasPickTarget = __result; }

        public static void GetMovementInput_Postfix(ProtoActor __instance, ref Vector2 __result)
        {
            if (!Ready || PanelVisible) return;
            try
            {
                if (!__instance.AmIAvatar()) return;
                Vector2 s = MoveStick;
                if (s.magnitude < StickDeadzone) return;
                if (MimesisBinds.WheelUsesStick(MimesisBinds.SwapSticks)) return; // the emote wheel uses this stick
                bool moveRight = MimesisBinds.SwapSticks;
                if (WalkByController && (moveRight ? Controllers.RightPoseValid : Controllers.LeftPoseValid))
                {
                    // Body faces the head; rotate the stick by (controller yaw - head yaw)
                    Vector3 cf = (moveRight ? Controllers.RightAimRot : Controllers.LeftAimRot) * Vector3.forward, hf = HeadLocalRot * Vector3.forward;
                    float a = (Mathf.Atan2(cf.x, cf.z) - Mathf.Atan2(hf.x, hf.z));
                    float cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                    s = new Vector2(s.x * cs + s.y * sn, -s.x * sn + s.y * cs);
                }
                __result += s;
                if (__result.magnitude > 1f) __result.Normalize();
            }
            catch (Exception ex) { VRModCore.LogError("[MimesisVR] Movement postfix error:", ex); }
        }

        public static void RotateByInput_Postfix(ProtoActor __instance)
        {
            if (!Ready || !HeadValid) return;
            try
            {
                if (!__instance.AmIAvatar()) return;
                LocalActor = __instance;
                if (MimesisHands.LocalEmote) return; // the emote animation keeps the body; turning the head does not stop it
                Vector3 fwd = HeadWorldRot * Vector3.forward;
                // Body yaw: looking straight down (or up) the forward vector is nearly vertical and its direction
                // spins - the head's up vector then points where the face is turned, so it is mixed in by the pitch
                Vector3 up = HeadWorldRot * Vector3.up;
                Vector3 dir = new Vector3(fwd.x, 0f, fwd.z) - new Vector3(up.x, 0f, up.z) * fwd.y;
                float yaw = dir.sqrMagnitude > 1e-6f ? Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg : __instance.transform.eulerAngles.y;
                float pitch = -Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg; // positive = looking down
                pitch = Mathf.Clamp(pitch, -80f, 80f);
                __instance.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                var camRoot = __instance.FpvCameraRoot;
                if (camRoot != null) camRoot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }
            catch (Exception ex) { VRModCore.LogError("[MimesisVR] Rotation postfix error:", ex); }
        }

        private static void Scroll(float stick)
        {
            var mouse = Mouse.current;
            if (mouse == null || !LaserOnPanel) return;
            float amount = Mathf.Sign(stick) * Mathf.InverseLerp(0.3f, 1f, Mathf.Abs(stick)) * 1200f * Time.unscaledDeltaTime;
            using (StateEvent.From(mouse, out var ev))
            {
                mouse.position.WriteValueIntoEvent(LaserScreenPos, ev);
                mouse.scroll.WriteValueIntoEvent(new Vector2(0f, amount), ev);
                UnityEngine.InputSystem.InputSystem.QueueEvent(ev);
            }
        }

        // ---- Laser pointer -> mouse ----
        private static bool _mouseDown;

        private static void UpdateLaserMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (LaserOnPanel)
            {
                mouse.WarpCursorPosition(LaserScreenPos);
                InputState.Change(mouse.position, LaserScreenPos);
            }
            bool trig = Down(MenuLeft ? XrControllerInput.F.LTrigger : XrControllerInput.F.RTrigger) && LaserOnPanel;
            if (trig != _mouseDown)
            {
                _mouseDown = trig;
                using (StateEvent.From(mouse, out var ev))
                {
                    mouse.position.WriteValueIntoEvent(LaserScreenPos, ev);
                    mouse.leftButton.WriteValueIntoEvent(trig ? 1f : 0f, ev);
                    UnityEngine.InputSystem.InputSystem.QueueEvent(ev);
                }
            }
        }
    }
}
