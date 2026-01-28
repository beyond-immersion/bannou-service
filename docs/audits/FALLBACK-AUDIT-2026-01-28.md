# Fallback Pattern Audit - 2026-01-28

> **Status**: CENTRAL VALIDATION IMPLEMENTED - REMAINING ISSUES PENDING REVIEW
> **Total Problematic Instances**: ~290+
> **Priority**: All decisions deferred to user

---

## RESOLUTION: Central Configuration Validation

**Implemented**: 2026-01-28

A central validation mechanism was added to prevent empty strings on non-nullable configuration properties:

### Files Modified

1. **`bannou-service/Configuration/IServiceConfiguration.cs`**
   - Added `ValidateNonNullableStrings()` default interface method
   - Uses NullabilityInfoContext to detect non-nullable string properties
   - Throws `InvalidOperationException` with detailed error message if any are empty

2. **`bannou-service/Plugins/PluginLoader.cs`**
   - Calls `ValidateNonNullableStrings()` after building each configuration
   - Applied to both `AppConfiguration` and all service configurations

### Tenet Updates

- **TENETS.md**: Added violation entry for secondary fallbacks
- **IMPLEMENTATION.md (T21)**: Added requirement #9 forbidding secondary fallbacks for schema-defaulted properties

### Rationale

- NRT provides compile-time null safety for non-nullable strings
- Schema provides default values (e.g., `= "generic"`)
- The only way to get empty string is explicit override (e.g., `GAME_SESSION_SUPPORTED_GAME_SERVICES=""`)
- Empty string on a property with a schema default is always a user configuration error
- Central validation catches this at startup with a clear error message

### Impact on Remaining Tasks

For **non-nullable string configuration properties with schema defaults**, inline null checks are now redundant:
- The central validation handles empty string detection
- Service code can trust the configuration is valid after DI injection

For **nullable configuration properties** (string? with no default), inline fallbacks may still be appropriate depending on context.

---

## CRITICAL - FIXED via Central Validation

### 1. GameSessionService.cs - SupportedGameServices Fallback

**File**: `/home/lysander/repos/bannou/plugins/lib-game-session/GameSessionService.cs`
**Status**: **FIXED** - Inline check removed, central validation handles empty string case

**Before**:
```csharp
_supportedGameServices = new HashSet<string>(configuredServices ?? new[] { "system" }, StringComparer.OrdinalIgnoreCase);
```

**After**:
```csharp
var configuredServices = configuration.SupportedGameServices.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
_supportedGameServices = new HashSet<string>(configuredServices, StringComparer.OrdinalIgnoreCase);
```

---

### 2. GameSessionStartupService.cs - Duplicate Fallback

**File**: `/home/lysander/repos/bannou/plugins/lib-game-session/GameSessionStartupService.cs`
**Status**: **FIXED** - Inline check removed, central validation handles empty string case

**Before**:
```csharp
var supportedGameServices = _configuration.SupportedGameServices?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? new[] { "system" };
```

**After**:
```csharp
var supportedGameServices = _configuration.SupportedGameServices.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
```

---

## Configuration Property Fallbacks (17 instances)

### 3. Orchestrator DockerImageName (4 backends)

**Files**:
- `/home/lysander/repos/bannou/plugins/lib-orchestrator/Backends/DockerComposeOrchestrator.cs:679`
- `/home/lysander/repos/bannou/plugins/lib-orchestrator/Backends/KubernetesOrchestrator.cs:407`
- `/home/lysander/repos/bannou/plugins/lib-orchestrator/Backends/PortainerOrchestrator.cs:365`
- `/home/lysander/repos/bannou/plugins/lib-orchestrator/Backends/DockerSwarmOrchestrator.cs:373`

```csharp
var imageName = _configuration.DockerImageName ?? "bannou:latest";
```

**Problem**: Falls back to `latest` tag in production. If configuration fails, deploys unpredictable image version.

---

### 4. Orchestrator PresetsHostPath (2 instances)

**File**: `/home/lysander/repos/bannou/plugins/lib-orchestrator/OrchestratorService.cs`
**Line**: 122

```csharp
var presetsPath = configuration.PresetsHostPath ?? "/app/provisioning/orchestrator/presets";
```

