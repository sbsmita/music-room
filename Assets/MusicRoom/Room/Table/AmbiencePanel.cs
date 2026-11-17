// The ambience panel (spec 07 section 10, switchPanel): three brass toggles FIRE / WIND / SNOW on a walnut
// plate on the side table, an IInstrument of Kind Table at TickOrder 40 (spec 05 section 1.1). A fingertip that was
// already tracked last frame (active && wasActive) entering a toggle's zone, (dx^2 + dz^2) < .018^2 and .005 < y < .06
// in panel space, flips it once; it must leave before it can flip it again. Levers sit at +-31.5 degrees (source frame
// -+0.55 rad), the LED glows #ff8a2a x 1.4 when on (the three LEDs are one vertex-coloured mesh, LedStrip: its colours are
// rewritten on a flip, never per frame; no MaterialPropertyBlock on URP Lit).
// Flipping goes through IAmbienceSwitches.Set (lever, sound, event, snow).
// Desktop (ARCHITECTURE.md section 0): a left click whose nearest PlaySurface hit is one of
// this panel's click cylinders (hit meshes r .016 h .05 at (x, .03, 0)) flips that toggle; the mouse hand's
// pointers never poke, so a click flips exactly once.
using System;
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Room.Table
{
    public sealed class AmbiencePanel : IInstrument
    {
        public const int Order = 40;
        public const float TouchRadius = 0.018f, TouchMinY = 0.005f, TouchMaxY = 0.06f;
        public const float LeverOnDeg = 31.513f;          // source rotation.x -0.55 rad, mirrored
        public const float LedOnIntensity = MusicRoom.Room.Render.RoomMaterials.LedOnIntensity;
        public const float ControlReach = 0.035f;          // BUTTON_R: a pinch this close over a lever is a press
        /// <summary>The hit mesh: a cylinder r .016 (top and bottom), h .05 at (x, .03, 0).</summary>
        public const float HitRadius = 0.016f, HitHeight = 0.05f, HitY = 0.03f;
        /// <summary>The desktop camera's far plane (ARCHITECTURE.md section 3.1): no click target is farther.</summary>
        public const float ClickReach = 40f;
        public static readonly float[] ToggleX = { -0.075f, 0f, 0.075f };
        public static readonly string[] Labels = { "FIRE", "WIND", "SNOW" };

        public sealed class Toggle
        {
            public AmbienceKind Kind;
            public float X;
            public bool On;
            public Transform Lever;
            public Collider Hit;
            public readonly List<Pointer> Held = new List<Pointer>(16);
        }

        readonly App _app;
        readonly Transform _panel;
        readonly Func<Rig> _rig;
        readonly Action<AmbienceKind, bool> _set;
        readonly LedStrip _leds;
        readonly List<Renderer> _played = new List<Renderer>(3);
        public readonly Toggle[] Toggles = new Toggle[3];

        /// <param name="app">For the desktop click path (camera, XR state, mouse hand); null in pure-logic use.</param>
        /// <param name="set">IAmbienceSwitches.Set: the one path that flips a switch (lever, LED, audio, event).</param>
        public AmbiencePanel(App app, Transform panel, Transform[] levers, LedStrip leds, Collider[] hits, Func<Rig> rig, Action<AmbienceKind, bool> set)
        {
            _app = app;
            _panel = panel;
            _rig = rig;
            _set = set;
            _leds = leds;
            for (int i = 0; i < 3; i++)
            {
                Toggles[i] = new Toggle { Kind = (AmbienceKind)i, X = ToggleX[i], Lever = levers[i], Hit = hits != null ? hits[i] : null };
                var lr = levers[i] != null ? levers[i].GetComponent<Renderer>() : null;
                if (lr != null) _played.Add(lr);
                Show(Toggles[i]);
            }
        }

        public Transform Panel => _panel;
        public LedStrip Leds => _leds;

        /// <summary>Lever angle and LED for the toggle's state.</summary>
        public void Show(Toggle t)
        {
            if (t.Lever != null) t.Lever.localRotation = Quaternion.Euler(t.On ? LeverOnDeg : -LeverOnDeg, 0f, 0f);
            _leds?.Set((int)t.Kind, t.On);
        }

        public void SetState(AmbienceKind kind, bool on)
        {
            var t = Toggles[(int)kind];
            t.On = on;
            Show(t);
        }

        // ------------------------------------------------------------------ desktop click

        /// <summary>The toggle whose click cylinder this collider is, else null.</summary>
        public Toggle ToggleOf(Collider c)
        {
            if (c == null) return null;
            for (int i = 0; i < Toggles.Length; i++) if (Toggles[i].Hit == c) return Toggles[i];
            return null;
        }

        /// <summary>Pointer down: the nearest PlaySurface hit along the ray; this panel acts only when that hit is
        /// one of its own cylinders (another module's nearer target wins). Returns true when a toggle flipped.</summary>
        public bool DesktopDown(Ray ray)
        {
            if (!Physics.Raycast(ray, out RaycastHit hit, ClickReach, Layers.PlaySurfaceMask, QueryTriggerInteraction.Collide)) return false;
            Toggle t = ToggleOf(hit.collider);
            if (t == null) return false;
            _set(t.Kind, !t.On);
            return true;
        }

        /// <summary>Desktop only (!IsPresenting): a plain left press (Shift / Alt belong to Layout's rig drag).</summary>
        void DesktopClick()
        {
            if (_app == null || _app.IsPresenting || !DesktopInput.MouseDown(0) || DesktopInput.Shift || DesktopInput.Alt) return;
            Camera cam = _app.Rig != null ? _app.Rig.HeadCamera : null;
            if (cam == null || !cam.isActiveAndEnabled) return;
            DesktopDown(cam.ScreenPointToRay(DesktopInput.MousePosition));
        }

        // ------------------------------------------------------------------ IInstrument

        public InstrumentKind Kind => InstrumentKind.Table;
        public int TickOrder => Order;
        public bool AcceptsStickPointers => false;
        public bool IsPresent => _panel != null && _panel.gameObject.activeInHierarchy && (_rig() == null || _rig().InScene);
        public Transform Root => _panel;
        public IReadOnlyList<Renderer> PlayedParts => _played;

        public void Tick(IReadOnlyList<Pointer> pointers, float dt)
        {
            DesktopClick();
            Localize(pointers, requireWasActive: true, skipMouse: true);
            // The order: toggle by toggle, every pointer (so simultaneous flips publish FIRE, WIND, SNOW in order).
            for (int ti = 0; ti < Toggles.Length; ti++)
            {
                Toggle t = Toggles[ti];
                for (int i = 0; i < _count; i++)
                {
                    if (!_live[i]) continue;
                    Pointer p = pointers[i];
                    if (Inside(_local[i], t.X))
                    {
                        if (!t.Held.Contains(p))
                        {
                            t.Held.Add(p);
                            _set(t.Kind, !t.On);
                        }
                    }
                    else t.Held.Remove(p);
                }
            }
        }

        // Pointer tips in panel space, computed once per pointer per call (not once per toggle).
        Vector3[] _local = new Vector3[16];
        bool[] _live = new bool[16];
        int _count;

        void Localize(IReadOnlyList<Pointer> pointers, bool requireWasActive, bool skipMouse)
        {
            _count = pointers.Count;
            if (_local.Length < _count) { _local = new Vector3[_count * 2]; _live = new bool[_count * 2]; }
            for (int i = 0; i < _count; i++)
            {
                Pointer p = pointers[i];
                _live[i] = p.Active && (!requireWasActive || p.WasActive) && !(skipMouse && p.FromMouse);
                if (_live[i]) _local[i] = _panel.InverseTransformPoint(p.Tip);
            }
        }

        /// <summary>The toggle's touch zone in panel space.</summary>
        public static bool Inside(Vector3 local, float toggleX)
        {
            float dx = local.x - toggleX, dz = local.z;
            return dx * dx + dz * dz < TouchRadius * TouchRadius && local.y < TouchMaxY && local.y > TouchMinY;
        }

        /// <summary>Hover: a glow over a lever when a fingertip is within 2.5 cm sideways and -2..+6 cm of its
        /// top. The mouse hand shows it too (a visual stand-in; only its pokes are muted).</summary>
        public void Hover(IReadOnlyList<Pointer> pointers, HoverGlowPool glow)
        {
            Localize(pointers, requireWasActive: false, skipMouse: false);
            for (int ti = 0; ti < Toggles.Length; ti++)
            {
                Toggle t = Toggles[ti];
                for (int i = 0; i < _count; i++)
                {
                    if (!_live[i]) continue;
                    Vector3 l = _local[i];
                    float h = l.y - 0.045f;
                    float sx = l.x - t.X, sz = l.z;
                    if (h < -0.02f || h > 0.06f || sx * sx + sz * sz > 0.025f * 0.025f) continue;
                    Vector3 pos = _panel.TransformPoint(t.X, 0.046f, 0f);
                    glow.Add(HoverGlowSpec.At(pos, _panel.up, 0.045f, 0.045f, 1f - Mathf.Max(0f, h) / 0.06f));
                }
            }
        }

        /// <summary>Over the table controls, for the levers: within BUTTON_R sideways and 0 < y < .1 of the panel.</summary>
        public PlaySurface PlaySurfaceAt(Vector3 world)
        {
            if (_panel == null || !_panel.gameObject.activeInHierarchy) return PlaySurface.None;
            Vector3 l = _panel.InverseTransformPoint(world);
            if (l.y <= 0f || l.y >= 0.1f) return PlaySurface.None;
            for (int ti = 0; ti < Toggles.Length; ti++)
            {
                float dx = l.x - Toggles[ti].X;
                if (dx * dx + l.z * l.z < ControlReach * ControlReach) return PlaySurface.Table;
            }
            return PlaySurface.None;
        }
    }

    /// <summary>The three switch LEDs as one mesh (MusicRoom/VertexColorUnlit): each LED's vertices carry the off or
    /// on colour, rewritten only when that switch flips. The colours fold the lit LED into fixed values: off
    /// = the #331a0a body as the room's dim light leaves it, on = emissive #ff8a2a x 1.4 (linear, clamped on output).</summary>
    public sealed class LedStrip
    {
        public static readonly Color Off = new Color(0.012f, 0.004f, 0.0012f, 1f);
        public static readonly Color On = TextureUtil.Hex("#ff8a2a").linear * MusicRoom.Room.Render.RoomMaterials.LedOnIntensity
            + TextureUtil.Hex("#331a0a").linear * 0.25f;

        readonly Mesh _mesh;
        readonly Color[] _colors;
        readonly int _perLed;
        readonly bool[] _on = new bool[3];

        public LedStrip(Mesh mesh, int verticesPerLed)
        {
            _mesh = mesh;
            _perLed = verticesPerLed;
            _colors = new Color[mesh.vertexCount];
            for (int i = 0; i < _colors.Length; i++) _colors[i] = Off;
            _mesh.colors = _colors;
        }

        public Mesh Mesh => _mesh;
        public int VerticesPerLed => _perLed;
        public bool IsOn(int led) => _on[led];

        /// <summary>The colour LED <paramref name="led"/> shows (its first vertex: all of its vertices share it).</summary>
        public Color ColorOf(int led) => _colors[led * _perLed];

        public void Set(int led, bool on)
        {
            if (_on[led] == on) return;
            _on[led] = on;
            Color c = on ? On : Off;
            for (int i = led * _perLed, end = i + _perLed; i < end; i++) _colors[i] = c;
            _mesh.colors = _colors;
        }
    }
}
