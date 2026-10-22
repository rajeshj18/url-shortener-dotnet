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

        private static List<string> ProjectReferencesOf(string project)
        {
            string csproj = Path.Combine(RepositoryRoot(), "src", project, project + ".csproj");
            XDocument document = XDocument.Load(csproj);
            return document.Descendants("ProjectReference")
                .Select(reference => Path.GetFileNameWithoutExtension((string?)reference.Attribute("Include") ?? string.Empty))
                .ToList();
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
