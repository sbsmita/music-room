# marimba: review items the revision never saw

The design workflow passed only the first 12,000 characters of the independent review to the reviser, so the items below were never addressed. The build agent must handle each one: fix it, or explain in its report why not, with evidence. Low-severity items are optional. Full review: critiques/marimba-critique.json; revision report: critiques/marimba-revision.json.


## Item 6 [medium] spec

**what:** The lesson pack relies on lesson-engine behaviour that doesn't exist, and no contract request covers it. The platform's LessonOptions are {NoCountdown, Accompany, QuietMistakes, Forgiving, Hidden}. There, Forgiving means 'any key plays the target note', switched on live by the tour; the marimba spec instead says 'after 2 misses'. `AnyImplement` doesn't exist, and `Matches` already accepts any mode. The pack never sets `Accompany`. Accompaniment is defined only through IAudioEngine.Piano/Release, so 'the marimba's own low bars answer' has no defined path.

**evidence:** sunny-tour.md 2.5 (accompaniment plays through IAudioEngine.Piano/Release) and 7.4 (LessonOptions). out/lessons-marimba.json has marimba-first options ['Forgiving','AnyImplement'] plus an `accompany` array. Spec 7 and 5.6 `Matches` say 'Any mode counts'.

**fix:** Set marimba-first options to ['Accompany','QuietMistakes'] (the piano first-run pattern), drop AnyImplement, and rely on platform B6 for forgiveness. Add contract request R6: an `ILessonAccompany.Play(midi[], vel)` hook on the instrument, implemented by the marimba as unit strikes with no recorder event and no bar dip. Fallback: the piano accompaniment of sunny-tour 2.5.


## Item 7 [medium] spec

**what:** The 'not over-wet' rule fails for the low and high sources, and no contract request covers it. The rule requires seat DRR within ±2 dB of the piano's +6.1 dB, so at least +4.1. The spec instead compares against the harp. Separately, the ff true peak of -0.99 dBTP is 0.01 dB above the -1 dBTP ceiling.

**evidence:** Spec 3.1: Lo +2.79 dB and Hi +2.32 dB (ear mean). The −1 dB send the spec mentions gives +3.8 / +3.3 dB, which still fails. ff C3+C4+C5 through MasterBusV2: -0.99 dBTP.

**fix:** In AudioSourceTable, set the MarimbaLo and MarimbaHi sends to -2 dB and keep Mid at 0 dB. By the spec's own slope of about 1 dB DRR per dB of send, that gives about +4.8 / +4.3. Re-run tools/parity.mjs and record the result. Otherwise file a written exception as R7. Report the ff true peak against the master's own ceiling.


## Item 8 [medium] lessons

**what:** The Mary lesson's suggested sticking (strict R/L alternation) crosses the hands on 7 of 26 notes. Crossed hands block each other from the Quest's cameras and confuse beginners.

**evidence:** From out/lessons-marimba.json: E4R D4L C4R (the right hand passes left of the left hand). Crossings at events 2, 3, 11, 14, 17, 23 and 24.

**fix:** In lessons.mjs, alternate hands unless the alternation would cross the other hand's position; then repeat the same hand. Resulting order: E4R D4L C4L D4R E4R E4L E4R D4L D4R D4L E4R G4R G4L E4L D4L C4L D4R E4R E4L E4R E4L D4L D4R E4R D4L C4L (0 crossings, longest same-hand run 4, at 0.6 s per note).


## Item 9 [medium] performance

**what:** The main-thread contact solve costs far more for fingertip and soft strokes than the spec's '4–38 µs' and its 'C# double should be similar'. The fingertip is what 'First notes' invites. On the Quest under IL2CPP this scalar double code should run several times slower than Node on the M5, failing check M2 (≤ 80 µs).

**evidence:** c6-contact-cost, warm Node on the M5 Pro, OS 4. Finger vel .15 (contact 6.3 ms): 99.9 µs average. Finger vel .7: 46.4 µs. Soft C3 vel .3: 29.4 µs. Medium C4 vel .6: 13.2 µs. burst.md 10.2 puts the clock ratio at 2.77 with a pipe ratio of about 1.5, so roughly 4x, or about 180-400 µs per fingertip stroke on the Quest. With OS 2 instead: finger vel .15 drops to 30 µs, the injected states change by -48 to -63 dB, and the contact segment by -22 to -28 dB.

