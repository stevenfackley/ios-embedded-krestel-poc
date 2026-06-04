using Microsoft.Extensions.Logging;

namespace KestrelBackend.Tests;

public sealed class LoggingTests
{
    [Fact]
    public void RingBuffer_CapturesEntries()
    {
        var sink = new RingBufferSink(capacity: 10);
        var provider = new RingBufferLoggerProvider(sink);
        var logger = provider.CreateLogger("TestCat");

        logger.LogInformation("Message {N}", 1);
        logger.LogWarning("Warning {N}", 2);
        logger.LogError("Error {N}", 3);

        var entries = sink.Snapshot();
        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, e => e.Level == "Information" && e.Message.Contains("1"));
        Assert.Contains(entries, e => e.Level == "Warning");
        Assert.Contains(entries, e => e.Level == "Error");
    }

    [Fact]
    public void RingBuffer_DropOldestOnOverflow()
    {
        var sink = new RingBufferSink(capacity: 3);
        var provider = new RingBufferLoggerProvider(sink);
        var logger = provider.CreateLogger("Cat");

        for (int i = 1; i <= 5; i++)
            logger.LogInformation("Entry {N}", i);

        var entries = sink.Snapshot();
        Assert.Equal(3, entries.Count);
        // Oldest (1,2) were dropped; newest (3,4,5) remain
        Assert.DoesNotContain(entries, e => e.Message.Contains(" 1"));
        Assert.DoesNotContain(entries, e => e.Message.Contains(" 2"));
        Assert.Contains(entries, e => e.Message.Contains(" 5"));
    }

    [Fact]
    public void RingBuffer_RecordsCorrelationId()
    {
        CorrelationContext.Current = "corr-test-99";
        var sink = new RingBufferSink(capacity: 10);
        var provider = new RingBufferLoggerProvider(sink);
        provider.CreateLogger("Cat").LogInformation("hello");

        var entries = sink.Snapshot();
        Assert.Single(entries);
        Assert.Equal("corr-test-99", entries[0].CorrelationId);

        CorrelationContext.Current = null;
    }

    [Fact]
    public void RingBuffer_SequenceIsMonotonic()
    {
        var sink = new RingBufferSink(capacity: 10);
        var provider = new RingBufferLoggerProvider(sink);
        var logger = provider.CreateLogger("Cat");

        for (int i = 0; i < 5; i++)
            logger.LogInformation("x");

        var seqs = sink.Snapshot().Select(e => e.Seq).ToList();
        Assert.Equal(seqs.OrderBy(x => x).ToList(), seqs);
    }
}
