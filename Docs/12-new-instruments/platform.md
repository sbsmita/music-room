# 12 · New instruments · platform: eight instruments in one seated room (implementation-ready spec)

> **2026-11-10: the theremin was removed from the app** at the player's request (its folders, unit, source, lessons, Sunny lines and tests; `theremin.md` and its critique deleted). The id block value 8 stays retired and is never reused; `Assets/MusicRoom/Core/Registry/RetiredInstruments.cs` migrates an old save. Mentions of it below are the historical design record.

**What this is.** The many-instrument platform for the Unity app. Today's code assumes three instruments (piano,
drums, harp) plus the side table. We are adding a handpan, hand drums, a marimba with mallets, a theremin and singing
bowls (their own designs: `12-new-instruments/<key>.md`), so 8 instruments. They cannot all sit within a seated 2 ft
reach. This spec covers:

- the instrument registry;
- where instruments live when they are not in use, and how the player brings one to the seat;
- the menu, my set, arrange mode and persistence;
- recording, the looper, lessons and Sunny for every instrument;
- mixed reality;
- the audio engine plug-in for five new sound roles;
- budgets, tests, and the merge plan for six parallel clones.

**Who builds it.** The platform agent, in one clone (phase 0, section 13), after `u-arrange` and `u-looper` are
merged. The five instrument agents and the sheet-music agent then build against the contracts below, in parallel
clones, each in its own folders.

**Evidence.** The geometry, the ray pick, the control-stream encoder and the budget model are Node 26 reference code
with deterministic test vectors. They are the platform lab (`Tools/SoundLab/platform/` holds `registry.mjs` and `control-decimator.mjs`;
`node make-vectors.mjs` writes `platform.vectors.json`, frozen in `Tests/EditMode/Layout/Reference~/`, and runs its self-checks: all pass). The footprints of the
three existing instruments are fitted to the instrument geometry of `Docs/05-instruments.md`. The five new ones use realistic sizes, marked
**provisional**; their agents replace them with real `Rig.Handles` bounds, and a conformance test checks those bounds
within 3 cm (section 12.2). Nothing here was run in Unity. Device-only facts are marked **UNVERIFIED**, each with a
test (section 14).

Coordinates are Unity's (ARCHITECTURE 5): the default seat's head is at (0, 1.2, 0) facing +z, +x is right, and yaw is
in degrees clockwise from above (+90 = to the right).

---------------------------------------------------------------------------------------------------------------------------

## 0. Decisions (ranked)

1. **A data-driven registry.** Each instrument module ships one `IInstrumentDefinition` that returns an
   `InstrumentDescriptor`: plain data covering identity, seat, display spot, tools, audio role, event types, lessons,
   tips, portrait and budgets. Core collects the descriptors by reflection before any module's `Init`. Layout, Menu,
   Lessons, Guide, MR, Audio and FMM read the registry and never switch on `InstrumentKind` again. A source-scan test
   enforces this (section 3.4).
   - Ids are given out once, in one block, for all eight instruments (section 2).
   - Adding a ninth instrument later means a new module folder, a new unit folder and the 6-line id block. Nothing
     else changes.
2. **Instruments live on display in the room (VR).** Every instrument that is not at the seat stands, hangs or rests
   in its own place along the walls (section 4.1):
   - the piano against the front wall;
   - the theremin by the floor lamp;
   - the harp in the front-left corner;
   - the drum kit along the left wall, with the handpan on a wall bracket above it;
   - the marimba and the hand drums under the window;
   - the singing bowls on the window sill.
   A displayed instrument is a static, silent object: not ticked, no sound CPU, its audio sources paused. In mixed
   reality the displayed instruments are hidden.
3. **"My set" holds up to 3 instruments, plus the side table.** This is `LayoutMode.All` with a player-chosen set. The
   default set stays piano + drums + harp, at exactly today's homes (tested). A seat planner checks the fit:
   - 7 seat slots (front, the two front diagonals, the two sides, the two rear diagonals);
   - each instrument's home rotated about the head;
   - a cost-optimal search.

   With the footprints here, every instrument fits alone, 27 of 28 pairs fit and 47 of 56 triples fit. No set of 4
   fits within reach, so the menu promises 3. The planner itself has no size limit. "One alone" (`LayoutMode.Single`)
   stays as it is.
4. **Three ways to bring an instrument to the seat, through one Layout call, `Bring(kind, reason)`:**
   - the menu's Instruments page, a grid of 8 portrait tiles;
   - the page-turn swipe in One alone, through the carousel order piano → harp → theremin → singing bowls → handpan →
     marimba → hand drums → drums → back to piano;
   - pointing at a displayed instrument and pinching (a 6° cone pick, held 0.35 s). When the set is full, the instrument
     that will make room lifts 2 cm first as a preview.
5. **Instruments travel; they never teleport.** A glide of 0.9–1.4 s on a path that stays at least 0.75 m from the
   head; the outgoing instrument leaves 0.15 s first. Mixed reality keeps today's 0.35 s slide.
6. **Audio:**
   - five new unit-only roles in the Burst engine: `UnitId` 5–9 and 7 new `SourceId`s;
   - unit handles widened from 3 to 4 bits;
   - the units idle-skip while silent, and displayed instruments' sources pause;
   - CPU caps per unit, plus a priority governor (`UnitShare`);
   - the room's early-reflection groups grow from 3 fixed to 4 assigned dynamically.

   "Classic sound" never touches the new roles.
7. **Recording is generic.** Each instrument registers its `TakeEvent` types with a role (Onset, Release, Control,
   State, Marker) and implements `IInstrumentPlayback`. Continuous instruments (theremin, bowl rubbing) use the
   ControlStream encoder:
   - deadbands of 2 cents and 0.5 dB, at most 30 Hz, a keyframe every 0.25 s;
   - measured: ≤ 29 events/s, a reconstruction error of 1.7 cents rms (8.7 cents max) and 0.41 dB.
8. **Lessons are per-instrument packs**, run by a generic tutorial path through `ILessonTargets`. The three existing
   instruments keep their tested code path.
9. **Sunny's tips are data in each descriptor**, with their voice clips in the instrument's own `Resources`. The Guide
   merges every module's manifest. Coverage tests run over the registry.
10. **Hand ownership.**
    - Contact zones, the menu and Sunny claim the fingertips inside them, every frame.
    - A contactless instrument (the theremin) plays only from unclaimed hands, and only while the player faces it
      (head-facing focus).
    - Sticks, mallets and bowl sticks share one tool system: `IHandToolProvider` and `Pointer.Tool`.
11. **The merge plan.**
    - Phase 0 (the platform clone) makes every shared-file change, plus five stub instruments.
    - Six clones then build in parallel, each owning only its folders.
    - Integration is a folder copy plus a checklist.
12. **A conformance suite over the registry** gives every instrument about 20 tests for free (section 12.2).

---------------------------------------------------------------------------------------------------------------------------

## 1. What today's code assumes (audit)

47 runtime files name an instrument (a grep for `InstrumentKind.`, `RigId.`, the three id strings and the per-role
`UnitId` / `SourceId` names). The ones that block an eighth instrument:

| Where | What is hard-wired | Phase 0 change |
|---|---|---|
| `Core/Contracts/Instruments.cs` | `InstrumentKind` 0–4, `PlaySurface` per instrument, `IInstrument.AcceptsStickPointers` (drums only) | id block (§2); `PlaySurface.Instrument` (generic); tool masks (§4.6) |
| `Core/Contracts/Rigs.cs` | `RigId` 0–4 | id block |
| `Core/App/AppSettings.cs:52-78` | `musicroom.current` parses only "piano", "drums", "harp" | registry parse; new keys (§4.7) |
| `Core/Input/Pointer.cs:58,77-78` | `DrumLastHit[9]` / `DrumArm[9]` on every pointer | kept for the kit; new instruments keep per-pointer state in their own arrays indexed by the new `Pointer.Slot` |
| `Core/Input/FastMotionPolicy.cs:102` | Fast Motion Mode wanted only for drums | `InstrumentTraits.FastMotion` |
| `Core/App/App.cs` `TickInstruments` | `AcceptsStickPointers ? AllPointers : HandPointers` | per-instrument pointer lists by `ToolMask`; pointer claims (§4.6) |
| `Layout/LayoutModule.cs:23,233-239,273-277,284` | `Row = {Piano, Drums, Harp}`; `IsRowRig`, `RigOf`, `KindOf`, `Normalize`; `Set` accepts only the three; `IsInScene` = All or current | registry, carousel, set, `PlaceOf` |
| `Layout/LayoutModule.Arrange.cs:266-302` | per-`RigId` switch in placement | `SeatPlanner` (§4.2) |
| `Layout/LayoutModule.Grab.cs:82,166-191` | piano key space, stick pick-up through `IDrumsApi` | piano rule kept; tools through `IHandToolProvider` |
| `Menu/MenuController.cs:330-331,392-420`; `MenuStyle.cs:100-106` | `s_row`, `IconOf`, `Pretty`, `AloneOf`, `IdOf`, `KindOf`; the Instruments screen's 4 layout rows | registry, tile grid (§5) |
| `Menu/MenuIcons.cs` | three vector icons | portraits + descriptor vector fallback (§5.2) |
| `Lessons/Tutorial.cs:48-49,139,215-225,324-489` | travel, lead, block shape, hit matching and block poses per instrument | generic path via `ILessonTargets` for the new ones (§7.2) |
| `Lessons/LessonLibrary.cs:37`, `LessonHints.cs`, `LessonStage.cs:39-41` | one `lessons.json`; hints and roots per instrument | lesson packs (§7.1) |
| `Lessons/PlaybackWiring.cs` (u-looper) | seven `SetTarget` lines | generic loop over `IInstrumentPlayback` (§6.2) |
| `Lessons/Looper.cs:501,555-569` (u-looper) | `IsOnset` = pianoOn / drum / harp; held-note logic piano-only | registry roles; `CloseLane` (§6.4) |
| `MR/MrModule.cs:515-765` | the piano is the anchor, the table instrument and the light target | "lead" instrument (§9) |
| `Room/Table/SideTable.cs:250-263` | `RigShadows.Defaults` for piano, kit, harp (3 draws) | descriptor `Shadow`, one combined mesh (1 draw) |
| `Audio/Engine/IAudioEngine.cs` | `SourceId` 0–17 | +7 sources (§10.1) |
| `Audio/Engine/AudioRig.cs` | range checks `id <= DrumRoom`, `Kick..Ride` for acoustics, sends, pools | one `AudioSourceTable` row per `SourceId` |
| `Audio/Burst/Units/UnitSlot.cs`, `SoundUnit.cs` | `UnitId` 0–4; 3-bit unit field in `UnitHandles` (max 8 units); 4 partial hooks and factories; `Roles`, `ClassicSources` | `UnitId` 5–9, 4-bit field, 10 partials pre-declared (§10.2) |
| `Audio/Sound/Room/RoomModel.cs:46-51`, `RoomUnit.cs:96-160` | 12 tap sources, 3 fixed early-reflection groups (piano, harp, kit) | taps from the table, 4 dynamic groups (§10.2) |
| `Audio/Synths/GuideVoice.cs`, `Guide/Resources/guide-lines.json` / `Voice/manifest.json` | one line table | per-module manifests merged (§8.2) |
| `Core/link.xml`, `Tests/*/MusicRoom.Tests.*.asmdef`, `Editor/MusicRoom.Editor.asmdef` | assemblies listed by name | the six new assembly names added once |

---------------------------------------------------------------------------------------------------------------------------

## 2. Identity: the id block (phase 0, once)

