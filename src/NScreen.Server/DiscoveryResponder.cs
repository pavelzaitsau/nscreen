using System.Net;
using System.Net.Sockets;

namespace NScreen.Server;

/// <summary>
/// Answers discovery probes so nobody types an IP address. One UDP socket and one thread parked in
/// ReceiveFrom; it never announces itself, so an idle server costs no CPU.
/// </summary>
internal static class DiscoveryResponder
{
    public static void Start(int tcpPort, CancellationToken shutdown)
        => new Thread(() => Listen(tcpPort, shutdown))
        {
            IsBackground = true,
            Name = "nscreen-discovery",
        }.Start();

    private static void Listen(int tcpPort, CancellationToken shutdown)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            // ReuseAddress so a restart with the socket still in TIME_WAIT does not fail outright.
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(IPAddress.Any, Discovery.Port));

            // Closing the socket is what breaks the blocking ReceiveFrom below.
            using var stop = shutdown.Register(socket.Close);

            var request = new byte[Discovery.MaxDatagramBytes];
            var reply = new byte[Discovery.MaxDatagramBytes];
            var replyLength = Discovery.WriteReply(reply, tcpPort, Environment.MachineName);
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);

            while (!shutdown.IsCancellationRequested)
            {
                var received = socket.ReceiveFrom(request, ref from);
                if (Discovery.IsProbe(request.AsSpan(0, received)))
                {
                    socket.SendTo(reply, 0, replyLength, SocketFlags.None, from);
                }
            }
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            // Either the server is shutting down, or UDP 7001 is taken. Discovery is a convenience, so
            // losing it must never take the screen stream down with it.
            if (!shutdown.IsCancellationRequested)
            {
                Console.WriteLine($"Discovery unavailable ({ex.Message}). Clients must be given the address.");
            }
        }
    }
}
