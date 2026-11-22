using System.Linq;
using TMPro;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityVRMod.Core;
using UnityVRMod.Features.VRVisualization.OpenXR;

namespace UnityVRMod.Mimesis
{
    /// <summary>
    /// MIMESIS VR: gear button on the left wrist and the mod settings panel in front of the eyes.
    /// The panel is a private uGUI canvas rendered by its own camera into a texture, shown as an OpenXR quad layer.
    /// Font and colours are taken from the game's own UI when possible. Input is handled here directly
    /// (right-hand pointer + trigger), the game's EventSystem never sees this canvas.
    /// </summary>
    internal static partial class MimesisSettingsUI
    {
        public static bool Open { get; private set; }
        /// <summary>Controller buttons must not reach the game this frame (movement still does).</summary>
        public static bool BlockGame => Open || _gearHover || _wristHover || _wristTouch || _holdUntilRelease;
        /// <summary>Right pointer hits the wrist inventory or the gear this frame: distance along the ray (rig units), else -1.</summary>
        public static float UiRayHit = -1f;

        // ---- gear layer
        public static bool GearActive;
        public static RenderTexture GearTex;
        public static Vector3 GearLocalPos;
        public static Quaternion GearLocalRot = Quaternion.identity;
        public static Vector2 GearSize;
        // ---- panel layer
        public static bool PanelActive;
        public static RenderTexture PanelTex;
        public static Vector3 PanelLocalPos;
        public static Quaternion PanelLocalRot = Quaternion.identity;
        public static Vector2 PanelSize;
        public static RenderTexture RawTex => _raw;

        private const int W = 1536, H = 1024;
        private const int UiLayer = 31;
        private const float PanelWidth = 0.92f, PanelDist = 0.8f;
        private const float GearMeters = 0.045f;

        private static RenderTexture _gearNormal, _gearHoverTex;
        private static bool _gearHover, _gearHoverStartedHeld, _holdUntilRelease, _poked;
        private static Camera _cam;
        private static Canvas _canvas;
        private static RenderTexture _raw;
        private static RectTransform _content, _cursor;
        private static string _tab = "controls";
        private static bool _showLayout;
        /// <summary>Build 44: the Buttons tab shows the controller layout instead of its list.</summary>
        public static bool ShowLayout { get => _showLayout; set { if (_showLayout != value) { _showLayout = value; _dirty = true; } } }
        private static bool _dirty = true;
        private static string _styleSig = "";
        private static bool _prevTrig, _prevTrigL, _prevB, _prevMenu, _stickLatched, _prevGrip;
        private static float _stickNext;
        private static Hit _hovered;
        private static Label _desc;
        private static string _feedbackId; private static string _feedbackText; private static float _feedbackUntil;

        // ---------------------------------------------------------------- style
        private static TMP_FontAsset _font;
        private static Font _legacyFont;
        private static Sprite _btnSprite;
        private static Color cBg = new(0.055f, 0.06f, 0.065f, 0.95f);
        private static Color cHeader = new(0.10f, 0.105f, 0.11f, 1f);
        private static Color cRow = new(1f, 1f, 1f, 0.035f);
        private static Color cRowHover = new(1f, 1f, 1f, 0.10f);
        private static Color cBtn = new(0.17f, 0.18f, 0.19f, 1f);
        private static Color cBtnHover = new(0.26f, 0.27f, 0.28f, 1f);
        private static Color cText = new(0.93f, 0.92f, 0.88f, 1f);
        private static Color cDim = new(0.60f, 0.60f, 0.58f, 1f);
        private static Color cAccent = new(0.86f, 0.73f, 0.36f, 1f);
        private static readonly Color cCursor = new(0.35f, 1f, 0.45f, 1f);

        private sealed class Label
        {
            public TMP_Text Tmp; public Text Legacy;
            public string Text { set { if (Tmp != null) Tmp.text = value; else if (Legacy != null) Legacy.text = value; } }
            public Color Color { set { if (Tmp != null) Tmp.color = value; else if (Legacy != null) Legacy.color = value; } }
        }

        private sealed class Hit
        {
            public RectTransform Rt; public Image Bg; public Color Normal, HoverCol;
            public Action Click; public Action<int> Step; public MimesisSettings.Def Def;
        }

        private sealed class Row { public MimesisSettings.Def Def; public Label Value; public Label Feedback; public int Last = -1; public bool IsBool; }

        private static readonly List<Hit> _hits = new();
        private const int VisibleRows = 9;
        private static readonly Dictionary<string, int> _scroll = new();
        private static int ScrollOf(string tab) => _scroll.TryGetValue(tab, out var v) ? v : 0;
        private static bool _stickYLatched;
        private static int _rebuildFails;
        private static readonly List<Row> _rows = new();

        private static string L(string en, string ru) => MimesisLoc.Tr(en, ru);

        // ---------------------------------------------------------------- per frame
        /// <summary>Called each VR frame after the HUD/wrist pieces have been placed (rig-local coordinates).</summary>
        public static void Update(Transform rig)
        {
            GearActive = false; PanelActive = false; UiRayHit = -1f;
            MimesisFps.Active = false;
            var c = MimesisInput.Controllers;
            bool ready = rig != null && MimesisInput.VrActive && c != null && c.Ready;
            if (!ready) { if (Open) Close(); _gearHover = false; return; }
            try
            {
                MimesisSettings.Sync();
                bool trig = _prevTrig ? c.Axes[(int)XrControllerInput.F.RTrigger] > 0.35f : c.Axes[(int)XrControllerInput.F.RTrigger] > 0.6f;
                bool trigPressed = trig && !_prevTrig;
                bool bBtn = c.Buttons[(int)XrControllerInput.B.B], menuBtn = c.Buttons[(int)XrControllerInput.B.Menu];
                bool bPressed = bBtn && !_prevB, menuPressed = menuBtn && !_prevMenu;
                _prevTrig = trig; _prevB = bBtn; _prevMenu = menuBtn;
                if (_holdUntilRelease && !trig && !bBtn && !menuBtn && c.Axes[(int)XrControllerInput.F.RGrip] < 0.35f) _holdUntilRelease = false;

                // ---- gear on the left wrist
                bool gearPressed = UpdateGear(c, trig, trigPressed);
                // the frame counter stays at the left panel even when the gear is on the right watch (build 41)
                MimesisFps.Update(_fpsAnchorOk, _fpsAnchorPos, _fpsAnchorRot, MimesisWatches.GearWatchSize);

                bool grip = !MimesisWatches.GripBlocked(true) && (_prevGrip ? c.Axes[(int)XrControllerInput.F.RGrip] > 0.35f : c.Axes[(int)XrControllerInput.F.RGrip] > 0.6f);
                bool gripPressed = grip && !_prevGrip;
                _prevGrip = grip;
                UpdateWrist(c, trig, trigPressed && !gearPressed, bPressed && !Open, gripPressed && !Open);
                ProcessPendingDrop();
                if (gearPressed) { if (Open) Close(); else OpenPanel(); _holdUntilRelease = true; }
                else if (Open && (bPressed || menuPressed)) { Close(); _holdUntilRelease = true; }

                bool trigL = _prevTrigL ? c.Axes[(int)XrControllerInput.F.LTrigger] > 0.35f : c.Axes[(int)XrControllerInput.F.LTrigger] > 0.6f;
                bool trigLPressed = trigL && !_prevTrigL;
                _prevTrigL = trigL;
                if (trigLPressed && Open) MimesisInput.MenuLeft = true;
                else if (trigPressed && Open && !gearPressed) MimesisInput.MenuLeft = false;
                if (Open) UpdatePanel(c, MimesisInput.MenuLeft ? trigLPressed : (trigPressed && !gearPressed));
            }
            catch (Exception ex) { VRModCore.LogError("[MimesisVR] Settings UI error:", ex); Close(); _dirty = true; }
        }

