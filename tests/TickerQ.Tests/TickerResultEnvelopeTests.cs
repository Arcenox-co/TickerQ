using System;
using TickerQ.Utilities.Models;
using Xunit;

namespace TickerQ.Tests;

public class TickerResultEnvelopeTests
{
    [Fact]
    public void Constructor_StoresMetadata_AndDefensivelyCopiesPayload()
    {
        var payload = new byte[] { 1, 2, 3 };
        var envelope = new TickerResultEnvelope(payload, TickerResultEnvelope.CurrentVersion, "application/json", "contract-1", "My.Type");

        Assert.Equal(TickerResultEnvelope.CurrentVersion, envelope.Version);
        Assert.Equal("application/json", envelope.MediaType);
        Assert.Equal("contract-1", envelope.ContractId);
        Assert.Equal("My.Type", envelope.ContractType);
        Assert.Equal(3, envelope.PayloadLength);

        // Mutating the source array must not affect the stored payload.
        payload[0] = 99;
        Assert.Equal(1, envelope.ToPayloadArray()[0]);
    }

    [Fact]
    public void ToPayloadArray_ReturnsIndependentCopy_EachCall()
    {
        var envelope = new TickerResultEnvelope(new byte[] { 5 }, 1, "application/json");
        var first = envelope.ToPayloadArray();
        first[0] = 42;
        Assert.Equal(5, envelope.ToPayloadArray()[0]);
    }

    [Fact]
    public void Constructor_AllowsEmptyPayload_ForJsonNull()
    {
        var envelope = new TickerResultEnvelope(Array.Empty<byte>(), 1, "application/json");
        Assert.Equal(0, envelope.PayloadLength);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_Rejects_NonPositiveVersion(int version)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new TickerResultEnvelope(new byte[] { 1 }, version, "application/json"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Rejects_BlankMediaType(string mediaType)
        => Assert.Throws<ArgumentException>(
            () => new TickerResultEnvelope(new byte[] { 1 }, 1, mediaType));

    [Fact]
    public void Constructor_Rejects_NullPayload()
        => Assert.Throws<ArgumentNullException>(
            () => new TickerResultEnvelope(null, 1, "application/json"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Rejects_BlankContractIdentity_WhenProvided(string blank)
    {
        Assert.Throws<ArgumentException>(
            () => new TickerResultEnvelope(new byte[] { 1 }, 1, "application/json", contractId: blank));
        Assert.Throws<ArgumentException>(
            () => new TickerResultEnvelope(new byte[] { 1 }, 1, "application/json", contractType: blank));
    }

    [Fact]
    public void EnsureSupportedVersion_FailsClosed_OnUnknownVersion()
    {
        var future = new TickerResultEnvelope(new byte[] { 1 }, TickerResultEnvelope.CurrentVersion + 1, "application/json");
        Assert.False(future.IsSupportedVersion);
        Assert.Throws<NotSupportedException>(() => future.EnsureSupportedVersion());
    }

    [Fact]
    public void EnsureSupportedVersion_Passes_OnCurrentVersion()
    {
        var envelope = new TickerResultEnvelope(new byte[] { 1 }, TickerResultEnvelope.CurrentVersion, "application/json");
        Assert.True(envelope.IsSupportedVersion);
        envelope.EnsureSupportedVersion();
    }
}
