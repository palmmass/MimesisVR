using System.Collections;
using System.Linq;
using UnityVRMod.Core;

namespace UnityVRMod.Mimesis
{
    /// <summary>
    /// MIMESIS VR: test helpers for trying the mod quickly (undying, endless sprint, health, money, items...).
    /// Health, stamina, money and items are decided by the game's server, which runs inside the HOST's game.
    /// So these work only when you host the lobby: they call the game's own built-in admin commands on the
    /// local server, for every player in the lobby or only for you. In someone else's lobby they do nothing.
    /// </summary>
    internal static class MimesisCheats
    {
        public static bool Undying, EndlessStamina, SlowMonsters, StopAi;
        public static bool ForAll = true;
        /// <summary>Build 47: cheats work only with "Debug tools" on.</summary>
        public static bool Allowed => MimesisSettings.DebugTools;
        public static void DisableAll() { Undying = EndlessStamina = SlowMonsters = StopAi = false; }
        public static string LastResult = "";

        private static float _next;
        private static int _itemCursor = -1;
        private static List<(int id, string name)> _items;

        private static object Hub() => AccessType("Hub")?.GetField("s", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
                                        ?? AccessType("Hub")?.GetProperty("s", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);

        private static Type AccessType(string name) => typeof(Mimic.Actors.ProtoActor).Assembly.GetType(name);

        private static object Get(object o, string name)
        {
            if (o == null) return null;
            var t = o.GetType();
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var p = t.GetProperty(name, F);
            if (p != null) return p.GetValue(o);
            var f = t.GetField(name, F);
            return f?.GetValue(o);
        }

        private static object World()
        {
            var w = Get(Hub(), "vworld");
            if (w == null) return null;
            return Get(w, "Running") is bool run && run ? w : null;
        }

        public static bool IsHost => World() != null;

        private static object LocalServerPlayer(object world)
        {
            var actor = MimesisInput.LocalActor;
            if (actor == null || world == null) return null;
            int id = actor.ActorID;
            foreach (var room in Rooms(world))
            {
                var m = room?.GetType().GetMethod("FindPlayerByObjectID", BindingFlags.Instance | BindingFlags.Public);
                var p = m?.Invoke(room, new object[] { id });
                if (p != null) return p;
            }
            return null;
        }

        private static IEnumerable<object> Rooms(object world)
        {
            var rooms = Get(Get(world, "VRoomManager"), "_vrooms") as IDictionary;
            if (rooms == null) yield break;
            foreach (var r in rooms.Values) if (r != null) yield return r;
        }

        /// <summary>Players the toggles and per-player commands apply to: everyone in the lobby, or only you.</summary>
        private static List<object> Targets(object world)
        {
            var list = new List<object>();
            if (!ForAll) { var me = LocalServerPlayer(world); if (me != null) list.Add(me); return list; }
            foreach (var room in Rooms(world))
                if (Get(room, "_vPlayerDict") is IDictionary d)
                    foreach (var p in d.Values) if (p != null && !list.Contains(p)) list.Add(p);
            return list;
        }

        private static string RunOne(object world, object player, string cmd, string args)
        {
            var func = world.GetType().GetMethod("GetAdminCommand")?.Invoke(world, new object[] { cmd }) as Delegate;
            if (func == null) return "?" + cmd;
            var argsObj = Activator.CreateInstance(AccessType("AdminCommandArgs"), args);
            return func.DynamicInvoke(player, argsObj)?.ToString() ?? "null";
        }

        /// <summary>
        /// Runs one of the game's admin commands on the local (host) server. perPlayer: for each target player
        /// (everyone or only you, by the "Cheats for" option); otherwise once (room-wide commands).
        /// </summary>
        public static bool Command(string cmd, string args = "", bool perPlayer = false)
        {
            if (!Allowed) { LastResult = MimesisSettings.T("Debug tools are off", "Отладка выключена"); return false; }
            try
            {
                var world = World();
                if (world == null) { LastResult = MimesisSettings.T("Only when you host", "Только когда ты хост"); return false; }
                var me = LocalServerPlayer(world);
                if (me == null) { LastResult = MimesisSettings.T("Player not found", "Игрок не найден"); return false; }
                var targets = perPlayer ? Targets(world) : new List<object> { me };
                int ok = 0; string last = "";
                foreach (var p in targets)
                {
                    last = RunOne(world, p, cmd, args);
                    if (last == "Success") ok++;
                }
                VRModCore.Log($"[MimesisVR] Test command '{cmd} {args}' for {targets.Count} player(s): {ok} ok, last result {last}");
                LastResult = ok > 0 ? (targets.Count > 1 ? MimesisLoc.TrF("Done ({0} of {1})", "Готово ({0} из {1})", ok, targets.Count) : MimesisSettings.T("Done", "Готово"))
                                    : MimesisSettings.T("Not possible here", "Здесь нельзя") + $" ({last})";
                return ok > 0;
            }
            catch (Exception ex) { LastResult = "Error"; VRModCore.LogError($"[MimesisVR] Test command '{cmd}' failed:", ex); return false; }
        }

        /// <summary>Keeps the toggles applied (rooms and server players change between scenes).</summary>
        public static void Update()
        {
            if (!Allowed && (Undying || EndlessStamina || SlowMonsters || StopAi)) DisableAll(); // the applied ones are reverted below
            if (Time.unscaledTime < _next || (!Undying && !EndlessStamina && !SlowMonsters && !StopAi && !_slowApplied && !_undyingApplied && !_aiApplied)) return;
            _next = Time.unscaledTime + 0.5f;
            try
            {
                var world = World();
                if (world == null) return;
                var targets = Targets(world);
                foreach (var player in targets)
                {
                    var stat = Get(player, "StatControlUnit");
                    if (stat == null) continue;
                    if (EndlessStamina) stat.GetType().GetMethod("RecoverStamina")?.Invoke(stat, new object[] { 100000L });
                    var getU = stat.GetType().GetMethod("GetCheatUndying");
                    bool cur = getU != null && (bool)getU.Invoke(stat, null);
                    if (cur != Undying && (Undying || _undyingApplied)) stat.GetType().GetMethod("SetCheatUndying")?.Invoke(stat, new object[] { Undying });
                }
                _undyingApplied = Undying;
                var slow = world.GetType().GetProperty("CheatMonsterMoveSlowRate");
                if (slow != null)
                {
                    float want = SlowMonsters ? 0.3f : 1f;
                    if (!Mathf.Approximately((float)slow.GetValue(world), want)) slow.SetValue(world, want);
                    _slowApplied = SlowMonsters;
                }
                if (StopAi || _aiApplied)
                {
                    foreach (var room in Rooms(world))
                    {
                        var en = room.GetType().GetProperty("EnableAIController", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        bool curAi = en == null || !(en.GetValue(room) is bool b) || b;
                        if (curAi == StopAi) room.GetType().GetMethod("EnableAIControl")?.Invoke(room, new object[] { !StopAi });
                    }
                    _aiApplied = StopAi;
                }
            }
            catch (Exception ex) { VRModCore.LogWarning("[MimesisVR] Test toggles: " + ex.Message); _next = Time.unscaledTime + 5f; }
        }

        private static bool _undyingApplied, _slowApplied, _aiApplied;

        private static string Text(string key)
        {
            try
            {
                var lc = Get(Hub(), "lcman");
                var m = lc?.GetType().GetMethod("GetText", new[] { typeof(string), typeof(object[]) });
                var s = m?.Invoke(lc, new object[] { key, new object[0] }) as string;
                return string.IsNullOrEmpty(s) ? key : s;
            }
            catch { return key; }
        }

        private static void EnsureItems()
        {
            if (_items != null) return;
            _items = new List<(int, string)>();
            var dict = Get(Get(Get(Hub(), "dataman"), "ExcelDataManager"), "ItemInfoDict") as IEnumerable;
            if (dict != null)
                foreach (var kv in dict)
                {
                    var info = Get(kv, "Value");
                    if (info == null) continue;
                    string type = Get(info, "ItemType")?.ToString() ?? "?";
                    string name = Text(Get(info, "Name") as string ?? "?");
                    _items.Add(((int)Get(info, "MasterID"), $"{name} [{type}]"));
                }
            _items = _items.OrderBy(x => x.id).ToList();
            VRModCore.Log($"[MimesisVR] Test items: {_items.Count} items: " + string.Join(", ", _items.Select(x => $"{x.id}={x.name}")));
        }

        /// <summary>Stick left/right on the item row: choose the item (nothing is spawned yet).</summary>
        public static void SelectItem(int dir)
        {
            try
            {
                EnsureItems();
                if (_items.Count == 0) { LastResult = MimesisSettings.T("No item list", "Нет списка предметов"); return; }
                _itemCursor = ((_itemCursor + dir) % _items.Count + _items.Count) % _items.Count;
                LastResult = $"{_itemCursor + 1}/{_items.Count}: {_items[_itemCursor].name}";
            }
            catch (Exception ex) { VRModCore.LogError("[MimesisVR] Item list failed:", ex); }
        }

        /// <summary>Spawns the chosen item next to you, or (everywhere) at every loot point of the level.</summary>
        public static void SpawnSelected(bool everywhere)
        {
            try
            {
                EnsureItems();
                if (_items.Count == 0) { LastResult = MimesisSettings.T("No item list", "Нет списка предметов"); return; }
                if (_itemCursor < 0) _itemCursor = 0;
                var it = _items[_itemCursor];
                if (Command(everywhere ? "spawnitemallpoint" : "spawnitem", $"masterid={it.id}")) LastResult = (everywhere ? MimesisSettings.T("Everywhere: ", "По всей карте: ") : "") + it.name;
            }
            catch (Exception ex) { VRModCore.LogError("[MimesisVR] Spawn item failed:", ex); }
        }

        public static string SelectedName => _items != null && _itemCursor >= 0 && _itemCursor < _items.Count ? _items[_itemCursor].name : "";
    }
}
