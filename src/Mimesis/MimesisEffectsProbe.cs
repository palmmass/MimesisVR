using System.Text;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityVRMod.Core;

namespace UnityVRMod.Mimesis
{
    /// <summary>
    /// MIMESIS VR: "mark" button in the service tab - writes a snapshot of everything that may draw over the view:
    /// objects attached to / right in front of the game camera, full-screen UI graphics, active post-process volumes
    /// and renderer features. Used to find the "dirty screen at 1 m" and the film grain in the dark.
    /// </summary>
    internal static class MimesisEffectsProbe
    {
        private static int _marks;
        public static int Marks => _marks;

        public static void Mark(string what)
        {
            _marks++;
            var sb = new StringBuilder();
            sb.Append($"[MimesisVR] ===== EFFECT MARK #{_marks}: {what} (t={Time.time:F1}) =====\n");
            try { Scenes(sb); } catch (Exception ex) { sb.Append("  scenes failed: ").Append(ex.Message).Append('\n'); }
            try { CameraObjects(sb); } catch (Exception ex) { sb.Append("  camera objects failed: ").Append(ex.Message).Append('\n'); }
            try { ScreenUi(sb); } catch (Exception ex) { sb.Append("  UI failed: ").Append(ex.Message).Append('\n'); }
            try { Volumes(sb); } catch (Exception ex) { sb.Append("  volumes failed: ").Append(ex.Message).Append('\n'); }
            try { Features(sb); } catch (Exception ex) { sb.Append("  features failed: ").Append(ex.Message).Append('\n'); }
            sb.Append($"[MimesisVR] ===== END MARK #{_marks} =====");
            VRModCore.Log(sb.ToString());
        }

        private static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (int i = 0; t != null && i < 8; i++, t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static void Scenes(StringBuilder sb)
        {
            sb.Append("  scenes: ");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                sb.Append(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).name).Append(", ");
            sb.Append('\n');
        }

        private static string Mat(Renderer r)
        {
            var m = r.sharedMaterial;
            if (m == null) return "no material";
            string tex = "";
            try { if (m.mainTexture != null) tex = " tex=" + m.mainTexture.name; } catch { }
            return $"mat={m.name} shader={(m.shader != null ? m.shader.name : "?")} queue={m.renderQueue}{tex}";
        }

