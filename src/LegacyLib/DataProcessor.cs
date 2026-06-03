using System;
using System.Security.Cryptography;
using System.Text;

namespace LegacyLib
{
    /// <summary>
    /// Legacy business logic, untouched in its original netstandard2.0 form. The whole
    /// point of the PoC is that this type is reused as-is and surfaced to the iOS app
    /// through the embedded HTTP server rather than being rewritten in Swift.
    /// </summary>
    public sealed class DataProcessor
    {
        /// <summary>
        /// Processes <paramref name="input"/> into a <see cref="ProcessResult"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="input"/> is null.</exception>
        public ProcessResult Process(string input)
        {
            if (input is null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            string hash = Transform(input);

            return new ProcessResult(
                input: input,
                hash: hash,
                length: input.Length,
                processedAtUtc: DateTime.UtcNow.ToString("O"));
        }

        // ────────────────────────── YOUR CONTRIBUTION ──────────────────────────
        // This 5–10 line method is the core business decision and it defines the
        // wire contract the Swift client decodes. The default below hashes the raw
        // UTF-8 bytes as SHA-256 hex. Worth deciding before you ship:
        //   • Normalize first (e.g. input.Trim().ToLowerInvariant()) so equivalent
        //     inputs hash identically — stable, but discards casing/whitespace.
        //   • Encode as Base64 instead of hex — ~33% shorter payload, less readable.
        //   • Return something domain-specific entirely (HMAC with a key, CRC, etc.).
        // Constraint: keep it allocation-light and reflection-free so it survives
        // NativeAOT trimming on the .NET 9 iOS side.
        private static string Transform(string input)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(input);

            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(bytes);

                var builder = new StringBuilder(digest.Length * 2);
                foreach (byte b in digest)
                {
                    builder.Append(b.ToString("x2"));
                }

                return builder.ToString();
            }
        }
        // ────────────────────────────────────────────────────────────────────────
    }
}
