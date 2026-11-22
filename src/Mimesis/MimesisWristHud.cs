using HarmonyLib;
using TMPro;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Mimic;
using UnityVRMod.Core;

namespace UnityVRMod.Mimesis
{
    internal static partial class MimesisSettingsUI
    {
        /// <summary>
        /// MIMESIS VR: the mod's own wrist panel instead of the inventory piece cut out of the game screen.
        ///  - Health and contamination: the game's own elements (pulse graph with its colours, contamination gauge)
        ///    copied every frame from the game's interface picture into a fixed place of the panel (above / below /
        ///    left / right of the slots).
        ///  - Stamina: a bar with the game's own picture and colour, shown when the game shows its bar (space kept).
        ///  - Slots in the game's order and proportions (bag slots smaller) with the game's item pictures and the same
        ///    caption the game puts in its slot (charge, ammo, durability - only where the game shows it).
        /// On the forearm the bag slots go to the left of the main ones. Same touch / pointer handling of the slots as
        /// the cut-out piece (SlotAtUv maps back to the game's slot order).
        /// </summary>
        internal static class WristHud
        {
            public static bool On = true;
            public static bool LookToShow = true;
            public static float IdleAlpha = 0.25f;
            public static bool OnForearm = false;
            /// <summary>Forearm mount: turn in the panel plane (+ = counterclockwise), tilt, shifts (own set, the
            /// controller mount keeps the "hand inventory" settings).</summary>
            public static float FaRoll = 90f, FaTilt, FaX, FaY, FaZ;

            public static bool Active { get; private set; }
            public static RenderTexture Tex;
            public static int PxW, PxH;

            private const int Pad = 10, Gap = 8, SlotPx = 96, StamH = 8, BarGap = 6, StatusGap = 6, MaxW = 2048, MaxH = 1024;
            private const float BaseWidthPx = 560f; // "inventory size" = the width of about this many pixels
            private static Camera _cam;
            private static Canvas _canvas;
            private static RenderTexture _raw, _base, _statusTmp;
            private static int _rtW, _rtH;
            private static RectTransform _root;
            private static string _layout;
            private static float _alpha = 1f, _nextRender;
            private static int _lastSig;
            private static bool _styled;
            private static UIPrefab_Inventory _inv;
            private static FieldInfo _invSlots;
            private static RectInt _statusArea;       // panel pixels, top-left origin; width 0 = no status area
            private static Vector2Int _statusSize;    // size of the game's status piece (UI pixels) the layout is made for
            private static bool _statusDrawn;
            private static Material _fadeMat;
            private static int _logged;

            private sealed class SlotView
            {
                public int Game; // game slot index
                public int Src;  // index into Wrist.SlotRects (touch / pointer code works with it)
                public Rect Px;  // top-left pixel rect inside the panel
                public Image Bg, Icon; public RectTransform Sel; public Label Caption;
            }
            private static readonly List<SlotView> _slots = new();

            /// <summary>Nothing to patch any more: health / contamination / stamina are taken from the game's own
            /// interface (its stamina call was too short to patch - the runtime inlines it).</summary>
            public static void Install(HarmonyLib.Harmony h) { }

            /// <summary>Slot under a point in panel UV (u right, v up), or -1 (index into Wrist.SlotRects order).</summary>
            public static int SlotAtUv(float u, float v)
            {
                float x = u * PxW, y = (1f - v) * PxH;
                foreach (var s in _slots) if (s.Px.Contains(new Vector2(x, y))) return s.Src;
                return -1;
            }

            /// <summary>Called by MimesisHudSegments.Place after the cut-out pieces were placed.</summary>
            public static void Place(UnityVRMod.Features.VRVisualization.OpenXR.XrControllerInput c, Transform rig)
            {
                Active = false;
                var w = MimesisHudSegments.Wrist;
                if (!On) return;
                // health / contamination are shown only inside the panel (and hidden with it, e.g. after death)
                if (MimesisHudSegments.StatusPlace != 2) MimesisHudSegments.Status.Active = false;
                if (!w.Active || c == null) { _hadPanel = false; return; }
                try
                {
                    if (!Update(w)) { w.Active = false; return; } // never fall back to the cut-out piece
                    float mpp = MimesisHudSegments.WristWidth / BaseWidthPx;
                    Vector2 size = new Vector2(PxW * mpp, PxH * mpp);
                    NaturalSize = size;
                    Vector3 pos = default; Quaternion rot = Quaternion.identity;
                    _onWatch = OnForearm && MimesisWatches.LeftScreen(rig, out pos, out rot); // on the left watch (build 40)
                    if (_onWatch) size *= MimesisWatches.ScreenScale(MimesisWatches.L); // as set, or fitted to the watch (41)
                    else MimesisHudSegments.LeftPanelPose(c, size.y, out pos, out rot, out _);
                    w.LocalPos = pos; w.LocalRot = rot; w.Size = size;
                    w.DrawTex = Tex; w.DrawW = PxW; w.DrawH = PxH;
                    Active = true;
                    _hadPanel = true;
                    Fade(w);
                    Compose();
                }
                catch (Exception ex) { VRModCore.LogError("[MimesisVR] Wrist panel failed - back to the cut-out:", ex); On = false; Active = false; }
            }

