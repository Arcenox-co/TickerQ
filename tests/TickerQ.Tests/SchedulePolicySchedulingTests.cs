using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Managers;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

[Collection("TickerFunctionProviderState")]
public sealed class SchedulePolicySchedulingTests : IDisposable
{
    private const string FunctionName = "Policy.Test";
    private readonly ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity> _persistence =
        Substitute.For<ITickerPersistenceProvider<TimeTickerEntity, CronTickerEntity>>();
    private readonly ISchedulePolicyStore _policies = Substitute.For<ISchedulePolicyStore>();
    private readonly ITickerQHostScheduler _host = Substitute.For<ITickerQHostScheduler>();
    private readonly ITickerClock _clock = Substitute.For<ITickerClock>();
    private readonly ITimeTickerManager<TimeTickerEntity> _timeManager;
    private readonly ICronTickerManager<CronTickerEntity> _cronManager;

    private static readonly DateTime Friday = new(2026, 1, 2, 18, 0, 0, DateTimeKind.Utc);

    public SchedulePolicySchedulingTests()
    {
        TickerFunctionProvider.RegisterFunctions(new Dictionary<string,
            (string, TickerTaskPriority, TickerFunctionDelegate, int)>
        {
            [FunctionName] = (string.Empty, TickerTaskPriority.Normal,
                (_, _, _) => Task.CompletedTask, 0),
        });
        TickerFunctionProvider.Build();
        _clock.UtcNow.Returns(Friday);
        _persistence.AddTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>()).Returns(1);
        _persistence.UpdateTimeTickers(Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>()).Returns(1);
        _persistence.InsertCronTickers(Arg.Any<CronTickerEntity[]>(), Arg.Any<CancellationToken>()).Returns(1);

