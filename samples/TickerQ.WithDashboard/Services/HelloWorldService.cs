using TickerQ.WithDashboard.Interfaces;

namespace TickerQ.WithDashboard.Services;

public class HelloWorldService : IHelloWorldService
{
    public void SayHello(string source)
    {
        Console.WriteLine($"Hello from {source}.");
    }
}