using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ArchiveExplorer.Demo;

/// <summary>
/// Writes a hand-scripted, life-like export for the README screenshots: fictional people, eleven years of history
/// with busy and quiet periods, generated photos and profile pictures (in a contacts.vcf, as users would supply
/// them). Seeded: the same call produces the same content (bytes may differ across platforms and runtimes).
/// Phone numbers come from Ofcom's range reserved for drama (+44 7700 900xxx), so none belongs to anyone.
/// Never put real chats here.
/// </summary>
public static class ShowcaseArchiveWriter
{
    /// <summary>Appears in several chats across the years, for the search screenshot.</summary>
    public const string SearchWord = "lighthouse";

    public const string HeroChat = "Lena Hoffmann";

    /// <summary>The day the hero screenshot shows.</summary>
    public static readonly DateOnly HeroDay = new(2019, 6, 22);

    private static readonly TimeZoneInfo Zone = BerlinTime();

    /// <summary>Europe/Berlin; on systems without time-zone data the same rule built by hand (CET/CEST).</summary>
    private static TimeZoneInfo BerlinTime()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday));
            return TimeZoneInfo.CreateCustomTimeZone("CET-showcase", TimeSpan.FromHours(1), "CET", "CET", "CEST", [rule]);
        }
    }

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private const string Me = "\0me";

    private sealed class Msg
    {
        public long Ts;
        public string? Sender; // null = the other person in a private chat, Me = me
        public string? Text;
        public string? MediaPath;
        public string? Mime;
        public string? Caption;
        public bool System;
        public bool Call;
        public Msg? ReplyTo;
        public readonly List<(string Who, string Emoji)> Reactions = [];
        public bool Edited;
        public string KeyId = "";

        public Msg Reply(Msg to)
        {
            ReplyTo = to;
            return this;
        }

        public Msg React(string who, string emoji)
        {
            Reactions.Add((who, emoji));
            return this;
        }
    }

    private sealed class Chat(string jid, string name, bool group, int seed)
    {
        public int Seed { get; } = seed;
        public string Jid { get; } = jid;
        public string Name { get; } = name;
        public bool Group { get; } = group;
        public Random Random { get; } = new(seed);
        public List<Msg> Messages { get; } = [];
        public HashSet<DateOnly> Scripted { get; } = [];
    }

    private sealed class Writer(string root)
    {
        public int Media;
        private int _photo;

        public string Photo(DateOnly day, Scene scene, int seed)
        {
            var path = $"WhatsApp/Media/WhatsApp Images/IMG-{day:yyyyMMdd}-WA{++_photo:D4}.png";
            var (w, h, rgb) = SceneRenderer.Landscape(scene, seed);
            WriteFile(root, path, PngEncoder.Encode(w, h, rgb, CompressionLevel.Fastest));
            Media++;
            return path;
        }

        public string Voice(DateOnly day, int seconds)
        {
            var path = $"WhatsApp/Media/WhatsApp Voice Notes/{day:yyyyMM}/PTT-{day:yyyyMMdd}-WA{++_photo:D4}.wav";
            WriteFile(root, path, SilentWav(seconds));
            Media++;
            return path;
        }
    }

    /// <summary>A day of scripted messages in one chat; times are "HH:mm" Berlin time.</summary>
    private sealed class Day(Chat chat, DateOnly day, Writer writer)
    {
        private Msg Add(string time, string? sender, Action<Msg> fill)
        {
            var m = new Msg { Ts = At(day, time), Sender = sender };
            fill(m);
            chat.Messages.Add(m);
            return m;
        }

        public Msg Me(string time, string text) => Add(time, ShowcaseArchiveWriter.Me, m => m.Text = text);
        public Msg They(string time, string text) => Add(time, null, m => m.Text = text);
        public Msg From(string who, string time, string text) => Add(time, who, m => m.Text = text);
        public Msg System(string time, string text) => Add(time, null, m => (m.Text, m.System) = (text, true));

        public Msg Photo(string time, string? sender, Scene scene, string? caption = null) =>
            Add(time, sender, m =>
            {
                m.MediaPath = writer.Photo(day, scene, chat.Random.Next());
                m.Mime = "image/png";
                m.Caption = caption;
            });

        public Msg Voice(string time, string? sender, int seconds) =>
            Add(time, sender, m => (m.MediaPath, m.Mime) = (writer.Voice(day, seconds), "audio/wav"));
    }

    // ---- the cast (all fictional) ----

    private static readonly string[] Climbers = ["Jonas Weber", "Aisha Rahman", "Sam Okoro", "Mia Lindqvist", "Daniel Kim"];
    private static readonly string[] FamilyMembers = ["Mama", "Papa", "Clara", "Oma Gerda"];
    private static readonly string[] Readers = ["Chloé Dubois", "Priya Raman", "Tomás Silva", "Elif Yılmaz"];
    private static readonly string[] Flatmates = ["Marco Bianchi", "Ingrid Ødegård", "Kenji Sato"];
    private static readonly string[] Travellers = ["Lena Hoffmann", "Marco Bianchi", "Aisha Rahman"];

    private static readonly (string Name, int Number, string? Avatar)[] People =
    [
        ("Lena Hoffmann", 101, "portrait:7"),
        ("Marco Bianchi", 102, "portrait:12"),
        ("Aisha Rahman", 103, "portrait:3"),
        ("Oma Gerda", 104, "portrait:21"),
        ("Sam Okoro", 105, "portrait:16"),
        ("Daniel Kim", 106, "scene:Mountains"),
        ("Chloé Dubois", 107, "scene:Sunset"),
        ("Mia Lindqvist", 108, "portrait:9"),
        ("Jonas Weber", 109, null),
        ("Bike Shop Kessler", 110, null),
        ("Dental Practice Berg", 111, null),
    ];

    private static readonly string[] LenaTheirs =
    [
        "Good morning ☀️", "Did you sleep well?", "Running 10 min late, sorry!", "Haha 😂", "Miss you", "Call me when you're free?",
        "Can you pick up bread on the way home?", "I'm at the gym, back around 8", "That's so sweet ❤️", "What do you feel like for dinner?",
        "Ugh, train is delayed again 🙄", "Look at this 😍", "Love you", "Home soon!", "Did you book the table?", "Ok 👍",
        "On my way", "I'll be home late, big meeting", "Don't forget your keys this time 😉", "Thinking of you", "Yes!!", "Sounds perfect",
        "Can you water the plants?", "Shall we watch the next episode tonight?", "I found the best bakery near work 🥐", "Good night 🌙",
        "Is it raining at yours too?", "Your mum called, call her back", "Coffee? ☕", "Just landed ✈️", "How did the presentation go?",
    ];

    private static readonly string[] LenaMine =
    [
        "Morning! 😊", "Yes, all good", "No worries", "Haha", "Miss you too", "Sure, will do", "Leaving the office now", "❤️", "Sounds good",
        "Pasta? 🍝", "Running a bit late", "Just got home", "Love you", "On my way", "Booked for 8!", "Ok", "Can't wait", "Pick you up at 7?",
        "Yes please", "Good luck today! You've got this 💪", "Haha true", "Good night 🌙", "The heating guy comes at 10", "Went well, tell you later",
        "Bringing ice cream 🍦", "Bus was packed", "I'll cook tonight", "Sure!", "Let's do it", "Where are you?", "Nearly there",
    ];

    private static readonly string[] SmallTalk =
    [
        "Hey, how are you?", "All good, you?", "Haha 😂", "Thanks!", "See you then 👋", "Sounds great", "Let me check and get back to you",
        "Happy birthday 🎉🎂", "Perfect", "Ok 👍", "Sorry, just saw this", "Next week works", "Congrats!! 🥳", "Where are you?", "Nearly there",
        "Did you see the match?", "Yes!", "Speak soon", "Have a good weekend ☀️", "Thank you ❤️", "No problem", "Good idea",
    ];

    private static readonly string[] CrewTalk =
    [
        "Gym tonight?", "In 💪", "Can't today, sorry", "Who has the rope?", "Bouldering at 7?", "My fingers are done 😅", "Nice send!!",
        "Forecast looks good for Saturday ☀️", "Rain again 🌧️", "Anyone want to split a car?", "That project is impossible", "Pizza after?",
        "Count me in", "Running late, start without me", "New routes at the gym 🔥", "Haha", "Who's driving?", "🙌",
    ];

    private static readonly string[] FamilyTalk =
    [
        "Guten Morgen ☀️", "Wie geht's euch?", "Sunday lunch at ours?", "Happy birthday Papa 🎉", "Has anyone seen my reading glasses?",
        "Photo from today", "Call me later?", "❤️", "The garden is blooming 🌷", "Safe travels!", "Merry Christmas everyone 🎄", "We're home 🏠",
        "Who's bringing dessert?", "Haha 😂", "Love you all", "Oma says hi 👋",
    ];

    public static DemoSummary Write(string root)
    {
        Directory.CreateDirectory(root);
        var writer = new Writer(root);
        var chats = new List<Chat>();
        var number = new Dictionary<string, int>();
        foreach (var p in People)
        {
            number[p.Name] = p.Number;
        }
        Chat Private(string name, int seed) => Add(new Chat($"447700900{number[name]:D3}@s.whatsapp.net", name, false, seed));
        Chat Group(string name, int id, int seed) => Add(new Chat($"120363-{id}@g.us", name, true, seed));
        Chat Add(Chat c)
        {
            chats.Add(c);
            return c;
        }
        Day On(Chat c, int y, int m, int d)
        {
            var day = new DateOnly(y, m, d);
            c.Scripted.Add(day);
            return new Day(c, day, writer);
        }

        // ---- Lena: eleven years, the hero chat ----
        var lena = Private(HeroChat, 1);
        var first = On(lena, 2015, 3, 14);
        first.They("19:42", "Hi, it's Lena from the climbing gym 👋 You forgot your chalk bag on the bench");
        first.Me("19:50", "Oh no, thank you! Are you there on Thursday?");
        first.They("19:51", "Every Thursday 😄 I'll bring it");
        first.Me("19:52", "You're a lifesaver. Coffee's on me then ☕");

        var before = On(lena, 2019, 6, 21);
        before.They("08:15", "Can't believe we're finally in Lisbon 🇵🇹");
        before.Photo("08:16", null, Scene.City);
        before.Me("08:30", "Breakfast at the place with the blue tiles?");
        before.They("08:31", "Already ordered you a galão ☕");

        var hero = On(lena, HeroDay.Year, HeroDay.Month, HeroDay.Day);
        hero.They("10:12", "Made it to Sintra 🚋 the palace is unreal");
        hero.Photo("10:13", null, Scene.Forest);
        hero.Me("10:20", "Jealous! Still stuck in the queue for tram 28 😅");
        var sunset = hero.They("10:24", "Meet you at Cabo da Roca for sunset? The lighthouse is supposed to be beautiful");
        var deal = hero.Me("10:25", "Deal. I'll bring the pastéis de nata 🥮").React(HeroChat, "❤️");
        hero.Photo("20:41", Me, Scene.Lighthouse, "Westernmost point of Europe ✨").React(HeroChat, "😍");
        hero.They("20:43", "You ate them all on the bus, didn't you?").Reply(deal);
        hero.Me("20:44", "Only four. Out of six.");
        hero.They("20:44", "😂😂");
        hero.Me("21:02", "Best day of the trip.").Reply(sunset);
        hero.They("21:03", "Let's come back every year ❤️");

        var later = On(lena, 2024, 6, 22);
        later.Photo("20:58", null, Scene.Lighthouse, "Back at the lighthouse. Five years later 🥹");
        later.Me("21:00", "Every year, as promised ❤️");

        var lenaLast = On(lena, 2026, 11, 20);
        lenaLast.Me("18:40", "Leaving the office now");
        lenaLast.They("18:47", "On my way too, need anything from the store? 🛒");

        Fill(lena, writer, new DateOnly(2015, 3, 15), new DateOnly(2026, 11, 19), LenaRate, LenaMine, LenaTheirs, [], 0.007);

        // ---- groups ----
        var crew = Group("Climbing crew 🧗", 2001, 2);
        On(crew, 2018, 4, 7).System("10:00", "Jonas Weber created group \"Climbing crew 🧗\"");
        On(crew, 2022, 5, 14).From("Daniel Kim", "07:45", "Lighthouse crag is dry today, anyone? 🧗");
        var invite = On(crew, 2026, 11, 15);
        invite.From("Jonas Weber", "08:02", "Who's in for Frankenjura tomorrow? Forecast says 14° and dry ☀️");
        invite.From("Aisha Rahman", "08:05", "In! I'll bring the 80 m rope");
        invite.From("Sam Okoro", "08:06", "Me too, but I need a lift 🙏");
        var seats = invite.Me("08:10", "I've got 2 free seats, leaving 7:30 from the station").React("Sam Okoro", "🙌");
        invite.From("Mia Lindqvist", "08:11", "Can I take the second one?").Reply(seats);
        invite.Me("08:12", "Sure!");
        var send = On(crew, 2026, 11, 16);
        send.Photo("16:40", "Aisha Rahman", Scene.Mountains, "Sam sending his project 🔥")
            .React("Jonas Weber", "🔥").React("Mia Lindqvist", "😍").React(Me, "👏");
        send.From("Daniel Kim", "16:42", "Nooo I missed it! Next weekend?");
        send.Voice("16:45", "Jonas Weber", 14);
        send.From("Sam Okoro", "16:47", "Couldn't have done it without Mia's beta 🙌");
        send.From("Mia Lindqvist", "17:03", "Pizza at Napoli tonight? 🍕");
        send.From("Jonas Weber", "17:04", "Always");
        On(crew, 2026, 11, 19).From("Jonas Weber", "21:30", "Same time next week? 💪");
        Fill(crew, writer, new DateOnly(2018, 4, 8), new DateOnly(2026, 11, 18), d => Seasonal(d, 1.5, summerBoost: 1.6), CrewTalk, CrewTalk, Climbers, 0.01);

        var family = Group("Family 👨‍👩‍👧", 2002, 3);
        On(family, 2017, 8, 12).From("Papa", "11:20", "Found Grandpa's old lighthouse postcard from Sylt 📮");
        var dinner = On(family, 2026, 11, 20);
        dinner.From("Clara", "18:58", "Is everyone coming on Sunday?");
        dinner.From("Mama", "19:12", "Dinner on Sunday? I'm making Rouladen 🍲");
        Fill(family, writer, new DateOnly(2016, 1, 1), new DateOnly(2026, 11, 19), d => Seasonal(d, 1.2, christmasBoost: 3), FamilyTalk, FamilyTalk, FamilyMembers, 0.02);

        var books = Group("Book club 📚", 2003, 4);
        On(books, 2020, 3, 5).From("Priya Raman", "20:15", "March pick: To the Lighthouse by Virginia Woolf 🌊");
        On(books, 2026, 11, 17).From("Chloé Dubois", "21:40", "Next pick: Klara and the Sun ☀️");
        Fill(books, writer, new DateOnly(2019, 9, 1), new DateOnly(2026, 11, 16), d => 0.6, SmallTalk, SmallTalk, Readers, 0);

        var flat = Group("Flat 3B 🏠", 2004, 5);
        On(flat, 2018, 3, 31).From("Ingrid Ødegård", "12:00", "Last one out, turn off the heating! 🔑");
        Fill(flat, writer, new DateOnly(2016, 2, 1), new DateOnly(2018, 3, 30), d => 2.5, SmallTalk, SmallTalk, Flatmates, 0.005);

        var lisbon = Group("Lisbon trip 2019 ✈️", 2005, 6);
        On(lisbon, 2019, 7, 2).From("Aisha Rahman", "19:30", "Photo dump incoming 📸");
        Fill(lisbon, writer, new DateOnly(2019, 4, 2), new DateOnly(2019, 7, 1), d => d.Month == 6 ? 9 : 2, SmallTalk, SmallTalk, Travellers, 0.04);

        // ---- private chats: their last message is what the chat list shows ----
        var marco = Private("Marco Bianchi", 7);
        On(marco, 2021, 9, 3).From("Marco Bianchi", "18:20", "Staying at the lighthouse hostel in Porto, you'd love it");
        var risotto = On(marco, 2026, 11, 19);
        risotto.They("11:58", "Made nonna's risotto last night, finally got it right 🍚");
        risotto.Me("12:05", "Grazie! Send me the recipe 🍝");
        Fill(marco, writer, new DateOnly(2016, 2, 1), new DateOnly(2026, 11, 18), d => 0.5, SmallTalk, SmallTalk, [], 0.01);

        var aisha = Private("Aisha Rahman", 8);
        On(aisha, 2026, 11, 18).They("17:22", "The tickets are booked 🎟️");
        Fill(aisha, writer, new DateOnly(2018, 4, 1), new DateOnly(2026, 11, 17), d => 0.4, SmallTalk, SmallTalk, [], 0.005);

        var oma = Private("Oma Gerda", 9);
        var omaLast = On(oma, 2026, 11, 16);
        omaLast.Me("15:10", "Alles Gute zum Geburtstag, Oma! 🎂🌷");
        omaLast.They("16:02", "Danke mein Schatz ❤️");
        Fill(oma, writer, new DateOnly(2017, 1, 1), new DateOnly(2026, 11, 15), d => 0.15, FamilyTalk, FamilyTalk, [], 0.03);

        var sam = Private("Sam Okoro", 10);
        On(sam, 2026, 11, 14).They("22:10", "Haha classic 😂");
        Fill(sam, writer, new DateOnly(2019, 1, 1), new DateOnly(2026, 11, 13), d => 0.3, SmallTalk, SmallTalk, [], 0);

        var daniel = Private("Daniel Kim", 11);
        On(daniel, 2026, 11, 10).Me("09:30", "Thanks for the photos! 📸");
        Fill(daniel, writer, new DateOnly(2020, 6, 1), new DateOnly(2026, 11, 9), d => 0.2, SmallTalk, SmallTalk, [], 0.01);

        var chloe = Private("Chloé Dubois", 12);
        On(chloe, 2026, 11, 2).They("23:05", "Bonne nuit 🌙");
        Fill(chloe, writer, new DateOnly(2019, 9, 1), new DateOnly(2026, 11, 1), d => 0.2, SmallTalk, SmallTalk, [], 0);

        var mia = Private("Mia Lindqvist", 13);
        On(mia, 2026, 10, 28).Me("13:15", "See you at the gym!");
        Fill(mia, writer, new DateOnly(2024, 2, 1), new DateOnly(2026, 10, 27), d => 0.25, CrewTalk, CrewTalk, [], 0);

        var jonas = Private("Jonas Weber", 14);
        On(jonas, 2026, 8, 30).They("10:00", "Happy birthday 🎉🎂");
        Fill(jonas, writer, new DateOnly(2018, 4, 1), new DateOnly(2026, 8, 29), d => 0.15, SmallTalk, SmallTalk, [], 0);

        var bikes = Private("Bike Shop Kessler", 15);
        On(bikes, 2026, 10, 21).They("16:30", "Your bike is ready for pickup 🚲");

        var dentist = Private("Dental Practice Berg", 16);
        On(dentist, 2026, 9, 30).They("09:00", "Reminder: your appointment is on Oct 4 at 9:30");

        var calls = Add(new Chat(DemoArchiveWriter.CallsJid, "Calls", false, 17));
        var callRandom = new Random(17);
        for (var i = 0; i < 40; i++)
        {
            var day = new DateOnly(2026, 11, 17).AddDays(-i * 9 - callRandom.Next(5));
            var text = callRandom.Next(4) switch
            {
                0 => "Missed voice call",
                1 => $"Video call ({callRandom.Next(5, 60)} min)",
                _ => $"Voice call ({callRandom.Next(1, 40)} min)",
            };
            calls.Messages.Add(new Msg { Ts = At(day, $"{callRandom.Next(8, 22):D2}:{callRandom.Next(60):D2}"), Sender = callRandom.Next(2) == 0 ? Me : null, Text = text, Call = true });
        }

        var messages = 0;
        foreach (var chat in chats)
        {
            messages += chat.Messages.Count;
            WriteFile(root, $"json/{chat.Jid.Split('@')[0]}.json", Serialize(chat));
        }
        WriteFile(root, "contacts.vcf", Contacts());
        return new DemoSummary(chats.Count, messages, writer.Media, 0);
    }

    // ---- activity ----

    /// <summary>Messages per day with Lena: getting to know each other, dating, moving in, lockdown, time apart.</summary>
    private static double LenaRate(DateOnly d)
    {
        var rate = d.Year switch
        {
            2015 => 2 + Math.Max(0, d.Month - 3) * 2.6,
            2016 or 2017 => 20,
            2018 => d.Month <= 3 ? 16 : 6,
            2019 => d.Month == 6 ? 18 : d.Month == 7 ? 3 : 6,
            2020 => d.Month is >= 3 and <= 5 ? 1.5 : 6,
            2021 => 7,
            2022 => d.Month is 10 or 11 ? 16 : 7,
            2023 => d.Month >= 9 ? 24 : 7,
            2024 => d.Month == 6 ? 11 : 8,
            _ => 8,
        };
        return d.Month == 12 ? rate * 1.25 : rate;
    }

    private static double Seasonal(DateOnly d, double baseRate, double summerBoost = 1, double christmasBoost = 1)
    {
        var rate = baseRate * (d.Month is >= 5 and <= 9 ? summerBoost : 1);
        return d.Month == 12 && d.Day >= 18 ? rate * christmasBoost : rate;
    }

    /// <summary>Everyday messages on every day without a scripted scene, in short conversational bursts.</summary>
    private static void Fill(Chat chat, Writer writer, DateOnly from, DateOnly to, Func<DateOnly, double> rate,
        string[] mine, string[] theirs, string[] senders, double photoShare)
    {
        var r = chat.Random;
        Scene[] scenes = [Scene.Sunset, Scene.Mountains, Scene.Sea, Scene.City, Scene.Forest, Scene.Night];
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (chat.Scripted.Contains(day))
            {
                continue;
            }
            var mood = Math.Exp((r.NextDouble() + r.NextDouble() + r.NextDouble() - 1.5) * 1.1); // lumpy days
            var weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 1.2 : 1;
            var count = Poisson(r, rate(day) * mood * weekend);
            var minute = 7 * 60 + r.Next(60);
            for (var i = 0; i < count && minute < 23 * 60 + 50; i++)
            {
                minute += r.Next(5) == 0 ? r.Next(30, 240) : r.Next(0, 4); // a new conversation now and then
                if (minute > 23 * 60 + 59)
                {
                    break;
                }
                var mineNow = r.Next(2) == 0;
                var sender = mineNow ? Me : senders.Length > 0 ? senders[r.Next(senders.Length)] : null;
                var m = new Msg { Ts = At(day, minute), Sender = sender };
                if (r.NextDouble() < photoShare)
                {
                    m.MediaPath = writer.Photo(day, scenes[r.Next(scenes.Length)], r.Next());
                    m.Mime = "image/png";
                }
                else
                {
                    var pool = mineNow ? mine : theirs;
                    m.Text = pool[r.Next(pool.Length)];
                }
                if (r.Next(60) == 0)
                {
                    var other = senders.Length > 0 ? senders[r.Next(senders.Length)] : chat.Name;
                    m.Reactions.Add((mineNow ? other : Me, r.Next(2) == 0 ? "❤️" : "😂"));
                }
                m.Edited = r.Next(150) == 0;
                chat.Messages.Add(m);
            }
        }
    }

    private static int Poisson(Random r, double lambda)
    {
        if (lambda <= 0)
        {
            return 0;
        }
        if (lambda > 30)
        {
            var gauss = Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble());
            return Math.Max(0, (int)Math.Round(lambda + gauss * Math.Sqrt(lambda)));
        }
        var limit = Math.Exp(-lambda);
        var k = 0;
        var p = r.NextDouble();
        while (p > limit)
        {
            k++;
            p *= r.NextDouble();
        }
        return k;
    }

    private static long At(DateOnly day, string time)
    {
        var parts = time.Split(':');
        return At(day, int.Parse(parts[0], CultureInfo.InvariantCulture) * 60 + int.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    private static long At(DateOnly day, int minuteOfDay)
    {
        var local = day.ToDateTime(new TimeOnly(minuteOfDay / 60, minuteOfDay % 60));
        return new DateTimeOffset(local, Zone.GetUtcOffset(local)).ToUnixTimeSeconds();
    }

    // ---- output ----

    private static byte[] Serialize(Chat chat)
    {
        var ordered = chat.Messages.OrderBy(m => m.Ts).ToList();
        var keys = new Random(chat.Seed * 7919 + 1);
        foreach (var m in ordered)
        {
            m.KeyId = $"3EB0{keys.NextInt64(0, long.MaxValue):X16}";
        }
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, WriterOptions))
        {
            w.WriteStartObject();
            w.WriteStartObject(chat.Jid);
            w.WriteString("name", chat.Name);
            w.WriteString("type", "android");
            w.WriteNull("my_avatar");
            w.WriteNull("their_avatar");
            w.WriteNull("their_avatar_thumb");
            w.WriteNull("status");
            w.WriteString("media_base", "");
            w.WriteStartObject("messages");
            for (var i = 0; i < ordered.Count; i++)
            {
                var m = ordered[i];
                w.WriteStartObject((i + 1).ToString(CultureInfo.InvariantCulture));
                var fromMe = m.Sender == Me;
                w.WriteBoolean("from_me", fromMe);
                w.WriteNumber("timestamp", m.Ts);
                w.WriteString("time", TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(m.Ts), Zone).ToString("HH:mm", CultureInfo.InvariantCulture));
                w.WriteString("key_id", m.KeyId);
                w.WriteNumber("message_type", m.System ? 7 : 0);
                w.WriteNumber("received_timestamp", m.Ts + 2);
                w.WriteNumber("read_timestamp", m.Ts + 60);
                w.WriteBoolean("safe", false);
                w.WriteBoolean("sticker", false);
                w.WriteNull("thumb");
                if (chat.Group && !fromMe && m.Sender is not null && !m.System)
                {
                    w.WriteString("sender", m.Sender);
                }
                else
                {
                    w.WriteNull("sender");
                }
                w.WriteBoolean("media", m.MediaPath is not null);
                w.WriteBoolean("meta", m.System || m.Call);
                w.WriteString("data", m.MediaPath ?? m.Text);
                if (m.Mime is null)
                {
                    w.WriteNull("mime");
                }
                else
                {
                    w.WriteString("mime", m.Mime);
                }
                if (m.Caption is null)
                {
                    w.WriteNull("caption");
                }
                else
                {
                    w.WriteString("caption", m.Caption);
                }
                if (m.ReplyTo is { } to)
                {
                    w.WriteString("reply", to.KeyId);
                    w.WriteString("quoted_data", to.Text ?? to.Caption ?? "📷 Photo");
                }
                else
                {
                    w.WriteNull("reply");
                    w.WriteNull("quoted_data");
                }
                w.WriteStartObject("reactions");
                foreach (var (who, emoji) in m.Reactions)
                {
                    w.WriteString(who == Me ? "You" : who, emoji);
                }
                w.WriteEndObject();
                w.WriteBoolean("edited", m.Edited);
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return buffer.ToArray();
    }

    /// <summary>The address book users export from their phone: names and profile pictures by number.</summary>
    private static byte[] Contacts()
    {
        var sb = new StringBuilder();
        foreach (var (name, number, avatar) in People)
        {
            sb.Append("BEGIN:VCARD\r\nVERSION:3.0\r\n");
            sb.Append(CultureInfo.InvariantCulture, $"FN:{name}\r\nTEL;TYPE=CELL:+44 7700 900{number:D3}\r\n");
            if (avatar is not null)
            {
                var (w, h, rgb) = avatar.StartsWith("portrait:", StringComparison.Ordinal)
                    ? SceneRenderer.Portrait(int.Parse(avatar["portrait:".Length..], CultureInfo.InvariantCulture))
                    : SceneRenderer.Landscape(Enum.Parse<Scene>(avatar["scene:".Length..]), number, 192, 192);
                sb.Append("PHOTO;ENCODING=b;TYPE=PNG:").Append(Convert.ToBase64String(PngEncoder.Encode(w, h, rgb))).Append("\r\n");
            }
            sb.Append("END:VCARD\r\n");
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] SilentWav(int seconds)
    {
        const int rate = 8000;
        var samples = rate * seconds;
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write("RIFF"u8);
        bw.Write(36 + samples);
        bw.Write("WAVEfmt "u8);
        bw.Write(16);
        bw.Write((short)1); // PCM
        bw.Write((short)1); // mono
        bw.Write(rate);
        bw.Write(rate); // byte rate (8-bit mono)
        bw.Write((short)1);
        bw.Write((short)8);
        bw.Write("data"u8);
        bw.Write(samples);
        bw.Write(Enumerable.Repeat((byte)128, samples).ToArray());
        bw.Flush();
        return ms.ToArray();
    }

    private static void WriteFile(string root, string relative, byte[] content)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }
}
