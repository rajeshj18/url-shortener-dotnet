using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NetArchTest.Rules;
using Xunit;

namespace UrlShortener.ArchitectureTests
{
    /// <summary>
    /// ADR-0009 dependency rules, checked at <b>type level</b> with NetArchTest. As the layers fill up
    /// (TSK-05 onwards), these tests catch forbidden usages inside the compiled code.
    /// </summary>
    public sealed class TypeDependencyTests
    {
        private static readonly string[] ServiceNamespaces = { "UrlShortener.Api", "UrlShortener.Redirect", "UrlShortener.AnalyticsWorker" };

        private static readonly string[] Frameworks =
        {
            "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore", "Microsoft.Extensions", "Npgsql", "Confluent.Kafka",
        };

        private static readonly string[] InnerLayersForDomain = { "UrlShortener.Application", "UrlShortener.Infrastructure" };

        private static readonly string[] ForbiddenForApplication =
        {
            "UrlShortener.Infrastructure", "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore", "Npgsql", "Confluent.Kafka",
        };

        [Fact]
        public void Domain_depends_on_no_other_layer_or_framework()
        {
            string[] forbidden = InnerLayersForDomain.Concat(ServiceNamespaces).Concat(Frameworks).ToArray();

            AssertNoDependency(Load("UrlShortener.Domain"), forbidden);
        }

        [Fact]
        public void Application_depends_on_no_infrastructure_service_or_persistence_framework()
        {
            string[] forbidden = ForbiddenForApplication.Concat(ServiceNamespaces).ToArray();

            AssertNoDependency(Load("UrlShortener.Application"), forbidden);
        }

        [Fact]
        public void Infrastructure_depends_on_no_service()
        {
            AssertNoDependency(Load("UrlShortener.Infrastructure"), ServiceNamespaces);
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
        public void Controllers_depend_on_no_infrastructure_type(string service)
        {
            TestResult result = Types.InAssembly(Load(service))
                .That().Inherit(typeof(ControllerBase))
                .ShouldNot().HaveDependencyOn("UrlShortener.Infrastructure")
                .GetResult();

            result.IsSuccessful.Should().BeTrue("controllers may only use Application interfaces (ADR-0009); offending: {0}",
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
