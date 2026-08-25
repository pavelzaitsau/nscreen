using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace NScreen;

/// <summary>
/// Discovery framing. Probe: "NSCQ". Reply: "NSCR" | int32 tcpPort | byte nameLength | UTF-8 name.
/// The client asks and the server answers, so an idle server does no periodic work. Geometry stays
/// out of the reply because the hello already carries it.
/// </summary>
public static class Discovery
{
    /// <summary>UDP port for probes and replies. Unrelated to the TCP port frames travel on.</summary>
    public const int Port = 7001;

    /// <summary>"NSCQ" little-endian.</summary>
    public const int ProbeMagic = 0x5143534E;

    /// <summary>"NSCR" little-endian.</summary>
    public const int ReplyMagic = 0x5243534E;

    private const int ReplyHeaderBytes = 9;

    /// <summary>Fixed header plus the longest name a single length byte can describe.</summary>
    public const int MaxDatagramBytes = ReplyHeaderBytes + 255;

    public static int WriteProbe(Span<byte> destination)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, ProbeMagic);
        return 4;
    }

    public static bool IsProbe(ReadOnlySpan<byte> datagram)
        => datagram.Length >= 4 && BinaryPrimitives.ReadInt32LittleEndian(datagram) == ProbeMagic;

    public static int WriteReply(Span<byte> destination, int tcpPort, string name)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, ReplyMagic);
        BinaryPrimitives.WriteInt32LittleEndian(destination[4..], tcpPort);

        // Truncate rather than fail: a machine name too long for one byte is not worth an error.
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var nameLength = Math.Min(nameBytes.Length, 255);
        destination[8] = (byte)nameLength;
        nameBytes.AsSpan(0, nameLength).CopyTo(destination[ReplyHeaderBytes..]);
        return ReplyHeaderBytes + nameLength;
    }

    public static bool TryReadReply(ReadOnlySpan<byte> datagram, IPAddress from, out ServerInfo server)
    {
        server = default;
        if (datagram.Length < ReplyHeaderBytes || BinaryPrimitives.ReadInt32LittleEndian(datagram) != ReplyMagic)
        {
            return false;
        }

        var nameLength = datagram[8];
        if (datagram.Length < ReplyHeaderBytes + nameLength)
        {
            return false;
        }

        server = new ServerInfo(
            from,
            BinaryPrimitives.ReadInt32LittleEndian(datagram[4..]),
            Encoding.UTF8.GetString(datagram.Slice(ReplyHeaderBytes, nameLength)));
        return true;
    }
}
