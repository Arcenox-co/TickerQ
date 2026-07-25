# TickerQ Dashboard Authentication

Simple, clean authentication for your TickerQ Dashboard.

## 🚀 Quick Examples

### No Authentication (Public Dashboard)

Anonymous access is an **explicit opt-in**. If no auth scheme is configured and you don't
acknowledge public exposure, startup throws. Call `AllowAnonymousDashboard()` to opt in:

```csharp
services.AddTickerQ<MyTimeTicker, MyCronTicker>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.AllowAnonymousDashboard(); // deliberately public — anyone reachable can control jobs
    });
});
```

> ⚠️ A public dashboard grants full control over your jobs to anyone who can reach it. Only
> opt in when another layer (private network, authenticating gateway, mTLS) protects it.

### Basic Authentication
```csharp
services.AddTickerQ<MyTimeTicker, MyCronTicker>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.WithBasicAuth("admin", "secret123");
    });
});
```

### API Key Authentication
```csharp
services.AddTickerQ<MyTimeTicker, MyCronTicker>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.WithApiKey("my-secret-api-key-12345");
    });
});
```

### Use Host Application's Authentication
```csharp
services.AddTickerQ<MyTimeTicker, MyCronTicker>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.WithHostAuthentication();
    });
});
```

### Use Host Authentication with Custom Policy
```csharp
services.AddTickerQ<MyTimeTicker, MyCronTicker>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.WithHostAuthentication("AdminPolicy");
    });
});
```

### Dedicated OpenAPI Group
```csharp
services.AddTickerQ<MyTimeTicker, MyCronTicker>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.SetGroupName("tickerq");
    });
});
```

## 🔧 Fluent API Methods

**Authentication**

- `WithBasicAuth(username, password)` - Enable username/password authentication
- `WithApiKey(apiKey)` - Enable API key authentication
- `WithHostAuthentication(policy)` - Use your app's existing auth with optional policy (e.g., "AdminPolicy")
- `WithAuthentication(auth => …)` - Composable multi-scheme builder; use `AddJwtBearer(...)` / `AddCookieLogin(...)` to set the signing key, token lifetime, and cookie behavior
- `AllowAnonymousDashboard()` - Explicitly acknowledge an unauthenticated (public) dashboard

**Deployment hardening**

- `AllowOrigins(params string[] origins)` - Exact browser origins allowed for credentialed cross-origin access (required for split-origin hosting)
- `EnableForwardedHeaders()` + `TrustForwardedProxy(ip)` / `TrustForwardedNetwork(cidr)` - Honor `X-Forwarded-*` only from trusted proxies/networks
- `SetBackendDomain(domain)` - Set backend API domain (does **not** by itself grant cross-origin trust — pair with `AllowOrigins`)
- `SetCorsPolicy(policy)` - Configure a fully custom CORS policy

**Other**

- `SetBasePath(path)` - Set dashboard URL path
- `SetGroupName(name)` - Set OpenAPI group name for dashboard endpoints

> **Deployment hardening details** — CORS allow-lists, trusted proxies, the anonymous opt-in,
> JWT signing keys, and sliding token-renewal semantics are documented in
> **[docs/security.md](../../docs/security.md)**. Unsafe combinations fail validation at
> startup rather than exposing the dashboard silently.

## 🔒 How It Works

The dashboard automatically detects your authentication method:

1. **No auth configured** → startup **fails** unless you call `AllowAnonymousDashboard()` to explicitly acknowledge public exposure
2. **Basic auth configured** → Username/password login
3. **Bearer token configured** → API key authentication
4. **Host auth configured** → Delegates to your app's auth system

## 🌐 Frontend Integration

The frontend automatically adapts based on your backend configuration:
- Shows appropriate login UI
- Handles SignalR authentication 
- Supports both header and query parameter auth (for WebSockets)

That's it! Simple and clean. 🎉
