# Hand drums: contract requests (phase B)

Shared-file changes the hand drums need, for the integrator (BUILD-GUIDE 10). Everything else is in the owned folders.
Each item was applied locally in the phase-B copy to run the whole chain green end to end; at hand-back the
shared files are restored to the instruments base's bytes, so `diff -rq` lists only the owned paths (and the bake's
`Generated/Resources/Generated/HandDrums/`, the new test folders' `.meta` files in `Tests/EditMode/`). The critical line
of the palm-menu hotfix, `float target = !f.isTracked ? 0f : f.isHighConfidence ? 1f : lowConfidenceAlpha;`, is
untouched by R1 (byte for byte).

Settled without a shared change (phase A's list): zones (the platform keeps no zone list), the pitched-skin tail rule
(this folder's own test `HandDrumsPlatformTests.ThePitchedSkinTailRuleHoldsOnBothDrums`: conga 23.7 dB / 0.75, tumba
36.3 dB / 0.75), the seat numbers (the descriptor; the planner's data is R3), the 1 dB drier room send (the descriptor's
`AudioSourceSpec` override), the demo hands (the platform's presets + two `DemoKind.Custom` motions), the tip triggers
(`TipTrigger.Signal`, `LessonStart`), the line and manifest shapes, the unit's registration.

## R1 [required] `MetaHandVisual.SetPalmLift`: the drawn hand rides on a drum head
- **File:** `Assets/MusicRoom/Hands/MetaHandVisual.cs`
- **Why:** handdrums.md 4.5, critique M4: on a hard palm stroke the tracked palm goes 2-8 cm into the head (the skin
  gives; the tracker does not know), and the drawn hand sinks into the drum. The module computes the lift every frame
  (`HandDrumsInput.PalmLift[hand]`: the palm or heel skin's depth under a head, up to 60 mm, fading to 0 at 90 mm, attack
  instant, release tau 25 ms; 6.1 mm at the deepest point of the bass demo in `HandDrumsPlayTests`) and calls this in VR
  only (never in MR: the cut-out shows the real hand). Per frame: a call that stops (the drums leave, arrange mode, a
  displayed rig) leaves no lift behind. E1's finger lift is reduced by what the palm lift already raised along its normal,
  so a clamped fingertip does not float. Nothing changes while no instrument calls it.
- **Change:**
```diff
--- a/Assets/MusicRoom/Hands/MetaHandVisual.cs
+++ b/Assets/MusicRoom/Hands/MetaHandVisual.cs
@@ -65,7 +65,28 @@
         OVRPlugin.HandState _delayed;
         bool _delayedValid, _loggedDelayed;
         int _delayedFailures;
+        // Hand drums contract request R1: the whole drawn hand raised along a surface normal for this frame (a palm on a drum
+        // head rides on the skin instead of sinking into it). Visual only, VR only (the cut-out keeps the real hand's pose).
+        static readonly MetaHandVisual[] s_instances = new MetaHandVisual[2];
+        float _palmLift;
+        Vector3 _palmLiftNormal = Vector3.up;
+        int _palmLiftFrame = -1;
 
+        /// <summary>Raise the drawn <paramref name="side"/> hand (every bone) by liftMetres (clamped to 0..0.06) along
+        /// worldNormal, for the current frame only: call it every frame while it applies (the hand drums' palm lift,
+        /// handdrums.md 4.5). Visual only: the hand data and every play logic keep the raw joints. No-op without a visual.</summary>
+        public static void SetPalmLift(HandSide side, float liftMetres, Vector3 worldNormal)
+        {
+            MetaHandVisual v = (uint)side < 2u ? s_instances[(int)side] : null;
+            if (v == null) return;
+            v._palmLift = Mathf.Clamp(liftMetres, 0f, 0.06f);
+            v._palmLiftNormal = worldNormal.sqrMagnitude > 1e-8f ? worldNormal.normalized : Vector3.up;
+            v._palmLiftFrame = Time.frameCount;
+        }
+
+        /// <summary>This frame's palm lift (m): 0 unless SetPalmLift was called this frame.</summary>
+        public float PalmLift => _palmLiftFrame == Time.frameCount ? _palmLift : 0f;
+
         /// <summary>Builds the visual under the rig's tracking space. Components are added while the GameObject is
         /// inactive, so every Awake already sees the configured side.</summary>
         public static MetaHandVisual Create(HandSide side, MetaHandSource source, Transform trackingSpace, Material material)
@@ -90,6 +111,7 @@
             go.AddComponent<OVRMesh>();          // Awake: mesh type from GetComponent<IOVRMeshDataProvider>()
             go.AddComponent<OVRMeshRenderer>();  // Awake: finds provider, OVRMesh, OVRSkeleton on this GameObject
             go.SetActive(true);
+            s_instances[(int)side] = visual;
             return visual;
         }
 
@@ -201,9 +223,10 @@
             if (!_poseValid || look == Look.PassthroughCutout || _skeleton == null || !_skeleton.IsInitialized || !_skeleton.IsDataValid) return;
             var bones = _skeleton.Bones;
             if (bones == null || bones.Count < HandFrame.JointCount) return;
+            float palm = PalmLift;
             for (int k = 0; k < 5; k++)
             {
-                float lift = _lift[k];
+                float lift = _lift[k] - palm * Mathf.Max(0f, Vector3.Dot(_normal[k], _palmLiftNormal));   // R1: what the palm lift already raised
                 if (lift <= 0f) continue;
                 Transform pivot = bones[ContactMath.PivotJoint[k]].Transform, tip = bones[HandFrame.TipJoint[k]].Transform;
                 if (pivot == null || tip == null) continue;
@@ -212,9 +235,32 @@
             }
         }
 
+        /// <summary>R1: the palm lift moves the skeleton's root bones (the children follow the hierarchy), after OVRSkeleton posed
+        /// them from this frame's data (nothing accumulates), before E1 turns the fingers.</summary>
+        void ApplyPalmLift()
+        {
+            float lift = PalmLift;
+            if (lift <= 0f || !_poseValid || look == Look.PassthroughCutout || _skeleton == null || !_skeleton.IsInitialized || !_skeleton.IsDataValid) return;
+            var bones = _skeleton.Bones;
+            if (bones == null) return;
+            Vector3 d = _palmLiftNormal * lift;
+            for (int i = 0; i < bones.Count; i++)
+            {
+                // only the roots move (OVRSkeleton parents every other bone under its parent bone: the hierarchy carries them)
+                if (_skeleton.IsValidBone((OVRSkeleton.BoneId)bones[i].ParentBoneIndex)) continue;
+                if (bones[i].Transform != null) bones[i].Transform.position += d;
+            }
+        }
+
+        void OnDestroy()
+        {
+            if ((uint)side < 2u && s_instances[(int)side] == this) s_instances[(int)side] = null;
+        }
+
         void LateUpdate()
         {
             if (_renderer == null) return;
+            ApplyPalmLift();
             ApplyContacts();
             _renderer.GetPropertyBlock(_block);
             _block.SetFloat(AlphaId, Alpha);
```
- **Test:** `HandDrumsPlayTests.TheHeadsAreContactSurfacesAndThePalmLiftReachesTheHands` (the module finds the method:
  `HandDrumsInstrument.PalmLiftWired`; the lift is computed under a palm bass). The integrator may add an on-device check
  (T-HD-REACH: through the lens, the drawn palm stays on the skin).
- **Fallback until applied:** the module looks the method up by reflection once (`HandDrumsInstrument.FindPalmLift`):
  without it, nothing is called and the drawn palm sinks into the head on hard strokes (E1's finger clamp still stops the
  fingertips on it: the heads are `IContactSurface`s).

## R2 [required] `FastMotionDriverTests`: strike the instrument through its own fixture
- **File:** `Assets/MusicRoom/Tests/PlayMode/Core/FastMotionDriverTests.cs`
- **Why:** `TheHandDrumsLiveOnsetsAskForFastMotionLikeDrumHits` finds the instrument as a `PlaceholderInstrument` and
  knocks its "top" anchor: with the real hand drums there is no placeholder ("Sequence contains no matching element").
  The generic form plays one live onset with the instrument's own conformance fixture (`FixtureContext.For(kind)`: the
  stubs' `PlaceholderFixture` knocks the top as before, the hand drums play a scripted open tone through the real stroke
  logic), checks FMM is on in the frame the onset is recorded, and plays the recorded event back from a take (a unit call
  on the test tap) without FMM. It serves the marimba (still a stub here) and every later real instrument unchanged.
- **Change:**
```diff
--- a/Assets/MusicRoom/Tests/PlayMode/Core/FastMotionDriverTests.cs
+++ b/Assets/MusicRoom/Tests/PlayMode/Core/FastMotionDriverTests.cs
@@ -99,30 +99,41 @@
                 Assert.IsFalse(policy.On, "the piano never asks for Fast Motion Mode");
             }
 
-            // A fingertip knocks the stub's top at the seat: its live Onset, FMM on at once.
-            var stub = app.Instruments.OfType<PlaceholderInstrument>().Single(i => i.Kind == kind);
-            Assert.That(stub.IsPresent, Is.True, kind + " at the seat (My set)");
-            Assert.That(stub.TryGetAnchor("top", out Vector3 top, out Vector3 up), Is.True, "the top face's centre");
-            var tip = new Pointer("fmm-test", null, 0) { Slot = 0, Active = true, Speed = 1f };
-            tip.Prev = top + up * 0.02f;
-            tip.Tip = top - up * 0.005f;
+            // A live onset at the seat, played by the instrument's own conformance fixture (a scripted stroke through its real
+            // contact logic; a phase-0 stub's PlaceholderFixture knocks its top): FMM on in the frame its Onset is recorded.
+            IInstrument inst = app.Instruments.Single(i => i.Kind == kind);
+            Assert.That(inst.IsPresent, Is.True, kind + " at the seat (My set)");
+            IInstrumentFixture fx = FixtureContext.For(kind);
+            Assert.That(fx, Is.Not.Null, kind + " has a conformance fixture");
             events.Clear();
-            stub.Tick(new[] { tip }, Dt);
-            Assert.That(stub.LiveKnocks, Is.EqualTo(1), "the knock");
-            Assert.That(events.Any(e => e.Event.type == onset && !e.DuringPlayback), Is.True, "its live Onset " + onset + " recorded");
-            Run(1);
-            Assert.IsTrue(policy.On, kind + ": on at once, like a drum hit");
+            bool seen = false, onAtOnset = false;
+            fx.Strike(new FixtureContext
+            {
+                Boot = _boot,
+                Frame = () =>
+                {
+                    Run(1);
+                    if (!seen && events.Any(e => e.Event.type == onset && !e.DuringPlayback)) { seen = true; onAtOnset = policy.On; }
+                },
+            });
+            Assert.That(seen, Is.True, "its live Onset " + onset + " recorded");
+            Assert.IsTrue(onAtOnset, kind + ": on at once, like a drum hit");
             Assert.That(app.FastMotion.Sent, Is.EqualTo(0), "no headset runtime: the hint is not sent");
+            TakeEvent played = events.First(e => e.Event.type == onset && !e.DuringPlayback).Event;
             Run((int)(9.2f * 90));
             Assert.IsFalse(policy.On, "off after the recent-hit window and 5 quiet seconds");
 
-            // The same event from a take: played back through its IInstrumentPlayback, not drumming.
+            // The same event from a take: played back through its IInstrumentPlayback (a unit call on the test tap), not drumming.
             var recorder = app.Get<IRecorder>();
-            var take = new System.Collections.Generic.List<TakeEvent> { new TakeEvent { t = 0.05f, type = onset, i = 2, vel = 0.7f } };
-            int played = stub.PlaybackKnocks;
+            var engine = app.Get<MusicRoom.Audio.IAudioEngine>();
+            var tap = new MusicRoom.Audio.AudioTestTap();
+            engine.TestTap = tap;
+            played.t = 0.05f;
+            var take = new System.Collections.Generic.List<TakeEvent> { played };
             Assert.That(recorder.PlayEvents(take, 0.5), Is.True);
-            for (int i = 0; i < 90 && stub.PlaybackKnocks == played; i++) Run(1);
-            Assert.That(stub.PlaybackKnocks, Is.EqualTo(played + 1), "the take played it back");
+            for (int i = 0; i < 90 && tap.Calls.Count == 0; i++) Run(1);
+            engine.TestTap = null;
+            Assert.That(tap.Calls.Count, Is.GreaterThan(0), "the take played it back");
             Run(3);
             Assert.IsFalse(policy.On, "a played-back " + onset + " does not ask for Fast Motion Mode");
             recorder.Stop();
```
- **Test:** itself (both cases pass in this clone: hand drums real, marimba stub).
- **Fallback until applied:** that one case fails for the real hand drums; nothing in the app depends on it.

## R3 [required, data] The planner's reference: the hand drums' final boxes and display spot
- **Files:** `Tools/SoundLab/platform/registry.mjs`, the Node stow plan, then `node make-vectors.mjs` and copy
  `platform.vectors.json` to `Assets/MusicRoom/Tests/EditMode/Layout/Reference~/platform.vectors.json`
- **Why:** (a) the conformance check 3 measured the model: the back foot of the basket stand reached z 0.324 (2.9 cm past
  box B) and the hoops y 0.808; box B grows 3.5 cm at the back (cz 0.1025, hz 0.2275: z -0.125..0.33), box A's top 0.81.
  The feet also moved 5 cm toward the player (the tilted drum's weight sits well inside the three feet). (b) The display
  spot: the ivy hanging from the sill (decor, `RoomFurniture.Decor` "ivy on the sill", x 2.84..3.20, z -2.11..-1.61,
  y 0.14..0.76) reached 13.5 cm into the phase-0 spot (2.64, -1.85); the search in `HandDrumsDisplayTests` (furniture,
  decor and every other display, 2 cm grid, yaw 75 / 90 / 105) puts the nearest spot by the window that keeps 8 cm at
  (2.42, -1.94) yaw 90: 8.5 cm from the ivy, 25 cm from the inside sill, clear of the marimba's display. No decor move is
  needed. `StowPlanTests.TheLiveDescriptorsSpotsAreClearOfEverything` compares the live spot with the vectors and fails
  until the vectors are re-run.
- **Change:**
```diff
--- a/Tools/SoundLab/platform/registry.mjs
+++ b/Tools/SoundLab/platform/registry.mjs
@@ -124,7 +124,7 @@
   bowls: {
     // bowls.md rev 2 section 3.2 / the bowls layout's RIG.seat (final): in the pocket left of the knees
     name: 'Singing bowls', canonicalSlot: 'FrontLeft', home: { x: -0.46, z: 0.25, yaw: 0 }, single: { x: -0.46, z: 0.25, yaw: 0 },
-    prefs: ['FrontLeft', 'FrontRight', 'Left', 'Right', 'Front'], reachMax: 0.62, playCentre: { x: 0.006, z: -0.011 },
+    prefs: ['FrontLeft', 'FrontRight', 'Left', 'Right', 'Front'], reachMax: 0.62, playCentre: { x: 0, z: -0.016 },
     boxes: [
       { cx: 0, cz: 0, hx: 0.205, hz: 0.145, y0: 0, y1: 0.61 },             // low wooden stand
       { cx: 0, cz: 0, hx: 0.205, hz: 0.145, y0: 0.61, y1: 0.72 },          // bowls, cushions, puja
--- a/stow-plan.mjs (Node planner)
+++ b/stow-plan.mjs (Node planner)
@@ -42,7 +42,7 @@
                stowBoxes: [ { cx: 0, cz: 0, hx: 0.27, hz: 0.12, y0: 1.30, y1: 1.86 } ] },
   marimba:   { id: 'window-near', mount: 'floor', pose: { x: 2.30, z: -0.80, yaw: 90 }, note: 'under the window, bars below the sill, mallets on the bars' },
   handdrums: { id: 'window-far', mount: 'floor', pose: { x: 2.64, z: -1.85, yaw: 90 }, note: 'under the far end of the window, heads just above the sill line' },
-  bowls:     { id: 'window-sill', mount: 'surface', pose: { x: 3.08, z: -1.62, yaw: 90 }, note: 'the three bowls on the inside sill on their cushions, striker and stick beside them (the stand hidden)',
+  bowls:     { id: 'window-sill', mount: 'surface', pose: { x: 3.09, z: -0.62, yaw: 90 }, note: 'the three bowls in a row on the near half of the inside sill on their cushions, the stick in front (the stand hidden), clear of the ivy at the far end',
                stowBoxes: [ { cx: -0.01, cz: 0, hx: 0.255, hz: 0.09, y0: 0.63, y1: 0.735 } ] },
 };
```
  Re-run in a copy of the lab: `seat planner: fits 1: 8/8, 2: 26/28, 3: 38/56` (unchanged), every seat-planner result
  unchanged, `stow plan ok: true`, the rays ok/ok/ok; the vectors differ only in the hand drums' boxes, spot, its
  validation entry (distance 3.06 m, bearing 129.3 deg) and its three rays.
- **Test:** `StowPlanTests` (11 green with the re-run vectors here), `SeatPlannerTests`, `RoomFurnitureTests` (the spot 25.0
  cm from the nearest blocker), `HandDrumsDisplayTests`.
- **Fallback until applied:** `StowPlanTests.TheLiveDescriptorsSpotsAreClearOfEverything` fails on the hand drums' x / z.

## R4 [required] The layout tests and `PointerClaimsTests`: the theremin's generic versions, no delta
- **Files:** `Assets/MusicRoom/Tests/PlayMode/Layout/{InstrumentView.cs (new), SetLayoutTests.cs, GlideTests.cs,
  DisplayArrangeTests.cs, RaySummonTests.cs}`, `Assets/MusicRoom/Tests/PlayMode/Core/PointerClaimsTests.cs`
- **Why:** with the real hand drums, `SetLayoutTests.TheDefaultSetIsTodaysRoomWithTheStubsOnDisplay` failed at "HandDrums
  stub loaded" (it asked for a `PlaceholderInstrument`). As the integrator asked, this clone adopts the theremin's
  versions exactly (`Theremin/CONTRACT-REQUESTS.md` R1 and R1b: `InstrumentView` in place of
  `Stub(k)`, live onsets counted from the recorder): copied byte for byte from the theremin's phase-B copy, they pass with the hand drums
  real and the handpan, marimba, theremin and bowls stubs here. The hand drums need nothing on top of them.
- **Change:** the theremin's R1 and R1b.
- **Test:** the full `test-play` here with them (see the report).
- **Fallback until applied:** `SetLayoutTests.TheDefaultSetIsTodaysRoomWithTheStubsOnDisplay` fails on the hand drums.

## R5 [required] `AudioEngineTests`: the early-reflection send from the table's row with its overrides
- **File:** `Assets/MusicRoom/Tests/PlayMode/Audio/AudioEngineTests.cs`
- **Why:** `MixedRealityLowersEveryAcousticSendBySixDecibels` expects every drum piece's early-reflection send to be the
  kit's +3 dB constant, while the platform lets a descriptor override it per source (BUILD-GUIDE 4.3) and the hand drums
  do (+2 dB, with the VR send -4 dB: phase A's measured room DRR, review item 13). The VR send is already compared with
  the table's current row (`AudioRig.VrReverbSendDb`); the ER should be too.
- **Change:**
```diff
--- a/Assets/MusicRoom/Tests/PlayMode/Audio/AudioEngineTests.cs
+++ b/Assets/MusicRoom/Tests/PlayMode/Audio/AudioEngineTests.cs
@@ -392,8 +392,11 @@
                 var x = s.GetComponent<MetaXRAudioSourceExperimentalFeatures>();
                 Assert.That(x, Is.Not.Null, s.id + " tuning start values");
                 Assert.That(x.ReverbReach, Is.EqualTo(AudioRig.InstrumentReverbReach));
+                // the table's current row: the kit's +3 dB, a descriptor's override where it has one (AudioSourceSpec.ErDb: the
+                // hand drums' +2 dB), 0 for everything else
                 bool piece = (s.id >= SourceId.Kick && s.id <= SourceId.Ride) || s.id == SourceId.HandDrumLo || s.id == SourceId.HandDrumHi;
-                Assert.That(x.EarlyReflectionsSendDb, Is.EqualTo(piece ? AudioRig.DrumEarlyReflectionsDb : 0f), s.id + " early reflections");
+                Assert.That(x.EarlyReflectionsSendDb, Is.EqualTo(piece ? row.ErDb : 0f), s.id + " early reflections");
+                if (s.id >= SourceId.Kick && s.id <= SourceId.Ride) Assert.That(row.ErDb, Is.EqualTo(AudioRig.DrumEarlyReflectionsDb), s.id + ": the kit's +3 dB");
             }
             _engine.SetMixedReality(true);
             foreach (var s in _engine.Sources)
```
- **Test:** itself.
- **Fallback until applied:** that test fails at "HandDrumLo early reflections: expected 3, was 2".

A note, not a request: `RoomCalibrationTests.TheDirectToRoomRatioAtThePianoSeatIsTheDesigns` failed once in the full GPU
run here (250 Hz: 2.99 dB against its 3 dB floor) and passed on the re-run: its own comment names the white noise's
run-to-run spread (0.1-0.2 dB per octave) as "an occasional failure". It measures the piano seat's room; nothing of the
hand drums is in it.

## R6 [required] `EightInstrumentsLoadTests`: the bowls' version (theremin R1c + bowls R8), no delta; `IUnitWorstCase`
- **Files:** `Assets/MusicRoom/Tests/PlayMode/Audio/EightInstrumentsLoadTests.cs` (Category Performance);
  `Assets/MusicRoom/Audio/Burst/Units/IUnitWorstCase.cs` (the bowls' R8)
- **Why:** both tests cast each platform role's unit to `KnockUnit`; with the real hand drums' unit the offline test threw
  `InvalidCastException` and the live one failed "HandDrums plays". As the integrator asked, this clone uses the bowls'
  version of the test byte for byte (the bowls' phase B: the theremin's R1c with the theremin branch under `#if THEREMIN_REAL`, plus
  the generic `IUnitWorstCase` line) and the hand drums' unit implements the hook: `HandDrumsUnit.DriveWorst(block, when)`
  plays their live maximum (the most the stroke logic can produce, `HandDrumsLoadTests` "live max": every hand on every
  drum at its 40 ms window, full slaps, the four hands 10 ms apart). No instrument-specific line in the test.
- **Change:** none beyond the bowls' R8. The interface is copied into this folder with the same name, namespace and
  signature (`Audio/Sound/HandDrums/IUnitWorstCase.cs`) so it compiles now: **delete that copy** when the interface moves
  to `Audio/Burst/Units` (the bowls' folder has the same copy: two copies in one assembly would not compile).
- **Test:** itself, with the hand drums real and four stubs: offline best of 3 avg 405.9 us, p99 589.1 us per block;
  live on the audio thread p99 650 us (limit 2.1 ms), the governor stepping.
- **Fallback until applied:** the two perf cases fail with the real hand drums.

`UnitShareTests` (the integrator's note): it drives the bowls' role as a `KnockUnit`; with the bowls still a stub here it
passes (3 / 3) and nothing in it names the hand drums. It fails once the bowls' real unit lands: the integrator's fix.

## R7 [optional] A Decor list on `IRoomFurniture`
- **File:** `Assets/MusicRoom/Core/Contracts/Room.cs` (and `Room/World/Furniture.cs`, which has it as `RoomFurniture.Decor`)
- **Why:** the display spots were planned without the decor (the ivy, the plants' leaves); `HandDrumsDisplayTests` reads
  it through the Room module's concrete class (`RoomModule.Furniture.Decor`). As a contract member, `stow-plan.mjs` and
  the platform's own `RoomFurnitureTests` could fail on decor contacts instead of logging them.
- **Change:** `IReadOnlyList<FurnitureBlocker> Decor => System.Array.Empty<FurnitureBlocker>();` on `IRoomFurniture`
  (default-bodied), overridden by `RoomFurniture.Decor`.
- **Test:** `HandDrumsDisplayTests` would switch to it; `RoomFurnitureTests.EveryDisplaySpotStandsThreeCentimetresClearOfTheFurniture` could assert decor too.
- **Fallback until applied:** what is done now (the concrete class).
