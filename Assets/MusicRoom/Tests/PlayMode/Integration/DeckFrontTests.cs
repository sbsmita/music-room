// "The recorder is appearing behind me even if i am seeing straight in front. instruments should in front. drums is not
// seeing properly on place" (headset test, 2026-11-17, One alone with the kit, My set piano, harp, hand drums). With every
// real module (simulated headset, manual frames at 72 Hz), the kit alone after the ways the headset log shows it coming:
//   (a) the app starting with the headset still settling: the system's recentre comes while it is away, with the tracking
//       origin's heading 120 degrees from where the player then faces; it is applied once the headset is back; then
//       "Play here";
//   (b) Reset view (the menu's row) and the system's recentre (the Meta button held) with the player facing ahead;
//   (c) a page turn to the kit from the hand drums;
// the side table with the tape deck stands at the player's left, beside them (never farther behind the eyes than its
// home's 21 cm), facing them; the kit's pieces centred ahead of the eyes, facing the player, at its usual distance.
using System.Collections;
using System.Collections.Generic;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.Layout;
using MusicRoom.Lessons;
using MusicRoom.Tests.PlayMode.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Integration
{
    public class DeckFrontTests
    {
        const float Dt = 1f / 72f;

        TestBoot _boot;
        App _app;
        LayoutModule _layout;

        IEnumerator BootApp(string current)
        {
            _boot = new TestBoot(simulateHeadset: true, manualFrames: true);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, "single");
            _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, current);
            _boot.Settings.SetString(AppSettings.KeySet, "piano,harp,handdrums");
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
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_app != null && _app.Rig != null) _app.Rig.HeadTracked = true;
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            _layout = null;
            _app = null;
        }

        static HandPose Away(HandSide side) =>
            HandPose.Open(side, Vector3.zero, Quaternion.identity).WithIndexTipAt(new Vector3(side == HandSide.Right ? 0.6f : -0.6f, 1.9f, -0.9f));

        void Step(int n = 1) => _boot.Step(Dt, n);

        Transform Head => _app.Head;

        Rig RigOf(RigId id) => _app.Get<IRigRegistry>().Get(id);

        /// <summary>The head held at a pose for some frames (the desktop camera script may turn it between frames).</summary>
        void Hold(Vector3 pos, Quaternion rot, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                Head.SetPositionAndRotation(pos, rot);
                Step();
            }
            Head.SetPositionAndRotation(pos, rot);
        }

        void Settle(Vector3 pos, Quaternion rot)
        {
            for (int i = 0; i < 400 && (_layout.AnyMoving || _layout.Settling || _layout.IsTurning); i++) Hold(pos, rot, 1);
            Hold(pos, rot, 3);
        }

        /// <summary>A point in the player's seat frame (x right, z ahead of the eyes; the eyes' heading on the floor).</summary>
        static Vector2 InSeat(Vector3 world, Vector3 head, float yawDeg)
        {
            Vector3 d = world - head;
            d.y = 0f;
            Vector3 s = Quaternion.Euler(0f, -yawDeg, 0f) * d;
            return new Vector2(s.x, s.z);
        }

        /// <summary>Measures and checks the side table and the kit about the player's seat (eyes <paramref name="head"/>,
        /// facing <paramref name="yawDeg"/>).</summary>
        void AssertDeckAndKit(Vector3 head, float yawDeg, string how)
        {
            Assert.That(_layout.Mode, Is.EqualTo(LayoutMode.Single), how);
            Assert.That(_layout.Current, Is.EqualTo(InstrumentKind.Drums), how);
            Assert.IsFalse(_layout.AnyMoving, how + ": landed");
            Rig table = RigOf(RigId.Table), kit = RigOf(RigId.Drums), menu = RigOf(RigId.Lessons);
            Vector2 t = InSeat(table.Group.position, head, yawDeg);
            float tTurn = Mathf.DeltaAngle(table.Group.eulerAngles.y, yawDeg + table.HomeYawAll);
            RigEntry ke = _layout.Placement.Entry(kit);
            _layout.Placement.Pose(ke);
            Vector2 kc = InSeat(PlacementSolver.CentreWorld(ke), head, yawDeg);
            Vector2 kg = InSeat(kit.Group.position, head, yawDeg);
            float kTurn = Mathf.DeltaAngle(kit.Group.eulerAngles.y, yawDeg + kit.HomeYawSingle);
            Vector2 m = menu != null && menu.Group != null ? InSeat(menu.Group.position, head, yawDeg) : Vector2.zero;
            float reach = _layout.ReachOf(kit);
            Debug.Log($"[DeckFront] {how}: table ({t.x:+0.000;-0.000}, {t.y:+0.000;-0.000}) turned {tTurn:+0.0;-0.0} deg (home {SideTableHome.x:+0.00}, {SideTableHome.z:+0.00}); " +
                      $"kit group ({kg.x:+0.000;-0.000}, {kg.y:+0.000;-0.000}) centre ({kc.x:+0.000;-0.000}, {kc.y:+0.000;-0.000}) turned {kTurn:+0.0;-0.0} deg, reach {reach:F3} m, eyes {_layout.EyeDistance(kit):F3} m; " +
                      $"menu ({m.x:+0.00;-0.00}, {m.y:+0.00;-0.00}); resolver moves {_layout.Placement.LastMoves}");
            Assert.That(t.x, Is.LessThan(-0.25f), $"{how}: the side table at the player's left (x {t.x:F3})");
            Assert.That(t.y, Is.GreaterThan(SideTableHome.z - 0.03f), $"{how}: the side table beside the player, not behind them (z {t.y:F3})");
            Assert.That(Mathf.Abs(tTurn), Is.LessThan(5f), $"{how}: the side table faces the player");
            Assert.That(Mathf.Abs(kc.x), Is.LessThan(0.03f), $"{how}: the kit centred ahead (x {kc.x:F3})");
            Assert.That(kc.y, Is.GreaterThan(0.2f), $"{how}: the kit ahead of the eyes");
            Assert.That(Mathf.Abs(kTurn), Is.LessThan(2f), $"{how}: the kit faces the player");
        }

        static readonly Vector3 SideTableHome = new Vector3(-0.39f, 0f, -0.21f);

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator TheDeckStaysAtTheLeftAndTheKitAheadAfterStartResetAndAPageTurn()
        {
            yield return BootApp("drums");
            Vector3 head = new Vector3(0.05f, 1.15f, 0.02f);
            float yaw = 15f;
            Quaternion face = Quaternion.Euler(0f, yaw, 0f);

            // (a) The start: the system menu has the hands while the app comes up; the system recentres then with the
            // origin's heading 120 degrees away from where the player will face (the head was turned while putting it on).
            _app.Events.Publish(new InputFocusEvent { Focused = false });
            Step(5);
            Assert.IsTrue(_layout.AwayOrSettling, "away");
            Transform rig = _app.Rig.RigRoot;
            Quaternion origin = Quaternion.Euler(0f, yaw + 120f, 0f);
            rig.SetPositionAndRotation(new Vector3(head.x, 0f, head.z), origin);   // the origin on the floor under the head
            Head.SetPositionAndRotation(head, face);
            int backs = _layout.BringBacks;
            _app.Events.Publish(new RecenteredEvent());
            Assert.That(_layout.BringBacks, Is.EqualTo(backs), "held back while away");
            _app.Events.Publish(new InputFocusEvent { Focused = true });
            Hold(head, face, Mathf.CeilToInt(HeightConstants.ReturnSettle / Dt) + 6);
            Assert.That(_layout.BringBacks, Is.EqualTo(backs + 1), "applied once it is back");
            Settle(head, face);
            // Laid out about where the settled player faces (the origin's heading was 120 degrees away): the player's front
            // for every later bring, so the kit comes in front and the deck at the left without a "Play here".
            AssertDeckAndKit(head, yaw, "(a) the start's recentre on the return");
            for (int i = 0; i < 2; i++)
            {
                _layout.Next(1);
                Settle(head, face);
                _layout.Next(-1);
                Settle(head, face);
            }
            AssertDeckAndKit(head, yaw, "(a) page turns after the start's recentre");
            // "Play here".
            _app.Events.Publish(new StartChoiceEvent { InMyRoom = false });
            Settle(head, face);
            AssertDeckAndKit(head, yaw, "(a) start + Play here");

            // (b) Reset view by the menu, then the system's recentre (the Meta button held) facing ahead.
            _layout.BringToPlayer();
            Settle(head, face);
            AssertDeckAndKit(head, yaw, "(b) Reset view (menu)");
            rig.SetPositionAndRotation(new Vector3(head.x, 0f, head.z), face);
            Hold(head, face, 2);
            _app.Events.Publish(new RecenteredEvent());
            Assert.That(_layout.LastBringBack, Is.EqualTo(BringBackSource.Recentre));
            Settle(head, face);
            AssertDeckAndKit(head, yaw, "(b) Reset view (recentre)");

            // (c) A page turn to the kit from the hand drums (and the hand drums from the kit), looking ahead.
            Assert.IsTrue(_layout.Bring(InstrumentKind.HandDrums, BringReason.Menu));
            Settle(head, face);
            Assert.That(_layout.Current, Is.EqualTo(InstrumentKind.HandDrums));
            for (int dir = 1; dir >= -1; dir -= 2)
            {
                for (int i = 0; i < 12 && _layout.Current != InstrumentKind.Drums; i++)
                {
                    _layout.Next(dir);
                    Settle(head, face);
                }
                AssertDeckAndKit(head, yaw, "(c) page turn " + (dir > 0 ? "forward" : "back"));
                Assert.IsTrue(_layout.Bring(InstrumentKind.HandDrums, BringReason.Menu));
                Settle(head, face);
            }
            yield return null;
        }

        /// <summary>A kit turned or moved by the player (or by a spot an older build saved by itself) that touches the side table:
        /// the table moves off it to the left, never back behind the player (before: straight back, seat z -0.32 .. -0.38).</summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator AKitTouchingTheTableNeverPushesTheDeckBehind()
        {
            yield return BootApp("drums");
            Vector3 head = new Vector3(0f, 1.15f, 0f);
            Quaternion face = Quaternion.identity;
            Hold(head, face, 3);
            _app.Events.Publish(new StartChoiceEvent { InMyRoom = false });
            Settle(head, face);
            float[] dists = { 0f, 0.06f, 0.12f };
            float[] sides = { -0.15f, -0.08f, 0f, 0.08f };
            float[] yaws = { 0f, -20f, 20f };
            int touched = 0;
            foreach (float dd in dists)
            foreach (float ss in sides)
            foreach (float yy in yaws)
            {
                var offs = new Dictionary<RigId, ComfortOffset> { [RigId.Drums] = new ComfortOffset(dd, ss, 0f, yy) };
                _app.Settings.SetComfort(LayoutMode.Single, InstrumentKind.Drums, ComfortCodec.Encode(offs));
                _layout.Bring(InstrumentKind.HandDrums, BringReason.Menu);   // another layout's key: the offsets are read again
                Settle(head, face);
                int kept = _layout.TableKeptBeside;
                _layout.Bring(InstrumentKind.Drums, BringReason.Menu);
                Settle(head, face);
                _layout.BringToPlayer();
                Settle(head, face);
                if (_layout.TableKeptBeside > kept) touched++;
                Rig table = RigOf(RigId.Table), kit = RigOf(RigId.Drums);
                Vector2 t = InSeat(table.Group.position, head, 0f);
                Vector2 k = InSeat(kit.Group.position, head, 0f);
                string how = $"kit offset d {dd:F2} s {ss:+0.00;-0.00} yaw {yy:+0;-0}";
                Debug.Log($"[DeckSweep] {how}: kit ({k.x:+0.000;-0.000}, {k.y:+0.000;-0.000}) reach {_layout.ReachOf(kit):F3}; table ({t.x:+0.000;-0.000}, {t.y:+0.000;-0.000}); moves {_layout.Placement.LastMoves}");
                Assert.That(t.y, Is.GreaterThan(SideTableHome.z - 0.03f), $"{how}: the side table not pushed behind the player (z {t.y:F3})");
                Assert.That(t.x, Is.LessThan(-0.25f), $"{how}: the side table at the left (x {t.x:F3})");
                RigEntry te = _layout.Placement.Entry(table);
                Assert.IsTrue(_layout.Placement.Free(te, OthersOf(te), head, 0f), $"{how}: the side table touches nothing");
            }
            Debug.Log($"[DeckSweep] {touched} kit pose(s) touched the table");   // none since the playing room (2026-11-18): the kit stands farther out
            _app.Settings.SetComfort(LayoutMode.Single, InstrumentKind.Drums, "");
            yield return null;
        }

        List<RigEntry> OthersOf(RigEntry e)
        {
            var list = new List<RigEntry>();
            foreach (RigEntry o in _layout.Placement.Entries) if (o != e && PlacementSolver.Active(o)) list.Add(o);
            return list;
        }

        static readonly DeckButton[] Buttons = { DeckButton.Rec, DeckButton.Play, DeckButton.Stop, DeckButton.Loop, DeckButton.Guide };

        /// <summary>"im also unable to hands properly on tape deck": the deck's buttons in the seat frame (x right, z ahead of the
        /// eyes, y above the floor) for One alone with the piano and the kit and for My set; at the player's left, beside the
        /// hip, never farther behind the eyes than the table's home allows; and the deck holds still while nothing changes
        /// (its jump rule, PLAYFIX, counts every fingertip in a control as already holding after the deck moves 1 cm).</summary>
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator TheDecksButtonsAreAtTheLeftHandAndStill()
        {
            yield return BootApp("piano");
            Vector3 head = new Vector3(0f, 1.15f, 0f);
            Quaternion face = Quaternion.identity;
            Hold(head, face, 3);
            _app.Events.Publish(new StartChoiceEvent { InMyRoom = false });
            Settle(head, face);
            LessonsModule m = null;
            foreach (IAppModule x in _app.Modules) if (x is LessonsModule l) m = l;
            Assert.That(m?.Deck, Is.Not.Null, "the deck");
            string[] names = { "One alone: piano", "One alone: drums", "My set" };
            for (int c = 0; c < names.Length; c++)
            {
                if (c == 1) _layout.Bring(InstrumentKind.Drums, BringReason.Menu);
                if (c == 2) _layout.Set(LayoutMode.All, InstrumentKind.Piano);
                Settle(head, face);
                _layout.BringToPlayer();
                Settle(head, face);
                int jumps = m.Deck.Jumps;
                Hold(head, face, 144);   // two seconds with nothing changing
                Assert.That(m.Deck.Jumps, Is.EqualTo(jumps), names[c] + ": the deck holds still");
                var sb = new System.Text.StringBuilder();
                foreach (DeckButton b in Buttons)
                {
                    Vector3 w = m.Deck.Root.TransformPoint(TapeDeck.ButtonLocal(b));
                    Vector2 p = InSeat(w, head, 0f);
                    sb.Append($" {b} ({p.x:+0.000;-0.000}, {p.y:+0.000;-0.000}, y {w.y:F2})");
                    Assert.That(p.x, Is.LessThan(-0.15f), $"{names[c]}: {b} at the left");
                    // at the table's home (seat (-0.39, -0.21)): the buttons 3-23 cm behind the eyes, beside the left hip; never
                    // farther back than that (a resolver push, a saved spot)
                    Assert.That(p.y, Is.GreaterThan(-0.25f), $"{names[c]}: {b} beside the player, not behind (z {p.y:F3})");
                    Assert.That(new Vector2(p.x, p.y).magnitude, Is.LessThan(0.5f), $"{names[c]}: {b} within the left hand's reach");
                }
                Debug.Log($"[DeckFront] {names[c]} ({_layout.Mode}, {_layout.Current}): deck buttons{sb}; table at {InSeat(RigOf(RigId.Table).Group.position, head, 0f)}");
            }
            yield return null;
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator TheDeckStaysAtTheLeftForEveryInstrumentAlone()
        {
            yield return BootApp("drums");
            Vector3 head = new Vector3(0f, 1.15f, 0f);
            Quaternion face = Quaternion.identity;
            Hold(head, face, 3);
            _app.Events.Publish(new StartChoiceEvent { InMyRoom = false });
            Settle(head, face);
            IInstrumentRegistry reg = _app.Registry;
            for (int i = 0; i < reg.Carousel.Count; i++)
            {
                InstrumentKind k = reg.Carousel[i];
                InstrumentDescriptor d = reg.Get(k);
                Rig r = RigOf(InstrumentIds.RigOf(k));
                if (d == null || !d.Playable || !reg.IsLoaded(k) || r?.Group == null) continue;
                if (_layout.Current != k) Assert.IsTrue(_layout.Bring(k, BringReason.Menu), k + " brought");
                Settle(head, face);
                Rig table = RigOf(RigId.Table);
                Vector2 t = InSeat(table.Group.position, head, 0f);
                RigEntry e = _layout.Placement.Entry(r);
                _layout.Placement.Pose(e);
                Vector2 c = InSeat(PlacementSolver.CentreWorld(e), head, 0f);
                Debug.Log($"[DeckFront] {k} alone: table ({t.x:+0.000;-0.000}, {t.y:+0.000;-0.000}); play centre ({c.x:+0.000;-0.000}, {c.y:+0.000;-0.000}), reach {_layout.ReachOf(r):F3} m; resolver moves {_layout.Placement.LastMoves}");
                Assert.That(t.x, Is.LessThan(-0.25f), $"{k}: the side table at the player's left");
                Assert.That(t.y, Is.GreaterThan(SideTableHome.z - 0.03f), $"{k}: the side table beside the player, not behind (z {t.y:F3})");
            }
            yield return null;
        }
    }
}
