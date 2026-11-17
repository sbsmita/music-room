# 12 · New instruments · BUILD-GUIDE: building an instrument (or the sheet music) on the phase-0 platform

> **2026-11-10: the theremin was removed from the app** at the player's request (its folders, unit, source, lessons, Sunny lines and tests; `theremin.md` and its critique deleted). The id block value 8 stays retired and is never reused; `Assets/MusicRoom/Core/Registry/RetiredInstruments.cs` migrates an old save. Mentions of it below are the historical design record.

**Who reads this.** The five instrument agents (handpan, hand drums, marimba, theremin, singing bowls) and the sheet-music
agent. With this file, your own spec (`12-new-instruments/<key>.md`, plus `critiques/<key>-unaddressed.md`) and your clone,
you can build your instrument without touching a shared file. platform.md is the design; this file is what phase 0
actually built, with every name you need.

**What phase 0 is.** The clone `u-platform` (built 2026-11-01): the registry, the id block, the stubs of all
five instruments and an empty sheet module, every shared contract pre-declared, all tests green (test-edit 962, test-play
402, --gpu 435, perf 15: ARCHITECTURE.md 20.12).
`instruments-base` is a frozen copy of it. Everything below names files relative to `Assets/MusicRoom/`.

---------------------------------------------------------------------------------------------------------------------------

## 1. The rules (short)

1. **Your folders only** (platform.md 13.2):

   | You | Own (create, change, delete) |
   |---|---|
   | Instrument `<X>` (`Handpan`, `HandDrums`, `Marimba`, `Theremin`, `Bowls`) | `<X>/**`; `Audio/Sound/<X>/**`; `Tests/EditMode/<X>/**`, `Tests/PlayMode/<X>/**`, `Tests/EditMode/Audio/<X>/**` (incl. `Reference~/`); your lab and listen folders; `Builds/Shots/<key>/` |
   | Sheet | `Sheet/**`, `Tests/EditMode/Sheet/**`, `Tests/PlayMode/Sheet/**`, its lab and listen folders, `Builds/Shots/sheet/` |

   Everything else is shared. A shared need goes into your `CONTRACT-REQUESTS.md` (section 10) with the exact code; never
   edit the shared file. The integrator applies requests.
2. **Keep the stub's asmdef and its `.meta` GUID** (section 11 lists them). Edit `<X>/MusicRoom.<X>.asmdef` in place (add
   references); never delete and recreate it. The test asmdefs, `MusicRoom.Editor.asmdef` and `Core/link.xml` already list
   all six names. Do not add an asmdef under `Tests/*/<X>/`: your tests compile into the shared `MusicRoom.Tests.EditMode` /
   `MusicRoom.Tests.PlayMode` assemblies.
3. **Implement exactly your two partial methods**, `SoundUnits.Create<X>` and `UnitHooks.Render<X>`, in your own files under
   `Audio/Sound/<X>/`. The declarations and their switch cases exist (`Audio/Burst/Units/SoundUnit.cs`, `UnitSlot.cs`).
