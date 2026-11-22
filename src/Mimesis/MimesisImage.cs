using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityVRMod.Core;

namespace UnityVRMod.Mimesis
{
    /// <summary>
    /// MIMESIS VR: picture options - Beautify sharpen and lens dirt, volumetric fog (Buto), posterize, SSAO,
    /// and the game's frame-rate limit (VSync / target frame rate) while in VR. Applied about once a second
    /// so new scenes and the game's own settings changes are picked up. "As in game" restores the original.
    /// </summary>
    internal static class MimesisImage
    {
        public static int Sharpen;            // 0 as in game, 1 reduced, 2 off
        public static bool LensDirt = false; // off by default: the lens-dirt glow reads as dirt / grain in VR
        public static bool VolumetricFog = true;
        public static bool Posterize = true;
        public static bool Ssao = true;
        /// <summary>0 as in game, 1 headset paces (unlimited), 2 = 72, 3 = 60, 4 = 45 fps.</summary>
        public static int FrameLimit = 1;
        public static float RenderScale = 1f;
        public static float ShadowScale = 1f; // multiplies the URP shadow distance; 0 = no shadows in VR
        public static bool Decals = true;
        public static int ShadowRes;          // 0 as in game, 1 = 512, 2 = 1024, 3 = 2048
        public static int TextureLevel;       // 0 full, 1 half, 2 quarter
        public static float LodScale = 1f;
        public static bool LampShadows = true;
        public static float DrawDistance = 0f;   // metres for the eye cameras' far clip; 0 = as in game
        public static bool EyePost = true;       // post-processing (glow, colour grading) in the eyes   // shadows of lamps (additional lights); off = only the sun casts shadows
        public static int LightsPerObject;       // 0 as in game, 1 = 4, 2 = 2, 3 = 1 lamp per object
        private static int _origAddShadows = -1, _origPerObject = -1;
        private static bool _lightsLogged;
        /// <summary>Warning when a fixed frame limit does not match the headset refresh rate ("" if fine).</summary>
        private static float _warnHz; private static int _warnTarget;
        public static string FrameWarningEn => _warnHz > 0f ? $" WARNING: the headset runs at {_warnHz:F0} Hz, a {_warnTarget} limit does not match it - choose Headset or set the headset to {_warnTarget} Hz." : "";
        public static string FrameWarning => _warnHz > 0f ? MimesisLoc.TrF(" WARNING: the headset runs at {0} Hz, a {1} limit does not match it - choose Headset or set the headset to {1} Hz.",
            " ВНИМАНИЕ: шлем работает на {0} Гц, лимит {1} с ним не совпадает - выбери «По шлему» или поставь в шлеме {1} Гц.", _warnHz.ToString("F0"), _warnTarget) : "";
        private static int _origMainRes = -1, _origAddRes = -1, _origTex = -1;
        private static float _origLod = -1f;
        private static float _origShadowDist = -1f;

        private static float _next;
        private static readonly Dictionary<VolumeParameter, (object value, bool over)> _orig = new();
        private static readonly Dictionary<ScriptableRendererFeature, bool> _featOrig = new();
        private static bool _frameLogged;
        private static int _origVsync = -99, _origTarget = -99;

        public static void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            try { ApplyVolumes(); } catch (Exception ex) { VRModCore.LogWarning("[MimesisVR] Picture options (volumes): " + ex.Message); }
            try { ApplyFeatures(); } catch (Exception ex) { VRModCore.LogWarning("[MimesisVR] Picture options (features): " + ex.Message); }
            try { ApplyFrameRate(); } catch (Exception ex) { VRModCore.LogWarning("[MimesisVR] Frame rate option: " + ex.Message); }
        }

        public static void ApplyNow() { _next = 0f; }

        private static void ApplyVolumes()
        {
            foreach (var vol in UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (vol == null) continue;
                var prof = vol.HasInstantiatedProfile() ? vol.profile : vol.sharedProfile;
                if (prof == null) continue;
                foreach (var comp in prof.components)
                {
                    if (comp == null || comp.GetType().FullName != "Beautify.Universal.Beautify") continue;
                    SetFloat(comp, "sharpenIntensity", Sharpen == 0 ? (float?)null : Sharpen == 1 ? 0.3f : 0f, relative: Sharpen == 1);
                    SetFloat(comp, "lensDirtIntensity", LensDirt ? (float?)null : 0f, relative: false);
                }
            }
        }

