# handpan: review items the revision never saw

The design workflow passed only the first 12,000 characters of the independent review to the reviser, so the items below were never addressed. The build agent must handle each one: fix it, or explain in its report why not, with evidence. Low-severity items are optional. Full review: critiques/handpan-critique.json; revision report: critiques/handpan-revision.json.


## Item 8 [medium] spec

**what:** The meaning of sampleDt is ambiguous for the Unity implementation, and the wrong reading makes every tap about 3 times too soft. handpan-interaction.mjs divides the frame-to-frame height change by frame.sampleDt. The 30 Hz test vector models a 30 Hz pose stream, where poses repeat every 3 frames. In Unity the predicted pose moves every frame, while HandFrame.sampleInterval is the tracker interval: about 33 ms at the LOW rate the handpan keeps. An implementer filling sampleDt from HandFrame (where the palm and flags also come from) would get vn about 3 times too low at 90 Hz, so a 0.6 m/s tap plays at about vel 0.2.

**evidence:** - handpan-interaction.mjs:67 `dtS = max(F.sampleDt or dt, 1 ms)`.
- interaction-tests.mjs:105 (tracker-30hz repeats each pose for 3 frames).
- 02-meta-hands.md §6: 'In Unity the predicted pose moves every frame'.
- Hands/README.md: sampleInterval is 'about 16.7 ms with FMM, 33 ms without'.
- DrumKit.cs:203 uses Pointer.SampleDt, which PointerInput.cs:96-101 learns as the gap between position changes (about the frame dt).

**fix:** In spec 6.2 and HandpanPlay, state: positions = Pointer.Prev / Pointer.Tip, dt = DrumMath.SampleSeconds(Pointer.SampleDt, dt), and never HandFrame.sampleInterval. Or use vn = -n · Pointer.TrackerVel. Take the palm centre and normal from HandMath on HandInput.Instance.Get(side). Replace the tracker-30hz vector with a predicted-pose version that moves every frame, and add an EditMode assert that the velocity matches the 90 Hz case within 0.05.


## Item 9 [medium] interaction

**what:** The near fields are too low for larger players, and the first lesson uses them. A3 and C4 sit just above the thighs (sweet spot 0.645 m, outer edge 0.604 m) and clear a 95th-percentile seated thigh by -1.5 cm. A finger that overshoots the virtual steel, which is normal in VR, will reach the leg. If the thigh is higher than the field, the pad never crosses the surface (h <= 0) and the tap is lost. The designer's sweep never tried a higher rim. Raising it fixes clearance and actually shortens the far reach, because the shoulder is above the instrument. In MR, the stand's legs (z 0.20-0.62 m) are also drawn through the real lap and shins.

**evidence:** critic/c4-geometry.mjs (thigh top = seat height + thigh clearance ≈ 0.56 / 0.61 / 0.66 m for the 5th-percentile female, 50th and 95th-percentile male):
- Today, (0, 0.68, 0.38) at 28°: sweet-spot clearance +8.5 / +3.5 / -1.5 cm, far-apex reach 0.578 m.
- Proposed, (0, 0.74, 0.38) at 28°: +14.5 / +9.5 / +4.5 cm, reach 0.557 m (edge points 0.594 m), near rim 0.616 m.
The designer's alternatives in geometry.mjs only span rim heights 0.62-0.71 m.

**fix:** Change PLACE.centre from (0, 0.68, 0.38) to (0, 0.74, 0.38) at 28°, or to (0, 0.74, 0.40) at 24° for more room at the belly (reach 0.586 m). Raise the stand ring centre by 6 cm. Regenerate geometry.json and interaction-vectors.json, and update spec 6.5 and 7.1. Keep device check 6. In MR, either draw the stand between the knees or flag it in check 7.


## Item 10 [low] interaction

**what:** Fast Motion Mode stays off for the handpan, but its taps are exactly the fast fingertip strokes that the drums turn it on for. At the default ~30 Hz camera rate, a 4 cm, 60-100 ms tap gets only 2-3 tracker samples, and prediction has to invent the bottom of the stroke. Meta warns that FMM adds jitter, so the right setting has to be measured, not assumed.

**evidence:** experience.md E4: at about 30 Hz, 'prediction has to invent the bottom of every stroke'. Hands/README.md: a 33 ms sample interval without FMM. A 0.6 m/s tap moves 2 cm between samples. Spec 11.5 only asserts that FMM jitter is worse.

**fix:** Run device checks T-HP-FEEL and T-HP-VEL twice, with FMM off and on. Log the velocity SD of 20 repeated taps, missed taps and false strikes while hovering. If FMM wins, add 'Single mode with Handpan current' to FastMotionPolicy.want.


## Item 11 [low] lessons

**what:** Sunny's tip `tip_handpan_gentle` says 'Softer taps ring longer and sound sweeter.' That is false for this model, where T60 does not depend on velocity, and the published measurement says the opposite. The tip also does not address the real beginner problem of lifting the finger. The 9-10-word lines may exceed the tour's 3.6 s limit at eSpeak's 150 words per minute, and no durations are given.

