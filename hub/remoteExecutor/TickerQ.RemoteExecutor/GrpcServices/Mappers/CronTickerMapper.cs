using TickerQ.Grpc.Contracts;
using TickerQ.Utilities.Entities;

namespace TickerQ.RemoteExecutor.GrpcServices.Mappers;

internal static class CronTickerMapper
{
    public static CronTickerEntity ToEntity(CronTickerMessage msg)
    {
        var entity = new CronTickerEntity
        {
            Id = Guid.Parse(msg.Id),
            Function = msg.Function,
            Description = msg.Description,
            Expression = msg.Expression,
            Retries = msg.Retries,
            IsEnabled = msg.IsEnabled,
            Request = msg.Request.IsEmpty ? null : msg.Request.ToByteArray()
        };

        SetInitIdentifier(entity, msg.InitIdentifier);
        SetCreatedAt(entity, msg.CreatedAt?.ToDateTime() ?? DateTime.UtcNow);
        SetUpdatedAt(entity, msg.UpdatedAt?.ToDateTime() ?? DateTime.UtcNow);

        if (msg.RetryIntervals.Count > 0)
            entity.RetryIntervals = msg.RetryIntervals.ToArray();

        return entity;
    }

    private static void SetInitIdentifier(CronTickerEntity entity, string value)
    {
        typeof(Utilities.Entities.BaseEntity.BaseTickerEntity)
            .GetProperty(nameof(Utilities.Entities.BaseEntity.BaseTickerEntity.InitIdentifier))!
            .SetValue(entity, value);
    }

    private static void SetCreatedAt(CronTickerEntity entity, DateTime value)
    {
        typeof(Utilities.Entities.BaseEntity.BaseTickerEntity)
            .GetProperty(nameof(Utilities.Entities.BaseEntity.BaseTickerEntity.CreatedAt))!
            .SetValue(entity, value);
    }

    private static void SetUpdatedAt(CronTickerEntity entity, DateTime value)
    {
        typeof(Utilities.Entities.BaseEntity.BaseTickerEntity)
            .GetProperty(nameof(Utilities.Entities.BaseEntity.BaseTickerEntity.UpdatedAt))!
            .SetValue(entity, value);
    }
}
