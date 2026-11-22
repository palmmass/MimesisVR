using System.Linq;
using HarmonyLib;
using Mimic.InputSystem;
using UnityVRMod.Core;
using GameInputAction = Mimic.InputSystem.InputAction;

namespace UnityVRMod.Mimesis
{
    /// <summary>
    /// MIMESIS VR (build 44): button layout. Every game action of the list can be put on any controller button,
    /// presets (standard / left-handed), button names in the game's own key prompts and the emote wheel
    /// (the game's gamepad emote panel, picked by moving the hand or with the other stick).
    /// </summary>
    internal static class MimesisBinds
    {
        public enum Btn { None, A, B, X, Y, LStick, RStick, LTrigger, RTrigger, LGrip, RGrip }

        public sealed class Slot
        {
            public string Id, En, Ru, DescEn, DescRu;
            public GameInputAction Action;
            public Btn Std;
            public Btn Cur;
        }

        public static readonly Slot[] Slots =
        {
            new Slot { Id = "Fire", Action = GameInputAction.FireWeapon, Std = Btn.RTrigger,
                En = "Attack / use item", Ru = "Атака / применить предмет",
                DescEn = "The game's left mouse button. Swinging the hand also attacks (Hands tab).", DescRu = "Левая кнопка мыши в игре. Взмах рукой тоже атакует (вкладка «Руки и предметы»)." },
            new Slot { Id = "Fire2", Action = GameInputAction.FireWeapon2, Std = Btn.LTrigger,
                En = "Second action", Ru = "Второе действие",
                DescEn = "The game's right mouse button (second use of the item in hand).", DescRu = "Правая кнопка мыши в игре (второе применение предмета в руке)." },
            new Slot { Id = "Interact", Action = GameInputAction.Interact, Std = Btn.RGrip,
                En = "Interact (take, open)", Ru = "Взаимодействие (взять, открыть)",
                DescEn = "Take items, open doors, press buttons.", DescRu = "Брать предметы, открывать двери, нажимать кнопки." },
            new Slot { Id = "Voice", Action = GameInputAction.PushToTalk, Std = Btn.LGrip,
                En = "Voice (push to talk)", Ru = "Голос (нажми и говори)",
                DescEn = "Works in menus and with the mod settings open too.", DescRu = "Работает и в меню, и при открытых настройках мода." },
            new Slot { Id = "Jump", Action = GameInputAction.Jump, Std = Btn.A,
                En = "Jump", Ru = "Прыжок", DescEn = "", DescRu = "" },
            new Slot { Id = "Drop", Action = GameInputAction.DropItemOnHand, Std = Btn.B,
                En = "Drop item in hand", Ru = "Выбросить предмет из руки", DescEn = "", DescRu = "" },
            new Slot { Id = "Status", Action = GameInputAction.ShowStatus, Std = Btn.X,
                En = "Status", Ru = "Состояние",
                DescEn = "The game's status screen (Tab).", DescRu = "Экран состояния игры (Tab)." },
            new Slot { Id = "Emote", Action = GameInputAction.EmotePanel, Std = Btn.Y,
                En = "Emote wheel", Ru = "Колесо эмоций",
                DescEn = "Hold: the wheel opens. Move the hand towards an emote (or tilt the other stick) and release.",
                DescRu = "Держи: откроется колесо. Сдвинь руку к эмоции (или наклони другой стик) и отпусти." },
            new Slot { Id = "Reload", Action = GameInputAction.ReloadWeapon, Std = Btn.RStick,
                En = "Reload", Ru = "Перезарядка", DescEn = "", DescRu = "" },
            new Slot { Id = "Run", Action = GameInputAction.Run, Std = Btn.LStick,
                En = "Run", Ru = "Бег",
                DescEn = "Toggles running (stops when you stop moving), or hold - see Sprint on the Controls tab.",
                DescRu = "Включает бег (выключается, когда остановишься) или удержание - см. «Бег» на вкладке «Управление»." },
            new Slot { Id = "Tram", Action = GameInputAction.TramSettings, Std = Btn.None,
                En = "Tram settings (host)", Ru = "Настройки трамвая (хост)",
                DescEn = "The game's tram settings key. Off by default.", DescRu = "Клавиша настроек трамвая в игре. По умолчанию не назначена." },
        };

