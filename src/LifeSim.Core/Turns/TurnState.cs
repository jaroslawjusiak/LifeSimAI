namespace LifeSim.Core.Turns;

/// <summary>
/// The states of the per-turn pipeline. Stages marked <c>*</c> in the plan are AI seams
/// (Translating, Narrating, Options); the rest are engine stages.
/// </summary>
public enum TurnState
{
    Idle,
    InputReceived,
    Translating,
    Validating,
    Applying,
    WorldTick,
    Narrating,
    Options,
}
