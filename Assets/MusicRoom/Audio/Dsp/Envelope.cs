// Per-sample parameter automation, for the subset of ramp types the synths use (spec 06 section 1.1):
//   SetValue      = setValueAtTime(v, now)
//   LinearTo      = linearRampToValueAtTime(v, now + n)
//   ExponentialTo = exponentialRampToValueAtTime(v, now + n)  v(k) = v0 * (v1 / v0)^(k / n)
//   Hold          = setValueAtTime(sameValue, later)
//   SetTarget     = cancelScheduledValues + setTargetAtTime(v, now, tau): v += (target - v) * (1 - e^(-1/tau))
// Next() returns the value AT the current sample, then advances. Fixed segment array: no allocation after construction.
using System;

namespace MusicRoom.Audio.Dsp
{
    public sealed class Envelope
    {
        private enum Kind : byte { Hold, Linear, Exponential }

        private struct Segment
        {
            public Kind Kind;
            public double Target;
            public int Length;      // samples until the target (ceil of Exact)
            public double Exact;    // the ramp's true length in samples (may be fractional)
        }

        private readonly Segment[] _segments;
        private int _count, _index, _left;
        private double _value, _step;
        private bool _targetMode;
        private double _target, _coef;

        public Envelope(int maxSegments = 6) { _segments = new Segment[maxSegments]; }

        public double Value => _value;

        /// <summary>True when no ramp is running (the value is constant).</summary>
        public bool IsSettled => !_targetMode && _index >= _count;

        /// <summary>True when the value is below eps and stays there: settled, or a setTarget whose target is below eps too.</summary>
        public bool IsQuiet(double eps) =>
            Math.Abs(_value) < eps && (_targetMode ? Math.Abs(_target) < eps : _index >= _count);

        public void SetValue(double v)
        {
            _value = v;
            _count = _index = _left = 0;
            _targetMode = false;
        }

        public void LinearTo(double target, int samples) => Push(Kind.Linear, target, samples);

        /// <summary>Both ends must be non-zero and of the same sign (exponential ramp rule).</summary>
        public void ExponentialTo(double target, int samples) => Push(Kind.Exponential, target, samples);

        /// <summary>exponentialRampToValueAtTime over a fractional length: sample k of the ramp is v0 (v1/v0)^(k / samples)
        /// for k &lt; samples, then v1 (the ramp is evaluated at the true frame time; a 9.8 ms knock is 470.4 samples).</summary>
        public void ExponentialTo(double target, double samples) => Push(Kind.Exponential, target, samples);

        public void Hold(int samples) => Push(Kind.Hold, 0, samples);

        public void SetTarget(double target, double tauSamples)
        {
            _count = _index = _left = 0;
            _targetMode = true;
            _target = target;
            _coef = tauSamples <= 0 ? 1.0 : 1.0 - Math.Exp(-1.0 / tauSamples);
        }

        private void Push(Kind kind, double target, double samples)
        {
            if (_count >= _segments.Length) return; // never allocate on the audio thread: extra segments are dropped
            _targetMode = false;
            double exact = Math.Max(1, samples);
            _segments[_count++] = new Segment { Kind = kind, Target = target, Length = (int)Math.Ceiling(exact), Exact = exact };
            if (_index == _count - 1 && _left == 0) Enter();
        }

        private void Enter()
        {
            if (_index >= _count) return;
            ref Segment s = ref _segments[_index];
            _left = s.Length;
            switch (s.Kind)
            {
                case Kind.Linear:
                    _step = (s.Target - _value) / s.Exact;
                    break;
                case Kind.Exponential:
                    if (_value == 0 || s.Target == 0 || (_value > 0) != (s.Target > 0)) { _value = s.Target; _step = 1; }
                    else _step = Math.Pow(s.Target / _value, 1.0 / s.Exact);
                    break;
                default:
                    _step = 0;
                    break;
            }
        }

        public float Next()
        {
            double v = _value;
            if (_targetMode)
            {
                _value += (_target - _value) * _coef;
                return (float)v;
            }
            if (_index >= _count) return (float)v;
            ref Segment s = ref _segments[_index];
            if (s.Kind == Kind.Linear) _value += _step;
            else if (s.Kind == Kind.Exponential) _value *= _step;
            if (--_left <= 0)
            {
                if (s.Kind != Kind.Hold) _value = s.Target; // land exactly on the breakpoint
                _index++;
                Enter();
            }
            return (float)v;
        }
    }
}
