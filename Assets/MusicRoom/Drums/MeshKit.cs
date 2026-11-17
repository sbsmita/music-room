// A small procedural mesh builder for the kit and the sticks (cylinder / circle / torus / box / lathe / sphere parts,
// merged), built natively in Unity coordinates. Every triangle is oriented so its front face agrees with the vertex
// normals, so a sign slip in a generator can never leave a part inside out (a plain lathe of the cymbal profile renders
// inside out: its front faces point into the plate). Build time only: allocates freely.
using System.Collections.Generic;
using UnityEngine;

namespace MusicRoom.Drums
{
    public sealed class MeshKit
    {
        readonly List<Vector3> _v = new List<Vector3>(1024);
        readonly List<Vector3> _n = new List<Vector3>(1024);
        readonly List<Vector2> _uv = new List<Vector2>(1024);
        readonly List<int> _t = new List<int>(4096);
        Matrix4x4 _m = Matrix4x4.identity;
        Matrix4x4 _nm = Matrix4x4.identity;

        public int VertexCount => _v.Count;

        /// <summary>Transform applied to every part added afterwards (position, rotation, scale).</summary>
        public Matrix4x4 Transform
        {
            get => _m;
            set { _m = value; _nm = value.inverse.transpose; }
        }

        public MeshKit At(Vector3 pos, Quaternion rot) { Transform = Matrix4x4.TRS(pos, rot, Vector3.one); return this; }
        public MeshKit Identity() { Transform = Matrix4x4.identity; return this; }

        public void Clear() { _v.Clear(); _n.Clear(); _uv.Clear(); _t.Clear(); Transform = Matrix4x4.identity; }

        int Vert(Vector3 p, Vector3 n, Vector2 uv)
        {
            _v.Add(_m.MultiplyPoint3x4(p));
            Vector3 wn = _nm.MultiplyVector(n);
            _n.Add(wn.sqrMagnitude > 1e-20f ? wn.normalized : Vector3.up);
            _uv.Add(uv);
            return _v.Count - 1;
        }

        /// <summary>A triangle whose winding is chosen so Unity's front face (Cross(b - a, c - a)) points along the normals.</summary>
        void Tri(int a, int b, int c)
        {
            Vector3 face = Vector3.Cross(_v[b] - _v[a], _v[c] - _v[a]);
            Vector3 n = _n[a] + _n[b] + _n[c];
            if (Vector3.Dot(face, n) < 0f) { int t = b; b = c; c = t; }
            _t.Add(a); _t.Add(b); _t.Add(c);
        }

        void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }

        /// <summary>Cylinder (rTop, rBottom, height, radial segments, 1 height segment, openEnded): centred on the origin along +y.</summary>
        public void Cylinder(float rTop, float rBottom, float height, int radial, bool openEnded)
        {
            float h2 = height * 0.5f;
            float slope = (rBottom - rTop) / Mathf.Max(height, 1e-6f);
            int top0 = _v.Count;
            for (int i = 0; i <= radial; i++)
            {
                float u = (float)i / radial, a = u * Mathf.PI * 2f;
                float s = Mathf.Sin(a), c = Mathf.Cos(a);
                Vector3 n = new Vector3(s, slope, c);
                Vert(new Vector3(rTop * s, h2, rTop * c), n, new Vector2(u, 1f));
                Vert(new Vector3(rBottom * s, -h2, rBottom * c), n, new Vector2(u, 0f));
            }
            for (int i = 0; i < radial; i++)
            {
                int t0 = top0 + i * 2, b0 = t0 + 1, t1 = t0 + 2, b1 = t0 + 3;
                Quad(t0, t1, b1, b0);
            }
            if (openEnded) return;
            Cap(rTop, h2, radial, true);
            Cap(rBottom, -h2, radial, false);
        }

        void Cap(float r, float y, int radial, bool up)
        {
            if (r <= 0f) return;
            Vector3 n = up ? Vector3.up : Vector3.down;
            int centre = Vert(new Vector3(0f, y, 0f), n, new Vector2(0.5f, 0.5f));
            int ring0 = _v.Count;
            for (int i = 0; i <= radial; i++)
            {
                float a = (float)i / radial * Mathf.PI * 2f;
                float s = Mathf.Sin(a), c = Mathf.Cos(a);
                Vert(new Vector3(r * s, y, r * c), n, new Vector2(s * 0.5f + 0.5f, c * 0.5f + 0.5f));
            }
            for (int i = 0; i < radial; i++) Tri(centre, ring0 + i, ring0 + i + 1);
        }

