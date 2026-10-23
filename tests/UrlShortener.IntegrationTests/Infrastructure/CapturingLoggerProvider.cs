using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace UrlShortener.IntegrationTests.Infrastructure
{
    /// <summary>Keeps every formatted log line (and exception text) so tests can check what is never logged.</summary>
    public sealed class CapturingLoggerProvider : ILoggerProvider
    {
        /// <summary>All captured lines.</summary>
        public ConcurrentQueue<string> Lines { get; } = new ConcurrentQueue<string>();

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName)
        {
            return new CapturingLogger(this, categoryName);
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly CapturingLoggerProvider _provider;
            private readonly string _category;

            public CapturingLogger(CapturingLoggerProvider provider, string category)
            {
                _provider = provider;
                _category = category;
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                _provider.Lines.Enqueue(_category + ": " + formatter(state, exception) + " " + exception);
            }
        }
    }
}
