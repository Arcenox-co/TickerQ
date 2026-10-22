using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public sealed class EfDashboardMetadataTests
{
    [Fact]
    public async Task List_filters_counts_and_chain_summaries_do_not_select_payload_columns()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var capture = new QueryCapture();
        var options = new DbContextOptionsBuilder<TestTickerQDbContext>()
            .UseSqlite(connection).AddInterceptors(capture).Options;
        await using var db = new TestTickerQDbContext(options);
        await db.Database.EnsureCreatedAsync();
        using var services = new ServiceCollection()
            .AddSingleton<IDbContextFactory<TestTickerQDbContext>>(new PooledDbContextFactory<TestTickerQDbContext>(options))
            .BuildServiceProvider();
        var clock = Substitute.For<ITickerClock>(); clock.UtcNow.Returns(DateTime.UtcNow);
        var provider = new TestableProvider(services, clock, new SchedulerOptionsBuilder(), Substitute.For<ITickerQRedisContext>());
        var payload = new byte[1024 * 1024];
        var root = new TimeTickerEntity { Function = "chain", Request = payload, ExecutionTime = DateTime.UtcNow,
            ProgressPercent = 42.5, ProgressMessage = "Preparing invoices", ProgressUpdatedAt = DateTime.UtcNow,
            Children = [new TimeTickerEntity { Function = "child", Request = payload,
                Children = [new TimeTickerEntity { Function = "leaf", Request = payload }] }] };
        await provider.AddTimeTickers([root, new TimeTickerEntity { Function = "single", Request = payload }], CancellationToken.None);
        var cron = new CronTickerEntity { Function = "cron", Expression = "* * * * *", Request = payload, Retries = 3 };
        await provider.InsertCronTickers([cron], CancellationToken.None);
        db.Add(new CronTickerOccurrenceEntity<CronTickerEntity>
        {
            Id = Guid.NewGuid(), CronTickerId = cron.Id, Status = TickerStatus.Done,
            ExecutionTime = DateTime.UtcNow, ExecutedAt = DateTime.UtcNow,
            ProgressPercent = 67.5, ProgressMessage = "Uploading report", ProgressUpdatedAt = DateTime.UtcNow,
        });
        db.Add(new TimeTickerEntity { Function = "foreign", ApplicationNamespaceKey = "other", Request = payload });
        await db.SaveChangesAsync();
        capture.Commands.Clear();

        var dashboard = new TickerDashboardDataService<TimeTickerEntity, CronTickerEntity>(provider);
        var chains = await dashboard.GetTimeTickersFlatAsync(new TimeTickerQueryFilter { HasChildren = true, PageSize = 1, SortBy = "functionname" });
        var item = Assert.Single(chains.Items);
        Assert.Equal(root.Id, item.Id);
        Assert.Equal(1, chains.TotalCount);
        Assert.Equal(1, item.ChildCount);
        Assert.Equal(3, item.ChainSummary.TotalCount);
        Assert.Equal(42.5, item.ProgressPercent);
        Assert.Equal("Preparing invoices", item.ProgressMessage);
        Assert.NotNull(item.ProgressUpdatedAt);
        var singles = await dashboard.GetTimeTickersFlatAsync(new TimeTickerQueryFilter { HasChildren = false });
        Assert.Equal("single", Assert.Single(singles.Items).FunctionName);
        Assert.Equal(1, singles.TotalCount);
        var all = await dashboard.GetTimeTickersFlatAsync(new TimeTickerQueryFilter { PageSize = 1, SortBy = "functionname", SortDescending = false });
        Assert.Equal(2, all.TotalCount);
        Assert.Equal("chain", Assert.Single(all.Items).FunctionName);
        var schedule = Assert.Single((await dashboard.GetCronTickersFlatAsync(new CronTickerQueryFilter())).Items);
        Assert.Equal(cron.Id, schedule.Id);
        Assert.Equal(1, schedule.OccurrenceCount);
        Assert.Equal(TickerStatus.Done, schedule.LastRunStatus);
        Assert.Equal(3, schedule.Retries);
        var occurrence = Assert.Single((await dashboard.GetCronOccurrencesFlatAsync(
            cron.Id, new CronOccurrenceQueryFilter())).Items);
        Assert.Equal(67.5, occurrence.ProgressPercent);
        Assert.Equal("Uploading report", occurrence.ProgressMessage);
        Assert.NotNull(occurrence.ProgressUpdatedAt);
        Assert.NotEmpty(capture.Commands);
        Assert.All(capture.Commands, sql => Assert.DoesNotContain("\"Request\"", sql));
        capture.Commands.Clear();
        var totals = await dashboard.GetOverallStatusesAsync();
        Assert.Equal(4, totals.Single(row => row.Status == TickerStatus.Idle).Count);
        Assert.Equal(1, totals.Single(row => row.Status == TickerStatus.Done).Count);
        Assert.Equal(2, capture.Commands.Count);
        Assert.All(capture.Commands, sql =>
        {
            Assert.Contains("GROUP BY", sql);
            Assert.DoesNotContain("\"Request\"", sql);
            Assert.DoesNotContain("\"Function\"", sql);
        });
        // Projection is a read optimization, never a mutation of stored payloads.
        Assert.Equal(payload.Length, (await provider.GetTimeTickerById(root.Id, default)).Request.Length);
    }

    private sealed class QueryCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
