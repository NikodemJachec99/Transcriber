using System;

namespace AlwaysOnTopTranscriber.Core.Theming;

public interface IThemeService
{
    bool IsDarkMode { get; }

    event EventHandler<bool>? ThemeChanged;

    void ApplyTheme(bool darkMode, bool persist = true);

    void ToggleTheme();
}
