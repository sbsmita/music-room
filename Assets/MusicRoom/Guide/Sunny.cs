// Sunny's brain: the tour state machine, the speech bookkeeping (finish-the-sentence queueing, the 1.2 s late rule, the
// mouth follower, the chirps), blinking, placement and the demo clock (Docs/08-ux-lessons-menu-guide.md 3.2-3.4),
// with no GameObjects, so the EditMode tests drive it with a scripted clock, a fake voice
// and a fake world. SunnyView applies the result to the scene; GuideModule wires it to the app.
// The platform's tips (12-new-instruments/platform.md 8): a tip may say several lines (any module's merged lines too), or
// show a caption only (TextOnly: no voice), and loop a ghost-hand demo at a world anchor (SunnyDemo.EvaluateAnchored,
// then its ThenDemo), or one of the tour's demos in front of Sunny when it has no anchor.
using System;
using MusicRoom.Audio;
using MusicRoom.Core;
using UnityEngine;

namespace MusicRoom.Guide
{
    /// <summary>Tip: a one-line tip outside the tour (IGuide.Tip): Sunny appears, says it, then leaves silently.</summary>
    /// <summary>Script: driven line by line from outside (DeckGuide, the tape deck walkthrough), perched where it says.</summary>
    public enum SunnyState : byte { Idle = 0, Intro, Step, Wait, Praise, Skip, Bye, Leave, Gone, Tip, Script }

    public enum SunnyMood : byte { Calm = 0, Happy }

    /// <summary>What follows a step once Sunny is quiet (the queued follow-up).</summary>
    public enum SunnyNext : byte { None = 0, Praise, Skip }

    /// <summary>What Sunny watches (the panel, the layout, the drums and the camera).</summary>
    public interface ISunnyWorld
    {
        /// <summary>No headset hands: the desktop lines, and any kick counts in the nod step (not presenting).</summary>
        bool IsDesktop { get; }
        bool TryHead(out Vector3 position, out Vector3 forward);
        bool HasPanel { get; }
        bool PanelShown { get; }
        MenuScreen PanelScreen { get; }
        /// <summary>True in single-instrument mode (no single layout otherwise), with the current instrument.</summary>
        bool TrySingleLayout(out InstrumentKind current);
        /// <summary>Every hand pointer is more than PAD_FAR from the kick pad (true when there are no drums).</summary>
        bool HandsFarFromPad();
    }

    /// <summary>Sunny's voice: the robotised lines and the chirps on the Guide source (IAudioEngine in the app).</summary>
    public interface ISunnyVoice
    {
        /// <summary>The audio runs (engine started): without it lines are captions only and no chirp plays.</summary>
        bool Available { get; }
        /// <summary>The audio clock (AudioSettings.dspTime): line starts and ends are scheduled on it.</summary>
        double Now { get; }
        bool HasLine(string id);
        /// <summary>The line's processed buffer exists (its clip is loaded and rendered).</summary>
        bool LineReady(string id);
        /// <summary>Dry seconds of the recording, -1 when unknown.</summary>
        float LineSeconds(string id);
        /// <summary>Plays the line at `when` (absolute audio time); returns the audio time it ends (0 = could not play).</summary>
        double Play(string id, double when);
        /// <summary>Cuts the line in flight (fade 4 ms, stop +30 ms).</summary>
        void Stop();
        void Chirp(ChirpKind kind);
        /// <summary>RMS of the processed voice at the playback position (21.3 ms windows).</summary>
        float Level { get; }
    }

    /// <summary>First five (Sunny.FirstFive.cs): world-anchored demos for the scripts, the poke feedback, the script owner.</summary>
    public sealed partial class Sunny
    {
        /// <summary>Where Sunny floats before the first placement (design base (-0.25, 1.05, -0.5), z negated).</summary>
        public static readonly Vector3 InitialBase = new Vector3(-0.25f, 1.05f, 0.5f);
        /// <summary>A started line whose audio clock never reaches its end (audio stalled) counts as ended this long after.</summary>
        public const float StallSeconds = 1f;

        readonly ISunnyWorld _world;
        readonly ISunnyVoice _voice;
        Mulberry32 _rng;

        public Sunny(ISunnyWorld world, ISunnyVoice voice, uint seed)
        {
            _world = world;
            _voice = voice;
            _rng = new Mulberry32(seed == 0 ? 1u : seed);
            _nextBlink = GuideTiming.FirstBlinkMin + (float)_rng.Next() * GuideTiming.FirstBlinkSpan;
            Base = Position = InitialBase;
        }

