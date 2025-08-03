# TickerQ Service Discovery and Registration

TickerQ now includes a powerful service discovery and registration system that allows you to automatically discover and register functions/services across multiple assemblies.

## Overview

The service discovery system provides:

- **Automatic Function Discovery**: Scan assemblies for methods marked with `[TickerFunction]` attribute
- **Fluent Configuration**: Use method chaining to register assemblies
- **Runtime Assembly Scanning**: Discover functions at runtime, not just compile-time
- **Flexible Integration**: Works alongside existing TickerQ functionality

## Key Features

### RegisterServicesFromAssemblies Method

The new `RegisterServicesFromAssemblies` method allows you to specify which assemblies should be scanned for TickerFunction methods:

```csharp
public TickerOptionsBuilder RegisterServicesFromAssemblies(params Assembly[] assemblies)
```

**Features:**
- Takes a variable number of Assembly parameters
- Adds assemblies to an internal `AssembliesToRegister` collection
- Returns `this` to enable method chaining (fluent interface)
- Handles null and empty assembly arrays gracefully

## Usage Examples

### Basic Usage

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddTickerQ(options =>
{
    // Register current assembly for service discovery
    options.RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly());
});

var host = builder.Build();
host.UseTickerQ(); // Automatically discovers and registers functions
```

### Multiple Assemblies

```csharp
builder.Services.AddTickerQ(options =>
{
    options.RegisterServicesFromAssemblies(
        Assembly.GetExecutingAssembly(),           // Current assembly
        typeof(ExternalService).Assembly,          // External assembly
        Assembly.LoadFrom("MyPlugin.dll")          // Plugin assembly
    );
});
```

### Method Chaining

```csharp
builder.Services.AddTickerQ(options =>
{
    options
        .RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly())
        .RegisterServicesFromAssemblies(typeof(ExternalService).Assembly)
        .SetMaxConcurrency(4)
        .SetInstanceIdentifier("MyApp");
});
```

## Function Discovery

The system automatically discovers methods marked with the `[TickerFunction]` attribute:

```csharp
public class MyScheduledTasks
{
    [TickerFunction("DailyReport", "0 0 8 * * *", TickerTaskPriority.Normal)]
    public async Task GenerateDailyReport(CancellationToken cancellationToken)
    {
        // Your scheduled task logic
    }

    [TickerFunction("HealthCheck", "0 */5 * * * *")]
    public static async Task PerformHealthCheck()
    {
        // Static method support
    }

    [TickerFunction("ProcessData", TickerTaskPriority.High)]
    public async Task ProcessData(TickerFunctionContext<MyRequest> context)
    {
        // Method with typed request context
    }
}
```

## Supported Method Signatures

The service discovery system supports various method signatures:

```csharp
// Basic signatures
public void MyMethod()
public async Task MyMethodAsync()

// With cancellation token
public async Task MyMethodAsync(CancellationToken cancellationToken)

// With context
public async Task MyMethodAsync(TickerFunctionContext context)

// With typed context
public async Task MyMethodAsync(TickerFunctionContext<MyRequest> context)

// With dependency injection
public async Task MyMethodAsync(IMyService service, CancellationToken cancellationToken)

// Combined parameters
public async Task MyMethodAsync(
    CancellationToken cancellationToken, 
    IServiceProvider serviceProvider, 
    TickerFunctionContext context)
```

## Architecture

### TickerOptionsBuilder

The `TickerOptionsBuilder` class now includes:

```csharp
public class TickerOptionsBuilder
{
    // Collection of assemblies to register for service discovery
    internal List<Assembly> AssembliesToRegister { get; private set; }
    
    // Method to register assemblies
    public TickerOptionsBuilder RegisterServicesFromAssemblies(params Assembly[] assemblies)
    {
        if (assemblies != null && assemblies.Length > 0)
        {
            AssembliesToRegister.AddRange(assemblies);
        }
        return this;
    }
}
```

### TickerFunctionProvider

Enhanced with runtime assembly scanning:

```csharp
public static class TickerFunctionProvider
{
    // New method for runtime assembly scanning
    public static void RegisterFunctionsFromAssemblies(Assembly[] assemblies)
    
    // Existing methods remain unchanged
    public static void RegisterFunctions(IDictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate)> functions)
    public static void RegisterRequestType(IDictionary<string, (string, Type)> requestTypes)
}
```

## Integration with Existing System

The service discovery system integrates seamlessly with existing TickerQ functionality:

1. **Source Generators**: Continue to work for compile-time discovery
2. **Manual Registration**: Still supported via `RegisterFunctions`
3. **Configuration**: Cron expressions from configuration still work
4. **External Providers**: EntityFramework and other providers remain compatible

## Best Practices

### 1. Assembly Organization

```csharp
// Register assemblies containing your scheduled tasks
options.RegisterServicesFromAssemblies(
    Assembly.GetExecutingAssembly(),     // Main application
    typeof(CoreTasks).Assembly,          // Core business logic
    typeof(IntegrationTasks).Assembly    // External integrations
);
```

### 2. Dependency Injection

```csharp
public class EmailService
{
    private readonly IEmailSender _emailSender;
    
    public EmailService(IEmailSender emailSender)
    {
        _emailSender = emailSender;
    }
    
    [TickerFunction("SendDailyDigest", "0 0 9 * * *")]
    public async Task SendDailyDigest(ILogger<EmailService> logger)
    {
        // Dependencies are automatically injected
    }
}
```

### 3. Error Handling

The system includes built-in error handling:
- Invalid assemblies are skipped
- Reflection errors don't break the application
- Invalid method signatures are ignored

## Migration Guide

### From Manual Registration

**Before:**
```csharp
var functions = new Dictionary<string, (string, TickerTaskPriority, TickerFunctionDelegate)>
{
    { "MyTask", ("0 0 * * * *", TickerTaskPriority.Normal, myDelegate) }
};
TickerFunctionProvider.RegisterFunctions(functions);
```

**After:**
```csharp
// Just add the attribute and register the assembly
[TickerFunction("MyTask", "0 0 * * * *")]
public async Task MyTask() { }

// In startup
options.RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly());
```

### From Source Generator Only

**Before:**
```csharp
// Relied only on compile-time discovery
builder.Services.AddTickerQ();
```

**After:**
```csharp
// Add runtime discovery for additional assemblies
builder.Services.AddTickerQ(options =>
{
    options.RegisterServicesFromAssemblies(
        Assembly.LoadFrom("MyPlugin.dll")  // Runtime-loaded assemblies
    );
});
```

## Performance Considerations

- Assembly scanning occurs once during application startup
- Reflection is used only during registration, not execution
- Discovered functions are cached for optimal runtime performance
- No impact on existing source generator performance

## Troubleshooting

### Functions Not Discovered

1. Ensure the assembly is registered: `options.RegisterServicesFromAssemblies(assembly)`
2. Verify the method has the `[TickerFunction]` attribute
3. Check that the method signature is supported
4. Ensure the class is public and has a public constructor

### Dependency Injection Issues

1. Register services in the DI container before calling `UseTickerQ()`
2. Ensure service lifetimes are appropriate (Scoped/Transient for per-execution services)
3. Use constructor injection in your service classes

### Performance Issues

1. Limit the number of assemblies registered for scanning
2. Avoid registering system assemblies
3. Consider using source generators for compile-time discovery when possible
