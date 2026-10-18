using System.Net;
using System.Net.Sockets;

namespace TickerQ.Notifications.Infrastructure;

internal static class WebhookSecurity
{
    internal static Uri ValidateUrl(string? value, NotificationOptions options)
    {
        if (value == null || value.Length > 4096 || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Webhook URL must be an absolute HTTPS URL without user information or a fragment.");
        if (options.AllowedHosts.Count > 0 && !options.AllowedHosts.Contains(uri.DnsSafeHost, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Webhook host is not on the server's allowed-host list.");
        if (!options.AllowPrivateEndpoints && (uri.IsLoopback || uri.DnsSafeHost.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(uri.DnsSafeHost, out var ip) && !IsPublic(ip))))
            throw new ArgumentException("Private and loopback webhook destinations are disabled by the host.");
        return uri;
    }

    internal static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) return IsPublic(ip.MapToIPv4());
        if (IPAddress.IsLoopback(ip)) return false;
        var b = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
            return b[0] != 0 && b[0] != 10 && b[0] != 127 && b[0] < 224
                && !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] is >= 16 and <= 31)
                && !(b[0] == 192 && (b[1] == 168 || b[1] == 0))
                && !(b[0] == 100 && b[1] is >= 64 and <= 127)
                && !(b[0] == 198 && b[1] is 18 or 19);
        // Global unicast only; disallow transition prefixes that embed another address.
        return ip.AddressFamily == AddressFamily.InterNetworkV6 && (b[0] & 0xe0) == 0x20
            && !(b[0] == 0x20 && b[1] == 0x02)
            && !(b[0] == 0x20 && b[1] == 0x01 && b[2] == 0 && b[3] == 0);
    }

    internal static SocketsHttpHandler CreateHandler(NotificationOptions options) => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        ConnectCallback = async (context, ct) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct).ConfigureAwait(false);
            if (addresses.Length == 0 || (!options.AllowPrivateEndpoints && addresses.Any(ip => !IsPublic(ip))))
                throw new HttpRequestException("Webhook DNS resolved to a disallowed address.");
            // Connect to a validated IP, not the hostname: DNS cannot change between validation and connection.
            foreach (var address in addresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (SocketException) { socket.Dispose(); }
                catch { socket.Dispose(); throw; }
            }
            throw new HttpRequestException("Could not connect to webhook destination.");
        }
    };
}
