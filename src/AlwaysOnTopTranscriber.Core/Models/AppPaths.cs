using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AlwaysOnTopTranscriber.Core.Models;

public sealed class AppPaths
{
    public AppPaths(string rootDirectory)
    {
        RootDirectory = rootDirectory;
        TranscriptsDirectory = Path.Combine(RootDirectory, "transcripts");
        ModelsDirectory = Path.Combine(RootDirectory, "models");
        LogsDirectory = Path.Combine(RootDirectory, "logs");
        DatabasePath = Path.Combine(RootDirectory, "app.db");
        SettingsPath = Path.Combine(RootDirectory, "settings.json");

        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(TranscriptsDirectory);
        Directory.CreateDirectory(ModelsDirectory);
        Directory.CreateDirectory(LogsDirectory);

        EnsureWritable(RootDirectory);
    }

    public string RootDirectory { get; }

    public string TranscriptsDirectory { get; }

    public string ModelsDirectory { get; }

    public string LogsDirectory { get; }

    public string DatabasePath { get; }

    public string SettingsPath { get; }

    public static AppPaths CreateDefault()
    {
        var envOverride = Environment.GetEnvironmentVariable("TRANSCRIBER_DATA_DIR");
        if (string.IsNullOrWhiteSpace(envOverride))
        {
            // Backward compatibility for earlier builds.
            envOverride = Environment.GetEnvironmentVariable("NICK_VOICE_DATA_DIR");
        }

        var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var tempPath = Path.GetTempPath();

        // Priorytet: jawny override -> roaming -> local -> lokalny katalog obok aplikacji/workdir.
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(envOverride))
        {
            candidates.Add(envOverride.Trim());
        }

        if (!string.IsNullOrWhiteSpace(roamingAppData))
        {
            candidates.Add(Path.Combine(roamingAppData, "Transcriber"));
            candidates.Add(Path.Combine(roamingAppData, "NICK VOICE"));
        }

        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            candidates.Add(Path.Combine(localAppData, "Transcriber"));
            candidates.Add(Path.Combine(localAppData, "NICK VOICE"));
        }

        if (!string.IsNullOrWhiteSpace(documents))
        {
            candidates.Add(Path.Combine(documents, "Transcriber"));
            candidates.Add(Path.Combine(documents, "NICK VOICE"));
        }

        if (!string.IsNullOrWhiteSpace(tempPath))
        {
            candidates.Add(Path.Combine(tempPath, "Transcriber"));
            candidates.Add(Path.Combine(tempPath, "NICK VOICE"));
        }

        if (!string.IsNullOrWhiteSpace(AppContext.BaseDirectory))
        {
            candidates.Add(Path.Combine(AppContext.BaseDirectory, ".transcriber-data"));
            candidates.Add(Path.Combine(AppContext.BaseDirectory, ".nick-voice-data"));
        }

        if (!string.IsNullOrWhiteSpace(Environment.CurrentDirectory))
        {
            candidates.Add(Path.Combine(Environment.CurrentDirectory, ".transcriber-data"));
            candidates.Add(Path.Combine(Environment.CurrentDirectory, ".nick-voice-data"));
        }

        Exception? lastError = null;
        foreach (var candidate in candidates
                     .Where(static path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                return new AppPaths(candidate);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException(
            "Nie udało się utworzyć katalogu danych aplikacji. " +
            "Ustaw zmienną TRANSCRIBER_DATA_DIR na ścieżkę z prawem zapisu.",
            lastError);
    }

    private static void EnsureWritable(string directory)
    {
        var probePath = Path.Combine(directory, $".write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            using var stream = new FileStream(probePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            stream.WriteByte(0x4F);
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            if (File.Exists(probePath))
            {
                try
                {
                    File.Delete(probePath);
                }
                catch
                {
                    // Brak możliwości usunięcia pliku probe nie blokuje działania aplikacji.
                }
            }
        }
    }
}
