// Builds the kit's GameObjects and meshes (Docs/05-instruments.md: drums build, stands, kick extras) under the kit
// root, grouped so the flash scale and the cymbal wobble move the right parts:
// per drum a shell, a batter head (played), a resonant head and one chrome mesh (rims + lugs); the pad's body, rim and
// top (played); per cymbal the plate (played), the nut and, for the hi-hat, the lower plate; one chrome mesh per stand
// (pole + legs merged, so each stays its own grab handle), the kick's spurs and pedal. Materials are
// clones of template assets (MaterialLibrary); without a library (EditMode tests) the renderers get no material and only
// the geometry is built.
// The kit's contact shadow on the floor (Docs/07-world-visuals.md, rigDecor.drumsBlob) is NOT built here: the Room module
// owns every floor decal (RigShadows: the kit's blob follows the rig on the floor with Room's MusicRoom/FloorDecal shader,
// Offset -1,-1 and the linear-framebuffer alpha), so building one here would darken the floor twice. The rig zone still
// covers the blob's rectangle: the kit group's world box includes it (DrumKit.RigZone).
// Draw budget (ARCHITECTURE.md sections 14 and 17: drums <= 25): every part that never moves on its own (shells,
// resonant heads, chrome, stands, nuts, the pad's body) is ALSO baked into one kit-frame mesh per material, and its own
// renderer is kept with forceRenderingOff: it stays enabled with valid bounds (one grab-handle OBB per stand or shell for
// Layout, the rig zone, the played-part exclusion), but draws nothing. Only the parts that move or flash draw alone:
// batter heads and the pad's top (flash scale), cymbal plates (wobble), the lower hi-hat (pedal) and the sticks: 19
// draws. The shells therefore take no 1.5 % flash swell; the heads, which carry the eye, still do.
// Desktop clicks (section 0): a thin disc collider on Layers.PlaySurface under every head, the pad top and each cymbal
// group (not under the rocking plate, so the target stays put).
// E7 (experience.md section 9): batter heads are ringed discs (289 vertices) that dent for a frame or three under a hit
// (HeadGive); the cymbal plates rock (CymbalMotion); the kick pedal gets a decorative beater: its chrome shaft and the
// footboard (no longer in the static chrome mesh) are one dynamic mesh, its felt ball a second draw (2 draws more: 21).
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Drums
{
    /// <summary>The kit's materials (Docs/05-instruments.md, drums build materials + the sticks').</summary>
    public sealed class DrumMaterials
    {
        public Material Pearl, Shell, Chrome, Rubber, Head, HeadClear, Cymbal, StickWood, StickTape, Felt;

        public static DrumMaterials Create(MaterialLibrary lib)
        {
            var m = new DrumMaterials();
            if (lib == null) return m;
            m.Pearl = lib.Lit(TextureUtil.Hex(DrumSpec.PearlHex), DrumSpec.PearlSmoothness);
            m.Shell = lib.Lit(TextureUtil.Hex(DrumSpec.ShellHex), DrumSpec.ShellSmoothness);
            m.Chrome = lib.Clone(MaterialLibrary.LitMetal);
            m.Chrome.SetColor(MaterialLibrary.BaseColor, TextureUtil.Hex(DrumSpec.ChromeHex));
            m.Chrome.SetFloat(MaterialLibrary.Metallic, 1f);
            m.Chrome.SetFloat(MaterialLibrary.Smoothness, DrumSpec.ChromeSmoothness);
            m.Rubber = lib.Lit(TextureUtil.Hex(DrumSpec.RubberHex), DrumSpec.RubberSmoothness);
            // Core's shared procedural textures (ARCHITECTURE.md section 14: one shared copy, never a second one).
            m.Head = lib.Lit(Color.white, DrumSpec.HeadSmoothness, 0f, SharedTextures.Get(SharedTextures.DrumHead));
            m.Head.SetTextureScale(MaterialLibrary.BaseMap, new Vector2(DrumSpec.HeadTextureRepeat, DrumSpec.HeadTextureRepeat));
            m.HeadClear = lib.Clone(MaterialLibrary.LitTransparent);
            Color clear = TextureUtil.Hex(DrumSpec.HeadClearHex);
            clear.a = DrumSpec.HeadClearOpacity;
            m.HeadClear.SetColor(MaterialLibrary.BaseColor, clear);
            m.HeadClear.SetFloat(MaterialLibrary.Smoothness, DrumSpec.HeadClearSmoothness);
            m.Cymbal = lib.Clone(DrumsMaterialTemplates.Cymbal);
            m.Cymbal.SetColor(MaterialLibrary.BaseColor, Color.white);
            m.Cymbal.SetTexture(MaterialLibrary.BaseMap, DrumTextures.LoadCymbal());
            m.Cymbal.SetFloat(MaterialLibrary.Metallic, DrumSpec.CymbalMetallic);
            m.Cymbal.SetFloat(MaterialLibrary.Smoothness, 1f);
            m.StickWood = lib.Lit(TextureUtil.Hex(StickSpec.WoodHex), StickSpec.WoodSmoothness);
            m.StickTape = lib.Lit(TextureUtil.Hex(StickSpec.TapeHex), StickSpec.TapeSmoothness);
            m.Felt = lib.Lit(TextureUtil.Hex(DrumSpec.FeltHex), DrumSpec.FeltSmoothness);
            return m;
        }
    }

    /// <summary>What the builder made: the pieces, the renderer sets the rig and the layout need, and the meshes to free.</summary>
    public sealed class DrumKitParts
    {
        public readonly DrumPiece[] Pieces = new DrumPiece[DrumSpec.PieceCount];
        /// <summary>Played parts (never grab handles): batter heads, the pad's top, cymbal groups. The
        /// sticks add theirs.</summary>
        public readonly List<Renderer> Played = new List<Renderer>(16);
        /// <summary>Everything else of the kit: shells, resonant heads, chrome, stands, the pad's body and rim, the
        /// kick's spurs and pedal.</summary>
        public readonly List<Renderer> Handles = new List<Renderer>(48);
        /// <summary>The kit-frame meshes that draw the static parts, one per material (chrome, red shell, pearl, resonant
        /// heads, rubber). Neither played parts nor handles: the per-part renderers above are those.</summary>
        public readonly List<Renderer> Static = new List<Renderer>(5);
        /// <summary>Desktop click targets on Layers.PlaySurface, indexed by piece id.</summary>
        public readonly Collider[] Clicks = new Collider[DrumSpec.PieceCount];
        public readonly List<Mesh> Meshes = new List<Mesh>(64);
        /// <summary>E7: the kick pedal's beater (and its two renderers: the chrome shaft + footboard, the felt ball).</summary>
        public KickBeater Beater;
        public readonly List<Renderer> BeaterRenderers = new List<Renderer>(2);
    }

    public static class DrumKitBuilder
    {
        /// <summary>Parts whose geometry goes into the kit-frame mesh of their material (filled while building).</summary>
        sealed class StaticBatch
        {
            public Material Material;
            public string Name;
            public bool CastShadow;
            public readonly List<CombineInstance> Parts = new List<CombineInstance>(32);
        }

        public static DrumKitParts Build(Transform kit, DrumMaterials mats)
        {
            var parts = new DrumKitParts();
            var mk = new MeshKit();
            s_batches.Clear();
            for (int i = 0; i < DrumSpec.PieceCount; i++)
            {
                PieceSpec spec = DrumSpec.Pieces[i];
                var g = new GameObject(spec.Pad ? "kick-pad" : spec.Kind).transform;
                g.SetParent(kit, false);
                g.localPosition = spec.Pos;
                g.localRotation = spec.LocalRotation;
                var piece = new DrumPiece(spec, g);
                parts.Pieces[i] = piece;
                if (spec.IsCymbal) BuildCymbal(piece, mk, mats, parts);
                else if (spec.Pad) BuildPad(piece, mk, mats, parts);
                else BuildDrum(piece, mk, spec.Id == DrumSpec.SnareId ? mats.Pearl : mats.Shell, mats, parts);
                if (DrumSpec.HasStand(spec)) BuildStand(kit, spec, mk, mats, parts);
            }
            BuildKickExtras(kit, mk, mats, parts);
            CombineStatic(kit, parts);
            return parts;
        }

        // Build time only (the kit is built once per app); keyed by the batch name so a library-less EditMode build, where
        // every material is null, still batches per role.
        static readonly List<StaticBatch> s_batches = new List<StaticBatch>(5);

        /// <summary>Hides the part's own renderer and queues its mesh for the kit-frame mesh of <paramref name="batch"/>.</summary>
        static Renderer Static(Renderer r, string batch, Material mat, bool castShadow, Transform kit)
        {
            StaticBatch b = null;
            for (int i = 0; i < s_batches.Count; i++) if (s_batches[i].Name == batch) { b = s_batches[i]; break; }
            if (b == null) { b = new StaticBatch { Name = batch, Material = mat }; s_batches.Add(b); }
            b.CastShadow |= castShadow;
            b.Parts.Add(new CombineInstance
            {
                mesh = r.GetComponent<MeshFilter>().sharedMesh,
                transform = kit.worldToLocalMatrix * r.transform.localToWorldMatrix,
            });
            r.forceRenderingOff = true;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return r;
        }

        static void CombineStatic(Transform kit, DrumKitParts parts)
        {
            for (int i = 0; i < s_batches.Count; i++)
            {
                StaticBatch b = s_batches[i];
                int verts = 0;
                for (int k = 0; k < b.Parts.Count; k++) verts += b.Parts[k].mesh.vertexCount;
                var mesh = new Mesh { name = "static-" + b.Name };
                if (verts > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.CombineMeshes(b.Parts.ToArray(), true, true);
                mesh.RecalculateBounds();
                parts.Meshes.Add(mesh);
                var go = new GameObject("static-" + b.Name);
                go.transform.SetParent(kit, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = b.Material;
                r.shadowCastingMode = b.CastShadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
                r.receiveShadows = false;
                parts.Static.Add(r);
            }
            s_batches.Clear();
        }

        /// <summary>The desktop click target of a piece: a closed disc .004 thick around its strike plane (y = 0), on
        /// Layers.PlaySurface, a child of the piece group so it follows the flash scale but not the cymbal wobble.</summary>
        static void Click(DrumPiece piece, MeshKit mk, DrumKitParts parts)
        {
            mk.Cylinder(piece.R, piece.R, DrumSpec.ClickThickness, DrumSpec.ClickSegments, false);
            var mesh = mk.ToMesh("click-" + piece.Kind);
            mk.Clear();
            parts.Meshes.Add(mesh);
            var go = new GameObject("click");
            go.layer = Layers.PlaySurface;
            go.transform.SetParent(piece.Group, false);
            var c = go.AddComponent<MeshCollider>();
            c.sharedMesh = mesh;
            parts.Clicks[piece.Id] = c;
        }

        static Renderer Part(Transform parent, string name, MeshKit mk, Material mat, DrumKitParts parts, bool castShadow)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mesh = mk.ToMesh(name);
            parts.Meshes.Add(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            // The kit casts shadows from shells, stands, the pad body, cymbals and stick shafts only; nothing in the kit
            // receives them (only MR's shadow catcher does). VR has no shadow-casting light at all.
            r.shadowCastingMode = castShadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = false;
            mk.Clear();
            return r;
        }

        /// <summary>A drum: open shell from y 0 to -depth, batter head at +.001 (up), resonant head at
        /// -depth-.001 (down), chrome rims at 0 and -depth plus 8 lugs at r + .012 around the middle.</summary>
        static void BuildDrum(DrumPiece piece, MeshKit mk, Material shellMat, DrumMaterials mats, DrumKitParts parts)
        {
            float r = piece.R, depth = piece.Spec.Depth;
            Transform g = piece.Group;
            mk.At(new Vector3(0f, -depth * 0.5f, 0f), Quaternion.identity).Cylinder(r, r, depth, DrumSpec.ShellSegments, true);
            bool pearl = piece.Id == DrumSpec.SnareId;
            parts.Handles.Add(Static(Part(g, "shell", mk, shellMat, parts, true), pearl ? "pearl" : "shell", shellMat, true, g.parent));

            mk.RingedDisc(r, DrumSpec.HeadRings, DrumSpec.ShellSegments, DrumSpec.HeadLift);
            var head = Part(g, "head", mk, mats.Head, parts, false);
            parts.Played.Add(head);
            piece.Top = head.transform;
            piece.Give = new HeadGive(head.GetComponent<MeshFilter>().sharedMesh, r);
            Click(piece, mk, parts);

            mk.Disc(r, DrumSpec.ShellSegments, -depth - DrumSpec.HeadLift, false);
            parts.Handles.Add(Static(Part(g, "resonant", mk, mats.HeadClear, parts, false), "resonant", mats.HeadClear, false, g.parent));

            mk.Torus(r + DrumSpec.RimOffset, DrumSpec.RimTube, DrumSpec.RimRadialSegments, DrumSpec.RimTubularSegments, 0f);
            mk.Torus(r + DrumSpec.RimOffset, DrumSpec.RimTube, DrumSpec.RimRadialSegments, DrumSpec.RimTubularSegments, -depth);
            for (int i = 0; i < DrumSpec.LugCount; i++)
            {
                float a = (float)i / DrumSpec.LugCount * Mathf.PI * 2f;
                float rl = r + DrumSpec.LugOffset;
                // Box x radial (source frame: rotateY(-a), then translate (cos a, ., sin a)); Unity mirrors z, and the eight
                // evenly spaced lugs land on the same set of angles.
                mk.At(new Vector3(Mathf.Cos(a) * rl, -depth * 0.5f, Mathf.Sin(a) * rl), Quaternion.AngleAxis(-a * Mathf.Rad2Deg, Vector3.up)).Box(DrumSpec.LugSize);
            }
            mk.Identity();
            parts.Handles.Add(Static(Part(g, "chrome", mk, mats.Chrome, parts, false), "chrome", mats.Chrome, false, g.parent));
        }

        /// <summary>The kick pad: rubber body r .1 x .03 under y 0, chrome rim, rubber top r .095 at +.001.</summary>
        static void BuildPad(DrumPiece piece, MeshKit mk, DrumMaterials mats, DrumKitParts parts)
        {
            Transform g = piece.Group;
            mk.At(new Vector3(0f, -DrumSpec.PadBodyHeight * 0.5f, 0f), Quaternion.identity).Cylinder(piece.R, piece.R, DrumSpec.PadBodyHeight, DrumSpec.PadSegments, false);
            mk.Identity();
            parts.Handles.Add(Static(Part(g, "body", mk, mats.Rubber, parts, true), "rubber", mats.Rubber, true, g.parent));
            mk.Torus(piece.R, DrumSpec.PadRimTube, DrumSpec.PadRimRadialSegments, DrumSpec.PadRimTubularSegments, 0f);
            parts.Handles.Add(Static(Part(g, "rim", mk, mats.Chrome, parts, false), "chrome", mats.Chrome, false, g.parent));
            mk.Disc(DrumSpec.PadTopRadius, DrumSpec.PadSegments, DrumSpec.HeadLift, true);
            var top = Part(g, "top", mk, mats.Rubber, parts, false);
            parts.Played.Add(top);
            piece.Top = top.transform;
            Click(piece, mk, parts);
        }

        /// <summary>A cymbal: the lathe plate (64 segments), the chrome nut, and for the hi-hat the
        /// lower plate flipped (rotation.x = pi) at y -.016. The whole cymbal group is a played part.</summary>
        static void BuildCymbal(DrumPiece piece, MeshKit mk, DrumMaterials mats, DrumKitParts parts)
        {
            Transform g = piece.Group;
            Vector2[] profile = DrumSpec.CymbalProfile(piece.R);
            mk.Lathe(profile, DrumSpec.CymbalSegments);
            var plate = Part(g, "plate", mk, mats.Cymbal, parts, true);
            parts.Played.Add(plate);
            piece.Top = plate.transform;
            mk.At(new Vector3(0f, DrumSpec.NutY, 0f), Quaternion.identity).Cylinder(DrumSpec.NutRadius, DrumSpec.NutRadius, DrumSpec.NutHeight, DrumSpec.NutSegments, false);
            mk.Identity();
            parts.Played.Add(Static(Part(g, "nut", mk, mats.Chrome, parts, false), "chrome", mats.Chrome, false, g.parent));
            Click(piece, mk, parts);
            if (piece.IsHat)
            {
                mk.Lathe(profile, DrumSpec.CymbalSegments);
                var lower = Part(g, "lower", mk, mats.Cymbal, parts, true);
                lower.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
                lower.transform.localPosition = new Vector3(0f, DrumSpec.HatClosedY, 0f);
                parts.Played.Add(lower);
                piece.Lower = lower.transform;
            }
        }

        /// <summary>A stand in the kit frame: chrome pole r .011 (10 sides) from the floor to the top and
        /// three legs r .007 (6 sides) from the hip at .32 to feet .3 out at y .01, one mesh per stand.</summary>
        static void BuildStand(Transform kit, in PieceSpec spec, MeshKit mk, DrumMaterials mats, DrumKitParts parts)
        {
            Vector3 top = DrumSpec.StandTop(spec);
            mk.CylinderBetween(new Vector3(top.x, 0f, top.z), top, DrumSpec.PoleRadius, DrumSpec.PoleSegments);
            Vector3 hip = DrumSpec.LegHip(top);
            for (int k = 0; k < 3; k++) mk.CylinderBetween(hip, DrumSpec.LegFoot(top, k, spec.LegTurnRad), DrumSpec.LegRadius, DrumSpec.LegSegments);
            parts.Handles.Add(Static(Part(kit, "stand-" + (spec.Pad ? "kick-pad" : spec.Kind), mk, mats.Chrome, parts, true), "chrome", mats.Chrome, true, kit));
        }

        /// <summary>Kick extras: two chrome spurs and the decorative pedal (kit frame).</summary>
        static void BuildKickExtras(Transform kit, MeshKit mk, DrumMaterials mats, DrumKitParts parts)
        {
            mk.CylinderBetween(DrumSpec.SpurA0, DrumSpec.SpurB0, DrumSpec.SpurRadius, DrumSpec.SpurSegments);
            parts.Handles.Add(Static(Part(kit, "kick-spur-0", mk, mats.Chrome, parts, false), "chrome", mats.Chrome, false, kit));
            mk.CylinderBetween(DrumSpec.SpurA1, DrumSpec.SpurB1, DrumSpec.SpurRadius, DrumSpec.SpurSegments);
            parts.Handles.Add(Static(Part(kit, "kick-spur-1", mk, mats.Chrome, parts, false), "chrome", mats.Chrome, false, kit));
            mk.Box(DrumSpec.PedalSize);
            var pedal = Part(kit, "kick-pedal", mk, mats.Chrome, parts, false);
            pedal.transform.localPosition = DrumSpec.PedalPos;
            pedal.transform.localRotation = Quaternion.Euler(DrumSpec.PedalPitchDeg, 0f, 0f);
            // Still a grab handle (its box), but it no longer draws from the static chrome mesh: the footboard moves (E7).
            pedal.forceRenderingOff = true;
            parts.Handles.Add(pedal);
            BuildBeater(kit, mk, mats, parts);
        }

        /// <summary>E7 kick beater: one chrome mesh for the shaft (built vertical from the axle) and the footboard (built
        /// centred, unpitched), posed by KickBeater; the felt ball under a pivot at the axle.</summary>
        static void BuildBeater(Transform kit, MeshKit mk, DrumMaterials mats, DrumKitParts parts)
        {
            mk.CylinderBetween(Vector3.zero, new Vector3(0f, KickBeater.ShaftLength, 0f), KickBeater.ShaftRadius, DrumSpec.BeaterShaftSegments);
            int shaftVerts = mk.VertexCount;
            mk.Box(DrumSpec.PedalSize);
            var chrome = Part(kit, "kick-beater-chrome", mk, mats.Chrome, parts, false);
            var pivot = new GameObject("kick-beater").transform;
            pivot.SetParent(kit, false);
            pivot.localPosition = KickBeater.Axle;
            mk.At(new Vector3(0f, KickBeater.ShaftLength, 0f), Quaternion.identity).Sphere(KickBeater.BallRadius, DrumSpec.BeaterBallWidthSegments, DrumSpec.BeaterBallHeightSegments);
            mk.Identity();
            var ball = Part(pivot, "kick-beater-ball", mk, mats.Felt, parts, false);
            parts.Beater = new KickBeater(pivot, chrome.GetComponent<MeshFilter>().sharedMesh, shaftVerts);
            parts.BeaterRenderers.Add(chrome);
            parts.BeaterRenderers.Add(ball);
        }
    }
}
