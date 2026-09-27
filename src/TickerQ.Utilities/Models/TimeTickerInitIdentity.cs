using System;
using System.Security.Cryptography;
using System.Text;

namespace TickerQ.Utilities.Models;

internal static class TimeTickerInitIdentity
{
    private const string Namespace = "TickerQ.TimeTicker.Init.v1\n";

    internal static Guid DeterministicId(string initIdentifier)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(Namespace + initIdentifier));
        Span<byte> bytes = stackalloc byte[16];
        digest.AsSpan(0, 16).CopyTo(bytes);
        // RFC 4122 version/variant bits; namespace and full digest still determine the value.
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }
}
