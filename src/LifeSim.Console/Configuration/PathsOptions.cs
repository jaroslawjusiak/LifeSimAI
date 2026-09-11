namespace LifeSim.Console.Configuration;

/// <summary>
/// File-system paths for worlds library and save slots.
/// Bound from Paths:* configuration keys.
/// </summary>
public sealed record PathsOptions
{
    public const string SectionName = "Paths";

    /// <summary>
    /// Portable worlds directory (relative to the executable or absolute).
    /// Defaults to a "worlds" subdirectory next to the binary.
    /// </summary>
    public string Worlds { get; init; } = "worlds";

    /// <summary>
    /// User save-slot directory. Defaults to ~/.lifesim/saves.
    /// </summary>
    public string Saves { get; init; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".lifesim", "saves");
}
