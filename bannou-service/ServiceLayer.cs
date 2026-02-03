namespace BeyondImmersion.BannouService;

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
