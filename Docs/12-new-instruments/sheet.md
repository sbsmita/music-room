# 12 · Sheet music on the piano's music rest (key: `sheet`, implementation-ready spec)

**What this is.** A real music book stands open on the piano's music rest. It shows the current lesson's notes, with the
note letter under every note (the same letter that is printed on the key), a soft warm tint on the next note, and pages
that turn themselves (or when you tap a page corner). With no lesson running, it opens at a contents page: tap a song's
name and that lesson starts. Everything on the page is drawn by our own code: no music font, no images, no third-party
assets. Text uses the project's own font (TextMesh Pro's LiberationSans, already shipped).

**Status.** Designed and prototyped. The Node engraver renders every piano lesson; 1637 deterministic checks pass; the
seat view was simulated pixel for pixel at Quest 3S resolution. Nothing was written in the Unity project or the clones.

**Builds on.** `12-new-instruments/platform.md` section 13.5 (the sheet hooks: `IPianoApi.MusicRestPose`,
`ITutorial.CurrentEvent`, `InstrumentDescriptor.SheetAnchor`, `LessonSpec.Notation`, `SheetModule` Order 45, no rig,
no `IInstrument`, ≤ 2 draws, 0 B per frame). Where this spec needs more than that, it is listed in section 12.

| Lab and outputs | Where |
|---|---|
| Node engraver (no packages) | the sheet lab (Node prototype: `lib/*.mjs`, `engrave-all.mjs`, `seat-view.mjs`, `tests.mjs`, `page-sound.mjs`, `export-font.mjs`, `glyph-sheet.mjs`, `lessons-meta.json`) |
| Every piano lesson, engraved | `Builds/Listen/sheet/<lesson>.png` (15), `piano-ode-first-two-staff.png`, `piano-ode-hands-two-staff.png`, `_contents.png`, `_book-NN-MM.png` (every spread), `_glyphs.png` |
| What the headset shows | `Builds/Listen/sheet/piano-ode-seat-{home,fitted,lean}.png` (+ `-zoom4x`), `contents-seat-home.png` |
| Marker and hover | `Builds/Listen/sheet/piano-ode-marker.png`, `_contents-hover.png` |
| Page-turn sound | `Builds/Listen/sheet/sheet-page-turns.wav` (48 kHz, 24-bit, stereo, −18.0 LUFS, −1.10 dBTP), `sheet-README.txt` |
| Vectors for the C# code | the lab's `out/test-vectors.json`, `page-sound-vectors.json`, `book.json` (page map), `sheet-font.bytes` (frozen in `Tests/EditMode/Sheet/Reference~/`) |

Run (in the sheet lab): `node engrave-all.mjs 6 && node seat-view.mjs piano-ode home && node tests.mjs && node page-sound.mjs && node export-font.mjs` (about 20 s).

---------------------------------------------------------------------------------------------------------------------------

## 0. Decisions

1. **A real book, not a panel.** An oblong cloth-bound music book, open on the rest: two cream pages of 320 × 244 mm
   (open 640 mm wide), resting on the rest's ledge and leaning back with it at 0.3 rad. Warm black print, a gentle
   gutter curl, a red silk ribbon, and a small brass reading lamp clipped to the rest. Calm, lit by the room.
2. **Sized for the Quest 3S from the real seat.** The rest is **0.69–0.80 m** from the eyes, not 0.5–0.6 m (section 1).
   Staff space **4.9 mm**, note letters **5.8 mm** cap height: **6.8–7.3 display px** per staff space and **8.1–8.6 px**
   letters from the home seat, more after `FitToPlayer`. Readable without leaning (simulated, section 1.3).
3. **Four bars a line, one song a page or a spread.** 4/4 songs print 4 bars a line (11–18 notes); 3 lines a page with
   the title. A song that needs two pages starts on a left page, so **no lesson today ever turns a page mid-song**.
4. **Letters under every note,** exactly the key labels (`PianoSpec.LabelText`): `C4`, `D`, `D#`, `C5`, small raised
   sharp, small octave digit. Bold, in one row per line. Tied continuations, rests and the accompaniment get none.
5. **The current note: a soft warm tint** (lamplight amber, multiply), drawn by the page shader from a uniform. No
   glow, no colour change on mistakes (the falling blocks already flash).
6. **Page turns:** automatic at lesson start (a single turn or a riffle to the song, during the 3 s countdown) and
   whenever the next note is on a page that is not open. Manual: **tap a page's bottom outer corner** (forward on the
   right page, back on the left). **Tap the ribbon** to go back to the contents. A bent, corner-led leaf animation with
   a paper sound from the engine's one-shot pool.
7. **Free play: the contents spread.** Two pages listing the 15 piano songs; **tap a title to start that lesson.**
8. **Drums: no notation.** Harp, marimba: a small stand **later** (P1 marimba, P2 harp), reusing the renderer.
   Handpan, hand drums, theremin, singing bowls: none.
9. **Rendering: our CPU rasteriser at lesson load.** A Burst job fills an R8 coverage texture per page (the same
   algorithm as the Node lab, test vectors shared); a `Texture2DArray` of 4 slices, 4.27 MB with mips; **2 draws**.
   Text is the TMP font's own outlines rasterised into the page (a requested amendment to ARCHITECTURE 14, section 12).
10. **Two-staff music works** (brace, bass clef, chords, ledger lines below the bass staff). No two-hand lesson exists
    yet; the first-run song's chords and a proposed "Ode to Joy (both hands)" are rendered as proof.

---------------------------------------------------------------------------------------------------------------------------

## 1. Readability from the seat (Quest 3S)

### 1.1 Where the book really is
From `PianoBody.MusicRest` and the seat frame (Unity, x right, y up, z ahead of the eyes):
- the rest's slot is at back `Name + 0.087 = 0.240` m behind the white-key fronts; its front face bottom is at
  y = key tops + 0.039, leaning back 0.3 rad (17.2°); the ledge top is 7.3 mm up the face;
- home (`Rig` home `(0, .72, .45)`, eye 1.20 m): key fronts 0.45 m ahead, key tops 0.48 m below the eyes;
- fitted (`ILayoutService.FitToPlayer`, sunny-tour 7.5): key fronts 0.36 m ahead (0.30–0.42), key tops eye − 0.48.

