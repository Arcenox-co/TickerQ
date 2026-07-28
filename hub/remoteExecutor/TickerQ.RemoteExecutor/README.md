# TickerQ RemoteExecutor

## Node scheduler HTTP compatibility bridge

TickerQ's modern RemoteExecutor uses Hub/gRPC for function registration, webhooks, and worker transport. The Node SDK still uses the historical signed HTTP scheduler-persistence calls. Hosts that run Node workers must opt into the narrow compatibility surface explicitly:

```csharp
var app = builder.Build();

app.UseTickerQ();
app.MapTickerQNodeCompatibilityEndpoints("/tickerq/node");

app.Run();
```

Custom ticker entities use the generic overload:

```csharp
app.MapTickerQNodeCompatibilityEndpoints<MyTimeTicker, MyCronTicker>("/tickerq/node");
```

The Hub-provided scheduler/application URL consumed by the Node SDK must include the exact same prefix and a trailing slash, for example `https://scheduler.example.com/tickerq/node/`. The Node signer includes the resulting full path (and query string) in its HMAC, so reverse proxies must preserve that path byte-for-byte.

### Deliberate scope

The bridge maps only the routes used by the current Node SDK:

- time ticker create/update/delete, request retrieval, fenced context update, and unified-context update;
- cron ticker create/update/delete;
- cron occurrence request retrieval and fenced context update.

It does **not** restore obsolete HTTP function registration, Hub webhooks, queue/acquisition operations, or any route now owned by Hub/gRPC. This boundary is intentional: the broad legacy endpoint set was removed during the gRPC transport migration in commit `9364b33`, while the later Node SDK retained these scheduler calls.

### Security and lifecycle contract

- Mapping is opt-in. Requests fail closed with `503` until the Hub webhook signature is available.
- HMAC is `Base64(HMAC-SHA256(secret, UTF8("METHOD\nPATH?QUERY\nUNIX_SECONDS\n") + rawBody))` using `X-Timestamp` and `X-TickerQ-Signature`.
- Timestamp skew is limited to five minutes and signatures use fixed-time comparison. The raw body is buffered and authenticated before JSON parsing.
- Request bodies are limited to 2 MiB. Result envelopes use canonical Base64 and are limited to 1 MiB after decoding.
- No replay cache is applied: this protocol has no wire nonce, and legitimate same-second retries must remain possible.
- Executable Node callbacks must contain a non-empty `AcquisitionToken` UUID. It is propagated unchanged into every status write; missing or malformed tokens fail before user code runs.
- Only `Done`/`DueDone` can mutate `ResultEnvelope`. A successful execution always sends that mutation: an envelope publishes a result, while explicit JSON `null` clears stale durable output from an earlier attempt.
- Context updates go through `IInternalTickerManager.UpdateTickerAsync`, never directly to a provider. Invalid payloads return `400`, stale/non-acknowledged fenced writes `409`, unsupported result providers `501`, and unexpected failures `500`. Node treats non-2xx completion acknowledgement as an execution failure.

The upstream Hub/dispatcher must include the acquired row's `AcquisitionToken` in every inbound Node execution payload. A Hub version that omits it is intentionally incompatible with the secure default and must be upgraded before enabling this bridge.
