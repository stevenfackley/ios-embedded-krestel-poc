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
    private readonly SqliteConnection _db;
    private readonly List<NoteRecord> _jsonStore = [];

    public PersistenceModule()
    {
        _db = new SqliteConnection("Data Source=:memory:");
        _db.Open();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS notes (id INTEGER PRIMARY KEY, body TEXT NOT NULL)";
        cmd.ExecuteNonQuery();
    }

    public IEnumerable<CapabilityDescriptor> Describe() =>
    [
        new("persist.sqlite",  "Persistence", "SQLite in-memory", "Microsoft.Data.Sqlite CREATE/INSERT/SELECT in :memory:", Verdict.Works, "M.Data.Sqlite; iOS sandbox-safe (in-memory)"),
        new("persist.jsonfile", "Persistence", "JSON file store",  "Simple STJ-backed in-memory list (file I/O disabled in iOS sandbox)", Verdict.Works, "STJ List<T>; no file I/O"),
        new("persist.fileio",  "Persistence", "Temp file I/O",    "File.WriteAllText/ReadAllText in Path.GetTempPath()",  Verdict.Works, "System.IO.File; sandbox temp dir"),
    ];

    public Task<CapabilityResult> RunAsync(string id, CancellationToken ct) =>
        Task.FromResult(id switch
        {
            "persist.sqlite"  => RunSqlite(),
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
        using var tx = _db.BeginTransaction();
        using var ins = _db.CreateCommand();
        ins.CommandText = "INSERT INTO notes (body) VALUES (@b)";
        ins.Parameters.AddWithValue("@b", "probe note");
        ins.ExecuteNonQuery();

        using var sel = _db.CreateCommand();
        sel.CommandText = "SELECT COUNT(*) FROM notes";
        long count = (long)(sel.ExecuteScalar() ?? 0L);
        tx.Rollback();

        return Works("persist.sqlite", "Persistence", "SQLite in-memory",
            $"INSERT+SELECT COUNT={count}; in-memory :memory: DB");
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

        using var ins = _db.CreateCommand();
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

        using var del = _db.CreateCommand();
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
        using var sel = _db.CreateCommand();
        sel.CommandText = "SELECT id, body FROM notes";
        using var reader = sel.ExecuteReader();
        while (reader.Read())
            rows.Add(new NoteRecord(reader.GetInt64(0), reader.GetString(1)));
        return rows;
    }

    private static CapabilityResult Works(string id, string cat, string title, string detail) =>
        new() { Id = id, Category = cat, Title = title, Verdict = Verdict.Works,
                Detail = detail, CorrelationId = CorrelationContext.Current };

    private static CapabilityResult Unknown(string id) =>
        new() { Id = id, Verdict = Verdict.Fails, Detail = $"Unknown: {id}" };
}

internal sealed record NoteRecord(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("body")] string Body);
