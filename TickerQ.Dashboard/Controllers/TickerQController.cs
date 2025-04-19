using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Threading;
using TickerQ.Dashboard.Controllers.Attributes;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.Dashboard.Controllers
{
    [ApiController]
    [BasicAuth]
    [Route("api")]
    public class TickerQController : ControllerBase
    {
        private ITickerDashboardRepository TickerDashboardRepository
            => HttpContext.RequestServices.GetService<ITickerDashboardRepository>();

        private ITickerHost TickerHost
            => HttpContext.RequestServices.GetService<ITickerHost>();

        private TickerOptionsBuilder TickerOptionsBuilder
            => HttpContext.RequestServices.GetService<TickerOptionsBuilder>();

        [HttpGet("options")]
        public IActionResult GetOptions()
        {
            return Ok(new
            {
                MaxConcurrency = TickerOptionsBuilder.MaxConcurrency,
                CurrentMachine = TickerOptionsBuilder.InstanceIdentifier,
                LastHostExceptionMessage = TickerOptionsBuilder.LastHostExceptionMessage,
            });
        }

        [HttpGet("cron-tickers")]
        public async Task<IActionResult> GetCronTickersAsync()
        {
            return Ok(await TickerDashboardRepository.GetCronTickersAsync());
        }

        [HttpGet("time-tickers")]
        public async Task<IActionResult> GetTimeTickersAsync()
        {
            return Ok(await TickerDashboardRepository.GetTimeTickersAsync());
        }

        [HttpGet("time-tickers/:graph-data-range")]
        public async Task<IActionResult> GetTimeTickersGraphDataByRangeAsync([FromQuery] int pastDays = 3,
            [FromQuery] int futureDays = 3,
            CancellationToken cancellationToken = default)
        {
            return Ok(await TickerDashboardRepository.GetTimeTickersGraphSpecificDataAsync(pastDays, futureDays,
                cancellationToken));
        }

        [HttpGet("time-tickers/:graph-data")]
        public async Task<IActionResult> GetTimeTickersGraphDataAsync(CancellationToken cancellationToken)
        {
            return Ok(await TickerDashboardRepository.GetTimeTickerFullDataAsync(cancellationToken));
        }

        [HttpGet("cron-tickers/:graph-data-range")]
        public async Task<IActionResult> GetCronTickersGraphDataByRangeAsync([FromQuery] int pastDays = 3,
            [FromQuery] int futureDays = 3, CancellationToken cancellationToken = default)
        {
            return Ok(await TickerDashboardRepository.GetCronTickersGraphSpecificDataAsync(pastDays, futureDays,
                cancellationToken));
        }

        [HttpGet("cron-tickers/:graph-data-range-id")]
        public async Task<IActionResult> GetCronTickersByIdGraphDataByRangeAsync([FromQuery] Guid id,
            [FromQuery] int pastDays = 3,
            [FromQuery] int futureDays = 3, CancellationToken cancellationToken = default)
        {
            return Ok(await TickerDashboardRepository.GetCronTickersGraphSpecificDataByIdAsync(id, pastDays, futureDays,
                cancellationToken));
        }

        [HttpGet("cron-tickers/:graph-data")]
        public async Task<IActionResult> GetCronTickersGraphDataAsync(CancellationToken cancellationToken)
        {
            return Ok(await TickerDashboardRepository.GetCronTickerFullDataAsync(cancellationToken));
        }

        [HttpGet("cron-ticker-occurrences/:cronTickerId")]
        public async Task<IActionResult> GetCronTickerOccurrencesAsync([FromQuery] Guid cronTickerId)
        {
            var data = await TickerDashboardRepository.GetCronTickersOccurrencesAsync(cronTickerId);

            return Ok(data);
        }

        [HttpGet("cron-ticker-occurrences/:cronTickerId/:graph-data")]
        public async Task<IActionResult> GetCronTickerOccurrencesGraphDataAsync([FromQuery] Guid cronTickerId)
        {
            return Ok(await TickerDashboardRepository.GetCronTickersOccurrencesGraphDataAsync(cronTickerId));
        }

        [HttpPost("ticker/:cancel")]
        public IActionResult CancelTickerByIdAsync([FromQuery] Guid id)
        {
            if (TickerDashboardRepository.CancelTickerById(id))
                return Ok();

            return BadRequest();
        }

        [HttpDelete("/time-ticker/:delete")]
        public async Task<IActionResult> DeleteTimeTickerAsync([FromQuery] Guid id)
        {
            await TickerDashboardRepository.DeleteTimeTickerByIdAsync(id);
            return Ok();
        }

        [HttpDelete("cron-ticker/:delete")]
        public async Task<IActionResult> DeleteCronTickerAsync([FromQuery] Guid id)
        {
            await TickerDashboardRepository.DeleteCronTickerByIdAsync(id);
            return Ok();
        }

        [HttpDelete("cron-ticker-occurrence/:delete")]
        public async Task<IActionResult> DeleteCronTickerOccurrenceAsync([FromQuery] Guid id)
        {
            await TickerDashboardRepository.DeleteCronTickerOccurrenceByIdAsync(id);
            return Ok();
        }

        [HttpGet("ticker-request/:id")]
        public async Task<IActionResult> GetTickerRequestByIdAsync([FromQuery] Guid tickerId, TickerType tickerType)
        {
            var resultData = await TickerDashboardRepository.GetTickerRequestByIdAsync(tickerId, tickerType);

            var response = new
            {
                Result = resultData.Item1,
                MatchType = resultData.Item2,
            };
            return Ok(response);
        }

        [HttpGet("ticker-functions")]
        public IActionResult GetTickerFunctionsAsync()
        {
            var result = TickerDashboardRepository.GetTickerFunctions().Select(x => new
            {
                FunctionName = x.Item1,
                FunctionRequestNamespace = x.Item2.Item1,
                FunctionRequestType = x.Item2.Item2,
                Priority = x.Item2.Item3,
            });

            return Ok(result);
        }

        [HttpPut("time-ticker/:update")]
        public async Task<IActionResult> UpdateTimeTickerAsync([FromQuery] Guid id, [FromBody] JsonElement json)
        {
            var jsonString = json.GetRawText();
            await TickerDashboardRepository.UpdateTimeTickerAsync(id, jsonString);
            return Ok();
        }

        [HttpPost("time-ticker/:add")]
        public async Task<IActionResult> UpdateTimeTickerAsync([FromBody] JsonElement json)
        {
            var jsonString = json.GetRawText();
            await TickerDashboardRepository.AddTimeTickerAsync(jsonString);
            return Ok();
        }

        [HttpPost("cron-ticker/:add")]
        public async Task<IActionResult> AddCronTickerAsync([FromBody] JsonElement json)
        {
            var jsonString = json.GetRawText();
            await TickerDashboardRepository.AddCronTickerAsync(jsonString);
            return Ok();
        }

        [HttpPut("/cron-ticker/:update")]
        public async Task<IActionResult> UpdateCronTickerAsync([FromQuery] Guid id, [FromBody] JsonElement json)
        {
            var jsonString = json.GetRawText();
            await TickerDashboardRepository.UpdateCronTickerAsync(id, jsonString);
            return Ok();
        }

        [HttpGet("ticker-host/:next-ticker")]
        public IActionResult GetNextTickerAsync()
        {
            var result = new
            {
                NextOccurrence = TickerHost.NextPlannedOccurrence
            };
            return Ok(result);
        }

        [HttpPost("ticker-host/:stop")]
        public IActionResult StopTickerHostAsync()
        {
            TickerHost.Stop();
            return Ok();
        }

        [HttpPost("ticker-host/:start")]
        public IActionResult StartTickerHostAsync()
        {
            TickerHost.Start();
            return Ok();
        }

        [HttpPost("ticker-host/:restart")]
        public IActionResult RestartTickerHostAsync()
        {
            TickerHost.Restart();
            return Ok();
        }

        [HttpGet("ticker-host/:status")]
        public IActionResult GetTickerHostStatusAsync()
        {
            return Ok(new { IsRunning = TickerHost.IsRunning() });
        }

        [HttpGet("ticker/statuses/:get-last-week")]
        public async Task<IActionResult> GetLastWeekJobStatusAsync()
        {
            var jobStatuses = await TickerDashboardRepository.GetLastWeekJobStatusesAsync();

            return Ok(jobStatuses.Select(x => new { x.Item1, x.Item2 }).ToArray());
        }

        [HttpGet("ticker/statuses/:get")]
        public async Task<IActionResult> GetJobStatusesAsync()
        {
            var jobStatuses = await TickerDashboardRepository.GetOverallJobStatusesAsync();

            return Ok(jobStatuses.Select(x => new { x.Item1, x.Item2 }).ToArray());
        }

        [HttpGet("ticker/machine/:jobs")]
        public async Task<IActionResult> GetMachineJobsAsync()
        {
            var machineJobs = await TickerDashboardRepository.GetMachineJobsAsync();

            return Ok(machineJobs.Select(x => new { item1 = x.Item1, item2 = x.Item2 }).ToArray());
        }
    }
}