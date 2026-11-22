using System.Linq;
using UnityVRMod.Cfg;
using UnityVRMod.Core;

namespace UnityVRMod.Mimesis
{
    /// <summary>
    /// MIMESIS VR: mod settings (shown in the wrist settings panel), two languages, saved to BepInEx\config\MimesisVR.cfg.
    /// Every setting is a list of choices stored as an index; live values stay in the existing static fields,
    /// so hotkeys keep working and are picked up by a periodic sync.
    /// </summary>
    internal static class MimesisSettings
    {
        public enum Kind { Choice, Action }

        public sealed class Def
        {
            public string Id, Tab;
            public string[] Label = new string[2], Desc = new string[2];
            public Kind Kind;
            public string[][] Options; // [lang][index]
            public int Default;
            public Func<int> Get;
            public Action<int> Set;
            public Action Run;
            public ConfigEntry<int> Entry;
            public bool NoSave;
            public bool DebugOnly;      // build 47: shown only with "Debug tools" on
            public float[] Values;      // Num settings: value list (index is what is saved)
            public string OldId;        // Num settings: earlier id whose saved index is migrated by value
            public float[] OldValues;
            public int Count => Options != null ? Options[0].Length : 0;
        }

        public sealed class TabDef { public string Id; public string[] Label = new string[2]; public bool DebugOnly; }

        /// <summary>Build 47: diagnostics, the test tab (cheats) and developer hotkeys are available only with this on.</summary>
        public static bool DebugTools;
        public static bool Visible(Def d) => DebugTools || !d.DebugOnly;
        public static bool Visible(TabDef t) => DebugTools || !t.DebugOnly;
        public static void SyncNow() { _nextSync = 0f; Sync(); }

        /// <summary>Effective language: 0 = English, 1 = Russian, 2 = Korean, then languages from files (build 48: MimesisLoc).</summary>
        public static int Lang => MimesisLoc.Cur;
        public static float HeightOffset;

        public static readonly List<TabDef> Tabs = new();
        public static readonly List<Def> All = new();
        private static ConfigFile _cfg;
        internal static ConfigFile Cfg => _cfg;
        private static ConfigEntry<string> _langEntry;
        private static bool _loaded;
        private static float _nextSync;

        public static string T(string en, string ru) => MimesisLoc.Tr(en, ru);

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                MimesisLoc.Init();
                Define();
                ApplyTunedDefaults();
                _cfg = new ConfigFile(Path.Combine(MvrPaths.ConfigDir, "MimesisVR.cfg"), false);
                _cfg.SaveOnConfigSet = false; // one write after all settings are bound
                Dictionary<ConfigDefinition, string> orphans = null;
                try { orphans = typeof(ConfigFile).GetProperty("OrphanedEntries", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(_cfg) as Dictionary<ConfigDefinition, string>; } catch { }
                // build 48: language by code, "auto" = as the game; the old 0/1 setting (build 45) is carried over
                bool codeSaved = orphans != null && orphans.ContainsKey(new ConfigDefinition("General", "LanguageCode"));
                string oldLang = null;
                if (!codeSaved && orphans != null && orphans.TryGetValue(new ConfigDefinition("General", "Language"), out string ol)) oldLang = ol.Trim() == "1" ? "ru" : "en";
                _langEntry = _cfg.Bind("General", "LanguageCode", oldLang ?? "auto", "Mod texts language: auto (as the game), en, ru, ko or a code of a file in the Lang folder");
                MimesisLoc.Choice = string.IsNullOrWhiteSpace(_langEntry.Value) ? "auto" : _langEntry.Value.Trim();
                MimesisLoc.Refresh();
                if (oldLang != null) VRModCore.Log($"[MimesisVR] Language setting carried over: {oldLang}.");
                foreach (var d in All)
                {
                    if (d.Kind != Kind.Choice || d.NoSave) continue;
                    // Migration: the value list of this setting changed (new id) - carry the old choice over by value
                    int migrated = -1;
                    if (d.OldId != null && d.OldValues != null && orphans != null)
                    {
                        var newDef = new ConfigDefinition(d.Tab, d.Id);
                        // "tab/id" = a setting that moved from another tab (build 40: forearm panel -> left watch)
                        int sl = d.OldId.IndexOf('/');
                        var oldDef = sl > 0 ? new ConfigDefinition(d.OldId.Substring(0, sl), d.OldId.Substring(sl + 1)) : new ConfigDefinition(d.Tab, d.OldId);
                        if (!orphans.ContainsKey(newDef) && orphans.TryGetValue(oldDef, out string oldStr) && int.TryParse(oldStr, out int oldIdx))
                        {
                            float oldVal = d.OldValues[Mathf.Clamp(oldIdx, 0, d.OldValues.Length - 1)];
                            migrated = Nearest(d.Values, oldVal);
                            VRModCore.Log($"[MimesisVR] Setting {d.OldId} -> {d.Id}: kept value {oldVal}");
                        }
                    }
                    d.Entry = _cfg.Bind(d.Tab, d.Id, d.Default, d.Desc[0]);
                    if (migrated >= 0) d.Entry.Value = migrated;
                    int v = Mathf.Clamp(d.Entry.Value, 0, d.Count - 1);
                    try { d.Set(v); } catch (Exception ex) { VRModCore.LogWarning($"[MimesisVR] Setting {d.Id}: {ex.Message}"); }
                }
                _cfg.SaveOnConfigSet = true;
                _cfg.Save();
                VRModCore.Log($"[MimesisVR] Settings loaded from MimesisVR.cfg ({All.Count} items, language {MimesisLoc.Choice} -> {MimesisLoc.Code}, {MimesisLoc.Langs.Count} languages).");
                if (GameVersionDiffers) VRModCore.LogWarning($"[MimesisVR] Game version {Application.version}, the mod was tested with {TestedGameVersion}: something may not work after a game update.");
            }
            catch (Exception ex) { VRModCore.LogError("[MimesisVR] Settings load failed (defaults in use):", ex); }
        }

        /// <summary>Game version the mod was made and tested with (build 45: warning when it differs).</summary>
        public const string TestedGameVersion = "0.4.2";
        public static bool GameVersionDiffers => !string.IsNullOrEmpty(Application.version) && Application.version != TestedGameVersion;

        /// <summary>Language setting: 0 = as the game, then MimesisLoc.Langs in order.</summary>
        public static int LanguageIndex => MimesisLoc.Choice == "auto" ? 0 : Math.Max(0, MimesisLoc.Langs.FindIndex(l => l.Code == MimesisLoc.Choice)) + 1;

        public static void SetLanguage(int index)
        {
            MimesisLoc.Choice = index <= 0 || index > MimesisLoc.Langs.Count ? "auto" : MimesisLoc.Langs[index - 1].Code;
            if (_langEntry != null) _langEntry.Value = MimesisLoc.Choice;
            MimesisLoc.Refresh();
            MimesisSettingsUI.Invalidate();
        }

        public static void Change(Def d, int index)
        {
            if (d.Kind != Kind.Choice) return;
            index = ((index % d.Count) + d.Count) % d.Count;
            try { d.Set(index); } catch (Exception ex) { VRModCore.LogWarning($"[MimesisVR] Setting {d.Id}: {ex.Message}"); }
            if (d.Entry != null) d.Entry.Value = index;
            VRModCore.Log($"[MimesisVR] Setting {d.Id} = {d.Options[0][index]}");
        }

        public static int Current(Def d)
        {
            try { return Mathf.Clamp(d.Get(), 0, Math.Max(0, d.Count - 1)); } catch { return d.Default; }
        }

        public static void ResetAll()
        {
            foreach (var d in All) if (d.Kind == Kind.Choice && !d.NoSave) Change(d, d.Default);
        }

        /// <summary>Pick up changes made by hotkeys or other code (once a second).</summary>
        public static void Sync()
        {
            if (!_loaded || Time.unscaledTime < _nextSync) return;
            _nextSync = Time.unscaledTime + 1f;
            foreach (var d in All)
            {
                if (d.Entry == null) continue;
                int cur = Current(d);
                if (cur != d.Entry.Value) d.Entry.Value = cur;
            }
        }

        // ---------------------------------------------------------------- definitions
        private static void Tab(string id, string en, string ru) => Tabs.Add(new TabDef { Id = id, Label = new[] { en, ru } });

        private static Def Add(string tab, string id, string en, string ru, string descEn, string descRu)
        {
            var d = new Def { Id = id, Tab = tab, Label = new[] { en, ru }, Desc = new[] { descEn, descRu } };
            All.Add(d);
            return d;
        }

        private static readonly string[][] OnOff = { new[] { "Off", "On" }, new[] { "Выкл", "Вкл" } };

        private static void Bool(string tab, string id, string en, string ru, string descEn, string descRu, Func<bool> get, Action<bool> set, bool def)
        {
            var d = Add(tab, id, en, ru, descEn, descRu);
            d.Kind = Kind.Choice; d.Options = OnOff; d.Default = def ? 1 : 0;
            d.Get = () => get() ? 1 : 0; d.Set = i => set(i == 1);
        }

        private static void Enum(string tab, string id, string en, string ru, string descEn, string descRu, string[] optEn, string[] optRu, Func<int> get, Action<int> set, int def)
        {
            var d = Add(tab, id, en, ru, descEn, descRu);
            d.Kind = Kind.Choice; d.Options = new[] { optEn, optRu }; d.Default = def; d.Get = get; d.Set = set;
        }

        private static void Num(string tab, string id, string en, string ru, string descEn, string descRu, float[] values, Func<float, int, string> fmt, Func<float> get, Action<float> set, float def,
            string oldId = null, float[] oldValues = null)
        {
            var d = Add(tab, id, en, ru, descEn, descRu);
            d.Kind = Kind.Choice; d.Values = values; d.OldId = oldId; d.OldValues = oldValues;
            d.Options = new[] { values.Select(v => fmt(v, 0)).ToArray(), values.Select(v => fmt(v, 1)).ToArray() };
            d.Default = Nearest(values, def);
            d.Get = () => Nearest(values, get());
            d.Set = i => set(values[Mathf.Clamp(i, 0, values.Length - 1)]);
        }

        private static void Act(string tab, string id, string en, string ru, string descEn, string descRu, Action run)
        {
            var d = Add(tab, id, en, ru, descEn, descRu);
            d.Kind = Kind.Action; d.Run = run;
        }

