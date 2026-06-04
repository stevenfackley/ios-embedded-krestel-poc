using System.Threading.Channels;

namespace KestrelBackend;

internal sealed class ConcurrencyModule : ICapabilityModule
{
    public IEnumerable<CapabilityDescriptor> Describe() =>
    [
        new("concurrency.channels",  "Concurrency", "System.Threading.Channels", "Bounded Channel<T> producer/consumer",          Verdict.Works, "Channel<T>; lock-free; AOT-safe"),
        new("concurrency.parallel",  "Concurrency", "Parallel.ForEachAsync",     "CPU-bound parallel workload",                   Verdict.Works, "Parallel.ForEachAsync; AOT-safe"),
        new("concurrency.tasks",     "Concurrency", "Task.WhenAll + Interlocked", "Concurrent counter with Task.WhenAll",         Verdict.Works, "Task.WhenAll; Interlocked; AOT-safe"),
    ];

    public Task<CapabilityResult> RunAsync(string id, CancellationToken ct) => id switch
    {
        "concurrency.channels" => RunChannelsAsync(ct),
        "concurrency.parallel" => RunParallelAsync(ct),
        "concurrency.tasks"    => RunTasksAsync(ct),
        _ => Task.FromResult(Unknown(id))
    };

    public void MapRoutes(Router router) { }

    private static async Task<CapabilityResult> RunChannelsAsync(CancellationToken ct)
    {
        var ch = Channel.CreateBounded<int>(new BoundedChannelOptions(10));
        int sum = 0;

        var producer = Task.Run(async () =>
        {
            for (int i = 1; i <= 5; i++)
                await ch.Writer.WriteAsync(i, ct).ConfigureAwait(false);
            ch.Writer.Complete();
        }, ct);

        var consumer = Task.Run(async () =>
        {
            await foreach (var item in ch.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                sum += item;
        }, ct);

        await Task.WhenAll(producer, consumer).ConfigureAwait(false);
        return Works("concurrency.channels", "Concurrency", "System.Threading.Channels",
            $"Bounded channel sum 1..5 = {sum} (expected 15); correct={sum == 15}");
    }

    private static async Task<CapabilityResult> RunParallelAsync(CancellationToken ct)
    {
        long total = 0;
        await Parallel.ForEachAsync(Enumerable.Range(1, 100), ct, (i, _) =>
        {
            System.Threading.Interlocked.Add(ref total, i);
            return ValueTask.CompletedTask;
        }).ConfigureAwait(false);
        return Works("concurrency.parallel", "Concurrency", "Parallel.ForEachAsync",
            $"Sum 1..100 = {total} (expected 5050); correct={total == 5050}");
    }

    private static async Task<CapabilityResult> RunTasksAsync(CancellationToken ct)
    {
        long counter = 0;
        var tasks = Enumerable.Range(0, 50).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 100; i++)
                System.Threading.Interlocked.Increment(ref counter);
        }, ct));
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return Works("concurrency.tasks", "Concurrency", "Task.WhenAll + Interlocked",
            $"50 tasks × 100 increments = {counter} (expected 5000); correct={counter == 5000}");
    }

    private static CapabilityResult Works(string id, string cat, string title, string detail) =>
        new() { Id = id, Category = cat, Title = title, Verdict = Verdict.Works,
                Detail = detail, CorrelationId = CorrelationContext.Current };

    private static CapabilityResult Unknown(string id) =>
        new() { Id = id, Verdict = Verdict.Fails, Detail = $"Unknown: {id}" };
}
