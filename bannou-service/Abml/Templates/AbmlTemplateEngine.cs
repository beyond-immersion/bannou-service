// ═══════════════════════════════════════════════════════════════════════════
// ABML Template Engine (Fluid-based Liquid Templates)
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.BehaviorCompiler.Exceptions;
using BeyondImmersion.BannouService.Abml.Exceptions;
using BeyondImmersion.BannouService.Abml.Expressions;
using Fluid;
using Fluid.Values;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Abml.Templates;

/// <summary>
/// Template engine for Liquid-style templates using Fluid.
/// </summary>
public sealed class AbmlTemplateEngine
{
    private readonly FluidParser _parser = new();
    private readonly ConcurrentDictionary<string, IFluidTemplate> _templateCache = new();
    private readonly TemplateOptions _options;
    private readonly int _maxCacheSize;

    /// <summary>Gets the number of cached templates.</summary>
    public int CacheCount => _templateCache.Count;

    /// <summary>
    /// Creates a new template engine.
    /// </summary>
    /// <param name="maxCacheSize">Maximum number of cached templates.</param>
    public AbmlTemplateEngine(int maxCacheSize = 1000)
    {
        _maxCacheSize = maxCacheSize;
        _options = new TemplateOptions();

        // Configure Fluid options
        _options.MemberAccessStrategy = new UnsafeMemberAccessStrategy();

        // Register custom filters
        RegisterFilters();
    }

    private void RegisterFilters()
    {
        // String filters
        _options.Filters.AddFilter("upper", (input, args, ctx) =>
            new StringValue(input.ToStringValue().ToUpperInvariant()));

        _options.Filters.AddFilter("lower", (input, args, ctx) =>
            new StringValue(input.ToStringValue().ToLowerInvariant()));

        _options.Filters.AddFilter("trim", (input, args, ctx) =>
            new StringValue(input.ToStringValue().Trim()));

        // Type checking filters
        _options.Filters.AddFilter("is_null", (input, args, ctx) =>
            BooleanValue.Create(input.IsNil()));

        _options.Filters.AddFilter("is_empty", (input, args, ctx) =>
        {
            if (input.IsNil()) return BooleanValue.True;
            var str = input.ToStringValue();
            return BooleanValue.Create(string.IsNullOrEmpty(str));
        });

        _options.Filters.AddFilter("type_of", (input, args, ctx) =>
        {
            if (input.IsNil()) return new StringValue("null");
            return input switch
            {
                BooleanValue => new StringValue("boolean"),
                NumberValue => new StringValue("number"),
                StringValue => new StringValue("string"),
                ArrayValue => new StringValue("array"),
                ObjectValue => new StringValue("object"),
                _ => new StringValue(input.GetType().Name.ToLowerInvariant())
            };
        });
    }

    /// <summary>
    /// Renders a template with the given variables.
    /// </summary>
    /// <param name="template">The template string.</param>
    /// <param name="scope">Variable scope for template rendering.</param>
    /// <returns>The rendered string.</returns>
    public string Render(string template, IVariableScope scope)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(scope);

        var fluidTemplate = GetOrParseTemplate(template);
        var context = CreateContext(scope);

        return fluidTemplate.Render(context);
    }

    /// <summary>
    /// Renders a template asynchronously.
    /// </summary>
    public async Task<string> RenderAsync(string template, IVariableScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(scope);

        var fluidTemplate = GetOrParseTemplate(template);
        var context = CreateContext(scope);

        return await fluidTemplate.RenderAsync(context);
    }

    /// <summary>
    /// Tries to render a template.
    /// </summary>
    public bool TryRender(string template, IVariableScope scope, out string? result)
    {
        try
        {
            result = Render(template, scope);
            return true;
        }
        catch
        {
            result = null;
            return false;
        }
    }

    /// <summary>
    /// Validates a template without rendering.
    /// </summary>
    public bool IsValid(string template)
    {
        try
        {
            GetOrParseTemplate(template);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Clears the template cache.
    /// </summary>
    public void ClearCache()
    {
        _templateCache.Clear();
    }

    private IFluidTemplate GetOrParseTemplate(string template)
    {
        if (_templateCache.TryGetValue(template, out var cached))
        {
            return cached;
        }

        if (!_parser.TryParse(template, out var parsed, out var error))
        {
            throw new AbmlCompilationException($"Template parse error: {error}");
        }

        // Evict oldest entries if cache is full (simple strategy)
        if (_templateCache.Count >= _maxCacheSize)
        {
            // Remove random entries (simple eviction)
            var keysToRemove = _templateCache.Keys.Take(_maxCacheSize / 10).ToList();
            foreach (var key in keysToRemove)
            {
                _templateCache.TryRemove(key, out _);
            }
        }

        _templateCache.TryAdd(template, parsed);
        return parsed;
    }

    private TemplateContext CreateContext(IVariableScope scope)
    {
        var context = new TemplateContext(_options);

        // Add variables from scope
        if (scope is VariableScope varScope)
        {
            foreach (var (name, value) in varScope.GetAllVariables())
            {
                context.SetValue(name, FluidValue.Create(value, _options));
            }
        }

        return context;
    }
}

/// <summary>
/// Extension methods for VariableScope to support template rendering.
/// </summary>
public static class VariableScopeExtensions
{
    /// <summary>
    /// Gets all variables as a dictionary.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, object?>> GetAllVariables(this VariableScope scope)
    {
        // Access the local variables via reflection or a dedicated method
        // For now, we'll return an empty enumerable - the actual implementation
        // would need access to the internal dictionary
        return scope.GetLocalVariables();
    }
}
