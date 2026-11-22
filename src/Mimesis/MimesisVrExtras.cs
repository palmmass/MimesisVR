using System.Text;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityVRMod.Core;
using UnityVRMod.Features.VrVisualization;

namespace UnityVRMod.Mimesis
{
    /// <summary>
    /// MIMESIS VR: controller markers, floating menu panel with laser pointer,
    /// and render tweaks applied only while the VR eyes render.
    /// </summary>
    internal static class MimesisVrExtras
    {
        public const int VrOnlyLayer = 30; // "DebugOnly" - not rendered by the game camera
        public static int VrOnlyLayerMask => 1 << VrOnlyLayer;

        public static bool LegacyFlip;       // F8 toggles
        public static int MsaaSamples = 4;   // F5 toggles 4 <-> 1
        public static bool ShowBody;         // F4 toggles
        public static bool ItemFollowsController = true; // F3 toggles
        public static bool ShowControllers = true;
        public static float BeamIdleAlpha = 0.12f;
        public static bool BeamShort = true;
        private static GameObject _dot, _stub;
        private static Material _dotMat, _stubMat;
        /// <summary>Length of the short beam; it fades out over the last fifth.</summary>
        public static float StubLength = 0.30f;
        private const float StubSolidShare = 0.8f;

        /// <summary>Two crossed strips along +Z (length 1), opaque to 80% of the length, then fading to 0.</summary>
        private static Mesh MakeStubMesh()
        {
            var m = new Mesh { name = "MimesisVR_BeamStub" };
            float f = StubSolidShare;
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            void Strip(Vector3 side)
            {
                int b = v.Count;
                foreach (float z in new[] { 0f, f, 1f })
                {
                    float a = z < 0.999f ? 1f : 0f;
                    v.Add(-side * 0.5f + new Vector3(0, 0, z)); c.Add(new Color(1, 1, 1, a));
                    v.Add(side * 0.5f + new Vector3(0, 0, z)); c.Add(new Color(1, 1, 1, a));
                }
                for (int i = 0; i < 2; i++) { int k = b + i * 2; t.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 }); }
            }
            Strip(Vector3.right); Strip(Vector3.up);
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
        public enum HoldMode { AutoAlign, Grip, Aim }
        public static HoldMode ItemHold = HoldMode.AutoAlign; // PageDown cycles
        private static readonly Dictionary<int, Vector3> _itemAxis = new();

        /// <summary>Unit vector from the item's pivot towards its far end, in item-local space (zero if not elongated).</summary>
        private static Vector3 GetItemAxis(Transform itemT)
        {
            int id = itemT.GetInstanceID();
            if (_itemAxis.TryGetValue(id, out var cached)) return cached;
            Vector3 axis = Vector3.zero;
            try
            {
                bool any = false;
                var b = new Bounds();
                void Add(Transform t, Bounds lb)
                {
                    Vector3 c = lb.center, e = lb.extents;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = c + Vector3.Scale(e, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        var p = itemT.InverseTransformPoint(t.TransformPoint(corner));
                        if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                    }
                }
                foreach (var mf in itemT.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh != null) Add(mf.transform, mf.sharedMesh.bounds);
                foreach (var sm in itemT.GetComponentsInChildren<SkinnedMeshRenderer>()) if (sm.sharedMesh != null) Add(sm.transform, sm.sharedMesh.bounds);
                if (any)
                {
                    Vector3 size = b.size;
                    float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                    float second = size.x + size.y + size.z - longest - Mathf.Min(size.x, Mathf.Min(size.y, size.z));
                    if (longest > 0.25f && longest > second * 2.2f) // clearly elongated
                    {
                        Vector3 dir = size.x == longest ? Vector3.right : size.y == longest ? Vector3.up : Vector3.forward;
                        float s = Vector3.Dot(b.center, dir);
                        axis = s >= 0 ? dir : -dir;
                    }
                    VRModCore.Log($"[MimesisVR] Item '{itemT.name}': local bounds center {b.center} size {size} -> axis {axis}");
                }
            }
            catch (Exception ex) { VRModCore.LogError("[MimesisVR] GetItemAxis failed:", ex); }
            _itemAxis[id] = axis;
            return axis;
        }
        private static Material _pointerMat;

        // Held item: orientation relative to the controller is taken once when the item appears in the hand and then
        // kept fixed (it used to follow the game's hold animation every frame, which flipped long items like the bat).
        private static int _heldId, _heldParent;
        private static float _heldAt;
        private static bool _heldSettled;
        private static Quaternion _heldRel = Quaternion.identity;

