namespace KestrelBackend;

internal interface ICapabilityModule
{
    IEnumerable<CapabilityDescriptor> Describe();
    Task<CapabilityResult> RunAsync(string id, CancellationToken ct);
    void MapRoutes(Router router);
}
