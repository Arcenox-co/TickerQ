using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models;

namespace DashboardTestApp;

// ─────────────────────────────────────────────────────────────────────────────
// OPTIONAL MODE: messages held at OpenAI (Conversations API).
//
// The DEFAULT db mode needs none of this — it's one line on the operational
// store (see Program.cs):
//     efOptions.AddAssistantHistory();
// which maps the packaged AssistantConversations/AssistantMessages tables
// into the TickerQ DbContext and registers the EF-backed store.
//
// This file is the alternative backend (ASSISTANT_HISTORY=openai): your DB
// keeps only a pointer row per conversation; the transcript lives at OpenAI.
// Trade-offs: OpenAI-only, transcripts under OpenAI's retention, an extra
// round-trip to render history.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Pointer row: which OpenAI conversation belongs to which user.</summary>
public class AssistantConversationPointer
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Model { get; set; } = "";

    /// <summary>OpenAI "conv_…" id — where the actual transcript lives.</summary>
    public string ProviderConversationId { get; set; } = "";

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class AssistantPointerDbContext(DbContextOptions<AssistantPointerDbContext> options) : DbContext(options)
{
    public DbSet<AssistantConversationPointer> Conversations => Set<AssistantConversationPointer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AssistantConversationPointer>(e =>
        {
            e.ToTable("AssistantConversationPointers");
            e.HasKey(x => x.Id);
            e.Property(x => x.UserId).HasMaxLength(256).IsRequired();
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Model).HasMaxLength(100);
            e.Property(x => x.ProviderConversationId).HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.UserId, x.UpdatedAt });
        });
    }
}

public sealed class OpenAIConversationHistoryStore(
    AssistantPointerDbContext db, IHttpClientFactory httpClientFactory) : IAssistantHistoryStore
{
    private const int MaxConversationsPerUser = 50;

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient("openai-conversations");
        client.BaseAddress = new Uri("https://api.openai.com/v1/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
        return client;
    }

    public async Task<IReadOnlyList<AssistantConversationSummary>> ListConversationsAsync(
        string userId, CancellationToken ct = default)
        => await db.Conversations.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => new AssistantConversationSummary
            {
                Id = c.Id, Title = c.Title, Model = c.Model,
                CreatedAt = c.CreatedAt, UpdatedAt = c.UpdatedAt,
            })
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AssistantHistoryMessage>> GetMessagesAsync(
        string userId, Guid conversationId, CancellationToken ct = default)
    {
        var conversation = await db.Conversations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId, ct);
        if (conversation?.ProviderConversationId is not { Length: > 0 } providerId) return null!;

        using var http = CreateClient();
        var response = await http.GetAsync($"conversations/{providerId}/items?order=asc&limit=100", ct);
        if (!response.IsSuccessStatusCode) return Array.Empty<AssistantHistoryMessage>();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var messages = new List<AssistantHistoryMessage>();
        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            if (item.GetProperty("type").GetString() != "message") continue;
            var role = item.GetProperty("role").GetString() ?? "user";
            var text = new StringBuilder();
            foreach (var part in item.GetProperty("content").EnumerateArray())
                if (part.TryGetProperty("text", out var t)) text.Append(t.GetString());
            messages.Add(new AssistantHistoryMessage
            {
                Role = role == "assistant" ? "assistant" : "user",
                Content = text.ToString(),
                CreatedAt = conversation.UpdatedAt,
            });
        }
        return messages;
    }

    public async Task<Guid> AppendExchangeAsync(
        string userId, Guid? conversationId, string userMessage, string assistantMessage,
        string model, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var conversation = conversationId.HasValue
            ? await db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId, ct)
            : null;

        using var http = CreateClient();

        if (conversation is null)
        {
            // Create the provider-held conversation; our DB keeps the pointer only.
            var create = await http.PostAsync("conversations",
                new StringContent("{}", Encoding.UTF8, "application/json"), ct);
            create.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await create.Content.ReadAsStringAsync(ct));
            var providerId = doc.RootElement.GetProperty("id").GetString()!;

            conversation = new AssistantConversationPointer
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = userMessage.Length > 60 ? userMessage[..60] + "…" : userMessage,
                Model = model,
                ProviderConversationId = providerId,
                CreatedAt = now,
            };
            db.Conversations.Add(conversation);

            var excess = await db.Conversations
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.UpdatedAt)
                .Skip(MaxConversationsPerUser - 1)
                .Select(c => c.Id)
                .ToListAsync(ct);
            if (excess.Count > 0)
                await db.Conversations.Where(c => excess.Contains(c.Id)).ExecuteDeleteAsync(ct);
        }

        conversation.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        // Append both turns to the provider-held transcript.
        var payload = JsonSerializer.Serialize(new
        {
            items = new object[]
            {
                new { type = "message", role = "user", content = userMessage },
                new { type = "message", role = "assistant", content = assistantMessage },
            },
        });
        var append = await http.PostAsync(
            $"conversations/{conversation.ProviderConversationId}/items",
            new StringContent(payload, Encoding.UTF8, "application/json"), ct);
        append.EnsureSuccessStatusCode();

        return conversation.Id;
    }

    public async Task DeleteConversationAsync(string userId, Guid conversationId, CancellationToken ct = default)
    {
        var conversation = await db.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId, ct);
        if (conversation is null) return;

        if (conversation.ProviderConversationId is { Length: > 0 } providerId)
        {
            using var http = CreateClient();
            try { await http.DeleteAsync($"conversations/{providerId}", ct); }
            catch { /* provider-side cleanup is best-effort */ }
        }

        db.Conversations.Remove(conversation);
        await db.SaveChangesAsync(ct);
    }
}
