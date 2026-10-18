using System.Text.Json;
using NSec.Cryptography;
using TickerQ.Licensing;
using TickerQ.Utilities.Licensing;

namespace TickerQ.Tests;

public sealed class LicenseCertificateReaderTests : IDisposable
{
    private const string KeyId = "ephemeral-test-key";
    private readonly SignatureAlgorithm _algorithm = SignatureAlgorithm.Ed25519;
    private readonly Key _key;
    private readonly byte[] _publicKey;

    public LicenseCertificateReaderTests()
    {
        _key = Key.Create(_algorithm, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport,
        });
        _publicKey = _key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
    }

    [Fact]
    public void Evaluate_V4FullWithExactOneYearTerm_IsActiveAndExecutable()
    {
        var issuedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var state = Evaluate(Payload(4, LicenseKind.Full, LicensePlan.Business,
            issuedAt, issuedAt.AddYears(1), "5.x"), issuedAt.AddDays(1));

        Assert.Equal(TickerQLicenseStatus.Active, state.Status);
        Assert.True(state.ExecutionAllowed);
        Assert.False(state.IsEvaluation);
    }

    [Theory]
    [InlineData(0, 12086162, TickerQLicenseStatus.Active)]
    [InlineData(1, 0, TickerQLicenseStatus.Active)]
    [InlineData(180, 0, TickerQLicenseStatus.Active)]
    [InlineData(364, 0, TickerQLicenseStatus.Expiring)]
    public void Evaluate_V4FullIssuedAfterPayment_UsesSignedSubscriptionEnd(
        int delayDays, int delayTicks, TickerQLicenseStatus expectedStatus)
    {
        var paidAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var issuedAt = paidAt.AddDays(delayDays).AddTicks(delayTicks);
        var subscriptionEnd = paidAt.AddYears(1);

        var state = Evaluate(Payload(4, LicenseKind.Full, LicensePlan.Business,
            issuedAt, subscriptionEnd, "5.x"), issuedAt.AddSeconds(1));

        Assert.Equal(expectedStatus, state.Status);
        Assert.True(state.ExecutionAllowed);
        Assert.Equal(subscriptionEnd, state.ExpiresAt);
        Assert.Equal(issuedAt, state.IssuedAt);
    }

    [Fact]
    public void Evaluate_ReissuedV4FullExpiresAtOriginalSubscriptionEnd()
    {
        var paidAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var subscriptionEnd = paidAt.AddYears(1);
        var state = Evaluate(Payload(4, LicenseKind.Full, LicensePlan.Priority,
            paidAt.AddMonths(6), subscriptionEnd, "5.x"), subscriptionEnd);

        Assert.Equal(TickerQLicenseStatus.Expired, state.Status);
        Assert.True(state.ExecutionAllowed);
        Assert.Equal(subscriptionEnd, state.ExpiresAt);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void Evaluate_V4FullWithExpiryAtOrBeforeIssue_RemainsInvalid(int expiryOffsetSeconds)
    {
        var issuedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var state = Evaluate(Payload(4, LicenseKind.Full, LicensePlan.Business,
            issuedAt, issuedAt.AddSeconds(expiryOffsetSeconds), "5.x"), issuedAt);

        Assert.Equal(TickerQLicenseStatus.Invalid, state.Status);
        Assert.False(state.ExecutionAllowed);
        Assert.Contains("incoherent certificate dates", state.Message);
    }

    [Fact]
    public void Evaluate_V4FullWithoutExpiry_RemainsInvalid()
    {
        var issuedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var state = Evaluate(Payload(4, LicenseKind.Full, LicensePlan.Business,
            issuedAt, null, "5.x"), issuedAt);

        Assert.Equal(TickerQLicenseStatus.Invalid, state.Status);
        Assert.False(state.ExecutionAllowed);
        Assert.Contains("missing runtime expiry", state.Message);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Evaluate_LegacyFullWithNonAnnualTerm_RemainsInvalid(int schema)
    {
        var issuedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var state = Evaluate(Payload(schema, LicenseKind.Full, LicensePlan.Business,
            issuedAt, issuedAt.AddDays(364), null), issuedAt.AddDays(1));

        Assert.Equal(TickerQLicenseStatus.Invalid, state.Status);
        Assert.False(state.ExecutionAllowed);
        Assert.Contains("invalid full-license term", state.Message);
    }

    [Fact]
    public void Evaluate_ExpiredV4Full_PreservesRuntimeWithoutClaimingFallbackEntitlement()
    {
        var issuedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var state = Evaluate(Payload(4, LicenseKind.Full, LicensePlan.Priority,
            issuedAt, issuedAt.AddYears(1), "5.x"), issuedAt.AddYears(1).AddSeconds(1));

        Assert.Equal(TickerQLicenseStatus.Expired, state.Status);
        Assert.True(state.ExecutionAllowed);
        Assert.Contains("runtime continuity only", state.Message);
        Assert.Contains("does not verify fallback entitlement", state.Message);
    }

    [Fact]
    public void Evaluate_ExpiredV4Evaluation_PreservesRuntimeWithNoProductionRights()
    {
        var issuedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var state = Evaluate(Payload(4, LicenseKind.Evaluation, LicensePlan.Business,
            issuedAt, issuedAt.AddDays(30), null), issuedAt.AddDays(31));

        Assert.Equal(TickerQLicenseStatus.Expired, state.Status);
        Assert.True(state.IsEvaluation);
        Assert.True(state.ExecutionAllowed);
        Assert.Contains("Evaluation rights have ended", state.Message);
        Assert.Contains("never permits production", state.Message);
    }

    [Fact]
    public void Evaluate_ExpiredLegacyTrial_PreservesRuntimeContinuity()
    {
        var issuedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var state = Evaluate(Payload(3, LicenseKind.Trial, LicensePlan.Business,
            issuedAt, issuedAt.AddDays(15), null), issuedAt.AddDays(16));

        Assert.Equal(TickerQLicenseStatus.Expired, state.Status);
        Assert.True(state.IsEvaluation);
        Assert.True(state.ExecutionAllowed);
    }

    [Fact]
    public void Evaluate_ExpiredLegacyFull_PreservesRuntimeWithoutClaimingFallbackEntitlement()
    {
        var issuedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var state = Evaluate(Payload(3, LicenseKind.Full, LicensePlan.Business,
            issuedAt, issuedAt.AddYears(1), null), issuedAt.AddYears(1).AddSeconds(1));

        Assert.Equal(TickerQLicenseStatus.Expired, state.Status);
        Assert.True(state.ExecutionAllowed);
        Assert.Contains("does not verify fallback entitlement", state.Message);
    }

    [Fact]
    public void Evaluate_ActiveLegacyFull_RemainsExecutableDuringSignedTerm()
    {
        var issuedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var state = Evaluate(Payload(3, LicenseKind.Full, LicensePlan.Business,
            issuedAt, issuedAt.AddYears(1), null), issuedAt.AddDays(1));

        Assert.Equal(TickerQLicenseStatus.Active, state.Status);
        Assert.True(state.ExecutionAllowed);
    }

    [Theory]
    [InlineData(1, TickerQLicenseStatus.Active)]
    [InlineData(366, TickerQLicenseStatus.Expired)]
    public void Evaluate_V4FullForDifferentFunctionalLine_DoesNotBlockRuntime(int days, TickerQLicenseStatus expectedStatus)
    {
        var issuedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var state = Evaluate(Payload(4, LicenseKind.Full, LicensePlan.Business,
            issuedAt, issuedAt.AddYears(1), "6.x"), issuedAt.AddDays(days));

        Assert.Equal(expectedStatus, state.Status);
        Assert.True(state.ExecutionAllowed);
        Assert.Equal("6.x", state.AnchoredMinorLine);
        if (expectedStatus == TickerQLicenseStatus.Expired)
            Assert.Contains("does not verify fallback entitlement", state.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("5.0")]
    [InlineData("05.x")]
    [InlineData("0.x")]
    [InlineData("-1.x")]
    [InlineData(" 5.x")]
    [InlineData("5.x ")]
    public void Evaluate_V4FullWithMalformedAnchor_RemainsInvalid(string? anchor)
    {
        var issuedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var state = Evaluate(Payload(4, LicenseKind.Full, LicensePlan.Business,
            issuedAt, issuedAt.AddYears(1), anchor), issuedAt.AddDays(1));

        Assert.Equal(TickerQLicenseStatus.Invalid, state.Status);
        Assert.False(state.ExecutionAllowed);
        Assert.Contains("invalid anchored minor line", state.Message);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Evaluate_ExpiredCommunity_PreservesRuntimeWithoutFallbackRights(int schema)
    {
        var issuedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var state = Evaluate(Payload(schema, LicenseKind.Full, LicensePlan.Community,
            issuedAt, issuedAt.AddYears(1), null), issuedAt.AddYears(1));

        Assert.Equal(TickerQLicenseStatus.Expired, state.Status);
        Assert.True(state.ExecutionAllowed);
        Assert.False(state.IsEvaluation);
        Assert.Contains("Community has no fallback rights", state.Message);
        Assert.Equal(TickerQLicenseState.RenewCertificateUrl, state.ActionUrl);
    }

    [Theory]
    [InlineData(4, (int)LicenseKind.Evaluation, (int)LicensePlan.Business, 30, null)]
    [InlineData(3, (int)LicenseKind.Full, (int)LicensePlan.Community, 365, null)]
    [InlineData(4, (int)LicenseKind.Full, (int)LicensePlan.Business, 365, "5.x")]
    [InlineData(4, (int)LicenseKind.Full, (int)LicensePlan.Business, 365, "6.x")]
    public void Read_ExpiredCertificateOnRestart_PreservesRuntime(
        int schema, int kind, int plan, int termDays, string? anchor)
    {
        var issuedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var expiresAt = issuedAt.AddDays(termDays);
        var root = Directory.CreateTempSubdirectory("tickerq-license-restart-");
        try
        {
            File.WriteAllBytes(Path.Combine(root.FullName, LicenseCertificateReader.DefaultFileName),
                CreateCertificate(Payload(schema, (LicenseKind)kind, (LicensePlan)plan, issuedAt, expiresAt, anchor)));

            var state = LicenseCertificateReader.Read(null, root.FullName, expiresAt.AddSeconds(1),
                candidate => candidate == KeyId ? _publicKey : null!);

            Assert.Equal(TickerQLicenseStatus.Expired, state.Status);
            Assert.True(state.ExecutionAllowed);
            Assert.Contains("runtime continuity only", state.Message);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Evaluate_CertificateIssuedInFuture_IsInvalidAndBlocked()
    {
        var observedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var issuedAt = observedAt.AddMinutes(1);

        var state = Evaluate(Payload(4, LicenseKind.Full, LicensePlan.Business,
            issuedAt, issuedAt.AddYears(1), "5.x"), observedAt);

        Assert.Equal(TickerQLicenseStatus.Invalid, state.Status);
        Assert.False(state.ExecutionAllowed);
        Assert.Contains("not yet valid", state.Message);
    }

    [Fact]
    public void Evaluate_TamperedSignedPayload_IsInvalidAndBlocked()
    {
        var issuedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var certificate = CreateCertificate(Payload(4, LicenseKind.Full, LicensePlan.Business,
            issuedAt, issuedAt.AddYears(1), "5.x"));
        certificate[^1] ^= 0x01;

        var state = Evaluate(certificate, issuedAt.AddDays(1));

        Assert.Equal(TickerQLicenseStatus.Invalid, state.Status);
        Assert.False(state.ExecutionAllowed);
    }

    [Fact]
    public void Evaluate_UntrustedKeyId_IsInvalidAndBlocked()
    {
        var issuedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var certificate = CreateCertificate(Payload(4, LicenseKind.Full, LicensePlan.Business,
            issuedAt, issuedAt.AddYears(1), "5.x"));

        var state = LicenseCertificateReader.EvaluateForTests(certificate, issuedAt.AddDays(1), _ => null!);

        Assert.Equal(TickerQLicenseStatus.Invalid, state.Status);
        Assert.False(state.ExecutionAllowed);
        Assert.Contains("untrusted signing key", state.Message);
    }

    [Fact]
    public void Read_OversizedCertificate_IsInvalidAndBlocked()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tickerq-license-size-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            const string fileName = "oversized.tqlicense";
            File.WriteAllBytes(Path.Combine(directory, fileName), new byte[(128 * 1024) + 1]);

            var state = LicenseCertificateReader.Read(
                fileName,
                directory,
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

            Assert.Equal(TickerQLicenseStatus.Invalid, state.Status);
            Assert.False(state.ExecutionAllowed);
            Assert.Contains("too large", state.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private TickerQLicenseState Evaluate(LicensePayload payload, DateTimeOffset now)
        => Evaluate(CreateCertificate(payload), now);

    private TickerQLicenseState Evaluate(byte[] certificate, DateTimeOffset now)
        => LicenseCertificateReader.EvaluateForTests(
            certificate,
            now,
            candidate => candidate == KeyId ? _publicKey : null!);

    private byte[] CreateCertificate(LicensePayload payload)
    {
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, LicenseJsonContext.Default.LicensePayload);
        var signature = _algorithm.Sign(_key, payloadBytes);
        var envelope = new LicenseEnvelope
        {
            SchemaVersion = payload.SchemaVersion,
            Algorithm = "Ed25519",
            KeyId = payload.KeyId,
            Payload = Encode(payloadBytes),
            Signature = Encode(signature),
        };
        return JsonSerializer.SerializeToUtf8Bytes(envelope, LicenseJsonContext.Default.LicenseEnvelope);
    }

    private static LicensePayload Payload(
        int schemaVersion,
        LicenseKind kind,
        LicensePlan plan,
        DateTimeOffset issuedAt,
        DateTimeOffset? expiresAt,
        string? anchoredMinorLine)
        => new()
        {
            LicenseId = Guid.NewGuid(),
            WorkspaceId = Guid.NewGuid(),
            WorkspaceName = "Test Workspace",
            Plan = plan,
            IssuedAt = issuedAt,
            RuntimeExpiresAt = expiresAt,
            KeyId = KeyId,
            SchemaVersion = schemaVersion,
            LicenseKind = kind,
            AnchoredMinorLine = anchoredMinorLine!,
        };

    private static string Encode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public void Dispose() => _key.Dispose();
}
