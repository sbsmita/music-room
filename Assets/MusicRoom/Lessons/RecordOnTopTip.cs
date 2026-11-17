// Sunny's one line about recording on top (2026-11-17, Recorder.RecordOnTop): "Press REC while it plays to add a part on
// top." Asked the first time a take (the deck's or one from Recordings; never a loop) plays to its very end: AskAfter
// later, then again every AskEvery while Sunny is busy, for at most AskFor, and only while the deck rests (nothing records
// or plays, no loop: the player has not moved on). Once said (or heard before, or once the player has recorded on top by
// themselves) it is never asked again. A later take's end asks again when this one gave up.
// Plain C# (the module ticks it in the Recorder phase); allocation-free.
using System;
using MusicRoom.Core;

namespace MusicRoom.Lessons
{
    public sealed class RecordOnTopTip
    {
        public const string TipId = "tip_record_on_top";
        /// <summary>Seconds after the take's end before the first ask (the show's own end lines come first), the retry
        /// period while Sunny is busy, and how long a take's end keeps asking.</summary>
        public const float AskAfter = 2.5f, AskEvery = 4f, AskFor = 30f;

        readonly Recorder _rec;
        readonly Func<IGuide> _guide;
        int _ends;
        bool _wanted;
        float _time, _askAt, _until;

        public RecordOnTopTip(Recorder recorder, Func<IGuide> guide)
        {
            _rec = recorder;
            _guide = guide;
            _ends = recorder != null ? recorder.TakeEnds : 0;
        }

        /// <summary>Said (or heard before, or the player found it): never asked again.</summary>
        public bool Done { get; private set; }
        /// <summary>Times Sunny was asked (tests).</summary>
        public int Asks { get; private set; }

        public void Tick(float dt)
        {
            if (Done || _rec == null) return;
            _time += dt;
            if (_rec.OnTopStarts > 0) { Done = true; return; }   // they found it themselves
            if (_rec.TakeEnds != _ends)
            {
                _ends = _rec.TakeEnds;
                _wanted = true;
                _askAt = _time + AskAfter;
                _until = _time + AskFor;
            }
            if (!_wanted || _time < _askAt) return;
            bool resting = _rec.State == RecorderState.Idle && (_rec.Looper == null || !_rec.Looper.Active);
            if (!resting || _time > _until) { _wanted = false; return; }   // moved on: the next take's end asks again
            _askAt = _time + AskEvery;
            IGuide g = _guide?.Invoke();
            if (g == null) return;
            if (g.TipSeen(TipId)) { Done = true; return; }
            Asks++;
            if (g.Tip(TipId) > 0f) Done = true;
        }
    }
}
