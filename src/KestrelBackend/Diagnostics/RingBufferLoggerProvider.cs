using Microsoft.Extensions.Logging;

namespace KestrelBackend;

internal sealed class RingBufferLoggerProvider : ILoggerProvider
{
    private readonly RingBufferSink _sink;

    public RingBufferLoggerProvider(RingBufferSink sink) => _sink = sink;

    public ILogger CreateLogger(string categoryName) => new RingBufferLogger(categoryName, _sink);

    public void Dispose() { }

    private sealed class RingBufferLogger : ILogger
    {
        private readonly string _category;
        private readonly RingBufferSink _sink;

        public RingBufferLogger(string category, RingBufferSink sink)
        {
            _category = category;
            _sink = sink;
        }

        public bool IsEnabled(LogLevel level) => level != LogLevel.None;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public void Log<TState>(LogLevel level, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;

            string message = formatter(state, exception);
            if (exception is not null)
                message += $" | {exception.GetType().Name}: {exception.Message}";

            _sink.Write(new LogEntry
            {
                Seq = _sink.NextSeq(),
                TimestampUtc = DateTime.UtcNow.ToString("O"),
                Level = level.ToString(),
                Category = _category,
                EventId = eventId.Id,
                Message = message,
                CorrelationId = CorrelationContext.Current
            });
        }
    }
}
