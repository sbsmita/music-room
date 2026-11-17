# Guide module: contract requests for the merge

## Integration status (2026-10-31): APPLIED

Merged into `vr-music-room-unity` by the integrator. Every item below is resolved as stated here; the text after
this box is the module's original request, kept for the record.

| # | Request | Resolution |
|---|---|---|
| 1 | `IAudioEngine.VoiceLineReady` | Added (default `HasVoiceLine`); `AudioEngine` answers from the sample bank; `EngineVoice.LineReady` uses it (`_concrete`/`EngineSlot` removed) |
| 2 | Voice lines render early | In the merged Audio module (intro lines right after middle C's neighbours) |
| 3 | `SynthSource` race | Audio module's version merged; `AudioBootGuard` is now a no-op |
| 4 | `DesktopInput.Ctrl` / `Meta` | Added; `GuideKeys.CtrlOrMeta` forwards; `Unity.InputSystem` removed from the Guide asmdef |

---

The Guide compiles and passes its tests without any of these, against the revised foundation;
module-local workarounds are in place, and they also hold against the Audio module's own working copy (m-audio). Each request removes a workaround or fixes a foundation defect the Guide's
tests ran into. Status as of 2026-10-31 (rebase onto the revised foundation).

| # | Owner | Request | Status |
|---|---|---|---|
| 1 | Audio | `IAudioEngine.VoiceLineReady(string id)` | open |
| 2 | Audio | Render Sunny's lines before the piano and harp | done in m-audio ("the intro lines render first; a line asked for early is rendered on the spot"); still open in the revised foundation's Audio |
| 3 | Audio | `SynthSource.Awake` publishes `_voices` before `_mix` (defect) | done in m-audio (`Volatile.Write(ref _voices, voices)` last); still present in the revised foundation's Audio |
| 4 | Core | `DesktopInput.Ctrl` and `DesktopInput.Meta` | open |

## 1. `IAudioEngine.VoiceLineReady(string id)` (Audio, new member)

**Why:** Sunny voices a line only once its audio is ready, and drops it (caption only) when that takes
more than LATE = 1.2 s (Docs/08-ux-lessons-menu-guide.md 3.4). The audio is the robotised buffer rendered on a worker. A line
started before its buffer exists becomes a `BufferVoice` that waits 0.3 s and then ends silently, while the return value
of `PlayVoiceLine` says it is speaking (wrong mouth, wrong "finish the sentence" timing). The m-audio engine renders such a
line synchronously inside `PlayVoiceLine` instead (a main-thread stall of a few ms); the Guide still prefers to wait for
the worker's render.

**Workaround now:** `EngineVoice.LineReady` (Guide/SunnyAdapters.cs) casts `IAudioEngine` to `AudioEngine` and reads
`Bank.Get(AudioEngine.VoiceSlot0 + index)`; any other engine is trusted once `HasVoiceLine` is true. Both Audio versions
keep `AudioEngine.Bank`, `VoiceSlot0 = 200` and the slot-per-line layout, so it works either way.

**Exact code** (against m-audio's `GuideVoice`, which already has `IndexOf` and `IsRendered`):

```csharp
// Audio/Engine/IAudioEngine.cs, in the "guide" block after VoiceLineSeconds:
        /// <summary>The line's robotised buffer is in the sample bank.</summary>
        bool VoiceLineReady(string id);

// Audio/Engine/AudioEngine.cs, next to HasVoiceLine:
        public bool VoiceLineReady(string id)
        {
            int i = GuideVoice != null ? GuideVoice.IndexOf(id) : -1;
            return i >= 0 && GuideVoice.Has(id) && GuideVoice.IsRendered(i);
        }
```

(The revised foundation's `GuideVoice` has neither helper. Add
`public int IndexOf(string id) => _index.TryGetValue(id, out int i) ? i : -1;`, keep the bank and slot 0 given to
`QueueRenders` in fields, and add `public bool IsRendered(int i) => _bank != null && _bank.Get(_slot0 + i) != null;`.)

**Then in the Guide:** `EngineVoice.LineReady(id) => _engine != null && _engine.VoiceLineReady(id);` and delete
`_concrete` and `EngineSlot`.

## 2. Render Sunny's lines first (Audio, `AudioEngine.Init`)

**Why:** on the first launch Sunny greets the player at the first arranged frame, while the piano is still rendering
(ARCHITECTURE.md 17: "all renders during Sunny's intro"). The robot chain over a line costs a few ms, so 22 lines first
delay the first piano key by well under 0.1 s.

**Exact code** (revised foundation's `AudioEngine.Init`): move `GuideVoice.QueueRenders(Renders, VoiceSlot0);` from after
the harp loop to right after
`Renders.Add(-1, _ => { PeriodicWaveTables.Build(); MetalBank.Build(sr); return null; });`. m-audio already orders the
intro lines first; keep that at the merge.

**Workaround now:** `GuideModule.AutoStart` waits up to `AutoStartVoiceWait` = 6 s of app time for the intro line's buffer
before starting the first-launch tour (then starts anyway; a missing buffer means a caption only). With the change it
waits a frame or two.

## 3. `SynthSource.Awake` publishes `_voices` before `_mix` (Audio, defect)

**Observed** (Guide PlayMode runs on foundation-v1; the revised foundation has the same `Awake`):
`ArgumentNullException: Value cannot be null. Parameter name: array at System.Array.Clear ... at
MusicRoom.Audio.SynthSource.OnAudioFilterRead (SynthSource.cs:211)`, logged during `AudioRig.Build` of a freshly booted
app. The audio thread entered the filter after `Awake` assigned `_voices` and before it assigned `_mix`, and
`OnAudioFilterRead` only tests `_voices == null`. Whichever PlayMode test is booting then fails with "Unhandled log
message" (seen in 4 of 9 runs under a 10-way parallel load, twice in foundation tests).

**Exact code** (revised foundation's `Audio/Engine/SynthSource.cs`, `Awake`): build the pool into a local and publish it
last, after `_mix`, `_send`, `_pending` and the resonator:

```csharp
            var voices = new Voice[_poolEnd];
            for (int i = 0; i < _poolEnd; i++)
            {
                voices[i] = i < _toneFrom ? new NoiseVoice()
                    : i < _metalFrom ? new ToneVoice()
                    : i < _bufferFrom ? (Voice)new MetalVoice()
                    : new BufferVoice();
            }
            _mix = new float[MaxFrames];
            _send = new float[MaxFrames];
            _pending = new float[MaxFrames];
            if (resonatorModes != null && resonatorModes.Length > 0)
            {
                _resonator = new ModalResonator();
                _resonator.Build(resonatorModes, resonatorSeconds, (float)_sampleRate, resonatorSeed);
            }
            Volatile.Write(ref _voices, voices);   // the audio thread tests _voices == null: publish it last
```

m-audio's `SynthSource` already does this.

**Workaround now:** none is possible outside the Audio folder. `Tests/PlayMode/Guide/AudioBootGuard` tolerates exactly
this exception while a Guide test boots (around `TestBoot.Start`) and still fails on any other error; it becomes a no-op
once the fix lands. Foundation tests are not guarded.

## 4. `DesktopInput.Ctrl` and `DesktopInput.Meta` (Core, new members)

**Why:** Shift+H is ignored while Ctrl or Cmd is held (Shift, no key repeat, no Meta, no Ctrl, not presenting;
system shortcuts). `DesktopInput` has
only `Shift` and `Alt`.

**Workaround now:** `GuideKeys.CtrlOrMeta` in `Guide/GuideModule.cs` reads the keyboard itself under the same
`ENABLE_INPUT_SYSTEM` switch, which is why `MusicRoom.Guide.asmdef` references `Unity.InputSystem` (a package assembly,
allowed by ARCHITECTURE.md section 2 rule 1).

**Exact code** (`Assets/MusicRoom/Core/Util/DesktopInput.cs`, after `Alt`):

```csharp
        /// <summary>Ctrl held (either side). With Ctrl or Meta held the app's desktop shortcuts are ignored.</summary>
        public static bool Ctrl
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                var k = Keyboard.current; return k != null && (k.leftCtrlKey.isPressed || k.rightCtrlKey.isPressed);
#else
                return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
#endif
            }
        }

        /// <summary>Cmd (macOS) / Windows key held (either side).</summary>
        public static bool Meta
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                var k = Keyboard.current; return k != null && (k.leftMetaKey.isPressed || k.rightMetaKey.isPressed);
#else
                return Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand)
                    || Input.GetKey(KeyCode.LeftWindows) || Input.GetKey(KeyCode.RightWindows);
#endif
            }
        }
```

**Then in the Guide:** in `GuideModule.Tick` replace `!GuideKeys.CtrlOrMeta` with `!DesktopInput.Ctrl && !DesktopInput.Meta`,
delete `GuideKeys`, and remove `"Unity.InputSystem"` from `MusicRoom.Guide.asmdef`. `GuideShiftHTests` covers both keys.

## Notes (no change requested)

- The Guide's tests assert speech on `AudioTestTap.Calls` (`Chirp`, `VoiceLine` with the line id and `When`,
  `StopVoiceLine`), never on `Commands`, so a retuned chirp or voice recipe in the Audio module cannot break them. They use
  only `IAudioEngine`, `AudioEngine.Bank` / `VoiceSlot0` (the readiness workaround above), `GuideVoice.LineIds` /
  `GuideVoice.Lead`, `RobotVoice.Process(float[], int)` and `Biquad.Create`, all present with the same signatures in both
  Audio versions.
- `PlayVoiceLine` must keep returning the time the line ends (`start + clip length`) on the clock `IAudioEngine.Now`
  reads: Sunny's "finish the sentence" rule and the scripted-clock tour test rely on it. Both Audio versions do.
- The voice WAV import settings live in the Guide folder: Decompress On Load, Preload Audio Data, Force To Mono and
  Vorbis quality 0.7 (spec 08 section 3.5), so `AudioClip.GetData` in `GuideVoice` always sees loaded mono samples.

---

## Phase 0 of the many-instrument platform (workstream B, 2026-11-01): requests outside Guide

### G1. GuideVoice merges the module manifests (Audio workstream)

The Guide now ships `Resources/GuideLines/Guide.json` (the platform's six lines) and `Resources/VoiceManifests/Guide.json`
(their `{"<id>": {"file", "seconds", "rate"}}` entries, exactly the shape of `Voice/manifest.json`), with the six WAVs in
`Guide/Resources/Voice` (tip_arrange's importer settings). Until `Audio/Synths/GuideVoice.cs` merges
`Resources.LoadAll<TextAsset>("VoiceManifests")` and loads `Resources.Load<AudioClip>("Voice/<id>")` for them, Sunny
shows these six as captions (EngineVoice.HasLine is false); everything else works. `GuideLines.Ids` stays the 38 of
`GuideVoice.LineIds`; the merged ids follow in `GuideLines.Merged.Ids` (stable order).

### G2. Signals the platform tips wait for (Menu, Layout)

As the brief lists: the Menu publishes `InstrumentSignalEvent{Kind = Table}` "menu.instruments" when the Instruments
screen opens and "menu.instruments.set" when it opens in My set with 2 or more members; Layout publishes, with the
instrument's Kind, "layout.bring.menu" | "layout.bring.ray" | "layout.bring.pageturn" | "layout.bring.lesson" |
"layout.bring.sunny" | "layout.replaced" | "layout.putaway", `{Kind = Table}` "layout.single" when One alone is chosen, and
`InstrumentMoveEvent{Kind, Arrived}` at the end of every Bring (the arrival tips' trigger). `ILayoutService.
TryGetDisplayPoint` must answer for displayed instruments (the gaze tip and its "display" anchor).

### G3. RegistryLintTests allowlist (Core tests)

Instrument names that remain in Guide by design: `Guide/DeckGuide.cs:145-147` (the deck walkthrough's layered song:
drums, then harp, then piano, with their lines lay_drums / lay_harp / lay_piano), and `GuideModule.OnRecorder`
(`TakeEventType.Drum` with kind "kick": the tour's nod step). `SourceId.Guide` (GuideModule.cs) is Sunny's own source.
