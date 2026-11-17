// Sunny's voice clips are always imported the same way (Docs/11-first-five/sunny-tour.md 5.4): Decompress On Load,
// Preload Audio Data, Load In Background (the decode runs off the main thread; GuideVoice waits for it), Force To Mono,
// Vorbis quality 0.7, 3D off is irrelevant (GuideVoice reads the samples). A new line
// rendered by Guide/Tools~/render-voice.mjs gets a .meta with these settings; this guard keeps any other way of adding one
// (a copied file, a re-import) from importing it wrongly, for every module's Resources/Voice folder.
using UnityEditor;
using UnityEngine;

namespace MusicRoom.EditorTools
{
    public sealed class GuideVoiceImport : AssetPostprocessor
    {
        public const string Folder = "Assets/MusicRoom/Guide/Resources/Voice/";

        /// <summary>A voice clip of any module (Assets/MusicRoom/&lt;Module&gt;/Resources/Voice/): GuideVoice reads them all.</summary>
        public static bool IsVoiceClip(string assetPath) =>
            assetPath != null && assetPath.StartsWith("Assets/MusicRoom/", System.StringComparison.Ordinal)
            && assetPath.IndexOf("/Resources/Voice/", System.StringComparison.Ordinal) > 0;

        void OnPreprocessAudio()
        {
            if (!IsVoiceClip(assetPath)) return;
            var importer = (AudioImporter)assetImporter;
            AudioImporterSampleSettings s = importer.defaultSampleSettings;
            bool changed = s.loadType != AudioClipLoadType.DecompressOnLoad || s.compressionFormat != AudioCompressionFormat.Vorbis
                || Mathf.Abs(s.quality - 0.7f) > 1e-4f || !s.preloadAudioData || !importer.forceToMono || !importer.loadInBackground;
            if (!changed) return;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.7f;
            s.preloadAudioData = true;
            importer.defaultSampleSettings = s;
            importer.forceToMono = true;
            importer.loadInBackground = true;
        }

        /// <summary>The import settings the guard enforces, for the tests and tools.</summary>
        public static bool Conforms(AudioImporter importer)
        {
            if (importer == null) return false;
            AudioImporterSampleSettings s = importer.defaultSampleSettings;
            return s.loadType == AudioClipLoadType.DecompressOnLoad && s.compressionFormat == AudioCompressionFormat.Vorbis
                && Mathf.Abs(s.quality - 0.7f) <= 1e-4f && s.preloadAudioData && importer.forceToMono && importer.loadInBackground;
        }
    }
}
