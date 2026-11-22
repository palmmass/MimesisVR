using UnityEngine.Rendering.Universal;
using UnityVRMod.Core;

namespace UnityVRMod.Mimesis
{
    /// <summary>
    /// MIMESIS VR: renders the game's screen-space UI (hints, crosshair, money, slots) with a dedicated
    /// camera into a transparent texture that is shown as a head-locked quad layer during gameplay.
    /// While a menu is open the canvases go back to overlay mode so the menu panel capture sees them.
    /// </summary>
    internal static class MimesisHud
    {
        public static bool Enabled = true; // Home toggles
        public static bool Active;
        public static RenderTexture Output;
        public static Vector3 LocalPos;
        public static Quaternion LocalRot = Quaternion.identity;
        public static Vector2 Size;
        public static float Distance = 1.1f;
        public static float Scale = 1f;
        public static float MenuBackdropAlpha = 0.72f;
        /// <summary>HUD width in metres; grows with distance so the apparent size stays the same.</summary>
        public static float Width => 1.4f * Scale * Distance / 1.1f;
        public static bool OutputReady;

        // ---- Screen effects (UIScreenEffect_*: rain on the "lens", contamination smudge) ----
        public enum FxMode { Eyes, Off, Hud }
        public static FxMode Fx = FxMode.Eyes;
        /// <summary>Screen effects rendered alone this frame (Unity texture orientation), to be drawn over each eye.</summary>
        public static bool FxReady;
        private static RenderTexture _fxRaw;
        private static readonly List<UnityEngine.UI.Graphic> _fxGraphics = new();
        private static readonly List<UnityEngine.UI.Graphic> _uiGraphics = new();
        private static readonly List<UnityEngine.UI.Graphic> _tmpG = new();
        private static readonly Dictionary<CanvasRenderer, float> _savedAlpha = new();
        private static Material _fxMat;
        private static readonly HashSet<string> _fxLogged = new();
        /// <summary>Build 41: rain drops on the "lens" (UIScreenEffect_Rain). Off by default: in the headset its
        /// additive drop sheet came out as a black sheet with holes, and the rain itself is in the world anyway.</summary>
        public static bool LensRain;
        /// <summary>Build 41: the game's noise overlays on its interface panels (shader ReLU/UINoiseOverlay). Off by
        /// default: in the headset they made dark backings behind tips.</summary>
        public static bool UiNoise;
        private static readonly List<UnityEngine.UI.Graphic> _rainGraphics = new(), _noiseGraphics = new();

        /// <summary>The game just started a screen effect (blood, trap, ...): look for it before this frame's picture,
        /// so it never shows for a moment in the flat interface in front of you.</summary>
        public static void FxSpawned() { _nextScan = 0f; }

        public static void InstallFx(HarmonyLib.Harmony h)
        {
            try
            {
                var post = new HarmonyLib.HarmonyMethod(typeof(MimesisHud), nameof(FxSpawned));
                foreach (var name in new[] { "PlayScreenEffect", "PlayLoopingScreenEffect" })
                {
                    var m = HarmonyLib.AccessTools.Method(typeof(UIManager), name);
                    if (m != null) h.Patch(m, postfix: post);
                    else VRModCore.LogWarning("[MimesisVR] UIManager." + name + " not found - screen effects are found by the periodic scan only.");
                }
            }
            catch (Exception ex) { VRModCore.LogWarning("[MimesisVR] Screen effect hook failed: " + ex.Message); }
        }

        private static void ScanFx()
        {
            _fxGraphics.Clear(); _uiGraphics.Clear(); _rainGraphics.Clear(); _noiseGraphics.Clear();
            MimesisHudSegments.FxGraphics.Clear();
            foreach (var c in _converted)
            {
                if (c == null) continue;
                c.GetComponentsInChildren(true, _tmpG);
                _uiGraphics.AddRange(_tmpG);
                CollectFx(c.transform);
            }
            foreach (var g in _uiGraphics)
            {
                if (g == null) continue;
                var m = g.material;
                var sh = m != null ? m.shader : null;
                if (sh != null && sh.name.IndexOf("UINoiseOverlay", StringComparison.OrdinalIgnoreCase) >= 0 && !MimesisHudSegments.FxGraphics.Contains(g)) _noiseGraphics.Add(g);
            }
        }

