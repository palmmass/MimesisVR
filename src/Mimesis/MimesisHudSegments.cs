using System.Text;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityVRMod.Core;

namespace UnityVRMod.Mimesis
{
    /// <summary>
    /// MIMESIS VR: splits the rendered game UI into pieces attached to the hands.
    ///  - Inventory slots  -> left wrist
    ///  - Crosshair + interaction hint -> end of the right-hand pointer
    ///  - While a menu is open the in-game HUD panels are hidden so only the menu is shown.
    /// Pieces are cut out of the UI texture (CopyTexture) and erased from the head-locked HUD.
    /// </summary>
    internal static class MimesisHudSegments
    {
        public static bool Enabled = true; // Insert toggles
        public static float WristWidth = 0.26f;
        public static float PointerScale = 1f;
        /// <summary>true: inventory floats above the left controller facing you; false: lies on the wrist.</summary>
        public static bool WristAbove = true;

        /// <summary>Health and contamination next to the inventory: 0 = above the slots, 1 = below, 2 = on the screen (as in the game).</summary>
        public static int StatusPlace = 0;
        public static float StatusX = 0f, StatusY = 0f, StatusScale = 1f;
        /// <summary>Key / item tips (right top of the screen): 0 = on the right hand, 1 = on the screen, 2 = hidden.</summary>
        public static int TipsPlace = 0;
        public static float TipsX = 0f, TipsY = 0f, TipsZ = 0f, TipsTilt = 0f, TipsScale = 1f;
        /// <summary>Tips at the bottom right (tram): 0 = hidden, 1 = on the screen.</summary>
        public static int TipsRBPlace = 0;

        /// <summary>Pose of the left-hand panel (inventory) and of the gear under it, rig-local.</summary>
        public static float InvX, InvY, InvZ, InvTilt;

        public static void LeftPanelPose(UnityVRMod.Features.VRVisualization.OpenXR.XrControllerInput c, float height, out Vector3 pos, out Quaternion rot, out Vector3 gearPos)
        {
            if (WristAbove)
            {
                // Frame: forward = horizontal direction from the head to the controller, right, up
                Vector3 fwd = c.LeftPos - MimesisInput.HeadLocalPos; fwd.y = 0f;
                fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                Vector3 baseP = c.LeftPos + Vector3.up * (0.06f + InvY) + right * InvX + fwd * InvZ;
                pos = baseP + Vector3.up * (height * 0.5f + 0.03f);
                Vector3 toEye = pos - MimesisInput.HeadLocalPos;
                rot = toEye.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(toEye.normalized, Vector3.up) : Quaternion.identity;
                if (InvTilt != 0f) rot = rot * Quaternion.Euler(InvTilt, 0f, 0f);
                gearPos = baseP;
            }
            else
            {
                Vector3 off = new Vector3(InvX, 0.04f + InvY, -0.07f + InvZ);
                rot = c.LeftRot * Quaternion.LookRotation(Vector3.down, Vector3.forward);
                if (InvTilt != 0f) rot = rot * Quaternion.Euler(InvTilt, 0f, 0f);
                pos = c.LeftPos + c.LeftRot * off;
                gearPos = c.LeftPos + c.LeftRot * (off + new Vector3(0f, 0f, -(height * 0.5f + 0.035f)));
            }
        }

        public sealed class Segment
        {
            public string Name;
            public string Match;
            /// <summary>Optional: the piece is this child of the matched panel (e.g. HPGraph inside UIPrefab_InGame).</summary>
            public string Child;
            /// <summary>Optional: the piece is made of these children of the matched panel, each taken with its small
            /// surrounding group (icon, frame); the panel itself is only used to know it is shown.</summary>
            public string[] Roots;
            internal readonly List<Transform> RootTs = new();
            internal bool Built;
            /// <summary>Do not grow the cut-out over neighbouring elements.</summary>
            public bool NoExpand;
            public RenderTexture Tex;
            /// <summary>What is shown on the headset layer (the mod's own wrist panel replaces the cut-out); null = Tex.</summary>
            public RenderTexture DrawTex;
            public int DrawW, DrawH;
            public bool Active;
            /// <summary>Time of the last cut (the own wrist panel copies the status piece from Tex).</summary>
            public float CutTime = -1f;
            public int W, H;           // valid pixel size inside Tex (top-left aligned)
            public Vector3 LocalPos;
            public Quaternion LocalRot = Quaternion.identity;
            public Vector2 Size;
            internal Transform Panel;
            internal readonly List<Graphic> Graphics = new();
            /// <summary>Cut-out area in UI-texture pixels (x right, y up from the bottom).</summary>
            public RectInt ScreenRect;
            /// <summary>Wrist only: inventory slot rectangles in UI-texture pixels (bottom-up), in display order.</summary>
            public readonly List<Rect> SlotRects = new();
            public int HoverSlot = -1;
            /// <summary>Game inventory slot index for each entry of SlotRects.</summary>
            public readonly List<int> SlotGameIndex = new();
            public Color HighlightColor = new Color(0.55f, 0.5f, 0.35f, 0.4f);
        }

