using Microsoft.Extensions.AI;
using NSubstitute;
using TickerQ.Dashboard.Assistant;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Integrations;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Tests;

public class AssistantToolsTests
{
    private readonly ITickerDashboardDataService<TimeTickerEntity, CronTickerEntity> _data =
        Substitute.For<ITickerDashboardDataService<TimeTickerEntity, CronTickerEntity>>();
    private readonly ITickerIntegrationAdminService _integrations = Substitute.For<ITickerIntegrationAdminService>();
    private readonly List<AssistantProposal> _proposals = [];
    private readonly Guid _id = Guid.NewGuid();
    private readonly Guid _destination = Guid.NewGuid();
    private readonly Guid _version = Guid.NewGuid();

    public AssistantToolsTests()
    {
        _data.GetAllFunctionsAsync(Arg.Any<CancellationToken>()).Returns(new List<FunctionInfoDto>
            { new() { FunctionName = "Root" }, new() { FunctionName = "Child" } });
        _integrations.GetAsync(Arg.Any<CancellationToken>()).Returns(new IntegrationSnapshot
        {
            Available = true, MonitoringAvailable = true,
            Configuration = new IntegrationConfiguration { Version = _version,
                Destinations = [new() { Id = _destination, Name = "Operations", Provider = "slack",
                    Url = "https://secret.example/token", Headers = new() { ["Authorization"] = "secret-header" },
                    PayloadTemplate = "secret-template", MessageTemplates = new() { JobAlert = "secret-category-template" } }] },
            RecentDeliveries = [new() { DestinationId = _destination, Outcome = "failed", Message = "secret-response-body" }]
        });
    }

    private IList<AITool> Tools(bool writes = true, bool integrations = true,
        Func<Guid, CancellationToken, Task<ExecutionLogReadResult>>? logs = null) =>
        TickerAssistantTools.Build(_data, writes, p => { _proposals.Add(p); return Task.CompletedTask; },
            CancellationToken.None, logs, integrations ? _integrations : null);

    private async Task<string> Invoke(string name, Dictionary<string, object?> arguments,
        IList<AITool>? tools = null)
    {
        var function = (tools ?? Tools()).OfType<AIFunction>().Single(x => x.Name == name);
        var result = await function.InvokeAsync(new AIFunctionArguments(arguments));
        return result?.ToString() ?? "";
    }

    [Fact]
    public void ReadOnlyUsers_GetNoProposalTools_AndUnprivilegedUsersGetNoIntegrationTools()
    {
        Assert.DoesNotContain(Tools(false), t => t.Name.StartsWith("propose_"));
        Assert.DoesNotContain(Tools(integrations: false), t => t.Name.Contains("integration") || t.Name is "propose_monitor" or "propose_alert_rule");
        Assert.Contains(Tools(false), t => t.Name == "get_integrations");
    }

    [Fact]
    public async Task IntegrationInventory_WhitelistsMetadata_AndNeverSendsOrSaves()
    {
        var result = await Invoke("get_integrations", []);
        Assert.Contains("Operations", result);
        Assert.Contains("failed", result);
        Assert.DoesNotContain("secret", result);
        Assert.DoesNotContain(_integrations.ReceivedCalls(), c => c.GetMethodInfo().Name is "SaveAsync" or "TestAsync");
    }

    [Theory]
    [InlineData("cancel", TickerStatus.Idle, "Cancels this pending")]
    [InlineData("cancel", TickerStatus.InProgress, "cooperative cancellation")]
    [InlineData("retry", TickerStatus.Failed, "Runs this time ticker again")]
    [InlineData("delete", TickerStatus.Done, "Permanently deletes")]
    public async Task ExecutionActions_OnlyEmitProposals_WithExactObservedState(string action, TickerStatus state, string warning)
    {
        var locked = DateTime.UtcNow.AddMinutes(-1);
        _data.GetTimeTickerByIdAsync(_id, Arg.Any<CancellationToken>()).Returns(new TimeTickerFlatDto
            { Id = _id, FunctionName = "Root", Status = state, LockedAt = locked });
        await Invoke("propose_execution_action", new() { ["action"] = action, ["targetId"] = _id.ToString(), ["targetType"] = "TimeTicker" });
        var p = Assert.Single(_proposals);
        Assert.Equal(action + "Execution", p.Kind);
        Assert.Equal(state.ToString(), p.ExpectedStatus);
        Assert.Equal(locked, p.ExpectedLockedAt);
        Assert.Equal(_id, p.TargetId);
        Assert.Contains(warning, p.Warning);
        Assert.All(_data.ReceivedCalls(), c => Assert.StartsWith("Get", c.GetMethodInfo().Name));
    }

