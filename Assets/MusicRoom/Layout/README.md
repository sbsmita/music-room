# MusicRoom.Layout (stage 3)

Owns `Assets/MusicRoom/Layout`: which instruments are in the room and where, clear of the eyes and of
each other (the placement solver), grab-to-move with the guards that keep a playing pinch from ever grabbing,
the grab feedback, the desktop Shift / Alt rig drag, the seat swivel, the two-hand summon, the page-turn sweep
and its animation, the first arrangement, the mixed-reality layout calls (placeAroundPiano, restoreHomes,
anchor, the placing pinch) and the persistence of mode / current instrument / lock.

Rebased on the revised foundation (ARCHITECTURE.md section 21, review round 1): `App.IsPresenting` is the XR gate,
`IDrumsApi.TryPickUp` and `IMixedReality.StartPlacing` are called directly, `Rig.Handles` is read as the list its module
filled once, and the PlayMode tests boot through `TestBoot`. `CONTRACT-REQUESTS.md` lists the few changes still wanted
outside this folder (none blocks the merge).

Spec: `05-instruments.md` sections 7-8, `07-world-visuals.md` section 8, `08-ux-lessons-menu-guide.md`
section 5, ARCHITECTURE.md sections 4, 8 and 15.5.

## The Layout for eight instruments (phase 0, 12-new-instruments/platform.md 4, 9.2-9.6)

