# 10-upgrade · ENGINE-API: how a sound plugs into the Burst audio engine

> **2026-11-11:** only the unit-only roles (handpan, hand drums, marimba, bowls) have units now; the piano, drum, harp and room units, the room's listener stage (`ListenRoom`, `ListenerGraph`, `RenderListener`), the source taps and the Classic sound switch described below were deleted (the classic sound is the only sound). The rest of the contract is unchanged.

**What this is.** This is the contract between the Burst audio engine (built from `burst.md`, now in
`Assets/MusicRoom/Audio/Burst/`) and the four sound workstreams: piano, drums, harp, and room plus master. It covers the
types, how to register a unit, the per-source buffers, the audio-thread rules, the classic-sound switch, how to test a new
instrument, and how to render a WAV offline in EditMode for null tests against the Node reference renders in
each test folder's `Reference~/renders/`.

**Base.** Every workstream builds on the engine as it is in this project. The engine's own tests (`Tests/EditMode/Audio/Burst/`, `Tests/PlayMode/Audio/SoundSwitchTests.cs`) show
every call below in use.

**Golden rule.** You add **new files** only. A unit plugs in through two C# *partial methods*: one renders a block, the
other creates the unit. An unimplemented partial method compiles to nothing, so the four units never touch the same file.
If a shared file really has to change, make the smallest compatible change and list it in your result (section 9).

---

## 1. The picture

```
main thread                                   mixer thread (every 256 frames)
-----------                                   ----------------------------------------------------------------------
IAudioEngine.Piano(...)                       SynthSource #first.RenderBlock
  -> AudioEngine routes the role:               GraphBridge.Render -> AudioGraphHost.EnsureRendered      (ONE Burst call)
     new sound: your unit's IPianoSound           AudioGraph.RenderBlock(state, blockStart, frames)
     classic:   today's PianoSynth                  graph ring -> banks -> one-shots
  -> your unit stages GraphCommands                 for each registered unit:
     on ITS OWN ring, then Commit()                   drain ITS ring -> due events (At, Seq) with Offset
                                                      clear its channels -> UnitHooks.RenderPiano(u)     <- your Burst code
                                                      mix its channels into Out[source] with its fade ramp
                                                    LOD (CPU cap over the banks)
                                              every SynthSource.RenderBlock:
                                                today's voices, processors, body -> += graph slice Out[id] -> send -> bus
                                                -> tap (post-bus copy, if enabled)
                                              -> Meta spatializer -> mixer -> MasterBus (listener):
                                                 room unit registered and playing? ListenerGraph.Render -> UnitHooks.ListenRoom
                                                 else today's master (crossfade while the room fades in or out)
```

- **One Burst call per block** renders the whole graph and every unit. Each source then only adds its slice.
- Your unit writes **mono channels**, one per SynthSource it plays on (at most 8). The engine mixes them into the sources.
  The sources keep their spatializer, reverb send, drum-room send, bus and master, exactly as today.
- Your unit never handles the classic switch itself. The engine fades your output, suspends your render, and sets a
  `Reset` flag when you come back.

## 2. Files and names

| What | Where |
|---|---|
| Kernels, layouts, setup (burst.md sections 4–6) | `Audio/Burst/BurstDspTypes.cs`, `DspKernels.cs`, `DspSetup.cs` |
| Graph, arena, ring, allocator | `Audio/Burst/AudioGraph.cs`, `GraphArena.cs`, `NativeCommandRing.cs`, `VoiceAllocator.cs` |
| Unity owner, bridge, meter, HUD | `Audio/Burst/AudioGraphHost.cs`, `GraphBridge.cs`, `DspLoadMeter.cs`, `DspLoadHud.cs` |
| **The plug-in API** | `Audio/Burst/Units/UnitSlot.cs` (unmanaged side, `UnitHooks`), `Units/SoundUnit.cs` (managed side, `SoundUnits`) |
| **The reference unit (your template)** | `Audio/Burst/Units/ExampleUnit.cs` |
| **Your files** | `Assets/MusicRoom/Audio/Sound/<Piano|Drums|Harp|Room>/` (folder exists, with a README.md stub) |
| Your EditMode tests | `Assets/MusicRoom/Tests/EditMode/Audio/<Piano|Drums|Harp|Room>/` (namespace `MusicRoom.Tests.Audio`) |
| Offline harness | `Tests/EditMode/Audio/Burst/OfflineEngine.cs` (`OfflineEngine`, `WavFile`, `NullTest`) |

