# Harp: requests for foundation changes and merge notes

## Integration status (2026-10-31): APPLIED

Merged into the project by the integrator. Every item below is resolved as stated here; the text after
this box is the module's original request, kept for the record.

| # | Request | Resolution |
|---|---|---|
| 1 | `SynthSource`/`AudioRig` race | Audio module's version merged, plus `playOnAwake = false` in `AudioRig.Build` |
| 2 | `LitEmissive` loses `_EMISSION` | `MaterialTemplates.cs`: `RealtimeEmissive` GI flag + `_EMISSION`; `render-setup` rewrote the template (`m_ValidKeywords: _EMISSION`) |
| 3 | `IAudioEngine.BlockSeconds` | Added (default 256 / SampleRate; `AudioEngine` reads the real DSP buffer in `Init`). `EngineSound` keeps its own equivalent read |
| note | Recorder playback wiring | Present in `Lessons/PlaybackWiring.cs` (Harp and HarpDamp targets) |
| note | Contact shadow | CHANGED at integration: Room's `RigShadows` already had a harp entry (FloorDecal, stays on the floor), so the harp no longer builds its own blob (`Blob` is null); one blob under the harp. Tests updated (5 own draws) |

---

The Harp module edits only `Assets/MusicRoom/Harp/**`, `Tests/EditMode/Harp/**` and `Tests/PlayMode/Harp/**`. It
compiles and passes against the revised foundation (review round 1, ARCHITECTURE.md section 21) as is. The harp itself
needs **no** foundation change. The items below are foundation bugs it found or confirmed, and notes for the integrator.

## 1. `Audio/Engine/AudioRig.cs` + `Audio/Engine/SynthSource.cs`: audio-thread race at start-up (Audio module)

Owner: the Audio module, which is reworking `SynthSource` in its own clone. Its current code already
allocates first and publishes the voice pool last. This item is here only so the integrator checks that the merged
Audio folder has all three parts.

- **Symptom:** `ArgumentNullException: Value cannot be null. Parameter name: array` from the audio thread
  (`SynthSource.OnAudioFilterRead` -> `Array.Clear(_mix, ...)`) while `AudioModule.Init` builds the sources. It fails
  whichever PlayMode test is booting. On 2026-10-31 it hit `AppBootTests.HoverGlowsAreOneDynamicMeshWithoutPropertyBlocks`
  in a plain `test-play` (no `--gpu`) on this clone, before the local patch below.
- **Exact change** (applied *locally* in this clone only, so its runs are not flaky; the Harp merge does not carry it):

```csharp
// AudioRig.Build, right after AddComponent<AudioSource>():
var src = go.AddComponent<AudioSource>();
src.playOnAwake = false;            // no clip-less play before SynthSource.Awake has configured the source

// SynthSource.Awake: build everything the audio thread touches first, publish the voice pool last.
_mix = new float[MaxFrames];
_send = new float[MaxFrames];
_pending = new float[MaxFrames];
if (resonatorModes != null && resonatorModes.Length > 0)
{
    var resonator = new ModalResonator();
    resonator.Build(resonatorModes, resonatorSeconds, (float)_sampleRate, resonatorSeed);
    _resonator = resonator;
}
_bus.SetValue(busGain);
var voices = new Voice[_poolEnd];
for (int i = 0; i < _poolEnd; i++)
    voices[i] = i < _toneFrom ? new NoiseVoice() : i < _metalFrom ? new ToneVoice()
              : i < _bufferFrom ? (Voice)new MetalVoice() : new BufferVoice();
Volatile.Write(ref _voices, voices);

// SynthSource.OnAudioFilterRead: the guard reads the published pool.
if (frames > MaxFrames || Volatile.Read(ref _voices) == null) { Array.Clear(data, 0, data.Length); return; }
```

## 2. `Editor/MaterialTemplates.cs`: the `LitEmissive` template loses `_EMISSION` (foundation, affects other modules)

The harp no longer depends on this, because its glow is vertex data in its own shader. The bug is still present in the
revised foundation (`Assets/MusicRoom/Editor/MaterialTemplates.cs:27`).

```csharp
// MaterialTemplates.Foundation(), the LitEmissive init:
m.EnableKeyword("_EMISSION");
m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;   // was: None
```

Then run `Tools/unity.sh render-setup`. Why: URP 17's material postprocessor derives `_EMISSION` from the GI flags on
import (`BaseShaderGUI.SetMaterialKeywords`). With `None`, the committed `LitEmissive.mat` has `m_ValidKeywords: []`,
so every clone of it ignores `_EmissionColor`.

## 3. Optional: `double IAudioEngine.BlockSeconds { get; }`

ARCHITECTURE.md 15.3 schedules sweep plucks at `FrameNow + 256 / SampleRate + delay`. `EngineSound` (Harp) uses the
real DSP block (`AudioSettings.GetDSPBufferSize` read once / `SampleRate`). That equals 256/SR now, and it follows
`AudioModule.PreferredDspBuffer` if that knob is ever set to 128. An engine property would make this a contract value:

```csharp
// IAudioEngine
double BlockSeconds { get; }   // AudioEngine: _dspLength / (double)SampleRate, read once in Init
```

## Merge notes (not contract changes)

- **Recorder playback (Lessons):** wire
  `Recorder.SetTarget(TakeEventType.Harp, e => harp.PlaybackPluck(e.i, e.vel, e.u, e.speed, e.mode))` and
  `Recorder.SetTarget(TakeEventType.HarpDamp, e => harp.PlaybackDamp(e.i))` with `harp = app.Get<IHarpApi>()`.
  `IRecorder` has no `SetTarget`, so the Lessons module owns the wiring.
  Playback plucks are live (`when = 0`).
- **Contact shadow (Room):** the harp builds its own blob under its `Rig.Group` (Docs/07-world-visuals.md `rigDecor.harpBlob`, harp-local
  (-0.2725, 0.005, -0.168), 1.1 x 0.7, opacity .45, `SharedTextures` `Core/soft_dot`), so it travels with the harp. The
  Room module must not add a second one. It is one of the harp's 6 draws.
- **Desktop (Layout / every module):** the harp's click target is a `BoxCollider` on `Layers.PlaySurface`
  (`World/Harp/StringsHitPlane`, 1.0 x 1.4 x 0.002 m at harp-local (-0.33, 0.72, 0)). It has no renderer, so it is
  neither a handle nor a played part. The harp acts only when the nearest `PlaySurfaceMask` hit is that collider.
  `DesktopRigFallback.dragBlockMask` (set by AppRoot) then keeps the look-around off for that press. The harp no longer
  touches `DesktopRigFallback.dragBlocked`.
- **Layout footprints:** `Rig.Handles` are the 12 frame parts as **invisible** renderers (`forceRenderingOff`). Bounds,
  transforms and meshes are valid, but they never draw. The frame is drawn by 4 combined meshes (one per material).
  Layout should read `Renderer.bounds` / the mesh as usual and must not toggle `forceRenderingOff` or `enabled` on handles.
- **Render pipeline:** the strings shader lights per vertex with `GetAdditionalLightsCount()` / `GetAdditionalLight(i, pos)`,
  which is right for URP Forward (the project's mode). If the URP asset is ever switched to Forward+, the strings
  would get only the main light and the ambient light.