            private static bool _onWatch, _hadPanel;
            public static float Alpha => _alpha;
            private static Rect _gearCellPx;
            private static Sprite _round;

            /// <summary>Build 42: with a menu open the panel stays on the watch with its last picture (no touching).</summary>
            public static void PlaceFrozen(Transform rig)
            {
                Active = false;
                var w = MimesisHudSegments.Wrist;
                var c = MimesisInput.Controllers;
                if (!On || Tex == null || !_hadPanel || c == null || !c.Ready || NaturalSize.x <= 0f || w.Tex == null) return;
                Vector2 size = NaturalSize;
                Vector3 pos = default; Quaternion rot = Quaternion.identity;
                _onWatch = OnForearm && MimesisWatches.LeftScreen(rig, out pos, out rot);
                if (_onWatch) size *= MimesisWatches.ScreenScale(MimesisWatches.L);
                else { if (!c.LeftPoseValid) return; MimesisHudSegments.LeftPanelPose(c, size.y, out pos, out rot, out _); }
                w.LocalPos = pos; w.LocalRot = rot; w.Size = size;
                w.DrawTex = Tex; w.DrawW = PxW; w.DrawH = PxH;
                w.Active = true; Active = true;
                Fade(w);
                MimesisStream.WristAge = 0f; // menus: the last picture stays on the monitor too
            }

            /// <summary>Build 42: the gear in its own cell at the start of the panel (GearPlace 4).</summary>
            public static bool GearCell(out Vector3 pos, out Quaternion rot, out float size, out bool byLook)
            {
                var w = MimesisHudSegments.Wrist;
                pos = default; rot = Quaternion.identity; size = 0.03f; byLook = false;
                if (!Active || _gearCellPx.width <= 0f || PxW <= 0 || PxH <= 0) return false;
                Vector2 cpx = _gearCellPx.center;
                rot = w.LocalRot;
                pos = w.LocalPos + w.LocalRot * new Vector3((cpx.x / PxW - 0.5f) * w.Size.x, (0.5f - cpx.y / PxH) * w.Size.y, -0.0008f);
                size = _gearCellPx.width / PxW * w.Size.x * 0.9f;
                return true;
            }

            /// <summary>Rounded backing like the right screen (9-slice sprite).</summary>
            private static Sprite Round()
            {
                if (_round != null) return _round;
                const int N = 64, R = 30;
                var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                var px = new Color32[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float dx = Mathf.Max(0f, Mathf.Max(R - (x + 0.5f), (x + 0.5f) - (N - R)));
                        float dy = Mathf.Max(0f, Mathf.Max(R - (y + 0.5f), (y + 0.5f) - (N - R)));
                        float a = Mathf.Clamp01(R - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                        px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                t.SetPixels32(px); t.Apply();
                _round = Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(R, R, R, R));
                return _round;
            }
            /// <summary>Panel size at "Screen: size" x1 (rig metres), for fitting it to the watch.</summary>
            public static Vector2 NaturalSize;

            private static void Fade(MimesisHudSegments.Segment w)
            {
                float target = 1f;
                if (LookToShow)
                {
                    Vector3 head = MimesisInput.HeadLocalPos, f = MimesisInput.HeadLocalRot * Vector3.forward;
                    Vector3 to = (w.LocalPos - head).normalized;
                    float d = Vector3.Dot(f, to); // 1 = looking straight at the panel
                    target = Mathf.Lerp(Mathf.Clamp01(IdleAlpha), 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.82f, 0.94f, d)));
                }
                // on the watch: hidden while it faces away (a headset layer would show through the arm)
                if (_onWatch) target *= MimesisWatches.Facing(w.LocalPos, w.LocalRot);
                _alpha = Mathf.MoveTowards(_alpha, target, Time.unscaledDeltaTime * 4f);
            }

