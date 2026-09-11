using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using LifeSim.Console.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LifeSim.Console.Tests;

public class ConfigurationSystemTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IConfiguration BuildFromDictionary(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static (LifeSimOptions options, IReadOnlyList<ValidationResult> errors)
        BindFrom(Dictionary<string, string?> values) =>
        LifeSimConfigurationBuilder.BindAndValidate(BuildFromDictionary(values));

    // ── Default / Missing Config ──────────────────────────────────────────────

    [Fact]
    public void Defaults_AreUsed_WhenNoValuesProvided()
    {
        var (options, errors) = BindFrom([]);

        errors.Should().BeEmpty();
        options.Llm.Endpoint.Should().Be("http://127.0.0.1:1337/v1");
        options.Llm.Model.Should().Be("default");
        options.Ai.Enabled.Should().BeTrue();
        options.Ui.Theme.Should().Be("default");
        options.Ui.Verbosity.Should().Be("normal");
        options.Logging.RedactSensitiveContent.Should().BeFalse();
    }

    [Fact]
    public void LoggingDefaults_ResolveToUserLogDirectory()
    {
        var (options, errors) = BindFrom([]);

        errors.Should().BeEmpty();
        options.Logging.Directory.Should().Contain(".lifesim");
        options.Logging.Directory.Should().Contain("logs");
    }

    [Fact]
    public void LoggingOverrides_AreHonored()
    {
        var (options, errors) = BindFrom(new Dictionary<string, string?>
        {
            ["Logging:Directory"] = "C:/tmp/lifesim",
            ["Logging:RetainedFileCount"] = "2",
            ["Logging:RedactSensitiveContent"] = "true"
        });

        errors.Should().BeEmpty();
        options.Logging.Directory.Should().Be("C:/tmp/lifesim");
        options.Logging.RetainedFileCount.Should().Be(2);
        options.Logging.RedactSensitiveContent.Should().BeTrue();
    }

    [Fact]
    public void InvalidLoggingFileSize_ProducesValidationError()
    {
        var (_, errors) = BindFrom(new Dictionary<string, string?>
        {
            ["Logging:FileSizeLimitBytes"] = "10"
        });

        errors.Should().NotBeEmpty();
        errors.Should().Contain(e => e.MemberNames.Any(m => m.Contains("FileSizeLimitBytes")));
    }

    [Fact]
    public void MissingAppSettingsFile_FallsBackToFullDefaults()
    {
        var emptyDir = Path.Combine(Path.GetTempPath(), $"lifesim-missing-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyDir);

        try
        {
            var (options, errors) = LifeSimConfigurationBuilder.BindAndValidate(
                LifeSimConfigurationBuilder.Build(appSettingsBasePath: emptyDir));

            errors.Should().BeEmpty();
            options.Llm.Endpoint.Should().Be("http://127.0.0.1:1337/v1");
            options.Ai.Enabled.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(emptyDir, recursive: true);
        }
    }

    [Fact]
    public void EmptySavesPath_ResolvesToUserDefault()
    {
        var (options, errors) = BindFrom(new Dictionary<string, string?> { ["Paths:Saves"] = "" });

        errors.Should().BeEmpty();
        options.Paths.Saves.Should().Contain(".lifesim");
        options.Paths.Saves.Should().Contain("saves");
    }

    // ── Per-Agent Overrides ───────────────────────────────────────────────────

    [Fact]
    public void PerAgent_Temperature_IsHonored()
    {
        var (options, errors) = BindFrom(new Dictionary<string, string?>
        {
            ["Llm:Agents:Narrator:Temperature"] = "0.3",
            ["Llm:Agents:Narrator:MaxTokens"] = "2000",
            ["Llm:Agents:Narrator:TimeoutSeconds"] = "60"
        });

        errors.Should().BeEmpty();
        options.Llm.Agents.Should().ContainKey("Narrator");
        options.Llm.Agents["Narrator"].Temperature.Should().BeApproximately(0.3, 0.001);
        options.Llm.Agents["Narrator"].MaxTokens.Should().Be(2000);
        options.Llm.Agents["Narrator"].TimeoutSeconds.Should().Be(60);
    }

    // ── Validation Errors ─────────────────────────────────────────────────────

    [Fact]
    public void InvalidEndpointUrl_ProducesValidationError()
    {
        var (_, errors) = BindFrom(new Dictionary<string, string?> { ["Llm:Endpoint"] = "not-a-url" });

        errors.Should().NotBeEmpty();
        errors.Should().Contain(e => e.MemberNames.Any(m => m.Contains("Endpoint")));
    }

    [Fact]
    public void InvalidTheme_ProducesValidationError()
    {
        var (_, errors) = BindFrom(new Dictionary<string, string?> { ["Ui:Theme"] = "neon-purple" });

        errors.Should().NotBeEmpty();
        errors.Should().Contain(e => e.MemberNames.Any(m => m.Contains("Theme")));
    }

    [Fact]
    public void InvalidVerbosity_ProducesValidationError()
    {
        var (_, errors) = BindFrom(new Dictionary<string, string?> { ["Ui:Verbosity"] = "super-loud" });

        errors.Should().NotBeEmpty();
        errors.Should().Contain(e => e.MemberNames.Any(m => m.Contains("Verbosity")));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(2.1)]
    public void AgentTemperatureOutOfRange_ProducesValidationError(double badTemp)
    {
        var (_, errors) = BindFrom(new Dictionary<string, string?>
        {
            ["Llm:Agents:Narrator:Temperature"] = badTemp.ToString("F1")
        });

        // Out-of-range agent Temperature is caught by Range validation on AgentModelOptions
        errors.Should().NotBeEmpty();
    }

    // ── Env Var Override Precedence ───────────────────────────────────────────

    [Fact]
    public void EnvVarOverride_WinsOver_JsonFile()
    {
        // Simulate the env var override effect using AddInMemoryCollection layering
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Llm:Model"] = "model-from-file" })
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Llm:Model"] = "model-from-env" }) // highest priority
            .Build();

        var (options, errors) = LifeSimConfigurationBuilder.BindAndValidate(config);

        errors.Should().BeEmpty();
        options.Llm.Model.Should().Be("model-from-env");
    }
}