        private static bool _fpsAnchorOk, _gearPokedL, _prevGearTrigL;
        private static Vector3 _fpsAnchorPos;
        private static Quaternion _fpsAnchorRot = Quaternion.identity;
        private static RenderTexture _gearFade;

        private static bool UpdateGear(XrControllerInput c, bool trig, bool trigPressed)
        {
            EnsureGearTex();
            var wrist = MimesisHudSegments.Wrist;
            // the left place (as before): the gear there, or only the frame counter when the gear is on the right watch
            MimesisHudSegments.LeftPanelPose(c, wrist.Active ? wrist.Size.y : 0.05f, out _, out var lrot, out var lpos);
            if (WristHud.Active && WristHud.OnForearm)
            {
                // on the forearm: gear (and the frame counter right of it) to the left of the whole panel
                float fps = MimesisFps.Enabled ? MimesisFps.Size.x + 0.006f : 0f;
                lrot = wrist.LocalRot;
                lpos = wrist.LocalPos + wrist.LocalRot * new Vector3(-(wrist.Size.x * 0.5f + 0.008f + fps + MimesisWatches.GearWatchSize * 0.5f), 0f, -0.001f);
            }
            _fpsAnchorOk = c.LeftPoseValid; _fpsAnchorPos = lpos; _fpsAnchorRot = lrot;

            // build 41: on the right watch (end of the body, shown when looked at) or in the corner of its screen
            bool placed = MimesisWatches.GearPose(out var rp, out var rr, out float rs, out bool byLook);
            if (!placed && MimesisWatches.GearPlace != 0 && MimesisInput.VrActive && (MimesisWatches.GearLeft ? WristHud.On && WristHud.OnForearm : MimesisWatches.StatusOnWatch))
            {
                // its place on a watch is not shown right now (screen turned away...): no jumping elsewhere
                _gearHover = false; GearActive = false; return false;
            }
            bool onRight = placed && !MimesisWatches.GearLeft; // pressed with the left hand
            float size = MimesisWatches.GearWatchSize; // build 42: the smaller size everywhere
            if (placed) { GearLocalPos = rp; GearLocalRot = rr; size = rs; }
            else
            {
                if (!c.LeftPoseValid) { _gearHover = false; return false; }
                GearLocalPos = lpos; GearLocalRot = lrot;
            }
            GearSize = new Vector2(size, size);
            float alpha = 1f;
            if (placed && byLook && WristHud.LookToShow)
            {
                Vector3 head = MimesisInput.HeadLocalPos, f = MimesisInput.HeadLocalRot * Vector3.forward;
                float d = Vector3.Dot(f, (GearLocalPos - head).normalized);
                alpha = Mathf.Lerp(Mathf.Clamp01(WristHud.IdleAlpha), 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.82f, 0.94f, d)));
            }
            if (MimesisWatches.GearPlace == 4 || MimesisWatches.GearPlace == 5) alpha = WristHud.Alpha;
            if (placed) alpha *= MimesisWatches.Facing(GearLocalPos, GearLocalRot);
            _gearAlpha = Mathf.MoveTowards(_gearAlpha, alpha, Time.unscaledDeltaTime * 4f);
            bool usable = _gearAlpha > 0.3f && !MimesisWatches.EditMode;

