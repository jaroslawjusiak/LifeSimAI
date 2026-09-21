using LifeSim.AI.Probe;
using LifeSim.Console.Configuration;
using Spectre.Console;

namespace LifeSim.Console.Commands;

/// <summary>
/// Implements the <c>probe</c> command: run the four authored probe cases against the
/// configured local model, render a Spectre results table, archive the acceptance record to
/// disk, and return a non-zero exit code when any JSON-contract case fails.
/// </summary>
public static class ProbeCommand
{
    /// <summary>Default archive location for the model acceptance record.</summary>
    public const string DefaultArchivePath = "docs/model-acceptance.md";

    public static async Task<int> RunAsync(
        LlmOptions llm,
        HttpMessageHandler handler,
        IAnsiConsole? console = null,
        string? archivePath = DefaultArchivePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(llm);
        ArgumentNullException.ThrowIfNull(handler);

        var sink = console ?? AnsiConsole.Console;
        sink.MarkupLine("[grey]Running structured-output probe against the local model…[/]");

        var runner = new ProbeRunner(new ProbeSender(handler, llm.Endpoint, llm.Model, llm.ApiKey));
        var results = await runner.RunAsync(cancellationToken).ConfigureAwait(false);

        Render(sink, results);

        if (archivePath is not null)
        {
            await ArchiveAsync(results, llm, archivePath, sink, cancellationToken).ConfigureAwait(false);
        }

        return ProbeReport.OverallPass(results) ? 0 : 1;
    }

    private static async Task ArchiveAsync(
        IReadOnlyList<ProbeResult> results,
        LlmOptions llm,
        string archivePath,
        IAnsiConsole sink,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(archivePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(
            fullPath,
            ProbeReport.Format(llm.Model, llm.Endpoint, results),
            cancellationToken).ConfigureAwait(false);

        sink.MarkupLine($"[grey]Results archived to [cyan]{Markup.Escape(fullPath)}[/].[/]");
    }

    private static void Render(IAnsiConsole sink, IReadOnlyList<ProbeResult> results)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn("Case"))
            .AddColumn(new TableColumn("Contract"))
            .AddColumn(new TableColumn("Result"))
            .AddColumn(new TableColumn("Latency"))
            .AddColumn(new TableColumn("Note"));

        foreach (var result in results)
        {
            table.AddRow(
                new Markup(Markup.Escape(result.Name)),
                new Markup($"[grey]{Markup.Escape(result.Kind.ToString())}[/]"),
                result.Success ? new Markup("[green]pass[/]") : new Markup("[red]FAIL[/]"),
                new Markup(Markup.Escape($"{result.LatencyMs} ms")),
                new Markup(Markup.Escape(result.Error ?? result.Detail ?? string.Empty)));
        }

        var pass = ProbeReport.OverallPass(results);
        var panel = new Panel(table)
        {
            Header = new PanelHeader(pass ? "[bold green] Probe: PASS [/]" : "[bold red] Probe: FAIL [/]"),
            Border = BoxBorder.Heavy,
            Padding = new Padding(1, 0),
        };

        sink.Write(panel);
        sink.MarkupLine(string.Empty);

        if (!pass)
        {
            sink.MarkupLine("[yellow]One or more JSON contract cases failed. This model may be too weak for structured output.[/]");
        }
    }
}
