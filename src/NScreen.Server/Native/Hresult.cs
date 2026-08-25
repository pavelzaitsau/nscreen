namespace NScreen.Server.Native;

/// <summary>The DXGI status codes desktop duplication actually produces.</summary>
internal static class Hresult
{
    public const int E_ACCESSDENIED = unchecked((int)0x80070005);

    public const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);
    public const int DXGI_ERROR_UNSUPPORTED = unchecked((int)0x887A0004);
    public const int DXGI_ERROR_DEVICE_REMOVED = unchecked((int)0x887A0005);
    public const int DXGI_ERROR_DEVICE_RESET = unchecked((int)0x887A0007);
    public const int DXGI_ERROR_ACCESS_LOST = unchecked((int)0x887A0026);
    public const int DXGI_ERROR_WAIT_TIMEOUT = unchecked((int)0x887A0027);
    public const int DXGI_ERROR_SESSION_DISCONNECTED = unchecked((int)0x887A0028);

    /// <summary>
    /// True where duplication is dead but rebuildable: resolution change, driver reset, UAC or the
    /// secure desktop, a fullscreen exclusive app, a session switch.
    /// </summary>
    public static bool IsRecoverable(int hr) => hr is DXGI_ERROR_ACCESS_LOST
        or DXGI_ERROR_DEVICE_REMOVED
        or DXGI_ERROR_DEVICE_RESET
        or DXGI_ERROR_SESSION_DISCONNECTED
        or E_ACCESSDENIED;
}
