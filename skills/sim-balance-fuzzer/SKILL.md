---
name: sim-balance-fuzzer
description: |
  Guides the implementation, headless balancing, and invariant fuzzing of the
  deterministic simulation engine in LifeSim Engine (LifeSim.Core).
  Covers GameClock transitions, decaying StatSets with once-per-crossing thresholds,
  transactional Action resolvers, Monte Carlo strategy bots, and seeded invariant testing.
---

# Deterministic Simulation, Balance Fuzzing & Invariant Testing in LifeSim Engine

This skill guides the construction of the headless engine core ([`LifeSim.Core`](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L25)) in .NET 10. It enforces total determinism, atomic state mutations, and headless balance simulations.

---

## 1. Architectural Principles

1. **Pure Engine Determinism:** Given identical initial state, config, and random seed, re-running a sequence of actions or journals must yield identical state hashes ([M1-06](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L265)).
2. **Transactional Action Resolver:** An action evaluates all preconditions first. If ANY requirement fails, return `ActionResult.Failure(reasons)` and mutate **nothing** (proven by snapshot-equality tests). If all pass, apply deltas atomically ([M1-04](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L227)).
3. **Clamped Bounds & Once-Per-Crossing Events:** Stats clamp strictly within $[min, max]$. Critical threshold events (e.g., energy $\le 0$ triggering pass-out) must fire exactly once upon crossing, never spamming every tick ([M1-02](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L190)).
4. **Headless Balance Simulation:** Balance the economy and stat curves by simulating 100 in-game months headless using scripted strategy bots before shipping world content ([M8-01](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L1025)).

---

## 2. Core Engine Components

### A. GameClock & Phased Time ([M1-01](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L173))

```csharp
public enum DayPhase { Night, Morning, Midday, Evening }

public record struct GameClock(int DayIndex, DayOfWeek DayOfWeek, int Hour, int Minute)
{
    public DayPhase Phase => Hour switch
    {
        >= 6 and < 12 => DayPhase.Morning,
        >= 12 and < 17 => DayPhase.Midday,
        >= 17 and < 22 => DayPhase.Evening,
        _ => DayPhase.Night
    };

    public (GameClock NewClock, int HoursPassed, bool DayStarted) Advance(int minutes)
    {
        int totalMinutes = Minute + minutes;
        int deltaHours = totalMinutes / 60;
        int newMinute = totalMinutes % 60;

        int totalHours = Hour + deltaHours;
        int deltaDays = totalHours / 24;
        int newHour = totalHours % 24;

        int newDayIndex = DayIndex + deltaDays;
        var newDayOfWeek = (DayOfWeek)(((int)DayOfWeek + deltaDays) % 7);

        return (new GameClock(newDayIndex, newDayOfWeek, newHour, newMinute), deltaHours, deltaDays > 0);
    }
}
```

---

### B. Clamped StatSet with Once-Per-Crossing Thresholds ([M1-02](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L190))

```csharp
public record StatDef(string Id, int Min, int Max, int DecayPerHour, int CriticalThreshold);

public sealed class StatInstance(StatDef def, int initialValue)
{
    public StatDef Def { get; } = def;
    public int Value { get; private set; } = Math.Clamp(initialValue, def.Min, def.Max);
    public bool IsCriticalArmed { get; private set; } = initialValue > def.CriticalThreshold;

    public bool ApplyDelta(int delta, out bool thresholdCrossed)
    {
        thresholdCrossed = false;
        int prev = Value;
        Value = Math.Clamp(Value + delta, Def.Min, Def.Max);

        if (IsCriticalArmed && Value <= Def.CriticalThreshold)
        {
            thresholdCrossed = true;
            IsCriticalArmed = false; // Disarm until recovery
        }
        else if (!IsCriticalArmed && Value > Def.CriticalThreshold)
        {
            IsCriticalArmed = true; // Rearm after recovering above threshold
        }

        return Value != prev;
    }
}
```

---

### C. Transactional Action Resolver ([M1-04](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L227))

```csharp
public sealed class ActionResolver
{
    public static ActionResult Resolve(WorldState state, ActionDefinition action)
    {
        // 1. Evaluate Preconditions (Read-only check)
        var failures = new List<string>();
        foreach (var req in action.Requirements)
        {
            if (!req.IsSatisfied(state, out string reason))
                failures.Add(reason);
        }

        if (failures.Count > 0)
        {
            // Snapshot equality guaranteed: zero mutations performed
            return ActionResult.Failure(action.Id, failures);
        }

        // 2. Apply Effects Atomically
        foreach (var effect in action.Effects)
        {
            effect.Apply(state);
        }

        // 3. Advance Clock & Tick Hourly Decay
        var (newClock, hoursPassed, dayStarted) = state.Clock.Advance(action.TimeMinutes);
        state.Clock = newClock;

        for (int i = 0; i < hoursPassed; i++)
        {
            state.ApplyHourlyDecay();
        }

        return ActionResult.Success(action.Id);
    }
}
```

---

## 3. Headless Balance Simulation & Strategy Bots ([M8-01](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L1025))

Simulate high-volume playthroughs to verify game economy, survival rate, and pacing:

```csharp
public interface ISimulationStrategy
{
    ActionDefinition SelectAction(WorldState state, IReadOnlyList<ActionDefinition> availableActions);
}

public sealed class BalanceSimulator(WorldState initialState)
{
    public SimulationMetrics RunMonths(ISimulationStrategy strategy, int monthsToSimulate, int seed)
    {
        var rng = new Random(seed);
        int totalDays = monthsToSimulate * 30;
        int survivedDays = 0;
        decimal peakMoney = 0;
        int passOutCount = 0;

        for (int day = 0; day < totalDays; day++)
        {
            while (initialState.Clock.Hour < 23)
            {
                var legal = initialState.GetAvailableActions();
                if (legal.Count == 0) break;

                var chosen = strategy.SelectAction(initialState, legal);
                var result = ActionResolver.Resolve(initialState, chosen);

                if (initialState.Player.Energy <= 0) passOutCount++;
                peakMoney = Math.Max(peakMoney, initialState.Player.Money);
            }

            if (initialState.Player.IsDead || initialState.Player.Money < -1000)
                break;

            survivedDays++;
            initialState.AdvanceToNextDay();
        }

        return new SimulationMetrics(
            SurvivalRate: (double)survivedDays / totalDays,
            PeakMoney: peakMoney,
            PassOutCount: passOutCount
        );
    }
}
```

---

## 4. Invariant Fuzz Testing ([M9-01](file:///C:/1/Repos/LifeSimAI/Tasks/Implementation-plan/plan.md#L1107))

```csharp
public class EngineInvariantFuzzerTests
{
    [Fact]
    public void FuzzRandomActions_InvariantsNeverViolated()
    {
        var state = DemoWorldFixture.CreateWorld();
        var rng = new Random(1337);

        for (int turn = 0; turn < 1000; turn++)
        {
            var actions = state.GetAvailableActions();
            if (actions.Count == 0) break;

            var action = actions[rng.Next(actions.Count)];
            ActionResolver.Resolve(state, action);

            // Invariant Assertions
            state.Player.Energy.Should().BeInRange(0, 100);
            state.Player.Hunger.Should().BeInRange(0, 100);
            state.Clock.Hour.Should().BeInRange(0, 23);
            state.Clock.Minute.Should().BeInRange(0, 59);
        }
    }
}
```
