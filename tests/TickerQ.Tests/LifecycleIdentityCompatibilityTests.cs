using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public sealed class LifecycleIdentityCompatibilityTests
{
    [Theory]
    [InlineData("")]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    [InlineData("custom-negative-sign")]
    [InlineData("custom-formatter")]
    public void Emitted_ids_and_messages_match_legacy_formatting(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CreateCulture(cultureName);
            string[] partitions =
            [
                null,
                "",
                "quartz-comparison",
                "分区/é/😀|\ud800",
                new string('界', 400), // Small character buffer, larger UTF-8 buffer.
                "\udc00" + string.Concat(Enumerable.Repeat("long/分区/😀|", 700)) + "\ud800"
            ];
            TickerType[] types = [TickerType.TimeTicker, TickerType.CronTickerOccurrence, (TickerType)(-31), (TickerType)int.MaxValue];
            var tickerId = Guid.Parse("1140d0d6-ae94-4181-92d0-a3c68bca9722");
            var lease = Guid.Parse("32c6773a-7b7f-405c-a204-b5803ae51612");
            var generation = Guid.Parse("4d8e56cd-ea19-40e0-8648-88ee2cb74403");
            var clock = Substitute.For<ITickerClock>();
            clock.UtcNow.Returns(new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc));

            foreach (var partition in partitions)
            foreach (var type in types)
            for (var nullableShape = 0; nullableShape < 4; nullableShape++)
            {
                var context = new InternalFunctionContext
                {
                    TickerId = tickerId,
                    FunctionName = "IdentityCompatibility",
                    RuntimePartitionKey = partition,
                    Type = type,
                    AcquisitionToken = (nullableShape & 1) == 0 ? null : lease,
                    ChainGeneration = (nullableShape & 2) == 0 ? null : generation,
                    RetryCount = -4,
                    Retries = 2,
                    ElapsedTime = 1234,
                    ExceptionDetails = "{\"Message\":\"failure reason\",\"StackTrace\":\"not logged\"}"
                };

                AssertEvent(clock, context, TickerLifecycleEvent.Created, TickerStatus.Idle, null,
                    type == TickerType.CronTickerOccurrence ? "Cron occurrence created." : "Job created.",
                    writer => writer.Created(context.TickerId, context.FunctionName, context.Type, clock.UtcNow, partition));
                AssertEvent(clock, context, TickerLifecycleEvent.Queued, TickerStatus.Queued, null,
                    "Queued for execution.", writer => writer.Queued(context));
                AssertEvent(clock, context, TickerLifecycleEvent.Started, TickerStatus.InProgress, -2,
                    $"Running — attempt {-1}.", writer => writer.Started(context, -2));
                AssertEvent(clock, context, TickerLifecycleEvent.RetryScheduled, TickerStatus.InProgress, -2,
                    $"Retry {-2}/{context.Retries} scheduled in {3}s. failure reason",
                    writer => writer.RetryScheduled(context, -2, 3));

                context.ParametersToUpdate.Add(nameof(InternalFunctionContext.Status));
                foreach (var status in new[] { TickerStatus.Done, TickerStatus.DueDone, TickerStatus.Failed, TickerStatus.Cancelled, TickerStatus.Skipped })
                {
                    context.Status = status;
                    var outcome = status == TickerStatus.Done ? "Completed"
                        : status == TickerStatus.DueDone ? "Completed (overdue recovery)" : status.ToString();
                    var reason = status is TickerStatus.Done or TickerStatus.DueDone ? "" : " failure reason";
                    AssertEvent(clock, context, TickerLifecycleEvent.Completed, status, null,
                        $"{outcome} — 1.234s; {context.RetryCount} retries." + reason,
                        writer => writer.Committed(context));
                }

                foreach (var status in new[] { TickerStatus.InProgress, TickerStatus.Skipped, (TickerStatus)(-109), (TickerStatus)int.MaxValue })
                {
                    context.Status = status;
                    AssertEvent(clock, context, TickerLifecycleEvent.ChainLaunched, status, null,
                        status == TickerStatus.Skipped ? "Recurring chain skipped. failure reason"
                            : "Recurring chain launched. Follow the child jobs for their outcomes.",
                        writer => writer.ChainLaunched(context));
                }
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Filtering_and_redaction_still_apply_to_each_lifecycle_path()
    {
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var store = new RecordingStore();
        var redactedMessages = new List<string>();
        var options = new ExecutionLogOptions
        {
            MinimumLevel = LogLevel.Warning,
            Redact = message => { redactedMessages.Add(message); return "redacted"; }
        };
        var writer = new TickerLifecycleLogger(store, options, clock);
        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(), FunctionName = "Filtered", RuntimePartitionKey = "partition",
            Status = TickerStatus.Done, ParametersToUpdate = [nameof(InternalFunctionContext.Status)]
        };

        writer.Created(context.TickerId, context.FunctionName, context.Type, clock.UtcNow, context.RuntimePartitionKey);
        writer.Queued(context);
        writer.Started(context, 0);
        writer.Committed(context);
        writer.ChainLaunched(context);
        Assert.Empty(store.Lines);
        Assert.Empty(redactedMessages);

        writer.RetryScheduled(context, 1, 3);
        context.Status = TickerStatus.Failed;
        writer.Committed(context);
        context.Status = TickerStatus.Skipped;
        writer.ChainLaunched(context);
        Assert.Equal(3, store.Lines.Count);
        Assert.All(store.Lines, line => Assert.Equal("redacted", line.Message));
        Assert.Equal(new[] { (int)LogLevel.Warning, (int)LogLevel.Error, (int)LogLevel.Warning },
            store.Lines.Select(line => line.Level));
        Assert.Equal(3, redactedMessages.Count);

        options.IncludeLifecycleEvents = false;
        writer.RetryScheduled(context, 1, 3);
        writer.Committed(context);
        writer.ChainLaunched(context);
        options.IncludeLifecycleEvents = true;
        options.Enabled = false;
        writer.RetryScheduled(context, 1, 3);
        writer.Committed(context);
        writer.ChainLaunched(context);
        Assert.Equal(3, store.Lines.Count);
        Assert.Equal(3, redactedMessages.Count);
    }

    [Fact]
    public void Store_failures_remain_isolated_from_all_lifecycle_notifications()
    {
        var clock = Substitute.For<ITickerClock>();
        clock.UtcNow.Returns(DateTime.UtcNow);
        var store = Substitute.For<ITickerExecutionLogStore>();
        store.When(value => value.Add(Arg.Any<TickerExecutionLogLine>()))
            .Do(_ => throw new InvalidOperationException("Diagnostic sink failed."));
        var writer = new TickerLifecycleLogger(store, new ExecutionLogOptions(), clock);
        var context = new InternalFunctionContext
        {
            TickerId = Guid.NewGuid(), FunctionName = "FailureIsolation",
            RuntimePartitionKey = new string('界', 3000), Status = TickerStatus.Failed,
            ParametersToUpdate = [nameof(InternalFunctionContext.Status)]
        };

        var failure = Record.Exception(() =>
        {
            writer.Created(context.TickerId, context.FunctionName, context.Type, clock.UtcNow, context.RuntimePartitionKey);
            writer.Queued(context);
            writer.Started(context, 0);
            writer.RetryScheduled(context, 1, 3);
            writer.Committed(context);
            writer.ChainLaunched(context);
        });

        Assert.Null(failure);
        store.Received(6).Add(Arg.Any<TickerExecutionLogLine>());
    }

    private static CultureInfo CreateCulture(string name)
    {
        if (name == "custom-formatter") return new CustomFormattingCulture();
        if (name != "custom-negative-sign") return new CultureInfo(name);
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        // Exercise fallback sizing for culture-dependent integers as well as long partitions.
        culture.NumberFormat.NegativeSign = new string('~', 700) + "−";
        return culture;
    }

    private sealed class CustomFormattingCulture() : CultureInfo("en-US"), ICustomFormatter
    {
        public override object GetFormat(Type formatType)
            => formatType == typeof(ICustomFormatter) ? this : base.GetFormat(formatType);

        public string Format(string format, object arg, IFormatProvider formatProvider)
            => throw new InvalidOperationException("Providerless interpolation must not invoke a custom formatter.");
    }

    private static void AssertEvent(ITickerClock clock, InternalFunctionContext context,
        TickerLifecycleEvent kind, TickerStatus status, int? attempt, string message,
        Action<TickerLifecycleLogger> emit)
    {
        var store = new RecordingStore();
        emit(new TickerLifecycleLogger(store, new ExecutionLogOptions(), clock));
        var line = Assert.Single(store.Lines);

        // This is the exact previous identity expression. Existing buffered/persisted IDs must
        // remain compatible so receiving the same lifecycle notification still deduplicates.
        var identity = $"{context.RuntimePartitionKey}|{context.Type}|{context.TickerId}|{kind}|{status}|" +
            (kind == TickerLifecycleEvent.Created ? "" : $"{context.AcquisitionToken}|{context.ChainGeneration}|{attempt ?? context.RetryCount}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        Assert.Equal(new Guid(hash.AsSpan(0, 16)), line.Id);
        Assert.Equal(message, line.Message);
        Assert.Equal(context.TickerId, line.TickerId);
        Assert.Equal(context.FunctionName, line.FunctionName);
        Assert.Equal(kind, line.LifecycleEvent);
        Assert.Equal(status, line.Status);
        Assert.Equal(kind == TickerLifecycleEvent.Created ? 0 : (attempt ?? context.RetryCount) + 1, line.Attempt);
    }

    private sealed class RecordingStore : ITickerExecutionLogStore
    {
        public List<TickerExecutionLogLine> Lines { get; } = [];
        public void Add(TickerExecutionLogLine line) => Lines.Add(line);
        public IReadOnlyList<TickerExecutionLogLine> GetTail(Guid tickerId)
            => Lines.Where(line => line.TickerId == tickerId).ToArray();
    }
}
