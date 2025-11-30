using Dapr;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace BeyondImmersion.BannouService.Permissions;

/// <summary>
/// Controller for handling Dapr pubsub events related to permissions.
/// This controller is separate from the generated PermissionsController to allow
/// Dapr subscription discovery while maintaining schema-first architecture.
/// </summary>
[ApiController]
[Route("[controller]")]
public class PermissionsEventsController : ControllerBase
{
    private readonly ILogger<PermissionsEventsController> _logger;
    private readonly IPermissionsService _permissionsService;

    public PermissionsEventsController(
        ILogger<PermissionsEventsController> logger,
        IPermissionsService permissionsService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _permissionsService = permissionsService ?? throw new ArgumentNullException(nameof(permissionsService));
    }

    /// <summary>
    /// Handle service registration events from other services.
    /// Called by Dapr when a message is published to "permissions.service-registered" topic.
    /// </summary>
    [Topic("bannou-pubsub", "permissions.service-registered")]
    [HttpPost("handle-service-registration")]
    public async Task<IActionResult> HandleServiceRegistrationAsync()
    {
        try
        {
            // Read the request body directly - Dapr pubsub delivers CloudEvents format
            string rawBody;
            using (var reader = new StreamReader(Request.Body, leaveOpen: true))
            {
                rawBody = await reader.ReadToEndAsync();
            }

            _logger.LogInformation("Received service registration event - raw body length: {Length}, content-type: {ContentType}",
                rawBody?.Length ?? 0, Request.ContentType);
            _logger.LogInformation("Raw body (first 500 chars): {RawBody}", rawBody?.Substring(0, Math.Min(500, rawBody?.Length ?? 0)));

            if (string.IsNullOrWhiteSpace(rawBody))
            {
                _logger.LogError("Received empty request body");
                return BadRequest(new { error = "Empty request body" });
            }

            // Parse the JSON manually
            JsonElement serviceEventObj;
            try
            {
                using var document = JsonDocument.Parse(rawBody);
                serviceEventObj = document.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse request body as JSON: {Body}", rawBody);
                return BadRequest(new { error = $"Invalid JSON: {ex.Message}" });
            }

            _logger.LogInformation("Parsed JSON element kind: {Kind}", serviceEventObj.ValueKind);

            // If the event is wrapped in CloudEvents format, extract the data property
            JsonElement actualEventData = serviceEventObj;
            if (serviceEventObj.ValueKind == JsonValueKind.Object && serviceEventObj.TryGetProperty("data", out var dataElement))
            {
                _logger.LogInformation("CloudEvents format detected, extracting data property. Data kind: {DataKind}", dataElement.ValueKind);
                actualEventData = dataElement;
            }

            // Log the actual event data we're parsing
            _logger.LogInformation("Parsing event data with kind: {Kind}", actualEventData.ValueKind);

            // Validate the event data
            if (actualEventData.ValueKind != JsonValueKind.Object)
            {
                _logger.LogError("Event data is not a JSON object. Kind: {Kind}", actualEventData.ValueKind);
                return BadRequest(new { error = $"Invalid event data - expected object, got {actualEventData.ValueKind}" });
            }

            // Parse the ServiceRegistrationEvent with explicit validation
            if (!actualEventData.TryGetProperty("serviceId", out var serviceIdElement))
            {
                _logger.LogError("Missing 'serviceId' property in event data");
                return BadRequest(new { error = "Missing required property: serviceId" });
            }
            var serviceId = serviceIdElement.GetString();

            if (!actualEventData.TryGetProperty("version", out var versionElement))
            {
                _logger.LogError("Missing 'version' property in event data");
                return BadRequest(new { error = "Missing required property: version" });
            }
            var version = versionElement.GetString();

            if (!actualEventData.TryGetProperty("endpoints", out var endpoints))
            {
                _logger.LogError("Missing 'endpoints' property in event data");
                return BadRequest(new { error = "Missing required property: endpoints" });
            }

            _logger.LogInformation("Processing service registration for {ServiceId} version {Version} with {EndpointCount} endpoints",
                serviceId, version, endpoints.GetArrayLength());

            // Build State -> Role -> Methods mapping from endpoint permissions
            var permissionMatrix = new Dictionary<string, StatePermissions>();

            foreach (var endpointElement in endpoints.EnumerateArray())
            {
                var path = endpointElement.GetProperty("path").GetString();
                var method = endpointElement.GetProperty("method").GetString();
                var permissions = endpointElement.GetProperty("permissions");

                var methodSignature = $"{method}:{path}";

                // Process each permission requirement for this endpoint
                foreach (var permissionElement in permissions.EnumerateArray())
                {
                    var role = permissionElement.GetProperty("role").GetString();
                    var requiredStates = permissionElement.GetProperty("requiredStates");

                    // Extract all required states for this permission
                    var stateKeys = new List<string>();
                    foreach (var stateProperty in requiredStates.EnumerateObject())
                    {
                        var stateServiceId = stateProperty.Name;
                        var requiredState = stateProperty.Value.GetString();

                        // Create state key: either just the state name, or service:state if from another service
                        var stateKey = stateServiceId == serviceId ? requiredState : $"{stateServiceId}:{requiredState}";
                        if (!string.IsNullOrEmpty(stateKey))
                        {
                            stateKeys.Add(stateKey);
                        }
                    }

                    // If no specific states required, default to "authenticated"
                    if (stateKeys.Count == 0)
                    {
                        stateKeys.Add("authenticated");
                    }

                    // Add method to each required state/role combination
                    foreach (var stateKey in stateKeys)
                    {
                        if (!permissionMatrix.ContainsKey(stateKey))
                        {
                            permissionMatrix[stateKey] = new StatePermissions();
                        }

                        if (!permissionMatrix[stateKey].ContainsKey(role ?? "user"))
                        {
                            permissionMatrix[stateKey][role ?? "user"] = new Collection<string>();
                        }

                        // Add the method if not already present
                        if (!permissionMatrix[stateKey][role ?? "user"].Contains(methodSignature))
                        {
                            permissionMatrix[stateKey][role ?? "user"].Add(methodSignature);
                        }
                    }
                }
            }

            // Create the ServicePermissionMatrix
            var servicePermissionMatrix = new ServicePermissionMatrix
            {
                ServiceId = serviceId ?? "",
                Version = version ?? "",
                Permissions = permissionMatrix
            };

            _logger.LogInformation("Built permission matrix for {ServiceId}: {StateCount} states, {MethodCount} total methods",
                serviceId, permissionMatrix.Count,
                permissionMatrix.Values.SelectMany(sp => sp.Values).SelectMany(methods => methods).Count());

            // Register the permissions using the service
            var result = await _permissionsService.RegisterServicePermissionsAsync(servicePermissionMatrix);

            if (result.Item1 == StatusCodes.OK)
            {
                _logger.LogInformation("Successfully registered permissions for service {ServiceId}", serviceId);
                return Ok();
            }
            else
            {
                _logger.LogError("Failed to register permissions for service {ServiceId}: {StatusCode}",
                    serviceId, result.Item1);
                return StatusCode((int)result.Item1);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling service registration event. Exception: {ExceptionMessage}",
                ex.Message);
            return StatusCode(500, new { error = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    /// <summary>
    /// Handle session state change events from other services.
    /// Called by Dapr when a message is published to "permissions.session-state-changed" topic.
    /// </summary>
    [Topic("bannou-pubsub", "permissions.session-state-changed")]
    [HttpPost("handle-session-state-change")]
    public async Task<IActionResult> HandleSessionStateChangeAsync()
    {
        try
        {
            // Read the request body directly - Dapr pubsub delivers CloudEvents format
            string rawBody;
            using (var reader = new StreamReader(Request.Body, leaveOpen: true))
            {
                rawBody = await reader.ReadToEndAsync();
            }

            _logger.LogInformation("Received session state change event - raw body length: {Length}, content-type: {ContentType}",
                rawBody?.Length ?? 0, Request.ContentType);
            _logger.LogInformation("Raw body (first 500 chars): {RawBody}", rawBody?.Substring(0, Math.Min(500, rawBody?.Length ?? 0)));

            if (string.IsNullOrWhiteSpace(rawBody))
            {
                _logger.LogError("Received empty request body");
                return BadRequest(new { error = "Empty request body" });
            }

            // Parse the JSON manually
            JsonElement stateEventObj;
            try
            {
                using var document = JsonDocument.Parse(rawBody);
                stateEventObj = document.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse request body as JSON: {Body}", rawBody);
                return BadRequest(new { error = $"Invalid JSON: {ex.Message}" });
            }

            _logger.LogInformation("Parsed JSON element kind: {Kind}", stateEventObj.ValueKind);

            // If the event is wrapped in CloudEvents format, extract the data property
            JsonElement actualEventData = stateEventObj;
            if (stateEventObj.ValueKind == JsonValueKind.Object && stateEventObj.TryGetProperty("data", out var dataElement))
            {
                _logger.LogInformation("CloudEvents format detected, extracting data property. Data kind: {DataKind}", dataElement.ValueKind);
                actualEventData = dataElement;
            }

            // Validate the event data
            if (actualEventData.ValueKind != JsonValueKind.Object)
            {
                _logger.LogError("Event data is not a JSON object. Kind: {Kind}", actualEventData.ValueKind);
                return BadRequest(new { error = $"Invalid event data - expected object, got {actualEventData.ValueKind}" });
            }

            // Parse the event data with explicit validation
            if (!actualEventData.TryGetProperty("sessionId", out var sessionIdElement))
            {
                _logger.LogError("Missing 'sessionId' property in event data");
                return BadRequest(new { error = "Missing required property: sessionId" });
            }
            var sessionId = sessionIdElement.GetString();

            if (!actualEventData.TryGetProperty("serviceId", out var serviceIdElement))
            {
                _logger.LogError("Missing 'serviceId' property in event data");
                return BadRequest(new { error = "Missing required property: serviceId" });
            }
            var serviceId = serviceIdElement.GetString();

            if (!actualEventData.TryGetProperty("newState", out var newStateElement))
            {
                _logger.LogError("Missing 'newState' property in event data");
                return BadRequest(new { error = "Missing required property: newState" });
            }
            var newState = newStateElement.GetString();

            var previousState = actualEventData.TryGetProperty("previousState", out var prevProp) ?
                prevProp.GetString() : null;

            _logger.LogInformation("Received session state change event for {SessionId}: {ServiceId} → {NewState}",
                sessionId, serviceId, newState);

            if (sessionId == null || serviceId == null || newState == null)
            {
                _logger.LogWarning("Invalid session state change event - missing required fields");
                return BadRequest("Missing required fields");
            }

            var stateUpdate = new SessionStateUpdate
            {
                SessionId = sessionId,
                ServiceId = serviceId,
                NewState = newState,
                PreviousState = previousState
            };

            await _permissionsService.UpdateSessionStateAsync(stateUpdate);

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling session state change event. Exception: {ExceptionMessage}",
                ex.Message);
            return StatusCode(500, new { error = ex.Message, stackTrace = ex.StackTrace });
        }
    }
}
