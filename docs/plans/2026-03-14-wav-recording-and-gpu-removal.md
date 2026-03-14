# WAV Recording + GPU Removal Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Enable reliable 5-hour recordings by streaming audio to WAV on disk, remove GPU acceleration, and fix deferred transcription to read from WAV instead of bounded in-memory channel.

**Architecture:** During recording, raw audio frames are converted to PCM16 mono 16kHz and streamed directly to a WAV file on disk (zero RAM growth). When user clicks "Transcribe Now", the WAV file is read back, re-chunked with silence filtering, and transcribed one chunk at a time. Session output includes .wav alongside .txt/.md/.json.

**Tech Stack:** C# .NET 8, WPF, NAudio (WaveFileWriter/WaveFileReader for WAV I/O), Whisper.net 1.9.0 (CPU only)

---

## Task 1: Remove GPU acceleration from LocalWhisperEngine

**Files:**
- Modify: `src/AlwaysOnTopTranscriber.Core/Transcription/LocalWhisperEngine.cs`

**Step 1: Strip GPU code from LocalWhisperEngine**

Remove these members and methods entirely:
- Field `_detectedGpuProvider` (line 22)
- Constructor lines calling `DetectGpuProvider()` and `ConfigureGpuEnvironmentVariables()` (lines 28-31)
- Method `ConfigureGpuEnvironmentVariables()` (lines 34-79)
- Method `DetectGpuProvider()` (lines 226-248)
- Method `TryDetectGpu()` (lines 253-304)
- Method `HasWindowsGpu()` (lines 310-355)

Simplify constructor to:
```csharp
public LocalWhisperEngine(ILogger<LocalWhisperEngine> logger, AppSettings? settings = null)
{
    _logger = logger;
    _settings = settings;
}
```

Remove `_detectedGpuProvider` field. Remove `using System.Diagnostics;` and `using System.Runtime.InteropServices;` if no longer needed.

**Step 2: Simplify EnsureFactoryAsync — always CPU**

Replace lines 165-172 with:
```csharp
_factory = WhisperFactory.FromPath(modelPath);
```

Remove the `if (factoryOptions.UseGpu)` block (lines 175-182), replace with:
```csharp
_logger.LogInformation("Załadowano model Whisper: {ModelPath}", modelPath);
```

**Step 3: Fix transcription logging — remove false GPU indicator**

Replace line 130:
```csharp
var gpuIndicator = realTimeRatio < 0.5 ? " [GPU DETECTED ✓]" : "";
```
With:
```csharp
var gpuIndicator = "";
```

Or just remove the variable and the `{GpuIndicator}` placeholder from the log template entirely.

**Step 4: Commit**
```bash
git add src/AlwaysOnTopTranscriber.Core/Transcription/LocalWhisperEngine.cs
git commit -m "Remove GPU acceleration from LocalWhisperEngine - CPU only"
```

---

## Task 2: Remove GPU settings from AppSettings and UI

**Files:**
- Modify: `src/AlwaysOnTopTranscriber.Core/Models/AppSettings.cs`
- Modify: `src/AlwaysOnTopTranscriber.App/MainWindow.xaml`
- Modify: `src/AlwaysOnTopTranscriber.App/MainWindow.xaml.cs`

**Step 1: Remove GPU properties from AppSettings**

In `AppSettings.cs`, delete lines 69-82 (the `TryGpuAcceleration` and `GpuProvider` properties with their XML doc comments).

**Step 2: Remove GPU UI from MainWindow.xaml**

Delete the entire GPU Acceleration Settings block (lines 367-385):
```xml
<!-- GPU Acceleration Settings -->
<StackPanel Margin="0,12,0,0">
    <CheckBox x:Name="EnableGpuAccelerationCheckBox" ... />
    <StackPanel Margin="0,8,0,0" x:Name="GpuProviderPanel">
        ...
    </StackPanel>
</StackPanel>
```

**Step 3: Remove GPU code from MainWindow.xaml.cs**

