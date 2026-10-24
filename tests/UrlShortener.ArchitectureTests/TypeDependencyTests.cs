using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NetArchTest.Rules;
using Xunit;

namespace UrlShortener.ArchitectureTests
{
    /// <summary>
    /// Dependency rules of the simplified structure (TSK-SIMPLE), checked at <b>type level</b> with NetArchTest.
    /// The DDD rule that matters most stays: the domain model is plain C#. Controllers stay thin: they call Core
    /// services and never touch the database or Kafka themselves.
    /// </summary>
    public sealed class TypeDependencyTests
    {
        private static readonly string[] ServiceNamespaces = { "UrlShortener.Api", "UrlShortener.Redirect", "UrlShortener.AnalyticsWorker" };

        private static readonly string[] Frameworks =
        {
            "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore", "Microsoft.Extensions", "Npgsql", "Confluent.Kafka", "Polly",
        };

        private static readonly string[] CoreProject = { "UrlShortener.Core" };

        private static readonly string[] DataAccess = { "Microsoft.EntityFrameworkCore", "Npgsql", "Confluent.Kafka", "Polly" };

        [Fact]
        public void Domain_depends_on_no_other_project_or_framework()
        {
            string[] forbidden = CoreProject.Concat(ServiceNamespaces).Concat(Frameworks).ToArray();

            AssertNoDependency(Load("UrlShortener.Domain"), forbidden);
        }

        [Fact]
        public void Core_depends_on_no_service()
        {
            AssertNoDependency(Load("UrlShortener.Core"), ServiceNamespaces);
        }

        [Theory]
        [InlineData("UrlShortener.Api")]
        [InlineData("UrlShortener.Redirect")]
        [InlineData("UrlShortener.AnalyticsWorker")]
        public void Service_depends_on_no_other_service(string service)
        {
            AssertNoDependency(Load(service), ServiceNamespaces.Where(other => other != service).ToArray());
        }

        [Theory]
        [InlineData("UrlShortener.Api")]
        [InlineData("UrlShortener.Redirect")]
        public void Controllers_use_no_database_or_messaging_type(string service)
        {
            TestResult result = Types.InAssembly(Load(service))
                .That().Inherit(typeof(ControllerBase))
                .ShouldNot().HaveDependencyOnAny(DataAccess)
                .GetResult();

            result.IsSuccessful.Should().BeTrue("controllers only map HTTP to Core services; offending: {0}",
                string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
        }

        private static void AssertNoDependency(Assembly assembly, string[] forbidden)
        {
            TestResult result = Types.InAssembly(assembly)
                .ShouldNot().HaveDependencyOnAny(forbidden)
                .GetResult();

            result.IsSuccessful.Should().BeTrue("{0} must not depend on {1}; offending: {2}",
                assembly.GetName().Name,
                string.Join(", ", forbidden),
                string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
        }

        private static Assembly Load(string name)
        {
            return Assembly.Load(new AssemblyName(name));
        }
    }
}
