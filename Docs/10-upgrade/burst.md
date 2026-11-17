# 10-upgrade · burst: the Burst audio-thread architecture for the upgraded synthesis (Quest 3/3S)

**What this is.** This is the performance architecture that lets the upgraded instruments (the piano, drums, harp and room specs in `10-upgrade/*.md`) run live on the Quest audio thread. It covers Burst-compiled float4 kernels, one graph render per block, physical voice ownership with a CPU budget instead of voice stealing, denormal safety, deterministic tests, build settings, the budget, the latency and on-device profiling.

**Not a timbre change.** The A/B renders in section 15 prove that the engine is transparent: today's piano, rendered live by the new kernels, nulls against today's baked buffers to −73…−80 dB.

**Evidence.** Everything below was either measured here (Node 26 and a .NET 6 harness on an Apple M5 Pro) or is marked **UNVERIFIED**, meaning a model or a device-only fact with a stated test.

| Item | Location |
|---|---|
| Lab | the Burst Node lab (section 15); its kernels are vendored at `Tools/SoundLab/burst/lab/` |
| C# skeletons | `csharp/` (compile-checked with Unity's own Roslyn against the project's UnityEngine, UnityEditor, Burst, Mathematics and NUnit assemblies; Unity was not launched) |
| Managed-path check | `harness/run.sh`: **ALL PASS, 23 bit-exact checks** against the Node vectors |
| Renders | `burst-*.wav`, written by the lab's `render-ab.mjs` (section 14) |

---

## 0. Decisions

1. **One Burst call per block.** The first `SynthSource.OnAudioFilterRead` of each mixer block calls `AudioGraphHost.EnsureRendered(blockStart, frames)`. That renders every instrument into per-source mono buffers in one direct Burst call (`AudioGraph.RenderBlock`). Every source callback then only adds its slice (`GraphBridge.AddGraphSlice`).
   - This works because the callbacks are serial on one mixer thread and share `dspTime` per block (spec 04 E4).
   - Unity's 18 `AudioSource`s, the Meta spatializer, the mixer and `MasterBus` stay exactly as they are.
2. **Burst direct calls, raw pointers, one unmanaged arena.**
   - Methods: `[BurstCompile]` static methods called directly. No `FunctionPointer<T>`, no jobs, no `NativeArray` on the audio thread.
   - Memory: all state lives in one 64-byte-aligned `UnsafeUtility.Malloc` block. That is 1.39 MiB for the Quest configuration.
   - Unity's `float[] data` is pinned with `fixed`, which is free under Boehm.
   - Every kernel's function pointer is resolved once on the main thread (warm-up).
3. **Modal banks are the workhorse (MB4).**
   - Each mode is a damped complex resonator, `z ← p·z (+ gi·x)`, output `go·Im z`. Four modes share one float4, and four groups are interleaved per sample step.
   - The kernels use `FloatMode.Fast`, so `math.mad` becomes AArch64 `fmla`.
   - Waveguides (WG4: 4 strings in lanes), biquads (BQ4), noise (NZ4) and gain ramps complete the kit.
4. **Voices follow the physics.**
   - Every key, string, drum, cymbal and soundboard owns one bank for the whole session. A strike adds energy to the same state, as on the real instrument, so keyed instruments never steal.
   - CPU is capped by a global **LOD budget** (default 1200 groups = 4800 modes). The least audible groups fade out over one block when the budget is exceeded.
   - One-shot noises (knocks, thuds, wires) use pools with **quietest-first stealing into a 2.7 ms ghost fade**, replacing the current hard kill of the oldest voice.
