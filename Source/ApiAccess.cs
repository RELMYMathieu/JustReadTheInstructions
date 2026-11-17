using System;
using System.Security.Cryptography;

namespace JustReadTheInstructions
{
    public static class ApiAccess
    {
        private const string KeyAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        public const int KeyLength = 10;
        public const string BearerPrefix = "Bearer ";

        public static string NewKey()
        {
            var bytes = new byte[KeyLength];
            using (var random = RandomNumberGenerator.Create())
                random.GetBytes(bytes);
            var key = new char[KeyLength];
            for (int i = 0; i < KeyLength; i++)
                key[i] = KeyAlphabet[bytes[i] % KeyAlphabet.Length];
            return new string(key);
        }

        public static bool IsValidKey(string key)
        {
            if (key == null || key.Length != KeyLength) return false;
            foreach (char c in key)
                if (KeyAlphabet.IndexOf(c) < 0) return false;
            return true;
        }

        public static string ProvidedKey(string authorizationHeader, string keyQuery)
        {
            if (authorizationHeader != null && authorizationHeader.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
                return authorizationHeader.Substring(BearerPrefix.Length).Trim();
            return keyQuery?.Trim();
        }

        public static bool KeyMatches(string provided, string expected)
        {
            if (provided == null || expected == null) return false;
            provided = provided.ToUpperInvariant();
            int difference = provided.Length ^ expected.Length;
            for (int i = 0; i < expected.Length; i++)
                difference |= (i < provided.Length ? provided[i] : 0) ^ expected[i];
            return difference == 0;
        }

        public static bool IsLocalhostOrigin(string origin)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
            return uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                   || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSameOrigin(string origin, string requestHost)
            => Uri.TryCreate(origin, UriKind.Absolute, out var uri)
               && string.Equals(uri.Authority, requestHost, StringComparison.OrdinalIgnoreCase);

        public static bool IsBlockedCrossSite(string secFetchSite, string origin, string requestHost)
        {
            if (origin == null || IsSameOrigin(origin, requestHost) || IsLocalhostOrigin(origin))
                return secFetchSite == "cross-site" && origin == null;
            return true;
        }
    }
}
