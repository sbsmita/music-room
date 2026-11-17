// The harp's shape in its own frame (Docs/05-instruments.md section 5, `harp`, `harp.build`). The origin is on
// the floor; strings and frame lie in the plane z = 0, which the right-handed -> Unity z flip keeps, so every in-plane
// value is used unchanged. The exceptions are the parts with depth: the brass tuning pins sit at Unity z -0.03
// (right-handed +0.03), the feet at z +-0.09 and the soundbox, whose rotation is converted with Coords (section 5).
// Positions, quaternions and meshes below are built in the right-handed frame and converted once (ToUnity / Coords).
using System;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Harp
{
    public enum StringColour : byte { Cream = 0, Red = 1, Blue = 2 }

    /// <summary>One string's fixed layout (harp-local Unity; a and b have z = 0).</summary>
    public readonly struct StringLayout
    {
        public readonly int Index, Midi;
        public readonly Vector3 A, B;          // a: soundboard end (u = 0), b: neck end (u = 1)
        public readonly float Length, Radius;
        public readonly StringColour Colour;
        public StringLayout(int index, int midi, Vector3 a, Vector3 b, float length, float radius, StringColour colour)
        {
            Index = index; Midi = midi; A = a; B = b; Length = length; Radius = radius; Colour = colour;
        }
    }

    /// <summary>A frame part: right-handed transform, Unity-space transform and mesh data (already converted).</summary>
    public sealed class HarpPart
    {
        public string Name;
        public HarpWood Material;
        public Vector3 LocalPosition;    // Unity, harp-local
        public Quaternion LocalRotation; // Unity
        public Vector3 LocalScale = Vector3.one;
        public MeshData Mesh;            // Unity space, in the part's own frame
        public bool CastShadows;         // casts a shadow (soundbox, pillar, neck, base)
    }

    public enum HarpWood : byte { Maple, Spruce, Walnut, Brass }

    public static class HarpGeometry
    {
        public const int StringCount = 22;
        /// <summary>SEGMENTS: rings along each string, so it can bend where the finger holds it and blur while it rings.</summary>
        public const int Segments = 16;
        public const int StringSides = 5;

        // The soundboard runs from sb0 (bottom, near the player) up to sb1; the neck curves from the
        // pillar top to sb1.
        public const double Sb0X = -0.55, Sb0Y = 0.17, Sb1X = 0.0, Sb1Y = 1.1;
        public static readonly double[] NeckPoints = { -0.68, 1.3, 0, -0.5, 1.43, 0, -0.25, 1.39, 0, -0.06, 1.25, 0, 0.0, 1.12, 0 };
        // Where the strings sit on the frame: lower ends along the soundboard line, upper ends along the
        // neck, as fractions of each. The bass string stays 5.4 cm from the pillar's axis so the pillar never hides it,
        // and the treble strings end before the neck dives into the soundbox (the shortest string is 21.5 cm).
        public const double BoardFrom = 0.045, BoardTo = 0.865, NeckFrom = 0.075, NeckTo = 0.88;
        // The height a seated player's hand meets the strings, kept within the playable middle. 10-upgrade
        // harp.md 5.2: at most .40 up the string (was .70), so lessons pluck near a third of the string as harpists do: a
        // pluck at the middle cancels the even partials (hollow C4-E4). Moves the glow and the capsule toward the board on
        // C3..A4 (C3 ~28 cm, C4 ~10 cm); B4 and up are unchanged.
        public const float PlayY = 0.95f, PlayUMin = 0.3f, PlayUMax = 0.40f;
        // Neck tube: 40 x 12, radius .04 scaled by 1 - .4 clamp((t - .55)/.4, 0, 1) along the arc.
        public const int NeckTubular = 40, NeckRadial = 12;
        public const float NeckRadius = 0.04f;
        // The invisible desktop hit plane: 1.0 x 1.4 centred (-0.33, 0.72) in the string plane.
        public static readonly Rect DesktopPlane = Rect.MinMaxRect(-0.33f - 0.5f, 0.72f - 0.7f, -0.33f + 0.5f, 0.72f + 0.7f);
        /// <summary>The harp sound source's harp-local position (ARCHITECTURE.md section 7.2).</summary>
        public static readonly Vector3 SourceLocal = new Vector3(-0.33f, 0.8f, 0f);
        /// <summary>Contact shadow (Docs/07-world-visuals.md rigDecor.harpBlob): harp-local Unity centre, 1.1 x 0.7, opacity .45.</summary>
        public static readonly Vector3 BlobLocal = new Vector3(-0.2725f, 0.005f, -0.168f);
        public const float BlobWidth = 1.1f, BlobDepth = 0.7f, BlobOpacity = 0.45f;

        static readonly int[] Degrees = { 0, 2, 4, 5, 7, 9, 11 };
        static CatmullRomCurve s_neck;
        static StringLayout[] s_strings;

        /// <summary>The neck centreline (shared, immutable after construction).</summary>
        public static CatmullRomCurve Neck => s_neck ?? (s_neck = new CatmullRomCurve(NeckPoints));

        /// <summary>C major from C3 to C6.</summary>
        public static int MidiOf(int i) => 48 + 12 * (i / 7) + Degrees[i % 7];

        public static StringColour ColourOf(int i) => i % 7 == 0 ? StringColour.Red : i % 7 == 3 ? StringColour.Blue : StringColour.Cream;

        /// <summary>The 22 strings.</summary>
        public static StringLayout[] Strings
        {
            get
            {
                if (s_strings != null) return s_strings;
                var list = new StringLayout[StringCount];
                CatmullRomCurve neck = Neck;
                for (int i = 0; i < StringCount; i++)
                {
                    double t = i / (double)(StringCount - 1);
                    double k = BoardFrom + (BoardTo - BoardFrom) * t;
                    double ax = Sb0X + (Sb1X - Sb0X) * k, ay = Sb0Y + (Sb1Y - Sb0Y) * k;
                    neck.GetPoint(NeckFrom + (NeckTo - NeckFrom) * t, out double bx, out double by, out _);
                    double len = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                    float r = (float)(0.0016 - t * 0.0005);
                    list[i] = new StringLayout(i, MidiOf(i), new Vector3((float)ax, (float)ay, 0f), new Vector3((float)bx, (float)by, 0f), (float)len, r, ColourOf(i));
                }
                s_strings = list;
                return list;
            }
        }

        /// <summary>PlayPoint(i): where a seated player plucks string i (harp-local), lessons' capsule and glow.</summary>
        public static Vector3 PlayPoint(int i)
        {
            if (i < 0 || i >= StringCount) return Vector3.zero;
            StringLayout s = Strings[i];
            float u = Mathf.Clamp((PlayY - s.A.y) / (s.B.y - s.A.y), PlayUMin, PlayUMax);
            return Vector3.LerpUnclamped(s.A, s.B, u);
        }

        /// <summary>Mean x of the string midpoints: the single-mode home's x (spec 08 section 5, FRONT .45).</summary>
        public static float MeanStringMidX()
        {
            double sum = 0;
            StringLayout[] s = Strings;
            for (int i = 0; i < s.Length; i++) sum += (s[i].A.x + s[i].B.x) * 0.5;
            return (float)(sum / s.Length);
        }

        // ---------------- frame parts (frame, then the pins) ----------------

        static Vector3 V(double x, double y, double z = 0) => new Vector3((float)x, (float)y, (float)z);

        /// <summary>The soundboard's outward unit normal in the plane, away from the strings: (dir.y, -dir.x) / |dir|.</summary>
        public static Vector3 Out
        {
            get
            {
                double dx = Sb1X - Sb0X, dy = Sb1Y - Sb0Y, l = Math.Sqrt(dx * dx + dy * dy);
                return V(dy / l, -dx / l);
            }
        }

        /// <summary>Soundbox axis ends: sb0 + .093 out and sb1 + .057 out.</summary>
        public static void SoundboxAxis(out Vector3 c0, out Vector3 c1)
        {
            Vector3 o = Out;
            c0 = V(Sb0X, Sb0Y) + o * 0.093f;
            c1 = V(Sb1X, Sb1Y) + o * 0.057f;
        }

        /// <summary>The right-handed rotation of a cylinderBetween part: +y onto the unit direction.</summary>
        static Quaternion Along(Vector3 a, Vector3 b) => PrimitiveMeshes.FromUnitVectors(Vector3.up, (b - a).normalized);

        /// <summary>Every frame part (soundbox, soundboard, rib, pillar, cap, neck, base, 4 feet, pins), in their
        /// scene order, converted to Unity. New data on every call (meshes are built once by the instrument).</summary>
        public static HarpPart[] BuildFrame()
        {
            var parts = new HarpPart[12];
            int n = 0;
            Vector3 sb0 = V(Sb0X, Sb0Y), sb1 = V(Sb1X, Sb1Y), o = Out;

            // Soundbox: a flat-shaded five-sided frustum behind the soundboard (outside the strings, as on a real harp,
            // so from the seat it never stands in front of a string); a flat face looks at the strings.
            SoundboxAxis(out Vector3 c0, out Vector3 c1);
            Vector3 axis = c1 - c0;
            Quaternion qBox = PrimitiveMeshes.FromUnitVectors(Vector3.up, axis.normalized) * PrimitiveMeshes.AxisAngle(Vector3.up, Math.PI / 10);
            parts[n++] = Part("Soundbox", HarpWood.Maple, c0 + axis * 0.5f, qBox, Vector3.one,
                PrimitiveMeshes.Cylinder(0.055f, 0.1f, axis.magnitude, 5, 1).ToNonIndexed().ComputeVertexNormals(), true);

            // Soundboard strip (spruce): wide across the string plane and thin within it, so the strings come out of
            // its middle; a cylinder (.011, .016, h, 8 sides) scaled 4x through the plane.
            Vector3 ba = sb0 + V(0, -0.04) + o * 0.012f, bb = sb1 + V(0, 0.03) + o * 0.012f;
            parts[n++] = Part("Soundboard", HarpWood.Spruce, (ba + bb) * 0.5f, Along(ba, bb), new Vector3(1f, 1f, 4f),
                PrimitiveMeshes.Cylinder(0.011f, 0.016f, (bb - ba).magnitude, 8, 1), false);

            // Walnut string rib along the soundboard line.
            parts[n++] = Part("Rib", HarpWood.Walnut, (sb0 + sb1) * 0.5f, Along(sb0, sb1), Vector3.one,
                PrimitiveMeshes.Cylinder(0.008f, 0.008f, (sb1 - sb0).magnitude, 8, 1), false);

            // Pillar and its cap.
            Vector3 pa = V(-0.7, 0.02), pb = V(-0.68, 1.3);
            parts[n++] = Part("Pillar", HarpWood.Maple, (pa + pb) * 0.5f, Along(pa, pb), Vector3.one,
                PrimitiveMeshes.Cylinder(0.035f, 0.035f, (pb - pa).magnitude, 16, 1), true);
            parts[n++] = Part("Cap", HarpWood.Maple, V(-0.68, 1.31), Quaternion.identity, Vector3.one, PrimitiveMeshes.Sphere(0.045f, 20, 16), false);

            // Neck: full thickness over the crown, slimmer toward the treble end where it meets the soundbox, so it
            // does not swallow the tops of the short strings.
            parts[n++] = Part("Neck", HarpWood.Maple, Vector3.zero, Quaternion.identity, Vector3.one, NeckMesh(), true);

            // Base and feet (walnut).
            parts[n++] = Part("Base", HarpWood.Walnut, V(-0.33, 0.04), Quaternion.identity, Vector3.one, PrimitiveMeshes.Box(0.95f, 0.04f, 0.22f), true);
            foreach (double x in new[] { -0.72, 0.05 })
                foreach (double z in new[] { -0.09, 0.09 })
                    parts[n++] = Part("Foot", HarpWood.Walnut, V(x, 0.02, z), Quaternion.identity, Vector3.one, PrimitiveMeshes.Box(0.06f, 0.04f, 0.04f), false);

            // Brass tuning pins, one merged mesh: a cylinder (.004, .004, .02, 8 sides) turned onto z at each string top,
            // right-handed z +0.03 (Unity -0.03).
            var pins = new MeshData();
            StringLayout[] strings = Strings;
            for (int i = 0; i < strings.Length; i++)
                pins.Append(PrimitiveMeshes.Cylinder(0.004f, 0.004f, 0.02f, 8, 1).RotateX(Math.PI / 2).Translate(new Vector3(strings[i].B.x, strings[i].B.y, 0.03f)));
            parts[n++] = Part("Pins", HarpWood.Brass, Vector3.zero, Quaternion.identity, Vector3.one, pins, false);

            for (int i = 0; i < parts.Length; i++) parts[i].Name = parts[i].Name == "Foot" ? "Foot" + (i - 7) : parts[i].Name;
            return parts;
        }

        /// <summary>The neck tube with its radius taper, then smooth normals, in the right-handed frame.</summary>
        public static MeshData NeckMesh()
        {
            CatmullRomCurve neck = Neck;
            MeshData g = PrimitiveMeshes.Tube(neck, NeckTubular, NeckRadius, NeckRadial);
            for (int i = 0; i <= NeckTubular; i++)
            {
                double t = (double)i / NeckTubular;
                neck.GetPointAt(t, out double cx, out double cy, out double cz);
                var c = V(cx, cy, cz);
                float k = NeckTaper(t);
                for (int j = 0; j <= NeckRadial; j++)
                {
                    int at = i * (NeckRadial + 1) + j;
                    g.Positions[at] = (g.Positions[at] - c) * k + c;
                }
            }
            return g.ComputeVertexNormals();
        }

        /// <summary>1 - .4 clamp((t - .55) / .4, 0, 1): the neck's radius factor at arc-length fraction t.</summary>
        public static float NeckTaper(double t) => (float)(1 - 0.4 * Math.Min(1, Math.Max(0, (t - 0.55) / 0.4)));

        static HarpPart Part(string name, HarpWood wood, Vector3 rhPos, Quaternion rhRot, Vector3 scale, MeshData rhMesh, bool shadows)
        {
            return new HarpPart
            {
                Name = name,
                Material = wood,
                LocalPosition = Coords.FromRightHanded(rhPos),
                LocalRotation = Coords.FromRightHanded(rhRot),
                LocalScale = scale,
                Mesh = rhMesh.ToUnity(),
                CastShadows = shadows,
            };
        }

        /// <summary>One string's mesh: an open cylinder (r, r, len, 5 sides, SEGMENTS rows) in Unity space (its local y runs
        /// from a at -len/2 to b at +len/2, x lies in the plane across it, z is the harp's z) and the transform that puts it
        /// between a and b. Vertex rows go from b (row 0, u = 1) to a (row SEGMENTS, u = 0), 6 vertices per row.</summary>
        public static MeshData StringMesh(int i, out Vector3 localPosition, out Quaternion localRotation)
        {
            StringLayout s = Strings[i];
            localPosition = (s.A + s.B) * 0.5f;     // z = 0: the same in both spaces
            localRotation = Coords.FromRightHanded(Along(s.A, s.B)); // a rotation about z: unchanged by the conversion
            return PrimitiveMeshes.Cylinder(s.Radius, s.Radius, s.Length, StringSides, Segments, true).ToUnity();
        }
    }
}
