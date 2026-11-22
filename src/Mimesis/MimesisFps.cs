namespace UnityVRMod.Mimesis
{
    /// <summary>
    /// MIMESIS VR: frames per second next to the wrist gear. Digits are drawn with a tiny pixel font into a small
    /// texture twice a second (no UI, practically free). Green - the headset rate is kept, yellow - a bit below,
    /// red - far below (reprojection / stutter).
    /// </summary>
    internal static class MimesisFps
    {
        public static bool Enabled = true;
        public static bool Active;
        public static RenderTexture Tex;
        public static Vector3 LocalPos;
        public static Quaternion LocalRot = Quaternion.identity;
        public static Vector2 Size = new(0.045f, 0.018f);

        private const int W = 40, H = 16;
        private static Texture2D _img;
        private static readonly Color32[] _px = new Color32[W * H];
        private static int _frames, _shown = -1;
        private static float _windowStart = -1f;

        // 3x5 digits, rows top to bottom, bits left to right
        private static readonly int[][] Font =
        {
            new[] { 7, 5, 5, 5, 7 }, new[] { 2, 6, 2, 2, 7 }, new[] { 7, 1, 7, 4, 7 }, new[] { 7, 1, 7, 1, 7 }, new[] { 5, 5, 7, 1, 1 },
            new[] { 7, 4, 7, 1, 7 }, new[] { 7, 4, 7, 5, 7 }, new[] { 7, 1, 2, 2, 2 }, new[] { 7, 5, 7, 5, 7 }, new[] { 7, 5, 7, 1, 7 },
        };

        /// <summary>Called once per frame with the gear pose (rig-local).</summary>
        public static void Update(bool gearActive, Vector3 gearPos, Quaternion gearRot, float gearSize)
        {
            Active = false;
            if (!Enabled || !gearActive) return;
            float now = Time.unscaledTime;
            if (_windowStart < 0f) _windowStart = now;
            _frames++;
            if (now - _windowStart >= 0.5f)
            {
                int fps = Mathf.RoundToInt(_frames / (now - _windowStart));
                _frames = 0; _windowStart = now;
                if (fps != _shown || Tex == null) { _shown = fps; Draw(fps); }
            }
            if (Tex == null) return;
            LocalRot = gearRot;
            LocalPos = gearPos + gearRot * new Vector3(gearSize * 0.5f + Size.x * 0.5f + 0.006f, 0f, 0f);
            Active = true;
        }

        private static void Draw(int fps)
        {
            if (_img == null) { _img = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point }; }
            // same format as the gear (matches the headset layer); premultiplied alpha, flipped like the gear
            if (Tex == null) { Tex = new RenderTexture(W * 4, H * 4, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.B8G8R8A8_SRGB) { filterMode = FilterMode.Point }; Tex.Create(); }
            float hz = MimesisPerf.PeriodMs > 1f ? 1000f / MimesisPerf.PeriodMs : 72f;
            float k = fps / Mathf.Max(1f, hz);
            Color32 fg = k >= 0.95f ? new Color32(110, 230, 120, 255) : k >= 0.7f ? new Color32(240, 210, 90, 255) : new Color32(240, 90, 80, 255);
            var bg = new Color32(7, 7, 8, 170); // premultiplied (colour * alpha)
            for (int i = 0; i < _px.Length; i++) _px[i] = bg;
            string t = Mathf.Clamp(fps, 0, 999).ToString();
            const int scale = 2, cw = 3 * scale, gap = scale;
            int total = t.Length * cw + (t.Length - 1) * gap;
            int x0 = (W - total) / 2, y0 = (H - 5 * scale) / 2;
            for (int c = 0; c < t.Length; c++)
            {
                var glyph = Font[t[c] - '0'];
                for (int row = 0; row < 5; row++)
                    for (int col = 0; col < 3; col++)
                    {
                        if ((glyph[row] & (4 >> col)) == 0) continue;
                        for (int sy = 0; sy < scale; sy++)
                            for (int sx = 0; sx < scale; sx++)
                            {
                                int x = x0 + c * (cw + gap) + col * scale + sx;
                                int y = H - 1 - (y0 + row * scale + sy); // texture rows go bottom-up
                                if (x >= 0 && x < W && y >= 0 && y < H) _px[y * W + x] = fg;
                            }
                    }
            }
            _img.SetPixels32(_px); _img.Apply(false);
            Graphics.Blit(_img, Tex, new Vector2(1f, -1f), new Vector2(0f, 1f));
        }
    }
}
