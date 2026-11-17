# Sheet music: phase B (done)

Phase B is applied: `Host/TodayHost.cs` is gone, `Host/PlatformHost.cs` runs on the platform hooks, the module registers
`IGuideAnchors` and `IPointerClaimant`, the tips are `Tips/SheetTipSource.cs` (an `IGuideTipSource`), the signals are
`InstrumentSignalEvent { Kind = Piano }`, the paper sound plays through `AddOneShotPreset` / `PlayOneShot`, the asmdef no
longer references Piano or Lessons (it allows unsafe code for the raster job), the rest's pose comes from the host
(`BookGeometry.SetRest`; the defaults equal `PianoMusicRest`), and a lesson without a sheet-book.json entry prints from its
own `meter` / `unit` / `pickup` / `credit` (two staves when a target names a hand). The plan written in phase A follows,
for the record.

# Sheet music: notes for phase B (moving onto the finished platform)

Phase A was built in a copy of the merged project (no platform hooks). Everything the platform will
provide goes through ONE adapter, `Host/ISheetHost.cs`; phase A's implementation is `Host/TodayHost.cs`.

## 1. Copy

Copy `Assets/MusicRoom/Sheet/**`, `Assets/MusicRoom/Tests/EditMode/Sheet/**` (with `Reference~/`) and
`Assets/MusicRoom/Tests/PlayMode/Sheet/**`, keeping every `.meta`. `MusicRoom.Sheet.asmdef.meta` already carries the
platform stub's GUID `9a59a9c072e594e73b2e4258ada28282`; the stub's `SheetModule.cs` is replaced by ours (same class
name, Order 45). The two test-asmdef lines of CONTRACT-REQUESTS 1 are already in the platform base.

## 2. Switch the adapter (a few lines)

1. Add `Host/PlatformHost.cs` and delete `Host/TodayHost.cs`:
   ```csharp
   public sealed class PlatformHost : ISheetHost
   {
       readonly App _app; IPianoApi _piano; ITutorial _tut; IAudioEngine _audio;
       readonly int[] _presets = { -1, -1, -1, -1 };
       readonly SheetPaperSound.Shot[] _shots = new SheetPaperSound.Shot[SheetPaperSound.MaxShotsPerTurn];
       public PlatformHost(App app) { _app = app; }
       IPianoApi Piano => _piano ?? (_piano = _app.Get<IPianoApi>());
       public Transform PianoRoot => Piano?.Root;
       public Pose MusicRestLocal => Piano != null ? Piano.MusicRestLocal : new Pose(BookGeometry.RestLocalPosition, BookGeometry.RestLocalRotation);
       public float MusicRestLedge => Piano != null && Piano.MusicRestLedge > 0f ? Piano.MusicRestLedge : BookGeometry.DefaultLedge;
       public Pose? MusicRestPose => Piano?.MusicRestPose;
       public int CurrentEvent => (_tut ?? (_tut = _app.Get<ITutorial>()))?.CurrentEvent ?? -1;
       public void PaperTurn(float seconds, float level, uint seed, double when)
       {
           var a = _audio ?? (_audio = _app.Get<IAudioEngine>());
           if (a == null) return;
           for (int p = 0; p < 4; p++) if (_presets[p] < 0) _presets[p] = a.AddOneShotPreset(ToOneShot(SheetPaperSound.Presets[p]));
           int n = SheetPaperSound.Turn(_shots, 0, when, seconds, level, seed);
           for (int i = 0; i < n; i++) a.PlayOneShot(_presets[_shots[i].Preset], _shots[i].Gain * SheetPaperSound.SourceGain, SourceId.PianoMid, 0, _shots[i].At);
       }
       public float Tip(string id) => _app.Get<IGuide>()?.Tip(id) ?? 0f;
       public void Signal(string s) => _app.Events.Publish(new InstrumentSignalEvent { Kind = InstrumentKind.Piano, Signal = s });
       public double AudioNow => _app.AudioTime;
   }
   ```
   `ToOneShot` maps `SheetPaperSound.PresetSpec` (lanes: type bp/lp/hp, Hz, Q, tau s, mix; length s; level dB) onto the
   platform's `OneShotPreset` struct (check its field names; the values are the lab's, vectors-checked by
   `SheetPaperSoundTests`). `PaperTurn` is called once per leaf with `when` = now; the shots' `At` are absolute audio s.
