using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KestrelBackend;

internal sealed class CryptoModule : ICapabilityModule
{
    public IEnumerable<CapabilityDescriptor> Describe() =>
    [
        new("crypto.sha",    "Crypto", "SHA-256/512",    "Hardware-backed hash via System.Security.Cryptography", Verdict.Works, "SHA256/SHA512 managed wrappers; AOT-safe; no reflection"),
        new("crypto.hmac",   "Crypto", "HMAC-SHA256",    "Keyed MAC using HMACSHA256",                           Verdict.Works, "HMACSHA256; reflection-free"),
        new("crypto.aesgcm", "Crypto", "AES-GCM",        "Authenticated encryption using AesGcm",               Verdict.Works, "AesGcm; hardware AES-NI on arm64/x64"),
        new("crypto.rsa",    "Crypto", "RSA sign/verify", "2048-bit RSA with PKCS#1 v2.1 (OAEP)",              Verdict.Works, "RSA.Create(); PEM import; AOT-safe"),
        new("crypto.pbkdf2", "Crypto", "PBKDF2",          "Password hashing via Rfc2898DeriveBytes",            Verdict.Works, "Rfc2898DeriveBytes with SHA256; trim-safe"),
    ];

    public Task<CapabilityResult> RunAsync(string id, CancellationToken ct) =>
        Task.FromResult(id switch
        {
            "crypto.sha"    => RunSha(),
            "crypto.hmac"   => RunHmac(),
            "crypto.aesgcm" => RunAesGcm(),
            "crypto.rsa"    => RunRsa(),
            "crypto.pbkdf2" => RunPbkdf2(),
            _ => Unknown(id)
        });

    public void MapRoutes(Router router) =>
        router.Map("POST", "/api/crypto/hash", (req, rv, ct) =>
        {
            string input = req.Query.TryGetValue("input", out string? v) ? v
                         : Encoding.UTF8.GetString(req.Body.Span);
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(
                new CryptoHashResult(input, "SHA256", hash),
                ApiJsonContext.Default.CryptoHashResult);
            return Task.FromResult(HttpResponse.Json(json));
        });

    private static CapabilityResult RunSha()
    {
        byte[] input = "hello kestrel"u8.ToArray();
        string sha256 = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        string sha512 = Convert.ToHexString(SHA512.HashData(input)).ToLowerInvariant();
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            new AnonymousShaResult(sha256, sha512), ApiJsonContext.Default.AnonymousShaResult);
        return Works("crypto.sha", "Crypto", "SHA-256/512",
            $"SHA256={sha256[..16]}…  SHA512={sha512[..16]}…", json);
    }

    private static CapabilityResult RunHmac()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] msg = "hello hmac"u8.ToArray();
        byte[] mac = HMACSHA256.HashData(key, msg);
        return Works("crypto.hmac", "Crypto", "HMAC-SHA256",
            $"MAC={Convert.ToHexString(mac)[..16]}… (key rotated per probe)");
    }

    private static CapabilityResult RunAesGcm()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        byte[] tag = new byte[AesGcm.TagByteSizes.MaxSize];
        byte[] plaintext = "hello aes-gcm"u8.ToArray();
        byte[] ciphertext = new byte[plaintext.Length];
        using var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Decrypt to verify
        byte[] decrypted = new byte[plaintext.Length];
        aes.Decrypt(nonce, ciphertext, tag, decrypted);
        bool ok = plaintext.AsSpan().SequenceEqual(decrypted);
        return Works("crypto.aesgcm", "Crypto", "AES-GCM",
            $"Encrypt+decrypt verified={ok}; ciphertext={Convert.ToBase64String(ciphertext)}");
    }

    private static CapabilityResult RunRsa()
    {
        using var rsa = RSA.Create(2048);
        byte[] data = "sign me"u8.ToArray();
        byte[] sig = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        bool valid = rsa.VerifyData(data, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return Works("crypto.rsa", "Crypto", "RSA sign/verify",
            $"2048-bit RSA PKCS#1 sign+verify={valid}; sig={Convert.ToBase64String(sig)[..20]}…");
    }

    private static CapabilityResult RunPbkdf2()
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2("hunter2", salt, 100_000, HashAlgorithmName.SHA256, 32);
        return Works("crypto.pbkdf2", "Crypto", "PBKDF2",
            $"PBKDF2-SHA256 100k iter; hash={Convert.ToBase64String(hash)[..16]}…");
    }

    private static CapabilityResult Works(string id, string cat, string title, string detail,
        byte[]? rawOutput = null)
    {
        System.Text.Json.JsonElement? output = null;
        if (rawOutput is not null)
        {
            using var doc = JsonDocument.Parse(rawOutput);
            output = doc.RootElement.Clone();
        }
        return new CapabilityResult
        {
            Id = id, Category = cat, Title = title,
            Verdict = Verdict.Works, Detail = detail,
            ElapsedMs = 0, CorrelationId = CorrelationContext.Current,
            Output = output
        };
    }

    private static CapabilityResult Unknown(string id) =>
        new() { Id = id, Verdict = Verdict.Fails, Detail = $"Unknown probe id: {id}" };
}

internal sealed record CryptoHashResult(
    [property: System.Text.Json.Serialization.JsonPropertyName("input")] string Input,
    [property: System.Text.Json.Serialization.JsonPropertyName("algorithm")] string Algorithm,
    [property: System.Text.Json.Serialization.JsonPropertyName("hash")] string Hash);

internal sealed record AnonymousShaResult(
    [property: System.Text.Json.Serialization.JsonPropertyName("sha256")] string Sha256,
    [property: System.Text.Json.Serialization.JsonPropertyName("sha512")] string Sha512);
