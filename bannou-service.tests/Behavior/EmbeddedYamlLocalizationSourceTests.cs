// =============================================================================
// EmbeddedYamlLocalizationSource Tests
// Verifies the abstract base behavior using embedded YAML fixtures bundled
// into the test assembly under
// BeyondImmersion.BannouService.Tests.Behavior.Fixtures.strings.{locale}.yaml
// =============================================================================

using System.Reflection;
using BeyondImmersion.BannouService.Behavior;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BeyondImmersion.BannouService.Tests.Behavior;

/// <summary>
/// Tests for <see cref="EmbeddedYamlLocalizationSource"/>.
/// Uses the test assembly itself as the resource source via the
/// <see cref="TestEmbeddedSource"/> subclass below.
/// </summary>
public sealed class EmbeddedYamlLocalizationSourceTests
{
    private TestEmbeddedSource CreateSource(string? prefixOverride = null)
    {
        return new TestEmbeddedSource(
            NullLogger<TestEmbeddedSource>.Instance,
            new NullTelemetryProvider(),
            prefixOverride);
    }

    [Fact]
    public void GetText_LoadsFlattenedKey_FromEmbeddedYaml()
    {
        var source = CreateSource();

        var startText = source.GetText("ui.menu.start", "en");
        var quitText = source.GetText("ui.menu.quit", "en");
        var greetingText = source.GetText("dialogue.greeting", "en");

        Assert.Equal("Start Game", startText);
        Assert.Equal("Quit", quitText);
        Assert.Equal("Hello!", greetingText);
    }

    [Fact]
    public void GetText_LoadsMultipleLocales_FromSeparateEmbeddedFiles()
    {
        var source = CreateSource();

        var en = source.GetText("dialogue.greeting", "en");
        var fr = source.GetText("dialogue.greeting", "fr");

        Assert.Equal("Hello!", en);
        Assert.Equal("Bonjour!", fr);
    }

    [Fact]
    public void GetText_DeeplyNestedKey_FlattensWithDotNotation()
    {
        var source = CreateSource();

        // items.direwolf.name in YAML → flattened key "items.direwolf.name"
        var directwolfName = source.GetText("items.direwolf.name", "en");

        Assert.Equal("Direwolf", directwolfName);
    }

    [Fact]
    public void GetText_MissingKey_ReturnsNull()
    {
        var source = CreateSource();

        var missing = source.GetText("ui.does-not-exist", "en");

        Assert.Null(missing);
    }

    [Fact]
    public void GetText_MissingLocale_ReturnsNull()
    {
        var source = CreateSource();

        var notLoaded = source.GetText("dialogue.greeting", "ja");

        Assert.Null(notLoaded);
    }

    [Fact]
    public void GetText_PrefixDoesNotMatchAssembly_ReturnsNull()
    {
        // Subclass with a prefix that won't match any embedded resource.
        var source = CreateSource(prefixOverride: "BeyondImmersion.NonExistent.");

        var anything = source.GetText("ui.menu.start", "en");

        Assert.Null(anything);
    }

    [Fact]
    public void SupportedLocales_DiscoversAllEmbeddedLocales()
    {
        var source = CreateSource();

        var locales = source.SupportedLocales;

        Assert.Contains("en", locales);
        Assert.Contains("fr", locales);
    }

    [Fact]
    public async Task ReloadAsync_ClearsCachedData_NextGetTextRefetches()
    {
        var source = CreateSource();

        // Trigger initial load
        var first = source.GetText("dialogue.greeting", "en");
        Assert.Equal("Hello!", first);

        // Reload — drops cache; SupportedLocales should re-discover
        await source.ReloadAsync(TestContext.Current.CancellationToken);
        var locales = source.SupportedLocales;

        Assert.Contains("en", locales);
        Assert.Contains("fr", locales);

        // Subsequent GetText still works (re-loads from embedded resource)
        var second = source.GetText("dialogue.greeting", "en");
        Assert.Equal("Hello!", second);
    }

    [Fact]
    public void GetText_EmptyKey_ThrowsArgumentException()
    {
        var source = CreateSource();

        // ArgumentException.ThrowIfNullOrEmpty throws plain ArgumentException
        // for empty (vs ArgumentNullException for null).
        Assert.Throws<ArgumentException>(() => source.GetText(string.Empty, "en"));
    }

    [Fact]
    public void GetText_EmptyLocale_ThrowsArgumentException()
    {
        var source = CreateSource();

        Assert.Throws<ArgumentException>(() => source.GetText("ui.menu.start", string.Empty));
    }

    [Fact]
    public void Name_AndPriority_ReturnFromSubclass()
    {
        var source = CreateSource();

        Assert.Equal("test-embedded", source.Name);
        Assert.Equal(42, source.Priority);
    }

    /// <summary>
    /// Test subclass that points at this test assembly's embedded YAML fixtures.
    /// Resources are bundled as
    /// <c>BeyondImmersion.BannouService.Tests.Behavior.Fixtures.strings.{locale}.yaml</c>.
    /// </summary>
    private sealed class TestEmbeddedSource : EmbeddedYamlLocalizationSource
    {
        private readonly string _prefix;

        public TestEmbeddedSource(
            ILogger logger,
            ITelemetryProvider telemetryProvider,
            string? prefixOverride)
            : base(logger, telemetryProvider)
        {
            _prefix = prefixOverride
                ?? "BeyondImmersion.BannouService.Tests.Behavior.Fixtures.";
        }

        public override string Name => "test-embedded";
        public override int Priority => 42;
        protected override Assembly ResourceAssembly => typeof(TestEmbeddedSource).Assembly;
        protected override string ResourcePrefix => _prefix;
    }
}
