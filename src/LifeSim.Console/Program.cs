using LifeSim.AI.Diagnostics;
using LifeSim.Console.Configuration;
using LifeSim.Console.Logging;
using Serilog;
using Spectre.Console;

// ── Configuration ──────────────────────────────────────────────────────────
var commandLine = CommandLineOptions.Parse(args);
var configuration = LifeSimConfigurationBuilder.Build();
var (options, errors) = LifeSimConfigurationBuilder.BindAndValidate(configuration);

if (ConfigurationValidationReporter.ReportAndCheck(errors))
{
    return 1; // Exit with error code on invalid config
}

// ── Logging ────────────────────────────────────────────────────────────────
using var logger = LoggingSetup.Create(options.Logging, commandLine.Verbose);
Log.Logger = logger;
logger.Information(
    "LifeSim starting. AiEnabled={AiEnabled} Model={Model} Theme={Theme} Verbose={Verbose}",
    options.Ai.Enabled,
    options.Llm.Model,
    options.Ui.Theme,
    commandLine.Verbose);

// ── LLM call journal (wrapped around the live IChatClient in M4-01) ────────
using var llmCallRecorder = new JsonlLlmCallRecorder(
    options.Logging.Directory,
    options.Logging.FileSizeLimitBytes,
    options.Logging.RetainedFileCount,
    options.Logging.MaxDirectoryBytes,
    options.Logging.RedactSensitiveContent);
logger.Information("LLM call journal: {JournalPath}", llmCallRecorder.ActiveFilePath);

// ── Startup ─────────────────────────────────────────────────────────────────
AnsiConsole.Write(new FigletText("LifeSim").Color(Color.Cyan1));
AnsiConsole.MarkupLine($"AI: [bold]{(options.Ai.Enabled ? "[green]Enabled[/]" : "[yellow]Offline[/]")}[/] · Model: [cyan]{Markup.Escape(options.Llm.Model)}[/] · Theme: [cyan]{Markup.Escape(options.Ui.Theme)}[/]");

logger.Information("LifeSim startup complete.");
return 0;
