// The drum kit's platform adapters (12-new-instruments/platform.md 3.3, 13.3): thin forwards over today's kit.
//   IStowable            SetStowed(true): the static display configuration: both sticks let go and lying on the snare at
//                        once (their rest pose, no settle), the nod kick off; the stick glows and the kit's own tick stop
//                        with Rig.InScene (Layout's). SetStowed(false) gives the nod kick back.
//   IInstrumentPlayback  drum -> IDrumsApi.PlaybackHit(kind, vel, strike), drumChoke -> PlaybackChoke, hat ->
//                        SetHatOpen(open, null, playback) (Lessons/PlaybackWiring's classic lines); ReleaseAll: nothing is
//                        held by playback (hits ring and decay, chokes are events of their own); CloseLane: no lane.
//   IGuideAnchors        "piece.<kind>" (snare, tom1, tom2, floor, kick, hat, crash, ride): the strike surface's centre
//                        (the piece group's origin, its local y = 0 plane), up = its normal; "pad": the kick pad's.
// The sticks' IHandToolProvider is DrumToolProvider (DrumToolProvider.cs). Allocation-free.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Drums
{
    public sealed class DrumsPlatform : IStowable, IInstrumentPlayback, IGuideAnchors
    {
        public const string PiecePrefix = "piece.", Pad = "pad";
        readonly DrumsModule _module;
        bool _nodWas = true;

        public DrumsPlatform(DrumsModule module) { _module = module; }

        /// <summary>Creates the adapter and registers its three services (DrumsModule.Init).</summary>
        public static DrumsPlatform Register(App app, DrumsModule module)
        {
            var p = new DrumsPlatform(module);
            app.Register<IStowable>(p);
            app.Register<IInstrumentPlayback>(p);
            app.Register<IGuideAnchors>(p);
            return p;
        }

        DrumKit Kit => _module?.Kit;
        public InstrumentKind Kind => InstrumentKind.Drums;

        // ---------------------------------------------------------------- IStowable

        public bool Stowed { get; private set; }

        public void SetStowed(bool stowed, StowMount mount)
        {
            if (stowed)
            {
                DrumSticks sticks = _module?.Sticks;
                if (sticks != null)
                {
                    sticks.ReleaseAll();
                    // On the snare at once: a displayed kit is not ticked, so the 0.4 s settle would never run.
                    for (int i = 0; i < sticks.Count; i++)
                    {
                        Drumstick s = sticks[i];
                        s.Settle = 1f;
                        s.Root.localPosition = s.RestPos;
                        s.Root.localRotation = s.RestRot;
                        s.ResetVisual();
                    }
                }
                if (!Stowed && _module?.Nod != null) { _nodWas = _module.Nod.Enabled; _module.Nod.Enabled = false; }
            }
            else if (Stowed && _module?.Nod != null) _module.Nod.Enabled = _nodWas;
            Stowed = stowed;
        }

        // ---------------------------------------------------------------- IInstrumentPlayback

        public bool Play(in TakeEvent e)
        {
            DrumKit kit = Kit;
            if (kit == null) return false;
            if (e.type == TakeEventType.Drum) { kit.PlaybackHit(e.kind, e.vel, StrikeOf(e)); return true; }
            if (e.type == TakeEventType.DrumChoke) { kit.PlaybackChoke(e.kind); return true; }
            if (e.type == TakeEventType.Hat) { kit.SetHatOpen(e.open, null, true); return true; }
            return false;
        }

        /// <summary>Playback hit: { pos: e.pos ?? .3, zone: e.zone || 'head', open: !!e.open } (PlaybackWiring.StrikeOf).</summary>
        public static DrumStrikeInfo StrikeOf(in TakeEvent e)
        {
            bool has = !string.IsNullOrEmpty(e.zone);
            return new DrumStrikeInfo { Pos = has ? e.pos : 0.3f, Zone = has ? e.zone : "head", Open = e.open };
        }

        public void ReleaseAll() { }
        public void CloseLane(int lane) { }

        // ---------------------------------------------------------------- IGuideAnchors

        /// <summary>s[at..at+n) equals the first n characters of what (ordinal).</summary>
        static bool Matches(string s, int at, string what, int n)
        {
            if (s.Length < at + n || what.Length < n) return false;
            for (int i = 0; i < n; i++) if (s[at + i] != what[i]) return false;
            return true;
        }

        public bool TryGetAnchor(string anchor, out Vector3 world, out Vector3 up)
        {
            world = default;
            up = Vector3.up;
            DrumKit kit = Kit;
            if (kit == null || anchor == null) return false;
            var pieces = kit.Pieces;
            int id = -1;
            if (string.Equals(anchor, Pad, System.StringComparison.Ordinal)) id = kit.KickPadId;
            else if (Matches(anchor, 0, PiecePrefix, PiecePrefix.Length))
            {
                int n = anchor.Length - PiecePrefix.Length;
                for (int i = 0; i < pieces.Count && id < 0; i++)
                {
                    string k = pieces[i].Kind;
                    if (k != null && k.Length == n && n > 0 && !pieces[i].Pad && Matches(anchor, PiecePrefix.Length, k, n)) id = i;
                }
            }
            if (id < 0 || id >= pieces.Count || pieces[id].Transform == null) return false;
            Transform t = pieces[id].Transform;
            world = t.position;
            up = t.up;
            return true;
        }
    }
}
