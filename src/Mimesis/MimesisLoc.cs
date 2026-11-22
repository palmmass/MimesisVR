using System.IO;
using System.Text;
using UnityVRMod.Config;
using UnityVRMod.Core;

namespace UnityVRMod.Mimesis
{
    /// <summary>
    /// Build 48: languages of the mod's texts. English and Russian live in the code (pairs en/ru); other languages are
    /// files keyed by the English text: "@ English" line, then "= translation" line (\n = new line). Built-in: Korean.
    /// Files in UserData\MimesisVR\Lang\&lt;code&gt;.txt (code as the game's: ko, ja, zh_cn, de, ...) add a language or
    /// override built-in lines (also for en / ru). A missing or empty line falls back to English.
    /// </summary>
    internal static class MimesisLoc
    {
        public sealed class LangInfo { public string Code, Name; public Dictionary<string, string> Map = new(); }

        /// <summary>[0] en, [1] ru, [2] ko, then languages added by files.</summary>
        public static readonly List<LangInfo> Langs = new();
        /// <summary>Effective language (index in Langs).</summary>
        public static int Cur;
        /// <summary>"auto" (as the game) or a language code.</summary>
        public static string Choice = "auto";
        public static string Code => Langs.Count > Cur ? Langs[Cur].Code : "en";

        /// <summary>The game's languages (its own list), for files in the Lang folder.</summary>
        public static readonly string[] GameCodes = { "en", "ko", "es", "ja", "fr", "de", "zh_cn", "zh_tw", "pt_br", "it", "ru", "uk", "vi", "th", "tr", "pl", "ar", "da" };
        private static readonly Dictionary<string, string> _seen = new(); // runtime texts (en -> ru), for the template

        public static string Dir => Path.Combine(MvrPaths.ConfigDir, "Lang");

        public static void Init()
        {
            if (Langs.Count > 0) return;
            Langs.Add(new LangInfo { Code = "en", Name = "English" });
            Langs.Add(new LangInfo { Code = "ru", Name = "Русский" });
            var ko = new LangInfo { Code = "ko", Name = "한국어" };
            Parse(LangKo.Text, ko);
            Langs.Add(ko);
            try
            {
                if (!Directory.Exists(Dir)) return;
                foreach (var f in Directory.GetFiles(Dir, "*.txt"))
                {
                    string code = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                    if (Array.IndexOf(GameCodes, code) < 0) continue; // templates and other files
                    var li = Langs.Find(l => l.Code == code);
                    bool add = li == null;
                    li ??= new LangInfo { Code = code, Name = code };
                    int n = Parse(File.ReadAllText(f, Encoding.UTF8), li);
                    if (add) Langs.Add(li);
                    VRModCore.Log($"[MimesisVR] Language file {Path.GetFileName(f)}: {n} lines{(add ? " (new language)" : "")}.");
                }
            }
            catch (Exception ex) { VRModCore.LogWarning("[MimesisVR] Language files: " + ex.Message); }
        }

        /// <summary>Reads "@ key" / "= value" pairs into the language; "#name: ..." sets its display name.</summary>
        private static int Parse(string text, LangInfo li)
        {
            int n = 0; string key = null;
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.StartsWith("#name:")) { li.Name = line.Substring(6).Trim(); continue; }
                if (line.StartsWith("@ ")) { key = Unescape(line.Substring(2)); continue; }
                if (line.StartsWith("=") && key != null)
                {
                    string v = Unescape(line.Length > 1 && line[1] == ' ' ? line.Substring(2) : line.Substring(1));
                    if (v.Length > 0) { li.Map[key] = v; n++; }
                    key = null;
                }
            }
            return n;
        }

        private static string Unescape(string s) => s.Replace("\\n", "\n").Replace("\\\\", "\\");
        private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n");

        public static string Tr(string en, string ru)
        {
            if (en == null) return ru;
            var li = Langs.Count > Cur ? Langs[Cur] : null;
            if (!_seen.ContainsKey(en)) _seen[en] = ru;
            if (li != null && li.Map.TryGetValue(en, out var t)) return t;
            return Cur == 1 && ru != null ? ru : en;
        }

        public static string Tr(string[] pair) => pair == null || pair.Length == 0 ? "" : Tr(pair[0], pair.Length > 1 ? pair[1] : pair[0]);
        public static string TrF(string en, string ru, params object[] args)
        {
            try { return string.Format(Tr(en, ru), args); } catch { return string.Format(en, args); }
        }

        /// <summary>Language code the choice stands for ("auto": the game's language; Ukrainian game - Russian, as before).</summary>
        public static string Resolve(string choice)
        {
            if (choice != "auto") return choice;
            string g = "";
            try { g = PlayerPrefs.GetString("language", ""); } catch { }
            if (g.Length == 0)
            {
                try
                {
                    var sl = Application.systemLanguage;
                    g = sl == SystemLanguage.Russian || sl == SystemLanguage.Ukrainian ? "ru" : sl == SystemLanguage.Korean ? "ko" : "en";
                }
                catch { g = "en"; }
            }
            if (g == "uk") g = "ru";
            return g;
        }

        /// <summary>Re-reads the effective language; true when it changed.</summary>
        public static bool Refresh()
        {
            string code = Resolve(Choice);
            int i = Langs.FindIndex(l => l.Code == code);
            if (i < 0) i = 0;
            if (i == Cur) return false;
            Cur = i;
            return true;
        }

        /// <summary>Writes Lang\&lt;code&gt;.template.txt with every text of the mod (and the current lines of that language).</summary>
        public static string WriteTemplate()
        {
            var keys = new List<string>();
            var ruOf = new Dictionary<string, string>();
            void K(string en, string ru)
            {
                if (string.IsNullOrEmpty(en) || !System.Text.RegularExpressions.Regex.IsMatch(en, "[A-Za-z]{2,}") || ruOf.ContainsKey(en)) return;
                keys.Add(en); ruOf[en] = ru;
            }
            foreach (var t in MimesisSettings.Tabs) K(t.Label[0], t.Label[1]);
            foreach (var d in MimesisSettings.All)
            {
                K(d.Label[0], d.Label[1]); K(d.Desc[0], d.Desc[1]);
                if (d.Options != null && d.Values == null) for (int i = 0; i < d.Options[0].Length; i++) K(d.Options[0][i], d.Options[1][i]);
            }
            foreach (var kv in _seen) K(kv.Key, kv.Value);
            var li = Langs[Cur];
            var sb = new StringBuilder();
            sb.Append("# MIMESIS VR language file. Save as <code>.txt (e.g. ja.txt, de.txt, zh_cn.txt) in this folder and restart the game.\n");
            sb.Append("# Each text: \"@ English\" line, then \"= translation\" line. Empty or missing translation = English. \\n = new line.\n");
            sb.Append("#name: ").Append(li.Code == "en" ? "Language name" : li.Name).Append('\n');
            foreach (var k in keys)
            {
                string v = li.Map.TryGetValue(k, out var m) ? m : li.Code == "ru" ? ruOf[k] ?? "" : "";
                sb.Append("\n@ ").Append(Escape(k)).Append('\n');
                sb.Append("= ").Append(Escape(v)).Append('\n');
            }
            Directory.CreateDirectory(Dir);
            string path = Path.Combine(Dir, li.Code + ".template.txt");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            VRModCore.Log($"[MimesisVR] Language template written: {path} ({keys.Count} texts).");
            return path;
        }
    }
}
