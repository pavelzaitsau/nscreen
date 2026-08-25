using System.Runtime.InteropServices;

namespace NScreen.Client.Native;

/// <summary>
/// The Objective-C runtime, hand-written the way the server writes COM. Every message goes through
/// <c>objc_msgSend</c>, and each one needs a declaration whose signature matches the method being
/// sent: arm64 passes every argument in its own register, so a mismatched declaration reads the
/// wrong register rather than failing. Only what the Vision text request needs is here.
/// </summary>
internal static partial class ObjC
{
    private const string Runtime = "/usr/lib/libobjc.A.dylib";

    /// <summary>Looks a class up by name. Zero when the framework that defines it is not loaded.</summary>
    [LibraryImport(Runtime, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr Class(string name);

    /// <summary>Interns a selector. Cheap enough to call at every use site.</summary>
    [LibraryImport(Runtime, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr Selector(string name);

    /// <summary>No arguments. Also used for methods that return nothing, whose result is ignored.</summary>
    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static partial IntPtr Send(IntPtr receiver, IntPtr selector);

    /// <summary>One object argument, returning an object.</summary>
    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static partial IntPtr SendObject(IntPtr receiver, IntPtr selector, IntPtr argument);

    /// <summary>Two object arguments, returning an object.</summary>
    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static partial IntPtr SendObjects(IntPtr receiver, IntPtr selector, IntPtr first, IntPtr second);

    /// <summary>Two object arguments, returning <c>BOOL</c> - one byte on every Apple platform.</summary>
    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static partial byte SendObjectsForBool(IntPtr receiver, IntPtr selector, IntPtr first, IntPtr second);

    /// <summary>One <c>NSInteger</c> argument.</summary>
    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static partial void SendInteger(IntPtr receiver, IntPtr selector, nint argument);

    /// <summary>One <c>BOOL</c> argument.</summary>
    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static partial void SendBool(IntPtr receiver, IntPtr selector, byte argument);

    /// <summary>One <c>NSUInteger</c> argument, returning an object.</summary>
    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static partial IntPtr SendIndex(IntPtr receiver, IntPtr selector, nuint argument);

    /// <summary>No arguments, returning <c>NSUInteger</c>.</summary>
    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static partial nuint SendForCount(IntPtr receiver, IntPtr selector);

    /// <summary>
    /// No arguments, returning <c>CGRect</c>. Four doubles are a homogeneous aggregate, which arm64
    /// returns in registers - this is why the <c>objc_msgSend_stret</c> of the x86-64 days has no
    /// part here.
    /// </summary>
    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    internal static partial CoreGraphics.CGRect SendForRect(IntPtr receiver, IntPtr selector);
}
