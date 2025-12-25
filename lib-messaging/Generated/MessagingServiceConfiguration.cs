using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Messaging;

/// <summary>
/// Configuration class for Messaging service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(MessagingService))]
public class MessagingServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// RabbitMQ server hostname
    /// Environment variable: MESSAGING_RABBITMQ_HOST
    /// </summary>
    public string RabbitMQHost { get; set; } = "rabbitmq";

    /// <summary>
    /// RabbitMQ server port
    /// Environment variable: MESSAGING_RABBITMQ_PORT
    /// </summary>
    public int RabbitMQPort { get; set; } = 5672;

    /// <summary>
    /// RabbitMQ username
    /// Environment variable: MESSAGING_RABBITMQ_USERNAME
    /// </summary>
    public string RabbitMQUsername { get; set; } = "guest";

    /// <summary>
    /// RabbitMQ password
    /// Environment variable: MESSAGING_RABBITMQ_PASSWORD
    /// </summary>
    public string RabbitMQPassword { get; set; } = "guest";

    /// <summary>
    /// RabbitMQ virtual host
    /// Environment variable: MESSAGING_RABBITMQ_VHOST
    /// </summary>
    public string RabbitMQVirtualHost { get; set; } = "/";

    /// <summary>
    /// Default exchange name for publishing
    /// Environment variable: MESSAGING_DEFAULT_EXCHANGE
    /// </summary>
    public string DefaultExchange { get; set; } = "bannou";

    /// <summary>
    /// Enable RabbitMQ publisher confirms for reliability
    /// Environment variable: MESSAGING_ENABLE_CONFIRMS
    /// </summary>
    public bool EnablePublisherConfirms { get; set; } = true;

    /// <summary>
    /// Number of connection retry attempts
    /// Environment variable: MESSAGING_CONNECTION_RETRY_COUNT
    /// </summary>
    public int ConnectionRetryCount { get; set; } = 5;

    /// <summary>
    /// Delay between connection retry attempts in milliseconds
    /// Environment variable: MESSAGING_CONNECTION_RETRY_DELAY_MS
    /// </summary>
    public int ConnectionRetryDelayMs { get; set; } = 1000;

    /// <summary>
    /// Default prefetch count for subscriptions
    /// Environment variable: MESSAGING_DEFAULT_PREFETCH_COUNT
    /// </summary>
    public int DefaultPrefetchCount { get; set; } = 10;

    /// <summary>
    /// Default auto-acknowledge setting for subscriptions
    /// Environment variable: MESSAGING_DEFAULT_AUTO_ACK
    /// </summary>
    public bool DefaultAutoAck { get; set; } = false;

    /// <summary>
    /// Dead letter exchange name for failed messages
    /// Environment variable: MESSAGING_DEAD_LETTER_EXCHANGE
    /// </summary>
    public string DeadLetterExchange { get; set; } = "bannou-dlx";

    /// <summary>
    /// Maximum retry attempts before dead-lettering
    /// Environment variable: MESSAGING_RETRY_MAX_ATTEMPTS
    /// </summary>
    public int RetryMaxAttempts { get; set; } = 3;

    /// <summary>
    /// Delay between retry attempts in milliseconds
    /// Environment variable: MESSAGING_RETRY_DELAY_MS
    /// </summary>
    public int RetryDelayMs { get; set; } = 5000;

    /// <summary>
    /// Use MassTransit wrapper (true) or direct RabbitMQ.Client (false)
    /// Environment variable: MESSAGING_USE_MASSTRANSIT
    /// </summary>
    public bool UseMassTransit { get; set; } = true;

    /// <summary>
    /// Enable message bus metrics collection
    /// Environment variable: MESSAGING_ENABLE_METRICS
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Enable distributed tracing for messages
    /// Environment variable: MESSAGING_ENABLE_TRACING
    /// </summary>
    public bool EnableTracing { get; set; } = true;

}
