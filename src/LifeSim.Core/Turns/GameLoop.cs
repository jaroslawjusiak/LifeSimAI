using LifeSim.Core.Actions;
using LifeSim.Core.Diagnostics;
using LifeSim.Core.Entities;
using LifeSim.Core.Journal;

namespace LifeSim.Core.Turns;

/// <summary>
/// Drives the per-turn pipeline through its states, journaling every transition with the
/// turn correlation id. AI seams (translator, narrator, options) are injectable delegates
/// with scripted fallbacks; any stage failure is recovered as a typed <see cref="TurnError"/>
/// and the loop never throws across the UI boundary.
/// </summary>
public sealed class GameLoop
{
    private static readonly IReadOnlyDictionary<TurnState, TurnState> LegalTransitions =
        new Dictionary<TurnState, TurnState>
        {
            [TurnState.Idle] = TurnState.InputReceived,
            [TurnState.InputReceived] = TurnState.Translating,
            [TurnState.Translating] = TurnState.Validating,
            [TurnState.Validating] = TurnState.Applying,
            [TurnState.Applying] = TurnState.WorldTick,
            [TurnState.WorldTick] = TurnState.Narrating,
            [TurnState.Narrating] = TurnState.Options,
            [TurnState.Options] = TurnState.Idle,
        };

    private readonly WorldState _world;

    public GameLoop(WorldState world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    public TurnState State { get; private set; } = TurnState.Idle;

    /// <summary>AI seam: maps free text to a canonical command. Null treats input as the action id.</summary>
    public Func<string, WorldState, TranslatedCommand>? Translator { get; set; }

    /// <summary>Optional deterministic world-tick hook (NPC schedules).</summary>
    public Action<WorldState>? WorldTick { get; set; }

    /// <summary>AI seam: narrates the resolved action. Null yields empty narration.</summary>
    public Func<WorldState, string>? Narrator { get; set; }

    /// <summary>AI seam: proposes next options. Null yields no options.</summary>
    public Func<WorldState, IReadOnlyList<string>>? OptionsGenerator { get; set; }

    /// <summary>
    /// Attempts a legal state transition, journaling it on success. Returns false for an
    /// illegal transition (the state is left unchanged).
    /// </summary>
    public bool TryTransition(TurnState next)
    {
        if (!LegalTransitions.TryGetValue(State, out var expected) || expected != next)
        {
            return false;
        }

        State = next;
        JournalStage(next);
        return true;
    }

    /// <summary>Runs one turn from <see cref="TurnState.Idle"/> and returns to <see cref="TurnState.Idle"/>.</summary>
    public TurnResult RunTurn(string input)
    {
        using var _ = TurnCorrelation.Begin();

        try
        {
            return RunPipeline(input);
        }
        catch (Exception ex)
        {
            State = TurnState.Idle;
            return new TurnResult(TurnState.Idle, false, null, null, string.Empty, [], [new TurnError(TurnState.Applying, ex.Message)]);
        }
    }

    private TurnResult RunPipeline(string input)
    {
        var errors = new List<TurnError>();

        if (!TryTransition(TurnState.InputReceived))
        {
            return new TurnResult(TurnState.Idle, false, null, null, string.Empty, [], errors);
        }

        TranslatedCommand command;
        try
        {
            command = Translator is not null ? Translator(input, _world) : new TranslatedCommand(input);
        }
        catch (Exception ex)
        {
            errors.Add(new TurnError(TurnState.Translating, ex.Message));
            command = new TranslatedCommand(input);
        }

        TryTransition(TurnState.Translating);
        TryTransition(TurnState.Validating);

        ActionResult actionResult;
        try
        {
            actionResult = ActionResolver.Resolve(_world, command.ActionId, command.TargetId);
        }
        catch (Exception ex)
        {
            errors.Add(new TurnError(TurnState.Applying, ex.Message));
            actionResult = ActionResult.Failure(command.ActionId, [ex.Message]);
        }

        TryTransition(TurnState.Applying);

        try
        {
            WorldTick?.Invoke(_world);
        }
        catch (Exception ex)
        {
            errors.Add(new TurnError(TurnState.WorldTick, ex.Message));
        }

        TryTransition(TurnState.WorldTick);

        string narration;
        try
        {
            narration = Narrator is not null ? Narrator(_world) : string.Empty;
        }
        catch (Exception ex)
        {
            errors.Add(new TurnError(TurnState.Narrating, ex.Message));
            narration = string.Empty;
        }

        TryTransition(TurnState.Narrating);

        IReadOnlyList<string> options;
        try
        {
            options = OptionsGenerator is not null ? OptionsGenerator(_world) : [];
        }
        catch (Exception ex)
        {
            errors.Add(new TurnError(TurnState.Options, ex.Message));
            options = [];
        }

        TryTransition(TurnState.Options);
        TryTransition(TurnState.Idle);

        return new TurnResult(
            TurnState.Idle,
            actionResult.IsSuccess,
            command.ActionId,
            actionResult,
            narration,
            options,
            errors);
    }

    private void JournalStage(TurnState state)
    {
        _world.Journal.Append(
            TurnCorrelation.Current ?? string.Empty,
            _world.Clock,
            JournalEntryTypes.StageTransition,
            state.ToString());
    }
}
