# 11 · Tutorials audit: every lesson, the harp in lessons, the first five minutes

Audit of all 29 lessons of VR Music Room, made on 1 Nov 2026. It is read-only: no project or clone file
was changed. It covers four things:
- whether each melody is musically correct and public domain;
- whether it can be played on our instruments with bare hands, seated;
- teaching quality and the Unity lesson code;
- the harp's sound in lessons, old against new.

Deliverables:
- `lessons-proposed.json`: the corrected set, in the exact schema of the Unity `lessons.json`. Ids are kept stable, and it
  is ready to drop in (it now ships as `Lessons/Resources/lessons.json`). Section 8 lists the changes and the tests that must change with it.
- `tools/`: `lessons-src.mjs` (the proposed tunes, in the lesson token syntax), `make-lessons.mjs` (builds the JSON and
  `out/stats.json`), `render-tutorials.mjs` (all renders and the harp measurements), `harp-variants.mjs` and
  `harp-damping.mjs` (the harp experiments).
- Renders: the `tutorials` listening set (51 files, 350 MB, with a plain-English `README.txt`).

Sources read:
- Lesson code and data, snapshot taken at 04:10–04:30 on 1 Nov:
  - `Assets/MusicRoom/Lessons/*` (`Tutorial.cs` sha1 f9eb5e17…, `LessonHints.cs` 3f6b3b97…, both unchanged since 25 Oct);
  - `lessons.json` (sha1 12a8916a…, identical to `Docs/lessons.json`);
  - `Menu/MenuController.cs`, `MenuText.cs`, `MenuPainter.cs`;
  - `Drums/DrumSpec.cs`, `DrumMath.cs`;
  - `Harp/HarpGeometry.cs`.
- Specs: spec 05, spec 08 §1.5–2.3, `10-upgrade/{harp,piano,drums}.md`, `11-first-five/sunny-tour.md` (the first-run tour).

**Moving target.** During the audit the integrator merged the new sounds into the main project:
- `HarpGeometry.cs:60` now clamps the play point at `PlayUMax = 0.40`;
- `Audio/Sound/Harp/HarpUnit.cs` was added;
- `DrumSpec.cs:89-94` sets `FingertipImplement = Tip`.

`MenuController.cs` line numbers moved twice while it was being read. Cite it by method name; the line numbers given are
as of 04:30.

---------------------------------------------------------------------------------------------------------------------------

## 0. Summary

**Counts.** Of the 29 current lessons:

| Outcome | Count | Lessons |
|---|---|---|
| OK, notes unchanged | 14 | **Piano 8:** scale, mary, frere, london, macdonald, jingle, row, elise<br>**Drums 5:** beat, halftime, shuffle, fill (retitled "Tom fill"), waltz<br>**Harp 1:** arpeggio |
| Fixed | 14 | **Piano 5:** ode, twinkle, baa, birthday, silent<br>**Drums 4:** rock, disco, bossa, fill-round<br>**Harp 5:** twinkle, frere, jingle, grace, silent |
| Removed | 1 | harp-scarborough |
| Added | 8 | **Piano:** lullaby, auld<br>**Drums:** first, hat, nod<br>**Harp:** ode, lullaby, auld |

The proposed set has **36 lessons**: 15 piano, 12 drums and 9 harp, with 1148 events and 1263 targets. Every lesson is now
in easiest-first order.

**The most important findings**
1. **No instrument starts with a guaranteed win.**
   - Piano opens with a scale, not a tune.
   - Drums open with the two-hand eighth-note beat. Its kick is a pad nobody has been told about.
   - Harp opens with an arpeggio that skips strings up to the top string.

   **Fix:** new first lessons, each 12–21 s at tempo with one target at a time:
   - piano: Ode to Joy, the whole 8 bars, which continues the tour's first-run phrase;
   - drums: "First beat" (kick and snare), then "Add the hi-hat";
   - harp: Ode to Joy on five neighbouring strings.
2. **Three melodies have wrong notes or rhythm, and three stop at an awkward point.** The cuts are Ode to Joy ending on
   the half cadence, Silent Night stopping at "mother and child", and Twinkle being only one phrase.
   - **Baa Baa:** bars 6–8 are wrong and end on D.
   - **Amazing Grace:** line 3 repeats line 1's cadence instead of "I once was lost…".
   - **Happy Birthday:** "dear Na-me" is a beat too long.

   Every fix is checked against a public-domain score (section 3).
3. **Scarborough Fair is removed.** The tune everyone knows is not safely public domain: its only source is Ewan MacColl's
   1947 notation, printed in 1960. The lesson also had wrong notes in its second half and ran over the bar lines. Every
   other song is public domain in melody and words (section 3). Happy Birthday is cleared: the 2015 US ruling and 2016
   judgment, and EU expiry in 2017. The app never shows lyrics.
4. **Two drum lessons need three hands.** Disco (8 events) and Bossa nova (4 events) stack hi-hat + kick + snare. The hint
   never says to nod.
   - **Fix:** rewritten so no event has more than two targets. A two-hand bossa nova (kick pad + clave). A "Nod for the
     kick" lesson before Disco. Disco puts the hi-hat on the off-beats, its real signature.
   - "Rock beat (8 bars)" was the Basic beat repeated 8 times; it is now a real rock variation.
5. **Lesson code bug B1, high: cymbal targets are buried in the cymbal and aim at the bell.**
   - The waiting disc lands 1–11 mm above the strike plane, and the glow 2 mm above it. The cymbal surface there is
     12–16 mm up, so both are hidden. The screenshot `Builds/Shots/lessons/lessons-drums-waiting-close.png` shows only a
     thin ring on the hi-hat.
   - A hit on the disc, at the centre of the crash or ride, plays the **bell**: hear `drums-cymbal-edge-vs-centre.wav`.
   - The toms, crash and ride discs also sit at 0.73–0.78 m from the nearer shoulder, a full stretch for bare hands.
   - **Fix:** land drum and cymbal targets at 0.55 r on the side facing the player, on the surface (section 5).
6. **Lesson code bug B2, medium:** striking a target you already hit, in the same event, counts as a mistake and flashes
   red. This happens with a bouncing hand on the hi-hat, or a re-triggered chord key. Smaller issues B3–B7 are in
   section 5.

**Harp verdict.** The new harp is better for every lesson, with one cost, and that cost is now handled in the lesson data.

What is better:
- Every note of a tune comes out at the same strength. The 10–90 % spread of note levels is 2.1–3.9 dB, against
  4.2–11.6 dB for the old harp.
- Strings are evenly voiced: 3.4 dB from C3 to C6 with neighbour steps ≤ 0.3 dB. The old harp spans 12.8 dB, with random
  jumps of up to 7.1 dB between neighbours, such as E5 against D5.
- The top notes ring like a harp: C5 takes 0.77 s and C6 0.64 s to fall 20 dB, against the old harp's 0.36 s and 0.41 s
  plink.
- No clicks, no metallic (inharmonic) peaks, and an attack under 3 ms on every string.

