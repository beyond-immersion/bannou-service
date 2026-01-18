using BeyondImmersion.Bannou.MusicStoryteller.Actions;
using BeyondImmersion.Bannou.MusicStoryteller.Intent;
using BeyondImmersion.Bannou.MusicStoryteller.Narratives;
using BeyondImmersion.Bannou.MusicStoryteller.Planning;
using BeyondImmersion.Bannou.MusicStoryteller.State;

namespace BeyondImmersion.Bannou.MusicStoryteller;

/// <summary>
/// The main orchestrator for music storytelling.
/// Transforms composition requests into purposeful musical journeys
/// through narrative templates, GOAP planning, and intent generation.
/// Source: Plan Phase 8 - The Storyteller Orchestrator
/// </summary>
public sealed class Storyteller
{
    private readonly ActionLibrary _actions;
    private readonly GOAPPlanner _planner;
    private readonly Replanner _replanner;
    private readonly IntentGenerator _intentGenerator;
    private readonly NarrativeSelector _narrativeSelector;
    private readonly ActionSelection _actionSelection;

    /// <summary>
    /// Creates a new Storyteller with default components.
    /// </summary>
    public Storyteller()
    {
        _actions = new ActionLibrary();
        _planner = new GOAPPlanner(_actions);
        _replanner = new Replanner(_planner);
        _intentGenerator = new IntentGenerator();
        _narrativeSelector = new NarrativeSelector();
        _actionSelection = new ActionSelection(_actions);
    }

    /// <summary>
    /// Creates a Storyteller with custom components.
    /// </summary>
    /// <param name="actions">Action library.</param>
    /// <param name="narrativeSelector">Narrative selector.</param>
    public Storyteller(ActionLibrary actions, NarrativeSelector narrativeSelector)
    {
        _actions = actions;
        _planner = new GOAPPlanner(_actions);
        _replanner = new Replanner(_planner);
        _intentGenerator = new IntentGenerator();
        _narrativeSelector = narrativeSelector;
        _actionSelection = new ActionSelection(_actions);
    }

    /// <summary>
    /// Gets the action library.
    /// </summary>
    public ActionLibrary Actions => _actions;

    /// <summary>
    /// Gets the narrative selector.
    /// </summary>
    public NarrativeSelector Narratives => _narrativeSelector;

    /// <summary>
    /// Composes a musical journey based on a request.
    /// </summary>
    /// <param name="request">The composition request.</param>
    /// <returns>A complete composition result with intents for each section.</returns>
    public CompositionResult Compose(CompositionRequest request)
    {
        // 1. Select narrative template
        var narrative = request.TemplateId != null
            ? _narrativeSelector.GetById(request.TemplateId) ?? _narrativeSelector.Select(request)
            : _narrativeSelector.Select(request);

        // 2. Initialize state
        var state = new CompositionState();
        if (request.InitialEmotion != null)
        {
            state.Emotional.Tension = request.InitialEmotion.Tension;
            state.Emotional.Brightness = request.InitialEmotion.Brightness;
            state.Emotional.Energy = request.InitialEmotion.Energy;
            state.Emotional.Warmth = request.InitialEmotion.Warmth;
            state.Emotional.Stability = request.InitialEmotion.Stability;
            state.Emotional.Valence = request.InitialEmotion.Valence;
        }

        // 3. Process each phase
        var sections = new List<CompositionSection>();
        var totalBars = request.TotalBars > 0 ? request.TotalBars : narrative.IdealBars;

        for (var phaseIndex = 0; phaseIndex < narrative.Phases.Count; phaseIndex++)
        {
            var phase = narrative.Phases[phaseIndex];
            var (startBar, endBar) = narrative.GetPhaseBarRange(phaseIndex, totalBars);
            var phaseBars = endBar - startBar + 1;

            // 4. Create plan to reach phase target
            var goal = GOAPGoal.FromNarrativePhase(phase);
            var worldState = WorldState.FromCompositionState(state);
            var plan = _planner.CreatePlan(worldState, goal);

            // 5. Generate intents from plan
            var phaseIntents = _intentGenerator.FromPlan(plan, state, phase, phaseBars).ToList();

            // 6. Add transition if not first phase
            IReadOnlyList<CompositionIntent>? transitionIntents = null;
            if (phaseIndex > 0)
            {
                var prevPhase = narrative.Phases[phaseIndex - 1];
                var transitionBars = PhaseTransition.SuggestTransitionBars(prevPhase, phase, totalBars);
                var transitionPlan = PhaseTransition.CreateTransitionPlan(prevPhase, phase, transitionBars);
                transitionIntents = _intentGenerator.FromTransition(transitionPlan, state).ToList();
            }

            sections.Add(new CompositionSection
            {
                PhaseName = phase.Name,
                PhaseIndex = phaseIndex,
                StartBar = startBar,
                EndBar = endBar,
                Plan = plan,
                Intents = phaseIntents,
                TransitionIntents = transitionIntents
            });

            // 7. Apply plan effects to state for next phase
            foreach (var action in plan.MusicalActions)
            {
                action.Apply(state);
            }

            state.Position.Bar = endBar + 1;
        }

        return new CompositionResult
        {
            Request = request,
            Narrative = narrative,
            Sections = sections,
            TotalBars = totalBars,
            FinalState = state
        };
    }