2. `SheetModule.Init`: `Host = new PlatformHost(app);` (one line).
3. `MusicRoom.Sheet.asmdef`: remove `"MusicRoom.Piano"` and `"MusicRoom.Lessons"` (only TodayHost used them).
4. Tests: `SheetPlayTests` casts `(TodayHost)_m.Host` for `PaperTurns` / `LastPaperSeconds`; count the paper sound instead
   with the engine's test tap (`AudioTestTap`, one-shot calls), or keep a counter in PlatformHost.

## 3. Register what the platform now has

- `IGuideAnchors`: `SheetModule.TryGetAnchor` already has the signature; add the interface and
  `app.Register<IGuideAnchors>(this)` (Kind: default Table, the book is not an instrument).
- `IPointerClaimant`: `ClaimsPoint(world)`: visible, and the point is within 30 mm in front of the open pages
  (`BookGeometry.ToPage`: `s <= PageW + 0.025`, `-0.025 <= v <= PageH`, `0 <= n <= 0.03`), or in the ribbon capsule.
  `app.Register<IPointerClaimant>(this)`.
- `IGuideTipSource`: one parameterless class returning `SheetTipTable.Tips` as `TipSpec { Id, Title, Lines = { Line },
  Trigger = TipTrigger.Manual, Class, Demo (Point for tip_book, Poke for contents / turn / ribbon), Anchor, Success
  (None, or Signal "sheet.title" / "sheet.turn" / "sheet.ribbon"), Covers = { Covers } }` and `SheetTipTable.Features` as
  `FeatureSpec(id, passive)`. The desktop wordings (`*_desk`) are separate line ids the module asks for itself on a desktop
  (`SheetTips.Offer`); give them their own TipSpec ids or keep them as lines only, as the platform's coverage tests prefer.
  The Guide merges `Resources/GuideLines/Sheet.json`, `Resources/VoiceManifests/Sheet.json` and `Resources/Voice/*.wav`
  (8 lines, 22.05 kHz mono, rendered by the lab's voice tool with the project's eSpeak pipeline).
- `InstrumentSignalEvent` replaces `SheetSignalEvent` (PlatformHost.Signal above); delete the struct if nothing else uses it.

## 4. Keep in mind

- `BookGeometry.RestLocalPosition` / `RestLocalRotation` are also used for the ribbon's path onto the top panel and its poke
  capsule (`BookMesh.RibbonTail`, `SheetPokes`): if the platform's `MusicRestLocal` ever differs from PianoBody's rest,
  derive both from the host's pose instead.
- `ITutorial.CurrentEvent` in the platform is "the next block to land, or the one being waited for"; today's
  `Tutorial.CurrentEvent` is the same (`_cur`). The book maps a lesson with `bookSong` + `eventOffset`
  (`piano-ode-first` -> the Ode's events 15..29) when that lesson exists (the first-five tour).
- The platform's `LessonSpec.Notation` / `InstrumentDescriptor.SheetAnchor` (a stand for the marimba, P1) are not used yet.
- After the copy: `compile -> render-setup -> bake -> scene -> test-edit -> test-play -> test-play --gpu -> test-perf`.
  render-setup writes `Sheet/Resources/MaterialTemplates/Sheet/{Paper,Case}.mat`; bake writes
  `Generated/Resources/Generated/Sheet/paper_detail.png`.
- Expected counts from phase A: EditMode 39 (Sheet), PlayMode 8 + 1 Graphics + 1 Performance.
