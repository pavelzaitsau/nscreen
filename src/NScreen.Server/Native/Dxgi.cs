using System.Runtime.InteropServices;

namespace NScreen.Server.Native;

/// <summary>Only DestinationRect is ever read; SourcePoint is here to keep the size at 24 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DXGI_OUTDUPL_MOVE_RECT
{
    public int SourceX;
    public int SourceY;
    public RECT DestinationRect;
}

/// <summary>
/// 48 bytes, and the size is the contract: DXGI writes all of it. Only LastPresentTime,
/// RectsCoalesced and TotalMetadataBufferSize are read; the rest is padding with real names.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DXGI_OUTDUPL_FRAME_INFO
{
    public long LastPresentTime;
    public long LastMouseUpdateTime;
    public uint AccumulatedFrames;
    public int RectsCoalesced;
    public int ProtectedContentMaskedOut;
    public int PointerX;
    public int PointerY;
    public int PointerVisible;
    public uint TotalMetadataBufferSize;
    public uint PointerShapeBufferSize;
}

/// <summary>
/// DXGI_MODE_DESC inlined, plus rotation and the system-memory flag. Width/Height is the
/// authoritative texture size; IDXGIOutput::GetDesc reports DPI-virtualised coordinates instead.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DXGI_OUTDUPL_DESC
{
    public uint Width;
    public uint Height;
    public uint RefreshRateNumerator;
    public uint RefreshRateDenominator;
    public uint Format;
    public uint ScanlineOrdering;
    public uint Scaling;
    public uint Rotation;
    public int DesktopImageInSystemMemory;
}

/// <summary>
/// 96 bytes on x64, and the size is the contract: DXGI writes all of it. Only DesktopCoordinates and
/// AttachedToDesktop are read; DeviceName and Monitor hold the layout together.
/// </summary>
/// <remarks>
/// DeviceName WCHAR[32] at 0 (64), DesktopCoordinates at 64 (16), AttachedToDesktop at 80 (4),
/// Rotation at 84 (4), Monitor at 88 (8) - already 8-aligned, so nothing pads. Total 96.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct DXGI_OUTPUT_DESC
{
    public fixed char DeviceName[32];
    public RECT DesktopCoordinates;
    public int AttachedToDesktop;
    public uint Rotation;
    public nint Monitor;
}

/// <summary>Interface GUIDs, copied from the Windows SDK headers.</summary>
internal static class Iid
{
    public static readonly Guid IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    public static readonly Guid IDXGIOutput1 = new("00cddea8-939b-4b83-a340-a685226666cc");
    public static readonly Guid ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
}

/// <summary>
/// Vtable calls for the DXGI methods duplication needs. Slots count from IUnknown:
/// QueryInterface=0, AddRef=1, Release=2. Read AGENTS.md here before adding one.
/// </summary>
internal static unsafe class Dxgi
{
    // IDXGIDevice slot 7
    public static void* DeviceGetAdapter(void* device)
    {
        void* adapter;
        Com.Check(
            ((delegate* unmanaged[Stdcall]<void*, void**, int>)(*(void***)device)[7])(device, &adapter),
            "IDXGIDevice::GetAdapter");
        return adapter;
    }

    // IDXGIAdapter slot 7
    public static int AdapterEnumOutputs(void* adapter, uint index, out void* output)
    {
        void* result;
        var hr = ((delegate* unmanaged[Stdcall]<void*, uint, void**, int>)(*(void***)adapter)[7])(adapter, index, &result);
        output = result;
        return hr;
    }

    // IDXGIOutput slot 7
    public static DXGI_OUTPUT_DESC OutputGetDesc(void* output)
    {
        DXGI_OUTPUT_DESC desc = default;
        Com.Check(
            ((delegate* unmanaged[Stdcall]<void*, DXGI_OUTPUT_DESC*, int>)(*(void***)output)[7])(output, &desc),
            "IDXGIOutput::GetDesc");
        return desc;
    }

    // IDXGIOutput1 slot 22
    public static int Output1DuplicateOutput(void* output1, void* device, out void* duplication)
    {
        void* result;
        var hr = ((delegate* unmanaged[Stdcall]<void*, void*, void**, int>)(*(void***)output1)[22])(output1, device, &result);
        duplication = result;
        return hr;
    }

    // IDXGIOutputDuplication slot 7 - returns void, not HRESULT
    public static DXGI_OUTDUPL_DESC DuplicationGetDesc(void* duplication)
    {
        DXGI_OUTDUPL_DESC desc = default;
        ((delegate* unmanaged[Stdcall]<void*, DXGI_OUTDUPL_DESC*, void>)(*(void***)duplication)[7])(duplication, &desc);
        return desc;
    }

    // IDXGIOutputDuplication slot 8
    public static int AcquireNextFrame(void* duplication, uint timeoutMs, out DXGI_OUTDUPL_FRAME_INFO info, out void* resource)
    {
        DXGI_OUTDUPL_FRAME_INFO localInfo = default;
        void* localResource;
        var hr = ((delegate* unmanaged[Stdcall]<void*, uint, DXGI_OUTDUPL_FRAME_INFO*, void**, int>)(*(void***)duplication)[8])(
            duplication, timeoutMs, &localInfo, &localResource);
        info = localInfo;
        resource = localResource;
        return hr;
    }

    // IDXGIOutputDuplication slot 9
    public static int GetFrameDirtyRects(void* duplication, uint bufferBytes, RECT* buffer, out uint requiredBytes)
    {
        uint required;
        var hr = ((delegate* unmanaged[Stdcall]<void*, uint, RECT*, uint*, int>)(*(void***)duplication)[9])(
            duplication, bufferBytes, buffer, &required);
        requiredBytes = required;
        return hr;
    }

    // IDXGIOutputDuplication slot 10
    public static int GetFrameMoveRects(void* duplication, uint bufferBytes, DXGI_OUTDUPL_MOVE_RECT* buffer, out uint requiredBytes)
    {
        uint required;
        var hr = ((delegate* unmanaged[Stdcall]<void*, uint, DXGI_OUTDUPL_MOVE_RECT*, uint*, int>)(*(void***)duplication)[10])(
            duplication, bufferBytes, buffer, &required);
        requiredBytes = required;
        return hr;
    }

    // IDXGIOutputDuplication slot 14
    public static int ReleaseFrame(void* duplication)
        => ((delegate* unmanaged[Stdcall]<void*, int>)(*(void***)duplication)[14])(duplication);
}
