using FluentAssertions;
using LifeSim.Console.Configuration;
using LifeSim.Console.Logging;
using Xunit;

namespace LifeSim.Console.Tests;

public class LoggingSetupTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"lifesim-logs-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Create_WritesInformationToRollingFile()
    {
        var options = new LoggingOptions { Directory = _directory };

        using (var logger = LoggingSetup.Create(options, verbose: false))
        {
            logger.Information("info-marker");
        }

        ReadAllLogText().Should().Contain("info-marker");
    }

    [Fact]
    public void Create_WithVerbose_IncludesDebugEntries()
    {
        var options = new LoggingOptions { Directory = _directory };

        using (var logger = LoggingSetup.Create(options, verbose: true))
        {
            logger.Debug("debug-marker");
            logger.Information("info-marker");
        }

        var text = ReadAllLogText();
        text.Should().Contain("debug-marker");
        text.Should().Contain("info-marker");
    }

    [Fact]
    public void Create_WithoutVerbose_SuppressesDebugEntries()
    {
        var options = new LoggingOptions { Directory = _directory };

        using (var logger = LoggingSetup.Create(options, verbose: false))
        {
            logger.Debug("debug-marker");
            logger.Information("info-marker");
        }

        var text = ReadAllLogText();
        text.Should().NotContain("debug-marker");
        text.Should().Contain("info-marker");
    }

    private string ReadAllLogText() =>
        string.Join(
            Environment.NewLine,
            Directory.GetFiles(_directory, "*.log").Select(File.ReadAllText));
}
