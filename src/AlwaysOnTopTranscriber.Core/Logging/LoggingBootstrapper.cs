using System.IO;
using AlwaysOnTopTranscriber.Core.Models;
using Serilog;
using Serilog.Events;

namespace AlwaysOnTopTranscriber.Core.Logging;

public static class LoggingBootstrapper
{
    // Serilog rolling file daje stabilne logi długich sesji i prostą diagnostykę po błędzie użytkownika.
    public static ILogger BuildLogger(AppPaths appPaths)
    {
        var logFilePattern = Path.Combine(appPaths.LogsDirectory, "app-.log");
        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .WriteTo.File(
                path: logFilePattern,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateLogger();
    }
}
