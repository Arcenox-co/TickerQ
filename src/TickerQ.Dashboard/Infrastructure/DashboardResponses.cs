using System;
using TickerQ.Utilities.Enums;

namespace TickerQ.Dashboard.Infrastructure;

internal class AuthInfoResponse
{
    public string Mode { get; set; }
    public bool Enabled { get; set; }
    public int SessionTimeout { get; set; }

    /// <summary>
    /// Names of every registered auth scheme in priority order — lets the SPA
    /// pick the right UX (e.g. show a login form when "jwt" or "cookie" is
    /// present, hide it entirely for "host"-only setups).
    /// </summary>
    public string[] Schemes { get; set; } = System.Array.Empty<string>();

    /// <summary>True when the dashboard issues credentials via /api/auth/login.</summary>
    public bool LoginAvailable { get; set; }

    /// <summary>
    /// Host-app login page (Host mode only). The SPA does a full-page
    /// redirect here with <c>?returnUrl=…</c> when a session expires.
    /// </summary>
    public string LoginRedirect { get; set; }
}

internal class AuthValidateResponse
{
    public bool Authenticated { get; set; }
    public string Username { get; set; }
    public string Message { get; set; }
}

internal class DashboardOptionsResponse
{
    public int MaxConcurrency { get; set; }
    public TimeSpan IdleWorkerTimeOut { get; set; }
    public string CurrentMachine { get; set; }
    public string LastHostExceptionMessage { get; set; }
    public string SchedulerTimeZone { get; set; }

    /// <summary>Minimum interval between database polls (guards against tight loops).</summary>
    public TimeSpan MinPollingInterval { get; set; }

    /// <summary>Interval of the fallback due-work checker.</summary>
    public TimeSpan FallbackIntervalChecker { get; set; }

    /// <summary>Global per-attempt execution timeout applied to tickers with no own timeout. Null → no timeout.</summary>
    public TimeSpan? DefaultExecutionTimeout { get; set; }

    /// <summary>True when the stale-job watchdog recovers jobs abandoned by a dead node.</summary>
    public bool StaleJobRecoveryEnabled { get; set; }

    /// <summary>Historical-record retention state exposed to the dashboard.</summary>
    public RetentionConfigResponse Retention { get; set; }
}

internal class RetentionConfigResponse
{
    public bool Enabled { get; set; }
    public TimeSpan? DeleteSucceededAfter { get; set; }
    public TimeSpan? DeleteFailedAfter { get; set; }
    public TimeSpan? DeleteCancelledAfter { get; set; }
    public TimeSpan? DeleteSkippedAfter { get; set; }
    public TimeSpan SweepInterval { get; set; }
    public int BatchSize { get; set; }
    public int MaxBatchesPerSweep { get; set; }
    public int MaxNodesPerChain { get; set; }
}

internal class ActionResponse
{
    public bool Success { get; set; }
    public string Message { get; set; }
}

internal class ActionResponseWithId
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public Guid? TickerId { get; set; }
}

internal class TickerRequestResponse
{
    public string Result { get; set; }
    public int MatchType { get; set; }
}

internal class TickerFunctionResponse
{
    public string FunctionName { get; set; }
    public string FunctionRequestNamespace { get; set; }
    public string FunctionRequestType { get; set; }
    public int Priority { get; set; }
}

internal class NextTickerResponse
{
    public DateTime? NextOccurrence { get; set; }
}

internal class HostStatusResponse
{
    public bool IsRunning { get; set; }
}

/// <summary>
/// Minimal, safe projection of the shared license state for the Dashboard. Deliberately excludes the raw
/// certificate, envelope, payload, signature, public key, file path, and any internal exception detail.
/// </summary>
internal sealed class LicenseResponse
{
    public string Status { get; set; }
    public bool ExecutionAllowed { get; set; }
    public string Message { get; set; }
    public string ActionLabel { get; set; }
    public string ActionUrl { get; set; }
    public Guid? WorkspaceId { get; set; }
    public string WorkspaceName { get; set; }
    public string Plan { get; set; }
    public string Kind { get; set; }
    public bool IsEvaluation { get; set; }
    public Guid? LicenseId { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public int? DaysRemaining { get; set; }
    public int? SchemaVersion { get; set; }
    public string Algorithm { get; set; }
    public string KeyId { get; set; }
    public string AnchoredMinorLine { get; set; }
}

internal class TupleResponse<T1, T2>
{
    public T1 Item1 { get; set; }
    public T2 Item2 { get; set; }
}

internal class FrontendConfigResponse
{
    public string BasePath { get; set; }
    public string BackendDomain { get; set; }

    /// <summary>Dashboard package version (assembly informational version, without commit hash).</summary>
    public string Version { get; set; }

    /// <summary>Customer-branded title shown in the shell + browser tab.</summary>
    public string Title { get; set; }

    /// <summary>Optional customer logo URL.</summary>
    public string LogoUrl { get; set; }

    /// <summary>When true the SPA hides all mutation UI (server also rejects writes with 403).</summary>
    public bool ReadOnly { get; set; }

    /// <summary>Default rendering time zone id (IANA on Linux/macOS). Null → browser zone.</summary>
    public string Timezone { get; set; }

    public RealtimeConfigResponse Realtime { get; set; }

    public AssistantConfigResponse Assistant { get; set; }

    public AuthInfoResponse Auth { get; set; }
}

internal class RealtimeConfigResponse
{
    public bool Enabled { get; set; }

    /// <summary>Hub route relative to the dashboard base path, e.g. "tickerq-notification-hub".</summary>
    public string HubPath { get; set; }
}

internal class AssistantConfigResponse
{
    /// <summary>True only when the operator configured a chat client. Gates the whole chat UI.</summary>
    public bool Enabled { get; set; }

    /// <summary>Cosmetic model label for the chat panel header.</summary>
    public string Model { get; set; }

    /// <summary>
    /// True when an IAssistantHistoryStore is registered — the SPA shows the
    /// per-user conversation list and loads history from the server instead
    /// of the browser's localStorage.
    /// </summary>
    public bool History { get; set; }
}
