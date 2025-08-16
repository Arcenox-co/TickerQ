using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Interfaces
{
    internal interface ITickerDashboardRepository
    {
        Task<IList<TimeTickerDto>> GetTimeTickersAsync();
        Task SetTimeTickerBatchParent(Guid targetId, Guid parentId, BatchRunCondition? batchRunCondition = null);
        Task UnbatchTimeTickerAsync(Guid tickerId);
        Task<IList<Tuple<TickerStatus, int>>> GetTimeTickerFullDataAsync(CancellationToken cancellationToken);
        Task<IList<TickerGraphData>> GetTimeTickersGraphSpecificDataAsync(int pastDays, int futureDays,CancellationToken cancellationToken);
        Task<IList<TickerGraphData>> GetCronTickersGraphSpecificDataAsync(int pastDays, int futureDays,CancellationToken cancellationToken);
        Task<IList<TickerGraphData>> GetCronTickersGraphSpecificDataByIdAsync(Guid id, int pastDays, int futureDays,CancellationToken cancellationToken);
        Task<IList<Tuple<TickerStatus, int>>> GetCronTickerFullDataAsync(CancellationToken cancellationToken);
        Task<IList<CronTickerDto>> GetCronTickersAsync();
        Task AddOnDemandCronTickerOccurrenceAsync(Guid id);
        Task<IList<CronTickerOccurrenceDto>> GetCronTickersOccurrencesAsync(Guid guid);
        Task<IList<CronOccurrenceTickerGraphData>> GetCronTickersOccurrencesGraphDataAsync(Guid guid);
        bool CancelTickerById(Guid tickerId);
        Task DeleteCronTickerByIdAsync(Guid id);
        Task DeleteTimeTickerByIdAsync(Guid id);
        Task DeleteCronTickerOccurrenceByIdAsync(Guid id);
        Task<(string, int)> GetTickerRequestByIdAsync(Guid tickerId, TickerType tickerType);
        IEnumerable<(string, (string, string, TickerTaskPriority))> GetTickerFunctions();
        Task UpdateTimeTickerAsync(Guid id, UpdateTimeTickerRequest request);
        Task AddTimeTickerAsync(AddTimeTickerRequest request);
        Task AddCronTickerAsync(AddCronTickerRequest request);
        Task UpdateCronTickerAsync(Guid id, UpdateCronTickerRequest request);
        Task<IList<(int, int)>> GetLastWeekJobStatusesAsync();
        Task<IList<(TickerStatus, int)>> GetOverallJobStatusesAsync();
        Task<IList<(string, int)>> GetMachineJobsAsync();
    }
}