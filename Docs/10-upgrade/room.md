# 10-upgrade · room: room acoustics and master bus (implementation spec for Unity C#), revision 2

> **2026-11-11: not in the app.** The room unit built from this spec (`Audio/Sound/Room`), its listener stage and `RoomGroupAssigner` were deleted: the room is Meta's room acoustics and the spec 06 master (the classic sound, the user's choice). `Tools/SoundLab/room/` stays (the hand drums' lab renders through it). This spec is the design record. Backup: `music-room-vr-wip/unity-backups/pre-classic-cleanup/`.

Key: `room`. Owner folder: the room Node lab (`room-engine.mjs`, `instruments.mjs` and `lib/fft.mjs` are vendored at `Tools/SoundLab/room/`). Renders: `room-*.wav` with a listening guide; the `dry`, `new` and `new-subtle` renders are in `Tests/EditMode/Audio/Room/Reference~/renders/`.
Read-only references: `Docs/06-audio-dsp.md`, `Docs/06-audio-dsp.reference.mjs`, `Docs/04-unity-audio-engine.md`, `Docs/ARCHITECTURE.md` §7, `Library/PackageCache/com.meta.xr.sdk.audio@3292c33a8423/` (SDK 85.0.0).

**Transliterate `Tools/SoundLab/room/room-engine.mjs` (v2) to C#.** It is the single source of truth for every algorithm and constant below. Its test vectors are in `Tests/EditMode/Audio/Room/Reference~/test-vectors.json` (§9). v1 is frozen in the lab's `v1-snapshot/` for comparison only.

---

## R. Revision 2: response to the independent critique

The critique (the lab's `critic/`; its original outputs are preserved in `critic/out-before-rev2/`, the re-runs on v2 are in `critic/out/` and `out/critic-rerun-*.log`) rated v1 "better with fixes": two high and three medium issues, plus artefacts and missing reference pieces. All were fixed. Two of the critic's suggested *remedies* were replaced by different ones (R3, R7), with the reasons given. All numbers are per ear (mean of L and R in dB), never on a mono downmix.

| # | Issue (severity) | Verdict | What changed in v2 | Before (v1) → after (v2) |
|---|---|---|---|---|
| R1 | Default room 3–5 dB wetter than the design's own physics; metrics on a mono downmix (high) | fixed | (a) **Frequency-dependent source directivity** `SRC_Q = [1, 2, 2.5]` (low, mid, high band), power pattern ((1+cos θ)/2)^(Q−1) about the source→head axis, applied to every image tap **and** to the late calibration, so early and late agree. (b) **Late field calibrated per octave** against classical diffuse theory at the ear, with the frontal head gain the critic found missing (+0.8 dB in v1). (c) The early/late double count is removed by the longer FDN lines (R5): the late field now builds up from 16.6 ms and is 4.6 dB under the exponential in 10–50 ms (critic 04 hump), so no blanket trim is needed (`LATE_EARLY_TRIM_DB = 0`). (d) Per-ear metrics everywhere. | Piano seat: DRR **0.07 → +6.08 dB** (Barron & Lee Q=2 theory +5.25), C50 11.6 → 13.0 dB, C80 16.9 → 17.2 dB, late after 80 ms −14.1 → **−16.3 dB** re direct (Barron Q=2 −16.0). Engine vs Q=2 theory (critic 02b): piano −0.8, harp −1.1, snare +0.8, kick −0.5, hat −1.8 dB (v1: +2.6…+5.2 dB wetter). |
| R2 | Master overworked by gain staging; drum A/B confounded by the limiter (high) | fixed | `MASTER_DEFAULTS.trimDb` **+1 → −3 dB**. The current kit's hi-hat onset spike (+3.9 dBFS for 0.1 ms) gets a 0.3 ms raised-cosine fade-in **in every drum render (dry, old, new alike)**. It is a kit bug, and the drums owner fixes it in the new kit (§12). | Drum groove: limiter max GR 9.5 → **3.8 dB**, time above 1 dB GR 6.6 % → **1.5 %**, the same in `dry` and `new` (−3.9 / −3.8 dB), so it no longer differs between versions. Error-to-signal of the limiter −9.8 → −19.7 dB (critic 05). Leveler swing per 8th note 1.8 → **0.86 dB** median (critic 05d). Für Elise p→ff range lost 1.39 → **0.38 dB**. Forte chords limiter 6.5 → 3.7 dB. |
| R3 | Thinner bass: point-source floor/back-wall reflections cancel 110–160 Hz; nothing models LF modes (medium) | fixed (different remedy) | (a) **Image-source ER are high-passed at the room's Schroeder frequency** (1st order, f_s = 2000·√(T/V) = 137 Hz here). Below f_s the field is modal, and a low-order point-source image series is not a valid model: it digs deterministic notches (the harp had a −23 dB notch at 125 Hz on one ear). Geometrical acoustics is a high-frequency model (Vorländer 2020; Southern et al. 2013 cross over at the Schroeder frequency). (b) Q = 1 in the low band (instruments are omnidirectional in the bass) puts the LF late field 3 dB above the mid band's Q=2 level, the critic's +3 dB shelf on physical grounds. (c) Per-octave calibration: 250 Hz moves from −4.1 to −1.7 dB re theory. (d) The harp's ER origin moves 0.35 m down to its soundbox (critic's suggestion), and the kit's 0.2 m down toward the kick and floor tom. **Rejected: lowering the piano's ER origin to y 0.5.** The app's piano is a slim 88-key case on an open stand (spec 05 §2.2: bottom 9 cm below the keys), so its radiating body sits at key height. (e) 2nd-order 50 Hz high-pass on the late input: below about 60 Hz this room has single modes (the first axial mode is at 26.8 Hz, about 7 modes below 63 Hz), not a diffuse field. | Tonal fingerprint (direct+room over direct, 125/250 Hz re 1 kHz): piano −2.85/+0.46 → **−1.21/+1.29**; harp −5.68/−0.50 → **−0.92/+0.20**; snare −2.75/−0.93 → **−1.05/+0.74**; kick −4.50/−3.74 → **+0.02/−0.54**; hat −1.96/−0.55 → **−0.86/+0.82** dB (100 ms pre-roll, see R9). Matched LTAS new−dry per octave, 125 Hz–8 kHz: piano −0.3…+0.8, harp −0.7…+0.7, drums −1.0…+2.1 dB (v1: piano 1–4 kHz +2.3…+3.0, harp 126 Hz −5.5). |
| R4 | Blind test "New / Classic" pits New against the never-heard Meta shoebox (medium) | fixed | **Room amount presets** `off / web / low / natural / high` (§5.6). `web` (the no-ER preset) = no ER, late field at about the legacy mix's level, −25.8 dB re direct against −25.6 (with this room's 0.45 s decay, not the 2 s hiss tail). The on-device blind test is No-ER vs Natural vs High (§10.3). | `natural` is the default. `low` is what `-new-subtle` renders. |
| R5 | FDN tail tonal; Hadamard output rows gave cLᵀA = e5ᵀ, a recirculating comb (medium) | fixed | Output vectors: cL = seeded random signs (`mulberry32(514)`), cR = cL ⊙ Sylvester-Hadamard row 1. They are exactly orthogonal, and the maximum projection onto any line is **0.25**, the minimum possible for N = 32: the best nonlinearity of a 5-variable Boolean function is 12, so max |WHT| = 8/32. The critic's seed reached 0.313. **Line lengths doubled** (797…4001 samples, Σ 64 048, 1.33 modes/Hz; sweep `experiments/fdn-v2-sweep.mjs`). The high band now decays with T(4 kHz), fitted at 4 kHz, which removes the +3.6 dB long 4 kHz tail. | Projection 1.0 → 0.25; autocorrelation at the comb lags 0.148 → 0.03/0.04 (L), 0.148 → 0.017 (R); power CV at 0.73 Hz resolution 250 Hz…2 kHz 1.43–1.78 → **1.09–1.18** (ideal noise 1.0; 4 kHz 2.0 → 1.46); narrow-band ringing over Rayleigh mean/max 3.3/6.7 → **1.0/4.5 dB**; modal overlap 2.6–3.9 → **5.1–7.7**; third-octave peakiness of the seat IR −0.32/+0.59 dB (legacy noise IR −0.28/+0.58). |
| R6 | Artefacts: chirp when the layout snaps, material-region steps, FDN DC gain, SetRoom with audio in the ring, denormals | fixed | Delay jumps > 8 samples/block **crossfade over 2 blocks** (old taps at old delays fade out, new fade in) instead of ramping the delays. **Soft material edges**, feathered by the first Fresnel-zone radius per band. DC blockers. `SetRoom` no longer resets the taps (it crossfades), and gains snap only while the FDN is idle. **Idle skip** for ER (4096 silent frames) and FDN (1.5·T_max), which zeroes the state once, so no denormals. | Snap: energy at the v1 chirp frequency −34.9 → **−47.9 dB** re tone, splatter −6.5 → −12.1 dB (steady −15.9) (critic 06; `out/snap-check.json`). Region step 5.9 → **0.93 dB per cm** of head motion. FDN DC gain +2.9 dB → −180 dB. SetRoom 2nd-difference spike +29.6 dB → −8.2 dB (no click). Denormals: none (v1: from 5.8 s). |
| R7 | The critic's level remedy: ER −3 dB uniform and late −3 dB (−2.2 dB "double count" + 0.8 dB head gain) | replaced | A uniform −3 dB on every image ignores that directivity depends on frequency and direction. A blanket −2.2 dB late trim puts the tail under theory everywhere. v2 does it per band and per direction (R1), lets the FDN onset take care of the overlap, and lands within ±1.8 dB of the Q=2 theory for all five sources. | The critic's E trial: piano DRR +3.1, late80 −17.1 dB. v2: +6.1, −16.3 dB, so drier early, and the tail on theory. |
| R8 | Missing reference / spec pieces | fixed | Implemented and test-vectored: idle skip, room-change crossfade, ramped preset changes, presets, the Schroeder high-pass, the 2-block crossfade, per-octave calibration. Spec §6 states where the calibration runs. | `test-vectors.json` items 3b, 3c, 5 (presetSwitch). |
| R9 | Analysis tools | fixed, plus one more found | My `roomParams` dropped the band-filtered direct's pre-onset energy from C50/C80 (anechoic impulse gave C50 −117 dB; critic 08). Fixed: anechoic now gives +262…+285 dB in every band. **Found in revision:** the critic's zero-phase FFT octave filter (`critic/lib.mjs bandFFT`) loses up to half the energy of anything within one filter length of t = 0. An impulse at 1.9 ms loses 1.7 dB in the 125 Hz octave, which biased the 125 Hz fingerprint of v1 and v2 alike (critic 03c v2: piano −0.98 vs −1.21 with pre-roll). My harnesses add a 100 ms pre-roll. | Also found and fixed in my own v2 code before rendering: after `setRoom` the late input EQ ramped from 1 toward the calibrated gains during the first block, so an impulse at sample 0 saw uncalibrated gains. It now snaps when idle. |

What is still open (honest list): the kit's group-centroid ER is a compromise for the 8 drum pieces (critic 03b; floor tom and ride get a centroid reflection stronger than their own geometry would give). The drum set's LTAS is warmer than dry (+3.1 dB at 63 Hz, +2.1 at 125 Hz). That is the kick's room bloom: at 1.34 m it sits beyond the low-frequency critical distance (≈0.73 m). The 63 Hz octave is +1.8 to +2.3 dB over dry for piano and harp, which is roughly what diffuse theory predicts at this seat. If the user hears boom, see §10.3 step 5.

---

## 0. Decision in one paragraph

Use a **hybrid**. Meta XR Audio does what it is best at: the HRTF direct path of all 17 `SynthSource`s. Our own Burst DSP does the room:
- **Early reflections:** per instrument, image-source reflections to order 2. They have per-band source directivity, position-dependent materials with Fresnel-feathered edges, head-tracked ITD/ILD, and a high-pass at the Schroeder frequency.
- **Late tail:** a 32-line feedback delay network, decaying with Eyring RT per band. Its level is calibrated per octave to diffuse-field theory at the ear, with frequency-dependent interaural coherence.
- **Master bus:** a new, gentle one. Trim −3 dB, RMS leveler at 1.6:1, a 7.5 kHz shelf, and a 4× true-peak limiter at −1 dBTP.

Meta's own reflections and reverb are switched off per source (`EnableAcoustics = false`). **Acoustic Ray Tracing is not used** (§2.4):
- It needs editor-baked geometry and maps, and its MonoBehaviours are `internal`.
- It cannot know the MR room at build time.
- Its CPU cost on Quest is unpublished.
- It cannot be auditioned offline.

The current Unity shoebox setup is very live. PlasterOnBrick/Brick/WoodFloor in a bare box gives Eyring T ≈ 2.2–2.4 s at 500 Hz–1 kHz (§1.2), the "over-reverberant" failure the user already rejected once. The new room is a furnished living room: T30 0.60/0.57/0.54/0.49/0.43/0.37 s by octave. At the piano seat, per ear: DRR +6.1 dB, C50 13.0 dB, tail after 80 ms −16.3 dB re direct (the legacy mix: −26.3 dB). It is deterministic, and identical in the editor, on the device and in the approved WAVs. It costs about 0.27 ms per 256-frame block in Node, worst case, with all three instruments and the master; the Quest Burst estimate is 0.07–0.28 ms (§8). It allocates 0.55 MiB and needs no baked data. A **Room amount** setting (Off / No-ER / Low / Natural / High) is the user's escape hatch and the blind-test variable. Two runtime alternatives stay available for A/B on the device: `MetaEr` (Meta shoebox ER with our coefficients plus our late tail) and `Classic` (today's sound).

---

## 1. What we have today and what v2 does (measured, per ear)

Impulse at PianoMid (0.66 m from the head), heard at the seat, facing the piano (`out/analysis.json → ir.pianoMid`; separated direct / early / late parts, octave filters with a 100 ms pre-roll):

| | legacy (old) | v1 | **v2 (natural)** |
|---|---|---|---|
| ER / late energy re direct | – / −25.6 dB (all reverb) | −3.8 / −2.5 dB | **−9.0 / −9.3 dB** |
| late after 80 ms re direct | −26.3 dB | −14.1 dB | **−16.3 dB** (Barron Q=2: −16.0) |
| DRR / C50 / C80 broadband | +25.6 / 26.0 / 26.3 dB | +0.1 / 11.6 / 16.9 dB | **+6.1 / 13.0 / 17.2 dB** |
| DRR per octave 125…4k (dB) | 20.6 21.0 21.7 23.3 25.6 27.9 | – | **3.5 3.5 5.0 5.3 6.2 6.5** |
| T30 per octave 125…4k (s) | 3.45 3.37 3.52 3.61 3.71 4.08 (noise tail) | 0.64 0.57 0.53 0.47 0.41 0.35 | **0.60 0.57 0.54 0.49 0.43 0.37** |
| EDT per octave (s) | (direct dominates) | – | 0.72 0.64 0.58 0.55 0.49 0.40 |
| late IACC 80–600 ms per octave | 0.05 0.06 0.10 0.08 0.07 0.06 | 0.87 0.64 0.39 0.19 0.07 0.08 | **0.92 0.81 0.51 0.27 0.08 0.12** |
| third-octave tail peakiness over Rayleigh, mean / max | −0.28 / +0.58 dB | +0.28 / +2.05 dB | **−0.32 / +0.59 dB** |

Harp seat (0.92 m): DRR +3.4, C50 12.1, late80 −14.9 dB. Snare: DRR +3.5, C50 12.1, late80 −14.8 dB. EDC curves (per-ear energy, 5 ms steps, octave bands and broadband): the lab's `out/edc-old.csv`, `edc-new.csv`. IRs: `room-ir-old.wav`, `room-ir-new.wav` (§11).

The legacy mix (spec 06) is almost anechoic. It has a faint 2 s "hall" tail 26 dB down, and that tail is decorrelated even at 125 Hz, which sounds phasey on headphones. Real diffuse fields are coherent below about 300 Hz. Its master compressor (−14 dB, 4:1, attack 3 ms, release 180 ms, +2.14 dB makeup) lets the drum groove reach **+2.95 dBTP** even with the hi-hat fix (+3.8 without it). The device hard-clips that.

### 1.2 The Classic room as it stands (ARCH §7.2, `AudioEngine.SetRoomBox`)
- **Shoebox:** a Meta shoebox, 6.4 × 2.9 × 5.2 m. PlasterOnBrick on the left, right and front walls, Brick at the back, a GypsumBoard ceiling, WoodFloor, clutter 0.6.
- **Reverb time:** our Eyring calculation of that bare box with the presets' absorption gives **0.94 / 1.78 / 2.24 / 2.42 / 1.88 / 1.29 s** (`experiments/meta-shoebox.mjs`). How Meta derives its late RT from wall coefficients and clutter is UNVERIFIED.
- **Drum room:** the `DrumRoomReverb` (an FDN approximation of the 0.45 s IR) sits at the kit centre.
- **Master:** the legacy compressor (spec 06 curve), without look-ahead.

---

## 2. Meta XR Audio SDK 85: what is there and what it can do for us

Paths are relative to `Library/PackageCache/com.meta.xr.sdk.audio@3292c33a8423/Runtime/scripts/`. (Unchanged from v1.)

### 2.1 Visibility
| Type | Visibility | Use from `MusicRoom.Audio` |
|---|---|---|
| `MetaXRAcousticGeometry` (`MetaXRAcousticGeometry.cs:45`), `MetaXRAcousticMap` (`:38`), `MetaXRAcousticControlZone` (`:39`), `MetaXRAcousticMaterialProperties` (`:200`) | **internal** (`InternalsVisibleTo` only `Meta.XR.Audio.Editor`, `Runtime/AssemblyInfo.cs:21`) | Only by reflection or editor tooling |
| `MetaXRAcousticMaterial` (`:32`) | public class; `properties`/`customData` are private or internal | You can add it, but not set its data |
| `MetaXRAcousticSettings` (`:33`) | public; the `AcousticModel` setter is public (`:50-61`) | yes |
| `MetaXRAcousticNativeInterface.Interface` (`:248`) | **public**: geometry, material, map (SceneIR) and control-zone C API | yes. This is the only supported-from-code route to ray tracing |
| `MetaXRAudioNativeInterface.Interface` | public: `SetAdvancedBoxRoomParameters`, `SetRoomClutterFactor`, `SetSharedReverbWetLevel`, `SetEnabled` (`:140-184`) | yes |

### 2.2 Acoustic models (`MetaXRAcousticNativeInterface.cs:33-38`)
`Automatic = -1`, `None = 0`, `ShoeboxRoom = 1`, `AcousticRayTracing = 3`. The project asset `Assets/Resources/MetaXRAcousticSettings.asset` holds `acousticModel: -1`. **Set it to `ShoeboxRoom` (1)** so that stray geometry can never switch the model.

### 2.3 Ray tracing recipe (a future experiment, not the default)
- **Materials:** `CreateAudioMaterial`, `AudioMaterialReset`, `AudioMaterialSetFrequency(h, ABSORPTION|TRANSMISSION|SCATTERING, f, v)`. Use the octave values of §4.2, with transmission from the Meta presets (GypsumBoard .035 .0125 .0056 .0025 .0013 .0032; WoodFloor .071 .025 .0158 .0056 .0035 .0016; Carpet .004 .0079 .0056 .0016 .0014 .0005; Glass .125 .089 .05 .028 .022 .079; Brick .025 .019 .01 .0045 .0018 .00089).
- **Geometry from code:** `CreateAudioGeometry`, `AudioGeometryUploadMeshArrays(...)`, `AudioGeometrySetObjectFlag(ENABLED)`, `AudioGeometrySetTransform`, then `AcousticModel = AcousticRayTracing`. That is 6 planes plus about 8 boxes, about 120 triangles.
- **Map:** `CreateAudioSceneIR`, `InitializeAudioSceneIRParameters`, `AudioSceneIRCompute` (`MetaXRAcousticMap.cs:984-1025`), `AudioSceneIRWriteFile`/`ReadMemory`. Dynamic objects do not affect the baked reverb.
- **Control zones:** RT60, REVERB_LEVEL and EARLY_REFLECTIONS_LEVEL, as additive offsets per frequency.
- **Headless:** the native calls work in batchmode PlayMode (spec 04 E3). Whether `AudioSceneIRCompute` needs a GPU in batchmode is UNVERIFIED.
- **Test:** a 100 ms noise burst at PianoMid, the listener tap, and the same analysis as `lib/analysis.mjs`, compared with `edc-new.csv`.

### 2.4 Verdict on ray tracing for this app
1. **Mixed reality:** the real room replaces ours. There is no geometry to bake, so MR would fall back to the shoebox.
2. **Our room is a box:** its first- and second-order specular paths are exactly the image sources.
3. **Black box:** it cannot be rendered offline, so the user's A/B approval would not carry over.
4. **API friction:** the components are `internal`.
5. **CPU:** Meta publishes no Quest numbers.

### 2.5 Meta shoebox early reflections as an A/B option (`RoomSoundMode.MetaEr`)
- **Meta's ER on, Meta's late off:** `EnableAcoustics = true` on the 12 instrument sources, and `SetSharedReverbWetLevel(0f)` globally.
- **Our wall coefficients:** disable `MetaXRAudioRoomAcousticProperties`, then call `SetAdvancedBoxRoomParameters(6.4f, 2.9f, 5.2f, false, new Vector3(0, 1.45f, -1.48f), wall)` once, with the wall order right, left, ceiling, floor, front, back and 4 energy reflection coefficients each. The band centres are **inferred** as about 125/354/1800/4000 Hz. Values: `right [.8174,.8927,.9512,.9659], left [.8655,.9034,.9227,.9326], ceiling [.7100,.9251,.9346,.9100], floor [.9075,.8972,.7608,.6823], front(+z?) [.8600,.9201,.9585,.9700], back(−z?) [.8805,.9182,.9396,.9434]`. Whether "front" is Unity +z is UNVERIFIED.
- **Clutter:** `SetRoomClutterFactor(new[]{0.0375f, 0.075f, 0.15f, 0.3f})`.
- **Our ER off:** `RoomEngine.ErEnabled = false`. Calibrate Meta's ER level to ours with the PlayMode test in §10.2: our ER energy for the piano pose is **−9.0 dB** re direct, per ear.

---

## 3. Architecture

```
main thread (every frame): RoomPose {head pos, right vector, piano/harp/drums group points} in room-local coords → RoomEngine.PostPose (seqlock)
main thread (on SetRoom / VR↔MR, worker job): roomAcoustics → fdnT3 → setDecay → calibrateLate (per octave) → publish params (version)
audio thread, per 256-frame block:
  SynthSource ×17 OnAudioFilterRead  → mono s[i] = _mix[i]·bus  ──→ Meta HRTF (direct only; EnableAcoustics = false) → mixer → listener
                                      └→ RoomBus.Add(group, s, frames, roomSend, blockStart)   (12 instrument sources only)
  AudioListener: MasterBusV2.OnAudioFilterRead(data = HRTF direct of everything):
      groups = RoomBus.Take(blockStart)      // piano = PianoLo+Mid+Hi, harp, drums = 8 pieces
      per group: ImageSourceER.Update(pose) → Process (24 taps × 2 ears × 3 bands; 2× taps during a crossfade; idle skip)
      Σ groups × lateTrim·wet (ramped) → LateFDN (50 Hz HP², 3-band input EQ, 4 allpasses, 32 lines, coherence; idle skip)
      MasterBusV2: trim −3 dB → RMS leveler 1.6:1 @ −18 dBFS → high shelf 7.5 kHz +1.5 dB → 4× true-peak limiter (−1 dBTP, 53 samples)
```
- **Retired in `New` mode:** the `DrumRoomReverb` (drums `sendGain = 0`, DrumRoom `busGain = 0`), the legacy compressor, and Meta's reflections and reverb.
- **Kept in every mode:** `NoiseTable`, voices, resonators and ambience beds. Fire, wind, snow, Sunny and the UI have **no room send**.
- **Modes** (`RoomSoundMode`, dev menu): `New` (default) | `MetaEr` (§2.5) | `Classic` (today's sound). The ER taps crossfade over 2 blocks and the late gains ramp per block, so a switch never clicks.
- **User setting "Room amount"** (`RoomEngine.SetAmount`): Off / No-ER (`web`) / Low / Natural (default) / High (§5.6).

---

## 4. Room model

### 4.1 Geometry (Unity coordinates; spec 07 §2–3, spec 05)
Box x ∈ [−3.2, 3.2], y ∈ [0, 2.9], z ∈ [−4.08, 1.12]; V = 96.51 m³, S = 133.84 m². Walls: 0 = x-min (left, bookshelf), 1 = x-max (right, window), 2 = floor, 3 = ceiling, 4 = z-min (fireplace, behind the player), 5 = z-max (behind the piano). In-plane (u, v): x-walls (z, y), y-walls (x, z), z-walls (x, y). Regions are blended in order, so the first has priority (§5.3).

| wall | base | regions (u, v → material) |
|---|---|---|
| 0 left | plasterLath | z [−3.23, −2.13], y [0, 1.9] → books |
| 1 right | plasterLath | z [−2.08, −0.28], y [0.7, 2.4] → glass |
| 2 floor | woodFloor | x [−2.3, 2.3], z [−2.15, 1.05] → rug |
| 3 ceiling | gypsum | – |
| 4 fireplace | plasterLath | x [−0.15, 0.75], y [0, 0.78] → firebox; x [−0.53, 1.13], y [0, 2.9] → brick |
| 5 piano wall | plasterLath | – |

`prepareRegions()` replaces region edges that touch or pass the wall boundary with ±∞, so they are never feathered. For example, the bookshelf stands on the floor and the brick reaches the ceiling (`test-vectors.json → regions`). Areas (m²): plasterLath 57.32, gypsum 33.28, woodFloor 18.56, rug 14.72, brick 4.11, glass 3.06, books 2.09, firebox 0.70.

**Positions** (all mode, head (0, 1.2, 0)):
- **Piano:** PianoLo/Mid/Hi (−0.42/0/0.42, 0.77, 0.50).
- **Harp:** (−0.82, 0.8, 0.128).
- **Drums:** snare (0.5, .72, −.26), tom1 (.74, .86, −.24), tom2 (.62, .85, −.55), floor (.22, .62, −.64), kick (.9, .29, −.38), hat (.56, .8, .16), crash (.78, 1.02, .13), ride (.47, 1.04, −.74).
- **Group points (HRTF points):** piano = PianoMid, harp = Harp, drums = the centroid of the 8 pieces (0.599, 0.775, −0.315).
- **ER origins:** each group point + `ER_CENTRE_DY` in world y: piano 0, harp −0.35 m (the soundbox runs down to the floor), drums −0.2 m (the kick and floor tom sit low).
- **Facing in the renders:** yaw 0° (piano), −81.1° (harp), +117.8° (drums).

### 4.2 Materials (octave bands 125 … 4000 Hz; `MAT` in `room-engine.mjs`; unchanged)
| key | absorption α | scattering s | source |
|---|---|---|---|
| plasterLath | .14 .10 .06 .05 .04 .03 | .10 .11 .12 .13 .14 .15 | classic tabulated plaster on lath; s = Meta GypsumBoard |
| gypsum | .29 .10 .05 .04 .07 .09 | .10 … .15 | Meta `GypsumBoard` (`MetaXRAcousticMaterialProperties.cs:528-538`) |
| woodFloor | .15 .11 .10 .07 .06 .07 | .10 ×5, .15 | Meta `WoodFloor` (`:700-710`) |
| rug | .02 .06 .14 .37 .48 .63 | .10 .15 .20 .25 .35 .50 | Meta `CarpetHeavy` (`:374-384`) |
| glass | .35 .25 .18 .12 .07 .05 | .05 ×6 | Meta `Glass` (`:482-492`) |
| brick | .02 .02 .03 .04 .05 .07 | .20 … .45 | Meta `Brick` (`:326-336`) |
| firebox | .30 .40 .50 .50 .50 .50 | .50 ×6 | estimate |
| books | .10 .15 .25 .30 .30 .30 | .30 … .70 | estimate |
| curtain | .07 .31 .49 .75 .70 .60 | .10 … .50 | Meta `Curtain`, **only if the Room module adds curtains** |

Objects (absorption area in m², per octave):
- loveseat .35 .75 1.25 1.55 1.65 1.65
- bench cushion .02 .04 .08 .12 .14 .14
- seated player .15 .25 .40 .45 .45 .45
- drum kit .30 .25 .15 .10 .10 .10
- piano, harp, tables, lamp and plants .20 .20 .20 .20 .25 .25

All are estimates. Air absorption: ISO 9613-1, 20 °C, 50 % RH, dB/km 0.4 1.3 2.7 4.7 9.9 29.7.

### 4.3 Reverberation time (Eyring with air; `roomAcoustics()`; unchanged)
- **Formula:** T = 0.161·V / (−S·ln(1 − ᾱ) + 4mV).
- **Visible room only:** 0.60 / 0.98 / 1.15 / 0.96 / 0.83 / 0.66 s.
- **Target:** furnished living rooms measure about 0.5 s (Perkin; Kylliäinen et al.; Burgess & Utley 0.33 s at 500 Hz). The model targets **T(1 kHz) = 0.45 s** with a residual absorption `[.25 .45 .75 .95 1 1] × k`, k = 16.04 m², which stands for curtains, cushions and a doorway. **Room module: add window curtains and a cushion or two.**
- **Result:** T = **0.507 / 0.639 / 0.565 / 0.450 / 0.403 / 0.356 s**.
- **FDN band decay `fdnT3(T)`:** [(T125+T250)/2, (T500+T1k)/2, T4k] = **0.5731 / 0.5073 / 0.3558 s**.
- **A′ (Eyring-equivalent area) per octave:** 30.6 / 24.3 / 27.5 / 34.5 / 38.5 / 43.7 m².
- **Schroeder frequency:** f_s = 2000·√(T1k/V) = **136.6 Hz**.

### 4.4 Mixed reality room (`mrRoom(size)`)
- **Box:** the MRUK room bounds, centred on the room anchor and **rotated by the room yaw** (all poses go into room-local coordinates on the main thread).
- **Materials:** plasterLath walls, a gypsum ceiling, a woodFloor floor. Objects: the seated player only.
- **Target T(1 kHz):** clamp(0.30 + 0.0015·V, 0.35, 0.60) s. Example: 4 × 2.5 × 3.5 m gives T3 .3917 .3728 .3303, fdnT3 .3917 .3728 .3236, f_s 200.7 Hz.
- **Wet trim:** **−3 dB** in MR. With no scene, use a default 4 × 2.5 × 4 m box and −6 dB wet.
- **API change (MR module):** `IAudioEngine.SetRoomBox(Vector3 center, Vector3 size, float yawDeg, bool realRoom)`.

---

## 5. Algorithms (implement these exactly; Node names in `room-engine.mjs`)

### 5.1 Constants
| name | value | source / reason |
|---|---|---|
| `SR` | `AudioSettings.outputSampleRate` (48000 assumed; FDN and diffuser lengths scale to the nearest prime) | spec 06 §0 |
| `C_SOUND`, `HEAD_RADIUS` | 343 m/s, 0.0875 m | 20 °C; Woodworth, Brown & Duda 1998 |
| `XO_LO`, `XO_HI` | 300, 2400 Hz | 3 bands ≈ 125+250, 500+1k, 2k+4k |
| `BAND_F` | 200, 1000, 4500 Hz | band frequency for head shadow, air and the Fresnel feather |
| `HRTF_FRONT_DB` | −3 dB | spec 06 HRTF front −5.0 dB/ear (§1.4) |
| `distGain(d)` | 0.6 / (0.6 + 1.1·(max(d, 0.6) − 0.6)) | spec 06 `inverse` distance law = Unity Logarithmic min 0.6, Rolloff 1.1 |
| **`SRC_Q`** | **[1.0, 2.0, 2.5]** (low, mid, high band) | design; trend of instrument directivity: omni in the bass, beaming in the treble (Meyer 2009; Otondo & Rindel 2004). Not a measurement of these instruments |
| **`dirAmp(cosθ, Q)`** | ((1 + cosθ)/2)^((Q − 1)/2) (amplitude); power mean over the sphere = 1/Q | §5.3 |
| **`ER_CENTRE_DY`** | [0, −0.35, −0.20] m | §4.1 |
| `T_TARGET_1K` | 0.45 s | §4.3 |
| ER max order, ring | 2 (24 images), 4096 frames × float4 | Allen & Berkley 1979; the longest order-2 path in a 12 × 3.5 × 14 m MR room is 3989 samples |
| **`JUMP_SAMPLES`, `XF_BLOCKS`** | 8 samples per block; 2 blocks | head motion at 1 m/s is ≤ 1 sample per block; snaps are 50–140 |
| **`DC_HZ`, `schroederHz(T,V)`** | 15 Hz floor; 2000·√(T1k/V) | the ER high-pass |
| **`LATE_HP_HZ`** | 50 Hz, two cascaded 1st-order sections | §R3(e) |
| `IDLE_EPS` | 1e-7 | idle skip |
| `FDN_N` | 32 | |
| **`FDN_LEN_48K`** | 797 839 887 937 983 1039 1093 1153 1213 1283 1361 1423 1487 1571 1657 1753 1831 1933 2039 2137 2251 2371 2503 2633 2797 2917 3083 3253 3413 3607 3803 4001 (primes; Σ 64 048) | `experiments/fdn-v2-sweep.mjs`: best power CV (1.11–1.27, 250 Hz–2 kHz) at a late-only echo density of 0.94–0.97 in 20–60 ms |
| `DIFF_LEN_48K`, `DIFF_G` | 37 53 71 97; 0.5 ×4 | short diffusers (long ones ring) |
| **`FIT_F`** | 177, 707, **4000** Hz | the high band decays with T(4 kHz) |
| `COHERENCE_FC` | 400 Hz | IC(f) = 1/(1 + (f/fc)²), cf. Menzer & Faller 2009 |
| FDN input signs b | ±1/√32 from `mulberry32(7)` (r < 0.5 → −) | seeded |
| **FDN outputs** | cL = ±1/√32 from `mulberry32(514)`; cR = cL ⊙ `hRow(1)` | cL·cR = 0; max projection 0.25 |
| `LATE_EARLY_TRIM_DB`, `LATE_LF_DB` | 0, 0 | kept as knobs; not needed with the long lines |
| **Master** | **trim −3 dB**; leveler threshold −18 dBFS RMS, ratio 1.6, knee 8 dB, RMS τ 30 ms, attack 10 ms, release 250 ms; shelf 7500 Hz +1.5 dB; ceiling −1 dBTP; W = 48; release 10 ms (isolated) … 150 ms (sustained), activity τ 80 ms; safety clamp 0.999 | Giannoulis et al. 2012; ITU-R BS.1770-4 |

### 5.2 Complementary 3-band split (ER rings, FDN absorption, FDN input EQ)
`k(fc) = 1 − exp(−2π fc / SR)` (k300 = 0.03850884, k2400 = 0.2695973, k400 = 0.05101271, k50 = 0.006523; see `test-vectors.json → onePole`).
`a += kLo·(x − a); r = x − a; b += kHi·(r − b); low = a; mid = b; high = r − b`, so low + mid + high = x exactly.

### 5.3 Image-source early reflections (`ImageSourceER`)
**Enumeration** (once): per axis the pairs (m, q) with |m − q| + |m| ≤ 2, products of three axes with 1 ≤ Σ order ≤ 2, sorted by (order, mx, qx, my, qy, mz, qz). That gives 24 images (`images2`). Image coordinate per axis: `I = b0 + σ(s − b0) + 2mL`, with σ = 1 − 2q.

**Update** (audio thread at block start, from the latest pose; also on the main thread at init). The source s = group point + (0, ER_CENTRE_DY[g], 0). Source and head are clamped 5 cm inside the box. The source axis is â = (h − s)/|h − s|. For every image:
1. D = I − h, d = |D|, û = D/d.
2. **Bounces.** For each axis with image cell c = ⌊(I − b0)/L⌋ ≠ 0, the crossed planes are k = 1…c (c > 0) or k = c+1…0 (c < 0), at b0 + kL; wall = 2·axis + (k even ? 0 : 1). The crossing point is P = h + t·D with t = (b0 + kL − h)/D. Fold the other two coordinates into the room (`u = (x − b0) mod 2L; u > L ? 2L − u : u`). **Soft material (v2)**, per band b: feather half-width w_b = max(0.02, √(λ_b·d·t·(1 − t))), the first Fresnel-zone radius with λ_b = c/BAND_F_b. Then walk the wall's rects in order: `wgt = softIn(u, u0, u1, w)·softIn(v, v0, v1, w)`, `f += rem·wgt·R_mat`, `rem *= 1 − wgt`, and finally `f += rem·R_base`, where `softIn(x, a, b, h) = smooth((x − a + h)/2h)·smooth((b + h − x)/2h)` and smooth(t) = t²(3 − 2t) clamped. The material factor is R_mat,b = √((1 − α_b)(1 − s_b)), with α and s the 3-band averages of the octave pairs. Multiply the band factors over all bounces.
3. **Directivity (v2).** The emission direction at the real source is the image's ray mirrored back: e = −σ ⊙ û, so cosθ = −Σ_a σ_a û_a â_a. The band factor is `dirAmp(cosθ, SRC_Q[b])`.
4. **Ears.** cr = clamp(û·right, −1, 1), lat = asin(cr), ITD = (a/c)(lat + sin lat). Delay L = (d/c + ITD/2)·SR, delay R = (d/c − ITD/2)·SR. θR = acos(cr), θL = π − θR.
5. **Gains** per band and ear: `distGain(d) · 10^(−3/20) · R_b · 10^(−airDb_b·d/20) · dirAmp_b · |H_BD(α(θ_e), BAND_F_b)| · erTrim · wet`. Brown–Duda: α(θ) = 1.05 + 0.95·cos(θ/150° · π), |H| = √((1 + α²x²)/(1 + x²)), x = 2πf·a/(2c). Air uses the 250 / 1k / 4k octave values.
6. Write the target tap (stride 8 floats): dL, dR, gL0..2, gR0..2.
7. **Jump detection (v2).** If this is the first update ever, cur = tgt. Else, if no crossfade is running and max |tgt.d − cur.d| over all taps and both ears > `JUMP_SAMPLES`: copy cur → old (count oc), set cur = tgt, set xf = 1. During a crossfade, updates only move tgt (normal ramps).

**Process** (per block; x = the group's mono input):
- **Idle check.** The block peak < 1e-7 adds n to `idle`, else idle = 0. If `!enabled` or idle ≥ 4096: zero the ring and the filter states once, set cur = tgt, and return (skipped).
- **Taps for the block.** In a normal block: wc = cur, inc = (tgt − cur)/n. In crossfade block j (j = 1…XF_BLOCKS, p0 = (j − 1)/XF, p1 = j/XF):
  - taps [0, oc) = old: delays fixed, gains old·(1 − p0) → old·(1 − p1);
  - taps [oc, oc + cnt) = new: delays cur → tgt, gains cur·p0 → tgt·p1.
  - After the last crossfade block, xf = 0.
- **Per sample:** `dc += kHp·(v − dc); v −= dc`, with kHp = k(max(15, f_s)) (**the Schroeder high-pass, v2**). Then the 3-band split, and write float4(low, mid, high, 0) at ring[w]. For each tap: wc += inc, then per ear read with linear interpolation at w − d (`p0 = (w − ⌊d⌋) & mask`, `p1 = p0 − 1`) and accumulate `dot(lerp(ring[p0], ring[p1], frac), g)`. At the block end, cur = tgt exactly.

Example taps, piano at the seat, head facing +z (`erTapsPiano`):

| tap | wall | delay (samples) | gains low, mid, high | reference |
|---|---|---|---|---|
| 1 | piano wall | 250.8238 | .19609 .08445 .04835 | v1: .19609 .18420 .15574. The low band is unchanged (Q = 1); mid and high drop from directivity, since this path leaves the source backward |
| 2 | floor, rug | 284.4261 | .17777 .07120 .02909 | |
| 0 | fireplace wall | 1213.388 | .03660 .02907 .02208 | |
| 4 | left wall | dL 885.29, dR 915.45 | gL .05563 .05988 .06672 | |

The direct gain is distGain(0.6595) = 0.9017 × −3 dB. In the ER impulse (high-pass 136.6 Hz), L at samples 250/251/252 = .01097482 .05317106 .01019879, and energy L .01912014, R .01912739.

### 5.4 Late reverberation (`LateFDN`)
- **Input**, per sample:
  - u = Σ_g x_g·lateTrim_g·wet, linearly ramped per block from the last block's value;
  - × `inGain` (ramped; 1);
  - **two 1st-order high-pass sections at 50 Hz**: `dc += k50·(u − dc); u −= dc; dc2 += k50·(u − dc2); u −= dc2`;
  - **3-band input EQ**: split u with states (eA, eB), then `u = e0·low + e1·mid + e2·high` (gains ramped per block);
  - 4 Schroeder allpasses (`v = u − g·buf[p]; buf[p] = v; u = buf_old + g·v`).
- **Loop**, per sample:
  - y_k = line_k[pos_k]; l0 = Σ cL_k·y_k; r0 = Σ cR_k·y_k;
  - absorption per line: band split with states (a_k, b_k), then `f_k = gL_k·a + gM_k·b + gH_k·(r − b)`;
  - a 32-point FWHT × 1/√32;
  - line_k[pos_k] = f_k + b_k·u; advance pos_k.
- **Coherence:** cm = (l0 + r0)/√2; C += kC·(cm − C); Lp += kC·(l0 − Lp); Rp += kC·(r0 − Rp); out L += C + (l0 − Lp); out R += C + (r0 − Rp).
- **Absorption fit** (`setDecay(fdnT3)`): per line, target dB per pass t_b = −60·len/(SR·T_b). Iterate `x_b += t_b − absorbMagDb(x, FIT_F_b)` 8 times and clamp the gains to ≤ 0.99999. Line 0 = .8249836 .7877874 .6760087; line 31 = .3809301 .2941241 .1013891.
- **Idle skip:** after `idleLimit = 1.5·max(fdnT3)·SR` frames (41 266 here) of input below 1e-7, `clear()` once and return. That is 90 dB of decay, and no denormals.
- **Measured** (FDN alone, octave T30 per ear): 0.59 0.57 0.55 0.49 0.42 0.36 (8 kHz 0.32) s against the room's 0.51 0.64 0.57 0.45 0.40 0.36.

### 5.5 Late calibration (`calibrateLate`, main thread or worker at `SetRoom`; v2: per octave)
For each octave o (125…4000), the target is the energy in [60, 300] ms after emission (mean of L and R) of the late output of a unit impulse through a causal octave filter. The filter is `octaveCausal`: 2× 2nd-order Butterworth HP at f/√2, then 2× LP at f·√2. The target is:

`E_o = distGain(1)² · 10^(−0.3) · |H_front(f_o)|² · ⟨|H|²⟩_diff/front(f_o) · 16π / (Q_o · A′_o) · (e^{−13.8155·0.06/T_o} − e^{−13.8155·0.3/T_o}) · ε_o`

- ε_o is the filter's impulse energy (`octaveCausalImpulseEnergy`).
- Q_o = SRC_Q of the octave's band.
- |H_front|² and ⟨|H|²⟩ are from the Brown–Duda model at f_o (1800-point θ integral, sin-weighted).

Solve: start eq = [1, 1, 1]. Four times: render 0.6 s of a fresh FDN (`setDecay(fdnT3)`, eq) and measure the errors e_o in dB, then e_b *= 10^(−(e_{2b} + e_{2b+1})/2 / 20). Deterministic.

**VR room result:** eq = **2.022859 / 0.8290472 / 0.473164**. Per-octave error before the last update: +1.42 / −1.66 / +0.45 / +0.63 / +0.93 / +0.01 dB. It runs in about 60 ms in Node, so on the Quest run it on a worker (not the audio thread) and publish atomically. If the FDN is idle when the result lands, snap the gains; otherwise ramp them over one block. The VR room's result can be committed as constants and checked by an EditMode test.

### 5.6 RoomEngine wiring and presets
- **`SetRoom(room)`:** acoustics → `fdnT3` → `setDecay` → f_s → `ImageSourceER.setHighpass(f_s)` → calibrate. The ER taps crossfade at their next update (no reset).
- **`SetGroupPosition(g, p)`:** per block, from the pose → `update`.
- **`Process(inputs[3], outL, outR, n)`:** ER per group (idle and skip handled inside), then the late send ramp, then the FDN.
- **`SetAmount(name)`:** sets `erTrim` and `lateTrim` for all groups; erDb −∞ also disables ER processing. Presets (dB re the calibrated level):

| preset | ER dB | late dB | what it is |
|---|---|---|---|
| off | −∞ | −∞ | no room |
| **`web`** (no ER) | −∞ | **−16.5** | close to the legacy mix: late field total −25.8 dB re direct at the piano, against the legacy IR's −25.6 dB (the lab's no-ER preset experiment) |
| low | −3 | −4 | `-new-subtle` in the renders |
| **natural** | 0 | 0 | default, physically calibrated (`-new`) |
| high | +3 | +3 | ≈ the critic's corrected v1 level |

### 5.7 MasterBusV2 (listener)
- **Trim:** (l, r) × trim (**−3 dB**).
- **Leveler:** `ms += kRms·((l² + r²)/2 − ms)`, x = 10·log10(ms + 1e-12), soft-knee static curve (Giannoulis 2012 eq. 4). Branching attack/release smoothing in dB.
- **Shelf:** `biquadCoeffs('highshelf', 7500, 0, 1.5)`, DF-I.
- **True-peak limiter:**
  1. A 16-sample history per channel. The 4 polyphase outputs of the 48-tap Kaiser FIR `LIM_FIR`, plus |x[n−6]| and |x[n−5]|, give iv. tp = max(iv, previous iv).
  2. req = tp > ceil ? ceil/tp : 1.
  3. Sliding minimum over W = 48 (monotonic deque), then a W-sample moving average (double `boxSum`, recomputed every 4096 frames) gives s.
  4. act += kAct·((s < 0.999) − act); kRel = kFast + (kSlow − kFast)·act; g = s < g ? s : g + (s − g)·kRel.
  5. Output = the input delayed D = 53 samples (1.10 ms) × g, then the safety clamp ±0.999 (counted).

The gain never exceeds what the peak requires.

---

## 6. Unity integration (Audio module)

- **`RoomBus`** (static, audio thread only): `float[3][8192]`, `long stamp[3]`, `int frames[3]`. `SynthSource.OnAudioFilterRead` adds `s·roomSend` for the 12 instrument sources (Piano* → 0, Harp → 1, drum kinds → 2). Ordering assumption: the listener filter runs after every source of the same mix. This is UNVERIFIED, so count `RoomBus.Misses` and fall back to a 2-slot ring (one block late on the room path only).
- **`SynthSource`:** new fields `RoomGroup roomGroup = None` and `float roomSend = 1`.
- **`AudioRig.Configure` / `AudioEngine`, in `New` mode:**
  - the 12 instrument `MetaXRAudioSource.EnableAcoustics = false`;
  - drums `sendGain = 0`, DrumRoom `busGain = 0`;
  - `MetaXRAcousticSettings.AcousticModel = ShoeboxRoom`.
  - `SetMixedReality(true)` → `RoomEngine.SetRoom(mrRoom(size), pose)` on a worker, and wet −3 dB.
- **Pose:** in `LateUpdate`, compute the room-local head position, `camera.right` and the three group points (PianoMid world position, the Harp source, the mean of the 8 drum sources). Write them to a seqlock `RoomPose`. The audio thread reads at block start (retry ≤ 2, else keep the last pose) and calls `SetGroupPosition` for each group every block. The tap update is allocation-free with precomputed image tuples and the region table; it is about 6 µs per group in Node.
- **`MasterBusV2`** replaces `MasterBus` on the AudioListener (keep the old class for Classic). Stats out (volatile): `RoomMicrosAvg/Max`, `LimiterMaxGrDb`, `LevelerMaxGrDb`, `SafetyHits`, `RoomBusMisses`, `ErCrossfades`, `ErSkippedBlocks`, `FdnSkippedBlocks`.
- **Settings:** `musicroom.roomAmount` (string preset, default `natural`), shown as a settings row with five steps.
- **Burst:**
  - `RoomEngineState` is an unmanaged struct of `NativeArray`/`UnsafeList` (Allocator.Persistent).
  - Hot loops are `[BurstCompile] static unsafe` functions via `BurstCompiler.CompileFunctionPointer`.
  - float4 for the rings and taps; FDN lanes 32 = 8 × float4; FWHT stages 1–2 in-register, 3–5 across registers.
  - `FloatMode.Default` (the tests need IEEE).
  - `update()` uses pow, acos, asin and sqrt per image (24 × 3 groups per block); `math.*` is fine.
- **Latency alignment:** `RoomEngine.AlignSamples` (from the measured Meta HRTF group delay, §10.2).
- **Level reference:** `RoomEngine.ReferenceDb` (measured Meta frontal per-ear level vs −5.0 dB), so the DRR matches the renders.

## 7. Data layout and memory
| block | bytes |
|---|---|
| ER rings ×3, float4[4096] | 192 KiB |
| ER taps ×3: cur, tgt, old [24·8] + work wc, winc [48·8] | 16 KiB |
| image tuples, region table | < 1 KiB |
| FDN lines float[64 048] + offsets | **250 KiB** (v1 125) |
| FDN filters, gains, b, cL, cR, EQ, diffusers | 2.4 KiB |
| master | 4.3 KiB |
| RoomBus float[3][8192] | 96 KiB |
| **total** | **≈ 0.55 MiB, no baked audio** |

## 8. CPU
Measured in Node 26.7 (V8) on an Apple M5 Pro, 256-frame blocks (`bench.mjs` → `out/bench.json`; independent re-bench `critic/out/07-cpu.json`):

| component | ns/sample | µs/block |
|---|---|---|
| ER, one group (24 taps × 2 ears × 3 bands, per-sample ramps) | 245.6 | 62.9 |
| ER, one group, crossfading (48 taps; at most 2 blocks per snap) | 467.7 | 119.7 |
| ER tap update (24 images, soft materials, directivity), per group per block | – | 6.0 |
| ER order 3 (62 taps), for comparison | 574.5 | 147.1 |
| late FDN (32 lines, 64k, EQ, HP, diffusers, coherence) | 198.5 | 50.8 |
| master (leveler, shelf, TP limiter, stereo) | 88.8 | 22.7 |
| **worst case: 3 groups ER + updates + FDN + master** | **1045** | **267.5** |
| typical, one instrument (other groups idle-skipped) | ≈ 540 | ≈ 140 |

**Node-to-Quest-Burst ratio assumed: 0.25 typical, 1.0 pessimistic.**
- **Why not 0.10:** the burst spec (§10.2) measured ×0.10 for streaming modal kernels. These loops are gather- and scatter-bound (ring reads at 48 random delays; 32 separate delay lines), so they gain less from NEON.
- **Quest estimate:** worst case **67–268 µs per block**, which is 3–13 % of the 2 ms audio-graph budget at CPU level 3 (1.65 GHz).
- **Cross-check:** the critic's operation count (49 µs NEON-bound, 195 µs scalar-bound at 2.4 GHz) scaled to 1.65 GHz gives 71–284 µs.
- **Idle groups:** they cost ≈ 0.
- **Savings levers:** skip the per-sample ramp when |tgt − cur| < 1e-6 (static head, about −35 % ER), or render the ER at 24 kHz for the high band.

## 9. Test vectors (EditMode; `Tests/EditMode/Audio/Room/Reference~/test-vectors.json`, deterministic: identical MD5 over repeated runs)
Tolerances: taps and gains 1e-5 relative; IR samples 1e-4 relative or 1e-7 absolute; energies 1e-3 relative; T 1e-4 s; calibration gains 1e-4 relative.
1. **RNG:** `mulberry32(7)` → 0.01170475, 0.06195826, 0.9769076, 0.6990287.
2. **One-pole k:** 0.03850884 (300), 0.2695973 (2400), 0.05101271 (400), k50 (`onePole.kLateHp50`).
3. **Head:**
   - α(0) = 2, α(90°) = 0.7564339, α(180°) = 0.2814339; |H(90°, 4500)| = 0.7763609; ITD(90°) = 655.8154 µs;
   - diffuse head gain 1.012319 / 1.228517 / 1.749591; frontal |H|² 0.9892843 / 0.8326877 / 0.6027362.
4. **Directivity** `dirAmp` at cos = 1, .5, 0, −.5, −1 for Q = 1/2/2.5: [1,1,1] [1,.8660254,.8059274] [1,.7071068,.5946036] [1,.5,.3535534] [1,0,0]. Causal octave impulse energies `octaveCausalImpulseEnergy`.
5. **Room:**
   - VR room: V 96.512, S 133.84, residual 16.0417; T .5071372 .639145 .5646426 .45 .4031008 .3558051; fdnT3 .5731411 .5073213 .3558051; f_s 136.5669 Hz;
   - MR room 4 × 2.5 × 3.5: f_s 200.713 Hz;
   - region table with ±∞ edges.
6. **Images:** 24 (order 2), 62 (order 3).
7. **ER taps** (`erTapsPiano`, `erTapsDrumsFacingKit` with the −0.2 m origin, `erTapsHarpFacingPiano`). ER impulse (§5.3 example values).
8. **Snap crossfade** (`erSnapCrossfade`): xf state at the updates [0,0,0,0,1,2,0,0,0,0], 1 crossfade, L samples at 1023…2000. **Idle skip** (`erIdleSkip`): the first skip after 16 silent blocks.
9. **FDN** (fresh, eq 1): lengths; cL `++-+--++-+--+--+-----++++-+-+++-`, cR `+----++----+++---+-+--+-+++++-++`, cL·cR = 0, projections 0.25 / 0.25. Impulse samples L[797] = −.001968458, L[834] = −.002933956, R[1000] = .008811961; energy L 20–100 ms .03612166, 100–500 ms .002848256; 26 skipped blocks in 1 s.
10. **RoomEngine:** `lateEq` 2.022859 / 0.8290472 / 0.473164, `lateErrDbOct`, f_s. Piano impulse energies L 0–10 ms .005004867, 10–50 ms .02242503, 50–200 ms .009477033, 200–800 ms 6.925067e-5. **Presets** table. **Preset switch** `natural` → `web` → `natural` under noise: max |out| .1260803, ER skipped for 20 blocks while on the no-ER preset.
11. **Master** (trim −3):
    - latency +53 samples (trim 0, shelf 0);
    - +6 dBFS sine: max |out| .8911793 ≤ ceiling .8912509, 0 safety hits, leveler max GR −6.76 dB, limiter −4.02 dB;
    - −30 dBFS: gain −3.000 dB;
    - spike: −6.33 dB, 832 frames above 1 dB GR.
12. **No allocation:** the `GC.GetAllocatedBytesForCurrentThread()` delta is 0 over 1000 blocks, including crossfade and idle blocks.
13. **Determinism:** bit for bit across runs.

## 10. Verification in the editor (headless) and on the device

### 10.1 PlayMode (batchmode)
- **RoomBus ordering:** 3 s of noise, `RoomBusMisses == 0`.
- **Engine parity:** an impulse into `RoomBus` (Meta direct muted). The room output nulls against item 10 within −60 dB.
- **Mode and preset switch:** cycle New ↔ Classic and all 5 presets 10 times under noise. No sample above 0.999, and no step larger than −40 dB re the signal.
- **Snap:** move the piano rig 1 m in one frame under a 440 Hz tone. `ErCrossfades` +1, and no energy at 600–800 Hz above −45 dB re the tone in the ER output.

### 10.2 Calibrations (PlayMode, then constants committed)
- **Meta HRTF delay:** an impulse at 1 m in front; the onset gives `AlignSamples`.
- **Meta direct level:** white noise at 1 m, per-ear RMS → `ReferenceDb`.
- **MetaEr only:** the listener-tap energy 2–40 ms after the direct for a 100 ms burst at PianoMid, against our −9.0 dB.

### 10.3 On the Quest (development build)
1. **Logcat** `[Room]` once per second: `room us avg/max`, `limiter maxGR`, `leveler maxGR`, `safety`, `roombus misses`, `crossfades`, `mode`, `amount`. Pass: room max < 400 µs, misses 0, safety 0.
2. **Self-test at boot** (dev builds): items 7–11 on the device (`room-selftest PASS`).
3. **Capture:** the dev menu "Capture 10 s" writes `room-capture-<t>.wav` (MasterBusV2 input and output). Compare it with the Node renders.
4. **Blind listening protocol with the user** (critique R4): seated, the three lesson songs. A second person toggles **Room amount No-ER / Natural / High** blind, three rounds each, in random order. Record the preference per instrument. The majority choice becomes the default (`musicroom.roomAmount`). If Natural and High tie, choose Natural.
5. **If the user hears boom on the kit:** first try Low. If only the kit is boomy, add a drums-only late low-band trim (`lateTrim[2]` through a 300 Hz one-pole; the spec owner will provide a value from a render). Do not change the room model.

---

## 11. A/B renders (48 kHz stereo 24-bit, loudness-matched, gain only)
25 files, 56.0 MB, with a listening guide. The `dry`, `new` and `new-subtle` renders are in `Tests/EditMode/Audio/Room/Reference~/renders/`.
- **Loudness matching:** each set is matched to −18 LUFS integrated, or lower when a version's peak-to-loudness ratio would push its true peak above −1 dBTP (drums −22.39, single snare −19.47).
- **Measured:** spread within each set ≤ 0.009 LU (critic 01). Every file is ≤ −1.1 dBTP with 0 clipped samples.
- **Instruments:** the CURRENT sounds from `06-audio-dsp.reference.mjs`, with the 0.3 ms hi-hat onset fade in all drum versions (§R2).
- **Direct path:** the same spherical-head model in every version.

| file | content |
|---|---|
| `room-piano-fur-elise-{dry,old,new,new-subtle}.wav` | lesson `piano-elise`, legacy auto-pedal, 16 s |
| `room-drums-rock-groove-{dry,old,new,new-subtle}.wav` | lesson `drums-rock`, 4 bars + crash, 13 s |
| `room-harp-arpeggio-{dry,old,new,new-subtle}.wav` | lesson `harp-arpeggio`, old harp sound, 12 s |
| `room-single-{piano-C4,snare,harp-G4}-{dry,old,new}.wav` | one note or hit |
| `room-ir-{old,new}.wav` | seat IR of the piano source (peak −1 dBFS) |

What the versions are:
- `dry`: direct only, through MasterBusV2.
- `old`: the legacy main room + drum room (at the world origin, as shipped) + the legacy compressor + shelf.
- `new`: RoomEngine `natural` + MasterBusV2.
- `new-subtle`: preset `low`.

**Nominal loudness before matching** (LUFS, dry / old / new / subtle):
- piano −25.38 / −21.17 / −24.97 / −25.38
- drums −20.79 / −18.34 / −19.27 / −19.99
- harp −36.03 / −32.12 / −34.37 / −35.31

New is 3.8 dB quieter than old on the piano (the master trim −3 dB plus less room energy); the headset volume makes up for it.

**LRA** (dry / old / new / subtle):
- piano 5.69 / 5.53 / 5.36 / 5.02 LU
- harp 5.86 / 6.05 / 5.64 / 5.72 LU
- drums 2.75 / 3.35 / 1.90 / 2.25 LU

The drum LRA drop comes from the room filling the gaps between hits (2.91 → 2.17 LU before the master, `experiments/drum-dynamics.mjs`). The leveler adds −0.25 LU.

**Master activity:**
- piano: leveler ≤ 0.03 dB, limiter 0;
- harp: 0 / 0;
- drums: limiter ≤ 3.8 dB, above 1 dB GR 1.5 % of the time, the same in `dry` (3.9 dB, 1.5 %); leveler ≤ 2.3 dB.

The old chain reaches +2.95 dBTP on the groove.

**Acceptance (critique R3)**, matched LTAS new − dry per octave 63…8k (dB):
- piano +2.3 −0.3 +0.3 −0.3 +0.8 +0.5 +0.2 +0.6
- harp +1.8 −0.6 +0.1 −0.7 +0.7 +0.2 −0.1 −0.1
- drums +3.1 +2.1 +1.2 +0.8 −0.1 −0.4 −1.0 −0.8

Within ±2 dB from 125 Hz to 8 kHz, except the drums at 125 Hz (+2.1). The 63 Hz octave is +1.8…+3.1: see "What is still open" in §R.

Tools (all Node 26, no packages):
- `render-ab.mjs`: renders, `out/analysis.json` and the EDC CSVs;
- `test-vectors.mjs`, `bench.mjs`, `metallic-check.mjs`;
- `lib/{wav,fft,analysis}.mjs`: BS.1770-4, 4× true peak, LRA, octave LTAS, per-ear ISO 3382 parameters;
- `experiments/v2-metrics.mjs`: per-ear DRR/C50/C80, fingerprint, late vs theory per octave, v1 vs v2;
- `experiments/fdn-v2-sweep.mjs`, `fdn-octave-t.mjs`, `lowband-diag.mjs`, `snap-check.mjs`, the no-ER preset check, `drum-dynamics.mjs`, `late-window-debug.mjs`, and the older `fdn-variants`, `late-band-check`, `trim-check` and `meta-shoebox`.

## 12. Notes for other modules
- **Drums:**
  - The current closed hi-hat starts six squares in phase and peaks at +3.9 dBFS for 0.1 ms. Add a 0.3 ms raised-cosine onset ramp, or random start phases, in the new kit; it was previewed in these renders.
  - The current kit (×1.7) still reaches **+5.7 dBTP before the master** on kick and snare peaks. That causes 3.8 dB of limiting at trim −3, in dry and new alike. With the kit 4.4 dB lower there is no limiting at all (critic 05 `kitMinus4_4dB`). The new kit should keep its peaks ≤ −1 dBTP at the ear before the master, or accept ~1–3 dB of transparent limiting.
  - The drum room (`DrumRoomReverb`) is retired in New mode. The drums spec's renders used it; on the device the room engine replaces it.
- **Piano:** the room expects the piano's three buses at key height. If the new piano adds a soundboard radiator lower in the case, tell the room owner (the ER origin offset is one constant).
- **Harp:** the room gives the old harp +1.7 dB (nominal). The level balance belongs to the harp owner.
- **Room (visuals):** add curtains and cushions (§4.3). If curtains are added, put `curtain` on wall 1 around the window; the bisection lowers the residual automatically.
- **MR:** `SetRoomBox` needs the yaw (§4.4). The MR room's calibration runs on a worker at room change, ≈ 60 ms in Node.
- **Settings / menu:** a "Room amount" row with 5 steps (default Natural), stored as `musicroom.roomAmount`.

## 13. Risks and UNVERIFIED items
1. Listener-filter ordering vs source filters within one block (RoomBus); handled by the miss counter and the fallback slot.
2. Meta HRTF latency and frontal level (§10.2). Until they are measured, the ER may land early by an unknown number of samples, and the DRR may be off by the HRTF level error.
3. The Node → Quest Burst ratio (0.25–1.0 assumed for gather-bound loops).
4. The residual absorption is a calibration to published living-room RTs, and the furniture coefficients are estimates. `SRC_Q` is a design value with a literature trend, not a measurement of these synthetic instruments.
5. The kit's ER come from one group centroid (8 sources). The floor tom and ride get centroid reflections stronger than their own geometry would give (critic 03b). Per-piece ER would cost 8× the kit's ER. A kick-only 4th group (+≈25 % ER cost) is the next step if the kit sounds wrong.
6. **The user's taste.** v2 natural is about 16 dB more late room than the legacy mix (late −9.3 vs −25.6 dB re direct) and about 4–6 dB less than v1. The Room amount presets and the blind test (§10.3) decide the default.
7. Meta shoebox band centres for MetaEr are inferred; front/back orientation is unknown.
8. The Quest sample rate is assumed to be 48 kHz. Lengths and one-poles derive from `outputSampleRate`.

## 14. Sources
- J. B. Allen, D. A. Berkley, "Image method for efficiently simulating small-room acoustics", JASA 65(4), 943–950, 1979.
- C. F. Eyring, "Reverberation time in 'dead' rooms", JASA 1, 217–241, 1930.
- M. Barron, L.-J. Lee, "Energy relations in concert auditoriums", JASA 84(2), 618–628, 1988 (revised theory; used as the critic quoted it).
- M. R. Schroeder, "Natural sounding artificial reverberation", JAES 10(3), 1962; M. R. Schroeder, K. H. Kuttruff, "On frequency response curves in rooms", JASA 34, 1962 (f_s = 2000·√(T/V)).
- M. Vorländer, *Auralization*, 2nd ed., Springer 2020 (geometrical acoustics valid above the Schroeder frequency); A. Southern, S. Siltanen, D. T. Murphy, L. Savioja, "Room impulse response synthesis and validation using a hybrid acoustic model", IEEE TASLP 21(9), 2013 (wave/geometric crossover).
- J.-M. Jot, A. Chaigne, "Digital delay networks for designing artificial reverberators", AES 90th Conv., preprint 3030, 1991.
- F. Menzer, C. Faller, "Binaural reverberation using a modified Jot reverberator with frequency-dependent interaural coherence matching", AES 126th Conv., paper 7765, 2009.
- G. Dal Santo et al., arXiv:2402.11216 (FDN input/output gain optimisation for colourless reverberation; as cited by the critic, not re-checked).
- Heldmann et al., DAFx 2020 (low coloration when modes are excited equally; as cited by the critic); A. Lindau, L. Kosanke, S. Weinzierl, "Perceptual evaluation of model- and signal-based predictors of the mixing time in binaural room impulse responses", JAES 60(11), 2012; S. Pelzer, B. Masiero, M. Vorländer 2014 (t_m = 20 V/S + 12 ms = 26 ms here).
- J. Meyer, *Acoustics and the Performance of Music*, 5th ed., Springer 2009 (instrument directivity); F. Otondo, J. H. Rindel, "The influence of the directivity of musical instruments in a room", Acta Acustica united with Acustica 90, 1178–1184, 2004.
- J. S. Abel, P. Huang, "A simple, robust measure of reverberation echo density", AES 121st Conv., 2006.
- C. P. Brown, R. O. Duda, IEEE Trans. Speech Audio Process. 6(5), 1998; R. S. Woodworth, *Experimental Psychology*, 1938.
- D. Giannoulis, M. Massberg, J. D. Reiss, "Digital dynamic range compressor design", JAES 60(6), 2012.
- ITU-R BS.1770-4; EBU Tech 3342 (LRA); ISO 3382-1/-2; ISO 9613-1.
- The nonlinearity bound for 5-variable Boolean functions (max 12, so max |WHT| = 8), e.g. Berlekamp & Welch, "Weight distributions of the cosets of the (32,6) Reed-Muller code", IEEE Trans. IT 18(1), 1972.
- Living-room RT: Perkin et al. (BBC) ≈ 0.5 s; Burgess & Utley (IOA) below 0.5 s; Kylliäinen et al., 207 living rooms, 0.5 s average (summarized in *Applied Sciences* 11(6), 2709, 2021).
- Meta XR Audio SDK 85 source (paths above) and Meta's acoustic ray tracing docs and blog (2024-07-30).