Remove these sections:
- Lines 57-71: GPU initialization block (`EnableGpuAccelerationCheckBox`, `GpuProviderPanel`, `GpuProviderComboBox`)
- Lines 176, 178-187: GPU save logic in `SaveSettingsAsync()` (the `_settings.TryGpuAcceleration` line and the GPU provider combobox reading block)
- Lines 251-252: GPU reset in `ResetAdvancedSettingsButton_OnClick()`
- Lines 256-274: Both event handlers `EnableGpuAccelerationCheckBox_OnToggled()` and `GpuProviderComboBox_OnSelectionChanged()`

**Step 4: Commit**
```bash
git add src/AlwaysOnTopTranscriber.Core/Models/AppSettings.cs src/AlwaysOnTopTranscriber.App/MainWindow.xaml src/AlwaysOnTopTranscriber.App/MainWindow.xaml.cs
git commit -m "Remove GPU settings from AppSettings and UI"
```

---

## Task 3: Remove GPU NuGet packages

**Files:**
- Modify: `src/AlwaysOnTopTranscriber.Core/AlwaysOnTopTranscriber.Core.csproj`
- Modify: `src/AlwaysOnTopTranscriber.App/AlwaysOnTopTranscriber.App.csproj`

**Step 1: Remove GPU packages from Core.csproj**

Remove these lines:
```xml
<PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.17.3" />
<PackageReference Include="Microsoft.ML.OnnxRuntime.Gpu" Version="1.17.3" />
<PackageReference Include="Whisper.net.Runtime.Cuda" Version="1.9.0" />
<!-- TODO: OpenVINO comment -->
<!-- <PackageReference Include="Whisper.net.Runtime.OpenVino" Version="1.9.0" /> -->
```

**Step 2: Remove RuntimeIdentifier from App.csproj**

Remove or simplify:
```xml
<!-- Ensure native runtimes (OpenVINO, CUDA) are included in publish for Windows x64 -->
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<PublishReadyToRun>true</PublishReadyToRun>
```

The `RuntimeIdentifier` comment references GPU. Remove the comment. Keep `RuntimeIdentifier` and `PublishReadyToRun` if they're needed for general deployment, but remove the GPU comment.

Actually keep `RuntimeIdentifier` since it's needed for native Whisper.net runtime deployment even on CPU. Just remove the GPU comment:
```xml
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<PublishReadyToRun>true</PublishReadyToRun>
```

**Step 3: Commit**
```bash
git add src/AlwaysOnTopTranscriber.Core/AlwaysOnTopTranscriber.Core.csproj src/AlwaysOnTopTranscriber.App/AlwaysOnTopTranscriber.App.csproj
git commit -m "Remove GPU NuGet packages (CUDA, OnnxRuntime.Gpu)"
```

---

## Task 4: Add WAV recording to TranscriptionSessionService

This is the core change. During recording, audio frames are converted to PCM16 mono and streamed to a WAV file on disk instead of being buffered in a channel.

**Files:**
- Modify: `src/AlwaysOnTopTranscriber.Core/Sessions/TranscriptionSessionService.cs`

**Step 1: Add WAV writer field and recording path**

Add fields near line 52:
```csharp
private NAudio.Wave.WaveFileWriter? _wavWriter;
private string? _recordingWavPath;
private readonly object _wavWriterLock = new();
```

**Step 2: Create WAV file in StartAsync**

In `StartAsync()`, after `ResetMetrics()` (around line 152), add:
```csharp
// Create WAV file for recording
var safeName = Utilities.FilenameSanitizer.Sanitize(_sessionName);
var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
_recordingWavPath = Path.Combine(_appPaths.TranscriptsDirectory, $"{safeName}_{stamp}.wav");
var wavFormat = new WaveFormat(16_000, 16, 1); // PCM16 mono 16kHz
_wavWriter = new WaveFileWriter(_recordingWavPath, wavFormat);
_logger.LogInformation("Nagrywanie audio do: {WavPath}", _recordingWavPath);
```

**Step 3: Replace RunChunkerAsync with RunRecorderAsync for deferred mode**

