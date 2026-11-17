// Heights: the player's level (2026-11-11 headset test in "Play in my room", no Space Setup: "i was in front of that
// instrument and when off my meta and came back that instrument become low. and then later i was unable to bring that
// instrument to my level"; "allow it place instrument to their height/level").
//
// Every instrument's default height is its home (a seated height) above the floor plus the PLAYER LIFT, held inside the
// instrument's own Seat.LiftMin / LiftMax (the side table and the menu never move up or down):
//   - Reset view (the menu's row, or both hands held still) sets the lift for the eyes' height now (EyeLift: none for a
//     seated player, eyes 1.05-1.30 m above the floor; the excess above that, a bar stool or standing; the shortfall below
//     it, a low sofa) and forgets the height tweaks of the layout, so Reset view always brings every instrument back to the
//     player's level; the spots (distance, side, turn) are kept;
//   - the headset coming off and on, the system menu and a tracking loss are an AWAY: the eyes' height (smoothed while
//     playing) is kept when it starts, and when the headset is back, worn, tracked and still for ReturnSettle the eyes are
//     measured again. A change of FollowMin..FollowMax (the system re-estimated its floor after the tracking loss, or the
//     player sits a little differently) moves every instrument up or down by as much, so they stay where they were for
//     the player; a bigger change (sat down, stood up) takes the lift for the eyes now. The system's recentre events that
//     come in a burst on every resume (often while the head is still untracked or the headset not yet on: the headset log
//     of 2026-11-11 06:37:06 / 06:38:42 / 06:46:19) wait for that return and are applied once, with a fresh head pose;
//   - a Move instruments drag lifts or lowers the held instrument with the hand (LayoutModule.Move), and the tweak is kept
//     with its spot (ComfortOffset.Height, relative to the default height of that moment).
// A raised instrument stands on a riser (RiserVisuals), the piano on its stretched stand.
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Layout
{
    public sealed partial class LayoutModule
    {
        float _playerLift;
        /// <summary>The player lift in force (metres added to every instrument's home height, each clamped to its range).</summary>
        public float PlayerLift => _playerLift;
        /// <summary>The risers under raised instruments (state readable when headless).</summary>
        public RiserVisuals Risers { get; private set; }

        // ---- the eyes, the away and the return
        float _eyeEma = float.NaN, _eyeBefore = float.NaN;
        bool _focused = true, _away, _returning, _recentrePending;
        float _retT, _retMin, _retMax, _retSum;
        int _retN;
        readonly List<RigId> _tweakKeys = new List<RigId>(8);

        /// <summary>The eyes' height above the floor, smoothed while the headset is worn and tracked (NaN before).</summary>
        public float EyeHeight => _eyeEma;
        /// <summary>The headset is off, the system menu has the hands, tracking is lost, or it is settling after one of those.</summary>
        public bool AwayOrSettling => _away || _returning;
        /// <summary>Returns that compared the eyes since boot, the last one's eye change and the lift it moved by (tests, logs).</summary>
        public int Returns { get; private set; }
        public float LastReturnEyeShift { get; private set; }
        public float LastReturnLiftShift { get; private set; }
        /// <summary>Recentre events held back while the headset was away or settling (applied once on the return).</summary>
        public int DeferredRecentres { get; private set; }

        /// <summary>The lift for a player whose eyes are <paramref name="eye"/> m above the floor: none for a seated player,
        /// else the eyes' distance above SeatedEyeMax (or below SeatedEyeMin), within PlayerLiftMin..Max.</summary>
        public static float EyeLift(float eye)
        {
            if (float.IsNaN(eye)) return 0f;
            float lift = eye > HeightConstants.SeatedEyeMax ? eye - HeightConstants.SeatedEyeMax
                : eye < HeightConstants.SeatedEyeMin ? eye - HeightConstants.SeatedEyeMin : 0f;
            return Mathf.Clamp(lift, HeightConstants.PlayerLiftMin, HeightConstants.PlayerLiftMax);
        }

        /// <summary>The player lift as one instrument takes it (its Seat.LiftMin / LiftMax; 0 for the side table, the menu
        /// and any rig without a descriptor).</summary>
        float LiftFor(Rig r) => LiftFor(r, _playerLift);

        float LiftFor(Rig r, float lift)
        {
            if (r == null || !IsRowRig(r.Id)) return 0f;
            InstrumentDescriptor d = _app?.Registry?.Get(KindOf(r.Id));
            if (d?.Seat == null) return 0f;
            return Mathf.Clamp(lift, Mathf.Min(0f, d.Seat.LiftMin), Mathf.Max(0f, d.Seat.LiftMax));
        }

        /// <summary>A rig's default height: its home height <paramref name="homeY"/> on the floor (the real one in mixed
        /// reality) plus the player lift.</summary>
        float DefaultY(Rig r, float homeY) => homeY + FloorY + LiftFor(r);

        bool HeadTracked => _app.Rig == null || _app.Rig.HeadTracked;

        // ---------------------------------------------------------------- the frame (FirstArrange phase)

        void TickHeight(float dt)
        {
            if (_app.Head == null || !Presenting) return;
            if (!_mounted || !_focused || !HeadTracked)
            {
                if (!_away) GoAway(!HeadTracked ? "tracking lost" : !_mounted ? "headset off" : "system menu");
                _returning = false;
                return;
            }
            if (_away)
            {
                _away = false;
                _returning = true;
                ResetReturn();
            }
            float eye = _app.Head.position.y - FloorY;
            bool valid = eye >= HeightConstants.EyeMin && eye <= HeightConstants.EyeMax;
            if (_returning)
            {
                if (!valid) { ResetReturn(); return; }
                if (_retN == 0) _retMin = _retMax = eye;
                _retMin = Mathf.Min(_retMin, eye);
                _retMax = Mathf.Max(_retMax, eye);
                if (_retMax - _retMin > HeightConstants.ReturnStill)
                {
                    // still settling the headset: the wait starts again here
                    ResetReturn();
                    _retMin = _retMax = eye;
                }
                _retSum += eye;
                _retN++;
                _retT += dt;
                if (_retT >= HeightConstants.ReturnSettle) FinishReturn(_retSum / _retN);
                return;
            }
            if (!valid) return;
            if (float.IsNaN(_eyeEma)) _eyeEma = eye;
            else _eyeEma += (1f - Mathf.Exp(-dt / HeightConstants.EyeTau)) * (eye - _eyeEma);
        }

        void ResetReturn()
        {
            _retT = _retSum = 0f;
            _retN = 0;
        }

        /// <summary>The headset came off, the system menu took the hands, or tracking was lost: the eyes' height of the
        /// moment is kept for the return (a second away while settling keeps the first one's).</summary>
        void GoAway(string why)
        {
            if (_away) return;
            if (!_returning) _eyeBefore = _eyeEma;
            _away = true;
            _returning = false;
            Debug.Log($"[Layout] away ({why}): eyes {(float.IsNaN(_eyeBefore) ? "unknown" : _eyeBefore.ToString("F2") + " m")} above the floor");
        }

        /// <summary>Back, worn, tracked and still: the eyes now against the eyes when it went away. Small change: every
        /// instrument moves by as much (the player's level is kept); big change: the lift for the eyes now. Then the recentre
        /// that waited, if any (it places with the new lift).</summary>
        void FinishReturn(float eye)
        {
            _returning = false;
            float before = _eyeBefore, old = _playerLift;
            _eyeBefore = float.NaN;
            _eyeEma = eye;
            float d = float.IsNaN(before) ? 0f : eye - before;
            string what = "unchanged";
            if (!float.IsNaN(before) && Mathf.Abs(d) >= HeightConstants.FollowMin)
            {
                if (Mathf.Abs(d) <= HeightConstants.FollowMax)
                {
                    _playerLift = Mathf.Clamp(_playerLift + d, HeightConstants.PlayerLiftMin, HeightConstants.PlayerLiftMax);
                    what = "the instruments follow (the floor moved under the player, or they sit a little differently)";
                }
                else
                {
                    _playerLift = EyeLift(eye);
                    what = "a new posture: the lift for the eyes now";
                }
            }
            Returns++;
            LastReturnEyeShift = d;
            LastReturnLiftShift = _playerLift - old;
            Debug.Log($"[Layout] back: eyes {(float.IsNaN(before) ? "unknown" : before.ToString("F2"))} -> {eye:F2} m above the floor ({d:+0.00;-0.00} m), " +
                      $"player lift {old:+0.00;-0.00} -> {_playerLift:+0.00;-0.00} m: {what}");
            int backs = BringBacks;
            if (_recentrePending)
            {
                _recentrePending = false;
                // nothing when the origin is not under the player (BringBack's rule); about the head when the origin faces away
                BringBack(BringBackSource.Recentre, OriginFacesAway());
            }
            if (BringBacks == backs && Mathf.Abs(_playerLift - old) > 1e-4f) ApplyLiftChange(old);
        }

        /// <summary>The origin's heading this far (deg) from the settled head's on a return: the deferred recentre lays the room
        /// out about the head instead.</summary>
        public const float ReturnTurnMax = 40f;

        /// <summary>The deferred recentre's origin was set while the headset was going on (the system recentred while it was away
        /// or settling): its heading is where the head pointed then, which can be far from where the player, now settled,
        /// faces. Since 2026-11-17 the arrangement it makes is the player's front for every later bring (FRONTSPOT), so a front
        /// turned away from the player would stick (the deck behind them, the instrument to a side): then the head's pose now
        /// is the seat. A real recentre with the head on (OnRecentered) still lays out about the origin.</summary>
        bool OriginFacesAway()
        {
            if (_app?.Head == null || !HeadTracked) return false;
            ReadOrigin(out _, out float oy);
            ReadSeat(out _, out float hy);
            float d = Mathf.Abs(Mathf.DeltaAngle(oy * Mathf.Rad2Deg, hy * Mathf.Rad2Deg));
            if (d <= ReturnTurnMax) return false;
            Debug.Log($"[Layout] recentre on the return: the origin faces {d:F0} deg from the player's head; laid out about the head");
            return true;
        }

        /// <summary>The player lift changed with nothing else to place: every instrument at the seat moves up or down by its
        /// own share of the change (x, z and heading stay), inside its range.</summary>
        void ApplyLiftChange(float oldLift)
        {
            int moved = 0;
            for (int i = 0; i < _rigs.Count; i++)
            {
                Rig r = _rigs[i];
                Transform g = r.Group;
                if (g == null || !r.InScene || r == _held || !IsRowRig(r.Id) || GlideOf(KindOf(r.Id)) != null || OnRealTable(r)) continue;
                float dy = LiftFor(r) - LiftFor(r, oldLift);
                if (Mathf.Abs(dy) < 1e-5f) continue;
                LiftRangeHere(r, out float lo, out float hi);
                Vector3 p = g.position;
                g.position = new Vector3(p.x, Mathf.Clamp(p.y + dy, Mathf.Min(lo, p.y), Mathf.Max(hi, p.y)), p.z);
                moved++;
            }
            if (moved == 0) return;
            RefreshZones();
            if (_arranging) { Placement.MeasureAll(); UpdateArrangeVisuals(); }
            _app.Input.ResetMotion();   // the instruments moved under the hands
        }

        /// <summary>Reset view by the player (menu row, both hands): the lift for the eyes now, and the height tweaks of the
        /// layout forgotten (their spots stay), so every instrument comes back at the player's level.</summary>
        void LiftForEyesNow(BringBackSource source)
        {
            float eye = _app.Head.position.y - FloorY;
            if (eye < HeightConstants.EyeMin || eye > HeightConstants.EyeMax) return;
            float old = _playerLift;
            _playerLift = EyeLift(eye);
            _eyeEma = eye;
            int forgot = ForgetHeightTweaks();
            if (Mathf.Abs(old - _playerLift) > 1e-4f || forgot > 0)
                Debug.Log($"[Layout] reset view ({source}) at eyes {eye:F2} m: player lift {old:+0.00;-0.00} -> {_playerLift:+0.00;-0.00} m, {forgot} height tweak(s) forgotten");
        }

        /// <summary>The current layout's comfort offsets lose their height (kept: distance, side, turn). Returns how many.</summary>
        int ForgetHeightTweaks()
        {
            LoadOffsets();
            _tweakKeys.Clear();
            foreach (KeyValuePair<RigId, ComfortOffset> kv in _offsets) if (Mathf.Abs(kv.Value.Height) > 1e-4f) _tweakKeys.Add(kv.Key);
            for (int i = 0; i < _tweakKeys.Count; i++)
            {
                ComfortOffset o = _offsets[_tweakKeys[i]];
                o.Height = 0f;
                if (o.IsZero) _offsets.Remove(_tweakKeys[i]);
                else _offsets[_tweakKeys[i]] = o;
            }
            if (_tweakKeys.Count == 0) return 0;
            _app.Settings.SetComfort(_mode, _current, ComfortCodec.Encode(_offsets));
            _app.Settings.Save();
            _usableReady = false;
            return _tweakKeys.Count;
        }

        // ---------------------------------------------------------------- risers (LayoutAnimation phase)

        /// <summary>Parts whose lowest point is within this of the rig's feet stand on the floor: the riser covers them.</summary>
        const float RiserFootParts = 0.25f;

        /// <summary>How high a rig stands above where it would stand on the floor (0 when it is not raised that way: an
        /// instrument whose own stand stretches, the piano's; on a real table it is not raised; a moving page turn or glide is not shown).</summary>
        public float RaiseOf(Rig r) => r != null && Risers != null ? Risers.Height((int)r.Id) : 0f;

        void UpdateRisers()
        {
            RiserVisuals v = Risers;
            if (v == null) return;
            float floor = FloorY;
            for (int i = 0; i < _rigs.Count; i++)
            {
                Rig r = _rigs[i];
                int id = (int)r.Id;
                Transform g = r.Group;
                InstrumentDescriptor d = g != null && r.InScene && IsRowRig(r.Id) ? _app.Registry?.Get(KindOf(r.Id)) : null;
                if (d?.Seat == null || d.Seat.StandStretches || !g.gameObject.activeInHierarchy || Mathf.Abs(g.localScale.y - 1f) > 1e-3f)
                { v.Hide(id); continue; }
                LiftRange(r, out float lo, out float hi);
                float raise = g.position.y - r.HomeAll.y - floor;
                // Above its own range it stands on something real (a table in mixed reality), not on a riser.
                if (raise <= RiserVisuals.MinRaise || raise > hi - r.HomeAll.y + 0.02f) { v.Hide(id); continue; }
                RigEntry e = Placement.Entry(r);
                if (e == null) { v.Hide(id); continue; }
                if (!e.RiserReady) ComputeRiser(e);
                if (!e.HasRiser) { v.Hide(id); continue; }
                float yaw = LayoutMath.YawOf(g);
                LayoutMath.RotY(e.Riser.X, e.Riser.Z, yaw, out float rx, out float rz);
                Vector3 p = g.position;
                v.Set(id, new Vector3(p.x + rx, floor + raise * 0.5f, p.z + rz),
                    new Vector3(2f * (e.Riser.Hx + RiserVisuals.Lip), raise, 2f * (e.Riser.Hz + RiserVisuals.Lip)), yaw * Mathf.Rad2Deg);
            }
        }

        /// <summary>The riser's rectangle in the rig frame: the floor extent of the parts that reach down to the feet (stands,
        /// legs, the kick, the harp's base; not the cymbals or a drum's head overhanging them), else of every shown part.</summary>
        void ComputeRiser(RigEntry e)
        {
            e.RiserReady = true;
            e.HasRiser = false;
            Transform g = e.Group;
            if (g == null) return;
            Matrix4x4 toLocal = g.worldToLocalMatrix;
            float feet = -e.Rig.HomeAll.y;
            float x0 = float.PositiveInfinity, x1 = float.NegativeInfinity, z0 = float.PositiveInfinity, z1 = float.NegativeInfinity;
            bool any = false;
            for (int pass = 0; pass < 2 && !any; pass++)
                for (int i = 0; i < e.ZoneParts.Count; i++)
                {
                    PartRef part = e.ZoneParts[i];
                    if (part.R == null || !part.R.enabled || !part.R.gameObject.activeInHierarchy) continue;
                    if (!PlacementSolver.GeometryBox(part, out Bounds b, out Matrix4x4 tw)) continue;
                    Matrix4x4 m = toLocal * tw;
                    Vector3 c = b.center, x = b.extents;
                    float ylo = float.PositiveInfinity, ax0 = float.PositiveInfinity, ax1 = float.NegativeInfinity, az0 = float.PositiveInfinity, az1 = float.NegativeInfinity;
                    for (int k = 0; k < 8; k++)
                    {
                        Vector3 q = m.MultiplyPoint3x4(new Vector3(c.x + ((k & 1) != 0 ? x.x : -x.x), c.y + ((k & 2) != 0 ? x.y : -x.y), c.z + ((k & 4) != 0 ? x.z : -x.z)));
                        ylo = Mathf.Min(ylo, q.y);
                        ax0 = Mathf.Min(ax0, q.x); ax1 = Mathf.Max(ax1, q.x); az0 = Mathf.Min(az0, q.z); az1 = Mathf.Max(az1, q.z);
                    }
                    if (pass == 0 && ylo > feet + RiserFootParts) continue;
                    x0 = Mathf.Min(x0, ax0); x1 = Mathf.Max(x1, ax1); z0 = Mathf.Min(z0, az0); z1 = Mathf.Max(z1, az1);
                    any = true;
                }
            if (!any) return;
            e.Riser = new LocalRect((x0 + x1) * 0.5f, (z0 + z1) * 0.5f, (x1 - x0) * 0.5f, (z1 - z0) * 0.5f);
            e.HasRiser = true;
        }
    }
}
