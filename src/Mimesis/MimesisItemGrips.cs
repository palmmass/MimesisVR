using System.Globalization;
using System.Linq;
using System.Text;
using UnityVRMod.Cfg;
using UnityVRMod.Core;

namespace UnityVRMod.Mimesis
{
    /// <summary>
    /// MIMESIS VR: per-item grip correction for the character hand (offset in metres and rotation in degrees, in
    /// controller axes). Keyed by the item's prefab name; saved as one line in MimesisVR.cfg, section [items].
    /// </summary>
    internal static class MimesisItemGrips
    {
        public struct Grip { public float X, Y, Z, Pitch, Yaw, Roll; }

        /// <summary>Item currently held in the right hand (set by MimesisHands every frame), null if none.</summary>
        public static string Current;

        private static readonly Dictionary<string, Grip> _map = new();
        private static ConfigEntry<string> _entry;
        private static bool _loaded;

        public static string KeyOf(Transform item)
        {
            if (item == null) return null;
            string n = item.name;
            int i = n.IndexOf("(Clone)", StringComparison.Ordinal);
            return (i >= 0 ? n.Substring(0, i) : n).Trim();
        }

        private static void Load()
        {
            if (_loaded) return;
            var cfg = MimesisSettings.Cfg;
            if (cfg == null) return;
            _loaded = true;
            try
            {
                _entry = cfg.Bind("items", "Grips", "", "Per-item grip correction for the character hand: name=x,y,z,pitch,yaw,roll;... (metres / degrees, controller axes)");
                foreach (var part in (_entry.Value ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = part.IndexOf('=');
                    if (eq <= 0) continue;
                    var v = part.Substring(eq + 1).Split(',');
                    if (v.Length < 6) continue;
                    float F(int k) => float.TryParse(v[k], NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0f;
                    _map[part.Substring(0, eq).Trim()] = new Grip { X = F(0), Y = F(1), Z = F(2), Pitch = F(3), Yaw = F(4), Roll = F(5) };
                }
                VRModCore.Log($"[MimesisVR] Item grips loaded: {_map.Count}.");
            }
            catch (Exception ex) { VRModCore.LogWarning("[MimesisVR] Item grips load failed: " + ex.Message); }
        }

        private static void Save()
        {
            if (_entry == null) return;
            var sb = new StringBuilder();
            foreach (var kv in _map)
            {
                var g = kv.Value;
                if (g.X == 0f && g.Y == 0f && g.Z == 0f && g.Pitch == 0f && g.Yaw == 0f && g.Roll == 0f) continue;
                sb.Append(kv.Key).Append('=').Append(string.Join(",", new[] { g.X, g.Y, g.Z, g.Pitch, g.Yaw, g.Roll }.Select(f => f.ToString("0.###", CultureInfo.InvariantCulture)))).Append(';');
            }
            _entry.Value = sb.ToString();
        }

        public static Grip Get(string key)
        {
            Load();
            return key != null && _map.TryGetValue(key, out var g) ? g : default;
        }

        /// <summary>Field of the current item's grip: 0..5 = X, Y, Z, pitch, yaw, roll.</summary>
        public static float Value(int field)
        {
            var g = Get(Current);
            return field switch { 0 => g.X, 1 => g.Y, 2 => g.Z, 3 => g.Pitch, 4 => g.Yaw, _ => g.Roll };
        }

        public static void SetValue(int field, float v)
        {
            Load();
            if (Current == null) { VRModCore.Log("[MimesisVR] Item grip: nothing in the right hand."); return; }
            var g = Get(Current);
            switch (field) { case 0: g.X = v; break; case 1: g.Y = v; break; case 2: g.Z = v; break; case 3: g.Pitch = v; break; case 4: g.Yaw = v; break; default: g.Roll = v; break; }
            _map[Current] = g;
            Save();
        }

        public static void ResetCurrent()
        {
            Load();
            if (Current == null) return;
            _map.Remove(Current);
            Save();
            VRModCore.Log($"[MimesisVR] Item grip reset: {Current}");
        }

        public static string Label(int lang) => Current == null
            ? MimesisLoc.Tr(" Nothing in the right hand now.", " Сейчас в правой руке ничего нет.")
            : MimesisLoc.TrF(" In hand now: {0}.", " Сейчас в руке: {0}.", Current);
    }
}
