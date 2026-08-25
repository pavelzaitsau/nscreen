using System.Net;

namespace NScreen;

/// <summary>A server that answered a discovery probe.</summary>
/// <param name="Address">Where the reply came from, so where to open the TCP connection.</param>
/// <param name="TcpPort">Port the server listens on.</param>
/// <param name="Name">Machine name - the only thing that tells two servers apart.</param>
public readonly record struct ServerInfo(IPAddress Address, int TcpPort, string Name)
{
    public override string ToString() => $"{Name} at {Address}:{TcpPort}";
}
