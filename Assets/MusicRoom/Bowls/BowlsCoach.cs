// What the bowls module tells Sunny and the lessons (bowls.md 7; review items 11 and 12), as plain C#: it watches the
// hand logic's events and the unit's stats once per frame and raises
//   signals  (InstrumentSignalEvent { Kind = Bowls, Signal = "bowls.<name>" } in phase B) for the tip triggers and successes
//   markers  bowlSing {i, u = seconds sung in this rub}: each full second of singing and at the end of a rub (lessons)
// and it holds the lesson rules: a strike target counts a live strike of >= 0.2 m/s on the target bowl; a rub target a
// bowlSing with u >= hold; after 8 s waiting Sunny re-demos; after 15 s any touch on the target bowl plays an audible
// 0.7 m/s strike and counts. Main thread; nothing allocates per frame once the queues have grown.
using System;
using System.Collections.Generic;
using MusicRoom.Audio.Burst;

namespace MusicRoom.Bowls
{
    public struct BowlsSignal { public double T; public string Name; public int Bowl; }
    public struct BowlsSingMarker { public double T; public int Bowl; public double Seconds; }

    /// <summary>What the coach reads from the unit each frame (IBowlsSound, or a test's numbers).</summary>
    public interface IBowlsStats
    {
        float SingVelocity(int bowl);
        float ContactLost(int bowl);
        float Level(int bowl);
    }

    public static class BowlsLessonRules
    {
        public const double MinStrikeSpeed = 0.2, RedemoAfter = 8.0, AnyTouchAfter = 15.0, AssistSpeed = 0.7;

        /// <summary>ILessonTargets.Matches for a strike target: a live strike (any kind) of >= 0.2 m/s on bowl i.</summary>
        public static bool MatchesStrike(int targetBowl, int strikeBowl, double vel, bool live) => live && strikeBowl == targetBowl && vel >= MinStrikeSpeed;

        /// <summary>ILessonTargets.Matches for a rub target: a live bowlSing marker on bowl i with u >= hold.</summary>
        public static bool MatchesRub(int targetBowl, double hold, int markerBowl, double secondsSung, bool live) => live && markerBowl == targetBowl && secondsSung >= hold - 1e-6;

        public enum Stall : byte { Waiting, Redemo, AnyTouch }
        public static Stall StallState(double waitingSeconds) => waitingSeconds >= AnyTouchAfter ? Stall.AnyTouch : waitingSeconds >= RedemoAfter ? Stall.Redemo : Stall.Waiting;

        /// <summary>After 15 s: a touch (or a too-soft tap) on the target bowl is answered with an audible strike and counts.</summary>
        public static bool AssistTouch(int targetBowl, in BowlsHandEvent e, double waitingSeconds)
            => StallState(waitingSeconds) == Stall.AnyTouch && e.Bowl == targetBowl && (e.Type == BowlsHandEventType.Touch || e.Type == BowlsHandEventType.Tap || e.Type == BowlsHandEventType.RubContact);
    }

    public sealed class BowlsCoach
    {
        public const float SingAt = 0.15f;
        public const double ChatterLost = 0.2, ChatterFor = 1.0, SlowSpeed = 0.08, SlowFor = 3.0, InnerFor = 2.0, RingingDb = -30, RingingFor = 8.0;
        public const double Tapped3Delay = 2.0, SlowStart = 4.0, Unplayed = 120.0;

        public readonly List<BowlsSignal> Signals = new List<BowlsSignal>(32);
        public readonly List<BowlsSingMarker> Markers = new List<BowlsSingMarker>(32);

        readonly BowlsHands _hands;
        int _strikes, _palmStrikes, _sang;
        bool _everRubbed, _tapped3Sent, _tapped10Sent, _unplayedSent;
        double _thirdTapT = -1, _lastPlayT = -1e9, _tourDoneT = -1;
        // per bowl
        readonly bool[] _rubbing = new bool[3], _chatterSent = new bool[3], _slowSent = new bool[3], _innerSent = new bool[3], _sangThisRub = new bool[3], _ringCounted = new bool[3];
        readonly double[] _rubStartT = new double[3], _sungThisRub = new double[3], _nextMarker = new double[3], _lostFor = new double[3], _innerFor = new double[3], _ringFor = new double[3];
        readonly double[] _speedSum = new double[3]; readonly int[] _speedN = new int[3];
        readonly BowlSurface[] _surface = new BowlSurface[3];
        int _ringingEpisodes;
        bool _ringingSent, _firstSingSeen;
        double _t;

        public BowlsCoach(BowlsHands hands) { _hands = hands; }

        void Raise(double t, string name, int bowl = -1) => Signals.Add(new BowlsSignal { T = t, Name = name, Bowl = bowl });

