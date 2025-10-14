# TickerQ Dashboard Authentication

Simple, clean authentication for your TickerQ Dashboard.

## 🚀 Quick Examples

### No Authentication (Public Dashboard)
```csharp
services.AddTickerQ<MyTimeTicker, MyCronTicker>(config =>
{
    config.AddDashboard(dashboard =>
    {
        // No authentication setup = public dashboard
    });
});
```

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

### Bearer Token Authentication
```csharp
services.AddTickerQ<MyTimeTicker, MyCronTicker>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.WithBearerToken("my-secret-api-key-12345");
    });
});
```

### Use Host Application's Authentication
```csharp
services.AddTickerQ<MyTimeTicker, MyCronTicker>(config =>
{
    config.AddDashboard(dashboard =>
    {
        dashboard.WithHostAuthentication(
            requiredRoles: new[] { "Admin", "TickerQUser" }
        );
    });
});
```

## 🔧 Fluent API Methods

- `WithBasicAuth(username, password)` - Enable username/password authentication
- `WithBearerToken(apiKey)` - Enable API key authentication  
- `WithHostAuthentication(roles?, policies?)` - Use your app's existing auth
- `SetBasePath(path)` - Set dashboard URL path
- `SetBackendDomain(domain)` - Set backend API domain
- `SetCorsPolicy(policy)` - Configure CORS

## 🔒 How It Works

The dashboard automatically detects your authentication method:

1. **No auth configured** → Public dashboard
2. **Basic auth configured** → Username/password login
3. **Bearer token configured** → API key authentication
4. **Host auth configured** → Delegates to your app's auth system

## 🌐 Frontend Integration

The frontend automatically adapts based on your backend configuration:
- Shows appropriate login UI
- Handles SignalR authentication 
- Supports both header and query parameter auth (for WebSockets)

That's it! Simple and clean. 🎉