        /// <summary>The author's settings as the defaults (22.11.2026, build 46; before: 10.11, build 35): list positions by
        /// setting, all settings of the file except the language. Settings that are not in the list keep the default written in Define().</summary>
        private static readonly Dictionary<string, int> TunedDefaults = new()
        {
            ["binds/B_Drop"] = 2,
            ["binds/B_Emote"] = 4,
            ["binds/B_Fire"] = 8,
            ["binds/B_Fire2"] = 7,
            ["binds/B_Interact"] = 10,
            ["binds/B_Jump"] = 1,
            ["binds/B_Reload"] = 6,
            ["binds/B_Run"] = 5,
            ["binds/B_Status"] = 3,
            ["binds/B_Tram"] = 0,
            ["binds/B_Voice"] = 9,
            ["binds/EmoteView"] = 0,
            ["binds/Hints"] = 1,
            ["binds/SwapSticks"] = 0,
            ["binds/WheelBg"] = 7,
            ["binds/WheelReach"] = 4,
            ["comfort/ArmThickness"] = 15,
            ["comfort/BodyAutoLift"] = 1,
            ["comfort/BodyForward"] = 0,
            ["comfort/BodyTilt"] = 22,
            ["comfort/BodyUp"] = 50,
            ["comfort/DamageBuzz"] = 1,
            ["comfort/DamageFlash"] = 1,
            ["comfort/FingerBend"] = 0,
            ["comfort/FingerMax"] = 14,
            ["comfort/FingerRest3"] = 17,
            ["comfort/FingerSpread"] = 14,
            ["comfort/FingerSqueeze"] = 12,
            ["comfort/ForearmTwist"] = 20,
            ["comfort/GripTilt3"] = 46,
            ["comfort/GripX3"] = 25,
            ["comfort/GripY3"] = 25,
            ["comfort/GripZ3"] = 30,
            ["comfort/HandOffset3"] = 10,
            ["comfort/HandPitch3"] = 36,
            ["comfort/HandRoll3"] = 36,
            ["comfort/HandSize"] = 12,
            ["comfort/HandYaw3"] = 36,
            ["comfort/HandsWalls"] = 1,
            ["comfort/Height2"] = 12,
            ["comfort/LegsForward"] = 30,
            ["comfort/LowerLength"] = 24,
            ["comfort/ShoulderAnchor"] = 1,
            ["comfort/ShoulderForward2"] = 50,
            ["comfort/ShoulderOut2"] = 60,
            ["comfort/ShoulderSmooth"] = 5,
            ["comfort/ShoulderUp2"] = 60,
            ["comfort/Spreadindex"] = 35,
            ["comfort/Spreadlittle"] = 40,
            ["comfort/Spreadmiddle"] = 28,
            ["comfort/Spreadring"] = 40,
            ["comfort/TorsoUpright"] = 3,
            ["comfort/TouchFingers"] = 1,
            ["comfort/UpperLength"] = 25,
            ["controls/Deadzone2"] = 3,
            ["controls/JumpLift"] = 6,
            ["controls/NoPitchShift"] = 1,
            ["controls/RoomScale"] = 1,
            ["controls/RoomWalkAnim"] = 1,
            ["controls/RunMode"] = 0,
            ["controls/SmoothSpeed2"] = 7,
            ["controls/SnapAngle2"] = 8,
            ["controls/TurnMode"] = 0,
            ["controls/WalkDirection"] = 0,
            ["hands/ActionDir"] = 0,
            ["hands/BeamLength"] = 10,
            ["hands/BeamOpacity2"] = 2,
            ["hands/FingerBase"] = 0,
            ["hands/GripFingers"] = 0,
            ["hands/Hands"] = 1,
            ["hands/HideItemInTransit"] = 1,
            ["hands/HoldMode"] = 0,
            ["hands/InteractMode"] = 0,
            ["hands/ItemFollowsController"] = 1,
            ["hands/PointerStyle"] = 0,
            ["hands/ShowBody"] = 1,
            ["hands/ShowControllers"] = 1,
            ["hands/SocketBase"] = 0,
            ["hands/SwingHaptics"] = 1,
            ["hands/SwingMode"] = 2,
            ["hands/SwingThreshold"] = 22,
            ["hands/VibrationFreq"] = 0,
            ["perf/Decals"] = 1,
            ["perf/DrawDist2"] = 0,
            ["perf/EyePost"] = 1,
            ["perf/FrameLimit"] = 1,
            ["perf/HudEvery"] = 0,
            ["perf/LampShadows"] = 1,
            ["perf/LodDetail2"] = 11,
            ["perf/MonitorView"] = 1,
            ["perf/Msaa"] = 2,
            ["perf/RenderScale2"] = 12,
            ["perf/ShadowRes"] = 0,
            ["perf/Shadows2"] = 20,
            ["perf/Ssao"] = 1,
            ["perf/Textures"] = 0,
            ["perf/VolumetricFog"] = 1,
            ["service/FlipMode"] = 0,
            ["service/MenuPanelMode"] = 0,
            ["test/TestForAll"] = 0,
            ["ui/DarkScreens2"] = 0,
            ["ui/FpsCounter"] = 0,
            ["ui/HintSize2"] = 3,
            ["ui/Hud"] = 1,
            ["ui/HudDistance3"] = 8,
            ["ui/HudOnHands"] = 1,
            ["ui/HudScale3"] = 14,
            ["ui/InvTilt3"] = 40,
            ["ui/InvX3"] = 40,
            ["ui/InvY3"] = 40,
            ["ui/InvZ3"] = 45,
            ["ui/MenuBackdrop2"] = 10,
            ["ui/MenuDistance3"] = 6,
            ["ui/TipsPlace"] = 0,
            ["ui/TipsRBPlace"] = 0,
            ["ui/TipsScale"] = 14,
            ["ui/TipsTilt"] = 18,
            ["ui/TipsX"] = 33,
            ["ui/TipsY"] = 15,
            ["ui/TipsZ"] = 30,
            ["ui/WristPanel"] = 1,
            ["ui/WristPanelIdle"] = 3,
            ["ui/WristPanelMount"] = 1,
            ["ui/WristPanelShow"] = 0,
            ["ui/WristPlace"] = 0,
            ["ui/WristSize3"] = 9,
            ["video/LensDirt"] = 0,
            ["video/LensRain"] = 0,
            ["video/Mirror"] = 1,
            ["video/MirrorEye"] = 1,
            ["video/Posterize"] = 1,
            ["video/ScreenFx"] = 0,
            ["video/Sharpen"] = 0,
            ["video/StreamSize"] = 4,
            ["video/StreamUi"] = 2,
            ["video/UiNoise"] = 0,
            ["watchL/Across2"] = 42,
            ["watchL/Along2"] = 250,
            ["watchL/BagRow"] = 1,
            ["watchL/Bank2"] = 88,
            ["watchL/BgAlpha"] = 10,
            ["watchL/Dial"] = 10,
            ["watchL/FaceEyes"] = 0,
            ["watchL/Fit"] = 0,
            ["watchL/Height2"] = 50,
            ["watchL/Length"] = 35,
            ["watchL/Model"] = 0,
            ["watchL/Roll2"] = 188,
            ["watchL/SAcross2"] = 150,
            ["watchL/SAlong2"] = 155,
            ["watchL/SBank2"] = 178,
            ["watchL/SHeight"] = 37,
            ["watchL/SRoll2"] = 270,
            ["watchL/SScale"] = 12,
            ["watchL/STilt2"] = 180,
            ["watchL/Shadow"] = 0,
            ["watchL/Thick"] = 21,
            ["watchL/Tilt2"] = 226,
            ["watchL/TwistFollow"] = 20,
            ["watchL/Width"] = 23,
            ["watchR/Across2"] = 250,
            ["watchR/Along2"] = 331,
            ["watchR/Bank2"] = 272,
            ["watchR/BgAlpha"] = 10,
            ["watchR/Dial"] = 10,
            ["watchR/FaceEyes"] = 0,
            ["watchR/Fit"] = 0,
            ["watchR/GearPlace"] = 5,
            ["watchR/Height2"] = 55,
            ["watchR/Length"] = 17,
            ["watchR/Model"] = 0,
            ["watchR/Roll2"] = 162,
            ["watchR/SAcross2"] = 150,
            ["watchR/SAlong2"] = 150,
            ["watchR/SBank2"] = 180,
            ["watchR/SHeight"] = 35,
            ["watchR/SRoll2"] = 90,
            ["watchR/SScale"] = 28,
            ["watchR/STilt2"] = 180,
            ["watchR/Shadow"] = 0,
            ["watchR/StatusPlace2"] = 0,
            ["watchR/Thick"] = 13,
            ["watchR/Tilt2"] = 232,
            ["watchR/TwistFollow"] = 20,
            ["watchR/Width"] = 16
        };

        private static void ApplyTunedDefaults()
        {
            int n = 0;
            foreach (var d in All)
            {
                if (d.Kind != Kind.Choice || d.Options == null) continue;
                if (!TunedDefaults.TryGetValue(d.Tab + "/" + d.Id, out int i)) continue;
                d.Default = Mathf.Clamp(i, 0, d.Options[0].Length - 1); n++;
            }
            VRModCore.Log($"[MimesisVR] Tuned defaults applied to {n} settings.");
        }

