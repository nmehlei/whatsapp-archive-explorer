using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ArchiveExplorer.Demo;

/// <summary>Options for a synthetic export. The same options always produce byte-identical files.</summary>
public sealed record DemoOptions(
    int Seed = 42,
    int Chats = 12,
    int MessagesPerChat = 200,
    bool PerChat = true,
    bool WithRecordExtras = true);

public sealed record DemoSummary(int Chats, int Messages, int MediaFiles, int VCards);

/// <summary>
/// Writes a synthetic WhatsApp-Chat-Exporter export (JSON + media) with fictional people only.
/// Shapes follow WhatsApp-Chat-Exporter 0.13 (see docs/format.md); never put real chats into tests.
/// </summary>
public static class DemoArchiveWriter
{
    /// <summary>A phrase that appears exactly once, in a known chat, for search tests.</summary>
    public const string UniquePhrase = "lighthouse picnic";

    public const string CallsJid = "000000000000000";

    private static readonly string[] People =
    [
        "Anna Becker", "Jürgen Müller", "Zoë Laurent", "Mateo García", "Ingrid Ødegård", "Kenji Sato",
        "Fatima Haddad", "Liam O'Connor", "Sofia Rossi", "Noah Schäfer", "Amara Okafor", "Elif Yılmaz",
        "Lucas Martin", "Maja Nowak", "Priya Raman", "Tomás Silva",
    ];

    private static readonly string[] Groups =
    [
        "Family 👨‍👩‍👧", "Climbing crew", "Book club", "Neighbours", "Uni friends",
    ];

