using BeyondImmersion.BannouService.Mapping;
using BeyondImmersion.BannouService.Testing;

namespace BeyondImmersion.BannouService.HttpTester.Tests;

/// <summary>
/// Test handler for mapping-related API endpoints using generated clients.
/// Tests the mapping service APIs directly via NSwag-generated MappingClient.
/// Covers authority management, publishing, queries, and affordance system.
/// </summary>
public class MappingTestHandler : BaseHttpTestHandler
{
    public override ServiceTest[] GetServiceTests() =>
    [
        // Channel Management
        new ServiceTest(TestCreateChannel, "CreateChannel", "Mapping", "Test channel creation and authority grant"),
        new ServiceTest(TestCreateChannelConflict, "CreateChannelConflict", "Mapping", "Test channel conflict when authority exists"),
        new ServiceTest(TestReleaseAuthority, "ReleaseAuthority", "Mapping", "Test authority release"),
        new ServiceTest(TestAuthorityHeartbeat, "AuthorityHeartbeat", "Mapping", "Test authority heartbeat extension"),

        // Publishing
        new ServiceTest(TestPublishMapUpdate, "PublishMapUpdate", "Mapping", "Test RPC map update publishing"),
        new ServiceTest(TestPublishObjectChanges, "PublishObjectChanges", "Mapping", "Test batch object changes publishing"),
        new ServiceTest(TestPublishWithInvalidToken, "PublishWithInvalidToken", "Mapping", "Test publish rejection with invalid authority"),
        new ServiceTest(TestRequestSnapshot, "RequestSnapshot", "Mapping", "Test snapshot request"),

        // Queries
        new ServiceTest(TestQueryPoint, "QueryPoint", "Mapping", "Test point query for nearby objects"),
        new ServiceTest(TestQueryBounds, "QueryBounds", "Mapping", "Test bounds query for objects in area"),
        new ServiceTest(TestQueryObjectsByType, "QueryObjectsByType", "Mapping", "Test type-based object query"),

        // Affordance System
        new ServiceTest(TestQueryAffordance, "QueryAffordance", "Mapping", "Test affordance query for scored locations"),
        new ServiceTest(TestQueryAffordanceWithActorCapabilities, "QueryAffordanceWithActorCapabilities", "Mapping", "Test affordance query with actor capabilities"),

        // Authoring APIs
        new ServiceTest(TestAuthoringCheckout, "AuthoringCheckout", "Mapping", "Test authoring checkout lock acquisition"),
        new ServiceTest(TestAuthoringConflict, "AuthoringConflict", "Mapping", "Test authoring checkout conflict"),
        new ServiceTest(TestAuthoringCommit, "AuthoringCommit", "Mapping", "Test authoring commit and release"),
        new ServiceTest(TestAuthoringRelease, "AuthoringRelease", "Mapping", "Test authoring release without commit"),
    ];

    #region Channel Management Tests