So the rest's face is 0.60–0.69 m ahead and 0.43 m below the eyes: **0.69–0.80 m away**, not the 0.5–0.6 m in the task
brief. At 0.55 m everything would be 1.4× larger in pixels; designing for 0.55 m would have made the music unreadable.

The book spans about ±22° across and from 14° to 32° below the horizon (home). Its top edge is 0.20 m below eye level,
so it hides only the lower part of the wall behind the piano. The page is almost square-on to the gaze: the lean of the
rest (17°) nearly matches the downward look (21°), so the page is tilted 0.2°–18° from square-on (cosine ≥ 0.95).

### 1.2 Display resolution and thresholds
Quest 3S: 1832 × 1920 per eye over about 96°, about **20 px per degree** at the lens centre (Quest 3: about 25).
One display pixel is 0.70 mm at 0.80 m and 0.61 mm at 0.70 m.

Thresholds used (pixels at 20 px/deg, worst row of the page):
- **staff space ≥ 6.5 px:** a notehead is then one space tall and its line or space differs by ≥ 3 px; ledger lines
  can be counted;
- **letter cap height ≥ 7.5 px:** the bold capitals A–G separate at a glance (strokes ≥ 1.2 px);
- titles ≥ 10 px; small print (credits, page numbers, the contents sub-line) about 6 px: readable with a look, never
  needed.

### 1.3 Result (from `seat-view.mjs`, which also prints this table)

| Seat | Eye distance (m) | Staff space (px) | Letter cap (px) | Corner from the right shoulder |
|---|---|---|---|---|
| Home (no fit) | 0.774–0.799 | 6.8–7.3 | 8.1–8.6 | 0.81 m |
| Fitted (key fronts 0.36 m) | 0.688–0.724 | 7.4–8.2 | 8.7–9.6 | 0.72 m |
| Leaning 8 cm (reaching for a corner) | 0.613–0.660 | 7.9–9.1 | 9.3–10.8 | 0.64 m |

`seat-view.mjs` ray-casts the open book on the rest through a rectilinear 20 px/deg camera with 4 × 4 rays per pixel
(the best a GPU filter can do) and a 0.4 px lens/aperture blur. `piano-ode-seat-home-zoom4x.png` is the evidence: every
letter reads and every note sits visibly on its line or space. The real headset adds lens blur off-axis and filtering
loss; small head movements give some of it back. **Device item S1** confirms with a person.

Chosen sizes (`lib/engrave.mjs` `STYLE`):

| Item | Size | Notes |
|---|---|---|
| Staff space S | 4.9 mm | staff 19.6 mm tall (about 2.6× a printed beginner book) |
| Staff line / ledger / stem / bar line | 0.15 / 0.18 / 0.13 / 0.17 S | a little heavier than print for 20 px/deg |
| Note letters | 5.8 mm cap, LiberationSans + 0.035 em synthetic bold | sharp at 0.62 raised 0.35 cap, octave digit at 0.72 (the key labels' layout) |
| Title | 7.6 mm cap, + 0.03 em bold | |
| Credit, tempo mark, page number | 4.2 mm cap | |
| Contents rows | title 6.0 mm cap, row pitch 21.5 mm | each row is a 21.5 × 279 mm poke target |

### 1.4 Notes per line, lines per page (every lesson)

Page 320 × 244 mm, margins 13 top, 18 bottom, 18 outer, 23 at the spine: 279 × 213 mm of print. "Stretch" is the
spacing used divided by the ideal spacing (1 = relaxed; letters never get closer than 0.45 S).

| Lesson | Meter | Bars | Pages | Bars per line | Notes per line | Stretch |
|---|---|---|---|---|---|---|
| piano-ode | 4/4 | 8 | 4 | 4+4 | 15+15 | 0.64 0.68 |
| piano-mary | 4/4 | 8 | 5 | 4+4 | 13+13 | 0.73 0.79 |
| piano-twinkle | 4/4 | 12 | 6 | 4+4+4 | 14+14+14 | 0.69 0.74 0.73 |
| piano-scale | 4/4 | 4 | 7 | 4 | 15 | 0.65 |
| piano-frere | 4/4 | 8 | 8 | 4+4 | 14+18 | 0.68 0.62 |
| piano-london | 4/4 | 8 | 9 | 4+4 | 13+11 | 0.73 0.89 |
| piano-macdonald | 4/4 | 8 | 10 | 4+4 | 13+12 | 0.73 0.78 |
| piano-jingle | 4/4 | 16 | 12–13 | 4+4+4+4 | 11+15+11+14 | 0.85 0.70 0.90 0.75 |
| piano-row | 6/8 | 8 | 14 | 4+4 | 10+17 | 0.98 0.66 |
| piano-baa | 4/4 | 8 | 16–17 | 2+2+2+2 | 9+7+11+11 | 1.28 1.60 1.18 1.19 |
| piano-lullaby | 3/4, pickup | 17 | 18–19 | 5+4+4+4 | 15+14+12+10 | 0.66 0.83 0.92 1.07 |
| piano-auld | 4/4, pickup | 17 | 20–21 | 5+4+4+4 | 15+14+14+13 | 0.58 0.73 0.74 0.80 |
| piano-birthday | 3/4, pickup | 9 | 22 | 5+4 | 14+11 | 0.72 0.99 |
| piano-silent | 3/4 | 24 | 24–25 | 6+6+6+6 | 11+12+14+9 | 0.74 0.74 0.66 0.86 |
| piano-elise | 3/8, pickup | 9 | 26 | 4+3+2 | 16+14+5 | 0.85 1.15 1.60 |
| piano-ode-first (two staves) | 4/4 | 4 | – | 4 | 15 | 0.63 |
| piano-ode-hands (two staves) | 4/4 | 8 | – | 4+4 | 19+20 | 0.63 0.67 |

The book has 25 pages (2–26): the contents on 2–3, songs from 4, and three "Notes" pages (blank staves, 11, 15, 23)
where a two-page song would otherwise start on a right page. Baa Baa is the one 8-bar song on two lines of 2 bars:
its eighth notes with letters cannot fit 4 bars on a line.

---------------------------------------------------------------------------------------------------------------------------

## 2. The book: look and feel

### 2.1 Physical build (book frame: x along the spine's normal, u up the rest, n toward the player; mm)

| Part | Spec |
|---|---|
| Pages | Two 320 × 244 cream pages, spine at x = 0. Bottom edge 10.3 mm up the rest face (ledge 7.3 + board square 3). |
| Gutter curl | Page height above the cover plane: `lift(|x|) = 2.5 + 2.0 + 9.0 (1 − e^(−|x|/14)) − 4.0 (|x|/320)^2` mm (`lib/geometry.mjs`). The page texture follows the arc length, so print near the spine is never stretched. |
| Boards | Dark green bookcloth #2C453A, 2.5 mm thick, 3 mm proud of the pages ("squares"), rounded 1.5 mm. |
| Page block | Cream fore-edges #E6DCC4 with faint lines, visible at the outer edges and the bottom. |
| Ribbon | Deep red silk #7A1F23, 6 mm wide, leaving the book at the bottom of the spine and lying over the piano's top panel, cut in a V. It is the closest part of the book to the player. |
| Reading lamp | A small brass clip-on piano lamp on the rest's top edge, left of centre, with a dark green shade over the pages. The bulb is never visible (no glow). |
| Fit on the rest | The book (646 mm with boards) overhangs the 520 mm rest by 63 mm each side, as oblong books do; it stands 122 mm above the rest's top. |

### 2.2 Paper and print
- **Paper:** #F3EAD5 (sRGB), a fibre mottle of ±3 % from the baked `Sheet/paper_detail` texture (the same
  `paperDetail` value noise as the lab, 9 mm, 2.7 mm and 0.6 mm octaves).