**File**: `/home/lysander/repos/bannou/plugins/lib-orchestrator/Backends/DockerComposeOrchestrator.cs`
**Line**: 86

```csharp
_presetsHostPath = config.PresetsHostPath ?? "/app/provisioning/orchestrator/presets";
```

**Problem**: Hardcoded container path masks configuration failures.

---

### 5. DockerComposeOrchestrator - DockerNetwork

**File**: `/home/lysander/repos/bannou/plugins/lib-orchestrator/Backends/DockerComposeOrchestrator.cs`
**Line**: 84

```csharp
_configuredDockerNetwork = config.DockerNetwork ?? "bannou_default";
```

**Problem**: Falls back to default network name.

---

### 6. DockerComposeOrchestrator - CertificatesHostPath

**File**: `/home/lysander/repos/bannou/plugins/lib-orchestrator/Backends/DockerComposeOrchestrator.cs`
**Line**: 85

```csharp
_certificatesHostPath = config.CertificatesHostPath ?? "/app/provisioning/certificates";
```

**Problem**: Container path hardcoded; masks configuration failure.

---

### 7. DockerComposeOrchestrator - LogsVolumeName

**File**: `/home/lysander/repos/bannou/plugins/lib-orchestrator/Backends/DockerComposeOrchestrator.cs`
**Line**: 87

```csharp
_logsVolumeName = config.LogsVolumeName ?? "logs-data";
```

**Problem**: Falls back to default volume name.

---

### 8. Documentation GitStoragePath (2 instances)

**File**: `/home/lysander/repos/bannou/plugins/lib-documentation/Services/RepositorySyncSchedulerService.cs`
**Line**: 231

```csharp
var storagePath = _configuration.GitStoragePath ?? "/tmp/bannou-git-repos";
```

**File**: `/home/lysander/repos/bannou/plugins/lib-documentation/DocumentationService.cs`
**Line**: 2783

```csharp
return Path.Combine(_configuration.GitStoragePath ?? "/tmp/bannou-git-repos", bindingId.ToString());
```

**Problem**: Falls back to `/tmp` directory - may be cleared on reboot (data loss).

---

### 9. Voice RtpEngineHost

**File**: `/home/lysander/repos/bannou/plugins/lib-voice/Services/ScaledTierCoordinator.cs`
**Line**: 148

```csharp
var rtpHost = _configuration.RtpEngineHost ?? "localhost";
```

**Problem**: Falls back to localhost. In production with distributed RTPEngine, causes silent failure.

---

### 10. Subscription AuthorizationSuffix

**File**: `/home/lysander/repos/bannou/plugins/lib-subscription/SubscriptionService.cs`
**Line**: 54

```csharp
private string AuthorizationSuffix => _configuration.AuthorizationSuffix ?? "authorized";
```

**Problem**: Property-level fallback used frequently. Misconfiguration goes undetected.

---

### 11. Messaging DefaultExchange (Triple Fallback)

**File**: `/home/lysander/repos/bannou/plugins/lib-messaging/Services/RabbitMQMessageTap.cs`
**Line**: 61

```csharp
var effectiveSourceExchange = sourceExchange ?? _configuration.DefaultExchange ?? AppConstants.DEFAULT_APP_NAME;
```

**Problem**: Triple fallback chain - configuration failure is completely invisible.

---

### 12. SmartRestartManager DockerHost

**File**: `/home/lysander/repos/bannou/plugins/lib-orchestrator/SmartRestartManager.cs`
**Line**: 39

```csharp
var dockerHost = _configuration.DockerHost ?? "unix:///var/run/docker.sock";
```

**Problem**: Defaults to Linux socket path. On Windows or remote Docker, fails silently.

---

### 13. KubernetesOrchestrator Namespace

**File**: `/home/lysander/repos/bannou/plugins/lib-orchestrator/Backends/KubernetesOrchestrator.cs`
**Line**: 71

```csharp
_namespace = configuration.KubernetesNamespace ?? "default";
```

**Problem**: Falls back to "default" namespace. In production K8s, could deploy to wrong namespace.

---

## String Fallbacks Needing Review (7 instances)

### 14. Documentation Content Coercion

**File**: `/home/lysander/repos/bannou/plugins/lib-documentation/DocumentationService.cs`
**Line**: 3466

```csharp
Content = d.Content ?? string.Empty,
```

