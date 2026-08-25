# Wire protocol

One TCP connection, one direction: the server talks, the client listens. There is no control
channel, no acknowledgement and no heartbeat — the connection itself is the session, and closing it
ends the session. All integers are **little-endian**.

Implemented by [`Protocol.cs`](../src/NScreen.Core/Protocol.cs) (the layout, shared),
[`ScreenServer.cs`](../src/NScreen.Server/ScreenServer.cs) (writer) and
[`FrameReceiver.cs`](../src/NScreen.Client/FrameReceiver.cs) (reader).

## Socket options

| Option | Value | Why |
| --- | --- | --- |
| `TCP_NODELAY` | on, both ends | Nagle would hold a small frame waiting for more data |
| `SO_SNDBUF` (server) | 256 KB | Small on purpose: backpressure must appear quickly. See _Flow control_ |
| `SO_RCVBUF` (client) | 1 MB | Absorbs a whole-screen frame without stalling the reader |

## Hello

Sent once by the server immediately after accept, before any frame. 12 bytes.

| Offset | Size | Field | Notes |
| --- | --- | --- | --- |
| 0 | 4 | `magic` | `0x3143534E` = `"NSC1"`. Version lives in the magic; bump it on any framing change |
| 4 | 4 | `width` | int32, physical pixels |
| 8 | 4 | `height` | int32, physical pixels |

A client that does not see `"NSC1"` must close the connection.

Geometry is fixed for the lifetime of the connection. The server drops the connection rather than
renegotiating, because the client's bitmap is allocated from these dimensions. A resolution change
does that, and so does a different monitor taking over. The client reconnects and sizes a new bitmap
from the next hello, so a monitor switch shows as a held picture rather than a closed window.

A server whose monitors carry no desktop closes the connection before the hello. A client MUST retry
rather than fail: there is nothing to show, and an invented size would put a black screen on the
wire.

There is no capability or feature field. Compression is decided per frame and advertised in the
frame itself, so there is nothing to negotiate here.

## Frame

Repeated until the connection closes. The server sends a frame only when the desktop actually
changed — an idle screen produces no traffic at all.

```text
 flags       u8       bit 7 set = payload is Brotli-compressed
 rectCount   u16      1..384
 rects       rectCount x { left i32, top i32, right i32, bottom i32 }
 wireBytes   i32      bytes of payload on the socket
 rawBytes    i32      bytes after decompression; equal to wireBytes when not compressed
 payload     wireBytes bytes
```

**Every frame has the same shape.** There is no "full frame" message type: a whole-screen update is
one rectangle covering the screen. That removes a branch from the writer, the reader and the
renderer, at a cost of 16 bytes on the frames that need it.

`rectCount` of 0 is invalid — a frame with nothing in it would not have been sent.

### Payload

Tightly packed **BGRA32**, top-down, no row padding. For each rectangle in order, its rows back to
back: `(right - left) * 4` bytes per row, `bottom - top` rows.

Rectangles may overlap; the client applies them in order, so later rectangles win.

Every rectangle lies inside the screen the hello announced, and `right`/`bottom` are never smaller
than `left`/`top`. Neither `wireBytes` nor `rawBytes` exceeds one whole-screen frame: the rectangles
cover at most that, and a compressed payload is kept only when it came out smaller than the raw one.

A reader MUST check all four of those before it indexes anything, and MUST close the connection when
one fails. A rectangle reaching outside the bitmap walks off the buffer, and a length taken on trust
sizes an allocation from bytes that arrived over the network.

BGRA32 is not an arbitrary choice: it is exactly `PixelFormat.Bgra8888`, so frames reach the screen
with a row `memcpy` and no pixel conversion on either platform.

The alpha channel is whatever the compositor left there. The client's bitmap is
`AlphaFormat.Opaque`, which ignores it — the duplicated desktop is the final composited image, so
its alpha carries no meaning.

### Compression

