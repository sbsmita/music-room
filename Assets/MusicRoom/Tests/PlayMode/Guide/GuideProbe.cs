// A stand-in for the menu, the layout and the drums (Order 45, before the Guide), passed in BootOptions.ExtraModules
// so the Guide's tests boot only Audio + Guide and drive the Core contracts and events directly.
using System;
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Tests.PlayMode.Guide
{
    public sealed class GuideProbe : AppModule, IMenuPanel, ILayoutService, IDrumsApi
    {
        App _app;
        public override int Order => 45;

        public override void Init(App app)
        {
            _app = app;
            app.Register<IMenuPanel>(this);
            app.Register<ILayoutService>(this);
            app.Register<IDrumsApi>(this);
        }

        // ---- IMenuPanel ----
        public bool Shown { get; set; }
        public MenuScreen Screen { get; set; } = MenuScreen.Home;
        public void Show(HandState hand = null) { Shown = true; _app.Events.Publish(new MenuEvent { Change = MenuChange.Open, Screen = Screen }); }
        public void Hide() { Shown = false; _app.Events.Publish(new MenuEvent { Change = MenuChange.Close, Screen = Screen }); }
        public void Toggle(HandState hand = null) { if (Shown) Hide(); else Show(hand); }
        public void Go(MenuScreen screen) { Screen = screen; _app.Events.Publish(new MenuEvent { Change = MenuChange.Screen, Screen = screen }); }
        public void Redraw() { }
        public Bounds? GrabZone => null;
        public Rig Rig => null;
        public Transform Root => null;   // IMenuPanel.Root and IDrumsApi.Root

        // ---- ILayoutService ----
        public LayoutMode Mode { get; set; }
        public InstrumentKind Current { get; set; }
        public bool IsTurning => false;
        public bool FirstArrangeDone { get; set; }
        public SeatFrame Seat => default;
        public void Set(LayoutMode mode, InstrumentKind instrument)
        {
            Mode = mode;
            Current = instrument;
            _app.Events.Publish(new LayoutChangedEvent { Mode = mode, Current = instrument });
        }
        public void Next(int direction) { }
        public void ArrangeAroundPlayer(bool force, bool movePiano = true) { FirstArrangeDone = true; }
        public void PlaceAroundPiano() { }
        public void RestoreHomes() { }
        public Pose Anchor => Pose.identity;
        public bool IsInScene(InstrumentKind instrument) => true;

        // ---- IDrumsApi ----
        public StickPick TryPickUp(HandState hand, Vector3 tip) => StickPick.None;   // the probe has no sticks
        public IReadOnlyList<DrumPieceInfo> Pieces => Array.Empty<DrumPieceInfo>();
        public int KickId => 4;
        public int KickPadId => 5;
        public int HatId => 6;
        public bool HatOpen => false;
        public void SetHatOpen(bool open, HandState hand, bool playback = false) { }
        public void PlaybackHit(string kind, float vel, DrumStrikeInfo strike) { }
        public void PlaybackChoke(string kind) { }
        public bool AnyStickHeld => false;
        public void ReleaseAllSticks() { }
        public bool PadFar = true;
        public float LastMetres;
        public int LastPiece = -1;
        public bool EveryHandPointerFartherThan(float metres, int pieceId)
        {
            LastMetres = metres;
            LastPiece = pieceId;
            return PadFar;
        }

        public void Kick(bool duringPlayback) =>
            _app.Events.Publish(new RecorderEvent { Event = TakeEvent.Drum("kick", 0.7f, 0.3f, "head", false), DuringPlayback = duringPlayback });
    }
}
