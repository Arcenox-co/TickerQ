using TickerQ.Utilities.Base;
using TickerQ.Utilities.Models;

namespace TickerQ.WithPostgres.Jobs
{
    public class MyFirstExample
    {
        [TickerFunction(functionName: "CleanupLogs2")]
        public async Task CleanupLogs2(TickerFunctionContext<string> tickerContext, CancellationToken cancellationToken)
        {
            Console.WriteLine("🧹 CLEANUP_TOKEN: CleanupLogs2 executing");
            Console.WriteLine(tickerContext.Request); // Output cleanup_example_file.txt
        }
    }
}