        private static int Nearest(float[] values, float v)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < values.Length; i++) { float dd = Mathf.Abs(values[i] - v); if (dd < bd) { bd = dd; best = i; } }
            return best;
        }

        /// <summary>Values from..to with a step (rounded, includes both ends).</summary>
        private static float[] R(float from, float to, float step)
        {
            var l = new List<float>();
            for (int i = 0; ; i++) { float v = (float)Math.Round(from + step * i, 4); if (v > to + step * 0.01f) break; l.Add(v); }
            return l.ToArray();
        }
        private static float[] Join(params float[] v) => v.Distinct().OrderBy(x => x).ToArray();

        private static string Deg(float v, int l) => $"{v:0}°";
        private static string DegS(float v, int l) => l == 1 ? $"{v:0}°/с" : $"{v:0}°/s";
        private static string Pct(float v, int l) => $"{v * 100f:0}%";
        private static string Cm(float v, int l) => l == 1 ? $"{v * 100f:0} см" : $"{v * 100f:0} cm";
        private static string M(float v, int l) => l == 1 ? $"{v:0.##} м" : $"{v:0.##} m";
        private static string SignedCm1(float v, int l) => (v > 0 ? "+" : "") + (l == 1 ? $"{v * 100f:0.0} см" : $"{v * 100f:0.0} cm");
        private static string SignedCm(float v, int l) => (v > 0 ? "+" : "") + (l == 1 ? $"{v * 100f:0} см" : $"{v * 100f:0} cm");
        private static string X(float v, int l) => $"x{v:0.##}";
        private static string Mm(float v, int l) => l == 1 ? $"{v * 1000f:0} мм" : $"{v * 1000f:0} mm";
        private static string SignedMm(float v, int l) => (v > 0 ? "+" : "") + (l == 1 ? $"{v * 1000f:0} мм" : $"{v * 1000f:0} mm");

        private static void Define()
        {
            Tab("controls", "Controls", "Управление");
            Tab("binds", "Buttons", "Кнопки");
            Tab("hands", "Hands & items", "Руки и предметы");
            Tab("ui", "Interface", "Интерфейс");
            Tab("watchL", "Left watch", "Левые часы");
            Tab("watchR", "Right watch", "Правые часы");
            Tab("video", "Graphics", "Изображение");
            Tab("perf", "Performance", "Производительность");
            Tab("comfort", "Comfort", "Комфорт");
            Tab("service", "Service", "Служебное");
            Tab("test", "Test (host)", "Тест (хост)");
            Tabs[Tabs.Count - 1].DebugOnly = true;

            // ---- Controls
            Enum("controls", "TurnMode", "Turning", "Поворот",
                "How the right stick turns you.", "Как правый стик поворачивает персонажа.",
                new[] { "Snap", "Smooth" }, new[] { "Рывками", "Плавно" },
                () => MimesisInput.SmoothTurn ? 1 : 0, i => MimesisInput.SmoothTurn = i == 1, 0);
            Bool("controls", "NoPitchShift", "Looking down does not move the view", "Наклон головы не сдвигает обзор",
                "The game moves its camera forward and down when you look down (made for the flat screen to show the feet). In VR that shifts the whole view and spins it when you look straight down. On: the view moves only with your head.",
                "Игра сдвигает камеру вперед и вниз, когда смотришь вниз (для плоского экрана, чтобы были видны ноги). В VR это сдвигает весь обзор и крутит его, когда смотришь прямо вниз. Вкл: обзор двигается только вместе с головой.",
                () => MimesisActions.NoPitchShift, v => MimesisActions.NoPitchShift = v, true);
            Num("controls", "JumpLift", "Jump: view lift", "Прыжок: подъем обзора",
                "In a jump the game lifts the camera (about 65 cm) on top of the body animation; without it the view hardly rises. 100% = as in the game, 0% = the view stays. Pick the value where your arms and the body's shadow jump together.",
                "В прыжке игра поднимает камеру (около 65 см) поверх анимации тела; без этого обзор почти не поднимается. 100% = как в игре, 0% = обзор на месте. Подбери, чтобы руки и тень тела прыгали вместе.",
                R(0f, 1f, 0.05f), Pct, () => MimesisActions.JumpLift, v => MimesisActions.JumpLift = v, 0.45f);
            Bool("controls", "RoomScale", "Walking in the room moves the character", "Ходьба по комнате двигает персонажа",
                "When you physically step around, the character comes with you (walls stop it as in the game). Off: the view moves away from the body.",
                "Когда ты физически ходишь, персонаж идет вместе с тобой (стены его останавливают, как в игре). Выкл: обзор уходит от тела.",
                () => MimesisRoomScale.Enabled, v => MimesisRoomScale.Enabled = v, true);
            Bool("controls", "RoomWalkAnim", "Walking in the room: legs walk", "Ходьба по комнате: ноги шагают",
                "The character's legs walk (and footsteps sound) when you walk in the room, instead of sliding.",
                "Ноги персонажа шагают (и звучат шаги), когда ты ходишь по комнате, а не скользят.",
                () => MimesisRoomScale.WalkAnimation, v => MimesisRoomScale.WalkAnimation = v, true);
            Num("controls", "SnapAngle2", "Snap angle", "Угол рывка",
                "Angle of one snap turn.", "Угол одного рывка.",
                R(5f, 90f, 5f), Deg, () => MimesisInput.SnapAngle, v => MimesisInput.SnapAngle = v, 45f,
                "SnapAngle", new[] { 15f, 30f, 45f, 60f, 90f });
            Num("controls", "SmoothSpeed2", "Smooth turn speed", "Скорость плавного поворота",
                "Turn speed with the stick fully pushed.", "Скорость поворота при полностью отклоненном стике.",
                R(15f, 360f, 15f), DegS, () => MimesisInput.SmoothTurnSpeed, v => MimesisInput.SmoothTurnSpeed = v, 120f,
                "SmoothSpeed", new[] { 45f, 60f, 90f, 120f, 180f, 240f });
            Num("controls", "Deadzone2", "Stick dead zone", "Мертвая зона стика",
                "Small stick movements below this are ignored (walking).", "Отклонения стика меньше этого не двигают персонажа.",
                R(0f, 0.5f, 0.05f), Pct, () => MimesisInput.StickDeadzone, v => MimesisInput.StickDeadzone = v, 0.15f,
                "Deadzone", new[] { 0.05f, 0.10f, 0.15f, 0.20f, 0.30f });
            Enum("controls", "WalkDirection", "Walk direction", "Направление ходьбы",
                "Stick forward walks where you look, or where the controller of the move stick points.", "Стик вперед ведет туда, куда смотришь, или куда направлен контроллер со стиком ходьбы.",
                new[] { "Head", "Left controller" }, new[] { "По голове", "По левому контроллеру" },
                () => MimesisInput.WalkByController ? 1 : 0, i => MimesisInput.WalkByController = i == 1, 0);
            Enum("controls", "RunMode", "Sprint", "Бег",
                "The Run button (Buttons tab) toggles sprint, or sprint while it is held.", "Кнопка бега (вкладка «Кнопки») включает бег, или бег, пока она нажата.",
                new[] { "Click to toggle", "Hold" }, new[] { "Переключение кликом", "Удержание" },
                () => MimesisInput.RunHold ? 1 : 0, i => MimesisInput.RunHold = i == 1, 0);

            // ---- Hands & items
            Bool("hands", "ItemFollowsController", "Item follows controller", "Предмет следует за контроллером",
                "The item in your hand follows the right controller (only in the headset picture). Key F3.", "Предмет в руке следует за правым контроллером (только в картинке шлема). Клавиша F3.",
                () => MimesisVrExtras.ItemFollowsController, v => MimesisVrExtras.ItemFollowsController = v, true);
            Enum("hands", "HoldMode", "Item grip", "Хват предмета",
                "How the item is oriented in the hand. Key PageDown.", "Как предмет ориентирован в руке. Клавиша PageDown.",
                new[] { "Auto (long axis)", "Like controller", "Along pointer" }, new[] { "Авто (по длинной оси)", "Как контроллер", "По лучу" },
                () => (int)MimesisVrExtras.ItemHold, i => { MimesisVrExtras.ItemHold = (MimesisVrExtras.HoldMode)i; MimesisVrExtras.ResetHeldItem(); }, 0);
            Enum("hands", "GripFingers", "Fingers on an item", "Пальцы на предмете",
                "Game: the fingers take the game's own hold pose for each item (as in the original). Mod: a fist, set by 'Fingers: grip on an item'.",
                "Как в игре: пальцы берут хват самой игры для каждого предмета (как в оригинале). Мод: кулак, сила - «Пальцы: сжатие на предмете».",
                new[] { "Game", "Mod" }, new[] { "Как в игре", "Мод" },
                () => MimesisHands.GameGripFingers ? 0 : 1, i => MimesisHands.GameGripFingers = i == 0, 0);
            Bool("hands", "HideItemInTransit", "Hide item until it is in the hand", "Скрывать предмет, пока он не в руке",
                "When switching items the game first puts the new item elsewhere (Hands = Character arms); it is hidden until it is in the hand.",
                "При смене предмета игра сначала кладет новый предмет в другое место (Руки = Руки персонажа); пока он не в руке, он скрыт.",
                () => MimesisHands.HideItemInTransit, v => MimesisHands.HideItemInTransit = v, true);
            Enum("hands", "SwingMode", "Melee hit", "Удар ручным оружием",
                "Button: the trigger, as in the game. Swing: a fast movement of the right hand starts the hit (the game's wind-up delay and pace stay, so you cannot hit faster than with the button). Both: either.",
                "Кнопкой: курок, как в игре. Взмахом: быстрое движение правой руки запускает удар (задержка замаха и темп игры остаются - чаще, чем кнопкой, бить нельзя). Оба: и так, и так.",
                new[] { "Button", "Swing", "Both" }, new[] { "Кнопкой", "Взмахом", "Оба" },
                () => MimesisSwing.Mode, i => MimesisSwing.Mode = i, 2);
            Num("hands", "SwingThreshold", "Swing: hand speed", "Взмах: скорость руки",
                "How fast the hand must move to count as a swing. Raise it if hits start by accident, lower it if swings are missed.",
                "Насколько быстро должна двигаться рука, чтобы это считалось взмахом. Увеличь, если удары срабатывают случайно, уменьши - если взмахи не срабатывают.",
                R(0.8f, 5f, 0.1f), (v, l) => l == 1 ? $"{v:0.0} м/с" : $"{v:0.0} m/s", () => MimesisSwing.Threshold, v => MimesisSwing.Threshold = v, 1.8f);
            Bool("hands", "SwingHaptics", "Swing: vibration", "Взмах: вибрация",
                "Medium pulse on the swing, strong when the hit lands, medium when it misses, soft when you can hit again.", "Средний импульс на замах, сильный - при попадании, средний - при промахе, мягкий - когда снова можно бить.",
                () => MimesisSwing.Haptics, v => MimesisSwing.Haptics = v, true);
            Act("hands", "VibrationTest", "Test vibration", "Проверить вибрацию",
                "Four pulses on the right controller, about a second apart: 1 - default, 2 - 160 Hz, 3 - 320 Hz, 4 - shortest. Tell which ones you felt.",
                "Четыре импульса на правом контроллере примерно через секунду: 1 - обычный, 2 - 160 Гц, 3 - 320 Гц, 4 - самый короткий. Скажи, какие почувствовал.",
                () => MimesisSwing.StartVibrationTest());
            Enum("hands", "VibrationFreq", "Vibration frequency", "Частота вибрации",
                "Frequency used for all pulses. Default lets the headset choose; if it does not vibrate, try 160 or 320 Hz.",
                "Частота для всех импульсов. «Обычная» - на выбор шлема; если вибрации нет, попробуй 160 или 320 Гц.",
                new[] { "Default", "160 Hz", "320 Hz" }, new[] { "Обычная", "160 Гц", "320 Гц" },
                () => Features.VRVisualization.OpenXR.XrControllerInput.HapticFrequency >= 300f ? 2 : Features.VRVisualization.OpenXR.XrControllerInput.HapticFrequency >= 100f ? 1 : 0,
                i => Features.VRVisualization.OpenXR.XrControllerInput.HapticFrequency = i == 2 ? 320f : i == 1 ? 160f : 0f, 0);
            Enum("hands", "ActionDir", "Item actions direction", "Направление действий предметов",
                "Hits, shots and other item actions go where the right hand points, or where you look (as in the game). Other players briefly see your character turn that way.",
                "Удары, выстрелы и другие действия предметов идут туда, куда указывает правая рука, или туда, куда смотришь (как в игре). Другие игроки на миг видят, как персонаж поворачивается в ту сторону.",
                new[] { "Hand", "Gaze" }, new[] { "Рукой", "Взглядом" },
                () => MimesisActions.ByHand ? 0 : 1, i => MimesisActions.ByHand = i == 0, 0);
            Enum("hands", "InteractMode", "Interaction", "Взаимодействие",
                "Pick up and use things with the right hand pointer or with your gaze. Key End.", "Подбор и взаимодействие лучом правой руки или взглядом. Клавиша End.",
                new[] { "Hand (pointer)", "Gaze" }, new[] { "Рукой (луч)", "Взглядом" },
                () => MimesisInput.InteractByHand ? 0 : 1, i => MimesisInput.InteractByHand = i == 0, 0);
            Num("hands", "BeamOpacity2", "Pointer opacity", "Прозрачность луча",
                "How visible the pointer is when it is not on something usable (on a usable thing it always lights up). 0% = hidden.", "Насколько виден луч, когда он ни на что не наведен (на предмете он всегда ярче). 0% = скрыт.",
                Join(0f, 0.01f, 0.02f, 0.03f, 0.04f, 0.05f, 0.07f, 0.10f, 0.15f, 0.20f, 0.25f, 0.35f, 0.5f, 0.75f, 1f), Pct, () => MimesisVrExtras.BeamIdleAlpha, v => MimesisVrExtras.BeamIdleAlpha = v, 0.04f,
                "BeamOpacity", new[] { 0f, 0.02f, 0.04f, 0.07f, 0.10f, 0.15f, 0.25f });
            Enum("hands", "PointerStyle", "Pointer style", "Вид луча",
                "Short: a short beam from the hand and a dot where it points. Full: the whole beam.", "Короткий: короткий луч из руки и точка там, куда он указывает. Полный: луч на всю длину.",
                new[] { "Short + dot", "Full beam" }, new[] { "Короткий + точка", "Полный луч" },
                () => MimesisVrExtras.BeamShort ? 0 : 1, i => MimesisVrExtras.BeamShort = i == 0, 0);
            Num("hands", "BeamLength", "Short pointer length", "Длина короткого луча",
                "Length of the short beam from the hand (Pointer style = Short).", "Длина короткого луча из руки (Вид луча = Короткий).",
                R(0.10f, 1.00f, 0.02f), Cm, () => MimesisVrExtras.StubLength, v => MimesisVrExtras.StubLength = v, 0.30f);
            Enum("hands", "Hands", "Hands", "Руки",
                "Character: the character's own arms follow the controllers (the body is then shown). Bars: grey bars. None: nothing.",
                "Руки персонажа: руки модели следуют за контроллерами (тело при этом видно). Бруски: серые бруски. Нет: ничего.",
                new[] { "Bars", "Character arms", "None" }, new[] { "Бруски", "Руки персонажа", "Нет" },
                () => (int)MimesisHands.HandMode, i => MimesisHands.HandMode = (MimesisHands.Mode)i, 1);
            Bool("hands", "ShowControllers", "Bars visible", "Бруски видны",
                "Grey bars where your controllers are (when Hands = Bars).", "Серые бруски на месте контроллеров (когда Руки = Бруски).",
                () => MimesisVrExtras.ShowControllers, v => MimesisVrExtras.ShowControllers = v, true);
            Bool("hands", "ShowBody", "Show body", "Показывать тело",
                "Show your character's body in the headset. Key F4.", "Показывать тело персонажа в шлеме. Клавиша F4.",
                () => MimesisVrExtras.ShowBody, v => MimesisVrExtras.ShowBody = v, false);
            // Per-item grip (character arms): applies to the item in the right hand right now, saved per item
            string[] gEn = { "Item in hand: back/forward", "Item in hand: down/up", "Item in hand: left/right", "Item in hand: tilt (barrel down)", "Item in hand: turn left/right", "Item in hand: roll" };
            string[] gRu = { "Предмет в руке: назад/вперед", "Предмет в руке: вниз/вверх", "Предмет в руке: влево/вправо", "Предмет в руке: наклон (ствол вниз)", "Предмет в руке: поворот влево/вправо", "Предмет в руке: крен" };
            int[] gField = { 2, 1, 0, 3, 4, 5 };
            for (int k = 0; k < 6; k++)
            {
                int f = gField[k];
                bool ang = f >= 3;
                float[] vals = ang ? R(-180f, 180f, 5f) : R(-0.30f, 0.30f, 0.01f);
                Num("hands", "ItemGrip" + f, gEn[k], gRu[k],
                    "Correction for the item in your right hand now (Hands = Character arms). Saved separately for each item.",
                    "Поправка для предмета, который сейчас в правой руке (Руки = Руки персонажа). Запоминается для каждого предмета отдельно.",
                    vals, ang ? Deg : SignedCm, () => MimesisItemGrips.Value(f), v => MimesisItemGrips.SetValue(f, v), 0f);
                All[All.Count - 1].NoSave = true;
            }
            Act("hands", "ItemGripReset", "Item in hand: reset grip", "Предмет в руке: сбросить хват",
                "Removes the correction of the item in your right hand now.", "Убирает поправку предмета, который сейчас в правой руке.",
                () => MimesisItemGrips.ResetCurrent());
            Enum("hands", "SocketBase", "Item in hand: base pose", "Предмет в руке: основа положения",
                "Model: the item sits in the hand as in the character model (does not move with the game animation). Game, fixed: as the game holds it, taken 0.6 s after the item is in hand and then kept. Game: as the game animates it (no fix).",
                "Модель: предмет сидит в руке как в модели персонажа (анимация игры его не двигает). Игра, фиксировано: как держит игра, берется через 0,6 с после того, как предмет в руке, и дальше не меняется. Игра: как анимирует игра (без фиксации).",
                new[] { "Model", "Game, fixed", "Game" }, new[] { "Модель", "Игра, фиксировано", "Игра" },
                () => MimesisHands.SocketBase, i => MimesisHands.SocketBase = i, 0);
            Enum("hands", "FingerBase", "Finger rest pose", "Основа позы пальцев",
                "Model: fingers start from the model's own pose (stable). Animation: from the game animation at load (old behaviour, could bend fingers the wrong way).",
                "Модель: пальцы считаются от исходной позы модели (стабильно). Анимация: от анимации игры в момент загрузки (как раньше, пальцы могли выгибаться не туда).",
                new[] { "Model", "Animation (old)" }, new[] { "Модель", "Анимация (старое)" },
                () => MimesisHands.FingerBaseFromModel ? 0 : 1, i => { MimesisHands.FingerBaseFromModel = i == 0; MimesisHands.Rebind(); }, 0);

            // ---- Interface
            Bool("ui", "Hud", "Interface in headset", "Интерфейс в шлеме",
                "Show the game interface in the headset. Key Home.", "Показывать интерфейс игры в шлеме. Клавиша Home.",
                () => MimesisHud.Enabled, v => MimesisHud.Enabled = v, true);
            Bool("ui", "HudOnHands", "Interface on hands", "Интерфейс по рукам",
                "Inventory on the left wrist, hints at the pointer. Key Insert.", "Инвентарь на левом запястье, подсказки у луча. Клавиша Insert.",
                () => MimesisHudSegments.Enabled, v => MimesisHudSegments.Enabled = v, true);
            Enum("ui", "WristPlace", "Hand inventory place", "Место инвентаря на руке",
                "Above the left controller facing you, or lying on the wrist.", "Над левым контроллером лицом к тебе, или лежит на запястье.",
                new[] { "Above the controller", "On the wrist" }, new[] { "Над контроллером", "На запястье" },
                () => MimesisHudSegments.WristAbove ? 0 : 1, i => MimesisHudSegments.WristAbove = i == 0, 0);
            Num("ui", "WristSize3", "Wrist inventory size", "Размер инвентаря на запястье",
                "Width of the inventory panel on the left wrist.", "Ширина панели инвентаря на левом запястье.",
                R(0.08f, 0.60f, 0.02f), Cm, () => MimesisHudSegments.WristWidth, v => MimesisHudSegments.WristWidth = v, 0.26f,
                "WristSize2", R(0.12f, 0.40f, 0.02f));
            Num("ui", "InvX3", "Hand inventory: left/right", "Инвентарь на руке: влево/вправо",
                "Moves the hand inventory left (-) or right (+).", "Сдвиг инвентаря на руке влево (-) или вправо (+).",
                R(-0.40f, 0.40f, 0.01f), SignedCm, () => MimesisHudSegments.InvX, v => MimesisHudSegments.InvX = v, 0f,
                "InvX2", R(-0.20f, 0.20f, 0.01f));
            Num("ui", "InvY3", "Hand inventory: down/up", "Инвентарь на руке: вниз/вверх",
                "Moves the hand inventory down (-) or up (+).", "Сдвиг инвентаря на руке вниз (-) или вверх (+).",
                R(-0.30f, 0.40f, 0.01f), SignedCm, () => MimesisHudSegments.InvY, v => MimesisHudSegments.InvY = v, 0f,
                "InvY2", R(-0.16f, 0.24f, 0.01f));
            Num("ui", "InvZ3", "Hand inventory: closer/further", "Инвентарь на руке: ближе/дальше",
                "Moves the hand inventory towards you (-) or away (+).", "Сдвиг инвентаря на руке к тебе (-) или от тебя (+).",
                R(-0.40f, 0.40f, 0.01f), SignedCm, () => MimesisHudSegments.InvZ, v => MimesisHudSegments.InvZ = v, 0f,
                "InvZ2", R(-0.20f, 0.20f, 0.01f));
            Num("ui", "InvTilt3", "Hand inventory: tilt", "Инвентарь на руке: наклон",
                "Tilts the hand inventory top away (+) or towards you (-).", "Наклон инвентаря на руке: верх от тебя (+) или к тебе (-).",
                R(-180f, 180f, 5f), Deg, () => MimesisHudSegments.InvTilt, v => MimesisHudSegments.InvTilt = v, 0f,
                "InvTilt2", R(-90f, 90f, 10f));
            Bool("ui", "FpsCounter", "Frame counter on the wrist", "Счетчик кадров на руке",
                "Frames per second next to the wrist gear: green - the headset rate is kept, yellow - a bit below, red - far below.",
                "Кадры в секунду рядом с шестеренкой на руке: зеленый - частота шлема держится, желтый - немного ниже, красный - сильно ниже.",
                () => MimesisFps.Enabled, v => MimesisFps.Enabled = v, true);
            Bool("ui", "WristPanel", "Own wrist panel", "Своя панель на руке",
                "The mod draws its own panel on the left hand: the slots with item pictures and the same numbers as in the game; health, contamination and stamina are on the right watch. Off: the pieces cut out of the game screen (as before).",
                "Мод рисует свою панель на левой руке: ячейки с картинками предметов и теми же числами, что в игре; здоровье, радиация и бег - на правых часах. Выкл: куски, вырезанные из экрана игры (как раньше).",
                () => MimesisSettingsUI.WristHud.On, v => MimesisSettingsUI.WristHud.On = v, true);
            Enum("ui", "WristPanelShow", "Wrist panel: visibility", "Панель на руке: видимость",
                "When you look at it: half transparent until you look at your hand. Always: always fully visible.",
                "При взгляде: полупрозрачная, пока не посмотришь на руку. Всегда: всегда видна полностью.",
                new[] { "When you look at it", "Always" }, new[] { "При взгляде", "Всегда" },
                () => MimesisSettingsUI.WristHud.LookToShow ? 0 : 1, i => MimesisSettingsUI.WristHud.LookToShow = i == 0, 0);
            Num("ui", "WristPanelIdle", "Wrist panel: when not looking", "Панель на руке: когда не смотришь",
                "How visible the panel stays while you do not look at it (0% = hidden).", "Насколько видна панель, пока на нее не смотришь (0% = не видна).",
                R(0f, 1f, 0.05f), Pct, () => MimesisSettingsUI.WristHud.IdleAlpha, v => MimesisSettingsUI.WristHud.IdleAlpha = v, 0.25f);
            Enum("ui", "WristPanelMount", "Wrist panel: attached to", "Панель на руке: крепление",
                "Controller: above the left hand, as before (positioned by the 'Hand inventory' settings). Forearm: on the left watch on the character's forearm (Hands = Character arms; positioned on the 'Left watch' tab). On the forearm the bag slots go to the left of the main ones, the gear and frame counter further left.",
                "К контроллеру: над левой кистью, как раньше (положение - регулировки «Инвентарь на руке»). К предплечью: на левых часах на предплечье персонажа (Руки = Руки персонажа; положение - вкладка «Левые часы»). На предплечье ячейки сумок идут левее основных, шестеренка и счетчик кадров - еще левее.",
                new[] { "Controller", "Forearm" }, new[] { "К контроллеру", "К предплечью" },
                () => MimesisSettingsUI.WristHud.OnForearm ? 1 : 0, i => MimesisSettingsUI.WristHud.OnForearm = i == 1, 0);
            Enum("ui", "TipsPlace", "Key tips (top right)", "Подсказки по кнопкам (справа вверху)",
                "Hints about keys and the item in hand: on the right hand (like the inventory on the left), on the screen as in the game, or hidden.",
                "Подсказки по кнопкам и предмету в руке: на правой руке (как инвентарь на левой), на экране, как в игре, или скрыть.",
                new[] { "On the right hand", "On the screen", "Hidden" }, new[] { "На правой руке", "На экране", "Скрыть" },
                () => MimesisHudSegments.TipsPlace, i => MimesisHudSegments.TipsPlace = i, 0);
            Num("ui", "TipsX", "Key tips: left/right", "Подсказки: влево/вправо", "Position on the right hand.", "Положение на правой руке.",
                R(-0.30f, 0.30f, 0.01f), SignedCm, () => MimesisHudSegments.TipsX, v => MimesisHudSegments.TipsX = v, 0f);
            Num("ui", "TipsY", "Key tips: down/up", "Подсказки: вниз/вверх", "Position on the right hand.", "Положение на правой руке.",
                R(-0.30f, 0.40f, 0.01f), SignedCm, () => MimesisHudSegments.TipsY, v => MimesisHudSegments.TipsY = v, 0f);
            Num("ui", "TipsZ", "Key tips: closer/further", "Подсказки: ближе/дальше", "Position on the right hand.", "Положение на правой руке.",
                R(-0.30f, 0.30f, 0.01f), SignedCm, () => MimesisHudSegments.TipsZ, v => MimesisHudSegments.TipsZ = v, 0f);
            Num("ui", "TipsTilt", "Key tips: tilt", "Подсказки: наклон", "Tilt of the tips panel.", "Наклон панели подсказок.",
                R(-90f, 90f, 5f), Deg, () => MimesisHudSegments.TipsTilt, v => MimesisHudSegments.TipsTilt = v, 0f);
            Num("ui", "TipsScale", "Key tips: size", "Подсказки: размер", "Size relative to the inventory scale.", "Размер относительно масштаба инвентаря.",
                R(0.3f, 3f, 0.05f), X, () => MimesisHudSegments.TipsScale, v => MimesisHudSegments.TipsScale = v, 1f);
            Enum("ui", "TipsRBPlace", "Tips at the bottom right (tram)", "Подсказки справа внизу (трамвай)",
                "The hints at the bottom right of the screen (tram and similar): hidden or on the screen as in the game.",
                "Подсказки в правом нижнем углу экрана (трамвай и подобные): скрыть или на экране, как в игре.",
                new[] { "Hidden", "On the screen" }, new[] { "Скрыть", "На экране" },
                () => MimesisHudSegments.TipsRBPlace, i => MimesisHudSegments.TipsRBPlace = i, 0);
            Num("ui", "HintSize2", "Pointer hint size", "Размер подсказки у луча",
                "Size of the crosshair and interaction hint at the end of the pointer.", "Размер прицела и подсказки у конца луча.",
                R(0.5f, 3f, 0.25f), X, () => MimesisHudSegments.PointerScale, v => MimesisHudSegments.PointerScale = v, 1f,
                "HintSize", new[] { 0.75f, 1f, 1.25f, 1.5f });
            Num("ui", "HudDistance3", "Interface distance", "Расстояние до интерфейса",
                "Distance of the interface in front of your eyes (its apparent size stays the same).", "Расстояние до интерфейса перед глазами (видимый размер не меняется).",
                Join(0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f, 1.6f, 1.8f, 2.0f, 2.25f, 2.5f, 3.0f, 3.5f, 4.0f, 5.0f), M, () => MimesisHud.Distance, v => MimesisHud.Distance = v, 1.1f,
                "HudDistance2", Join(0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f, 1.1f, 1.2f, 1.3f, 1.4f, 1.6f, 1.8f, 2.0f, 2.5f, 3.0f));
            Num("ui", "HudScale3", "Interface size", "Размер интерфейса",
                "Size of the interface in front of your eyes.", "Размер интерфейса перед глазами.",
                R(0.3f, 2.5f, 0.05f), X, () => MimesisHud.Scale, v => MimesisHud.Scale = v, 1f,
                "HudScale2", R(0.5f, 1.6f, 0.05f));
            Num("ui", "MenuDistance3", "Menu distance", "Расстояние до меню",
                "Distance of the game menu panel. In a menu the right stick also moves it.", "Расстояние до панели меню игры. В меню его также меняет правый стик.",
                Join(0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f, 1.1f, 1.25f, 1.5f, 1.75f, 2.0f, 2.5f, 3.0f, 3.5f, 4.0f, 5.0f), M, () => MimesisInput.PanelDistance, v => MimesisInput.PanelDistance = v, 1.0f,
                "MenuDistance2", Join(0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f, 1.1f, 1.25f, 1.5f, 1.75f, 2.0f, 2.5f, 3.0f));
            Num("ui", "MenuBackdrop2", "Menu backdrop", "Затемнение фона меню",
                "Darkness of the backing behind game menus.", "Насколько темная подложка у меню игры.",
                Join(0f, 0.05f, 0.1f, 0.15f, 0.2f, 0.25f, 0.3f, 0.35f, 0.4f, 0.45f, 0.5f, 0.55f, 0.6f, 0.65f, 0.7f, 0.72f, 0.75f, 0.8f, 0.85f, 0.9f, 0.95f, 1f), Pct, () => MimesisHud.MenuBackdropAlpha, v => MimesisHud.MenuBackdropAlpha = v, 0.72f,
                "MenuBackdrop", new[] { 0f, 0.4f, 0.6f, 0.72f, 0.85f });
            Num("ui", "DarkScreens2", "Dark screens of the game", "Темные экраны игры",
                "Full-screen dark overlays of the game (hints, fades, black screens) are made no darker than this. 0% = removed.", "Полноэкранные темные подложки игры (подсказки, затемнения, черные экраны) не темнее этого. 0% = убраны совсем.",
                R(0f, 1f, 0.05f), Pct, () => MimesisHud.DimCap, v => MimesisHud.DimCap = v, 0f,
                "DarkScreens", new[] { 0f, 0.1f, 0.25f, 0.45f, 0.65f, 1f });

            // ---- Watches (build 40)
            DefineWatch("watchL", MimesisWatches.L);
            Enum("watchR", "StatusPlace2", "Health, contamination, stamina", "Здоровье, радиация, бег",
                "On the right watch: the mod's own live picture (pulse, health in quarters, contamination tube with bubbles, stamina). On the screen: the game's own elements, as in the game.",
                "На правых часах: свой живой рисунок мода (пульс, здоровье по четвертям, шкала радиации с пузырьками, бег). На экране: элементы самой игры, как в игре.",
                new[] { "On the right watch", "On the screen" }, new[] { "На правых часах", "На экране" },
                () => MimesisHudSegments.StatusPlace == 2 ? 1 : 0, i => MimesisHudSegments.StatusPlace = i == 1 ? 2 : 0, 0);
            DefineWatch("watchR", MimesisWatches.R);

            // ---- Graphics
            Bool("video", "Mirror", "Headset view on monitor", "Картинка шлема на мониторе",
                "Show the headset picture (one eye) with the interface on the monitor. Key F1.", "Показывать на мониторе картинку шлема (один глаз) с интерфейсом. Клавиша F1.",
                () => MimesisVrExtras.Mirror, v => MimesisVrExtras.Mirror = v, false);
            Enum("video", "MirrorEye", "Monitor: eye", "Монитор: глаз",
                "Which eye's picture the monitor (headset view, F1) shows.", "Картинку какого глаза показывает монитор (картинка шлема, F1).",
                new[] { "Left", "Right" }, new[] { "Левый", "Правый" },
                () => MimesisVrExtras.MirrorRightEye ? 1 : 0, i => MimesisVrExtras.MirrorRightEye = i == 1, 1);
            Enum("video", "StreamUi", "Monitor: interface for viewers", "Монитор: интерфейс для зрителей",
                "What the monitor (headset view, F1) shows of the watch screens and the wrist panel. Corners: health bottom left, inventory bottom middle, always readable. In the world: where the player sees them (with the gear and the laser).",
                "Что монитор (картинка шлема, F1) показывает из экранов часов и панели на руке. По углам: здоровье внизу слева, инвентарь внизу по центру, всегда читается. В мире: там, где их видит игрок (вместе с шестеренкой и лучом).",
                new[] { "Off", "Corners", "In the world" }, new[] { "Выкл", "По углам", "В мире" },
                () => MimesisStream.Mode, i => MimesisStream.Mode = i, 1);
            Num("video", "StreamSize", "Monitor: corner picture size", "Монитор: размер картинок в углах",
                "Height of the health picture as a share of the monitor height.", "Высота картинки здоровья в долях высоты монитора.",
                R(0.1f, 0.35f, 0.025f), Pct, () => MimesisStream.CornerSize, v => MimesisStream.CornerSize = v, 0.2f);

            Enum("video", "ScreenFx", "Screen effects (rain, smudge)", "Экранные эффекты (дождь, пятна)",
                "Effects the game draws on the screen: on the eyes like on a lens, off, or in the interface panel as before.", "Эффекты, которые игра рисует на экране: на глазах как на линзе, выключены, или в панели интерфейса как раньше.",
                new[] { "On the eyes", "Off", "In interface (old)" }, new[] { "На глазах", "Выкл", "В интерфейсе (старое)" },
                () => (int)MimesisHud.Fx, i => MimesisHud.Fx = (MimesisHud.FxMode)i, 0);
            Bool("video", "LensRain", "Rain on the lens", "Дождь на линзе",
                "The game's rain drops on the screen, drawn on the eyes. Off by default: in the headset they looked like a black sheet with holes; the rain itself is in the world anyway.",
                "Капли дождя игры на экране, на глазах. По умолчанию выкл: в шлеме они выглядели как черное полотно с дырками; сам дождь и так есть в мире.",
                () => MimesisHud.LensRain, v => MimesisHud.LensRain = v, false);
            Bool("video", "UiNoise", "Noise on game panels", "Шум на панелях игры",
                "The game's noise overlay on its interface panels (tips, health). Off by default: in the headset it gave dark backings.",
                "Шумовой слой игры на ее панелях интерфейса (подсказки, здоровье). По умолчанию выкл: в шлеме он давал темную подложку.",
                () => MimesisHud.UiNoise, v => MimesisHud.UiNoise = v, false);
            Enum("video", "Sharpen", "Sharpening", "Резкость",
                "Game sharpening filter (Beautify). Strong sharpening adds grain in dark places in VR.", "Фильтр резкости игры (Beautify). Сильная резкость в VR дает зерно в темноте.",
                new[] { "As in game", "Reduced", "Off" }, new[] { "Как в игре", "Ослабить", "Выкл" },
                () => MimesisImage.Sharpen, i => { MimesisImage.Sharpen = i; MimesisImage.ApplyNow(); }, 0);
            Bool("video", "LensDirt", "Lens dirt", "Грязь на линзе",
                "Dirty-lens glow around bright lights (Beautify).", "Свечение грязной линзы вокруг ярких источников света (Beautify).",
                () => MimesisImage.LensDirt, v => { MimesisImage.LensDirt = v; MimesisImage.ApplyNow(); }, false);
            Bool("video", "Posterize", "Posterize filter", "Постеризация",
                "The game's colour posterize / dither filter.", "Фильтр постеризации цвета игры.",
                () => MimesisImage.Posterize, v => { MimesisImage.Posterize = v; MimesisImage.ApplyNow(); }, true);

            // ---- Performance
            Enum("perf", "Preset", "Preset", "Набор настроек",
                "Applies a ready set of the options below (from Very low to High = as in game); any option can then be changed separately.",
                "Применяет готовый набор настроек ниже (от «Очень низкого» до «Высокого» = как в игре); потом любую можно поменять отдельно.",
                new[] { "Custom", "Very low", "Low", "Medium", "Good", "High (as game)" }, new[] { "Свой", "Очень низкий", "Низкий", "Средний", "Хороший", "Высокий (как в игре)" },
                () => PresetNow(), i => ApplyPreset(i), 0);
            All[All.Count - 1].NoSave = true;
            Bool("perf", "MonitorView", "Picture on the monitor", "Картинка на мониторе",
                "Off: the game does not draw its picture for the monitor while you are in VR (a big saving). The monitor shows a note, or the headset view with F1.",
                "Выкл: игра не рисует картинку для монитора, пока ты в шлеме (большая экономия). На мониторе - надпись, или картинка шлема по F1.",
                () => MimesisMonitor.RenderMonitor, v => MimesisMonitor.RenderMonitor = v, true);
            Num("perf", "RenderScale2", "Headset resolution", "Разрешение в шлеме",
                "Share of the headset's recommended resolution the game renders at; the picture is stretched to full size.", "Доля рекомендованного разрешения шлема, в которой рисует игра; картинка растягивается до полного размера.",
                R(0.4f, 1f, 0.05f), Pct, () => MimesisImage.RenderScale, v => MimesisImage.RenderScale = v, 1f,
                "RenderScale", new[] { 0.6f, 0.7f, 0.8f, 0.9f, 1f });
            Enum("perf", "Msaa", "Anti-aliasing (MSAA)", "Сглаживание (MSAA)",
                "Smoother edges in the headset; higher costs GPU time. Key F5.", "Более гладкие края в шлеме; чем выше, тем больше нагрузка. Клавиша F5.",
                new[] { "Off", "x2", "x4", "x8" }, new[] { "Выкл", "x2", "x4", "x8" },
                () => MimesisVrExtras.MsaaSamples >= 8 ? 3 : MimesisVrExtras.MsaaSamples >= 4 ? 2 : MimesisVrExtras.MsaaSamples >= 2 ? 1 : 0,
                i => MimesisVrExtras.MsaaSamples = i == 3 ? 8 : i == 2 ? 4 : i == 1 ? 2 : 1, 2);
            Enum("perf", "FrameLimit", "Frame limit", "Ограничение кадров",
                "As in game: the game's own VSync / limit. Headset: no game limit, the headset sets the pace. Or a fixed limit.",
                "Как в игре: собственный VSync / лимит игры. По шлему: без лимита игры, темп задает шлем. Или фиксированный лимит.",
                new[] { "As in game", "Headset", "72", "60", "45" }, new[] { "Как в игре", "По шлему", "72", "60", "45" },
                () => MimesisImage.FrameLimit, i => { MimesisImage.FrameLimit = i; MimesisImage.ApplyNow(); }, 1);
            Bool("perf", "LampShadows", "Lamp shadows", "Тени от ламп",
                "Shadows cast by lamps and other local lights. Each eye draws them separately - off can help a lot on weaker PCs.", "Тени от ламп и других местных источников света. Каждый глаз рисует их отдельно - выключение может сильно помочь на слабых ПК.",
                () => MimesisImage.LampShadows, v => { MimesisImage.LampShadows = v; MimesisImage.ApplyNow(); }, true);
            Num("perf", "DrawDist2", "Draw distance", "Дальность прорисовки",
                "How far the headset draws the world. Less = fewer objects for the processor on open maps; far objects disappear. Indoors it changes little.", "Как далеко шлем рисует мир. Меньше - меньше объектов для процессора на открытых картах; дальние объекты пропадают. В помещениях почти не влияет.",
                new[] { 0f, 500f, 400f, 300f, 200f, 150f, 120f, 100f, 80f, 60f, 50f, 40f, 30f, 20f, 15f }, (v, l) => v <= 0f ? (l == 1 ? "Как в игре" : "As in game") : (l == 1 ? $"{v:0} м" : $"{v:0} m"),
                () => MimesisImage.DrawDistance, v => MimesisImage.DrawDistance = v, 0f,
                "DrawDist", new[] { 0f, 200f, 120f, 80f, 50f, 30f });
            Bool("perf", "EyePost", "Post-processing in headset", "Постобработка в шлеме",
                "Glow, colour grading and other screen effects of the game in the eyes. Off is faster but the picture looks plainer.", "Свечение, цветокоррекция и другие экранные эффекты игры в глазах. Выключение ускоряет, но картинка проще.",
                () => MimesisImage.EyePost, v => MimesisImage.EyePost = v, true);
            Enum("perf", "HudEvery", "Interface refresh", "Обновление интерфейса",
                "Redraw the game interface every frame or less often (it still moves smoothly). Saves processor time.", "Перерисовывать интерфейс игры каждый кадр или реже (двигается он все равно плавно). Экономит время процессора.",
                new[] { "Every frame", "Every 2nd frame", "Every 3rd frame" }, new[] { "Каждый кадр", "Через кадр", "Каждый 3-й кадр" },
                () => Mathf.Clamp(MimesisHud.UpdateEvery - 1, 0, 2), i => MimesisHud.UpdateEvery = i + 1, 0);
            Num("perf", "Shadows2", "Shadow distance", "Дальность теней",
                "How far shadows are drawn (share of the game's distance). 0% = no shadows.", "Насколько далеко рисуются тени (доля от игровой дальности). 0% = без теней.",
                R(0f, 1.5f, 0.05f), Pct, () => MimesisImage.ShadowScale, v => { MimesisImage.ShadowScale = v; MimesisImage.ApplyNow(); }, 1f,
                "Shadows", new[] { 0f, 0.25f, 0.5f, 0.75f, 1f });
            Enum("perf", "ShadowRes", "Shadow quality", "Качество теней",
                "Resolution of the shadow maps.", "Разрешение карт теней.",
                new[] { "As in game", "Low (512)", "Medium (1024)", "High (2048)" }, new[] { "Как в игре", "Низкое (512)", "Среднее (1024)", "Высокое (2048)" },
                () => MimesisImage.ShadowRes, i => { MimesisImage.ShadowRes = i; MimesisImage.ApplyNow(); }, 0);
            Enum("perf", "Textures", "Texture quality", "Качество текстур",
                "Lower texture resolution saves video memory and some speed.", "Пониженное разрешение текстур экономит видеопамять и немного скорость.",
                new[] { "Full", "Half", "Quarter" }, new[] { "Полное", "Половина", "Четверть" },
                () => MimesisImage.TextureLevel, i => { MimesisImage.TextureLevel = i; MimesisImage.ApplyNow(); }, 0);
            Num("perf", "LodDetail2", "Detail distance", "Дальность детализации",
                "How far detailed versions of models are used (LOD).", "Как далеко используются детальные версии моделей (LOD).",
                Join(0.1f, 0.15f, 0.2f, 0.25f, 0.3f, 0.35f, 0.4f, 0.5f, 0.6f, 0.75f, 0.9f, 1f, 1.25f, 1.5f, 2f, 3f), X, () => MimesisImage.LodScale, v => { MimesisImage.LodScale = v; MimesisImage.ApplyNow(); }, 1f,
                "LodDetail", new[] { 0.25f, 0.35f, 0.5f, 0.75f, 1f, 1.5f });
            Bool("perf", "VolumetricFog", "Volumetric fog", "Объемный туман",
                "Buto volumetric fog. Off is faster and removes its noise.", "Объемный туман Buto. Выключение ускоряет игру и убирает его шум.",
                () => MimesisImage.VolumetricFog, v => { MimesisImage.VolumetricFog = v; MimesisImage.ApplyNow(); }, true);
            Bool("perf", "Ssao", "Ambient occlusion (SSAO)", "Затенение (SSAO)",
                "Contact shadows. Off is faster.", "Контактные тени. Выключение ускоряет игру.",
                () => MimesisImage.Ssao, v => { MimesisImage.Ssao = v; MimesisImage.ApplyNow(); }, true);
            Bool("perf", "Decals", "Decals", "Декали",
                "Stains, marks and other decals on surfaces.", "Пятна, следы и другие наклейки на поверхностях.",
                () => MimesisImage.Decals, v => { MimesisImage.Decals = v; MimesisImage.ApplyNow(); }, true);

            // ---- Comfort
            Act("comfort", "Recenter", "Recenter now", "Центрировать сейчас",
                "Re-centre the view on where you stand and look now. Key F10.", "Заново центрировать по тому, где ты сейчас стоишь и куда смотришь. Клавиша F10.",
                () => VrVisualizationRecenter());
            Num("comfort", "HandPitch3", "Hand calibration: tilt", "Калибровка кисти: наклон",
                "Tilts the character's hands relative to the controllers (fingers up/down).", "Наклон кистей персонажа относительно контроллеров (пальцы вверх/вниз).",
                R(-180f, 180f, 5f), Deg, () => MimesisHands.Pitch, v => MimesisHands.Pitch = v, 0f,
                "HandPitch2", R(-90f, 90f, 5f));
            Num("comfort", "HandRoll3", "Hand calibration: roll", "Калибровка кисти: поворот",
                "Rolls the hands around the forearm.", "Поворот кистей вокруг предплечья.",
                R(-180f, 180f, 5f), Deg, () => MimesisHands.Roll, v => MimesisHands.Roll = v, 0f,
                "HandRoll2", R(-180f, 180f, 15f));
            Num("comfort", "HandYaw3", "Hand calibration: turn", "Калибровка кисти: разворот",
                "Turns the hands left/right.", "Разворот кистей влево/вправо.",
                R(-180f, 180f, 5f), Deg, () => MimesisHands.Yaw, v => MimesisHands.Yaw = v, 0f,
                "HandYaw2", R(-90f, 90f, 5f));
            Num("comfort", "HandOffset3", "Hand calibration: wrist offset", "Калибровка кисти: отступ запястья",
                "How far the wrist sits behind the controller grip.", "Насколько запястье находится позади рукояти контроллера.",
                R(-0.10f, 0.30f, 0.01f), Cm, () => MimesisHands.WristBack, v => MimesisHands.WristBack = v, 0.07f,
                "HandOffset2", R(0f, 0.20f, 0.01f));
            Num("comfort", "GripTilt3", "Hand and item: barrel down", "Кисть и предмет: ствол вниз",
                "Tilts the hand and the held item down (+) or up (-), like pointing a pistol barrel lower. Works with and without an item.", "Наклон кисти и предмета вниз (+) или вверх (-), как опустить ствол пистолета. Работает и с предметом, и без.",
                R(-180f, 180f, 5f), Deg, () => MimesisHands.GripTilt, v => MimesisHands.GripTilt = v, 50f,
                "GripTilt2", R(-60f, 90f, 5f));
            Num("comfort", "GripX3", "Hand and item: left/right", "Кисть и предмет: влево/вправо",
                "Shifts the hands and the held item sideways (mirrored for the left hand: + is outward).", "Сдвиг кистей и предмета вбок (для левой руки зеркально: + наружу).",
                R(-0.25f, 0.25f, 0.01f), SignedCm, () => MimesisHands.GripX, v => MimesisHands.GripX = v, 0f,
                "GripX2", R(-0.12f, 0.12f, 0.01f));
            Num("comfort", "GripY3", "Hand and item: down/up", "Кисть и предмет: вниз/вверх",
                "Shifts the hands and the held item down (-) or up (+) relative to the controller.", "Сдвиг кистей и предмета вниз (-) или вверх (+) относительно контроллера.",
                R(-0.25f, 0.25f, 0.01f), SignedCm, () => MimesisHands.GripY, v => MimesisHands.GripY = v, 0f,
                "GripY2", R(-0.12f, 0.12f, 0.01f));
            Num("comfort", "GripZ3", "Hand and item: back/forward", "Кисть и предмет: назад/вперед",
                "Shifts the hands and the held item back (-) or forward (+) along the controller.", "Сдвиг кистей и предмета назад (-) или вперед (+) вдоль контроллера.",
                R(-0.30f, 0.30f, 0.01f), SignedCm, () => MimesisHands.GripZ, v => MimesisHands.GripZ = v, 0f,
                "GripZ2", R(-0.16f, 0.16f, 0.01f));
            Num("comfort", "FingerRest3", "Fingers at rest", "Пальцы в покое",
                "How much the fingers are bent when no button is pressed. Raise it if they look bent backwards.", "Насколько согнуты пальцы, когда кнопки не нажаты. Увеличь, если они выглядят выгнутыми назад.",
                R(-60f, 120f, 5f), Deg, () => MimesisHands.FingerRest, v => MimesisHands.FingerRest = v, 25f,
                "FingerRest2", R(-30f, 90f, 5f));
            Enum("comfort", "FingerBend", "Finger bend direction", "Направление сгиба пальцев",
                "Switch if the fingers bend backwards.", "Переключи, если пальцы гнутся в обратную сторону.",
                new[] { "Normal", "Reversed" }, new[] { "Обычное", "Обратное" },
                () => MimesisHands.CurlSign < 0f ? 0 : 1, i => MimesisHands.CurlSign = i == 0 ? -1f : 1f, 0);
            Num("comfort", "FingerSqueeze", "Fingers: grip on an item", "Пальцы: сжатие на предмете",
                "How tightly the fingers close around a held item (100% = full fist). Lower it if the fingers sink into the item.", "Насколько сильно пальцы сжимают предмет (100% = полный кулак). Уменьши, если пальцы проваливаются в предмет.",
                R(0f, 1f, 0.05f), Pct, () => MimesisHands.HoldSqueeze, v => MimesisHands.HoldSqueeze = v, 0.7f);
            Num("comfort", "FingerMax", "Fingers: full bend", "Пальцы: полный сгиб",
                "How far the fingers bend at full trigger / grip (100% = as before).", "Насколько сгибаются пальцы при полном нажатии курка / грипа (100% = как раньше).",
                R(0.3f, 1.5f, 0.05f), Pct, () => MimesisHands.FingerMax, v => MimesisHands.FingerMax = v, 1f);
            Num("comfort", "FingerSpread", "Fingers: spread", "Пальцы: разведение",
                "Spreads the fingers apart (+) or together (-), so they do not sink into each other. If + brings them together, use -.", "Разводит пальцы (+) или сводит (-), чтобы они не проваливались друг в друга. Если + их сводит - используй -.",
                R(-30f, 30f, 1f), Deg, () => MimesisHands.FingerSpread, v => MimesisHands.FingerSpread = v, 0f);
            Bool("comfort", "TouchFingers", "Fingers react to touch", "Пальцы реагируют на касание",
                "The thumb bends a little when it rests on a button, the stick or the thumbrest; the index finger - when it rests on the trigger.", "Большой палец немного подгибается, когда лежит на кнопке, стике или площадке; указательный - когда лежит на курке.",
                () => MimesisHands.TouchFingers, v => MimesisHands.TouchFingers = v, true);
            Bool("comfort", "HandsWalls", "Hands stop at walls", "Руки не проходят сквозь стены",
                "The character's hands stop at walls and objects instead of going through them (only how they look; pointing and pressing work as before).",
                "Руки персонажа упираются в стены и предметы, а не проходят сквозь них (только вид; указание и нажатия работают как раньше).",
                () => MimesisHands.BlockByWalls, v => MimesisHands.BlockByWalls = v, true);
            Bool("comfort", "DamageFlash", "Damage: red flash on the eyes", "Урон: красная вспышка на глазах",
                "When health drops, a short red flash at the edges of the view (stronger for a bigger hit).", "Когда здоровье падает - короткая красная вспышка по краям обзора (сильнее при большем уроне).",
                () => MimesisWatches.DamageFlash, v => MimesisWatches.DamageFlash = v, true);
            Bool("comfort", "DamageBuzz", "Damage: vibration", "Урон: вибрация",
                "When health drops, both controllers vibrate briefly.", "Когда здоровье падает, оба контроллера коротко вибрируют.",
                () => MimesisWatches.DamageBuzz, v => MimesisWatches.DamageBuzz = v, true);
            string[] fEn = { "index", "middle", "ring", "little" }, fRu = { "указательного", "среднего", "безымянного", "мизинца" };
            for (int k = 0; k < 4; k++)
            {
                int f = k;
                Num("comfort", "Spread" + fEn[k], "Fingers: spread of the " + fEn[k] + " finger", "Пальцы: разведение " + fRu[k],
                    "Turns this finger sideways (degrees), on top of the common spread. Use it to separate fingers that touch each other.",
                    "Отводит этот палец в сторону (градусы), вдобавок к общему разведению. Чтобы разделить пальцы, которые касаются друг друга.",
                    R(-40f, 40f, 1f), Deg, () => MimesisHands.SpreadPer[f], v => MimesisHands.SpreadPer[f] = v, 0f);
            }
            Num("comfort", "ForearmTwist", "Arms: forearm twist", "Руки: скручивание предплечья",
                "How much of the hand's roll the forearm takes (so the wrist does not wring like a cloth). 0% = only the hand turns (as before).",
                "Какую часть поворота кисти берет на себя предплечье (чтобы запястье не скручивалось, как тряпка). 0% = вертится только кисть (как раньше).",
                R(0f, 1f, 0.05f), Pct, () => MimesisHands.ForearmTwist, v => MimesisHands.ForearmTwist = v, 0.6f);
            Enum("comfort", "ShoulderAnchor", "Arms: shoulders follow", "Руки: плечи привязаны",
                "Body: shoulders are where the character model has them (game animations of the body - throws, jumps, item switch - move the arms). Head: fixed under your head (animations do not move the arms; the shoulder settings shift from that point; in a jump the arms go as high as your view). Body, smoothed: on the body, but sharp jerks of the animation are damped.",
                "К телу: плечи там, где у модели персонажа (анимации тела - броски, прыжки, смена предмета - двигают руки). К голове: закреплены под головой (анимации руки не двигают; регулировки плеч считаются от этой точки; в прыжке руки поднимаются вместе с обзором). К телу, сглажено: на теле, но резкие рывки анимации гасятся.",
                new[] { "Body", "Head", "Body, smoothed" }, new[] { "К телу", "К голове", "К телу, сглажено" },
                () => MimesisHands.ShoulderMode, i => MimesisHands.ShoulderMode = i, 1);
            Num("comfort", "ShoulderSmooth", "Arms: shoulder smoothing", "Руки: сглаживание плеч",
                "For 'Body, smoothed': how fast the shoulders follow the body animation (lower = smoother, but the arms lag behind the body).",
                "Для «К телу, сглажено»: как быстро плечи догоняют анимацию тела (меньше - плавнее, но руки сильнее отстают от тела).",
                R(1f, 20f, 1f), (v, l) => $"{v:0}", () => MimesisHands.ShoulderSmooth, v => MimesisHands.ShoulderSmooth = v, 6f);
            Num("comfort", "BodyForward", "Body: back/forward", "Тело: назад/вперед",
                "Moves the character model forward so the body (feet) is under you and not behind (only in your view; Hands = Character arms). Too far and the chest covers the view.",
                "Сдвигает модель персонажа вперед, чтобы тело (ступни) было под тобой, а не сзади (только в твоем виде; Руки = Руки персонажа). Слишком далеко - грудь закроет обзор.",
                R(-0.50f, 0.80f, 0.01f), SignedCm, () => MimesisHands.BodyForward, v => MimesisHands.BodyForward = v, 0f);
            Num("comfort", "BodyUp", "Body: down/up", "Тело: вниз/вверх",
                "Moves the character model down or up (only in your view).", "Сдвигает модель персонажа вниз или вверх (только в твоем виде).",
                R(-0.50f, 0.50f, 0.01f), SignedCm, () => MimesisHands.BodyUp, v => MimesisHands.BodyUp = v, 0f);
            Num("comfort", "LegsForward", "Legs: back/forward", "Ноги: назад/вперед",
                "Moves only the pelvis and legs forward (the chest and arms stay) - the model keeps its feet far behind. Use with 'Body: back/forward'.",
                "Сдвигает вперед только таз и ноги (грудь и руки остаются) - у модели ступни далеко сзади. Вместе с «Тело: назад/вперед».",
                R(-0.30f, 0.80f, 0.01f), SignedCm, () => MimesisHands.LegsForward, v => MimesisHands.LegsForward = v, 0f);
            Num("comfort", "BodyTilt", "Body: tilt (feet forward)", "Тело: наклон (ноги вперед)",
                "Tilts the whole model back around your head: the feet come forward much more than the chest, nothing stretches. Only in your view; the shadow leans too.",
                "Наклоняет всю модель назад вокруг головы: ступни уходят вперед намного сильнее груди, ничего не растягивается. Только в твоем виде; тень тоже наклоняется.",
                R(0f, 40f, 1f), Deg, () => MimesisHands.BodyTilt, v => MimesisHands.BodyTilt = v, 0f);
            Bool("comfort", "BodyAutoLift", "Body tilt: feet stay on the ground", "Наклон тела: ступни на земле",
                "With 'Body: tilt' the model is lifted or lowered by itself so the lowest foot stays where the animation has it (no feet in the ground). 'Body: down/up' still adds on top.",
                "При «Тело: наклон» модель сама поднимается или опускается, чтобы нижняя ступня оставалась там, где ее ставит анимация (ступни не уходят в землю). «Тело: вниз/вверх» добавляется сверху.",
                () => MimesisHands.BodyAutoLift, v => MimesisHands.BodyAutoLift = v, true);
            Num("comfort", "TorsoUpright", "Torso straight", "Торс прямо",
                "After the game's animation the spine and neck are straightened towards the model's vertical: walking no longer leans the body forward and looking down no longer bends the chest and neck into the view. 0% = as animated. Only in your view - the others see the animation as before.",
                "После анимации игры позвоночник и шея выпрямляются по вертикали модели: при ходьбе тело больше не наклоняется вперед, при взгляде вниз грудь и шея не лезут в обзор. 0% = как в анимации. Только в твоем виде - другие видят анимацию как раньше.",
                R(0f, 1f, 0.05f), Pct, () => MimesisHands.TorsoUpright, v => MimesisHands.TorsoUpright = v, 0.8f);
            Num("comfort", "ArmThickness", "Arms: thickness", "Руки: толщина",
                "Makes the character's arms thinner or thicker (the hand size is set separately).", "Делает руки персонажа тоньше или толще (размер кистей - отдельно).",
                R(0.3f, 1.5f, 0.05f), X, () => MimesisHands.ArmThickness, v => MimesisHands.ArmThickness = v, 1f);
            Num("comfort", "UpperLength", "Arms: upper arm length", "Руки: длина плеча",
                "Length from the shoulder to the elbow.", "Длина от плеча до локтя.",
                R(0.5f, 2.5f, 0.05f), X, () => MimesisHands.UpperLength, v => MimesisHands.UpperLength = v, 1f);
            Num("comfort", "LowerLength", "Arms: forearm length", "Руки: длина предплечья",
                "Length from the elbow to the wrist.", "Длина от локтя до запястья.",
                R(0.5f, 2.5f, 0.05f), X, () => MimesisHands.LowerLength, v => MimesisHands.LowerLength = v, 1f);
            Num("comfort", "HandSize", "Arms: hand size", "Руки: размер кистей",
                "Size of the hands and fingers (the item in the hand keeps its real size).", "Размер кистей и пальцев (предмет в руке остается своего размера).",
                R(0.4f, 1.6f, 0.05f), X, () => MimesisHands.HandSize, v => MimesisHands.HandSize = v, 1f);
            Num("comfort", "ShoulderUp2", "Arms: shoulders down/up", "Руки: плечи вниз/вверх",
                "Moves where the arms start (the shoulders) down (-) or up (+).", "Сдвигает место, откуда растут руки (плечи), вниз (-) или вверх (+).",
                R(-0.60f, 0.40f, 0.01f), SignedCm, () => MimesisHands.ShoulderUp, v => MimesisHands.ShoulderUp = v, 0f,
                "ShoulderUp", R(-0.40f, 0.20f, 0.01f));
            Num("comfort", "ShoulderForward2", "Arms: shoulders back/forward", "Руки: плечи назад/вперед",
                "Moves the shoulders back (-) or forward (+).", "Сдвигает плечи назад (-) или вперед (+).",
                R(-0.50f, 0.60f, 0.01f), SignedCm, () => MimesisHands.ShoulderForward, v => MimesisHands.ShoulderForward = v, 0f,
                "ShoulderForward", R(-0.40f, 0.20f, 0.01f));
            Num("comfort", "ShoulderOut2", "Arms: shoulders narrower/wider", "Руки: плечи уже/шире",
                "Moves the shoulders inward (-) or outward (+).", "Сдвигает плечи внутрь (-) или наружу (+).",
                R(-0.30f, 0.60f, 0.01f), SignedCm, () => MimesisHands.ShoulderOut, v => MimesisHands.ShoulderOut = v, 0f,
                "ShoulderOut", R(-0.20f, 0.30f, 0.01f));
            Num("comfort", "Height2", "Height correction", "Поправка роста",
                "Raise or lower your eyes relative to the character.", "Поднять или опустить глаза относительно персонажа.",
                Join(-0.6f, -0.5f, -0.4f, -0.3f, -0.25f, -0.2f, -0.15f, -0.1f, -0.08f, -0.06f, -0.04f, -0.02f, 0f, 0.02f, 0.04f, 0.06f, 0.08f, 0.1f, 0.15f, 0.2f, 0.25f, 0.3f, 0.4f, 0.5f, 0.6f), SignedCm, () => HeightOffset, v => HeightOffset = v, 0f,
                "Height", new[] { -0.3f, -0.2f, -0.1f, -0.05f, 0f, 0.05f, 0.1f, 0.2f, 0.3f });

            // ---- Service
            Act("service", "MarkSmudge", "Mark: smudge in front of eyes", "Отметить: пятно перед глазами",
                "Press while you see the 'dirty screen' about a metre away. Writes what is on screen to the log.", "Нажми, когда видишь «грязный экран» примерно в метре от глаз. В лог запишется, что сейчас на экране.",
                () => MimesisEffectsProbe.Mark("smudge in front of eyes"));
            Act("service", "MarkGrain", "Mark: grain / noise", "Отметить: зерно / шум",
                "Press while the film-grain noise in the dark bothers you. Writes active effects to the log.", "Нажми, когда мешает зерно в темноте. В лог запишутся включенные эффекты.",
                () => MimesisEffectsProbe.Mark("grain / noise"));
            Act("service", "DumpItemModel", "Write item in hand model to files", "Записать модель предмета в руке в файлы",
                "Writes the meshes, materials and textures of the item in your right hand to BepInEx/MimesisVR_dump (for the wrist watch work).",
                "Записывает сетки, материалы и текстуры предмета в правой руке в BepInEx/MimesisVR_dump (для работы над часами).",
                () => MimesisModelDump.DumpHeld());
            Act("service", "DumpUi", "Write interface tree to log", "Записать интерфейс в лог",
                "Writes the list of visible interface panels to the log.", "Записывает в лог список видимых панелей интерфейса.",
                () => MimesisHud.ForceDump());
            var langNames = MimesisLoc.Langs.Select(l => l.Name).ToArray();
            Enum("service", "Language", "Language", "Язык",
                "Language of the mod's texts. 'As the game' follows the game's language (English when the mod has no such language). More languages: files in UserData\\MimesisVR\\Lang (see README).",
                "Язык текстов мода. «Как в игре» - по языку игры (английский, если такого языка в моде нет). Другие языки: файлы в UserData\\MimesisVR\\Lang (см. README).",
                new[] { "As the game" }.Concat(langNames).ToArray(), new[] { "Как в игре" }.Concat(langNames).ToArray(),
                () => LanguageIndex, SetLanguage, 0);
            All[All.Count - 1].NoSave = true;
            Bool("service", "Debug", "Debug tools", "Отладка",
                "Shows the diagnostic tools on this tab, the 'Test (host)' tab with cheats and the developer hotkeys. Turning it off also switches every cheat off.",
                "Показывает диагностические инструменты на этой вкладке, вкладку «Тест (хост)» с читами и служебные клавиши. При выключении все читы тоже выключаются.",
                () => DebugTools, v => { if (DebugTools == v) return; DebugTools = v; if (!v) MimesisCheats.DisableAll(); MimesisSettingsUI.Invalidate(); }, false);
            Enum("service", "FlipMode", "Frame flip mode", "Переворот кадра",
                "Diagnostics. Key F8.", "Диагностика. Клавиша F8.",
                new[] { "Blit", "Projection (old)" }, new[] { "Blit", "Проекция (старый)" },
                () => MimesisVrExtras.LegacyFlip ? 1 : 0, i => MimesisVrExtras.LegacyFlip = i == 1, 0);
            Enum("service", "MenuPanelMode", "Menu panel mode", "Режим панели меню",
                "Diagnostics. Key F2.", "Диагностика. Клавиша F2.",
                new[] { "Headset layer", "In-world object" }, new[] { "Слой шлема", "Объект в мире" },
                () => MimesisVrExtras.QuadLayerUnavailable ? 1 : 0, i => MimesisVrExtras.QuadLayerUnavailable = i == 1, 0);
            DefineTest();
            DefineBinds();
            Act("service", "LangTemplate", "Write a language file template", "Записать шаблон языкового файла",
                "For translators: writes UserData\\MimesisVR\\Lang\\<language>.template.txt with every text of the mod and its current translation.",
                "Для переводчиков: записывает UserData\\MimesisVR\\Lang\\<язык>.template.txt со всеми текстами мода и их текущим переводом.",
                () => MimesisLoc.WriteTemplate());
            Act("service", "ResetAll", "Reset all settings", "Сбросить все настройки",
                "Return every setting to its default (language stays).", "Вернуть все настройки по умолчанию (язык сохраняется).",
                () => ResetAll());

            // build 47: diagnostics only with "Debug tools" on
            foreach (var d in All)
                if (d.Tab == "service" && (d.Id == "MarkSmudge" || d.Id == "MarkGrain" || d.Id == "DumpItemModel" || d.Id == "DumpUi" || d.Id == "FlipMode" || d.Id == "MenuPanelMode" || d.Id == "LangTemplate"))
                    d.DebugOnly = true;
        }

        /// <summary>Build 44: button layout tab.</summary>
        private static void DefineBinds()
        {
            Act("binds", "ShowLayout", "Show the layout", "Показать схему",
                "Both controllers with what each button does now.", "Оба контроллера и что сейчас делает каждая кнопка.",
                () => MimesisSettingsUI.ShowLayout = true);
            All[All.Count - 1].NoSave = true;
            Act("binds", "PresetStd", "Standard layout", "Стандартная раскладка",
                "Attack on the right trigger, interact on the right grip, voice on the left grip, emotes on Y.",
                "Атака на правом курке, взаимодействие на правом захвате, голос на левом захвате, эмоции на Y.",
                () => MimesisBinds.ApplyPreset());
            foreach (var slot in MimesisBinds.Slots)
            {
                var sl = slot;
                Enum("binds", "B_" + sl.Id, sl.En, sl.Ru,
                    (sl.DescEn.Length > 0 ? sl.DescEn + " " : "") + "One button can hold several actions; \"-\" = not assigned.",
                    (sl.DescRu.Length > 0 ? sl.DescRu + " " : "") + "На одну кнопку можно поставить несколько действий; \"-\" = не назначено.",
                    MimesisBinds.BtnEn, MimesisBinds.BtnRu,
                    () => (int)sl.Cur, i => sl.Cur = (MimesisBinds.Btn)i, (int)sl.Std);
            }
            Bool("binds", "SwapSticks", "Move with the right stick", "Ходьба правым стиком",
                "Move with the right stick; turn and switch slots with the left one.", "Ходьба правым стиком; поворот и смена слотов левым.",
                () => MimesisBinds.SwapSticks, v => MimesisBinds.SwapSticks = v, false);
            Bool("binds", "Hints", "Button names in game prompts", "Кнопки контроллера в подсказках игры",
                "The game's prompts (\"press E\") show the controller button instead of the keyboard key.",
                "Подсказки игры («нажми E») показывают кнопку контроллера вместо клавиши.",
                () => MimesisBinds.Hints, v => MimesisBinds.Hints = v, true);
            Num("binds", "WheelReach", "Emote wheel: hand travel", "Колесо эмоций: ход руки",
                "How far to move the hand to pick an emote.", "Насколько сдвинуть руку, чтобы выбрать эмоцию.",
                R(0.04f, 0.16f, 0.01f), Cm, () => MimesisBinds.WheelReach, v => MimesisBinds.WheelReach = v, 0.08f);
            Num("binds", "WheelBg", "Emote wheel: disc opacity", "Колесо эмоций: прозрачность круга",
                "The game draws the wheel on a solid dark disc. Applies the next time the wheel opens.",
                "Игра рисует колесо на сплошном темном круге. Применяется при следующем открытии колеса.",
                R(0f, 1f, 0.05f), Pct, () => MimesisBinds.WheelBgAlpha, v => MimesisBinds.WheelBgAlpha = v, 0.35f);
            Enum("binds", "EmoteView", "Emotes: view", "Эмоции: обзор",
                "Some emotes move the body forward, in front of your eyes. Follow: the view moves with the character's head sideways. Stay: the view stays. Hide body: your own body is hidden while the emote plays (others see it).",
                "Некоторые эмоции выдвигают тело вперед, перед глазами. Следовать: обзор сдвигается вместе с головой персонажа по горизонтали. На месте: обзор не двигается. Скрыть тело: свое тело скрыто, пока идет эмоция (другие его видят).",
                new[] { "Follow the head", "Stay", "Hide body" }, new[] { "Следовать за головой", "На месте", "Скрыть тело" },
                () => MimesisHands.EmoteView, i => MimesisHands.EmoteView = i, 0);
        }

        /// <summary>Settings of one watch (build 40): body on the forearm, then its screen on top of it.</summary>
        private static void DefineWatch(string tab, MimesisWatches.Side w)
        {
            bool left = !w.Right;
            // build 41: 1 degree / 1 mm steps (the 5 degree / 5 mm values move over by value)
            float[] rot = R(-180f, 180f, 1f), rot5 = R(-180f, 180f, 5f);
            Bool(tab, "EditByHand", "Move the watches and screens by hand", "Двигать часы и экраны рукой",
                "While on, the watch bodies glow: grip a body or its screen with the other hand and move and turn it as you like; let go - it stays, the values below follow. The screen keeps its place on the body. Not saved (off at the next start).",
                "Пока включено, корпуса часов светятся: возьми корпус или его экран грипом другой руки и двигай и крути как удобно; отпустил - остается, значения ниже следуют за ним. Экран держится за корпус. Не сохраняется (при следующем запуске выключено).",
                () => MimesisWatches.EditMode, v => MimesisWatches.EditMode = v, false);
            All[All.Count - 1].NoSave = true;
            Bool(tab, "Model", "Watch body", "Корпус часов",
                "The body under the screen: the lid of the game's compass with the game's own texture. Off: only the screen.",
                "Корпус под экраном: крышка компаса игры с ее текстурой. Выкл: только экран.",
                () => w.Model, v => w.Model = v, false);
            Enum(tab, "Fit", "Fit", "Подгонка",
                "Sizes as set; or the body around the screen (its length / width are then ignored); or the screen as large as fits on the body ('Screen: size' is then ignored).",
                "Размеры как заданы; или корпус по экрану (тогда его длина / ширина не используются); или экран по корпусу - самый большой, что помещается («Экран: размер» тогда не используется).",
                new[] { "As set", "Body to the screen", "Screen to the body" }, new[] { "Как задано", "Корпус по экрану", "Экран по корпусу" },
                () => w.Fit, i => w.Fit = i, 0);
            if (left)
                Bool(tab, "BagRow", "Bag slots in a second row", "Ячейки сумки вторым рядом",
                    "On the watch the bag slots go to a row under the main ones - only while you have a bag, otherwise the panel is one row.",
                    "На часах ячейки сумки идут вторым рядом под основными - только пока есть сумка, без нее панель в один ряд.",
                    () => MimesisWatches.BagRow, v => MimesisWatches.BagRow = v, true);
            Num(tab, "BgAlpha", "Screen backing", "Фон экрана",
                "How dark the backing behind the screen is (0% = none).", "Насколько темный фон под экраном (0% = без фона).",
                R(0f, 1f, 0.05f), Pct, () => w.BgAlpha, v => w.BgAlpha = v, w.BgAlpha);
            Num(tab, "FaceEyes", "Screen: turns to the eyes", "Экран: поворот к глазам",
                "The model's elbow does not rise when you bring the hand to the eyes, so the watch stays steep: the screen turns towards your eyes by this share, the body stays on the arm. 0% = flat on the body.",
                "Локоть модели не поднимается, когда подносишь руку к глазам, и часы остаются наклонными: экран поворачивается к глазам на эту долю, корпус остается на руке. 0% = лежит на корпусе.",
                R(0f, 1f, 0.05f), Pct, () => w.FaceEyes, v => w.FaceEyes = v, w.FaceEyes);
            Num(tab, "Dial", "Watch face", "Циферблат",
                "A dark eight-sided face over the whole top of the body, so the screen does not sit on bare wood (0% = none). Also seen on the monitor.",
                "Темный восьмиугольный циферблат на весь верх корпуса, чтобы экран не лежал на голом дереве (0% = нет). Виден и на мониторе.",
                R(0f, 1f, 0.05f), Pct, () => w.Dial, v => w.Dial = v, w.Dial);
            Bool(tab, "Shadow", "Watch shadow", "Тень от часов",
                "The body casts a shadow (a little extra work for the video card).", "Корпус отбрасывает тень (немного лишней работы видеокарте).",
                () => w.Shadow, v => w.Shadow = v, false);
            Num(tab, "TwistFollow", "Watch: turns with the skin", "Часы: поворот вместе с кожей",
                "How much the watch turns when you turn the hand: 100% = exactly like the forearm skin under it (no edges through the watch), 0% = not at all.",
                "Насколько часы поворачиваются, когда поворачиваешь кисть: 100% = ровно как кожа предплечья под ними (грани не лезут сквозь часы), 0% = никак.",
                R(0f, 1.5f, 0.05f), Pct, () => w.TwistFollow, v => w.TwistFollow = v, 1f);
            Num(tab, "Along2", "Watch: shift along the arm", "Часы: сдвиг вдоль руки",
                "Moves the watch along the forearm (+ = towards the hand).", "Сдвиг часов вдоль предплечья (+ = к кисти).",
                R(-0.20f, 0.20f, 0.001f), SignedCm1, () => w.Along, v => w.Along = v, w.Along, "Along", R(-0.20f, 0.20f, 0.005f));
            Num(tab, "Across2", "Watch: shift across the arm", "Часы: сдвиг поперек руки",
                "Moves the watch to the side of the forearm.", "Сдвиг часов вбок по предплечью.",
                R(-0.15f, 0.15f, 0.001f), SignedCm1, () => w.Across, v => w.Across = v, w.Across, "Across", R(-0.15f, 0.15f, 0.005f));
            Num(tab, "Height2", "Watch: height above the arm", "Часы: высота над рукой",
                "Lifts the watch off the arm (+) or lowers it into it (-).", "Поднять часы над рукой (+) или опустить в нее (-).",
                R(-0.10f, 0.20f, 0.001f), SignedCm1, () => w.Height, v => w.Height = v, w.Height, "Height", R(-0.10f, 0.20f, 0.005f));
            Num(tab, "Roll2", "Watch: turn on the arm", "Часы: поворот на руке",
                "Turns the watch flat on the arm (around the axis out of the arm).", "Поворот часов, лежащих на руке (вокруг оси, выходящей из руки).",
                rot, Deg, () => w.Roll, v => w.Roll = v, w.Roll, "Roll", rot5);
            Num(tab, "Tilt2", "Watch: tilt along the arm", "Часы: наклон вдоль руки",
                "Tilts the watch: the end towards the hand up (+) or down (-).", "Наклон часов: край к кисти вверх (+) или вниз (-).",
                rot, Deg, () => w.Tilt, v => w.Tilt = v, w.Tilt, "Tilt", rot5);
            Num(tab, "Bank2", "Watch: tilt across the arm", "Часы: наклон поперек руки",
                "Tilts the watch sideways (around the arm). 180 turns it over.", "Наклон часов вбок (вокруг руки). 180 переворачивает.",
                rot, Deg, () => w.Bank, v => w.Bank = v, w.Bank, "Bank", rot5);
            Num(tab, "Length", "Watch: length", "Часы: длина",
                "Size of the body along the arm.", "Размер корпуса вдоль руки.",
                R(0.03f, 0.30f, 0.005f), Cm, () => w.Length, v => w.Length = v, w.Length);
            Num(tab, "Width", "Watch: width", "Часы: ширина",
                "Size of the body across the arm.", "Размер корпуса поперек руки.",
                R(0.02f, 0.15f, 0.005f), Cm, () => w.Width, v => w.Width = v, w.Width);
            Num(tab, "Thick", "Watch: thickness", "Часы: толщина",
                "Thickness of the body.", "Толщина корпуса.",
                R(0.002f, 0.04f, 0.001f), Mm, () => w.Thick, v => w.Thick = v, w.Thick);
            Num(tab, "SAlong2", "Screen: shift along", "Экран: сдвиг вдоль",
                "Moves the screen on the watch along the arm.", "Сдвиг экрана на часах вдоль руки.",
                R(-0.15f, 0.15f, 0.001f), SignedCm1, () => w.SAlong, v => w.SAlong = v, w.SAlong, "SAlong", R(-0.15f, 0.15f, 0.005f));
            Num(tab, "SAcross2", "Screen: shift across", "Экран: сдвиг поперек",
                "Moves the screen on the watch across the arm.", "Сдвиг экрана на часах поперек руки.",
                R(-0.15f, 0.15f, 0.001f), SignedCm1, () => w.SAcross, v => w.SAcross = v, w.SAcross, "SAcross", R(-0.15f, 0.15f, 0.005f));
            Num(tab, "SHeight", "Screen: height above the watch", "Экран: высота над часами",
                "Distance from the top of the watch body.", "Расстояние от верха корпуса.",
                R(-0.03f, 0.10f, 0.001f), SignedMm, () => w.SHeight, v => w.SHeight = v, w.SHeight);
            Num(tab, "SRoll2", "Screen: turn", "Экран: поворот",
                "Turns the screen in its plane (+ = counterclockwise). 90 / -90 = along the arm.", "Поворот экрана в его плоскости (+ = против часовой). 90 / -90 = вдоль руки.",
                rot, Deg, () => w.SRoll, v => w.SRoll = v, w.SRoll, "SRoll", rot5);
            Num(tab, "STilt2", "Screen: tilt", "Экран: наклон",
                "Tilts the screen towards your eyes. 180 flips it if it faces the wrong way.", "Наклон экрана к глазам. 180 переворачивает, если он смотрит не туда.",
                rot, Deg, () => w.STilt, v => w.STilt = v, w.STilt, "STilt", rot5);
            Num(tab, "SBank2", "Screen: tilt sideways", "Экран: наклон вбок",
                "Tilts the screen around its other axis.", "Наклон экрана вокруг второй оси.",
                rot, Deg, () => w.SBank, v => w.SBank = v, w.SBank, "SBank", rot5);
            Num(tab, "SScale", "Screen: size", "Экран: размер",
                left ? "Size of the wrist panel on the watch (x1 = the 'Wrist inventory size' on the Interface tab)." : "Size of the screen (x1 = 6 cm wide).",
                left ? "Размер панели на часах (x1 = «Размер инвентаря на запястье» на вкладке «Интерфейс»)." : "Размер экрана (x1 = 6 см в ширину).",
                R(0.1f, 3f, 0.05f), X, () => w.SScale, v => w.SScale = v, w.SScale);
            if (!left)
                Enum(tab, "GearPlace", "Settings gear", "Шестеренка настроек",
                    "Where the gear that opens these settings is. On the left: next to the panel, at the elbow end of the left watch (shows when you look at it) or in its own cell at the start of the panel - press with the right hand. On the right: at the elbow end of the right watch (shows when you look at it) or in the corner of the right screen - press with the left hand.",
                    "Где шестеренка, открывающая эти настройки. Слева: рядом с панелью, на торце левых часов у локтя (проявляется при взгляде) или в своей ячейке в начале панели - нажимать правой рукой. Справа: на торце правых часов у локтя (проявляется при взгляде) или в углу правого экрана - нажимать левой рукой.",
                    new[] { "Left, next to the panel", "Right watch, end", "Right screen, corner", "Left watch, end", "Left panel, own cell", "Left panel, corner" }, new[] { "Слева, рядом с панелью", "Правые часы, торец", "Правый экран, угол", "Левые часы, торец", "Левая панель, своя ячейка", "Левая панель, угол" },
                    () => MimesisWatches.GearPlace, i => MimesisWatches.GearPlace = i, 1);
        }

        private static void DefineTest()
        {
            const string hostEn = " Works only when you host the lobby.", hostRu = " Работает, только когда ты хост лобби.";
            Enum("test", "TestForAll", "Cheats for", "Читы для",
                "Who the health, sprint and revive cheats apply to." + hostEn, "Для кого действуют бессмертие, бег, здоровье и воскрешение." + hostRu,
                new[] { "Everyone", "Only me" }, new[] { "Всех игроков", "Только меня" },
                () => MimesisCheats.ForAll ? 0 : 1, i => MimesisCheats.ForAll = i == 0, 0);
            Bool("test", "TestUndying", "Undying", "Бессмертие",
                "Health cannot drop to zero." + hostEn, "Здоровье не падает до нуля." + hostRu,
                () => MimesisCheats.Undying, v => MimesisCheats.Undying = v, false);
            All[All.Count - 1].NoSave = true;
            Bool("test", "TestStamina", "Endless sprint", "Бесконечный бег",
                "Stamina is refilled all the time." + hostEn, "Выносливость постоянно восполняется." + hostRu,
                () => MimesisCheats.EndlessStamina, v => MimesisCheats.EndlessStamina = v, false);
            All[All.Count - 1].NoSave = true;
            Bool("test", "TestSlowMonsters", "Slow monsters", "Медленные монстры",
                "Monsters move at 30% speed." + hostEn, "Монстры двигаются на 30% скорости." + hostRu,
                () => MimesisCheats.SlowMonsters, v => MimesisCheats.SlowMonsters = v, false);
            All[All.Count - 1].NoSave = true;
            Bool("test", "TestStopAi", "Monsters frozen (AI off)", "Монстры стоят (ИИ выкл)",
                "Monster AI is switched off: they stand still." + hostEn, "Искусственный интеллект монстров выключен: они стоят на месте." + hostRu,
                () => MimesisCheats.StopAi, v => MimesisCheats.StopAi = v, false);
            All[All.Count - 1].NoSave = true;
            Act("test", "TestHeal", "Restore health", "Восстановить здоровье",
                "Full health now." + hostEn, "Полное здоровье сейчас." + hostRu, () => MimesisCheats.Command("recoverhp", "", true));
            Act("test", "TestConta", "Remove contamination", "Снять заражение",
                "Clears contamination (radiation) of the whole team." + hostEn, "Снимает заражение (радиацию) у всей команды." + hostRu, () => MimesisCheats.Command("recoverconta"));
            Act("test", "TestRevive", "Revive", "Воскресить",
                "Brings dead players back." + hostEn, "Возвращает погибших игроков." + hostRu, () => MimesisCheats.Command("revive", "", true));
            Act("test", "TestMoney", "Money +1000", "Деньги +1000",
                "Adds 1000 to the team money (in the tram / maintenance only)." + hostEn, "Добавляет 1000 к деньгам команды (только в трамвае / на базе)." + hostRu,
                () => MimesisCheats.Command("addcurrency", "amount=1000"));
            Act("test", "TestItem", "Item: spawn next to me", "Предмет: положить рядом",
                "Stick left/right chooses the item from the game's full list, trigger spawns it next to you." + hostEn,
                "Стик влево/вправо - выбрать предмет из полного списка игры, курок - положить рядом с тобой." + hostRu,
                () => MimesisCheats.SpawnSelected(false));
            Act("test", "TestLootAll", "Item: as loot all over the map", "Предмет: лутом по всей карте",
                "Puts the item chosen above on every loot point of the level." + hostEn, "Раскладывает выбранный выше предмет во все точки лута на уровне." + hostRu,
                () => MimesisCheats.SpawnSelected(true));
            Act("test", "TestTime", "Game time +1 hour", "Время игры +1 час",
                "Moves the level clock one hour forward (on a level only)." + hostEn, "Сдвигает часы уровня на час вперед (только на уровне)." + hostRu,
                () => MimesisCheats.Command("addgametime", "hour=1"));
            Act("test", "TestTimeBack", "Game time -1 hour", "Время игры -1 час",
                "Moves the level clock one hour back (on a level only)." + hostEn, "Сдвигает часы уровня на час назад (только на уровне)." + hostRu,
                () => MimesisCheats.Command("addgametime", "hour=-1"));
            Act("test", "TestKillMonsters", "Remove all monsters", "Убрать всех монстров",
                "Kills every monster on the level." + hostEn, "Убивает всех монстров на уровне." + hostRu, () => MimesisCheats.Command("killallmonster"));
        }

        // Presets: monitor, scale, msaa, frame limit, shadow distance, shadow res, textures, lod, fog, ssao, decals
        private static readonly (bool mon, float scale, int msaa, int limit, float shadows, int sres, int tex, float lod, bool fog, bool ssao, bool decals)[] Presets =
        {
            default,
            (false, 0.6f, 1, 1, 0f, 1, 2, 0.5f, false, false, false),     // very low
            (false, 0.7f, 1, 1, 0.5f, 1, 1, 0.75f, false, false, false),  // low
            (false, 0.8f, 2, 1, 0.75f, 2, 0, 1f, true, false, true),      // medium
            (false, 0.9f, 4, 1, 1f, 0, 0, 1f, true, true, true),          // good
            (true, 1f, 4, 1, 1f, 0, 0, 1f, true, true, true),             // high = as in game
        };

        private static int PresetNow()
        {
            for (int i = 1; i < Presets.Length; i++)
            {
                var p = Presets[i];
                if (MimesisMonitor.RenderMonitor == p.mon && Mathf.Approximately(MimesisImage.RenderScale, p.scale) && MimesisVrExtras.MsaaSamples == p.msaa
                    && MimesisImage.FrameLimit == p.limit && Mathf.Approximately(MimesisImage.ShadowScale, p.shadows) && MimesisImage.VolumetricFog == p.fog
                    && MimesisImage.Ssao == p.ssao && MimesisImage.Decals == p.decals && MimesisImage.ShadowRes == p.sres
                    && MimesisImage.TextureLevel == p.tex && Mathf.Approximately(MimesisImage.LodScale, p.lod)) return i;
            }
            return 0;
        }

        private static void ApplyPreset(int i)
        {
            if (i <= 0 || i >= Presets.Length) return;
            var p = Presets[i];
            MimesisMonitor.RenderMonitor = p.mon; MimesisImage.RenderScale = p.scale; MimesisVrExtras.MsaaSamples = p.msaa;
            MimesisImage.FrameLimit = p.limit; MimesisImage.ShadowScale = p.shadows; MimesisImage.VolumetricFog = p.fog;
            MimesisImage.Ssao = p.ssao; MimesisImage.Decals = p.decals;
            MimesisImage.ShadowRes = p.sres; MimesisImage.TextureLevel = p.tex; MimesisImage.LodScale = p.lod;
            MimesisImage.ApplyNow();
            _nextSync = 0f; // write the new values to the config
        }

        public static Action RecenterHandler;
        private static void VrVisualizationRecenter() { RecenterHandler?.Invoke(); }
    }
}
