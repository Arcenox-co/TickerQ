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
}
