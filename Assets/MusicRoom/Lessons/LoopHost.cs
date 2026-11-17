// The looper's app side (ILoopHost): the audio clock, the instruments' playback calls inside the engine's ScheduleAt
// scope (ENGINE-API.md section 10: the call's sound starts at the event's exact sample, the instrument API unchanged), the
// metronome click, the playback release-all, the Sampled State values (the hi-hat pedal; other instruments' from the last
// live event), the instruments' sound paths (Classic sound), the registry's event roles, the displayed instruments' wake
// and Sunny's first-use tip. Everything is resolved lazily through Core contracts (no feature module is referenced).
using System;
using MusicRoom.Audio;
using MusicRoom.Core;

namespace MusicRoom.Lessons
{
    public sealed class AppLoopHost : ILoopHost
    {
        /// <summary>The first LOOP press: Sunny's one line (Docs/11-first-five/sunny-tour.md 5.3), said once ever.</summary>
        public const string TipLoopStart = "tip_loop_start";

        readonly App _app;
        readonly Recorder _recorder;
        IAudioEngine _audio;
        IDrumsApi _drums;

        public AppLoopHost(App app, Recorder recorder)
        {
            _app = app;
            _recorder = recorder;
        }

        IAudioEngine Audio => _audio ?? (_audio = _app.Get<IAudioEngine>());

        public double Now => _app.AudioTime;

        public int SampleRate
        {
            get
            {
                IAudioEngine a = Audio;
                return a != null && a.SampleRate > 0 ? a.SampleRate : 48000;
            }
        }

        public void Dispatch(in TakeEvent e, double when)
        {
            IAudioEngine a = Audio;
            if (a == null || when <= 0)
            {
                _recorder.Dispatch(e);
                return;
            }
            using (a.ScheduleAt(when)) _recorder.Dispatch(e, when);
        }

        public void Click(bool accent, double when) => Audio?.Click(accent, when);

        public void ReleaseAll() => _recorder.ReleaseAll();

        public bool HatOpen
        {
            get
            {
                if (_drums == null) _drums = _app.Get<IDrumsApi>();
                return _drums != null && _drums.HatOpen;
            }
        }

        public IInstrumentRegistry Registry => _app.Registry;

        /// <summary>The hi-hat pedal from the kit as before; any other Sampled State type from its last live event.</summary>
        public bool TrySample(string type, out TakeEvent e)
        {
            if (string.Equals(type, TakeEventType.Hat, StringComparison.Ordinal)) { e = TakeEvent.Hat(HatOpen); return true; }
            return _recorder.TryLastLive(type, out e);
        }

        public bool WantsWakes => true;

        public void Wake(in TakeEvent e) => _recorder.WakeOwner(e.type);

        public float StartTip()
        {
            IGuide guide = _app.Get<IGuide>();
            return guide != null ? guide.Tip(TipLoopStart) : 0f;
        }
    }
}
