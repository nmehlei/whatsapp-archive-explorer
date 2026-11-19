using ArchiveExplorer.Index;
using Microsoft.Data.Sqlite;

namespace ArchiveExplorer.Web;

/// <summary>Index lifecycle shared by the API and the background indexer. Counts only, never content.</summary>
public sealed class IndexState(IndexStore store)
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string State { get; private set; } = "indexing";
    public int FilesDone { get; private set; }
    public int FilesTotal { get; private set; }
    public RefreshResult? Result { get; private set; }
    public TimeSpan Elapsed { get; private set; }
    public Task Ready => _ready.Task;
    public bool IsReady => State == "ready";

    /// <summary>What went wrong, without content: exception type, plus SQLite's own message (schema-level only).</summary>
    public string? ErrorSummary { get; private set; }

    public SqliteConnection OpenReader() => store.OpenReader();

    internal void Run()
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            Result = store.Refresh(new Progress(this));
            Elapsed = started.Elapsed;
            State = "ready";
            _ready.TrySetResult();
        }
        catch (Exception e)
        {
            State = "error";
            ErrorSummary = e switch
            {
                Microsoft.Data.Sqlite.SqliteException s => $"{nameof(Microsoft.Data.Sqlite.SqliteException)} {s.SqliteErrorCode}/{s.SqliteExtendedErrorCode}: {s.Message}",
                _ => $"{e.GetType().FullName} at {e.TargetSite?.DeclaringType?.Name}.{e.TargetSite?.Name}",
            };
            _ready.TrySetException(e);
        }
    }

    private sealed class Progress(IndexState state) : IProgress<IndexProgress>
    {
        public void Report(IndexProgress value)
        {
            state.FilesDone = value.FilesDone;
            state.FilesTotal = value.FilesTotal;
        }
    }
}
