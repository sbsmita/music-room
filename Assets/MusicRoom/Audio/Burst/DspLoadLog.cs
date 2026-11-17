// Development-build "dsp load" log (spec 10-upgrade/burst.md section 13): the once-a-second [Audio] dsp line for the
// device log, nothing on screen (the head-locked HUD text it used to draw is gone: headset feedback). Added by AudioModule
// only in development players (Debug.isDebugBuild && !Application.isEditor), or in the editor on demand
// (DspLoadLog.LogInEditor). Line format (filter the device log for '[Audio] dsp'):
//   [Audio] dsp avg 412us max 1320us p99 650us block 5333us load 7.7%/24.8% over 0 gaps 0 groups 214/1200 lod 0 drop 0 sr 48000 buf 256x4 work ...
using System.Text;
using UnityEngine;

namespace MusicRoom.Audio.Burst
{
    public sealed unsafe class DspLoadLog : MonoBehaviour
    {
        /// <summary>Editor: add the logger in Play mode too (default off: tests run there).</summary>
        public static bool LogInEditor;

        public AudioGraphHost Host;
        public bool LogLines = true;
        /// <summary>The last logged line (tests, tools).</summary>
        public string LastLine { get; private set; }
        readonly StringBuilder _sb = new StringBuilder(320);
        float _nextLog;
        int _dspFrames, _dspBuffers;

        void Start() => AudioSettings.GetDSPBufferSize(out _dspFrames, out _dspBuffers);

        void LateUpdate()
        {
            var host = Host;
            if (host == null || host.IsDisposed || !LogLines) return;
            float now = Time.unscaledTime;
            if (now < _nextLog) return;
            _nextLog = now + 1f;
            var s = host.Load.Read();
            _sb.Clear();
            _sb.Append(Line(host, s, _dspFrames, _dspBuffers));
            AppendUnits(_sb, host.State);
            LastLine = _sb.ToString();   // development players only: one string a second
            Debug.Log(LastLine);
        }

        /// <summary>The units that did work in the last block, each with its part of the units' work and its governor share
        /// below 1 (12-new-instruments/platform.md 10.4: units report UnitSlot.Work; per-unit timing is not measured).</summary>
        static void AppendUnits(StringBuilder sb, GraphState* g)
        {
            long total = 0;
            for (int k = 0; k < g->UnitCount; k++) if (g->Units[k].Registered != 0) total += System.Threading.Volatile.Read(ref g->Units[k].Work);
            if (total <= 0) return;
            sb.Append(" work");
            for (int k = 0; k < g->UnitCount; k++)
            {
                UnitSlot* u = g->Units + k;
                int w = System.Threading.Volatile.Read(ref u->Work);
                if (u->Registered == 0 || (w == 0 && u->Share >= 1f)) continue;
                sb.Append(' ').Append(u->Id.ToString()).Append(' ').Append((100.0 * w / total).ToString("0")).Append('%');
                if (u->Share < 1f) sb.Append(" x").Append(u->Share.ToString("0.00"));
            }
        }

        /// <summary>The once-a-second log line (without the units' work).</summary>
        public static string Line(AudioGraphHost host, in DspLoadMeter.Snapshot s, int dspFrames, int dspBuffers)
        {
            GraphState* g = host.State;
            return $"[Audio] dsp avg {s.AvgMicros:0}us max {s.MaxMicros:0}us p99 {s.P99Micros:0}us block {s.BlockMicros:0}us load {s.AvgLoad * 100:0.0}%/{s.MaxLoad * 100:0.0}% over {s.OverBudget} gaps {s.Gaps} groups {g->ActiveGroups}/{g->Budget} lod {g->LodDropped} drop {host.DroppedCommands + g->DroppedEvents} sr {host.SampleRate} buf {dspFrames}x{dspBuffers}";
        }
    }
}
