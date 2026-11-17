# MusicRoom.Room (stage 3, implemented)

The calm evening room (shell, furniture, decor, fire, snow, materials, procedural textures, ambience switches) per
spec 07 (`Docs/07-world-visuals.md`) and ARCHITECTURE.md sections 13 and 15.4. References: Core, Hands, Audio, URP Core/Universal Runtime, TextMeshPro.

Built and verified in the Room module's clone (`m-room`), rebased onto the revised foundation (ARCHITECTURE.md section 21): Core's
`ISnowPortals`, `Rig.Handles` as a list filled once, `Layers.PlaySurface`, `App.IsPresenting`, `Pointer.FromMouse`,
the shared `SharedTextures` (`Core/<name>`), `TestBoot` in every PlayMode test, no MaterialPropertyBlock, the
per-rig draw budgets. `CONTRACT-REQUESTS.md` lists what Room would like changed outside its folder (none blocks it).

## Public contract

| What | Where | Notes |
|---|---|---|
| `RoomModule` (Order 20) | `RoomModule.cs` | builds everything in `Init`, ticks in `Decor` and `EndFrame` |
| `IRoom` | `RoomModule` | `Root` (the "Room" object under `World`), `Visible`, `SetVisible` (MR hides the shell, decor and its lights; the table rig, the weather and the instruments' floor shadows stay, but for the piano's (`ShadowSpec.HiddenInMr`); `SetVisible(true)` restores the VR ambient and main light), `SetCandles` (wax, flames and both candle lights; the holders and wicks, merged into the brass and soot meshes, are swapped out with them), `RoomBox` = `GrabConstants.RoomBox` = `RoomFrame.WorldBox` (world: the room faces the fire) |
| `IAmbienceSwitches` | `RoomModule` | `Set(kind, on)`: lever + LED, `IAudioEngine.SetFire/SetWind/SetSnow`, `SnowWeather.Target` for SNOW, `AmbienceEvent`; no-op when unchanged. FIRE and WIND switch sounds only (the fire always burns) |
| `ISnowWeather` | `Decor/SnowWeather.cs` (registered instance) | spec 07 section 5.3 1:1 in double precision; `OnGust` is wired to `IAudioEngine.SnowGust` |
| `Rig` (RigId.Table) | `Table/SideTable.cs` | group "Table" under `World`, home (-0.39, 0, -0.21) yaw -90 (single mode: stays), priority 3, `Zone` = slab box + `ZonePad`, `Handles` = one `List<Renderer>` filled once in `Build` (exposed as `IReadOnlyList`): the merged slab / legs / plate mesh (levers, LEDs, labels and the contact shadow are not handles; the Lessons deck may add its body in place: `(rig.Handles as List<Renderer>).Add(body)`). The deck goes at table-local (0.07, 0.6, 0) |
| `IInstrument` switch panel | `Table/AmbiencePanel.cs` | Kind Table, TickOrder 40, `PlayedParts` = 3 levers, `PlaySurfaceAt` = Table within 3.5 cm of a lever, 0 < y < .1 (over the table controls). Desktop: `DesktopDown(ray)` (the nearest layer-29 hit must be one of its click cylinders) |
| `IMaterialTemplateProvider` | `Render/RoomTemplates.cs` | `Room/LitGlossMap` (wood + metallic-gloss map), `Room/LitNormal` (loveseat weave), `Room/LitEmissive` (lamp shade, sill mound), `Room/LitSway` / `Room/LitSwayLeaf` (plants), all on `MusicRoom/RoomLit`; `Room/LedUnlit` (`MusicRoom/VertexColorUnlit`); one per custom shader; `Room/LabelText` (TMP mobile SDF). Written by `Tools/unity.sh render-setup` into `Room/Resources/MaterialTemplates/Room/` |
| `ITextureBakeProvider` | `Textures/RoomTextures.cs` | `Room/wood_oak_gloss`, `Room/wood_walnut_gloss`, `Room/plaster`, `Room/rug`, `Room/leaf_atlas`, `Room/brick`, `Room/weave_normal`, `Room/scenery_atlas`, `Room/night_sky`, `Room/hill_snow`, baked to `Generated/Resources/Generated/Room/<name>.png`. Also binds Core's `Core/wood_oak`, `Core/wood_walnut`, `Core/soft_dot` (baked by `CoreTextureProvider`) |

Shaders (`Shaders/*.shader`, URP `UniversalForward`, single-pass instanced, float noise, `RoomNoise.hlsl`):
`MusicRoom/Flame`, `Coals`, `Snow` (queue 3002), `Frost` (3004), `NightSky`, `NightGround`, `FloorDecal` (2950,
Offset -1,-1), `Billboard`, `VertexColorUnlit` (the LEDs) and `RoomLit`: URP's own Lit forward pass (LitInput +
LitForwardPass, same UnityPerMaterial, SRP Batcher compatible) without a ShaderGUI, so template keywords survive the
editor (URP Lit's ValidateMaterial drops `_METALLICSPECGLOSSMAP` / `_NORMALMAP` / `_EMISSION` from a template without
textures or emissive GI flags); keyword `_SWAY` adds the plants' vertex sway, `_SWAY_TWO_SIDED` flips the normal on
back faces (two-sided lighting). The MR shadow catcher is MR's own shader. Globals pushed every Decor phase, also
while the room is hidden: `_FireTime` (also the sway clock), `_SnowTime`, `_SnowIntensity`, `_SnowWind`,
`_SnowWindVel`, `_SnowCover`, `_SnowPx`.

| `IRoomFurniture` (Core) | `World/Furniture.cs` (registered: `RoomModule.Furniture`) | the room's furniture as data for the display plan (platform.md 4.1), see "Furniture" below |

### Mixed reality
The snow portals Room drew behind real windows for MR (`ISnowPortals`, `Decor/SnowPortals.cs`) were removed 2026-11-13
with the snowy window (the player: "remove all window things"; backup music-room-vr-wip/unity-backups/pre-snow-paint2/
deleted-by-roomsnow/). The `_Snow*` globals keep running while the room is hidden. (`SnowFieldParams.Portal` /
`FitPortal` in `Decor/SnowField.cs` are no longer used.)

