using System.IO.Compression;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Hangfire.Common;

namespace TickerQ.Benchmarks.Comparisons;

/// <summary>
/// Compares job/request serialization approaches:
/// - TickerQ: System.Text.Json + optional GZip (UTF-8 bytes)
/// - Hangfire: Newtonsoft.Json (via SerializationHelper) for Job expression trees
/// - Quartz: JobDataMap (dictionary-based, no serialization for RAM store)
///
/// This benchmarks the data serialization path, not the job definition itself.
///
/// Results Overview (Apple M4 Pro, .NET 10.0):
/// ┌──────────────────────┬─────────────────────┬──────────────────────────┬─────────────────────────┐
/// │ Operation            │ TickerQ (STJ)       │ Hangfire (Newtonsoft)     │ Speedup                 │
/// ├──────────────────────┼─────────────────────┼──────────────────────────┼─────────────────────────┤
/// │ Serialize small      │ 145 ns / 464 B      │ 308 ns / 1,952 B         │ 2.1x faster, 4.2x less  │
/// │ Serialize medium     │ 913 ns / 2 KB       │ 2,054 ns / 9 KB          │ 2.3x faster, 4.4x less  │
/// │ Deserialize small    │ 290 ns / 800 B      │ 536 ns / 3,224 B         │ 1.8x faster, 4x less    │
/// │ Deserialize medium   │ 2,156 ns / 9 KB     │ 3,729 ns / 11.6 KB       │ 1.7x faster, 1.3x less  │
/// └──────────────────────┴─────────────────────┴──────────────────────────┴─────────────────────────┘
/// Winner: TickerQ (System.Text.Json) — 1.7-2.3x faster, up to 4.2x less memory.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.HostProcess)]
public class JobSerializationComparison
{
    private SampleRequest _smallRequest = null!;
    private SampleRequest _mediumRequest = null!;
    private byte[] _tickerqSmallBytes = null!;
    private byte[] _tickerqMediumBytes = null!;
    private byte[] _tickerqSmallGzip = null!;
    private byte[] _tickerqMediumGzip = null!;
    private string _hangfireSmallJson = null!;
    private string _hangfireMediumJson = null!;

    [GlobalSetup]
    public void Setup()
    {
        _smallRequest = new SampleRequest
        {
            Id = Guid.NewGuid(),
            Name = "Process Order",
            Amount = 99.95m,
            Tags = ["urgent", "retail"]
        };

        _mediumRequest = new SampleRequest
        {
            Id = Guid.NewGuid(),
            Name = "Generate Monthly Report with Extended Analytics Dashboard",
            Amount = 1_234_567.89m,
            Tags = Enumerable.Range(0, 50).Select(i => $"tag-{i}").ToArray(),
            Metadata = Enumerable.Range(0, 20)
                .ToDictionary(i => $"key-{i}", i => $"value-{i}-{Guid.NewGuid()}")
        };

        // Pre-serialize for deserialization benchmarks
        _tickerqSmallBytes = JsonSerializer.SerializeToUtf8Bytes(_smallRequest);
        _tickerqMediumBytes = JsonSerializer.SerializeToUtf8Bytes(_mediumRequest);
        _tickerqSmallGzip = CompressGzip(_tickerqSmallBytes);
        _tickerqMediumGzip = CompressGzip(_tickerqMediumBytes);

        _hangfireSmallJson = SerializationHelper.Serialize(_smallRequest, SerializationOption.User);
        _hangfireMediumJson = SerializationHelper.Serialize(_mediumRequest, SerializationOption.User);
    }

    // ── Serialize: Small payload ──

    [Benchmark(Baseline = true, Description = "TickerQ (STJ): Serialize small")]
    public byte[] TickerQ_Serialize_Small() =>
        JsonSerializer.SerializeToUtf8Bytes(_smallRequest);

    [Benchmark(Description = "TickerQ (STJ+GZip): Serialize small")]
    public byte[] TickerQ_SerializeGzip_Small() =>
        CompressGzip(JsonSerializer.SerializeToUtf8Bytes(_smallRequest));

    [Benchmark(Description = "Hangfire (Newtonsoft): Serialize small")]
    public string Hangfire_Serialize_Small() =>
        SerializationHelper.Serialize(_smallRequest, SerializationOption.User);

    // ── Serialize: Medium payload ──

    [Benchmark(Description = "TickerQ (STJ): Serialize medium")]
    public byte[] TickerQ_Serialize_Medium() =>
        JsonSerializer.SerializeToUtf8Bytes(_mediumRequest);

    [Benchmark(Description = "TickerQ (STJ+GZip): Serialize medium")]
    public byte[] TickerQ_SerializeGzip_Medium() =>
        CompressGzip(JsonSerializer.SerializeToUtf8Bytes(_mediumRequest));

    [Benchmark(Description = "Hangfire (Newtonsoft): Serialize medium")]
    public string Hangfire_Serialize_Medium() =>
        SerializationHelper.Serialize(_mediumRequest, SerializationOption.User);

    // ── Deserialize: Small payload ──

    [Benchmark(Description = "TickerQ (STJ): Deserialize small")]
    public SampleRequest? TickerQ_Deserialize_Small() =>
        JsonSerializer.Deserialize<SampleRequest>(_tickerqSmallBytes);

    [Benchmark(Description = "TickerQ (STJ+GZip): Deserialize small")]
    public SampleRequest? TickerQ_DeserializeGzip_Small()
    {
        var decompressed = DecompressGzip(_tickerqSmallGzip);
        return JsonSerializer.Deserialize<SampleRequest>(decompressed);
    }

    [Benchmark(Description = "Hangfire (Newtonsoft): Deserialize small")]
    public SampleRequest? Hangfire_Deserialize_Small() =>
        SerializationHelper.Deserialize<SampleRequest>(_hangfireSmallJson, SerializationOption.User);

    // ── Deserialize: Medium payload ──

    [Benchmark(Description = "TickerQ (STJ): Deserialize medium")]
    public SampleRequest? TickerQ_Deserialize_Medium() =>
        JsonSerializer.Deserialize<SampleRequest>(_tickerqMediumBytes);

    [Benchmark(Description = "TickerQ (STJ+GZip): Deserialize medium")]
    public SampleRequest? TickerQ_DeserializeGzip_Medium()
    {
        var decompressed = DecompressGzip(_tickerqMediumGzip);
        return JsonSerializer.Deserialize<SampleRequest>(decompressed);
    }

    [Benchmark(Description = "Hangfire (Newtonsoft): Deserialize medium")]
    public SampleRequest? Hangfire_Deserialize_Medium() =>
        SerializationHelper.Deserialize<SampleRequest>(_hangfireMediumJson, SerializationOption.User);

    // ── Helpers ──

    private static byte[] CompressGzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
            gzip.Write(data, 0, data.Length);
        return output.ToArray();
    }

    private static byte[] DecompressGzip(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    // ── Sample types ──

    public class SampleRequest
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public decimal Amount { get; set; }
        public string[] Tags { get; set; } = [];
        public Dictionary<string, string>? Metadata { get; set; }
    }
}
