using Microsoft.Extensions.AI;
using NSubstitute;
using TickerQ.Dashboard.Assistant;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public class AssistantSchedulePolicyToolsTests
{
    private readonly ITickerDashboardDataService<TimeTickerEntity, CronTickerEntity> _data =
        Substitute.For<ITickerDashboardDataService<TimeTickerEntity, CronTickerEntity>>();
    private readonly ISchedulePolicyManager _policies = Substitute.For<ISchedulePolicyManager>();
    private readonly List<AssistantProposal> _proposals = [];
    private readonly Guid _cronId = Guid.NewGuid();
    private readonly SchedulePolicy _business;
    private readonly SchedulePolicy _maintenance;

    public AssistantSchedulePolicyToolsTests()
    {
        _business = new SchedulePolicy
        {
            Name = "Business hours", TimeZoneId = "Europe/Berlin", Version = 3,
            AllowedWeekdays = ScheduleWeekdays.MondayToFriday,
            AllowedTimeWindows = [new ScheduleTimeWindow(new TimeOnly(9, 0), new TimeOnly(17, 0))],
            ExcludedAnnualDates = [new ScheduleAnnualDate(12, 25)],
            Description = "Office hours",
        };
        _maintenance = new SchedulePolicy
        {
            Name = "Maintenance window", TimeZoneId = "UTC", Version = 1,
            AllowedWeekdays = ScheduleWeekdays.Sunday,
            AllowedTimeWindows = [new ScheduleTimeWindow(new TimeOnly(1, 0), new TimeOnly(4, 0))],
        };
        _policies.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<SchedulePolicy> { _business, _maintenance });
        _data.GetAllFunctionsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<FunctionInfoDto> { new() { FunctionName = "Orders.Import", ContractVersion = 1 } });
        var cron = new CronTickerFlatDto
        {
            Id = _cronId, FunctionName = "Orders.Import", Expression = "0 * * * *",
            IsEnabled = true, SchedulePolicyName = "Business hours",
        };
        _data.GetCronTickersFlatAsync(Arg.Any<CronTickerQueryFilter>(), Arg.Any<CancellationToken>())
            .Returns(new PaginationResult<CronTickerFlatDto> { Items = [cron], TotalCount = 1 });
        _data.GetCronTickerByIdAsync(_cronId, Arg.Any<CancellationToken>()).Returns(cron);
    }

    private IList<AITool> Tools(bool writes = true, bool policies = true) =>
        TickerAssistantTools.Build(_data, writes, p => { _proposals.Add(p); return Task.CompletedTask; },
            CancellationToken.None, schedulePolicies: policies ? _policies : null);

    private async Task<string> Invoke(string name, Dictionary<string, object?> arguments, IList<AITool>? tools = null)
    {
        var function = (tools ?? Tools()).OfType<AIFunction>().Single(x => x.Name == name);
        return (await function.InvokeAsync(new AIFunctionArguments(arguments)))?.ToString() ?? "";
    }

    private static Dictionary<string, object?> PolicyArgs(string name, Action<Dictionary<string, object?>>? configure = null)
    {
        var args = new Dictionary<string, object?>
        {
            ["name"] = name, ["description"] = "", ["timeZoneId"] = "", ["allowedDays"] = "", ["dailyWindows"] = "",
            ["excludedDates"] = "", ["excludedEveryYear"] = "", ["requiredPolicies"] = "", ["blockedWhenPoliciesAllow"] = "",
        };
        configure?.Invoke(args);
        return args;
    }

    [Fact]
    public void Read_only_users_get_policy_reads_but_no_policy_proposals()
    {
        var names = Tools(writes: false).OfType<AIFunction>().Select(x => x.Name).ToList();

        Assert.Contains("list_schedule_policies", names);
        Assert.Contains("preview_schedule_policy", names);
        Assert.DoesNotContain("propose_schedule_policy", names);
        Assert.DoesNotContain("propose_cron_schedule_policy", names);
    }

    [Fact]
    public void Policy_tools_are_absent_when_the_host_has_no_policy_manager()
    {
        var names = Tools(policies: false).OfType<AIFunction>().Select(x => x.Name).ToList();

        Assert.DoesNotContain("list_schedule_policies", names);
        Assert.DoesNotContain("propose_schedule_policy", names);
    }

    [Fact]
    public async Task Listing_shows_rules_composition_and_cron_usage()
    {
        var result = await Invoke("list_schedule_policies", new());

        Assert.Contains("Business hours", result);
        Assert.Contains("zone=Europe/Berlin", result);
        Assert.Contains("days=Monday–Friday", result);
        Assert.Contains("hours=09:00-17:00", result);
        Assert.Contains("everyYear=12-25", result);
        Assert.Contains($"usedByCron=Orders.Import ({_cronId})", result);
        Assert.Contains("Maintenance window", result);
    }

    [Fact]
    public async Task Preview_reports_allowed_and_blocked_occurrences()
    {
        var monday = new DateTime(2026, 10, 26, 8, 0, 0, DateTimeKind.Utc);
        _policies.PreviewCronAsync("Business hours", "0 9 * * *", Arg.Any<DateTime>(), 2, "Europe/Berlin", Arg.Any<CancellationToken>())
            .Returns(new List<SchedulePolicyPreviewItem>
            {
                new() { RequestedAtUtc = monday, IsAllowed = true, Reason = "Allowed by the schedule policy." },
                new() { RequestedAtUtc = monday.AddDays(5), IsAllowed = false, Reason = "Saturday is outside the allowed weekdays." },
            });

        var result = await Invoke("preview_schedule_policy",
            new() { ["policyName"] = "business HOURS", ["expression"] = "0 9 * * *", ["count"] = 2 });

        Assert.Contains("1 allowed, 1 blocked", result);
        Assert.Contains("Mon 2026-10-26 09:00 local", result);
        Assert.Contains("BLOCKED | Saturday is outside", result);
    }

    [Fact]
    public async Task New_policy_is_parsed_into_a_create_card()
    {
        var result = await Invoke("propose_schedule_policy", PolicyArgs("Orders calendar", a =>
        {
            a["description"] = "Warehouse shifts";
            a["timeZoneId"] = "Europe/Berlin";
            a["allowedDays"] = "Mon, Tue, MondayToFriday, Saturday";
            a["dailyWindows"] = "06:00-14:00, 22:00-02:00";
            a["excludedDates"] = "2026-12-24";
            a["excludedEveryYear"] = "12-25, 01-01";
            a["requiredPolicies"] = "business hours";
            a["blockedWhenPoliciesAllow"] = "Maintenance Window";
        }));

        Assert.Contains("NOT been applied", result);
        var proposal = Assert.Single(_proposals);
        Assert.Equal("createSchedulePolicy", proposal.Kind);
        Assert.Null(proposal.ExpectedVersion);
        var policy = proposal.Policy!;
        Assert.Equal("Orders calendar", policy.Name);
        Assert.Equal("Warehouse shifts", policy.Description);
        Assert.Equal("Europe/Berlin", policy.TimeZoneId);
        Assert.Equal((ScheduleWeekdays.MondayToFriday | ScheduleWeekdays.Saturday).ToString(), policy.AllowedWeekdays);
        Assert.Equal(["06:00:00", "22:00:00"], policy.AllowedTimeWindows.Select(w => w.Start));
        Assert.Equal(["14:00:00", "02:00:00"], policy.AllowedTimeWindows.Select(w => w.End));
        Assert.Equal(["2026-12-24"], policy.ExcludedDates);
        Assert.Equal([(12, 25), (1, 1)], policy.ExcludedAnnualDates.Select(d => (d.Month, d.Day)));
        Assert.Equal(["Business hours"], policy.RequiredPolicyNames);
        Assert.Equal(["Maintenance window"], policy.ExcludedWhenPolicyAllowsNames);
    }

    [Fact]
    public async Task Existing_policy_name_becomes_a_versioned_update_that_keeps_unspecified_fields()
    {
        await Invoke("propose_schedule_policy", PolicyArgs("BUSINESS HOURS", a =>
        {
            a["dailyWindows"] = "08:00-18:00";
            a["excludedEveryYear"] = "none";
        }));

        var proposal = Assert.Single(_proposals);
        Assert.Equal("updateSchedulePolicy", proposal.Kind);
        Assert.Equal(_business.Id, proposal.TargetId);
        Assert.Equal(3, proposal.ExpectedVersion);
        Assert.Equal("Business hours", proposal.Policy!.Name);
        Assert.Equal("Europe/Berlin", proposal.Policy.TimeZoneId);
        Assert.Equal("Office hours", proposal.Policy.Description);
        Assert.Equal("MondayToFriday", proposal.Policy.AllowedWeekdays);
        Assert.Equal("08:00:00", Assert.Single(proposal.Policy.AllowedTimeWindows).Start);
        Assert.Empty(proposal.Policy.ExcludedAnnualDates);
        Assert.Equal("09:00:00", Assert.Single(proposal.CurrentPolicy!.AllowedTimeWindows).Start);
        Assert.Single(proposal.CurrentPolicy.ExcludedAnnualDates);
    }

    [Theory]
    [InlineData("timeZoneId", "Mars/Olympus", "Unknown time zone")]
    [InlineData("dailyWindows", "9-5", "must use HH:mm-HH:mm")]
    [InlineData("dailyWindows", "09:00-09:00", "is empty")]
    [InlineData("allowedDays", "Funday", "allowedDays must name")]
    [InlineData("excludedDates", "25/12/2026", "must use YYYY-MM-DD")]
    [InlineData("excludedEveryYear", "02-30", "must use MM-DD")]
    [InlineData("requiredPolicies", "Night shift", "No schedule policy named 'Night shift'")]
    [InlineData("requiredPolicies", "Orders calendar", "No schedule policy named")]
    public async Task Invalid_policy_input_produces_no_card(string argument, string value, string message)
    {
        var result = await Invoke("propose_schedule_policy", PolicyArgs("Orders calendar", a => a[argument] = value));

        Assert.Contains(message, result);
        Assert.Empty(_proposals);
    }

    [Fact]
    public async Task A_policy_cannot_reference_itself_or_require_and_exclude_the_same_policy()
    {
        Assert.Contains("cannot require or exclude itself",
            await Invoke("propose_schedule_policy", PolicyArgs("Business hours", a => a["requiredPolicies"] = "Business hours")));
        Assert.Contains("both require and exclude",
            await Invoke("propose_schedule_policy", PolicyArgs("Orders calendar", a =>
            {
                a["requiredPolicies"] = "Maintenance window";
                a["blockedWhenPoliciesAllow"] = "maintenance window";
            })));
        Assert.Empty(_proposals);
    }

    [Fact]
    public async Task Cron_policy_can_be_switched_or_removed_but_not_set_to_the_same_policy()
    {
        Assert.Contains("already uses", await Invoke("propose_cron_schedule_policy",
            new() { ["cronTickerId"] = _cronId.ToString(), ["policyName"] = "business hours" }));
        Assert.Contains("No schedule policy named", await Invoke("propose_cron_schedule_policy",
            new() { ["cronTickerId"] = _cronId.ToString(), ["policyName"] = "Night shift" }));
        Assert.Empty(_proposals);

        await Invoke("propose_cron_schedule_policy", new() { ["cronTickerId"] = _cronId.ToString(), ["policyName"] = "maintenance window" });
        await Invoke("propose_cron_schedule_policy", new() { ["cronTickerId"] = _cronId.ToString(), ["policyName"] = "none" });

        Assert.Collection(_proposals,
            p =>
            {
                Assert.Equal("setCronSchedulePolicy", p.Kind);
                Assert.Equal(_cronId, p.TargetId);
                Assert.Equal("Maintenance window", p.SchedulePolicyName);
                Assert.Equal("Business hours", p.CurrentSchedulePolicyName);
                Assert.Equal("0 * * * *", p.Expression);
            },
            p =>
            {
                Assert.Equal("", p.SchedulePolicyName);
                Assert.Contains("no longer be checked", p.Warning);
            });
    }

    [Fact]
    public async Task New_cron_ticker_can_name_an_existing_policy()
    {
        var args = new Dictionary<string, object?>
        {
            ["functionName"] = "Orders.Import", ["expression"] = "0 * * * *", ["retries"] = 0,
            ["description"] = "", ["requestJson"] = "", ["schedulePolicyName"] = "Night shift",
        };
        Assert.Contains("No schedule policy named", await Invoke("propose_cron_ticker", args));
        Assert.Empty(_proposals);

        args["schedulePolicyName"] = "business hours";
        await Invoke("propose_cron_ticker", args);

        Assert.Equal("Business hours", Assert.Single(_proposals).SchedulePolicyName);
    }

    [Fact]
    public async Task Cron_tools_show_the_attached_policy()
    {
        var list = await Invoke("list_cron_tickers", new() { ["limit"] = 10 });

        Assert.Contains("schedulePolicy=Business hours", list);
    }
}
