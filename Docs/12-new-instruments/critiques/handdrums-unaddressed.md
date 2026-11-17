# handdrums: review items the revision never saw

The design workflow passed only the first 12,000 characters of the independent review to the reviser, so the items below were never addressed. The build agent must handle each one: fix it, or explain in its report why not, with evidence. Low-severity items are optional. Full review: critiques/handdrums-critique.json; revision report: critiques/handdrums-revision.json.


## Item 9 [medium] spec

**what:** Several parts cannot be built without guessing:
- The reference velocity differs from the C# implementation's: frame dt in the reference, sampleInterval plus tipVelocity as a 'C#-only addition' in the C# code. So a 1:1 transliteration has no reference behaviour to match.
- Budget scaling (capGroups, fade order, 'quieter drum', fade law) exists only in prose; the reference has no implementation or vector.
- Patch(speed) is ambiguous: is it u or V in m/s?
- The Register output order needs channel 0 = HandDrumHi, the reverse of the platform's listed order (Lo, Hi).
- The 'back-of-hand axis' is not tied to a function.
- The head-dent vertex normals are not specified.
- Recorder relabelling (mute/closed after up to 60 ms) needs an API the recorder does not have.
- Playback ignores the recorded ρ, so loops sound different from the take.
- Stale numbers: the spec says 86 µs adversarial and Q4 0.7; bench.json says 127.7 µs and verify.json 0.94.

**evidence:** engine/handdrums.reference.mjs has no Budget/Share code. interaction.mjs step() uses dt. handdrums.md 2.12 (Patch 'A V (m/s)' vs 'float speed'), 3.3 step 2 ('C#-only addition'), 3.5, 4.5. ENGINE-API.md 'Register(ctx, role, outputs…)'. Platform.md 2: sources 'HandDrumLo, HandDrumHi'. HandMath.Palm already exists (HandFrame.cs).

**fix:** 1. Put the velocity source the C# implementation will use into the reference, and regenerate the cases.
2. Implement capGroups in the reference with a vector at Share 0.4: which groups, fade over 256 samples linearly, quieter drum by the last block's rms.
3. Declare Patch(…, float u, …) and compute V inside the unit.
4. Write the Register line: Register(ctx, UnitId.HandDrums, new[]{SourceId.HandDrumHi, SourceId.HandDrumLo}, …) plus the SetSourceParent targets.
5. back = −HandMath.Palm normal.
6. Dent normals: analytic from the Gaussian, or recompute on the 2 head meshes only while dented, within the 0.12 ms Tick budget.
7. Write the recorder event 60 ms late, or drop the relabel and rely on handDrumMute.
8. Playback offsets the canonical points by the recorded pos.
9. Refresh the numbers from bench.json and verify.json.


## Item 10 [medium] lessons

**what:** Sunny's demos name kinds that don't exist as written: 'Slap (edge only)', 'Strike (palm)', 'Slap (cupped)', 'Tap (rocking)', 'Tap (two hands)', 'Hold (palm)'. The platform's Slap is an open palm at the edge and then the centre; Strike is a finger tap; Tap is a 2 cm fingertip tap. So the bass, open, slap and heel-toe tips would demonstrate the wrong gesture. The slap line doesn't say where to play, but the classifier needs the knuckles at the rim. The P3 triggers ('≥ 3 strokes and no open yet') need named Signals that are never defined.

**evidence:** sunny-tour.md 7.3 and platform.md 8.3 (demo kinds: Palm, Poke, Swipe, Nod, Press, Strike, Sweep, Pinch…, Tap, Slap, MalletStrike, Approach, Circle). platform.md 8.1: TipTrigger is FirstArrival | FirstDisplayedGaze | IdleNear | Signal. handdrums.md 5.2.

**fix:** 1. Add a contract request for the demo variants: OpenTone (flat fingers at the edge), PalmBass, CuppedSlap, HeelToe, Roll. Use PalmRest for the quiet tip.
2. Change the slap line to 'At the edge, cup your fingers and snap the tips.' (9 words).
3. Define signals handdrums.needOpen/needBass/needSlap/needMute/needHeel/ringing/needRoll, with their counters, in HandDrumsModule.