Namespace: `MusicRoom.Audio.Burst`. Assembly: `MusicRoom.Audio` (Burst, Mathematics, `allowUnsafeCode`). Your files
must live in this assembly, because a partial method's parts must share one assembly.

## 3. The audio-thread side

### 3.1 The hook

Each role has one block hook. Implement yours in your own file, with exactly this class header:

```csharp
namespace MusicRoom.Audio.Burst
{
    public static unsafe partial class UnitHooks
    {
        static partial void RenderPiano(UnitSlot* u) => PianoDsp.Render(u);   // RenderDrums / RenderHarp / RenderRoom
    }
}
```

The room also has `static partial void ListenRoom(UnitSlot* u, float* data, int frames, int channels, long blockStart)`
(section 7).

### 3.2 What `UnitSlot* u` gives you

`UnitSlot` is defined in `UnitSlot.cs`.

| Field | Meaning |
|---|---|
| `u->State` | Your own unmanaged state, allocated on the main thread (section 4.2) |
| `u->BlockStart`, `u->Frames` | The block: the absolute engine sample of its first frame, and its length (256 on the device; up to `MaxFrames`) |
| `u->SampleRate` | 48000 |
| `u->Due[0 .. u->DueCount)` | This block's events, sorted by `(At, Seq)`, so equal times keep call order. Each `UnitEvent` has `At` (absolute sample), `Offset` (`At − BlockStart`, in `[0, Frames)`) and `C`, the `GraphCommand` you staged. |
| `u->Channels`, `u->Source[c]` | Your output channels and the `SourceId` each one plays on |
| `u->Channel(c)` | Channel c's buffer. **Add** your block into `[0, Frames)`. The engine zeroes it before your call. |
| `u->Flags & UnitFlags.Reset` | You were suspended (classic sound) and now resume. Drop every voice and pending state **first**. |
| `u->Graph` | The whole `GraphState`: the current LOD `Budget`, the modal banks and the one-shot pool (section 5) |

**Sample-accurate events.** Split your block at each event, as the graph does for its banks:

```csharp
int cursor = 0;
for (int e = 0; e < u->DueCount; e++) { Segment(s, cursor, u->Due[e].Offset); Apply(s, u->Due + e); cursor = u->Due[e].Offset; }
Segment(s, cursor, u->Frames);
```

`ExampleUnitDsp.Render` is the full, tested pattern.

### 3.3 Commands

A `GraphCommand` is 64 bytes and holds no references.

| Field | Meaning |
|---|---|
| `Op` | **Your own** sub-op (any byte: cast `(GraphOp)myOp`; the graph never interprets it) |
| `When` | Same rules as `SynthCommand`: 0 = ASAP (the next block), > 0 = absolute `dspTime` (a past time plays now), < 0 = relative |
| `Target`, `A`–`G`, `N`, `Id`, `Seed`, `Flags` | Your payload |
| `Flags & CommandFlags.LateDrop` | Drop the command if it is more than 100 ms late (headset off, then on). Set it on onsets (strike, pluck), never on dampers or pedals. Drops are counted in `u->LateDropped`. |

Each unit has **its own ring**, so its commands keep their call order, as `piano.md` section 3.2 requires. The default ring
holds 256 commands and the pending queue 256 events; set both in `Register`. Late events past the block stay pending. A
full queue counts in `u->DroppedEvents`, and a full ring counts in `SoundUnit.DroppedCommands`.

### 3.4 Rules for code that runs inside `RenderBlock`

- No allocation, no managed objects, no locks, no exceptions, no Unity API, no `Debug.Log`.
- Use raw pointers into your state. Static readonly arrays of blittable values are fine; tables in your state are better.
- **FloatMode is the entry point's: `FloatMode.Fast`.** This is measured: `BurstFloatModeTests` pins it. A callee's own
  `[BurstCompile(FloatMode = …)]` is ignored when `RenderBlock` calls it.
  - `math.mad` fuses, sums may be reassociated, and NaN/Inf are assumed absent. So never test `float.IsNaN` in the hot
    path; clamp or flush instead.
  - Results stay within about 1e-6 relative of a strict render. Test against the Node vectors with a tolerance (burst.md
    section 7: 1e-5 absolute per sample).
  - For **bit-exact** checks, run the same code managed (`!BurstCompiler.IsEnabled`), or call your kernel from the test
    through a public `[BurstCompile(FloatMode = FloatMode.Default)]` entry of your own.
