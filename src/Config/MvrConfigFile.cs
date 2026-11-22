using System.Linq;
using System.Globalization;
using System.Text;

namespace UnityVRMod.Cfg
{
    // Build 43: the mod's own settings store, independent of the loader (BepInEx or MelonLoader).
    // Same file format as BepInEx's .cfg ([Section], "## description", "Key = Value"), so the existing
    // BepInEx\config\*.cfg files are read as they are. Only the parts of the BepInEx API the mod uses.

    public readonly struct ConfigDefinition : IEquatable<ConfigDefinition>
    {
        public readonly string Section, Key;
        public ConfigDefinition(string section, string key) { Section = section ?? ""; Key = key ?? ""; }
        public bool Equals(ConfigDefinition o) => string.Equals(Section, o.Section, StringComparison.Ordinal) && string.Equals(Key, o.Key, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is ConfigDefinition d && Equals(d);
        public override int GetHashCode() => (Section.GetHashCode() * 397) ^ Key.GetHashCode();
        public override string ToString() => Section + "." + Key;
    }

    public abstract class ConfigEntryBase
    {
        public ConfigFile ConfigFile { get; }
        public ConfigDefinition Definition { get; }
        public string Description { get; }
        public Type SettingType { get; }
        public object DefaultValue { get; }
        public abstract object BoxedValue { get; set; }
        protected ConfigEntryBase(ConfigFile file, ConfigDefinition def, Type t, object dflt, string desc)
        { ConfigFile = file; Definition = def; SettingType = t; DefaultValue = dflt; Description = desc ?? ""; }
        internal string Serialize() => Conv.ToText(BoxedValue, SettingType);
        internal void SetFromText(string text)
        {
            if (Conv.TryParse(text, SettingType, out var v)) BoxedValue = v;
        }
    }

    public sealed class ConfigEntry<T> : ConfigEntryBase
    {
        private T _value;
        public event EventHandler SettingChanged;
        internal ConfigEntry(ConfigFile file, ConfigDefinition def, T dflt, string desc) : base(file, def, typeof(T), dflt, desc) { _value = dflt; }
        public T Value
        {
            get => _value;
            set
            {
                if (Equals(_value, value)) return;
                _value = value;
                SettingChanged?.Invoke(this, EventArgs.Empty);
                if (ConfigFile.SaveOnConfigSet) ConfigFile.Save();
            }
        }
        public override object BoxedValue { get => _value; set => Value = (T)value; }
    }

    public sealed class ConfigFile
    {
        private readonly Dictionary<ConfigDefinition, ConfigEntryBase> _entries = new();
        private readonly object _lock = new();
        public string ConfigFilePath { get; }
        public bool SaveOnConfigSet { get; set; } = true;
        /// <summary>Values in the file that no Bind asked for (yet) - used to carry renamed settings over.</summary>
        public Dictionary<ConfigDefinition, string> OrphanedEntries { get; } = new();

        public ConfigFile(string path, bool saveOnInit)
        {
            ConfigFilePath = path;
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)); } catch { }
            Reload();
            if (saveOnInit) Save();
        }

        public void Reload()
        {
            lock (_lock)
            {
                OrphanedEntries.Clear();
                if (!File.Exists(ConfigFilePath)) return;
                string section = "";
                foreach (var raw in File.ReadAllLines(ConfigFilePath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2).Trim(); continue; }
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    var def = new ConfigDefinition(section, line.Substring(0, eq).Trim());
                    string val = line.Substring(eq + 1).Trim();
                    if (_entries.TryGetValue(def, out var e)) e.SetFromText(val);
                    else OrphanedEntries[def] = val;
                }
            }
        }

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description = null)
        {
            lock (_lock)
            {
                var def = new ConfigDefinition(section, key);
                if (_entries.TryGetValue(def, out var existing)) return (ConfigEntry<T>)existing;
                var e = new ConfigEntry<T>(this, def, defaultValue, description);
                _entries[def] = e;
                if (OrphanedEntries.TryGetValue(def, out var text))
                {
                    bool save = SaveOnConfigSet; SaveOnConfigSet = false;
                    try { e.SetFromText(text); } finally { SaveOnConfigSet = save; }
                    OrphanedEntries.Remove(def);
                }
                if (SaveOnConfigSet) Save();
                return e;
            }
        }

        public bool TryGetEntry<T>(string section, string key, out ConfigEntry<T> entry)
        {
            lock (_lock)
            {
                entry = _entries.TryGetValue(new ConfigDefinition(section, key), out var e) ? e as ConfigEntry<T> : null;
                return entry != null;
            }
        }

        public bool ContainsKey(ConfigDefinition def) { lock (_lock) return _entries.ContainsKey(def); }
        public ConfigEntryBase this[ConfigDefinition def] { get { lock (_lock) return _entries[def]; } }

        public void Save()
        {
            lock (_lock)
            {
                var sb = new StringBuilder();
                sb.AppendLine("## Settings file of MIMESIS VR (same format as BepInEx .cfg)");
                sb.AppendLine();
                var sections = _entries.Values.Select(e => e.Definition.Section)
                    .Concat(OrphanedEntries.Keys.Select(k => k.Section)).Distinct().OrderBy(x => x, StringComparer.Ordinal);
                foreach (var sec in sections)
                {
                    sb.Append('[').Append(sec).AppendLine("]");
                    sb.AppendLine();
                    foreach (var e in _entries.Values.Where(x => x.Definition.Section == sec).OrderBy(x => x.Definition.Key, StringComparer.Ordinal))
                    {
                        foreach (var dl in e.Description.Split('\n')) if (dl.Trim().Length > 0) sb.Append("## ").AppendLine(dl.Trim());
                        sb.Append("# Setting type: ").AppendLine(e.SettingType.Name);
                        sb.Append("# Default value: ").AppendLine(Conv.ToText(e.DefaultValue, e.SettingType));
                        sb.Append(e.Definition.Key).Append(" = ").AppendLine(e.Serialize());
                        sb.AppendLine();
                    }
                    // values nobody bound (renamed settings, other builds): kept so nothing is lost
                    foreach (var kv in OrphanedEntries.Where(x => x.Key.Section == sec).OrderBy(x => x.Key.Key, StringComparer.Ordinal))
                    {
                        sb.Append(kv.Key.Key).Append(" = ").AppendLine(kv.Value);
                        sb.AppendLine();
                    }
                }
                try
                {
                    string tmp = ConfigFilePath + ".tmp";
                    File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                    if (File.Exists(ConfigFilePath)) File.Delete(ConfigFilePath);
                    File.Move(tmp, ConfigFilePath);
                }
                catch (Exception ex) { UnityVRMod.Core.VRModCore.LogWarning("[MimesisVR] Settings file not saved: " + ex.Message); }
            }
        }
    }

    internal static class Conv
    {
        public static string ToText(object v, Type t)
        {
            if (v == null) return "";
            if (t == typeof(string)) return Escape((string)v);
            if (t == typeof(bool)) return (bool)v ? "true" : "false";
            if (t == typeof(float)) return ((float)v).ToString("R", CultureInfo.InvariantCulture);
            if (t == typeof(double)) return ((double)v).ToString("R", CultureInfo.InvariantCulture);
            if (t.IsEnum) return v.ToString();
            if (v is IFormattable f) return f.ToString(null, CultureInfo.InvariantCulture);
            return v.ToString();
        }

        public static bool TryParse(string s, Type t, out object v)
        {
            v = null;
            try
            {
                if (t == typeof(string)) { v = Unescape(s); return true; }
                if (t == typeof(bool)) { v = bool.Parse(s); return true; }
                if (t == typeof(int)) { v = int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture); return true; }
                if (t == typeof(float)) { v = float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture); return true; }
                if (t == typeof(double)) { v = double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture); return true; }
                if (t.IsEnum) { v = Enum.Parse(t, s, true); return true; }
                v = Convert.ChangeType(s, t, CultureInfo.InvariantCulture); return true;
            }
            catch { return false; }
        }

        // BepInEx escapes strings the same way (backslash, quote, line breaks, tab)
        private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        private static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
                char n = s[++i];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    default: sb.Append(n); break;
                }
            }
            return sb.ToString();
        }
    }
}
