---
name: markdown-ast-validator
description: |
  Guides parsing, referential integrity validation, and packaging of game worlds
  authored in Markdown + YAML frontmatter for LifeSim Engine (LifeSim.World).
  Covers Markdig AST parsing, YamlDotNet strict deserialization, non-throwing
  diagnostic accumulation, RefWhitelist generation, and deterministic .lifeworld packaging.
---

# Markdown World Loader, Validator & Packaging in LifeSim Engine

This skill guides the construction of the content layer ([`LifeSim.World`](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L26)) using **Markdig** and **YamlDotNet** in .NET 10. It enforces strict referential validation and deterministic packaging for `.lifeworld` files.

---

## 1. Architectural Principles

1. **Non-Throwing Diagnostic Accumulator:** Parsing and validation must never crash on the first error. Accumulate all errors, warnings, and infos into a `DiagnosticBag` so authors receive a comprehensive integrity report.
2. **Strict Schema, Tolerant Prose:** Frontmatter YAML keys must be strictly validated (unknown keys produce warnings; missing required keys produce errors). Markdown body sections (e.g. `## Description`, `## Personality`) are extracted as structured prose to feed LLM context builders.
3. **RefWhitelist Generation:** The validator produces an exhaustive `RefWhitelist` containing every valid entity ID, location ID, item ID, and flag. This whitelist is used directly by [`AIGate`](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L831) to prevent hallucinated entity references ([R-02](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L1254)).
4. **Deterministic Zip Packaging:** `.lifeworld` archives must be byte-deterministic across builds by normalizing zip entry timestamps (e.g., set to fixed epoch) and sorting filenames lexicographically.

---

## 2. Parser & Diagnostic Model ([M2-01](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L310), [M2-04](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L365))

### A. Diagnostic Bag

```csharp
public enum DiagnosticSeverity { Info, Warning, Error }

public record Diagnostic(
    DiagnosticSeverity Severity,
    string FilePath,
    int? LineNumber,
    string Message
);

public sealed class DiagnosticBag : IEnumerable<Diagnostic>
{
    private readonly List<Diagnostic> _diagnostics = [];

    public bool HasErrors => _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    public void Add(DiagnosticSeverity severity, string filePath, int? line, string message) =>
        _diagnostics.Add(new Diagnostic(severity, filePath, line, message));

    public void AddError(string filePath, int? line, string message) => Add(DiagnosticSeverity.Error, filePath, line, message);
    public void AddWarning(string filePath, int? line, string message) => Add(DiagnosticSeverity.Warning, filePath, line, message);

    public IEnumerator<Diagnostic> GetEnumerator() => _diagnostics.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
```

---

### B. Markdig + YamlDotNet Frontmatter Parser

```csharp
public sealed class MarkdownEntityParser
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .Build();

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties() // Or strict mapping with custom warning hook
        .Build();

    public static ParsedMarkdownDocument<TFrontmatter> Parse<TFrontmatter>(string filePath, string markdownContent, DiagnosticBag diagnostics)
        where TFrontmatter : class
    {
        var document = Markdown.Parse(markdownContent, Pipeline);
        var yamlBlock = document.Descendants<YamlFrontMatterBlock>().FirstOrDefault();

        if (yamlBlock == null)
        {
            diagnostics.AddError(filePath, 1, "Missing required YAML frontmatter block (---)");
            return new ParsedMarkdownDocument<TFrontmatter>(null, string.Empty, new Dictionary<string, string>());
        }

        string yamlText = markdownContent.Substring(yamlBlock.Span.Start, yamlBlock.Span.Length)
            .Trim('-', '\r', '\n');

        TFrontmatter? frontmatter = null;
        try
        {
            frontmatter = YamlDeserializer.Deserialize<TFrontmatter>(yamlText);
        }
        catch (YamlException ex)
        {
            diagnostics.AddError(filePath, ex.Start.Line, $"YAML parsing failed: {ex.Message}");
        }

        // Extract body prose separated by H2 headings
        string body = markdownContent[yamlBlock.Span.End..].Trim();
        var sections = ExtractH2Sections(body);

        return new ParsedMarkdownDocument<TFrontmatter>(frontmatter, body, sections);
    }

    private static Dictionary<string, string> ExtractH2Sections(string markdown)
    {
        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = markdown.Split('\n');
        string currentHeading = "Overview";
        var currentLines = new List<string>();

        foreach (var rawLine in lines)
        {
            string line = rawLine.TrimEnd('\r');
            if (line.StartsWith("## "))
            {
                if (currentLines.Count > 0)
                {
                    sections[currentHeading] = string.Join("\n", currentLines).Trim();
                    currentLines.Clear();
                }
                currentHeading = line[3..].Trim();
            }
            else
            {
                currentLines.Add(line);
            }
        }

        if (currentLines.Count > 0)
            sections[currentHeading] = string.Join("\n", currentLines).Trim();

        return sections;
    }
}
```

