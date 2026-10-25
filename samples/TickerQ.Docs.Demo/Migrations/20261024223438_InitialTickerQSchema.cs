using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TickerQ.Docs.Demo.Migrations
{
    /// <inheritdoc />
    public partial class InitialTickerQSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ticker");

            migrationBuilder.CreateTable(
                name: "AssistantConversations",
                schema: "ticker",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantConversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssistantInvestigationBudgets",
                schema: "ticker",
                columns: table => new
                {
                    ApplicationNamespace = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    StartedAtTicks = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantInvestigationBudgets", x => x.ApplicationNamespace);
                });

            migrationBuilder.CreateTable(
                name: "AssistantInvestigationLeases",
                schema: "ticker",
                columns: table => new
                {
                    ApplicationNamespace = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    EvidenceFingerprint = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    OwnerToken = table.Column<Guid>(type: "TEXT", nullable: false),
                    LeaseUntil = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantInvestigationLeases", x => new { x.ApplicationNamespace, x.EvidenceFingerprint });
                });

            migrationBuilder.CreateTable(
                name: "AssistantInvestigations",
                schema: "ticker",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ApplicationNamespace = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ExecutionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExecutionType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ChainRootId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FunctionName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ExecutionStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Classification = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ReviewState = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EvidenceFingerprint = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SourceRepository = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    SourceRevision = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SourcePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    SourceLine = table.Column<int>(type: "INTEGER", nullable: true),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StartedByUserId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ReviewedByUserId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RecoveredByExecutionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RecoveredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FailureMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantInvestigations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CronTickers",
                schema: "ticker",
                columns: table => new
                {
                    ApplicationNamespaceKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeedKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    SeedOwnerNamespace = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SeedManifestEpoch = table.Column<long>(type: "INTEGER", nullable: true),
                    DefinitionRevision = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    SeedLastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RetirementRequestedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RetiredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SeedWasEnabledBeforeRetirement = table.Column<bool>(type: "INTEGER", nullable: true),
                    Chain = table.Column<string>(type: "TEXT", nullable: true),
                    Expression = table.Column<string>(type: "TEXT", nullable: true),
                    TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Request = table.Column<byte[]>(type: "BLOB", nullable: true),
                    Retries = table.Column<int>(type: "INTEGER", nullable: false),
                    RetryIntervals = table.Column<string>(type: "TEXT", nullable: true),
                    SkipIfAlreadyRunning = table.Column<bool>(type: "INTEGER", nullable: false),
                    CatchUpMode = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSystemPaused = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    OnStale = table.Column<int>(type: "INTEGER", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    Function = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    InitIdentifier = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RequestContractVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    RequestContractFingerprint = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SchedulePolicyName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SchedulePolicyVersion = table.Column<long>(type: "INTEGER", nullable: true),
                    BlockedOccurrenceBehavior = table.Column<int>(type: "INTEGER", nullable: true),
                    MissedExecutionAction = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    MaximumLateness = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CronTickers", x => new { x.ApplicationNamespaceKey, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "ExecutionLogs",
                schema: "ticker",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PartitionKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TickerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    Payload = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Truncated = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationSettings",
                schema: "ticker",
                columns: table => new
                {
                    ApplicationNamespaceKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProtectedConfiguration = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationSettings", x => x.ApplicationNamespaceKey);
                });

            migrationBuilder.CreateTable(
                name: "NodeFinalizationOutbox",
                schema: "ticker",
                columns: table => new
                {
                    ApplicationNamespaceKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    OutboxId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    TickerType = table.Column<int>(type: "INTEGER", nullable: false),
                    TickerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AcquisitionToken = table.Column<Guid>(type: "TEXT", nullable: false),
                    DispatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NodeEpoch = table.Column<Guid>(type: "TEXT", nullable: false),
                    FinalizeUri = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    FinalizePathAndQuery = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    AllowPrivateCallbackAddressesForLocalDevelopment = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequestNonce = table.Column<Guid>(type: "TEXT", nullable: false),
                    ControlNonce = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExactBody = table.Column<byte[]>(type: "BLOB", maxLength: 8192, nullable: false),
                    CreatedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    TerminalMutationDigest = table.Column<byte[]>(type: "BLOB", maxLength: 32, nullable: false),
                    AvailableAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClaimToken = table.Column<Guid>(type: "TEXT", nullable: true),
                    ClaimedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastErrorCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeFinalizationOutbox", x => new { x.ApplicationNamespaceKey, x.OutboxId });
                });

            migrationBuilder.CreateTable(
                name: "SchedulePolicies",
                schema: "ticker",
                columns: table => new
                {
                    ApplicationNamespaceKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NameKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DefinitionJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulePolicies", x => new { x.ApplicationNamespaceKey, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "TickerQControlCommands",
                schema: "ticker",
                columns: table => new
                {
                    ApplicationNamespaceKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetNodeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TargetExecutionOwnerId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    TickerType = table.Column<int>(type: "INTEGER", nullable: false),
                    TickerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AcquisitionToken = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequestedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TickerQControlCommands", x => new { x.ApplicationNamespaceKey, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "TickerQDefinitions",
                schema: "ticker",
                columns: table => new
                {
                    ApplicationNamespaceKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Target = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NameKey = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DefinitionJson = table.Column<string>(type: "TEXT", nullable: false),
                    IncludesRequestData = table.Column<bool>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    ActivationToken = table.Column<Guid>(type: "TEXT", nullable: true),
                    PendingRootTickerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActivationClaimedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ActivatedRootTickerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActivatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TickerQDefinitions", x => new { x.ApplicationNamespaceKey, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "TickerQNodes",
                schema: "ticker",
                columns: table => new
                {
                    ApplicationNamespaceKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    NodeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExecutionOwnerId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    NodeIdentifier = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DashboardUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Version = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsRunning = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaxConcurrency = table.Column<int>(type: "INTEGER", nullable: false),
                    FunctionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ActiveExecutions = table.Column<int>(type: "INTEGER", nullable: false),
                    QueuedExecutions = table.Column<int>(type: "INTEGER", nullable: false),
                    ActiveWorkflows = table.Column<int>(type: "INTEGER", nullable: false),
                    WaitingForFunction = table.Column<int>(type: "INTEGER", nullable: false),
                    RetryingExecutions = table.Column<int>(type: "INTEGER", nullable: false),
                    ActivationEpoch = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TickerQNodes", x => new { x.ApplicationNamespaceKey, x.NodeKey });
                });

            migrationBuilder.CreateTable(
                name: "TickerQStoreMetadata",
                schema: "ticker",
                columns: table => new
                {
                    ApplicationNamespaceKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Id = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    DataVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    LastMigrationId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    ActivationEpoch = table.Column<long>(type: "INTEGER", nullable: false),
                    ActivationPhase = table.Column<int>(type: "INTEGER", nullable: false),
                    ActivationCheckpoint = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ActivationManifest = table.Column<string>(type: "TEXT", maxLength: 48, nullable: true),
                    ActivationManifestHistory = table.Column<string>(type: "TEXT", maxLength: 1568, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TickerQStoreMetadata", x => new { x.ApplicationNamespaceKey, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "TimeTickers",
                schema: "ticker",
                columns: table => new
                {
                    ApplicationNamespaceKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Function = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    InitIdentifier = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RequestContractVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    RequestContractFingerprint = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SchedulePolicyName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SchedulePolicyVersion = table.Column<long>(type: "INTEGER", nullable: true),
                    BlockedOccurrenceBehavior = table.Column<int>(type: "INTEGER", nullable: true),
                    MissedExecutionAction = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    MaximumLateness = table.Column<long>(type: "INTEGER", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    LockHolder = table.Column<string>(type: "TEXT", nullable: true),
                    Request = table.Column<byte[]>(type: "BLOB", nullable: true),
                    ExecutionTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RequestedExecutionTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ScheduleAdjustmentReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    LockedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExceptionMessage = table.Column<string>(type: "TEXT", nullable: true),
                    SkippedReason = table.Column<string>(type: "TEXT", nullable: true),
                    ElapsedTime = table.Column<long>(type: "INTEGER", nullable: false),
                    ProgressPercent = table.Column<double>(type: "REAL", nullable: true),
                    ProgressMessage = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ProgressUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Retries = table.Column<int>(type: "INTEGER", nullable: false),
                    RetryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RetryIntervals = table.Column<string>(type: "TEXT", nullable: true),
                    ParentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RunCondition = table.Column<int>(type: "INTEGER", nullable: true),
                    DeleteBehavior = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    LeaseUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AcquisitionToken = table.Column<Guid>(type: "TEXT", nullable: true),
                    ChainRootId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OriginCronTickerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ChainGeneration = table.Column<Guid>(type: "TEXT", nullable: true),
                    ChainRetryFromId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ChainRetryPreviousStatus = table.Column<int>(type: "INTEGER", nullable: true),
                    OnStale = table.Column<int>(type: "INTEGER", nullable: false),
                    StaleRestartCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeTickers", x => new { x.ApplicationNamespaceKey, x.Id });
                    table.ForeignKey(
                        name: "FK_TimeTickers_TimeTickers_ApplicationNamespaceKey_ParentId",
                        columns: x => new { x.ApplicationNamespaceKey, x.ParentId },
                        principalSchema: "ticker",
                        principalTable: "TimeTickers",
                        principalColumns: new[] { "ApplicationNamespaceKey", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "AssistantMessages",
                schema: "ticker",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TokensIn = table.Column<int>(type: "INTEGER", nullable: true),
                    TokensOut = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistantMessages_AssistantConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "ticker",
                        principalTable: "AssistantConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CronTickerOccurrences",
                schema: "ticker",
                columns: table => new
                {
                    ApplicationNamespaceKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    LockHolder = table.Column<string>(type: "TEXT", nullable: true),
                    ExecutionTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CronTickerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChainRootId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DefinitionRevision = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    LockedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExceptionMessage = table.Column<string>(type: "TEXT", nullable: true),
                    SkippedReason = table.Column<string>(type: "TEXT", nullable: true),
                    ElapsedTime = table.Column<long>(type: "INTEGER", nullable: false),
                    ProgressPercent = table.Column<double>(type: "REAL", nullable: true),
                    ProgressMessage = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ProgressUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RetryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LeaseUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    StaleRestartCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AcquisitionToken = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CronTickerOccurrences", x => new { x.ApplicationNamespaceKey, x.Id });
                    table.ForeignKey(
                        name: "FK_CronTickerOccurrences_CronTickers_ApplicationNamespaceKey_CronTickerId",
                        columns: x => new { x.ApplicationNamespaceKey, x.CronTickerId },
                        principalSchema: "ticker",
                        principalTable: "CronTickers",
                        principalColumns: new[] { "ApplicationNamespaceKey", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TimeTickerResults",
                schema: "ticker",
                columns: table => new
                {
                    ApplicationNamespaceKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    TickerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Payload = table.Column<byte[]>(type: "BLOB", nullable: false),
                    EnvelopeVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    MediaType = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ContractId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ContractType = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeTickerResults", x => new { x.ApplicationNamespaceKey, x.TickerId });
                    table.ForeignKey(
                        name: "FK_TimeTickerResults_TimeTickers_ApplicationNamespaceKey_TickerId",
                        columns: x => new { x.ApplicationNamespaceKey, x.TickerId },
                        principalSchema: "ticker",
                        principalTable: "TimeTickers",
                        principalColumns: new[] { "ApplicationNamespaceKey", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CronTickerOccurrenceResults",
                schema: "ticker",
                columns: table => new
                {
                    ApplicationNamespaceKey = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    TickerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Payload = table.Column<byte[]>(type: "BLOB", nullable: false),
                    EnvelopeVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    MediaType = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ContractId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ContractType = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CronTickerOccurrenceResults", x => new { x.ApplicationNamespaceKey, x.TickerId });
                    table.ForeignKey(
                        name: "FK_CronTickerOccurrenceResults_CronTickerOccurrences_ApplicationNamespaceKey_TickerId",
                        columns: x => new { x.ApplicationNamespaceKey, x.TickerId },
                        principalSchema: "ticker",
                        principalTable: "CronTickerOccurrences",
                        principalColumns: new[] { "ApplicationNamespaceKey", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantConversation_UserId_UpdatedAt",
                schema: "ticker",
                table: "AssistantConversations",
                columns: new[] { "UserId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantInvestigationLease_LeaseUntil",
                schema: "ticker",
                table: "AssistantInvestigationLeases",
                column: "LeaseUntil");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantInvestigation_Namespace_Evidence_CompletedAt",
                schema: "ticker",
                table: "AssistantInvestigations",
                columns: new[] { "ApplicationNamespace", "EvidenceFingerprint", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantInvestigation_Namespace_Function_OccurredAt",
                schema: "ticker",
                table: "AssistantInvestigations",
                columns: new[] { "ApplicationNamespace", "FunctionName", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AssistantInvestigation_Namespace_UpdatedAt",
                schema: "ticker",
                table: "AssistantInvestigations",
                columns: new[] { "ApplicationNamespace", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_AssistantInvestigation_Namespace_Execution",
                schema: "ticker",
                table: "AssistantInvestigations",
                columns: new[] { "ApplicationNamespace", "ExecutionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssistantMessage_ConversationId_Ordinal",
                schema: "ticker",
                table: "AssistantMessages",
                columns: new[] { "ConversationId", "Ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_CronOccurrence_DefinitionRevision_Status",
                schema: "ticker",
                table: "CronTickerOccurrences",
                columns: new[] { "ApplicationNamespaceKey", "CronTickerId", "DefinitionRevision", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CronTickerOccurrence_CronTickerId",
                schema: "ticker",
                table: "CronTickerOccurrences",
                columns: new[] { "ApplicationNamespaceKey", "CronTickerId" });

            migrationBuilder.CreateIndex(
                name: "IX_CronTickerOccurrence_ExecutionTime",
                schema: "ticker",
                table: "CronTickerOccurrences",
                columns: new[] { "ApplicationNamespaceKey", "ExecutionTime" });

            migrationBuilder.CreateIndex(
                name: "IX_CronTickerOccurrence_Status_ExecutedAt",
                schema: "ticker",
                table: "CronTickerOccurrences",
                columns: new[] { "ApplicationNamespaceKey", "Status", "ExecutedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CronTickerOccurrence_Status_ExecutionTime",
                schema: "ticker",
                table: "CronTickerOccurrences",
                columns: new[] { "ApplicationNamespaceKey", "Status", "ExecutionTime" });

            migrationBuilder.CreateIndex(
                name: "UQ_CronTickerId_ExecutionTime",
                schema: "ticker",
                table: "CronTickerOccurrences",
                columns: new[] { "ApplicationNamespaceKey", "CronTickerId", "ExecutionTime" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CronTickers_Expression",
                schema: "ticker",
                table: "CronTickers",
                columns: new[] { "ApplicationNamespaceKey", "Expression" });

            migrationBuilder.CreateIndex(
                name: "IX_CronTickers_SchedulePolicy",
                schema: "ticker",
                table: "CronTickers",
                columns: new[] { "ApplicationNamespaceKey", "SchedulePolicyName" });

            migrationBuilder.CreateIndex(
                name: "IX_CronTickers_SeedOwner_Epoch",
                schema: "ticker",
                table: "CronTickers",
                columns: new[] { "ApplicationNamespaceKey", "SeedOwnerNamespace", "SeedManifestEpoch" });

            migrationBuilder.CreateIndex(
                name: "IX_Function_Expression",
                schema: "ticker",
                table: "CronTickers",
                columns: new[] { "ApplicationNamespaceKey", "Function", "Expression" });

            migrationBuilder.CreateIndex(
                name: "UX_CronTickers_SeedKey",
                schema: "ticker",
                table: "CronTickers",
                columns: new[] { "ApplicationNamespaceKey", "SeedKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionLogs_PartitionKey_ExpiresAtUtcTicks",
                schema: "ticker",
                table: "ExecutionLogs",
                columns: new[] { "PartitionKey", "ExpiresAtUtcTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionLogs_PartitionKey_UpdatedAtUtcTicks_Id",
                schema: "ticker",
                table: "ExecutionLogs",
                columns: new[] { "PartitionKey", "UpdatedAtUtcTicks", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_NodeFinalizationOutbox_ApplicationNamespaceKey_AvailableAtUtc_OutboxId",
                schema: "ticker",
                table: "NodeFinalizationOutbox",
                columns: new[] { "ApplicationNamespaceKey", "AvailableAtUtc", "OutboxId" });

            migrationBuilder.CreateIndex(
                name: "IX_NodeFinalizationOutbox_ApplicationNamespaceKey_TickerType_TickerId_AcquisitionToken_DispatchId_NodeEpoch",
                schema: "ticker",
                table: "NodeFinalizationOutbox",
                columns: new[] { "ApplicationNamespaceKey", "TickerType", "TickerId", "AcquisitionToken", "DispatchId", "NodeEpoch" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchedulePolicies_Updated",
                schema: "ticker",
                table: "SchedulePolicies",
                columns: new[] { "ApplicationNamespaceKey", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_SchedulePolicies_Name",
                schema: "ticker",
                table: "SchedulePolicies",
                columns: new[] { "ApplicationNamespaceKey", "NameKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TickerQControlCommands_ApplicationNamespaceKey_ExpiresAtUtc",
                schema: "ticker",
                table: "TickerQControlCommands",
                columns: new[] { "ApplicationNamespaceKey", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TickerQControlCommands_ApplicationNamespaceKey_TargetNodeKey_Status_RequestedAtUtc",
                schema: "ticker",
                table: "TickerQControlCommands",
                columns: new[] { "ApplicationNamespaceKey", "TargetNodeKey", "Status", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TickerQDefinitions_List",
                schema: "ticker",
                table: "TickerQDefinitions",
                columns: new[] { "ApplicationNamespaceKey", "Kind", "Target", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_TickerQDefinitions_Name",
                schema: "ticker",
                table: "TickerQDefinitions",
                columns: new[] { "ApplicationNamespaceKey", "NameKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TickerQNodes_ApplicationNamespaceKey_LastSeenAtUtc",
                schema: "ticker",
                table: "TickerQNodes",
                columns: new[] { "ApplicationNamespaceKey", "LastSeenAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeTicker_ChainRootId",
                schema: "ticker",
                table: "TimeTickers",
                columns: new[] { "ApplicationNamespaceKey", "ChainRootId" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeTicker_ExecutionTime",
                schema: "ticker",
                table: "TimeTickers",
                columns: new[] { "ApplicationNamespaceKey", "ExecutionTime" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeTicker_SchedulePolicy",
                schema: "ticker",
                table: "TimeTickers",
                columns: new[] { "ApplicationNamespaceKey", "SchedulePolicyName" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeTicker_Status_ExecutedAt",
                schema: "ticker",
                table: "TimeTickers",
                columns: new[] { "ApplicationNamespaceKey", "Status", "ExecutedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeTicker_Status_ExecutionTime",
                schema: "ticker",
                table: "TimeTickers",
                columns: new[] { "ApplicationNamespaceKey", "Status", "ExecutionTime" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeTickers_ApplicationNamespaceKey_OriginCronTickerId_Status",
                schema: "ticker",
                table: "TimeTickers",
                columns: new[] { "ApplicationNamespaceKey", "OriginCronTickerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TimeTickers_ApplicationNamespaceKey_ParentId",
                schema: "ticker",
                table: "TimeTickers",
                columns: new[] { "ApplicationNamespaceKey", "ParentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssistantInvestigationBudgets",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "AssistantInvestigationLeases",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "AssistantInvestigations",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "AssistantMessages",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "CronTickerOccurrenceResults",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "ExecutionLogs",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "IntegrationSettings",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "NodeFinalizationOutbox",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "SchedulePolicies",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "TickerQControlCommands",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "TickerQDefinitions",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "TickerQNodes",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "TickerQStoreMetadata",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "TimeTickerResults",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "AssistantConversations",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "CronTickerOccurrences",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "TimeTickers",
                schema: "ticker");

            migrationBuilder.DropTable(
                name: "CronTickers",
                schema: "ticker");
        }
    }
}
