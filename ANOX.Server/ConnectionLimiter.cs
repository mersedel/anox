using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using ANOX.Core;

namespace ANOX.Server;

/// <summary>
/// Caps open connections in total and per address. Every connection costs a thread,
/// so without a cap one host could open thousands of sockets and exhaust the server.
/// </summary>
public static class ConnectionLimiter
{
    private static readonly object _lock = new();
    private static readonly Dictionary<string, int> _perAddress = new();
    private static int _total;

    private static int _maxConnections = 500;
    private static int _maxPerAddress = 5;

    public static void Configure(ServerConfig config)
    {
        _maxConnections = config.MaxConnections;
        _maxPerAddress = config.MaxConnectionsPerIp;
    }

    /// <summary>
    /// Key used for per-address limits (connections, /login attempts).
    /// IPv6 is grouped by /64: one customer usually owns a whole /64 and could rotate addresses inside it.
    /// </summary>
    public static string GetAddressKey(IPAddress? address)
    {
        if (address == null) return "unknown";

        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (address.AddressFamily != AddressFamily.InterNetworkV6) return address.ToString();

        byte[] bytes = address.GetAddressBytes();
        for (int i = 8; i < bytes.Length; i++) bytes[i] = 0;

        return new IPAddress(bytes) + "/64";
    }

    /// <param name="rejection">Which limit was hit, to tell the client; meaningless when true is returned.</param>
    public static bool TryAcquire(string addressKey, out MessageErrorType rejection)
    {
        lock (_lock)
        {
            _perAddress.TryGetValue(addressKey, out int count);

            if (_total >= _maxConnections)
            {
                rejection = MessageErrorType.ServerFull;
                return false;
            }

            if (count >= _maxPerAddress)
            {
                rejection = MessageErrorType.TooManyConnections;
                return false;
            }

            rejection = default;
            _perAddress[addressKey] = count + 1;
            _total++;
            return true;
        }
    }

    /// <summary>Call exactly once per successful <see cref="TryAcquire"/>.</summary>
    public static void Release(string addressKey)
    {
        lock (_lock)
        {
            if (!_perAddress.TryGetValue(addressKey, out int count)) return;

            // Remove empty entries, otherwise the dictionary grows with every address ever seen.
            if (count <= 1) _perAddress.Remove(addressKey);
            else _perAddress[addressKey] = count - 1;

            _total--;
        }
    }
}