            private static bool StatusAvailable(out MimesisHudSegments.Segment st)
            {
                st = MimesisHudSegments.Status;
                return MimesisHudSegments.StatusPlace != 2 && st.Tex != null && st.W > 0 && st.H > 0
                    && st.CutTime >= 0f && Time.unscaledTime - st.CutTime < 1f;
            }

            private static bool Update(MimesisHudSegments.Segment w)
            {
                var actor = MimesisInput.LocalActor;
                if (actor == null || w.SlotGameIndex.Count == 0) return false;
                EnsureCanvas();
                // layout from the game's own slot row: order and relative sizes (bag slots are smaller)
                float maxW = 1f;
                foreach (var r in w.SlotRects) maxW = Mathf.Max(maxW, r.width);
                Vector2Int stSize = Vector2Int.zero; // build 40: health / contamination / stamina are on the right watch
                float k = BaseWidthPx / MimesisHudSegments.PiecePxPerWristWidth * Mathf.Max(0.1f, MimesisHudSegments.StatusScale);
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < w.SlotGameIndex.Count && i < w.SlotRects.Count; i++)
                    sb.Append(w.SlotGameIndex[i]).Append(':').Append(Mathf.RoundToInt(w.SlotRects[i].width / maxW * 8f)).Append(',');
                sb.Append(OnForearm ? "F" : "C").Append(MimesisHudSegments.StatusPlace).Append(' ').Append(stSize.x).Append('x').Append(stSize.y)
                  .Append(' ').Append(k.ToString("F3")).Append(' ').Append(MimesisHudSegments.StatusX.ToString("F2")).Append(MimesisHudSegments.StatusY.ToString("F2"))
                  .Append(' ').Append(MimesisHudSegments.WristWidth.ToString("F2"))
                  .Append(MimesisWatches.BagRow ? " B2" : " B1").Append(' ').Append(MimesisWatches.GearPlace).Append(' ').Append(MimesisWatches.L.BgAlpha.ToString("F2"));
                string layout = sb.ToString();
                if (layout != _layout) { _layout = layout; Build(w, maxW, stSize, k); }

                // values
                int sel = -1;
                try { sel = actor.GetSelectedInventorySlotIndex(); } catch { }
                var gameSlots = GameSlots();
                int sig = 17;
                foreach (var s in _slots)
                {
                    var gs = gameSlots != null && s.Game >= 0 && s.Game < gameSlots.Count ? gameSlots[s.Game] : null;
                    var img = gs?.image;
                    bool shown = img != null && img.enabled && gs.itemGroup != null && gs.itemGroup.activeSelf;
                    Sprite sp = shown ? img.sprite : null;
                    s.Icon.sprite = sp; s.Icon.enabled = sp != null;
                    bool selected = s.Game == sel, hover = w.HoverSlot == s.Src;
                    s.Sel.gameObject.SetActive(selected);
                    s.Bg.color = hover ? new Color(1f, 1f, 1f, 0.22f) : selected ? new Color(1f, 1f, 1f, 0.12f) : new Color(1f, 1f, 1f, 0.05f);
                    // the same caption as the game's own slot (charge / ammo / durability only where the game shows it)
                    string cap = "";
                    var gt = sp != null ? gs.stackCount : null;
                    if (gt != null && gt.gameObject.activeInHierarchy) cap = gt.text ?? "";
                    if (s.Caption != null)
                    {
                        if (s.Caption.Tmp != null && gt != null)
                        {
                            if (gt.font != null && s.Caption.Tmp.font != gt.font) s.Caption.Tmp.font = gt.font;
                            if (gt.spriteAsset != null && s.Caption.Tmp.spriteAsset != gt.spriteAsset) s.Caption.Tmp.spriteAsset = gt.spriteAsset;
                            s.Caption.Tmp.color = gt.color;
                        }
                        s.Caption.Text = cap;
                    }
                    sig = sig * 31 + (sp != null ? sp.GetInstanceID() : 0) + (selected ? 7 : 0) + (hover ? 13 : 0) + cap.GetHashCode();
                }
                // render the panel when something changed (and at least 4 times a second for safety)
                if (sig != _lastSig || Time.unscaledTime >= _nextRender)
                {
                    _lastSig = sig; _nextRender = Time.unscaledTime + 0.25f;
                    _cam.Render();
                    Graphics.Blit(_raw, _base, new Vector2(1f, -1f), new Vector2(0f, 1f)); // -> OpenXR orientation
                }
                return true;
            }

