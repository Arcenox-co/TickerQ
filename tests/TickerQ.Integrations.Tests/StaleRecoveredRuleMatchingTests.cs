using TickerQ.Integrations.Infrastructure;
using TickerQ.Utilities.Integrations;
using TickerQ.Utilities.Models;

namespace TickerQ.Integrations.Tests;

/// <summary>
/// A stale-recovery summary covers a whole sweep, so it carries no function name. These tests pin
/// the intended matching: it reaches rules without a function filter (or a bare "*"), and never
/// rules scoped to specific functions.
/// </summary>
public class StaleRecoveredRuleMatchingTests
{
    private static readonly TickerFailureEvent Summary = new()
    {
        Kind = "stale_recovered",
        Reason = "1 restarted, 0 cancelled after their executing node stopped renewing its lease",
        Function = null,
    };

    [Fact]
    public void Matches_rule_without_function_filters()
        => Assert.True(IntegrationConfigurationService.Matches(Rule(), Summary));

    [Fact]
    public void Matches_rule_whose_include_filter_is_a_bare_wildcard()
        => Assert.True(IntegrationConfigurationService.Matches(Rule(include: ["*"]), Summary));

    [Theory]
    [InlineData("Orders.*")]
    [InlineData("Orders.Import")]
    public void Does_not_match_rule_scoped_to_specific_functions(string include)
        => Assert.False(IntegrationConfigurationService.Matches(Rule(include: [include]), Summary));

    [Fact]
    public void Specific_exclude_filter_does_not_block_the_summary()
        => Assert.True(IntegrationConfigurationService.Matches(Rule(exclude: ["Orders.*"]), Summary));

    [Fact]
    public void Rule_must_subscribe_to_the_stale_recovered_event()
    {
        var rule = Rule();
        rule.Events = ["failed"];
        Assert.False(IntegrationConfigurationService.Matches(rule, Summary));
    }

    private static IntegrationRule Rule(List<string>? include = null, List<string>? exclude = null) => new()
    {
        Name = "Recovery",
        Events = ["stale_recovered"],
        IncludeFunctions = include ?? [],
        ExcludeFunctions = exclude ?? [],
    };
}
