using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AlwaysOnTopTranscriber.Core.Models;
using AlwaysOnTopTranscriber.Core.Utilities;

namespace AlwaysOnTopTranscriber.Core.Storage;

public sealed class TranscriptFileWriter : ITranscriptFileWriter
{
    private readonly AppPaths _appPaths;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public TranscriptFileWriter(AppPaths appPaths)
    {
        _appPaths = appPaths;
    }

    public async Task<TranscriptFiles> WriteAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        var safeName = FilenameSanitizer.Sanitize(snapshot.Name);
        var stamp = snapshot.StartTimeUtc.ToLocalTime().ToString("yyyy-MM-dd_HH-mm-ss");
        var fileBase = $"{safeName}_{stamp}";

        var markdownPath = Path.Combine(_appPaths.TranscriptsDirectory, $"{fileBase}.md");
        var jsonPath = Path.Combine(_appPaths.TranscriptsDirectory, $"{fileBase}.json");
        var textPath = Path.Combine(_appPaths.TranscriptsDirectory, $"{fileBase}.txt");

        var markdown = BuildMarkdown(snapshot);
        await File.WriteAllTextAsync(markdownPath, markdown, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

        var jsonModel = new
        {
            snapshot.Name,
            snapshot.StartTimeUtc,
            snapshot.EndTimeUtc,
            durationSeconds = (snapshot.EndTimeUtc - snapshot.StartTimeUtc).TotalSeconds,
            snapshot.EngineType,
            snapshot.ModelName,
            segments = snapshot.Segments.Select(static segment => new
            {
                startMs = (long)segment.Start.TotalMilliseconds,
                endMs = (long)segment.End.TotalMilliseconds,
                segment.Text,
                segment.Confidence
            })
        };
        var json = JsonSerializer.Serialize(jsonModel, _jsonOptions);
        await File.WriteAllTextAsync(jsonPath, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(textPath, snapshot.TranscriptText, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

        return new TranscriptFiles
        {
            MarkdownPath = markdownPath,
            JsonPath = jsonPath,
            TextPath = textPath
        };
    }

    private static string BuildMarkdown(SessionSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {snapshot.Name}");
        builder.AppendLine();
        builder.AppendLine($"- Start (UTC): {snapshot.StartTimeUtc:O}");
        builder.AppendLine($"- End (UTC): {snapshot.EndTimeUtc:O}");
        builder.AppendLine($"- Engine: {snapshot.EngineType}");
        builder.AppendLine($"- Model: {snapshot.ModelName}");
        builder.AppendLine($"- Duration: {(snapshot.EndTimeUtc - snapshot.StartTimeUtc):hh\\:mm\\:ss}");
        builder.AppendLine();
        builder.AppendLine("## Transcript");
        builder.AppendLine();
        foreach (var segment in snapshot.Segments)
        {
            builder.AppendLine($"[{segment.Start:hh\\:mm\\:ss}] {segment.Text}");
        }

        return builder.ToString();
    }
}