        // ---------------- the run ----------------
        public SunnyState State { get; private set; } = SunnyState.Idle;
        public SunnyNext Queued { get; private set; }
        public float Timer { get; private set; }
        /// <summary>Sunny's own clock (seconds of Update); the corona and the bob run on it.</summary>
        public float Time { get; private set; }
        /// <summary>Seconds since the current line stopped being busy (0 while one is spoken).</summary>
        public float QuietT { get; private set; }
        public int Step { get; private set; } = -1;
        /// <summary>The run was completed or dismissed (since the last Start).</summary>
        public bool Done { get; private set; }
        public bool Desktop { get; private set; }
        public bool Visible { get; private set; }
        public int Nodded { get; private set; }
        public int Kicks { get; private set; }
        /// <summary>Present: here and guiding or saying goodbye (what the menu row reads).</summary>
        public bool Present => Visible && State != SunnyState.Idle && State != SunnyState.Gone && State != SunnyState.Leave;
        public StepDef CurrentStep => Step >= 0 && Step < GuideLines.Steps.Length ? GuideLines.Steps[Step] : default;

        bool _hasScreen0;
        MenuScreen _screen0;
        bool _hasLayout0;
        InstrumentKind _layout0;

        // ---------------- the caption ----------------
        public string Line { get; private set; } = "";
        public string LineId { get; private set; } = "";
        public float LineAt { get; private set; } = -1f;
        /// <summary>Lines shown so far.</summary>
        public int Lines { get; private set; }
        /// <summary>Bumped whenever the caption changes, so the view redraws the bubble only then.</summary>
        public int LineVersion { get; private set; }
        public bool BubbleVisible { get; private set; }

        // ---------------- the face ----------------
        public SunnyMood Mood { get; private set; } = SunnyMood.Calm;
        public bool EyesShut { get; private set; }
        public bool MouthOpen { get; private set; }
        public float MouthLevel { get; private set; }
        float _blinkT, _nextBlink;
        float _voicePeak;

        // ---------------- the pose ----------------
        /// <summary>Where it floats, before the bob (eases toward the spot beside the head).</summary>
        public Vector3 Base { get; private set; }
        public Vector3 Position { get; private set; }
        public Quaternion Rotation { get; private set; } = Quaternion.identity;
        public float Scale { get; private set; } = 1f;
        public float BodyOpacity { get; private set; } = 1f;
        public float BubbleOpacity { get; private set; } = 1f;
        public float Glow { get; private set; } = 1f;
        /// <summary>The warm light in design units (0.7 + 0.2 sin 2.1t); SunnyView converts to URP.</summary>
        public float LightIntensity { get; private set; }
        public float LeaveY { get; private set; }
        bool _placed;

        // ---------------- the demo ----------------
        public DemoKind DemoKind { get; private set; }
        /// <summary>The demo's loop clock (settable, for poses posed by hand).</summary>
        public float DemoT { get; set; }
        DemoPose _demo;
        public DemoPose Demo => _demo;
        /// <summary>The nod demo's dip this frame (0..1).</summary>
        public float NodAmount { get; private set; }

        // ---------------- the line in flight ----------------
        int _talkSeq;
        bool _hasTalk, _talkStarted, _talkEnded, _talkCancelled, _talkFailed, _talkLate, _talkPending, _talkLive;
        string _talkId = "";
        float _talkAt, _talkEndT, _talkDuration;
        double _talkAsked, _talkStartAt, _talkEndDsp;
        bool _wasSpeaking;
        int _playing;

        /// <summary>Increments with every line spoken (tells one line in flight from the next).</summary>
        public int TalkSeq => _hasTalk ? _talkSeq : 0;
        public string TalkId => _hasTalk ? _talkId : "";
        public bool TalkStarted => _hasTalk && _talkStarted;
        public bool TalkEnded => _hasTalk && _talkEnded;
        public bool TalkCancelled => _hasTalk && _talkCancelled;
        public bool TalkFailed => _hasTalk && _talkFailed;
        public bool TalkLate => _hasTalk && _talkLate;
        /// <summary>Waiting for the processed buffer (at most LATE seconds).</summary>
        public bool TalkPending => _hasTalk && _talkPending;
        public double TalkStartAt => _talkStartAt;
        public double TalkEndDsp => _talkEndDsp;
        public float TalkDuration => _talkDuration;

        // Stats (test and debugging counters).
        public int Played { get; private set; }
        public int Stopped { get; private set; }
        public int Failed { get; private set; }
        public int Chirps { get; private set; }
        public int MaxPlaying { get; private set; }
        public int Playing => _playing;

        // =====================================================================================================
        // The run
        // =====================================================================================================

        /// <summary>Hide Sunny while it is here, otherwise start the tour (the menu row and Shift+H).</summary>
        public void Toggle()
        {
            if (Present) Dismiss();
            else Start();
        }

        /// <summary>"Hide Sunny": the voice and the tour stop at once and it leaves (shrinks, fades and rises away).</summary>
        public void Dismiss()
        {
            if (!Present) return;
            if (!Hinting && !InTip && State != SunnyState.Script) Done = true;   // a hint, a tip or the deck walkthrough is not the tour
            Step = -1;
            EndDemo();
            StopVoice();
            Leave();
        }

        /// <summary>Showing a one-off text hint (Hint), not the tour.</summary>
        public bool Hinting { get; private set; }