- **IL2CPP (managed code on the device):** never write `cond ? somePointer : null` in managed (non-Burst) code. IL2CPP
  6000.3 typed that merged value as `int32` and truncated the 64-bit pointer: it showed as a C++ `-Wint-to-pointer-cast`
  warning in the first APK build. Use `p = null; if (cond) p = …;` instead. Burst-compiled code is not affected. Check
  `Logs/ci/build.log` for that warning after a build.
- **Denormals** (burst.md section 6): flush every recursive state below `1e-20` at block end (`DspKernels.Flush`). Zero
  and retire voices below −110 dBFS. Zero a waveguide's lines when its string retires (`DspSetup.ZeroLine`). Never add DC
  or noise "guards".
- **CPU.** Your render is part of the graph's 2 ms per block. `u->Graph->Budget` is the adaptive LOD budget (1200 by
  default, 400–2048). Read it at block start and scale your own caps by `Budget / 1200f`; the engine lowers it when p99
  passes 60 % of the target. `DspLoadMeter` times the whole render. Time your unit alone offline (section 6.4).

## 4. The main-thread side

### 4.1 The factory

Each role has one factory. Implement yours in your own file:

```csharp
namespace MusicRoom.Audio.Burst
{
    public static partial class SoundUnits
    {
        static partial void CreatePiano(SoundContext ctx, ref SoundUnit unit) => unit = new PianoUnit(ctx);   // CreateDrums / CreateHarp / CreateRoom
    }
}
```

`AudioEngine.Init` creates the roles in the order piano, drums, harp, room, after the classic synths and before the graph
goes live on the sources. If the constructor throws, the error is logged and the classic sound plays that role.

### 4.2 The unit class

Write a subclass of `SoundUnit` that implements the role's interface:

| Role | Interface (from `IAudioEngine`) | Sources (channels) |
|---|---|---|
| Piano | `IPianoSound`: `Piano(midi, vel, when)` returns a `VoiceHandle`; `Release(h, thud, when)` | `PianoLo, PianoMid, PianoHi` |
| Drums | `IDrumSound`: `Drum(kind, vel, strike, when)`, `Choke(kind)` | `Kick, Snare, Hat, Tom1, Tom2, Floor, Crash, Ride` |
| Harp | `IHarpSound`: `Pluck(i, vel, when, in options)` returns a `VoiceHandle`; `Damp(h, hands, when)`; `HarpRingSeconds(midi)` | `Harp` |
| Room | `IRoomSound`: `SetMixedReality(mr)`, `SetRoomBox(center, size, real)` | none (listener stage) |

```csharp
public sealed unsafe class PianoUnit : SoundUnit, IPianoSound
{
    readonly PianoState* _s;
    public PianoUnit(SoundContext ctx) : base(ctx)                 // the base keeps ctx, so Alloc works before Register
    {
        _s = Alloc<PianoState>();                    // zeroed, 64-byte aligned, freed with the host
        // build tables here or on ctx.StartWorker(() => ...); write them into _s before the first command uses them
        Register(ctx, UnitId.Piano, new[] { SourceId.PianoLo, SourceId.PianoMid, SourceId.PianoHi }, _s, ringCapacity: 512);
    }
    public VoiceHandle Piano(int midi, float vel, double when)
    {
        Post(new GraphCommand { Op = (GraphOp)OpOn, Flags = CommandFlags.LateDrop, When = when, N = midi, A = vel });
        return UnitHandles.Make(Context.Source(SourceId.PianoMid), Id, ++_gen[midi - 21] << 7 | (midi - 21));
    }
    ...
}
```

| `SoundUnit` member | Use |
|---|---|
| `Register(ctx, role, outputs, state, ringCapacity = 256, pendingCapacity = 256)` | Once, in the constructor |
| `Alloc<T>(count)` / `Host.Alloc(bytes)` | Unmanaged memory, zeroed, 64-byte aligned, owned and freed by the host |
| `Stage(c)`, `Commit()`, `Post(c)` | Your ring. Stage every command of one call, then Commit once, so they land in the same block. |
| `Context.SampleRate`, `.Seed`, `.Clock`, `.Offline`, `.Source(id)`, `.StartWorker(job)` | Context: `Seed` is the session seed; `Clock` is the main-thread audio clock; `StartWorker` runs boot work on a lowered-priority thread and is polled through `IsDone` |
| `virtual Ready` | Diagnostics only: calls are routed to you even before you are ready. Queue or drop them yourself. |
| `virtual Tick()` | Every frame (`AudioEngine.Update`): poll a worker, publish parameters, post a pose |
| `virtual Seed(uint)` | `IAudioEngine.SeedAll`: post a command that re-seeds your audio-thread streams |
| `virtual OnPathChanged(bool active)` | The classic switch (section 8). Called once at start-up too. |
| `virtual Stats`, `virtual Dispose()` | A diagnostics line; managed clean-up |

