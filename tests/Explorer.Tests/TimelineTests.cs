using ArchiveExplorer.Index;
using Microsoft.Data.Sqlite;

namespace ArchiveExplorer.Tests;

/// <summary>Per-day message counts behind the chat timeline.</summary>
public sealed class TimelineTests : IDisposable
{
    private const string Jid = "4915550100001@s.whatsapp.net";
    private readonly string _dir = Directory.CreateTempSubdirectory("wae-timeline-").FullName;

    public void Dispose()
    {
        foreach (var store in _stores)
        {
            store.Dispose();
        }
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private Queries Index(params long[] timestamps)
    {
        Directory.CreateDirectory(Path.Combine(_dir, "json"));
        var messages = string.Join(", ", timestamps.Select((ts, i) =>
            $$"""
            "{{i + 1}}": { "from_me": false, "timestamp": {{ts}}, "data": "message {{i}}" }
            """));
        File.WriteAllText(Path.Combine(_dir, "json", "a.json"), $$"""{ "{{Jid}}": { "name": "A", "messages": { {{messages}} } } }""");
        var store = IndexStore.Open(_dir, null);
        store.Refresh(null);
        _stores.Add(store);
        return new Queries(store.Connection);
    }

    private readonly List<IndexStore> _stores = [];

    private static long Utc(int y, int m, int d, int h = 12, int min = 0) =>
        new DateTimeOffset(y, m, d, h, min, 0, TimeSpan.Zero).ToUnixTimeSeconds();

    private static long Day(int y, int m, int d) => new DateTimeOffset(y, m, d, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds() / 86400;

    [Fact]
    public void CountsMessagesPerDay_OldestFirst_SkippingEmptyDays()
    {
        var q = Index(Utc(2019, 3, 2), Utc(2016, 1, 5, 8), Utc(2016, 1, 5, 20), Utc(2019, 3, 2, 13));
        var chat = q.ListChats(null).Single();

        var days = q.Timeline(chat.Id, 0);

        Assert.Equal([new TimelineDay(Day(2016, 1, 5), 2), new TimelineDay(Day(2019, 3, 2), 2)], days);
        Assert.Equal(chat.MessageCount, days.Sum(d => d.Count));
    }

    [Fact]
    public void DaysFollowTheViewersOffset()
    {
        // 23:30 UTC is already the next day in Berlin (UTC+1) and still the same day in New York (UTC-5)
        var q = Index(Utc(2020, 6, 1, 23, 30));
        var chat = q.ListChats(null).Single();

        Assert.Equal(Day(2020, 6, 2), Assert.Single(q.Timeline(chat.Id, 60)).Day);
        Assert.Equal(Day(2020, 6, 1), Assert.Single(q.Timeline(chat.Id, -300)).Day);
        Assert.Equal(Day(2020, 6, 1), Assert.Single(q.Timeline(chat.Id, 0)).Day);
    }

    [Fact]
    public void OffsetIsClampedToRealTimezones()
    {
        var q = Index(Utc(2020, 6, 1, 0, 30));
        var chat = q.ListChats(null).Single();

        Assert.Equal(q.Timeline(chat.Id, 14 * 60), q.Timeline(chat.Id, 100_000));
        Assert.Equal(q.Timeline(chat.Id, -14 * 60), q.Timeline(chat.Id, -100_000));
    }

    [Fact]
    public void UnknownChatHasNoDays()
    {
        var q = Index(Utc(2020, 6, 1));

        Assert.Empty(q.Timeline(999_999, 0));
    }

    [Fact]
    public void SearchInChat_ReturnsTimestampsForTheTimeline()
    {
        var q = Index(Utc(2016, 1, 5), Utc(2019, 3, 2));
        var chat = q.ListChats(null).Single();

        var hits = q.SearchInChat(chat.Id, "message");

        Assert.Equal([Utc(2016, 1, 5), Utc(2019, 3, 2)], hits.Select(h => h.Ts));
    }

}
