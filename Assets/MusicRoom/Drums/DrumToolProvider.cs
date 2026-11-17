// The kit's sticks as a hand tool (12-new-instruments/platform.md 4.6): Layout's grab pass asks every IHandToolProvider
// for NearestFree(tip) and calls TryPickUp on the nearest only; Fast Motion Mode watches AnyHeld of a FastMotion
// instrument. A thin adapter over DrumSticks and IDrumsApi, which stays. Allocation-free.
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Drums
{
    /// <summary>The kit's sticks as a hand tool (platform.md 4.6, IHandToolProvider), a thin adapter over DrumSticks and
    /// IDrumsApi (which stays): NearestFree is the distance TryGrab measures (the point to the nearest free stick's shaft),
    /// +inf when the kit is not at the seat; TryPickUp is IDrumsApi.TryPickUp; ShadowSpheres the drawn tips (E3).</summary>
    public sealed class DrumToolProvider : IHandToolProvider
    {
        readonly DrumKit _kit;
        readonly DrumSticks _sticks;

        public DrumToolProvider(DrumKit kit, DrumSticks sticks) { _kit = kit; _sticks = sticks; }

        public InstrumentKind Owner => InstrumentKind.Drums;

        public float NearestFree(Vector3 tip)
        {
            if (_kit == null || _sticks == null || !_kit.IsPresent) return float.PositiveInfinity;
            return _sticks.NearestFree(tip, out float distance) != null ? distance : float.PositiveInfinity;
        }

        /// <summary>Through the app's IDrumsApi (the first registered: the kit, or a test's stand-in in front of it), so the
        /// pick-up keeps going through the kit's contract exactly as before the platform.</summary>
        public StickPick TryPickUp(HandState hand, Vector3 tip)
        {
            IDrumsApi api = App.Current?.Get<IDrumsApi>() ?? _kit;
            return api != null ? api.TryPickUp(hand, tip) : StickPick.None;
        }
        public bool AnyHeld => _sticks != null && _sticks.AnyHeld;
        public void ReleaseAll() => _sticks?.ReleaseAll();
        public bool Holds(HandState hand) => hand != null && hand.Holding is Drumstick s && ReferenceEquals(s.Hand, hand) && Owns(s);
        public void Release(HandState hand) { if (Holds(hand)) _sticks.Release((Drumstick)hand.Holding); }

        bool Owns(Drumstick s)
        {
            if (_sticks == null) return false;
            for (int i = 0; i < _sticks.Count; i++) if (ReferenceEquals(_sticks[i], s)) return true;
            return false;
        }
        public int ShadowSpheres(System.Span<Vector4> centreRadius) => _kit != null ? _kit.StickShadowSpheres(centreRadius) : 0;
    }
}
