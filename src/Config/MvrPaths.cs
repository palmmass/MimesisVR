namespace UnityVRMod.Core
{
    /// <summary>Build 43: folders that depend on the loader (BepInEx or MelonLoader), set by the loader entry.</summary>
    public static class MvrPaths
    {
        /// <summary>Settings files (BepInEx\config or UserData\MimesisVR).</summary>
        public static string ConfigDir = "";
        /// <summary>Logs, dumps (BepInEx or UserData\MimesisVR).</summary>
        public static string DataDir = "";
        /// <summary>Native helpers: openxr_loader.dll, UnityGraphicsHelper.dll, MimesisVRNative.dll.</summary>
        public static string NativeDir = "";
        /// <summary>"BepInEx" or "MelonLoader".</summary>
        public static string LoaderName = "";

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        /// <summary>Loads the native helpers by full path, so [DllImport] finds them wherever the loader keeps mods.</summary>
        public static void PreloadNatives()
        {
            foreach (var n in new[] { "UnityGraphicsHelper.dll", "MimesisVRNative.dll" })
            {
                try
                {
                    string p = Path.Combine(NativeDir, n);
                    if (!File.Exists(p)) { VRModCore.LogWarning($"[MimesisVR] Native helper not found: {p}"); continue; }
                    if (LoadLibrary(p) == IntPtr.Zero) VRModCore.LogWarning($"[MimesisVR] Native helper not loaded ({System.Runtime.InteropServices.Marshal.GetLastWin32Error()}): {p}");
                }
                catch (Exception ex) { VRModCore.LogWarning($"[MimesisVR] Native helper {n}: {ex.Message}"); }
            }
        }
    }
}
