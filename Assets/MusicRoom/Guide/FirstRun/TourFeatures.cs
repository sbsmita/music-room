// Every learnable feature of the app, as data (Docs/11-first-five/sunny-tour.md section 1 and 6): an id, what kind of
// thing it is, and - for the few with nothing to learn - why Sunny says nothing about it. SunnyCoverageTests check that
// every feature here is taught by a first-run step (FirstRun.Covers) or a tip (TourTips.All[*].Covers), that every
// menu row kind maps to one (Menu's MenuRowIds.FeatureOf), and that every line those name has its clip.
namespace MusicRoom.Guide
{
    public enum TourFeatureKind : byte { Gesture = 0, Row, Screen, Control, Mode, Passive }

    public readonly struct TourFeature
    {
        public readonly string Id, Title;
        public readonly TourFeatureKind Kind;
        /// <summary>Passive only: why nothing is said (the user's rule: every feature gets a line or a reason).</summary>
        public readonly string PassiveReason;

        public TourFeature(string id, TourFeatureKind kind, string title, string passive = null)
        {
            Id = id; Kind = kind; Title = title; PassiveReason = passive;
        }

        public override string ToString() => Id;
    }

    public static class TourFeatures
    {
        const TourFeatureKind G = TourFeatureKind.Gesture, R = TourFeatureKind.Row, S = TourFeatureKind.Screen,
            C = TourFeatureKind.Control, M = TourFeatureKind.Mode, P = TourFeatureKind.Passive;