| Instrument | `InstrumentKind` | `RigId` | `UnitId` | Sources | Module class, Order | `TickOrder` | Folder / asmdef | Persisted id | Name | Carousel |
|---|---|---|---|---|---|---|---|---|---|---|
| Piano | 0 | 0 | 0 | PianoLo, PianoMid, PianoHi | `PianoModule` 30 | 0 | `Piano` | `piano` | Piano | 0 |
| Drums | 1 | 1 | 1 | Kick … Ride, DrumRoom | `DrumsModule` 31 | 10 | `Drums` | `drums` | Drums | 7 |
| Harp | 2 | 2 | 2 | Harp | `HarpModule` 32 | 20 | `Harp` | `harp` | Harp | 1 |
| Table (deck, switches) | 3 | 3 | – | – | Room 20, Lessons 40 | 30, 40 | – | – | Side table | – |
| Menu | 4 | 4 (`Lessons`) | – | Ui | `MenuModule` 60 | 50 | – | – | – | – |
| Handpan | 5 | 5 | 5 | `Handpan` | `HandpanModule` 33 | 21 | `Handpan` / `MusicRoom.Handpan` | `handpan` | Handpan | 4 |
| Hand drums | 6 | 6 | 6 | `HandDrumLo`, `HandDrumHi` | `HandDrumsModule` 34 | 22 | `HandDrums` / `MusicRoom.HandDrums` | `handdrums` | Hand drums | 6 |
| Marimba | 7 | 7 | 7 | `MarimbaLo`, `MarimbaHi` | `MarimbaModule` 35 | 23 | `Marimba` / `MusicRoom.Marimba` | `marimba` | Marimba | 5 |
| Theremin | 8 | 8 | 8 | `Theremin` | `ThereminModule` 36 | 28 | `Theremin` / `MusicRoom.Theremin` | `theremin` | Theremin | 2 |
| Singing bowls | 9 | 9 | 9 | `Bowls` | `BowlsModule` 37 | 24 | `Bowls` / `MusicRoom.Bowls` | `bowls` | Singing bowls | 3 |
| Sheet music (no rig) | – | – | – | – | `SheetModule` 45 | – | `Sheet` / `MusicRoom.Sheet` | – | – | – |

Notes on the block:
- **`UnitId`:** `Room` stays 3 and `Example` stays 4; `Count` becomes 10.
- **`SourceId`:** the 7 new values are appended after `Ui` and before `Count`, so the persisted and tested values 0–17 do
  not move.
- **The `RigId` = `InstrumentKind` value rule:** `RigOf(kind) = (RigId)(int)kind` holds for every value (`Lessons` = 4
  = `Menu`). `RegistryTests` asserts it.
- **`TickOrder`:** the contact instruments tick before the contactless theremin (28), and both before the deck (30).

Rules:
1. Values never move or get reused. `InstrumentKind` is persisted as the id string, `RigId` by its name (in
   `ArrangementCodec`), and the `UnitId` value lives in runtime handles only.
2. **`UnitHandles` widens to a 4-bit unit field** (16 roles):
   `Make = -(1 + ((payload & 0x07FFFFFF) << 4 | unit))`, `Unit = (-h - 1) & 15`, `Payload = (-h - 1) >> 4`.
   - Payloads lose one bit (27 bits).
   - The piano's generation counter `(gen << 7) | key` wraps at 2^20 strikes per key instead of 2^21; the harp's
     `(g << 5) | string` is unaffected.
   - `UnitIdTests` round-trips every role with payloads 0, 1, 2^27 − 1.
3. **A ninth instrument** needs exactly:
   - `InstrumentKind` + `RigId` (Core `Instruments.cs`, `Rigs.cs`);
   - `UnitId` + 2 partial declarations + 2 switch cases (`UnitSlot.cs`, `SoundUnit.cs`);
   - its `SourceId`s + `AudioSourceTable` rows (`IAudioEngine.cs`, `AudioRig.cs`);
   - the asmdef name in `link.xml` and three asmdefs.

   Everything else is registry data in its own folders.

---------------------------------------------------------------------------------------------------------------------------

## 3. The registry (Core)

### 3.1 Contract (`Core/Contracts/Registry.cs`, new)

```csharp
namespace MusicRoom.Core
{
    public enum SeatSlot : byte { Front = 0, FrontRight, Right, FrontLeft, Left, RightRear, LeftRear }   // 0, 45, 90, -45, -90, 120, -120 deg
    [Flags] public enum InstrumentTraits : uint {
        None = 0, Playable = 1, Contactless = 2, Continuous = 4, UsesTools = 8, FastMotion = 16, Pitched = 32,
        HasStand = 64, TableTop = 128 }     // TableTop: may stand on a real table in MR
    public enum ToolKind : byte { None = 0, Stick = 1, Mallet = 2, Striker = 3, RubStick = 4 }
    [Flags] public enum ToolMask : byte { None = 0, Stick = 1 << 1, Mallet = 1 << 2, Striker = 1 << 3, RubStick = 1 << 4 }
    public enum EventRole : byte { Onset, Release, Control, State, Marker }   // section 6.1
    public enum EventKey : byte { None, Midi, Index, Kind, Lane }             // what pairs a Release with its Onset
    public enum StowMount : byte { Floor, Wall, Surface }
    public enum LessonBlockShape : byte { Box, Disc, Capsule, Ring, Bar }
    public enum NotationKind : byte { None, Treble, Grand, Percussion, PitchLine }   // for the sheet-music module

    public readonly struct EventTypeSpec { public readonly string Type; public readonly EventRole Role; public readonly EventKey Key;
        public EventTypeSpec(string type, EventRole role, EventKey key = EventKey.None) { Type = type; Role = role; Key = key; } }
    public struct FootBox { public float Cx, Cz, Hx, Hz, Y0, Y1; public bool Air; }   // rig-local xz centre, half sizes; heights above the floor

    public sealed class SeatSpec {
        public SeatSlot CanonicalSlot; public SeatSlot[] Prefs; public int Priority;     // planning order (big first)
        public float ReachMax; public Vector2 PlayCentre;                               // rig-local xz; horizontal reach from the head
        public FootBox[] Boxes;                                                          // checked against Rig.Handles (3 cm)
        public float LiftMin, LiftMax;                                                   // arrange-mode height range about the home (m)
    }
    public sealed class StowSpec { public string SpotId; public StowMount Mount; public FootBox[] Boxes; public string Where; }  // "By the window"
    public sealed class PortraitSpec { public Vector3 From, At; public float Fov; public IconStroke[] Fallback; }               // rig-local
    public readonly struct IconStroke { public readonly Vector2[] Points; public readonly bool Closed, Fill; }                 // 100-unit box, MenuIcons style
    public sealed class AudioSpec { public byte Unit; public float CapAvgMs, CapWorstMs, ArenaMiB, BakedMiB; public Vector3 ErOrigin; }
    public sealed class LessonSpec { public float Travel, LeadSeconds; public LessonBlockShape Shape; public NotationKind Notation; public string Pack; public string CountNoun; }
    public sealed class MrSpec { public bool OnTable; public float SurfaceLift; }
    public sealed class ShadowSpec { public Vector2 Local; public float W, D, Opacity; }
    public sealed class TipSpec { /* section 8.1 */ }

    public sealed class InstrumentDescriptor {
        public InstrumentKind Kind; public RigId Rig; public string Id, Name, AloneName, Family;
        public int Carousel, ModuleOrder, TickOrder;
        public InstrumentTraits Traits; public ToolMask Tools;
        public SeatSpec Seat; public StowSpec Stow; public PortraitSpec Portrait; public AudioSpec Audio;
        public LessonSpec Lessons; public MrSpec Mr; public ShadowSpec Shadow;
        public EventTypeSpec[] Events; public TipSpec[] Tips; public string[] Features;   // feature ids "<id>.<feature>"
        public int DrawsAtSeat, DrawsStowed, TrisAtSeat, TrisStowed;                      // budgets the instrument promises (section 11)
        public Pose SheetAnchor;                                                          // rig-local music-rest pose (piano); default = none
    }

    public interface IInstrumentDefinition { InstrumentDescriptor Describe(); }      // one per instrument module, parameterless

    public interface IInstrumentRegistry {
        IReadOnlyList<InstrumentDescriptor> All { get; }          // playable ones, in carousel order
        IReadOnlyList<InstrumentKind> Carousel { get; }
        InstrumentDescriptor Get(InstrumentKind kind);            // null for an unknown or unloaded kind
        bool TryParse(string id, out InstrumentKind kind);        // persisted ids, lesson "instrument" fields
        string IdOf(InstrumentKind kind);
        bool IsLoaded(InstrumentKind kind);                       // its module registered a Rig this boot
        InstrumentKind OwnerOfEvent(string takeEventType);        // which instrument recorded this TakeEvent type
        EventRole RoleOf(string takeEventType);
        InstrumentKind Next(InstrumentKind from, int direction);  // carousel step, skipping unloaded instruments
    }
}
```

### 3.2 Discovery and lifetime
- **When:** `new App(...)` builds the registry before `ModuleDiscovery.CreateModules()`. It reflects over the
  `MusicRoom.*` assemblies (never `MusicRoom.Tests.*`) for parameterless `IInstrumentDefinition` classes, the same rule
  as module discovery.
- **Built-in descriptors:** Table and Menu (`Core/Registry/BuiltIns.cs`) are not playable and are left out of `All`.
- **Access:** `App.Registry`, and also `app.Get<IInstrumentRegistry>()`.
- **Validation at build time:** a duplicate `Kind`, `Id`, `Carousel` index, event type, tip id or feature id is a hard
  error with a `[Core] registry:` log line, and the later definition is dropped. `RegistryTests` fails on it.
- **Test boots:** `BootOptions.InstrumentFilter` (a `Func<InstrumentKind, bool>`, default all) lets a test boot the
  three classic instruments only. A filtered-out instrument is `!IsLoaded`: the menu hides it and the carousel skips
  it.
- **`IsLoaded`** turns true when the instrument's module registers its `Rig`. A definition without a module shows
  nothing and is logged once.

### 3.3 What an instrument implements

| Interface | Required? | Purpose |
|---|---|---|
| `IInstrumentDefinition` | yes | its descriptor |
| `IInstrument` (existing) | yes | `Tick`, `Hover` (may be empty), `IsPresent` = `Rig.InScene`, `PlaySurfaceAt`, `PlayedParts` |
| `Rig` (existing) | yes | `Id`, `Group`, `Handles` (filled once), `Zone`, `HomeAll` at its canonical slot, `HomeSingle` |
| `IStowable` | yes | `SetStowed(bool stowed, StowMount mount)`: static display configuration (tools resting on it, power off, stand hidden for `Surface`); `Stowed` |
| `IInstrumentPlayback` | yes | `Play(in TakeEvent e)`, `ReleaseAll()`, `CloseLane(int lane)` (section 6.2) |
| `ILessonTargets` | if it has lessons | section 7.2 |
| `IGuideAnchors` | yes | `TryGetAnchor(string anchor, out Vector3 world, out Vector3 up)`: Sunny's demo targets (section 8.4) |
| `IContactSurface` (existing, E1/E3) | for struck surfaces | finger clamp and soft contact shadows on heads, bars, the pan |
| `IHandToolProvider` | if it has tools | section 4.6 |
| `IInstrumentSurface` | if `TableTop` | MR: `SetOnSurface(bool)`, `OnSurface` (generalises `IPianoApi.SetOnSurface`) |
| its own API (`IHandpanApi`, …) | optional | in its own assembly, or in Core only if another module truly needs it (request it) |

New instruments use no hover glows (the user rejects glowing UI). Feedback is physical: the contact shadows and the
finger clamp of E1/E3, plus the instrument's own motion. Lesson targets use the pale warm cue decided for the tour
(section 7.2).

### 3.4 Rules (tested)
- **No identity switches in shared modules.** Layout, Menu, Lessons, Guide, MR, Room and Core (outside
  `Core/Registry`) never name a specific instrument. That means no `InstrumentKind.<X>`, no `RigId.<X>`, no id
  string literals, and no `UnitId.<X>` / `SourceId.<X>` in Audio outside `Audio/Sound/<Role>` and `AudioSourceTable`.
  - `RegistryLintTests` (EditMode) scans those folders' `.cs` files.
  - Its allowlist keeps the tested piano-specific rules: Layout's piano key space, MR's piano anchor E9, the tutorial's
    three classic paths, and `Table` / `Menu`.
  - Any other hit fails, with the file and line.
- **Static data only.** A descriptor holds no Unity objects, so the registry can be built in EditMode without a scene.
- **Shared ids carry a prefix.** Every string an instrument adds to a shared space starts with its id: event types
  (`handpan`, `handpanMute`), settings keys (`musicroom.handpan.*`), voice lines (`tip_handpan_*`), lesson ids
  (`handpan-*`), feature ids (`handpan.*`), material templates and bake names (`Handpan/<name>`) and the
  `Builds/Shots/handpan/` folder.
  - Exceptions: the classic three keep today's names, and the hand drums' event types start with `handDrum`.
  - `RegistryTests` checks the prefixes.

### 3.5 Descriptor values for the existing instruments (phase 0 writes `PianoDefinition.cs`, `DrumsDefinition.cs`, `HarpDefinition.cs`)

