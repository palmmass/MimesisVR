using UnityEngine.UI;
using UnityVRMod.Core;

namespace UnityVRMod.Mimesis
{
    /// <summary>
    /// Build 47: the same settings panel on the monitor, without a headset (opened from the game's settings menu).
    /// The panel texture is shown on a screen-space overlay canvas over a dark backdrop; the mouse plays the pointer:
    /// left click = trigger, wheel = scroll, right click or a click outside the panel = close.
    /// Values only change the settings; graphics options are applied when VR runs.
    /// </summary>
    internal static partial class MimesisSettingsUI
    {
        public static bool FlatOpen { get; private set; }
        private static Canvas _flatCanvas;
        private static RawImage _flatImg;

        public static void Invalidate() { _dirty = true; }

        /// <summary>Called by the "VR" entry of the game's settings menu.</summary>
        public static void OpenFromGameMenu()
        {
            try
            {
                MimesisSettings.EnsureLoaded();
                var c = MimesisInput.Controllers;
                if (MimesisInput.VrActive && c != null && c.Ready)
                {
                    if (!Open) OpenPanel();
                    _holdUntilRelease = true;
                    return;
                }
                if (FlatOpen) return;
                FlatOpen = true;
                if (MimesisLoc.Refresh()) _dirty = true; // build 48: the game's language may have changed
                try
                {
                    string sig = SampleStyle();
                    if (sig != _styleSig) { _styleSig = sig; _dirty = true; }
                }
                catch (Exception ex) { VRModCore.LogWarning("[MimesisVR] Settings style sampling failed: " + ex.Message); }
                VRModCore.Log("[MimesisVR] Settings panel opened on the monitor.");
            }
            catch (Exception ex) { VRModCore.LogError("[MimesisVR] Opening the settings from the game menu failed:", ex); }
        }

        public static void CloseFlat()
        {
            if (!FlatOpen) return;
            FlatOpen = false;
            if (_flatCanvas != null) _flatCanvas.gameObject.SetActive(false);
            if (_hovered?.Bg != null) _hovered.Bg.color = _hovered.Normal;
            _hovered = null;
            try { MimesisSettings.SyncNow(); } catch (Exception ex) { VRModCore.LogWarning("[MimesisVR] Settings save failed: " + ex.Message); }
            VRModCore.Log("[MimesisVR] Settings panel on the monitor closed.");
        }

        /// <summary>Every frame (VRModCore.Update), with or without VR.</summary>
        public static void FlatUpdate()
        {
            if (!FlatOpen) return;
            try
            {
                var c = MimesisInput.Controllers;
                if (MimesisInput.VrActive && c != null && c.Ready) { CloseFlat(); return; } // the headset took over
                MimesisSettings.Sync();
                EnsureCanvas();
                if (_dirty) Rebuild();
                EnsureFlatCanvas();

                // centred, up to 92% of the screen, panel aspect
                float sw = Screen.width, sh = Screen.height;
                float scale = Mathf.Min(sw * 0.92f / W, sh * 0.92f / H);
                var size = new Vector2(W * scale, H * scale);
                _flatImg.rectTransform.sizeDelta = size;
                var origin = new Vector2((sw - size.x) * 0.5f, (sh - size.y) * 0.5f);
                Vector3 m = UniverseLib.Input.InputManager.MousePosition;
                var px = new Vector2((m.x - origin.x) / scale, (m.y - origin.y) / scale);
                bool onPanel = px.x >= 0f && px.y >= 0f && px.x <= W && px.y <= H;

                Hit hov = null;
                if (onPanel) foreach (var h in _hits) if (h.Rt != null && RectTransformUtility.RectangleContainsScreenPoint(h.Rt, px, _cam)) { hov = h; break; }
                if (hov != _hovered)
                {
                    if (_hovered?.Bg != null) _hovered.Bg.color = _hovered.Normal;
                    if (hov?.Bg != null) hov.Bg.color = hov.HoverCol;
                    _hovered = hov;
                }

                if (UniverseLib.Input.InputManager.GetMouseButtonDown(0))
                {
                    if (!onPanel) { CloseFlat(); return; }
                    if (hov?.Click != null) { hov.Click(); if (!FlatOpen) return; }
                }
                if (UniverseLib.Input.InputManager.GetMouseButtonDown(1)) { CloseFlat(); return; }
                float wheel = UniverseLib.Input.InputManager.MouseScrollDelta.y;
                if (onPanel && Mathf.Abs(wheel) > 0.01f) { _scroll[_tab] = ScrollOf(_tab) + (wheel > 0f ? -1 : 1); _dirty = true; }

                if (_dirty) { Rebuild(); _hovered = null; }
                RefreshValues();
                if (_cursor != null) _cursor.gameObject.SetActive(false); // the system mouse cursor is the pointer
                if (_forceCanvas) { _forceCanvas = false; Canvas.ForceUpdateCanvases(); }
                _cam.Render();
            }
            catch (Exception ex) { VRModCore.LogError("[MimesisVR] Settings panel on the monitor failed:", ex); CloseFlat(); _dirty = true; }
        }

        private static void EnsureFlatCanvas()
        {
            if (_flatCanvas == null)
            {
                var go = new GameObject("MimesisVR_FlatSettings");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _flatCanvas = go.AddComponent<Canvas>();
                _flatCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _flatCanvas.sortingOrder = 32000;
                go.AddComponent<GraphicRaycaster>(); // the backdrop takes the clicks, the game's menu below gets none

                var back = new GameObject("Backdrop").AddComponent<RawImage>();
                back.rectTransform.SetParent(go.transform, false);
                back.rectTransform.anchorMin = Vector2.zero; back.rectTransform.anchorMax = Vector2.one;
                back.rectTransform.offsetMin = Vector2.zero; back.rectTransform.offsetMax = Vector2.zero;
                back.color = new Color(0f, 0f, 0f, 0.6f);

                _flatImg = new GameObject("Panel").AddComponent<RawImage>();
                _flatImg.rectTransform.SetParent(go.transform, false);
                _flatImg.rectTransform.anchorMin = _flatImg.rectTransform.anchorMax = _flatImg.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                _flatImg.rectTransform.anchoredPosition = Vector2.zero;
                _flatImg.color = Color.white;
            }
            if (_flatImg.texture != _raw) _flatImg.texture = _raw;
            if (!_flatCanvas.gameObject.activeSelf) _flatCanvas.gameObject.SetActive(true);
        }
    }
}
