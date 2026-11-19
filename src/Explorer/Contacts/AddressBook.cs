using System.Text;

namespace ArchiveExplorer.Contacts;

/// <summary>
/// Names and photos from an address-book export (.vcf, vCard 2.1/3.0/4.0 as written by Android/iOS/Google Contacts),
/// keyed by phone number in international form. WhatsApp backups do not contain contact names or profile pictures,
/// so this is where they come from. Read once, kept in memory only; the file is never modified or copied.
/// </summary>
public sealed class AddressBook
{
    public readonly record struct Photo(byte[] Bytes, string Mime);

    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Photo> _photos = new(StringComparer.Ordinal);

    public static readonly AddressBook Empty = new();

    /// <summary>Cards that had a name (counts only — for the startup summary).</summary>
    public int Contacts { get; private set; }

    public int Numbers => _names.Count;

    public int Photos { get; private set; }

    /// <summary>Numbers written nationally ("0155…") that could not be used because no country code was given.</summary>
    public int NationalNumbersIgnored { get; private set; }

    /// <param name="countryCode">Digits of the country code for numbers written nationally ("0155…"), e.g. "49"; null = such numbers are ignored.</param>
    public static AddressBook Load(string path, string? countryCode)
    {
        var book = new AddressBook();
        var card = new List<(string Name, Dictionary<string, string> Params, string Value)>();
        var inCard = false;
        foreach (var line in Unfold(File.ReadAllText(path)))
        {
            if (line.Equals("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase))
            {
                card.Clear(); // an unterminated card before is dropped
                inCard = true;
                continue;
            }
            if (line.Equals("END:VCARD", StringComparison.OrdinalIgnoreCase))
            {
                if (inCard)
                {
                    book.Add(card, countryCode);
                }
                inCard = false;
                continue;
            }
            if (inCard && Parse(line) is { } property)
            {
                card.Add(property);
            }
        }
        return book;
    }

    /// <summary>Name for a WhatsApp chat id such as "4915550100001@s.whatsapp.net".</summary>
    public string? NameFor(string jid)
    {
        var user = jid.Split('@')[0];
        return user.All(char.IsAsciiDigit) && _names.TryGetValue(user, out var name) ? name : null;
    }

    public Photo? PhotoFor(string jid)
    {
        var user = jid.Split('@')[0];
        return _photos.TryGetValue(user, out var photo) ? photo : null;
    }

    /// <summary>Group senders and reactions come as display names or numbers; only numbers are looked up.</summary>
    public string? NameForSender(string? sender)
    {
        if (string.IsNullOrWhiteSpace(sender) || sender.Any(char.IsLetter))
        {
            return null;
        }
        var digits = new string(sender.Where(char.IsAsciiDigit).ToArray());
        return digits.Length >= 6 && _names.TryGetValue(digits, out var name) ? name : null;
    }

    private void Add(List<(string Name, Dictionary<string, string> Params, string Value)> card, string? countryCode)
    {
        string? name = null;
        string? fromN = null;
        Photo? photo = null;
        var numbers = new List<string>();
        foreach (var (prop, parameters, raw) in card)
        {
            switch (prop)
            {
                case "FN":
                    name = Text(raw, parameters);
                    break;
                case "N":
                    var parts = Text(raw, parameters, keepSemicolons: true).Split(';');
                    fromN = string.Join(' ', new[] { parts.ElementAtOrDefault(3), parts.ElementAtOrDefault(1), parts.ElementAtOrDefault(2), parts.ElementAtOrDefault(0), parts.ElementAtOrDefault(4) }
                        .Where(p => !string.IsNullOrWhiteSpace(p))).Trim();
                    break;
                case "TEL":
                    if (Normalize(raw, countryCode) is { } number)
                    {
                        numbers.Add(number);
                    }
                    else if (countryCode is null && Normalize(raw, "0") is not null)
                    {
                        NationalNumbersIgnored++;
                    }
                    break;
                case "PHOTO":
                    photo ??= DecodePhoto(raw, parameters);
                    break;
            }
        }
        name = string.IsNullOrWhiteSpace(name) ? fromN : name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        Contacts++;
        foreach (var number in numbers)
        {
            _names.TryAdd(number, name);
            if (photo is { } p && _photos.TryAdd(number, p))
            {
                Photos++;
            }
        }
    }

    /// <summary>"+49 (155) 501-00002", "0049…", "tel:+81-90-…", "0155 …" (with country code) → "4915550100002".</summary>
    internal static string? Normalize(string raw, string? countryCode)
    {
        var value = raw.Trim();
        if (value.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
        {
            value = value[4..];
        }
        var plus = value.StartsWith('+');
        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length < 6)
        {
            return null;
        }
        if (plus)
        {
            return digits;
        }
        if (digits.StartsWith("00", StringComparison.Ordinal))
        {
            return digits[2..];
        }
        if (digits.StartsWith('0'))
        {
            return countryCode is null ? null : countryCode + digits[1..];
        }
        return digits; // already international without "+"
    }

    private static Photo? DecodePhoto(string raw, Dictionary<string, string> parameters)
    {
        string? base64 = null;
        var value = raw.Trim();
        if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = value.IndexOf(',', StringComparison.Ordinal);
            if (comma > 0 && value[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
            {
                base64 = value[(comma + 1)..];
            }
        }
        else if (parameters.TryGetValue("ENCODING", out var encoding) && encoding.ToUpperInvariant() is "B" or "BASE64")
        {
            base64 = value;
        }
        if (base64 is null)
        {
            return null; // remote URLs are never fetched
        }
        try
        {
            var bytes = Convert.FromBase64String(new string(base64.Where(c => !char.IsWhiteSpace(c)).ToArray()));
            var mime = bytes switch
            {
                [0xFF, 0xD8, ..] => "image/jpeg",
                [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
                [0x47, 0x49, 0x46, ..] => "image/gif",
                [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
                _ => null,
            };
            return mime is null ? null : new Photo(bytes, mime);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string Text(string raw, Dictionary<string, string> parameters, bool keepSemicolons = false)
    {
        var value = raw;
        if (parameters.TryGetValue("ENCODING", out var encoding) && encoding.Equals("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase))
        {
            var charset = parameters.TryGetValue("CHARSET", out var cs) ? cs : "UTF-8";
            Encoding enc;
            try
            {
                enc = Encoding.GetEncoding(charset);
            }
            catch (ArgumentException)
            {
                enc = Encoding.UTF8;
            }
            value = DecodeQuotedPrintable(value, enc);
        }
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                var next = value[++i];
                sb.Append(next switch
                {
                    'n' or 'N' => " ",
                    ';' when keepSemicolons => "\\;",
                    _ => next.ToString(),
                });
            }
            else
            {
                sb.Append(value[i]);
            }
        }
        return sb.ToString();
    }

    private static string DecodeQuotedPrintable(string value, Encoding encoding)
    {
        var bytes = new List<byte>(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '=' && i + 2 < value.Length && Uri.IsHexDigit(value[i + 1]) && Uri.IsHexDigit(value[i + 2]))
            {
                bytes.Add(Convert.ToByte(value.Substring(i + 1, 2), 16));
                i += 2;
            }
            else
            {
                bytes.AddRange(encoding.GetBytes(value[i].ToString()));
            }
        }
        return encoding.GetString(bytes.ToArray());
    }

    /// <summary>Undoes line folding (continuation lines start with a space/tab) and quoted-printable soft breaks ("=" at the end).</summary>
    private static IEnumerable<string> Unfold(string text)
    {
        var current = new StringBuilder();
        var qpContinuation = false;
        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (current.Length > 0 && (qpContinuation || rawLine.StartsWith(' ') || rawLine.StartsWith('\t')))
            {
                current.Append(qpContinuation ? rawLine : rawLine[1..]);
            }
            else
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                }
                current.Clear().Append(rawLine);
            }
            var line = current.ToString();
            qpContinuation = line.Contains("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase) && line.EndsWith('=');
            if (qpContinuation)
            {
                current.Length--; // drop the soft break "="
            }
        }
        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static (string Name, Dictionary<string, string> Params, string Value)? Parse(string line)
    {
        var colon = line.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
        {
            return null;
        }
        var head = line[..colon].Split(';');
        var name = head[0];
        var group = name.LastIndexOf('.');
        if (group >= 0)
        {
            name = name[(group + 1)..]; // "item1.TEL" → "TEL"
        }
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in head.Skip(1))
        {
            var eq = p.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0)
            {
                parameters[p[..eq]] = p[(eq + 1)..].Trim('"');
            }
            else if (p.Equals("QUOTED-PRINTABLE", StringComparison.OrdinalIgnoreCase) || p.Equals("BASE64", StringComparison.OrdinalIgnoreCase))
            {
                parameters["ENCODING"] = p; // vCard 2.1 shorthand
            }
        }
        return (name.ToUpperInvariant(), parameters, line[(colon + 1)..]);
    }
}
