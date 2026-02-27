using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using AlwaysOnTopTranscriber.Core.Models;
using Microsoft.Extensions.Logging;

namespace AlwaysOnTopTranscriber.Core.Utilities;

public sealed class ModelManager : IModelManager
{
    private static readonly HttpClient HttpClient = new();

    private static readonly Dictionary<string, ModelSource> KnownModels =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["tiny"] = new ModelSource(
                "ggml-tiny.bin",
                "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin",
                30L * 1024 * 1024,
                75L * 1024 * 1024),
            ["base"] = new ModelSource(
                "ggml-base.bin",
                "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin",
                60L * 1024 * 1024,
                145L * 1024 * 1024),
            ["small"] = new ModelSource(
                "ggml-small.bin",
                "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin",
                120L * 1024 * 1024,
                465L * 1024 * 1024),
            ["basics"] = new ModelSource(
                "ggml-medium-q5_0.bin",
                "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium-q5_0.bin",
                200L * 1024 * 1024,
                575L * 1024 * 1024)
        };

    private readonly AppPaths _appPaths;
    private readonly ILogger<ModelManager> _logger;

    public ModelManager(AppPaths appPaths, ILogger<ModelManager> logger)
    {
        _appPaths = appPaths;
        _logger = logger;
    }

    public Task<IReadOnlyList<ModelDescriptor>> GetAvailableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var models = KnownModels.Select(pair =>
        {
            var modelName = pair.Key;
            var info = pair.Value;
            var localPath = Path.Combine(_appPaths.ModelsDirectory, info.FileName);
            var downloaded = File.Exists(localPath);
            return new ModelDescriptor
            {
                Name = modelName,
                FileName = info.FileName,
                DownloadUrl = info.Url,
                LocalPath = downloaded ? localPath : null,
                IsDownloaded = downloaded,
                ApproximateSizeBytes = info.ApproximateSizeBytes
            };
        }).ToList();

        return Task.FromResult<IReadOnlyList<ModelDescriptor>>(models);
    }

    public async Task<string> DownloadAsync(
        string modelName,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!KnownModels.TryGetValue(modelName, out var descriptor))
        {
            throw new InvalidOperationException($"Nieobsługiwany model: {modelName}");
        }

        var destinationPath = Path.Combine(_appPaths.ModelsDirectory, descriptor.FileName);
        var tempPath = destinationPath + ".download";

        if (File.Exists(destinationPath))
        {
            var existing = new FileInfo(destinationPath);
            if (existing.Length >= descriptor.MinValidBytes)
            {
                progress?.Report(new ModelDownloadProgress
                {
                    ModelName = modelName,
                    DownloadedBytes = existing.Length,
                    TotalBytes = existing.Length,
                    FractionCompleted = 1,
                    IsResumedDownload = false
                });

                return destinationPath;
            }

            File.Move(destinationPath, tempPath, overwrite: true);
        }

        var resumeBytes = File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0L;
        var isResumed = resumeBytes > 0;

        _logger.LogInformation("Pobieranie modelu {ModelName} do {Path}", modelName, destinationPath);
        using var request = new HttpRequestMessage(HttpMethod.Get, descriptor.Url);
        if (resumeBytes > 0)
        {
            request.Headers.Range = new RangeHeaderValue(resumeBytes, null);
        }

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (resumeBytes > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            File.Move(tempPath, destinationPath, overwrite: true);
            return destinationPath;
        }

        if (resumeBytes > 0 && response.StatusCode == HttpStatusCode.OK)
        {
            // Serwer nie wsparł range - restart od początku, ale bez wycieku śmieciowych plików.
            File.Delete(tempPath);
            resumeBytes = 0;
            isResumed = false;
        }

        response.EnsureSuccessStatusCode();

        var now = DateTimeOffset.UtcNow;
        var startedAt = now;
        var lastReportedAt = now;

        var responseLength = response.Content.Headers.ContentLength;
        var totalBytes = responseLength.HasValue
            ? responseLength.Value + resumeBytes
            : descriptor.ApproximateSizeBytes;

        progress?.Report(new ModelDownloadProgress
        {
            ModelName = modelName,
            DownloadedBytes = resumeBytes,
            TotalBytes = totalBytes,
            FractionCompleted = totalBytes is > 0 ? resumeBytes / (double)totalBytes.Value : 0,
            IsResumedDownload = isResumed
        });

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var destination = new FileStream(
                         tempPath,
                         resumeBytes > 0 ? FileMode.Append : FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 128 * 1024,
                         useAsync: true))
        {
            var buffer = new byte[128 * 1024];
            var downloadedBytes = resumeBytes;

            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                downloadedBytes += read;

                var timestamp = DateTimeOffset.UtcNow;
                if (timestamp - lastReportedAt < TimeSpan.FromMilliseconds(200))
                {
                    continue;
                }

                var elapsed = timestamp - startedAt;
                var downloadedThisRun = Math.Max(1, downloadedBytes - resumeBytes);
                var bytesPerSecond = downloadedThisRun / Math.Max(0.1, elapsed.TotalSeconds);
                TimeSpan? eta = null;

                if (totalBytes is > 0 && bytesPerSecond > 1)
                {
                    var remainingBytes = Math.Max(0, totalBytes.Value - downloadedBytes);
                    eta = TimeSpan.FromSeconds(remainingBytes / bytesPerSecond);
                }

                progress?.Report(new ModelDownloadProgress
                {
                    ModelName = modelName,
                    DownloadedBytes = downloadedBytes,
                    TotalBytes = totalBytes,
                    FractionCompleted = totalBytes is > 0 ? Math.Min(1, downloadedBytes / (double)totalBytes.Value) : 0,
                    EstimatedRemaining = eta,
                    IsResumedDownload = isResumed
                });
                lastReportedAt = timestamp;
            }
        }

        var info = new FileInfo(tempPath);
        if (!info.Exists || info.Length < descriptor.MinValidBytes)
        {
            throw new InvalidOperationException("Pobrany model jest zbyt mały i wygląda na uszkodzony.");
        }

        File.Move(tempPath, destinationPath, overwrite: true);
        progress?.Report(new ModelDownloadProgress
        {
            ModelName = modelName,
            DownloadedBytes = info.Length,
            TotalBytes = info.Length,
            FractionCompleted = 1,
            IsResumedDownload = isResumed
        });

        return destinationPath;
    }

    private readonly record struct ModelSource(
        string FileName,
        string Url,
        long MinValidBytes,
        long? ApproximateSizeBytes);
}
