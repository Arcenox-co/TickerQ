using System;

namespace TickerQ.Utilities.Compat;

/// <summary>Lowercase hexadecimal encoding; <c>Convert.ToHexStringLower</c> exists from .NET 9.</summary>
internal static class HexCompat
{
    public static string ToHexStringLower(ReadOnlySpan<byte> bytes)
#if NET9_0_OR_GREATER
        => Convert.ToHexStringLower(bytes);
#else
        => Convert.ToHexString(bytes).ToLowerInvariant();
#endif
}
