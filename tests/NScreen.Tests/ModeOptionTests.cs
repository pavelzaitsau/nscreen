using NScreen.Client;

namespace NScreen.Tests;

/// <summary>
/// <c>--mode</c>, the one argument the viewer gained. The keys inside the window change the same
/// setting, so a token that silently falls back to image is a copy the user did not ask for.
/// </summary>
[TestClass]
public sealed class ModeOptionTests
{
    [TestMethod]
    public void Image_and_text_are_the_two_modes()
    {
        Assert.IsTrue(Program.TryParseMode("image", out var image));
        Assert.AreEqual(SelectionMode.Image, image);

        Assert.IsTrue(Program.TryParseMode("text", out var text));
        Assert.AreEqual(SelectionMode.Text, text);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("Image")]
    [DataRow("TEXT")]
    [DataRow("ocr")]
    [DataRow("image ")]
    public void Anything_else_is_rejected(string token)
        => Assert.IsFalse(Program.TryParseMode(token, out _));

    [TestMethod]
    public void A_rejected_token_leaves_the_default_in_place()
    {
        Assert.IsFalse(Program.TryParseMode("ocr", out var mode));
        Assert.AreEqual(SelectionMode.Image, mode);
    }
}
