using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TickerQ.EntityFrameworkCore.Entities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Infrastructure
{
    internal sealed class EfAssistantInvestigationStore<TContext> :
        IAssistantInvestigationStore,
        IAssistantInvestigationCoordinator
        where TContext : DbContext
    {
        private readonly TContext _db;
        private readonly AssistantHistoryOptions _options;

        public EfAssistantInvestigationStore(TContext db, AssistantHistoryOptions options)
        {
            _db = db;
            _options = options;
        }

        private DbSet<AssistantInvestigationEntity> Investigations
            => _db.Set<AssistantInvestigationEntity>();

        private DbSet<AssistantInvestigationLeaseEntity> Leases
            => _db.Set<AssistantInvestigationLeaseEntity>();

        private DbSet<AssistantInvestigationBudgetEntity> Budgets
            => _db.Set<AssistantInvestigationBudgetEntity>();

        public async Task<Guid?> TryStartAsync(
            AssistantInvestigationRecord investigation,
            CancellationToken cancellationToken = default)
        {
            if (investigation == null) throw new ArgumentNullException(nameof(investigation));
            if (string.IsNullOrWhiteSpace(investigation.ApplicationNamespace))
                throw new ArgumentException("Application namespace is required.", nameof(investigation));
            if (investigation.ExecutionId == Guid.Empty)
                throw new ArgumentException("Execution id is required.", nameof(investigation));

            // Most duplicate attempts come from later automatic sweeps, not a race. Avoid using
            // a database constraint violation as the normal control path (and avoid its error log).
            // The unique index and the catch below remain the final guard for concurrent nodes.
            if (await Investigations.AsNoTracking().ExistsAsync(x =>
                x.ApplicationNamespace == investigation.ApplicationNamespace &&
                x.ExecutionId == investigation.ExecutionId, cancellationToken))
                return null;

            var now = DateTime.UtcNow;
            var entity = new AssistantInvestigationEntity
            {
                Id = investigation.Id == Guid.Empty ? Guid.NewGuid() : investigation.Id,
                ApplicationNamespace = investigation.ApplicationNamespace,
                ExecutionId = investigation.ExecutionId,
                ExecutionType = RequiredText(investigation.ExecutionType),
                ChainRootId = investigation.ChainRootId,
                FunctionName = RequiredText(investigation.FunctionName),
                ExecutionStatus = RequiredText(investigation.ExecutionStatus),
                Classification = RequiredText(investigation.Classification, "Unclassified"),
                Summary = "",
                Model = investigation.Model ?? "",
                Trigger = RequiredText(investigation.Trigger, AssistantInvestigationValues.User),
                State = AssistantInvestigationValues.Processing,
                ReviewState = AssistantInvestigationValues.New,
                EvidenceFingerprint = RequiredText(investigation.EvidenceFingerprint),
                SourceRepository = investigation.SourceRepository ?? "",
                SourceRevision = investigation.SourceRevision ?? "",
                SourcePath = investigation.SourcePath ?? "",
                SourceLine = investigation.SourceLine,
                ConversationId = investigation.ConversationId,
                StartedByUserId = investigation.StartedByUserId ?? "",
                ReviewedByUserId = "",
                OccurredAt = investigation.OccurredAt == default ? now : investigation.OccurredAt,
                CreatedAt = now,
                UpdatedAt = now,
                FailureMessage = "",
            };

            Investigations.Add(entity);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _db.Entry(entity).State = EntityState.Detached;
                if (await Investigations.AsNoTracking().ExistsAsync(x =>
                    x.ApplicationNamespace == entity.ApplicationNamespace &&
                    x.ExecutionId == entity.ExecutionId, cancellationToken))
                    return null;
                throw;
            }

            await PruneAsync(entity.ApplicationNamespace, cancellationToken);
            return entity.Id;
        }

        public async Task<bool> TryAcquireEvidenceLeaseAsync(string applicationNamespace,
            string evidenceFingerprint, Guid ownerToken, DateTime leaseUntilUtc,
            CancellationToken cancellationToken = default)
        {
            ValidateCoordinationKey(applicationNamespace, evidenceFingerprint, ownerToken);
            var now = DateTime.UtcNow;
            var leaseUntil = NormalizeUtc(leaseUntilUtc);
            if (leaseUntil <= now)
                throw new ArgumentOutOfRangeException(nameof(leaseUntilUtc), "Lease expiry must be in the future.");

            // An expired owner can be replaced with one conditional write. Only one contender
            // can update the same expired row because the first write also advances LeaseUntil.
            var replaced = await Leases.Where(x =>
                    x.ApplicationNamespace == applicationNamespace &&
                    x.EvidenceFingerprint == evidenceFingerprint &&
                    x.LeaseUntil <= now)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.OwnerToken, ownerToken)
                    .SetProperty(x => x.LeaseUntil, leaseUntil)
                    .SetProperty(x => x.UpdatedAt, now), cancellationToken);
            if (replaced == 1) return true;

            if (await Leases.AsNoTracking().ExistsAsync(x =>
                    x.ApplicationNamespace == applicationNamespace &&
                    x.EvidenceFingerprint == evidenceFingerprint, cancellationToken))
                return false;

            var entity = new AssistantInvestigationLeaseEntity
            {
                ApplicationNamespace = applicationNamespace,
                EvidenceFingerprint = evidenceFingerprint,
                OwnerToken = ownerToken,
                LeaseUntil = leaseUntil,
                UpdatedAt = now,
            };
            Leases.Add(entity);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                _db.Entry(entity).State = EntityState.Detached;
                return true;
            }
            catch (DbUpdateException)
            {
                _db.Entry(entity).State = EntityState.Detached;
                if (await Leases.AsNoTracking().ExistsAsync(x =>
                        x.ApplicationNamespace == applicationNamespace &&
                        x.EvidenceFingerprint == evidenceFingerprint, cancellationToken))
                    return false;
                throw;
            }
        }

        public async Task<bool> RenewEvidenceLeaseAsync(string applicationNamespace,
            string evidenceFingerprint, Guid ownerToken, DateTime leaseUntilUtc,
            CancellationToken cancellationToken = default)
        {
            ValidateCoordinationKey(applicationNamespace, evidenceFingerprint, ownerToken);
            var now = DateTime.UtcNow;
            var leaseUntil = NormalizeUtc(leaseUntilUtc);
            if (leaseUntil <= now)
                throw new ArgumentOutOfRangeException(nameof(leaseUntilUtc), "Lease expiry must be in the future.");

            return await Leases.Where(x =>
                    x.ApplicationNamespace == applicationNamespace &&
                    x.EvidenceFingerprint == evidenceFingerprint &&
                    x.OwnerToken == ownerToken)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.LeaseUntil, leaseUntil)
                    .SetProperty(x => x.UpdatedAt, now), cancellationToken) == 1;
        }

        public async Task ReleaseEvidenceLeaseAsync(string applicationNamespace,
            string evidenceFingerprint, Guid ownerToken,
            CancellationToken cancellationToken = default)
        {
            ValidateCoordinationKey(applicationNamespace, evidenceFingerprint, ownerToken);
            await Leases.Where(x =>
                    x.ApplicationNamespace == applicationNamespace &&
                    x.EvidenceFingerprint == evidenceFingerprint &&
                    x.OwnerToken == ownerToken)
                .ExecuteDeleteAsync(cancellationToken);
        }

        public async Task<bool> TryReserveModelCallAsync(string applicationNamespace,
            DateTime startedAtUtc, int maximumPerHour,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(applicationNamespace))
                throw new ArgumentException("Application namespace is required.", nameof(applicationNamespace));
            if (maximumPerHour is < 1 or > 500)
                throw new ArgumentOutOfRangeException(nameof(maximumPerHour));

            var startedAt = NormalizeUtc(startedAtUtc);
            var cutoffTicks = startedAt.AddHours(-1).Ticks;
            for (var attempt = 0; attempt < 32; attempt++)
            {
                var row = await Budgets.AsNoTracking().SingleOrDefaultAsync(x =>
                    x.ApplicationNamespace == applicationNamespace, cancellationToken);
                if (row == null)
                {
                    var created = new AssistantInvestigationBudgetEntity
                    {
                        ApplicationNamespace = applicationNamespace,
                        StartedAtTicks = startedAt.Ticks.ToString(),
                        Version = 1,
                        UpdatedAt = startedAt,
                    };
                    Budgets.Add(created);
                    try
                    {
                        await _db.SaveChangesAsync(cancellationToken);
                        _db.Entry(created).State = EntityState.Detached;
                        return true;
                    }
                    catch (DbUpdateException)
                    {
                        _db.Entry(created).State = EntityState.Detached;
                        if (!await Budgets.AsNoTracking().ExistsAsync(x =>
                                x.ApplicationNamespace == applicationNamespace, cancellationToken))
                            throw;
                        await Task.Yield();
                        continue;
                    }
                }

                var starts = ParseStartedAtTicks(row.StartedAtTicks, cutoffTicks);
                if (starts.Count >= maximumPerHour) return false;
                starts.Add(startedAt.Ticks);
                var serialized = string.Join(",", starts);
                var updated = await Budgets.Where(x =>
                        x.ApplicationNamespace == applicationNamespace &&
                        x.Version == row.Version)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.StartedAtTicks, serialized)
                        .SetProperty(x => x.Version, row.Version + 1)
                        .SetProperty(x => x.UpdatedAt, startedAt), cancellationToken);
                if (updated == 1) return true;
                await Task.Yield();
            }

            throw new InvalidOperationException(
                "Could not reserve the automatic investigation budget after concurrent updates.");
        }

        public async Task AbandonProcessingAsync(string applicationNamespace, Guid investigationId,
            CancellationToken cancellationToken = default)
        {
            await Investigations.Where(x =>
                    x.ApplicationNamespace == applicationNamespace &&
                    x.Id == investigationId &&
                    x.State == AssistantInvestigationValues.Processing)
                .ExecuteDeleteAsync(cancellationToken);
        }

        public async Task AbandonStaleProcessingAsync(string applicationNamespace, Guid executionId,
            DateTime notUpdatedSinceUtc, CancellationToken cancellationToken = default)
        {
            var cutoff = NormalizeUtc(notUpdatedSinceUtc);
            await Investigations.Where(x =>
                    x.ApplicationNamespace == applicationNamespace &&
                    x.ExecutionId == executionId &&
                    x.State == AssistantInvestigationValues.Processing &&
                    x.UpdatedAt <= cutoff)
                .ExecuteDeleteAsync(cancellationToken);
        }

        public async Task<AssistantInvestigationRecord> FindReusableAsync(
            string applicationNamespace,
            string evidenceFingerprint,
            Guid executionId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(applicationNamespace) ||
                string.IsNullOrWhiteSpace(evidenceFingerprint))
                return null;

            var row = await Investigations.AsNoTracking()
                .Where(x => x.ApplicationNamespace == applicationNamespace &&
                    x.ExecutionId != executionId &&
                    x.EvidenceFingerprint == evidenceFingerprint &&
                    x.State == AssistantInvestigationValues.Completed &&
                    // Oracle stores an empty string as NULL, so "not empty" must also exclude NULL.
                    x.Summary != null && x.Summary != "")
                .OrderByDescending(x => x.CompletedAt)
                .ThenByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            return row == null ? null : Map(row);
        }

        public async Task CompleteAsync(string applicationNamespace, Guid investigationId,
            string summary, string model, Guid? conversationId = null,
            CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            await Investigations.Where(x =>
                    x.ApplicationNamespace == applicationNamespace &&
                    x.Id == investigationId &&
                    x.State != AssistantInvestigationValues.Dismissed)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Summary, summary ?? "")
                    .SetProperty(x => x.Model, model ?? "")
                    .SetProperty(x => x.ConversationId, x => conversationId ?? x.ConversationId)
                    .SetProperty(x => x.State, AssistantInvestigationValues.Completed)
                    .SetProperty(x => x.CompletedAt, now)
                    .SetProperty(x => x.UpdatedAt, now)
                    .SetProperty(x => x.FailureMessage, ""), cancellationToken);
        }

        public async Task FailAsync(string applicationNamespace, Guid investigationId,
            string failureMessage, CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            var limitedFailure = Limit(failureMessage, 2000);
            await Investigations.Where(x =>
                    x.ApplicationNamespace == applicationNamespace &&
                    x.Id == investigationId &&
                    x.State != AssistantInvestigationValues.Dismissed)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.State, AssistantInvestigationValues.Failed)
                    .SetProperty(x => x.FailureMessage, limitedFailure)
                    .SetProperty(x => x.UpdatedAt, now), cancellationToken);
        }

        public async Task<AssistantInvestigationPage> ListAsync(string applicationNamespace,
            AssistantInvestigationQuery query, CancellationToken cancellationToken = default)
        {
            query ??= new AssistantInvestigationQuery();
            var page = Math.Max(1, query.PageNumber);
            var size = Math.Clamp(query.PageSize, 1, 100);
            var rows = VisibleInvestigations(applicationNamespace);
            if (!string.IsNullOrWhiteSpace(query.State)) rows = rows.Where(x => x.State == query.State);
            if (!string.IsNullOrWhiteSpace(query.ReviewState)) rows = rows.Where(x => x.ReviewState == query.ReviewState);
            if (!string.IsNullOrWhiteSpace(query.Classification)) rows = rows.Where(x => x.Classification == query.Classification);
            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var search = query.Search.Trim();
                rows = rows.Where(x => x.FunctionName.Contains(search) || x.ExecutionId.ToString().Contains(search));
            }

            if (query.GroupByFunction)
            {
                var groupedRows = rows.GroupBy(x => x.FunctionName)
                    .Select(group => new
                    {
                        FunctionName = group.Key,
                        LatestCreatedAt = group.Max(x => x.CreatedAt),
                    });
                var groupCount = await groupedRows.CountAsync(cancellationToken);
                var functionNames = await groupedRows
                    .OrderByDescending(group => group.LatestCreatedAt)
                    .ThenBy(group => group.FunctionName)
                    .Skip((page - 1) * size)
                    .Take(size)
                    .Select(group => group.FunctionName)
                    .ToListAsync(cancellationToken);
                var groupOrder = functionNames
                    .Select((functionName, index) => new { functionName, index })
                    .ToDictionary(item => item.functionName, item => item.index, StringComparer.Ordinal);
                var groupedEntities = functionNames.Count == 0
                    ? []
                    : await rows.Where(EfPredicateBuilder.Contains<AssistantInvestigationEntity, string>(
                            functionNames, x => x.FunctionName))
                        .ToListAsync(cancellationToken);
                var orderedEntities = groupedEntities
                    .OrderBy(x => groupOrder[x.FunctionName])
                    .ThenByDescending(x => x.CreatedAt)
                    .ThenByDescending(x => x.Id)
                    .Select(Map)
                    .ToArray();

                return new AssistantInvestigationPage
                {
                    Items = orderedEntities,
                    TotalCount = groupCount,
                    PageNumber = page,
                    PageSize = size,
                };
            }

            var count = await rows.CountAsync(cancellationToken);
            var entities = await rows.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
                .Skip((page - 1) * size).Take(size).ToListAsync(cancellationToken);
            return new AssistantInvestigationPage
            {
                Items = entities.Select(Map).ToArray(),
                TotalCount = count,
                PageNumber = page,
                PageSize = size,
            };
        }

        public async Task<AssistantInvestigationRecord> GetAsync(string applicationNamespace,
            Guid investigationId, CancellationToken cancellationToken = default)
        {
            var row = await Investigations.AsNoTracking().FirstOrDefaultAsync(x =>
                x.ApplicationNamespace == applicationNamespace && x.Id == investigationId &&
                x.State != AssistantInvestigationValues.Dismissed, cancellationToken);
            return row == null ? null : Map(row);
        }

        public async Task<AssistantInvestigationOverview> GetOverviewAsync(string applicationNamespace,
            CancellationToken cancellationToken = default)
        {
            var rows = VisibleInvestigations(applicationNamespace);
            var today = DateTime.UtcNow.Date;
            var recent = await rows.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
                .Take(8).ToListAsync(cancellationToken);
            var patternRows = await rows.Where(x => x.CreatedAt >= DateTime.UtcNow.AddDays(-7))
                .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
                .Take(250).ToListAsync(cancellationToken);
            var patterns = patternRows
                .GroupBy(x => new { x.FunctionName, x.Classification })
                .Select(group => new AssistantInvestigationPattern
                {
                    FunctionName = group.Key.FunctionName,
                    Classification = group.Key.Classification,
                    Count = group.Count(),
                    LatestAt = group.Max(x => x.CreatedAt),
                })
                .Where(x => x.Count > 1)
                .OrderByDescending(x => x.Count).ThenByDescending(x => x.LatestAt).Take(5).ToArray();

            return new AssistantInvestigationOverview
            {
                Total = await rows.CountAsync(cancellationToken),
                AwaitingReview = await rows.CountAsync(x => x.State == AssistantInvestigationValues.Completed &&
                    x.ReviewState == AssistantInvestigationValues.New, cancellationToken),
                CompletedToday = await rows.CountAsync(x => x.CompletedAt >= today, cancellationToken),
                Recovered = await rows.CountAsync(x => x.RecoveredAt != null, cancellationToken),
                Processing = await rows.CountAsync(x => x.State == AssistantInvestigationValues.Processing, cancellationToken),
                Recent = recent.Select(Map).ToArray(),
                Patterns = patterns,
            };
        }

        public async Task<bool> SetReviewStateAsync(string applicationNamespace, Guid investigationId,
            string reviewState, string reviewedByUserId, CancellationToken cancellationToken = default)
        {
            if (reviewState is not (AssistantInvestigationValues.New or AssistantInvestigationValues.Reviewed or AssistantInvestigationValues.Resolved))
                throw new ArgumentOutOfRangeException(nameof(reviewState));
            var now = DateTime.UtcNow;
            var reviewedAt = reviewState == AssistantInvestigationValues.New ? (DateTime?)null : now;
            var updated = await Investigations.Where(x =>
                    x.ApplicationNamespace == applicationNamespace &&
                    x.Id == investigationId &&
                    x.State != AssistantInvestigationValues.Dismissed)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.ReviewState, reviewState)
                    .SetProperty(x => x.ReviewedByUserId, reviewedByUserId ?? "")
                    .SetProperty(x => x.ReviewedAt, reviewedAt)
                    .SetProperty(x => x.UpdatedAt, now), cancellationToken);
            return updated > 0;
        }

        public async Task<bool> DeleteAsync(string applicationNamespace, Guid investigationId,
            CancellationToken cancellationToken = default)
        {
            var deleted = await DismissAsync(Investigations.Where(x =>
                x.ApplicationNamespace == applicationNamespace &&
                x.Id == investigationId &&
                x.State != AssistantInvestigationValues.Dismissed), cancellationToken);
            return deleted > 0;
        }

        public async Task<int> DeleteByFunctionAsync(string applicationNamespace, string functionName,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(functionName)) return 0;
            return await DismissAsync(Investigations.Where(x =>
                    x.ApplicationNamespace == applicationNamespace &&
                    x.FunctionName == functionName &&
                    x.State != AssistantInvestigationValues.Dismissed), cancellationToken);
        }

        public async Task<int> MarkRecoveredAsync(string applicationNamespace, string functionName,
            Guid successfulExecutionId, DateTime successfulAt, CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            return await Investigations.Where(x =>
                    x.ApplicationNamespace == applicationNamespace &&
                    x.FunctionName == functionName &&
                    x.OccurredAt < successfulAt &&
                    x.RecoveredAt == null &&
                    x.State == AssistantInvestigationValues.Completed)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.RecoveredByExecutionId, successfulExecutionId)
                    .SetProperty(x => x.RecoveredAt, successfulAt)
                    .SetProperty(x => x.UpdatedAt, now), cancellationToken);
        }

        private IQueryable<AssistantInvestigationEntity> VisibleInvestigations(string applicationNamespace)
            => Investigations.AsNoTracking().Where(x =>
                x.ApplicationNamespace == applicationNamespace &&
                x.State != AssistantInvestigationValues.Dismissed);

        private async Task PruneAsync(string applicationNamespace, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var cutoff = now - (_options.InvestigationRetention <= TimeSpan.Zero
                ? TimeSpan.FromDays(30) : _options.InvestigationRetention);
            var expirable = Investigations.Where(x => x.ApplicationNamespace == applicationNamespace &&
                x.State != AssistantInvestigationValues.Processing &&
                x.State != AssistantInvestigationValues.Dismissed);
            var readRetention = _options.ReadInvestigationRetention;
            var resolvedRetention = _options.ResolvedInvestigationRetention;
            var readRetentionEnabled = readRetention.HasValue && readRetention.Value > TimeSpan.Zero;
            var resolvedRetentionEnabled = resolvedRetention.HasValue &&
                resolvedRetention.Value > TimeSpan.Zero;

            if (readRetentionEnabled)
            {
                var readCutoff = now - readRetention.GetValueOrDefault();
                await expirable.Where(x =>
                        x.ReviewState == AssistantInvestigationValues.Reviewed &&
                        x.ReviewedAt != null && x.ReviewedAt < readCutoff)
                    .ExecuteDeleteAsync(cancellationToken);
            }

            if (resolvedRetentionEnabled)
            {
                var resolvedCutoff = now - resolvedRetention.GetValueOrDefault();
                await expirable.Where(x =>
                        x.ReviewState == AssistantInvestigationValues.Resolved &&
                        x.ReviewedAt != null && x.ReviewedAt < resolvedCutoff)
                    .ExecuteDeleteAsync(cancellationToken);
            }

            var generallyExpirable = expirable;
            if (readRetentionEnabled)
                generallyExpirable = generallyExpirable.Where(x =>
                    x.ReviewState != AssistantInvestigationValues.Reviewed || x.ReviewedAt == null);
            if (resolvedRetentionEnabled)
                generallyExpirable = generallyExpirable.Where(x =>
                    x.ReviewState != AssistantInvestigationValues.Resolved || x.ReviewedAt == null);

            await generallyExpirable.Where(x => x.CreatedAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken);

            // Automatic investigation lookback is capped at one day. Keep deletion tombstones
            // beyond that entire window even when visible-history retention is configured lower.
            // This prevents an old failure from returning on the next scan after its diagnosis
            // was deleted, without retaining tombstones indefinitely.
            var tombstoneRetention = _options.InvestigationRetention > TimeSpan.FromDays(2)
                ? _options.InvestigationRetention
                : TimeSpan.FromDays(2);
            var tombstoneCutoff = now - tombstoneRetention;
            await Investigations.Where(x => x.ApplicationNamespace == applicationNamespace &&
                x.CreatedAt < tombstoneCutoff && x.State == AssistantInvestigationValues.Dismissed)
                .ExecuteDeleteAsync(cancellationToken);

            var keep = Math.Max(1, _options.MaxInvestigationsPerNamespace);
            var excess = await Investigations.Where(x => x.ApplicationNamespace == applicationNamespace &&
                    x.State != AssistantInvestigationValues.Processing &&
                    x.State != AssistantInvestigationValues.Dismissed)
                .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
                .Skip(keep).Select(x => x.Id)
                .ToListAsync(cancellationToken);
            if (excess.Count > 0)
                await Investigations
                    .Where(EfPredicateBuilder.Contains<AssistantInvestigationEntity, Guid>(excess, x => x.Id))
                    .ExecuteDeleteAsync(cancellationToken);
        }

        /// <summary>
        /// Stored in required text columns when a value is missing or redacted. Oracle treats an
        /// empty string as NULL, so required columns cannot hold "".
        /// </summary>
        internal const string MissingText = "-";

        private static string RequiredText(string value, string fallback = MissingText)
            => string.IsNullOrEmpty(value) ? fallback : value;

        private static Task<int> DismissAsync(IQueryable<AssistantInvestigationEntity> rows,
            CancellationToken cancellationToken)
        {
            // Preserve only the namespace/execution claim and timestamps. The diagnosis and
            // source evidence are removed even though the internal tombstone remains. Required
            // columns get MissingText; tombstones are never returned to callers.
            var now = DateTime.UtcNow;
            return rows.ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ExecutionType, MissingText)
                .SetProperty(x => x.ChainRootId, (Guid?)null)
                .SetProperty(x => x.FunctionName, MissingText)
                .SetProperty(x => x.ExecutionStatus, MissingText)
                .SetProperty(x => x.Classification, MissingText)
                .SetProperty(x => x.Summary, "")
                .SetProperty(x => x.Model, "")
                .SetProperty(x => x.Trigger, MissingText)
                .SetProperty(x => x.State, AssistantInvestigationValues.Dismissed)
                .SetProperty(x => x.ReviewState, MissingText)
                .SetProperty(x => x.EvidenceFingerprint, MissingText)
                .SetProperty(x => x.SourceRepository, "")
                .SetProperty(x => x.SourceRevision, "")
                .SetProperty(x => x.SourcePath, "")
                .SetProperty(x => x.SourceLine, (int?)null)
                .SetProperty(x => x.ConversationId, (Guid?)null)
                .SetProperty(x => x.StartedByUserId, "")
                .SetProperty(x => x.ReviewedByUserId, "")
                .SetProperty(x => x.CompletedAt, (DateTime?)null)
                .SetProperty(x => x.ReviewedAt, (DateTime?)null)
                .SetProperty(x => x.RecoveredByExecutionId, (Guid?)null)
                .SetProperty(x => x.RecoveredAt, (DateTime?)null)
                .SetProperty(x => x.FailureMessage, "")
                .SetProperty(x => x.UpdatedAt, now), cancellationToken);
        }

        // Optional text is returned as "" when NULL: Oracle stores "" as NULL, and callers and the
        // dashboard see the same values on every database.
        private static AssistantInvestigationRecord Map(AssistantInvestigationEntity x) => new()
        {
            Id = x.Id,
            ApplicationNamespace = x.ApplicationNamespace,
            ExecutionId = x.ExecutionId,
            ExecutionType = x.ExecutionType,
            ChainRootId = x.ChainRootId,
            FunctionName = x.FunctionName,
            ExecutionStatus = x.ExecutionStatus,
            Classification = x.Classification,
            Summary = x.Summary ?? "",
            Model = x.Model ?? "",
            Trigger = x.Trigger,
            State = x.State,
            ReviewState = x.ReviewState,
            EvidenceFingerprint = x.EvidenceFingerprint,
            SourceRepository = x.SourceRepository ?? "",
            SourceRevision = x.SourceRevision ?? "",
            SourcePath = x.SourcePath ?? "",
            SourceLine = x.SourceLine,
            ConversationId = x.ConversationId,
            StartedByUserId = x.StartedByUserId ?? "",
            ReviewedByUserId = x.ReviewedByUserId ?? "",
            OccurredAt = x.OccurredAt,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt,
            CompletedAt = x.CompletedAt,
            ReviewedAt = x.ReviewedAt,
            RecoveredByExecutionId = x.RecoveredByExecutionId,
            RecoveredAt = x.RecoveredAt,
            FailureMessage = x.FailureMessage ?? "",
        };

        private static string Limit(string value, int maximum)
            => string.IsNullOrEmpty(value) || value.Length <= maximum ? value ?? "" : value.Substring(0, maximum);

        private static DateTime NormalizeUtc(DateTime value)
            => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

        private static void ValidateCoordinationKey(string applicationNamespace,
            string evidenceFingerprint, Guid ownerToken)
        {
            if (string.IsNullOrWhiteSpace(applicationNamespace))
                throw new ArgumentException("Application namespace is required.", nameof(applicationNamespace));
            if (string.IsNullOrWhiteSpace(evidenceFingerprint))
                throw new ArgumentException("Evidence fingerprint is required.", nameof(evidenceFingerprint));
            if (ownerToken == Guid.Empty)
                throw new ArgumentException("Lease owner token is required.", nameof(ownerToken));
        }

        private static List<long> ParseStartedAtTicks(string value, long cutoffTicks)
        {
            var starts = new List<long>();
            if (string.IsNullOrWhiteSpace(value)) return starts;
            foreach (var item in value.Split(','))
                if (long.TryParse(item, out var ticks) && ticks > cutoffTicks)
                    starts.Add(ticks);
            return starts;
        }
    }
}
