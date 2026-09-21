namespace LifeSim.AI.Probe;

/// <summary>A single chat message sent to the model during a probe case.</summary>
public sealed record ProbeMessage(string Role, string Content);
