using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace UrlShortener.IntegrationTests.Infrastructure
{
    /// <summary>
    /// A TCP server that accepts connections and never answers: a database that hangs. With no interfaces to fake, this
    /// is how the timeout tests make a real call wait until the request budget cancels it.
    /// </summary>
    public sealed class BlackHole : IDisposable
    {
        private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly List<TcpClient> _held = new List<TcpClient>();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();

        public BlackHole()
        {
            _listener.Start();
            _ = Task.Run(AcceptAsync);
        }

        /// <summary>A connection string that reaches the black hole, with timeouts longer than the tests' request budgets.</summary>
        public string ConnectionString => string.Create(
            CultureInfo.InvariantCulture, $"Host=127.0.0.1;Port={((IPEndPoint)_listener.LocalEndpoint).Port};Database=links;Username=nobody;Password=x;Timeout=10;Command Timeout=10");

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Dispose();
            lock (_held)
            {
                _held.ForEach(client => client.Dispose());
            }

            _stop.Dispose();
        }

        private async Task AcceptAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    lock (_held)
                    {
                        _held.Add(client);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // stopped
            }
            catch (SocketException)
            {
                // stopped
            }
        }
    }
}
