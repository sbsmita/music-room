# MusicRoom.Sheet: the music book on the piano's rest

An open, cloth-bound music book stands on the piano's music rest. It prints the piano lessons as real sheet music, with
the key letter under every note (the same letter as on the key: `C4`, `D`, `D#`, `C5`), a soft warm tint on the next
note while a lesson runs, pages that turn themselves to the lesson, and pages you turn by tapping a page's bottom corner.
With no lesson running it opens at its contents: tap a song's name and that lesson starts. The red ribbon's tail, lying
on the piano's top panel, takes you back to the contents. A small brass piano lamp lights the pages.

Every mark on the page is drawn by our code: staff, both clefs, noteheads, stems, flags, beams, dots, rests, ties,
sharps, flats, naturals, bar lines, the brace, time signatures. The words use the project's own LiberationSans outlines.
No music font, no images, no third-party assets.

Spec: `Docs/12-new-instruments/sheet.md`. Lab (Node, the reference): the sheet lab. Built in phase A
(in its own copy of the project), moved onto the platform in phase B (base `instruments-base`):
`PHASE-B-NOTES.md`. Requests: `CONTRACT-REQUESTS.md` (no shared source change needed).

## How it works

| Part | Files | What |
|---|---|---|
| Engraver | `Engrave/SheetScore.cs`, `SheetEngraver.cs`, `SheetBookLayout.cs`, `SheetGlyphs.cs`, `SheetStroke.cs`, `SheetFont.cs`, `SheetTrueType.cs`, `JsMath.cs`, `SheetBookData.cs` | The lab's `lib/*.mjs` in C#, written so the pages are **bit-identical** with the lab's: doubles where JavaScript uses numbers, floats where it uses Float32Array, V8's `Math.hypot`, `Math.round`, a stable sort, V8's `log2` for the spacing table. Never throws on unusual lesson data (overlaps, unspellable lengths: warnings). |
| Font | `Resources/sheet-font.bytes` (+ `LiberationSans - OFL.txt`) | LiberationSans outlines flattened at 0.0015 em, 199 glyphs, 104 kerning pairs, generated from `Assets/TextMesh Pro/Fonts/LiberationSans.ttf` by `SheetFontTable.Build` (doubles: format 2). |
| Book data | `Resources/sheet-book.json` | Meter, the note value of a beat, pickup, credit, staves, `bookSong` + `eventOffset` per lesson; the two-staff demo pieces. A lesson with no entry prints in 4/4 from beat 0. |
| Raster | `Raster/SheetRasterJob.cs`, `SheetRaster.cs`, `SheetPages.cs` | A Burst job (FloatMode.Strict, raw pointers) fills an R8 coverage page with its mip chain; a `Texture2DArray` of 4 slices, 1024 x 781, 3.2 texels/mm, 4.26 MB; the draw list is built on a worker; uploads through a staging texture and `CopyTexture` (GPU) or `SetPixelData` (batchmode). The next spread is drawn ahead while idle. |
| View | `View/BookGeometry.cs`, `BookMesh.cs`, `LeafTurn.cs`, `SheetMarker.cs`, `SheetMaterials.cs`, `Shaders/SheetPaper.shader` | Two meshes, two draws: the case (boards, page block, ribbon, lamp; `Sheet/Case` on the room's lit shader) and the paper (both pages with the gutter curl and three turning leaves; `MusicRoom/SheetPaper`: paper, ink, gutter shade, the lamp's analytic light, the marker and hover tints, the leaf shadow, URP lights, single-pass stereo). |
| Input | `Input/SheetPokes.cs` | Index tips (never the mouse hand): bottom outer corners, contents rows, the ribbon's tail; the menu's poke rule; a sideways flick turns too. Desktop: click colliders on `Layers.PlaySurface`. |
| Sunny | `Tips/SheetTips.cs`, `Tips/SheetTipSource.cs`, `Resources/{Voice,VoiceManifests,GuideLines}` | Five tips (eight TipSpecs and lines with the desktop "Click" wordings), each <= 12 words, rendered with the project's eSpeak pipeline (the lab's voice tool); an `IGuideTipSource`, all `TipTrigger.Manual` (the book decides the moment), learned by the book's `InstrumentSignalEvent`s. |
| Sound | `Sound/SheetPaperSound.cs` | The page turn's one-shot presets and schedule (the lab's `page-sound.mjs`, vectors-checked), played through the engine's one-shot pool on PianoMid. |
| Adapter | `Host/ISheetHost.cs`, `Host/PlatformHost.cs` | The platform hooks: `IPianoApi.MusicRestPose / MusicRestLocal / MusicRestLedge`, `ITutorial.CurrentEvent`, `IAudioEngine.AddOneShotPreset / PlayOneShot`, `IGuide.Tip`, `InstrumentSignalEvent`. |
| Module | `SheetModule.cs` | Order 45, no Rig, no IInstrument; registers `ISheetBook`, `IGuideAnchors` (`sheet.corner.right/left`, `sheet.row.1`, `sheet.ribbon`, `sheet.letter.first`) and `IPointerClaimant` (30 mm in front of the pages, and the ribbon's tail). Decor phase: visibility, follow the lesson, pokes / clicks, turns, marker, tips. EndFrame: rasters and leaf vertices. |

**Behaviour.** The book is a child of the piano's rig at the rest's pose: it moves, lifts and turns with the piano (arrange
mode, grabs, mixed reality on a real table, My set) and is hidden while the piano is not at the seat (put away, on display,
gliding: `IPianoApi.MusicRestPose` is null) or MR is placing it; it appears when a glide brings the piano back. It opens
at the remembered spread (`musicroom.sheet.spread`), else the contents. A piano lesson's start turns to its song (one leaf
0.52 s when adjacent, else a riffle of up to three leaves) within the countdown; a manual turn during a lesson is undone
at the next note. The marker: half strength in the countdown, full when playing, a slow breath while waiting, a 0.12 s
glide to the next column, a fade across lines and pages, a 1.5 s fade when the lesson ends; nothing on mistakes; nothing in
arrange mode. Tapping a page's bottom outer corner turns forward (right) or back (left), corner-led, 0.62 s; past the last
page or before the contents nothing happens. Drum and harp lessons leave the book as it is. Free play never moves it.

