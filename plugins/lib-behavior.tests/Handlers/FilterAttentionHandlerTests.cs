// =============================================================================
// Filter Attention Handler Unit Tests
// Tests for attention filtering (Cognition Stage 1).
// =============================================================================

using BeyondImmersion.BannouService.Abml.Cognition;
using BeyondImmersion.BannouService.Abml.Cognition.Handlers;
using BeyondImmersion.BannouService.Abml.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Abml.Expressions;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Handlers;

/// <summary>
/// Unit tests for FilterAttentionHandler.
/// </summary>
public class FilterAttentionHandlerTests : CognitionHandlerTestBase
{
    private readonly FilterAttentionHandler _handler = new();

    #region CanHandle Tests

    [Fact]
    public void CanHandle_FilterAttentionAction_ReturnsTrue()
    {
        var action = CreateDomainAction("filter_attention", new Dictionary<string, object?>());

        var result = _handler.CanHandle(action);

        Assert.True(result);
    }

    [Fact]
    public void CanHandle_OtherAction_ReturnsFalse()
    {
        var action = CreateDomainAction("other_action", new Dictionary<string, object?>());

        var result = _handler.CanHandle(action);

        Assert.False(result);
    }

    [Fact]
    public void CanHandle_NonDomainAction_ReturnsFalse()
    {
        var action = new SetAction("var", "value");

        var result = _handler.CanHandle(action);

        Assert.False(result);
    }

    #endregion

    #region ExecuteAsync Tests - Basic

