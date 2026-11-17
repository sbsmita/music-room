// A unit's declared worst-case drive for the platform's load tests (EightInstrumentsLoadTests): called once per block, it
// posts what the unit's worst case is (the bowls: all three bowls rubbed, struck again every 0.5 s; the handpan: a
// ten-finger roll; the marimba: two mallets at 15 strokes a second; the hand drums: every hand on every drum). Generic: any
// unit may implement it, so the shared load test needs one line, not a branch per instrument (bowls CONTRACT-REQUESTS R8,
// moved here from the instruments' folders at integration).
namespace MusicRoom.Audio.Burst
{
    public interface IUnitWorstCase
    {
        /// <summary>Block `block`'s commands of the worst case; when = absolute seconds offline, 0 = as soon as possible.</summary>
        void DriveWorst(long block, double when);
    }
}
