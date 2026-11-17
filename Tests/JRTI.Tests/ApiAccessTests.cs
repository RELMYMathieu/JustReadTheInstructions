using JustReadTheInstructions;
using Xunit;

namespace JRTI.Tests
{
    public class ApiAccessTests
    {
        [Fact]
        public void NewKeysAreValidAndDiffer()
        {
            var first = ApiAccess.NewKey();
            Assert.True(ApiAccess.IsValidKey(first));
            Assert.NotEqual(first, ApiAccess.NewKey());
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("ABCDEFGH2", false)]
        [InlineData("ABCDEFGHI2", false)]
        [InlineData("ABCDEFGHJ2", true)]
        public void ValidatesKeys(string key, bool valid) => Assert.Equal(valid, ApiAccess.IsValidKey(key));

        [Theory]
        [InlineData("Bearer ABCDEFGHJ2", null, "ABCDEFGHJ2")]
        [InlineData("bearer ABCDEFGHJ2 ", null, "ABCDEFGHJ2")]
        [InlineData(null, "ABCDEFGHJ2", "ABCDEFGHJ2")]
        [InlineData("Basic xyz", "ABCDEFGHJ2", "ABCDEFGHJ2")]
        [InlineData(null, null, null)]
        public void ReadsKeyFromHeaderThenQuery(string header, string query, string expected)
            => Assert.Equal(expected, ApiAccess.ProvidedKey(header, query));

        [Theory]
        [InlineData("ABCDEFGHJ2", true)]
        [InlineData("abcdefghj2", true)]
        [InlineData("ABCDEFGHJ3", false)]
        [InlineData("ABCDEFGHJ", false)]
        [InlineData("ABCDEFGHJ22", false)]
        [InlineData(null, false)]
        public void ComparesKeys(string provided, bool matches)
            => Assert.Equal(matches, ApiAccess.KeyMatches(provided, "ABCDEFGHJ2"));

        [Theory]
        [InlineData("http://localhost:5173", true)]
        [InlineData("http://127.0.0.1:3000", true)]
        [InlineData("http://[::1]:3000", true)]
        [InlineData("http://app.localhost", true)]
        [InlineData("https://example.com", false)]
        [InlineData("http://localhost.example.com", false)]
        [InlineData("null", false)]
        public void RecognisesLocalhostOrigins(string origin, bool local)
            => Assert.Equal(local, ApiAccess.IsLocalhostOrigin(origin));

        [Theory]
        [InlineData(null, null, false)]
        [InlineData("none", null, false)]
        [InlineData("same-origin", "http://192.168.1.10:8080", false)]
        [InlineData("same-site", "http://localhost:5173", false)]
        [InlineData("cross-site", "https://evil.example", true)]
        [InlineData("cross-site", null, true)]
        [InlineData(null, "https://evil.example", true)]
        [InlineData(null, "null", true)]
        public void BlocksWritesFromOtherSites(string secFetchSite, string origin, bool blocked)
            => Assert.Equal(blocked, ApiAccess.IsBlockedCrossSite(secFetchSite, origin, "192.168.1.10:8080"));
    }
}
