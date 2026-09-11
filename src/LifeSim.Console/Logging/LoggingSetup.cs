using LifeSim.Console.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace LifeSim.Console.Logging;

/// <summary>
/// Configures the rolling file logger for application events. Size-based rotation plus
/// retained-file limits keep the log directory bounded. <c>--verbose</c> lowers the
/// minimum level for both the file and the console.
/// </summary>
public static class LoggingSetup
{
    public static Logger Create(LoggingOptions options, bool verbose, string? directoryOverride = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var directory = string.IsNullOrWhiteSpace(directoryOverride) ? options.Directory : directoryOverride;
        Directory.CreateDirectory(directory);

        var minimumLevel = verbose ? LogEventLevel.Debug : LogEventLevel.Information;

        return new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .WriteTo.File(
                Path.Combine(directory, "lifesim-.log"),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: options.FileSizeLimitBytes,
                retainedFileCountLimit: options.RetainedFileCount,
                rollOnFileSizeLimit: true)
            .WriteTo.Console(
                restrictedToMinimumLevel: verbose ? LogEventLevel.Debug : LogEventLevel.Warning)
            .CreateLogger();
    }
}
