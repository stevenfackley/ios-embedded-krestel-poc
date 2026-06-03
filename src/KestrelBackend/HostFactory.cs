namespace KestrelBackend;

/// <summary>
/// Selects the HTTP host implementation at compile time. The <c>USE_KESTREL</c>
/// constant is defined by the csproj only when <c>-p:UseKestrel=true</c> is passed
/// (which is also the only configuration that pulls in the ASP.NET Core framework
/// reference), guaranteeing the fallback build never depends on it.
/// </summary>
internal static class HostFactory
{
    public static IBackendHost Create() =>
#if USE_KESTREL
        new KestrelHost();
#else
        new RawHttpHost();
#endif
}
