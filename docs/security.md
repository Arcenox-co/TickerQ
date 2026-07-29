# Dashboard Security & Deployment Hardening

The TickerQ Dashboard exposes operational control over your job scheduler (inspect, retry,
run-now, cancel). Treat it as a privileged surface. This page documents the deployment
boundaries the dashboard enforces and the explicit opt-ins it requires.

All options below are configured on `DashboardOptionsBuilder`:

```csharp
builder.Services.AddTickerQ(options =>
{
    options.AddDashboard(dashboard =>
    {
        dashboard.WithBasicAuth("admin", "secret");
        // ... hardening options below
    });
});
```

The dashboard **validates its configuration at startup** and throws
`InvalidOperationException` for unsafe combinations rather than silently exposing itself.

## Authentication is required (or explicitly waived)

If no authentication scheme is configured, startup **fails** unless you explicitly
acknowledge public exposure:

> `TickerQ Dashboard authentication is not configured. Call AllowAnonymousDashboard() to explicitly acknowledge public exposure, or configure an authentication scheme.`

- Configure auth with `WithBasicAuth(...)`, `WithApiKey(...)`, or
  `WithHostAuthentication(...)`.
- Or call **`AllowAnonymousDashboard()`** to opt in to an unauthenticated dashboard. This is
  a deliberate, visible acknowledgement — anonymous access is never the silent default.

```csharp
// Explicitly acknowledge an unauthenticated dashboard (e.g. behind a trusted gateway).
dashboard.AllowAnonymousDashboard();
```

> **Risk:** an anonymous dashboard gives anyone who can reach it full control over your
> jobs. Only opt in when the endpoint is protected by another layer (private network,
> authenticating reverse proxy, mTLS, etc.).

## Cross-origin requests (CORS)

The dashboard denies all cross-origin requests by default. Credentialed cross-origin access
requires an **explicit allow-list of exact browser origins** — trust is never inferred from
the backend domain.

```csharp
dashboard.SetBackendDomain("https://api.example.com");
dashboard.AllowOrigins(
    "https://ops.example.com",
    "https://admin.example.com");
```

- `AllowOrigins(params string[] origins)` — each entry must be an absolute `http`/`https`
  origin with path `/` and no query/fragment; invalid values throw
  `ArgumentException`. Matched origins receive an exact `Access-Control-Allow-Origin` plus
  credentials. Unlisted origins receive neither.
- `SetBackendDomain(string)` sets the API domain **only** — it does **not** grant
  cross-origin trust. Setting a backend domain for split-origin hosting **without** an
  allow-list fails validation:

  > `SetBackendDomain does not grant cross-origin trust. Call AllowOrigins() with exact browser origins.`

- `SetCorsPolicy(Action<CorsPolicyBuilder>)` — supply a fully custom policy if you need
  behavior beyond the allow-list. Avoid credentialed reflect-any-origin policies.

## Forwarded headers behind a proxy

When the dashboard runs behind a reverse proxy or load balancer, forwarded headers
(`X-Forwarded-For` / `-Proto` / `-Host`) are honored **only from trusted sources**. Enabling
them without a trust boundary fails validation, so direct clients cannot spoof their IP,
host, or scheme:

```csharp
dashboard.EnableForwardedHeaders();
dashboard.TrustForwardedProxy(IPAddress.Parse("10.0.0.4"));
dashboard.TrustForwardedNetwork(IPNetwork.Parse("10.0.0.0/8"));
```

- `EnableForwardedHeaders(bool enabled = true)` — opt-in; off by default.
- `TrustForwardedProxy(IPAddress)` / `TrustForwardedNetwork(IPNetwork)` — register the exact
  proxies/networks whose forwarded headers are accepted. Calling either also enables
  forwarded headers.
- Forwarded headers enabled with **no** trusted proxy or network throws:

  > `EnableForwardedHeaders requires at least one TrustForwardedProxy() or TrustForwardedNetwork().`

## JWT signing keys (multi-instance)

Basic-auth and API-key logins issue a signed access token. For a **single instance** you can
run with an ephemeral key; for **multiple instances** (load-balanced replicas) all instances
must share a deterministic signing key so a token issued by one validates on another.

