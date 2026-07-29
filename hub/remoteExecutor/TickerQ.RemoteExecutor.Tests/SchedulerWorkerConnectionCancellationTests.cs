using Grpc.Core;
using TickerQ.RemoteExecutor.WorkerStream;
using TickerQ.Worker.V1;
using Xunit;

namespace TickerQ.RemoteExecutor.Tests;

public sealed class SchedulerWorkerConnectionCancellationTests
{
    [Fact]
    public async Task CallerCancellation_SendsCorrelatedCancelOnSameConnection()
    {
        var writer = new RecordingWriter();
        await using var connection = new SchedulerWorkerConnection(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "node", "worker", 1, writer);
        var execute = new ExecuteFunction
        {
            RequestId = "execution-generation",
            TickerId = Guid.NewGuid().ToString(),
            FunctionName = "job"
        };
        using var cts = new CancellationTokenSource();

        var pending = connection.ExecuteFunctionAsync(execute, TimeSpan.FromMinutes(1), cts.Token);
        await writer.WaitForCountAsync(1);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await writer.WaitForCountAsync(2);

        Assert.Same(execute, writer.Commands[0].ExecuteFunction);
        var cancel = writer.Commands[1].CancelExecution;
        Assert.Equal(execute.TickerId, cancel.TickerId);
        Assert.True(cancel.HasExecutionRequestId);
        Assert.Equal(execute.RequestId, cancel.ExecutionRequestId);
    }

    private sealed class RecordingWriter : IServerStreamWriter<SchedulerCommand>
    {
        private readonly SemaphoreSlim _written = new(0);
        public List<SchedulerCommand> Commands { get; } = [];
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(SchedulerCommand message)
            => WriteAsync(message, CancellationToken.None);

        public Task WriteAsync(SchedulerCommand message, CancellationToken cancellationToken)
        {
            lock (Commands) Commands.Add(message);
            _written.Release();
            return Task.CompletedTask;
        }

        public async Task WaitForCountAsync(int count)
        {
            while (true)
            {
                lock (Commands)
                    if (Commands.Count >= count) return;
                await _written.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }
}