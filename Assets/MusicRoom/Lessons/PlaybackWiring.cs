// Recorder playback targets (spec 08 section 4.1): each recorded event type goes back
// to its instrument's playback API, resolved through Core contracts so this module never references an instrument
// assembly. The APIs are looked up per dispatch (cheap, allocation-free) because an instrument may register after us
// or be absent (tests): a missing instrument just drops its events. The closures are created once, here.
// The platform (platform.md 6.2): after the seven classic lines, every registered IInstrumentPlayback gets each of its
// descriptor's non-Marker types that is not wired yet (one closure per type, made once at wiring time); the release-all
// calls every playback's ReleaseAll, and a take's stop closes every playback's take lane.
using System;
using System.Collections.Generic;
using MusicRoom.Core;

namespace MusicRoom.Lessons
{
    public static class PlaybackWiring
    {
        public static void Wire(Recorder recorder, Func<IPianoApi> piano, Func<IDrumsApi> drums, Func<IHarpApi> harp)
        {
            recorder.SetTarget(TakeEventType.PianoOn, e => piano()?.PlaybackOn(e.midi, e.vel));
            recorder.SetTarget(TakeEventType.PianoOff, e => piano()?.PlaybackOff(e.midi));
            recorder.SetTarget(TakeEventType.Drum, e => drums()?.PlaybackHit(e.kind, e.vel, StrikeOf(e)));
            recorder.SetTarget(TakeEventType.DrumChoke, e => drums()?.PlaybackChoke(e.kind));
            recorder.SetTarget(TakeEventType.Hat, e => drums()?.SetHatOpen(e.open, null, true));
            recorder.SetTarget(TakeEventType.Harp, e =>
            {
                IHarpApi h = harp();
                if (h == null) return;
                // Playback pluck: the whole stroke when the event carries one, else the default pluck (.35, 0, through).
                if (string.IsNullOrEmpty(e.mode)) h.PlaybackPluck(e.i, e.vel);
                else h.PlaybackPluck(e.i, e.vel, e.u, e.speed, e.mode);
            });
            recorder.SetTarget(TakeEventType.HarpDamp, e => harp()?.PlaybackDamp(e.i));
            recorder.SetReleaseAll(() => piano()?.PlaybackReleaseAll());
        }

        /// <summary>The generic loop (platform.md 6.2): for every playback and each non-Marker type of its descriptor that has
        /// no target yet, recorder.SetTarget(type, e =&gt; pb.Play(e)). The release-all becomes the piano's (as Wire set it)
        /// plus every playback's ReleaseAll, and a take's stop calls CloseLane(PlaybackLanes.Take) on each. Returns the types
        /// wired by this call. Call again when instruments register later: wired types are skipped.</summary>
        public static int WireInstruments(Recorder recorder, IInstrumentRegistry registry, IReadOnlyList<IInstrumentPlayback> playbacks, Func<IPianoApi> piano = null)
        {
            if (recorder == null || registry == null || playbacks == null) return 0;
            int n = 0;
            var all = new IInstrumentPlayback[playbacks.Count];
            for (int i = 0; i < playbacks.Count; i++)
            {
                IInstrumentPlayback pb = playbacks[i];
                all[i] = pb;
                InstrumentDescriptor d = pb != null ? registry.Get(pb.Kind) : null;
                if (d?.Events == null) continue;
                foreach (EventTypeSpec spec in d.Events)
                {
                    if (spec.Role == EventRole.Marker || string.IsNullOrEmpty(spec.Type) || recorder.HasTarget(spec.Type)) continue;
                    recorder.SetTarget(spec.Type, e => pb.Play(e));
                    n++;
                }
            }
            recorder.SetReleaseAll(() =>
            {
                piano?.Invoke()?.PlaybackReleaseAll();
                for (int i = 0; i < all.Length; i++) all[i]?.ReleaseAll();
            });
            recorder.SetCloseLane(lane =>
            {
                for (int i = 0; i < all.Length; i++) all[i]?.CloseLane(lane);
            });
            return n;
        }

        /// <summary>Drum playback hit: { pos: e.pos ?? .3, zone: e.zone || 'head', open: !!e.open }. A flat struct has no
        /// "undefined", so an event without a zone (an old or hand-made take) is the default strike.</summary>
        public static DrumStrikeInfo StrikeOf(in TakeEvent e)
        {
            bool has = !string.IsNullOrEmpty(e.zone);
            return new DrumStrikeInfo { Pos = has ? e.pos : 0.3f, Zone = has ? e.zone : "head", Open = e.open };
        }
    }
}
