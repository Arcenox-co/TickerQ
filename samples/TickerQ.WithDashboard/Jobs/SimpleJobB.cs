using TickerQ.Utilities.Base;
using TickerQ.WithDashboard.Interfaces;

namespace TickerQ.WithDashboard.Jobs
{
    public class SimpleJobB(IHelloWorldService helloWorldService)
    {
        private readonly IHelloWorldService _helloWorldService = helloWorldService ?? throw new ArgumentNullException(nameof(helloWorldService));

        [TickerFunction(functionName: nameof(HelloWorldJobB), cronExpression: "* * * * *")]
        public void HelloWorldJobB()
        {
            _helloWorldService.SayHello(nameof(SimpleJobB));
        }
    }
}