The room holds the classic three and five more instruments (the phase-0 stubs until their agents land). Layout reads
the registry (`App.Registry`) and names no instrument except the allowlisted piano key space and the piano's mixed
reality placing rules (`RigId.Piano` in `LayoutModule.Grab.cs`, `PlaceAt`'s `movePiano` / height rule, `RestoreHomes`),
plus `RigId.Table` / `RigId.Lessons`.

| What | Where | Details |
|---|---|---|
| Seat planner | `SeatPlanner.cs` | Follows the Node reference planner (seat-planner.mjs + geometry.mjs) line for line (double precision, the same loop order and candidate count): `PlanSet` (the side table at home, else its alternate behind-left; a set of one = its Single home), `CostOf`, `Join` (add, else replace the least recently played member that makes room, ties to the smaller instrument, else alone), `AllSets`, `PlanMixedReality` (rear slots +100, then without them, else the last member put away). Descriptor floats are read as the decimals they were written as (6 places), so the live planner decides as the reference does. Runs only on set changes (worst set 0.15 ms in the editor) |
| Display | `StowPlan.cs`, `LayoutModule.Set.cs` | Each instrument's spot from its descriptor (`Stow`), the spares (back-wall-left on the floor, the coffee table's top at 0.45 m), the 3 cm rule against the at-seat boxes (the seat frame follows the head), `[Layout] display blocked: <id>` when every place is taken (shown again when the seat moves), the pick point and the 6 degree cone pick. Out of My set an instrument with a display configuration (a descriptor spot and an `IStowable`) stands at its place: rig active, `Rig.InScene` false, `SetStowed(true, mount)`, its zone emptied, never ticked. Without an `IStowable` (the classic three until workstream R adds theirs) it is hidden as today. Mixed reality hides them all. Furniture: `IRoomFurniture.FloorBlockers`, else stow-plan.mjs's reference blockers |
| My set | `LayoutModule.Set.cs` | `MySet` (planner order, `musicroom.set`; the default `piano,drums,harp` is today's room at exactly today's homes), `SetVersion`, `CanAdd`, `PreviewJoin`, `ReplaceCandidate`, `TrySetSet`, `Bring(kind, reason)` (One alone: the page turn to it; My set: join(); reason Menu refuses Alone), `PutAway` (not the last member), `PlaceOf`, `SlotOf`, `Facing`, `IsMoving`, `AnyMoving`, `Lead`, `TryGetDisplayPoint`, `PlaceAroundLead` (`PlaceAroundPiano` calls it). A member at its canonical slot stands exactly at `Rig.HomeAll`; another at its slot's pose. A set that no longer fits at load is re-planned from its first member (each next member joins when join() can add it). Signals (`InstrumentSignalEvent`): `layout.bring.<reason>`, `layout.replaced` (the member sent back), `layout.putaway`, `{Table} layout.single` |
| Glides | `RigGlide.cs` | Lift 5 cm (0.15 s), an arc about the head at 0.75 m or more (straight out first, straight in last, never inside the head's 0.6 m sphere), round the side that stays inside the walls (held 0.35 m in), descend (0.15 s); yaw with the travel, ease in-out on arc length; 0.9 s + 0.1 s per metre beyond 2 m, at most 1.4 s; the outgoing one leaves 0.15 s first. While moving: not present, no zone, `PlaceOf` Gliding, tools released first, one swish per change. `InstrumentMoveEvent` Leaving at departure, Arrived on arrival (after `SetStowed(false)`, `InScene`, the zone), Displayed at the end of a put-away. Mixed reality, and a rig without a display configuration, slide (today's 0.35 s page turn). A change is applied at once by the tested placement code, then every rig that changed place travels from where it was (`Animate`) |
| Page turn | `LayoutModule.cs` / `.Set.cs` | The carousel of eight (`Registry.Next`). In the VR room the incoming instrument glides from its display spot (`TurnEvent` start / end kept, `IsTurning` for the whole glide); otherwise today's slide |
| Ray summon | `RaySummon.cs` | The system pointer pose and the cone pick over the displayed instruments; after 0.25 s a cream card with the name (TextMeshPro, dark ink, 8 cm, 12 cm above the pick point, facing the head) and a 3 mm pale ray (#f3e6cf, alpha .35); the grab pinch held 0.35 s brings it (`Bring(kind, Ray)`), the target and the member that would make room lifting 2 cm meanwhile; an early release cancels. The summon owns its pinch (no grab, stick pick-up, swivel or two-hand summon). Guards: presenting, not MR, not arrange mode, no rig held, not within 1 s of playing, not the system gesture, not with the index tip in a play zone or the menu zone (`InPlayZone`). One mesh (card + ray) and the text: 2 draws. Desktop: a left click on a displayed instrument's click target (or its displayed boxes) brings it |
| Head rule (2026-11-10) | `LayoutModule.Gestures.cs` | Nothing moves, turns or switches an instrument because the head moved. A recentre lays the room out about the tracking origin the system set (`ReadOrigin`; a recentre whose origin is not within 0.5 m under the head moves nothing), so a burst of recentre events lands in one place. The page-turn sweep is measured in the seat's frame, not the head's. Reset view (`BringBack`): the menu's Reset view row (`ILayoutService.BringToPlayer`), both hands pinched and held still 1 s in empty air (a gold ring fills round them; Move instruments on or off), or a recentre: one placement in front of the player, then nothing follows the head; that seat is the player's front every later instrument change lands about (2026-11-17, below) |
| Focus | `LayoutModule.Set.cs` (`TickFocus`, Swivel phase) | `Facing`: the at-seat member whose slot heading is within 30 degrees of the head's flattened forward, held to 40 degrees, a change after 0.3 s; One alone: Current. `FocusEvent` on change |
| Tools | `LayoutModule.Grab.cs`, `.Set.cs` | The grab pass asks every `IHandToolProvider` for `NearestFree(tip)` and calls `TryPickUp` on the nearest only (Near / Grabbed and the 2 cm handle rule as before); while no provider owns the kit, `IDrumsApi.TryPickUp` as today. An instrument leaving the seat has its providers' `ReleaseAll` (the kit: `ReleaseAllSticks`, as today) |
| Arrange mode | `LayoutModule.ArrangeMode.cs`, `LayoutModule.DisplayArrange.cs` | Lift ranges from the descriptor (`Seat.LiftMin` / `LiftMax` about the home; both 0: on the floor). In the VR room the displayed instruments move too (ray grab, or the cone pick past the seat's instruments): floor ones slide on the floor, wall ones along their wall, surface ones stay on their surface; inside the room, 3 cm clear of the furniture, the other displayed instruments and the seat's instruments, the play centre 1.1 m or more from the head; saved as `musicroom.stow.<id>` ("x,z,yaw"); Reset brings back the plan's spots. `ArrangeVisuals.Slots` 10 |
| Persistence | `ArrangementCodec.cs` | `musicroom.arrange.all` merges on save (`Merge`: entries of rigs not at the seat are kept); a saved pose that now collides (footprints overlapping by more than their 3 cm tolerance) is dropped for the planner's (`Usable`); the side table's saved pose is not used while it is at its alternate |

Tests: EditMode `SeatPlannerTests`, `JoinTests`, `CarouselTests`, `StowPlanTests`, `SeatAwayTests`, `RaySummonPickTests`
(on `Reference~/platform.vectors.json`, read by `PlatformVectors.cs`), `ArrangementCodecTests` (merge on save, collide
and drop); PlayMode `SetLayoutTests`, `GlideTests` (+ `GlideLessonTests`), `RaySummonTests` (+ `DesktopSummonTests`),
`FocusTests`, `DisplayArrangeTests`, `ToolProviderTests`, `StowPlanPlayTests` (skipped until a module registers
`IRoomFurniture`), Graphics `LayoutShotsTests` (Builds/Shots/platform/layout). `LayoutModeTests` and `PageTurnSoundTests`
follow the carousel of eight.

## Always in front, within a foot, drag and drop by a handle (2026-11-02)

After a headset session where the instruments "opened anywhere" (a remembered pose or anchor from an earlier visit) and
"Move instruments" was never found:

| What | Where | Details |
|---|---|---|
| In front on every open | `LayoutModule.Arrange.cs` `PlaceAt` | Every arrangement (the first presenting frame, the start panel's "Play here", a page turn, Bring / My set changes, recentre, the two-hand summon, entering or leaving "Play in my room", Reset) places each instrument at its default pose about the head's position and heading at that moment: its single home, or its My set slot (the main one straight ahead, the others at their slots), standing on the floor (the real floor in mixed reality, `IMixedReality.FloorHeight`). Nothing keeps an earlier height or place: the piano's height is its home height plus the player's comfort offset |
| The reach band | `LayoutModule.Reach.cs` | The default pose is pulled straight toward the player until the near edge of the playing surface (the floor rectangles of the played parts, `RigEntry.PlayRects`) is at most `ReachTarget` 0.27 m (band `ReachBand` 0.30 m, one foot) from the chest point (the head on the floor, `ChestBack` 0.10 m behind it). Then the resolver keeps everything clear of the eyes (0.30 m), the seat and each other. `ReachOf(rig)`, `MeasureReach(list)`. The PlayMode tests built on the frozen reference layouts (`LayoutPlayTestBase`) boot with `ReachBandDefault = false` |
| Comfort offsets | `ComfortCodec.cs`, `LayoutModule.ArrangeMode.cs` | A drag (handle or Move instruments) stores, per instrument and layout, its offset from the default pose: distance (along the head-to-play-centre bearing), side, height, yaw (`musicroom.comfort.all`, `musicroom.comfort.single.<id>`, text "c1;Piano:d,s,h,yaw"). Clamped to closer 0.3 / farther 0.6 / side 0.6 (the main one 0.3) / height 0.45 / yaw 90. Reset deletes it. In mixed reality too, except a piano the player put on a real table |
| Old saves | `LoadOffsets` | An old seat-frame placement (`musicroom.arrange.*`, "a1;...") is turned into offsets the first time its layout is placed and deleted; an entry outside the comfortable range is dropped (`Migrations`, `[Layout] old placement of ...: n kept, m dropped`). Display spots (`musicroom.stow.*`) are unchanged |
| Grab handles (retired 2026-11-10) | - | The small gold knobs are gone: instruments move only with the "Move instruments" setting (below). |

## A recording brings its instruments (2026-11-11)

Headset test: a take of the piano and the drums, played back with only the drums in front, played no piano ("either bring
the single instrument playing or bring all the instruments and let recording play"). `LayoutModule.Stage.cs`:

| What | Details |
|---|---|
| The trigger | Lessons' `Recorder` publishes `RecordingPlaybackEvent` (Core) when the player starts a recording playing back (the deck's PLAY of a take or of its loop, a take or a loop from Recordings) with `Kinds` (one bit per instrument it plays), and its end (its end, STOP, CLEAR, another playback replacing it). A loop session being recorded (LOOP) is never one |
| The stage | Every instrument of the recording that can stand at the seat, in planner order, as many as fit together (My set's rule, `PlanSet(...).Fits`, at most 3): one comes as One alone, several as My set (in mixed reality `PlanForRoom` as usual). Applied with the tested placement code (`Animate`: glides from the display spots in the VR room, slides in mixed reality) about the SEAT FRAME the player's layout was laid out about (`_seatHead`, `_seatYaw`), never the head's pose at the PLAY (the player looks at the deck or the menu then). Nothing is saved while it stands (`Persist` / `PersistSet` return while `PlaybackStaged`). Nothing moves when everything it plays is in front already, while Move instruments is on, while a lesson runs or MR places. Instruments it brings arrive with `BringReason.Take` (no first-arrival introduction from Sunny) |
| The end | `StageEndGrace` 1 s after the end (a replay meanwhile keeps them) the player's own layout comes back exactly: mode, instrument, My set, its plan, the seat frame, the side table's alternate, every at-seat rig's pose (heights, risers, Move-instruments spots), gliding back |
| Kept | The player changing the layout keeps it (and from then on it is theirs, saved): `Set` (with their own My set: One alone / My set mean theirs, not the recording's), `Next`, `Bring`, `PutAway`, `TrySetSet`, Reset view and recentres (`BringBack`), Move instruments (`StartMoving`), mixed reality on / off, `PlaceAroundLead`, `RestoreHomes`; at the end, any at-seat rig more than `StageMoveEps` 2 cm / `StageYawEps` 2 degrees from where the stage left it (a Move instruments drag, a level change on the headset's return, a swivel). REC or LOOP while it stands: the instruments stay for the recording |
| Loops | A looping playback keeps them until it stops; an overdub onto it (REC / LOOP arms a layer, the loop goes on) too |
| Sound | Every instrument of a recording sounds whatever the layout: Audio's `AudioPresence` parks the sources of a hidden instrument (mixed reality off the seat, a blocked display spot) on the audio host where they were, active, and puts them back under it when it shows (before, they went inactive with its group: silent) |
| Sunny | `tip_play_brings` "Your recording brings its instruments to you." the first time (asked 0.5 s after, again every 4 s while busy) |

Tests: PlayMode `Integration/PlaybackLayoutTests` (8 with the MR one: the old behaviour, the two-instrument take from One
alone and back, a one-instrument take from My set and back, the player's changes kept, a loop and an overdub, nothing
moves when all are in front, Sunny, zero GC on Layout's frames), `MR/MrPlaybackLayoutTests`; EditMode
`Lessons/RecordingPlaybackEventTests`.

## One front spot for a single instrument (2026-11-17)

Headset test: "when instrument changed or presses loop and changed the instruments appear here and there. it appears within
one foot but sometimes left or right. make it appear at once place in front of eyes only".

| What | Where | Details |
|---|---|---|
| The player's front | `LayoutModule.Arrange.cs` (`KeepFront`, `ReadFront`, `Front`) | The seat an arrangement about the head or the origin lays the room out about (the first presenting frame, "Play here", mixed reality on / off, Reset view by the menu or both hands, a recentre, Move instruments' Reset) is kept as the player's front, in the tracking space (a swivel, fitSeat or anything that moves the rig carries it). Every instrument chosen (menu tiles, One alone, the ray, look and pinch, Sunny: `Bring`), brought by the deck's LOOP / Sunny's deck guide (`Set`), turned to (`Next`), or staged by a playback (`Stage`) is placed about it, never about where the head looks or leans at that moment (before: `ReadSeat` at the change, so a look at the menu or the deck put it up to a foot to the side). Its eyes' height is the player's settled eye level (`EyeHeight`). A head `FrontWalk` 0.5 m or more from the front's seat point has walked away: the next bring takes the head's pose as the new front (`FrontReseats`, `[Layout] front: ...`). Off the headset (desktop) the head as before |
| Centred | `LayoutModule.Reach.cs` (`SingleHome`, `CentreOnFrontLine`) | One alone's default pose (and a My set of one): the single home moved sideways so the play centre (the piano's mid key depth, the kit's mean piece, the harp's middle string, the descriptor's `PlayCentre` for the others) is on the forward line, then pulled into the reach band straight toward the chest. The singing bowls' single home was their My set left slot (46 cm left of the line), the harp's middle string 2.6 cm left. With the reach band only (the frozen reference layouts keep the homes as built) |
| Only a deliberate move is saved | `LayoutModule.ArrangeMode.cs` (`SaveArrangement`, `_movedByPlayer`, `NoteMovedByPlayer`) | Move instruments saves the spot of the rigs the player picked up and put down in that session only; turning it on and off without a drag saved every instrument's automatic pose (the resolver's push included) as if it were the player's. The player's own spots (comfort offsets) are kept and applied as before |
| Several at once | `LayoutModule.Stage.cs` | A playback of 2-3 instruments keeps My set's arrangement about the same front; one instrument alone lands at its One alone spot |

Tests: PlayMode `Integration/FrontSpotTests` (every instrument chosen / brought by the deck guide's Set / page turn / a
one-instrument playback and back, after recentres at 0, 35, -60 and 120 degrees with the head looking away and leaning at
each change: the play centre within 2 cm of the forward line, facing the player, the same distance each time; Move
instruments on and off saves nothing; a walked-away player), `MR/MrFrontSpotTests` (the same in a recorded room).

## "Move instruments": one On / Off setting (2026-11-10)

The user asked for pinch-and-drag-and-drop with a full 360 degree turn, no gold knob, and one on/off switch in the
settings. The knobs (`LayoutModule.Handles.cs`, `HandleVisuals.cs`, Core `IGrabHandles` / `HandleConstants` / handle
events), the one-shot arrange mode entry and "Lock instruments" are replaced by ONE setting:

| What | Where | Details |
|---|---|---|
| The setting | `LayoutModule.Move.cs`, Core `Contracts/Arrange.cs`, `AppSettings.MoveInstruments` (`musicroom.moveInstruments`) | Off by default. `IArrangeMode.MoveOn` is the player's choice (persisted); `Arranging` says whether it is in effect now. `BeginArrange` / `EndArrange` / `ToggleArrange` switch the setting (the menu's home row "Move instruments", the Instruments screen's "Move" button, the floating Done button). The app suspends moving by itself (`StopMoving`, setting unchanged) while the headset is off, when mixed reality changes the room, while an instrument is brought / put away, and before the start panel is answered; `TryResumeMove` turns it back on once the player is in the room, the headset is on, nothing glides and MR is not placing (`MoveResumes`). Exit leaves it as it is |
| Migration | `ComfortCodec.MigrateMoveSetting` | An old `musicroom.locked` is deleted on boot; Move instruments stays off (what Lock on meant) unless the player turned it on |
| Off | `LayoutModule.Grab.cs` | No pinch moves an instrument (`InPlayGrab` off); a still pinch on a body publishes `InstrumentBodyPinchEvent` (Sunny's `tip_move_turn_on`). The menu panel still moves by its zone |
| On | `LayoutModule.ArrangeMode.cs` | The instruments are quiet (`App.TickInstruments`), outlined (`ArrangeVisuals`: soft warm brackets and floor ring; light gold on the one a hand is near or points at; gold while held), the floating Done / Reset buttons show. A pinch within 8 cm of any part (or a hand's ray) picks one up after 80 ms |
| The drag | `LayoutModule.Move.cs` | The pinch point (or the ray's far point) through `DragFilter` (One Euro, 2.5 Hz + 10 Hz per m/s; reset on the hand's own point, so no jump and the grab offset is kept) carries the footprint centre in x and z, yaw only; the hand's own height (its own One Euro filter; once it has moved 2 cm, `MoveConstants.LiftDeadband`, so a level drag never bobs) lifts or lowers it 1:1 inside its range (2026-11-11; a ray grab lifts with the hand, never the ray's far end; raised, it stands on a riser, `RiserVisuals`, the piano on its stretched stand). The wrist's unwrapped heading, filtered, past a soft 6 degree dead band spins it `TurnGain` 2 x about its own centre (a 90 degree wrist turn = about 170 degrees; a few grabs go all the way round; comfort offsets keep any yaw, `ComfortCodec.MaxYaw` 180). Two hands on it twist it 1:1 about its centre, carried by their filtered midpoint; when one lets go the other carries on re-anchored. Hold hysteresis: holds while the grab pinch is on, the thumb-index gap is under 4.5 cm or Meta's strength is 0.3 or more; lets go only after 0.12 s off (`MoveConstants.ReleaseGrace`); a tracking gap up to 0.3 s (`LostGrace`) waits and re-anchors. Constraint / room clamp as every grab |
| The drop | `LayoutModule.Grab.cs` `EndGrab`, `LayoutModule.Move.cs` | `SettleOne(offSeat: true)`, the comfort offset saved (`SaveArrangement`), then a 0.25 s ease-out from where the hand let go to the free spot (`StartSettle` / `TickSettle`; quiet until it arrives) |
| Why not the Interaction SDK | - | `com.meta.xr.sdk.interaction` is not in the project; the hands are the app's own pipeline (MetaHandSource / FakeHandSource / mouse); a `Grabbable` + `HandGrab` would need colliders, an ISDK rig and prefabs per instrument, its transformers would still have to be constrained to yaw on the floor, and it could not be tested headless |

Tests: EditMode `DragFilterTests` (6, the spin gain), `SettingsMigrationTests` (Lock removed), `CoreTests` (persistence),
`ComfortCodecTests`; PlayMode `Integration/MoveDragTests` (9: persistence / start panel / headset, Off moves nothing, On
plays no note, no jump / offset kept / jitter / follow / no lift, flicker / loose pinch / tracking gap, wrist spin past 360,
two-hand twist, eased drop, zero GC with a moving turning hand), `ArrangeModeTests`, `Integration/ArrangeIntegrationTests`,
`Integration/PlacementInFrontTests`.

Tests: EditMode `ComfortCodecTests` (5); PlayMode `Integration/PlacementInFrontTests` (6: every instrument alone and the
sets at three head positions and four yaws, the handle drag and its offset at another seat and Reset, drops into the
piano and onto the seat, pinches on keys / snare / strings and just after playing, old placements and an old anchor),
`MR/MrInFrontTests` (2), Graphics `Integration/PlacementShotsTests` (Builds/Shots/placement).

## Since the first headset test (2026-11-01, u-arrange): "Move instruments"

The user could not move any instrument on the Quest with the in-play grab, so Layout now also implements
`IArrangeMode` (Core/Contracts/Arrange.cs; `LayoutModule.ArrangeMode.cs`, `ArrangeVisuals.cs`, `ArrangementCodec.cs`).
The full design record, the numbers and why, the in-play grab review and the merge notes are in `ARRANGE-NOTES.md`.

| What | Details |
|---|---|
| Entry / exit | `BeginArrange()` (the menu's "Move instruments" row; no-op while MR places the piano): any grab ends, the swivel / two-hand summon / page turn are off for the duration, sticks go back (`IDrumsApi.ReleaseAllSticks`), a running lesson stops, a take being played back stops, `ArrangeModeEvent{Active = true}`. `EndArrange()` (Done, "Done moving", the headset coming off): lets go, saves, `Input.ResetMotion()`, `ArrangeModeEvent{false}`. Entering or leaving MR ends it without saving. `ToggleArrange()`. Works whether or not `Locked` |
| Silence | Core's `App.TickInstruments` ticks no instrument but the menu while `Arranging` (no notes, no hover glows, no switches or deck); Drums turns its nod kick and stick glows off; Guide holds a tour in progress out of sight |
| Highlight | `ArrangeVisuals`: per arrangeable rig (piano, kit, harp, side table; not the menu, not a piano on a real table in MR) thin corner brackets (24 square-tube arms of 4 mm, 18 % of each box edge, 3.5-10 cm, round its parts' box in its own frame + 3 cm) and a floor ring (0.45-0.5 m scaled to the footprint), calm and warm to match the lamp-lit room (the user prefers realism to glowing UI; changed at the merge, was additive cyan #5fd8ff 7 mm): a soft warm white #fff1dc at alpha .42 (ring .16) while waiting, a light gold #f2cb84 at .8 while a hand's pinch point or ray is on it, gold #ffc46a at .92 while held, red #ff5a4a where it is pushed through or too close. The ray is warm white at .3, light gold at .7 on an instrument, with a gold dot. Alpha-blended (UnlitTransparent clones, ParticlesUnlitAlpha for the rays: painted, not added light), one clone per renderer, no MaterialPropertyBlock. Bracket meshes rewritten only when a box changes (the piano's stand). At most 8 draws plus 4 for the rays and dots |
| Near grab | The grab pinch (`HandState.GrabPinch*`) starting with the pinch point (thumb and index tips) within 8 cm (`NearReach`) of ANY part of an arrangeable rig (handles and played parts: keys, heads, strings) takes it after an 80 ms debounce (`GrabDebounce`; no stillness test). It follows the pinch point 1:1 in x and z |
| Ray grab | No rig near the hand, pointer data valid (`HandFrame.pointerValid` = InputStateValid) and not the system gesture: a ray from the hand's system pointer pose (OVRPlugin `PointerPose`) to the first part of an arrangeable rig it hits within 6 m (oriented boxes, as the desktop pick), drawn as a 4.5 mm beam with a dot where it lands (gold when it hits, a 1.2 m cyan beam when it does not) and lighting that rig gold. A pinch takes it after the debounce; it keeps the hit distance d0 along the current ray, and pushing or pulling the hand along the first ray moves it 2.5 x as far in the room (d = d0 + 1.5 x push, 0.2-6 m) |
| Height | Both grabs: the hand's own height (1:1 after a 2 cm dead band; for a ray grab not the far point), clamped by `LayoutModule.LiftRange` (the descriptors' `Seat.LiftMin / LiftMax`, 2026-11-11): piano key tops 0.55-1.10 m (Piano stretches its stand to the floor), kit 0..+0.35 m (snare 0.72-1.07 m), harp and marimba 0..+0.30, handpan and bowls 0..+0.40, hand drums 0..+0.35, each raised on a riser (`RiserVisuals`: a dark wood platform under its floor-standing parts plus a 4 cm lip, one draw, only while raised); never into the floor; the side table keeps its height |
| The player's level | `LayoutModule.Height.cs` | Every default height is the home plus the player lift (each instrument clamps it to its range). Reset view (menu row, two-hand hold) sets it for the eyes now (`EyeLift`: 0 for eyes 1.05-1.30 m above the floor, else the excess / shortfall, -0.30..+0.45) and forgets the layout's height tweaks (spots kept). The headset off / the system menu / a tracking loss is an "away": the eyes when it starts are compared with the eyes once back, worn, tracked and still for 0.5 s; a change of 3-35 cm moves everything by as much (the system's floor moved, or the player sits a little differently), more is a new posture (the lift for the eyes now). Recentre events while away or settling wait and are applied once, with a fresh head pose |
| Turn | The wrist's yaw (`WristYawTracker`, unwrapped); a second hand's grab pinch on the held rig (near it or in its zone, near grabs only) twists it about the pinch midpoint |
| Rules kept | `PlacementSolver.Constrain` while moving (stops and slides at contact, through after 300 ms with a red ring), `ClampRoom`, the floor ring while held (GrabVisuals), `SettleOne` on release (no overlap, 30 cm from the eyes, inside the walls) |
| Never a grab | A pinch that starts as the OS system gesture (`PointerInput.SystemGestureLike`: the SystemGestureInProgress flag, or the palm within 50 degrees of the eyes, under 0.8 m, turned back toward the player) or inside the shown menu's zone |
| Persistence | Every arrange-mode release and `EndArrange` save each placed rig of the layout relative to the seat it was arranged around (x right, z ahead of the head on the floor, y absolute, yaw relative; `ArrangementCodec`, text "a1;Piano:x,y,z,yaw;...") in `AppSettings` (`musicroom.arrange.all`, `.single.piano`, `.single.drums`, `.single.harp`). Every later arrangement of that layout (the next launch's first presenting frame, a recentre, the two-hand summon, `Set`, a page turn's incoming instrument, leaving MR) places those rigs there about the current seat, then the resolver. Never in MR. The seat frame turns with the swivel snap turn. In-play grabs are not saved (as before) |
| Reset | `ResetArrangement()`: the saved placement of the current layout is deleted and `ArrangeAroundPlayer(true)` lays it out at the homes, the piano at its home height. Arrange mode stays on |
| Desktop | While arranging, a plain left-drag moves an instrument (locked or not; a press on the menu or the buttons is theirs) |
| Extras | `Arranging`, `ArrangeView`, `HeldByRay`, `NearTarget(hand)`, `RayTarget(hand)`, `PinchIgnored(hand)`, `ArrangeGrabs`, `RayGrabs`, `ArrangeSaves`, `ArrangeResets`, `Arrangeable(rig)`, `NearestArrangeRig`, `RayArrangeRig`, `LiftRange(rig)` |

**The in-play grab** (pinch and hold) stays, with what the device test showed to be too strict made more forgiving:
it now reads the grab pinch (thumb-index OR Meta's index pinch, hysteresis 0.85 / 0.5) instead of the thumb-index rule
alone, its dwell allows 4 cm of wander (`GrabStill`, was 2.5 cm), and a pinch that starts as the system gesture never
arms it. See ARRANGE-NOTES.md for the analysis.

## Public contract

`LayoutModule` (Order 100, after every rig owner) registers:

| Contract | What it does |
|---|---|
| `IRigRegistry` | `Rigs`, `Get(RigId)`, `Register`, `Unregister`, `Changed`. Collects `app.GetAll<Rig>()` in `Init`; later registrations join. |
| `ILayoutService` | `Mode`, `Current`, `Locked` (persisted, raises `LayoutChangedEvent`), `IsTurning`, `FirstArrangeDone`, `Seat`, `Set(mode, inst)`, `Next(dir)`, `IsInScene(kind)`, `ArrangeAroundPlayer(force, movePiano = true)`, `PlaceAroundPiano()`, `RestoreHomes()`, `Anchor`. |
| `IGrabService` | `HeldRig`, `AnyHeld`, `GrabTarget(point, reach = .07)` (menu zone first, then the nearest grab handle). |
| `IPlacementService` | `Resolve(useRoomBox)`, `SettleOne(rig)`, `ClearPanel(panelRig)`, `RoomShift()` (measures only), `EyeDistance(rig)`. |

Events: `LayoutChangedEvent` (Set with a change, the end of a page turn, Locked), `TurnEvent` (Started at the start and
false at the end of a turn), `ArrangedEvent{Forced}` (every arrangement). Subscribes `RecenteredEvent` (a reset view about the tracking origin),
`MixedRealityEvent` (the piano returns to its stand height on the next arrangement), `HmdMountEvent{false}` (lets go,
drops the swivel). Phases: FirstArrange, Grab, Swivel, TwoHandSummon, PageTurn, LayoutAnimation.

Calls into the other modules: `IDrumsApi.TryPickUp` (sticks first in the Grab pass), `IDrumsApi.ReleaseAllSticks` (the kit
leaves), `ITutorial.Stop` (a lesson's instrument leaves), `IMixedReality.StartPlacing` (the placing pinch),
`IAudioEngine.Swish` (page turn), `IMenuPanel` (`Shown`, `GrabZone`, `Root`), `IPianoApi` / `IHarpApi` / `IDrumsApi` for the
play centres and the piano's key space.

Public extras on `LayoutModule` (tests and later use): `TurnProgress`, `FadeOf(rig)`, `Disarm(hand)`, `EmptyAirPinches`,
`XrArrangements`,
`DesktopStep(DesktopMouse)`, `PickRig(ray, out hit)`, `MouseHolding`, `MouseTurning`, `Placement` (the solver),
`Visuals` (grab feedback state), `Sweep` (the gesture), `ZoneOf(rig)`, `NearInstrument(p)`, `PlaySurfaceAt(p)`,
`GrabCandidate(p)`, `Grabbable(rig)`, `EndGrab()`, `LastSeatShift`, `Turns`, `SnapTurns`, `Summons`, `SwivelHand`,
`GrabHand` / `SecondHand` / `TurnerHand`, `PassingThrough`, `TurnRestIn/Out`, `TurnRight`.

What the other modules provide: each instrument / room / menu module registers its `Rig` (Group, homes, priority,
`Handles` filled once, its instruments, optional `Zone`). Layout moves `Rig.Group` in world space (yaw only), sets
`Rig.InScene` and toggles the group's active state for piano / drums / harp membership. The instruments' `IsPresent`
should read `Rig.InScene` (`CONTRACT-REQUESTS.md` 1). The menu's group is deactivated only while the MR placing pinch
hides the rigs, and only if the menu was open; it comes back with them.

## Behaviour

**Modes.** `All` (default): the piano in front, the kit to the right, the harp and side table to the left.
`Single`: the current instrument at its single home plus the side table and the menu; the others are inactive and out
of every list (`Rig.InScene = false`). Removing the kit calls `IDrumsApi.ReleaseAllSticks()`, a lesson whose instrument
left is stopped (`ITutorial.Stop()`), a held rig that leaves is let go. `Set` finishes a running turn, applies
membership, re-arranges around the player and publishes when something changed.

**Placement (place, arrangeAroundPlayer).** Every rig at its seat-frame home (`LayoutMath.SeatPlace`:
head on the floor + yaw-rotated (x, z), height = home.y, yaw = home + seat), the piano keeping its current height (a
lifted piano stays lifted; the very first placement and entering/leaving MR use its home height). In `Single` mode the
table and the menu keep their all-mode homes. Then the resolver (below) and the zones. `ArrangeAroundPlayer` also ends
a grab and a turn first, forgets the grab heights, resets the sweep and pointer motion and publishes `ArrangedEvent`.

**When the room is arranged (spec 08 section 5.4).** `Init` puts every rig at its home about ORIGIN
(0, 1.2, 0), as built. That is the desktop room: the app never arranges on the desktop, so with the
desktop camera at (0, 1.2, -0.1) the piano is 0.55 m ahead of it. On the desktop the first frame only sets
`FirstArrangeDone`. A single-instrument layout restored from the settings is a `Set(...)`, so it is
arranged about the camera (no fitSeat). In XR every session start (`App.IsPresenting` turning true)
arranges once, on its first presenting frame with the head above 0.2 m. That includes a
runtime that comes up after RigBootstrap's desktop fallback, which is arranged about the real head.

**`ArrangeAroundPlayer(force, movePiano)`.** `movePiano` (default true) moves the piano and allows
fitSeat; `force` is only reported in `ArrangedEvent.Forced`.

**fitSeat with the fix (VR room, while presenting, `movePiano`).** Dry run: every rig at its home, no settle; each
placed rig's eyes cleared in priority order with the walls off (`PlacementSolver.FitShift`); then the room shift of
that arrangement moves the player's rig (`XrMode.MoveRig`). The original fitSeat measured the shift without the eye clearance. For
single drums at the XR origin its dry run has the kit's back at source-frame z -1.207 (past the back wall), so the seat moves
back .089. The resolver then pushes the kit into the wall again for the eyes and back out for the wall, which leaves it
.29 m (.237 at head y 1.1) from the eyes, so findSpot slides it sideways (.075 m; the resolver about the unmoved head
slides it .725 m). With the fix the seat moves back 11.9 cm and the kit stays centred (tests reproduce every step with
the reference rigs).

**Resolver (`PlacementSolver`).** Each placement rig (everything but the menu) is measured from its
renderers as oriented boxes in the rig frame (mesh bounds carried by `rig.worldToLocal * renderer.localToWorld`); its
floor footprint is each box's x/z extent with the rectangles inside another dropped, and their bound. Measured parts =
`Rig.Handles` plus the `PlayedParts` of the rig's instruments (not every visible mesh; the cymbals,
keys and strings are played parts and matter for the eye clearance), renderers still under the group only (so a stick
the Drums module reparented into a hand is left out). In priority order (`Rig.Priority`: piano 0, drums 1, harp 2, table 3): `clearEye` (straight back
from the player along the play-centre direction by the shortfall + 2 cm, up to six times), `clearWalls` (VR room only,
least shift), and if still not free `findSpot` (away from what it touches, centre to centre, away from the player, 16
compass directions, 2 cm steps up to 1.6 m, never onto the seat rectangle, then five bisections). Finally the menu panel
is cleared out of the instruments (toward the player, never closer than 30 cm, or lifted, up to 45 cm, facing the
player with +z away). Drags: `Constrain` (a rig already touching moves freely; a turn into contact is refused; a move
marches in 1 cm steps, bisects six times and slides full / tangent / x / z keeping the best gain). `SettleOne` moves a
released rig out of another, the eyes or a wall on its own. Every working list is a field: a resolve allocates nothing.

**Mixed reality (ARCHITECTURE.md section 12).** While `IMixedReality.Active`: no fitSeat, no room box,
no swivel. `PlaceAroundPiano()`: every rig of the layout at its VR-room offset from the piano (turned with it, at its
home height, shown again), then settled with the piano pinned. `RestoreHomes()`: the piano at its home about the
default seat (0, 1.2, 0) facing +z, the layout placed about that seat (in the full room the piano stays; in single mode
the current instrument moves) and settled with the walls. `Anchor`: the piano's pose in the full room, the current
instrument's in single mode. The placing pinch (step 5 below) hides every other rig group (the menu's too, if open; the
menu stays `Shown`: only the panel's group is hidden) and calls `IMixedReality.StartPlacing()`. While
`IsPlacing` the Grab pass returns at once (MR clears it in the landing frame's EndFrame, so that frame is skipped too),
and the summon and page turn pause. `PlaceAroundPiano`, `ArrangeAroundPlayer` and `RestoreHomes` show the menu again, and
the settle clears it out of the instruments.

**Grab.** Only while `App.IsPresenting`, not while MR is placing.
Per hand, sticks first: a `PinchStart` (or a `GraspStart` when not playing) with pointer 0 active, the hand neither
grabbing nor holding, asks `IDrumsApi.TryPickUp(hand, tip)`; `Grabbed`, or `Near` with no grab handle within 2 cm, disarms
the hand for this pinch. `PinchStart`: armed when the hand has not played for 1 s (`HandState.LastPlayMs`), the dwell starts on the pinch point
(pointer 0, or the raw index tip while holding a stick), the candidate (drives the dwell ring) is the grab target unless
the point is over a play surface (`IInstrument.PlaySurfaceAt` of the instruments in the scene, the menu excepted), in the
piano's key space, or locked. Disarmed when the pinch ends, the hand plays, or the smoothed point wanders more than
2.5 cm (EMA 60 ms against the mean of the first 80 ms). At 0.3 s (one shot): smoothed speed over the last 0.1 s at
most 0.5 m/s, no play surface, the piano rule, unlocked (the menu always) -> `StartGrab`. The rig then follows the hand
and turns with the unwrapped wrist heading about its footprint's centre (no limit), lifts within -0.45..+0.6 m of its
height at the first grab after a 3 cm dead band, its origin clamped to x +-2.8, z -3.6..0.8 in the VR room, constrained
against the other rigs until the hand has pushed 300 ms (`UnstickMs`), then passing through with a red ring until clear.
A second hand pinching inside the held rig's zone twists it (pair heading, unwrapped, about the pinch midpoint); a
second hand pinching in the air beside it turns it 45 degrees per 12 cm sideways pull (right = clockwise from above).
Let go: `SettleOne` (the menu: `ClearPanel`). Tracking loss lets go. Step 5: a dwell-complete pinch with no rig
under it, in mixed reality, the piano in the layout and the point outside every zone, asks to place the piano again. `Rig.Held` is set while held, so the Core skips
that rig's instruments. Zones = the world box of every renderer of the rig + 12 cm, plus the
module's `Rig.Zone` and the shown menu's `GrabZone` for "outside every zone".

**Desktop Shift / Alt drag.** Only while not presenting (the editor's mouse hand), in the Grab
phase from `DesktopInput` and the head camera's mouse ray (`DesktopStep` takes a scripted `DesktopMouse` in tests).
A left press with Shift or Alt picks the nearest grabbable rig whose shown parts (handles and played parts, as oriented
boxes; the menu by its renderers while shown) the ray hits; locked, only the menu. Shift: the rig follows the ray on
the horizontal plane at the hit height (no lift, no turn). Alt (wins over Shift): yaw = yaw0 - dx 0.012 rad per pixel
(added in the right-handed source frame) about the footprint's centre. Both go through the same grab (constraint, unstick,
red ring, rings and cage, `Rig.Held`); releasing the button lets go and settles. While Shift / Alt is held or a mouse
drag runs the camera does not look around: Shift / Alt through `DesktopRigFallback.DragBlocked` itself, the drag
through Layout's test chained into `dragBlocked` (restored in Shutdown).

**Feedback (`GrabVisuals`).** Floor ring 0.42-0.5 m at y .006 under the footprint centre, scaled
`clamp(hypot(hu, hv) .85, .3, 1.1) / .5`, additive #ffc46a alpha .5 (red #ff5a4a alpha .6 passing through); the dwell
ring (0.43-0.5 m, 64 segments filled from +x toward +z as k = PinchFor / 0.3 grows from 0.05 s, alpha .25 + .45k);
the edge cage (parts box in the rig frame + 2 cm, alpha .12 + .4k during the dwell, .3 held, .45 red). Each renderer has its
own clone of the foundation `UnlitAdditive` template, and its colour and alpha are set on that clone. There are no
MaterialPropertyBlocks (ARCHITECTURE.md section 14: they take a URP Unlit renderer out of the SRP Batcher). At most 3
draws, only during a dwell or a grab. No meshes when headless (the state is still tracked for tests). `Destroy` (module
Shutdown) frees the three procedural meshes with the root.

**Swivel.** VR room only, while presenting: a grab-pinch start (`HandState.GrabPinchStart`: the thumb-index rule OR Meta's
own index pinch, since the merge of u-arrange; the rule alone flickers on the headset, where the two tip joints sit ~17 mm
apart when the fingertips touch) with a tracked index tip, no play for 1 s, no rig held, outside every zone and not made
like the OS system gesture (`PointerInput.SystemGestureLike`) takes the swivel; every 14 cm of head-local x snaps the
seat 45 degrees about the head (`XrMode.SnapTurn(-45)` for a pull to the right: like dragging the world) and resets
pointer motion; ends when the grab pinch parts or on tracking loss.

**Two-hand summon.** While presenting, no rig held (in MR not while placing and only with the piano in the layout): two hands
with a grab pinch (`HandState.GrabPinching`), holding no stick, not playing, not making the OS system gesture, index tips
outside every zone, for 0.8 s, once: drop the swivel, `ArrangeAroundPlayer(true)`. Re-arms when fewer than two hands
count. Tests of the grab pinch on both: `Tests/PlayMode/Layout/GrabPinchGestureTests` (3).

**Page turn.** The sweep (spec 08 section 5.2): an open (grasp >= .15), vertical-palmed (|n.up| <= .6), idle (not
pinching, no stick, not played for 400 ms, nothing held, no swivel, not placing) hand's palm centre in the head's floor
frame, 16-sample ring, jump > .4 m resets; fires when, walking back within 0.3 s, the first sample 0.15 m across gives
>= 1.2 m/s and |dx| >= 1.5 |dy|, 1.5 |dz|, outside the 0.6 s cooldown, once per frame; left = next. Re-arms below
0.5 m/s. `Next` acts only in single mode with no turn running: the incoming instrument's rest pose is its single home
about the seat as it is NOW, settled with the outgoing one left out (the zones of the rigs the settle moved, such as the
side table, are refreshed); 0.35 s easeInOutQuad: outgoing `rest - dir 0.9 e right`, scale `1 - .15e`; incoming
`rest + dir 0.9 (1 - e) right`, scale `.85 + .15e`; `Swish()` at the start, `TurnEvent` at both ends, membership and
`LayoutChangedEvent` at the end. While it slides, the incoming rig is drawn but out of the layout (`Rig.InScene` false: not
ticked by Core when its `IsPresent` honours it, no grab target, no zone, not "near"): its group is only added to
the scene until the turn finishes; the outgoing one stays live until the end.

## Constants

All from `GrabConstants` (Core): PLAY_GUARD 1000 ms, dwell .3 s, still .04 m (.025 until the device test), speed .5 m/s, smoothing
.06 s, speed window .1 s, hint after .05 s, 48 dwell samples, handle reach .07 m, zone pad .12 m, unstick 300 ms, lift
-.45 / +.6 / deadband .03, off-hand pull .12 m / 45 degrees, stick near-miss handle reach .02 m, desktop turn .012
rad per pixel; EYE_CLEAR .30, EYE_MARGIN .02, GAP .01, WALL_SLOP .005
(+ .002 margin), STEP .02, MAX_PUSH 1.6, seat half .2 / front .1 / back .3, panel min eye .3 / max rise .45; swivel
.14 m / 45 degrees; summon .8 s; turn .35 s / slide .9 / scale .85; sweep .15 m, 1.2 m/s, .3 s window, ratio 1.5, palm
vertical .6, open hand .15, play guard 400 ms, cooldown .6 s, re-arm .5 m/s, jump .4 m, 16 samples. Room box (world, the room
facing the fire: Core's `RoomFrame`, 2026-11-11) x +-3.2, z -1.62..3.58; origin clamp x +-2.8, z -1.3..3.1 (the room
frame's z -4.08..1.12 and -3.6..0.8 turned). The display spots, spares, reference furniture and saved stows
(`musicroom.stow.<id>`) are room-frame values that `StowPlan.Spot.From` / `MovedToRoom` / `At` turn into the world; a
moved display is saved back in room coordinates (`Spot.RoomPose`). Homes: ARCHITECTURE.md section 8 (from each `Rig`). Play centres:
piano (0, 0, L/2), kit mean piece position (pad yes, kick shell no), middle harp string's midpoint, table deck
(0.07, 0.6, 0).

## Files

`LayoutModule.cs` (registry, modes, membership, page turn, phases), `LayoutModule.Arrange.cs` (place, fitSeat,
placeAroundPiano, restoreHomes, anchor, collect parts / centres / zones, IPlacementService), `LayoutModule.Grab.cs`
(sticks first, guards, dwell, drag, twist, step turn, release, the placing pinch, feedback), `LayoutModule.Desktop.cs`
(Shift / Alt mouse drag, ray pick, `DesktopMouse`), `LayoutModule.Gestures.cs` (swivel, summon, sweep hook-up), `PlacementSolver.cs` (the placement solver),
`Geometry.cs` (`Obb` + SAT + ray slab test, floor rectangles, `LayoutMath`), `HandTrackers.cs` (`DwellTracker`, `WristYawTracker`,
`PageTurnGesture`), `GrabVisuals.cs`, `CONTRACT-REQUESTS.md`; since the device test `LayoutModule.ArrangeMode.cs`
(IArrangeMode), `ArrangeVisuals.cs` (brackets, rings, rays), `ArrangementCodec.cs` (the saved placement),
`ARRANGE-NOTES.md` (design record and the ARCHITECTURE section 21 draft).

## Tests

Added after the first headset test: EditMode `ArrangementCodecTests` (4: seat-frame round trips, text and damaged
text, the settings keys per layout, the height ranges and constants); PlayMode `ArrangeModeTests` (13, reference rigs, 72 Hz:
entry / exit, silence, highlights, events, sticks and lesson; near grabs on keys, a head and a string after the
debounce, 1:1; the ray: target, beam, gold, the grab, x 2.5 pull, aiming; heights; wrist and two-hand turns; no overlap
and the settle; persistence across a reboot of the test app at another seat, then Reset; each layout its own placement;
lock; Meta's pinch alone with hysteresis, in arrange mode and for the in-play dwell; the system gesture never grabs; no
swivel / summon / page turn meanwhile; the arrange frame allocates nothing). `ConstantsAreTheReferenceValues` checks GrabStill
.04 beside the pre-device-test .025. End to end with the real menu and piano: `Tests/PlayMode/Integration/ArrangeIntegrationTests`.

The reference data is the frozen layout golden `Tests/EditMode/Layout/Reference~/layout-golden.json`: every placement
rig's measured boxes, the play-zone data, resolver outcomes for 16 seats (with the original fitSeat shift and the fixed
one), constrain / settle / clearPanel cases, grab targets and a page-turn rest pose; `gen_fixture.py` converts it to
Unity coordinates as `GoldenLayoutFixture.cs` (EditMode and PlayMode copies). The tests rebuild the four reference rigs
from those boxes.

EditMode (`Tests/EditMode/Layout`, 65 cases):
- `PlacementTests`: `MeasureMatchesTheGoldenFootprints`; `ResolveMatchesTheGoldenForEverySeat` (16 seats incl. walls,
  corners, swivelled seats, MR: every pose, eye distance and overlap count as the golden); `AtTheOriginSeatTheFullRoom
  NeedsNoMoves` (.628 / .529 / .74 / .603); `FitSeatShiftsMatchTheGoldenAndTheFix` (14 seats: the original shift, the fixed
  shift and the arrangement after it); `SingleModeDrumsStaysCentredAfterTheFitSeatFix`; `ConstrainSlidesAlongThePiano
  LikeTheGolden` (25-step path), `ConstrainStopsABigMoveAtContact`, `ConstrainRefusesATurnIntoContact`,
  `ConstrainLetsAnOverlappingRigMoveFreely`; `SingleModeDrumsAtTheSpecHeights` (15.5's numbers at head heights 1.2 / 1.1
  / 1.3, each step: the dry run's kit back at source-frame z -1.207, the original seat shift -.089, the eye distance .29 / .237 /
  .363 after clearEye + clearWalls about the moved seat, findSpot's slide there, the .725 jump about the unmoved head,
  and the fixed seat moving straight back with the kit centred); `SettleOneMovesTheReleasedRigOutLikeTheGolden` (kit in the
  piano, harp in the face, table in the wall); `ClearPanelLiftsOrPullsThePanelOutLikeTheGolden`;
  `PageTurnRestPoseAfterASwivelMatchesTheGolden`; `APartMovedOutOfItsRigIsNotMeasured` (a held stick).
- `LayoutMathTests`: OBB distance / extents / SAT / ray slab test (face, edge-cross and turned cases), floor-rectangle overlap and
  world transform, wall shift, seat rectangle, easing and wrap, the constants, `AStillButJitteryPinchPassesTheDwell`,
  `ADwellRejectsAMovingPinch`, `DwellSettlesOnTheMeanOfTheFirst80Ms`, wrist yaw full turn and axis switch.
- `PageTurnGestureTests`: slow / short / fast left / right sweeps, the spec's 0.2 m in 0.12 s,
  cooldown, pinching / stick / played / grabbing / flat palm / fist / diagonal / untracked never turn, a jump resets,
  the head frame.

PlayMode (`Tests/PlayMode/Layout`, 49 cases). Every fixture boots through the foundation's `TestBoot` (a fresh
`MemorySettingsStore`, a temporary data folder) with manual frames of exactly 1/72 s. A simulated headset makes
`App.IsPresenting` true unless a test says otherwise. The scripted hands carry +-1 cm jitter, and the reference rigs probe
stands in for the instruments. The Audio module is booted only by `PageTurnSoundTests`.
- `FirstArrangeTests`: on the desktop the room is the homes about ORIGIN (piano .55 m ahead of the camera, nothing
  re-arranges, no hand grabs); a single layout restored from the settings is arranged about the desktop camera
  (no fitSeat); a late XR start (mouse hand first, then the headset's hands) arranges once about the moved and
  turned head, and every new session arranges again.
- `LayoutStubTests` (the foundation's stub modules): the desktop room is every home about ORIGIN; `PlaceAroundPiano` keeps
  the home offsets.
- `LayoutModeTests`: the first presenting frame lays out the golden room about the head; fitSeat moves the seat back
  instead of sliding the kit; Set membership / tutorial stop / sticks release / persistence / events / lock; harp
  strings land ahead of a swivelled seat; a turn lands exactly where `Set` would put it; a sweep turns the page (slide,
  scale, swish, events, membership at the end); the incoming instrument stays out of play (not in the scene, not
  ticked, no grab target, no zone) until the turn ends while the outgoing one stays live; the turn's settle refreshes
  the zone of the table it pushed; all mode ignores sweeps and `Next` wraps; MR has no walls and no seat fit; a recentre
  re-arranges; the menu opened inside an instrument is cleared out, still facing the player.
- `PageTurnSoundTests`: a right sweep in single mode turns the page back and the real engine stages exactly the two
  swish noise bursts on the Ui source (AudioTestTap).
- `GrabTests`: grab targets / play surfaces / zones vs the golden; a still pinch on each rig's handle
  grabs (dwell ring mid-dwell, floor ring and cage held, instruments not ticked) and a drag moves it ~20 cm; a moving
  pinch never grabs; played 0.8 s ago never, 1.1 s ago yes; never over keys, heads, cymbals, pad, strings, deck buttons,
  levers; locked rigs stay, the menu moves (no pokes while held, lifted, follows); a constraint never traps a grab
  (stop, 300 ms, red ring, through, back, let go inside -> settled, no 2D / 3D overlap, eyes clear); four quarter wrist
  turns = 360 degrees about the footprint centre; a two-hand twist of 200 degrees; the other hand's pull turns 45
  degrees clockwise for a pull right; the lift dead band and -0.45..+0.6 m range, the x -2.8 origin clamp and the
  release back inside the walls; tracking loss lets go; no grab while MR places; the MR empty-air pinch starts placing.
- `SeatGestureTests`: swivel snap -45 for a pull right, about the head, motion reset, ends on PinchEnd; no swivel near
  an instrument or after playing; the two-hand summon after 0.8 s, once, re-arming, cancelling the swivel; no summon
  with a hand on an instrument; the grab takes hold in the Grab phase and a turn animates in LayoutAnimation
  (FrameOrderTests pattern); the per-frame layout work allocates nothing (drag, dwell, sweep, swivel, summon, turn).
- `GrabVisualsTests` (not headless): ring / dwell ring / cage meshes, segment fill, gold alpha .5 on each renderer's own
  clone (no property blocks), floor height, centred under the footprint, zero allocation with real renderers; the three
  procedural meshes are destroyed with the app.
- `DesktopAndMixedRealityTests`: Shift-drag slides the piano 20 cm on its drag plane (no lift, no turn, not ticked, camera
  blocked during the drag, a plain press does nothing); Alt-drag turns by -25 px x .012 rad about the footprint centre;
  locked, only the menu moves; the nearest rig on the ray is picked; a pinch that just misses a stick (`TryPickUp` =
  Near) never grabs the kit, one on the handle still does, a picked-up stick never does; the MR empty-air pinch calls
  `StartPlacing`, hides the other rigs and the open menu's group (the menu stays shown), blocks grabs, and
  `PlaceAroundPiano` brings them back with the menu shown and cleared out (a closed menu stays closed); PlaceAroundPiano
  keeps the piano and turns the rest with it, settled; RestoreHomes = the golden room about the origin seat, `Anchor` in
  both modes; `ArrangeAroundPlayer(false, false)` keeps the piano and reports Forced false; `GrabTarget` /
  `GrabCandidate` / `NearInstrument` allocate nothing.

## Known gaps

- Page-turn opacity fade: slide and scale only, as ARCHITECTURE.md 15.5 fixes for v1; `FadeOf` / `TurnProgress` are
  public for a later fade.
- The incoming piano of a page turn is still ticked while it slides until `PianoInstrument.IsPresent` reads
  `Rig.InScene` (`CONTRACT-REQUESTS.md` 1; Drums, Harp and Room already do).
- The piano's "on a surface" state in MR is inferred from its height (`CONTRACT-REQUESTS.md` 2).
- Contact shadows under a lifted rig are the owning module's (to be lowered on release).
- The desktop mouse path is tested through `DesktopStep` with scripted rays; the live `DesktopInput` read is not
  exercised headless.
- An intermittent foundation Audio race (`SynthSource.Awake` order, `CONTRACT-REQUESTS.md` 3) can fail a foundation test
  that boots the Audio module; it is not in this module.
- Verified against the reference rigs' boxes and the foundation's stub modules; the real instruments' `Handles` /
  `PlayedParts` decide the footprints after the merge (the golden scenarios are the reference to compare against).

## The deck stays beside the player (2026-11-17, TUNE-DECK-FRONT)

- `Settle` -> `KeepTableBeside`: a side table the resolver moved more than `TableBackSlack` (3 cm) toward behind the
  player (an instrument in front touched it: a kit turned left) is moved off it to the left or left-and-forward instead
  (smallest free move, up to 0.6 m); logged "[Layout] side table: ...", counted `TableKeptBeside`.
- A recentre held back while the headset was away is applied on the return about the head when the origin's heading is
  more than `ReturnTurnMax` (40 deg) from the settled head's (`OriginFacesAway`; it becomes the player's front).
- `ComfortCodec.MigrateAutoSavedSpots`: once, an install that ran an older build forgets every saved spot (those builds
  saved every rig's automatic pose on Move instruments off).
- Tests: Integration/DeckFrontTests, Integration/DeckFrontShotsTests (Graphics), EditMode Layout/AutoSpotsMigrationTests.
