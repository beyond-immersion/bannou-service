using BeyondImmersion.BannouService.Mapping.Helpers;
using System.Text.Json;

namespace BeyondImmersion.BannouService.Mapping.Tests;

/// <summary>
/// Unit tests for AffordanceScorer helper methods.
/// </summary>
public class AffordanceScorerTests
{
    private readonly AffordanceScorer _sut;

    public AffordanceScorerTests()
    {
        _sut = new AffordanceScorer();
    }

    #region GetKindsForAffordanceType Tests

    [Fact]
    public void GetKindsForAffordanceType_Ambush_ReturnsCorrectKinds()
    {
        // Act
        var kinds = _sut.GetKindsForAffordanceType(AffordanceType.Ambush);

        // Assert
        Assert.Contains(MapKind.Static_geometry, kinds);
        Assert.Contains(MapKind.Dynamic_objects, kinds);
        Assert.Contains(MapKind.Navigation, kinds);
    }

    [Fact]
    public void GetKindsForAffordanceType_Shelter_ReturnsCorrectKinds()
    {
        // Act
        var kinds = _sut.GetKindsForAffordanceType(AffordanceType.Shelter);

        // Assert
        Assert.Contains(MapKind.Static_geometry, kinds);
        Assert.Contains(MapKind.Dynamic_objects, kinds);
        Assert.Equal(2, kinds.Count);
    }

    [Fact]
    public void GetKindsForAffordanceType_Vista_ReturnsCorrectKinds()
    {
        // Act
        var kinds = _sut.GetKindsForAffordanceType(AffordanceType.Vista);

        // Assert
        Assert.Contains(MapKind.Terrain, kinds);
        Assert.Contains(MapKind.Static_geometry, kinds);
        Assert.Contains(MapKind.Points_of_interest, kinds);
    }

    [Fact]
    public void GetKindsForAffordanceType_ChokePoint_ReturnsCorrectKinds()
    {
        // Act
        var kinds = _sut.GetKindsForAffordanceType(AffordanceType.Choke_point);

        // Assert
        Assert.Contains(MapKind.Navigation, kinds);
        Assert.Contains(MapKind.Static_geometry, kinds);
    }

    [Fact]
    public void GetKindsForAffordanceType_Custom_ReturnsAllKinds()
    {
        // Act
        var kinds = _sut.GetKindsForAffordanceType(AffordanceType.Custom);

        // Assert - Should return all kinds
        var allKinds = Enum.GetValues<MapKind>();
        Assert.Equal(allKinds.Length, kinds.Count);
    }

    [Fact]
    public void GetKindsForAffordanceType_DefensiblePosition_ReturnsCorrectKinds()
    {
        // Act
        var kinds = _sut.GetKindsForAffordanceType(AffordanceType.Defensible_position);

        // Assert
        Assert.Contains(MapKind.Static_geometry, kinds);
        Assert.Contains(MapKind.Terrain, kinds);
        Assert.Contains(MapKind.Navigation, kinds);
    }

    #endregion

    #region ScoreAffordance Tests

    [Fact]
    public void ScoreAffordance_BasicCandidate_ReturnsBaseScore()
    {
        // Arrange
        var candidate = CreateMapObject();

        // Act
        var score = _sut.ScoreAffordance(candidate, AffordanceType.Ambush, null, null);

        // Assert - Base score is 0.5
        Assert.Equal(0.5, score);
    }

    [Fact]
    public void ScoreAffordance_WithCoverRating_IncreasesScoreForAmbush()
    {
        // Arrange
        var data = JsonSerializer.SerializeToElement(new { cover_rating = 0.8 });
        var candidate = CreateMapObject(data);

        // Act
        var score = _sut.ScoreAffordance(candidate, AffordanceType.Ambush, null, null);

        // Assert - Should add 0.8 * 0.3 = 0.24 to base score
        Assert.True(score > 0.5);
        Assert.InRange(score, 0.7, 0.75);
    }

