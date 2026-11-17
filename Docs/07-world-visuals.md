# 07 - World visuals: room, decor, VFX, lighting, layout, MR (Unity spec)

**[measured]** = measured on the running reference room model. Unity refs are under `Library/PackageCache/<pkg>/`. Unchecked claims are marked **UNVERIFIED**.

Room data: the data sets named below (`meshes[]` (all 138 room meshes, with source-frame **and** Unity transforms and bboxes), `lights`, `ambient`, textures, `snow`, `plants`, blobs, `switchPanel`/`tapeDeck`, layout poses, footprints) were generated from the reference room model; their values are compiled into the Room, Layout and MR modules.

## 0. Conventions and three findings to act on

**Coordinates.** The reference model is authored in a right-handed source frame (-z forward). Source (x,y,z) -> Unity (x,y,-z). Source YXZ Euler (α,β,γ) -> `Quaternion.Euler(-α,-β,γ)` (Unity applies z, x, y = the same YXZ product). Quaternion (x,y,z,w) -> (-x,-y,z,w). Yaw θ -> -θ. A source plane ↔ Unity Quad (normal -z), a source box ↔ Cube: same transform rule, no mesh change. Procedural meshes built vertex by vertex: negate z and normal.z **and reverse triangle winding** (a,b,c)->(a,c,b). Simplest and least error-prone: compute every procedural layout (plants, books, candles, mound) in source coordinates with the formulas below, then convert once at the end. Painted-texture row 0 is the texture top: write painted row y to `Texture2D` row `h-1-y`.

In Unity the player faces **+z**; the room is x in [-3.2,3.2], z in [-4.08,1.12], y in [0,2.9]. Back wall (paintings, behind the piano) at z=+1.12, window on the right wall x=+3.2, fireplace behind the player on z=-4.08.

**Defects in the reference room model (decide, do not copy blindly):**
1. **Floor is rotated 90°** (a W x D plane turned π/2 about its normal swaps extents). Measured floor bbox x in [-2.6,2.6], z in [-1.72,4.68]: a 0.6 m strip along both side walls has no floor; you see the night ground plane (black, or blue-grey while snow cover builds). **Unity: floor covers the full 6.4 x 5.2 m**, plank direction and scale kept (§2).
2. **Pendant lamp is under the floor** (the -0.35 offset lands on the *group* y, not the shade). The shade, its point light (1.3) and the only shadow-casting light (spot 14, 1024² map) sit at y~-1.15. **So the approved VR room has no pendant and no realtime shadows at all**; the under-floor point light adds ~0.03 x albedo to walls and ceiling through the floor. **Unity default: omit the pendant** (look matches the approved build); intended values are in data `lights[pendant*]` if the user wants it (open question).
3. **Texture scale differs per face.** Plaster (repeat 4) and brick (repeat 2) are stretched over each plane or box face's own [0,1] UVs, so bricks on the jambs are about 4x smaller than on the chimney breast. Unity: world-scale UVs (§2, §3.6).

## 1. Pipeline and project prerequisites

The project is still **Built-in + Gamma**: `GraphicsSettings.asset:40 m_CustomRenderPipeline: {fileID: 0}` (and QualitySettings), `ProjectSettings.asset:50 m_ActiveColorSpace: 0`, `:49 m_StereoRenderingPath: 0`.

**Use URP 17 (Forward), Linear.**
- Meta's setup tool *requires* Linear (`com.meta.xr.sdk.core@4c2eac90db2d/Editor/OVRProjectSetup/Tasks/Implementations/OVRProjectSetupRenderingTasks.cs:105-115`). It recommends Vulkan (`:136-149`), Forward (`:232-237`), Stereo Instancing (`:250-253`), no HDR (`:468-491`), no depth/opaque texture (`:418-464`), no camera stack (`:522-539`) and MSAA = `OVRPlugin.recommendedMSAALevel` (`:591-611`). Meta labels its Built-in shaders "(Legacy)" (`mrutilitykit/Core/Shaders/*BiRP (Legacy).shader`).
- URP shades up to 8 lights per object in one pass (`universal/Runtime/UniversalRenderPipeline.cs:131-134`). Built-in forward adds a pass per pixel light.
- URP asset: Forward, MSAA 4x, HDR off, no post. Main light per pixel; shadows 1024, 1 cascade, 4 m (used in MR only). Additional lights per pixel, `maxAdditionalLightsCount = 4` (`UniversalRenderPipelineAsset.cs:1339`), no additional shadows. SRP Batcher on. `OVRManager.foveatedRenderingLevel = High` (`OVRManager.cs:1570`; fixed foveation level 1).
- **Create materials as .mat assets in the editor** (a setup script), not `new Material(Shader.Find(...))` at runtime, or URP Lit keyword variants (`_ALPHATEST_ON`, `_NORMALMAP`, `_EMISSION`, `_METALLICSPECGLOSSMAP`, transparent) get stripped from the build. Assign the generated textures at runtime.

## 2. Room shell

Constants: W=6.4, D=5.2, H=2.9, zBack=-1.12, zFront=4.08, cz=1.48 (source frame). Unity: floor centre (0,0,-1.48).

| Part | source frame | Unity pos / Euler | Notes |
|---|---|---|---|
| Floor | bug, see §0 | Quad 5.2x6.4 at (0,0,-1.48), Euler (90,90,0); tiling (2.4375, 3.6923) | Planks run along world X, 0.133 m wide, 0.867 m long. Equivalently u=-z/2.1333, v=x/1.7333 |
| Ceiling | (0,2.9,1.48) rotX +90 | (0,2.9,-1.48) (-90,0,0) | `ceiling`, no texture |
| Back wall 6.4x2.9 | (0,1.45,-1.12) | (0,1.45,1.12) (0,0,0) | `wall` (plaster) |
| Front wall | (0,1.45,4.08) yaw 180 | (0,1.45,-4.08) (0,180,0) | |
| Left wall 5.2x2.9 | (-3.2,1.45,1.48) yaw 90 | (-3.2,1.45,-1.48) (0,-90,0) | |
| Right wall, 4 pieces around the opening | 4 planes | x=3.2, yaw +90; pieces: 1.4x2.9 at z=+0.42, 2.0x2.9 at z=-3.08, 1.8x0.5 at y 2.65 z -1.18, 1.8x0.7 at y 0.35 z -1.18 | Window opening z in [-2.08,-0.28], y in [0.7,2.4] |

