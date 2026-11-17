# MusicRoom.Menu (stage 3, implemented)

The palm menu (spec 08 section 1, `Tests/EditMode/Menu/08-ux-visuals.json` `panel` and `summonRing`,
ARCHITECTURE.md sections 9 and 15.6). Hold an open palm toward your face for half a second and a curved panel opens
in front of that hand; poke rows with the index fingertip; the same gesture puts it away. Desktop: M toggles it,
click a row, drag a long list.

Built on the review-round-1 foundation (ARCHITECTURE.md section 21): `Rig.Handles` filled once, `App.IsPresenting`
gates, the one desktop click model on `Layers.PlaySurface`, `Pointer.FromMouse` / `HandState.IsMouse`, the menu always
present, `TestBoot` for every PlayMode boot.

## The many-instrument platform (phase 0, 2026-11-01; 12-new-instruments/platform.md 5)

The menu names no instrument any more (only `Table` / `Menu`): every name, place, lesson id, portrait and option comes
from `App.Registry` (`MenuController.Registry`).

| What | Details |
|---|---|
| **Instruments screen** (5.1) | Header `‹ Instruments` with the note "With you: Piano, Drums, Harp" (`ILayoutService.MySet` in planner order) or "Handpan alone" (`AloneName`); for 2 s instead: "Harp went back to the corner" (`MenuText.WentBack`: Name + " went back " + the place read sensibly: "to the front wall", "by the lamp", "to the sill"), "One stays with you", "No room: take one out first" (header note shrinks to fit, never cut). Two rows of four tiles at y 128-338 / 358-568, x 32 + i x 244, 226 x 210 px, in carousel order; unloaded instruments are not drawn and the grid closes up. A tile is a printed card (the panel's cream ink as paper, its dark face colour as ink): the portrait (a photograph on a warm grey-beige studio sweep, `MenuStyle.PhotoPaper`, inset 7 px) or the descriptor's line drawing on the same backdrop, a hairline, the name at 34 px, the status at 22 px: "With you" (`PlaceOf` Seat, bold, and an amber frame round the card), the descriptor's `Stow.Where` on display or gliding, "Put away" when `Hidden` or in mixed reality; My set's members carry a printed tick box (cream square, dark rule and tick) top right; an at-seat tile whose instrument has `Options` carries a small dark "Options" chip top left. Bottom row y 590-700: "My set \| One alone" (one segmented control, 456 px, the chosen half in the amber "on" style), "Move" as a button to the right edge (and "Real hands" beside it in mixed reality, both 228 x 110) with its state under the label. Footer as before (unlocked / One alone / My set: "Poke one to bring it, or a ticked one to send it back") |
| **Pokes** | One alone: the tile of the instrument played does nothing (opens its options page when it has options); any other: `Bring(kind, Menu)`. My set: a member: `PutAway(kind)` (the last member stays: "One stays with you", no call); a displayed tile: `PreviewJoin` Add -> `Bring`; Replace -> `Bring`, and the note names the leaver; Alone / None -> "No room: take one out first", `IAudioEngine.Click(false)` (the Ui source's soft click, never a buzz), no `Bring`. "My set" / "One alone": `Set(mode, Facing ?? Current)` (nothing when that mode is already on). Chips come before tiles in the row list, so a poke on a chip is the chip's |
| **Events** | Every press: `MenuRowEvent { RowId, Screen, Confirming }` ("inst.<id>", "layout.all" / "layout.single", "lock", "classic", "options.<id>", "option.<id>.<Key>", and the sunny-tour ids of every other row; declared in `MenuRow.cs` until Core has it, CONTRACT-REQUESTS.md 7). Opening the Instruments screen (a screen change while shown, or opening the panel on it): `InstrumentSignalEvent { Kind = Table, Signal = "menu.instruments" }`, plus "menu.instruments.set" in My set with 2+ members (Sunny's tip_bring_grid / tip_put_away). Redraws on `LayoutChangedEvent` (incl. `SetVersion`) and `InstrumentMoveEvent`, and a per-frame poll of every instrument's `PlaceOf` while the grid is shown (two bits each, allocation-free); one relayout per user action |
| **Lessons screen** (5.3) | The same grid for the loaded instruments with at least one lesson, status "N lessons"; a tile opens its course list (unchanged). A course starts with `Tutorial.Start(id)` alone (the tutorial brings its instrument). The summary prints `ITutorial.CountNoun` ("16 strokes  ·  0 mistakes  ·  9.0 s"; the noun is in `MenuText.Summary`'s cache key) |
| **Options page** | A sub-page of `MenuScreen.Instruments` (`MenuController.OnOptions`, `OptionsFor`; no new `MenuScreen`, no MenuEvent): header `‹ <Name>`, note "Options", one row per `InstrumentDescriptor.Options` entry (label, its small print, the choice in amber at the right; up to 5 rows, h 100 gap 12 from y 130), footer "Poke a row to change it". A poke steps to the next choice, stores it as AppSettings int "musicroom.<id>.<Key>" (default `Default`), publishes `InstrumentOptionEvent { Kind, Key, Value }` and says the option's `Tip` once (`IGuide.Tip`, unless `TipSeen`). Back returns to the grid. Reached by poking the tile of the instrument played alone, or the "Options" chip of an at-seat tile |
| **Portraits** (5.2) | `Core/Render/PortraitStudio` (one 1024 x 512 ARGB32 sRGB atlas, 8 cells of 256 x 256, the portrait in each cell's middle 256 x 170 band at the tile's aspect): made once Layout's first arrangement is done; one portrait per frame while the start panel waits, else from the first time a tile grid is shown; the grid repaints once when nothing is left to render. A disabled camera (stereoTargetEye None, culling mask layer 30), the rig's renderers on layer 30 for one `RenderPipeline.SubmitRenderRequest(StandardRequest)` and straight back, a warm key (#fff1dc, 1.1, upper left front, layer 30 only, no shadows) as `RenderSettings.sun` for the request only, the room's ambient; 2x supersampled and box-filtered, then `Graphics.CopyTexture` into the cell. View direction and Fov from the descriptor's `Portrait.From -> At`; the aim and distance fitted to the rendered parts (every renderer's local-bounds corners) so each instrument is centred and spans 80 % of the frame along its limiting axis (`PortraitStudio.FitToBounds`). The tiles blit the band through `IMenuCanvas.Image` (MenuShape primitive 5, rounded, linear -> sRGB). Fallback (headless, `-nographics`, no CopyTexture, `PortraitStudio.UseRenderRequests = false` or the setting `musicroom.menu.portraits` = 0, a request that throws): the descriptor's `Portrait.Fallback` strokes with the icons' stroke rules (`MenuIcons.DrawStrokes`). Editor cost: 0.25-0.8 ms CPU per portrait (2.5-2.7 ms for the first of a boot), 8 frames |
| **Anchors** | An `IGuideAnchors` of Kind `Table` (`MenuModule.Anchors`): "menu.tile.<id>" = the tile's portrait centre on the face while the Instruments grid is shown, "menu.option.<id>.<Key>" = the row's centre while that options page is shown; up = the face normal toward the player |
| **Panel home** | When the instrument the player faces (`ILayoutService.Facing`; One alone: `Current`; while a lesson runs, the lesson's instrument first) has `SeatSpec.PanelHome`, the palm menu opens there (seat frame, upright, facing the head) instead of at the palm, a lesson screen shown while the panel is open moves there, and the menu rig's `HomeAll` / `HomeSingle` (Layout's arrangement) follow it; otherwise `MenuConstants.HomeSeat` and the palm placement as before (no instrument has a panel home today) |

Tests: EditMode `MenuInstrumentGridTests` (19: tiles, rects, close-up, statuses, ticks, notes, both modes' pokes, the
knock, the replace note, the last member, the mode switch, row ids, signals, the "went back" phrases, the options page,
anchors, the lessons grid, the count noun, fallback icons without a GPU, the printed cards, one relayout per action);
PlayMode `MenuInstrumentGridTests` (10, with `GridLayoutProbe`, a stand-in `ILayoutService` at Order 5 that forwards the
classic members to the real Layout and simulates My set / Bring / PutAway / PlaceOf / Facing: fingertip pokes, the knock
on the Ui source through `AudioTestTap`, events on the bus, anchors, options, the panel home, the grid's and the options page's frames allocating nothing, the headless fallback);
`PortraitTests` (Graphics, 2: every cell Ready, not blank, fill 40-85 % along the limiting axis, centred, mean luminance
0.25-0.75, layers / sun / flag restored, the grid shots; one portrait per frame while the start panel waits). Shots
(`$MUSICROOM_SHOTS`, default `Builds/Shots/platform/menu/`): `portrait-atlas`, `portrait-<id>`,
`canvas-instruments-grid`, `seated-instruments-grid`, `canvas-instruments-replaced`, `-noroom`, `-alone`, `-options`,
`seated-instruments-options`, and `MenuScreenshotTests`' screens (now written there too).

UNVERIFIED on the device: portraits through `SubmitRenderRequest` on Quest (Vulkan, single-pass; device item 49: upright,
crisp, lit, the next head-camera frame unaffected; else switch `musicroom.menu.portraits` to 0); the 22 px status line
and 34 px names read at the panel's distance; the "Options" chip (33 x 12 mm) is easy to poke. In this phase-0 copy the
real Layout still hides the instruments outside My set (no display yet), so in the app only their vector icons show
until Layout's display lands (the GPU test shows every rig for its portrait).

## Since the first headset test (2026-11-01, u-arrange)

The same canvas, painter, rows and poke state machine now also make:

| What | Details |
|---|---|
| **Start panel** | At launch (`App.StartPanel`, from `BootOptions.StartPanel`: on in the app, off in `TestBoot` unless asked), once the room is ready (rig up, headset on, `ILayoutService.FirstArrangeDone`, head posed, MR not placing) the panel opens in front of the eyes along the look direction (not at a palm): `MenuMath.PlaceFor` at 0.5 m (its key-front rule over the piano), else 0.55 m, 6 cm below the eyes, then `ClearPanel`. Screen `MenuScreen.Start`: the title "VR Music Room", "Where would you like to play?", two big rows (h 176) **Play here** (green) and **Play in my room** (cyan; only while `IMixedReality.Available` and `Status != NotAvailable`, `MenuController.RoomAvailable`), footer "Later: show your palm to open the menu". `IMenuPanel.StartPending` is true from boot until an answer: Sunny's first-launch tour waits for it. Play here closes the panel; Play in my room calls `IMixedReality.Enter()` (the existing entry) and closes it. The palm gesture and M do nothing while it is up; whatever hides it unanswered (headset off, session change) only hides it: it comes back. The next palm menu opens on Home (`MenuController.ResetScreen`, silent) |
| **Move instruments** | Home row 3 (only with an `IArrangeMode`, from Layout): "Move instruments" · "Place them"; while on, "Done moving" · "Moving now" (on, amber). Poke: `IArrangeMode.BeginArrange()` and the menu closes; poked again ("Done moving"): `EndArrange()`, the menu stays open. `ArrangeModeEvent{Active}` also closes the menu |
| **Done / Reset buttons** | A second `MenuView` (`ArrangeButtons` root, `MenuStyle.ArrangeScale` 0.5: 0.16 x 0.12 m, no summon ring) with its own `MenuController` on `MenuScreen.Arrange` and a host that publishes nothing. Shown while `IArrangeMode.Arranging` (event plus a per-frame check), placed when shown: 35 cm ahead of the eyes along the look direction, 15 cm below them, 10 cm to the left, turned to face the eyes (`ArrangeConstants.Buttons*`). Header "Move instruments", "Pinch one, or point at it and pinch", "Move your hand, turn your wrist", two big buttons **Done** (green check) and **Reset** (undo arrow), each 452 x 394 px (7 x 6 cm). Poked with either index finger exactly like menu rows: the tip in the buttons' frame goes through `MenuMath.ScaledToPanel`, which keeps ARM (3 mm), HOVER (10 mm) and BEHIND (-6 cm) in real metres. Same 150 ms open / close fade, hover highlight uniforms and hover glow; desktop clicks too. Done: `EndArrange()`; Reset: `ResetArrangement()` |
| **Exit** | Home's last row, "Exit" · "Close the app". First poke: "Tap again to exit" (red, no note) for 3 s (`MenuStyle.ExitConfirmMs`); the 250 ms row debounce means a bounce never counts as the second poke; closing the menu or 3 s without a second poke forgets it. Second poke: "Closing…", arrange mode is ended (its placement saved), `AudioListener.volume` fades to 0 over 150 ms (`QuitFadeSeconds`, in the EndFrame phase), the settings are saved and `App.Quit()` runs: `BootOptions.Quit` when set (TestBoot always sets a counter, so a test never quits the runner), else `Application.Quit` on the device and leaving Play mode in the editor. The volume is restored in `Shutdown` |
| **Home geometry** | 4 + Move + Room + Setup + Exit rows share y 132..~684: 5 rows h 100 gap 12, 6 h 84 gap 10, 7 h 72 gap 8, 8 h 62 gap 8 (`MenuController.HomeGeometry`). On the 7 / 8 row pages the label starts at 2 x floor(0.78 h / 2) px (56 / 48) and the note at min(42, 0.6 h) px. Instruments footer (unlocked): "To move them: Move instruments, on the first page" |
| **Hover uniforms** | `MenuView.SetHover` now compares exactly (`Vector4.Equals`): Unity's `==` treats values within 1e-5 as equal, which could leave a fade resting at k = 0.999996 (a timing-dependent failure of `AHoveringTipLightsTheRowAndAddsOneGlowOnTheFace`) |

New icons (`MenuIcons`): Move (four arrows), Exit (door and arrow), House, Check, Undo. New row kinds: Arrange, Exit,
PlayHere, PlayRoom, Done, Reset. `IMenuHost` gains `RequestQuit()` and `StartChosen(bool)` with empty default bodies.

**Merged with the 10-upgrade (2026-11-01).** The panel's `IContactSurface` (the finger stops on the face) sits beside
these rows: the enums keep the upgrade's members first and append these (no value is persisted). (The upgrade's
"Classic sound" row, `RowKind.Classic`, was removed 2026-11-10: the classic sound is the only sound.) The Instruments
screen shows the five rows and this footer ("To move them: Move instruments, on the first page"). The Done / Reset buttons are not an
`IContactSurface` yet (a finger is not stopped on their face; poking works as before).

## Public contract

| What | Details |
|---|---|
| `MenuModule` (Order 60) | registers `IMenuPanel`, `IInstrument` (Kind Menu, TickOrder 50) and `Rig` (RigId.Lessons, Priority 10, home `MenuConstants.HomeSeat` (0, 1.03, 0.47) yaw 0) |
| `IMenuPanel` | `Shown`, `Screen`, `Show(hand)`, `Hide()`, `Toggle(hand)`, `Go(screen)`, `Redraw()`, `GrabZone` (world AABB of face + back plate at scale 1, plus 0.12 m; null while hidden), `Rig`, `Root`, `StartPending` (the start panel is not answered yet) |
| Extras (tests, tools) | `Controller`, `View`, `ArrangeButtons` / `ArrangeView` / `ArrangeRoot` / `ArrangeButtonsShown` / `ArrangePaints` (the Done / Reset panel), `StartShowing`, `Quitting`, `QuitCalled` |
| `Rig` | `Group` = `Root` (always scale 1; the open/close animation scales a child), `Zone` = the grab zone, `Handles` = the list `{ Back, Face }` filled once in Init (the meshes Layout's `ClearPanel` boxes; both are inactive while the panel is hidden, so Layout, which measures only shown renderers, sees no footprint then), `Instruments` = the menu |
| `IInstrument` | `IsPresent` is always true (the summon must run while hidden); `PlaySurfaceAt` is always `None`: a pinch on the panel grabs the panel (the grab checks the menu zone first), so Layout should use `GrabZone` / `Rig.Zone`; `PlayedParts` empty |
| `MenuEvent` | `Open` / `Close` / `Screen` on every open, close and screen change (also the lesson -> courses fallback) |
| Also publishes | `MenuRowEvent` (every press), `InstrumentSignalEvent { Kind = Table }` ("menu.instruments", "menu.instruments.set"), `InstrumentOptionEvent`; registers an `IGuideAnchors` (Kind Table); reads `App.Registry`, `AppSettings` (options), `ILayoutService.MySet / SetVersion / PlaceOf / PreviewJoin / Bring / PutAway / Facing / Seat`; listens to `InstrumentMoveEvent` |
| Reads | `ITutorial`, `ILessonLibrary`, `IRecorder`, `ITakeLibrary`, `IGuide`, `IAudioEngine` (`MenuController.Sound`: the scroll ticks' `Click`) (in Init), `ILayoutService`, `IMixedReality`, `IPlacementService`, `IPianoApi` (lazily) |
| Listens | `TutorialEvent`, `RecorderStateEvent`, `GuidePresenceEvent`, `LayoutChangedEvent`, `LibraryChangedEvent`, `MixedRealityEvent` (relayout), `HmdMountEvent{Mounted=false}` (hide); every frame it also polls those services' state, so nothing is missed if a publisher is silent |
| Calls | `IPlacementService.ClearPanel(rig)` once per open, before `MenuEvent{Open}` (not when a shown panel is shown again); `ILayoutService.Set / Locked`; `ITutorial.Start / Stop`; `IRecorder.PlayEvents / Stop`; `ITakeLibrary.List / Get / Remove`; `IGuide.Start / Dismiss`; `IMixedReality.Enter / Exit / RunSpaceSetup` |
| Material templates | `MenuMaterials` (`IMaterialTemplateProvider`): `Menu/Face` (MusicRoom/MenuFace, queue 2999), `Menu/Back` (same shader, 2998), `Menu/Shape` (MusicRoom/MenuShape). Written by `Tools/unity.sh render-setup` into `Menu/Resources/MaterialTemplates/Menu/` |

## Contact surface (10-upgrade/experience.md E1 / E3, visual only)

`MenuModule` implements `IContactSurface` (registered in `Init`): while the panel is shown and not held, a point inside
the 0.32 x 0.24 m face on the R = 0.8 m cylinder (`MenuMath.ToSurface`) hits the face with the normal toward the player
(`MenuMath.SurfaceNormal`), `Use` Clamp: Core's visual clamp stops the drawn poking finger on the face (poke limiting);
the pokes keep reading the raw tip, so nothing about a press changes. No contact shadow (review 2026-11-01): the face
(queue 2999, depth-writing) draws over the shadows (queue 2991), so it was never seen, yet it halved the hover glow under
the fingertip (Core dims a glow a blob covers). A straight poke is no longer bent either (Core's clamp fades out within
15-30 degrees of the normal). Shot: `Builds/Shots/exp-hands/finger-on-menu.png`
(vs `finger-in-menu-raw.png`).

## Files

| File | Role |
|---|---|
| `MenuModule.cs` | the module: services, events, frame phases, Show / Hide / pose, pokes from the index tips, desktop mouse, hover glow, animation, repaint |
| `MenuController.cs` | pure logic (no Unity objects): screens, `LayoutRows` (the rows of every screen, with the home rows), poke state machine, scroll, hover fade, every row action, take loading |
| `SummonGesture.cs` | the summon gesture (pure; `SummonSample.From` reads a hand) |
| `MenuMath.cs` | curved plane meshes, `ToSurface` / `SurfacePoint` / canvas mapping, `PlaceFor`, `GrabZone`, ring mesh |
| `MenuPainter.cs`, `MenuIcons.cs` | the panel's redraw / draw* / icon functions onto `IMenuCanvas` |
| `GpuMenuCanvas.cs` | `IMenuCanvas` on the GPU: one CommandBuffer into the render texture, SDF shapes, TextMeshPro meshes, `FitText` |
| `MenuView.SetHover` | the row highlights as face-material uniforms (`_HoverRect0/1`, `_HoverParam0/1`, `_HoverClip`), set only when they change |
| `MenuView.cs` | scene objects: Root / Visual / Face (+ MeshCollider) / Back, the summon ring, render textures, materials |
| `MenuStyle.cs`, `MenuText.cs`, `MenuRow.cs` | every constant, colour and string; cached formatted strings; the 24 row slots |
| `Shaders/MenuShape.shader` | 2D painter (rounded box fill / stroke / gradient, segment, ellipse arc, triangle, box shadow) |
| `Shaders/MenuFace.shader` | the face and back plate in the room (URP, single-pass instanced); draws the two hover highlights (hovered row fading in, previous row fading out) over the canvas |

## Behaviour (spec 08 sections 1.1-1.7)

- **Summon** (IInstrument.Tick, before the pokes): the first hand (left, then right) that is tracked, not pinching,
  holding nothing, not doing the system gesture and has not played for 400 ms, with its palm centre no lower than
  0.45 m under the eyes, grasp >= 0.15, 0.08-0.8 m from the head and its palm normal within 40 degrees (cos .7660) of
  the head, charges; another hand (or none) resets. The desktop mouse hand (`HandState.IsMouse`) never charges. The
  ring (annulus .028-.036 m, #5fd8ff additive, both faces) sits 3 cm out of the palm facing the head, scale .5+.5k,
  opacity .15+.65k, k = T/0.5. At 0.5 s it toggles once. While
  the panel is held or the piano is being placed in MR (Core skips the menu's Tick) the ring hides, the charge is kept
  and the open / close fade holds where it is.
- **Open** (`placeFor`): along the palm's flat direction at clamp(|v| + .35, .30, .55) m (|v| <= .05: the look
  direction at .5 m), 0.12 m below the eyes, facing the player; inside the piano's box (|x| < .8, -.2 < y < .75,
  -.01 < back < .9) it moves to 1 cm in front of the key fronts. Then `ClearPanel`, then `MenuEvent{Open}`.
- **Hide**: at once (zone, collider, hover, every poke state, `MenuEvent{Close}`), then a 150 ms fade. Also hidden on
  `HmdMountEvent{Mounted=false}` and whenever `App.IsPresenting` changes (XR session start / end).
  Animation: animK toward 0/1 by dt/0.15, k = 1-(1-animK)^2, scale .9+.1k (on the Visual child), opacity k.
- **Pokes**: each hand's `Pointers[0]` (index tip) in panel-local metres -> `ToSurface` (u along the arc, y, z out of
  the curved face). Inside the face + 1 cm margin: armed once in front past ARM (3 mm); fires on reaching z <= 0 (not
  from more than 6 cm behind, the pointer active last frame); must come back past ARM; a row slot ignores a second
  fire within 250 ms. On a scrolling list a touch fires on release unless it moved more than SLOP (8 mm); a drag sets
  scroll = scroll0 + dy/0.24 x 768. Hover: -5..10 mm lights the row (120 ms fade) and adds one hover glow 7 mm out of
  the face, 0.045 x 0.045, strength 1 - z/0.01.
- **Screens**: home, lessons, courses, lesson (countdown / hint / done), instruments (with Lock instruments), recordings, take; texts, colours and rectangles as `08-ux-visuals.json` (the EditMode tests read that file). Home:
  Lessons, Instruments, Recordings, **Play in my room** · Passthrough / **Music room** · Virtual room, (in MR with
  `Status == NoRooms`) **Set up my room** · Space Setup, Show me around · Sunny / Hide Sunny · Stop the tour; five
  rows h 100 gap 12, six rows h 84 gap 10, from y 132 (no MR service at all: four rows, h 128).
- **Canvas**: 1024 x 768 render texture painted only when something it shows changed, at most once per frame, inside
  `Profiler.BeginSample("[Menu] redraw")`; the back plate (512 x 384) once. Hover never repaints (ARCHITECTURE.md 15.6):
  the highlight (rgba(95,216,255,.22k) fill, 4 px stroke at alpha k inset 2 px, radius min(28, h/2); the back
  row of a sub-screen: fill only, radius CORNER-6; list rows clipped to 130..750) is drawn by MusicRoom/MenuFace from
  uniforms `MenuPainter.HoverShape` computes, k = min(1, (now - hoverT)/120 ms) every frame. Desktop: a ray on the
  face's MeshCollider gives canvas px from its uv.
- **Desktop** (ARCHITECTURE.md section 0, while `!App.IsPresenting`): M toggles the panel along the look direction; a
  left press whose nearest `Layers.PlaySurface` hit (`Physics.Raycast(..., Layers.PlaySurfaceMask)`) is the face fires
  the row under it (desktopDown); with Shift / Alt held the press is Layout's rig drag, never a click.
  The face is on `Layers.PlaySurface`, which AppRoot puts in `DesktopRigFallback.dragBlockMask`, so a press on it never
  turns the desktop camera. The mouse hand's pointers (`Pointer.FromMouse`) never poke and never glow.

## Deliberate changes (beyond ARCHITECTURE.md)

- **No sound switch** (2026-11-10): the upgrade's "Classic sound" row is gone (the piano, drums, harp and room always
  play the classic sound). The Instruments screen's bottom row is My set | One alone, then Move to the right edge
  (511-990); in mixed reality Move and Real hands share it as two 228 px buttons (`MenuStyle.BarX0`, `ButtonWAlone`).
  `MenuController.Sound` is still read for the scroll ticks' click.

1. **Rendering**: ARCHITECTURE.md 15.6 asks for a disabled camera on the PanelUI layer (31) through
   `SubmitRenderRequest`. The menu uses a cheaper path that meets the 15.6 rules: one CommandBuffer
   (`Graphics.ExecuteCommandBuffer`) draws signed-distance shapes and TextMeshPro meshes straight into the render
   texture (no camera, no culling, no pipeline setup, no layer, no uGUI). The rules are asserted on the GPU: redraw only
   on content change (never idle, never for hover, exactly one per user action), at most once per frame inside
   `[Menu] redraw`, hover as face-material uniforms, 2 draws in the room for the open panel (face, back plate), 3 with
   the summon ring (budget 4). The command buffer's `MaterialPropertyBlock` feeds `CommandBuffer.DrawMesh` on the
   menu's own shaders, never a renderer (the SRP Batcher rule of section 14 is about renderers). Architecture sign-off
   and a device check are requested (CONTRACT-REQUESTS.md 5). The canvas keeps sRGB numbers and blends them in gamma
   space; MusicRoom/MenuFace converts the premultiplied value to linear, so translucent texels (the back plate's glow)
   look as designed instead of twice as bright under linear blending. Text colour comes through `_FaceColor` (TMP would gamma-convert vertex
   colours in a Linear project).
2. **Text fitting**: `FitText` (drop characters until "text…" fits) instead of TMP's ellipsis; bold text is
   tightened by -4% em (TMP's synthetic bold adds 7% em per glyph); the header note may use the width a short title
   leaves (at least 400 px), so "Courses  ·  drag to scroll" is no longer cut; the long body lines of the
   recordings screen (wider than the canvas) shrink to fit.
3. **Release-fire guard**: a touch fires on release only on the screen it began on. Without it a press held past the
   250 ms debounce on "Piano" (lessons screen) would start the first course on release, because the scrolling course
   row took the same slot.
4. **Hover**: drawn by the face shader on top of the finished canvas (15.6), so it lies over the row's text and its
   amber "on" stroke instead of under them: hovered text takes a 22% cyan tint. The
   fade is smooth to k = 1 (a repaint-driven fade could rest at ~85%).
5. **Desktop**: mouse-down on a row fires it (desktopDown); on a scrolling list the mouse acts like a touch
   instead (drag scrolls, a still click fires on release), so long lists are reachable without a headset, and the row
   under the mouse lights.
6. **Room row** without passthrough (desktop, failed init) reads "Not available" and does nothing.
7. The lesson -> courses fallback raises `MenuEvent{Screen}` like every screen change.
8. The panel is put away whenever `App.IsPresenting` changes (XR session start /
   end).

## Constants

ARM .003, HOVER .010, SLOP .008, BEHIND -.06, edge margin .01 m, debounce 250 ms (`MenuConstants`); summon .5 s,
grasp .15, .08-.8 m, cos 40 degrees, play guard 400 ms; face .32 x .24 m on a .8 m cylinder, 32 segments; back plate
.3483 x .2688 m, 4 mm behind; canvas 1024 x 768 (3200 px/m, corner 64 px), back canvas 512 x 384; open .15 s;
hover fade 120 ms; glow 7 mm out, .045 m; list 130..750 px, pitch 130, rows 120, maxScroll = max(0, n x 130 - 630),
min 30 px visible, 24 row slots; palette INK #f3ead8, DIM 55%, CYAN #5fd8ff, GREEN #5dff8a, RED #ff5a5a,
AMBER #ffc46a, ROSE #ff8ec4 (`MenuStyle`).

## Tests

Added after the first headset test: EditMode `MenuArrangeExitStartTests` (10: the Move row in both layouts and with
MR / Setup, entering and leaving through it, Exit asks then quits, the 3 s timeout and closing forget the first poke, a
bounce inside the debounce never counts, the start panel's rows and its hidden Play in my room, Done / Reset, what the
painter draws, the half-size buttons' poke mapping); the home tests updated for Exit (and the 6 / 7 / 8 row geometry).
PlayMode `Tests/PlayMode/Integration/ArrangeIntegrationTests` (6, every real module, scripted index pokes: the row
enters arrange mode, the buttons' pose and pokes, Done / Reset, "Done moving"; Exit's question, timeout, 150 ms fade,
save and the quit hook; the start panel's pose, the tour waiting for it, Play in my room calling Enter, the hidden row,
an unanswered panel coming back) and the GPU `ArrangeShotsTests` (`Builds/Shots/arrange/`: start_panel, menu_home,
arrange_seat, arrange_buttons, arrange_overview, arrange_ray, arrange_held, arrange_piano_raised, exit_confirm).
`MenuScreenshotTests` counts eight home rows in MR with Setup.

EditMode (`Tests/EditMode/Menu`, 55): `MenuMathTests` (curved face on the cylinder, winding toward the player, back
plate, `ToSurface` incl. the curved-edge case, canvas mapping, `PlaceFor` incl. the key-front rule, grab zone,
ring), `MenuPokeTests` (spec 08 section 9 poke acceptance: fire once / hold, re-entry from +2 mm vs +4 mm after
250 ms, never from behind, inactive pointers, edge margin, the curved edge poke, a 9 mm drag scrolls 28.8 px without
firing, a still tap fires on release, 4 cm drag, scroll clamp, the long-press guard, hover fade timing with no repaint,
the hover highlight geometry (row / back row / list clip, following the scroll), the painter drawing no hover colour,
hide resets, desktop mouse), `MenuScreenTests` (home rows and notes incl. MR, Sunny, saving off; five / six / four row geometry;
Room and Setup rows; the guide row; back rows; navigation and events; lessons, courses (maxScroll 1060 / 540 / 280),
a course bringing its instrument in, the lesson screen through countdown / waiting / playing / done, stop and back;
instruments and the lock; recordings, take Play / Stop / Delete with confirm; painter texts and accents; label
shrink; no relayout when idle; the string cache), `MenuLayoutJsonTests` (rectangles, list geometry, palette, sizes
and ring against `08-ux-visuals.json`; templates and shaders), `SummonGestureTests` (spec 08 section 9 and
the tutorial reference checks: toggles once after .5 s, ring position / scale / opacity, re-arm after a break, pinch / play / stick /
fist / pointing / low / palm-down / system gesture never, distance and the 40 degree cone, left hand, pause/reset).

PlayMode (`Tests/PlayMode/Menu`, every boot through `TestBoot`; `Tools/unity.sh test-play`):
- `MenuPlayModeTests` (9; a placement probe at Order 5 counts `ClearPanel` and moves nothing, so the menu's own
  placement is checked whatever Layout's clear-out does): registration and the rig (`Handles` filled once, shown only
  with the panel); a FakeHandSource palm summons the panel in front of it with the ring, once; an index-tip poke
  navigates and the back row returns; the hover glow on the face and the highlight uniforms on the face material
  (fade in to 1, the left row fading out, then gone); hidden on unmount then faded, the face on `Layers.PlaySurface`
  inside `dragBlockMask`; the grab zone follows the panel; **opening clears the panel out exactly once, before
  `MenuEvent{Open}`, and showing a shown panel does not**; a lesson start shows the lesson screen; the per-frame path
  and the hidden summon allocate nothing.
- `MenuPresentingTests` (3; simulated headset, manual frames at 1/72 s): the summon fires after 0.5 s of frame time
  and the open animation runs on frame time; **the close fade runs in the menu's instrument tick and holds while MR
  places the piano** (Core ticks no instrument then); the panel is put away when `App.IsPresenting` changes, and the
  desktop mouse hand never charges the summon.
- `MenuStubTests` (1; the foundation's contract check, now the menu's): toggle events, grab zone, always present.
- `MenuBootGuardTests` (1).
- `MenuScreenshotTests` (`[Category("Graphics")]`, `Tools/unity.sh test-play --gpu`, 4):
  `CapturesEveryScreenAndChecksTheCanvas` (13 screens + the summon ring saved as `canvas-*.png` (the painted canvas,
  no hover) and `seated-*.png` (the desktop seated view, the shader's hover highlight on the Lock instruments row) to
  `Builds/Shots/menu/`, the menu reference checks' look section, repaints allocate nothing, every text slot
  scale-static); **`TheCanvasRepaintsOnlyWhenItsContentChanges`** (`Paints`: 20 idle frames 0, hover in and out 0, one
  Lock poke exactly 1); **`TheFaceShaderLightsTheHoveredRowInTheRenderedView`** (rendered pixels: the hovered Lock row
  +48 blue, the Harp row above unchanged; saves `seated-hover-probe.png`); **`TheOpenPanelStaysInsideTheMenuDrawBudget`**
  (profiler draw count: the open panel adds 2 draws, budget 4). These tests find the Move row by its id (and assert it
  ends the bottom row: no sound switch).

## Known gaps

- Since the first headset test: the start panel, the Done / Reset buttons and Exit are verified headless only (scripted
  pokes, GPU screenshots). Their reach, size and text at 35 cm (the buttons) and 0.55 m (the start panel) need a
  headset check (Layout/ARRANGE-NOTES.md). The Exit fade drives `AudioListener.volume` from the frame loop (about 11
  steps at 72 Hz), not a sample-accurate ramp.

- `ITutorial` has no live mistake count; the lesson screen reads `Summary.Mistakes` (CONTRACT-REQUESTS.md 1).
- Clear-out is Layout's `IPlacementService.ClearPanel` (a no-op in the foundation stub); the menu calls it once per
  open (tested with a probe).
- No device run yet: the summon cone and distances, poke depths and the text size at 3200 px/m are the design values,
  unverified on a Quest 3S; the system-gesture exclusion relies on `HandFrame.systemGesture`; the command-buffer canvas
  on Vulkan / single-pass instanced is a device checklist item (CONTRACT-REQUESTS.md 5).
- The foundation's `SynthSource.Awake` race (CONTRACT-REQUESTS.md 3): fixed in the Audio module's clone; until that is
  merged, `MenuBootGuard` tolerates exactly that exception in the menu's boots. (This clone's Audio copy carries the
  same fix so the whole suite is green on every run; the merge does not take Audio from here.)
- The first editor run that generates TextMeshPro text (the GPU tests) re-serialises
  `Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset` in TMP's newer format (no glyph or atlas
  change). It is outside this folder; the clone keeps the foundation's copy (restored after each GPU run) and
  CONTRACT-REQUESTS.md 6 asks the foundation to accept the upgrade once.
- The mouse hand's "never pokes" rule is covered by code review and the summon / session test, not by a pointer test:
  `TestBoot` has no scripted mouse hand.

## Keep painting (2026-11-11, the playback show)

A recording's page (`MenuScreen.Take`) has a fourth row under Play / Stop / Delete: **Keep painting** (`RowKind.Paint`,
row id "paint", y 682-752, the spec's three rows unchanged), its state at the right ("On: until the next song", amber /
"Off: it fades away"). A poke switches `AppSettings.KeepPaintings` (`musicroom.keepPaintings`) and saves at once; the
room's wall paint (Room/Decor/WallPaint.cs) reads it when a painting would fade. Hidden while Delete asks (the footer
line takes its place). Feature `recordings.paint`, Sunny `tip_paint_keep` (asked by the room when a painting is about to
fade). Tests: `MenuScreenTests` (the row, its poke), `MenuLayoutJsonTests` (its rectangle in 08-ux-visuals.json).
