// E4 (experience.md section 6): Fast Motion Mode only while drumming. Meta's FMM raises the hand-tracking camera to
// ~60 Hz but "may increase jitter, impacting high accuracy interactions like direct touch": that is the piano and the
// harp, so the manifest asks for the low-jitter default (QuestSetup: HandTrackingFrequency.LOW) and FMM is hinted at
// runtime only while the player drums, where a 3 m/s stick moves 10 cm between 30 Hz samples.
//   FastMotionPolicy  plain C#, the reference fmmFrame line for line (doubles, as in JavaScript), EditMode-tested;
//   FastMotionDriver  App step 18 (EndFrame): drum intent from the recorder stream, the layout and the sticks, hand
//                     confidence from the hand frames; sends the hint through MetaHandSource while the runtime is up,
//                     and publishes HandsLowLightEvent once when the low-light guard blocks FMM (Sunny's text hint).
// No allocation per frame.
using System.Collections.Generic;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.Core
{
    public sealed class FastMotionPolicy
    {
        /// <summary>A drum hit keeps the drumming intent this long (about two bars at 60 bpm).</summary>
        public const double RecentHit = 4.0;
        /// <summary>FMM goes off after this long without drumming intent (no flapping between piano and drums in All mode).</summary>
        public const double OffAfter = 5.0;
        /// <summary>At most one request per second (the off requests: on is never delayed).</summary>
        public const double MinGap = 1.0;
        /// <summary>Low-light guard: a block of LowConfWindow tracked hand-seconds with more than LowConfFrac of them at low
        /// confidence blocks FMM for the session (its short exposure degrades in a dim room); judged from LowConfAfterOn
        /// seconds after each on request.</summary>
        public const double LowConfFrac = 0.25, LowConfWindow = 3.0, LowConfAfterOn = 1.0;

        double _lastHit = -1e9, _quietSince = -1e9, _lastReq = -1e9, _onSince = -1e9, _lowT, _lowN;

        /// <summary>Our own record of the hint (the runtime has no getter and may ignore it).</summary>
        public bool On { get; private set; }
        /// <summary>The low-light guard tripped: no FMM for the rest of the session.</summary>
        public bool Blocked { get; private set; }
        /// <summary>The low-confidence share of the block that tripped the guard (the log line).</summary>
        public double BlockedFraction { get; private set; }
        public int Requests { get; private set; }
        public double LastRequestAt => _lastReq;

        /// <summary>One frame at time t (s). Returns +1 when FMM should be requested on now, -1 off, 0 no request.</summary>
        public int Frame(double t, double dt, bool drumHit, bool stickHeld, bool singleDrums, int handsTracked, int lowConfHands)
        {
            if (drumHit) _lastHit = t;
            bool want = !Blocked && (singleDrums || stickHeld || t - _lastHit < RecentHit);
            if (want) _quietSince = t;
            // Low-light guard: the low-confidence share of tracked hand-time, in blocks of LowConfWindow hand-seconds
            // (frame-rate independent).
            if (On && t - _onSince > LowConfAfterOn && handsTracked > 0)
            {
                _lowT += dt * handsTracked;
                _lowN += dt * lowConfHands;
                if (_lowT >= LowConfWindow)
                {
                    if (_lowN / _lowT > LowConfFrac) { Blocked = true; BlockedFraction = _lowN / _lowT; }
                    _lowT = 0;
                    _lowN = 0;
                }
            }
            int req = 0;
            if (!On && want) req = 1;   // on at once: never late for the first stroke
            else if (On && (Blocked || t - _quietSince >= OffAfter) && t - _lastReq >= MinGap) req = -1;
            if (req == 0) return 0;
            On = req > 0;
            _lastReq = t;
            Requests++;
            if (On) { _onSince = t; _lowT = 0; _lowN = 0; }
            return req;
        }
    }

    /// <summary>Feeds FastMotionPolicy from the app and sends its hint (App.Frame step 18).</summary>
    public sealed class FastMotionDriver
    {
        /// <summary>Dev builds: the hands' tracker interval is logged this often (FMM on ~16.7 ms, off ~33 ms).</summary>
        public const float SampleLogSeconds = 10f;

        readonly App _app;
        bool _hit, _sent, _blockLogged;
        float _logT;
        ILayoutService _layout;
        IDrumsApi _drums;

        public FastMotionPolicy Policy { get; } = new FastMotionPolicy();
        /// <summary>Requests that reached the runtime (tests read Policy; this counts the native calls).</summary>
        public int Sent { get; private set; }
        /// <summary>HandsLowLightEvent publications (at most one per session).</summary>
        public int LowLightEvents { get; private set; }

        public FastMotionDriver(App app)
        {
            _app = app;
            app.Events.Subscribe<RecorderEvent>(OnRecorder);
        }

        /// <summary>Drumming intent, generalised (platform.md 4.6): a live Onset, Release or State event of a FastMotion
        /// instrument (the kit's drum / drumChoke / hat as before; the hand drums' strokes).</summary>
        void OnRecorder(RecorderEvent e)
        {
            if (e.DuringPlayback) return;
            string type = e.Event.type;
            IInstrumentRegistry reg = _app.Registry;
            if (reg == null) return;
            InstrumentDescriptor d = reg.Get(reg.OwnerOfEvent(type));
            if (d == null || !d.Has(InstrumentTraits.FastMotion)) return;
            EventRole role = reg.RoleOf(type);
            if (role == EventRole.Onset || role == EventRole.Release || role == EventRole.State) _hit = true;
        }

        readonly List<IHandToolProvider> _tools = new List<IHandToolProvider>(4);
        int _toolsVersion = -1;

        /// <summary>Any tool held of a FastMotion instrument (the kit's sticks through DrumToolProvider, a mallet), or the kit
        /// itself saying so (a stand-in IDrumsApi in tests).</summary>
        bool AnyFastToolHeld()
        {
            if (_toolsVersion != _app.ServicesVersion)
            {
                _toolsVersion = _app.ServicesVersion;
                _tools.Clear();
                foreach (var t in _app.GetAll<IHandToolProvider>())
                {
                    InstrumentDescriptor d = _app.Registry?.Get(t.Owner);
                    if (d != null && d.Has(InstrumentTraits.FastMotion)) _tools.Add(t);
                }
            }
            for (int i = 0; i < _tools.Count; i++) if (_tools[i].AnyHeld) return true;
            return _drums != null && _drums.AnyStickHeld;
        }

        public void Tick(float dt)
        {
            if (_layout == null) _layout = _app.Get<ILayoutService>();
            if (_drums == null) _drums = _app.Get<IDrumsApi>();
            bool single = false;
            if (_layout != null && _layout.Mode == LayoutMode.Single)
            {
                InstrumentDescriptor cur = _app.Registry?.Get(_layout.Current);
                single = cur != null && cur.Has(InstrumentTraits.FastMotion);
            }
            bool stick = AnyFastToolHeld();
            int tracked = 0, low = 0;
            HandInput hands = _app.HandInput;
            if (hands != null && hands.Source != null)
                for (int h = 0; h < 2; h++)
                {
                    HandFrame f = hands.Get((HandSide)h);
                    if (f == null || !f.isTracked) continue;
                    tracked++;
                    if (!f.isHighConfidence) low++;
                }
            Policy.Frame(_app.Clock.Now, dt, _hit, stick, single, tracked, low);
            _hit = false;
            if (Policy.Blocked && !_blockLogged)
            {
                _blockLogged = true;
                Debug.Log($"[Hands] FMM blocked: low confidence {Policy.BlockedFraction:0.00} (dim room?)");
                LowLightEvents++;
                _app.Events.Publish(new HandsLowLightEvent { LowConfidenceFraction = Policy.BlockedFraction });   // once per session
            }
            Send(hands);
        }

        /// <summary>The native hint, only while the runtime is up (re-sent if it came up with the policy already on).</summary>
        void Send(HandInput hands)
        {
            MetaHandSource meta = hands != null ? hands.Meta : null;
            if (meta == null || !_app.Rig.IsXrActive) return;
            if (Debug.isDebugBuild && (_logT += _app.Clock.Dt) >= SampleLogSeconds)
            {
                _logT = 0f;
                Debug.Log($"[Hands] sampleInterval L {meta.Get(HandSide.Left).sampleInterval * 1000f:F1} ms R {meta.Get(HandSide.Right).sampleInterval * 1000f:F1} ms (FMM {(_sent ? "on" : "off")})");
            }
            if (Policy.On == _sent) return;
            bool ok = meta.TryRequestFastMotion(Policy.On, out int code);
            _sent = Policy.On;
            Sent++;
            Debug.Log($"[Hands] FMM request {(Policy.On ? "on" : "off")}: {(ok ? "ok" : "failed")} ({code}) (sampleInterval L {meta.Get(HandSide.Left).sampleInterval * 1000f:F1} ms R {meta.Get(HandSide.Right).sampleInterval * 1000f:F1} ms)");
        }
    }
}
