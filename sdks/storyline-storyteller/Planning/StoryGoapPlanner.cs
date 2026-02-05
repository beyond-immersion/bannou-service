// Copyright (c) Beyond Immersion. All rights reserved.

using BeyondImmersion.Bannou.StorylineTheory.Arcs;
using BeyondImmersion.Bannou.StorylineTheory.Planning;
using BeyondImmersion.Bannou.StorylineTheory.Spectrums;
using BeyondImmersion.Bannou.StorylineStoryteller.Actions;
using BeyondImmersion.Bannou.StorylineStoryteller.Templates;

namespace BeyondImmersion.Bannou.StorylineStoryteller.Planning;

/// <summary>
/// GOAP planner using A* search for story action sequencing.
/// </summary>
public sealed class StoryGoapPlanner
{
    /// <summary>
    /// Plans a sequence of actions to reach a target phase.
    /// </summary>
    /// <param name="initialState">Starting world state.</param>
    /// <param name="template">Story template being followed.</param>
    /// <param name="targetPhase">Target phase to reach.</param>
    /// <param name="genre">Genre for action filtering.</param>
    /// <param name="primarySpectrum">Primary spectrum for heuristic calculation.</param>
    /// <param name="urgency">Planning urgency affecting search parameters.</param>
    /// <returns>A storyline plan for the phase.</returns>
    public StorylinePlan Plan(
        WorldState initialState,
        StoryTemplate template,
        StoryPhase targetPhase,
        string genre,
        SpectrumType primarySpectrum,
        PlanningUrgency urgency = PlanningUrgency.Medium)
    {
        var (maxIterations, beamWidth) = GetSearchParameters(urgency);

        // A* search from initialState to targetPhase.TargetState
        var plannedActions = AStarSearch(
            initialState,
            targetPhase,
            genre,
            primarySpectrum,
            maxIterations,
            beamWidth);

        // Project end state by applying all actions
        var endState = initialState;
        foreach (var action in plannedActions)
        {
            endState = action.Execute(endState);
        }

        // Build plan actions
        var planActions = new List<StorylinePlanAction>();
        for (var i = 0; i < plannedActions.Count; i++)
        {
            var action = plannedActions[i];
            planActions.Add(new StorylinePlanAction
            {
                ActionId = action.Id,
                SequenceIndex = i,
                Effects = action.Effects,
                NarrativeEffect = action.NarrativeEffect,
                IsCoreEvent = action.IsCoreEvent,
                ChainedFrom = null
            });
        }

        // Build the phase plan
        var phasePlan = new StorylinePlanPhase
        {
            PhaseNumber = targetPhase.PhaseNumber,
            PhaseName = targetPhase.Name,
            Actions = planActions.ToArray(),
            TargetState = targetPhase.TargetState,
            PositionBounds = targetPhase.Position
        };

        // Get required core events for genre
        var coreEvents = ActionRegistry.GetCoreEvents(genre)
            .Select(a => a.Id)
            .ToArray();

        return new StorylinePlan
        {
            PlanId = Guid.NewGuid(),
            ArcType = template.ArcType,
            Genre = genre,
            Subgenre = null,
            PrimarySpectrum = primarySpectrum,
            Phases = new[] { phasePlan },
            InitialState = initialState,
            ProjectedEndState = endState,
            RequiredCoreEvents = coreEvents
        };
    }

    private static (int MaxIterations, int BeamWidth) GetSearchParameters(PlanningUrgency urgency)
    {
        return urgency switch
        {
            PlanningUrgency.Low => (1000, 20),
            PlanningUrgency.Medium => (500, 15),
            PlanningUrgency.High => (200, 10),
            _ => (500, 15)
        };
    }

    private static List<StoryAction> AStarSearch(
        WorldState initialState,
        StoryPhase targetPhase,
        string genre,
        SpectrumType primarySpectrum,
        int maxIterations,
        int beamWidth)
    {
        // Priority queue for A* frontier (min-heap by f-score)
        var frontier = new PriorityQueue<SearchNode, double>();
        var visited = new HashSet<string>();

        var startNode = new SearchNode
        {
            State = initialState,
            Actions = new List<StoryAction>(),
            GScore = 0,
            HScore = Heuristic(initialState, targetPhase, primarySpectrum)
        };

        frontier.Enqueue(startNode, startNode.FScore);

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            if (frontier.Count == 0)
            {
                break;
            }

            var current = frontier.Dequeue();

            // Goal check: primary spectrum within target range
            var spectrumValue = current.State.NarrativeState[primarySpectrum] ?? 0.5;
            if (spectrumValue >= targetPhase.TargetState.MinPrimarySpectrum &&
                spectrumValue <= targetPhase.TargetState.MaxPrimarySpectrum)
            {
                return current.Actions;
            }

            // Generate state hash for visited check
            var stateHash = GetStateHash(current.State);
            if (visited.Contains(stateHash))
            {
                continue;
            }
            visited.Add(stateHash);

            // Expand applicable actions
            var applicableActions = ActionRegistry.GetApplicable(current.State, genre).ToList();

            // Beam search: only consider top N actions by heuristic
            var beamActions = applicableActions
                .OrderBy(a => a.Cost)
                .Take(beamWidth)
                .ToList();

            foreach (var action in beamActions)
            {
                var newState = action.Execute(current.State);
                var newActions = new List<StoryAction>(current.Actions) { action };
                var gScore = current.GScore + action.Cost;
                var hScore = Heuristic(newState, targetPhase, primarySpectrum);

                var newNode = new SearchNode
                {
                    State = newState,
                    Actions = newActions,
                    GScore = gScore,
                    HScore = hScore
                };

                frontier.Enqueue(newNode, newNode.FScore);
            }
        }

        // Return best partial plan if goal not reached
        return startNode.Actions;
    }

    private static double Heuristic(WorldState state, StoryPhase targetPhase, SpectrumType primarySpectrum)
    {
        // Euclidean distance to target state center
        var currentValue = state.NarrativeState[primarySpectrum] ?? 0.5;
        var targetCenter = (targetPhase.TargetState.MinPrimarySpectrum + targetPhase.TargetState.MaxPrimarySpectrum) / 2;
        return Math.Abs(currentValue - targetCenter);
    }

    private static string GetStateHash(WorldState state)
    {
        // Simple hash based on key facts
        var hash = state.Position.GetHashCode();
        foreach (var kvp in state.Facts.OrderBy(k => k.Key))
        {
            hash = HashCode.Combine(hash, kvp.Key, kvp.Value);
        }
        return hash.ToString();
    }

    private sealed class SearchNode
    {
        public required WorldState State { get; init; }
        public required List<StoryAction> Actions { get; init; }
        public required double GScore { get; init; }
        public required double HScore { get; init; }
        public double FScore => GScore + HScore;
    }
}
