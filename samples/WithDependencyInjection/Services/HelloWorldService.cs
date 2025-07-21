using TickerQ.WithDependecyInjection.Interfaces;

namespace TickerQ.WithDependencyInjection.Services;

public class HelloWorldService : IHelloWorldService
{
    public void SayHello()
    {
        Console.WriteLine("Hello World!");
    }
}