## Behaviour summary

- **Method.** Every layout is computed in the right-handed source frame (-z forward) with the spec's own numbers and
  the geometry generators in `Geometry/PrimitiveMeshes.cs` (Box, Plane, Cylinder, Torus, Circle, Sphere, RoundedBox, Tube with the
  centripetal Catmull-Rom curve, arc-length sampling and Frenet frames), then mirrored once into Unity (z, normal z,
  winding).
- **Draws (spec 07 section 11, ARCHITECTURE.md sections 13 / 14 / 17).** Static geometry is merged per material: 27
  renderers (27.8 k triangles) including one stem and one leaf renderer for all six plants, one canvas renderer for the
  three paintings (scenery atlas) and one loveseat renderer (`SofaPalette`); plus the fireplace (coals, glow, flames,
  smoke, embers) and the candles (wax, flames): **34 renderers, 37 while snow shows** (35 / 38 since the flowers' petals, 2026-11-11) (snow field, frost, sill
  mound); the instruments' contact shadows, the piano's included, are one draw of their own (`RigShadows`, below). Measured on the GPU from the seat with a 90 x 90
  degree view (`RoomDrawTests`): front 10, fireplace 30, shelf 17, window 20 draws (11 / 31 / 17 / 23 snowing). The
  side-table rig is 8 draws (wood, brass, contact shadow, 3 levers, one LED mesh, one label mesh) of the 15 it shares
  with the Lessons deck.
- **Shell.** Floor covers the full 6.4 x 5.2 m (not turned 90 degrees), planks along x (u = -z/2.1333,
  v = x/1.7333). Walls merged with world-scale plaster UVs (1.6 x 0.725 m tiles); right wall in four pieces around the
  1.8 x 1.7 m opening; bricks box-projected at 0.83 x 0.82 m. No pendant lamp.
- **Furniture.** Bench, bookshelf with 88 books (10-colour palette texture, HSL in linear sRGB), three gilt paintings
  (each canvas's UVs point at its slot of `Room/scenery_atlas`), floor lamp (open shade, two-sided geometry, emissive
  #ffd9a0 x .35), loveseat (rounded boxes; frame, cushions and pillow in one material: a 2 x 2 clamped palette whose
  quadrant each part's UVs are shifted into by whole weave tiles, so the weave normal map is unchanged), coffee table,
  rug, fireplace (bricks, soot firebox, stone hearth and mantel, three logs).
- **Plants.** Six plants, mulberry32 seeds 101..106, the spec's random-call order; pots static; stems and leaves of all
  six merged, each vertex carrying its plant's base (TEXCOORD2) and (phase, amplitude, cos / sin of the plant frame's
  yaw) (TEXCOORD3); `MusicRoom/RoomLit` with `_SWAY` turns them about the base in the plant's frame (rotZ =
  sin(.7t + phi) amp, rotX = sin(.5t + 1.3 phi) .7 amp, x angle mirrored); `PlantSway.Apply` is the CPU mirror the
  tests compare with a pivot transform. Leaves: alpha clip .5, both faces, back faces lit from behind.
- **Fire.** One 8-quad flame mesh (seed + intensity per vertex), coals quad, camera-facing cavity glow (opacity
  .45 + .15 sin(5t + 1.3) + .08 sin(13.1t) in the shader), fire light with the spec's flicker (frame-rate independent
  walk). Smoke (36) and embers (70) are ParticleSystems that die exactly at the wrap height (smoke .98, embers
  1.0, behind the lintel whose bottom is at .78): lifetime span / SpeedMin, a collision plane at the top facing down
  (lifetime loss 1), emission 1.5x the steady-state rate so the maxParticles cap keeps the column full. Candles: 4
  (coffee table + mantel).
- **Window and snow.** Frame, glass (Lit transparent .08), sill (top y .63) with the trailing ivy, outside ledge, night
  sky (40 x 11 quad, tint 1 - .12 level; the hill-snow overlay is baked and bound at boot, NightSky skips it while
  `_SnowCover` is 0), ground (colour lerp to #435070 by .92 cover^.8 in linear), the 6000-flake field (seed 7, flakes
  drawn only through the window), frost (cover), sill mound (scale.y = cover^.75). SnowWeather: level rate .8 / snap
  .003, cover +1/40 per s x level, -1/150 per s, gusts only at level >= .15 (first at 3 s), wind .32(1 + .3 sin .11t) +
  2.3 envelope.
- **Curtains** (10-upgrade room.md 4.3 / 12, `World/Curtains.cs`). The room's acoustic model counts on soft furnishings,
  so two gathered panels hang open beside the window: 0.40 m wide, 0.16 m outside the opening (clear of the deep sill),
  from y .02 to a brass rod at y 2.66 on two brackets with ball finials. The pleats are geometry: 5 sines per panel,
  half-depth .018 at the rod to .032 at the hem, both faces lit. The panels join the bench cushion's `Fabric` batch and
  the rod joins `Brass`, so the counts stay 27 static / 34 drawing (about +2.4 k triangles). Measured from the seat
  facing the window: 21 draws (24 snowing), up from 20 / 23 because the fabric batch now shows there; budget 34 / 37. The numbers are
  `MusicRoom.Audio.Burst.RoomMath.Curtain*`, the same ones the acoustic model uses for the right wall's two `curtain`
  regions, so the room you see is the room you hear.
- **Lights.** Moon = main light #a9bbff 0.1751, Euler (24.05, -68.40), no shadows; floor lamp #ffd6a0 1.0804 r 5;
  fire #ff8f3c 2.0747 r 6 (flicker .22); candles #ffc070 0.1293 r 1.8 (flicker .3); Trilight ambient #747578 /
  #69696a / #4c4c4b; one realtime reflection probe (128 px at (0, 1.2, -1.48)) rendered once the room is textured; box-projected
  over the room's box (6.4 x 2.9 x 5.2 m centred at (0, 1.45, -1.48), experience.md E11; QuestURP's box projection is
  switched on by `render-setup`), so the cymbals, the chrome and the black piano reflect the window and the fire in place.
