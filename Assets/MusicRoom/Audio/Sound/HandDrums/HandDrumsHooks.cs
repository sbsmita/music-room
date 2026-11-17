// The hand drums' two partials of the platform (BUILD-GUIDE 4.3): the factory and the block render. The unit is
// unit-only (no classic synth): Classic sound never suspends it and audio.ActiveUnit(UnitId.HandDrums) is always it.
namespace MusicRoom.Audio.Burst
{
    public static partial class SoundUnits
    {
        static partial void CreateHandDrums(SoundContext ctx, ref SoundUnit unit) => unit = new HandDrumsUnit(ctx);
    }

    public static unsafe partial class UnitHooks
    {
        static partial void RenderHandDrums(UnitSlot* u) => HandDrumsDsp.Render(u);
    }
}
