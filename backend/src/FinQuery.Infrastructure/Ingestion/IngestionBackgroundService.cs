using System.Threading.Channels;
using FinQuery.Application.Ingestion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FinQuery.Infrastructure.Ingestion;

public sealed class IngestionWorkQueue : IIngestionWorkQueue
{
    private readonly Channel<(int DocumentId, bool Force)> _channel = Channel.CreateUnbounded<(int, bool)>();

    public void Enqueue(int documentId, bool force) => _channel.Writer.TryWrite((documentId, force));

    public IAsyncEnumerable<(int DocumentId, bool Force)> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

public sealed class IngestionBackgroundService(IngestionWorkQueue queue, IServiceScopeFactory scopes) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var pipeline = scope.ServiceProvider.GetRequiredService<IIngestionPipeline>();
                await pipeline.RunAsync(job.DocumentId, job.Force, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // The pipeline records its own failure. A host-level fault must not stop the queue.
            }
        }
    }
}
