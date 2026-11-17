// The MR module's one MonoBehaviour (on World/MixedReality): Unity callbacks the frame phases cannot give.
// OnApplicationFocus(true) reloads the scene in mixed reality (the player may have edited Space Setup from the
// system menu, spec 03 section 2.5). (Its beginCameraRendering hook set the snowy window's flake size; the window was
// removed 2026-11-13.)
using System;
using UnityEngine;

namespace MusicRoom.MR
{
    public sealed class MrRuntime : MonoBehaviour
    {
        public Action<bool> FocusChanged;

        void OnApplicationFocus(bool focus) => FocusChanged?.Invoke(focus);
    }
}