    [Fact]
    public void ScoreAffordance_WithCoverRating_IncreasesScoreForShelter()
    {
        // Arrange
        var data = JsonSerializer.SerializeToElement(new { cover_rating = 0.8 });
        var candidate = CreateMapObject(data);

        // Act
        var score = _sut.ScoreAffordance(candidate, AffordanceType.Shelter, null, null);

        // Assert
        Assert.True(score > 0.5);
    }

    [Fact]
    public void ScoreAffordance_WithCoverRating_IncreasesScoreForDefensiblePosition()
    {
        // Arrange
        var data = JsonSerializer.SerializeToElement(new { cover_rating = 0.8 });
        var candidate = CreateMapObject(data);

        // Act
        var score = _sut.ScoreAffordance(candidate, AffordanceType.Defensible_position, null, null);

        // Assert
        Assert.True(score > 0.5);
    }

    [Fact]
    public void ScoreAffordance_WithElevation_IncreasesScoreForVista()
    {
        // Arrange
        var data = JsonSerializer.SerializeToElement(new { elevation = 50.0 });
        var candidate = CreateMapObject(data);

        // Act
        var score = _sut.ScoreAffordance(candidate, AffordanceType.Vista, null, null);

        // Assert - Math.Min(50/100, 0.3) = 0.3 added to base score 0.5 = 0.8
        Assert.True(score > 0.5);
        Assert.Equal(0.8, score);
    }

    [Fact]
    public void ScoreAffordance_WithElevation_IncreasesScoreForDramaticReveal()
    {
        // Arrange
        var data = JsonSerializer.SerializeToElement(new { elevation = 50.0 });
        var candidate = CreateMapObject(data);

        // Act
        var score = _sut.ScoreAffordance(candidate, AffordanceType.Dramatic_reveal, null, null);

        // Assert
        Assert.True(score > 0.5);
    }

    [Fact]
    public void ScoreAffordance_WithSightlines_IncreasesScoreForAmbush()
    {
        // Arrange
        var data = JsonSerializer.SerializeToElement(new { sightlines = 4 });
        var candidate = CreateMapObject(data);

        // Act
        var score = _sut.ScoreAffordance(candidate, AffordanceType.Ambush, null, null);

        // Assert - 4 * 0.05 = 0.2 added to base score
        Assert.True(score > 0.5);
        Assert.InRange(score, 0.69, 0.71);
    }

    [Fact]
    public void ScoreAffordance_WithTinyActor_IncreasesScoreForShelter()
    {
        // Arrange
        var candidate = CreateMapObject();
        var actor = new ActorCapabilities { Size = ActorSize.Tiny };

        // Act
        var score = _sut.ScoreAffordance(candidate, AffordanceType.Shelter, null, actor);

        // Assert - Base 0.5 * 1.2 = 0.6
        Assert.Equal(0.6, score);
    }

    [Fact]
    public void ScoreAffordance_WithHugeActor_DecreasesScoreForShelter()
    {
        // Arrange
        var candidate = CreateMapObject();
        var actor = new ActorCapabilities { Size = ActorSize.Huge };

        // Act
        var score = _sut.ScoreAffordance(candidate, AffordanceType.Shelter, null, actor);

        // Assert - Base 0.5 * 0.8 = 0.4
        Assert.Equal(0.4, score);
    }

    [Fact]
    public void ScoreAffordance_WithStealthRating_IncreasesScoreForAmbush()
    {
        // Arrange
        var candidate = CreateMapObject();
        var actor = new ActorCapabilities { Size = ActorSize.Medium, StealthRating = 0.5 };

        // Act
        var score = _sut.ScoreAffordance(candidate, AffordanceType.Ambush, null, actor);

        // Assert - Base 0.5 * (1.0 + 0.5 * 0.2) = 0.5 * 1.1 = 0.55
        Assert.Equal(0.55, score);
    }