        static MimesisBinds() { foreach (var s in Slots) s.Cur = s.Std; }

        /// <summary>Move with the right stick, turn and switch slots with the left one.</summary>
        public static bool SwapSticks;
        /// <summary>Controller button names in the game's key prompts.</summary>
        public static bool Hints = true;
        /// <summary>Emote wheel: hand distance (m) that counts as a full stick tilt.</summary>
        public static float WheelReach = 0.08f;
        /// <summary>Opacity of the emote wheel's disc (the game: 1).</summary>
        public static float WheelBgAlpha = 0.35f;

        public static readonly string[] BtnEn = { "-", "A", "B", "X", "Y", "Left stick click", "Right stick click", "Left trigger", "Right trigger", "Left grip", "Right grip" };
        public static readonly string[] BtnRu = { "-", "A", "B", "X", "Y", "Левый стик (нажать)", "Правый стик (нажать)", "Левый курок", "Правый курок", "Левый захват", "Правый захват" };
        /// <summary>Short names for the key caps of the game's prompts.</summary>
        public static readonly string[] BtnShort = { "", "A", "B", "X", "Y", "LS", "RS", "LT", "RT", "LG", "RG" };

        public static Slot Find(GameInputAction a)
        {
            foreach (var s in Slots) if (s.Action == a) return s;
            return null;
        }

        public static bool IsLeftHand(Btn b) => b == Btn.X || b == Btn.Y || b == Btn.LStick || b == Btn.LTrigger || b == Btn.LGrip;

        /// <summary>Standard layout (the left-handed preset was dropped: items stay in the right hand of the character).</summary>
        public static void ApplyPreset()
        {
            foreach (var s in Slots)
            {
                var d = MimesisSettings.All.Find(x => x.Tab == "binds" && x.Id == "B_" + s.Id);
                if (d != null) MimesisSettings.Change(d, (int)s.Std); else s.Cur = s.Std;
            }
            var sw = MimesisSettings.All.Find(x => x.Tab == "binds" && x.Id == "SwapSticks");
            if (sw != null) MimesisSettings.Change(sw, 0); else SwapSticks = false;
            VRModCore.Log("[MimesisVR] Button layout: standard");
        }

        /// <summary>Buttons that hold more than one action (shown in the layout view).</summary>
        public static string Conflicts(int lang)
        {
            var parts = new List<string>();
            foreach (Btn b in System.Enum.GetValues(typeof(Btn)))
            {
                if (b == Btn.None) continue;
                var on = Slots.Where(s => s.Cur == b).ToList();
                if (on.Count > 1) parts.Add(MimesisLoc.Tr(BtnEn[(int)b], BtnRu[(int)b]) + ": " + string.Join(" + ", on.Select(s => MimesisLoc.Tr(s.En, s.Ru))));
            }
            return string.Join("; ", parts);
        }

        // ---------------------------------------------------------------- patches
        private static AccessTools.FieldRef<UIPrefab_GamepadEmote, bool> _didUnlock;
        private static MethodInfo _resetIcons;

        public static void Install(HarmonyLib.Harmony h)
        {
            try
            {
                h.Patch(AccessTools.Method(typeof(InputManager), "GetKeyBind"), postfix: new HarmonyMethod(typeof(MimesisBinds), nameof(GetKeyBind_Postfix)));
                _resetIcons = AccessTools.Method(typeof(UIPrefab_KeyIcon), "ResetIcons");
                h.Patch(AccessTools.Method(typeof(UIPrefab_KeyIcon), "SetKey"), prefix: new HarmonyMethod(typeof(MimesisBinds), nameof(KeyIcon_SetKey_Prefix)));
            }
            catch (Exception ex) { VRModCore.LogError("[MimesisVR] Button hint patches failed:", ex); }
            try
            {
                _didUnlock = AccessTools.FieldRefAccess<UIPrefab_GamepadEmote, bool>("_didUnlockCursor");
                h.Patch(AccessTools.Method(typeof(UIPrefab_GamepadEmote), "OnShow"), postfix: new HarmonyMethod(typeof(MimesisBinds), nameof(Emote_OnShow_Postfix)));
                h.Patch(AccessTools.Method(typeof(UIPrefab_GamepadEmote), "TryGetInputContext"), prefix: new HarmonyMethod(typeof(MimesisBinds), nameof(Emote_Context_Prefix)));
                h.Patch(AccessTools.Method(typeof(UIPrefab_GamepadEmote), "TryGetSelectionDirection"), prefix: new HarmonyMethod(typeof(MimesisBinds), nameof(Emote_Direction_Prefix)));
            }
            catch (Exception ex) { VRModCore.LogError("[MimesisVR] Emote wheel patches failed:", ex); }
            try
            {
                // build 45: the server cancels an emote on any move or view change (head turn > 1 degree) - not sent while it plays
                h.Patch(AccessTools.Method(typeof(Mimic.Actors.ProtoActor), "SendTransform"), prefix: new HarmonyMethod(typeof(MimesisBinds), nameof(SendTransform_Prefix)));
            }
            catch (Exception ex) { VRModCore.LogError("[MimesisVR] Emote hold patch failed:", ex); }
        }

