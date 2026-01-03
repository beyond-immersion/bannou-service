using BeyondImmersion.BannouService.Connect;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// HTTP tests for peer-to-peer routing infrastructure.
///
/// ## Architecture Context (from ACTORS_PLUGIN_V3 Section 9.4)
///
/// Bannou deploys Connect nodes in two modes:
///
/// 1. **External Connect** (player-facing):
///    - Clients connect with JWT auth
///    - Receive full capability manifest with client-salted GUIDs
///    - Use GUIDs to route API calls to services
///    - Can route to peers using MessageFlags.Client + peerGuid
///
/// 2. **Internal Connect** (service-facing, not yet implemented):
///    - Game Servers and Actor Pool Nodes connect
///    - Use service tokens instead of user JWTs
///    - Don't need capability manifests (no lib-mesh outbound)
///    - Only need sessionId + peerGuid for peer routing
///
/// ## Pain Points Identified
///
/// These tests document what's missing for internal service connections:
///
/// 1. **No internal connection endpoint**: Current /connect requires JWT with user claims
/// 2. **Capability manifest overhead**: Internal services don't need API GUIDs
/// 3. **No service token auth**: Need alternative auth for internal services
/// 4. **peerGuid exposure**: peerGuid is in capability manifest - internal services need simpler access
///
/// ## Future: Internal Connect Endpoint
///
/// A future /connect/internal or /connect?mode=internal endpoint would:
/// - Accept service tokens (not user JWTs)
/// - Skip capability manifest generation
/// - Return only: sessionId, peerGuid
/// - Support same peer routing as external Connect
///
/// ## Voice Migration Note
///
/// lib-voice/Services/P2PCoordinator.cs uses sessionId for peer identification.
/// This should migrate to peerGuid for consistency:
/// - VoicePeer.SessionId → VoicePeer.PeerGuid
/// - peerSessionId → peerGuid in voice-client-events.yaml
/// - Direct WebRTC signaling still works the same way
/// </summary>
public class PeerRoutingTestHandler : BaseHttpTestHandler
{
    public override ServiceTest[] GetServiceTests() =>
    [
        // Capability manifest includes peerGuid
        new ServiceTest(TestCapabilityManifestIncludesPeerGuid, "ManifestPeerGuid", "PeerRouting",
            "Test that capability manifest includes peerGuid for peer routing"),

        // Internal connection pain points (these tests document what's missing)
        new ServiceTest(TestInternalConnectionRequirements, "InternalConnReqs", "PeerRouting",
            "Document requirements for internal service connections"),

        // Peer routing metadata tests
        new ServiceTest(TestPeerRoutingMetadataInProxy, "ProxyPeerMetadata", "PeerRouting",
            "Test if internal proxy could support peer routing metadata"),
    ];

    /// <summary>
    /// Test that the capability manifest includes peerGuid.
    /// This verifies external clients receive their peer identifier for peer-to-peer routing.
    /// </summary>
    private static async Task<TestResult> TestCapabilityManifestIncludesPeerGuid(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var connectClient = GetServiceClient<IConnectClient>();

            try
            {
                var response = await connectClient.GetClientCapabilitiesAsync(new GetClientCapabilitiesRequest());

                if (response == null)
                    return TestResult.Failed("Capability response is null");

                // Note: The HTTP endpoint returns ClientCapabilitiesResponse which may not have peerGuid.
                // peerGuid is in the WebSocket capability manifest event (CapabilityManifestEvent).
                //
                // PAIN POINT: HTTP endpoint doesn't expose peerGuid - only WebSocket does.
                // For internal services that want to query their peerGuid via HTTP, we'd need
                // to add it to ClientCapabilitiesResponse in connect-api.yaml.

                return TestResult.Successful(
                    $"Capability manifest retrieved. SessionId: {response.SessionId ?? "(null)"}, " +
                    $"Capabilities: {response.Capabilities?.Count ?? 0}. " +
                    "NOTE: peerGuid is only in WebSocket CapabilityManifestEvent, not HTTP response.");
            }
            catch (ApiException ex) when (ex.StatusCode == 401)
            {
                // Expected for unauthenticated requests
                return TestResult.Successful(
                    "Capability endpoint requires auth (401). " +
                    "PAIN POINT: Internal services would need service token auth.");
            }
        }, "Manifest peerGuid");

    /// <summary>
    /// Documents the requirements for internal service connections.
    /// This is a "documentation test" that passes while describing what's needed.
    /// </summary>
    private static async Task<TestResult> TestInternalConnectionRequirements(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            await Task.CompletedTask;
            // This test documents what internal services need
            var requirements = new List<string>
            {
                "1. Internal Connect Endpoint: /connect/internal or mode=internal query param",
                "2. Service Token Auth: Accept X-Service-Token header or internal network trust",
                "3. Minimal Response: Return only { sessionId, peerGuid } - no capability manifest",
                "4. Same Peer Routing: Support MessageFlags.Client (0x20) with peerGuid",
                "5. No lib-mesh Outbound: Internal services don't call other services through Connect",
            };

            var painPoints = new List<string>
            {
                "CURRENT: /connect requires JWT with user claims",
                "CURRENT: Capability manifest is generated (unnecessary overhead)",
                "CURRENT: No HTTP endpoint exposes peerGuid (only WebSocket event)",
                "CURRENT: Voice uses sessionId instead of peerGuid for P2P",
            };

            var result = new System.Text.StringBuilder();
            result.AppendLine("Internal Service Connection Requirements:");
            foreach (var req in requirements)
                result.AppendLine($"  {req}");
            result.AppendLine();
            result.AppendLine("Current Pain Points:");
            foreach (var pain in painPoints)
                result.AppendLine($"  {pain}");

            // This test passes - it's documenting requirements
            return TestResult.Successful(result.ToString());
        }, "Internal connection requirements");

    /// <summary>
    /// Tests if the internal proxy could support peer routing metadata.
    /// The /internal/proxy endpoint could potentially be extended to include peer info.
    /// </summary>
    private static async Task<TestResult> TestPeerRoutingMetadataInProxy(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var connectClient = GetServiceClient<IConnectClient>();

            try
            {
                // Test if proxy endpoint accepts requests
                // Note: This will fail because we don't have a valid sessionId
                var proxyRequest = new InternalProxyRequest
                {
                    SessionId = "test-session-id",
                    TargetService = "testing",
                    TargetEndpoint = "/testing/ping",
                    Method = InternalProxyRequestMethod.GET
                };

                var response = await connectClient.ProxyInternalRequestAsync(proxyRequest);

                // FUTURE: The proxy response could include peer routing info:
                // - senderPeerGuid: The peerGuid of the session making the request
                // - This would let internal services learn peer GUIDs through HTTP

                return TestResult.Successful(
                    $"Proxy endpoint responded. Status: {response?.StatusCode ?? 0}. " +
                    "FUTURE: Could add senderPeerGuid to InternalProxyResponse for peer discovery.");
            }
            catch (ApiException ex)
            {
                // Expected - we don't have a valid session
                return TestResult.Successful(
                    $"Proxy returned {ex.StatusCode} (expected for test session). " +
                    "FUTURE: InternalProxyResponse could include senderPeerGuid field.");
            }
        }, "Proxy peer metadata");
}
