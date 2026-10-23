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
    /// ADR-0009 dependency rules, checked at <b>project level</b> by reading the <c>.csproj</c> files.
    /// Project references are checked even before any types exist, so a forbidden reference is caught
    /// the moment it is added.
    /// </summary>
    public sealed class ProjectReferenceTests
    {
        private static readonly string[] Services = { "UrlShortener.Api", "UrlShortener.Redirect", "UrlShortener.AnalyticsWorker" };
        private static readonly string[] AllowedForApplication = { "UrlShortener.Domain" };
        private static readonly string[] AllowedForInfrastructure = { "UrlShortener.Application", "UrlShortener.Domain" };

        [Fact]
        public void Domain_references_no_project()
        {
            ProjectReferencesOf("UrlShortener.Domain").Should().BeEmpty();
        }

        [Fact]
        public void Application_references_only_Domain()
        {
            ProjectReferencesOf("UrlShortener.Application").Should().BeEquivalentTo(AllowedForApplication);
        }

        [Fact]
        public void Infrastructure_references_only_Application_and_Domain()
        {
            ProjectReferencesOf("UrlShortener.Infrastructure").Should().BeSubsetOf(AllowedForInfrastructure);
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
        /// Positive check: proves the reference parsing actually works on this OS. Without it, a parsing bug
        /// would make every "never references" rule pass vacuously. That happened on Linux CI in TSK-04.
        /// </summary>
        [Theory]
        [InlineData("UrlShortener.Api")]
        [InlineData("UrlShortener.Redirect")]
        [InlineData("UrlShortener.AnalyticsWorker")]
        public void Service_references_Application_and_Infrastructure(string service)
        {
            ProjectReferencesOf(service).Should().Contain(AllowedForInfrastructure[0]).And.Contain("UrlShortener.Infrastructure");
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
            string csproj = Path.Combine(RepositoryRoot(), "src", project, project + ".csproj");
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
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "UrlShortener.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("Repository root (UrlShortener.sln) not found.");
        }
    }
}