    private static readonly string[] Phrases =
    [
        "Good morning!", "Are we still on for tonight?", "Running ten minutes late, sorry", "Haha 😂",
        "Can you send me the address?", "Happy birthday 🎉🎂", "That sounds great", "See you at the station",
        "Did you see the match yesterday?", "I'll bring the cake", "Thanks a lot ❤️", "Let me check and get back to you",
        "Weather looks perfect for the weekend ☀️", "Where did we park the car?", "Grüße aus München!",
        "Ça va? On se voit demain?", "Don't forget the umbrella ☔", "Photo from today", "Call me when you can",
        "Straße gesperrt, nimm die Umleitung", "Who has the key?", "Dinner is ready 🍝", "Good night 🌙",
    ];

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static DemoSummary Write(string root, DemoOptions options)
    {
        var random = new Random(options.Seed);
        Directory.CreateDirectory(root);
        var chats = new List<(string Jid, string FileName, byte[] Json)>();
        var media = 0;
        var vcards = 0;
        var messages = 0;
        var start = new DateTimeOffset(2014, 1, 1, 8, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var end = new DateTimeOffset(2026, 11, 1, 20, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var groupCount = Math.Min(Groups.Length, Math.Max(1, options.Chats / 4));

        for (var c = 0; c < options.Chats; c++)
        {
            string jid, name, kind;
            if (c == 0)
            {
                (jid, name, kind) = (CallsJid, "Calls", "calls");
            }
            else if (c <= groupCount)
            {
                (jid, name, kind) = ($"120363-{1000 + c}@g.us", Groups[(c - 1) % Groups.Length], "group");
            }
            else
            {
                var person = People[(c - 1 - groupCount) % People.Length];
                (jid, name, kind) = ($"49155501{c:D5}@s.whatsapp.net", person, "private");
            }

            var count = kind == "calls" ? Math.Min(10, options.MessagesPerChat) : options.MessagesPerChat;
            using var buffer = new MemoryStream();
            using (var w = new Utf8JsonWriter(buffer, WriterOptions))
            {
                w.WriteStartObject();
                w.WriteStartObject(jid);
                w.WriteString("name", name);
                w.WriteString("type", "android");
                w.WriteNull("my_avatar");
                if (kind == "private" && c % 3 == 0)
                {
                    var avatar = $"WhatsApp/Avatars/{jid.Split('@')[0]}.png";
                    WriteFile(root, avatar, Png(random, 24));
                    w.WriteString("their_avatar", avatar);
                }
                else
                {
                    w.WriteNull("their_avatar");
                }
                w.WriteNull("their_avatar_thumb");
                w.WriteNull("status");
                w.WriteString("media_base", "");
                w.WriteStartObject("messages");

                var ts = start + random.NextInt64(0, (end - start) / 2);
                var step = Math.Max(60, (end - ts) / Math.Max(1, count));
                var keyIds = new List<string>();
                for (var m = 0; m < count; m++)
                {
                    ts = Math.Min(ts + random.NextInt64(30, step * 2), end - (count - m) * 60L); // stay inside the demo period
                    var id = (c * 100_000 + m + 1).ToString(CultureInfo.InvariantCulture);
                    var keyId = $"3EB0{random.NextInt64(0, long.MaxValue):X16}";
                    keyIds.Add(keyId);
                    var fromMe = random.Next(2) == 0;
                    var sender = kind == "group" && !fromMe ? People[random.Next(People.Length)] : null;
                    var roll = random.Next(100);

                    w.WriteStartObject(id);
                    WriteCommon(w, fromMe, ts, keyId, roll < 2 && kind == "group" ? 7 : 0, sender);
                    if (kind == "calls")
                    {
                        WriteText(w, random.Next(3) == 0 ? "Missed voice call" : $"Voice call ({random.Next(1, 40)} min)", meta: true);
                    }
                    else if (c == groupCount + 1 && m == count / 2)
                    {
                        WriteText(w, $"Shall we do the {UniquePhrase} on Sunday?");
                    }
                    else if (roll < 2 && kind == "group")
                    {
                        WriteText(w, $"{People[random.Next(People.Length)]} joined using this group's invite link", meta: true);
                    }
                    else if (roll < 10)
                    {
                        var path = $"WhatsApp/Media/WhatsApp Images/IMG-{Date(ts)}-WA{c:D2}{m:D4}.png";
                        WriteFile(root, path, Png(random, 32));
                        media++;
                        WriteMedia(w, path, "image/png", random.Next(3) == 0 ? "Photo from today" : null);
                    }
                    else if (roll < 13)
                    {
                        var path = $"WhatsApp/Media/WhatsApp Voice Notes/{Date(ts)[..6]}/PTT-{Date(ts)}-WA{c:D2}{m:D4}.opus";
                        WriteFile(root, path, Encoding.ASCII.GetBytes("OggS-demo-voice-note"));
                        media++;
                        WriteMedia(w, path, "audio/ogg", null);
                    }
                    else if (roll < 15)
                    {
                        var path = $"WhatsApp/Media/WhatsApp Documents/Notes-{c}-{m}.pdf";
                        WriteFile(root, path, Encoding.ASCII.GetBytes("%PDF-1.4\n% demo document\n%%EOF\n"));
                        media++;
                        WriteMedia(w, path, "application/pdf", null);
                    }
                    else if (roll < 16)
                    {
                        var person = People[random.Next(People.Length)];
                        var file = $"WhatsApp/vCards/{id}-{person.Replace(" ", "", StringComparison.Ordinal)}.vcf";
                        WriteFile(root, file, Encoding.UTF8.GetBytes($"BEGIN:VCARD\r\nVERSION:3.0\r\nFN:{person}\r\nTEL:+49155501{random.Next(10000, 99999)}\r\nEND:VCARD\r\n"));
                        vcards++;
                        w.WriteBoolean("media", false);
                        w.WriteBoolean("meta", true);
                        w.WriteString("data", $"This media include the following vCard file(s):<br><a href=\"{file}\">{person}</a>");
                        w.WriteString("mime", "text/x-vcard");
                        w.WriteNull("caption");
                    }
                    else
                    {
                        WriteText(w, Phrases[random.Next(Phrases.Length)]);
                    }

                    var reply = kind != "calls" && m > 3 && random.Next(10) == 0;
                    if (reply)
                    {
                        w.WriteString("reply", keyIds[random.Next(keyIds.Count - 1)]);
                        w.WriteString("quoted_data", Phrases[random.Next(Phrases.Length)]);
                    }
                    else
                    {
                        w.WriteNull("reply");
                        w.WriteNull("quoted_data");
                    }
                    w.WriteStartObject("reactions");
                    if (kind != "calls" && random.Next(15) == 0)
                    {
                        w.WriteString(People[random.Next(People.Length)], random.Next(2) == 0 ? "❤️" : "😂");
                    }
                    w.WriteEndObject();
                    var edited = kind != "calls" && random.Next(40) == 0;
                    w.WriteBoolean("edited", edited);
                    if (options.WithRecordExtras && edited)
                    {
                        // record format: archived original on top, later versions oldest first
                        w.WriteStartArray("revisions");
                        w.WriteStartObject();
                        WriteCommon(w, fromMe, ts + 120, keyId, 0, sender);
                        WriteText(w, "This message was deleted");
                        w.WriteEndObject();
                        w.WriteEndArray();
                    }
                    w.WriteEndObject();
                    messages++;

                    if (options.WithRecordExtras && kind == "private" && m == 5)
                    {
                        // record format: exporter id reused after a restore, kept as "<id>~<key_id>"
                        var otherKey = $"3EB0{random.NextInt64(0, long.MaxValue):X16}";
                        w.WriteStartObject($"{id}~{otherKey}");
                        WriteCommon(w, !fromMe, ts + 30, otherKey, 0, null);
                        WriteText(w, "Message from after a restore");
                        w.WriteNull("reply");
                        w.WriteNull("quoted_data");
                        w.WriteStartObject("reactions");
                        w.WriteEndObject();
                        w.WriteBoolean("edited", false);
                        w.WriteEndObject();
                        messages++;
                    }
                }

                w.WriteEndObject();
                w.WriteEndObject();
                w.WriteEndObject();
            }
            chats.Add((jid, $"{jid.Split('@')[0]}.json", buffer.ToArray()));
        }

        if (options.PerChat)
        {
            foreach (var (_, fileName, json) in chats)
            {
                WriteFile(root, $"json/{fileName}", json);
            }
        }
        else
        {
            using var all = new MemoryStream();
            all.WriteByte((byte)'{');
            for (var i = 0; i < chats.Count; i++)
            {
                var inner = chats[i].Json.AsSpan(1, chats[i].Json.Length - 2); // strip outer braces
                if (i > 0)
                {
                    all.WriteByte((byte)',');
                }
                all.Write(inner);
            }
            all.WriteByte((byte)'}');
            WriteFile(root, "result.json", all.ToArray());
        }

        return new DemoSummary(chats.Count, messages, media, vcards);
    }

    private static void WriteCommon(Utf8JsonWriter w, bool fromMe, long ts, string keyId, int type, string? sender)
    {
        w.WriteBoolean("from_me", fromMe);
        w.WriteNumber("timestamp", ts);
        w.WriteString("time", DateTimeOffset.FromUnixTimeSeconds(ts).ToString("HH:mm", CultureInfo.InvariantCulture));
        w.WriteString("key_id", keyId);
        w.WriteNumber("message_type", type);
        w.WriteNumber("received_timestamp", ts + 2);
        w.WriteNumber("read_timestamp", ts + 60);
        w.WriteBoolean("safe", false);
        w.WriteBoolean("sticker", false);
        w.WriteNull("thumb");
        if (sender is null)
        {
            w.WriteNull("sender");
        }
        else
        {
            w.WriteString("sender", sender);
        }
    }

    private static void WriteText(Utf8JsonWriter w, string text, bool meta = false)
    {
        w.WriteBoolean("media", false);
        w.WriteBoolean("meta", meta);
        w.WriteString("data", text);
        w.WriteNull("mime");
        w.WriteNull("caption");
    }

    private static void WriteMedia(Utf8JsonWriter w, string path, string mime, string? caption)
    {
        w.WriteBoolean("media", true);
        w.WriteBoolean("meta", false);
        w.WriteString("data", path);
        w.WriteString("mime", mime);
        if (caption is null)
        {
            w.WriteNull("caption");
        }
        else
        {
            w.WriteString("caption", caption);
        }
    }

    private static string Date(long ts) =>
        DateTimeOffset.FromUnixTimeSeconds(ts).ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private static void WriteFile(string root, string relative, byte[] content)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    /// <summary>A tiny valid PNG with a random colour gradient (no image library needed).</summary>
    private static byte[] Png(Random random, int size)
    {
        var (r, g, b) = ((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
        var raw = new byte[size * (size * 3 + 1)];
        for (var y = 0; y < size; y++)
        {
            var row = y * (size * 3 + 1);
            raw[row] = 0; // filter: none
            for (var x = 0; x < size; x++)
            {
                raw[row + 1 + x * 3] = (byte)(r + x * 4);
                raw[row + 2 + x * 3] = (byte)(g + y * 4);
                raw[row + 3 + x * 3] = b;
            }
        }

        using var idat = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            z.Write(raw);
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)size);
        WriteBigEndian(header, 4, (uint)size);
        header[8] = 8; // bit depth
        header[9] = 2; // colour type: RGB
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", idat.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, (uint)data.Length);
        s.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        var crc = new byte[4];
        WriteBigEndian(crc, 0, Crc32([.. typeBytes, .. data]));
        s.Write(crc);
    }

    private static void WriteBigEndian(byte[] target, int offset, uint value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }
        return ~crc;
    }
}