The flag is **per frame**, not per connection: the server tries Brotli (quality 1) and keeps the
result only if it actually came out smaller. A client must handle both, always.

## When the server sends the whole screen

As one rectangle covering the output, when:

- the client is newly connected — there is no other way to establish the initial image;
- duplication was lost and rebuilt (`DXGI_ERROR_ACCESS_LOST` from a UAC prompt, session switch,
  fullscreen exclusive app or driver reset);
- DXGI set `RectsCoalesced`, meaning it merged updates it could not describe precisely;
- the changed area exceeds **55%** of the screen, or there are more than **384** rectangles — past
  that point, enumerating rectangles costs more than sending everything.

Move rectangles from `GetFrameMoveRects` (window dragging, scrolling) are reported as dirty over
their _destination_ rectangle. By the time the server reads the staging texture the pixels are
already at their new location, so the destination is the only thing the client needs.

## Flow control

There is no explicit rate negotiation, and it is not an omission:

1. A slow client lets the server's send buffer fill, and `stream.Write` blocks.
2. While blocked, the server is not calling `AcquireNextFrame`, so DXGI accumulates changes.
3. The next acquire returns one frame with coalesced rectangles.

The result is honest frame dropping under load instead of a queue that grows latency. That is why
the send buffer is deliberately small — a large one would let stale frames pile up in the kernel
where nothing can drop them. It is also why there is no frame-rate option: there is nothing for one
to do that backpressure does not already do better.

## Discovery

A separate, tiny UDP exchange on port **7001** whose only job is to save the user from typing an IP
address. Implemented by [`Discovery.cs`](../src/NScreen.Core/Discovery.cs) (framing),
[`DiscoveryResponder.cs`](../src/NScreen.Server/DiscoveryResponder.cs) (server) and
[`DiscoveryProbe.cs`](../src/NScreen.Client/DiscoveryProbe.cs) (client).

**Probe** — client to broadcast, 4 bytes:

| Offset | Size | Field | Notes |
| --- | --- | --- | --- |
| 0 | 4 | `magic` | `0x5143534E` = `"NSCQ"` |

**Reply** — server to the probe's sender, unicast:

| Offset | Size | Field | Notes |
| --- | --- | --- | --- |
| 0 | 4 | `magic` | `0x5243534E` = `"NSCR"` |
| 4 | 4 | `tcpPort` | int32, where to open the frame connection |
| 8 | 1 | `nameLength` | Bytes of UTF-8 name that follow, truncated at 255 |
| 9 | n | `name` | Machine name — the only thing that tells two servers apart |

Screen geometry is deliberately absent: the hello carries it once connected, and duplicating it
here would be a second thing to keep in step.

The client drives this on purpose. **The server never announces anything by itself** — no timer, no
periodic beacon — so an idle server holds one UDP socket and one thread parked in `recvfrom` and
burns no CPU at all. The cost of discovery is paid only when somebody actually looks.

Probes go to `255.255.255.255` _and_ to the directed broadcast address of every up IPv4 interface.
The global address does not cross a subnet, and on a machine with Wi-Fi plus Ethernet plus a
Hyper-V switch Windows sends it out of only one adapter — the per-interface addresses are what make
this work there. Each probe is sent twice, because UDP has no retransmission.

A multi-homed server answers once per interface the probe reached it on. Every one of those
addresses is reachable by definition, so the client keys on machine name plus TCP port, keeps the
first (fastest) reply, and discards the rest.

Discovery is a convenience, never a dependency: if UDP 7001 is blocked or taken, the server logs it
and keeps serving frames, and the client still works when given an address directly.

## What this protocol does not do

No authentication, no encryption, no integrity check. Anyone who can reach `IP:port` sees the
screen, and any machine on the network can discover that a server exists. This is a trusted-LAN
tool by explicit design — see [ROADMAP.md](ROADMAP.md) for what adding security would involve.
