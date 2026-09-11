---
name: solution-architecture-guardrails
description: |
  Use when scaffolding or reviewing the LifeSim Engine .NET 10 solution: csproj files,
  project references, Directory.Build.props, Directory.Packages.props, layer boundaries,
  or the Definition of Done. Enforces the strict one-way dependency direction, central
  package management, and architecture tests for LifeSim.Core purity.
---

# Solution Architecture & Guardrails in LifeSim Engine

This skill encodes the non-negotiable structural rules for the LifeSim Engine solution
([M0-01](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L57),
[M0-02](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L74)).
Apply it to every new project, reference, package, or public API.

---

## 1. The Five Projects & the One-Way Rule

```
LifeSim.Console      Spectre screens · HUD · menus · free-text input
LifeSim.AI           client · orchestrator · 5 agents · templates · cache
LifeSim.World        markdown loaders · validator · registry · .lifeworld
LifeSim.Persistence  versioned JSON saves · config · logs
LifeSim.Core         clock · stats · actions · turn loop · journal · AIGate
```

Dependency direction is **strictly one-way**:
`Console → { AI, World, Persistence } → Core`. `Core` references nothing above it.

1. **`LifeSim.Core` is pure.** No `Spectre.Console`, no `Microsoft.Extensions.AI`, no
   `Markdig`/`YamlDotNet`, no file/network I/O. Domain types + deterministic rules only.
2. **AI proposes, engine disposes ([ADR-006](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L1352)).**
   Validation gates (`AIGate`) live in `Core`; the provider SDK stays in `LifeSim.AI`.
3. **One project = one reason to change.** New code goes in the layer that owns the concern,
   never "temporarily" in `Core`.
4. **No new engine API without a markdown-level test** proving content can drive it
   (see the Definition of Done below).

---

## 2. Central Build Configuration

### `Directory.Build.props` (repo root)

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <WarningsNotAsErrors>CS1591</WarningsNotAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
</Project>
```

### `Directory.Packages.props` (Central Package Management)

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>

  <ItemGroup>
    <!-- Pin exact versions in M0-02; never float. Verify latest before pinning. -->
    <PackageVersion Include="Spectre.Console" />
    <PackageVersion Include="Markdig" />
    <PackageVersion Include="YamlDotNet" />
    <PackageVersion Include="Microsoft.Extensions.AI" />
    <PackageVersion Include="Microsoft.Extensions.AI.OpenAI" />
    <PackageVersion Include="Polly" />
    <PackageVersion Include="JsonSchema.Net" />
    <PackageVersion Include="xunit.v3" />
    <PackageVersion Include="FluentAssertions" />
  </ItemGroup>
</Project>
```

Projects then use `<PackageReference Include="..." />` with **no** `Version` attribute.

### Reference direction in a `.csproj`

```xml
<!-- LifeSim.Console.csproj -->
<ItemGroup>
  <ProjectReference Include="..\LifeSim.AI\LifeSim.AI.csproj" />
  <ProjectReference Include="..\LifeSim.World\LifeSim.World.csproj" />
  <ProjectReference Include="..\LifeSim.Persistence\LifeSim.Persistence.csproj" />
</ItemGroup>

<!-- LifeSim.Core.csproj must NOT contain ProjectReference or UI/AI packages -->
```

---

## 3. Enforce the Rules with Architecture Tests

Structural rules rot silently. Pin them with tests that fail the build when violated.

```csharp
public class ArchitectureTests
{
    private static readonly string[] ForbiddenInCore =
    [
        "Spectre.Console", "Spectre.Console.Cli", "Microsoft.Extensions.AI",
        "Markdig", "YamlDotNet", "Microsoft.Extensions.AI.OpenAI"
    ];

    [Fact]
    public void Core_MustNotReference_PresentationOrInfrastructure()
    {
        var referenced = typeof(WorldState).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ToArray();

        referenced.Should().NotIntersectWith(ForbiddenInCore);
    }

    [Fact]
    public void Core_MustNotReference_UpperLayers()
    {
        var referenced = typeof(WorldState).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ToArray();

        referenced.Should().NotContain(new[]
        {
            "LifeSim.Console", "LifeSim.AI", "LifeSim.World", "LifeSim.Persistence"
        });
    }
}
```

```csharp
// Tests live in LifeSim.Core.Tests; make types visible but keep the API closed.
[assembly: InternalsVisibleTo("LifeSim.Core.Tests")]
```

---

## 4. Definition of Done (every story, every milestone)

A story is done only when **all** of these hold
([plan DoD](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L1370)):

- [ ] All suites green — **including the offline path** (fake `IChatClient`, AI disabled).
- [ ] Fallback proven: kill the stage, the turn still completes.
- [ ] Validator clean on both shipped worlds.
- [ ] Journal + `llm-calls.jsonl` written for the change's happy path.
- [ ] Docs updated: format reference, AI guidelines, or README as applicable.
- [ ] No new engine API without a markdown-level test proving content can drive it.
- [ ] `dotnet build` **zero warnings**; `dotnet test` green.

---

## 5. Task Vocabulary

Stories carry a prefix and an effort score
([plan header](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L9)):

| Prefix | Meaning                         |
| ------ | ------------------------------- |
| `[CS]` | Console / engine C# work        |
| `[AI]` | Agent / prompt / model work     |

Effort: `S < M < L < XL`. Priority: MoSCoW (`Must` > `Should` > `Could`).
Always confirm the story's `_Depends on:_` line before starting.

---

## 6. Related Skills

- `.editorconfig` + formatting: keep `EnforceCodeStyleInBuild` green.
- Save/Load layers: `save-game-persistence`.
- Agent layer boundaries: `ai-agent-orchestration`.
- Running a story end-to-end: `adr-plan-workflow`.
- Test conventions: `dotnet-testing-standards`.
