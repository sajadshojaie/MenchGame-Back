using System.Net;

namespace MenchGameBackend.Helper;

public static class CorsHelper
{
    public static bool IsAllowedOrigin(string origin, string[] extra)
    {
        if (extra.Contains(origin, StringComparer.OrdinalIgnoreCase)) return true;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        return IPAddress.TryParse(uri.Host, out var ip) && IsPrivate(ip);
    }

    public static bool IsPrivate(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 4) return false;
        return bytes[0] == 10
               || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
               || (bytes[0] == 192 && bytes[1] == 168);
    }
}