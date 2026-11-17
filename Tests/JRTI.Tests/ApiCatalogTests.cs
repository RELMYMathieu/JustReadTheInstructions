using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using JustReadTheInstructions;
using Xunit;

namespace JRTI.Tests
{
    public class ApiCatalogTests
    {
        private const string UpdateVariable = "JRTI_UPDATE_OPENAPI";

        [Fact]
        public void OperationIdsAreUnique()
            => Assert.Empty(ApiCatalog.Operations.GroupBy(op => op.Id).Where(g => g.Count() > 1).Select(g => g.Key));

        [Fact]
        public void NoTwoOperationsShareAMethodAndPath()
        {
            var pairs = ApiCatalog.Operations.SelectMany(op => op.Methods.Select(m => $"{m} {op.Template}"));
            Assert.Empty(pairs.GroupBy(p => p).Where(g => g.Count() > 1).Select(g => g.Key));
        }

        [Fact]
        public void EveryPathParameterIsDescribed()
        {
            foreach (var op in ApiCatalog.Operations)
                Assert.Equal(op.PathParameterNames.OrderBy(n => n), op.Path.Select(p => p.Name).OrderBy(n => n));
        }

        [Fact]
        public void EveryOperationHasAResponse()
            => Assert.All(ApiCatalog.Operations, op => Assert.NotNull(op.Response));

        [Fact]
        public void PlainReadsNeverWrite()
            => Assert.All(ApiCatalog.Operations.Where(op => op.Method == "GET"), op => Assert.False(op.Writes, op.Id));

        [Fact]
        public void EveryReferencedComponentExists()
        {
            var document = OpenApiDocument.Build(ApiCatalog.Operations, ApiCatalog.Components);
            var references = Regex.Matches(document, "#/components/schemas/(\\w+)")
                .Cast<Match>().Select(m => m.Groups[1].Value).Distinct();
            Assert.All(references, name => Assert.True(ApiCatalog.Components.ContainsKey(name), name));
        }

        [Fact]
        public void PublishedOpenApiDocumentIsCurrent()
        {
            var path = Path.Combine(RepositoryRoot(), "docs", "openapi.json");
            var expected = OpenApiDocument.Build(ApiCatalog.Operations, ApiCatalog.Components);

            if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
                File.WriteAllText(path, expected);

            Assert.True(File.Exists(path), $"Run the tests once with {UpdateVariable}=1 to write {path}");
            Assert.True(expected == File.ReadAllText(path).Replace("\r\n", "\n"),
                $"docs/openapi.json is out of date: run the tests with {UpdateVariable}=1 and commit it");
        }

        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "mkdocs.yml")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("mkdocs.yml not found above the test folder");
        }
    }
}
