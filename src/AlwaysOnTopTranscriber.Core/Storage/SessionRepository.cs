using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AlwaysOnTopTranscriber.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace AlwaysOnTopTranscriber.Core.Storage;

public sealed class SessionRepository : ISessionRepository
{
    private readonly ILogger<SessionRepository> _logger;
    private readonly string _databasePath;
    private readonly string _connectionString;
    private volatile bool _ftsEnabled;

    public SessionRepository(AppPaths appPaths, ILogger<SessionRepository> logger)
    {
        _logger = logger;
        _databasePath = appPaths.DatabasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = appPaths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = true
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex) when (IsRecoverableDatabaseError(ex))
        {
            _logger.LogWarning(
                ex,
                "Błąd inicjalizacji SQLite ({Code}). Próba odzyskania bazy: {Path}",
                ex.SqliteErrorCode,
                _databasePath);

            BackupBrokenDatabaseFiles();
            await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await ConfigurePragmasAsync(connection, cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS sessions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                start_time_utc TEXT NOT NULL,
                end_time_utc TEXT NOT NULL,
                duration_seconds INTEGER NOT NULL,
                markdown_path TEXT NOT NULL,
                json_path TEXT NOT NULL,
                txt_path TEXT NOT NULL DEFAULT '',
                transcript_text TEXT NOT NULL,
                engine_type TEXT NOT NULL,
                model_name TEXT NOT NULL,
                word_count INTEGER NOT NULL,
                notes TEXT NOT NULL DEFAULT '',
                tags TEXT NOT NULL DEFAULT ''
            );
            """,
            cancellationToken).ConfigureAwait(false);

        await EnsureColumnExistsAsync(connection, "sessions", "notes", "TEXT NOT NULL DEFAULT ''", cancellationToken)
            .ConfigureAwait(false);
        await EnsureColumnExistsAsync(connection, "sessions", "tags", "TEXT NOT NULL DEFAULT ''", cancellationToken)
            .ConfigureAwait(false);
        await EnsureColumnExistsAsync(connection, "sessions", "txt_path", "TEXT NOT NULL DEFAULT ''", cancellationToken)
            .ConfigureAwait(false);

        try
        {
            await ExecuteNonQueryAsync(
                connection,
                """
                CREATE VIRTUAL TABLE IF NOT EXISTS sessions_fts
                USING fts5(name, transcript_text, content='sessions', content_rowid='id');
                """,
                cancellationToken).ConfigureAwait(false);

            await ExecuteNonQueryAsync(
                connection,
                """
                CREATE TRIGGER IF NOT EXISTS sessions_ai AFTER INSERT ON sessions BEGIN
                    INSERT INTO sessions_fts(rowid, name, transcript_text)
                    VALUES (new.id, new.name, new.transcript_text);
                END;
                """,
                cancellationToken).ConfigureAwait(false);

            await ExecuteNonQueryAsync(
                connection,
                """
                CREATE TRIGGER IF NOT EXISTS sessions_ad AFTER DELETE ON sessions BEGIN
                    INSERT INTO sessions_fts(sessions_fts, rowid, name, transcript_text)
                    VALUES('delete', old.id, old.name, old.transcript_text);
                END;
                """,
                cancellationToken).ConfigureAwait(false);

            await ExecuteNonQueryAsync(
                connection,
                """
                CREATE TRIGGER IF NOT EXISTS sessions_au AFTER UPDATE ON sessions BEGIN
                    INSERT INTO sessions_fts(sessions_fts, rowid, name, transcript_text)
                    VALUES('delete', old.id, old.name, old.transcript_text);
                    INSERT INTO sessions_fts(rowid, name, transcript_text)
                    VALUES (new.id, new.name, new.transcript_text);
                END;
                """,
                cancellationToken).ConfigureAwait(false);

            _ftsEnabled = true;
        }
        catch (SqliteException ex)
        {
            _ftsEnabled = false;
            _logger.LogWarning(ex, "FTS5 niedostępne, wyszukiwanie przełączy się na LIKE.");
        }
    }

    public async Task<long> AddSessionAsync(SessionEntity session, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO sessions (
                name, start_time_utc, end_time_utc, duration_seconds, markdown_path, json_path, txt_path,
                transcript_text, engine_type, model_name, word_count, notes, tags
            ) VALUES (
                $name, $start, $end, $duration, $markdown, $json, $txt, $text, $engine, $model, $words, $notes, $tags
            );
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$name", session.Name);
        command.Parameters.AddWithValue("$start", session.StartTimeUtc.ToString("O"));
        command.Parameters.AddWithValue("$end", session.EndTimeUtc.ToString("O"));
        command.Parameters.AddWithValue("$duration", (long)session.Duration.TotalSeconds);
        command.Parameters.AddWithValue("$markdown", session.MarkdownPath);
        command.Parameters.AddWithValue("$json", session.JsonPath);
        command.Parameters.AddWithValue("$txt", session.TextPath);
        command.Parameters.AddWithValue("$text", session.TranscriptText);
        command.Parameters.AddWithValue("$engine", session.EngineType);
        command.Parameters.AddWithValue("$model", session.ModelName);
        command.Parameters.AddWithValue("$words", session.WordCount);
        command.Parameters.AddWithValue("$notes", session.Notes);
        command.Parameters.AddWithValue("$tags", session.Tags);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<SessionEntity>> GetSessionsAsync(CancellationToken cancellationToken)
    {
        return await QuerySessionsAsync(new SessionQueryOptions(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SessionEntity>> SearchSessionsAsync(string query, CancellationToken cancellationToken)
    {
        return await QuerySessionsAsync(new SessionQueryOptions { Query = query }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SessionEntity>> QuerySessionsAsync(SessionQueryOptions options, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var query = options.Query?.Trim();
        if (!string.IsNullOrWhiteSpace(query) && _ftsEnabled)
        {
            try
            {
                await using var command = BuildFtsQuery(connection, options);
                var ftsRows = await ReadSessionsAsync(command, cancellationToken).ConfigureAwait(false);
                return ftsRows;
            }
            catch (SqliteException ex)
            {
                _logger.LogWarning(ex, "Błąd FTS, fallback do LIKE.");
            }
        }

        await using var fallback = BuildLikeQuery(connection, options);
        return await ReadSessionsAsync(fallback, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteSessionAsync(long sessionId, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM sessions WHERE id = $id;";
        command.Parameters.AddWithValue("$id", sessionId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RenameSessionAsync(long sessionId, string newName, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE sessions SET name = $name WHERE id = $id;";
        command.Parameters.AddWithValue("$name", newName);
        command.Parameters.AddWithValue("$id", sessionId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateSessionNotesAsync(long sessionId, string notes, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE sessions SET notes = $notes WHERE id = $id;";
        command.Parameters.AddWithValue("$notes", notes);
        command.Parameters.AddWithValue("$id", sessionId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateSessionTagsAsync(long sessionId, string tags, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE sessions SET tags = $tags WHERE id = $id;";
        command.Parameters.AddWithValue("$tags", tags);
        command.Parameters.AddWithValue("$id", sessionId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateSessionTranscriptAsync(
        long sessionId,
        string transcriptText,
        int wordCount,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE sessions SET transcript_text = $text, word_count = $words WHERE id = $id;";
        command.Parameters.AddWithValue("$text", transcriptText);
        command.Parameters.AddWithValue("$words", wordCount);
        command.Parameters.AddWithValue("$id", sessionId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateSessionTextPathAsync(long sessionId, string textPath, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE sessions SET txt_path = $txtPath WHERE id = $id;";
        command.Parameters.AddWithValue("$txtPath", textPath);
        command.Parameters.AddWithValue("$id", sessionId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static SessionEntity ReadSession(SqliteDataReader reader)
    {
        return new SessionEntity
        {
            Id = reader.GetInt64(0),
            Name = reader.GetString(1),
            StartTimeUtc = DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            EndTimeUtc = DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            Duration = TimeSpan.FromSeconds(reader.GetInt64(4)),
            MarkdownPath = reader.GetString(5),
            JsonPath = reader.GetString(6),
            TextPath = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
            TranscriptText = reader.GetString(8),
            EngineType = reader.GetString(9),
            ModelName = reader.GetString(10),
            WordCount = reader.GetInt32(11),
            Notes = reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
            Tags = reader.IsDBNull(13) ? string.Empty : reader.GetString(13)
        };
    }

    private static async Task<IReadOnlyList<SessionEntity>> ReadSessionsAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var result = new List<SessionEntity>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(ReadSession(reader));
        }

        return result;
    }

    private static SqliteCommand BuildFtsQuery(SqliteConnection connection, SessionQueryOptions options)
    {
        var query = options.Query?.Trim() ?? string.Empty;
        var command = connection.CreateCommand();

        var whereParts = new List<string>
        {
            "sessions_fts MATCH $query"
        };

        command.Parameters.AddWithValue("$query", query);
        AppendFilterClauses(whereParts, command, options, alias: "s");

        command.CommandText =
            $"""
             SELECT s.id, s.name, s.start_time_utc, s.end_time_utc, s.duration_seconds, s.markdown_path, s.json_path, s.txt_path,
                    s.transcript_text, s.engine_type, s.model_name, s.word_count, s.notes, s.tags
             FROM sessions s
             INNER JOIN sessions_fts fts ON s.id = fts.rowid
             WHERE {string.Join(" AND ", whereParts)}
             ORDER BY s.start_time_utc DESC;
             """;

        return command;
    }

    private static SqliteCommand BuildLikeQuery(SqliteConnection connection, SessionQueryOptions options)
    {
        var command = connection.CreateCommand();
        var whereParts = new List<string>();
        var query = options.Query?.Trim();

        if (!string.IsNullOrWhiteSpace(query))
        {
            whereParts.Add("(s.name LIKE $queryLike OR s.transcript_text LIKE $queryLike)");
            command.Parameters.AddWithValue("$queryLike", $"%{query}%");
        }

        AppendFilterClauses(whereParts, command, options, alias: "s");

        var whereSql = whereParts.Count == 0
            ? string.Empty
            : $"WHERE {string.Join(" AND ", whereParts)}";

        command.CommandText =
            $"""
             SELECT s.id, s.name, s.start_time_utc, s.end_time_utc, s.duration_seconds, s.markdown_path, s.json_path, s.txt_path,
                    s.transcript_text, s.engine_type, s.model_name, s.word_count, s.notes, s.tags
             FROM sessions s
             {whereSql}
             ORDER BY s.start_time_utc DESC;
             """;

        return command;
    }

    private static void AppendFilterClauses(
        List<string> whereParts,
        SqliteCommand command,
        SessionQueryOptions options,
        string alias)
    {
        if (!string.IsNullOrWhiteSpace(options.Tag))
        {
            whereParts.Add($"{alias}.tags LIKE $tagFilter");
            command.Parameters.AddWithValue("$tagFilter", $"%{options.Tag.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(options.ModelName))
        {
            whereParts.Add($"{alias}.model_name = $modelFilter");
            command.Parameters.AddWithValue("$modelFilter", options.ModelName.Trim());
        }

        if (options.DateFromUtc is { } fromUtc)
        {
            whereParts.Add($"{alias}.start_time_utc >= $dateFromUtc");
            command.Parameters.AddWithValue("$dateFromUtc", fromUtc.ToString("O"));
        }

        if (options.DateToUtc is { } toUtc)
        {
            whereParts.Add($"{alias}.start_time_utc <= $dateToUtc");
            command.Parameters.AddWithValue("$dateToUtc", toUtc.ToString("O"));
        }
    }

    private static bool IsRecoverableDatabaseError(SqliteException ex)
    {
        // 10 = disk I/O, 11 = malformed db image, 26 = not a database.
        return ex.SqliteErrorCode is 10 or 11 or 26;
    }

    private async Task ConfigurePragmasAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(connection, "PRAGMA foreign_keys = ON;", cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, "PRAGMA temp_store = MEMORY;", cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, "PRAGMA busy_timeout = 5000;", cancellationToken).ConfigureAwait(false);

        try
        {
            await ExecuteNonQueryAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            // WAL bywa niestabilny na niektórych systemach plików - fallback utrzymuje zgodność.
            _logger.LogWarning(ex, "WAL niedostępny, fallback na journal_mode=DELETE.");
            await ExecuteNonQueryAsync(connection, "PRAGMA journal_mode = DELETE;", cancellationToken).ConfigureAwait(false);
        }
    }

    private void BackupBrokenDatabaseFiles()
    {
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        BackupOne(_databasePath, $".broken.{stamp}");
        BackupOne($"{_databasePath}-wal", $".broken.{stamp}");
        BackupOne($"{_databasePath}-shm", $".broken.{stamp}");
        BackupOne($"{_databasePath}-journal", $".broken.{stamp}");
    }

    private static void BackupOne(string sourcePath, string suffix)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        var destinationPath = sourcePath + suffix;
        File.Move(sourcePath, destinationPath, overwrite: true);
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureColumnExistsAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string declarationSql,
        CancellationToken cancellationToken)
    {
        await using var pragmaCommand = connection.CreateCommand();
        pragmaCommand.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await pragmaCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {declarationSql};";
        await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
