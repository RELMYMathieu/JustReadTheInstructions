using System;
using System.Linq;
using System.Security.Cryptography;

namespace JustReadTheInstructions
{
    public static class ApiAccess
    {
        private const string KeyAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        public const int KeyLength = 10;
        public const string BearerPrefix = "Bearer ";
        private static readonly string[] PrivateNameSuffixes = { ".ts.net", ".local", ".lan", ".home", ".home.arpa", ".internal", ".localdomain" };
        private static readonly char[] HostListSeparators = { ',', ' ' };
        private static readonly string[] ProxyHeaders = { "X-Forwarded-For", "X-Forwarded-Host", "X-Real-IP", "Forwarded" };

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
            if (provided == null || !IsValidKey(expected)) return false;
            provided = provided.ToUpperInvariant();
            int difference = provided.Length ^ expected.Length;
            for (int i = 0; i < expected.Length; i++)
                difference |= (i < provided.Length ? provided[i] : 0) ^ expected[i];
            return difference == 0;
        }

        public static bool IsLocalhostOrigin(string origin)
            => Uri.TryCreate(origin, UriKind.Absolute, out var uri) && IsLoopback(uri);

        public static bool IsLoopbackHost(string hostHeader)
            => TryParseHost(hostHeader, out var uri) && IsLoopback(uri);

        public static bool IsAllowedHost(string hostHeader, string allowedHosts)
        {
            if (string.IsNullOrEmpty(hostHeader)) return true;
            if (!TryParseHost(hostHeader, out var uri)) return false;
            if (uri.HostNameType != UriHostNameType.Dns || IsLoopback(uri) || IsPrivateName(uri.Host)) return true;

            return (allowedHosts ?? "").Split(HostListSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Any(name => string.Equals(name, uri.Host, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsPrivateName(string host)
            => host.IndexOf('.') < 0 || PrivateNameSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

        public static bool IsDirectFromThisComputer(bool connectionIsLocal, string hostHeader, Func<string, string> header)
            => connectionIsLocal && IsLoopbackHost(hostHeader) && ProxyHeaders.All(name => header(name) == null);

        private static bool TryParseHost(string hostHeader, out Uri uri)
            => Uri.TryCreate("http://" + hostHeader, UriKind.Absolute, out uri)
               && uri.AbsolutePath == "/" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;

        private static bool IsLoopback(Uri uri)
            => uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
               || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);

        public static bool IsSameOrigin(string origin, string requestHost)
            => Uri.TryCreate(origin, UriKind.Absolute, out var uri)
               && string.Equals(uri.Authority, requestHost, StringComparison.OrdinalIgnoreCase);

        public static bool IsBlockedCrossSite(string secFetchSite, string origin, string requestHost)
        {
            if (origin == null || IsSameOrigin(origin, requestHost) || IsLocalhostOrigin(origin))
                return secFetchSite == "cross-site" && origin == null;
            return true;
        }

        public enum WriteVerdict { Allowed, OtherWebsite, NeedsKey }

        public static WriteVerdict CheckWrite(bool requireKey, string expectedKey, bool connectionIsLocal,
                                              string hostHeader, string keyQuery, Func<string, string> header)
        {
            if (IsBlockedCrossSite(header("Sec-Fetch-Site"), header("Origin"), hostHeader)) return WriteVerdict.OtherWebsite;
            if (!requireKey || IsDirectFromThisComputer(connectionIsLocal, hostHeader, header)) return WriteVerdict.Allowed;
            return KeyMatches(ProvidedKey(header("Authorization"), keyQuery), expectedKey) ? WriteVerdict.Allowed : WriteVerdict.NeedsKey;
        }
    }
}
