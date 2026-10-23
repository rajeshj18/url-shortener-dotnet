using System;
using Npgsql;

namespace UrlShortener.Infrastructure.Persistence.Links
{
    /// <summary>
    /// The <c>LinksPrimary</c> data source: the writer endpoint, with <c>Target Session Attributes=read-write</c> in
    /// production (LLD §6.7). A named wrapper type, so DI can't mix it up with the replicas.
    /// </summary>
    public sealed class LinksPrimaryDataSource : IDisposable
    {
        /// <summary>Creates the wrapper and takes ownership of the data source.</summary>
        /// <param name="dataSource">The Npgsql data source.</param>
        public LinksPrimaryDataSource(NpgsqlDataSource dataSource)
        {
            DataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        }

        /// <summary>The underlying data source.</summary>
        public NpgsqlDataSource DataSource { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            DataSource.Dispose();
        }
    }

    /// <summary>
    /// The <c>LinksReplicas</c> data source: the reader endpoint or a host list, with
    /// <c>Load Balance Hosts=true</c> and <c>Target Session Attributes=prefer-standby</c> in production (LLD §6.7).
    /// </summary>
    public sealed class LinksReplicaDataSource : IDisposable
    {
        /// <summary>Creates the wrapper and takes ownership of the data source.</summary>
        /// <param name="dataSource">The Npgsql data source (multi-host if several hosts are configured).</param>
        public LinksReplicaDataSource(NpgsqlDataSource dataSource)
        {
            DataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        }

        /// <summary>The underlying data source.</summary>
        public NpgsqlDataSource DataSource { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            DataSource.Dispose();
        }
    }
}
