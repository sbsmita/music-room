# Piano: requests for the foundation (apply at merge)

## Integration status (2026-10-31): APPLIED

Merged into the project by the integrator. Every item below is resolved as stated here; the text after
this box is the module's original request, kept for the record.

| # | Request | Resolution |
|---|---|---|
| 1 | `IPianoApi.InKeySpace` | Added to `Core/Contracts/Instruments.cs` (default body = the rule, so stand-in pianos need nothing); `PianoInstrument.InKeySpace` implements it; Layout's `PianoKeySpace` calls it |
| 2 | `SynthSource` start-up race | Audio folder taken from the Audio module (buffers first, voice pool published last with `Volatile.Write`); integrator added `AddInput`/`AddResonatorInput` guards and `AudioSource.playOnAwake = false` in `AudioRig.Build`. 0 occurrences in the merged PlayMode runs |
| 3 | `DesktopInput.Command` | Added (with `Ctrl` and `Meta`); `PianoDesktop.CommandHeld` forwards to it; `Unity.InputSystem` removed from the Piano asmdef |
| + | Layout request 1 | `PianoInstrument.IsPresent` now also needs `Rig.InScene`; `PianoInstrument.OnSurface` implements the new `IPianoApi.OnSurface` |

---

The Piano module compiles and works without these; each entry names the module-local workaround in place.
All three are open against the current foundation (which this clone's Core / Hands / Audio /
Editor / foundation tests, test asmdefs, `Tools/unity.sh`, `TagManager.asset` and `Assets/XR` were synced to; the module
uses `Rig.Handles` as an `IReadOnlyList<Renderer>`, `Layers`, `Pointer.FromMouse`, `App.IsPresenting`, `SharedTextures`
and `TestBoot`).

## 1. `IPianoApi.InKeySpace(Vector3 world)` (Core/Contracts/Instruments.cs)

```csharp
/// <summary>pianoKeySpace: InPlayZone and piano-local y > -0.05 (above the bottom of the keys). A pinch here
/// never grabs the piano; below it (the face under the keybed, the stand), beside or behind the keys it may.</summary>
bool InKeySpace(Vector3 world);
```

Why: the Layout grab guard (spec 05 section 8, `GrabCandidate`) returns null when the piano is visible, `InPlayZone(at)`,
and the rig is none or is the piano with `PianoKeySpace(at)`. `IPianoApi` exposes `InPlayZone` and `OverKeys` but not the
key space, so Layout would have to re-derive the piano's local frame rule.
Workaround in place: `PianoInstrument.InKeySpace(world)` exists (public, same rule, tested in
`PianoPlayTests.RegistrationZonesAndSurfaces`); until the contract has it, Layout can compute
`api.InPlayZone(at) && api.Root.InverseTransformPoint(at).y > -0.05f`.

## 2. Audio start-up race in `SynthSource` (Audio/Engine/SynthSource.cs, `Awake` / `OnAudioFilterRead`)

Seen in this clone's PlayMode runs (2 of about 8 full runs on foundation-v1, and on the current foundation in
`FrameOrderTests.OneFrameRunsThePhasesInTheDocumentedOrder` and in `PianoPlayTests.LeavingTheRoomLetsGoOfEverything`'s
SetUp, SynthSource.cs:213; it lands in whichever test happens to boot at that moment):

```
ArgumentNullException: Value cannot be null. Parameter name: array
  at System.Array.Clear (System.Array array, ...)
  at MusicRoom.Audio.SynthSource.OnAudioFilterRead (System.Single[] data, System.Int32 channels) [SynthSource.cs:213]
```

The audio thread can call `OnAudioFilterRead` as soon as `AudioRig.Build` activates the source GameObject, while `Awake` is
still running on the main thread. `Awake` assigns `_voices` (the field the callback's guard checks) before `_mix`,
`_send` and `_pending`, so the callback can pass the guard and clear a null `_mix`.

Change (either is enough; both is safest):
- in `Awake`, allocate `_mix`, `_send`, `_pending` (and build the resonator) first and assign `_voices` last
  (build the voice array in a local, then `Volatile.Write(ref _voices, voices)`);
- in `OnAudioFilterRead`, guard `if (frames > MaxFrames || _voices == null || _mix == null)`.

No workaround is possible inside the Piano folder; the failure is unrelated to the piano (it happens inside
`AudioModule.Init`, before the piano exists) and makes any PlayMode test that boots the app flaky.

## 3. `DesktopInput.Command` (Core/Util/DesktopInput.cs): Ctrl or Cmd held

```csharp
/// <summary>Ctrl or Cmd (Meta) is held: the keydown handlers return at once, so editor and
/// system shortcuts (Cmd+S, Ctrl+P, Cmd+D, Ctrl+K ...) never play the piano row, open the menu (M) or call Sunny (Shift+H).</summary>
public static bool Command
{
    get
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        return k != null && (k.leftCtrlKey.isPressed || k.rightCtrlKey.isPressed || k.leftMetaKey.isPressed || k.rightMetaKey.isPressed);
#else
        return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
            || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
#endif
    }
}
```

Why: every keydown is filtered on Ctrl / Meta (the piano row and Z / X, the M menu key in the same
handler, Shift+H). `DesktopInput` offers only `Shift` and `Alt`, so each module would read the device itself.
Workaround in place: `PianoDesktop.CommandHeld` (the same code) with `Unity.InputSystem` added to the Piano asmdef's
references (a package assembly, allowed by ARCHITECTURE section 2 rule 1). Once Core has `DesktopInput.Command`,
`PianoDesktop.Tick` passes it instead and the asmdef reference can go. Menu (M) and Guide (Shift+H) want the same guard.
