# Room module: contract requests

## Integration status (2026-10-31): APPLIED

Merged into `vr-music-room-unity` by the integrator. Every item below is resolved as stated here; the text after
this box is the module's original request, kept for the record.

| # | Request | Resolution |
|---|---|---|
| A | `TextureBakeSpec.AnisoLevel` | Added (`Core/Contracts/Providers.cs`, `Editor/TextureBaker.cs`); `RoomTextures.BakeSpecs` sets `AnisoLevel = AnisoFor(n)`; the load-time raise stays as a fallback |
| B | `SynthSource` race | Audio module's version merged, plus the `AddInput` guard requested here |
| C | `LitEmissive` | Applied (see Harp 2) |
| D | Bake names in ARCHITECTURE 13 | Recorded in ARCHITECTURE.md section 21 (integration entry); the project had no stale Room PNGs (fresh bake: 35 textures) |
| test | Table handles / draws | The Lessons deck joins the table rig (handles appended in place, 7 draws): Room tests now count the table's own 8 and the shared table + deck + switches budget of 15 |

---

The Room module's clone (`m-room`) is rebased onto the revised foundation (ARCHITECTURE.md section 21) and implements every round-1 contract change itself: Core's `ISnowPortals` /
`ISnowPortal`, `Rig.Handles` as the list filled once, `Layers.PlaySurface`, `App.IsPresenting`, `Pointer.FromMouse`,
the shared Core textures (`SharedTextures`, bake names `<Module>/<name>`), `TestBoot` in every Room PlayMode test, no
MaterialPropertyBlock, the per-rig draw budgets. Nothing below blocks the Room module; each item says who would change
what, with the exact code.

## A. Core / Editor: anisotropy for baked textures (minor)

**Why.** The floor, walls, rug and bricks are seen at grazing angles; the spec samples them with anisotropy 4 (the night
sky 8). `TextureBakeSpec` has no field for it, so a baked PNG imports with the importer's default 1.

**Change.** `Core/Contracts/Providers.cs`, in `struct TextureBakeSpec`:

```csharp
        /// <summary>Anisotropic filtering level for the importer (0 = the importer's default).</summary>
        public int AnisoLevel;
```

`Editor/TextureBaker.cs`, in `BakeAll`, after `imp.mipmapEnabled = spec.Mipmaps;`:

```csharp
                if (spec.AnisoLevel > 0) imp.anisoLevel = spec.AnisoLevel;
```

Room would then set `AnisoLevel = RoomTextures.AnisoFor(n)` in `RoomTextures.BakeSpecs`.

**Workaround now.** `TextureLoader.Get` raises `anisoLevel` on the loaded texture at runtime (`RoomTextures.AnisoFor`).

## B. Audio: `SynthSource.Awake` races the audio thread (applied in this clone's Audio folder; please merge)

**Seen.** `ArgumentNullException: Value cannot be null. Parameter name: array` at `SynthSource.OnAudioFilterRead`
(`Array.Clear(_mix, 0, frames)`), logged from the audio thread while a test boots. During this rebase it failed the
foundation's own `AppBootTests.PinchAndGraspHysteresisThroughHandInput` once (SetUp: unhandled log message).

**Cause.** `AudioRig.Build` adds the `AudioSource` (playOnAwake defaults to true) before the `SynthSource` and then
activates the object, so the source can already be running filter callbacks while `SynthSource.Awake` builds its
state; `Awake` assigned `_voices` (the callback's only guard) before `_mix`, `_send` and `_pending` existed.

**Change** (`Assets/MusicRoom/Audio/Engine/SynthSource.cs`; `using System.Threading;` is already there). In `Awake`,
build the pool in a local and publish it last:

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
                var resonator = new ModalResonator();
                resonator.Build(resonatorModes, resonatorSeconds, (float)_sampleRate, resonatorSeed);
                _resonator = resonator;
            }
            _bus.SetValue(busGain);
            Volatile.Write(ref _voices, voices);   // publish LAST: the audio callback's guard
