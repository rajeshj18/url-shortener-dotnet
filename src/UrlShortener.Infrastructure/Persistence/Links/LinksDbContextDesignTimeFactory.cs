using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UrlShortener.Infrastructure.Persistence.Links
{
    /// <summary>
    /// Used only by the EF Core tools: <c>dotnet ef migrations add</c> and <c>migrations bundle</c>. Adding a
    /// migration doesn't connect anywhere. The bundle receives the real (migrator) connection string through
    /// <c>--connection</c> at run time, so no connection string or secret is stored here (ADR-0011).
    /// </summary>
    public sealed class LinksDbContextDesignTimeFactory : IDesignTimeDbContextFactory<LinksDbContext>
    {
        /// <summary>Environment variable the tools can use instead of the placeholder.</summary>
        public const string ConnectionVariable = "LINKS_MIGRATOR_CONNECTION";

        /// <inheritdoc />
        public LinksDbContext CreateDbContext(string[] args)
        {
            string connection = Environment.GetEnvironmentVariable(ConnectionVariable) ?? "Host=localhost;Database=links";
            DbContextOptions<LinksDbContext> options = new DbContextOptionsBuilder<LinksDbContext>()
                .UseNpgsql(connection)
                .Options;
            return new LinksDbContext(options);
        }
    }
}
