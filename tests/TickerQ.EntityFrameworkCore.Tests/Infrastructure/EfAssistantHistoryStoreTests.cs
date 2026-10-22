using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TickerQ.EntityFrameworkCore.Configurations;
using TickerQ.EntityFrameworkCore.Infrastructure;
using TickerQ.Utilities.Models;

namespace TickerQ.EntityFrameworkCore.Tests.Infrastructure;

public sealed class EfAssistantHistoryStoreTests
{
    [Fact]
    public async Task Proposal_cards_and_confirmation_results_round_trip_for_the_owner()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<HistoryContext>().UseSqlite(connection).Options;
        await using var db = new HistoryContext(options);
        await db.Database.EnsureCreatedAsync();
        var store = new EfAssistantHistoryStore<HistoryContext>(db, new AssistantHistoryOptions());
        var proposalId = Guid.NewGuid();
        const string proposalJson = "{\"proposalId\":null,\"kind\":\"createTimeTickerDraft\",\"function\":\"Demo.Progress\"}";

        var conversationId = await store.AppendExchangeWithProposalsAsync(
            "operator-1", null, "Create a demo job", "I drafted it.", "test-model",
            [new AssistantHistoryProposal { Id = proposalId, ProposalJson = proposalJson }]);

        var initial = await store.GetMessagesAsync("operator-1", conversationId);
        Assert.Equal(["user", "proposal", "assistant"], initial.Select(message => message.Role));
        var proposal = initial[1];
        Assert.Equal(proposalId, proposal.Id);
        Assert.Equal(proposalJson, proposal.ProposalJson);
        Assert.Equal("pending", proposal.ProposalStatus);
        Assert.Equal(string.Empty, proposal.ProposalResultJson);

        Assert.False(await store.SetProposalStateAsync("operator-2", proposalId,
            "applied", "{\"message\":\"Job draft saved\"}"));
        Assert.True(await store.SetProposalStateAsync("operator-1", proposalId,
            "applied", "{\"message\":\"Job draft saved\",\"href\":\"/time-tickers/new-chain?draft=91af8d52-95fd-4a37-867c-a42176111d4c\"}"));
        Assert.False(await store.SetProposalStateAsync("operator-1", proposalId,
            "dismissed", string.Empty));

        var restored = await store.GetMessagesAsync("operator-1", conversationId);
        Assert.Equal("applied", restored[1].ProposalStatus);
        Assert.Contains("Job draft saved", restored[1].ProposalResultJson);
        Assert.Null(await store.GetMessagesAsync("operator-2", conversationId));
    }

    private sealed class HistoryContext(DbContextOptions<HistoryContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new AssistantConversationConfigurations());
            modelBuilder.ApplyConfiguration(new AssistantMessageConfigurations());
        }
    }
}
