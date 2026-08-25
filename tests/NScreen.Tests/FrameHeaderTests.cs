using System.Buffers.Binary;

namespace NScreen.Tests;

/// <summary>
/// The frame header, built here exactly as docs/PROTOCOL.md describes it and read back at the
/// offsets FrameReceiver reads. The server writes this header from the same constants, so a change
/// to any of them shows up as a shifted field rather than as a corrupt picture.
/// </summary>
[TestClass]
public sealed class FrameHeaderTests
{
    [TestMethod]
    public void A_two_rect_header_matches_the_documented_byte_offsets()
    {
        RECT[] rects =
        [
            new() { Left = 0, Top = 0, Right = 2, Bottom = 1 },
            new() { Left = 4, Top = 4, Right = 6, Bottom = 5 },
        ];

        var header = new byte[Protocol.MaxHeaderBytes];
        var length = WriteHeader(header, Protocol.FlagCompressed, rects, wireBytes: 9, rawBytes: 16);

        Assert.AreEqual(1 + 2 + (2 * Protocol.RectBytes) + 8, length);
        CollectionAssert.AreEqual(
            new byte[]
            {
                0x80,                                               // flags: compressed
                0x02, 0x00,                                         // rect count
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,     // rect 0: left, top
                0x02, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,     // rect 0: right, bottom
                0x04, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00,     // rect 1: left, top
                0x06, 0x00, 0x00, 0x00, 0x05, 0x00, 0x00, 0x00,     // rect 1: right, bottom
                0x09, 0x00, 0x00, 0x00,                             // wire bytes
                0x10, 0x00, 0x00, 0x00,                             // raw bytes
            },
            header[..length]);
    }

    [TestMethod]
    public void A_header_read_back_yields_the_rectangles_that_were_written()
    {
        RECT[] written =
        [
            new() { Left = 100, Top = 50, Right = 300, Bottom = 150 },
            new() { Left = 0, Top = 0, Right = 1920, Bottom = 1080 },
        ];

        var header = new byte[Protocol.MaxHeaderBytes];
        WriteHeader(header, flags: 0, written, wireBytes: 42, rawBytes: 42);

        Assert.AreEqual(0, header[0] & Protocol.FlagCompressed);
        Assert.AreEqual((ushort)written.Length, BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(1)));

        for (var i = 0; i < written.Length; i++)
        {
            var offset = 3 + (i * Protocol.RectBytes);
            Assert.AreEqual(written[i].Left, BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(offset)));
            Assert.AreEqual(written[i].Top, BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(offset + 4)));
            Assert.AreEqual(written[i].Right, BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(offset + 8)));
            Assert.AreEqual(written[i].Bottom, BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(offset + 12)));
        }
    }

    [TestMethod]
    public void The_largest_rectangle_list_still_fits_MaxHeaderBytes()
    {
        var rects = new RECT[Protocol.MaxRects];
        var header = new byte[Protocol.MaxHeaderBytes];

        Assert.AreEqual(Protocol.MaxHeaderBytes, WriteHeader(header, flags: 0, rects, wireBytes: 0, rawBytes: 0));
    }

    /// <summary>Mirrors ScreenServer.WriteFrame. Returns the number of header bytes written.</summary>
    private static int WriteHeader(byte[] header, byte flags, RECT[] rects, int wireBytes, int rawBytes)
    {
        header[0] = flags;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(1), (ushort)rects.Length);

        var position = 3;
        foreach (var rect in rects)
        {
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(position), rect.Left);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(position + 4), rect.Top);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(position + 8), rect.Right);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(position + 12), rect.Bottom);
            position += Protocol.RectBytes;
        }

        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(position), wireBytes);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(position + 4), rawBytes);
        return position + 8;
    }
}
