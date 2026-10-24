using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Dashboard.Assistant;

internal static partial class TickerAssistantTools
{
    private const string KeepCurrent = "";
    private const string ClearValue = "none";

    /// <summary>
    /// Schedule policy tools. Reads are available to every assistant user. Proposals only emit a
    /// confirmation card; the dashboard's normal policy and cron endpoints apply them.
    /// </summary>
    private static void AddSchedulePolicyTools<TTimeTicker, TCronTicker>(IList<AITool> tools,
        ITickerDashboardDataService<TTimeTicker, TCronTicker> data, ISchedulePolicyManager policies,
        bool allowProposals, Func<AssistantProposal, Task>? emit, CancellationToken ct)
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        tools.Add(AIFunctionFactory.Create(async () =>
        {
            var all = await policies.ListAsync(ct);
            if (all.Count == 0) return "No schedule policies exist.";
            var crons = (await data.GetCronTickersFlatAsync(new CronTickerQueryFilter { PageSize = 200 }, ct)).Items.ToList();
            var sb = new StringBuilder($"{all.Count} schedule policies. Days, hours and dates are evaluated in each policy's own time zone.\n");
            foreach (var p in all.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Take(50))
            {
                var users = crons.Where(c => SamePolicyName(c.SchedulePolicyName, p.Name)).ToList();
                sb.AppendLine($"- {p.Name} | id={p.Id} | version={p.Version} | zone={p.TimeZoneId}");
                sb.AppendLine($"  days={DescribeDays(p.AllowedWeekdays)} | hours={DescribeWindows(p.AllowedTimeWindows)}");
                sb.AppendLine($"  excludedDates={JoinOrNone(p.ExcludedDates.Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))} | " +
                              $"everyYear={JoinOrNone(p.ExcludedAnnualDates.Select(d => $"{d.Month:00}-{d.Day:00}"))} | dateProviders={JoinOrNone(p.DateProviderNames)}");
                sb.AppendLine($"  requires={JoinOrNone(p.RequiredPolicyNames)} | blockedWhenAllowedBy={JoinOrNone(p.ExcludedWhenPolicyAllowsNames)}");
                if (!string.IsNullOrWhiteSpace(p.Description)) sb.AppendLine($"  description={Trim(p.Description)}");
                sb.AppendLine(users.Count == 0
                    ? "  usedByCron=none"
                    : $"  usedByCron={string.Join(", ", users.Take(10).Select(c => $"{c.FunctionName} ({c.Id})"))}{(users.Count > 10 ? $" and {users.Count - 10} more" : "")}");
            }
            return sb.Length <= 24000 ? sb.ToString() : sb.ToString(0, 24000) + "\n[Output truncated]";
        }, "list_schedule_policies",
            "List schedule policies (availability calendars) with their time zone, allowed days and hours, excluded dates, composition with other policies, and which cron tickers use them. " +
            "Schedule policies are NOT cron tickers; use this whenever the user asks about policies, calendars, business hours, blackout dates, or maintenance windows."));

