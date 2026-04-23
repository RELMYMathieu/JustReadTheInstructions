using System.Collections.Generic;
using System.Linq;

namespace JustReadTheInstructions
{
    public static class JRTICameraRuntime
    {
        private static readonly Dictionary<uint, int> _runtimeIds = new Dictionary<uint, int>();

        public static int ResolveId(uint persistentId, int preferredId)
        {
            if (_runtimeIds.TryGetValue(persistentId, out int existing)) return existing;
            int id = preferredId > 0 && !IsIdTaken(persistentId, preferredId)
                ? preferredId
                : NextAvailableId(persistentId);
            _runtimeIds[persistentId] = id;
            return id;
        }

        private static bool IsIdTaken(uint excludePersistentId, int candidateId)
            => _runtimeIds.Any(kvp => kvp.Key != excludePersistentId && kvp.Value == candidateId);

        private static int NextAvailableId(uint excludePersistentId)
        {
            var taken = new HashSet<int>(_runtimeIds
                .Where(kvp => kvp.Key != excludePersistentId)
                .Select(kvp => kvp.Value));
            int next = 1;
            while (taken.Contains(next)) next++;
            return next;
        }
    }
}
