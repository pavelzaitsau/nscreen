using NScreen.Server.Native;

namespace NScreen.Server.Capture;

/// <summary>
/// DXGI Desktop Duplication over the first output of the default adapter. The compositor hands over
/// the changed rectangles with the frame, so only changed pixels ever move.
/// </summary>
internal sealed unsafe class DesktopDuplicator : IDisposable
{
    /// <summary>Above this fraction of the screen, one whole-screen rectangle is cheaper than a list.</summary>
    private const double WholeScreenThreshold = 0.55;

    private void* _device;
    private void* _context;
    private void* _duplication;
    private void* _staging;
    private byte[] _metadata = new byte[64 * 1024];

    public int Width { get; private set; }

    public int Height { get; private set; }

    public int FrameBytes => Width * 4 * Height;

    /// <summary>False while no monitor carries a desktop - the one state with nothing to send.</summary>
    public bool HasScreen => _duplication is not null;

    public DesktopDuplicator() => Initialize();

    private void Initialize()
    {
        D3D11.CreateDevice(out _device, out _context);

        var dxgiDevice = Com.Cast(_device, Iid.IDXGIDevice, "IDXGIDevice");
        void* adapter = null;
        void* output = null;
        void* output1 = null;
        try
        {
            adapter = Dxgi.DeviceGetAdapter(dxgiDevice);

            output = SelectOutput(adapter);
            if (output is null)
            {
                // No monitor carries a desktop: they are unplugged, or the session has none. There
                // is nothing to duplicate, and inventing a size would put a black screen on the
                // wire, so duplication stays down and Grab reports that the way it reports a lost
                // one - the caller's Reset is what looks again.
                Width = 0;
                Height = 0;
                return;
            }

            output1 = Com.Cast(output, Iid.IDXGIOutput1, "IDXGIOutput1");

            var hr = Dxgi.Output1DuplicateOutput(output1, _device, out _duplication);
            if (hr == Hresult.DXGI_ERROR_UNSUPPORTED)
            {
                throw new InvalidOperationException(
                    "DuplicateOutput is unsupported. On a hybrid-graphics laptop, pin this exe to " +
                    "the GPU that actually drives the display in Windows Graphics Settings.");
            }

            Com.Check(hr, "IDXGIOutput1::DuplicateOutput");

            // Geometry comes from the duplication object, not the output: this is the real size of
            // the texture this copies out of, and it ignores process DPI awareness.
            var desc = Dxgi.DuplicationGetDesc(_duplication);
            Width = (int)desc.Width;
            Height = (int)desc.Height;
            if (Width <= 0 || Height <= 0)
            {
                throw new InvalidOperationException($"Duplication reported an empty desktop ({Width}x{Height}).");
            }

            _staging = D3D11.CreateTexture2D(_device, new D3D11_TEXTURE2D_DESC
            {
                Width = (uint)Width,
                Height = (uint)Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = D3D11.FORMAT_B8G8R8A8_UNORM,
                SampleDescCount = 1,
                Usage = D3D11.USAGE_STAGING,
                CPUAccessFlags = D3D11.CPU_ACCESS_READ,
            });
        }
        finally
        {
            Com.ReleaseAndNull(ref output1);
            Com.ReleaseAndNull(ref output);
            Com.ReleaseAndNull(ref adapter);
            Com.ReleaseAndNull(ref dxgiDevice);
        }
    }

    /// <summary>
    /// The output to duplicate: the primary monitor where there is one, and any other monitor
    /// carrying a desktop otherwise. Null means none of them carries one.
    /// </summary>
    /// <remarks>
    /// Windows puts the primary monitor at the origin of the virtual desktop, and that is what
    /// identifies it here without a single user32 call. AttachedToDesktop is what keeps a monitor
    /// showing nothing out of the stream: duplicating a detached output succeeds and then hands over
    /// an empty image, which looks like a bug at the far end rather than a missing monitor.
    /// </remarks>
    private static void* SelectOutput(void* adapter)
    {
        void* candidate = null;
        var index = 0u;

        // EnumOutputs answers DXGI_ERROR_NOT_FOUND one past the last output, and that is the only
        // thing that says how many there are.
        while (true)
        {
            var hr = Dxgi.AdapterEnumOutputs(adapter, index++, out var output);
            if (hr == Hresult.DXGI_ERROR_NOT_FOUND)
            {
                return candidate;
            }

            Com.Check(hr, "IDXGIAdapter::EnumOutputs");

            var desc = Dxgi.OutputGetDesc(output);
            var carriesDesktop = desc.AttachedToDesktop != 0
                && desc.DesktopCoordinates.Width > 0
                && desc.DesktopCoordinates.Height > 0;

            if (carriesDesktop && desc.DesktopCoordinates.Left == 0 && desc.DesktopCoordinates.Top == 0)
            {
                Com.ReleaseAndNull(ref candidate);
                return output;
            }

            if (carriesDesktop && candidate is null)
            {
                candidate = output;
            }
            else
            {
                Com.Release(output);
            }
        }
    }

    /// <summary>Tears everything down and rebuilds it. Used after a recoverable DXGI failure.</summary>
    public void Reset()
    {
        ReleaseAll();
        Initialize();
    }

