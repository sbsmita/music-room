// The bowls rig in the scene (bowls.md 5.3-5.6): the GameObjects, their materials and what moves when played.
// At the seat: 5 draws (the three bowls on one Bronze material, each its own transform so it can rock; the stand with the
// cushions, and the puja, both on the one Wood atlas). On a real table in MR the stand hides: its renderer swaps to the
// cushions-only mesh, and the puja, a renderer of its own, stays where it is (in the hand or on the table).
// On display (the window sill, no stand): 2 draws (the bowls as one static mesh, the cushions with the puja in front).
// What moves (physical, no glow): a struck bowl rocks on its cushion, A e^(-t/0.25 s) sin(2 pi 3.2 Hz t) about the
// horizontal axis perpendicular to the strike, A = min(1.5 deg, 0.4 deg x V) (x2 for a palm); a rest or a palm stops it.
// The held puja's transform follows the hand (no mesh rewrite).
// Playback of a rub (the take or the loop replays a bowl being sung): no hand and no stick to see, so the bowl sways the way
// a rubbed bowl leans from the moving stick: a steady tilt of up to HumMaxDeg x the replayed level, away from the rub point,
// that travels round with it (the recorded angle, advanced at the recorded angular speed), easing on and off over HumTau.
// Main thread; Tick allocates nothing.
using MusicRoom.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusicRoom.Bowls
{
    public sealed class BowlsVisuals
    {
        public const float RockHz = 3.2f, RockTau = 0.25f, RockMaxDeg = 1.5f, RockPerMs = 0.4f;
        /// <summary>The playback rub's sway: the most it tilts at full level (deg) and its ease (s).</summary>
        public const float HumMaxDeg = 0.6f, HumTau = 0.12f;

        public readonly GameObject Root;
        public readonly Transform[] Bowl = new Transform[3];
        public readonly MeshRenderer[] BowlRenderers = new MeshRenderer[3];
        public readonly MeshRenderer StandRenderer, PujaRenderer, DisplayBowlsRenderer, DisplayCushionsRenderer;
        public readonly MeshFilter StandFilter;
        public readonly Transform Puja;
        public readonly GameObject Seat, Stowed;
        public readonly Material BronzeMat, WoodMat;
        readonly Texture2D[] _textures;
        readonly Mesh[] _meshes;
        readonly Mesh _standAndCushions, _cushionsOnly;
        bool _onSurface;

        // rocking state per bowl
        readonly float[] _rockA = new float[3], _rockT = new float[3];
        readonly Vector3[] _rockAxis = new Vector3[3];
        readonly Quaternion[] _rest = new Quaternion[3];
        // the playback rub's sway per bowl: the target (level, rub angle, angular speed) and the eased tilt vector (bowl frame xz)
        readonly float[] _humLevel = new float[3], _humAngle = new float[3], _humOmega = new float[3];
        readonly Vector2[] _hum = new Vector2[3];

        public BowlsVisuals(Transform parent, MaterialLibrary lib, string name = "Bowls")
        {
            Root = new GameObject(name);
            if (parent != null) Root.transform.SetParent(parent, false);
            BowlsMaterials.Create(lib, out BronzeMat, out WoodMat, out _textures);
            var meshes = new System.Collections.Generic.List<Mesh>();

            // ---- at the seat
            Seat = new GameObject("AtSeat"); Seat.transform.SetParent(Root.transform, false);
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("Bowl" + i + " " + BowlsLayout.Note[i]);
                go.transform.SetParent(Seat.transform, false);
                go.transform.localPosition = BowlsLayout.BaseLocal(i) - new Vector3(0, 0.001f, 0);   // sunk 1 mm into the cushion
                // each bowl turned a little (its hammer pattern and engraving do not line up with its neighbours'): the turn
                // is baked into the mesh, so the transform stays axis-aligned and its bounds stay the bowl's own square
                // (the Layout's placement solver measures the footprint from the renderers' bounds)
                _rest[i] = Quaternion.identity;
                go.transform.localRotation = _rest[i];
                var mesh = BowlsGeometry.Bowl(i); meshes.Add(mesh);
                Turn(mesh, Turned(i));
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = BronzeMat;
                mr.shadowCastingMode = ShadowCastingMode.On; mr.receiveShadows = true;
                Bowl[i] = go.transform; BowlRenderers[i] = mr;
            }
            {
                var go = new GameObject("StandAndCushions"); go.transform.SetParent(Seat.transform, false);
                _standAndCushions = BowlsGeometry.StandAndCushions(); meshes.Add(_standAndCushions);
                _cushionsOnly = BowlsGeometry.CushionsOnly(); meshes.Add(_cushionsOnly);
                StandFilter = go.AddComponent<MeshFilter>(); StandFilter.sharedMesh = _standAndCushions;
                StandRenderer = go.AddComponent<MeshRenderer>(); StandRenderer.sharedMaterial = WoodMat;
            }
            {
                var go = new GameObject("Puja"); go.transform.SetParent(Seat.transform, false);
                var mesh = BowlsGeometry.PujaMesh(); meshes.Add(mesh);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                PujaRenderer = go.AddComponent<MeshRenderer>(); PujaRenderer.sharedMaterial = WoodMat;
                Puja = go.transform;
            }

            // ---- on display (the window sill): bowls in a row on their cushions, the puja in front; no stand
            Stowed = new GameObject("OnDisplay"); Stowed.transform.SetParent(Root.transform, false);
            {
                var combine = new CombineInstance[3];
                for (int i = 0; i < 3; i++) combine[i] = new CombineInstance { mesh = BowlsGeometry.Bowl(i, 20), transform = Matrix4x4.TRS(new Vector3(BowlsLayout.StowX[i], BowlsLayout.CushionLift - 0.001f, 0), Turned(i), Vector3.one) };
                var m = new Mesh { name = "Bowls/BowlsStowed" }; m.CombineMeshes(combine, true, true);
                foreach (var c in combine) Object.Destroy(c.mesh);
                meshes.Add(m);
                var go = new GameObject("Bowls"); go.transform.SetParent(Stowed.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                DisplayBowlsRenderer = go.AddComponent<MeshRenderer>(); DisplayBowlsRenderer.sharedMaterial = BronzeMat;
                var cm = BowlsGeometry.DisplayCushionsAndPuja(); meshes.Add(cm);
                var cg = new GameObject("CushionsAndPuja"); cg.transform.SetParent(Stowed.transform, false);
                cg.AddComponent<MeshFilter>().sharedMesh = cm;
                DisplayCushionsRenderer = cg.AddComponent<MeshRenderer>(); DisplayCushionsRenderer.sharedMaterial = WoodMat;
            }
            Stowed.SetActive(false);
            _meshes = meshes.ToArray();
            SetPujaPose(null);
        }

        /// <summary>Bowl i's own turn about its axis (37 + 113 i degrees).</summary>
        public static Quaternion Turned(int i) => Quaternion.Euler(0, 37f + 113f * i, 0);

        static void Turn(Mesh m, Quaternion q)
        {
            var v = m.vertices; var n = m.normals; var t = m.tangents;
            for (int k = 0; k < v.Length; k++) v[k] = q * v[k];
            for (int k = 0; k < n.Length; k++) n[k] = q * n[k];
            for (int k = 0; k < t.Length; k++) { Vector3 d = q * new Vector3(t[k].x, t[k].y, t[k].z); t[k] = new Vector4(d.x, d.y, d.z, t[k].w); }
            m.vertices = v; m.normals = n; if (t.Length == v.Length) m.tangents = t;
            m.RecalculateBounds();
        }

        public bool IsStowed => Stowed.activeSelf;
        public bool OnSurface => _onSurface;

        /// <summary>IStowable.SetStowed's visual part: the sill row (no stand) or the seat rig.</summary>
        public void SetStowed(bool stowed)
        {
            Stowed.SetActive(stowed); Seat.SetActive(!stowed);
            if (stowed) { for (int i = 0; i < 3; i++) { StopRocking(i); _humLevel[i] = 0f; _humOmega[i] = 0f; _hum[i] = Vector2.zero; } SetPujaPose(null); }
        }

        /// <summary>IInstrumentSurface: on a real table (MR) the stand hides and the cushions stay (the same renderer, its
        /// cushions-only mesh); the bowls and the puja are untouched.</summary>
        public void SetOnSurface(bool onSurface)
        {
            _onSurface = onSurface;
            StandFilter.sharedMesh = onSurface ? _cushionsOnly : _standAndCushions;
        }

        /// <summary>A strike on bowl i at angle (bowl frame, radians from +x toward +z) and speed V.</summary>
        public void Strike(int i, float angle, float V, bool palm)
        {
            float a = Mathf.Min(RockMaxDeg, RockPerMs * V) * (palm ? 2f : 1f);
            a = Mathf.Min(a, RockMaxDeg * 2f);
            if (a < _rockA[i] * Mathf.Exp(-_rockT[i] / RockTau)) return;   // a softer strike does not restart a bigger rock
            // the bowl tilts away from the blow: about the horizontal axis perpendicular to the strike direction
            Vector3 dir = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            _rockAxis[i] = Vector3.Cross(Vector3.up, dir).normalized;
            _rockA[i] = a; _rockT[i] = 0f;
        }

        /// <summary>A resting hand or a palm stops the rocking at once.</summary>
        public void StopRocking(int i)
        {
            _rockA[i] = 0f;
            if (Bowl[i] != null) Bowl[i].localRotation = _rest[i];
        }

        public bool Rocking(int i) => _rockA[i] > 0f;

        /// <summary>A replayed rub on bowl i: level 0..1 (0 lets it settle), the rub point's angle (bowl frame, radians from
        /// +x toward +z, as Strike) and its angular speed (rad/s) to carry it round between the recorded controls.</summary>
        public void Hum(int i, float level, float angle, float omega)
        {
            if ((uint)i >= 3u) return;
            _humLevel[i] = Mathf.Clamp01(level);
            _humAngle[i] = angle;
            _humOmega[i] = level > 0f ? omega : 0f;
        }

        /// <summary>Every bowl's playback sway eases off (the take stopped, the bowls left the seat).</summary>
        public void StopHum()
        {
            for (int i = 0; i < 3; i++) { _humLevel[i] = 0f; _humOmega[i] = 0f; }
        }

        /// <summary>The playback sway now (deg, tests).</summary>
        public float HumDegrees(int i) => (uint)i < 3u ? _hum[i].magnitude * HumMaxDeg : 0f;
        public float RockDegrees(int i) => _rockA[i] * Mathf.Exp(-_rockT[i] / RockTau) * Mathf.Sin(2 * Mathf.PI * RockHz * _rockT[i]);

        public void Tick(float dt)
        {
            float k = dt > 0f ? 1f - Mathf.Exp(-dt / HumTau) : 0f;
            for (int i = 0; i < 3; i++)
            {
                bool humming = HumStep(i, dt, k);
                if (_rockA[i] > 0f)
                {
                    _rockT[i] += dt;
                    float env = Mathf.Exp(-_rockT[i] / RockTau);
                    if (env * _rockA[i] < 0.005f) StopRocking(i);
                }
                bool rocking = _rockA[i] > 0f;
                if (!rocking && !humming) continue;
                Quaternion q = _rest[i];
                if (humming)
                {
                    // tilt away from the rub point: about the horizontal axis perpendicular to it (Strike's rule)
                    Vector2 h = _hum[i];
                    float m = h.magnitude;
                    Vector3 axis = Vector3.Cross(Vector3.up, new Vector3(h.x / m, 0f, h.y / m));
                    q = Quaternion.AngleAxis(m * HumMaxDeg, axis) * q;
                }
                if (rocking) q = Quaternion.AngleAxis(RockDegrees(i), _rockAxis[i]) * q;
                Bowl[i].localRotation = q;
            }
        }

        /// <summary>One frame of bowl i's playback sway; true while it tilts (it rests exactly once it has eased off).</summary>
        bool HumStep(int i, float dt, float k)
        {
            if (_humLevel[i] <= 0f && _hum[i] == Vector2.zero) return false;
            _humAngle[i] += _humOmega[i] * dt;
            Vector2 target = _humLevel[i] * new Vector2(Mathf.Cos(_humAngle[i]), Mathf.Sin(_humAngle[i]));
            _hum[i] += (target - _hum[i]) * k;
            if (_humLevel[i] <= 0f && _hum[i].sqrMagnitude < 1e-4f)
            {
                _hum[i] = Vector2.zero;
                if (_rockA[i] <= 0f && Bowl[i] != null) Bowl[i].localRotation = _rest[i];
                return false;
            }
            return _hum[i] != Vector2.zero;
        }

        /// <summary>The puja's pose in the rig frame (null = at rest on the stand). Moves its transform only.</summary>
        public void SetPujaPose(Matrix4x4? rigLocal)
        {
            Matrix4x4 m = rigLocal ?? BowlsGeometry.PujaRestMatrix();
            Puja.localPosition = m.GetColumn(3);
            Puja.localRotation = m.rotation;
        }

        public int Triangles(bool stowed)
        {
            int n = 0;
            if (stowed)
            {
                n += DisplayBowlsRenderer.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3;
                n += DisplayCushionsRenderer.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3;
                return n;
            }
            for (int i = 0; i < 3; i++) n += BowlRenderers[i].GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3;
            n += _standAndCushions.triangles.Length / 3;
            n += PujaRenderer.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3;
            return n;
        }

        public void Destroy()
        {
            foreach (var m in _meshes) if (m != null) Object.Destroy(m);
            foreach (var x in _textures) if (x != null) Object.Destroy(x);
            Object.Destroy(BronzeMat); Object.Destroy(WoodMat);
            Object.Destroy(Root);
        }
    }
}
