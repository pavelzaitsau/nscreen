using NScreen.Client;

namespace NScreen.Tests;

/// <summary>
/// The mapping between a drag in the window and the pixels it copies. Every case here is one the
/// viewer meets: a window wider than the remote screen, one taller than it, a drag that ran off the
/// edge, a window resized mid-drag, and a click that was never a selection.
/// </summary>
[TestClass]
public sealed class RegionSelectionTests
{
    private const int Width = 1920;
    private const int Height = 1080;

    [TestMethod]
    public void A_window_of_the_same_shape_has_no_bars()
    {
        var box = RegionSelection.Fit(960, 540, Width, Height);

        Assert.AreEqual(0, box.Left, 0.001);
        Assert.AreEqual(0, box.Top, 0.001);
        Assert.AreEqual(960, box.Width, 0.001);
        Assert.AreEqual(540, box.Height, 0.001);
    }

    [TestMethod]
    public void A_wider_window_puts_the_bars_left_and_right()
    {
        var box = RegionSelection.Fit(1160, 540, Width, Height);

        Assert.AreEqual(100, box.Left, 0.001);
        Assert.AreEqual(0, box.Top, 0.001);
        Assert.AreEqual(960, box.Width, 0.001);
    }

    [TestMethod]
    public void A_taller_window_puts_the_bars_above_and_below()
    {
        var box = RegionSelection.Fit(960, 740, Width, Height);

        Assert.AreEqual(0, box.Left, 0.001);
        Assert.AreEqual(100, box.Top, 0.001);
        Assert.AreEqual(540, box.Height, 0.001);
    }

    [TestMethod]
    public void A_drag_maps_to_the_pixels_it_covered()
    {
        var region = Drag(960, 540, 100, 50, 300, 150);

        Assert.IsNotNull(region);
        Assert.AreEqual(new PixelRegion(200, 100, 400, 200), region.Value);
    }

    [TestMethod]
    public void The_letterbox_offset_comes_out_of_the_coordinates()
    {
        var region = Drag(1160, 540, 100, 0, 1060, 540);

        Assert.IsNotNull(region);
        Assert.AreEqual(new PixelRegion(0, 0, Width, Height), region.Value);
    }

    [TestMethod]
    public void A_drag_that_ran_onto_the_bars_stops_at_the_image()
    {
        var region = Drag(1160, 740, -500, -500, 5000, 5000);

        Assert.IsNotNull(region);
        Assert.AreEqual(new PixelRegion(0, 0, Width, Height), region.Value);
    }

    [TestMethod]
    public void Dragging_backwards_selects_the_same_pixels()
        => Assert.AreEqual(Drag(960, 540, 100, 50, 300, 150), Drag(960, 540, 300, 150, 100, 50));

    /// <summary>
    /// Both corners are held in remote pixels, so a window that changes size between the press and
    /// the release still copies the pixels the rectangle was drawn over.
    /// </summary>
    [TestMethod]
    public void A_window_resized_mid_drag_keeps_the_selection()
    {
        var pressed = RegionSelection.Fit(960, 540, Width, Height);
        var released = RegionSelection.Fit(480, 270, Width, Height);

        var (anchorX, anchorY) = RegionSelection.ToPixels(100, 50, pressed, Width, Height);
        var (cursorX, cursorY) = RegionSelection.ToPixels(150, 75, released, Width, Height);

        var region = RegionSelection.ToRegion(anchorX, anchorY, cursorX, cursorY, Width, Height);

        Assert.IsNotNull(region);
        Assert.AreEqual(new PixelRegion(200, 100, 400, 200), region.Value);
    }

    [TestMethod]
    [DataRow(0.0, 0.0)]
    [DataRow(1.0, 1.0)]
    [DataRow(3.0, 40.0)]
    public void A_drag_shorter_than_the_minimum_is_not_a_selection(double dx, double dy)
        => Assert.IsNull(Drag(1920, 1080, 100, 100, 100 + dx, 100 + dy));

    [TestMethod]
    public void The_highlight_lands_where_the_drag_did()
    {
        var box = RegionSelection.Fit(1160, 540, Width, Height);
        var region = new PixelRegion(200, 100, 400, 200);

        var bounds = RegionSelection.ToBounds(region, box, Width, Height);

        Assert.AreEqual(200, bounds.Left, 0.001);
        Assert.AreEqual(50, bounds.Top, 0.001);
        Assert.AreEqual(200, bounds.Width, 0.001);
        Assert.AreEqual(100, bounds.Height, 0.001);
    }

    [TestMethod]
    public void A_crop_takes_the_rows_the_region_names()
    {
        var crop = RegionSelection.Crop(Stamped(), 4 * 4, new PixelRegion(1, 1, 2, 2));

        // Column, row, zero, and an alpha the crop forces opaque.
        CollectionAssert.AreEqual(
            new byte[]
            {
                1, 1, 0, 255, 2, 1, 0, 255,
                1, 2, 0, 255, 2, 2, 0, 255,
            },
            crop);
    }

    /// <summary>A locked bitmap's rows are padded, so the crop takes a stride, not a width.</summary>
    [TestMethod]
    public void A_crop_follows_a_padded_stride()
    {
        const int Stride = (4 * 4) + 8;
        var padded = new byte[Stride * 3];
        var tight = Stamped();
        for (var y = 0; y < 3; y++)
        {
            tight.AsSpan(y * 4 * 4, 4 * 4).CopyTo(padded.AsSpan(y * Stride));
        }

        var crop = RegionSelection.Crop(padded, Stride, new PixelRegion(1, 1, 2, 2));

        CollectionAssert.AreEqual(
            new byte[]
            {
                1, 1, 0, 255, 2, 1, 0, 255,
                1, 2, 0, 255, 2, 2, 0, 255,
            },
            crop);
    }

    /// <summary>A transparent frame still copies as a picture rather than as nothing.</summary>
    [TestMethod]
    public void A_crop_never_carries_a_zero_alpha()
    {
        var frame = new byte[4 * 3 * 4];

        var crop = RegionSelection.Crop(frame, 4 * 4, new PixelRegion(0, 0, 4, 3));

        for (var i = 3; i < crop.Length; i += 4)
        {
            Assert.AreEqual(255, crop[i]);
        }
    }

    /// <summary>Four pixels across, three down, each stamped with its own column and row.</summary>
    private static byte[] Stamped()
    {
        var frame = new byte[4 * 3 * 4];
        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                frame[((y * 4) + x) * 4] = (byte)x;
                frame[(((y * 4) + x) * 4) + 1] = (byte)y;
            }
        }

        return frame;
    }

    private static PixelRegion? Drag(
        double boundsWidth, double boundsHeight, double x1, double y1, double x2, double y2)
    {
        var box = RegionSelection.Fit(boundsWidth, boundsHeight, Width, Height);
        var (anchorX, anchorY) = RegionSelection.ToPixels(x1, y1, box, Width, Height);
        var (cursorX, cursorY) = RegionSelection.ToPixels(x2, y2, box, Width, Height);

        return RegionSelection.ToRegion(anchorX, anchorY, cursorX, cursorY, Width, Height);
    }
}