    [Theory]
    [InlineData("retry", TickerStatus.InProgress)]
    [InlineData("cancel", TickerStatus.Done)]
    [InlineData("delete", TickerStatus.Queued)]
    [InlineData("delete", TickerStatus.InProgress)]
    public async Task InvalidActions_ProduceNoCard(string action, TickerStatus state)
    {
        _data.GetTimeTickerByIdAsync(_id, Arg.Any<CancellationToken>()).Returns(new TimeTickerFlatDto { Id = _id, Status = state });
        await Invoke("propose_execution_action", new() { ["action"] = action, ["targetId"] = _id.ToString(), ["targetType"] = "TimeTicker" });
        Assert.Empty(_proposals);
    }

    [Fact]
    public async Task CronRetry_UsesSourceDefinition_AndDescribesFreshOccurrence()
    {
        var cronId = Guid.NewGuid();
        _data.GetCronOccurrenceByIdAsync(_id, Arg.Any<CancellationToken>()).Returns(new CronOccurrenceFlatDto
            { Id = _id, CronTickerId = cronId, FunctionName = "Root", Status = TickerStatus.Failed });
        await Invoke("propose_execution_action", new() { ["action"] = "retry", ["targetId"] = _id.ToString(), ["targetType"] = "CronOccurrence" });
        var p = Assert.Single(_proposals);
        Assert.Equal(cronId, p.CronTickerId);
        Assert.Contains("current cron definition", p.Warning);
    }

    [Fact]
    public async Task Logs_CheckRuntimeMembership_AndBoundOutput()
    {
        var reads = 0;
        var tools = Tools(logs: (_, _) => { reads++; return Task.FromResult(new ExecutionLogReadResult(
            Enumerable.Range(0, 150).Select(i => new TickerExecutionLogLine { UnixMs = i, Message = new string('x', 1000) }).ToList(), true, false)); });
        var args = new Dictionary<string, object?> { ["executionId"] = _id.ToString(), ["executionType"] = "TimeTicker", ["limit"] = 1000 };
        await Invoke("get_execution_logs", args, tools);
        Assert.Equal(0, reads);
        _data.GetTimeTickerByIdAsync(_id, Arg.Any<CancellationToken>()).Returns(new TimeTickerFlatDto { Id = _id, FunctionName = "Root" });
        var result = await Invoke("get_execution_logs", args, tools);
        Assert.Equal(1, reads);
        Assert.Contains("Persistence available=False", result);
        Assert.Contains("selected=100", result);
        Assert.Contains("Output truncated", result);
        Assert.True(result.Length < 24100);
    }

    [Theory]
    [InlineData("99 99 * * *")]
    [InlineData("%Configuration:Schedule%")]
    [InlineData("1 2 3 4")]
    public async Task InvalidCronExpressions_AreRejectedBeforeProposal(string expression)
    {
        var result = await Invoke("propose_cron_ticker", new() { ["functionName"] = "Root", ["expression"] = expression,
            ["retries"] = 0, ["description"] = "", ["requestJson"] = "" });
        Assert.Contains("Invalid cron", result);
        Assert.Empty(_proposals);
    }

    [Fact]
    public async Task RecurringChain_ValidatesFunctionsAndConditions_ThenPreservesRootPayloadAndOverlap()
    {
        var args = new Dictionary<string, object?> { ["expression"] = "0 8 * * *", ["skipIfAlreadyRunning"] = true,
            ["chainJson"] = """{"function":"Root","requestJson":"{\"name\":\"héllo\"}","children":[{"function":"Child","runCondition":"OnFailure"}]}""" };
        await Invoke("propose_cron_chain", args);
        var p = Assert.Single(_proposals);
        Assert.Equal("createCronChain", p.Kind);
        Assert.True(p.SkipIfAlreadyRunning);
        Assert.Contains("OnFailure", p.ChainJson);
        _proposals.Clear();
        args["chainJson"] = """{"function":"Root","children":[null]}""";
        await Invoke("propose_cron_chain", args);
        Assert.Empty(_proposals);
    }

    [Theory]
    [InlineData(-1, "{}")]
    [InlineData(0, "not json")]
    public async Task InvalidPayloadOrRetries_ProducesNoProposal(int retries, string payload)
    {
        await Invoke("propose_cron_ticker", new() { ["functionName"] = "Root", ["expression"] = "* * * * *",
            ["retries"] = retries, ["description"] = "", ["requestJson"] = payload });
        Assert.Empty(_proposals);
    }

