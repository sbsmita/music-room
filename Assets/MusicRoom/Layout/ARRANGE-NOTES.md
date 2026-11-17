# Move instruments, Exit, the start panel and Meta's pinch (u-arrange, 2026-11-01)

Built in `u-arrange` (merge base `arrange-base` = the integrated project + the palm-summon
hotfix) after the user's first sessions on a Quest: "add exit btn in menu. i am unable to drag all instruments or
single. i should be able to place it according to my comforting level", and earlier "i dont see option to enter vr or
play in my room". The palm-summon hotfix (`Menu/SummonGesture.cs`, `Hands/MetaHandVisual.cs`) is untouched.

## 1. What was built

| Feature | Where | In one line |
|---|---|---|
| Exit | Menu | Home's last row; first poke "Tap again to exit" (3 s), second poke: 150 ms fade, settings saved, `App.Quit()` (`BootOptions.Quit` hook in tests) |
| Move instruments | Layout (+ Core, Menu, Piano, Drums, Guide) | `IArrangeMode`: highlighted, silent instruments; near grab (8 cm, any part) and ray grab (system pointer pose) after an 80 ms debounce; x / y / z and yaw; seated-comfort heights; Done / Reset buttons; saved per layout |
| Meta's pinch | Hands, Core | `HandInput.metaPinching` (strength 0.85 on / 0.5 off, or IsIndexPinching) and the grab pinch `HandState.GrabPinch*` = thumb-index OR Meta's |
| System-gesture guard | Core, Layout | `PointerInput.SystemGestureLike`: a pinch that starts with the palm turned toward the headset never grabs |
| In-play grab | Layout | Reads the grab pinch, dwell stillness 4 cm (was 2.5), the system-gesture guard |
| Start panel | Menu, Guide | At launch the panel in front of the player: "VR Music Room", Play here / Play in my room; Sunny's first-launch tour waits for the answer |
| Sunny's tip | Guide, Audio | New line `tip_arrange` "Pinch an instrument to pick it up." the first time arrange mode turns on; a tour in progress is held instead |

## 2. The numbers and why

**Heights** (`ArrangeConstants`, `LayoutModule.LiftRange`). The app is seated. A comfortable keyboard is at or a little
below the elbow; seated elbow height is the seat plus about 0.18-0.29 m (adult 5th-95th percentile), and seats people
actually use run from a low sofa (0.40 m) through chairs and wheelchairs (0.43-0.50) and beds (0.55) to a bar stool
(0.75). That puts the key tops between about 0.58 and 0.98 m; the home is the standard 0.72 m (an acoustic piano's key
bed is 0.71-0.74 m).
- Piano key tops: **0.55-0.95 m** (17 cm down, 23 cm up). Its stand stretches or shortens from the body's underside so
  its feet stay on the floor (`PianoInstrument.FitStand`, LateUpdate; scale 0.73 at 0.55 m, 1.37 at 0.95 m).
- Drum kit: **-0.17..+0.15 m** off the floor, the snare (0.72 m at home) from 0.55 to 0.87 m, matching the keyboard
  range at the bottom. Real snare stands run about 0.45-0.75 m with thrones 0.46-0.70 m. The kit's stands do not
  lengthen, so the top of the range stops at +15 cm, where the floating kit still reads as "raised"; lowered, the
  bass drum and the stand feet go into the floor (hidden by it).
- Harp and side table: on the floor (a floor harp is played standing on the floor; the table carries the deck and
  switches at their designed height).
- The in-play grab keeps its own lift (-0.45..+0.6 m about the first-grab height), unchanged.

