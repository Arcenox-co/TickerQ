#nullable disable
using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TickerQ.Caching.StackExchangeRedis.Helpers;

/// <summary>
/// Serializes <see cref="DateTime"/> using the round-trip ("O") format with a fixed
/// seven-digit fractional second so the on-wire representation is byte-stable.
/// <para>
/// This is required because the Lua CAS scripts (see <c>CasReplace.lua</c>/<c>Acquire.lua</c>)
/// compare the stored <c>UpdatedAt</c> string against <c>expectedUpdatedAt.ToString("O")</c> for
/// an EXACT match, and because <c>RecoverStale.lua</c> relies on lexicographic ordering of
/// fixed-width <c>LockedAt</c>/<c>LeaseUntil</c> timestamps. System.Text.Json's built-in converter
/// trims trailing fractional zeros (e.g. writes <c>12:00:00Z</c> instead of
/// <c>12:00:00.0000000Z</c>), which makes consecutive CAS writes silently fail and corrupts the
/// stale-recovery ordering. Emitting the canonical seven-digit form keeps the C# comparands and
/// the persisted JSON identical.
/// </para>
/// AOT/source-generation safe: purely string based, no reflection.
/// </summary>
internal sealed class CanonicalDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => DateTime.Parse(reader.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("O", CultureInfo.InvariantCulture));
}

/// <summary>
/// Nullable counterpart of <see cref="CanonicalDateTimeConverter"/>. Registered explicitly so the
/// canonical format is applied deterministically to every nullable timestamp field
/// (<c>LockedAt</c>, <c>ExecutedAt</c>, <c>LeaseUntil</c>, ...) regardless of source-gen wrapping.
/// </summary>
internal sealed class CanonicalNullableDateTimeConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        return DateTime.Parse(reader.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteStringValue(value.Value.ToString("O", CultureInfo.InvariantCulture));
        else
            writer.WriteNullValue();
    }
}
