// Sunny's playback lines, one at a time (2026-11-11). The playback show has a line per part (the room listening back, the
// the ghost hands, the song's stars, the window, the painted walls, throwing paint, the framed painting, keeping a painting), and each part asks for its own as
// soon as it shows; on a first playback they all wanted Sunny at once. Here they take turns: in Order, a line is asked
// only once every line before it has been heard to the end (IGuide.TipSeen), and at most one of them is said per playback
// of a recording (RecordingPlaybackEvent Started counts the playbacks). tip_play_brings (StageId: Layout, said as a
// recording's instruments glide in) goes before them all whenever a playback brings instruments: while Layout wants to say
// it (Pending) the show's lines wait, and it counts as that playback's one line.
// So the first playback says what it does, and each later playback introduces one more part of the show.
// While the player records on top of a take (IRecorder.RecordingOnTop, 2026-11-17) no show line is asked.
// Per App (tests boot several): the state follows App.Current's event bus. Main thread; no allocation after the first
// subscription.
using System;

namespace MusicRoom.Core
{
    public static class PlaybackTips
    {
        /// <summary>The order the show's lines are introduced in, one per playback.</summary>
        public static readonly string[] Order =
        {
            "tip_listen_back", "tip_ghost_hands", "tip_song_stars", "tip_window_show", "tip_paint_walls",
            "tip_paint_throw", "tip_paint_gallery", "tip_paint_keep",
        };

        /// <summary>Layout's line for a playback that brings its instruments: first, outside the order.</summary>
        public const string StageId = "tip_play_brings";

        /// <summary>Layout wants to say StageId now: the show's lines wait.</summary>
        public static bool Pending;

        static EventBus _bus;
        static Action<RecordingPlaybackEvent> _onPlayback;
        static int _playback, _saidIn = -1;
        /// <summary>Show lines said in this app's run (one bit per Order index): a line cut short (not "heard to the end", so
        /// not TipSeen) still lets the next one have its turn; its own part does not ask it again.</summary>
        static int _saidMask;

        /// <summary>Playbacks of a recording started since this app booted.</summary>
        public static int Playback { get { Follow(); return _playback; } }

        static void Follow()
        {
            EventBus bus = App.Current?.Events;
            if (bus == _bus) return;
            _bus = bus;
            _playback = 0;
            _saidIn = -1;
            _saidMask = 0;
            Pending = false;
            if (bus == null) return;
            _onPlayback ??= e => { if (e.Started) _playback++; };
            bus.Subscribe(_onPlayback);
        }

        /// <summary>The index of a show line in Order, -1 for any other line.</summary>
        public static int IndexOf(string id)
        {
            for (int i = 0; i < Order.Length; i++) if (string.Equals(Order[i], id, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>Its turn now: every earlier line heard and no show line said yet in this playback.</summary>
        public static bool MayAsk(IGuide guide, string id)
        {
            Follow();
            if (RecordingOnTop()) return false;   // the player records on top of the playback: Sunny stays quiet (as for a REC)
            int at = IndexOf(id);
            if (guide == null) return true;
            if (at < 0) return !string.Equals(id, StageId, StringComparison.Ordinal) || _saidIn != _playback;
            if (_saidIn == _playback || Pending) return false;
            for (int i = 0; i < at; i++) if ((_saidMask & (1 << i)) == 0 && !guide.TipSeen(Order[i])) return false;
            return true;
        }

        static bool RecordingOnTop()
        {
            IRecorder r = App.Current?.Get<IRecorder>();
            return r != null && r.RecordingOnTop;
        }

        /// <summary>Ask Sunny for a show line when it is its turn. True once it is done with: heard before, or being said now
        /// (the caller then stops asking); false to ask again later.</summary>
        public static bool Ask(IGuide guide, string id)
        {
            if (guide == null) return false;
            if (guide.TipSeen(id)) return true;
            if (!MayAsk(guide, id)) return false;
            if (guide.Tip(id) <= 0f) return false;
            int at = IndexOf(id);
            if (at >= 0) { _saidIn = _playback; _saidMask |= 1 << at; }
            else if (string.Equals(id, StageId, StringComparison.Ordinal)) _saidIn = _playback;
            return true;
        }
    }
}
