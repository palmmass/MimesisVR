#if ML
using UnityVRMod.Cfg;
using UnityVRMod.Config;
using UnityVRMod.Core;

namespace UnityVRMod.Loader.ML
{
    /// <summary>Build 43: main config of the VR core in the mod's own file (MelonLoader has no BepInEx ConfigFile).
    /// Same file name, section and format as under BepInEx, so a copied file just works.</summary>
    public class FileConfigHandler : ConfigHandler
    {
        private const string CONFIG_CATEGORY = "General";
        private ConfigFile _file;
        public string FilePath { get; }

        public FileConfigHandler(string path) { FilePath = path; }

        public override void Init()
        {
            try { _file = new ConfigFile(FilePath, false); }
            catch (Exception ex) { VRModCore.LogError($"Config file '{FilePath}' failed:", ex); }
        }

        public override void RegisterConfigElement<T>(ConfigElement<T> configElement)
        {
            if (_file == null) Init();
            if (_file == null) return;
            var entry = _file.Bind(CONFIG_CATEGORY, configElement.Name, (T)configElement.DefaultValue, configElement.Description);
            configElement.Value = entry.Value;
            entry.SettingChanged += (s, a) => configElement.Value = entry.Value;
        }

        public override T GetConfigValue<T>(ConfigElement<T> element)
            => _file != null && _file.TryGetEntry(CONFIG_CATEGORY, element.Name, out ConfigEntry<T> e) ? e.Value : element.Value;

        public override void SetConfigValue<T>(ConfigElement<T> element, T value)
        {
            if (_file != null && _file.TryGetEntry(CONFIG_CATEGORY, element.Name, out ConfigEntry<T> e)) e.Value = value;
        }

        public override void LoadConfig()
        {
            if (_file == null) return;
            foreach (var kv in ConfigManager.ConfigElements)
            {
                var def = new ConfigDefinition(CONFIG_CATEGORY, kv.Key);
                if (_file.ContainsKey(def))
                    try { kv.Value.BoxedValue = _file[def].BoxedValue; } catch (Exception ex) { VRModCore.LogError($"Config value {kv.Key}:", ex); }
            }
        }

        public override void SaveConfig() { try { _file?.Save(); } catch (Exception ex) { VRModCore.LogError("Config save failed:", ex); } }
    }
}
#endif