- **Ink:** #2A2521, a warm black, never pure black. Cue notes (the accompaniment, "played for you") print at 50 %.
- **Shading in the shader:** the gutter `1 − 0.22 e^(−d/9) − 0.06 e^(−d/40)` (d = mm from the spine) and a faint
  outer-edge shade `1 − 0.05 e^(−d/6)`; Lambert with a 0.15 wrap and a faint grazing sheen (3 %, roughness 0.8), so
  paper never looks like plastic. Lit only by URP lights (moon, floor lamp, fire, candles; MR main light) plus the
  reading lamp below.
- **The reading lamp's light** is analytic inside the page shader: `_LampPos`, `_LampDir`, a cone of 55° with a soft
  edge and inverse-square falloff, colour #FFD9A8, tuned for a target of 0.45–0.55 linear paper luminance at the page
  centre in the night room (device item S2). It costs no URP additional light (the room already uses all four per-pixel slots).
- **Print layout:** title centred; tempo mark (a drawn note "= 108") at the left and the credit at the right under it;
  page number centred at the bottom. No lyrics anywhere.

### 2.3 Calm by design
No animation when nothing happens. The tint moves only when the lesson advances. Page turns take 0.5–0.6 s and sound
like paper, not a whoosh. No floating labels, no outlines, no highlights on the book itself.

---------------------------------------------------------------------------------------------------------------------------

## 3. Engraving rules (what the lab implements, and the C# code reproduces)

### 3.1 Data
The lesson JSON has beats, lengths and targets but no meter. The book's own table, `Sheet/Resources/sheet-book.json`
(from `lessons-meta.json`), gives per lesson id:

| Field | Meaning | Values used |
|---|---|---|
| `meter` | time signature | 4/4; 3/4 (Lullaby, Birthday, Silent Night); 6/8 (Row); 3/8 (Für Elise) |
| `unit` | the note value of one lesson beat | 4 (quarter); 8 for Für Elise (its 0.5-beat notes are sixteenths, as in the 1888 edition) |
| `pickup` | beats before the first full bar | 1 for Lullaby, Auld Lang Syne, Happy Birthday, Für Elise |
| `credit` | printed at the top right | composer or "Traditional" (all public domain) |
| `staves` | 1 or 2 | 2 only for two-hand music |
| `bookSong`, `eventOffset` | a lesson shown on another song's page | `piano-ode-first` → `piano-ode` + 15 (it is bars 5–8 of the Ode) |

A piano lesson with no entry fails `SheetDataTests`. Future packs may carry `meter`/`unit`/`pickup` on the `Lesson`
itself (optional JsonUtility fields); the table wins when both exist. Two-hand lessons add optional `targets[].hand`
("R" / "L") and `targets[].len` (a hand holding longer than the event); see platform 7.4 (at most 2 targets per event).

### 3.2 Notation
| Step | Rule |
|---|---|
| Spelling | From the target's `note` ("D#5"); black keys are sharps, as on the key labels. Key: C major, no key signature. |
| Staff position | `p = diatonic(note) − diatonic(bottom line)`, half-spaces; treble bottom line E4, bass bottom line G2. Ledger lines at even p ≤ −2 and ≥ 10. Pitches in use: C4–G5 (treble), G3 in Frère Jacques (two ledger lines), C2–G3 in the bass. |
| Bars | Pickup bar, then full bars; a final bar that completes the pickup stays short; otherwise the last bar is filled with rests (Old MacDonald). |
| Note values | Beats × (4 / unit) quarters, split at bar lines (tied) and into whole, dotted half, half, dotted quarter, quarter, dotted eighth, eighth, dotted sixteenth, sixteenth. |
| Rests | Gaps; split at beats; a full silent bar is a whole-bar rest centred. |
| Accidentals | Per bar and staff position; a natural cancels an earlier sharp in the bar (Für Elise: D♮5 after D♯5, tested). |
| Beams | Within the beat (simple meters: the quarter; 6/8 and 3/8: the dotted quarter, so Für Elise beams six sixteenths). Single flagged notes get flags. Dotted-eighth + sixteenth: a beamlet toward the dot. |
| Stems | Notes below the middle line up, on or above it down; chords by the farther note; a beam group by the sum of positions. Stems reach the middle line for far notes. Beam slant ≤ 1 half-space for steps, ≤ 2 for leaps. |
| Ties | Curves away from the stems; across a line break, half ties at both ends. |
| Letters | Under every onset (not ties, rests or cue notes), one row per staff, at least 0.65 S below the lowest ink and 2.3 S + cap below the staff. A chord prints its labels low to high ("C3 E G"). |
| Time signature | Digits of the text font, synthetic bold, 2 S tall, on the first line only. Clef on every line. |
| Grand staff | Brace, a line joining the staves, bar lines through both, treble letters between the staves, bass letters under the bass. |

### 3.3 Spacing, lines, pages
- **Ideal spacing** after a note of q quarters: `1.9 + 1.55 log2(1 + 2q)` S; **minimum**: the right extent of one
  column + the left extent of the next + 0.45 S, where extents include noteheads, accidentals, dots, flags and the
  letter's half width.
