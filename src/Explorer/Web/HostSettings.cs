namespace ArchiveExplorer.Web;

/// <param name="ArchiveRoot">The export folder (read-only).</param>
/// <param name="DbPath">Index file; null keeps the index in memory.</param>
/// <param name="Port">0 picks a free port.</param>
/// <param name="ListenAddress">null = 127.0.0.1 only.</param>
/// <param name="ExtraHostNames">Host names accepted besides the loopback names (for --listen).</param>
/// <param name="ContactsPath">Optional .vcf address-book export for names and photos (read-only).</param>
/// <param name="CountryCode">Country code for nationally written numbers in the .vcf, e.g. "49".</param>
public sealed record HostSettings(
    string ArchiveRoot,
    string? DbPath,
    int Port = 0,
    string? ListenAddress = null,
    IReadOnlyList<string>? ExtraHostNames = null,
    string? ContactsPath = null,
    string? CountryCode = null);
