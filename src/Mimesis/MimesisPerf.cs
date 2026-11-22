using UnityVRMod.Core;

namespace UnityVRMod.Mimesis
{
    /// <summary>
    /// MIMESIS VR: hitch logger. Every VR frame that takes much longer than the headset frame period is written
    /// to the log with where the time went (main thread / render thread / runtime / garbage collection),
    /// plus a summary every 30 seconds.
    /// </summary>
    internal static class MimesisPerf
    {
        private static double _last;
        private static int _gc;
        private static int _frames, _hitches;
        private static float _sum, _max, _nextSummary, _nextLog;
        private static int _suppressed;

        /// <summary>Time marks inside the VR frame (ms from its start): after the pre-eye work (HUD, settings, hands), after each eye.</summary>
        public static float TPre, TLeft, TRight;
        public static float PeriodMs;
        // Pre-eye parts: 0 misc (options, cheats, controllers), 1 HUD and panel, 2 mod settings; hands (onBeforeRender)
        private static readonly System.Diagnostics.Stopwatch _lap = new();
        private static readonly float[] _laps = new float[3];
        private static float _hands;
        private static double _lapLast;
        public static void LapStart() { _lap.Restart(); _lapLast = 0; }
        public static void Lap(int i) { double t = _lap.Elapsed.TotalMilliseconds; _laps[i] += (float)(t - _lapLast); _lapLast = t; }
        public static void AddHands(float ms) { _hands += ms; }
        private static Unity.Profiling.ProfilerRecorder[] _rec;
        private static readonly string[] _recNames = { "Batches Count", "SetPass Calls Count", "Draw Calls Count", "Shadow Casters Count", "Visible Skinned Meshes Count", "Main Thread", "Render Thread" };
        private static double[] _recSum;
        private static float _sGpu, _sCpu; private static int _nGpu;
        private static readonly FrameTiming[] _ft = new FrameTiming[1];
        private static float _sPre, _sL, _sR, _sPost, _sOut;

        private static string LightSummary()
        {
            try
            {
                int lamps = 0, shadowLamps = 0, pointShadow = 0, dirs = 0;
                foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (l == null || !l.isActiveAndEnabled || l.intensity <= 0f) continue;
                    if (l.type == LightType.Directional) { dirs++; continue; }
                    lamps++;
                    if (l.shadows != LightShadows.None) { shadowLamps++; if (l.type == LightType.Point) pointShadow++; }
                }
                return $"sun/directional {dirs}, lamps {lamps}, lamps with shadows {shadowLamps} (point {pointShadow}), lamp shadows option {(MimesisImage.LampShadows ? "on" : "off")}";
            }
            catch (Exception ex) { return "lights ?" + ex.Message; }
        }

