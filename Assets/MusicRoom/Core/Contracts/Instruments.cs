// Instrument contracts (spec 05 sections 2-7, 11). Instruments register themselves with
// app.Register<IInstrument>(this) plus their typed API (IPianoApi, IDrumsApi, IHarpApi) so the lessons, the
// recorder playback and Sunny can drive them without referencing the instrument assemblies.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusicRoom.Core
{
    /// <summary>The id block (12-new-instruments/platform.md section 2): values never move or get reused. RigOf(kind) =
    /// (RigId)(int)kind for every value. The persisted form is the registry id ("handpan"), never the number. 8 is retired
    /// (an instrument removed on 2026-11-10, RetiredInstruments): never reused.</summary>
    public enum InstrumentKind { Piano = 0, Drums = 1, Harp = 2, Table = 3, Menu = 4, Handpan = 5, HandDrums = 6, Marimba = 7, Bowls = 9 }

    /// <summary>The play-surface test: what is under a point (a pinch over a play surface is play, never a grab).
    /// Instrument: the generic play surface of any registry instrument (its heads, bars, fields, rods).</summary>
    public enum PlaySurface { None = 0, PianoKeys, Drums, HatStand, HarpStrings, Table, Menu, Instrument }

    public interface IInstrument
    {
        InstrumentKind Kind { get; }
        /// <summary>Tick order within the Instruments phase (spec 05 section 1.1: piano, drums, harp, deck, switch panel,
        /// menu): Piano 0, Drums 10, Harp 20, tape deck 30 (Lessons, Kind Table), switch panel 40 (Room, Kind Table), Menu 50.</summary>
        int TickOrder { get; }
        /// <summary>Drums only (kept for the classic kit): receives the stick tip / shoulder pointers as well as the
        /// fingertips. Since the platform Core builds each instrument's pointer list from its descriptor's ToolMask (hand
        /// pointers plus the tool pointers whose Pointer.Tool is in the mask); an instrument without a descriptor (the deck,
        /// the switch panel, the menu) gets AllPointers when this is true, else HandPointers.</summary>
        bool AcceptsStickPointers { get; }
        /// <summary>In the scene, visible and playable: false while the instrument's Rig.InScene is false (its rig is out of
        /// the layout, or is the incoming instrument of a page turn, shown while it slides in but not yet in the layout).
        /// Exception: the Menu is ALWAYS present: its update runs its summon gesture and open/close
        /// animation at the start of Tick and guards its pokes with Shown itself.</summary>
        bool IsPresent { get; }
        Transform Root { get; }
        /// <summary>Contact / strike logic for this frame.</summary>
        void Tick(IReadOnlyList<Pointer> pointers, float dt);
        /// <summary>Add hover glows for pointers near playable parts (HoverGlowPool.Add).</summary>
        void Hover(IReadOnlyList<Pointer> pointers, HoverGlowPool glow);
        /// <summary>Which play surface of this instrument a world point is over (None when not over one).</summary>
        PlaySurface PlaySurfaceAt(Vector3 world);
        /// <summary>Played parts (keys, heads, cymbals, sticks, strings, controls): never grab handles.</summary>
        IReadOnlyList<Renderer> PlayedParts { get; }
    }

    public struct PianoKeyInfo
    {
        public int Midi;
        public float X;       // piano-local x of the key centre (white: x0 + (k + .5) W; black: offsets per pitch class)
        public bool Black;
        public string Label;  // "C", "C#", ... (lesson hints use note names)
    }

    /// <summary>Piano API (spec 05 section 2.6).</summary>
    public interface IPianoApi
    {
        float W { get; }      // 0.0235 white pitch
        float BW { get; }     // 0.0137 black width
        float BTOP { get; }   // 0.012 black top above white tops
        float L { get; }      // 0.15 key length front -> hinge
        Transform Root { get; }
        /// <summary>|x| &lt; .641, back in (-.30, .21), y in (-.15, .28), in piano-local units.</summary>
        bool InPlayZone(Vector3 world);
        /// <summary>back &gt; -.12 and y &gt; -.02 (over the keys).</summary>
        bool OverKeys(Vector3 world);
        /// <summary>The piano key space: InPlayZone and piano-local y &gt; -0.05 (above the bottom of the keys). A pinch here
        /// never grabs the piano; below it (the face under the keybed, the stand), beside or behind the keys it may.
        /// The default is the rule itself, so a stand-in piano (tests) needs nothing.</summary>
        bool InKeySpace(Vector3 world) => InPlayZone(world) && Root != null && Root.InverseTransformPoint(world).y > -0.05f;
        bool TryGetKey(int midi, out PianoKeyInfo key);
        /// <summary>Computer keyboard / desktop: one pointerKey object per computer key so chords hold.</summary>
        void PressMidi(int midi, object pointerKey, float vel = 0.75f);
        void ReleaseMidi(int midi, object pointerKey);
        void PlaybackOn(int midi, float vel);
        void PlaybackOff(int midi);
        void PlaybackReleaseAll();
        /// <summary>MR: hide the stand when the piano sits on a real table (tableLift 0.092).</summary>
        void SetOnSurface(bool onTable);
        void ResetSurface();
        /// <summary>True after SetOnSurface(true) (the piano stands on a real table in MR, its stand hidden), false after
        /// ResetSurface(). The default (a stand-in piano without a stand) is false.</summary>
        bool OnSurface => false;
        int HeldKeyCount { get; }
        /// <summary>The sheet-music hook (platform.md 13.5, sheet.md): the WORLD pose of the music rest's front face, bottom
        /// centre (local up along the rest's face, local forward away from the player), or null while the piano is not at
        /// the seat (displayed, gliding, out of the layout). The default: null.</summary>
        Pose? MusicRestPose => null;
        /// <summary>The same pose in piano-local coordinates (the rig group's frame), whether or not it is at the seat.
        /// sheet.md: (0, 0.039, 0.2375), up = (0, cos .3, sin .3). The default: identity.</summary>
        Pose MusicRestLocal => Pose.identity;
        /// <summary>Depth of the rest's ledge the pages stand on (m). The default: 0.</summary>
        float MusicRestLedge => 0f;
    }

    public enum DrumPieceType { Drum, Cymbal }

    public struct DrumPieceInfo
    {
        public int Id;               // 0 snare, 1 tom1, 2 tom2, 3 floor, 4 kick, 5 kick pad, 6 hat, 7 crash, 8 ride
        public string Kind;          // snare, tom1, tom2, floor, kick, hat, crash, ride (the pad's kind is "kick")
        public DrumPieceType Type;
        public float Radius;
        public bool Pad;
        public Transform Transform;  // the piece group; its local y = 0 plane is the strike surface
    }

    public struct DrumStrikeInfo
    {
        public float Pos;    // 0 centre .. 1 edge
        public string Zone;  // head | rim | bell | bow | edge | top
        public bool Open;    // hi-hat open
        public static DrumStrikeInfo Default => new DrumStrikeInfo { Pos = 0.3f, Zone = "head" };
    }

    /// <summary>Stick pick-up result (spec 05 section 4).</summary>
    public enum StickPick { None = 0, Near, Grabbed }

    /// <summary>Drums API (spec 05 sections 3-4).</summary>
    public interface IDrumsApi
    {
        /// <summary>Stick pick-up for a hand at tip (spec 05 section 4). Layout calls it FIRST in its Grab pass, for each
        /// connected hand with PinchStart (or GraspStart while not playing), pointer 0 active, not grabbing a rig and not
        /// holding. Grabbed: the hand now holds a stick (Holding set). Near: within the dead zone
        /// (.24 m) of a free stick: Layout consumes the pinch unless a rig handle is within .02 (IGrabService.GrabTarget(tip,
        /// .02f)). None: no stick near, or the kit is not in the scene. tip = pointer 0's tip.</summary>
        StickPick TryPickUp(HandState hand, Vector3 tip);
        Transform Root { get; }
        IReadOnlyList<DrumPieceInfo> Pieces { get; }
        int KickId { get; }      // 4
        int KickPadId { get; }   // 5
        int HatId { get; }       // 6
        bool HatOpen { get; }
        void SetHatOpen(bool open, HandState hand, bool playback = false);
        void PlaybackHit(string kind, float vel, DrumStrikeInfo strike);
        void PlaybackChoke(string kind);
        /// <summary>True while any hand holds a drumstick.</summary>
        bool AnyStickHeld { get; }
        void ReleaseAllSticks();
        /// <summary>Hands/pointers farther than this from the kick pad count as "not touching the pad" (Sunny's nod step).</summary>
        bool EveryHandPointerFartherThan(float metres, int pieceId);
        /// <summary>10-upgrade/experience.md E3: the DRAWN tip-ball centre (xyz, world) and radius (w) of every held stick
        /// (after the E2 rebound), written into <paramref name="centreRadius"/>; returns the count. ContactVisuals queries them
        /// against the contact surfaces for their soft shadows. Allocation-free. The default (a stand-in kit) has none.</summary>
        int StickShadowSpheres(Span<Vector4> centreRadius) => 0;
    }

    /// <summary>Harp API (spec 05 section 5).</summary>
    public interface IHarpApi
    {
        Transform Root { get; }
        int StringCount { get; }                 // 22
        int MidiOf(int i);                       // 48 + 12*(i/7) + [0,2,4,5,7,9,11][i%7] (static table: no allocation)
        Vector3 StringStart(int i);              // harp-local a (soundboard end)
        Vector3 StringEnd(int i);                // harp-local b (neck end)
        Vector3 PlayPoint(int i);                // harp-local, lerp(a, b, clamp((.95 - a.y)/(b.y - a.y), .3, .4)) (10-upgrade harp.md 5.2)
        void Highlight(int i, float k = 1f);
        void PlaybackPluck(int i, float vel, float u = 0.35f, float speed = 0f, string mode = "through");
        void PlaybackDamp(int i);
    }

    // ---------------------------------------------------------------- the platform (12-new-instruments/platform.md 3.3)

    /// <summary>Every instrument: its static display configuration while it is not at the seat (platform.md 4.1). Stowed
    /// true: tools released and resting on it, power off, its stand hidden for Mount Surface, no Tick (Rig.InScene false).
    /// Called by Layout on arrival (false) and when it goes back on display (true), and on the room changes.</summary>
    public interface IStowable
    {
        InstrumentKind Kind { get; }
        bool Stowed { get; }
        void SetStowed(bool stowed, StowMount mount);
    }

    /// <summary>Generic take / loop playback (platform.md 6.2). Lessons' PlaybackWiring routes every recorded event of the
    /// descriptor's non-Marker types to Play. e.lane tells where it comes from (PlaybackLanes: 1..6 a looper layer, 7 a
    /// take); Play makes the sound and the visual the live gesture would have made (a displayed instrument may skip the
    /// visual) and returns false for a type it does not know.</summary>
    public interface IInstrumentPlayback
    {
        InstrumentKind Kind { get; }
        bool Play(in TakeEvent e);
        /// <summary>Every playback-held note up (take stop, looper clear).</summary>
        void ReleaseAll();
        /// <summary>End a continuous lane now (the looper's window end, a take's stop): a final level-0 control and an off.</summary>
        void CloseLane(int lane);
    }

    /// <summary>Sunny's demo targets and the lessons' stall re-demo targets (platform.md 8.4): a world point and the surface's
    /// up (its length may carry a radius, e.g. a rim for DemoKind.Circle). Anchor ids are documented in the module README and
    /// listed in InstrumentDescriptor.Anchors. Any module may register one (the sheet music does).</summary>
    public interface IGuideAnchors
    {
        bool TryGetAnchor(string anchor, out Vector3 world, out Vector3 up);
        /// <summary>Whose anchors these are: the Guide asks the tip's instrument first, then every other provider (anchor ids
        /// need only be unique per instrument). The default (a module without an instrument, the sheet music): Table.</summary>
        InstrumentKind Kind => InstrumentKind.Table;
    }

    /// <summary>Sticks, mallets, strikers and bowl sticks (platform.md 4.6). Layout's grab pass asks every provider for
    /// NearestFree(tip) and calls TryPickUp on the nearest only (Near / Grabbed and the 0.02 m handle rule as the kit's).</summary>
    public interface IHandToolProvider
    {
        InstrumentKind Owner { get; }
        /// <summary>Metres from tip to the nearest free tool's grip; +inf when none, or when the owner is not at the seat.</summary>
        float NearestFree(Vector3 tip);
        /// <summary>As IDrumsApi.TryPickUp: None / Near (inside the dead zone) / Grabbed.</summary>
        StickPick TryPickUp(HandState hand, Vector3 tip);
        bool AnyHeld { get; }
        void ReleaseAll();
        /// <summary>The tool <paramref name="hand"/> holds is one of this provider's (HeldToolGuard asks).</summary>
        bool Holds(HandState hand) => false;
        /// <summary>Puts down the tool <paramref name="hand"/> holds (back to its rest, as an open hand does); the other
        /// hand keeps its own.</summary>
        void Release(HandState hand) { if (Holds(hand)) ReleaseAll(); }
        /// <summary>E3 contact shadows of held tool tips: centre (xyz) and radius (w); returns the count. Allocation-free.</summary>
        int ShadowSpheres(Span<Vector4> centreRadius);
    }

    /// <summary>A TableTop instrument in mixed reality (generalises IPianoApi.SetOnSurface): on a real table its stand hides.</summary>
    public interface IInstrumentSurface
    {
        InstrumentKind Kind { get; }
        bool OnSurface { get; }
        void SetOnSurface(bool onSurface);
    }

    /// <summary>Something that claims fingertips inside it before a contactless instrument may use them (platform.md 4.6:
    /// Sunny's poke sphere, the sheet music's layer in front of the pages). Core asks every registered claimant after the
    /// instruments' zones and the menu, in registration order.</summary>
    public interface IPointerClaimant
    {
        bool ClaimsPoint(Vector3 world);
    }
}
