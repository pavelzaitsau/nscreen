using System.Buffers.Binary;
using System.Text;

namespace NScreen.Tests;

/// <summary>
/// The hello, the compression helpers and the constants the frame header is built from.
/// Expected bytes come from docs/PROTOCOL.md, not from the code, so a layout change fails here.
/// </summary>
[TestClass]
public sealed class ProtocolTests
{
    [TestMethod]
    public void Magic_spells_NSC1()
        => Assert.AreEqual("NSC1", MagicText(Protocol.Magic));

    [TestMethod]
    public void Constants_match_the_documented_protocol()
    {
        Assert.AreEqual(7000, Protocol.DefaultPort);
        Assert.AreEqual(12, Protocol.HelloBytes);
        Assert.AreEqual((byte)0x80, Protocol.FlagCompressed);
        Assert.AreEqual(384, Protocol.MaxRects);
        Assert.AreEqual(16, Protocol.RectBytes);
    }

    [TestMethod]
    public void MaxHeaderBytes_covers_flags_count_rects_and_the_two_lengths()
        => Assert.AreEqual(1 + 2 + (Protocol.MaxRects * Protocol.RectBytes) + 8, Protocol.MaxHeaderBytes);

    [TestMethod]
    public void WriteHello_lays_out_magic_then_width_then_height()
    {
        var hello = new byte[Protocol.HelloBytes];
        Protocol.WriteHello(hello, 1920, 1080);

        CollectionAssert.AreEqual(
            new byte[] { 0x4E, 0x53, 0x43, 0x31, 0x80, 0x07, 0x00, 0x00, 0x38, 0x04, 0x00, 0x00 },
            hello);
    }

    [TestMethod]
    [DataRow(640, 360)]
    [DataRow(1444, 915)]
    [DataRow(3840, 2160)]
    public void ReadHello_returns_what_WriteHello_wrote(int width, int height)
    {
        var hello = new byte[Protocol.HelloBytes];
        Protocol.WriteHello(hello, width, height);

        Assert.AreEqual((width, height), Protocol.ReadHello(hello));
    }

    [TestMethod]
    public void ReadHello_rejects_a_stream_that_is_not_an_nscreen_server()
    {
        var notUs = new byte[Protocol.HelloBytes];
        BinaryPrimitives.WriteInt32LittleEndian(notUs, 0x30435353);

        Assert.ThrowsExactly<InvalidDataException>(() => _ = Protocol.ReadHello(notUs));
    }

    [TestMethod]
    public void Compress_then_Decompress_returns_the_original_pixels()
    {
        var raw = DesktopLikePixels(256, 256);
        var wire = new byte[Protocol.MaxCompressedBytes(raw.Length)];

        var wireBytes = Protocol.Compress(raw, wire);
        Assert.IsGreaterThan(0, wireBytes, "Brotli refused a buffer sized by MaxCompressedBytes.");
        Assert.IsLessThan(raw.Length, wireBytes, "Flat desktop pixels came out no smaller than raw.");

        var back = new byte[raw.Length];
        Assert.AreEqual(raw.Length, Protocol.Decompress(wire.AsSpan(0, wireBytes), back));
        CollectionAssert.AreEqual(raw, back);
    }

    [TestMethod]
    public void MaxCompressedBytes_is_enough_for_incompressible_input()
    {
        var noise = new byte[64 * 1024];
        new Random(1).NextBytes(noise);

        Assert.IsGreaterThan(0, Protocol.Compress(noise, new byte[Protocol.MaxCompressedBytes(noise.Length)]));
    }

    [TestMethod]
    public void Compress_reports_minus_one_when_the_destination_is_too_small()
    {
        var noise = new byte[64 * 1024];
        new Random(2).NextBytes(noise);

        Assert.AreEqual(-1, Protocol.Compress(noise, new byte[16]));
    }

    [TestMethod]
    public void Decompress_rejects_a_payload_that_is_not_brotli()
    {
        var junk = "not brotli"u8.ToArray();
        var destination = new byte[64];

        Assert.ThrowsExactly<InvalidDataException>(() => _ = Protocol.Decompress(junk, destination));
    }

    private static string MagicText(int magic)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, magic);
        return Encoding.ASCII.GetString(bytes);
    }

    /// <summary>Long runs of one colour, which is what makes desktop pixels compress at all.</summary>
    private static byte[] DesktopLikePixels(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var row = i / 4 / width;
            var band = (byte)((row % 8) * 32);
            pixels[i] = band;
            pixels[i + 1] = band;
            pixels[i + 2] = band;
            pixels[i + 3] = 0xFF;
        }

        return pixels;
    }
}