| | Piano | Drums | Harp |
|---|---|---|---|
| Canonical slot, home (All) | Front, (0, .72, .45) yaw 0 | Right, (.2, 0, −.26) yaw 90 | Left, (−.78, 0, −.2) yaw 83 |
| Single home | same | (0, 0, .1) yaw 0 | (mx, 0, .45) yaw 180 |
| Slot preferences | Front, Left, Right | Right, Left | Left, Right, FrontLeft, FrontRight |
| Reach max (m, play centre) | 0.62 | 0.70 | 0.95 (long strings, played at the near half) |
| Traits, tools | Playable, Pitched, HasStand, TableTop | Playable, UsesTools, FastMotion; Stick | Playable, Pitched |
| Display spot | front-wall-right | left-wall | front-left-corner |
| Draws at seat / stowed | 10 / 4 | 21 / 6 | 5 / 3 |

The footprint boxes are in `Tools/SoundLab/platform/registry.mjs`: the piano's keys + case + stand sides; per kit piece,
its shell or cymbal plus its pole; the harp's base and its string plane. They reproduce today's default layout with no
collisions (the self-check `default set at today's homes`).

---------------------------------------------------------------------------------------------------------------------------

## 4. Where instruments are, and how they come to the seat

### 4.1 The display (VR): `Layout/StowPlan.cs`

Each instrument has one display spot. This is a pose in room coordinates, where its rig stands, hangs or rests the way
a real living room would keep it. The rig stays active and visible there. When it is put away the instrument gets
`IStowable.SetStowed(true, mount)`, and from then on:
- **Not present:** `Rig.InScene = false`, so Core skips it (no Tick, no Hover).
- **Static:** its static display meshes are on, combined per material (`DrawsStowed`); its tools rest on it; the
  theremin is powered off.
- **Silent:** its unit idle-skips once silent and its sources pause (section 10.1).

The rig is never deactivated in VR. In MR it is deactivated (section 9).

| Instrument | Spot id | Mount | Rig pose (x, z, yaw) | Distance / bearing from the seat | What it looks like |
|---|---|---|---|---|---|
| Piano | `front-wall-right` | floor | (1.78, 0.77, 0) | 1.96 m / +65° | against the front wall under the right painting, as a digital piano stands at home |
| Theremin | `front-wall-left` | floor | (−1.62, 0.91, 0) | 1.79 m / −65° | on its stand by the floor lamp, antenna up, pilot lamp off |
| Harp | `front-left-corner` | floor | (−3.06, 0.26, 90) | 3.12 m / −79° | standing in the corner beyond the lamp, strings toward the room |
| Drum kit | `left-wall` | floor | (−2.06, −0.66, −90) | 2.45 m / −106° | along the left wall, throne side to the room, sticks on the snare |
| Handpan | `left-wall-bracket` | wall | (−3.05, −0.62, −90), 1.30–1.86 m high | 3.11 m / −101° | on a wooden wall bracket above the kit, tone fields toward the room |
| Marimba | `window-near` | floor | (2.71, −0.80, 90) | 2.83 m / +106° | under the window, bars below the sill line, mallets on the bars |
| Hand drums | `window-far` | floor | (2.75, −1.80, 90) | 3.29 m / +123° | under the far end of the window, heads at the sill line |
| Singing bowls | `window-sill` | surface | (3.08, −1.62, 90), on the 0.63 m sill | 3.48 m / +118° | the three bowls on their cushions on the inside sill, striker and stick beside them; their low stand is hidden |

**Why these places.** The front wall is in view, and the theremin is the first thing to the front-left, its "wow"
moment. The window, the room's focus with the snow, has the low instruments under it and the bowls on its sill, where
they catch the cold light. The left wall takes the big kit and the hung handpan. Nothing stands in front of the fire,
the shelf or the sofa. Nothing taller than 0.85 m stands in front of the window glass. Nothing is closer than 1.3 m to
the head.

