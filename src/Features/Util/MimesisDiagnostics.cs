using System.Text;
using UnityEngine.Rendering.Universal;
using UnityVRMod.Core;

namespace UnityVRMod.Features.Util
{
    // MIMESIS VR: one-shot dump of the camera setup so the next iteration can target
    // hands/held-item cameras, layers and per-camera effects.
    internal static class MimesisDiagnostics
    {
        private static string Path(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        private static string Layers(int mask)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 32; i++)
                if ((mask & (1 << i)) != 0) sb.Append(i).Append(':').Append(LayerMask.LayerToName(i)).Append(' ');
            return sb.ToString();
        }

        internal static void DumpCameras(Camera main)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("[MimesisVR] ===== CAMERA DUMP =====");
                sb.Append("Layers: ");
                for (int i = 0; i < 32; i++) { var n = LayerMask.LayerToName(i); if (!string.IsNullOrEmpty(n)) sb.Append(i).Append('=').Append(n).Append(", "); }
                sb.AppendLine();
                if (main != null) sb.AppendLine($"Main: {Path(main.transform)} pos={main.transform.position} euler={main.transform.eulerAngles} fov={main.fieldOfView} near={main.nearClipPlane}");

                var cams = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var c in cams)
                {
                    if (c == null || c.name.StartsWith("XrVrCamera")) continue;
                    sb.AppendLine($"-- Camera '{Path(c.transform)}' active={c.gameObject.activeInHierarchy} enabled={c.enabled} depth={c.depth} clear={c.clearFlags} rt={(c.targetTexture != null ? c.targetTexture.name : "-")} fov={c.fieldOfView} near={c.nearClipPlane}");
                    sb.AppendLine($"   mask: {Layers(c.cullingMask)}");
                    var d = c.GetComponent<UniversalAdditionalCameraData>();
                    if (d != null)
                    {
                        sb.Append($"   urp: type={d.renderType} post={d.renderPostProcessing} volMask={Layers(d.volumeLayerMask.value)}");
                        if (d.cameraStack != null && d.cameraStack.Count > 0)
                        {
                            sb.Append(" stack=[");
                            foreach (var s in d.cameraStack) sb.Append(s != null ? s.name : "null").Append(", ");
                            sb.Append(']');
                        }
                        sb.AppendLine();
                    }
                    sb.Append("   comps: ");
                    foreach (var comp in c.GetComponents<Component>()) if (comp != null) sb.Append(comp.GetType().FullName).Append(", ");
                    sb.AppendLine();
                }

                var vols = UnityEngine.Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                sb.AppendLine($"Volumes: {vols.Length}");
                foreach (var v in vols)
                    sb.AppendLine($"   vol '{Path(v.transform)}' global={v.isGlobal} layer={LayerMask.LayerToName(v.gameObject.layer)} weight={v.weight} profile={(v.sharedProfile != null ? v.sharedProfile.name : "-")}");

                sb.AppendLine($"RenderSettings.fog={RenderSettings.fog} mode={RenderSettings.fogMode} density={RenderSettings.fogDensity} ambient={RenderSettings.ambientMode}");
                sb.AppendLine("[MimesisVR] ===== END DUMP =====");
                VRModCore.Log(sb.ToString());
            }
            catch (Exception ex) { VRModCore.LogError("[MimesisVR] DumpCameras failed:", ex); }
        }
    }
}
