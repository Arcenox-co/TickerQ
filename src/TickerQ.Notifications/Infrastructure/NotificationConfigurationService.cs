using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using TickerQ.Utilities.Notifications;

namespace TickerQ.Notifications.Infrastructure;

internal sealed class NotificationConfigurationService(
    ITickerNotificationSettingsStore store, IDataProtectionProvider protection,
    NotificationOptions options, IEnumerable<INotificationPayloadFormatter> formatters)
{
    private readonly IDataProtector _protector = protection.CreateProtector("TickerQ.Notifications.Configuration.v1");
    internal readonly Dictionary<string, INotificationPayloadFormatter> Formatters = formatters.ToDictionary(f => f.Name, StringComparer.Ordinal);
    internal bool Persistent => store.NotificationSettingsArePersistent;
    internal static readonly string[] Events = ["failed", "timeout_cancelled", "stale_recovered", "contract_drift"];

    internal async Task<NotificationConfiguration> LoadAsync(CancellationToken ct)
    {
        var row = await store.ReadNotificationSettingsAsync(ct).ConfigureAwait(false);
        if (row == null) return new();
        var result = JsonSerializer.Deserialize(_protector.Unprotect(row.ProtectedConfiguration), NotificationJsonContext.Default.NotificationConfiguration)
            ?? throw new InvalidOperationException("Invalid notification configuration.");
        result.Version = row.Version;
        return result;
    }

    internal async Task SaveAsync(NotificationConfiguration input, CancellationToken ct)
    {
        if (input == null) throw new ArgumentException("Notification configuration is required.");
        // Clone caller-owned input and never modify the object supplied by an application.
        var next = JsonSerializer.Deserialize(JsonSerializer.Serialize(input, NotificationJsonContext.Default.NotificationConfiguration), NotificationJsonContext.Default.NotificationConfiguration)!;
        var old = await LoadAsync(ct).ConfigureAwait(false);
        if (old.Version != input.Version) throw new NotificationConfigurationConflictException();
        if (next.Destinations == null || next.Rules == null || next.Destinations.Count > 32 || next.Rules.Count > 100)
            throw new ArgumentException("Use at most 32 destinations and 100 rules.");
        if (next.Destinations.Any(x => x == null) || next.Rules.Any(x => x == null)) throw new ArgumentException("Destinations and rules cannot contain null entries.");
        if (next.Destinations.Select(x => x.Id).Distinct().Count() != next.Destinations.Count || next.Rules.Select(x => x.Id).Distinct().Count() != next.Rules.Count)
            throw new ArgumentException("Destination and rule IDs must be unique.");
        foreach (var d in next.Destinations)
        {
            ValidateName(d.Id, d.Name);
            if (d.Provider == null || !Formatters.TryGetValue(d.Provider, out var formatter)) throw new ArgumentException("Unknown notification provider.");
            var previous = old.Destinations.SingleOrDefault(x => x.Id == d.Id);
            if (d.Url == null && previous?.Provider == d.Provider) d.Url = previous.Url;
            // A provider change must not forward another destination's saved credentials.
            d.Headers ??= previous?.Provider == d.Provider ? previous.Headers ?? [] : [];
            var uri = WebhookSecurity.ValidateUrl(d.Url, options);
            d.EndpointHost = uri.Host; d.HasUrl = true; d.HasHeaders = d.Headers.Count > 0;
            if (d.Headers.Count > 16) throw new ArgumentException("At most 16 custom headers are allowed.");
            if (d.Headers.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != d.Headers.Count)
                throw new ArgumentException("Custom header names must be unique, ignoring case.");
            foreach (var (name, value) in d.Headers)
                if (name.Length > 100 || !Regex.IsMatch(name, "^[A-Za-z0-9-]+$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50))
                    || value == null || value.Length > 4096 || value.Any(c => c == '\r' || c == '\n' || c == '\0')
                    || new[] { "Host", "Content-Type", "Content-Length", "Connection", "Cookie", "Transfer-Encoding", "Expect", "Upgrade", "Trailer", "TE" }.Contains(name, StringComparer.OrdinalIgnoreCase)
                    || name.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Sec-", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Invalid or reserved custom HTTP header.");
            if (!string.IsNullOrWhiteSpace(d.PayloadTemplate)) PayloadTemplates.Render(d.PayloadTemplate, NotificationEngine.Sample(options));
            var payload = formatter.Format(d, NotificationEngine.Sample(options)).ToJsonString();
            if (System.Text.Encoding.UTF8.GetByteCount(payload) > 24000) throw new ArgumentException("Rendered payload is too large.");
        }
        foreach (var rule in next.Rules)
        {
            ValidateName(rule.Id, rule.Name);
            if (rule.DestinationIds == null || rule.DestinationIds.Count is 0 or > 32 || rule.DestinationIds.Any(id => !next.Destinations.Any(d => d.Id == id)))
                throw new ArgumentException("Each rule must reference existing destinations.");
            if (rule.Events == null || rule.Events.Count == 0 || rule.Events.Any(e => !Events.Contains(e))) throw new ArgumentException("Choose supported notification events.");
            ValidatePatterns(rule.IncludeFunctions); ValidatePatterns(rule.ExcludeFunctions);
            if (rule.CooldownSeconds is < 0 or > 86400) throw new ArgumentException("Cooldown must be between 0 and 86,400 seconds.");
        }
        next.Version = Guid.NewGuid();
        var encrypted = _protector.Protect(JsonSerializer.Serialize(next, NotificationJsonContext.Default.NotificationConfiguration));
        if (encrypted.Length > 500000) throw new ArgumentException("Notification configuration is too large.");
        if (!await store.SaveNotificationSettingsAsync(new(next.Version, encrypted), old.Version, ct).ConfigureAwait(false))
            throw new NotificationConfigurationConflictException();
    }

    internal static NotificationConfiguration Redact(NotificationConfiguration configuration)
    {
        foreach (var d in configuration.Destinations)
        {
            d.HasUrl = !string.IsNullOrEmpty(d.Url); d.HasHeaders = d.Headers?.Count > 0;
            d.EndpointHost = Uri.TryCreate(d.Url, UriKind.Absolute, out var uri) ? uri.Host : null;
            d.Url = null; d.Headers = null;
        }
        return configuration;
    }

    private static void ValidateName(Guid id, string name)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(name) || name.Length > 80) throw new ArgumentException("Each destination and rule needs an ID and a name of at most 80 characters.");
    }
    private static void ValidatePatterns(List<string>? values)
    {
        if (values == null || values.Count > 100 || values.Any(v => string.IsNullOrWhiteSpace(v) || v.Length > 256))
            throw new ArgumentException("Function filters accept at most 100 patterns, each 1–256 characters.");
    }

    internal static bool Matches(NotificationRule rule, Utilities.Models.TickerFailureEvent e)
        => rule.Enabled && rule.Events.Contains(e.Kind)
            && (rule.IncludeFunctions.Count == 0 || rule.IncludeFunctions.Any(p => Glob(p, e.Function ?? "")))
            && !rule.ExcludeFunctions.Any(p => Glob(p, e.Function ?? ""));

    private static bool Glob(string pattern, string value)
    {
        int p = 0, v = 0, star = -1, retry = 0;
        while (v < value.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == value[v])) { p++; v++; }
            else if (p < pattern.Length && pattern[p] == '*') { star = p++; retry = v; }
            else if (star >= 0) { p = star + 1; v = ++retry; }
            else return false;
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }
}
