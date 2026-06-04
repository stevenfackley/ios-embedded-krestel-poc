using Microsoft.Extensions.Logging;

namespace KestrelBackend;

/// <summary>
/// Source-generated, allocation-free log messages (zero reflection at call site).
/// </summary>
internal static partial class LogMessages
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Request {Method} {Path} started")]
    public static partial void RequestStarted(ILogger logger, string method, string path);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Request {Method} {Path} completed {Status} in {ElapsedMs:F1} ms")]
    public static partial void RequestCompleted(ILogger logger, string method, string path, int status, double elapsedMs);

    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "Probe {Id} started")]
    public static partial void ProbeStarted(ILogger logger, string id);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Probe {Id} completed verdict={Verdict} in {ElapsedMs:F1} ms")]
    public static partial void ProbeCompleted(ILogger logger, string id, string verdict, double elapsedMs);

    [LoggerMessage(EventId = 3000, Level = LogLevel.Error, Message = "Unhandled exception in request {Method} {Path}: {ErrorMessage}")]
    public static partial void UnhandledException(ILogger logger, string method, string path, string errorMessage, Exception exception);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning, Message = "Probe {Id} failed: {ErrorMessage}")]
    public static partial void ProbeFailed(ILogger logger, string id, string errorMessage, Exception exception);
}