        /// <summary>Where the item in the right hand should be in VR (world), driven by the right controller.</summary>
        public static bool ComputeItemTarget(Transform itemT, out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = Quaternion.identity;
            var c = MimesisInput.Controllers;
            if (itemT == null || _rig == null || c == null || !c.Ready || !c.RightPoseValid || MimesisInput.GameCamera == null) return false;
            Quaternion holdWorld = _rig.rotation * (ItemHold == HoldMode.Aim ? c.RightAimRot : c.RightRot);
            Vector3 gripWorld = _rig.TransformPoint(c.RightPos);
            MimesisHands.AdjustGrip(ref gripWorld, ref holdWorld, true);
            int id = itemT.GetInstanceID();
            int parent = itemT.parent != null ? itemT.parent.GetInstanceID() : 0;
            // Re-take the orientation when the item moves to another socket (taken out of the inventory it first sits
            // on the back), and once more half a second later, when the game's draw animation has finished
            bool settle = id == _heldId && !_heldSettled && Time.unscaledTime - _heldAt > 0.5f;
            if (id != _heldId || parent != _heldParent || settle)
            {
                if (id != _heldId || parent != _heldParent) { _heldAt = Time.unscaledTime; _heldSettled = false; } else _heldSettled = true;
                _heldId = id; _heldParent = parent;
                // Orientation relative to the view in the game's hold pose, re-based on the controller
                Quaternion rel = Quaternion.Inverse(MimesisInput.GameCamera.rotation) * itemT.rotation;
                if (ItemHold == HoldMode.AutoAlign)
                {
                    Vector3 axisLocal = GetItemAxis(itemT);
                    if (axisLocal != Vector3.zero)
                    {
                        // Long items: long axis along the controller; direction chosen once from the hold pose
                        Vector3 axisInHold = rel * axisLocal;
                        float d = Vector3.Dot(axisInHold, Vector3.forward);
                        if (Mathf.Abs(d) > 0.5f && d < 0f) axisInHold = -axisInHold;
                        rel = Quaternion.FromToRotation(axisInHold, Vector3.forward) * rel;
                    }
                }
                _heldRel = rel;
                VRModCore.Log($"[MimesisVR] Holding '{itemT.name}' (in '{itemT.parent?.name}'): grip orientation fixed ({ItemHold}{(settle ? ", after draw animation" : "")}).");
            }
            rot = holdWorld * _heldRel;
            pos = gripWorld + holdWorld * new Vector3(0f, 0f, 0.03f);
            return true;
        }

        public static void ResetHeldItem() { _heldId = 0; }
        private static bool _panelUvFlip = true; // F6 toggles

        private static Transform _rig;
        private static GameObject _leftHand, _rightHand, _panel, _laser;
        private static Material _panelMat, _laserMat;
        private static RenderTexture _panelRT;
        private static bool _panelWasVisible;
        private static Vector3 _panelAnchorLocal, _panelDirLocal = Vector3.forward;

        // RenderObjects camera overrides (first-person hands technique)
        private class Override { public object camSettings; public FieldInfo field; public bool original; public string name; }
        private static readonly List<Override> _overrides = new();
        private static bool _overridesScanned;

        public static void Setup(Transform rig)
        {
            Teardown();
            _rig = rig;
            try
            {
                _leftHand = MakeHand("MimesisVR_LeftHand");
                _rightHand = MakeHand("MimesisVR_RightHand");

                _panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
                _panel.name = "MimesisVR_Panel";
                StripCollider(_panel);
                _panel.layer = VrOnlyLayer;
                _panel.transform.SetParent(rig, false);
                _panelMat = MakeOverlayMaterial(Color.white);
                _panel.GetComponent<Renderer>().sharedMaterial = _panelMat;
                _panel.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                _panel.SetActive(false);

                _laser = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _laser.name = "MimesisVR_Laser";
                StripCollider(_laser);
                _laser.layer = VrOnlyLayer;
                _laser.transform.SetParent(rig, false);
                _laserMat = MakeOverlayMaterial(new Color(0.3f, 0.8f, 1f, 1f));
                _laser.GetComponent<Renderer>().sharedMaterial = _laserMat;
                _laser.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                _laser.SetActive(false);

                if (!_overridesScanned) ScanRendererFeatures();
                VRModCore.Log("[MimesisVR] Extras ready (hands, panel, laser).");
            }
            catch (Exception ex) { VRModCore.LogError("[MimesisVR] Extras setup failed:", ex); }
        }

        public static void Teardown()
        {
            foreach (var go in new[] { _leftHand, _rightHand, _panel, _laser, _pointer, _dot, _stub }) if (go != null) UnityEngine.Object.Destroy(go);
            _leftHand = _rightHand = _panel = _laser = _pointer = _dot = _stub = null;
            if (_captureOut != null) { _captureOut.Release(); UnityEngine.Object.Destroy(_captureOut); _captureOut = null; }
            LaserLayerActive = false;
            if (_panelRT != null) { _panelRT.Release(); UnityEngine.Object.Destroy(_panelRT); _panelRT = null; }
            PanelOutput = null;
            PanelLayerActive = false;
            MimesisHud.Shutdown();
            _rig = null;
            _panelWasVisible = false;
        }