            /// <summary>Every frame: panel picture + the game's live health / contamination piece + fade.</summary>
            private static void Compose()
            {
                Graphics.CopyTexture(_base, Tex);
                _statusDrawn = false;
                if (_statusArea.width > 0 && StatusAvailable(out var st))
                {
                    float k = BaseWidthPx / MimesisHudSegments.PiecePxPerWristWidth * Mathf.Max(0.1f, MimesisHudSegments.StatusScale);
                    int aw = Mathf.Clamp(Mathf.RoundToInt(st.W * k), 1, _statusArea.width), ah = Mathf.Clamp(Mathf.RoundToInt(st.H * k), 1, _statusArea.height);
                    if (_statusTmp == null || _statusTmp.width != aw || _statusTmp.height != ah)
                    {
                        if (_statusTmp != null) { _statusTmp.Release(); UnityEngine.Object.Destroy(_statusTmp); }
                        _statusTmp = new RenderTexture(aw, ah, 0, Tex.graphicsFormat) { filterMode = FilterMode.Bilinear };
                        _statusTmp.Create();
                    }
                    // both textures are top-row-first (OpenXR orientation): the piece sits at the top left of st.Tex
                    Graphics.Blit(st.Tex, _statusTmp, new Vector2((float)st.W / st.Tex.width, (float)st.H / st.Tex.height), Vector2.zero);
                    int x = _statusArea.x + (_statusArea.width - aw) / 2, y = _statusArea.y + (_statusArea.height - ah) / 2;
                    int cw = Mathf.Min(aw, Tex.width - x), ch = Mathf.Min(ah, Tex.height - y);
                    if (cw > 0 && ch > 0 && x >= 0 && y >= 0) { Graphics.CopyTexture(_statusTmp, 0, 0, 0, 0, cw, ch, Tex, 0, 0, x, y); _statusDrawn = true; }
                }
                if (_logged < 2 && _statusArea.width > 0) { _logged++; VRModCore.Log($"[MimesisVR] Wrist panel: health/contamination from the game {(_statusDrawn ? "drawn" : "not available yet")} (area {_statusArea.width}x{_statusArea.height} px)."); }
                MimesisStream.CopyWrist(Tex, PxW, PxH); // build 45: unfaded copy for the monitor
                if (_alpha < 0.999f) MultiplyAlpha(Tex, _alpha);
            }

            /// <summary>Fade a premultiplied picture: every channel times a.</summary>
            private static void MultiplyAlpha(RenderTexture rt, float a)
            {
                if (_fadeMat == null)
                {
                    var sh = Shader.Find("Hidden/Internal-Colored");
                    if (sh == null) return;
                    _fadeMat = new Material(sh);
                    _fadeMat.SetInt("_SrcBlend", (int)BlendMode.Zero);
                    _fadeMat.SetInt("_DstBlend", (int)BlendMode.SrcAlpha);
                    _fadeMat.SetInt("_ZTest", (int)CompareFunction.Always);
                    _fadeMat.SetInt("_ZWrite", 0);
                    _fadeMat.SetInt("_Cull", (int)CullMode.Off);
                }
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                GL.PushMatrix();
                GL.LoadPixelMatrix(0, rt.width, 0, rt.height);
                _fadeMat.SetPass(0);
                GL.Begin(GL.QUADS);
                GL.Color(new Color(0f, 0f, 0f, Mathf.Clamp01(a)));
                GL.Vertex3(0, 0, 0); GL.Vertex3(0, rt.height, 0); GL.Vertex3(rt.width, rt.height, 0); GL.Vertex3(rt.width, 0, 0);
                GL.End();
                GL.PopMatrix();
                RenderTexture.active = prev;
            }

            /// <summary>The game's own inventory UI slots (pictures and captions are set there for every slot).</summary>
            private static List<UIPrefab_Inventory.Slot> GameSlots()
            {
                try
                {
                    if (_inv == null) _inv = UnityEngine.Object.FindObjectOfType<UIPrefab_Inventory>(true);
                    if (_inv == null) return null;
                    _invSlots ??= typeof(UIPrefab_Inventory).GetField("inventorySlots", BindingFlags.Instance | BindingFlags.NonPublic);
                    return _invSlots?.GetValue(_inv) as List<UIPrefab_Inventory.Slot>;
                }
                catch { return null; }
            }

