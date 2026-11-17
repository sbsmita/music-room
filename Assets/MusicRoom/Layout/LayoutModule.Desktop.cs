// The desktop rig moves (ARCHITECTURE.md section 15.5): while the app is not presenting (the
// editor's mouse hand, never the headset), Shift + left-drag on a rig slides it across the horizontal plane at the
// height where the mouse ray hit it, and Alt + left-drag turns it in place by 0.012 rad per pixel of horizontal mouse
// travel. The rig is the nearest one whose parts the mouse ray hits; while the instruments are locked only the menu
// panel moves. Both go through the same grab as the hands (constraint, unstick, red ring, settle on release), so a
// mouse drag can never leave a rig inside another. While Shift / Alt is held or a mouse drag runs, the desktop camera
// does not look around (a grab press never starts a look).
using MusicRoom.Core;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.Layout
{
    /// <summary>One frame of desktop mouse input (screen ray, left button, modifier keys, horizontal pixel position).</summary>
    public struct DesktopMouse
    {
        public Ray Ray;
        /// <summary>The left button went down this frame.</summary>
        public bool Pressed;
        /// <summary>The left button is down.</summary>
        public bool Held;
        public bool Shift, Alt;
        /// <summary>Horizontal mouse position in pixels.</summary>
        public float X;
    }

    public sealed partial class LayoutModule
    {
        enum MouseGrab { None, Move, Rotate }

        /// <summary>Radians of turn per pixel of mouse travel (the right-handed frame adds it; Unity yaw subtracts it).</summary>
        public const float MouseTurnPerPixel = 0.012f;

        MouseGrab _mouseMode;
        float _mouseX0, _mouseYaw0;
        Plane _dragPlane;
        DesktopRigFallback _desk;
        System.Func<bool> _prevDragBlocked, _dragBlockedHook;

        /// <summary>A rig is held by the desktop mouse (Shift-drag moves, Alt-drag turns).</summary>
        public bool MouseHolding => _mouseMode != MouseGrab.None && _held != null;
        public bool MouseTurning => _mouseMode == MouseGrab.Rotate && _held != null;

        /// <summary>Chains a "do not look around while a mouse rig drag runs" test into the desktop camera (its own
        /// DragBlocked already refuses a press with Shift / Alt held or on a PlaySurface collider). Restored in Shutdown if
        /// nobody replaced it since.</summary>
        void HookDesktopCamera()
        {
            _desk = _app.Rig != null ? _app.Rig.Desktop : null;
            if (_desk == null) return;
            _prevDragBlocked = _desk.dragBlocked;
            _dragBlockedHook = DesktopBlocksLook;
            _desk.dragBlocked = _dragBlockedHook;
        }

        void UnhookDesktopCamera()
        {
            if (_desk != null && _desk.dragBlocked == _dragBlockedHook) _desk.dragBlocked = _prevDragBlocked;
            _desk = null;
        }

        bool DesktopBlocksLook()
        {
            if (_mouseMode != MouseGrab.None) return true;
            System.Func<bool> prev = _prevDragBlocked;
            return prev != null && prev();
        }

        /// <summary>The live mouse, read only while not presenting (the Grab phase calls it).</summary>
        void TickDesktop()
        {
            Camera cam = _app.Rig != null ? _app.Rig.HeadCamera : null;
            if (cam == null)
            {
                if (_mouseMode != MouseGrab.None) EndGrab();
                return;
            }
            Vector2 mp = DesktopInput.MousePosition;
            var m = new DesktopMouse
            {
                Ray = cam.ScreenPointToRay(mp),
                Pressed = DesktopInput.MouseDown(0),
                Held = DesktopInput.Mouse(0),
                Shift = DesktopInput.Shift,
                Alt = DesktopInput.Alt,
                X = mp.x,
            };
            DesktopStep(m);
        }

        /// <summary>One frame of the desktop path (public so tests can script the mouse): a Shift / Alt press on a rig takes
        /// it; while the button is held the rig follows the ray on its drag plane (Shift) or turns (Alt); releasing the
        /// button lets go (settle).</summary>
        public void DesktopStep(in DesktopMouse m)
        {
            if (_mouseMode != MouseGrab.None)
            {
                if (_held == null) { _mouseMode = MouseGrab.None; return; }
                if (!m.Held) { EndGrab(); return; }
                if (_mouseMode == MouseGrab.Move)
                {
                    if (_dragPlane.Raycast(m.Ray, out float t)) MoveGrab(m.Ray.GetPoint(t), 0f, wrist: false);
                }
                else TurnGrab(_mouseYaw0 - (m.X - _mouseX0) * MouseTurnPerPixel);
                return;
            }
            // The ray summon on the desktop (platform.md 4.4 c): a plain left click on an instrument on display brings it.
            if (m.Pressed && !m.Shift && !m.Alt && !_arranging && _held == null && DesktopSummon(m.Ray)) return;
            // In "Move instruments" a plain left drag moves an instrument (Alt still turns it), locked or not; a press on the
            // menu or on the Done / Reset buttons stays theirs.
            if (!m.Pressed || !(m.Shift || m.Alt || _arranging) || _held != null) return;
            Rig pick;
            Vector3 hit;
            if (_arranging)
            {
                if (PressBelongsToUi(m.Ray)) return;
                pick = RayArrangeRig(m.Ray, 40f, out float at);
                hit = pick != null ? m.Ray.GetPoint(at) : Vector3.zero;
            }
            else pick = PickRig(m.Ray, out hit);
            if (pick == null) return;
            _dragPlane = new Plane(Vector3.up, new Vector3(0f, hit.y, 0f));
            if (!_dragPlane.Raycast(m.Ray, out float d)) return;
            StartGrab(pick, m.Ray.GetPoint(d), null, 0f);
            _mouseMode = m.Alt ? MouseGrab.Rotate : MouseGrab.Move;   // Alt turns, otherwise the drag moves
            _mouseX0 = m.X;
            _mouseYaw0 = _curYaw;
        }

        /// <summary>The nearest grabbable rig of the layout whose shown parts the ray hits (handles and
        /// played parts as oriented boxes; the menu panel by its renderers while it is shown). Runs on a press only.</summary>
        public Rig PickRig(in Ray ray, out Vector3 hit)
        {
            hit = Vector3.zero;
            Rig best = null;
            float bestT = float.PositiveInfinity;
            for (int i = 0; i < Placement.Entries.Count; i++)
            {
                RigEntry e = Placement.Entries[i];
                if (!PlacementSolver.InScene(e) || !Grabbable(e.Rig)) continue;
                for (int k = 0; k < e.Footprint.Count; k++)
                {
                    PartRef part = e.Footprint[k];
                    if (!Placement.Shows(part.R, e.Group) || !PlacementSolver.GeometryBox(part, out Bounds b, out Matrix4x4 tw)) continue;
                    if (Obb.FromBounds(b, tw).Raycast(ray, out float t) && t < bestT) { bestT = t; best = e.Rig; }
                }
            }
            IMenuPanel menu = Menu;
            Rig menuRig = Get(RigId.Lessons) ?? menu?.Rig;
            if (menu != null && menu.Shown && menuRig != null && menuRig.Group != null && Grabbable(menuRig))
            {
                menuRig.Group.GetComponentsInChildren(false, _rendererScratch);
                for (int k = 0; k < _rendererScratch.Count; k++)
                {
                    Renderer r = _rendererScratch[k];
                    if (r.enabled && r.bounds.IntersectRay(ray, out float t) && t < bestT) { bestT = t; best = menuRig; }
                }
            }
            if (best != null) hit = ray.GetPoint(bestT);
            return best;
        }

        /// <summary>The nearest desktop click target (Layers.PlaySurface) under the mouse is not part of an instrument: the
        /// menu's face or the Done / Reset buttons, which handle the press themselves.</summary>
        bool PressBelongsToUi(in Ray ray)
        {
            if (!Physics.Raycast(ray, out RaycastHit rh, 40f, Layers.PlaySurfaceMask, QueryTriggerInteraction.Collide)) return false;
            Transform t = rh.collider.transform;
            for (int i = 0; i < Placement.Entries.Count; i++)
            {
                Transform g = Placement.Entries[i].Group;
                if (g != null && t.IsChildOf(g)) return false;
            }
            return true;
        }

        /// <summary>Desktop clicks that brought a displayed instrument (tests).</summary>
        public int DesktopSummons { get; private set; }

        /// <summary>A left click on a displayed instrument: its pick collider (Layers.PlaySurface colliders under its rig: the
        /// stubs' click target, the classic rigs' desktop colliders) when that is the nearest play surface under the mouse, else
        /// its displayed boxes. Bring(kind, Ray). False when the click is not on one (it stays the click it was).</summary>
        bool DesktopSummon(in Ray ray)
        {
            if (MrActive || _app.Registry == null) return false;
            InstrumentKind? best = null;
            if (Physics.Raycast(ray, out RaycastHit hit, 40f, Layers.PlaySurfaceMask, QueryTriggerInteraction.Collide))
            {
                Transform t = hit.collider.transform;
                for (int i = 0; i < _rigs.Count; i++)
                {
                    Rig r = _rigs[i];
                    if (r.Group == null || !t.IsChildOf(r.Group)) continue;
                    if (IsRowRig(r.Id) && PlaceOf(KindOf(r.Id)) == InstrumentPlace.Display) best = KindOf(r.Id);
                    break;
                }
                if (best == null) return false;   // the nearest play surface is someone else's (the menu, an instrument at the seat)
            }
            else
            {
                float bestT = float.PositiveInfinity;
                for (int k = 0; k < _shownAt.Length; k++)
                {
                    StowPlan.Spot s = _shownAt[k];
                    if (s == null || PlaceOf((InstrumentKind)k) != InstrumentPlace.Display) continue;
                    _dragBoxes.Clear();
                    s.WorldBoxes(_dragBoxes);
                    for (int b = 0; b < _dragBoxes.Count; b++)
                    {
                        float t = RayBox(ray, _dragBoxes[b]);
                        if (t >= 0f && t < bestT) { bestT = t; best = (InstrumentKind)k; }
                    }
                }
            }
            if (best == null || !Bring(best.Value, BringReason.Ray)) return false;
            DesktopSummons++;
            return true;
        }

        /// <summary>geometry.mjs rayObb: the ray against a floor box with its height range; the distance or -1.</summary>
        static float RayBox(in Ray ray, in SeatPlanner.FloorObb box)
        {
            double px = ray.origin.x - box.Cx, pz = ray.origin.z - box.Cz;
            double[] p = { px * box.Ux + pz * box.Uz, 0.0, -px * box.Uz + pz * box.Ux };
            double[] v = { ray.direction.x * box.Ux + ray.direction.z * box.Uz, ray.direction.y, -ray.direction.x * box.Uz + ray.direction.z * box.Ux };
            double[] lo = { -box.Hx, box.Y0 - ray.origin.y, -box.Hz }, hi = { box.Hx, box.Y1 - ray.origin.y, box.Hz };
            double t0 = 0.0, t1 = 1e9;
            for (int i = 0; i < 3; i++)
            {
                if (System.Math.Abs(v[i]) < 1e-12) { if (p[i] < lo[i] || p[i] > hi[i]) return -1f; continue; }
                double a = (lo[i] - p[i]) / v[i], b = (hi[i] - p[i]) / v[i];
                if (a > b) { double s = a; a = b; b = s; }
                t0 = System.Math.Max(t0, a);
                t1 = System.Math.Min(t1, b);
                if (t0 > t1) return -1f;
            }
            return (float)t0;
        }

        /// <summary>The held rig turned in place about its footprint's centre; a turn into another
        /// rig is held back by the constraint (and goes through after UNSTICK_MS, like a drag).</summary>
        void TurnGrab(float yaw)
        {
            Transform g = _held.Group;
            float px = g.position.x, pz = g.position.z, pyaw = _curYaw;
            Vector3 pw = PivotWorld(g);
            PlacePivotAt(g, pw.x, pw.z, yaw);
            ConstrainGrab(px, pz, pyaw);
            PlaceRing();
            RefreshHeldZone();
        }
    }
}