        private static void StripCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) UnityEngine.Object.Destroy(c);
        }

        private static GameObject MakeHand(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            StripCollider(go);
            go.layer = VrOnlyLayer;
            go.transform.SetParent(_rig, false);
            go.transform.localScale = new Vector3(0.035f, 0.035f, 0.10f);
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.sharedMaterial = MakeOpaqueMaterial(new Color(0.55f, 0.6f, 0.7f, 1f));
            return go;
        }

        /// <summary>Unlit opaque material that writes depth (so world screens/transparents do not draw over it).</summary>
        private static Material MakeOpaqueMaterial(Color c)
        {
            var sh = Shader.Find("Hidden/Internal-Colored");
            if (sh == null) return MakeOverlayMaterial(c, depthTest: true);
            var m = new Material(sh) { color = c, renderQueue = 2000 };
            m.SetInt("_SrcBlend", (int)BlendMode.One);
            m.SetInt("_DstBlend", (int)BlendMode.Zero);
            m.SetInt("_Cull", (int)CullMode.Back);
            m.SetInt("_ZWrite", 1);
            m.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            return m;
        }

        private static Material MakeOverlayMaterial(Color c, bool depthTest = false)
        {
            Shader sh = Shader.Find("UI/Default") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default");
            VRModCore.Log($"[MimesisVR] Overlay shader: {(sh != null ? sh.name : "NONE")}");
            var m = new Material(sh) { color = c, renderQueue = 4000 };
            if (m.HasProperty("unity_GUIZTestMode")) m.SetInt("unity_GUIZTestMode", (int)(depthTest ? CompareFunction.LessEqual : CompareFunction.Always));
            if (depthTest) m.renderQueue = 2450;
            return m;
        }

        /// <summary>Called each VR frame after the rig has been positioned, before the eyes render.</summary>
        public static void BeforeEyeRender(VrCameraSetup_CoreOpenXR setup)
        {
            MimesisPerf.LapStart();
            HandleHotkeys();
            MimesisSettings.RecenterHandler ??= setup.RequestRecenter;
            MimesisImage.Update();
            MimesisMonitor.Ensure();
            MimesisHands.Ensure();
            MimesisCheats.Update();
            MimesisHands.Rig = _rig;
            MimesisHands.Refollow ??= setup.RefollowNow;
            if (_rig == null) return;
            var c = MimesisInput.Controllers;
            if (c == null || !c.Ready) { UpdatePanel(); MimesisHud.Update(_rig); AfterHud(); MimesisWatches.Frame(_rig); MimesisSettingsUI.Update(_rig); return; }

            if (_leftHand != null)
            {
                _leftHand.SetActive(c.LeftPoseValid && ShowControllers && MimesisHands.HandMode == MimesisHands.Mode.Bars);
                if (c.LeftPoseValid) _leftHand.transform.SetLocalPositionAndRotation(c.LeftPos + c.LeftRot * new Vector3(0, 0, 0.02f), c.LeftRot);
            }
            if (_rightHand != null)
            {
                _rightHand.SetActive(c.RightPoseValid && ShowControllers && MimesisHands.HandMode == MimesisHands.Mode.Bars);
                if (c.RightPoseValid) _rightHand.transform.SetLocalPositionAndRotation(c.RightPos + c.RightRot * new Vector3(0, 0, 0.02f), c.RightRot);
            }

            MimesisPerf.Lap(0);
            UpdatePanel();
            MimesisHud.Update(_rig);
            AfterHud();
            MimesisWatches.Frame(_rig);
            MimesisPerf.Lap(1);
            MimesisSettingsUI.Update(_rig);
            MimesisPerf.Lap(2);
        }

        /// <summary>Laser quad from the right controller (rig-local), used by the mod settings panel.</summary>
        public static void ShowLaser(Vector3 o, Vector3 d, float len)
        {
            EnsureLaserTex();
            Vector3 mid = o + d * (len * 0.5f);
            Vector3 toViewer = MimesisInput.HeadLocalPos - mid;
            Vector3 perp = toViewer - Vector3.Project(toViewer, d);
            if (perp.sqrMagnitude < 1e-6f) perp = Vector3.Cross(d, Vector3.right);
            LaserLocalRot = Quaternion.LookRotation(-perp.normalized, d);
            LaserLocalPos = mid;
            LaserSize = new Vector2(0.006f, len);
            LaserLayerActive = true;
            if (_pointer != null) _pointer.SetActive(false);
        }

        public static bool Mirror; // F1 toggles
        /// <summary>Build 45: which eye the monitor shows (right by default - most players aim with the right eye).</summary>
        public static bool MirrorRightEye = true;
        private static Material _mirrorUiMat;

        public static void MirrorToScreen(RenderTexture eye, Camera eyeCam = null, Transform rig = null)
        {
            if (!Mirror || eye == null || !eye.IsCreated()) return;
            try
            {
                float e = (float)eye.width / eye.height;
                float a = (float)Screen.width / Mathf.Max(1, Screen.height);
                Vector2 scale = Vector2.one, offset = Vector2.zero;
                if (e < a) { scale.y = e / a; offset.y = (1f - scale.y) * 0.5f; }
                else { scale.x = a / e; offset.x = (1f - scale.x) * 0.5f; }
                Graphics.Blit(eye, (RenderTexture)null, scale, offset);
                var st = MimesisSettingsUI.RawTex;
                if (MimesisSettingsUI.Open && st != null && st.IsCreated() && MimesisStream.Mode != 2)
                {
                    if (_mirrorUiMat == null) { _mirrorUiMat = new Material(Shader.Find("UI/Default")); }
                    RenderTexture.active = null;
                    GL.PushMatrix();
                    GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);
                    float sw = Screen.width * 0.6f, sh = sw * st.height / st.width;
                    Graphics.DrawTexture(new Rect((Screen.width - sw) * 0.5f, (Screen.height - sh) * 0.5f, sw, sh), st, _mirrorUiMat);
                    GL.PopMatrix();
                }
                var ui = MimesisHud.Raw;
                if (ui != null && ui.IsCreated() && (MimesisHud.Active || MimesisInput.PanelVisible))
                {
                    if (_mirrorUiMat == null) { _mirrorUiMat = new Material(Shader.Find("UI/Default")); }
                    RenderTexture.active = null;
                    GL.PushMatrix();
                    GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);
                    Graphics.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), ui, _mirrorUiMat);
                    GL.PopMatrix();
                }
                MimesisStream.Draw(eyeCam, rig, scale); // build 45: watch screens, wrist panel and the rest for viewers
            }
            catch (Exception ex) { VRModCore.LogError("[MimesisVR] Mirror failed, disabling:", ex); Mirror = false; }
        }

        private static void HandleHotkeys()
        {
            if (UniverseLib.Input.InputManager.GetKeyDown(KeyCode.F1)) { Mirror = !Mirror; VRModCore.Log($"[MimesisVR] Mirror to monitor: {Mirror}"); }
            if (!MimesisSettings.DebugTools) return; // build 47: the rest are developer hotkeys
            if (UniverseLib.Input.InputManager.GetKeyDown(KeyCode.F8)) { LegacyFlip = !LegacyFlip; VRModCore.Log($"[MimesisVR] Flip mode: {(LegacyFlip ? "legacy (projection)" : "blit")}"); }
            if (UniverseLib.Input.InputManager.GetKeyDown(KeyCode.F7)) { MimesisInput.ForcePanel = !MimesisInput.ForcePanel; VRModCore.Log($"[MimesisVR] Force panel: {MimesisInput.ForcePanel}"); }
            if (UniverseLib.Input.InputManager.GetKeyDown(KeyCode.PageDown)) { ItemHold = (HoldMode)(((int)ItemHold + 1) % 3); ResetHeldItem(); VRModCore.Log($"[MimesisVR] Item hold mode: {ItemHold}"); }
            if (UniverseLib.Input.InputManager.GetKeyDown(KeyCode.End)) { MimesisInput.InteractByHand = !MimesisInput.InteractByHand; VRModCore.Log($"[MimesisVR] Interact by hand: {MimesisInput.InteractByHand}"); }
            if (UniverseLib.Input.InputManager.GetKeyDown(KeyCode.F5)) { MsaaSamples = MsaaSamples > 1 ? 1 : 4; VRModCore.Log($"[MimesisVR] MSAA: x{MsaaSamples}"); }
            if (UniverseLib.Input.InputManager.GetKeyDown(KeyCode.F4)) { ShowBody = !ShowBody; VRModCore.Log($"[MimesisVR] Show body: {ShowBody}"); }
            if (UniverseLib.Input.InputManager.GetKeyDown(KeyCode.F3)) { ItemFollowsController = !ItemFollowsController; VRModCore.Log($"[MimesisVR] Item follows controller: {ItemFollowsController}"); }
            if (UniverseLib.Input.InputManager.GetKeyDown(KeyCode.F6)) { _panelUvFlip = !_panelUvFlip; _quadFlip = !_quadFlip; VRModCore.Log($"[MimesisVR] Panel flip: uv={_panelUvFlip} quad={_quadFlip}"); }
            if (UniverseLib.Input.InputManager.GetKeyDown(KeyCode.F2)) { QuadLayerUnavailable = !QuadLayerUnavailable; VRModCore.Log($"[MimesisVR] Panel mode: {(QuadLayerUnavailable ? "in-world mesh" : "quad layer")}"); }
        }

        // ---- Panel state consumed by the quad-layer submitter ----
        public static RenderTexture PanelOutput;
        public static bool PanelLayerActive;
        public static bool QuadLayerUnavailable;
        public static Vector3 PanelLocalPos;
        public static Quaternion PanelLocalRot = Quaternion.identity;
        public static Vector2 PanelSize;
        public static bool MenuBackdrop;
        private static bool _replacePanel;
        private static bool _quadFlip;          // F6 toggles (quad layer orientation)
        private static Material _cursorMat;

        public static void RequestPanelReplace() { _replacePanel = true; }

        // Laser (menu) as its own quad layer so it is drawn over the menu panel
        public static bool LaserLayerActive;
        public static RenderTexture LaserTex;
        public static Vector3 LaserLocalPos;
        public static Quaternion LaserLocalRot = Quaternion.identity;
        public static Vector2 LaserSize;
        public static ulong PanelLayerFlags;
        private static RenderTexture _captureOut;
        private static GameObject _pointer;
        public static Vector3 PointerEndWorld;

        internal static void EnsureLaserTex()
        {
            if (LaserTex != null && LaserTex.IsCreated()) return;
            var t = new Texture2D(8, 2, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            float[] a = { 0f, 0.25f, 0.85f, 1f, 1f, 0.85f, 0.25f, 0f };
            for (int x = 0; x < 8; x++) for (int y = 0; y < 2; y++)
            {
                float k = a[x];
                // premultiplied green with a whitish core
                var col = new Color(Mathf.Lerp(0.15f, 0.8f, k * k) * k, 1f * k, Mathf.Lerp(0.15f, 0.7f, k * k) * k, k);
                t.SetPixel(x, y, col);
            }
            t.Apply();
            LaserTex = new RenderTexture(8, 2, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.B8G8R8A8_SRGB);
            LaserTex.Create();
            Graphics.Blit(t, LaserTex);
            UnityEngine.Object.Destroy(t);
        }

        private static void UpdateGameplayPointer(bool menuOpen)
        {
            var c = MimesisInput.Controllers;
            bool show = !menuOpen && !MimesisSettingsUI.Open && MimesisInput.InteractByHand && c != null && c.Ready && c.RightPoseValid && _rig != null;
            MimesisInput.AimValid = show;
            if (!show) { if (_pointer != null) _pointer.SetActive(false); if (_dot != null) _dot.SetActive(false); if (_stub != null) _stub.SetActive(false); return; }
            Vector3 o = _rig.TransformPoint(c.RightAimPos);
            Vector3 d = _rig.TransformDirection(c.RightAimRot * Vector3.forward).normalized;
            MimesisInput.AimOrigin = o;
            MimesisInput.AimDir = d;
            if (_pointer == null)
            {
                _pointer = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _pointer.name = "MimesisVR_Pointer";
                StripCollider(_pointer);
                _pointer.layer = VrOnlyLayer;
                _pointer.transform.SetParent(_rig, false);
                var r = _pointer.GetComponent<Renderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                _pointerMat = MakeOverlayMaterial(new Color(0.35f, 1f, 0.45f, 0.55f), depthTest: true);
                // Draw after the game's transparent objects (in-world screens, glass) - still hidden by solid walls
                _pointerMat.renderQueue = 3500;
                r.sharedMaterial = _pointerMat;
            }
            bool target = MimesisInput.HasPickTarget;
            float len = 2.5f;
            int mask = ~((1 << 2) | (1 << 20) | (1 << 21) | (1 << VrOnlyLayer));
            bool hitSomething = Physics.Raycast(o, d, out var hit, len, mask, QueryTriggerInteraction.Ignore);
            if (hitSomething) len = Mathf.Max(0.05f, hit.distance);
            // Pointing at the wrist inventory or the gear (last frame): the beam ends there and lights up
            float ui = MimesisSettingsUI.UiRayHit * _rig.lossyScale.x;
            if (ui > 0f && ui < len) { len = ui; hitSomething = true; target = true; }
            PointerEndWorld = o + d * len;
            float inv = 1f / Mathf.Max(0.0001f, _rig.lossyScale.x);
            var pr = _pointer.GetComponent<Renderer>();
            float beamLen;
            if (BeamShort)
            {
                // Short, almost transparent stub from the hand that fades out over its last fifth (StubLength);
                // it lights up on something usable, and a dot shows where it points
                if (_stub == null)
                {
                    _stub = new GameObject("MimesisVR_BeamStub") { layer = VrOnlyLayer };
                    _stub.transform.SetParent(_rig, false);
                    _stub.AddComponent<MeshFilter>().sharedMesh = MakeStubMesh();
                    var sr = _stub.AddComponent<MeshRenderer>();
                    sr.shadowCastingMode = ShadowCastingMode.Off;
                    _stubMat = MakeOverlayMaterial(new Color(0.35f, 1f, 0.45f, 0.1f), depthTest: true);
                    _stubMat.renderQueue = 3500;
                    sr.sharedMaterial = _stubMat;
                }
                float sl = Mathf.Min(len, StubLength);
                _stubMat.color = new Color(0.35f, 1f, 0.45f, target ? 0.5f : BeamIdleAlpha);
                _stub.SetActive(true);
                _stub.transform.SetPositionAndRotation(o, Quaternion.LookRotation(d));
                _stub.transform.localScale = new Vector3(0.0035f * inv, 0.0035f * inv, sl * inv);
                _pointer.SetActive(false);
                UpdateDot(target || (hitSomething && BeamIdleAlpha > 0f), target, PointerEndWorld - d * 0.01f, Vector3.Distance(o, PointerEndWorld), inv);
                return;
            }
            if (_stub != null) _stub.SetActive(false);
            {
                beamLen = len;
                if (_pointerMat != null) _pointerMat.color = target ? new Color(0.35f, 1f, 0.45f, 0.6f) : new Color(0.35f, 1f, 0.45f, BeamIdleAlpha);
                if (pr != null) pr.enabled = target || BeamIdleAlpha > 0.001f;
            }
            _pointer.SetActive(true);
            _pointer.transform.position = o + d * (beamLen * 0.5f);
            _pointer.transform.rotation = Quaternion.LookRotation(d);
            _pointer.transform.localScale = new Vector3(0.0025f, 0.0025f, beamLen * inv);
            UpdateDot(BeamShort && (target || hitSomething), target, PointerEndWorld - d * 0.01f, Vector3.Distance(o, PointerEndWorld), inv);
        }

        private static void UpdateDot(bool show, bool target, Vector3 pos, float dist, float inv)
        {
            if (!show) { if (_dot != null) _dot.SetActive(false); return; }
            if (_dot == null)
            {
                _dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _dot.name = "MimesisVR_PointerDot";
                StripCollider(_dot);
                _dot.layer = VrOnlyLayer;
                _dot.transform.SetParent(_rig, false);
                var r = _dot.GetComponent<Renderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                _dotMat = MakeOverlayMaterial(new Color(0.35f, 1f, 0.45f, 0.9f), depthTest: false);
                _dotMat.renderQueue = 3600;
                r.sharedMaterial = _dotMat;
            }
            _dot.SetActive(true);
            _dotMat.color = target ? new Color(0.45f, 1f, 0.55f, 0.95f) : new Color(0.35f, 1f, 0.45f, Mathf.Clamp(BeamIdleAlpha * 4f, 0.05f, 0.35f));
            float size = Mathf.Clamp(dist * (target ? 0.012f : 0.007f), 0.004f, 0.03f);
            _dot.transform.position = pos;
            _dot.transform.localScale = Vector3.one * size * inv;
        }

        private static void UpdatePanel()
        {
            MenuBackdrop = MimesisInput.LocalActor == null;
            bool visible = MimesisInput.PanelVisible;
            LaserLayerActive = false;
            if (_panel == null) return;
            UpdateGameplayPointer(visible);
            if (_laser != null) _laser.SetActive(false); // replaced by the laser quad layer / mesh fallback below

            if (!visible)
            {
                if (_panelWasVisible) { _panel.SetActive(false); MimesisInput.LaserOnPanel = false; }
                _panelWasVisible = false;
                PanelLayerActive = false;
                return;
            }

            int w = Mathf.Max(64, Screen.width), h = Mathf.Max(64, Screen.height);
            bool uiCamMode = MimesisHud.Enabled && !QuadLayerUnavailable;

            float width = 1.25f;
            float height = width * h / w;
            if (!_panelWasVisible || _replacePanel)
            {
                var headRot = MimesisInput.HeadLocalRot;
                Vector3 fwd = headRot * Vector3.forward; fwd.y = 0; if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; fwd.Normalize();
                _panelAnchorLocal = MimesisInput.HeadLocalPos + new Vector3(0f, -0.08f, 0f);
                _panelDirLocal = fwd;
                _panel.transform.localRotation = Quaternion.LookRotation(fwd, Vector3.up);
                _panel.transform.localScale = new Vector3(width, height, 1f);
                _panel.SetActive(true);
                _panelWasVisible = true;
                _replacePanel = false;
            }
            _panel.transform.localPosition = _panelAnchorLocal + _panelDirLocal * MimesisInput.PanelDistance;
            PanelLocalPos = _panel.transform.localPosition;
            PanelLocalRot = _panel.transform.localRotation;
            PanelSize = new Vector2(width, height);

            // Laser from the right controller aim pose (rig-local maths)
            var c = MimesisInput.Controllers;
            MimesisInput.LaserOnPanel = false;
            bool aimOk = false; Vector3 aimPos = default; Quaternion aimRot = Quaternion.identity;
            if (c != null && c.Ready) MimesisInput.MenuAim(c, out aimOk, out aimPos, out aimRot);
            if (aimOk)
            {
                Vector3 o = aimPos;
                Vector3 d = (aimRot * Vector3.forward).normalized;
                float len = 2f;
                Vector3 n = PanelLocalRot * Vector3.forward;
                float denom = Vector3.Dot(n, d);
                if (Mathf.Abs(denom) > 1e-4f)
                {
                    float t = Vector3.Dot(PanelLocalPos - o, n) / denom;
                    if (t > 0f)
                    {
                        Vector3 hit = o + d * t;
                        Vector3 local = Quaternion.Inverse(PanelLocalRot) * (hit - PanelLocalPos);
                        float u = local.x / width, v = local.y / height;
                        if (Mathf.Abs(u) <= 0.5f && Mathf.Abs(v) <= 0.5f)
                        {
                            len = t;
                            MimesisInput.LaserOnPanel = true;
                            MimesisInput.LaserScreenPos = new Vector2((u + 0.5f) * Screen.width, (v + 0.5f) * Screen.height);
                        }
                    }
                }
                if (MimesisSettingsUI.Open) MimesisInput.LaserOnPanel = false; // mod settings panel has the pointer
                EnsureLaserTex();
                Vector3 mid = o + d * (len * 0.5f);
                Vector3 toViewer = MimesisInput.HeadLocalPos - mid;
                Vector3 perp = toViewer - Vector3.Project(toViewer, d);
                if (perp.sqrMagnitude < 1e-6f) perp = Vector3.Cross(d, Vector3.right);
                LaserLocalRot = Quaternion.LookRotation(-perp.normalized, d);
                LaserLocalPos = mid;
                LaserSize = new Vector2(0.006f, len);
                LaserLayerActive = !QuadLayerUnavailable;
                if (QuadLayerUnavailable && _laser != null)
                {
                    _laser.SetActive(true);
                    _laser.transform.localPosition = mid;
                    _laser.transform.localRotation = Quaternion.LookRotation(d);
                    _laser.transform.localScale = new Vector3(0.004f, 0.004f, len);
                }
            }

            if (uiCamMode)
            {
                // Content comes from the VR UI camera (menu only, live world behind it)
                _panel.GetComponent<Renderer>().enabled = false;
                PanelLayerFlags = XrQuadLayer.FLAG_BLEND_SOURCE_ALPHA;
                // PanelOutput / PanelLayerActive are set after MimesisHud.Update
                return;
            }

            // Fallback: capture the monitor image
            if (_panelRT == null || _panelRT.width != w || _panelRT.height != h || _panelRT.useMipMap != QuadLayerUnavailable)
            {
                if (_panelRT != null) { _panelRT.Release(); UnityEngine.Object.Destroy(_panelRT); }
                _panelRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { useMipMap = QuadLayerUnavailable, autoGenerateMips = false, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
                _panelRT.Create();
                _panelMat.mainTexture = _panelRT;
            }
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_panelRT);
            PanelLayerFlags = 0;

            if (!QuadLayerUnavailable)
            {
                if (_captureOut == null || _captureOut.width != w || _captureOut.height != h)
                {
                    if (_captureOut != null) { _captureOut.Release(); UnityEngine.Object.Destroy(_captureOut); }
                    _captureOut = new RenderTexture(w, h, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.B8G8R8A8_UNorm);
                    _captureOut.Create();
                }
                if (_quadFlip) Graphics.Blit(_panelRT, _captureOut, new Vector2(1f, -1f), new Vector2(0f, 1f));
                else Graphics.Blit(_panelRT, _captureOut);
                if (MimesisInput.LaserOnPanel) DrawCursor(_captureOut, MimesisInput.LaserScreenPos);
                _panel.GetComponent<Renderer>().enabled = false;
                PanelOutput = _captureOut;
                PanelLayerActive = true;
            }
            else
            {
                _panelRT.GenerateMips();
                _panelMat.mainTextureScale = new Vector2(1f, _panelUvFlip ? -1f : 1f);
                _panelMat.mainTextureOffset = new Vector2(0f, _panelUvFlip ? 1f : 0f);
                _panel.GetComponent<Renderer>().enabled = true;
                PanelLayerActive = false;
            }
        }

        /// <summary>Called after MimesisHud.Update: hook the UI camera output into the menu panel layer.</summary>
        private static void AfterHud()
        {
            if (!MimesisInput.PanelVisible || !MimesisHud.Enabled || QuadLayerUnavailable) return;
            PanelOutput = MimesisHud.Output;
            PanelLayerActive = MimesisHud.OutputReady && PanelOutput != null;
            if (!PanelLayerActive)
            {
                // No converted canvases yet: fall back to the capture for this frame
                QuadLayerUnavailableThisFrame();
            }
        }

        private static void QuadLayerUnavailableThisFrame() { /* panel stays hidden for a frame */ }

        public static void DrawCursor(RenderTexture rt, Vector2 screenPos)
        {
            if (_cursorMat == null)
            {
                var sh = Shader.Find("Hidden/Internal-Colored");
                if (sh == null) return;
                _cursorMat = new Material(sh);
                _cursorMat.SetInt("_ZTest", (int)CompareFunction.Always);
                _cursorMat.SetInt("_Cull", (int)CullMode.Off);
            }
            float x = screenPos.x / Screen.width * rt.width;
            float y = screenPos.y / Screen.height * rt.height;
            if (!_quadFlip) y = rt.height - y;
            float s = Mathf.Max(6f, rt.height / 110f);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, rt.width, 0, rt.height);
            _cursorMat.SetPass(0);
            GL.Begin(GL.TRIANGLES);
            Disc(x, y, s * 1.35f, new Color(0.05f, 0.35f, 0.1f, 1f));
            Disc(x, y, s, new Color(0.35f, 1f, 0.45f, 1f));
            Disc(x, y, s * 0.45f, new Color(0.85f, 1f, 0.88f, 1f));
            GL.End();
            GL.PopMatrix();
            RenderTexture.active = prev;
        }

        private static void Disc(float x, float y, float r, Color col)
        {
            const int seg = 20;
            GL.Color(col);
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                GL.Vertex3(x, y, 0);
                GL.Vertex3(x + Mathf.Cos(a0) * r, y + Mathf.Sin(a0) * r, 0);
                GL.Vertex3(x + Mathf.Cos(a1) * r, y + Mathf.Sin(a1) * r, 0);
            }
        }

        private static void Quad(float x, float y, float r, Color col)
        {
            GL.Color(col);
            GL.Vertex3(x - r, y - r, 0); GL.Vertex3(x - r, y + r, 0); GL.Vertex3(x + r, y + r, 0); GL.Vertex3(x + r, y - r, 0);
        }

        private static Vector3 HeadLocal()
        {
            // Approximate head position in rig space from the left eye camera if present
            var cam = _rig.Find("XrVrCamera_Left");
            return cam != null ? cam.localPosition : new Vector3(0, 1.6f, 0);
        }

        // ---- Renderer feature handling ----
        private static void ScanRendererFeatures()
        {
            _overridesScanned = true;
            var sb = new StringBuilder("[MimesisVR] ===== RENDERER FEATURES =====\n");
            try
            {
                var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (asset == null) { sb.AppendLine("No URP asset"); VRModCore.Log(sb.ToString()); return; }
                var listField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic);
                var list = listField?.GetValue(asset) as ScriptableRendererData[];
                if (list == null) { sb.AppendLine("No renderer data list"); VRModCore.Log(sb.ToString()); return; }
                for (int i = 0; i < list.Length; i++)
                {
                    var data = list[i];
                    if (data == null) continue;
                    sb.AppendLine($"Renderer[{i}] {data.name} ({data.GetType().Name})");
                    foreach (var f in data.rendererFeatures)
                    {
                        if (f == null) continue;
                        sb.AppendLine($"  - {f.name} : {f.GetType().FullName} active={f.isActive}");
                        var settingsField = f.GetType().GetField("settings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        var settings = settingsField?.GetValue(f);
                        if (settings == null) continue;
                        DumpObject(sb, settings, "      ", 2);
                        var camField = settings.GetType().GetField("cameraSettings");
                        var camSettings = camField?.GetValue(settings);
                        var ovField = camSettings?.GetType().GetField("overrideCamera");
                        if (ovField != null && ovField.FieldType == typeof(bool) && (bool)ovField.GetValue(camSettings))
                        {
                            _overrides.Add(new Override { camSettings = camSettings, field = ovField, name = f.name });
                            sb.AppendLine("      >> camera override will be disabled for VR eyes");
                        }
                    }
                }
            }
            catch (Exception ex) { sb.AppendLine("Scan failed: " + ex); }
            sb.Append("[MimesisVR] ===== END FEATURES =====");
            VRModCore.Log(sb.ToString());
        }

        private static void DumpObject(StringBuilder sb, object o, string indent, int depth)
        {
            if (o == null || depth < 0) return;
            foreach (var fi in o.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                object v;
                try { v = fi.GetValue(o); } catch { continue; }
                if (v == null) { sb.AppendLine($"{indent}{fi.Name} = null"); continue; }
                var t = v.GetType();
                if (t.IsPrimitive || t.IsEnum || v is string || v is Vector3 || v is LayerMask || v is Color)
                    sb.AppendLine($"{indent}{fi.Name} = {(v is LayerMask lm ? lm.value.ToString() : v.ToString())}");
                else if (v is UnityEngine.Object uo)
                    sb.AppendLine($"{indent}{fi.Name} = [{t.Name}] {uo.name}");
                else if (depth > 0 && !(v is System.Collections.IEnumerable))
                {
                    sb.AppendLine($"{indent}{fi.Name}:");
                    DumpObject(sb, v, indent + "  ", depth - 1);
                }
            }
        }

        // ---- Per-frame changes that only the VR eyes should see ----
        private static Transform _movedItem;
        private static Vector3 _itemPos; private static Quaternion _itemRot;
        private static readonly List<Renderer> _hiddenBody = new();
        private static FieldInfo _puppetField;

        public static void PreEyes(Camera left, Camera right)
        {
            DisableCameraOverrides();
            foreach (var cam in new[] { left, right })
            {
                if (cam == null) continue;
                var d = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam);
                if (d != null && !LegacyFlip && MsaaSamples > 1) d.antialiasing = AntialiasingMode.None;
            }

            _movedItem = null;
            _hiddenBody.Clear();
            var actor = MimesisInput.LocalActor;
            var c = MimesisInput.Controllers;
            if (actor == null || _rig == null) return;
            if (!_bodyDumped) { _bodyDumped = true; DumpBody(actor); }
            try
            {
                Transform itemT = null;
                var item = actor.GetHandheldItem();
                if (item != null) itemT = item.Transform;

                if (!ShowBody && !MimesisHands.CharacterArms)
                {
                    _puppetField ??= typeof(Mimic.Actors.ProtoActor).GetField("puppet", BindingFlags.Instance | BindingFlags.NonPublic);
                    var puppet = _puppetField?.GetValue(actor) as Component;
                    if (puppet != null)
                    {
                        foreach (var r in puppet.GetComponentsInChildren<Renderer>())
                        {
                            if (r == null || !r.enabled) continue;
                            if (itemT != null && r.transform.IsChildOf(itemT)) continue;
                            r.enabled = false;
                            _hiddenBody.Add(r);
                        }
                    }
                }

                // Character arms: an item that is not in the hand yet (it travels from the back when switching items) is hidden
                if (MimesisHands.CharacterArms && MimesisHands.HideItemInTransit && itemT != null && !MimesisHands.ItemFollowsHand(itemT))
                {
                    foreach (var r in itemT.GetComponentsInChildren<Renderer>())
                        if (r != null && r.enabled) { r.enabled = false; _hiddenBody.Add(r); }
                }
                // Character arms: the item always stays in its game socket and follows the hand (build 28)
                if (!MimesisHands.CharacterArms && ItemFollowsController && itemT != null && ComputeItemTarget(itemT, out Vector3 ipos, out Quaternion irot))
                {
                    _itemPos = itemT.position; _itemRot = itemT.rotation;
                    itemT.SetPositionAndRotation(ipos, irot);
                    _movedItem = itemT;
                }
            }
            catch (Exception ex) { VRModCore.LogError("[MimesisVR] PreEyes error:", ex); }
        }

        private static bool _bodyDumped;

        /// <summary>One-time dump of the local player's model: renderers and arm bones (for real hands / body).</summary>
        private static void DumpBody(Mimic.Actors.ProtoActor actor)
        {
            try
            {
                var sb = new StringBuilder("[MimesisVR] ===== PLAYER MODEL =====\n");
                _puppetField ??= typeof(Mimic.Actors.ProtoActor).GetField("puppet", BindingFlags.Instance | BindingFlags.NonPublic);
                var puppet = _puppetField?.GetValue(actor) as Component;
                sb.Append($"actor '{actor.name}' puppet '{(puppet != null ? puppet.name : "null")}'\n");
                Transform root = puppet != null ? puppet.transform : actor.transform;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var m = r.sharedMaterial;
                    sb.Append($"  renderer {r.transform.parent?.name}/{r.name} [{r.GetType().Name}] enabled={r.enabled} active={r.gameObject.activeInHierarchy} layer={LayerMask.LayerToName(r.gameObject.layer)} shadow={r.shadowCastingMode} forceOff={r.forceRenderingOff} mat={(m != null ? m.name : "-")} shader={(m != null && m.shader != null ? m.shader.name : "-")}");
                    if (r is SkinnedMeshRenderer sk) sb.Append($" bones={sk.bones?.Length} rootBone={(sk.rootBone != null ? sk.rootBone.name : "-")} updateOffscreen={sk.updateWhenOffscreen}");
                    sb.Append('\n');
                }
                int n = 0;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    string nm = t.name;
                    if (nm.IndexOf("Arm", StringComparison.OrdinalIgnoreCase) < 0 && nm.IndexOf("Hand", StringComparison.OrdinalIgnoreCase) < 0 && nm.IndexOf("Shoulder", StringComparison.OrdinalIgnoreCase) < 0
                        && nm.IndexOf("Clavicle", StringComparison.OrdinalIgnoreCase) < 0 && nm.IndexOf("Spine", StringComparison.OrdinalIgnoreCase) < 0 && nm.IndexOf("Neck", StringComparison.OrdinalIgnoreCase) < 0
                        && nm.IndexOf("Head", StringComparison.OrdinalIgnoreCase) < 0 && nm.IndexOf("Finger", StringComparison.OrdinalIgnoreCase) < 0 && nm.IndexOf("Thumb", StringComparison.OrdinalIgnoreCase) < 0
                        && nm.IndexOf("Index", StringComparison.OrdinalIgnoreCase) < 0 && nm.IndexOf("Socket", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var p = t; var path = nm; for (int i = 0; i < 3 && p.parent != null && p.parent != root; i++) { p = p.parent; path = p.name + "/" + path; }
                    sb.Append($"  bone {path} localPos={t.localPosition} localRot={t.localRotation.eulerAngles} scale={t.localScale}\n");
                    if (++n > 120) break;
                }
                var anim = root.GetComponentInChildren<Animator>(true);
                sb.Append($"  animator: {(anim != null ? anim.name + " humanoid=" + anim.isHuman + " avatar=" + (anim.avatar != null ? anim.avatar.name : "-") : "none")}\n");
                sb.Append("[MimesisVR] ===== END PLAYER MODEL =====");
                VRModCore.Log(sb.ToString());
            }
            catch (Exception ex) { VRModCore.LogWarning("[MimesisVR] Player model dump failed: " + ex.Message); }
        }

        public static void PostEyes()
        {
            RestoreCameraOverrides();
            try
            {
                // Item first (its saved pose was taken with the arms already posed), then the arms
                if (_movedItem != null) _movedItem.SetPositionAndRotation(_itemPos, _itemRot);
                foreach (var r in _hiddenBody) if (r != null) r.enabled = true;
            }
            catch (Exception ex) { VRModCore.LogError("[MimesisVR] PostEyes error:", ex); }
            _movedItem = null;
            _hiddenBody.Clear();
        }

        public static void DisableCameraOverrides()
        {
            foreach (var o in _overrides)
            {
                try { o.original = (bool)o.field.GetValue(o.camSettings); o.field.SetValue(o.camSettings, false); } catch { }
            }
        }

        public static void RestoreCameraOverrides()
        {
            foreach (var o in _overrides)
            {
                try { o.field.SetValue(o.camSettings, o.original); } catch { }
            }
        }
    }
}
