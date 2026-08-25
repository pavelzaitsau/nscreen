using System.Runtime.InteropServices;

namespace NScreen.Server.Native;

/// <summary>
/// COM without the CLR's interop: every call goes through the object's vtable directly, which keeps
/// it AOT-friendly and free of marshalling stubs. See AGENTS.md in this folder.
/// </summary>
internal static unsafe class Com
{
    /// <summary>vtable slot 0 - IUnknown::QueryInterface</summary>
    public static void* Cast(void* self, Guid iid, string what)
    {
        void* result;
        var hr = ((delegate* unmanaged[Stdcall]<void*, Guid*, void**, int>)(*(void***)self)[0])(self, &iid, &result);
        Check(hr, $"QueryInterface({what})");
        return result;
    }

    /// <summary>vtable slot 2 - IUnknown::Release</summary>
    public static uint Release(void* self)
        => self is null ? 0u : ((delegate* unmanaged[Stdcall]<void*, uint>)(*(void***)self)[2])(self);

    public static void ReleaseAndNull(ref void* self)
    {
        if (self is null)
        {
            return;
        }

        Release(self);
        self = null;
    }

    public static void Check(int hr, string what)
    {
        if (hr < 0)
        {
            throw new COMException($"{what} failed: 0x{hr:X8}", hr);
        }
    }
}
