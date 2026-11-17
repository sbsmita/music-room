# 04 · Unity audio engine: low-latency procedural audio on Quest 3S (Meta XR Audio SDK 85)

**Basis.** I verified everything below in a scratch copy of the project. The setup was Unity 6000.3.27f1, the macOS editor on an Apple M5 Pro, 48 kHz, and 256-frame blocks. Nine PlayMode experiments ran in batchmode (classes `AudioSpikeTests`, `AudioEngineLoadTests` and `AllocCheckTests`). All 3 tests passed with 0 compiler warnings.

Quest numbers are extrapolations and are marked **UNVERIFIED**.

**Code:** every C# file named here was compile- and run-verified (the shipping versions are under `Assets/MusicRoom/Audio/`); the experiments printed raw `[SPIKE]` and `[LOAD]` output.

Package paths below are relative to `Library/PackageCache/com.meta.xr.sdk.audio@3292c33a8423/` unless another root is named. Spec 06 (`Docs/06-audio-dsp.md`) holds the per-voice constants.

---

## 0. Decisions
1. **17 `SynthSource`s, one per spatial source (spec 06 §10).** Each is an `AudioSource` with a silent looping carrier (`spatialize`, **`spatializePostEffects=true`**, `spatialBlend=1`, priority 0, doppler 0, Logarithmic 0.6–30 m), a `MetaXRAudioSource`, and our `OnAudioFilterRead` mixer of pooled voices.
2. **Project settings.** Spatializer and ambisonic decoder `"Meta XR Audio"`, DSP 256, Stereo, Classic, Rolloff Scale 1.1 (the inverse-distance `rolloffFactor`). Set them with `AudioProjectSetup.Apply`, which works in batchmode; Meta's own auto-setup does not run there.
3. **Reverb.** The Meta shoebox replaces the room impulse-response convolvers (spec 06 §6). **All sources output to a mixer group carrying "Meta XR Audio Reflection"**: without a running, fed mixer there is no reverb at all (E3). Turn Auto Mixer Suspend off.
4. **Master chain.** `MasterBus` on the AudioListener (×0.9 → compressor with spec 06 §1.5's static curve → +1.5 dB shelf), with no 6 ms look-ahead.
5. **Threads.** A lock-free SPSC `SynthCommand` ring per source, staged and committed per API call. Starts and releases are sample-accurate from a dspTime `When`.
6. **Startup renders.** Piano and harp buffers render on 2 workers, middle-out (79.9 MiB at 48 kHz). Body convolvers become modal resonators. Drums and ambience are synthesized live on the audio thread.

## 1. Measured facts

The experiment code was a PlayMode test folder in the scratch copy. Every setup includes a Meta spatializer source, a listener-filter tap and `AudioListener.GetOutputData`.

| # | Experiment | Result | Consequence |
|---|---|---|---|
| E1 | Noise from `OnAudioFilterRead`, source 1.5 m right | `postEffects=true`: L −42.8 / R −28.7 dBFS (**ILD 14.1 dB**). `false`: L = R = −16.8 dBFS, the raw level, **not spatialized or attenuated**. With no clip, or without `MetaXRAudioSource`: the same as `true`. | Use `spatializePostEffects=true`. The carrier is optional; keep it. |
| E1-D | `spatialize=false`, `spatialBlend=1`, so Unity's built-in 3D pan | L = R = −16.8 dBFS | **Unity's built-in panner never touches filter output.** If the spatializer name is empty, every synth is 2D at full level. |
| E1-E | Generator writes only channel 0 | Same levels as both channels | The Meta spatializer takes **channel 0** of the filter output as its mono input. |
| E2 | Front 1 m vs 4 m, Logarithmic, min 0.6, Rolloff Scale 1.1 | −24.6 vs −37.0 dBFS, a 12.4 dB drop. The prediction 0.6/(0.6+1.1(d−0.6)) gives 12.4 dB. | Unity's rolloff is the **only** distance attenuation. It reproduces the `inverse` distance model (ref 0.6, rolloff 1.1) exactly. |
| E2 | Custom flat rolloff curve, 1 m vs 4 m | −19.8 / −19.8 dBFS | The Meta plugin adds **no** distance attenuation of its own. |
| E3 | 100 ms burst at 2 m, tail energy 30–600 ms vs the burst | No mixer, native `SetEnabled(..., true)` only, or mixer loaded with nothing routed: **no tail**. Routed: tail −20.0 dB, early (6–30 ms) −13.7 dB. Not routed with a silent carrier routed: −19.9 dB. `ReverbSendDb=-60`: −74 dB, early −29.6 dB. `EnableAcoustics=false`: −113 dB. | Reflections and reverb exist only while the "Meta XR Audio Reflection" effect runs in an **active** (fed) mixer. They are a shared bus, so routing each source is not strictly needed, but routing all of them keeps the mixer alive. |
| E3' | Taps compared | The listener-filter tap and `GetOutputData` agree to 0.1 dB, including audio routed through the mixer. | A C# master bus on the AudioListener sees everything. |
| E4 | Two sources, logged per callback | **Same** dspTime per block, **one** thread, called 1,2,1,2. dspTime steps 256/48000 s. On the main thread, dspTime is one block ahead of the audio thread. | Callbacks are serial; block identity = dspTime. On the main thread, `Now` is the next block. |
| E10 | `Post(ASAP)` → block containing the onset | **0.00 ms** after the main-thread dspTime at post (3/3). `When = Now + 50 ms`: the onset block starts 2.00 ms before `When` (offset inside the block). | ASAP lands in the next mixed block; scheduling is sample-exact. |
| E9 | `Voice.Render` start offset 100, release offset 128 | Silent before sample 100; sample 100 = noise[0]·peak exactly. Release fades with τ, stops at +2 ms. | Sample-accurate start, release and stop. |
| E6 | `AudioSettings.Reset(dspBufferSize 512)` | Returns `true` and fires the change event, but **`isPlaying=false` and no filter calls** until `Play()`. Then 512-frame blocks. | Re-`Play()` in the handler (`SynthSource` does). |
| E7 | `AudioClip.Create(stream: true, PCMReaderCallback)` | 10 callbacks at Create plus 10 per second while playing, 1536–4096 frames each, on a **separate thread** (not main, not mixer). | Streamed clips read ahead by about 40k frames, so they are unusable for live synthesis. |
| E5 | Config after `Apply` | 48000 Hz, `dspBufferSize` 256, `GetDSPBufferSize` 256 × **4** buffers, Stereo, 32 real / 512 virtual voices, spatializer `'Meta XR Audio'`. Registered plugins: `[OculusSpatializer, Meta XR Audio]`. | Two spatializers are present; pick Meta's by name. |
| bug | `[StructLayout(Explicit)]` struct nested in generic `SpscRing<T>` | `TypeLoadException: Generic class cannot have explicit layout` | Keep padded structs at namespace level (fixed in `SpscRing.cs`). |

Isolated runs are in `measurements/*-isolated-run.txt`. In the combined run (`spike-results-combined-run.txt`), reverb from an earlier test's mixer leaks into later non-routed sources. ILD drops to 9.4 dB there, which is more evidence for the shared-bus behaviour.

---

## 2. Unity audio mechanics (Q1)

### 2.1 The synth bus
- **Signature.** `void OnAudioFilterRead(float[] data, int channels)` runs on the FMOD mixer thread. `data` is interleaved, 256 frames, and **2 channels** even with a mono carrier (E1). Its input is the clip's audio, which we overwrite.
- **Placement and rules.** Component order is filter order: `AudioSource`, then `MetaXRAudioSource` (not a filter), then `SynthSource`; add no other filters. Inside the callback: no Unity API except `AudioSettings.dspTime`, no allocation, no locks.
- **Output.** Write the mono mix to every channel. Channel 0 is what the spatializer uses (E1-E); all channels matter for the 2D `Ui` source.
- **Creating sources at runtime.** Create the GameObject inactive, add components, set fields, then `SetActive(true)`. Otherwise `SynthSource.Awake` runs with default pool sizes (`AudioRig.cs`).

### 2.2 `AudioSource.spatializePostEffects`
- The XML doc says it "Determines if the spatializer effect is inserted before or after the effect filters." `true` means the spatializer runs **after** `OnAudioFilterRead`.
- Measured in E1: `true` spatializes our synthesis; `false` leaves it dry and unattenuated.

### 2.3 Configuration
- **Read.** `AudioSettings.GetConfiguration()` returns `AudioConfiguration {speakerMode, dspBufferSize, sampleRate, numRealVoices, numVirtualVoices}`. `AudioSettings.GetDSPBufferSize(out int bufferLength, out int numBuffers)` returned 256 × 4. `AudioSettings.outputSampleRate` was 48000 in the editor.
- **Quest.** The 48 kHz output rate is **UNVERIFIED**. Log `outputSampleRate` and `GetDSPBufferSize` at startup, and never hard-code the rate (all code reads it at runtime).
- **Reset.** `AudioSettings.Reset(config)` restarts the device and stops every source (E6). Handle `AudioSettings.OnAudioConfigurationChanged(bool deviceWasChanged)`: kill voices (the `SynthOp.Panic` command) and call `Play()`. Prefer project settings over runtime `Reset`.
- **AudioManager.asset (probed).** `m_SampleRate` (0 = system), `m_DSPBufferSize`, `m_RequestedDSPBufferSize` (enum, raw 0/256/512/1024), `Default Speaker Mode` (enum, raw 2 = Stereo), `m_RealVoiceCount`, `m_VirtualVoiceCount`, `m_SpatializerPlugin`, `m_AmbisonicDecoderPlugin`, `m_DisableAudio`, `m_VirtualizeEffects`, `Rolloff Scale`, `Doppler Factor`.
  - New in 6.3: `m_AudioFoundation` (**Classic** = 0 / Enhanced; keep Classic, since Enhanced with native spatializers is UNVERIFIED), `m_OutputChannelLayout`, `m_OutputSamplingRate`.
  - Set enum fields with the raw value through `intValue`, not `enumValueIndex`.

### 2.4 Clocks
- **`AudioSettings.dspTime`.** A sample-counted double, monotonic, unaffected by `timeScale`. On the audio thread it is the start of the current block, shared by every source (E4). On the main thread it is the start of the next block, moving in whole blocks (5.333 ms). `Time.*` is frame-based; use it for visuals only.
- **Scheduling.**
  - `When = 0` means ASAP: the first sample of the next block.
  - `When > 0` is an absolute dspTime. A time in the past starts now (the `max(t, currentTime)` rule).
  - `When < 0` is a relative delay of −When seconds, exact only inside one committed batch.
  - For strums and sequences that span several calls, use `AudioEngine.FrameNow` (dspTime sampled once per frame in `Update`, `[DefaultExecutionOrder(-1000)]`) plus the offset plus one block.

### 2.5 Clip APIs
- **`AudioClip.Create`.** The XML doc says: "Non-streamed clips call [PCMReaderCallback] only once at creation time while streamed clips call this continuously." E7 shows the streamed read-ahead.
  - Use non-streamed only, and only for the carrier: `AudioClip.Create("SynthCarrier", sr, 1, sr, false)` plus `SetData(new float[sr], 0)`.
  - Span overloads `SetData(ReadOnlySpan<float>, int)` and `GetData(Span<float>, int)` exist.
- **`AudioSource.PlayScheduled(double)`.** The doc says to schedule "~100-200ms" ahead, which is too late for live play. One AudioSource per note would also use one spatial voice each (Meta 64, Unity 32 real), so we don't use it.

### 2.6 Voice limits
- **Unity.** 32 real and 512 virtual voices (AudioManager). Culled sources lose filter processing when `m_VirtualizeEffects=1`, so set synth sources to `priority=0`. Our total is 17 sources plus about 1 line voice.
- **Meta.** `MetaXRAudioSettings.voiceLimit = 64` (`Runtime/scripts/MetaXRAudioSettings.cs:41`). It is applied before the scene loads (`Runtime/scripts/MetaXRAudioSource.cs:48-53`). Over the limit, "the oldest voice ... is silenced" (`MetaXRAudioSettings.cs:32-35`).
- **Voices inside one source.** These are our own pools (§4.4). Steal released voices first, then the oldest.

### 2.7 Threading and the command ring
- **One writer per structure.** Callbacks are serial on one mixer thread (E4), but nothing depends on that.
  - Main → audio: per-source `SpscRing<SynthCommand>(512)`.
  - Workers → all: `SampleBank` slots, published with `Volatile.Write`.
  - Audio → main: `volatile float` meters (`Rms`, `Avg/MaxBlockMicros`) and `Interlocked` counters.
- **Batching.** `TryStage` writes a slot; `Commit` publishes the whole batch with one release store, so all voices of a hit start in the same block.

```csharp
public bool TryStage(in T item) {                       // producer (main thread)
    int staged = _staged, head = Volatile.Read(ref _head.Value);
    if (staged - head >= _slots.Length) return false;   // full: caller counts a drop
    _slots[staged & _mask] = item; _staged = staged + 1; return true; }
public void Commit() => Volatile.Write(ref _tail.Value, _staged);   // release
public bool TryDequeue(out T item) {                    // consumer (audio thread)
    int head = _head.Value, tail = Volatile.Read(ref _tail.Value);  // acquire
    if (head == tail) { item = default; return false; }
    item = _slots[head & _mask]; Volatile.Write(ref _head.Value, head + 1); return true; }
[StructLayout(LayoutKind.Explicit, Size = 128)] internal struct PaddedIndex { [FieldOffset(64)] public int Value; } // NOT nested in the generic
```

---

## 3. Meta XR Audio SDK 85 (Q2)

### 3.1 Selecting the plugin
- **Registered name.** `"Meta XR Audio"` for both the spatializer and the ambisonic decoder. It appears in `editor/MetaXRAudioPluginUpdater.cs:154` and in core `Editor/BuildingBlocks/BlockData/SpatialAudio/Scripts/SpatialAudioSetupRules.cs:44`.
- **Mixer effect name.** `"Meta XR Audio Reflection"` (`editor/MetaXRAudioReflectionCustomGUI.cs:37`).
- **Meta's auto-setup.** `OverrideSpatializerPlugin` (`MetaXRAudioPluginUpdater.cs:156-205`) writes both AudioManager fields through SerializedObject, but only if the field is **empty**. It is skipped in batchmode (`:76-86`; there is a private `BatchmodePluginUpdate` at `:110-114`).
- **This project's state.** `m_SpatializerPlugin` is currently empty, so run our setup.
- **Unity APIs (probed by reflection, 6000.3).**
  - Public: `AudioSettings.SetSpatializerPluginName(string)`, `GetSpatializerPluginName()`, `GetSpatializerPluginNames()`.
  - **Internal**: `SetAmbisonicDecoderPluginName(string)`, `GetAmbisonicDecoderPluginName()`, `SetConfiguration(AudioConfiguration)`, `SetSpatializerName(string)`.
  - Meta's rules call these internal methods by reflection (`SpatialAudioSetupRules.cs:35-42`). They require DSP buffer 256 and Stereo (`:45,142-160`).
- **Our editor script.** `Assets/MusicRoom/Editor/AudioProjectSetup.cs`, verified: after it runs, the asset reads `Meta XR Audio` ×2, 256, Stereo, Rolloff Scale 1.1.

```csharp
if (AudioSettings.GetSpatializerPluginNames().Contains("Meta XR Audio")) AudioSettings.SetSpatializerPluginName("Meta XR Audio");
var so = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset")[0]);
so.FindProperty("m_SpatializerPlugin").stringValue = "Meta XR Audio";
so.FindProperty("m_AmbisonicDecoderPlugin").stringValue = "Meta XR Audio";
so.FindProperty("m_DSPBufferSize").intValue = 256; so.FindProperty("m_RequestedDSPBufferSize").intValue = 256; // raw enum value
so.FindProperty("Default Speaker Mode").intValue = (int)AudioSpeakerMode.Stereo;  // 2
so.FindProperty("Rolloff Scale").floatValue = 1.1f; so.FindProperty("m_AudioFoundation").intValue = 0;      // Classic
so.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets();
MetaXRAudioSettings.Instance.voiceLimit = 64; EditorUtility.SetDirty(MetaXRAudioSettings.Instance);  // Assets/Resources/MetaXRAudioSettings.asset
```

Batchmode: `Unity -batchmode -quit -projectPath P -executeMethod MusicRoom.Audio.Editor.AudioProjectSetup.Apply`. The Oculus XR Plugin's `"OculusSpatializer"` is a different plugin; it is only packed if it is selected (`com.unity.xr.oculus@44578ba4bd45/Editor/OculusBuildProcessor.cs:56-80`).

### 3.2 `MetaXRAudioSource`
`Runtime/scripts/MetaXRAudioSource.cs`: `[RequireComponent(typeof(AudioSource))] public class MetaXRAudioSource : MonoBehaviour` (`:42-43`).

| Property (backing field) | Range / default | Native param | Line |
|---|---|---|---|
| `EnableSpatialization` (`enableSpatialization`) | true; **sets `AudioSource.spatialize` every frame** | `source_.spatialize` | 57, 270 |
| `GainBoostDb` (`gainBoostDb`) | 0..20 dB, 0 | `P_GAIN` | 78, 271 |
| `EnableAcoustics` (`enableAcoustics`) | true | `P_DISABLE_RFL` = !value | 99, 272 |
| `ReverbSendDb` (`reverbSendDb`) | −60..20 dB, 0 | `P_REVERB_SEND` | 120, 273 |

- **`enum NativeParameterIndex : int` (`:165-262`).** In order: `P_GAIN, P_USEINVSQR`(unused)`, P_NEAR, P_FAR`(unused)`, P_RADIUS, P_DISABLE_RFL, P_AMBISTAT, P_READONLY_GLOBAL_RFL_ENABLED, P_READONLY_NUM_VOICES, P_HRTF_INTENSITY, P_REFLECTIONS_SEND, P_REVERB_SEND, P_DIRECTIVITY_ENABLED, P_DIRECTIVITY_INTENSITY, P_AMBI_DIRECT_ENABLED, P_REVERB_REACH, P_DIRECT_ENABLED, P_OCCLUSION_INTENSITY, P_MEDIUM_ABSORPTION, P_NUM`.
- **Per-frame cost.** `Update` calls `UpdateParameters()` every frame, which is 3 `SetSpatializerFloat` calls. That is cheap and allocates nothing.
- **Near field.** There is no near-field property in v85, and the inverse-square params are marked `[UNUSED]`.

### 3.3 `MetaXRAudioSourceExperimentalFeatures`
`Runtime/scripts/MetaXRAudioSourceExperimentalFeatures.cs`, `[RequireComponent(typeof(MetaXRAudioSource))]` (`:42-43`). Fields, pushed every frame in `UpdateParameters` (`:278-289`):

Property, default and line:
- `HrtfIntensity` 0..1, 1 (`:51`); `VolumetricRadius` m, 0 (`:75`);
- `EarlyReflectionsSendDb` −60..20, 0 (`:95`); `ReverbReach` 0..1, 0.5 (`:119`);
- `OcclusionIntensity` 0..1, 1 (`:140`); `DirectivityIntensity` 0..1, 1 (`:167`);
- `DirectivityPattern {None, HumanVoice}`, None (`:158-162,191`); `DirectSoundEnabled` true (`:210`);
- `MediumAbsorption` true, ray tracing only (`:229`).

Use `HumanVoice` for Sunny. Volumetric radius of about 0.3 m suits the piano sources and 0.2 m the harp (tuning).

### 3.4 Room: `MetaXRAudioRoomAcousticProperties`
`Runtime/scripts/MetaXRAudioRoomAcousticProperties.cs`, `public sealed class ... : MonoBehaviour` (`:36`).

- **Fields.** `lockPositionToListener=true` (`:42`); `width=8, height=3, depth=5` m (`:46-54`); `left/right/front/backMaterial = GypsumBoard`, `ceilingMaterial = AcousticTile`, `floorMaterial = Carpet` (`:59-84`); `clutterFactor 0..1 = 0.5` (`:101`).
- **Clutter bands.** The highest of 4 bands gets `clutterFactor`; each lower band gets half of the one above (`:186-191`).
- **`enum MaterialPreset` (`:114-152`).** 36 presets, AcousticTile = 0 … MetaDefault. The ones used here are Brick, Carpet, Glass, GypsumBoard, PlasterOnBrick, WoodFloor and Curtain. Each has 4 band reflection coefficients in `GetMaterialPresetBands`.
- **`Update()` (`:175-193`).**
  - Writes the walls in native order right, left, ceiling, floor, front, back. `NativeInterface.SetAdvancedBoxRoomParameters` documents the same order (`MetaXRAudioNativeInterface.cs:150`).
  - Then calls `Interface.SetAdvancedBoxRoomParameters(width, height, depth, lock, position, wallMaterials)` and `SetRoomClutterFactor`. The Unity wrapper negates z (`:289-294`).
  - It does this **every frame**, and `GetMaterialPresetBands` returns a `new float[]` per call (`:195-237`): 6 allocations per frame. This is from source only, because the batchmode heap meters read 0 even for a calibration allocation.
  - **Set `room.enabled = false` after its first `Update`; re-enable it for one frame when dimensions or materials change.**
- **Room lifecycle.** With no room present, `CheckSceneHasRoom` (`:154-173`) applies a temporary default room; the log "No Meta XR Audio Room found, setting default room" was seen. Exactly one room is allowed; more than one is an error.
- **Virtual room.** W 6.4 × D 5.2 × H 2.9. The centre in Unity is (0, 1.45, −1.48): the source layout has cz = 1.48, and z → −z. Set `lockPositionToListener=false` with the transform at that centre.
  - Materials: left, right and front `PlasterOnBrick`; back (fireplace) `Brick`; ceiling `GypsumBoard`; floor `WoodFloor`; `clutterFactor` 0.6 (furniture and rug).
  - Whether "front" means Unity +z is **UNVERIFIED**.
- **MR mode ("play in my room").** Set the dimensions from the MRUK room bounds, materials `GypsumBoard` / `Carpet`, and the centre at the room anchor centre (another spec covers MRUK). Keep `MetaXRAcousticSettings.AcousticModel = ShoeboxRoom`.
  - The default is `Automatic` (`Runtime/scripts/MetaXRAcousticSettings.cs:46`). Automatic switches to ray tracing whenever acoustic geometry exists (`MetaXRAcousticNativeInterface.cs:33-39`).
  - It is applied `BeforeSceneLoad` from `Resources/MetaXRAcousticSettings` (`:35-39`).

### 3.5 Reflection effect and mixer (required)
- **Create the project mixer** `Assets/Audio/MusicRoom.mixer`, with Master group effects in this order: Attenuation, then **Meta XR Audio Reflection**.
- **Effect parameters.** `"Refl. Enable"`=1, `"Reverb Enable"`=1, `"Reverb Level"`=0 dB (range −60..20), and `"Voice Limit"`. Sources: `MetaXRAudioReflectionCustomGUI.cs:27-29,79` and sample `Samples~/scenes/mixers/SpatializerMixer.mixer:33-44`, whose snapshot holds 1/1/0.
- **Auto suspend.** The sample has `m_EnableSuspend: 1` and `m_SuspendThreshold: -80` (`:82-83`). Set **Auto Mixer Suspend off**: the `m_EnableSuspend` property via SerializedObject on the mixer asset. The reverb disappeared whenever nothing fed the mixer (E3).
- **Creating it.** There is no public API to create mixers or effects. Make it once in the editor, or copy the SDK sample (the tests load such a copy). Route every source with `outputAudioMixerGroup = group`.
- **Native toggles.** `MetaXRAudioNativeInterface.Interface.SetEnabled(int, bool)` (`MetaXRAudioNativeInterface.cs:174,312`) with `Meta.XR.Audio.EnableFlag {SIMPLE_ROOM_MODELING=2, LATE_REVERBERATION=3, RANDOMIZE_REVERB=4, PERFORMANCE_COUNTERS=5}` (`:39-46`). It returned 0 (ok) but did not create reverb without the mixer (E3-vi).
- **Global wet level.** `SetSharedReverbWetLevel(float linear)` (`:165,305`).

### 3.6 Quest checklist
- [ ] `AudioProjectSetup.Apply` has run and its output is committed: spatializer and ambisonic decoder `Meta XR Audio`, DSP 256, Stereo, Classic, Rolloff Scale 1.1, Doppler 0, `m_DisableAudio=0`. With audio disabled, `editor/MetaXRAcousticBuildPreprocessor.cs:67-80` drops `libMetaXRAudioUnity.so`.
- [ ] arm64 `Runtime/Plugins/Android/libs/arm64-v8a/libMetaXRAudioUnity.so` is enabled for Android and preloaded (its `.meta` file).
- [ ] The project mixer has the Reflection effect and suspend off; all sources are routed to it; there is exactly one room component.
- [ ] At startup, log `outputSampleRate`, `GetDSPBufferSize`, `GetSpatializerPluginName()` and `MetaXRAudioSettings.Instance.voiceLimit`. Assert that the spatializer is `"Meta XR Audio"`; otherwise everything plays 2D (E1-D).
- [ ] Keep `AudioSettings.Mobile.stopAudioOutputOnMute=false` (default; UNVERIFIED on Quest). On `OnApplicationPause(false)`, re-`Play()` any source with `!isPlaying`.

---

## 4. Engine architecture (Q3)

### 4.1 Graph
```
main thread: instruments → AudioEngine API → stage SynthCommand → per-source SpscRing → Commit
audio thread (1 mixer thread, every 5.33 ms):
  SynthSource.OnAudioFilterRead ×17 → [drain ring → voices.Render (sample-exact) → processors (beds)
                                       → modal resonator on the send → bus gain] → channel 0..n
    → Meta XR Audio spatializer (HRTF, distance = Unity rolloff, ER/reverb send)
    → AudioMixer group "Room" [Attenuation, Meta XR Audio Reflection (+shared ER/late reverb)]
    → AudioListener: MasterBus.OnAudioFilterRead (gain 0.9 → compressor → shelf → ceiling) → device
workers (startup): OfflineRenderQueue ×2 → PianoRenderer / HarpRenderer → SampleBank (Volatile.Write)
```

### 4.2 Sources
Source names and positions follow spec 06 §10. Convert the right-handed source layout (x, y, z) → Unity (x, y, −z); parent each source to its rig with the local offset.

| SourceId | Source name | Placement (Unity) | Pools noise/tone/metal/buffer | Notes |
|---|---|---|---|---|
| PianoLo/Mid/Hi | piano-lo/-mid/-hi | piano local (−0.42/0/0.42, 0.05, 0.05) | 16/0/0/48 each | MIDI <52, <76, ≥76; one soundboard bank per source |
| Harp | harp | harp local (−0.33, 0.8, 0) | 24/0/0/48 | soundbox bank |
| Kick, Snare, Hat, Tom1, Tom2, Floor, Crash, Ride | drum:<kind> | the piece's group | see `AudioRig.Configure` | hat, hatOpen and hatClose share Hat |
| Fire / Snow / Wind | fire / snow / wind | (0.3, 0.5, −3.85) / (3.1, 1.55, −1.18) / (3.1, 1.9, −1.18) | processors | `EnableAcoustics=false` (no reverb send) |
| Guide | guide | Sunny | tone 8, plus a separate line AudioSource | `HumanVoice` directivity |
| Ui | (master) | none; `spatial=false` | 8/8/0/0 | layout whoosh, UI clicks |

The `drum:room` send (a 0.45 s IR, never positioned) is replaced by Meta early reflections. Tuning start: drums `ReverbSendDb` −3 (set by `AudioRig`) plus `EarlyReflectionsSendDb` +3 through `MetaXRAudioSourceExperimentalFeatures` (not added by `AudioRig` yet).

### 4.3 Main-thread API
`AudioEngine` (voice recipes in spec 06 §2–§8). Everything is allocation-free by construction: struct commands, no closures or LINQ on the hot path.

```csharp
public double Now { get; }        public double FrameNow { get; }          // dspTime
public VoiceHandle Piano(int midi, float vel, double when = 0);           // spec 06 §3.2
public void Release(VoiceHandle h, bool thud = true, double when = 0);   // τ 60 ms, stop +0.5 s
public VoiceHandle Pluck(int stringIndex, float vel, double when = 0);   // spec 06 §5.1 (old)
public void Damp(VoiceHandle h, int hands = 1, double when = 0);         // τ 18 ms, stop +0.25 s
public void Drum(DrumKind kind, float vel, DrumStrike strike, double when = 0); // spec 06 §4 (full table)
public void Choke(DrumKind kind);                                        // τ 12 ms
public void SetFire(bool on); SetWind(bool on); SetSnow(bool on);        // spec 06 §7, bus τ 0.4 s
public void Noise/Tone/Metal(SynthSource dest, double when, ...); public void Flush(); // building blocks (stage only)
```

- **Still to add** (§4.8): `SnowGust(strength, attack, hold, release)` and `PluckNew(..., position)`. Both build mechanically on the same primitives.
- **Handles.** `VoiceHandle {SynthSource Source; int Id}` identifies a playing voice. Ids are engine-unique; releasing an ended voice is a no-op.
- **Recorder and lessons.** Timestamp with `Now`. On playback, schedule events 50 ms ahead at `When = playStartDsp + ev.t`; this is sample-exact, unlike per-frame dispatch.

### 4.4 Voices
All voices derive from `Voice` (`Voices/Voice.cs`): `Begin`, `StopAt`, `ReleaseAt(sample, tau, stopAfter)`, and `Render(mix, send, blockStart, frames)`, which splits the block at start, release and stop.

| Voice (spec 06) | Unity class | Notes (constants: spec 06 §2–§7) |
|---|---|---|
| `noiseVoice` (§2) | `NoiseVoice` | Shared noise table from index 0. Chain bp (linear Q) → hp (1 dB) → lp (1 dB, only if <20 k). Envelope max(peak, 5e-4) → exponential to 5e-4 over decay; stops at decay + 50 ms. |
| `toneVoice` (§2) | `ToneVoice` | Sine table, PolyBLEP square ×0.848, or triangle. f0→f1 exponential over `slide`; optional lp. |
| `metalVoice` (§2) | `MetalVoice` | 6 squares {263, 400, 421, 474, 587, 845} × ratio → bp Q 0.9 → hp. |
| `playBuffer` (§3.2) | `BufferVoice` | Rate with linear interpolation, lp Q 0.4 dB, level. `Res` feeds the resonator send. Waits ≤300 ms for a buffer still rendering. Jitter is computed on the main thread (piano j = 0.25). |
| harp pluck comb (§5.2) | `BufferVoice` `Comb` / `F0` | y = x(n) − d·x(n − lag), read from the buffer itself, so no delay line is needed. |
| body convolvers (§3, §5) | `ModalResonator` | Σ decaying sines (§6) equal 2-pole sections: b0 = a·sin φ, b1 = −a·r·sin(φ−W), a1 = −2r·cos W, a2 = r². |
| room convolvers (§1.6, §4) | Meta shoebox | §4.7. |
| fire / wind / snow beds (§7) | `NoiseBed`, `FirePops` | Pops come from an audio-thread 110 ms clock with p 0.65; the bed LFOs start at phase 0. |
| snow gust (§7) | TODO `ISynthProcessor` | Linear-ramp BP with Q 0.7, plus a 6 kHz HP branch at ×0.12. |

Pools are fixed and allocated in `Awake`; `Take()` steals released voices first, then the oldest. Choke: all chokable cymbal and hat voices use `Group 1`, and `ChokeGroup` ramps every voice of that group started at or before the choke.

### 4.5 Offline renders and memory
- **Piano.** `PianoRenderer.Render(midi, sr)` implements spec 06 §3.1: up to 12 partials, each with 2 strings detuned 1.2 + 2.5u cents; each partial uses `Envelope` segments 0.0001 → amp (4 ms) → 0.45·amp (120 ms) → max(amp·0.001, 1e-5) at min(T, 0.12 + 5.5τ); a recursive double phasor; a 30 ms hammer noise through BP Q 0.8; out ×0.5; normalized to 0.9 with a 50 ms tail fade.
- **Harp.** `HarpRenderer.RenderOld` implements spec 06 §5.1. Randomness is seeded per note, so renders are deterministic.
- **Queue.** `OfflineRenderQueue(bank, n).Add(slot, job)` and `.Start(workers)`. Threads are `IsBackground`, `BelowNormal`. Poll `Progress`, `Completed` and `IsDone` from the main thread; workers never call into Unity.
- **Order.** Middle-out from C4. **A loop bound of `d <= 44` would never precompute MIDI 105–108**; ours uses `d <= 48`.

- **Measured (editor, Mono JIT).**
  - 88 keys on 1 thread: 2.59 s, slowest key 60 ms.
  - 88 keys plus 22 strings on 2 workers: 1.15–1.40 s; the first key is ready in 24–47 ms.
  - Piano at 48 kHz: **17,740,758 samples = 67.7 MiB** float32 (62.2 MiB at 44.1 kHz). Harp: 12.2 MiB old, 11.8 MiB new.
- **Quest estimate.** About ×2–3 (UNVERIFIED): 3–4 s for everything, with C4 ready in under 0.15 s. With 2 workers it finishes during Sunny's intro.
- **Memory.** Fine on 8 GB. int16 storage would halve it, at one multiply per read.

### 4.6 Master bus (`Runtime/MasterBus.cs`, verified)
- **Placement.** On the AudioListener (the XR CenterEyeAnchor camera).
- **Chain (spec 06 §1.6).** ×0.9 → compressor (threshold −14, knee 18, ratio 4, attack 3 ms, release 180 ms) → high shelf 7.5 kHz +1.5 dB (S = 1) → ceiling 0.98.
- **Static curve.** Spec 06 §1.5 (`KneeCurve`, `Saturate`, `KAtSlope`, 15 geometric-mean steps), with makeup (1/Saturate(1, k))^0.6.
- **Results.** k = 1.5125 and **makeup +2.14 dB**; both the Python model and the C# run print 2.14.
  - Static input → output, including makeup: −20 → −17.9, −10 → −8.1, −6 → −5.0, 0 → −1.4, +6 → +0.5 dBFS.
- **Pre-delay.** The reference compressor adds a 6 ms `kPreDelay` look-ahead to everything; we don't.
- **Exact dynamics.** For the reference attack and release, implement spec 06's `makeCompressor()` (§6.3).

### 4.7 Reverb mapping (tuning, UNVERIFIED parity)
- **Reference send levels.** Piano 0.22, harp old 0.34 / new 0.3. Drums: snare noise 0.3, toms 0.15, crash 0.25/0.2, ride 0.1–0.2, plus the 0.45 s drum room. These sends are pre-panner, so wet level does not fall with distance.
- **Meta at 2 m, send 0 dB.** Tail/burst −20 dB, early −13.7 dB.
- **Start values.** Piano `ReverbSendDb` 0, harp +3, drums −3 with ER +3. Fire, wind, snow and Sunny get `EnableAcoustics=false`. Use `ReverbReach` 0.7 for the instruments to keep the wet level up with distance.
- **Tuning.** Do it on the headset with `SetSharedReverbWetLevel`.

### 4.8 Guide, MR, recorder
- **Sunny's chirps** (spec 06 §8) are `Tone(Square, ...)` on the Guide source, the second at `When = −0.065` in the same batch.
- **Voice lines.** Unity does not import `.m4a` (UNVERIFIED for 6.3; use WAV). Ship the 23 lines as WAV, imported as Decompress On Load.
  - Play each line on a second AudioSource at Sunny with `spatializePostEffects=true`.
  - A `RobotFilter.OnAudioFilterRead` processes channel 0 in place (spec 06 §8): dry 0.92 + ring 0.08 × sin(80 Hz); comb 5 ms, feedback 0.3, level 0.12; HP 120 Hz (Q 0.7 dB); peaking 2.5 kHz Q 0.8 +2 dB.
  - Publish the RMS for the mouth.
- **MR snow behind real windows.** Move the Snow and Wind sources to the window anchors. Update the room box (§3.4).

### 4.9 CPU
- **Per voice (E8).** ns per sample per active voice, editor Mono JIT on M5 Pro:
  - noise bp+hp+lp 24.3, noise hp 11.4;
  - tone sine with glide and lp 17.7, tone square with lp 20.3;
  - **metal 74.4**;
  - buffer 12.5, buffer with comb 16.5;
  - soundboard bank 35.6 per source (always running).

- **Busy groove (E12).** All instruments at once for 4 s: 16th-note drums, crash, ride, 30 held piano notes, harp plucks every 125 ms, all 3 beds.
  - Sum of per-source averages: **428–477 µs per 5333 µs block (8.0–8.9 %)**; sum of maxima 741–1006 µs.
  - Heaviest sources: Ride 101–110 µs (metal), Harp 97–127 µs, piano 34–49 µs each.
  - Dropped commands: 0.
- **Quest estimate.** About 2× the editor numbers for IL2CPP ARM64 on XR2 Gen 2 (UNVERIFIED): ~0.9 ms average and ~2 ms peak per 5.33 ms block on the single mixer thread, plus Meta HRTF for 17 sources and the reverb. Budget: keep our C# under 1.5 ms per block. Measure on device with `SynthSource.Avg/MaxBlockMicros`. In a development build, the Unity Profiler shows the DSP nodes "SynthSource", "Meta XR Audio" and "Meta XR Audio Reflection" (the marker names exist; the recorders read 0 in batchmode).
- **Optimizations in order.** All exact unless noted.
  1. Pre-render each metal ratio's raw 6-square sum once (deterministic, since the oscillators restart at phase 0 per hit), giving metal ≈ buffer + 2 biquads, about 3× cheaper.
  2. Skip the resonator while its input and state are below 1e-7.
  3. End `BufferVoice` at a precomputed "−90 dB point" per buffer.
  4. Burst `FunctionPointer` kernels; these can be called from the audio thread.
  5. Wind sweep coefficients are already updated only every 32 samples.

### 4.10 Latency budget (fingertip → ear)
- **Budget.**
  - Detection in `Update`: 0–14 ms at 72 Hz (hand-tracking latency not counted).
  - Post → block start: 0–5.33 ms (E10).
  - Unity output ring: 4 × 256 = 21.3 ms worst case in the editor.
  - Quest buffer count and AAudio/HAL time (~5–10 ms): UNVERIFIED.
  - **Expected total: about 20–35 ms.**
- **Rules.** Use ASAP `When` for live strikes. No `PlayScheduled`. No compressor look-ahead. Keep the DSP buffer at 256.
- **Measure on device.** `adb shell dumpsys media.audio_flinger` shows the output thread's frames and latency. Also film a pad tap together with the headset audio.

---

## 5. Code (Q4) — compile and run verified
The sketch holds `Runtime/` (asmdef `MusicRoom.Audio`, references `Meta.XR.Audio`, platforms Android, Editor, macOS, Win64), `Editor/` (`MusicRoom.Audio.Editor`) and `Tests/` (PlayMode, `overrideReferences` + `nunit.framework.dll`, `UNITY_INCLUDE_TESTS`).

| File | What it contains |
|---|---|
| `Runtime/SpscRing.cs` | Lock-free ring with stage and commit |
| `Runtime/SynthCommand.cs` | Command struct |
| `Runtime/Voices/Voice.cs` | Voice base class |
| `Runtime/Voices/SynthVoices.cs` | Noise, Tone, Metal and Buffer voices |
| `Runtime/Dsp/Biquad.cs` | RBJ filters with spec 06 §1.2 semantics |
| `Runtime/Dsp/Envelope.cs` | Parameter-automation subset (spec 06 §1.1) |
| `Runtime/Dsp/Noise.cs` | xorshift32 and the shared table |
| `Runtime/Dsp/Oscillators.cs` | Sine table, PolyBLEP |
| `Runtime/Dsp/ModalResonator.cs` | Body resonator bank |
| `Runtime/SynthSource.cs` | `OnAudioFilterRead` mixer |
| `Runtime/AudioEngine.cs` | Main-thread API, full drum table |
| `Runtime/AudioRig.cs` | Builds the 17 sources |
| `Runtime/Ambience.cs` | Noise beds and fire pops |
| `Runtime/Offline/SampleBank.cs` | Bank and render queue |
| `Runtime/Offline/Renderers.cs` | Piano and old harp |
| `Runtime/MasterBus.cs` | Master chain |
| `Editor/AudioProjectSetup.cs` | `Apply` and `Probe` |

Result: zero `error CS` or `warning CS`. PlayMode: `Spike`, `EngineLoad` and `AllocCheck` all **Passed**.

Voice base: sample-accurate segments (excerpt of `Voice.Render`):
```csharp
long blockEnd = blockStart + frames;
if (StartSample >= blockEnd) return true;                       // scheduled later
int from = StartSample > blockStart ? (int)(StartSample - blockStart) : 0;
while (from < frames) {
    long now = blockStart + from;
    if (now >= _stopAt) { Active = false; return false; }
    if (now >= _releaseAt) { Gate.SetTarget(0.0, _releaseTau * SampleRate);
        if (!float.IsInfinity(_releaseStopAfter)) StopAt(now + (long)(_releaseStopAfter * SampleRate));
        _releaseAt = long.MaxValue; Released = true; OnRelease(now); }
    long next = Math.Min(blockEnd, Math.Min(_releaseAt, _stopAt));
    int to = (int)(next - blockStart);
    if (to > from && !Process(mix, send, from, to)) { Active = false; return false; }
    from = to; }
return true;
```

Mixer skeleton (`SynthSource.OnAudioFilterRead`):
```csharp
int frames = data.Length / channels; if (frames > MaxFrames) { Array.Clear(data, 0, data.Length); return; }
long blockStart = (long)Math.Round(AudioSettings.dspTime * _sampleRate);   // same value for every source in the block
Array.Clear(_mix, 0, frames); if (_resonator != null) Array.Clear(_send, 0, frames);
Drain(blockStart, sr);       // When>0 abs: max(blockStart, round(When*sr)); 0: blockStart; <0: blockStart + round(-When*sr)
for (int i = 0; i < _poolEnd; i++) { var v = _voices[i]; if (v.Active) v.Render(_mix, send, blockStart, frames); }
for (int p = 0; p < processorCount; p++) _processors[p].Process(_mix, blockStart, frames);
_resonator?.ProcessAdd(_send, _mix, frames, resonatorGain);
for (int i = 0, j = 0; i < frames; i++) { float s = _mix[i] * _bus.Next(); for (int c = 0; c < channels; c++) data[j++] = s; }
```

`Biquad.Set` uses the RBJ formulas with the Q conventions of §6.1 (full table in 06 §1.2). Envelope and noise follow the parameter-automation rules of 06 §1.1:
```csharp
public float Next() {                   // returns the value AT this sample, then advances
    double v = _value;
    if (_targetMode) { _value += (_target - _value) * _coef; return (float)v; }  // setTargetAtTime: coef = 1 - e^(-1/τ)
    if (_index >= _count) return (float)v;                                       // hold
    ref Segment s = ref _segments[_index];
    if (s.Kind == Kind.Linear) _value += _step; else if (s.Kind == Kind.Exponential) _value *= _step; // step = (v1/v0)^(1/n)
    if (--_left <= 0) { if (s.Kind != Kind.Hold) _value = s.Target; _index++; Enter(); }
    return (float)v; }
public uint NextUInt() { uint x = _state; x ^= x << 13; x ^= x >> 17; x ^= x << 5; _state = x; return x; } // xorshift32
public float NextBipolar() => (int)NextUInt() * (1f / 2147483648f);
```

---

## 6. DSP semantics rules
Spec 06 is the authority for per-voice constants (`06-audio-dsp.md` §1–§7, `06-audio-dsp.reference.mjs`). The engine-level rules, checked against the reference implementation, are:

1. **Biquad Q.** For lowpass and highpass, Q is in **dB** (`biquad.cc`: `resonance = pow10(resonance/20)`). For bandpass and peaking it is linear. So a lowpass Q of 0.4 means 1.047, and an unset Q means 1 dB, which is 1.122.
2. **Convolver normalization.** Impulse responses are always normalized: `scale = 1/max(rms, 1.25e-4) · 10^(−58·0.05) · 44100/sr`. `ModalResonator.Build` applies this; its tail rings past the reference's 0.28 s IR truncation.
3. **Compressor.** Makeup `(1/Saturate(1,k))^0.6` = +2.14 dB, and `kPreDelay` 6 ms. `MasterBus` keeps the static curve exact and simplifies attack and release. For the exact reference dynamics (adaptive release, 32-frame envelope), implement `makeCompressor()` from the 06 reference minus the pre-delay.
4. **Oscillators and buffers.** Oscillators start at phase 0. The band-limited square's plateau is about ±0.848, so `Oscillator.SquareScale` = 0.848 (06 §1.3). `playbackRate` uses linear interpolation. Every noise voice restarts the shared 2 s buffer at offset 0. Use JS `Math.round` semantics, `Math.Floor(x + 0.5)`.

## 7. Risks and UNVERIFIED items
- **Device-only numbers.** Quest output rate (48 kHz expected), `numBuffers`, end-to-end latency and CPU (×2 extrapolation) are all **UNVERIFIED**. Measure them in the first device build (§3.6, §4.9, §4.10).
- **Measured on macOS only.** The reverb, mixer and channel-0 behaviour was measured with the macOS editor plugin. The arm64 `.so` contains the same `MetaXRAudioUnityReflection` symbols. Mixer suspension with silent carriers, and the shoebox front/back orientation, are UNVERIFIED.
- **GC.** Allocation-freedom is by construction (struct commands, preallocated pools, no closures, LINQ or strings on hot paths). Batchmode meters cannot see allocations, so confirm with a Profiler allocation capture on device.
- **Not yet implemented.** The new harp sound (spec 06 §5.2), the snow-gust processor and the robot line filter are designed here (and in 06) but not implemented. Their primitives are verified.
- **Scratch copy only.** All experiments ran in a scratch copy of the project. To adopt: take the `Runtime` and `Editor` sources, run `Apply`, create the mixer.
