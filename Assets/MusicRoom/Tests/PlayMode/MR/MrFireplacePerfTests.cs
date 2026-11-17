// The cost of the mixed reality fireplace (Category Performance: `Tools/unity.sh test-perf`; Room/Decor/MrFireplace.cs), in
// one of MRUK's recorded rooms in the editor (Apple silicon): a frame while it burns (the flicker of its light, the checks),
// a frame while FIRE is off in mixed reality, a frame in the VR room, and one placing (a FIRE press: the scene model's
// walls and blocks, the instruments, the paintings, MrHearthPlace's search). Printed as [PERF] lines. The GPU side: 9
// draws while it burns (brick, soot, stone, logs, coals, glow, flames, smoke, embers) and one more pixel light; none off.
#if UNITY_EDITOR
using System.Collections;
using System.Diagnostics;
using MusicRoom.Core;
using MusicRoom.Room;
using MusicRoom.Room.Decor;
using NUnit.Framework;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace MusicRoom.Tests.PlayMode.MR
{
    [Category("Performance")]
    public class MrFireplacePerfTests : MrTestRig
    {
        const float Dt = 1f / 72f;
        protected override bool TableOnEnter => false;
        protected override void Prepare(MemorySettingsStore settings) => settings.SetInt(AppSettings.KeyTourDone, 1);

        [UnityTest]
        public IEnumerator TheFireplaceCostsLittleAFrame()
        {
            yield return EnterRoom("MeshLivingRoom2");
            StepFrames(4);
            var room = App.Get<IRoom>() as RoomModule;
            MrFireplace fire = room.MrFireplace;
            // placing: FIRE on, the first frame (the search), measured over a few presses
            const int presses = 8;
            double placeMs = 0;
            var sw = new Stopwatch();
            for (int i = 0; i < presses; i++)
            {
                room.Set(AmbienceKind.Fire, true);
                sw.Restart();
                fire.Tick(Dt, false);
                placeMs += sw.Elapsed.TotalMilliseconds;
                room.Set(AmbienceKind.Fire, false);
                for (int k = 0; k < 72; k++) fire.Tick(Dt, false);
            }
            placeMs /= presses;
            Assert.That(fire.Placements, Is.EqualTo(presses));
            room.Set(AmbienceKind.Fire, true);
            for (int i = 0; i < 144; i++) fire.Tick(Dt, false);
            Assert.That(fire.Level, Is.EqualTo(1f));
            const int frames = 3000;
            sw.Restart();
            for (int i = 0; i < frames; i++) fire.Tick(Dt, false);
            double burningUs = sw.Elapsed.TotalMilliseconds * 1000.0 / frames;
            room.Set(AmbienceKind.Fire, false);
            for (int i = 0; i < 144; i++) fire.Tick(Dt, false);
            Assert.IsFalse(fire.Showing);
            sw.Restart();
            for (int i = 0; i < frames; i++) fire.Tick(Dt, false);
            double offUs = sw.Elapsed.TotalMilliseconds * 1000.0 / frames;
            Mr.Exit();
            StepFrames(2);
            sw.Restart();
            for (int i = 0; i < frames; i++) fire.Tick(Dt, true);
            double vrUs = sw.Elapsed.TotalMilliseconds * 1000.0 / frames;
            Debug.Log($"[PERF] mixed reality fireplace: burning {burningUs:F2} us/frame, FIRE off {offUs:F2} us/frame, VR room {vrUs:F2} us/frame; " +
                      $"placing (a FIRE press) {placeMs:F2} ms once (editor, Apple silicon; GPU: 9 draws + 1 pixel light while it burns, 0 off)");
            Assert.That(burningUs, Is.LessThan(30.0), "a small share of the 11 ms frame");
            Assert.That(offUs, Is.LessThan(10.0));
            Assert.That(vrUs, Is.LessThan(10.0));
            Assert.That(placeMs, Is.LessThan(20.0), "a FIRE press does not hitch");
        }
    }
}
#endif
