// A stand-in ILayoutService for the menu's PlayMode tests while the real Layout's platform half (My set, Bring, the
// display, PlaceOf, Facing) is built in parallel (phase 0). Registered at Order 5, before Layout (100), so App.Get
// returns it; the real Layout still runs and arranges the room, and the probe forwards the classic members to it
// (FirstArrangeDone, Seat, the arrangement calls). My set, One alone, Bring, PutAway, PreviewJoin, PlaceOf and Facing are
// simulated here exactly as platform.md 4.2-4.4 describes them to the menu, and every change publishes
// LayoutChangedEvent (with SetVersion) and InstrumentMoveEvents, as Layout will.
using System.Collections.Generic;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Tests.PlayMode.Menu
{
    public sealed class GridLayoutProbe : AppModule, ILayoutService
    {
        App _app;
        ILayoutService _real;
        public override int Order => 5;

        public LayoutMode Mode { get; set; } = LayoutMode.All;
        public InstrumentKind Current { get; set; } = InstrumentKind.Piano;
        public readonly List<InstrumentKind> Members = new List<InstrumentKind> { InstrumentKind.Piano, InstrumentKind.Drums, InstrumentKind.Harp };
        public readonly List<(InstrumentKind kind, BringReason reason)> Brings = new List<(InstrumentKind, BringReason)>();
        public readonly List<InstrumentKind> PutAways = new List<InstrumentKind>();
        public readonly List<(LayoutMode mode, InstrumentKind kind)> Sets = new List<(LayoutMode, InstrumentKind)>();
        public JoinPreview? Join;
        public InstrumentKind? FacingKind;
        public int Version;

        public override void Init(App app)
        {
            _app = app;
            app.Register<ILayoutService>(this);
        }

        /// <summary>The real Layout (the second ILayoutService registered).</summary>
        ILayoutService Real
        {
            get
            {
                if (_real != null) return _real;
                foreach (var l in _app.GetAll<ILayoutService>()) if (!ReferenceEquals(l, this)) { _real = l; break; }
                return _real;
            }
        }

        void Changed(InstrumentKind moved, MovePhase phase, BringReason reason)
        {
            Version++;
            _app.Events.Publish(new InstrumentMoveEvent { Kind = moved, Phase = phase, Reason = reason });
            _app.Events.Publish(new LayoutChangedEvent { Mode = Mode, Current = Current, SetVersion = Version });
        }

        // ---- forwarded to the real Layout
        public bool IsTurning => Real != null && Real.IsTurning;
        public bool FirstArrangeDone => Real == null || Real.FirstArrangeDone;
        public SeatFrame Seat => Real != null ? Real.Seat : default;
        public void Next(int direction) { }
        public void ArrangeAroundPlayer(bool force, bool movePiano = true) => Real?.ArrangeAroundPlayer(force, movePiano);
        public void PlaceAroundPiano() => Real?.PlaceAroundPiano();
        public void RestoreHomes() => Real?.RestoreHomes();
        public Pose Anchor => Real != null ? Real.Anchor : Pose.identity;

        // ---- simulated (platform.md 4.2-4.4)
        public void Set(LayoutMode mode, InstrumentKind instrument)
        {
            Sets.Add((mode, instrument));
            Mode = mode;
            Current = instrument;
            Changed(instrument, MovePhase.Arrived, BringReason.Menu);
        }

        public bool IsInScene(InstrumentKind k) => Mode == LayoutMode.All ? Members.Contains(k) : k == Current;
        public IReadOnlyList<InstrumentKind> MySet => Members;
        public int SetVersion => Version;
        public InstrumentPlace PlaceOf(InstrumentKind k) => IsInScene(k) ? InstrumentPlace.Seat : InstrumentPlace.Display;
        public InstrumentKind? Facing => Mode == LayoutMode.Single ? Current : FacingKind;

        public JoinPreview PreviewJoin(InstrumentKind k)
        {
            if (Join.HasValue) return Join.Value;
            if (Members.Contains(k)) return new JoinPreview(JoinAction.Already);
            return Members.Count < MySetRules.Max ? new JoinPreview(JoinAction.Add) : new JoinPreview(JoinAction.Replace, Members[0]);
        }

        public bool Bring(InstrumentKind k, BringReason reason)
        {
            Brings.Add((k, reason));
            if (Mode == LayoutMode.Single) { Current = k; Changed(k, MovePhase.Arrived, reason); return true; }
            JoinPreview p = PreviewJoin(k);
            if (p.Action == JoinAction.Add) Members.Add(k);
            else if (p.Action == JoinAction.Replace) Members[Members.IndexOf(p.Replaced)] = k;
            else return false;
            Changed(k, MovePhase.Arrived, reason);
            return true;
        }

        public bool PutAway(InstrumentKind k)
        {
            if (Mode != LayoutMode.All || Members.Count <= 1 || !Members.Remove(k)) return false;
            PutAways.Add(k);
            Changed(k, MovePhase.Displayed, BringReason.Menu);
            return true;
        }
    }
}
