using System.Runtime.InteropServices;

namespace NScreen.Server.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct D3D11_TEXTURE2D_DESC
{
    public uint Width;
    public uint Height;
    public uint MipLevels;
    public uint ArraySize;
    public uint Format;
    public uint SampleDescCount;
    public uint SampleDescQuality;
    public uint Usage;
    public uint BindFlags;
    public uint CPUAccessFlags;
    public uint MiscFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D3D11_MAPPED_SUBRESOURCE
{
    public unsafe void* Data;
    public uint RowPitch;
    public uint DepthPitch;
}

internal static unsafe partial class D3D11
{
    public const uint SDK_VERSION = 7;
    public const uint DRIVER_TYPE_HARDWARE = 1;

    public const uint FORMAT_B8G8R8A8_UNORM = 87;

    public const uint USAGE_STAGING = 3;
    public const uint CPU_ACCESS_READ = 0x20000;
    public const uint MAP_READ = 1;

    [LibraryImport("d3d11.dll")]
    private static partial int D3D11CreateDevice(
        void* adapter, uint driverType, nint software, uint flags,
        uint* featureLevels, uint featureLevelCount, uint sdkVersion,
        void** device, uint* featureLevel, void** immediateContext);

    public static void CreateDevice(out void* device, out void* context)
    {
        void* dev;
        void* ctx;

        // Null feature levels means the runtime's own newest-first list. No flags either: BGRA
        // support only matters for Direct2D interop, and this only ever CopyResources to staging.
        Com.Check(
            D3D11CreateDevice(null, DRIVER_TYPE_HARDWARE, 0, 0, null, 0, SDK_VERSION, &dev, null, &ctx),
            "D3D11CreateDevice");

        device = dev;
        context = ctx;
    }

    // ID3D11Device slot 5
    public static void* CreateTexture2D(void* device, D3D11_TEXTURE2D_DESC desc)
    {
        void* texture;

        // desc is a by-value local of an unmanaged type, so it already lives at a fixed stack
        // address - no `fixed` statement (the compiler rejects one here, CS0213).
        Com.Check(
            ((delegate* unmanaged[Stdcall]<void*, D3D11_TEXTURE2D_DESC*, void*, void**, int>)(*(void***)device)[5])(
            device, &desc, null, &texture), "ID3D11Device::CreateTexture2D");

        return texture;
    }

    // ID3D11DeviceContext slot 14
    public static D3D11_MAPPED_SUBRESOURCE Map(void* context, void* resource)
    {
        D3D11_MAPPED_SUBRESOURCE mapped = default;
        Com.Check(
            ((delegate* unmanaged[Stdcall]<void*, void*, uint, uint, uint, D3D11_MAPPED_SUBRESOURCE*, int>)(*(void***)context)[14])(
            context, resource, 0, MAP_READ, 0, &mapped), "ID3D11DeviceContext::Map");
        return mapped;
    }

    // ID3D11DeviceContext slot 15 - returns void
    public static void Unmap(void* context, void* resource)
        => ((delegate* unmanaged[Stdcall]<void*, void*, uint, void>)(*(void***)context)[15])(context, resource, 0);

    // ID3D11DeviceContext slot 47 - returns void
    public static void CopyResource(void* context, void* dst, void* src)
        => ((delegate* unmanaged[Stdcall]<void*, void*, void*, void>)(*(void***)context)[47])(context, dst, src);
}