- **Switch panel.** Plate on the side table at rig-local (-0.19, 0.6, -0.02) yaw -6.8755; toggles at x -.075 / 0 / .075;
  levers +-31.513 degrees; the three LEDs are one mesh (`LedStrip`, `MusicRoom/VertexColorUnlit`): off = the dim #331a0a
  body, on = #ff8a2a x 1.4, an LED's vertex colours rewritten only when its switch flips; the FIRE / WIND / SNOW labels
  (TMP bold #e8dcc4, text-up away from the player) are laid out by TextMeshPro once and merged into one mesh. Nothing
  on the table casts a shadow (MR casters are the instrument bodies only); wood and brass receive. Touch: a pointer
  active this frame and last frame entering (dx^2 + dz^2) < .018^2, .005 < y < .06 flips once (held per pointer until
  it leaves); mouse-hand pointers (`Pointer.FromMouse`) never poke. Hover glow over a lever (2.5 cm sideways, -2..+6 cm
  above its top). Desktop (ARCHITECTURE.md section 0): while `!App.IsPresenting`, a plain left
  press whose nearest `Layers.PlaySurface` hit is a click cylinder (r .016, h .05 at (x, .03, 0)) flips that toggle once.
- **Contact shadows.** 10 static blobs + 4 wall-base AO strips in one decal mesh; the table's blob is a child of its
  rig. The instruments' (`RigShadows`, `Table/SideTable.cs`; platform.md 11 "rig shadows 1 (was 3)") are ONE mesh and
  ONE draw: a quad per registry instrument with a `ShadowSpec` (rig-local centre, W x D, opacity in the vertex colour),
  under a root of its own (`World/RigShadows`), not under the rigs. A quad shows while its rig is at the seat
  (`Rig.InScene`, its group active, `ILayoutService.PlaceOf` neither Display nor Gliding); a `HiddenInMr` quad (the
  piano's, which belongs to the room) also needs the room shown, so mixed reality hides it as it hid the
  room's blob. EndFrame (after Layout's page-turn animation): the vertices are rewritten only when a rig moved, turned or
  scaled (held, gliding and page-turning rigs every frame), came or went, or the room was shown or hidden; a hidden quad
  collapses onto a shown one; 0 B per frame. The default set draws the three blobs of before exactly: the piano's
  (0, .2) 2 x 1 .55, the kit's (.1, .5) 1.9 x 1.6 .45, the harp's (-.2725, -.168) 1.1 x .7 .45. Nothing in the table
  assumes its home: it moves as a rig (its alternate pose, behind-left, is Layout's).
- **Textures.** `RoomTexGen` holds the room-only texture generators of spec 07 section 7 (per-pixel byte for byte with Core's
  `ProcTex.Hash / VNoise / Fbm / ClampByte`; canvas generators through `Canvas2D`), the leaf and scenery atlases and the
  woods' URP gloss maps (from Core's `ProcTex.WoodPixel` roughness bytes). Baked PNGs load at once; unbaked ones
  (Core's included) are generated on worker threads and uploaded one per frame (placeholder tints meanwhile), cached
  for later boots. Headless boots never generate.
- **Compositing.** The shader overlays are specified for a non-sRGB framebuffer; URP blends linear light. Flames, the
  cavity glow, the frost and the snow flakes write the colour that adds the specified sRGB value over black
  (`SRGBToLinear(rgb x a) / a`), within ~10 % of the reference over the night sky; decals use alpha 1 - (1 - a)^2.2;
  smoke / ember particles carry `RoomMaterials.OverlayColor(colour, alpha)`.

- **Listening back** (`Decor/ListenBackModel.cs`, `Decor/ListenBackPulse.cs`; `RoomModule.ListenBack`). While a take or
  a loop plays, the room reacts to the notes the recorder sends back (Core's `PlaybackNoteEvent`). Recording and plain live
  play leave the room calm (2026-11-11 headset test: "why its glowing when i started recording?"): the recorder still
  publishes `RecordedNoteEvent` for the notes it captures, but the room no longer listens to it, so an overdub over a
  playing loop pulses only the played-back sounds, each once. Sunny: `tip_listen_back` (`tip_record_glow` is no longer
  asked). Per instrument a level: an onset kicks it to its velocity (attack
  20 ms) and it falls back slowly (decay 0.55 s); a continuous lane (a rubbed bowl) holds it at the replayed
  level until its release; a note the looper sends ahead waits for its audio time; the recorder going idle lets every hold
  go. Drawn as (1) soft motes: one world-space ParticleSystem under `World/ListenBack` (96 at most, one draw, the
  `ParticlesUnlitAdditive` template with `Core/soft_dot`, so it is multiview safe), released by `Emit` above the played
  parts of the instrument that played, in the playback glow's warm orange (`ListenBackModel.Glow`, sRGB 1.0 / 0.5 / 0.1,
  the same for every instrument since 2026-11-11: "playback glow should be orange color"), more and brighter for louder
  notes, under a shared budget (36 a second); none where the instrument is hidden; (2) the room's own lights, no new
  light: floor lamp +45% and tinted up to 60% toward the orange glow, its shade's emission +100%, the fire and candles' flicker gain +25% (`FlickerLight.Gain`), all
  scaled by the loudest level, and written back exactly once at rest. In mixed reality (room hidden) only the motes show.
  Once a playback has sounded 1.5 s it asks Sunny for `tip_listen_back` (Guide's TourTips) until it was said.
  Allocation-free per frame (`ListenBackModelTests`, `ListenBackPlayTests`).

- **Flowers** (`World/FlowerBuilder.cs`, placed by `RoomBuilder.Flowers`). Four arrangements on the furniture: a cream
  ceramic vase of seven tulips (orange, red, yellow, peach) at the mantel's right end, a brass bud vase of four daisies at
  its left end, a terracotta pot of red geraniums on the window sill under the mullion, a stone jar of five pink cosmos on
  the bookshelf's top beside the fern. Vases join the Trim / Brass / Terracotta / Stone / Soil batches, stems and leaves
  the plants' Stem / Leaf batches, every petal ONE new static renderer (`RoomMat.Petal`, template `Room/LitSwayPetal`:
  RoomLit `_SWAY` + `_SWAY_TWO_SIDED` + `_EMISSION`, Cull Off; colours from an 8 x 2 palette made in code,
  `PetalPalette`, also the emission map). Everything above a vase mouth sways about it with the plants' sway; the pivot's
  w (`FlowerBuilder.Response` 1.6; the plants' is 0) weights RoomLit's music term, a faster second sway scaled by the
  `_MusicSway` global (`ListenBackPulse.FlowerSway` x the room level), and the petals' emission rises by
  `FlowerGlow` (0.55) x level: the flowers sway and glow with what is recorded or played back, exactly their own at rest.
  Decor footprints: "flowers on the mantel", "flowers on the sill", "flowers on the bookshelf". `RoomScene.FlowerPerches`:
  the top of every flower head (20). Hidden with the room in mixed reality. Draws: 28 static, 35 drawing (38 snowing).
- **Butterflies** (`Decor/ButterflyFlock.cs` the behaviour, pure; `Decor/Butterflies.cs` the drawing; `RoomModule.Butterflies`).
  While a recording plays back (a take or a loop: `ListenBackPulse.PlaybackModel`, the played-back notes only; never while
  recording, 2026-11-11: "butterfly should not appear while recording. it will appear while playing the recordings"; the
  lights, motes and flowers still follow a recording's live notes), 4 (soft) to 12 (loud) come: in VR they take off
  from the flowers, in mixed reality they fade in beyond the instruments. They flutter around the instruments that sound
  (targets above and beyond the played parts' box, never on the player's side of it), faster, brighter and with faster
  wings for louder notes; between phrases (1.2 s without a note) they land on free flower heads (VR) or drift (MR); 2.5 s
  after STOP (or 7 s of silence) they drift away and fade out over 2.2 s. Hard keep-out: the head's 0.35 m sphere plus a
  wing's reach, 0.12 m around every active fingertip and stick tip, the box over the played parts; softer repulsion further
  out; inside the room box in VR. Drawn as ONE dynamic mesh under `World/Butterflies` (12 x 2 wing quads, the CPU writes
  96 vertices a frame into one NativeArray; the renderer is off while none is out), shader `MusicRoom/Butterfly` (template
  `Room/Butterfly`: alpha-blended, both faces, per-vertex ambient + main + point lights as the harp's strings, a faint
  self light, the vertex colour's orange glow, single-pass instanced / multiview), wing atlas `Room/butterfly_wings`
  (`Textures/ButterflyArt.cs`, 512 x 256, eight designs). No light, no shadow. Allocation-free per frame.

- **Snow in the whole room** (2026-11-13; `Decor/RoomSnowModel.cs` the numbers, pure, with `FlakeAt` = the shader's maths
  on the CPU; `Decor/RoomSnow.cs` the driver; `Shaders/RoomSnow.shader`, template `Room/RoomSnow`; `RoomModule.RoomSnow`).
  Whenever SNOW is on (its `SnowWeather` level brings the flakes in one by one), `RoomSnowModel.Count` (3600) flakes fall all
  around the player: VR in the built room's box (`RoomFrame.LocalBox` under the Room root's pose, walls inset 0.1 m), mixed
  reality in a round snowfall 3.6 m about the player from the real floor (`IMixedReality.FloorHeight`, else the head less
  1.2 m) to 2.6 m above it. Flakes are world-anchored (the MR volume follows the head, the flakes stay), 1.2-3.2 cm, gentle
  (0.17-0.4 m/s) with the weather's wind as a soft draft and their own sway; they land on the floor, lie ~1.5-3.4 s and melt.
  Never within 0.34 m of the head (0.26 m of an eye), faint over the playing area ahead / below the face, melting by the
  fire (VR) and warmer near it; soft white over passthrough. A playback: the window show's gust (`WindowShowModel`, the one
  envelope) swirls it (eddies, a whirl about the player, a lift) and the near flakes take the playing instrument's colour
  (`SongStars.ColorOf`); exactly nothing at rest. ONE mesh, ONE draw (Transparent+5, after the window's frost), ten material
  vectors a frame, the vertex shader moves everything; renderer off while SNOW is off. Sunny: `tip_room_snow` once the snow
  has fallen 3 s. The VR window's own snow outside is unchanged.
- **A fireplace in your room** (2026-11-14; `Decor/MrFireplace.cs` the driver, `Decor/MrHearthPlace.cs` where it stands,
  pure; `World/HearthBuilder.cs` the hearth; `RoomModule.MrFireplace`). In mixed reality the FIRE switch lights the VR
  room's own fireplace (bricks, firebox, mantel, logs, coals, glow, flames, smoke, embers: the room's materials, its own
  flame and glow copies) free-standing in the player's room at 0.85 size (0.8 / 0.65 of that in a tight room): against a
  real wall in front of the player when the scene model (Core's `IRealWalls`) has a free stretch (clear of doors, windows,
  wall art, furniture, the instruments on display, the gallery's hung paintings, 1 m from the head, the flames not hidden
  by an instrument), else free-standing on the floor about 2.5 m in front, facing the player. FIRE on: it grows out of the
  floor and the fire flares (0.9 s); FIRE off: the fire dies and it sinks (0.6 s); the crackle (`SourceId.Fire`) comes from
  it and one warm flickering point light (range 4.5 m) falls on it and the instruments near it, off with the fire. Entering
  MR still turns FIRE off; leaving hides it at once and puts the crackle back at the VR fireplace. It moves (fade out / in)
  only when an instrument comes to stand in it, or walls arrive after a wall-less placing. Built on first entering MR
  (never for a VR-only session); 9 draws while it burns, none off; allocation-free per frame. Sunny: `tip_mr_fire` 8 s into
  MR while FIRE is off, pointing at the switch.
- **The snowy window joins in** (`Decor/WindowShowModel.cs` the numbers, pure; `Decor/WindowShow.cs` the driver;
  `Shaders/WindowShow.hlsl`; `RoomModule.WindowShow`). Only while a recording or a loop plays back (`RecordingPlaybackEvent`
  Started .. ended, its `PlaybackNoteEvent`s; REC ends it, live play never reaches it): every onset blows a gust through the
  snow (kick 0.35 + 0.65 vel, rise 60 ms, calm 0.9 s: the flakes circle in small eddies, whirl about the field's middle and
  lift a little, `WindowSwirl` in `Snow.shader`, exactly the plain snowfall at gust 0), and northern lights fill the sky:
  three curtains (low / middle / high notes; kick and floor tom low, snare and toms middle, hats and cymbals high) in the
  night palette (green, teal, violet, a touch of pink; instrument and pitch pick the shade), louder playing brighter, each
  beat a ripple; the flakes catch their light (`_WindowSnowTint`). VR: in `NightSky.shader` (sky metres, above the hills).
  (The mixed reality snowy window that joined in too was removed 2026-11-13, and with it `Frost.shader`'s `_AuroraOn`
  path: the frost is the pre-show shader again.) The end fades
  the lights (~5 s) and calms the snow; at rest the six globals are written once as exact zeros. No new renderer, light or
  draw. Sunny: `tip_window_show` once the lights have been up 3 s with a window in view. `TryGetWindow` / `Covers` give the
  window that shows now (only the VR room's, while the room shows; none in mixed reality) for what keeps off it (the
  playback paint).

- **Your song becomes a night sky** (`Decor/SongSkyModel.cs` the numbers, pure; `Decor/SongSky.cs` the drawing;
  `Shaders/SongStars.shader`, template `Room/SongStars`; the shared maths in Core's `Recording/SongStars.cs`;
  `RoomModule.SongSky`). Only while a recording or a loop plays back (`PlaybackNoteEvent` onsets; REC and live play make
  no star): every note sends a star up from above the part that played it (the piano's key, the drum or cymbal, the harp's
  string, else the played parts) to a sky framed from the head when the playback starts: across an arc around the
  player's view with the song's time (left to right; a loop's passes over the same arc, a repeated note replacing its old
  star), higher for a higher note (the take's own pitch range, at least an octave; the kit low to high), brighter and
  larger for a louder one, in its instrument's colour (piano gold, kit ice blue, harp lilac, handpan sea glass, hand drums
  coral, marimba green, bowls rose). Thin lines join each star to the one before it of its instrument: the constellation.
  VR: under the room's ceiling as built (`Static[RoomMat.Ceiling].bounds`: its height and walls, so it follows the room),
  with a soft night-blue field just under the ceiling; MR: in open air 0.6-1.15 m above the head, 1.45 m out. Never in
  the head's sphere (0.35 m). After the end it lingers 4 s, fades 2.5 s, then clears (renderer off; nothing else touched).
  256 stars at most (the oldest fade first). ONE mesh, ONE draw: star and line data written once per star, the shader
  animates them (rise, flash, twinkle, fades) from the model's clock. Sunny: `tip_song_stars` once a playback has made 6
  stars. The Recordings list draws each take's constellation on its row (`SongStars.BuildThumb`, Menu caches it per take).
- **The notes paint the walls** (`Decor/WallCanvas.cs` the placement maths, pure; `Decor/PaintPalette.cs` the colours and
  pitches; `Decor/WallPaint.cs` the driver and the mesh; `Textures/PaintArt.cs` the splash atlas; `Shaders/WallPaint.shader`,
  template `Room/WallPaint` from `Render/PaintTemplates.cs`; `RoomModule.WallPaint`). Only between a playback's
  `RecordingPlaybackEvent` Started and its end (`PlaybackNoteEvent` onsets; REC, live play and a live loop session's own
  layers paint nothing): each note a soft watercolour splash (four marks: blot, splatter, brush dab, bloom; random turn
  and stretch), its instrument's pigment shaded by pitch (rose piano, burnt orange kit, teal harp, ochre handpan, rust hand
  drums, sap green marimba, lavender bowls; low deeper, high lighter), radius 6-19 cm and strength by loudness, a loud one
  throwing 1-2 droplets. Placement (`WallCanvas`): every wall a grid of 10 cm cells; the walls in order round the room make
  one strip; the song moves along it at 0.15 m/s from 0.9 m before the wall point the player faced when it started, pitch
  sets the height (0.8-2.3 m), a hash of the note (index, key, instrument) spreads it (the same take paints the same
  picture); of 3 places round its target the clear one with the fewest splashes wins; the whole disc must lie on clear
  wall, else up to 8 steps of 0.45 m along the walls, else the note is not painted. VR: the walls are read once at boot from
  the room's own wall mesh in the room root's space (so the paint follows the turned room; the window opening is a hole)
  and every other mesh under the root blocks what stands within 0.5 m in front of a wall (fireplace, shelf, paintings,
  curtains, plants, lamp, flowers), with one cell of margin; the band is 0.5 m above the floor to 0.3 m under the ceiling
  (the night sky's). MR: Core's `IRealWalls` (MR's `MrPaintWalls`): the scene model's walls, windows, doors, wall art and
  furniture volumes, else one depth ray a frame along a fan about the look direction (splashes on the flat upright walls
  it finds), else nothing. 2400 quads per place (since round 2, below) (VR under the room root, MR under World), a ring; ONE draw each, only while
  something shows. The shader lights the paint per vertex as the wall (ambient SH, the moon, the lamp, fire and candles) and
  alpha-blends it (VR: the alpha channel kept; MR: an unlit wash, alpha as over); a splash blooms in over 0.45 s on the
  GPU; each painting fades with its own uniform. After the end: 7 s held, 3.2 s fade (round 2: a saved recording's
  splashes swirl into its frame meanwhile), then every splash gone and the
  renderers off. "Keep painting" (`AppSettings.KeyKeepPaintings`, the menu's row on a recording's page) keeps it until the
  next playback, which washes it away over 1.2 s while it paints its own. Sunny: `tip_paint_walls` (2 s of paint),
  `tip_paint_keep` (a painting about to fade with Keep painting off). Allocation-free per frame after boot (the canvas
  build at boot: about 40 ms in the editor, logged as `[Room] wall paint`). Tests: `WallPaintTests` (EditMode),
  `WallPaintPlayTests`, `WallPaintPerfTests` [Performance], `WallPaintShotsTests` [Graphics] (`Builds/Shots/wallpaint/`).
- **The creative paint** (round 2, 2026-11-13: "its not that creative in paint. can we do something creative for fun??"):
  - Each instrument paints its own way (`Decor/PaintBrushes.cs`, pure, shared by the walls and the gallery's pictures;
    a 16-cell brush atlas `Textures/PaintArt.cs`, made on a worker at boot): the piano's melody writes ONE flowing ribbon
    along the walls (0.4 m/s, its height easing toward each note's pitch, thicker when loud, drawn piece by piece as the
    brush moves, tapering at a phrase's end; chord notes add side flicks; past the window or the chimney it lifts and goes
    on); drums splat and their paint DRIPS down the wall over 1-2 s (kick big, snare a sharp burst, cymbals a spray); the
    harp sweeps soft rainbow arcs (a glissando sweeps one arc on); bowls spread three ripples; the handpan blooms petals;
    hand drums leave fingertip prints; the marimba strings beads. The shader animates them (Grow, Drip, Wipe, Band modes
    in the vertices): 2400 quads per place, still one draw, 4 vertices written per new quad.
  - Throwing paint (`Decor/PaintThrow.cs`, the gesture, pure): during a playback and its hold, a quick forward flick of an
    open hand throws a blob (a small lit sphere flying an arc) that splats where the throw points on a wall (VR room
    walls, MR scene walls, or the depth sensor's wall; never in the air), in the colour of the instrument heard last, with a
    soft splat (a filtered-noise one-shot, `Audio/Burst/NoiseShots.cs`). Never from a hand Layout calls busy (playing,
    holding a stick or mallet, at the menu), a pinch, a fist, a slow move, a stroke down or a tracking jump; at most 4 a
    second.
  - The gallery (`Decor/PaintGallery.cs`, `Decor/GalleryPicture.cs`): when a saved recording's playback ends, after the
    hold its splashes swirl off the walls (copies: a second renderer of the paint mesh, `_GatherOn`) into a gold frame
    and the song hangs there as a picture painted from the take's own events with the same brushes (deterministic: only
    "slot:id" is saved, `musicroom.gallery`), a brass plaque with its name and local date. Up to 6 frames on clear wall
    nearest the seat's view (MR: on the scene model's walls; no scene model, no gallery there), the newest replacing the
    oldest; deleting a recording takes its painting down; a hung frame keeps paint off its wall. Pictures 0.58-0.78 m
    wide as the wall allows (hung at 1.58 m, or higher above what stands there), a little self-light (the room's
    LitEmissive with the picture as its emission). What stands at the walls and can move (every rig's renderer boxes:
    the instruments on display, a rig moved to a wall) is read again once the layout settles (never mid-playback stage
    or mid-swirl; `WallCanvas.BlockMoving`): the paint keeps off it, and a frame something now stands at moves to the
    next clear place. Keep painting keeps the
    walls painted too; the framed picture goes up either way. The recording is the one the Menu noted (Core's
    `PlayingTake`) or the deck's own take found in the library by its events. 4 static draws per place for all frames.
  - Sunny: `tip_paint_throw`, `tip_paint_gallery` (PlaybackTips' order: walls, throw, gallery, keep). Tests:
    `PaintBrushTests` (EditMode), `PaintShowPlayTests`, `PaintShowShotsTests` [Graphics] (`Builds/Shots/paintshow/`).
- **The nature paint** (round 4, 2026-11-16: "make paintings of flowers birds nature scenery"; supersedes the round-2
  marks above, same mesh, shader, throw and gallery): the song paints a watercolour nature scene. `Textures/PaintArt.cs`
  is now an atlas of 16 nature motifs (blot/pebble, trunk, leaf, blossom, stem, grass, droplet, bird, butterfly, band,
  cloud, petal, crown, bead, tulip, mushroom cap; density + coverage, each quad its own pigment). `Decor/PaintBrushes.cs`:
  the piano's melody grows a green flowering vine (0.4 m/s, its height 1.05-1.95 m by pitch) with a blossom or tulip
  opening on a short stalk for every melody note (a golden heart, a leaf every other note) and buds hanging off it for
  chord notes; drums plant the ground (a strong kick an autumn tree in the drums' orange, a soft one a bush, the snare
  grass and pebbles, the hats sprigs, the ride pebbles, the toms leaves that drift down the wall, a crash a gust of them);
  the harp a flock: a bird a note along a swooping arc, a faint trail of its path; bowls slowly swelling clouds; the
  handpan butterflies with a dotted flight; hand drums mushrooms; the marimba slim trees. `PaintBrushes.Height` places each
  motif in its band (the ground low, the vine in the middle, birds and clouds high). Shader mode 1 is now Fall (the whole
  quad drifts down). Thrown paint bursts into a flower with petals flying round it, some drifting down, and leaves.
  `Decor/GalleryPicture.cs` composes each recording's framed picture as a small landscape from its events (deterministic):
  the sky by the song's mood (lively: a bright day, the sun high; calm: dawn or dusk with a low sun; slow, low and quiet:
  moonlight and stars), far hills following the melody's contour and near ones its slower swell, a meadow; long notes
  (held piano notes, ringing bowls) a river from the hills' lowest dip or a lake; trees from drum hits (cymbals small far
  ones) and the marimba, flowers from piano notes (taller stalks for higher notes), birds from harp notes (a glissando a
  flock), clouds from bowls, butterflies from the handpan, mushrooms from the hand drums; washes with granulation and wet
  edges, a deckled paper margin. About 15-30 ms per picture on a worker. Tests: `PaintBrushTests` (motif per instrument,
  where it belongs, the flock, the vine and its flowers, the landscape's contents and determinism), `WallPaintTests`
  (the atlas).

## Facing the fire (Core's `RoomFrame`, 2026-11-11)

"in music room VR i want to inverse my position. I should see the fire and whole room." The room is built exactly as
before, with the numbers below, in the ROOM FRAME (the old world); the Room root's local pose is `RoomFrame` (yaw 180
about the seat, slid 0.5 m back: `RoomFrame.Position` (0, 0, -0.5)), so the seated player (world origin, facing +z) sees
the fireplace 3.58 m ahead, the window on the left wall, the loveseat, coffee table and bookshelf in front, the old front
wall 1.62 m behind (1.12 m was too tight for the drum kit at its home once the head sat back a little). Transforms under the root are set in local coordinates (`Rh.Apply`, the lights, the moon, the
probe); the bench and the seat's contact shadow are built through `RoomBuilder.SeatFix`, so they stay at the seat.
`RoomScene.FlowerPerches` are world points; `RoomFurniture.FloorBlockers` / `Decor` (IRoomFurniture) are world boxes,
`RoomBlockers` / `RoomDecor` the room frame's. Everything else that holds room coordinates turns them through `RoomFrame`:
`GrabConstants.RoomBox` / `OriginClamp`, `SeatPlanner.RoomX0..Z1`, the placement solver and glides, Layout's display
spots, spares and saved stows, Audio's fire / snow / wind sources and acoustic box. `NightGround` draws its snow pattern in
the room frame (object space). Mixed reality is not affected (the room is hidden there). `RoomFrameTests` (EditMode),
`RoomFacingShotsTests` ([Graphics]: the fire seen over the default set and over the piano alone).

## Key constants

Room frame (the world turns it by `RoomFrame`; world: x +-3.2, z -1.62..3.58, fireplace (-0.3, 0, 3.58), window
(-3.18, 1.55, 0.68) on the left wall):
Room box x +-3.2, z -4.08..1.12, y 0..2.9 (`RoomDims`); window frame (3.18, 1.55, -1.18) yaw 90 (+z outward); fireplace
(0.3, 0, -4.08) yaw 180 (room on its local -z); snow field: rect (.94, .89), depth [.1, 15] bias 1.6, lateral (1.4, .9),
low (-1.6, 0), high (1.3, .9), fade [10, 18.5], size [.007, .022], #c8d4f0, haze #2a3554 x .6; frost half (.9, .85),
mullion .02, transom .204, reach .15; mound 1.92 x .155 x .065 at window-local (0, -.878, .04), bumps mulberry32(31).
Scenery atlas 512 x 1184 (slots of 512 x 384, 16-row gutters); leaf atlas 256 x 256 (light | dark); loveseat palette
shift 16 UV (128 weave tiles).

## Tests

EditMode (`Tests/EditMode/Room/`, 38). `RoomGoldenTests` (20) compares against golden values computed by the reference
code in Node: hash / vnoise / fbm, wood bytes (Core's `ProcTex.WoodPixel`) and the gloss maps derived from them,
plaster / weave / hill-snow bytes, `SnowWeather` timeline over 90 s with mulberry32(1234) gusts, gust envelope,
`FlakeAt`, sill mound, books, plants p1 and ivy (with the plant's own random stream), leaf blade, the primitive meshes,
Euler orders, the mirror. `RoomCanvasGoldenTests` (5): the canvas-drawn textures against the canvas goldens, frozen
reference pixels kept in `Tests/EditMode/Room/RoomCanvasGoldenTests.cs`: rug and brick interior pixels exact (field, both
border strokes, diamond centres; each brick row's face and mortar, both parities), leaves and night sky within 2
levels, paintings within 1 (the goldens' fixed-point gradients), the moon disc, the larger stars, and the night sky's two
ridge lines per column within one row. `RoomDataTests` (12): light conversion, switch geometry, touch zone and click
cylinders, flicker / sway formulas, the shader sway against a CPU pivot (upright plant and the fern on the turned
shelf), the loveseat palette quadrants, the particles' lifetimes, shaders import without errors and have templates,
template assets and their keywords, bake provider names and flags, texture shapes and atlases, scenery / night-sky
determinism, room dimensions.

PlayMode (`Tests/PlayMode/Room/`, all through `TestBoot`). `RoomModuleTests` (21): contracts and orders; the shell;
lights + flicker; the merged draw budget (28 static, 35 drawing, 38 snowing since the flowers, no property block); the plants' sway data
(six bases, amplitudes, both leaf variants, `_SWAY`); the loveseat's quadrants; smoke and embers dying at the wrap
height (8 s simulated: no live particle above the top, every death within one step of it, column full); the table rig
(8 renderers, no shadow casters, one label mesh over the toggles, no TMP objects left); `SetCandles` mesh swap; the
hill snow bound at boot; a fingertip poke flipping FIRE once (TestTap, event, lever, LED vertex colours); hover glow,
play surface, handles (fixed list, allocation-free), zone; a desktop click; `Set` from code; snow switch -> weather ->
window dressing; gusts; SetVisible / SetCandles; rig shadows; Decor phase
timing; zero allocation of Decor + EndFrame + panel Tick / Hover (~3 us per frame in the editor); background texture
generation (Core's soft dot unbaked). `RoomDrawTests` [Graphics] (2): profiler draw counts from the seat in four
directions, clear and snowing (<= 34 / 37), and the side-table rig alone (<= 8), every other renderer switched off
each frame and the camera's own baseline subtracted. `RoomScreenshotTests` [Graphics]: the seated head camera's draws
(<= 150) and `seat_piano`, `seat_fireplace`, `seat_right`, `seat_left`, `side_table_switches` (FIRE on),
`overview_plan`, `overview_corner`, `window_snow_full_cover`, `seat_right_snow` to `Builds/Shots/room/` (override:
`$MUSICROOM_SHOTS_ROOT/room`).

Commands, in this order: `Tools/unity.sh render-setup`, `bake`, `compile`, `test-edit`, `test-play`,
`test-play --gpu`.

## Known gaps / open points

- Ambient is Trilight as decided (ARCHITECTURE.md section 0). The specified ambient is dominated by a
  direction-dependent environment (spec 07 section 6.2): the back and left walls render ~20 % darker and the fireplace /
  window walls lighter than the reference photos. An L2 `ambientProbe` fitted to the six measured axes would close it if the decision is
  reopened.
- URP point lights fall off as 1/d^2; the spec's decay is 1.5-1.8. The light matches at the seat, but surfaces within
  ~0.5 m of the fire or the floor lamp are up to ~2x brighter than specified.
- No tone mapping (decided): very dark values (night sky) read slightly lighter than the reference.
- The window glass (smoothness .97) mirrors the fire (ahead of the seat since the room faces it; it was behind) through the room's reflection probe, a warm spot
  low in the right-hand pane (`seat_right.png`); a generic environment map would show no such reflection.
- The canvas textures' anti-aliased edges (diamond, brick and ridge edges, leaf outline, stars) differ from the goldens by a
  few levels; only interior pixels and ridge rows are pinned.
- The flake size uses `_SnowPx` from the eye-texture height (XR) or the screen; UNVERIFIED on device, as is the GPU
  cost of the fire, snow, frost and the plants' vertex sway.
- `CONTRACT-REQUESTS.md`: A (bake-spec anisotropy; runtime workaround in place), B (`SynthSource.Awake` race: fixed in
  this clone's Audio folder, to merge), C (the foundation's `LitEmissive` template loses `_EMISSION`; Room uses its own
  `Room/LitEmissive`), D (ARCHITECTURE.md section 13 bake-name list).

## Furniture (IRoomFurniture, 12-new-instruments/platform.md 4.1)

`RoomBuilder` measures every piece while it builds it (`Footprint`, `World/Furniture.cs`): each piece's code runs
between `Begin(name, frame)` and `End`, and every vertex `Add` appends meanwhile grows the box in the piece's own frame;
the two curtain panels are measured from their own vertices in the fabric batch. `RoomFurniture.FloorBlockers` (room
coordinates, Unity: centre, half sizes, yaw; heights above the floor) are the display plan's fifteen, in its order: a
piece square to the room is published axis-aligned and clamped to the room's interior. A floor plant's blocker is its
body (pot, soil, stems), grown by its sway at the tip; its spread of leaves (up to 0.9 m from the pot) and what stands
on a blocker (the fern on the bookshelf, the ivy on the sill, the coffee-table candle) are `RoomFurniture.Decor`,
measured the same way but not blockers: the display plan was laid out without them. The window view is the window
frame above 0.85 m, as deep as the inside sill. Measured against the display plan's FURNITURE table (its Node prototype; centre / half sizes / heights;
`Tests/PlayMode/Room/RoomFurnitureTests` logs it):

| Blocker | Measured | Reference | Difference |
|---|---|---|---|
| bookshelf | (-3.020, -2.680) +-(0.170, 0.553) y 0-1.925 | (-3.03, -2.68) +-(0.17, 0.56) y 0-1.93 | centre 1 cm, depth -7 mm |
| floor lamp (base, pole) | (-2.400, 0.520) +-(0.150, 0.150) y 0-1.60 | +-0.16 y 0-1.80 | the shade is its own blocker |
| lamp shade | +-0.220 y 1.50-1.80 | +-0.22 y 1.50-1.80 | none |
| plant P1 / P2 / P3 / P4 (body) | about +-0.20-0.32, y up to 0.84-1.36 | +-0.30 y 0-1.20 | within 10 cm; heights -0.36..+0.16 |
| loveseat | (1.652, -2.828) +-(0.625, 0.428) y 0-0.818 yaw 48.7 | +-(0.63, 0.43) y 0-0.85 | 3 mm, top -3 cm |
| coffee table | (0.850, -3.130) +-0.300 y 0-0.435 | y 0-0.45 | top -1.5 cm |
| hearth (the whole fireplace) | (0.300, -3.713) +-(0.950, 0.368) y 0-2.90 | (0.30, -3.85) +-(0.80, 0.25) y 0-1.40 | the hearth stone (0.735 m deep, 1.9 m wide) and the chimney breast to the ceiling |
| curtain near / far | x 3.125 +-0.032, z 0.08 / -2.44 +-0.200, y 0.02-2.62 | +-(0.06, 0.20) y 0-2.62 | pleats 3.2 cm deep, not 6 |
| inside sill | (3.100, -1.180) +-(0.100, 1.050) y 0.59-0.63 | the same | none |
| window view | (3.100, -1.180) +-(0.100, 0.970) y 0.85-2.47 | (3.15, -1.18) +-(0.05, 0.90) y 0.85-2.40 | as deep as the sill, the frame's width and top |
| bench | (0, -0.100) +-(0.380, 0.170) y 0-0.515 | y 0-0.54 | top -2.5 cm |

Decor: the plants' leaves P1 (-0.947, -3.521) +-(0.813, 0.559) y 0.56-1.32, P2 (2.681, -3.548) +-(0.519, 0.532),
P3 (2.556, 0.520) +-(0.644, 0.600) y 0.62-1.52, P4 (-2.750, -1.897) +-(0.450, 0.522); the ivy (3.018, -1.855)
+-(0.182, 0.253) y 0.14-0.76; the candle (0.930, -3.150) +-0.063 y 0.435-0.59; the fern (-2.935, -2.479) +-(0.265, 0.387)
y 1.93-2.34. Every registry display spot stands 3 cm clear of every blocker; three touch decor (logged, reported): the
piano's front-wall-right spot reaches 37 cm into plant P3's leaves, the bowls' sill spot stands on the ivy's pot and the
hand drums' window-far spot meets the ivy's trailing stems.
