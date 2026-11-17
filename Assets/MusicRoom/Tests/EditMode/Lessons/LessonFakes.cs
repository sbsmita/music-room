// Test doubles for the Lessons tests: the three instrument APIs with the foundation's key / piece / string tables on
// real Transforms (so block poses can be checked in world space), and an ILessonStage whose eyes and visibility the
// test sets. They record the playback calls (PlaybackWiring tests) and never allocate in the calls the tutorial makes.
using System.Collections.Generic;
using System.Globalization;
using MusicRoom.Core;
using MusicRoom.Lessons;
using UnityEngine;

namespace MusicRoom.Tests.Lessons
{
    static class Fmt
    {
        /// <summary>Culture-independent "0.80" for the recorded call strings.</summary>
        public static string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);
    }

    public sealed class FakePiano : IPianoApi
    {
        public const float KeyW = 0.0235f, KeyBW = 0.0137f, KeyBTOP = 0.012f, KeyL = 0.15f, X0 = -0.611f;
        static readonly float[] X = new float[109];
        static readonly bool[] IsBlack = new bool[109];
        public readonly List<string> Calls = new List<string>();

        static FakePiano()
        {
            int whites = 0;
            for (int m = 21; m <= 108; m++)
            {
                int pc = m % 12;
                bool black = pc == 1 || pc == 3 || pc == 6 || pc == 8 || pc == 10;
                IsBlack[m] = black;
                if (!black) { X[m] = X0 + (whites + 0.5f) * KeyW; whites++; }
                else X[m] = X0 + whites * KeyW + (pc == 1 ? -0.0028f : pc == 3 ? 0.0028f : pc == 6 ? -0.004f : pc == 8 ? 0f : 0.004f);
            }
        }

        public FakePiano(Transform root) { Root = root; }
        public Transform Root { get; }
        public float W => KeyW;
        public float BW => KeyBW;
        public float BTOP => KeyBTOP;
        public float L => KeyL;
        public bool InPlayZone(Vector3 world) => false;
        public bool OverKeys(Vector3 world) => false;
        public bool TryGetKey(int midi, out PianoKeyInfo key)
        {
            key = default;
            if (midi < 21 || midi > 108) return false;
            key = new PianoKeyInfo { Midi = midi, X = X[midi], Black = IsBlack[midi] };
            return true;
        }
        public void PressMidi(int midi, object pointerKey, float vel = 0.75f) { }
        public void ReleaseMidi(int midi, object pointerKey) { }
        public void PlaybackOn(int midi, float vel) => Calls.Add($"on {midi} {Fmt.F(vel)}");
        public void PlaybackOff(int midi) => Calls.Add($"off {midi}");
        public void PlaybackReleaseAll() => Calls.Add("releaseAll");
        public void SetOnSurface(bool onTable) { }
        public void ResetSurface() { }
        public int HeldKeyCount => 0;
    }

    public sealed class FakeDrums : IDrumsApi
    {
        // The foundation stub's pieces (spec 05 section 3): kind, type, radius, pad, kit-local position, pitch (deg).
        static readonly (string kind, DrumPieceType type, float r, bool pad, Vector3 pos, float pitch)[] Data =
        {
            ("snare", DrumPieceType.Drum, 0.18f, false, new Vector3(0f, 0.72f, 0.30f), -5.73f),
            ("tom1", DrumPieceType.Drum, 0.15f, false, new Vector3(-0.02f, 0.86f, 0.54f), -25.78f),
            ("tom2", DrumPieceType.Drum, 0.165f, false, new Vector3(0.29f, 0.85f, 0.42f), -25.78f),
            ("floor", DrumPieceType.Drum, 0.20f, false, new Vector3(0.38f, 0.62f, 0.02f), -2.86f),
            ("kick", DrumPieceType.Drum, 0.28f, false, new Vector3(0.12f, 0.29f, 0.70f), -90f),
            ("kick", DrumPieceType.Drum, 0.10f, true, new Vector3(-0.27f, 0.60f, 0.20f), -14.32f),
            ("hat", DrumPieceType.Cymbal, 0.17f, false, new Vector3(-0.42f, 0.80f, 0.36f), -6.88f),
            ("crash", DrumPieceType.Cymbal, 0.21f, false, new Vector3(-0.39f, 1.02f, 0.58f), -20.05f),
            ("ride", DrumPieceType.Cymbal, 0.22f, false, new Vector3(0.48f, 1.04f, 0.27f), -17.19f),
        };
        readonly List<DrumPieceInfo> _pieces = new List<DrumPieceInfo>();
        public readonly List<string> Calls = new List<string>();

        public FakeDrums(Transform root, bool withoutRide = false)
        {
            Root = root;
            for (int i = 0; i < Data.Length; i++)
            {
                var d = Data[i];
                if (withoutRide && d.kind == "ride") continue;
                var g = new GameObject(d.kind + (d.pad ? "-pad" : "")).transform;
                g.SetParent(root, false);
                g.localPosition = d.pos;
                g.localRotation = Quaternion.Euler(d.pitch, 0f, 0f);
                _pieces.Add(new DrumPieceInfo { Id = i, Kind = d.kind, Type = d.type, Radius = d.r, Pad = d.pad, Transform = g });
            }
        }

        public Transform Root { get; }
        public IReadOnlyList<DrumPieceInfo> Pieces => _pieces;
        public DrumPieceInfo Piece(string kind, bool pad = false) { foreach (var p in _pieces) if (p.Kind == kind && p.Pad == pad) return p; return default; }
        public int KickId => 4;
        public int KickPadId => 5;
        public int HatId => 6;
        public bool HatOpen { get; private set; }
        public void SetHatOpen(bool open, HandState hand, bool playback = false) { HatOpen = open; Calls.Add($"hat {open} {playback}"); }
        public void PlaybackHit(string kind, float vel, DrumStrikeInfo strike) => Calls.Add($"hit {kind} {Fmt.F(vel)} {Fmt.F(strike.Pos)} {strike.Zone} {strike.Open}");
        public void PlaybackChoke(string kind) => Calls.Add($"choke {kind}");
        public bool AnyStickHeld => false;
        public StickPick TryPickUp(HandState hand, Vector3 tip) => StickPick.None;   // no sticks: lessons never pick one up
        public void ReleaseAllSticks() { }
        public bool EveryHandPointerFartherThan(float metres, int pieceId) => true;
    }

    public sealed class FakeHarp : IHarpApi
    {
        static readonly int[] Major = { 0, 2, 4, 5, 7, 9, 11 };
        readonly Vector3[] _a = new Vector3[22], _b = new Vector3[22];
        public readonly float[] Highlights = new float[22];
        public int HighlightCalls;
        public readonly List<string> Calls = new List<string>();

        public FakeHarp(Transform root)
        {
            Root = root;
            Vector2 sb0 = new Vector2(-0.55f, 0.17f), sb1 = new Vector2(0f, 1.10f);
            for (int i = 0; i < 22; i++)
            {
                float t = i / 21f;
                Vector2 a = Vector2.Lerp(sb0, sb1, 0.045f + 0.82f * t);
                Vector2 b = Vector2.Lerp(new Vector2(-0.63f, 1.344f), new Vector2(0.01f, 1.14f), t);
                _a[i] = new Vector3(a.x, a.y, 0f);
                _b[i] = new Vector3(b.x, b.y, 0f);
            }
        }

        public Transform Root { get; }
        public int StringCount => 22;
        public int MidiOf(int i) => 48 + 12 * (i / 7) + Major[i % 7];
        public Vector3 StringStart(int i) => _a[i];
        public Vector3 StringEnd(int i) => _b[i];
        public Vector3 PlayPoint(int i) => Vector3.Lerp(_a[i], _b[i], Mathf.Clamp((0.95f - _a[i].y) / (_b[i].y - _a[i].y), 0.3f, 0.7f));
        public void Highlight(int i, float k = 1f) { Highlights[i] = k; HighlightCalls++; }
        public void PlaybackPluck(int i, float vel, float u = 0.35f, float speed = 0f, string mode = "through") => Calls.Add($"pluck {i} {Fmt.F(vel)} {Fmt.F(u)} {Fmt.F(speed)} {mode}");
        public void PlaybackDamp(int i) => Calls.Add($"damp {i}");
    }

    /// <summary>A stage with all three instruments on posed transforms, eyes at the desktop seat.</summary>
    public sealed class FakeStage : ILessonStage
    {
        public readonly GameObject Root;
        public readonly FakePiano FakePiano;
        public readonly FakeDrums FakeDrums;
        public readonly FakeHarp FakeHarp;
        public Vector3 Eyes = new Vector3(0f, 1.2f, -0.1f);
        public bool PianoShown = true, DrumsShown = true, HarpShown = true;
        public bool NoPiano, NoDrums, NoHarp;

        public FakeStage(bool withoutRide = false)
        {
            Root = new GameObject("FakeStage");
            var piano = new GameObject("Piano").transform;
            piano.SetParent(Root.transform, false);
            piano.SetPositionAndRotation(new Vector3(0.1f, 0.72f, 0.45f), Quaternion.Euler(0f, 12f, 0f));   // off-axis on purpose
            var drums = new GameObject("Drums").transform;
            drums.SetParent(Root.transform, false);
            drums.SetPositionAndRotation(new Vector3(0.2f, 0f, -0.26f), Quaternion.Euler(0f, 90f, 0f));
            var harp = new GameObject("Harp").transform;
            harp.SetParent(Root.transform, false);
            harp.SetPositionAndRotation(new Vector3(-0.78f, 0f, -0.2f), Quaternion.Euler(0f, 83f, 0f));
            FakePiano = new FakePiano(piano);
            FakeDrums = new FakeDrums(drums, withoutRide);
            FakeHarp = new FakeHarp(harp);
        }

        public IPianoApi Piano => NoPiano ? null : FakePiano;
        public IDrumsApi Drums => NoDrums ? null : FakeDrums;
        public IHarpApi Harp => NoHarp ? null : FakeHarp;
        public Vector3 EyePosition => Eyes;
        public bool IsShown(InstrumentKind k) => k == InstrumentKind.Piano ? PianoShown && !NoPiano : k == InstrumentKind.Drums ? DrumsShown && !NoDrums : HarpShown && !NoHarp;

        public void Destroy() => Object.DestroyImmediate(Root);
    }
}