**Grabbing.** Near reach 8 cm from any part's box (keys, heads and strings included: nothing plays meanwhile), which
covers a pinch "on" a part even with the tracker's 1-2 cm error; a debounce of 80 ms (6 frames at 72 Hz) instead of
the 0.3 s still dwell, so a pinch that is moving already still takes hold; the ray keeps the hit distance and pushing
or pulling the hand along the first ray moves the instrument 2.5 x as far in the room (hit distance + 1.5 x the push,
0.2-6 m); the height always follows the hand itself 1:1 (a ray's far end would swing it by the hand's pitch times the
distance). The Done / Reset panel is 0.16 x 0.12 m, 35 cm ahead of the eyes, 15 cm below and 10 cm to the left: inside
an easy seated reach, below the line of sight to the piano, left of the right hand's usual ray.

**Meta's pinch.** Start at strength 0.85, end below 0.5 (or the runtime's IsIndexPinching flag); while the runtime marks
the pinch data unusable (InputStateValid off) the state is held for 0.25 s, then dropped, so a flicker never drops an
instrument and a lost hand never sticks. Like the other pinch state it is held while the hand is untracked or
SystemGestureInProgress is up.

**The system gesture.** Quest raises SystemGestureInProgress for any open palm facing the headset (the palm-summon
hotfix), and a pinch then opens the OS menu. A grab pinch is ignored when it starts with that flag, or with the palm
within 50 degrees of the eyes, under 0.8 m from them and turned back toward the player (horizontal component of the
palm normal against the look direction > 0.5): a palm-down hand above the head faces the eyes too but is not the
gesture (the MR empty-air pinch test caught this).

## 3. Why the in-play grab failed on the headset (review) and what changed

