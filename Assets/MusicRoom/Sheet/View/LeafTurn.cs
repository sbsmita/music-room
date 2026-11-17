// Page turns (sheet.md 5.1, 5.3, 5.4): one bent leaf, or a riffle of up to three, over the open book. Pure timing and
// geometry: the module asks it to turn from one spread to another; each frame it says which page each face shows (the
// paper shader's slice roles), writes the leaves' vertices and gives the leaf shadow. A forward turn lifts the right page:
// the right page under it shows the new right page at once, the leaf's front the old right page, its back the new left
// page, and the old left page stays until the last leaf lands. A backward turn mirrors it.
//   theta(tau) = pi tau^2 (3 - 2 tau); phi(s) = theta -+ 0.8 sin theta (s / W)^1.5 (the free edge trails);
//   corner-led (a tapped corner): the top rows start 0.10 s later than the bottom corner.
using Unity.Collections;
using UnityEngine;

namespace MusicRoom.Sheet
{
    public enum TurnKind : byte { Manual = 0, Auto, Riffle }

    public sealed class LeafTurn
    {
        public const float ManualSeconds = 0.62f, AutoSeconds = 0.52f, RiffleSeconds = 0.34f, RiffleStagger = 0.085f, CornerLead = 0.10f;

        public bool Active { get; private set; }
        public int FromLeft { get; private set; }
        public int ToLeft { get; private set; }
        public bool Forward { get; private set; }
        public int LeafCount { get; private set; }
        public float LeafSeconds { get; private set; }
        public bool CornerLed { get; private set; }
        public TurnKind Kind { get; private set; }
        public float Time { get; private set; }
        public float Duration => (LeafCount - 1) * RiffleStagger + LeafSeconds;
        /// <summary>Waiting for the destination pages before the leaf moves (at most HoldMax).</summary>
        public bool Holding { get; private set; }
        public const float HoldMax = 0.6f;
        float _held;
        /// <summary>Turns completed (tests).</summary>
        public int Completed { get; private set; }
        /// <summary>Leaves started so far in this turn (the paper sound fires once per leaf).</summary>
        public int LeavesStarted { get; private set; }

        public void Begin(int fromLeft, int toLeft, TurnKind kind, int spreadsApart)
        {
            Active = true;
            FromLeft = fromLeft; ToLeft = toLeft;
            Forward = toLeft > fromLeft;
            Kind = kind;
            CornerLed = kind == TurnKind.Manual;
            if (kind == TurnKind.Riffle || spreadsApart > 1)
            {
                Kind = TurnKind.Riffle;
                LeafCount = Mathf.Clamp(spreadsApart, 2, BookMesh.Leaves);
                LeafSeconds = RiffleSeconds;
                CornerLed = false;
            }
            else
            {
                LeafCount = 1;
                LeafSeconds = kind == TurnKind.Manual ? ManualSeconds : AutoSeconds;
            }
            Time = 0f;
            _held = 0f;
            Holding = true;
            LeavesStarted = 0;
        }

        /// <summary>Advance. destinationReady: the new pages are rastered (else the leaf waits up to HoldMax). Returns true on the
        /// frame the turn ends.</summary>
        public bool Step(float dt, bool destinationReady)
        {
            if (!Active) return false;
            if (Holding)
            {
                _held += dt;
                if (!destinationReady && _held < HoldMax) return false;
                Holding = false;
            }
            Time += dt;
            if (Time >= Duration)
            {
                Active = false;
                Completed++;
                return true;
            }
            return false;
        }

        /// <summary>The time leaf k starts (s after the turn's start), for the paper sound.</summary>
        public float LeafStart(int k) => k * RiffleStagger;
        public bool LeafStarted(int k) => !Holding && Time >= LeafStart(k);
        public void MarkLeafStarted() => LeavesStarted++;

        float Tau(int k, float v01)
        {
            float t = Time - LeafStart(k);
            if (CornerLed)
            {
                float delay = CornerLead * v01;   // the bottom corner leads, the top follows
                return Mathf.Clamp01((t - delay) / Mathf.Max(0.05f, LeafSeconds - CornerLead));
            }
            return Mathf.Clamp01(t / LeafSeconds);
        }

        /// <summary>Leaf k's angle at mid-height (0 on the right page, pi on the left).</summary>
        public float Theta(int k) => !Active || Holding ? (Forward ? 0f : Mathf.PI) : BookGeometry.Theta(Tau(k, 0.5f), Forward);

