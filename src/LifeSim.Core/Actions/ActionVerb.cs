namespace LifeSim.Core.Actions;

/// <summary>
/// Built-in action verbs. <see cref="Custom"/> is for world-defined actions whose mechanics
/// are expressed purely through requirements and effects.
/// </summary>
public enum ActionVerb
{
    Move,
    Talk,
    Work,
    Study,
    Eat,
    Sleep,
    Shop,
    Exercise,
    Socialize,
    Custom,
}