- **Line breaking:** every line gets the same number of full bars k, the pickup rides on line 1, a lone last bar joins
  the line before. k is chosen from 4, 2, 6, 3, 8, 5, 1 by cost: tight lines `((1 − st) 3.5)^2`, loose lines
  `((st − 1) 1.2)^2`, 2.5 per line, 0.6 per step down that list, 2.5 when k does not divide the bars; a line below
  0.6 of its ideal spacing (0.53 with a pickup) is ruled out. Lines are justified; a last line looser than 1.6 stays
  ragged.
- **Pages:** systems stack with gaps of 2.0–9 S (≤ 5 S on a song's last page). A song that needs two pages starts on a
  left page (else a "Notes" page is inserted) and its systems are balanced between the two pages.
- **Contents:** pages 2–3, 8 rows on the left page under "Piano Songs" and "Contents", the rest on the right. Row:
  number, title, dot leaders, page number; under it in grey the credit and "N notes".

### 3.4 Glyphs (all procedural, `lib/glyphs.mjs`, staff-space units)
Treble clef and bass clef (variable-width strokes along Catmull-Rom centrelines plus the ball and dots), noteheads
(black, half, whole: rotated ellipses, holes as reversed contours), stems, flags (Bézier-bounded shapes, 1 or 2),
beams and beamlets (parallelograms), dots, ledger lines, sharp, flat, natural, quarter / eighth / sixteenth / half /
whole rests, ties (crescents), bar lines (thin, final thin-thick), brace. `_glyphs.png` shows them large. Text:
`sheet-font.bytes`, 82 glyphs of LiberationSans flattened at 0.0015 em, 96 kerning pairs, 22 KB (generated by
`export-font.mjs`, like `PianoKfelt.g.cs`; ship `LiberationSans - OFL.txt` beside it).

### 3.5 The rasteriser (`lib/raster.mjs`, translates 1:1 to Burst)
Nonzero winding over all contours of a group at once (so overlapping strokes union exactly), 16 sub-scanlines per pixel
row, exact horizontal span coverage via an accumulator plus a prefix-summed delta row, stable insertion sorts, float32
storage, groups composited `dst + c (1 − dst)`. Flat arrays only: in C#, `NativeArray` scratch sized once (edges, active
list, crossings, two row buffers), no allocation per call.

---------------------------------------------------------------------------------------------------------------------------

## 4. The current-note marker

| Aspect | Spec |
|---|---|
| Look | A rounded rectangle over the note's column, from 1.4 S above the staff to 1.6 mm under its letter, as wide as the head or the letter + 2.4 mm, 3 mm soft edge. Colour #F0C884 applied as a multiply at strength 0.55: the paper turns the colour of lamplight. Ink stays ink. |
| Where | The engraver's mark rect for event k (`book.json` `marks`), one per event, spanning both staves for a two-hand event. |
| Drawn by | The page shader: `_Mark` (rect in page mm), `_MarkSlice`, `_MarkStrength`. Zero texture updates, zero extra draws. |
| Countdown | On the first note at 50 %, rising to 100 % as the first block lands. |
| Advancing | On `ITutorial.CurrentEvent` change: glides to the next column in 0.12 s (ease-out). Across a line or page: fades out 0.1 s and in 0.12 s at the new place. |
| Waiting | A slow breath of ±12 % at 0.5 Hz. |
| Mistakes | Nothing on the page. |
| Done / stopped | Fades out over 1.5 s. |
| First-run tour | The book shows Ode to Joy and the marker runs over bars 5–8 (`bookSong` + `eventOffset`). Sunny says nothing about it. |
| Not now (P2) | A faint graphite tick above each note already played. |

The same tint at strength 0.35 marks the contents row under a hovering fingertip (`_Hover`), so the tint always means
"this one".

---------------------------------------------------------------------------------------------------------------------------

## 5. Page turns

### 5.1 When
| Event | Turn |
|---|---|
| A piano lesson starts (`TutorialEvent{Countdown}`) and its song is not open | Adjacent spread: one leaf, 0.52 s. Farther: a riffle (section 5.3), inside the 3 s countdown. |
| `CurrentEvent`'s mark is on a page that is not open (a song longer than a spread, or the player turned away) | Toward it. T = clamp(time to the next due − 0.1 s, 0.35, 0.52). No lesson today needs this mid-song. |
| Tap the right page's bottom outer corner | Forward one spread, 0.62 s, corner-led. |
| Tap the left page's bottom outer corner | Back one spread. |
| Tap the ribbon's tail | Riffle back to the contents. |
| Tap a contents title | `ITutorial.Start(id)`; the start rule above turns to the song. |
| Lesson stops or ends | The book stays where it is (play it again freely). |
| App start | The persisted spread (`musicroom.sheet.spread`), else the contents. |

Turning past the last page or before the contents does nothing (the corner lifts and settles). Manual turns are allowed
during a lesson; the next hit brings the lesson's page back.

### 5.2 Touch
- **Who pokes:** each tracked hand's index tip (`HandState.Pointers[0]`), never the mouse hand, never a stick.
- **Where:** the tip in page coordinates (x along the arc from the spine, v up the page, n = height above the curled
  surface). Corner zones: the bottom 45 × 45 mm at the outer corner of each page, extended 25 mm beyond the page's
  outer and bottom edges (the corner can be caught from just outside). Contents rows: their rects. Ribbon: a 30 mm
  capsule around its last 30 mm.
- **Firing:** the menu's poke rule (spec 08 1.4): armed above n = 3 mm, fires on reaching n ≤ 0 within the zone, never
  from behind (n > −60 mm), one fire per entry, 400 ms debounce. A sideways flick also fires a corner: within 15 mm of
  the surface, ≥ 40 mm toward the spine at ≥ 0.25 m/s.
- **Hover:** a tip within 60 mm in front of a corner lifts that corner up to 8 mm (the page bends toward the finger, an
  affordance, not a glow). Over a contents row: the warm hover tint.
- **Never:** while `IArrangeMode.Arranging`, while `IGrabService.HeldRig` is the piano, while `IMixedReality.IsPlacing`,
  while the piano is not at the seat (`Rig.InScene` false), or with the menu's grab zone containing the tip.
