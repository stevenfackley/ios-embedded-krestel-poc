using System.Text.Json.Serialization;
using LegacyLib;

namespace KestrelBackend;

/// <summary>
/// System.Text.Json source-generation context. By generating the
/// (de)serialization metadata at compile time it removes all serializer reflection,
/// which is what makes JSON work under NativeAOT trimming. Both hosts serialize
/// <see cref="ProcessResult"/> through <c>ApiJsonContext.Default.ProcessResult</c>.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ProcessResult))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(LogEntry))]
[JsonSerializable(typeof(IReadOnlyList<LogEntry>))]
[JsonSerializable(typeof(DiagInfo))]
[JsonSerializable(typeof(CapabilityDescriptor))]
[JsonSerializable(typeof(IReadOnlyList<CapabilityDescriptor>))]
[JsonSerializable(typeof(CapabilityResult))]
[JsonSerializable(typeof(IReadOnlyList<CapabilityResult>))]
// Crypto
[JsonSerializable(typeof(CryptoHashResult))]
[JsonSerializable(typeof(AnonymousShaResult))]
// Serialization
[JsonSerializable(typeof(SerializeResult))]
[JsonSerializable(typeof(Animal))]
[JsonSerializable(typeof(Dog))]
[JsonSerializable(typeof(Cat))]
// Persistence
[JsonSerializable(typeof(NoteRecord))]
[JsonSerializable(typeof(List<NoteRecord>))]
[JsonSerializable(typeof(SqlcipherProof))]
// Networking
[JsonSerializable(typeof(FetchResult))]
// Text
[JsonSerializable(typeof(RegexResult))]
// Compression
[JsonSerializable(typeof(CompressResult))]
internal partial class ApiJsonContext : JsonSerializerContext
{
}
