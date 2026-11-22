using UnityVRMod.Core;

namespace UnityVRMod.Mimesis
{
    /// <summary>
    /// MIMESIS VR (build 45): the interface the player sees in the headset, for the monitor (stream).
    /// The headset gets the watch screens, the gear, the settings panel and the like as separate headset layers;
    /// the monitor picture is only the left eye plus the game interface. Here they are drawn on the monitor too:
    /// mode 1 - as a flat HUD in the corners (health bottom left, inventory bottom middle),
    /// mode 2 - where they are in the world, as the player sees them.
    /// </summary>
    internal static class MimesisStream
    {
        /// <summary>0 = off (as before build 45), 1 = corners of the screen, 2 = in the world.</summary>
        public static int Mode = 1;
        /// <summary>Corner pictures: height as a share of the monitor height.</summary>
        public static float CornerSize = 0.2f;

        private struct Quad { public RenderTexture Tex; public Vector3 Pos; public Quaternion Rot; public Vector2 Size; public int W, H; }
        private static readonly List<Quad> _quads = new();
        private static Material _mat;

        /// <summary>Corner mode wants the watch and wrist pictures even when the player does not look at them.</summary>
        public static bool WantCorners => Mode == 1 && MimesisVrExtras.Mirror && MimesisInput.VrActive;

        // copies made before the headset fade (corner mode shows them unfaded)
        public static RenderTexture WatchCopy, WristCopy;
        public static int WristW, WristH;
        public static float WatchAge = 99f, WristAge = 99f;

        public static void BeginFrame() => _quads.Clear();

        /// <summary>A headset layer that was submitted this frame (rig-local pose, texture in OpenXR orientation).</summary>
        public static void Add(RenderTexture tex, Vector3 pos, Quaternion rot, Vector2 size, int w = 0, int h = 0)
        {
            if (Mode != 2 || tex == null) return;
            _quads.Add(new Quad { Tex = tex, Pos = pos, Rot = rot, Size = size, W = w > 0 ? w : tex.width, H = h > 0 ? h : tex.height });
        }

        public static void CopyWatch(RenderTexture src)
        {
            if (!WantCorners || src == null || !src.IsCreated()) return;
            WatchCopy = Same(WatchCopy, src);
            Graphics.CopyTexture(src, WatchCopy);
            WatchAge = 0f;
        }

        public static void CopyWrist(RenderTexture src, int w, int h)
        {
            if (!WantCorners || src == null || !src.IsCreated()) return;
            WristCopy = Same(WristCopy, src);
            Graphics.CopyTexture(src, WristCopy);
            WristW = w; WristH = h; WristAge = 0f;
        }

        private static RenderTexture Same(RenderTexture rt, RenderTexture like)
        {
            if (rt != null && rt.width == like.width && rt.height == like.height && rt.graphicsFormat == like.graphicsFormat && rt.IsCreated()) return rt;
            if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
            rt = new RenderTexture(like.width, like.height, 0, like.graphicsFormat) { filterMode = FilterMode.Bilinear };
            rt.Create();
            return rt;
        }

        private static Material Mat()
        {
            if (_mat == null) _mat = new Material(Shader.Find("UI/Default")) { name = "MimesisVR_Stream" };
            return _mat;
        }

        /// <summary>Called by the monitor mirror after the eye picture and the game interface were drawn.</summary>
        public static void Draw(Camera eye, Transform rig, Vector2 blitScale)
        {
            if (Mode == 0) return;
            try
            {
                RenderTexture.active = null;
                GL.Clear(true, false, Color.clear); // depth only: the pictures go on top of the eye picture
                if (Mode == 1) DrawCorners();
                else if (eye != null && rig != null) DrawWorld(eye, rig, blitScale);
            }
            catch (Exception ex) { VRModCore.LogError("[MimesisVR] Stream interface failed, off:", ex); Mode = 0; }
            WatchAge += Time.unscaledDeltaTime; WristAge += Time.unscaledDeltaTime;
        }

        private static void DrawCorners()
        {
            float sh = Screen.height, sw = Screen.width, m = sh * 0.025f;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, sw, sh, 0);
            // health (right watch screen): bottom left; gone with the watch screen (death, menus) after a moment
            if (WatchCopy != null && WatchAge < 0.5f)
            {
                float h = sh * CornerSize, w = h * WatchCopy.width / Mathf.Max(1f, WatchCopy.height);
                Rect2D(WatchCopy, new Rect(m, sh - m - h, w, h), 1f, 1f);
            }
            // inventory (left wrist panel): bottom middle
            if (WristCopy != null && WristAge < 0.5f && WristW > 0 && WristH > 0)
            {
                float h = sh * CornerSize * 0.8f, w = h * WristW / (float)WristH;
                if (w > sw * 0.6f) { w = sw * 0.6f; h = w * WristH / (float)WristW; }
                Rect2D(WristCopy, new Rect((sw - w) * 0.5f, sh - m - h, w, h), (float)WristW / WristCopy.width, (float)WristH / WristCopy.height);
            }
            GL.PopMatrix();
        }

        /// <summary>Texture in OpenXR orientation (top row first): the top of the picture is at v = 0.</summary>
        private static void Rect2D(Texture tex, Rect r, float u1, float v1)
        {
            var mat = Mat();
            mat.mainTexture = tex;
            mat.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(Color.white);
            GL.TexCoord2(0f, 0f); GL.Vertex3(r.xMin, r.yMin, 0f);
            GL.TexCoord2(u1, 0f); GL.Vertex3(r.xMax, r.yMin, 0f);
            GL.TexCoord2(u1, v1); GL.Vertex3(r.xMax, r.yMax, 0f);
            GL.TexCoord2(0f, v1); GL.Vertex3(r.xMin, r.yMax, 0f);
            GL.End();
        }

        private static void DrawWorld(Camera eye, Transform rig, Vector2 blitScale)
        {
            if (_quads.Count == 0) return;
            // the mirror shows the middle part of the eye picture (scale < 1 on one axis): stretch the projection the same way
            var fit = Matrix4x4.Scale(new Vector3(1f / Mathf.Max(0.01f, blitScale.x), 1f / Mathf.Max(0.01f, blitScale.y), 1f));
            GL.PushMatrix();
            GL.LoadProjectionMatrix(fit * eye.projectionMatrix);
            GL.modelview = eye.worldToCameraMatrix * rig.localToWorldMatrix;
            var mat = Mat();
            foreach (var q in _quads)
            {
                if (q.Tex == null || !q.Tex.IsCreated()) continue;
                mat.mainTexture = q.Tex;
                mat.SetPass(0);
                float u1 = (float)q.W / q.Tex.width, v1 = (float)q.H / q.Tex.height;
                Vector3 r = q.Rot * new Vector3(q.Size.x * 0.5f, 0f, 0f), u = q.Rot * new Vector3(0f, q.Size.y * 0.5f, 0f);
                GL.Begin(GL.QUADS);
                GL.Color(Color.white);
                GL.TexCoord2(0f, 0f); GL.Vertex(q.Pos - r + u);
                GL.TexCoord2(u1, 0f); GL.Vertex(q.Pos + r + u);
                GL.TexCoord2(u1, v1); GL.Vertex(q.Pos + r - u);
                GL.TexCoord2(0f, v1); GL.Vertex(q.Pos - r - u);
                GL.End();
            }
            GL.PopMatrix();
        }
    }
}
