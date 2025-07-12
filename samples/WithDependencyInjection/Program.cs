using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TickerQ.DependencyInjection;
using TickerQ.DependencyInjection.Hosting;
using TickerQ.Utilities.Base;
using TickerQ.WithDependecyInjection.Interfaces;
using TickerQ.WithDependencyInjection.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddTickerQ();
builder.Services.AddScoped<IHelloWorldService, HelloWorldService>();
var host = builder.Build();

host.UseTickerQ(); 

await host.RunAsync();

class BackgroundService
{
    private readonly IHelloWorldService _helloWorldService;
    public BackgroundService(IHelloWorldService helloWorldService)
    {
        _helloWorldService = helloWorldService ?? throw new ArgumentNullException(nameof(helloWorldService));
    }
    
    // Prints Hello World every 1 minute.
    [TickerFunction(functionName: nameof(HelloWorld), cronExpression: "* * * * *")]
    public void HelloWorld()
    {
        _helloWorldService.SayHello();
    }
}