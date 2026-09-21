namespace LifeSim.Core.Turns;

/// <summary>A typed stage failure recovered by the turn loop.</summary>
public sealed record TurnError(TurnState Stage, string Message);
