// Development players only: records the final mix (after MasterBus, the last filter on the listener) to a WAV so a
// device-only sound fault can be heard and measured on the desktop. Arm it with
//   adb shell touch /sdcard/Android/data/com.vrmusicroom.app/files/audiotap
// it records Seconds of audio, writes files/audiotap-<HHmmss>.wav and deletes the trigger. The buffer is allocated once
// when armed; the audio thread only copies into it.
using System;
using System.IO;
using System.Threading;
using UnityEngine;

namespace MusicRoom.Audio
{
    public sealed class AudioTap : MonoBehaviour
    {
        public const float Seconds = 25f;
        const string Trigger = "audiotap";

        float[] _buf;
        int _pos, _channels, _rate;
        volatile bool _recording;
        int _done;
        float _poll;

        void Update()
        {
            if (_recording)
            {
                if (Volatile.Read(ref _done) == 1) Write();
                return;
            }
            _poll -= Time.unscaledDeltaTime;
            if (_poll > 0f) return;
            _poll = 1f;
            string trig = Path.Combine(Application.persistentDataPath, Trigger);
            if (!File.Exists(trig)) return;
            File.Delete(trig);
            _rate = AudioSettings.outputSampleRate;
            _buf = new float[(int)(Seconds * _rate) * 2];
            _pos = 0;
            _channels = 0;
            Volatile.Write(ref _done, 0);
            _recording = true;
            Debug.Log($"[AudioTap] recording {Seconds:0} s at {_rate} Hz");
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (!_recording || Volatile.Read(ref _done) == 1) return;
            _channels = channels;
            int n = Math.Min(data.Length, _buf.Length - _pos);
            Array.Copy(data, 0, _buf, _pos, n);
            _pos += n;
            if (_pos >= _buf.Length || channels != 2) Volatile.Write(ref _done, 1);
        }

        void Write()
        {
            _recording = false;
            string path = Path.Combine(Application.persistentDataPath, $"audiotap-{DateTime.Now:HHmmss}.wav");
            int ch = _channels > 0 ? _channels : 2, frames = _pos / ch;
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = frames * ch * 2;
                w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' }); w.Write(36 + bytes);
                w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                w.Write(16); w.Write((short)1); w.Write((short)ch); w.Write(_rate); w.Write(_rate * ch * 2); w.Write((short)(ch * 2)); w.Write((short)16);
                w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' }); w.Write(bytes);
                for (int i = 0; i < frames * ch; i++) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(_buf[i] * 32767f), -32768, 32767));
            }
            _buf = null;
            Debug.Log($"[AudioTap] wrote {path} ({frames / (float)_rate:0.0} s, {ch} ch)");
        }
    }
}
