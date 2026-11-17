// Pointer claims (platform.md 4.6, Core frame step 3b right after Input.Update): every active fingertip gets an owner for
// the frame, the first of (1) a present, non-contactless instrument in TickOrder whose rig Zone contains the tip (the side
// table's deck counts), (2) the shown menu's grab zone, (3) a registered IPointerClaimant (Sunny's poke sphere, the sheet
// music's pages), else null. Contact instruments ignore the claims (their own surface tests decide); contactless
// instruments (none today) play only from unclaimed hands, and gesture code may read them. About 10 pointers x 6 zones a
// frame (~5 us). Allocation-free per frame: the instrument -> rig table rebuilds only when registrations change.
using System.Collections.Generic;
using UnityEngine;

namespace MusicRoom.Core
{
    public sealed class PointerClaims
    {
        readonly App _app;
        readonly List<IInstrument> _order = new List<IInstrument>(16);
        readonly List<Rig> _rigOf = new List<Rig>(16);
        readonly List<IPointerClaimant> _claimants = new List<IPointerClaimant>(4);
        IReadOnlyList<IInstrument> _seen;
        int _seenCount = -1, _claimantCount = -1;

        public PointerClaims(App app) { _app = app; }

        /// <summary>Claims made this frame (diagnostics, tests).</summary>
        public int ClaimedCount { get; private set; }

        /// <summary>Forget the cached tables (a registration changed).</summary>
        public void Invalidate() { _seenCount = -1; _claimantCount = -1; }

        public void Update()
        {
            IReadOnlyList<IInstrument> all = _app.Instruments;
            if (!ReferenceEquals(all, _seen) || all.Count != _seenCount) Rebuild(all);
            if (_claimantCount < 0) { _claimants.Clear(); _claimants.AddRange(_app.GetAll<IPointerClaimant>()); _claimantCount = _claimants.Count; }
            IMenuPanel menu = _app.Get<IMenuPanel>();
            Bounds? menuZone = menu != null && menu.Shown ? menu.GrabZone : null;
            IReadOnlyList<Pointer> hand = _app.Input.HandPointers;
            int claimed = 0;
            for (int i = 0; i < hand.Count; i++)
            {
                Pointer p = hand[i];
                p.ClaimedBy = null;
                if (!p.Active) continue;
                Vector3 tip = p.Tip;
                object owner = null;
                for (int k = 0; k < _order.Count && owner == null; k++)
                {
                    IInstrument inst = _order[k];
                    Rig rig = _rigOf[k];
                    if (rig == null || rig.Zone == null || !inst.IsPresent || !rig.InScene) continue;
                    if (rig.Zone.Contains(tip)) owner = inst;
                }
                if (owner == null && menuZone.HasValue && menuZone.Value.Contains(tip)) owner = menu;
                for (int k = 0; k < _claimants.Count && owner == null; k++)
                    if (_claimants[k] != null && _claimants[k].ClaimsPoint(tip)) owner = _claimants[k];
                p.ClaimedBy = owner;
                if (owner != null) claimed++;
            }
            ClaimedCount = claimed;
        }

        void Rebuild(IReadOnlyList<IInstrument> all)
        {
            _seen = all;
            _seenCount = all.Count;
            _order.Clear();
            _rigOf.Clear();
            List<Rig> rigs = _app.GetAll<Rig>();
            IInstrumentRegistry reg = _app.Registry;
            for (int i = 0; i < all.Count; i++)
            {
                IInstrument inst = all[i];
                if (inst == null || inst.Kind == InstrumentKind.Menu) continue;
                InstrumentDescriptor d = reg?.Get(inst.Kind);
                if (d != null && d.Has(InstrumentTraits.Contactless)) continue;
                Rig rig = null;
                for (int r = 0; r < rigs.Count && rig == null; r++) if (rigs[r].Instruments.Contains(inst)) rig = rigs[r];
                _order.Add(inst);
                _rigOf.Add(rig);
            }
        }
    }
}
