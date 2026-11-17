# MusicRoom.Audio

Every sound in the room, synthesized by our code: the synth recipes of spec 06 in C#, **nulled against the reference
renders** of those synths (`Tests/EditMode/Audio/Reference~`). References `MusicRoom.Core`, `Meta.XR.Audio`.
Specs: ARCHITECTURE.md section 7 (7.5 = this module's stage-3 checklist), `Docs/06-audio-dsp.md` (+ `.reference.mjs`;
reference WAVs in `Tests/EditMode/Audio/Reference~`), `Docs/04-unity-audio-engine.md`.

Built on the review-round-1 foundation (ARCHITECTURE 21): this folder is a three-way merge of `foundation-v1` (base), the
revised foundation's Audio folder and this module's recipe work, so it carries both (see "Rebase" below).

## The classic sound only (2026-11-10)

After the headset test the user chose the original sound: the piano, the drums, the harp and the room always play
today's synths (`Synths/PianoSynth`, `DrumSynth`, `StringSynth`, the drum room), Meta's room acoustics and `MasterBus`'s
chain. There is no switch any more (no menu row, no `IAudioEngine.ClassicSound`); a save's old `musicroom.classicSound`
(`AppSettings.KeyClassicSoundOld`) is deleted once by `AudioEngine.Init`. The engine creates only the unit-only roles'
units (`SoundUnits.LiveRoles`, the roles with no classic synth) through the Burst graph; the upgraded
`PianoUnit`, `DrumUnit`, `HarpUnit` and `RoomUnit` (`Sound/Piano`, `Drums`, `Harp`, `Room`) are never created, so they
cost no CPU or memory, and today's piano and harp buffers always render at start-up. The sections below are the history
of the upgrade: where they describe "Classic sound" switching, it is gone.

**Deleted (2026-11-11, "I want only classic sound").** `Sound/Piano`, `Sound/Drums` (with `Resources/MusicRoomDrumKit.bytes`
and its `Tools~`), `Sound/Harp`, `Sound/Room`, `Engine/RoomGroupAssigner.cs`; in the files that stay: the room's listener
stage (`ListenerGraph`, `AudioGraphHost.RenderListener` / `ListenerLoad` / `ListenerLatency`, `MasterBus`'s crossfade,
`Shadow` and 53-sample delay line: it is today's chain only), the source taps (`EnableTap` / `Tap` / `WriteTap`,
`GraphBridge.Tap`), the `UnitHooks` / `SoundUnits` partials of the four roles (`SoundUnits.Roles` = the four unit-only
roles; `UnitId` keeps Piano 0, Drums 1, Harp 2, Room 3), `SoundUnits.ClassicSources`, `SoundUnit.OnPathChanged`,
`AudioGraphHost.SuspendNow`, `SynthOp.FadeAll`, `AudioEngine.ClassicReady`, the interfaces only those units implemented
(`IPianoSound`, `IPianoPedal`, `IDrumSound`, `IDrumKitSound`, `Burst.IHarpSound`, `IHarpStrings`, `IRoomSound`,
`IRoomGeometry`, `IRoomGroups`, `IRoomControl`), `AudioSourceRow.RoomGroup` / `RoomPoint` / `RoomDy` and Core's
`AudioSpec.ErOrigin`. `HarpDecay` (today's harp's per-partial decay for the string blur) moved to
`Engine/HarpPartialDecay.cs`. The unit-only roles' loudness reference is the lab's piano mf reference
(`Loudness.PianoMfLufs`, -24.15 LUFS, the level they were tuned to). Backup:
`music-room-vr-wip/unity-backups/pre-classic-cleanup/`.
Tests: `PlayMode/Audio/ClassicSoundTests`, `PlayMode/Piano/PianoClassicPlayTests`, `PlayMode/Harp/HarpClassicPlayTests`,
`PlayMode/Audio/DrumKitPlayTests`, `AudioEngineTests`, `AudioAllocTests`.

## The many-instrument platform (12-new-instruments/platform.md 10, phase 0, 2026-11-01)

- **25 sources.** `SourceId` 18-24: `Handpan`, `HandDrumLo`, `HandDrumHi`, `MarimbaLo`, `MarimbaMid`, `MarimbaHi`,
  `Bowls` (0-17 never move). `Engine/AudioRig.cs` `AudioSourceTable`: one row per source (spatial, Meta acoustics, VR send,
  reach, ER, pools, room tap / group); the 18 classic rows are today's values (pinned by `AudioSourceTableTests`); the new
  rows are unit-only (pools 0, acoustics on, reach 0.7; sends Handpan 0, hand drums -3 / ER +3, marimba -2 / 0 / -2,
  bowls -1 dB; MR -6); a descriptor's `AudioSourceSpec.SendDb / ErDb` overrides its rows at boot.
- **Four unit-only roles.** `UnitId` 5-9 (8 retired, Count 10), `UnitHandles` 4-bit unit field (27-bit payloads), `SoundUnits.Roles`
  (4 since 2026-11-11), `Create<X>` / `Render<X>` partials pre-declared, `SoundUnits.IsUnitOnly`: the only roles the app
  creates a unit for (`SoundUnits.LiveRoles`), and `ActiveUnit` always returns their unit. Phase 0 plays the platform's placeholder knock (`Burst/Units/KnockUnit.cs`) in
  each role (`Sound/<X>/<X>StubUnit.cs`); the instrument agents replace those files.
