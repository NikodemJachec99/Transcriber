using AlwaysOnTopTranscriber.Hybrid.Services.System;
using Microsoft.Extensions.Logging;

namespace AlwaysOnTopTranscriber.Hybrid.Services.Windows;

public sealed class WindowsHotkeyService(ILogger<WindowsHotkeyService> logger) : IHotkeyService
{
    public void Initialize()
    {
        logger.LogInformation("Windows hotkey service initialized (hybrid scaffold).");
    }

    public void Shutdown()
    {
        logger.LogInformation("Windows hotkey service shutdown.");
    }
}