    [Fact]
    public async Task ExecuteAsync_NullInput_ReturnsEmptyFilteredList()
    {
        var action = CreateDomainAction("filter_attention", new Dictionary<string, object?>
        {
            { "input", null }
        });
        var context = CreateTestContext();

        var result = await _handler.ExecuteAsync(action, context, CancellationToken.None);

        Assert.Equal(ActionResult.Continue, result);
        var filtered = GetScopeValue<IReadOnlyList<Perception>>(context, "filtered_perceptions");
        Assert.NotNull(filtered);
        Assert.Empty(filtered);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyInput_ReturnsEmptyFilteredList()
    {
        var action = CreateDomainAction("filter_attention", new Dictionary<string, object?>
        {
            { "input", new List<Perception>() }
        });
        var context = CreateTestContext();

        var result = await _handler.ExecuteAsync(action, context, CancellationToken.None);

        Assert.Equal(ActionResult.Continue, result);
        var filtered = GetScopeValue<IReadOnlyList<Perception>>(context, "filtered_perceptions");
        Assert.NotNull(filtered);
        Assert.Empty(filtered);
    }

    [Fact]
    public async Task ExecuteAsync_SinglePerception_FiltersSuccessfully()
    {
        var perceptions = new List<Perception>
        {
            CreatePerception("routine", "Test", 0.5f)
        };
        var action = CreateDomainAction("filter_attention", new Dictionary<string, object?>
        {
            { "input", perceptions }
        });
        var context = CreateTestContext();

        var result = await _handler.ExecuteAsync(action, context, CancellationToken.None);

        Assert.Equal(ActionResult.Continue, result);
        var filtered = GetScopeValue<IReadOnlyList<Perception>>(context, "filtered_perceptions");
        Assert.NotNull(filtered);
        Assert.Single(filtered);
    }

    #endregion

    #region ExecuteAsync Tests - Priority Weights

    [Fact]
    public async Task ExecuteAsync_ThreatPerception_HasHigherPriority()
    {
        var perceptions = new List<Perception>
        {
            CreatePerception("routine", "Normal event", 0.5f),
            CreatePerception("threat", "Danger!", 0.5f)
        };
        var action = CreateDomainAction("filter_attention", new Dictionary<string, object?>
        {
            { "input", perceptions },
            { "priority_weights", new Dictionary<string, object?>
                {
                    { "threat", 10.0f },
                    { "routine", 1.0f }
                }
            }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var filtered = GetScopeValue<IReadOnlyList<Perception>>(context, "filtered_perceptions");
        Assert.NotNull(filtered);
        Assert.Equal(2, filtered.Count);
        // Threat should have higher priority and come first
        Assert.Equal("threat", filtered[0].Category);
    }

    [Fact]
    public async Task ExecuteAsync_CustomWeights_AppliesCorrectly()
    {
        var perceptions = new List<Perception>
        {
            CreatePerception("social", "Friend approached", 0.5f),
            CreatePerception("novelty", "New discovery", 0.5f)
        };
        var action = CreateDomainAction("filter_attention", new Dictionary<string, object?>
        {
            { "input", perceptions },
            { "priority_weights", new Dictionary<string, object?>
                {
                    { "social", 20.0f },  // Higher than default novelty
                    { "novelty", 5.0f }
                }
            }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var filtered = GetScopeValue<IReadOnlyList<Perception>>(context, "filtered_perceptions");
        Assert.NotNull(filtered);
        // Social should be first with higher weight
        Assert.Equal("social", filtered[0].Category);
    }

    #endregion

    #region ExecuteAsync Tests - Budget Constraints

    [Fact]
    public async Task ExecuteAsync_MaxPerceptionsLimit_RespectsLimit()
    {
        var perceptions = Enumerable.Range(0, 20)
            .Select(i => CreatePerception("routine", $"Event {i}", 0.5f))
            .ToList();

        var action = CreateDomainAction("filter_attention", new Dictionary<string, object?>
        {
            { "input", perceptions },
            { "max_perceptions", 5 }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var filtered = GetScopeValue<IReadOnlyList<Perception>>(context, "filtered_perceptions");
        Assert.NotNull(filtered);
        Assert.True(filtered.Count <= 5);
    }

    [Fact]
    public async Task ExecuteAsync_AttentionBudget_LimitsOutput()
    {
        var perceptions = Enumerable.Range(0, 20)
            .Select(i => CreatePerception("threat", $"Threat {i}", 0.9f))
            .ToList();

        var action = CreateDomainAction("filter_attention", new Dictionary<string, object?>
        {
            { "input", perceptions },
            { "attention_budget", 30f },  // Low budget
            { "priority_weights", new Dictionary<string, object?>
                {
                    { "threat", 10.0f }  // Each threat costs 10
                }
            }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var filtered = GetScopeValue<IReadOnlyList<Perception>>(context, "filtered_perceptions");
        Assert.NotNull(filtered);
        // Should only fit 3 threats (30 / 10 = 3)
        Assert.True(filtered.Count <= 3);
    }

    #endregion

    #region ExecuteAsync Tests - Threat Fast Track

    [Fact]
    public async Task ExecuteAsync_ThreatFastTrack_SeparatesHighUrgencyThreats()
    {
        var perceptions = new List<Perception>
        {
            CreatePerception("routine", "Normal event", 0.5f),
            CreatePerception("threat", "Critical threat!", 0.9f)  // Above threshold
        };

        var action = CreateDomainAction("filter_attention", new Dictionary<string, object?>
        {
            { "input", perceptions },
            { "threat_fast_track", true },
            { "threat_threshold", 0.8f }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var fastTrack = GetScopeValue<IReadOnlyList<Perception>>(context, "fast_track_perceptions");
        var filtered = GetScopeValue<IReadOnlyList<Perception>>(context, "filtered_perceptions");

        Assert.NotNull(fastTrack);
        Assert.NotNull(filtered);
        Assert.Single(fastTrack);
        Assert.Equal("threat", fastTrack[0].Category);
        Assert.Single(filtered);
        Assert.Equal("routine", filtered[0].Category);
    }

    [Fact]
    public async Task ExecuteAsync_ThreatFastTrackDisabled_NoSeparation()
    {
        var perceptions = new List<Perception>
        {
            CreatePerception("threat", "Critical threat!", 0.9f)
        };

        var action = CreateDomainAction("filter_attention", new Dictionary<string, object?>
        {
            { "input", perceptions },
            { "threat_fast_track", false }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var fastTrack = GetScopeValue<IReadOnlyList<Perception>>(context, "fast_track_perceptions");
        var filtered = GetScopeValue<IReadOnlyList<Perception>>(context, "filtered_perceptions");

        Assert.NotNull(fastTrack);
        Assert.Empty(fastTrack);
        Assert.NotNull(filtered);
        Assert.Single(filtered);
    }

    [Fact]
    public async Task ExecuteAsync_ThreatBelowThreshold_NotFastTracked()
    {
        var perceptions = new List<Perception>
        {
            CreatePerception("threat", "Minor threat", 0.5f)  // Below threshold
        };

        var action = CreateDomainAction("filter_attention", new Dictionary<string, object?>
        {
            { "input", perceptions },
            { "threat_fast_track", true },
            { "threat_threshold", 0.8f }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var fastTrack = GetScopeValue<IReadOnlyList<Perception>>(context, "fast_track_perceptions");

        Assert.NotNull(fastTrack);
        Assert.Empty(fastTrack);
    }

    #endregion

    #region ExecuteAsync Tests - Result Variables

    [Fact]
    public async Task ExecuteAsync_CustomResultVariable_UsesCustomName()
    {
        var perceptions = new List<Perception>
        {
            CreatePerception("routine", "Test", 0.5f)
        };

        var action = CreateDomainAction("filter_attention", new Dictionary<string, object?>
        {
            { "input", perceptions },
            { "result_variable", "my_filtered" },
            { "fast_track_variable", "my_fast_track" }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var filtered = GetScopeValue<IReadOnlyList<Perception>>(context, "my_filtered");
        var fastTrack = GetScopeValue<IReadOnlyList<Perception>>(context, "my_fast_track");

        Assert.NotNull(filtered);
        Assert.NotNull(fastTrack);
    }

    #endregion

    #region ExecuteAsync Tests - Input Conversion

    [Fact]
    public async Task ExecuteAsync_DictionaryInput_ConvertsToPerception()
    {
        var dictPerception = new Dictionary<string, object?>
        {
            { "category", "threat" },
            { "content", "Enemy spotted" },
            { "urgency", 0.8f }
        };
        var input = new List<object> { dictPerception };

        var action = CreateDomainAction("filter_attention", new Dictionary<string, object?>
        {
            { "input", input }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var filtered = GetScopeValue<IReadOnlyList<Perception>>(context, "filtered_perceptions");
        Assert.NotNull(filtered);
        Assert.Single(filtered);
        Assert.Equal("threat", filtered[0].Category);
    }

    #endregion
}
