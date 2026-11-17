// Shift+H toggles Sunny on a desktop (ARCHITECTURE.md 15.8): a test keyboard drives DesktopInput
// through the Input System. Plain H stays a piano key; Ctrl/Cmd+Shift+H (app and OS shortcuts) and any key while
// presenting (a headset session, here BootOptions.SimulateHeadset) do nothing.
using System.Collections;
using MusicRoom.Audio;
using MusicRoom.Core;
using MusicRoom.Guide;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace MusicRoom.Tests.PlayMode.Guide
{
    public class GuideShiftHTests
    {
        const float Dt = 1f / 72f;
        TestBoot _boot;
        GuideModule _guide;
        Keyboard _keyboard;
        InputSettings.EditorInputBehaviorInPlayMode _savedEditorBehaviour;
        InputSettings.BackgroundBehavior _savedBackground;

        IEnumerator Boot(bool simulateHeadset)
        {
            MusicRoom.Guide.GuideModule.ClassicTour = true;   // first five: these tests check the classic five-step tour
            _boot = new TestBoot(headless: true, simulateHeadset: simulateHeadset, manualFrames: true, audioClock: () => 0.0);
            _boot.Settings.SetInt(AppSettings.KeyTourDone, 1);   // no first-launch tour: only the keys start Sunny
            _boot.Options.ModuleFilter = t => t == typeof(AudioModule) || t == typeof(GuideModule);
            AudioBootGuard.Begin();
            yield return _boot.Start();
            yield return null;
            AudioBootGuard.End();
            _guide = (GuideModule)App.Current.Get<IGuide>();

            // A test keyboard as Keyboard.current. Batchmode has no focused Game View: route its events to the player.
            _savedEditorBehaviour = InputSystem.settings.editorInputBehaviorInPlayMode;
            _savedBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _keyboard = InputSystem.AddDevice<Keyboard>("GuideTestKeyboard");
            if (!_keyboard.enabled) InputSystem.EnableDevice(_keyboard);
            _keyboard.MakeCurrent();
            Keys();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MusicRoom.Guide.GuideModule.ClassicTour = false;
            AudioBootGuard.Reset();
            if (_keyboard != null)
            {
                InputSystem.RemoveDevice(_keyboard);
                _keyboard = null;
                InputSystem.settings.editorInputBehaviorInPlayMode = _savedEditorBehaviour;
                InputSystem.settings.backgroundBehavior = _savedBackground;
            }
            if (_boot != null) yield return _boot.Stop();
            _boot = null;
        }

        /// <summary>Exactly these keys held from now on (one input update), then one app frame.</summary>
        void Press(params Key[] keys)
        {
            Keys(keys);
            _boot.Step(Dt);
        }

        void Keys(params Key[] keys)
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));
            InputSystem.Update();
        }

        [UnityTest]
        public IEnumerator ShiftHTogglesSunnyOnTheDesktop()
        {
            yield return Boot(simulateHeadset: false);
            Assert.IsFalse(App.Current.IsPresenting);
            Assert.IsFalse(_guide.Present);

            Keys(Key.LeftShift, Key.H);
            Assert.IsTrue(DesktopInput.Shift && DesktopInput.KeyDown(DKey.H), "the test keyboard reaches DesktopInput");
            _boot.Step(Dt);
            Assert.IsTrue(_guide.Present, "Shift+H shows Sunny");
            Assert.That(_guide.Sunny.LineId, Is.EqualTo(GuideLines.IntroDesk));
            Press(Key.LeftShift, Key.H);
            Assert.IsTrue(_guide.Present, "a held key does not repeat the toggle");
            Press(Key.LeftShift);
            Press(Key.H);
            Assert.IsTrue(_guide.Present, "plain H is a piano key");
            Press();
            Press(Key.RightShift, Key.H);
            Assert.IsFalse(_guide.Present, "Shift+H again hides it");
            Press();

            Press(Key.LeftCtrl, Key.LeftShift, Key.H);
            Assert.IsFalse(_guide.Present, "Ctrl+Shift+H is an app / OS shortcut: ignored");
            Press();
            Press(Key.LeftMeta, Key.LeftShift, Key.H);
            Assert.IsFalse(_guide.Present, "Cmd+Shift+H likewise");
            Press();
            Press(Key.LeftShift, Key.H);
            Assert.IsTrue(_guide.Present, "and a clean Shift+H still works afterwards");
        }

        [UnityTest]
        public IEnumerator ShiftHDoesNothingWhilePresenting()
        {
            yield return Boot(simulateHeadset: true);
            Assert.IsTrue(App.Current.IsPresenting, "a simulated headset session");
            Keys(Key.LeftShift, Key.H);
            Assert.IsTrue(DesktopInput.Shift && DesktopInput.KeyDown(DKey.H), "the key press is there");
            _boot.Step(Dt);
            Assert.IsFalse(_guide.Present, "the desktop shortcut is off in a headset (the menu row toggles Sunny there)");
            _guide.Toggle();
            Assert.IsTrue(_guide.Present);
            Assert.That(_guide.Sunny.LineId, Is.EqualTo(GuideLines.IntroVr));
        }
    }
}
