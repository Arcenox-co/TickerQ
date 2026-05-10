using Google.Protobuf.WellKnownTypes;
using TickerQ.RemoteExecutor.Grpc;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.RemoteExecutor.GrpcServices;

internal static class DashboardProtoMapper
{
    // ===== Enums =====

    public static TickerStatusProto ToProto(this TickerStatus status) => (TickerStatusProto)(int)status;
    public static TickerStatus FromProto(this TickerStatusProto status) => (TickerStatus)(int)status;

    public static TickerTaskPriorityProto ToProto(this TickerTaskPriority p) => (TickerTaskPriorityProto)(int)p;

    public static RunConditionProto ToProto(this RunCondition? rc)
        => rc.HasValue ? (RunConditionProto)((int)rc.Value + 1) : RunConditionProto.RunConditionUnspecified;

    public static ExecutionTypeProto ToProto(this ExecutionType t) => (ExecutionTypeProto)(int)t;
    public static ExecutionType FromProto(this ExecutionTypeProto t) => (ExecutionType)(int)t;

    // ===== Timestamps =====

    public static Timestamp ToProto(this DateTime dt)
    {
        var utc = dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        return Timestamp.FromDateTime(utc);
    }

    public static DateTime? FromProto(this Timestamp ts)
        => ts?.ToDateTime();

    // ===== DTO → Proto =====

    public static TimeTickerFlatProto ToProto(this TimeTickerFlatDto dto)
    {
        var p = new TimeTickerFlatProto
        {
            Id = dto.Id.ToString(),
            FunctionName = dto.FunctionName ?? string.Empty,
            Status = dto.Status.ToProto(),
            ElapsedTime = dto.ElapsedTime,
            Retries = dto.Retries,
            RetryCount = dto.RetryCount,
            Priority = dto.Priority.ToProto(),
            CreatedAt = dto.CreatedAt.ToProto(),
            ChildCount = dto.ChildCount,
            RunCondition = dto.RunCondition.ToProto()
        };
        if (dto.ScheduledFor.HasValue) p.ScheduledFor = dto.ScheduledFor.Value.ToProto();
        if (dto.ExecutedAt.HasValue) p.ExecutedAt = dto.ExecutedAt.Value.ToProto();
        if (dto.ExceptionMessage != null) p.ExceptionMessage = dto.ExceptionMessage;
        if (dto.SkippedReason != null) p.SkippedReason = dto.SkippedReason;
        if (dto.ParentId.HasValue) p.ParentId = dto.ParentId.Value.ToString();
        if (dto.Description != null) p.Description = dto.Description;
        return p;
    }

    public static CronTickerFlatProto ToProto(this CronTickerFlatDto dto)
    {
        var p = new CronTickerFlatProto
        {
            Id = dto.Id.ToString(),
            FunctionName = dto.FunctionName ?? string.Empty,
            Expression = dto.Expression ?? string.Empty,
            Retries = dto.Retries,
            Priority = dto.Priority.ToProto(),
            CreatedAt = dto.CreatedAt.ToProto(),
            OccurrenceCount = dto.OccurrenceCount,
            IsEnabled = dto.IsEnabled,
            IsSystemPaused = dto.IsSystemPaused
        };
        if (dto.Description != null) p.Description = dto.Description;
        if (dto.LastRunStatus.HasValue) p.LastRunStatus = dto.LastRunStatus.Value.ToProto();
        if (dto.LastRunAt.HasValue) p.LastRunAt = dto.LastRunAt.Value.ToProto();
        return p;
    }

    public static CronOccurrenceFlatProto ToProto(this CronOccurrenceFlatDto dto)
    {
        var p = new CronOccurrenceFlatProto
        {
            Id = dto.Id.ToString(),
            CronTickerId = dto.CronTickerId.ToString(),
            Status = dto.Status.ToProto(),
            ScheduledFor = dto.ScheduledFor.ToProto(),
            ElapsedTime = dto.ElapsedTime,
            RetryCount = dto.RetryCount,
            CreatedAt = dto.CreatedAt.ToProto()
        };
        if (dto.FunctionName != null) p.FunctionName = dto.FunctionName;
        if (dto.ExceptionMessage != null) p.ExceptionMessage = dto.ExceptionMessage;
        if (dto.SkippedReason != null) p.SkippedReason = dto.SkippedReason;
        if (dto.ExecutedAt.HasValue) p.ExecutedAt = dto.ExecutedAt.Value.ToProto();
        return p;
    }

    public static ExecutionFlatProto ToProto(this ExecutionFlatDto dto)
    {
        var p = new ExecutionFlatProto
        {
            Id = dto.Id.ToString(),
            Type = dto.Type.ToProto(),
            Status = dto.Status.ToProto(),
            ScheduledFor = dto.ScheduledFor.ToProto(),
            ElapsedTime = dto.ElapsedTime,
            RetryCount = dto.RetryCount,
            Retries = dto.Retries,
            ChildCount = dto.ChildCount
        };
        if (dto.FunctionName != null) p.FunctionName = dto.FunctionName;
        if (dto.ExceptionMessage != null) p.ExceptionMessage = dto.ExceptionMessage;
        if (dto.SkippedReason != null) p.SkippedReason = dto.SkippedReason;
        if (dto.ExecutedAt.HasValue) p.ExecutedAt = dto.ExecutedAt.Value.ToProto();
        return p;
    }