        /// <summary>A cylinder of radius r from a to b (closed, segs sides).</summary>
        public void CylinderBetween(Vector3 a, Vector3 b, float r, int segs)
        {
            Vector3 dir = b - a;
            float len = dir.magnitude;
            if (len < 1e-6f) return;
            Matrix4x4 saved = _m;
            Transform = saved * Matrix4x4.TRS(a + dir * 0.5f, Quaternion.FromToRotation(Vector3.up, dir / len), Vector3.one);
            Cylinder(r, r, len, segs, false);
            Transform = saved;
        }

        /// <summary>Circle (r, segs) laid in the XZ plane at height y, facing up or down. UVs: (x/r+1)/2,
        /// (z/r+1)/2 (the drum-head speckle repeats on top of that).</summary>
        public void Disc(float r, int segs, float y, bool up)
        {
            Vector3 n = up ? Vector3.up : Vector3.down;
            int centre = Vert(new Vector3(0f, y, 0f), n, new Vector2(0.5f, 0.5f));
            int ring0 = _v.Count;
            for (int i = 0; i <= segs; i++)
            {
                float a = (float)i / segs * Mathf.PI * 2f;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                Vert(new Vector3(r * c, y, r * s), n, new Vector2(c * 0.5f + 0.5f, s * 0.5f + 0.5f));
            }
            for (int i = 0; i < segs; i++) Tri(centre, ring0 + i, ring0 + i + 1);
        }

        /// <summary>A face-up disc in the XZ plane at height y made of <paramref name="rings"/> concentric rings of
        /// <paramref name="segs"/> vertices around the centre (1 + rings x segs vertices; 6 x 48 = 289), with Disc's planar
        /// UVs, so the head can dent anywhere (E7 HeadGive). No seam vertex: planar UVs are continuous all round.</summary>
        public void RingedDisc(float r, int rings, int segs, float y)
        {
            int centre = Vert(new Vector3(0f, y, 0f), Vector3.up, new Vector2(0.5f, 0.5f));
            int first = _v.Count;
            for (int k = 1; k <= rings; k++)
            {
                float f = (float)k / rings, rr = r * f;
                for (int i = 0; i < segs; i++)
                {
                    float a = (float)i / segs * Mathf.PI * 2f;
                    float c = Mathf.Cos(a), s = Mathf.Sin(a);
                    Vert(new Vector3(rr * c, y, rr * s), Vector3.up, new Vector2(c * f * 0.5f + 0.5f, s * f * 0.5f + 0.5f));
                }
            }
            for (int i = 0; i < segs; i++) Tri(centre, first + i, first + (i + 1) % segs);
            for (int k = 1; k < rings; k++)
            {
                int inner = first + (k - 1) * segs, outer = first + k * segs;
                for (int i = 0; i < segs; i++)
                {
                    int i1 = (i + 1) % segs;
                    Quad(inner + i, outer + i, outer + i1, inner + i1);
                }
            }
        }

        /// <summary>Torus (R, tube, radial, tubular) rotated flat (rotateX(pi/2)): ring in XZ at height y.</summary>
        public void Torus(float R, float tube, int radial, int tubular, float y)
        {
            int start = _v.Count;
            for (int j = 0; j <= radial; j++)
            {
                float v = (float)j / radial * Mathf.PI * 2f;
                float cv = Mathf.Cos(v), sv = Mathf.Sin(v);
                for (int i = 0; i <= tubular; i++)
                {
                    float u = (float)i / tubular * Mathf.PI * 2f;
                    float cu = Mathf.Cos(u), su = Mathf.Sin(u);
                    Vector3 radialDir = new Vector3(cu, 0f, su);
                    Vector3 p = radialDir * (R + tube * cv) + new Vector3(0f, y + tube * sv, 0f);
                    Vector3 n = radialDir * cv + new Vector3(0f, sv, 0f);
                    Vert(p, n, new Vector2((float)i / tubular, (float)j / radial));
                }
            }
            int row = tubular + 1;
            for (int j = 0; j < radial; j++)
                for (int i = 0; i < tubular; i++)
                {
                    int a = start + j * row + i, b = a + 1, c = a + row + 1, d = a + row;
                    Quad(a, b, c, d);
                }
        }