        /// <summary>A one-off hint outside the tour: Sunny comes up beside the head with the caption (text only: no
        /// recording, only the chime), holds it for seconds, then leaves without a goodbye line. It never touches the tour
        /// (Done stays as it was). False while Sunny is here or still leaving: the caller asks again later.</summary>
        public bool Hint(string text, float seconds)
        {
            if (Present || State == SunnyState.Leave || string.IsNullOrEmpty(text)) return false;
            Desktop = _world.IsDesktop;
            Hinting = true;
            Step = -1;
            Visible = true;
            _placed = false;
            Scale = 1f;
            BodyOpacity = 1f;
            BubbleOpacity = 1f;
            Glow = 1f;
            SetMood(SunnyMood.Calm);
            Say(text);   // not a line id: captioned, no voice
            SetState(SunnyState.Bye, seconds);
            return true;
        }

        /// <summary>Begins (or begins again) beside the head with the intro line.</summary>
        public void Start()
        {
            InTip = false;
            Desktop = _world.IsDesktop;
            Hinting = false;
            Done = false;
            Step = -1;
            Visible = true;
            _placed = false;
            Scale = 1f;
            BodyOpacity = 1f;
            BubbleOpacity = 1f;
            Glow = 1f;
            SetMood(SunnyMood.Happy);
            string intro = Desktop ? GuideLines.IntroDesk : GuideLines.IntroVr;
            Say(intro);
            SetState(SunnyState.Intro, Hold(intro, GuideTiming.IntroT));
        }

        /// <summary>Sunny is here for a one-off tip (Tip), not the tour: from Tip until it is gone (its leave included).</summary>
        public bool InTip { get; private set; }
        /// <summary>The tip being said (IGuide.Tip), "" outside a tip.</summary>
        public string TipId { get; private set; } = "";
        /// <summary>The tip ran its course (its line was not cut): the module marks it seen.</summary>
        public bool TipHeard { get; private set; }

        /// <summary>A one-line tip outside the tour (sunny-tour.md 3.1, the minimal form: no demo; "Move instruments" and the
        /// looper's first LOOP press): Sunny appears beside the head, says the line with the usual chirps, stays for
        /// Hold(id, TipT), then leaves silently (no "Bye!"). The tour's Done flag is untouched, so a tip never counts as the
        /// tour. False, and nothing happens, for an id that is not a line or while Sunny is here (tour, script, hint, tip).</summary>
        public bool Tip(string id) => TipLines(id, null, GuideTiming.TipT);

        /// <summary>A tip of the platform (TipSpec): its lines said in order (any line id, the merged ones included; null =
        /// the one line <paramref name="tipId"/>), the last held at least minHold. TipId is the tip's id.</summary>
        public bool TipLines(string tipId, string[] lines, float minHold)
        {
            string first = lines != null && lines.Length > 0 ? lines[0] : tipId;
            if (Present || !GuideLines.Has(first)) return false;
            BeginTip(tipId);
            _tipLines = lines;
            _tipLine = 0;
            _tipMinHold = minHold;
            Say(first);
            SetState(SunnyState.Tip, Hold(first, LastTipLine ? minHold : GuideTiming.TipT));
            return true;
        }

        /// <summary>A caption-only tip (TipSpec.TextOnly: its Title, no voice clip yet): shown with the chime, held for
        /// seconds, then Sunny leaves silently. False while Sunny is here.</summary>
        public bool TipText(string tipId, string caption, float seconds)
        {
            if (Present || string.IsNullOrEmpty(caption)) return false;
            BeginTip(tipId);
            _tipLines = null;
            _tipLine = 0;
            Say(caption);
            SetState(SunnyState.Tip, Mathf.Max(seconds, GuideTiming.TipT));
            return true;
        }

        string[] _tipLines;
        int _tipLine;
        float _tipMinHold;
        bool LastTipLine => _tipLines == null || _tipLine >= _tipLines.Length - 1;

        void BeginTip(string id)
        {
            Desktop = _world.IsDesktop;
            Hinting = false;
            Step = -1;
            EndDemo();
            Visible = true;
            _placed = false;
            Scale = 1f;
            BodyOpacity = 1f;
            BubbleOpacity = 1f;
            Glow = 1f;
            SetMood(SunnyMood.Happy);
            InTip = true;
            TipId = id;
            TipHeard = false;
        }

        // ---------------- scripted (DeckGuide) ----------------

        /// <summary>Where a script perches Sunny (world; null = beside the head). Set every frame by the script.</summary>
        public Vector3? Perch { get; set; }
        /// <summary>The control the press demo points at (world) and its up.</summary>
        public Vector3 PressAnchor { get; private set; }
        public Vector3 PressUp { get; private set; } = Vector3.up;

