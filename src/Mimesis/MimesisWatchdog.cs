using System.Threading;
using UnityVRMod.Core;

namespace UnityVRMod.Mimesis
{
    /// <summary>
    /// MIMESIS VR: background watchdog. If the VR frame loop stops making progress it writes the current
    /// main-thread step and the render-thread (native helper) step to BepInEx\mimesisvr_watchdog.log
    /// (flushed immediately, so it survives a hang + forced close).
    /// </summary>
    internal static class MimesisWatchdog
    {
        private static volatile int _mainStage, _frames;
        private static volatile bool _armed;

        public static int MainStage
        {
            get => _mainStage;
            set { _mainStage = value; var s = RtSubmit.Shared; if (s != IntPtr.Zero) System.Runtime.InteropServices.Marshal.WriteInt32(s, 0, value); }
        }
        public static int Frames
        {
            get => _frames;
            set { _frames = value; var s = RtSubmit.Shared; if (s != IntPtr.Zero) System.Runtime.InteropServices.Marshal.WriteInt32(s, 4, value); }
        }
        public static bool Armed
        {
            get => _armed;
            set { _armed = value; var s = RtSubmit.Shared; if (s != IntPtr.Zero) System.Runtime.InteropServices.Marshal.WriteInt32(s, 12, value ? 1 : 0); }
        }

        private static Thread _thread;
        private static string _path;

        private static readonly string[] Names =
        {
            "idle", "wait previous submit", "xrWaitFrame", "xrBeginFrame", "locate views / input", "extras (panel, HUD)",
            "render left eye", "render right eye", "quad layers", "submit to render thread", "frame done"
        };

        public static void Start()
        {
            if (_thread != null) return;
            try
            {
                _path = Path.Combine(MvrPaths.DataDir, "mimesisvr_watchdog.log");
                Write($"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} watchdog started (MIMESIS VR {VRModCore.VERSION}) ===");
                _thread = new Thread(Loop) { IsBackground = true, Name = "MimesisVR Watchdog" };
                _thread.Start();
            }
            catch (Exception ex) { VRModCore.LogWarning("[MimesisVR] Watchdog not started: " + ex.Message); }
        }

        private static void Write(string line)
        {
            try { File.AppendAllText(_path, line + Environment.NewLine); } catch { }
        }

        private static void Loop()
        {
            int lastFrames = -1;
            DateTime lastChange = DateTime.UtcNow;
            bool reported = false;
            while (true)
            {
                Thread.Sleep(500);
                if (!Armed) { lastFrames = Frames; lastChange = DateTime.UtcNow; reported = false; continue; }
                int f = Frames;
                if (f != lastFrames)
                {
                    if (reported) Write($"{DateTime.Now:HH:mm:ss.fff} recovered after stall (frame {f}).");
                    lastFrames = f; lastChange = DateTime.UtcNow; reported = false;
                    continue;
                }
                double stall = (DateTime.UtcNow - lastChange).TotalSeconds;
                if (stall > 2.0 && !reported)
                {
                    int ms = MainStage;
                    string mn = ms >= 0 && ms < Names.Length ? Names[ms] : ms.ToString();
                    int ns = RtSubmit.NativeStage;
                    Write($"{DateTime.Now:HH:mm:ss.fff} STALL {stall:F1}s at VR frame {f}: main thread = [{ms}] {mn}; " +
                          $"render-thread helper stage = {DescribeNative(ns)}; pending = {RtSubmit.Pending}; native = {RtSubmit.NativeActive}");
                    reported = true;
                }
            }
        }

        private static string DescribeNative(int s)
        {
            if (s == 0) return "0 (not started / queued)";
            if (s == 1) return "1 (callback entered)";
            if (s >= 10 && s < 200)
            {
                int copy = (s - 10) / 10; int step = (s - 10) % 10;
                string st = step switch { 1 => "xrAcquireSwapchainImage", 2 => "xrWaitSwapchainImage", 3 => "CopyResource", 4 => "xrReleaseSwapchainImage", _ => "?" };
                return $"{s} (copy #{copy}: {st})";
            }
            if (s == 200) return "200 (xrEndFrame)";
            if (s == 300) return "300 (done)";
            return s.ToString();
        }
    }
}
