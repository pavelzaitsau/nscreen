using System.Net;
using System.Net.Sockets;
using NScreen.Client;

namespace NScreen.Tests;

/// <summary>
/// Discovery end to end over a real socket: the client's broadcast probe against a responder that
/// answers the way the server does. This is the half of discovery that framing tests cannot reach -
/// binding, broadcasting, and the reply arriving back at the sender.
/// </summary>
[TestClass]
public sealed class DiscoveryProbeTests
{
    /// <summary>Answering port. Nothing ever connects to it, so any free-looking number does.</summary>
    private const int AnsweredTcpPort = 7321;

    /// <summary>
    /// Gets or sets the context MSTest assigns before the test runs. Only its cancellation token
    /// matters here: the cooperative timeout below is what cancels it.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [Timeout(20_000, CooperativeCancellation = true)]
    public void Find_returns_a_server_that_answers_the_probe()
    {
        var name = "nscreen-test-" + Guid.NewGuid().ToString("N")[..8];

        using var responder = Responder.Start(name, TestContext.CancellationToken);
        if (responder is null)
        {
            Assert.Inconclusive(
                $"UDP port {Discovery.Port} is already bound on this machine, so the responder " +
                "cannot answer. Stop any local nscreen-server and run again.");
            return;
        }

        var found = DiscoveryProbe.Find();

        // A real server on the same LAN answers as well, so this looks for ours rather than
        // asserting on the count.
        var ours = found.Find(server => string.Equals(server.Name, name, StringComparison.Ordinal));
        Assert.AreEqual(name, ours.Name, $"The probe found {found.Count} servers, none of them {name}.");
        Assert.AreEqual(AnsweredTcpPort, ours.TcpPort);
        Assert.IsNotNull(ours.Address);
    }

    /// <summary>
    /// A stand-in for the server's DiscoveryResponder: one socket on the discovery port, one thread
    /// answering probes.
    /// </summary>
    private sealed class Responder : IDisposable
    {
        /// <summary>
        /// How long a receive blocks before the loop rechecks <see cref="_stopping"/>. Disposing a
        /// socket while a thread sits in a blocking ReceiveFrom hangs on Unix, so the thread has to
        /// be able to leave the call by itself.
        /// </summary>
        private const int ReceiveTimeoutMs = 200;

        private readonly Socket _socket;
        private readonly Thread _thread;
        private readonly CancellationTokenSource _stopping;

        private Responder(Socket socket, string name, CancellationToken cancellation)
        {
            _socket = socket;
            _socket.ReceiveTimeout = ReceiveTimeoutMs;
            _stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            _thread = new Thread(() => Answer(name))
            {
                IsBackground = true,
                Name = "nscreen-test-responder",
            };
            _thread.Start();
        }

        /// <summary>
        /// Null when something else already holds the discovery port. Cancelling
        /// <paramref name="cancellation"/> - the test's timeout token - stops the answering thread,
        /// so the cooperative timeout above can end the run.
        /// </summary>
        public static Responder? Start(string name, CancellationToken cancellation)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                socket.Bind(new IPEndPoint(IPAddress.Any, Discovery.Port));
                return new Responder(socket, name, cancellation);
            }
            catch (SocketException)
            {
                socket.Dispose();
                return null;
            }
        }

        public void Dispose()
        {
            _stopping.Cancel();
            _thread.Join();
            _stopping.Dispose();
            _socket.Dispose();
        }

        private void Answer(string name)
        {
            var datagram = new byte[Discovery.MaxDatagramBytes];
            var reply = new byte[Discovery.MaxDatagramBytes];

            while (!_stopping.IsCancellationRequested)
            {
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                int received;
                try
                {
                    received = _socket.ReceiveFrom(datagram, ref from);
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                {
                    continue;
                }

                if (!Discovery.IsProbe(datagram.AsSpan(0, received)))
                {
                    continue;
                }

                var length = Discovery.WriteReply(reply, AnsweredTcpPort, name);
                _socket.SendTo(reply, 0, length, SocketFlags.None, from);
            }
        }
    }
}
