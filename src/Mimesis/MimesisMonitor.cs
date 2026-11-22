using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityVRMod.Core;

namespace UnityVRMod.Mimesis
{
    /// <summary>
    /// MIMESIS VR: "monitor view off". While in VR the game's own camera keeps running (game logic, audio,
    /// Camera.main) but renders nothing: culling mask 0, no post-processing, no shadows, the light renderer,
    /// solid black. Its settings are switched only for the duration of its own render
    /// (beginCameraRendering/endCameraRendering), so the VR rig still copies the real settings.
    /// A placeholder text is drawn on the monitor (unless the headset mirror is on).
    /// </summary>
    internal static class MimesisMonitor
    {
        public static bool RenderMonitor = true;

        private static bool _hooked, _active;
        private static Camera _cam;
        private static int _mask;
        private static CameraClearFlags _clear;
        private static Color _bg;
        private static bool _post, _shadows;
        private static int _rendererIndex = -1;
        private static FieldInfo _rendererField;
        private static bool _logged;

        public static bool Off => !RenderMonitor && MimesisInput.VrActive;

        public static void Ensure()
        {
            if (_hooked) return;
            _hooked = true;
            RenderPipelineManager.beginCameraRendering += OnBegin;
            RenderPipelineManager.endCameraRendering += OnEnd;
            try
            {
                if (VRModBehaviour.Instance != null && VRModBehaviour.Instance.GetComponent<MimesisScreenStub>() == null)
                    VRModBehaviour.Instance.gameObject.AddComponent<MimesisScreenStub>();
            }
            catch (Exception ex) { VRModCore.LogWarning("[MimesisVR] Monitor placeholder unavailable: " + ex.Message); }
        }

        private static Camera GameCam()
        {
            var t = MimesisInput.GameCamera;
            return t != null ? t.GetComponent<Camera>() : null;
        }

        private static void OnBegin(ScriptableRenderContext ctx, Camera cam)
        {
            if (!Off || cam == null || cam != GameCam()) return;
            try
            {
                _cam = cam;
                _mask = cam.cullingMask; _clear = cam.clearFlags; _bg = cam.backgroundColor;
                cam.cullingMask = 0;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                // Only plain camera properties are changed here. Changing URP camera data (renderer, post, shadows)
                // inside this callback corrupted URP's per-frame state in build 21.
                _active = true;
                if (!_logged) { _logged = true; VRModCore.Log("[MimesisVR] Monitor view off: the game camera renders nothing while in VR."); }
            }
            catch (Exception ex) { VRModCore.LogError("[MimesisVR] Monitor-off failed, rendering the monitor again:", ex); RenderMonitor = true; }
        }

        private static void OnEnd(ScriptableRenderContext ctx, Camera cam)
        {
            if (!_active || cam != _cam) return;
            _active = false;
            try
            {
                cam.cullingMask = _mask; cam.clearFlags = _clear; cam.backgroundColor = _bg;
            }
            catch (Exception ex) { VRModCore.LogError("[MimesisVR] Monitor-off restore failed:", ex); }
        }

        private static int _lightIdx = -2;

        /// <summary>Index of a renderer without heavy features (the game's "debugRenderer"), or -1.</summary>
        private static int LightRendererIndex()
        {
            if (_lightIdx != -2) return _lightIdx;
            _lightIdx = -1;
            try
            {
                var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                var listField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic);
                if (asset != null && listField?.GetValue(asset) is ScriptableRendererData[] list)
                    for (int i = 0; i < list.Length; i++)
                        if (list[i] != null && list[i].name.IndexOf("debug", StringComparison.OrdinalIgnoreCase) >= 0) { _lightIdx = i; break; }
            }
            catch { }
            return _lightIdx;
        }
    }

    /// <summary>Text on the monitor while the monitor view is off.</summary>
    public class MimesisScreenStub : MonoBehaviour
    {
        private GUIStyle _style, _small;

        private void OnGUI()
        {
            if (!MimesisMonitor.Off || MimesisVrExtras.Mirror) return;
            if (Event.current.type != EventType.Repaint) return;
            _style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.Max(18, Screen.height / 22), normal = { textColor = new Color(0.93f, 0.9f, 0.8f) } };
            _small ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.Max(12, Screen.height / 50), normal = { textColor = new Color(0.6f, 0.6f, 0.58f) } };
            float w = Screen.width, h = Screen.height;
            GUI.Label(new Rect(0, h * 0.40f, w, h * 0.1f), MimesisLoc.Tr("MIMESIS VR - picture is in the headset", "MIMESIS VR - изображение в шлеме"), _style);
            GUI.Label(new Rect(0, h * 0.50f, w, h * 0.08f), MimesisLoc.Tr("Monitor view is off for speed. Turn on: wrist mod settings -> Performance, or F1 (headset view).",
                "Вывод на монитор отключен для скорости. Включить: настройки мода на запястье -> Производительность, или F1 (картинка шлема)."), _small);
        }
    }
}
