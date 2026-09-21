namespace LifeSim.Core.Turns;

/// <summary>The result of the translation stage: a canonical action and optional target.</summary>
public sealed record TranslatedCommand(string ActionId, string? TargetId = null);
