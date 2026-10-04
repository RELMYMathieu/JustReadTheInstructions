using System;
using System.Collections.Generic;
using System.IO;

namespace JustReadTheInstructions
{
    internal static class GameDataSounds
    {
        private static Dictionary<string, string> _wavPaths;

        public static ClipPcm ReadWav(string clipName)
        {
            if (_wavPaths == null) _wavPaths = IndexWavFiles();
            if (!_wavPaths.TryGetValue(clipName, out var path)) return null;

            try { return ClipPcm.FromWav(File.ReadAllBytes(path)); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private static Dictionary<string, string> IndexWavFiles()
        {
            var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in GameDatabase.Instance.databaseAudioFiles)
            {
                if (!string.Equals(file.fileExtension.TrimStart('.'), "wav", StringComparison.OrdinalIgnoreCase)) continue;
                paths[file.url] = file.fullPath;
                if (!paths.ContainsKey(file.name)) paths[file.name] = file.fullPath;
            }
            return paths;
        }
    }
}
