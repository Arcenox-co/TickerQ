using System.Text.Json;
using TickerQ.Licensing;
using TickerQ.Utilities.Licensing;

namespace TickerQ.Tests.Licensing;

public sealed class PortalGoldenFixtureTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly string FixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Licensing");

    [Theory]
    [InlineData("active-business-full-v4.tqlicense", TickerQLicenseStatus.Active, true, false, "Business", "Full")]
    [InlineData("expired-business-full-v4.tqlicense", TickerQLicenseStatus.Expired, true, false, "Business", "Full")]
    [InlineData("active-evaluation-v4.tqlicense", TickerQLicenseStatus.Expiring, true, true, "Business", "Evaluation")]
    [InlineData("expired-evaluation-v4.tqlicense", TickerQLicenseStatus.Expired, false, true, "Business", "Evaluation")]
    public void Portal_generated_fixture_matches_package_contract(
        string fixture,
        TickerQLicenseStatus expectedStatus,
        bool executionAllowed,
        bool isEvaluation,
        string plan,
        string kind)
    {
        var state = ReadFixture(fixture);

        Assert.Equal(expectedStatus, state.Status);
        Assert.Equal(executionAllowed, state.ExecutionAllowed);
        Assert.Equal(isEvaluation, state.IsEvaluation);
        Assert.Equal(plan, state.Plan);
        Assert.Equal(kind, state.Kind);
        Assert.Equal(4, state.SchemaVersion);
        Assert.Equal("tickerq-golden-fixture-2026-09", state.KeyId);
    }

    [Fact]
    public void Tampered_portal_fixture_fails_signature_verification()
    {
        var source = File.ReadAllText(Path.Combine(FixtureRoot, "active-business-full-v4.tqlicense"));
        using var document = JsonDocument.Parse(source);
        var root = document.RootElement;
        var payload = root.GetProperty("payload").GetString()!;
        var tamperedPayload = (payload[0] == 'A' ? 'B' : 'A') + payload[1..];
        var tampered = JsonSerializer.Serialize(new
        {
            schemaVersion = root.GetProperty("schemaVersion").GetInt32(),
            algorithm = root.GetProperty("algorithm").GetString(),
            keyId = root.GetProperty("keyId").GetString(),
            payload = tamperedPayload,
            signature = root.GetProperty("signature").GetString(),
        });
        var path = Path.Combine(Path.GetTempPath(), $"tickerq-tampered-{Guid.NewGuid():N}.tqlicense");
        try
        {
            File.WriteAllText(path, tampered);
            var state = LicenseCertificateReader.Read(path, string.Empty, ObservedAt, ResolveFixtureKey);

            Assert.Equal(TickerQLicenseStatus.Invalid, state.Status);
            Assert.False(state.ExecutionAllowed);
            Assert.Contains("signature mismatch", state.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static TickerQLicenseState ReadFixture(string name) =>
        LicenseCertificateReader.Read(Path.Combine(FixtureRoot, name), string.Empty, ObservedAt, ResolveFixtureKey);

    private static byte[] ResolveFixtureKey(string keyId)
    {
        using var authority = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FixtureRoot, "fixture-authority.json")));
        return string.Equals(keyId, authority.RootElement.GetProperty("keyId").GetString(), StringComparison.Ordinal)
            ? Convert.FromBase64String(authority.RootElement.GetProperty("publicKey").GetString()!)
            : null!;
    }
}
