using Aircane.Api.Hubs;
using Aircane.Application.Abstractions;
using Aircane.Domain.Entities;
using Aircane.Domain.Enums;
using Aircane.Infrastructure.Chat;
using Aircane.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aircane.UnitTests.Chat;

/// <summary>Stored table chat: what is kept, how it pages, and the limits.</summary>
public sealed class ChatServiceTests : IDisposable
{
    private readonly AircaneDbContext _db;
    private readonly ChatService _sut;
    private readonly Guid _session = Guid.NewGuid();
    private readonly Guid _alice = Guid.NewGuid();

    public ChatServiceTests()
    {
        _db = new AircaneDbContext(new DbContextOptionsBuilder<AircaneDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _sut = new ChatService(_db);
    }

    public void Dispose() => _db.Dispose();

    private Task<ChatMessageDto?> SayAsync(string text, Guid? session = null, string name = "Alice", ParticipantRole role = ParticipantRole.Player)
        => _sut.RecordAsync(session ?? _session, _alice, name, role, text);

    [Fact]
    public async Task Record_StoresSenderNameRoleTextAndTime()
    {
        var before = DateTimeOffset.UtcNow;

        var stored = await SayAsync("  Hello table  ", role: ParticipantRole.Player);

        Assert.NotNull(stored);
        Assert.Equal("Hello table", stored!.Text);
        Assert.Equal("Alice", stored.SenderName);
        Assert.Equal(_alice, stored.SenderId);
        Assert.Equal(ParticipantRole.Player, stored.SenderRole);
        Assert.Equal(_session, stored.SessionId);
        Assert.True(stored.SentAt >= before);
        Assert.Single(await _db.ChatMessages.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Record_EmptyText_IsNotStored(string text)
    {
        Assert.Null(await SayAsync(text));
        Assert.Empty(await _db.ChatMessages.ToListAsync());
    }

    [Fact]
    public async Task Record_TooLong_IsRefused_AndNotStored()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => SayAsync(new string('x', ChatMessage.MaxTextLength + 1)));
        Assert.Empty(await _db.ChatMessages.ToListAsync());

        Assert.NotNull(await SayAsync(new string('x', ChatMessage.MaxTextLength)));   // exactly the limit is fine
    }

    [Fact]
    public async Task History_IsOldestFirst_AndHoldsOnlyThisSessionsMessages()
    {
        await SayAsync("one");
        await SayAsync("two");
        await SayAsync("elsewhere", session: Guid.NewGuid());
        await SayAsync("three");

        var history = await _sut.GetHistoryAsync(_session);

        Assert.Equal(["one", "two", "three"], history.Messages.Select(m => m.Text));
        Assert.False(history.HasMore);
    }

    [Fact]
    public async Task History_ReturnsTheLastNMessages_AndPagesBackWithBefore()
    {
        for (var i = 1; i <= 25; i++)
            await SayAsync($"m{i:00}");

        var newest = await _sut.GetHistoryAsync(_session, limit: 10);
        Assert.Equal(Enumerable.Range(16, 10).Select(i => $"m{i:00}"), newest.Messages.Select(m => m.Text));
        Assert.True(newest.HasMore);

        var middle = await _sut.GetHistoryAsync(_session, limit: 10, before: newest.Messages[0].Id);
        Assert.Equal(Enumerable.Range(6, 10).Select(i => $"m{i:00}"), middle.Messages.Select(m => m.Text));
        Assert.True(middle.HasMore);

        var oldest = await _sut.GetHistoryAsync(_session, limit: 10, before: middle.Messages[0].Id);
        Assert.Equal(Enumerable.Range(1, 5).Select(i => $"m{i:00}"), oldest.Messages.Select(m => m.Text));
        Assert.False(oldest.HasMore);
    }

    [Fact]
    public async Task History_DefaultsTo100_AndNeverMoreThanTheMaximum()
    {
        for (var i = 0; i < ChatHistory.MaxLimit + 10; i++)
            await SayAsync($"m{i}");

        Assert.Equal(ChatHistory.DefaultLimit, (await _sut.GetHistoryAsync(_session)).Messages.Count);
        var greedy = await _sut.GetHistoryAsync(_session, limit: 5000);
        Assert.Equal(ChatHistory.MaxLimit, greedy.Messages.Count);   // asked for 5000, got the maximum
        Assert.True(greedy.HasMore);
        Assert.Single((await _sut.GetHistoryAsync(_session, limit: 0)).Messages);   // at least one
    }

    [Fact]
    public async Task History_BeforeAMessageOfAnotherSession_ReturnsNothing_NotTheNewestPage()
    {
        await SayAsync("mine");
        var theirs = await SayAsync("theirs", session: Guid.NewGuid());

        var page = await _sut.GetHistoryAsync(_session, before: theirs!.Id);

        Assert.Empty(page.Messages);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task Retention_KeepsOnlyTheNewestMessagesOfASession()
    {
        var other = await SayAsync("another table", session: Guid.NewGuid());
        for (var i = 0; i < ChatMessage.MaxStoredPerSession + 5; i++)
            await SayAsync($"m{i}");

        var stored = await _db.ChatMessages.AsNoTracking().Where(m => m.SessionId == _session).ToListAsync();

        Assert.Equal(ChatMessage.MaxStoredPerSession, stored.Count);
        Assert.DoesNotContain(stored, m => m.Text == "m0" || m.Text == "m4");   // the oldest five are gone
        Assert.Contains(stored, m => m.Text == "m5");
        Assert.Contains(stored, m => m.Text == $"m{ChatMessage.MaxStoredPerSession + 4}");
        Assert.NotNull(await _db.ChatMessages.FindAsync(other!.Id));                // another session is untouched
    }
}

public sealed class ChatRateLimiterTests
{
    private readonly Guid _session = Guid.NewGuid();
    private readonly Guid _alice = Guid.NewGuid();
    private DateTimeOffset _now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private ChatRateLimiter Limiter() => new(() => _now);

    [Fact]
    public void AllowsTheLimit_ThenRefuses()
    {
        var limiter = Limiter();

        for (var i = 0; i < ChatRateLimiter.MaxMessages; i++)
            Assert.True(limiter.TryAcquire(_session, _alice));

        Assert.False(limiter.TryAcquire(_session, _alice));
    }

    [Fact]
    public void AllowsAgainOnceTheWindowHasPassed()
    {
        var limiter = Limiter();
        for (var i = 0; i < ChatRateLimiter.MaxMessages; i++)
            limiter.TryAcquire(_session, _alice);
        Assert.False(limiter.TryAcquire(_session, _alice));

        _now += ChatRateLimiter.Window;

        Assert.True(limiter.TryAcquire(_session, _alice));
    }

    [Fact]
    public void CountsEachSenderAndEachSessionSeparately()
    {
        var limiter = Limiter();
        for (var i = 0; i < ChatRateLimiter.MaxMessages; i++)
            limiter.TryAcquire(_session, _alice);

        Assert.True(limiter.TryAcquire(_session, Guid.NewGuid()));
        Assert.True(limiter.TryAcquire(Guid.NewGuid(), _alice));
    }
}
