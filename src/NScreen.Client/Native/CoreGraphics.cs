using System.Runtime.InteropServices;

namespace NScreen.Client.Native;

/// <summary>
/// The slice of CoreGraphics that turns a block of BGRA bytes into the <c>CGImage</c> Vision reads.
/// Plain C exports, so these are ordinary P/Invoke rather than Objective-C messages.
/// </summary>
internal static partial class CoreGraphics
{
    /// <summary>
    /// <c>kCGImageAlphaNoneSkipFirst | kCGBitmapByteOrder32Little</c>: bytes in B, G, R, A order,
    /// with the fourth byte ignored. That is the wire format of a frame, unchanged.
    /// </summary>
    internal const uint BgraBitmapInfo = 6 | 8192;

    private const string Framework = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [LibraryImport(Framework)]
    internal static partial IntPtr CGColorSpaceCreateDeviceRGB();

    [LibraryImport(Framework)]
    internal static partial void CGColorSpaceRelease(IntPtr space);

    /// <summary>
    /// Wraps a buffer without copying it, so the buffer has to outlive both the provider and the
    /// image made from it. A null release callback leaves freeing it to the caller.
    /// </summary>
    [LibraryImport(Framework)]
    internal static unsafe partial IntPtr CGDataProviderCreateWithData(
        IntPtr info, void* data, nuint size, IntPtr releaseCallback);

    [LibraryImport(Framework)]
    internal static partial void CGDataProviderRelease(IntPtr provider);

    [LibraryImport(Framework)]
    internal static partial IntPtr CGImageCreate(
        nuint width,
        nuint height,
        nuint bitsPerComponent,
        nuint bitsPerPixel,
        nuint bytesPerRow,
        IntPtr colorSpace,
        uint bitmapInfo,
        IntPtr provider,
        IntPtr decode,
        byte shouldInterpolate,
        nint intent);

    [LibraryImport(Framework)]
    internal static partial void CGImageRelease(IntPtr image);

    /// <summary>
    /// Vision reports a bounding box in this, normalised to 0..1 with the origin at the bottom-left
    /// corner. Flattened from the nested CGPoint and CGSize, which changes nothing about the layout.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CGRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }
}