        /// <summary>Sunny appears for a script (the deck walkthrough): no tour steps, no timers; the script says the lines,
        /// shows the press demo and ends it. The tour's Done flag is untouched.</summary>
        public void BeginScript()
        {
            Desktop = _world.IsDesktop;
            InTip = false;
            Hinting = false;
            Step = -1;
            EndDemo();
            StopVoice();
            Visible = true;
            _placed = false;
            Scale = 1f;
            BodyOpacity = 1f;
            BubbleOpacity = 1f;
            Glow = 1f;
            SetMood(SunnyMood.Happy);
            SetState(SunnyState.Script);
            Owner = null;   // first five: the script that called sets itself as the owner
            _worldDemo = false;
        }

        /// <summary>The script says a line (caption, chirp, voice; a cut line in flight stops first).</summary>
        public void SayScript(string id, SunnyMood mood = SunnyMood.Calm)
        {
            SetMood(mood);
            Say(id);
        }

        /// <summary>The ghost hand presses the control at `anchor` (world) twice per loop, until EndPress or another demo.</summary>
        public void ShowPress(Vector3 anchor, Vector3 up)
        {
            PressAnchor = anchor;
            PressUp = up.sqrMagnitude > 1e-8f ? up.normalized : Vector3.up;
            if (DemoKind != DemoKind.Press) DemoT = 0f;
            DemoKind = DemoKind.Press;
        }

        /// <summary>Restart the demo loop (the 8 s re-demo).</summary>
        public void RestartDemo() => DemoT = 0f;

        public void EndPress() { if (DemoKind == DemoKind.Press) EndDemo(); }

        /// <summary>The script ends: Sunny leaves (with "Bye!" when said is true, else silently).</summary>
        public void EndScript(bool bye)
        {
            if (State != SunnyState.Script) return;
            EndDemo();
            Perch = null;
            Leave(say: bye);
        }

        /// <summary>No line is in flight and Sunny has been quiet for the usual gap (the script may say the next one).</summary>
        public bool Quiet => !LineBusy() && QuietT >= GuideTiming.Gap;

        /// <summary>A step's line for this run: the hands line in a headset, else the desktop one.</summary>
        public string LineFor(in StepDef s) => Desktop ? s.Desk : s.Vr;

        /// <summary>How long a state lasts: at least t, and long enough for its line to be heard to the end.</summary>
        public float Hold(string id, float t) => Mathf.Max(t, LineSeconds(id) + GuideTiming.HoldPad);

        /// <summary>A line's playing time: dry seconds / rate + the chirp lead (0 when unknown).</summary>
        public float LineSeconds(string id)
        {
            float s = _voice != null ? _voice.LineSeconds(id) : -1f;
            if (s <= 0f)
            {
                int i = GuideLines.IndexOf(id);
                s = i >= 0 ? GuideLines.ManifestSeconds[i] : GuideLines.Merged.Seconds(id);
            }
            return s > 0f ? s / GuideTiming.Rate + GuideTiming.Lead : 0f;
        }

        /// <summary>Skip the step in progress at once (unlike a timeout it does not wait for the line); outside a step, end
        /// the run (it says goodbye and leaves).</summary>
        public void Skip()
        {
            if (State == SunnyState.Step || State == SunnyState.Wait)
            {
                EndDemo();
                SayNext(SunnyNext.Skip);
            }
            else if (State == SunnyState.Intro || State == SunnyState.Praise || State == SunnyState.Skip) Finish();
        }

        void SetState(SunnyState state, float t = 0f)
        {
            State = state;
            Timer = t;
            if (state != SunnyState.Wait) Queued = SunnyNext.None;
        }

        public void BeginStep(int i)
        {
            if (i >= GuideLines.Steps.Length)
            {
                Finish();
                return;
            }
            Step = i;
            StepDef s = GuideLines.Steps[i];
            _hasScreen0 = _world.HasPanel;
            _screen0 = _hasScreen0 ? _world.PanelScreen : MenuScreen.Home;
            _hasLayout0 = _world.TrySingleLayout(out _layout0);
            Nodded = 0;
            Kicks = 0;
            DemoT = 0f;
            DemoKind = s.Demo;
            SetMood(SunnyMood.Calm);
            SetState(SunnyState.Step, s.Timeout);
            Say(LineFor(s));
        }

        /// <summary>The step's gesture happened: the demo stops and Sunny smiles at once, but the praise waits for the line
        /// in flight to end (one praise however often the gesture repeats).</summary>
        public void Complete()
        {
            if (State != SunnyState.Step) return;
            EndDemo();
            SetMood(SunnyMood.Happy);
            After(SunnyNext.Praise);
        }

        /// <summary>The step waited long enough: its skip line, again only once the line in flight has ended.</summary>
        public void Timeout()
        {
            if (State != SunnyState.Step) return;
            EndDemo();
            After(SunnyNext.Skip);
        }

        void After(SunnyNext what)
        {
            if (LineBusy() || QuietT < GuideTiming.Gap)
            {
                SetState(SunnyState.Wait);
                Queued = what;
            }
            else SayNext(what);
        }

