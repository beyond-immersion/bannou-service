using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Tests;

/// <summary>
/// Tests for ConfigConstraintGroupAttribute, ConfigConstraintGroupDefinitionAttribute,
/// and constraint group validation in configuration classes.
/// </summary>
[Collection("unit tests")]
public class ConfigConstraintGroupValidationTests : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    public ConfigConstraintGroupValidationTests(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigConstraintGroupValidationTests>.Instance;
    }

    #region Test Configuration Classes

    [ServiceConfiguration]
    [ConfigConstraintGroupDefinition("weights", ConstraintGroupType.SumEquals, Value = 1.0)]
    private class SumEqualsValidConfig : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigConstraintGroup("weights")]
        public double? WeightA { get; set; } = 0.25;

        [ConfigConstraintGroup("weights")]
        public double? WeightB { get; set; } = 0.25;

        [ConfigConstraintGroup("weights")]
        public double? WeightC { get; set; } = 0.25;

        [ConfigConstraintGroup("weights")]
        public double? WeightD { get; set; } = 0.25;
    }

    [ServiceConfiguration]
    [ConfigConstraintGroupDefinition("weights", ConstraintGroupType.SumEquals, Value = 1.0)]
    private class SumEqualsInvalidConfig : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigConstraintGroup("weights")]
        public double? WeightA { get; set; } = 0.5;

        [ConfigConstraintGroup("weights")]
        public double? WeightB { get; set; } = 0.5;

        [ConfigConstraintGroup("weights")]
        public double? WeightC { get; set; } = 0.25;

        [ConfigConstraintGroup("weights")]
        public double? WeightD { get; set; } = 0.25;
    }

    [ServiceConfiguration]
    [ConfigConstraintGroupDefinition("weights", ConstraintGroupType.SumEquals, Value = 1.0, Tolerance = 0.01)]
    private class SumEqualsCustomToleranceConfig : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigConstraintGroup("weights")]
        public double? WeightA { get; set; } = 0.333;

        [ConfigConstraintGroup("weights")]
        public double? WeightB { get; set; } = 0.333;

        [ConfigConstraintGroup("weights")]
        public double? WeightC { get; set; } = 0.334;
    }

    [ServiceConfiguration]
    [ConfigConstraintGroupDefinition("weights", ConstraintGroupType.SumEquals, Value = 1.0)]
    private class SumEqualsWithNullConfig : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigConstraintGroup("weights")]
        public double? WeightA { get; set; } = 1.0;

        [ConfigConstraintGroup("weights")]
        public double? WeightB { get; set; }  // null = contributes 0
    }

    [ServiceConfiguration]
    [ConfigConstraintGroupDefinition("provider", ConstraintGroupType.ExactlyOne)]
    private class ExactlyOneValidConfig : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigConstraintGroup("provider")]
        public string? OptionA { get; set; } = "selected";

        [ConfigConstraintGroup("provider")]
        public string? OptionB { get; set; }

        [ConfigConstraintGroup("provider")]
        public string? OptionC { get; set; }
    }

    [ServiceConfiguration]
    [ConfigConstraintGroupDefinition("provider", ConstraintGroupType.ExactlyOne)]
    private class ExactlyOneNoneSetConfig : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigConstraintGroup("provider")]
        public string? OptionA { get; set; }

        [ConfigConstraintGroup("provider")]
        public string? OptionB { get; set; }
    }

    [ServiceConfiguration]
    [ConfigConstraintGroupDefinition("provider", ConstraintGroupType.ExactlyOne)]
    private class ExactlyOneTwoSetConfig : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigConstraintGroup("provider")]
        public string? OptionA { get; set; } = "first";

        [ConfigConstraintGroup("provider")]
        public string? OptionB { get; set; } = "second";
    }

    [ServiceConfiguration]
    [ConfigConstraintGroupDefinition("backend", ConstraintGroupType.AtMostOne)]
    private class AtMostOneNoneSetConfig : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigConstraintGroup("backend")]
        public string? BackendA { get; set; }

        [ConfigConstraintGroup("backend")]
        public string? BackendB { get; set; }
    }

    [ServiceConfiguration]
    [ConfigConstraintGroupDefinition("backend", ConstraintGroupType.AtMostOne)]
    private class AtMostOneOneSetConfig : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigConstraintGroup("backend")]
        public string? BackendA { get; set; } = "redis://localhost";

        [ConfigConstraintGroup("backend")]
        public string? BackendB { get; set; }
    }

    [ServiceConfiguration]
    [ConfigConstraintGroupDefinition("backend", ConstraintGroupType.AtMostOne)]
    private class AtMostOneTwoSetConfig : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigConstraintGroup("backend")]
        public string? BackendA { get; set; } = "redis://localhost";

        [ConfigConstraintGroup("backend")]
        public string? BackendB { get; set; } = "mysql://localhost";
    }

    [ServiceConfiguration]
    [ConfigConstraintGroupDefinition("tls", ConstraintGroupType.AllOrNone)]
    private class AllOrNoneAllSetConfig : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigConstraintGroup("tls")]
        public string? CertPath { get; set; } = "/etc/ssl/cert.pem";

        [ConfigConstraintGroup("tls")]
        public string? KeyPath { get; set; } = "/etc/ssl/key.pem";
    }

    [ServiceConfiguration]
    [ConfigConstraintGroupDefinition("tls", ConstraintGroupType.AllOrNone)]
    private class AllOrNoneNoneSetConfig : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigConstraintGroup("tls")]
        public string? CertPath { get; set; }

        [ConfigConstraintGroup("tls")]
        public string? KeyPath { get; set; }
    }

    [ServiceConfiguration]
    [ConfigConstraintGroupDefinition("tls", ConstraintGroupType.AllOrNone)]
    private class AllOrNonePartialConfig : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigConstraintGroup("tls")]
        public string? CertPath { get; set; } = "/etc/ssl/cert.pem";

        [ConfigConstraintGroup("tls")]
        public string? KeyPath { get; set; }
    }

    [ServiceConfiguration]
    [ConfigConstraintGroupDefinition("limits", ConstraintGroupType.SumMinimum, Value = 10.0)]
    private class SumMinimumValidConfig : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigConstraintGroup("limits")]
        public double? LimitA { get; set; } = 6.0;

        [ConfigConstraintGroup("limits")]
        public double? LimitB { get; set; } = 5.0;
    }

    [ServiceConfiguration]
    [ConfigConstraintGroupDefinition("limits", ConstraintGroupType.SumMinimum, Value = 10.0)]
    private class SumMinimumInvalidConfig : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigConstraintGroup("limits")]
        public double? LimitA { get; set; } = 3.0;

        [ConfigConstraintGroup("limits")]
        public double? LimitB { get; set; } = 4.0;
    }

    [ServiceConfiguration]
    [ConfigConstraintGroupDefinition("caps", ConstraintGroupType.SumMaximum, Value = 100.0)]
    private class SumMaximumValidConfig : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigConstraintGroup("caps")]
        public double? CapA { get; set; } = 30.0;

        [ConfigConstraintGroup("caps")]
        public double? CapB { get; set; } = 40.0;
    }

    [ServiceConfiguration]
    [ConfigConstraintGroupDefinition("caps", ConstraintGroupType.SumMaximum, Value = 100.0)]
    private class SumMaximumInvalidConfig : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigConstraintGroup("caps")]
        public double? CapA { get; set; } = 60.0;

        [ConfigConstraintGroup("caps")]
        public double? CapB { get; set; } = 50.0;
    }

    [ServiceConfiguration]
    [ConfigConstraintGroupDefinition("weights", ConstraintGroupType.SumEquals, Value = 1.0)]
    [ConfigConstraintGroupDefinition("provider", ConstraintGroupType.ExactlyOne)]
    private class MultiGroupValidConfig : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigConstraintGroup("weights")]
        public double? WeightA { get; set; } = 0.5;

        [ConfigConstraintGroup("weights")]
        public double? WeightB { get; set; } = 0.5;

        [ConfigConstraintGroup("provider")]
        public string? ProviderA { get; set; } = "selected";

        [ConfigConstraintGroup("provider")]
        public string? ProviderB { get; set; }
    }

    [ServiceConfiguration]
    private class NoGroupsConfig : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        public string? SomeProperty { get; set; } = "value";
    }

    [ServiceConfiguration]
    [ConfigConstraintGroupDefinition("weights", ConstraintGroupType.SumEquals, Value = 1.0)]
    private class SumEqualsIntegerConfig : IServiceConfiguration
    {
        public Guid? ForceServiceId { get; set; }

        [ConfigConstraintGroup("weights")]
        public int? PartA { get; set; } = 1;

        [ConfigConstraintGroup("weights")]
        public int? PartB { get; set; } = 0;
    }

    #endregion

    #region SumEquals Tests

    [Fact]
    public void ValidateConstraintGroups_SumEquals_ValidWeights_NoException()
    {
        IServiceConfiguration config = new SumEqualsValidConfig();
        var exception = Record.Exception(() => config.ValidateConstraintGroups());
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateConstraintGroups_SumEquals_InvalidWeights_ThrowsException()
    {
        IServiceConfiguration config = new SumEqualsInvalidConfig();
        var exception = Assert.Throws<InvalidOperationException>(() => config.ValidateConstraintGroups());
        Assert.Contains("weights", exception.Message);
        Assert.Contains("SumEquals", exception.Message);
        Assert.Contains("sum=1.5", exception.Message);
    }

    [Fact]
    public void ValidateConstraintGroups_SumEquals_CustomTolerance_PassesWithinTolerance()
    {
        // 0.333 + 0.333 + 0.334 = 1.0 within 0.01 tolerance
        IServiceConfiguration config = new SumEqualsCustomToleranceConfig();
        var exception = Record.Exception(() => config.ValidateConstraintGroups());
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateConstraintGroups_SumEquals_NullContributesZero()
    {
        // 1.0 + 0 (null) = 1.0
        IServiceConfiguration config = new SumEqualsWithNullConfig();
        var exception = Record.Exception(() => config.ValidateConstraintGroups());
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateConstraintGroups_SumEquals_IntegerWeights_Validates()
    {
        // 1 + 0 = 1.0
        IServiceConfiguration config = new SumEqualsIntegerConfig();
        var exception = Record.Exception(() => config.ValidateConstraintGroups());
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateConstraintGroups_SumEquals_FloatingPointPrecision()
    {
        // 0.25 + 0.25 + 0.25 + 0.25 should equal 1.0 within default tolerance
        var config = new SumEqualsValidConfig();
        IServiceConfiguration svc = config;
        var exception = Record.Exception(() => svc.ValidateConstraintGroups());
        Assert.Null(exception);
    }

    #endregion

    #region SumMinimum Tests

    [Fact]
    public void ValidateConstraintGroups_SumMinimum_AboveMinimum_NoException()
    {
        IServiceConfiguration config = new SumMinimumValidConfig();
        var exception = Record.Exception(() => config.ValidateConstraintGroups());
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateConstraintGroups_SumMinimum_BelowMinimum_ThrowsException()
    {
        IServiceConfiguration config = new SumMinimumInvalidConfig();
        var exception = Assert.Throws<InvalidOperationException>(() => config.ValidateConstraintGroups());
        Assert.Contains("limits", exception.Message);
        Assert.Contains("SumMinimum", exception.Message);
    }

    #endregion

    #region SumMaximum Tests

    [Fact]
    public void ValidateConstraintGroups_SumMaximum_BelowMaximum_NoException()
    {
        IServiceConfiguration config = new SumMaximumValidConfig();
        var exception = Record.Exception(() => config.ValidateConstraintGroups());
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateConstraintGroups_SumMaximum_AboveMaximum_ThrowsException()
    {
        IServiceConfiguration config = new SumMaximumInvalidConfig();
        var exception = Assert.Throws<InvalidOperationException>(() => config.ValidateConstraintGroups());
        Assert.Contains("caps", exception.Message);
        Assert.Contains("SumMaximum", exception.Message);
    }

    #endregion

    #region ExactlyOne Tests

    [Fact]
    public void ValidateConstraintGroups_ExactlyOne_OneSet_NoException()
    {
        IServiceConfiguration config = new ExactlyOneValidConfig();
        var exception = Record.Exception(() => config.ValidateConstraintGroups());
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateConstraintGroups_ExactlyOne_NoneSet_ThrowsException()
    {
        IServiceConfiguration config = new ExactlyOneNoneSetConfig();
        var exception = Assert.Throws<InvalidOperationException>(() => config.ValidateConstraintGroups());
        Assert.Contains("provider", exception.Message);
        Assert.Contains("ExactlyOne", exception.Message);
        Assert.Contains("0 set", exception.Message);
    }

    [Fact]
    public void ValidateConstraintGroups_ExactlyOne_TwoSet_ThrowsException()
    {
        IServiceConfiguration config = new ExactlyOneTwoSetConfig();
        var exception = Assert.Throws<InvalidOperationException>(() => config.ValidateConstraintGroups());
        Assert.Contains("provider", exception.Message);
        Assert.Contains("ExactlyOne", exception.Message);
        Assert.Contains("2 set", exception.Message);
    }

    #endregion

    #region AtMostOne Tests

    [Fact]
    public void ValidateConstraintGroups_AtMostOne_NoneSet_NoException()
    {
        IServiceConfiguration config = new AtMostOneNoneSetConfig();
        var exception = Record.Exception(() => config.ValidateConstraintGroups());
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateConstraintGroups_AtMostOne_OneSet_NoException()
    {
        IServiceConfiguration config = new AtMostOneOneSetConfig();
        var exception = Record.Exception(() => config.ValidateConstraintGroups());
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateConstraintGroups_AtMostOne_TwoSet_ThrowsException()
    {
        IServiceConfiguration config = new AtMostOneTwoSetConfig();
        var exception = Assert.Throws<InvalidOperationException>(() => config.ValidateConstraintGroups());
        Assert.Contains("backend", exception.Message);
        Assert.Contains("AtMostOne", exception.Message);
    }

    #endregion

    #region AllOrNone Tests

    [Fact]
    public void ValidateConstraintGroups_AllOrNone_AllSet_NoException()
    {
        IServiceConfiguration config = new AllOrNoneAllSetConfig();
        var exception = Record.Exception(() => config.ValidateConstraintGroups());
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateConstraintGroups_AllOrNone_NoneSet_NoException()
    {
        IServiceConfiguration config = new AllOrNoneNoneSetConfig();
        var exception = Record.Exception(() => config.ValidateConstraintGroups());
        Assert.Null(exception);
    }

    [Fact]
    public void ValidateConstraintGroups_AllOrNone_PartialSet_ThrowsException()
    {
        IServiceConfiguration config = new AllOrNonePartialConfig();
        var exception = Assert.Throws<InvalidOperationException>(() => config.ValidateConstraintGroups());
        Assert.Contains("tls", exception.Message);
        Assert.Contains("AllOrNone", exception.Message);
    }

    #endregion

    #region Multiple Groups Tests

    [Fact]
    public void ValidateConstraintGroups_MultipleGroups_AllValid_NoException()
    {
        IServiceConfiguration config = new MultiGroupValidConfig();
        var exception = Record.Exception(() => config.ValidateConstraintGroups());
        Assert.Null(exception);
    }

    #endregion

    #region No Groups Tests

    [Fact]
    public void ValidateConstraintGroups_NoGroups_NoException()
    {
        IServiceConfiguration config = new NoGroupsConfig();
        var exception = Record.Exception(() => config.ValidateConstraintGroups());
        Assert.Null(exception);
    }

    #endregion

    #region Validate() Integration Tests

    [Fact]
    public void Validate_IncludesConstraintGroupValidation()
    {
        // Verify that Validate() calls ValidateConstraintGroups() by testing with invalid group config
        IServiceConfiguration config = new SumEqualsInvalidConfig();
        var exception = Assert.Throws<InvalidOperationException>(() => config.Validate());
        Assert.Contains("weights", exception.Message);
    }

    [Fact]
    public void Validate_ValidConfig_PassesAllChecks()
    {
        IServiceConfiguration config = new SumEqualsValidConfig();
        var exception = Record.Exception(() => config.Validate());
        Assert.Null(exception);
    }

    #endregion

    #region Attribute Tests

    [Fact]
    public void ConfigConstraintGroupAttribute_StoresGroupName()
    {
        var attr = new ConfigConstraintGroupAttribute("my-group");
        Assert.Equal("my-group", attr.GroupName);
    }

    [Fact]
    public void ConfigConstraintGroupAttribute_NullGroupName_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ConfigConstraintGroupAttribute(null!));
    }

    [Fact]
    public void ConfigConstraintGroupAttribute_EmptyGroupName_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ConfigConstraintGroupAttribute(""));
    }

    [Fact]
    public void ConfigConstraintGroupDefinitionAttribute_StoresProperties()
    {
        var attr = new ConfigConstraintGroupDefinitionAttribute("weights", ConstraintGroupType.SumEquals)
        {
            Value = 1.0,
            Tolerance = 0.001
        };

        Assert.Equal("weights", attr.GroupName);
        Assert.Equal(ConstraintGroupType.SumEquals, attr.Constraint);
        Assert.Equal(1.0, attr.Value);
        Assert.Equal(0.001, attr.Tolerance);
        Assert.True(attr.HasValue);
        Assert.True(attr.IsSumConstraint);
        Assert.False(attr.IsPresenceConstraint);
    }

    [Fact]
    public void ConfigConstraintGroupDefinitionAttribute_PresenceConstraint_NoValue()
    {
        var attr = new ConfigConstraintGroupDefinitionAttribute("provider", ConstraintGroupType.ExactlyOne);

        Assert.False(attr.HasValue);
        Assert.False(attr.IsSumConstraint);
        Assert.True(attr.IsPresenceConstraint);
    }

    [Fact]
    public void ConfigConstraintGroupDefinitionAttribute_DefaultTolerance()
    {
        var attr = new ConfigConstraintGroupDefinitionAttribute("test", ConstraintGroupType.SumEquals);
        Assert.Equal(ConfigConstraintGroupDefinitionAttribute.DefaultTolerance, attr.Tolerance);
        Assert.Equal(0.0001, attr.Tolerance);
    }

    [Theory]
    [InlineData(ConstraintGroupType.ExactlyOne, "exactly one must be set")]
    [InlineData(ConstraintGroupType.AtMostOne, "at most one may be set")]
    [InlineData(ConstraintGroupType.AllOrNone, "all must be set or all must be absent")]
    public void GetConstraintDescription_PresenceConstraints_DescribesCorrectly(ConstraintGroupType constraint, string expectedFragment)
    {
        var attr = new ConfigConstraintGroupDefinitionAttribute("test", constraint);
        Assert.Contains(expectedFragment, attr.GetConstraintDescription());
    }

    [Fact]
    public void GetConstraintDescription_SumEquals_IncludesValueAndTolerance()
    {
        var attr = new ConfigConstraintGroupDefinitionAttribute("test", ConstraintGroupType.SumEquals)
        {
            Value = 1.0,
            Tolerance = 0.01
        };
        var desc = attr.GetConstraintDescription();
        Assert.Contains("1", desc);
        Assert.Contains("0.01", desc);
    }

    #endregion
}