- **Not mixed up with playing:** the corners are at back 0.23–0.25 m (the piano's play zone ends at 0.21) and 5–25 cm
  above the key tops. The Sheet claims the tip for gestures (`IPointerClaimant`, section 12) inside a 30 mm layer in
  front of the pages, so a reach for a corner never starts the layout swipe or a contactless instrument.
- **Desktop:** click colliders on `Layers.PlaySurface` over the two corners, the rows and the ribbon; a click is a tap.
  No new keys.
- **P2:** pinch the corner and drag: the leaf follows the hand, a release past halfway (or a flick) completes the turn.

**Reach.** The bottom outer corners are 0.72 m (fitted) to 0.81 m (home) from the right shoulder; the ribbon tail
about 0.70–0.78 m (it is central and low, so it needs no sideways reach). That is a lean, like a real piano's music rest, and beyond the 2 ft guideline. Nothing needs it:
lessons turn their own pages and the palm menu starts every lesson. Device item S3 decides whether the corner zones need
more margin.

### 5.3 The leaf animation
- The **paper mesh** holds the open spread (two pages) and one turning leaf; while nothing turns the leaf's triangles
  are collapsed (no extra draw). Per page 33 columns (denser near the spine: `x_i = W (i/32)^1.6`) × 9 rows; the leaf
  has a front and a back layer (0.1 mm apart).
- **Angle** of the leaf column at arc length s: `φ(s, t) = θ(τ) − 0.8 sin θ(τ) (s/W)^1.5`, `θ = π τ²(3 − 2τ)`; the free
  edge trails. Corner-led turns (a tapped bottom corner) delay the top: `τ = clamp((t − 0.10 (1 − v/H)) / T, 0, 1)`;
  automatic turns use no delay.
- **Position:** spine + `d(s) cos φ X + (d(s) sin φ + lift(d(s)) |cos φ|) N`, with `d(s)` the flat reach of arc
  length s on the static curl. At θ = 0 it is the right page, at θ = π the left page, upright at π/2.
- **Textures:** the leaf's front shows the old right page, its back the new left page; the right page under it shows
  the new right page from the start; the old left page stays until the leaf lands.
- **Shadow:** the page shader darkens the page the leaf is over by `0.25 sin θ` near the spine, falling off over
  40 mm (uniform `_Leaf`).
- **Vertex upload:** only while turning: about 600 vertices per frame, one `SetVertexBufferData`, 0 B.

### 5.4 Riffle
Up to three leaves, 0.34 s each, 0.085 s apart; the middle leaf shows the destination's left page (nobody reads a page
that flies past). The destination spread is swapped in under the last leaf. The riffle starts at once and hides the
raster time of the destination pages (≤ 0.1 s, section 7.4).

### 5.5 The sound (`page-sound.mjs`)
Only the engine's **one-shot pool** (burst.md 5.4): 4 presets × 4 noise lanes (`NZ4 → BQ4 → EXP4 → mix`), started
sample-accurately. No samples.

| Preset | Lanes (type, Hz, Q, τ ms, mix) | Length | Role |
|---|---|---|---|
| `lift` | bp 2600 0.9 18 .55; bp 5200 1.2 11 .35; lp 650 0.7 28 .30; hp 9000 0.7 5 .12 | 0.10 s | the corner leaves the page |
| `air` | bp 650 0.6 150 .50; bp 1500 0.7 110 .40; bp 3300 0.8 70 .20; lp 260 0.7 200 .18 | 0.55 s | the sweep (three swells) |
| `crinkle` | bp 4300 2.4 4 .60; bp 2300 2.0 6 .40; bp 7600 2.0 3 .25; hp 1200 0.7 2 .15 | 0.03 s | tiny paper ticks while it bends |
| `land` | lp 380 0.8 32 .75; bp 1100 1.0 18 .35; bp 2800 1.2 9 .18; hp 6000 0.7 4 .08 | 0.14 s | the soft landing |

`Turn(T, level, seed)` (Mulberry32): `lift` 0.55 at 0; 3–6 `crinkle`s (0.18–0.43) in [0.015, 0.4 T]; `air` 0.32 / 0.55 /
0.45 (± 15 %) at 0.10 T / 0.24 T / 0.40 T; `land` 0.6 (± 10 %) at T. `Riffle(n)`: leaves at 0.34 s, 0.085 s apart, each
5 % quieter. Unit gains measure −26.9 LUFS; the demo is normalised to −18 LUFS and reaches −1.10 dBTP. In the room it
plays on `PianoMid` (0.2 m in front of the book) at a starting gain of **0.25**, to be set by ear about 20 dB under a
mezzo-forte C4 (device item S4). It plays with "Classic sound" on too (one-shots belong to the graph, not to a unit).

Critic lessons applied: no lane above Q 2.4 and no τ over 200 ms (no metallic ring or hanging hiss), the low lanes are
lowpassed and quiet (no thump), no reverb of its own (the room adds it).

---------------------------------------------------------------------------------------------------------------------------

## 6. Free play, other instruments, other modes

### 6.1 Free play
The book opens at the contents. Tap a title: that lesson starts and the book riffles to it. When the lesson ends the
book stays on that song. The book never follows free playing (P3 idea: a "play along" mode that moves the tint with
matching notes).

### 6.2 Drum lessons: no notation
The kit sits to the side, the book is on the piano; a percussion staff (new clef, x noteheads, a line per drum) is one
more thing to learn for a 6–18-beat loop that the discs already teach. During a drum or harp lesson the book keeps its
page and shows no tint.

### 6.3 A music stand for other instruments
| Instrument | Notation | Stand | Why |
|---|---|---|---|
| Marimba | Treble + letters (`LessonSpec.Notation = Treble`) | **P1**, if the marimba ships lessons | Its bars are a keyboard layout; the same engraving and letters apply; the melodies are the piano's. |
| Harp | Treble + letters (C and F could print in the string colours) | **P2** | Harp lessons reuse the same tunes, but the player watches the strings; low value for the cost. |
| Handpan | None | No | No staff tradition (numbered tone fields); a tone-field map would be a different drawing (P3). |
| Hand drums | None | No | Percussion: zones, not pitches. |
| Theremin | None (`PitchLine` reserved) | No | A pitch-line display would be a new design. |
| Singing bowls | None | No | Three bowls; nothing to read. |

The stand is a slim black desk stand from the Sheet module, placed at the instrument's `InstrumentDescriptor.SheetAnchor`
when it is at the seat and a lesson of its own is running. It shows one page (no book, no turns: the song fits), merged
into the same two draws (its frame in the case mesh, its page in the paper mesh, a fifth texture slice, +1.07 MB).

