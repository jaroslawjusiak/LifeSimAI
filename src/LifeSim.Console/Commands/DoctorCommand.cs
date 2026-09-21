using LifeSim.Console.Configuration;
using Spectre.Console;

namespace LifeSim.Console.Commands;

/// <summary>
/// Implements the <c>doctor</c> command: probe the configured local LLM endpoint for
/// reachability and model presence, then render the result as a Spectre panel.
/// Returns exit code 0 only when the endpoint is reachable and the model is present,
/// so the command can gate scripts.
/// </summary>
public static class DoctorCommand
{
    public static async Task<int> RunAsync(
        LlmOptions llm,
        HttpMessageHandler handler,
        IAnsiConsole? console = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(llm);
        ArgumentNullException.ThrowIfNull(handler);

        var sink = console ?? AnsiConsole.Console;

        sink.MarkupLine("[grey]Probing local LLM endpoint…[/]");

        var doctor = new LlmDoctor(handler);
        var result = await doctor.CheckAsync(llm.Endpoint, llm.Model, llm.ApiKey, cancellationToken).ConfigureAwait(false);

        Render(sink, result);

        return result.EndpointReachable && result.ModelPresent ? 0 : 1;
    }

    private static void Render(IAnsiConsole sink, LlmDoctorResult result)
    {
        var healthy = result.EndpointReachable && result.ModelPresent;

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(healthy ? Color.Green : Color.Red)
            .HideHeaders()
            .AddColumn(new TableColumn("Key").Width(16))
            .AddColumn(new TableColumn("Value"));

        table.AddRow(new Markup("[grey]Endpoint[/]"), new Markup(Markup.Escape(result.Endpoint)));
        table.AddRow(new Markup("[grey]Probed[/]"), new Markup(Markup.Escape(result.ModelsUri)));
        table.AddRow(new Markup("[grey]Model[/]"), new Markup(Markup.Escape(result.Model)));
        table.AddRow(
            new Markup("[grey]Reachable[/]"),
            result.EndpointReachable
                ? new Markup($"[green]yes[/] ({result.Latency.TotalMilliseconds:0} ms)")
                : new Markup("[red]no[/]"));
        table.AddRow(
            new Markup("[grey]Model present[/]"),
            result.ModelPresent
                ? new Markup("[green]yes[/]")
                : new Markup("[red]no[/]"));

        if (result.StatusCode is not null)
        {
            table.AddRow(
                new Markup("[grey]HTTP status[/]"),
                new Markup(Markup.Escape(result.StatusCode.Value.ToString())));
        }

        var panel = new Panel(table)
        {
            Header = new PanelHeader(healthy ? "[bold green] Local LLM: healthy [/]" : "[bold red] Local LLM: problem [/]"),
            Border = BoxBorder.Heavy,
            Padding = new Padding(1, 0),
        };

        sink.Write(panel);
        sink.MarkupLine(string.Empty);

        if (healthy)
        {
            return;
        }

        if (result.Error is not null)
        {
            sink.MarkupLine($"[red]{Markup.Escape(result.Error)}[/]");
        }

        if (!result.EndpointReachable)
        {
            sink.MarkupLine("[grey]Is Jan (or your local server) running on the configured port? See [cyan]docs/local-llm-setup.md[/].[/]");
        }
        else if (result.AvailableModels.Count > 0)
        {
            sink.MarkupLine("[grey]Models reported by the endpoint:[/]");
            foreach (var model in result.AvailableModels)
            {
                sink.MarkupLine($"  [cyan]{Markup.Escape(model)}[/]");
            }
        }
        else
        {
            sink.MarkupLine("[grey]The endpoint returned no model listing. Verify the base URL ends with [cyan]/v1[/].[/]");
        }
    }
}
