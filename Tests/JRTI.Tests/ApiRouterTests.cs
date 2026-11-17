using System.Linq;
using JustReadTheInstructions;
using Xunit;

namespace JRTI.Tests
{
    public class ApiRouterTests
    {
        private static readonly ApiRoute[] Routes =
        {
            Route(new ApiOperation("list", "GET", "/cameras", "")),
            Route(new ApiOperation("read", "GET", "/layouts/{layout}", "")),
            Route(new ApiOperation("save", "PUT", "/layouts/{layout}", "") { AlsoAccepts = new[] { "POST" } }),
            Route(new ApiOperation("record", "POST", "/camera/{id}/recording/{action}", "")),
        };

        private static ApiRoute Route(ApiOperation operation) => new ApiRoute(operation, (ctx, v) => { });

        [Fact]
        public void MatchesLiteralRoute()
        {
            var outcome = ApiRouter.Resolve(Routes, "GET", "/cameras", out var route, out _, out _);
            Assert.Equal(ApiRouter.Outcome.Matched, outcome);
            Assert.Equal("list", route.Operation.Id);
        }

        [Fact]
        public void CapturesParameters()
        {
            ApiRouter.Resolve(Routes, "POST", "/camera/12/recording/start", out _, out var values, out _);
            Assert.Equal("12", values["id"]);
            Assert.Equal("start", values["action"]);
        }

        [Fact]
        public void PicksRouteByMethod()
        {
            ApiRouter.Resolve(Routes, "PUT", "/layouts/Launch", out var route, out var values, out _);
            Assert.Equal("save", route.Operation.Id);
            Assert.Equal("Launch", values["layout"]);
        }

        [Fact]
        public void HeadIsAllowedWhereGetIs()
            => Assert.Equal(ApiRouter.Outcome.Matched, ApiRouter.Resolve(Routes, "HEAD", "/cameras", out _, out _, out _));

        [Fact]
        public void WrongMethodListsAllowedMethods()
        {
            var outcome = ApiRouter.Resolve(Routes, "DELETE", "/layouts/Launch", out _, out _, out var allowed);
            Assert.Equal(ApiRouter.Outcome.WrongMethod, outcome);
            Assert.Equal(new[] { "GET", "PUT", "POST" }, allowed.ToArray());
        }

        [Theory]
        [InlineData("/nope")]
        [InlineData("/camera/12/recording")]
        [InlineData("/layouts/a/b")]
        [InlineData("")]
        public void UnknownPathsAreNotFound(string path)
            => Assert.Equal(ApiRouter.Outcome.NotFound, ApiRouter.Resolve(Routes, "GET", path, out _, out _, out _));

        [Theory]
        [InlineData("/api/v1/cameras", true, "/cameras")]
        [InlineData("/api/v1", true, "/")]
        [InlineData("/api/v10/cameras", false, null)]
        [InlineData("/cameras", false, null)]
        public void StripsVersionPrefix(string path, bool versioned, string rest)
        {
            Assert.Equal(versioned, ApiRouter.TryStripVersion(path, out var stripped));
            Assert.Equal(rest, stripped);
        }
    }
}
