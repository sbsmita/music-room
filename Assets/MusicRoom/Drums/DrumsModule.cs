// The Drums module (Order 31): builds the kit, the sticks and the nod kick, registers the Rig, the IInstrument and the
// IDrumsApi, parents the drum sources, and runs the module's own frame phases in this order:
//   StickPoses     sticks follow the hands BEFORE PointerInput.Update reads their tip / shoulder pointers
//   (Instruments)  Core ticks the kit with every pointer, sticks included
//   StickHover     pick-up glows, after the instruments and the tutorial; then the drawn sticks pivot and rebound in the
//                  hand (10-upgrade/experience.md E2), before Core's contact visuals (step 8a) read their tips
//   (Grab)         Layout calls IDrumsApi.TryPickUp first in its own grab pass and decides the pinch's consumption
//   StickReleases  the open-hand release
//   NodKick        head nod -> kick, NodEvent
//   EndFrame       drum sources follow the kit in and out of the layout
using System.Collections.Generic;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Hands;
using UnityEngine;

namespace MusicRoom.Drums
{
    public sealed class DrumsModule : AppModule, IDrumHost, IHandFrames
    {
        public const int ModuleOrder = 31;
        public override int Order => ModuleOrder;

        App _app;
        IAudioEngine _audio;
        IRecorder _recorder;
        IGrabService _grab;
        IMixedReality _mr;
        Transform _audioPark;
        bool _sourcesOnKit;
        System.Action<LayoutChangedEvent> _onLayout;
        System.Action<HmdMountEvent> _onMount;

        public DrumKit Kit { get; private set; }
        public DrumSticks Sticks { get; private set; }
        public NodKick Nod { get; private set; }
        public Rig Rig => Kit != null ? Kit.Rig : null;
        /// <summary>The platform adapters (DrumsPlatform) and the sticks' IHandToolProvider (DrumToolProvider).</summary>
        public DrumsPlatform Platform { get; private set; }
        public DrumToolProvider Tools { get; private set; }

        public int NodKicks { get; private set; }

        public override void Init(App app)
        {
            _app = app;
            _audio = app.Get<IAudioEngine>();
            var go = new GameObject("Drums");
            go.transform.SetParent(app.WorldRoot, false);
            Kit = go.AddComponent<DrumKit>();
            Kit.Build(this, app.Materials);
            Sticks = new DrumSticks(Kit, this, app.WorldRoot, Kit.Materials);
            Kit.AttachSticks(Sticks);
            Sticks.Register(app.Input);
            Nod = new NodKick();

            app.Register<Rig>(Kit.Rig);
            app.Register<IInstrument>(Kit);
            app.Register<IDrumsApi>(Kit);
            app.Register<IContactSurface>(Kit);   // 10-upgrade/experience.md E1 / E3: heads, pad, cymbal plates
            Platform = DrumsPlatform.Register(app, this);   // IStowable, IInstrumentPlayback, IGuideAnchors (platform.md 3.3)
            app.Register<IHandToolProvider>(Tools = new DrumToolProvider(Kit, Sticks));   // the sticks as a hand tool (platform.md 4.6)

            var park = new GameObject("DrumAudioPark");
            park.transform.SetParent(app.WorldRoot, false);
            _audioPark = park.transform;
            ParentSourcesToKit();

            _onLayout = OnLayout;
            _onMount = OnMount;
            app.Events.Subscribe(_onLayout);
            app.Events.Subscribe(_onMount);
            Debug.Log($"[Drums] ready: {DrumSpec.PieceCount} pieces, {Sticks.Count} sticks, {Kit.Parts.Meshes.Count} meshes, " +
                      $"{Kit.Parts.Handles.Count} handles, {Kit.PlayedParts.Count} played parts, audio={(_audio != null)}");
        }

        public override void Shutdown()
        {
            if (_app == null) return;
            if (_onLayout != null) _app.Events.Unsubscribe(_onLayout);
            if (_onMount != null) _app.Events.Unsubscribe(_onMount);
            Sticks?.Unregister(_app.Input);
        }

        public override void Tick(FramePhase phase, float dt)
        {
            if (Kit == null) return;
            switch (phase)
            {
                case FramePhase.StickPoses:
                    // The kit left the layout this frame (whatever the event order was): no stick stays in a hand.
                    if (Sticks.AnyHeld && !Kit.IsPresent) Sticks.ReleaseAll();
                    Sticks.UpdatePoses(dt);
                    break;
                case FramePhase.StickHover:
                    if (!Placing && !Arranging) Sticks.Hover(_app.Input.HandPointers, _app.HoverGlow);
                    Kit.Grab = Grab;
                    Kit.BeginContactFrame();
                    Sticks.UpdateVisuals(dt, Kit);
                    break;
                case FramePhase.StickReleases:
                    Sticks.UpdateReleases(dt);
                    break;
                case FramePhase.NodKick:
                    RunNodKick(dt);
                    break;
                case FramePhase.EndFrame:
                    SyncSourceParking();
                    break;
            }
        }

