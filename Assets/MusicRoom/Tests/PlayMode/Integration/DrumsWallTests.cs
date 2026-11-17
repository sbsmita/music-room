// "why i cant see drums placed near wall in VR room?? previous i used to see" (headset, 2026-11-17, One alone with the
// piano, the harp or the hand drums; My set piano, harp, hand drums). With every real module (simulated headset, manual
// frames), in the VR room after "Play here" at the seat: the kit, out of the layout, stands on display at its left-wall
// spot (its rig active, stowed), whichever of the three is out, after a page turn and a Reset view too.
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
    public class DrumsWallTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        App _app;
        LayoutModule _layout;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
            _app = null;
            _layout = null;
        }

        void Hold(Vector3 head, int n)
        {
            for (int i = 0; i < n; i++) { _app.Head.SetPositionAndRotation(head, Quaternion.identity); _boot.Step(Dt); }
        }

        void AssertKitOnTheWall(string how)
        {
            Rig kit = _app.Get<IRigRegistry>().Get(RigId.Drums);
            StowPlan.Result res;
            bool has = _layout.Display.TryGet(InstrumentKind.Drums, out res);
            var stow = _app.Registry.Get(InstrumentKind.Drums);
            Vector3 p = kit.Group.position;
            Debug.Log($"[DrumsWall] {how} (headless {_app.Headless}): current {_layout.Current}, display {(has ? res.Placement.ToString() : "none")} {(has && res.Spot != null ? res.Spot.Id : "-")}, kit at {p.ToString("F2")} yaw {kit.Group.eulerAngles.y:F0}, active {kit.Group.gameObject.activeInHierarchy}");
            if (has && res.Placement == StowPlan.Placement.Hidden) LogSeatBoxes(how);
            Assert.IsTrue(has, how + ": the kit is on the display list");
            Assert.That(res.Placement, Is.EqualTo(StowPlan.Placement.Spot), how + ": the kit at its own spot");
            Assert.That(res.Spot.Id, Is.EqualTo("left-wall"), how);
            Assert.IsTrue(kit.Group.gameObject.activeInHierarchy, how + ": the kit is shown");
        }

        void LogSeatBoxes(string how)
        {
            foreach (Rig r in _app.Get<IRigRegistry>().Rigs)
                if (r.Group != null) Debug.Log($"[DrumsWall] {how}: rig {r.Id} at {r.Group.position.ToString("F2")} yaw {r.Group.eulerAngles.y:F0} inScene {r.InScene} active {r.Group.gameObject.activeInHierarchy}");
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator TheKitStandsAtTheLeftWallWhileAnotherIsOut([Values("piano", "harp", "handdrums")] string current, [Values(true, false)] bool headless)
        {
            _boot = new TestBoot(headless: headless, simulateHeadset: true, manualFrames: true);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, "single");
            _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, current);
            _boot.Settings.SetString(AppSettings.KeySet, "piano,harp,handdrums");
            MenuBootGuard.Begin();
            yield return _boot.Start();
            MenuBootGuard.End();
            _app = _boot.App;
            _layout = _app.Get<ILayoutService>() as LayoutModule;
            float t0 = Time.realtimeSinceStartup;
            while (_app.Rig.State == XrState.Starting && Time.realtimeSinceStartup - t0 < 4f) yield return null;
            Vector3 head = new Vector3(0f, 1.15f, 0f);
            Hold(head, 5);
            AssertKitOnTheWall("boot");
            _app.Events.Publish(new StartChoiceEvent { InMyRoom = false });
            Hold(head, 120);
            AssertKitOnTheWall("Play here");
            _layout.BringToPlayer();
            Hold(head, 120);
            AssertKitOnTheWall("Reset view");
            InstrumentKind other = current == "piano" ? InstrumentKind.Harp : InstrumentKind.Piano;
            _layout.Bring(other, BringReason.Menu);
            Hold(head, 200);
            AssertKitOnTheWall("another brought");
            yield return null;
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator ZzStowSweep()
        {
            _boot = new TestBoot(headless: false, simulateHeadset: true, manualFrames: true);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);
            _boot.Settings.SetString(AppSettings.KeyLayoutMode, "single");
            _boot.Settings.SetString(AppSettings.KeyCurrentInstrument, "piano");
            _boot.Settings.SetString(AppSettings.KeySet, "piano,harp,handdrums");
            _boot.Settings.SetString(AppSettings.KeyStowPrefix + "drums", "0,0.3,-90");   // a saved spot at the seat: own spot blocked, spares tried
            MenuBootGuard.Begin();
            yield return _boot.Start();
            MenuBootGuard.End();
            _app = _boot.App;
            _layout = _app.Get<ILayoutService>() as LayoutModule;
            Hold(new Vector3(0f, 1.15f, 0f), 5);
            _layout.Display.TryGet(InstrumentKind.Drums, out StowPlan.Result res);
            Debug.Log($"[DrumsWall] stow at the seat: {res.Placement} {(res.Spot != null ? res.Spot.Id : "-")}");
            foreach (StowPlan.Spare sp in StowPlan.Spares)
            {
                StowPlan.Spot own = StowPlan.Spot.From(_app.Registry.Get(InstrumentKind.Drums));
                StowPlan.Spot at = own.At(sp);
                var boxes = new List<SeatPlanner.FloorObb>();
                at.WorldBoxes(boxes);
                string hits = "";
                foreach (SeatPlanner.FloorObb b in boxes)
                {
                    if (!SeatPlanner.InsideRoom(b)) hits += "outside-room ";
                    for (int k = 0; k < _layout.Display.Furniture.Count; k++) if (SeatPlanner.Overlaps(b, _layout.Display.Furniture[k], StowPlan.Gap)) hits += "furniture#" + k + " ";
                }
                Debug.Log($"[DrumsWall] spare {sp.Id}: {hits}");
            }
            IRoomFurniture f = _app.Get<IRoomFurniture>();
            if (f != null) for (int k = 0; k < f.FloorBlockers.Count; k++) Debug.Log($"[DrumsWall] furniture#{k} {f.FloorBlockers[k].Name} {f.FloorBlockers[k].Centre} half {f.FloorBlockers[k].Half}");
            yield return null;
        }
    }
}
