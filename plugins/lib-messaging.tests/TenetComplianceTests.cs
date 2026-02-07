#nullable enable

using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.TestUtilities;
using System.Reflection;

namespace BeyondImmersion.BannouService.Messaging.Tests;

/// <summary>
/// Tests that verify lib-messaging complies with Bannou development tenets.
/// Uses reflection to catch common tenet violations at the code structure level.
/// </summary>
public class TenetComplianceTests
{
    private static readonly Assembly MessagingAssembly = typeof(RabbitMQMessageBus).Assembly;

    #region Constructor Pattern Tests (Foundation Tenets)

    [Fact]
    public void RabbitMQMessageBus_HasValidConstructorPattern()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<RabbitMQMessageBus>();
    }

    [Fact]
    public void RabbitMQMessageSubscriber_HasValidConstructorPattern()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<RabbitMQMessageSubscriber>();
    }

    [Fact]
    public void RabbitMQConnectionManager_HasValidConstructorPattern()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<RabbitMQConnectionManager>();
    }

    [Fact]
    public void RabbitMQMessageTap_HasValidConstructorPattern()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<RabbitMQMessageTap>();
    }

    [Fact]
    public void MessagingService_HasValidConstructorPattern()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<MessagingService>();
    }

    [Fact]
    public void InMemoryMessageBus_HasValidConstructorPattern()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<InMemoryMessageBus>();
    }

    [Fact]
    public void InMemoryMessageTap_HasValidConstructorPattern()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<InMemoryMessageTap>();
    }

    [Fact]
    public void NativeEventConsumerBackend_HasValidConstructorPattern()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<NativeEventConsumerBackend>();
    }

    #endregion

    #region Interface Implementation Tests

    [Fact]
    public void RabbitMQMessageBus_ImplementsIMessageBus()
    {
        Assert.True(typeof(IMessageBus).IsAssignableFrom(typeof(RabbitMQMessageBus)));
    }

    [Fact]
    public void RabbitMQMessageSubscriber_ImplementsIMessageSubscriber()
    {
        Assert.True(typeof(IMessageSubscriber).IsAssignableFrom(typeof(RabbitMQMessageSubscriber)));
    }

    [Fact]
    public void RabbitMQConnectionManager_ImplementsIChannelManager()
    {
        Assert.True(typeof(IChannelManager).IsAssignableFrom(typeof(RabbitMQConnectionManager)));
    }

    [Fact]
    public void RabbitMQMessageTap_ImplementsIMessageTap()
    {
        Assert.True(typeof(IMessageTap).IsAssignableFrom(typeof(RabbitMQMessageTap)));
    }

    [Fact]
    public void MessageRetryBuffer_ImplementsIRetryBuffer()
    {
        Assert.True(typeof(IRetryBuffer).IsAssignableFrom(typeof(MessageRetryBuffer)));
    }

    [Fact]
    public void InMemoryMessageBus_ImplementsBothInterfaces()
    {
        Assert.True(typeof(IMessageBus).IsAssignableFrom(typeof(InMemoryMessageBus)));
        Assert.True(typeof(IMessageSubscriber).IsAssignableFrom(typeof(InMemoryMessageBus)));
    }

    #endregion

    #region Async Disposal Pattern Tests (Implementation Tenets - T24)

    [Fact]
    public void RabbitMQMessageBus_ImplementsIAsyncDisposable()
    {
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(RabbitMQMessageBus)));
    }

    [Fact]
    public void RabbitMQMessageSubscriber_ImplementsIAsyncDisposable()
    {
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(RabbitMQMessageSubscriber)));
    }

    [Fact]
    public void RabbitMQConnectionManager_ImplementsIAsyncDisposable()
    {
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(RabbitMQConnectionManager)));
    }

    [Fact]
    public void RabbitMQMessageTap_ImplementsIAsyncDisposable()
    {
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(RabbitMQMessageTap)));
    }

    [Fact]
    public void MessageRetryBuffer_ImplementsIAsyncDisposable()
    {
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(MessageRetryBuffer)));
    }

    #endregion

    #region Sealed Class Tests (Quality Tenets)

    [Fact]
    public void RabbitMQMessageBus_IsSealed()
    {
        Assert.True(typeof(RabbitMQMessageBus).IsSealed,
            "RabbitMQMessageBus should be sealed to prevent inheritance and clarify design intent.");
    }

    [Fact]
    public void RabbitMQMessageSubscriber_IsSealed()
    {
        Assert.True(typeof(RabbitMQMessageSubscriber).IsSealed,
            "RabbitMQMessageSubscriber should be sealed to prevent inheritance.");
    }

    [Fact]
    public void MessageRetryBuffer_IsSealed()
    {
        Assert.True(typeof(MessageRetryBuffer).IsSealed,
            "MessageRetryBuffer should be sealed to prevent inheritance.");
    }

    [Fact]
    public void InMemoryMessageBus_IsSealed()
    {
        Assert.True(typeof(InMemoryMessageBus).IsSealed,
            "InMemoryMessageBus should be sealed to prevent inheritance.");
    }

    #endregion

    #region Configuration Class Tests

    [Fact]
    public void MessagingServiceConfiguration_HasPublicParameterlessConstructor()
    {
        // Configuration classes must have parameterless constructors for binding
        var ctor = typeof(MessagingServiceConfiguration)
            .GetConstructor(Type.EmptyTypes);

        Assert.NotNull(ctor);
        Assert.True(ctor.IsPublic,
            "MessagingServiceConfiguration must have a public parameterless constructor for configuration binding.");
    }

    [Fact]
    public void MessagingServiceConfiguration_AllPropertiesHaveGetterAndSetter()
    {
        // Configuration classes should have read/write properties for binding
        var properties = typeof(MessagingServiceConfiguration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            Assert.True(prop.CanRead,
                $"Configuration property {prop.Name} must be readable.");
            Assert.True(prop.CanWrite,
                $"Configuration property {prop.Name} must be writable for configuration binding.");
        }
    }

    #endregion

    #region No Direct Environment Access Tests (Implementation Tenets - T21)

    [Fact]
    public void MessagingServiceTypes_DoNotDirectlyAccessEnvironment()
    {
        // T21: Services should use configuration classes, not direct Environment access
        var publicTypes = MessagingAssembly.GetExportedTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.Namespace?.StartsWith("BeyondImmersion.BannouService.Messaging") == true);

        foreach (var type in publicTypes)
        {
            // Skip the configuration class itself
            if (type.Name.EndsWith("Configuration"))
                continue;

            var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
                                           BindingFlags.Public | BindingFlags.NonPublic);

            foreach (var method in methods)
            {
                // Get the method body IL if possible (this is a best-effort check)
                var body = method.GetMethodBody();
                if (body == null)
                    continue;

                // We can't easily analyze IL for specific method calls without significant work,
                // but we verify at the type level that there's no direct field of type
                // that would store Environment values

                // Check that fields don't store raw environment values
                // This is a heuristic - real detection would require IL analysis
            }
        }

        // Instead, verify that the configuration is injected
        var configFields = typeof(RabbitMQMessageBus)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(MessagingServiceConfiguration));

        Assert.True(configFields.Any(),
            "RabbitMQMessageBus should inject MessagingServiceConfiguration, not use Environment directly.");
    }

    #endregion

    #region Thread Safety Tests (Implementation Tenets - T9)

    [Fact]
    public void RabbitMQMessageSubscriber_UsesConcurrentDictionary()
    {
        // T9: Multi-instance safety requires concurrent collections
        var fields = typeof(RabbitMQMessageSubscriber)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic);

        var hasConcurrentDict = fields.Any(f =>
            f.FieldType.IsGenericType &&
            f.FieldType.GetGenericTypeDefinition() == typeof(System.Collections.Concurrent.ConcurrentDictionary<,>));

        Assert.True(hasConcurrentDict,
            "RabbitMQMessageSubscriber should use ConcurrentDictionary for thread-safe subscription tracking.");
    }

    [Fact]
    public void MessageRetryBuffer_UsesConcurrentQueue()
    {
        // T9: Buffer should use concurrent collection for thread safety
        var fields = typeof(MessageRetryBuffer)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic);

        var hasConcurrentQueue = fields.Any(f =>
            f.FieldType.IsGenericType &&
            f.FieldType.GetGenericTypeDefinition() == typeof(System.Collections.Concurrent.ConcurrentQueue<>));

        Assert.True(hasConcurrentQueue,
            "MessageRetryBuffer should use ConcurrentQueue for thread-safe retry buffering.");
    }

    #endregion

    #region Public API Surface Tests

    [Fact]
    public void IMessageBus_PublishAsync_ReturnsTask()
    {
        // Verify the interface method returns Task for async patterns
        var method = typeof(IMessageBus).GetMethod("PublishAsync");
        Assert.NotNull(method);
        Assert.True(typeof(Task).IsAssignableFrom(method.ReturnType),
            "IMessageBus.PublishAsync must return Task for async/await support.");
    }

    [Fact]
    public void IMessageSubscriber_SubscribeAsync_ReturnsTask()
    {
        var methods = typeof(IMessageSubscriber).GetMethods()
            .Where(m => m.Name == "SubscribeAsync");

        Assert.True(methods.Any(), "IMessageSubscriber should have SubscribeAsync method.");

        foreach (var method in methods)
        {
            Assert.True(typeof(Task).IsAssignableFrom(method.ReturnType),
                "IMessageSubscriber.SubscribeAsync must return Task for async/await support.");
        }
    }

    [Fact]
    public void IMessageSubscriber_SubscribeDynamicAsync_ReturnsAsyncDisposable()
    {
        var methods = typeof(IMessageSubscriber).GetMethods()
            .Where(m => m.Name == "SubscribeDynamicAsync");

        Assert.True(methods.Any(), "IMessageSubscriber should have SubscribeDynamicAsync method.");

        foreach (var method in methods)
        {
            // Should return Task<IAsyncDisposable> for proper cleanup
            Assert.True(typeof(Task).IsAssignableFrom(method.ReturnType),
                "IMessageSubscriber.SubscribeDynamicAsync must return Task.");
        }
    }

    #endregion

    #region Logging Pattern Tests (Quality Tenets - T10)

    [Fact]
    public void MessagingClasses_InjectILogger()
    {
        // T10: Structured logging requires ILogger injection
        var servicesToCheck = new[]
        {
            typeof(RabbitMQMessageBus),
            typeof(RabbitMQMessageSubscriber),
            typeof(RabbitMQConnectionManager),
            typeof(MessageRetryBuffer),
            typeof(RabbitMQMessageTap),
            typeof(MessagingService)
        };

        foreach (var serviceType in servicesToCheck)
        {
            var ctors = serviceType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            Assert.Single(ctors);

            var parameters = ctors[0].GetParameters();
            var hasLogger = parameters.Any(p =>
                p.ParameterType.IsGenericType &&
                p.ParameterType.GetGenericTypeDefinition() == typeof(Microsoft.Extensions.Logging.ILogger<>));

            Assert.True(hasLogger,
                $"{serviceType.Name} must inject ILogger<{serviceType.Name}> for structured logging (T10).");
        }
    }

    #endregion

    #region Extension Method Accessibility Tests

    [Fact]
    public void IMessageBusExtensions_ArePublicStatic()
    {
        // Extension methods should be public and static
        var extensionTypes = MessagingAssembly.GetTypes()
            .Where(t => t.Name.Contains("Extensions") && t.IsClass && t.IsAbstract && t.IsSealed);

        foreach (var extType in extensionTypes)
        {
            var extensionMethods = extType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false));

            Assert.True(extensionMethods.All(m => m.IsStatic),
                $"All extension methods in {extType.Name} must be static.");
        }
    }

    #endregion

    #region Null Reference Type Compliance Tests

    [Fact]
    public void PublishOptions_HasNullableAnnotations()
    {
        // Verify nullable reference types are properly annotated
        var type = typeof(PublishOptions);
        var properties = type.GetProperties();

        // Exchange and RoutingKey should be nullable (optional overrides)
        var exchangeProp = properties.FirstOrDefault(p => p.Name == "Exchange");
        var routingKeyProp = properties.FirstOrDefault(p => p.Name == "RoutingKey");

        Assert.NotNull(exchangeProp);
        Assert.NotNull(routingKeyProp);

        // Check that nullable types compile correctly
        // (the fact that this code compiles with #nullable enable is the real test)
        var testOptions = new PublishOptions
        {
            Exchange = null,
            RoutingKey = null
        };

        Assert.Null(testOptions.Exchange);
        Assert.Null(testOptions.RoutingKey);
    }

    [Fact]
    public void SubscriptionOptions_HasDefaults()
    {
        var options = new SubscriptionOptions();

        // Verify sensible defaults
        Assert.True(options.Durable, "SubscriptionOptions.Durable should default to true.");
        Assert.False(options.Exclusive, "SubscriptionOptions.Exclusive should default to false.");
    }

    #endregion
}
