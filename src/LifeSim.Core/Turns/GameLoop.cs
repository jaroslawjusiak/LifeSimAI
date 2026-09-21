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
            var result = RunPipeline(input);
            State = TurnState.Idle;
            return result;
        }
        catch (Exception ex)
        {
            // D3(a): record the stage that actually executed, never a hardcoded stage.
            var stage = State;
            JournalStageFailure(stage, ex.Message);
            State = TurnState.Idle;
            return new TurnResult(TurnState.Idle, false, null, null, string.Empty, [], [new TurnError(stage, ex.Message)]);
        }
    }

    private TurnResult RunPipeline(string input)
    {
        var errors = new List<TurnError>();

        if (!TryEnter(TurnState.InputReceived, errors))
        {
            return FailedTurn(errors);
        }

        JournalInput(input);

        if (!TryEnter(TurnState.Translating, errors))
        {
            return FailedTurn(errors);
        }

        // D4: a configured translator that throws is fatal to this turn. The exception escapes
        // to RunTurn's catch (state is Translating), which returns a typed failure — raw input is
        // never resolved as an action id. A null translator keeps the documented M1 seam of
        // treating the input as the action id.
        var command = Translator is not null ? Translator(input, _world) : new TranslatedCommand(input);

        JournalCommand(command);

        if (!TryEnter(TurnState.Validating, errors))
        {
            return FailedTurn(errors);
        }

        ActionValidation validation;
        try
        {
            validation = ActionResolver.Validate(_world, command.ActionId, command.TargetId);
        }
        catch (Exception ex)
        {
            RecordStageFailure(TurnState.Validating, ex.Message, errors);
            validation = ActionValidation.Invalid([ex.Message]);
        }

        if (!TryEnter(TurnState.Applying, errors))
        {
            return FailedTurn(errors);
        }

        ActionResult actionResult;
        if (validation.IsSuccess)
        {
            try
            {
                actionResult = ActionResolver.Apply(_world, validation, command.TargetId);
            }
            catch (Exception ex)
            {
                RecordStageFailure(TurnState.Applying, ex.Message, errors);
                actionResult = ActionResult.Failure(command.ActionId, [ex.Message]);
            }
        }
        else
        {
            // Validation failed: the Applying stage is entered (the state machine is linear) but
            // its work — the only mutating phase — does not run.
            actionResult = ActionResult.Failure(command.ActionId, validation.Reasons);
        }

        if (!TryEnter(TurnState.WorldTick, errors))
        {
            return FailedTurn(errors);
        }

        try
        {
            WorldTick?.Invoke(_world);
        }
        catch (Exception ex)
        {
            RecordStageFailure(TurnState.WorldTick, ex.Message, errors);
        }

        if (!TryEnter(TurnState.Narrating, errors))
        {
            return FailedTurn(errors);
        }

        string narration;
        try
        {
            narration = Narrator is not null ? Narrator(_world) : string.Empty;
        }
        catch (Exception ex)
        {
            RecordStageFailure(TurnState.Narrating, ex.Message, errors);
            narration = string.Empty;
        }

        if (!TryEnter(TurnState.Options, errors))
        {
            return FailedTurn(errors);
        }

        IReadOnlyList<string> options;
        try
        {
            options = OptionsGenerator is not null ? OptionsGenerator(_world) : [];
        }
        catch (Exception ex)
        {
            RecordStageFailure(TurnState.Options, ex.Message, errors);
            options = [];
        }

        if (!TryEnter(TurnState.Idle, errors))
        {
            return FailedTurn(errors);
        }

        return new TurnResult(
            TurnState.Idle,
            actionResult.IsSuccess,
            command.ActionId,
            actionResult,
            narration,
            options,
            errors);
    }

    /// <summary>
    /// Enters <paramref name="next"/> or, when the transition is refused, records a typed
    /// failure (D2) so the pipeline stops instead of continuing on a desynced timeline.
    /// </summary>
    private bool TryEnter(TurnState next, List<TurnError> errors)
    {
        if (TryTransition(next))
        {
            return true;
        }

        RecordStageFailure(State, $"Illegal pipeline transition from '{State}' to '{next}'.", errors);
        return false;
    }

    private void RecordStageFailure(TurnState stage, string message, List<TurnError> errors)
    {
        errors.Add(new TurnError(stage, message));
        JournalStageFailure(stage, message);
    }

    private static TurnResult FailedTurn(IReadOnlyList<TurnError> errors) =>
        new(TurnState.Idle, false, null, null, string.Empty, [], errors);

    private void JournalInput(string input)
    {
        _world.Journal.Append(
            TurnCorrelation.Current ?? string.Empty,
            _world.Clock,
            JournalEntryTypes.RawInput,
            input);
    }

    private void JournalCommand(TranslatedCommand command)
    {
        var payload = command.TargetId is null ? command.ActionId : $"{command.ActionId} -> {command.TargetId}";
        _world.Journal.Append(
            TurnCorrelation.Current ?? string.Empty,
            _world.Clock,
            JournalEntryTypes.TranslatedCommand,
            payload);
    }

    private void JournalStageFailure(TurnState stage, string message)
    {
        _world.Journal.Append(
            TurnCorrelation.Current ?? string.Empty,
            _world.Clock,
            JournalEntryTypes.StageFailed,
            $"{stage}: {message}");
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
