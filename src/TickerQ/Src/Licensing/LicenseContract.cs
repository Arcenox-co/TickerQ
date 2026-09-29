using System;
using System.Text.Json.Serialization;

namespace TickerQ.Licensing
{
    /// <summary>
    /// Plan codes as frozen by the licensing portal. The ordinal values are part of the signed payload
    /// contract (the portal serializes the enum as a number) and must never be reordered.
    /// </summary>
    internal enum LicensePlan
    {
        Community,
        Business,
        Priority,
        Enterprise,
    }

    /// <summary>
    /// License kinds as frozen by the licensing portal. Schema v2 payloads omit the field and are always
    /// read as <see cref="Full"/>. The ordinal values are part of the signed payload contract.
    /// </summary>
    internal enum LicenseKind
    {
        Full,
        Trial,
        Evaluation,
    }

    /// <summary>
    /// The on-disk <c>.tqlicense</c> shape: an indented JSON <c>SignedLicenseEnvelope</c> with camelCase
    /// members. <see cref="Payload"/> and <see cref="Signature"/> are Base64Url-encoded.
    /// </summary>
    internal sealed class LicenseEnvelope
    {
        public int SchemaVersion { get; set; }
        public string Algorithm { get; set; }
        public string KeyId { get; set; }
        public string Payload { get; set; }
        public string Signature { get; set; }
    }

    /// <summary>
    /// The minimal signed payload fields TickerQ reads. Historical certificates may carry additional
    /// trailing commercial claims; those are ignored here — runtime expiry is the temporal authority and
    /// the anchored minor line is the only extra field a paid v4 Full certificate must carry.
    /// </summary>
    internal sealed class LicensePayload
    {
        public Guid LicenseId { get; set; }
        public Guid WorkspaceId { get; set; }
        public string WorkspaceName { get; set; }
        public LicensePlan Plan { get; set; }
        public DateTimeOffset IssuedAt { get; set; }
        public DateTimeOffset? RuntimeExpiresAt { get; set; }
        public string KeyId { get; set; }
        public int SchemaVersion { get; set; }
        public LicenseKind LicenseKind { get; set; }
        public string AnchoredMinorLine { get; set; }
    }

    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString)]
    [JsonSerializable(typeof(LicenseEnvelope))]
    [JsonSerializable(typeof(LicensePayload))]
    internal partial class LicenseJsonContext : JsonSerializerContext
    {
    }

    /// <summary>Base64Url codec matching the portal's encoding of the payload and signature.</summary>
    internal static class LicenseBase64Url
    {
        public static byte[] Decode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new FormatException("Empty base64url value.");
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded += (padded.Length % 4) switch
            {
                2 => "==",
                3 => "=",
                0 => "",
                _ => throw new FormatException("Invalid base64url length."),
            };
            return Convert.FromBase64String(padded);
        }
    }
}
