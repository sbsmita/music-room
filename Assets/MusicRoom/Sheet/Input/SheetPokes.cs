// Touching the book (sheet.md 5.2): each tracked hand's index tip (never the mouse hand, never a stick) in page coordinates
// (arc s from the spine, v up the page, n = height above the curled surface). Targets: the bottom outer corner of each page
// (45 x 45 mm, caught from 25 mm beyond the page's outer and bottom edges), the contents rows, the ribbon's tail (a 15 mm
// radius capsule around its last 30 mm on the piano's top panel). The menu's poke rule: armed above n = 3 mm, fires on
// reaching n <= 0 inside the target, never from behind (below -60 mm disarms), once per entry, 400 ms apart. A sideways
// flick also turns a corner: within 15 mm of the page, 40 mm toward the spine at 0.25 m/s or more. Allocation-free.
using MusicRoom.Sheet.Engrave;
using UnityEngine;

namespace MusicRoom.Sheet
{
    public enum PokeTarget : byte { None = 0, CornerRight, CornerLeft, Row, Ribbon }

    public struct PokeHit
    {
        public PokeTarget Target;
        public int Row;           // index into the open spread's contents rows
        public float N;           // height above the surface (m), toward the player
        public int Sigma;         // page side
        public Vector2 PageMm;
    }

    public sealed class SheetPokes
    {
        public const float Arm = 0.003f, Behind = -0.06f, DebounceMs = 400f, HoverRange = 0.06f;
        public const float CornerSize = 0.045f, CornerOut = 0.025f, RibbonRadius = 0.015f;
        public const float FlickBand = 0.015f, FlickDistance = 0.04f, FlickSpeed = 0.25f;
        const float RibbonPanelY = 0.038f + 0.0016f + 0.0006f;

        struct HandState { public bool Armed; public float PrevN; public bool HadPrev; public bool Flick; public int FlickSigma; public float FlickS0; }
        readonly HandState[] _h = new HandState[2];
        double _lastFireMs = double.NegativeInfinity;

        /// <summary>What a book-frame point is over. rows: the open contents rows (page mm) of the left (sigma -1) and right
        /// page, or null.</summary>
        public PokeHit Classify(Vector3 p, float sBottom, ContentsRowSet rows)
        {
            BookGeometry.ToPage(p, sBottom, out int sigma, out float s, out float v, out float n);
            var hit = new PokeHit { Target = PokeTarget.None, Row = -1, N = n, Sigma = sigma, PageMm = BookGeometry.PageMm(sigma, s, v) };
            // the ribbon's tail first: it lies on the piano's top panel, below the book
            Vector3 pp = BookGeometry.BookToPiano(p);
            BookMesh.RibbonTail(out Vector3 ra, out Vector3 rb);   // follows the host's rest pose
            Vector3 a = BookGeometry.BookToPiano(ra), b = BookGeometry.BookToPiano(rb);
            Vector2 q = new Vector2(pp.x, pp.z), qa = new Vector2(a.x, a.z), qb = new Vector2(b.x, b.z);
            Vector2 ab = qb - qa;
            float t = Mathf.Clamp01(Vector2.Dot(q - qa, ab) / Mathf.Max(1e-9f, ab.sqrMagnitude));
            if ((q - (qa + ab * t)).magnitude <= RibbonRadius && pp.y - RibbonPanelY < 0.08f && pp.y - RibbonPanelY > Behind)
            {
                hit.Target = PokeTarget.Ribbon;
                hit.N = pp.y - RibbonPanelY;
                return hit;
            }
            if (s >= BookGeometry.PageW - CornerSize && s <= BookGeometry.PageW + CornerOut && v >= -CornerOut && v <= CornerSize)
            {
                hit.Target = sigma > 0 ? PokeTarget.CornerRight : PokeTarget.CornerLeft;
                return hit;
            }
            if (rows != null && s <= BookGeometry.PageW && v >= 0f && v <= BookGeometry.PageH)
            {
                int r = rows.Find(sigma, hit.PageMm);
                if (r >= 0) { hit.Target = PokeTarget.Row; hit.Row = r; }
            }
            return hit;
        }

        /// <summary>One hand this frame (tip in the book frame, its speed in m/s). Returns the target fired, if any.</summary>
        public PokeTarget Update(int hand, Vector3 tip, float speed, double nowMs, float sBottom, ContentsRowSet rows, out PokeHit hit)
        {
            hit = Classify(tip, sBottom, rows);
            ref var st = ref _h[hand];
            float n = hit.N;
            var fired = PokeTarget.None;
            if (n > Arm) st.Armed = true;
            if (n < Behind) st.Armed = false;
            bool crossed = st.HadPrev && st.PrevN > 0f && n <= 0f && n > Behind;
            if (st.Armed && crossed && hit.Target != PokeTarget.None && nowMs - _lastFireMs >= DebounceMs)
            {
                fired = hit.Target;
                st.Armed = false;
                _lastFireMs = nowMs;
            }
            // the sideways flick from a corner toward the spine
            BookGeometry.ToPage(tip, sBottom, out int sigma, out float s, out _, out float nn);
            if (Mathf.Abs(nn) <= FlickBand && hit.Target != PokeTarget.Ribbon)
            {
                if (!st.Flick && (hit.Target == PokeTarget.CornerLeft || hit.Target == PokeTarget.CornerRight)) { st.Flick = true; st.FlickSigma = sigma; st.FlickS0 = s; }
                else if (st.Flick && sigma == st.FlickSigma && st.FlickS0 - s >= FlickDistance && speed >= FlickSpeed && fired == PokeTarget.None && nowMs - _lastFireMs >= DebounceMs)
                {
                    fired = st.FlickSigma > 0 ? PokeTarget.CornerRight : PokeTarget.CornerLeft;
                    hit.Target = fired;
                    st.Flick = false;
                    st.Armed = false;
                    _lastFireMs = nowMs;
                }
            }
            else st.Flick = false;
            st.PrevN = n;
            st.HadPrev = true;
            return fired;
        }

        /// <summary>A hand that is not tracked (or not allowed to poke now) forgets its state.</summary>
        public void Lost(int hand) { _h[hand] = default; }

        public void ResetAll() { _h[0] = default; _h[1] = default; }
    }

    /// <summary>The contents rows of the open spread (page mm per side), reused every frame.</summary>
    public sealed class ContentsRowSet
    {
        public const int Max = 64;
        public readonly int[] Sigma = new int[Max];
        public readonly Vector4[] Rect = new Vector4[Max];    // x0, y0, x1, y1 (page mm)
        public readonly string[] Id = new string[Max];
        public readonly int[] Page = new int[Max];
        public int Count;

        public void Clear() => Count = 0;
        public void Add(int sigma, ContentsRow r)
        {
            if (Count >= Max) return;
            Sigma[Count] = sigma; Rect[Count] = new Vector4((float)r.X0, (float)r.Y0, (float)r.X1, (float)r.Y1); Id[Count] = r.Id; Page[Count] = r.Page;
            Count++;
        }
        public int Find(int sigma, Vector2 mm)
        {
            for (int i = 0; i < Count; i++)
            {
                if (Sigma[i] != sigma) continue;
                var r = Rect[i];
                if (mm.x >= r.x - 2f && mm.x <= r.z + 2f && mm.y >= r.y && mm.y < r.w) return i;
            }
            return -1;
        }
    }
}
