# MusicRoom.Drums

The drum kit, the two drumsticks, the nod kick and the kit's play surfaces.
Spec: `Docs/05-instruments.md` sections 3, 4 and 7 (`drums`, `sticks`, `sticks_build`, `curves`). Checklist: ARCHITECTURE.md section 15.2. Built against the round-1 foundation (section 21).
References: `MusicRoom.Core`, `MusicRoom.Hands`, `MusicRoom.Audio`. It never references another feature module.

## Public contract

| What | Where |
|---|---|
| `DrumsModule` (Order 31) | the only `IAppModule`; registers the services below in `Init` |
| `IInstrument` = `DrumKit` | Kind `Drums`, TickOrder 10, `AcceptsStickPointers` true (gets `Input.AllPointers`), `IsPresent` = active and `Rig.InScene` |
| `IDrumsApi` = `DrumKit` | `Pieces` (ids 0 snare, 1 tom1, 2 tom2, 3 floor, 4 kick shell, 5 kick pad, 6 hat, 7 crash, 8 ride; the pad's kind is `"kick"`), `KickId` 4, `KickPadId` 5, `HatId` 6, `HatOpen`, `TryPickUp(hand, tip)`, `SetHatOpen(open, hand, playback)`, `PlaybackHit(kind, vel, strike)`, `PlaybackChoke(kind)`, `AnyStickHeld`, `ReleaseAllSticks()`, `EveryHandPointerFartherThan(m, pieceId)` |
| `TryPickUp(hand, tip)` | the stick pick-up, called by Layout FIRST in its Grab pass: `Grabbed` within .12 m of a free stick's shaft (sets `HandState.Holding`, reparents the stick to `App.WorldRoot` keeping its world pose, sets its pointers `Down`); `Near` within the .24 m dead zone; `None` otherwise, or with the kit out of the scene, or a hand already holding. The pinch point is the thumb-index midpoint when tracked, else `tip`. Layout decides whether the pinch is consumed (it calls on a grab-pinch start, `HandState.GrabPinchStart`, not made like the OS system gesture); Drums never touches the pinch flags |
| `Rig` (RigId.Drums) | `Group` = the kit root (floor origin), priority 1, homes All (.2, 0, -.26) yaw 90, Single (0, 0, .1) yaw 0; `Handles` = one list filled once in `Init` (28 renderers: 5 shells, 5 resonant heads, 5 chrome sets, pad body and rim, 8 stands, 2 spurs, pedal; enabled with live bounds but `forceRenderingOff`, see draw budget); `Zone` = the rig zone (the kit's world box, the floor blob's rectangle included, + .12) |
| `PlayedParts` | batter heads, the pad's top, the cymbal plates and nuts, the lower hi-hat, the sticks (17) |
| Events | publishes `NodEvent { Velocity }`; every hit / choke / hat move goes through `IRecorder.Add` (`drum`, `drumChoke`, `hat`) |
| Pointers | four external pointers `stick0`, `stick0-shoulder`, `stick1`, `stick1-shoulder` registered with `PointerInput.RegisterExternal`; `HandState.Holding` is the `Drumstick` (an `IHandTool`) |
| Audio | `SourceId.Snare/Tom1/Tom2/Floor/Kick/Hat/Crash/Ride` parented to their piece groups (kick = the shell; the pad plays through it), `DrumRoom` at kit-local (.05, .8, .4); parked at their last world positions while the kit is out of the layout |
| `IContactSurface` = `DrumKit` (10-upgrade/experience.md 2.1) | `QueryContact`: piece-local hypot(x, z) <= r at the height the surface is DRAWN (the heads and the pad top at `HeadLift` 1 mm; a cymbal's plate `piece.Top`, at its current tilt, along its lathe profile `DrumSpec.CymbalTopY`: 6 mm on the bow at 0.6 r, 16 mm on the bell), so shadows lie on what is seen and the drawn stick stops on it (the hit test keeps the y = 0 strike planes); `Use` Both on the heads and the pad, Shadow only on the cymbals (a hand on a cymbal is the choke gesture) and only down to 1 cm under a plate; nothing while the kit is held (`DrumKit.Grab`, set by the module each frame). Visual only |
| `IDrumsApi.StickShadowSpheres(Span<Vector4>)` | E3: the DRAWN tip-ball centre (xyz) and radius (w) of each held stick; Core shadows them |
| Desktop click targets | one thin disc `MeshCollider` (r = the piece's, 4 mm) on `Layers.PlaySurface` per head, pad top and cymbal group (`DrumKitParts.Clicks`, `DrumKit.PieceOf(collider)`) |
| Material template | `Drums/Cymbal` (URP Lit, smoothness from the albedo alpha) via `DrumsMaterialTemplates : IMaterialTemplateProvider` -> `Drums/Resources/MaterialTemplates/Drums/Cymbal.mat` (made by `render-setup`) |
| Textures | heads: Core's `SharedTextures.DrumHead` (`Core/drum_head`); plates: `Drums/cymbal` = Core's `ProcTex.CymbalPixel` packed with smoothness (255 - roughness) in alpha, via `DrumsTextureProvider : ITextureBakeProvider` (`Tools/unity.sh bake`; `GeneratedAssets` runtime generation is the fallback) |

## Frame phases (in order)

| Phase | Work |
|---|---|
| `StickPoses` | held sticks follow their hands (grip = thumb/index midpoint, axis = wrist->grip drooped 12.6 degrees toward the palm, butt .13 behind the grip) and set their tip / shoulder `Pointer.Next` BEFORE `PointerInput.Update`; tracking loss freezes and silences the stick and drops it after 1.5 s; released sticks settle back (k = 1-(1-s)^3, s += 2.5 dt); a stick still in a hand when the kit left the room is released here |
| Instruments (Core) | `DrumKit.Tick`: hi-hat stand pinch, cymbal choke, the strike test per piece x pointer, the kit's motion (E7: cymbal tilt, head give, kick beater) / lower hi-hat, the desktop click; then `Hover` |
| `StickHover` | one glow per free stick within .14 m of a fingertip (not the mouse hand; skipped while MR is placing); then E2 `DrumSticks.UpdateVisuals` (below), so Core's contact step 8a reads the drawn tips |
| (`Grab`) | Layout's: it calls `IDrumsApi.TryPickUp` first for each hand with a fresh pinch (or a closing fist while not playing) |
| `StickReleases` | let go after the hand stayed open (thumb-index > .05 m and not grasping) for more than .25 s |
| `NodKick` | `NodKick.Tick(dt, active, head.forward, now)`; active = `App.IsPresenting`, not MR placing, no rig held, kit present; a nod plays the kick shell (forced, {.2, head}) and publishes `NodEvent` |
| `EndFrame` | the drum sources move to a park object while the kit is out of the layout (so recorded drums keep sounding where the kit was) and back onto the pieces when it returns |

XR gates (section 3.3): the nod kick runs only while `App.IsPresenting` (true in XR, and in tests booted with
`TestBoot(simulateHeadset: true)`); the desktop click only while it is false. The stick pick-up is gated by Layout.

## Behaviour (exact constants in `DrumSpec`, `StickSpec`, `NodSpec`)

- Strike test, per piece x pointer active this frame and last, never a mouse-hand pointer
  (`Pointer.FromMouse`): shoulders hit cymbals only (and, with the modelled kit, the snare's rim as a cross-stick); arm once tip or prev is > .02 above the head; stroke = prev.y > 0 >=
  tip.y with prev.y <= .10 (finger) / .35 (stick); skip when disarmed; crossing radius from the prev->tip path; reach
  r + .03 on drums (rim), r on the pad and cymbals; any pass disarms; vy = (prev.y - tip.y) / max(SampleDt or dt, 1 ms);
  silent under v0; vel = clamp(((vy-v0)/(v1-v0))^.7, .05, 1) with (v0, v1) = (.12, 1.5) fingers, (.3, 3.0) sticks.
- Strike zones: pos = clamp(rr/r); hat top / edge (> .75); crash, ride bell (< .22) / bow / edge (> .85);
  drums rim past .9 r (not the pad); `open` = the hi-hat state.
- Hit: 3 ms per piece unless forced; 45 ms per owner (all five fingertips, or the stick's tip and shoulder,
  are stamped); stamps `owner.LastPlayMs` (stick hits stamp the stick, not the hand); `Drum(hat && open ? HatOpen :
  kind)` at once (`when` 0); records `drum`; flash 1; E7 motion at the crossing point (`Hit(..., Vector2? at)`; without
  a point, (0, -pos r) on the player's side): a cymbal tips there, a batter head gives there, every kick swings the beater.
- Choke: a cymbal hit 60..3000 ms ago, not choked, the first non-stick, non-mouse pointer with speed <= .35,
  |y| <= .03, within r, |prev.y| <= .03, |dy| <= .004 -> `Choke(kind)`, the plate's rocking dies (x exp(-dt/.03) a frame
  while choked), stamps the hand, records `drumChoke`.
- Hi-hat stand: an index-tip pointer on its hand's grab-pinch start (`HandState.GrabPinchStart`: the
  thumb-index rule OR Meta's own index pinch, since the u-arrange merge; the rule alone flickers on the headset; not the
  mouse hand), hand holding nothing,
  within .08 of the pole and .12 < y < hat.y - .08 -> toggle; closing = `Choke(Hat)` + `Drum(HatClose, .7)`; records
  `hat` (not in playback). The lower plate eases y += (target - y) min(1, 18 dt) between -.016 (closed) and -.05 (open).
- Visuals, E7 (experience.md section 9; `KitMotion.cs`, replacing the old wobble and 1.5 % scale pulse): flash -= 6 dt
  (a diagnostic; `FlashScale` 0, nothing breathes). `CymbalMotion`: a hit at plate-local (x, z), vel adds
  k = maxTilt 2 pi f0 vel / R to (Wx += k z, Wz -= k x); theta'' = -w0^2 theta - 2 zeta w0 theta' + 2 Omega J(theta')
  in <= 4 ms substeps, capped at 1.5 maxTilt; plate = Euler(Tx, 0, Tz): crash 1.6 Hz / .115 / swirl .35 Hz / 8 deg,
  ride 1.2 / .10 / .25 / 4 deg, open hat 2.5 / .20 / .5 / 3 deg, closed hat 0 deg. `HeadGive`: batter heads are ringed
  discs (6 x 48 + 1 = 289 vertices); a hit dents w = .004 vel exp(-d^2 / (2 (.25 R)^2)) exp(-t / .018) on the hit frame
  and the 2 after it (normals from the dent's slope), then the rest vertices come back. `DrumKit.Tick` ages every head
  and the beater FIRST, then this frame's hits strike: a hit from any phase (the hit test, the nod, playback, a desktop
  click) shows its t = 0 pose on exactly one frame (the old "fresh" flag held it twice for hits after the Tick). `KickBeater`: axle (.12, .08,
  .62), shaft .19, felt ball .025; rest -30 deg, contact +16.8 deg on every kick (pad, nod, shell, playback), critically
  damped back with tau 35 ms; the footboard pitch 14.3 deg - 10 deg (theta - rest)/(contact - rest), turning about its
  floor end. The kick's head gives where the ball lands. Transforms and vertices are written only while moving.
- Hover: -.005 <= y <= .1 and rr <= r + .02 -> a .09 disc at (x k, .002, z k), k pulls it 3 cm inside the
  edge, normal = piece up, strength 1 - y/.1.
- Play surfaces: a piece when -.12 <= y <= .25 and within r + .04, else the hat-stand spot.
- Desktop (section 0): while `!App.IsPresenting`, a plain left press (no Shift
  / Alt) casts `Physics.Raycast(ray, out hit, 3, Layers.PlaySurfaceMask)`; the kit acts only when the nearest hit is
  one of its own click discs (another module's nearer collider takes the click), a drum head only from above, and
  plays that piece once at .75 with the strike zone of the hit point. The mouse hand's own pointers never strike,
  choke or pinch the hat, so a click never fires twice.
- Sticks: see the phase table; mesh = shaft r .0075 -> .0045 x .4 + tip ball r .0062 (#d9b88a r .6) and a
  tape band r .008 x .09 at .08 (#222222 r .9); rest = an X across the snare (snare-local (-+.068, .009, -.188),
  AngleAxis(90, right) * AngleAxis(-+20.05, forward)).
- Nod kick: pitch = asin(forward.y); rate > 1.6 rad/s grows the swing; fire once while armed, swing <= .16 s
  and > .18 s since the last kick, vel = clamp(.25 + .75 (peak - 1.6)/4.9, .25, 1); re-arm under .8 rad/s.
- Construction: per drum an open shell (48 sides), batter head (speckle, r .85), resonant head
  (#c9c2b4, .85 opacity), chrome rims (torus r + .006, tube .007) and 8 lugs; pad (rubber body, chrome rim, rubber top);
  cymbals = lathe (bell 16 mm, 64 sides, gold ring texture, metallic .9) + chrome nut, hi-hat lower plate flipped;
  tripod stands (pole r .011, legs r .007 from .32 to feet .3 out, leg turn .4 or .91 for the ride); kick spurs and
  pedal. Materials are clones of template assets (`MaterialLibrary`); no `MaterialPropertyBlock` anywhere.

## Sticks that rebound (10-upgrade/experience.md E2, visual only)

Each stick's meshes hang under a child `Visual` of its `Root`; only `Visual` turns, by phi about the grip (13 cm from the
butt) and the axis normalize((tip - grip) x n), so `Root` and the tip / shoulder pointers keep the raw pose the hit test
reads (`DrumMath.StickPose`). `StickVisualMath.Step` (1:1 with the reference stickVisualStep): phiC lifts the raw tip ball
(r .0062) onto the surface under it, psi is a grip spring (wn 2 pi 8, zeta .8, 2 ms substeps) kicked AFTER the hit frame
with omega = max(omega, (1 + .25) v / .27), v = the approach speed the hit test measured (`DrumKit.Tick` records it on the
`Drumstick` when `Hit` succeeds with its pointer), phi = clamp(phiC + max(psi, 0), 0, .45) x w, w fading the correction
out between phiC .30 and .45 rad and, like the finger clamp, within 15-30 degrees of the surface normal
(`ContactMath.NormalWeight`: a stick pointing almost straight at a head would turn about a jitter-set axis). (max(psi,
0): the spring's small undershoot never pushes the drawn tip below the surface while phiC holds it; identical to the
reference on every vector frame.) The surface is the kit's own, at its drawn height (the head 1 mm up, a cymbal's
profile at the tip's radius) (`DrumKit.StickSurface`): the piece the tip ball came down onto from above stays its surface while the tip is inside its
footprint and under the ball's radius (down to 12 cm below), else the nearest piece under the tip within 30 cm; a stick
under a cymbal is never lifted through it. Free sticks are drawn as they lie (`Visual` reset).

## Draw budget (sections 14 and 17: drums <= 25)

21 draws since E7 (19 before, measured with the profiler, `DrumsGraphicsTests`: "Draw Calls Count" of a view framing
the kit, 20 with the kit, 1 without; the beater adds its chrome and its felt ball). Every part that never moves on its own (shells, resonant heads, chrome, stands, nuts, the pad's body)
is baked into one kit-frame mesh per material: chrome, red shell, pearl, resonant heads (transparent), rubber (5). Their
own renderers stay enabled with live bounds (grab-handle OBBs for Layout, the rig zone, the played-part split) but
draw nothing (`forceRenderingOff`). Drawing alone: 5 batter heads (give) + the pad's top, 3 plates (rocking) + the
lower hi-hat (pedal), 4 stick parts, and the E7 kick beater: one dynamic chrome mesh (shaft + footboard, rewritten only
while it moves; the pedal's own renderer stays a hidden grab handle, out of the static chrome mesh) and the felt ball.

## Files

`DrumsModule.cs` (wiring, phases, nod, audio parking) · `DrumKit.cs` (instrument, API incl. `TryPickUp`, hit test,
hover, rig zone, desktop click) · `DrumContact.cs` (partial `DrumKit` / `DrumSticks` / `Drumstick`: the contact surface,
the stick surface, `UpdateVisuals`, `StickShadowSpheres`) · `StickVisual.cs` (`StickVisualMath`, `StickVisualState`) · `DrumPiece.cs` · `DrumSpec.cs` (every constant and the piece table) · `DrumMath.cs`
(pure formulas) · `DrumKitBuilder.cs` (geometry, materials, static batching, click discs) · `MeshKit.cs` (procedural
meshes, outward winding guaranteed) · `DrumSticks.cs` · `NodKick.cs` · `DrumTextures.cs` (the packed cymbal map + bake
and material template providers) · `CONTRACT-REQUESTS.md`.


## The modelled kit (10-upgrade drums.md 2.3): deleted 2026-11-11

The drums play the classic sound only; the modelled kit's unit and `DrumKit.ModelledKit` (the cross-stick and the
silent hi-hat pedal's openness) were deleted on 2026-11-11, so a stick's shoulder strikes cymbals only. The implement
(`DrumStrike.Implement`) is still sent (today's kit ignores it). What follows is the history.

- **Implement.** Every `Drum` call carries `DrumStrike.Implement` (`DrumMath.ImplementOf`). A stick tip strikes as
  `Tip` and a stick's shoulder as `Shoulder`. A fingertip strikes as `DrumSpec.FingertipImplement`, which is `Tip`.
  The spec suggests `Finger`, but on the reference kit that is 11–29 dB quieter than a stick at the same stroke value,
  and fingertips are the main way to play here. Set the constant to `Finger` for real hand-drumming.
- **Cross-stick.** A stick's shoulder crossing the snare's rim zone (rr > `RimFraction` r) plays `DrumZone.CrossStick`.
  A shoulder pass over the rest of the head disarms and stays silent. It is recorded as zone `xstick`.
- **Hi-hat pedal.** Opening the hat by the stand pinch also sends `IDrumKitSound.SetHatOpenness(1)`, a silent pedal
  move, so a ringing closed hat opens. Strokes on an open hat stay `HatOpen`, and closing stays
  `Choke(Hat)` + `Drum(HatClose, .7)`.
- **Two sticks on one drum** need no stick id. The per-hand / per-stick 45 ms window leaves at most one stroke per hand
  in flight per drum. The kit's two contact slots take the two hands, and a third stroke waits for a stick to leave.

## Tests

EditMode `Tests/EditMode/Drums` (namespace `MusicRoom.Tests.Drums`; a geometry-only kit at the origin driven by
hand-made pointers and an exact clock; 54 tests):
- `DrumMathTests`: finger / stick / nod velocity curves vs `curves.*` and the spec's worked examples, strike zones,
  crossing radius, the stick grip droop, settle easing, segment distance, hat-stand and piece zones, the packed cymbal
  map vs Core's `ProcTex.CymbalPixel`.
- `DrumKitTests`: a finger strike (velocity, pos, zone, events, owner stamping, flash scale), crossing point decides
  the zone, arm / disarm, a slow pass disarms silently, finger .10 vs stick .35 start height, rim reach / pad / plates,
  shoulders on cymbals only, the 3 ms and 45 ms windows with a flam, choke at 60 ms (not after 3 s, not by a stick, a
  moving or lifting hand), hi-hat stand pinch and chick, open hat and the 18/s lower plate, wobble and flash formulas,
  hover discs, play surfaces, playback (forced, first piece of a kind, not re-recorded), the desktop click through the
  `PlaySurface` colliders (zones, from above only for drums, a nearer foreign collider takes it), the mouse hand never
  strikes / chokes / pinches the hat, `TryPickUp` results, `EveryHandPointerFartherThan`.
- `DrumGeometryTests`: piece poses / normals, stand tops and feet, kick extras, the whole kit vs `drums.kitBox`, the
  sticks' rest X vs `sticks`, every mesh faces outward, played parts vs handles (and no shadow under the kit),
  `Rig.Handles` is the one list, static meshes = the hidden parts' geometry and 19 drawing renderers, click discs on
  layer 29, the rig zone, cymbals clear above every drum, the lower hi-hat under the top plate.
- `DrumSticksTests`: pick-up radius / dead zone / nearest free stick, the grip pose and pointers, the .25 s open-hand
  release (a pinch, Meta's pinch (`HandState.GrabPinching`) or a fist keeps it), the .4 s settle, the 1.5 s tracking-loss grace, the pick-up glow (none for the
  mouse hand), no kit no sticks.
- `NodKickTests`: one kick per swing with the curve velocity, slow looks never fire, window and debounce, re-arm.
- `StickVisualTests` (10-upgrade E2): both frame tables of `experience.vectors.json` stickVisual (overshoot and rebound
  stroke: raw tip, hit speed, phiC, phi, drawn tip to 1e-4), the deep-dip fade, only `Visual` turns about the grip and
  the drawn ball sits on the head, `StickShadowSpheres`, a stick under a cymbal is never lifted through it.
- `DrumContactTests` (E1 / E3): heads and the pad (Both), cymbal plates (Shadow, at their current tilt, not 2 cm under),
  outside / above the query, nothing while held.
- `DrumSpacingTests` (2026-11-14, "two drums intersect little"): every two pieces as built (shells with hoops and lugs,
  heads, the pad, plates, the hi-hat closed and open) keep `DrumSpec.MinPieceGap` (2 cm); the snare / rack-tom / pad
  pairs that touched before the toms slid back (tom1 (-.04, .86, .625), tom2 (.32, .85, .482)) and the pad left
  ((-.29, .6, .19)) stay within 2-6 cm. GPU pictures: PlayMode `DrumSpaceShotsTests` (Builds/Shots/drum-space).

PlayMode `Tests/PlayMode/Drums` (namespace `MusicRoom.Tests.PlayMode.Drums`; every boot through
`TestBoot(simulateHeadset, manualFrames: true)` with `_boot.Step(1/72)`; sounds asserted on `AudioTestTap.Calls`
(`AudioCall.Drum` kind / vel / strike / when, `AudioCall.Choke`), never on the Audio module's recipe `Commands`):
- `DrumsPlayTests` (11): registration, source parenting (DrumRoom at the kit centre), the four stick pointers; a
  scripted finger strike on the snare; the hi-hat stand pinch and the chick (`Choke(Hat)` + `Drum(HatClose, .7)`);
  `TryPickUp` called by a probe module in the Grab phase exactly as Layout does (None at 35 cm, Near at 15 cm and
  consumed, Grabbed at 2 cm; no pick-up without it); a scripted stick pick-up, the same-frame tip pointer, a strike on
  the stick curve, the open-hand release and settle; tracking loss; the crash choke at 60 ms; the nod kick and
  `NodEvent` with a simulated headset, and none without; the kit leaving the layout (sticks released, sources parked
  and back); hot paths allocate nothing (`Is.Not.AllocatingGCMemory`).
- `StickVisualPlayTests` (10-upgrade E2 / E3): a scripted stroke 4 cm through the snare plays exactly one snare hit,
  the drawn tip never goes below the ball radius while phiC < .30 (the hit frame draws it on the head), it rebounds and
  settles back to the raw pose, the drawn tip shadows the head; a fingertip in the snare head is lifted; the stick
  visuals and the contact query allocate nothing.
- `DrumsGraphicsTests` (`[Category("Graphics")]`, `Tools/unity.sh test-play --gpu`): the draw budget (renderer count and
  profiler delta), and renders of the kit from the seat and around it, the sticks at rest and a stick in a scripted
  hand, to `Builds/Shots/drums/*.png`. Two test lights and a reflection probe stand in for the Room
  module, a stub in this clone (so its floor blob under the kit is not in these shots either).

## Known gaps and deliberate differences

- A plain lathe of the cymbal profile faces INTO the plate, so from above it shows the flat underside; here the plate
  faces out and the bell is visible. Same profile, UVs and ring texture.
- Clear coat (toms, floor, kick shells) has no URP Lit equivalent on Quest: smoothness .85 instead of .8 stands in for it.
- The kit's contact shadow is drawn by the Room module (`RigShadows`, `MusicRoom/FloorDecal`), not here
  (CONTRACT-REQUESTS merge note B). It is not a child of the kit, so it stays on the floor when the kit is lifted.
- Linear-space blending: the resonant heads (.85 opacity) let a little more of what is behind them through than an
  sRGB-space blend would.
- The desktop click is tested through the collider ray path (`DesktopHit(Ray)`); the Input System mouse-down gate in
  `Tick` is not simulated in batchmode.
- The hand holding a stick is not dimmed (`MetaHandVisual.appAlpha` is available if wanted).
- Device-only (UNVERIFIED): stick feel at the real tracker rate (the grip snaps to each sample, smoothing 1), nod
  thresholds with real head motion, the sound of fast stick rolls.

## 10-upgrade (experience.md E7)

- New: `KitMotion.cs` (`CymbalParams`, `CymbalTilt`, `CymbalMotion`, `HeadGive`, `KickBeater`), `MeshKit.RingedDisc`,
  `DrumMath.CrossingPoint`, `DrumKit.Hit(piece, vel, p, force, strike, Vector2? at)`, `DrumKit.Beater`,
  `DrumPiece.Tilt` / `Give`, `DrumKitParts.Beater` / `BeaterRenderers`, `DrumMaterials.Felt`. Removed: `DrumPiece.Wobble`,
  `WobblePhase`, `Scale`, `TopTurned` and the wobble constants; `DrumSpec.FlashScale` is 0.
- For E1 / E2 (another clone): the cymbal plates' current tilt is `DrumPiece.Tilt` (and `Top.localRotation`), the contact
  surface E1 / E2 asks for.
- Tests: `EditMode/Drums/KitMotionTests` (`CymbalTiltTests`: the crash, ride and open-hat vectors, the sign rule, choke and
  cap; `HeadDimpleTests`: the 5 headDimple rows, the ringed head's dent and normals, 3 frames; `KickBeaterTests`: the
  contact point, 90 % back in 0.136 s, no overshoot, the footboard), `DrumKitTests` (`CymbalsTipDownWhereStruckAndDrumsDoNotScale`,
  `HeadsGiveForThreeFramesAndEveryKickSwingsTheBeater`), the existing allocation test (every piece hit, the kit ticked),
  and `PlayMode/Drums/KitMotionShotsTests` (`--gpu`: crash rest / peak / 0.5 s, snare head rest / give, kick beater rest /
  contact, written to `Builds/Shots/exp-world/`).


## Integration of the 10-upgrade (2026-11-01)

The three upgrades that touch the strike test now share one line in `DrumKit.Tick`: the strike carries the striking part
(`DrumMath.ImplementOf`), a stick's shoulder across the snare rim plays `DrumZone.CrossStick` with the modelled
kit, `Hit(..., DrumMath.CrossingPoint(prev, tip))` passes where the head was crossed (E7: the dent and the cymbal tilt), and
a stick that struck records its approach speed for its drawn rebound (E2). Since 2026-11-10 the drums play only today's kit
(`DrumSynth`; the modelled kit was deleted on 2026-11-11) and `PlayMode/Audio/DrumKitPlayTests` plays it through the
module.

## The many-instrument platform (12-new-instruments/platform.md 3.3, 4.6, 13.3; phase 0)

`DrumsPlatform` (`DrumsPlatform.cs`) and `DrumToolProvider` (`DrumToolProvider.cs`), registered by `DrumsModule.Init`
(`DrumsModule.Platform`, `DrumsModule.Tools`), are thin forwards over today's kit; `IDrumsApi` stays.

| Service | Behaviour |
|---|---|
| `IStowable` (Kind Drums) | `SetStowed(true, mount)`: the static display configuration: both sticks let go and lying on the snare at once (their rest pose; a displayed kit is not ticked, so the 0.4 s settle would not run), the nod kick off (`NodKick.Enabled`, given back by `SetStowed(false)` as it was); the stick glows and the kit's tick stop with `Rig.InScene` (Layout's) |
| `IInstrumentPlayback` | `drum` -> `PlaybackHit(kind, vel, StrikeOf(e))` (`{ pos ?? .3, zone || head, open }`), `drumChoke` -> `PlaybackChoke(kind)`, `hat` -> `SetHatOpen(open, null, playback: true)` (Lessons' classic lines, the same calls: tested); `ReleaseAll` and `CloseLane`: nothing is held by playback |
| `IGuideAnchors` | `piece.<kind>` (snare, tom1, tom2, floor, kick, hat, crash, ride): the strike surface's centre (the piece group's origin, its local y = 0 plane), up = its normal; `piece.kick` is the drum, `pad` the kick pad. Parsed without allocating; all nine are in the descriptor |
| `IHandToolProvider` (Owner Drums) | `NearestFree(tip)` = `DrumSticks.NearestFree` (the shaft distance `TryGrab` measures), +inf while the kit is not at the seat; `TryPickUp` = `IDrumsApi.TryPickUp`; `AnyHeld`; `ReleaseAll`; `ShadowSpheres` = `IDrumsApi.StickShadowSpheres`. Fast Motion Mode reads it (any held tool of a FastMotion instrument) |

Conformance check 3 (`Tests/PlayMode/Drums/DrumsPlatformTests`): measured against `Rig.Handles`, the kick in
registry.mjs was centred on its batter head (z .50-.90): its shell and hoops run from the head at z .70 back to 1.107, and
the pedal lies in front (z .43-.65). `DrumsDefinition` now has the measured kick (0.12, 0.90) +-(0.30, 0.21) y 0-0.59 and a
pedal box (0.12, 0.54) +-(0.05, 0.11) y 0-0.06 (the reference planner plans all 92 sets exactly as before with them). The
stands are modelled as their poles: the tripod feet (hip at 0.32 m, feet 0.29 m out at the floor) are left out by design,
as in registry.mjs (with them the default set no longer plans). Margins with the stands as poles: x +0.169 / +0.099,
y -0.013 (the pedal's pitch) / +0.060, z -0.011 / +0.003.