### 4.3 Voice handles

Make your handles with `UnitHandles.Make(source, Id, payload)`, where the payload has 28 bits. Unit handles have
`Id < 0`; today's synths use positive ids. `AudioEngine.Release` and `Damp` route a unit handle to the unit that made it,
**even after the user switched to Classic**, so a note always gets its release. `UnitHandles.Unit(h)` and
`UnitHandles.Payload(h)` decode it.

### 4.4 Extra API (pedal, harp touch, hat openness …)

Do **not** grow `IAudioEngine`. Instead:

1. Declare your own interface in your folder, for example `public interface IPianoPedal { void PianoPedal(float depth, double when); void PianoKeyUp(int midi, double when); bool RestrikesInPlace { get; } }`.
2. Implement it on your unit.
3. The instrument module calls it through `audio.ActiveUnit(UnitId.Piano) as IPianoPedal`.

`ActiveUnit` is null while the classic sound plays that role, so the module falls back to today's behaviour by itself.
`IAudioEngine.ActiveUnit` has a default implementation, so the test fakes keep compiling.

### 4.5 Continuous parameters and stats

- **Main → audio** (pose, brightness, max active): either post a command, or write a field of your state and read it at
  block start. For multi-word values, use a seqlock: a version counter that is odd while writing.
- **Audio → main** (levels for visuals, stats): write fields of your state on the audio thread, and read them on the main
  thread with `Volatile.Read(ref _s->Field)`.

## 5. Shared engine services (optional)

- **Modal banks (MB4).** Add them on the main thread with `Host.AddBank(source, hz, tau, go, gi, strikeA, strikeB, velExp,
  phase, weight)`. Drive them with graph commands on `Host.Post` (`Strike`, `Pulse`, `Damp`, `Undamp`, `Silence`).
  - You get sample-accurate events, LOD and culling for free (burst.md section 5.2).
  - Their output goes straight to `Out[source]`, **not through your fade**. If you use banks, post `GraphOp.Silence` to
    them in `OnPathChanged(false)`.
- **One-shot pool** (burst.md section 5.4: 64 live + 16 ghost voices, quietest-first stealing into a 128-sample fade).
  - Build an `OneShotPreset` (up to 4 noise bands: `Biquad4` lanes, `EnvGain`/`EnvMul` per lane, `Mix`, `Length`,
    `LevelDb`) and add it with `Host.AddOneShotPreset`.
  - Then `Host.PlayOneShot(preset, gain, source, voiceId, when)` and `Host.ReleaseOneShot(voiceId, tau)`.
  - A start is sample-accurate. A steal and a release take effect at block start.
  - These voices also bypass your fade.
- **Kernels.** Call these from your render:
  - `DspKernels.ModalFree/Driven(List)`, `Waveguide4`, `Biquad4Process`, `Noise4Process`, `Exp4Process`, `MixRamp`,
    `Lane4Mix`, `AccResolve(Ramp)`, `ModalEndBlock`, `PeakAbs`
  - Setup: `DspSetup.SetMode/SetPole/Strike/SetString/SetBiquad(type, …)/DecayMultiplier/ZeroLine`
  - Noise seeding: `SplitMix32.SeedNoise`

## 6. Testing a new instrument (EditMode)

### 6.1 The offline engine

`OfflineEngine` builds the real graph and your real unit, through the same `SoundUnits` factory the app uses. It has a
sample clock of its own, so nothing depends on `dspTime`, threads or files.

```csharp
using var e = new OfflineEngine(UnitId.Piano);                     // or new OfflineEngine(roles, factory: ..., sources: false)
var piano = (IPianoSound)e.Unit(UnitId.Piano);
var y = e.RenderGraph(OfflineEngine.Samples(3.0), new[] { SourceId.PianoLo, SourceId.PianoMid, SourceId.PianoHi },
    t => { if (t == 0) piano.Piano(60, 0.8f, OfflineEngine.At(0.25)); });   // the event lands on round(0.25 * 48000)
float[] mono = OfflineEngine.Sum(y);
```

