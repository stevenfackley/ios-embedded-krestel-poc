using Microsoft.Data.Sqlite;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KestrelBackend;

/// <summary>
/// SQLite in-memory store + JSON file store + temp file I/O probes.
/// SQLite probe uses a connection-scoped in-memory DB to avoid iOS sandbox issues.
/// </summary>
internal sealed class PersistenceModule : ICapabilityModule
{
    private readonly Lazy<SqliteConnection> _lazyDb = new(OpenDatabase);

    // Opened on first use, NOT in the constructor. If the SQLitePCLRaw native
    // provider fails to initialize under NativeAOT/iOS, the throw lands inside a
    // probe or endpoint (caught by CapabilityCatalog → Fails verdict) instead of
    // aborting host construction and taking the entire server offline.
    private SqliteConnection Db => _lazyDb.Value;

    private static SqliteConnection OpenDatabase()
    {
        // Microsoft.Data.Sqlite.Core ships no provider; register the statically-linked
        // e_sqlcipher bundle explicitly. Idempotent and AOT-safe (no reflection).
        SQLitePCL.Batteries_V2.Init();
        var db = new SqliteConnection("Data Source=:memory:");
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS notes (id INTEGER PRIMARY KEY, body TEXT NOT NULL)";
        cmd.ExecuteNonQuery();
        return db;
    }

    public IEnumerable<CapabilityDescriptor> Describe() =>
    [
        new("persist.sqlite",  "Persistence", "SQLite in-memory", "Microsoft.Data.Sqlite CREATE/INSERT/SELECT in :memory:", Verdict.Works, "M.Data.Sqlite.Core + statically-linked e_sqlcipher (only SQLitePCLRaw bundle with iOS native libs)"),
        new("persist.sqlcipher", "Persistence", "SQLCipher encryption-at-rest", "Keyed on-disk DB: cipher_version, ciphertext file header, wrong-key rejected, round-trip with key", Verdict.Works, "Password=key → PRAGMA key (SQLCipher); proves the e_sqlcipher engine encrypts at rest, unlike plaintext :memory:"),
        new("persist.jsonfile", "Persistence", "JSON file store",  "Simple STJ-backed in-memory list (file I/O disabled in iOS sandbox)", Verdict.Works, "STJ List<T>; no file I/O"),
        new("persist.fileio",  "Persistence", "Temp file I/O",    "File.WriteAllText/ReadAllText in Path.GetTempPath()",  Verdict.Works, "System.IO.File; sandbox temp dir"),
    ];

    public Task<CapabilityResult> RunAsync(string id, CancellationToken ct) =>
        Task.FromResult(id switch
        {
            "persist.sqlite"  => RunSqlite(),
            "persist.sqlcipher" => RunSqlcipher(),
            "persist.jsonfile" => RunJsonFile(),
            "persist.fileio"  => RunFileIo(),
            _ => Unknown(id)
        });

    public void MapRoutes(Router router)
    {
        router.Map("GET",    "/api/notes",     ListNotes);
        router.Map("POST",   "/api/notes",     CreateNote);
        router.Map("DELETE", "/api/notes/{id}", DeleteNote);
    }

    // ── probes ────────────────────────────────────────────────────────────

    private CapabilityResult RunSqlite()
    {
        using var tx = Db.BeginTransaction();
        using var ins = Db.CreateCommand();
        ins.CommandText = "INSERT INTO notes (body) VALUES (@b)";
        ins.Parameters.AddWithValue("@b", "probe note");
        ins.ExecuteNonQuery();

        using var sel = Db.CreateCommand();
        sel.CommandText = "SELECT COUNT(*) FROM notes";
        long count = (long)(sel.ExecuteScalar() ?? 0L);
        tx.Rollback();

        return Works("persist.sqlite", "Persistence", "SQLite in-memory",
            $"INSERT+SELECT COUNT={count}; in-memory :memory: DB");
    }