- **Idle skip, Share, Work.** `SoundUnit.Register(..., idleSkip)`; the graph skips a silent unit's render after 8 blocks
  (`IdleBlocks`), its next render gets `UnitFlags.Resumed`; `UnitSlot.Share` (set by `Burst/UnitGovernor.cs` per class:
  Focus, AtSeat, Playback, Tail), `Class`, `Work` (the unit's own count, on the dev HUD).
- **AudioPresence** (`Engine/AudioPresence.cs`, `IAudioPresence`): displayed instruments' sources pause after 1 s of silence
  and resume on a bring, on `Wake` (playback dispatch) or when the unit sounds. A hidden instrument (its group inactive:
  mixed reality off the seat, a blocked display spot) is still heard (2026-11-11): its sources wait on the audio host at
  their last world pose, active, and go back under it at their own place when it shows (`Parks` / `Unparks`,
  `IsParked`); before, they went inactive with its group and a take's notes for it were silent (the kit parked its own).
- **The room** (`Sound/Room`): 20 taps, 4 early-reflection groups assigned per frame by `Engine/RoomGroupAssigner.cs` (the
  at-seat set, then recently sounding displayed instruments); the default set renders bit for bit as before.
- **Sunny's voice** (`Synths/GuideVoice.cs`, `Synths/VoiceManifest.cs`): the Guide's manifest merged with every
  `Resources/VoiceManifests/*.json`; the bank grows with the lines (>= 512 slots); lines render lazily (the two intros at
  boot), <= 24 resident.
- **Test and module hooks:** `AudioCall.Unit` + `SoundUnit.RecordUnit` (`AudioTestTap.CountUnit`), `ILoudnessReference`
  (`LoudnessParityTests`), `IAudioEngine.AddOneShotPreset / PlayOneShot / ReleaseOneShot`; `Burst/NoiseShots.Register`
  builds a filtered-noise one-shot preset without unsafe code in the caller (the Room's thrown-paint splat).
- Tests: `Tests/EditMode/Audio/Platform/` (UnitId, AudioSourceTable, IdleSkip, Governor, UnitShare, RoomGroups,
  LoudnessParity, VoiceManifestMerge), `Tests/PlayMode/Audio/AudioPresenceTests`, `EightInstrumentsLoadTests` (Performance).

## Burst engine and sound units (10-upgrade, 2026-11-01)

The audio-thread architecture of `Docs/10-upgrade/burst.md`, plus the plug-in point for the upgraded instruments
and the room (`Docs/10-upgrade/ENGINE-API.md`, the contract the piano / drums / harp / room workstreams build on).

- **Packages and assembly**: `com.unity.burst` 1.8.31 and `com.unity.mathematics` 1.3.3 are direct dependencies (the
  versions URP already resolved, so nothing re-resolves); `MusicRoom.Audio` (and both test assemblies) reference
  `Unity.Burst` / `Unity.Mathematics` and allow unsafe code. No new asmdef.
- **`Burst/`** (burst.md section 15's skeletons, compile-checked, now live):
  - `BurstDspTypes`, `DspKernels`, `DspSetup`: MB4 / WG4 / BQ4 / NZ4 / EXP4 float4 layouts and Burst kernels in the
    canonical `math.mad` order (section 5.1), FloatMode.Fast; engine additions `Lane4Mix`, `MixToward`, `PeakAbs`,
    `SetBiquad(type, ...)`, `ZeroLine`, `DecayMultiplier`.
  - `AudioGraph.RenderBlock`: ONE Burst direct call per block (`AudioGraphHost.EnsureRendered` from the first
    `SynthSource` of the block, `ProfilerMarker` "MusicRoom.AudioGraph.Render"): graph ring -> modal banks
    (sample-accurate strike / pulse / damp / undamp / silence, culling, fades) -> one-shot pool -> sound units -> LOD.
  - `GraphArena` (one 64-byte aligned arena, 1.55 MiB for the Quest config: the spec's 1.39 MiB + the one-shot pool, its
    lane scratch and the unit slots), `NativeCommandRing` (64-byte `GraphCommand`, SPSC, ops `OneShot`, `Release`,
    `UnitFade` added), `VoiceAllocator` (one-shots: 64 live + 16 ghosts, quietest-first stealing, older than 30 ms,
    released -20 dB, into a 128-sample ghost fade).
  - **CPU cap**: LOD budget 1200 groups (2 dB buckets from -130 dB, just-struck banks protected 30 ms, the quietest
    groups fade over one block); `AudioGraphHost.Tick` adapts it once a second from `DspLoadMeter` p99 (> 60 % of 2 ms:
    x 0.9; < 30 % with drops: x 1.05; 400..2048). Below 30 % without drops a lowered budget also climbs x 1.05, back to
    the configured 1200 and no further (`AdaptBudget(..., restore: Config.Budget)`): the piano and harp units scale their
    own caps by it and drop no graph bank, so before this one heavy second left them degraded for the session
    (review 2026-11-01; `TheAdaptiveBudgetRecoversAfterALoadSpike`).
  - **Denormals** (section 6): every recursive state flushed below 1e-20 at block end, groups below -110 dBFS retire
    zeroed, `DenormProbe` for the device test T-DENORM (editor ratio 1.02).
  - `DspLoadMeter` (avg / max / p99 in 50 us bins / over-budget / dspTime gaps) and `DspLoadLog` (development players:
    the `[Audio] dsp avg ...us max ...us p99 ... work <unit> <share>%` log line once a second; nothing on screen, the
    head-locked "DSP" HUD readout was removed after headset feedback).
  - `ListenerGraph.Render`: the listener stage (its own Burst entry, FloatMode.Default) for the room unit.
- **Sound units** (`Burst/Units/`): a unit is one role's DSP (piano, drums, harp, room) with its own unmanaged state,
  command ring and Burst render, called inside `RenderBlock`; it writes mono channels (one per SynthSource) that the
  engine mixes into the sources with the unit's fade ramp. Units plug in through two partial methods
  (`UnitHooks.Render<Role>`, `SoundUnits.Create<Role>`) implemented in `Audio/Sound/<Role>/`, so the four workstreams add
  files only. `ExampleUnit` is the reference unit (eight decaying sines) the engine's own tests use in the piano role.
- **Bridge**: `SynthSource.RenderBlock` renders the graph (`GraphBridge.Render`), never idle-skips a source with graph
  signal, adds its slice after today's voices, processors and body and before the send and the bus (so the new
  instruments keep the drum-room send, bus, spatializer and master), and writes its post-bus tap when enabled
  (`AudioGraphHost.EnableTap`, for the room). `MasterBus` hands the final mix to the room unit's listener stage when one
  plays, crossfading with today's chain while it fades in or out.
- **"Classic sound"** (removed 2026-11-10): the switch between the units and today's synths is gone; the piano, drums,
  harp and room always play today's synths and their buffers render at start-up.
- **Teardown**: `AudioModule.Shutdown` / `AudioEngine.OnDestroy` -> `ShutdownGraph` takes the host off the sources and
  the listener, then `AudioGraphHost.Dispose` waits for the audio thread to leave (Enter / Exit counters, no lock) before
  freeing the arena.
- **Editor setup**: `Editor/BurstAudioSetup.Apply` (called by `QuestSetup.ConfigureAll`, so `Tools/unity.sh configure`):
  IL2CPP ARM64, Release, "Faster runtime" code generation, method-only stack traces, and
  `ProjectSettings/BurstAotSettings_Android.json` (Burst on, optimisations on, safety checks off, Armv8-A + Armv8.2
  half-float targets, OptimizeFor Performance, FloatMode Default; the kernels opt into Fast per entry point).

Measured (editor, Burst on, Apple M5 Pro with other agents' Unity running, `Tools/unity.sh test-perf`, two runs on
2026-11-01): MB4 free 0.139-0.193 ns per mode-sample, driven 0.202-0.218, WG4 0.47-0.52 ns per string-sample, BQ4 0.49 ns
per filter-sample; the graph at the LOD ceiling (1200 groups = 4800 modes, plus 64 one-shots) **0.25-0.27 ms average,
0.30-0.45 ms p99, 0.36-0.55 ms worst per 5.33 ms block on the live audio thread** (0.26-0.30 ms average offline on the
main thread); an idle graph 0-4 us per block. APK with Burst AOT (arm64 `lib_burst_generated.so`, 0.79 MB): 65,356,099
bytes (+0.23 MB).

## Room and master v2 (10-upgrade room.md, 2026-11-01)

`Sound/Room/` holds the room unit (its README has the details). While the new sound plays, it replaces Meta's reflections
and reverb, the drum room and today's master: image-source ER (order 2, per-wall materials, per-band directivity, a
head-tracked spherical-head ear model), the 32-line FDN late tail calibrated per octave, and MasterBusV2 (RMS leveler,
shelf, 4x true-peak limiter at -1 dBTP, trim -3 dB), all in Burst, in the listener stage. "Room amount" is persisted as
`musicroom.roomAmount`. Mixed reality models the MRUK box (`IAudioEngine.SetRoomBox(center, size, yawDeg, realRoom)`).
The A/B pieces null against the Node renders at -103 ... -128 dB.

Engine changes for it:
- `AudioGraphHost.ListenerLoad` times the listener stage.
- A channel-less unit (a listener stage) ramps in when it resumes and sleeps one block after its fade, so `MasterBus`
  crossfades the two masters both ways.
- With a room unit registered, `MasterBus` delays today's chain by the room master's 53-sample look-ahead
  (`AudioGraphHost.ListenerLatency`), so the two masters crossfade time-aligned (no comb filter on a held note). While the
  room master plays alone, a cheap shadow of today's chain (gain, shelf, ceiling, the delay line) keeps it current, and
  its compressor settles (`DynamicsCompressor.Settle`) when it takes over again.

## Integration of the four units (2026-11-01)

(History: since 2026-11-10 the app creates none of these four units; the classic sound is the only sound.) All four
roles had their unit in the shipped project (piano, drums, harp, room). Merged from the workstream clones by the integrator (ARCHITECTURE.md 21):
- `IAudioEngine.HarpPartialTau` is the harp port's (double; the unit's own decays), not E8's float default; the harp module
  casts it (`EngineSound.PartialTau`).
