using System.Text.Json.Serialization;

namespace KestrelBackend;

[JsonConverter(typeof(JsonStringEnumConverter<Verdict>))]
internal enum Verdict { Works, Limited, Fails }