Add a new method that converts frames to PCM16 mono and writes to WAV:
```csharp
private async Task RunRecorderAsync(CancellationToken cancellationToken)
{
    if (_frameChannel is null || _activeSettings is null)
        return;

    var targetRate = 16_000;
    // Reuse AudioChunker's conversion logic by creating a temporary chunker
    // that just converts frames - but simpler: we can inline the conversion.
    //
    // Actually, we need the PCM16 conversion from AudioChunker.
    // The cleanest approach: make AudioChunker expose a ConvertFrames method,
    // or just write a converter inline.

    try
    {
        await foreach (var frame in ReadFramesAsync(_frameChannel.Reader, cancellationToken)
                           .ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pcm16 = ConvertFrameToPcm16Mono(frame, targetRate);
            if (pcm16.Length == 0)
                continue;

            lock (_wavWriterLock)
            {
                _wavWriter?.Write(pcm16, 0, pcm16.Length);
            }
        }
    }
    catch (OperationCanceledException) { }
    catch (Exception ex)
    {
        _pipelineError ??= ex;
        _logger.LogError(ex, "Błąd w recorderze audio.");
        WarningRaised?.Invoke(this, "Błąd nagrywania audio.");
    }
    finally
    {
        lock (_wavWriterLock)
        {
            _wavWriter?.Dispose();
            _wavWriter = null;
        }
    }
}
```

**Important:** The `ConvertFrameToPcm16Mono` method currently lives in `AudioChunker` as a private method. We need to either:
- Make it `internal static` so both classes can use it, OR
- Extract it to a shared utility class

**Recommended: Extract to `AudioConverter` static class** in `Audio/AudioConverter.cs`.

**Step 4: Switch StartAsync to use RunRecorderAsync in deferred mode**

In `StartAsync()`, replace the existing deferred block (lines 203-215):
```csharp
if (_activeSettings.EnableDeferredTranscription)
{
    _chunkerTask = RunRecorderAsync(_sessionCts.Token);
    _transcriberTask = Task.CompletedTask;
    _transcriptionStarted = false;
    _totalChunksCreatedDuringRecording = 0;
    _logger.LogInformation("Deferred transcription enabled - audio nagrywane do WAV");
}
else
{
    // Live mode: use original chunker + transcriber pipeline
    _chunkerTask = RunChunkerAsync(_sessionCts.Token);
    _transcriberTask = RunTranscriberAsync(_sessionCts.Token);
    _transcriptionStarted = true;
}
```

**Note:** In live mode, we still use the old chunker+transcriber pipeline but ALSO write to WAV. For simplicity, we can have live mode also write to WAV by adding WAV writing to `RunChunkerAsync`. But the user primarily uses deferred mode, so focus on that.

**Step 5: Update StopAsync to handle WAV closing**

In `StopAsync()`, the deferred early-return block (lines 354-365), update to log the WAV path:
```csharp
if (_activeSettings?.EnableDeferredTranscription == true && !_transcriptionStarted)
{
    // WAV was already closed by RunRecorderAsync completing
    SetSessionState(SessionState.Recorded);
    RecordingStateChanged?.Invoke(this, false);
    PublishUpdate();
    _logger.LogInformation("Nagranie zakończone. WAV: {WavPath}. Transkrypcja ręczna.",
        _recordingWavPath);
    return;
}
```

**Step 6: Update CleanupState**

In `CleanupState()`, don't null out `_recordingWavPath` until after transcription is complete:
```csharp
// Don't clear _recordingWavPath here - needed for deferred transcription
```

**Step 7: Commit**
```bash
git add src/AlwaysOnTopTranscriber.Core/Sessions/TranscriptionSessionService.cs
git commit -m "Add WAV recording during capture - stream audio to disk"
```

---

## Task 5: Extract AudioConverter utility

**Files:**
- Create: `src/AlwaysOnTopTranscriber.Core/Audio/AudioConverter.cs`
- Modify: `src/AlwaysOnTopTranscriber.Core/Audio/AudioChunker.cs`
- Modify: `src/AlwaysOnTopTranscriber.Core/Sessions/TranscriptionSessionService.cs`

**Step 1: Create AudioConverter.cs**

Extract `ConvertFrameToPcm16Mono`, `DecodeToMonoFloat`, `ReadSample`, and `Resample` from `AudioChunker` into a static class:

