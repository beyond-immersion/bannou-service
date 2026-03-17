using BeyondImmersion.BannouService.Environment;

namespace BeyondImmersion.BannouService.Environment.Tests;

/// <summary>
/// Unit tests for realm configuration endpoints: SetRealmConfig, GetRealmConfig.
/// Stub tests — internal storage models do not exist yet.
/// </summary>
public class EnvironmentServiceRealmConfigTests
{
    #region SetRealmConfig

    [Fact]
    public async Task SetRealmConfigAsync_NewConfig_CreatesAndRegistersReference()
    {
        // Map: CALL IRealmClient.RealmExistsAsync(realmId) -> 404 if not found
        //   READ climate:biome:{gameServiceId}:{biomeCode} -> 400 if not found or deprecated
        //   READ realm-config:{realmId}
        //   IF not exists: create + CALL IResourceClient.RegisterReferenceAsync(realmId, "environment", realmId, "realm")
        //   WRITE realm-config:{realmId} <- RealmEnvironmentConfigModel
        //   RETURN (200, RealmEnvironmentConfigResponse) — idempotent
        // Arrange: mock realm exists, mock biome template exists and not deprecated,
        //   mock realm-config to return null (new), setup captures
        // Act: call SetRealmConfigAsync
        // Assert: status == OK, config saved with correct realmId/biomeCode,
        //   IResourceClient.RegisterReferenceAsync called (new config)
        // TODO: Implement after RealmEnvironmentConfigModel exists in EnvironmentServiceModels.cs
        await Task.CompletedTask;
    }

    [Fact]
    public async Task SetRealmConfigAsync_ExistingConfig_UpdatesWithoutNewReference()
    {
        // Map: IF exists: update (no new reference registration)
        //   WRITE realm-config:{realmId} <- updated model
        //   RETURN (200, RealmEnvironmentConfigResponse)
        // Arrange: mock realm exists, mock biome template, mock existing realm-config
        // Act: call SetRealmConfigAsync
        // Assert: status == OK, config updated,
        //   IResourceClient.RegisterReferenceAsync NOT called (already registered)
        // TODO: Implement after RealmEnvironmentConfigModel exists
        await Task.CompletedTask;
    }

    [Fact]
    public async Task SetRealmConfigAsync_RealmNotFound_ReturnsNotFound()
    {
        // Map: CALL IRealmClient.RealmExistsAsync -> 404 if not found
        // Arrange: mock IRealmClient to return not-found
        // Act: call SetRealmConfigAsync
        // Assert: status == NotFound
        // TODO: Implement after internal models exist
        await Task.CompletedTask;
    }

    [Fact]
    public async Task SetRealmConfigAsync_BiomeNotFoundOrDeprecated_ReturnsBadRequest()
    {
        // Map: READ climate:biome:{gameServiceId}:{biomeCode} -> 400 if not found or deprecated
        // Arrange: mock realm exists, mock biome template to return null (or deprecated)
        // Act: call SetRealmConfigAsync
        // Assert: status == BadRequest
        // TODO: Implement after internal models exist
        await Task.CompletedTask;
    }

    #endregion

    #region GetRealmConfig

    [Fact]
    public async Task GetRealmConfigAsync_Exists_ReturnsOk()
    {
        // Map: READ realm-config:{realmId} -> 404 if not set
        //   Does NOT return DefaultBiomeCode fallback — only explicitly set configs
        //   RETURN (200, RealmEnvironmentConfigResponse)
        // Arrange: mock _climateStore.GetAsync("realm-config:{realmId}") to return config
        // Act: call GetRealmConfigAsync
        // Assert: status == OK, response matches stored config
        // TODO: Implement after RealmEnvironmentConfigModel exists
        await Task.CompletedTask;
    }

    [Fact]
    public async Task GetRealmConfigAsync_NotSet_ReturnsNotFound()
    {
        // Map: READ -> 404 if not set
        //   Does NOT return DefaultBiomeCode fallback
        // Arrange: mock store to return null
        // Act: call GetRealmConfigAsync
        // Assert: status == NotFound, response == null (no fallback to config.DefaultBiomeCode)
        // TODO: Implement after internal models exist
        await Task.CompletedTask;
    }

    #endregion
}
