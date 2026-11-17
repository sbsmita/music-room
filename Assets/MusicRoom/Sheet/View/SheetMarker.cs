// The current-note marker (sheet.md 4): a soft warm tint over the next note's column, the colour of lamplight on paper,
// drawn by the page shader (a multiply inside a soft rect, page mm). It follows ITutorial: half strength during the
// countdown, full once playing, a slow breath of +-12 % at 0.5 Hz while the lesson waits for the note, a glide of 0.12 s
// (ease-out) to the next column on the same line, a fade out 0.1 s / in 0.12 s across a line or a page, and a fade out over
// 1.5 s when the lesson ends or stops. Mistakes change nothing. Allocation-free.
using UnityEngine;

namespace MusicRoom.Sheet
{
    public sealed class SheetMarker
    {
        public const float Strength = 0.55f;          // multiply strength at full (#F0C884 at 0.55)
        public const float Glide = 0.12f, FadeOut = 0.10f, FadeIn = 0.12f, EndFade = 1.5f, Breath = 0.12f, BreathHz = 0.5f;
        public const float CountdownLevel = 0.5f, RiseSeconds = 0.6f;

        Vector4 _from, _to, _rect;
        float _glideT = 1f;
        int _page = -1, _pendingPage = -1;
        Vector4 _pendingRect;
        float _level;            // 0..1 envelope (fades, the countdown level)
        float _swap = 1f;        // 1 = shown; going down to 0 for a jump across a line or page, then up again
        bool _jumping;
        float _time;
        bool _had;

        public Vector4 Rect => _rect;
        public int Page => _page;
        /// <summary>The shader's strength now (0 = no tint).</summary>
        public float Value { get; private set; }

        /// <summary>One frame. target: the current event's mark (page, rect mm) or page -1 for none; target level: 0 (none / done),
        /// CountdownLevel, or 1; waiting: the breath. open: the target page is in the open spread (else the tint waits).</summary>
        public void Tick(float dt, int targetPage, Vector4 targetRect, float targetLevel, bool waiting, bool open)
        {
            _time += dt;
            if (targetPage >= 0 && open)
            {
                if (!_had || _page < 0) { _page = targetPage; _rect = _from = _to = targetRect; _glideT = 1f; _had = true; _swap = 1f; _jumping = false; }
                else if (targetPage != _page || Mathf.Abs(targetRect.y - _to.y) > 1f)
                {
                    if (targetPage != _pendingPage || targetRect != _pendingRect) { _pendingPage = targetPage; _pendingRect = targetRect; _jumping = true; }
                }
                else if (targetRect != _to) { _from = _rect; _to = targetRect; _glideT = 0f; }
            }
            // a jump: fade out, move, fade in
            if (_jumping)
            {
                _swap -= dt / FadeOut;
                if (_swap <= 0f)
                {
                    _swap = 0f; _jumping = false;
                    _page = _pendingPage; _rect = _from = _to = _pendingRect; _glideT = 1f;
                    _pendingPage = -1;
                }
            }
            else if (_swap < 1f) _swap = Mathf.Min(1f, _swap + dt / FadeIn);
            if (_glideT < 1f)
            {
                _glideT = Mathf.Min(1f, _glideT + dt / Glide);
                float e = 1f - (1f - _glideT) * (1f - _glideT);   // ease-out
                _rect = Vector4.Lerp(_from, _to, e);
            }
            float want = targetPage >= 0 && open ? targetLevel : 0f;
            float rate = want > _level ? (want <= CountdownLevel ? 1f / 0.3f : 1f / RiseSeconds) : (want <= 0f ? 1f / EndFade : 1f / 0.3f);
            _level = Mathf.MoveTowards(_level, want, rate * dt);
            float breath = waiting ? 1f + Breath * Mathf.Sin(2f * Mathf.PI * BreathHz * _time) : 1f;
            Value = Mathf.Clamp01(_level * _swap * breath) * Strength;
            if (_level <= 0f && want <= 0f) { _had = false; }
        }

        public void Reset() { _had = false; _page = -1; _level = 0f; Value = 0f; _jumping = false; _swap = 1f; }
    }
}
