// Sunny's first-five-minutes parts (Docs/11-first-five/sunny-tour.md 4.4 and 7.1): the scripts (FirstRun, the tips)
// show any demo kind, world-anchored ones at the target (SunnyDemoWorld) and Sunny's own (palm, poke, swipe, nod) beside
// it; a poke pushes Sunny 1.5 cm along the finger and it springs back (no flash, no ring), its eyes blink and it smiles,
// with a soft "bop" (ChirpKind.Poke). Owner says which script drives Sunny in the Script state (the deck walkthrough
// leaves it null). Allocation-free.
using MusicRoom.Audio;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Guide
{
    public sealed partial class Sunny
    {
        /// <summary>The script driving Sunny in the Script state (FirstRun, TourTipRunner); null for the deck walkthrough.</summary>
        public object Owner;

        public const float PushSpeed = 0.62f, PushOmega = 25f, PushZeta = 0.7f;

        bool _worldDemo;
        DemoAnchor _anchor;
        Vector3 _push, _pushVel;

        public DemoAnchor Anchor => _anchor;
        /// <summary>The demo plays at a world anchor this frame.</summary>
        public bool WorldDemo => _worldDemo;
        /// <summary>Pokes felt (tests).</summary>
        public int Pokes { get; private set; }
        public Vector3 PushOffset => _push;

        /// <summary>A script shows a demo: a world kind at <paramref name="anchor"/> (without one: in front of Sunny), Sunny's
        /// own kinds (palm, poke, swipe, nod) beside it. The same kind again keeps its loop; another restarts it.</summary>
        public void ShowDemo(DemoKind kind, in DemoAnchor anchor)
        {
            if (kind == DemoKind.None) { EndDemo(); return; }
            if (DemoKind != kind) DemoT = 0f;
            DemoKind = kind;
            if (SunnyDemoWorld.IsWorld(kind))
            {
                _anchor = anchor.Has ? anchor : DemoAnchor.At(Position + Rotation * (SunnyDemo.HandRoot * Mathf.Max(1e-4f, Scale)) + Vector3.down * 0.04f, Vector3.up);
                _worldDemo = true;
                if (kind == DemoKind.Press) { PressAnchor = _anchor.Point; PressUp = _anchor.Up; }
            }
            else _worldDemo = false;
        }

        /// <summary>Ends whatever demo is shown.</summary>
        public void StopDemo() => EndDemo();

        // The Script state's demo clock (Sunny.UpdateDemo): Sunny's own kinds are posed here, world kinds after placement.
        bool UpdateScriptDemo(float dt)
        {
            if (DemoKind == DemoKind.None)
            {
                if (_demo.Visible) EndDemo();
                return true;
            }
            DemoT += dt;
            if (!_worldDemo) SunnyDemo.Evaluate(DemoKind, DemoT % GuideTiming.DemoLoop, ref _demo);
            return true;
        }

        void PoseWorldDemo(Vector3 pos, Quaternion rot)
        {
            Rotation = rot;
            Vector3 eye = _world.TryHead(out Vector3 e, out _) ? e : pos - rot * Vector3.forward;
            SunnyDemoWorld.Evaluate(DemoKind, DemoT % GuideTiming.DemoLoop, _anchor, pos, rot, Scale, eye, ref _demo);
        }

        /// <summary>A fingertip poked Sunny moving along <paramref name="direction"/>: the body gives 1.5 cm and springs back
        /// (about 0.25 s), the eyes blink, Sunny smiles, and a soft bop plays (the line in flight is the script's to cut).</summary>
        public void Poked(Vector3 direction)
        {
            Pokes++;
            Vector3 d = direction.sqrMagnitude > 1e-8f ? direction.normalized : Rotation * Vector3.forward;
            _pushVel += d * PushSpeed;
            _blinkT = GuideTiming.BlinkT;
            EyesShut = true;
            SetMood(SunnyMood.Happy);
            if (_voice != null && _voice.Available)
            {
                _voice.Chirp(ChirpKind.Poke);
                Chirps++;
            }
        }

        // A damped spring (omega 25 rad/s, zeta 0.7) in small steps: the push's offset this frame.
        Vector3 PokeStep(float dt)
        {
            if (_push.sqrMagnitude < 1e-10f && _pushVel.sqrMagnitude < 1e-8f) { _push = _pushVel = Vector3.zero; return Vector3.zero; }
            float h = dt;
            int n = Mathf.Clamp(Mathf.CeilToInt(dt / 0.004f), 1, 16);
            h /= n;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = -PushOmega * PushOmega * _push - 2f * PushZeta * PushOmega * _pushVel;
                _pushVel += a * h;
                _push += _pushVel * h;
            }
            return _push;
        }

        /// <summary>Sunny's usual spot beside the head (the perch a script starts from).</summary>
        /// <summary>The player's eye (the head), for the ghost hand's soft edges (SunnyDemoMesh.Eye).</summary>
        public bool TryEye(out Vector3 eye) => _world.TryHead(out eye, out _);

        public Vector3 HeadSpot()
        {
            if (!_world.TryHead(out Vector3 head, out Vector3 fwd)) return Base;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 g = head + fwd * GuideTiming.Ahead - right * GuideTiming.Left;
            g.y = head.y - GuideTiming.Down;
            return g;
        }

        /// <summary>Cuts the line in flight and takes the caption down (a skip: no closing chirp).</summary>
        public void CutLine()
        {
            StopVoice();
            BubbleVisible = false;
            LineVersion++;
        }
    }
}
