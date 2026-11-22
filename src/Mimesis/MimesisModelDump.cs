using System.IO;
using System.Text;
using UnityVRMod.Core;

namespace UnityVRMod.Mimesis
{
    /// <summary>
    /// MIMESIS VR (service): writes the model of the item in the right hand to BepInEx/MimesisVR_dump/&lt;item&gt;/ -
    /// a text report (renderers, meshes, materials, shaders, texture slots), the textures as PNG and, when the mesh is
    /// readable, the mesh as OBJ. Also lists loaded textures / materials whose names look useful for a wrist device.
    /// Used to pick the game's own textures for the wrist watch.
    /// </summary>
    internal static class MimesisModelDump
    {
        private static readonly string[] Keywords = { "watch", "compass", "device", "metal", "plastic", "rubber", "strap", "leather", "screen", "lcd", "radio", "walkie", "scanner", "detector", "gadget", "case", "band" };

        public static void DumpHeld()
        {
            try
            {
                var actor = MimesisInput.LocalActor;
                Transform item = null;
                try { var hi = actor?.GetHandheldItem(); if (hi != null) item = hi.Transform; } catch { }
                string root = Path.Combine(MvrPaths.DataDir, "MimesisVR_dump");
                Directory.CreateDirectory(root);
                var sb = new StringBuilder();
                if (item != null)
                {
                    string name = Safe(MimesisItemGrips.KeyOf(item));
                    string dir = Path.Combine(root, name);
                    Directory.CreateDirectory(dir);
                    sb.AppendLine($"Item '{item.name}' (key {name})");
                    var done = new HashSet<Texture>();
                    int ri = 0;
                    foreach (var r in item.GetComponentsInChildren<Renderer>(true))
                    {
                        Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                        sb.AppendLine($"Renderer #{ri} '{PathOf(r.transform, item)}' [{r.GetType().Name}] enabled={r.enabled} bounds size={r.bounds.size}");
                        if (mesh != null)
                        {
                            sb.AppendLine($"  mesh '{mesh.name}' vertices {mesh.vertexCount} submeshes {mesh.subMeshCount} readable {mesh.isReadable} local bounds {mesh.bounds.center} / {mesh.bounds.size}");
                            if (mesh.isReadable) { WriteObj(mesh, Path.Combine(dir, $"mesh{ri}_{Safe(mesh.name)}.obj")); sb.AppendLine("  -> OBJ written"); }
                        }
                        foreach (var m in r.sharedMaterials)
                        {
                            if (m == null) continue;
                            sb.AppendLine($"  material '{m.name}' shader '{m.shader?.name}' color {(m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").ToString() : m.HasProperty("_Color") ? m.color.ToString() : "-")}");
                            foreach (var p in m.GetTexturePropertyNames())
                            {
                                var t = m.GetTexture(p);
                                if (t == null) continue;
                                sb.AppendLine($"    {p}: '{t.name}' {t.width}x{t.height} {t.GetType().Name} scale {m.GetTextureScale(p)} offset {m.GetTextureOffset(p)}");
                                if (done.Add(t)) SavePng(t, Path.Combine(dir, $"{Safe(p)}_{Safe(t.name)}.png"));
                            }
                        }
                        ri++;
                    }
                    File.WriteAllText(Path.Combine(dir, "report.txt"), sb.ToString());
                    VRModCore.Log($"[MimesisVR] Item model written to {dir}:\n{sb}");
                }
                else VRModCore.Log("[MimesisVR] Item model: nothing in the right hand.");

                // useful-looking loaded assets (names only)
                var list = new StringBuilder("Loaded textures / materials with useful names:\n");
                int n = 0;
                foreach (var t in Resources.FindObjectsOfTypeAll<Texture2D>())
                {
                    if (t == null || !Match(t.name)) continue;
                    list.AppendLine($"  tex '{t.name}' {t.width}x{t.height}");
                    if (++n > 300) break;
                }
                n = 0;
                foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                {
                    if (m == null || !Match(m.name)) continue;
                    list.AppendLine($"  mat '{m.name}' shader '{m.shader?.name}'");
                    if (++n > 300) break;
                }
                File.WriteAllText(Path.Combine(root, "loaded_assets.txt"), list.ToString());
                VRModCore.Log("[MimesisVR] Loaded asset names written to " + Path.Combine(root, "loaded_assets.txt"));
            }
            catch (Exception ex) { VRModCore.LogError("[MimesisVR] Item model dump failed:", ex); }
        }

        private static bool Match(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string l = name.ToLowerInvariant();
            foreach (var k in Keywords) if (l.Contains(k)) return true;
            return false;
        }

        private static string PathOf(Transform t, Transform root)
        {
            var s = t.name;
            for (var p = t.parent; p != null && p != root.parent; p = p.parent) s = p.name + "/" + s;
            return s;
        }

        private static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "noname";
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }

        private static System.Reflection.MethodInfo _encode;

        private static void SavePng(Texture t, string file)
        {
            RenderTexture rt = null; var prev = RenderTexture.active;
            try
            {
                int w = Mathf.Min(t.width, 2048), h = Mathf.Min(t.height, 2048);
                rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(t, rt);
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply(false);
                _encode ??= Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")?.GetMethod("EncodeToPNG", new[] { typeof(Texture2D) });
                if (_encode == null) throw new Exception("ImageConversion.EncodeToPNG not found");
                File.WriteAllBytes(file, (byte[])_encode.Invoke(null, new object[] { tex }));
                UnityEngine.Object.Destroy(tex);
            }
            catch (Exception ex) { VRModCore.LogWarning($"[MimesisVR] Could not save texture '{t.name}': {ex.Message}"); }
            finally { RenderTexture.active = prev; if (rt != null) RenderTexture.ReleaseTemporary(rt); }
        }

        private static void WriteObj(Mesh m, string file)
        {
            var sb = new StringBuilder();
            foreach (var v in m.vertices) sb.AppendLine($"v {-v.x:F5} {v.y:F5} {v.z:F5}");
            var uv = m.uv;
            foreach (var u in uv) sb.AppendLine($"vt {u.x:F5} {u.y:F5}");
            foreach (var n in m.normals) sb.AppendLine($"vn {-n.x:F5} {n.y:F5} {n.z:F5}");
            bool hasUv = uv != null && uv.Length == m.vertexCount;
            for (int s = 0; s < m.subMeshCount; s++)
            {
                sb.AppendLine($"g sub{s}");
                var tr = m.GetTriangles(s);
                for (int i = 0; i + 2 < tr.Length; i += 3)
                {
                    int a = tr[i] + 1, b = tr[i + 2] + 1, c = tr[i + 1] + 1; // mirrored x -> flip winding
                    sb.AppendLine(hasUv ? $"f {a}/{a}/{a} {b}/{b}/{b} {c}/{c}/{c}" : $"f {a}//{a} {b}//{b} {c}//{c}");
                }
            }
            File.WriteAllText(file, sb.ToString());
        }
    }
}
