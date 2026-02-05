namespace BeyondImmersion.BannouService;

/// <summary>
/// Application running states for service lifecycle management.
/// </summary>
public enum AppRunningStates
{
    /// <summary>
    /// Service is starting up and initializing.
    /// </summary>
    Starting,
    /// <summary>
    /// Service is running and ready to process requests.
    /// </summary>
    Running,
    /// <summary>
    /// Service has stopped and is no longer processing requests.
    /// </summary>
    Stopped
}

/// <summary>
/// Enumeration for common/supported HTTP methods.
/// (Not sure why there isn't an enum for this in .NET)
/// </summary>
public enum HttpMethodTypes
{
    /// <summary>
    /// HTTP GET method for retrieving data.
    /// </summary>
    GET = 0,
    /// <summary>
    /// HTTP POST method for creating resources.
    /// </summary>
    POST,
    /// <summary>
    /// HTTP PUT method for updating resources.
    /// </summary>
    PUT,
    /// <summary>
    /// HTTP DELETE method for removing resources.
    /// </summary>
    DELETE,

    // unsupported yet
    /// <summary>
    /// HTTP HEAD method (unsupported).
    /// </summary>
    HEAD,
    /// <summary>
    /// HTTP PATCH method (unsupported).
    /// </summary>
    PATCH,
    /// <summary>
    /// HTTP OPTIONS method (unsupported).
    /// </summary>
    OPTIONS
}

/// <summary>
/// An enumeration of possible API response codes returned by this service.
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPORTANT:</b> Use this enum (<c>BeyondImmersion.BannouService.StatusCodes</c>),
/// NOT <c>Microsoft.AspNetCore.Http.StatusCodes</c> which is a static class with int constants.
/// </para>
/// <para>
/// All service methods return <c>(StatusCodes, TResponse?)</c> tuples.
/// See TENETS.md T8: Return Pattern for usage requirements.
/// </para>
/// <para>
/// <b>WARNING - DO NOT ADD NEW STATUS CODES WITHOUT EXPLICIT APPROVAL.</b>
/// These codes are hand-chosen per TENET T8 to minimize client complexity.
/// Adding new codes requires updating: Controller.liquid template, ErrorResponses.cs,
/// client SDK ResponseCodes, and all consuming code. If you think you need a new
/// status code, the answer is almost always "use an existing code with appropriate
/// payload content to indicate the specific condition."
/// </para>
/// </remarks>
public enum StatusCodes
{
    /// <summary>
    /// Request succeeded (200).
    /// </summary>
    OK = 200,
    /// <summary>
    /// Bad request due to invalid input (400).
    /// </summary>
    BadRequest = 400,
    /// <summary>
    /// Authentication required (401).
    /// </summary>
    Unauthorized = 401,
    /// <summary>
    /// Access forbidden (403).
    /// </summary>
    Forbidden = 403,
    /// <summary>
    /// Requested resource not found (404).
    /// </summary>
    NotFound = 404,
    /// <summary>
    /// Request conflicts with current resource state (409).
    /// </summary>
    Conflict = 409,
    /// <summary>
    /// Internal server error (500).
    /// </summary>
    InternalServerError = 500,
    /// <summary>
    /// Method not implemented (501).
    /// Used for stub services that are planned but not yet implemented.
    /// </summary>
    NotImplemented = 501,
    /// <summary>
    /// Service temporarily unavailable (503).
    /// Used when a required dependency or subsystem is not available.
    /// </summary>
    ServiceUnavailable = 503
}

/// <summary>
/// Defines the service hierarchy layers per SERVICE_HIERARCHY.md.
/// Lower values load first. Dependencies may only flow to lower layers.
/// </summary>
/// <remarks>
/// <para>
/// The service hierarchy enables:
/// <list type="bullet">
///   <item>Deterministic plugin load ordering based on dependency layers</item>
///   <item>Safe constructor injection for services depending on lower layers</item>
///   <item>Compile-time and runtime validation of dependency rules</item>
///   <item>Optional deployment configurations (L3/L4 can be disabled)</item>
/// </list>
/// </para>
/// <para>
/// <b>SCHEMA-FIRST:</b> Set via <c>x-service-layer</c> in the service's API schema.
/// The code generation scripts read this value and apply it to the generated
/// <see cref="Attributes.BannouServiceAttribute"/>.
/// </para>
/// </remarks>
public enum ServiceLayer
{
    /// <summary>
    /// L0: Infrastructure plugins (state, messaging, mesh, telemetry).
    /// Loaded first. No service-layer dependencies.
    /// Required for any deployment (except telemetry which is optional).
    /// </summary>
    Infrastructure = 0,

    /// <summary>
    /// L1: App Foundation services (account, auth, connect, permission, contract, resource).
    /// Required for ANY deployment. Depends only on L0.
    /// Missing L1 service = startup failure.
    /// </summary>
    AppFoundation = 100,

    /// <summary>
    /// L2: Game Foundation services (realm, character, species, location, currency, item, inventory, etc.).
    /// Required for game deployments. Depends on L0 and L1.
    /// Missing L2 service when L4 is enabled = startup failure.
    /// </summary>
    GameFoundation = 200,

    /// <summary>
    /// L3: App Features (asset, orchestrator, documentation, website).
    /// Optional non-game capabilities. Depends on L0, L1, and other L3 (with graceful degradation).
    /// Missing L3 service = graceful degradation, not crash.
    /// </summary>
    AppFeatures = 300,

    /// <summary>
    /// L4: Game Features (actor, behavior, matchmaking, analytics, achievement, etc.).
    /// Optional game-specific capabilities. Depends on L0, L1, L2, L3*, L4* (* = graceful degradation).
    /// Missing L4 service = graceful degradation for L4 consumers.
    /// </summary>
    GameFeatures = 400,

    /// <summary>
    /// L5: Extensions (third-party plugins, internal meta-services).
    /// Loaded last. Can depend on all core layers (L0-L4).
    /// Use for plugins that need the full Bannou stack available.
    /// </summary>
    Extensions = 500
}
