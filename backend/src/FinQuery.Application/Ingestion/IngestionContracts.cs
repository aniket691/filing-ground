namespace FinQuery.Application.Ingestion;

public sealed record DocumentStatusResponse(
    int DocumentId,
    string Status,
    int? PageCount,
    DateTime? ProcessingStartedAt,
    DateTime? ProcessingCompletedAt,
    string? Error);

public sealed record IngestionStatsSnapshot(
    int DocumentsProcessed,
    int DocumentsPartiallyProcessed,
    int DocumentsFailed,
    int PagesProcessed,
    int TablesExtracted,
    int MetricsExtracted,
    int ChunksCreated,
    double TotalProcessingDurationMs,
    double UptimeSeconds);

public interface IIngestionCounters
{
    void Record(string status, double durationMs, int pages, int tables, int metrics, int chunks);

    IngestionStatsSnapshot Snapshot();
}

public sealed class IngestionCounters : IIngestionCounters
{
    private readonly Lock _gate = new();
    private readonly DateTime _started = DateTime.UtcNow;
    private int _processed;
    private int _partial;
    private int _failed;
    private int _pages;
    private int _tables;
    private int _metrics;
    private int _chunks;
    private double _durationMs;

    public void Record(string status, double durationMs, int pages, int tables, int metrics, int chunks)
    {
        lock (_gate)
        {
            if (status == "processed")
            {
                _processed++;
            }
            else if (status == "partially_processed")
            {
                _partial++;
            }
            else if (status == "failed")
            {
                _failed++;
            }

            _durationMs += durationMs;
            _pages += pages;
            _tables += tables;
            _metrics += metrics;
            _chunks += chunks;
        }
    }

    public IngestionStatsSnapshot Snapshot()
    {
        lock (_gate)
        {
            var uptime = (DateTime.UtcNow - _started).TotalSeconds;
            return new IngestionStatsSnapshot(
                _processed,
                _partial,
                _failed,
                _pages,
                _tables,
                _metrics,
                _chunks,
                Math.Round(_durationMs, 1),
                Math.Round(uptime, 1));
        }
    }
}

public interface IIngestionWorkQueue
{
    void Enqueue(int documentId, bool force);
}

public interface IIngestionPipeline
{
    Task RunAsync(int documentId, bool force, CancellationToken cancellationToken);
}