            bool hover = false, pressed = false;
            if (c.RightPoseValid && usable)
            {
                Vector3 o = c.RightAimPos, d = (c.RightAimRot * Vector3.forward).normalized;
                Vector3 n = GearLocalRot * Vector3.forward;
                float denom = Vector3.Dot(n, d);
                if (Mathf.Abs(denom) > 1e-4f)
                {
                    float t = Vector3.Dot(GearLocalPos - o, n) / denom;
                    if (t > 0f && t < 1.5f && (o + d * t - GearLocalPos).magnitude < size * 0.75f) { hover = true; UiRayHit = t; }
                }
                // poke with the right controller (the gear on the left)
                if (!onRight)
                {
                    float pd = (c.RightAimPos - GearLocalPos).magnitude;
                    if (!_poked && pd < 0.035f) { _poked = true; pressed = true; }
                    else if (_poked && pd > 0.07f) _poked = false;
                }
            }
            if (onRight && c.LeftPoseValid)
            {
                // on the right watch: poke it with the left controller, like a button on a watch
                float r0 = Mathf.Max(0.02f, size * 0.8f);
                float pd = (c.LeftAimPos - GearLocalPos).magnitude;
                if (usable && !_gearPokedL && pd < r0) { _gearPokedL = true; pressed = true; }
                else if (_gearPokedL && pd > r0 * 2.2f) _gearPokedL = false;
                // or point the left ray at it and pull the left trigger
                bool tl = _prevGearTrigL ? c.Axes[(int)XrControllerInput.F.LTrigger] > 0.35f : c.Axes[(int)XrControllerInput.F.LTrigger] > 0.6f;
                bool tlPressed = tl && !_prevGearTrigL;
                _prevGearTrigL = tl;
                if (usable)
                {
                    Vector3 o = c.LeftAimPos, d = (c.LeftAimRot * Vector3.forward).normalized;
                    Vector3 n = GearLocalRot * Vector3.forward;
                    float denom = Vector3.Dot(n, d);
                    if (Mathf.Abs(denom) > 1e-4f)
                    {
                        float t = Vector3.Dot(GearLocalPos - o, n) / denom;
                        if (t > 0f && t < 1f && (o + d * t - GearLocalPos).magnitude < size * 0.75f) { hover = true; if (tlPressed) pressed = true; }
                    }
                }
            }
            if (hover && !_gearHover) _gearHoverStartedHeld = trig;
            _gearHover = hover && !_gearHoverStartedHeld;
            if (hover && !trig) _gearHoverStartedHeld = false;
            if (_gearHover && trigPressed) pressed = true;
            var tex = (_gearHover || Open) && _gearHoverTex != null ? _gearHoverTex : _gearNormal;
            if (tex != null && _gearAlpha < 0.999f)
            {
                if (_gearAlpha <= 0.02f) { GearActive = false; return false; }
                if (_gearFade == null || _gearFade.width != tex.width || _gearFade.height != tex.height)
                {
                    if (_gearFade != null) { _gearFade.Release(); UnityEngine.Object.Destroy(_gearFade); }
                    _gearFade = new RenderTexture(tex.width, tex.height, 0, tex.graphicsFormat) { filterMode = tex.filterMode }; _gearFade.Create();
                }
                Graphics.CopyTexture(tex, _gearFade);
                MimesisWatches.Fade(_gearFade, _gearAlpha);
                tex = _gearFade;
            }
            GearTex = tex;
            GearActive = GearTex != null;
            return pressed;
        }

        private static float _gearAlpha = 1f;

        // ---------------------------------------------------------------- inventory on the wrist
        private static bool _wristHover, _wristHoverStartedHeld, _wristPoked, _wristTouch;
        private static int _pendingDrop = -1;
        private static float _pendingDropUntil;
        private static MethodInfo _releaseItem;
        private static FieldInfo _invField;
        private static MethodInfo _selectSlot;
        private static bool _slotWarned;

        private static void UpdateWrist(XrControllerInput c, bool trig, bool trigPressed, bool bPressed, bool gripPressed)
        {
            var w = MimesisHudSegments.Wrist;
            w.HoverSlot = -1;
            _wristTouch = false;
            if (Open || MimesisInput.PanelVisible || !w.Active || !c.RightPoseValid || w.SlotRects.Count == 0 || w.Size.x <= 0f || w.Size.y <= 0f) { _wristHover = false; return; }
            var inv = Quaternion.Inverse(w.LocalRot);
            int slot = -1; bool select = false, hoverRay = false;

            // Touch with the right controller
            Vector3 lt = inv * (c.RightAimPos - w.LocalPos);
            float tu = lt.x / w.Size.x + 0.5f, tv = lt.y / w.Size.y + 0.5f;
            bool inside = tu >= 0f && tu <= 1f && tv >= 0f && tv <= 1f;
            if (inside && Mathf.Abs(lt.z) < 0.025f)
            {
                int s = MimesisHudSegments.SlotAtUv(tu, tv);
                _wristTouch = true;
                if (s >= 0) { slot = s; if (!_wristPoked) { _wristPoked = true; select = true; } }
            }
            else if (!inside || Mathf.Abs(lt.z) > 0.05f) _wristPoked = false;

            // Pointer + trigger
            if (slot < 0)
            {
                Vector3 o = c.RightAimPos, d = (c.RightAimRot * Vector3.forward).normalized;
                Vector3 n = w.LocalRot * Vector3.forward;
                float den = Vector3.Dot(n, d);
                if (Mathf.Abs(den) > 1e-4f)
                {
                    float t = Vector3.Dot(w.LocalPos - o, n) / den;
                    if (t > 0f && t < 1.2f)
                    {
                        Vector3 l = inv * (o + d * t - w.LocalPos);
                        float u = l.x / w.Size.x + 0.5f, v = l.y / w.Size.y + 0.5f;
                        if (u >= 0f && u <= 1f && v >= 0f && v <= 1f) { hoverRay = true; UiRayHit = t; slot = MimesisHudSegments.SlotAtUv(u, v); }
                    }
                }
            }
            if (hoverRay && !_wristHover) _wristHoverStartedHeld = trig;
            _wristHover = hoverRay && !_wristHoverStartedHeld;
            if (!trig) _wristHoverStartedHeld = false;
            if (_wristHover && trigPressed && slot >= 0) select = true;
            w.HoverSlot = slot;
            if (select) SelectInventorySlot(slot);
            // Grip on a highlighted slot: select it; grip again on the already selected slot: drop that item
            if (gripPressed && slot >= 0 && (_wristHover || _wristTouch))
            {
                var map = MimesisHudSegments.Wrist.SlotGameIndex;
                var actor = MimesisInput.LocalActor;
                if (slot < map.Count && actor != null)
                {
                    if (actor.GetSelectedInventorySlotIndex() == map[slot]) bPressed = true; // drop below
                    else SelectInventorySlot(slot);
                    _holdUntilRelease = true;
                }
            }
            // B on a highlighted slot: drop that item (select it first if needed)
            if (bPressed && slot >= 0 && (_wristHover || _wristTouch))
            {
                var map = MimesisHudSegments.Wrist.SlotGameIndex;
                if (slot < map.Count)
                {
                    _pendingDrop = map[slot];
                    _pendingDropUntil = Time.unscaledTime + 1.5f;
                    var actor = MimesisInput.LocalActor;
                    if (actor != null && actor.GetSelectedInventorySlotIndex() != _pendingDrop) SelectInventorySlot(slot);
                    _holdUntilRelease = true;
                }
            }
        }

        private static void ProcessPendingDrop()
        {
            if (_pendingDrop < 0) return;
            var actor = MimesisInput.LocalActor;
            if (actor == null || Time.unscaledTime > _pendingDropUntil) { _pendingDrop = -1; return; }
            if (actor.GetSelectedInventorySlotIndex() != _pendingDrop) return; // wait for the slot change to arrive
            int slot = _pendingDrop;
            _pendingDrop = -1;
            try
            {
                _invField ??= typeof(Mimic.Actors.ProtoActor).GetField("inventory", BindingFlags.Instance | BindingFlags.NonPublic);
                var invObj = _invField?.GetValue(actor);
                if (invObj == null) return;
                _releaseItem ??= invObj.GetType().GetMethod("SendReleaseItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null);
                if (_releaseItem == null) { VRModCore.LogWarning("[MimesisVR] Inventory.SendReleaseItem not found."); return; }
                if (actor.GetSelectedInventoryItem() == null) return; // empty slot
                _releaseItem.Invoke(invObj, new object[] { false });
                VRModCore.Log($"[MimesisVR] Wrist inventory: dropped the item from slot {slot}.");
            }
            catch (Exception ex) { VRModCore.LogError("[MimesisVR] Wrist drop failed:", ex); }
        }

        private static void SelectInventorySlot(int uiIndex)
        {
            try
            {
                var actor = MimesisInput.LocalActor;
                if (actor == null) return;
                var map = MimesisHudSegments.Wrist.SlotGameIndex;
                int count = map.Count;
                if (uiIndex < 0 || uiIndex >= count) return;
                _invField ??= typeof(Mimic.Actors.ProtoActor).GetField("inventory", BindingFlags.Instance | BindingFlags.NonPublic);
                var invObj = _invField?.GetValue(actor);
                if (invObj == null) return;
                _selectSlot ??= invObj.GetType().GetMethod("SelectSlot", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new[] { typeof(int) }, null);
                if (_selectSlot == null) { VRModCore.LogWarning("[MimesisVR] Inventory.SelectSlot not found."); return; }
                int slot = map[uiIndex];
                _selectSlot.Invoke(invObj, new object[] { slot });
                VRModCore.Log($"[MimesisVR] Wrist inventory: selected slot {slot} (box {uiIndex + 1} of {count}).");
            }
            catch (Exception ex) { VRModCore.LogError("[MimesisVR] Wrist slot select failed:", ex); }
        }

        private static void UpdatePanel(XrControllerInput c, bool trigPressed)
        {
            EnsureCanvas();
            if (_dirty) Rebuild();
            PanelSize = new Vector2(PanelWidth, PanelWidth * H / W);

            // Pointer -> panel
            Vector2 px = new(-1f, -1f);
            bool onPanel = false;
            MimesisInput.MenuAim(c, out bool aimOk, out Vector3 aimPos, out Quaternion aimRot);
            if (aimOk)
            {
                Vector3 o = aimPos, d = (aimRot * Vector3.forward).normalized;
                float len = 2f;
                Vector3 n = PanelLocalRot * Vector3.forward;
                float denom = Vector3.Dot(n, d);
                if (Mathf.Abs(denom) > 1e-4f)
                {
                    float t = Vector3.Dot(PanelLocalPos - o, n) / denom;
                    if (t > 0f)
                    {
                        Vector3 local = Quaternion.Inverse(PanelLocalRot) * (o + d * t - PanelLocalPos);
                        float u = local.x / PanelSize.x, v = local.y / PanelSize.y;
                        if (Mathf.Abs(u) <= 0.5f && Mathf.Abs(v) <= 0.5f) { onPanel = true; len = t; px = new Vector2((u + 0.5f) * W, (v + 0.5f) * H); }
                    }
                }
                MimesisVrExtras.ShowLaser(o, d, len);
            }
            MimesisInput.LaserOnPanel = false; // the game's menu cursor must not move while our panel is open

            // Hover
            Hit hov = null;
            if (onPanel) foreach (var h in _hits) if (h.Rt != null && RectTransformUtility.RectangleContainsScreenPoint(h.Rt, px, _cam)) { hov = h; break; }
            if (hov != _hovered)
            {
                if (_hovered?.Bg != null) _hovered.Bg.color = _hovered.Normal;
                if (hov?.Bg != null) hov.Bg.color = hov.HoverCol;
                _hovered = hov;
            }
            if (trigPressed && hov?.Click != null) { hov.Click(); if (!Open) return; }

            // Stick left/right changes the hovered setting
            float sx = MimesisInput.MenuLeft ? c.LeftStick.x : c.RightStick.x;
            // holding the stick repeats: first step at once, then after 0.4 s every 0.07 s
            if (!_stickLatched && Mathf.Abs(sx) > 0.7f) { _stickLatched = true; _stickNext = Time.unscaledTime + 0.4f; _hovered?.Step?.Invoke(sx > 0 ? 1 : -1); }
            else if (_stickLatched && Mathf.Abs(sx) > 0.7f && Time.unscaledTime >= _stickNext) { _stickNext = Time.unscaledTime + 0.07f; _hovered?.Step?.Invoke(sx > 0 ? 1 : -1); }
            else if (_stickLatched && Mathf.Abs(sx) < 0.35f) _stickLatched = false;
            // Stick up/down scrolls a long tab
            float sy = MimesisInput.MenuLeft ? c.LeftStick.y : c.RightStick.y;
            if (!_stickYLatched && Mathf.Abs(sy) > 0.7f && Mathf.Abs(sx) < 0.5f)
            {
                _stickYLatched = true;
                int before = ScrollOf(_tab);
                _scroll[_tab] = before + (sy > 0 ? -1 : 1);
                _dirty = true;
            }
            else if (_stickYLatched && Mathf.Abs(sy) < 0.35f) _stickYLatched = false;

            if (_dirty) { Rebuild(); _hovered = null; }
            RefreshValues();

            if (_cursor != null)
            {
                _cursor.gameObject.SetActive(onPanel);
                if (onPanel) _cursor.anchoredPosition = px;
            }

            // Full canvas update only right after a rebuild (it updates every canvas of the game - expensive);
            // otherwise Unity's own end-of-frame canvas update is used (one frame of lag for the cursor)
            if (_forceCanvas) { _forceCanvas = false; Canvas.ForceUpdateCanvases(); }
            _cam.Render();
            Graphics.Blit(_raw, PanelTex, new Vector2(1f, -1f), new Vector2(0f, 1f));
            PanelActive = true;
        }

        private static void OpenPanel()
        {
            Open = true;
            var headRot = MimesisInput.HeadLocalRot;
            Vector3 fwd = headRot * Vector3.forward; fwd.y = 0; if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; fwd.Normalize();
            PanelLocalRot = Quaternion.LookRotation(fwd, Vector3.up);
            PanelLocalPos = MimesisInput.HeadLocalPos + new Vector3(0f, -0.12f, 0f) + fwd * PanelDist;
            if (MimesisLoc.Refresh()) _dirty = true; // build 48: the game's language may have changed
            try
            {
                string sig = SampleStyle();
                if (sig != _styleSig) { _styleSig = sig; _dirty = true; }
            }
            catch (Exception ex) { VRModCore.LogWarning("[MimesisVR] Settings style sampling failed: " + ex.Message); }
            VRModCore.Log("[MimesisVR] Settings panel opened.");
        }

        private static void Close()
        {
            if (!Open) return;
            Open = false;
            PanelActive = false;
            if (_hovered?.Bg != null) _hovered.Bg.color = _hovered.Normal;
            _hovered = null;
            VRModCore.Log("[MimesisVR] Settings panel closed.");
        }

        // ---------------------------------------------------------------- gear texture
        private static void EnsureGearTex()
        {
            if (_gearNormal != null && _gearNormal.IsCreated() && _gearHoverTex != null && _gearHoverTex.IsCreated()) return;
            _gearNormal = MakeGear(new Color(0.88f, 0.87f, 0.83f), new Color(0f, 0f, 0f, 0.55f), Color.clear);
            _gearHoverTex = MakeGear(cAccent, new Color(0.05f, 0.05f, 0.05f, 0.75f), cCursor);
        }

        private static RenderTexture MakeGear(Color fg, Color bg, Color ring)
        {
            const int S = 128;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            float px = 2f / S;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v), th = Mathf.Atan2(v, u);
                    float tooth = Mathf.Clamp01((Mathf.Cos(th * 8f) - 0.05f) * 2.5f);
                    float R = 0.60f + 0.17f * tooth;
                    float aGear = Mathf.Clamp01((R - r) / px + 0.5f) * Mathf.Clamp01((r - 0.25f) / px + 0.5f);
                    float aBg = Mathf.Clamp01((0.97f - r) / px + 0.5f) * bg.a;
                    float aRing = ring.a > 0f ? Mathf.Clamp01(1f - Mathf.Abs(r - 0.93f) / (px * 2.5f)) : 0f;
                    Color col = Color.Lerp(new Color(bg.r, bg.g, bg.b), new Color(fg.r, fg.g, fg.b), aGear);
                    col = Color.Lerp(col, new Color(ring.r, ring.g, ring.b), aRing);
                    float a = Mathf.Max(aBg, Mathf.Max(aGear, aRing));
                    t.SetPixel(x, y, new Color(col.r * a, col.g * a, col.b * a, a)); // premultiplied
                }
            t.Apply();
            var rt = new RenderTexture(S, S, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.B8G8R8A8_SRGB);
            rt.Create();
            Graphics.Blit(t, rt, new Vector2(1f, -1f), new Vector2(0f, 1f));
            UnityEngine.Object.Destroy(t);
            return rt;
        }

        // ---------------------------------------------------------------- canvas
        private static void EnsureCanvas()
        {
            if (_cam == null)
            {
                var go = new GameObject("MimesisVR_SettingsCamera");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.transform.position = new Vector3(0f, -20000f, 0f);
                _cam = go.AddComponent<Camera>();
                _cam.enabled = false;
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                _cam.cullingMask = 1 << UiLayer;
                _cam.nearClipPlane = 0.05f; _cam.farClipPlane = 10f;
                _cam.allowHDR = false; _cam.allowMSAA = false;
                var d = CameraExtensions.GetUniversalAdditionalCameraData(_cam);
                if (d != null)
                {
                    d.renderPostProcessing = false; d.antialiasing = AntialiasingMode.None; d.renderShadows = false;
                    d.requiresColorOption = CameraOverrideOption.Off; d.requiresDepthOption = CameraOverrideOption.Off;
                }
            }
            if (_raw == null || !_raw.IsCreated())
            {
                _raw = new RenderTexture(W, H, 24, UnityEngine.Experimental.Rendering.GraphicsFormat.B8G8R8A8_SRGB);
                _raw.Create();
                PanelTex = new RenderTexture(W, H, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.B8G8R8A8_SRGB);
                PanelTex.Create();
                _cam.targetTexture = _raw;
            }
            if (_canvas == null)
            {
                var go = new GameObject("MimesisVR_SettingsCanvas") { layer = UiLayer };
                UnityEngine.Object.DontDestroyOnLoad(go);
                _canvas = go.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceCamera;
                _canvas.worldCamera = _cam;
                _canvas.planeDistance = 1f;
                _canvas.pixelPerfect = false;
                _dirty = true;
            }
        }

        private static RectTransform Box(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name) { layer = UiLayer };
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        private static Image Fill(RectTransform rt, Color c, bool sprite = false)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            img.color = c;
            return img;
        }

        /// <summary>Thin frame inside a rectangle (four 2-px bars).</summary>
        private static void Frame(RectTransform rt, Color c, float t = 2f)
        {
            var size = rt.sizeDelta;
            Fill(Box(rt, "FrameT", 0, 0, size.x, t), c);
            Fill(Box(rt, "FrameB", 0, size.y - t, size.x, t), c);
            Fill(Box(rt, "FrameL", 0, 0, t, size.y), c);
            Fill(Box(rt, "FrameR", size.x - t, 0, t, size.y), c);
        }

        private static Color WithA(Color c, float a) => new Color(c.r, c.g, c.b, a);

        private enum Al { Left, Center, Right, TopLeft }

        private static Label MakeLabel(RectTransform parent, string text, float size, Color col, Al al, bool wrap = false)
        {
            // Text always lives on its own child: a GameObject can hold only one Graphic (the Image of a button)
            var rt = new GameObject("Text") { layer = UiLayer }.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var l = new Label();
            if (_font != null)
            {
                var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
                t.font = _font;
                t.fontSize = size;
                t.raycastTarget = false;
                t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
                t.overflowMode = TextOverflowModes.Ellipsis;
                t.alignment = al switch { Al.Left => TextAlignmentOptions.MidlineLeft, Al.Center => TextAlignmentOptions.Midline, Al.Right => TextAlignmentOptions.MidlineRight, _ => TextAlignmentOptions.TopLeft };
                l.Tmp = t;
            }
            else
            {
                _legacyFont ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var t = rt.gameObject.AddComponent<Text>();
                t.font = _legacyFont;
                t.fontSize = Mathf.RoundToInt(size);
                t.raycastTarget = false;
                t.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
                t.alignment = al switch { Al.Left => TextAnchor.MiddleLeft, Al.Center => TextAnchor.MiddleCenter, Al.Right => TextAnchor.MiddleRight, _ => TextAnchor.UpperLeft };
                l.Legacy = t;
            }
            l.Color = col;
            l.Text = text;
            return l;
        }

        private static Hit AddHit(RectTransform rt, Image bg, Color normal, Color hover, Action click, Action<int> step = null, MimesisSettings.Def def = null)
        {
            var h = new Hit { Rt = rt, Bg = bg, Normal = normal, HoverCol = hover, Click = click, Step = step, Def = def };
            _hits.Add(h);
            return h;
        }

        private static bool _forceCanvas;

        private static void Rebuild()
        {
            _dirty = false;
            _forceCanvas = true;
            _rebuildFails = 0;
            _hits.Clear(); _rows.Clear(); _hovered = null;
            if (MimesisLoc.Refresh() | _fontLang != MimesisLoc.Code) // build 48: language changed (setting or the game) - font with its letters
            {
                try { _styleSig = SampleStyle(); } catch (Exception ex) { VRModCore.LogWarning("[MimesisVR] Settings style: " + ex.Message); }
            }
            if (_content != null) UnityEngine.Object.DestroyImmediate(_content.gameObject);
            var root = _canvas.transform as RectTransform;
            _content = Box(root, "Content", 0, 0, W, H);
            int lang = MimesisSettings.Lang;
            var rowHits = new List<Hit>();

            // background + header
            Fill(_content, cBg);
            var header = Box(_content, "Header", 0, 0, W, 96);
            Fill(header, cHeader);
            MakeLabel(Box(header, "Title", 40, 0, 740, 96), "MIMESIS VR  -  " + L("Settings", "Настройки"), 44, cText, Al.Left);
            var accentLine = Box(_content, "Line", 0, 96, W, 4);
            Fill(accentLine, cAccent);

            // language + close
            float bx = W - 40 - 84;
            var close = Box(header, "Close", bx, 12, 84, 72);
            Color cLine = WithA(cText, 0.22f);
            var closeBg = Fill(close, cBtn);
            Frame(close, cLine);
            MakeLabel(close, "X", 40, cText, Al.Center);
            AddHit(close, closeBg, cBtn, cBtnHover, () => { Close(); CloseFlat(); _holdUntilRelease = true; });
            bx -= 24 + 92;
            // build 49: Auto (as the game) + every language (up to 5 here; more in Service - Language)
            int nb = Math.Min(MimesisLoc.Langs.Count, 5) + 1;
            int selIdx = MimesisSettings.LanguageIndex;
            for (int i = nb - 1; i >= 0; i--)
            {
                int li = i;
                var b = Box(header, "Lang" + i, bx, 12, 92, 72);
                bool sel = selIdx == i;
                Color selBg = Color.Lerp(cBtn, cAccent, 0.28f);
                var bg = Fill(b, sel ? selBg : cBtn);
                Frame(b, sel ? cAccent : cLine);
                string txt = i == 0 ? L("Auto", "Авто") : MimesisLoc.Langs[i - 1].Code.ToUpperInvariant().Replace("_", "");
                MakeLabel(b, txt, i == 0 ? 28 : 32, sel ? cAccent : cText, Al.Center);
                AddHit(b, bg, sel ? selBg : cBtn, sel ? selBg : cBtnHover, () => { if (MimesisSettings.LanguageIndex != li) { MimesisSettings.SetLanguage(li); _dirty = true; } });
                bx -= 8 + 92;
            }

            // tabs
            float ty = 124;
            if (!MimesisSettings.Tabs.Any(t => t.Id == _tab && MimesisSettings.Visible(t))) _tab = MimesisSettings.Tabs[0].Id; // build 47
            foreach (var tab in MimesisSettings.Tabs)
            {
                if (!MimesisSettings.Visible(tab)) continue;
                // build 40: 10 tabs - a bit lower and closer so they fit above the build number
                // build 44: 11 tabs - 64 px high, 72 px step
                var tb = Box(_content, "Tab_" + tab.Id, 24, ty, 330, 64);
                bool sel = tab.Id == _tab;
                var bg = Fill(tb, sel ? cBtnHover : cBtn);
                if (sel) Fill(Box(tb, "Mark", 0, 0, 6, 64), cAccent);
                MakeLabel(Box(tb, "L", 24, 0, 296, 64), MimesisLoc.Tr(tab.Label), 29, sel ? cText : cDim, Al.Left);
                string id = tab.Id;
                AddHit(tb, bg, sel ? cBtnHover : cBtn, cBtnHover, () => { if (_tab != id) { _tab = id; _showLayout = false; _dirty = true; } });
                ty += 72;
            }
            MakeLabel(Box(_content, "Ver", 24, H - 70, 330, 50), $"build {VRModCore.VERSION.Split('.').Last()}", 24, cDim, Al.Left);
            if (MimesisSettings.GameVersionDiffers) // build 45: the game was updated since the mod was tested
                MakeLabel(Box(header, "GameVer", 760, 0, 420, 96), L($"game {Application.version}, tested {MimesisSettings.TestedGameVersion}", $"игра {Application.version}, проверено на {MimesisSettings.TestedGameVersion}"), 24, cAccent, Al.Left, wrap: true);

            // rows
            float x0 = 380, rwFull = W - x0 - 24, ry = 124;
            var tabDefs = MimesisSettings.All.Where(x => x.Tab == _tab && MimesisSettings.Visible(x)).ToList();
            if (_showLayout && _tab == "binds") { tabDefs.Clear(); BuildLayout(x0, ry, rwFull, H - 124 - 12 - ry, lang, cLine); }
            else _showLayout = false;
            int total = tabDefs.Count;
            bool scrolls = total > VisibleRows;
            float rw = scrolls ? rwFull - 28 : rwFull;
            int first = Mathf.Clamp(ScrollOf(_tab), 0, Math.Max(0, total - VisibleRows));
            _scroll[_tab] = first;
            if (scrolls)
            {
                // Scroll bar: track + thumb, so it is clear the list goes on
                float trackH = VisibleRows * 80 - 6;
                var track = Box(_content, "ScrollTrack", x0 + rwFull - 14, 124, 10, trackH);
                Fill(track, WithA(cText, 0.12f));
                float thumbH = Mathf.Max(40f, trackH * VisibleRows / total);
                float thumbY = (trackH - thumbH) * first / Mathf.Max(1, total - VisibleRows);
                Fill(Box(track, "Thumb", 0, thumbY, 10, thumbH), cAccent);
            }
            for (int ri = first; ri < Math.Min(total, first + VisibleRows); ri++)
            {
                var d = tabDefs[ri];
                var def = d;
                var row = Box(_content, "Row_" + d.Id, x0, ry, rw, 74);
                var rbg = Fill(row, cRow);
                Fill(Box(row, "Sep", 0, 72, rw, 2), cLine);
                MakeLabel(Box(row, "L", 24, 0, 600, 72), MimesisLoc.Tr(d.Label), 30, cText, Al.Left);
                if (d.Kind == MimesisSettings.Kind.Action)
                {
                    var fb = MakeLabel(Box(row, "F", 640, 0, rw - 640 - 70, 74), "", 28, cAccent, Al.Right);
                    MakeLabel(Box(row, "Go", rw - 64, 0, 44, 74), ">", 34, cDim, Al.Center);
                    rowHits.Add(AddHitDeferred(row, rbg, cRow, cRowHover, () => RunAction(def), def.Id == "TestItem" ? (Action<int>)(st => Step(def, st)) : null, def));
                    _rows.Add(new Row { Def = d, Feedback = fb });
                }
                else
                {
                    float cw = 470, cx = rw - 24 - cw;
                    var ctrl = Box(row, "C", cx, 8, cw, 56);
                    Fill(ctrl, cBtn);
                    Label val;
                    if (d.Count >= 2)
                    {
                        var lA = Box(ctrl, "Prev", 0, 0, 64, 56); var lBg = Fill(lA, new Color(1, 1, 1, 0));
                        MakeLabel(lA, "<", 34, cText, Al.Center);
                        var rA = Box(ctrl, "Next", cw - 64, 0, 64, 56); var rBg = Fill(rA, new Color(1, 1, 1, 0));
                        MakeLabel(rA, ">", 34, cText, Al.Center);
                        val = MakeLabel(Box(ctrl, "V", 64, 0, cw - 128, 56), "", 29, cText, Al.Center);
                        AddHit(lA, lBg, new Color(1, 1, 1, 0), cBtnHover, () => Step(def, -1), s => Step(def, s), def);
                        AddHit(rA, rBg, new Color(1, 1, 1, 0), cBtnHover, () => Step(def, 1), s => Step(def, s), def);
                    }
                    else val = MakeLabel(Box(ctrl, "V", 0, 0, cw, 56), "", 29, cText, Al.Center); // single option
                    Frame(ctrl, cLine);
                    rowHits.Add(AddHitDeferred(row, rbg, cRow, cRowHover, () => Step(def, 1), s => Step(def, s), def));
                    _rows.Add(new Row { Def = d, Value = val, IsBool = d.Count == 2 && d.Options[0][0] == "Off" });
                }
                ry += 80;
            }
            _hits.AddRange(rowHits); // rows last: their buttons win

            // description / hint
            var descBox = Box(_content, "Desc", x0, H - 124, rw, 104);
            Fill(descBox, new Color(0f, 0f, 0f, 0.25f));
            _desc = MakeLabel(Box(descBox, "T", 24, 12, rw - 48, 80), "", 26, cDim, Al.TopLeft, wrap: true);

            // cursor (topmost)
            if (_cursor == null)
            {
                _cursor = new GameObject("Cursor") { layer = UiLayer }.AddComponent<RectTransform>();
                _cursor.SetParent(root, false);
                _cursor.anchorMin = _cursor.anchorMax = Vector2.zero;
                _cursor.pivot = new Vector2(0.5f, 0.5f);
                _cursor.sizeDelta = new Vector2(30, 30);
                var img = _cursor.gameObject.AddComponent<RawImage>();
                img.raycastTarget = false;
                img.texture = MakeDot();
            }
            _cursor.SetAsLastSibling();
        }

        // ---------------------------------------------------------------- build 44: controller layout
        private static Texture2D _disc;

        private static Texture2D Disc()
        {
            if (_disc != null) return _disc;
            const int S = 128;
            _disc = new Texture2D(S, S, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Bilinear };
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f, r = Mathf.Sqrt(u * u + v * v);
                    _disc.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((1f - r) * S * 0.5f)));
                }
            _disc.Apply();
            return _disc;
        }

        private static void Circle(RectTransform parent, float cx, float cy, float d, Color c, string text = null, float fs = 26, Color? tc = null)
        {
            var rt = Box(parent, "Disc", cx - d / 2f, cy - d / 2f, d, d);
            var img = rt.gameObject.AddComponent<RawImage>();
            img.raycastTarget = false; img.texture = Disc(); img.color = c;
            if (text != null) MakeLabel(rt, text, fs, tc ?? cText, Al.Center);
        }

        private static string Actions(MimesisBinds.Btn b, int lang)
        {
            var on = MimesisBinds.Slots.Where(s => s.Cur == b).Select(s => MimesisLoc.Tr(s.En, s.Ru)).ToList();
            return on.Count > 0 ? string.Join(", ", on) : "-";
        }

        private static void BuildLayout(float x, float y, float w, float h, int lang, Color cLine)
        {
            var area = Box(_content, "Layout", x, y, w, h);
            // back to the list
            var back = Box(area, "Back", 0, 0, 300, 60);
            var backBg = Fill(back, cBtn);
            Frame(back, cLine);
            MakeLabel(back, L("< Back to the list", "< К списку"), 28, cText, Al.Center);
            AddHit(back, backBg, cBtn, cBtnHover, () => ShowLayout = false);
            string conf = MimesisBinds.Conflicts(lang);
            if (conf.Length > 0) MakeLabel(Box(area, "Conf", 320, 0, w - 320, 60), L("Shared: ", "Совмещено: ") + conf, 22, cAccent, Al.Left, wrap: true);

            bool swap = MimesisBinds.SwapSticks;
            string move = L("move", "ходьба"), turn = L("turn; up/down - slots", "поворот; вверх/вниз - слоты");
            float colW = (w - 24) / 2f;
            for (int side = 0; side < 2; side++)
            {
                bool left = side == 0;
                var col = Box(area, left ? "LeftCtl" : "RightCtl", side * (colW + 24), 76, colW, h - 76);
                Fill(col, new Color(1f, 1f, 1f, 0.03f));
                MakeLabel(Box(col, "Title", 0, 0, colW, 50), left ? L("Left controller", "Левый контроллер") : L("Right controller", "Правый контроллер"), 30, cAccent, Al.Center);

                // face of the controller: ring, stick, two face buttons (and the menu button on the left)
                float fx = colW / 2f, fy = 180;
                Circle(col, fx, fy, 240, WithA(cText, 0.10f));
                Circle(col, fx, fy, 228, new Color(0.08f, 0.085f, 0.09f, 1f));
                Color btnC = Color.Lerp(cBtn, cAccent, 0.25f);
                // as seen from above: the stick on the outer side, the face buttons towards the middle
                float sg = left ? 1f : -1f;
                Circle(col, fx - sg * 34, fy - 50, 92, new Color(0.22f, 0.23f, 0.24f, 1f), L("stick", "стик"), 22, cDim);
                Circle(col, fx + sg * 52, fy + 18, 62, btnC, left ? "Y" : "B", 30);
                Circle(col, fx + sg * 6, fy + 72, 62, btnC, left ? "X" : "A", 30);
                if (left) Circle(col, fx - 62, fy + 58, 34, new Color(0.3f, 0.3f, 0.32f, 1f), "M", 20);

                bool moveHere = left != swap;
                var lines = new List<(string k, string v)>
                {
                    (L("Trigger", "Курок"), Actions(left ? MimesisBinds.Btn.LTrigger : MimesisBinds.Btn.RTrigger, lang)),
                    (L("Grip", "Захват"), Actions(left ? MimesisBinds.Btn.LGrip : MimesisBinds.Btn.RGrip, lang)),
                    (left ? "Y" : "B", Actions(left ? MimesisBinds.Btn.Y : MimesisBinds.Btn.B, lang)),
                    (left ? "X" : "A", Actions(left ? MimesisBinds.Btn.X : MimesisBinds.Btn.A, lang)),
                    (L("Stick", "Стик"), moveHere ? move : turn),
                    (L("Stick click", "Нажать стик"), Actions(left ? MimesisBinds.Btn.LStick : MimesisBinds.Btn.RStick, lang)),
                };
                if (left) lines.Add((L("Menu", "Меню"), L("game menu", "меню игры")));
                float ly = 318;
                foreach (var (k, v) in lines)
                {
                    MakeLabel(Box(col, "K", 20, ly, 200, 46), k, 26, cDim, Al.Left);
                    MakeLabel(Box(col, "V", 220, ly, colW - 236, 46), v, 26, cText, Al.Left);
                    ly += 48;
                }
            }
        }

        private static Hit AddHitDeferred(RectTransform rt, Image bg, Color normal, Color hover, Action click, Action<int> step, MimesisSettings.Def def)
            => new Hit { Rt = rt, Bg = bg, Normal = normal, HoverCol = hover, Click = click, Step = step, Def = def };

        private static Texture2D MakeDot()
        {
            const int S = 64;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Bilinear };
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f, r = Mathf.Sqrt(u * u + v * v);
                    float a = Mathf.Clamp01((1f - r) * S * 0.5f);
                    Color c = r < 0.45f ? new Color(0.85f, 1f, 0.88f) : r < 0.75f ? cCursor : new Color(0.05f, 0.35f, 0.1f);
                    t.SetPixel(x, y, new Color(c.r, c.g, c.b, a));
                }
            t.Apply();
            return t;
        }

        private static void Step(MimesisSettings.Def d, int dir)
        {
            if (d.Kind != MimesisSettings.Kind.Choice)
            {
                if (d.Id == "TestItem") { MimesisCheats.SelectItem(dir); RunActionFeedback(d); return; }
                if (dir > 0) RunAction(d);
                return;
            }
            MimesisSettings.Change(d, MimesisSettings.Current(d) + dir);
            if (d.Id == "Language") _dirty = true;
        }

        private static void RunAction(MimesisSettings.Def d)
        {
            try { d.Run?.Invoke(); }
            catch (Exception ex) { VRModCore.LogError($"[MimesisVR] Action {d.Id} failed:", ex); }
            _feedbackId = d.Id;
            _feedbackText = d.Id.StartsWith("Mark") ? L($"Marked #{MimesisEffectsProbe.Marks}", $"Отмечено #{MimesisEffectsProbe.Marks}")
                : d.Id.StartsWith("Test") ? MimesisCheats.LastResult : L("Done", "Готово");
            _feedbackUntil = Time.unscaledTime + 2.5f;
            if (d.Id == "ResetAll") _dirty = true;
        }

        private static void RunActionFeedback(MimesisSettings.Def d)
        {
            _feedbackId = d.Id; _feedbackText = MimesisCheats.LastResult; _feedbackUntil = Time.unscaledTime + 2.5f;
        }

        private static void RefreshValues()
        {
            int lang = MimesisSettings.Lang;
            foreach (var r in _rows)
            {
                if (r.Value != null)
                {
                    int cur = MimesisSettings.Current(r.Def);
                    if (cur != r.Last)
                    {
                        r.Last = cur;
                        r.Value.Text = MimesisLoc.Tr(r.Def.Options[0][cur], r.Def.Options[1][cur]);
                        if (r.IsBool) r.Value.Color = cur == 1 ? cAccent : cDim;
                    }
                }
                if (r.Feedback != null)
                {
                    bool fresh = _feedbackId == r.Def.Id && Time.unscaledTime < _feedbackUntil;
                    // the chosen item stays written next to the item rows (it used to vanish after 2.5 s)
                    if (!fresh && (r.Def.Id == "TestItem" || r.Def.Id == "TestLootAll") && !string.IsNullOrEmpty(MimesisCheats.SelectedName))
                        r.Feedback.Text = MimesisCheats.SelectedName;
                    else r.Feedback.Text = fresh ? _feedbackText : "";
                }
            }
            if (_desc != null)
                _desc.Text = _hovered?.Def != null ? MimesisLoc.Tr(_hovered.Def.Desc) + (_hovered.Def.Id == "FrameLimit" ? MimesisImage.FrameWarning : "")
                    + (_hovered.Def.Id.StartsWith("ItemGrip") ? MimesisItemGrips.Label(lang) : "")
                    : FlatOpen ? L("Click - select or change     Wheel - scroll     Right click - close", "Клик - выбрать или изменить     Колесо - прокрутка     Правая кнопка - закрыть")
                    : L("Trigger - select     Stick left/right - change, up/down - scroll     B - close", "Курок - выбрать     Стик влево/вправо - изменить, вверх/вниз - прокрутка     B - закрыть");
        }

        // ---------------------------------------------------------------- style from the game's UI
        private static string PathOf(Transform t)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; t != null && i < 8; i++, t = t.parent) sb.Insert(0, "/" + t.name);
            return sb.ToString();
        }

        private static float Weight(string path)
        {
            if (path.Contains("SettingListItem") || path.Contains("GameSettings")) return 6f;
            if (path.Contains("InGameMenu") || path.Contains("MainMenu") || path.Contains("Title") || path.Contains("Lobby")) return 3f;
            return 1f;
        }

        private static int Quant(Color c) => (Mathf.RoundToInt(c.r * 15) << 12) | (Mathf.RoundToInt(c.g * 15) << 8) | (Mathf.RoundToInt(c.b * 15) << 4) | Mathf.RoundToInt(c.a * 15);

        /// <summary>Takes font, text colours and button sprite from the game's menus. Returns a signature to detect changes.</summary>
        private static string _fontLang;
        private static readonly Dictionary<string, TMP_FontAsset> _osFonts = new();

        /// <summary>A Windows font with the letters of the language, when no game font has them (build 48).</summary>
        private static TMP_FontAsset OsFont(string probe)
        {
            string[] names = { "Malgun Gothic", "Yu Gothic UI", "Microsoft YaHei UI", "Microsoft JhengHei UI", "Leelawadee UI", "Segoe UI", "Arial" };
            foreach (var n in names)
            {
                try
                {
                    if (!_osFonts.TryGetValue(n, out var fa))
                    {
                        var f = Font.CreateDynamicFontFromOSFont(n, 64);
                        fa = f != null ? TMP_FontAsset.CreateFontAsset(f) : null;
                        _osFonts[n] = fa;
                    }
                    if (fa != null && fa.HasCharacters(probe, out uint[] _, false, true))
                    {
                        VRModCore.Log($"[MimesisVR] Settings font: no game font has the letters of '{MimesisLoc.Code}', using Windows font {n}.");
                        return fa;
                    }
                }
                catch (Exception ex) { VRModCore.LogWarning($"[MimesisVR] Font {n}: {ex.Message}"); }
            }
            return null;
        }

        private static string SampleStyle()
        {
            var fonts = new Dictionary<TMP_FontAsset, float>();
            var neutral = new Dictionary<int, (Color c, float w)>();
            var accent = new Dictionary<int, (Color c, float w)>();
            foreach (var t in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (t == null || t.font == null) continue;
                if (_canvas != null && t.transform.IsChildOf(_canvas.transform)) continue;
                string path = PathOf(t.transform);
                float w = Weight(path);
                fonts[t.font] = (fonts.TryGetValue(t.font, out var fw) ? fw : 0f) + w;
                Color c = t.color;
                if (c.a < 0.6f) continue;
                float mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                var dict = (mx - mn) < 0.15f ? neutral : accent;
                if ((mx - mn) >= 0.15f && mx < 0.45f) continue; // dark coloured text - not an accent
                int k = Quant(c);
                dict[k] = dict.TryGetValue(k, out var e) ? (e.c, e.w + w) : (c, w);
            }
            // build 48: the font must have the letters of the current language (Korean etc.)
            string probe = "Settings 0123456789 " + L("Settings", "Настройки") + " " + string.Join(" ", MimesisSettings.Tabs.Select(t => MimesisLoc.Tr(t.Label)));
            _fontLang = MimesisLoc.Code;
            _font = null;
            foreach (var kv in fonts.OrderByDescending(k => k.Value))
            {
                try { if (kv.Key.HasCharacters(probe, out uint[] _, true, true)) { _font = kv.Key; break; } } catch { }
            }
            if (_font == null && TMP_Settings.defaultFontAsset != null)
            {
                try { if (TMP_Settings.defaultFontAsset.HasCharacters(probe, out uint[] _, true, true)) _font = TMP_Settings.defaultFontAsset; } catch { }
            }
            if (_font == null) _font = OsFont(probe);
            if (_font == null && fonts.Count > 0) _font = fonts.OrderByDescending(k => k.Value).First().Key;
            if (_font == null && TMP_Settings.defaultFontAsset != null) _font = TMP_Settings.defaultFontAsset;

            // Text colour: the most common text colour of the in-game (Esc) menu - yellowish in MIMESIS
            var menuCols = new Dictionary<int, (Color c, float w)>();
            foreach (var t in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (t == null || t.color.a < 0.6f) continue;
                if (_canvas != null && t.transform.IsChildOf(_canvas.transform)) continue;
                if (PathOf(t.transform).IndexOf("InGameMenu", StringComparison.Ordinal) < 0) continue;
                Color c = t.color;
                if ((c.r + c.g + c.b) / 3f < 0.45f) continue;
                int k = Quant(c);
                menuCols[k] = menuCols.TryGetValue(k, out var e) ? (e.c, e.w + 1f) : (c, 1f);
            }
            var top = menuCols.OrderByDescending(k => k.Value.w).Take(4).Select(k => k.Value).ToList();
            if (top.Count > 0) cText = new Color(top[0].c.r, top[0].c.g, top[0].c.b, 1f);
            else cText = new Color(0.96f, 0.88f, 0.62f, 1f);
            cDim = new Color(cText.r * 0.68f, cText.g * 0.68f, cText.b * 0.68f, 1f);
            VRModCore.Log("[MimesisVR] Settings text colour from the in-game menu: " + (top.Count > 0 ? string.Join(", ", top.Select(x => $"{x.c} x{x.w:0}")) : "menu not loaded yet - default warm yellow"));
            var topAccent = accent.OrderByDescending(k => k.Value.w).Select(k => k.Value).FirstOrDefault();
            if (topAccent.w >= 3f) cAccent = new Color(topAccent.c.r, topAccent.c.g, topAccent.c.b, 1f);

            // Buttons: sliced sprite and colours of the game's own selectables
            var sprites = new Dictionary<Sprite, (float w, Color normal, Color hover)>();
            foreach (var s in Resources.FindObjectsOfTypeAll<Selectable>())
            {
                if (s == null || !(s.targetGraphic is Image im) || im.sprite == null || im.sprite.border.sqrMagnitude < 1f) continue;
                if (_canvas != null && s.transform.IsChildOf(_canvas.transform)) continue;
                float w = Weight(PathOf(s.transform));
                Color n = im.color * s.colors.normalColor, h = im.color * s.colors.highlightedColor;
                sprites[im.sprite] = sprites.TryGetValue(im.sprite, out var e) ? (e.w + w, e.normal, e.hover) : (w, n, h);
            }
            var best = sprites.OrderByDescending(k => k.Value.w).FirstOrDefault();
            _btnSprite = null; // flat colours only
            VRModCore.Log($"[MimesisVR] Settings style: font={(_font != null ? _font.name : "built-in")} ({fonts.Count} fonts seen), text={cText}, accent={cAccent} (votes {topAccent.w:F0})");
            return $"{(_font != null ? _font.name : "legacy")}|{cText}|{cAccent}";
        }
    }
}
