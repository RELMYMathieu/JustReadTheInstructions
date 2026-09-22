using System.Collections.Generic;
using System.Linq;

namespace JustReadTheInstructions
{
    public static class JRTICameraRuntime
    {
        private static readonly Dictionary<(uint, int), int> _runtimeIds = new Dictionary<(uint, int), int>();

        public static int ResolveId(uint persistentId, int cameraIndex, int preferredId)
        {
            var key = (persistentId, cameraIndex);
            if (_runtimeIds.TryGetValue(key, out int existing)) return existing;
            int candidateId = preferredId > 0 ? preferredId + cameraIndex : 0;
            int id = candidateId > 0 && !IsIdTaken(key, candidateId)
                ? candidateId
                : NextAvailableId(key);
            _runtimeIds[key] = id;
            return id;
        }

        public static void Reset() => _runtimeIds.Clear();

        public static void RetainOnly(HashSet<(uint, int)> liveCameraKeys)
        {
            var stale = _runtimeIds.Keys.Where(k => !liveCameraKeys.Contains(k)).ToList();
            foreach (var key in stale)
                _runtimeIds.Remove(key);
        }

        private static bool IsIdTaken((uint, int) excludeKey, int candidateId)
            => _runtimeIds.Any(kvp => !kvp.Key.Equals(excludeKey) && kvp.Value == candidateId);

        private static int NextAvailableId((uint, int) excludeKey)
        {
            var taken = new HashSet<int>(_runtimeIds
                .Where(kvp => !kvp.Key.Equals(excludeKey))
                .Select(kvp => kvp.Value));
            int next = 1;
            while (taken.Contains(next)) next++;
            return next;
        }
    }
}
