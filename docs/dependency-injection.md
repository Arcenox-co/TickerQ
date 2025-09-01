# Dependency Injection

TickerQ supports .NET Dependency Injection for job services and background tasks.

## Example
```csharp
builder.Services.AddScoped<IHelloWorldService, HelloWorldService>();
```

## Using DI in Jobs
```csharp
class BackgroundService
{
    private readonly IHelloWorldService _helloWorldService;
    public BackgroundService(IHelloWorldService helloWorldService)
    {
        _helloWorldService = helloWorldService;
    }
    [TickerFunction(functionName: nameof(HelloWorld), cronExpression: "* * * * *")]
    public void HelloWorld()
    {
        _helloWorldService.SayHello();
    }
}
```

See [Dependency Injection](https://tickerq.arcenox.com/intro/dependency-injection.html).