        private static void CameraObjects(StringBuilder sb)
        {
            var gc = MimesisInput.GameCamera;
            if (gc == null) { sb.Append("  game camera: none\n"); return; }
            sb.Append($"  game camera '{PathOf(gc)}' at {gc.position}\n");
            sb.Append("  renderers attached to the camera (or its parents up to 2 levels):\n");
            var seen = new HashSet<int>();
            Transform root = gc;
            for (int i = 0; i < 2 && root.parent != null; i++) root = root.parent;
            int n = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (r.gameObject.layer == MimesisVrExtras.VrOnlyLayer) continue;
                if (!seen.Add(r.GetInstanceID())) continue;
                float d = Vector3.Distance(gc.position, r.bounds.center);
                if (d > 3f) continue;
                sb.Append($"    {PathOf(r.transform)} [{r.GetType().Name}] dist={d:F2} layer={LayerMask.LayerToName(r.gameObject.layer)} {Mat(r)}\n");
                if (++n >= 40) { sb.Append("    ...\n"); break; }
            }
            sb.Append("  other renderers within 1.5 m of the camera:\n");
            n = 0;
            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || seen.Contains(r.GetInstanceID())) continue;
                if (r.gameObject.layer == MimesisVrExtras.VrOnlyLayer) continue;
                float d = Vector3.Distance(gc.position, r.bounds.ClosestPoint(gc.position));
                if (d > 1.5f) continue;
                sb.Append($"    {PathOf(r.transform)} [{r.GetType().Name}] dist={d:F2} size={r.bounds.size} {Mat(r)}\n");
                if (++n >= 40) { sb.Append("    ...\n"); break; }
            }
        }

        private static void ScreenUi(StringBuilder sb)
        {
            sb.Append("  full-screen / effect UI graphics:\n");
            int n = 0;
            foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (c == null || !c.isRootCanvas || !c.isActiveAndEnabled || c.name.StartsWith("MimesisVR")) continue;
                var crt = c.transform as RectTransform;
                if (crt == null) continue;
                Vector2 cs = crt.rect.size;
                if (cs.x <= 1f || cs.y <= 1f) continue;
                foreach (var g in c.GetComponentsInChildren<Graphic>(false))
                {
                    if (g == null || !g.isActiveAndEnabled) continue;
                    float a = g.color.a * (g.canvasRenderer != null ? g.canvasRenderer.GetInheritedAlpha() : 1f);
                    if (a < 0.02f) continue;
                    Vector2 s = g.rectTransform.rect.size * (Vector2)g.rectTransform.lossyScale / Mathf.Max(1e-5f, crt.lossyScale.x);
                    bool big = s.x >= cs.x * 0.5f && s.y >= cs.y * 0.5f;
                    string path = PathOf(g.transform);
                    bool fx = path.IndexOf("Effect", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("Screen", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("Dirt", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("Noise", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!big && !fx) continue;
                    string src = g is Image img && img.sprite != null ? "sprite=" + img.sprite.name : g is RawImage ri && ri.texture != null ? "texture=" + ri.texture.name : "";
                    string mat = g.material != null ? $" mat={g.material.name} shader={(g.material.shader != null ? g.material.shader.name : "?")}" : "";
                    sb.Append($"    [{c.name} order={c.sortingOrder} mode={c.renderMode}] {path} [{g.GetType().Name}] size={s.x:0}x{s.y:0} of {cs.x:0}x{cs.y:0} color={g.color} alpha={a:F2} {src}{mat}\n");
                    if (++n >= 60) { sb.Append("    ...\n"); return; }
                }
            }
        }

        private static string ParamValue(VolumeParameter p)
        {
            try
            {
                var v = p.GetType().GetProperty("value", BindingFlags.Instance | BindingFlags.Public)?.GetValue(p);
                if (v is UnityEngine.Object uo) return uo != null ? $"[{uo.GetType().Name}] {uo.name}" : "null";
                return v?.ToString() ?? "null";
            }
            catch { return "?"; }
        }

        private static void Volumes(StringBuilder sb)
        {
            var gc = MimesisInput.GameCamera;
            sb.Append("  volumes:\n");
            foreach (var vol in UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (vol == null || !vol.isActiveAndEnabled || vol.weight <= 0f) continue;
                var prof = vol.HasInstantiatedProfile() ? vol.profile : vol.sharedProfile;
                if (prof == null) continue;
                string where = vol.isGlobal ? "global" : "local";
                if (!vol.isGlobal && gc != null)
                {
                    float best = float.MaxValue;
                    foreach (var col in vol.GetComponents<Collider>()) if (col != null && col.enabled) best = Mathf.Min(best, Vector3.Distance(col.ClosestPoint(gc.position), gc.position));
                    if (best > vol.blendDistance + 0.01f) continue; // camera not inside
                    where += $" (camera inside, d={best:F2})";
                }
                sb.Append($"    Volume '{PathOf(vol.transform)}' {where} prio={vol.priority} weight={vol.weight:F2} layer={LayerMask.LayerToName(vol.gameObject.layer)} profile={prof.name}\n");
                foreach (var comp in prof.components)
                {
                    if (comp == null || !comp.active) continue;
                    sb.Append($"      {comp.GetType().FullName}:");
                    foreach (var p in comp.parameters) if (p != null && p.overrideState) sb.Append(' ').Append(ParamName(comp, p)).Append('=').Append(ParamValue(p)).Append(';');
                    sb.Append('\n');
                }
            }
        }

        private static readonly Dictionary<Type, Dictionary<VolumeParameter, string>> _names = new();

        private static string ParamName(VolumeComponent comp, VolumeParameter p)
        {
            foreach (var f in comp.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                try { if (ReferenceEquals(f.GetValue(comp), p)) return f.Name; } catch { }
            }
            return "?";
        }

        private static void Features(StringBuilder sb)
        {
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null) return;
            var listField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic);
            if (!(listField?.GetValue(asset) is ScriptableRendererData[] list)) return;
            sb.Append("  active renderer features: ");
            foreach (var data in list)
            {
                if (data == null) continue;
                foreach (var f in data.rendererFeatures) if (f != null && f.isActive) sb.Append(data.name).Append('/').Append(f.name).Append(", ");
            }
            sb.Append('\n');
        }
    }
}