- `RenderGraph` returns what the graph put on each source: your unit, plus any banks and one-shots. No SynthSource is
  involved.
- `RenderSources` runs the 18 SynthSources exactly as the mixer thread does: today's voices, bodies, the graph slice, the
  sends and the bus. It returns each source's mono output before the spatializer.
- `beforeBlock(blockStart)` runs before each 256-frame block: post the events that block needs there. An absolute `When`
  in seconds maps to `round(t · 48000)`, which is the Node references' event rule.

### 6.2 Test vectors and Node renders

- Copy the vectors you need into `Tests/EditMode/Audio/<Key>/Reference~/`. Unity never imports a `~` folder. Do not read
  the lab at run time.
- Assert with a tolerance when Burst is on, and bit-exact when it is off: `static bool Exact => !BurstCompiler.IsEnabled;`
  (`BurstKernelTests` is the pattern).

### 6.3 WAV renders and null tests

```csharp
WavFile.Write(Path.Combine(Application.dataPath, "../Builds/Listen/piano/piano-C4.wav"), new[] { mono, mono }, OfflineEngine.Sr, 24);
var node = WavFile.Read(Path.Combine(Application.dataPath, "MusicRoom/Tests/EditMode/Audio/Piano/Reference~/renders/piano-single-C4-ff-new.wav"), out int sr)[0];  // 24-bit PCM or float
double db = NullTest.ResidualDb(mono, node);                                    // 10 log10(Σ(y − ref)² / Σ ref²)
double dbMatched = NullTest.ResidualDb(mono, node, gain: NullTest.BestGain(mono, node));   // the renders are loudness-matched
float click = NullTest.MaxStep(mono, from, to);                                 // largest sample-to-sample step
```

- Write renders under `Builds/Listen/<key>/`, which Unity ignores. They are deterministic, so a normal test
  can write them.
- The Node A/B files are stereo dual mono and loudness-matched to −18 LUFS. Compare channel 0 against your **dry** unit
  output with `BestGain`, or render with the same script as the Node file (the key's `render-ab.mjs`, e.g. `Tools/SoundLab/piano/render-ab.mjs`).

### 6.4 Performance

Time your unit alone in EditMode, with Burst on, using `Stopwatch` around `e.Host.RenderOffline(clock, 256)` over a few
hundred blocks of your worst case. Log a `[PERF]` line. `BurstGraphLoadTests` is the pattern (run with `Tools/unity.sh
test-perf`). In the live app, `engine.Graph.Load.Read()` gives avg, max and p99 per block on the audio thread.

## 7. The room and the master

- **Listener stage.** Implement `UnitHooks.ListenRoom(u, data, frames, channels, blockStart)`.
  - `MasterBus.OnAudioFilterRead` calls it through its own Burst entry, `ListenerGraph.Render` (**FloatMode.Default**,
    IEEE). It runs on the final interleaved mix (HRTF direct paths of every source), and you process `data` in place.
  - While your unit is registered and not suspended, it **replaces** today's master (compressor, shelf, ceiling).
  - While your fade runs (`u->GainStart` → `u->GainEnd` over the block), `MasterBus` runs both masters and crossfades them
    for you.
- **Instrument signals.** In your constructor, call `Host.EnableTap(SourceId)` for each instrument source you need.
  - Each SynthSource then copies its post-bus mono block into its tap, stamped with the block start.
  - Store `Host.Tap(id)` and `Host.TapStamps` in your state, and read them in `ListenRoom`. In `RenderRoom`, which runs
    inside `RenderBlock`, the taps still hold the **previous** block.
  - A stamp that differs from `blockStart` means the source has not run yet in this block. Count it as a miss: the room
    spec's `RoomBus.Misses`.
- **Mixed reality.** `IRoomSound.SetMixedReality` and `SetRoomBox` reach your unit; today's Meta room keeps receiving
  them too.
- **Toggling Meta's acoustics.** Do it on the main thread in `OnPathChanged`: `EnableAcoustics` per source, the
  drum-room bus, `MetaXRAcousticSettings`. Restore today's values when it is called with `false`.

## 8. "Classic sound"

The setting is `IAudioEngine.ClassicSound { get; set; }`, persisted as `AppSettings.KeyClassicSound`
(`musicroom.classicSound`). It is the menu row **Classic sound** on the Instruments screen.

- **Default: off, the new sound.** Every role with a unit plays its unit. A role without a unit plays today's synth in
  both settings.
- **Switching on** (`OnPathChanged(false)`):
  - Your channels fade out over `AudioEngine.SwitchFadeSamples` (1024 samples, 21 ms), then the engine suspends you: no
    render, events dropped, no CPU.
  - New calls go to today's synth.
  - Today's piano and harp buffers are rendered **only now**, the first time. A role waits on the new sound until they are
    ready (about 1 s); `ClassicSoundPending` is true meanwhile, and the menu shows "Preparing…".
- **Switching off** (`OnPathChanged(true)`):
  - Today's voices on your sources get `SynthOp.FadeAll` (release, tau 15 ms).
  - You resume at once with `UnitFlags.Reset`. Clear everything, because nothing of yours is ringing.
- **Start-up.** The default boot no longer renders today's piano or harp buffers for a role that has a unit (−67.7 MiB
  for the piano, −12.2 MiB for the harp). A session that starts with Classic on renders them as before.
