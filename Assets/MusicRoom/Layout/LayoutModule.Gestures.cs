// The seat gestures (Docs/08-ux-lessons-menu-guide.md 5.4) and the page-turn sweep hook-up (5.2):
//   swivel     VR room only: a pinch in empty air pulled sideways 14 cm snap-turns the seat 45 degrees about the head
//              (pulling right turns the view left, like dragging the world);
//   bring back "Reset view" by hand: both hands pinched in empty air and held still for 1 s bring every instrument back
//              in front of the player at the player's level for the eyes now (LayoutModule.Height; the same path as the
//              menu's Reset view row, ILayoutService.BringToPlayer); a ring
//              round the hands fills during the hold, so it is never a surprise. Move instruments on or off alike; never
//              while a hand plays, is on an instrument or the menu, holds or points at one;
//   sweep      single mode: a fast sideways sweep of an open, vertical palm turns the page.
// "Empty air" is outside every zone: the placed rigs' zones, their modules' play zones and the shown menu's grab zone.
// Nothing here reads where the head looks to move anything (2026-11-10 headset test: "turning the head should not move
// instruments"): the sweep is measured in the seat's frame (the heading the instruments were laid out along), not the
// head's, so a turn of the head past a still, open hand is never a sweep; a recentre places about the tracking origin the
// system just reset (BringBack), not about wherever the head happens to look in the frames after it.
using MusicRoom.Core;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.Layout
{
    /// <summary>Who asked for a reset view (LayoutModule.BringBack).</summary>
    public enum BringBackSource : byte { Menu, Gesture, Recentre }

    public sealed partial class LayoutModule
    {
        HandState _swivelHand;
        float _swivelStartX;
        float _summonT;
        bool _summonDone;
        readonly SweepHand[] _sweepHands = new SweepHand[2];
        readonly Vector3[] _bringFrom = new Vector3[2], _bringAt = new Vector3[2];

        /// <summary>The hand that holds the swivel (null when none).</summary>
        public HandState SwivelHand => _swivelHand;
        /// <summary>Snap turns the swivel made since boot.</summary>
        public int SnapTurns { get; private set; }
        /// <summary>Two-hand bring-backs (the held pinch) that fired since boot.</summary>
        public int Summons { get; private set; }
        public float SummonHeld => _summonT;
        /// <summary>Reset views since boot (menu, both hands, recentre) and the last one's source.</summary>
        public int BringBacks { get; private set; }
        public BringBackSource LastBringBack { get; private set; }

        /// <summary>The swivel: a pinch start with a tracked index tip, no play by either hand for a second, no rig held and
        /// outside every zone takes the swivel; the tip's head-local x is tracked and every 0.14 m snaps 45 degrees. It ends
        /// when the pinch parts or on tracking loss. The index tip is used directly so a hand holding sticks can still swivel.
        /// The pinch is the grab pinch (HandState.GrabPinch*: the thumb-index rule OR Meta's own index pinch): on the headset
        /// the thumb-index rule alone flickers (the two tip joints sit ~17 mm apart when the fingertips touch, above its 15 mm
        /// start), and a flicker ended the swivel. A pinch that starts like the OS system gesture (palm toward the headset)
        /// never takes it.</summary>
        void TickSwivel()
        {
            if (!Presenting || MrActive || _app.Head == null) { _swivelHand = null; return; }
            HandState[] hands = _app.Input.Hands;
            for (int i = 0; i < hands.Length; i++)
            {
                HandState h = hands[i];
                if (!h.Connected || (Summon != null && Summon.Owns(i))) { if (_swivelHand == h) _swivelHand = null; continue; }
                bool tracked = _app.Input.TryIndexTip(h, out Vector3 tip);
                if (_swivelHand == null && _held == null && h.GrabPinchStart && tracked && !AnyHandPlaying(GrabConstants.PlayGuardMs) && !NearInstrument(tip)
                    && !_app.Input.SystemGestureLike(h, _app.Head.position, _app.Head.forward))
                {
                    _swivelHand = h;
                    _swivelStartX = HeadLocalX(tip);
                }
                if (_swivelHand != h) continue;
                if (!h.GrabPinching || !tracked) { _swivelHand = null; continue; }
                float dx = HeadLocalX(tip) - _swivelStartX;
                if (Mathf.Abs(dx) > GrabConstants.SwivelPull)
                {
                    SnapTurn(dx > 0f ? -GrabConstants.SwivelDeg : GrabConstants.SwivelDeg);
                    _swivelStartX = HeadLocalX(tip);
                }
            }
        }

        /// <summary>The rig turns about the head; the hands just jumped with it, so their motion is
        /// forgotten (the jump can never read as a stroke). Positive degrees: the player looks further right.</summary>
        void SnapTurn(float degrees)
        {
            XrMode.SnapTurn(_app.Rig, degrees);
            SnapTurns++;
            // The seat frame turns with the player (about the head), so a placement saved afterwards stays relative to
            // the way they sit, not to the heading before the turn.
            _seatYaw = LayoutMath.Wrap(_seatYaw + degrees * Mathf.Deg2Rad);
            _app.Input.ResetMotion();
        }

        /// <summary>Reset view by hand (the two-hand hold): in the headset with no rig held or dragged (in MR not while
        /// placing), two hands each pinching in empty air (outside every zone, the menu's too), holding no stick, not playing,
        /// not the OS system gesture, and in Move instruments neither near nor pointing at an instrument; both pinches held
        /// within BringBackStill of where the hold began for TwoHandSummonSeconds: once, BringBack(Gesture). It re-arms
        /// when fewer than two hands count; a hand that wanders starts the hold again. Works with Move instruments on or off,
        /// in the VR room and in mixed reality, whatever instrument is out. Allocation-free.</summary>
        void TickSummon(float dt)
        {
            int both = 0;
            if (Presenting && _app.Head != null && _held == null && _displayRig == null && !MrPlacing)
            {
                HandState[] hands = _app.Input.Hands;
                for (int i = 0; i < hands.Length && i < 2; i++)
                    if (BringHand(hands[i], i, out _bringAt[i])) both++;
            }
            if (both < 2)
            {
                _summonT = 0f;
                _summonDone = false;
                Visuals?.SetBringRing(false, Vector3.zero, Vector3.zero, Vector3.zero, 0f);
                return;
            }
            if (_summonT <= 0f || (_bringAt[0] - _bringFrom[0]).sqrMagnitude > GrabConstants.BringBackStill * GrabConstants.BringBackStill
                || (_bringAt[1] - _bringFrom[1]).sqrMagnitude > GrabConstants.BringBackStill * GrabConstants.BringBackStill)
            {
                // the hold starts (again) here
                _bringFrom[0] = _bringAt[0];
                _bringFrom[1] = _bringAt[1];
                _summonT = 0f;
            }
            _summonT += dt;
            float k = Mathf.Clamp01(_summonT / GrabConstants.TwoHandSummonSeconds);
            bool ring = !_summonDone && _summonT >= GrabConstants.BringBackShowAfter;
            Visuals?.SetBringRing(ring, _bringAt[0], _bringAt[1], _app.Head.position, k);
            if (_summonT >= GrabConstants.TwoHandSummonSeconds && !_summonDone)
            {
                _summonDone = true;
                Summons++;
                Visuals?.SetBringRing(false, Vector3.zero, Vector3.zero, Vector3.zero, 0f);
                BringBack(BringBackSource.Gesture);
            }
        }

        /// <summary>One hand counts for the bring-back hold: its pinch point (where it is now) in <paramref name="at"/>.</summary>
        bool BringHand(HandState h, int i, out Vector3 at)
        {
            at = Vector3.zero;
            if (h == null || !h.Connected || !h.GrabPinching || h.Holding != null || IsPlaying(h) || (Summon != null && Summon.Owns(i))) return false;
            PointerInput input = _app.Input;
            if (!input.TryPinchPoint(h, out at)) return false;
            if (input.TryIndexTip(h, out Vector3 tip) && NearInstrument(tip)) return false;
            if (NearInstrument(at) || PlaySurfaceAt(at) != PlaySurface.None) return false;
            if (input.SystemGestureLike(h, _app.Head.position, _app.Head.forward)) return false;
            if (_arranging && i < _arr.Length && (_arr[i].Ignore || _arr[i].Candidate != null || _arr[i].Near != null || _arr[i].RayRig != null)) return false;
            return true;
        }

        /// <summary>A real recentre puts the tracking origin on the floor right under the head (the system's new front); the
        /// head is then within RecentreRadius of it.</summary>
        public const float RecentreRadius = 0.5f;

        bool RecentredUnderHead()
        {
            Transform space = _app.Rig != null ? (_app.Rig.TrackingSpace != null ? _app.Rig.TrackingSpace : _app.Rig.RigRoot) : null;
            if (space == null) return true;
            Vector3 d = _app.Head.position - space.position;
            d.y = 0f;
            return d.sqrMagnitude <= RecentreRadius * RecentreRadius;
        }

        /// <summary>ILayoutService.HandBusy: the hand plays (or played in the last second), holds a stick or a rig, its
        /// index tip is ON a play surface (keys, heads, strings, bars) or in the shown menu, the ray summon owns its pinch, or
        /// in Move instruments it is near or points at an instrument (that pinch picks it up). Its pinch is then for nothing
        /// else. Being merely inside an instrument's padded zone does not count (2026-11-11, found with the snowy window, since
        /// removed: in My set the kit, the harp and the piano's zones fill the space in front of the chest, so a pointing hand
        /// was always "on an instrument").</summary>
        public bool HandBusy(int hand)
        {
            if (_app == null) return false;
            HandState[] hands = _app.Input.Hands;
            if (hand < 0 || hand >= hands.Length) return false;
            HandState h = hands[hand];
            if (h == null || !h.Connected) return false;
            if (h == _hand || h == _hand2 || h == _displayHand || h.Holding != null || IsPlaying(h)) return true;
            if (Summon != null && Summon.Owns(hand)) return true;
            if (_app.Input.TryIndexTip(h, out Vector3 tip) && (PlaySurfaceAt(tip) != PlaySurface.None || InShownMenuZone(tip))) return true;
            return _arranging && hand < _arr.Length && (_arr[hand].Candidate != null || _arr[hand].Near != null || _arr[hand].RayRig != null);
        }

        /// <summary>Reset view (ILayoutService.BringToPlayer, the menu's row): every instrument back in front of the player.</summary>
        public void BringToPlayer() => BringBack(BringBackSource.Menu);

        /// <summary>The one path of every reset view: MR placing (if any) ends, then the layout is placed once in front of the
        /// player: the menu and the two-hand hold about the head as it is now (where the player sits and faces), a recentre
        /// about the tracking origin the system just reset (so the system's own bursts of recentre events, and the head moving
        /// between them, all land in the same place). Move instruments stays as it is; the player's own spots (comfort
        /// offsets) are kept. Publishes ArrangedEvent (Forced).</summary>
        /// <param name="headNow">A recentre laid out about the head's pose now rather than the origin (the deferred one on the
        /// return when the origin's heading is far from where the settled player faces: LayoutModule.Height).</param>
        void BringBack(BringBackSource source, bool headNow = false)
        {
            if (_app == null || _app.Head == null) return;
            if (source == BringBackSource.Recentre && !RecentredUnderHead())
            {
                // A recentre event that did not put the origin under the player (the system's count moved, the space did
                // not): the instruments stay where they are in the world, as everything else does.
                Debug.Log("[Layout] recentre event without a new origin under the player: nothing moves");
                return;
            }
            LeaveStage("reset view");   // a playback's instruments stay where Reset view puts them
            IMixedReality mr = Mr;
            if (mr != null && mr.Active && mr.IsPlacing) mr.CancelPlacing("reset view");
            _swivelHand = null;
            // The player's own Reset view: every instrument at the player's level for the eyes now (LayoutModule.Height);
            // a system recentre keeps the level (its eye change is handled by the return from the away).
            if (source != BringBackSource.Recentre) LiftForEyesNow(source);
            ArrangeAt(true, true, source == BringBackSource.Recentre && !headNow ? SeatSource.Origin : SeatSource.Head);   // the player's front from now on
            BringBacks++;
            LastBringBack = source;
            if (_arranging) { Placement.MeasureAll(); UpdateArrangeVisuals(); }
            Debug.Log($"[Layout] reset view ({source}): the instruments are in front of the player");
        }

        /// <summary>The page turn, updated with grabbing = a rig held, the swivel held, a bring-back hold, MR placing or either
        /// hand played in the last 400 ms; the palm is measured in the seat's frame (the layout's heading), not the head's; a turn goes to the
        /// next instrument (layout.next acts only in single mode with no turn running).</summary>
        void TickSweep(float dt)
        {
            if (!Presenting || _app.Head == null)
            {
                Sweep.ResetMotion();
                return;
            }
            PointerInput input = _app.Input;
            HandState[] hands = input.Hands;
            for (int i = 0; i < hands.Length && i < _sweepHands.Length; i++)
            {
                HandState h = hands[i];
                ref SweepHand s = ref _sweepHands[i];
                s.Connected = h.Connected;
                s.Pinching = h.Pinching;
                s.Holding = h.Holding != null;
                s.LastPlayMs = h.LastPlayMs;
                s.PalmOk = h.Connected && input.TryPalm(h, out s.PalmCentre, out s.PalmNormal);
                s.Open = h.Connected ? input.GraspAmount(h) : float.NaN;
            }
            // Either hand played within the sweep's guard: a hand sweeping while the other plays (a harp glissando, the
            // a rubbing hand) is playing, never turning the page.
            bool grabbing = _held != null || _swivelHand != null || MrPlacing || AnyHandPlaying(PageTurnGesture.PlayGuardMs) || _summonT > 0f;
            // The seat's frame, never the head's: a head turning past a still open hand is no sweep.
            Vector3 seatForward = new Vector3(Mathf.Sin(_seatYaw), 0f, Mathf.Cos(_seatYaw));
            int fired = Sweep.Update(dt, grabbing, _app.Clock.NowMs, _seatHead, seatForward, _sweepHands);
            if (fired != 0) Next(fired);
        }
    }
}
