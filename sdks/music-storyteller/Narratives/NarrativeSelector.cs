using BeyondImmersion.Bannou.MusicStoryteller.Narratives.Templates;
using BeyondImmersion.Bannou.MusicStoryteller.State;

namespace BeyondImmersion.Bannou.MusicStoryteller.Narratives;

/// <summary>
/// Selects the most appropriate narrative template for a composition request.
/// Matches templates based on tags, emotional targets, and duration.
/// </summary>
public sealed class NarrativeSelector
{
    private readonly List<NarrativeTemplate> _templates = [];

    /// <summary>
    /// Gets all registered templates.
    /// </summary>
    public IEnumerable<NarrativeTemplate> Templates => _templates;

    /// <summary>
    /// Creates a selector with all built-in templates.
    /// </summary>
    public NarrativeSelector()
    {
        RegisterBuiltInTemplates();
    }

    /// <summary>
    /// Registers a custom template.
    /// </summary>
    /// <param name="template">The template to register.</param>
    public void Register(NarrativeTemplate template)
    {
        if (!template.IsValid())
        {
            throw new ArgumentException("Template is not valid (phase durations must sum to 1.0)", nameof(template));
        }

        _templates.Add(template);
    }

    /// <summary>
    /// Selects the best template for a composition request.
    /// </summary>
    /// <param name="request">The composition request.</param>
    /// <returns>The best matching template.</returns>
    public NarrativeTemplate Select(CompositionRequest request)
    {
        var scores = new List<(NarrativeTemplate template, double score)>();

        foreach (var template in _templates)
        {
            var score = ScoreTemplate(template, request);
            scores.Add((template, score));
        }

        // Return the highest scoring template, or SimpleArc as fallback
        var best = scores
            .OrderByDescending(s => s.score)
            .FirstOrDefault();

        return best.template ?? SimpleArc.Template;
    }

    /// <summary>
    /// Gets all templates matching specific tags.
    /// </summary>
    /// <param name="tags">Tags to match.</param>
    /// <returns>Matching templates.</returns>
    public IEnumerable<NarrativeTemplate> GetByTags(params string[] tags)
    {
        var tagSet = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);

        return _templates.Where(t =>
            t.Tags.Any(tag => tagSet.Contains(tag)));
    }

    /// <summary>
    /// Gets a template by ID.
    /// </summary>
    /// <param name="id">Template ID.</param>
    /// <returns>The template, or null if not found.</returns>
    public NarrativeTemplate? GetById(string id)
    {
        return _templates.FirstOrDefault(t =>
            t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    private double ScoreTemplate(NarrativeTemplate template, CompositionRequest request)
    {
        var score = 0.0;

        // Tag matching (highest weight)
        if (request.Tags.Count > 0)
        {
            var matchingTags = template.Tags
                .Count(t => request.Tags.Contains(t, StringComparer.OrdinalIgnoreCase));
            score += matchingTags * 2.0;
        }

        // Duration compatibility
        if (request.TotalBars > 0)
        {
            if (request.TotalBars >= template.MinimumBars &&
                request.TotalBars <= template.IdealBars * 2)
            {
                score += 1.0;

                // Bonus for being close to ideal
                var distanceFromIdeal = Math.Abs(request.TotalBars - template.IdealBars);
                score += Math.Max(0, 1.0 - distanceFromIdeal / (double)template.IdealBars);
            }
        }

        // Target emotional state matching
        if (request.TargetEmotion != null)
        {
            // Check if any phase's target is close to the request target
            var closestPhaseDistance = template.Phases
                .Min(p => p.EmotionalTarget.DistanceTo(request.TargetEmotion));

            // Closer distance = higher score
            score += Math.Max(0, 2.0 - closestPhaseDistance);
        }

        // Mood matching based on initial state
        if (request.InitialEmotion != null)
        {
            var firstPhase = template.Phases[0];
            var initialDistance = firstPhase.EmotionalTarget.DistanceTo(request.InitialEmotion);

            // Reward templates that start near the initial state
            score += Math.Max(0, 1.0 - initialDistance);
        }

        // Modulation preference
        if (request.AllowModulation && template.SupportsModulation)
        {
            score += 0.5;
        }
        else if (!request.AllowModulation && !template.SupportsModulation)
        {
            score += 0.5;
        }

        return score;
    }

    private void RegisterBuiltInTemplates()
    {
        _templates.Add(JourneyAndReturn.Template);
        _templates.Add(TensionAndRelease.Template);
        _templates.Add(SimpleArc.Template);
    }
}

/// <summary>
/// A request for composition that guides template selection.
/// </summary>
public sealed class CompositionRequest
{
    /// <summary>
    /// Gets or sets the total bars for the composition.
    /// </summary>
    public int TotalBars { get; set; }

    /// <summary>
    /// Gets or sets tags for template matching (e.g., "celtic", "dramatic").
    /// </summary>
    public IReadOnlyList<string> Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets the target emotional state to reach.
    /// </summary>
    public EmotionalState? TargetEmotion { get; set; }

    /// <summary>
    /// Gets or sets the initial emotional state.
    /// </summary>
    public EmotionalState? InitialEmotion { get; set; }

    /// <summary>
    /// Gets or sets whether modulation is allowed.
    /// </summary>
    public bool AllowModulation { get; set; } = true;

    /// <summary>
    /// Gets or sets a specific template ID to use (overrides matching).
    /// </summary>
    public string? TemplateId { get; set; }

    /// <summary>
    /// Gets or sets the target style name.
    /// </summary>
    public string? StyleName { get; set; }

    /// <summary>
    /// Creates a default composition request.
    /// </summary>
    public static CompositionRequest Default => new()
    {
        TotalBars = 32,
        AllowModulation = true
    };

    /// <summary>
    /// Creates a request for a specific template.
    /// </summary>
    /// <param name="templateId">The template ID.</param>
    /// <param name="totalBars">Total bars.</param>
    /// <returns>A composition request.</returns>
    public static CompositionRequest ForTemplate(string templateId, int totalBars = 32) => new()
    {
        TemplateId = templateId,
        TotalBars = totalBars
    };

    /// <summary>
    /// Creates a request with specific tags.
    /// </summary>
    /// <param name="tags">Tags to match.</param>
    /// <returns>A composition request.</returns>
    public static CompositionRequest WithTags(params string[] tags) => new()
    {
        Tags = tags,
        TotalBars = 32
    };
}