        void SayNext(SunnyNext what)
        {
            if (what != SunnyNext.Skip)
            {
                string id = GuideLines.Praise[Mathf.Max(0, Step) % GuideLines.Praise.Length];
                Say(id);
                SetState(SunnyState.Praise, Hold(id, GuideTiming.PraiseT));
            }
            else
            {
                string id = CurrentStep.Skip ?? GuideLines.Skip;
                Say(id);
                SetState(SunnyState.Skip, Hold(id, GuideTiming.SkipT));
            }
        }

        void Finish()
        {
            Done = true;
            Step = -1;
            EndDemo();
            SetMood(SunnyMood.Happy);
            Say(GuideLines.Bye);
            SetState(SunnyState.Bye, Hold(GuideLines.Bye, GuideTiming.ByeT));
        }

        void Leave(bool say = true)
        {
            LeaveY = Base.y;
            SetState(SunnyState.Leave, GuideTiming.LeaveT);
            if (Hinting) { }                         // a hint fades out with its caption
            else if (say && !InTip) Say(GuideLines.Leave);
            else
            {
                BubbleVisible = false;   // a tip (or a quiet script end) leaves without a word: the caption goes with it
                LineVersion++;
            }
        }

        void Vanish()
        {
            Hinting = false;
            Hush();
            EndDemo();
            InTip = false;
            Visible = false;
            LightIntensity = 0f;
            SetState(SunnyState.Gone);
        }

        // ---------------- what each step watches ----------------

        bool PanelOpen() => _world.HasPanel && _world.PanelShown;

        void CheckStep()
        {
            switch (CurrentStep.Id)
            {
                case StepId.Open:
                    if (PanelOpen()) Complete();
                    break;
                case StepId.Poke:
                    if (_world.HasPanel && (!_hasScreen0 || _world.PanelScreen != _screen0)) Complete();
                    break;
                case StepId.Close:
                    if (!PanelOpen()) Complete();
                    break;
                case StepId.Swipe:
                    CheckSwipe();
                    break;
                case StepId.Nod:
                    if (Nodded > 0) Complete();
                    break;
            }
        }

        // In single mode the first instrument seen during the step is the reference; a page turn away from it completes.
        void CheckSwipe()
        {
            if (!_world.TrySingleLayout(out InstrumentKind current)) return;
            if (!_hasLayout0)
            {
                _hasLayout0 = true;
                _layout0 = current;
            }
            else if (current != _layout0) Complete();
        }

        /// <summary>MenuEvent hook (the panel changed).</summary>
        public void OnPanel(MenuChange change)
        {
            if (State != SunnyState.Step) return;
            StepId id = CurrentStep.Id;
            if ((id == StepId.Open && change == MenuChange.Open) || (id == StepId.Poke && change == MenuChange.Screen)
                || (id == StepId.Close && change == MenuChange.Close)) Complete();
        }

        /// <summary>LayoutChangedEvent hook: the swipe step is also checked when the layout says it changed.</summary>
        public void OnLayoutChanged()
        {
            if (State == SunnyState.Step && CurrentStep.Id == StepId.Swipe) CheckSwipe();
        }

        /// <summary>NodEvent hook (counts every nod).</summary>
        public void OnNod() => Nodded++;

        /// <summary>A kick drum event from the recorder stream. Playback never counts; on a desktop any
        /// kick does; in a headset only a kick with every hand far from the pad (so it came from the nod).</summary>
        public void OnKick(bool duringPlayback)
        {
            if (duringPlayback) return;
            Kicks++;
            if (State != SunnyState.Step || CurrentStep.Id != StepId.Nod) return;
            if (_world.IsDesktop || _world.HandsFarFromPad()) Complete();
        }

        // =====================================================================================================
        // Speech (Docs/08-ux-lessons-menu-guide.md 3.4)
        // =====================================================================================================

        /// <summary>A line (an id in GuideLines, or any text, which is captioned but has no voice) goes to the bubble and its
        /// recording to the voice.</summary>
        public void Say(string idOrText)
        {
            int i = GuideLines.IndexOf(idOrText);
            string id = i >= 0 ? GuideLines.Ids[i] : "";
            string merged = i < 0 ? GuideLines.Merged.TextOf(idOrText) : null;   // a module's line (platform.md 8.2)
            if (merged != null) id = idOrText;
            Line = i >= 0 ? GuideLines.Texts[i] : merged ?? (idOrText ?? "");
            LineId = id;
            LineAt = Time;
            QuietT = 0f;
            Lines++;
            LineVersion++;
            BubbleVisible = true;
            Speak(id);
        }

        public void Hush()
        {
            Line = "";
            LineId = "";
            LineVersion++;
            BubbleVisible = false;
            StopVoice();
            _hasTalk = false;
            _wasSpeaking = false;
        }

