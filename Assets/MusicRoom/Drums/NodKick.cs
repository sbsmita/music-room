// The kick drum without feet (Docs/05-instruments.md section 3): hand tracking has no legs, so a quick nod of the head
// plays the kick. A nod is a fast downward pitch that reverses within a moment; its peak speed sets the loudness. Slow
// looks down (at the keys, the sticks) stay far below the threshold, and snap turns and placement jumps only change
// yaw, which this ignores. Plain logic: the module feeds it the head's forward vector in the NodKick phase.
using UnityEngine;

namespace MusicRoom.Drums
{
    public sealed class NodKick
    {
        public bool Enabled = true;
        float _lastPitch;
        bool _hasPitch;
        double _lastHit = double.NegativeInfinity;
        float _swing;     // how long the head has been pitching down fast
        float _peak;      // the fastest downward pitch rate of the current swing
        bool _armed = true;

        public int Fired { get; private set; }
        public float LastRate { get; private set; }

        /// <summary>The nod update. <paramref name="active"/> = presenting, not MR placing, no rig held, kit in
        /// the scene. Returns the kick velocity when a nod fired this frame, else 0 (the caller plays the hit).</summary>
        public float Tick(float dt, bool active, Vector3 headForward, double nowSeconds)
        {
            if (!Enabled || !active || dt <= 0f)
            {
                _hasPitch = false;
                _swing = 0f;
                return 0f;
            }
            float pitch = Mathf.Asin(Mathf.Clamp(headForward.normalized.y, -1f, 1f));
            if (!_hasPitch)
            {
                _lastPitch = pitch;
                _hasPitch = true;
                return 0f;
            }
            float rate = (_lastPitch - pitch) / dt;   // positive = nodding down
            _lastPitch = pitch;
            LastRate = rate;
            float fired = 0f;
            if (rate > NodSpec.NodSpeed)
            {
                _swing += dt;
                _peak = Mathf.Max(_peak, rate);
                // Fire on the swing itself, once, so the kick lands with the nod and not after it.
                if (_armed && _swing <= NodSpec.Window && nowSeconds - _lastHit > NodSpec.Debounce)
                {
                    fired = DrumMath.NodVelocity(_peak);
                    _lastHit = nowSeconds;
                    _armed = false;
                    Fired++;
                }
            }
            else
            {
                // The swing is over (head slowing, still or coming back up): re-arm for the next nod.
                _swing = 0f;
                _peak = 0f;
                if (rate < NodSpec.NodSpeed * NodSpec.Rearm) _armed = true;
            }
            return fired;
        }
    }
}