```csharp
using System;
using System.Buffers.Binary;
using AlwaysOnTopTranscriber.Core.Models;
using NAudio.Wave;

namespace AlwaysOnTopTranscriber.Core.Audio;

/// <summary>
/// Converts audio frames from any format to PCM16 mono at a target sample rate.
/// Used by both AudioChunker (for chunking) and TranscriptionSessionService (for WAV recording).
/// </summary>
internal static class AudioConverter
{
    public static byte[] ToPcm16Mono(AudioFrame frame, int targetSampleRate)
    {
        // Move the ConvertFrameToPcm16Mono logic here (from AudioChunker lines 118-143)
        // Move DecodeToMonoFloat (lines 146-172)
        // Move ReadSample (lines 174-208)
        // Move Resample (lines 211-238)
    }
}
```

**Step 2: Update AudioChunker to use AudioConverter**

Replace the private conversion methods with calls to `AudioConverter.ToPcm16Mono(frame, targetRate)`.

**Step 3: Update TranscriptionSessionService.RunRecorderAsync to use AudioConverter**

```csharp
var pcm16 = AudioConverter.ToPcm16Mono(frame, targetRate);
```

**Step 4: Commit**
```bash
git add src/AlwaysOnTopTranscriber.Core/Audio/AudioConverter.cs src/AlwaysOnTopTranscriber.Core/Audio/AudioChunker.cs src/AlwaysOnTopTranscriber.Core/Sessions/TranscriptionSessionService.cs
git commit -m "Extract AudioConverter utility for shared PCM16 conversion"
```

---

## Task 6: Rewrite StartTranscriptionAsync to read from WAV file

**Files:**
- Modify: `src/AlwaysOnTopTranscriber.Core/Sessions/TranscriptionSessionService.cs`

**Step 1: Add method to read WAV and produce AudioChunks**

```csharp
private async IAsyncEnumerable<AudioChunk> ReadChunksFromWavAsync(
    string wavPath,
    int chunkLengthSeconds,
    float silenceRmsThreshold,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    using var reader = new NAudio.Wave.WaveFileReader(wavPath);
    var sampleRate = reader.WaveFormat.SampleRate; // Should be 16000
    var bytesPerSecond = sampleRate * sizeof(short); // PCM16 mono
    var chunkSizeBytes = chunkLengthSeconds * bytesPerSecond;
    var buffer = new byte[chunkSizeBytes];
    var sequence = 0;
    var offset = TimeSpan.Zero;

    while (true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytesRead = await reader.ReadAsync(buffer, 0, chunkSizeBytes, cancellationToken)
            .ConfigureAwait(false);
        if (bytesRead == 0)
            break;

        var chunkData = bytesRead == chunkSizeBytes ? buffer : buffer[..bytesRead];
        var duration = TimeSpan.FromSeconds((double)bytesRead / bytesPerSecond);

        // Skip silent chunks for transcription (Whisper hallucinates on silence)
        if (!IsSilentChunk(chunkData, silenceRmsThreshold))
        {
            yield return new AudioChunk(
                chunkData.ToArray(), sampleRate, offset, duration, sequence);
        }

        offset += duration;
        sequence++;
    }
}
```

Note: `IsSilentChunk` already exists in `AudioChunker`. Extract it to `AudioConverter` or make it `internal static` in `AudioChunker`.

**Step 2: Rewrite StartTranscriptionAsync**

Replace the body of `StartTranscriptionAsync()` (lines 960-1042):

