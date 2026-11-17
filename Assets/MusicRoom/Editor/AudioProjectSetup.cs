// Project audio settings for Quest (spec 04 section 3.1): Meta XR Audio as spatializer and ambisonic decoder, DSP 256,
// Stereo, Classic foundation, Rolloff Scale 1.1 (= the inverse-distance rolloff factor 1.1), Doppler 0, Meta voice limit 64,
// a fixed 48 kHz output (with the system rate the headset ran at 24 kHz, and five instruments are 48 kHz only) and no
// output suspension (the stream must not stop under the instruments' scheduled audio).
// Meta's own auto-setup does not run in batchmode and only fills an EMPTY spatializer field, so CI must run this.
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MusicRoom.EditorTools
{
    public static class AudioProjectSetup
    {
        public const string MetaPluginName = "Meta XR Audio";
        /// <summary>The output rate written into ProjectSettings/AudioManager.asset (m_SampleRate).</summary>
        public const int SampleRate = 48000;
        /// <summary>m_EnableOutputSuspension: off.</summary>
        public const int OutputSuspension = 0;

        [MenuItem("Music Room/Audio/Apply Quest Audio Settings")]
        public static void Apply()
        {
            string[] names = AudioSettings.GetSpatializerPluginNames();
            bool available = names.Contains(MetaPluginName);
            Debug.Log($"[AUDIO-SETUP] spatializer plugins: [{string.Join(", ", names)}]");
            if (available) AudioSettings.SetSpatializerPluginName(MetaPluginName);
            else Debug.LogError($"[AUDIO-SETUP] '{MetaPluginName}' is not registered; is com.meta.xr.sdk.audio installed?");

            var manager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset")[0];
            var so = new SerializedObject(manager);
            SetString(so, "m_SpatializerPlugin", MetaPluginName);
            SetString(so, "m_AmbisonicDecoderPlugin", MetaPluginName);
            SetInt(so, "m_DSPBufferSize", 256);            // "Best latency"
            SetInt(so, "m_RequestedDSPBufferSize", 256);   // raw enum value
            SetInt(so, "Default Speaker Mode", (int)AudioSpeakerMode.Stereo);
            SetInt(so, "m_SampleRate", SampleRate);        // 48 kHz always (the system rate was 24 kHz on the headset)
            SetInt(so, "m_EnableOutputSuspension", OutputSuspension);
            SetInt(so, "m_RealVoiceCount", 32);
            SetInt(so, "m_VirtualVoiceCount", 512);
            SetFloat(so, "Rolloff Scale", 1.1f);           // rolloff factor 1.1 (spec 04 section 3.1)
            SetFloat(so, "Doppler Factor", 0f);
            SetInt(so, "m_AudioFoundation", 0);            // Classic (native spatializer plugins)
            SetInt(so, "m_DisableAudio", 0);
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            MetaXRAudioSettings.Instance.voiceLimit = 64;
            EditorUtility.SetDirty(MetaXRAudioSettings.Instance);
            AssetDatabase.SaveAssets();

            var cfg = AudioSettings.GetConfiguration();
            Debug.Log($"[AUDIO-SETUP] spatializer='{AudioSettings.GetSpatializerPluginName()}' ambisonic='{AmbisonicDecoderName()}' " +
                      $"dspBufferSize={cfg.dspBufferSize} sampleRate={cfg.sampleRate} speakerMode={cfg.speakerMode} real={cfg.numRealVoices} virtual={cfg.numVirtualVoices}");
        }

        public static string AmbisonicDecoderName()
        {
            var m = typeof(AudioSettings).GetMethod("GetAmbisonicDecoderPluginName", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            return m != null ? (string)m.Invoke(null, null) : "(no getter)";
        }

        static void SetString(SerializedObject so, string name, string v)
        {
            var p = so.FindProperty(name);
            if (p != null) p.stringValue = v; else Debug.LogWarning($"[AUDIO-SETUP] no property {name}");
        }

        static void SetInt(SerializedObject so, string name, int v)
        {
            var p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning($"[AUDIO-SETUP] no property {name}"); return; }
            if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = v != 0;
            else p.intValue = v; // enum fields take the RAW value through intValue
        }

        static void SetFloat(SerializedObject so, string name, float v)
        {
            var p = so.FindProperty(name);
            if (p != null) p.floatValue = v; else Debug.LogWarning($"[AUDIO-SETUP] no property {name}");
        }
    }
}
