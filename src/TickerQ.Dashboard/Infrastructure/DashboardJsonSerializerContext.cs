using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Entities.BaseEntity;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.Dashboard.Infrastructure;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
// Dashboard response DTOs
[JsonSerializable(typeof(AuthInfoResponse))]
[JsonSerializable(typeof(AuthValidateResponse))]
[JsonSerializable(typeof(DashboardOptionsResponse))]
[JsonSerializable(typeof(ActionResponse))]
[JsonSerializable(typeof(ActionResponseWithId))]
[JsonSerializable(typeof(TickerRequestResponse))]
[JsonSerializable(typeof(TickerFunctionResponse))]
[JsonSerializable(typeof(TickerFunctionResponse[]))]
[JsonSerializable(typeof(NextTickerResponse))]
[JsonSerializable(typeof(HostStatusResponse))]
[JsonSerializable(typeof(FrontendConfigResponse))]
// Tuple responses for statistics
[JsonSerializable(typeof(TupleResponse<int, int>))]
[JsonSerializable(typeof(TupleResponse<int, int>[]))]
[JsonSerializable(typeof(TupleResponse<string, int>))]
[JsonSerializable(typeof(TupleResponse<string, int>[]))]
[JsonSerializable(typeof(TupleResponse<TickerStatus, int>))]
[JsonSerializable(typeof(TupleResponse<TickerStatus, int>[]))]
// Graph data DTOs
[JsonSerializable(typeof(TickerGraphData))]
[JsonSerializable(typeof(TickerGraphData[]))]
[JsonSerializable(typeof(IList<TickerGraphData>))]
[JsonSerializable(typeof(CronOccurrenceTickerGraphData))]
[JsonSerializable(typeof(CronOccurrenceTickerGraphData[]))]
[JsonSerializable(typeof(IList<CronOccurrenceTickerGraphData>))]
// Entity types (base classes for serialization)
[JsonSerializable(typeof(BaseTickerEntity))]
[JsonSerializable(typeof(TimeTickerEntity))]
[JsonSerializable(typeof(TimeTickerEntity[]))]
[JsonSerializable(typeof(CronTickerEntity))]
[JsonSerializable(typeof(CronTickerEntity[]))]
[JsonSerializable(typeof(CronTickerOccurrenceEntity<CronTickerEntity>))]
[JsonSerializable(typeof(CronTickerOccurrenceEntity<CronTickerEntity>[]))]
// Pagination results
[JsonSerializable(typeof(PaginationResult<TimeTickerEntity>))]
[JsonSerializable(typeof(PaginationResult<CronTickerEntity>))]
[JsonSerializable(typeof(PaginationResult<CronTickerOccurrenceEntity<CronTickerEntity>>))]
// Tuple types used in graph data
[JsonSerializable(typeof(Tuple<int, int>))]
[JsonSerializable(typeof(Tuple<int, int>[]))]
[JsonSerializable(typeof(Tuple<TickerStatus, int>))]
[JsonSerializable(typeof(IList<Tuple<TickerStatus, int>>))]
[JsonSerializable(typeof(IList<Tuple<int, int>>))]
// Enums
[JsonSerializable(typeof(TickerStatus))]
[JsonSerializable(typeof(TickerType))]
[JsonSerializable(typeof(RunCondition))]
// Primitives used in responses
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(Guid?))]
[JsonSerializable(typeof(Guid[]))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTime?))]
[JsonSerializable(typeof(TimeSpan))]
[JsonSerializable(typeof(byte[]))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(int[]))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(System.Text.Json.JsonElement))]
internal partial class DashboardJsonSerializerContext : JsonSerializerContext
{
}
