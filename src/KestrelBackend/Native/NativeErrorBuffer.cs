namespace KestrelBackend;

/// <summary>
/// Holds the last error captured by the request pipeline so the Swift caller
/// can retrieve it via kestrel_last_error without needing a separate HTTP call.
/// Thread-safe via volatile (single-producer/multi-consumer; races lose at most
/// one error record, which is acceptable for diagnostics).
/// </summary>
internal static class NativeErrorBuffer
{
    private static volatile string? _last;

    /// <summary>Capture an error. Overwrites any previously held record.</summary>
    public static void Capture(string type, string message, string? correlationId)
        => _last = $"{type}|{message}|{correlationId ?? ""}";

    /// <summary>
    /// Copy the last error as UTF-8 into <paramref name="dest"/>.
    /// Returns bytes written (>=0), 0 if no error, or -(bytes needed) if buffer too small.
    /// </summary>
    public static int CopyInto(Span<byte> dest)
    {
        string? last = _last;
        if (last is null) return 0;

        int needed = System.Text.Encoding.UTF8.GetByteCount(last);
        if (needed > dest.Length) return -needed;

        return System.Text.Encoding.UTF8.GetBytes(last, dest);
    }
}