        public static bool SendTransform_Prefix(Mimic.Actors.ProtoActor __instance)
        {
            if (!Vr) return true;
            bool starting = Time.unscaledTime - _wheelReleasedAt < 1f && MimesisInput.Controllers != null && MimesisInput.MoveStick.magnitude < 0.3f;
            if (!MimesisHands.LocalEmote && !starting) return true;
            return !ReferenceEquals(__instance, MimesisInput.LocalActor);
        }

        private static bool Vr => MimesisInput.VrActive;

        // ---- key prompts: the game asks for the keyboard key of an action -> our button name
        public static void GetKeyBind_Postfix(GameInputAction action, bool gamepad, ref string __result)
        {
            if (!Vr || !Hints || gamepad) return;
            try
            {
                string s = Label(action);
                if (s != null) __result = "vr_" + s;
            }
            catch { }
        }

        private static string Label(GameInputAction a)
        {
            switch (a)
            {
                case GameInputAction.MoveForward: case GameInputAction.MoveBackward:
                case GameInputAction.MoveLeft: case GameInputAction.MoveRight:
                    return SwapSticks ? "RS" : "LS";
                case GameInputAction.Escape:
                    return "Menu";
                case GameInputAction.PreviousSpectatorTarget: a = GameInputAction.FireWeapon; break;
                case GameInputAction.NextSpectatorTarget: a = GameInputAction.FireWeapon2; break;
            }
            var slot = Find(a);
            if (slot == null || slot.Cur == Btn.None) return null;
            return BtnShort[(int)slot.Cur];
        }

        public static bool KeyIcon_SetKey_Prefix(UIPrefab_KeyIcon __instance, string _key)
        {
            if (string.IsNullOrEmpty(_key) || !_key.StartsWith("vr_")) return true;
            try
            {
                _resetIcons?.Invoke(__instance, null);
                var t = __instance.UE_UI_KeyText;
                if (t != null)
                {
                    if (!t.enableAutoSizing) { t.fontSizeMax = t.fontSize; t.fontSizeMin = Mathf.Max(6f, t.fontSize * 0.45f); t.enableAutoSizing = true; }
                    t.text = _key.Substring(3);
                }
                __instance.UE_Key.gameObject.SetActive(true);
                return false;
            }
            catch { return true; }
        }

        // ---- emote wheel: the game's gamepad panel, driven by the hand (or the other stick)
        public static bool WheelOpen;
        private static bool _wheelHeld;
        private static Vector3 _wheelStart;
        private static bool _wheelLeftHand;
        private static float _wheelReleasedAt = -10f;

        /// <summary>Called each frame from the input postfix: tracks the hand while the emote button is held.</summary>
        public static void TrackWheel(bool held, bool pressed)
        {
            var c = MimesisInput.Controllers;
            var slot = Find(GameInputAction.EmotePanel);
            _wheelLeftHand = slot == null || IsLeftHand(slot.Cur);
            if (pressed && c != null) _wheelStart = _wheelLeftHand ? c.LeftPos : c.RightPos;
            if (_wheelHeld && !held) _wheelReleasedAt = Time.unscaledTime; // an emote may be starting (server answer pending)
            _wheelHeld = held;
        }

        private static readonly HashSet<int> _wheelChecked = new();

