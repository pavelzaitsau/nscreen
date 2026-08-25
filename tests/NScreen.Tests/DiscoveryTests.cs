using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace NScreen.Tests;

/// <summary>
/// The UDP probe and reply framing. Expected bytes come from docs/PROTOCOL.md.
/// </summary>
[TestClass]
public sealed class DiscoveryTests
{
    private static readonly IPAddress Sender = IPAddress.Parse("192.168.1.17");

    [TestMethod]
    public void Magics_spell_NSCQ_and_NSCR()
    {
        Assert.AreEqual("NSCQ", MagicText(Discovery.ProbeMagic));
        Assert.AreEqual("NSCR", MagicText(Discovery.ReplyMagic));
    }

    [TestMethod]
    public void Port_is_7001_and_separate_from_the_frame_port()
    {
        Assert.AreEqual(7001, Discovery.Port);
        Assert.AreNotEqual(Protocol.DefaultPort, Discovery.Port);
    }

    [TestMethod]
    public void WriteProbe_writes_the_four_magic_bytes()
    {
        var probe = new byte[8];

        Assert.AreEqual(4, Discovery.WriteProbe(probe));
        CollectionAssert.AreEqual(new byte[] { 0x4E, 0x53, 0x43, 0x51 }, probe[..4]);
    }

    [TestMethod]
    public void IsProbe_accepts_a_probe_and_rejects_everything_else()
    {
        var probe = new byte[4];
        Discovery.WriteProbe(probe);

        Assert.IsTrue(Discovery.IsProbe(probe));
        Assert.IsFalse(Discovery.IsProbe(probe.AsSpan(0, 3)));
        Assert.IsFalse(Discovery.IsProbe("NSCR"u8));
    }

    [TestMethod]
    public void WriteReply_lays_out_magic_then_port_then_length_prefixed_name()
    {
        var reply = new byte[Discovery.MaxDatagramBytes];

        var length = Discovery.WriteReply(reply, 7000, "VENUS");

        Assert.AreEqual(14, length);
        CollectionAssert.AreEqual(
            new byte[] { 0x4E, 0x53, 0x43, 0x52, 0x58, 0x1B, 0x00, 0x00, 0x05, 0x56, 0x45, 0x4E, 0x55, 0x53 },
            reply[..length]);
    }

    [TestMethod]
    public void TryReadReply_returns_the_sender_the_port_and_the_name()
    {
        var reply = new byte[Discovery.MaxDatagramBytes];
        var length = Discovery.WriteReply(reply, 7000, "VENUS");

        Assert.IsTrue(Discovery.TryReadReply(reply.AsSpan(0, length), Sender, out var server));
        Assert.AreEqual(new ServerInfo(Sender, 7000, "VENUS"), server);
        Assert.AreEqual("VENUS at 192.168.1.17:7000", server.ToString());
    }

    [TestMethod]
    public void TryReadReply_carries_a_non_ascii_name_through_utf8()
    {
        var reply = new byte[Discovery.MaxDatagramBytes];
        var length = Discovery.WriteReply(reply, 7000, "Ноутбук");

        Assert.IsTrue(Discovery.TryReadReply(reply.AsSpan(0, length), Sender, out var server));
        Assert.AreEqual("Ноутбук", server.Name);
    }

    [TestMethod]
    public void WriteReply_truncates_a_name_that_does_not_fit_one_length_byte()
    {
        var reply = new byte[Discovery.MaxDatagramBytes];

        var length = Discovery.WriteReply(reply, 7000, new string('x', 300));

        Assert.AreEqual(Discovery.MaxDatagramBytes, length);
        Assert.IsTrue(Discovery.TryReadReply(reply.AsSpan(0, length), Sender, out var server));
        Assert.AreEqual(255, server.Name.Length);
    }

    [TestMethod]
    public void TryReadReply_rejects_a_probe()
    {
        var probe = new byte[Discovery.MaxDatagramBytes];
        var length = Discovery.WriteProbe(probe);

        Assert.IsFalse(Discovery.TryReadReply(probe.AsSpan(0, length), Sender, out _));
    }

    [TestMethod]
    public void TryReadReply_rejects_a_datagram_shorter_than_the_header()
    {
        var reply = new byte[Discovery.MaxDatagramBytes];
        Discovery.WriteReply(reply, 7000, "VENUS");

        Assert.IsFalse(Discovery.TryReadReply(reply.AsSpan(0, 8), Sender, out _));
    }

    [TestMethod]
    public void TryReadReply_rejects_a_name_cut_short_by_the_network()
    {
        var reply = new byte[Discovery.MaxDatagramBytes];
        var length = Discovery.WriteReply(reply, 7000, "VENUS");

        Assert.IsFalse(Discovery.TryReadReply(reply.AsSpan(0, length - 1), Sender, out _));
    }

    private static string MagicText(int magic)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, magic);
        return Encoding.ASCII.GetString(bytes);
    }
}