    private static async Task<TestResult> TestCreateChannel(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var mappingClient = GetServiceClient<IMappingClient>();
            var regionId = Guid.NewGuid();

            var request = new CreateChannelRequest
            {
                RegionId = regionId,
                Kind = MapKind.Terrain,
                NonAuthorityHandling = NonAuthorityHandlingMode.Reject_and_alert
            };

            var response = await mappingClient.CreateChannelAsync(request);

            if (response.ChannelId == Guid.Empty)
                return TestResult.Failed("Channel creation returned invalid channel ID");

            if (string.IsNullOrEmpty(response.AuthorityToken))
                return TestResult.Failed("Channel creation did not return authority token");

            if (!response.IngestTopic.StartsWith("map.ingest."))
                return TestResult.Failed($"Invalid ingest topic format: {response.IngestTopic}");

            if (response.ExpiresAt <= DateTimeOffset.UtcNow)
                return TestResult.Failed("Authority expiration is not in the future");

            return TestResult.Successful($"Channel created: ChannelId={response.ChannelId}, Kind={response.Kind}, IngestTopic={response.IngestTopic}");
        }, "Channel creation");

    private static async Task<TestResult> TestCreateChannelConflict(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var mappingClient = GetServiceClient<IMappingClient>();
            var regionId = Guid.NewGuid();

            // First create a channel
            var request = new CreateChannelRequest
            {
                RegionId = regionId,
                Kind = MapKind.Static_geometry,
                NonAuthorityHandling = NonAuthorityHandlingMode.Reject_and_alert
            };

            var firstResponse = await mappingClient.CreateChannelAsync(request);
            if (firstResponse.ChannelId == Guid.Empty)
                return TestResult.Failed("Initial channel creation failed");

            // Try to create same channel again - should conflict
            try
            {
                await mappingClient.CreateChannelAsync(request);
                return TestResult.Failed("Second channel creation should have returned Conflict");
            }
            catch (ApiException ex) when (ex.StatusCode == 409)
            {
                return TestResult.Successful($"Correctly received Conflict for duplicate channel: {regionId}+{request.Kind}");
            }
        }, "Channel conflict detection");

    private static async Task<TestResult> TestReleaseAuthority(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var mappingClient = GetServiceClient<IMappingClient>();
            var regionId = Guid.NewGuid();

            // Create channel
            var createRequest = new CreateChannelRequest
            {
                RegionId = regionId,
                Kind = MapKind.Navigation,
                NonAuthorityHandling = NonAuthorityHandlingMode.Reject_and_alert
            };
            var createResponse = await mappingClient.CreateChannelAsync(createRequest);

            // Release authority
            var releaseRequest = new ReleaseAuthorityRequest
            {
                ChannelId = createResponse.ChannelId,
                AuthorityToken = createResponse.AuthorityToken
            };
            var releaseResponse = await mappingClient.ReleaseAuthorityAsync(releaseRequest);

            if (!releaseResponse.Released)
                return TestResult.Failed("Authority release returned false");

            return TestResult.Successful($"Authority released for channel: {createResponse.ChannelId}");
        }, "Authority release");

    private static async Task<TestResult> TestAuthorityHeartbeat(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var mappingClient = GetServiceClient<IMappingClient>();
            var regionId = Guid.NewGuid();

            // Create channel
            var createRequest = new CreateChannelRequest
            {
                RegionId = regionId,
                Kind = MapKind.Resources,
                NonAuthorityHandling = NonAuthorityHandlingMode.Reject_and_alert
            };
            var createResponse = await mappingClient.CreateChannelAsync(createRequest);
            var originalExpiration = createResponse.ExpiresAt;

            // Small delay to ensure time difference
            await Task.Delay(100);

            // Send heartbeat
            var heartbeatRequest = new AuthorityHeartbeatRequest
            {
                ChannelId = createResponse.ChannelId,
                AuthorityToken = createResponse.AuthorityToken
            };
            var heartbeatResponse = await mappingClient.AuthorityHeartbeatAsync(heartbeatRequest);

            if (!heartbeatResponse.Valid)
                return TestResult.Failed("Heartbeat returned invalid status");

            if (heartbeatResponse.ExpiresAt <= originalExpiration)
                return TestResult.Failed("Heartbeat did not extend authority expiration");

            return TestResult.Successful($"Authority heartbeat acknowledged, new expiration: {heartbeatResponse.ExpiresAt}");
        }, "Authority heartbeat");

    #endregion

    #region Publishing Tests

    private static async Task<TestResult> TestPublishMapUpdate(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var mappingClient = GetServiceClient<IMappingClient>();
            var regionId = Guid.NewGuid();

            // Create channel
            var createRequest = new CreateChannelRequest
            {
                RegionId = regionId,
                Kind = MapKind.Dynamic_objects,
                NonAuthorityHandling = NonAuthorityHandlingMode.Reject_and_alert
            };
            var createResponse = await mappingClient.CreateChannelAsync(createRequest);

            // Publish update
            var publishRequest = new PublishMapUpdateRequest
            {
                ChannelId = createResponse.ChannelId,
                AuthorityToken = createResponse.AuthorityToken,
                Payload = new MapPayload
                {
                    ObjectType = "tree",
                    Position = new Position3D { X = 100.5, Y = 0, Z = 200.3 },
                    Data = new Dictionary<string, object>
                    {
                        { "health", 100 },
                        { "species", "oak" }
                    }
                },
                DeltaType = DeltaType.Delta
            };
            var publishResponse = await mappingClient.PublishMapUpdateAsync(publishRequest);

            if (!publishResponse.Accepted)
                return TestResult.Failed("Map update was not accepted");

            if (publishResponse.Version <= 0)
                return TestResult.Failed("Version not incremented");

            return TestResult.Successful($"Map update published: Version={publishResponse.Version}");
        }, "Map update publish");

    private static async Task<TestResult> TestPublishObjectChanges(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var mappingClient = GetServiceClient<IMappingClient>();
            var regionId = Guid.NewGuid();

            // Create channel
            var createRequest = new CreateChannelRequest
            {
                RegionId = regionId,
                Kind = MapKind.Points_of_interest,
                NonAuthorityHandling = NonAuthorityHandlingMode.Reject_and_alert
            };
            var createResponse = await mappingClient.CreateChannelAsync(createRequest);

            // Publish batch changes
            var publishRequest = new PublishObjectChangesRequest
            {
                ChannelId = createResponse.ChannelId,
                AuthorityToken = createResponse.AuthorityToken,
                Changes = new List<ObjectChange>
                {
                    new ObjectChange
                    {
                        ObjectId = Guid.NewGuid(),
                        Action = ObjectAction.Created,
                        ObjectType = "landmark",
                        Position = new Position3D { X = 50, Y = 0, Z = 50 },
                        Data = new Dictionary<string, object> { { "name", "Ancient Tree" } }
                    },
                    new ObjectChange
                    {
                        ObjectId = Guid.NewGuid(),
                        Action = ObjectAction.Created,
                        ObjectType = "quest_marker",
                        Position = new Position3D { X = 75, Y = 0, Z = 75 }
                    }
                }
            };
            var publishResponse = await mappingClient.PublishObjectChangesAsync(publishRequest);

            if (!publishResponse.Accepted)
                return TestResult.Failed("Object changes were not accepted");

            if (publishResponse.ProcessedCount != 2)
                return TestResult.Failed($"Expected 2 processed, got {publishResponse.ProcessedCount}");

            return TestResult.Successful($"Object changes published: ProcessedCount={publishResponse.ProcessedCount}, Version={publishResponse.Version}");
        }, "Object changes publish");

    private static async Task<TestResult> TestPublishWithInvalidToken(ITestClient client, string[] args) =>
        await ExecuteExpectingStatusAsync(async () =>
        {
            var mappingClient = GetServiceClient<IMappingClient>();

            // Try to publish with bad token
            var publishRequest = new PublishMapUpdateRequest
            {
                ChannelId = Guid.NewGuid(),
                AuthorityToken = "invalid-token-12345",
                Payload = new MapPayload { ObjectType = "rock" },
                DeltaType = DeltaType.Delta
            };
            await mappingClient.PublishMapUpdateAsync(publishRequest);
        }, 401, "Publish rejection with invalid token");

    private static async Task<TestResult> TestRequestSnapshot(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var mappingClient = GetServiceClient<IMappingClient>();
            var regionId = Guid.NewGuid();

            // Create channel and publish some data first
            var createRequest = new CreateChannelRequest
            {
                RegionId = regionId,
                Kind = MapKind.Spawn_points,
                NonAuthorityHandling = NonAuthorityHandlingMode.Reject_and_alert
            };
            var createResponse = await mappingClient.CreateChannelAsync(createRequest);

            // Publish a few objects
            for (var i = 0; i < 3; i++)
            {
                await mappingClient.PublishMapUpdateAsync(new PublishMapUpdateRequest
                {
                    ChannelId = createResponse.ChannelId,
                    AuthorityToken = createResponse.AuthorityToken,
                    Payload = new MapPayload
                    {
                        ObjectType = "spawn_point",
                        Position = new Position3D { X = i * 10, Y = 0, Z = i * 10 }
                    },
                    DeltaType = DeltaType.Delta
                });
            }

            // Request snapshot
            var snapshotRequest = new RequestSnapshotRequest
            {
                RegionId = regionId
            };
            var snapshotResponse = await mappingClient.RequestSnapshotAsync(snapshotRequest);

            if (!snapshotResponse.Requested)
                return TestResult.Failed("Snapshot request was not accepted");

            return TestResult.Successful($"Snapshot requested: RequestId={snapshotResponse.RequestId}");
        }, "Snapshot request");

    #endregion

    #region Query Tests

    private static async Task<TestResult> TestQueryPoint(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var mappingClient = GetServiceClient<IMappingClient>();
            var regionId = Guid.NewGuid();

            // Create channel and publish test object
            var createRequest = new CreateChannelRequest
            {
                RegionId = regionId,
                Kind = MapKind.Hazards,
                NonAuthorityHandling = NonAuthorityHandlingMode.Reject_and_alert
            };
            var createResponse = await mappingClient.CreateChannelAsync(createRequest);

            var testPosition = new Position3D { X = 100, Y = 0, Z = 100 };
            await mappingClient.PublishMapUpdateAsync(new PublishMapUpdateRequest
            {
                ChannelId = createResponse.ChannelId,
                AuthorityToken = createResponse.AuthorityToken,
                Payload = new MapPayload
                {
                    ObjectType = "fire_trap",
                    Position = testPosition,
                    Data = new Dictionary<string, object> { { "damage", 50 } }
                },
                DeltaType = DeltaType.Delta
            });

            // Query nearby
            var queryRequest = new QueryPointRequest
            {
                RegionId = regionId,
                Position = testPosition,
                Radius = 10.0
            };
            var queryResponse = await mappingClient.QueryPointAsync(queryRequest);

            if (queryResponse.Objects == null)
                return TestResult.Failed("Query returned null objects list");

            return TestResult.Successful($"Point query completed: Found {queryResponse.Objects.Count} objects at ({queryResponse.Position.X}, {queryResponse.Position.Z})");
        }, "Point query");

    private static async Task<TestResult> TestQueryBounds(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var mappingClient = GetServiceClient<IMappingClient>();
            var regionId = Guid.NewGuid();

            // Query bounds (no channel needed - reads from any existing data)
            var queryRequest = new QueryBoundsRequest
            {
                RegionId = regionId,
                Bounds = new Bounds
                {
                    Min = new Position3D { X = 0, Y = 0, Z = 0 },
                    Max = new Position3D { X = 1000, Y = 100, Z = 1000 }
                },
                MaxObjects = 100
            };
            var queryResponse = await mappingClient.QueryBoundsAsync(queryRequest);

            if (queryResponse.Objects == null)
                return TestResult.Failed("Query returned null objects list");

            if (queryResponse.Bounds == null)
                return TestResult.Failed("Query did not echo bounds");

            return TestResult.Successful($"Bounds query completed: Found {queryResponse.Objects.Count} objects, Truncated={queryResponse.Truncated}");
        }, "Bounds query");

    private static async Task<TestResult> TestQueryObjectsByType(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var mappingClient = GetServiceClient<IMappingClient>();
            var regionId = Guid.NewGuid();

            // Create channel and publish objects of specific type
            var createRequest = new CreateChannelRequest
            {
                RegionId = regionId,
                Kind = MapKind.Resources,
                NonAuthorityHandling = NonAuthorityHandlingMode.Reject_and_alert
            };
            var createResponse = await mappingClient.CreateChannelAsync(createRequest);

            // Add gold veins
            for (var i = 0; i < 3; i++)
            {
                await mappingClient.PublishMapUpdateAsync(new PublishMapUpdateRequest
                {
                    ChannelId = createResponse.ChannelId,
                    AuthorityToken = createResponse.AuthorityToken,
                    Payload = new MapPayload
                    {
                        ObjectType = "gold_vein",
                        Position = new Position3D { X = i * 20, Y = -10, Z = i * 20 }
                    },
                    DeltaType = DeltaType.Delta
                });
            }

            // Query by type
            var queryRequest = new QueryObjectsByTypeRequest
            {
                RegionId = regionId,
                ObjectType = "gold_vein",
                MaxObjects = 50
            };
            var queryResponse = await mappingClient.QueryObjectsByTypeAsync(queryRequest);

            if (queryResponse.ObjectType != "gold_vein")
                return TestResult.Failed($"Wrong object type in response: {queryResponse.ObjectType}");

            return TestResult.Successful($"Type query completed: Found {queryResponse.Objects?.Count ?? 0} gold_vein objects");
        }, "Object type query");

    #endregion

    #region Affordance Tests

    private static async Task<TestResult> TestQueryAffordance(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var mappingClient = GetServiceClient<IMappingClient>();
            var regionId = Guid.NewGuid();

            var queryRequest = new AffordanceQueryRequest
            {
                RegionId = regionId,
                AffordanceType = AffordanceType.Shelter,
                Bounds = new Bounds
                {
                    Min = new Position3D { X = 0, Y = 0, Z = 0 },
                    Max = new Position3D { X = 500, Y = 100, Z = 500 }
                },
                MaxResults = 10,
                MinScore = 0.1,
                Freshness = AffordanceFreshness.Fresh
            };
            var queryResponse = await mappingClient.QueryAffordanceAsync(queryRequest);

            if (queryResponse.Locations == null)
                return TestResult.Failed("Affordance query returned null locations");

            if (queryResponse.QueryMetadata == null)
                return TestResult.Failed("Affordance query did not return metadata");

            return TestResult.Successful($"Affordance query completed: Found {queryResponse.Locations.Count} shelter locations, CacheHit={queryResponse.QueryMetadata.CacheHit}");
        }, "Affordance query");

    private static async Task<TestResult> TestQueryAffordanceWithActorCapabilities(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var mappingClient = GetServiceClient<IMappingClient>();
            var regionId = Guid.NewGuid();

            var queryRequest = new AffordanceQueryRequest
            {
                RegionId = regionId,
                AffordanceType = AffordanceType.Ambush,
                Bounds = new Bounds
                {
                    Min = new Position3D { X = 0, Y = 0, Z = 0 },
                    Max = new Position3D { X = 500, Y = 100, Z = 500 }
                },
                MaxResults = 5,
                MinScore = 0.2,
                Freshness = AffordanceFreshness.Cached,
                ActorCapabilities = new ActorCapabilities
                {
                    Size = ActorSize.Medium,
                    Height = 1.8,
                    CanClimb = true,
                    CanSwim = false,
                    CanFly = false,
                    PerceptionRange = 50.0,
                    MovementSpeed = 5.0,
                    StealthRating = 0.3
                }
            };
            var queryResponse = await mappingClient.QueryAffordanceAsync(queryRequest);

            if (queryResponse.Locations == null)
                return TestResult.Failed("Affordance query with capabilities returned null locations");

            return TestResult.Successful($"Affordance with actor capabilities: Found {queryResponse.Locations.Count} ambush locations");
        }, "Affordance query with actor capabilities");

    #endregion

    #region Authoring Tests

    private static async Task<TestResult> TestAuthoringCheckout(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var mappingClient = GetServiceClient<IMappingClient>();
            var regionId = Guid.NewGuid();

            var checkoutRequest = new AuthoringCheckoutRequest
            {
                RegionId = regionId,
                Kind = MapKind.Static_geometry,
                EditorId = GenerateTestId("editor")
            };
            var checkoutResponse = await mappingClient.CheckoutForAuthoringAsync(checkoutRequest);

            if (!checkoutResponse.Success)
                return TestResult.Failed("Checkout was not successful");

            if (string.IsNullOrEmpty(checkoutResponse.AuthorityToken))
                return TestResult.Failed("Checkout did not return authority token");

            if (checkoutResponse.ExpiresAt == null || checkoutResponse.ExpiresAt <= DateTimeOffset.UtcNow)
                return TestResult.Failed("Invalid checkout expiration");

            return TestResult.Successful($"Authoring checkout successful: ExpiresAt={checkoutResponse.ExpiresAt}");
        }, "Authoring checkout");

    private static async Task<TestResult> TestAuthoringConflict(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var mappingClient = GetServiceClient<IMappingClient>();
            var regionId = Guid.NewGuid();

            // First checkout
            var firstCheckout = new AuthoringCheckoutRequest
            {
                RegionId = regionId,
                Kind = MapKind.Terrain,
                EditorId = GenerateTestId("editor1")
            };
            var firstResponse = await mappingClient.CheckoutForAuthoringAsync(firstCheckout);
            if (!firstResponse.Success)
                return TestResult.Failed("First checkout failed");

            // Second checkout should conflict
            try
            {
                var secondCheckout = new AuthoringCheckoutRequest
                {
                    RegionId = regionId,
                    Kind = MapKind.Terrain,
                    EditorId = GenerateTestId("editor2")
                };
                var secondResponse = await mappingClient.CheckoutForAuthoringAsync(secondCheckout);

                // If status 409 was returned but no exception, check response
                if (!secondResponse.Success && !string.IsNullOrEmpty(secondResponse.LockedBy))
                {
                    return TestResult.Successful($"Correctly detected lock by: {secondResponse.LockedBy}");
                }
                return TestResult.Failed("Second checkout should have failed with conflict");
            }
            catch (ApiException ex) when (ex.StatusCode == 409)
            {
                return TestResult.Successful("Correctly received Conflict for second checkout attempt");
            }
        }, "Authoring conflict detection");

    private static async Task<TestResult> TestAuthoringCommit(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var mappingClient = GetServiceClient<IMappingClient>();
            var regionId = Guid.NewGuid();

            // Checkout
            var checkoutRequest = new AuthoringCheckoutRequest
            {
                RegionId = regionId,
                Kind = MapKind.Navigation,
                EditorId = GenerateTestId("editor")
            };
            var checkoutResponse = await mappingClient.CheckoutForAuthoringAsync(checkoutRequest);
            if (!checkoutResponse.Success || string.IsNullOrEmpty(checkoutResponse.AuthorityToken))
                return TestResult.Failed("Checkout failed");

            // Commit
            var commitRequest = new AuthoringCommitRequest
            {
                RegionId = regionId,
                Kind = MapKind.Navigation,
                AuthorityToken = checkoutResponse.AuthorityToken
            };
            var commitResponse = await mappingClient.CommitAuthoringAsync(commitRequest);

            if (!commitResponse.Success)
                return TestResult.Failed("Commit was not successful");

            return TestResult.Successful($"Authoring commit successful: Version={commitResponse.Version}");
        }, "Authoring commit");

    private static async Task<TestResult> TestAuthoringRelease(ITestClient client, string[] args) =>
        await ExecuteTestAsync(async () =>
        {
            var mappingClient = GetServiceClient<IMappingClient>();
            var regionId = Guid.NewGuid();

            // Checkout
            var checkoutRequest = new AuthoringCheckoutRequest
            {
                RegionId = regionId,
                Kind = MapKind.Points_of_interest,
                EditorId = GenerateTestId("editor")
            };
            var checkoutResponse = await mappingClient.CheckoutForAuthoringAsync(checkoutRequest);
            if (!checkoutResponse.Success || string.IsNullOrEmpty(checkoutResponse.AuthorityToken))
                return TestResult.Failed("Checkout failed");

            // Release without commit
            var releaseRequest = new AuthoringReleaseRequest
            {
                RegionId = regionId,
                Kind = MapKind.Points_of_interest,
                AuthorityToken = checkoutResponse.AuthorityToken
            };
            var releaseResponse = await mappingClient.ReleaseAuthoringAsync(releaseRequest);

            if (!releaseResponse.Released)
                return TestResult.Failed("Release was not successful");

            return TestResult.Successful("Authoring release successful (changes discarded)");
        }, "Authoring release");

    #endregion
}
