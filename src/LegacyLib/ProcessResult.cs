using System;

namespace LegacyLib
{
    /// <summary>
    /// The wire contract produced by <see cref="DataProcessor.Process(string)"/> and
    /// serialized to JSON by the embedded server. The Swift side mirrors this as the
    /// <c>ProcessResponse</c> <c>Decodable</c>.
    /// </summary>
    /// <remarks>
    /// Kept a plain immutable POCO with no reflection-only members so it stays
    /// NativeAOT / System.Text.Json source-generator friendly on the .NET 9 side.
    /// </remarks>
    public sealed class ProcessResult
    {
        /// <summary>Creates a populated result.</summary>
        public ProcessResult(string input, string hash, int length, string processedAtUtc)
        {
            Input = input;
            Hash = hash;
            Length = length;
            ProcessedAtUtc = processedAtUtc;
        }

        /// <summary>The original input echoed back.</summary>
        public string Input { get; }

        /// <summary>The processor's derived value (default: SHA-256 hex).</summary>
        public string Hash { get; }

        /// <summary>Character length of <see cref="Input"/>.</summary>
        public int Length { get; }

        /// <summary>UTC timestamp (round-trip "O" format) when processing ran.</summary>
        public string ProcessedAtUtc { get; }
    }
}