        /// <summary>value null = restore original; relative = multiply the original.</summary>
        private static void SetFloat(VolumeComponent comp, string field, float? value, bool relative)
        {
            var f = comp.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public);
            if (!(f?.GetValue(comp) is VolumeParameter p)) return;
            var prop = p.GetType().GetProperty("value", BindingFlags.Instance | BindingFlags.Public);
            if (prop == null || prop.PropertyType != typeof(float)) return;
            if (value == null)
            {
                if (_orig.TryGetValue(p, out var o)) { prop.SetValue(p, o.value); p.overrideState = o.over; _orig.Remove(p); }
                return;
            }
            if (!_orig.ContainsKey(p)) _orig[p] = (prop.GetValue(p), p.overrideState);
            float baseVal = (float)_orig[p].value;
            float want = relative ? baseVal * value.Value : value.Value;
            if (!Mathf.Approximately((float)prop.GetValue(p), want) || !p.overrideState) { prop.SetValue(p, want); p.overrideState = true; }
        }

        private static void ApplyFeatures()
        {
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null) return;
            if (_origShadowDist < 0f) _origShadowDist = asset.shadowDistance;
            float sd = _origShadowDist * Mathf.Max(0f, ShadowScale);
            if (ShadowScale <= 0.001f) sd = 0f;
            if (!Mathf.Approximately(asset.shadowDistance, sd)) { asset.shadowDistance = sd; VRModCore.Log($"[MimesisVR] Shadow distance {sd:F0} (game {_origShadowDist:F0})"); }
            if (_origMainRes < 0) { _origMainRes = asset.mainLightShadowmapResolution; _origAddRes = asset.additionalLightsShadowmapResolution; }
            int res = ShadowRes == 1 ? 512 : ShadowRes == 2 ? 1024 : ShadowRes == 3 ? 2048 : -1;
            int wantMain = res > 0 ? Math.Min(res, _origMainRes) : _origMainRes, wantAdd = res > 0 ? Math.Min(res, _origAddRes) : _origAddRes;
            if (asset.mainLightShadowmapResolution != wantMain) { asset.mainLightShadowmapResolution = wantMain; VRModCore.Log($"[MimesisVR] Shadow map {wantMain} (game {_origMainRes})"); }
            if (asset.additionalLightsShadowmapResolution != wantAdd) asset.additionalLightsShadowmapResolution = wantAdd;
            if (_origTex < 0) { _origTex = QualitySettings.globalTextureMipmapLimit; _origLod = QualitySettings.lodBias; }
            int tex = Math.Max(_origTex, TextureLevel);
            if (QualitySettings.globalTextureMipmapLimit != tex) { QualitySettings.globalTextureMipmapLimit = tex; VRModCore.Log($"[MimesisVR] Texture mip limit {tex} (game {_origTex})"); }
            float lod = _origLod * LodScale;
            if (!Mathf.Approximately(QualitySettings.lodBias, lod)) QualitySettings.lodBias = lod;
            ApplyLights(asset);
            var listField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic);
            if (!(listField?.GetValue(asset) is ScriptableRendererData[] list)) return;
            foreach (var data in list)
            {
                if (data == null) continue;
                foreach (var f in data.rendererFeatures)
                {
                    if (f == null) continue;
                    string n = f.GetType().FullName ?? "";
                    bool? want = n.Contains("Buto") ? VolumetricFog : n.Contains("Posterize") ? Posterize : n.Contains("ScreenSpaceAmbientOcclusion") ? Ssao : n.Contains("DecalRendererFeature") ? Decals : (bool?)null;
                    if (want == null) continue;
                    if (!_featOrig.ContainsKey(f)) _featOrig[f] = f.isActive;
                    bool target = want.Value ? _featOrig[f] : false;
                    if (f.isActive != target) { f.SetActive(target); VRModCore.Log($"[MimesisVR] Renderer feature {f.name}: {(target ? "on" : "off")}"); }
                }
            }
        }

        private static void ApplyLights(UniversalRenderPipelineAsset asset)
        {
            const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var t = typeof(UniversalRenderPipelineAsset);
            var fShadows = t.GetField("m_AdditionalLightShadowsSupported", F);
            var fPerObj = t.GetField("m_AdditionalLightsPerObjectLimit", F);
            if (_origAddShadows < 0 && fShadows != null) _origAddShadows = (bool)fShadows.GetValue(asset) ? 1 : 0;
            if (_origPerObject < 0 && fPerObj != null) _origPerObject = (int)fPerObj.GetValue(asset);
            if (!_lightsLogged)
            {
                _lightsLogged = true;
                string mode = "?";
                try
                {
                    var listField = t.GetField("m_RendererDataList", F);
                    if (listField?.GetValue(asset) is ScriptableRendererData[] l && l.Length > 0 && l[0] is UniversalRendererData urd) mode = urd.renderingMode.ToString();
                }
                catch { }
                VRModCore.Log($"[MimesisVR] Lights in game settings: lamp shadows {(_origAddShadows == 1 ? "on" : "off")}, lamps per object {_origPerObject}, rendering path {mode}" +
                              (fShadows == null || fPerObj == null ? " (some fields not found: lamp options may not work)" : "") + ".");
            }
            if (fShadows != null && _origAddShadows >= 0)
            {
                bool want = LampShadows && _origAddShadows == 1;
                if ((bool)fShadows.GetValue(asset) != want) { fShadows.SetValue(asset, want); VRModCore.Log($"[MimesisVR] Lamp shadows {(want ? "on" : "off")}"); }
            }
            if (fPerObj != null && _origPerObject >= 0)
            {
                int lim = LightsPerObject == 1 ? 4 : LightsPerObject == 2 ? 2 : LightsPerObject == 3 ? 1 : _origPerObject;
                lim = Math.Min(lim, _origPerObject);
                if ((int)fPerObj.GetValue(asset) != lim) { fPerObj.SetValue(asset, lim); VRModCore.Log($"[MimesisVR] Lamps per object {lim} (game {_origPerObject})"); }
            }
        }

        private static bool _frApplied;
        private static string _frWarnLogged = "";

        private static void ApplyFrameRate()
        {
            bool want = FrameLimit != 0 && MimesisInput.VrActive;
            int target = FrameLimit == 2 ? 72 : FrameLimit == 3 ? 60 : FrameLimit == 4 ? 45 : -1;
            float hz = MimesisPerf.PeriodMs > 1f ? 1000f / MimesisPerf.PeriodMs : 0f;
            if (want && target > 0 && hz > 1f && Mathf.Abs(hz - target) > 1.5f && Mathf.Abs(hz - target * 2f) > 1.5f)
            {
                _warnHz = hz; _warnTarget = target;
            }
            else _warnHz = 0f;
            if (FrameWarningEn != _frWarnLogged) { _frWarnLogged = FrameWarningEn; if (FrameWarningEn != "") VRModCore.LogWarning("[MimesisVR]" + FrameWarningEn); }
            if (want)
            {
                if (QualitySettings.vSyncCount != 0 || Application.targetFrameRate != target)
                {
                    if (!_frApplied) { _origVsync = QualitySettings.vSyncCount; _origTarget = Application.targetFrameRate; }
                    VRModCore.Log($"[MimesisVR] Game frame limit in VR: VSync {QualitySettings.vSyncCount} -> 0, target frame rate {Application.targetFrameRate} -> {(target < 0 ? "unlimited (the headset paces frames)" : target.ToString())}.");
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = target;
                }
                _frApplied = true;
            }
            else if (_frApplied)
            {
                _frApplied = false;
                if (_origVsync != -99) { QualitySettings.vSyncCount = _origVsync; Application.targetFrameRate = _origTarget; }
                VRModCore.Log($"[MimesisVR] Game frame limit restored: VSync {_origVsync}, target frame rate {_origTarget}.");
            }
        }
    }
}
