using System.Reflection;
using System.Text;
using TickerQ.Licensing;
using TickerQ.Utilities.Licensing;

namespace TickerQ.Tests.Licensing;

public sealed class LicenseCertificateReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Missing_certificate_fails_closed()
    {
        var root = Directory.CreateTempSubdirectory("tickerq-license-missing-");
        try
        {
            var state = LicenseCertificateReader.Read(null, root.FullName, Now);

            Assert.Equal(TickerQLicenseStatus.Missing, state.Status);
            Assert.False(state.ExecutionAllowed);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Production_signing_key_is_pinned_to_the_live_portal_authority()
    {
        var key = TrustedLicenseKeys.TryGetPublicKey("production-2026-01");

        Assert.NotNull(key);
        Assert.Equal(
            Convert.FromBase64String("1vSynYg0x6OFd/6/qb7ug5ERbZcTwHtz68pIO1vNQ14="),
            key);
    }

    [Fact]
    public void Development_signing_key_is_not_trusted_by_release_runtime()
    {
        Assert.Null(TrustedLicenseKeys.TryGetPublicKey("arcenox-development-1"));
    }

    [Fact]
    public void Runtime_binary_is_bound_to_the_commercial_functional_line()
    {
        var assembly = typeof(LicenseCertificateReader).Assembly;
        var functionalLine = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "TickerQFunctionalMinorLine")
            ?.Value;

        Assert.Equal("5.x", functionalLine);
        Assert.StartsWith("10.5.", assembly.GetName().Version?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_signing_key_fails_closed()
    {
        var root = Directory.CreateTempSubdirectory("tickerq-license-unknown-key-");
        try
        {
            File.WriteAllText(
                Path.Combine(root.FullName, "tickerq.tqlicense"),
                """
                {
                  "schemaVersion": 4,
                  "algorithm": "Ed25519",
                  "keyId": "unknown-production-key",
                  "payload": "e30",
                  "signature": "AA"
                }
                """,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var state = LicenseCertificateReader.Read(null, root.FullName, Now);

            Assert.Equal(TickerQLicenseStatus.Invalid, state.Status);
            Assert.False(state.ExecutionAllowed);
            Assert.Equal(
                "The TickerQ license certificate is invalid (untrusted signing key). TickerQ execution is disabled.",
                state.Message);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Expired_full_certificate_keeps_anchored_execution_rights()
    {
        var state = TickerQLicenseState.ForCertificate(
            TickerQLicenseStatus.Expired,
            isEvaluation: false,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "Fixture Workspace",
            "Business",
            "Full",
            Now.AddYears(-1),
            Now.AddDays(-1),
            -1,
            4,
            "Ed25519",
            "production-2026-01",
            "5.x",
            executionAllowedAfterExpiry: true);

        Assert.True(state.ExecutionAllowed);
        Assert.Contains("anchored 5.x rights", state.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Expired_evaluation_blocks_execution()
    {
        var state = TickerQLicenseState.ForCertificate(
            TickerQLicenseStatus.Expired,
            isEvaluation: true,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            "Fixture Workspace",
            "Business",
            "Evaluation",
            Now.AddDays(-31),
            Now.AddDays(-1),
            -1,
            4,
            "Ed25519",
            "production-2026-01",
            null);

        Assert.False(state.ExecutionAllowed);
        Assert.Contains("execution is disabled", state.Message, StringComparison.Ordinal);
    }
}
