using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace LifeSim.Console.Configuration;

/// <summary>
/// Builds the layered IConfiguration and binds all typed options.
/// Precedence (lowest → highest):
///   1. Embedded defaults (appsettings.json)
///   2. User config file (~/.lifesim/config.json)
///   3. Environment variables (prefix LIFESIM_)
/// </summary>
public static class LifeSimConfigurationBuilder
{
    private static readonly string UserConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".lifesim", "config.json");

    /// <summary>
    /// Builds the configuration root from all sources in order.
    /// </summary>
    public static IConfiguration Build(string? appSettingsBasePath = null)
    {
        return new ConfigurationBuilder()
            .SetBasePath(appSettingsBasePath ?? AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile(UserConfigPath, optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "LIFESIM_")
            .Build();
    }

    /// <summary>
    /// Binds and validates all options from the configuration root.
    /// Returns null when all options are valid.
    /// Returns a list of <see cref="ValidationResult"/> when any binding fails.
    /// </summary>
    public static (LifeSimOptions Options, IReadOnlyList<ValidationResult> Errors) BindAndValidate(
        IConfiguration configuration)
    {
        var llm = new LlmOptions();
        var ai = new AiOptions();
        var paths = new PathsOptions();
        var ui = new UiOptions();
        var logging = new LoggingOptions();

        configuration.GetSection(LlmOptions.SectionName).Bind(llm);
        configuration.GetSection(AiOptions.SectionName).Bind(ai);
        configuration.GetSection(PathsOptions.SectionName).Bind(paths);
        configuration.GetSection(UiOptions.SectionName).Bind(ui);
        configuration.GetSection(LoggingOptions.SectionName).Bind(logging);

        // Resolve empty paths to their user defaults
        if (string.IsNullOrWhiteSpace(paths.Saves))
        {
            paths = paths with
            {
                Saves = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".lifesim", "saves")
            };
        }

        if (string.IsNullOrWhiteSpace(logging.Directory))
        {
            logging = new LoggingOptions
            {
                Directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".lifesim", "logs"),
                FileSizeLimitBytes = logging.FileSizeLimitBytes,
                RetainedFileCount = logging.RetainedFileCount,
                MaxDirectoryBytes = logging.MaxDirectoryBytes,
                RedactSensitiveContent = logging.RedactSensitiveContent,
            };
        }

        var options = new LifeSimOptions(llm, ai, paths, ui, logging);

        var errors = new List<ValidationResult>();

        // Validate each section independently for granular error messages
        ValidateSection(llm, $"{LlmOptions.SectionName}", errors);
        ValidateSection(ai, $"{AiOptions.SectionName}", errors);
        ValidateSection(ui, $"{UiOptions.SectionName}", errors);
        ValidateSection(logging, $"{LoggingOptions.SectionName}", errors);

        // Per-agent overrides live in a dictionary and are not reached by
        // property-level validation, so validate each entry explicitly.
        foreach (var (agentName, agent) in llm.Agents)
        {
            ValidateSection(agent, $"{LlmOptions.SectionName}:Agents:{agentName}", errors);
        }

        return (options, errors);
    }

    private static void ValidateSection<T>(T instance, string sectionPrefix, List<ValidationResult> errors)
        where T : class
    {
        var ctx = new ValidationContext(instance);
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(instance, ctx, results, validateAllProperties: true))
        {
            foreach (var r in results)
            {
                // Prefix member names with the section name for clarity
                var members = r.MemberNames.Select(m => $"{sectionPrefix}:{m}").ToList();
                errors.Add(new ValidationResult(r.ErrorMessage, members));
            }
        }
    }
}

/// <summary>
/// Aggregates all bound option sections into a single strongly-typed root.
/// </summary>
public sealed record LifeSimOptions(
    LlmOptions Llm,
    AiOptions Ai,
    PathsOptions Paths,
    UiOptions Ui,
    LoggingOptions Logging);
