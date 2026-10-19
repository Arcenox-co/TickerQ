using System.Text.Json;
using NSec.Cryptography;
using TickerQ.Licensing;
using TickerQ.Utilities.Licensing;

namespace TickerQ.Tests.Licensing;

public sealed class PaidCertificateTermTests
{
    private static readonly DateTimeOffset TermStart = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1.208617)]
    [InlineData(86400)]
    [InlineData(15552000)]
    public void Paid_v4_certificate_can_be_issued_after_annual_coverage_starts(double delaySeconds)
    {
        var issuedAt = TermStart.AddSeconds(delaySeconds);
        var expiresAt = TermStart.AddYears(1);
        var payload = PaidPayload(issuedAt, expiresAt);

        var state = ReadSigned(payload, issuedAt.AddMinutes(1));

        Assert.Equal(TickerQLicenseStatus.Active, state.Status);
        Assert.True(state.ExecutionAllowed);
        Assert.Equal(issuedAt, state.IssuedAt);
        Assert.Equal(expiresAt, state.ExpiresAt);
    }

    [Fact]
    public void Reissued_paid_certificate_uses_signed_expiry_without_extending_coverage()
    {
        var expiresAt = TermStart.AddYears(1);
        var payload = PaidPayload(TermStart.AddMonths(6), expiresAt);

        var state = ReadSigned(payload, expiresAt.AddSeconds(1));

        Assert.Equal(TickerQLicenseStatus.Expired, state.Status);
        Assert.Equal(expiresAt, state.ExpiresAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(0)]
    public void Paid_v4_still_requires_expiry_later_than_issuance(int? secondsAfterIssuance)
    {
        var payload = PaidPayload(TermStart,
            secondsAfterIssuance.HasValue ? TermStart.AddSeconds(secondsAfterIssuance.Value) : null);

        var state = ReadSigned(payload, TermStart.AddMinutes(1));

        Assert.Equal(TickerQLicenseStatus.Invalid, state.Status);
        Assert.False(state.ExecutionAllowed);
    }

    [Theory]
    [InlineData(2, 0, 364)]
    [InlineData(3, 0, 364)]
    [InlineData(3, 1, 14)]
    [InlineData(4, 2, 29)]
    public void Legacy_and_evaluation_certificate_terms_remain_strict(int schema, int kind, int days)
    {
        var payload = PaidPayload(TermStart, TermStart.AddDays(days));
        payload.SchemaVersion = schema;
        payload.LicenseKind = (LicenseKind)kind;
        payload.AnchoredMinorLine = null!;

        var state = ReadSigned(payload, TermStart.AddMinutes(1));

        Assert.Equal(TickerQLicenseStatus.Invalid, state.Status);
        Assert.False(state.ExecutionAllowed);
    }

    private static LicensePayload PaidPayload(DateTimeOffset issuedAt, DateTimeOffset? expiresAt) => new()
    {
        LicenseId = Guid.NewGuid(),
        WorkspaceId = Guid.NewGuid(),
        WorkspaceName = "Paid term fixture",
        Plan = LicensePlan.Business,
        LicenseKind = LicenseKind.Full,
        SchemaVersion = 4,
        KeyId = "paid-term-test-key",
        AnchoredMinorLine = "5.x",
        IssuedAt = issuedAt,
        RuntimeExpiresAt = expiresAt
    };

    private static TickerQLicenseState ReadSigned(LicensePayload payload, DateTimeOffset observedAt)
    {
        var algorithm = SignatureAlgorithm.Ed25519;
        using var key = Key.Create(algorithm);
        var publicKey = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, LicenseJsonContext.Default.LicensePayload);
        var envelope = new LicenseEnvelope
        {
            SchemaVersion = payload.SchemaVersion,
            KeyId = payload.KeyId,
            Algorithm = "Ed25519",
            Payload = Encode(bytes),
            Signature = Encode(algorithm.Sign(key, bytes))
        };

        return LicenseCertificateReader.EvaluateForTests(
            JsonSerializer.SerializeToUtf8Bytes(envelope, LicenseJsonContext.Default.LicenseEnvelope),
            observedAt, id => id == payload.KeyId ? publicKey : null!);
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