        /// <summary>Graphics of the screen effects (rain, contamination) - never part of a segment.</summary>
        public static readonly HashSet<Graphic> FxGraphics = new();
        private static readonly List<Graphic> _allGraphics = new();

        public static readonly Segment Wrist = new() { Name = "Wrist", Match = "UIPrefab_Inventory" };
        public static readonly Segment Pointer = new() { Name = "Pointer", Match = "UIPrefab_Crosshair" };
        /// <summary>Health (HPGraph) and contamination (OxyGauge, right under it) - one piece, as laid out by the game.</summary>
        public static readonly Segment Status = new() { Name = "Status", Match = "UIPrefab_InGame(", Roots = new[] { "HPGraph", "OxyGauge" }, NoExpand = true };
        // order matters: the status piece is placed relative to the wrist piece
        /// <summary>Key / item tips (UIPrefab_GameTips/RT) - on the right hand.</summary>
        public static readonly Segment Tips = new() { Name = "Tips", Match = "UIPrefab_GameTips(", Child = "RT", NoExpand = true };
        /// <summary>Tips at the bottom right (UIPrefab_GameTips/RB, tram) - cut out and not shown.</summary>
        public static readonly Segment TipsRB = new() { Name = "TipsRB", Match = "UIPrefab_GameTips(", Child = "RB", NoExpand = true };
        /// <summary>The tram tip ("tram settings F1" + PUBLIC picture) - a separate element of GameTips.</summary>
        public static readonly Segment TipsTram = new() { Name = "TipsTram", Match = "UIPrefab_GameTips(", Child = "UI_GameTip_PublicTram", NoExpand = true };
        private static readonly Segment[] _segments = { Wrist, Status, Tips, TipsRB, TipsTram, Pointer };
        private static Transform _stamina;
        private static int _outW = 1920;
        private static float _slotsTop, _slotsBottom, _staminaTop;
        private static bool _slotsValid;

        // HUD panels hidden while a menu is open
        private static readonly string[] HudPanelNames = { "UIPrefab_InGame(", "UIPrefab_Crosshair", "UIPrefab_Inventory", "UIPrefab_GameTips", "UIPrefab_GameHint", "UIPrefab_PlayerEnterInfo", "UIScreenEffect_Rain" };
        private static readonly List<Transform> _hudPanels = new();
        private static readonly Dictionary<CanvasRenderer, float> _hiddenAlpha = new();
        private static float _nextScan;
        private static Material _clearMat;
        private static bool _dumped;

        /// <summary>Find HUD panels under the converted root canvases (cached, rescanned twice a second).</summary>
        public static void Scan(IEnumerable<Canvas> canvases)
        {
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 0.5f;
            _hudPanels.Clear();
            foreach (var s in _segments) { s.Panel = null; s.Graphics.Clear(); s.RootTs.Clear(); s.Built = false; }
            _stamina = null;
            _uiInventory = null;
            _allGraphics.Clear();
            foreach (var c in canvases)
            {
                if (c == null) continue;
                c.GetComponentsInChildren(false, _tmpGraphics);
                _allGraphics.AddRange(_tmpGraphics);
                foreach (Transform layer in c.transform)
                    foreach (Transform panel in layer)
                    {
                        string n = panel.name;
                        if (_stamina == null && n.StartsWith("UIPrefab_InGame(")) _stamina = FindDeep(panel, "StaminaGauge");
                        foreach (var hn in HudPanelNames) if (n.StartsWith(hn)) { _hudPanels.Add(panel); break; }
                        foreach (var s in _segments)
                        {
                            if (s.Panel != null || !n.StartsWith(s.Match)) continue;
                            if (s.Roots != null)
                            {
                                foreach (var rn in s.Roots) { var rt = FindDeep(panel, rn); if (rt != null) s.RootTs.Add(rt); }
                                if (s.RootTs.Count == 0) continue;
                                s.Panel = panel; // graphics are collected in Process (needs the UI camera)
                                continue;
                            }
                            var target = s.Child != null ? FindDeep(panel, s.Child) : panel;
                            if (target == null) continue;
                            s.Panel = target; target.GetComponentsInChildren(true, s.Graphics);
                        }
                    }
            }
            if (Wrist.Panel != null) _uiInventory = Wrist.Panel.GetComponent<UIPrefab_Inventory>() ?? Wrist.Panel.GetComponentInChildren<UIPrefab_Inventory>(true);
            if (!_dumped && Wrist.Panel != null) { _dumped = true; DumpRects(); }
        }

