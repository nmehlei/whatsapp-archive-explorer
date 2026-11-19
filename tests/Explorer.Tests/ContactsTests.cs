using ArchiveExplorer.Contacts;

namespace ArchiveExplorer.Tests;

public sealed class ContactsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("wae-contacts-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC";

    private AddressBook Load(string vcf, string? countryCode = "49")
    {
        var path = Path.Combine(_dir, "contacts.vcf");
        File.WriteAllText(path, vcf.Replace("\n", "\r\n", StringComparison.Ordinal));
        return AddressBook.Load(path, countryCode);
    }

    [Fact]
    public void Reads_vCard21_QuotedPrintable_FromAndroid()
    {
        var book = Load("""
            BEGIN:VCARD
            VERSION:2.1
            N;CHARSET=UTF-8;ENCODING=QUOTED-PRINTABLE:M=C3=BCller;J=C3=BCrgen;;;
            FN;CHARSET=UTF-8;ENCODING=QUOTED-PRINTABLE:J=C3=BCrgen M=C3=BCller
            TEL;CELL:0155 501 00001
            TEL;HOME:+49 30 1234567
            END:VCARD
            """);

        Assert.Equal("Jürgen Müller", book.NameFor("4915550100001@s.whatsapp.net"));
        Assert.Equal("Jürgen Müller", book.NameFor("49301234567@s.whatsapp.net"));
        Assert.Null(book.NameFor("4915550199999@s.whatsapp.net"));
    }

    [Fact]
    public void Reads_vCard30_WithFoldedBase64Photo()
    {
        var folded = string.Join("\n ", Enumerable.Range(0, (TinyPngBase64.Length + 39) / 40)
            .Select(i => TinyPngBase64.Substring(i * 40, Math.Min(40, TinyPngBase64.Length - i * 40))));
        var book = Load($"""
            BEGIN:VCARD
            VERSION:3.0
            FN:Anna Becker
            TEL;TYPE=CELL:+49 (155) 501-00002
            PHOTO;ENCODING=b;TYPE=PNG:{folded}
            END:VCARD
            """);

        var photo = book.PhotoFor("4915550100002@s.whatsapp.net");
        Assert.NotNull(photo);
        Assert.Equal("image/png", photo.Value.Mime);
        Assert.Equal(Convert.FromBase64String(TinyPngBase64), photo.Value.Bytes);
    }

    [Fact]
    public void Reads_vCard40_DataUriPhoto_AndInternationalPrefixes()
    {
        var book = Load($"""
            BEGIN:VCARD
            VERSION:4.0
            FN:Kenji Sato
            TEL;VALUE=uri:tel:+81-90-1234-5678
            TEL:0060 12 345 6789
            PHOTO:data:image/png;base64,{TinyPngBase64}
            END:VCARD
            """);

        Assert.Equal("Kenji Sato", book.NameFor("819012345678@s.whatsapp.net"));
        Assert.Equal("Kenji Sato", book.NameFor("60123456789@s.whatsapp.net"));
        Assert.NotNull(book.PhotoFor("819012345678@s.whatsapp.net"));
    }

    [Fact]
    public void NameFromN_WhenNoFn_AndSenderStrings()
    {
        var book = Load("""
            BEGIN:VCARD
            VERSION:3.0
            N:Rossi;Sofia;;;
            TEL:+4915550100003
            END:VCARD
            """);

        Assert.Equal("Sofia Rossi", book.NameFor("4915550100003@s.whatsapp.net"));
        Assert.Equal("Sofia Rossi", book.NameForSender("+49 155 50100003"));
        Assert.Equal("Sofia Rossi", book.NameForSender("4915550100003"));
        Assert.Null(book.NameForSender("Sofia"));
    }

    [Fact]
    public void NationalNumbersNeedACountryCode()
    {
        var book = Load("""
            BEGIN:VCARD
            VERSION:3.0
            FN:National Only
            TEL:0155 50100004
            END:VCARD
            """, countryCode: null);

        Assert.Null(book.NameFor("4915550100004@s.whatsapp.net"));
        Assert.Equal(1, book.Contacts);
    }

    [Fact]
    public void BrokenCardsAreSkipped_NotFatal()
    {
        var book = Load("""
            BEGIN:VCARD
            FN:No end
            BEGIN:VCARD
            VERSION:3.0
            FN:Good One
            TEL:+4915550100005
            PHOTO;ENCODING=b:@@@not-base64@@@
            END:VCARD
            garbage line
            """);

        Assert.Equal("Good One", book.NameFor("4915550100005@s.whatsapp.net"));
        Assert.Null(book.PhotoFor("4915550100005@s.whatsapp.net"));
    }
}