**Problem**: Silently coerces null to empty string - should validate if Content is required.

---

### 15. Permission Version Fallback (2 instances)

**File**: `/home/lysander/repos/bannou/plugins/lib-permission/PermissionServiceEvents.cs`
**Line**: 134

```csharp
Version = evt.Version ?? "",
```

**File**: `/home/lysander/repos/bannou/plugins/lib-permission/PermissionService.cs`
**Line**: 1128

```csharp
var version = registrationData?.Version ?? "";
```

**Problem**: Silent coercion - unclear if Version should be nullable or required.

---

### 16. Orchestrator AppName Filter

**File**: `/home/lysander/repos/bannou/plugins/lib-orchestrator/OrchestratorService.cs`
**Line**: 1227

```csharp
Select(c => c.AppName ?? "")
```

**Problem**: Silent coercion then filter.

---

### 17. Actor GOAP ActionId (Double Fallback)

**File**: `/home/lysander/repos/bannou/plugins/lib-actor/ActorService.cs`
**Line**: 1277

```csharp
ActionId = dict.TryGetValue("actionId", out var actionId) ? actionId?.ToString() ?? "" : "",
```

**Problem**: Double fallback - both on null ToString() and missing key.

---

### 18. MetadataHelper Utilities (3 instances)

**File**: `/home/lysander/repos/bannou/bannou-service/MetadataHelper.cs`
**Lines**: 97, 119, 202

```csharp
result[kvp.Key] = kvp.Value?.ToString() ?? "";
result[kvp.Key] = kvp.Value?.ToString() ?? "";
JsonValueKind.String => element.GetString() ?? "",
```

**Problem**: Silently coerces null to empty - could mask missing metadata.

---

## SYSTEMIC: State Store Collection Fallbacks (208 instances)

**Pattern**: `await stateStore.GetAsync(key) ?? new List<T>()`

