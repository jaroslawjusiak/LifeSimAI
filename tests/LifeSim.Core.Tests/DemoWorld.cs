using LifeSim.Core.Actions;
using LifeSim.Core.Entities;
using LifeSim.Core.Stats;
using LifeSim.Core.Time;

namespace LifeSim.Core.Tests;

/// <summary>
/// A small C#-hardcoded demo world (3 locations, 3 NPCs, 7 actions) used as the fastest
/// engine fixture. It exercises move/talk/work/eat/sleep/rest before the markdown loader
/// exists (M2) and stays here as a permanent integration fixture.
/// </summary>
public static class DemoWorld
{
    public static StatSet PlayerStats() => new(
    [
        new StatDef("energy", 0m, 100m, DecayPerHour: 2m, CriticalAt: 0m, Consequence: StatConsequence.PassOut),
        new StatDef("hunger", 0m, 100m, DecayPerHour: 2m, CriticalAt: 0m, Consequence: StatConsequence.Starve),
        new StatDef("health", 0m, 100m, CriticalAt: 0m),
        new StatDef("mood", 0m, 100m, DecayPerHour: 1m, CriticalAt: 0m),
    ]);

    public static SkillSet PlayerSkills() => new([new SkillDef("coding", "Coding", [100, 250], [])]);

    private static StatSet NpcStats() => new([new StatDef("mood", 0m, 100m)]);

    public static WorldState Create() => new(
        new Player("Alex", PlayerStats(), PlayerSkills(), 100m, "flat"),
        [
            new Npc("mia", "Mia", NpcStats(), new WeeklySchedule("cafe", [])),
            new Npc("boss", "Boss", NpcStats(), new WeeklySchedule("office", [])),
            new Npc("neighbor", "Neighbor", NpcStats(), new WeeklySchedule("flat", [])),
        ],
        [
            new Location("flat", "Flat", [new LocationConnection("office"), new LocationConnection("cafe")], ["sleep", "eat", "rest"], type: "home"),
            new Location("office", "Office", [new LocationConnection("flat")], ["work"], type: "workplace"),
            new Location("cafe", "Café", [new LocationConnection("flat")], ["eat", "talk"], type: "shop"),
        ],
        [new Item("coffee", "Coffee")],
        [new SkillDef("coding", "Coding", [100, 250], [])],
        Actions(),
        new GameClock(0, 8, 0));

    public static IReadOnlyList<ActionDefinition> Actions() =>
    [
        new("move_to_flat", ActionVerb.Move, "Move", 30, 0m, 0m, [], [new ActionEffect.Move("flat")]),
        new("move_to_office", ActionVerb.Move, "Move", 30, 0m, 0m, [], [new ActionEffect.Move("office")]),
        new("move_to_cafe", ActionVerb.Move, "Move", 30, 0m, 0m, [], [new ActionEffect.Move("cafe")]),
        new("work", ActionVerb.Work, "Work", 480, 10m, 0m,
            [new ActionRequirement.LocationIs("office")],
            [new ActionEffect.Money(100m)]),
        new("eat", ActionVerb.Eat, "Self-care", 60, 0m, 15m,
            [],
            [new ActionEffect.StatDelta("hunger", 30m), new ActionEffect.StatDelta("energy", 10m)]),
        new("sleep", ActionVerb.Sleep, "Self-care", 480, 0m, 0m,
            [],
            [new ActionEffect.StatDelta("energy", 40m)]),
        new("rest", ActionVerb.Socialize, "Self-care", 120, 0m, 0m,
            [],
            [new ActionEffect.StatDelta("mood", 20m), new ActionEffect.StatDelta("energy", 10m)]),
    ];
}
