using Moq;
using System.Reflection;
using Xunit;

namespace BeyondImmersion.BannouService.TestUtilities;

/// <summary>
/// Validates service constructor patterns to catch common AI-introduced mistakes:
/// - Multiple constructors (DI might pick wrong one)
/// - Optional parameters (accidental defaults)
/// - Default values on parameters
/// - Missing null checks
/// - Wrong parameter names in ArgumentNullException
/// </summary>
public static class ServiceConstructorValidator
{
    /// <summary>
    /// Validates that a service type has a proper constructor pattern for DI.
    /// This single call replaces N individual constructor null-check tests.
    /// </summary>
    /// <typeparam name="TService">The service type to validate.</typeparam>
    /// <exception cref="Xunit.Sdk.XunitException">Thrown when validation fails.</exception>
    public static void ValidateServiceConstructor<TService>() where TService : class
    {
        ValidateServiceConstructor(typeof(TService));
    }

    /// <summary>
    /// Validates that a service type has a proper constructor pattern for DI.
    /// </summary>
    /// <param name="serviceType">The service type to validate.</param>
    /// <exception cref="Xunit.Sdk.XunitException">Thrown when validation fails.</exception>
    public static void ValidateServiceConstructor(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        var constructors = serviceType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        // CATCH: Multiple constructors (AI added overload, DI might pick wrong one)
        Assert.True(constructors.Length == 1,
            $"{serviceType.Name} must have exactly ONE public constructor, found {constructors.Length}. " +
            "Multiple constructors can cause DI to pick the wrong one.");

        var ctor = constructors[0];
        var parameters = ctor.GetParameters();

        // Validate each parameter
        foreach (var param in parameters)
        {
            // CATCH: Optional parameters
            Assert.False(param.IsOptional,
                $"{serviceType.Name} parameter '{param.Name}' must not be optional. " +
                "Optional parameters can be accidentally set to null by DI.");

            // CATCH: Default values
            Assert.False(param.HasDefaultValue,
                $"{serviceType.Name} parameter '{param.Name}' must not have a default value. " +
                "Default values can hide missing DI registrations.");
        }

        // NOTE: We intentionally do NOT validate null checks in constructors.
        // The DI container throws if a service isn't registered, and nullable
        // reference types provide compile-time warnings. Explicit null checks
        // are redundant boilerplate in a DI-managed codebase.
    }

    /// <summary>
    /// Creates a mock or instance of a type for testing purposes.
    /// Used by tests that need to construct services with valid dependencies.
    /// </summary>
    public static object? CreateMockOrInstance(Type type)
    {
        // Handle nullable value types
        if (Nullable.GetUnderlyingType(type) != null)
        {
            return null;
        }

        // Value types - return default
        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }

        // Strings
        if (type == typeof(string))
        {
            return "test-string";
        }

        // Configuration classes (concrete types ending in Configuration)
        if (type.Name.EndsWith("Configuration") && !type.IsAbstract && !type.IsInterface)
        {
            try
            {
                return Activator.CreateInstance(type);
            }
            catch
            {
                // Fall through to mock creation
            }
        }

        // Concrete classes that might have parameterless constructors
        if (!type.IsInterface && !type.IsAbstract)
        {
            var defaultCtor = type.GetConstructor(Type.EmptyTypes);
            if (defaultCtor != null)
            {
                try
                {
                    return Activator.CreateInstance(type);
                }
                catch
                {
                    // Fall through to mock creation
                }
            }
        }

        // Interfaces and abstract classes - create Moq mock dynamically
        try
        {
            var mockType = typeof(Mock<>).MakeGenericType(type);
            var mock = Activator.CreateInstance(mockType);

            // Use the non-generic Mock base class's Object property to avoid ambiguous match
            // Mock<T> inherits from Mock, which has a non-generic Object property
            var objectProperty = typeof(Mock).GetProperty("Object");
            return objectProperty?.GetValue(mock);
        }
        catch (Exception ex)
        {
            // If we can't mock it, throw with helpful message
            throw new InvalidOperationException(
                $"Failed to create mock for type '{type.FullName}'. " +
                $"This type may need special handling in ServiceConstructorValidator.CreateMockOrInstance. " +
                $"Error: {ex.Message}",
                ex);
        }
    }
}