Wall UVs: world scale, one tile = 1.6 m horizontal x 0.725 m vertical (the back wall's look). Do this for all pieces and merge them into one mesh. Ceiling #f2eee6, rough 1.

## 3. Furniture and props

All per-mesh transforms are in data `meshes[]`, grouped by `group`. Key placements (Unity coordinates; source frame = same with z negated):

| Prop | Unity group pos, yaw | Build |
|---|---|---|
| Bench (static) | (0,0,-0.1) | walnutGloss top + 4 legs, fabric cushion, seat height .515 |
| Bookshelf | (-3.02,0,-2.68), -90 | walnut box 1.1 wide x 1.9 x .32, back panel at Unity local z +.15 (wall side), 5 shelves at y .08+.44i, books + fern |
| Paintings (gilt) | p1 0.8x0.6 seed1 (-1.7,1.85,1.08) facing -z; p2 0.8x0.6 seed2 (1.75,1.85,1.08); p3 1.0x0.7 seed3 (1.95,1.9,-4.04) yaw 180 | rounded box (w+.12, h+.12, .05, r .012) `gilt`; box (w+.04, h+.04, .052) `frame` at Unity local z -.002; canvas at local z -.03 |
| Floor lamp | (-2.4,0,0.52) | brass base + pole 1.6 m, open shade (r .16/.22, h .3) at y 1.65 `lampShade`, light at y 1.6 |
| Loveseat | (1.65,0,-2.83), +48.70 | `sofa(x, z, -0.85, 1.25)`: 2 seats, rounded boxes (base, back, 2 arms, 2 seat + 2 back cushions, pillow), 4 legs; part transforms in data group `loveseat` |
| Coffee table | (0.85,0,-3.13) | walnutGloss disc top r .3 at y .42 on a tapered leg |
| Rug | (0,0.004,-0.55) Euler (90,0,0) | 4.6 (x) x 3.2 (z) |
| Ivy (window sill) | world (3.06,0.63,-1.83) | trailing plant, §3.2 |

### 3.1 Bookshelf books
Shelves i=0..3: `x=-0.5, k=0; while (x<0.46)`:
- `w = .025 + ((7i+3k)%5)*.006`, `h = .22 + ((3i+5k)%6)*.03`, `d = .22 + (k%3)*.02`;
- box centred at (x + w/2, y + .0125 + h/2, -.02) in shelf-local coordinates (Unity local z +.02);
- colour `HSL(((5i+11k)%10)/10, .45, .4)`, evaluated in **linear** space (the working colour space). Store `LinearToSRGB(rgb)` if you put it in an sRGB palette texture;
- `x += w + .003; k++; if (k%9==8) x += .05`.

Merge into one mesh. URP Lit ignores vertex colours, so use a 16x1 palette texture (point filter, UV per book) or a Shader Graph Lit that reads vertex colour. Roughness 0.8.

### 3.2 Plants
Six instances (4 floor plants, fern on the shelf, trailing ivy on the sill): options and Unity positions in data `plants`.

Pot: cyl(rTop=pot, rBot=.72*pot, h=1.5*pot, 24 seg) centred at y=.75*pot, `terracotta`. Rim: torus(R=pot, tube .07*pot, 8x32) at y 1.5*pot. Soil: disc r .94*pot at `base = 1.5*pot - .012`.

Per stem i:
- `a = 2πi/stems + U(-.35,.35)`, `r = spread*U(.55,1)`, `h = height*U(.65,1)`, `(dx,dz) = r(cos a, sin a)`.
- Upright stem through `[(0,b,0), (.35dx, b+.55h, .35dz), (dx, b+h, dz)]`. Trailing: `[(0,b,0), (.5dx, b+.05, .5dz), (dx, b-.45h, dz), (1.15dx, b-h, 1.15dz)]`. Curve = **centripetal** Catmull-Rom (tension .5). Tube: 10 segments, 6 sides, r .007 (trailing .004), `stem`.
- Leaves per stem n = lps (trailing 2*lps), lps default 3. Leaf k sits at curve t = .5 + .5k/(n-1) (trailing .25 + .75k/(n-1)).
- Leaf rotation: `yaw = atan2(dx,dz) + U(-.7,.7)`, `pitch = (trailing ? -1 : -droop) + U(-.3,.3)`, `roll = U(-.4,.4)`. Source Euler YXZ (π/2 - pitch, yaw, roll) -> Unity `Euler(-(π/2-pitch), -yaw, roll)`.
- Leaf scale `s = U(leafScale)*(k==n-1 ? 1 : .8)`.

Leaf blade: plane 1x1.8 with 3x8 segments, translated +0.9 in y, then `z = -.45u² + .12|x|` with u=y/1.8 (Unity: +.45u² - .12|x|, reversed winding). Material `leaf`/`leafDark`: alpha clip 0.5, both faces.

The reference model draws unseeded random numbers, so its plants differ on every load. Unity: `System.Random(seed)` per plant (e.g. 101..106).

Sway: the whole plant group gets `rotZ = sin(.7t+φ)*amp`, `rotX = sin(.5t+1.3φ)*.7amp`, amp in [.012,.022] rad, φ in [0,2π). Unity: keep pots static and sway stems + leaves in the vertex shader about each plant's base (store the pivot in UV2). Same angles, Unity x-angle negated.

### 3.3 Candles (only 4: coffee table + mantel)
| Candle | Unity world | r | h | light |
|---|---|---|---|---|
| table | (0.93,0.435,-3.15) | .03 | .07 | yes |
| mantel L | (-0.25,1.27,-3.78) | .018 | .10 | – |
| mantel M | (0.10,1.27,-3.78) | .018 | .14 | yes |
| mantel R | (0.60,1.27,-3.78) | .018 | .10 | – |

Parts per candle: holder cyl(1.8r / 2.1r, h .008, 20 seg) at y .004 `brass`; wax cyl(r, h, 16 seg) at .008 + h/2; wick cyl r .0015 h .012 at .008 + h + .005 `soot`; flame = 2 crossed quads .028x.075 (base at .008 + h + .002, rotated 0° and 90°), per-quad seed `s + 3.7i` with s random 0..20.

Merge into 4 meshes (holders, wax, wicks, flames); the flames use the shared Flame shader (§4.2) at intensity 1.8. Light: `FlickerLight(#ffc070, 0.45, range 1.8, decay 1.8, amount 0.3)` at the wick + (0, h + .08, 0). `SetCandles(false)` hides all candle meshes and both candle lights.

### 3.4 Contact shadows and AO strips
- `shadowBlob`: quad (wxd) at y .005, Unity Euler (90,-rot,0), black x `softDot` alpha x opacity, depth write off, queue after the floor.
- 11 static blobs and the 4 AO strips (0.55 m quads along each wall base at y .004, dark edge v=1 toward the wall) are listed in Unity coordinates in data `staticBlobs_unity`. Rig-attached blobs (table, drums, harp) are in `rigDecor`.
- Improvement: the piano blob is static at the piano's home although the piano moves with the layout. Parent it to the piano at local (0,.005,+.2).
- Back AO strip and rug share y .004: use ZTest LEqual with `Offset -1,-1` on the decal shader.

### 3.5 Materials -> URP
Roughness r -> URP smoothness 1-r. Roughness maps -> MetallicGlossMap with A = 1 - map*r, R = metalness. The reflection strength is `env x 0.12` (§6.3).

URP Lit unless noted (r = roughness, m = metalness):
- floor (oak + rough map, r1); wall (plaster, r.95; Simple Lit is fine); trim #f4f0e8 r.6; rug (r1).
- walnut (map + rough, r.8); walnutGloss (Physical, r.5, clearcoat .3/.4) -> smoothness .55, no clear coat on Quest; fabric #3b3f4a r1.
- lampShade #f7e6c8 r1, emissive #ffd9a0x.35, both faces.
- brick (r.95); stone #4a4542 r.9; soot #0a0806 r1; log = walnut map x #6a4a34 r1.
- terracotta #b8623f r.85; soil #2b1d14 r1; stem #3f6b2f r.8 (sway shader); leaf/leafDark alpha clip .5, both faces, r.75 (sway).
- sofa #66795f, sofaCushion #7a8c70, pillow #c9a46a (r1, bump = weave .4 -> normal map; one palette material or three).
- wax #f3e9d2 r.6; frame #2b1f16 r.5; scenery(seed) r.75.
- brass #cfa54e m1 r.3 env2.4; gilt #d4aa4e m1 r.32 env2.6.
- windowGlass #dfe6ee r.03, opacity .08 -> Lit Transparent, smoothness .97.
- mound #e4ecf8 r.95, emissive #262e40.
- Custom unlit: nightSky, nightGround (§5); shadowBlob/aoStrip decals.

### 3.6 Fireplace geometry
Group at Unity (0.3,0,-4.08), yaw 180; every box and log is in data group `fireplace`. Brick: jambs .38 wide, lintel, chimney breast 1.66 x 1.64 to the ceiling. Soot firebox (back + sides). Stone hearth 1.9 x .75 and mantel 1.9 x .55 with its top at y 1.27. Three logs lying in the firebox: source Euler XYZ (0,a,pi/2) -> Unity `Euler(0,-a,90)`. The opening is 0.9 wide x 0.78 high. Brick UVs: box-projected world scale, tile 0.83 x 0.82 m (= one brick texture over the chimney breast).

## 4. Fire effects

All flame and coal materials share `uTime` = Decor time; in Unity, a global `_FireTime`. Positions in §3.6 and §4 are **fireplace-local source-frame** coordinates: Unity local = (x, y, -z), yaw -a.

### 4.1 Rules for every custom shader (flames, coals, snow, frost)
- These shaders skip tone mapping and colour-space conversion. Their output values are displayed **as sRGB values**, and hex colour uniforms are linearised first (room snow #c8d4f0 displays as #93a8de). In Unity (Linear, sRGB eye buffer) return `SRGBToLinear(rgb)` and feed those uniforms as their linear values.
- Blending: Normal = `Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha`; Additive = `Blend SrcAlpha One, One One`; ZWrite Off. The separate alpha factors matter for compositing over passthrough in MR.
- Use `float` (not `half`) for hashes and noise. Add the single-pass-instanced macros (`UNITY_VERTEX_OUTPUT_STEREO` etc., `core/ShaderLibrary/UnityInstancing.hlsl:116-250`).

### 4.2 Flames (implement verbatim)
- Noise: `hash/noise/fbm`, fbm 4 octaves, p*2.03 + (1.7, 9.2).
- `t = uTime*.9 + seed`; three fbm terms at uv*(3,2.2) / (7,4) / (14,9), x offset +seed / -seed / +2seed, v minus 1.7t / 2.6t / 4t (the pattern rises).
- Shape: tongues -> width -> edge -> heat.
- Colour ramp (.45,.02,0) -> (.95,.3,.02) -> (1,.72,.18) -> (1,.92,.6).
- Output `(col*I*a, a)` with Additive blending, so the final add is col*I*a².

Fireplace fires (`makeFire`): `planes` quads (w x h, base at the origin) at yaw pi*i/planes, seed + 3.7i:
- fire1 w.5 h.72, 3 planes, seed 2, I 1.15 @ (.02,.07,.22)
- fire2 w.3 h.46, 3 planes, seed 7, I 1.05 @ (-.17,.09,.17), yaw .5
- fire3 w.26 h.38, 2 planes, seed 11, I 1.0 @ (.18,.1,.26), yaw -.4

Unity: one 8-quad mesh, both faces, with per-vertex seed and intensity in UV1 (a cluster flame material), so 1 draw. Candle flames use the same shader at I 1.8.

### 4.3 Coals, glow, smoke, embers
- **Coals**: quad .7x.5 facing up @ (0,.035,.22), seed 1.7.
  - `mask = 1-smoothstep(.75,1,|uv-.5|*2)`, `n = fbm(7uv+seed)`, `cracks = smoothstep(.42,.6,n)`, `pulse = .65+.35 sin(1.6t+14n)`.
  - `col = lerp((.03,.015,.01),(.95,.3,.04),cracks*pulse) + (1,.65,.25)*smoothstep(.6,.78,n)*pulse*.7`, alpha = mask, Normal blending.
- **Cavity glow**: billboard 1.1x.9 @ (0,.34,.1), softDot x #ff5a14, additive. Opacity `.45+.15 sin(5t+1.3)+.08 sin(13.1t)`, computed in the shader.
- **Smoke**: 36 quads of .28 m, #9a9a9a, alpha .14, normal blending. Spawn box x in [-.2,.2], y in [.45,.95], z in [.14,.3]. They rise at .18–.32 m/s, drift x by `sin(.8t+i)*.08*dt`, and wrap at y .98 back to .45 with a new x.
- **Embers**: 70 quads of .014 m, #ffa050, alpha .9, additive. Box x in [-.25,.25], y in [.1,.9], z in [.12,.3]; rise .15–.4 m/s; drift `sin(3t+i)*.04*dt`; wrap at y 1.0 back to .12.
- Unity: two ParticleSystems (Local space, Box shape, Prewarm, max 36/70, lifetime = (top - wrap)/speed, Noise for the drift, `Universal Render Pipeline/Particles/Unlit`, softDot texture), about 0.05 ms CPU each. A stateless quad shader works too.

### 4.4 Flicker lights

```csharp
walk += (Random.value - .5f) * .08f; walk *= .92f;   // per frame
light.intensity = baseI * (1 + amount * (.5f*Mathf.Sin(t*11 + ph) + .3f*Mathf.Sin(t*17.3f + 2*ph) + walk));
```

- Fire light: #ff8f3c, 4.2, range 6, decay 1.6, amount .22, at local (0,.5,.45) = Unity world (0.3,0.5,-3.63). Candle lights: §3.3.
- Frame-rate independent variant: `walk += (r-.5)*.08*sqrt(dt*72); walk *= pow(.92, dt*72)`.

## 5. Window, outside and snow

### 5.1 Window
Window group: Unity (3.18,1.55,-1.18), yaw +90 (local +z points **outward**, local x along the window).
- `trim` frame .08 deep (outer bars, centre mullion and a transom at y **+.204** = .12*winH, so the panes are unequal). Interior sill top at world y .63, holding the ivy. Glass quad 1.8x1.7 at local z 0. Outside ledge at local (0,-.8955,+.125). All parts are in data group `window`.

### 5.2 Sky and ground (outside)
- Sky: unlit quad 40x11 at Unity (17.2,4.5,-4.18), yaw +90 (faces the room), `nightSky` texture (§7). Moon disc at world ~ (17.2,5.38,-8.98).
  - While snowing, tint `k = 1-.12*level`; `color = (k, k+.02*level, k+.06*level)`.
  - Hill overlay: `ha = hs.a*smoothstep(0,.8,cover)`; `rgb = max(rgb, lerp(rgb, hs.rgb, ha))` with `hs = hillSnow(uv)`, created lazily when cover > .002.
- Ground: unlit quad 60x60 at Unity (23.2,-0.05,-1.18), facing up.
  - Colour `lerp(#05070f, #435070, .92*cover^.8)`.
  - Drift patch (hash `gh` constants 127.1/311.7/19.19): `g = (x, -z)_world`, `drift = gn(.35g)*.6 + gn(1.3g+4)*.4`, `far = smoothstep(5,15, x-3.2)`, `k = (.78 + .34*drift + .08*gn(9g))*(1-.3*far)`, `rgb *= lerp(1, k, smoothstep(0,.5,cover))`.
- Both are plain unlit colours that go through tone mapping (no extra sRGB conversion needed in Unity).

### 5.3 SnowWeather - a plain C# singleton
- Fields `Target` (switch 0/1), `Level`, `Cover`, `Wind`, `WindVel`, `Gust`, `NextGust = 3`, `event Action<Gust> OnGust` (audio subscribes: `audio.snowGust`).
- `Update(dt)` with dt ≤ 0.05:
  - `Level += (Target-Level)*min(1, .8dt)`, snapping when |Δ| < .003;
  - `Cover += dt*Level/40` while Target > 0, else `Cover -= dt/150`, clamped 0..1;
  - gusts only when `Level ≥ .15` (else `NextGust = max(NextGust, t+2)`); start one when `t ≥ NextGust` and the last has ended;
  - gust: strength U(.45,1), attack U(1.1,1.9), hold U(.4,1.6), release U(2.2,3.6); next = t + duration + U(1.5,6);
  - envelope = smoothstep up, hold, smoothstep down;
  - `WindVel = .32*(1+.3 sin .11t) + 2.3*env`, `Wind += WindVel*dt`.
- Push globals `_SnowTime, _SnowIntensity(=Level), _SnowWind, _SnowWindVel, _SnowCover, _SnowPx`. `_SnowPx = XRSettings.eyeTextureHeight*.5`; on a desktop use `Screen.height*.5`.
- Room dressing (`applyRoom`): field visible when Level > .001; mound visible when Cover > .01, `scale.y = max(.001, Cover^.75)`; frost visible when Cover > .01, `uFrost = Cover`.

### 5.4 Snow field
- **Custom shader, not ParticleSystem**: stateless, deterministic, no CPU per flake, and the per-flake window test needs a shader anyway.
- Mesh: N flakes x 4 verts (corner id in UV0.zw). Per flake from `mulberry32(seed)`, called **in this order**: pos.xyz (3), seed4 (size, sway, rate, phase), tone (1).

```csharp
uint a = seed; double Next(){ a += 0x6D2B79F5u; uint t = a; t = (t ^ (t >> 15)) * (t | 1u);
  t ^= t + (t ^ (t >> 7)) * (t | 61u); return (t ^ (t >> 14)) / 4294967296.0; }
```

Room field: N 6000, rect (.94,.89), depth [.1,15] bias 1.6, lateral (1.4,.9), low (-1.6,0), high (1.3,.9), fade [10,18.5], size [.007,.022], colour #c8d4f0, haze #2a3554 x .6, seed 7. It lives in the window group.

Vertex math in the **Unity window frame (+z outward)**, against the source-frame formulas:
- flake `p = (x, y, +dd)`;
- `cam = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos,1)).xyz`, `c = -cam.z` (eye distance in front of the glass);
- `keep = step(.02,c)*step(.02,dd)`, `k = c/max(c+dd, 1e-4)`, `hit = cam.xy + (p.xy - cam.xy)*k`;
- `px = size*_SnowPx*abs(UNITY_MATRIX_P._m11)/max(dist,.05)` with `dist = -positionVS.z`;
- expand the quad in view space by `.5*ps*dist/(_SnowPx*|P11|)` per corner;
- culled flake -> `positionCS = float4(2,2,.5,1)`.

Fragment: spin and squash the corner UV, soft disc, `col = lerp(uColor*(.78+.3tone), uHaze, vHaze)`. Normal blending. Mesh bounds = the volume (`updateBounds`; Unity z in [0, d1+.1]). Queue Transparent+2 (glass +0, frost +4). Keep a C# `FlakeAt(i, time, wind, windVel, camLocal)` mirror for EditMode tests.

### 5.5 Frost and sill mound
- Frost: quad over the glass at 4 mm toward the room (Unity local z -.004), queue +4. Uniforms: half (.9,.85), mullion half .02, transom (.204, 1), reach .15, veil 0 (room).
  - Implement verbatim: `h21` constants 233.34/851.73/23.45; soft-min edge distance; needles at scales 22 and 47; alpha ≤ .5.
  - Output via `SRGBToLinear`.
- Mound: grid nx 56 x nz 9 + a skirt row; `bump` = 9 values from mulberry32(31).
  - Profile `y = h*(sqrt(max(0,1-zn³))*.88+.12)*ends*(.82+.36*bump(xn))`, `ends = smooth(xn/.05)*smooth((1-xn)/.05)`.
  - In Unity it runs along +z (outward). Origin at window-local (0, -.878, +.04); w 1.92, d .155, h .065; scale.y from cover.

## 6. Lighting

### 6.1 Lights as approved (positions in Unity coordinates; all in the room root, all hidden in MR)
URP's falloff is 1/d² x (1-(d/r)⁴)² and Lambert has no 1/π (`RealtimeLights.hlsl:47-60`, `Lighting.hlsl:32-36`). The reference lights fall off as 1/d^decay x the same window, x albedo/π. So **Unity intensity = reference/π * d_ref^(2-decay)**; directional = reference/π.

| Light | reference | Unity | Unity intensity |
|---|---|---|---|
| Moon | Dir #a9bbff 0.55 from (5.2,3,2.18) -> (0.2,.6,.2), no shadow | **Main light**, Euler (24.05,-68.40,0), no shadows | 0.175 |
| Floor lamp | Point #ffd6a0 2.4 d5 decay1.5 | (-2.4,1.6,0.52) range 5 | 1.08 (d_ref 2) |
| Fire | Point #ff8f3c 4.2 d6 decay1.6, flicker .22 | (0.3,0.5,-3.63) range 6 | 2.07 (d_ref 3) |
| Candle x2 | Point #ffc070 .45 d1.8 decay1.8, flicker .3 | §3.3 | 0.13 (d_ref .6) |
| Hemisphere | sky #4a5a8a, ground #3a2a20, .22 | ambient (§6.2) | – |

That makes 1 main + 4 additional per-pixel lights, no shadows in VR (as approved). The moon lights every +x-facing surface through the walls (no shadow); keep that. MR lights are in §9.

### 6.2 Ambient and environment **[measured]**
The reference adds a prefiltered room environment map (σ .04) at intensity .12. That is the **dominant** ambient: on a white roughness-1 sphere, x0.12, the irradiance per normal is
- Unity +y .170, -y .070, +x .122, -x .103;
- **-z .235** (Unity -z normals: the back wall at z=+1.12 faces -z, so it is the brightest) vs +z .084 (the fireplace wall).

The hemisphere adds only ~ albedo x .07 x colour. Unity: `RenderSettings.ambientMode = Trilight`, sky **#747578**, equator **#69696a**, ground **#4c4c4b** (linear .175/.140/.073; data `ambient.vr`), intensity 1. Optional: encode the front/back asymmetry in `RenderSettings.ambientProbe` (SH L1) - SH coefficient conventions UNVERIFIED. Add ~10% for the under-floor pendant spill if A/B shows walls too dark.

### 6.3 Reflections, tone mapping, exposure
- Reflections for the metals: one ReflectionProbe (Realtime, Via scripting, 128 px) at (0,1.2,-1.48), with `RenderProbe()` called once after the build. The reference strength is `env*0.12` (brass .29, gilt .31); tune smoothness to match.
- Reference look: **ACES Filmic, exposure .95**, applied in every material (and the XR framebuffer). Its curve maps scene .05->.03, .18->.20, 1->.75, 2->.88.
- No post-processing on Quest means no tone mapping: expect slightly brighter shadows and harder-clipping highlights. If that matters, write `MusicRoom/LitToneMapped` (URP `UniversalFragmentPBR`, then this ACES fit in the shader). HLSL:

```hlsl
float3 ACES3(float3 c){ c *= 0.95/0.6;
 c = mul(c, float3x3(0.59719,0.07600,0.02840, 0.35458,0.90834,0.13383, 0.04823,0.01566,0.83777));
 c = (c*(c+0.0245786)-0.000090537)/(c*(0.983729*c+0.4329510)+0.238081);
 c = mul(c, float3x3(1.60475,-0.10208,-0.00327, -0.53108,1.10813,-0.07276, -0.07367,-0.00605,1.07602));
 return saturate(c); }   // GLSL mat3(col0,col1,col2)*v == mul(v, float3x3(col0,col1,col2))
```

- Background: camera Solid Color #05070f in VR; (0,0,0,0) in MR.

### 6.4 Baked vs realtime
v1: **realtime, no shadows**, everything generated at runtime: 5 lights in one URP forward pass, plus ambient and the one-shot probe (estimated 1.5–3 ms GPU on Quest 3S at 72 Hz, UNVERIFIED). v2 if GPU-bound: run the builder in the editor and bake the lamp, moon, candles and 80% of the fire into lightmaps. Keep a realtime fire light carrying only the flicker, plus light probes on a 0.5 m grid for the moving instruments. That saves ~1–2 ms but adds a bake step to every room change.

## 7. Procedural textures (generate to `Texture2D`; implement the CPU code exactly)

Core, in **double**:

```csharp
static double Hash(int x,int y){ unchecked{ int h = x*374761393 + y*668265263;
  h = (h ^ (int)((uint)h >> 13)) * 1274126177; return (uint)(h ^ (int)((uint)h >> 16)) / 4294967295.0; } }
// VNoise: lattice floor(), smoothstep fade s=f*f*(3-2f), bilinear lerp of Hash at the 4 corners.
// Fbm(x,y,oct=3): sum a*VNoise(x*f,y*f), a=.5 halving, f doubling, divided by sum of a (normalised).
```

| Texture | Size | Use | Algorithm |
|---|---|---|---|
| `woodTexture` | oak 1024 planks16 seed3 grain1.6 repeat3 light(.72,.52,.33) dark(.44,.28,.14); walnut 512 seed5 grain2.4 repeat2 light(.36,.21,.12) dark(.17,.09,.05) | floor, walnut*, log | Planks: `pw=size/planks`, `len=size/2`, `col=⌊x/pw⌋`, `off=⌊Hash(col,seed)*len⌋`, `row=⌊(y+off)/len⌋`, `id=Hash(31col+7row, seed+7)`, `tint=(id-.5)*.22`, `px=x+1000id`, `py=y+off+300row`, seam if `x-col*pw<2` or `(y+off)%len<2`. Then `figure=Fbm(.004px,.0015py,2)`, `warp=(Fbm(.02gs*px,.004gs*py,2)-.5)*5`, `stripe=.5+.5sin(.35gs*px+warp)`, `fine=VNoise(1.8gs*px,.1gs*py)`, `t=clamp01(.5+.32(stripe-.5)+.28(fine-.5)+.6(figure-.5)+tint)`, `rgb=lerp(dark,light,t)*(seam?.35:1)`, `rough=min(1,.5+.35(1-t)+(seam?.2:0))` -> mask A = 1-rough*r |
| `plasterTexture` | 256, colour (.91,.86,.78) | walls | `rgb = c*(.92+.08*Fbm(.12x,.12y,3))` (not seamless; acceptable) |
| `rugTexture` | 512 | rug | fill #5e1c26; gold #c99a4b stroke 6 px rect inset 22, 2 px inset 40; #7a2a34 diamonds on a 48 px grid from 60 to size-60 (vertices at cell mid ±(step/2-6)); x (.85+.15*VNoise(.9x,.9y)) |
| `leafTexture` | 128x256 x2 (light #7fb35a->#2f6a2e; dark #5f9a4a->#1f4d24) | leaves | almond outline from 2 cubic Béziers (64,252)->(64,4) with controls x=4/124 at y 184/51; diagonal gradient; soft dark rim; pale midrib and 13 vein pairs; x (.9+.1*VNoise(.15x,.15y)); alpha 0 outside |
| `brickTexture` | 512 | fireplace | mortar #b9ada0; `bw=size/6, bh=size/16, m=5`; row offset bw/2 on odd rows; `t=Hash(13col+7row,21)` -> rgb(110+40t, 55+25t, 45+18t), rect inset m/2; x (.85+.15*VNoise(.4x,.4y)) |
| `softDotTexture` | 64 | embers, smoke, blobs, glow | radial white alpha 1 @0, .55 @.35, 0 @1 |
| `weaveTexture` | 128 (tiling 8) | sofa bump | `h=.5+.25sin(.8x)+.25sin(.8y)+.15(VNoise(.5x,.5y)-.5)` -> normal map (Sobel, strength ~ .4) |
| `gradientStripTexture` | 4x64 | AO strips | alpha .42 (top/v=1) -> .12 @.45 -> 0, black |
| `sceneryTexture(seed)` | 512x384 x3 | paintings | kind = [lake, hills, coast][seed%3], palette[seed%3]. Sky gradient; sun radial r 90; 4 hill layers `base=horizon-10+22k`, `amp=(hills?30+14k:55+10k)*1.6*Fbm(x(.004+.002k)+10seed+7k, k, 3)`; water (stripes) or meadow; 14 ellipse trees; x brush(.94+.06VNoise(.25x,.9y)) x weave(.97+.03 sin1.6x sin1.6y) x vignette(1-.77(dx²+dy²)) |
| `nightSkyTexture` | 2048x1024, clamp | sky | gradient #040814 / #0b1430 .55 / #182448 .8 / #223058; 1100 stars `(Hash(i,1)w, Hash(i,2)*.78h)`, r = .8+3.2*Hash(i,3)³, a = .6+.4Hash(i,4), bluish if Hash(i,5)>.85; cloud band rows .3h–.6h `a=max(0,Fbm(.003x,.012y,3)-.5)*exp(-((y-.45h)/.08h)²)*.5` toward (90,100,140); moon at (.62w, .42h): halo r 20->160 rgba(220,230,255,.35->0), disc r 26 #f2f3ea, 6 craters; hills k=0 #0d1226 base .82 amp .09, k=1 #070a16 base .9 amp .06, ridge `h*base - Fbm(.0012x+5k, k, 3)*h*amp` (6 px polyline); 160 town lights `(Hash(i,11)w, h(.9+.08Hash(i,12)))`, rect (1.5+1.5Hash(i,14))x1.5 px, rgba(255,205,130,.9) if Hash(i,13)>.3 else rgba(200,225,255,.8) |
| `hillSnowTexture` | 1024x512 (alpha mask) | snow overlay | per column, X=2x: `r0=H*.82-Fbm(.0012X,0,3)*H*.09`, `r1=H*.9-Fbm(.0012X+5,1,3)*H*.06` (H=1024); for Y=2y≥r0: near=Y≥r1, u=Y-(near?r1:r0); patch = Fbm(.05X+(near?9:0), .1Y, 3)*.7 + VNoise(.35X,.35Y)*.3; cut = (near?.36:.3)+min(.12,.0015u); a = near ? .5e^(-u/18)+.5 : .45e^(-u/26)+.55; a *= clamp01(5(patch-cut))*min(1,(u+1)/4); rgb near (92,104,140) far (112,126,162) |

Unused in the current room, not built: `flameTexture`, `duskTexture`, `watercolorTexture`, castle/modern textures, fireworks.

Runtime cost: ~45 M hash calls, about 1.5–3 s on one core or 0.2–0.4 s with Burst `IJobParallelFor` (UNVERIFIED), ~30 MB uncompressed. **Recommended:** keep the C# in a runtime assembly and add an editor menu `Music Room/Bake Textures` that writes PNGs to `Assets/Generated/` (ASTC 6x6, mipmaps, masks and normals linear). Runtime generation is the fallback and is used by tests.

## 8. Layout and placement

**Seat frame.** `head` = camera position, `yaw` from the camera forward on the floor. Unity: `yaw = atan2(fwd.x, fwd.z)`, `pos = (head.x,0,head.z) + Quaternion.Euler(0,yaw°,0)*(home.x,0,home.z) + up*home.y`, `rot = homeYaw + yaw`.

| Rig | all mode (Unity home, yaw°) | single mode |
|---|---|---|
| piano (origin = white key tops) | (0,0.72,0.45), 0 | same |
| drums | (0.2,0,-0.26), +90 | (0,0,0.1), 0 (`KIT_FORWARD` .1) |
| harp | (-0.78,0,-0.2), +83 | (-0.2966,0,0.45), 180 (strings centred on the player, `FRONT` .45) |
| side table (deck + switches) | (-0.39,0,-0.21), -90 | stays where it is |
| lesson panel home | (0,1.03,0.47) | – |

**[measured]** At head (0,1.2,0) all mode needs no moves. Eye distances: piano .628, drums .529, harp .74, table .603; no overlaps; `fitSeat` shift null.

Side table rig (walnutGloss table top at y .6 with the deck and the switch panel): data `rigDecor.sideTable`.

**Page turn**: 0.35 s easeInOutQuad. Out goes `-dir*0.9 m` along the seat's right vector, scale 1->.85; in comes from `+dir*0.9 m`, scale .85->1, with opacity fades. Unity: a dither fade in the instrument shaders, or slide and scale only.

**Placement rules** (all constants fixed):
- `EYE_CLEAR .30`, `EYE_MARGIN .02`, `GAP .01`, `WALL_SLOP .005`, `STEP .02`, `MAX_PUSH 1.6`, `SEAT{half .2, front .1, back .3}`, `PANEL_MIN_EYE .3`, `PANEL_MAX_RISE .45`, priority piano > drums > harp > table.
- Room box: Unity `{x ±3.2, zMin -4.08, zMax 1.12}`, VR only.
- Footprint: for each visible mesh, `mesh.bounds` carried into the rig frame (`rig.worldToLocalMatrix * renderer.localToWorldMatrix`) gives an OBB; its floor rect is the x/z extent. Drop rects contained in another; skip contact shadows and sticks held in a hand.
- **resolve**, in priority order: `clearEye` (push away from the eye along the play centre by shortfall + .02, up to 6 times), then `clearWalls` (least shift), then, if still not free, `findSpot`. findSpot tries the contact normal, centre-to-centre, away from the player, then 16 compass directions, in .02 steps up to 1.6 m, staying off the seat, then 5 bisections. Finally `clearPanel`.
- **Drag** (`constrain`): an already-overlapping rig moves freely, and a turn into contact is refused. A move marches in 1 cm steps, bisects 6 times, then slides (full / tangent / x / z, keep the best gain). After 300 ms of pushing (`UNSTICK_MS`) the rig passes through with a red ring and `settleOne` runs on release. Lift [-.45,+.6] m about home with a .03 deadband. An off-hand pull of .12 m turns it 45°. In VR the origin is clamped to Unity `{x ±2.8, z -3.6..0.8}`.
- Grab visuals: additive floor ring 0.42–0.5 m #ffc46a α .5 (red #ff5a4a α .6 while passing through) at y .006, scaled `clamp(hypot(hu,hv)*.85,.3,1.1)/.5`; a dwell ring that fills over `GRAB_DWELL` .3 s; an edge cage = parts box +.02, α .3.
- **fitSeat** (VR only): dry-run the layout with no settle, then move the XR rig by `roomShift()`.

**[measured] Resolver cliff to fix in Unity.** Single-mode drums with the head at the XR origin: the kit's back reaches z -1.207 (source frame), outside the back wall. After the wall shift, the nearest part is 0.29 m from the eye (head y 1.2; 0.237 at y 1.1). That forces `findSpot`, which slides the kit **0.73 m to the right** (to source x .725). At y 1.3 it stays centred. Fix: fitSeat should dry-run *with* clearEye (walls off) before computing the shift, so the seat moves back instead of the kit jumping sideways.

## 9. Mixed reality

**Enter:** hide the room root (its lights too) and the candles, and hide the rigs until they are arranged. Background alpha 0, passthrough on. Turn the FIRE and WIND switches off (SNOW stays). Audio room mode `ar`. On the first frame with a pose, `autoArrangeAR` lays everything out **around the player**: seat-frame homes, no room box, no fitSeat, piano at .72 with its stand shown.
- Passthrough: `OVRManager.isInsightPassthroughEnabled = true` (`OVRManager.cs:1215`; check `IsInsightPassthroughSupported()` `:3824`) plus an `OVRPassthroughLayer` (`OVRPassthroughLayer.cs:43`, forced underlay `:531-534`). Toggle its `hidden` (`:50`). Camera clear (0,0,0,0).
- MRUK `EnableWorldLock` (`MRUK.cs:314`) rewrites the TrackingSpace every frame. Enable it in MR only and use `TrackingSpaceOffset` (`:333`); in VR the swivel and fitSeat move the rig themselves.
- Lights: ambient Trilight **#b1b1b1/#969696/#686868** (data `ambient.mr`). Main light white 0.414 at (0.6,2.4,-1.2), looking at the anchor (piano), soft shadows 1024, distance 4 m. No estimated real-world lighting is used (UNVERIFIED that Meta offers it).
- Shadow catcher: quad 2.6x1.8 at surface y + .002 under the anchor, rotated with it, outputs black with α = .32*(1-shadow). Write `MusicRoom/ShadowCatcher` (URP, `_MAIN_LIGHT_SHADOWS*`, Transparent, ZWrite Off). Reference: MRUK `Meta/MRUK/Scene/HighlightsAndShadows` (`Core/Shaders/HighlightsAndShadows.shader`).

**Table placement**. A one-hand pinch in empty air starts placing; the other rigs hide.
- Reticle #ffc46a: ring r .06–.08 (α .9) and a 1.326x.294 footprint (α .12, edge α .6) at Unity local z **+.393**, facing the head.
- Ray from the pinching hand (`OVRHand.PointerPose`/`IsPointerPoseValid`, `core/Scripts/Util/OVRHand.cs:153-160`), else the head. Cast with `room.Raycast(ray, 6f, new LabelFilter(TABLE|FLOOR|STORAGE|COUCH|OTHER), out hit, out anchor)` (`MRUKRoom.cs:672`; `LabelFilter.Included` is obsolete, `LabelFilter.cs:106`). Fallback: `EnvironmentRaycastManager.Raycast` (`EnvironmentRaycastManager.cs:203`, `IsSupported` `:187`). Improvement: require `normal.y > .7`.
- On pinch: yaw faces the head. `onTable = hit.y > .3` hides the stand, y = hit + .092 (`tableLift`); on the floor y = hit + .72. Then move .25 m away from the player (`PUSH_BACK`), place the catcher and sun, and lay the rigs out around the piano (`placeAroundPiano`, piano fixed).

**Snow behind real windows.** Use MRUK anchors labelled `WINDOW_FRAME` that have a `PlaneRect` (`MRUKAnchor.cs:58-78, 120`; the plane normal is +Z: `:502-518`, `MRUKRoom.cs:957-963`). Update on `MRUK.SceneLoadedEvent` (`MRUK.cs:263`) and the room's `AnchorCreated/Updated/RemovedEvent` (`MRUKRoom.cs:256-267`).

```csharp
var r = a.PlaneRect.Value; var c = a.transform.TransformPoint(r.center);
var n = Vector3.ProjectOnPlane(a.transform.forward, Vector3.up).normalized;
if (Vector3.Dot(head - c, n) < 0) n = -n;               // n toward the player
var rot = Quaternion.LookRotation(-n, Vector3.up);       // portal +z = outward
if (r.width > .15f && r.height > .15f && r.width < 8 && r.height < 5) PlacePortal(a, c, rot, r.width, r.height);
```

- Portal = SnowField (data `snow.portalField`: N 3000, depth [.06,8], #f4f7ff, no haze) plus frost (reach .12, at -.004). Each frame: `uFrost = max(cover, .4*level)`, `uVeil = .1*level` with veil colour (.78,.81,.86). Visible while level > .001 or cover > .01.
- **No window:** `USE_SCENE` refused (`OVRPermissionsRequester.cs:96`), `LoadSceneFromDevice` (`MRUK.cs:1050`) not Success, or no window anchor. After 2.5 s of snow (immediately with no scene), show `"No window found in your Space Setup. Point at a wall and pinch to place a snowy window."`
  - Ray against `WALL_FACE|INVISIBLE_WALL_FACE` (t in (.05,12]), else EnvironmentRaycast with |normal.y| < .35.
  - Reticle 1.0x1.2: edges #e6f0ff α .85, fill #cfe0ff α .12, no depth test. A pinch places the portal: centre y clamped to [.75,2.1], 5 mm off the wall.
- Leaving MR clears all portals.

## 10. Switches and tape deck (visual geometry; Unity local z = -source z)

Full geometry, colours and touch zones (source-frame and Unity local coordinates) are in data `switchPanel` and `tapeDeck`. Things that are easy to get wrong:
- Switch levers: Unity x-rotation on = **+31.5°**, off = -31.5° (source ∓0.55 rad). The LED glows #ff8a2a x1.4 when on. Labels are small up-facing quads; use the UI spec's text solution, no brand fonts.
- Deck: reels sit at Unity local z **+.035** and spin -3 rad/s (recording) or -4 rad/s (playing). Buttons and lamp are at z -.055. Lamp colours: idle #332222, recording #ff2a2a, playing #2aff5a (colour and emission). Buttons travel 5 mm at rate 18/s; REC/PLAY stay down while active.

## 11. Performance

**Reference model [measured]** (one desktop view, FOV 55): draws from the seat 47 (front) to 103 (back), whole room 189; 26–68 k triangles; 106 smoke/ember points plus 6000 snow points; 25–31 programs. 98 meshes are flagged castShadow, but the only shadowed light's frustum is under the floor, so its pass draws nothing (inferred: counts were identical with shadow updates off). Quest renders two views with a wider FOV.

**Unity target** (single-pass instanced, one draw covers both eyes): the VR room in about **34 draws** (37 with snow), the whole scene ≤ 150; ≤ 300 k triangles; no shadow casters in VR; one 1024 shadow map in MR. Merge static geometry per material at build time (`Mesh.CombineMeshes`), one renderer each:
- floor, walls, ceiling, rug, trim (frame + sills), glass, sky, ground;
- walnut (shelf + loveseat legs), books, walnutGloss (bench + coffee table), fabric, gilt, frames, canvases (3-slot atlas), brass (lamp + candle holders), lampShade;
- brick, soot (+ wicks), stone, logs, loveseat (palette), pots, soil, stems, leaves (2-variant atlas), wax;
- candle flames, fire flames, coals, glow, embers, smoke, floor decals (one atlas shader);
- + snow, frost, mound.

Rigs stay separate.

Estimated GPU costs (UNVERIFIED, Quest 3S, 72 Hz): Lit with 5 lights 1.5–3 ms; snow 0.2–0.4 ms; fire planes 0.1–0.3 ms when facing the fire; frost ~0.2 ms; sky and ground < 0.1 ms; particles ~0.05 ms CPU.

## 12. Suggested Unity classes (namespaces `MusicRoom.World` / `.Mr` / `.Layout`)

- `RoomBuilder.Build(RoomMaterials)` with `RoomDims` constants.
- `ProcTex` (static): `Hash`, `VNoise`, `Fbm`, `Wood`, `Plaster`, `Rug`, `Leaf(dark)`, `Brick`, `SoftDot`, `WeaveNormal`, `GradientStrip`, `Scenery(seed)`, `NightSky`, `HillSnow`.
- `ProcMesh` (static): `Cylinder(rTop, rBot, h, seg, open)`, `RoundedBox(w, h, d, r)` (clamp-and-push, 2 segments), `Torus`, `Disc`, `Tube(centripetal Catmull-Rom)`, `LeafBlade`, `SillMound`, `MirrorZ`.
- `Decor` (`SetCandles`, `SetSnow`, sway, glow), `FlickerLight`, `SnowWeather` (C#), `SnowField` (`SetRect`, `FlakeAt`), `FrostPanel`.
- `WindowSnowPortals`, `MixedRealityMode`, `LayoutService` (`Set`, `Next`, `Place`, `PlaceAroundPiano`, `RestoreHomes`), `PlacementResolver` (`Resolve`, `Constrain`, `SettleOne`, `ClearPanel`, `RoomShift`, `EyeDistance`).
- Shaders: `MusicRoom/Flame`, `Coals`, `Snow`, `Frost`, `NightSky`, `NightGround`, `FloorDecal`, `ShadowCatcher`, `Billboard`, optional `LitToneMapped`. Everything else uses URP Lit/Unlit/Particles.

## 13. Open questions / UNVERIFIED
1. Pendant lamp: keep the approved look (none; recommended) or build the intended one (spot ~5x brighter at the piano, plus a realtime shadow pass)?
2. The floor and texture-scale fixes (§0) change the look slightly; confirm with the user.
3. UNVERIFIED: WINDOW_FRAME `forward` points into the room (the code flips by head side anyway); anchor scale 1, so `PlaneRect` is in metres.
4. UNVERIFIED: GPU costs, Meta draw-call budgets, `ambientProbe` SH conventions, runtime `Texture2D.Compress` format on Quest.
5. Without ACES the shadows read slightly brighter: decide after an A/B against the approved look.
