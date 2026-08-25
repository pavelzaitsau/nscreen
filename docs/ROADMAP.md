# Roadmap

v1 is deliberately narrow: primary display, one client, raw pixels, trusted LAN. The server is
Windows-only by nature; the client runs on Windows and Apple Silicon macOS. This file records what
was left out, why, and what it would actually cost — so a future decision starts from evidence
rather than from scratch.

Nothing here is committed work. The ordering reflects value per unit of effort, not a schedule.

## 1. Mouse cursor — small, high impact

**Why it matters.** DXGI composites the desktop _without_ the cursor and hands the pointer over
separately. Right now a demo shows things happening with no visible pointer, which is exactly the
information the person watching needs most.

**What it takes.** `DXGI_OUTDUPL_FRAME_INFO.PointerPosition` already arrives on every frame and is
already parsed — it is ignored. The shape needs
`IDXGIOutputDuplication::GetFramePointerShape` (slot 11), which returns one of three formats:
monochrome (AND/XOR masks), colour, or masked colour. Send position plus shape-when-it-changes as a
third message kind, and composite it in the client on top of the mirror rather than into it, so the
cursor can move without dirtying pixels.

**Cost.** Roughly a day. The monochrome mask format is the fiddly part.

## 2. Monitor and window selection

**Why it was skipped.** Explicitly out of scope for v1 — first output, no choice.

**What it takes.** Monitor selection is nearly free: `IDXGIAdapter::EnumOutputs(n)` already takes an
index, and outputs can be enumerated across adapters to build a list. Note it would also need a
control message or a server flag, since the client currently learns geometry once in the hello.
A single _window_ is a different mechanism entirely — `Windows.Graphics.Capture`
(`GraphicsCaptureItem`), which means WinRT activation rather than plain COM. That is a real
dependency jump; weigh it against letting the presenter arrange their windows on one screen.

## 3. H.264 / HEVC via Media Foundation — the one real fork in the road

**When it becomes necessary.** Raw changed rectangles are the right answer on a gigabit LAN and the
wrong answer anywhere else. Over Wi-Fi, a VPN or the internet, a busy screen will saturate the link
long before the CPU notices. `--compress` (Brotli q1, 20-40x on desktop pixels) buys a lot of
headroom, but video content still defeats it.

**What it takes.** On the server: the staging texture already holds a GPU-side BGRA frame, the
natural input to a hardware encoder MFT. That is a Media Foundation transform wrapper — `MFTEnumEx`
to find the H.264 encoder, `IMFTransform` to drive it, `IMFDXGIDeviceManager` so the encoder shares
the server's D3D11 device and the frame never leaves the GPU.

Decode is the harder half now that the client is cross-platform: Media Foundation on Windows and
VideoToolbox on macOS are two separate interop layers, which is exactly the kind of per-platform
work Avalonia was chosen to avoid. A managed software decoder would work everywhere but gives back
the CPU headroom this was meant to buy. This is the main reason raw pixels remain the right default.

**What it costs beyond code.** Lossy 4:2:0 chroma subsampling blurs small text — the thing this tool
is most often pointed at. Encoder latency adds a frame or two. Hybrid-graphics machines and VMs
without a hardware encoder need a software fallback.

**Design note.** The frame already carries a flags byte with one bit used, so a codec bit fits
without a framing change. Put the encoder behind an `IFrameCodec` seam (`Encode(FramePacket) -> ReadOnlySpan<byte>`)
- but note the hello has no capability field any more, so a client that cannot decode on its
platform would need one added back, or a probe-and-fallback. The transport, the rectangle list and
the `WriteableBitmap` all stay as they are; this should not become a rewrite.

## 4. Multiple clients

**What it takes.** Structurally easy — capture once, fan the packet out to N sockets. The catch is
flow control: today a single slow socket throttles the capture loop, which is the desired behaviour.
With several clients, one slow one must not stall the others, so each needs its own send thread plus
a policy for who gets dropped. Also, each newly connected client needs a full frame while the others
are mid-stream, so the fan-out has to handle mixed full/dirty per client. Weigh all of that against
the stated goal of a frugal server.

