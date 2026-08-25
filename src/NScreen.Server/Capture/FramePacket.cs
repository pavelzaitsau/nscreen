namespace NScreen.Server.Capture;

internal enum GrabStatus
{
    /// <summary>No new desktop image within the timeout - the client keeps showing the last frame.</summary>
    Timeout = 0,

    /// <summary>A frame is sitting in the packet.</summary>
    Frame = 1,

    /// <summary>Duplication died (resolution change, UAC, driver reset). Caller must Reset().</summary>
    Lost = 2,
}

/// <summary>
/// One captured frame: the changed rectangles, and their rows packed back-to-back in
/// <see cref="Payload"/>. Refilled in place for the whole session, so it stops allocating once the
/// buffer has grown to the size the screen needs.
/// </summary>
internal sealed class FramePacket
{
    /// <summary>Changed rectangles, valid up to <see cref="RectCount"/>.</summary>
    public RECT[] Rects { get; } = new RECT[Protocol.MaxRects];

    public int RectCount { get; set; }

    /// <summary>Backing buffer; only the first <see cref="PayloadLength"/> bytes are meaningful.</summary>
    public byte[] Payload { get; private set; } = [];

    public int PayloadLength { get; set; }

    public void EnsurePayload(int bytes)
    {
        if (Payload.Length < bytes)
        {
            Payload = new byte[Math.Max(bytes, Payload.Length * 2)];
        }
    }
}
