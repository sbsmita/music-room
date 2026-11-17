// The snow field (spec 07 section 5.4): the flakes' seed mesh for the MusicRoom/Snow shader, its material parameters,
// the volume bounds and FlakeAt, the vertex shader's maths on the CPU (EditMode tests check it against the reference
// flakeAt). Frame: the window's, origin at the glass centre, x along the window, y up, +z OUTWARD (source frame -z).
// Seeds come from mulberry32(seed) in the reference order: position xyz, then size/sway/rate/phase, then tone.
using MusicRoom.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Room.Decor
{
    public struct SnowFieldParams
    {
        public int Count;
        public Vector2 Rect;                     // half-size of the window rectangle seen through
        public float DepthNear, DepthFar, DepthBias;
        public Vector2 Lateral, Low, High, Fade, Size;
        public Color ColorLinear, HazeLinear;
        public float HazeAmount;
        public uint Seed;

        /// <summary>The VR room's window (1.8 x 1.7 at y 1.55).</summary>
        public static SnowFieldParams Room => new SnowFieldParams
        {
            Count = 6000, Rect = new Vector2(1.8f / 2f + 0.04f, 1.7f / 2f + 0.04f), DepthNear = 0.1f, DepthFar = 15f, DepthBias = 1.6f,
            Lateral = new Vector2(1.4f, 0.9f), Low = new Vector2(-1.55f - 0.05f, 0f), High = new Vector2(1.3f, 0.9f), Fade = new Vector2(10f, 18.5f),
            Size = new Vector2(0.007f, 0.022f), ColorLinear = TextureUtil.Hex("#c8d4f0").linear, HazeLinear = TextureUtil.Hex("#2a3554").linear,
            HazeAmount = 0.6f, Seed = 7,
        };

        /// <summary>A portal field (spec 07 section 9, snow.portalField): snow behind a real window w x h (mixed reality),
        /// daylight-white flakes, no night haze, seed 11 + the number of portals already made.</summary>
        public static SnowFieldParams Portal(Vector2 size, uint seed)
        {
            var p = new SnowFieldParams
            {
                Count = 3000, DepthNear = 0.06f, DepthFar = 8f, DepthBias = 1.5f, Fade = new Vector2(5.5f, 9f),
                Size = new Vector2(0.008f, 0.024f), ColorLinear = TextureUtil.Hex("#f4f7ff").linear, HazeLinear = Color.black,
                HazeAmount = 0f, Seed = seed,
            };
            p.FitPortal(size);
            return p;
        }

        /// <summary>Portal size: the rectangle and the volume around it follow the window's size.</summary>
        public void FitPortal(Vector2 size)
        {
            Rect = new Vector2(size.x / 2f, size.y / 2f);
            Lateral = new Vector2(size.x / 2f + 0.4f, 1f);
            Low = new Vector2(-(size.y / 2f + 0.6f), -1f);
            High = new Vector2(size.y / 2f + 0.6f, 1f);
        }
    }

    public sealed class SnowField
    {
        public static readonly int VolId = Shader.PropertyToID("_Vol"), LatId = Shader.PropertyToID("_Lat"), LowId = Shader.PropertyToID("_Low"),
            HighId = Shader.PropertyToID("_High"), RectId = Shader.PropertyToID("_Rect"), FadeId = Shader.PropertyToID("_Fade"),
            SizeId = Shader.PropertyToID("_Size"), ColorId = Shader.PropertyToID("_Color"), HazeId = Shader.PropertyToID("_Haze"),
            HazeAmtId = Shader.PropertyToID("_HazeAmt");

        public readonly SnowFieldParams P;
        /// <summary>Per flake: position seeds (x, y, z), seed4 (size, sway, rate, phase), tone.</summary>
        public readonly Vector3[] Pos;
        public readonly Vector4[] Seed4;
        public readonly float[] Tone;

        public SnowField(in SnowFieldParams p)
        {
            P = p;
            Pos = new Vector3[p.Count];
            Seed4 = new Vector4[p.Count];
            Tone = new float[p.Count];
            var rnd = new Mulberry32(p.Seed);
            for (int i = 0; i < p.Count; i++)
            {
                // Float32 seed storage: the doubles are rounded to float.
                Pos[i] = new Vector3((float)rnd.Next(), (float)rnd.Next(), (float)rnd.Next());
                Seed4[i] = new Vector4((float)rnd.Next(), (float)rnd.Next(), (float)rnd.Next(), (float)rnd.Next());
                Tone[i] = (float)rnd.Next();
            }
        }

        /// <summary>4 vertices per flake (POSITION = seeds, TEXCOORD0 = corner + tone, TEXCOORD1 = seed4), bounds = the volume.</summary>
        public Mesh BuildMesh(string name = "SnowField")
        {
            int n = P.Count;
            var pos = new Vector3[n * 4];
            var uv0 = new Vector4[n * 4];
            var uv1 = new Vector4[n * 4];
            var tris = new int[n * 6];
            for (int i = 0; i < n; i++)
            {
                for (int c = 0; c < 4; c++)
                {
                    int v = i * 4 + c;
                    pos[v] = Pos[i];
                    uv0[v] = new Vector4(c == 1 || c == 2 ? 1f : 0f, c >= 2 ? 1f : 0f, Tone[i], 0f);
                    uv1[v] = Seed4[i];
                }
                int b = i * 4, t = i * 6;
                tris[t] = b; tris[t + 1] = b + 2; tris[t + 2] = b + 1;
                tris[t + 3] = b; tris[t + 4] = b + 3; tris[t + 5] = b + 2;
            }
            var m = new Mesh { name = name };
            if (pos.Length > 65535) m.indexFormat = IndexFormat.UInt32;
            m.vertices = pos;
            m.SetUVs(0, uv0);
            m.SetUVs(1, uv1);
            m.triangles = tris;
            m.bounds = VolumeBounds();
            return m;
        }

        /// <summary>The volume bounds in the Unity frame: z in [d0 - .1, d1 + .1] outward, widened by the lateral growth.</summary>
        public Bounds VolumeBounds() => VolumeBounds(P);

        public static Bounds VolumeBounds(in SnowFieldParams P)
        {
            var b = new Bounds();
            bool first = true;
            foreach (float d in new[] { P.DepthNear, P.DepthFar })
            {
                float hw = P.Lateral.x + P.Lateral.y * d + 0.15f;
                var lo = new Vector3(-hw, P.Low.x + P.Low.y * d, d - 0.1f);
                var hi = new Vector3(hw, P.High.x + P.High.y * d, d + 0.1f);
                if (first) { b = new Bounds(lo, Vector3.zero); first = false; } else b.Encapsulate(lo);
                b.Encapsulate(hi);
            }
            return b;
        }

        public void Apply(Material m) => Apply(m, P);

        /// <summary>The per-field material properties (the shader's uniforms; colours as linear vectors).</summary>
        public static void Apply(Material m, in SnowFieldParams P)
        {
            m.SetVector(VolId, new Vector4(P.DepthNear, P.DepthFar, P.DepthBias, 0f));
            m.SetVector(LatId, P.Lateral);
            m.SetVector(LowId, P.Low);
            m.SetVector(HighId, P.High);
            m.SetVector(RectId, P.Rect);
            m.SetVector(FadeId, P.Fade);
            m.SetVector(SizeId, P.Size);
            m.SetVector(ColorId, new Vector4(P.ColorLinear.r, P.ColorLinear.g, P.ColorLinear.b, 1f));
            m.SetVector(HazeId, new Vector4(P.HazeLinear.r, P.HazeLinear.g, P.HazeLinear.b, 1f));
            m.SetFloat(HazeAmtId, P.HazeAmount);
        }

        public struct Flake
        {
            public Vector3 Position;   // Unity window frame (+z outward)
            public float Fall, Xn, Yn;
            public bool Seen;
        }

        static double Fract(double x) => x - System.Math.Floor(x);

        /// <summary>flakeAt in double, as the reference computes it; camLocal = the eye in the window frame (Unity: the room side is -z).</summary>
        public Flake FlakeAt(int i, double time, double wind, double windVel, Vector3? camLocal = null)
        {
            double px = Pos[i].x, py = Pos[i].y, pz = Pos[i].z;
            double sizeN = Seed4[i].x, sway = Seed4[i].y, rate = Seed4[i].z, phase = Seed4[i].w;
            double fall = 0.5 + 0.7 * (0.75 * sizeN + 0.25 * Tone[i]);
            double d = P.DepthNear + (P.DepthFar - P.DepthNear) * System.Math.Pow(pz, P.DepthBias);
            double span = 2 * (P.Lateral.x + P.Lateral.y * d);
            double lag = Fract(px + wind / span) * span / SnowWeather.GustFront;
            double xn = Fract(px + (wind - windVel * lag) / span);
            double x = (xn - 0.5) * span;
            double yLo = P.Low.x + P.Low.y * d;
            double H = P.High.x + P.High.y * d - yLo;
            double yn = Fract(py - fall * time / H);
            double y = yLo + yn * H;
            double ph = phase * System.Math.PI * 2;
            double st = time * (0.7 + 1.4 * rate) + ph;
            double A = (0.02 + 0.1 * sway) * (1.25 - 0.5 * sizeN);
            x += A * System.Math.Sin(st);
            double dd = System.Math.Max(d + 0.6 * A * System.Math.Cos(st), P.DepthNear * 0.5);
            var f = new Flake { Position = new Vector3((float)x, (float)y, (float)dd), Fall = (float)fall, Xn = (float)xn, Yn = (float)yn };
            if (camLocal.HasValue)
            {
                Vector3 cam = camLocal.Value;
                double c = -cam.z;                               // eye distance in front of the glass
                double k = c / System.Math.Max(c + dd, 1e-4);
                double hx = cam.x + (x - cam.x) * k, hy = cam.y + (y - cam.y) * k;
                f.Seen = c > 0.02 && dd > 0.02 && System.Math.Abs(hx) < P.Rect.x && System.Math.Abs(hy) < P.Rect.y;
            }
            return f;
        }
    }
}