    [Fact]
    public async Task AlertRule_DraftsVersionedSettings_WithoutWritingOrSending()
    {
        await Invoke("propose_alert_rule", new() { ["name"] = "Failures", ["functionName"] = "Root", ["destinationIds"] = _destination.ToString(),
            ["events"] = "failed,timeout_cancelled", ["cooldownSeconds"] = 60 });
        var p = Assert.Single(_proposals);
        Assert.Equal(_version, p.ConfigurationVersion);
        Assert.Equal(new[] { "Operations" }, p.DestinationNames);
        Assert.Equal(new[] { "Root" }, p.Rule!.IncludeFunctions);
        Assert.DoesNotContain(_integrations.ReceivedCalls(), c => c.GetMethodInfo().Name is "SaveAsync" or "TestAsync");
        _proposals.Clear();
        await Invoke("propose_alert_rule", new() { ["name"] = "Failures", ["functionName"] = "Root", ["destinationIds"] = Guid.NewGuid().ToString(),
            ["events"] = "failed", ["cooldownSeconds"] = 60 });
        Assert.Empty(_proposals);
    }

    [Theory]
    [InlineData("Europe/Belgrade", 3, true)]
    [InlineData("No/SuchZone", 3, false)]
    [InlineData("UTC", 1, false)]
    public async Task Monitor_ValidatesDeadlineAndThresholds(string zone, int threshold, bool valid)
    {
        await Invoke("propose_monitor", new() { ["name"] = "Daily invoices", ["functionName"] = "Root", ["kind"] = "deadline",
            ["destinationIds"] = _destination.ToString(), ["notifyRecovery"] = true, ["deadlineLocalTime"] = "08:00",
            ["timeZoneId"] = zone, ["graceMinutes"] = 10, ["durationSeconds"] = 300, ["failureThreshold"] = threshold, ["lookbackHours"] = 24 });
        Assert.Equal(valid ? 1 : 0, _proposals.Count);
        if (valid) { Assert.Equal(zone, _proposals[0].Monitor!.TimeZoneId); Assert.True(_proposals[0].Monitor!.NotifyRecovery); }
    }

    [Fact]
    public async Task RecurringChain_RejectsMoreThan100Steps()
    {
        AssistantChainNode Tree(int depth) => new()
        {
            Function = "Root", RunCondition = depth == 0 ? null! : "OnSuccess",
            Children = depth == 3 ? [] : Enumerable.Range(0, 5).Select(_ => Tree(depth + 1)).ToList()
        };
        var json = System.Text.Json.JsonSerializer.Serialize(Tree(0), AssistantJsonContext.Default.AssistantChainNode);
        var result = await Invoke("propose_cron_chain", new() { ["expression"] = "0 8 * * *", ["skipIfAlreadyRunning"] = false, ["chainJson"] = json });
        Assert.Contains("max 100", result);
        Assert.Empty(_proposals);
    }

    [Fact]
    public async Task MonitorUnavailable_CannotDraftAnEnabledMonitor()
    {
        _integrations.GetAsync(Arg.Any<CancellationToken>()).Returns(new IntegrationSnapshot { MonitoringAvailable = false });
        var result = await Invoke("propose_monitor", new() { ["name"] = "Daily", ["functionName"] = "Root", ["kind"] = "deadline",
            ["destinationIds"] = _destination.ToString(), ["notifyRecovery"] = true, ["deadlineLocalTime"] = "08:00",
            ["timeZoneId"] = "UTC", ["graceMinutes"] = 10, ["durationSeconds"] = 300, ["failureThreshold"] = 3, ["lookbackHours"] = 24 });
        Assert.Contains("unavailable", result);
        Assert.Empty(_proposals);
    }

    [Fact]
    public async Task FunctionContract_IsBounded_AndUnknownFunctionDoesNotLeakOtherContracts()
    {
        _data.GetAllFunctionsAsync(Arg.Any<CancellationToken>()).Returns(new List<FunctionInfoDto> { new()
            { FunctionName = "Root", RequestContract = new() { Required = true, SchemaJson = new string('s', 20000) } } });
        var contract = await Invoke("get_function_contract", new() { ["functionName"] = "Root" });
        Assert.Contains("required=True", contract);
        Assert.Contains("Contract truncated", contract);
        Assert.True(contract.Length < 12100);
        Assert.Contains("Unknown function", await Invoke("get_function_contract", new() { ["functionName"] = "Other" }));
    }

    [Fact]
    public async Task CronUpdate_DoesNotProposeOverwritingUnrequestedFields()
    {
        _data.GetCronTickerByIdAsync(_id, Arg.Any<CancellationToken>()).Returns(new CronTickerFlatDto
            { Id = _id, FunctionName = "Root", Expression = "0 8 * * *", Retries = 4, IsEnabled = true, Description = "existing", SkipIfAlreadyRunning = true });
        await Invoke("propose_cron_update", new() { ["cronTickerId"] = _id.ToString(), ["newExpression"] = "0 9 * * *", ["enabled"] = "", ["retries"] = -1, ["description"] = "" });
        var p = Assert.Single(_proposals);
        Assert.Null(p.Retries);
        Assert.Null(p.Description);
        Assert.Null(p.IsEnabled);
        Assert.Equal("0 9 * * *", p.Expression);
    }
}