        // Never two lines at once (the one in flight stops first); a line whose recording is missing or late stays a caption.
        void Speak(string id)
        {
            StopVoice();
            _talkSeq++;
            _hasTalk = true;
            _talkId = id;
            _talkAt = Time;
            _talkStarted = _talkEnded = _talkCancelled = _talkFailed = _talkLate = _talkPending = _talkLive = false;
            _talkStartAt = _talkEndDsp = 0;
            _talkEndT = 0f;
            _talkDuration = 0f;
            _wasSpeaking = false;
            _voicePeak = 0f;
            if (_voice == null || !_voice.Available) return;
            Chime();
            if (id.Length == 0) return;
            _talkAsked = _voice.Now;
            if (!_voice.HasLine(id))
            {
                _talkFailed = true;
                Failed++;
                return;
            }
            _talkPending = true;
            PumpTalk();   // a rendered line plays at once (a cached buffer is ready before the next frame)
        }

        // Loading a line: wait for the processed buffer; more than LATE after the caption it is dropped.
        void PumpTalk()
        {
            if (!_talkPending) return;
            if (_talkCancelled)
            {
                _talkPending = false;
                return;
            }
            double now = _voice.Now;
            if (_voice.LineReady(_talkId))
            {
                _talkPending = false;
                if (now - _talkAsked > GuideTiming.Late)
                {
                    _talkFailed = _talkLate = true;
                    Failed++;
                    return;
                }
                PlayTalk(Math.Max(now, _talkAsked + GuideTiming.Lead));
            }
            else if (now - _talkAsked > GuideTiming.Late)
            {
                _talkPending = false;
                _talkFailed = _talkLate = true;
                Failed++;
            }
        }

        void PlayTalk(double when)
        {
            double end = _voice.Play(_talkId, when);
            if (end <= 0)
            {
                _talkFailed = true;
                Failed++;
                return;
            }
            _talkDuration = (float)(end - when);
            _talkStarted = true;
            _talkLive = true;
            _talkStartAt = when;
            _talkEndDsp = end;
            // The same end on Sunny's own clock, which lineBusy uses (endT).
            _talkEndT = Time + (float)(when - _voice.Now) + _talkDuration;
            _playing++;
            MaxPlaying = Mathf.Max(MaxPlaying, _playing);
            Played++;
        }

        void ReleaseTalk()
        {
            if (!_talkLive) return;
            _talkLive = false;
            _playing = Mathf.Max(0, _playing - 1);
        }

        /// <summary>Cut the line in flight (Hide Sunny, the headset taken off, an explicit skip, the next line).</summary>
        public void StopVoice()
        {
            if (!_hasTalk || _talkEnded || _talkCancelled) return;
            _talkCancelled = true;
            _talkPending = false;
            if (!_talkStarted || !_talkLive) return;
            ReleaseTalk();
            Stopped++;
            _voice.Stop();
        }

        // The line ended: the audio clock passed the scheduled end.
        void UpdateTalkEnd()
        {
            if (!_hasTalk || !_talkStarted || _talkEnded || _talkCancelled) return;
            if (_voice.Now >= _talkEndDsp || Time >= _talkEndT + StallSeconds)
            {
                _talkEnded = true;
                ReleaseTalk();
            }
        }

        /// <summary>Whether the line in flight still has words to come: its recording is sounding, or still on its way (it
        /// either plays or is dropped within LATE), or, as a caption only, it has not been up as long as it would take to say.</summary>
        public bool LineBusy()
        {
            if (!_hasTalk || _talkCancelled || _talkEnded) return false;
            if (_talkStarted) return Time < _talkEndT + GuideTiming.BusyTail;
            if (_talkId.Length > 0 && !_talkFailed && _voice != null && _voice.Available && Time - _talkAt < GuideTiming.Late + 0.1f) return true;
            return _talkId.Length > 0 && Time - _talkAt < LineSeconds(_talkId);
        }

        /// <summary>The line is being voiced now (the mouth and the closing chirp follow it).</summary>
        public bool Speaking() => _hasTalk && _talkStarted && !_talkEnded && !_talkCancelled;

        // The mouth follows the voice: open while the level is near the line's recent peak, shut in the gaps between
        // words, with hysteresis so it never flickers frame to frame.
        bool NextMouth(float dt)
        {
            float level = Speaking() ? _voice.Level : 0f;
            MouthLevel = level;
            _voicePeak = Mathf.Max(level, _voicePeak * Mathf.Exp(-dt * GuideTiming.MouthDecay));
            if (level < GuideTiming.MouthFloor) return false;
            float k = MouthOpen ? GuideTiming.MouthCloseK : GuideTiming.MouthOpenK;
            return level > _voicePeak * k;
        }

        void Chime()
        {
            if (_voice == null || !_voice.Available) return;
            _voice.Chirp(ChirpKind.Open);
            Chirps++;
        }

        void EndChirp()
        {
            if (_voice == null || !_voice.Available) return;
            _voice.Chirp(ChirpKind.Close);
            Chirps++;
        }

        // =====================================================================================================
        // The face, the demo, the frame
        // =====================================================================================================

        void SetMood(SunnyMood mood) => Mood = mood;