            private static void Build(MimesisHudSegments.Segment w, float maxW, Vector2Int stSize, float k)
            {
                if (!_styled) { _styled = true; try { SampleStyle(); } catch { } }
                foreach (Transform ch in _root) UnityEngine.Object.Destroy(ch.gameObject);
                _slots.Clear();
                _statusSize = stSize;
                int n = Mathf.Min(w.SlotGameIndex.Count, w.SlotRects.Count);
                // order: as the game; on the forearm the bag slots (game index 6+) go first (left)
                var order = new List<int>(n);
                if (OnForearm)
                {
                    for (int i = 0; i < n; i++) if (w.SlotGameIndex[i] >= 6) order.Add(i);
                    for (int i = 0; i < n; i++) if (w.SlotGameIndex[i] < 6) order.Add(i);
                }
                else for (int i = 0; i < n; i++) order.Add(i);
                var widths = new float[n];
                for (int j = 0; j < n; j++) { int i = order[j]; widths[j] = Mathf.Round(SlotPx * Mathf.Clamp(w.SlotRects[i].width / maxW, 0.4f, 1f)); }
                // build 41: on the forearm the bag slots can go to a second row below (only while there is a bag)
                bool twoRows = OnForearm && MimesisWatches.BagRow;
                var rowOf = new int[n];
                float row0W = 0f, row1W = 0f, row1H = 0f;
                // build 42: the gear in its own cell at the start of the main row
                bool gearCell = MimesisWatches.GearPlace == 4;
                bool gearCorner = MimesisWatches.GearPlace == 5; // build 43: small, top corner at the end of the row
                float gearCellW = gearCell ? Mathf.Round(SlotPx * 0.8f) : gearCorner ? Mathf.Round(SlotPx * 0.42f) : 0f;
                if (gearCell || gearCorner) row0W += gearCellW + Gap;
                for (int j = 0; j < n; j++)
                {
                    int i = order[j];
                    rowOf[j] = twoRows && w.SlotGameIndex[i] >= 6 ? 1 : 0;
                    if (rowOf[j] == 0) row0W += widths[j] + (row0W > 0f ? Gap : 0f);
                    else { row1W += widths[j] + (row1W > 0f ? Gap : 0f); row1H = Mathf.Max(row1H, widths[j]); }
                }
                float rowW = Mathf.Max(row0W, row1W);
                float blockW = rowW + Pad * 2, blockH = Pad + SlotPx + (row1W > 0f ? Gap + row1H : 0f) + Pad; // build 40: no stamina row (right watch)

                // health / contamination area relative to the slot block
                float sw = stSize.x * k, sh = stSize.y * k, sx = 0f, sy = 0f;
                bool hasStatus = sw >= 4f && sh >= 4f;
                if (hasStatus)
                {
                    switch (MimesisHudSegments.StatusPlace)
                    {
                        case 1: sx = (blockW - sw) * 0.5f; sy = blockH + StatusGap; break;          // below
                        case 3: sx = -sw - StatusGap; sy = (blockH - sh) * 0.5f; break;              // left
                        case 4: sx = blockW + StatusGap; sy = (blockH - sh) * 0.5f; break;           // right
                        default: sx = (blockW - sw) * 0.5f; sy = -sh - StatusGap; break;             // above
                    }
                    float mpp = MimesisHudSegments.WristWidth / BaseWidthPx;
                    sx += MimesisHudSegments.StatusX / mpp;
                    sy -= MimesisHudSegments.StatusY / mpp;
                }
                float minX = hasStatus ? Mathf.Min(0f, sx) : 0f, minY = hasStatus ? Mathf.Min(0f, sy) : 0f;
                float maxX = hasStatus ? Mathf.Max(blockW, sx + sw) : blockW, maxY = hasStatus ? Mathf.Max(blockH, sy + sh) : blockH;
                PxW = Mathf.Clamp(Mathf.CeilToInt(maxX - minX), 16, MaxW);
                PxH = Mathf.Clamp(Mathf.CeilToInt(maxY - minY), 16, MaxH);
                float bx = -minX, by = -minY;
                _statusArea = hasStatus ? new RectInt(Mathf.RoundToInt(sx - minX), Mathf.RoundToInt(sy - minY), Mathf.RoundToInt(sw), Mathf.RoundToInt(sh)) : default;
                EnsureTargets(PxW, PxH);

                // backing only under the slot block; the game's health / contamination float as in the game
                var bgImg = Fill(Box(_root, "Bg", bx, by, blockW, blockH), new Color(0.04f, 0.045f, 0.05f, Mathf.Clamp01(MimesisWatches.L.BgAlpha)));
                bgImg.sprite = Round(); bgImg.type = Image.Type.Sliced;
                bgImg.pixelsPerUnitMultiplier = 30f / Mathf.Max(4f, blockH * 0.12f); // corner radius ~12% of the height
                float y = by + Pad;
                float x0 = bx + Pad + (rowW - row0W) * 0.5f, x1 = bx + Pad + (rowW - row1W) * 0.5f;
                _gearCellPx = default;
                if (gearCorner) _gearCellPx = new Rect(x0 + row0W - gearCellW, y, gearCellW, gearCellW);
                if (gearCell)
                {
                    _gearCellPx = new Rect(x0, y + (SlotPx - gearCellW) * 0.5f, gearCellW, gearCellW);
                    x0 += gearCellW + Gap;
                }
                for (int j = 0; j < n; j++)
                {
                    int i = order[j];
                    float swd = widths[j], shd = swd; // square, smaller slots stay smaller
                    float x = rowOf[j] == 0 ? x0 : x1;
                    float syy = rowOf[j] == 0 ? y + (SlotPx - shd) : y + SlotPx + Gap; // main row bottom-aligned like the game
                    var s = new SlotView { Game = w.SlotGameIndex[i], Src = i, Px = new Rect(x, syy, swd, shd) };
                    var rt = Box(_root, "Slot" + i, x, syy, swd, shd);
                    s.Bg = Fill(rt, new Color(1f, 1f, 1f, 0.05f));
                    var iconRt = Box(rt, "Icon", swd * 0.1f, shd * 0.1f, swd * 0.8f, shd * 0.8f);
                    s.Icon = iconRt.gameObject.AddComponent<Image>(); s.Icon.raycastTarget = false; s.Icon.preserveAspect = true;
                    s.Sel = Box(rt, "Sel", 0, 0, swd, shd); Frame(s.Sel, cAccent, 3f);
                    var capRt = Box(rt, "Caption", 4, shd - 30, swd - 8, 28);
                    s.Caption = MakeLabel(capRt, "", Mathf.Max(12f, 22f * swd / SlotPx), cText, Al.Right);
                    if (s.Caption.Tmp != null) s.Caption.Tmp.overflowMode = TextOverflowModes.Overflow;
                    _slots.Add(s);
                    if (rowOf[j] == 0) x0 += swd + Gap; else x1 += swd + Gap;
                }
                _lastSig = 0;
                Canvas.ForceUpdateCanvases();
                VRModCore.Log($"[MimesisVR] Wrist panel layout: {n} slots ({_layout}), {PxW}x{PxH} px, health/contamination area {(_statusArea.width > 0 ? _statusArea.width + "x" + _statusArea.height : "none")}.");
            }

