# MusicRoom.Audio / Sound: the sound units

One folder per unit-only instrument (`Handpan/`, `HandDrums/`, `Marimba/`, `Bowls/`; 12-new-instruments). Each one
plugs into the Burst engine (`Audio/Burst/`) through two partial methods, so no two folders edit the same file:

| Folder | Block hook (`UnitHooks`, audio thread, Burst) | Factory (`SoundUnits`, main thread) |
|---|---|---|
| `Handpan/` | `RenderHandpan(UnitSlot* u)` | `CreateHandpan(SoundContext, ref SoundUnit)` |
| `HandDrums/` | `RenderHandDrums(UnitSlot* u)` | `CreateHandDrums(...)` |
| `Marimba/` | `RenderMarimba(UnitSlot* u)` | `CreateMarimba(...)` |
| `Bowls/` | `RenderBowls(UnitSlot* u)` | `CreateBowls(...)` |

The contract, the rules for audio-thread code, testing and offline WAV renders: `Docs/10-upgrade/ENGINE-API.md`. The
template is `Audio/Burst/Units/ExampleUnit.cs`.

**2026-11-11:** the upgraded piano, drum, harp and room units (`Piano/`, `Drums/`, `Harp/`, `Room/`, from
`Docs/10-upgrade`) were deleted: the piano, the drums, the harp and the room play the classic sound only (today's synths
and Meta's room acoustics, the user's choice). Backup: `music-room-vr-wip/unity-backups/pre-classic-cleanup/`.