    [Fact]
    public void ScoreAffordance_ClampsToMaxOne()
    {
        // Arrange
        var data = JsonSerializer.SerializeToElement(new
        {
            cover_rating = 1.0,
            sightlines = 10
        });
        var candidate = CreateMapObject(data);
        var actor = new ActorCapabilities { Size = ActorSize.Tiny, StealthRating = 1.0 };

        // Act
        var score = _sut.ScoreAffordance(candidate, AffordanceType.Ambush, null, actor);

        // Assert - Should be clamped to 1.0
        Assert.Equal(1.0, score);
    }

    [Fact]
    public void ScoreAffordance_ClampsToMinZero()
    {
        // This shouldn't happen with normal values, but the clamp should work
        var candidate = CreateMapObject();

        // Act
        var score = _sut.ScoreAffordance(candidate, AffordanceType.Vista, null, null);

        // Assert - Should be clamped to at least 0.0
        Assert.True(score >= 0.0);
    }

    #endregion

    #region ScoreAffordance Custom Tests

    [Fact]
    public void ScoreAffordance_CustomWithRequiredType_MatchingType_Passes()
    {
        // Arrange
        var candidate = CreateMapObject(objectType: "tree");
        var custom = new CustomAffordance
        {
            Requires = JsonSerializer.SerializeToElement(new
            {
                objectTypes = new[] { "tree", "bush" }
            })
        };

        // Act
        var score = _sut.ScoreAffordance(candidate, AffordanceType.Custom, custom, null);

        // Assert
        Assert.True(score > 0);
    }

    [Fact]
    public void ScoreAffordance_CustomWithRequiredType_NonMatchingType_ReturnsZero()
    {
        // Arrange
        var candidate = CreateMapObject(objectType: "rock");
        var custom = new CustomAffordance
        {
            Requires = JsonSerializer.SerializeToElement(new
            {
                objectTypes = new[] { "tree", "bush" }
            })
        };

        // Act
        var score = _sut.ScoreAffordance(candidate, AffordanceType.Custom, custom, null);

        // Assert
        Assert.Equal(0.0, score);
    }

    [Fact]
    public void ScoreAffordance_CustomWithMinRequirement_BelowMin_ReturnsZero()
    {
        // Arrange
        var data = JsonSerializer.SerializeToElement(new { height = 5.0 });
        var candidate = CreateMapObject(data);
        var custom = new CustomAffordance
        {
            Requires = JsonSerializer.SerializeToElement(new
            {
                height = new { min = 10.0 }
            })
        };

        // Act
        var score = _sut.ScoreAffordance(candidate, AffordanceType.Custom, custom, null);

        // Assert
        Assert.Equal(0.0, score);
    }

    [Fact]
    public void ScoreAffordance_CustomWithMinRequirement_AboveMin_Passes()
    {
        // Arrange
        var data = JsonSerializer.SerializeToElement(new { height = 15.0 });
        var candidate = CreateMapObject(data);
        var custom = new CustomAffordance
        {
            Requires = JsonSerializer.SerializeToElement(new
            {
                height = new { min = 10.0 }
            })
        };

        // Act
        var score = _sut.ScoreAffordance(candidate, AffordanceType.Custom, custom, null);

        // Assert
        Assert.True(score > 0);
    }

    [Fact]
    public void ScoreAffordance_CustomWithPreferences_BoostsScore()
    {
        // Arrange
        var data = JsonSerializer.SerializeToElement(new { bonus_feature = true });
        var candidate = CreateMapObject(data);
        var custom = new CustomAffordance
        {
            Prefers = JsonSerializer.SerializeToElement(new
            {
                bonus_feature = true
            })
        };

        // Act
        var score = _sut.ScoreAffordance(candidate, AffordanceType.Custom, custom, null);

        // Assert - Should boost score by 0.1
        Assert.Equal(0.6, score);
    }