4. **Names** carry your prefix (section 3). **Order / TickOrder** as section 2. No brand names, no third-party assets.
5. **Tests:** boot with `Tests/PlayMode/Core/TestBoot` (never edit it, `OfflineEngine`, `IntegrationShotsTests` or another
   module's tests). Set `MUSICROOM_SHOTS` to your own shots folder. Keep the conformance suite green (section 9).
6. **Shared modules never name your instrument** (`RegistryLintTests`): Layout, Menu, Lessons, Guide, MR, Room, Core and
   Audio outside `Audio/Sound/<X>` read the registry. You never need them to.

---------------------------------------------------------------------------------------------------------------------------

## 2. The id block (fixed; values never move)

| Instrument | `InstrumentKind` / `RigId` / `UnitId` | `SourceId` (value) | Module, `Order` | `TickOrder` | Folder / asmdef | Id (prefix) | Carousel |
|---|---|---|---|---|---|---|---|
| Handpan | 5 | `Handpan` (18) | `HandpanModule` 33 | 21 | `Handpan` / `MusicRoom.Handpan` | `handpan` | 4 |
| Hand drums | 6 | `HandDrumLo` (19), `HandDrumHi` (20) | `HandDrumsModule` 34 | 22 | `HandDrums` / `MusicRoom.HandDrums` | `handdrums` (types: `handDrum*`) | 6 |
| Marimba | 7 | `MarimbaLo` (21), `MarimbaMid` (22), `MarimbaHi` (23) | `MarimbaModule` 35 | 23 | `Marimba` / `MusicRoom.Marimba` | `marimba` | 5 |
| Theremin | 8 | `Theremin` (24) | `ThereminModule` 36 | 28 | `Theremin` / `MusicRoom.Theremin` | `theremin` | 2 |
| Singing bowls | 9 | `Bowls` (25) | `BowlsModule` 37 | 24 | `Bowls` / `MusicRoom.Bowls` | `bowls` (types: `bowl*`) | 3 |
| Sheet music | – | – | `SheetModule` 45 | – | `Sheet` / `MusicRoom.Sheet` | `sheet` | – |

The classic three keep 0–2 (piano, drums, harp); Table 3; Menu 4 (`RigId.Lessons`). `RigOf(kind) = (RigId)(int)kind`
(`InstrumentIds.RigOf`). **The theremin spec's "5" and the bowls spec's `UnitId.Bowls (5)` are superseded by this table.**
`MarimbaMid` is the marimba spec's R1 (8 platform sources, 26 in all). The bowls have ONE source (`Bowls`), not `Bowl0..2`.
`PlaySurface.Instrument` is the one generic play surface: no per-instrument `PlaySurface` values.

---------------------------------------------------------------------------------------------------------------------------

## 3. Names, prefixes, settings keys

| Shared space | Your names |
|---|---|
| Event types (`TakeEvent.type`) | start with your id (`handpan`, `handpanMute`); hand drums `handDrum*`; bowls `bowl*` |
| Tip ids, voice line ids | `tip_<id>_*` (e.g. `tip_theremin_volume`). **Rename** `tip_lesson_theremin`, `tip_lesson_bowls*` to `tip_theremin_lesson`, `tip_bowls_lesson*` |
| Feature ids | `<id>.<feature>`. **Rename** `lessons.marimba`, `lessons.theremin`, `menu.theremin*` to `marimba.lessons`, `theremin.lessons`, `theremin.help` ... |
| Signals (`InstrumentSignalEvent.Signal`) | `<id>.<signal>` (`theremin.loud`); the sheet music uses `sheet.<signal>` with `Kind = Piano` |
| Lesson ids | `<id>-*` (`marimba-first`) |
| Settings keys | `musicroom.<id>.*` (AppSettings, the injected store: never `PlayerPrefs` directly). **Rename** `musicroom.thereminHelp/Tone/Notes` to `musicroom.theremin.help/tone/notes` |
| Material templates, bake names | `<X>/<name>` (`Handpan/Steel`): **not** `MusicRoom/HandpanSteel` |
| Anchor ids | free, unique within your instrument (the Guide asks your `IGuideAnchors` first) |
| Screenshots | `Builds/Shots/<key>/` |

`RegistryTests` checks the event, tip, line and feature prefixes over every descriptor.

---------------------------------------------------------------------------------------------------------------------------

## 4. Your files and the types you fill in

Start from your stub (it compiles, plays a wooden knock and passes the conformance suite): `<X>/<X>Definition.cs`,
`<X>/<X>Module.cs`, `<X>/Resources/LessonPacks/<id>.json`, `<X>/README.md`, `<X>/CONTRACT-REQUESTS.md`,
`Audio/Sound/<X>/<X>StubUnit.cs`, `Tests/PlayMode/<X>/<X>Fixture.cs`. The stub's body is Core's `PlaceholderInstrument`
(`Core/Registry/PlaceholderInstrument.cs`): read it as a complete, minimal example of every interface below.

### 4.1 The descriptor: `<X>/<X>Definition.cs`

```csharp
public sealed class <X>Definition : IInstrumentDefinition { public InstrumentDescriptor Describe() => new InstrumentDescriptor { ... }; }
```

Parameterless, static data only (no Unity objects). Core finds it by reflection before any module's `Init`
(`App.Registry`, `app.Get<IInstrumentRegistry>()`). The fields (`Core/Contracts/Registry.cs`):

| Field | What you put |
|---|---|
| `Kind`, `Rig`, `Id`, `Carousel`, `ModuleOrder`, `TickOrder` | section 2 exactly |
| `Name`, `AloneName`, `Family` | the menu's tile name, "<Name> alone" for One alone, a family word |
| `Traits` | `Playable` + `Pitched`, `Contactless` (theremin), `Continuous` (theremin, bowls), `UsesTools`, `FastMotion` (Fast Motion Mode while played live: its tools held, its live onsets in the last 4 s, or One alone on it), `HasStand`, `TableTop` (may stand on a real table in MR) |
| `Tools` | `ToolMask` of the tool pointers your `Tick` receives besides the fingertips (marimba `Mallet`; bowls `Striker \| RubStick`) |
| `Seat` (`SeatSpec`) | `CanonicalSlot`, `Prefs` (slots in preference order), `Priority`, `Home`/`HomeYaw` (seat frame, the canonical slot), `Single`/`SingleYaw` (One alone), `ReachMax`, `PlayCentre` (rig-local xz), `Boxes` (`FootBox`: rig-local `Cx, Cz, Hx, Hz`, heights `Y0..Y1` above the floor **with the rig at Home**, `Air` for play space), `LiftMin`/`LiftMax` (arrange-mode height range about the home; both 0 = stays on the floor), `PanelHome` (optional seat-frame menu home while you are Facing: theremin R6) |
| `Stow` (`StowSpec`) | `SpotId`, `Mount` (`Floor`, `Wall`, `Surface`), `Pose` (room x, y, z of the rig origin) + `Yaw`, `Boxes` (displayed footprint, heights above the floor with the rig at `Pose`; null = the seat boxes), `Where` (the tile's status line: "By the window") |
| `Portrait` | `From`, `At` (rig-local), `Fov`; `Fallback` vector strokes in a 100-unit box (the menu draws them without a GPU) |
| `Audio` (`AudioSpec`) | `Unit` (= your kind), `CapAvgMs`, `CapWorstMs`, `ArenaMiB`, `BakedMiB`, `ErOrigin` (rig-local origin of your early-reflection group), `Sources` (`AudioSourceSpec(source, local, sendDb, erDb)`: your sources in channel order with rig-local positions and optional room-send overrides; NaN = the table's value) |
| `Lessons` (`LessonSpec`) | `Travel`, `LeadSeconds`, `Shape` (`Box, Disc, Capsule, Ring, Bar`), `Notation` (`None, Treble, Grand, Percussion, PitchLine`), `Pack` (= your id), `CountNoun` ("strokes", "rings") |
| `Mr` (`MrSpec`) | `OnTable`, `SurfaceLift` |
| `Shadow` (`ShadowSpec`) | the soft floor blob: rig-local centre, `W`, `D`, `Opacity` (one combined draw for all at-seat instruments) |
| `Events` | `EventTypeSpec(type, role, key, sampled, held)`: section 6.1 |
| `Tips` | `TipSpec`s: section 7 |
| `Features` | `FeatureSpec(id)` or `new FeatureSpec(id, "why passive")` |
| `Anchors` | every anchor id your tips and lessons name (conformance 13 resolves each) |
| `Options` | `InstrumentOption` rows for the menu (section 8.4) |
| `DrawsAtSeat`, `DrawsStowed`, `TrisAtSeat`, `TrisStowed` | the budgets you promise (platform.md 11: <= 4/6/6/5/5 at the seat, <= 2/3/3/3/3 on display, <= 25 k tris at the seat, <= 8 k on display) |
| `SheetAnchor`, `HasSheetAnchor` | a music-rest pose for the sheet module (a future stand) |
| `Placeholder` | **false** in your real descriptor (true only for the phase-0 stubs: relaxes the content rules and the loudness parity) |

### 4.2 The module: `<X>/<X>Module.cs`

`public sealed class <X>Module : AppModule` with `public const int ModuleOrder = <section 2>` and `Order => ModuleOrder`.
In `Init(App app)`: build your rig under `app.WorldRoot` at `d.Seat.Home` / `HomeYaw`, then register:

```csharp
var rig = new Rig { Id = RigId.<X>, Group = transform, Zone = <IPlayZone>, Priority = d.Seat.Priority,
    HomeAll = d.Seat.Home, HomeYawAll = d.Seat.HomeYaw, HomeSingle = d.Seat.Single, HomeYawSingle = d.Seat.SingleYaw,
    Handles = _handles /* filled ONCE, same list object forever */ };
rig.Instruments.Add(this);
app.Register<Rig>(rig);                       // marks the instrument loaded (Registry.IsLoaded)
app.Register<IInstrument>(this);
app.Register<IStowable>(this);
app.Register<IInstrumentPlayback>(this);
app.Register<IGuideAnchors>(this);
app.Register<ILessonTargets>(this);           // if you have lessons
app.Register<IContactSurface>(this);          // struck surfaces: finger clamp and contact shadows (E1/E3)
app.Register<IHandToolProvider>(tools);       // if you have tools
app.Register<IInstrumentSurface>(this);       // if TableTop
foreach (var s in d.Audio.Sources) audio.SetSourceParent((SourceId)s.Source, transform, s.Local);
```

Get `IRecorder` lazily (Lessons registers it at Order 40, after you): `_recorder ??= app.Get<IRecorder>()`.

| Interface (`Core/Contracts/Instruments.cs`) | What it must do |
|---|---|
| `IInstrument` | `Kind`, `TickOrder`, `AcceptsStickPointers` (false: Core builds your pointer list from `Tools`), `IsPresent` = active and `Rig.InScene` and not stowed, `Root`, `Tick(pointers, dt)` (contact logic; skip `p.FromMouse`), `Hover` (empty: no glows), `PlaySurfaceAt` (`PlaySurface.Instrument` over your surfaces), `PlayedParts` (played meshes, never grab handles) |
| `IStowable` | `SetStowed(bool stowed, StowMount mount)`: the static display configuration (tools released and laid on it, power off, its stand hidden for `Surface`, static meshes combined per material), `Stowed`, `Kind`. Layout calls it when you go on display and on arrival |
| `IInstrumentPlayback` | `Play(in TakeEvent e)` for each of your non-Marker types: the sound and (if `!Stowed`) the visual the live gesture made; false for unknown types. `e.lane` says where it comes from (section 6.3). `ReleaseAll()`, `CloseLane(int lane)` |
| `IGuideAnchors` | `TryGetAnchor(id, out world, out up)`, allocation-free; `Kind` = your kind; document the ids in your README |
| `ILessonTargets` | `Validate`, `TryGetLanding(t, out landing, out approach (unit, start -> landing), out size)`, `Matches(t, e)` (live events), `ShowTarget(t, k)` (your own cue, k reaches 1 when the block lands; no glow), `Hint(targets)`, optional `Travel(t, travel)` (a per-target travel cap: the handpan's near fields) |
| `IHandToolProvider` | `Owner`, `NearestFree(tip)` (+inf when not at the seat), `TryPickUp(hand, tip)` (None / Near / Grabbed, as the kit's), `AnyHeld`, `ReleaseAll`, `ShadowSpheres(Span<Vector4>)`. Tool pointers: `Pointer.CreateTool(id, owner, ToolKind.Mallet)` registered with `app.Input.RegisterExternal`; two ends that play differently are two pointers (`Striker`, `RubStick`). Shared grip maths: `Core/Util/HandToolMath.cs` (`Grip`, `Pose`, `SegmentDistance`, `SettleEase`: marimba R4) |
| `IInstrumentSurface` | `OnSurface`, `SetOnSurface(bool)`: MR on a real table hides the stand |
| `IContactSurface` | as Piano / Drums (`Core/Contracts/Contact.cs`) |
| `ILessonAccompany` (optional) | `Play(int[] midi, float vel)`, `Release()`: a lesson's `accompany` chords through your own sound (marimba R6) |

Per-pointer state: index arrays by `Pointer.Slot` (`PointerInput.MaxSlots` = 32). For every live play call
`PlayStamp.Mark(hand or tool owner, Kind, app.Clock.NowMs)` and `recorder.Add(ev)` (or `recorder.Add(ev, atAudioTime)` to
stamp a stroke you classify a little later: the hand drums' mute relabel).

**Contactless** (theremin): play only from hands with `p.ClaimedBy == null` (Core claims fingertips inside contact
instruments' zones, the menu, Sunny and the sheet pages every frame: `Core/Input/PointerClaims.cs`), only while
`ILayoutService.Facing == Kind` (in My set; One alone: always), and not when
`PlayStamp.PlayedOtherWithin(hand, Kind, nowMs)` (0.4 s).

### 4.3 The unit: `Audio/Sound/<X>/`

Delete `<X>StubUnit.cs`; write your unit (ENGINE-API.md 4 walks through one; `Audio/Burst/Units/ExampleUnit.cs` and
`KnockUnit.cs` are complete small units):

```csharp
namespace MusicRoom.Audio.Burst
{
    public interface I<X>Sound { ... }                       // your API; the module calls audio.ActiveUnit(UnitId.<X>) as I<X>Sound
    public sealed unsafe class <X>Unit : SoundUnit, I<X>Sound, ILoudnessReference
    {
        public <X>Unit(SoundContext ctx) : base(ctx)
        {
            _s = Alloc<<X>State>();
            Register(ctx, UnitId.<X>, new[] { SourceId.<...> }, _s, ringCapacity: 256, pendingCapacity: 256, idleSkip: true);
        }
        public void Strike(..., double when = 0) { RecordUnit(OpStrike, index, vel, when); Post(new GraphCommand { ... }); }
        ...
    }
    public static partial class SoundUnits { static partial void Create<X>(SoundContext ctx, ref SoundUnit unit) => unit = new <X>Unit(ctx); }
    public static unsafe partial class UnitHooks { static partial void Render<X>(UnitSlot* u) => <X>Dsp.Render(u); }
}
```

- **Unit-only role:** there is no classic synth; Classic sound never suspends you; `ActiveUnit(UnitId.<X>)` always returns
  your unit. No `HasClassic` / `FollowsClassic` flag is needed (handpan, theremin, bowls requests: superseded).
- **Every public method calls `RecordUnit(op, index, vel, when)`** (tests read `AudioTestTap.Calls`, `AudioCall.Unit`,
  `tap.CountUnit(UnitId.<X>, op)`).
- **Idle skip** (`idleSkip: true`): the graph stops calling your Render after 8 silent blocks with nothing due; your next
  Render gets `UnitFlags.Resumed` once. Displayed instruments' sources pause after 1 s of silence (`AudioPresence`) and wake
  >= 250 ms before a playback dispatch.
- **Share and Work:** at block start read `u->Graph->Budget / 1200f * u->Share` and scale your own caps (Share 0.4 must cut
  `Work` by >= 50 %: `UnitShareTests` pattern); write `u->Work` (your work count). The governor sets Share by class
  (Focus, AtSeat, Playback, Tail).
- **`ILoudnessReference`** (`ReferenceSeconds`, `PlayReference(start)`): your mf reference phrase; `LoudnessParityTests`
  requires it within +-2 LU of the piano's (-24.15 LUFS) once `Placeholder` is false.
- Boot work on `ctx.StartWorker(...)`; nothing allocates on the audio thread; flush denormals at block end; `LateDrop` on
  onsets only. platform.md 10.3 lists the conformance limits.
- **Room sends:** the `AudioSourceTable` rows are already the final specs' values (Handpan 0 dB; hand drums -3 / ER +3;
  MarimbaLo -2, Mid 0, Hi -2; Theremin 0; Bowls -1; reach 0.7; MR drops 6 dB). Override per source in your descriptor
  (`AudioSourceSpec.SendDb` / `ErDb`) if your measurements say so.
- Your early-reflection group point is your descriptor's `Audio.ErOrigin` (4 groups go to the at-seat set, then to
  displayed instruments that sounded in the last 2 s).
- One-shots without a unit (the sheet's paper): `IAudioEngine.AddOneShotPreset(in OneShotPreset)`, `PlayOneShot(preset,
  gain, source, voiceId, when)`, `ReleaseOneShot`.

---------------------------------------------------------------------------------------------------------------------------

## 5. Where you stand: footprints, the display and the planner

**Phase 0's footprints** (final specs; `Tools/SoundLab/platform/registry.mjs` holds them; your stub descriptor carries them):

| Instrument | Canonical slot, home (seat frame) | Prefs | Boxes (rig-local cx, cz, hx, hz, y0..y1) | Display spot (room x, z, yaw) |
|---|---|---|---|---|
| Handpan | Front (0, 0, 0.38) yaw 0 (= Single) | FL, FR, L, R, F | shell (0, 0, .265, .234, .62..86) (rim centre 0.74 m, 28 deg: the review's raised placement); stand (0, .18, .18, .12, 0..62), set back beyond the knees | `left-wall-bracket` wall (-3.06, -0.62, -90), box (0, 0, .27, .12, 1.30..1.86) |
| Hand drums | Front (0, 0, 0.57) | FR, FL, R, L, F | heads (.005, 0, .36, .165, .62..80); shells (.005, .085, .36, .21, 0..62) | `window-far` floor (2.64, -1.85, 90) |
| Marimba | Front (0, 0, 0.50) | F, L, R | A (0, -.005, .62, .255, 0..745); B (-.03, .43, .58, .215, 0..745) | `window-near` floor (2.30, -0.80, 90) |
| Theremin | Front (0.18, 0, 0.33) yaw -7 | F, R, L, Rr, Lr | `THEREMIN_SEAT` (6 boxes incl. the air field (-.12, -.02, .48, .22, .75..1.35)) | `front-wall-left` floor (-1.62, 0.91, 0) |
| Bowls | FrontLeft (-0.46, 0, 0.25) | FL, FR, L, R, F | stand (0, 0, .205, .145, 0..61); bowls (0, 0, .205, .145, .61..72) | `window-sill` surface (3.08, -1.62, 90), box (-.01, 0, .255, .09, .63..735) |

The marimba and hand-drums spots moved from platform.md 4.1 (2.71 / 2.75) to stay clear of the sill with the real depths.

**Fit with these numbers** (planner vectors regenerated): alone 8/8, pairs 26/28, triples 38/56, fours 0/70. Pair slots
(row = instrument, column = partner; `*` = the side table at its alternate pose; `+n` = pushed out n cm):

```
            piano   harp therem  bowls handpa marimb handdr  drums
piano           -      F      F      F      F      x      F      F
harp            L      -      L      L      L      L      L      L
theremin    Rr+15      F      -   R+10  Rr+15  L+10*   R+10  L+10*
bowls          FL     FL     FL      -     FL     FL     FL     FL
handpan      R+10  FR+15    F+5  FR+15      -      F  FR+15      F
marimba         x   R+10  R+10*   R+10   R+10      -   R+10      x
handdrums       R   FR+5  FL+10   FR+5  FL+10  FL+10      -  FL+10
drums           R      R     R*      R      R      x      R      -
```

**The marimba never sits Front in a set** with its spec boxes: box A spans y 0..0.745 at z 0.24..0.75 from the head, inside
the planner's knee box (x +-0.22, z 0.09..0.41, y 0.40..0.62), and box B reaches the front wall (z 1.145 > 1.10). Fold the
tubes away from the knees (critique item 11) and model it with boxes above 0.62 m near the player; then request a vectors
re-run.

**Changing your footprint or spot:** edit your descriptor (yours), keep the conformance check 3 (your `Rig.Handles` inside
`Seat.Boxes` within 3 cm) green, and put the new numbers in `CONTRACT-REQUESTS.md` as a `registry.mjs` / `stow-plan.mjs`
diff: the integrator re-runs `node make-vectors.mjs` and copies `platform.vectors.json` to
`Tests/EditMode/Layout/Reference~/`. (`SeatPlannerTests` checks the C# planner on the vectors' own data, so your change
cannot break it; one test logs, without failing, where the live stubs differ from the vectors.)

**What Layout does for you** (you never call it except `Bring`): My set (up to 3 + the side table, `musicroom.set`),
`Bring(kind, BringReason)` from the menu grid, the page turn, point-and-pinch from afar (`RaySummon`), lessons and Sunny;
glides of 0.9-1.4 s (`RigGlide`); `InstrumentMoveEvent { Kind, Phase = Leaving | Arrived | Displayed, Reason }`; `Rig.InScene`;
`IStowable.SetStowed`; `ILayoutService.PlaceOf` (Seat, Display, Gliding, Hidden), `SlotOf`, `Facing` (+-30 deg, 10 deg
hysteresis, 0.3 s dwell; `FocusEvent`), `IsMoving`, `Lead` (MR); arrange mode moves you within `LiftMin..LiftMax`, and a
displayed instrument on its floor / wall / surface.

---------------------------------------------------------------------------------------------------------------------------

## 6. Recording, playback, the looper

### 6.1 Event types
`EventTypeSpec(type, role, key, sampled: false, held: false)` in your descriptor's `Events`:

| Role | Meaning | Looper |
|---|---|---|
| `Onset` | a note or hit starts | tidy (quantise) allowed; starts a layer |
| `Release` | a note ends (`Key`: Midi / Index / Kind / Lane pairs it) | an open **`Held`** onset gets its Release at the window end |
| `Control` | a continuous parameter | never quantised; its lane is closed at the window end |
| `State` | a pedal or switch | `sampled: true`: sampled at layer start and played at offset 0 |
| `Marker` | a judgement (`thereminHold`, `bowlSing`) | recorded, never played back; lessons and tips read it |

**`held: true`** only for an onset that sounds until its Release (a pressed key). A struck bar, field or bowl is not Held:
it is never damped at a loop boundary (so `marimbaDamp`, `handpanMute` as Releases are safe). The bowls' `bowlDamp` as a
`State` is fine too.

`TakeEvent` fields: `t, type, midi, vel, kind, pos, zone, open, i, u, speed, mode, lane`. No new fields: map yours onto these
(the theremin's vibrato depth in `pos`, the bowls' contact angle in `midi` as milliradians are fine; document it).

### 6.2 Continuous control
`Core/Recording/ControlEncoder.cs` (the platform's C# version of `control-decimator.mjs`, exact to the vectors):
`new ControlEncoder("theremin", lane)` -> `Frame(t, pitch, level, frameDt, buffer)` emits `<prefix>On/Ctl/Off` only while
the lane sounds (a resting hand posts nothing: the theremin review item 9); `Close(t, buffer)`;
`ToTakeEvent(e)`; `ControlEncoderSettings` (deadbands) are yours to tune. The bowls may keep their own `RubEncoder` in their
folder. Playback dispatches a Control `min(speed, 0.05)` s early. **Your unit must ignore a level-0 Control to a sleeping
voice (no wake)** and post Controls only while sounding.

### 6.3 Lanes (`PlaybackLanes`)
Live play is lane 0; looper layer k (0..5) plays back on lane `1 + k` (1..6); a take from the deck or the library on lane 7
(`PlaybackLanes.Take`). A unit with fewer voices folds lanes: `PlaybackLanes.Fold(lane, voices)`. The theremin's 8 voices map
one to one (0 live, 1-6 layers, 7 take). **Live against playback on one voice** (the bowls review item 14):
`Core/Recording/LaneArbiter.cs` (one per voice): `Claim(lane)` -> `Take` / `Continue` / `Preempt` (the live hand takes the
voice from playback) / `Drop` (a playback lane never takes it from the live hand; the first playback lane keeps it);
`Release(lane)` ignores non-owners.

### 6.4 Wiring
`Lessons/PlaybackWiring` routes every non-Marker type of your descriptor to your `IInstrumentPlayback.Play` (closures made
once); `ReleaseAll` on stop and clear. `Recorder.MaxEvents` is 40000.

---------------------------------------------------------------------------------------------------------------------------

## 7. Sunny: tips, voice lines, demos, anchors, signals

### 7.1 Tips are descriptor data (`TipSpec`, `Core/Contracts/Guide.cs`)

```csharp
new TipSpec { Id = "tip_marimba_arrive", Title = "Pinch a mallet to pick it up.", Lines = new[] { "tip_marimba_arrive" },
    Class = TipClass.P2, Trigger = TipTrigger.FirstArrival, Demo = DemoKind.Pinch, Anchor = "tray.right",
    Success = TipSuccess.EventType, SuccessArg = "marimba", Covers = new[] { "marimba.play", "marimba.mallets" } }
```

- **Triggers:** `FirstArrival` (your first `InstrumentMoveEvent{Arrived}`), `FirstDisplayedGaze` (looked at on display 3 s),
  `IdleNear` (`TriggerArg` seconds), `Signal` (`TriggerArg` = your signal), `Manual` (call `app.Get<IGuide>().Tip(tipId)`
  yourself), `LessonStart` (your first lesson starts; `TriggerArg` = a lesson id or empty). Counters, field filters and
  sequences ("3 strokes and no open tone yet") are YOURS: count them in your module and publish
  `InstrumentSignalEvent { Kind, Signal = "<id>.<signal>" }`, or call `IGuide.Tip`.
- **Success:** `EventType` (a live event of `SuccessArg`; empty = any live onset of yours), `Signal`, `None`, `Facing`,
  `LessonHit`.
- **Classes:** P1, P2, P3 (P3 rate limited: not in the first 90 s after the tour, <= 3 per 10 min, 60 s apart).
- `Again` (default "again": "Like this."), `VrOnly`, `TextOnly` (a caption, no clip: the stubs only), `ThenDemo` /
  `ThenAnchor` (a second demo: "tap, then circle").
- **Every playable instrument needs one P2 `FirstArrival` tip with an `EventType` success** (`InstrumentTipsTests`), and one
  tip per special gesture; every feature is covered by a tip or marked passive (`SunnyCoverageTests`). The user's rule:
  Sunny covers every feature, and Sunny's text matches the real controls.

### 7.2 Voice lines live with you
- `<X>/Resources/GuideLines/<X>.json`: `{"lines":[{"id":"tip_<id>_x","text":"Caption.","spoken":"optional spoken form"}]}`
- `<X>/Resources/VoiceManifests/<X>.json`: exactly the shape of `Guide/Resources/Voice/manifest.json`:
  `{"tip_<id>_x": {"file": "tip_<id>_x.wav", "seconds": 2.468, "rate": 22050}}`
- `<X>/Resources/Voice/tip_<id>_*.wav`: 22.05 kHz mono, rendered with eSpeak NG through
  the voice render script `make-voice.mjs` `render()` (import it from a script in your scratch folder; never edit
  it or run its main), the same `.meta` importer settings as `Guide/Resources/Voice/tip_arrange.wav`
  (copy the .meta, new GUID). `seconds` measured from the file. Lint: <= 12 words, <= 4.0 s (aim <= 3.6 s), no brand words.
- GuideVoice merges every manifest (unbounded line count, rendered lazily, <= 24 resident); GuideLines merges every line
  file. A duplicate id is an error.

### 7.3 Demo kinds
`DemoKind`: the tour's `Palm, Poke, Swipe, Nod, Press`; `Tap, Slap, MalletStrike, Approach, Circle, Strike, Hold, PalmRest,
Pinch, Point`; the instruments' variants `OpenTone, PalmBass, CuppedSlap, HeelToe, Roll` (hand drums), `MalletHold,
OpenHand` (marimba), `Hover, Lift, Lower, Rock, Dip, Curl` (theremin), `FingerCircle` (bowls); `Custom` with
`TipSpec.Motion` (`DemoMotion`: `DemoKey(t, offset in the anchor frame, GhostHand shape)`, loop seconds) for anything else.
The enum's comments describe each preset. The anchor frame: y along your anchor's `up`, z toward the player, x to their right.

### 7.4 Anchors and signals
`IGuideAnchors.TryGetAnchor` with `Kind` = yours; list them in `Anchors`. The Guide asks your provider first. Sunny's B6
lesson re-demo anchors at the waited target's `TryGetLanding`. Signals: `"<id>.<signal>"`.

---------------------------------------------------------------------------------------------------------------------------

## 8. Lessons, the menu, mixed reality

### 8.1 Lesson packs
`<X>/Resources/LessonPacks/<id>.json`, the `LessonFile` schema of `Lessons/Resources/lessons.json`: `{"tutorial": {...},
"lessons": [ { "id": "<id>-first", "instrument": "<id>", "title", "listNote", "bpm", "secondsPerBeat", "leadSeconds",
"beats", "seconds", "eventCount", "notes", "events": [ { "k", "beat", "len", "time", "duration", "due" (= leadSeconds +
time), "hint", "targets": [ { "midi", "i", "note", "kind", "label", "color", "zone", "mode", "hand", "hold", "cents",
"len" } ] } ], "options": ["Accompany", "QuietMistakes"], "accompany": [ { "beat", "len", "midi": [], "vel" } ], "meter",
"unit", "credit", "pickup" } ] }`. Ordered after the classic lessons by carousel. Rules (platform.md 7.4): >= 3 lessons,
the first a guaranteed win (one target at a time, 12-21 s), public-domain melodies only (name the source), <= 2 targets per
event, gaps >= 0.2 s, repeats >= 0.25 s, theremin holds >= 0.5 s, every lesson < 1 min.

The generic tutorial path: blocks of `LessonSpec.Shape`, travel `ILessonTargets.Travel`, landing from `TryGetLanding`, hits
from `Matches` on live events, `ShowTarget(t, k)` cue (k = 1 at landing), `Hint`; B2 (a re-hit is no mistake), B4 (the
summary says `CountNoun`), B6 (8 s: Sunny re-demos; 15 s: any hit of yours counts). Options: `NoCountdown`, `Accompany`
(your `ILessonAccompany`, else the piano sound), `QuietMistakes`, `Forgiving`. Starting a lesson brings you to the seat
(`Bring(kind, Lesson)`); the countdown waits for `Arrived`.

### 8.2 The menu
The Instruments grid shows your tile automatically (portrait rendered from your model with your `Portrait` pose, else your
fallback strokes; name; "With you" / your `Stow.Where` / "Put away" in MR; the My set tick). The Lessons grid shows you when
you have lessons. No menu code needed.

### 8.3 Mixed reality
In MR you are hidden while displayed. As the lead (slot Front, else the first in carousel order): `Mr.OnTable` -> placed on
a real table lifted by `SurfaceLift`, `IInstrumentSurface.SetOnSurface(true)`; else on the floor. Draw guard: sum of at-seat
`DrawsAtSeat` + 37 <= 80.

### 8.4 Instrument option rows
`InstrumentDescriptor.Options`: `new InstrumentOption { Key = "tone", Label = "Theremin tone", Choices = new[] { "Warm",
"Classic", "Bright" }, Default = 1, Note = "...", Tip = "tip_theremin_tone" }`. Stored as int `musicroom.<id>.<Key>`; the
menu's options page (poke your tile while you are alone, or its "Options" chip) publishes `InstrumentOptionEvent { Kind, Key,
Value }`; anchors `menu.option.<id>.<Key>`, `menu.tile.<id>` resolve while shown.

---------------------------------------------------------------------------------------------------------------------------

## 9. The conformance suite

`Tests/PlayMode/Core/InstrumentConformanceTests.cs`, one case per registry instrument (stubs and real), plus
`Tests/EditMode/Core/RegistryTests.cs` and `RegistryLintTests.cs`. You provide ONE fixture,
`Tests/PlayMode/<X>/<X>Fixture.cs : IInstrumentFixture` (`Kind`; `Strike(FixtureContext c)` scripts one live onset with the
scripted hands at the seat: `c.Index(tip)`, `c.Stroke(from, to)`, `c.Frame()`), replacing the stub's `PlaceholderFixture`.

| Test | Checks (platform.md 12.2) |
|---|---|
| `AtTheSeat` | 1 present at the seat alone, nobody else present; 2 `Rig.Handles` non-empty, the same list object; 3 `Seat.Boxes` cover the handles within 3 cm (logged margins); homes; 11 every pack target validates and lands; 12 the P2 arrival tip; 13 every anchor resolves; 14 your sources hang under your rig; 20 the fallback icon |
| `PlaysRecordsAndPlaysBack` | 7 the fixture records a live onset of yours; 8 `Play` of every non-Marker type makes a sound call; 9 `ReleaseAll` / `CloseLane`; 17 arrange mode silences you |
| `TickAllocatesNothing` | 5 `Tick` / `Hover` / `PlaySurfaceAt` 120 times, 0 B |
| `DisplayedThenBroughtByTheRay` | 1 not present on display or gliding; 6 `Stowed`, no tool held; 19 `Bring(kind, Ray)`: Leaving, then Arrived (reason Ray), present |
| `APageTurnToAndFromIt` | 18 |
| `ItsUnit` | 10 your unit registers and stays active with Classic sound on |

Check 4 (draws) runs on the GPU in `DrawBudgetTests`, `Layout/LayoutShotsTests` and your own shots tests; check 16 (the
desktop click does not double-fire: skip `Pointer.FromMouse` in `Tick`, own desktop path via a `Layers.PlaySurface`
collider) is your module's own test.

**Run it:** `TEST_FILTER='InstrumentConformanceTests|RegistryTests|RegistryLintTests' Tools/unity.sh test-play` (and
`test-edit` for the two EditMode ones). Your own tests: `TEST_FILTER='<X>' Tools/unity.sh test-edit|test-play`. Before you
hand back: full `test-edit`, `test-play`, and (under the lock) `test-play --gpu` and `test-perf`.

**The GPU lock** (several clones share this Mac): around every `test-play --gpu` and `test-perf` run:

```sh
until mkdir "$GPU_LOCK" 2>/dev/null; do sleep 20; done    # GPU_LOCK: the shared lock folder
Tools/unity.sh test-play --gpu      # or test-perf
rmdir "$GPU_LOCK"    # always, also after a failure
```

One Unity process per clone folder at a time. Unity may write `Assets/Resources/MetaXRAcousticMaterialMapping.asset` during a
run: it is not yours; delete it before you hand back.

---------------------------------------------------------------------------------------------------------------------------

## 10. CONTRACT-REQUESTS.md (your folder)

One numbered item per shared change you need. Each item:

```markdown
## R<n> [required | optional] <one-line title>
- **File:** Assets/MusicRoom/<path> (the shared file, exactly)
- **Why:** the behaviour you need and the spec section or review item behind it
- **Change:** the exact code (a diff or the full member), minimal and default-bodied where it is an interface member
- **Test:** the test that shows it works (yours, or the one the integrator should add)
- **Fallback until applied:** what your code does without it
```

The integrator applies items after copying your folders (platform.md 13.4). A data change to the planner's reference
(`registry.mjs`, `stow-plan.mjs`) is a request too.

---------------------------------------------------------------------------------------------------------------------------

## 11. Phase B: moving phase-A work onto instruments-base

Phase A ran in clones cut from **platform-base** (before phase 0): your instrument's own folders, your own asmdef, and
probably local stand-ins for contracts that did not exist yet. Phase B moves that work onto **instruments-base** (this
platform's result) and wires it into the registry. Do it in a fresh clone of instruments-base.

1. **Copy your folders in**, keeping `.meta` files, but never over the stub's identity files:
   - `Assets/MusicRoom/<X>/` (your module, visuals, Resources), `Assets/MusicRoom/Audio/Sound/<X>/`,
     `Assets/MusicRoom/Tests/EditMode/<X>/`, `Tests/PlayMode/<X>/`, `Tests/EditMode/Audio/<X>/` (incl. `Reference~/`).
   - **Keep instruments-base's** `<X>/MusicRoom.<X>.asmdef.meta`, `Assets/MusicRoom/<X>.meta` (the folder) and
     `Audio/Sound/<X>.meta`; merge your asmdef's `references` into instruments-base's `MusicRoom.<X>.asmdef` (edit it in
     place; keep `"name": "MusicRoom.<X>"`). If your asmdef had another name, rename it to `MusicRoom.<X>`: the names in
     `Core/link.xml`, `Editor/MusicRoom.Editor.asmdef` and both test asmdefs are fixed. Delete any asmdef you made under
     `Tests/*/<X>/`: your tests compile into `MusicRoom.Tests.EditMode` / `.PlayMode`.

     | asmdef | `.meta` GUID to keep | folder `.meta` | `Audio/Sound/<X>` folder `.meta` |
     |---|---|---|---|
     | MusicRoom.Handpan | 23858a0e8b6ab42288451bb8ac04eaab | 278efbc4978d04649b150ec813532e74 | ab91d734cc76d49d9b120d825e21fad4 |
     | MusicRoom.HandDrums | 80ddf04dbf358463e8cf48571c528dcb | e635e33fbd2844c4a80a60e500361691 | b488c65f98ded4b50884e18921f46aae |
     | MusicRoom.Marimba | f789da014e3ea4fd881312f45b40047d | 28db4bf408976451d81611ea1b70584c | 96c241253400c4514836a3725a29809d |
     | MusicRoom.Theremin | c2019aff6ccb54813a744deca636e9b8 | ebf0fab239c8f43cdafd9db690573d12 | 04dd79fc8b199457481c8288858d39e5 |
     | MusicRoom.Bowls | 72ab60d398a4a4079afd34140511cbeb | 5a8cfd1c894cf4909be33c88d62ec49e | 7fa168f8f9b7e4508931ba034a1688e6 |
     | MusicRoom.Sheet | 9a59a9c072e594e73b2e4258ada28282 | 80ddbb19f9cef48c38f5483745ef5692 | – |

2. **Replace or keep the stub files:**

   | Stub file | Phase B |
   |---|---|
   | `<X>/<X>Definition.cs` | **keep the class and the id block**, replace the values with yours (section 4.1); `Placeholder = false` |
   | `<X>/<X>Module.cs` | replace with your module (keep the class name and `ModuleOrder`) |
   | `<X>/Resources/LessonPacks/<id>.json` | replace with your pack |
   | `<X>/README.md`, `<X>/CONTRACT-REQUESTS.md` | replace with yours |
   | `Audio/Sound/<X>/<X>StubUnit.cs` | **delete** (its `I<X>Sound` with `Knock`/`Hush` goes too); your unit implements `Create<X>` / `Render<X>` |
   | `Tests/PlayMode/<X>/<X>Fixture.cs` | replace with your real fixture (keep the class name) |
   | `Core/Registry/PlaceholderInstrument.cs`, `Audio/Burst/Units/KnockUnit.cs` | shared: keep, do not use |

3. **Swap your stand-ins for the platform's contracts.** Delete every local declaration that now exists in Core / Audio, and
   use the real one: `InstrumentKind.<X>` / `RigId.<X>` / `UnitId.<X>` / `SourceId.<...>` (section 2 values: the theremin and
   bowls specs' "5" are wrong here), `PlaySurface.Instrument` (not `PlaySurface.<X>`), `IStowable`, `IInstrumentPlayback`,
   `IGuideAnchors`, `IHandToolProvider`, `IInstrumentSurface`, `ILessonTargets`, `ILessonAccompany`, `TakeEvent.lane`,
   `LessonTarget.zone/mode/hand/hold/cents/len`, `ControlEncoder`, `PlayStamp`, `LaneArbiter`, `HandToolMath`, `TipSpec`,
   `DemoKind`, `InstrumentSignalEvent`, `InstrumentMoveEvent`, `FocusEvent`, `SoundUnit.Register(..., idleSkip)`,
   `UnitSlot.Share/Work/IdleBlocks/Class`, `UnitFlags.Resumed`, `RecordUnit`, `ILoudnessReference`, `IAudioPresence`. An API
   you put in Core in phase A (`IHandpanApi`, `IThereminApi`, `IBowlsApi`) moves into your own assembly (no other module
   needs it); `TakeEventType.<X>` constants move to your assembly; `TakeEvent.<X>(...)` factories become static helpers in
   your assembly.
4. **Wire into the registry** (section 4): fill your descriptor (footprint, display spot, events with roles and keys, tips,
   features, anchors, options, budgets), register the interfaces in your module's `Init`, parent your sources, give your unit
   `idleSkip`, Share / Work and `RecordUnit`, implement `ILoudnessReference`, move your Sunny lines into
   `Resources/GuideLines/<X>.json` + `Resources/VoiceManifests/<X>.json` + `Resources/Voice/*.wav`, your lessons into the pack.
5. **Anything that does not fit a contract** goes to `CONTRACT-REQUESTS.md` (section 10): do not edit shared files to make it
   compile.
6. **Verify:** `compile`, then `test-edit`, `test-play` (the conformance suite, `RegistryTests`, `RegistryLintTests`,
   `SunnyCoverageTests`, `LessonPackTests` must be green for your instrument), then under the GPU lock `test-play --gpu` and
   `test-perf`. Check that nothing outside your folders differs from instruments-base:
   `diff -rq <your clone>/Assets <instruments-base>/Assets` lists only your owned paths.

**The sheet agent, the same steps:** copy `Sheet/` and `Tests/*/Sheet/` (keep instruments-base's `MusicRoom.Sheet.asmdef.meta`
and folder `.meta`); replace the empty `SheetModule` (keep the class and Order 45); swap stand-ins for the hooks of section 12;
your tips go through an `IGuideTipSource` class in `Sheet/` with your lines in `Sheet/Resources/GuideLines/Sheet.json`,
`VoiceManifests/Sheet.json`, `Voice/`.

---------------------------------------------------------------------------------------------------------------------------

## 12. The sheet module's hooks (`Sheet/`, `SheetModule` Order 45)

| Need | Hook |
|---|---|
| Where the page stands | `IPianoApi.MusicRestPose` (`Pose?`, world, the rest's front face bottom centre, local up along the rest, forward into the board; null while the piano is not at the seat or on display), `MusicRestLocal` (piano-local: (0, 0.03974, 0.23761), up (0, cos 0.3, sin 0.3), measured from the real `PianoBody` music rest), `MusicRestLedge` (0.00655 m). `InstrumentDescriptor.SheetAnchor` = `MusicRestLocal` for the piano (`HasSheetAnchor`); a future stand's descriptor carries its own |
| What to print | `ILessonLibrary` (`Lessons`, `Get`, `Count`); `Lesson.meter`, `unit`, `pickup`, `credit`; `LessonTarget.hand`, `len`; `LessonSpec.Notation` of the lesson's instrument (`App.Registry.Get(kind).Lessons.Notation`) |
| Following along | `ITutorial`: `State`, `LessonId`, `Instrument`, `Version`, `CurrentEvent` (-1 idle), `EventsDone`, `CountNoun`; `TutorialEvent` |
| The piano's presence | `Rig.InScene` of `RigId.Piano` (`IRigRegistry`), `ILayoutService.PlaceOf(Piano)`, `IArrangeMode.Arranging`, `IGrabService.HeldRig`, `IMixedReality.IsPlacing` |
| Fingertips in front of the pages | register an `IPointerClaimant` (`ClaimsPoint(world)`): Core asks claimants after the instruments' zones and the menu |
| Sunny | an `IGuideTipSource` (parameterless; `Tips()`, `Features()`), an `IGuideAnchors` (`Kind` = Table), signals `InstrumentSignalEvent { Kind = Piano, Signal = "sheet.<signal>" }` |
| Paper sounds | `IAudioEngine.AddOneShotPreset(in OneShotPreset)` once, then `PlayOneShot(preset, gain, SourceId.PianoMid, voiceId, when)` (they play with Classic sound on too). No new `IAudioEngine` member is needed |
| Budget | <= 2 draws, 0 B per frame, no `Rig`, no `IInstrument`; parent the page under the piano's rig group |

---------------------------------------------------------------------------------------------------------------------------

## 13. What phase 0 pre-declared for the reviews, and what is still a request

**Pre-declared** (use them; no request needed):
- marimba: `ILessonAccompany`, pack `options` / `accompany`, `LessonOptions`; `SourceId.MarimbaMid`; per-source send
  overrides (`AudioSourceSpec.SendDb` / `ErDb`) and the table's -2 / 0 / -2 dB rows; `HandToolMath` (R4); demo `MalletHold`,
  `OpenHand`.
- hand drums: demo variants `OpenTone`, `PalmBass`, `CuppedSlap`, `HeelToe`, `Roll` (+ `PalmRest`, `Custom`); signals are
  yours (`InstrumentSignalEvent`, TipTrigger.Signal); `IRecorder.Add(ev, atAudioTime)` for the mute relabel; FMM from live
  onsets of a `FastMotion` instrument with no tool.
- theremin: `ControlEncoder` emits only while sounding (+ a final level-0 Control); lanes 1..6 per layer and 7 for takes;
  `SeatSpec.PanelHome` (R6); `InstrumentOption` rows; demo `Hover`, `Lift`, `Lower`, `Rock`, `Dip`, `Curl`; `TipSuccess.Facing`.
- bowls: `LaneArbiter` (live preempts playback on a bowl); demo `FingerCircle`; `ThenDemo` (two-step demos);
  `TipTrigger.LessonStart`, `TipSuccess.LessonHit`; the -1 dB send row.
- handpan: the raised placement (rim centre 0.74 m) in its phase-0 footprint; `ILessonTargets.Travel` (per-target travel cap).
- sheet: `IPianoApi.MusicRestPose` / `MusicRestLocal` / `MusicRestLedge`, `ITutorial.CurrentEvent`, `IPointerClaimant` from
  any module, `IGuideTipSource`, the one-shot hooks.

**Still requests** (not pre-declared; file them if you need them): hand drums `MetaHandVisual.SetPalmLift` (Hands module);
the conformance tail-rule variants (hand drums T4, marimba metal rule + 3.22, bowls LF window >= 200 ms) and the theremin's
4e-5 vector tolerance (write them as your own tests); a Decor list on `IRoomFurniture` (the room's ivy and plant leaves touch
the bowls', hand drums' and piano's display spots: Room's report); the integrator's vectors re-run for any footprint change.
