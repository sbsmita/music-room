// The open book on the music rest (sheet.md 2.1, 5.3; the lab's lib/geometry.mjs). Book frame = the rest pose: origin at
// the bottom centre of the rest's front face, +x to the player's right, +y up the (leaning) face, +z into the rest, away
// from the player; N (toward the player) = -z. The pages rise from the spine in a gutter curl
//   lift(|x|) = cover + block / 2 + curlA (1 - e^(-|x| / curlTau)) - sag (|x| / 0.32)^2
// and the page texture follows the ARC length s from the spine (print near the gutter is never stretched), so a page
// point is (sigma d(s), sBottom + v, -lift(d(s))) with d the flat reach of arc length s, sigma = +1 right, -1 left.
using UnityEngine;

namespace MusicRoom.Sheet
{
    public static class BookGeometry
    {
        public const float Lean = 0.3f;                    // the rest leans back 0.3 rad (PianoBody.MusicRest)
        public const float Cover = 0.0025f, Block = 0.004f, CurlA = 0.009f, CurlTau = 0.014f, Sag = 0.004f, Square = 0.003f;
        public const float PageW = 0.320f, PageH = 0.244f;  // arc length across, height (m)
        public const int Columns = 33, Rows = 9;            // per page; columns denser near the spine
        /// <summary>The music rest's front face, bottom centre, in the piano frame, and the ledge top along the face: the
        /// platform's IPianoApi.MusicRestLocal / MusicRestLedge (PianoMusicRest: the 5 mm board's front face, 1 mm above the
        /// body top, Name + 0.087 behind the key fronts: (0, 0.03974, 0.23761); the ledge 6.55 mm up the face). These are the
        /// defaults; SheetModule sets the host's values with SetRest before it builds the book (the ribbon's path onto the top
        /// panel and its poke capsule use them too).</summary>
        public static readonly Quaternion DefaultRestRotation = Quaternion.Euler(Lean * Mathf.Rad2Deg, 0f, 0f);
        public static readonly Vector3 DefaultRestPosition = new Vector3(0f, 0.039f, 0.153f + 0.087f) - DefaultRestRotation * Vector3.forward * 0.0025f;
        public static readonly float DefaultLedge = (0.039f + 0.007f - DefaultRestPosition.y) / Mathf.Cos(Lean);
        public static Vector3 RestLocalPosition { get; private set; } = DefaultRestPosition;
        public static Quaternion RestLocalRotation { get; private set; } = DefaultRestRotation;
        public static void SetRest(Pose local) { RestLocalPosition = local.position; RestLocalRotation = local.rotation; }

        public static float Lift(float ax) => Cover + Block * 0.5f + CurlA * (1f - Mathf.Exp(-ax / CurlTau)) - Sag * (ax / 0.32f) * (ax / 0.32f);
        public static float DLift(float ax) => (CurlA / CurlTau) * Mathf.Exp(-ax / CurlTau) - (2f * Sag * ax) / (0.32f * 0.32f);

        const int ArcN = 4000;
        const float ArcDx = 0.34f / ArcN;
        static readonly float[] s_arc = BuildArc();
        static float[] BuildArc()
        {
            var t = new float[ArcN + 1];
            double acc = 0;
            for (int i = 1; i <= ArcN; i++) { double x = (i - 0.5) * ArcDx, d = DLift((float)x); acc += System.Math.Sqrt(1 + d * d) * ArcDx; t[i] = (float)acc; }
            return t;
        }

        /// <summary>Arc length from the spine at flat reach ax (m); straight beyond the table.</summary>
        public static float ArcAt(float ax)
        {
            if (ax <= 0f) return 0f;
            float f = ax / ArcDx;
            int i = (int)f;
            if (i >= ArcN) return s_arc[ArcN] + (ax - ArcN * ArcDx);
            return s_arc[i] + (s_arc[i + 1] - s_arc[i]) * (f - i);
        }

