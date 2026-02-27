using System.Threading;
using AlwaysOnTopTranscriber.Core.Audio;
using AlwaysOnTopTranscriber.Core.Models;
using AlwaysOnTopTranscriber.Core.Sessions;
using AlwaysOnTopTranscriber.Core.Storage;
using AlwaysOnTopTranscriber.Core.Theming;
using AlwaysOnTopTranscriber.Core.Transcription;
using AlwaysOnTopTranscriber.Core.Utilities;
using AlwaysOnTopTranscriber.Hybrid.Services.Localization;
using AlwaysOnTopTranscriber.Hybrid.Services;
using AlwaysOnTopTranscriber.Hybrid.Services.System;
using AlwaysOnTopTranscriber.Hybrid.Services.Theming;
using AlwaysOnTopTranscriber.Hybrid.Services.Windows;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AlwaysOnTopTranscriber.Hybrid;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(static fonts =>
            {
                // Reuse system font stack for now, visual system is in CSS tokens.
            });

        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        var appPaths = AppPaths.CreateDefault();
        builder.Services.AddSingleton(appPaths);

        builder.Services.AddSingleton<ISettingsService, SettingsService>();
        builder.Services.AddSingleton<IModelManager, ModelManager>();
        builder.Services.AddSingleton<IAudioCaptureService, WasapiLoopbackCaptureService>();
        builder.Services.AddSingleton<AudioChunker>();
        builder.Services.AddSingleton<LocalWhisperEngine>();
        builder.Services.AddSingleton<CloudEnginePlaceholder>();
        builder.Services.AddSingleton<ITranscriptionEngineFactory, TranscriptionEngineFactory>();
        builder.Services.AddSingleton<ISessionRepository, SessionRepository>();
        builder.Services.AddSingleton<ITranscriptFileWriter, TranscriptFileWriter>();
        builder.Services.AddSingleton<ITranscriptionSessionService, TranscriptionSessionService>();

        builder.Services.AddSingleton<IUiLocalizationService, UiLocalizationService>();
        builder.Services.AddSingleton<LiveSessionState>();
        builder.Services.AddSingleton<IThemeService, BlazorThemeService>();

        builder.Services.AddSingleton<ITrayService, WindowsTrayService>();
        builder.Services.AddSingleton<IHotkeyService, WindowsHotkeyService>();
        builder.Services.AddSingleton<IMiniWidgetHost, WindowsMiniWidgetHost>();
        builder.Services.AddSingleton<IWindowCoordinator, WindowCoordinator>();

        var app = builder.Build();

        // Keep storage schema in sync before the UI starts reading sessions.
        var repository = app.Services.GetRequiredService<ISessionRepository>();
        _ = repository.InitializeAsync(CancellationToken.None);

        return app;
    }
}
