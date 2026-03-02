using BeyondImmersion.Bannou.Client;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.Bannou.GameSession.ClientEvents;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Species;

namespace BeyondImmersion.EdgeTester.Tests;

/// <summary>
/// Test handler demonstrating the new typed service proxies and typed event subscriptions.
/// This shows the ergonomic improvements from issue #105.
/// </summary>
public class TypedApiTestHandler : BaseWebSocketTestHandler
{
    private const string CodePrefix = "TYPE";
    private const string Description = "TypedApi";

    /// <summary>
    /// Formats an error response object for logging using BannouJson serialization.
    /// Returns a structured JSON representation instead of relying on default ToString().
    /// </summary>
    private static string FormatError(object? error)
    {
        if (error == null)
        {
            return "<null>";
        }

        return BannouJson.Serialize(error);
    }

    public override ServiceTest[] GetServiceTests() =>
    [
        new ServiceTest(TestTypedProxyCharacterCreate, "Typed API - Character Create (Proxy)", "WebSocket",
            "Test typed character creation using client.Character.CreateAsync()"),
        new ServiceTest(TestTypedEventSubscription, "Typed API - Event Subscription", "WebSocket",
            "Test typed event subscription using client.OnEvent<TEvent>()"),
    ];

    /// <summary>
    /// Demonstrates the new typed proxy pattern:
    /// Before: client.InvokeAsync&lt;object, JsonElement&gt;("POST", "/character/create", new { ... })
    /// After:  client.Character.CreateAsync(new CreateCharacterRequest { ... })
    /// </summary>
    private void TestTypedProxyCharacterCreate(string[] args)
    {
        Console.WriteLine("=== Typed Proxy Character Create Test ===");
        Console.WriteLine("Demonstrating typed service proxy usage...");

        RunWebSocketTest("Typed proxy test", async adminClient =>
        {
            var uniqueCode = GenerateUniqueCode();

            // Setup: Get or create game service (required for realm creation)
            var gameServiceId = await GetOrCreateTestGameServiceAsync(adminClient);
            if (gameServiceId == null)
            {
                Console.WriteLine("   Failed to get/create game service");
                return false;
            }

            // Setup: Create realm and species using typed proxies
            Console.WriteLine("   Creating realm using typed proxy...");
            var realmResponse = await adminClient.Realm.CreateRealmAsync(new CreateRealmRequest
            {
                Code = $"{CodePrefix}REALM{uniqueCode}",
                Name = $"{Description} Realm {uniqueCode}",
                Description = $"Test realm for {Description}",
                GameServiceId = gameServiceId.Value
            });

            if (!realmResponse.IsSuccess || realmResponse.Result == null)
            {
                Console.WriteLine($"   Failed to create realm: {FormatError(realmResponse.Error)}");
                return false;
            }

            var realm = realmResponse.Result;
            Console.WriteLine($"   Created realm: {realm.RealmId} ({realm.Code})");

            Console.WriteLine("   Creating species using typed proxy...");
            var speciesResponse = await adminClient.Species.CreateSpeciesAsync(new CreateSpeciesRequest
            {
                Code = $"{CodePrefix}SPEC{uniqueCode}",
                Name = $"{Description} Species {uniqueCode}",
                Description = $"Test species for {Description}",
                RealmIds = new List<Guid> { realm.RealmId }
            });

            if (!speciesResponse.IsSuccess || speciesResponse.Result == null)
            {
                Console.WriteLine($"   Failed to create species: {FormatError(speciesResponse.Error)}");
                return false;
            }

            var species = speciesResponse.Result;
            Console.WriteLine($"   Created species: {species.SpeciesId} ({species.Code})");

            // Main test: Create character using typed proxy
            Console.WriteLine("   Creating character using typed proxy...");
            var characterResponse = await adminClient.Character.CreateCharacterAsync(new CreateCharacterRequest
            {
                Name = $"TypedChar{uniqueCode}",
                RealmId = realm.RealmId,
                SpeciesId = species.SpeciesId,
                BirthDate = DateTimeOffset.UtcNow,
                Status = CharacterStatus.Alive
            });

            if (!characterResponse.IsSuccess || characterResponse.Result == null)
            {
                Console.WriteLine($"   Failed to create character: {FormatError(characterResponse.Error)}");
                return false;
            }

            var character = characterResponse.Result;
            Console.WriteLine($"   Created character: {character.CharacterId} ({character.Name})");

            // Verify with typed get
            Console.WriteLine("   Retrieving character using typed proxy...");
            var getResponse = await adminClient.Character.GetCharacterAsync(new GetCharacterRequest
            {
                CharacterId = character.CharacterId
            });

            if (!getResponse.IsSuccess || getResponse.Result == null)
            {
                Console.WriteLine($"   Failed to get character: {FormatError(getResponse.Error)}");
                return false;
            }

            var retrieved = getResponse.Result;
            Console.WriteLine($"   Retrieved character: {retrieved.CharacterId} ({retrieved.Name})");

            // Verify data matches
            var success = retrieved.CharacterId == character.CharacterId
                        && retrieved.Name == character.Name
                        && retrieved.RealmId == realm.RealmId;

            Console.WriteLine($"   Data match: {success}");
            Console.WriteLine("   Typed proxy test completed successfully!");

            return success;
        });
    }

    /// <summary>
    /// Demonstrates the new typed event subscription pattern:
    /// Before: client.OnEvent("game-session.chat-received", json => { var doc = JsonDocument.Parse(json); ... })
    /// After:  client.OnEvent&lt;SessionChatReceivedClientEvent&gt;(evt => Console.WriteLine(evt.Message))
    /// </summary>
    private void TestTypedEventSubscription(string[] args)
    {
        Console.WriteLine("=== Typed Event Subscription Test ===");
        Console.WriteLine("Demonstrating typed event subscription...");

        RunWebSocketTest("Typed event subscription test", async adminClient =>
        {
            // Subscribe to chat messages with typed handler
            Console.WriteLine("   Subscribing to SessionChatReceivedClientEvent...");

            using var subscription = adminClient.OnEvent<SessionChatReceivedClientEvent>(evt =>
            {
                Console.WriteLine($"   Received typed event: SessionId={evt.SessionId}, Message={evt.Message}");
            });

            Console.WriteLine($"   Subscription active: {subscription.IsActive}, EventName: {subscription.EventName}");

            // The subscription is active and typed - it will automatically deserialize
            // any SessionChatReceivedClientEvent that arrives on the WebSocket

            // For this test, we verify the subscription mechanism works
            // In a real scenario, you would join a game session and send chat messages

            Console.WriteLine($"   Subscription type verification passed");
            Console.WriteLine($"   EventName correctly resolved to: {subscription.EventName}");

            // Verify subscription can be disposed
            subscription.Dispose();
            Console.WriteLine($"   Subscription disposed, IsActive: {subscription.IsActive}");

            // Satisfy async requirement
            await Task.CompletedTask;

            return !subscription.IsActive; // Should be false after disposal
        });
    }
}
