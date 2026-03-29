using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Grpc.Contracts;
using TickerQ.RemoteExecutor.Execution;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces.Managers;

namespace TickerQ.RemoteExecutor.GrpcServices;

internal sealed class FunctionRegistrationGrpcService : FunctionRegistrationService.FunctionRegistrationServiceBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly GrpcNodeConnectionManager _connectionManager;

    public FunctionRegistrationGrpcService(
        IServiceProvider serviceProvider,
        GrpcNodeConnectionManager connectionManager)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
    }

    public override async Task<Empty> RegisterFunctions(RegisterFunctionsRequest request, ServerCallContext context)
    {
        if (request.Functions.Count == 0)
            return new Empty();

        var nodeName = request.NodeName;
        var functionDict = TickerFunctionProvider.TickerFunctions.ToDictionary();
        var requestInfoDict = TickerFunctionProvider.TickerFunctionRequestInfos?.ToDictionary()
            ?? new Dictionary<string, (string RequestType, string RequestExampleJson)>();

        var cronPairs = new List<(string Name, string CronExpression)>();

        foreach (var func in request.Functions)
        {
            if (!func.IsActive)
            {
                if (RemoteFunctionRegistry.IsRemote(func.Name) && functionDict.Remove(func.Name))
                {
                    RemoteFunctionRegistry.Remove(func.Name);
                    requestInfoDict.Remove(func.Name);
                }
                continue;
            }

            var functionDelegate = RemoteExecutionDelegateFactory.CreateForGrpcStream(
                nodeName,
                sp => sp.GetRequiredService<GrpcNodeConnectionManager>());

            var priority = (Utilities.Enums.TickerTaskPriority)(int)func.Priority;

            if (functionDict.TryAdd(func.Name, (func.CronExpression, priority, functionDelegate, 0)))
            {
                RemoteFunctionRegistry.MarkRemote(func.Name);
                requestInfoDict[func.Name] = (func.RequestType, func.RequestExampleJson);
            }

            if (!string.IsNullOrWhiteSpace(func.CronExpression))
            {
                cronPairs.Add((func.Name, func.CronExpression));
            }
        }

        TickerFunctionProvider.RegisterFunctions(functionDict);

        var existingRequestTypes = TickerFunctionProvider.TickerFunctionRequestTypes;
        
        if (existingRequestTypes is { Count: > 0 })
            TickerFunctionProvider.RegisterRequestType(existingRequestTypes.ToDictionary());

        TickerFunctionProvider.RegisterRequestInfo(requestInfoDict);
        TickerFunctionProvider.Build();

        if (cronPairs.Count > 0)
        {
            var manager = _serviceProvider.GetService<IInternalTickerManager>();
            if (manager != null)
            {
                await manager.MigrateDefinedCronTickers(cronPairs.ToArray(), context.CancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return new Empty();
    }

    public override Task<RemoveFunctionResponse> RemoveFunction(RemoveFunctionByNameRequest request, ServerCallContext context)
    {
        var removed = RemoveFunctionByName(request.FunctionName);
        return Task.FromResult(new RemoveFunctionResponse { Removed = removed });
    }

    private static bool RemoveFunctionByName(string functionName)
    {
        if (!RemoteFunctionRegistry.IsRemote(functionName))
            return false;

        var functionDict = TickerFunctionProvider.TickerFunctions.ToDictionary();
        if (!functionDict.Remove(functionName))
            return false;

        RemoteFunctionRegistry.Remove(functionName);
        var requestInfoDict = TickerFunctionProvider.TickerFunctionRequestInfos?.ToDictionary()
            ?? new Dictionary<string, (string RequestType, string RequestExampleJson)>();
        requestInfoDict.Remove(functionName);

        TickerFunctionProvider.RegisterFunctions(functionDict);

        var existingRequestTypes = TickerFunctionProvider.TickerFunctionRequestTypes;
        if (existingRequestTypes != null && existingRequestTypes.Count > 0)
        {
            var requestTypesDict = existingRequestTypes.ToDictionary();
            requestTypesDict.Remove(functionName);
            TickerFunctionProvider.RegisterRequestType(requestTypesDict);
        }

        TickerFunctionProvider.RegisterRequestInfo(requestInfoDict);
        TickerFunctionProvider.Build();
        return true;
    }
}