---

## 3. Referential Integrity Checking ([M2-04](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L365))

Verify all references between locations, NPCs, items, actions, and schedules:

```csharp
public sealed class WorldIntegrityValidator
{
    public static RefWhitelist Validate(WorldPackage package, DiagnosticBag diagnostics)
    {
        var knownLocations = package.Locations.Select(l => l.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownNpcs = package.Characters.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownItems = package.Items.Select(i => i.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownActions = package.Actions.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownFlags = package.Flags.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 1. Validate Location Connections
        foreach (var loc in package.Locations)
        {
            foreach (var targetId in loc.Connections)
            {
                if (!knownLocations.Contains(targetId))
                {
                    diagnostics.AddError(loc.SourceFile, null, $"Location '{loc.Id}' connects to unknown location '{targetId}'");
                }
            }
        }

        // 2. Validate NPC Schedules & Home Locations
        foreach (var npc in package.Characters)
        {
            if (!knownLocations.Contains(npc.HomeLocation))
            {
                diagnostics.AddError(npc.SourceFile, null, $"NPC '{npc.Id}' has unknown homeLocation '{npc.HomeLocation}'");
            }

            foreach (var slot in npc.WeeklySchedule)
            {
                if (!knownLocations.Contains(slot.LocationId))
                {
                    diagnostics.AddError(npc.SourceFile, null, $"NPC '{npc.Id}' schedule references unknown location '{slot.LocationId}'");
                }
            }
        }

        // Return the validated RefWhitelist for runtime AIGate checks
        return new RefWhitelist(knownLocations, knownNpcs, knownItems, knownActions, knownFlags);
    }
}
```

---

## 4. Deterministic `.lifeworld` Package Creation ([M7-01](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L942))

```csharp
public static class WorldPacker
{
    private static readonly DateTimeOffset ZipEpoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static async Task PackAsync(string sourceDirectory, string outputZipPath)
    {
        var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        // 1. Generate content.sha256 manifest
        var manifestLines = new StringBuilder();
        foreach (var file in files)
        {
            string relPath = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            if (relPath.Equals("content.sha256", StringComparison.OrdinalIgnoreCase)) continue;

            byte[] hash = await SHA256.HashDataAsync(File.OpenRead(file));
            manifestLines.AppendLine($"{Convert.ToHexString(hash).ToLowerInvariant()}  {relPath}");
        }

        string manifestPath = Path.Combine(sourceDirectory, "content.sha256");
        await File.WriteAllTextAsync(manifestPath, manifestLines.ToString());
        files.Add(manifestPath);

        // 2. Create deterministic Zip
        if (File.Exists(outputZipPath)) File.Delete(outputZipPath);
        using var zip = ZipFile.Open(outputZipPath, ZipArchiveMode.Create);

        foreach (var file in files.OrderBy(f => f, StringComparer.Ordinal))
        {
            string entryName = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            entry.LastWriteTime = ZipEpoch; // Normalize timestamp

            using var entryStream = entry.Open();
            using var fileStream = File.OpenRead(file);
            await fileStream.CopyToAsync(entryStream);
        }
    }
}
```
