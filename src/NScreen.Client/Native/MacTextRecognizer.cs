using System.Runtime.InteropServices;

namespace NScreen.Client.Native;

/// <summary>
/// Reads English text out of a block of BGRA pixels with Vision, the OCR engine macOS already
/// carries. Nothing is bundled and nothing is downloaded: the framework is loaded from the system,
/// its classes are reached through the Objective-C runtime, and the whole call is synchronous.
/// <para>
/// Every entry point here is macOS-only. <see cref="RegionCopier"/> is what keeps Windows out.
/// </para>
/// </summary>
internal static class MacTextRecognizer
{
    /// <summary>VNRequestTextRecognitionLevelAccurate. Fast misreads the small type on a shared screen.</summary>
    private const nint AccurateLevel = 0;

    private const string Foundation = "/System/Library/Frameworks/Foundation.framework/Foundation";
    private const string VisionFramework = "/System/Library/Frameworks/Vision.framework/Vision";

    /// <summary>
    /// The frameworks, loaded once, and the class and selector handles they define, interned once.
    /// Every <c>dlopen</c> raises a reference count and every name lookup marshals a string, and
    /// none of that changes between calls. Null where the frameworks are not there to load, which
    /// is every platform that is not macOS - and because the lookups live behind this, a Windows
    /// client never calls into <c>libobjc</c> at all.
    /// </summary>
    private static readonly Lazy<Names?> Vision = new(Load);

    /// <summary>
    /// Recognises the text in one region. Returns null when Vision is not there to ask, and an
    /// empty string when it found nothing.
    /// </summary>
    /// <param name="bgra">Tightly packed BGRA pixels.</param>
    /// <param name="width">Pixels across.</param>
    /// <param name="height">Pixels down.</param>
    public static unsafe string? Recognize(byte[] bgra, int width, int height)
    {
        if (Vision.Value is not { } names)
        {
            return null;
        }

        var pool = IntPtr.Zero;
        var colorSpace = IntPtr.Zero;
        var provider = IntPtr.Zero;
        var image = IntPtr.Zero;
        var handler = IntPtr.Zero;
        var request = IntPtr.Zero;
        void* buffer = null;

        // Everything below is released in the finally, so it all has to be created inside the try:
        // an allocation that throws must not leave the pool undrained or the buffer behind.
        try
        {
            // The Vision call autoreleases freely, and this thread is a task thread with no pool of
            // its own, so one has to be pushed around the whole call.
            pool = ObjC.Send(ObjC.Send(names.AutoreleasePool, names.Alloc), names.Init);

            // CGDataProviderCreateWithData does not copy, so the pixels live in unmanaged memory
            // that outlives the image rather than in a pinned managed array.
            var size = (nuint)bgra.Length;
            buffer = NativeMemory.Alloc(size);
            bgra.AsSpan().CopyTo(new Span<byte>(buffer, bgra.Length));

            colorSpace = CoreGraphics.CGColorSpaceCreateDeviceRGB();
            provider = CoreGraphics.CGDataProviderCreateWithData(IntPtr.Zero, buffer, size, IntPtr.Zero);
            image = CoreGraphics.CGImageCreate(
                (nuint)width,
                (nuint)height,
                8,
                32,
                (nuint)(width * 4),
                colorSpace,
                CoreGraphics.BgraBitmapInfo,
                provider,
                IntPtr.Zero,
                0,
                0);

            handler = ObjC.SendObjects(
                ObjC.Send(names.ImageRequestHandler, names.Alloc),
                names.InitWithImage,
                image,
                IntPtr.Zero);

            request = ObjC.Send(ObjC.Send(names.RecognizeTextRequest, names.Alloc), names.Init);
            ObjC.SendInteger(request, names.SetLevel, AccurateLevel);

            // Language correction rewrites what it thinks are typos, which on a screen full of
            // identifiers and paths does more harm than the misreadings it fixes.
            ObjC.SendBool(request, names.SetCorrection, 0);
            ObjC.SendObject(request, names.SetLanguages, Languages(names));

            var requests = ObjC.SendObject(names.Array, names.ArrayWithObject, request);
            var performed = ObjC.SendObjectsForBool(handler, names.PerformRequests, requests, IntPtr.Zero);

            return performed == 0 ? string.Empty : Collect(request, names);
        }
        finally
        {
            if (request != IntPtr.Zero)
            {
                ObjC.Send(request, names.Release);
            }

            if (handler != IntPtr.Zero)
            {
                ObjC.Send(handler, names.Release);
            }

            CoreGraphics.CGImageRelease(image);
            CoreGraphics.CGDataProviderRelease(provider);
            CoreGraphics.CGColorSpaceRelease(colorSpace);

            if (buffer is not null)
            {
                NativeMemory.Free(buffer);
            }

            if (pool != IntPtr.Zero)
            {
                ObjC.Send(pool, names.Drain);
            }
        }
    }