**fix:** Use OS = 2 whenever the Hertz contact time at the impact speed is over 1.2 ms (fingertip, soft, slow medium), and check it with a null test against OS 8. Alternatively, compile `MarimbaContact.Solve` with [BurstCompile] (double) and call it from the main thread. Add an EditMode [PERF] case for a fingertip at vel 0.15, and keep M2 at ≤ 80 µs on the device.


## Item 10 [medium] spec

**what:** Boot cost is understated. Spec 6.2 says Build is 'two 146×146 generalized eigenproblems, about 0.3 s in C#'. But the reference `profiles()` re-runs the Newton arch tuning (`tuneArch`). A literal port would stall the Quest at boot for many seconds.

**evidence:** Node on the M5 Pro: profiles() takes 2397 ms. One tuneArch from the tabled start takes 2191 ms. One solveBar takes 85 ms.

**fix:** On the device, load out/marimba-tables.json (94 KB TextAsset) and never run the FE code in the player. Keep `MarimbaDesign.Build` for EditMode tests only, using the tabled width/depth/shape with two solveBar calls and no tuneArch. State this in 6.2.


## Item 11 [medium] visual

**what:** The tubes and tray collide with a seated player's legs in MR, the judged 'Play in my room' mode. The middle-octave natural tubes hang at knee depth, 0.50 m ahead of the eyes, down to 0.37–0.44 m. The tray sits at 0.66 m, 0.265 m ahead, on the thighs (thigh tops are 0.56–0.68 m). The spec also contradicts itself: 4.2 says 'the top two octaves end on a cosmetic straight line at 120 mm', while 4.5 says E4–A4 tubes end at 0.38–0.45 m. C4's acoustic length (306 mm) can't be cut to 120 mm.

**evidence:** Spec 4.2 and 4.5; the tube lengths in table 2.7 (C4 306 mm, A4 176 mm); the seated knee figures in 4.5.

**fix:** Draw the C3–B4 natural tubes curved: vertical for 40 mm, then bent away from the player under the sharps (folded resonators like those on low-register concert marimbas). Their acoustic length is unchanged, and nothing below 0.62 m comes closer than 0.62 m ahead of the eyes. List each bar's visual tube end explicitly: C5–C6 end on a straight cosmetic line at the C5 length (146 mm) with stoppers. Move the tray to y = H + 0.005 on the front rail.


## Item 12 [low] visual

**what:** The mallet tray contradicts itself. Grips at x ±0.07 with butts 0.10 m behind puts the butts at ∓0.03, so the two shafts overlap at the centre. Heads at ±0.34 m overhang the 0.46 m tray (±0.23) by 0.11 m on each side.

**evidence:** Spec 5.1 'At rest' against tray 0.46 × 0.05 m (4.2) and mallet length 0.37 m (4.2).

**fix:** Use a 0.80 × 0.06 m tray. Place the grips at ±0.15, the butts at ±0.05 and the heads at ±0.42, resting on two felt cradles. Update the `tray.left`/`tray.right` anchors.


## Item 13 [low] sound

**what:** Assigning each octave to one fixed source makes the sound jump at the octave boundaries. Within a group, where a bar sounds can be up to 18° from where it is seen. The lessons (C4–A4) all sit in Mid, but free play and the reused piano/harp lessons (G3–G5) cross the boundaries. R1 also doesn't raise the seat maximum, because the piano and marimba never share a set (drums 8 + marimba 3 + hand drums 2 = 13).

**evidence:** Directions from the seat: Lo -31.7°, Mid 0°, Hi +32.3°. B3 is seen at -22.3° and C4 at -16.8°, but the sound jumps 31.7°; B4→C5 likewise (seen at 18.0° → 23.0°).

