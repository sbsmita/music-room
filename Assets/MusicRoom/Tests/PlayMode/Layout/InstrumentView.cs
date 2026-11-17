// A registry instrument as the layout tests see it, whether it is still a phase-0 stub (PlaceholderInstrument) or a real
// module (the handpan's HandpanInstrument, ...): its Rig (IRigRegistry), the rig group's transform, IsPresent (its
// IInstrument), Stowed (its IStowable). LiveKnocks is a stub's knock counter (0 for a real instrument: the "never ticked
// on display" checks then rest on IsPresent).
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Tests.PlayMode.Layout
{
    public sealed class InstrumentView
    {
        public IInstrument Instrument;
        public IStowable Stowable;
        public Rig Rig;
        public Transform transform => Rig?.Group;
        public GameObject gameObject => Rig?.Group != null ? Rig.Group.gameObject : null;
        public bool IsPresent => Instrument != null && Instrument.IsPresent;
        public bool Stowed => Stowable != null && Stowable.Stowed;
        public int LiveKnocks => (Instrument as PlaceholderInstrument)?.LiveKnocks ?? 0;

        /// <summary>The loaded instrument of kind <paramref name="k"/>, or null.</summary>
        public static InstrumentView Of(App app, InstrumentKind k)
        {
            if (app == null) return null;
            var v = new InstrumentView();
            foreach (IStowable s in app.GetAll<IStowable>()) if (s.Kind == k) v.Stowable = s;
            foreach (IInstrument i in app.Instruments) if (i.Kind == k) v.Instrument = i;
            v.Rig = app.Get<IRigRegistry>()?.Get(InstrumentIds.RigOf(k));
            return v.Stowable != null && v.Instrument != null && v.Rig != null ? v : null;
        }
    }
}
