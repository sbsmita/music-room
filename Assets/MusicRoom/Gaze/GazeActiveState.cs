// The Interaction SDK active state of look and pinch: the Gaze module sets it each frame (GazeRules.Enabled); while it is
// false the SDK disables the head gaze interactor (no hover, no select).
using Oculus.Interaction;
using UnityEngine;

namespace MusicRoom.Gaze
{
    public sealed class GazeActiveState : MonoBehaviour, IActiveState
    {
        public bool Value;
        public bool Active => Value;
    }
}