        /// <summary>Leaf shadow on the right (x) and left (y) page: 0.25 sin theta on the page a leaf is over.</summary>
        public Vector2 Shadow()
        {
            if (!Active || Holding) return Vector2.zero;
            float r = 0f, l = 0f;
            for (int k = 0; k < LeafCount; k++)
            {
                if (Time < LeafStart(k)) continue;
                float th = Theta(k), s = 0.25f * Mathf.Sin(th);
                if (th < Mathf.PI * 0.5f) r = Mathf.Max(r, s); else l = Mathf.Max(l, s);
            }
            return new Vector2(r, l);
        }

        /// <summary>Writes every leaf's vertices (both layers) into `verts` (BookMesh layout); collapsed leaves sit at a point.</summary>
        public void WriteLeaves(NativeArray<PaperVertex> verts, float sBottom)
        {
            int C = BookGeometry.Columns, R = BookGeometry.Rows, layer = BookMesh.VertsPerLayer;
            for (int k = 0; k < BookMesh.Leaves; k++)
            {
                bool live = Active && k < LeafCount && (Holding ? k == 0 : Time >= LeafStart(k));
                int fb = (2 * k) * layer, bb = (2 * k + 1) * layer;
                if (!live && Active && k < LeafCount)
                {
                    // not started yet: lies on its page, under the ones above it (riffle), hidden by the leaf on top
                    live = true;
                }
                if (!live)
                {
                    for (int i = 0; i < 2 * layer; i++) { var v = verts[fb + i]; v.Position = new Vector3(0f, sBottom, 0f); verts[fb + i] = v; }
                    continue;
                }
                float eps = BookMesh.LeafEps * (1f + 0.25f * (LeafCount - 1 - k));
                for (int j = 0; j < R; j++)
                {
                    float v01 = j / (float)(R - 1), y = sBottom + BookGeometry.PageH * v01;
                    float theta = Holding || Time < LeafStart(k) ? (Forward ? 0f : Mathf.PI) : BookGeometry.Theta(Tau(k, v01), Forward);
                    for (int i = 0; i < C; i++)
                    {
                        float s = BookGeometry.ColumnArc(i), d = BookGeometry.FlatReach(s);
                        var p = BookGeometry.LeafPoint(d, BookGeometry.Phi(theta, s, Forward), y, eps);
                        int idx = j * C + i;
                        var a = verts[fb + idx]; a.Position = p; verts[fb + idx] = a;
                        var b = verts[bb + idx]; b.Position = p; verts[bb + idx] = b;
                    }
                }
                // normals by finite differences over the grid (front toward the player at theta = 0)
                for (int j = 0; j < R; j++)
                    for (int i = 0; i < C; i++)
                    {
                        int idx = j * C + i;
                        Vector3 px0 = verts[fb + j * C + Mathf.Max(0, i - 1)].Position, px1 = verts[fb + j * C + Mathf.Min(C - 1, i + 1)].Position;
                        Vector3 py0 = verts[fb + Mathf.Max(0, j - 1) * C + i].Position, py1 = verts[fb + Mathf.Min(R - 1, j + 1) * C + i].Position;
                        Vector3 n = Vector3.Cross(py1 - py0, px1 - px0);
                        float m = n.magnitude;
                        n = m > 1e-9f ? n / m : new Vector3(0f, 0f, -1f);
                        var a = verts[fb + idx]; a.Normal = n; verts[fb + idx] = a;
                        var b = verts[bb + idx]; b.Normal = -n; verts[bb + idx] = b;
                    }
            }
        }

        /// <summary>Which page each paper role shows during (or after) this turn: left, right, leaf k front, leaf k back.</summary>
        public void Faces(int[] pageOfRole)
        {
            int L1 = FromLeft, R1 = FromLeft + 1, L2 = ToLeft, R2 = ToLeft + 1;
            bool landed = !Active;
            if (Forward)
            {
                pageOfRole[0] = landed ? L2 : L1;
                pageOfRole[1] = R2;
                for (int k = 0; k < BookMesh.Leaves; k++) { pageOfRole[2 + 2 * k] = k == 0 ? R1 : R2; pageOfRole[3 + 2 * k] = L2; }
            }
            else
            {
                pageOfRole[0] = L2;
                pageOfRole[1] = landed ? R2 : R1;
                for (int k = 0; k < BookMesh.Leaves; k++) { pageOfRole[3 + 2 * k] = k == 0 ? L1 : L2; pageOfRole[2 + 2 * k] = R2; }
            }
        }
    }
}