### 6.4 Arrange mode, grabs, layout
- The book is parented under the music-rest pose, so it moves, turns, lifts and glides with the piano.
- In arrange mode: shown, no pokes, no hover, no tint (arrange mode stops lessons anyway).
- Not a grab handle of its own; the rest beside it is (a pinch at the book grabs the piano after the usual dwell, as
  today with the rest).
- Displayed piano (platform: not at the seat): the book is hidden.

### 6.5 Mixed reality
- On a real table (stand hidden, `SetOnSurface(true)`) the rest stays and so does the book.
- MR lighting (Trilight + the white main light) lights the paper through the same shader; the reading lamp still works.
- No shadow casting (MR casters are instrument bodies only). While placing: hidden with the piano, no pokes.
- Draws in MR: the same 2 (MR ≤ 80).

---------------------------------------------------------------------------------------------------------------------------

## 7. Unity implementation (`Assets/MusicRoom/Sheet/`, asmdef `MusicRoom.Sheet`)

### 7.1 Module and files
`SheetModule` (Order 45; refs Core, Hands, Audio, Unity.Burst, Unity.Collections, Unity.Mathematics). Registers
`ISheetBook` (read-backs: `Spread`, `Turning`, `MarkRect`, `MarkPage`, `Ready`), `IPointerClaimant`,
`IMaterialTemplateProvider` (`Sheet/Paper`, `Sheet/Case`), `ITextureBakeProvider` (`Sheet/paper_detail` 256² RGBA,
`Sheet/case_palette` 32 × 8: cloth, page edges, ribbon, brass, shade, with a metallic/smoothness map).

| File | Follows | Role |
|---|---|---|
| `Engrave/SheetScore.cs` | `lib/score.mjs` | spelling, positions, bars, values, rests, accidentals, beams, stems |
| `Engrave/SheetEngraver.cs` | `lib/engrave.mjs` | spacing, line breaks, systems, letters, title block |
| `Engrave/SheetBookLayout.cs` | `lib/book.mjs` | pagination, fillers, contents rows, page draw lists, mark rects |
| `Engrave/SheetGlyphs.cs`, `SheetStroke.cs` | `lib/glyphs.mjs`, `lib/stroke.mjs` | the glyph contours (built once into a static table at boot) |
| `Engrave/SheetFont.cs` | `lib/text.mjs` | reads `sheet-font.bytes`; layout runs, kerning, synthetic bold (outline offset) |
| `Raster/SheetRasterJob.cs` | `lib/raster.mjs` | `[BurstCompile(FloatMode = Default)] IJob`: draw list → R8 page + mip chain (box filter in linear) |
| `Raster/SheetPages.cs` | – | the `Texture2DArray` (1024 × 781, 4 slices, R8, linear, mips, aniso 2, mip bias −0.4), slice reuse, job scheduling |
| `View/BookMesh.cs` | `lib/geometry.mjs` | case mesh (boards, page block, ribbon, lamp), paper mesh (2 pages + leaf) |
| `View/LeafTurn.cs` | – | turn and riffle timing, leaf vertices, `_Leaf` uniform |
| `View/SheetMarker.cs` | – | follows `ITutorial`, sets `_Mark`, `_Hover` |
| `Input/SheetPokes.cs`, `SheetDesktop.cs` | – | corners, rows, ribbon, flick, hover, desktop colliders |
| `Shaders/SheetPaper.shader` | – | `MusicRoom/SheetPaper`: URP forward, SRP-Batcher CBUFFER, stereo instancing macros, Texture2DArray by uv.z, paper, ink, gutter, lamp, marker, hover, leaf shadow |
| `Resources/sheet-book.json`, `sheet-font.bytes`, `LiberationSans - OFL.txt` | lab | data |
| `Resources/GuideLines/Sheet.json`, `VoiceManifests/Sheet.json`, `Voice/tip_book*.wav` | platform 8.2 | Sunny lines |

### 7.2 Flow
1. **Boot (`Init`, on a worker):** load `sheet-book.json` and the font, build glyph contours, engrave the whole book's
   layout (scores, line breaks, pagination, mark rects; Node: 77 ms for all 15 songs). Keep layouts, not draw lists.
2. **First posed frame with the piano at the seat:** build the meshes under `IPianoApi.MusicRestPose`; raster the open
   spread's two pages (two jobs in parallel), upload, show. Until then the book shows blank paper (never pink).
3. **A lesson starts:** find its spread; build that spread's draw lists (managed, about 5 ms) on a worker; schedule two
   raster jobs into free slices; start the riffle at once; swap the slices in when `JobHandle.IsCompleted`.
4. **Per frame (Decor phase, after Grab):** pokes and hover (index tips only), the marker from `ITutorial.State` /
   `CurrentEvent`, the leaf animation. Uploads in `EndFrame` only when something changed. **0 B per frame.**

### 7.3 Draw calls, triangles, memory
| | Value |
|---|---|
| Draws | **2**: case (URP Lit template `Sheet/Case`: boards, page block, ribbon, lamp, P1 stand frame) + paper (`MusicRoom/SheetPaper`: both pages and the leaf). Idle and turning alike. |
| Triangles | about 3.5 k (case 1.5 k, pages 1 k, leaf 1 k) |
| Page textures | `Texture2DArray` 1024 × 781 × 4 slices, R8, mips: **4.27 MB** (2.2 texels per display pixel at the home seat, 1.7 leaning). Fallback if memory is tight: 832 × 634 (2.8 MB, softer when leaning). |
| Baked textures | `Sheet/paper_detail` 256² ASTC (about 0.09 MB), `Sheet/case_palette` (tiny) |
| CPU scratch | per worker: coverage row buffers, edges (≤ 30 k vertices a page), crossings: about 0.5 MB, allocated once |
| Font table | 22 KB |

### 7.4 Timing (Node on the Mac, single thread; C# expected similar or faster with Burst; device item S5)
- Raster at 3.2 texels/mm: 28 ms mean, 51 ms worst page (the densest pages have ≤ 30 k contour vertices, ≤ 529 groups).
- Two pages run in parallel on two workers: the riffle (≥ 0.34 s) hides it.
- Upload: about 1 MB per page with mips, one `SetPixelData` per mip and an `Apply(false)`.