**fix:** Keep three sources. For the two bars on each side of each boundary (A#3–C#4 and A#4–C#5), crossfade at constant power between the two neighbouring sources by bar x, and keep the rest hard-assigned. Judge the comb filtering by ear (M8). Correct the seat-maximum sentence in R1.


## Item 14 [low] sound

**what:** Things to check by ear (M8). In the bass, the attack is overtones first and the fundamental swells afterwards. Above 6 kHz there is almost nothing, so the top octave is close to a pure sine plus a knock, which risks sounding dull or electronic rather than harsh.

**evidence:** c4-bloom: C3's fundamental is -6.9 dB at 20 ms, reaches -3 dB at 40 ms and peaks at 108 ms; G3 peaks at 70 ms. c2: in the first 43 ms at C3, the 4th / 10th partials are +5.7 / +5.3 dB over the fundamental at vel .6 (+6.0 / +7.3 at vel .9). At C6 mf the 2nd partial is -51 dB and the 3rd -105 dB. c1: energy in 6–12 kHz is -48 to -56 dB re total in every render.

**fix:** If the low notes sound like a 'ping then swell', raise KAPPA_G from 0.0069 toward 0.009 and re-run calibrate-coupling.mjs and calibrate.mjs, or inject 20% of the bar's velocity into the tube state at handoff. If the top octave sounds electronic, raise the knock for midi ≥ 72 by +4 dB. A/B each change against the current version, loudness-matched.


## Item 15 [low] spec

**what:** Two Sunny tip triggers, `marimba.fingerOnly` and `marimba.offCentre`, are defined nowhere: not in the 5.6 signal list, not in interaction.reference.mjs, and no IX case covers them.

**evidence:** Spec 8 rows tip_marimba_mallets and tip_marimba_centre; grep finds no producer for either signal.

**fix:** Add both to 5.6 with exact rules: fingerOnly = 6 fingertip onsets within 20 s with no mallet ever held; offCentre = 10 consecutive mallet onsets with ≥ 60% at u < 0.3 or > 0.7. Implement them in MarimbaPlay and add IX cases.


## Item 16 [low] spec

**what:** Some test vectors expect bit-exact results that a C# port cannot reproduce. TV2 asks for a bit-exact hash of the contact sound, which comes from a nonlinear double-precision solve of up to 2304 steps using Math.pow/exp/cos. The pole tables also come from Math.exp/cos. The device's maths library (IL2CPP) and Node's can differ by 1 ulp, which can flip float32 roundings and make the bit-exact checks fail spuriously.

**evidence:** Spec 9.1: 'Burst off: bit-exact FNV-1a' against TV2 segFnv, with poles computed in design.mjs laneTable. In Node, the TV2 hashes did reproduce in a fresh process (checked).

**fix:** Compare TV2 per sample to 1e-6 relative instead of by hash. Run TV4–TV8 bit-exact only with poles and strike records loaded from marimba-tables.json and the vectors file, not recomputed.


## Item 17 [low] visual

**what:** The bar texture and mesh are underspecified for realism. One 512×128 rosewood texture is shared by 37 bars with no UV mapping given ('streaks along x', but the bars run along z), which risks the same grain repeated on every bar or grain running the wrong way. '120 vertices per bar' can't hold drilled cord holes and a 16-segment arch with normal seams.

**evidence:** Spec 4.2, 'Bar geometry' and 'Textures'.

**fix:** Map texture u along the bar length (z). Give each bar a seeded UV offset, a flip and a ±4% tint, from a 1024×256 atlas. Draw the cord holes as a dark disc where the cord enters, or budget 180 vertices per bar.


## Item 18 [low] lessons

**what:** Two Sunny lines are 10 words long with two sentences, and one of them teaches the node spot. At 150 wpm, ten words take about 4.0 s before the sentence pause, which risks the 4.0 s clip lint.

**evidence:** tip_marimba_arrive (10 words) and tip_marimba_back (10 words, 2 sentences). For comparison, sunny-tour's 9-word line runs 3.54 s.

**fix:** Use 'Pinch a mallet to pick it up.' (the platform's own wording) and 'Sharps are the back row. Strike their nearest tip.' Render them and assert each clip is ≤ 3.8 s.


## Item 19 [low] interaction

**what:** Port hygiene. `lost()` adds 1000/90 ms per frame, so the 0.5 s release of a lost-hand mute stretches to 0.625 s at 72 Hz. The engine's header comment still says strike sounds are 'panned onto two sources'.

**evidence:** interaction.reference.mjs `lost()`; marimba-engine.mjs header comment.

**fix:** Use the frame's real elapsed time (Clock.NowMs difference) in `lost()`, and change the comment to three hard-assigned sources.