        var options = new SchedulerOptionsBuilder();
        options.BindRuntimeActivationScope(null, 1, schedulerEnabled: false);
        var manager = new TickerManager<TimeTickerEntity, CronTickerEntity>(
            _persistence, _host, _clock, Substitute.For<ITickerQNotificationHubSender>(),
            new TickerExecutionContext(), Substitute.For<ITickerQDispatcher>(), options,
            new TickerQActivationGate(), schedulePolicyStore: _policies,
            schedulePolicyEvaluator: new SchedulePolicyEvaluator());
        _timeManager = manager;
        _cronManager = manager;
    }

    [Fact]
    public async Task OneTime_Reject_Does_Not_Persist_A_Blocked_Request()
    {
        SetPolicies(BusinessHours());
        var ticker = NewTime(BlockedOccurrenceBehavior.Reject);

        var result = await _timeManager.AddAsync(ticker);

        Assert.False(result.IsSucceeded);
        Assert.Contains("blocked", result.Exception.Message, StringComparison.OrdinalIgnoreCase);
        await _persistence.DidNotReceive().AddTimeTickers(
            Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OneTime_MoveForward_Preserves_Requested_Time_And_Persists_Effective_Time()
    {
        var policy = BusinessHours();
        SetPolicies(policy);
        var ticker = NewTime(BlockedOccurrenceBehavior.MoveForward);

        var result = await _timeManager.AddAsync(ticker);

        Assert.True(result.IsSucceeded, result.Exception?.Message);
        Assert.Equal(Friday, ticker.RequestedExecutionTime);
        Assert.Equal(new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc), ticker.ExecutionTime);
        Assert.Equal(policy.Name, ticker.SchedulePolicyName);
        Assert.Equal(policy.Version, ticker.SchedulePolicyVersion);
        Assert.False(string.IsNullOrWhiteSpace(ticker.ScheduleAdjustmentReason));
        await _persistence.Received(1).AddTimeTickers(
            Arg.Is<TimeTickerEntity[]>(rows => rows.Length == 1 && rows[0] == ticker),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OneTime_Skip_Terminalizes_The_Whole_Chain()
    {
        SetPolicies(BusinessHours());
        var ticker = NewTime(BlockedOccurrenceBehavior.Skip);
        ticker.Children =
        [
            new TimeTickerEntity
            {
                Function = FunctionName,
                Children = [new TimeTickerEntity { Function = FunctionName }],
            },
        ];

        var result = await _timeManager.AddAsync(ticker);

        Assert.True(result.IsSucceeded, result.Exception?.Message);
        Assert.All(Flatten(ticker), row =>
        {
            Assert.Equal(TickerStatus.Skipped, row.Status);
            Assert.NotNull(row.ExecutedAt);
            Assert.Contains("Schedule policy", row.SkippedReason);
        });
        _host.DidNotReceiveWithAnyArgs().RestartIfNeeded(default);
    }

    [Fact]
    public async Task Cron_Skips_Blocked_Occurrences_In_Its_Own_Time_Zone()
    {
        var policy = BusinessHours();
        SetPolicies(policy);
        var cron = new CronTickerEntity
        {
            Function = FunctionName,
            Expression = "0 9 * * *",
            TimeZoneId = "UTC",
            SchedulePolicyName = policy.Name,
            BlockedOccurrenceBehavior = BlockedOccurrenceBehavior.Skip,
        };

        var result = await _cronManager.AddAsync(cron);

        Assert.True(result.IsSucceeded, result.Exception?.Message);
        Assert.Equal(policy.Version, cron.SchedulePolicyVersion);
        _host.Received(1).RestartIfNeeded(
            new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Child_Policies_Are_Rejected_Before_Persistence()
    {
        var root = new TimeTickerEntity
        {
            Function = FunctionName,
            ExecutionTime = Friday.AddHours(1),
            Children =
            [
                new TimeTickerEntity { Function = FunctionName, SchedulePolicyName = "Business hours" },
            ],
        };

        var result = await _timeManager.AddAsync(root);

        Assert.False(result.IsSucceeded);
        Assert.Contains("chain root", result.Exception.Message, StringComparison.OrdinalIgnoreCase);
        await _persistence.DidNotReceive().AddTimeTickers(
            Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Skip_When_Missed_Requires_A_Lateness_Limit()
    {
        var ticker = new TimeTickerEntity
        {
            Function = FunctionName,
            ExecutionTime = Friday.AddMinutes(1),
            MissedExecutionAction = MissedExecutionAction.Skip,
        };

        var result = await _timeManager.AddAsync(ticker);

        Assert.False(result.IsSucceeded);
        Assert.Contains("maximum lateness", result.Exception.Message, StringComparison.OrdinalIgnoreCase);
        await _persistence.DidNotReceive().AddTimeTickers(
            Arg.Any<TimeTickerEntity[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cron_RunLatest_Requires_Bounded_Skip_Behavior()
    {
        var cron = new CronTickerEntity
        {
            Function = FunctionName,
            Expression = "* * * * *",
            CatchUpMode = CronCatchUpMode.RunLatest,
            MissedExecutionAction = MissedExecutionAction.RunOnce,
        };

        var result = await _cronManager.AddAsync(cron);

        Assert.False(result.IsSucceeded);
        Assert.Contains("RunLatest", result.Exception.Message, StringComparison.OrdinalIgnoreCase);
        await _persistence.DidNotReceive().InsertCronTickers(
            Arg.Any<CronTickerEntity[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Metadata_Only_Update_Preserves_The_Original_MoveForward_Decision()
    {
        var effective = new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc);
        var stored = new TimeTickerEntity
        {
            Id = Guid.NewGuid(),
            Function = FunctionName,
            ExecutionTime = effective,
            SchedulePolicyName = "Business hours",
            SchedulePolicyVersion = 7,
            BlockedOccurrenceBehavior = BlockedOccurrenceBehavior.MoveForward,
            RequestedExecutionTime = Friday,
            ScheduleAdjustmentReason = "Friday is outside the allowed window.",
        };
        var update = new TimeTickerEntity
        {
            Id = stored.Id,
            Function = FunctionName,
            ExecutionTime = effective,
            SchedulePolicyName = stored.SchedulePolicyName,
            SchedulePolicyVersion = stored.SchedulePolicyVersion,
            BlockedOccurrenceBehavior = stored.BlockedOccurrenceBehavior,
            RequestedExecutionTime = stored.RequestedExecutionTime,
            ScheduleAdjustmentReason = stored.ScheduleAdjustmentReason,
            Description = "Only this field changed",
        };
        _persistence.GetTimeTickerById(stored.Id, Arg.Any<CancellationToken>()).Returns(stored);

        var result = await _timeManager.UpdateAsync(update);

        Assert.True(result.IsSucceeded, result.Exception?.Message);
        Assert.Equal(Friday, update.RequestedExecutionTime);
        Assert.Equal(effective, update.ExecutionTime);
        Assert.Equal(7, update.SchedulePolicyVersion);
        Assert.Equal(stored.ScheduleAdjustmentReason, update.ScheduleAdjustmentReason);
        await _policies.DidNotReceive().ListSchedulePoliciesAsync(Arg.Any<CancellationToken>());
    }

    public void Dispose() => TickerFunctionProvider.Build();

    private TimeTickerEntity NewTime(BlockedOccurrenceBehavior behavior) => new()
    {
        Function = FunctionName,
        ExecutionTime = Friday,
        SchedulePolicyName = "business HOURS",
        BlockedOccurrenceBehavior = behavior,
    };

    private static SchedulePolicy BusinessHours()
    {
        var policy = SchedulePolicy.Create("Business hours")
            .AllowWeekdays(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                DayOfWeek.Thursday, DayOfWeek.Friday)
            .AllowBetween(new TimeOnly(9, 0), new TimeOnly(17, 0))
            .Build();
        policy.Version = 7;
        return policy;
    }

    private void SetPolicies(params SchedulePolicy[] policies)
        => _policies.ListSchedulePoliciesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SchedulePolicy>>(policies));

    private static IReadOnlyList<TimeTickerEntity> Flatten(TimeTickerEntity root)
    {
        var result = new List<TimeTickerEntity>();
        var pending = new Stack<TimeTickerEntity>();
        pending.Push(root);
        while (pending.TryPop(out var current))
        {
            result.Add(current);
            foreach (var child in current.GetChildrenForRead()) pending.Push(child);
        }
        return result;
    }
}