```

In `OnAudioFilterRead`: `if (frames > MaxFrames || Volatile.Read(ref _voices) == null)`. First line of `AddInput`
(another source's callback feeding this one): `if (Volatile.Read(ref _voices) == null) return;`.

This clone carries exactly that change (the Audio folder is not Room's, so it does not travel with the Room merge).
The Room tests still collect garbage before each boot (`RoomModuleTests.CollectGarbage`), which only made the window
rarer.

## C. Editor: the foundation's `LitEmissive` template has no `_EMISSION` (affects every module)

**Seen.** `Assets/MusicRoom/Resources/MaterialTemplates/LitEmissive.mat` (in foundation-v1 and in the revised
foundation) has `m_ValidKeywords: []`, so every `MaterialLibrary.LitEmissiveMaterial` / `Clone(LitEmissive)` renders
without emission. The Room's lamp shade and sill mound never glowed before this rebase.

**Cause.** URP Lit's ShaderGUI re-derives the keywords from the material whenever the editor loads the asset
(`BaseShaderGUI.SetMaterialKeywords`). `_EMISSION` stays on only when `globalIlluminationFlags` has an emissive flag
(`AnyEmissive`), and the template sets `MaterialGlobalIlluminationFlags.None`. The same rule drops any texture-driven
keyword (`_NORMALMAP`, `_METALLICSPECGLOSSMAP`, ...) from a URP Lit template that has no texture assigned yet.

**Change** (`Assets/MusicRoom/Editor/MaterialTemplates.cs`, `Foundation()`, the LitEmissive spec):

```csharp
            yield return new MaterialTemplateSpec("LitEmissive", "Universal Render Pipeline/Lit", m =>
            {
                m.SetColor(MaterialLibrary.EmissionColor, Color.white);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;   // URP keeps _EMISSION only with an emissive GI flag
                m.EnableKeyword("_EMISSION");
            });
```

Then delete `LitEmissive.mat` once so `render-setup` recreates it (`Make` keeps an existing asset). A test in
`Tests/EditMode/Editor` could assert `IsKeywordEnabled("_EMISSION")` on the loaded template.

**Room now.** Room does not depend on it: its gloss-map, normal-map and emissive templates (`Room/LitGlossMap`,
`Room/LitNormal`, `Room/LitEmissive`) use `MusicRoom/RoomLit`, URP's own Lit forward pass without a ShaderGUI, so the
keywords its provider sets stay (EditMode `MaterialTemplateAssetsExistForTheRoom` checks them).

## D. ARCHITECTURE.md section 13: the Room's bake names (documentation only)

The room's own bake names are now `Room/plaster`, `Room/rug`, `Room/leaf_atlas` (light | dark), `Room/brick`,
`Room/weave_normal`, `Room/scenery_atlas` (the three paintings, 16-row gutters), `Room/night_sky`, `Room/hill_snow`,
`Room/wood_oak_gloss` and `Room/wood_walnut_gloss` (URP metallic-gloss maps derived from Core's wood roughness bytes).
The woods' colour maps and the soft dot are `SharedTextures` (`Core/wood_oak`, `Core/wood_walnut`, `Core/soft_dot`).
Suggested text for section 13's list: replace `Room/leaf_light`, `Room/leaf_dark`, `Room/gradient_strip` and
`Room/scenery_1..3` with `Room/leaf_atlas`, `Room/scenery_atlas`, `Room/wood_oak_gloss`, `Room/wood_walnut_gloss`.
Integrators: delete the stale PNGs of an older bake in `Generated/Resources/Generated/Room/` (`wood_oak`,
`wood_oak_rough`, `wood_walnut`, `wood_walnut_rough`, `soft_dot`, `leaf_light`, `leaf_dark`, `scenery_1..3`, with
their `.meta`; git-ignored outputs, already removed in this clone).