### 7.5 Why a CPU raster and not TextMesh Pro
ARCHITECTURE 14 says "Labels and text are TextMeshPro, never a generated texture". The book's text is print on paper,
so it belongs in the page texture. Rendering it with TMP would need a hidden camera, a GPU readback or a full RGBA
render texture per page, and a GPU for every test (the piano's label atlas has exactly these limits). The CPU path uses
the same font's outlines, runs in a Burst job off the main thread, works in batchmode `-nographics`, and matches the
Node vectors pixel for pixel within tolerance. Requested amendment in section 12.

---------------------------------------------------------------------------------------------------------------------------

## 8. Sunny

Each line is ≤ 12 words, rendered with the existing eSpeak "Annie" pipeline (sunny-tour 5.4), 22.05 kHz mono, files in
the Sheet module (platform 8.2). Tips follow sunny-tour 3.1 (quiet moment, classes, Tips list).

| Tip id | Title (Tips list) | Trigger and conditions | Line | Demo @ anchor | Success | Class |
|---|---|---|---|---|---|---|
| `tip_book` | Your songbook | After the tour; piano at the seat; the open book in view (gaze ≤ 20°) for 1.5 s; no lesson running | "This is your songbook. The letters match the keys." | Point @ the first letter on the open page | none (information) | P3 |
| `tip_book_marker` | The warm mark | First piano lesson countdown with the book in view (not in the tour) | "The warm mark in the book shows your next note." | none | none | P2 (a lesson tip) |
| `tip_book_contents` | Start from the book | `tip_book` heard; the contents open and in view 2 s; no lesson ever started from the book | "Tap a song's name in the book to start it." | Poke @ contents row 1 | a lesson started by a title tap | P3 |
| `tip_book_turn` | Book pages | First lesson `Done` with the book in view; no manual leaf turn yet | "Tap a page corner to turn the page." | Poke @ the right page's bottom corner | a manual leaf turn | P3 |
| `tip_book_ribbon` | Back to the list | First time the open spread is 2 or more spreads past the contents after a manual turn | "Tap the ribbon to go back to the song list." | Poke @ the ribbon tail | a ribbon tap | P3 |

- Titles avoid "Turn the page", which the Tips list already uses for the instrument swipe (`tip_swipe`).
- Signals: `InstrumentSignalEvent { Kind = Piano, Signal = "sheet.turn" | "sheet.title" | "sheet.ribbon" }` (platform
  8.1), published by the Sheet module.
- `FeatureCatalog`: `sheet.book`, `sheet.marker`, `sheet.contents`, `sheet.turn`, `sheet.ribbon` (covered by the tips
  above); `sheet.autoturn` and `sheet.lamp` Passive ("the book turns itself to the lesson; nothing to learn", "a lamp").
- Anchors (`IGuideAnchors`): `sheet.corner.right`, `sheet.row.1`, `sheet.ribbon`, `sheet.letter.first`.
- The first-run script is unchanged: the book quietly shows the song with the tint; no line.

---------------------------------------------------------------------------------------------------------------------------

## 9. Tests

### 9.1 Lab (`node tests.mjs`; 1637 checks pass; `--write` refreshes `out/test-vectors.json`)
| Id | Checks |
|---|---|
| T1 | every piano lesson and both two-staff pieces have notation data; every bar adds up (pickups and completing last bars included); every lesson event appears exactly once as an onset |
| T2 | note-to-staff position for every pitch used (22 pitch/clef pairs, C2–G5), against an independent derivation; ledger counts |
| T3 | pixel placement: each pitch rendered alone at 12 px per staff space; the head's ink centroid within 0.06 S of its line or space; ledger lines counted by scanning a column beside the head |
| T4 | on every page of the book: every notehead inked where the page map says (524 heads); hollow heads hollow; ink in the letter row of every marked event; marks inside the page |
| T5 | Für Elise accidentals: sharps at 0.5, 1.5, 9, 12.5, 13.5 and naturals at 3, 15 (beats) |
| T6 | two-page songs start on a left page; one mark per event; 15 contents rows, separate, each ≥ 20 × 200 mm |
| T7 | Ode to Joy layout vectors (x, y of all 30 notes, stretch, system heights); x order with ≥ 0.8 S between notes |
| T8 | glyph raster vectors (both clefs, noteheads, sharp, natural, quarter rest, flag, labels "C4", "D#", "G") as R8 base64 |
| T9 | readability: staff space ≥ 6.5 px and letters ≥ 7.5 px from the home seat, staff space ≥ 7 px fitted, at 20 px/deg |
| DET | every page's raster at 4.2 px/mm hashes identical to the stored vectors (determinism) |

### 9.2 Unity EditMode (`Tests/EditMode/Sheet`, vectors in `Reference~/`)
- `SheetDataTests`: every piano lesson in `ILessonLibrary` has a `sheet-book.json` entry; bars add up (T1); the
  first-run mapping lands on the Ode's events 15–29.
- `SheetScoreTests`: T2 positions and ledgers for every pitch; T5 accidentals; ties across bars (Silent Night's last C4).
- `SheetLayoutTests`: T7 Ode vectors to ±0.01 mm; every song's bars per line and pages equal `book.songs`; contents rows.
- `SheetRasterTests`: T8 glyph rasters, per pixel |Δ| ≤ 3/255 on ≥ 99.5 % of pixels and ink sums within 0.5 % (Burst
  Default float mode; bit-exact is not required); T3 placement with the C# raster; T4 on three pages (4, 12, 26); page
  ink sums within 0.2 % of the vectors.
- `SheetFontTests`: the table loads; every character of every title, credit and contents string is present; the OFL
  text ships beside it.
- `PaperSoundTests` (Audio, `OfflineEngine`): `Turn(0.62, 1, seed 1)` at sample 14400 matches `page-sound-vectors.json`
  (first 32 samples to 1e-5 with Burst, exact without; RMS per 50 ms within 0.5 dB); no lane Q > 2.4, no τ > 0.2 s.
- `SheetAllocTests`: the per-frame tick (pokes, hover, marker, leaf) allocates 0 B after warm-up.

### 9.3 Unity PlayMode (`Tests/PlayMode/Sheet`, `TestBoot` with Lessons, Piano, Sheet; manual frames)
- `SheetFollowTests`: start `piano-ode` from page 2: the book reaches page 4 before the countdown ends; each scripted key
  press of the right note moves `MarkRect` to the next event's mark; a wrong note does not move it; `Done` fades it out.
- `SheetPokeTests`: a scripted index tip pressing the right page's corner turns forward exactly once (no repeat while
  held); from behind never; the left corner turns back; the ribbon riffles to the contents; a title starts its lesson;
  nothing fires while arranging, while the piano is held, while MR is placing, or with the mouse hand.
- `SheetDesktopTests`: a click on a corner collider turns; a click on a row starts the lesson.
- `SheetTurnTests`: a turn takes 0.62 s (manual) / 0.52 s (auto); `PaperTurn` is called once per leaf with the right
  `when`; the leaf mesh rests collapsed afterwards.
- `SheetShotsTests` [Graphics]: the book draws 2 draws idle and 2 while turning; the seat view's page centre is cream
  and lit (linear luminance ≥ 0.40 under the night room); the marked note's paper is warmer (R − B larger) than its
  neighbour's; PNGs to `Builds/Shots/sheet/` (seat, close-up, mid-turn, contents, MR on a table).
- Sunny coverage (`SunnyCoverageTests`, `GestureCatalogTests`): the five tips and the new feature ids are covered.

---------------------------------------------------------------------------------------------------------------------------

## 10. Device checklist (append to ARCHITECTURE 19 after the platform's items)
- **S1** Readability: two people read the letters and name three notes' lines/spaces on Ode to Joy and Für Elise from
  the fitted seat without leaning. If not: S 5.3 mm and letters 6.3 mm with 3 bars a line (the lab re-engraves in 10 s).
- **S2** The paper is bright enough at night (reading lamp on) and not glowing; MR passthrough lighting looks like paper.
- **S3** Corner taps: 10 taps per corner, both hands, fitted seat: ≥ 9 hits, no false fires while playing the top
  octave or reaching across the keys; the ribbon tail is reachable without standing.
- **S4** Page-sound level by ear against a mezzo-forte C4 (start: gain 0.25 on PianoMid).
- **S5** Raster time per page on the Quest worker (log `[Sheet] raster page N: x ms`); the riffle hides it.
- **S6** Texture shimmer and text crispness with foveation 0.5/1.0 and mip bias −0.4 (try −0.2 / −0.6).
- **S7** The falling blocks in front of the book: the pale block passing over the page does not hide the tinted note.

---------------------------------------------------------------------------------------------------------------------------

## 11. Plan (about 3 days for one agent in `u-sheet`)
1. **Day 1:** implement score, engraver, book layout, glyphs, font, raster job; EditMode vector tests green.
2. **Day 2:** meshes, `SheetPaper` shader, texture array, follow-the-lesson, start-of-lesson turns and riffle, pokes,
   ribbon, contents; PlayMode tests.
3. **Day 3:** leaf animation polish, paper sound presets (Audio request), Sunny tips and lines, Graphics shots, device
   build and the S checklist.
P1 after the deadline-critical work: corner hover lift, the marimba stand. P2: pinch-drag turns, pencil ticks, the
harp stand.

---------------------------------------------------------------------------------------------------------------------------

## 12. Contract requests (beyond platform 13.5)

| # | Owner | Request | Default |
|---|---|---|---|
| 1 | Core / Piano | `IPianoApi.MusicRestPose` (platform phase 0): **piano-local** pose of the rest's front face, bottom centre: position `(0, BodyTop + 0.001, Name + 0.087 − 0.0025)` = (0, 0.039, 0.2375); rotation with local up = (0, cos 0.3, sin 0.3) and local forward = away from the player; plus `MusicRestLedge = 0.0073` (ledge top along up) | `null`: no book |
| 2 | Core / Lessons | `ITutorial.CurrentEvent` (platform phase 0) | −1 |
| 3 | Core | `IPointerClaimant` from any module (the Sheet's 30 mm layer in front of the pages), checked after Sunny | none |
| 4 | Audio | `IAudioEngine.PaperTurn(SourceId source, float seconds, float level, uint seed, double when)` and `PaperRiffle(...)`, default no-ops (like the looper's `Click`); implemented in `Audio/Sound/Paper/PaperSound.cs` with the four `OneShotPreset`s of section 5.5 | no-op |
| 5 | ARCHITECTURE 14 | "Labels and text are TextMeshPro, never a generated texture; **printed pages (the music book) rasterise the TMP font's own outlines into the page texture.**" | – |
| 6 | Guide | the five tip rows of section 8 (data), the anchors, the feature ids | – |
| 7 | Core / Lessons (future) | optional `Lesson.meter`, `unit`, `pickup`, `credit`; `LessonTarget.hand`, `len`; the tutorial's block height uses a target's own `len` | absent |
| 8 | Settings | `musicroom.sheet.spread` (int, the open left page) | 2 |

**ARCHITECTURE 21 entry (draft):**
> **2026-11-xx, sheet music (12-new-instruments/sheet.md).** `MusicRoom.Sheet` (Order 45): an open music book on the
> piano's rest, engraved procedurally from the lessons (no music font), letters under the notes, a warm marker on the
> current note, automatic and tapped page turns with a one-shot paper sound, a contents spread that starts lessons.
> CPU raster (Burst) into a 4-slice R8 `Texture2DArray` (4.27 MB); 2 draws; 0 B per frame. Requests 1–8 applied.
> Section 14 amended for printed pages. Device items S1–S7.

---------------------------------------------------------------------------------------------------------------------------

## 13. Risks and UNVERIFIED
1. **Legibility on the real lens** (S1). The simulation is the ideal-filter case. The fallback sizes are ready.
2. **Reach** (S3). The corners and ribbon are beyond 2 ft; the design never depends on them.
3. **Raster time on the Quest** (S5). Expected well under the riffle; if not, raster the next likely spread ahead
   (the following song) during idle frames.
4. **The text rule amendment** (request 5). If refused, the fallback is TMP into a render texture per page on
   `Layers.Hidden` (like the piano's label atlas): GPU-only tests, an RGBA RT per page (+12 MB), the glyph work
   unchanged.
5. **Composer credits** name real (long-dead) composers, as every music book does. If the rules' "identifiable
   people" is read strictly, set `credit` to "Traditional" or drop the line (one data change).
6. **Two-hand lessons do not exist yet.** The grand staff is proven on two pieces; the tutorial needs request 7 before
   any two-hand lesson ships.
7. **The page-turn sound was designed, measured and rendered, not yet judged by ear** (S4).
