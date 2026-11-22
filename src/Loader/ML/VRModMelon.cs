#if ML
using MelonLoader;
using MelonLoader.Utils;
using UnityVRMod.Core;
using UnityVRMod.Loader;
using UnityVRMod.Loader.ML;

[assembly: MelonInfo(typeof(UnityVRMod.VRModMelon), "MIMESIS VR", UnityVRMod.Core.VRModCore.VERSION, "Palmmass")]
[assembly: MelonGame("ReLUGames", "MIMESIS")]
[assembly: MelonPriority(-100)]

#pragma warning disable IDE0130
namespace UnityVRMod
#pragma warning restore IDE0130
{
    /// <summary>
    /// Build 43: MelonLoader entry (the loader of the MIMESIS mod community). Files:
    ///   Mods\MimesisVR.dll                    - this mod
    ///   UserLibs\UniverseLib.Mono.dll         - helper library
    ///   UserData\MimesisVR\Native\*.dll       - openxr_loader, UnityGraphicsHelper, MimesisVRNative
    ///   UserData\MimesisVR\*.cfg              - settings (taken over once from BepInEx\config if there)
    /// </summary>
    public class VRModMelon : MelonMod, IVRModLoader
    {
        public static VRModMelon Instance { get; private set; }
        private FileConfigHandler _configHandler;

        public string InteropAssembliesPath => Path.Combine(MelonEnvironment.GameRootDirectory, "MelonLoader", "Il2CppAssemblies");
        public UnityVRMod.Config.ConfigHandler ConfigHandler => _configHandler;
        public Action<object> LogMessage => o => LoggerInstance.Msg(o?.ToString());
        public Action<object> LogWarning => o => LoggerInstance.Warning(o?.ToString());
        public Action<object> LogError => o => LoggerInstance.Error(o?.ToString());

        public override void OnInitializeMelon()
        {
            Instance = this;
            string data = Path.Combine(MelonEnvironment.UserDataDirectory, "MimesisVR");
            Directory.CreateDirectory(data);
            MvrPaths.LoaderName = "MelonLoader";
            MvrPaths.ConfigDir = data;
            MvrPaths.DataDir = data;
            MvrPaths.NativeDir = FindNativeDir(data);
            TakeOverBepInExSettings(data);
            _configHandler = new FileConfigHandler(Path.Combine(data, "com.newunitymodder.unityvrmod.cfg"));
            VRModCore.Init(this);
        }

        /// <summary>
        /// Build 50: folder with the native dlls (openxr_loader, UnityGraphicsHelper, MimesisVRNative). Manual install:
        /// UserData\MimesisVR\Native. A Thunderstore mod manager puts the package's UserData into
        /// UserData\<Team>-<Package>\, so UserData\*\MimesisVR\Native is searched too, then a Native folder next to the dll.
        /// </summary>
        private string FindNativeDir(string data)
        {
            const string probe = "openxr_loader.dll";
            var candidates = new List<string> { Path.Combine(data, "Native") };
            try
            {
                foreach (var d in Directory.GetDirectories(MelonEnvironment.UserDataDirectory))
                {
                    candidates.Add(Path.Combine(d, "MimesisVR", "Native"));
                    candidates.Add(Path.Combine(d, "Native"));
                }
            }
            catch (Exception ex) { LoggerInstance.Warning($"UserData scan: {ex.Message}"); }
            try
            {
                string own = Path.GetDirectoryName(typeof(VRModMelon).Assembly.Location);
                if (!string.IsNullOrEmpty(own))
                {
                    candidates.Add(own); // build 51: Gale (local import) puts every file of the package next to the mod dll
                    candidates.Add(Path.Combine(own, "Native"));
                    candidates.Add(Path.Combine(Path.GetDirectoryName(own) ?? own, "Native"));
                }
            }
            catch { }
            foreach (var c in candidates)
            {
                try
                {
                    if (File.Exists(Path.Combine(c, probe)))
                    {
                        if (c != candidates[0]) LoggerInstance.Msg($"Native files found in {c}");
                        return c;
                    }
                }
                catch { }
            }
            LoggerInstance.Warning($"Native files ({probe} etc.) not found - expected in {candidates[0]}. VR will not start.");
            return candidates[0];
        }

        /// <summary>First start under MelonLoader: copy the settings files from BepInEx\config (left in place).</summary>
        private void TakeOverBepInExSettings(string data)
        {
            string bie = Path.Combine(MelonEnvironment.GameRootDirectory, "BepInEx", "config");
            foreach (var n in new[] { "MimesisVR.cfg", "com.newunitymodder.unityvrmod.cfg", "Unity VR Mod.Internal.cfg" })
            {
                try
                {
                    string src = Path.Combine(bie, n), dst = Path.Combine(data, n);
                    if (File.Exists(dst) || !File.Exists(src)) continue;
                    File.Copy(src, dst);
                    LoggerInstance.Msg($"Settings taken over from BepInEx: {n}");
                }
                catch (Exception ex) { LoggerInstance.Warning($"Settings {n} not taken over: {ex.Message}"); }
            }
        }
    }
}
#endif
