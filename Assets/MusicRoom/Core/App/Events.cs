// Cross-module events (published on App.Events). Struct payloads; the publisher is named in each comment.
namespace MusicRoom.Core
{
    /// <summary>Layout: after Set / Next / ArrangeAroundPlayer changed anything.</summary>
    /// <summary>SetVersion: ILayoutService.SetVersion after the change (My set's membership counter).</summary>
    public struct LayoutChangedEvent { public LayoutMode Mode; public InstrumentKind Current; public int SetVersion; }

    /// <summary>Layout: a page turn started (Started = true) or ended.</summary>
    public struct TurnEvent { public bool Started; public int Direction; public InstrumentKind From, To; }

    /// <summary>Layout: ArrangeAroundPlayer ran (instruments, panel home and audio sources may have moved).</summary>
    public struct ArrangedEvent { public bool Forced; }

    /// <summary>Menu: open / close / screen change (the lessons panel's events).</summary>
    public struct MenuEvent { public MenuChange Change; public MenuScreen Screen; }

    /// <summary>Lessons (Recorder.Add): EVERY play event, recording or not: the observer stream for lessons and Sunny.</summary>
    public struct RecorderEvent { public TakeEvent Event; public bool DuringPlayback; }

    /// <summary>Lessons (Recorder): a recorded event was just sent back to its instrument by a take or the looper (never
    /// by live play), for what watches a playback (the room's listen-back pulse, Sunny). Kind: the instrument that plays it
    /// (the registry's owner of the type); Role: its event role; Level: 0..1 (an onset's velocity, a control's level, 0 for
    /// a release); When: the audio time it sounds (the looper sends up to its look-ahead early; 0 = now); Lane: the playback
    /// lane (PlaybackLanes: 1..6 a looper layer, 7 a take);
    /// Event: the recorded event itself as sent (its midi / kind / i / zone..., lane set), so observers can tell which key,
    /// drum or string sounded (the playback show: the night sky, the ghost hands, the window, the paint).</summary>
    public struct PlaybackNoteEvent { public InstrumentKind Kind; public EventRole Role; public float Level; public double When; public int Lane; public TakeEvent Event; }

    /// <summary>Lessons (Recorder): a live note the player just played went into a recording: the tape deck's take (REC) or
    /// the looper layer recording its pass (never live play outside a recording, never a note sent back by a playback, so a
    /// played-back note and a recorded one never announce the same sound twice). For what watches the music being made (the
    /// room's lights and butterflies, Sunny). Kind / Role / Level as PlaybackNoteEvent's (Level: the onset's velocity, a
    /// control's level, 0 for a release); Lane: the event's own lane.</summary>
    public struct RecordedNoteEvent { public InstrumentKind Kind; public EventRole Role; public float Level; public int Lane; }

    /// <summary>Lessons: recorder state changed (deck lamp, menu rows).</summary>
    public struct RecorderStateEvent { public RecorderState State; public bool External; }

    /// <summary>Lessons (Recorder): the player started a recording playing back (Started: the deck's PLAY of a take or of its
    /// loop, a take or a loop from Recordings) or that playback ended (its end, STOP, CLEAR, another playback replacing it).
    /// Kinds: the instruments the recording plays, one bit per InstrumentKind (1u &lt;&lt; (int)kind; markers left out); Loop:
    /// it repeats until stopped. A loop session being recorded live (LOOP) is not a playback of a recording: it never
    /// raises this. Layout brings a playback's instruments in front of the player and puts the player's own layout back
    /// when it ends.</summary>
    public struct RecordingPlaybackEvent { public bool Started; public uint Kinds; public bool Loop; }

    /// <summary>Lessons: tutorial state or version changed (menu redraw).</summary>
    public struct TutorialEvent { public TutorialState State; public string LessonId; public int Version; }

    /// <summary>Lessons: the take library changed (menu redraw).</summary>
    public struct LibraryChangedEvent { public int Version; }

    /// <summary>Guide: Sunny appeared or left.</summary>
    public struct GuidePresenceEvent { public bool Present; }

    /// <summary>MR: entered or left "Play in my room".</summary>
    public struct MixedRealityEvent { public bool Active; }

    /// <summary>Room: an ambience switch flipped (fire / wind / snow).</summary>
    public struct AmbienceEvent { public AmbienceKind Kind; public bool On; }

    /// <summary>MR: placing the lead started (Active) or ended. Core ticks no instrument while it lasts, so a held note
    /// (a key, a string, a rubbed bowl) is let go when it starts, as when "Move instruments" starts (ArrangeModeEvent).</summary>
    public struct PlacingEvent { public bool Active; }

    /// <summary>Drums: the nod kick fired (Sunny's tour counts nods).</summary>
    public struct NodEvent { public float Velocity; }

    /// <summary>Hands/Core: the HMD was mounted or unmounted (menu hides, voice cuts on unmount).</summary>
    public struct HmdMountEvent { public bool Mounted; }

    /// <summary>Hands/Core: input focus was lost to a system overlay (the Quest menu) or came back.</summary>
    public struct InputFocusEvent { public bool Focused; }

    /// <summary>Audio: the audio clock jumped (the output restarted: AudioSettings.Reset, a new device). Delta = new minus
    /// old, seconds (negative when the clock restarted from 0). The looper re-bases its schedule and lets go of what it held.</summary>
    public struct AudioClockJumpEvent { public double Delta; }

    /// <summary>Core: the runtime recentred the player (Layout re-arranges).</summary>
    public struct RecenteredEvent { }

    /// <summary>Core (FastMotionDriver, E4): the low-light guard blocked Fast Motion Mode for the session (published once):
    /// the room is too dim for fast hand tracking. Sunny shows one text hint (experience.md 14 item 4).</summary>
    public struct HandsLowLightEvent { public double LowConfidenceFraction; }

    /// <summary>Lessons (the looper on the tape deck): what just happened to the loop (Docs/11-first-five/sunny-tour.md
    /// 7.4; Sunny's looper tips listen). Layers = finished layers in the session after the change; Layer = the layer it
    /// concerns (0-based, -1 none); On = Muted: muted (true) or unmuted.</summary>
    public struct LoopEvent { public LoopEventPhase Phase; public int Layers; public int Layer; public bool On; }

    /// <summary>Layout (platform.md 4.5): an instrument starts to leave the seat (Leaving), has arrived at the seat and is
    /// present (Arrived), or stands on display again (Displayed). Reason: why it moved (Bring's reason; PutAway and
    /// replacements use the bringing reason).</summary>
    public enum MovePhase : byte { Leaving = 0, Arrived, Displayed }
    public struct InstrumentMoveEvent { public InstrumentKind Kind; public MovePhase Phase; public BringReason Reason; }

    /// <summary>Layout: the instrument the player faces changed (ILayoutService.Facing). Has false: none.</summary>
    public struct FocusEvent { public InstrumentKind Kind; public bool Has; }

    /// <summary>Menu: an instrument-owned option row changed (InstrumentOption; AppSettings "musicroom.&lt;id&gt;.&lt;Key&gt;").</summary>
    public struct InstrumentOptionEvent { public InstrumentKind Kind; public string Key; public int Value; }

    /// <summary>Menu: a row was pressed (sunny-tour.md 7.2). RowId: the row's stable id ("lessons", "inst.handpan",
    /// "layout.all", "option.<id>.<key>", ...); Screen: the screen it was on; Confirming: a first poke that asks again
    /// (Exit, Delete).</summary>
    public struct MenuRowEvent { public string RowId; public MenuScreen Screen; public bool Confirming; }
}
