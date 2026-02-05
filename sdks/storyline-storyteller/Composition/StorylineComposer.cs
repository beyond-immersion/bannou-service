// Copyright (c) Beyond Immersion. All rights reserved.

using BeyondImmersion.Bannou.StorylineTheory.Archives;
using BeyondImmersion.Bannou.StorylineTheory.Planning;
using BeyondImmersion.Bannou.StorylineStoryteller.Planning;
using BeyondImmersion.Bannou.StorylineStoryteller.Templates;

namespace BeyondImmersion.Bannou.StorylineStoryteller.Composition;

/// <summary>
/// Main entry point for storyline composition.
/// </summary>
public sealed class StorylineComposer
{
    private readonly StoryGoapPlanner _planner;
    private readonly PhaseEvaluator _phaseEvaluator;
    private readonly ActionCountResolver _actionCountResolver;
    private readonly ArchiveExtractor _archiveExtractor;

    /// <summary>
    /// Creates a new storyline composer.
    /// </summary>
    public StorylineComposer()
    {
        _planner = new StoryGoapPlanner();
        _phaseEvaluator = new PhaseEvaluator();
        _actionCountResolver = new ActionCountResolver();
        _archiveExtractor = new ArchiveExtractor();
    }

    /// <summary>
    /// Composes a storyline plan from context and archive data.
    /// </summary>
    /// <param name="context">Story generation context.</param>
    /// <param name="archives">Archive bundle with character/realm data.</param>
    /// <returns>The composed storyline plan.</returns>
    public StorylinePlan Compose(StoryContext context, ArchiveBundle archives)
    {
        // Extract initial world state from archives
        var initialState = _archiveExtractor.ExtractWorldState(archives, context.PrimarySpectrum);
        context.CurrentState = initialState;

        // Resolve target action count
        var targetActions = _actionCountResolver.ResolveTargetActionCount(
            context.Template,
            context.Genre,
            context.TargetActionCount);

        // Initialize progress tracking
        context.Progress.OnPhaseGenerated(targetActions / context.Template.Phases.Length, context.Template.Phases.Length);

        // Get the first phase
        var currentPhase = context.Template.Phases[0];

        // Plan the first phase
        var plan = _planner.Plan(
            initialState,
            context.Template,
            currentPhase,
            context.Genre,
            context.PrimarySpectrum,
            PlanningUrgency.Medium);

        return plan;
    }

    /// <summary>
    /// Continues a storyline from the current context state.
    /// </summary>
    /// <param name="context">Story generation context with current state.</param>
    /// <returns>The next phase plan, or null if story is complete.</returns>
    public StorylinePlan? ContinuePhase(StoryContext context)
    {
        if (context.CurrentState == null)
        {
            throw new InvalidOperationException("Context has no current state. Call Compose first.");
        }

        // Check if story is complete
        if (_phaseEvaluator.IsComplete(context.Template, context.CurrentState))
        {
            return null;
        }

        // Get current phase based on position
        var currentPhase = _phaseEvaluator.GetCurrentPhase(context.Template, context.CurrentState.Position);

        // Check transition
        var transitionResult = _phaseEvaluator.CheckTransition(
            context.CurrentState,
            currentPhase,
            context.PrimarySpectrum);

        if (transitionResult == PhaseTransitionResult.NotReady)
        {
            // Continue in current phase
            return _planner.Plan(
                context.CurrentState,
                context.Template,
                currentPhase,
                context.Genre,
                context.PrimarySpectrum,
                PlanningUrgency.Medium);
        }

        // Advance to next phase
        context.Progress.OnPhaseCompleted();

        var nextPhaseIndex = currentPhase.PhaseNumber; // PhaseNumber is 1-based, so this is the next index
        if (nextPhaseIndex >= context.Template.Phases.Length)
        {
            return null; // Story complete
        }

        var nextPhase = context.Template.Phases[nextPhaseIndex];
        context.Progress.OnPhaseGenerated(
            context.Progress.CurrentPhaseActionsPlanned,
            context.Template.Phases.Length - nextPhaseIndex - 1);

        return _planner.Plan(
            context.CurrentState,
            context.Template,
            nextPhase,
            context.Genre,
            context.PrimarySpectrum,
            PlanningUrgency.Medium);
    }
}