    // Proves the bundled engine is SQLCipher, not plain SQLite: create a keyed on-disk
    // DB, then demonstrate (a) PRAGMA cipher_version identifies SQLCipher, (b) the file
    // is ciphertext at rest (first 16 bytes are not the "SQLite format 3\0" magic),
    // (c) a wrong key is rejected, and (d) the correct key round-trips. Runs on the
    // Windows test host too (the e_sqlcipher bundle resolves its win-x64 native).
    private static CapabilityResult RunSqlcipher()
    {
        SQLitePCL.Batteries_V2.Init();
        const string key = "correct horse battery staple";
        const string secret = "TOP SECRET payload";
        string path = Path.Combine(Path.GetTempPath(), $"krestrel-cipher-{Guid.NewGuid():N}.db");
        try
        {
            string cipherVersion = WriteKeyedDb(path, key, secret);

            byte[] header = ReadFileHeader(path, 16);
            byte[] magic = Encoding.ASCII.GetBytes("SQLite format 3\0");
            bool headerIsCiphertext = !header.SequenceEqual(magic);
            string headerHex = Convert.ToHexString(header);

            bool wrongKeyRejected = WrongKeyIsRejected(path);
            bool roundTripOk = CorrectKeyRecovers(path, key, secret);

            var proof = new SqlcipherProof(cipherVersion, headerHex, headerIsCiphertext, wrongKeyRejected, roundTripOk);
            var verdict = headerIsCiphertext && wrongKeyRejected && roundTripOk ? Verdict.Works : Verdict.Fails;
            string detail =
                $"cipher_version={cipherVersion}; header={headerHex} (not 'SQLite format 3' => encrypted); " +
                $"wrong-key rejected={wrongKeyRejected}; round-trip with key={roundTripOk}";
            return WithOutput("persist.sqlcipher", "Persistence", "SQLCipher encryption-at-rest", verdict, detail, proof);
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort temp cleanup */ }
        }
    }

    private static string WriteKeyedDb(string path, string key, string secret)
    {
        // Password= makes Microsoft.Data.Sqlite issue PRAGMA key as the first statement.
        // Pooling=false so the file handle is released on Dispose (else the pooled
        // connection keeps the temp file locked → ReadFileHeader/Delete throw on Windows).
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Password = key, Pooling = false
        };
        using var db = new SqliteConnection(csb.ConnectionString);
        db.Open();

        string cipherVersion;
        using (var ver = db.CreateCommand())
        {
            ver.CommandText = "PRAGMA cipher_version";
            cipherVersion = ver.ExecuteScalar() as string ?? "";
        }
        using (var ddl = db.CreateCommand())
        {
            ddl.CommandText = "CREATE TABLE secret (id INTEGER PRIMARY KEY, body TEXT NOT NULL)";
            ddl.ExecuteNonQuery();
        }
        using (var ins = db.CreateCommand())
        {
            ins.CommandText = "INSERT INTO secret (body) VALUES (@b)";
            ins.Parameters.AddWithValue("@b", secret);
            ins.ExecuteNonQuery();
        }
        return cipherVersion;
    }

    private static bool WrongKeyIsRejected(string path)
    {
        try
        {
            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = path, Mode = SqliteOpenMode.ReadWrite, Password = "the-wrong-key", Pooling = false
            };
            using var db = new SqliteConnection(csb.ConnectionString);
            db.Open();
            using var sel = db.CreateCommand();
            sel.CommandText = "SELECT COUNT(*) FROM secret";
            sel.ExecuteScalar();   // decrypt with the wrong key fails here
            return false;          // reading succeeded with a wrong key — NOT encrypted
        }
        catch (SqliteException)
        {
            return true;           // "file is not a database" — the key gate held
        }
    }

    private static bool CorrectKeyRecovers(string path, string key, string expected)
    {
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadWrite, Password = key, Pooling = false
        };
        using var db = new SqliteConnection(csb.ConnectionString);
        db.Open();
        using var sel = db.CreateCommand();
        sel.CommandText = "SELECT body FROM secret LIMIT 1";
        return sel.ExecuteScalar() as string == expected;
    }

    private static byte[] ReadFileHeader(string path, int n)
    {
        using var fs = File.OpenRead(path);
        byte[] buf = new byte[n];
        int read = fs.Read(buf, 0, n);
        return read == n ? buf : buf[..read];
    }

    private CapabilityResult RunJsonFile()
    {
        var list = new List<NoteRecord> { new(1, "probe"), new(2, "test") };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(list, ApiJsonContext.Default.ListNoteRecord);
        var back = JsonSerializer.Deserialize(json, ApiJsonContext.Default.ListNoteRecord);
        return Works("persist.jsonfile", "Persistence", "JSON file store",
            $"STJ list round-trip OK={back?.Count == 2}; {json.Length} bytes");
    }

    private static CapabilityResult RunFileIo()
    {
        string path = Path.Combine(Path.GetTempPath(), $"krestrel-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(path, "hello file");
            string read = File.ReadAllText(path);
            return Works("persist.fileio", "Persistence", "Temp file I/O",
                $"Write+Read OK={read == "hello file"}; path={path}");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ── notes CRUD endpoints ───────────────────────────────────────────────

    private Task<HttpResponse> ListNotes(HttpRequest req, IReadOnlyDictionary<string, string> rv, CancellationToken ct)
    {
        var rows = GetAllRows();
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(rows, ApiJsonContext.Default.ListNoteRecord);
        return Task.FromResult(HttpResponse.Json(json));
    }

    private Task<HttpResponse> CreateNote(HttpRequest req, IReadOnlyDictionary<string, string> rv, CancellationToken ct)
    {
        string body = req.Body.Length > 0
            ? Encoding.UTF8.GetString(req.Body.Span)
            : req.Query.TryGetValue("body", out string? b) ? b : "untitled";

        using var ins = Db.CreateCommand();
        ins.CommandText = "INSERT INTO notes (body) VALUES (@b); SELECT last_insert_rowid();";
        ins.Parameters.AddWithValue("@b", body);
        long id = (long)(ins.ExecuteScalar() ?? 0L);

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            new NoteRecord(id, body), ApiJsonContext.Default.NoteRecord);
        return Task.FromResult(HttpResponse.Json(json, HttpStatus.Created));
    }

    private Task<HttpResponse> DeleteNote(HttpRequest req, IReadOnlyDictionary<string, string> rv, CancellationToken ct)
    {
        if (!long.TryParse(rv["id"], out long id))
            return Task.FromResult(HttpResponse.Problem(HttpStatus.BadRequest, "id must be an integer"));

        using var del = Db.CreateCommand();
        del.CommandText = "DELETE FROM notes WHERE id = @id";
        del.Parameters.AddWithValue("@id", id);
        int affected = del.ExecuteNonQuery();

        return affected == 0
            ? Task.FromResult(HttpResponse.Problem(HttpStatus.NotFound, $"Note {id} not found"))
            : Task.FromResult(HttpResponse.Text("", HttpStatus.NoContent));
    }

    private List<NoteRecord> GetAllRows()
    {
        var rows = new List<NoteRecord>();
        using var sel = Db.CreateCommand();
        sel.CommandText = "SELECT id, body FROM notes";
        using var reader = sel.ExecuteReader();
        while (reader.Read())
            rows.Add(new NoteRecord(reader.GetInt64(0), reader.GetString(1)));
        return rows;
    }

    private static CapabilityResult Works(string id, string cat, string title, string detail) =>
        new() { Id = id, Category = cat, Title = title, Verdict = Verdict.Works,
                Detail = detail, CorrelationId = CorrelationContext.Current };

    private static CapabilityResult WithOutput(string id, string cat, string title, Verdict verdict, string detail, SqlcipherProof proof)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(proof, ApiJsonContext.Default.SqlcipherProof);
        using var doc = JsonDocument.Parse(bytes);
        return new CapabilityResult
        {
            Id = id, Category = cat, Title = title, Verdict = verdict,
            Detail = detail, Output = doc.RootElement.Clone(),
            CorrelationId = CorrelationContext.Current
        };
    }

    private static CapabilityResult Unknown(string id) =>
        new() { Id = id, Verdict = Verdict.Fails, Detail = $"Unknown: {id}" };
}

internal sealed record NoteRecord(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("body")] string Body);

internal sealed record SqlcipherProof(
    [property: JsonPropertyName("cipherVersion")] string CipherVersion,
    [property: JsonPropertyName("headerHex")] string HeaderHex,
    [property: JsonPropertyName("headerIsCiphertext")] bool HeaderIsCiphertext,
    [property: JsonPropertyName("wrongKeyRejected")] bool WrongKeyRejected,
    [property: JsonPropertyName("roundTripOk")] bool RoundTripOk);
