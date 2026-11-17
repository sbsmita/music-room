// A held tool (a drumstick, a marimba mallet, the bowl stick) mutes every fingertip of its hand on every instrument
// (PointerInput), and a curled hand keeps it, so a tool must never stay in a hand that has gone to play something else:
// the hand would be silent everywhere. Core frame step 3c (after the pointer claims): a hand puts its tool down when
//   - its grip (the thumb-index midpoint) stays more than AwayMargin outside the owning instrument's zone for
//     AwaySeconds (the hand left that instrument's reach), or
//   - its grip stays for ClaimSeconds inside the zone of another instrument at the seat that takes none of the tool's
//     pointer kinds, or inside the shown menu's grab zone (another instrument claims the hand).
// The tool goes back to its rest through its provider (IHandToolProvider.Release), as an open hand puts it back.
// Allocation-free per frame: the provider and rig tables rebuild only when registrations change.
using System.Collections.Generic;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.Core
{
    public sealed class HeldToolGuard
    {
        /// <summary>The grip may wander this far outside the owning instrument's zone (already padded by ZonePad) while
        /// playing: a stick's reach to a far cymbal.</summary>
        public const float AwayMargin = 0.35f;
        /// <summary>That long outside it puts the tool down.</summary>
        public const float AwaySeconds = 0.5f;
        /// <summary>That long inside another instrument's zone (or the shown menu's) puts the tool down.</summary>
        public const float ClaimSeconds = 0.3f;

        readonly App _app;
        readonly List<IHandToolProvider> _providers = new List<IHandToolProvider>(4);
        readonly List<Rig> _rigs = new List<Rig>(12);
        readonly float[] _away = new float[2], _claimed = new float[2];
        int _version = -1;

        public HeldToolGuard(App app) { _app = app; }

        /// <summary>Tools put down by this guard (tests, diagnostics).</summary>
        public int Releases { get; private set; }

        public void Update(float dt)
        {
            if (_version != _app.ServicesVersion)
            {
                _version = _app.ServicesVersion;
                _providers.Clear();
                _providers.AddRange(_app.GetAll<IHandToolProvider>());
                _rigs.Clear();
                _rigs.AddRange(_app.GetAll<Rig>());
            }
            HandState[] hands = _app.Input.Hands;
            for (int i = 0; i < hands.Length && i < _away.Length; i++)
            {
                HandState h = hands[i];
                IHandToolProvider p = h.Holding != null && h.Connected ? ProviderOf(h) : null;
                HandFrame f = p != null ? _app.Input.Frame(h) : null;
                if (p == null || f == null || !f.isTracked) { _away[i] = _claimed[i] = 0f; continue; }
                Vector3 grip = (f.position[(int)HandJoint.ThumbTip] + f.position[(int)HandJoint.IndexTip]) * 0.5f;
                Rig own = RigOf(p.Owner);
                _away[i] = own != null && Away(own, grip) ? _away[i] + dt : 0f;
                _claimed[i] = ClaimedElsewhere(h, own, grip) ? _claimed[i] + dt : 0f;
                if (_away[i] < AwaySeconds && _claimed[i] < ClaimSeconds) continue;
                string why = _away[i] >= AwaySeconds ? "left its instrument" : "went to another instrument";
                _away[i] = _claimed[i] = 0f;
                p.Release(h);
                if (h.Holding != null) continue;   // the provider kept it (not one of its tools after all)
                Releases++;
                Debug.Log($"[Input] {h.Side} hand put its {p.Owner} tool down: it {why}");
            }
        }

        IHandToolProvider ProviderOf(HandState h)
        {
            for (int k = 0; k < _providers.Count; k++) if (_providers[k] != null && _providers[k].Holds(h)) return _providers[k];
            return null;
        }

        Rig RigOf(InstrumentKind kind)
        {
            RigId id = InstrumentIds.RigOf(kind);
            for (int k = 0; k < _rigs.Count; k++) if (_rigs[k] != null && _rigs[k].Id == id) return _rigs[k];
            return null;
        }

        /// <summary>The grip is more than AwayMargin outside the rig's zone: neither it nor the point AwayMargin closer to
        /// the rig's origin (at the grip's height) is inside it. A rig without a zone or out of sight says nothing.</summary>
        static bool Away(Rig rig, Vector3 grip)
        {
            if (rig.Zone == null || rig.Group == null || !rig.Group.gameObject.activeInHierarchy) return false;
            if (rig.Zone.Contains(grip)) return false;
            Vector3 c = rig.Group.position;
            c.y = grip.y;
            Vector3 d = c - grip;
            float len = d.magnitude;
            if (len <= AwayMargin) return false;
            return !rig.Zone.Contains(grip + d * (AwayMargin / len));
        }

        /// <summary>The grip is in the shown menu's grab zone, or in the zone of another instrument at the seat that takes
        /// none of the held tool's pointer kinds.</summary>
        bool ClaimedElsewhere(HandState h, Rig own, Vector3 grip)
        {
            IMenuPanel menu = _app.Get<IMenuPanel>();
            if (menu != null && menu.Shown)
            {
                Bounds? z = menu.GrabZone;
                if (z.HasValue && z.Value.Contains(grip)) return true;
            }
            for (int k = 0; k < _rigs.Count; k++)
            {
                Rig r = _rigs[k];
                if (own != null && own.Zone != null && own.Zone.Contains(grip)) return false;   // where zones overlap the owner keeps it
                if (r == null || r == own || r.Id == RigId.Lessons || r.Zone == null || !r.InScene || r.Group == null
                    || !r.Group.gameObject.activeInHierarchy || !r.Zone.Contains(grip)) continue;
                if (!TakesTool(InstrumentIds.KindOf(r.Id), h.Holding)) return true;
            }
            return false;
        }

        /// <summary>The instrument's descriptor takes one of the tool's pointer kinds (the drums take a mallet's?).</summary>
        bool TakesTool(InstrumentKind kind, IHandTool tool)
        {
            InstrumentDescriptor d = _app.Registry?.Get(kind);
            if (d == null || !d.Playable || !(tool is IPointerOwner o)) return false;
            IReadOnlyList<Pointer> ps = o.Pointers;
            for (int i = 0; i < ps.Count; i++) if (ToolMasks.Has(d.Tools, ps[i].Tool)) return true;
            return false;
        }
    }
}
