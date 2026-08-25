using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NScreen.Client;

/// <summary>
/// Finds nscreen servers by broadcasting a UDP probe and collecting the replies.
/// </summary>
internal static class DiscoveryProbe
{
    /// <summary>Probes sent per attempt. UDP has no retransmission, so send more than one.</summary>
    private const int ProbesPerAttempt = 2;

    /// <summary>
    /// How long to collect replies. A LAN server answers in milliseconds; the rest of the window
    /// catches a dropped datagram.
    /// </summary>
    private static readonly TimeSpan ListenWindow = TimeSpan.FromMilliseconds(600);

    /// <summary>
    /// One entry per server that answered, in reply order, so the first is the fastest path.
    /// Empty when nothing replied.
    /// </summary>
    public static List<ServerInfo> Find()
    {
        // A multi-homed server answers once per interface, and each of those addresses is
        // reachable by definition. Keying on machine plus port keeps the fastest reply only.
        var found = new List<ServerInfo>();
        var seen = new HashSet<(string Name, int Port)>();

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.EnableBroadcast = true;
        socket.Bind(new IPEndPoint(IPAddress.Any, 0));

        Span<byte> probe = stackalloc byte[8];
        var probeLength = Discovery.WriteProbe(probe);

        // 255.255.255.255 leaves one adapter only on a multi-homed machine, so each interface's
        // own broadcast address is aimed at too. Without it, Wi-Fi plus Ethernet plus Hyper-V fails.
        foreach (var target in BroadcastTargets())
        {
            var endpoint = new IPEndPoint(target, Discovery.Port);
            for (var attempt = 0; attempt < ProbesPerAttempt; attempt++)
            {
                try
                {
                    socket.SendTo(probe[..probeLength], endpoint);
                }
                catch (SocketException)
                {
                    // A down or unroutable interface is not worth reporting; the others still answer.
                }
            }
        }

        var buffer = new byte[Discovery.MaxDatagramBytes];
        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < ListenWindow)
        {
            var remaining = ListenWindow - clock.Elapsed;
            if (remaining <= TimeSpan.Zero || !socket.Poll(remaining, SelectMode.SelectRead))
            {
                break;
            }

            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            int received;
            try
            {
                received = socket.ReceiveFrom(buffer, ref from);
            }
            catch (SocketException)
            {
                break;
            }

            if (from is IPEndPoint sender
                && Discovery.TryReadReply(buffer.AsSpan(0, received), sender.Address, out var server)
                && seen.Add((server.Name, server.TcpPort)))
            {
                found.Add(server);
            }
        }

        return found;
    }

    /// <summary>The global broadcast address plus the directed broadcast of every up IPv4 interface.</summary>
    private static IEnumerable<IPAddress> BroadcastTargets()
    {
        yield return IPAddress.Broadcast;

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up
                || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var ip in nic.GetIPProperties().UnicastAddresses)
            {
                if (ip.Address.AddressFamily != AddressFamily.InterNetwork || ip.IPv4Mask is null)
                {
                    continue;
                }

                var address = ip.Address.GetAddressBytes();
                var mask = ip.IPv4Mask.GetAddressBytes();
                for (var i = 0; i < address.Length; i++)
                {
                    address[i] |= (byte)~mask[i];
                }

                yield return new IPAddress(address);
            }
        }
    }
}
