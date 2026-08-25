# Server Native/ — agent instructions

This folder is the server's entire boundary to Windows: d3d11, dxgi and one winmm call. Everything
here is hand-written P/Invoke and hand-written COM. Mistakes in this folder do not throw — they read the wrong function
pointer or the wrong struct field and corrupt memory or silently produce black frames. Treat it with
more care than the rest of the repo.

## Non-negotiables

- **Never use `[ComImport]`, `Marshal.GetObjectForIUnknown`, or any CLR COM interop.** Every COM call
  goes through the object's vtable as a `delegate* unmanaged[Stdcall]<...>`. That is what keeps this
  AOT-compatible and free of marshalling stubs.
- **Use `[LibraryImport]`, never `[DllImport]`,** for flat exports. Source-generated marshalling only.
- **No `bool` in interop signatures.** Win32 `BOOL` is a 4-byte `int`; a managed `bool` is 1 byte
  unless marshalled. Declare `int` and compare against 0.
- **`Guid` is passed as `Guid*`**, taken from a by-value parameter or a local, never as a `ref` to a
  `static readonly` field via `fixed`.
- **`x64` only.** Struct layouts here are reasoned about with 8-byte pointers and 8-byte alignment.

## Adding a COM method

The vtable slot number is the single most dangerous value in this codebase. Derive it, do not guess it.

1. Open the interface in the Windows SDK header (`dxgi.h`, `dxgi1_2.h`, `d3d11.h`) and find the
   `Vtbl` struct — it lists methods in exact slot order.
2. Count from the top of the **full inheritance chain**, starting at `IUnknown`:
   `QueryInterface = 0`, `AddRef = 1`, `Release = 2`.
3. Every base interface contributes its methods before the derived ones. The chains that matter here:

   | Interface | Base chain | First own slot |
| --- | --- | --- |
   | `IDXGIObject` | IUnknown | 3 (`SetPrivateData`) |
   | `IDXGIDeviceSubObject` | IDXGIObject | 7 (`GetDevice`) |
   | `IDXGIResource` | IDXGIDeviceSubObject | 8 (`GetSharedHandle`) |
   | `IDXGIDevice` | IDXGIObject | 7 (`GetAdapter`) |
   | `IDXGIAdapter` | IDXGIObject | 7 (`EnumOutputs`) |
   | `IDXGIOutput` | IDXGIObject | 7 (`GetDesc`) |
   | `IDXGIOutput1` | IDXGIOutput | 19 (`GetDisplayModeList1`) |
   | `IDXGIOutputDuplication` | IDXGIObject | 7 (`GetDesc`) |
   | `ID3D11Device` | IUnknown | 3 (`CreateBuffer`) |
   | `ID3D11DeviceChild` | IUnknown | 3 (`GetDevice`) |
   | `ID3D11DeviceChild` | IUnknown | 3 (`GetDevice`) |
   | `ID3D11DeviceContext` | ID3D11DeviceChild | 7 (`VSSetConstantBuffers`) |

4. Put the slot number in a comment directly above the wrapper, exactly as the existing ones do:
   `// IDXGIOutputDuplication slot 8`.
5. Watch the return type. Most COM methods return `HRESULT` (`int`), but some return `void`
   (`IDXGIOutputDuplication::GetDesc`, `ID3D11DeviceContext::CopyResource`, `Unmap`). Declaring a
   `void` method as returning `int` reads garbage from a register.
6. Run the server afterwards. A wrong slot usually shows up immediately as an access violation, or
   as the startup line reporting a nonsense resolution.

## Adding or changing a struct

Check the size by hand before trusting it. Sequential layout in .NET matches the C compiler here,
but only if you account for alignment padding — a `long`/pointer field forces 8-byte alignment.

Worked example, `DXGI_OUTDUPL_FRAME_INFO`:

```text
LastPresentTime         long                    0  (8)
LastMouseUpdateTime     long                    8  (8)
AccumulatedFrames       uint                   16  (4)
RectsCoalesced          int (BOOL)             20  (4)
ProtectedContentMaskedOut int (BOOL)           24  (4)
PointerX, PointerY      int, int               28  (8)
PointerVisible          int (BOOL)             36  (4)
TotalMetadataBufferSize uint                   40  (4)
PointerShapeBufferSize  uint                   44  (4)
                                        total = 48
```

Note the pointer-position fields are spelled out inline rather than as a nested struct. **The size
is the contract** - DXGI writes all 48 bytes whether this code reads them or not, so a field that looks
unused is still load-bearing and must never be deleted to "clean up". If a new struct's arithmetic
is not obvious, check it with `sizeof(T)` rather than hoping.

## Where the IIDs come from

`Iid` in `Dxgi.cs` holds interface GUIDs copied from the SDK headers. Copy them from the header, not
from a search result — a transposed digit gives you a clean `E_NOINTERFACE` at best and the wrong
interface at worst. `Iid` holds only the interfaces actually queried; adding one you do not
`QueryInterface` for is dead weight.

## Naming

Structs, fields and constants intentionally mirror the SDK spelling (`WNDCLASSEXW`, `RECT`,
`DXGI_OUTDUPL_DESC`, `Format`). That is what makes a declaration checkable line-by-line against
MSDN, so `src/.editorconfig` disables the naming and file-organisation analyzers for this folder. Do
not "fix" these names, and do not add non-interop helper logic here that would then inherit the
exemption.

There is no counterpart on the client side any more: it renders through Avalonia and contains no
P/Invoke at all, which is what lets it run on macOS.
