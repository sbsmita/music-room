// The select of look and pinch, an Interaction SDK ISelector over the SDK's own IndexPinchSelector of each hand (each on an
// SDK Hand fed by MusicRoomHandDataSource). A pinch of either hand becomes the head gaze's select only when the Gaze module
// accepts it (TryBegin: the gate, a free hand, a target held by the gaze); its release is the unselect (Released first,
// so the module can judge the tap before the interactor sees the unselect). One pinch at a time.
using System;
using Oculus.Interaction;
using UnityEngine;

namespace MusicRoom.Gaze
{
    public sealed class GazePinchSelector : MonoBehaviour, ISelector
    {
        public event Action WhenSelected = delegate { };
        public event Action WhenUnselected = delegate { };

        /// <summary>The module: may the pinch of this hand (0 left, 1 right) start a choice?</summary>
        public Func<int, bool> TryBegin;
        /// <summary>The module: the owning hand let go (judge the tap now).</summary>
        public Action<int> Released;

        IndexPinchSelector[] _pinches;
        Action[] _on, _off;
        int _owner = -1;

        public int Owner => _owner;

        public void Bind(IndexPinchSelector left, IndexPinchSelector right)
        {
            _pinches = new[] { left, right };
            _on = new Action[] { () => OnPinch(0), () => OnPinch(1) };
            _off = new Action[] { () => OnRelease(0), () => OnRelease(1) };
            for (int i = 0; i < 2; i++)
            {
                _pinches[i].WhenSelected += _on[i];
                _pinches[i].WhenUnselected += _off[i];
            }
        }

        void OnDestroy()
        {
            if (_pinches == null) return;
            for (int i = 0; i < 2; i++)
            {
                if (_pinches[i] == null) continue;
                _pinches[i].WhenSelected -= _on[i];
                _pinches[i].WhenUnselected -= _off[i];
            }
        }

        void OnPinch(int hand)
        {
            if (_owner >= 0 || TryBegin == null || !TryBegin(hand)) return;
            _owner = hand;
            WhenSelected();
        }

        void OnRelease(int hand)
        {
            if (hand != _owner) return;
            _owner = -1;
            Released?.Invoke(hand);
            WhenUnselected();
        }

        /// <summary>Let go of the current pinch without a choice (the feature turned off, the hand lost).</summary>
        public void Cancel()
        {
            if (_owner < 0) return;
            _owner = -1;
            WhenUnselected();
        }
    }
}