```csharp
public async Task StartTranscriptionAsync()
{
    await _lifecycleLock.WaitAsync().ConfigureAwait(false);
    try
    {
        if (CurrentState != SessionState.Recorded)
        {
            _logger.LogWarning("Transkrypcja tylko z stanu 'Recorded', aktualny: {State}", CurrentState);
            return;
        }

        if (_transcriptionStarted || string.IsNullOrEmpty(_recordingWavPath) || !File.Exists(_recordingWavPath))
        {
            _logger.LogError("Brak pliku WAV do transkrypcji: {Path}", _recordingWavPath);
            return;
        }

        _transcriptionStarted = true;
        SetSessionState(SessionState.Transcribing);

        // Count total chunks for progress reporting
        var chunkLength = _activeSettings?.ChunkLengthSeconds ?? 10;
        var silenceThreshold = Math.Clamp(_activeSettings?.SilenceRmsThreshold ?? 0.003f, 0.0005f, 0.05f);

        // Calculate approximate total chunks from WAV duration
        using (var wavInfo = new NAudio.Wave.WaveFileReader(_recordingWavPath))
        {
            var totalSeconds = wavInfo.TotalTime.TotalSeconds;
            _totalChunksCreatedDuringRecording = (long)Math.Ceiling(totalSeconds / chunkLength);
        }

        _logger.LogInformation("Rozpoczęto transkrypcję z WAV: {Path}, szacowane chunki: {Total}",
            _recordingWavPath, _totalChunksCreatedDuringRecording);

        var engine = _engineFactory.Create(_activeSettings!);
        _engineType = engine.EngineName;
        var modelPath = ResolveModelPath(_activeSettings!);

        var request = new TranscriptionRequest
        {
            ModelName = _activeSettings!.ModelName,
            ModelPath = modelPath,
            Language = _activeSettings.Language,
            AutoPunctuation = _activeSettings.AutoPunctuation
        };

        var cts = new CancellationTokenSource();

        await foreach (var chunk in ReadChunksFromWavAsync(
            _recordingWavPath, chunkLength, silenceThreshold, cts.Token)
            .ConfigureAwait(false))
        {
            var result = await engine.TranscribeAsync(chunk, request, cts.Token)
                .ConfigureAwait(false);
            Interlocked.Increment(ref _chunksProcessed);
            _aggregator.Apply(result);
            Interlocked.Increment(ref _transcriptVersion);
            PublishUpdate();
        }

        // Save session
        var endUtc = DateTimeOffset.UtcNow;
        var transcript = BuildTranscriptTextFromSettings();
        var segments = _aggregator.Snapshot();
        var snapshot = new SessionSnapshot
        {
            Name = _sessionName,
            StartTimeUtc = _sessionStartUtc,
            EndTimeUtc = endUtc,
            EngineType = _engineType,
            ModelName = _modelName,
            Segments = segments,
            TranscriptText = transcript
        };

        var files = await _transcriptFileWriter.WriteAsync(snapshot, CancellationToken.None)
            .ConfigureAwait(false);
        var session = new SessionEntity
        {
            Name = _sessionName,
            StartTimeUtc = _sessionStartUtc,
            EndTimeUtc = endUtc,
            Duration = GetRecordingElapsed(),
            MarkdownPath = files.MarkdownPath,
            JsonPath = files.JsonPath,
            TextPath = files.TextPath,
            AudioPath = _recordingWavPath,
            TranscriptText = transcript,
            EngineType = _engineType,
            ModelName = _modelName,
            WordCount = CountWords(transcript)
        };

        session.Id = await _sessionRepository.AddSessionAsync(session, CancellationToken.None)
            .ConfigureAwait(false);
        SessionSaved?.Invoke(this, session);

        SetSessionState(SessionState.Completed);
        PublishUpdate();
        _logger.LogInformation("Transkrypcja zakończona. WAV: {WavPath}", _recordingWavPath);
        CleanupState();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Błąd transkrypcji z WAV");
        _pipelineError = ex;
        throw;
    }
    finally
    {
        _lifecycleLock.Release();
    }
}
```

**Step 3: Add IsSilentChunk to AudioConverter (or copy inline)**

Move the static `IsSilentChunk` method from `AudioChunker` to `AudioConverter` as `internal static`, so both classes can use it.

**Step 4: Commit**
```bash
git add src/AlwaysOnTopTranscriber.Core/Sessions/TranscriptionSessionService.cs src/AlwaysOnTopTranscriber.Core/Audio/AudioConverter.cs
git commit -m "Rewrite deferred transcription to read from WAV file"
```

---

## Task 7: Add AudioPath to SessionEntity, TranscriptFiles, and DB schema

**Files:**
- Modify: `src/AlwaysOnTopTranscriber.Core/Models/SessionEntity.cs`
- Modify: `src/AlwaysOnTopTranscriber.Core/Models/TranscriptFiles.cs`
- Modify: `src/AlwaysOnTopTranscriber.Core/Storage/SessionRepository.cs`

**Step 1: Add AudioPath to SessionEntity**

Add after `TextPath`:
```csharp
public string AudioPath { get; init; } = string.Empty;
```

