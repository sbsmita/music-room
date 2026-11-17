// The menu's first-five-minutes parts (Docs/11-first-five/sunny-tour.md 7.2): what Sunny points at and lights
// (IMenuGuideHooks: a row's rectangle on the face in the world, the spotlight that walks down the home rows while Sunny
// names them), what it listens to (the platform's MenuRowEvent on every poke, StartChoiceEvent when the start panel is
// answered), and the Done / Reset buttons of "Move instruments" stopping the drawn finger on their face like the palm
// menu does (IContactSurface, the known gap of the merge).
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Menu
{
    public sealed partial class MenuModule : IMenuGuideHooks
    {
        // ---------------- IMenuGuideHooks ----------------

        /// <summary>A row of the shown palm panel (or of the Done / Reset buttons): its centre on the face (world), the face's
        /// normal toward the player and its size in metres. "list": the middle of the shown list.</summary>
        public bool TryGetRowRect(string rowId, out Vector3 center, out Vector3 normal, out Vector2 size)
        {
            center = default; normal = Vector3.back; size = default;
            if (string.IsNullOrEmpty(rowId)) return false;
            if (_ctl != null && _ctl.Shown && _root != null)
            {
                if (rowId == "list")
                {
                    if (_ctl.Screen != MenuScreen.Courses && _ctl.Screen != MenuScreen.Recordings && _ctl.Screen != MenuScreen.Tips) return false;
                    return Rect(_root, 1f, MenuStyle.RowX0, MenuStyle.ListTop, MenuStyle.RowX1, MenuStyle.ListBottom, out center, out normal, out size);
                }
                int i = RowIndex(_ctl, rowId);
                if (i >= 0)
                {
                    MenuRow r = _ctl.Row(i);
                    if (r.Id == RowKind.Back) return Rect(_root, 1f, 0f, 0f, MenuStyle.CW, MenuStyle.Header, out center, out normal, out size);
                    return Rect(_root, 1f, r.X0, r.Y0, r.X1, r.Y1, out center, out normal, out size);
                }
            }
            if (_bar != null && _bar.Shown && _barRoot != null)
            {
                int i = RowIndex(_bar, rowId);
                if (i >= 0)
                {
                    MenuRow r = _bar.Row(i);
                    return Rect(_barRoot, MenuStyle.ArrangeScale, r.X0, r.Y0, r.X1, r.Y1, out center, out normal, out size);
                }
            }
            return false;
        }

        /// <summary>The shown row with this id; "prefix.*" matches the first row whose id starts with the prefix (an
        /// instrument's "Options" chip: "options.*").</summary>
        static int RowIndex(MenuController c, string rowId)
        {
            bool prefix = rowId.EndsWith("*", System.StringComparison.Ordinal);
            for (int i = 0; i < c.RowCount; i++)
            {
                string id = MenuRowIds.IdOf(c.Row(i));
                if (id == null) continue;
                if (prefix ? id.Length >= rowId.Length - 1 && string.CompareOrdinal(id, 0, rowId, 0, rowId.Length - 1) == 0 : id == rowId) return i;
            }
            return -1;
        }

        static bool Rect(Transform root, float scale, float x0, float y0, float x1, float y1, out Vector3 center, out Vector3 normal, out Vector2 size)
        {
            float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f;
            Vector3 on = MenuMath.CanvasPoint(cx, cy, 0f) * scale;
            Vector3 off = MenuMath.CanvasPoint(cx, cy, 0.01f / scale) * scale;
            center = root.TransformPoint(on);
            normal = (root.TransformPoint(off) - center).normalized;
            size = new Vector2((x1 - x0) / MenuStyle.PxPerMetre * scale, (y1 - y0) / MenuStyle.PxPerMetre * scale);
            return true;
        }

        /// <summary>The spotlight: one row of the shown screen lit with the hover highlight (a hovering fingertip wins).</summary>
        public bool Spotlight(string rowId)
        {
            if (_ctl == null) return false;
            if (string.IsNullOrEmpty(rowId)) { _ctl.Spotlight = -1; return true; }
            int i = _ctl.Shown ? RowIndex(_ctl, rowId) : -1;
            _ctl.Spotlight = i;
            return i >= 0;
        }

        public float ListMaxScroll => _ctl != null && _ctl.Shown ? _ctl.MaxScroll : 0f;
        public float ListScroll => _ctl != null && _ctl.Shown ? _ctl.Scroll : 0f;

        // ---------------- what Sunny listens to ----------------

        void PublishStartChoice(bool mixedReality) => _app.Events.Publish(new StartChoiceEvent { InMyRoom = mixedReality });

        /// <summary>The services of the first five (Lessons' progress, the Guide's Sunny screen), resolved late.</summary>
        void ResolveFirstFive()
        {
            if (_ctl.Progress == null) _ctl.Progress = _app.Get<ILessonProgress>();
            if (_ctl.Tour == null) _ctl.Tour = _app.Get<IGuideTour>();
        }

        // ---------------- the Done / Reset buttons stop the finger ----------------

        /// <summary>The Done / Reset face under a fingertip while the buttons are shown: the palm panel's rule on the half-size
        /// face (the drawn finger stops on it; the pokes keep reading the raw tip).</summary>
        bool BarContact(Vector3 world, float above, float below, ref ContactHit hit)
        {
            if (_bar == null || !_bar.Shown || _barRoot == null) return false;
            float s = MenuStyle.ArrangeScale;
            Vector3 local = MenuMath.ScaledToPanel(_barRoot.InverseTransformPoint(world), s);
            MenuMath.ToSurface(local, out float u, out float y, out float z);   // u, y on the full-size face; z in metres
            if (Mathf.Abs(u) > MenuMath.W / 2f || Mathf.Abs(y) > MenuMath.H / 2f || z > above || z < -below) return false;
            Vector3 p = _barRoot.TransformPoint(MenuMath.SurfacePoint(u, y, 0f) * s);
            Vector3 n = _barRoot.TransformDirection(MenuMath.SurfaceNormal(u)).normalized;
            float h = Vector3.Dot(n, world - p);
            hit.Point = world - n * h;
            hit.Normal = n;
            hit.Height = h;
            hit.Use = ContactUse.Clamp;
            return true;
        }
    }
}
