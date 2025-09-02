using TickerQ.WithPostgres.Interfaces;

namespace TickerQ.WithPostgres.Services;

public class HelloWorldService : IHelloWorldService
{
    public void SayHello(string source)
    {
        Console.WriteLine($"Hello from {source}.");
    }
}