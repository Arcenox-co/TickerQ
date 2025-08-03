using TickerQ.Utilities.Base;
using TickerQ.WithDashboard.Interfaces;

namespace TickerQ.WithDashboard.Jobs
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
