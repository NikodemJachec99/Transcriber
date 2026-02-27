using AlwaysOnTopTranscriber.Core.Models;
using AlwaysOnTopTranscriber.Core.Sessions;
using AlwaysOnTopTranscriber.Core.Utilities;
using AlwaysOnTopTranscriber.Hybrid.Services.Localization;

namespace AlwaysOnTopTranscriber.Hybrid.Services;

public sealed class LiveSessionState : IDisposable
{
    private readonly ITranscriptionSessionService _sessionService;
    private readonly ISettingsService _settingsService;
    private readonly IUiLocalizationService _localizationService;
    private AppSettings _settings;

    public LiveSessionState(
        ITranscriptionSessionService sessionService,
        ISettingsService settingsService,
        IUiLocalizationService localizationService)
    {
        _sessionService = sessionService;
        _settingsService = settingsService;
        _localizationService = localizationService;
        _settings = settingsService.Load();

        SessionName = BuildDefaultSessionName();
        SelectedLanguage = NormalizeLanguage(_settings.Language);
        _uiMode = NormalizeUiMode(_settings.UiMode);
        StatusText = _localizationService[UiTextKeys.StatusReady];

        _sessionService.RecordingStateChanged += HandleRecordingStateChanged;
        _sessionService.LiveTranscriptUpdated += HandleLiveTranscriptUpdated;
        _sessionService.WarningRaised += HandleWarningRaised;
        _sessionService.SessionSaved += HandleSessionSaved;
        _localizationService.LanguageChanged += HandleLanguageChanged;
    }

    public event Action? Changed;

    public IReadOnlyList<LanguageOption> LanguageOptions =>
    [
        new("auto", _localizationService[UiTextKeys.TranscriptionLanguageAuto]),
        new("pl", _localizationService[UiTextKeys.TranscriptionLanguagePl]),
        new("en", _localizationService[UiTextKeys.TranscriptionLanguageEn])
    ];

    public string SessionName { get; set; }

    public string UiMode
    {
        get => _uiMode;
        set
        {
            var normalized = NormalizeUiMode(value);
            if (_uiMode == normalized)
            {
                return;
            }

            _uiMode = normalized;
            _settings.UiMode = normalized;
            _ = _settingsService.SaveAsync(_settings, CancellationToken.None);
            NotifyChanged();
        }
    }

    public bool IsAdvancedMode => string.Equals(UiMode, "advanced", StringComparison.OrdinalIgnoreCase);

    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            var normalized = NormalizeLanguage(value);
            if (_selectedLanguage == normalized)
            {
                return;
            }

            _selectedLanguage = normalized;
            _settings.Language = normalized;
            _ = _settingsService.SaveAsync(_settings, CancellationToken.None);
            NotifyChanged();
        }
    }

    public bool IsRecording { get; private set; }

    public string StatusText { get; private set; } = string.Empty;

    public string ElapsedText { get; private set; } = "00:00:00";

    public double AudioLevelPercent { get; private set; }

    public string FullText { get; private set; } = string.Empty;

    public int PendingAudioFrames { get; private set; }

    public int PendingChunks { get; private set; }

    public int ProcessedChunks { get; private set; }

    public string ProcessingLagText { get; private set; } = "00:00";

    public SessionEntity? LastSavedSession { get; private set; }

    private string _selectedLanguage = "auto";
    private string _uiMode = "basic";
    private bool _warningVisible;

    public async Task ToggleRecordingAsync()
    {
        _settings.Language = SelectedLanguage;
        await _settingsService.SaveAsync(_settings, CancellationToken.None).ConfigureAwait(false);

        if (IsRecording)
        {
            await _sessionService.StopAsync().ConfigureAwait(false);
            SessionName = BuildDefaultSessionName();
            NotifyChanged();
            return;
        }

        await _sessionService.StartAsync(SessionName).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _sessionService.RecordingStateChanged -= HandleRecordingStateChanged;
        _sessionService.LiveTranscriptUpdated -= HandleLiveTranscriptUpdated;
        _sessionService.WarningRaised -= HandleWarningRaised;
        _sessionService.SessionSaved -= HandleSessionSaved;
        _localizationService.LanguageChanged -= HandleLanguageChanged;
    }

    private void HandleRecordingStateChanged(object? sender, bool isRecording)
    {
        IsRecording = isRecording;
        _warningVisible = false;
        StatusText = isRecording
            ? _localizationService[UiTextKeys.StatusListening]
            : _localizationService[UiTextKeys.StatusReady];
        if (!isRecording)
        {
            AudioLevelPercent = 0;
            PendingAudioFrames = 0;
            PendingChunks = 0;
        }

        NotifyChanged();
    }

    private void HandleLiveTranscriptUpdated(object? sender, LiveTranscriptUpdate update)
    {
        ElapsedText = update.Elapsed.ToString(@"hh\:mm\:ss");
        AudioLevelPercent = Math.Round(Math.Clamp(update.SmoothedAudioLevel * 100d, 0d, 100d), 1);
        PendingAudioFrames = update.PendingAudioFrames;
        PendingChunks = update.PendingChunks;
        ProcessedChunks = update.ProcessedChunks;
        ProcessingLagText = update.ProcessingLag.ToString(@"mm\:ss");
        FullText = update.FullText;
        NotifyChanged();
    }

    private void HandleWarningRaised(object? sender, string warning)
    {
        _warningVisible = true;
        StatusText = warning;
        NotifyChanged();
    }

    private void HandleSessionSaved(object? sender, SessionEntity session)
    {
        LastSavedSession = session;
        NotifyChanged();
    }

    private void HandleLanguageChanged(object? sender, string _)
    {
        if (!_warningVisible)
        {
            StatusText = IsRecording
                ? _localizationService[UiTextKeys.StatusListening]
                : _localizationService[UiTextKeys.StatusReady];
        }

        NotifyChanged();
    }

    private static string BuildDefaultSessionName() => $"Sesja_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";

    private static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return "auto";
        }

        var normalized = language.Trim().ToLowerInvariant();
        return normalized is "auto" or "pl" or "en" ? normalized : "auto";
    }

    private static string NormalizeUiMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return "basic";
        }

        return string.Equals(mode.Trim(), "advanced", StringComparison.OrdinalIgnoreCase)
            ? "advanced"
            : "basic";
    }

    private void NotifyChanged() => Changed?.Invoke();
}
