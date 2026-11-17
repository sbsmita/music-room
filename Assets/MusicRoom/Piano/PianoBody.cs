// The slim digital piano's body (Docs/05-instruments.md `piano.build`, `piano.bodyBoxes`):
// a matte black extruded case with rounded end blocks, a thin dark grey top panel with a power button and four flush
// buttons, a perforated speaker grille, a slim acrylic music rest leaning back 17.19 degrees, and a black wooden stand
// (two side panels on feet, a back panel, a low stretcher). One builder per material (merged per material).
// The spec's frame is right-handed (z = -back); every position here is already Unity piano-local (z = back).
using System.Collections.Generic;
using UnityEngine;

namespace MusicRoom.Piano
{
    public static class PianoBody
    {
        public const float E = 0.006f;                          // rounding of the extruded edges
        public const float Face = 0.012f;                       // the face under the keys, recessed behind the key fronts
        public const float Bed = -0.032f;                       // the keybed: a white key on the felt clears it
        public const float Name = PianoSpec.L + 0.003f;         // the face just behind the keys (the keys hinge at L)
        public const float CheekIn = -PianoSpec.X0 + 0.0015f;   // inner face of the end blocks, 1.5 mm clear of the last keys
        public const float PlateX = CheekIn - 0.012f;           // half width of the top panel and grille
        public const float Top = PianoSpec.BodyTop, Bot = PianoSpec.BodyBottom, Rear = PianoSpec.BodyRear, Half = PianoSpec.BodyHalfWidth;

        // Panel palette (the four panel greys): texel centres of the 4 x 1 panel palette texture.
        public static readonly Color32[] PanelColours =
        {
            new Color32(0x30, 0x31, 0x36, 0xff),   // plate
            new Color32(0x55, 0x57, 0x5c, 0xff),   // power ring
            new Color32(0x37, 0x38, 0x3d, 0xff),   // power cap
            new Color32(0x3b, 0x3c, 0x41, 0xff),   // flush buttons
        };
        public static Vector2 PanelUv(int i) => new Vector2((i + 0.5f) / PanelColours.Length, 0.5f);

        /// <summary>The body profile (back, y), drawn E inside the finished outline (Docs/05-instruments.md, the body shape).</summary>
        public static List<Vector2> BodyContour()
        {
            var p = new PianoMeshBuilder.Profile(6);
            p.MoveTo(Face + E, Bot + E);
            p.LineTo(Face + E, Bed - E);
            p.LineTo(Name + E, Bed - E);
            p.LineTo(Name + E, Top - E - 0.004f);
            p.QuadTo(Name + E, Top - E, Name + E + 0.004f, Top - E);
            p.LineTo(Rear - E - 0.02f, Top - E);
            p.QuadTo(Rear - E, Top - E, Rear - E, Top - E - 0.02f);
            p.LineTo(Rear - E, Bot + E + 0.004f);
            p.QuadTo(Rear - E, Bot + E, Rear - E - 0.004f, Bot + E);
            p.LineTo(Face + E, Bot + E);
            return p.Contour();
        }

        /// <summary>The end-block profile: full depth, its top sweeping down to a front 4 mm proud of the keys (Docs/05-instruments.md, the cheek shape).</summary>
        public static List<Vector2> CheekContour()
        {
            var p = new PianoMeshBuilder.Profile(10);
            p.MoveTo(-0.004f + E, Bot + E + 0.006f);
            p.LineTo(-0.004f + E, 0.006f - E);
            p.QuadTo(-0.004f + E, Top - E, 0.075f, Top - E);
            p.LineTo(Rear - E - 0.02f, Top - E);
            p.QuadTo(Rear - E, Top - E, Rear - E, Top - E - 0.02f);
            p.LineTo(Rear - E, Bot + E + 0.006f);
            p.QuadTo(Rear - E, Bot + E, Rear - E - 0.006f, Bot + E);
            p.LineTo(0.002f + E, Bot + E);
            p.QuadTo(-0.004f + E, Bot + E, -0.004f + E, Bot + E + 0.006f);
            return p.Contour();
        }

        /// <summary>Case (#131315): the low block under the keys plus the housing behind them, and the two end blocks.</summary>
        public static PianoMeshBuilder Case()
        {
            var b = new PianoMeshBuilder();
            b.AddExtrudedProfile(BodyContour(), -CheekIn - 0.02f, CheekIn + 0.02f, E, 3, Vector2.zero);
            var cheek = CheekContour();
            b.AddExtrudedProfile(cheek, -Half, -CheekIn, E, 3, Vector2.zero);
            b.AddExtrudedProfile(cheek, CheekIn, Half, E, 3, Vector2.zero);
            return b;
        }

