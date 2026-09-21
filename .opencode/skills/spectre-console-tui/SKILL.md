---
name: spectre-console-tui
description: |
  Guides the design, implementation, and testing of rich terminal user interfaces
  built with Spectre.Console in .NET 10 for LifeSim Engine (LifeSim.Console).
  Covers pure-function rendering, HUD status bars, grouped action selection prompts,
  markup injection escaping, and TestConsole snapshot testing at multiple column widths.
---

# Spectre.Console TUI Engineering for LifeSim Engine

This skill guides the development of the presentation layer ([`LifeSim.Console`](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L24)) using **Spectre.Console** in .NET 10. It enforces pure-function rendering, terminal safety, and headless snapshot testability.

---

## 1. Architectural Principles

1. **Decoupled Renderers (Pure Functions):** UI components must be pure functions that take domain state ([`WorldState`](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L212), view models) and return renderables ([`IRenderable`](file:///C:/1/Repos/LifeSimAI)). Never mix game mutation logic into console rendering code.
2. **Mandatory Markup Escaping (The Choke Point):** Local LLMs and markdown prose frequently emit brackets like `[hello]` or `[/tag]`. Rendering unescaped text directly through `AnsiConsole.Markup` causes terminal crashes or corrupted output ([R-10](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L1302)). **All external text MUST pass through `Markup.Escape()` before rendering.**
3. **Snapshot Testability via `TestConsole`:** Screens and HUD layouts must be verified headless across standard terminal widths (80, 100, 120 columns) using Spectre's `TestConsole`.

---

## 2. Core UI Component Patterns

### A. Persistent HUD Status Panel ([M3-02](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L449))

Renders clock, player stats, and current location using `Table` or `Grid` without borders for precise column alignment.

```csharp
public static class HudRenderer
{
    public static IRenderable Render(PlayerState player, GameClock clock, string locationName)
    {
        var grid = new Grid()
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().PadLeft(2))
            .AddColumn(new GridColumn().PadLeft(2));

        // Time and Location Header
        string timeMarkup = $"[bold white]{clock.DayOfWeek}[/] · Day {clock.DayIndex} · {clock.Hour:D2}:{clock.Minute:D2} · [yellow]{clock.Phase}[/]";
        string locMarkup = $"Location: [bold cyan]{Markup.Escape(locationName)}[/]";
        string moneyMarkup = $"Money: [bold green]${player.Money:N0}[/]";

        grid.AddRow(new Markup(timeMarkup), new Markup(locMarkup), new Markup(moneyMarkup));

        // Stat Bars with Threshold Coloring
        var statTable = new Table().Border(TableBorder.None).HideHeaders();
        statTable.AddColumns("Stat", "Bar", "Value");

        foreach (var (statName, val, min, max, criticalAt) in player.Stats)
        {
            string color = val switch
            {
                _ when val <= criticalAt => "red",
                _ when val <= criticalAt * 2 => "yellow",
                _ => "green"
            };

            var bar = new ProgressBar
            {
                Value = (double)(val - min) / (max - min) * 100,
                Width = 15,
                ShowRemaining = false
            };

            statTable.AddRow(
                new Markup($"[{color}]{Markup.Escape(statName)}[/]"),
                bar,
                new Markup($"[{color}]{val}/{max}[/]")
            );
        }

        return new Panel(new Rows(grid, new Rule().RuleStyle("grey"), statTable))
            .Header("[bold]Status[/]")
            .Border(BoxBorder.Rounded);
    }
}
```

---

### B. Safe Text Surface & Markup Escaping Choke Point ([M3-03](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L467), [M3-08](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L557))

Implement a unified text surface interface that guarantees all model and markdown outputs are safely escaped:

```csharp
public interface ITextSurface
{
    void WriteParagraph(string text);
    void WriteDialog(string npcName, string line, Color npcAccent);
    void WriteOutcome(string actionVerb, int deltaMoney, int deltaEnergy, int minutesElapsed);
}

public sealed class SpectreTextSurface(IAnsiConsole console) : ITextSurface
{
    public void WriteParagraph(string text)
    {
        // Safe measure wrapping with mandatory escape
        string safeText = Markup.Escape(text);
        console.Write(new Panel(new Markup(safeText))
            .Border(BoxBorder.None)
            .Padding(1, 0, 1, 0));
    }

    public void WriteDialog(string npcName, string line, Color npcAccent)
    {
        string safeName = Markup.Escape(npcName);
        string safeLine = Markup.Escape(line);
        console.MarkupLine($"  [{npcAccent.ToMarkup()} bold]{safeName}:[/] \"{safeLine}\"");
    }

    public void WriteOutcome(string actionVerb, int deltaMoney, int deltaEnergy, int minutesElapsed)
    {
        string moneyFmt = deltaMoney >= 0 ? $"+${deltaMoney}" : $"-${Math.Abs(deltaMoney)}";
        string energyFmt = deltaEnergy >= 0 ? $"+{deltaEnergy}⚡" : $"{deltaEnergy}⚡";

        console.MarkupLine($"[grey]>[/] [bold]{Markup.Escape(actionVerb)}[/] [yellow]({minutesElapsed}m)[/] · [green]{moneyFmt}[/] · [cyan]{energyFmt}[/]");
    }
}
```

---

### C. Grouped Action Menu with Disabled Reasons ([M3-04](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L485))

Use `SelectionPrompt<T>` with custom display mapping showing costs and disabled states clearly:

```csharp
public record ActionMenuItem(
    string Id,
    string Label,
    string Category,
    string CostSummary,
    bool IsEnabled,
    string? DisabledReason
);

public static class ActionMenuPrompt
{
    public static SelectionPrompt<ActionMenuItem> Create(IEnumerable<ActionMenuItem> items)
    {
        var prompt = new SelectionPrompt<ActionMenuItem>()
            .Title("[bold]What do you want to do?[/]")
            .PageSize(10)
            .UseConverter(item =>
            {
                if (!item.IsEnabled)
                {
                    return $"[grey]✕ {Markup.Escape(item.Label)} ({Markup.Escape(item.DisabledReason ?? "Unavailable")})[/]";
                }
                return $"{Markup.Escape(item.Label)} [grey dim]· {Markup.Escape(item.CostSummary)}[/]";
            });

        foreach (var item in items)
        {
            prompt.AddChoice(item);
        }

        return prompt;
    }
}
```

---

## 3. Headless Snapshot Testing with `TestConsole`

Verify layouts, column wrapping, and character escaping using Spectre's in-memory `TestConsole`:

```csharp
public class HudRendererTests
{
    [Theory]
    [InlineData(80)]
    [InlineData(100)]
    [InlineData(120)]
    public void Hud_RendersCleanly_AcrossTerminalWidths(int width)
    {
        // Arrange
        var testConsole = new TestConsole().Width(width);
        var clock = new GameClock(day: 1, hour: 14, minute: 30);
        var player = new PlayerState("Alex", money: 500);

        // Act
        var renderable = HudRenderer.Render(player, clock, "City Square");
        testConsole.Write(renderable);

        // Assert
        string output = testConsole.Output;
        output.Should().Contain("City Square");
        output.Should().Contain("$500");
    }

    [Fact]
    public void Narrative_HostileBrackets_DoNotCrashRenderer()
    {
        // Arrange
        var testConsole = new TestConsole();
        var surface = new SpectreTextSurface(testConsole);
        string hostileText = "Hello [b]world[/b] with [unclosed tags and [/invalid] markup [[]]";

        // Act
        var act = () => surface.WriteParagraph(hostileText);

        // Assert
        act.Should().NotThrow();
        testConsole.Output.Should().Contain("[b]world[/b]");
    }
}
```
