using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Infrastructure;
using TickerQ.Utilities.Integrations;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Dashboard.Assistant;

internal sealed class AssistantInvestigationRunner<TTimeTicker, TCronTicker>
    where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    where TCronTicker : CronTickerEntity, new()
{
    private readonly TickerAssistantService _assistant;
    private readonly ITickerDashboardDataService<TTimeTicker, TCronTicker> _data;
    private readonly IAssistantInvestigationStore _store;
    private readonly IServiceProvider _services;

    public AssistantInvestigationRunner(TickerAssistantService assistant,
        ITickerDashboardDataService<TTimeTicker, TCronTicker> data,
        IAssistantInvestigationStore store,
        IServiceProvider services)
    {
        _assistant = assistant;
        _data = data;
        _store = store;
        _services = services;
    }

    public async Task<bool> RunAsync(Guid executionId, string trigger,
        string? userId, CancellationToken cancellationToken)
    {
        var applicationNamespace = AssistantInvestigationFactory.ResolveNamespace(
            _services.GetService(typeof(SchedulerOptionsBuilder)) as SchedulerOptionsBuilder);
        var seed = await AssistantInvestigationFactory.CreateAsync(_data, executionId,
            applicationNamespace, trigger, userId, cancellationToken);
        if (seed == null) return false;

        AssistantInvestigationRecord? reusable = null;
        if (string.Equals(trigger, AssistantInvestigationValues.Automatic, StringComparison.Ordinal))
        {
            reusable = await _store.FindReusableAsync(applicationNamespace,
                seed.EvidenceFingerprint, seed.ExecutionId, cancellationToken);
            if (reusable != null)
                seed.Trigger = AssistantInvestigationValues.AutomaticReuse;
        }

        if (reusable != null)
        {
            await SaveReusableAsync(seed, reusable, applicationNamespace, cancellationToken);
            return false;
        }

        var automatic = string.Equals(trigger, AssistantInvestigationValues.Automatic,
            StringComparison.Ordinal);
        if (!automatic)
        {
            var manualId = await _store.TryStartAsync(seed, cancellationToken);
            return manualId.HasValue && await InvokeModelAsync(executionId,
                applicationNamespace, manualId.Value, cancellationToken, cancellationToken);
        }

        var coordinator = _services.GetService(typeof(IAssistantInvestigationCoordinator))
            as IAssistantInvestigationCoordinator
            ?? throw new InvalidOperationException(
                "Automatic AI investigations require an IAssistantInvestigationCoordinator.");
        var automaticOptions = _assistant.Options.RunInvestigator.Automatic;
        var ownerToken = Guid.NewGuid();
        var leaseDuration = automaticOptions.CoordinationLease;
        if (!await coordinator.TryAcquireEvidenceLeaseAsync(applicationNamespace,
                seed.EvidenceFingerprint, ownerToken, DateTime.UtcNow + leaseDuration,
                cancellationToken))
            return false;

        try
        {
            // A process can terminate after claiming the execution but before completing it.
            // Once its renewable evidence lease has expired, remove only that stale processing
            // row so this new exclusive owner can recover the execution.
            await coordinator.AbandonStaleProcessingAsync(applicationNamespace,
                seed.ExecutionId, DateTime.UtcNow - leaseDuration, cancellationToken);

            // The other node may have completed the same failure pattern immediately before
            // this node obtained the lease. Recheck inside the exclusive section before spending.
            reusable = await _store.FindReusableAsync(applicationNamespace,
                seed.EvidenceFingerprint, seed.ExecutionId, cancellationToken);
            if (reusable != null)
            {
                seed.Trigger = AssistantInvestigationValues.AutomaticReuse;
                await SaveReusableAsync(seed, reusable, applicationNamespace, cancellationToken);
                return false;
            }

            var investigationId = await _store.TryStartAsync(seed, cancellationToken);
            if (!investigationId.HasValue) return false;

            // Reserve before contacting the provider. If the shared budget is full, remove only
            // this untouched processing row so a future sweep can retry after the window moves.
            if (!await coordinator.TryReserveModelCallAsync(applicationNamespace, DateTime.UtcNow,
                    automaticOptions.MaxInvestigationsPerHour, cancellationToken))
            {
                await coordinator.AbandonProcessingAsync(applicationNamespace,
                    investigationId.Value, cancellationToken);
                return false;
            }

            using var modelCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var renewalStop = new CancellationTokenSource();
            var renewal = RenewLeaseAsync(
                _services.GetRequiredService<IServiceScopeFactory>(), applicationNamespace,
                seed.EvidenceFingerprint, ownerToken, leaseDuration, renewalStop.Token,
                modelCancellation);
            try
            {
                return await InvokeModelAsync(executionId, applicationNamespace,
                    investigationId.Value, modelCancellation.Token, cancellationToken);
            }
            finally
            {
                renewalStop.Cancel();
                try { await renewal; }
                catch (OperationCanceledException) when (renewalStop.IsCancellationRequested) { }
            }
        }
        finally
        {
            // A failed best-effort release cannot cause another concurrent owner: the lease
            // remains exclusive until its expiry and is then recovered by conditional update.
            try
            {
                await coordinator.ReleaseEvidenceLeaseAsync(applicationNamespace,
                    seed.EvidenceFingerprint, ownerToken, CancellationToken.None);
            }
            catch
            {
                // The renewable lease supplies crash and transient release recovery.
            }
        }
    }

    private async Task SaveReusableAsync(AssistantInvestigationRecord seed,
        AssistantInvestigationRecord reusable, string applicationNamespace,
        CancellationToken cancellationToken)
    {
        seed.Trigger = AssistantInvestigationValues.AutomaticReuse;
        var investigationId = await _store.TryStartAsync(seed, cancellationToken);
        if (!investigationId.HasValue) return;
        await _store.CompleteAsync(applicationNamespace, investigationId.Value,
            reusable.Summary, reusable.Model, cancellationToken: cancellationToken);
    }

    private async Task<bool> InvokeModelAsync(Guid executionId, string applicationNamespace,
        Guid investigationId, CancellationToken modelCancellationToken,
        CancellationToken requestCancellationToken = default)
    {

        try
        {
            var prompt = $"Investigate execution {executionId}. Use the run investigator first, " +
                "separate confirmed evidence from likely causes, and recommend the safest next action.";
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, _assistant.Options.SystemPrompt),
                new(ChatRole.User, prompt),
            };
            Func<Guid, CancellationToken, Task<ExecutionLogReadResult>>? readLogs =
                _services.GetService(typeof(ExecutionLogReader)) is ExecutionLogReader reader
                    ? reader.ReadAsync
                    : null;
            var integrations = _services.GetService(typeof(ITickerIntegrationAdminService))
                as ITickerIntegrationAdminService;
            var diagnostics = _services.GetService(typeof(TickerQRuntimeDiagnostics)) as TickerQRuntimeDiagnostics;
            var scheduler = _services.GetService(typeof(SchedulerOptionsBuilder)) as SchedulerOptionsBuilder;
            var chatOptions = new ChatOptions
            {
                Tools = TickerAssistantTools.Build(_data, allowProposals: false, emitProposal: null,
                    modelCancellationToken, readLogs, integrations,
                    scheduler?.SchedulerTimeZone.Id ?? "UTC", diagnostics, false,
                    _assistant.Options.RunInvestigator, _assistant.SourceContextProvider),
                MaxOutputTokens = 1500,
            };

            var text = new StringBuilder();
            await foreach (var update in _assistant.Client.GetStreamingResponseAsync(
                messages, chatOptions, modelCancellationToken))
                if (!string.IsNullOrEmpty(update.Text)) text.Append(update.Text);
            if (text.Length == 0) throw new InvalidOperationException("The AI provider returned an empty investigation.");
            await _store.CompleteAsync(applicationNamespace, investigationId,
                text.ToString(), _assistant.Options.ModelName,
                cancellationToken: modelCancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (requestCancellationToken.IsCancellationRequested)
        {
            await _store.FailAsync(applicationNamespace, investigationId,
                "The automatic investigation was cancelled.", CancellationToken.None);
            throw;
        }
        catch
        {
            await _store.FailAsync(applicationNamespace, investigationId,
                "The AI provider did not complete this investigation.", CancellationToken.None);
            throw;
        }
    }

    private static async Task RenewLeaseAsync(IServiceScopeFactory scopes,
        string applicationNamespace, string evidenceFingerprint, Guid ownerToken,
        TimeSpan leaseDuration, CancellationToken stopToken,
        CancellationTokenSource modelCancellation)
    {
        var interval = TimeSpan.FromTicks(Math.Max(TimeSpan.FromSeconds(20).Ticks,
            leaseDuration.Ticks / 3));
        while (!stopToken.IsCancellationRequested)
        {
            await Task.Delay(interval, stopToken);
            try
            {
                // Renewal runs concurrently with model streaming. Resolve it in an independent
                // scope so its EF store never shares a DbContext with the investigation record.
                using var scope = scopes.CreateScope();
                var coordinator = scope.ServiceProvider
                    .GetRequiredService<IAssistantInvestigationCoordinator>();
                if (await coordinator.RenewEvidenceLeaseAsync(applicationNamespace,
                        evidenceFingerprint, ownerToken, DateTime.UtcNow + leaseDuration,
                        stopToken))
                    continue;
            }
            catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Losing coordination is safer than continuing an unprotected model call.
            }

            modelCancellation.Cancel();
            return;
        }
    }
}
