namespace AlwaysOnTopTranscriber.Hybrid.Services.Localization;

public interface IUiLocalizationService
{
    string CurrentLanguage { get; }

    event EventHandler<string>? LanguageChanged;

    string this[string key] { get; }

    string Translate(string key);

    void SetLanguage(string language, bool persist = true);

    void ToggleLanguage();
}
