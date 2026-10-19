using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public sealed class EfMonitoringQueryTests
{
    [Fact]
    public async Task MonitoringProjectsOnlyMetadata_AndFiltersPartitionsAndPagesInSql()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var capture = new Capture();
        var dbOptions = new DbContextOptionsBuilder<TestTickerQDbContext>().UseSqlite(connection).AddInterceptors(capture).Options;
        var now = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        await using (var context = new TestTickerQDbContext(dbOptions))
        {
            await context.Database.EnsureCreatedAsync();
            foreach (var foreign in new[] { false, true })
            {
                var cron = new CronTickerEntity { Id = Guid.NewGuid(), Function = "monitor", Expression = "* * * * *", Request = new byte[100_000] };
                if (foreign) cron.ApplicationNamespaceKey = "other-partition";
                context.Add(cron);
                foreach (var status in new[] { TickerStatus.Done, TickerStatus.InProgress })
                {
                    var time = new TimeTickerEntity { Id = Guid.NewGuid(), Function = "monitor", Status = status,
                        ExecutedAt = now, ElapsedTime = 250, Request = new byte[100_000], ExceptionMessage = new string('x', 10_000) };
                    var occurrence = new CronTickerOccurrenceEntity<CronTickerEntity> { Id = Guid.NewGuid(), CronTickerId = cron.Id,
                        Status = status, ExecutedAt = now, ExecutionTime = status == TickerStatus.Done ? now : now.AddMinutes(-1), ElapsedTime = 500 };
                    if (foreign) { time.ApplicationNamespaceKey = "other-partition"; occurrence.ApplicationNamespaceKey = "other-partition"; }
                    context.AddRange(time, occurrence);
                }
            }
            await context.SaveChangesAsync();
        }
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<TestTickerQDbContext>>(new PooledDbContextFactory<TestTickerQDbContext>(dbOptions));
        await using var container = services.BuildServiceProvider();
        var provider = new TestableProvider(container, Substitute.For<ITickerClock>(), new SchedulerOptionsBuilder(), Substitute.For<ITickerQRedisContext>());
        capture.Commands.Clear();
        var result = await provider.ReadMonitoringSnapshotAsync("monitor", now.AddHours(-1), now);

        Assert.Null(result.Error);
        Assert.Equal(2, result.Completed.Length);
        Assert.Equal(2, result.Running.Length);
        Assert.Equal(new long[] { 250, 500 }, result.Completed.Select(x => x.ElapsedMilliseconds).Order());
        Assert.Equal(5, capture.Commands.Count);
        Assert.All(capture.Commands, sql =>
        {
            Assert.Contains("WHERE", sql);
            Assert.Contains("ApplicationNamespaceKey", sql);
            Assert.Contains("LIMIT", sql);
            Assert.DoesNotContain("Request", sql);
            Assert.DoesNotContain("ExceptionMessage", sql);
            Assert.DoesNotContain("Description", sql);
            Assert.DoesNotContain("SELECT *", sql);
        });
        Assert.Equal(2, capture.Commands.Count(sql => sql.Contains("ORDER BY")));
    }

    private sealed class Capture : DbCommandInterceptor
    {
        internal List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
