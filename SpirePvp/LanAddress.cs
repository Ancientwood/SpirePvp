using System.Globalization;
using System.Net;
using System.Security.Cryptography;

namespace SpirePvp;

public static class LanAddress
{
    public const ushort DefaultPort = 33771;
    public const ulong ClientId = 1000;

    public static ulong CreateNativeClientId()
    {
        return (BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong))) & 0x3FFF_FFFF_FFFF_FFFFUL)
            | 0x4000_0000_0000_0000UL;
    }

    public static bool TryParsePort(string? text, out ushort port)
    {
        return ushort.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out port)
            && port != 0;
    }

    public static bool TryParseEndpoint(string? text, out string address, out ushort port)
    {
        address = string.Empty;
        port = DefaultPort;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string host = text.Trim();
        int separator = host.IndexOf(':');
        if (separator >= 0)
        {
            if (separator != host.LastIndexOf(':') || !TryParsePort(host[(separator + 1)..], out port)) return false;
            host = host[..separator].Trim();
        }
        string[] parts = host.Split('.');
        if (parts.Length != 4) return false;
        var bytes = new byte[4];
        for (int index = 0; index < parts.Length; index++)
        {
            if (!byte.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out bytes[index]))
                return false;
        }
        if (bytes[0] == 0 || bytes[0] >= 224) return false;
        address = new IPAddress(bytes).ToString();
        return true;
    }

}