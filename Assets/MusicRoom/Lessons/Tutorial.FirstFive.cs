// The tutorial's first-five-minutes parts (Docs/11-first-five/sunny-tour.md 2.5 and 7.4, tutorials-audit.md B1 / B2),
// on the platform's tutorial (its LessonOptions NoCountdown / Accompany / QuietMistakes / Forgiving and its accompaniment
// through ILessonAccompany or the piano sound are Tutorial.cs's own):
//   - ILessonLive: the live target for Sunny's ghost hand, the forgiving switch the first run flips at its give-up.
//   - TutorialHitEvent for every play that lands on the lesson (hit or wrong), on every path.
//   - A hidden lesson (the first run's song, Lessons/Resources/lessons-firstrun.json): Start(id) finds it, the library,
//     the menu, the sheet music and the Lessons grid never list it.
//   - B1 on the classic drums: a target lands at 0.55 of the radius toward the player (0.45 on the hi-hat, whose top zone
//     is small), on the surface (cymbals rise above their strike plane), the kick pad's at its centre.
//   - B2 on the classic path: playing a target of the current event again, or the previous event's target within 120 ms
//     (a bounce, a re-triggered chord key), is not a mistake (the generic path has its own B2).
// Allocation-free per frame and per play.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Lessons
{
    public sealed partial class Tutorial
    {
        /// <summary>B1: landing radius toward the player (of the piece's radius) and the hi-hat's smaller one.</summary>
        public const float LandFraction = 0.55f, LandFractionHat = 0.45f;
        /// <summary>B1: the cymbal surface above its strike plane at 0.55 r (DrumSpec.CymbalProfile) plus 2 mm; the hi-hat's top.</summary>
        public const float CymbalLift = 0.009f, HatLift = 0.010f;
        /// <summary>B1: the off-centre disc and glow, small enough to stay on the head.</summary>
        public const float LandDiscFraction = 0.36f, LandGlowPerRadius = 0.9f;
        /// <summary>B2: the previous event's target struck again within this long is a bounce, not a mistake.</summary>
        public const float RepeatWindow = 0.12f;
        /// <summary>Where the ghost finger presses a white key: its front 2 cm, in front of the resting block (KeyZWhite).</summary>
        public const float KeyFrontPress = 0.02f;

        readonly System.Collections.Generic.Dictionary<string, Lesson> _hidden = new System.Collections.Generic.Dictionary<string, Lesson>();
        string _lastType, _lastKind;
        int _lastMidi = -1, _lastString = -1;
        double _lastHitAt = double.NegativeInfinity;

        public bool Quiet => (_options & LessonOptions.QuietMistakes) != 0;

        /// <summary>ILessonLive: any key of the lesson's instrument plays the next target (switchable live).</summary>
        public bool Forgiving
        {
            get => (_options & LessonOptions.Forgiving) != 0;
            set
            {
                if (value) _options |= LessonOptions.Forgiving;
                else _options &= ~LessonOptions.Forgiving;
            }
        }

        /// <summary>A lesson the menu never lists (the first run's song): Start(id) finds it, the library and the menu do not.</summary>
        public void AddHidden(Lesson lesson) { if (lesson?.id != null) _hidden[lesson.id] = lesson; }

        Lesson Find(string id) => _library?.Get(id) ?? (id != null && _hidden.TryGetValue(id, out Lesson l) ? l : null);

        // B2: the previous event's target struck again at once (a bounce on the hi-hat, a key re-triggered).
        bool RepeatOfPrevious(in TakeEvent ev)
        {
            if (_time - _lastHitAt > RepeatWindow || ev.type != _lastType) return false;
            if (ev.type == TakeEventType.PianoOn) return ev.midi == _lastMidi;
            if (ev.type == TakeEventType.Drum) return ev.kind == _lastKind;
            if (ev.type == TakeEventType.Harp) return ev.i == _lastString;
            return false;
        }

        void Remember(in TakeEvent ev)
        {
            _lastType = ev.type;
            _lastMidi = ev.midi;
            _lastKind = ev.kind;
            _lastString = ev.i;
            _lastHitAt = _time;
        }

        void PublishHit(bool hit) => _events?.Publish(new TutorialHitEvent { LessonId = LessonId, EventIndex = _cur, Hit = hit });

        // ---------------- B1: where a drum target lands ----------------

        void LandOn(in DrumPieceInfo piece, out float land, out float lift)
        {
            if (piece.Pad) { land = 0f; lift = DiscLift; return; }
            bool hat = piece.Kind == "hat";
            land = piece.Radius * (hat ? LandFractionHat : LandFraction);
            lift = piece.Type == DrumPieceType.Cymbal ? (hat ? HatLift : CymbalLift) : DiscLift;
        }

        /// <summary>The block's landing point in its piece's frame at height y: Land metres from the centre toward the player's
        /// eyes, in the piece's strike plane (recomputed every frame, so it follows a kit that is moved or turned).</summary>
        Vector3 LandPoint(Block b, float y)
        {
            if (b.Land <= 0f || b.Piece == null || _stage == null) return new Vector3(0f, y, 0f);
            Vector3 eye = b.Piece.InverseTransformPoint(_stage.EyePosition);
            Vector2 d = new Vector2(eye.x, eye.z);
            float m = d.magnitude;
            if (m < 1e-4f) return new Vector3(0f, y, 0f);
            d *= b.Land / m;
            return new Vector3(d.x, y, d.y);
        }

        /// <summary>Tests: a drum target's landing point in its piece's frame (no block needed).</summary>
        public Vector3 DrumLanding(in DrumPieceInfo piece)
        {
            LandOn(piece, out float land, out float lift);
            var b = new Block { Piece = piece.Transform, Land = land };
            return LandPoint(b, lift);
        }

        // ---------------- ILessonLive ----------------

        public bool WaitingForPlayer => State == TutorialState.Waiting;

        /// <summary>The first unplayed target of the event the player is on: the key's front 2 cm (in front of the resting
        /// block), the drum target's landing point, the string's play point; another instrument's landing
        /// (ILessonTargets.TryGetLanding).</summary>
        public bool TryGetLiveTarget(out Vector3 world, out Vector3 up, out InstrumentKind kind, out int midiOrString)
        {
            world = default; up = Vector3.up; kind = _instrument; midiOrString = -1;
            if (State != TutorialState.Playing && State != TutorialState.Waiting) return false;
            if (_cur >= _runCount || _stage == null) return false;
            Run r = _runs[_cur];
            int k = FirstUnhit(r);
            if (k < 0) return false;
            LessonTarget t = r.Targets[k];
            if (_generic)
            {
                if (_targets == null || !_targets.TryGetLanding(t, out Pose landing, out _, out _)) return false;
                world = landing.position;
                up = landing.rotation * Vector3.up;
                midiOrString = t.midi;
                return true;
            }
            if (_instrument == InstrumentKind.Piano)
            {
                IPianoApi piano = _stage.Piano;
                if (piano?.Root == null || !piano.TryGetKey(t.midi, out PianoKeyInfo key)) return false;
                world = piano.Root.TransformPoint(key.X, key.Black ? piano.BTOP : 0f, key.Black ? KeyZBlack - 0.03f : KeyFrontPress);
                up = piano.Root.up;
                midiOrString = t.midi;
                return true;
            }
            if (_instrument == InstrumentKind.Drums)
            {
                if (!TryPiece(t.kind, out DrumPieceInfo piece)) return false;
                world = piece.Transform.TransformPoint(DrumLanding(piece));
                up = piece.Transform.up;
                return true;
            }
            IHarpApi harp = _stage.Harp;
            if (harp?.Root == null || t.i < 0 || t.i >= harp.StringCount) return false;
            world = harp.Root.TransformPoint(harp.PlayPoint(t.i));
            up = -harp.Root.forward;
            midiOrString = t.i;
            return true;
        }
    }
}
