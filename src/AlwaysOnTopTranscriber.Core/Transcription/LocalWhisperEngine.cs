using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlwaysOnTopTranscriber.Core.Models;
using Microsoft.Extensions.Logging;
using Whisper.net;

namespace AlwaysOnTopTranscriber.Core.Transcription;

public sealed class LocalWhisperEngine : ITranscriptionEngine, IDisposable
{
    private readonly ILogger<LocalWhisperEngine> _logger;
    private readonly SemaphoreSlim _modelLock = new(1, 1);
    private WhisperFactory? _factory;
    private string? _loadedModelPath;

    public LocalWhisperEngine(ILogger<LocalWhisperEngine> logger)
    {
        _logger = logger;
    }

    public string EngineName => "LocalWhisper";

    public async Task<TranscriptionChunkResult> TranscribeAsync(
        AudioChunk chunk,
        TranscriptionRequest request,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(request.ModelPath))
        {
            throw new FileNotFoundException(
                $"Nie znaleziono modelu Whisper: {request.ModelPath}. Ustaw model w Ustawieniach.");
        }

        await EnsureFactoryAsync(request.ModelPath, cancellationToken).ConfigureAwait(false);

        var segments = new List<TranscriptSegment>();
        await using var waveStream = BuildWavePcm16MonoStream(chunk);

        var builder = _factory!.CreateBuilder();
        if (!string.Equals(request.Language, "auto", StringComparison.OrdinalIgnoreCase))
        {
            builder = builder.WithLanguage(request.Language);
        }

        // Tworzymy procesor per chunk, bo to bezpieczny i przewidywalny model pracy dla długich sesji.
        using var processor = builder.Build();
        await foreach (var result in processor.ProcessAsync(waveStream).WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            var text = (result.Text ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                continue;
            }

            var start = chunk.StartOffset + result.Start;
            var end = chunk.StartOffset + result.End;
            segments.Add(new TranscriptSegment(start, end, text));
        }

        return new TranscriptionChunkResult
        {
            ChunkStartOffset = chunk.StartOffset,
            Segments = segments,
            IsPartial = false
        };
    }

    public void Dispose()
    {
        _factory?.Dispose();
        _modelLock.Dispose();
    }

    private async Task EnsureFactoryAsync(string modelPath, CancellationToken cancellationToken)
    {
        await _modelLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_factory is not null && string.Equals(_loadedModelPath, modelPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _factory?.Dispose();
            _factory = WhisperFactory.FromPath(modelPath);
            _loadedModelPath = modelPath;
            _logger.LogInformation("Załadowano model Whisper: {ModelPath}", modelPath);
        }
        finally
        {
            _modelLock.Release();
        }
    }

    private static MemoryStream BuildWavePcm16MonoStream(AudioChunk chunk)
    {
        var stream = new MemoryStream(chunk.Pcm16MonoData.Length + 44);
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            var blockAlign = (short)2;
            var byteRate = chunk.SampleRate * blockAlign;
            var dataLength = chunk.Pcm16MonoData.Length;

            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(chunk.SampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);
            writer.Write(chunk.Pcm16MonoData);
            writer.Flush();
        }

        stream.Position = 0;
        return stream;
    }
}
