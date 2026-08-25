namespace NScreen.Client;

/// <summary>
/// Splits the address a user pastes into a host and a port. "host:port" is the form that gets
/// passed around, so it is accepted as well as --port.
/// </summary>
internal static class Target
{
    /// <summary>
    /// Returns <paramref name="fallbackPort"/> when the token carries no port of its own.
    /// <para>
    /// An IPv6 literal is all colons, so only the bracketed form carries a port: "[::1]:7000" is a
    /// host and a port, and a bare "fe80::7000" is a host. Splitting on the last colon regardless
    /// would turn that address into "fe80:" on port 7000 and connect somewhere else entirely.
    /// </para>
    /// </summary>
    public static (string Host, int Port) Split(string token, int fallbackPort)
    {
        if (token.StartsWith('['))
        {
            var close = token.IndexOf(']');
            if (close > 0)
            {
                var literal = token[1..close];
                return close + 2 < token.Length
                    && token[close + 1] == ':'
                    && int.TryParse(token.AsSpan(close + 2), out var bracketed)
                    ? (literal, bracketed)
                    : (literal, fallbackPort);
            }
        }

        var colon = token.LastIndexOf(':');
        if (colon > 0
            && token.IndexOf(':') == colon
            && int.TryParse(token.AsSpan(colon + 1), out var port))
        {
            return (token[..colon], port);
        }

        return (token, fallbackPort);
    }

    /// <summary>
    /// The address as a person reads it. An IPv6 host gets its brackets back, because
    /// "fe80::7000:7000" is unreadable and "[fe80::7000]:7000" is not.
    /// </summary>
    public static string Describe(string host, int port)
        => host.Contains(':', StringComparison.Ordinal) ? $"[{host}]:{port}" : $"{host}:{port}";
}
