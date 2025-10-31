using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Models;

namespace TickerQ.Utilities.Interfaces
{
    internal interface ITickerDashboardRepository<TTimeTicker, TCronTicker> 
        where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
        where TCronTicker : CronTickerEntity, new()
    {
        Task<TTimeTicker[]> GetTimeTickersAsync(CancellationToken cancellationToken = default);
        Task<PaginationResult<TTimeTicker>> GetTimeTickersPaginatedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default);
        Task SetTimeTickerBatchParent(Guid targetId, Guid parentId, RunCondition? batchRunCondition = null);
        Task UnbatchTimeTickerAsync(Guid tickerId);
        Task<IList<Tuple<TickerStatus, int>>> GetTimeTickerFullDataAsync(CancellationToken cancellationToken);
        Task<IList<TickerGraphData>> GetTimeTickersGraphSpecificDataAsync(int pastDays, int futureDays,CancellationToken cancellationToken);
        Task<IList<TickerGraphData>> GetCronTickersGraphSpecificDataAsync(int pastDays, int futureDays,CancellationToken cancellationToken);
        Task<IList<TickerGraphData>> GetCronTickersGraphSpecificDataByIdAsync(Guid id, int pastDays, int futureDays,CancellationToken cancellationToken);
        Task<IList<Tuple<TickerStatus, int>>> GetCronTickerFullDataAsync(CancellationToken cancellationToken);
        Task<CronTickerEntity[]> GetCronTickersAsync(CancellationToken cancellationToken = default);
        Task<PaginationResult<CronTickerEntity>> GetCronTickersPaginatedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default);
        Task AddOnDemandCronTickerOccurrenceAsync(Guid id, CancellationToken cancellationToken = default);
        Task<CronTickerOccurrenceEntity<TCronTicker>[]> GetCronTickersOccurrencesAsync(Guid guid, CancellationToken cancellationToken = default);
        Task<PaginationResult<CronTickerOccurrenceEntity<TCronTicker>>> GetCronTickersOccurrencesPaginatedAsync(Guid guid, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
        Task<IList<CronOccurrenceTickerGraphData>> GetCronTickersOccurrencesGraphDataAsync(Guid guid, CancellationToken cancellationToken = default);
        bool CancelTickerById(Guid tickerId);
        Task DeleteCronTickerByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task DeleteTimeTickerByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task DeleteCronTickerOccurrenceByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<(string, int)> GetTickerRequestByIdAsync(Guid tickerId, TickerType tickerType, CancellationToken cancellationToken = default);
        IEnumerable<(string, (string, string, TickerTaskPriority))> GetTickerFunctions();
        Task UpdateTimeTickerAsync(Guid id, TTimeTicker request, CancellationToken cancellationToken = default);
        Task AddTimeTickerAsync(TTimeTicker request, CancellationToken cancellationToken = default);
        Task AddCronTickerAsync(TCronTicker request, CancellationToken cancellationToken = default);
        Task UpdateCronTickerAsync(Guid id, UpdateCronTickerRequest request, CancellationToken cancellationToken = default);
        Task<IList<(int, int)>> GetLastWeekJobStatusesAsync(CancellationToken cancellationToken = default);
        Task<IList<(TickerStatus, int)>> GetOverallJobStatusesAsync(CancellationToken cancellationToken = default);
        Task<IList<(string, int)>> GetMachineJobsAsync(CancellationToken cancellationToken = default);
    }
}