// Every number of "Play in my room", with the spec (03, 07 section 9, 08) or ARCHITECTURE.md line that set it, or
// what it is for. Unity coordinates (right-handed z -> -z): local +z of the piano points away from the player.
using UnityEngine;

namespace MusicRoom.MR
{
    public static class MrConstants
    {
        // ---- piano on a real surface (place()) ----
        /// <summary>PUSH_BACK: the key fronts go this far beyond the chosen point, away from the player.</summary>
        public const float PushBack = 0.25f;
        /// <summary>tableLift = -BOT (the piano body's underside): the white key tops sit this far above a table top.</summary>
        public const float TableLift = 0.092f;
        /// <summary>On the floor the piano stands on its stand, key tops 0.72 m up.</summary>
        public const float StandHeight = 0.72f;
        /// <summary>onTable = hit.y &gt; 0.3 (here measured from the room's floor).</summary>
        public const float OnTableMinHeight = 0.3f;
        /// <summary>spec 07 section 9: room.Raycast(ray, 6f, ...) for the placing reticle.</summary>
        public const float SurfaceRayMax = 6f;

        // ---- the placing reticle (piano.solid = {halfWidth .663, front .004, rear -.29}) ----
        public const float RingInner = 0.06f, RingOuter = 0.08f;
        public const int RingSegments = 40;
        /// <summary>2 * halfWidth (1.326 m body width).</summary>
        public const float FootprintWidth = 1.326f;
        /// <summary>front - rear = .004 + .29.</summary>
        public const float FootprintDepth = 0.294f;
        /// <summary>Unity local z of the footprint centre: PUSH_BACK - (front + rear)/2 = .25 + .143 (right-handed: -.393).</summary>
        public const float FootprintZ = 0.393f;
        public static readonly Color ReticleColor = new Color32(0xff, 0xc4, 0x6a, 0xff);
        public const float RingAlpha = 0.9f, FootprintAlpha = 0.12f, FootprintEdgeAlpha = 0.6f;

        // ---- mixed-reality lights (Docs/07-world-visuals.md ambient.mr, spec 07 section 9) ----
        /// <summary>The sun's fixed position: right-handed (0.6, 2.4, 1.2). It looks at the piano (the layout anchor).</summary>
        public static readonly Vector3 SunPosition = new Vector3(0.6f, 2.4f, -1.2f);
        /// <summary>Intensity 1.3 / pi (URP Lambert has no 1/pi, spec 07 section 6.1).</summary>
        public const float SunIntensity = 1.3f / Mathf.PI;
        public static readonly Color AmbientSky = new Color32(0xb1, 0xb1, 0xb1, 0xff);
        public static readonly Color AmbientEquator = new Color32(0x96, 0x96, 0x96, 0xff);
        public static readonly Color AmbientGround = new Color32(0x68, 0x68, 0x68, 0xff);

        // ---- shadow catcher ----
        public const float CatcherWidth = 2.6f, CatcherDepth = 1.8f, CatcherOpacity = 0.32f;
        /// <summary>The catcher floats 2 mm over the surface it lies on, so it never fights its depth.</summary>
        public const float CatcherLift = 0.002f;
        /// <summary>The downward probe for the surface under the anchor starts just above where it stands: 2 cm over the
        /// piano body's underside (key tops - tableLift), 5 cm over a floor-standing rig's origin. Starting higher would
        /// hit the top of whatever stands on the table next to the piano (MeshOffice1: a box on the desk).</summary>
        public const float CatcherProbePiano = 0.02f, CatcherProbeFloorRig = 0.05f;

        // ---- depth rays (the piano's placer without a scene model, the wall paint's depth walls) ----
        // (The snowy window, its snow portals and their constants were removed 2026-11-13 at the player's request: snow
        // falls in the whole room now, Room/Decor.)
        /// <summary>Depth rays reach this far (m).</summary>
        public const float WallRayMax = 12f;
        /// <summary>A depth hit is a wall when |normal.y| &lt; .35 (findWall).</summary>
        public const float EnvWallMaxNormalY = 0.35f;
        /// <summary>The depth wall test (EnvironmentRaycastManager.PlaceBox): a flat, clear patch this big round the hit.</summary>
        public static readonly Vector3 DepthWallBox = new Vector3(0.5f, 0.5f, 0f);