| Suspect | Finding | Change |
|---|---|---|
| Pinch thresholds vs real joint centres | The thumb-index rule (start <= 15 mm, end > 25 mm) compares the OpenXR **tip joint centres**, which sit inside the fingertips: two touching tips are about the sum of their radii apart (7.6 + 9.8 = 17.4 mm at hand scale 1, `HandRestPose.Radius`), above the start threshold. A real pinch therefore starts late (only when pressed hard) and flickers around 25 mm; every flicker ends the pinch and restarts the 0.3 s dwell | The grab reads the grab pinch (thumb-index OR Meta's own index pinch with hysteresis) |
| Dwell stillness vs tracking jitter | The smoothed pinch point may wander only 2.5 cm from where it settled in the first 80 ms; the index tip keeps moving 1-2 cm while a pinch closes, on top of the tracker's millimetre jitter, and an EMA of 60 ms does not hide a centimetre drift | `GrabStill` 0.04 m (a stroke at 0.8 m/s still moves 24 cm in the dwell; `GrabSpeed` 0.5 m/s unchanged) |
| Play guard | 1 s without playing; resting fingers on the harp count as playing. Reasonable, kept; the pinch is now reliable after the second | Kept |
| Handle zones | Only the piano's case, stand and music rest, the kit's shells and stands, the harp's frame; the key space (above the bottom of the keys) never grabs the piano, so most of the case near the keys is out. Correct for play, but hard to find on a first try | Kept; arrange mode grabs any part |
| System gesture | HandInput freezes the pinch while SystemGestureInProgress is up (palm toward the headset); a grab never started there anyway | Added the explicit guard (also for the moment before the flag rises) |
| Lock | A locked layout grabs nothing but the menu | Kept; arrange mode always works |

## 4. Sunny

None of the 22 existing lines (`guide-lines.json`) described how to move instruments, so no existing line
was changed. Following the coordinator and `Docs/11-first-five/sunny-tour.md` (sections 3.2 and 5.3) there is one
new line, **`tip_arrange`**: "Pinch an instrument to pick it up." (the spec's wording, unchanged; it fits: near grabs
are pinches on or near the instrument, and the Done / Reset panel itself says "Pinch one, or point at it and pinch").
- Rendered with the project's own pipeline: the voice maker's `render()` (eSpeak NG 1.52.0
  `en-us+Annie`, 150 wpm, pitch 55, range 70, trim, peak 0.89, 5 ms fades) imported read-only from a scratch wrapper,
  22.05 kHz mono WAV, 2.086 s (the spec's measured value; the same wrapper reproduces `nod_vr` at 2.199 s exactly).
  `Guide/Resources/Voice/tip_arrange.wav` (+ .meta copied from `nod_vr.wav.meta`: Decompress On Load, Preload, Force
  To Mono, Vorbis 0.7), `manifest.json`, `guide-lines.json` (`lineCount` 23 and a `tips` note).
- `GuideLines` (Ids / Texts / ManifestSeconds, `TipArrange`) and the Audio module's `GuideVoice.LineIds` (+1, LineCount
  23: the engine renders and slots it like the others; voice slots 200..222 of 240).
- When: the first time arrange mode turns on and no tour is running, Sunny comes by beside the head, says it, and
  leaves without "Bye!" (`Sunny.Tip`). It is marked seen in `musicroom.tipsSeen` (the spec's key) when the line is over
  or the player makes an arrange grab. A tour in progress is held out of sight and silent instead and carries on after
  Done. The first-launch tour still starts after a tip.
- Not built here (the spec's full tip system): demos for tips, the 8 s re-demo, `tip_arrange_ray`, `tip_arrange_lift`,
  `tip_arrange_done`, `tip_arrange_saved`, classes, rate limits, the Tips list.
- **Spec difference to resolve:** `IMenuPanel.StartPending` makes the first-launch tour wait for the start panel's
  answer (the task's flow: "The choice closes the panel; then Sunny's first-run tour starts as today"). The sunny-tour
  spec (open question 6) instead starts its script under the panel so Sunny can help at 8 s; that needs only Guide's
  `AutoStart` to stop checking `StartPending`.

## 5. On the headset (device checklist, ARCHITECTURE section 19 additions)

30. The start panel opens in front at launch, readable at about 0.5 m, rows easy to poke; "Play in my room" offered
    on Quest 3 / 3S (passthrough pending counts as available) and its choice enters MR; Sunny's tour follows the answer.
31. Exit: double poke within 3 s closes the app cleanly (no audio pop: 150 ms fade of `AudioListener.volume` in ~11
    frame steps); a single poke times out. Relaunch restores the layout and placement.
32. Meta's pinch: PinchStrength 0.85 / 0.5 and IsIndexPinching make grabs reliable; `[Layout] arrange mode on` and
    pinches in the device log; the thumb-index distance at a firm pinch (expected 15-20 mm between joint centres).
33. The system pointer pose (PointerPose with InputStateValid) gives a steady ray from each hand; the ray's
    origin and direction match where the user points; it hides while the palm faces the headset.
34. Near grabs on keys / heads / strings; ray grabs from 1-3 m; the 2.5 x push feels right (or tune `RayPushGain`);
    the 80 ms debounce never takes an instrument by accident.
35. Heights: piano 0.55-0.95 m with the stand on the floor; kit -17..+15 cm (raised, it floats; lowered, it sinks into
    the floor); comfort of the defaults.
36. The Done / Reset panel's reach and size at 35 cm, below and left; the corner brackets' look (since the merge: 4 mm
    warm-white tubes at alpha .42, light gold under a hand or ray, gold held, red blocked, alpha-blended) and the 4.5 mm
    warm-white ray on the Quest display.
37. The OS system gesture (palm toward the face, pinch) never grabs; summoning the palm menu still works.
38. The in-play pinch-and-hold grab now works on the headset (grab pinch, 4 cm stillness).
39. The saved placement comes back on the next launch around the player (the seat frame is the head at the first
    presenting frame; a recentre re-places).

## 6. ARCHITECTURE section 21 entry (draft)

### 2026-11-01, u-arrange: start panel, Exit, "Move instruments", Meta's pinch

After the first headset sessions (the in-play grab never worked on the device; no visible way into mixed reality;
no way to quit). Verified on the Mac (section 7 below); nothing on a headset yet (section 19 items 30-39).

Contract changes (Core and Hands; every new interface member has a default body, so stand-ins compile unchanged):
- `Core/Contracts/Arrange.cs` (new): `IArrangeMode { Arranging; BeginArrange(); EndArrange(); ToggleArrange()
  (default); ResetArrangement(); }`, `ArrangeModeEvent { Active }`, `ArrangeConstants` (near reach .08, debounce .08,
  ray gain 2.5 / 0.2-6 m / idle 1.2, lift dead band .02, piano key tops .55-.95, kit -.17..+.15, buttons .35 ahead /
  .15 below / .10 left, system palm cos .643 / .8 m). Layout implements it (section 3.4: Layout also registers
  `IArrangeMode`).
- Section 4 step 5: no instrument but the Menu is ticked while `IArrangeMode.Arranging` (`App.TickInstruments`).
- Section 9: `MenuScreen.Start`, `MenuScreen.Arrange` (appended); `IMenuPanel.StartPending` (default false). Home rows:
  Lessons, Instruments, Move instruments, Recordings, (Room), (Setup), Show me around, Exit; 5-8 rows from y 132
  (h 100 / 84 / 72 / 62).
- Section 3.2 / 16: `BootOptions.StartPanel` (true; TestBoot false unless `startPanel: true`), `BootOptions.Quit`
  (TestBoot: a counter, `TestBoot.QuitCalls`); `App.StartPanel`, `App.Quit()`, `App.QuitRequests`.
- Section 6.1: `HandMath.MetaPinchStart/End` (.85 / .5), `MetaPinchHoldSeconds` .25, `NextMetaPinch`, `PalmFacesPoint`;
  `HandInput.metaPinching`, `grabPinching`, `grabPinchStart`, `grabPinchEnd`; `FakeHandSource.SetMetaPinch`,
  `SetInputValid`, `SetPointer`.
- Section 6.2: `HandState.GrabPinching / GrabPinchStart / GrabPinchEnd / GrabPinchFor / MetaPinching / PinchStrength`;
  `PointerInput.SystemGestureLike(hand, head, headForward)`.
- Section 8: `GrabConstants.GrabStill` .04 (was .025, kept as `GrabStillBeforeDeviceTest`); the in-play grab reads the grab pinch.
- Section 11: 23 voice lines (`tip_arrange`); `GuideVoice.LineIds` +1; the tour waits for `StartPending`; a tour is held
  while arranging; `Sunny.Tip`.
- Section 18: keys `musicroom.arrange.all`, `musicroom.arrange.single.{piano,drums,harp}` (`AppSettings.ArrangeKey`,
  `GetArrangement`, `SetArrangement`), `musicroom.tipsSeen`.

Module changes: Layout (`LayoutModule.ArrangeMode.cs`, `ArrangeVisuals.cs`, `ArrangementCodec.cs`; placement uses the
saved arrangement; the seat yaw follows the swivel; arrange-mode desktop drag; in-play grab changes), Menu (start panel,
Done / Reset panel as a second `MenuView` at scale 0.5, Exit, rows, icons, exact hover-uniform comparison), Piano (the
stand follows the floor), Drums (no nod kick or stick glows while arranging), Guide (hold, `tip_arrange`, waits for the
start panel), Audio (`GuideVoice.LineIds`).

### Merge notes (three-way against `arrange-base`)

- Likely overlaps with the upgrade: `Core/App/App.cs` (u-exp-hands adds step 8a to `Frame`; this changes only
  `TickInstruments` and adds `Quit`), `Hands/HandInput.cs` (`Derive`), `Hands/FakeHandSource.cs`, `Audio/Synths/GuideVoice.cs`
  (u-engine; keep `tip_arrange` as the 23rd id), `Menu/*` (E1 makes the menu an `IContactSurface`; the Done / Reset panel
  is a second face that may want the same), `Drums/DrumsModule.cs` (E2 / E7), `Guide/*` (the sunny-tour spec rewrites the
  tour: keep `tip_arrange`, `Sunny.Tip`, the arrange hold and the `StartPending` wait or its replacement).
- `MetaHandSource.cs` is untouched (it already fills PinchStrength, Pinches and PointerPose).
- Keep the palm-summon hotfix (`SummonGesture.SystemPinch`, `MetaHandVisual`) as is.
- `Assets/Resources/MetaXRAcousticMaterialMapping.asset` appeared in this clone on the first editor run (Meta XR Audio
  creates it); it is not part of this work.

## 7. Verification record

2026-11-01 04:15-04:28 on the Mac (Unity 6000.3.27f1, batchmode, this clone only, `Tools/unity.sh`; logs in the clone's
`Logs/final-*`). Baseline of the untouched clone first (`Logs/baseline`): test-edit 564/564, test-play 195/195.

| Step | Result |
|---|---|
| `compile` | 0 errors, 0 warnings in `Assets/MusicRoom/**` (also none in any later log) |
| `test-edit` | 581 / 581 (+17: `MenuArrangeExitStartTests` 10, `MetaPinchTests` 3, `ArrangementCodecTests` 4; 6 existing tests updated for the deliberate changes: Exit row geometry and labels, the Instruments footer, `GrabStill`) |
| `test-play` | 221 / 221 (+26: `ArrangeModeTests` 13, `ArrangeIntegrationTests` 6, `GuideArrangeTipTests` 4, `MetaPinchInputTests` 3; `AudioEngineTests` reads 23 voice lines) |
| `test-play --gpu` | 243 / 243 (+27: the above and `ArrangeShotsTests`; `MenuScreenshotTests` counts 8 home rows in MR with Setup). Head camera in arrange mode: 29 draws (budget 150) |
| `build` | `apkBytes=65211506` (62.2 MiB), 0 errors, 5 warnings (the known two MR snow shader `pow` notes and three IL2CPP notes); 35 baked textures already there; permissions HAND_TRACKING, USE_ANCHOR_API, USE_SCENE, BOUNDARY_VISIBILITY, no INTERNET |

Screenshots (`Builds/Shots/arrange/`): `start_panel`, `menu_home`, `arrange_seat` (highlighted room,
buttons, Sunny's tip), `arrange_buttons`, `arrange_overview`, `arrange_ray`, `arrange_held`, `arrange_piano_raised`,
`exit_confirm`. Fixed after looking at them: the start panel's "Play in my room" was cut by the note column (notes
dropped), the buttons' second line shrank too small (shortened), two cameras were badly placed. Note: the existing GPU
tests of the other modules also write their screenshots to `Builds/Shots/<module>/` (menu, integration, guide, room,
piano, drums, harp, lessons, mr), so those folders now hold this clone's images.

Not verified: anything on a headset (section 5).

## 8. Merged into the upgraded project (2026-11-01)

Merged three-way into `vr-music-room-unity` (after the 10-upgrade) together with u-looper; the record is ARCHITECTURE.md
section 21 ("merge of u-arrange and u-looper") and 20.10. What changed on the way in:
- `tip_arrange` stays the 23rd line (index 22); the looper's 15 lines follow it (38 in all, one order in `GuideLines.Ids`,
  `GuideVoice.LineIds`, `guide-lines.json` and the voice manifest).
- One tip mechanism: this clone's `Sunny.Tip` and the looper's were merged (`Sunny.Tip` returns bool, state `Tip`,
  `InTip` until gone; `GuideModule.StartTip` / `TrackTip`). A tip is now seen when heard to the end (the looper's rule)
  or at once on an arrange grab (this clone's rule); a cut tip is said again next time.
- The Menu keeps the upgrade's Classic sound row (Instruments, sixth row) beside this clone's rows and footer.
- The other pinch actions moved to the grab pinch as section 3 suggested: the hi-hat stand, a held stick, the one-hand
  seat turn, the two-hand summon and the MR placing pinches (plus the system-gesture guard on the stick pick-up, the
  seat turn, the summon and the MR placing pinch). A Meta-only pinch could start MR placing (Layout's grab) but never
  land the piano before (MR read the thumb-index pinch).
- The highlight is calmer and warmer (section 5 item 36): warm white, light gold, gold, red; alpha-blended; thinner.
