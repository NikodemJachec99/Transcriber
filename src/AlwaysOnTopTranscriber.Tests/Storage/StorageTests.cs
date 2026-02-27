using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AlwaysOnTopTranscriber.Core.Models;
using AlwaysOnTopTranscriber.Core.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace AlwaysOnTopTranscriber.Tests.Storage;

public sealed class StorageTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly AppPaths _paths;

    public StorageTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"always-on-top-transcriber-tests-{Guid.NewGuid():N}");
        _paths = new AppPaths(_tempRoot);
    }

    [Fact]
    public async Task SessionRepository_CanInsertQuerySearchAndDelete()
    {
        var repository = new SessionRepository(_paths, NullLogger<SessionRepository>.Instance);
        await repository.InitializeAsync(CancellationToken.None);

        var session = new SessionEntity
        {
            Name = "Zoom meeting",
            StartTimeUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            EndTimeUtc = DateTimeOffset.UtcNow,
            Duration = TimeSpan.FromMinutes(10),
            MarkdownPath = Path.Combine(_paths.TranscriptsDirectory, "a.md"),
            JsonPath = Path.Combine(_paths.TranscriptsDirectory, "a.json"),
            TextPath = Path.Combine(_paths.TranscriptsDirectory, "a.txt"),
            TranscriptText = "hello from teams and zoom",
            EngineType = "LocalWhisper",
            ModelName = "base",
            WordCount = 5
        };

        var id = await repository.AddSessionAsync(session, CancellationToken.None);
        await repository.RenameSessionAsync(id, "Nowa nazwa", CancellationToken.None);
        await repository.UpdateSessionNotesAsync(id, "To jest notatka", CancellationToken.None);
        await repository.UpdateSessionTagsAsync(id, "teams, sprint", CancellationToken.None);
        await repository.UpdateSessionTranscriptAsync(id, "updated text", 2, CancellationToken.None);

        var all = await repository.GetSessionsAsync(CancellationToken.None);
        var search = await repository.SearchSessionsAsync("teams", CancellationToken.None);
        var filtered = await repository.QuerySessionsAsync(new SessionQueryOptions
        {
            Tag = "sprint",
            ModelName = "base",
            Query = "updated"
        }, CancellationToken.None);

        Assert.Single(all);
        Assert.Empty(search);
        Assert.Single(filtered);
        Assert.Equal("Nowa nazwa", all[0].Name);
        Assert.Equal("To jest notatka", all[0].Notes);
        Assert.Equal("teams, sprint", all[0].Tags);
        Assert.Equal("updated text", all[0].TranscriptText);
        Assert.Equal(Path.Combine(_paths.TranscriptsDirectory, "a.txt"), all[0].TextPath);

        await repository.DeleteSessionAsync(id, CancellationToken.None);
        var afterDelete = await repository.GetSessionsAsync(CancellationToken.None);
        Assert.Empty(afterDelete);
    }

    [Fact]
    public async Task TranscriptFileWriter_WritesMarkdownJsonAndText()
    {
        var writer = new TranscriptFileWriter(_paths);
        var snapshot = new SessionSnapshot
        {
            Name = "Session/Unsafe:Name",
            StartTimeUtc = DateTimeOffset.UtcNow.AddMinutes(-2),
            EndTimeUtc = DateTimeOffset.UtcNow,
            EngineType = "LocalWhisper",
            ModelName = "base",
            Segments =
            [
                new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "hello"),
                new TranscriptSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "world")
            ],
            TranscriptText = "hello world"
        };

        var files = await writer.WriteAsync(snapshot, CancellationToken.None);

        Assert.True(File.Exists(files.MarkdownPath));
        Assert.True(File.Exists(files.JsonPath));
        Assert.True(File.Exists(files.TextPath));
        Assert.Contains("Session/Unsafe:Name".Replace('/', '_').Replace(':', '_'), Path.GetFileName(files.MarkdownPath));
        var plainText = await File.ReadAllTextAsync(files.TextPath, CancellationToken.None);
        Assert.Equal("hello world", plainText);
        Assert.DoesNotContain("startMs", plainText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("endMs", plainText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("confidence", plainText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("## Transcript", plainText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SessionRepository_AddsTxtPathColumnForExistingDatabase()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync(CancellationToken.None);
            await using var create = connection.CreateCommand();
            create.CommandText =
                """
                CREATE TABLE IF NOT EXISTS sessions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    start_time_utc TEXT NOT NULL,
                    end_time_utc TEXT NOT NULL,
                    duration_seconds INTEGER NOT NULL,
                    markdown_path TEXT NOT NULL,
                    json_path TEXT NOT NULL,
                    transcript_text TEXT NOT NULL,
                    engine_type TEXT NOT NULL,
                    model_name TEXT NOT NULL,
                    word_count INTEGER NOT NULL,
                    notes TEXT NOT NULL DEFAULT '',
                    tags TEXT NOT NULL DEFAULT ''
                );
                """;
            await create.ExecuteNonQueryAsync(CancellationToken.None);

            await using var seed = connection.CreateCommand();
            seed.CommandText =
                """
                INSERT INTO sessions (
                    name, start_time_utc, end_time_utc, duration_seconds,
                    markdown_path, json_path, transcript_text, engine_type, model_name, word_count
                )
                VALUES (
                    'legacy',
                    '2026-01-01T10:00:00.0000000+00:00',
                    '2026-01-01T10:00:10.0000000+00:00',
                    10,
                    'legacy.md',
                    'legacy.json',
                    'legacy text',
                    'LocalWhisper',
                    'base',
                    2
                );
                """;
            await seed.ExecuteNonQueryAsync(CancellationToken.None);
        }

        var repository = new SessionRepository(_paths, NullLogger<SessionRepository>.Instance);
        await repository.InitializeAsync(CancellationToken.None);

        await using var verifyConnection = new SqliteConnection(connectionString);
        await verifyConnection.OpenAsync(CancellationToken.None);
        await using var pragma = verifyConnection.CreateCommand();
        pragma.CommandText = "PRAGMA table_info(sessions);";
        await using var reader = await pragma.ExecuteReaderAsync(CancellationToken.None);

        var foundTxtPath = false;
        while (await reader.ReadAsync(CancellationToken.None))
        {
            if (string.Equals(reader.GetString(1), "txt_path", StringComparison.OrdinalIgnoreCase))
            {
                foundTxtPath = true;
                break;
            }
        }

        Assert.True(foundTxtPath);

        var sessions = await repository.GetSessionsAsync(CancellationToken.None);
        Assert.Single(sessions);
        Assert.Equal("legacy", sessions[0].Name);
        Assert.Equal(string.Empty, sessions[0].TextPath);
    }

    [Fact]
    public async Task SessionRepository_ReadWrite_TxtPath()
    {
        var repository = new SessionRepository(_paths, NullLogger<SessionRepository>.Instance);
        await repository.InitializeAsync(CancellationToken.None);

        var session = new SessionEntity
        {
            Name = "txt path test",
            StartTimeUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            EndTimeUtc = DateTimeOffset.UtcNow,
            Duration = TimeSpan.FromMinutes(1),
            MarkdownPath = Path.Combine(_paths.TranscriptsDirectory, "txt-path.md"),
            JsonPath = Path.Combine(_paths.TranscriptsDirectory, "txt-path.json"),
            TextPath = string.Empty,
            TranscriptText = "hello",
            EngineType = "LocalWhisper",
            ModelName = "base",
            WordCount = 1
        };

        var id = await repository.AddSessionAsync(session, CancellationToken.None);
        var updatedPath = Path.Combine(_paths.TranscriptsDirectory, "txt-path.txt");
        await repository.UpdateSessionTextPathAsync(id, updatedPath, CancellationToken.None);

        var sessions = await repository.GetSessionsAsync(CancellationToken.None);
        Assert.Single(sessions);
        Assert.Equal(updatedPath, sessions[0].TextPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // test cleanup best-effort
        }
    }
}
