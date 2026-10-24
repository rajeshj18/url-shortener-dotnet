using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace UrlShortener.ArchitectureTests
{
    /// <summary>
    /// Dependency rules of the simplified structure (TSK-SIMPLE), checked at <b>project level</b> by reading the
    /// <c>.csproj</c> files: Domain ← Core ← services, and no service references another.
    /// </summary>
    public sealed class ProjectReferenceTests
    {
        private static readonly string[] Services = { "UrlShortener.Api", "UrlShortener.Redirect", "UrlShortener.AnalyticsWorker" };
        private static readonly string[] AllowedForCore = { "UrlShortener.Domain" };

        [Fact]
        public void Domain_references_no_project()
        {
            ProjectReferencesOf("UrlShortener.Domain").Should().BeEmpty();
        }

        [Fact]
        public void Core_references_only_Domain()
        {
            ProjectReferencesOf("UrlShortener.Core").Should().BeEquivalentTo(AllowedForCore);
        }

        [Theory]
        [InlineData("UrlShortener.Api")]
        [InlineData("UrlShortener.Redirect")]
        [InlineData("UrlShortener.AnalyticsWorker")]
        public void Service_never_references_another_service(string service)
        {
            ProjectReferencesOf(service).Should().NotContain(Services.Where(other => other != service));
        }

        /// <summary>
        /// Positive check: proves the reference parsing works on this OS. Without it, a parsing bug would make every
        /// "never references" rule pass vacuously (it happened on Linux CI in TSK-04).
        /// </summary>
        [Theory]
        [InlineData("UrlShortener.Api")]
        [InlineData("UrlShortener.Redirect")]
        [InlineData("UrlShortener.AnalyticsWorker")]
        public void Service_references_Core(string service)
        {
            ProjectReferencesOf(service).Should().Contain("UrlShortener.Core");
        }

        /// <summary>Regression test for TSK-04: .csproj files use Windows separators, which Linux doesn't treat as separators.</summary>
        [Theory]
        [InlineData(@"..\UrlShortener.Domain\UrlShortener.Domain.csproj")]
        [InlineData("../UrlShortener.Domain/UrlShortener.Domain.csproj")]
        public void ReferenceName_any_path_separator_returns_project_name(string include)
        {
            ArgumentNullException.ThrowIfNull(include);

            ReferenceName(include).Should().Be("UrlShortener.Domain");
        }

        private static List<string> ProjectReferencesOf(string project)
        {
            string csproj = Path.Join(RepositoryRoot(), "src", project, project + ".csproj");
            XDocument document = XDocument.Load(csproj);
            return document.Descendants("ProjectReference")
                .Select(reference => ReferenceName((string?)reference.Attribute("Include") ?? string.Empty))
                .ToList();
        }

        private static string ReferenceName(string include)
        {
            // MSBuild paths use '\'. On Linux that isn't a separator, so normalise before taking the file name.
            return Path.GetFileNameWithoutExtension(include.Replace('\\', '/'));
        }

        private static string RepositoryRoot()
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Join(directory.FullName, "UrlShortener.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("Repository root (UrlShortener.sln) not found.");
        }
    }
}