        /// <summary>The top panel: a 1.6 mm plate between the end blocks with a round power button and four flush buttons at
        /// the left (palette uvs instead of vertex colours: URP Lit ignores vertex colour).</summary>
        public static PianoMeshBuilder Panel()
        {
            var b = new PianoMeshBuilder();
            b.AddRoundedBox(new Vector3(2f * PlateX, 0.0016f, 0.075f), 1, 0.0007f, new Vector3(0f, Top + 0.0008f, Name + 0.008f + 0.0375f),
                PianoMeshBuilder.BoxUv.Fixed(PanelUv(0)));
            float btnZ = Name + 0.008f + 0.026f;
            b.AddCylinder(0.0068f, 0.0068f, 0.0008f, 28, new Vector3(PianoSpec.X0 + 0.03f, Top + 0.002f, btnZ), PanelUv(1));
            b.AddCylinder(0.0055f, 0.0057f, 0.0016f, 28, new Vector3(PianoSpec.X0 + 0.03f, Top + 0.0024f, btnZ), PanelUv(2));
            for (int i = 0; i < 4; i++)
                b.AddRoundedBox(new Vector3(0.011f, 0.0012f, 0.0065f), 1, 0.0005f, new Vector3(PianoSpec.X0 + 0.058f + i * 0.017f, Top + 0.0019f, btnZ),
                    PianoMeshBuilder.BoxUv.Fixed(PanelUv(3)));
            return b;
        }

        /// <summary>The speaker grille: a perforated strip along the top rear, the texture repeating every 12 mm (3 mm holes).</summary>
        public static PianoMeshBuilder Grille()
        {
            var b = new PianoMeshBuilder();
            b.AddPlaneUp(2f * PlateX, 0.019f, new Vector3(0f, Top + 0.0004f, Rear - 0.034f), 0.012f);
            return b;
        }

        /// <summary>The music rest: a 5 mm acrylic board in a slot behind the panel, leaning back 0.3 rad about its bottom edge,
        /// with a narrow ledge for the music.</summary>
        public static PianoMeshBuilder MusicRest()
        {
            var b = new PianoMeshBuilder();
            const float lean = 0.3f, rh = 0.135f, slot = Name + 0.087f;
            b.AddRoundedBox(new Vector3(0.52f, rh, 0.005f), 2, 0.0022f, new Vector3(0f, Top + 0.001f, slot), PianoMeshBuilder.BoxUv.Planar,
                Quaternion.Euler(lean * Mathf.Rad2Deg, 0f, 0f), new Vector3(0f, rh / 2f, 0f));
            b.AddRoundedBox(new Vector3(0.52f, 0.007f, 0.018f), 2, 0.0025f, new Vector3(0f, Top + 0.0045f, slot - 0.008f), PianoMeshBuilder.BoxUv.Planar);
            return b;
        }

        /// <summary>The stand: two side panels on feet, a back panel under the body and a low stretcher, on the floor 0.72 m
        /// below the key tops. The player's knees go between the side panels.</summary>
        public static PianoMeshBuilder Stand()
        {
            var b = new PianoMeshBuilder();
            const float floor = PianoSpec.FloorY;
            const float sideX = CheekIn - 0.035f, sideT = 0.024f, sideD = 0.25f, sideZ = 0.155f;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                b.AddRoundedBox(new Vector3(sideT, Bot - floor - 0.02f, sideD), 2, 0.004f, new Vector3(sx * sideX, (floor + 0.02f + Bot) / 2f, sideZ), PianoMeshBuilder.BoxUv.Planar);
                b.AddRoundedBox(new Vector3(0.05f, 0.026f, 0.31f), 2, 0.008f, new Vector3(sx * sideX, floor + 0.013f, sideZ + 0.005f), PianoMeshBuilder.BoxUv.Planar);
            }
            float span = 2f * sideX - sideT;
            b.AddRoundedBox(new Vector3(span + 0.004f, 0.2f, 0.016f), 2, 0.003f, new Vector3(0f, Bot - 0.1f, Rear - 0.03f), PianoMeshBuilder.BoxUv.Planar);
            b.AddRoundedBox(new Vector3(span + 0.004f, 0.04f, 0.02f), 2, 0.004f, new Vector3(0f, floor + 0.11f, Rear - 0.035f), PianoMeshBuilder.BoxUv.Planar);
            return b;
        }
    }
}
