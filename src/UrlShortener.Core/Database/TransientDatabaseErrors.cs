using System;
using Npgsql;

namespace UrlShortener.Core.Database
{
    /// <summary>
    /// Which database failures are <b>transient</b> (LLD §6.7): connection failures, timeouts, "in recovery" or shutting
    /// down, serialization failures and deadlocks. Only these count towards a breaker and may be retried.
    /// </summary>
    public static class TransientDatabaseErrors
    {
        private static readonly string[] TransientSqlStates =
        {
            "08000", "08001", "08003", "08004", "08006", // connection exceptions
            "40001",                                     // serialization_failure
            "40P01",                                     // deadlock_detected
            "53300",                                     // too_many_connections
            "57014",                                     // query_canceled (statement_timeout)
            "57P01", "57P02", "57P03",                   // admin/crash shutdown, cannot connect now (in recovery)
        };

        /// <summary>Checks the exception and its inner exceptions.</summary>
        /// <param name="exception">The failure.</param>
        /// <returns><c>true</c> if a later attempt may succeed.</returns>
        public static bool IsTransient(Exception? exception)
        {
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                switch (current)
                {
                    case PostgresException postgres:
                        return Array.IndexOf(TransientSqlStates, postgres.SqlState) >= 0;
                    case NpgsqlException npgsql when npgsql.IsTransient:
                        return true;
                    case TimeoutException:
                        return true;
                }
            }

            return false;
        }
    }
}