        public static void FrameDone(float updatePosesMs, long displayPeriodNs)
        {
            float pre = TPre, left = Mathf.Max(0f, TLeft - TPre), right = Mathf.Max(0f, TRight - TLeft), post = Mathf.Max(0f, updatePosesMs - Mathf.Max(TRight, TPre));
            TPre = TLeft = TRight = 0f;
            double now = Time.realtimeSinceStartupAsDouble;
            int gc = GC.CollectionCount(0);
            bool gcRan = gc != _gc;
            _gc = gc;
            if (_last <= 0) { _last = now; _nextSummary = Time.unscaledTime + 30f; return; }
            float dt = (float)((now - _last) * 1000.0);
            _last = now;
            float period = displayPeriodNs > 0 ? displayPeriodNs / 1e6f : 11.1f;
            PeriodMs = period;
            try
            {
                if (_rec == null)
                {
                    _rec = new Unity.Profiling.ProfilerRecorder[_recNames.Length]; _recSum = new double[_recNames.Length];
                    for (int i = 0; i < _recNames.Length; i++)
                    {
                        var cat = i < 5 ? Unity.Profiling.ProfilerCategory.Render : Unity.Profiling.ProfilerCategory.Internal;
                        try { _rec[i] = Unity.Profiling.ProfilerRecorder.StartNew(cat, _recNames[i]); } catch { }
                    }
                }
                for (int i = 0; i < _rec.Length; i++) if (_rec[i].Valid) _recSum[i] += _rec[i].LastValue;
            }
            catch { }
            try
            {
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, _ft) > 0 && _ft[0].gpuFrameTime > 0) { _sGpu += (float)_ft[0].gpuFrameTime; _sCpu += (float)_ft[0].cpuMainThreadFrameTime; _nGpu++; }
            }
            catch { }
            _frames++; _sum += dt; if (dt > _max) _max = dt;
            _sPre += pre; _sL += left; _sR += right; _sPost += post; _sOut += Mathf.Max(0f, dt - updatePosesMs);

            if (dt > Mathf.Max(30f, period * 2.5f))
            {
                _hitches++;
                if (Time.unscaledTime >= _nextLog)
                {
                    _nextLog = Time.unscaledTime + 0.5f;
                    string extra = _suppressed > 0 ? $" (+{_suppressed} more since last line)" : "";
                    _suppressed = 0;
                    VRModCore.Log($"[MimesisVR] Hitch {dt:F0} ms at t={now:F1}s (headset period {period:F1} ms): main thread VR work {updatePosesMs:F1} ms (pre {pre:F1}, left eye {left:F1}, right eye {right:F1}, layers {post:F1}), game outside VR {Mathf.Max(0f, dt - updatePosesMs):F1} ms, " +
                                  $"wait for previous frame {RtSubmit.LastWaitMs:F1} ms; render thread: copies {RtSubmit.RtCopiesMs:F1}, xrEndFrame {RtSubmit.RtEndMs:F1}, " +
                                  $"xrWaitFrame {RtSubmit.RtWaitFrameMs:F1}, xrBeginFrame {RtSubmit.RtBeginMs:F1} ms; GC {(gcRan ? "YES" : "no")}; settings open {MimesisSettingsUI.Open}{extra}");
                }
                else _suppressed++;
            }

            if (Time.unscaledTime >= _nextSummary)
            {
                _nextSummary = Time.unscaledTime + 30f;
                float avg = _frames > 0 ? _sum / _frames : 0f;
                VRModCore.Log($"[MimesisVR] Perf 30s: {_frames} frames, avg {avg:F1} ms ({(avg > 0 ? 1000f / avg : 0):F0} fps), worst {_max:F0} ms, hitches {_hitches}; runtime '{RtSubmit.RuntimeName}', headset period {period:F1} ms; " +
                              $"avg per frame: pre-eye (HUD/settings/hands) {_sPre / Mathf.Max(1, _frames):F1}, left eye {_sL / Mathf.Max(1, _frames):F1}, right eye {_sR / Mathf.Max(1, _frames):F1}, layers {_sPost / Mathf.Max(1, _frames):F1}, game outside VR {_sOut / Mathf.Max(1, _frames):F1} ms; scene '{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}'");
                VRModCore.Log($"[MimesisVR] Perf 30s lights/GPU: {LightSummary()}; GPU per frame {(_nGpu > 0 ? (_sGpu / _nGpu).ToString("F1") + " ms" : "n/a")}, CPU main thread {(_nGpu > 0 ? (_sCpu / _nGpu).ToString("F1") + " ms" : "n/a")}");
                _sGpu = _sCpu = 0f; _nGpu = 0;
                int n = Mathf.Max(1, _frames);
                var sb = new System.Text.StringBuilder($"[MimesisVR] Perf 30s details: pre-eye parts - options/cheats {_laps[0] / n:F1}, HUD {_laps[1] / n:F1}, mod settings {_laps[2] / n:F1} ms; hands IK {_hands / n:F2} ms (outside VR); counters per frame:");
                for (int i = 0; _rec != null && i < _rec.Length; i++)
                {
                    if (!_rec[i].Valid) { sb.Append($" {_recNames[i]} n/a;"); continue; }
                    double v = _recSum[i] / n;
                    sb.Append(i >= 5 ? $" {_recNames[i]} {v / 1e6:F1} ms;" : $" {_recNames[i]} {v:F0};");
                    _recSum[i] = 0;
                }
                sb.Append($" options: draw distance {MimesisImage.DrawDistance:P0}, eye post {MimesisImage.EyePost}, HUD every {MimesisHud.UpdateEvery}, shadows {MimesisImage.ShadowScale:P0}, render scale {MimesisImage.RenderScale:P0}");
                VRModCore.Log(sb.ToString());
                _laps[0] = _laps[1] = _laps[2] = 0f; _hands = 0f;
                _sPre = _sL = _sR = _sPost = _sOut = 0f;
                _frames = 0; _sum = 0; _max = 0; _hitches = 0;
            }
        }
    }
}
