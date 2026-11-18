// "When instrument changed or presses loop and changed the instruments appear here and there. it appears within one foot
// but sometimes left or right. make it appear at once place in front of eyes only" (headset test, 2026-11-17). With every
// real module (simulated headset, manual frames at 72 Hz), after a recentre at several headings, the player looking away
// and leaning (at the menu, the deck, Sunny) at the moment of each change:
//   - every instrument chosen on the menu (or by the ray, or by Sunny), brought by the deck's guide (Set, as DeckGuide does),
//     turned to by a page turn, or brought alone by a playback of a one-instrument recording lands with its play centre
//     (the piano's keys, the kit's pieces, the harp's middle string, the others' descriptor centre) within 2 cm of the
//     player's forward line (the recentre's seat and heading), facing the player, and every time at the same distance;
//   - the player's own layout comes back after that playback, in the same front spot;
//   - Move instruments turned on and off without moving anything saves no spot (only a deliberate move is kept);
//   - a player who walked away from their seat gets the next instrument in front of where they stand and face now.
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using MusicRoom.Tests.PlayMode.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Integration
{
    public class FrontSpotTests
    {
        const float Dt = 1f / 72f;
        /// <summary>The play centre's distance from the player's forward line (m).</summary>
        const float MaxLateral = 0.02f;
        /// <summary>The same instrument lands as far ahead every time (m).</summary>
        const float MaxDrift = 0.01f;

        TestBoot _boot;
        App _app;
        LayoutModule _layout;
        /// <summary>The seat the last recentre set (head on the floor, heading in degrees): the expected forward line.</summary>
        Vector3 _seatHead;
        float _seatYaw;
        readonly Dictionary<InstrumentKind, Vector2> _first = new Dictionary<InstrumentKind, Vector2>();
        int _looks;
        float _homeSide;

        IEnumerator BootApp()
        {
            _boot = new TestBoot(simulateHeadset: true, manualFrames: true);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, "single");
            _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, "piano");
            _boot.Hands.SetPose(HandSide.Right, Away(HandSide.Right));
            _boot.Hands.SetPose(HandSide.Left, Away(HandSide.Left));
            MenuBootGuard.Begin();
            yield return _boot.Start();
            MenuBootGuard.End();
            _app = _boot.App;
            _layout = _app.Get<ILayoutService>() as LayoutModule;
            Assert.That(_layout, Is.Not.Null);
            float t0 = Time.realtimeSinceStartup;
            while (_app.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 4f) yield return null;
            Step(3);
            Assert.IsTrue(_layout.FirstArrangeDone);
            Assert.That(_layout.Mode, Is.EqualTo(LayoutMode.Single));
            _first.Clear();
            _looks = 0;
        }

        [UnitySetUp] public IEnumerator SetUp() { yield return BootApp(); }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            _layout = null;
            _app = null;
        }

        static HandPose Away(HandSide side) =>
            HandPose.Open(side, Vector3.zero, Quaternion.identity).WithIndexTipAt(new Vector3(side == HandSide.Right ? 0.6f : -0.6f, 1.9f, -0.9f));

        void Step(int n = 1) => _boot.Step(Dt, n);

        void Settle()
        {
            for (int i = 0; i < 400 && (_layout.AnyMoving || _layout.Settling || _layout.IsTurning); i++) Step();
            Step(3);
        }

        Transform Head => _app.Head;

        Rig RigOf(InstrumentKind k) => _app.Get<IRigRegistry>().Get(InstrumentIds.RigOf(k));

        List<InstrumentKind> Kinds()
        {
            var list = new List<InstrumentKind>();
            IInstrumentRegistry reg = _app.Registry;
            for (int i = 0; i < reg.Carousel.Count; i++)
            {
                InstrumentKind k = reg.Carousel[i];
                InstrumentDescriptor d = reg.Get(k);
                if (d != null && d.Playable && reg.IsLoaded(k) && RigOf(k)?.Group != null) list.Add(k);
            }
            Assert.That(list.Count, Is.GreaterThanOrEqualTo(3), "the instruments of the build");
            return list;
        }

        /// <summary>The system recentred with the player's front at <paramref name="yawDeg"/> and the eyes at
        /// <paramref name="head"/>: the room is laid out once about it (fitSeat may move the seat off a wall).</summary>
        void Recentre(float yawDeg, Vector3 head)
        {
            Transform rig = _app.Rig.RigRoot;
            Quaternion rot = Quaternion.Euler(0f, yawDeg, 0f);
            rig.rotation = rot;
            rig.position = head - rot * DesktopRigFallback.EyePosition;
            Head.SetPositionAndRotation(head, rot);
            Step();
            Head.SetPositionAndRotation(head, rot);
            int backs = _layout.BringBacks;
            _app.Events.Publish(new RecenteredEvent());
            Assert.That(_layout.BringBacks, Is.EqualTo(backs + 1), "the recentre laid the room out");
            Assert.That(_layout.LastBringBack, Is.EqualTo(BringBackSource.Recentre));
            _seatHead = head + _layout.LastSeatShift;
            _seatYaw = yawDeg;
            _first.Clear();   // a new seat (its eyes' height sets how far the eye clearance keeps some of them)
            Settle();
            LookAhead();
        }

        // The head at the moment of a change: looking at the menu on the right, the deck down on the left, Sunny, the window;
        // leaning toward them (well inside FrontWalk).
        static readonly Vector3[] Leans = { new Vector3(0.18f, -0.05f, 0.08f), new Vector3(-0.22f, -0.12f, 0.02f), new Vector3(0.06f, -0.02f, 0.15f), new Vector3(-0.1f, 0f, -0.06f) };
        static readonly float[] Looks = { 65f, -105f, 25f, -40f };
        static readonly float[] Pitches = { 20f, 45f, -10f, 5f };

        void LookAway()
        {
            int k = _looks++ % Looks.Length;
            Quaternion seat = Quaternion.Euler(0f, _seatYaw, 0f);
            Head.SetPositionAndRotation(_seatHead + seat * Leans[k], Quaternion.Euler(Pitches[k], _seatYaw + Looks[k], 0f));
        }

        void LookAhead() => Head.SetPositionAndRotation(_seatHead, Quaternion.Euler(0f, _seatYaw, 0f));

        /// <summary>The change, made while the head looks away; the head comes back to the seat while it lands.</summary>
        void WhileLookingAway(System.Action change)
        {
            LookAway();
            change();
            Step();
            LookAway();
            Step();
            LookAhead();
            Settle();
        }

        /// <summary>An instrument's play centre in the seat frame (x to the right of the forward line, y ahead of the eyes) and
        /// how far its heading is from facing the player.</summary>
        Vector2 SpotOf(InstrumentKind k, out float turn)
        {
            Rig r = RigOf(k);
            RigEntry e = _layout.Placement.Entry(r);
            Assert.That(e, Is.Not.Null, k + " is placed");
            Assert.IsTrue(e.HasCentre, k + " has a play centre");
            _layout.Placement.Pose(e);
            Vector3 d = PlacementSolver.CentreWorld(e) - _seatHead;
            d.y = 0f;
            Vector3 s = Quaternion.Euler(0f, -_seatYaw, 0f) * d;
            turn = Mathf.DeltaAngle(r.Group.eulerAngles.y, _seatYaw + r.HomeYawSingle);
            // Where its single home alone put the play centre (before 2026-11-17), for the record.
            Vector3 c = Quaternion.Euler(0f, r.HomeYawSingle, 0f) * e.CentreLocal;
            _homeSide = r.HomeSingle.x + c.x;
            return new Vector2(s.x, s.z);
        }

        void AssertFront(InstrumentKind k, string how, float farther = 0f)
        {
            Assert.That(_layout.Mode, Is.EqualTo(LayoutMode.Single), how);
            Assert.That(_layout.Current, Is.EqualTo(k), how);
            Assert.IsFalse(_layout.AnyMoving, how + ": landed");
            Vector2 s = SpotOf(k, out float turn);
            Debug.Log($"[FrontSpot] seat {_seatYaw:F0} deg, {how}: {k} play centre {s.x * 100f:+0.0;-0.0} cm to the side, {s.y:F3} m ahead, turned {turn:+0.0;-0.0} deg (its single home alone: {_homeSide * 100f:+0.0;-0.0} cm)");
            Assert.That(Mathf.Abs(s.x), Is.LessThan(MaxLateral), $"{how}: {k}'s play centre is {s.x * 100f:F1} cm off the player's forward line");
            Assert.That(Mathf.Abs(turn), Is.LessThan(1f), $"{how}: {k} faces the player's front");
            Assert.That(s.y, Is.GreaterThan(0.05f), $"{how}: {k} in front of the eyes");
            if (_first.TryGetValue(k, out Vector2 f))
            {
                Assert.That(s.y, Is.EqualTo(f.y + farther).Within(farther > 0f ? 0.03f : MaxDrift), $"{how}: {k} as far ahead as every time before from this seat{(farther > 0f ? " (+ the show's StageFarther)" : "")}");
                Assert.That(s.x, Is.EqualTo(f.x).Within(MaxDrift), $"{how}: {k} no sideways drift from swap to swap");
            }
            else _first[k] = s;
        }

        static readonly float[] Yaws = { 0f, 35f, -60f, 120f };
        static readonly Vector3[] Seats = { new Vector3(0f, 1.2f, 0f), new Vector3(0.15f, 1.15f, 0.2f), new Vector3(-0.2f, 1.25f, -0.1f), new Vector3(0.1f, 1.2f, 0.1f) };

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator EveryInstrumentLandsAtOneSpotOnThePlayersForwardLine()
        {
            List<InstrumentKind> kinds = Kinds();
            BringReason[] reasons = { BringReason.Menu, BringReason.Ray, BringReason.Sunny };
            for (int y = 0; y < Yaws.Length; y++)
            {
                Recentre(Yaws[y], Seats[y]);
                AssertFront(_layout.Current, "after the recentre");
                // Chosen on the menu (the ray, Sunny, a look and pinch: the same Bring).
                for (int i = 0; i < kinds.Count; i++)
                {
                    InstrumentKind k = kinds[(i + y) % kinds.Count];
                    if (_layout.Current == k) continue;
                    bool brought = false;
                    WhileLookingAway(() => brought = _layout.Bring(k, reasons[i % reasons.Length]));
                    Assert.IsTrue(brought, k + " brought");
                    AssertFront(k, "chosen (" + reasons[i % reasons.Length] + ")");
                }
                // Brought by the deck's LOOP / Sunny's deck guide (DeckGuide.Bring: Set(One alone, kind)); fitSeat may move the seat.
                for (int i = kinds.Count - 1; i >= 0; i--)
                {
                    InstrumentKind k = kinds[i];
                    if (_layout.Current == k) continue;
                    WhileLookingAway(() => _layout.Set(LayoutMode.Single, k));
                    _seatHead += _layout.LastSeatShift;
                    AssertFront(k, "brought by the deck guide");
                }
                // A page turn.
                WhileLookingAway(() => _layout.Next(1));
                AssertFront(_layout.Current, "a page turn");
                // A playback of a one-instrument recording brings it alone; the player's own instrument comes back after it.
                InstrumentKind own = _layout.Current;
                InstrumentKind take = kinds[(kinds.IndexOf(own) + 2) % kinds.Count];
                WhileLookingAway(() => _app.Events.Publish(new RecordingPlaybackEvent { Started = true, Kinds = 1u << (int)take }));
                Assert.IsTrue(_layout.PlaybackStaged, "the playback brought its instrument");
                AssertFront(take, "a playback of " + take, LayoutModule.StageFarther);   // the show stands farther out (2026-11-18)
                _app.Events.Publish(new RecordingPlaybackEvent { Started = false, Kinds = 1u << (int)take });
                Step(Mathf.CeilToInt(1.3f / Dt));
                Settle();
                Assert.IsFalse(_layout.PlaybackStaged);
                AssertFront(own, "back after the playback");
            }
            Debug.Log($"[FrontSpot] {_first.Count} instruments, every landing on the forward line");
            yield return null;
        }

        [UnityTest]
        public IEnumerator MoveInstrumentsWithoutAMoveSavesNoSpot()
        {
            Recentre(20f, new Vector3(0.1f, 1.2f, 0f));
            List<InstrumentKind> kinds = Kinds();
            foreach (InstrumentKind k in kinds)
            {
                if (_layout.Current != k) { WhileLookingAway(() => _layout.Bring(k, BringReason.Menu)); }
                _layout.BeginArrange();
                Step(5);
                _layout.EndArrange();
                Step(2);
                foreach (InstrumentKind o in kinds) Assert.IsFalse(_layout.TryGetOffset(InstrumentIds.RigOf(o), out _), $"{o}: no spot saved by Move instruments on and off ({k} out)");
                Assert.IsFalse(_layout.TryGetOffset(RigId.Table, out _), "the side table: none either");
                Assert.That(_app.Settings.GetComfort(LayoutMode.Single, k), Is.EqualTo(""), k + ": nothing saved");
            }
            // And the next bring still lands on the line.
            InstrumentKind next = kinds[(kinds.IndexOf(_layout.Current) + 1) % kinds.Count];
            WhileLookingAway(() => _layout.Bring(next, BringReason.Menu));
            AssertFront(next, "after Move instruments on and off");
            yield return null;
        }

        [UnityTest]
        public IEnumerator APlayerWhoWalkedAwayGetsTheNextOneWhereTheyStandNow()
        {
            Recentre(0f, new Vector3(0f, 1.2f, 0f));
            List<InstrumentKind> kinds = Kinds();
            // Walked 0.9 m to the right in the room (the tracking space stays) and faces 50 degrees right.
            int reseats = _layout.FrontReseats;
            Vector3 walked = new Vector3(0.9f, 1.2f, 0.3f);
            Head.SetPositionAndRotation(walked, Quaternion.Euler(0f, 50f, 0f));
            InstrumentKind k = kinds[(kinds.IndexOf(_layout.Current) + 1) % kinds.Count];
            Assert.IsTrue(_layout.Bring(k, BringReason.Menu));
            Assert.That(_layout.FrontReseats, Is.EqualTo(reseats + 1), "a new front");
            _seatHead = walked;
            _seatYaw = 50f;
            for (int i = 0; i < 400 && (_layout.AnyMoving || _layout.IsTurning); i++) { Head.SetPositionAndRotation(walked, Quaternion.Euler(0f, 50f, 0f)); Step(); }
            AssertFront(k, "after walking away");
            // From there, looking about, the next one lands in the same front spot.
            InstrumentKind n = kinds[(kinds.IndexOf(k) + 1) % kinds.Count];
            WhileLookingAway(() => _layout.Bring(n, BringReason.Menu));
            Assert.That(_layout.FrontReseats, Is.EqualTo(reseats + 1), "leaning is not walking");
            AssertFront(n, "the next one");
            yield return null;
        }
    }
}
