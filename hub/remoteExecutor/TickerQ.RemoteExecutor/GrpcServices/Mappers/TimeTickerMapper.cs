using TickerQ.Grpc.Contracts;
using TickerQ.Utilities.Entities;
using TickerStatus = TickerQ.Utilities.Enums.TickerStatus;
using RunCondition = TickerQ.Utilities.Enums.RunCondition;

namespace TickerQ.RemoteExecutor.GrpcServices.Mappers;

internal static class TimeTickerMapper
{
    public static TimeTickerEntity ToEntity(TimeTickerMessage msg)
    {
        var entity = new TimeTickerEntity
        {
            Id = Guid.Parse(msg.Id),
            Function = msg.Function,
            Description = msg.Description,
            Retries = msg.Retries,
            RetryCount = msg.RetryCount,
            Request = msg.Request.IsEmpty ? null : msg.Request.ToByteArray(),
            ElapsedTime = msg.ElapsedTime,
            ExceptionMessage = msg.ExceptionMessage,
            SkippedReason = msg.SkippedReason
        };

        // Status (internal setter - use reflection or trust proto value)
        SetStatus(entity, (TickerStatus)(int)msg.Status);
        SetLockHolder(entity, msg.LockHolder);
        SetInitIdentifier(entity, msg.InitIdentifier);
        SetCreatedAt(entity, msg.CreatedAt?.ToDateTime() ?? DateTime.UtcNow);
        SetUpdatedAt(entity, msg.UpdatedAt?.ToDateTime() ?? DateTime.UtcNow);

        if (msg.ExecutionTime != null)
            entity.ExecutionTime = msg.ExecutionTime.ToDateTime();

        if (msg.LockedAt != null)
            SetLockedAt(entity, msg.LockedAt.ToDateTime());

        if (msg.ExecutedAt != null)
            SetExecutedAt(entity, msg.ExecutedAt.ToDateTime());

        if (msg.RetryIntervals.Count > 0)
            entity.RetryIntervals = msg.RetryIntervals.ToArray();

        if (msg.HasParentId)
            SetParentId(entity, Guid.Parse(msg.ParentId));

        if (msg.HasRunCondition)
            entity.RunCondition = (RunCondition)(int)msg.RunCondition;

        return entity;
    }

    // Internal setters require reflection for properties with `internal set`
    private static void SetStatus(TimeTickerEntity entity, TickerStatus status)
    {
        typeof(TimeTickerEntity<TimeTickerEntity>)
            .GetProperty(nameof(TimeTickerEntity.Status))!
            .SetValue(entity, status);
    }

    private static void SetLockHolder(TimeTickerEntity entity, string value)
    {
        typeof(TimeTickerEntity<TimeTickerEntity>)
            .GetProperty(nameof(TimeTickerEntity.LockHolder))!
            .SetValue(entity, value);
    }

    private static void SetInitIdentifier(TimeTickerEntity entity, string value)
    {
        typeof(Utilities.Entities.BaseEntity.BaseTickerEntity)
            .GetProperty(nameof(Utilities.Entities.BaseEntity.BaseTickerEntity.InitIdentifier))!
            .SetValue(entity, value);
    }

    private static void SetCreatedAt(TimeTickerEntity entity, DateTime value)
    {
        typeof(Utilities.Entities.BaseEntity.BaseTickerEntity)
            .GetProperty(nameof(Utilities.Entities.BaseEntity.BaseTickerEntity.CreatedAt))!
            .SetValue(entity, value);
    }

    private static void SetUpdatedAt(TimeTickerEntity entity, DateTime value)
    {
        typeof(Utilities.Entities.BaseEntity.BaseTickerEntity)
            .GetProperty(nameof(Utilities.Entities.BaseEntity.BaseTickerEntity.UpdatedAt))!
            .SetValue(entity, value);
    }

    private static void SetLockedAt(TimeTickerEntity entity, DateTime value)
    {
        typeof(TimeTickerEntity<TimeTickerEntity>)
            .GetProperty(nameof(TimeTickerEntity.LockedAt))!
            .SetValue(entity, (DateTime?)value);
    }

    private static void SetExecutedAt(TimeTickerEntity entity, DateTime value)
    {
        typeof(TimeTickerEntity<TimeTickerEntity>)
            .GetProperty(nameof(TimeTickerEntity.ExecutedAt))!
            .SetValue(entity, (DateTime?)value);
    }

    private static void SetParentId(TimeTickerEntity entity, Guid value)
    {
        typeof(TimeTickerEntity<TimeTickerEntity>)
            .GetProperty(nameof(TimeTickerEntity.ParentId))!
            .SetValue(entity, (Guid?)value);
    }
}