5. **Denormals are designed out, not trapped.**
   - Every recursive state below `1e-20` is flushed at block end.
   - A group below −110 dBFS retires with its state zeroed.
   - No FPCR games are needed (measured: no subnormal penalty on the Mac's ARM64 core). Device test T-DENORM decides whether the optional FTZ plugin is ever needed.
6. **Determinism.** The canonical operation order is `math.mad` nesting.
   - Managed (Mono, IL2CPP without Burst, .NET) is **bit-exact** with the Node reference.
   - Burst `Fast` is within 1.5e-6 of it (tests use 1e-5).
   - Kernels compile synchronously in the editor, so a test never mixes managed and Burst results.
7. **Budget.** On the Quest core at CPU level 3 (1.65 GHz, the cap while passthrough is enabled), the worst case is about **0.89 ms of a 5.33 ms block** (0.80 ms without the optional convolution). That covers every instrument at the LOD ceiling plus one-shots, waveguides, overhead, the 18 callbacks, the master bus and an optional 2 s stereo convolution, against the 2 ms target.
8. **Memory.** A live modal piano removes the 67.7 MiB piano sample bank. The graph arena is 1.39 MiB. The whole baked-audio budget (≤ 96 MB) is free for the instrument specs.
9. **Latency.**
   - Keep the DSP buffer at **256**; 128 is an acceptance-tested device option (section 11).
   - The fingertip-to-ear total is dominated by hand tracking (≈ 28–31 ms) and the output queue. The audio path after detection is ≈ 28–33 ms at 256 and ≈ 19–21 ms at 128.

---

## 1. Today's engine and what changes

| Today (`Assets/MusicRoom/Audio`, verified in spec 04 and ARCHITECTURE 7) | Upgrade |
|---|---|
| 18 `SynthSource`s, each a managed `OnAudioFilterRead` mixing pooled class voices (`NoiseVoice`, `ToneVoice`, `MetalVoice`, `BufferVoice`) with double-precision `Biquad` and `Envelope` | Sources unchanged. A per-block Burst graph renders the new instruments. The legacy voices keep working during migration, because `GraphBridge` adds the graph slice to the legacy `_mix`. |
| Piano and harp: 88 + 22 note buffers rendered at boot (79.9 MiB) | Piano and harp as live banks: 0 MiB for the bank. Baked buffers are optional per instrument spec. |
| Body resonators: `ModalResonator`, an 8-mode double biquad bank | Body banks are MB4 banks driven by the instrument mix (driven kernel). |
| Stealing in `SynthSource.Take`: free, else oldest released, else oldest, then `Kill()` with an instant cut (click) | Keyed banks never steal. One-shot pools steal the quietest into a ghost fade (section 5). |
| Per-source stats `Avg/MaxBlockMicros` | `DspLoadMeter` for the whole graph (avg, max, p99, over-budget, dspTime gaps), HUD and logcat |
| Measured cost: 310–327 µs average per block (editor Mono), about 0.65 ms on device by the ×2 rule (UNVERIFIED) | About 0.89 ms worst case with **20–50× more synthesis** (4800 live modes, against today's handful of oscillators plus buffer reads) |

---

## 2. Packages and assemblies

Burst, Collections and Mathematics are installed only as transitive dependencies. The table shows how each one resolves today (`Packages/packages-lock.json`, `Library/PackageCache`).

| Package | Resolved | Cache folder | Pulled in by |
|---|---|---|---|
| `com.unity.burst` | **1.8.31** (LLVM 23 default per its changelog) | `com.unity.burst@bfa84123c21a` | `com.unity.render-pipelines.core` 17.3.0 (asks 1.8.14) and `com.unity.collections` (asks 1.8.29); depth 2 |
| `com.unity.mathematics` | **1.3.3** | `com.unity.mathematics@19a9377c4ffa` | render-pipelines.core (1.3.2), xr.hands (1.2.6), burst, collections; depth 1 |
| `com.unity.collections` | 2.7.1 | `com.unity.collections@88d5bf65ce66` | render-pipelines.core (2.4.3); depth 2 |
| `com.unity.test-framework.performance` | 3.5.0 | | collections (depth 3) |

**Must they be direct dependencies?** They don't have to be: asmdef references resolve because the packages are installed. **Add Burst and Mathematics anyway, at exactly the resolved versions**, so nothing re-resolves or downloads:

```json
"com.unity.burst": "1.8.31",
"com.unity.mathematics": "1.3.3",
```

There are three reasons:
- The audio code then depends on them explicitly. A URP or xr.hands update can no longer silently change the Burst compiler, which changes codegen.
- UPM uses a project-manifest version over transitive requests, so the codegen version is pinned.
- `BurstAudioSetup.Apply` warns when they are missing.

**Collections is not needed.** `NativeArray`, `Allocator` and `UnsafeUtility` live in `UnityEngine.CoreModule`. The runtime compiles without `Unity.Collections.dll` (checked).

**asmdef edits.** There is exactly one asmdef per module (ARCHITECTURE section 2), so there is no new asmdef. The Burst code goes into `Assets/MusicRoom/Audio/Burst/` inside `MusicRoom.Audio`.
- `MusicRoom.Audio.asmdef`: add `"Unity.Burst"` and `"Unity.Mathematics"` to `references`, and set `"allowUnsafeCode": true`.
- `MusicRoom.Tests.EditMode.asmdef` (foundation-owned; the integrator edits it): add `"Unity.Burst"` and `"Unity.Mathematics"`, and set `"allowUnsafeCode": true`.
- PlayMode tests need the same edits only if they touch the kernel types.

---

## 3. Calling Burst from `OnAudioFilterRead`

### 3.1 Options compared (Burst 1.8.31 manual, `Documentation~/csharp-calling-burst-code.md`, `csharp-function-pointers.md`)

| Option | Verdict |
|---|---|
| **Direct call**: a `[BurstCompile]` static method on a `[BurstCompile]` static class, called like normal C#. Burst's IL post-processor turns it into a cached function-pointer call into `lib_burst_generated.so`. | **Chosen.** No boilerplate, no delegate. Arguments are pointers and primitives only (structs only by reference; NativeArray can't be passed). The first call resolves the pointer, so warm up on the main thread (`AudioGraphHost.WarmUp`). |
| `FunctionPointer<T>`: `BurstCompiler.CompileFunctionPointer`, then `.Invoke` | Only if a kernel must be chosen at runtime. From C#, `.Invoke` goes through a delegate, so cache it in a `static readonly`; IL2CPP needs `[MonoPInvokeCallback]` and Cdecl. The manual warns it can be slower than plain C# for small functions. Our one call per block makes this moot. |
| `IJob.Run()` on the audio thread | Possible (synchronous, Burst), but adds job-system plumbing and safety handles for nothing. **Never `Schedule` or `Complete` from the mixer thread**: waiting on worker threads is priority inversion. |
| Unity 6.3 **Scriptable audio generators** (`AudioSource.generator`, `IAudioGenerator`, `GeneratorInstance.IRealtime.Process`, `RealtimeContext`, `ControlContext.Manual.BeginMix/EndMix`; found in this editor's `UnityEngine.AudioModule.xml`) | **Option B, a later spike.** It could remove the 18 managed callbacks and offers manual offline mixes for tests. Its interplay with the Meta XR spatializer and `spatializePostEffects` is UNVERIFIED (spec 04 verified only the filter path), and the API has had deprecations within 6.x. |

**Buffers.**
- `OnAudioFilterRead`'s `float[] data`: use `fixed (float* d = data)`. Unity's GC (Boehm, Mono and IL2CPP) never moves objects, so pinning costs nothing. Never `GCHandle.Alloc` on the audio thread.
- Our own buffers and state: raw pointers into one arena (`GraphArena`). `NativeArray` would add `AtomicSafetyHandle` checks in the editor and can't cross into Burst function pointers. If one is ever used, take `GetUnsafePtr()` once on the main thread.

### 3.2 Frame and block flow

```
main thread (Update): instruments -> AudioGraphHost.Stage(GraphCommand) xN -> Commit()   [one release store per hit]
mixer thread, every 256 frames:
  SynthSource#k.OnAudioFilterRead (first of the block)
     -> GraphBridge.AddGraphSlice -> AudioGraphHost.EnsureRendered(blockStart, frames)
          -> ProfilerMarker "MusicRoom.AudioGraph.Render" -> AudioGraph.RenderBlock (Burst): ring -> pending -> due (sorted),
             per bank: split at events -> MB4 kernels -> AccResolve into Out[source], flush/cull, LOD -> DspLoadMeter.Record
     -> mix += Out[k] (MixRamp), bus gain, MonoToInterleaved
  SynthSource#j (rest of the block) -> only the slice copy
  -> Meta XR spatializer -> mixer (Meta reflections) -> MasterBus (listener)
```

- `blockStart = round(AudioSettings.dspTime · sr)` is identical for all callbacks in a block (spec 04 E4). `RenderBlock` is idempotent per `blockStart` (`LastBlock`, initialised to `long.MinValue`).
- Because all synthesis runs in one callback, per-source `SynthSource.MaxBlockMicros` stop meaning per-instrument cost. Use `DspLoadMeter` (section 13).
- **Commands.** `GraphCommand` is 64 bytes, carried by an unmanaged SPSC ring with 1024 slots. The producer stages and commits; the consumer is Burst (`Volatile.Read` and `Write`, supported by Burst, `csharp-system-support.md`).
  - `When` has the same rules as `SynthCommand`: 0 = ASAP, > 0 absolute `dspTime` (a past time plays now), < 0 relative.
  - **Late guard:** a `Strike` or `Pulse` more than 4800 samples (100 ms) late, for example after the headset was off, is dropped (`DroppedEvents`) instead of firing as a burst.
- **Sample accuracy.** A bank's block is split at each of its events (strike, pulse, damp). Driven excitation is synthesized sample by sample, so onsets are exact, like `Voice.Render` today (spec 04 E9).

Code (compile-checked):
- `csharp/Runtime/Engine/AudioGraph.cs` (graph, Burst)
- `NativeCommandRing.cs`
- `GraphArena.cs`
- `AudioGraphHost.cs` (Unity owner, warm-up, ProfilerMarker)
- `GraphBridge.cs` (the SynthSource hook)

The exact SynthSource change is the commented sketch at the top of `GraphBridge.cs`.

---

## 4. Data layouts (SIMD)

All structs are blittable and built from float4 members; a lane is one mode, string or filter. They are identical to the Node layouts (`lab/kernels.mjs` `MB`, `WG`, `BQ`).

| Struct | Bytes | Members (in order) |
|---|---|---|
| `ModalGroup` (MB4) | 96 | `float4 Zr, Zi` state; `Pr, Pi` pole `r cos w`, `r sin w`; `Go` output gain; `Gi` input gain |
| `WaveguideGroup` (WG4) | 144 | `int4 L` integer delay; `float4 C` allpass coefficient; `X1, Y1` allpass state; `B0, A1` loss; `S1` loss state; `Go`; `int W, Mask, Len, Pad`. The lines live elsewhere: lane l uses `lines[l·Len … (l+1)·Len)`. |
| `Biquad4` (BQ4) | 112 | `float4 B0, B1, B2, NA1 (= −a1), NA2 (= −a2), Z1, Z2` (TDF-II) |
| `Noise4` (NZ4) | 16 | `uint4 State` (xorshift32 13/17/5 per lane, seeded by `SplitMix32`) |
| `Exp4` | 32 | `float4 G, M` |
| `ModalBank` | 88 | `First, Count, Modes, Source, StrikeOffset, ListCount, PulseCount, Silence, long LastStrike, Pulse P0, P1` |
| `GraphCommand` | 64 | `Op, Flags, Pad, Target, double When, float A, B, C, D, int N` |
| `GraphEvent` | 32 | `long At, int Seq, Target, Op, float A, B, int N` |
| Arena (`GraphConfig.QuestDefault(48000)`) | **1,462,144 (1.39 MiB)** | Out 18 × 8192 floats; Acc and AccFade 8192 float4; Exc; 2048 groups plus undamped poles P0r and P0i; Active, Fading and List; Energy and Weight; Strike (4 floats per mode: centre amplitude a, edge amplitude b, velocity exponent e, phase); 160 banks; 512 pending and due events; LOD scratch; ring 1024 × 64 B; head and tail on separate 64-byte lines |

**Loop order (MB4).** Groups are outer and samples inner, with **four groups held in registers** (16 modes per sample step) and the accumulator `acc[n]` (float4) shared.
- Why 4: one group alone is latency-bound, because `zr → fmul (3) → fmla (4) → zi` is a 7-cycle loop-carried chain on Cortex-A78-class cores. Four groups are throughput-bound.
- Measured on the M5 (wasm f32x4 ≈ NEON):

| Interleave | Cycles per mode-sample (FMA) |
|---|---|
| K = 1 | 1.38–1.43 |
| K = 2 | 0.79–0.81 |
| **K = 4** | **0.44–0.46** |

- FMA versus separate mul + add at K = 4: free 0.443 vs 0.544, driven 0.461 vs 0.669.
- Registers: K = 4 uses about 26 of the 32 NEON registers when `−pi·zi` folds into `fmls`.
- A bank's active groups are an ascending index list (`ModalFreeList` and `ModalDrivenList` gather four group pointers), so culled groups cost nothing and the accumulation order stays canonical.

---

## 5. Algorithms (canonical order), constants and voice allocation

### 5.1 Kernels (`csharp/Runtime/Dsp/DspKernels.cs`; reference `lab/kernels.mjs`)

Notation: `mad(a,b,c) = a·b + c`. Managed code rounds twice; Burst `Fast` rounds once. Expressions are evaluated exactly as written.

| Kernel | Per sample n |
|---|---|
| **MB4 free** | `t = mad(pr, zi, pi·zr)`; `zr = mad(pr, zr, (−pi)·zi)`; `zi = t`; `acc[n] = mad(go, zi, acc[n])` (groups added in ascending order) |
| **MB4 driven** | as free, but `zr = mad(pr, zr, mad(−pi, zi, gi·x[n]))`. The impulse response is `go·gi·rⁿ·sin(wn)`, with y[0] = 0 because the output uses the updated state. |
| **AccResolve** | `out[n] += csum(acc[n])` with Unity.Mathematics `csum = (x + y) + (z + w)`, then `acc[n] = 0`. `AccResolveRamp` multiplies by `g0 + (n − from)·dg` (fades). |
| **ModalEndBlock** | `z = select(z, 0, abs(z) < 1e-20)`; `energy = cmax((zr² + zi²)·go²)`. A group retires when `energy < 1e-11` (−110 dBFS) and no pulse is running. |
| **WG4** | `rd = (w − L) & mask`; `x = line_l[rd_l]` (4 gathers); `y = mad(c, x − y1, x1)`; `x1 = x`; `y1 = y`; `s = mad(b0, y, a1·s1)`; `s1 = s`; `v = s + exc4[n]`; `line_l[w] = v_l`; `acc[n] = mad(go, v, acc[n])`; `w = (w + 1) & mask`. States are flushed at block end. |
| **BQ4** | `y = mad(b0, x, z1)`; `z1 = mad(b1, x, mad(na1, y, z2))`; `z2 = mad(b2, x, na2·y)` (float32 state; use MB4 for low-frequency, high-Q resonances) |
| **NZ4** | `x ^= x<<13`; `x ^= x>>17`; `x ^= x<<5`; `o = (float4)asint(x) · 2⁻³¹` |
| **EXP4, MixRamp, MonoToInterleaved, Clear** | `io *= g`, `g *= m`; `dst += src·g`, `g += dg`; mono to every channel |
| **DenormProbe** | 16 dependent `y = y·a + b` per iteration, `FloatMode.Strict` (device test T-DENORM) |

**Main-thread setup** (`DspSetup.cs`; double maths, rounded once):
- `SetMode`: `r = exp(−1/(τ·sr))`, `w = 2πf/sr`, `pr = (float)(r cos w)`, `pi = (float)(r sin w)`.
- `SetPole(mult)` for piecewise envelopes.
- `Strike`: `z += A·e^{jφ}`.
- `SetString` (WG4):
  - loss pole `a1 = 1 − bright`;
  - loop gain `g = 10^(−3/(t60·f0))`;
  - `b0 = (1 − a1)·g/|H(f0)|`;
  - loss phase delay `atan2(a1 sin w, 1 − a1 cos w)/w`;
  - `rest = sr/f0 − lossDelay`, `L = floor(rest − 0.5)`, `d = rest − L ∈ [0.5, 1.5)`;
  - Thiran first-order allpass `c = (1 − d)/(1 + d)`.
- `SetBiquad`: normalized `(b0, b1, b2, a1, a2)` from the cookbook biquad formulas in `Dsp/Biquad.cs`.

### 5.2 Graph render (`AudioGraph.RenderBlock`; reference `lab/graph.mjs`)

1. Return if `blockStart == LastBlock`; clear `Out` for all sources.
2. Drain the ring into Pending (`At` from `When`, late guard). Due = pending with `At < blockStart + frames` (stable), insertion-sorted by `(At, Seq)`. `SetBudget` applies first.
3. Per bank, in ascending id:
   - Skip if it has no active groups, events, fades or pulses.
   - For each of its events in time order: render the segment up to the event, then apply the event.
     - `Strike(vel, pos)`: `A_m = vel^{e_m}·(a_m + (b_m − a_m)·pos)`, `z_m += A_m·e^{jφ_m}`.
     - `Pulse(amp, width)`: a half-sine force by rotation, `x = amp·Im z`, `z *= e^{jπ/W}`, `W = max(2, round(width·sr))`; at most 2 per bank, and the oldest is replaced.
     - `Damp(k)`: every pole `r₀ → r₀^k` from the stored undamped poles.
     - `Undamp`.
     - `Silence`: fade over the next block.
   - Render the last segment.
   - Fading groups render into `AccFade` and get a 1 → 0 ramp over the block, then retire.
   - EndBlock: flush, energy, cull; rebuild the ascending list.
4. **LOD.**
   - If the active non-fading groups exceed `Budget`: bucket each group by `20·log10(sqrt(energy)·weight)` in 2 dB steps from −130 dB (65 buckets).
   - **Protected** groups (bank struck less than 1440 samples = 30 ms ago) are counted first and never dropped.
   - Keep the loudest buckets until the budget is full and fade the rest over the next block.
   - This is O(n) and needs no sort.
   - `weight` is a fixed per-group perceptual factor set by the recipe, such as the voice level or an A-weighting gain.

### 5.3 Constants

| Constant | Value | Source or reason |
|---|---|---|
| Flush threshold | `1e-20f` | FLT_MIN is 1.18e-38. Within one 256-sample block a state ≥ 1e-20 can only fall below FLT_MIN if `|p|²⁵⁶ < 1.2e-18`, that is `r < 0.85` (τ < 6 samples), which no recipe uses. |
| Cull energy | `1e-11` (−110 dBFS amplitude at the bank output) | Below the 24-bit noise floor (−144 dBFS) plus the ≥ 30 dB headroom of the room and listener path, and below audibility at any sane gain. A retired group costs nothing. |
| LOD buckets | 2 dB × 65 from −130 dB | Selection resolution well below the JND of a fading partial |
| LOD protection | 1440 samples (30 ms) | Attacks are where partials matter most |
| Default budget | 1200 groups (4800 modes) | Section 10: 0.59 ms worst case (all driven) at 1.65 GHz |
| Late drop | 4800 samples (100 ms) | Pause or resume safety |
| Steal fade | 128 samples (2.67 ms) linear | Longer than a click (≥ 1–2 ms ramps are click-free for broadband material), short enough that the ghost slot frees quickly |
| Steal protection | 1440 samples (30 ms) | Same as LOD |
| Released bonus | 20 dB | A released voice is already decaying (τ 18–60 ms in today's recipes) |
| Ring and pending | 1024 / 512 | Over 10× the worst burst per frame (a 10-finger chord plus a kit fill is fewer than 60 commands) |

### 5.4 Voice allocation and stealing (`VoiceAllocator.cs`)

**Keyed instruments, no stealing.**

| Instrument | Banks |
|---|---|
| Piano | 88 key banks (strings × partials of that key) plus 3 soundboard banks (one per piano source, driven by that source's mix) |
| Kit | One bank per piece: kick, snare head (plus a wires one-shot), 3 toms, hats (open, closed and choke as `Damp`), crash, ride |
| Harp | 22 string banks (or WG4 lanes) plus a soundbox bank |

- Re-striking a ringing key or drum adds energy to its state: physically right, no click, no allocation.
- Release and damper are `Damp(k)`, for example `k = 30` for a felt damper; re-press is `Undamp`.
- The only limit is the CPU budget (LOD).
- **Adaptive budget (main thread, dev and release builds).** Read `DspLoadMeter` once a second.
  - If p99 > 60 % of the 2 ms target, `SetBudget(Budget·0.9)`.
  - If p99 < 30 % and drops > 0, raise by 5 %.
  - The floor is 400 groups, the ceiling 2048.

**One-shot pools** (N live + G ghost slots):
1. Take a free slot.
2. Otherwise the victim is the lowest `LevelDb − (Released ? 20 : 0)` among slots older than 30 ms (all slots if every one is younger). Ties go to the older, then the lower index.
3. The victim moves to a ghost slot that fades it out over 128 samples. With no free ghost, the most-faded ghost is reused.

Sizes: live 64, ghost 16 for the whole graph (today's pools total 414 class voices, `AudioRig.Configure`).

The skeleton provides the allocator (`VoiceAllocator.Allocate`, `GhostRamp`), the kernels (NZ4 → BQ4 → EXP4 → `MixRamp`) and the tests. Each instrument writes its per-voice render loop inside `RenderBlock`, placed after the banks and mixing into `Out[source]`. Ghosts mix with the `GhostRamp` gain over at most 128 samples.

---

## 6. Denormals on ARM64

- On AArch64, Advanced SIMD and scalar FP follow `FPCR.FZ` (bit 24). Android starts threads with FZ = 0, so subnormals are produced and handled in IEEE mode.
  - ARMv7 NEON always flushed; AArch64 does not.
  - Burst `FloatMode.Fast` doesn't set FZ.
  - Burst `FloatMode.Deterministic` flushes subnormals on every platform (Burst manual, `compilation-burstcompile.md`), but it disables optimizations and uses deterministic maths. Not for kernels.
  - Burst has an x86 MXCSR intrinsic but **no FPCR intrinsic** (checked in `Runtime/Intrinsics`).
- **Measured here** (`lab/bench.mjs`, wasm f32, which keeps IEEE subnormals): on the M5 a dependent `y·0.5 + b` chain held at 2e-40 costs **1.01×** the normal-range time, scalar and f32x4 alike. **No published Cortex-A78C subnormal timing was found** (UNVERIFIED).
- **Policy:**
  1. Block-end flush of every recursive state (MB4, WG4, BQ4, EXP4).
  2. Cull at −110 dBFS, which zeroes a retired group.
  3. Zero a waveguide's lines when its string retires.
  4. Never inject DC or noise "denormal guards": they leak into the output and break null tests.
- **Device test T-DENORM:** `DspKernels.DenormProbe(1<<20, 1e-30f, 0.5f, 1e-30f)` against `(1<<20, 1e-40f, 0.5f, 1e-40f)`, both timed.
  - Below 1.2×: done.
  - Above 1.5×: add the optional IL2CPP source plugin `Assets/Plugins/Android/mr_fpcr.c`, set FZ on entry to `RenderBlock` and restore it on exit, so the Meta spatializer on the same thread keeps IEEE:

```c
#include <stdint.h>
uint64_t mr_fz_on(void)  { uint64_t f; __asm__ volatile("mrs %0, fpcr" : "=r"(f)); __asm__ volatile("msr fpcr, %0" :: "r"(f | (1ull << 24))); return f; }
void     mr_fz_off(uint64_t f) { __asm__ volatile("msr fpcr, %0" :: "r"(f)); }
```

  Bind it with `[DllImport("__Internal")]`; IL2CPP compiles `.c` sources under `Assets/Plugins/Android`. With FZ on, the bit-exact tests are device-specific, so run T-VEC with tolerance.

---

## 7. Determinism and EditMode tests

**Canonical arithmetic.**
- Every kernel expression is a `math.mad` nest. Managed `math.mad(a,b,c)` is `a*b + c` (Unity.Mathematics `math.cs:1554`); RyuJIT, Mono and IL2CPP don't contract it, so managed equals the Node strict reference bit for bit.
- `csum` order is `(x + y) + (z + w)` (`math.cs:6195`).
- Coefficients are computed in double and rounded once, on the main thread.
- No transcendental runs per sample.

**Seeds.**
- `SplitMix32` seeds the noise lanes. Seed 1 gives first outputs `2527132011, 314344336, 2535364964, 2041432039`; seed 12345 gives lane seeds `1200724404, 818072533, 996137225, 2397394836`.
- Recipe randomness stays on the main thread (existing `SeedAll`).

**Editor Burst.**
- Kernels carry `CompileSynchronously = true`, so in the editor the first call is already Burst and a test never sees mixed results. `AudioGraphHost.WarmUp` makes that first call on the main thread.
- With Burst on, tests assert a tolerance of **1e-5 absolute** per sample; the Node-measured fused deviation is ≤ 1.43e-6.
- With Burst off (`!BurstCompiler.IsEnabled`, Jobs > Burst > Enable Compilation), they also assert bit-exact FNV-1a.

**Inputs.** Tests are synthetic: no `dspTime`, threads, `Random` or files. They drive `RenderBlock(g, k·256, 256)` directly.

**Files.**
- `csharp/Tests/EditMode/BurstScenarios.cs`: the scenarios, with no NUnit and no UnityEngine, so the .NET harness runs them too.
- `BurstVectors.g.cs`: expected values, generated by `lab/gen-cs-vectors.mjs`.
- `BurstKernelTests.cs`: NUnit, 8 tests.

Target folder: `Assets/MusicRoom/Tests/EditMode/Audio/`.

**Offline proof.** `harness/run.sh` compiles the runtime against UnityEngine, Burst and Mathematics, the editor script against UnityEditor, and the tests against NUnit. It then runs the managed path on .NET 6 ARM64: **ALL PASS** (23 checks).

---

## 8. IL2CPP ARM64 and Burst AOT build settings

`csharp/Editor/BurstAudioSetup.cs` applies these settings (`MusicRoom.EditorTools.BurstAudioSetup.Apply`; call it from `QuestSetup.ConfigureAll`).

| Setting | Value | Note |
|---|---|---|
| Scripting backend, architecture | IL2CPP, ARM64 only | Already set by `QuestSetup` |
| IL2CPP C++ compiler configuration | Release (Master for store builds) | `ProjectSettings` has Android 1 = Release |
| IL2CPP code generation | `OptimizeSpeed` ("Faster runtime") | |
| Stack trace information | `MethodOnly` | |
| `ProjectSettings/BurstAotSettings_Android.json` | `EnableBurstCompilation true`, `EnableOptimisations true`, `EnableSafetyChecks false`, `EnableDebugInAllBuilds false` (true for symbolized simpleperf runs), `CpuTargetsArm64 2560` = ARMV8A (512) + ARMV8A_HALFFP (2048), with Burst's runtime dispatch picking the variant (`BurstPlatformAotSettings.cs`), `OptimizeFor 1` (Performance), `FloatMode 0` (the kernels opt into Fast per method) | Version 5 format |
| Managed stripping | Unchanged (`Core/link.xml` keeps the `MusicRoom.*` assemblies) | Burst code is static and needs no preservation |
| Development builds | Autoconnect Profiler for the profiling runs only; no Deep Profiling (it distorts the audio thread) | |

---

## 9. Test vectors (expected values)

Source: `results/test-vectors.json`, written by `lab/vectors.mjs` at 48 kHz in 256-frame blocks. The strict FNV-1a value is over the float32 bits.

| Vector | Setup | Strict FNV-1a | First values | Sum / Σx² / peak | Fused max \|Δ\| |
|---|---|---|---|---|---|
| **mb4Impulse** | 1 mode, 1000 Hz, τ = 10 ms, go = gi = 1, x = δ | (16 values exact) | `pr = 0.989381492`, `pi = 0.130254552`; y = `0, 0.130254552, 0.257742882, 0.380299151, 0.495850682, 0.602453113` | | |
| **mb4Free** | 12 modes, `hz = f32(110(m+1)√(1+0.0004(m+1)²))`, `τ = f32(0.8/(1+0.3m))`, `go = f32(1/(m+1))`, struck 0.5·e^{j·0.37m}; 1 block | `0xc707a2a7` | 0.512269795, 0.496798396, 0.481536269, 0.467059165 | 71.28491183 / 29.00317477 / 0.550270200 | 3.6e-7 |
| **mb4Driven** | 20 modes (one 4-group pass + 1 group): `hz = 80 + 61.7m`, `τ = 0.3 + 0.05m`, go 0.2, `gi = 0.05(1 + m%3)`; x = sin(πn/32) for n < 32 | `0x3aec3900` | 0, 0, 0.00335315242, 0.0133335618 | 146.7446257 / 458.4136152 / 4.64196157 | 1.43e-6 |
| **wg4** | 110/220/440/880 Hz, t60 3/2.5/2/1.5 s, bright .5/.6/.7/.8, go .25, Len 1024. Derived L = 434/217/108/53, c = −0.153920352/0.319688052/0.202670813/−0.129104942, b0 = 0.489743978/0.592784286/0.695231438/0.797470868. Pulse `sin(πn/24)(0.5 + 0.1l)`, 2 blocks. | `0xb2dc8e4c` | 0, 0.0848420188, 0.168232381, 0.248744234 | 53.63825703 / 18.37701314 / 0.649999976 | 6e-8 |
| **noise4** | `SplitMix32.SeedNoise(12345)`, 256 × 4 | `0x0835b95f` (exact in every mode: integer) | −0.247943997, 0.739860296, −0.377510071, −0.695817173 | 20.32914318 / 326.4677789 / 0.999074221 | 0 |
| **biquad4** | noise4 through `(0.2, 0.4, 0.2, −0.5, 0.3)`, `(0.05, 0, −0.05, −1.8, 0.9)`, `(0.9, −1.8, 0.9, −1.78, 0.81)`, `(1, 0, 0, 0, 0)` | `0x51fdeed2` | −0.0495887995, 0.0369930156, −0.339759052, −0.695817173 | 21.27020963 / 208.9928309 / 1.20058250 | 1.04e-6 |
| **graph** | 3 banks: (8 modes, 196 Hz, τ .6, amp .4) src 0; (10, 82.4 Hz, 1.2, .3) src 0; (6, 523.25 Hz, 2, .3) src 1. Events: Strike b0 @100 (.8, .25); Pulse b1 @30 (.5, 2 ms); Strike b2 @261 (.6, 0); Damp b2 @589 k 8; SetBudget @768 → 3; Strike b0 @1224 (.5, 1). 6 blocks. | src0/src1 per block: `054ccabb/1f116dc5 6663449a/79121783 3289ad3c/8f35da13 9079ca45/c77c7d66 67ace730/19ee793e d6693d41/e536cad4` | | Stats: lodDropped 3 (bank 1's groups at block 5, once its 30 ms protection ended), culled 0, maxActive 7 | |
| **lod** | amps `[.5, .01, .2, 1e-4, .05, .3, 2e-3 (protected), .08]`, budget 4 | | buckets `61, 45, 58, 25, 51, 59, 38, 54`, so thr = 54 and keepAtThr = 0: keeps .5, .3, .2 plus the protected one | | |
| **steal** | 4 live + 2 ghosts at now = 4000: starts 0/100/2000/3000, levels −20 (released)/−45/−30/−10 dB | | Allocations return `{1, ghost 4}`, then `{0, ghost 5}` | | |

---

## 10. Quest 3S CPU budget

### 10.1 Platform facts

- **SoC.** Snapdragon XR2 Gen 2 (Quest 3 and 3S). Qualcomm describes 2 performance cores and 4 efficiency cores (UploadVR).
  - A Geekbench listing of another XR2 Gen 2 headset reads **six Cortex-A78C** cores: 2 at 2.36 GHz and 4 at 2.05 GHz (GSMArena).
  - Wikipedia's Quest 3 and 3S pages list A715 + A510, uncited.
  - The core type is therefore UNVERIFIED. The model below uses A78C-class pipes (2 × 128-bit FP pipes), which also holds for A715.
- **Apps get 3 CPU cores** (Meta, "Quest boost" / CPU-GPU levels pages).
- **CPU levels** (Meta "CPU and GPU levels", Quest 3 and 3S): 0 0.69, 1 1.09, 2 1.38, **3 1.65**, **4 1.92**, 5 2.05, 6 2.21, 8 2.36 GHz.
  - The default is `SustainedHigh`, which is level 4.
  - **Level 4 requires that the app does not enable passthrough features.**
  - This app initialises passthrough at start (ARCHITECTURE 0), so **budget at level 3, 1.65 GHz.** Level 4 or higher would need passthrough off in VR, a project-wide trade-off (main and render threads too) for the Rig owner.
- **A78 pipes** (Arm *Cortex-A78 Core Software Optimization Guide*, Issue 3.0, PJDOC-466751330-9691): ASIMD FADD latency 2, throughput 2; FMUL 3 / 2; FMLA 4 (2 with accumulator late-forwarding) / 2, on the V pipes. There are 32 × 128-bit registers.

### 10.2 Measured, modelled and the Node-to-Quest ratio

Machine: M5 Pro P-core at 4.574 GHz, from a dependent `i32.add` chain.

| Kernel | Node js-f32 (reference) | M5 wasm f32x4 | M5 cycles | A78C model (cycles) | Quest @1.65 GHz | Quest @1.92 GHz |
|---|---|---|---|---|---|---|
| MB4 free, K = 4, fused | 3.89–4.01 ns/mode-sample | 0.097 ns | 0.443 | 0.66 (20 V-ops / 2 pipes per 16 modes, plus 5 % loop) | **0.40 ns** | 0.34 ns |
| MB4 driven, K = 4, fused | 4.23–4.83 | 0.101 | 0.461 | 0.79 | **0.48 ns** | 0.41 ns |
| MB4 free, strict (no fmla) | | 0.119 (mul) | 0.544 | 1.05 | 0.64 ns | |
| WG4 | 6.64 ns/string-sample | 1.48 (scalar wasm, header in memory) | 6.78 | ≈ 2.5 (4 gathers + 4 scatters + 6 V-ops per 4 strings) | 1.5 ns | |
| BQ4 | 7.71 ns/filter-sample | | | 2.0 (latency-bound; 1.25 with two BQ4 interleaved) | 1.2 ns | |
| NZ4 | 1.26 ns/lane-sample | | | 3.0 | 1.8 ns | |

**Assumed ratios (Quest Burst time = measured time × ratio, at CPU level 3):**
- **wasm f32x4 on the M5 × 4.1** (free), × 4.75 (driven): clock 4.574/1.65 = 2.77 × pipe ratio ≈ 1.5.
- **Node js-f32 reference on the M5 × 0.10.** Quest Burst runs about 10× faster than the bit-exact Node reference.

Other agents can convert their Node ns/sample with these ratios.

The managed .NET 6 path (no Burst, no SIMD) costs 2.8 ns/mode-sample on the M5, which is why Burst is required.

**Partials per ms** (1 ms of Quest core time per 5.33 ms block):

| Case | Modes |
|---|---|
| Free-decaying, level 3 | **9,765** |
| Free-decaying, level 4 | 11,363 |
| Driven, level 3 | 8,158 |
| Strict (no FMA), level 3 | 6,138, so `FloatMode.Fast` is worth 1.6× |
| Real-time ceiling, one core, level 3 | about 52,000 modes |

### 10.3 Worst case (`lab/budget.mjs`, CPU level 3, per 256-frame block = 5333 µs)

| Item | µs |
|---|---|
| Modal groups at the LOD ceiling (1200 groups = 4800 modes), all driven | 588 |
| One-shot voices, 64 + 16 ghosts | 99 |
| 22 waveguide strings (6 WG4) | 9 |
| Graph overhead (160 banks, clears, ring, LOD) | 64 |
| 18 `OnAudioFilterRead` callbacks (UNVERIFIED ~2 µs each) | 36 |
| Master bus in Burst | 6 |
| Optional uniformly partitioned convolution, stereo 2 s IR at B = 256 (zero added latency when the partition equals the block) | 83 |
| **Total** | **≈ 886 (761 at level 4) vs 2000 target** |

- Margin: 2.25×. A slower core, for example an A510-class little core, would roughly triple this; the adaptive budget (section 5.4) then lowers the ceiling automatically.
- The Meta spatializer and reflections run on the same thread and are not ours (measure "Meta XR Audio" in the Profiler).

**A realistic upgraded scene sits below the ceiling:**

| Instrument | Groups |
|---|---|
| Piano: 24 sounding keys × 18 groups (3 strings × 24 partials) | 432 |
| Soundboards | 24 |
| Kit (kick 6, snare 8, toms 18, hats 16, crash 40, ride 40) | 128 |
| Harp (22 × 5) | 110 |
| **Total** | **≈ 694 groups (2,776 modes), ≈ 0.34 ms of modal work** |

**Memory.**
- Arena: 1.39 MiB.
- A live piano removes 67.7 MiB of sample bank; what remains baked today is harp 12.2 + voice lines ≈ 6 + metal sums 1.5 MiB.
- That leaves ≥ 76 MB of the 96 MB baked-audio budget free for the instrument specs.

---

## 11. DSP buffer: 128 or 256

**Ship 256** (verified path, spec 04 E5: 256 × 4 in the editor) and make 128 a device experiment (`AudioModule.PreferredDspBuffer`).

| | 256 | 128 |
|---|---|---|
| Block period | 5.33 ms | 2.67 ms |
| Wait from post to next block | 0–5.33 (mean 2.67) ms | 0–2.67 (mean 1.33) ms |
| Unity output queue (numBuffers × block; 4 in the editor, Quest UNVERIFIED) | ≤ 21.3 ms | ≤ 10.7 ms |
| Our worst case as % of period (level 3) | 17 % | ≈ 36 % (fixed per-block costs double per second; the per-sample work stays the same) |
| Fit with Meta's recommended AAudio burst of 192 frames/channel (Meta Horizon OS platform audio) | LCM 768 (16 ms pattern) | LCM 384 (8 ms pattern), better aligned |
| Risks | | Unity "Best latency" crackle reports on some Android phones (Unity issue tracker); Meta spatializer per-callback overhead doubles; underruns under main-thread GC or thermal throttling |

**Keep 128 only if all of these hold:**
1. Zero underruns in `dumpsys media.audio_flinger` and zero `DspLoadMeter` gaps over a 10-minute stress run: worst-case polyphony, passthrough on (level 3), Sunny talking, MR room scan.
2. Worst-case block time including Meta (Profiler) under 50 % of the period, that is ≤ 1.33 ms.
3. No audible crackle on a blind listen. The gain is about 6–12 ms.

---

## 12. Android output path and hand-to-sound latency

**Output path.**
- Unity 2023.1 and later layer Unity Audio over **AAudio** on Android 8.1+ (Unity issue tracker release note), so Unity 6.3 uses AAudio on Quest.
- Horizon OS renders at 48 kHz with a default buffer of 192 frames per channel and recommends `AAUDIO_PERFORMANCE_MODE_LOW_LATENCY` (Meta platform-audio page). That is about 4 ms per burst, so about 8 ms for a double-buffered low-latency stream.
- Unity's AAudio stream mode (exclusive or MMAP vs shared) and its buffer count are UNVERIFIED.
- Keep the project at 48 kHz (already `m_OutputSamplingRate 48000`): a 44.1 kHz project would be resampled and add latency (Meta).

**Hand to ear** (VR, 72 Hz, 256-frame blocks):

| Stage | Range | Typical | Source |
|---|---|---|---|
| Hand tracking: real motion to pose available | 14–42 ms | ≈ 28–31 | Quest Pro robot study: 27.8 ± 13.7 ms during ongoing motion (177 ms from rest), arXiv 2406.04491. Road to VR, Quest 3 v63: virtual hand 31.3 ms behind passthrough (70 ms photon-to-photon). |
| Contact detected in `Update` (frame quantization) | 0–13.9 | 6.9 | 72 Hz frame; at 90 Hz 0–11.1 |
| Post to next mixer block | 0–5.33 | 2.7 | Spec 04 E10 |
| Unity output queue | ~16–21 | 18 | numBuffers 4 × 5.33 in the editor (E5), Quest UNVERIFIED |
| AAudio and HAL plus DAC | ~6–10 | 8 | 2 × 192-frame bursts plus codec (UNVERIFIED) |
| Speaker to ear | ~0.1 | 0.1 | Integrated speakers a few cm from the ear. Bluetooth headphones add ~100+ ms. |
| **Audio path after detection** | | **≈ 28–33 ms** (≈ 19–21 at 128) | |
| **Fingertip to ear** | | **≈ 63–71 ms** (≈ 54–59 at 128) | |

**Levers, in order:**
1. 90 Hz: −1.4 ms mean.
2. DSP buffer 128: −6 to −12 ms.
3. Predictive strike: when the fingertip trajectory crosses the surface within the next frame, post `When = predicted crossing` one frame early. Sample-accurate `When` makes this exact. It saves 6.9 ms mean and 13.9 ms at most, at a false-trigger risk the instrument specs must judge.
4. Hand-tracking high-frequency or Fast Motion Mode (Meta Hands 2.2: up to 40 % or 75 % lower tracking latency).

Burst itself doesn't shorten the chain; it buys the CPU headroom that makes 128 viable.

Note that the virtual hand on screen is about as late as the audio (70 ms photon-to-photon), so sound and the seen contact stay in sync.

---

## 13. On-device profiling plan and acceptance

1. **Instrumentation** (ships in dev builds, near-zero cost in release):
   - `ProfilerMarker(ProfilerCategory.Audio, "MusicRoom.AudioGraph.Render")` around `RenderBlock`. Begin and End are `[Conditional("ENABLE_PROFILER")]`, so they vanish in release.
   - Whether the FMOD mixer thread shows as a Profiler timeline thread is UNVERIFIED; the Audio module's DSP view showed "SynthSource" nodes in spec 04.
   - `DspLoadMeter`: Stopwatch per block, then avg, max and p99 (50 µs histogram), over-budget count, and dspTime gaps (a block that doesn't follow the previous one). Published every 64 blocks through `Volatile`.
   - `DspLoadHud` (dev, world-space TextMesh 0.6 m ahead) and a logcat line once a second:
     `[Audio] dsp avg 412us max 1320us p99 650us block 5333us load 7.7%/24.8% over 0 gaps 0 groups 214/1200 lod 0 drop 0 sr 48000 buf 256x4`
2. **adb:**
   - `adb logcat -s Unity:V | grep '\[Audio\]'`: dsp lines, the start-up line (sr, dsp frames × buffers, spatializer) and latency-probe lines.
   - `adb shell dumpsys media.audio_flinger` before and after a stress run: the app's output thread, frames, latency, **underruns**. For MMAP streams, use `adb shell dumpsys media.aaudio`.
   - `adb shell top -H -p $(pidof <package>)` and `adb shell ps -T -o TID,NAME,PSR,PRI,NI,POL -p <pid>`: the FMOD mixer thread's name, core (PSR), priority and policy (FIFO or RR?), next to UnityMain and UnityGfx.
   - `adb shell simpleperf record -p <pid> -t <mixer tid> -g --duration 20`, then `simpleperf report`: hotspots inside `lib_burst_generated.so`. Use Burst Force Debug Information for symbols in that build only.
   - Meta's OVR Metrics Tool overlay to confirm CPU level 3 or 4 and per-core load while passthrough is on or off.
3. **Device tests** (a dev-menu or test-player entry; `-testPlatform Android` is UNVERIFIED, ARCHITECTURE 19.23):

| Test | Procedure | Pass |
|---|---|---|
| **T-DENORM** | Section 6 | ratio < 1.2 |
| **T-KERNEL** | Time `ModalFree`/`ModalDriven` on 64 groups × 400 blocks, and WG4 and BQ4 | MB4 free ≤ 0.6 ns/mode-sample at the measured CPU level. If it is > 0.8, re-check that Burst compiled (no managed fallback) and that `FloatMode.Fast` produced `fmla`, using the Burst Inspector on an Android target. |
| **T-VEC** | The section 9 vectors, run on device with Burst on | within 1e-5 |
| **T-STRESS** | 60 s: 88-key glissando with every key ringing, kit rolls with crash every 8th, harp glissandi, beds on, passthrough on | p99 ≤ 1.2 ms, max ≤ 2 ms, 0 gaps, 0 underruns, LOD drops audible as nothing (A/B by ear against a budget of 2048) |
| **T-LAT** | `AudioEngine.LatencyProbe` lines plus a 240 fps phone video of a fingertip tapping a drum pad, audio from the headset speaker, contact frame to audio onset | Report the split against section 12 |
| **T-128** | Section 11 criteria | as listed there |
| **T-PAUSE** | Headset off for 5 s, then on | No late burst (`DroppedEvents` counts) |

---

## 14. A/B renders (`burst-*.wav`, Node lab)

All renders are 48 kHz, 24-bit, stereo (dual mono; the app spatializes). Each pair is loudness-matched with ITU-R BS.1770-4: integrated −18 LUFS, with the common target lowered only when a true peak would pass −1 dBTP. Total size is 22.7 MB.
- **"old"** is today's piano: 06 `renderPianoKey` buffers through the PianoSynth play stage (knock, lowpass, level, board, release, thud).
- **"new"** is the same recipe synthesized **live by the MB4 kernel**. Each partial-string is one mode whose pole radius follows the recipe's 3-segment envelope; it switches at sample n1+1 and n2+1, and holds from n3.

| Files | Content | Result |
|---|---|---|
| `burst-ode-old.wav` / `-new.wav` | Ode to Joy (lessons.json `piano-ode`), 11.4 s | residual **−79.8 dB**, both −18.0 LUFS, −4.8 dBTP: identical by design |
| `burst-note-A0/C4/C7-old.wav` / `-new.wav` | Single notes held 2.5 s, then released | residual −73.8 / −72.9 / −76.3 dB. Float32 pole rounding shifts frequencies by about 0.0004 Hz, an inaudible slow phase drift; within the first 120 ms the difference is far lower. |
| `burst-elise-old.wav` / `-new.wav` | Für Elise opening, legato ×1.6; "new" with a **stress** LOD budget of 12 groups while 18 are needed | 168 group fades; residual −39.7 dB against unlimited. This shows what the CPU limiter costs when it is forced well below the load. At the shipping budget (1200) it never engages for a piano alone. |

How to listen: each pair should sound **the same**. That is the point. The Burst engine and the CPU limiter must be invisible, so the upgraded timbres from the instrument specs are heard exactly as designed.

---

## 15. Files

**Lab:** the Burst Node lab, Node 26, no packages. `common.mjs` and `kernels.mjs` are vendored at `Tools/SoundLab/burst/lab/`.

| File | Contents |
|---|---|
| `common.mjs` | RNGs, WAV 24-bit, BS.1770-4 loudness, true peak, FNV |
| `kernels.mjs` | Canonical float32 kernels: MB4, WG4, BQ4, NZ4, EXP4, LOD |
| `graph.mjs` | Graph render reference |
| `wasm.mjs` | Hand-written wasm assembler and SIMD kernels |
| `bench.mjs` | Writes `results/bench.json` |
| `vectors.mjs` | Writes `results/test-vectors.json` |
| `gen-cs-vectors.mjs` | Writes `csharp/Tests/EditMode/BurstVectors.g.cs` |
| `piano-live.mjs` | Live modal piano vs buffers |
| `render-ab.mjs` | Renders and `results/renders.json` |
| `budget.mjs` | Writes `results/budget.json` |

**C#:** the lab's `csharp/` skeletons, which map to `Assets/MusicRoom/...`.

| Source | Target |
|---|---|
| `Runtime/Dsp/BurstDspTypes.cs`, `DspKernels.cs`, `DspSetup.cs` | `Audio/Burst/` |
| `Runtime/Engine/AudioGraph.cs`, `NativeCommandRing.cs`, `GraphArena.cs`, `VoiceAllocator.cs`, `AudioGraphHost.cs`, `GraphBridge.cs` | `Audio/Burst/` |
| `Runtime/Dev/DspLoadMeter.cs`, `DspLoadHud.cs` | `Audio/Burst/` |
| `Editor/BurstAudioSetup.cs` | `Editor/` |
| `Tests/EditMode/BurstScenarios.cs`, `BurstVectors.g.cs`, `BurstKernelTests.cs` | `Tests/EditMode/Audio/` |

**Harness:** `harness/run.sh` and `Program.cs` compile-check everything and run the managed bit-exactness checks; output goes to `harness/out/`.

---

## 16. Integration order (for the implementing agent)

1. Manifest: add Burst 1.8.31 and Mathematics 1.3.3. Make the two asmdef edits (section 2). Copy the `csharp/` files to their targets. Run `compile`.
2. Run `BurstAudioSetup.Apply` (or add it to `configure`), then `test-edit`: the 8 Burst tests must be green.
3. `AudioEngine.Init`:
   - create `AudioGraphHost(GraphConfig.QuestDefault(sr))`;
   - set `GraphBridge.Host`;
   - add the `GraphBridge.AddGraphSlice` line to `SynthSource.OnAudioFilterRead`, after the legacy voices and before the bus loop;
   - in dev device builds, add `DspLoadHud` under the head;
   - dispose the host in `AudioModule.Shutdown`.
4. Map the instrument specs onto banks, pulses and dampers: one bank per key, string or piece; recipe tables into `AddBank`; strikes as `GraphCommand` batches. The legacy recipes stay until each instrument's A/B is approved.
5. Run `test-perf`, adding a Burst variant of `AudioLoadTests` that logs `DspLoadMeter` for the E12 groove. Then a device build and the section 13 tests.
6. Once all instruments run live, delete the baked piano bank (−67.7 MiB) and size the one-shot pools down.

---

## 17. Risks and UNVERIFIED

- **Core type and the audio thread's core.** These are UNVERIFIED. The XR2 Gen 2 is reported both as 6 × A78C and as A715 + A510; if the mixer thread runs on an efficiency core, costs are about 3× higher. Mitigation: the adaptive LOD budget, plus T-KERNEL and `ps -T` on device.
- **CPU level 3 cap.** This follows from passthrough being enabled at start (Meta's level table). It is a project-wide fact, worth raising with the Rig owner: passthrough initialised only on entering MR would unlock level 4 (+16 %).
- **Burst behaviour on the FMOD mixer thread under IL2CPP.** Expected to work (the code is native with no runtime calls), but UNVERIFIED on device. The first-call warm-up must run on the main thread.
- **Not measured on device.** Unity's Android AAudio mode and buffer count; the actual output latency; whether 128 is stable; Meta spatializer cost per source at 128 vs 256. Hand-tracking latency figures come from third-party measurements (Quest Pro robot study; Road to VR on Quest 3).
- **The A78C cycle model.** Derived from the A78 optimisation guide and calibrated against the M5's measured latency-bound K = 1 and K = 4 results. It is not a device measurement. T-KERNEL settles it.
- **Option B (Unity 6.3 scriptable generators).** Not evaluated on device. Keep `OnAudioFilterRead` until a spike proves spatializer compatibility.
- **Determinism of transcendental functions.** Setup-time `Math.Exp`, `Cos` and `Pow` come from the platform libm: V8, Apple libm, bionic. They can differ in the last double ulp, and after float32 rounding they almost never differ. Bit-exact tests run on the Mac (Mono or .NET, Apple libm); device runs use the tolerance.

## 18. Sources

- Meta: [CPU and GPU levels](https://developers.meta.com/horizon/documentation/native/android/os-cpu-gpu-levels/), [Quest boost](https://developers.meta.com/horizon/documentation/unity/po-quest-boost/), [Horizon OS platform audio](https://developers.meta.com/horizon/documentation/unreal/platform-audio/)
- XR2 Gen 2: [UploadVR](https://www.uploadvr.com/snapdragon-xr2-gen-2/), [GSMArena (Geekbench, six Cortex-A78C)](https://m.gsmarena.com/samsungs_project_moohan_xr_headset_appears_on_geekbench_with_snapdragon_xr2_gen_2_chip-amp-67975.php), [Wikipedia Quest 3S](https://en.wikipedia.org/wiki/Meta_Quest_3S)
- Arm: *Cortex-A78 Core Software Optimization Guide*, Issue 3.0 (PJDOC-466751330-9691), via [documentation-service.arm.com](https://documentation-service.arm.com/static/5f15976320b7cf4bc5246c21)
- Hand tracking: [Road to VR, Quest 3 hand-tracking latency](https://www.roadtovr.com/apple-vision-pro-meta-quest-3-hand-tracking-latency-comparison/); [arXiv 2406.04491 (Quest Pro robot latency)](https://arxiv.org/html/2406.04491v1); [Road to VR, Hands 2.2](https://roadtovr.com/meta-quest-hand-tracking-responsive-controllers/)
- Unity: [AAudio since 2023.1 (issue tracker)](https://issuetracker.unity.com/issues/316/when-dsp-buffer-size-is-set-to-best-latency-the-audio-plays-significantly-slower-and-is-crackling-on-oneplus-7-pro); [`AudioConfiguration.dspBufferSize`](https://docs.unity.cn/6000.1/Documentation/ScriptReference/AudioConfiguration-dspBufferSize.html)
- Local: Burst 1.8.31 `Documentation~` (direct calls, function pointers, FloatMode, AOT settings, System support) and source; Unity.Mathematics 1.3.3 `math.cs`; Unity 6000.3.27f1 `UnityEngine.AudioModule.xml`
- Loudness: ITU-R BS.1770-4 (K-weighting coefficients at 48 kHz, gating)
