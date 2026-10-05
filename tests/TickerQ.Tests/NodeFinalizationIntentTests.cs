using System.Text;
using System.Text.Json;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public sealed class NodeFinalizationIntentTests
{
    private readonly Guid _tickerId = Guid.NewGuid();
    private readonly Guid _token = Guid.NewGuid();
    private readonly Guid _dispatch = Guid.NewGuid();
    private readonly Guid _epoch = Guid.NewGuid();
    private readonly Guid _control = Guid.NewGuid();

    [Fact]
    public void Constructor_RejectsFinalizeUriQuery() =>
        Assert.Throws<ArgumentException>(() => Create(uri: "https://node.example/finalize?secret=value", path: "/finalize?secret=value"));

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void Constructor_RejectsMalformedOrWrongShapeBody(string body) =>
        Assert.Throws<ArgumentException>(() => Create(body: Encoding.UTF8.GetBytes(body)));

    [Fact]
    public void Constructor_RejectsEveryIdentityMismatch()
    {
        Assert.Throws<ArgumentException>(() => Create(body: Body(tickerType: TickerType.CronTickerOccurrence)));
        Assert.Throws<ArgumentException>(() => Create(body: Body(tickerId: Guid.NewGuid())));
        Assert.Throws<ArgumentException>(() => Create(body: Body(token: Guid.NewGuid())));
        Assert.Throws<ArgumentException>(() => Create(body: Body(dispatch: Guid.NewGuid())));
        Assert.Throws<ArgumentException>(() => Create(body: Body(epoch: Guid.NewGuid())));
        Assert.Throws<ArgumentException>(() => Create(body: Body(control: Guid.NewGuid())));
    }

    [Fact]
    public void Constructor_RejectsDuplicateUndefinedNullAndSecretBearingFields()
    {
        var valid = Encoding.UTF8.GetString(Body());
        Assert.Throws<ArgumentException>(() => Create(body: Encoding.UTF8.GetBytes(valid.Replace("\"tickerId\":", "\"tickerId\":\"00000000-0000-0000-0000-000000000001\",\"tickerId\":"))));
        Assert.Throws<ArgumentException>(() => Create(body: Encoding.UTF8.GetBytes(valid.Replace("\"tickerType\":1", "\"tickerType\":999"))));
        Assert.Throws<ArgumentException>(() => Create(body: Encoding.UTF8.GetBytes(valid.Replace($"\"tickerId\":\"{_tickerId}\"", "\"tickerId\":null"))));
        Assert.Throws<ArgumentException>(() => Create(body: Encoding.UTF8.GetBytes(valid[..^1] + ",\"clientSecret\":\"must-not-persist\"}")));
    }

    [Fact]
    public void Constructor_RequiresExactNonEmptyGuidStrings()
    {
        var valid = Encoding.UTF8.GetString(Body());
        Assert.Throws<ArgumentException>(() => Create(body: Encoding.UTF8.GetBytes(valid.Replace($"\"tickerId\":\"{_tickerId}\"", $"\"tickerId\":\"{{{_tickerId}}}\""))));
        Assert.Throws<ArgumentException>(() => Create(body: Encoding.UTF8.GetBytes(valid.Replace($"\"tickerId\":\"{_tickerId}\"", "\"tickerId\":\"00000000-0000-0000-0000-000000000000\""))));
    }

    [Fact]
    public void Constructor_DefensivelyCopiesInputAndOutput()
    {
        var source = Body();
        var expected = (byte[])source.Clone();
        var intent = Create(body: source);
        source[0] ^= 0xff;
        var first = intent.ExactBody;
        Assert.Equal(expected, first);
        first[0] ^= 0xff;
        Assert.Equal(expected, intent.ExactBody);
    }

    private byte[] Body(TickerType tickerType = TickerType.TimeTicker, Guid? tickerId = null, Guid? token = null,
        Guid? dispatch = null, Guid? epoch = null, Guid? control = null) => JsonSerializer.SerializeToUtf8Bytes(new
        {
            tickerType, tickerId = tickerId ?? _tickerId, acquisitionToken = token ?? _token,
            dispatchId = dispatch ?? _dispatch, nodeEpoch = epoch ?? _epoch, controlNonce = control ?? _control
        });

    private NodeFinalizationIntent Create(byte[]? body = null, string uri = "https://node.example/finalize", string path = "/finalize") =>
        new(1, _dispatch, TickerType.TimeTicker, _tickerId, _token, _dispatch, _epoch,
            uri, path, false, Guid.NewGuid(), _control, body ?? Body(), DateTime.UtcNow);
}