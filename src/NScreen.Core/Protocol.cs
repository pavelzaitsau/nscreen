using System.Buffers.Binary;
using System.IO.Compression;

namespace NScreen;

/// <summary>
/// The wire layout, and the only place it is written down in code. Byte offsets are in
/// docs/PROTOCOL.md. Every frame has the same shape, so a whole-screen update is one rectangle
/// rather than a second message type.
/// </summary>
public static class Protocol
{
    /// <summary>"NSC1" little-endian. The framing version lives here - bump it on any change.</summary>
    public const int Magic = 0x3143534E;

    public const int DefaultPort = 7000;
    public const int HelloBytes = 12;

    /// <summary>Set in the flags byte when this particular payload went out Brotli-compressed.</summary>
    public const byte FlagCompressed = 0x80;

    /// <summary>Beyond this many rectangles the server stops enumerating and sends one covering the screen.</summary>
    public const int MaxRects = 384;

    /// <summary>Bytes a rectangle takes on the wire: left, top, right, bottom.</summary>
    public const int RectBytes = 16;

    /// <summary>flags + rect count + rects + wire length + raw length.</summary>
    public const int MaxHeaderBytes = 1 + 2 + (MaxRects * RectBytes) + 8;

    /// <summary>Quality 1 - fast enough to stay out of the way, still ~20x on desktop pixels.</summary>
    private const int BrotliQuality = 1;
    private const int BrotliWindow = 22;

    public static void WriteHello(Span<byte> destination, int width, int height)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, Magic);
        BinaryPrimitives.WriteInt32LittleEndian(destination[4..], width);
        BinaryPrimitives.WriteInt32LittleEndian(destination[8..], height);
    }

    public static (int Width, int Height) ReadHello(ReadOnlySpan<byte> source)
    {
        if (BinaryPrimitives.ReadInt32LittleEndian(source) != Magic)
        {
            throw new InvalidDataException("Not an nscreen server (bad magic).");
        }

        return (
            BinaryPrimitives.ReadInt32LittleEndian(source[4..]),
            BinaryPrimitives.ReadInt32LittleEndian(source[8..]));
    }

    /// <summary>Returns the compressed length, or -1 when it did not fit in the destination.</summary>
    public static int Compress(ReadOnlySpan<byte> source, Span<byte> destination)
        => BrotliEncoder.TryCompress(source, destination, out var written, BrotliQuality, BrotliWindow)
            ? written
            : -1;

    public static int Decompress(ReadOnlySpan<byte> source, Span<byte> destination)
        => BrotliDecoder.TryDecompress(source, destination, out var written)
            ? written
            : throw new InvalidDataException("Brotli payload did not decompress.");

    public static int MaxCompressedBytes(int rawBytes) => rawBytes + (rawBytes / 4) + 1024;
}