        /// <summary>The wheel's graphics with a special shader (blur of the picture behind it and the like) come out
        /// as a solid black area in the VR interface picture: switch them off.</summary>
        private static void CleanWheel(UIPrefab_GamepadEmote w)
        {
            if (!_wheelChecked.Add(w.GetInstanceID())) return;
            var sb = new System.Text.StringBuilder("[MimesisVR] Emote wheel graphics:");
            foreach (var g in w.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
            {
                var m = g.material;
                string sh = m != null && m.shader != null ? m.shader.name : "-";
                var rt = g.rectTransform;
                bool odd = !(sh == "UI/Default" || sh.Contains("TextMeshPro") || sh == "Sprites/Default" || sh == "-");

                sb.Append($" {g.name}[{g.GetType().Name} {sh} {g.color} {rt.rect.width:0}x{rt.rect.height:0}{(odd ? " HIDDEN" : "")}]");
                if (odd) g.enabled = false;
            }
            VRModCore.Log(sb.ToString());
        }

        public static void Emote_OnShow_Postfix(UIPrefab_GamepadEmote __instance)
        {
            if (!Vr) return;
            WheelOpen = true;
            try { CleanWheel(__instance); } catch (Exception ex) { VRModCore.LogWarning("[MimesisVR] Emote wheel clean: " + ex.Message); }
            try
            {
                // build 45: the wheel's own disc is solid dark grey - in the headset it reads as a black hole: see-through
                foreach (var g in __instance.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                    if (g.name == "Bg") { var col = g.color; col.a = WheelBgAlpha; g.color = col; }
            }
            catch { }
            try
            {
                // The game frees the mouse for the keyboard wheel; in VR that would open the menu panel instead
                if (_didUnlock != null && _didUnlock(__instance))
                {
                    _didUnlock(__instance) = false;
                    var im = Hub.s != null ? AccessTools.Property(typeof(Hub), "inputman")?.GetValue(Hub.s) as InputManager : null;
                    im?.SetCapturing(on: true);
                }
            }
            catch (Exception ex) { VRModCore.LogWarning("[MimesisVR] Emote wheel: " + ex.Message); }
        }

        public static bool Emote_Context_Prefix(ref bool isKBM, ref UnityEngine.InputSystem.Gamepad gamepad, ref bool __result)
        {
            if (!Vr) return true;
            isKBM = false; gamepad = null; __result = Hub.s != null;
            return false;
        }

        public static bool Emote_Direction_Prefix(UIPrefab_GamepadEmote __instance, ref Vector2 direction, ref float deadzone, ref bool __result)
        {
            if (!Vr) return true;
            WheelOpen = __instance.gameObject.activeInHierarchy;
            direction = WheelDirection();
            deadzone = 0.3f;
            __result = true;
            return false;
        }

        public static Vector2 WheelDirection()
        {
            var c = MimesisInput.Controllers;
            if (c == null) return Vector2.zero;
            // The stick of the other hand wins when tilted
            Vector2 st = _wheelLeftHand ? c.RightStick : c.LeftStick;
            if (st.magnitude > 0.3f) return st;
            if (!_wheelHeld) return Vector2.zero;
            Vector3 p = _wheelLeftHand ? c.LeftPos : c.RightPos;
            Vector3 d = Quaternion.Inverse(MimesisInput.HeadLocalRot) * (p - _wheelStart);
            return new Vector2(d.x, d.y) / Mathf.Max(0.02f, WheelReach);
        }

        /// <summary>While the wheel is up, the other stick picks an emote instead of turning / moving.</summary>
        public static bool WheelUsesStick(bool rightStick)
        {
            if (!_wheelHeld) return false;
            return rightStick == _wheelLeftHand;
        }

        // ---------------------------------------------------------------- emote playing
        private static FieldInfo _emoteField;
        private static MethodInfo _isPlaying, _stop;

        private static object EmotePlayerOf(Mimic.Actors.ProtoActor a)
        {
            if (a == null) return null;
            _emoteField ??= AccessTools.Field(typeof(Mimic.Actors.ProtoActor), "emotePlayer");
            return _emoteField?.GetValue(a);
        }

        /// <summary>The local character plays an emote: arms and body are left to the animation.</summary>
        public static bool LocalEmote(Mimic.Actors.ProtoActor a)
        {
            try
            {
                var ep = EmotePlayerOf(a);
                if (ep == null) return false;
                _isPlaying ??= AccessTools.Method(ep.GetType(), "IsEmotePlaying");
                return _isPlaying != null && (bool)_isPlaying.Invoke(ep, null);
            }
            catch { return false; }
        }

        public static void StopLocalEmote(Mimic.Actors.ProtoActor a)
        {
            try
            {
                var ep = EmotePlayerOf(a);
                if (ep == null) return;
                _stop ??= AccessTools.Method(ep.GetType(), "StopEmote");
                _stop?.Invoke(ep, null);
                VRModCore.Log("[MimesisVR] Emote stopped by input.");
            }
            catch (Exception ex) { VRModCore.LogWarning("[MimesisVR] Stop emote: " + ex.Message); }
        }

        // ---------------------------------------------------------------- D3D11 check
        /// <summary>VR needs Direct3D 11. Under MelonLoader it comes from the line force-d3d11=1 in boot.config,
        /// which a Steam update or file check removes: put it back and tell the player to restart.</summary>
        public static bool D3D11Missing;
        public static string D3D11Note;

        public static void CheckD3D11()
        {
            try
            {
                var api = SystemInfo.graphicsDeviceType;
                if (api == UnityEngine.Rendering.GraphicsDeviceType.Direct3D11) return;
                D3D11Missing = true;
                // BepInEx: doorstop gives the game its own boot config (BepInEx\config\mimesisvr_boot.config)
                string boot = MvrPaths.LoaderName == "BepInEx"
                    ? Path.Combine(MvrPaths.ConfigDir, "mimesisvr_boot.config")
                    : Path.Combine(Application.dataPath, "boot.config");
                string text = File.Exists(boot) ? File.ReadAllText(boot) : "";
                bool has = text.Split('\n').Any(l => l.Trim().StartsWith("force-d3d11", StringComparison.OrdinalIgnoreCase));
                if (!has)
                {
                    string add = (text.Length > 0 && !text.EndsWith("\n") ? "\n" : "") + "force-d3d11=1\n";
                    File.AppendAllText(boot, add);
                    D3D11Note = "boot.config fixed";
                    VRModCore.LogWarning($"[MimesisVR] The game runs on {api}; VR needs Direct3D 11. Added force-d3d11=1 to {boot} - restart the game.");
                }
                else
                {
                    D3D11Note = "launch option";
                    VRModCore.LogWarning($"[MimesisVR] The game runs on {api} although boot.config has force-d3d11. Add -force-d3d11 to the Steam launch options.");
                }
                var go = new GameObject("MimesisVR_D3D11Notice");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<D3D11Notice>();
            }
            catch (Exception ex) { VRModCore.LogWarning("[MimesisVR] D3D11 check: " + ex.Message); }
        }
    }

    /// <summary>On-screen note for the flat monitor (VR does not start without Direct3D 11).</summary>
    internal class D3D11Notice : MonoBehaviour
    {
        private float _until;
        private GUIStyle _st;

        private void Start() { _until = Time.unscaledTime + 60f; }

        private void OnGUI()
        {
            if (Time.unscaledTime > _until) { Destroy(gameObject); return; }
            _st ??= new GUIStyle(GUI.skin.box) { fontSize = Mathf.Max(18, Screen.height / 40), wordWrap = true, alignment = TextAnchor.MiddleCenter };
            string msg = MimesisBinds.D3D11Note == "boot.config fixed"
                ? "MIMESIS VR: VR needs Direct3D 11. The setting has been restored - please restart the game.\nMIMESIS VR: для VR нужен Direct3D 11. Настройка восстановлена - перезапусти игру."
                : "MIMESIS VR: VR needs Direct3D 11. Add -force-d3d11 to the game's Steam launch options.\nMIMESIS VR: для VR нужен Direct3D 11. Добавь -force-d3d11 в параметры запуска игры в Steam.";
            float w = Screen.width * 0.7f, h = Screen.height * 0.16f;
            GUI.Box(new Rect((Screen.width - w) / 2f, Screen.height * 0.1f, w, h), msg, _st);
        }
    }
}