The cost: notes ring longer, so each new note stands out 1.2–3.9 dB less than on the old harp.
- Repeated notes stand out 0.5–6.9 dB less.
- By the numbers it matters only in quick passages (notes standing out by under 5 dB): the eighth notes of Frère Jacques
  and the repeated E4s of Jingle Bells. Nobody has listened yet.
- The only lever that works is decay length. Brightness, pluck noise and sympathy change nothing.
- **Done in the data:** Frère 100 → 88 bpm, Jingle 112 → 100 bpm.
- **If it still blurs on the device:** `T60_C3` 7.0 → 5.6. That one constant gives back the old harp's clarity in every
  lesson (measured).
- **Palm damping works, with a quieter tail.** The played strings fall 30 dB within 0.3 s. A faint hum from the free,
  octave-lower strings, which rang in sympathy, stays 36–50 dB under the playing level and fades over a second. The old
  harp cuts to digital silence.

---------------------------------------------------------------------------------------------------------------------------

## 1. How it was checked

**Melodies.** An independent research pass read the notes from machine-readable or scanned public-domain sources:
- Wikipedia's LilyPond scores;
- Mutopia `.ly` files;
- Wikimedia Commons MIDI files, decoded by script;
- archive.org scans of Kidson (1891) and *Slave Songs of the United States* (1867).

A script then checked the beat totals, ranges and white-keys-only for every string. Sources are cited per song in
section 3.

**Playability.** `make-lessons.mjs` computes, per lesson:
- range, span on the keys (cm) and largest leap;
- the shortest gap between notes, and between repeats of the same target;
- targets at once, and events that need three hands or a nod;
- harp play-point heights with the new and old clamp.

The inputs are:
- the instrument geometry in spec 05 (`Docs/05-instruments.md`: piano keys, harp strings `a`/`b`, drum pieces);
- the seat frame: head at kit-local (−0.26, ·, −0.20), shoulders ±0.18 m to the side and 0.25 m below the eyes.

All numbers are in `tools/out/stats.json`.

**Code.** Read line by line. The behaviour claims in section 5 come from `Tutorial.cs`, not from the README.

**Renders.** Every lesson is played "at tempo", never waiting. Real players in wait mode are usually slower, so clarity in
use is at least as good as measured.
- **Harp:** index-finger pokes through the string plane at the glowing play point (u = PlayPoint ± 0.02), as the harp lab's
  `lessonJingle` does. The speed is 0.45 m/s, 0.65 on downbeats and 0.55 on long notes, with ±8 % speed and ±6 ms.
  - Old harp: `Tools/SoundLab/harp/old-harp.mjs`, the spec 06 recipe.
  - New harp: `harp-engine.mjs` v2.
  - Both go through `room.mjs`, and each pair is matched to −18 LUFS with the code of `render.mjs`.
- **Piano:** the new engine with the app's automatic sustain v2 (`app-sim.mjs`), keys held for 90 % of each note, and the
  piano lab's listening mix.
- **Drums:** the new kit with stick-tip strokes (what a fingertip plays in the app now), in the drum lab's mix.

**Harp measurements** are taken on the dry signal (direct + body, before the room):
- **Clarity:** energy 30–200 ms after a note against 10–60 ms before it, in dB. This is the piano lab's `wash.mjs` metric.
- **Wash:** the previous different pitch's fundamental against the new note's, 150 ms in, in dB; lower is cleaner.
- **Blur count:** how many earlier pitches still ring within 20 dB of the new note.
- **Level spread:** each note's fundamental peak in its first 150 ms.
- **Clicks:** the harp lab's `clickScan`. Its guard is 8 ms here, because the new harp's catch starts a re-pluck 4 ms
  late (harp.md 5.5).
- **Per string:** a 0.55 m/s poke on every string, giving momentary loudness, attack, centroids, HF share, time to −20 dB
  and inharmonic peaks.

---------------------------------------------------------------------------------------------------------------------------

## 2. Lesson by lesson

Lesson token syntax: `NOTE:len`, len in beats. Seconds are at tempo; the countdown and the 1.9 s first approach
come on top. The full proposed token strings are in `tools/lessons-src.mjs`.

### 2.1 Piano (13 → 15)

All notes lie in G3..G5, from 13 cm left to 20 cm right of middle C. The keys are 0.45 m ahead, so everything is in reach
from the seat, and every key carries a printed label.