        /// <summary>Every BowlsHands event (the module's sink forwards them).</summary>
        public void OnHandEvent(in BowlsHandEvent e)
        {
            switch (e.Type)
            {
                case BowlsHandEventType.Tap:
                    _strikes++; _lastPlayT = e.T;
                    if (e.StrikeKind == BowlStrikeKind.Suede) Raise(e.T, "bowls.suedeStrike", e.Bowl);
                    if (_strikes == 3) _thirdTapT = e.T;
                    if (_strikes >= 10 && _palmStrikes == 0 && !_tapped10Sent) { _tapped10Sent = true; Raise(e.T, "bowls.tapped10"); }
                    break;
                case BowlsHandEventType.PalmStrike:
                    _strikes++; _palmStrikes++; _lastPlayT = e.T; Raise(e.T, "bowls.palmStrike", e.Bowl);
                    break;
                case BowlsHandEventType.RubContact:
                    _rubStartT[e.Bowl] = e.T; _lastPlayT = e.T;
                    break;
                case BowlsHandEventType.RubStart:
                    _everRubbed = true; _lastPlayT = e.T;
                    _rubbing[e.Bowl] = true; _surface[e.Bowl] = e.Surface;
                    _sungThisRub[e.Bowl] = 0; _nextMarker[e.Bowl] = 1; _sangThisRub[e.Bowl] = false;
                    _chatterSent[e.Bowl] = _slowSent[e.Bowl] = _innerSent[e.Bowl] = false;
                    _lostFor[e.Bowl] = _innerFor[e.Bowl] = 0; _speedSum[e.Bowl] = 0; _speedN[e.Bowl] = 0;
                    if (e.Surface == BowlSurface.Outer) Raise(e.T, "bowls.rubOuter", e.Bowl);
                    break;
                case BowlsHandEventType.RubEnd:
                    if (_rubbing[e.Bowl] && _sungThisRub[e.Bowl] > 0) Markers.Add(new BowlsSingMarker { T = e.T, Bowl = e.Bowl, Seconds = _sungThisRub[e.Bowl] });
                    _rubbing[e.Bowl] = false;
                    break;
                case BowlsHandEventType.Damp:
                    if (e.Level == 3) Raise(e.T, "bowls.palmRest", e.Bowl);
                    break;
            }
        }

        /// <summary>Phase B: the platform says whether the bowls are at the seat and when Sunny's tour ended.</summary>
        public void SetPresence(bool atSeat, bool tourDone, double t)
        {
            if (!atSeat || !tourDone) { _tourDoneT = -1; return; }
            if (_tourDoneT < 0) _tourDoneT = t;
        }

        /// <summary>The puja's pick-up (BowlsPuja).</summary>
        public void OnPujaPicked(double t) { Raise(t, "bowls.pujaPick"); _lastPlayT = t; }

        public void Tick(double t, double dt, IBowlsStats stats)
        {
            _t = t;
            if (!_tapped3Sent && _thirdTapT >= 0 && t - _thirdTapT >= Tapped3Delay)
            {
                _tapped3Sent = true;
                if (!_everRubbed) Raise(t, "bowls.tapped3");
            }
            for (int i = 0; i < 3; i++)
            {
                float sv = stats.SingVelocity(i);
                bool rubbing = _rubbing[i] && _hands.Rubbing(i);
                if (rubbing)
                {
                    _speedSum[i] += _hands.RubSpeed(i); _speedN[i]++;
                    if (sv >= SingAt)
                    {
                        _sungThisRub[i] += dt;
                        if (_sungThisRub[i] >= _nextMarker[i]) { Markers.Add(new BowlsSingMarker { T = t, Bowl = i, Seconds = Math.Floor(_sungThisRub[i]) }); _nextMarker[i] += 1; }
                        if (!_sangThisRub[i] && _sungThisRub[i] >= 1.0)
                        {
                            _sangThisRub[i] = true; _sang++;
                            Raise(t, "bowls.sang", i);
                            if (_sang == 2) Raise(t, "bowls.sang2", i);
                            if (_sang == 3) Raise(t, "bowls.sang3", i);
                            if (_surface[i] == BowlSurface.Lip) Raise(t, "bowls.lipSang", i);
                            if (!_firstSingSeen) { _firstSingSeen = true; if (t - 1.0 - _rubStartT[i] > SlowStart) Raise(t, "bowls.slowStart", i); }
                        }
                    }
                    // chatter: the stick keeps losing the rim
                    _lostFor[i] = stats.ContactLost(i) > ChatterLost ? _lostFor[i] + dt : 0;
                    if (!_chatterSent[i] && _lostFor[i] >= ChatterFor) { _chatterSent[i] = true; Raise(t, "bowls.chatter", i); }
                    // too slow: 3 s of rubbing, mean speed under 0.08 m/s, no song
                    if (!_slowSent[i] && !_sangThisRub[i] && t - _rubStartT[i] >= SlowFor && _speedN[i] > 0 && _speedSum[i] / _speedN[i] < SlowSpeed) { _slowSent[i] = true; Raise(t, "bowls.slow", i); }
                    // the inside: an inner contact moving for 2 s
                    _innerFor[i] = _hands.RubSurface(i) == BowlSurface.Inner ? _innerFor[i] + dt : 0;
                    if (!_innerSent[i] && _innerFor[i] >= InnerFor) { _innerSent[i] = true; Raise(t, "bowls.inner", i); }
                }
                // ringing on and on after playing, untouched: the 2nd time, Sunny shows the palm
                double db = 20 * Math.Log10(Math.Max(1e-12, stats.Level(i)));
                bool touched = _hands.AnyTouch(i) || _hands.RubbingHand(i) >= 0;
                _ringFor[i] = db > RingingDb && !touched ? _ringFor[i] + dt : 0;
                if (_ringFor[i] >= RingingFor && !_ringCounted[i]) { _ringCounted[i] = true; _ringingEpisodes++; if (_ringingEpisodes == 2 && !_ringingSent) { _ringingSent = true; Raise(t, "bowls.ringing", i); } }
                if (_ringFor[i] == 0) _ringCounted[i] = false;
            }
            if (!_unplayedSent && _tourDoneT >= 0 && t - Math.Max(_tourDoneT, _lastPlayT) >= Unplayed) { _unplayedSent = true; Raise(t, "bowls.unplayed120"); }
        }
    }
}
