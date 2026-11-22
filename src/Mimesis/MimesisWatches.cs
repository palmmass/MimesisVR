using System.Linq;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityVRMod.Core;

namespace UnityVRMod.Mimesis
{
    /// <summary>
    /// MIMESIS VR (build 40): watches on both forearms.
    ///  - Body: the lid of the game's compass (mesh "LT_Compass_cover" with the game's own material), laid flat and
    ///    scaled to the set length / width / thickness. Only with Hands = Character arms.
    ///  - Left screen: the mod's wrist panel (slots, gear, frame counter) sits on the left watch.
    ///  - Right screen: the mod's own live drawing - heart pulse (rate and colour by health), health in quarters,
    ///    contamination tube with bubbles, stamina bar - as an OpenXR quad layer (sharp in the headset).
    /// Poses: forearm frame (MimesisHands.Forearm: +Y along the arm to the hand, +Z into the arm, +X to the side),
    /// watch = frame + shifts (across, along, height) and turns; screen = watch top + shifts and turns.
    /// </summary>
    internal static class MimesisWatches
    {
        public sealed class Side
        {
            public bool Right;
            public bool Model; // build 43: screens in the air by default, the body is an option
            public float Along, Across, Height = 0.02f, Roll, Tilt, Bank;
            public float Length = 0.09f, Width = 0.06f, Thick = 0.008f;
            public float SAlong, SAcross, SHeight = 0.001f, SRoll, STilt, SBank, SScale = 1f;
            /// <summary>Build 41: how much the watch turns with the forearm skin (1 = exactly like the skin under it).</summary>
            public float TwistFollow = 1f;
            /// <summary>Build 41: dark backing of the screen, 0..1.</summary>
            public float BgAlpha = 0.45f;
            /// <summary>Build 41: 0 = sizes as set, 1 = body fits the screen, 2 = screen fits the body.</summary>
            public int Fit;
            /// <summary>Build 42: the screen turns towards the eyes by this share (the body stays on the arm).</summary>
            public float FaceEyes = 0.6f;
            /// <summary>Build 42: dark watch face over the whole top of the body (0 = none).</summary>
            public float Dial;
            /// <summary>Build 42: the body casts a shadow.</summary>
            public bool Shadow;
            internal GameObject Go, DialGo;
            internal MeshRenderer Mr;
            internal bool Grabbing, Near, PrevDown;
            internal Vector3 GrabPos;
            internal Quaternion GrabRot = Quaternion.identity;
            internal float EffW, EffL;
            internal Transform ScaleT;
            internal bool LidOk;
            internal bool GrabScreen;
            internal Vector3 LidPos;
            internal Quaternion LidRot = Quaternion.identity;
        }

        public static readonly Side L = new() { Right = false, Length = 0.13f, Width = 0.065f, SRoll = 90f, SScale = 0.5f };
        public static readonly Side R = new() { Right = true, Length = 0.08f, Width = 0.055f, Along = 0.04f, Height = 0.06f, SRoll = -90f };

        /// <summary>Build 41: bag slots in a second row under the main ones (only while there is a bag).</summary>
        public static bool BagRow = true;

        /// <summary>Right screen as a headset layer (rig-local).</summary>
        public static bool ScreenActive { get; private set; }
        public static RenderTexture ScreenTex => _out;
        public static Vector3 ScreenPos;
        public static Quaternion ScreenRot = Quaternion.identity;
        public static Vector2 ScreenSize;

        /// <summary>Health, contamination and stamina on the right watch (false = on the screen, as in the game; also
        /// false with the own wrist panel off - then the old cut-out pieces are used as before).</summary>
        public static bool StatusOnWatch => MimesisHudSegments.StatusPlace != 2 && MimesisSettingsUI.WristHud.On;

        private const float NaturalWidth = 0.06f; // right screen width at size x1
        private const int PxW = 480, PxH = 300;
        private static int _lastFrame = -100;

        // ------------------------------------------------------------------ poses
        /// <summary>Watch body pose (rig-local) on the forearm; false without character arms.</summary>
        private static bool Lid(Side s, Transform rig, out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = Quaternion.identity;
            if (!MimesisHands.Forearm(s.Right, rig, out var fp, out var fr, s.TwistFollow, s.Along)) return false;
            pos = fp + fr * new Vector3(-s.Across, s.Along, -s.Height);
            rot = fr * Quaternion.Euler(0f, 0f, s.Roll) * Quaternion.Euler(s.Tilt, 0f, 0f) * Quaternion.Euler(0f, s.Bank, 0f);
            return true;
        }

        private static void ScreenOn(Side s, Vector3 lidPos, Quaternion lidRot, out Vector3 pos, out Quaternion rot)
        {
            Vector3 top = lidPos + lidRot * new Vector3(0f, 0f, -s.Thick * 0.5f);
            pos = top + lidRot * new Vector3(-s.SAcross, s.SAlong, -s.SHeight);
            rot = lidRot * Quaternion.Euler(0f, 0f, s.SRoll) * Quaternion.Euler(s.STilt, 0f, 0f) * Quaternion.Euler(0f, s.SBank, 0f);
            // build 42: the model's elbow does not rise when the hand comes up to the eyes, so the watch stays steep -
            // the screen turns towards the eyes by FaceEyes (keeping its own up as far as possible)
            if (s.FaceEyes > 0.001f)
            {
                Vector3 to = pos - MimesisInput.HeadLocalPos;
                if (to.sqrMagnitude > 1e-6f)
                {
                    Vector3 n = rot * Vector3.forward;
                    Quaternion full = Quaternion.FromToRotation(n, to.normalized) * rot;
                    rot = Quaternion.Slerp(rot, full, Mathf.Clamp01(s.FaceEyes));
                }
            }
        }

        /// <summary>Left wrist panel on the left watch (rig-local); false without character arms.</summary>
        public static bool LeftScreen(Transform rig, out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = Quaternion.identity;
            if (!Lid(L, rig, out var lp, out var lr)) return false;
            ScreenOn(L, lp, lr, out pos, out rot);
            return true;
        }

        // ------------------------------------------------------------------ fitting body and screen (build 41)
        private const float FitMargin = 0.004f;

        /// <summary>Screen size at "Screen: size" x1 (left: the wrist panel as it is laid out now).</summary>
        private static Vector2 Natural(Side s)
            => s.Right ? new Vector2(NaturalWidth, NaturalWidth * PxH / PxW) : MimesisSettingsUI.WristHud.NaturalSize;

        /// <summary>Extent of a w x h screen in the body's plane (across, along), turned by "Screen: turn".</summary>
        private static Vector2 Extent(Side s, Vector2 size)
        {
            float r = s.SRoll * Mathf.Deg2Rad, c = Mathf.Abs(Mathf.Cos(r)), sn = Mathf.Abs(Mathf.Sin(r));
            return new Vector2(c * size.x + sn * size.y, sn * size.x + c * size.y);
        }

