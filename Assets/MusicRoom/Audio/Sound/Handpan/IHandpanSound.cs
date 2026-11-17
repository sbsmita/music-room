// The handpan unit's own API (handpan.md 5.2; ENGINE-API.md 4.4: a unit-specific interface in the unit's folder, reached
// through IAudioEngine.ActiveUnit(UnitId.Handpan) as IHandpanSound once the platform has the role). Main thread only.
namespace MusicRoom.Audio.Burst
{
    public enum HandpanTech : byte { Tip = 0, Slap = 1, Tak = 2 }
    public enum HandpanDampKind : byte { Finger = 0, Palm = 1 }

    public interface IHandpanSound
    {
        /// <summary>field 0..8 (0 = ding), 9 = between the fields (the tak's shell pulse). u, v: field-normalised coordinates
        /// (u along the radial long axis, + toward the rim; v across, + clockwise from above; both / the semi-axes).
        /// gain scales the contact force on the main thread (1 for every player strike; a tak's neighbour pulses w &lt; 1).
        /// when: SynthCommand rules (0 = the next block; inside AudioSchedule.ScheduleAt the scope's time).</summary>
        void Strike(int field, float vel, float u, float v, HandpanTech tech, double when, float gain = 1f);
        /// <summary>slot 0 L fingers, 1 R fingers, 2 L palm, 3 R palm; field 9 = the whole shell; strength 0 releases.</summary>
        void Damp(int field, int slot, float strength, float u, float v, HandpanDampKind kind, double when);
        /// <summary>Every damping slot released (a take stops, the looper clears).</summary>
        void ReleaseAll(double when);
        /// <summary>Per field: sqrt of the tuned modes' energy in the last block (audio -&gt; main), 0 for a bad index.</summary>
        float Level(int field);
    }
}