        bool Placing
        {
            get
            {
                if (_mr == null) _mr = _app.Get<IMixedReality>();
                return _mr != null && _mr.IsPlacing;
            }
        }

        IGrabService Grab => _grab ?? (_grab = _app.Get<IGrabService>());

        IArrangeMode _arrange;
        /// <summary>"Move instruments" is on: nothing plays, so no nod kick and no stick glows either.</summary>
        bool Arranging
        {
            get
            {
                if (_arrange == null) _arrange = _app.Get<IArrangeMode>();
                return _arrange != null && _arrange.Arranging;
            }
        }

        /// <summary>The nod kick's gate: presenting, not MR placing, no rig held, not arranging, the kit in the scene.</summary>
        void RunNodKick(float dt)
        {
            IGrabService grab = Grab;
            // The presenting gate is App.IsPresenting (ARCHITECTURE.md section 3.3): true in XR and
            // in tests booted with a simulated headset.
            bool active = _app.IsPresenting && !Placing && (grab == null || !grab.AnyHeld) && !Arranging && Kit.IsPresent;
            Transform head = _app.Head;
            float vel = Nod.Tick(dt, active, head != null ? head.forward : Vector3.forward, _app.Clock.Now);
            if (vel <= 0f) return;
            // The real kick (not the hand pad), so the hit sounds from the kick's own source.
            DrumPiece kick = Kit.Piece(DrumSpec.KickId);
            if (kick != null) Kit.Hit(kick, vel, null, true, new DrumStrike { Pos = NodSpec.StrikePos, Zone = DrumZone.Head });
            NodKicks++;
            _app.Events.Publish(new NodEvent { Velocity = vel });
        }

        void OnLayout(LayoutChangedEvent e)
        {
            // Layout apply: the kit leaving the room takes its sticks with it.
            if (!Kit.IsPresent) Sticks.ReleaseAll();
        }

        void OnMount(HmdMountEvent e)
        {
            // Session end: the headset is off, nobody is holding sticks.
            if (!e.Mounted) Sticks.ReleaseAll();
        }

        // =============================== audio sources ===============================

        /// <summary>ARCHITECTURE.md section 7.2: each drum source on its piece group (the kick's on the shell, the pad plays
        /// through it), the drum room at the kit centre.</summary>
        void ParentSourcesToKit()
        {
            _sourcesOnKit = true;
            if (_audio == null) return;
            for (int i = 0; i < DrumSpec.PieceCount; i++)
            {
                PieceSpec spec = DrumSpec.Pieces[i];
                if (spec.Pad) continue;
                _audio.SetSourceParent(spec.Source, Kit.Piece(i).Group, Vector3.zero);
            }
            _audio.SetSourceParent(SourceId.DrumRoom, Kit.transform, DrumSpec.KitCentre);
        }

        /// <summary>When Layout takes the kit out of the room it deactivates the rig group, which would silence the drum
        /// sources parented under it; they must keep sounding where the kit was (recorder playback of a take with
        /// drums in single mode). So the sources wait on a park object at their last world positions until the kit is back.</summary>
        void SyncSourceParking()
        {
            bool onKit = Kit.gameObject.activeInHierarchy;
            if (onKit == _sourcesOnKit || _audio == null) return;
            if (onKit) { ParentSourcesToKit(); return; }
            _sourcesOnKit = false;
            for (SourceId id = SourceId.Kick; id <= SourceId.DrumRoom; id++)
            {
                SynthSource s = _audio.Source(id);
                if (s == null) continue;
                Vector3 world = s.transform.position;
                _audio.SetSourceParent(id, _audioPark, _audioPark.InverseTransformPoint(world));
            }
        }

        // =============================== IDrumHost / IHandFrames ===============================
        public double NowMs => _app.Clock.NowMs;
        public IAudioEngine Audio => _audio;
        public IRecorder Recorder => _recorder ?? (_recorder = _app.Get<IRecorder>());
        public IReadOnlyList<Pointer> HandPointers => _app.Input.HandPointers;
        /// <summary>Desktop paths run while the app is not presenting (ARCHITECTURE.md section 0).</summary>
        public bool DesktopClicks => !_app.IsPresenting;
        public Camera DesktopCamera => _app.Rig != null ? _app.Rig.HeadCamera : null;
        public HandFrame Frame(HandState hand) => hand != null ? _app.Input.Frame(hand) : null;
    }
}
