using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.Dashboard.Infrastructure;

internal sealed record DashboardTestRunStepMetadata(string? Description);

internal sealed class DashboardTestRunSession : IDisposable
{
    private int _completed;

    public DashboardTestRunSession(
        InternalFunctionContext root,
        IReadOnlyDictionary<Guid, DashboardTestRunStepMetadata> metadata)
    {
        Root = root;
        Metadata = metadata;
        StartedAtUtc = DateTime.UtcNow;
        Cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    }

    public Guid Id => Root.TickerId;
    public InternalFunctionContext Root { get; }
    public IReadOnlyDictionary<Guid, DashboardTestRunStepMetadata> Metadata { get; }
    public DateTime StartedAtUtc { get; }
    public DateTime? FinishedAtUtc { get; private set; }
    public string? Error { get; private set; }
    public bool Completed => Volatile.Read(ref _completed) != 0;
    public CancellationTokenSource Cancellation { get; }

    public void Finish(string? error)
    {
        Error = error;
        FinishedAtUtc = DateTime.UtcNow;
        Volatile.Write(ref _completed, 1);
    }

    public void Dispose() => Cancellation.Dispose();
}

/// <summary>
/// Bounded memory-only ownership for dashboard chain tests. Sessions retain the temporary
/// execution graph long enough for the UI to animate it and read log tails, but never enter
/// a ticker provider or the durable execution-log writer.
/// </summary>
internal sealed class DashboardTestRunRegistry : IDisposable
{
    private const int MaxSessions = 32;
    private static readonly TimeSpan CompletedRetention = TimeSpan.FromMinutes(10);
    private readonly object _gate = new();
    private readonly Dictionary<Guid, DashboardTestRunSession> _sessions = new();

    public bool TryCreate(
        InternalFunctionContext root,
        IReadOnlyDictionary<Guid, DashboardTestRunStepMetadata> metadata,
        out DashboardTestRunSession session)
    {
        lock (_gate)
        {
            PruneLocked();
            if (_sessions.Count >= MaxSessions)
            {
                var completed = _sessions.Values
                    .Where(item => item.Completed)
                    .OrderBy(item => item.FinishedAtUtc)
                    .FirstOrDefault();
                if (completed == null)
                {
                    session = null!;
                    return false;
                }
                RemoveLocked(completed.Id);
            }

            session = new DashboardTestRunSession(root, metadata);
            _sessions.Add(session.Id, session);
            return true;
        }
    }

    public bool TryGet(Guid id, out DashboardTestRunSession session)
    {
        lock (_gate)
        {
            PruneLocked();
            return _sessions.TryGetValue(id, out session!);
        }
    }

    public void Start(DashboardTestRunSession session, ITickerExecutionTaskHandler handler)
    {
        _ = Task.Run(async () =>
        {
            string? error = null;
            try
            {
                await handler.ExecuteTestChainAsync(session.Root, session.Cancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested)
            {
                error = "The test chain exceeded its five-minute execution limit.";
                if (session.Root.Status is TickerStatus.Idle or TickerStatus.Queued or TickerStatus.InProgress)
                {
                    session.Root.Status = TickerStatus.Cancelled;
                    session.Root.ExceptionDetails = error;
                    session.Root.ExecutedAt = DateTime.UtcNow;
                }
            }
            catch (Exception exception)
            {
                error = exception.Message;
                if (session.Root.Status is TickerStatus.Idle or TickerStatus.Queued or TickerStatus.InProgress)
                {
                    session.Root.Status = TickerStatus.Failed;
                    session.Root.ExceptionDetails = error;
                    session.Root.ExecutedAt = DateTime.UtcNow;
                }
            }
            finally
            {
                session.Finish(error);
            }
        });
    }

    private void PruneLocked()
    {
        var threshold = DateTime.UtcNow - CompletedRetention;
        foreach (var id in _sessions.Values
                     .Where(item => item.Completed && item.FinishedAtUtc < threshold)
                     .Select(item => item.Id)
                     .ToArray())
            RemoveLocked(id);
    }

    private void RemoveLocked(Guid id)
    {
        if (_sessions.Remove(id, out var removed)) removed.Dispose();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var session in _sessions.Values)
            {
                if (!session.Completed)
                {
                    // The execution still observes this source while it unwinds. Disposing it
                    // here can race with linked-token registration in the function handler.
                    session.Cancellation.Cancel();
                    continue;
                }
                session.Dispose();
            }
            _sessions.Clear();
        }
    }
}
