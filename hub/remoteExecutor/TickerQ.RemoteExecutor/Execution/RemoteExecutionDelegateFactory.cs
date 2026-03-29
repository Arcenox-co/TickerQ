using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Grpc.Contracts;
using TickerQ.RemoteExecutor.Execution;
using TickerQ.Utilities;

namespace TickerQ.RemoteExecutor;

internal static class RemoteExecutionDelegateFactory
{
    public static TickerFunctionDelegate CreateForGrpcStream(
        string nodeName,
        Func<IServiceProvider, GrpcNodeConnectionManager> connectionManagerProvider)
    {
        if (string.IsNullOrWhiteSpace(nodeName))
            throw new ArgumentException("Node name is required.", nameof(nodeName));

        return (ct, serviceProvider, context) =>
        {
            var connectionManager = connectionManagerProvider(serviceProvider);

            var task = new DispatchTask
            {
                Id = context.Id.ToString(),
                FunctionName = context.FunctionName,
                Type = (Grpc.Contracts.TickerType)(int)context.Type,
                RetryCount = context.RetryCount,
                IsDue = context.IsDue,
                ScheduledFor = Timestamp.FromDateTime(DateTime.SpecifyKind(context.ScheduledFor, DateTimeKind.Utc))
            };

            connectionManager.DispatchToAny(task);
            return Task.CompletedTask;
        };
    }
}
