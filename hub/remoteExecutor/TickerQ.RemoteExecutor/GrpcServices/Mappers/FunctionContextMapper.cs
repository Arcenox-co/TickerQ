using Google.Protobuf.WellKnownTypes;
using TickerQ.Grpc.Contracts;
using TickerQ.Utilities.Models;

namespace TickerQ.RemoteExecutor.GrpcServices.Mappers;

internal static class FunctionContextMapper
{
    public static InternalFunctionContext ToInternal(FunctionContext proto)
    {
        var ctx = new InternalFunctionContext
        {
            FunctionName = proto.FunctionName,
            TickerId = Guid.Parse(proto.TickerId),
            ParentId = proto.HasParentId ? Guid.Parse(proto.ParentId) : null,
            Type = (Utilities.Enums.TickerType)(int)proto.Type,
            Retries = proto.Retries,
            RetryCount = proto.RetryCount,
            Status = (Utilities.Enums.TickerStatus)(int)proto.Status,
            ElapsedTime = proto.ElapsedTime,
            ExceptionDetails = proto.ExceptionDetails,
            ExecutedAt = proto.ExecutedAt?.ToDateTime() ?? default,
            RetryIntervals = proto.RetryIntervals.ToArray(),
            ReleaseLock = proto.ReleaseLock,
            ExecutionTime = proto.ExecutionTime?.ToDateTime() ?? default,
            RunCondition = (Utilities.Enums.RunCondition)(int)proto.RunCondition,
            CachedPriority = (Utilities.Enums.TickerTaskPriority)(int)proto.CachedPriority,
            CachedMaxConcurrency = proto.CachedMaxConcurrency
        };

        // Restore the ParametersToUpdate tracking
        if (proto.ParametersToUpdate.Count > 0)
        {
            ctx.ParametersToUpdate = new HashSet<string>(proto.ParametersToUpdate);
        }

        return ctx;
    }

    public static FunctionContext ToProto(InternalFunctionContext ctx)
    {
        var proto = new FunctionContext
        {
            FunctionName = ctx.FunctionName ?? string.Empty,
            TickerId = ctx.TickerId.ToString(),
            Type = (Grpc.Contracts.TickerType)(int)ctx.Type,
            Retries = ctx.Retries,
            RetryCount = ctx.RetryCount,
            Status = (Grpc.Contracts.TickerStatus)(int)ctx.Status,
            ElapsedTime = ctx.ElapsedTime,
            ExceptionDetails = ctx.ExceptionDetails ?? string.Empty,
            ReleaseLock = ctx.ReleaseLock,
            RunCondition = (Grpc.Contracts.RunCondition)(int)ctx.RunCondition,
            CachedPriority = (Grpc.Contracts.TickerTaskPriority)(int)ctx.CachedPriority,
            CachedMaxConcurrency = ctx.CachedMaxConcurrency
        };

        if (ctx.ParentId.HasValue)
            proto.ParentId = ctx.ParentId.Value.ToString();

        if (ctx.ExecutedAt != default)
            proto.ExecutedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(ctx.ExecutedAt, DateTimeKind.Utc));

        if (ctx.ExecutionTime != default)
            proto.ExecutionTime = Timestamp.FromDateTime(DateTime.SpecifyKind(ctx.ExecutionTime, DateTimeKind.Utc));

        if (ctx.RetryIntervals is { Length: > 0 })
            proto.RetryIntervals.AddRange(ctx.RetryIntervals);

        if (ctx.ParametersToUpdate is { Count: > 0 })
            proto.ParametersToUpdate.AddRange(ctx.ParametersToUpdate);

        return proto;
    }
}