## 5. Security

**Currently.** None. Whoever reaches `IP:port` watches the screen, in cleartext, and discovery will
happily tell the whole subnet that a server exists.

**Minimum credible version.** A pre-shared session code, and either `SslStream` with a self-signed
certificate (simple, but certificate trust on a home LAN is its own annoyance) or ChaCha20-Poly1305
from `System.Security.Cryptography` over the existing framing with a key derived from the code.
The latter is less ceremony and stays dependency-free.

**Worth being honest about:** on a trusted home LAN this buys very little, and it is the kind of
half-measure that invites misplaced confidence. Add it when the tool leaves the LAN — which in
practice means doing it together with item 3 and 6, not before.

## 6. Reaching outside the LAN

Not a code problem first. The options, roughly in order of effort:

- **WireGuard / Tailscale.** Zero code for the frame stream — the current build already works over
  it, though UDP broadcast discovery does not cross it, so pass the address directly. This is the
  right answer for a personal tool and should be the recommendation before anything below.
- **Manual port forward.** Also zero code, worse security posture.
- **TCP relay on a VPS.** Both ends dial out, matched by a short session code. Works behind any NAT,
  costs a host and adds a round trip.
- **STUN / UDP hole punching.** Real P2P, but ~20-30% of NAT pairs still fall back to a relay, so it
  is strictly _additional_ work on top of having a relay, never instead of it.

## 7. Capturing across UAC, the lock screen and a logoff

**Why it comes up.** `--headless --system` covers unattended use until the desktop switches. A UAC
prompt and the lock screen are separate desktops, where duplication returns `ACCESS_LOST`. The
client holds the last frame until the user's own desktop comes back. Administrator rights change
none of that; the desktop, not the token, is what withholds the pixels.

**What it takes.** A process that follows the input desktop, which means `SetThreadDesktop` against
the current one, which means the SYSTEM account rather than an elevated user. Desktop Duplication
cannot run in session 0, so SYSTEM alone is not enough either: it takes a service that stays in
session 0 and a worker it launches into the interactive session through `WTSQueryUserToken` and
`CreateProcessAsUser`. The service also owns installation, session-change notifications and
restarting a worker that died with its session.

**Cost.** Days rather than hours, and it turns one executable into a service plus a worker. It sits
this far down for that reason, not because the gap does not matter. Weigh it against the frugal server
this is meant to be.

## 8. Text recognition on Windows

**Where it stands.** Dragging a region and copying it works on both platforms. Reading the text in
that region works on macOS only, through Vision — see
[`MacTextRecognizer.cs`](../src/NScreen.Client/Native/MacTextRecognizer.cs). On Windows the client
copies nothing in text mode and says why.

**What it takes.** `Windows.Media.Ocr` is the engine, and it is WinRT rather than flat exports or
COM the way `Native/` uses them. Reaching it means either `CsWinRT` projections, which is a package
and a trimming problem in a client that carries neither, or hand-written `IActivationFactory`
activation through `RoGetActivationFactory`. The second one fits the codebase and is perhaps 200
lines: activate `OcrEngine`, call `TryCreateFromUserProfileLanguages`, wrap the pixels in a
`SoftwareBitmap`, then walk `OcrResult.Lines`.

**Worth knowing first.** The engine needs the language pack for what it reads, English is present on
most installs, and the result is per-line text with no layout. That is the same shape Vision returns,
so `RegionCopier` would need no new seam beyond a second implementation behind the platform check.

## Explicitly not planned

- **Input forwarding** (mouse/keyboard control). That turns a screen-sharing tool into remote
  desktop, which is a different product with a different threat model. Use RDP.
- **Audio.** Separate capture path (`IAudioClient` loopback on the server, a second output stack on
  each client platform), separate sync problem, no stated need.
- **Recording to file.** The server's live fps/bitrate line answers the performance questions; a
  recorder would mostly be a way to accidentally write the user's screen to disk.
- **A settings UI.** The tool is a handful of flags on each side. A UI would be more code than the
  thing it configures.