    /// <summary>Loads the two frameworks and interns the names, or null where they are absent.</summary>
    private static Names? Load()
        => OperatingSystem.IsMacOS()
            && NativeLibrary.TryLoad(Foundation, out _)
            && NativeLibrary.TryLoad(VisionFramework, out _)
                ? new Names()
                : null;

    /// <summary>An NSArray holding the one language this build asks for.</summary>
    private static IntPtr Languages(Names names)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8("en-US");
        try
        {
            var language = ObjC.SendObject(names.String, names.StringWithUtf8, utf8);
            return ObjC.SendObject(names.Array, names.ArrayWithObject, language);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    /// <summary>
    /// Takes the best candidate for every observation and puts the lines back in reading order.
    /// Vision returns observations in no defined order and measures from the bottom-left corner, so
    /// the top edge sorts descending. Rounding it groups the pieces of one line together, and the
    /// left edge orders them within it.
    /// </summary>
    private static string Collect(IntPtr request, Names names)
    {
        // A CGRect is four doubles, which arm64 returns in registers. x86-64 returns a struct that
        // size through a hidden pointer and needs objc_msgSend_stret, which this file does not
        // declare - so there, the order Vision returned is used as it stands.
        var sortable = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

        var results = ObjC.Send(request, names.Results);
        var found = ObjC.SendForCount(results, names.Count);
        var lines = new List<(double Top, double Left, string Text)>((int)found);

        for (nuint i = 0; i < found; i++)
        {
            var observation = ObjC.SendIndex(results, names.ObjectAtIndex, i);
            var candidates = ObjC.SendIndex(observation, names.TopCandidates, 1);
            if (ObjC.SendForCount(candidates, names.Count) == 0)
            {
                continue;
            }

            var candidate = ObjC.SendIndex(candidates, names.ObjectAtIndex, 0);
            var text = Marshal.PtrToStringUTF8(
                ObjC.Send(ObjC.Send(candidate, names.Text), names.Utf8String));

            if (text is null)
            {
                continue;
            }

            if (sortable)
            {
                var box = ObjC.SendForRect(observation, names.BoundingBox);
                lines.Add((Math.Round(box.Y + box.Height, 2, MidpointRounding.AwayFromZero), box.X, text));
            }
            else
            {
                // Descending, so the sort below leaves the arrival order alone.
                lines.Add((-(double)i, 0, text));
            }
        }

        return string.Join(
            '\n',
            lines.OrderByDescending(line => line.Top)
                .ThenBy(line => line.Left)
                .Select(line => line.Text));
    }

    /// <summary>
    /// The class and selector handles this file sends messages to. Built once, on the first
    /// recognition, and only after the frameworks that define them loaded.
    /// </summary>
    private sealed class Names
    {
        public Names()
        {
            Alloc = ObjC.Selector("alloc");
            Init = ObjC.Selector("init");
            Release = ObjC.Selector("release");
            Drain = ObjC.Selector("drain");
            Count = ObjC.Selector("count");
            ObjectAtIndex = ObjC.Selector("objectAtIndex:");
            ArrayWithObject = ObjC.Selector("arrayWithObject:");
            StringWithUtf8 = ObjC.Selector("stringWithUTF8String:");
            Utf8String = ObjC.Selector("UTF8String");
            InitWithImage = ObjC.Selector("initWithCGImage:options:");
            PerformRequests = ObjC.Selector("performRequests:error:");
            SetLevel = ObjC.Selector("setRecognitionLevel:");
            SetCorrection = ObjC.Selector("setUsesLanguageCorrection:");
            SetLanguages = ObjC.Selector("setRecognitionLanguages:");
            Results = ObjC.Selector("results");
            TopCandidates = ObjC.Selector("topCandidates:");
            BoundingBox = ObjC.Selector("boundingBox");
            Text = ObjC.Selector("string");

            AutoreleasePool = ObjC.Class("NSAutoreleasePool");
            Array = ObjC.Class("NSArray");
            String = ObjC.Class("NSString");
            ImageRequestHandler = ObjC.Class("VNImageRequestHandler");
            RecognizeTextRequest = ObjC.Class("VNRecognizeTextRequest");
        }

        public IntPtr Alloc { get; }

        public IntPtr Init { get; }

        public IntPtr Release { get; }

        public IntPtr Drain { get; }

        public IntPtr Count { get; }

        public IntPtr ObjectAtIndex { get; }

        public IntPtr ArrayWithObject { get; }

        public IntPtr StringWithUtf8 { get; }

        public IntPtr Utf8String { get; }

        public IntPtr InitWithImage { get; }

        public IntPtr PerformRequests { get; }

        public IntPtr SetLevel { get; }

        public IntPtr SetCorrection { get; }

        public IntPtr SetLanguages { get; }

        public IntPtr Results { get; }

        public IntPtr TopCandidates { get; }

        public IntPtr BoundingBox { get; }

        public IntPtr Text { get; }

        public IntPtr AutoreleasePool { get; }

        public IntPtr Array { get; }

        public IntPtr String { get; }

        public IntPtr ImageRequestHandler { get; }

        public IntPtr RecognizeTextRequest { get; }
    }
}