    /// <summary>Waits for the screen to change, then fills the packet with what changed.</summary>
    /// <param name="timeoutMs">How long to wait.</param>
    /// <param name="packet">Refilled in place.</param>
    /// <param name="wholeScreen">Force one rectangle covering everything, for a new client.</param>
    public GrabStatus Grab(int timeoutMs, FramePacket packet, bool wholeScreen)
    {
        if (_duplication is null)
        {
            // No monitor to wait on. Waiting the timeout out here keeps the caller retrying at the
            // rhythm a live duplication sets, instead of spinning on Reset as fast as it can.
            Thread.Sleep(timeoutMs);
            return GrabStatus.Lost;
        }

        var hr = Dxgi.AcquireNextFrame(_duplication, (uint)timeoutMs, out var info, out var resource);

        if (hr == Hresult.DXGI_ERROR_WAIT_TIMEOUT)
        {
            return GrabStatus.Timeout;
        }

        if (Hresult.IsRecoverable(hr))
        {
            return GrabStatus.Lost;
        }

        Com.Check(hr, "IDXGIOutputDuplication::AcquireNextFrame");

        bool haveImage;
        try
        {
            // LastPresentTime == 0 means only the mouse cursor moved: no new desktop image.
            haveImage = info.LastPresentTime != 0;
            if (haveImage)
            {
                var texture = Com.Cast(resource, Iid.ID3D11Texture2D, "ID3D11Texture2D");
                try
                {
                    D3D11.CopyResource(_context, _staging, texture);
                }
                finally
                {
                    Com.Release(texture);
                }

                // RectsCoalesced means DXGI merged updates it could not describe precisely.
                if (wholeScreen || info.RectsCoalesced != 0 || !CollectRects(info, packet))
                {
                    packet.RectCount = 1;
                    packet.Rects[0] = new RECT { Right = Width, Bottom = Height };
                }
            }
        }
        finally
        {
            Com.ReleaseAndNull(ref resource);
            Dxgi.ReleaseFrame(_duplication);
        }

        if (!haveImage)
        {
            return GrabStatus.Timeout;
        }

        Pack(packet);
        return GrabStatus.Frame;
    }

    /// <summary>
    /// Reads move and dirty rectangles from the frame metadata. False means the caller should fall
    /// back to one whole-screen rectangle.
    /// </summary>
    private bool CollectRects(DXGI_OUTDUPL_FRAME_INFO info, FramePacket packet)
    {
        packet.RectCount = 0;

        var metaBytes = info.TotalMetadataBufferSize;
        if (metaBytes == 0)
        {
            return false;
        }

        if (_metadata.Length < metaBytes)
        {
            _metadata = new byte[metaBytes];
        }

        long totalArea = 0;

        fixed (byte* meta = _metadata)
        {
            var hr = Dxgi.GetFrameMoveRects(_duplication, metaBytes, (DXGI_OUTDUPL_MOVE_RECT*)meta, out var moveBytes);
            if (hr < 0)
            {
                return false;
            }

            hr = Dxgi.GetFrameDirtyRects(_duplication, metaBytes - moveBytes, (RECT*)(meta + moveBytes), out var dirtyBytes);
            if (hr < 0)
            {
                return false;
            }

            var moveCount = (int)(moveBytes / (uint)sizeof(DXGI_OUTDUPL_MOVE_RECT));
            var dirtyCount = (int)(dirtyBytes / (uint)sizeof(RECT));
            if (moveCount + dirtyCount is 0 or > Protocol.MaxRects)
            {
                return false;
            }

            // A move rect's pixels already sit at their new home in the staging copy, so as far as
            // the client is concerned the destination rectangle is dirty.
            var moves = (DXGI_OUTDUPL_MOVE_RECT*)meta;
            for (var i = 0; i < moveCount; i++)
            {
                if (!TryAdd(packet, moves[i].DestinationRect, ref totalArea))
                {
                    return false;
                }
            }

            var dirty = (RECT*)(meta + moveBytes);
            for (var i = 0; i < dirtyCount; i++)
            {
                if (!TryAdd(packet, dirty[i], ref totalArea))
                {
                    return false;
                }
            }
        }

        return packet.RectCount > 0;
    }

    private bool TryAdd(FramePacket packet, RECT r, ref long totalArea)
    {
        // Clamp: some drivers hand back rectangles that stick out past the output bounds.
        r.Left = Math.Max(0, r.Left);
        r.Top = Math.Max(0, r.Top);
        r.Right = Math.Min(Width, r.Right);
        r.Bottom = Math.Min(Height, r.Bottom);
        if (r.Width <= 0 || r.Height <= 0)
        {
            return true;
        }

        totalArea += (long)r.Width * r.Height;
        if (totalArea > (long)(Width * (double)Height * WholeScreenThreshold))
        {
            return false;
        }

        packet.Rects[packet.RectCount++] = r;
        return true;
    }

    /// <summary>Copies the packet's rectangles out of the mapped staging texture, tightly packed.</summary>
    private void Pack(FramePacket packet)
    {
        var mapped = D3D11.Map(_context, _staging);
        try
        {
            var src = (byte*)mapped.Data;
            var pitch = (int)mapped.RowPitch;

            var needed = 0;
            for (var i = 0; i < packet.RectCount; i++)
            {
                needed += packet.Rects[i].Width * packet.Rects[i].Height * 4;
            }

            packet.EnsurePayload(needed);

            fixed (byte* dst = packet.Payload)
            {
                var offset = 0;
                for (var i = 0; i < packet.RectCount; i++)
                {
                    var r = packet.Rects[i];
                    var rowBytes = r.Width * 4;
                    for (var y = r.Top; y < r.Bottom; y++)
                    {
                        Buffer.MemoryCopy(
                            src + ((long)y * pitch) + ((long)r.Left * 4), dst + offset, rowBytes, rowBytes);
                        offset += rowBytes;
                    }
                }

                packet.PayloadLength = offset;
            }
        }
        finally
        {
            D3D11.Unmap(_context, _staging);
        }
    }

    private void ReleaseAll()
    {
        Com.ReleaseAndNull(ref _staging);
        Com.ReleaseAndNull(ref _duplication);
        Com.ReleaseAndNull(ref _context);
        Com.ReleaseAndNull(ref _device);
    }

    public void Dispose() => ReleaseAll();
}