    /// <summary>
    /// Composes using a specific narrative template.
    /// </summary>
    /// <param name="templateId">Template ID.</param>
    /// <param name="totalBars">Total bars.</param>
    /// <returns>Composition result.</returns>
    public CompositionResult ComposeWithTemplate(string templateId, int totalBars = 32)
    {
        var request = CompositionRequest.ForTemplate(templateId, totalBars);
        return Compose(request);
    }

    /// <summary>
    /// Gets the best action to take given current state and goal.
    /// Useful for step-by-step composition.
    /// </summary>
    /// <param name="state">Current composition state.</param>
    /// <param name="phase">Current narrative phase.</param>
    /// <returns>The best action to take, or null if goal is achieved.</returns>
    public IMusicalAction? GetNextAction(CompositionState state, NarrativePhase phase)
    {
        var goal = GOAPGoal.FromNarrativePhase(phase);
        var worldState = WorldState.FromCompositionState(state);

        if (goal.IsSatisfied(worldState))
            return null;

        return _actionSelection.SelectBest(goal, state);
    }

    /// <summary>
    /// Evaluates whether the current execution needs replanning.
    /// </summary>
    /// <param name="execution">Current plan execution.</param>
    /// <param name="state">Actual composition state.</param>
    /// <returns>Replan decision.</returns>
    public ReplanDecision EvaluateReplan(PlanExecution execution, CompositionState state)
    {
        var actualWorld = WorldState.FromCompositionState(state);
        return _replanner.Evaluate(execution, actualWorld);
    }

    /// <summary>
    /// Creates a new plan from the current state.
    /// </summary>
    /// <param name="state">Current composition state.</param>
    /// <param name="phase">Target phase.</param>
    /// <returns>A new plan.</returns>
    public Plan Replan(CompositionState state, NarrativePhase phase)
    {
        var goal = GOAPGoal.FromNarrativePhase(phase);
        return _replanner.Replan(state, goal);
    }

    /// <summary>
    /// Generates a single intent for the next section.
    /// </summary>
    /// <param name="action">The action to convert.</param>
    /// <param name="state">Current state.</param>
    /// <param name="phase">Current phase.</param>
    /// <param name="bars">Bars for this section.</param>
    /// <returns>A composition intent.</returns>
    public CompositionIntent GenerateIntent(
        IMusicalAction action,
        CompositionState state,
        NarrativePhase phase,
        int bars = 4)
    {
        return _intentGenerator.FromAction(action, state, phase, bars);
    }
}

/// <summary>
/// Result of a complete composition.
/// </summary>
public sealed class CompositionResult
{
    /// <summary>
    /// Gets the original request.
    /// </summary>
    public required CompositionRequest Request { get; init; }

    /// <summary>
    /// Gets the selected narrative template.
    /// </summary>
    public required NarrativeTemplate Narrative { get; init; }

    /// <summary>
    /// Gets the composed sections.
    /// </summary>
    public required IReadOnlyList<CompositionSection> Sections { get; init; }

    /// <summary>
    /// Gets the total bars in the composition.
    /// </summary>
    public required int TotalBars { get; init; }

    /// <summary>
    /// Gets the final composition state.
    /// </summary>
    public required CompositionState FinalState { get; init; }

    /// <summary>
    /// Gets all intents in order (including transitions).
    /// </summary>
    public IEnumerable<CompositionIntent> AllIntents
    {
        get
        {
            foreach (var section in Sections)
            {
                if (section.TransitionIntents != null)
                {
                    foreach (var intent in section.TransitionIntents)
                    {
                        yield return intent;
                    }
                }

                foreach (var intent in section.Intents)
                {
                    yield return intent;
                }
            }
        }
    }

    /// <summary>
    /// Gets total number of intents generated.
    /// </summary>
    public int TotalIntents => Sections.Sum(s =>
        s.Intents.Count + (s.TransitionIntents?.Count ?? 0));
}

/// <summary>
/// A section of the composition corresponding to a narrative phase.
/// </summary>
public sealed class CompositionSection
{
    /// <summary>
    /// Gets the name of the narrative phase.
    /// </summary>
    public required string PhaseName { get; init; }

    /// <summary>
    /// Gets the phase index within the narrative.
    /// </summary>
    public required int PhaseIndex { get; init; }

    /// <summary>
    /// Gets the starting bar.
    /// </summary>
    public required int StartBar { get; init; }

    /// <summary>
    /// Gets the ending bar.
    /// </summary>
    public required int EndBar { get; init; }

    /// <summary>
    /// Gets the GOAP plan for this section.
    /// </summary>
    public required Plan Plan { get; init; }

    /// <summary>
    /// Gets the composition intents for this section.
    /// </summary>
    public required IReadOnlyList<CompositionIntent> Intents { get; init; }

    /// <summary>
    /// Gets the transition intents leading into this section.
    /// Null for the first section.
    /// </summary>
    public IReadOnlyList<CompositionIntent>? TransitionIntents { get; init; }

    /// <summary>
    /// Gets the total bars in this section.
    /// </summary>
    public int TotalBars => EndBar - StartBar + 1;
}
