// Poking Sunny skips (Docs/11-first-five/sunny-tour.md 4.1-4.2, the user's decision "poking Sunny skips the current
// step"), and where Sunny floats so a poke can never be confused with playing (4.3, 4.6).
//   Hit: a sphere of 0.065 m on Sunny's body (r 0.045 + 0.02 for tracker jitter), or the bubble's front face crossed
//        inside its rectangle (+1 cm). Not while Sunny arrives or leaves (scale < 0.5).
//   Who: each hand's index tip, never a stick, never the mouse hand's pointers.
//   Armed: the tip was at least 0.10 m from Sunny's centre (and off the bubble) since the last poke.
//   Fires: an armed tip enters, moving toward Sunny at 0.10 m/s or more, with the pointer live this frame and the last.
//   Guards (the poke is ignored, the hand disarmed): that hand played in the last 400 ms, it pinches (the grab pinch) or
//        holds a stick, the tip is over a play surface of an instrument, or over the menu face, the start panel or the
//        Done / Reset buttons (the menu wins), or mixed reality is placing the piano.
//   Debounce 0.6 s across both hands: one deliberate poke is one skip.
//   Desktop: a left click on Sunny's body.
// The perch: Sunny's spot is moved off every play zone (0.12 m clear) and beside a shown menu panel.
// Plain C#, ticked in the Guide phase (after the instruments and the grab: a same-frame play always wins). No allocation.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Guide
{
    public sealed class SunnyPoke
    {
        public const float BodyRadius = 0.065f, ArmDistance = 0.10f, MinSpeed = 0.10f, PlayGuardMs = 400f, Debounce = 0.6f;
        public const float BubbleHalfW = 0.13f + 0.01f, BubbleHalfH = 0.0575f + 0.01f, BubbleBehind = 0.01f, BubbleArm = 0.03f;

        readonly bool[] _armed = new bool[2];
        float _quiet;
        public int Fired { get; private set; }
        public int Guarded { get; private set; }
        public bool Armed(int hand) => _armed[hand];

        /// <summary>One frame: true when a poke fired (direction: the finger's motion).</summary>
        public bool Update(App app, Sunny sunny, Transform bubble, float dt, out Vector3 direction)
        {
            direction = default;
            _quiet -= dt;
            bool live = sunny.Visible && sunny.Scale >= 0.5f && sunny.State != SunnyState.Leave && sunny.State != SunnyState.Gone && sunny.State != SunnyState.Idle;
            if (!live || app.Input == null) { _armed[0] = _armed[1] = false; return false; }
            Vector3 c = sunny.Position;
            bool bubbleOn = bubble != null && bubble.gameObject.activeInHierarchy && sunny.BubbleOpacity > 0.8f;
            for (int h = 0; h < 2; h++)
            {
                HandState hand = app.Input.Hands[h];
                Pointer p = hand?.IndexPointer;
                if (hand == null || hand.IsMouse || p == null || !p.Active || !p.WasActive) { _armed[h] = false; continue; }
                Vector3 tip = p.Tip;
                float d = (tip - c).magnitude;
                bool nearBubble = bubbleOn && InBubbleBox(bubble, tip, BubbleArm);
                if (!_armed[h])
                {
                    if (d >= ArmDistance && !nearBubble) _armed[h] = true;
                    continue;
                }
                bool body = d <= BodyRadius;
                bool face = bubbleOn && CrossedBubble(bubble, p.Prev, tip);
                if (!body && !face) continue;
                _armed[h] = false;   // fired or guarded: the tip must go back out before it pokes again
                Vector3 toward = (c - p.Prev);
                float speed = toward.sqrMagnitude > 1e-8f ? Vector3.Dot(p.Vel, toward.normalized) : p.Vel.magnitude;
                if (face && !body) speed = Mathf.Max(speed, Vector3.Dot(p.Vel, bubble.forward));
                if (speed < MinSpeed) { Guarded++; continue; }
                if (Guard(app, hand, tip)) { Guarded++; continue; }
                if (_quiet > 0f) continue;
                _quiet = Debounce;
                direction = p.Vel.sqrMagnitude > 1e-8f ? p.Vel.normalized : (c - tip).normalized;
                Fired++;
                return true;
            }
            return false;
        }

        /// <summary>Desktop: a left click whose ray passes through Sunny's body.</summary>
        public bool Click(App app, Sunny sunny)
        {
            if (app.IsPresenting || !sunny.Visible || sunny.Scale < 0.5f || !DesktopInput.MouseDown(0)) return false;
            Camera cam = app.Rig != null ? app.Rig.HeadCamera : null;
            if (cam == null) return false;
            Ray ray = cam.ScreenPointToRay(DesktopInput.MousePosition);
            Vector3 oc = sunny.Position - ray.origin;
            float t = Vector3.Dot(oc, ray.direction);
            if (t < 0f || (oc - ray.direction * t).sqrMagnitude > BodyRadius * BodyRadius) return false;
            if (_quiet > 0f) return false;
            _quiet = Debounce;
            Fired++;
            return true;
        }

        static bool Guard(App app, HandState hand, Vector3 tip)
        {
            if (app.Clock.NowMs - hand.LastPlayMs < PlayGuardMs) return true;
            if (hand.GrabPinching || hand.Holding != null) return true;
            IMixedReality mr = app.Get<IMixedReality>();
            if (mr != null && mr.IsPlacing) return true;
            var all = app.Instruments;
            for (int i = 0; i < all.Count; i++)
            {
                IInstrument inst = all[i];
                if (inst.Kind == InstrumentKind.Menu || !inst.IsPresent) continue;
                if (inst.PlaySurfaceAt(tip) != PlaySurface.None) return true;
            }
            IMenuPanel menu = app.Get<IMenuPanel>();
            if (menu != null && menu.Shown && menu.Root != null && NearPanel(menu.Root, tip, 0.18f, 0.14f)) return true;
            IMenuGuideHooks hooks = app.Get<IMenuGuideHooks>();
            if (hooks != null && (hooks.TryGetRowRect("done", out Vector3 dc, out _, out _) && (dc - tip).sqrMagnitude < 0.12f * 0.12f)) return true;
            return false;
        }

        static bool NearPanel(Transform root, Vector3 tip, float halfW, float halfH)
        {
            Vector3 l = root.InverseTransformPoint(tip);
            return Mathf.Abs(l.x) < halfW && Mathf.Abs(l.y) < halfH && Mathf.Abs(l.z) < 0.05f;
        }

        static bool InBubbleBox(Transform bubble, Vector3 tip, float grow)
        {
            Vector3 l = bubble.InverseTransformPoint(tip);
            return Mathf.Abs(l.x) <= BubbleHalfW + grow && Mathf.Abs(l.y) <= BubbleHalfH + grow && Mathf.Abs(l.z) <= 0.03f + grow;
        }

        // The tip crossed the bubble's front face (toward Sunny) inside its rectangle. The bubble faces the player along
        // its local -z (Sunny's face side).
        static bool CrossedBubble(Transform bubble, Vector3 prev, Vector3 tip)
        {
            Vector3 a = bubble.InverseTransformPoint(prev), b = bubble.InverseTransformPoint(tip);
            float fa = -a.z, fb = -b.z;   // distance in front of the face
            if (!(fa >= 0f && fb < 0f && fb > -BubbleBehind - 0.02f)) return false;
            return Mathf.Abs(b.x) <= BubbleHalfW && Mathf.Abs(b.y) <= BubbleHalfH;
        }
    }

    /// <summary>Where a script perches Sunny: its spot moved clear of every play zone (0.12 m), and beside a shown menu panel
    /// so its body and bubble never cover the rows (sunny-tour.md 4.6, the analytic form).</summary>
    public static class SunnyPerch
    {
        public const float Clearance = 0.12f;
        static readonly Vector3[] s_probe =
        {
            Vector3.zero, new Vector3(Clearance, 0f, 0f), new Vector3(-Clearance, 0f, 0f), new Vector3(0f, Clearance, 0f),
            new Vector3(0f, -Clearance, 0f), new Vector3(0f, 0f, Clearance), new Vector3(0f, 0f, -Clearance),
        };

        public static Vector3 Clear(App app, Vector3 goal)
        {
            IMenuPanel menu = app.Get<IMenuPanel>();
            if (menu != null && menu.Shown && menu.Root != null) return BesidePanel(menu.Root);
            if (Free(app, goal)) return goal;
            Transform head = app.Head;
            if (head == null) return goal;
            Vector3 f = head.forward; f.y = 0f;
            f = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, f);
            Vector3 mirrored = goal + right * (2f * GuideTiming.Left);
            if (Free(app, mirrored)) return mirrored;
            Vector3 raised = goal + Vector3.up * Clearance;
            if (Free(app, raised)) return raised;
            Vector3 farther = goal + f * 0.10f;
            if (Free(app, farther)) return farther;
            // An instrument within a foot of the player (the keyboard over the knees) can fill the space under the spot:
            // higher, up to 0.48 m, until the probes are clear.
            for (int k = 2; k <= 4; k++)
            {
                Vector3 up = goal + Vector3.up * (Clearance * k);
                if (Free(app, up)) return up;
            }
            return raised;
        }

        /// <summary>Beside the panel: to its left and a little up (its bubble over the panel's top-left corner).</summary>
        public static Vector3 BesidePanel(Transform panel) => panel.position - panel.right * 0.27f + Vector3.up * 0.08f - panel.forward * 0.02f;

        public static bool Free(App app, Vector3 p)
        {
            var all = app.Instruments;
            for (int i = 0; i < all.Count; i++)
            {
                IInstrument inst = all[i];
                if (inst.Kind == InstrumentKind.Menu || !inst.IsPresent) continue;
                for (int k = 0; k < s_probe.Length; k++) if (inst.PlaySurfaceAt(p + s_probe[k]) != PlaySurface.None) return false;
            }
            return true;
        }
    }
}
