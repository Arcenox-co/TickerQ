using TickerQ.Utilities.Base;
using TickerQ.WithPostgres.Interfaces;

namespace TickerQ.WithPostgres.Jobs
{
    public class SimpleJobA(IHelloWorldService helloWorldService)
    {
        private readonly IHelloWorldService _helloWorldService = helloWorldService ?? throw new ArgumentNullException(nameof(helloWorldService));

        // Prints Hello World every 1 minute.
        [TickerFunction(functionName: nameof(HelloWorldJobA), cronExpression: "* * * * *")]
        public void HelloWorldJobA()
        {
            _helloWorldService.SayHello(nameof(SimpleJobA));
        }
    }
}
