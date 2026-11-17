// Grab to move and turn (spec 05 section 8). Fingers curl while playing and the headset reads that as a
// pinch, so a pinch counts as a grab only if the hand has not played for a second, it is not over a playing surface,
// it is on a rig's grab handle (its body: case, shells, frame; or in the shown menu's zone), InPlayGrab allows it (since
// 2026-11-04 only the menu: instruments move with "Move instruments", LayoutModule.Move), and the
// pinch is held still for the dwell. (Until 2026-11-10 a still pinch in empty air in mixed reality re-placed the lead
// with a reticle that followed the hand, else the head; retired: instruments move only with Move instruments, by hand.)
// One hand then drags the rig and turns it with the wrist about its footprint's
// centre; two hands twist it; the other hand's sideways pull turns it 45 degrees. A drag stops at contact and slides
// (PlacementSolver.Constrain) until the hand has pushed for UNSTICK_MS, when it goes through (red ring); let go, it
// moves the shortest way out of whatever it is in (SettleOne).
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.Layout
{
    public sealed partial class LayoutModule
    {
        // ---- the held rig
        Rig _held;
        HandState _hand, _hand2, _turner;
        float _yaw0, _turn, _curYaw, _yawPrev;          // radians, unwrapped: every grab carries on from the last angle
        float _pairPrev, _pairTurn, _pairYaw0;
        Vector3 _pivot, _pivotOff;                      // footprint centre in the rig frame; its offset from the pinch
        float _y0, _pointY0;
        bool _lifting;
        readonly float[] _homeY = new float[InstrumentIds.KindCount];   // height at the first grab since the last arrangement, by RigId
        bool _blocked, _pass;
        double _blockedAt;
        float _turnX;

        // ---- per hand
        readonly bool[] _armed = new bool[2];
        readonly Rig[] _grabRig = new Rig[2];
        readonly DwellTracker[] _dwell = { new DwellTracker(), new DwellTracker() };
        readonly WristYawTracker[] _wristYaw = { new WristYawTracker(), new WristYawTracker() };
        readonly bool[] _holdingLast = new bool[2];
        readonly Vector3[] _cageCorners = new Vector3[8];

        public Rig HeldRig => _held;
        public bool AnyHeld => _held != null;
        public HandState GrabHand => _hand;
        public HandState SecondHand => _hand2;
        public HandState TurnerHand => _turner;
        /// <summary>The held rig was pushed through another (the red ring).</summary>
        public bool PassingThrough => _pass;
        public bool IsArmed(int hand) => _armed[hand];
        public DwellTracker DwellOf(int hand) => _dwell[hand];
        /// <summary>Still pinches in empty air whose dwell completed (they move nothing; the old mixed-reality re-place of
        /// the lead from such a pinch is retired).</summary>
        public int EmptyAirPinches { get; private set; }
        /// <summary>The menu was open when the placing pinch hid its group; the next placement shows it again.</summary>
        bool _menuHiddenForPlacing;

        bool IsPlaying(HandState h) => PointerInput.IsPlaying(h, _app.Clock.NowMs, GrabConstants.PlayGuardMs);

        /// <summary>A hand played within the guard (either hand: a pinch of the free hand while the other plays is part of
        /// playing too).</summary>
        bool AnyHandPlaying(double guardMs)
        {
            HandState[] hands = _app.Input.Hands;
            for (int i = 0; i < hands.Length; i++) if (hands[i].Connected && PointerInput.IsPlaying(hands[i], _app.Clock.NowMs, guardMs)) return true;
            return false;
        }

        /// <summary>The value every new LayoutModule starts with for InPlayGrab. Off: in play no pinch moves an instrument
        /// (it moves only while "Move instruments" is on, LayoutModule.Move), so a hand resting on the piano's cheek, a drum's
        /// rim or the harp's pillar between notes (Meta reads curled fingers as a pinch) never drags it. The PlayMode tests
        /// built on the frozen reference rigs (LayoutPlayTestBase) turn it on to keep testing that path.</summary>
        public static bool InPlayGrabDefault = false;

        /// <summary>The in-play dwell grab (spec 05 section 8) may take an instrument; the menu panel is always grabbed by its
        /// zone, and Move instruments has its own grabs.</summary>
        public bool InPlayGrab = InPlayGrabDefault;

        /// <summary>Still pinches on an instrument's body that moved nothing (Move instruments off; InstrumentBodyPinchEvent).</summary>
        public int BodyPinches { get; private set; }

        bool InPlayGrabAllows(Rig r) => r != null && (InPlayGrab || r.Id == RigId.Lessons);

        /// <summary>A pinch that picked up a stick, or just missed one with no grab handle within 2 cm,
        /// is not the start of a grab (the Grab pass does this itself after IDrumsApi.TryPickUp; public for a module that
        /// takes a hand's pinch for something else).</summary>
        public void Disarm(HandState hand)
        {
            if (hand == null || hand.Index < 0 || hand.Index >= _armed.Length) return;
            _armed[hand.Index] = false;
            _grabRig[hand.Index] = null;
        }

        /// <summary>A rig the in-play grab may consider (InPlayGrabAllows decides whether it moves).</summary>
        public bool Grabbable(Rig r) => r != null;

        /// <summary>A pinch that just missed a stick still grabs a rig whose handle is this close.</summary>
        public const float StickMissReach = 0.02f;

        /// <summary>ILayoutService.RestoreAfterPlacing: placing ended without a landing (MR's cancel paths). Every rig of
        /// the layout that is out of sight shows again (MR brings back what it hid itself; a rig out of the scene stays
        /// hidden), and the menu's group too when placing hid it while the menu was open: an invisible menu would still
        /// take pokes and claim fingertips.</summary>
        public void RestoreAfterPlacing()
        {
            if (_app == null) return;
            for (int i = 0; i < _rigs.Count; i++)
            {
                Rig r = _rigs[i];
                if (r.Id == RigId.Lessons || r.Group == null || !r.InScene) continue;
                if (!r.Group.gameObject.activeSelf) r.Group.gameObject.SetActive(true);
            }
            ShowMenuAfterPlacing();
            _app.Input.ResetMotion();
        }

        // ---------------------------------------------------------------- targets and guards

        /// <summary>The menu panel while it is shown and its zone holds the point; else
        /// the rig whose nearest grab handle (a handle's oriented box) is within reach; ties within 0.1 mm go to the smaller
        /// zone, so a pinch on the harp's pillar never goes to the side table under it.</summary>
        public Rig GrabTarget(Vector3 point, float reach = GrabConstants.HandleReach) => GrabTarget(point, reach, out _);

        public Rig GrabTarget(Vector3 point, float reach, out float distance)
        {
            distance = float.PositiveInfinity;
            if (_app == null) return null;
            IMenuPanel menu = Menu;
            if (menu != null && menu.Shown)
            {
                Rig menuRig = Get(RigId.Lessons) ?? menu.Rig;
                Bounds? z = menu.GrabZone;
                if (menuRig != null && z.HasValue && z.Value.Contains(point)) { distance = 0f; return menuRig; }
            }
            Rig best = null;
            float bestD = float.PositiveInfinity, bestVol = 0f;
            for (int i = 0; i < Placement.Entries.Count; i++)
            {
                RigEntry e = Placement.Entries[i];
                float d = HandleDistance(e, point);
                if (d > reach) continue;
                float vol = 0f;
                if (e.HasZone) { Vector3 s = e.Zone.size; vol = s.x * s.y * s.z; }
                if (best == null || d < bestD - 1e-4f || (Mathf.Abs(d - bestD) <= 1e-4f && vol < bestVol))
                {
                    best = e.Rig; bestD = d; bestVol = vol;
                }
            }
            distance = bestD;
            return best;
        }

        /// <summary>The distance to the nearest shown grab handle of a rig in the scene.</summary>
        float HandleDistance(RigEntry e, Vector3 p)
        {
            if (!PlacementSolver.InScene(e)) return float.PositiveInfinity;
            float d = float.PositiveInfinity;
            for (int i = 0; i < e.Handles.Count; i++)
            {
                PartRef part = e.Handles[i];
                if (!Placement.Shows(part.R) || !PlacementSolver.GeometryBox(part, out Bounds b, out Matrix4x4 tw)) continue;
                d = Mathf.Min(d, Obb.FromBounds(b, tw).DistanceTo(p));
            }
            return d;
        }

        /// <summary>The keys, a drum head, cymbal, the kick pad or the hat stand, the strings, the
        /// deck's buttons or a switch lever. A pinch there is part of playing and never a grab. The menu is not a play
        /// surface here (it is grabbed by its zone).</summary>
        public PlaySurface PlaySurfaceAt(Vector3 at)
        {
            IReadOnlyList<IInstrument> all = _app.Instruments;
            for (int i = 0; i < all.Count; i++)
            {
                IInstrument inst = all[i];
                if (inst == null || inst.Kind == InstrumentKind.Menu) continue;
                Transform root = inst.Root;
                if (root == null || !root.gameObject.activeInHierarchy) continue;
                PlaySurface s = inst.PlaySurfaceAt(at);
                if (s != PlaySurface.None) return s;
            }
            return PlaySurface.None;
        }

        bool PianoVisible
        {
            get { Rig p = Get(RigId.Piano); return p != null && p.InScene && p.Group != null && p.Group.gameObject.activeInHierarchy; }
        }

        bool PianoInPlayZone(Vector3 at)
        {
            if (!PianoVisible) return false;
            IPianoApi p = PianoApi;
            return p != null && p.InPlayZone(at);
        }

        /// <summary>The space the hands play the piano in (its play zone above the bottom of the keys):
        /// a pinch there never takes the piano; below it, beside the keys and behind them it does.</summary>
        bool PianoKeySpace(Vector3 at)
        {
            if (!PianoInPlayZone(at)) return false;
            IPianoApi p = PianoApi;
            if (p.Root != null) return p.InKeySpace(at);   // IPianoApi.InKeySpace: the piano's own rule
            return Get(RigId.Piano).Group.InverseTransformPoint(at).y > -0.05f;
        }

        /// <summary>The rig a pinch here would take, if it may (drives the dwell ring).</summary>
        public Rig GrabCandidate(Vector3 at)
        {
            if (PlaySurfaceAt(at) != PlaySurface.None) return null;
            Rig rig = GrabTarget(at);
            if (PianoInPlayZone(at) && (rig == null || (rig.Id == RigId.Piano && PianoKeySpace(at)))) return null;
            return Grabbable(rig) && InPlayGrabAllows(rig) ? rig : null;
        }

        float HeadLocalX(Vector3 world) => _app.Head.InverseTransformPoint(world).x;

        // ---------------------------------------------------------------- the per-frame guards

        void TickGrab()
        {
            // A grabbing hand that stops being tracked lets go of the rig (so does a rig whose module destroyed it).
            if (_held != null && (_held.Group == null || (_hand != null && !_hand.Connected))) EndGrab();
            if (_held != null && _hand2 != null && !_hand2.Connected) DropSecondHand();
            if (_turner != null && !_turner.Connected) _turner = null;
            HandState[] hands = _app.Input.Hands;
            // Desktop (not presenting): Shift / Alt + drag moves or turns a rig with the mouse (LayoutModule.Desktop.cs).
            bool presenting = Presenting;
            if (!presenting) TickDesktop();
            // The hands grab only in XR, and not while (or in the frame) the piano is being placed in MR
            // (MR clears IsPlacing in its EndFrame of the landing frame, so that frame's pass is skipped too).
            if (!presenting || MrPlacing)
            {
                // A hand's grab ends when placing starts or the session stops presenting (the desktop mouse drag has no
                // hand and is TickDesktop's).
                if (_held != null && (MrPlacing || _hand != null)) EndGrab();
                ShowHint(null, 0f);
                SnapshotHolding(hands);
                return;
            }
            Rig pending = null;
            float pendingK = 0f;
            PointerInput input = _app.Input;
            bool tools = DrumsApi != null || ToolProviderCount() > 0;
            for (int i = 0; i < hands.Length; i++)
            {
                HandState h = hands[i];
                if (!h.Connected) continue;
                // A pinch the ray summon took (RaySummon) is never a grab, a stick pick-up or MR placing.
                if (Summon != null && Summon.Owns(i)) { _armed[i] = false; _grabRig[i] = null; continue; }
                Pointer p = h.Pointers[0];
                // A hand that took a stick since the last Grab pass by some other path is not starting a grab.
                if (h.Holding != null && !_holdingLast[i] && h != _hand && h != _hand2) { _armed[i] = false; continue; }
                // Tools first, for any free hand, whether or not the other hand holds a rig: a pinch,
                // or a closing fist once the hand stopped playing, near a free tool picks it up (the nearest of every
                // IHandToolProvider; the kit's sticks through IDrumsApi.TryPickUp while no provider owns the kit); a pinch
                // that just misses a tool does nothing unless it is right on a rig's handle (2 cm).
                // Every grab test below reads the grab pinch (HandState.GrabPinch*: the thumb-index rule OR Meta's own index
                // pinch). On the headset the thumb-index rule alone flickers around its 15 / 25 mm thresholds (the two tip
                // joints sit ~17 mm apart when the fingertips touch), and a flicker restarted the dwell every time.
                // A pinch that starts like the OS system gesture (palm toward the headset) picks up no stick either.
                bool stickPinch = h.GrabPinchStart && !input.SystemGestureLike(h, _app.Head.position, _app.Head.forward);
                if (tools && (stickPinch || (h.GraspStart && !IsPlaying(h))) && p.Active && h != _hand && h != _hand2 && h.Holding == null)
                {
                    StickPick took = PickUpTool(h, p.Tip);
                    if (took == StickPick.Grabbed || (took == StickPick.Near && GrabTarget(p.Tip, StickMissReach) == null))
                    {
                        _armed[i] = false;
                        _grabRig[i] = null;
                        continue;
                    }
                }
                if (h.GrabPinchStart)
                {
                    // A pinch that starts with the palm toward the headset is the OS system gesture: never a grab.
                    _armed[i] = !IsPlaying(h) && !input.SystemGestureLike(h, _app.Head.position, _app.Head.forward);
                    bool has = input.TryPinchPoint(h, out Vector3 at0);
                    if (has) _dwell[i].Start(at0, h.GrabPinchFor);
                    else _armed[i] = false;
                    _grabRig[i] = _armed[i] && has && h.Holding == null ? GrabCandidate(at0) : null;
                }
                if (!h.GrabPinching || IsPlaying(h)) _armed[i] = false;
                if (_armed[i] && h.GrabPinchFor < GrabConstants.GrabDwell)
                {
                    if (!input.TryPinchPoint(h, out Vector3 at1) || _dwell[i].Sample(at1, h.GrabPinchFor) > GrabConstants.GrabStill) _armed[i] = false;
                    else if (_held == null && _grabRig[i] != null && h.GrabPinchFor >= GrabConstants.HintAfter && pending == null)
                    {
                        pending = _grabRig[i];
                        pendingK = h.GrabPinchFor / GrabConstants.GrabDwell;
                    }
                }
                // The dwell is complete: one shot.
                if (_held == null && _armed[i] && h.GrabPinchFor >= GrabConstants.GrabDwell)
                {
                    _armed[i] = false;
                    if (!input.TryPinchPoint(h, out Vector3 at) || _dwell[i].Sample(at, h.GrabPinchFor) > GrabConstants.GrabStill || _dwell[i].Speed() > GrabConstants.GrabSpeed) continue;
                    if (PlaySurfaceAt(at) != PlaySurface.None) continue;
                    Rig rig = h.Holding != null ? null : GrabTarget(at);
                    if (PianoInPlayZone(at) && (rig == null || (rig.Id == RigId.Piano && PianoKeySpace(at)))) continue;
                    if (rig != null && !Grabbable(rig)) continue;
                    if (rig != null && !InPlayGrabAllows(rig))
                    {
                        // Move instruments is off: nothing moves; Sunny may say where to turn it on
                        BodyPinches++;
                        _app.Events.Publish(new InstrumentBodyPinchEvent { Kind = KindOf(rig.Id) });
                        continue;
                    }
                    if (rig != null) StartGrab(rig, at, h, _wristYaw[i].Read(input.Frame(h), reset: true));
                    else if (rig == null) EmptyAirPinches++;   // empty air: nothing moves (no MR re-place, no head-aimed reticle)
                    continue;
                }
                if (_held == null) continue;
                // A second hand pinching the held rig joins for a two-hand twist; pinching the air beside it, it turns the
                // rig in 45 degree steps with a sideways pull. (Not while the desktop mouse holds it: there is no first hand.)
                if (_hand != null && h != _hand && _hand2 == null && h != _turner && h.GrabPinchStart && p.Active && !IsPlaying(h))
                {
                    Bounds? z = ZoneOf(_held);
                    if (z.HasValue && z.Value.Contains(p.Tip)) { _hand2 = h; StartTwoHand(_hand.Pointers[0].Tip, p.Tip); }
                    else { _turner = h; _turnX = HeadLocalX(p.Tip); }
                    continue;
                }
                if (h == _turner)
                {
                    if (!h.GrabPinching || !p.Active) _turner = null;
                    else
                    {
                        float dx = HeadLocalX(p.Tip) - _turnX;
                        if (Mathf.Abs(dx) > GrabConstants.OffHandTurnPull)
                        {
                            // A pull to the right turns the rig clockwise seen from above (source frame: -45 degrees; Unity yaw +45).
                            StepTurnHeld((dx > 0f ? 1f : -1f) * GrabConstants.OffHandTurnDeg * Mathf.Deg2Rad);
                            _turnX = HeadLocalX(p.Tip);
                        }
                    }
                    continue;
                }
                if (h == _hand2)
                {
                    if (!h.GrabPinching || !p.Active) DropSecondHand();
                    continue;
                }
                if (h != _hand) continue;
                if (!h.GrabPinching || !p.Active) EndGrab();
                else if (_hand2 != null) MoveTwoHand(p.Tip, _hand2.Pointers[0].Tip);
                else MoveGrab(p.Tip, _wristYaw[i].Read(input.Frame(h)));
            }
            ShowHint(pending, pendingK);
            SnapshotHolding(hands);
        }

        void SnapshotHolding(HandState[] hands)
        {
            for (int i = 0; i < hands.Length && i < _holdingLast.Length; i++) _holdingLast[i] = hands[i].Holding != null;
        }

        // ---------------------------------------------------------------- holding

        float HomeY(Rig r)
        {
            int id = (int)r.Id;
            return id >= 0 && id < _homeY.Length && !float.IsNaN(_homeY[id]) ? _homeY[id] : r.Group.position.y;
        }

        void StartGrab(Rig rig, Vector3 at, HandState h, float handYaw)
        {
            _held = rig;
            _hand = h;
            _hand2 = null;
            _turner = null;
            Transform g = rig.Group;
            _y0 = g.position.y;
            _pointY0 = at.y;
            _lifting = false;
            int id = (int)rig.Id;
            if (id >= 0 && id < _homeY.Length && float.IsNaN(_homeY[id])) _homeY[id] = g.position.y;
            _yaw0 = _curYaw = LayoutMath.YawOf(g);
            _turn = 0f;
            _yawPrev = handYaw;
            _blocked = false;
            _pass = false;
            rig.Held = true;
            // Re-measure every rig (a stick may have been picked up since the last placement).
            CollectAll();
            Placement.MeasureAll();
            RigEntry e = Placement.Entry(rig);
            // The turning centre: the middle of the rig's footprint (the menu panel turns about itself).
            _pivot = e != null && e.HasBound ? new Vector3(e.Bound.X, 0f, e.Bound.Z) : Vector3.zero;
            Vector3 pw = PivotWorld(g);
            _pivotOff = new Vector3(pw.x - at.x, 0f, pw.z - at.z);
            Visuals.SetHint(null, Vector3.zero, 1f, 0f);
            PlaceRing();
        }

        /// <summary>Where the footprint centre is in the world.</summary>
        Vector3 PivotWorld(Transform g)
        {
            float k = g.localScale.x;
            LayoutMath.RotY(_pivot.x * k, _pivot.z * k, _curYaw, out float rx, out float rz);
            Vector3 p = g.position;
            return new Vector3(p.x + rx, 0f, p.z + rz);
        }

        /// <summary>The rig posed so that its footprint centre lands on (x, z) at heading yaw.</summary>
        void PlacePivotAt(Transform g, float x, float z, float yaw)
        {
            float k = g.localScale.x;
            LayoutMath.RotY(_pivot.x * k, _pivot.z * k, yaw, out float rx, out float rz);
            g.SetPositionAndRotation(new Vector3(x - rx, g.position.y, z - rz), LayoutMath.YawRotation(yaw));
            _curYaw = yaw;
        }

        /// <summary>Vertical follow starts only once the hand has clearly moved up or down (a level drag stays level); then
        /// the rig tracks the hand's height within LIFT of its height at the first grab.</summary>
        void ApplyLift(float pointY)
        {
            if (_arranging) return;   // Move instruments lifts with its own filter and ranges (LayoutModule.Move StepLift)
            if (!_lifting && Mathf.Abs(pointY - _pointY0) > GrabConstants.LiftDeadband) _lifting = true;
            if (!_lifting) return;
            Transform g = _held.Group;
            float home = HomeY(_held);
            Vector3 p = g.position;
            g.position = new Vector3(p.x, Mathf.Clamp(_y0 + (pointY - _pointY0), home + GrabConstants.LiftMin, home + GrabConstants.LiftMax), p.z);
        }

        /// <summary>In the VR room the rig's origin stays inside x +-2.8, z -3.6..0.8.</summary>
        void ClampRoom(Transform g)
        {
            if (MrActive) return;
            Rect c = GrabConstants.OriginClamp;
            Vector3 p = g.position;
            g.position = new Vector3(Mathf.Clamp(p.x, c.xMin, c.xMax), p.y, Mathf.Clamp(p.z, c.yMin, c.yMax));
        }

        /// <summary>Keep the rig where the hand puts it unless that runs it into another rig; then it
        /// stops at contact (and slides) until the hand has pushed for UNSTICK_MS, when it goes on through.</summary>
        void ConstrainGrab(float px, float pz, float pyaw)
        {
            RigEntry e = Placement.Entry(_held);
            if (e == null) return;   // the menu panel has no collision constraint
            Transform g = _held.Group;
            float tx = g.position.x, tz = g.position.z, tyaw = _curYaw;
            if (_pass)
            {
                // Through it: free, until it is out of everything again.
                if (!Placement.TouchingAny(e)) _pass = false;
                _blocked = false;
                return;
            }
            Placement.Constrain(e, px, pz, pyaw, tx, tz, tyaw, out float yaw);
            _curYaw = yaw;
            Vector3 q = g.position;
            float lag = Mathf.Sqrt((q.x - tx) * (q.x - tx) + (q.z - tz) * (q.z - tz));
            float ylag = Mathf.Abs(LayoutMath.Wrap(_curYaw - tyaw));
            if (lag < 0.02f && ylag < 0.05f) { _blocked = false; return; }
            double now = _app.Clock.NowMs;
            if (!_blocked) { _blocked = true; _blockedAt = now; }
            else if (now - _blockedAt >= GrabConstants.UnstickMs)
            {
                _pass = true;
                _blocked = false;
                g.SetPositionAndRotation(new Vector3(tx, q.y, tz), LayoutMath.YawRotation(tyaw));
                _curYaw = tyaw;
            }
        }

        /// <summary><paramref name="wrist"/> false is the desktop mouse (no hand
        /// heading): the rig only slides.</summary>
        void MoveGrab(Vector3 point, float handYaw, bool wrist = true) => MoveGrabAt(point.x, point.z, point.y, handYaw, wrist);

        /// <summary>The drag itself: the footprint centre follows (x, z) + the grab offset, the height follows
        /// <paramref name="liftY"/> (a ray grab's far point carries x and z while the hand itself carries the height).</summary>
        void MoveGrabAt(float x, float z, float liftY, float handYaw, bool wrist)
        {
            Transform g = _held.Group;
            float px = g.position.x, pz = g.position.z, pyaw = _curYaw;
            // Turning the wrist turns the rig about its footprint's centre, frame by frame, unwrapped: no limit.
            if (wrist)
            {
                _turn += LayoutMath.Wrap(handYaw - _yawPrev);
                _yawPrev = handYaw;
            }
            PlacePivotAt(g, x + _pivotOff.x, z + _pivotOff.z, _yaw0 + _turn);
            ApplyLift(liftY);
            ClampRoom(g);
            ConstrainGrab(px, pz, pyaw);
            _turn = _curYaw - _yaw0;   // a refused turn slips: the wrist carries on from the angle the rig is at
            PlaceRing();
            RefreshHeldZone();
        }

        void StartTwoHand(Vector3 pA, Vector3 pB)
        {
            Transform g = _held.Group;
            _pairPrev = Mathf.Atan2(pB.x - pA.x, pB.z - pA.z);
            _pairTurn = 0f;
            _pairYaw0 = _curYaw;
            Vector3 mid = (pA + pB) * 0.5f;
            Vector3 pw = PivotWorld(g);
            _pivotOff = new Vector3(pw.x - mid.x, 0f, pw.z - mid.z);
            _y0 = g.position.y;
            _pointY0 = mid.y;
            _lifting = false;
        }

        void MoveTwoHand(Vector3 pA, Vector3 pB)
        {
            Transform g = _held.Group;
            float px = g.position.x, pz = g.position.z, pyaw = _curYaw;
            // The hands' heading, unwrapped frame by frame: a twist goes past half a turn and on.
            float a = Mathf.Atan2(pB.x - pA.x, pB.z - pA.z);
            _pairTurn += LayoutMath.Wrap(a - _pairPrev);
            _pairPrev = a;
            Vector3 mid = (pA + pB) * 0.5f;
            PlacePivotAt(g, mid.x + _pivotOff.x, mid.z + _pivotOff.z, _pairYaw0 + _pairTurn);
            ApplyLift(mid.y);
            ClampRoom(g);
            ConstrainGrab(px, pz, pyaw);
            _pairTurn = _curYaw - _pairYaw0;
            PlaceRing();
            RefreshHeldZone();
        }

        /// <summary>The second hand lets go (or drops out): the rig goes back to the first hand cleanly.</summary>
        void DropSecondHand()
        {
            if (_held == null || _hand2 == null) return;
            _hand2 = null;
            Vector3 a = GrabPoint(_hand);   // pointer 0 in play, the pinch point in arrange mode
            Transform g = _held.Group;
            Vector3 pw = PivotWorld(g);
            _pivotOff = new Vector3(pw.x - a.x, 0f, pw.z - a.z);
            _yaw0 = _curYaw;
            _turn = 0f;
            _yawPrev = _wristYaw[_hand.Index].Read(_app.Input.Frame(_hand), reset: true);
            _y0 = g.position.y;
            _pointY0 = a.y;
            _lifting = false;
            if (_moveDrag) ReanchorMoveDrag(_rayGrab ? _rayTarget : a, _yawPrev, a.y);   // the filtered drag carries on from here
        }

        /// <summary>The other hand's sideways pull, a 45 degree step about the footprint's centre;
        /// a step into contact is refused. Returns the angle actually turned.</summary>
        float StepTurnHeld(float rad)
        {
            Transform g = _held.Group;
            float px = g.position.x, pz = g.position.z, pyaw = _curYaw;
            Vector3 pw = PivotWorld(g);
            PlacePivotAt(g, pw.x, pw.z, pyaw + rad);
            RigEntry e = Placement.Entry(_held);
            if (e != null)
            {
                Placement.Constrain(e, px, pz, pyaw, g.position.x, g.position.z, _curYaw, out float yaw);
                _curYaw = yaw;
            }
            float did = _curYaw - pyaw;
            _yaw0 += did;
            _pairYaw0 += did;
            PlaceRing();
            RefreshHeldZone();
            return did;
        }

        /// <summary>Let go inside another instrument, in the player's face or in a wall, it alone moves
        /// out; the menu let go inside an instrument comes out of it. In Move instruments the drop is kept (relative to the
        /// default pose in front of the player) and a hand's drop eases to its free spot (StartSettle).</summary>
        public void EndGrab()
        {
            if (_held == null) return;
            Rig rig = _held;
            _held = null;
            bool drag = _moveDrag;
            _moveDrag = false;
            _dragPaused = false;
            Vector3 from = rig.Group != null ? rig.Group.position : Vector3.zero;
            Quaternion fromRot = rig.Group != null ? rig.Group.rotation : Quaternion.identity;
            if (_app?.Head != null && rig.Group != null)
            {
                SyncPlacementFlags();
                if (rig.Id != RigId.Lessons)
                {
                    RigEntry e = Placement.Entry(rig);
                    if (e != null)
                    {
                        ReadSeat(out Vector3 head, out float yaw);
                        // In Move instruments a drop on the player's own seat moves off it too.
                        if (_arranging) Placement.SettleOne(e, head, yaw, offSeat: true);
                        else Placement.SettleOne(e, head, yaw);
                        RefreshZone(e);
                    }
                }
                else Placement.ClearPanel(_app.Head.position);
            }
            rig.Held = false;
            _hand = _hand2 = _turner = null;
            _mouseMode = MouseGrab.None;
            _pass = false;
            _blocked = false;
            _rayGrab = false;
            Visuals?.SetRing(false, Vector3.zero, 1f, false);
            Visuals?.SetCage(false, null, Vector3.zero, Vector3.one, 0f, false);
            // "Move instruments": every release is kept (relative to the seat) for the next arrangement of this layout.
            if (_arranging && rig.Id != RigId.Lessons && _app != null)
            {
                _movedByPlayer.Add(rig.Id);   // a deliberate move: only these are saved (SaveArrangement)
                SaveArrangement();
                RigEntry m = Placement.Entry(rig);
                if (m != null && PlacementSolver.InScene(m)) Placement.Measure(m);
            }
            if (drag && rig.Id != RigId.Lessons) StartSettle(rig, from, fromRot);
        }

        void RefreshHeldZone()
        {
            RigEntry e = Placement.Entry(_held);
            if (e != null) RefreshZone(e);
        }

        // ---------------------------------------------------------------- feedback

        /// <summary>The floor ring for a rig, at its footprint's centre and sized to it.</summary>
        void RingOf(Rig r, out Vector3 centre, out float scale)
        {
            RigEntry e = Placement.Entry(r);
            if (e != null && e.HasBound)
            {
                Placement.Pose(e);
                centre = new Vector3(e.WB.X, 0f, e.WB.Z);
                scale = Mathf.Clamp(Mathf.Sqrt(e.WB.Hu * e.WB.Hu + e.WB.Hv * e.WB.Hv) * 0.85f, 0.3f, 1.1f) / GrabVisuals.RingOuter;
            }
            else
            {
                Vector3 p = r.Group != null ? r.Group.position : Vector3.zero;
                centre = new Vector3(p.x, 0f, p.z);
                scale = 0.6f;
            }
        }

        void PlaceRing()
        {
            RingOf(_held, out Vector3 c, out float s);
            Visuals.SetRing(true, c, s, _pass);
            // In arrange mode the instrument's own corner brackets (gold, red while pushed through) replace the cage.
            if (_held.Id != RigId.Lessons && !_arranging) FitCage(_held, _pass ? 0.45f : 0.3f, _pass);
        }

        /// <summary>The thin ring filling in round the rig a pinch is about to take.</summary>
        void ShowHint(Rig pending, float k)
        {
            if (pending == null)
            {
                Visuals.SetHint(null, Vector3.zero, 1f, 0f);
                if (_held == null) Visuals.SetCage(false, null, Vector3.zero, Vector3.one, 0f, false);
                return;
            }
            RingOf(pending, out Vector3 c, out float s);
            Visuals.SetHint(pending, c, s, k);
            if (pending.Id != RigId.Lessons) FitCage(pending, 0.12f + 0.4f * Mathf.Clamp01(k), false);
        }

        /// <summary>The box round the rig's parts in its own frame, + 0.02.</summary>
        void FitCage(Rig r, float opacity, bool red)
        {
            RigEntry e = Placement.Entry(r);
            if (e == null || e.Local.Count == 0 || r.Group == null)
            {
                Visuals.SetCage(false, null, Vector3.zero, Vector3.one, 0f, false);
                return;
            }
            Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
            for (int i = 0; i < e.Local.Count; i++)
            {
                e.Local[i].Corners(_cageCorners);
                for (int k = 0; k < 8; k++) { mn = Vector3.Min(mn, _cageCorners[k]); mx = Vector3.Max(mx, _cageCorners[k]); }
            }
            Vector3 pad = Vector3.one * 0.02f;
            mn -= pad;
            mx += pad;
            Visuals.SetCage(true, r.Group, (mn + mx) * 0.5f, mx - mn, opacity, red);
        }
    }
}
