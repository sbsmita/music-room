// Look and pinch, the menu's side (Core's IGazeMenu; the Gaze module holds the head gaze and the pinch, built on Meta's
// Interaction SDK, and asks the menu through this interface only). The head ray is cast against the face's own MeshCollider
// (Collider.Raycast: that collider only, no scene query), its uv gives canvas px like the desktop click path, the row under
// it is the target. Its hover is the poke's own hover (the face shader's row highlight, MenuController.GazeHover, only when no
// fingertip hovers a row), and the choice is fired inside the menu's own tick through MenuController.Fire, as a poke fires
// it (debounce, MenuRowEvent, the row's action). A row key carries the controller's layout count, so a key from an older
// layout (the screen changed under the gaze) is ignored. Allocation-free.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Menu
{
    public sealed partial class MenuModule : IGazeMenu
    {
        const int GazeRowBits = 5;   // MenuController.MaxRows 24 < 32
        int _gazeHoverKey = -1, _gazeFireKey = -1;
        double _gazeHoverAt = double.NaN;
        int _lookServices = -1;

        static int GazeKey(int relayouts, int row) => (relayouts << GazeRowBits) | row;

        /// <summary>The row of a key from the current layout (-1 when stale).</summary>
        int GazeRow(int key)
        {
            if (key < 0 || (key >> GazeRowBits) != _ctl.Relayouts) return -1;
            int row = key & ((1 << GazeRowBits) - 1);
            return row < _ctl.RowCount ? row : -1;
        }

        public bool GazeCast(in Ray ray, float maxDistance, out float distance, out Vector3 point, out Vector3 normal, out int rowKey, out Vector3 rowCentre)
        {
            distance = 0f; point = normal = rowCentre = default; rowKey = -1;
            if (_ctl == null || !_ctl.Shown || _view == null || _root == null || !_view.Collider.enabled) return false;
            if (_grab == null) _grab = _app.Get<IGrabService>();
            if (_grab != null && Rig != null && ReferenceEquals(_grab.HeldRig, Rig)) return false;
            if (!_view.Collider.Raycast(ray, out RaycastHit rh, maxDistance)) return false;
            float px = rh.textureCoord.x * MenuConstants.CanvasW;
            float py = (1f - rh.textureCoord.y) * MenuConstants.CanvasH;
            int row = _ctl.RowAt(px, py);
            if (row < 0) return false;
            MenuRow r = _ctl.Row(row);
            MenuMath.FromCanvas((r.X0 + r.X1) * 0.5f, (r.Y0 + r.Y1) * 0.5f, out float u, out float y);
            rowCentre = _root.TransformPoint(MenuMath.SurfacePoint(u, y, 0f));
            distance = rh.distance;
            point = rh.point;
            normal = rh.normal;
            rowKey = GazeKey(_ctl.Relayouts, row);
            return true;
        }

        public void GazeHover(int rowKey)
        {
            _gazeHoverKey = rowKey;
            _gazeHoverAt = _app.Clock.NowMs;
        }

        public bool GazeSelect(int rowKey)
        {
            if (_ctl == null || !_ctl.Shown || GazeRow(rowKey) < 0) return false;
            _gazeFireKey = rowKey;
            return true;
        }

        /// <summary>The menu's tick, right after BeginPokes: a pending look-and-pinch choice fires (and the Look service is
        /// found once it registers).</summary>
        void GazeFirePending()
        {
            if (_ctl.Look == null && _lookServices != _app.ServicesVersion)
            {
                _lookServices = _app.ServicesVersion;
                _ctl.Look = _app.Get<ILookAndPinch>();
            }
            if (_gazeFireKey < 0) return;
            int row = GazeRow(_gazeFireKey);
            _gazeFireKey = -1;
            if (row >= 0) _ctl.GazeFire(row);
        }

        /// <summary>The menu's tick, after the pokes: the gazed row of this frame lights (a fingertip's hover wins).</summary>
        void GazeHoverPending(double now)
        {
            if (_gazeHoverAt != now) return;
            int row = GazeRow(_gazeHoverKey);
            if (row >= 0 && _ctl.Shown) _ctl.GazeHover(row);
        }
    }
}
