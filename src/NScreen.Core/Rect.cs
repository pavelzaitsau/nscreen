using System.Runtime.InteropServices;

namespace NScreen;

/// <summary>
/// Win32 RECT: SDK name, field order and exclusive right/bottom. DXGI writes it and the wire
/// carries it unchanged, so both sides need this exact layout.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public readonly int Width => Right - Left;

    public readonly int Height => Bottom - Top;
}