        void Blink(float dt)
        {
            if (_blinkT > 0f)
            {
                _blinkT -= dt;
                if (_blinkT <= 0f && EyesShut) EyesShut = false;
                return;
            }
            _nextBlink -= dt;
            if (_nextBlink <= 0f)
            {
                _nextBlink = GuideTiming.BlinkMin + (float)_rng.Next() * GuideTiming.BlinkSpan;
                _blinkT = GuideTiming.BlinkT;
                EyesShut = true;
            }
        }

        // ---------------- the platform's tip demos ----------------

        bool _tipDemo, _tipAnchored, _thenAnchored;
        Vector3 _tipAnchor, _tipUp, _thenAnchor, _thenUp;
        DemoKind _then;
        DemoMotion _motion;

        /// <summary>The anchor the tip demo points at (world) and its up (its length may carry a radius).</summary>
        public Vector3 TipAnchor => _tipAnchor;
        public bool TipDemoAnchored => _tipDemo && _tipAnchored;

        /// <summary>While a tip is said: loop <paramref name="kind"/> at the anchor (world point and up; anchored false: one of
        /// the tour's demos in front of Sunny), then <paramref name="then"/> at its own anchor after one loop.</summary>
        public void ShowTipDemo(DemoKind kind, bool anchored, Vector3 anchor, Vector3 up, DemoMotion motion = null,
            DemoKind then = DemoKind.None, bool thenAnchored = false, Vector3 thenAnchor = default, Vector3 thenUp = default)
        {
            if (kind == DemoKind.None || (!anchored && !SunnyDemo.IsTourKind(kind))) return;
            _tipDemo = true;
            _tipAnchored = anchored;
            _tipAnchor = anchor;
            _tipUp = up.sqrMagnitude > 1e-12f ? up : Vector3.up;
            _motion = motion;
            _then = then;
            _thenAnchored = thenAnchored;
            _thenAnchor = thenAnchor;
            _thenUp = thenUp.sqrMagnitude > 1e-12f ? thenUp : Vector3.up;
            DemoKind = kind;
            DemoT = 0f;
        }

        /// <summary>The tip was learned while it is said: its demo ends, the line finishes.</summary>
        public void EndTipDemo() { if (_tipDemo) EndDemo(); }

        float TipDemoLoop => DemoKind == DemoKind.Custom && _motion != null && _motion.Loop > 0.1f ? _motion.Loop : GuideTiming.DemoLoop;

        void EndDemo()
        {
            _tipDemo = false;
            _worldDemo = false;   // first five
            DemoKind = DemoKind.None;
            _demo.Visible = false;
            _demo.Ring = false;
            _demo.Ghost = false;
            _demo.Arrow = false;
        }

        void UpdateDemo(float dt)
        {
            if (DemoKind == DemoKind.Press && State == SunnyState.Script) { DemoT += dt; return; }   // posed after placement
            if (_tipDemo && State == SunnyState.Tip)
            {
                DemoT += dt;
                if (_then != DemoKind.None && DemoT >= TipDemoLoop)
                {
                    // One loop of the first demo, then the second one at its own anchor.
                    DemoKind = _then;
                    _then = DemoKind.None;
                    _tipAnchored = _thenAnchored;
                    _tipAnchor = _thenAnchor;
                    _tipUp = _thenUp;
                    DemoT = 0f;
                    if (!_tipAnchored && !SunnyDemo.IsTourKind(DemoKind)) { EndDemo(); return; }
                }
                if (!_tipAnchored) SunnyDemo.Evaluate(DemoKind, DemoT % GuideTiming.DemoLoop, ref _demo);   // in front of Sunny
                return;
            }
            if (State == SunnyState.Script && UpdateScriptDemo(dt)) return;                          // first five
            if (DemoKind == DemoKind.None || State != SunnyState.Step)
            {
                if (_demo.Visible) EndDemo();
                return;
            }
            DemoT += dt;
            SunnyDemo.Evaluate(DemoKind, DemoT % GuideTiming.DemoLoop, ref _demo);
        }

        float NodPose()
        {
            if (DemoKind != DemoKind.Nod || (State != SunnyState.Step && State != SunnyState.Script)) return 0f;   // first five: scripts nod too
            return SunnyDemo.NodAmount(DemoT % GuideTiming.DemoLoop);
        }

        /// <summary>Beside the head: 0.55 m ahead, 0.25 m to the left, 0.10 m under the eyes (the goal).</summary>
        public Vector3 Goal()
        {
            if (Perch.HasValue && State == SunnyState.Script) return Perch.Value;
            if (!_world.TryHead(out Vector3 head, out Vector3 fwd)) return Base;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);   // design cross(fwd, up), operands swapped for the z mirror
            Vector3 g = head + fwd * GuideTiming.Ahead - right * GuideTiming.Left;
            g.y = head.y - GuideTiming.Down;
            return g;
        }