        private static readonly List<Graphic> _tmpGraphics = new();

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform ch in t) { var r = FindDeep(ch, name); if (r != null) return r; }
            return null;
        }

        /// <summary>Menu open: hide HUD panels (alpha 0 on their renderers). Menu closed: restore.</summary>
        public static void SetHudHidden(bool hide)
        {
            if (hide)
            {
                foreach (var p in _hudPanels)
                {
                    if (p == null) continue;
                    foreach (var g in p.GetComponentsInChildren<Graphic>(false))
                    {
                        var cr = g.canvasRenderer;
                        if (cr == null || _hiddenAlpha.ContainsKey(cr)) continue;
                        _hiddenAlpha[cr] = cr.GetAlpha();
                        cr.SetAlpha(0f);
                    }
                }
            }
            else if (_hiddenAlpha.Count > 0)
            {
                foreach (var kv in _hiddenAlpha) if (kv.Key != null) kv.Key.SetAlpha(kv.Value);
                _hiddenAlpha.Clear();
            }
        }

        /// <summary>Cut the segments out of the (OpenXR-oriented) HUD texture. Call after the UI render + flip.</summary>
        public static void Process(RenderTexture output, Camera uiCam)
        {
            foreach (var s in _segments) s.Active = false;
            if (!Enabled || output == null || uiCam == null) return;
            int W = output.width, H = output.height;
            _outW = W;
            var ctl = MimesisInput.Controllers;
            foreach (var s in _segments)
            {
                if (s.Panel == null || !s.Panel.gameObject.activeInHierarchy) continue;
                if (s == Pointer && !MimesisInput.AimValid)
                {
                    // Hand mode but the right controller is not tracked (put down): hide the piece instead of
                    // dropping it back into the head-locked HUD. Gaze mode / open panels keep it in the HUD.
                    bool lost = MimesisInput.InteractByHand && ctl != null && ctl.Ready && !ctl.RightPoseValid && !MimesisSettingsUI.Open && !MimesisInput.PanelVisible;
                    if (!lost) continue;
                }
                if (s == Wrist && (ctl == null || !ctl.Ready)) continue;                                  // no controllers at all: keep it in the HUD
                // stays on the screen; with the mod's own wrist panel it is always cut (shown only inside the panel,
                // hidden with it - e.g. after death, when the game hides the inventory)
                bool ownPanel = MimesisSettingsUI.WristHud.On && ctl != null && ctl.Ready;
                if (s == Status && (StatusPlace == 2 || (!Wrist.Active && !ownPanel))) continue;
                if (s == Tips && (TipsPlace == 1 || ctl == null || !ctl.Ready)) continue;
                if ((s == TipsRB || s == TipsTram) && TipsRBPlace == 1) continue;
                if (s.Roots != null && !s.Built) BuildRootGraphics(s, uiCam, W, H);
                RectInt r;
                if (s == Status && MimesisSettingsUI.WristHud.On)
                {
                    // stable area (laid out, visible or not) - the panel keeps a fixed place for it
                    if (!StableRect(s, uiCam, W, H, out r) && !TryGetScreenRect(s, uiCam, W, H, out r)) continue;
                }
                else if (!TryGetScreenRect(s, uiCam, W, H, out r)) continue;
                if (!s.NoExpand) r = ExpandWithOverlaps(s, r, uiCam, W, H);
                s.ScreenRect = r;
                LogPiece(s, r, uiCam, W, H);
                // OpenXR-oriented texture: screen y (bottom-up) -> texture row = H - y
                int sx = r.x, sy = H - (r.y + r.height), w = r.width, h = r.height;
                EnsureTex(s, w, h, output);
                Graphics.CopyTexture(output, 0, 0, sx, sy, w, h, s.Tex, 0, 0, 0, 0);
                s.W = w; s.H = h;
                ClearRect(output, sx, sy, w, h);
                if (s == Wrist) { UpdateSlotRects(uiCam, W, H); DrawSlotHighlight(r); MeasureWrist(uiCam, W, H); }
                s.Active = true;
                s.CutTime = Time.unscaledTime;
            }
        }

        private static readonly Vector3[] _corners = new Vector3[4];

        /// <summary>The game's stamina bar (hidden until you run).</summary>
        public static Transform StaminaRoot => _stamina;

        private static readonly List<Graphic> _stamGraphics = new();
        private static Transform _stamFor;
        private static bool _stamHidden;
        /// <summary>Build 40: with the stamina on the right watch the game's own bar is never drawn into the head-locked
        /// interface (it flashed in the old look for a moment at the first run of a level). Its state is still read.
        /// Called right before the interface picture is rendered.</summary>
        public static void HideStamina(bool hide)
        {
            if (_stamina != _stamFor)
            {
                _stamFor = _stamina; _stamGraphics.Clear();
                if (_stamina != null) _stamina.GetComponentsInChildren(true, _stamGraphics);
            }
            if (hide)
            {
                foreach (var g in _stamGraphics) if (g != null && g.canvasRenderer != null) g.canvasRenderer.SetAlpha(0f);
                _stamHidden = true;
            }
            else if (_stamHidden)
            {
                foreach (var g in _stamGraphics) if (g != null && g.canvasRenderer != null) g.canvasRenderer.SetAlpha(1f);
                _stamHidden = false;
            }
        }

        /// <summary>Area of all the piece's elements as laid out, shown or not (particles and noise left out), so the
        /// piece keeps one size while values change.</summary>
        private static bool StableRect(Segment s, Camera cam, int W, int H, out RectInt rect)
        {
            rect = default;
            float kx = (float)W / Mathf.Max(1, cam.pixelWidth), ky = (float)H / Mathf.Max(1, cam.pixelHeight);
            Rect cur = default; bool any = false;
            foreach (var g in s.Graphics)
            {
                if (g == null || FxGraphics.Contains(g)) continue;
                if (g.GetType().Name.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (g.name.IndexOf("Noise", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (!GraphicRect(g, cam, kx, ky, out Rect gr)) continue;
                if (gr.width > W * 0.25f || gr.height > H * 0.25f) continue;
                cur = any ? Rect.MinMaxRect(Mathf.Min(cur.xMin, gr.xMin), Mathf.Min(cur.yMin, gr.yMin), Mathf.Max(cur.xMax, gr.xMax), Mathf.Max(cur.yMax, gr.yMax)) : gr;
                any = true;
            }
            if (!any) return false;
            const int pad = 8;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(cur.xMin) - pad, 0, W - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(cur.yMin) - pad, 0, H - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(cur.xMax) + pad, 1, W);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(cur.yMax) + pad, 1, H);
            if (x1 - x0 < 8 || y1 - y0 < 8 || (x1 - x0) > W * 0.3f || (y1 - y0) > H * 0.3f) return false;
            rect = new RectInt(x0, y0, x1 - x0, y1 - y0);
            return true;
        }

        /// <summary>UI pixels per metre of panel width, as used for the cut-out pieces ("inventory size").</summary>
        public static float PiecePxPerWristWidth => Mathf.Max(64f, 0.31f * _outW);

        /// <summary>Screen rectangle of one graphic in UI-texture pixels (bottom-up).</summary>
        private static bool GraphicRect(Graphic g, Camera cam, float kx, float ky, out Rect rect)
        {
            rect = default;
            float xmin = float.MaxValue, ymin = float.MaxValue, xmax = float.MinValue, ymax = float.MinValue;
            void Acc(Vector3 world)
            {
                Vector3 p = cam.WorldToScreenPoint(world);
                xmin = Mathf.Min(xmin, p.x); ymin = Mathf.Min(ymin, p.y);
                xmax = Mathf.Max(xmax, p.x); ymax = Mathf.Max(ymax, p.y);
            }
            g.rectTransform.GetWorldCorners(_corners);
            for (int i = 0; i < 4; i++) Acc(_corners[i]);
            // Text can overflow its rectangle: add the actual text bounds
            if (g is TMPro.TMP_Text tmp)
            {
                var b = tmp.textBounds;
                if (b.size.x > 0f && b.size.y > 0f)
                {
                    var tr = tmp.rectTransform;
                    Acc(tr.TransformPoint(new Vector3(b.min.x, b.min.y, 0))); Acc(tr.TransformPoint(new Vector3(b.max.x, b.max.y, 0)));
                    Acc(tr.TransformPoint(new Vector3(b.min.x, b.max.y, 0))); Acc(tr.TransformPoint(new Vector3(b.max.x, b.min.y, 0)));
                }
            }
            rect = Rect.MinMaxRect(xmin * kx, ymin * ky, xmax * kx, ymax * ky);
            return rect.width > 0.5f && rect.height > 0.5f;
        }

        /// <summary>
        /// Grow a cut-out so that any other HUD element overlapping it is taken whole (stamina bar next to the
        /// inventory, bits next to the interaction hint) instead of being sliced between the hand and the HUD.
        /// </summary>
        private static RectInt ExpandWithOverlaps(Segment s, RectInt r, Camera cam, int W, int H)
        {
            float kx = (float)W / Mathf.Max(1, cam.pixelWidth), ky = (float)H / Mathf.Max(1, cam.pixelHeight);
            var own = new HashSet<Graphic>(s.Graphics);
            var others = new HashSet<Graphic>();
            foreach (var o in _segments) if (o != s) others.UnionWith(o.Graphics);
            Rect cur = new Rect(r.x, r.y, r.width, r.height);
            const float near = 8f; // also take elements that almost touch (outlines, shadows)
            for (int pass = 0; pass < 3; pass++)
            {
                bool grew = false;
                foreach (var g in _allGraphics)
                {
                    if (g == null || !g.isActiveAndEnabled || own.Contains(g) || FxGraphics.Contains(g)) continue;
                    // invisible elements are taken too: they fade in later (item-name tooltip next to the door hint)
                    if (others.Contains(g)) continue;
                    if (!GraphicRect(g, cam, kx, ky, out Rect gr)) continue;
                    if (gr.width > W * 0.35f || gr.height > H * 0.35f) continue; // big backdrops - not part of a piece
                    var grown = Rect.MinMaxRect(gr.xMin - near, gr.yMin - near, gr.xMax + near, gr.yMax + near);
                    if (!grown.Overlaps(cur)) continue;
                    if (cur.xMin <= gr.xMin - 6 && cur.yMin <= gr.yMin - 6 && cur.xMax >= gr.xMax + 6 && cur.yMax >= gr.yMax + 6) continue;
                    cur = Rect.MinMaxRect(Mathf.Min(cur.xMin, gr.xMin - 10), Mathf.Min(cur.yMin, gr.yMin - 10), Mathf.Max(cur.xMax, gr.xMax + 10), Mathf.Max(cur.yMax, gr.yMax + 10));
                    grew = true;
                }
                if (!grew) break;
            }
            int x0 = Mathf.Clamp(Mathf.FloorToInt(cur.xMin), 0, W - 1), y0 = Mathf.Clamp(Mathf.FloorToInt(cur.yMin), 0, H - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(cur.xMax), x0 + 1, W), y1 = Mathf.Clamp(Mathf.CeilToInt(cur.yMax), y0 + 1, H);
            if ((x1 - x0) > W * 0.6f || (y1 - y0) > H * 0.6f) return r; // grew too much - keep the original cut
            return new RectInt(x0, y0, x1 - x0, y1 - y0);
        }

        /// <summary>Screen rectangle of all visible graphics under t (UI-texture pixels), skipping noise overlays.</summary>
        private static bool SubtreeRect(Transform t, Camera cam, float kx, float ky, out Rect rect)
        {
            // hidden elements count too (the stamina bar is hidden until you run, but must not end up in the group)
            rect = default; bool any = false;
            foreach (var g in t.GetComponentsInChildren<Graphic>(true))
            {
                if (g == null || FxGraphics.Contains(g) || g.name.IndexOf("Noise", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (!GraphicRect(g, cam, kx, ky, out Rect gr)) continue;
                rect = any ? Rect.MinMaxRect(Mathf.Min(rect.xMin, gr.xMin), Mathf.Min(rect.yMin, gr.yMin), Mathf.Max(rect.xMax, gr.xMax), Mathf.Max(rect.yMax, gr.yMax)) : gr;
                any = true;
            }
            return any;
        }

        /// <summary>Each root is taken with the largest parent group that is still small on the screen (its icon and
        /// frame), never the whole HUD; noise overlays are left out.</summary>
        private static void BuildRootGraphics(Segment s, Camera cam, int W, int H)
        {
            s.Built = true; s.Graphics.Clear();
            float kx = (float)W / Mathf.Max(1, cam.pixelWidth), ky = (float)H / Mathf.Max(1, cam.pixelHeight);
            var sb = new StringBuilder($"[MimesisVR] HUD piece '{s.Name}' groups:");
            foreach (var root in s.RootTs)
            {
                if (root == null) continue;
                Transform best = root;
                for (var p = root.parent; p != null && p != s.Panel; p = p.parent)
                {
                    if (!SubtreeRect(p, cam, kx, ky, out Rect pr)) continue;
                    if (pr.width > W * 0.2f || pr.height > H * 0.15f) break;
                    best = p;
                }
                foreach (var g in best.GetComponentsInChildren<Graphic>(true))
                    if (g != null && g.name.IndexOf("Noise", StringComparison.OrdinalIgnoreCase) < 0 && !s.Graphics.Contains(g)
                        && (_stamina == null || !g.transform.IsChildOf(_stamina))) s.Graphics.Add(g);
                sb.Append($" {root.name} -> {best.name}");
            }
            if (_statusLogged++ < 3) VRModCore.Log(sb.ToString());
        }
        private static int _statusLogged;

        /// <summary>Top / bottom of the slot row and top of the stamina bar (even while it is hidden), UI pixels.</summary>
        private static void MeasureWrist(Camera cam, int W, int H)
        {
            _slotsValid = Wrist.SlotRects.Count > 0;
            if (_slotsValid)
            {
                _slotsTop = float.MinValue; _slotsBottom = float.MaxValue;
                foreach (var sr in Wrist.SlotRects) { _slotsTop = Mathf.Max(_slotsTop, sr.yMax); _slotsBottom = Mathf.Min(_slotsBottom, sr.yMin); }
            }
            _staminaTop = float.MinValue;
            if (_stamina is RectTransform srt)
            {
                float ky = (float)H / Mathf.Max(1, cam.pixelHeight);
                srt.GetWorldCorners(_corners);
                for (int i = 0; i < 4; i++) _staminaTop = Mathf.Max(_staminaTop, cam.WorldToScreenPoint(_corners[i]).y * ky);
            }
        }

        private static UIPrefab_Inventory _uiInventory;
        private static FieldInfo _uiSlotsField;

        /// <summary>Slot boxes straight from the game's inventory UI: UI slot list index == game slot index.</summary>
        private static void UpdateSlotRects(Camera cam, int W, int H)
        {
            Wrist.SlotRects.Clear();
            Wrist.SlotGameIndex.Clear();
            var actor = MimesisInput.LocalActor;
            if (_uiInventory == null || actor == null) return;
            _uiSlotsField ??= typeof(UIPrefab_Inventory).GetField("inventorySlots", BindingFlags.Instance | BindingFlags.NonPublic);
            if (!(_uiSlotsField?.GetValue(_uiInventory) is List<UIPrefab_Inventory.Slot> slots)) return;
            List<int> visible;
            try { visible = actor.GetVisibleInventorySlotIndices(); } catch { return; }
            if (visible == null) return;
            float kx = (float)W / Mathf.Max(1, cam.pixelWidth), ky = (float)H / Mathf.Max(1, cam.pixelHeight);
            bool colorTaken = false;
            foreach (int idx in visible)
            {
                if (idx < 0 || idx >= slots.Count || slots[idx] == null || slots[idx].root == null) continue;
                var rt = slots[idx].root.transform as RectTransform;
                if (rt == null || !rt.gameObject.activeInHierarchy) continue;
                if (!colorTaken && slots[idx].frame != null)
                {
                    var fi = slots[idx].frame.GetComponent<UnityEngine.UI.Graphic>() ?? slots[idx].frame.GetComponentInChildren<UnityEngine.UI.Graphic>(true);
                    if (fi != null) { var c = fi.color; Wrist.HighlightColor = new Color(c.r * 0.55f, c.g * 0.55f, c.b * 0.55f, 0.45f); colorTaken = true; }
                }
                rt.GetWorldCorners(_corners);
                float xmin = float.MaxValue, ymin = float.MaxValue, xmax = float.MinValue, ymax = float.MinValue;
                for (int i = 0; i < 4; i++)
                {
                    Vector3 p = cam.WorldToScreenPoint(_corners[i]);
                    xmin = Mathf.Min(xmin, p.x); ymin = Mathf.Min(ymin, p.y);
                    xmax = Mathf.Max(xmax, p.x); ymax = Mathf.Max(ymax, p.y);
                }
                if (xmax - xmin < 4 || ymax - ymin < 4) continue;
                Wrist.SlotRects.Add(Rect.MinMaxRect(xmin * kx, ymin * ky, xmax * kx, ymax * ky));
                Wrist.SlotGameIndex.Add(idx);
            }
        }

        /// <summary>Slot under a point given in wrist-quad UV (u right, v up, 0..1), or -1.</summary>
        public static int SlotAtUv(float u, float v)
        {
            if (MimesisSettingsUI.WristHud.Active) return MimesisSettingsUI.WristHud.SlotAtUv(u, v);
            var r = Wrist.ScreenRect;
            float sx = r.x + u * r.width, sy = r.y + v * r.height;
            for (int i = 0; i < Wrist.SlotRects.Count; i++) if (Wrist.SlotRects[i].Contains(new Vector2(sx, sy))) return i;
            return -1;
        }

        private static void DrawSlotHighlight(RectInt r)
        {
            int i = Wrist.HoverSlot;
            if (i < 0 || i >= Wrist.SlotRects.Count || Wrist.Tex == null) return;
            if (_hlMat == null)
            {
                var sh = Shader.Find("Hidden/Internal-Colored");
                if (sh == null) return;
                _hlMat = new Material(sh);
                _hlMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                _hlMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                _hlMat.SetInt("_ZTest", (int)CompareFunction.Always);
                _hlMat.SetInt("_ZWrite", 0);
                _hlMat.SetInt("_Cull", (int)CullMode.Off);
            }
            Rect sr = Wrist.SlotRects[i];
            // segment texture rows are top-origin: row = (r.y + r.height) - y
            float x0 = sr.xMin - r.x, x1 = sr.xMax - r.x;
            float y0 = (r.y + r.height) - sr.yMax, y1 = (r.y + r.height) - sr.yMin;
            var prev = RenderTexture.active;
            RenderTexture.active = Wrist.Tex;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, Wrist.Tex.width, 0, Wrist.Tex.height);
            _hlMat.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(Wrist.HighlightColor);
            GL.Vertex3(x0, y0, 0); GL.Vertex3(x0, y1, 0); GL.Vertex3(x1, y1, 0); GL.Vertex3(x1, y0, 0);
            GL.End();
            GL.PopMatrix();
            RenderTexture.active = prev;
        }

        private static Material _hlMat;

        private static bool TryGetScreenRect(Segment s, Camera cam, int W, int H, out RectInt rect)
        {
            rect = default;
            float kx = (float)W / Mathf.Max(1, cam.pixelWidth), ky = (float)H / Mathf.Max(1, cam.pixelHeight);
            Rect cur = default; bool any = false;
            foreach (var g in s.Graphics)
            {
                if (g == null || !g.isActiveAndEnabled) continue;
                if (g.color.a < 0.01f || g.canvasRenderer.GetInheritedAlpha() < 0.01f) continue;
                if (FxGraphics.Contains(g)) continue;
                if (!GraphicRect(g, cam, kx, ky, out Rect gr)) continue;
                cur = any ? Rect.MinMaxRect(Mathf.Min(cur.xMin, gr.xMin), Mathf.Min(cur.yMin, gr.yMin), Mathf.Max(cur.xMax, gr.xMax), Mathf.Max(cur.yMax, gr.yMax)) : gr;
                any = true;
            }
            if (!any) return false;
            const int pad = 12;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(cur.xMin) - pad, 0, W - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(cur.yMin) - pad, 0, H - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(cur.xMax) + pad, 1, W);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(cur.yMax) + pad, 1, H);
            if (x1 - x0 < 8 || y1 - y0 < 8) return false;
            if ((x1 - x0) > W * 0.95f && (y1 - y0) > H * 0.95f) return false; // whole screen - not a separable piece
            rect = new RectInt(x0, y0, x1 - x0, y1 - y0);
            return true;
        }

        private static readonly HashSet<string> _segLogged = new();

        /// <summary>Once per piece size: which elements made up the cut-out (to track down sliced bits).</summary>
        private static void LogPiece(Segment s, RectInt r, Camera cam, int W, int H)
        {
            string key = s.Name + r.width / 16 + "x" + r.height / 16;
            if (_segLogged.Count > 40 || !_segLogged.Add(key)) return;
            float kx = (float)W / Mathf.Max(1, cam.pixelWidth), ky = (float)H / Mathf.Max(1, cam.pixelHeight);
            var sb = new StringBuilder($"[MimesisVR] HUD piece '{s.Name}' cut {r.x},{r.y} {r.width}x{r.height}; elements touching it:\n");
            var rr = new Rect(r.x - 30, r.y - 30, r.width + 60, r.height + 60);
            int n = 0;
            foreach (var g in _allGraphics)
            {
                if (g == null || !g.isActiveAndEnabled) continue;
                if (!GraphicRect(g, cam, kx, ky, out Rect gr) || !gr.Overlaps(rr)) continue;
                float a = g.color.a * g.canvasRenderer.GetInheritedAlpha();
                bool inside = gr.xMin >= r.x && gr.yMin >= r.y && gr.xMax <= r.x + r.width && gr.yMax <= r.y + r.height;
                sb.Append($"  {(inside ? "in " : "OUT")} {g.transform.parent?.name}/{g.name} [{g.GetType().Name}] {gr.xMin:0},{gr.yMin:0}-{gr.xMax:0},{gr.yMax:0} a={a:F2}{(FxGraphics.Contains(g) ? " fx" : "")}\n");
                if (++n > 60) break;
            }
            VRModCore.Log(sb.ToString());
        }

        private static void EnsureTex(Segment s, int w, int h, RenderTexture like)
        {
            int tw = Mathf.Max(64, Mathf.CeilToInt(w / 64f) * 64), th = Mathf.Max(64, Mathf.CeilToInt(h / 64f) * 64);
            if (s.Tex != null && s.Tex.width >= w && s.Tex.height >= h && s.Tex.width <= tw * 2 && s.Tex.height <= th * 2) return;
            if (s.Tex != null) { s.Tex.Release(); UnityEngine.Object.Destroy(s.Tex); }
            s.Tex = new RenderTexture(tw, th, 0, like.graphicsFormat);
            s.Tex.Create();
            VRModCore.Log($"[MimesisVR] HUD segment '{s.Name}' texture {tw}x{th}");
        }

        private static void ClearRect(RenderTexture rt, int x, int y, int w, int h)
        {
            if (_clearMat == null)
            {
                var sh = Shader.Find("Hidden/Internal-Colored");
                if (sh == null) return;
                _clearMat = new Material(sh);
                _clearMat.SetInt("_SrcBlend", (int)BlendMode.One);
                _clearMat.SetInt("_DstBlend", (int)BlendMode.Zero);
                _clearMat.SetInt("_ZTest", (int)CompareFunction.Always);
                _clearMat.SetInt("_ZWrite", 0);
                _clearMat.SetInt("_Cull", (int)CullMode.Off);
            }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, rt.width, 0, rt.height);
            _clearMat.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(new Color(0, 0, 0, 0));
            GL.Vertex3(x, y, 0); GL.Vertex3(x, y + h, 0); GL.Vertex3(x + w, y + h, 0); GL.Vertex3(x + w, y, 0);
            GL.End();
            GL.PopMatrix();
            RenderTexture.active = prev;
        }

        /// <summary>Place the pieces (rig-local poses). metersPerPixelAt1m = HUD width / texture width / HUD distance.</summary>
        public static void Place(Transform rig, float metersPerPixelAt1m)
        {
            var c = MimesisInput.Controllers;
            foreach (var s in _segments) s.DrawTex = null;
            if (Wrist.Active)
            {
                if (c != null && c.Ready && c.LeftPoseValid)
                {
                    // Over the back of the left hand / wrist, readable when you look at your hand
                    // Constant pixel scale: "inventory size" is the width of the usual inventory (about 31% of the
                    // UI width); a bag makes the panel longer instead of shrinking the slots
                    float mpp = WristMpp;
                    float width = Wrist.W * mpp;
                    float hgt = Wrist.H * mpp;
                    LeftPanelPose(c, hgt, out Wrist.LocalPos, out Wrist.LocalRot, out _);
                    Wrist.Size = new Vector2(width, hgt);
                }
                else Wrist.Active = false;
            }
            PlaceStatus();
            MimesisSettingsUI.WristHud.Place(c, rig); // own panel over the cut-out (also hides the status piece)
            if (TipsRB.Active) TipsRB.Active = false; // cut out of the screen, not shown anywhere
            if (TipsTram.Active) TipsTram.Active = false;
            if (Tips.Active)
            {
                if (TipsPlace == 2 || c == null || !c.RightPoseValid) Tips.Active = false;
                else
                {
                    float mpp = WristMpp * Mathf.Max(0.1f, TipsScale);
                    Tips.Size = new Vector2(Tips.W * mpp, Tips.H * mpp);
                    Vector3 fwd = c.RightPos - MimesisInput.HeadLocalPos; fwd.y = 0f;
                    fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
                    Vector3 right = Vector3.Cross(Vector3.up, fwd);
                    Vector3 p = c.RightPos + Vector3.up * (0.09f + TipsY + Tips.Size.y * 0.5f) + right * TipsX + fwd * TipsZ;
                    Vector3 toEye = p - MimesisInput.HeadLocalPos;
                    Quaternion r = toEye.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(toEye.normalized, Vector3.up) : Quaternion.identity;
                    if (TipsTilt != 0f) r = r * Quaternion.Euler(TipsTilt, 0f, 0f);
                    Tips.LocalPos = p; Tips.LocalRot = r;
                }
            }
            if (Pointer.Active)
            {
                if (MimesisInput.AimValid && rig != null)
                {
                    Vector3 head = MimesisInput.HeadLocalPos;
                    Vector3 hit = rig.InverseTransformPoint(MimesisVrExtras.PointerEndWorld);
                    Vector3 toHit = hit - head;
                    float d = Mathf.Max(0.3f, toHit.magnitude);
                    Vector3 pos = head + toHit.normalized * (d - 0.03f);
                    Pointer.LocalPos = pos;
                    Pointer.LocalRot = Quaternion.LookRotation(toHit.normalized, Vector3.up);
                    float mpp = metersPerPixelAt1m * d * PointerScale;
                    Pointer.Size = new Vector2(Pointer.W * mpp, Pointer.H * mpp);
                }
                else Pointer.Active = false;
            }
        }

        private static float WristMpp => WristWidth / Mathf.Max(64f, 0.31f * _outW);

        /// <summary>Health + contamination above / below the slot row (the stamina bar keeps its place even while
        /// hidden), centred on the inventory, in the inventory's scale.</summary>
        private static void PlaceStatus()
        {
            if (!Status.Active) return;
            if (!Wrist.Active || Wrist.W <= 0) { Status.Active = false; return; }
            float mpp = WristMpp;
            float mppS = mpp * Mathf.Max(0.1f, StatusScale);
            Status.Size = new Vector2(Status.W * mppS, Status.H * mppS);
            var r = Wrist.ScreenRect;
            float cy = r.y + r.height * 0.5f;
            float top = _slotsValid ? Mathf.Max(_slotsTop, _staminaTop) : r.y + r.height;
            float bottom = _slotsValid ? _slotsBottom : r.y;
            const float gap = 0.008f;
            float y = StatusPlace == 1
                ? (bottom - cy) * mpp - gap - Status.Size.y * 0.5f
                : (top - cy) * mpp + gap + Status.Size.y * 0.5f;
            Vector3 local = new Vector3(StatusX, y + StatusY, -0.002f);
            Status.LocalPos = Wrist.LocalPos + Wrist.LocalRot * local;
            Status.LocalRot = Wrist.LocalRot;
        }

        public static IEnumerable<Segment> All => _segments;

        private static void DumpRects()
        {
            try
            {
                var sb = new StringBuilder("[MimesisVR] HUD panels (children with active graphics):\n");
                foreach (var p in _hudPanels)
                {
                    if (p == null) continue;
                    sb.Append("  ").Append(p.name).Append(p.gameObject.activeInHierarchy ? "" : " (inactive)").Append(": ");
                    int n = 0;
                    foreach (Transform ch in p)
                    {
                        sb.Append(ch.name).Append(ch.gameObject.activeInHierarchy ? "" : "(off)").Append(", ");
                        if (++n > 40) { sb.Append("..."); break; }
                    }
                    sb.Append('\n');
                }
                VRModCore.Log(sb.ToString());
            }
            catch { }
        }
    }
}