        // ---- audio (ARCHITECTURE.md 7.2) ----
        /// <summary>The VR room's snow and wind sources (AudioModule's world positions).</summary>
        public static readonly Vector3 SnowSourceVr = MusicRoom.Audio.AudioModule.SnowPosition;
        public static readonly Vector3 WindSourceVr = MusicRoom.Audio.AudioModule.WindPosition;
        /// <summary>In mixed reality the snow falls all round the player: its sound comes from SnowSourceAbove metres above the
        /// head on entering, the wind WindSourceRise above that.</summary>
        public const float SnowSourceAbove = 0.9f, WindSourceRise = 0.35f;

        // ---- hints (spec 03 section 2.11; spec 08 section 8.6 rewording) ----
        // In the headset the hints are short cards, so the informational ones keep the two gestures and drop the
        // "move an instrument" sentence (the menu's Instruments screen says it).
        // 2026-11-10: no hint says "pinch and hold empty air with one hand" any more (that re-placed the lead and is retired:
        // instruments move only with Move instruments, by hand); both hands held is Reset view (LayoutModule.TickSummon).
        public const string EnterHint = "Everything is in front of you. To move them, turn on Move instruments in the menu. Pinch and hold with both hands to bring them back.";
        public const string EnterOnTableHint = "The piano is on your table. Pinch and hold with both hands to bring everything back to you.";
        public const string PlacingHint = "Point at a table or the floor and pinch to put the piano there. Pinch at nothing to stop.";
        public const string PlacedHint = "Placed. Pinch and hold with both hands to bring everything back to you.";

        // ---- the lead instrument (12-new-instruments/platform.md 9) ----
        // The hints above name the piano, the lead of the default set. Another lead's name replaces LeadName in them
        // ("the marimba", from its descriptor); a lead that cannot stand on a table gets the floor wordings, and the
        // on-the-table line is rephrased so a plural name ("the singing bowls") reads right.
        public const string LeadName = "the piano", LeadNameCapital = "The piano", LeadSlot = "{lead}";
        public const string EnterHintFloor = "Everything is in front of you. To move {lead}, turn on Move instruments in the menu. Pinch and hold with both hands to bring it back.";
        public const string PlacingHintFloor = "Point at the floor and pinch to put {lead} there. Pinch at nothing to stop.";
        public const string EnterOnTableHintLead = "Now on your table: {lead}. Pinch and hold with both hands to bring everything back to you.";
        /// <summary>platform.md 9.6: the draws mixed reality spends on everything but the at-seat instruments, and its budget.
        /// While the at-seat descriptors' DrawsAtSeat plus these exceed the budget, the last member of My set is put away.</summary>
        public const int FixedDraws = 37, DrawBudget = 80;
        /// <summary>Logged (and published as InstrumentSignalEvent NoRoomSignal for the menu's note) when the draw guard puts a
        /// member away.</summary>
        public const string NoRoomNote = "Put away: no room here", NoRoomSignal = "mr.noroom";
        /// <summary>Placing that has not landed ends by itself after this long: the instruments, which are silent while
        /// placing, play again (a placing nobody finishes looked like a hang).</summary>
        public const float PlacingTimeoutSeconds = 20f;
        /// <summary>s an informational hint stays up (the actionable ones stay while they apply).</summary>
        public const float HintSeconds = 8f;
        /// <summary>The card floats this far ahead of the head, above the gaze (what the player aims at stays clear):
        /// elevation = gaze elevation + 18 degrees, kept within [-25, +12] degrees of the horizon.</summary>
        public const float HintDistance = 0.65f, HintAboveGazeDeg = 18f, HintMinElevationDeg = -25f, HintMaxElevationDeg = 12f;
        /// <summary>It re-centres only when the head turns further than this from it (lazy follow), gliding back unless the
        /// turn is past HintSnapDeg, when it jumps.</summary>
        public const float HintFollowDeg = 22f, HintSnapDeg = 60f;
    }
}