        /// <summary>Box (size.x, size.y, size.z) centred on the origin.</summary>
        public void Box(Vector3 size)
        {
            Vector3 h = size * 0.5f;
            Face(new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 1, 0), h);
            Face(new Vector3(-1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 1, 0), h);
            Face(new Vector3(0, 1, 0), new Vector3(1, 0, 0), new Vector3(0, 0, 1), h);
            Face(new Vector3(0, -1, 0), new Vector3(1, 0, 0), new Vector3(0, 0, 1), h);
            Face(new Vector3(0, 0, 1), new Vector3(1, 0, 0), new Vector3(0, 1, 0), h);
            Face(new Vector3(0, 0, -1), new Vector3(1, 0, 0), new Vector3(0, 1, 0), h);
        }

        void Face(Vector3 n, Vector3 u, Vector3 v, Vector3 h)
        {
            Vector3 c = Vector3.Scale(n, h);
            Vector3 du = Vector3.Scale(u, h), dv = Vector3.Scale(v, h);
            int a = Vert(c - du - dv, n, new Vector2(0, 0));
            int b = Vert(c + du - dv, n, new Vector2(1, 0));
            int cc = Vert(c + du + dv, n, new Vector2(1, 1));
            int d = Vert(c - du + dv, n, new Vector2(0, 1));
            Quad(a, b, cc, d);
        }

        /// <summary>A lathe of the profile with segs segments, UVs u = i/segs, v = j/(n-1) and length-weighted smooth
        /// profile normals, facing OUT of the solid (a plain lathe of this profile faces into it).</summary>
        public void Lathe(Vector2[] profile, int segs)
        {
            int np = profile.Length;
            var pn = new Vector2[np];
            Vector2 prev = Vector2.zero;
            for (int j = 0; j < np; j++)
            {
                if (j == np - 1) { pn[j] = prev.normalized; break; }
                Vector2 d = profile[j + 1] - profile[j];
                Vector2 cur = new Vector2(-d.y, d.x);   // outward for a profile traced centre -> edge over the top
                pn[j] = j == 0 ? cur.normalized : (cur + prev).normalized;
                prev = cur;
            }
            int start = _v.Count;
            for (int i = 0; i <= segs; i++)
            {
                float phi = (float)i / segs * Mathf.PI * 2f;
                float s = Mathf.Sin(phi), c = Mathf.Cos(phi);
                for (int j = 0; j < np; j++)
                {
                    Vector2 p = profile[j];
                    Vert(new Vector3(p.x * s, p.y, p.x * c), new Vector3(pn[j].x * s, pn[j].y, pn[j].x * c), new Vector2((float)i / segs, (float)j / (np - 1)));
                }
            }
            for (int i = 0; i < segs; i++)
                for (int j = 0; j < np - 1; j++)
                {
                    int a = start + j + i * np, b = a + np, cc = b + 1, d = a + 1;
                    Quad(a, b, cc, d);
                }
        }

        /// <summary>Sphere (r, wSegs, hSegs) centred on the origin.</summary>
        public void Sphere(float r, int wSegs, int hSegs)
        {
            int start = _v.Count;
            for (int y = 0; y <= hSegs; y++)
            {
                float v = (float)y / hSegs, theta = v * Mathf.PI;
                for (int x = 0; x <= wSegs; x++)
                {
                    float u = (float)x / wSegs, phi = u * Mathf.PI * 2f;
                    Vector3 n = new Vector3(-Mathf.Cos(phi) * Mathf.Sin(theta), Mathf.Cos(theta), Mathf.Sin(phi) * Mathf.Sin(theta));
                    Vert(n * r, n, new Vector2(u, 1f - v));
                }
            }
            int row = wSegs + 1;
            for (int y = 0; y < hSegs; y++)
                for (int x = 0; x < wSegs; x++)
                {
                    int a = start + y * row + x, b = a + 1, c = a + row + 1, d = a + row;
                    if (y != 0) Tri(a, d, b);
                    if (y != hSegs - 1) Tri(b, d, c);
                }
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (_v.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(_v);
            mesh.SetNormals(_n);
            mesh.SetUVs(0, _uv);
            mesh.SetTriangles(_t, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
