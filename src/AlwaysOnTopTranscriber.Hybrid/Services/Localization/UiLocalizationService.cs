using AlwaysOnTopTranscriber.Core.Utilities;

namespace AlwaysOnTopTranscriber.Hybrid.Services.Localization;

public sealed class UiLocalizationService : IUiLocalizationService
{
    private readonly ISettingsService _settingsService;

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Dictionaries =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["pl"] = BuildPolishDictionary(),
            ["en"] = BuildEnglishDictionary()
        };

    public UiLocalizationService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        CurrentLanguage = NormalizeLanguage(_settingsService.Load().UiLanguage);
    }

    public string CurrentLanguage { get; private set; }

    public event EventHandler<string>? LanguageChanged;

    public string this[string key] => Translate(key);

    public string Translate(string key)
    {
        if (TryGetValue(CurrentLanguage, key, out var localized))
        {
            return localized;
        }

        if (!string.Equals(CurrentLanguage, "en", StringComparison.OrdinalIgnoreCase)
            && TryGetValue("en", key, out var fallback))
        {
            return fallback;
        }

        return key;
    }

    public void SetLanguage(string language, bool persist = true)
    {
        var normalized = NormalizeLanguage(language);
        if (string.Equals(CurrentLanguage, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CurrentLanguage = normalized;
        LanguageChanged?.Invoke(this, normalized);

        if (!persist)
        {
            return;
        }

        _ = PersistLanguageAsync(normalized);
    }

    public void ToggleLanguage()
    {
        SetLanguage(CurrentLanguage == "pl" ? "en" : "pl", persist: true);
    }

    private static bool TryGetValue(string language, string key, out string value)
    {
        value = string.Empty;
        if (!Dictionaries.TryGetValue(language, out var dictionary) || dictionary is null)
        {
            return false;
        }

        if (!dictionary.TryGetValue(key, out var localized) || localized is null)
        {
            return false;
        }

        value = localized;
        return true;
    }

    private async Task PersistLanguageAsync(string language)
    {
        var settings = _settingsService.Load();
        if (string.Equals(NormalizeLanguage(settings.UiLanguage), language, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        settings.UiLanguage = language;
        await _settingsService.SaveAsync(settings, CancellationToken.None).ConfigureAwait(false);
    }

    private static string NormalizeLanguage(string? language)
    {
        if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            return "en";
        }

        return "pl";
    }

    private static IReadOnlyDictionary<string, string> BuildPolishDictionary()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [UiTextKeys.AppName] = "Transcriber v1.2",
            [UiTextKeys.NavLive] = "Live",
            [UiTextKeys.NavSessions] = "Sesje",
            [UiTextKeys.NavSettings] = "Ustawienia",
            [UiTextKeys.NavMiniWidget] = "Mini widget",
            [UiTextKeys.ThemeLight] = "Jasny",
            [UiTextKeys.ThemeDark] = "Ciemny",
            [UiTextKeys.ThemeLabel] = "Motyw",
            [UiTextKeys.UiLanguageLabel] = "Język UI",
            [UiTextKeys.UiModeLabel] = "Tryb",
            [UiTextKeys.UiModeBasic] = "Basic",
            [UiTextKeys.UiModeAdvanced] = "Advanced",
            [UiTextKeys.UiLanguagePl] = "Polski",
            [UiTextKeys.UiLanguageEn] = "English",
            [UiTextKeys.ButtonSave] = "Zapisz",
            [UiTextKeys.ButtonRefresh] = "Odśwież",
            [UiTextKeys.StatusReady] = "Gotowe",
            [UiTextKeys.StatusListening] = "Nasłuchiwanie",
            [UiTextKeys.StatusSaved] = "Zapisano",
            [UiTextKeys.StatusError] = "Błąd",
            [UiTextKeys.StatusLoading] = "Ładowanie",
            [UiTextKeys.TranscriptionLanguageLabel] = "Język rozmowy",
            [UiTextKeys.TranscriptionLanguageAuto] = "Auto",
            [UiTextKeys.TranscriptionLanguagePl] = "Polski",
            [UiTextKeys.TranscriptionLanguageEn] = "English",
            [UiTextKeys.SessionNameLabel] = "Nazwa sesji",
            [UiTextKeys.LiveTitle] = "Transkrypcja na żywo",
            [UiTextKeys.LiveSubtitle] = "Start, stop i zapis plików bez dodatkowych kroków.",
            [UiTextKeys.LivePrimaryActionStart] = "Start nagrywania",
            [UiTextKeys.LivePrimaryActionStop] = "Stop i zapisz",
            [UiTextKeys.LiveOpenMiniWidget] = "Tryb mini",
            [UiTextKeys.LiveElapsed] = "Czas",
            [UiTextKeys.LiveAudioLevel] = "Poziom audio",
            [UiTextKeys.LiveTranscriptTitle] = "Podgląd transkrypcji",
            [UiTextKeys.LiveTranscriptSubtitle] = "Tekst pojawia się na bieżąco podczas sesji.",
            [UiTextKeys.LiveTranscriptPlaceholder] = "Tu pojawi się transkrypcja.",
            [UiTextKeys.LiveLastSaved] = "Ostatni zapis",
            [UiTextKeys.LiveAdvancedTitle] = "Diagnostyka pipeline",
            [UiTextKeys.LiveAdvancedSubtitle] = "Dodatkowe metryki wydajności i kolejek.",
            [UiTextKeys.LivePendingAudio] = "Audio w kolejce",
            [UiTextKeys.LivePendingChunks] = "Chunki w kolejce",
            [UiTextKeys.LiveProcessedChunks] = "Przetworzone chunky",
            [UiTextKeys.LiveLag] = "Lag przetwarzania",
            [UiTextKeys.SessionsTitle] = "Sesje",
            [UiTextKeys.SessionsSubtitle] = "Historia transkrypcji z filtrowaniem.",
            [UiTextKeys.SessionsSearch] = "Szukaj po nazwie lub treści",
            [UiTextKeys.SessionsModelFilter] = "Model",
            [UiTextKeys.SessionsModelAll] = "Wszystkie",
            [UiTextKeys.SessionsEmpty] = "Brak zapisanych sesji.",
            [UiTextKeys.SessionsNoResults] = "Brak wyników dla bieżących filtrów.",
            [UiTextKeys.SessionsError] = "Nie udało się wczytać sesji.",
            [UiTextKeys.SessionsWords] = "słowa",
            [UiTextKeys.SessionsDuration] = "Czas",
            [UiTextKeys.SessionsRecorded] = "Nagrano",
            [UiTextKeys.SessionsFiles] = "Pliki",
            [UiTextKeys.SessionsOpen] = "Otwórz",
            [UiTextKeys.SessionsOpenFolder] = "Otwórz folder",
            [UiTextKeys.SessionsCopyPath] = "Kopiuj ścieżkę",
            [UiTextKeys.SessionsFileMissing] = "Brak pliku",
            [UiTextKeys.SessionsOpened] = "Otwarto",
            [UiTextKeys.SessionsCopied] = "Skopiowano ścieżkę",
            [UiTextKeys.SessionsOpenFailed] = "Nie udało się otworzyć pliku",
            [UiTextKeys.SettingsTitle] = "Ustawienia",
            [UiTextKeys.SettingsSubtitle] = "Konfiguracja aplikacji i modelu.",
            [UiTextKeys.SettingsSectionGeneral] = "General",
            [UiTextKeys.SettingsSectionModel] = "Model",
            [UiTextKeys.SettingsSectionUi] = "UI",
            [UiTextKeys.SettingsSectionAdvanced] = "Advanced",
            [UiTextKeys.SettingsModelSelect] = "Wybór modelu",
            [UiTextKeys.SettingsModelDownloadSelect] = "Model do pobrania",
            [UiTextKeys.SettingsModelDownload] = "Pobierz model",
            [UiTextKeys.SettingsModelDownloadThis] = "Pobierz",
            [UiTextKeys.SettingsModelCancelDownload] = "Anuluj pobieranie",
            [UiTextKeys.SettingsModelRefresh] = "Odśwież modele",
            [UiTextKeys.SettingsModelCatalog] = "Dostępne modele",
            [UiTextKeys.SettingsModelDownloaded] = "Pobrany",
            [UiTextKeys.SettingsModelNotDownloaded] = "Niepobrany",
            [UiTextKeys.SettingsModelDownloading] = "Pobieranie modelu",
            [UiTextKeys.SettingsModelProgress] = "Postęp pobierania",
            [UiTextKeys.SettingsModelCustomHint] = "Jeśli ścieżka własnego modelu nie jest pusta, ma priorytet nad wyborem modelu.",
            [UiTextKeys.SettingsModelDownloadDone] = "Model pobrany",
            [UiTextKeys.SettingsModelDownloadError] = "Błąd pobierania modelu",
            [UiTextKeys.SettingsModelDownloadCanceled] = "Pobieranie anulowane",
            [UiTextKeys.SettingsModelMissing] = "Wybrany model nie jest pobrany. Pobierz go lub ustaw ścieżkę własnego modelu.",
            [UiTextKeys.SettingsModelName] = "Nazwa modelu",
            [UiTextKeys.SettingsModelPath] = "Ścieżka własnego modelu",
            [UiTextKeys.SettingsChunkLength] = "Długość chunka (s)",
            [UiTextKeys.SettingsSilenceThreshold] = "Próg ciszy RMS",
            [UiTextKeys.SettingsAutoPunctuation] = "Auto interpunkcja",
            [UiTextKeys.SettingsDarkMode] = "Tryb ciemny",
            [UiTextKeys.SettingsSaved] = "Ustawienia zapisane",
            [UiTextKeys.SettingsAdvancedHint] = "Opcje zaawansowane są dla strojenia pipeline.",
            [UiTextKeys.MiniTitle] = "Mini widget",
            [UiTextKeys.MiniOpenFullPanel] = "Pełny panel",
            [UiTextKeys.MiniActionStart] = "Start",
            [UiTextKeys.MiniActionStop] = "Stop",
            [UiTextKeys.NotFound] = "Strona nie istnieje."
        };
    }

    private static IReadOnlyDictionary<string, string> BuildEnglishDictionary()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [UiTextKeys.AppName] = "Transcriber v1.2",
            [UiTextKeys.NavLive] = "Live",
            [UiTextKeys.NavSessions] = "Sessions",
            [UiTextKeys.NavSettings] = "Settings",
            [UiTextKeys.NavMiniWidget] = "Mini widget",
            [UiTextKeys.ThemeLight] = "Light",
            [UiTextKeys.ThemeDark] = "Dark",
            [UiTextKeys.ThemeLabel] = "Theme",
            [UiTextKeys.UiLanguageLabel] = "UI language",
            [UiTextKeys.UiModeLabel] = "Mode",
            [UiTextKeys.UiModeBasic] = "Basic",
            [UiTextKeys.UiModeAdvanced] = "Advanced",
            [UiTextKeys.UiLanguagePl] = "Polish",
            [UiTextKeys.UiLanguageEn] = "English",
            [UiTextKeys.ButtonSave] = "Save",
            [UiTextKeys.ButtonRefresh] = "Refresh",
            [UiTextKeys.StatusReady] = "Ready",
            [UiTextKeys.StatusListening] = "Listening",
            [UiTextKeys.StatusSaved] = "Saved",
            [UiTextKeys.StatusError] = "Error",
            [UiTextKeys.StatusLoading] = "Loading",
            [UiTextKeys.TranscriptionLanguageLabel] = "Conversation language",
            [UiTextKeys.TranscriptionLanguageAuto] = "Auto",
            [UiTextKeys.TranscriptionLanguagePl] = "Polish",
            [UiTextKeys.TranscriptionLanguageEn] = "English",
            [UiTextKeys.SessionNameLabel] = "Session name",
            [UiTextKeys.LiveTitle] = "Live transcription",
            [UiTextKeys.LiveSubtitle] = "Start, stop and save files without extra steps.",
            [UiTextKeys.LivePrimaryActionStart] = "Start recording",
            [UiTextKeys.LivePrimaryActionStop] = "Stop and save",
            [UiTextKeys.LiveOpenMiniWidget] = "Mini mode",
            [UiTextKeys.LiveElapsed] = "Elapsed",
            [UiTextKeys.LiveAudioLevel] = "Audio level",
            [UiTextKeys.LiveTranscriptTitle] = "Transcript preview",
            [UiTextKeys.LiveTranscriptSubtitle] = "Text appears live while the session is running.",
            [UiTextKeys.LiveTranscriptPlaceholder] = "The transcript will appear here.",
            [UiTextKeys.LiveLastSaved] = "Last saved",
            [UiTextKeys.LiveAdvancedTitle] = "Pipeline diagnostics",
            [UiTextKeys.LiveAdvancedSubtitle] = "Extra queue and performance metrics.",
            [UiTextKeys.LivePendingAudio] = "Pending audio",
            [UiTextKeys.LivePendingChunks] = "Pending chunks",
            [UiTextKeys.LiveProcessedChunks] = "Processed chunks",
            [UiTextKeys.LiveLag] = "Processing lag",
            [UiTextKeys.SessionsTitle] = "Sessions",
            [UiTextKeys.SessionsSubtitle] = "Transcript history with filtering.",
            [UiTextKeys.SessionsSearch] = "Search by name or transcript",
            [UiTextKeys.SessionsModelFilter] = "Model",
            [UiTextKeys.SessionsModelAll] = "All",
            [UiTextKeys.SessionsEmpty] = "No sessions saved yet.",
            [UiTextKeys.SessionsNoResults] = "No results for current filters.",
            [UiTextKeys.SessionsError] = "Failed to load sessions.",
            [UiTextKeys.SessionsWords] = "words",
            [UiTextKeys.SessionsDuration] = "Duration",
            [UiTextKeys.SessionsRecorded] = "Recorded",
            [UiTextKeys.SessionsFiles] = "Files",
            [UiTextKeys.SessionsOpen] = "Open",
            [UiTextKeys.SessionsOpenFolder] = "Open folder",
            [UiTextKeys.SessionsCopyPath] = "Copy path",
            [UiTextKeys.SessionsFileMissing] = "File missing",
            [UiTextKeys.SessionsOpened] = "Opened",
            [UiTextKeys.SessionsCopied] = "Path copied",
            [UiTextKeys.SessionsOpenFailed] = "Unable to open file",
            [UiTextKeys.SettingsTitle] = "Settings",
            [UiTextKeys.SettingsSubtitle] = "Application and model configuration.",
            [UiTextKeys.SettingsSectionGeneral] = "General",
            [UiTextKeys.SettingsSectionModel] = "Model",
            [UiTextKeys.SettingsSectionUi] = "UI",
            [UiTextKeys.SettingsSectionAdvanced] = "Advanced",
            [UiTextKeys.SettingsModelSelect] = "Model selection",
            [UiTextKeys.SettingsModelDownloadSelect] = "Model to download",
            [UiTextKeys.SettingsModelDownload] = "Download model",
            [UiTextKeys.SettingsModelDownloadThis] = "Download",
            [UiTextKeys.SettingsModelCancelDownload] = "Cancel download",
            [UiTextKeys.SettingsModelRefresh] = "Refresh models",
            [UiTextKeys.SettingsModelCatalog] = "Available models",
            [UiTextKeys.SettingsModelDownloaded] = "Downloaded",
            [UiTextKeys.SettingsModelNotDownloaded] = "Not downloaded",
            [UiTextKeys.SettingsModelDownloading] = "Downloading model",
            [UiTextKeys.SettingsModelProgress] = "Download progress",
            [UiTextKeys.SettingsModelCustomHint] = "If custom model path is set, it overrides the selected model.",
            [UiTextKeys.SettingsModelDownloadDone] = "Model downloaded",
            [UiTextKeys.SettingsModelDownloadError] = "Model download failed",
            [UiTextKeys.SettingsModelDownloadCanceled] = "Download canceled",
            [UiTextKeys.SettingsModelMissing] = "The selected model is not downloaded. Download it or set a custom model path.",
            [UiTextKeys.SettingsModelName] = "Model name",
            [UiTextKeys.SettingsModelPath] = "Custom model path",
            [UiTextKeys.SettingsChunkLength] = "Chunk length (s)",
            [UiTextKeys.SettingsSilenceThreshold] = "Silence RMS threshold",
            [UiTextKeys.SettingsAutoPunctuation] = "Auto punctuation",
            [UiTextKeys.SettingsDarkMode] = "Dark mode",
            [UiTextKeys.SettingsSaved] = "Settings saved",
            [UiTextKeys.SettingsAdvancedHint] = "Advanced options are intended for pipeline tuning.",
            [UiTextKeys.MiniTitle] = "Mini widget",
            [UiTextKeys.MiniOpenFullPanel] = "Full panel",
            [UiTextKeys.MiniActionStart] = "Start",
            [UiTextKeys.MiniActionStop] = "Stop",
            [UiTextKeys.NotFound] = "Page not found."
        };
    }
}