- Switching to Classic with all units present: the drums switch at once; the piano and harp roles wait for today's
  buffers (`ClassicSoundPending` covers only those), as the engine designed.
- All sounds at their worst at once, in one graph (`Tests/PlayMode/Audio/AllSoundsLoadTests`, editor, Burst, M5 Pro with
  another agent's Unity running): **0.342 ms average, 0.507 ms p99, 0.548 ms worst block** per 5.33 ms block offline
  (piano palms at the 2,000-mode cap + all eight drums every 120 ms + a 22-string harp glissando every 4 blocks + the
  room with a moving head); live on the audio thread the graph block averages 0.283 ms (p99 0.61 ms, 0 blocks over budget)
  and the listener stage 0.112 ms (p99 0.15 ms). Quest 3S expected about 3-4x: device check ARCHITECTURE.md 19 item 40.
- Listening demos through the integrated engine (`Tests/EditMode/Audio/FinalDemoTests`, every EditMode run): each
  instrument as the new sound, the new sound without the room, and Classic, in
  `Builds/Listen/final/` (the piano played through the real `PianoAction`; a plain
  stereo pan stands in for Meta's HRTF, and Classic has no Meta room reverb offline). `OfflineAudio.RenderEach` returns
  each source's own output for that mix.

## Scheduling: the `ScheduleAt` scope and the metronome click (the looper, merged 2026-11-01)

From the looper workstream (ENGINE-API.md section 10). `Engine/AudioSchedule.cs` and `IAudioEngine.ScheduleAt(when)` (a
default method, nothing to implement in a fake): inside `using (audio.ScheduleAt(t)) piano.PlaybackOn(60, 0.8f);` every
instrument command that would play as soon as possible plays at `t` (absolute audio seconds), a relative one at `t + s`,
an absolute one is kept; scopes nest; a struct, no allocation. Stamped in `AudioEngine.Piano / Release / Pluck / Damp /
Drum` (and `Choke`'s recorded `When`), `SynthKit.Stage` (today's synths, instrument sources PianoLo..Ride only: Sunny,
the UI and the ambience never), `SoundUnit.Stage` / `Post` (every unit but the room, so the units' own extra APIs are
scheduled too: the piano's `PianoPedal` / `PianoKeyUp`, the kit's `Choke` / `ChokeAt` / `SetHatOpenness`, the harp's
`HarpTouch` / `HarpHold` / `HarpDampString`) and `AudioGraphHost.Stage` (banks and one-shots).
`IAudioEngine.Click(accent, when)` (default no-op; `UiSounds.Click`, a soft synthesized wooden tock on the 2D Ui source)
and `AudioCall.Click`. Guide lines: `GuideVoice.LineIds` has 38 (Sunny's original 22, `tip_arrange`, the looper's 15), slots
200..237 of the 240-slot bank.

Through the shipped sound units, not only today's synths (`Tests/EditMode/Audio/Burst/ScheduleScopeSoundUnitTests`,
added at the merge; `ScheduleScopeTests` covers today's drum and the Example unit):
- every call the instrument modules make, inside a scope with When 0 or a relative When, renders bit for bit what the
  same call at the explicit absolute time renders, for the piano (strikes, a rolled chord, pedal, key-up, releases), the
  kit (kick, hat openness, open hat, crash, a hand choke without `when`, rim, closing hat, tom) and the harp (plucks, a
  sweep's delay, touch, damp, palm): no command of a unit escapes the scope;
- each unit is silent before the scope's sample and sounds from exactly that sample (lag 0) at block offsets 0, 17, 131
  and 255: sample-accurate, not block-accurate;
- a 14-pass loop on the new kit at 110 BPM, dispatched from frames 6-51 ms apart up to 80 ms ahead the way `Looper` does,
  renders bit for bit like the same hits at their exact samples, and every pass's kick starts on its own sample (no
  drift); the seconds-to-samples round trip is exact for 3 days of audio clock.
The looper on the classic three: `Tests/PlayMode/Lessons/LooperPlayTests` (every looped note on its sample).

## Public contract (ARCHITECTURE.md 7.3; unchanged)

- `IAudioEngine` (registered by `AudioModule`, Order 10): `Piano/Release`, `Pluck/Damp/HarpRingSeconds`, `HarpPartialTau`
  (10-upgrade E8 + harp port, a default interface member returning `double`: the new harp's loop decays while its unit
  plays, else the old synth's formula, `HarpDecay.ClassicPartialTau` = `HarpPartialDecay.OldTau`; `HarpPartialDecay.V2Tau`
  is the v2 design decay the harp module's test doubles use), `Drum/Choke`,
  `SetFire/SetWind/SetSnow/FireOn/WindOn/SnowOn`, `SnowGust(strength, attack, hold, release)`, `Source/SetSourceParent/
  SetSourcePosition`, `SetMixedReality`, `SetRoomBox`, `Chirp`, `HasVoiceLine/VoiceLineSeconds/PlayVoiceLine/StopVoiceLine/
  VoiceLinePlaying/VoiceLevel`, `Swish`, `TestTap`, `SeedAll`, `IsReady/SampleRate/Now/FrameNow/RenderProgress`.
  `Now`/`FrameNow` honour `BootOptions.AudioClock` (`Engine.Init(sources, 0, app.AudioClock)`); the mixer always runs on
  `dspTime`.
- `AudioTestTap`: `Calls` (one `Call` per engine method: `AudioCall` + index / vel / strike / pluck / when / flag / hands /
  line / handle; `Count(AudioCall)`), what instrument tests assert; `Commands` (`Count(SourceId, SynthOp)`), this module's
  recipe detail.
- Types: `SourceId` (18), `DrumKind`, `DrumZone`, `DrumStrike`, `PluckMode`, `PluckOptions`, `ChirpKind`, `VoiceHandle`,
  `AudioCall`, `SynthCommand`/`SynthOp`/`Wave`, `SynthSource` (stats `Rms`, `OutputEnergy`, `BlocksRendered`, `IdleBlocks`,
  `DroppedCommands`, `StolenVoices`, `ActiveVoices`, `Avg/Max/LastBlockMicros`, `ResetStats`).
- Constants other modules use: `AudioEngine.PianoSlot0/HarpSlot0/VoiceSlot0`, `AudioModule.VrRoomCenter/VrRoomSize`,
  `GuideVoice.Lead` (0.14 s), `GuideVoice.LineIds`.
- Knobs and diagnostics: `AudioModule.PreferredDspBuffer` (256; try 128 on the headset, `AudioSettings.Reset` once in
  `Init` before any source exists), `AudioEngine.LatencyProbe` (dev device builds: one `[Audio] latency-probe` line per live
  piano / drum / pluck call), `AudioEngine.PreallocatedBytes`, `FirstKeySeconds`, `RenderSeconds`, `Seed`,
  `SumAvgBlockMicros/SumMaxBlockMicros/TotalDroppedCommands/TotalStolenVoices/ResetBlockStats`,
  `OfflineRenderQueue.PriorityLowered`; `AudioModule.StartupReport/RenderReport/LogBudget`.
- Concrete-class extra (no interface change): `AudioEngine.SnowGust(in Core.SnowGust)` for `ISnowWeather.OnGust` subscribers.
- 10-upgrade additions (default interface members, so fakes keep compiling): `ActiveUnit(UnitId role)` (the role's unit,
  for a unit's own extra interface; `IAudioEngine.ClassicSound` / `ClassicSoundPending` were removed 2026-11-10);
  `AudioEngine.Graph`, `Unit(role)`, `ClassicReady(role)`, `ShutdownGraph()`, `UseGraph`; `Init(sources, seed, clock, settings)`; `SynthSource.Graph`;
  `SynthOp.FadeAll`; `MasterBus.ListenerBlocks`; `AudioModule.DspLog`.

Wiring owned by other modules (no contract change needed): Room calls `SetFire/Wind/Snow` from `IAmbienceSwitches.Set`
and **`IAudioEngine.SnowGust(g.Strength, g.Attack, g.Hold, g.Release)` from `ISnowWeather.OnGust`** (the gust heard is the
gust seen: same frame, same shape; Audio does not subscribe itself, so gusts never double); Piano/Harp/Drums/Guide parent
their sources (`SetSourceParent`); MR moves Snow/Wind to the window portal and calls `SetMixedReality`/`SetRoomBox`.

## Layout

- `Dsp/`: `Biquad` (RBJ, Q in dB for low/high-pass), `Envelope` (ramp-automation subset, fractional ramp lengths, `IsQuiet`), `Noise`,
  `Oscillators` (`PeriodicWaveTables` exact + the bit-identical nested-sum fast build, `Oscillator` with the table range
  selected once per frequency and `RangeStep` per glide sample, PolyBLEP fallback, `MetalBank` = the pre-rendered six-square
  sums per metal ratio), `ModalResonator` (body IR as resonators **minus a delayed cancel bank = the truncated
  convolution**), `Fft`, `DynamicsCompressor`, `Notes`.
- `Engine/`: `SpscRing`, `SynthCommand` (+ `SynthFlags`, `SynthCustom`), `SynthSource` (`RenderBlock` is the audio-thread
  mixer, also driven offline by the tests; `ISynthProcessor` with `Process` and `Skip`), `MasterBus`, `AudioRig`, `SynthKit`,
  `IAudioEngine` (+ `AudioTestTap`), `AudioEngine`.
- `Voices/`: `Voice` (sample-accurate start/release/stop + filter ring-out), `NoiseVoice`, `ToneVoice` (render-quantum start rules),
  `MetalVoice` (reads `MetalBank`), `BufferVoice`.
- `Offline/`: `SampleBank`, `OfflineRenderQueue` (2 workers, Android JNI priority), `PianoRenderer`, `HarpRenderer` (old
  sound; `RenderInto` variants write into preallocated buffers with per-thread scratch).
- `Synths/`: `PianoSynth`, `DrumSynth`, `StringSynth`, `Ambience` (`NoiseBed`, `FirePops`, `SnowGustProcessor`),
  `GuideVoice` (+ `RobotVoice`, `Resampler`, `UiSounds`), `DrumRoomReverb` (partitioned FFT convolution).
- `Resources/MusicRoomMixer.mixer`: Master -> Attenuation -> Meta XR Audio Reflection, auto-suspend off.
- `Burst/`: the Burst engine (above); `Burst/Units/`: `UnitSlot` + `UnitHooks`, `SoundUnit` + `SoundContext` +
  `UnitHandles` + `SoundUnits` + the role interfaces, `ExampleUnit`.
- `Sound/<Piano|Drums|Harp|Room>/`: the four workstreams' units (empty in engine-base, README stubs).

## Behaviour

Graph and threading as ARCHITECTURE 7.1: main thread stages `SynthCommand`s per source (one committed batch per call);
the mixer thread drains, renders voices sample-accurately, runs processors and the body resonator, sends to `sendTarget`,
applies the bus. Nothing on the audio thread allocates, locks or logs. **Every render output** (88 piano keys, 22 harp
strings, 22 robotised lines and their mouth envelopes: 89.7 MiB) **is allocated on the main thread in `AudioEngine.Init`**;
the two workers fill them in place and reuse per-thread scratch (piano accumulator and noise, robot comb), so they make no
large garbage while Sunny's intro plays. On Android each worker lowers itself with `Process.setThreadPriority(10)` through
JNI (IL2CPP's `Thread.Priority` is a no-op) and detaches on exit.

Render order: the periodic-wave tables + `MetalBank` and middle C first (two workers: they run side by side), C4 +- 2 semitones,
then Sunny's two intro lines, the rest of the piano outward from C4 with the other 20 lines after C4 +- 12, the harp last.
`FirstKeySeconds` (Init to middle C rendered) is in the "renders done" log; budget 0.15 s (section 17).

Routing (spec 06):
- **One piano soundboard**, heard from `PianoMid`: `PianoLo/PianoHi` forward their body send through
  `SynthSource.resonatorTarget`. Phases per session (mulberry32 of the session seed; seed 2 = the 06 board).
- **Drum room = an exact convolution reverb**: uniformly partitioned overlap-save FFT convolution (256-sample
  partitions, no added latency at 256-frame callbacks) of `Notes.MakeRoomImpulse(0.45, .004, .3, .45, .3, .8)` (left
  channel) x the convolver normalisation scale (`Notes.ConvolverScale`); return x 0.3 on `DrumRoom`; exact idle skip once the input has been silent for
  the IR's length. Replaced the foundation's FDN, which cannot reproduce a 450 ms noise IR cut off at -19 dB.
- The snare wires and the choke skin bypass the drum-room send (`SynthFlags.NoSend`).
- Body resonators reproduce the **truncated** IR (the ring past 0.28 / 0.30 s is cancelled): without it the piano and harp
  null tests fail (the IR stops at -25 dB).

**Idle sources** (review finding): a source whose bus is below -120 dB and heading to zero, with no active voice and no
pending input, is skipped after its commands are drained: output zero, and each processor's `Skip` keeps time instead of
`Process`. The ambience beds (four noise beds with about 7 `Math.Sin/Cos` per sample, the pop timer, the gust) therefore
cost nothing while their switch is off (from the first block when never switched on; 5.5 s after a switch-off). `NoiseBed`
advances its LFO time and noise position, `FirePops` runs its 110 ms timer with the same draws (none while the fire is off),
and a bed coming back re-filters its last 85 ms of noise, so the render equals a never-skipped one (EditMode: residual
-128.5 dB, max |diff| after the switch-on 7.5e-9 of a 0.08 peak). Sources with a body resonator never skip.

Rendering rules the reference renders needed (each found by nulling, all in the code with comments):
- A source stop is the source only: filters keep ringing with zero input x the envelope/gate (`Voice.RingSamples` 1024).
  Quiet one-shots (damper thud, harp shh, choke skin) null at -112..-148 dB with it, -39..-56 dB without.
- Stop frames: `ceil` of the stop time rounded to 1/1024 frame (`Voice.StopSamples`); exponential envelopes run on the
  true time, so a 9.8 ms decay lasts 470.4 samples (`Envelope.ExponentialTo(double)`); the knock went -59 -> -157 dB.
- Sunny's chirps: an oscillator reads its frequency automation from frame 0 of its 128-frame start quantum (the default 440 Hz
  before the event) (`SynthFlags.QuantumLead`), and `0.02 + 0.065 = 0.08500000000000001` s starts the oscillator one frame
  late while the envelope, glide and stop count from the event (`SynthFlags.OscLateStart`). Chirps: +3 dB -> -118/-111 dB.
- Fire pops: a 110 ms timer from the ambience's start (first tick at 110 ms), draws test/time/bp/peak/decay from
  mulberry32, and each pop starts between samples (noise read with linear interpolation, envelope on the true time).
- Wind whistle and snow gust: a-rate filter frequencies, so coefficients are recomputed every sample (wind -75 dB; at
  every 32 frames it was -66 dB). The gust's hiss gain ramps .0001 -> .12 peak (not .12 x .0001).

Sunny's voice: the 22 lines (22.05 kHz WAVs) are resampled to the output rate (Kaiser-windowed sinc, 32 taps), given 60 ms
of tail, and robotised **at the output rate** as a live chain would (ring 0.92/0.08 x 80 Hz, 240-frame comb fb .3
x .12, HP 120 Hz Q .7 dB, peak 2.5 kHz Q .8 +2 dB), in place in their preallocated buffers. A line asked for before its
render started is rendered on the spot (start-up only, `SyncRenders`). Line timing (`Playing`, `Level`, the returned end)
runs on the engine's audio clock. Mouth: RMS of the last 1024 output samples (a 1024-point analyser window), precomputed every 256 frames.

Acoustics start values (to tune on the headset): reverb sends VR piano 0 / harp +3 / drums -3 / drum room 0 dB, MR -6 dB;
`ReverbReach` 0.7 on instruments, early reflections +3 dB on the drum pieces; ambience and Sunny have no room send.
**HRTF front calibration**: the Meta HRTF measures -7.4 dB per ear for front white noise at 0.5 m against the reference renders' -5.0, so
every spatial source gets `GainBoostDb` +2.4 dB (`AudioRig.HrtfCalibrationDb`). With it: front -5.0, 90 deg right L -22.2 /
R -1.0 (reference -13.4 / -1.3), behind -11.8 (reference -8.9). `HrtfLevelTests` measures the uncalibrated front itself, asserts
the constant equals the gap (+-0.5 dB) and the calibrated front is -5.0 +- 1 dB.

Start-up (`AudioModule`): `[Audio] sr=48000 dsp=256x4 spatializer='Meta XR Audio' voiceLimit=64 acoustics=Automatic mixer=True
lines=22/22 renders=133 buffers=89.7MiB probe=False seed=... graph=1.55MiB burst=True classic=False units=none` (renders and
buffers drop by 88 keys / 67.7 MiB and 22 strings / 12.2 MiB once the piano and harp units exist and the session starts
with the new sound) (a warning if the spatializer is not Meta's: everything would
play 2D), then `[Audio] renders done in 1.37 s: 133/133 (failed 0), first key 0.070 s (budget 0.15), wave tables 20 ms,
bank 89 MiB, lines 22/22 (on demand 0), workers lowered 0` (editor; `workers lowered` counts the Android JNI calls).
Development players also log the block cost once a second (`AudioModule.LogBudget`; off in the editor, where tests run)
and the graph's `[Audio] dsp ...` line (`DspLoadLog`, log only; `DspLoadLog.LogInEditor` adds it in the editor).

Performance (`Tools/unity.sh test-perf`, editor Mono, 2026-10-31): E12 busy groove with the tables ready and all three beds
on, **302 us total of the per-source averages per 5.33 ms block** (5.7 %; budget 1500 us on device), 1299 us sum of maxima,
drum room 94 us, Ride 24, Crash 7, Hat 7, Harp 48, 0 dropped, 0 stolen; metal six-square sum 237 ns/sample per-sample range,
27.5 hoisted, 42 PolyBLEP, 2.8 from `MetalBank`; renders 133/133 in 1.32 s. With the beds off they cost nothing (idle skip).

## Constants (spec 06)

Master 0.9 -> compressor -14/18/4, 3 ms/180 ms, makeup +2.137 dB, no look-ahead -> shelf 7.5 kHz +1.5 dB -> 0.98.
Drum level 1.7; room sends kick .35, crash/ride .2, others .5; return .3. Piano: level .08 + .8 v^2, cutoff
clamp(1.5 f + 300 + 11000 v^1.6, 500, 16000) Q .4 dB, jitter x .25 (rate, cutoff, level), board send .6 + .4 v, return .22,
knock bp clamp(1.6 f + 900, 600, 3200) Q 1.1 peak .05 + .3 v^2 decay .007 + .004 v, release tau .06 stop +.5, thud bp 260
Q .8 .028/.03. Harp (old): level .1 + .6 v, cutoff 1000 + 5000 v, jitter 1, box send .5 + .4 v, return .24, thud lp 700
.02 + .05 v / .012, damp tau .018 stop +.25 + shh bp 2400 Q .7 .004 sqrt(max(1, n)) / .07. Choke tau .012 + skin lp 1500
.05/.03. Ambience: fire lp 420 .09 + .03 sin(.17 Hz) + pops; wind lp 260 .1 + .06 sin(.07) and bp 650 + 320 sin(.11) Q 6
.1 + .07 sin(.05); snow lp 520 Q .3 .07 + .025 sin(.05); bus tau .4; gust f0 360 + 220 R, x(1.25 + .35 s), peak .025 + .05 s,
hiss hp 6000 x .12, stop dur + .1. Chirps (square, lp 5000): open 880->1320 .04 .022/.06, +.065 1320->700 .05 .018/.07;
close 1100->560 .05 .018/.06, +.065 700->420 .05 .015/.07. Swish: bp 1400 Q .7 lp 7000 .09/.32, +.04 hp 2500 lp 9000 .035/.22.

## Tests

10-upgrade (engine): EditMode `Tests/EditMode/Audio/Burst/`: `BurstKernelTests` (burst.md section 9: mb4Impulse,
mb4Free, mb4Driven, wg4, noise4 + biquad4, graph (6 blocks x 2 sources, LOD 3 drops, 0 culled, max 7 active), steal
order, render twice identical; within 1e-5 with Burst on, bit-exact FNV-1a with Burst off), `BurstFloatModeTests` (a
callee inherits its entry point's FloatMode: the rule unit code relies on), `AudioGraphEngineTests` (arena size and
alignment; unit events sample-accurate and in call order; late onsets dropped; fade-out / suspend / reset / resume without
clicks; the graph slice through a SynthSource and its bus bit-exact, taps; the listener stage; today's voices hand over
with FadeAll, no click; one-shots: ghost fade over 128 samples, expiry, release; the CPU cap fades the quietest groups
click-free and keeps the protected ones; denormal flush and retire; T-DENORM; adaptive budget rule; load meter; WAV round
trip), with `OfflineEngine` / `WavFile` / `NullTest` (the harness the workstreams use). PlayMode `ClassicSoundTests`
(the graph live on every source; only the unit-only roles have units; the classic three play today's voices with their
buffers rendered at start-up; the room is Meta's and today's master; an old save's switch is deleted) and
`BurstGraphLoadTests` (Performance: T-KERNEL, T-DENORM, the LOD ceiling offline and live). `AudioEngineTests`,
`AudioAllocTests` and `AudioLoadTests` cover today's engine (the app's only sound for the classic three).

EditMode (`Tests/EditMode/Audio`, namespace `MusicRoom.Tests.Audio`):
- `NullTests` (33 cases; `OfflineAudio` drives the real sources offline with the 06 seeds; WAV copies in
  `Reference~/`, a folder Unity never imports). Residuals (limit -60 dB): drums -74..-133 (16 kind/zone cases incl. wires),
  piano C4 v40/v80 -80, A0 -80, C7 -75, harp C3/G4/C6 -137, damper thud -112, hammer knock -157, harp shh -147, choke skin
  -148, swish -145, chirps open -118 / close -111, wind -75, snow + gust -75, fire bed + pops -78.
- `AudioRecipeTests`: fast table build bit-identical (3 waves); fractional exponential ramp; soundboard resonator = the
  truncated convolution of the seed-2 board; drum room = the seed-6 IR x scale (06 fingerprint 0.0043076) and goes idle;
  drum room gets the hits but not NoSend voices; bass/treble notes sound the one board on PianoMid; robot chain impulse;
  resampler (-60 dB on a 1 kHz tone); mouth window; chirp start frames and flags; voice lines load (22) and robotise;
  pop timer draw order; gust shape; gusts only while snow is on; bed on/off ramps; **idle beds skip and come back as if
  never skipped**; piano jitter draw order; velocity clamps and registers; release / damp / re-pluck; glissando delays;
  choke; ring-out then free.
- From the foundation (owned here, rule 3): `DspTests` (ring, biquads, envelope, oscillators incl.
  `HoistedRangeIsBitIdenticalToThePerSampleReference`, `GlideRangeTracksThePerSampleReference`,
  `MetalBankSumEqualsTheLiveSixOscillators`, compressor, noise/IR fingerprints, panner law), `RendererTests` (harp
  bit-exact, piano fingerprints, `RenderInto`).

PlayMode (`Tests/PlayMode/Audio`, namespace `MusicRoom.Tests.PlayMode.Audio`; every fixture boots through `TestBoot` with
`ModuleFilter = AudioModule` only, so the fresh store's first-launch tour cannot sound into the measurements):
- `AudioEngineTests`: the foundation's call log (`CallLogRecordsEveryApiCallAsTheInstrumentMadeIt`), recipes reaching
  their sources with the audio thread running, `MetalBank` hits; every engine call on `Calls`; start-up report facts,
  preallocated buffers, every render lands (88 keys, 22 strings, 22 non-silent lines), first key ready (< 1 s in the editor,
  logged against the 0.15 s device budget); Sunny speaks with a moving mouth and stops on cue; ambience ramps and gusts
  follow the weather; beds never switched on are idle from the first block; MR sends -6 dB and the tuning start values;
  busy-groove block budget (0 dropped, 0 stolen); the remaining engine calls allocate nothing. `AudioClockTests`:
  `Now`/`FrameNow`/`App.AudioTime` and a voice line's end follow `BootOptions.AudioClock`.
- `HrtfLevelTests`: Meta HRTF level vs the reference renders' (uncalibrated front measured, `HrtfCalibrationDb` = the gap, calibrated
  front -5.0 +- 1 dB, ILD present).
- From the foundation: `AudioAllocTests` (190 calls, 0 B; `MidiOf` / `Rig.Handles`), `AudioLoadTests` (category
  Performance, `test-perf` only).

## Rebase onto the review-round-1 foundation (2026-10-31)

Base: the review-round-1 foundation, ours: the main project's `Assets/MusicRoom/Audio`, theirs: this module (the module's
pre-rebase files were recovered exactly from the earlier fix session's diffs and replayed against the partly merged tree).
Kept from the foundation: `IAudioEngine` with `AudioTestTap.Calls` and `Record` in every engine method; `Init(sources, seed,
clock)` and `Now/FrameNow` on the clock; the hoisted `Oscillator` range (`RangeStep`) and `MetalBank` (this module's own
table cache and `MetalSums` were dropped in their favour; the nested-sum fast table build stays); `PreallocatedBytes`,
`RenderInto` with per-thread scratch, the JNI worker priority; `PreferredDspBuffer` + `ApplyDspBuffer`, `LatencyProbe`;
`ResetStats` with its own block count. Kept from the module: every recipe and rendering rule above, `Fft` + `DrumRoomReverb`,
ring-out, the single soundboard, the HRTF calibration, the logs. `GuideVoice` renders into preallocated slots (output and
mouth envelope sized at queue time). The 06 null tests, unchanged, decided every overlap: all 33 still null at the same
residuals.

## Known gaps / device checklist

- 10-upgrade engine, device only (burst.md section 13): T-DENORM (`DspKernels.DenormProbe`; editor ratio 1.02), T-KERNEL
  (MB4 free <= 0.6 ns per mode-sample at the measured CPU level), T-VEC (the section 9 vectors with Burst on the device),
  T-STRESS / T-PAUSE (`DspLoadMeter` gaps, `[Audio] dsp` lines in a `-development` build, `adb shell dumpsys
  media.audio_flinger`), whether the FMOD mixer thread shows `MusicRoom.AudioGraph.Render` in the Profiler, and that the
  listener (MasterBus) runs after every source of the same block (the room's taps count misses).
- The graph's sample rate is fixed at `Init` (48 kHz on the Quest); an output-rate change after start-up (a Bluetooth
  device) would detune the units. Today's sources follow the new rate as before.
- `DspLoadLog` (the `[Audio] dsp` line) and `AudioTap` run only in `-development` builds (the default `Tools/unity.sh
  build` is a release build), or the log in the editor with `DspLoadLog.LogInEditor`. No build shows a DSP HUD.
- IL2CPP pitfall found by the first APK build: a managed `cond ? pointer : null` became a 32-bit integer in the C++ output
  (`-Wint-to-pointer-cast`); the engine uses plain if / else (ENGINE-API.md section 3.4).

- The reference set's NEW harp sound (`useNew(true)`, `harp_new_G4_v070.wav`) is not implemented: no recipe selects it
  (no caller of `useNew`), and ARCHITECTURE section 0 fixes the old sound. `PluckOptions.U/Speed/Mode` are accepted and
  unused, as in the old sound's recipe.
- The drum room is mono (left IR channel) on one spatial source; the reference fed a stereo IR into an HRTF panner (unverified).
- The robot chain is pre-rendered per line from rest (ring phase 0, comb empty); a live chain would carry its ring
  phase and comb state across lines (inaudible).
- The piano nulls at -75..-80 dB (limit -60); the remaining error is the offline piano render's own approximation of
  the reference (`PianoRenderer`, ~1e-4 to the 06 fingerprints).
- `FirstKeySeconds` includes the 89.7 MiB main-thread allocation in `Init` (the editor: 0.03-0.17 s with other editors
  running); check the 0.15 s budget in the device log.
- Device only (ARCHITECTURE 19, items 11-15, 26): output rate, DSP buffer count and latency (`LatencyProbe`); audio CPU
  under IL2CPP (budget 1.5 ms; editor 0.3 ms); the JNI worker priority (`workers lowered 2`) and intro GC behaviour; the HRTF
  calibration and reverb sends/reach/ER by ear; mixer suspension; pause/resume.