- **Tests of today's engine.** `AudioEngineTests`, `AudioAllocTests` and `AudioLoadTests` boot with Classic on
  (`_boot.Settings.SetInt(AppSettings.KeyClassicSound, 1)`). Do the same in any test that asserts today's recipe details
  (`AudioTestTap.Commands`). Tests that assert `AudioTestTap.Calls` work on either path.
- **Forcing your unit off in a test:** `SoundUnits.Override = (role, ctx) => null`. Reset it to null in TearDown.
  `AudioEngine.UseGraph = false` (static, before boot) turns the whole Burst graph off: today's engine exactly as before.

## 9. What to report, and which shared files may change

Should be none. If unavoidable, keep the change minimal and list it:

| Shared file | Likely reason | Prefer instead |
|---|---|---|
| `IAudioEngine.cs` | A new instrument call | Section 4.4 (`ActiveUnit` plus your own interface) |
| `AudioEngine.cs` | Routing a new call | Your unit, reached through `ActiveUnit` |
| `AudioRig.cs` | Pools, sends, buses for the new path | Runtime changes in `OnPathChanged` (keep today's values for Classic) |
| `SynthSource.cs`, `MasterBus.cs` | A new per-source or listener stage | Taps (section 7), `ListenRoom` |
| Instrument modules (`Piano/`, `Drums/`, `Harp/`) | Pedal, touch, hat openness, velocity curves | Yours to change (your spec says so); keep them working with `ActiveUnit == null` |

Report your unit's offline cost per block (section 6.4), its arena and `Alloc` bytes, and the null results against the
Node renders.

---

## 10. Scheduling a call: the `AudioSchedule` scope (added by the looper)

**What it is.** The looper (Lessons) plays recorded layers back through the ordinary instrument playback calls
(`IPianoApi.PlaybackOn`, `IDrumsApi.PlaybackHit`, `IHarpApi.PlaybackPluck`, ...). To make them sample-accurate without
changing any instrument signature, it sends each event about 80 ms ahead on the audio clock inside an ambient scope:

```csharp
using (audio.ScheduleAt(when)) piano.PlaybackOn(60, 0.8f);   // `when`: absolute audio seconds (IAudioEngine.Now's clock)
```

Every command the instrument issues inside the scope that would play "as soon as possible" plays at `when` instead, to
the sample, on today's synths and on your unit alike. Files: `Audio/Engine/AudioSchedule.cs` (the scope),
`IAudioEngine.ScheduleAt` (a default method: `AudioSchedule.Begin(when)`; test fakes need nothing).

**The rule** (main thread, like every engine call). Inside a scope at T > 0:

| A `When` of | becomes |
|---|---|
| `0` (ASAP) | `T` |
| `< 0` (relative: `-s` seconds after the batch) | `T + s` |
| `> 0` (absolute) | unchanged (the caller computed it) |

Scopes nest (the inner wins); `Dispose` restores the outer one; `Begin(when <= 0)` is a no-op scope. `Scope` is a struct:
`using` never boxes, nothing allocates.

**Where it is applied** (so nothing an instrument or a unit issues can escape it):
1. `AudioEngine.Piano / Release / Pluck / Damp / Drum`: the `when` argument is stamped first, so **your unit's
   `IPianoSound.Piano(midi, vel, when)` etc. receive the absolute time T** (and `AudioTestTap.Calls` records it). `Choke`
   has no `when`: it records `When = AudioSchedule.At`, and its commands are stamped below.
2. `SynthKit.Stage` (today's synths): instrument sources only (`PianoLo` .. `Ride`). Sunny, the UI (the metronome click)
   and the ambience are never stamped.
3. `SoundUnit.Stage` / `Post`: every role but the room. **Your own extra API** (e.g. `IPianoPedal.PianoPedal(depth, 0)`
   called by the piano module inside the scope) is therefore scheduled too, as long as it stages through `Stage`/`Post`.
4. `AudioGraphHost.Stage` / `Post` (banks: `Strike`, `Pulse`, `Damp`; `PlayOneShot`, `ReleaseOneShot`).

**What a sound port must do: nothing, if** it (a) passes the `when` it was given into its `GraphCommand.When` (or
leaves `When = 0` for "now"), and (b) never computes its own absolute time from `Context.Clock()` for an onset that
should follow the call (that would bypass the scope). A unit that schedules internal follow-ups relative to an onset
should use the onset's `when` (absolute inside a scope) or a negative relative `When`.

**Late events.** A stamped onset arrives up to 80 ms ahead, so `CommandFlags.LateDrop` never fires for it in normal
play; the looper drops onsets that are more than 100 ms late itself and always sends releases.

**Classic sound.** A switch while commands are scheduled ahead: the outgoing path drops or fades them (units are
suspended, today's voices get `FadeAll`, which kills voices whose start is still ahead). The looper watches
`IAudioEngine.ActiveUnit(role)` and sends what it scheduled from one block ahead again on the new path, so no loop note
is lost and none doubles (`Tests/PlayMode/Lessons/LooperPlayTests.ClassicSoundSwitchedMidLoop...`).

**Tests.** `Tests/EditMode/Audio/Burst/ScheduleScopeTests.cs`: the rules, nesting, reset, today's drum and the Example
unit (piano role) starting on the scope's exact sample offline, the Ui source untouched, no allocation.
`Tests/EditMode/Audio/Burst/ScheduleScopeSoundUnitTests.cs` (added with the looper,
2026-11-01): the same through the shipped piano, drum and harp units. Scoped calls (When 0 or relative, the units' extra
APIs included) render bit for bit like explicit times; each unit sounds from exactly the scope's sample at any block
offset; a 14-pass loop on the new kit dispatched like the looper renders bit for bit like exact posting (no drift).

**Also added for the looper:** `IAudioEngine.Click(bool accent, double when)` (a default no-op; `AudioEngine` plays
`UiSounds.Click`, a soft wooden tock on the 2D Ui source) and `AudioCall.Click` for the tap.

## 11. The many-instrument platform's additions (12-new-instruments/platform.md 10, phase 0)

- `UnitId` 5-9 (Handpan, HandDrums, Marimba, Theremin, Bowls; Count 10); `UnitHandles`: 4-bit unit field, **27-bit payloads**
  (`Make = -(1 + ((payload & 0x07FFFFFF) << 4 | unit))`).
- `SoundUnit.Register(ctx, role, outputs, state, ringCapacity, pendingCapacity, kernel, bool idleSkip = false)`: with
  `idleSkip` the graph stops rendering the unit after 8 blocks with nothing due or pending, no fade and `ChannelsUsed == 0`;
  its next render gets `UnitFlags.Resumed` (2) once.
- `UnitSlot.Share` (main -> audio, default 1): scale your caps by `u->Graph->Budget / 1200f * u->Share` at block start;
  `UnitSlot.Work`: write your own work count each block; `Class`, `IdleBlocks` are the engine's.
- Every public method of a unit-only role calls `RecordUnit(op, index, vel, when)` (`AudioCall.Unit`).
- `ILoudnessReference { double ReferenceSeconds; void PlayReference(double start); }`: a real unit's mf reference phrase
  (`LoudnessParityTests`: within +-2 LU of the piano's -24.15 LUFS).
- `IRoomGroups` (the room unit): per-tap early-reflection groups and group points written through the pose seqlock
  (`RoomGroupAssigner`).
- `IAudioEngine.AddOneShotPreset / PlayOneShot / ReleaseOneShot`: the graph's pooled one-shots for modules without a unit.
- The instrument agents' how-to is Docs/12-new-instruments/BUILD-GUIDE.md section 4.3.

