// THE TONE A/B (handpan.md 3.6 and 9.4; listen/new-instruments/handpan-README.txt): the user has not picked yet.
//   Calm     = handpan-singles.wav          (every overtone tuned almost exactly; a slow swell on some notes)
//   Measured = handpan-singles-measured.wav (octaves and fifths slightly flat; octave pairs shimmer 2-6 Hz)
// Both voices are full tables in HandpanTables.g.cs and cost the same. To switch, change this ONE line; nothing else
// reads a voice name (HandpanSound, the offline renders and the tests all start from HandpanVoiceChoice.Voice).
namespace MusicRoom.Audio.Burst
{
    public static class HandpanVoiceChoice
    {
        public const HandpanVoice Voice = HandpanVoice.Calm;
    }
}
