using HarmonyLib;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityVRMod.Core;

namespace UnityVRMod.Mimesis
{
    /// <summary>
    /// Build 47: a "VR" entry in the game's own settings menu, under Audio / Display / Control.
    /// It is a copy of the "Control" entry with the game's scripts removed (element marker, localisation, sound relay,
    /// hover triggers), so the game never sees it; a click opens the mod settings (in the headset, or on the monitor).
    /// </summary>
    internal static class MimesisGameMenu
    {
        private const string BtnName = "MimesisVR_MenuButton";

        public static void Install(HarmonyLib.Harmony h)
        {
            try
            {
                var t = typeof(UIPrefab_Setting);
                h.Patch(AccessTools.Method(t, "Start"), postfix: new HarmonyMethod(typeof(MimesisGameMenu), nameof(Start_Postfix)));
                h.Patch(AccessTools.Method(t, "OnDisable"), postfix: new HarmonyMethod(typeof(MimesisGameMenu), nameof(OnDisable_Postfix)));
            }
            catch (Exception ex) { VRModCore.LogWarning("[MimesisVR] Game settings menu entry not installed: " + ex.Message); }
        }

        private static void Start_Postfix(UIPrefab_Setting __instance)
        {
            try { AddEntry(__instance); }
            catch (Exception ex) { VRModCore.LogWarning("[MimesisVR] Game settings menu entry failed: " + ex.Message); }
        }

        private static void OnDisable_Postfix() { MimesisSettingsUI.CloseFlat(); }

        private static void AddEntry(UIPrefab_Setting ui)
        {
            var ctrl = ui.UE_UI_MenuTextButtonControl;
            if (ctrl == null) { VRModCore.LogWarning("[MimesisVR] Game settings menu: 'Control' entry not found."); return; }
            var parent = ctrl.transform.parent;
            if (parent.Find(BtnName) != null) return;

            var go = UnityEngine.Object.Instantiate(ctrl.gameObject, parent, false);
            go.name = BtnName;
            go.transform.SetSiblingIndex(ctrl.transform.GetSiblingIndex() + 1);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || mb is Graphic || mb is Selectable || mb is BaseMeshEffect || mb is LayoutGroup || mb is LayoutElement
                    || mb is ContentSizeFitter || mb is Mask || mb is RectMask2D) continue;
                try { UnityEngine.Object.DestroyImmediate(mb); } catch { }
            }
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                if (t != go.transform && t.name.IndexOf("Checkmark", StringComparison.OrdinalIgnoreCase) >= 0) t.gameObject.SetActive(false);

            // place it one step after "Control" (unless a layout group does it)
            if (parent.GetComponent<LayoutGroup>() == null && go.transform is RectTransform rt && ctrl.transform is RectTransform cr)
            {
                Vector2 step = new(0f, -(cr.rect.height + 8f));
                var disp = ui.UE_UI_MenuTextButtonDisplay;
                if (disp != null && disp.transform.parent == parent && disp.transform is RectTransform dr)
                {
                    Vector2 s = cr.anchoredPosition - dr.anchoredPosition;
                    if (s.sqrMagnitude > 1f) step = s;
                }
                rt.anchoredPosition = cr.anchoredPosition + step;
            }

            var texts = go.GetComponentsInChildren<TMP_Text>(true);
            Color normal = texts.Length > 0 ? texts[0].color : Color.white;
            foreach (var tx in texts) { tx.text = "VR"; }

            var btn = go.GetComponent<Button>();
            if (btn == null) { VRModCore.LogWarning("[MimesisVR] Game settings menu: the copied entry has no button."); UnityEngine.Object.Destroy(go); return; }
            btn.onClick = new Button.ButtonClickedEvent();
            btn.onClick.AddListener(() => MimesisSettingsUI.OpenFromGameMenu());

            // hover colour, as the game's entries
            var trig = go.AddComponent<EventTrigger>();
            AddTrigger(trig, EventTriggerType.PointerEnter, () => { Color hc = HoverColor(normal); foreach (var tx in texts) if (tx != null) tx.color = hc; });
            AddTrigger(trig, EventTriggerType.PointerExit, () => { foreach (var tx in texts) if (tx != null) tx.color = normal; });
            go.SetActive(true);
            VRModCore.Log("[MimesisVR] 'VR' entry added to the game settings menu.");
        }

        private static UIManager _uiman;

        private static Color HoverColor(Color fallback)
        {
            try
            {
                if (_uiman == null) _uiman = UnityEngine.Object.FindFirstObjectByType<UIManager>();
                return _uiman != null ? _uiman.mouseOverTextColor : fallback;
            }
            catch { return fallback; }
        }

        private static void AddTrigger(EventTrigger trig, EventTriggerType type, Action a)
        {
            var e = new EventTrigger.Entry { eventID = type };
            e.callback.AddListener(_ => a());
            trig.triggers.Add(e);
        }
    }
}
