namespace LifeSim.Console.Configuration;

/// <summary>Process-level switches parsed from command-line arguments.</summary>
public sealed record CommandLineOptions(bool Verbose)
{
    public static CommandLineOptions Parse(IEnumerable<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var verbose = false;
        foreach (var arg in args)
        {
            if (string.Equals(arg, "--verbose", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-v", StringComparison.OrdinalIgnoreCase))
            {
                verbose = true;
            }
        }

        return new CommandLineOptions(verbose);
    }
}