This pattern appears across 25+ services. When state store returns null (key doesn't exist), code silently treats as empty collection.

### By Service (partial list with line numbers):

#### Account Service
| File | Line | Code |
|------|------|------|
| AccountService.cs | 295 | `roles = body.Roles?.ToList() ?? new List<string>()` |
| AccountService.cs | 518 | `currentMetadata = account.Metadata ?? new Dictionary<string, object>()` |
| AccountService.cs | 711 | `authMethods = await authMethodsStore.GetAsync() ?? new List<AuthMethodInfo>()` |
| AccountService.cs | 888 | `return authMethods ?? new List<AuthMethodInfo>()` |
| AccountService.cs | 1414 | `AuthMethods = authMethods ?? new List<AuthMethodInfo>()` |

#### Actor Service
| File | Line | Code |
|------|------|------|
| ActorService.cs | 271 | `allIds = await indexStore.GetAsync() ?? new List<string>()` |
| ActorServiceEvents.cs | 89 | `allIds = await indexStore.GetAsync() ?? new List<string>()` |
| Handlers/StateUpdateHandler.cs | 106 | `existing = scope.GetValue(rootKey) as Dictionary ?? new Dictionary<string, object?>()` |
| Handlers/StateUpdateHandler.cs | 116 | `list = scope.GetValue(rootKey) as List ?? new List<object?>()` |
| Runtime/ActorRunner.cs | 337 | `Data = initialData ?? new Dictionary<string, object?>()` |
| Runtime/BackstoryProvider.cs | 32 | `_allElements = backstory?.Elements?.ToList() ?? new List<BackstoryElement>()` |
| Runtime/EncountersProvider.cs | 53-55 | Multiple dictionary fallbacks |

#### Auth Service
| File | Line | Code |
|------|------|------|
| AuthServiceEvents.cs | 57 | `evt.ChangedFields ?? new List<string>()` |
| AuthServiceEvents.cs | 67 | `newRoles = evt.Roles?.ToList() ?? new List<string>()` |
| Services/OAuthProviderService.cs | 693 | `existingLinks = await indexStore.GetAsync() ?? new List<string>()` |
| Services/SessionService.cs | 141 | `existingSessions = await listStore.GetAsync() ?? new List<string>()` |
| Services/SessionService.cs | 329 | `return sessionKeys ?? new List<string>()` |
| Services/TokenService.cs | 101 | `Roles = account.Roles?.ToList() ?? new List<string>()` |

#### Character Service
| File | Line | Code |
|------|------|------|
| CharacterService.cs | 1312 | `characterIds = await store.GetAsync() ?? new List<string>()` |

#### Character Encounter Service
| File | Line | Code |
|------|------|------|
| CharacterEncounterService.cs | 500 | `providedPerspectives = body.Perspectives?.ToDictionary() ?? new Dictionary<Guid, PerspectiveInput>()` |
| CharacterEncounterService.cs | 1583 | `return index?.PerspectiveIds.ToList() ?? new List<Guid>()` |
| CharacterEncounterService.cs | 1591 | `return index?.EncounterIds.ToList() ?? new List<Guid>()` |
| CharacterEncounterService.cs | 1598 | `return index?.EncounterIds.ToList() ?? new List<Guid>()` |

#### Connect Service
| File | Line | Code |
|------|------|------|
| BannouSessionManager.cs | 507 | `existingSessions = await store.GetAsync() ?? new HashSet<string>()` |
| BannouSessionManager.cs | 588 | `return sessions ?? new HashSet<string>()` |
| ConnectServiceEvents.cs | 45 | `sessionIds = evt.SessionIds?.Select().ToList() ?? new List<string>()` |

#### Contract Service
| File | Line | Code |
|------|------|------|
| ContractService.cs | 179 | `.ToList() ?? new List<PartyRoleModel>()` |
| ContractService.cs | 282 | `await store.GetAsync(ALL_TEMPLATES_KEY) ?? new List<string>()` |
| ContractService.cs | 441 | `await store.GetAsync(templateIndexKey) ?? new List<string>()` |
| ContractService.cs | 530 | `await store.GetAsync(partyIndexKey) ?? new List<string>()` |
| ContractService.cs | 893-909 | Multiple index fallbacks |
| ContractService.cs | 1121 | `MilestoneProgress = milestoneProgress ?? new List<MilestoneProgressSummary>()` |
| ContractService.cs | 1653-1871 | Multiple party index fallbacks |
| ContractServiceEscrowIntegration.cs | 420, 476 | Clause type index fallbacks |

#### Currency Service
| File | Line | Code |
|------|------|------|
| CurrencyService.cs | 92 | `BannouJson.Deserialize<List<string>>(allDefsJson) ?? new List<string>()` |
| CurrencyService.cs | 228 | `BannouJson.Deserialize<List<string>>(allDefsJson) ?? new List<string>()` |
| CurrencyService.cs | 1599, 1661 | Transaction ID list fallbacks |
| CurrencyService.cs | 2388 | `currencyDefIds = BannouJson.Deserialize() ?? new List<string>()` |
| CurrencyService.cs | 2599 | `holdIds = BannouJson.Deserialize() ?? new List<string>()` |
| CurrencyService.cs | 2754, 2776 | List deserialize fallbacks |
| Services/CurrencyAutogainTaskService.cs | 121, 169 | ID list fallbacks |

#### Escrow Service
| File | Line | Code |
|------|------|------|
| EscrowService.cs | 463-471 | Parties, ExpectedDeposits, Deposits, Consents fallbacks |
| EscrowService.cs | 526, 560, 605 | Asset list fallbacks |
| EscrowServiceCompletion.cs | 57-631 | Multiple party/deposit/asset fallbacks (10+ instances) |
| EscrowServiceConsent.cs | 294 | `agreementModel.Parties ?? new List<EscrowPartyModel>()` |
| EscrowServiceDeposits.cs | 108-378 | Multiple asset/deposit fallbacks |
| EscrowServiceEvents.cs | 256, 267 | Party and deposit model fallbacks |
| EscrowServiceValidation.cs | 365 | `.ToHashSet() ?? new HashSet<Guid>()` |

#### Game Session Service
| File | Line | Code |
|------|------|------|
| GameSessionService.cs | 186 | `await store.GetAsync() ?? new List<string>()` |
| GameSessionService.cs | 338 | `sessionIds = await sessionListStore.GetAsync() ?? new List<string>()` |
| GameSessionService.cs | 670 | `NewGameState = body.ActionData ?? new Dictionary<string, object?>()` |
| GameSessionService.cs | 1953 | `return existing?.SessionIds.ToList() ?? new List<Guid>()` |
| GameSessionService.cs | 2149 | `sessionIds = await sessionListStore.GetAsync() ?? new List<string>()` |
| ReservationCleanupService.cs | 104, 207 | Session list fallbacks |

#### Inventory Service
| File | Line | Code |
|------|------|------|
| InventoryService.cs | 155 | `Tags = body.Tags?.ToList() ?? new List<string>()` |
| InventoryService.cs | 283, 333 | Container ID list fallbacks |
| InventoryService.cs | 2001, 2027 | JSON deserialize fallbacks |

#### Item Service
| File | Line | Code |
|------|------|------|
| ItemService.cs | 99 | `Tags = body.Tags?.ToList() ?? new List<string>()` |
| ItemService.cs | 219, 828, 870 | Template/instance ID fallbacks |
| ItemService.cs | 983, 1020 | JSON deserialize fallbacks |

#### Leaderboard Service
| File | Line | Code |
|------|------|------|
| LeaderboardService.cs | 140 | `EntityTypes = body.EntityTypes?.ToList() ?? new List<EntityType> { EntityType.Account }` |
| LeaderboardService.cs | 1072 | `EntityTypes = definition.EntityTypes ?? new List<EntityType>()` |

#### Location Service
| File | Line | Code |
|------|------|------|
| LocationService.cs | 142, 199, 277, 346 | Location/child/root ID fallbacks |
| LocationService.cs | 922, 1253-1321 | Multiple index fallbacks (10+ instances) |

#### Mapping Service
| File | Line | Code |
|------|------|------|
| MappingService.cs | 962, 1870, 1887, 1902, 1918, 2071, 2119, 2151 | GUID list fallbacks |

#### Matchmaking Service
| File | Line | Code |
|------|------|------|
| MatchmakingService.cs | 503-504 | Property dictionary fallbacks |
| MatchmakingService.cs | 1111 | `reservations = sessionResponse.Reservations ?? new List<ReservationInfo>()` |
| MatchmakingService.cs | 1525-1702 | Multiple queue/ticket index fallbacks (10+ instances) |

#### Mesh Service
| File | Line | Code |
|------|------|------|
| MeshService.cs | 180 | `Services = body.Services ?? new List<string>()` |
| MeshServiceEvents.cs | 77 | `Services = evt.Services?.Select() ?? new List<string>()` |
| MeshServiceEvents.cs | 123 | Service routing dictionary fallback |

#### Orchestrator Service
| File | Line | Code |
|------|------|------|
| OrchestratorService.cs | 1453 | `targets = body.Targets?.ToList() ?? new List<CleanTarget>()` |
| OrchestratorService.cs | 1761 | `changes = body.Changes ?? new List<TopologyChange>()` |
| OrchestratorService.cs | 2568-3210 | Multiple processor lease/instance fallbacks (15+ instances) |
| OrchestratorStateManager.cs | 159 | `Services = heartbeat.Services?.Select() ?? new List<string>()` |
| PresetLoader.cs | 59, 138 | RequiredBackends and Services fallbacks |
| Backends/PortainerOrchestrator.cs | 228, 304 | Container list fallbacks |

#### Permission Service
| File | Line | Code |
|------|------|------|
| PermissionService.cs | 276, 332, 445, 466 | Registered services/endpoints/sessions fallbacks |
| PermissionService.cs | 527, 531, 546 | State/permission dictionary fallbacks |
| PermissionService.cs | 593, 600, 768, 769 | More state dictionary fallbacks |
| PermissionService.cs | 884, 947, 1013, 1086 | Service registration fallbacks |
| PermissionService.cs | 1209, 1238, 1249, 1299, 1312 | Session state/connection fallbacks |
| PermissionServiceEvents.cs | 80, 83 | Permission requirement/state fallbacks |
| PermissionServiceEvents.cs | 218, 219, 246 | Role/authorization list fallbacks |

#### Realm Service
| File | Line | Code |
|------|------|------|
| RealmService.cs | 305, 441 | All realm ID fallbacks |

#### Relationship Service
| File | Line | Code |
|------|------|------|
| RelationshipService.cs | 110, 207, 291 | Relationship ID fallbacks |
| RelationshipService.cs | 656, 674, 692, 706 | Index query fallbacks |
| RelationshipService.cs | 768, 796, 823 | Metadata dictionary fallbacks |

#### Relationship Type Service
| File | Line | Code |
|------|------|------|
| RelationshipTypeService.cs | 128, 1076, 1097, 1110, 1121, 1133 | Type/children ID fallbacks |

#### Save-Load Service
| File | Line | Code |
|------|------|------|
| SaveLoadService.cs | 140-141 | Tags and Metadata fallbacks |
| SaveLoadService.cs | 570, 1137, 1329, 2029 | Metadata dictionary fallbacks |

#### Scene Service
| File | Line | Code |
|------|------|------|
| SceneService.cs | 177, 279, 288, 340, 452 | Tags and candidate ID fallbacks |
| SceneService.cs | 1123, 1469 | Validation rules and history fallbacks |
| SceneService.cs | 1530, 1536, 1548, 1572, 1621, 1630 | Index HashSet fallbacks |
| SceneService.cs | 1980, 2005, 2008, 2025, 2046, 2067 | Scene node/tags fallbacks |

#### Species Service
| File | Line | Code |
|------|------|------|
| SpeciesService.cs | 391, 406, 573 | RealmIds and species ID fallbacks |
| SpeciesService.cs | 1190, 1202 | Species index fallbacks |
| SpeciesService.cs | 1287, 1326 | TraitModifiers dictionary fallbacks |

#### Subscription Service
| File | Line | Code |
|------|------|------|
| SubscriptionService.cs | 288, 573, 585, 597 | Subscription ID list fallbacks |

#### Voice Service
| File | Line | Code |
|------|------|------|
| VoiceService.cs | 294 | `?? new List<string> { "stun:stun.l.google.com:19302" }` (STUN servers with default) |
| VoiceService.cs | 571 | `IceCandidates = body.IceCandidates?.ToList() ?? new List<string>()` |

---

## Acceptable Patterns (Do Not Fix)

### Client SDK STUN Server Fallback

**File**: `/home/lysander/repos/bannou/sdks/client-voice/SIPSorceryVoicePeer.cs`
**Line**: 83

```csharp
var iceServers = (stunServers ?? new[] { "stun:stun.l.google.com:19302" })
    .Select(uri => new RTCIceServer { urls = uri })
    .ToList();
```

**Reason**: This is client SDK code providing a sensible public default for voice connectivity. Acceptable.

---

### ETag Compiler Satisfaction (62 instances)

Pattern with proper comments like:
```csharp
// GetWithETagAsync returns non-null etag when key exists; coalesce satisfies compiler's nullable analysis (will never execute)
etag ?? string.Empty
```

**Reason**: These follow CLAUDE.md documented exception for compiler satisfaction with explanatory comments.

---

### JSON GetString() Compiler Satisfaction (3 instances)

Pattern with comments like:
```csharp
// GetString() returns string? but cannot return null when ValueKind is String; coalesce satisfies compiler
property.Value.GetString() ?? string.Empty
```

**Reason**: These follow CLAUDE.md documented exception.

---

## Summary Statistics

| Category | Count | Status |
|----------|-------|--------|
| CRITICAL (GameSession) | 2 | FIX IMMEDIATELY |
| Configuration Property Fallbacks | 17 | REVIEW EACH |
| String Fallbacks | 7 | REVIEW EACH |
| State Store Collection Fallbacks | 208 | ARCHITECTURAL DECISION |
| Request Property Collection Fallbacks | 55+ | ARCHITECTURAL DECISION |
| Acceptable Patterns | 66+ | DO NOT FIX |
| **TOTAL PROBLEMATIC** | **~290** | |

---

## Decision Required

For each category above, decide:

1. **CRITICAL**: Should throw exception (recommended)
2. **Configuration**: Throw vs. acceptable default for dev?
3. **String fallbacks**: Throw vs. coerce to empty?
4. **State store fallbacks**:
   - Distinguish "key doesn't exist" (OK) vs "error" (throw)?
   - Log warning on null returns?
   - Accept as intentional for optional indexes?
5. **Request property fallbacks**:
   - Validate explicitly?
   - Accept coercing null to empty for optional fields?

---

*Generated: 2026-01-28*
*Audit triggered by: GameSession SupportedGameServices fallback using "system" instead of schema default "generic"*
