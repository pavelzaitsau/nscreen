# nscreen

[![ci](https://github.com/pavelzaitsau/nscreen/actions/workflows/ci.yml/badge.svg)](https://github.com/pavelzaitsau/nscreen/actions/workflows/ci.yml)

Minimal screen streaming over a local network: DXGI Desktop Duplication, raw changed rectangles, TCP, an Avalonia window.

This is not a remote desktop. There is no mouse or keyboard control, no audio and no authentication. It shows one machine's screen on another, and nothing else.

- **The server** runs on Windows x64, and cannot run anywhere else: DXGI Desktop Duplication has no macOS equivalent. It has no runtime dependencies - every OS call is hand-written P/Invoke, and COM goes through the vtable directly.
- **The client** runs on Windows x64 and on **macOS with Apple Silicon**, from one codebase, on Avalonia.

The server takes four flags and the client takes two. Everything else decides itself.

## Quick start

Every push to `main` publishes both platforms, so the
[latest release](https://github.com/pavelzaitsau/nscreen/releases/latest) matches the code on `main`.
`SHA256SUMS.txt` sits beside the archives.

Or build both platforms on Windows:

```bash
pwsh ./publish.ps1
```

```text
publish/win-x64/     nscreen-server.exe, nscreen-client.exe + native libraries
publish/osx-arm64/   nscreen-client + native libraries
```

Neither machine needs .NET. **Copy the whole folder, not one file** - Skia, HarfBuzz and the Avalonia native backend sit next to the executable.

On the Windows machine whose screen is shared:

```bash
nscreen-server
```

On the watching machine. **No address needed:**

```bash
nscreen-client
```

On macOS, once after copying:

```bash
chmod +x nscreen-client
```

If Gatekeeper blocks the unsigned binary:

```bash
xattr -dr com.apple.quarantine .
```

`Esc` closes the window and `F11` toggles fullscreen. Dragging the left button over the picture copies that region of the remote screen. After a client disconnects the server goes back to listening.

### From source

Each side runs from its own project:

```bash
dotnet run --project src/NScreen.Server -c Release
```

```bash
dotnet run --project src/NScreen.Client -c Release
```

On macOS, build the client project only. The server does not start there.

## Commands

### nscreen-server, the screen source, Windows

It shares the primary display and takes four flags:

| Option | Effect |
| --- | --- |
| `--port N` | TCP port, default 7000 |
| `--compress` | Brotli over the payload |
| `--headless` | Serve in the background: no console, no window, a log file instead |
| `--system` | High priority, with administrator rights |

`--compress` earns its cost on Wi-Fi, which means almost always where the client is a laptop, and is unnecessary on wired gigabit. On a desktop it gives **20-40x** for the price of one Brotli pass per frame. Compression is decided per frame: where Brotli does not win, the frame goes out raw.

There is no frame-rate option, and nothing to set one with. DXGI delivers a frame only when the screen changed, and TCP backpressure sets the rest.

#### Serving in the background

`--headless` starts the server as a background process and hands the prompt straight back:

```bash
nscreen-server --headless
```

Windows fixes both the console and the access token when a process starts, so a running process can shed neither. The first process starts the real server, prints where it went, and exits. Task Manager lists the server under **Background processes**, because it owns no window.

A background server has no console to print to. It writes events to `nscreen-server.log` beside the executable: the startup line, every client arriving and leaving, a screen change, and the stop. The per-second `fps` line is dropped, because each one overwrites the last and a file keeps every one of them. The log restarts once it passes 1 MB. A folder the server cannot write to costs the log, never the stream.

Stop it the way `Ctrl+C` would:

```bash
nscreen-server --stop
```

`--stop` sets a named event, and the server tears down the socket and the duplication object on it. The name is scoped to one Windows session, which is the session the server shares. Every server in that session waits on the same event, so one `--stop` stops all of them. Where no server answers, `--stop` prints one line and exits with 1.

#### Priority and rights

`--system` raises the process to the High priority class, and asks UAC for administrator rights where it has none:

```bash
nscreen-server --headless --system
```

The two flags belong together: UAC prompts once, and nothing appears on the screen after that. Realtime priority is deliberately absent. The capture loop would outrank the compositor and the input stack of the machine whose screen it shares.

Administrator rights do not buy two things worth knowing about. The UAC prompt and the lock screen live on a separate desktop, which DXGI never hands over. The picture stops until that desktop goes away. And an ordinary prompt cannot signal a server that came up elevated on its own, because Windows integrity levels block it whatever the ACL says. Stop that one from an elevated prompt, or with `taskkill /IM nscreen-server.exe`.

### nscreen-client, the viewer, Windows and macOS

It finds the server itself, and takes an address only where discovery cannot reach:

| Invocation | Effect |
| --- | --- |
| `nscreen-client` | Find a server on the network and connect |
| `nscreen-client 192.168.1.42` | Connect directly, skipping discovery |
| `nscreen-client host:7001` | The same, with the port in the address |
| `nscreen-client [fe80::1]:7000` | An IPv6 literal carries a port only in brackets |

Two flags follow the address: `--port N` for the TCP port, default 7000, and `--mode image` or
`--mode text` for the copy mode the window starts in.

#### Copying a region

Drag the left button over the picture. The frame freezes for the length of the drag, so a moving
screen cannot slide out from under the rectangle. Releasing the button copies what the rectangle
covered:

| Key | Effect |
| --- | --- |
| `I` | Image mode: the selected pixels, at the remote screen's own resolution |
| `T` | Text mode: the English text inside the selection |
| `Esc` | Drop a selection in progress; with none open, close the window |
| `F11` | Fullscreen |

The window title and a badge in the corner carry the current mode. A message under the picture says
what was copied, and the console repeats it.

The pixels come from the frame as it arrived, not from the scaled picture on screen. A window at
half size still copies at the remote screen's full resolution.

Text mode reads the selection with Vision, the OCR engine macOS carries: nothing is bundled, and no
pixels leave the machine. One region takes about 200 ms on Apple Silicon, and 30 to 45 ms once the
first call has loaded the model. On Windows text mode copies nothing and says so, because the OCR
engine there sits behind WinRT activation that this client does not use - see
[docs/ROADMAP.md](docs/ROADMAP.md).

## How it works

One frame travels this path, from the compositor to the window:

```text
        nscreen-server (Windows)                  nscreen-client (Windows / macOS)
   +----------------------------------+        +----------------------------------+
   | IDXGIOutputDuplication           |  UDP   | broadcast "NSCQ" --> find server  |
   |   AcquireNextFrame()             | <----> |                                  |
   |   | GPU texture + dirty rects    |        |                                  |
   | ID3D11DeviceContext              |        | NetworkStream                    |
   |   CopyResource -> staging        |  TCP   |   read header + payload          |
   |   Map() -> CPU pointer           | -----> |   |                              |
   | Pack: changed rows only          |        | WriteableBitmap (Bgra8888)       |
   |                                  |        |   patch each rectangle           |
   |                                  |        | Window.Render -> Skia            |
   +----------------------------------+        +----------------------------------+
```

The key idea: **the Windows compositor already knows which parts of the screen changed.**
`IDXGIOutputDuplication::GetFrameDirtyRects` and `GetFrameMoveRects` hand those rectangles over for free. An idle desktop therefore costs almost no traffic, with no codec at all. Move rectangles, from dragging a window, count as dirty over their destination area. The pixels already sit at their new location in the staging copy.

**A full frame is not a separate case.** Where more than 55% of the screen changed, where there are more than 384 rectangles, where a client is newly connected, or where DXGI merged the updates itself, the server sends **one rectangle covering the screen**. Same code path, same branch, nothing extra on either side.

### Why the client converts no pixels

The wire format is tightly packed BGRA32, which is exactly `PixelFormat.Bgra8888` in Avalonia. A frame lands in a `WriteableBitmap` through a row `memcpy` with no conversion, identically on Windows and on macOS. Skia scales it on the GPU from there.

The client patches the bitmap **straight from the network thread**. It posts only a repaint request to the UI thread, at `DispatcherPriority.Render`. That priority collapses a burst of frames into one paint. No intermediate copy of a full frame exists.

A child `Control` inside the window draws the frame by overriding `Visual.Render`. The client needs **neither XAML nor an Avalonia theme**, because a visual that draws itself needs no template. That keeps the Avalonia XAML compiler and its reflection out of the publish, which is what makes trimming safe.

The drawing cannot sit on the `Window` itself. On macOS with Avalonia 12.1.1 a window that overrides `Visual.Render` paints nothing: frames arrive and decode, and the window stays white. A child control renders on both platforms.

### Discovery

The client broadcasts `"NSCQ"` over UDP to port 7001, and the server answers with its TCP port and machine name. The reply does not repeat the resolution - the handshake carries it.

The client drives this deliberately. The server announces nothing by itself: no timer, no periodic broadcast. It holds one UDP socket and one thread parked in `recvfrom`, so an idle server costs no CPU. Probes go to `255.255.255.255` and to the directed broadcast address of every up IPv4 interface. Without the second part, a machine with Wi-Fi plus Ethernet plus Hyper-V sends the probe out of one adapter only.

### Why raw pixels rather than H.264

For a home gigabit network this is the optimum:

- **No codec latency.** No encoder, no decoder, no B-frames, no reorder buffer.
- **Lossless.** Small text and code read perfectly; H.264 4:2:0 blurs them.
- **No platform split.** The hardware decoder is VideoToolbox on macOS and Media Foundation on Windows; raw pixels are the same everywhere.

The codec is the one fork worth revisiting if the project leaves the local network. See [docs/ROADMAP.md](docs/ROADMAP.md).

### Flow control

There is no control channel, and none is needed:

1. A slow client fills the socket send buffer, and `stream.Write` blocks.
2. While the server is blocked it does not call `AcquireNextFrame`, so DXGI **accumulates** the changes.
3. The next call returns one frame with the rectangles merged.

Under load the system degrades honestly in frame rate rather than growing latency. That is why `SendBufferSize` is deliberately small, at 256 KB: backpressure has to appear quickly.

## Measured performance

An RDP session at 1444x915, AMD Radeon 780M, .NET 10, loopback against a recursive mirror. The whole screen changes every frame, which is the worst possible case:

```text
raw          33 fps, 40-250 Mbit/s
--compress    ~1.6-7.5 Mbit/s      same image, 20-40x
server       67 MB working set
client      147 MB working set     the price of Avalonia + Skia
```

Ordinary editor work costs single-digit Mbit/s, and a static screen produces no traffic at all.

Two server messages show that capture is alive. It prints the resolution at startup, which proves duplication came up. It then prints `fps` and `Mbit/s` every second while it serves.

## Layout

Three projects and a test project, and the split decides what each binary can contain:

```text
nscreen.slnx
Directory.Build.props        shared properties + the analyzer set
global.json                  pins SDK 10.0.4xx
publish.ps1                  win-x64 + osx-arm64
.github/
  workflows/ci.yml           the gates, both platforms, the release
  scripts/version.ps1        the next version, from the tags and the commits
docs/PROTOCOL.md             the wire format, byte by byte
docs/ROADMAP.md              what comes next, and why in that order
src/
  NScreen.Core/              net10.0 - wire formats only, not one OS call
    Protocol.cs              frames and handshake
    Discovery.cs             UDP probe and reply
    ServerInfo.cs            what the client found on the network
    Rect.cs                  RECT, which also describes a rectangle on the wire
  NScreen.Server/            net10.0-windows, x64. Links neither user32 nor gdi32
    Program.cs               four flags, and what each one decides before the server starts
    ScreenServer.cs          listener + send loop
    DiscoveryResponder.cs    one UDP socket, one sleeping thread
    Launcher.cs              the relaunch --headless and --system need, and the priority
    StopSignal.cs            the named event behind --stop
    Log.cs                   console, or the log file --headless writes instead
    Native/                  AGENTS.md - the vtable rules live here
      Com.cs                 QueryInterface/Release through the vtable, no CLR COM interop
      Dxgi.cs                structs, IIDs, vtable slot numbers
      D3D11.cs               CreateDevice, CreateTexture2D, Map, CopyResource
      Hresult.cs             DXGI codes, and which of them are recoverable
    Capture/
      DesktopDuplicator.cs   the core: duplication, rectangles, pixel packing
      FramePacket.cs         reusable frame container
  NScreen.Client/            net10.0, Avalonia. Not one Windows-only call
    Program.cs               arguments, discovery, connection, UI startup
    DiscoveryProbe.cs        broadcast + reply collection
    ViewerWindow.cs          window sizing, title, keys and the copy mode
    ViewerSurface.cs         WriteableBitmap + letterbox render + the selection overlay
    FrameReceiver.cs         receive loop
    RegionSelection.cs       window coordinates to remote pixels, and the crop
    RegionCopier.cs          the clipboard, as pixels or as recognised text
    SelectionMode.cs         image or text
    PixelRegion.cs           a rectangle of the remote screen, in its own pixels
    Target.cs                host[:port], including the IPv6 forms
    Native/                  macOS only, behind a runtime check
      ObjC.cs                objc_getClass, sel_registerName, objc_msgSend
      CoreGraphics.cs        BGRA bytes to the CGImage Vision reads
      MacTextRecognizer.cs   VNRecognizeTextRequest, and the reading order of its results
tests/
  NScreen.Tests/             net10.0, MSTest. Wire formats and discovery
```

The split is not cosmetic: the server holds no window or rendering code, and the client holds no D3D or capture code. `CA1416`, platform compatibility, is deliberately left enabled in the client and in Core. It is the analyzer that catches a Windows-only call in code that has to run on macOS.

`NScreen.Client/Native/` is the one platform-specific corner of the client, and it is macOS-only. The Objective-C runtime, CoreGraphics and Vision are reached the way the server reaches COM. `RegionCopier` checks `OperatingSystem.IsMacOS()` first, so a Windows client never loads those frameworks.

## Tests

```bash
dotnet test nscreen.slnx
```

MSTest, so the Visual Studio Test Explorer lists them. They cover the wire formats: the hello, the frame header, the discovery datagrams and the `RECT` layout. The expected bytes are written out in the tests, copied from [docs/PROTOCOL.md](docs/PROTOCOL.md) rather than produced by the code. A moved byte offset fails a test instead of arriving as a corrupt picture.

One case is not a unit test. It binds UDP 7001, answers a probe the way the server does, and runs the client's real `DiscoveryProbe.Find` against it. It reports inconclusive, not failed, when something else already holds the port.

## Troubleshooting

Each row is a symptom somebody has hit, with its cause:

| Symptom | Cause | Fix |
| --- | --- | --- |
| `DuplicateOutput is unsupported` | Hybrid graphics: duplication works only on the GPU that drives the display | Pin the exe to that adapter in Settings, System, Display, Graphics |
| The client finds no server | Windows Firewall blocks UDP 7001 or TCP 7000 | Allow both, see below |
| The client finds no server on a guest network or a VPN | Client isolation, separate subnets, or WireGuard, which UDP broadcast does not cross | Pass the address: `nscreen-client 192.168.1.42` |
| macOS: `Permission denied` | The execute bit was lost copying from Windows | `chmod +x nscreen-client` |
| macOS: cannot verify the developer | Gatekeeper; the binary is unsigned | `xattr -dr com.apple.quarantine .` in the client folder |
| The picture freezes for a second | A UAC prompt or a user switch kills duplication with `DXGI_ERROR_ACCESS_LOST` | None needed: the server rebuilds it and resends the whole screen |
| The picture holds for a second, then returns at a new size | The screen changed size, so the hello the client already has is wrong | None needed: the server drops the connection and the client reconnects against the new geometry |
| The client prints `Waiting for ...` and holds the last frame | No monitor on the server carries a desktop, so there is nothing to send | Plug one back in. The client picks the screen up by itself |
| A black client window | Look at the server console | Non-zero `fps` and `Mbit/s` there mean capture and sending are fine, and the fault is in the client or the network |
| `accepted the connection and then sent nothing` | Something that is not nscreen-server listens on that port. On macOS the AirPlay receiver holds TCP 7000 | Point the client at the right port, or turn the other listener off |

Allowing the two ports needs an administrator shell:

```bash
netsh advfirewall firewall add rule name="nscreen tcp" dir=in action=allow protocol=TCP localport=7000
```

```bash
netsh advfirewall firewall add rule name="nscreen udp" dir=in action=allow protocol=UDP localport=7001
```

Capturing the secure desktop, the UAC dialog itself, is impossible. That is a Windows restriction rather than a defect here.

## Deliberately absent

Every item here was considered and left out, not overlooked:

- **The mouse cursor is invisible.** DXGI hands the pointer over separately through `GetFramePointerShape`, and it is not in the frame. This shows during a demonstration, and it is the first thing worth adding.
- The primary monitor only, and no picker. The server follows whichever monitor Windows puts at the origin of the virtual desktop, so unplugging that one or promoting another switches the stream. Choosing a particular monitor is not possible.
- One viewer at a time.
- **Text mode reads the screen on macOS only.** Windows has an OCR engine, `Windows.Media.Ocr`, but reaching it means WinRT activation rather than the plain P/Invoke this codebase uses. Image mode works on both platforms.
- No encryption and no authentication. Whoever reaches `IP:port` watches. Use it on a trusted network only.
- No test touches the capture path. Its risk sits in vtable slot numbers and struct layouts, and only running the server on Windows checks those. The wire formats and discovery are covered; see [Tests](#tests).

[docs/ROADMAP.md](docs/ROADMAP.md) holds the order of the next steps and the reasoning behind it.

## Requirements

The two sides need different things, and a published binary needs almost nothing:

| Part | Needs |
| --- | --- |
| Server | Windows 10 1607+ or Windows 11, x64 |
| Client | Windows x64, or macOS on Apple Silicon |
| Building | .NET SDK 10.0.4xx, pinned by `global.json` |
| Running a published binary | Nothing; .NET is not required |

NativeAOT applies to the server only, and only where the Visual Studio workload "Desktop development with C++" is installed. Without it `publish.ps1` falls back to a trimmed single file. The client is always a trimmed single file: NativeAOT cannot cross-compile from Windows to macOS.

The release workflow publishes the server with `-RequireAot`, so a released server is always the AOT build. A runner image without the C++ toolchain fails the job instead.