    public static PaginationMetadata ToProto<T>(this PaginationResult<T> result) => new()
    {
        TotalCount = result.TotalCount,
        PageNumber = result.PageNumber,
        PageSize = result.PageSize,
        TotalPages = result.TotalPages,
        HasPrevious = result.HasPreviousPage,
        HasNext = result.HasNextPage
    };

    // ===== Request → Filter =====

    public static TimeTickerQueryFilter ToFilter(this TimeTickerQueryRequest r)
    {
        var f = new TimeTickerQueryFilter
        {
            FunctionName = r.HasFunctionName ? r.FunctionName : null,
            Statuses = r.Statuses.Select(s => s.FromProto()).ToArray(),
            ScheduledFrom = r.ScheduledFrom?.ToDateTime(),
            ScheduledTo = r.ScheduledTo?.ToDateTime(),
            ExecutedFrom = r.ExecutedFrom?.ToDateTime(),
            ExecutedTo = r.ExecutedTo?.ToDateTime(),
            ParentOnly = r.HasParentOnly ? r.ParentOnly : null,
            HasChildren = r.HasHasChildren ? r.HasChildren : null,
            Search = r.HasSearch ? r.Search : null,
            SortBy = r.HasSortBy ? r.SortBy : null,
            SortDescending = r.SortDescending,
            PageNumber = r.PageNumber <= 0 ? 1 : r.PageNumber,
            PageSize = r.PageSize <= 0 ? 20 : r.PageSize
        };
        return f;
    }

    public static CronTickerQueryFilter ToFilter(this CronTickerQueryRequest r) => new()
    {
        FunctionName = r.HasFunctionName ? r.FunctionName : null,
        LastRunStatuses = r.LastRunStatuses.Select(s => s.FromProto()).ToArray(),
        Expression = r.HasExpression ? r.Expression : null,
        Search = r.HasSearch ? r.Search : null,
        SortBy = r.HasSortBy ? r.SortBy : null,
        SortDescending = r.SortDescending,
        PageNumber = r.PageNumber <= 0 ? 1 : r.PageNumber,
        PageSize = r.PageSize <= 0 ? 20 : r.PageSize
    };

    public static CronOccurrenceQueryFilter ToFilter(this CronOccurrenceQueryRequest r) => new()
    {
        Statuses = r.Statuses.Select(s => s.FromProto()).ToArray(),
        ScheduledFrom = r.ScheduledFrom?.ToDateTime(),
        ScheduledTo = r.ScheduledTo?.ToDateTime(),
        SortBy = r.HasSortBy ? r.SortBy : null,
        SortDescending = r.SortDescending,
        PageNumber = r.PageNumber <= 0 ? 1 : r.PageNumber,
        PageSize = r.PageSize <= 0 ? 20 : r.PageSize
    };

    public static ExecutionQueryFilter ToFilter(this ExecutionQueryRequest r) => new()
    {
        Types = r.Types_.Select(t => t.FromProto()).ToArray(),
        Statuses = r.Statuses.Select(s => s.FromProto()).ToArray(),
        FunctionName = r.HasFunctionName ? r.FunctionName : null,
        ExecutedFrom = r.ExecutedFrom?.ToDateTime(),
        ExecutedTo = r.ExecutedTo?.ToDateTime(),
        Search = r.HasSearch ? r.Search : null,
        SortBy = r.HasSortBy ? r.SortBy : null,
        SortDescending = r.SortDescending,
        PageNumber = r.PageNumber <= 0 ? 1 : r.PageNumber,
        PageSize = r.PageSize <= 0 ? 20 : r.PageSize
    };

    // ===== Nodes / Functions / Host / Graphs =====

    public static NodeProto ToProto(this NodeDto dto)
    {
        var p = new NodeProto
        {
            NodeName = dto.NodeName ?? string.Empty,
            HealthStatus = (NodeHealthStatusProto)(int)dto.HealthStatus,
            FunctionCount = dto.FunctionCount,
            ActiveJobs = dto.ActiveJobs
        };
        if (dto.LastHeartbeat.HasValue) p.LastHeartbeat = dto.LastHeartbeat.Value.ToProto();
        return p;
    }

    public static FunctionProto ToProto(this FunctionInfoDto dto)
    {
        var p = new FunctionProto
        {
            FunctionName = dto.FunctionName ?? string.Empty,
            Priority = dto.Priority.ToProto()
        };
        if (dto.RequestType != null) p.RequestType = dto.RequestType;
        if (dto.RequestExample != null) p.RequestExample = dto.RequestExample;
        if (dto.CronExpression != null) p.CronExpression = dto.CronExpression;
        return p;
    }

    public static HostStatusProto ToProto(this HostStatusDto dto) => new()
    {
        IsRunning = dto.IsRunning,
        ActiveThreads = dto.ActiveThreads,
        MaxConcurrency = dto.MaxConcurrency
    };

    public static NextTickerProto ToProto(this NextTickerDto dto)
    {
        var p = new NextTickerProto { Type = dto.Type.ToProto() };
        if (dto.Id.HasValue) p.Id = dto.Id.Value.ToString();
        if (dto.FunctionName != null) p.FunctionName = dto.FunctionName;
        if (dto.ScheduledFor.HasValue) p.ScheduledFor = dto.ScheduledFor.Value.ToProto();
        return p;
    }

    public static GraphBucketProto ToProto(this GraphBucketDto dto)
    {
        var p = new GraphBucketProto { Date = dto.Date.ToProto() };
        if (dto.Counts != null)
        {
            foreach (var (status, count) in dto.Counts)
                p.Counts.Add(new StatusCountProto { Status = status.ToProto(), Count = count });
        }
        return p;
    }
}
