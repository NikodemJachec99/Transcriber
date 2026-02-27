using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AlwaysOnTopTranscriber.Core.Models;
using Microsoft.Extensions.Logging;

namespace AlwaysOnTopTranscriber.Core.Utilities;

public sealed class SettingsService : ISettingsService
{
    private readonly AppPaths _appPaths;
    private readonly ILogger<SettingsService> _logger;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SettingsService(AppPaths appPaths, ILogger<SettingsService> logger)
    {
        _appPaths = appPaths;
        _logger = logger;
    }

    public event EventHandler<AppSettings>? SettingsChanged;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_appPaths.SettingsPath))
            {
                var defaults = new AppSettings();
                SaveSync(defaults);
                return defaults;
            }

            var json = File.ReadAllText(_appPaths.SettingsPath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
            if (loaded is null)
            {
                return new AppSettings();
            }

            return loaded;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się wczytać settings.json, używam domyślnych ustawień.");
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            settings.UpdatedAtUtc = DateTimeOffset.UtcNow;
            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            var temp = $"{_appPaths.SettingsPath}.tmp";
            await File.WriteAllTextAsync(temp, json, cancellationToken).ConfigureAwait(false);
            File.Move(temp, _appPaths.SettingsPath, true);
            SettingsChanged?.Invoke(this, settings);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private void SaveSync(AppSettings settings)
    {
        settings.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        File.WriteAllText(_appPaths.SettingsPath, json);
        SettingsChanged?.Invoke(this, settings);
    }
}