```csharp
// Multi-instance: explicit shared key (>= 32 bytes), from config/secret store.
dashboard.WithAuthentication(auth =>
{
    auth.AddJwtBearer(jwt =>
    {
        jwt.SetSigningKey(builder.Configuration["TickerQ:SigningKey"]!);
        jwt.AccessTokenLifetime = TimeSpan.FromMinutes(60);
    });
});
```

> `WithAuthentication(Action<AuthSchemeBuilder>)` is the composable entry point; inside it
> `AddJwtBearer(Action<JwtBearerOptions>)` and `AddCookieLogin(Action<CookieAuthOptions>)`
> configure token/cookie behavior. The single-scheme shortcuts (`WithBasicAuth`,
> `WithApiKey`, `WithHostAuthentication`) cover the common cases.

- Shared key: `JwtBearerOptions.SigningKey` (`byte[]`) or `SetSigningKey(string)` — must be
  **at least 32 bytes**. The cookie scheme has the equivalent `CookieAuthOptions.SigningKey`
  / `SetSigningKey`.
- Ephemeral (dev-only) key: `AllowEphemeralSigningKey = true`. Without an explicit key and
  without this flag, startup throws:

  > `... requires an explicit shared JWT signing key. Set JwtBearerOptions.SigningKey / CookieAuthOptions.SigningKey (at least 32 bytes), or set AllowEphemeralSigningKey = true only for development ...`

  When allowed, a random 32-byte key is generated per process and a warning is logged.
  Tokens will not validate across instances or across restarts.

> The signing key is **not** derived from randomized DataProtection output — it is either the
> explicit key you supply or an explicit ephemeral dev key. There is no hidden
> "stable-by-magic" derivation.

## Token renewal: sliding, not refresh tokens

Session renewal is **sliding access-token renewal**, honestly documented as such. There is
**no durable refresh-token store, no revocation, and no server-side bearer logout.**

| Endpoint | Behavior |
|----------|----------|
| `POST {basePath}/api/auth/login` | Issues an access token (default lifetime **60 minutes**; cookie session default **8 hours**). |
| `POST {basePath}/api/auth/refresh` | Re-issues a fresh access token from a **still-valid** token (bearer header or auth cookie). Expired tokens require a fresh login. Response carries `X-TickerQ-Session-Semantics: sliding-access-token`. |
| `POST {basePath}/api/auth/logout` | Clears the auth cookie. Bearer tokens are stateless — the SPA drops them client-side; there is nothing to revoke server-side. |

The only revocation mechanism is token lifetime. Keep `AccessTokenLifetime` short if you need
tight session bounds. The auth cookie is set `HttpOnly`, `Secure` (when `SecureCookie` is
enabled), `SameSite=Strict`.

## Login throttling & credential comparison

- **Throttling:** failed logins are rate-limited in a fixed window — **5 failures per 5
  minutes** — with **separate buckets per client IP and per username** (the username bucket
  is keyed by a SHA-256 hash, not stored in clear). Storage is bounded (capacity 10,000) and
  pruned deterministically. Exceeding the limit returns **HTTP 429** with a `Retry-After`
  header.
- **Availability trade-off:** a remote attacker who knows a username can intentionally fill
  that username's bucket and temporarily delay legitimate logins for the same account. The
  separate IP bucket limits broad spray attacks, but deployments with stronger availability
  requirements should also enforce rate limits and bot controls at a trusted edge.
- **Constant-time comparison:** Basic-auth credentials and JWT signatures are compared with
  `CryptographicOperations.FixedTimeEquals` (after a length check) to avoid timing side
  channels.

## Compatibility notes

- Dashboard CORS, forwarded-headers, anonymous-access, and signing-key configuration are
  public contracts. Existing deployments that relied on implicit permissive defaults must
  now opt in explicitly (`AllowOrigins`, `TrustForwardedProxy`/`TrustForwardedNetwork`,
  `AllowAnonymousDashboard`, `SetSigningKey`/`AllowEphemeralSigningKey`) or startup will
  throw with the messages above.
- These are fail-closed by design: a missing acknowledgement is a startup error, not a
  silent insecure default.
