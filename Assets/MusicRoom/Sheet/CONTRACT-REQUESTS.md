# Sheet music: contract requests (phase B, base `instruments-base`)

Spec: `Docs/12-new-instruments/sheet.md`; hooks: BUILD-GUIDE section 12. Format: BUILD-GUIDE section 10.

**No shared source file needs to change.** Every hook the book uses is pre-declared on instruments-base:
`IPianoApi.MusicRestPose / MusicRestLocal / MusicRestLedge`, `ITutorial.CurrentEvent`, `IPointerClaimant`, `IGuideAnchors`,
`IGuideTipSource`, `InstrumentSignalEvent`, `IAudioEngine.AddOneShotPreset / PlayOneShot`, `Lesson.meter / unit / pickup /
credit`, `LessonTarget.hand / len`. The test asmdefs, `Editor/MusicRoom.Editor.asmdef` and `Core/link.xml` already list
`MusicRoom.Sheet`, and `IntegrationShotsTests` already counts the sheet module among its sixteen, so the phase-A edits
(both test asmdefs) and the phase-A module-count change are no longer needed.

Files outside the owned folders that the pipeline itself rewrites in this clone (generated, not hand-edited; the
integrator's own `render-setup -> bake -> scene` produces the same):
- `Assets/MusicRoom/Generated/Resources/Generated/Sheet/paper_detail.png` (+ `.meta`): `Tools/unity.sh bake`, from the
  book's `ITextureBakeProvider` (`Sheet/paper_detail`, 256 x 256 R8, linear, mips, repeat).
- `Sheet/Resources/MaterialTemplates/Sheet/{Paper,Case}.mat` are inside the owned folder (`render-setup`).

## R1 optional ARCHITECTURE.md 14: printed pages rasterise the font's own outlines
- **File:** `Docs/ARCHITECTURE.md` section 14 (documentation only).
- **Why:** sheet.md 7.5. The book's text is print on paper and belongs in the page texture. TextMesh Pro would need a hidden
  camera and an RGBA render texture per page (+12 MB), a GPU in every test, and could not match the lab's pages.
- **Change:** "Labels and text are TextMeshPro, never a generated texture; **printed pages (the music book) rasterise the
  TMP font's own outlines (LiberationSans, generated from the TTF: `Sheet/Resources/sheet-font.bytes`) into the page
  texture.**"
- **Test:** `SheetFontTests.TableRegeneratedFromTheProjectTtfIsIdentical` (byte for byte from the TTF), `SheetRasterTests`
  (44 of 44 pages SHA-1 equal to the lab).
- **Fallback until applied:** none needed (documentation).

## Notes for the integrator
- **Voice bank:** 8 lines (`Sheet/Resources/Voice/tip_book*.wav`, `VoiceManifests/Sheet.json`, `GuideLines/Sheet.json`),
  merged by GuideVoice / GuideLines; the platform's `SunnyCoverageTests` cover the book's 8 tips and 7 features through
  `SheetTipSource`.
- **Settings keys:** `musicroom.sheet.spread`, `.turned`, `.titleStarted`, `.ribbon`.
- **Room dependency:** the `Sheet/Case` template uses the room's `MusicRoom/RoomLit` shader (URP Lit without a ShaderGUI,
  so `_METALLICSPECGLOSSMAP` stays): keep that shader name.
- **Known flaky test (not the book):** `AudioAllocTests.HarpMidiOfAndRigHandlesAllocateNothing` failed 1 in 4 runs in the
  phase-A clone with the sheet module switched off.

## Deliberate differences from the spec (sheet.md)
- **Font table format 2 (doubles)**, so the C# pages are bit-identical with the lab's; 199 glyphs, 104 kerning pairs, 117 KB.
- **The reading lamp's colour** #FFE6C8 (not #FFD9A8, which rendered the cream paper orange); the page centre measures
  0.44 linear luminance in the night room (target 0.40-0.55).
- **Corner-led turns** delay the top rows (the bottom corner leads), as the spec's text says.
- **The rest's pose** is the platform's measured `PianoMusicRest` (0, 0.03974, 0.23761), ledge 6.55 mm (the spec's 7.3 mm
  assumed the ledge met the face at the board's back); the pages' bottom edge is 9.55 mm up the rest.
- **Desktop wordings:** the three tips that name a control have a second TipSpec with "Click" for "Tap"
  (`tip_book_*_desk`); the headset ones are `VrOnly`. All eight are `TipTrigger.Manual` (the book decides the moment).
- **Not built:** the corner hover lift (P1), pinch-drag turns and pencil ticks (P2), the marimba / harp stands
  (`LessonSpec.Notation`, `InstrumentDescriptor.SheetAnchor`).