## Item 11 [low] lessons

**what:** The first lesson runs 11.4 s at tempo (16 beats at 84 bpm). The audit asks for 12–21 s at tempo, and the drums' first lesson uses 80 bpm for 12.0 s. The Tumbao lesson's slap targets also inherit the speed-misclassification above; only B6's 15 s 'any hit' fallback rescues them. Public-domain status is fine: traditional rhythms, and the 1899 'shave and a haircut' tag.

**evidence:** out/handdrums-lessons.json checks: seconds 11.43. tutorials-audit.md line 58 ('12–21 s at tempo').

**fix:** Set handdrums-first to 80 bpm (12.0 s). Once the slap classifier is fixed, re-run the lesson with the scripted hands at 2 m/s and require that every slap target matches.


## Item 12 [low] sound

**what:** The pitch glide is under-reported. Inside the engine, s jumps to +51 cents (conga) and +60 cents (tumba) 5 ms after a u 1 bass, and +33/+37 cents at u .8, falling to about 0 by 100 ms. The spec quotes '+11 cents at 15–45 ms'. A half-semitone drop on hard bass strokes may read as a synth-tom 'boing'. There is no published conga value; glideMax was chosen, not measured.

**evidence:** critic/c3-strokes.json glideCentsAt for bass u .8/u 1: 5 ms 33/51 (conga) and 37/60 (tumba); 20 ms 16/27 and 20/35; 50 ms 4.7/8.1 and 6.8/12.2.

**fix:** Report the s(t) trajectory. Halve glideMax (conga 1.5 %, tumba 1.75 %) until a recording settles it, and list 'pitch drop on hard basses' in the README checklist.


## Item 13 [low] spec

**what:** The 'not over-wet' rule (DRR within ±2 dB of the piano's +6.1 dB) was never measured. The impulse DRR is acceptable, but programme DRR is low because the drums' energy sits at 70–262 Hz. The safety claim of 13 cm above the knees holds at the head centre only: the tilted near edge, where open tones are played, sits at 0.712 m (9 cm above the knee box), and the palm and wrist hang over the knee box during open tones.

**evidence:** critic/c4-room.mjs → out/c4-room.json:
- Impulse DRR per ear: conga 7.7 / 3.1 dB, tumba 2.1 / 6.2 dB.
- C50: 10.4–14.8 dB.
- Tumbao programme direct-to-room ratio: 1.7 dB.
Geometry from interaction.mjs head(): near edge y = 0.75 − 0.146·sin 15° = 0.712 m, z = 0.429 m; the palm for an open tone is at z ≈ 0.394 m, inside the knee box (front 0.41 m).

**fix:** Add DRR, C50 and late-energy-after-80 ms to verify.json, and keep the room send as is if the per-ear mean stays within ±2 dB. In T-HD-REACH, check the open tones at the near edge specifically. If anyone touches a knee, raise LiftMin to 0 or reduce the tilt to 10°.


## Item 14 [low] visual

**what:** The materials are too loosely specified for a realism-first look. The rawhide texture has no resolution and no normal or roughness map, although it is the surface viewed closest, at about 0.45 m. The 512×256 stave texture is about 0.5 texel/mm around the shell, against about 2.6 display px/mm at that distance on Quest 3, so it will look smeared, and a '1 px seam' at 42.7 px per stave will alias. The stand legs use 'a darker vertex-free UV region of the shared Core/grille texture' on a chrome LitMetal material. The 12×48 head grid, 1.2–1.9 cm apart, makes small dents lumpy.

**evidence:** handdrums.md 4.1–4.2 and 4.5.

**fix:** 1. Rawhide: 1024² albedo plus a normal map (fibre and hand wear) from ProcTex at bake time, smoothness 0.25–0.35.
2. Shell: UVs per stave (u 0..1 per stave, tiled), a 1024×512 grain texture with the seam drawn in, and the crease as geometry.
3. Legs: their own satin-black region in a small atlas, or chrome without the grille hack.
4. Head grid: 16 rings × 64 segments, still one draw for both heads.
