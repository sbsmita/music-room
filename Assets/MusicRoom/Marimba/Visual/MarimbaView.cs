// The marimba's scene objects and their physical feedback (spec 4.2-4.4): five draws at the seat (the 37 bars in one
// dynamic mesh, the frame wood, the metal, the two mallets), three on display (bars, frame + both mallets combined,
// metal). A struck bar dips on its cord and rocks back (1.6 mm x vel on the hit frame, a spring at 11 Hz with damping
// 0.55 in 2 ms substeps, a roll of 0.6 deg x c x vel about the cord line); a lesson target lifts 3 mm x k. Only a moving
// bar's vertex range is rewritten (Mesh.SetVertexBufferData), and Tick allocates nothing. Nothing glows: no emission,
// no hover glow; the aiming cue is the platform's contact shadows (E3).
// Hidden handle renderers with live bounds (the drums pattern) stand for the end boards, the rails and the tubes; one thin
// box collider per bar on Layers.PlaySurface takes desktop clicks.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MusicRoom.Core;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MusicRoom.Marimba
{
    public sealed class MarimbaView : IDisposable
    {
        public const float DipPerVel = 0.0016f, RollDegPerVel = 0.6f, LiftPerK = 0.003f;
        public const float SpringHz = 11f, SpringZeta = 0.55f, Substep = 0.002f, HitFrame = 1f / 90f;
        public const int DrawsAtSeat = 5, DrawsDisplay = 3;

        [StructLayout(LayoutKind.Sequential)]
        struct PN { public Vector3 P, N; }

        struct BarMotion
        {
            public float D, Dv, R, Rv;          // dip (m, negative = down) and its rate; roll (deg) and its rate
            public float RampT, RampFromD, RampToD, RampFromR, RampToR;   // the hit frame's sin ramp (RampT < HitFrame while ramping)
            public bool Ramping, Moving, Dirty;
            public float Lift, DrawnLift;
        }

        public GameObject Root { get; private set; }
        public float NaturalTopH { get; }
        public MarimbaVisualSpec.TubePlan[] Tubes { get; private set; }
        public IReadOnlyList<Renderer> Handles => _handles;
        public IReadOnlyList<Renderer> PlayedParts => _played;
        public bool Stowed { get; private set; }
        public int BarVertexCount => _bars.Pos.Length;
        public int TrianglesAtSeat { get; private set; }
        public int TrianglesDisplay { get; private set; }
        public string TriangleReport { get; private set; }
        /// <summary>Renderers that draw in the current mode (one material each: one draw each).</summary>
        public int DrawCalls
        {
            get
            {
                int n = 0;
                foreach (var r in _drawing) if (r != null && r.enabled && r.gameObject.activeInHierarchy && !r.forceRenderingOff) n++;
                return n;
            }
        }

        readonly List<Renderer> _handles = new List<Renderer>(48);
        readonly List<Renderer> _played = new List<Renderer>(3);
        readonly List<Renderer> _drawing = new List<Renderer>(6);
        readonly List<Mesh> _meshes = new List<Mesh>(8);
        readonly List<Material> _materials = new List<Material>(4);
        readonly List<Texture2D> _textures = new List<Texture2D>(3);
        readonly Transform[] _mallets = new Transform[2];
        readonly BoxCollider[] _colliders = new BoxCollider[MarimbaGeometry.Bars];
        readonly BarMotion[] _motion = new BarMotion[MarimbaGeometry.Bars];
        MarimbaBarsData _bars;
        PN[] _pn;
        Mesh _barMesh;
        Renderer _barsR, _frameR, _metalR, _displayR, _displayBarsR, _displayMetalR;
        Renderer[] _seatDraws, _displayDraws;
        readonly Renderer[] _malletR = new Renderer[2];
        int _moving;
        float _accum;

        MarimbaView(float h) { NaturalTopH = h; }

        /// <summary>Builds the marimba under parent (its local frame = MarimbaGeometry's), materials cloned from the
        /// foundation templates through mats.</summary>
        public static MarimbaView Build(Transform parent, MaterialLibrary mats, float naturalTopH = MarimbaGeometry.HDefault)
        {
            var v = new MarimbaView(naturalTopH);
            v.BuildAll(parent, mats);
            return v;
        }

        void BuildAll(Transform parent, MaterialLibrary mats)
        {
            float h = NaturalTopH;
            Root = new GameObject("Marimba");
            if (parent != null) Root.transform.SetParent(parent, false);

            // textures and materials (phase B: the templates Marimba/Rosewood, Marimba/FrameWood, Marimba/Metal)
            Texture2D rose = Keep(MarimbaTextures.Rosewood()), frame = Keep(MarimbaTextures.FrameWood()), metal = Keep(MarimbaTextures.Metal());
            Material roseM = Mat(mats.Lit(Color.white, 0.42f, 0f, rose), "Marimba/Rosewood");
            Material frameM = Mat(mats.Lit(Color.white, 0.30f, 0f, frame), "Marimba/FrameWood");
            Material malletM = Mat(mats.Lit(Color.white, 0.18f, 0f, frame), "Marimba/Mallet");
            Material metalM = Mat(mats.Clone(MaterialLibrary.LitMetal), "Marimba/Metal");
            metalM.SetColor(MaterialLibrary.BaseColor, Color.white);
            metalM.SetTexture(MaterialLibrary.BaseMap, metal);
            // anodised bronze and brass: clearly metal (high metallic, a soft brushed sheen), never mirror-bright, nothing glows
            metalM.SetFloat(MaterialLibrary.Metallic, 0.85f);
            metalM.SetFloat(MaterialLibrary.Smoothness, 0.6f);

            // 1. the bars: one dynamic mesh, position + normal in stream 0 (rewritten per moving bar), uv in stream 1
            _bars = MarimbaBuilder.Bars(h);
            int nv = _bars.Pos.Length;
            _pn = new PN[nv];
            for (int i = 0; i < nv; i++) _pn[i] = new PN { P = _bars.Pos[i], N = _bars.Nrm[i] };
            _barMesh = new Mesh { name = "marimba-bars" };
            _barMesh.MarkDynamic();
            _barMesh.SetVertexBufferParams(nv,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 0),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 1));
            _barMesh.SetVertexBufferData(_pn, 0, 0, nv, 0);
            _barMesh.SetVertexBufferData(_bars.Uv, 0, 0, nv, 1);
            _barMesh.SetIndexBufferParams(_bars.Tris.Length, IndexFormat.UInt32);
            _barMesh.SetIndexBufferData(_bars.Tris, 0, 0, _bars.Tris.Length);
            _barMesh.subMeshCount = 1;
            var bounds = new Bounds(_bars.Pos[0], Vector3.zero);
            foreach (var p in _bars.Pos) bounds.Encapsulate(p);
            bounds.Expand(new Vector3(0.01f, 0.02f, 0.01f));
            _barMesh.SetSubMesh(0, new SubMeshDescriptor(0, _bars.Tris.Length) { bounds = bounds, vertexCount = nv }, MeshUpdateFlags.DontRecalculateBounds);
            _barMesh.bounds = bounds;
            _meshes.Add(_barMesh);
            _barsR = Part("Bars", _barMesh, roseM, true);
            _played.Add(_barsR);

            // 2. frame wood, 3. metal
            Mesh frameMesh = Keep(MarimbaBuilder.Frame(h, out var boxes));
            _frameR = Part("Frame", frameMesh, frameM, true);
            Mesh metalMesh = Keep(MarimbaBuilder.Metal(h, out var tubes));
            Tubes = tubes;
            _metalR = Part("Metal", metalMesh, metalM, true);

            // 4, 5. the mallets on the tray (heads outward, butts 20 mm apart)
            Mesh malletMesh = Keep(MarimbaBuilder.Mallet());
            for (int s = 0; s < 2; s++)
            {
                var t = new GameObject(s == 0 ? "Mallet L" : "Mallet R").transform;
                t.SetParent(Root.transform, false);
                _mallets[s] = t;
                RestMallet(s);
                _malletR[s] = MakeRenderer(t, "mallet", malletMesh, malletM, true);
                _played.Add(_malletR[s]);
            }

            // the display version (spec 4.3): plain bars, the frame with both mallets merged in, 8-sided tubes: 3 draws.
            // Hidden with forceRenderingOff (not SetActive), so every renderer keeps live bounds for Layout in both modes.
            Mesh dBars = Keep(MarimbaBuilder.DisplayBars(h)), dFrame = Keep(MarimbaBuilder.DisplayFrame(h)), dMetal = Keep(MarimbaBuilder.DisplayMetal(tubes));
            _displayBarsR = Part("Display bars", dBars, roseM, true);
            _displayR = Part("Display frame", dFrame, frameM, true);
            _displayMetalR = Part("Display metal", dMetal, metalM, true);
            _seatDraws = new[] { _barsR, _frameR, _metalR, _malletR[0], _malletR[1] };
            _displayDraws = new[] { _displayBarsR, _displayR, _displayMetalR };
            foreach (var r in _displayDraws) r.forceRenderingOff = true;
            _drawing.AddRange(_seatDraws); _drawing.AddRange(_displayDraws);
            TrianglesAtSeat = Tri(_barMesh) + Tri(frameMesh) + Tri(metalMesh) + 2 * Tri(malletMesh);
            TrianglesDisplay = Tri(dBars) + Tri(dFrame) + Tri(dMetal);
            TriangleReport = $"seat: bars {Tri(_barMesh)}, frame {Tri(frameMesh)}, metal {Tri(metalMesh)}, mallets 2 x {Tri(malletMesh)}; " +
                             $"display: bars {Tri(dBars)}, frame + mallets {Tri(dFrame)}, metal {Tri(dMetal)}";

            // hidden handles with live bounds: end boards, rails, tubes
            var handlesRoot = new GameObject("Handles").transform;
            handlesRoot.SetParent(Root.transform, false);
            Mesh cube = Keep(UnitCube());
            foreach (var (c, rot, size) in boxes) _handles.Add(Handle(handlesRoot, "frame", cube, frameM, c, rot, size));
            foreach (var t in tubes)
            {
                var b = new Bounds(t.Path[0], Vector3.zero);
                foreach (var p in t.Path) b.Encapsulate(p);
                b.Expand(2f * t.Radius);
                _handles.Add(Handle(handlesRoot, "tube " + MarimbaGeometry.NoteName(t.Bar), cube, metalM, b.center, Quaternion.identity, b.size));
            }

            // desktop clicks: one thin box per bar on the PlaySurface layer
            var colRoot = new GameObject("Bar colliders").transform;
            colRoot.SetParent(Root.transform, false);
            for (int i = 0; i < MarimbaGeometry.Bars; i++)
            {
                var go = new GameObject("bar " + MarimbaGeometry.NoteName(i)) { layer = Layers.PlaySurface };
                go.transform.SetParent(colRoot, false);
                go.transform.localPosition = MarimbaGeometry.TopCentre(i, h);
                var bc = go.AddComponent<BoxCollider>();
                bc.size = new Vector3(MarimbaGeometry.Width(i), 0.008f, MarimbaGeometry.Length(i));
                bc.center = new Vector3(0f, -0.004f, 0f);
                _colliders[i] = bc;
            }
        }

        static int Tri(Mesh m) { int n = 0; for (int s = 0; s < m.subMeshCount; s++) n += (int)m.GetIndexCount(s) / 3; return n; }

        Texture2D Keep(Texture2D t) { _textures.Add(t); return t; }
        Mesh Keep(Mesh m) { _meshes.Add(m); return m; }
        Material Mat(Material m, string name) { m.name = name; _materials.Add(m); return m; }

        Renderer Part(string name, Mesh mesh, Material mat, bool castShadow) => MakeRenderer(Root.transform, name, mesh, mat, castShadow);

        static Renderer MakeRenderer(Transform parent, string name, Mesh mesh, Material mat, bool castShadow)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = castShadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = true;
            return r;
        }

        static Renderer Handle(Transform parent, string name, Mesh cube, Material mat, Vector3 centre, Quaternion rot, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            go.transform.localRotation = rot;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = cube;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.forceRenderingOff = true;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        static Mesh UnitCube()
        {
            var k = new MarimbaMeshKit();
            k.Box(Vector3.zero, Quaternion.identity, Vector3.one, new UvRegion(0, 1, 0, 1));
            return k.ToMesh("marimba-handle-cube");
        }

        // ------------------------------------------------------------------ the API

        /// <summary>A mallet's transform (0 left, 1 right): local origin at the butt, +y along the shaft to the head.</summary>
        public Transform Mallet(int side) => _mallets[side];

        /// <summary>Lays mallet side back on the tray (its rest pose).</summary>
        public void RestMallet(int side)
        {
            MarimbaGeometry.MalletRest(side, NaturalTopH, out Vector3 butt, out Vector3 axis);
            _mallets[side].localPosition = butt;
            _mallets[side].localRotation = Quaternion.FromToRotation(Vector3.up, axis);
        }

        public BoxCollider BarCollider(int bar) => _colliders[bar];

        /// <summary>The bar's current vertical offset (m): the dip (negative) plus the lesson lift.</summary>
        public float BarOffset(int bar) => _motion[bar].D + _motion[bar].DrawnLift;
        public float BarRollDeg(int bar) => _motion[bar].R;
        public int MovingBars => _moving;

        /// <summary>The struck bar dips on its cord (1.6 mm x vel within the hit frame) and rocks back on its spring; a hit off
        /// the centre line (c, -1..1) adds a roll of 0.6 deg x c x vel.</summary>
        public void Kick(int bar, float vel, float c)
        {
            if ((uint)bar >= MarimbaGeometry.Bars) return;
            vel = Mathf.Clamp01(vel);
            ref BarMotion m = ref _motion[bar];
            m.Ramping = true;
            m.RampT = 0f;
            m.RampFromD = m.D; m.RampToD = Mathf.Min(m.D, -DipPerVel * vel);
            m.RampFromR = m.R; m.RampToR = RollDegPerVel * Mathf.Clamp(c, -1f, 1f) * vel;
            m.Dv = 0f; m.Rv = 0f;
            if (!m.Moving) { m.Moving = true; _moving++; }
        }

        /// <summary>The lesson target cue: lifts the bar 3 mm x k on its cord (k 0..1; no tint).</summary>
        public void SetTargetLift(int bar, float k)
        {
            if ((uint)bar >= MarimbaGeometry.Bars) return;
            ref BarMotion m = ref _motion[bar];
            float lift = LiftPerK * Mathf.Clamp01(k);
            if (Mathf.Abs(lift - m.Lift) < 1e-7f) return;
            m.Lift = lift;
            m.Dirty = true;
        }

        /// <summary>Steps the bar springs and rewrites the vertices of the bars that moved. No allocation.</summary>
        public void Tick(float dt)
        {
            if (_barMesh == null) return;
            if (_moving > 0)
            {
                _accum += Mathf.Min(dt, 0.1f);
                float w = 2f * Mathf.PI * SpringHz, w2 = w * w, z2w = 2f * SpringZeta * w;
                int steps = (int)(_accum / Substep);
                _accum -= steps * Substep;
                for (int i = 0; i < MarimbaGeometry.Bars; i++)
                {
                    ref BarMotion m = ref _motion[i];
                    if (!m.Moving) continue;
                    for (int s = 0; s < steps; s++)
                    {
                        if (m.Ramping)
                        {
                            m.RampT += Substep;
                            float k = Mathf.Sin(0.5f * Mathf.PI * Mathf.Clamp01(m.RampT / HitFrame));
                            m.D = Mathf.Lerp(m.RampFromD, m.RampToD, k);
                            m.R = Mathf.Lerp(m.RampFromR, m.RampToR, k);
                            if (m.RampT >= HitFrame) { m.Ramping = false; m.Dv = 0f; m.Rv = 0f; }
                            continue;
                        }
                        m.Dv += (-w2 * m.D - z2w * m.Dv) * Substep; m.D += m.Dv * Substep;
                        m.Rv += (-w2 * m.R - z2w * m.Rv) * Substep; m.R += m.Rv * Substep;
                    }
                    if (!m.Ramping && Mathf.Abs(m.D) < 2e-6f && Mathf.Abs(m.Dv) < 2e-4f && Mathf.Abs(m.R) < 1e-3f && Mathf.Abs(m.Rv) < 0.1f)
                    {
                        m.D = 0f; m.Dv = 0f; m.R = 0f; m.Rv = 0f;
                        m.Moving = false; _moving--;
                    }
                    m.Dirty = true;
                }
            }
            for (int i = 0; i < MarimbaGeometry.Bars; i++)
            {
                ref BarMotion m = ref _motion[i];
                if (!m.Dirty) continue;
                m.Dirty = false;
                m.DrawnLift = m.Lift;
                WriteBar(i, m.D + m.Lift, m.R);
            }
        }

        void WriteBar(int bar, float dy, float rollDeg)
        {
            int start = _bars.Start[bar], count = _bars.Count[bar];
            Vector3 pv = _bars.Pivot[bar];
            float a = rollDeg * Mathf.Deg2Rad, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            for (int v = start; v < start + count; v++)
            {
                Vector3 p = _bars.Pos[v], n = _bars.Nrm[v];
                float px = p.x - pv.x, py = p.y - pv.y;
                // roll about the bar's long axis (z) through the cord line
                _pn[v].P = new Vector3(pv.x + px * ca - py * sa, pv.y + px * sa + py * ca + dy, p.z);
                _pn[v].N = new Vector3(n.x * ca - n.y * sa, n.x * sa + n.y * ca, n.z);
            }
            _barMesh.SetVertexBufferData(_pn, start, start, count, 0,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds);
        }

        /// <summary>Display (stowed) or at the seat. At the seat: the full model, the mallets their own transforms (5 draws). On
        /// display: the light version of spec 4.3 (plain bars, frame + resting mallets in one mesh, 8-sided tubes; 3 draws,
        /// about 4 k triangles). Stowing lays the mallets back on the tray.</summary>
        public void SetDisplayMode(bool stowed)
        {
            Stowed = stowed;
            if (stowed) { RestMallet(0); RestMallet(1); }
            foreach (var r in _seatDraws) r.forceRenderingOff = stowed;
            foreach (var r in _displayDraws) r.forceRenderingOff = !stowed;
        }

        public void Dispose()
        {
            bool play = Application.isPlaying;
            void Kill(Object o) { if (o == null) return; if (play) Object.Destroy(o); else Object.DestroyImmediate(o); }
            foreach (var m in _meshes) Kill(m);
            foreach (var m in _materials) Kill(m);
            foreach (var t in _textures) Kill(t);
            _meshes.Clear(); _materials.Clear(); _textures.Clear();
            if (Root != null) Kill(Root);
            Root = null;
            _barMesh = null;
        }
    }
}