| Lesson | Verdict | Problems (evidence) | Exact fix |
|---|---|---|---|
| `piano-scale` C major scale | **OK** (moved 1 → 4) | Notes right (C4..C5..C4, 84 bpm, 11.4 s). As the *first* lesson it is an exercise, not a tune, so it is no win for a first-time judge. | Order only: position 4, after three five-key songs. |
| `piano-twinkle` Twinkle Twinkle | **Fixed** (incomplete) | Correct notes, but only the first phrase: 4 of 12 bars. The well-known "Up above the world so high" is missing. | Whole song, 42 notes, 48 beats, 28.8 s at 100: `C4 C4 G4 G4 A4 A4 G4:2 F4 F4 E4 E4 D4 D4 C4:2 G4 G4 F4 F4 E4 E4 D4:2 G4 G4 F4 F4 E4 E4 D4:2` + the first phrase again. |
| `piano-ode` Ode to Joy | **Fixed** (awkward cut), now **first** | Correct notes, but it stops after bar 4 on D, the half cadence: the tune sounds unfinished. | The 8-bar period ending on C (30 notes, 17.8 s at 108): add `E4 E4 F4 G4 G4 F4 E4 D4 C4 C4 D4 E4 D4:1.5 C4:0.5 C4:2`. Bars 5–8 are exactly the first-run phrase `piano-ode-first` (sunny-tour.md 2.5), so the menu lesson is "now the whole tune". One hand, five neighbouring keys C4–G4. |
| `piano-mary` Mary Had a Little Lamb | **OK** | Correct. Today's tune is the 1867 "Merrily We Roll Along" chorus. The English words in bar 6 were not checked against a printed English score (UNVERIFIED, minor). | none |
| `piano-jingle` Jingle Bells | **OK** | Chorus correct, checked against the Commons MIDI. 51 notes, 32 s; repeated eighth-note Es 0.25 s apart, fine for hand tracking. | Order only (8th). |
| `piano-baa` Baa Baa Black Sheep | **Fixed** (wrong notes) | Bar 6 ends `D4:2`, without the "And" pickup (`D4:1.5 D4:0.5`). Bars 7–8 ("one for the little boy who lives down the lane") are `G G G F F F F` then `E E E D:2`: the second half has 4 notes for 5 syllables and ends on D, not home on C. The lesson's 12-bar shape, a repeat of "Baa baa…", matches no reputable source. | The standard 8 bars (Wikipedia score; Rosewig 1879), 38 notes: `… E4 E4:0.5 E4:0.5 D4:1.5 D4:0.5 G4 G4:0.5 G4:0.5 F4:0.5 G4:0.5 A4:0.5 F4:0.5 E4 D4:0.5 D4:0.5 C4:2`. It ends home on C. |
| `piano-birthday` Happy Birthday | **Fixed** (rhythm) | Bar 6 has "Na-me" as `B4 A4:2`. The standard 3/4 bar is `B4 A4` with the "Hap-py" pickup `F5:0.75 F5:0.25` in the same bar, so the last phrase ran a beat late (26 beats instead of 24). | `… B4 A4 F5:0.75 F5:0.25 E5 C5 D5 C5:2`. A fermata on A is a performance choice; wait mode lets the player hold it anyway. |
| `piano-row` Row Row Row Your Boat | **OK** | Matches Wikipedia's 6/8 score exactly. "Merrily" is 12 eighths with triple repeats, 0.27 s apart. | Order only (9th). |
| `piano-elise` Für Elise (opening) | **OK**, now **last** | Pickup, bars 1–7 and the first ending match the right hand of the 1888 Breitkopf edition (Mutopia). It is the hardest lesson: 5 black-key presses (D#5, G#4), 16ths 0.25 s apart, and an E4 → E5 jump inside a 16th group. It used to sit 9th, before four easier songs. | Order only: last (15th). |
| `piano-london` London Bridge | **OK** | Matches Wikipedia's score, including `D4:2 G4:2 E4 C4:3`. | Order only (6th). |
| `piano-macdonald` Old MacDonald | **OK** | Correct frame, in G, with no F#. The "moo-moo here" section is left out, rightly: it is about 20 repeated G4 eighths, poor for hand tracking. | Order only (7th). |
| `piano-frere` Frère Jacques | **OK** | Correct. The G3 at the end is 3 white keys left of C4. | Order only (5th). |
| `piano-silent` Silent Night | **Fixed** (awkward cut) | Stops at bar 6 of 12 (6/8 counting) on E4, "mother and child", mid-song. | The whole first verse, 72 beats, 45 s at 96. It adds "Holy infant…" and "Sleep in heavenly peace" ×2: `… D5:2 D5 F5:1.5 D5:0.5 B4 C5:3 E5:3 C5:1.5 G4:0.5 E4 G4:1.5 F4:0.5 D4 C4:6`. All white keys, C4..F5. |
| `piano-lullaby` Brahms' Lullaby | **Added** | A reason to come back: slow 3/4 with eighth pickups, C4..C5, all white keys in C. | 51 notes, 36 s at 80. Notes in `lessons-src.mjs` `LULLABY`. |
| `piano-auld` Auld Lang Syne | **Added** | Verse and chorus in G, D4..E5, using only G A B D E: white keys. | 56 notes, 45.7 s at 84 (`AULD_G`). |

### 2.2 Drums (9 → 12)

Assumed hand roles: the left hand on the hi-hat; the right hand on the kick pad and the snare. The pad is played by a hand
or replaced by a nod, which also counts as "kick".

Every proposed lesson has at most two targets at once, so two hands always suffice. In the old set, 12 events needed a
third hand.

Reach is measured from the nearer shoulder, seated (spec 05 §3 positions):

| Piece | Centre | Nearest edge |
|---|---|---|
| snare | 0.56 m | 0.38 m |
| kick pad | 0.56 m | 0.46 m |
| hi-hat | 0.58 m | 0.41 m |
| floor tom | 0.61 m | 0.41 m |
| ride | 0.74 m | 0.52 m |
| tom 2 | 0.73 m | 0.56 m |
| tom 1 | 0.75 m | 0.60 m |
| crash | 0.78 m | 0.57 m |

| Lesson | Verdict | Problems (evidence) | Exact fix |
|---|---|---|---|
| `drums-first` First beat | **Added**, first | Nothing simpler existed. | `kick snare kick snare` ×4 bars at 80 bpm: 16 single targets, 12 s. Teaches that the pad is the kick. |
| `drums-hat` Add the hi-hat | **Added**, second | It bridges to the eighth-note beat. | `hat+kick hat+snare hat+kick hat+snare` ×4 at 80: quarter notes, two hands, 12 s. |
| `drums-beat` Basic beat | **OK** (moved 1 → 3) | The pattern is the standard 8th-note beat. As the *first* lesson it asked for two-hand coordination plus an unexplained pad. | Order only. |
| `drums-halftime` Half-time groove | **OK** | Snare on 3 only, matching Wikipedia "Half-time"; kick on 1 and 2& is a common variant. Two targets at most. | Order only (4th). |
| `drums-rock` Rock beat (8 bars) | **Fixed** | It was `BEAT_BAR.repeat(8)`: identical to the Basic beat, 64 events and 96 targets, a stamina test rather than a new groove. | 4 bars with the kick on 1, 3 and the "and" of 3, then `crash+kick:2`. 33 events, 11.3 s at 96. Title "Rock beat". |
| `drums-shuffle` Shuffle | **OK** | Triplet swing (2/3 + 1/3), kick on 1 and 3, snare on 2 and 4. Matches Commons "Shuffle feel" (Mattingly). The swung "a" is 0.2 s before the beat: the fastest gap in the set, fine for one hand. | none |
| `drums-fill` Fill | **OK** (title) | Correct. Tom 1 and tom 2 sit at full arm's reach for bare hands (above), and their disc is on the centre (B1). | Title "Tom fill". A Sunny tip or hint to pick up the sticks for tom lessons would help. |
| `drums-fill-round` Fill round | **Fixed** | Ends `crash+kick:2 ride ride`: two ride hits after the final crash, an afterthought. The title is unclear. | Ends on `crash+kick:2` (18 beats, 11.3 s). Title "Groove with fills". |
| `drums-waltz` Waltz (3/4 ride) | **OK** | Kick on 1, snare on 2 and 3 (soundbrenner). The ride is the second-farthest piece, and the disc on its centre plays the bell (B1). | none in the data; fix B1 in code. |
| `drums-bossa` Bossa nova (rim clicks) | **Fixed** | The pattern is right: kick 1, 2&, 3, 4&, and the 3-2 bossa clave 1, 2&, 4 \| 2, 3& (Wikipedia "Clave"; Commons MIDI). But 4 events stack hat + kick + snare, three hands, and the hint never mentions the nod. "Rim clicks" cannot be taught: the disc sits on the head centre, and the tutorial accepts any snare hit. | Two-hand bossa: the right hand plays the kick pattern on the pad, the left the clave on the snare. No hi-hat, at most 2 targets, 4 bars at 100, 9.6 s. Title "Bossa nova". |
| `drums-nod` Nod for the kick | **Added** | The nod kick is never taught in a lesson, yet Disco needs it. | `kick kick+snare kick kick+snare` ×4 at 90: four on the floor, snare on 2 and 4. Playable by nodding or on the pad. Sunny's `tip_lesson_kick` demonstrates the nod (sunny-tour.md 3.2). |
| `drums-disco` Disco | **Fixed** | 8 events need three hands (hat + kick + snare on 2 and 4). The off-beat hi-hat, the genre's signature, is missing: Wikipedia says "four-on-the-floor … open hissing hi-hat on the off-beat". | Kick on every beat, snare on 2 and 4, hi-hat on the "and"s only. At most 2 targets, 4 bars at 108. Title "Disco (nod the kick)", placed after `drums-nod`. Still playable slowly with the pad. |

### 2.3 Harp (7 → 9)

Every note is a white string between C3 and C6. `make-lessons.mjs` converts names with `harpString()`, which
throws on any sharp or flat, so a lever flip is never needed.

The proposed lessons use G3 (string 4) to C6 (string 21). With the new play-point clamp (u ≤ 0.40, now in
`HarpGeometry.cs:60`), the glowing play points sit at 0.78 m (G3) to 0.96 m; the old clamp put them all at 0.95 m. Reach
on the device is checked in harp.md §11 item 4.

| Lesson | Verdict | Problems (evidence) | Exact fix |
|---|---|---|---|
| `harp-ode` Ode to Joy | **Added**, first | No stepwise starter existed. | The 8-bar period on strings 7–11 (C4..G4, the red C4 as landmark), 92 bpm, 20.9 s. The same song as the piano's first lesson and the first-run tour. |
| `harp-twinkle` Twinkle on harp | **Fixed** | First phrase only. The title's "on harp" is redundant inside the Harp list. | The whole song (42 notes, 96 bpm, 30 s). Title "Twinkle Twinkle". |
| `harp-frere` Frère Jacques | **Fixed** (tempo) | Notes correct. At 100 bpm, the eighth-note runs (G A G F, 0.3 s apart) blur on the ringing new harp: 8 notes stand out less than 5 dB, against 2 on the old harp (section 6). | 88 bpm: mean clarity 9.6 → 11.4 dB; notes under 5 dB 8 → 5. |
| `harp-arpeggio` C major arpeggio | **OK** (moved 1 → 4) | Correct. As the *first* lesson: string-skipping thirds over two octaves to C6, the shortest and highest string at the neck. | Order only. |
| `harp-lullaby` Brahms' Lullaby | **Added** | A slow, harp-idiomatic piece in C, C4..C5, no accidentals. | 51 notes, 80 bpm, 36 s. |
| `harp-jingle` Jingle Bells | **Fixed** (tempo) | Notes correct. At 112, the repeated E4 re-plucks lose onset contrast on the new harp: repeated-note clarity 9.4 dB, against 12.0 on the old harp. | 100 bpm: repeated-note clarity 10.9 dB; notes under 5 dB 4 → 2. |
| `harp-auld` Auld Lang Syne | **Added** | Pentatonic melody, idiomatic on a harp, G3..A4. | 56 notes, 84 bpm, 45.7 s. |
| `harp-scarborough` Scarborough Fair | **Removed** | (1) Copyright doubt: see section 3. (2) Bars 9–15 wrong: the lesson rises `A4:2 B4:0.5 C5:0.5 D5 C5:2`, where the tune has `D5:2 D5 C5:2 A4 A4 G4 F4 E4:3`. "She once was a true love of mine" has 6 notes, and the final `D4:3` starts on beat 2, totalling 46 beats (15⅓ bars). | Removed. The unquestionably public-domain notation (Kidson 1891) is a different tune in G that nobody would recognise. |
| `harp-grace` Amazing Grace | **Fixed** (wrong notes) | Line 3 re-uses line 1's cadence, `G4:3 G4:2 E4:0.5 G4:0.5 G4:1.5 E4:0.5 C4 E4:2 D4 C4:2 A3 G3:2`, instead of "I once was lost but now am found". | Excell 1900 (Wikipedia score; Mutopia `new_britain.ly`; hymnary.org incipit): `… D4 G4:2 E4 G4:1.5 E4:0.5 G4:0.5 E4:0.5 C4:2 G3 A3:1.5 C4:0.5 C4:0.5 A3:0.5 G3:2 G3 …`. 35 notes. |
| `harp-silent` Silent Night | **Fixed** (awkward cut) | As for the piano. | The whole first verse, 92 bpm, 47 s, C4..F5. |

---------------------------------------------------------------------------------------------------------------------------

## 3. Public-domain status (melody and words)

The app shows titles only, never lyrics. Every title is plain English, with no brand names. "Für Elise" and
"Frère Jacques" are the standard English titles, and their glyphs ü, è and the · are in the menu's TextMeshPro atlas.

| Song | Melody | Words | Status | Notes checked against |
|---|---|---|---|---|
| Twinkle Twinkle / Baa Baa | "Ah ! vous dirai-je, maman", printed 1761 | Jane Taylor 1806 / Baa Baa c.1744; tune and words together by Rosewig 1879 | PD | en.wikipedia.org/wiki/Twinkle,_Twinkle,_Little_Star · en.wikipedia.org/wiki/Baa,_Baa,_Black_Sheep · en.wikipedia.org/wiki/Ah!_vous_dirai-je,_maman |
| Ode to Joy | Beethoven, 1824 | (not used) | PD | en.wikipedia.org/wiki/Ode_to_Joy |
| Jingle Bells | Pierpont 1857; the modern chorus by 1898 | Pierpont 1857 | PD | commons.wikimedia.org/wiki/File:Jingle_Bells_refrain_vector.mid · en.wikipedia.org/wiki/Jingle_Bells |
| Mary Had a Little Lamb | "Goodnight, Ladies" chorus, 1867 | Sarah Josepha Hale 1830 | PD | en.wikipedia.org/wiki/Mary_Had_a_Little_Lamb · en.wikipedia.org/wiki/Goodnight,_Ladies |
| Happy Birthday | "Good Morning to All", Hill sisters 1893 | US: Judge King, 22 Sep 2015 (*Marya v. Warner/Chappell*): the claimed copyright is invalid; 2016 settlement and judgment, no valid claim. EU: Patty Hill died 1946, expired 1 Jan 2017 | PD (low residual risk) | en.wikipedia.org/wiki/Happy_Birthday_to_You |
| Row Row Row Your Boat | 1881 (Franklin Square) | 1852 | PD | en.wikipedia.org/wiki/Row,_Row,_Row_Your_Boat |
| Für Elise WoO 59 | Beethoven 1810, published 1867 | – | PD | mutopiaproject.org …/fur_Elise_WoO59.ly |
| London Bridge | traditional, late 19th-century tune | c.1744 | PD | en.wikipedia.org/wiki/London_Bridge_Is_Falling_Down |
| Old MacDonald | traditional; an "Ohio" variant in *Tommy's Tunes* 1917 | traditional | PD (do not sample 1920s recordings) | en.wikipedia.org/wiki/Old_MacDonald_Had_a_Farm · fr.wikipedia.org (score) |
| Frère Jacques | manuscript c.1780 | traditional | PD | en.wikipedia.org/wiki/Frère_Jacques |
| Silent Night | Gruber 1818 | Mohr 1818 | PD | en.wikipedia.org/wiki/Silent_Night · mutopiaproject.org …/stille-nacht.ly |
| Amazing Grace | NEW BRITAIN, 1829; Excell's setting 1900 | Newton 1779 | PD | en.wikipedia.org/wiki/Amazing_Grace · hymnary.org/tune/new_britain · mutopiaproject.org …/new_britain.ly |
| Brahms' Lullaby (new) | Op. 49 No. 4, 1868 | (not used) | PD | en.wikipedia.org/wiki/Wiegenlied_(Brahms); Mutopia `LullabyBrahms-C.ly` |
| Auld Lang Syne (new) | Thomson 1799 | Burns 1788 | PD | en.wikipedia.org/wiki/Auld_Lang_Syne |
| **Scarborough Fair** | The known tune exists only in MacColl's 1947 notation of Mark Anderson (no recording), printed in MacColl & Seeger, *The Singing Island* (1960). Carthy learned it from that book, then Simon & Garfunkel from Carthy (they later settled with Carthy's publisher). UK law recognises editorial copyright (*Sawkins v Hyperion*, 2005). | traditional | **Doubtful → removed** | en.wikipedia.org/wiki/Scarborough_Fair_(ballad) · Kidson 1891: archive.org/details/imslp-tunes-kidson-frank (a different, G-major tune) |
| Scale, arpeggio, drum grooves | generic patterns | – | no issue | – |