        private static void CollectFx(Transform t)
        {
            // build 41: also by the game's screen effect component (not every effect prefab has the name prefix)
            if (t.name.StartsWith("UIScreenEffect_") || t.GetComponent<UIScreenEffectBase>() != null)
            {
                t.GetComponentsInChildren(true, _tmpG);
                bool rain = t.name.IndexOf("Rain", StringComparison.OrdinalIgnoreCase) >= 0;
                foreach (var g in _tmpG) { _fxGraphics.Add(g); MimesisHudSegments.FxGraphics.Add(g); if (rain) _rainGraphics.Add(g); }
                if (_fxLogged.Add(t.name))
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var g in _tmpG)
                    {
                        var m = g != null ? g.material : null;
                        if (m == null) continue;
                        sb.Append(g.name).Append('=').Append(m.name).Append('/').Append(m.shader != null ? m.shader.name : "-");
                        if (m.HasProperty("_SrcBlend")) sb.Append(" blend ").Append(m.GetFloat("_SrcBlend")).Append('/').Append(m.HasProperty("_DstBlend") ? m.GetFloat("_DstBlend") : -1f);
                        sb.Append("; ");
                    }
                    VRModCore.Log($"[MimesisVR] Screen effect '{t.name}' ({_tmpG.Count} graphics) - shown {(rain && !LensRain ? "nowhere (lens rain off)" : Fx.ToString())} in VR. Materials: {sb}");
                }
                return;
            }
            foreach (Transform ch in t) CollectFx(ch);
        }

        private static void SaveAndSetAlpha(List<UnityEngine.UI.Graphic> list, float a)
        {
            foreach (var g in list)
            {
                if (g == null) continue;
                var cr = g.canvasRenderer;
                if (cr == null) continue;
                if (!_savedAlpha.ContainsKey(cr)) _savedAlpha[cr] = cr.GetAlpha();
                cr.SetAlpha(a);
            }
        }

        private static void RestoreAlpha()
        {
            foreach (var kv in _savedAlpha) if (kv.Key != null) kv.Key.SetAlpha(kv.Value);
            _savedAlpha.Clear();
        }

        private static bool AnyFxVisible()
        {
            foreach (var g in _fxGraphics)
            {
                if (!LensRain && _rainGraphics.Contains(g)) continue;
                if (g != null && g.isActiveAndEnabled && g.color.a > 0.01f && g.canvasRenderer.GetInheritedAlpha() > 0.01f) return true;
            }
            return false;
        }

        /// <summary>Draw the screen effects over one eye image as if they sat on the lens (same place in both eyes).</summary>
        public static void CompositeFx(RenderTexture eye, float fovLeft, float fovRight, float fovUp, float fovDown)
        {
            if (!FxReady || _fxRaw == null || eye == null) return;
            try
            {
                if (_fxMat == null) _fxMat = new Material(Shader.Find("UI/Default"));
                var gc = MimesisInput.GameCamera != null ? MimesisInput.GameCamera.GetComponent<Camera>() : null;
                float vfov = gc != null ? gc.fieldOfView : 60f;
                float halfV = Mathf.Tan(vfov * 0.5f * Mathf.Deg2Rad), halfH = halfV * _fxRaw.width / Mathf.Max(1, _fxRaw.height);
                float tl = Mathf.Tan(fovLeft), tr = Mathf.Tan(fovRight), tu = Mathf.Tan(fovUp), td = Mathf.Tan(fovDown);
                float w = eye.width, h = eye.height;
                float x0 = (-halfH - tl) / (tr - tl) * w, x1 = (halfH - tl) / (tr - tl) * w;
                float yTop = (tu - halfV) / (tu - td) * h, yBot = (tu + halfV) / (tu - td) * h;
                var prev = RenderTexture.active;
                RenderTexture.active = eye;
                GL.PushMatrix();
                GL.LoadPixelMatrix(0, w, h, 0);
                Graphics.DrawTexture(new Rect(x0, yTop, x1 - x0, yBot - yTop), _fxRaw, _fxMat);
                GL.PopMatrix();
                RenderTexture.active = prev;
            }
            catch (Exception ex) { VRModCore.LogError("[MimesisVR] Screen effect composite failed, showing them in the HUD:", ex); Fx = FxMode.Hud; FxReady = false; }
        }
        private static RenderTexture _raw;
        public static RenderTexture Raw => _raw;
        private static readonly HashSet<int> _dumped = new();

        private static Camera _uiCam;
        private static readonly List<Canvas> _converted = new();
        private static float _nextScan;
        private static int _uiMask;

        public static void Update(Transform rig)
        {
            if (MimesisSettings.DebugTools && UniverseLib.Input.InputManager.GetKeyDown(KeyCode.Home)) { Enabled = !Enabled; VRModCore.Log($"[MimesisVR] HUD layer: {Enabled}"); }

            if (MimesisSettings.DebugTools && UniverseLib.Input.InputManager.GetKeyDown(KeyCode.Insert)) { MimesisHudSegments.Enabled = !MimesisHudSegments.Enabled; VRModCore.Log($"[MimesisVR] HUD on hands: {MimesisHudSegments.Enabled}"); }
            bool want = Enabled && rig != null && MimesisInput.VrActive;
            if (!want)
            {
                MimesisHudSegments.SetHudHidden(false);
                MimesisHudSegments.HideStamina(false);
                foreach (var sg in MimesisHudSegments.All) sg.Active = false;
                RestoreAll(); Active = false; OutputReady = false; return;
            }

            try
            {
                EnsureCamera();
                EnsureOutput();
                if (Time.unscaledTime >= _nextScan) { ScanAndConvert(); ScanFx(); _nextScan = Time.unscaledTime + 0.5f; }
                if (_converted.Count == 0) { Active = false; OutputReady = false; return; }

                if (MimesisInput.PanelVisible != _lastPanel) { _lastPanel = MimesisInput.PanelVisible; DumpActiveTree(_lastPanel ? "menu open" : "gameplay"); }
                _uiCam.cullingMask = _uiMask;
                // Dark translucent backing for menus; fully transparent for the in-game HUD
                _uiCam.backgroundColor = MimesisInput.PanelVisible ? new Color(0f, 0f, 0f, MenuBackdropAlpha) : new Color(0f, 0f, 0f, 0f);
                // Option: redraw the HUD picture only every N-th frame (positions still follow every frame)
                if (!MimesisInput.PanelVisible && UpdateEvery > 1 && OutputReady && Time.frameCount % UpdateEvery != 0)
                {
                    MimesisHudSegments.Place(rig, Width / Output.width / Distance);
                    Quaternion r0 = MimesisInput.HeadLocalRot;
                    LocalRot = r0;
                    LocalPos = MimesisInput.HeadLocalPos + r0 * new Vector3(0f, 0f, Distance);
                    Size = new Vector2(Width, Width * Output.height / Output.width);
                    Active = true;
                    return;
                }
                if (Time.unscaledTime >= _nextDimScan) { ScanDimOverlays(); _nextDimScan = Time.unscaledTime + 0.25f; }
                ApplyDimCap();
                FxReady = false;
                // screen effects (rain, blood, contamination) never go into the interface picture - also not while a
                // panel is shown (during death the game frees the cursor and the HUD is shown as a panel)
                bool splitFx = Fx != FxMode.Hud && _fxGraphics.Count > 0;
                bool fxVisible = splitFx && Fx == FxMode.Eyes && AnyFxVisible();
                if (splitFx) SaveAndSetAlpha(_fxGraphics, 0f);
                bool hideNoise = !UiNoise && _noiseGraphics.Count > 0;
                if (hideNoise) SaveAndSetAlpha(_noiseGraphics, 0f);
                MimesisHudSegments.HideStamina(MimesisSettingsUI.WristHud.On && MimesisWatches.StatusOnWatch);
                _uiCam.Render();
                if (splitFx || hideNoise) RestoreAlpha();
                if (fxVisible)
                {
                    // Second pass: only the screen effects, into their own texture
                    if (_fxRaw == null || _fxRaw.width != _raw.width || _fxRaw.height != _raw.height)
                    {
                        if (_fxRaw != null) { _fxRaw.Release(); UnityEngine.Object.Destroy(_fxRaw); }
                        _fxRaw = new RenderTexture(_raw.width, _raw.height, 24, UnityEngine.Experimental.Rendering.GraphicsFormat.B8G8R8A8_SRGB);
                        _fxRaw.Create();
                    }
                    foreach (var g in _uiGraphics)
                    {
                        if (g == null || (MimesisHudSegments.FxGraphics.Contains(g) && (LensRain || !_rainGraphics.Contains(g)))) continue;
                        var cr = g.canvasRenderer;
                        if (cr == null) continue;
                        if (!_savedAlpha.ContainsKey(cr)) _savedAlpha[cr] = cr.GetAlpha();
                        cr.SetAlpha(0f);
                    }
                    var bg = _uiCam.backgroundColor;
                    _uiCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                    _uiCam.targetTexture = _fxRaw;
                    _uiCam.Render();
                    _uiCam.targetTexture = _raw;
                    _uiCam.backgroundColor = bg;
                    RestoreAlpha();
                    FxReady = true;
                }
                // Unity render-texture orientation -> OpenXR (top-left origin)
                Graphics.Blit(_raw, Output, new Vector2(1f, -1f), new Vector2(0f, 1f));
                OutputReady = true;

                MimesisHudSegments.Scan(_converted);
                if (MimesisInput.PanelVisible)
                {
                    MimesisHudSegments.SetHudHidden(true);
                    foreach (var sg in MimesisHudSegments.All) sg.Active = false;
                    MimesisSettingsUI.WristHud.PlaceFrozen(rig); // build 42: the watch keeps its panel in menus
                }
                else
                {
                    MimesisHudSegments.SetHudHidden(false);
                    MimesisHudSegments.Process(Output, _uiCam);
                    MimesisHudSegments.Place(rig, Width / Output.width / Distance);
                }

                if (MimesisInput.PanelVisible)
                {
                    // Menu open: the same texture is shown on the world-fixed menu panel instead
                    if (MimesisInput.LaserOnPanel) MimesisVrExtras.DrawCursor(Output, MimesisInput.LaserScreenPos);
                    Active = false;
                    return;
                }

                // Head-locked: centre of the HUD stays where you look (crosshair = gaze)
                Quaternion r = MimesisInput.HeadLocalRot;
                LocalRot = r;
                LocalPos = MimesisInput.HeadLocalPos + r * new Vector3(0f, 0f, Distance);
                Size = new Vector2(Width, Width * Output.height / Output.width);
                Active = true;
            }
            catch (Exception ex)
            {
                VRModCore.LogError("[MimesisVR] HUD error, disabling HUD layer:", ex);
                Enabled = false; RestoreAll(); Active = false;
            }
        }

        private static void EnsureCamera()
        {
            if (_uiCam != null) return;
            var go = new GameObject("MimesisVR_UICamera");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.transform.position = new Vector3(0f, -10000f, 0f); // out of the way
            _uiCam = go.AddComponent<Camera>();
            _uiCam.enabled = false;
            _uiCam.clearFlags = CameraClearFlags.SolidColor;
            _uiCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _uiCam.fieldOfView = 60f;
            _uiCam.nearClipPlane = 0.05f;
            _uiCam.farClipPlane = 20f;
            _uiCam.allowHDR = false;
            _uiCam.allowMSAA = false;
            var d = CameraExtensions.GetUniversalAdditionalCameraData(_uiCam);
            if (d != null)
            {
                d.renderPostProcessing = false;
                d.antialiasing = AntialiasingMode.None;
                d.renderShadows = false;
                d.requiresColorOption = CameraOverrideOption.Off;
                d.requiresDepthOption = CameraOverrideOption.Off;
            }
        }

        private static void EnsureOutput()
        {
            int w = Mathf.Max(64, Screen.width), h = Mathf.Max(64, Screen.height);
            if (Output != null && Output.width == w && Output.height == h) return;
            if (Output != null) { Output.Release(); UnityEngine.Object.Destroy(Output); }
            if (_raw != null) { _raw.Release(); UnityEngine.Object.Destroy(_raw); }
            // sRGB targets so bytes match the sRGB swapchain exactly
            _raw = new RenderTexture(w, h, 24, UnityEngine.Experimental.Rendering.GraphicsFormat.B8G8R8A8_SRGB);
            _raw.Create();
            Output = new RenderTexture(w, h, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.B8G8R8A8_SRGB);
            Output.Create();
            _uiCam.targetTexture = _raw;
        }

        // ---- death without a menu: the game frees the cursor while the death camera plays; that is not a menu
        private static UIPrefab_InGame _inGame;
        private static float _nextInGameLook, _nextDeadCheck;
        private static bool _deadNoMenu;
        private static readonly string[] HudPrefabs = { "UIPrefab_InGame(", "UIPrefab_Crosshair", "UIPrefab_Inventory", "UIPrefab_GameTips", "UIPrefab_GameHint", "UIPrefab_PlayerEnterInfo", "UIPrefab_GamepadCursor" };

        /// <summary>true while the local player is dead (death camera) and no real menu panel is open.</summary>
        public static bool DeadNoMenu()
        {
            if (Time.unscaledTime < _nextDeadCheck) return _deadNoMenu;
            _nextDeadCheck = Time.unscaledTime + 0.2f;
            _deadNoMenu = false;
            try
            {
                if (_inGame == null && Time.unscaledTime >= _nextInGameLook)
                {
                    _nextInGameLook = Time.unscaledTime + 2f;
                    _inGame = UnityEngine.Object.FindObjectOfType<UIPrefab_InGame>(true);
                }
                if (_inGame == null || !_inGame.isDead) return false;
                foreach (var c in _converted)
                {
                    if (c == null) continue;
                    foreach (Transform layer in c.transform)
                        foreach (Transform panel in layer)
                        {
                            if (!panel.gameObject.activeInHierarchy || !panel.name.StartsWith("UIPrefab_")) continue;
                            bool hud = false;
                            foreach (var h in HudPrefabs) if (panel.name.StartsWith(h)) { hud = true; break; }
                            if (!hud) return false; // a real menu is open
                        }
                }
                _deadNoMenu = true;
            }
            catch { }
            return _deadNoMenu;
        }

        private static void ScanAndConvert()
        {
            _converted.RemoveAll(c => c == null);
            var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            int added = 0;
            foreach (var c in canvases)
            {
                if (c == null || !c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                if (_converted.Contains(c)) continue;
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = _uiCam;
                c.planeDistance = 1f;
                _converted.Add(c);
                DumpCanvas(c);
                added++;
            }
            _uiMask = 0;
            foreach (var c in _converted) if (c != null) _uiMask |= 1 << c.gameObject.layer;
            if (added > 0) VRModCore.Log($"[MimesisVR] HUD: moved {added} canvas(es) to the VR UI camera (total {_converted.Count}).");
        }

        // ---- Full-screen dark overlays (fades, "BlackScreen", tutorial backdrops) ----
        // Head-locked in VR they black out the whole view; cap their opacity so the world stays visible.
        public static float DimCap = 0f;
        /// <summary>Redraw the HUD every N-th frame (1 = every frame).</summary>
        public static int UpdateEvery = 1;
        private static float _nextDimScan;
        private static readonly List<UnityEngine.UI.Graphic> _dimmers = new();
        private static readonly HashSet<int> _dimLogged = new();

        private static void ScanDimOverlays()
        {
            _dimmers.Clear();
            foreach (var c in _converted)
            {
                if (c == null || !c.isActiveAndEnabled) continue;
                var crt = c.transform as RectTransform;
                if (crt == null) continue;
                Vector2 cs = crt.rect.size;
                if (cs.x <= 1f || cs.y <= 1f) continue;
                foreach (var g in c.GetComponentsInChildren<UnityEngine.UI.Graphic>(false))
                {
                    if (g == null || !g.isActiveAndEnabled) continue;
                    if (!(g is UnityEngine.UI.Image) && !(g is UnityEngine.UI.RawImage)) continue;
                    var col = g.color;
                    if (Mathf.Max(col.r, Mathf.Max(col.g, col.b)) > 0.12f) continue;
                    // any sprite: popup backdrops use rounded-box sprites (e.g. 'box_r0' behind the session report)
                    if (g is UnityEngine.UI.RawImage ri && ri.texture != null) continue;
                    Vector2 s = g.rectTransform.rect.size * (Vector2)g.rectTransform.lossyScale / Mathf.Max(1e-5f, crt.lossyScale.x);
                    if (s.x < cs.x * 0.9f || s.y < cs.y * 0.9f) continue;
                    _dimmers.Add(g);
                    if (_dimLogged.Add(g.GetInstanceID())) VRModCore.Log($"[MimesisVR] Dark full-screen UI '{g.name}' (parent '{g.transform.parent?.name}') - opacity capped to {DimCap:F2} in VR.");
                }
            }
        }

        private static void ApplyDimCap()
        {
            foreach (var g in _dimmers)
            {
                if (g == null) continue;
                var cr = g.canvasRenderer;
                if (cr == null) continue;
                float baseA = g.color.a;
                if (baseA * cr.GetAlpha() > DimCap) cr.SetAlpha(DimCap / Mathf.Max(0.01f, baseA));
            }
        }

        private static bool _lastPanel;
        private static int _treeDumps;

        public static void ForceDump() { _treeDumps = 0; DumpActiveTree("requested from settings"); }

        private static void DumpActiveTree(string when)
        {
            if (_treeDumps++ > 12) return;
            try
            {
                var sb = new System.Text.StringBuilder($"[MimesisVR] UI tree ({when}):\n");
                foreach (var c in _converted)
                {
                    if (c == null || !c.isActiveAndEnabled) continue;
                    sb.Append("  ").Append(c.name).Append('\n');
                    foreach (Transform l1 in c.transform)
                    {
                        if (!l1.gameObject.activeInHierarchy) continue;
                        sb.Append("    ").Append(l1.name).Append(": ");
                        int n = 0;
                        foreach (Transform l2 in l1)
                        {
                            if (!l2.gameObject.activeInHierarchy) continue;
                            sb.Append(l2.name).Append(", ");
                            if (++n >= 30) { sb.Append("..."); break; }
                        }
                        sb.Append('\n');
                    }
                }
                VRModCore.Log(sb.ToString());
            }
            catch { }
        }

        private static void DumpCanvas(Canvas c)
        {
            try
            {
                if (!_dumped.Add(c.GetInstanceID())) return;
                var sb = new System.Text.StringBuilder();
                sb.Append($"[MimesisVR] Canvas '{c.name}' order={c.sortingOrder} layer={LayerMask.LayerToName(c.gameObject.layer)} scene={c.gameObject.scene.name} children: ");
                int n = 0;
                foreach (Transform ch in c.transform)
                {
                    sb.Append(ch.name).Append(ch.gameObject.activeSelf ? "" : "(off)").Append(", ");
                    if (++n >= 40) { sb.Append("..."); break; }
                }
                VRModCore.Log(sb.ToString());
            }
            catch { }
        }

        private static void RestoreAll()
        {
            if (_converted.Count == 0) return;
            foreach (var c in _converted)
            {
                if (c == null) continue;
                try { c.renderMode = RenderMode.ScreenSpaceOverlay; } catch { }
            }
            _converted.Clear();
        }

        public static void Shutdown()
        {
            RestoreAll();
            Active = false;
        }
    }
}
