using System;
using System.Threading;
using System.Threading.Tasks;
using AlwaysOnTopTranscriber.Core.Models;

namespace AlwaysOnTopTranscriber.Core.Utilities;

public interface ISettingsService
{
    event EventHandler<AppSettings>? SettingsChanged;

    AppSettings Load();

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