**Step 2: Add AudioPath to TranscriptFiles**

```csharp
public string AudioPath { get; init; } = string.Empty;
```

**Step 3: Add audio_path column to SessionRepository**

In `InitializeCoreAsync`, add migration:
```csharp
await EnsureColumnExistsAsync(connection, "sessions", "audio_path", "TEXT NOT NULL DEFAULT ''", cancellationToken)
    .ConfigureAwait(false);
```

Update `AddSessionAsync` INSERT to include `audio_path`:
- Add `audio_path` to column list
- Add `$audio` parameter
- Add: `command.Parameters.AddWithValue("$audio", session.AudioPath);`

Update `ReadSession` to read `audio_path`:
- Add to SELECT column list (index 14)
- Add: `AudioPath = reader.IsDBNull(14) ? string.Empty : reader.GetString(14),`

Update all SELECT queries (in `BuildFtsQuery` and `BuildLikeQuery`) to include `s.audio_path` after `s.tags`.

**Step 4: Commit**
```bash
git add src/AlwaysOnTopTranscriber.Core/Models/SessionEntity.cs src/AlwaysOnTopTranscriber.Core/Models/TranscriptFiles.cs src/AlwaysOnTopTranscriber.Core/Storage/SessionRepository.cs
git commit -m "Add AudioPath to session model and database schema"
```

---

## Task 8: Update README — remove GPU, add WAV info

**Files:**
- Modify: `README.md`

**Step 1: Remove GPU section (section 6) entirely**

Remove the "Ustawienia GPU / CPU" section and all GPU-related tables/instructions.

**Step 2: Update model recommendations**

Remove GPU-specific recommendations. Simplify to CPU-only advice.

**Step 3: Add info about audio recording**

Add section:
```markdown
## Audio nagrań
Każda sesja automatycznie zapisuje plik `.wav` z pełnym nagraniem audio (PCM16, 16kHz mono).
- Plik WAV zapisywany jest obok transkryptów w `%AppData%\Transcriber\transcripts\`
- 1 godzina nagrania ≈ 110 MB
- 5 godzin ≈ 549 MB
```

**Step 4: Commit**
```bash
git add README.md
git commit -m "Update README - remove GPU, add WAV recording info"
```

---

## Task 9: Build verification and final commit

**Step 1: Build the solution**
```bash
dotnet build AlwaysOnTopTranscriber.sln -c Release
```
Expected: Build succeeds with 0 errors.

**Step 2: Fix any build errors**

Common issues:
- Missing `using` statements after refactoring
- References to removed GPU properties (`TryGpuAcceleration`, `GpuProvider`)
- Missing `AudioPath` in places that construct `SessionEntity`

**Step 3: Verify no GPU references remain**
```bash
grep -ri "TryGpuAcceleration\|GpuProvider\|EnableGpuAcceleration\|GpuProviderComboBox\|GpuProviderPanel\|HasWindowsGpu\|DetectGpuProvider\|ConfigureGpuEnvironment\|OnnxRuntime\.Gpu\|Runtime\.Cuda\|Runtime\.OpenVino" src/
```
Expected: No matches.

**Step 4: Final commit and push**
```bash
git add -A
git commit -m "WAV recording + GPU removal - complete implementation"
git push origin main
```

---

## Execution order summary

| Task | Description | Risk |
|------|-------------|------|
| 1 | Strip GPU from LocalWhisperEngine | Low — deletion only |
| 2 | Strip GPU from AppSettings + UI | Low — deletion only |
| 3 | Remove GPU NuGet packages | Low — deletion only |
| 4 | Add WAV recording to session service | **High** — core recording logic |
| 5 | Extract AudioConverter utility | Medium — refactor shared code |
| 6 | Rewrite deferred transcription from WAV | **High** — core transcription logic |
| 7 | Add AudioPath to model + DB | Low — additive schema change |
| 8 | Update README | Low — docs only |
| 9 | Build + verify | Required — catch integration issues |

Tasks 1-3 can be done in parallel (all deletions). Tasks 4-6 are sequential (depend on each other). Task 7 can be done in parallel with 4-6. Task 8 is independent. Task 9 must be last.
