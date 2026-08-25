using NScreen.Client;

namespace NScreen.Tests;

/// <summary>
/// Splitting the address a user types. The IPv6 cases are the reason this is not a call to
/// <c>LastIndexOf(':')</c>.
/// </summary>
[TestClass]
public sealed class TargetTests
{
    private const int Fallback = 7000;

    [TestMethod]
    [DataRow("192.168.1.42")]
    [DataRow("venus")]
    [DataRow("venus.local")]
    public void A_bare_host_keeps_the_fallback_port(string token)
    {
        var (host, port) = Target.Split(token, Fallback);

        Assert.AreEqual(token, host);
        Assert.AreEqual(Fallback, port);
    }

    [TestMethod]
    [DataRow("192.168.1.42:7005", "192.168.1.42", 7005)]
    [DataRow("venus:7005", "venus", 7005)]
    [DataRow("venus:1", "venus", 1)]
    public void A_single_colon_separates_the_port(string token, string expectedHost, int expectedPort)
    {
        var (host, port) = Target.Split(token, Fallback);

        Assert.AreEqual(expectedHost, host);
        Assert.AreEqual(expectedPort, port);
    }

    [TestMethod]
    [DataRow("::1")]
    [DataRow("fe80::1")]
    [DataRow("fe80::7000")]
    [DataRow("2001:db8::42")]
    public void A_bare_ipv6_literal_is_never_split(string token)
    {
        var (host, port) = Target.Split(token, Fallback);

        Assert.AreEqual(token, host);
        Assert.AreEqual(Fallback, port);
    }

    [TestMethod]
    [DataRow("[::1]:7005", "::1", 7005)]
    [DataRow("[fe80::1]:1", "fe80::1", 1)]
    public void A_bracketed_ipv6_literal_carries_a_port(string token, string expectedHost, int expectedPort)
    {
        var (host, port) = Target.Split(token, Fallback);

        Assert.AreEqual(expectedHost, host);
        Assert.AreEqual(expectedPort, port);
    }

    [TestMethod]
    [DataRow("[::1]")]
    [DataRow("[fe80::1]")]
    public void Brackets_without_a_port_keep_the_fallback(string token)
    {
        var (host, port) = Target.Split(token, Fallback);

        Assert.AreEqual(token[1..^1], host);
        Assert.AreEqual(Fallback, port);
    }

    [TestMethod]
    public void A_port_outside_the_range_is_returned_for_the_caller_to_reject()
    {
        // Program checks the range once, after every argument has been read.
        var (host, port) = Target.Split("venus:99999", Fallback);

        Assert.AreEqual("venus", host);
        Assert.AreEqual(99999, port);
    }

    [TestMethod]
    [DataRow("venus:")]
    [DataRow("venus:http")]
    public void A_token_that_is_not_host_and_port_passes_through_unchanged(string token)
    {
        var (host, port) = Target.Split(token, Fallback);

        Assert.AreEqual(token, host);
        Assert.AreEqual(Fallback, port);
    }
}
