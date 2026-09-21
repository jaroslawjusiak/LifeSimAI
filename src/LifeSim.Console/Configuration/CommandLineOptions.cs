namespace LifeSim.Console.Configuration;

/// <summary>Process-level switches and command verb parsed from command-line arguments.</summary>
public sealed record CommandLineOptions(bool Verbose, string? Command)
{
    public static CommandLineOptions Parse(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var verbose = false;
        string? command = null;

        foreach (var arg in args)
        {
            if (string.Equals(arg, "--verbose", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-v", StringComparison.OrdinalIgnoreCase))
            {
                verbose = true;
            }
            else if (!arg.StartsWith('-') && command is null)
            {
                command = arg;
            }
        }

        return new CommandLineOptions(verbose, command);
    }
}