**evidence:** Spec 2.2: T60 is fixed per lane. Wessel 2008: 'the loud note rings longer than the soft one'. sunny-tour.md section 5: lines must be under 12 words and under 3.6 s (eSpeak NG Annie, 150 wpm).

**fix:** Change the text to 'Gentle taps sound sweeter. Hard taps get bright.' (8 words), and add `tip_handpan_lift` (see the press-and-hold issue). Render all 9 lines with export-voice-wav-unity.mjs (sunny-tour.md 5.4) and record `seconds` / `lineSeconds`, each under 3.6 s, in spec 8.2.


## Item 12 [low] visual

**what:** The 23 Hz 'shimmer' on a ringing field is not what vibrating steel looks like. The steel vibrates at 147-523 Hz, so the eye sees a slightly blurred highlight, not a 23 Hz tremble. A tremble reads as a rendering flicker, and the project already replaced a flicker with a blur for the harp (E8). It also needs a new custom Lit-variant shader template with a per-material uniform array, for a tilt of at most 3 mrad. Its shader is underspecified: no HLSL, and no rule for how one vertex-colour channel encodes 9 fields.

**evidence:** Spec 7.3 / 7.4.3. ARCHITECTURE.md §19 item 46: 'plucked strings read as translucent spindles (no 45 Hz flicker)'. ARCHITECTURE.md §0: materials only from template .mat assets.

**fix:** Drop the shimmer, or replace it with a level-driven blur: smoothness -0.05 · min(1, Level(i) / L_ref) for the struck field, through a global array (Shader.SetGlobalVectorArray('_HandpanLevel', ...)) with the field index in uv2.x. Specify the HLSL in 7.3.


## Item 13 [low] sound

**what:** The ding misses a sympathetic path the measured instrument shows. In the real undamped D3, the D4 field's octave (≈ 592 Hz) is among the three strongest partials. In the model, the ding's 4 f0 (587 Hz) is weak, because each sympathetic lane responds only to bus content at its own frequency. The driven D4 fundamental never produces its own octave.

**evidence:** critic/c2-sound.mjs: with the ding struck at the apex at vel 0.6, 4 f0 is at -42.1 dB re f0 at 10 ms and -42.7 dB at 0.5 s, while the fifth is at -21.7 dB. Alon & Murphy 2015 Table 2: undamped D3 partials 152.1, 297.7 and 592.3 Hz, with 592 Hz above the fifth. With the other fields damped, the D3 shows 1:2:3 instead.

**fix:** Also drive each field's SYM octave lane from that field's own SYM fundamental output. This stays one-way, so it remains stable. Calibrate it in lanePeak so the ding at mf shows 587 Hz above its own fifth in the 10-100 ms frame (target about -15 dB re f0). Check with TV6.


## Item 14 [low] sound

**what:** Decays are 15-30 % longer than the published per-register means. Together with the sympathetic halo, this can blur fast passages.

**evidence:** From the designer's own measured T60s, the mean of the three main modes per register:
- low (D3, A3): 4.2 s, against Alon & Murphy 2015 Table 3's 3.2 ± 0.9 s (the ding alone: 4.7 s);
- mid: 3.4 against 3.0 ± 1.2 s;
- high: 3.0 against 2.5 ± 0.9 s;
- all: 3.4 against 2.9 ± 1.0 s.

**fix:** Include T60_F0_REF = 4.8 s (instead of 5.5) in the 'measured' A/B render, and correct spec 3.4 to say 'at the top of the range' rather than 'within'.


## Item 15 [low] spec

**what:** A few contract details would still be guessed during the Unity build.

**evidence:** ARCHITECTURE.md §8 IInstrument requires Hover(pointers, HoverGlowPool), AcceptsStickPointers, TickOrder and PlaySurfaceAt. The desktop path (§0) needs a collider on Layers.PlaySurface. Spec 5.1 and 6.4 do not specify these, or how palm and confidence data reach HandpanPlay.Step from Tick(pointers).

**fix:** In spec 5.6 / 6, state:
- Hover: no glow (E3 shadows only).
- AcceptsStickPointers = false.
- TickOrder: after the harp.
- PlaySurfaceAt: the top cap and bands → PlaySurface.Handpan.
- Desktop collider: a 32 × 64 MeshCollider of the top cap on layer 29.
- Hand data: HandFrame from HandInput.Instance.Get(side) gives isTracked, isHighConfidence, poseInferred and systemGesture, and HandMath gives the palm centre and normal.


## Item 16 [low] sound

**what:** The worst-case output of +11.5 dBFS relies entirely on the master limiter downstream of the Meta spatializer source, which nobody has verified. The spec rejects a soft clip because it would break the null tests, but a clip that acts only above 0.95 full scale would leave every test vector unchanged.

**evidence:** Spec 3.8 and 11.7. The largest test-vector peak is TV2 at 0.741, and a single ff note peaks at -2.9 dBFS. room.md: MasterBusV2 applies a -3 dB trim, a leveller and a -1 dBTP limiter after the sources.

**fix:** Either add a per-unit safety soft clip above 0.95 FS (for example y = sign(x)·(0.95 + 0.05·tanh((|x| - 0.95)/0.05))), or make device check 4 confirm the source-to-master path is float end to end before relying on the limiter.