        /// <summary>One frame: speech, face, demo, placement, then the run.</summary>
        public void Update(float dt)
        {
            Time += dt;
            if (!Visible)
            {
                LightIntensity = 0f;
                return;
            }
            PumpTalk();
            UpdateTalkEnd();
            Blink(dt);
            bool speaking = Speaking();
            MouthOpen = NextMouth(dt);
            // A line that ran its course gets the closing chirp; a line cut short gets none.
            if (_wasSpeaking && !speaking && _hasTalk && _talkEnded) EndChirp();
            _wasSpeaking = speaking;
            QuietT = LineBusy() ? 0f : QuietT + dt;
            UpdateDemo(dt);

            // Place: ease toward the goal (snap on the first frame, frozen while leaving), then bob; face the player.
            Vector3 goal = Goal();
            float k = _placed ? 1f - Mathf.Exp(-dt * GuideTiming.Follow) : 1f;
            if (State != SunnyState.Leave) Base = Vector3.Lerp(Base, goal, k);
            _placed = true;
            Vector3 pos = Base;
            pos.y += Mathf.Sin(Time * GuideTiming.BobRateHz * Mathf.PI * 2f) * GuideTiming.BobAmp;
            Quaternion rot = Rotation;
            if (_world.TryHead(out Vector3 head, out _))
            {
                Vector3 away = pos - head;   // facing rule: local +z away from the player, the face (-z) toward them
                if (away.sqrMagnitude > 1e-8f) rot = Quaternion.LookRotation(away, Vector3.up);
            }
            float nod = NodPose();
            NodAmount = nod;
            if (nod > 0f)
            {
                pos.y -= GuideTiming.NodDip * nod;
                // design-frame rotateX(+0.45 n) tips the face (+z there) down; mirrored, the face is -z, so the angle is negated.
                rot *= Quaternion.AngleAxis(-GuideTiming.NodTiltRad * nod * Mathf.Rad2Deg, Vector3.right);
            }
            LightIntensity = GuideTiming.LightBase + GuideTiming.LightPulse * Mathf.Sin(Time * GuideTiming.LightRate);

            // The run. Each timed state also waits for its line to end (it can start late, while its buffer renders).
            bool quiet = QuietT >= GuideTiming.Gap;
            switch (State)
            {
                case SunnyState.Intro:
                    Timer -= dt;
                    if (Timer <= 0f && quiet) BeginStep(0);
                    break;
                case SunnyState.Step:
                    CheckStep();
                    if (State == SunnyState.Step && CurrentStep.Timeout > 0f)
                    {
                        Timer -= dt;
                        if (Timer <= 0f) Timeout();
                    }
                    break;
                case SunnyState.Wait:
                    if (quiet) SayNext(Queued == SunnyNext.None ? SunnyNext.Praise : Queued);
                    break;
                case SunnyState.Praise:
                case SunnyState.Skip:
                    Timer -= dt;
                    if (Timer <= 0f && quiet) BeginStep(Step + 1);
                    break;
                case SunnyState.Bye:
                    Timer -= dt;
                    if (Timer <= 0f && quiet) Leave();
                    break;
                case SunnyState.Tip:
                    Timer -= dt;
                    if (Timer <= 0f && quiet)
                    {
                        if (!TalkCancelled && !LastTipLine)
                        {
                            string next = _tipLines[++_tipLine];   // the tip's next line
                            Say(next);
                            Timer = Hold(next, LastTipLine ? _tipMinHold : GuideTiming.TipT);
                            break;
                        }
                        TipHeard = !TalkCancelled;   // a line cut short (headset off) is not counted as heard
                        Leave(say: false);
                    }
                    break;
                case SunnyState.Leave:
                {
                    Timer -= dt;
                    float u = 1f - Mathf.Max(0f, Timer) / GuideTiming.LeaveT;
                    Scale = 1f - 0.95f * u;
                    BodyOpacity = 1f - u;
                    BubbleOpacity = 1f - u;
                    Glow = 1f - u;
                    pos.y += GuideTiming.LeaveRise * u;
                    LightIntensity = (1f - u) * GuideTiming.LightLeave;
                    if (Timer <= 0f) Vanish();
                    break;
                }
            }
            pos += PokeStep(dt);   // first five: the push of a poke springs back
            Position = pos;
            if (DemoKind == DemoKind.Press && State == SunnyState.Script && !_worldDemo)
            {
                Rotation = rot;
                SunnyDemo.EvaluatePress(DemoT % GuideTiming.DemoLoop, PressAnchor, PressUp, pos, rot, Scale, _world.TryHead(out Vector3 eye, out _) ? eye : pos - rot * Vector3.forward, ref _demo);
            }
            else if (_tipDemo && _tipAnchored && State == SunnyState.Tip)
            {
                Vector3 eye = _world.TryHead(out Vector3 h, out _) ? h : pos - rot * Vector3.forward;
                SunnyDemo.EvaluateAnchored(DemoKind, DemoT % TipDemoLoop, _tipAnchor, _tipUp, eye, _motion, pos, rot, Scale, ref _demo);
            }
            else if (_worldDemo && State == SunnyState.Script) PoseWorldDemo(pos, rot);   // first five
            Rotation = rot;
        }
    }
}
