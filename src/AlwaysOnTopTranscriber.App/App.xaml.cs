using System.Threading;
using System.Windows;
using AlwaysOnTopTranscriber.Core.Audio;
using AlwaysOnTopTranscriber.Core.Logging;
using AlwaysOnTopTranscriber.Core.Models;
using AlwaysOnTopTranscriber.Core.Sessions;
using AlwaysOnTopTranscriber.Core.Storage;
using AlwaysOnTopTranscriber.Core.Transcription;
using AlwaysOnTopTranscriber.Core.Utilities;
using Microsoft.Extensions.Logging;
using Serilog;

namespace AlwaysOnTopTranscriber.App;

public partial class App : Application
{
    private ILoggerFactory? _loggerFactory;
    private ITranscriptionSessionService? _sessionService;
    private IAudioCaptureService? _audioCaptureService;
    private LocalWhisperEngine? _localWhisperEngine;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var appPaths = AppPaths.CreateDefault();

            // Initialize Serilog to write logs to file
            var serilogLogger = LoggingBootstrapper.BuildLogger(appPaths);
            Log.Logger = serilogLogger;

            // Create LoggerFactory that uses Serilog for file output
            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information);
                builder.AddSerilog(serilogLogger);
            });

            var settingsService = new SettingsService(appPaths, _loggerFactory.CreateLogger<SettingsService>());
            var settings = settingsService.Load();
            var modelManager = new ModelManager(appPaths, _loggerFactory.CreateLogger<ModelManager>());
            var audioCaptureService = new WasapiLoopbackCaptureService(
                _loggerFactory.CreateLogger<WasapiLoopbackCaptureService>());
            var localWhisperEngine = new LocalWhisperEngine(
                _loggerFactory.CreateLogger<LocalWhisperEngine>(),
                settings);
            var engineFactory = new TranscriptionEngineFactory(localWhisperEngine, new CloudEnginePlaceholder());
            var sessionRepository = new SessionRepository(appPaths, _loggerFactory.CreateLogger<SessionRepository>());
            await sessionRepository.InitializeAsync(CancellationToken.None);
            var transcriptFileWriter = new TranscriptFileWriter(appPaths);
            var sessionService = new TranscriptionSessionService(
                audioCaptureService,
                new AudioChunker(_loggerFactory.CreateLogger<AudioChunker>()),
                engineFactory,
                settingsService,
                sessionRepository,
                transcriptFileWriter,
                appPaths,
                _loggerFactory.CreateLogger<TranscriptionSessionService>());

            _audioCaptureService = audioCaptureService;
            _localWhisperEngine = localWhisperEngine;
            _sessionService = sessionService;

            var window = new MainWindow(
                appPaths,
                settingsService,
                modelManager,
                sessionRepository,
                sessionService);
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "Transcriber Lite - startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_sessionService is not null &&
            (_sessionService.CurrentState == SessionState.Recording || _sessionService.CurrentState == SessionState.Paused))
        {
            await _sessionService.StopAsync();
        }

        _audioCaptureService?.Dispose();
        _localWhisperEngine?.Dispose();
        _loggerFactory?.Dispose();

        // Flush and close Serilog
        Log.CloseAndFlush();

        base.OnExit(e);
    }
}