    [Fact]
    public void ScoreAffordance_CustomWithExclusion_MatchingExclusion_ReturnsZero()
    {
        // Arrange
        var data = JsonSerializer.SerializeToElement(new { excluded_property = true });
        var candidate = CreateMapObject(data);
        var custom = new CustomAffordance
        {
            Excludes = JsonSerializer.SerializeToElement(new
            {
                excluded_property = true
            })
        };

        // Act
        var score = _sut.ScoreAffordance(candidate, AffordanceType.Custom, custom, null);

        // Assert
        Assert.Equal(0.0, score);
    }

    #endregion

    #region ExtractFeatures Tests

    [Fact]
    public void ExtractFeatures_Ambush_ExtractsRelevantFeatures()
    {
        // Arrange
        var data = JsonSerializer.SerializeToElement(new
        {
            cover_rating = 0.8,
            sightlines = 4,
            concealment = "dense",
            irrelevant = "value"
        });
        var candidate = CreateMapObject(data);

        // Act
        var features = _sut.ExtractFeatures(candidate, AffordanceType.Ambush) as Dictionary<string, object>;

        // Assert
        Assert.NotNull(features);
        Assert.True(features.ContainsKey("cover_rating"));
        Assert.True(features.ContainsKey("sightlines"));
        Assert.True(features.ContainsKey("concealment"));
        Assert.False(features.ContainsKey("irrelevant"));
    }

    [Fact]
    public void ExtractFeatures_Vista_ExtractsRelevantFeatures()
    {
        // Arrange
        var data = JsonSerializer.SerializeToElement(new
        {
            elevation = 50.0,
            visibility_range = 100,
            sightlines = 6
        });
        var candidate = CreateMapObject(data);

        // Act
        var features = _sut.ExtractFeatures(candidate, AffordanceType.Vista) as Dictionary<string, object>;

        // Assert
        Assert.NotNull(features);
        Assert.True(features.ContainsKey("elevation"));
        Assert.True(features.ContainsKey("visibility_range"));
        Assert.True(features.ContainsKey("sightlines"));
    }

    [Fact]
    public void ExtractFeatures_IncludesObjectTypeWhenOtherFeaturesPresent()
    {
        // Arrange - Need to include a relevant feature for Shelter
        var data = JsonSerializer.SerializeToElement(new { cover_rating = 0.8 });
        var candidate = CreateMapObject(data, objectType: "special_object");

        // Act
        var features = _sut.ExtractFeatures(candidate, AffordanceType.Shelter) as Dictionary<string, object>;

        // Assert - Should include objectType when there are other features
        Assert.NotNull(features);
        Assert.True(features.ContainsKey("objectType"));
        Assert.Equal("special_object", features["objectType"]);
    }

    [Fact]
    public void ExtractFeatures_NoRelevantFeatures_ReturnsNull()
    {
        // Arrange - Data with no relevant features for Ambush
        var data = JsonSerializer.SerializeToElement(new { unrelated = "data" });
        var candidate = CreateMapObject(data);

        // Act
        var features = _sut.ExtractFeatures(candidate, AffordanceType.Ambush);

        // Assert - When only objectType is present (count == 1), returns null
        Assert.Null(features);
    }

    [Fact]
    public void ExtractFeatures_Shelter_ExtractsCorrectFeatures()
    {
        // Arrange
        var data = JsonSerializer.SerializeToElement(new
        {
            cover_rating = 0.9,
            protection = "full",
            capacity = 4
        });
        var candidate = CreateMapObject(data);

        // Act
        var features = _sut.ExtractFeatures(candidate, AffordanceType.Shelter) as Dictionary<string, object>;

        // Assert
        Assert.NotNull(features);
        Assert.True(features.ContainsKey("cover_rating"));
        Assert.True(features.ContainsKey("protection"));
        Assert.True(features.ContainsKey("capacity"));
    }

