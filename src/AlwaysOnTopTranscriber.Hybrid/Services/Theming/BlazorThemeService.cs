using AlwaysOnTopTranscriber.Core.Theming;
using AlwaysOnTopTranscriber.Core.Utilities;

namespace AlwaysOnTopTranscriber.Hybrid.Services.Theming;

public sealed class BlazorThemeService : IThemeService
{
    private readonly ISettingsService _settingsService;

    public BlazorThemeService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        IsDarkMode = _settingsService.Load().DarkMode;
    }

    public bool IsDarkMode { get; private set; }

    public event EventHandler<bool>? ThemeChanged;

    public void ApplyTheme(bool darkMode, bool persist = true)
    {
        IsDarkMode = darkMode;
        ThemeChanged?.Invoke(this, darkMode);

        if (!persist)
        {
            return;
        }

        _ = SaveDarkModeAsync(darkMode);
    }

    public void ToggleTheme()
    {
        ApplyTheme(!IsDarkMode, persist: true);
    }

    private async Task SaveDarkModeAsync(bool darkMode)
    {
        var settings = _settingsService.Load();
        if (settings.DarkMode == darkMode)
        {
            return;
        }

        settings.DarkMode = darkMode;
        await _settingsService.SaveAsync(settings, CancellationToken.None).ConfigureAwait(false);
    }
}
