using System.ComponentModel.DataAnnotations;
using Spectre.Console;
using DataAnnotationsValidationResult = System.ComponentModel.DataAnnotations.ValidationResult;

namespace LifeSim.Console.Configuration;

/// <summary>
/// Renders actionable configuration validation errors as a Spectre.Console error panel.
/// </summary>
public static class ConfigurationValidationReporter
{
    /// <summary>
    /// Renders all validation errors to the console.
    /// Returns true when there are errors (caller should exit), false when clean.
    /// </summary>
    public static bool ReportAndCheck(IReadOnlyList<DataAnnotationsValidationResult> errors, IAnsiConsole? console = null)
    {
        var sink = console ?? AnsiConsole.Console;

        if (errors.Count == 0)
        {
            return false;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Red)
            .AddColumn(new TableColumn("[bold red]Config Key[/]").Width(35))
            .AddColumn(new TableColumn("[bold red]Problem[/]"));

        foreach (var error in errors)
        {
            string keys = error.MemberNames.Any()
                ? string.Join(", ", error.MemberNames)
                : "(unknown)";

            table.AddRow(
                new Markup($"[yellow]{Markup.Escape(keys)}[/]"),
                new Markup(Markup.Escape(error.ErrorMessage ?? "Validation failed."))
            );
        }

        var panel = new Panel(table)
        {
            Header = new PanelHeader("[bold red] Configuration Error [/]"),
            Border = BoxBorder.Heavy,
            Padding = new Padding(1, 0)
        };

        sink.Write(panel);
        sink.MarkupLine(string.Empty);
        sink.MarkupLine("[grey]Tip: edit [cyan]~/.lifesim/config.json[/] or set [cyan]LIFESIM_*[/] environment variables to fix the values above.[/]");
        sink.MarkupLine("[grey]Run with [cyan]--help[/] to see all supported configuration keys.[/]");

        return true;
    }
}
