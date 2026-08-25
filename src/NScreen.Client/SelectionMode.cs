namespace NScreen.Client;

/// <summary>What a released selection puts on the clipboard.</summary>
internal enum SelectionMode
{
    /// <summary>The selected pixels, at the remote screen's own resolution.</summary>
    Image = 0,

    /// <summary>The English text those pixels contain, read by the platform's own OCR.</summary>
    Text = 1,
}