            private static void EnsureTargets(int w, int h)
            {
                int tw = Mathf.Clamp(Mathf.CeilToInt(w / 64f) * 64, 64, MaxW), th = Mathf.Clamp(Mathf.CeilToInt(h / 64f) * 64, 64, MaxH);
                if (_raw != null && _raw.IsCreated() && _rtW == tw && _rtH == th) return;
                foreach (var old in new[] { _raw, _base, Tex }) if (old != null) { old.Release(); UnityEngine.Object.Destroy(old); }
                _rtW = tw; _rtH = th;
                _raw = new RenderTexture(tw, th, 24, UnityEngine.Experimental.Rendering.GraphicsFormat.B8G8R8A8_SRGB); _raw.Create();
                _base = new RenderTexture(tw, th, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.B8G8R8A8_SRGB); _base.Create();
                Tex = new RenderTexture(tw, th, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.B8G8R8A8_SRGB); Tex.Create();
                _cam.targetTexture = _raw;
                if (_root != null) _root.sizeDelta = new Vector2(tw, th);
            }

            private static void EnsureCanvas()
            {
                if (_cam == null)
                {
                    var go = new GameObject("MimesisVR_WristCamera");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    go.transform.position = new Vector3(0f, -21000f, 0f);
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
                if (_raw == null || !_raw.IsCreated()) { _rtW = 0; EnsureTargets(1024, 256); }
                if (_canvas == null)
                {
                    var go = new GameObject("MimesisVR_WristCanvas") { layer = UiLayer };
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    _canvas = go.AddComponent<Canvas>();
                    _canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    _canvas.worldCamera = _cam;
                    _canvas.planeDistance = 1f;
                    _root = Box(go.transform, "Root", 0, 0, _rtW, _rtH);
                    _layout = null;
                }
            }
        }
    }
}
