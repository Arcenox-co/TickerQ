using TickerQ.Utilities.Base;

namespace TickerQ.Sample.LicenseSmoke;

// Harmless definitions for the dashboard registry. These never send mail, access files,
// contact external services, or alter customer data. Cron seeding is disabled in --suite.
public sealed class PreviewJobs(ILogger<PreviewJobs> logger)
{
    [TickerFunction("PreviewHeartbeat", "* * * * *")]
    public Task Heartbeat(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Local preview heartbeat completed.");
        return Task.CompletedTask;
    }

    [TickerFunction("PreviewDailySummary", "0 2 * * *")]
    public Task DailySummary(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Local preview daily summary completed; no report was sent.");
        return Task.CompletedTask;
    }

    [TickerFunction("PreviewOnDemand")]
    public Task OnDemand(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Local preview on-demand job completed.");
        return Task.CompletedTask;
    }

    [TickerFunction("PreviewSlowJob")]
    public async Task SlowJob(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        const int totalRecords = 20;
        const int batchSize = 5;
        logger.LogInformation("Log demo started. Processing {TotalRecords} sample records in memory.", totalRecords);
        logger.LogInformation("Loading local demo configuration.");
        logger.LogInformation("Validation complete. No external connections are required.");

        for (var batch = 1; batch <= totalRecords / batchSize; batch++)
        {
            logger.LogInformation("Batch {Batch}/4 started.", batch);
            for (var offset = 1; offset <= batchSize; offset++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var record = (batch - 1) * batchSize + offset;
                if (record == 7)
                    logger.LogWarning("Sample record {Record:D2} has no optional display name. Using the demo default.", record);
                if (record == 13)
                {
                    logger.LogError("Demo only: simulated transient error for sample record {Record:D2}. Retrying locally.", record);
                    await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
                    logger.LogInformation("Local retry succeeded for sample record {Record:D2}.", record);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken);
                logger.LogInformation("Processed sample record {Record:D2}/{TotalRecords}. Progress: {Progress}%.",
                    record, totalRecords, record * 100 / totalRecords);
            }
            logger.LogInformation("Batch {Batch}/4 completed: {BatchSize} records processed.", batch, batchSize);
        }

        logger.LogInformation("Demo summary:\n  20 records processed\n  1 optional-field warning\n  1 simulated error recovered");
        logger.LogInformation("Log demo finished successfully. No external services were called.");
    }
}