**Validation** (`stow-plan.mjs validate()`, all pass; `StowPlanTests` repeats it with Room's real blockers). Every
displayed box is:
- inside the room;
- 3 cm clear of every furniture blocker (bookshelf, lamp and shade, four plants, loveseat, coffee table, hearth, both
  curtain panels, the inside sill, the window view above 0.85 m, the bench);
- 3 cm clear of every other displayed instrument;
- 3 cm clear of every box of every set that fits at the default seat. So a displayed instrument never stands where
  some set could put an instrument.

Room publishes the blockers as data: `IRoomFurniture.FloorBlockers`, a list of oriented boxes with height ranges
(phase 0, in the Room folder).

**Other seats.** The seat frame follows the head (ARCHITECTURE 15.5: fitSeat). If an at-seat box ends up within 3 cm
of a displayed one, that display spot is "blocked":
1. The instrument moves to the first free spare: `back-wall-left`, at (−2.2, −3.85, 180), left of the fireplace; then
   `coffee-table` (`Surface`, at 0.45 m).
2. If both spares are taken it is hidden, logged `[Layout] display blocked: <id>`, and shown again when the seat moves.

`SeatAwayTests` runs heads at (±0.6, 1.2, ±0.6).

**Arrange mode moves displayed instruments too (VR).** Ray grab, as u-arrange. Rules:
- A floor instrument slides on the floor; a wall instrument slides on its wall plane; a surface instrument stays on
  its surface.
- Everything stays inside the room, out of the blockers, and 1.1 m or more from the head.
- The result is saved as `musicroom.stow.<id>`; Reset brings back the plan's spot.

### 4.2 My set and the seat planner: `Layout/SeatPlanner.cs` (reference `seat-planner.mjs`)

**Slots.** Seven seat slots, by heading about the head:

| Slot | Front | FrontRight | Right | FrontLeft | Left | RightRear | LeftRear |
|---|---|---|---|---|---|---|---|
| Heading | 0° | +45° | +90° | −45° | −90° | +120° | −120° |

The rear slots cost more (below), and the seat-turn gesture reaches them in one pull. A candidate pose is the
instrument's canonical All home rotated about the head from its canonical slot to the slot, then pushed out 0, 5, 10
or 15 cm along the slot's direction.

**A candidate is accepted when:**
- (a) the play centre is within `ReachMax` of the head (horizontal);
- (b) no box overlaps, with a 3 cm gap: the side table, an already placed member, or (for non-air boxes) the seated
  player (torso, knees, the seat cushion);
- (c) every non-air box is inside the room box at the default seat. At other seats Layout's `PlacementSolver` fits the
  seat as today.

"Air" boxes are play space, such as the theremin's hand field. They keep other instruments out, but not the player.

**The search.**
- Members are taken in `Priority` order: piano, drums, marimba, theremin, harp, hand drums, handpan, bowls.
- Each member tries its slot preferences in order, using the first push that fits in each slot.
- Every complete assignment is costed: `cost = preference rank + 2 for a rear slot + 0.25 per 5 cm push`. The cheapest
  wins; ties go to the first found.
- Canonical members cost 0, so the default set comes out exactly at today's homes, and every existing Layout test
  stays true.
- At most 259 candidates are evaluated for one set (4853 over all 92), well under 2 ms in C# on the Quest
  (estimated). The planner runs only when the set changes, the seat is re-fitted or the head re-centres; never per
  frame.

**The side table yields.** The table (deck and switches) is always at the seat:
- It stands at its home (left, (−0.39, 0, −0.21) yaw −90).
- Only when a set cannot be planned that way is it tried at its alternate pose, behind-left at (−0.25, 0, −0.62)
  yaw 180, 0.67 m from the head.
- 29 of the 82 sets that fit need the alternate.
- Sunny's deck lines must not say "on your left" (section 8.1).

**A set of one** is the instrument alone, at its Single home. That is today's One alone, and the planner returns it
directly.

**Results with these footprints** (vectors: `seatPlanner.sets`, all 92 sets of up to 3):

| | Fit | Do not fit |
|---|---|---|
| Alone | 8 / 8 | – |
| Pairs | 27 / 28 | piano + marimba (both need the front: the marimba's 1.2 m of bars cannot reach from a side) |
| Triples | 47 / 56 | the 6 triples with both the piano and the marimba; harp + theremin + drums, piano + theremin + drums, theremin + handpan + drums (the theremin's 1 m hand field does not fit beside the kit with a third instrument) |
| Four | 0 / 70 | within a 2 ft reach, three instruments and the side table fill the front half of the seat |

Which slot each instrument takes in a pair (row = the instrument, column = its partner; F front, R right, L left,
FR / FL diagonals, Rr right-rear, x does not fit):

```
            piano   harp therem  bowls handpa marimb handdr  drums
piano           -      F      F      F      F      x      F      F
harp            L      -      L      L      L      L      L      L
theremin       Rr      F      -      F     Rr      R      R      L
bowls           R      R      R      -      R      R      R      F
handpan         R     FR      F      F      -      R     FR      F
marimba         x      F      F      F      F      -      F      F
handdrums       R     FR     FL     FL     FL      R      -     FL
drums           R      R      R      R      R      R      R      -
```

**Joining a set** (`join()`; vectors `joins`). When an instrument is added:
1. If set + new fits (and size < 3), it is added.
2. Otherwise it replaces the member whose removal makes it fit. Members are tried least recently played first, then
   the smaller instrument first.
3. If nothing fits even then, the set becomes the new instrument alone.

`lastPlayed` comes from `RecorderEvent`s and is kept for the session only. Examples:
- piano + drums + harp, + handpan, with the harp last played 300 s ago: the harp goes back, and the handpan takes the
  left slot (the table yields).
- The same set, + marimba: the harp and the drums cannot make room (the piano and the marimba both need the front), so
  the marimba replaces the piano.

### 4.3 The carousel (page turn, the menu grid, `Next`)

Order: **piano → harp → theremin → singing bowls → handpan → marimba → hand drums → drums → piano**. Each step changes
one thing:
- piano → harp: strings, struck → plucked;
- harp → theremin: melodic and airy, touch → no touch;
- theremin → singing bowls: sustained and ethereal;
- bowls → handpan: metal, rubbed → tapped;
- handpan → marimba: struck tuned notes, hand → mallet;
- marimba → hand drums: struck by hand;
- hand drums → drums: skins, hand → stick;
- drums → piano: rhythm → keys.

The order is also the menu grid: the top row is the melodic and sustained four, the bottom row the struck four.
`Next` skips instruments that are not loaded. The first swipe from the piano now goes to the harp, not the drums. No
spoken line names the next instrument (`tip_swipe` / `swipe_vr` are generic).

### 4.4 Bringing an instrument to the seat

One call does it all: `ILayoutService.Bring(kind, BringReason reason)`, with reasons Menu, PageTurn, Ray, Lesson, Take,
Sunny.
- In One alone the instrument replaces the current one.
- In My set it joins (section 4.2), with the replacement chosen by `join()`.
- Lessons and take playback call it too. A take never brings anything: displayed instruments play back in place
  (section 6.2).

**(a) The menu.** Section 5.

**(b) The page turn** (One alone only, today's sweep gesture) goes to `Registry.Next(current, ±1)`.

**(c) Point and pinch from afar** (VR, `Layout/RaySummon.cs`). The ray is the system pointer pose
(`HandFrame.pointer`, valid only while `InputStateValid`), as in u-arrange. The target is chosen by a **cone pick**:
- among displayed instruments whose pick point lies within 6° of the ray and 6 m of the hand, the smallest angle
  wins; ties within 0.5° go to the nearer;
- the pick point is the top centre of its displayed boxes, 5 cm down;
- a first-hit raycast would hide the bowls behind the hand drums from the seat; the cone pick finds them (vectors:
  `stow.rays`, all 8 aims picked; with a ±3° hand-ray error, 23 of 24).

Sequence:
1. **Hover**, after 0.25 s on the same target:
   - a small cream card with the instrument's name (TextMeshPro, dark ink, 8 cm wide) appears 12 cm above the pick
     point, facing the head;
   - the ray shows as a thin pale line (#f3e6cf α 0.35, 3 mm) only while it points at a target;
   - no glow, no outline.
2. **Pinch** (the grab pinch: thumb-index or Meta's pinch strength 0.85/0.5) on the hovered target, then **hold
   0.35 s**:
   - while holding, the target lifts 2 cm;
   - if the set is full, the member that would make room (`ReplaceCandidate`) also lifts 2 cm, so the player sees which
     one will go;
   - releasing before 0.35 s cancels, and both settle back.
3. **Commit:** `Bring(kind, Ray)`.

Guards (all from the existing grab code):
- only while `App.IsPresenting`, not in MR, not in arrange mode (arrange has its own ray grab), and with no rig held;
- not within 1 s of playing (`PointerInput.IsPlaying`);
- not with the palm toward the headset (`SystemGestureLike`);
- not with the hand inside any play zone or the menu zone.

A summon pinch is consumed: it never starts an in-play grab or the MR placing.

**Desktop:** a left click on a displayed instrument's pick collider (`Layers.PlaySurface`) brings it.

### 4.5 The glide: `Layout/RigGlide.cs`

**Path:**
- lift 5 cm (0.15 s);
- then travel on a horizontal arc about the head at a radius of at least 0.75 m, never crossing the head's 0.6 m
  sphere (sampled every 2 cm in `GlideTests`);
- then descend onto the target pose.

Yaw is slerped, with ease in-out on arc length. Duration `0.9 s + 0.1 s per metre beyond 2 m`, at most 1.4 s. When
the set changes, the outgoing instrument starts 0.15 s before the incoming one.

**While gliding:**
- the instrument is not present (no Tick) and the planner treats it as absent;
- its sounding notes may ring on (realistic) and its sources follow it;
- tools held by the player are released first (`IHandToolProvider.ReleaseAll`).

**Sound:** a soft cloth swish (`IAudioEngine.Swish`, the page-turn sound), once per bring.

**On arrival:** `IStowable.SetStowed(false)`, `Rig.InScene = true`, `RefreshZone`, then `InstrumentMoveEvent { Kind,
Phase = Arrived, Reason }`. Departure publishes `Leaving`, and the end of a put-away publishes `Displayed`.

**Unchanged:** `TurnEvent` is still published for page turns. A lesson whose instrument leaves is stopped. The first
arrangement, a recentre and the two-hand summon still re-place the set at once, as today.

**MR** keeps today's 0.35 s slide + scale page turn: there is no visible display spot to come from.

### 4.6 Hand ownership, focus and tools

**Pointer claims** (Core, new step 3b after `Input.Update`; `Core/Input/PointerClaims.cs`). For each active hand
pointer, `Pointer.ClaimedBy` is set to the first of:
1. a present, non-contactless instrument in `TickOrder` whose rig `Zone` contains the tip;
2. the shown menu, if its `GrabZone` contains it;
3. Sunny (`IPointerClaimant.ClaimsPoint`, the poke sphere);
4. otherwise null.

Cost: about 10 pointers × 6 zones of tests (≈ 5 µs). Contact instruments ignore claims: their own surface tests decide.
The claims serve contactless instruments and the gesture code.

**Focus** (Layout, step 17 next to the swivel). The head's flattened forward against the slot headings gives
`ILayoutService.Facing`:
- the at-seat instrument whose slot is within ±30° of the gaze, with 10° hysteresis and 0.3 s dwell;
- in One alone, `Facing = Current` always.

`FocusEvent { Kind }` is published on change. Used by the theremin, the governor (section 10.4), FMM and Sunny.

**Contactless rule.** A `Contactless` instrument plays from a hand only when all of these hold:
- the hand is unclaimed;
- the instrument is `Facing` (in My set);
- the hand has not played another instrument in the last 0.4 s (`HandState.LastInstrument`, `LastPlayMs`).

So reaching for the piano never makes the theremin sing.

**Tools.** `Pointer` gains `ToolKind Tool` and `int Slot`, a dense stable index for per-pointer state tables. Every
tool pointer is registered with `Input.RegisterExternal` as today.
- **Pointer lists:** Core builds one pointer list per instrument (hand pointers plus the tool pointers whose `Tool` is
  in the descriptor's `Tools` mask). The lists are rebuilt only on registration, never per frame, and replace
  `AcceptsStickPointers`.
- **The kit's mask is `Stick`.** A stick on the marimba needs the marimba's mask to include `Stick`; the marimba agent
  decides.
- **The provider contract:**

```csharp
public interface IHandToolProvider {
    InstrumentKind Owner { get; }
    float NearestFree(Vector3 tip);                    // metres to the nearest free tool's grip, +inf if none or the owner is not at the seat
    StickPick TryPickUp(HandState hand, Vector3 tip);  // as IDrumsApi.TryPickUp: None / Near / Grabbed
    bool AnyHeld { get; } void ReleaseAll();
    int ShadowSpheres(Span<Vector4> centreRadius);     // E3 contact shadows of held tool tips
}
```

- **Pick-up order.** Layout's grab pass asks every provider for `NearestFree(tip)` and calls `TryPickUp` on the nearest
  only, keeping today's `Near` / `Grabbed` and 0.02 m handle rules.
- **The kit.** Phase 0 adds `DrumToolProvider` (Drums folder), a thin adapter over the existing sticks; `IDrumsApi`
  stays.
- **Tools that leave.** Tools of an instrument that leaves the seat are released and laid on it (`SetStowed`).
- **Fast Motion Mode.** FMM's `AnyStickHeld` becomes "any tool held of a `FastMotion` instrument".

### 4.7 Persistence (`AppSettings`, the injected store)

| Key | Value | Notes |
|---|---|---|
| `musicroom.layoutMode` | "all" \| "single" | unchanged |
| `musicroom.current` | any registry id | unknown or unloaded → "piano" |
| `musicroom.set` (new) | "piano,drums,harp" | My set in planner order. Unknown ids are dropped; a set that no longer fits is re-planned by `join()` from its first member. Default when missing: "piano,drums,harp" (so today's users see no change) |
| `musicroom.arrange.all` | ArrangementCodec "a1;Piano:…;Handpan:…" | **merge on save:** entries of rigs not in the current set are kept, so an instrument returns to where the player put it. A saved pose that now collides is dropped and the planner pose used (`ArrangementCodecTests`) |
| `musicroom.arrange.single.<id>` | as today | for every id |
| `musicroom.stow.<id>` (new) | "x,z,yaw" (room) or "" | a displayed instrument the player moved in arrange mode (VR) |
| `musicroom.<id>.*` | instrument-owned | e.g. `musicroom.theremin.range` |

Migration needs no code: the old keys keep their meaning.

---------------------------------------------------------------------------------------------------------------------------

## 5. Menu

### 5.1 The Instruments screen (`MenuController.InstrumentRows` replaced; canvas 1024 × 768, 0.3125 mm per pixel)

```
 y   0-110   [< Back]  Instruments                          header note: "With you: Piano, Drums, Harp"
 y 128-338   [ Piano ][ Harp ][ Theremin ][ Singing bowls ]       4 tiles, x 32 + i * 244, 226 x 210 px (71 x 66 mm)
 y 358-568   [ Handpan ][ Marimba ][ Hand drums ][ Drums ]
 y 590-700   [ My set | One alone ]   [ Lock ]   [ Classic sound ]   (segmented pair 456 px; buttons 228 x 110 px)
```

**Tile layout.** Each tile has:
- the portrait (section 5.2), 226 × 150 px;
- the name, at 34 px;
- a status line, at 22 px: "With you" at the seat; the display place ("Front wall", "By the lamp", "Left wall",
  "Corner", "By the window", "On the sill") so the player can find it in the room; "Put away" in MR;
- in My set, a printed tick box in the top-right corner (cream square, dark tick) on members.

Tiles of unloaded instruments are not drawn, and the grid closes up in carousel order.

| Poke | One alone | My set |
|---|---|---|
| a tile at the seat | nothing (it is already here) | takes it out of the set (it goes back to its place); the last member stays: "One stays with you" |
| a tile on display | it comes alone (`Bring(Menu)`) | it joins if it fits; if the set is full or it does not fit, it takes the place of the member `join()` picks, and the note names it for 2 s: "Harp went back to the corner". If nothing fits: "No room: take one out first", with a soft wooden knock (`Ui` click), never an error buzz |
| My set / One alone | switches mode, keeping the instrument the player faces (or the last played) | same |
| Lock, Classic sound | today's rows, as buttons | |

**Redraws, hover and events.**
- Redraws stay "≤ 1 per user action" (ARCHITECTURE 15.6). Hover is the face shader's `_HoverRect`.
- Each tile poke publishes `MenuRowEvent { RowId = "inst.<id>" }` (the sunny-tour row ids, extended per instrument).
- The **Home row** "Instruments" shows the set ("Piano, Drums, Harp") or "Handpan alone".

### 5.2 Realistic small icons: portraits (`Core/Render/PortraitStudio.cs`)

**What.** A photograph-like portrait of each instrument's own model, rendered once at runtime into one
1024 × 512 RGBA32 atlas (8 cells of 256², 2 MiB) that the menu face samples.

**When.** After `FirstArrangeDone`, one portrait per frame while the start panel is up: 8 frames, about 0.3 ms GPU
each (UNVERIFIED).

**How.**
- A disabled camera with `stereoTargetEye = None`, culling mask layer 30 (`Hidden`, which the head camera never
  draws).
- Each rig's renderers move to layer 30 for one `RenderPipeline.SubmitRenderRequest(cam, StandardRequest { destination
  = cell RT })`, then move back. A static `PortraitStudio.Rendering` flag lets modules skip work meanwhile.
- The camera uses the descriptor's `Portrait.From` / `At` / `Fov` in rig-local space.
- Background: the panel's paper colour.
- Light: a warm studio key (#fff1dc, 1.1, from the upper left front) is set as `RenderSettings.sun` for the request
  only. Ambient stays the room's.
- Displayed instruments are rendered in their display configuration; the bowls with their stand shown.

**Fallback.** If the request fails, or under `-nographics`, the descriptor's vector `IconStrokes` are drawn with
`MenuIcons`' stroke rules. That is also the headless test path.

**Device risk.** The menu already moved from a camera request to a command buffer for its canvas (ARCHITECTURE 19
item 28). If the request misbehaves on the Quest, the fallback is the vector icon (device item 49).

### 5.3 The Lessons screen
The same tile grid, holding the instruments with at least one lesson, each with the note "N lessons". Poking a tile
leads to the course list (unchanged). Starting a lesson calls `Bring(kind, Lesson)` first (section 7.3).

---------------------------------------------------------------------------------------------------------------------------

## 6. Recorder, takes and looper

### 6.1 Event types (each descriptor's `Events`; `IInstrumentRegistry.RoleOf` / `OwnerOfEvent`)

| Role | Meaning | Recorder | Looper | Lessons / Sunny |
|---|---|---|---|---|
| Onset | a note or hit starts | recorded | timing-tidy (quantise) allowed; starts a layer | hits |
| Release | a note ends (pairs by `Key`) | recorded | a held note gets its Release at the window end | – |
| Control | a continuous parameter (section 6.3) | recorded | never quantised; its lane is closed at the window end | – |
| State | a pedal or switch (hi-hat, damp pedal) | recorded | sampled at layer start (as the hat today) | – |
| Marker | a judgement, not a sound (the theremin "held a pitch") | recorded, never played back | dropped | lesson hits, tip success |

The classic types keep their strings: `pianoOn` Onset/Midi, `pianoOff` Release/Midi, `drum` Onset, `drumChoke`
Release/Kind, `hat` State, `harp` Onset, `harpDamp` Release/Index.

**Starting set for the new instruments.** Their agents may add more, prefixed with the instrument's id:

| Instrument | Types (role, key) | Fields used |
|---|---|---|
| Handpan | `handpan` (Onset, Index), `handpanMute` (Release, Index) | `i` tone field, `midi`, `vel`, `pos` 0 centre … 1 edge, `zone` "ding" \| "tone" \| "gu" \| "rim" |
| Hand drums | `handDrum` (Onset, Kind), `handDrumMute` (State, Kind) | `kind` drum id, `zone` "open" \| "slap" \| "bass" \| "mute" \| "tip", `pos`, `vel`, `open` (pressed) |
| Marimba | `marimba` (Onset, Midi), `marimbaDamp` (Release, Midi) | `midi`, `vel`, `pos` along the bar, `mode` "mallet" \| "stick" \| "finger" |
| Theremin | `thereminOn` (Onset, Lane), `thereminCtl` (Control, Lane), `thereminOff` (Release, Lane), `thereminHold` (Marker, Midi) | `lane`, `u` pitch (fractional MIDI), `vel` level 0…1, `speed` glide seconds, `midi` for Hold |
| Singing bowls | `bowlStrike` (Onset, Index), `bowlRubOn` / `bowlRubCtl` / `bowlRubOff` (Onset / Control / Release, Lane), `bowlDamp` (Release, Index) | `i` bowl, `lane`, `vel` rub speed or strike, `u` pressure, `pos` height on the wall, `mode` "mallet" \| "knuckle" |

`TakeEvent` gains one field, `int lane` (default 0). JsonUtility reads old takes as lane 0. Instruments still call
`app.Get<IRecorder>().Add(TakeEvent…)` for every play and stamp `LastPlayMs`. Phase 0 adds a helper,
`PlayStamp.Mark(HandState hand, InstrumentKind kind)`, which also sets `HandState.LastInstrument`.

### 6.2 Generic playback
```csharp
public interface IInstrumentPlayback {
    InstrumentKind Kind { get; }
    bool Play(in TakeEvent e);          // a recorded event of one of its types: sound + the visual it would have made; false if unknown
    void ReleaseAll();                  // every playback-held note up (take stop, looper clear)
    void CloseLane(int lane);           // end a continuous lane now (the looper's window end, a take's stop)
}
```

**Wiring.** `PlaybackWiring.Wire` keeps the seven classic `SetTarget` lines. Then, for every registered
`IInstrumentPlayback` and each of its descriptor's non-Marker types, it adds `recorder.SetTarget(type, e =>
pb.Play(e))`. The closures are created once at wiring time, never per event. `SetReleaseAll` calls every
`ReleaseAll`. A missing instrument drops its events, as today.

**Displayed instruments play back in place.** A loop layer of drums keeps playing while the player plays the marimba
alone, and the sound comes from the kit on the left wall. When a playback event is dispatched for a displayed
instrument:
- its unit wakes (any command does);
- its sources resume about 250 ms ahead: the looper's look-ahead is 80 ms, so the registry's
  `AudioPresence.Wake(kind)` is called on the first dispatch within 0.3 s, and takes are pre-scanned at Play;
- its visual moves (keys, beaters) only if `IStowable.Stowed` allows: the classic three animate, new instruments may
  skip it.

This is the only time a displayed instrument uses CPU.

### 6.3 Continuous control: `Core/Recording/ControlEncoder.cs` (reference `control-decimator.mjs`)

**Encoder.** One encoder per lane, run once per frame by the instrument:
- `On` when the level rises above 0.01;
- `Ctl` on the first frame where the pitch moved ≥ 0.02 semitone (2 cents) or the level ≥ 0.5 dB since the last sent
  value, and ≥ 1/30 s since the last event; or a keyframe every 0.25 s;
- `Off` after 60 ms below 0.008, preceded by a final `Ctl` at level 0.
- `speed` = seconds since the lane's previous event (the glide length), capped at 0.25.

**Decoder (the unit).**
- Each `Ctl` glides linearly from wherever the value is to the event's value over `speed`.
- **Playback dispatches a `Ctl` `min(speed, 0.05)` early**, so the glide ends when the value was recorded. The looper's
  look-ahead is 80 ms and the take playback's is the same, so this is free.

**Vectors** (`control`). On the 10 s test gesture (seeded jitter, a slow glide A3 → A4, a 5.5 Hz ±30-cent vibrato, a
fall):
- 242 events, 28.6 per sounding second;
- reconstruction error 1.74 cents rms, 8.67 cents max (the vibrato peaks) and 0.41 dB;
- events per section: swell 30, glide 90, vibrato 89, fall 33;
- the first 12 events and the last 3 are in the file.

The C# implementation (double maths, the same order) must reproduce the event list exactly and the error within 1e-6.

**Why these numbers.** At a 20 Hz cap the vibrato peaks lose 14.9 cents (measured), which flattens the vibrato audibly.
30 Hz keeps them within 9 cents. The deadbands keep a still hand at 4 events/s (keyframes only).

### 6.4 Looper rules (u-looper's `Looper` with registry roles)
- `IsOnset(type)` becomes `Registry.RoleOf(type) == Onset`. Tidy (quantise) applies to Onsets only.
- **Held notes.** At a recording window's end:
  - every open Onset with a Release key gets its Release (generalising `Open[midi]`, keyed by `Key` + value);
  - every open Control lane gets a `CloseLane` event (the encoder's final `Ctl` + `Off`).
- **Lanes.** Live play uses lane 0. Layer k plays back on lane 1 + (k mod 3). Continuous units support at least
  4 lanes (live + 3 playback).
- Markers are dropped from layers.
- State events are sampled at layer start (the hat rule, generalised: the descriptor marks a State type as "sampled";
  the instrument's `IInstrumentPlayback` must accept a synthetic State at offset 0).

### 6.5 Capacity
- `Recorder.MaxEvents` rises from 20000 to 40000. Two continuous lanes at 29 events/s for 10 minutes make 35000.
- `MaxSeconds` stays 600.
- Takes are serialised off the main thread (JsonUtility is safe on background threads, UNVERIFIED on IL2CPP, device
  item 52). Size is about 170 B per event, so a full take is 6.8 MB.

---------------------------------------------------------------------------------------------------------------------------

## 7. Lessons

### 7.1 Lesson packs
- **Files.** Each instrument ships `Resources/LessonPacks/<id>.json`, with the same `LessonFile` schema as
  `lessons.json` (`tutorial` constants for this instrument, `lessons[]`).
- **Loading.** `LessonLibrary` loads `lessons.json` (the classic 36 lessons proposed in tutorials-audit.md) and every
  `Resources.LoadAll<TextAsset>("LessonPacks")`. It orders the lessons by carousel, then file order, and recomputes
  `counts`.
- **Validation.** A lesson whose `instrument` is not in the registry is dropped and logged. An id clash fails
  `LessonPackTests`.
- **New `LessonTarget` fields** (all optional, JsonUtility defaults): `zone` (hand drums, handpan), `mode` (bowls
  "strike" \| "rub"), `hold` (seconds, theremin), `cents` (tolerance, theremin, default 35).
- **Packs are generated** by each instrument's own lab tool (like the tutorials audit's `lessons-src.mjs`) and copied in.

### 7.2 Generic tutorial path
`Tutorial` keeps its three classic code paths (tested, `TutorialTests`). For any other instrument it uses:

```csharp
public interface ILessonTargets {
    InstrumentKind Kind { get; }
    bool Validate(in LessonTarget t, out string why);                                     // pack checks (EditMode)
    bool TryGetLanding(in LessonTarget t, out Pose landing, out Vector3 approach, out Vector2 size);  // world; block travels along approach
    bool Matches(in LessonTarget t, in TakeEvent e);                                      // a live (not playback) event hits it
    void ShowTarget(in LessonTarget t, float k);                                          // the instrument's own cue, k 0..1 (no glow: a warm tint, a lifted bar)
    string Hint(IReadOnlyList<LessonTarget> targets);                                     // "Tap D", "Slap the low drum", "Hold A"
}
```

Block behaviour on this path:
- shape from `LessonSpec.Shape`;
- travel and lead from `LessonSpec`;
- colour from the pale warm palette of the tour decision (no cyan);
- one `RenderMeshInstanced` draw as today.

Fixes the audit asked for, applied on the generic path from the start:
- B2: a re-hit of a target already hit in the event is not a mistake;
- B4: the summary uses `CountNoun`;
- B6: after 8 s waiting, Sunny's re-demo; after 15 s, any hit on the instrument is accepted.

**Theremin lessons** use `thereminHold` Markers. The theremin emits one when the pitch has stayed within `cents` of a
semitone for 0.25 s at a level ≥ 0.2, and `Matches` compares its `midi`. `hold` > 0.25 asks for a longer hold.

### 7.3 Bringing the instrument
Starting a lesson calls `Bring(kind, Lesson)`, which joins or replaces as section 4.2. The countdown starts when
`InstrumentMoveEvent{Arrived}` arrives; the glide (≤ 1.4 s) runs inside the 3 s countdown.

### 7.4 Content rules for every pack
- At least 3 lessons. The first is a guaranteed win: one target at a time, 12–21 s, easiest first (tutorials-audit 0).
- Public-domain melodies only, with their source named in the agent's spec. The app never shows lyrics.
- At most 2 targets per event (two hands).
- Tempo: shortest gap ≥ 0.2 s, same-target repeat ≥ 0.25 s (tutorials-audit 4). Theremin holds ≥ 0.5 s.
- Every lesson under 1 minute.

---------------------------------------------------------------------------------------------------------------------------

## 8. Sunny

### 8.1 Tips are data in each descriptor
```csharp
public sealed class TipSpec {
    public string Id, Title;                   // "tip_handpan_tap", "Tap the handpan"
    public string[] Lines; public string Again;  // voice line ids (section 8.2)
    public TipTrigger Trigger; public string TriggerArg;   // FirstArrival | FirstDisplayedGaze | IdleNear | Signal ("<id>.<signal>")
    public TipClass Class;                     // P1 / P2 / P3 (sunny-tour 3.1)
    public DemoKind Demo; public string Anchor; // IGuideAnchors id, e.g. "field.ding"
    public TipSuccess Success; public string SuccessArg;   // EventType (a TakeEvent type, live) | Signal | None
    public string[] Covers;                    // feature ids
}
public struct InstrumentSignalEvent { public InstrumentKind Kind; public string Signal; }   // "handpan.mute", "theremin.power"
```

**Signals.** Instruments publish `InstrumentSignalEvent` for gestures that are not take events (a power switch, a tool
pick-up), so no Core enum ever grows per instrument.

**Every playable descriptor must have** (`InstrumentTipsTests`):
- one **P2 arrival tip**, triggered by the first `InstrumentMoveEvent{Arrived}`, success = its first live Onset;
- one **P3 technique tip** per special gesture.

| Instrument | Arrival tip (≤ 12 words) | Technique tips |
|---|---|---|
| Handpan | "Tap the notes with your fingertips." | rest a hand to quiet a note |
| Hand drums | "Hit the drums with your hands." | slap near the edge for a sharp sound; palm in the middle for a deep one |
| Marimba | "Pinch a mallet to pick it up." | hit the middle of a bar |
| Theremin | "Move your right hand closer to raise the pitch." | raise your left hand to play softer; face it to play |
| Singing bowls | "Tap a bowl with the striker." | pinch the stick and circle the rim to make it sing |

The texts are suggestions; the instrument agents own the wording within sunny-tour 6.1's rules.

**Platform tips** (Guide, phase 0 data):
- `tip_summon_far` "Point at an instrument and pinch to bring it here." (P3, VR, ≥ 90 s after the tour, a displayed
  instrument in view for 3 s);
- `tip_my_set` "Open Instruments to choose which ones sit with you." (P3, after the first ray summon);
- `tip_swipe` (existing) unchanged.

Sunny's deck tips use "on the side table", never "on your left" (the table may have yielded, section 4.2).

**Before the full tip system exists** (sunny-tour build, step 5 of the plan), phase 0's Guide shows each instrument's
arrival tip through u-arrange's `Sunny.Tip(lineId)`, once (`musicroom.tipsSeen`). So the instrument clones can test
their first line.

### 8.2 Voice lines live with the instrument
- **Files.** An instrument adds `Resources/Voice/tip_<id>_*.wav` (22.05 kHz mono, the sunny-tour 5.4 pipeline, never
  hand-edited) plus `Resources/VoiceManifests/<Module>.json` (`{ id, seconds }`) and `Resources/GuideLines/<Module>.json`
  (`{ id, text }`).
- **Loading.** `GuideVoice` (Audio) and `GuideLines` (Guide) merge every manifest and line file
  (`Resources.LoadAll<TextAsset>`) with their own. A duplicate id is an error.
- **Rendering.** Voices are rendered lazily (sunny-tour 7.1: at most 24 resident, LRU, about 12 MB). The bank's
  `SlotCount` grows to 512, so the voice slots 200..511 hold 312 lines.

### 8.3 Demo kinds added in phase 0 (Guide, `SunnyDemo`)

| Kind | Ghost-hand pose (3 s loop) |
|---|---|
| Tap | fingertips together, two light 2 cm taps |
| Slap | open palm, a 5 cm slap at the edge, then the centre |
| MalletStrike | a held mallet: two 6 cm strokes |
| Approach | the right hand moves 25 cm toward the anchor and back |
| Circle | a pinched stick circles the anchor's rim (r from the anchor's `up` length) |

Existing kinds (Press, Strike, Sweep, Pinch, …) are reused where they fit.

### 8.4 Anchors
`IGuideAnchors.TryGetAnchor(anchor, out world, out up)`. Each instrument documents its anchor ids in its README.
These are the tip demo targets and the lesson stall re-demo targets (B6).

### 8.5 Coverage
Every descriptor `Features` id is covered by a tip or marked Passive with a reason. Every tip's lines exist
(manifest, WAV, text). Lines are lint-checked: ≤ 12 words, ≤ 4.0 s, no brand words. `SunnyCoverageTests` (sunny-tour
6.2) runs over `Registry.All[*].Tips` as well as `TipBook`.

---------------------------------------------------------------------------------------------------------------------------

## 9. Mixed reality

1. **Entering MR:**
   - displayed instruments are hidden (`Group.SetActive(false)`), and their units and sources sleep;
   - the ray summon and display arrange are off;
   - the tiles read "Put away" and still bring instruments: they slide in at the seat, with no glide.
2. **The lead instrument** replaces "the piano" in today's MR flow (§12 of ARCHITECTURE). It is the set member in
   slot Front, else the first member in carousel order.
   - Auto-arrange, `StartPlacing`, `PlaceAroundLead` (today's `PlaceAroundPiano`: the other members at their planner
     offsets from the lead), the light target and the shadow catcher all use the lead.
   - `IMixedReality.TryGetPianoPose` becomes `TryGetLeadPose(kind, out pose, out onTable)`.
3. **Real tables.** A lead with `Mr.OnTable` may stand on a real table:
   - lifted by `SurfaceLift` (piano 0.092, handpan 0.04 on its cradle, bowls 0, theremin 0 with its legs hidden);
   - `IInstrumentSurface.SetOnSurface(true)` hides its stand.
   - A floor-only lead (drums, harp, marimba, hand drums) stands on the floor in front of the player.
4. **E9 anchors** (`PianoAnchorStore`) stay piano-only. Another lead is placed fresh each visit.
5. **The swivel is off in MR**, so the planner gives the rear slots cost +100 there (in practice: not used). An MR set
   that needs a rear slot is re-planned without it; if it no longer fits, its last member is put away, with the note
   "Put away: no room here".
6. **Draw guard:**
   - `Σ DrawsAtSeat(set) + 37 (MR fixed) ≤ 80`, else the last member is put away;
   - worst today: piano + hand drums + drums = 74 (`budgets.draws.worstMr`);
   - shadow casters stay the at-seat instruments' bodies only (`Rig.Handles`).
7. **The theremin in MR** plays as in VR. Passthrough hands (E6) do not change the hand data.

---------------------------------------------------------------------------------------------------------------------------

## 10. Audio engine: five new roles

### 10.1 Roles, sources, Classic sound

| Role | Sources (rig-local position, set by the instrument with `SetSourceParent`) | Reverb send VR / reach / ER | Notes |
|---|---|---|---|
| Handpan (5) | `Handpan`: the pan's centre | 0 dB / 0.7 / 0 | one source: the pan radiates as one body |
| HandDrums (6) | `HandDrumLo`, `HandDrumHi`: each head | −3 / 0.7 / +3 | like the kit's pieces |
| Marimba (7) | `MarimbaLo`, `MarimbaHi`: ±0.35 m along the bars, bar height | 0 / 0.7 / 0 | two keep the 1.2 m width audible |
| Theremin (8) | `Theremin`: the cabinet speaker | −3 / 0.7 / 0 | a real theremin is heard from its speaker, not the antenna |
| Bowls (9) | `Bowls`: the bowls' centre | +2 / 0.7 / 0 | |

Rules:
- Every new source is spatial (`MetaXRAudioSource`, HRTF calibration +2.4 dB). It has Meta acoustics on: it plays in
  Meta's shoebox under Classic sound, and the room unit turns acoustics off on the new path. Voice pools are 0
  (unit-only).
- **Unit-only roles.** There is no classic synth for the new roles. `ClassicSources(role)` is empty, the Classic
  switch never suspends them, and `ActiveUnit(role)` always returns the unit.
- **No new `IAudioEngine` methods.** Each instrument talks to its unit through `audio.ActiveUnit(UnitId.X) as IXSound`,
  an interface declared in its unit folder (ENGINE-API 4.4).
- **Test tap.** `AudioTestTap` gains `AudioCall.Unit` with `Unit` and `Op` fields. New units call
  `TestTap?.RecordUnit(Id, op, index, vel, when)` in every public method, so instrument tests assert sounds as today.
- **Source pausing** (`AudioPresence`, Audio, main thread):
  - a displayed instrument's sources pause (`AudioSource.Pause`) once its unit has output nothing
    (`UnitSlot.ChannelsUsed == 0`) for 1 s;
  - they resume at the start of a bring glide, or ≥ 250 ms before a playback dispatch;
  - at-seat sources never pause, because unpausing costs a block and the spatializer may ramp.
  - The graph is triggered by any active source and the master, and the `Ui` and `Guide` sources never pause.
  - Active instrument sources: at most 13 at the seat (piano 3 + drums 8 + marimba or hand drums 2) + DrumRoom, against 18 sources in total today.

### 10.2 Shared-file changes (phase 0 only; the instrument agents never touch these)
- `UnitSlot.cs`:
  - `UnitId` 5–9, `Count = 10`;
  - `static partial void RenderHandpan/RenderHandDrums/RenderMarimba/RenderTheremin/RenderBowls(UnitSlot* u)` and
    their switch cases;
  - new fields `float Share` (main → audio, default 1), `byte Class` (Focus 3, AtSeat 2, Playback 1, Tail 0),
    `int Work` (the unit's own work count for the block), `int IdleBlocks`;
  - `UnitFlags.Resumed = 2`.
- `SoundUnit.cs`:
  - `static partial void CreateHandpan(…)` × 5 and their switch cases;
  - `Roles` holds all nine;
  - `Register(…, bool idleSkip = false)`;
  - `UnitHandles` 4-bit (section 2).
- `AudioGraph.cs` (idle skip): a unit registered with `idleSkip` skips its Render while it has no due or pending
  events, no fade, and `ChannelsUsed == 0` for 8 blocks (43 ms). Its first Render after a skip gets `Resumed`. Banks
  and one-shots are culled by the graph as today.
- `AudioRig.cs`: `AudioSourceTable` (one row per `SourceId`: spatial, acoustics, send dB, reach, ER dB, pools, room
  group) replaces the range checks. The classic values are unchanged, and a test pins them.
- `RoomModel.cs` / `RoomUnit.cs` (the room workstream's files; the minimal change):
  - `TapSources` = every instrument source of the table (12 → 19);
  - `MaxErGroups` 3 → 4 (one more early-reflection group, measured cost +63 µs per block in Node, about +6–60 µs on
    the Quest);
  - `GroupOf` becomes a per-tap group index (−1 = late reverb only) and a group point, both written by the main thread
    through the room's existing pose seqlock.
  - `RoomGroupAssigner` (Audio, main thread, per frame) gives the 4 groups to the at-seat set (slot order), then to
    displayed instruments that sounded in the last 2 s, by recency. A change crossfades over the room's existing
    2-block snap.
  - The group point is the instrument's `Audio.ErOrigin` (rig-local; the harp's soundbox −0.35 m, the kit's −0.2 m,
    as room.md).
- `GuideVoice.cs`: manifests merged; `SlotCount` 512 (section 8.2).
- `IAudioEngine.cs`: the `SourceId` values; `AudioCall.Unit`.

### 10.3 What every new unit must meet (conformance, before its merge)

| Check | Limit | How | Lesson from the critics |
|---|---|---|---|
| CPU, Quest Burst estimate | handpan, hand drums, bowls ≤ 0.15 ms avg / 0.25 worst block; marimba ≤ 0.20 / 0.30; theremin ≤ 0.06 / 0.10 | Node reference ≤ 10× the cap (burst.md 10.2: Quest ≈ 0.10 × Node); `[PERF]` EditMode (ENGINE-API 6.4) | |
| Budget scaling | reads `u->Graph->Budget / 1200f × u->Share` at block start and scales its own caps | `UnitShareTests`: Share 0.4 lowers `Work` ≥ 50 % | |
| No low-frequency push | energy < 60 Hz ≤ −20 dB re the hit; mean / rms ≤ 0.01 | modal output = pre-update state + c₀·input (drums.md H1) | drums H1 |
| No metallic tails from sparse modes | for wood and skin, the top 20 partials hold ≤ 35 % of the 0.3–9 kHz energy after 0.5 s; for metal (handpan, bowls), the highs die faster than the lows (no partial above 3 kHz outlives the fundamental's T60 × 0.8) | `analysis.mjs` share and T60 per partial | drums H2, harp |
| Not over-wet | the room send as the table; DRR at the seat within ±2 dB of the piano's +6.1 dB | room.md 10 | room R1 |
| Loudness parity | mf reference phrase, dry, `OfflineEngine.RenderSources` sum: within ±2 LU of the piano's mf reference (Ode to Joy bar 1, vel 0.55); ff true peak through MasterBusV2 ≤ −1 dBTP | `LoudnessParityTests` (BS.1770-4 in C#, following the lab's `lib/analysis.mjs`) | piano / drums: A/B pairs judged loudness-matched |
| Clicks | sample step ≤ the signal's own maximum step + 1 % at every onset, steal, release, idle resume, Classic switch, lane change | `NullTest.MaxStep` | |
| Denormals | flush below 1e-20 at block end; retire below −110 dBFS | `T-DENORM` | burst 6 |
| Late onsets | `CommandFlags.LateDrop` on onsets only | review | |
| Allocation | 0 B in `Tick`, the public API and the render | `Is.Not.AllocatingGCMemory()` | |
| Determinism | seeded; Node vectors bit-exact managed, 1e-5 under Burst | ENGINE-API 6.2 | |
| Memory | arena + baked ≤ 2 MiB arena / 4 MiB baked | `Stats` line | |

### 10.4 The governor: `Audio/Burst/UnitGovernor.cs` (reference `budget.mjs governor()`)

**Classes**, set from the registry every frame:
- **Focus:** the instrument played live in the last 2 s, or `Facing`. It never drops.
- **AtSeat.**
- **Playback:** sounding only through take or loop dispatch.
- **Tail:** displayed and still ringing.

**Adjusting.** Once a second, from `DspLoadMeter` p99 of the graph block:
- above 1.6 ms (80 % of the 2 ms target), the lowest class whose share is above 0.4 loses 0.15, all its units
  together;
- below 1.0 ms, the highest class below 1 gains 0.05.

**Measuring.** Units report `Work`, so the dev HUD shows each unit's share of the block. Per-unit timing inside the
Burst call (`ProfilerUnsafeUtility.Timestamp`) is UNVERIFIED and diagnostics only.

**Vectors** (`budgets.governor`). Cost model: 20 % fixed + 80 % scaled; p99 = 1.15 × average.
- *Busy worst:* drums played live at their worst (0.80), plus looped piano (0.60), marimba (0.20) and bowls (0.15),
  plus the harp at the seat (0.15). Starting at 2.68 ms p99, it settles in 8 steps at 2.07 ms p99, 39 % of the
  5.33 ms block. Shares: drums 1, all others 0.4.
- *Busy typical:* drums groove 0.35 with three typical loops. It stays at 1.33 ms, untouched.

---------------------------------------------------------------------------------------------------------------------------

## 11. Performance budgets with eight instruments (Quest 3S, 90 Hz under the governor of E5)

**Draw calls** (single-pass instanced; the per-rig allowances of ARCHITECTURE 14; `budget.mjs draws()`):

| | At the seat | On display (static LOD, combined per material) |
|---|---|---|
| Piano / drums / harp | 10 / 21 / 5 | 4 / 6 / 3 |
| Handpan / hand drums / marimba / theremin / bowls | ≤ 4 / 6 / 6 / 5 / 5 | ≤ 2 / 3 / 3 / 3 / 3 |
| Fixed, VR | room 37, table + deck 15, menu 4, Sunny 6, blocks 1, glows 1, hands 2, contact shadows 1, rig shadows 1 (was 3: one combined mesh for all 8), ray summon 2 = 70 | |

| Totals | Allowances | Budget |
|---|---|---|
| Default set, VR (piano, drums, harp + 5 on display) | 120 | 150 |
| Worst VR set (piano + hand drums + drums + 5 on display) | 121 | 150 |
| Worst MR set (same; nothing displayed; fixed 37) | 74 | 80 |

The allowances are ceilings. The measured head camera today is 24–29 draws (ARCHITECTURE 21, u-arrange), because the
SRP Batcher merges. `DrawBudgetTests` guards the real counts: whole scene ≤ 150; MR ≤ 80, run in the worst set.

**Triangles:** each new instrument ≤ 25 k at the seat and ≤ 8 k on display; the scene ≤ 300 k (ARCHITECTURE 17).

**Main-thread CPU** (our scripts ≤ 3 ms per frame; input + instruments ≤ 1 ms):
- each new instrument: Tick + Hover ≤ 0.12 ms while played and ≤ 0.02 ms present but idle; 0 on display;
- platform additions ≤ 0.04 ms: claims about 5 µs, focus 1 µs, ray summon 5 µs (2 hands × 8 cone tests),
  `AudioPresence` + `RoomGroupAssigner` + governor 10 µs, a glide 10 µs;
- the planner runs only on set changes (≤ 2 ms, once);
- the portrait studio runs once at boot (8 frames during the start panel);
- GC: 0 B per frame everywhere (`Is.Not.AllocatingGCMemory()` over 120 frames of the worst set).

**Audio thread** (target 2 ms per 5.33 ms block, CPU level 3):

| | Per block |
|---|---|
| The five new caps, worst, summed | 0.71 ms (avg caps) |
| All eight at their worst at once + room (4 groups) + overhead | 2.69 ms (never happens without the governor; it settles at ≤ 2.07 ms p99, 39 % of the block) |
| All eight typical at once | 1.44 ms |
| A displayed, silent instrument | 0 (idle skip, sources paused) |

**Memory:**

| Item | Size |
|---|---|
| New units | ≤ 2 MiB arena + ≤ 4 MiB baked each; the five ≈ 8.5 MiB as provisioned (`budgets.units`) |
| Textures | ≤ 3 MB ASTC each beyond the shared `Core/*` woods, drum head and cymbal; ≈ 9.5 MB |
| Display LOD meshes | ≈ 0.5 MB each |
| Portrait atlas | 2 MiB |
| Voice lines | LRU ≤ 12 MB (sunny-tour) |

Total added ≈ 35 MB against the app's 600 MB. Classic sound's 80 MiB of piano and harp buffers still appear only when
Classic is on.

**APK:** now 63.2 MiB. Expected +10–14 MB (baked textures, 5 × about 4 voice clips, code), against the 90 MB budget.

**Start-up:** the new units build their tables on `ctx.StartWorker` during the start panel. The first strike is ready
in < 0.15 s for the default set. A new instrument's first strike waits at most until its worker is done; a summon's
glide (≥ 0.9 s) covers it.

---------------------------------------------------------------------------------------------------------------------------

## 12. Tests

### 12.1 Platform tests (phase 0; vectors from the platform lab's `platform.vectors.json`, in `Tests/EditMode/Layout/Reference~/`)

| Test | Kind | Asserts |
|---|---|---|
| `RegistryTests` | EditMode | one descriptor per playable kind; ids, carousel, event types, tips and features unique; prefixes; `RigOf(kind) == (RigId)(int)kind`; `UnitId` / `SourceId` mapping matches `AudioSourceTable`; carousel order as section 4.3 |
| `RegistryLintTests` | EditMode | the source scan of section 3.4 |
| `UnitIdTests` | EditMode | 4-bit handles: all 10 roles, payloads 0, 1, 2^27 − 1; piano and harp payloads decode as today |
| `SeatPlannerTests` | EditMode | all 92 sets: fits, `tableAlt`, slot, push, pose (1e-4), reach, cost, candidate count; the default set at today's homes; each alone at its Single home |
| `JoinTests` | EditMode | the 8 `joins` vectors |
| `CarouselTests` | EditMode | `Next` ±1 for all 8; skipping unloaded instruments (`carousel.skip`) |
| `StowPlanTests` | EditMode + PlayMode | the 8 spots against Room's real `FloorBlockers`, each other and every fitting seat plan; seats at (±0.6, ±0.6) use spares or hide; ≥ 1.3 m |
| `RaySummonPickTests` | EditMode | the 24 `stow.rays` (aim, ±3°): picked id and angle |
| `ControlStreamTests` | EditMode | the gesture → 242 events, first 12 and last 3 exact; error 1.7365 / 8.672 cents, 0.4103 dB (1e-4) |
| `GovernorTests` | EditMode | `busyWorst` (shares, p99 2.0723) and `busyTypical` (1.334) |
| `PointerClaimsTests` | PlayMode | a fingertip in the piano zone is claimed by the piano; in the menu zone by the menu; a contactless stub ignores both; LastInstrument guard 0.4 s |
| `FocusTests` | PlayMode | head yaw sweeps: `Facing` with ±30°, 10° hysteresis, 0.3 s dwell |
| `SetLayoutTests` | PlayMode | default set: eye distances .628 / .529 / .74 / .603 unchanged; add a stub → joins its planned slot; full → replace LRU; set persisted and restored; merge-on-save of arrangements |
| `GlideTests` | PlayMode | durations; path ≥ 0.6 m from the head at every 2 cm; not present while gliding; `InstrumentMoveEvent` order; a lesson waits for Arrived |
| `RaySummonTests` | PlayMode | a scripted pointer pose at a stub on display + a 0.35 s pinch → it arrives; release at 0.3 s → nothing; guards (palm to face, playing, held rig, arrange mode, MR) |
| `DisplayedSilenceTests` | PlayMode | a displayed stub is not ticked; its unit idle-skips (`IdleBlocks` grows, `Work` 0) and its sources pause after 1 s; a take dispatch wakes it ≥ 250 ms ahead |
| `IdleSkipTests` | EditMode (OfflineEngine) | skipped after 8 silent blocks; the first event after a skip lands on its exact sample; `Resumed` set once |
| `AudioSourceTableTests` | EditMode | classic rows equal today's values; new rows unit-only with acoustics on |
| `RoomGroupsTests` | EditMode | 4 groups to the set; a 5th sounding instrument gets −1 (late only); the crossfade on reassignment ≤ 2 dB step |
| `LoudnessParityTests` | EditMode | the piano's mf reference measured; each unit's reference within ±2 LU (stubs exempt) |
| `MenuInstrumentGridTests` | EditMode + PlayMode | 8 tiles in carousel order, rects as section 5.1; poke actions in both modes; "No room" path; unloaded instruments hidden |
| `PortraitTests` | PlayMode Graphics | each cell non-blank; the instrument fills 40–85 % of it; mean luminance 0.25–0.75; fallback icons without a GPU |
| `LessonPackTests` | EditMode | packs load; instruments known; ids unique; `ILessonTargets.Validate` true for every target |
| `GenericTutorialTests` | PlayMode | a stub instrument's lesson: blocks land at `TryGetLanding`; hits from its events; the B2, B4, B6 rules |
| `LooperRegistryTests` | EditMode (u-looper's harness) | onsets quantised, controls not; a control lane closed at the window end; markers dropped; lanes 1 + (k mod 3) |
| `VoiceManifestMergeTests` | EditMode | lines from every module load; duplicates fail |
| `MrLeadTests` | PlayMode | a set without the piano: the lead placed on a table / the floor; displayed instruments hidden; the draw estimate ≤ 80 |
| `EightInstrumentsShotsTests` | PlayMode Graphics | the VR room with all 8 (default set + 5 on display): screenshots `Builds/Shots/platform/`; every displayed instrument visible from the seat (frustum + not fully occluded); ≤ 150 draws in the worst set |
| `EightInstrumentsLoadTests` | Performance | all 8 units at their declared worst, offline and live: avg / p99; the governor keeps p99 ≤ 2.1 ms; `[PERF]` lines |

### 12.2 The conformance suite (`Tests/PlayMode/Core/InstrumentConformanceTests.cs`, `TestCaseSource` = the registry)

Every playable instrument, stub or real, runs these with no code of its own:

1. `IsPresent` is false while displayed or gliding and true at the seat.
2. `Rig.Handles` is filled once in `Init`, is non-empty, and is the same list object on every frame.
3. The descriptor's `Seat.Boxes` cover the union of `Rig.Handles` bounds within 3 cm.
4. `DrawsAtSeat` and `DrawsStowed` are not exceeded (GPU run).
5. `Tick` / `Hover` / the public API allocate nothing over 120 scripted frames.
6. `SetStowed(true)` releases its tools, and nothing ticks.
7. Every declared Onset type is produced by a scripted strike or gesture fixture of the instrument (each instrument
   provides one fixture class `I<Kind>Fixture` in its PlayMode folder).
8. `IInstrumentPlayback.Play` of every declared non-Marker type makes a unit call (`AudioCall.Unit`) or a classic
   engine call.
9. `ReleaseAll` and `CloseLane` silence its playback.
10. Its unit registers, renders, idle-skips, and honours `Share`.
11. Its lesson pack validates, and lesson 1 completes with the fixture.
12. Its arrival tip's lines exist.
13. `IGuideAnchors` resolves every anchor its tips and lessons name.
14. Its sources follow its rig.
15. It works with `ActiveUnit` both classic and new (for the classic three).
16. The desktop click path does not double-fire (`Pointer.FromMouse`).
17. Arrange mode silences it.
18. A page turn to and from it.
19. A ray summon of it.
20. Its portrait or fallback icon draws.

### 12.3 Changes to existing tests (phase 0)
- Layout: `LayoutModeTests` now expects 8 carousel steps.
- Menu: `MenuScreenTests` and `MenuScreenshotTests` use the new Instruments screen.
- Lessons: `TutorialTests` unchanged (classic path).
- `AudioEngineTests`: 25 sources; `GuideVoice` slot count.
- Room: table and deck budgets unchanged; `RigShadows` is one draw.
- FMM: the policy vectors are unchanged for drums and add a FastMotion stub.

### 12.4 Device checklist additions (ARCHITECTURE 19, items 48+)
48. The 8 displayed instruments look right from the seat in the headset: scale, lighting by the lamp and the fire,
    nothing floating, the handpan on its bracket, the bowls on the sill. The theremin reads at once, front-left.
49. Portraits through `SubmitRenderRequest` render on the Quest (Vulkan, single-pass): upright, crisp, lit, and no
    effect on the next frame's head camera. Else the fallback icons.
50. Point and pinch:
    - the system pointer pose is steady at 2–3.5 m;
    - the cone pick picks what the player means (bowls vs hand drums, kit vs handpan);
    - the 0.35 s hold feels intentional, not slow;
    - the 2 cm preview lift is noticed.
51. Glide comfort: no vection or unease while a kit crosses the room at 1.4 s (if any, shorten the arc or fade the
    last 20 %).
52. Take serialisation off the main thread under IL2CPP (40000 events) and the STOP press hitch.
53. 25 sources: the "Meta XR Audio" profiler time with 13 instrument sources active, vs today's 18 total; pausing the
    displayed ones keeps it flat.
54. Theremin focus: facing ±30° feels natural seated; reaching to the menu or the piano never makes it sound.
55. The side table's alternate pose (behind-left) is reachable for LOOP / REC from the seat.
56. MR with a non-piano lead: the handpan or the theremin on a real table, the drums on the floor; nothing displayed
    leaks into passthrough.

---------------------------------------------------------------------------------------------------------------------------

## 13. Merge plan

### 13.1 Phases and bases

| Step | Clone | Base | Who | Lands |
|---|---|---|---|---|
| 1 | (merge) | the project | integrator | `u-arrange`, `u-looper` into the main project (their own merge notes) |
| 2 | `u-platform` | `platform-base` = the project after step 1 | platform agent | everything in 13.3, plus 5 stub instruments and the conformance suite, all green |
| 3 | `u-handpan`, `u-handdrums`, `u-marimba`, `u-theremin`, `u-bowls`, `u-sheet` | `instruments-base` = the project after step 2 (frozen copy) | five instrument agents + the sheet agent, in parallel | their folders only (13.2) |
| 4 | (merge) | the project | integrator | folder copies, `CONTRACT-REQUESTS.md` items, conformance + platform tests + device APK |
| 5 | – | – | – | the first-five-minutes and full Sunny tour build, covering every instrument |

Bases are frozen copies, never live projects. Each clone records its base's commit-free fingerprint (`find Assets -type
f | sort | xargs shasum | shasum`) in its README, so the integrator knows what it was cut from.

### 13.2 Folder ownership (step 3)

| Owner | Owns (may create, change, delete) | Never touches |
|---|---|---|
| Instrument agent `<X>` (Handpan, HandDrums, Marimba, Theremin, Bowls) | `Assets/MusicRoom/<X>/**` (its module, asmdef `MusicRoom.<X>`, definition, visuals, `Resources/LessonPacks/<id>.json`, `Resources/Voice/tip_<id>_*.wav`, `Resources/VoiceManifests/<X>.json`, `Resources/GuideLines/<X>.json`, `Resources/MaterialTemplates/<X>/**` from render-setup, README, `CONTRACT-REQUESTS.md`); `Assets/MusicRoom/Audio/Sound/<X>/**` (unit, DSP, state, its sound interface, README); `Tests/EditMode/<X>/**`, `Tests/PlayMode/<X>/**`, `Tests/EditMode/Audio/<X>/**` (incl. `Reference~/`); its lab folder, `Builds/Listen/<key>/`, `Docs/12-new-instruments/<key>.md`; `Builds/Shots/<x>/` | every other file. Shared needs go to `CONTRACT-REQUESTS.md` with the exact code (as the 10-upgrade clones did) |
| Sheet agent | `Assets/MusicRoom/Sheet/**` (asmdef `MusicRoom.Sheet`), `Tests/*/Sheet/**`, its lab and listen folders | the same |
| Integrator | everything else, the merge order and the requests | – |

Rules inside the owned folders:
- **Keep the stub's asmdef file and its `.meta` GUID.** Edit the asmdef in place; never delete and recreate it. The
  test asmdefs and `link.xml` reference it by name; other `.meta` GUIDs must also survive, or scene and template
  references break.
- **Implement exactly your two partial methods**, `Create<X>` and `Render<X>`, in your own unit files. The
  declarations exist (phase 0).
- **Names.** Material template and bake names are `<X>/<name>`. Settings keys are `musicroom.<id>.*`. Event types,
  tip ids, line ids, feature ids and lesson ids use your prefix (section 3.4).
- **Write your module `Order` and `TickOrder` as section 2.**
- **Tests.**
  - Boot with `TestBoot`.
  - For `Builds/Shots`, set `$MUSICROOM_SHOTS` to your own folder: the shared `Builds/Shots/integration` folder belongs
    to the integrator.
  - Never edit `IntegrationShotsTests`, `OfflineEngine`, `TestBoot` or another module's tests.
- **Machine.** One Unity per clone folder. With six clones on one Mac, run `test-play --gpu` and `test-perf` one clone
  at a time: the integrator hands out a lock folder, `gpu.lock`, which you take with
  `mkdir`.

### 13.3 Phase 0 file list (the platform agent; the only shared-file changes of the whole effort)

| Area | Files |
|---|---|
| Core | `Contracts/Instruments.cs` (enum values, `PlaySurface.Instrument`, `IStowable`, `IInstrumentPlayback`, `IGuideAnchors`, `IHandToolProvider`, `IInstrumentSurface`, `IPointerClaimant`), `Contracts/Rigs.cs` (`RigId`), **new** `Contracts/Registry.cs`, **new** `Registry/InstrumentRegistry.cs` + `BuiltIns.cs`, `Contracts/Lessons.cs` (`TakeEvent.lane`, `LessonTarget` fields, `ILessonTargets`, `EventRole` use), `Contracts/Guide.cs` (`TipSpec` types, `InstrumentSignalEvent`, `DemoKind` values), `Contracts/MixedReality.cs` (`TryGetLeadPose`), `Contracts/Room.cs` (`IRoomFurniture`), `Contracts/Rigs.cs` `ILayoutService` additions (with default bodies: `Set`, `CanAdd`, `TrySetSet`, `Bring`, `PutAway`, `ReplaceCandidate`, `PlaceOf`, `SlotOf`, `Facing`, `IsMoving`), `App/App.cs` (registry, claims step, per-instrument pointer lists), `App/AppSettings.cs`, `App/Events.cs` (`InstrumentMoveEvent`, `FocusEvent`, `LayoutChangedEvent.SetVersion`), `Input/Pointer.cs` (`Slot`, `Tool`, `ClaimedBy`), `Input/PointerClaims.cs`, `Input/FastMotionPolicy.cs`, **new** `Recording/ControlEncoder.cs`, **new** `Recording/PlayStamp.cs`, **new** `Render/PortraitStudio.cs`, `link.xml` |
| Audio | `Engine/IAudioEngine.cs`, `Engine/AudioRig.cs` (+ `AudioSourceTable`), `Engine/AudioEngine.cs` (roles loop, unit-only roles, `AudioPresence`), `Burst/Units/UnitSlot.cs`, `Burst/Units/SoundUnit.cs`, `Burst/AudioGraph.cs` (idle skip, Share, Work), **new** `Burst/UnitGovernor.cs`, **new** `Engine/RoomGroupAssigner.cs`, `Sound/Room/RoomModel.cs` + `RoomUnit.cs` (taps, 4 groups), `Synths/GuideVoice.cs`, `Offline/*` if the source count needs it; **new** `Sound/<X>/<X>StubUnit.cs` × 5 (a one-shot knock, so the stubs sound) |
| Layout | `LayoutModule*.cs` (registry, set, `Bring`, focus, display), **new** `SeatPlanner.cs`, `StowPlan.cs`, `RigGlide.cs`, `RaySummon.cs`; `PlacementSolver` (skip rigs that are not `InScene`); `ArrangementCodec` (merge on save); `LayoutModule.ArrangeMode.cs` (displayed instruments, lift ranges from the descriptor) |
| Menu | `MenuController.cs`, `MenuStyle.cs`, `MenuRow.cs`, `MenuPainter.cs` (tiles, portrait blit), `MenuIcons.cs` (descriptor strokes) |
| Lessons | `LessonLibrary.cs` (packs), `Tutorial.cs` (generic path), `LessonHints.cs`, `LessonStage.cs`, `PlaybackWiring.cs`, `Looper.cs` / `LoopSession.cs` (roles, lanes, `CloseLane`), `Recorder.cs` (40000) |
| Guide | `GuideLines.cs` (merged line files), `SunnyAdapters.cs` (registry), `SunnyDemo.cs` (5 demo kinds), `GuideModule.cs` (arrival tips via `Sunny.Tip`), `Resources/GuideLines/Guide.json` + the 2 platform tips (rendered clips) |
| MR | `MrModule.cs` (lead, hide displayed, MR draw guard) |
| Room | `Table/SideTable.cs` (`RigShadows` from descriptors, one mesh; the table's alternate pose), **new** `World/Furniture.cs` (`IRoomFurniture`) |
| Piano, Drums, Harp | **new** `<X>Definition.cs` each; `IStowable` + `IInstrumentPlayback` + `IGuideAnchors` adapters (new files); Drums **new** `DrumToolProvider.cs`; the one-line registrations in each module's `Init`; Piano `IInstrumentSurface` adapter |
| Stubs | `Assets/MusicRoom/{Handpan,HandDrums,Marimba,Theremin,Bowls}/`: asmdef, Definition (the provisional numbers of `registry.mjs`), a Module with a placeholder model (neutral wood boxes at the footprint), a tap-to-knock `IInstrument`, every interface of 3.3 minimal, 1 lesson in a pack, a text-only arrival tip; `Assets/MusicRoom/Sheet/` asmdef + an empty module |
| Tests | the asmdefs (6 names), section 12.1 and 12.2, the stubs' fixtures |
| Editor | `MusicRoom.Editor.asmdef` (6 names) |
| Docs | ARCHITECTURE.md sections 2, 3.4, 4, 7, 8, 9, 10, 12, 14, 17, 18, 19, 21 (a draft entry is in 13.6) |

### 13.4 Step 4: integrating the six clones
1. For each instrument clone, in carousel order (any order works; folders are disjoint):
   - delete the stub's folders in the project;
   - copy the clone's owned folders, keeping `.meta`;
   - check `git status`-style that no file outside them differs from `instruments-base` (`diff -rq` against the base).
     Any difference is a rule break: move it to a request.
2. Apply the `CONTRACT-REQUESTS.md` items. Each is a minimal default-bodied member or a data row, as the 10-upgrade
   integration did.
3. Run `compile → render-setup → bake → scene → test-edit → test-play → test-play --gpu → test-perf`. Expect the
   conformance suite × 8, `RegistryLintTests`, `DrawBudgetTests` and `EightInstrumentsLoadTests` to be green.
4. Build the APK; walk the device checklist.

**Conflict hot spots,** and why they cannot collide:
- the line tables and lessons are now per-module files;
- `UnitSlot` / `SoundUnit` partials are pre-declared;
- the asmdef names are pre-listed;
- Core contracts are pre-declared, and requests are applied by the integrator alone;
- `Builds/Shots` is per module;
- the sheet module touches only `IPianoApi.MusicRestPose` (phase 0 adds it, default `null`), plus `ITutorial`'s
  current-event index (phase 0 adds `ITutorial.CurrentEvent`, default −1).

### 13.5 The sheet-music hooks (phase 0 provides; `Sheet/` consumes)
- **Placement and notation:**
  - `IPianoApi.MusicRestPose` (world pose of the music rest's face, null when the piano is not at the seat);
  - `InstrumentDescriptor.SheetAnchor` and `Lessons.Notation`, for any future instrument's stand.
- **Lesson state:**
  - `ILessonLibrary` for the score;
  - `ITutorial` (`State`, `LessonId`, `Version`, `CurrentEvent`, `EventsDone`) and `TutorialEvent` for follow-along.
- **Registration:** `SheetModule` Order 45 registers no `Rig` and no `IInstrument`. It parents its page under the
  piano's rig. It obeys `Rig.InScene` (no page while the piano is displayed), a draw budget ≤ 2, and 0 B per frame.

### 13.6 ARCHITECTURE.md section 21 entry (draft, for step 4)
> **2026-11-xx, many-instrument platform (12-new-instruments/platform.md).**
> - Registry (`IInstrumentDefinition`, `InstrumentDescriptor`, `App.Registry`) with an id block for 8 instruments.
> - Displayed instruments in the VR room (`StowPlan`, `IStowable`).
> - My set (`musicroom.set`, `SeatPlanner`, up to 3).
> - `Bring` by menu tile, page turn and point-and-pinch (`RaySummon`), with glides (`RigGlide`).
> - Pointer claims, focus, the tool providers.
> - Generic recording (`EventRole`, `IInstrumentPlayback`, `ControlEncoder`, `TakeEvent.lane`, 40000 events).
> - Lesson packs + `ILessonTargets`; per-module Sunny tips and voice manifests.
> - MR lead instrument.
> - Audio: `UnitId` 5–9, 4-bit handles, 7 sources, unit-only roles, idle skip, `UnitShare` governor, 4 dynamic ER
>   groups.
> - Menu Instruments grid with runtime portraits.
>
> Every new interface member has a default body. Verification: section 20.x.

---------------------------------------------------------------------------------------------------------------------------

## 14. Risks and UNVERIFIED

1. **Footprints are provisional** for the five new instruments. The fit table will change when their agents replace
   them: the theremin's hand field and the marimba's length are the sensitive ones. The planner and its tests do not
   change, only the vectors (re-run `make-vectors.mjs` with the real boxes).
2. **Only 3 fit within reach.** No set of 4 fits with today's sizes. That is the honest result of a 2 ft reach plus the
   side table. If the user insists on 4, the levers are:
   - rear slots without the cost penalty (a big seated twist);
   - shrinking the side table into the menu (the deck as a panel);
   - a smaller handpan on the lap.
3. **The table's alternate pose** (behind-left, 0.67 m) may be awkward for LOOP and REC (device item 55). The fallback
   is to forbid sets that need it, which loses 29 of the 82 sets that fit.
4. **Portrait rendering** through a render request on the Quest (item 49). The fallback is the vector icons.
5. **Cone-pick ambiguity** between neighbours on the window wall at ±3° hand-ray error (1 of 24 vectors). The hover
   card shows the target before the pinch, and the cone could shrink to 4° if the device test says so.
6. **Spatializer cost of 25 sources** (item 53). Pausing displayed sources is the mitigation.
7. **A glide across the room** may cause vection for some people (item 51).
8. **The governor's floor** (0.4) can leave the busy-worst scene at 2.07 ms p99, above the 2 ms target. It is still
   39 % of the block, and the real limit is underruns.
9. **`ProfilerUnsafeUtility.Timestamp` inside Burst** for per-unit timing is diagnostics only. The governor relies on
   the whole-graph meter.
10. **JsonUtility on a worker thread under IL2CPP** (item 52). The fallback is the main thread, with the hitch moved to
    the STOP press.

## 15. Files

- `Tools/SoundLab/platform/registry.mjs`: descriptors as the platform sees them:
  slots, footprints, homes, the carousel, priorities, the table, the player.
- `geometry.mjs`: oriented boxes, SAT overlap, the ray-OBB test, pose rotation (Unity yaw).
- `seat-planner.mjs`: `plan`, `join`, `allSets`, `costOf`.
- `stow-plan.mjs`: furniture blockers, display spots, `validate`, `pickPoint`, the cone `pick`.
- `control-decimator.mjs`: the ControlStream encoder / decoder, the test gesture, error measurement.
- `budget.mjs`: draw, audio and memory model, the governor.
- `make-vectors.mjs`: writes `platform.vectors.json` (all expected values above) and runs the self-checks.

The platform renders no audio, so there is no `Builds/Listen/platform/` file.
