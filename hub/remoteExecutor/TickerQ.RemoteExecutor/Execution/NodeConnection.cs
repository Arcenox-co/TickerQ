using System.Threading.Channels;
using TickerQ.Grpc.Contracts;

namespace TickerQ.RemoteExecutor.Execution;

internal sealed class NodeConnection
{
    public string NodeName { get; }
    public ChannelWriter<ExecutionCommand> WriteChannel { get; }
    public Task WriterTask { get; }
    public NodeState State { get; }
    public NodeCircuitBreaker CircuitBreaker { get; }

    public NodeConnection(
        string nodeName,
        ChannelWriter<ExecutionCommand> writeChannel,
        Task writerTask,
        NodeState state,
        NodeCircuitBreaker circuitBreaker)
    {
        NodeName = nodeName;
        WriteChannel = writeChannel;
        WriterTask = writerTask;
        State = state;
        CircuitBreaker = circuitBreaker;
    }
}
