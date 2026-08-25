using System.Runtime.InteropServices;

namespace NScreen.Tests;

/// <summary>
/// <see cref="RECT"/> is written by DXGI and copied onto the wire unchanged, so its size and field
/// order are part of the protocol rather than an implementation detail.
/// </summary>
[TestClass]
public sealed class RectTests
{
    [TestMethod]
    public void Rect_is_the_size_the_wire_reserves_for_it()
        => Assert.AreEqual(Protocol.RectBytes, Marshal.SizeOf<RECT>());

    [TestMethod]
    public void Fields_sit_in_left_top_right_bottom_order()
    {
        var rect = new RECT { Left = 1, Top = 2, Right = 3, Bottom = 4 };

        var bytes = MemoryMarshal.AsBytes(new ReadOnlySpan<RECT>(in rect)).ToArray();

        CollectionAssert.AreEqual(
            new byte[] { 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0, 4, 0, 0, 0 },
            bytes);
    }

    [TestMethod]
    [DataRow(0, 0, 1920, 1080, 1920, 1080)]
    [DataRow(100, 50, 200, 150, 100, 100)]
    [DataRow(10, 10, 10, 10, 0, 0)]
    public void Right_and_bottom_are_exclusive(
        int left, int top, int right, int bottom, int width, int height)
    {
        var rect = new RECT { Left = left, Top = top, Right = right, Bottom = bottom };

        Assert.AreEqual(width, rect.Width);
        Assert.AreEqual(height, rect.Height);
    }
}