        /// <summary>Scale of the screen to use: as set, or (Fit = 2) the largest that fits on the body.</summary>
        public static float ScreenScale(Side s)
        {
            float set = Mathf.Max(0.1f, s.SScale);
            if (s.Fit != 2) return set;
            Vector2 n = Natural(s);
            if (n.x <= 1e-4f || n.y <= 1e-4f) return set;
            Vector2 e = Extent(s, n);
            float k = Mathf.Min((s.Width - 2f * FitMargin) / Mathf.Max(1e-4f, e.x), (s.Length - 2f * FitMargin) / Mathf.Max(1e-4f, e.y));
            return Mathf.Clamp(k, 0.05f, 5f);
        }

        /// <summary>Body width / length to use: as set, or (Fit = 1) around the screen.</summary>
        private static void BodySize(Side s, out float w, out float l)
        {
            w = s.Width; l = s.Length;
            if (s.Fit != 1) return;
            Vector2 n = Natural(s);
            if (n.x <= 1e-4f || n.y <= 1e-4f) return;
            Vector2 e = Extent(s, n * ScreenScale(s));
            w = Mathf.Max(0.01f, e.x + 2f * FitMargin); l = Mathf.Max(0.01f, e.y + 2f * FitMargin);
        }

        /// <summary>0..1: how much a screen faces the eyes (a layer would otherwise show through the arm).</summary>
        public static float Facing(Vector3 pos, Quaternion rot)
        {
            Vector3 to = pos - MimesisInput.HeadLocalPos;
            if (to.sqrMagnitude < 1e-6f) return 1f;
            float d = Vector3.Dot(to.normalized, rot * Vector3.forward); // +Z of a panel points away from the viewer
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, 0.2f, d));
        }

        // ------------------------------------------------------------------ per frame
        /// <summary>Called each VR frame after the HUD pieces were placed (before the eyes render).</summary>
        public static void Frame(Transform rig)
        {
            _lastFrame = Time.frameCount;
            ScreenActive = false;
            var c = MimesisInput.Controllers;
            bool vr = rig != null && MimesisInput.VrActive && c != null && c.Ready && !MimesisHands.LocalDead;
            try
            {
                // bodies
                bool leftUsed = vr && L.Model && MimesisSettingsUI.WristHud.On && MimesisSettingsUI.WristHud.OnForearm;
                bool rightUsed = vr && R.Model && StatusOnWatch;
                Edit(L, c, rig, vr, MimesisSettingsUI.WristHud.On && MimesisSettingsUI.WristHud.OnForearm);
                Edit(R, c, rig, vr, StatusOnWatch);
                PlaceLid(L, rig, leftUsed);
                PlaceLid(R, rig, rightUsed);

                // right screen
                TrackDamage(c, vr);
                if (!vr || !StatusOnWatch || !MimesisHud.Enabled || !MimesisHudSegments.Enabled) { _fade = 0f; return; } // build 42: also in menus
                var ig = InGameHud();
                if (ig == null || ig.isDead) { _fade = 0f; return; }
                Vector3 pos; Quaternion rot;
                if (R.LidOk) ScreenOn(R, R.LidPos, R.LidRot, out pos, out rot);
                else if (Lid(R, rig, out var lp, out var lr)) ScreenOn(R, lp, lr, out pos, out rot);
                else
                {
                    // no character arms: above the right controller, lying on it
                    if (!c.RightPoseValid) return;
                    Quaternion fr = c.RightRot * Quaternion.LookRotation(Vector3.down, Vector3.forward);
                    Vector3 fp = c.RightPos + c.RightRot * new Vector3(0f, 0.04f, -0.07f);
                    ScreenOn(R, fp + fr * new Vector3(-R.Across, R.Along, -R.Height), fr * Quaternion.Euler(0f, 0f, R.Roll) * Quaternion.Euler(R.Tilt, 0f, 0f) * Quaternion.Euler(0f, R.Bank, 0f), out pos, out rot);
                }
                _fade = Mathf.MoveTowards(_fade, Facing(pos, rot), Time.unscaledDeltaTime * 6f);
                bool stream = MimesisStream.WantCorners; // build 45: the monitor shows it in a corner, looked at or not
                if (_fade <= 0.01f && !stream) return;
                Draw(ig);
                MimesisStream.CopyWatch(_out);
                if (_fade <= 0.01f) return;
                if (_fade < 0.999f) MultiplyAlpha(_out, _fade);
                ScreenPos = pos; ScreenRot = rot;
                float w = NaturalWidth * ScreenScale(R);
                ScreenSize = new Vector2(w, w * PxH / PxW);
                ScreenActive = true;
            }
            catch (Exception ex)
            {
                if (Time.unscaledTime >= _nextErr) { _nextErr = Time.unscaledTime + 10f; VRModCore.LogError("[MimesisVR] Watches failed:", ex); }
                ScreenActive = false;
            }
        }

        private static float _fade, _nextErr;

        /// <summary>From the hands' onBeforeRender: hide the bodies when the VR frame stopped placing them.</summary>
        public static void Watchdog()
        {
            if (Time.frameCount - _lastFrame < 30) return;
            if (L.Go != null && L.Go.activeSelf) L.Go.SetActive(false);
            if (R.Go != null && R.Go.activeSelf) R.Go.SetActive(false);
            ScreenActive = false;
        }

        // ------------------------------------------------------------------ body (compass lid)
        private static Mesh _mesh;
        private static Material _mat;
        private static float _nextFind;
        private static Quaternion _fixRot = Quaternion.identity;
        private static Vector3 _fixPos;
        private static Vector3 _plate = new(0.16f, 0.18f, 0.012f); // lid width, length, thickness (mesh units)

        private static void PlaceLid(Side s, Transform rig, bool used)
        {
            s.LidOk = false;
            if (used && Lid(s, rig, out s.LidPos, out s.LidRot)) s.LidOk = true;
            bool show = s.LidOk && EnsureLid(s);
            if (s.Go == null) return;
            if (s.Go.activeSelf != show) s.Go.SetActive(show);
            if (!show) { UpdateDial(s, false); return; }
            s.Go.transform.SetPositionAndRotation(rig.TransformPoint(s.LidPos), rig.rotation * s.LidRot);
            s.Go.transform.localScale = Vector3.one * rig.lossyScale.x;
            BodySize(s, out s.EffW, out s.EffL);
            s.ScaleT.localScale = new Vector3(Mathf.Max(0.005f, s.EffW) / _plate.x, Mathf.Max(0.005f, s.EffL) / _plate.y, Mathf.Max(0.001f, s.Thick) / _plate.z);
            UpdateDial(s, show);
            if (s.Mr != null)
            {
                var sm = s.Shadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
                if (s.Mr.shadowCastingMode != sm) s.Mr.shadowCastingMode = sm;
                Material want = !EditMode ? _mat : (s.Grabbing || s.Near) ? Glow(true) : Glow(false);
                if (want != null && s.Mr.sharedMaterial != want) s.Mr.sharedMaterial = want;
            }
        }

        private static bool EnsureLid(Side s)
        {
            if (s.Go != null) return true;
            if (_mesh == null)
            {
                if (Time.unscaledTime < _nextFind) return false;
                _nextFind = Time.unscaledTime + 3f;
                if (!FindCover()) return false;
            }
            var go = new GameObject(s.Right ? "MimesisVR_Watch_R" : "MimesisVR_Watch_L");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var sc = new GameObject("Scale").transform; sc.SetParent(go.transform, false);
            var m = new GameObject("Lid"); m.transform.SetParent(sc, false);
            m.transform.localRotation = _fixRot; m.transform.localPosition = _fixPos;
            m.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = m.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            s.Mr = mr;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
            go.layer = sc.gameObject.layer = m.layer = 0;
            s.Go = go; s.ScaleT = sc;
            go.SetActive(false);
            return true;
        }

        /// <summary>The compass lid from the game's item table (prefabs are always loaded), else from loaded meshes.</summary>
        private static bool FindCover()
        {
            const string MeshName = "LT_Compass_cover";
            string src = null;
            try
            {
                var tm = typeof(Hub).GetProperty("tableman", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(Hub.s);
                var table = tm?.GetType().GetField("lootingObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(tm) as MMLootingObjectTable;
                if (table != null)
                    foreach (var row in table.rows)
                    {
                        if (row?.prefab == null) continue;
                        foreach (var mf in row.prefab.GetComponentsInChildren<MeshFilter>(true))
                        {
                            if (mf.sharedMesh == null || mf.sharedMesh.name != MeshName) continue;
                            var r = mf.GetComponent<MeshRenderer>();
                            _mesh = mf.sharedMesh; _mat = r != null ? r.sharedMaterial : null;
                            src = "item table, row '" + row.id + "'";
                            break;
                        }
                        if (_mesh != null) break;
                    }
            }
            catch (Exception ex) { VRModCore.LogWarning("[MimesisVR] Watch: item table not readable: " + ex.Message); }
            if (_mesh == null)
            {
                foreach (var m in Resources.FindObjectsOfTypeAll<Mesh>()) if (m != null && m.name == MeshName) { _mesh = m; src = "loaded meshes"; break; }
                if (_mesh == null) return false;
            }
            if (_mat == null) foreach (var m in Resources.FindObjectsOfTypeAll<Material>()) if (m != null && m.name == "LT_Compass") { _mat = m; break; }
            if (_mat == null) { _mesh = null; return false; }

            // The lid is modelled open (tilted about X). Lay it flat: its long side along +Y, its outer face to -Z
            // (towards the viewer, the forearm frame's +Z goes into the arm), centred on the origin.
            var b = _mesh.bounds;
            Vector3 sz = b.size;
            Vector3 along, outer;
            float t0 = Mathf.Clamp(Mathf.Min(sz.x, Mathf.Min(sz.y, sz.z)) * 0.12f, 0.004f, 0.03f);
            if (sz.z >= sz.y * 3f) { along = Vector3.forward; outer = Vector3.up; _plate = new Vector3(sz.x, sz.z, Mathf.Max(t0, sz.y)); }
            else if (sz.y >= sz.z * 3f) { along = Vector3.up; outer = Vector3.back; _plate = new Vector3(sz.x, sz.y, Mathf.Max(t0, sz.z)); }
            else
            {
                float th = Mathf.Atan2(sz.y, sz.z);
                along = b.center.z < 0f ? new Vector3(0f, Mathf.Sin(th), -Mathf.Cos(th)) : new Vector3(0f, Mathf.Sin(th), Mathf.Cos(th));
                outer = new Vector3(0f, along.z, -along.y);
                if (b.center.z >= 0f) outer = -outer;
                float len = (sz.y + sz.z) / Mathf.Max(0.1f, Mathf.Sin(th) + Mathf.Cos(th)) - t0;
                _plate = new Vector3(sz.x, Mathf.Max(0.01f, len), t0);
            }
            Quaternion q = Quaternion.LookRotation(-outer, along);
            _fixRot = Quaternion.Inverse(q);
            _fixPos = -(_fixRot * b.center);
            VRModCore.Log($"[MimesisVR] Watch body: '{_mesh.name}' from {src}, material '{_mat.name}' ({_mat.shader?.name}); mesh bounds {b.center:F3} / {sz:F3}, laid flat as {_plate.x * 100f:F1} x {_plate.y * 100f:F1} x {_plate.z * 100f:F1} cm.");
            return true;
        }

        // ------------------------------------------------------------------ moving a watch by hand (build 41)
        /// <summary>Settings switch (not saved): the bodies glow, grip with the other hand near one to move and turn it.</summary>
        public static bool EditMode;
        private const float GrabReach = 0.12f;

        /// <summary>The game must not get this grip (it is used to hold a watch, or is about to be).</summary>
        public static bool GripBlocked(bool rightHand)
        {
            if (!EditMode) return false;
            var s = rightHand ? L : R; // the left watch is held with the right hand and the other way round
            return s.Near || s.Grabbing;
        }

        private static void Edit(Side s, Features.VRVisualization.OpenXR.XrControllerInput c, Transform rig, bool vr, bool screenUsed)
        {
            bool rightHand = !s.Right;
            bool valid = vr && c != null && (rightHand ? c.RightPoseValid : c.LeftPoseValid);
            // build 44: the body pose is needed for the screen too, also with the watch model hidden
            Vector3 lidPos = s.LidPos; Quaternion lidRot = s.LidRot;
            bool lidOk = s.LidOk || (valid && EditMode && Lid(s, rig, out lidPos, out lidRot));
            bool bodyGrab = s.Model && s.LidOk;
            if (!EditMode || !valid || !lidOk || (!bodyGrab && !screenUsed))
            {
                if (s.Grabbing) VRModCore.Log($"[MimesisVR] Watch {(s.Right ? "right" : "left")}: let go.");
                s.Grabbing = false; s.Near = false; s.PrevDown = false;
                return;
            }
            Vector3 hp = rightHand ? c.RightPos : c.LeftPos;
            Quaternion hr = rightHand ? c.RightRot : c.LeftRot;
            float g = c.Axes[(int)(rightHand ? Features.VRVisualization.OpenXR.XrControllerInput.F.RGrip : Features.VRVisualization.OpenXR.XrControllerInput.F.LGrip)];
            bool down = s.Grabbing ? g > 0.35f : g > 0.6f;
            // build 44: the screen (left: interface, right: health) can be grabbed too - whichever is nearer to the hand
            ScreenOn(s, lidPos, lidRot, out var scrPos, out var scrRot);
            float dLid = bodyGrab ? (hp - lidPos).magnitude : float.MaxValue, dScr = screenUsed ? (hp - scrPos).magnitude : float.MaxValue;
            bool wasNear = s.Near;
            s.Near = Mathf.Min(dLid, dScr) < GrabReach;
            if (s.Near && !wasNear && !s.Grabbing) { try { c.Vibrate(rightHand, 0.015f, 0.15f); } catch { } } // in reach: a light tick
            if (!s.Grabbing && down && !s.PrevDown && s.Near)
            {
                s.Grabbing = true;
                s.GrabScreen = dScr < dLid;
                Vector3 tp = s.GrabScreen ? scrPos : lidPos; Quaternion tr = s.GrabScreen ? scrRot : lidRot;
                s.GrabPos = Quaternion.Inverse(hr) * (tp - hp);
                s.GrabRot = Quaternion.Inverse(hr) * tr;
                try { c.Vibrate(rightHand, 0.05f, 0.4f); } catch { }
            }
            else if (s.Grabbing && !down)
            {
                s.Grabbing = false;
                if (s.GrabScreen)
                    VRModCore.Log($"[MimesisVR] Watch {(s.Right ? "right" : "left")} screen moved by hand: along {s.SAlong * 100f:F1}, across {s.SAcross * 100f:F1} cm, height {s.SHeight * 1000f:F0} mm; turn {s.SRoll:F0}, tilt {s.STilt:F0}, sideways {s.SBank:F0} deg.");
                else
                    VRModCore.Log($"[MimesisVR] Watch {(s.Right ? "right" : "left")} moved by hand: along {s.Along * 100f:F1}, across {s.Across * 100f:F1}, height {s.Height * 100f:F1} cm; turn {s.Roll:F0}, tilt {s.Tilt:F0}, sideways {s.Bank:F0} deg.");
                try { c.Vibrate(rightHand, 0.04f, 0.3f); } catch { }
            }
            s.PrevDown = down;
            if (!s.Grabbing) return;
            if (s.GrabScreen)
            {
                // the screen is kept relative to the watch body: offsets from the top of the body, turns in its frame
                Vector3 sp = hp + hr * s.GrabPos;
                Quaternion sr = hr * s.GrabRot;
                Vector3 top = lidPos + lidRot * new Vector3(0f, 0f, -s.Thick * 0.5f);
                Vector3 sl = Quaternion.Inverse(lidRot) * (sp - top);
                s.SAcross = Mathf.Clamp(-sl.x, -0.15f, 0.15f);
                s.SAlong = Mathf.Clamp(sl.y, -0.15f, 0.15f);
                s.SHeight = Mathf.Clamp(-sl.z, -0.03f, 0.10f);
                Vector3 se = Quaternion.Inverse(Quaternion.Inverse(lidRot) * sr).eulerAngles;
                s.STilt = Mathf.DeltaAngle(0f, -se.x);
                s.SBank = Mathf.DeltaAngle(0f, -se.y);
                s.SRoll = Mathf.DeltaAngle(0f, -se.z);
                return;
            }
            if (!MimesisHands.Forearm(s.Right, rig, out var fp, out var fr, s.TwistFollow, s.Along)) return;
            Vector3 np = hp + hr * s.GrabPos;
            Quaternion nr = hr * s.GrabRot;
            Vector3 l = Quaternion.Inverse(fr) * (np - fp);
            s.Across = Mathf.Clamp(-l.x, -0.15f, 0.15f);
            s.Along = Mathf.Clamp(l.y, -0.20f, 0.20f);
            s.Height = Mathf.Clamp(-l.z, -0.10f, 0.20f);
            // turns: rot = Rz(roll) * Rx(tilt) * Ry(bank); its inverse is Unity's Euler(-tilt, -bank, -roll)
            Vector3 e = Quaternion.Inverse(Quaternion.Inverse(fr) * nr).eulerAngles;
            s.Tilt = Mathf.DeltaAngle(0f, -e.x);
            s.Bank = Mathf.DeltaAngle(0f, -e.y);
            s.Roll = Mathf.DeltaAngle(0f, -e.z);
        }

        private static Material _glow, _glowStrong;
        private static Material Glow(bool strong)
        {
            if (_mat == null) return null;
            if (_glow == null)
            {
                _glow = MakeGlow(new Color(0.35f, 0.75f, 1f) * 0.6f);
                _glowStrong = MakeGlow(new Color(0.45f, 1f, 0.6f) * 1.4f);
                VRModCore.Log($"[MimesisVR] Watch glow: material properties emission={_mat.HasProperty("_EmissionColor")} emissionMap={_mat.HasProperty("_EmissionMap")} baseColor={_mat.HasProperty("_BaseColor")} color={_mat.HasProperty("_Color")}.");
            }
            return strong ? _glowStrong : _glow;
        }

        private static Material MakeGlow(Color c)
        {
            var m = new Material(_mat) { name = "MimesisVR_WatchGlow" };
            if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", Texture2D.whiteTexture);
            if (m.HasProperty("_EmissionColor")) { m.SetColor("_EmissionColor", c); m.EnableKeyword("_EMISSION"); }
            Color tint = Color.Lerp(Color.white, c, 0.5f) * 1.3f; tint.a = 1f;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
            else if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
            return m;
        }

        // ------------------------------------------------------------------ gear on the right watch (build 41)
        /// <summary>0 = left of the wrist panel (as before), 1 = at the elbow end of the right watch (shown when looked
        /// at), 2 = in the corner of the right screen (always shown).</summary>
        public static int GearPlace = 1; // build 42: 3 = left watch end, 4 = in the left panel (own cell)
        private const float GearReservePx = 74f;
        public const float GearWatchSize = 0.03f;

        /// <summary>Where the gear goes on the right watch (rig-local); false = not on the right watch now.</summary>
        public static bool GearPose(out Vector3 pos, out Quaternion rot, out float size, out bool byLook)
        {
            pos = default; rot = Quaternion.identity; size = GearWatchSize; byLook = true;
            if (GearPlace == 3)
            {
                if (!L.LidOk || !L.Model) return false;
                rot = L.LidRot * Quaternion.Euler(0f, 0f, L.SRoll);
                pos = L.LidPos + L.LidRot * new Vector3(0f, -(L.EffL * 0.5f + size * 0.5f + 0.004f), -(L.Thick * 0.5f + 0.002f));
                return true;
            }
            if (GearPlace == 4 || GearPlace == 5) return MimesisSettingsUI.WristHud.GearCell(out pos, out rot, out size, out byLook);
            if (GearPlace == 2)
            {
                if (!ScreenActive) return false;
                byLook = false;
                size = Mathf.Clamp(ScreenSize.y * 0.36f, 0.012f, 0.05f);
                rot = ScreenRot;
                pos = ScreenPos + ScreenRot * new Vector3(ScreenSize.x * 0.5f - size * 0.5f - ScreenSize.x * 0.02f, ScreenSize.y * 0.5f - size * 0.5f - ScreenSize.y * 0.04f, -0.0008f);
                return true;
            }
            if (GearPlace != 1) return false;
            if (R.LidOk && R.Model)
            {
                rot = R.LidRot * Quaternion.Euler(0f, 0f, R.SRoll);
                pos = R.LidPos + R.LidRot * new Vector3(0f, -(R.EffL * 0.5f + size * 0.5f + 0.004f), -(R.Thick * 0.5f + 0.002f));
                return true;
            }
            if (ScreenActive)
            {
                rot = ScreenRot;
                pos = ScreenPos + ScreenRot * new Vector3(0f, -(ScreenSize.y * 0.5f + size * 0.5f + 0.004f), 0f);
                return true;
            }
            return false;
        }

        /// <summary>The gear is on the left side (pressed with the right hand).</summary>
        public static bool GearLeft => GearPlace == 0 || GearPlace == 3 || GearPlace == 4 || GearPlace == 5;

        // ------------------------------------------------------------------ watch face (build 42)
        private static Material _dialMat;
        private static Mesh _dialMesh;

        /// <summary>A dark octagon over the whole top of the body (the lid is eight-sided): the screen sits on a watch
        /// face instead of on bare wood. A world mesh - seen on the monitor too, hidden under the screens.</summary>
        private static void UpdateDial(Side s, bool show)
        {
            show &= s.Dial > 0.01f;
            if (!show) { if (s.DialGo != null && s.DialGo.activeSelf) s.DialGo.SetActive(false); return; }
            if (s.DialGo == null)
            {
                if (_dialMat == null)
                {
                    var sh = Shader.Find("Sprites/Default");
                    if (sh == null) return;
                    _dialMat = new Material(sh) { name = "MimesisVR_WatchDial", renderQueue = 3100 };
                }
                if (_dialMesh == null) _dialMesh = OctagonMesh(0.26f);
                s.DialGo = new GameObject("Dial");
                s.DialGo.transform.SetParent(s.Go.transform, false);
                s.DialGo.AddComponent<MeshFilter>().sharedMesh = _dialMesh;
                var r = s.DialGo.AddComponent<MeshRenderer>();
                r.sharedMaterial = _dialMat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            }
            if (!s.DialGo.activeSelf) s.DialGo.SetActive(true);
            var t = s.DialGo.transform;
            t.localPosition = new Vector3(0f, 0f, -(s.Thick * 0.5f + 0.0006f));
            t.localRotation = Quaternion.identity;
            t.localScale = new Vector3(Mathf.Max(0.005f, s.EffW - 0.004f), Mathf.Max(0.005f, s.EffL - 0.004f), 1f);
            var mr = s.DialGo.GetComponent<MeshRenderer>();
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_Color", new Color(0.03f, 0.035f, 0.04f, Mathf.Clamp01(s.Dial)));
            mr.SetPropertyBlock(mpb);
        }

        /// <summary>Unit octagon (1 x 1) with cut corners (share of the side), facing -Z.</summary>
        private static Mesh OctagonMesh(float cut)
        {
            float h = 0.5f, c = 0.5f - cut;
            var v = new[] { Vector3.zero, new Vector3(-c, h), new Vector3(c, h), new Vector3(h, c), new Vector3(h, -c), new Vector3(c, -h), new Vector3(-c, -h), new Vector3(-h, -c), new Vector3(-h, c) };
            var tri = new List<int>();
            for (int i = 1; i <= 8; i++) { tri.Add(0); tri.Add(i == 8 ? 1 : i + 1); tri.Add(i); }
            var m = new Mesh { name = "MimesisVR_Octagon" };
            m.vertices = v;
            m.colors = Enumerable.Repeat(Color.white, v.Length).ToArray();
            m.uv = v.Select(p => new Vector2(p.x + 0.5f, p.y + 0.5f)).ToArray();
            m.triangles = tri.ToArray();
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ damage flash (build 42)
        /// <summary>Red flash on the eyes and a short vibration when health drops.</summary>
        public static bool DamageFlash = true, DamageBuzz = true;
        private static float _lastHp = -1f, _flash;
        private static Texture2D _vignette;
        private static Material _flashMat;

        private static void TrackDamage(Features.VRVisualization.OpenXR.XrControllerInput c, bool vr)
        {
            _flash = Mathf.MoveTowards(_flash, 0f, Time.unscaledDeltaTime * 1.4f);
            var ig = vr ? InGameHud() : null;
            if (ig == null || ig.isDead) { _lastHp = -1f; return; }
            float hp = Hp(ig);
            if (_lastHp >= 0f && hp < _lastHp - 0.004f)
            {
                float drop = _lastHp - hp;
                _flash = Mathf.Clamp01(Mathf.Max(_flash, 0.45f + drop * 3f));
                if (DamageBuzz) try { c.Vibrate(true, 0.12f, Mathf.Clamp01(0.4f + drop * 3f)); c.Vibrate(false, 0.12f, Mathf.Clamp01(0.4f + drop * 3f)); } catch { }
            }
            _lastHp = hp;
        }

        /// <summary>From the eye render: red vignette over the whole eye picture while the flash lasts.</summary>
        public static void CompositeDamage(RenderTexture eye)
        {
            if (!DamageFlash || _flash <= 0.01f || eye == null) return;
            try
            {
                if (_vignette == null)
                {
                    const int N = 128;
                    _vignette = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                    var px = new Color32[N * N];
                    for (int y = 0; y < N; y++)
                        for (int x = 0; x < N; x++)
                        {
                            float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                            float r = Mathf.Sqrt(dx * dx + dy * dy);
                            float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1.15f, r)) * 0.85f;
                            px[y * N + x] = new Color32(150, 8, 8, (byte)(a * 255f));
                        }
                    _vignette.SetPixels32(px); _vignette.Apply();
                }
                if (_flashMat == null) _flashMat = new Material(Shader.Find("UI/Default"));
                _flashMat.color = new Color(1f, 1f, 1f, _flash);
                var prev = RenderTexture.active;
                RenderTexture.active = eye;
                GL.PushMatrix();
                GL.LoadPixelMatrix(0, eye.width, eye.height, 0);
                Graphics.DrawTexture(new Rect(0, 0, eye.width, eye.height), _vignette, _flashMat);
                GL.PopMatrix();
                RenderTexture.active = prev;
            }
            catch { DamageFlash = false; }
        }

        /// <summary>Public for the gear: fade a premultiplied picture.</summary>
        public static void Fade(RenderTexture rt, float a) => MultiplyAlpha(rt, a);

        // ------------------------------------------------------------------ right screen: data
        private static UIPrefab_InGame _ig;
        private static FieldInfo _hpField, _hasHpField;
        private static Image _hpImg, _contaImg, _stamFill;
        private static Transform _stamRoot;

        private static UIPrefab_InGame InGameHud()
        {
            var p = MimesisHudSegments.Status.Panel;
            if (p == null || !p.gameObject.activeInHierarchy) return null;
            if (_ig == null || _ig.transform != p) { _ig = p.GetComponent<UIPrefab_InGame>() ?? p.GetComponentInChildren<UIPrefab_InGame>(true); _hpImg = null; _contaImg = null; }
            return _ig;
        }

        private static float Hp(UIPrefab_InGame ig)
        {
            _hpField ??= typeof(UIPrefab_InGame).GetField("latestHpPercent", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _hasHpField ??= typeof(UIPrefab_InGame).GetField("hasHpValue", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            try
            {
                if (_hasHpField != null && _hasHpField.GetValue(ig) is bool has && !has) return 1f;
                if (_hpField != null && _hpField.GetValue(ig) is float f) return Mathf.Clamp01(f);
            }
            catch { }
            return 1f;
        }

        private static Color HpColor(UIPrefab_InGame ig, float hp)
        {
            Color own = hp >= 0.75f ? new Color(0.36f, 0.93f, 0.55f) : hp >= 0.5f ? new Color(0.95f, 0.86f, 0.32f) : hp >= 0.25f ? new Color(1f, 0.56f, 0.22f) : new Color(1f, 0.26f, 0.22f);
            try
            {
                if (_hpImg == null && ig.UE_HPGraph != null)
                    foreach (var im in ig.UE_HPGraph.GetComponentsInChildren<Image>(true)) if (im.name == "hp_Fill_01") { _hpImg = im; break; }
                if (_hpImg != null && Saturated(_hpImg.color)) { var gc = _hpImg.color; return new Color(gc.r, gc.g, gc.b, 1f); }
            }
            catch { }
            return own;
        }

        private static float Conta(UIPrefab_InGame ig, out Color col)
        {
            float v = 0f;
            col = new Color(0.55f, 0.95f, 0.35f);
            try
            {
                var sl = ig.UE_OxyGaugeSlider;
                if (sl != null)
                {
                    v = Mathf.Clamp01(sl.normalizedValue);
                    if (_contaImg == null && sl.fillRect != null) _contaImg = sl.fillRect.GetComponent<Image>();
                    if (_contaImg != null && Saturated(_contaImg.color)) { var gc = _contaImg.color; col = new Color(gc.r, gc.g, gc.b, 1f); }
                    else col = Color.Lerp(new Color(0.55f, 0.95f, 0.35f), new Color(1f, 0.3f, 0.2f), v);
                }
            }
            catch { }
            return v;
        }

        private static bool Stamina(out float v, out Color col)
        {
            v = 1f; col = new Color(0.75f, 0.85f, 0.95f);
            var root = MimesisHudSegments.StaminaRoot;
            if (root != _stamRoot) { _stamRoot = root; _stamFill = null; }
            if (root == null) return false;
            if (_stamFill == null) foreach (var im in root.GetComponentsInChildren<Image>(true)) if (im.name == "StaminaGaugeFilled") { _stamFill = im; break; }
            if (_stamFill != null) { v = Mathf.Clamp01(_stamFill.fillAmount); var gc = _stamFill.color; col = new Color(gc.r, gc.g, gc.b, 1f); }
            return root.gameObject.activeInHierarchy;
        }

        private static bool Saturated(Color c)
        {
            Color.RGBToHSV(c, out _, out float s, out float v);
            return s > 0.2f && v > 0.2f && c.a > 0.1f;
        }

        // ------------------------------------------------------------------ right screen: drawing
        private static RenderTexture _draw, _out;
        private static Material _mat2, _fadeMat;
        private static bool _linear;
        // pulse trace: samples at 200 Hz
        private const int Hz = 200, Ring = 1024;
        private static readonly float[] _trace = new float[Ring];
        private static int _head;
        private static float _acc, _phase, _hpShown = 1f, _contaShown;
        private static readonly System.Random _rnd = new(40);
        private struct Bubble { public float X, Y, R, V, W, Age; }
        private static readonly List<Bubble> _bubbles = new();
        private static float _bubbleAcc;

        private static void EnsureTargets()
        {
            if (_out != null && _out.IsCreated() && _draw != null && _draw.IsCreated()) return;
            var fmt = UnityEngine.Experimental.Rendering.GraphicsFormat.B8G8R8A8_SRGB;
            var desc = new RenderTextureDescriptor(PxW, PxH, fmt, 0) { msaaSamples = 4 };
            _draw = new RenderTexture(desc); _draw.Create();
            _out = new RenderTexture(PxW, PxH, 0, fmt) { filterMode = FilterMode.Bilinear }; _out.Create();
            _linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            var sh = Shader.Find("Hidden/Internal-Colored");
            _mat2 = new Material(sh);
            _mat2.SetInt("_SrcBlend", (int)BlendMode.One);
            _mat2.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _mat2.SetInt("_ZTest", (int)CompareFunction.Always);
            _mat2.SetInt("_ZWrite", 0);
            _mat2.SetInt("_Cull", (int)CullMode.Off);
            VRModCore.Log($"[MimesisVR] Right watch screen {PxW}x{PxH} px, MSAA x4, {(_linear ? "linear" : "gamma")} colours.");
        }

        /// <summary>Premultiplied colour for the sRGB target.</summary>
        private static Color P(Color c, float a)
        {
            a = Mathf.Clamp01(a * c.a);
            Color l = _linear ? c.linear : c;
            return new Color(l.r * a, l.g * a, l.b * a, a);
        }

        private static float Beat(float ph)
        {
            // P, QRS, T of a cardiogram, 0 = base line
            if (ph < 0.10f) return 0f;
            if (ph < 0.20f) return 0.12f * Mathf.Sin(Mathf.PI * (ph - 0.10f) / 0.10f);
            if (ph < 0.24f) return 0f;
            if (ph < 0.26f) return -0.14f * (ph - 0.24f) / 0.02f;
            if (ph < 0.28f) return Mathf.Lerp(-0.14f, 1f, (ph - 0.26f) / 0.02f);
            if (ph < 0.31f) return Mathf.Lerp(1f, -0.32f, (ph - 0.28f) / 0.03f);
            if (ph < 0.34f) return Mathf.Lerp(-0.32f, 0f, (ph - 0.31f) / 0.03f);
            if (ph < 0.46f) return 0f;
            if (ph < 0.64f) return 0.26f * Mathf.Sin(Mathf.PI * (ph - 0.46f) / 0.18f);
            return 0f;
        }

        private static void Draw(UIPrefab_InGame ig)
        {
            EnsureTargets();
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            float hp = Hp(ig);
            _hpShown = Mathf.MoveTowards(_hpShown, hp, dt * 0.8f);
            Color hpc = HpColor(ig, hp);
            float conta = Conta(ig, out Color cc);
            _contaShown = Mathf.MoveTowards(_contaShown, conta, dt * 0.6f);
            bool stamOn = Stamina(out float stam, out Color sc);

            // pulse: faster and a bit weaker when hurt
            float bpm = Mathf.Lerp(150f, 68f, hp);
            float amp = Mathf.Lerp(0.75f, 1f, hp);
            _acc += dt;
            while (_acc >= 1f / Hz)
            {
                _acc -= 1f / Hz;
                _phase = (_phase + bpm / 60f / Hz) % 1f;
                _trace[_head] = Beat(_phase) * amp + ((float)_rnd.NextDouble() - 0.5f) * 0.015f;
                _head = (_head + 1) % Ring;
            }

            var prev = RenderTexture.active;
            RenderTexture.active = _draw;
            GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, PxW, PxH, 0); // y down: top-left origin while drawing
            _mat2.SetPass(0);
            GL.Begin(GL.TRIANGLES);

            // backing and frame (compass bronze)
            Color bronze = new Color(0.80f, 0.66f, 0.42f);
            RoundRect(2, 2, PxW - 4, PxH - 4, 22, P(new Color(0.035f, 0.04f, 0.045f), Mathf.Clamp01(R.BgAlpha)));
            RoundFrame(2, 2, PxW - 4, PxH - 4, 22, 3, P(bronze, 0.75f));

            // pulse area: grid + trace
            float ex = 20, ey = 18, ew = PxW - 40 - (GearPlace == 2 ? GearReservePx : 0f), eh = 128;
            for (int i = 1; i < 8; i++) Rect(ex + ew * i / 8f - 0.75f, ey, 1.5f, eh, P(hpc, 0.10f));
            for (int i = 1; i < 4; i++) Rect(ex, ey + eh * i / 4f - 0.75f, ew, 1.5f, P(hpc, 0.10f));
            float baseY = ey + eh * 0.68f, scaleY = eh * 0.58f;
            const float window = 2.4f;
            int n = Mathf.Min(Ring - 1, (int)(window * Hz));
            Vector2 last = default;
            Vector2[] pts = _pts;
            for (int i = 0; i <= n; i++)
            {
                int idx = ((_head - 1 - (n - i)) % Ring + Ring) % Ring;
                float age = (n - i) / (float)Hz;
                pts[i] = new Vector2(ex + ew * (1f - age / window), baseY - _trace[idx] * scaleY);
            }
            for (int pass = 0; pass < 2; pass++)
            {
                float th = pass == 0 ? 11f : 3.6f;
                for (int i = 1; i <= n; i++)
                {
                    float k = i / (float)n; // 0 = oldest
                    float a = pass == 0 ? 0.16f * k : Mathf.Lerp(0.12f, 1f, k * k);
                    Line(pts[i - 1], pts[i], th, P(hpc, a));
                }
            }
            last = pts[n];
            Disc(last.x, last.y, 10f, P(hpc, 0.25f));
            Disc(last.x, last.y, 5f, P(Color.Lerp(hpc, Color.white, 0.5f), 1f));

            // health in quarters
            float rowX = 62, rowW = PxW - 20 - rowX;
            float hy = 162, hh = 28;
            Cross(36, hy + hh * 0.5f, 24, 8, P(hpc, 0.95f));
            float gap = 6f, segW = (rowW - gap * 3f) / 4f;
            for (int i = 0; i < 4; i++)
            {
                float sx = rowX + i * (segW + gap);
                Rect(sx, hy, segW, hh, P(Color.white, 0.08f));
                float f = Mathf.Clamp01((_hpShown - i * 0.25f) / 0.25f);
                if (f > 0f) Rect(sx, hy, segW * f, hh, P(hpc, 0.92f));
            }

            // contamination: tube with bubbles (always shown, also at zero)
            float cy = 206, ch = 44;
            Trefoil(36, cy + ch * 0.5f, 15f, P(Color.Lerp(cc, new Color(0.9f, 0.9f, 0.9f), 0.25f), 0.95f));
            RoundRect(rowX, cy, rowW, ch, ch * 0.5f, P(Color.white, 0.07f));
            float fillW = Mathf.Max(0f, rowW * _contaShown);
            if (fillW > 1f) RoundRect(rowX, cy, Mathf.Max(fillW, ch), ch, ch * 0.5f, P(cc, 0.85f));
            UpdateBubbles(dt, rowX, cy, fillW, ch);
            Color bc = Color.Lerp(cc, Color.white, 0.6f);
            foreach (var b in _bubbles)
            {
                float a = Mathf.Clamp01(b.Age * 3f) * Mathf.Clamp01((b.Y - cy) / 8f);
                Ring2(b.X, b.Y, b.R, 1.6f, P(bc, 0.85f * a));
                Disc(b.X - b.R * 0.3f, b.Y - b.R * 0.3f, b.R * 0.28f, P(Color.white, 0.6f * a));
            }
            RoundFrame(rowX, cy, rowW, ch, ch * 0.5f, 2f, P(new Color(0.85f, 0.85f, 0.85f), 0.35f));

            // stamina: thin bar, only while the game shows its bar (the place is kept)
            if (stamOn)
            {
                float sy = 264, sh = 14;
                Chevrons(36, sy + sh * 0.5f, P(sc, 0.9f));
                RoundRect(rowX, sy, rowW, sh, sh * 0.5f, P(Color.white, 0.08f));
                if (stam > 0.01f) RoundRect(rowX, sy, Mathf.Max(sh, rowW * stam), sh, sh * 0.5f, P(sc, 0.95f));
            }

            GL.End();
            GL.PopMatrix();
            // Unity RT orientation -> OpenXR (top row first); also resolves MSAA
            Graphics.Blit(_draw, _out, new Vector2(1f, -1f), new Vector2(0f, 1f));
            RenderTexture.active = prev;
        }

        private static readonly Vector2[] _pts = new Vector2[Ring];

        private static void UpdateBubbles(float dt, float x0, float y0, float fillW, float h)
        {
            float conta = _contaShown;
            if (conta > 0.005f && fillW > h * 0.6f)
            {
                _bubbleAcc += dt * (1.5f + 22f * conta);
                while (_bubbleAcc >= 1f)
                {
                    _bubbleAcc -= 1f;
                    if (_bubbles.Count >= 60) break;
                    float r = 2.5f + (float)_rnd.NextDouble() * (3f + 3f * conta);
                    _bubbles.Add(new Bubble
                    {
                        X = x0 + h * 0.3f + (float)_rnd.NextDouble() * Mathf.Max(1f, fillW - h * 0.6f),
                        Y = y0 + h - r - 2f, R = r,
                        V = 14f + (float)_rnd.NextDouble() * (18f + 30f * conta),
                        W = (float)_rnd.NextDouble() * 6.28f, Age = 0f
                    });
                }
            }
            for (int i = _bubbles.Count - 1; i >= 0; i--)
            {
                var b = _bubbles[i];
                b.Age += dt; b.Y -= b.V * dt; b.W += dt * 5f;
                b.X += Mathf.Sin(b.W) * 6f * dt;
                if (b.Y - b.R < y0 + 3f || b.X > x0 + Mathf.Max(fillW, h) - b.R) _bubbles.RemoveAt(i);
                else _bubbles[i] = b;
            }
        }

        // ---- GL helpers (top-left pixel coordinates, triangles, premultiplied colours)
        private static void V(float x, float y) => GL.Vertex3(x, y, 0f);
        private static void Tri(float ax, float ay, float bx, float by, float cx, float cy, Color c)
        {
            GL.Color(c); V(ax, ay); V(bx, by); V(cx, cy);
        }
        private static void Rect(float x, float y, float w, float h, Color c)
        {
            if (w <= 0f || h <= 0f) return;
            GL.Color(c);
            V(x, y); V(x + w, y); V(x + w, y + h);
            V(x, y); V(x + w, y + h); V(x, y + h);
        }
        private static void Fan(float cx, float cy, float r, float a0, float a1, Color c, int seg = 8)
        {
            GL.Color(c);
            for (int i = 0; i < seg; i++)
            {
                float t0 = Mathf.Lerp(a0, a1, i / (float)seg), t1 = Mathf.Lerp(a0, a1, (i + 1) / (float)seg);
                V(cx, cy); V(cx + Mathf.Cos(t0) * r, cy + Mathf.Sin(t0) * r); V(cx + Mathf.Cos(t1) * r, cy + Mathf.Sin(t1) * r);
            }
        }
        private static void Disc(float cx, float cy, float r, Color c) => Fan(cx, cy, r, 0f, Mathf.PI * 2f, c, 20);
        private static void Ring2(float cx, float cy, float r, float th, Color c)
        {
            GL.Color(c);
            const int seg = 16;
            for (int i = 0; i < seg; i++)
            {
                float t0 = Mathf.PI * 2f * i / seg, t1 = Mathf.PI * 2f * (i + 1) / seg;
                float c0 = Mathf.Cos(t0), s0 = Mathf.Sin(t0), c1 = Mathf.Cos(t1), s1 = Mathf.Sin(t1);
                float ri = Mathf.Max(0f, r - th);
                V(cx + c0 * r, cy + s0 * r); V(cx + c1 * r, cy + s1 * r); V(cx + c1 * ri, cy + s1 * ri);
                V(cx + c0 * r, cy + s0 * r); V(cx + c1 * ri, cy + s1 * ri); V(cx + c0 * ri, cy + s0 * ri);
            }
        }
        private static void RoundRect(float x, float y, float w, float h, float r, Color c)
        {
            r = Mathf.Min(r, Mathf.Min(w, h) * 0.5f);
            Rect(x + r, y, w - 2f * r, h, c);
            Rect(x, y + r, r, h - 2f * r, c);
            Rect(x + w - r, y + r, r, h - 2f * r, c);
            Fan(x + r, y + r, r, Mathf.PI, Mathf.PI * 1.5f, c);
            Fan(x + w - r, y + r, r, Mathf.PI * 1.5f, Mathf.PI * 2f, c);
            Fan(x + w - r, y + h - r, r, 0f, Mathf.PI * 0.5f, c);
            Fan(x + r, y + h - r, r, Mathf.PI * 0.5f, Mathf.PI, c);
        }
        private static void RoundFrame(float x, float y, float w, float h, float r, float th, Color c)
        {
            r = Mathf.Min(r, Mathf.Min(w, h) * 0.5f);
            Rect(x + r, y, w - 2f * r, th, c);
            Rect(x + r, y + h - th, w - 2f * r, th, c);
            Rect(x, y + r, th, h - 2f * r, c);
            Rect(x + w - th, y + r, th, h - 2f * r, c);
            Arc(x + r, y + r, r, th, Mathf.PI, Mathf.PI * 1.5f, c);
            Arc(x + w - r, y + r, r, th, Mathf.PI * 1.5f, Mathf.PI * 2f, c);
            Arc(x + w - r, y + h - r, r, th, 0f, Mathf.PI * 0.5f, c);
            Arc(x + r, y + h - r, r, th, Mathf.PI * 0.5f, Mathf.PI, c);
        }
        private static void Arc(float cx, float cy, float r, float th, float a0, float a1, Color c)
        {
            GL.Color(c);
            const int seg = 8;
            float ri = Mathf.Max(0f, r - th);
            for (int i = 0; i < seg; i++)
            {
                float t0 = Mathf.Lerp(a0, a1, i / (float)seg), t1 = Mathf.Lerp(a0, a1, (i + 1) / (float)seg);
                float c0 = Mathf.Cos(t0), s0 = Mathf.Sin(t0), c1 = Mathf.Cos(t1), s1 = Mathf.Sin(t1);
                V(cx + c0 * r, cy + s0 * r); V(cx + c1 * r, cy + s1 * r); V(cx + c1 * ri, cy + s1 * ri);
                V(cx + c0 * r, cy + s0 * r); V(cx + c1 * ri, cy + s1 * ri); V(cx + c0 * ri, cy + s0 * ri);
            }
        }
        private static void Line(Vector2 a, Vector2 b, float th, Color c)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            Vector2 nrm = new Vector2(-d.y, d.x) / len * (th * 0.5f);
            Vector2 ext = d / len * (th * 0.35f); // a little overlap so joints have no gaps
            Vector2 a2 = a - ext, b2 = b + ext;
            GL.Color(c);
            V(a2.x + nrm.x, a2.y + nrm.y); V(b2.x + nrm.x, b2.y + nrm.y); V(b2.x - nrm.x, b2.y - nrm.y);
            V(a2.x + nrm.x, a2.y + nrm.y); V(b2.x - nrm.x, b2.y - nrm.y); V(a2.x - nrm.x, a2.y - nrm.y);
        }
        private static void Cross(float cx, float cy, float size, float th, Color c)
        {
            Rect(cx - size * 0.5f, cy - th * 0.5f, size, th, c);
            Rect(cx - th * 0.5f, cy - size * 0.5f, th, size, c);
        }
        private static void Trefoil(float cx, float cy, float r, Color c)
        {
            // radiation sign: three 60-degree blades around a small disc
            for (int k = 0; k < 3; k++)
            {
                float a0 = Mathf.PI / 2f + k * Mathf.PI * 2f / 3f - Mathf.PI / 6f; // one blade down, two up (y goes down)
                float a1 = a0 + Mathf.PI / 3f;
                GL.Color(c);
                const int seg = 6;
                float ri = r * 0.32f;
                for (int i = 0; i < seg; i++)
                {
                    float t0 = Mathf.Lerp(a0, a1, i / (float)seg), t1 = Mathf.Lerp(a0, a1, (i + 1) / (float)seg);
                    float c0 = Mathf.Cos(t0), s0 = Mathf.Sin(t0), c1 = Mathf.Cos(t1), s1 = Mathf.Sin(t1);
                    V(cx + c0 * r, cy + s0 * r); V(cx + c1 * r, cy + s1 * r); V(cx + c1 * ri, cy + s1 * ri);
                    V(cx + c0 * r, cy + s0 * r); V(cx + c1 * ri, cy + s1 * ri); V(cx + c0 * ri, cy + s0 * ri);
                }
            }
            Disc(cx, cy, r * 0.2f, c);
        }
        private static void Chevrons(float cx, float cy, Color c)
        {
            for (int k = 0; k < 2; k++)
            {
                float x = cx - 9f + k * 9f;
                Line(new Vector2(x, cy - 7f), new Vector2(x + 7f, cy), 3.5f, c);
                Line(new Vector2(x + 7f, cy), new Vector2(x, cy + 7f), 3.5f, c);
            }
        }

        /// <summary>Fade a premultiplied picture: every channel times a.</summary>
        private static void MultiplyAlpha(RenderTexture rt, float a)
        {
            if (_fadeMat == null)
            {
                var sh = Shader.Find("Hidden/Internal-Colored");
                if (sh == null) return;
                _fadeMat = new Material(sh);
                _fadeMat.SetInt("_SrcBlend", (int)BlendMode.Zero);
                _fadeMat.SetInt("_DstBlend", (int)BlendMode.SrcAlpha);
                _fadeMat.SetInt("_ZTest", (int)CompareFunction.Always);
                _fadeMat.SetInt("_ZWrite", 0);
                _fadeMat.SetInt("_Cull", (int)CullMode.Off);
            }
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, rt.width, 0, rt.height);
            _fadeMat.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(new Color(0f, 0f, 0f, Mathf.Clamp01(a)));
            GL.Vertex3(0, 0, 0); GL.Vertex3(0, rt.height, 0); GL.Vertex3(rt.width, rt.height, 0); GL.Vertex3(rt.width, 0, 0);
            GL.End();
            GL.PopMatrix();
            RenderTexture.active = prev;
        }
    }
}
