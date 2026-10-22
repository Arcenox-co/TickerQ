using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TickerQ.EntityFrameworkCore.Entities;

namespace TickerQ.EntityFrameworkCore.Configurations
{
    public class AssistantConversationConfigurations : IEntityTypeConfiguration<AssistantConversationEntity>
    {
        private readonly string _schema;

        public AssistantConversationConfigurations(string schema = Constants.DefaultSchema)
            => _schema = schema;

        public void Configure(EntityTypeBuilder<AssistantConversationEntity> builder)
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.UserId)
                .HasMaxLength(256)
                .IsRequired();

            builder.Property(x => x.Title)
                .HasMaxLength(200);

            builder.Property(x => x.Model)
                .HasMaxLength(100);

            // The list query: "my conversations, newest first".
            builder.HasIndex(x => new { x.UserId, x.UpdatedAt })
                .HasDatabaseName("IX_AssistantConversation_UserId_UpdatedAt");

            builder.HasMany(x => x.Messages)
                .WithOne()
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.ToTable("AssistantConversations", _schema);
        }
    }

    public class AssistantMessageConfigurations : IEntityTypeConfiguration<AssistantMessageEntity>
    {
        private readonly string _schema;

        public AssistantMessageConfigurations(string schema = Constants.DefaultSchema)
            => _schema = schema;

        public void Configure(EntityTypeBuilder<AssistantMessageEntity> builder)
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.Role)
                .HasMaxLength(16)
                .IsRequired();

            builder.Property(x => x.Content)
                .IsRequired();

            // The read query: "messages of a conversation, in order".
            builder.HasIndex(x => new { x.ConversationId, x.Ordinal })
                .HasDatabaseName("IX_AssistantMessage_ConversationId_Ordinal");

            builder.ToTable("AssistantMessages", _schema);
        }
    }

    public class AssistantInvestigationConfigurations : IEntityTypeConfiguration<AssistantInvestigationEntity>
    {
        private readonly string _schema;

        public AssistantInvestigationConfigurations(string schema = Constants.DefaultSchema)
            => _schema = schema;

        public void Configure(EntityTypeBuilder<AssistantInvestigationEntity> builder)
        {
            builder.HasKey(x => x.Id);
            builder.Property(x => x.ApplicationNamespace).HasMaxLength(256).IsRequired();
            builder.Property(x => x.ExecutionType).HasMaxLength(32).IsRequired();
            builder.Property(x => x.FunctionName).HasMaxLength(512).IsRequired();
            builder.Property(x => x.ExecutionStatus).HasMaxLength(32).IsRequired();
            builder.Property(x => x.Classification).HasMaxLength(64).IsRequired();
            builder.Property(x => x.Model).HasMaxLength(100);
            builder.Property(x => x.Trigger).HasMaxLength(32).IsRequired();
            builder.Property(x => x.State).HasMaxLength(32).IsRequired();
            builder.Property(x => x.ReviewState).HasMaxLength(32).IsRequired();
            builder.Property(x => x.EvidenceFingerprint).HasMaxLength(128).IsRequired();
            builder.Property(x => x.SourceRepository).HasMaxLength(256);
            builder.Property(x => x.SourceRevision).HasMaxLength(128);
            builder.Property(x => x.SourcePath).HasMaxLength(1024);
            builder.Property(x => x.StartedByUserId).HasMaxLength(256);
            builder.Property(x => x.ReviewedByUserId).HasMaxLength(256);
            builder.Property(x => x.FailureMessage).HasMaxLength(2000);

            // This unique index is the cluster-wide auto-investigation claim.
            builder.HasIndex(x => new { x.ApplicationNamespace, x.ExecutionId })
                .IsUnique()
                .HasDatabaseName("UX_AssistantInvestigation_Namespace_Execution");
            builder.HasIndex(x => new { x.ApplicationNamespace, x.UpdatedAt })
                .HasDatabaseName("IX_AssistantInvestigation_Namespace_UpdatedAt");
            builder.HasIndex(x => new { x.ApplicationNamespace, x.FunctionName, x.OccurredAt })
                .HasDatabaseName("IX_AssistantInvestigation_Namespace_Function_OccurredAt");
            builder.HasIndex(x => new { x.ApplicationNamespace, x.EvidenceFingerprint, x.CompletedAt })
                .HasDatabaseName("IX_AssistantInvestigation_Namespace_Evidence_CompletedAt");

            builder.ToTable("AssistantInvestigations", _schema);
        }
    }

    public class AssistantInvestigationLeaseConfigurations : IEntityTypeConfiguration<AssistantInvestigationLeaseEntity>
    {
        private readonly string _schema;

        public AssistantInvestigationLeaseConfigurations(string schema = Constants.DefaultSchema)
            => _schema = schema;

        public void Configure(EntityTypeBuilder<AssistantInvestigationLeaseEntity> builder)
        {
            builder.HasKey(x => new { x.ApplicationNamespace, x.EvidenceFingerprint });
            builder.Property(x => x.ApplicationNamespace).HasMaxLength(256).IsRequired();
            builder.Property(x => x.EvidenceFingerprint).HasMaxLength(128).IsRequired();
            builder.HasIndex(x => x.LeaseUntil)
                .HasDatabaseName("IX_AssistantInvestigationLease_LeaseUntil");
            builder.ToTable("AssistantInvestigationLeases", _schema);
        }
    }

    public class AssistantInvestigationBudgetConfigurations : IEntityTypeConfiguration<AssistantInvestigationBudgetEntity>
    {
        private readonly string _schema;

        public AssistantInvestigationBudgetConfigurations(string schema = Constants.DefaultSchema)
            => _schema = schema;

        public void Configure(EntityTypeBuilder<AssistantInvestigationBudgetEntity> builder)
        {
            builder.HasKey(x => x.ApplicationNamespace);
            builder.Property(x => x.ApplicationNamespace).HasMaxLength(256).IsRequired();
            // The public limit is 500 starts. Twenty decimal UTC ticks plus a separator keeps
            // the largest valid window comfortably below this portable bound.
            builder.Property(x => x.StartedAtTicks).HasMaxLength(16_000).IsRequired();
            builder.ToTable("AssistantInvestigationBudgets", _schema);
        }
    }
}
