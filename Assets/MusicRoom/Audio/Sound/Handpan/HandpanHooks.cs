// The handpan role's two engine hooks (BUILD-GUIDE 4.3): the factory and the block render. The declarations and their
// switch cases are the platform's (Audio/Burst/Units/SoundUnit.cs, UnitSlot.cs).
namespace MusicRoom.Audio.Burst
{
    public static unsafe partial class UnitHooks
    {
        static partial void RenderHandpan(UnitSlot* u) => HandpanDsp.RenderUnit(u);
    }

    public static partial class SoundUnits
    {
        static partial void CreateHandpan(SoundContext ctx, ref SoundUnit unit) => unit = new HandpanUnit(ctx);
    }
}
