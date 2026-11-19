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