---------------------------------------------------------------------------------------------------------------------------

## 4. Playability on our instruments: tempo and density for hand tracking

| | Shortest gap between notes | Shortest same-target repeat | Max targets at once | Comment |
|---|---|---|---|---|
| Piano (proposed) | 0.125 s (Happy Birthday's dotted "Hap-py": the short G4 lies 0.125 s before A4) | 0.25 s (Jingle Bells E E) | 1 | Fine at the default ~30 Hz hand tracking. The key must rise to half travel (S_RESET .5) before it re-fires. Für Elise's E5–D#5 alternation (0.25 s, white/black neighbours) is the hardest figure, so it comes last. |
| Drums (proposed) | 0.2 s (the shuffle's swung "a") | 0.2 s | 2 | The old set needed a third hand 12 times (Disco 8, Bossa 4). The nod's debounce is 0.18 s (`DrumSpec.cs:246`): fine for four-on-the-floor at 90–108 bpm. |
| Harp (proposed) | 0.30 s (Jingle at 100) | 0.30 s | 1 | The 70 ms per-string retrigger guard and the new harp's 4 ms catch are far below this. A sweep that overshoots by one string counts a mistake (section 5, B2 note). |

Lengths: every lesson takes under 1 minute including the countdown. The longest is harp Silent Night, 0.86 min; the
limit asked for is 2 minutes.

---------------------------------------------------------------------------------------------------------------------------

## 5. Teaching quality and the Unity lesson code

### 5.1 How it behaves (confirmed in `Tutorial.cs`)

- **Start** (`Start`, :151-188): a 3 s countdown shows 3-2-1 on the panel with "Get ready". Then the first block
  approaches.
  - It approaches at 0.18 m/s from 0.35 m (piano, drums) or 0.30 m (harp), so it lands 1.94 s (harp 1.67 s) after the
    countdown.
- **Wait mode** (`Update`, :270-279): when the current event's block lands, the clock freezes. Every block stops, and the
  target pulses and glows (`Animate`, :394-417; harp strings also `Highlight`). The hint text turns white.
  - It waits forever. There is no skip and no help after a long wait; see B6.
- **Hits** (`OnPlay`, :208-255): only the current event counts, and only while waiting or ≤ 0.15 s before it lands.
  - Playing earlier does nothing: no feedback, no mistake.
  - Chord and drum combos need every target, in any order.
  - Plays come from the recorder stream, so a fingertip, a stick, a nod, the mouse or the keyboard all count. Tape-deck
    playback does not.
- **Mistakes:** a wrong note in reach adds 1 to "Mistakes: N" and flashes the waiting blocks red for 0.3 s. The wrong note
  still sounds, and progress never goes backwards. Good for beginners.
- **Blocks:**
  - **Piano:** 60 % of key width; height 1.2–6 cm encodes the length. At 120 bpm a 16th-note gap is 4.5 cm of travel; the
    tightest pair is Birthday's 0.25-beat G4, 2.25 cm behind a 1.3 cm block, which is readable.
  - **Drums:** discs half a head wide, max 6 cm, at opacity 0.38.
  - **Harp:** capsules come in along the line of sight and stop 0.25 m from the eyes.
- **Progress:** a bar plus "N of M" (targets) and "Mistakes: N", on the palm panel (`MenuController.LessonRows`,
  MenuController.cs:486-500).
- **Done** (`Finish`, :294-300; MenuController.cs:501-511): "Lesson complete!" in green, then "N notes · M mistakes ·
  T.T s", then Replay and Courses. Nothing else happens: no sound and no next step.
- **Order:** the course list is the JSON order filtered by instrument (`CourseRows`, MenuController.cs:369-377). The
  proposed JSON's order *is* the recommended menu order.

### 5.2 Bugs and gaps (file:line)

| # | Severity | Where | What happens | Fix |
|---|---|---|---|---|
| B1 | **high** | `Lessons/Tutorial.cs:493` (disc at piece-local `(0, dist + DiscLift, 0)`), `:434` (glow at `(0, DrumGlowLift, 0)`); `Drums/DrumSpec.cs:164-167` (`CymbalProfile`: surface .016 m at r .03, .012 at .06, .006 at .6 r); `Lessons/Shaders/LessonBlock.shader:21-22` (`ZWrite Off`, `ZTest LEqual`); `DrumSpec.cs:108` + `DrumMath.cs:38` (bell below .22 r) | (1) On the hi-hat, crash and ride, the waiting disc (1–11 mm up) and the glow (2 mm up) are inside the cymbal, so the player sees a target vanish just as it lands. Evidence: `Builds/Shots/lessons/lessons-drums-waiting-close.png`, where the hi-hat shows only a thin cyan ring. (2) Hitting where the disc points, the centre, plays the crash's or ride's **bell**: `drums-cymbal-edge-vs-centre.wav` (tutorials listening set). (3) Toms, crash and ride discs sit 0.73–0.78 m from the nearer shoulder. | Land every target except the pad at **0.55·r from the centre, on the side facing the player** (the piece-local direction toward the head, in the head plane), and on the surface: cymbals `y = profile(0.55 r) + 0.002` (≈ 9 mm), drums `DiscLift`. Use 0.45 r on the hi-hat, whose top zone is < .75 r. That lands crash and ride in the bow, and brings every target 8–12 cm closer. Glows follow. Update the `TutorialTests` drum disc pose asserts. |
| B2 | medium | `Tutorial.cs:217`, `:221`, `:225` (each match requires `!cur.Hit[i]`) then `:230-237` | A second strike of a target already hit in the current event is a **mistake**, with a red flash. Examples: a hand bouncing on the hi-hat while the snare of the same `hat+snare` is still due; a chord key re-triggered. | Before counting a mistake, ignore a play that matches an already-hit target of `cur`. Optionally also ignore one that matches the previous event's target within ~120 ms. |
| B3 | low | `Tutorial.cs:272-273` | `elapsed` runs from the end of the countdown, but the first note cannot be played before 1.79 s. The summary time is ~1.8 s too long. | Start `elapsed` at the first hit, or subtract `Due0 − Early`. |
| B4 | low | `Menu/MenuText.cs:43-49` | Drum lessons end with "24 notes · …". | Say "hits" for drums. |
| B5 | low | `Lessons/LessonHints.cs:15-20, 53`; `Piano/PianoSpec.cs:89-93` | Hints say "Play E4". The key labels print the octave digit only on C ("C4", then "D", "E" …), so the hint text never appears on the keys except for the Cs. | Optional: make the piano hint the printed label ("Play E", "Play C5", "Play D#"); the falling block shows which one. Change `LessonHints.For` and regenerate the JSON hints together (`make-lessons.mjs`), since `TutorialTests` asserts they match. Harp hints ("Pluck C4 (red)", "(blue)") match the string colours. |
| B6 | UX gap | `MenuController.LessonRows` (MenuController.cs:501-511); `Tutorial.Update` Waiting | **Completion** is text only: no next-lesson row, no sound, nothing remembered, so there is no reason to come back. **Stalls:** if a target is out of reach or misunderstood (a tom, the pad), wait mode waits forever. | (a) A third row on Done, **"Next: <title>"** (the next lesson of that instrument). (b) A calm, real-instrument cadence on Done: piano tonic chord, harp rolled chord, a crash. (c) Persist completion and best mistakes per lesson in the settings store, and show a check in the course list. (d) After 8 s waiting on one event, the tour's re-demo and `Forgiving` pattern (sunny-tour.md 7.4); after 15 s, accept any hit on that instrument. |
| B7 | info | `Tutorial.cs:364-377` (kick target = the pad) | The nod also counts, since it is the same kind, but only Sunny's `tip_lesson_kick` says so. | Keep the tip. The proposed "Nod for the kick" lesson teaches it. |

Checked and fine:
- harp hint colours;
- "N of M" counts targets;
- other instruments are never mistakes;
- a lesson stops when its instrument leaves the layout (`LessonsModule.cs:135-140`);
- hidden instruments draw nothing;
- the hint font (`MenuPainter.cs:212`) drops to 80 px above 14 characters. The longest proposed drum hint is 14
  characters ("Hi-hat + Snare"); the removed "Hi-hat + Kick + Snare" was 21.

---------------------------------------------------------------------------------------------------------------------------

## 6. The harp in lessons: new against old, measured

**Per lesson** (dry; at tempo; Frère and Jingle at the proposed 88 / 100 bpm):

| Lesson | Clarity mean (dB) old / new | Worst note | Repeated notes | Wash (dB) | Blur count | Note-level spread P10–P90 (dB) | Notes < 5 dB | Clicks |
|---|---|---|---|---|---|---|---|---|
| ode (92) | 13.7 / 11.6 | 6.7 / 5.1 | 11.3 / 9.8 | −17.6 / −16.8 | 0.72 / 0.66 | 7.1 / **2.9** | 0 / 0 | 0 / 0 |
| twinkle (96) | 14.8 / 12.5 | 8.1 / 7.1 | 11.6 / 9.3 | −19.9 / −21.2 | 0.56 / 0.49 | 5.5 / **3.1** | 0 / 0 | 0 / 0 |
| frere (88) | 12.9 / 11.4 | 4.4 / 3.3 | 21.4 / 20.9 | −13.9 / −12.7 | 1.23 / 1.19 | 6.1 / **2.8** | 2 / 5 | 0 / 0 |
| arpeggio (96) | 16.9 / 13.0 | 8.7 / 8.4 | – | −20.8 / −14.0 | 1.25 / 1.50 | 5.3 / **2.1** | 0 / 0 | 0 / 0 |
| lullaby (80) | 14.8 / 13.3 | 2.7 / 4.8 | 8.7 / 7.2 | −15.8 / −16.6 | 1.04 / 1.10 | 5.8 / **3.5** | 3 / 4 | 0 / 1* |
| jingle (100) | 14.8 / 12.2 | 4.0 / 3.3 | 13.6 / 10.9 | −20.1 / −19.7 | 0.42 / 0.46 | 4.2 / **2.4** | 1 / 2 | 0 / 0 |
| auld (84) | 15.9 / 13.9 | 4.8 / 4.0 | 16.1 / 13.7 | −17.8 / −17.7 | 1.02 / 0.91 | 5.7 / **3.9** | 1 / 3 | 0 / 0 |
| grace (92) | 14.1 / 12.9 | 4.2 / 4.2 | 14.8 / 13.2 | −15.1 / −13.8 | 1.00 / 0.82 | 5.8 / **3.3** | 1 / 2 | 0 / 0 |
| silent (92) | 23.4 / 20.0 | 4.6 / 5.2 | 33.2 / 26.3 | −24.4 / −21.6 | 0.69 / 0.71 | 9.3 / **2.9** | 1 / 0 | 0 / 0 |
| scarborough (removed, 104) | 20.2 / 16.2 | 0 / 3.2 | 24.1 / 20.0 | −22.4 / −19.7 | 0.53 / 0.67 | **11.6** / 2.4 | 1 / 2 | 0 / 0 |

\* The detector flags one frame at 34.915 s. It is the attack of a caught re-pluck of C4: 4 ms catch, then the onset,
9 ms after the finger and 4 dB brighter in the 6 kHz band than a fresh pluck. It is not a discontinuity: rendering the same caught re-pluck alone shows a clean onset.

At the current tempos, Frère (100) measured clarity 11.2 / 9.6 with notes under 5 dB 2 / 8, and Jingle (112) 13.1 / 10.7
with repeated notes 12.0 / 9.4.

For reference, the piano lab's lesson wash with the new piano is −13.7 (Frère) to −20.3 (Twinkle), so the harp sits in the
same range.

**Every string, at the lesson stroke** (0.55 m/s poke at its play point; `out/harp-metrics.json` → `strings`):

| | Old | New |
|---|---|---|
| Loudness C3 → C6 | −26.2 → −35.6 LUFS, range **12.8 dB**, neighbour steps up to **7.1 dB**. E5 −33.5 sits between D5 −26.4 and F5 −29.2; G4 −29.2 sits next to F4 −25.5 | −26.1 → −29.5, range 3.4 dB, steps ≤ 0.3 dB: the calibrated −0.1 dB per semitone tilt |
| Attack (5 ms RMS to −3 dB of peak) | ≤ 3 ms on all 22 | ≤ 3 ms on all 22 |
| Time to −20 dB, C3 / C4 / C5 / C6 | 0.93 / 0.86 / **0.36** / 0.41 s: the treble plinks | 1.54 / 1.01 / 0.77 / 0.64 s |
| Attack centroid | 1530–2438 Hz, erratic | 1757–2306 Hz, smooth |
| HF share > 5 kHz, first 100 ms | max −26 dB (G3) | max −23.8 dB (C6, the brightest; only the arpeggio uses it); not harsh |
| Inharmonic (metallic) peaks | 0 | 0 |

**Palm damping** (`tools/harp-damping.mjs` → `out/harp-damping.json`, broadband; the palm is re-sent every 0.2 s as
harp.md 5.7 prescribes):

| Case | Old | New |
|---|---|---|
| A lesson phrase (E E F G pokes), the palm on C4–G4 | −68.7 dB re the phrase at +0.3 s, then digital silence | **−36.3 dB** at +0.3 s, −44 dB at +1 s |
| The tour's sweep (D4–B4), the palm on C4–C5 1.9 s later | −56 dB re the moment the palm lands, at +0.3 s | **−12.4 dB** re that moment at +0.3 s (−44 dB re the sweep), −18.6 at +1 s |
| The same with the free strings muted (no sympathy) | – | −46 dB at +0.1 s |
| A finger stopping a ringing G4 | – | residual −46 dB re the note |

The palm silences the strings under it within 0.1 s. What remains is the octave-lower strings (C3–B3), which rang in
sympathy through the body modes. Halving the bridge conductance `GBB`, or even quartering it, lowers that hum by only
1.9–2.7 dB.

**What would make notes speak more clearly?** `tools/harp-variants.mjs` → `out/harp-variants.json`, mean clarity in dB:

| | Frère | Jingle | Ode | Arpeggio |
|---|---|---|---|---|
| Old harp | 11.2 | 13.1 | 13.7 | 16.9 |
| New, default | 9.6 | 10.7 | 11.6 | 13.0 |
| New, `BRIGHT_DB` +2 | 9.8 | 10.8 | 11.7 | 13.1 |
| New, pluck noise ×2 (`FRIC` .3) | 9.7 | 10.7 | 11.6 | 13.0 |
| New, sympathy −6 dB (`GBB` ×.5) | 9.5 | 10.6 | 11.5 | 12.8 |
| **New, T60 ×0.8 (`T60_C3` 7.0 → 5.6)** | **11.3** | **12.5** | **13.6** | **15.5** |
| **New, lesson ~12 % slower** (Frère 88, Jingle 100, Ode 84) | **11.4** | **12.2** | **13.0** | – |

**Verdict.** Yes, the new harp is genuinely better for the lessons:
- even, harp-like voicing, against the old random ±7 dB per string, which makes melodies lurch;
- treble notes that sustain;
- no clicks or metallic notes;
- a natural sympathetic halo.

Its one weakness in lessons is ring-over in quick passages: 1.2–3.9 dB less onset contrast, and 0.5–2.7 dB less on
repeated notes at the proposed tempos (6.9 dB in Silent Night, where both harps stay above 26 dB). Only decay length moves
it.

What to change:
1. **Lessons (done in `lessons-proposed.json`):** Frère 100 → 88, Jingle 112 → 100. Keep the other harp lessons at ≤ 96
   bpm with no repeated 16ths.
2. **Model, only if the device test still finds blur:** `T60_C3` 7.0 → 5.6. It is one constant, and it restores the old
   harp's clarity in every lesson. The cost is a 20 % shorter ring in free play. Re-run `calibrate.mjs` afterwards (loudness
   is calibrated over the first 400 ms, so the change is small). Leave `BRIGHT_DB`, `FRIC` and `GBB` alone for this.
3. **Palm:** keep. If the remaining hum is judged distracting on the device, have `HarpModule`'s palm also `Damp` the
   strings an octave below the covered ones while they ring in sympathy. That is a UX choice, not physics; it needs a lab
   pass. `GBB` will not do it.
4. **Device check:** the lower glow on bass strings. G3's play point is now 0.78 m, A3's 0.80 m, against 0.95 m before.
   It is used by Frère, Grace and Auld Lang Syne. Check it is comfortable from the seat.

---------------------------------------------------------------------------------------------------------------------------

## 7. Recommended order and new lessons

### 7.1 The first five minutes

The tour (sunny-tour.md) already gives:
- the answering phrase of Ode to Joy on five keys;
- a drum hit and a nod;
- a harp sweep.

After the tour, each instrument's first menu lesson continues that moment in under 30 s, one target at a time:

| Instrument | Proposed order (easiest first; **new** in bold) |
|---|---|
| Piano | Ode to Joy (whole) · Mary Had a Little Lamb · Twinkle Twinkle (whole) · C major scale · Frère Jacques · London Bridge · Old MacDonald · Jingle Bells · Row Row Row Your Boat · Baa Baa Black Sheep · **Brahms' Lullaby** · **Auld Lang Syne** · Happy Birthday · Silent Night (whole verse) · Für Elise (opening) |
| Drums | **First beat** · **Add the hi-hat** · Basic beat · Half-time groove · Rock beat · Shuffle · Tom fill · Groove with fills · Waltz (3/4 ride) · Bossa nova · **Nod for the kick** · Disco (nod the kick) |
| Harp | **Ode to Joy** · Twinkle Twinkle · Frère Jacques · C major arpeggio · **Brahms' Lullaby** · Jingle Bells · **Auld Lang Syne** · Amazing Grace · Silent Night |

### 7.2 A reason to come back

- **Data (in the JSON).** Longer, beautiful pieces at the end of each list: Brahms' Lullaby, Auld Lang Syne, the whole
  Silent Night, Für Elise, Amazing Grace.
- **Code (B6).** A "Next" row, a quiet real-instrument cadence when a lesson completes, and completion checks plus best
  mistakes in the course list.

### 7.3 Verified candidates for later

Not in the JSON. All public domain; the notes were read from the cited scores and are harp-safe (white keys only).

| Tune | Tokens | bpm | Source |
|---|---|---|---|
| Hot Cross Buns (a 15 s, 3-key starter if the user wants one shorter than Ode) | `E4 D4 C4:2 E4 D4 C4:2 C4:0.5 C4:0.5 C4:0.5 C4:0.5 D4:0.5 D4:0.5 D4:0.5 D4:0.5 E4 D4 C4:2` | 100 | en.wikipedia.org/wiki/Hot_Cross_Buns_(song) |
| Au clair de la lune, 8 bars | `C4 C4 C4 D4 E4:2 D4:2 C4 E4 D4 D4 C4:4` ×2 | 100 | fr.wikipedia.org/wiki/Au_clair_de_la_lune |
| Lightly Row | `G4 E4 E4:2 F4 D4 D4:2 C4 D4 E4 F4 G4 G4 G4:2 G4 E4 E4:2 F4 D4 D4:2 C4 E4 G4 G4 C4:4 D4 D4 D4 D4 D4 E4 F4:2 E4 E4 E4 E4 E4 F4 G4:2 G4 E4 E4:2 F4 D4 D4:2 C4 E4 G4 G4 C4:4` | 108 | de.wikipedia.org/wiki/Hänschen_klein |
| Simple Gifts (harp G4–G5) | in the research notes. It is long, with many eighths: a later harp lesson | 100 | commons.wikimedia.org/wiki/File:SimpleGifts.png |
| Ode to Joy, 16 bars (A A′ B A′) | adds `D4 D4 E4 C4 D4 E4:0.5 F4:0.5 E4 C4 D4 E4:0.5 F4:0.5 E4 D4 C4 D4 G3:2` then A′ | 108 | Wikipedia score |
| Ode to Joy, two hands | the melody plus a left-hand root on each bar's downbeat (C3 / G2) as a second target | – | arrangement; needs a design pass |

Not proposed:
- **Greensleeves:** needs G# and F#, so it is piano-only, and its notes were not verified in this pass.
- **Michael, Row the Boat Ashore:** the familiar second half comes from a 1954 adaptation (UNVERIFIED public-domain
  status).

---------------------------------------------------------------------------------------------------------------------------

## 8. Changes list (`lessons-proposed.json` against the current `lessons.json`)

`lessons-proposed.json` is generated by `tools/make-lessons.mjs` from `tools/lessons-src.mjs`. It keeps:
- the same top-level keys and lesson / event / target keys, in the same order;
- the same field meanings;
- hints in today's `LessonHints.For` format, so `TutorialTests`' JSON-vs-code hint check still passes.

`generatedFrom` and `sourceSha1` point at `lessons-src.mjs`.

**Per lesson.** Status: = unchanged · R reordered · T title · N notes · B bpm · + added · − removed.

| Id | Change |
|---|---|
| piano-ode | N (8 bars, +15 notes), R 3 → 1 |
| piano-mary | R 5 → 2 |
| piano-twinkle | N (whole song, 14 → 42 notes), R 2 → 3 |
| piano-scale | R 1 → 4 |
| piano-frere | R 12 → 5 |
| piano-london | R 10 → 6 |
| piano-macdonald | R 11 → 7 |
| piano-jingle | R 4 → 8 |
| piano-row | R 8 → 9 |
| piano-baa | N (standard 8 bars, 52 → 38 notes), R 6 → 10 |
| piano-lullaby | + |
| piano-auld | + |
| piano-birthday | N (bar 6 rhythm, end `C5:2`), R 7 → 13 |
| piano-silent | N (whole verse, 23 → 46 notes), R 13 → 14 |
| piano-elise | R 9 → 15 |
| drums-first | + |
| drums-hat | + |
| drums-beat | R 1 → 3 |
| drums-halftime | R 9 → 4 |
| drums-rock | N (kick on 3&, 4 bars + crash), T "Rock beat", R 3 → 5 |
| drums-shuffle | R 5 → 6 |
| drums-fill | T "Tom fill", R 2 → 7 |
| drums-fill-round | N (no trailing ride), T "Groove with fills", R 7 → 8 |
| drums-waltz | R 8 → 9 |
| drums-bossa | N (kick + clave, no hat), B 120 → 100, T "Bossa nova", R 6 → 10 |
| drums-nod | + |
| drums-disco | N (hat on the off-beats), B 116 → 108, T "Disco (nod the kick)", R 4 → 12 |
| harp-ode | + |
| harp-twinkle | N (whole song), T "Twinkle Twinkle" |
| harp-frere | B 100 → 88, R 7 → 3 |
| harp-arpeggio | R 1 → 4 |
| harp-lullaby | + |
| harp-jingle | B 112 → 100, R 3 → 6 |
| harp-auld | + |
| harp-grace | N (line 3), R 5 → 8 |
| harp-silent | N (whole verse), R 6 → 9 |
| harp-scarborough | − |

**Counts.** 29 → 36 lessons; piano 13 → 15, drums 9 → 12, harp 7 → 9; events 826 → 1148; targets 966 → 1263. List notes
that tests use stay the same: `piano-scale` "15 notes", `drums-beat` "8 beats".

**Tests to update when this is dropped in.** These are the Unity project's own tests that hard-code the current data:
- `Tests/EditMode/Lessons/LessonsTests.cs:82-86`: counts 13/9/7 → 15/12/9; 826 → 1148; 966 → 1263.
- `Tests/EditMode/Lessons/TutorialTests.cs:507`: 826 hints → 1148.
- `Tests/EditMode/Menu/MenuScreenTests.cs`:
  - `:26-28`: per-instrument counts.
  - `:39`, `:51`, `:74`, `:471`: "29 courses" → "36 courses".
  - `:184`: "13 courses,9 courses,7 courses" → "15 courses,12 courses,9 courses".
  - `:197`: maxScroll `n·130 − 630` → piano 1320, drums 930, harp 540 (were 1060 / 540 / 280).
- `Tests/EditMode/Menu/MenuPokeTests.cs`:
  - `:135`: 1060 → 1320.
  - `:169`: the second piano course is now `piano-mary`, not `piano-twinkle`.
  - `:180`: the last piano course is now `piano-elise`, not `piano-silent`.

Unaffected; they read the library at run time and the lessons they use still exist with the same opening:
- `PlayMode/Audio/DrumKitPlayTests.cs:213` (`drums-rock`, first 12 events);
- `PlayMode/Harp/HarpPhysicalTests.cs:251` (`harp-jingle`, 8 pokes);
- `PlayMode/Piano/PianoNewSoundPlayTests.cs:118` (`piano-ode`);
- `TutorialTests.cs:213` (`piano-twinkle` opens C4 C4).

`PlayMode/Harp/HarpLoadTests.cs:20` hard-codes the harp-jingle beats "at 112 bpm" as a load scenario. It stays valid as a
benchmark, but its comment would be stale.

The older lesson export `export-lessons.mjs` expects 29 lessons. It exports the original 29-lesson set and is not used
for this file.

---------------------------------------------------------------------------------------------------------------------------

## 9. Files

- `Docs/11-first-five/tutorials-audit.md`: this report.
- `lessons-proposed.json`: the drop-in lesson set (now `Assets/MusicRoom/Lessons/Resources/lessons.json`).
- The audit's `tools/` (Node, kept with the audit, not in this project):
  - `lessons-src.mjs`, `make-lessons.mjs`, `render-tutorials.mjs`, `harp-variants.mjs`, `harp-damping.mjs`;
  - `out/stats.json`, `out/harp-metrics.json`, `out/harp-variants.json`, `out/harp-damping.json`,
    `out/render-report.json`.
- The `tutorials` listening set:
  - `harp-<lesson>-old.wav` / `-new.wav`: 11 pairs, including `scarborough-removed` and `firstrun-moments`;
  - `piano-<lesson>.wav` (15);
  - `drums-<lesson>.wav` (12);
  - `drums-beat-fingers.wav`, `drums-cymbal-edge-vs-centre.wav`;
  - `README.txt`.
- Rebuild: `node tools/make-lessons.mjs && node tools/render-tutorials.mjs` (about 2 minutes; Node 26, no packages).
