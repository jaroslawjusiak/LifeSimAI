---
name: dotnet-testing-standards
description: |
  Use when writing or fixing tests in the LifeSim Engine solution: xUnit conventions,
  FluentAssertions, test project layout, golden-file and snapshot-equality tests,
  deterministic fixtures, fault injection, seeded fuzzing, and the 80% Core coverage gate.
  Applies to LifeSim.Core.Tests, LifeSim.World.Tests, and LifeSim.AI.Tests.
---

# .NET Testing Standards in LifeSim Engine

All suites must run **offline and deterministically** on a clean machine. This skill is
the shared test contract; layer-specific doubles live in the referenced skills.

---

## 1. Stack & Project Layout

- **xUnit** with **FluentAssertions**. One test project per testable layer:
  `LifeSim.Core.Tests`, `LifeSim.World.Tests`, `LifeSim.AI.Tests`.
- Test code lives in the test assembly; the fast C# demo world is a **permanent test
  fixture** ([M1-07](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L283)).
- Use `Microsoft.NET.Test.Sdk`, `xunit.v3`, `xunit.runner.visualstudio`, `FluentAssertions`,
  `coverlet.collector` — versions pinned centrally, never inlined.

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.NET.Test.Sdk" />
  <PackageReference Include="xunit.v3" />
  <PackageReference Include="xunit.runner.visualstudio" />
  <PackageReference Include="FluentAssertions" />
  <PackageReference Include="coverlet.collector" />
</ItemGroup>
```

---

## 2. Naming & Structure

```
<MethodUnderTest>_<Scenario>_<ExpectedOutcome>
```

- Class: `ThingTests`; file: `ThingTests.cs`; one behavior per test.
- Parameterized matrices use `[Theory]` + `[InlineData]` / `[MemberData]`.
- Arrange / Act / Assert, with a blank line between phases.
- No `DateTime.Now`, `Random` without a seed, `Thread.Sleep`, or network access.
  Inject the clock; pass the seed; use the fake transport.

```csharp
public class GameClockTests
{
    [Theory]
    [InlineData(23, 30, 60, 0, 30, 1)] // crosses midnight
    [InlineData(23, 30, 120, 1, 30, 1)]
    public void Advance_AcrossMidnight_RollsDayAndHour(
        int hour, int minute, int advance, int expectedHour, int expectedMinute, int expectedDays)
    {
        var clock = new GameClock(DayIndex: 0, DayOfWeek.Monday, Hour: hour, Minute: minute);

        var (advanced, _, dayStarted) = clock.Advance(advance);

        advanced.Hour.Should().Be(expectedHour);
        advanced.Minute.Should().Be(expectedMinute);
        advanced.DayIndex.Should().Be(expectedDays);
        dayStarted.Should().BeTrue();
    }
}
```

Determinism requires a **canonical serialization** for hashing. Never hash with default
`JsonSerializerOptions` (property order/formatting is not stable enough across refactors).

```csharp
public static class CanonicalJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public static class StateHasher
{
    public static string Compute(WorldState state)
    {
        var json = JsonSerializer.Serialize(state, CanonicalJson.Options);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}
```

---

## 3. Snapshot Equality for Transactions

Every precondition/atomicity claim in [M1-04](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L227)
is proven by comparing a state hash before and after.

```csharp
public class ActionResolverTests
{
    [Fact]
    public void Resolve_FailedPrecondition_MutatesNothing()
    {
        var state = DemoWorldFixture.NewGame(seed: 42);
        var before = StateHasher.Compute(state);
        var tooExpensive = state.Catalog.Get("buy_gym_membership");

        var result = ActionResolver.Resolve(state, tooExpensive);

        result.IsSuccess.Should().BeFalse();
        StateHasher.Compute(state).Should().Be(before);
    }

    [Fact]
    public void Replay_FromJournal_ReproducesStateHash()
    {
        var (state, journal) = DemoWorldFixture.ScriptedSevenDayRun(seed: 7).ToTuple();

        var replayed = JournalReplay.Apply(state.InitialSeedState, journal);

        StateHasher.Compute(replayed).Should().Be(StateHasher.Compute(state));
    }
}
```

---

## 4. Golden-File Tests (World Content)

Loader output is compared against a checked-in expected definition. Keep goldens
human-diffable; regenerate only via an explicit, reviewed command.

```csharp
public class WorldLoaderGoldenTests
{
    [Fact]
    public void Load_ClassicLife_MatchesGoldenDefinition()
    {
        var actual = WorldLoader.Load("worlds/classic-life").Definition;

        actual.Should().BeEquivalentTo(
            GoldenFile.Read<WorldDefinition>("classic-life.world.json"),
            o => o.WithStrictOrdering());
    }
}
```

- Golden files live beside the fixture world, named `<world>.world.json`.
- Prefer `BeEquivalentTo` with strict ordering over string equality so formatting churn
  does not create false failures.
- For validators, the "golden" is the expected diagnostic set — see
  `markdown-ast-validator` for the fault-fixture pattern.

---

## 5. Fault Injection & Chaos

Any seam that can fail must have a test that forces the failure and asserts a **typed**
outcome (never an escaped exception).

- LLM transport failures → fake transport throwing `TimeoutException` / returning garbage
  → assert typed `LlmError` (see `microsoft-extensions-ai-testing`).
- Turn pipeline: kill each stage in isolation → assert the turn completes via fallback
  ([M5-06](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L795)).
- Persistence: kill mid-write / corrupt a save → assert previous save intact
  (see `save-game-persistence`).

```csharp
[Fact]
public async Task Turn_WhenNarratorThrows_TurnStillCompletes()
{
    var orchestrator = TestOrchestrator.WithNarrator(() => throw new LlmError.Timeout());

    var result = await orchestrator.RunTurnAsync(new PlayerInput("nap"));

    result.IsSuccess.Should().BeTrue();
    result.Narration.Should().Be(FallbackNarrator.LastOutcomeText);
}
```

---

## 6. Seeded Fuzzing & Invariants

Every fuzz failure must replay from the recorded seed, so encode the seed in the test name
([M9-01](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L1107)).

```csharp
[Theory]
[InlineData(1337)]
[InlineData(20260101)]
[InlineData(0xC0FFEE)]
public void Fuzz_RandomLegalActions_NeverViolateInvariants(int seed)
{
    var state = DemoWorldFixture.NewGame(seed);
    var rng = new Random(seed);

    for (var turn = 0; turn < 10_000; turn++)
    {
        var legal = state.GetAvailableActions();
        if (legal.Count == 0) break;
        ActionResolver.Resolve(state, legal[rng.Next(legal.Count)]);

        state.Player.Stats.Values.Should().OnlyContain(s => s.Value >= s.Def.Min && s.Value <= s.Def.Max);
        state.Journal.Should().BeInAscendingOrder(e => e.Seq);
    }
}
```

---

## 7. Coverage Gate

Engine core must stay **≥ 80% line coverage**, enforced by script, not by convention.

```powershell
dotnet test LifeSim.sln `
  --collect:"XPlat Code Coverage" `
  --results-directory ./TestResults `
  -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Include="[LifeSim.Core]*"

# Fail the build if Core coverage drops below the gate.
& ./scripts/check-coverage.ps1 -Path ./TestResults -Minimum 80
```

---

## 8. Related Skills

- LLM/fake-client testing: `microsoft-extensions-ai-testing`.
- Console snapshot testing: `spectre-console-tui`.
- Validator fault fixtures: `markdown-ast-validator`.
- Engine invariants & balance sims: `sim-balance-fuzzer`.
- Save fault tests: `save-game-persistence`.
