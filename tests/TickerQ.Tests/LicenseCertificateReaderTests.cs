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

    [Fact]
    public void Evaluate_V4FullWithNonAnnualTerm_IsInvalidAndBlocked()
    {
        var issuedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var state = Evaluate(Payload(4, LicenseKind.Full, LicensePlan.Business,
            issuedAt, issuedAt.AddDays(364), "5.x"), issuedAt.AddDays(1));

        Assert.Equal(TickerQLicenseStatus.Invalid, state.Status);
        Assert.False(state.ExecutionAllowed);
        Assert.Contains("invalid full-license term", state.Message);
    }

    [Fact]
    public void Evaluate_ExpiredV4Full_RemainsExecutableUnderAnchoredRights()
    {
        var issuedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var state = Evaluate(Payload(4, LicenseKind.Full, LicensePlan.Priority,
            issuedAt, issuedAt.AddYears(1), "5.x"), issuedAt.AddYears(1).AddSeconds(1));

        Assert.Equal(TickerQLicenseStatus.Expired, state.Status);
        Assert.True(state.ExecutionAllowed);
        Assert.Contains("anchored 5.x rights", state.Message);
    }

    [Fact]
    public void Evaluate_ExpiredV4Evaluation_BlocksExecution()
    {
        var issuedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var state = Evaluate(Payload(4, LicenseKind.Evaluation, LicensePlan.Business,
            issuedAt, issuedAt.AddDays(30), null), issuedAt.AddDays(31));

        Assert.Equal(TickerQLicenseStatus.Expired, state.Status);
        Assert.True(state.IsEvaluation);
        Assert.False(state.ExecutionAllowed);
    }

    [Fact]
    public void Evaluate_ExpiredLegacyTrial_BlocksExecution()
    {
        var issuedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var state = Evaluate(Payload(3, LicenseKind.Trial, LicensePlan.Business,
            issuedAt, issuedAt.AddDays(15), null), issuedAt.AddDays(16));

        Assert.Equal(TickerQLicenseStatus.Expired, state.Status);
        Assert.True(state.IsEvaluation);
        Assert.False(state.ExecutionAllowed);
    }

    [Fact]
    public void Evaluate_ExpiredLegacyFull_BlocksUnsignedFallbackRights()
    {
        var issuedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var state = Evaluate(Payload(3, LicenseKind.Full, LicensePlan.Business,
            issuedAt, issuedAt.AddYears(1), null), issuedAt.AddYears(1).AddSeconds(1));

        Assert.Equal(TickerQLicenseStatus.Expired, state.Status);
        Assert.False(state.ExecutionAllowed);
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

    [Fact]
    public void Evaluate_V4FullForDifferentFunctionalLine_IsInvalidAndBlocked()
    {
        var issuedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var state = Evaluate(Payload(4, LicenseKind.Full, LicensePlan.Business,
            issuedAt, issuedAt.AddYears(1), "6.x"), issuedAt.AddDays(1));

        Assert.Equal(TickerQLicenseStatus.Invalid, state.Status);
        Assert.False(state.ExecutionAllowed);
        Assert.Contains("invalid anchored minor line", state.Message);
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