        public static readonly TourFeature[] All =
        {
            // A. start, app, Sunny
            new TourFeature("start.here", R, "Start panel: Play here"),
            new TourFeature("start.room", R, "Start panel: Play in my room"),
            new TourFeature("app.exit", R, "Exit, tap again to exit"),
            new TourFeature("sunny.tour", M, "Sunny's fast tour (first run, and Show me around): the SNOW / FIRE / WIND switches, the palm menu, Move instruments, My set and One alone, REC / PLAY and Recordings, Reset view and the both-hands hold, GUIDE; no song"),
            new TourFeature("sunny.screen", S, "The Sunny screen of the menu"),
            new TourFeature("sunny.hide", R, "Hide Sunny"),
            new TourFeature("sunny.poke", G, "Poke Sunny to skip"),
            new TourFeature("sunny.tips", S, "Tips list, Tips while I play"),
            new TourFeature("app.mount", P, "Headset off cuts Sunny's line, the menu hides", "nothing to do: it happens by itself"),
            new TourFeature("app.governor", P, "90 Hz with a quality governor", "nothing to do: it happens by itself"),
            // B. piano
            new TourFeature("piano.keys", G, "Press keys with the fingertips"),
            new TourFeature("piano.labels", C, "Note letters on the keys, middle C"),
            new TourFeature("piano.sustain", P, "Automatic sustain", "it sounds right by itself"),
            new TourFeature("piano.fingerstop", P, "Fingers stop on the keys", "felt, not learned"),
            new TourFeature("piano.shadows", P, "Contact shadows under the fingertips", "seen, not learned"),
            // C. drums
            new TourFeature("drums.hit", G, "Hit heads and cymbals"),
            new TourFeature("drums.sticks", G, "Pick up a stick"),
            new TourFeature("drums.sticks.drop", G, "Put a stick back"),
            new TourFeature("drums.hihat", G, "Pinch the hi-hat stand"),
            new TourFeature("drums.choke", G, "Hold a cymbal to choke it"),
            new TourFeature("drums.nod", G, "Nod for the kick"),
            new TourFeature("drums.pad", C, "The kick pad by the snare"),
            new TourFeature("drums.zones", P, "Strike zones, ghost notes", "they sound right where you strike"),
            new TourFeature("drums.motion", P, "Sticks bounce, cymbals rock, heads give", "seen, not learned"),
            new TourFeature("drums.fastmotion", C, "Fast drum tracking and its low-light guard"),
            // D. harp
            new TourFeature("harp.pluck", G, "Pluck or sweep the strings"),
            new TourFeature("harp.colors", C, "Red C strings, blue F strings"),
            new TourFeature("harp.damp", G, "Palm damping"),
            new TourFeature("harp.blur", P, "String blur", "seen, not learned"),
            // E. side table
            new TourFeature("table.switches", C, "FIRE, WIND, SNOW"),
            new TourFeature("room.snow", M, "With SNOW on, snow falls in the whole room all around you (the music room, and your own room in mixed reality), settles on the floor and melts, and swirls with a playback; the music room's window keeps its own snow outside"),
            new TourFeature("room.mrfire", M, "In mixed reality FIRE lights a fireplace in your own room, against a real wall in front of you (else on the floor), the crackle and a warm flicker from it; FIRE off puts it out"),
            new TourFeature("deck.rec", C, "Tape deck REC"),
            new TourFeature("deck.stop", C, "Tape deck STOP"),
            new TourFeature("deck.play", C, "Tape deck PLAY"),
            new TourFeature("deck.listen", M, "Listen back: the room pulses and the instruments replay the notes"),
            new TourFeature("deck.alive", M, "While you listen back: the lights, the flowers and the butterflies follow the notes (recording leaves the room calm)"),
            new TourFeature("deck.bring", M, "Playing a recording brings its instruments in front of you; your own layout comes back after"),
            new TourFeature("deck.window", M, "While you listen back the snowy window joins in: the snow gusts on the notes, northern lights in their colours"),
            new TourFeature("deck.stars", M, "While you listen back every note sends a star up into a night sky (under the ceiling in VR, above the instruments in mixed reality), joined into the song's constellation; each recording's row shows its constellation"),
            new TourFeature("deck.paint", M, "While you listen back the notes paint a nature scene in watercolour on the walls (the VR room's, your real ones in mixed reality), each instrument its own motif in its own colour: the piano's melody grows a flowering vine (a flower for every note, higher notes higher up), drums plant trees, grass and falling leaves, the harp sends flocks of birds swooping, bowls float clouds, the handpan butterflies, hand drums mushrooms, the marimba slim trees; size by loudness; it fades a few seconds after the end"),
            new TourFeature("deck.paintThrow", M, "While a recording plays back (and a few seconds after), flick an open hand toward a wall to throw a blob of paint: it bursts into a flower with flying petals where you point, in the colour of the instrument playing (never while you play an instrument)"),
            new TourFeature("deck.gallery", M, "When a recording's playback ends its painting swirls into a gold frame on a wall as a small landscape made from the song (a lively song a sunny day, a calm one dusk or moonlight; hills that follow the melody; trees from the drums, flowers from the piano, birds from the harp, a river or lake from long notes), with its name and date on a brass plaque: a gallery of your last six recordings, kept between visits; deleting a recording takes its painting down"),
            new TourFeature("deck.ghost", M, "While you listen back see-through hands play the recording on each instrument shown: fingers on the piano keys, sticks on the drums, a finger at the harp strings, taps and mallets on the others; faint where your own hand is"),
            new TourFeature("deck.ontop", C, "REC while a take plays records a new part on top of it: the take plays on, your new notes line up with it, and STOP saves a new take where both parts play together once; the new part may run longer than the take, and the original stays as it was"),
            // F. looper
            new TourFeature("looper.loop", C, "LOOP, the count"),
            new TourFeature("looper.tempo", C, "SLOW / MED / FAST"),
            new TourFeature("looper.bars", C, "2 BARS / 4 BARS"),
            new TourFeature("looper.layers", C, "Layers on top"),
            new TourFeature("looper.mute", C, "Layer lights mute"),
            new TourFeature("looper.undo", C, "UNDO"),
            new TourFeature("looper.clear", C, "CLEAR, tap again"),
            new TourFeature("looper.stop", C, "Looper STOP"),
            new TourFeature("looper.click", C, "CLICK"),
            new TourFeature("looper.tidy", C, "TIDY"),
            new TourFeature("looper.saved", C, "Loops saved to Recordings"),
            new TourFeature("looper.guide", M, "GUIDE and Make a long song: the long song, record on top part by part; Loop a beat: the one-minute loop show"),
            // G. layout
            new TourFeature("layout.pageturn", G, "Page-turn swipe"),
            new TourFeature("layout.seat", G, "Seat turn"),
            // Look and pinch (2026-11-13, the Gaze module on Meta's Interaction SDK): head gaze lights a menu row or an
            // instrument on display, a pinch chooses it; the Sunny screen's "Look and pinch" row turns it off.
            new TourFeature("gaze.pinch", G, "Look and pinch: look at a menu row or an instrument, pinch to choose it"),
            new TourFeature("layout.summon", G, "Two-hand summon"),
            new TourFeature("layout.level", G, "Reset view brings the instruments to the player's level (the headset off and on keeps it)"),
            new TourFeature("layout.turn", G, "Turn a held instrument"),
            new TourFeature("layout.twist", G, "Two-hand twist"),
            new TourFeature("layout.panel", G, "Move the menu panel"),
            new TourFeature("layout.single", R, "All instruments / one alone"),
            // H. Move instruments
            new TourFeature("arrange.mode", M, "Move instruments (the On / Off setting)"),
            new TourFeature("arrange.near", G, "Near grab"),
            new TourFeature("arrange.ray", G, "Ray grab"),
            new TourFeature("arrange.quiet", M, "Instruments are quiet while Move instruments is on"),
            new TourFeature("arrange.turn", G, "Turn in arrange mode"),
            new TourFeature("arrange.height", G, "Lift or lower an instrument to your height (it stands on a riser)"),
            new TourFeature("arrange.done", R, "Done and Reset"),
            new TourFeature("arrange.saved", C, "Placement remembered"),
            new TourFeature("arrange.guard", P, "Meta's pinch fallback, the system-gesture guard", "it keeps a pinch steady by itself"),
            // I. menu
            new TourFeature("menu.summon", G, "Palm summon"),
            new TourFeature("menu.poke", G, "Poke a row"),
            new TourFeature("menu.close", G, "Palm again to close"),
            new TourFeature("menu.back", R, "Back"),
            new TourFeature("menu.scroll", G, "Drag a list"),
            new TourFeature("menu.screens", R, "Lessons, Instruments, Recordings rows"),
            new TourFeature("lessons.instrument", R, "Lessons: pick an instrument"),
            new TourFeature("lessons.course", R, "Course list"),
            new TourFeature("lessons.play", M, "Playing a lesson"),
            new TourFeature("lessons.stop", R, "Lesson Stop"),
            new TourFeature("lessons.after", R, "Next, Replay, Courses"),
            new TourFeature("lessons.kick", M, "Drum lessons: the kick"),
            new TourFeature("lessons.harp", M, "Harp lessons"),
            new TourFeature("recordings.list", S, "Recordings"),
            new TourFeature("recordings.take", R, "A take"),
            new TourFeature("recordings.play", R, "Take Play / Stop"),
            new TourFeature("recordings.delete", R, "Delete (poke twice)"),
            new TourFeature("recordings.paint", R, "Keep painting: the notes' painting stays on the walls until the next playback"),
            new TourFeature("recordings.off", P, "Saving off notice", "the panel says it in words"),
            new TourFeature("menu.room", R, "Play in my room / Music room"),
            new TourFeature("menu.setup", R, "Set up my room"),
            new TourFeature("menu.resetview", R, "Reset view: the instruments back in front of you"),
            // J. mixed reality
            new TourFeature("mr.enter", M, "Play in my room"),
            new TourFeature("mr.table", C, "The piano on a real table"),
            new TourFeature("mr.summon", G, "Two-hand summon in your room"),
            new TourFeature("mr.permission", C, "Spatial data permission"),
            new TourFeature("mr.hands", R, "Real hands: your own hands or the drawn ones in your room"),
            new TourFeature("layout.front", P, "Instruments open in front of you, within reach", "it happens by itself; Sunny says it in the Tips list"),
            new TourFeature("mr.ambience", P, "Fire and wind off in your room, snow stays", "nothing to do: it happens by itself"),
            // K. more instruments (the platform: the Instruments grid, point and pinch; its own features are the platform's)
            new TourFeature("instruments.bring", R, "Bring another instrument to your seat"),
            new TourFeature("instruments.options", R, "An instrument's options (the Options chip on its tile)"),
        };

        public static int IndexOf(string id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].Id == id) return i;
            return -1;
        }
    }
}