        /// <summary>Flat reach of arc length s (the inverse of ArcAt).</summary>
        public static float FlatReach(float s)
        {
            if (s <= 0f) return 0f;
            if (s >= s_arc[ArcN]) return ArcN * ArcDx + (s - s_arc[ArcN]);
            int lo = 0, hi = ArcN;
            while (hi - lo > 1) { int m = (lo + hi) >> 1; if (s_arc[m] < s) lo = m; else hi = m; }
            float a = s_arc[lo], b = s_arc[hi];
            return (lo + (b > a ? (s - a) / (b - a) : 0f)) * ArcDx;
        }

        /// <summary>The pages' flat half width (about 318.5 mm).</summary>
        public static readonly float WFlat = FlatReach(PageW);

        /// <summary>Arc length of column i (0 at the spine, PageW at the outer edge): s_i = W (i / 32)^1.6.</summary>
        public static float ColumnArc(int i) => PageW * Mathf.Pow(i / (float)(Columns - 1), 1.6f);

        /// <summary>A page point in the book frame (sigma +1 right page, -1 left; s arc from the spine, v up the page).</summary>
        public static Vector3 PagePoint(int sigma, float s, float v, float sBottom)
        {
            float d = FlatReach(s);
            return new Vector3(sigma * d, sBottom + v, -Lift(d));
        }

        /// <summary>The page's normal toward the player at flat reach d on side sigma.</summary>
        public static Vector3 PageNormal(int sigma, float d)
        {
            float a = DLift(d) * sigma;
            return new Vector3(-a, 0f, -1f).normalized;
        }

        /// <summary>Page mm (x from the page's left edge, y down from its top) of (sigma, s, v).</summary>
        public static Vector2 PageMm(int sigma, float s, float v)
            => new Vector2(sigma > 0 ? s * 1000f : (PageW - s) * 1000f, (PageH - v) * 1000f);

        /// <summary>(sigma, arc s, v, n = height above the curled surface toward the player) of a book-frame point.</summary>
        public static void ToPage(Vector3 p, float sBottom, out int sigma, out float s, out float v, out float n)
        {
            sigma = p.x >= 0f ? 1 : -1;
            float ax = Mathf.Abs(p.x);
            s = ArcAt(ax);
            v = p.y - sBottom;
            n = -p.z - Lift(Mathf.Min(ax, WFlat));
        }

        /// <summary>Piano frame -> book frame (the rest pose) and back.</summary>
        public static Vector3 PianoToBook(Vector3 piano) => Quaternion.Inverse(RestLocalRotation) * (piano - RestLocalPosition);
        public static Vector3 BookToPiano(Vector3 book) => RestLocalPosition + RestLocalRotation * book;

        // ---- the leaf (sheet.md 5.3)
        /// <summary>theta(tau) = pi tau^2 (3 - 2 tau) (forward); a backward turn runs pi -> 0.</summary>
        public static float Theta(float tau, bool forward)
        {
            tau = Mathf.Clamp01(tau);
            float e = tau * tau * (3f - 2f * tau);
            return Mathf.PI * (forward ? e : 1f - e);
        }

        /// <summary>The angle of the leaf column at arc s: the free edge trails (phi = theta -+ 0.8 sin theta (s / W)^1.5).</summary>
        public static float Phi(float theta, float s, bool forward)
        {
            float bend = 0.8f * Mathf.Sin(theta) * Mathf.Pow(Mathf.Clamp01(s / PageW), 1.5f);
            return forward ? theta - bend : theta + bend;
        }

        /// <summary>A leaf point: spine + d cos phi X + (d sin phi + lift(d) |cos phi|) N, lifted eps toward the player.</summary>
        public static Vector3 LeafPoint(float d, float phi, float y, float eps)
        {
            float c = Mathf.Cos(phi), sn = Mathf.Sin(phi);
            return new Vector3(d * c, y, -(d * sn + Lift(d) * Mathf.Abs(c) + eps));
        }
    }
}
