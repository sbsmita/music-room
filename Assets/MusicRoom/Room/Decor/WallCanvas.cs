// The walls the music paints on (Room/Decor/WallPaint.cs), as a grid of 10 cm cells per wall: pure maths, no Unity objects
// (EditMode tests drive it directly). A wall is an upright rectangle (origin at its bottom-left on the floor, U along it, N
// toward the player); its cells are marked as wall where wall geometry covers them (the VR room's wall pieces: the window
// opening stays a hole) or all of them (a mixed-reality scene wall), then anything standing on or near the wall blocks the
// cells it covers when seen straight on (the VR room's furniture, paintings, fireplace, shelf, curtains, plants: every
// triangle within Band of the wall; MR: the scene model's windows, doors and furniture boxes), one cell more round it
// (Margin), and the cells too low, too near the ceiling (the night sky's) or too near a corner are left out (Finish).
// Placing a note: the walls in order round the room make one strip (the perimeter, s); the song moves along it as it plays
// (Drift metres a second from where the player looked when it started), pitch sets the height (Low..High), and a little
// randomness from a hash of the note itself (its index, its key, its instrument: the same take paints the same picture)
// spreads the splashes; the splash goes to the nearest cell round there where the whole of it fits on clear wall, else a
// little further along the walls, else nowhere. Allocation-free after Finish.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusicRoom.Room.Decor
{
    /// <summary>One note to paint: its instrument, pitch (0 low .. 1 high), loudness (0..1), seconds since the playback
    /// started, its index in the playback and a key that tells it from its neighbours (midi, string, drum).</summary>
    public struct PaintNote
    {
        public int Kind;
        public float Pitch, Level, Time;
        public int Index, Key;
        /// <summary>A hand drum's slap (the brushes paint it bolder).</summary>
        public bool Accent;
    }

    /// <summary>Where a splash goes: the wall, its place on the wall (u along, v up, metres), its radius, and the point and
    /// frame in the canvas's space (the VR room root's local space, or the world in mixed reality).</summary>
    public struct PaintSpot
    {
        public int Wall;
        public float U, V, Radius;
        public Vector3 Centre, Along, Normal;
    }

    public sealed class WallCanvas
    {
        /// <summary>Cell size (m); cells kept clear round anything blocking (cells); how far in front of a wall a thing blocks it
        /// (m) and how far behind (a frame sunk into the wall).</summary>
        public const float Cell = 0.1f, Band = 0.5f, Behind = 0.06f;
        public const int Margin = 1;
        /// <summary>The paintable band: from MinY above the floor to TopClear below the wall's top (the ceiling is the night
        /// sky's), EdgeClear from a wall's ends (the corners).</summary>
        public float MinY = 0.5f, TopClear = 0.3f, EdgeClear = 0.15f;
        /// <summary>Pitch to height: the lowest note at Low, the highest at High (m above the floor; clamped to the band).</summary>
        public float Low = 0.8f, High = 2.3f;
        /// <summary>The song moves along the walls this fast (m/s); a splash lands up to Spread (m) either side of it and
        /// Jitter (m) above or below its pitch's height.</summary>
        public float Drift = 0.15f, Spread = 0.85f, Jitter = 0.25f;
        /// <summary>A note tries this many places round its target and takes the one with the fewest splashes on it already, so
        /// the colours layer without piling into one muddy patch.</summary>
        public int Candidates = 3;
        /// <summary>A splash's radius: soft notes RMin, loud ones RMax (m).</summary>
        public float RMin = 0.06f, RMax = 0.19f;
        /// <summary>The nearest-fit search: up to SearchCells round the target, then up to Tries steps of Step (m) along the walls.</summary>
        public int SearchCells = 5, Tries = 8;
        public float Step = 0.45f;

        public const byte None = 0, WallCell = 1, Blocked = 2, Paint = 3;

        struct WallRec
        {
            public Vector3 Origin, U, N;
            public float Width, Height;
            public int Cols, Rows, Start;
            public float S;
        }

        readonly List<WallRec> _walls = new List<WallRec>(8);
        byte[] _cells = new byte[0];
        byte[] _marks = new byte[0];   // how many splashes lie on each cell now (Mark / ClearMarks): the next one prefers fewer
        byte[] _reserved = new byte[0];   // kept clear for something hung there (the gallery's frames: Reserve / Release)
        byte[] _moving = new byte[0];     // blocked by what can move (the instruments on display): ClearMoving / BlockMoving
        int _used;
        int[] _order = new int[0];
        bool _finished;

        public int WallCount => _walls.Count;
        public float Perimeter { get; private set; }
        public bool Finished => _finished;

        public void Clear()
        {
            _walls.Clear();
            _used = 0;
            _finished = false;
            Perimeter = 0f;
        }

        /// <summary>A wall: bottom-left corner on the floor, along (horizontal), its normal toward the player (horizontal),
        /// width and height (m). solid: all of it is wall (a scene wall); else MarkWall says where (the VR room's pieces).</summary>
        public int AddWall(Vector3 origin, Vector3 along, Vector3 inward, float width, float height, bool solid)
        {
            along.y = 0f; inward.y = 0f;
            along.Normalize(); inward.Normalize();
            int cols = Mathf.Max(1, Mathf.CeilToInt(width / Cell)), rows = Mathf.Max(1, Mathf.CeilToInt(height / Cell));
            var w = new WallRec { Origin = origin, U = along, N = inward, Width = width, Height = height, Cols = cols, Rows = rows, Start = _used };
            _used += cols * rows;
            if (_cells.Length < _used) Array.Resize(ref _cells, Mathf.NextPowerOfTwo(_used));
            if (_marks.Length < _cells.Length) _marks = new byte[_cells.Length];
            if (_reserved.Length < _cells.Length) Array.Resize(ref _reserved, _cells.Length);
            Array.Clear(_reserved, w.Start, cols * rows);
            if (_moving.Length < _cells.Length) Array.Resize(ref _moving, _cells.Length);
            Array.Clear(_moving, w.Start, cols * rows);
            Array.Clear(_cells, w.Start, cols * rows);
            if (solid) for (int i = 0; i < cols * rows; i++) _cells[w.Start + i] = WallCell;
            _walls.Add(w);
            _finished = false;
            return _walls.Count - 1;
        }

        /// <summary>A triangle of a wall's own geometry: the cells whose centres it covers are wall.</summary>
        public void MarkWall(int wall, Vector3 a, Vector3 b, Vector3 c)
        {
            WallRec w = _walls[wall];
            Vector2 pa = UV(w, a), pb = UV(w, b), pc = UV(w, c);
            Box(w, Vector2.Min(pa, Vector2.Min(pb, pc)), Vector2.Max(pa, Vector2.Max(pb, pc)), out int c0, out int r0, out int c1, out int r1);
            for (int r = r0; r <= r1; r++)
                for (int col = c0; col <= c1; col++)
                {
                    var p = new Vector2((col + 0.5f) * Cell, (r + 0.5f) * Cell);
                    if (Inside(p, pa, pb, pc)) _cells[w.Start + r * w.Cols + col] = WallCell;
                }
        }

        /// <summary>A triangle of something in the room: every wall it stands in front of (within Band, or sunk up to Behind
        /// into it) is blocked where it covers it, seen straight on (its bounding box on the wall: generous on purpose).</summary>
        public void Block(Vector3 a, Vector3 b, Vector3 c)
        {
            for (int i = 0; i < _walls.Count; i++)
            {
                WallRec w = _walls[i];
                float da = Vector3.Dot(a - w.Origin, w.N), db = Vector3.Dot(b - w.Origin, w.N), dc = Vector3.Dot(c - w.Origin, w.N);
                float lo = Mathf.Min(da, Mathf.Min(db, dc)), hi = Mathf.Max(da, Mathf.Max(db, dc));
                if (lo > Band || hi < -Behind) continue;
                Vector2 pa = UV(w, a), pb = UV(w, b), pc = UV(w, c);
                BlockRect(w, Vector2.Min(pa, Vector2.Min(pb, pc)), Vector2.Max(pa, Vector2.Max(pb, pc)));
            }
        }

        /// <summary>An oriented box (a scene model's window, door or furniture): blocks every wall it is near, by its corners.</summary>
        public void BlockBox(Vector3 centre, Vector3 half, Quaternion rotation)
        {
            Vector3 ax = rotation * new Vector3(half.x, 0f, 0f), ay = rotation * new Vector3(0f, half.y, 0f), az = rotation * new Vector3(0f, 0f, half.z);
            for (int i = 0; i < _walls.Count; i++)
            {
                WallRec w = _walls[i];
                float lo = float.MaxValue, hi = float.MinValue;
                Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 p = centre + ((k & 1) == 0 ? -ax : ax) + ((k & 2) == 0 ? -ay : ay) + ((k & 4) == 0 ? -az : az);
                    float d = Vector3.Dot(p - w.Origin, w.N);
                    lo = Mathf.Min(lo, d); hi = Mathf.Max(hi, d);
                    Vector2 uv = UV(w, p);
                    min = Vector2.Min(min, uv); max = Vector2.Max(max, uv);
                }
                if (lo > Band || hi < -Behind - 0.1f) continue;
                BlockRect(w, min, max);
            }
        }

        void BlockRect(in WallRec w, Vector2 min, Vector2 max)
        {
            if (max.x < 0f || max.y < 0f || min.x > w.Width || min.y > w.Height) return;
            Box(w, min, max, out int c0, out int r0, out int c1, out int r1);
            for (int r = r0; r <= r1; r++)
                for (int col = c0; col <= c1; col++)
                {
                    int k = w.Start + r * w.Cols + col;
                    if (_cells[k] == WallCell) _cells[k] = Blocked;
                }
        }

        /// <summary>After the walls and what blocks them: the margin round blocked cells and holes, the band's limits, the walls'
        /// order round the room (by the angle of their centres about the room's middle) and the perimeter.</summary>
        public void Finish()
        {
            for (int i = 0; i < _walls.Count; i++)
            {
                WallRec w = _walls[i];
                for (int r = 0; r < w.Rows; r++)
                    for (int c = 0; c < w.Cols; c++)
                    {
                        int k = w.Start + r * w.Cols + c;
                        if (_cells[k] != WallCell) continue;
                        float u = (c + 0.5f) * Cell, v = (r + 0.5f) * Cell;
                        if (v < MinY || v > w.Height - TopClear || u < EdgeClear || u > w.Width - EdgeClear) continue;
                        bool clear = true;
                        for (int dr = -Margin; dr <= Margin && clear; dr++)
                            for (int dc = -Margin; dc <= Margin; dc++)
                            {
                                int rr = r + dr, cc = c + dc;
                                if (rr < 0 || cc < 0 || rr >= w.Rows || cc >= w.Cols) continue;
                                byte s = _cells[w.Start + rr * w.Cols + cc];
                                if (s == None || s == Blocked) { clear = false; break; }
                            }
                        if (clear) _cells[k] = Paint;
                    }
            }
            // the walls in order round the room
            Vector3 mid = Vector3.zero;
            for (int i = 0; i < _walls.Count; i++) mid += Centre(_walls[i]);
            if (_walls.Count > 0) mid /= _walls.Count;
            if (_order.Length < _walls.Count) _order = new int[_walls.Count];
            for (int i = 0; i < _walls.Count; i++) _order[i] = i;
            for (int i = 1; i < _walls.Count; i++)
            {
                int x = _order[i];
                float ax = Angle(_walls[x], mid);
                int j = i - 1;
                while (j >= 0 && Angle(_walls[_order[j]], mid) > ax) { _order[j + 1] = _order[j]; j--; }
                _order[j + 1] = x;
            }
            float s0 = 0f;
            for (int i = 0; i < _walls.Count; i++)
            {
                int k = _order[i];
                WallRec w = _walls[k];
                w.S = s0;
                _walls[k] = w;
                s0 += w.Width;
            }
            Perimeter = s0;
            _finished = true;
        }

        static float Angle(in WallRec w, Vector3 mid)
        {
            Vector3 c = Centre(w) - mid;
            return Mathf.Atan2(c.z, c.x);
        }

        static Vector3 Centre(in WallRec w) => w.Origin + w.U * (0.5f * w.Width) + Vector3.up * (0.5f * w.Height);

        static Vector2 UV(in WallRec w, Vector3 p)
        {
            Vector3 d = p - w.Origin;
            return new Vector2(Vector3.Dot(d, w.U), d.y);
        }

        static void Box(in WallRec w, Vector2 min, Vector2 max, out int c0, out int r0, out int c1, out int r1)
        {
            c0 = Mathf.Clamp(Mathf.FloorToInt(min.x / Cell), 0, w.Cols - 1);
            c1 = Mathf.Clamp(Mathf.FloorToInt(max.x / Cell), 0, w.Cols - 1);
            r0 = Mathf.Clamp(Mathf.FloorToInt(min.y / Cell), 0, w.Rows - 1);
            r1 = Mathf.Clamp(Mathf.FloorToInt(max.y / Cell), 0, w.Rows - 1);
        }

        static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
            bool neg = d1 < -1e-6f || d2 < -1e-6f || d3 < -1e-6f, pos = d1 > 1e-6f || d2 > 1e-6f || d3 > 1e-6f;
            return !(neg && pos);
        }

        static float Cross(Vector2 p, Vector2 a, Vector2 b) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

        // ---------------------------------------------------------------- reading it

        /// <summary>The state of the cell under (u, v) of a wall (None outside it).</summary>
        public byte CellAt(int wall, float u, float v)
        {
            if ((uint)wall >= (uint)_walls.Count) return None;
            WallRec w = _walls[wall];
            int c = Mathf.FloorToInt(u / Cell), r = Mathf.FloorToInt(v / Cell);
            if (c < 0 || r < 0 || c >= w.Cols || r >= w.Rows) return None;
            return _cells[w.Start + r * w.Cols + c];
        }

        public bool Paintable(int wall, float u, float v)
        {
            if ((uint)wall >= (uint)_walls.Count) return false;
            WallRec w = _walls[wall];
            int c = Mathf.FloorToInt(u / Cell), r = Mathf.FloorToInt(v / Cell);
            if (c < 0 || r < 0 || c >= w.Cols || r >= w.Rows) return false;
            int k = w.Start + r * w.Cols + c;
            return _cells[k] == Paint && _reserved[k] == 0 && _moving[k] == 0;
        }

        // ---------------------------------------------------------------- what can move (the instruments on display)

        /// <summary>Nothing movable blocks the walls (before they are read again).</summary>
        public void ClearMoving() => Array.Clear(_moving, 0, _moving.Length);

        /// <summary>A movable thing's box (centre, half sizes, rotation in the canvas's space: an instrument on its display, a
        /// rig standing at a wall) blocks every wall it is near, seen straight on, with a cell of margin.</summary>
        public void BlockMoving(Vector3 centre, Vector3 half, Quaternion rotation)
        {
            Vector3 ax = rotation * new Vector3(half.x, 0f, 0f), ay = rotation * new Vector3(0f, half.y, 0f), az = rotation * new Vector3(0f, 0f, half.z);
            for (int i = 0; i < _walls.Count; i++)
            {
                WallRec w = _walls[i];
                float lo = float.MaxValue, hi = float.MinValue;
                Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 p = centre + ((k & 1) == 0 ? -ax : ax) + ((k & 2) == 0 ? -ay : ay) + ((k & 4) == 0 ? -az : az);
                    float d = Vector3.Dot(p - w.Origin, w.N);
                    lo = Mathf.Min(lo, d); hi = Mathf.Max(hi, d);
                    Vector2 uv = UV(w, p);
                    min = Vector2.Min(min, uv); max = Vector2.Max(max, uv);
                }
                if (lo > Band || hi < -Behind - 0.1f) continue;
                min -= new Vector2(Cell, Cell); max += new Vector2(Cell, Cell);
                if (max.x < 0f || max.y < 0f || min.x > w.Width || min.y > w.Height) continue;
                Box(w, min, max, out int c0, out int r0, out int c1, out int r1);
                for (int r = r0; r <= r1; r++)
                    for (int c = c0; c <= c1; c++) _moving[w.Start + r * w.Cols + c] = 1;
            }
        }

        /// <summary>Cells blocked by movable things now (tests).</summary>
        public int MovingCells
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _used && i < _moving.Length; i++) if (_moving[i] != 0) n++;
                return n;
            }
        }

        // ---------------------------------------------------------------- reserving (the gallery's frames)

        /// <summary>Every cell of the rectangle (u0..u1 along, v0..v1 up) is clear wall, nothing reserved there.</summary>
        public bool RectClear(int wall, float u0, float v0, float u1, float v1)
        {
            if ((uint)wall >= (uint)_walls.Count) return false;
            for (float v = v0 + Cell * 0.5f; v < v1; v += Cell)
                for (float u = u0 + Cell * 0.5f; u < u1; u += Cell)
                    if (!Paintable(wall, u, v)) return false;
            return Paintable(wall, u1 - 0.01f, v1 - 0.01f) && Paintable(wall, u0 + 0.01f, v1 - 0.01f);
        }

        /// <summary>Keep a rectangle clear of paint (something hangs there); Release gives it back. Counted: two reservations
        /// of one cell need two releases.</summary>
        public void Reserve(int wall, float u0, float v0, float u1, float v1) => ReserveRect(wall, u0, v0, u1, v1, 1);
        public void Release(int wall, float u0, float v0, float u1, float v1) => ReserveRect(wall, u0, v0, u1, v1, -1);

        void ReserveRect(int wall, float u0, float v0, float u1, float v1, int add)
        {
            if ((uint)wall >= (uint)_walls.Count) return;
            WallRec w = _walls[wall];
            Box(w, new Vector2(u0, v0), new Vector2(u1, v1), out int c0, out int r0, out int c1, out int r1);
            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                {
                    int k = w.Start + r * w.Cols + c;
                    _reserved[k] = (byte)Mathf.Clamp(_reserved[k] + add, 0, 255);
                }
        }

        /// <summary>Nothing reserved anywhere (a canvas rebuilt).</summary>
        public void ReleaseAll() => Array.Clear(_reserved, 0, _reserved.Length);

        /// <summary>The paintable cells not reserved (tests).</summary>
        public int FreeCells
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _used; i++) if (_cells[i] == Paint && _reserved[i] == 0 && _moving[i] == 0) n++;
                return n;
            }
        }

        // ---------------------------------------------------------------- a ray (thrown paint)

        /// <summary>The nearest wall a ray from inside the room hits (from its front), within the wall's rectangle: the wall,
        /// u, v and the distance. False when it hits none.</summary>
        public bool Raycast(Vector3 origin, Vector3 dir, out int wall, out float u, out float v, out float distance)
        {
            wall = -1; u = v = 0f; distance = float.MaxValue;
            if (!_finished || dir.sqrMagnitude < 1e-8f) return false;
            dir.Normalize();
            for (int i = 0; i < _walls.Count; i++)
            {
                WallRec w = _walls[i];
                float den = Vector3.Dot(dir, w.N);
                if (den > -1e-4f) continue;
                float t = Vector3.Dot(w.Origin - origin, w.N) / den;
                if (t <= 0f || t >= distance) continue;
                Vector3 p = origin + dir * t;
                float pu = Vector3.Dot(p - w.Origin, w.U), pv = p.y - w.Origin.y;
                if (pu < 0f || pu > w.Width || pv < 0f || pv > w.Height) continue;
                distance = t; wall = i; u = pu; v = pv;
            }
            return wall >= 0;
        }

        /// <summary>The nearest point round (u, v) of a wall where a disc of radius r fits on clear wall (cell centres, ring by
        /// ring, up to rings cells out).</summary>
        public bool FindClear(int wall, float u, float v, float r, int rings, out float fu, out float fv)
        {
            fu = fv = 0f;
            if ((uint)wall >= (uint)_walls.Count) return false;
            int keep = SearchCells;
            SearchCells = rings;
            bool ok = Search(wall, u, v, r, out fu, out fv);
            SearchCells = keep;
            return ok;
        }

        /// <summary>Paintable cells in all (tests, the log).</summary>
        public int PaintableCells
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _used; i++) if (_cells[i] == Paint) n++;
                return n;
            }
        }

        /// <summary>The wall a place on the perimeter lies on starts at this s (its left end).</summary>
        public float WallStart(int wall) => (uint)wall < (uint)_walls.Count ? _walls[wall].S : 0f;

        public void WallFrame(int wall, out Vector3 origin, out Vector3 along, out Vector3 inward, out float width, out float height)
        {
            WallRec w = _walls[wall];
            origin = w.Origin; along = w.U; inward = w.N; width = w.Width; height = w.Height;
        }

        /// <summary>A wall point in the canvas's space.</summary>
        public Vector3 Point(int wall, float u, float v)
        {
            WallRec w = _walls[wall];
            return w.Origin + w.U * u + Vector3.up * v;
        }

        /// <summary>Where a horizontal look from a point meets the walls, as a place on the perimeter (the nearest wall hit in
        /// front, inside its width); 0 when none is hit.</summary>
        public float PerimeterAlong(Vector3 from, Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-8f || !_finished) return 0f;
            dir.Normalize();
            float best = float.MaxValue, s = 0f;
            for (int i = 0; i < _walls.Count; i++)
            {
                WallRec w = _walls[i];
                float den = Vector3.Dot(dir, w.N);
                if (den > -1e-4f) continue;   // the wall faces the other way (or edge-on)
                float t = Vector3.Dot(w.Origin - from, w.N) / den;
                if (t <= 0f || t >= best) continue;
                float u = Vector3.Dot(from + dir * t - w.Origin, w.U);
                if (u < 0f || u > w.Width) continue;
                best = t;
                s = w.S + u;
            }
            return s;
        }

        /// <summary>The wall and its u at a place on the perimeter (wraps round).</summary>
        public void Locate(float s, out int wall, out float u)
        {
            wall = -1; u = 0f;
            if (!_finished || Perimeter <= 0f) return;
            s %= Perimeter;
            if (s < 0f) s += Perimeter;
            for (int i = 0; i < _walls.Count; i++)
            {
                WallRec w = _walls[i];
                if (s >= w.S && s < w.S + w.Width) { wall = i; u = s - w.S; return; }
            }
            wall = _order[0];
            u = 0f;
        }

        /// <summary>Hash of a note for its randomness: the same note of the same take always lands the same way.</summary>
        public static uint Hash(int index, int key, int kind)
        {
            unchecked
            {
                uint h = (uint)index * 0x9E3779B1u ^ (uint)key * 0x85EBCA77u ^ (uint)kind * 0xC2B2AE3Du ^ 0x27d4eb2fu;
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
                return h;
            }
        }

        /// <summary>A uniform number in [0, 1) from a hash and a channel.</summary>
        public static float Rand(uint h, int channel)
        {
            unchecked
            {
                uint x = h + (uint)channel * 0x6D2B79F5u;
                x = (x ^ (x >> 15)) * (x | 1u);
                x ^= x + (x ^ (x >> 7)) * (x | 61u);
                return ((x ^ (x >> 14)) >> 8) / 16777216f;
            }
        }

        /// <summary>Where a note's splash goes, starting from s0 on the perimeter (where the player looked when the playback
        /// started): of Candidates places round its target, the clear one with the fewest splashes on it. False when no clear
        /// wall is found near enough.</summary>
        public bool Place(in PaintNote note, float s0, out PaintSpot spot)
        {
            uint h = Hash(note.Index, note.Key, note.Kind);
            float radius = Mathf.Lerp(RMin, RMax, Mathf.Clamp01(note.Level)) * (0.85f + 0.3f * Rand(h, 3));
            return Place(note, s0, radius, out spot);
        }

        /// <summary>As Place, for a mark that needs a disc of this radius round its anchor (a brush's Reach).</summary>
        public bool Place(in PaintNote note, float s0, float radius, out PaintSpot spot)
        {
            spot = default;
            if (!_finished || Perimeter <= 0f) return false;
            uint h = Hash(note.Index, note.Key, note.Kind);
            float sBase = s0 + Drift * Mathf.Max(0f, note.Time), vBase = Mathf.Lerp(Low, High, Mathf.Clamp01(note.Pitch));
            int best = int.MaxValue;
            for (int k = 0; k < Mathf.Max(1, Candidates); k++)
            {
                float s = sBase + Spread * (2f * Rand(h, 1 + 40 * k) - 1f);
                float v = vBase + Jitter * (2f * Rand(h, 2 + 40 * k) - 1f);
                if (!PlaceAt(s, v, radius, out PaintSpot c)) continue;
                int m = MarksAt(c.Wall, c.U, c.V);
                if (m < best) { best = m; spot = c; }
                if (m == 0) break;
            }
            return best != int.MaxValue;
        }

        bool PlaceAt(float s, float v, float radius, out PaintSpot spot)
        {
            spot = default;
            for (int t = 0; t <= Tries; t++)
            {
                // 0, +1, -1, +2, -2 ... steps along the walls
                int k = (t + 1) / 2;
                float st = s + ((t & 1) == 1 ? k : -k) * Step;
                Locate(st, out int wall, out float u);
                if (wall < 0) continue;
                for (int shrink = 0; shrink < 2; shrink++)
                {
                    float r = shrink == 0 ? radius : radius * 0.65f;
                    if (Search(wall, u, v, r, out float fu, out float fv))
                    {
                        WallRec w = _walls[wall];
                        spot = new PaintSpot { Wall = wall, U = fu, V = fv, Radius = r, Centre = w.Origin + w.U * fu + Vector3.up * fv, Along = w.U, Normal = w.N };
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>The splashes on the cells round a point (its cell and the eight round it).</summary>
        public int MarksAt(int wall, float u, float v)
        {
            if ((uint)wall >= (uint)_walls.Count) return 0;
            WallRec w = _walls[wall];
            int c0 = Mathf.FloorToInt(u / Cell), r0 = Mathf.FloorToInt(v / Cell), n = 0;
            for (int dr = -1; dr <= 1; dr++)
                for (int dc = -1; dc <= 1; dc++)
                {
                    int cc = c0 + dc, rr = r0 + dr;
                    if (cc < 0 || rr < 0 || cc >= w.Cols || rr >= w.Rows) continue;
                    n += _marks[w.Start + rr * w.Cols + cc];
                }
            return n;
        }

        /// <summary>A splash was painted here: its cells count one more (up to 255).</summary>
        public void Mark(in PaintSpot spot)
        {
            if ((uint)spot.Wall >= (uint)_walls.Count) return;
            WallRec w = _walls[spot.Wall];
            int reach = Mathf.Max(0, Mathf.RoundToInt(spot.Radius / Cell));
            int c0 = Mathf.FloorToInt(spot.U / Cell), r0 = Mathf.FloorToInt(spot.V / Cell);
            for (int dr = -reach; dr <= reach; dr++)
                for (int dc = -reach; dc <= reach; dc++)
                {
                    int cc = c0 + dc, rr = r0 + dr;
                    if (cc < 0 || rr < 0 || cc >= w.Cols || rr >= w.Rows) continue;
                    int k = w.Start + rr * w.Cols + cc;
                    if (_marks[k] < 255) _marks[k]++;
                }
        }

        /// <summary>A new painting: no splash counted anywhere.</summary>
        public void ClearMarks() => Array.Clear(_marks, 0, _marks.Length);

        /// <summary>The nearest cell centre round (u, v), ring by ring, where a disc of radius r fits on clear wall.</summary>
        bool Search(int wall, float u, float v, float r, out float fu, out float fv)
        {
            WallRec w = _walls[wall];
            int c0 = Mathf.FloorToInt(u / Cell), r0 = Mathf.FloorToInt(v / Cell);
            fu = fv = 0f;
            for (int ring = 0; ring <= SearchCells; ring++)
            {
                for (int dr = -ring; dr <= ring; dr++)
                    for (int dc = -ring; dc <= ring; dc++)
                    {
                        if (Mathf.Max(Mathf.Abs(dr), Mathf.Abs(dc)) != ring) continue;
                        int cc = c0 + dc, rr = r0 + dr;
                        if (cc < 0 || rr < 0 || cc >= w.Cols || rr >= w.Rows) continue;
                        // keep the note's own spot inside its cell when it is clear; else the cell's centre
                        float pu = ring == 0 ? u : (cc + 0.5f) * Cell, pv = ring == 0 ? v : (rr + 0.5f) * Cell;
                        if (Fits(wall, pu, pv, r)) { fu = pu; fv = pv; return true; }
                    }
            }
            return false;
        }

        /// <summary>A disc of radius r round (u, v) lies on clear wall: its centre and eight points round it at 0.8 r.</summary>
        public bool Fits(int wall, float u, float v, float r)
        {
            if (!Paintable(wall, u, v)) return false;
            float k = 0.8f * r, d = 0.5657f * r;
            return Paintable(wall, u + k, v) && Paintable(wall, u - k, v) && Paintable(wall, u, v + k) && Paintable(wall, u, v - k)
                && Paintable(wall, u + d, v + d) && Paintable(wall, u - d, v + d) && Paintable(wall, u + d, v - d) && Paintable(wall, u - d, v - d);
        }
    }
}
