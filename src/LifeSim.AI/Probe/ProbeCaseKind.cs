namespace LifeSim.AI.Probe;

/// <summary>
/// The kind of contract a probe case exercises. <see cref="Json"/>, <see cref="RepairJson"/>
/// and <see cref="InjectionAsData"/> are JSON-contract cases and gate the exit code;
/// <see cref="Roleplay"/> is an informational text case.
/// </summary>
public enum ProbeCaseKind
{
    Json,
    RepairJson,
    Roleplay,
    InjectionAsData,
}
