using System.ComponentModel.DataAnnotations;

namespace LifeSim.Console.Configuration;

/// <summary>
/// Controls the terminal UI appearance and log verbosity.
/// Bound from Ui:* configuration keys.
/// </summary>
public sealed class UiOptions
{
    public const string SectionName = "Ui";

    /// <summary>Color theme name. Supported values: default, colorblind.</summary>
    [RegularExpression("^(default|colorblind)$",
        ErrorMessage = "Ui:Theme must be 'default' or 'colorblind'.")]
    public string Theme { get; init; } = "default";

    /// <summary>
    /// Console verbosity level. Controls how much detail is written to the terminal.
    /// Supported values: quiet, normal, verbose.
    /// </summary>
    [RegularExpression("^(quiet|normal|verbose)$",
        ErrorMessage = "Ui:Verbosity must be 'quiet', 'normal', or 'verbose'.")]
    public string Verbosity { get; init; } = "normal";
}