    [Fact]
    public void ExtractFeatures_ChokePoint_ExtractsCorrectFeatures()
    {
        // Arrange
        var data = JsonSerializer.SerializeToElement(new
        {
            width = 2.5,
            defensibility = 0.8,
            exit_count = 2
        });
        var candidate = CreateMapObject(data);

        // Act
        var features = _sut.ExtractFeatures(candidate, AffordanceType.Choke_point) as Dictionary<string, object>;

        // Assert
        Assert.NotNull(features);
        Assert.True(features.ContainsKey("width"));
        Assert.True(features.ContainsKey("defensibility"));
        Assert.True(features.ContainsKey("exit_count"));
    }

    [Fact]
    public void ExtractFeatures_GatheringSpot_ExtractsCorrectFeatures()
    {
        // Arrange
        var data = JsonSerializer.SerializeToElement(new
        {
            capacity = 10,
            comfort = 0.7,
            accessibility = "easy"
        });
        var candidate = CreateMapObject(data);

        // Act
        var features = _sut.ExtractFeatures(candidate, AffordanceType.Gathering_spot) as Dictionary<string, object>;

        // Assert
        Assert.NotNull(features);
        Assert.True(features.ContainsKey("capacity"));
        Assert.True(features.ContainsKey("comfort"));
        Assert.True(features.ContainsKey("accessibility"));
    }

    [Fact]
    public void ExtractFeatures_HiddenPath_ExtractsCorrectFeatures()
    {
        // Arrange
        var data = JsonSerializer.SerializeToElement(new
        {
            concealment = "dense",
            width = 1.5,
            traversability = 0.9
        });
        var candidate = CreateMapObject(data);

        // Act
        var features = _sut.ExtractFeatures(candidate, AffordanceType.Hidden_path) as Dictionary<string, object>;

        // Assert
        Assert.NotNull(features);
        Assert.True(features.ContainsKey("concealment"));
        Assert.True(features.ContainsKey("width"));
        Assert.True(features.ContainsKey("traversability"));
    }

    [Fact]
    public void ExtractFeatures_ExtractsNumericValuesCorrectly()
    {
        // Arrange
        var data = JsonSerializer.SerializeToElement(new { elevation = 123.456 });
        var candidate = CreateMapObject(data);

        // Act
        var features = _sut.ExtractFeatures(candidate, AffordanceType.Vista) as Dictionary<string, object>;

        // Assert
        Assert.NotNull(features);
        Assert.True(features.ContainsKey("elevation"));
        Assert.Equal(123.456, features["elevation"]);
    }

    [Fact]
    public void ExtractFeatures_ExtractsStringValuesCorrectly()
    {
        // Arrange
        var data = JsonSerializer.SerializeToElement(new { protection = "full" });
        var candidate = CreateMapObject(data);

        // Act
        var features = _sut.ExtractFeatures(candidate, AffordanceType.Shelter) as Dictionary<string, object>;

        // Assert
        Assert.NotNull(features);
        Assert.True(features.ContainsKey("protection"));
        Assert.Equal("full", features["protection"]);
    }

    [Fact]
    public void ExtractFeatures_ExtractsBooleanValuesCorrectly()
    {
        // Arrange - We need a property that would match one of the relevant keys
        // Let's use concealment with a boolean value
        var data = JsonSerializer.SerializeToElement(new { concealment = true });
        var candidate = CreateMapObject(data);

        // Act
        var features = _sut.ExtractFeatures(candidate, AffordanceType.Ambush) as Dictionary<string, object>;

        // Assert
        Assert.NotNull(features);
        Assert.True(features.ContainsKey("concealment"));
        Assert.Equal(true, features["concealment"]);
    }

    #endregion

    #region Helper Methods

    private static MapObject CreateMapObject(JsonElement? data = null, string objectType = "test_object")
    {
        return new MapObject
        {
            ObjectId = Guid.NewGuid(),
            ObjectType = objectType,
            Kind = MapKind.Static_geometry,
            Data = data
        };
    }

    private static MapObject CreateMapObject(object dataObject, string objectType = "test_object")
    {
        var data = JsonSerializer.SerializeToElement(dataObject);
        return CreateMapObject(data, objectType);
    }

    #endregion
}