## Tests

- EditMode `Tests/EditMode/Sheet` (39): the lab's vectors (`Reference~/`): every page of BOTH lesson files
  (`lessons.json` and `lessons-proposed.json`) SHA-1 identical with the lab (19 + 25 pages), glyph rasters identical
  (20,308 of 20,308 pixels), staff positions and ledgers of every pitch used, head centroids, Für Elise's sharps and
  naturals, bars adding up, ties across bar lines, the Ode layout vectors, the page map (marks, noteheads, contents rows),
  two-staff pieces, Burst = managed = repeat (determinism), the mip chain, the font table regenerated from the TTF byte for
  byte, Sunny's lines and triggers, the paper sound against the lab's render vectors, the rest pose = PianoBody's rest.
- PlayMode `Tests/PlayMode/Sheet` (9 + 2 Graphics + 1 Performance): on the rest and moving with the piano, hidden while
  away, a lesson turning the book and the marker following right notes only, pokes (once, never from behind, back, the
  ribbon's riffle), a title starting its lesson, arrange mode stopping pokes and hover, turn durations and the paper
  sound per leaf, the leaves collapsed at rest, desktop clicks, 0 B per frame, the platform hooks (claimant, anchors, signals,
the one-shot pool), no book while the piano is away or gliding and the book back on its rest after the glide; the Graphics
tests write the pictures below (phase B's into `phase-b/`)
  and checks 2 draws idle and turning, the page lit, the marker warmer than its neighbour, the readability numbers.

Pictures: `Builds/Shots/sheet/` (`$MUSICROOM_SHOTS_ROOT/sheet` or `$MUSICROOM_SHOTS` override).

## Numbers (phase A, editor on the Mac)

| | |
|---|---|
| Readability at 20 px/deg, home seat (0.78-0.82 m) | staff space 6.9-7.2 px, letter caps 8.1-8.6 px |
| Fitted seat (0.69-0.74 m) | staff space 7.6-8.1 px, letter caps 9.0-9.5 px |
| Draws | 2 idle, 2 while a leaf turns |
| Textures | 4.26 MB (Texture2DArray 4 x 1024 x 781 R8 + mips); paper mottle 256 x 256 R8; palettes 32 x 8 |
| Meshes | paper 2,376 vertices / 4,096 triangles; case 1,039 / 675 |
| Raster | 15 ms mean, 28 ms worst per page (Burst, editor); draw list build 2 ms mean |
| Frame | 1.8 us, 0 B allocated |
| Boot | the whole book engraved on a worker in 12-100 ms |

## Known gaps

- Corner hover lift (P1), pinch-drag turns and pencil ticks (P2), and the marimba / harp stands are not built.
- Device items S1-S7 (readability on the lens, lamp level, corner reach, page-sound level, raster time on the Quest,
  shimmer, the falling blocks over the page) are for the headset.
