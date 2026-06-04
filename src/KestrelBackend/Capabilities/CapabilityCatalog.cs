using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace KestrelBackend;

internal sealed class CapabilityCatalog
{
    private readonly IReadOnlyList<ICapabilityModule> _modules;
    private readonly ILogger<CapabilityCatalog> _logger;
    private readonly Dictionary<string, (ICapabilityModule module, CapabilityDescriptor desc)> _index;

    public CapabilityCatalog(ILogger<CapabilityCatalog> logger, IEnumerable<ICapabilityModule> modules)
    {
        _logger = logger;
        _modules = [.. modules];
        _index = [];
        foreach (var m in _modules)
            foreach (var d in m.Describe())
                _index[d.Id] = (m, d);
    }

    public IReadOnlyList<ICapabilityModule> Modules => _modules;
    public IReadOnlyList<CapabilityDescriptor> Descriptors => [.. _index.Values.Select(x => x.desc)];

    public async Task<CapabilityResult> RunAsync(string id, CancellationToken ct)
    {
        if (!_index.TryGetValue(id, out var entry))
        {
            return new CapabilityResult
            {
                Id = id, Verdict = Verdict.Fails,
                Detail = $"Unknown capability id: {id}",
                Error = new ProblemDetails { Title = "Not Found", Status = 404, Detail = $"No capability '{id}'", CorrelationId = CorrelationContext.Current ?? "" }
            };
        }

        var sw = Stopwatch.StartNew();
        try
        {
            LogMessages.ProbeStarted(_logger, id);
            var result = await entry.module.RunAsync(id, ct).ConfigureAwait(false);
            sw.Stop();
            LogMessages.ProbeCompleted(_logger, id, result.Verdict.ToString(), sw.Elapsed.TotalMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            LogMessages.ProbeFailed(_logger, id, ex.Message, ex);
            return new CapabilityResult
            {
                Id = id, Category = entry.desc.Category, Title = entry.desc.Title,
                Verdict = Verdict.Fails, Detail = ex.Message,
                ElapsedMs = sw.Elapsed.TotalMilliseconds,
                CorrelationId = CorrelationContext.Current,
                Error = ProblemDetails.From(ex, 500, CorrelationContext.Current ?? "")
            };
        }
    }

    public async Task<IReadOnlyList<CapabilityResult>> RunAllAsync(CancellationToken ct)
    {
        var tasks = _index.Keys.Select(id => RunAsync(id, ct));
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
