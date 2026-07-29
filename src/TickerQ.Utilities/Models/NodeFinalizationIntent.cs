using System;
using System.Collections.Generic;
using System.Text.Json;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Models;

/// <summary>
/// Immutable, secret-free description of the exact Node finalize request that must be
/// delivered after the matching terminal ticker mutation commits.
/// </summary>
public sealed record NodeFinalizationIntent
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxExactBodyBytes = 8 * 1024;
    public const int MaxUriLength = 2048;
    public const int MaxPathAndQueryLength = 2048;

    private readonly byte[] _exactBody;

    public NodeFinalizationIntent(
        int schemaVersion,
        Guid outboxId,
        TickerType tickerType,
        Guid tickerId,
        Guid acquisitionToken,
        Guid dispatchId,
        Guid nodeEpoch,
        string finalizeUri,
        string finalizePathAndQuery,
        bool allowPrivateCallbackAddressesForLocalDevelopment,
        Guid requestNonce,
        Guid controlNonce,
        byte[] exactBody,
        DateTime createdAtUtc)
    {
        if (schemaVersion != CurrentSchemaVersion) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        if (outboxId == Guid.Empty) throw new ArgumentException("Outbox ID is required.", nameof(outboxId));
        if (dispatchId == Guid.Empty) throw new ArgumentException("Dispatch ID is required.", nameof(dispatchId));
        if (outboxId != dispatchId) throw new ArgumentException("Outbox ID must equal dispatch ID.", nameof(outboxId));
        if (tickerId == Guid.Empty) throw new ArgumentException("Ticker ID is required.", nameof(tickerId));
        if (acquisitionToken == Guid.Empty) throw new ArgumentException("Acquisition token is required.", nameof(acquisitionToken));
        if (nodeEpoch == Guid.Empty) throw new ArgumentException("Node epoch is required.", nameof(nodeEpoch));
        if (requestNonce == Guid.Empty) throw new ArgumentException("Request nonce is required.", nameof(requestNonce));
        if (controlNonce == Guid.Empty) throw new ArgumentException("Control nonce is required.", nameof(controlNonce));
        if (createdAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("CreatedAtUtc must be UTC.", nameof(createdAtUtc));
        if (string.IsNullOrWhiteSpace(finalizeUri) || finalizeUri.Length > MaxUriLength)
            throw new ArgumentException($"Finalize URI must be non-empty and at most {MaxUriLength} characters.", nameof(finalizeUri));
        if (!Uri.TryCreate(finalizeUri, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Finalize URI must be an absolute HTTP(S) URI without credentials, a query, or a fragment.", nameof(finalizeUri));
        if (string.IsNullOrWhiteSpace(finalizePathAndQuery) || finalizePathAndQuery.Length > MaxPathAndQueryLength ||
            !string.Equals(uri.PathAndQuery, finalizePathAndQuery, StringComparison.Ordinal))
            throw new ArgumentException("Finalize path and query must exactly match the finalize URI.", nameof(finalizePathAndQuery));
        ArgumentNullException.ThrowIfNull(exactBody);
        if (exactBody.Length is 0 or > MaxExactBodyBytes)
            throw new ArgumentException($"Exact body must contain between 1 and {MaxExactBodyBytes} bytes.", nameof(exactBody));

        ValidateExactBody(exactBody, tickerType, tickerId, acquisitionToken, dispatchId, nodeEpoch, controlNonce);

        SchemaVersion = schemaVersion;
        OutboxId = outboxId;
        TickerType = tickerType;
        TickerId = tickerId;
        AcquisitionToken = acquisitionToken;
        DispatchId = dispatchId;
        NodeEpoch = nodeEpoch;
        FinalizeUri = finalizeUri;
        FinalizePathAndQuery = finalizePathAndQuery;
        AllowPrivateCallbackAddressesForLocalDevelopment = allowPrivateCallbackAddressesForLocalDevelopment;
        RequestNonce = requestNonce;
        ControlNonce = controlNonce;
        _exactBody = (byte[])exactBody.Clone();
        CreatedAtUtc = createdAtUtc;
    }

    private static void ValidateExactBody(byte[] exactBody, TickerType tickerType, Guid tickerId,
        Guid acquisitionToken, Guid dispatchId, Guid nodeEpoch, Guid controlNonce)
    {
        try
        {
            using var document = JsonDocument.Parse(exactBody, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Exact body must be a JSON object.", nameof(exactBody));

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var count = 0;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                count++;
                if (!seen.Add(property.Name))
                    throw new ArgumentException($"Exact body contains duplicate field '{property.Name}'.", nameof(exactBody));
                switch (property.Name)
                {
                    case "tickerType":
                        if (property.Value.ValueKind != JsonValueKind.Number ||
                            !property.Value.TryGetInt32(out var rawTickerType) ||
                            !Enum.IsDefined(typeof(TickerType), rawTickerType) ||
                            (TickerType)rawTickerType != tickerType)
                            throw new ArgumentException("Exact body tickerType is invalid or mismatched.", nameof(exactBody));
                        break;
                    case "tickerId": ValidateGuid(property.Value, tickerId, property.Name, exactBody); break;
                    case "acquisitionToken": ValidateGuid(property.Value, acquisitionToken, property.Name, exactBody); break;
                    case "dispatchId": ValidateGuid(property.Value, dispatchId, property.Name, exactBody); break;
                    case "nodeEpoch": ValidateGuid(property.Value, nodeEpoch, property.Name, exactBody); break;
                    case "controlNonce": ValidateGuid(property.Value, controlNonce, property.Name, exactBody); break;
                    default:
                        throw new ArgumentException($"Exact body contains unexpected field '{property.Name}'.", nameof(exactBody));
                }
            }

            if (count != 6 || seen.Count != 6 ||
                !seen.SetEquals(["tickerType", "tickerId", "acquisitionToken", "dispatchId", "nodeEpoch", "controlNonce"]))
                throw new ArgumentException("Exact body must contain exactly the six canonical control fields.", nameof(exactBody));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Exact body must contain valid bounded JSON.", nameof(exactBody), exception);
        }
    }

    private static void ValidateGuid(JsonElement value, Guid expected, string fieldName, byte[] exactBody)
    {
        if (value.ValueKind != JsonValueKind.String ||
            !Guid.TryParseExact(value.GetString(), "D", out var parsed) || parsed == Guid.Empty || parsed != expected)
            throw new ArgumentException($"Exact body {fieldName} is invalid or mismatched.", nameof(exactBody));
    }

    public int SchemaVersion { get; }
    public Guid OutboxId { get; }
    public TickerType TickerType { get; }
    public Guid TickerId { get; }
    public Guid AcquisitionToken { get; }
    public Guid DispatchId { get; }
    public Guid NodeEpoch { get; }
    public string FinalizeUri { get; }
    public string FinalizePathAndQuery { get; }
    public bool AllowPrivateCallbackAddressesForLocalDevelopment { get; }
    public Guid RequestNonce { get; }
    public Guid ControlNonce { get; }
    public byte[] ExactBody => (byte[])_exactBody.Clone();
    public DateTime CreatedAtUtc { get; }
}

/// <summary>A provider-issued, generation- and lease-fenced claim over one finalization intent.</summary>
public sealed record NodeFinalizationClaim
{
    public const int MaxClaimedByLength = 256;

    public NodeFinalizationClaim(NodeFinalizationIntent intent, Guid claimToken, string claimedBy,
        DateTime leaseUntilUtc, int attemptCount)
    {
        Intent = intent ?? throw new ArgumentNullException(nameof(intent));
        if (claimToken == Guid.Empty) throw new ArgumentException("Claim token is required.", nameof(claimToken));
        if (string.IsNullOrWhiteSpace(claimedBy) || claimedBy.Length > MaxClaimedByLength)
            throw new ArgumentException($"ClaimedBy must be non-empty and at most {MaxClaimedByLength} characters.", nameof(claimedBy));
        if (leaseUntilUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("LeaseUntilUtc must be UTC.", nameof(leaseUntilUtc));
        if (attemptCount < 1) throw new ArgumentOutOfRangeException(nameof(attemptCount));
        ClaimToken = claimToken;
        ClaimedBy = claimedBy;
        LeaseUntilUtc = leaseUntilUtc;
        AttemptCount = attemptCount;
    }

    public NodeFinalizationIntent Intent { get; }
    public Guid ClaimToken { get; }
    public string ClaimedBy { get; }
    public DateTime LeaseUntilUtc { get; }
    public int AttemptCount { get; }
}

/// <summary>Bounded operational state stored beside an immutable intent by durable providers.</summary>
public sealed record NodeFinalizationOperationalState
{
    public const int MaxErrorCodeLength = 128;

    public NodeFinalizationOperationalState(DateTime availableAtUtc, Guid? claimToken, string? claimedBy,
        int attemptCount, DateTime? lastAttemptAtUtc, string? lastErrorCode)
    {
        if (availableAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("AvailableAtUtc must be UTC.", nameof(availableAtUtc));
        if (claimToken == Guid.Empty) throw new ArgumentException("Claim token cannot be empty.", nameof(claimToken));
        if (claimedBy is { Length: > NodeFinalizationClaim.MaxClaimedByLength }) throw new ArgumentException("ClaimedBy is too long.", nameof(claimedBy));
        if (claimToken.HasValue != !string.IsNullOrWhiteSpace(claimedBy)) throw new ArgumentException("Claim token and claimed-by owner must be present together.");
        if (attemptCount < 0) throw new ArgumentOutOfRangeException(nameof(attemptCount));
        if (lastAttemptAtUtc is { Kind: not DateTimeKind.Utc }) throw new ArgumentException("LastAttemptAtUtc must be UTC.", nameof(lastAttemptAtUtc));
        if (lastErrorCode is { Length: > MaxErrorCodeLength }) throw new ArgumentException("Last error code is too long.", nameof(lastErrorCode));
        AvailableAtUtc = availableAtUtc;
        ClaimToken = claimToken;
        ClaimedBy = claimedBy;
        AttemptCount = attemptCount;
        LastAttemptAtUtc = lastAttemptAtUtc;
        LastErrorCode = lastErrorCode;
    }

    public DateTime AvailableAtUtc { get; }
    public Guid? ClaimToken { get; }
    public string? ClaimedBy { get; }
    public int AttemptCount { get; }
    public DateTime? LastAttemptAtUtc { get; }
    public string? LastErrorCode { get; }
}
