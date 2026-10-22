using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TickerQ.Dashboard.Assistant;
using TickerQ.Utilities;
using TickerQ.Utilities.DashboardDtos;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Tests;

public sealed class AssistantAutoInvestigationServiceTests
{
    [Fact]
    public async Task Start_rejects_a_history_store_without_cluster_coordination()
    {
        var assistantOptions = new AssistantOptionsBuilder()
            .UseChatClient(new StaticClient("unused"))
            .ConfigureRunInvestigator(options => options.EnableAutomaticInvestigations());
        using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(Substitute.For<IAssistantInvestigationStore>())
            .BuildServiceProvider();
        var service = new AssistantAutoInvestigationService<TimeTickerEntity, CronTickerEntity>(
            services.GetRequiredService<IServiceScopeFactory>(), assistantOptions,
            services.GetRequiredService<ILogger<AssistantAutoInvestigationService<TimeTickerEntity, CronTickerEntity>>>());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.StartAsync(CancellationToken.None));

        Assert.Contains(nameof(IAssistantInvestigationCoordinator), error.Message);
    }

    [Fact]
    public async Task Sweep_investigates_only_terminal_failures_with_exhausted_retries()
    {
        var exhaustedId = Guid.NewGuid();
        var retryingId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var data = Substitute.For<ITickerDashboardDataService<TimeTickerEntity, CronTickerEntity>>();
        data.GetExecutionsFlatAsync(Arg.Any<ExecutionQueryFilter>(), Arg.Any<CancellationToken>())
            .Returns(new PaginationResult<ExecutionFlatDto>(
            [
                new() { Id = exhaustedId, FunctionName = "Invoices.Charge", Status = TickerStatus.Failed, Retries = 1, RetryCount = 1, ExecutedAt = now },
                new() { Id = retryingId, FunctionName = "Invoices.Export", Status = TickerStatus.Failed, Retries = 2, RetryCount = 1, ExecutedAt = now },
            ], 2, 1, 10));
        data.GetTimeTickerByIdAsync(exhaustedId, Arg.Any<CancellationToken>()).Returns(new TimeTickerFlatDto
        {
            Id = exhaustedId,
            FunctionName = "Invoices.Charge",
            Status = TickerStatus.Failed,
            Retries = 1,
            RetryCount = 1,
            ExecutedAt = now,
            ExceptionMessage = "Payment provider rejected the request.",
        });

        var store = Substitute.For<IAssistantInvestigationStore>();
        store.TryStartAsync(Arg.Any<AssistantInvestigationRecord>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());
        var coordinator = SuccessfulCoordinator();
        var client = new StaticClient("The provider rejection is confirmed by the stored exception.");
        var assistantOptions = new AssistantOptionsBuilder()
            .UseChatClient(client)
            .WithModelName("ops-model")
            .ConfigureRunInvestigator(options => options.EnableAutomaticInvestigations(automatic => automatic
                .DetectSuccessfulRecovery(false)
                .WithBatchSize(10)));
        var scheduler = new SchedulerOptionsBuilder();
        scheduler.BindRuntimeActivationScope("orders-production", 1, schedulerEnabled: true);

        using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(assistantOptions)
            .AddSingleton(scheduler)
            .AddSingleton(data)
            .AddSingleton(store)
            .AddSingleton(coordinator)
            .AddSingleton(sp => new TickerAssistantService(assistantOptions, sp))
            .BuildServiceProvider();
        var service = new AssistantAutoInvestigationService<TimeTickerEntity, CronTickerEntity>(
            services.GetRequiredService<IServiceScopeFactory>(), assistantOptions,
            services.GetRequiredService<ILogger<AssistantAutoInvestigationService<TimeTickerEntity, CronTickerEntity>>>());

        await service.RunOnceAsync(CancellationToken.None);

        await store.Received(1).TryStartAsync(Arg.Is<AssistantInvestigationRecord>(record =>
            record.ExecutionId == exhaustedId &&
            record.ApplicationNamespace == "orders-production" &&
            record.Trigger == AssistantInvestigationValues.Automatic), Arg.Any<CancellationToken>());
        await store.Received(1).CompleteAsync("orders-production", Arg.Any<Guid>(),
            Arg.Is<string>(summary => summary.Contains("provider rejection")), "ops-model", null,
            Arg.Any<CancellationToken>());
        await data.DidNotReceive().GetTimeTickerByIdAsync(retryingId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sweep_reuses_matching_completed_diagnosis_without_calling_the_model()
    {
        var executionId = Guid.NewGuid();
        var investigationId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var data = Substitute.For<ITickerDashboardDataService<TimeTickerEntity, CronTickerEntity>>();
        data.GetExecutionsFlatAsync(Arg.Any<ExecutionQueryFilter>(), Arg.Any<CancellationToken>())
            .Returns(new PaginationResult<ExecutionFlatDto>(
            [
                new() { Id = executionId, FunctionName = "Invoices.Charge", Status = TickerStatus.Failed,
                    ExecutedAt = now },
            ], 1, 1, 10));
        data.GetTimeTickerByIdAsync(executionId, Arg.Any<CancellationToken>()).Returns(new TimeTickerFlatDto
        {
            Id = executionId,
            FunctionName = "Invoices.Charge",
            Status = TickerStatus.Failed,
            ExecutedAt = now,
            ExceptionMessage = """
                {"Type":"System.InvalidOperationException","Message":"Order 9876 failed","SourceFile":"ChargeInvoice.cs","SourceLine":42}
                """,
        });

        var store = Substitute.For<IAssistantInvestigationStore>();
        store.TryStartAsync(Arg.Any<AssistantInvestigationRecord>(), Arg.Any<CancellationToken>())
            .Returns(investigationId);
        store.FindReusableAsync("orders-production", Arg.Any<string>(), executionId,
                Arg.Any<CancellationToken>())
            .Returns(new AssistantInvestigationRecord
            {
                Id = Guid.NewGuid(),
                ApplicationNamespace = "orders-production",
                ExecutionId = Guid.NewGuid(),
                State = AssistantInvestigationValues.Completed,
                Summary = "The charge code failed at the application frame.",
                Model = "ops-model",
            });
        var coordinator = SuccessfulCoordinator();
        var client = new StaticClient("This must not be requested.");
        var assistantOptions = new AssistantOptionsBuilder()
            .UseChatClient(client)
            .WithModelName("ops-model")
            .ConfigureRunInvestigator(options => options.EnableAutomaticInvestigations(automatic => automatic
                .DetectSuccessfulRecovery(false)
                .WithBatchSize(10)));
        var scheduler = new SchedulerOptionsBuilder();
        scheduler.BindRuntimeActivationScope("orders-production", 1, schedulerEnabled: true);

        using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(assistantOptions)
            .AddSingleton(scheduler)
            .AddSingleton(data)
            .AddSingleton(store)
            .AddSingleton(coordinator)
            .AddSingleton(sp => new TickerAssistantService(assistantOptions, sp))
            .BuildServiceProvider();
        var service = new AssistantAutoInvestigationService<TimeTickerEntity, CronTickerEntity>(
            services.GetRequiredService<IServiceScopeFactory>(), assistantOptions,
            services.GetRequiredService<ILogger<AssistantAutoInvestigationService<TimeTickerEntity, CronTickerEntity>>>());

        await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, client.CallCount);
        await store.Received(1).TryStartAsync(Arg.Is<AssistantInvestigationRecord>(record =>
            record.ExecutionId == executionId &&
            record.Trigger == AssistantInvestigationValues.AutomaticReuse), Arg.Any<CancellationToken>());
        await store.Received(1).CompleteAsync("orders-production", investigationId,
            "The charge code failed at the application frame.", "ops-model", null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sweep_does_not_call_model_when_another_node_owns_the_fingerprint_lease()
    {
        var (service, store, coordinator, client, _) = CreateNovelFailureService();
        coordinator.TryAcquireEvidenceLeaseAsync("orders-production", Arg.Any<string>(),
                Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(false);

        await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, client.CallCount);
        await store.DidNotReceive().TryStartAsync(Arg.Any<AssistantInvestigationRecord>(),
            Arg.Any<CancellationToken>());
        await coordinator.Received(1).TryAcquireEvidenceLeaseAsync("orders-production",
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sweep_abandons_processing_claim_when_shared_hourly_budget_is_full()
    {
        var (service, store, coordinator, client, _) = CreateNovelFailureService();
        var investigationId = Guid.NewGuid();
        store.TryStartAsync(Arg.Any<AssistantInvestigationRecord>(), Arg.Any<CancellationToken>())
            .Returns(investigationId);
        coordinator.TryReserveModelCallAsync("orders-production", Arg.Any<DateTime>(), 30,
                Arg.Any<CancellationToken>())
            .Returns(false);

        await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, client.CallCount);
        await coordinator.Received(1).AbandonProcessingAsync("orders-production",
            investigationId, Arg.Any<CancellationToken>());
        await coordinator.Received(1).ReleaseEvidenceLeaseAsync("orders-production",
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    private static (AssistantAutoInvestigationService<TimeTickerEntity, CronTickerEntity> Service,
        IAssistantInvestigationStore Store, IAssistantInvestigationCoordinator Coordinator,
        StaticClient Client, Guid ExecutionId) CreateNovelFailureService()
    {
        var executionId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var data = Substitute.For<ITickerDashboardDataService<TimeTickerEntity, CronTickerEntity>>();
        data.GetExecutionsFlatAsync(Arg.Any<ExecutionQueryFilter>(), Arg.Any<CancellationToken>())
            .Returns(new PaginationResult<ExecutionFlatDto>(
            [
                new() { Id = executionId, FunctionName = "Invoices.Charge",
                    Status = TickerStatus.Failed, ExecutedAt = now },
            ], 1, 1, 10));
        data.GetTimeTickerByIdAsync(executionId, Arg.Any<CancellationToken>()).Returns(new TimeTickerFlatDto
        {
            Id = executionId,
            FunctionName = "Invoices.Charge",
            Status = TickerStatus.Failed,
            ExecutedAt = now,
            ExceptionMessage = "Payment provider rejected the request.",
        });
        var store = Substitute.For<IAssistantInvestigationStore>();
        var coordinator = SuccessfulCoordinator();
        var client = new StaticClient("diagnosis");
        var assistantOptions = new AssistantOptionsBuilder()
            .UseChatClient(client)
            .WithModelName("ops-model")
            .ConfigureRunInvestigator(options => options.EnableAutomaticInvestigations(automatic => automatic
                .DetectSuccessfulRecovery(false)));
        var scheduler = new SchedulerOptionsBuilder();
        scheduler.BindRuntimeActivationScope("orders-production", 1, schedulerEnabled: true);
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(assistantOptions)
            .AddSingleton(scheduler)
            .AddSingleton(data)
            .AddSingleton(store)
            .AddSingleton(coordinator)
            .AddSingleton(sp => new TickerAssistantService(assistantOptions, sp))
            .BuildServiceProvider();
        var service = new AssistantAutoInvestigationService<TimeTickerEntity, CronTickerEntity>(
            services.GetRequiredService<IServiceScopeFactory>(), assistantOptions,
            services.GetRequiredService<ILogger<AssistantAutoInvestigationService<TimeTickerEntity, CronTickerEntity>>>());
        return (service, store, coordinator, client, executionId);
    }

    private static IAssistantInvestigationCoordinator SuccessfulCoordinator()
    {
        var coordinator = Substitute.For<IAssistantInvestigationCoordinator>();
        coordinator.TryAcquireEvidenceLeaseAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(true);
        coordinator.RenewEvidenceLeaseAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(true);
        coordinator.TryReserveModelCallAsync(Arg.Any<string>(), Arg.Any<DateTime>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(true);
        return coordinator;
    }

    private sealed class StaticClient(string response) : IChatClient
    {
        public int CallCount { get; private set; }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            CallCount++;
            yield return new ChatResponseUpdate(ChatRole.Assistant, response);
            await Task.CompletedTask;
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