        tools.Add(AIFunctionFactory.Create(async (
            [Description("Exact schedule policy name from list_schedule_policies.")] string policyName,
            [Description("Cron expression to test, 5 or 6 fields, e.g. '0 9 * * *'.")] string expression,
            [Description("How many upcoming occurrences to check, 1–20. Default 10.")] int count) =>
        {
            var policy = await FindPolicyAsync(policies, policyName, ct);
            if (policy == null) return $"No schedule policy named '{policyName}'. Use list_schedule_policies.";
            var exprError = ValidateCronExpression(expression);
            if (exprError != null) return exprError;
            var items = await policies.PreviewCronAsync(policy.Name, expression.Trim(), DateTime.UtcNow,
                count is < 1 or > 20 ? 10 : count, policy.TimeZoneId, ct);
            if (items.Count == 0) return "The expression has no upcoming occurrences.";
            var zone = SchedulePolicyEvaluator.ResolveTimeZone(policy.TimeZoneId);
            var sb = new StringBuilder($"Policy '{policy.Name}' ({policy.TimeZoneId}), expression '{expression.Trim()}' evaluated in the policy time zone. " +
                $"{items.Count(x => x.IsAllowed)} allowed, {items.Count(x => !x.IsAllowed)} blocked.\n");
            foreach (var item in items)
            {
                var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(item.RequestedAtUtc, DateTimeKind.Utc), zone);
                sb.AppendLine($"- {local:ddd yyyy-MM-dd HH:mm} local ({Fmt(item.RequestedAtUtc)}) | {(item.IsAllowed ? "ALLOWED" : "BLOCKED")} | {Trim(item.Reason ?? "")}");
            }
            return sb.ToString();
        }, "preview_schedule_policy",
            "Check which upcoming occurrences of a cron expression a schedule policy allows or blocks, with the reason for each. Read-only."));

        if (!allowProposals || emit == null) return;

        tools.Add(AIFunctionFactory.Create(async (
            [Description("Policy name, 1–200 characters. If a policy with this name exists, the card updates it; otherwise it creates a new one.")] string name,
            [Description("Description. Empty keeps the current one (or none for a new policy); 'none' clears it.")] string description,
            [Description("IANA or Windows time zone id, e.g. Europe/Berlin or UTC. Empty keeps the current zone (UTC for a new policy). Ask the user when unclear.")] string timeZoneId,
            [Description("Allowed days: comma-separated day names, or All, MondayToFriday, Weekend. Empty keeps the current days (MondayToFriday for a new policy).")] string allowedDays,
            [Description("Allowed daily hours as HH:mm-HH:mm, comma-separated, e.g. '09:00-17:00, 18:00-20:00'. Windows may cross midnight. 'all day' allows every hour. Empty keeps the current hours (all day for a new policy).")] string dailyWindows,
            [Description("Excluded exact dates as YYYY-MM-DD, comma-separated. Empty keeps the current list; 'none' clears it.")] string excludedDates,
            [Description("Dates excluded every year as MM-DD, comma-separated, e.g. '12-25, 01-01'. Empty keeps the current list; 'none' clears it.")] string excludedEveryYear,
            [Description("Names of other policies that must ALSO allow a time, comma-separated. Empty keeps the current list; 'none' clears it.")] string requiredPolicies,
            [Description("Names of other policies that BLOCK a time whenever they allow it (for example a maintenance window), comma-separated. Empty keeps the current list; 'none' clears it.")] string blockedWhenPoliciesAllow) =>
        {
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200) return "Policy name must contain 1–200 characters.";
            var all = await policies.ListAsync(ct);
            var existing = all.FirstOrDefault(x => SamePolicyName(x.Name, name));
            var draft = existing == null
                ? new AssistantSchedulePolicyDraft { Name = name.Trim(), AllowedWeekdays = "MondayToFriday" }
                : AssistantSchedulePolicyDraft.From(existing);

            if (!string.IsNullOrEmpty(description?.Trim()))
            {
                var value = description.Trim();
                if (value.Length > 2000) return "Description must contain at most 2,000 characters.";
                draft.Description = IsClear(value) ? null : value;
            }

            if (!string.IsNullOrWhiteSpace(timeZoneId))
            {
                try { _ = SchedulePolicyEvaluator.ResolveTimeZone(timeZoneId.Trim()); }
                catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
                { return $"Unknown time zone '{timeZoneId}'. Use an id such as Europe/Berlin or UTC."; }
                draft.TimeZoneId = timeZoneId.Trim();
            }

            if (!string.IsNullOrWhiteSpace(allowedDays))
            {
                if (!TryParseWeekdays(allowedDays, out var days) || days == ScheduleWeekdays.None)
                    return "allowedDays must name at least one day: Monday…Sunday, or All, MondayToFriday, Weekend.";
                draft.AllowedWeekdays = days.ToString();
            }

            if (!string.IsNullOrWhiteSpace(dailyWindows))
            {
                if (!TryParseWindows(dailyWindows, out var windows, out var windowError)) return windowError;
                draft.AllowedTimeWindows = windows;
            }

            if (!string.IsNullOrWhiteSpace(excludedDates))
            {
                var parsed = new List<string>();
                if (!IsClear(excludedDates))
                    foreach (var part in Split(excludedDates))
                    {
                        if (!DateOnly.TryParseExact(part, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                            return $"Excluded date '{part}' must use YYYY-MM-DD.";
                        parsed.Add(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    }
                if (parsed.Count > 366) return "Use at most 366 excluded dates.";
                draft.ExcludedDates = parsed;
            }

            if (!string.IsNullOrWhiteSpace(excludedEveryYear))
            {
                var parsed = new List<ScheduleAnnualDate>();
                if (!IsClear(excludedEveryYear))
                    foreach (var part in Split(excludedEveryYear))
                    {
                        if (!DateOnly.TryParseExact($"2000-{part}", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                            return $"Yearly date '{part}' must use MM-DD.";
                        parsed.Add(new ScheduleAnnualDate(date.Month, date.Day));
                    }
                draft.ExcludedAnnualDates = parsed;
            }

            if (!string.IsNullOrWhiteSpace(requiredPolicies))
            {
                var names = ResolvePolicyNames(all, requiredPolicies, out var unknown);
                if (unknown != null) return $"No schedule policy named '{unknown}'. Use list_schedule_policies.";
                draft.RequiredPolicyNames = names;
            }

            if (!string.IsNullOrWhiteSpace(blockedWhenPoliciesAllow))
            {
                var names = ResolvePolicyNames(all, blockedWhenPoliciesAllow, out var unknown);
                if (unknown != null) return $"No schedule policy named '{unknown}'. Use list_schedule_policies.";
                draft.ExcludedWhenPolicyAllowsNames = names;
            }

            if (draft.RequiredPolicyNames.Concat(draft.ExcludedWhenPolicyAllowsNames).Any(x => SamePolicyName(x, draft.Name)))
                return "A policy cannot require or exclude itself.";
            if (draft.RequiredPolicyNames.Any(x => draft.ExcludedWhenPolicyAllowsNames.Any(y => SamePolicyName(x, y))))
                return "A policy cannot both require and exclude the same policy.";

            await emit(new AssistantProposal
            {
                Kind = existing == null ? "createSchedulePolicy" : "updateSchedulePolicy",
                TargetId = existing?.Id,
                ExpectedVersion = existing?.Version,
                Policy = draft,
                CurrentPolicy = existing == null ? null : AssistantSchedulePolicyDraft.From(existing),
                Warning = existing == null
                    ? "Creates a reusable calendar. It affects no job until a ticker uses it."
                    : "Updating a policy changes when every ticker that uses it may run. Saving fails if someone else changed the policy first.",
            });
            return ProposalShown;
        }, "propose_schedule_policy",
            "Draft a NEW schedule policy, or a change to an existing one with the same name, for the user to confirm. " +
            "For updates, empty arguments keep the current values. Nothing is saved until the user clicks Confirm."));

        tools.Add(AIFunctionFactory.Create(async (
            [Description("Id (GUID) of the cron ticker, from list_cron_tickers.")] string cronTickerId,
            [Description("Exact schedule policy name to use, or 'none' to remove the current policy.")] string policyName) =>
        {
            if (!Guid.TryParse(cronTickerId, out var id)) return "Invalid cron ticker id.";
            var cron = await data.GetCronTickerByIdAsync(id, ct);
            if (cron == null) return "No cron ticker with that id — use list_cron_tickers to find the right one.";
            string? target = null;
            if (!IsClear(policyName))
            {
                var policy = await FindPolicyAsync(policies, policyName, ct);
                if (policy == null) return $"No schedule policy named '{policyName}'. Use list_schedule_policies.";
                target = policy.Name;
            }
            if (SamePolicyName(cron.SchedulePolicyName, target) || (string.IsNullOrEmpty(cron.SchedulePolicyName) && target == null))
                return target == null ? "That cron ticker has no schedule policy." : $"That cron ticker already uses '{target}'.";

            await emit(new AssistantProposal
            {
                Kind = "setCronSchedulePolicy",
                TargetId = cron.Id,
                Function = cron.FunctionName,
                Expression = cron.Expression,
                SchedulePolicyName = target ?? "",
                CurrentSchedulePolicyName = cron.SchedulePolicyName ?? "",
                Warning = target == null
                    ? "Occurrences will no longer be checked against a schedule policy."
                    : "Occurrences outside the policy will be skipped. Preview the policy first if you are unsure.",
            });
            return ProposalShown;
        }, "propose_cron_schedule_policy",
            "Draft attaching a schedule policy to an EXISTING cron ticker, switching it to another policy, or removing it ('none'). Requires the user's confirmation."));
    }

    private static async Task<SchedulePolicy?> FindPolicyAsync(ISchedulePolicyManager policies, string name, CancellationToken ct)
        => string.IsNullOrWhiteSpace(name)
            ? null
            : (await policies.ListAsync(ct)).FirstOrDefault(x => SamePolicyName(x.Name, name));

    private static bool SamePolicyName(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
           SchedulePolicyNames.Normalize(left) == SchedulePolicyNames.Normalize(right);

    private static bool IsClear(string? value) => string.Equals(value?.Trim(), ClearValue, StringComparison.OrdinalIgnoreCase);

    private static List<string> ResolvePolicyNames(IReadOnlyList<SchedulePolicy> all, string value, out string? unknown)
    {
        unknown = null;
        var names = new List<string>();
        if (IsClear(value)) return names;
        foreach (var part in Split(value))
        {
            var match = all.FirstOrDefault(x => SamePolicyName(x.Name, part));
            if (match == null) { unknown = part; return names; }
            if (!names.Contains(match.Name)) names.Add(match.Name);
        }
        return names;
    }

    internal static bool TryParseWeekdays(string value, out ScheduleWeekdays days)
    {
        days = ScheduleWeekdays.None;
        foreach (var part in Split(value))
        {
            var key = part.Replace(" ", "", StringComparison.Ordinal);
            if (!Enum.TryParse<ScheduleWeekdays>(key, ignoreCase: true, out var parsed) || parsed == ScheduleWeekdays.None ||
                int.TryParse(key, out _))
            {
                // Accept three-letter abbreviations such as "Mon".
                var day = Enum.GetNames<ScheduleWeekdays>().FirstOrDefault(n =>
                    n != nameof(ScheduleWeekdays.None) && n.Length > 3 && key.Length >= 3 &&
                    n.StartsWith(key, StringComparison.OrdinalIgnoreCase));
                if (day == null) return false;
                parsed = Enum.Parse<ScheduleWeekdays>(day);
            }
            days |= parsed;
        }
        return days != ScheduleWeekdays.None;
    }

    internal static bool TryParseWindows(string value, out List<AssistantScheduleWindow> windows, out string error)
    {
        windows = new List<AssistantScheduleWindow>();
        error = "";
        var trimmed = value.Trim();
        if (trimmed.Equals("all day", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("allday", StringComparison.OrdinalIgnoreCase) || IsClear(trimmed))
            return true;
        foreach (var part in Split(trimmed))
        {
            var bounds = part.Split('-', StringSplitOptions.TrimEntries);
            if (bounds.Length != 2 ||
                !TimeOnly.TryParseExact(bounds[0], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
                !TimeOnly.TryParseExact(bounds[1], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            {
                error = $"Daily window '{part}' must use HH:mm-HH:mm, for example 09:00-17:00.";
                return false;
            }
            if (start == end)
            {
                error = $"Daily window '{part}' is empty. Use 'all day' to allow every hour.";
                return false;
            }
            windows.Add(new AssistantScheduleWindow { Start = start.ToString("HH:mm:ss", CultureInfo.InvariantCulture), End = end.ToString("HH:mm:ss", CultureInfo.InvariantCulture) });
        }
        if (windows.Count > 24) { error = "Use at most 24 daily windows."; return false; }
        return true;
    }

    private static string DescribeDays(ScheduleWeekdays days) => days switch
    {
        ScheduleWeekdays.All => "every day",
        ScheduleWeekdays.MondayToFriday => "Monday–Friday",
        ScheduleWeekdays.Weekend => "Saturday, Sunday",
        _ => days.ToString(),
    };

    private static string DescribeWindows(IReadOnlyCollection<ScheduleTimeWindow> windows)
        => windows == null || windows.Count == 0
            ? "all day"
            : string.Join(", ", windows.Select(w => $"{w.Start:hh\\:mm}-{w.End:hh\\:mm}"));

    private static string JoinOrNone(IEnumerable<string> values)
    {
        var list = values?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? new List<string>();
        return list.Count == 0 ? "none" : string.Join(", ", list.Take(30)) + (list.Count > 30 ? $" (+{list.Count - 30})" : "");
    }
}
