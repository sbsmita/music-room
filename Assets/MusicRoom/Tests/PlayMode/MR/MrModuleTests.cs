// "Play in my room" end to end in PlayMode without a headset: TestBoot with a simulated headset, passthrough simulated,
// MRUK's recorded rooms loaded from JSON, scripted hands, the contract Layout (MrTestRig). Covers ARCHITECTURE.md
// sections 12 / 15.9: entry and exit, the rig reset, the auto-arrange with the piano back at stand height and then on the
// best table (else in front on its stand), a re-arrange that carries the piano off its table, the placing reticle and
// pinch as Layout's StartPlacing starts it, no window or snow portal anywhere (the snowy window was removed 2026-11-13: the
// snow falls in the whole room), MR lighting and the shadow catcher (also in single mode), the focus reload, Space Setup
// without a headset, a shutdown inside MR, hints, the audio hand-offs, and the allocation budget of the MixedReality tick.
#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Hands;
using MusicRoom.MR;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace MusicRoom.Tests.PlayMode.MR
{
    public class MrModuleTests : MrTestRig
    {
        /// <summary>Objects under root (inactive ones too) whose name contains part (any case).</summary>
        internal static int NamedUnder(Transform root, string part)
        {
            int n = 0;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0) n++;
            return n;
        }

        [UnityTest]
        public IEnumerator EnterHidesTheRoomLoadsTheSceneAndShowsNoWindow()
        {
            var switches = App.Get<IAmbienceSwitches>();
            switches.Set(AmbienceKind.Fire, true);
            switches.Set(AmbienceKind.Wind, true);
            switches.Set(AmbienceKind.Snow, true);
            Assert.That(Mr.Available, Is.True, "simulated passthrough");
            Assert.That(Mr.Status, Is.EqualTo(MrStatus.Idle));

            yield return EnterRoom("MeshLivingRoom2");

            Assert.That(Mr.Active && Mr.Simulated);
            Assert.That(Mr.HasSpaceSetup, Is.True);
            Assert.That(Mr.IsPlacing, Is.False, "arranged: instruments play");
            Assert.That(MrEvents.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(MrEvents[0].Active, Is.True);
            Assert.That(App.Get<IRoom>().Visible, Is.False, "the virtual room is hidden");
            Assert.That(switches.Get(AmbienceKind.Fire), Is.False, "fire off");
            Assert.That(switches.Get(AmbienceKind.Wind), Is.False, "wind off");
            Assert.That(switches.Get(AmbienceKind.Snow), Is.True, "snow stays (EnterAR)");
            Assert.That(((AudioEngine)App.Get<IAudioEngine>()).MixedReality, Is.True, "audio MR mode (-6 dB sends)");
            Assert.That(App.Rig.HeadCamera.backgroundColor, Is.EqualTo(MrDevOptions.SimulatedBackground), "simulated passthrough");

            // No snowy window, no snow portal behind the room's five windows (removed 2026-11-13): nothing of them anywhere.
            Assert.That(NamedUnder(App.WorldRoot, "SnowPortal") + NamedUnder(App.WorldRoot, "Snow portal"), Is.Zero, "no snow portal");
            Assert.That(NamedUnder(App.WorldRoot.Find("MixedReality"), "window"), Is.Zero, "no window, ghost or reticle of one");

            // The snow falls all round the player: its sound from 0.9 m above the head, the wind 0.35 m above that.
            var snowSrc = App.Get<IAudioEngine>().Source(SourceId.Snow).transform.position;
            Assert.That(Vector3.Distance(snowSrc, App.Head.position + Vector3.up * MrConstants.SnowSourceAbove), Is.LessThan(0.05f));
            var windSrc = App.Get<IAudioEngine>().Source(SourceId.Wind).transform.position;
            Assert.That(windSrc.y - snowSrc.y, Is.EqualTo(0.35f).Within(1e-4f));

            // MR lighting: room-neutral Trilight, one white shadowed sun as URP's main light.
            Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Trilight));
            Assert.That(ColorUtility.ToHtmlStringRGB(RenderSettings.ambientSkyColor), Is.EqualTo("B1B1B1"));
            Assert.That(ColorUtility.ToHtmlStringRGB(RenderSettings.ambientEquatorColor), Is.EqualTo("969696"));
            Assert.That(ColorUtility.ToHtmlStringRGB(RenderSettings.ambientGroundColor), Is.EqualTo("686868"));
            Assert.That(RenderSettings.sun, Is.SameAs(Mr.Lighting.Sun));
            Assert.That(Mr.Lighting.Sun.isActiveAndEnabled && Mr.Lighting.Sun.shadows == LightShadows.Hard);
            Assert.That(Mr.Lighting.Sun.intensity, Is.EqualTo(1.3f / Mathf.PI).Within(1e-5f));
            Assert.That(Mr.Lighting.Sun.color, Is.EqualTo(Color.white));
        }

        [UnityTest]
        public IEnumerator ThePianoGoesOnTheBestTableWithTheRigsAroundIt()
        {
            yield return EnterRoom("MeshOffice1");
            var piano = Rig(RigId.Piano);
            // The head the module used (the seated desktop view) picks the 0.74 m desk.
            Assert.IsTrue(TableFinder.TryFindBest(Room, App.Head.position, App.Head.forward, TableQuery.Default, out var best));
            Assert.That(best.TopHeight, Is.EqualTo(0.74f).Within(0.01f));
            Pose want = TableFinder.PianoPoseOnTable(best, best.EdgeMidpoint);
            Assert.That(Mr.PianoOnTable, Is.True);
            Assert.That(Stand.OnSurface, Is.True, "stand hidden on the table");
            Assert.That(Mr.PlacedByPlayer, Is.False);
            Assert.That(Vector3.Distance(piano.Group.position, want.position), Is.LessThan(1e-4f));
            Assert.That(piano.Group.position.y, Is.EqualTo(best.TopY + 0.092f).Within(1e-4f), "key tops TopY + tableLift");
            Assert.That(Vector3.Dot(piano.Group.forward, best.TowardUser), Is.LessThan(-0.99f), "facing the user");
            Vector3 edgeToPiano = piano.Group.position - best.EdgeMidpoint;
            edgeToPiano.y = 0f;
            float push = TableFinder.PushFor(best, best.EdgeMidpoint);
            Assert.That(edgeToPiano.magnitude, Is.EqualTo(push).Within(1e-3f), "PUSH_BACK onto the table (shortened on a shallow top)");
            Assert.That(push + TableFinder.BodyRear, Is.LessThanOrEqualTo(best.Depth - TableFinder.EdgeMargin + 1e-4f), "the body fits on the top");
            Assert.That(TryGetPose(out var pose, out bool onTable) && onTable && Vector3.Distance(pose.position, want.position) < 1e-4f);

            // placeAroundPiano (Layout): every other rig keeps its VR-room offset from the piano, turned with it.
            float turn = piano.Group.eulerAngles.y - piano.HomeYawAll;
            foreach (var id in new[] { RigId.Drums, RigId.Harp, RigId.Table })
            {
                var r = Rig(id);
                Vector3 d = r.HomeAll - piano.HomeAll; d.y = 0f;
                Vector3 p = piano.Group.position + Quaternion.Euler(0f, turn, 0f) * d; p.y = r.HomeAll.y;
                Assert.That(Vector3.Distance(r.Group.position, p), Is.LessThan(1e-3f), id + " around the piano");
                Assert.That(Mathf.DeltaAngle(r.Group.eulerAngles.y, r.HomeYawAll + turn), Is.EqualTo(0f).Within(0.01f), id + " heading");
            }

            // Catcher on the desk under the piano, turned with it; the sun looks at the piano.
            Assert.That(Mr.Lighting.CatcherVisible, Is.True);
            Vector3 c = Mr.Lighting.Catcher.position;
            Assert.That(c.y, Is.EqualTo(best.TopY + 0.002f).Within(2e-3f), "2 mm over the desk top");
            Assert.That(new Vector2(c.x - piano.Group.position.x, c.z - piano.Group.position.z).magnitude, Is.LessThan(1e-4f));
            Assert.That(Mathf.DeltaAngle(Mr.Lighting.Catcher.eulerAngles.y, piano.Group.eulerAngles.y), Is.EqualTo(0f).Within(0.01f));
            Vector3 toPiano = (piano.Group.position - MrConstants.SunPosition).normalized;
            Assert.That(Vector3.Dot(Mr.Lighting.Sun.transform.forward, toPiano), Is.GreaterThan(0.9999f));
            Assert.That(Mr.Hint, Is.EqualTo(MrConstants.EnterOnTableHint));
        }

        bool TryGetPose(out Pose pose, out bool onTable) => Mr.TryGetPianoPose(out pose, out onTable);

        [UnityTest]
        public IEnumerator WithoutATableThePianoStandsInFrontOfThePlayer()
        {
            yield return EnterRoom("MeshBedroom3"); // no tables, no windows
            var piano = Rig(RigId.Piano);
            Assert.That(Mr.PianoOnTable, Is.False);
            Assert.That(Stand.OnSurface, Is.False, "on its stand");
            Assert.That(Mr.TryGetPianoPose(out _, out _), Is.False);
            var seat = SeatFrame.From(App.Head);
            Vector3 home = seat.Place(piano.HomeAll);
            Assert.That(new Vector2(piano.Group.position.x - home.x, piano.Group.position.z - home.z).magnitude, Is.LessThan(1e-3f), "seat home (0, .72, .45)");
            Assert.That(piano.Group.position.y, Is.EqualTo(TableFinder.FloorY(Room) + 0.72f).Within(1e-4f), "on its stand");
            Assert.That(Mr.Lighting.Catcher.position.y, Is.EqualTo(TableFinder.FloorY(Room) + 0.002f).Within(2e-3f), "catcher on the floor");
            Assert.That(Mr.Hint, Is.EqualTo(MrConstants.EnterHint));
        }

        // MR's AutoArrange: piano.position.y = pianoHome.position.y before arrangeAroundPlayer, because Layout keeps the
        // piano's height (a piano lifted in VR, Grab lift range -.45..+.6 m, would otherwise enter MR at that height with
        // its stand floating or sunk into the real floor).
        [UnityTest]
        public IEnumerator ALiftedPianoGetsItsStandHeightBackOnEntering()
        {
            var piano = Rig(RigId.Piano);
            foreach (float lift in new[] { 0.4f, -0.3f })
            {
                piano.Group.position += new Vector3(0f, lift, 0f);
                Layout.ArrangeAroundPlayer(true);
                Assert.That(piano.Group.position.y, Is.EqualTo(0.72f + lift).Within(1e-4f), "the contract Layout keeps a lifted piano lifted");
                yield return EnterRoom("MeshBedroom3");
                Assert.That(piano.Group.position.y, Is.EqualTo(TableFinder.FloorY(Room) + MrConstants.StandHeight).Within(1e-4f), $"lifted {lift}: back on its stand");
                Assert.That(Stand.OnSurface, Is.False);
                Mr.Exit();
                yield return null;
                yield return null;
                Assert.That(piano.Group.position.y, Is.EqualTo(0.72f).Within(1e-4f), "RestoreHomes on exit");
            }
        }

        // A Layout re-arrange in MR (two-hand summon, recentre) keeps the piano's height. Off the table it must not hang
        // there with the stand hidden (a floating piano) nor show a stand ending in mid-air: the stand comes back
        // and the piano stands on the floor, unless its body still rests on a real top where Layout put it.
        [UnityTest]
        public IEnumerator ARearrangeThatCarriesThePianoOffItsTablePutsItBackOnItsStand()
        {
            yield return EnterRoom("MeshOffice1");
            var piano = Rig(RigId.Piano);
            Assert.That(Mr.PianoOnTable && Stand.OnSurface, "on the desk");
            float floor = TableFinder.FloorY(Room);
            float tableKeys = piano.Group.position.y;

            // Layout slides it 10 cm along the same desk (the seat moved): its body still rests on the top, so it stays
            // there with the stand hidden.
            var desk = App.Rig.Desktop;
            Vector3 slid = piano.Group.position + piano.Group.right * 0.1f;
            Vector3 head0 = App.Head.position;
            var seat0 = SeatFrame.From(App.Head);
            Vector3 offset = seat0.Place(piano.HomeAll) - new Vector3(seat0.Head.x, piano.HomeAll.y, seat0.Head.z);
            App.Head.position = new Vector3(slid.x - offset.x, head0.y, slid.z - offset.z);
            Layout.ArrangeAroundPlayer(true);
            Assert.That(new Vector2(piano.Group.position.x - slid.x, piano.Group.position.z - slid.z).magnitude, Is.LessThan(1e-3f), "Layout moved it 10 cm");
            Assert.That(Mr.PianoOnTable && Stand.OnSurface, "still resting on the desk");
            Assert.That(piano.Group.position.y, Is.EqualTo(tableKeys).Within(1e-4f));
            App.Head.position = head0;

            // A heading whose seat home lands over open floor (no furniture under the body).
            float floorYaw = float.NaN;
            for (float yaw = 0f; yaw < 360f && float.IsNaN(floorYaw); yaw += 10f)
            {
                Vector3 home = SeatHome(yaw, piano);
                if (!TableFinder.TryPointAtSurface(Room, new Ray(new Vector3(home.x, floor + 1.6f, home.z), Vector3.down), out _, out _, out bool onTable)) continue;
                if (!onTable && FootprintClear(home)) floorYaw = yaw;
            }
            Assert.That(float.IsNaN(floorYaw), Is.False, "a heading with open floor ahead");
            Debug.Log($"[MR-TEST] re-arrange off the desk: heading {floorYaw}");

            Face(desk, floorYaw);
            int resets = Stand.Resets;
            Layout.ArrangeAroundPlayer(true);
            Assert.That(Stand.Resets, Is.EqualTo(resets + 1), "the stand comes back");
            Assert.That(Stand.OnSurface, Is.False);
            Assert.That(Mr.PianoOnTable, Is.False);
            Assert.That(piano.Group.position.y, Is.EqualTo(floor + MrConstants.StandHeight).Within(1e-4f), "on the floor at stand height, not at the desk's");
            Vector3 home2 = SeatHome(floorYaw, piano);
            Assert.That(new Vector2(piano.Group.position.x - home2.x, piano.Group.position.z - home2.z).magnitude, Is.LessThan(1e-3f), "where Layout put it");
            yield return null;
            // The catcher follows it down to the floor.
            Assert.That(Mr.Lighting.Catcher.position.y, Is.EqualTo(floor + MrConstants.CatcherLift).Within(2e-3f));
            // A later arrange leaves a piano on its stand alone.
            Layout.ArrangeAroundPlayer(true);
            Assert.That(Stand.Resets, Is.EqualTo(resets + 1));
            Assert.That(piano.Group.position.y, Is.EqualTo(floor + MrConstants.StandHeight).Within(1e-4f));
        }

        Vector3 SeatHome(float yaw, Rig piano)
        {
            Vector3 head = App.Head.position;
            return new SeatFrame { Head = head, YawDeg = yaw }.Place(piano.HomeAll);
        }

        // Nothing (bed, chair, desk) under the piano body's middle and ends: open floor for the stand.
        static bool FootprintClear(Vector3 keys)
        {
            foreach (float dx in new[] { -0.55f, 0f, 0.55f })
            {
                Vector3 p = keys + new Vector3(dx, 0f, 0.15f);
                if (Room.IsPositionInSceneVolume(new Vector3(p.x, 0.3f, p.z), false, 0.05f)) return false;
            }
            return true;
        }

        void Face(DesktopRigFallback desk, float yaw)
        {
            desk.yaw = yaw;
            desk.pitch = 0f;
            App.Head.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        // ARCHITECTURE.md 12 / 21: Layout's Grab step 5 hides the other rigs, then calls IMixedReality.StartPlacing for a
        // dwell-complete pinch in empty air. While placing nothing ticks (the menu included); the next pinch
        // puts the piano where the hand points and is used up in its frame (placedThisFrame).
        [UnityTest]
        public IEnumerator StartPlacingFreezesEveryInstrumentAndTheNextPinchPutsThePianoThere()
        {
            yield return EnterRoom("MeshOffice1");
            Assert.That(TableFinder.TryFindBest(Room, App.Head.position, App.Head.forward, TableQuery.Default, out var desk));
            var drums = Rig(RigId.Drums);
            Assert.That(Probes.Table.Ticks, Is.GreaterThan(0), "instruments tick normally");

            // The pinch Layout's dwell completed: held still in empty air to the player's left.
            Vector3 air = App.Head.position + new Vector3(-0.55f, -0.25f, 0.05f);
            bool menuActive = Rig(RigId.Lessons).Group.gameObject.activeSelf;
            Fake.SetPose(HandSide.Right, RightAt(air, false));
            yield return null;
            Fake.SetPose(HandSide.Right, RightAt(air, true));
            yield return null;
            yield return null;
            Assert.That(App.Input.Hand(Handedness.Right).Pinching, Is.True);
            Mr.StartPlacing();
            Assert.That(Mr.IsPlacing, Is.True);
            Assert.That(drums.Group.gameObject.activeSelf, Is.False, "the other rigs hide");
            Assert.That(Rig(RigId.Piano).Group.gameObject.activeSelf, Is.True);
            Assert.That(Rig(RigId.Lessons).Group.gameObject.activeSelf, Is.EqualTo(menuActive), "the menu rig is left alone");
            int tableAtStart = Probes.Table.Ticks, menuAtStart = Probes.Menu.Ticks;
            yield return null;
            Assert.That(Mr.Hint, Is.EqualTo(MrConstants.PlacingHint));
            yield return null;
            Assert.That(Probes.Table.Ticks, Is.EqualTo(tableAtStart), "no instrument ticks while placing");
            Assert.That(Probes.Menu.Ticks, Is.EqualTo(menuAtStart), "the menu included");

            // Let go, point at the desk: the reticle lies on it, facing away from the head, footprint beyond the point.
            Vector3 target = desk.EdgeMidpoint - desk.TowardUser * 0.12f;
            Vector3 wrist = App.Head.position + new Vector3(0.15f, -0.35f, 0.15f);
            Fake.SetPose(HandSide.Right, AimRightAt(wrist, target, 0.05f));
            yield return null;
            yield return null;
            Assert.That(Mr.PianoReticle.Visible, Is.True, "reticle on the desk");
            Vector3 hit = Mr.LastReticleHit;
            Assert.That(hit.y, Is.EqualTo(desk.TopY).Within(2e-3f), "on the desk top");
            Assert.That(Vector3.Distance(hit, target), Is.LessThan(0.03f));
            Transform ret = Mr.PianoReticle.Transform;
            Assert.That(Vector3.Dot(ret.forward, MrUtil.Flat(hit - App.Head.position, Vector3.forward)), Is.GreaterThan(0.9999f));

            // Pinch: the piano goes there, the pinch is used up this frame only.
            Phases.PlacingAtGrab.Clear();
            Fake.SetPose(HandSide.Right, AimRightAt(wrist, target, 0f));
            yield return null;
            Assert.That(Mr.IsPlacing, Is.False, "placed: the next frame plays again");
            Assert.That(Phases.PlacingAtGrab.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(Phases.PlacingAtGrab[0], Is.True, "the placing pinch is used up in its frame (placedThisFrame)");
            yield return null;
            Assert.That(Phases.PlacingAtGrab[Phases.PlacingAtGrab.Count - 1], Is.False);
            Pose want = PianoPlacement.FromHit(hit, App.Head.position, TableFinder.FloorY(Room), out bool onTable);
            var piano = Rig(RigId.Piano);
            Assert.That(onTable, Is.True);
            Assert.That(Vector3.Distance(piano.Group.position, want.position), Is.LessThan(2e-3f));
            Assert.That(piano.Group.position.y, Is.EqualTo(hit.y + 0.092f).Within(1e-3f));
            Assert.That(Mr.PianoOnTable && Mr.PlacedByPlayer && Stand.OnSurface);
            Assert.That(drums.Group.gameObject.activeSelf, Is.True, "rigs back");
            Assert.That(Mr.PianoReticle.Visible, Is.False);
            Assert.That(Mr.Hint, Is.EqualTo(MrConstants.PlacedHint));
            yield return null;
            Assert.That(Probes.Table.Ticks, Is.GreaterThan(tableAtStart), "instruments tick again");
            Assert.That(Probes.Menu.Ticks, Is.GreaterThan(menuAtStart));
        }

        [UnityTest]
        public IEnumerator APinchWithNothingToPointAtEndsPlacing()
        {
            yield return EnterRoom("MeshBedroom3");
            // Aim at the ceiling (not an upward surface) from a hand; the head looks up too.
            Vector3 wrist = App.Head.position + new Vector3(0.15f, -0.3f, 0.2f);
            App.Rig.Desktop.pitch = -80f;
            Fake.SetPose(HandSide.Right, AimRightAt(wrist, wrist + Vector3.up * 3f, 0.05f));
            yield return null;
            yield return null;
            Assert.That(Mr.BeginPlacing(1), Is.True);
            Assert.That(Mr.IsPlacing, Is.True);
            yield return null;
            yield return null;
            Assert.That(Mr.PianoReticle.Visible, Is.False, "nothing to put it on");
            Fake.SetPose(HandSide.Right, AimRightAt(wrist, wrist + Vector3.up * 3f, 0f));
            yield return null;
            Assert.That(Mr.IsPlacing, Is.False, "the pinch ends placing (no trap)");
            Assert.That(Rig(RigId.Drums).Group.gameObject.activeSelf, Is.True);
            Assert.That(Mr.PlacedByPlayer, Is.False);
        }

        // Rigs Layout hid before calling StartPlacing come back even when placing ends without a landing.
        [UnityTest]
        public IEnumerator StartPlacingFromLayoutBringsBackTheRigsLayoutHid()
        {
            Mr.StartPlacing();
            Assert.That(Mr.IsPlacing, Is.False, "outside mixed reality StartPlacing does nothing");
            yield return EnterRoom("MeshOffice1");
            Assert.That(TableFinder.TryFindBest(Room, App.Head.position, App.Head.forward, TableQuery.Default, out var desk));
            var drums = Rig(RigId.Drums);
            Vector3 wrist = App.Head.position + new Vector3(0.15f, -0.35f, 0.15f);
            Vector3 up = wrist + Vector3.up * 3f;
            App.Rig.Desktop.pitch = -80f; // the head looks at the ceiling too: no surface anywhere
            Fake.SetPose(HandSide.Right, AimRightAt(wrist, up, 0.05f));
            yield return null;
            Fake.SetPose(HandSide.Right, AimRightAt(wrist, up, 0f));
            yield return null;
            yield return null;
            Assert.That(App.Input.Hand(Handedness.Right).Pinching, Is.True);

            drums.Group.gameObject.SetActive(false); // what Layout does before calling StartPlacing
            Mr.StartPlacing();
            Assert.That(Mr.IsPlacing, Is.True);
            Mr.StartPlacing();
            Assert.That(Mr.IsPlacing, Is.True, "a second call while placing changes nothing");
            Assert.That(Rig(RigId.Harp).Group.gameObject.activeSelf, Is.False, "the other rigs hide");

            // Release, pinch again at nothing: placing ends, and the drums Layout hid are back with the rest.
            Fake.SetPose(HandSide.Right, AimRightAt(wrist, up, 0.05f));
            yield return null;
            Fake.SetPose(HandSide.Right, AimRightAt(wrist, up, 0f));
            yield return null;
            Assert.That(Mr.IsPlacing, Is.False);
            Assert.That(drums.Group.gameObject.activeSelf, Is.True, "rigs in the layout come back");
            Assert.That(Rig(RigId.Harp).Group.gameObject.activeSelf, Is.True);

            // Again through StartPlacing, now aiming at the desk: the piano lands there (the placing hand's ray first).
            App.Rig.Desktop.pitch = 0f;
            Vector3 target = desk.EdgeMidpoint - desk.TowardUser * 0.12f;
            Fake.SetPose(HandSide.Right, AimRightAt(wrist, target, 0.05f));
            yield return null;
            Fake.SetPose(HandSide.Right, AimRightAt(wrist, target, 0.05f));
            Mr.StartPlacing();
            yield return null;
            yield return null;
            Assert.That(Mr.PianoReticle.Visible, Is.True);
            Fake.SetPose(HandSide.Right, AimRightAt(wrist, target, 0f));
            yield return null;
            Assert.That(Mr.IsPlacing, Is.False);
            Assert.That(Mr.PlacedByPlayer && Mr.PianoOnTable, "placed on the desk");
            Assert.That(Rig(RigId.Piano).Group.position.y, Is.EqualTo(desk.TopY + MrConstants.TableLift).Within(3e-3f));
        }

        // The placing reads the grab pinch (HandState.GrabPinch*: the thumb-index rule OR Meta's own index pinch), as Layout's
        // grab that starts it does: on the headset the rule alone flickers (the two tip joints sit ~17 mm apart when the
        // fingertips touch). A pinch during the OS system gesture lands nothing.
        [UnityTest]
        public IEnumerator AMetaPinchAloneStartsPlacingAndLandsThePianoOnTheDesk()
        {
            yield return EnterRoom("MeshOffice1");
            Assert.That(TableFinder.TryFindBest(Room, App.Head.position, App.Head.forward, TableQuery.Default, out var desk));
            HandState right = App.Input.Hand(Handedness.Right);
            Vector3 wrist = App.Head.position + new Vector3(0.15f, -0.35f, 0.15f);
            Vector3 target = desk.EdgeMidpoint - desk.TowardUser * 0.12f;
            Fake.SetPose(HandSide.Right, AimRightAt(wrist, target, 0.05f));   // the fingertips apart: no thumb-index pinch
            yield return null;
            Fake.SetMetaPinch(HandSide.Right, 0.95f);
            yield return null;
            yield return null;
            Assert.That(right.Pinching, Is.False);
            Assert.That(right.GrabPinching, Is.True, "Meta's pinch");
            Mr.StartPlacing();
            Assert.That(Mr.IsPlacing, Is.True);
            yield return null;
            yield return null;
            Assert.That(Mr.PianoReticle.Visible, Is.True, "aimed at the desk");
            Fake.SetMetaPinch(HandSide.Right, 0.2f);
            yield return null;
            // The OS system gesture: the pinch state freezes, no landing.
            Fake.SystemGesture = true;
            Fake.SetMetaPinch(HandSide.Right, 0.95f);
            yield return null;
            yield return null;
            Assert.That(Mr.IsPlacing, Is.True, "a pinch during the system gesture is not a landing");
            Fake.SystemGesture = false;
            Fake.SetMetaPinch(HandSide.Right, 0.2f);
            yield return null;
            yield return null;
            Assert.That(Mr.IsPlacing, Is.True);
            Fake.SetMetaPinch(HandSide.Right, 0.95f);
            yield return null;
            Assert.That(Mr.IsPlacing, Is.False, "Meta's pinch lands it");
            Assert.That(Mr.PlacedByPlayer && Mr.PianoOnTable, "placed on the desk");
            Assert.That(Rig(RigId.Piano).Group.position.y, Is.EqualTo(desk.TopY + MrConstants.TableLift).Within(3e-3f));
            Fake.SetMetaPinch(HandSide.Right, 0f);
        }

        [UnityTest]
        public IEnumerator FocusRegainedReloadsTheRoomOnlyWhenItChanged()
        {
            yield return EnterRoom("MeshLivingRoom2");
            int loads = Mr.Loads;
            Mr.OnApplicationFocusChanged(false);
            Assert.That(Mr.Loads, Is.EqualTo(loads), "losing focus does nothing");
            // Back from the system menu with the same room: nothing is reloaded (the reload was a hitch every time).
            Mr.OnApplicationFocusChanged(true);
            yield return null;
            yield return null;
            Assert.That(Mr.Loads, Is.EqualTo(loads), "focus back, the room unchanged: no reload");
            // The device's rooms changed meanwhile (Space Setup from the system menu): reload.
            Mr.SceneChangedProbe = () => System.Threading.Tasks.Task.FromResult(true);
            Mr.OnApplicationFocusChanged(true);
            yield return WaitFor(() => Mr.Loads > loads);
            Assert.That(Mr.Loads, Is.EqualTo(loads + 1), "focus back, the room changed: reload");
            Mr.OnApplicationFocusChanged(true);
            Assert.That(Mr.Loads, Is.EqualTo(loads + 1), "one load at a time");
            Mr.SceneChangedProbe = null;
            yield return WaitFor(() => !Mr.Loading);
            yield return null;
            yield return null;
            Assert.That(Mr.Status, Is.EqualTo(MrStatus.Loaded));
        }

        [UnityTest]
        public IEnumerator ExitRestoresTheVirtualRoom()
        {
            var switches = App.Get<IAmbienceSwitches>();
            switches.Set(AmbienceKind.Fire, true);
            var ambientSky = RenderSettings.ambientSkyColor;
            var sun = RenderSettings.sun;
            yield return EnterRoom("MeshOffice1");
            Assert.That(Mr.PianoOnTable, Is.True);
            int events = MrEvents.Count;
            Mr.Exit();
            Assert.That(Mr.Active, Is.False);
            Assert.That(Mr.IsPlacing, Is.False);
            Assert.That(Mr.Status, Is.EqualTo(MrStatus.Idle));
            Assert.That(MRUK.Instance.EnableWorldLock, Is.False, "world lock only in MR");
            Assert.That(MRUK.Instance.TrackingSpaceOffset, Is.EqualTo(Matrix4x4.identity));
            Assert.That(App.Get<IRoom>().Visible, Is.True);
            Assert.That(switches.Get(AmbienceKind.Fire), Is.True, "fire back as it was");
            Assert.That(((AudioEngine)App.Get<IAudioEngine>()).MixedReality, Is.False);
            Assert.That(App.Get<IAudioEngine>().Source(SourceId.Snow).transform.position, Is.EqualTo(MrConstants.SnowSourceVr));
            Assert.That(App.Rig.HeadCamera.backgroundColor, Is.EqualTo(XrMode.VrBackground));
            Assert.That(RenderSettings.ambientSkyColor, Is.EqualTo(ambientSky));
            Assert.That(RenderSettings.sun, Is.SameAs(sun));
            Assert.That(Mr.Lighting.Sun.gameObject.activeSelf, Is.False, "no realtime shadows in VR");
            Assert.That(Mr.Lighting.CatcherVisible, Is.False);
            Assert.That(Mr.PianoOnTable, Is.False);
            Assert.That(Stand.OnSurface, Is.False, "the stand is back");
            Assert.That(Mr.Hint, Is.Null);
            Assert.That(MrEvents.Count, Is.EqualTo(events + 1));
            Assert.That(MrEvents[MrEvents.Count - 1].Active, Is.False);
            var piano = Rig(RigId.Piano);
            Assert.That(piano.Group.position.y, Is.EqualTo(0.72f).Within(1e-4f), "back at its home height (RestoreHomes)");
            var seat = SeatFrame.From(App.Head);
            Vector3 home = seat.Place(piano.HomeAll);
            Assert.That(new Vector2(piano.Group.position.x - home.x, piano.Group.position.z - home.z).magnitude, Is.LessThan(1e-3f), "then arranged about the player");
            yield return null;
            yield return null;
            Assert.That(MRUK.Instance.Rooms.Count, Is.EqualTo(0), "scene cleared");
        }

        // EnterAR: rig.position / rotation = 0. MRUK's world lock writes the tracking space's world pose with
        // TrackingSpaceOffset (MRUK.cs:729-745), so a rig root left moved by the VR seat fit or swivel would make the head
        // jump when the lock engages.
        [UnityTest]
        public IEnumerator EnteringPutsThePlayerRigBackAtTheOriginForTheWorldLock()
        {
            var rr = App.Rig.RigRoot;
            rr.SetPositionAndRotation(new Vector3(0.6f, 0f, -0.4f), Quaternion.Euler(0f, 45f, 0f)); // fitSeat + a swivel
            yield return null;
            yield return EnterRoom("MeshLivingRoom2");
            Assert.That(rr.position, Is.EqualTo(Vector3.zero), "rig root at the origin");
            Assert.That(Quaternion.Angle(rr.rotation, Quaternion.identity), Is.LessThan(1e-3f));
            Assert.That(MRUK.Instance.EnableWorldLock, Is.True, "world lock in MR");
            Assert.That(MRUK.Instance.TrackingSpaceOffset, Is.EqualTo(rr.localToWorldMatrix), "the offset is the rig root's pose");
            Assert.That(MRUK.Instance.TrackingSpaceOffset, Is.EqualTo(Matrix4x4.identity));
            // Everything was laid out about the head after the reset.
            var seat = SeatFrame.From(App.Head);
            var drums = Rig(RigId.Drums);
            if (!Mr.PianoOnTable)
            {
                Vector3 home = seat.Place(drums.HomeAll);
                Assert.That(Vector3.Distance(drums.Group.position, home), Is.LessThan(1e-3f));
            }
            Mr.Exit();
            Assert.That(MRUK.Instance.TrackingSpaceOffset, Is.EqualTo(Matrix4x4.identity));
        }

        // Layout's anchor(): the piano in the full room, the one instrument in single mode: the catcher and the sun follow it.
        [UnityTest]
        public IEnumerator InSingleModeTheCatcherAndSunFollowTheCurrentInstrument()
        {
            yield return EnterRoom("MeshBedroom3"); // no tables: everything stands on the floor
            float floor = TableFinder.FloorY(Room);
            Layout.Set(LayoutMode.Single, InstrumentKind.Drums);
            yield return null;
            var drums = Rig(RigId.Drums).Group;
            Assert.That(Rig(RigId.Piano).Group.gameObject.activeSelf, Is.False, "single mode: the piano left");
            Pose anchor = App.Get<ILayoutService>().Anchor;
            Assert.That(Vector3.Distance(anchor.position, drums.position), Is.LessThan(1e-4f));
            Assert.That(Mr.Lighting.CatcherVisible, Is.True, "the catcher stays with the drums");
            Vector3 c = Mr.Lighting.Catcher.position;
            Assert.That(new Vector2(c.x - drums.position.x, c.z - drums.position.z).magnitude, Is.LessThan(1e-4f), "under the drums");
            Assert.That(c.y, Is.EqualTo(floor + MrConstants.CatcherLift).Within(2e-3f), "on the floor");
            Assert.That(Mathf.DeltaAngle(Mr.Lighting.Catcher.eulerAngles.y, drums.eulerAngles.y), Is.EqualTo(0f).Within(0.01f));
            Assert.That(Vector3.Dot(Mr.Lighting.Sun.transform.forward, (drums.position - MrConstants.SunPosition).normalized), Is.GreaterThan(0.9999f), "the sun looks at the drums");

            Layout.Set(LayoutMode.All, InstrumentKind.Piano);
            yield return null;
            var piano = Rig(RigId.Piano).Group;
            c = Mr.Lighting.Catcher.position;
            Assert.That(new Vector2(c.x - piano.position.x, c.z - piano.position.z).magnitude, Is.LessThan(1e-4f), "back under the piano");
            Assert.That(piano.position.y, Is.EqualTo(floor + MrConstants.StandHeight).Within(1e-4f), "the piano came back on its stand");
        }

        [UnityTest]
        public IEnumerator SpaceSetupNeedsTheHeadset()
        {
            Assert.That(App.Rig.IsXrActive, Is.False);
            int loads = Mr.Loads;
            MrStatus before = Mr.Status;
            var t = Mr.RunSpaceSetup();
            Assert.That(t.IsCompleted, "refused at once (OVRScene.RequestSpaceSetup is a native call in the editor too)");
            Assert.That(Mr.Loads, Is.EqualTo(loads));
            Assert.That(Mr.Status, Is.EqualTo(before));
            yield return EnterRoom("MeshBedroom3");
            loads = Mr.Loads;
            t = Mr.RunSpaceSetup();
            yield return null;
            Assert.That(t.IsCompleted);
            Assert.That(Mr.Loads, Is.EqualTo(loads), "no reload either");
            Assert.That(Mr.Status, Is.EqualTo(MrStatus.Loaded), "the loaded room stays");
            Assert.That(Mr.Loading, Is.False);
        }

        // App.Shutdown runs the modules in reverse Order, so Layout (100) is gone when MR (70) shuts down: MR puts back only
        // what outlives the App (lights, MRUK, passthrough / clear colour) and neither calls nor notifies other modules.
        [UnityTest]
        public IEnumerator ShuttingDownInMixedRealityRestoresOnlyWhatOutlivesTheApp()
        {
            var mode = RenderSettings.ambientMode;
            var sky = RenderSettings.ambientSkyColor;
            yield return EnterRoom("MeshOffice1");
            Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Trilight));
            var cam = App.Rig.HeadCamera;
            int events = MrEvents.Count;
            int arranges = Layout.Arranges;
            var mr = Mr;
            Light mrSun = Mr.Lighting.Sun;
            Assert.That(RenderSettings.sun, Is.SameAs(mrSun));
            Object.Destroy(Boot.Root); // App.Shutdown, while in mixed reality
            yield return null;
            Assert.That(App.Current, Is.Null, "the app is gone");
            Assert.That(mr.Active, Is.False);
            Assert.That(MrEvents.Count, Is.EqualTo(events), "no MixedRealityEvent to modules that are shutting down");
            Assert.That(Layout.Arranges, Is.EqualTo(arranges), "Layout (already shut down) is not called");
            Assert.That(RenderSettings.ambientMode, Is.EqualTo(mode), "the VR ambient is back");
            Assert.That(RenderSettings.ambientSkyColor, Is.EqualTo(sky));
            Assert.That(ReferenceEquals(RenderSettings.sun, mrSun), Is.False, "the MR sun is no longer URP's main light");
            Assert.That(mrSun == null, "the MR sun is destroyed with the module");
            Assert.That(cam.backgroundColor, Is.EqualTo(XrMode.VrBackground), "the simulated passthrough is off");
            Assert.That(XrMode.PassthroughOn, Is.False);
            Assert.That(MRUK.Instance == null || (!MRUK.Instance.EnableWorldLock && MRUK.Instance.Rooms.Count == 0), "MRUK unlocked and cleared (or gone)");
        }

        [UnityTest]
        public IEnumerator TheMixedRealityTickDoesNotAllocate()
        {
            yield return EnterRoom("MeshLivingRoom2");
            Weather.Cover = 0.3f;
            for (int i = 0; i < 4; i++) yield return null;
            // Warm up, then measure the steady state (catcher, hint card live).
            for (int i = 0; i < 20; i++) { Mr.Tick(FramePhase.MixedReality, 1f / 72f); Mr.Tick(FramePhase.EndFrame, 1f / 72f); }
            Assert.That(() => { var a = new float[1024]; a[0] = 1f; }, Is.AllocatingGCMemory(), "calibration");
            Assert.That(() =>
            {
                for (int i = 0; i < 50; i++)
                {
                    Mr.Tick(FramePhase.MixedReality, 1f / 72f);
                    Mr.Tick(FramePhase.EndFrame, 1f / 72f);
                }
            }, Is.Not.AllocatingGCMemory());
            // And while placing (reticle raycasts every frame).
            Assert.That(Mr.BeginPlacing(1), Is.True);
            for (int i = 0; i < 10; i++) { Mr.Tick(FramePhase.MixedReality, 1f / 72f); Mr.Tick(FramePhase.EndFrame, 1f / 72f); }
            Assert.That(() =>
            {
                for (int i = 0; i < 50; i++)
                {
                    Mr.Tick(FramePhase.MixedReality, 1f / 72f);
                    Mr.Tick(FramePhase.EndFrame, 1f / 72f);
                }
            }, Is.Not.AllocatingGCMemory());
        }

        [UnityTest]
        public IEnumerator LeavingAndComingBackDuringALoadStillLoadsTheRoom()
        {
            MrDevOptions.SceneJson = RoomJson("MeshLivingRoom2");
            var first = Mr.Enter();
            Assert.That(Mr.Loading, Is.True, "the scene load runs over several frames");
            Mr.Exit();
            Assert.That(Mr.Active, Is.False);
            var second = Mr.Enter();
            Assert.That(Mr.Active, Is.True);
            yield return WaitFor(() => !Mr.Loading && first.IsCompleted && second.IsCompleted);
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(Mr.Status, Is.EqualTo(MrStatus.Loaded), "the load that was running serves the new session");
            Assert.That(MRUK.Instance.EnableWorldLock, Is.True);
            Mr.Exit();
            for (int i = 0; i < 2; i++) yield return null;
            Assert.That(MRUK.Instance.Rooms.Count, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator WithoutSimulationEnterIsRefusedOffHeadset()
        {
            MrDevOptions.SimulatePassthroughOverride = false;
            Assert.That(Mr.Available, Is.False);
            var t = Mr.Enter();
            yield return null;
            Assert.That(t.IsCompleted);
            Assert.That(Mr.Active, Is.False, "no passthrough, no mixed reality");
            Assert.That(App.Get<IRoom>().Visible, Is.True);
            Assert.That(MrEvents.Count, Is.EqualTo(0));
        }
    }

    /// <summary>The same entry and exit with the build's own Layout module (the foundation stub here, the real one after
    /// the merge) instead of the contract Layout: MR drives whatever implements ILayoutService.</summary>
    public class MrWithTheBuildsLayoutTests : MrTestRig
    {
        protected override bool UseContractLayout => false;

        [UnityTest]
        public IEnumerator EnterPlacesThePianoOnTheDeskAndExitBringsTheRoomBack()
        {
            Assert.That(App.Get<ILayoutService>(), Is.Not.InstanceOf<ContractLayout>());
            yield return EnterRoom("MeshOffice1");
            var piano = Rig(RigId.Piano);
            Assert.That(Mr.PianoOnTable && Stand.OnSurface);
            Assert.IsTrue(TableFinder.TryFindBest(Room, App.Head.position, App.Head.forward, TableQuery.Default, out var best));
            Assert.That(piano.Group.position.y, Is.EqualTo(best.TopY + MrConstants.TableLift).Within(1e-4f));
            Assert.That(Rig(RigId.Drums).Group.gameObject.activeSelf, Is.True, "Layout placed the rigs around the piano");
            Mr.Exit();
            yield return null;
            Assert.That(App.Get<IRoom>().Visible, Is.True);
            Assert.That(Stand.OnSurface, Is.False);
            Assert.That(piano.Group.position.y, Is.EqualTo(0.72f).Within(1e-4f));
        }
    }
}
#endif
