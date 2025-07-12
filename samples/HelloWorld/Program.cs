using Microsoft.Extensions.Hosting;
using TickerQ.DependencyInjection;
using TickerQ.DependencyInjection.Hosting;
using TickerQ.Utilities.Base;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddTickerQ(); 

var host = builder.Build();

host.UseTickerQ(); 

await host.RunAsync();

class BackgroundService
{
    // Prints Hello World every 1 minute.
    [TickerFunction(functionName: nameof(HelloWorld), cronExpression: "* * * * *")]
    public void HelloWorld()
    {
        Console.WriteLine("Hello World!");
    }
}

