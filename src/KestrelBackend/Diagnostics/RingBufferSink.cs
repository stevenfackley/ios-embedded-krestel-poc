namespace KestrelBackend;

/// <summary>
/// Fixed-capacity circular log buffer. Thread-safe via lock. Overflows evict oldest entry.
/// </summary>
internal sealed class RingBufferSink
{
    private readonly LogEntry[] _ring;
    private readonly int _capacity;
    private long _seq;
    private int _head;   // next write position
    private int _count;  // entries filled (0..capacity)
    private readonly object _lock = new();

    public RingBufferSink(int capacity = 500)
    {
        _capacity = capacity;
        _ring = new LogEntry[capacity];
    }

    public void Write(LogEntry entry)
    {
        lock (_lock)
        {
            _ring[_head] = entry;
            _head = (_head + 1) % _capacity;
            if (_count < _capacity) _count++;
        }
    }

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_lock)
        {
            var result = new LogEntry[_count];
            // Read from tail (oldest) to head (newest)
            int tail = _count < _capacity ? 0 : _head;
            for (int i = 0; i < _count; i++)
                result[i] = _ring[(tail + i) % _capacity];
            return result;
        }
    }

    public long NextSeq() => System.Threading.Interlocked.Increment(ref _seq);
}
