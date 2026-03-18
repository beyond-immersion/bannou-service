# Bannou Code Generation Scripts Catalog

> **Do not edit manually** - regenerate with `make generate-docs` or update via catalog maintenance.
> **Last Updated**: 2026-03-18

This catalog documents every script in `scripts/`, describing what each does, the tools it uses, what it outputs, and where it fits in the code generation pipeline.

---

## Table of Contents

1. [Pipeline Overview](#pipeline-overview)
2. [Shared Library](#shared-library)
3. [Top-Level Orchestrators](#top-level-orchestrators)
4. [Schema Resolvers (Pre-Processing)](#schema-resolvers-pre-processing)
5. [Core NSwag Generators (Per-Service)](#core-nswag-generators-per-service)
6. [Event & Messaging Generators](#event--messaging-generators)
7. [Permission & Subscription Generators](#permission--subscription-generators)
8. [Meta & Companion Generators](#meta--companion-generators)
9. [Client SDK Generators (.NET)](#client-sdk-generators-net)
10. [Client SDK Generators (TypeScript)](#client-sdk-generators-typescript)
11. [Client SDK Generators (Unreal Engine)](#client-sdk-generators-unreal-engine)
12. [Resource & Reference Generators](#resource--reference-generators)
13. [State & Variable Provider Generators](#state--variable-provider-generators)
14. [Documentation Generators](#documentation-generators)
15. [Document Catalog Generators](#document-catalog-generators)
16. [Bootstrapping & One-Time Generators](#bootstrapping--one-time-generators)
17. [Utility & Non-Generation Scripts](#utility--non-generation-scripts)
18. [Pipeline Execution Order](#pipeline-execution-order)

---

## Pipeline Overview

The top-level entry point is `make generate`, which calls:

1. `scripts/generate-all-services.sh` — The master orchestrator for all service code generation
2. `scripts/generate-storyline-archives.sh` — Storyline-theory SDK archive type extraction
3. `scripts/generate-client-sdk.sh` — .NET client SDK assembly
4. `make generate-sdk-ts` → `generate-client-schema.py` + TypeScript SDK tooling
5. `make generate-unreal-sdk` → `generate-client-schema.py` + `generate-unreal-sdk.py`
6. `scripts/generate-docs.sh` — All documentation generation

The `make generate-services` target (with optional `PLUGIN=` filter) calls `generate-all-services.sh` directly for service-only regeneration.

---

## Shared Library

### `common.sh`
- **Purpose**: Shared utility functions sourced by all shell-based generation scripts.
- **What it provides**:
  - **String conversion**: `to_pascal_case` (hyphenated-name → PascalCase), `to_camel_case` (hyphenated-name → camelCase)
  - **NSwag discovery**: `find_nswag_exe` (searches global tools, NuGet package paths), `require_nswag` (exits if not found, sets `NSWAG_EXE`)
  - **.NET setup**: `ensure_dotnet_root` (finds and exports `DOTNET_ROOT`)
  - **Schema validation**: `validate_schema_file`, `validate_service_args`
  - **Directory helpers**: `get_script_dir`, `get_repo_root`
  - **$ref extraction**: `extract_api_refs` (finds `$ref` to service's `-api.yaml`), `extract_common_api_refs` (finds `$ref` to `common-api.yaml`), `extract_inlined_types` (reads `x-inlined-types` from resolved schemas) — all return comma-separated type name lists for NSwag exclusion
  - **Post-processing functions**:
    - `postprocess_enum_suppressions` — Wraps `public enum` declarations with `#pragma warning disable/restore CS1591` (enum members can't have XML docs in OpenAPI)
    - `postprocess_additional_properties_docs` — Adds XML `<summary>` comments before `[JsonExtensionData] AdditionalProperties` declarations (NSwag omits these, causing CS1591)
    - `postprocess_enum_pascalcase` — Converts NSwag's underscore-style enum member names (e.g., `Event_only`) to PascalCase (`EventOnly`) using two perl passes: one for member declarations, one for default value references
  - **Lifecycle event generation**: `generate_lifecycle_event_models` — Full lifecycle event C# model generation from a service's `schemas/Generated/{service}-service-lifecycle-events.yaml`. Handles resolver invocation, ref extraction, exclusion list building, NSwag invocation, and all post-processing steps. Called by `generate-models.sh` and `generate-service-events.sh`.
- **Tools used**: bash, sed, awk, perl, grep, python3
- **Run from**: Sourced by other scripts (`source "$(dirname "$0")/common.sh"`)
- **Output**: No direct output (library only)

---

## Top-Level Orchestrators

### `generate-all-services.sh`
- **Purpose**: Master orchestrator for the entire service code generation pipeline. The single script that generates ALL service code from schemas.
- **Usage**: `./generate-all-services.sh [service-name] [components...]`
- **Pipeline position**: Called by `make generate` as step 1, or directly via `make generate-services`
- **Execution order** (runs each step sequentially, exits on failure):
  1. `generate-state-stores.py` — State store definitions from `state-stores.yaml`
  2. `generate-variable-providers.py` — Variable provider definitions from `variable-providers.yaml`
  3. `generate-lifecycle-events.py` — Lifecycle event YAML schemas from `x-lifecycle`
  4. `generate-resource-mappings.py` — Resource event mappings from `x-resource-mapping`
  5. `generate-resource-templates.py` — Resource templates from `x-archive-type`
  6. `generate-common-api.sh` — Common API types (EntityType, etc.)
  7. `generate-common-events.sh` — Common event models (BaseServiceEvent, etc.)
  8. `generate-service-events.sh` — Per-service event models (includes lifecycle events)
  9. `generate-published-topics.py` — Published topic constants from `x-event-publications`
  10. `generate-event-publishers.py` — Typed `Publish*Async` extension methods
  11. `generate-client-events.sh` — Client event models (server→client WebSocket push)
  12. `generate-client-event-whitelist.sh` — Connect service event whitelist
  13. `generate-event-subscription-registry.sh` — Event subscription registry
  14. `embed-meta-schemas.py` — Meta schemas for companion endpoints
  15. **Per-service loop**: For each `schemas/*-api.yaml`, calls `generate-service.sh` with requested components
  16. **Global post-processing** (runs on ALL `*/Generated/*.cs` files):
      - Fix NSwag `AdditionalProperties` lazy initialization (prevents `{}` in JSON)
      - Remove NSwag pragma warning blocks (15 pragma disable lines per file)
      - Remove null checks on enum parameters (CS0472 fix)
      - Replace hardcoded `"bannou"` app-id defaults with `AppConstants.DEFAULT_APP_NAME`
  17. `generate-env-example.py` — `.env.example` from configuration schemas
  18. `generate-client-proxies.py` — Client SDK typed proxies
  19. `generate-storyline-archives.sh` — Storyline archive types
  20. `generate-service-navigator.py` — `IServiceNavigator`/`ServiceNavigator` aggregator
  21. `generate-references.py` — Resource reference tracking code
  22. `generate-compression-callbacks.py` — Compression callback registration
  23. `generate-event-templates.py` — Event template registration
  24. `generate-client-event-registry.py` — Client event registry
  25. `generate-client-event-groups.py` — Service-grouped event subscriptions
  26. `generate-client-endpoint-metadata.py` — Client endpoint metadata
- **Tools used**: bash, python3 (delegates to sub-scripts)
- **Run from**: Repository root or scripts directory (auto-cds to scripts/)
- **Output**: Everything described in the sub-scripts below

### `generate-service.sh`
- **Purpose**: Per-service orchestrator that generates all code artifacts for a single service in dependency order.
- **Usage**: `./generate-service.sh <service-name> [components...]`
- **Components**: `project`, `models`, `controller`, `meta-controller`, `event-subscriptions`, `client`, `interface`, `config`, `implementation`, `models-internal`, `plugin`, `permissions`, `tests`, `all` (default)
- **Pipeline position**: Called by `generate-all-services.sh` for each service, or directly for single-service regeneration (`cd scripts && ./generate-service.sh account`)
- **Execution order** (dependency-ordered): project → models → controller → meta-controller → event-subscriptions → client → interface → config → implementation → models-internal → plugin → permissions → tests
- **Behavior**: Each component calls `generate-{component}.sh` with the service name. Skips unrequested components. Stops on critical failures (project, controller). Reports summary.
- **Tools used**: bash (delegates to component scripts)
- **Run from**: Scripts directory (relative paths assume `../schemas/`)
- **Output**: Delegates to component scripts

### `generate-docs.sh`
- **Purpose**: Orchestrator for all documentation generation. Generates auto-maintained markdown files into `docs/generated/`.
- **Usage**: `./scripts/generate-docs.sh` or `make generate-docs`
- **Pipeline position**: Called by `make generate` as step 6 (final step)
- **Calls** (sequentially):
  1. `generate-state-stores.py` (also generates docs)
  2. `generate-variable-providers.py` (also generates docs)
  3. `generate-localization-categories.py`
  4. `generate-event-docs.py`
  5. `generate-config-docs.py`
  6. `generate-service-details-docs.py`
  7. `generate-client-api-docs.py`
  8. `generate-client-events-docs.py`
  9. `generate-metadata-docs.py`
  10. `generate-deprecation-docs.py`
  11. `generate-guides-catalog.py`
  12. `generate-planning-catalog.py`
  13. `generate-faq-catalog.py`
  14. `generate-operations-catalog.py`
  15. `generate-specifications-catalog.py`
  16. `generate-sdks-catalog.py`
- **Tools used**: bash, python3
- **Output**: All `docs/generated/GENERATED-*.md` files

---

## Schema Resolvers (Pre-Processing)

These Python scripts resolve cross-file `$ref` references that NSwag cannot handle natively. They produce "resolved" YAML schemas in `schemas/Generated/` with complex types inlined, enabling NSwag to process them.

### `resolve-schema-refs.py`
- **Purpose**: Unified schema `$ref` resolver handling ALL schema types (API, events, lifecycle). Replaces three separate scripts (`resolve-api-refs.py`, `resolve-event-refs.py`, `resolve-lifecycle-refs.py`) with one script using the strongest resolution logic — recursive cross-file chain following with schema caching.
- **The problem it solves**: NSwag can't handle cross-file `$ref`s where the referenced type contains its own nested `$ref`s. For example, `arbitration-api.yaml` references `EntityReference` from `common-api.yaml`, and `EntityReference` internally `$ref`s `EntityType`. NSwag resolves the inner `$ref` relative to the processing file, where `EntityType` doesn't exist. This script inlines complex types and their full transitive dependency trees.
- **Usage**:
  - `python3 resolve-schema-refs.py` — Resolve all schema types
  - `python3 resolve-schema-refs.py --api [service]` — API schemas only
  - `python3 resolve-schema-refs.py --events [service]` — Event schemas only
  - `python3 resolve-schema-refs.py --lifecycle [service]` — Lifecycle event schemas only
  - Modes can be combined: `--events --lifecycle` resolves both
- **Input**: `schemas/*-api.yaml`, `schemas/*-service-events.yaml`, `schemas/Generated/*-service-lifecycle-events.yaml`, and all transitively referenced schemas
- **Output** (only when resolution is needed):
  - `schemas/Generated/{service}-api-resolved.yaml`
  - `schemas/Generated/{service}-service-events-resolved.yaml`
  - `schemas/Generated/{service}-service-lifecycle-events-resolved.yaml`
- **Key behaviors**:
  - Triggers on ALL cross-file `$ref`s with nested refs (not just `allOf` — fixes the bug where standalone property refs to complex common-api types were missed)
  - Recursively follows cross-file ref chains through multiple files (lifecycle → API → common)
  - Schema caching avoids re-reading files during recursive traversal
  - Handles path fixing for schemas in vs out of `Generated/` subdirectory
  - Embeds `x-inlined-types` metadata list for downstream NSwag exclusion logic
  - Cleans up stale resolved files before processing
- **Tools used**: Python 3, `ruamel.yaml`
- **Pipeline position**: Called by `generate-models.sh` (`--api`), `generate-service-events.sh` (`--events --lifecycle`), `common.sh:generate_lifecycle_event_models()` (`--lifecycle`)

### ~~`resolve-schema-refs.py`~~ (DELETED)
- **Status**: Consolidated into `resolve-schema-refs.py`. Had a narrower trigger (allOf-only) that missed standalone `$ref`s to complex common-api types like `EntityReference`.

### ~~`resolve-event-refs.py`~~ (DELETED)
- **Status**: Consolidated into `resolve-schema-refs.py`. Had weaker resolution than `resolve-lifecycle-refs.py` (didn't follow transitive cross-file chains).

### ~~`resolve-lifecycle-refs.py`~~ (DELETED)
- **Status**: Consolidated into `resolve-schema-refs.py`. Its recursive chain-following logic with schema caching became the unified resolver's core algorithm.

---

## Core NSwag Generators (Per-Service)

These scripts are called by `generate-service.sh` for each service and use NSwag to generate C# code from OpenAPI schemas.

### `generate-models.sh`
- **Purpose**: Generates C# request/response model classes (DTOs) from a service's API schema. Also generates lifecycle event C# models if the service has `x-lifecycle` definitions.
- **Usage**: `cd scripts && ./generate-models.sh <service-name> [schema-file]`
- **Input**: `schemas/{service}-api.yaml` (or resolved version), `schemas/Generated/{service}-service-lifecycle-events.yaml`
- **Output**: `bannou-service/Generated/Models/{Service}Models.cs` — Contains all request/response types. Also `bannou-service/Generated/Events/{Service}LifecycleEvents.cs` if lifecycle events exist.
- **Tools used**: NSwag (`openapi2csclient` with `/generateDtoTypes:true`, `/generateClientClasses:false`), `resolve-schema-refs.py`, `extract-sdk-types.py`, post-processing (JsonRequired injection, enum CS1591 suppressions, AdditionalProperties docs, enum PascalCase)
- **Namespace**: `BeyondImmersion.BannouService.{Service}`
- **Exclusion logic**: Excludes `ApiException`, common-api.yaml types (already in `CommonApiModels.cs`), and `x-sdk-type` mapped types (external SDK types)
- **Pipeline position**: Step 2 in per-service generation order (after project)
- **Run from**: Scripts directory

### `generate-controller.sh`
- **Purpose**: Generates the HTTP controller class from a service's API schema with NSwag's controller generator.
- **Usage**: `cd scripts && ./generate-controller.sh <service-name> [schema-file]`
- **Input**: `schemas/{service}-api.yaml`
- **Output**: `plugins/lib-{service}/Generated/{Service}Controller.cs` — Routes HTTP requests to service interface methods. For schemas with `x-controller-only: true`, generates an abstract base controller class. For `x-manual-implementation: true`, generates comment-only placeholders.
- **Pre-processing**: Creates a filtered schema to fix dual content-type issues (`application/yaml` removed when `application/json` coexists) and `format: uri` issues (NSwag generates `Uri` types instead of `string`). Filtered schemas go to `schemas/Generated/` with `../` ref path adjustments.
- **Post-processing**: If `x-controller-only` methods exist and no manual partial controller file exists yet, generates a skeleton partial controller class with `override` stubs.
- **Tools used**: NSwag (`openapi2cscontroller`), python3 (schema filtering, abstract method extraction), Liquid templates (`../templates/nswag`)
- **Namespace**: `BeyondImmersion.BannouService.{Service}`
- **Pipeline position**: Step 3 in per-service generation order (after models)

### `generate-interface.sh`
- **Purpose**: Generates the `I{Service}Service` interface from the API schema using Python-based schema parsing (not NSwag). Extracts all endpoint operation IDs and builds async method signatures.
- **Usage**: `cd scripts && ./generate-interface.sh <service-name> [schema-file]`
- **Input**: `schemas/{service}-api.yaml`, `bannou-service/Generated/Models/{Service}Models.cs` (for enum discovery)
- **Output**: `plugins/lib-{service}/Generated/I{Service}Service.cs`
- **Key logic**: Parses the schema's `paths`, converts OpenAPI types to C# types (including enum matching against generated models), excludes `x-controller-only` and `x-manual-implementation` endpoints, builds `Task<(StatusCodes, TResponse?)>` return types.
- **Tools used**: Python 3 (yaml parsing, type conversion), bash (file generation)
- **Namespace**: `BeyondImmersion.BannouService.{Service}`
- **Pipeline position**: Step 7 in per-service generation order (after client)

### `generate-client.sh`
- **Purpose**: Generates the service client class used for inter-service mesh calls (via lib-mesh).
- **Usage**: `cd scripts && ./generate-client.sh <service-name> [schema-file]`
- **Input**: `schemas/{service}-api.yaml`
- **Output**: `bannou-service/Generated/Clients/{Service}Client.cs` — Contains `{Service}Client` class and `I{Service}Client` interface for dependency injection.
- **Pre-processing**: Strips `x-from-authorization` parameters (service-to-service calls don't use JWT header parameters). Creates filtered schema in `schemas/Generated/` if needed.
- **Tools used**: NSwag (`openapi2csclient` with `/generateClientClasses:true`, `/generateDtoTypes:false`, `/generateOptionalParameters:true`, `/useHttpClientCreationMethod:true`), python3 (parameter filtering)
- **Namespace**: `BeyondImmersion.BannouService.{Service}`
- **Key NSwag options**: Injects via `IMeshInvocationClient` pattern, uses `BeyondImmersion.BannouService.ServiceClients` namespace
- **Pipeline position**: Step 6 in per-service generation order (after event-subscriptions)

### `generate-config.sh`
- **Purpose**: Generates typed configuration classes from `{service}-configuration.yaml`. Handles both main configuration (`x-service-configuration`) and helper configurations (`x-helper-configurations`).
- **Usage**: `cd scripts && ./generate-config.sh <service-name> [schema-file]`
- **Input**: `schemas/{service}-configuration.yaml`, optionally `schemas/{service}-api.yaml` (for `$ref` enum resolution and `x-sdk-type` detection)
- **Output**: `plugins/lib-{service}/Generated/{Service}ServiceConfiguration.cs` — Main configuration class. Also `{Service}{Helper}Configuration.cs` files for each `x-helper-configurations` entry.
- **Key features**:
  - Maps OpenAPI types to C# types (string, int, double, bool, Guid, DateTime, long, float, string[])
  - Handles `$ref` to API schema enums (reuses already-generated enum types)
  - Handles `x-sdk-type` references (adds SDK namespace usings)
  - Generates `[ConfigRange]`, `[ConfigStringLength]`, `[ConfigPattern]`, `[ConfigMultipleOf]` validation attributes from OpenAPI keywords
  - Generates `[ConfigConstraintGroupDefinition]` class-level and `[ConfigConstraintGroup]` property-level attributes from `x-constraint-groups`
  - Generates `[ServiceConfiguration]` attribute with env prefix
  - Handles inline enum definitions (generates enum types within the config file, deduplicating against API schema enums)
  - Generates `[Required]` for non-nullable strings without defaults
- **Tools used**: Python 3 (yaml parsing, C# code generation), bash
- **Namespace**: `BeyondImmersion.BannouService.{Service}`
- **Pipeline position**: Step 8 in per-service generation order (after interface)

### `generate-implementation.sh`
- **Purpose**: Generates a one-time service implementation template (`{Service}Service.cs`) with TODO stubs for each endpoint. **Never overwrites** existing implementations.
- **Usage**: `cd scripts && ./generate-implementation.sh <service-name> [schema-file]`
- **Input**: `schemas/{service}-api.yaml`
- **Output**: `plugins/lib-{service}/{Service}Service.cs` (only if it doesn't already exist)
- **Key features**:
  - Reads `x-service-layer` from schema for `[BannouService]` attribute
  - Detects `x-references` for conditional `IResourceClient` injection
  - Generates proper `Task<(StatusCodes, TResponse?)>` or `Task<StatusCodes>` return types per endpoint
  - Excludes `x-controller-only` and `x-manual-implementation` endpoints
  - First method includes example patterns (lib-state CRUD, lib-messaging events)
  - Includes comprehensive XML doc comments with implementation checklist
- **Tools used**: Python 3 (yaml parsing, method stub generation), bash
- **Pipeline position**: Step 9 in per-service generation order (after config). One-time only.

### `generate-models-internal.sh`
- **Purpose**: Generates a one-time internal models template file (`{Service}ServiceModels.cs`) for storage models, cache entries, and internal DTOs. **Never overwrites** existing files.
- **Usage**: `cd scripts && ./generate-models-internal.sh <service-name> [schema-file]`
- **Output**: `plugins/lib-{service}/{Service}ServiceModels.cs` (only if it doesn't exist)
- **Tools used**: bash (heredoc template)
- **Pipeline position**: Step 10 in per-service generation order. One-time only.

---

## Event & Messaging Generators

### `generate-common-api.sh`
- **Purpose**: Generates shared API types from `common-api.yaml` (system-wide types like `EntityType` used by all services).
- **Usage**: `cd scripts && ./generate-common-api.sh`
- **Input**: `schemas/common-api.yaml`
- **Output**: `bannou-service/Generated/CommonApiModels.cs`
- **Tools used**: NSwag (`openapi2csclient`), post-processing (enum suppressions, AdditionalProperties docs)
- **Namespace**: `BeyondImmersion.BannouService`
- **Pipeline position**: Step 6 in `generate-all-services.sh` (before service-specific models, since services exclude these types)

### `generate-common-events.sh`
- **Purpose**: Generates shared event base types from `common-events.yaml` (e.g., `BaseServiceEvent`, `ServiceRegistrationEvent`, `ServiceHeartbeatEvent`).
- **Usage**: `cd scripts && ./generate-common-events.sh`
- **Input**: `schemas/common-events.yaml`
- **Output**: `bannou-service/Generated/CommonEventsModels.cs`
- **Exclusions**: `BaseServiceEvent` (hand-defined in Core SDK for inheritance), common-api.yaml types
- **Tools used**: NSwag (`openapi2csclient`), post-processing (JsonRequired, EventName override fix, enum suppressions, AdditionalProperties docs)
- **Namespace**: `BeyondImmersion.BannouService.Events`
- **Pipeline position**: Step 7 in `generate-all-services.sh` (after common API, before per-service events)

### `generate-service-events.sh`
- **Purpose**: Generates C# event model classes from per-service `{service}-service-events.yaml` files and lifecycle event schemas.
- **Usage**: `scripts/generate-service-events.sh [service-name]` (auto-cds; can run from anywhere)
- **Input**: All `schemas/*-service-events.yaml`, resolved versions in `schemas/Generated/`
- **Output**: `bannou-service/Generated/Events/{Service}EventsModels.cs` for each service. Also `{Service}LifecycleEvents.cs` via `generate_lifecycle_event_models()`.
- **Pre-processing**: Runs `generate-lifecycle-events.py` (YAML schema generation) and `resolve-schema-refs.py --events --lifecycle` (complex ref resolution)
- **Exclusion logic**: Excludes `BaseServiceEvent`, API schema types (already generated in `{Service}Models.cs`), common-api types, and inlined types from resolved schemas
- **Tools used**: NSwag, python3, post-processing (JsonRequired, EventName override, enum suppressions, AdditionalProperties docs)
- **Namespace**: `BeyondImmersion.BannouService.Events`
- **Pipeline position**: Step 8 in `generate-all-services.sh`

### `generate-client-events.sh`
- **Purpose**: Generates server→client WebSocket push event model classes from `{service}-client-events.yaml` files.
- **Usage**: `scripts/generate-client-events.sh` (auto-cds; can run from anywhere)
- **Input**: `schemas/common-client-events.yaml`, all `schemas/*-client-events.yaml`
- **Output**:
  - `bannou-service/Generated/CommonClientEventsModels.cs` (common events: `CapabilityManifestEvent`, `DisconnectNotificationEvent`, etc.)
  - `plugins/lib-{service}/Generated/{Service}ClientEventsModels.cs` (per-service events)
- **Exclusions**: `BaseClientEvent` (hand-defined in Core SDK), API types, common-api types
- **Tools used**: NSwag, post-processing (JsonRequired, EventName override, enum suppressions, AdditionalProperties docs)
- **Namespace**: Common: `BeyondImmersion.BannouService.ClientEvents`; Per-service: `BeyondImmersion.Bannou.{Service}.ClientEvents`
- **Pipeline position**: Step 11 in `generate-all-services.sh` (after service events)

### `generate-lifecycle-events.py`
- **Purpose**: Reads `x-lifecycle` declarations from `*-service-events.yaml` files and generates two outputs per service: (1) OpenAPI event YAML schemas and (2) companion C# interface files. This is both a YAML-to-YAML and YAML-to-C# step.
- **Input**: All `schemas/*-service-events.yaml` with `x-lifecycle` sections
- **Output**:
  - `schemas/Generated/{service}-service-lifecycle-events.yaml` — Complete OpenAPI schemas with `{Entity}CreatedEvent`, `{Entity}UpdatedEvent`, `{Entity}DeletedEvent` definitions. Includes auto-injected fields (`createdAt`, `updatedAt`), deprecation fields when `deprecation: true`, `x-resource-mapping` metadata, and `changedFields` on UpdatedEvent / `deletedReason` on DeletedEvent.
  - `bannou-service/Generated/Events/{Service}LifecycleEvents.Interfaces.cs` — Companion partial classes adding `ILifecycleCreatedEvent`, `ILifecycleUpdatedEvent`, `ILifecycleDeletedEvent` interfaces (and `IDeprecatableEntity` for deprecation entities).
- **Key features**:
  - **Batch mode**: Entities with `batch: true` produce `BatchCreatedEvent`, `BatchModifiedEvent`, `BatchDestroyedEvent` with entry arrays instead of individual events. Interfaces go on entry models, not event wrappers.
  - **Topic derivation**: Pattern A (`entity.action`) without prefix; Pattern C (`prefix.entity.action`) with `topic_prefix` — handles entity-equals-prefix, entity-starts-with-prefix, and unrelated entity names.
  - **Sensitive field exclusion**: Fields listed in `sensitive` are omitted from event data.
  - **Auto-stripping**: If `createdAt`/`updatedAt` or deprecation fields are manually defined in the model, they're stripped and replaced by the standardized auto-injected versions.
- **Tools used**: Python 3, `ruamel.yaml` (755 lines)
- **Pipeline position**: Step 3 in `generate-all-services.sh`; also called by `generate-service-events.sh` pre-processing

### `generate-published-topics.py`
- **Purpose**: Generates static topic constant classes from `x-event-publications` in `*-service-events.yaml`.
- **Input**: All `schemas/*-service-events.yaml` with `x-event-publications`
- **Output**: `plugins/lib-{service}/Generated/{Service}PublishedTopics.cs` — Contains `const string` fields for each published topic. Services use these instead of inline topic strings.
- **Tools used**: Python 3, `ruamel.yaml`
- **Pipeline position**: Step 9 in `generate-all-services.sh` (after service events)

### `generate-event-publishers.py`
- **Purpose**: Generates typed `Publish*Async` extension methods on `IMessageBus` from `x-event-publications`.
- **Input**: All `schemas/*-service-events.yaml` with `x-event-publications`
- **Output**: `plugins/lib-{service}/Generated/{Service}EventPublisher.cs` — Extension methods like `PublishAccountCreatedAsync(this IMessageBus bus, AccountCreatedEvent evt)`. For parameterized topics (with `topic-params`), generates overloads with typed parameters.
- **Tools used**: Python 3, `ruamel.yaml`
- **Pipeline position**: Step 10 in `generate-all-services.sh` (after published topics)

---

## Permission & Subscription Generators

### `generate-permissions.sh`
- **Purpose**: Generates permission registration code from `x-permissions` declarations on API endpoints.
- **Input**: `schemas/{service}-api.yaml`
- **Output**: `plugins/lib-{service}/Generated/{Service}PermissionRegistration.cs` — Contains permission matrix registration for RBAC capability manifest compilation.
- **Tools used**: Python 3 (yaml parsing, C# code generation), bash
- **Pipeline position**: Step 12 in per-service generation order

### `generate-event-subscriptions.sh`
- **Purpose**: Generates one-time event consumer template from `x-event-subscriptions` in `{service}-service-events.yaml`. Creates `{Service}ServiceEvents.cs` with `RegisterEventConsumers` method and handler stubs. **Never overwrites** existing files.
- **Input**: `schemas/{service}-service-events.yaml` with `x-event-subscriptions`
- **Output**: `plugins/lib-{service}/{Service}ServiceEvents.cs` (one-time template, then manually maintained)
- **Tools used**: Python 3, bash
- **Pipeline position**: Step 4 in per-service generation order

### `generate-event-subscription-registry.sh`
- **Purpose**: Generates the global event subscription registry for `NativeEventConsumerBackend` deserialization. Maps event topics to their C# types for automatic deserialization.
- **Input**: All `schemas/*-service-events.yaml` with `x-event-subscriptions`
- **Output**: Event subscription type registry used by the messaging infrastructure
- **Tools used**: bash, python3
- **Pipeline position**: Step 13 in `generate-all-services.sh`

### `generate-client-event-whitelist.sh`
- **Purpose**: Generates the Connect service event whitelist from `x-client-event: true` markers in `*-client-events.yaml` schemas. Controls which events can be forwarded to WebSocket clients.
- **Input**: All `schemas/*-client-events.yaml`
- **Output**: Whitelist file used by Connect service for event validation
- **Tools used**: bash, python3
- **Pipeline position**: Step 12 in `generate-all-services.sh`

---

## Meta & Companion Generators

### `generate-meta-controller.sh`
- **Purpose**: Generates the `{Service}Controller.Meta.cs` companion file providing runtime schema introspection. Allows services to expose their own schema at runtime for documentation and debugging.
- **Input**: `schemas/{service}-api.yaml`
- **Output**: `plugins/lib-{service}/Generated/{Service}Controller.Meta.cs`
- **Tools used**: bash, python3
- **Pipeline position**: Step 4 in per-service generation order (after controller)

### `embed-meta-schemas.py`
- **Purpose**: Creates copies of API schemas with self-contained JSON Schema strings embedded as extension fields. Recursively resolves all `$ref`s and bundles referenced types into `$defs` (JSON Schema draft-07 format), handling circular references. The bundled schemas are embedded as JSON strings so `generate-meta-controller.sh` can emit them as C# raw string literals.
- **Usage**: `python3 scripts/embed-meta-schemas.py [--service SERVICE_NAME]`
- **Input**: All `schemas/*-api.yaml` (or single service with `--service`)
- **Output**: `schemas/Generated/{service}-api-meta.yaml` — Copy of original schema with three extension fields per operation: `x-request-schema-json` (bundled JSON Schema for request body), `x-response-schema-json` (bundled JSON Schema for 200/201 response), `x-endpoint-info-json` (summary, description, tags, deprecated, operationId as JSON)
- **Tools used**: Python 3, `ruamel.yaml`, `json` (321 lines)
- **Pipeline position**: Step 14 in `generate-all-services.sh` (before per-service loop)

---

## Client SDK Generators (.NET)

### `generate-client-sdk.sh`
- **Purpose**: Generates behavior runtime files for both server and client SDKs by copying behavior-compiler SDK files (`sdks/behavior-compiler/`) with namespace transformation. Copies Runtime, Intent, Archetypes, Goap, and Documents files.
- **Input**: `sdks/behavior-compiler/` source files, `plugins/lib-behavior/` evaluator files
- **Output**:
  - `sdks/server/Generated/Behavior/` — Server SDK behavior files with namespace `BeyondImmersion.Bannou.Server.Behavior`
  - `sdks/client/Generated/Behavior/` — Client SDK behavior files with namespace `BeyondImmersion.Bannou.Client.Behavior` (excludes server-only files like `CinematicInterpreter.cs`)
- **Tools used**: bash, sed (namespace transformation)
- **Pipeline position**: Called by `make generate` as step 3

### `generate-client-proxies.py`
- **Purpose**: Generates typed proxy classes for the .NET client SDK. Each proxy wraps a service's endpoints with `IBannouClient.InvokeAsync` calls. Three method patterns: request+response (full typed), response-only (sends empty object), request-only (fire-and-forget via `SendEventAsync`).
- **Input**: All `schemas/*-api.yaml`
- **Output**:
  - `sdks/client/Generated/Proxies/{Service}Proxy.cs` — One proxy per service with typed async methods
  - `sdks/client/Generated/BannouClientProxyExtensions.cs` — Partial class adding lazy-initialized proxy properties to `BannouClient` (e.g., `client.Auth`, `client.Character`)
- **Tools used**: Python 3, `ruamel.yaml` (443 lines)
- **Pipeline position**: Step 18 in `generate-all-services.sh`

### `generate-client-proxies-ts.py`
- **Purpose**: Generates TypeScript typed proxy classes from the consolidated client schema. Uses `Schemas['{TypeName}']` indexed access types (from `openapi-typescript`-generated types). Uses `Object.defineProperty` with Symbol-keyed cache for lazy proxy initialization and TypeScript declaration merging (`declare module`) for type-safe property types.
- **Input**: `schemas/Generated/bannou-client-api.yaml` (consolidated schema from `generate-client-schema.py`)
- **Output** (all in `sdks/typescript/client/src/Generated/`):
  - `proxies/{Service}Proxy.ts` — One per service with typed async methods using `invokeAsync`
  - `proxies/index.ts` — Barrel export file
  - `BannouClientExtensions.ts` — Proxy properties via `Object.defineProperty` + declaration merging
- **Tools used**: Python 3, `ruamel.yaml` (481 lines)
- **Pipeline position**: Called during `make generate-sdk-ts`

### `generate-client-event-registry.py`
- **Purpose**: Generates the .NET client event registry with bidirectional type↔name mappings. Provides `GetEventName<TEvent>()`, `GetEventType(string)`, `IsRegistered`, `GetAllEventTypes`, `GetAllEventNames`.
- **Input**: All `schemas/*-client-events.yaml` with `x-client-event: true` (skips `x-internal: true`)
- **Output**: `sdks/client/Generated/Events/ClientEventRegistry.cs` — Static class with `TypeToEventName` and `EventNameToType` dictionaries
- **Namespace logic**: Common events → `BeyondImmersion.BannouService.ClientEvents`; per-service → `BeyondImmersion.Bannou.{Service}.ClientEvents`
- **Tools used**: Python 3, `ruamel.yaml` (297 lines)
- **Pipeline position**: Step 22 in `generate-all-services.sh`

### `generate-client-event-registry-ts.py`
- **Purpose**: TypeScript version of the client event registry. Generates `Map<string, EventMetadata>` registry with `eventName`, `typeName`, and `service` per event. Includes `isClientEvent` type guard function for runtime validation. Skips `x-internal: true` events.
- **Input**: All `schemas/*-client-events.yaml` with `x-client-event: true`
- **Output**: `sdks/typescript/client/src/Generated/events/ClientEventRegistry.ts` + `index.ts` (barrel export). Provides `getEventMetadata`, `isRegisteredEvent`, `getAllEventNames`, `getEventsByService`, `isClientEvent`.
- **Tools used**: Python 3, `ruamel.yaml` (281 lines)
- **Pipeline position**: Called during `make generate-sdk-ts`

### `generate-client-event-groups.py`
- **Purpose**: Generates service-grouped event subscription classes enabling `client.Events.GameSession.OnPlayerJoined(handler)` pattern. Creates three output types per run.
- **Input**: All `schemas/*-client-events.yaml` with `x-client-event: true` (skips `x-internal: true`)
- **Output** (all in `sdks/client/Generated/Events/`):
  - `{Service}EventSubscriptions.cs` — Per-service class with `On{Event}(Action<TEvent> handler)` methods returning `IEventSubscription`
  - `BannouClientEvents.cs` — Container class with lazy-initialized per-service subscription properties
  - `BannouClientEventsExtension.cs` — Partial `BannouClient` class adding `client.Events` property
- **Common events**: Grouped under "System" (`SystemEventSubscriptions`)
- **Tools used**: Python 3, `ruamel.yaml` (395 lines)
- **Pipeline position**: Step 23 in `generate-all-services.sh`

### `generate-client-endpoint-metadata.py`
- **Purpose**: Generates runtime type discovery metadata mapping `(METHOD, path)` tuples to their request/response C# types. Enables runtime endpoint resolution without compile-time knowledge.
- **Input**: All `schemas/*-api.yaml`
- **Output**: `sdks/client/Generated/ClientEndpointMetadata.cs` — Static class with `METHOD:path` keyed dictionary. Uses fully qualified type references (`BeyondImmersion.BannouService.{Service}.{Type}`). Provides `GetRequestType`, `GetResponseType`, `GetEndpointInfo`, `IsRegistered`, `GetEndpointsByService`, `Count`.
- **Tools used**: Python 3, `ruamel.yaml` (355 lines)
- **Pipeline position**: Step 24 in `generate-all-services.sh` (final code generation step)

### `generate-client-schema.py`
- **Purpose**: Generates a consolidated client-facing OpenAPI schema by merging all service API schemas into a single file, filtering by permissions and excluding internal services.
- **Input**: All `schemas/*-api.yaml`, `schemas/common-api.yaml`
- **Output**:
  - `schemas/Generated/bannou-client-api.yaml` (YAML format)
  - `schemas/Generated/bannou-client-api.json` (JSON format)
- **Key behaviors**:
  - Filters endpoints by `x-permissions` — only includes `anonymous`/`user`/`authenticated`/`developer` roles (excludes admin-only)
  - Excludes entire services: `mesh`, `messaging`, `state`, `orchestrator` (internal/infrastructure)
  - Deduplicates `components/schemas` across services (first-encountered wins)
  - Dead schema elimination: only includes schemas transitively referenced by included endpoints
  - Prefixes `operationId` with service name for global uniqueness
  - Rewrites cross-file `$ref`s to local refs (all types are inlined)
  - Adds `x-bannou-protocol` extension with full binary header layout (31-byte request, 16-byte response), message flags, and response codes
- **Tools used**: Python 3, `ruamel.yaml`, `json` (421 lines)
- **Pipeline position**: Called by `make generate-client-schema` (prerequisite for `generate-sdk-ts` and `generate-unreal-sdk`)

---

## Client SDK Generators (Unreal Engine)

### `generate-unreal-sdk.py`
- **Purpose**: Top-level orchestrator for Unreal Engine SDK generation. Runs three sub-scripts as subprocesses: `generate-client-schema.py` → `generate-unreal-protocol.py` → `generate-unreal-types.py`. Verifies all 7 expected output files exist.
- **Input**: Individual service schemas (delegates to `generate-client-schema.py`)
- **Output**: 5 header files in `sdks/unreal/Generated/` (`BannouProtocol.h`, `BannouTypes.h`, `BannouEnums.h`, `BannouEndpoints.h`, `BannouEvents.h`) + 2 schema files in `schemas/Generated/` (`bannou-client-api.yaml`, `bannou-client-api.json`)
- **Tools used**: Python 3, subprocess (154 lines)
- **Pipeline position**: Called by `make generate-unreal-sdk`

### `generate-unreal-types.py`
- **Purpose**: Generates 4 of the 5 UE header files from the consolidated client schema. Maps OpenAPI types to Unreal C++ types (`FString`, `FGuid`, `FDateTime`, `int32`, `int64`, `float`, `double`, `TArray<T>`, `TOptional<T>`, `TMap<FString, FString>`). All types use `USTRUCT(BlueprintType)`/`UPROPERTY`/`UENUM(BlueprintType)` for Blueprint compatibility.
- **Input**: `schemas/Generated/bannou-client-api.yaml` (consolidated), `schemas/*-client-events.yaml` (for events header)
- **Output** (all in `sdks/unreal/Generated/`):
  - `BannouEnums.h` — All enum types with `UENUM` macro (generated first, types depend on it)
  - `BannouTypes.h` — All request/response structs with `USTRUCT`/`UPROPERTY` (forward declarations + definitions)
  - `BannouEndpoints.h` — `constexpr` endpoint constants organized by service + `Bannou::GetAllEndpoints()` lazy-initialized `TMap<FString, FEndpointInfo>` registry
  - `BannouEvents.h` — `constexpr` client event name constants from `x-client-event` schemas (skips `x-internal`)
- **Tools used**: Python 3, `ruamel.yaml` (652 lines)

### `generate-unreal-protocol.py`
- **Purpose**: Generates `BannouProtocol.h` — a complete header-only C++ implementation of the Bannou binary WebSocket protocol for Unreal Engine. Not just constants — includes full serialization/deserialization code.
- **Input**: Hardcoded protocol constants matching the TypeScript SDK (no schema input needed)
- **Output**: `sdks/unreal/Generated/BannouProtocol.h` containing:
  - `EMessageFlags` UENUM with `ENUM_CLASS_FLAGS` (None, Binary, Encrypted, Compressed, HighPriority, Event, Client, Response, Meta)
  - `EResponseCode` UENUM with helpers (`IsSuccess`, `IsError`, `GetResponseCodeName`, `MapToHttpStatus`)
  - `NetworkByteOrder` namespace — `ReadUInt16/32/64`, `WriteUInt16/32/64`, `ReadGuid`, `WriteGuid` (RFC 4122 byte ordering for `FGuid` ↔ wire format)
  - `FBannouMessage` USTRUCT — Full message representation with Flags, Channel, SequenceNumber, ServiceGuid, MessageId, ResponseCode, Payload/BinaryPayload, plus helper methods
  - `SerializeRequest()` / `ParseMessage()` inline functions — Auto-detects request (31-byte header) vs response (16-byte header)
- **Tools used**: Python 3 (no YAML dependency — protocol constants are hardcoded) (675 lines)

---

## Resource & Reference Generators

### `generate-references.py`
- **Purpose**: Generates resource reference tracking code from `x-references` declarations in API schemas. Produces registration helpers, cleanup callbacks, and migrate callbacks for lib-resource integration.
- **Input**: All `schemas/*-api.yaml` with `x-references` and/or `x-resource-lifecycle` in `info:`
- **Output**: `plugins/lib-{service}/Generated/{Service}ReferenceTracking.cs` — Contains:
  - `Register{Target}ReferenceAsync()` / `Unregister{Target}ReferenceAsync()` helpers for each reference target
  - `RegisterResourceCleanupCallbacksAsync()` for cascade/detach cleanup callbacks
  - `RegisterResourceMigrateCallbacksAsync()` for restrict-policy migrate callbacks
  - `[ResourceCleanupRequired("MethodName")]` class-level attributes (for structural test validation)
- **Additional behavior**: Detects `x-resource-lifecycle` on foundational service schemas and logs config (grace period, cleanup policy) but does not generate code for it currently.
- **Tools used**: Python 3, `ruamel.yaml` (477 lines)
- **Pipeline position**: Step 20 in `generate-all-services.sh`

### `generate-resource-templates.py`
- **Purpose**: Generates `ResourceTemplateBase` implementations from `x-compression-callback` + `x-archive-type` schema extensions. Recursively traverses archive response schemas to build `ValidPaths` dictionaries mapping dot-separated paths to C# types (e.g., `"personality.openness"` → `typeof(double)`).
- **Input**: All `schemas/*-api.yaml` — looks for `x-compression-callback` in `info:` to find compress endpoints, then checks the 200 response schema for `x-archive-type: true`
- **Output**: `bannou-service/Generated/ResourceTemplates/{SourceType}Template.cs` — Each file contains a `ResourceTemplateBase` subclass with `SourceType`, `Namespace`, and `ValidPaths` properties. Handles `$ref`, `allOf`, `additionalProperties`, and cross-file references during traversal (depth-limited to 10).
- **Tools used**: Python 3, `ruamel.yaml` (522 lines)
- **Pipeline position**: Step 5 in `generate-all-services.sh`

### `generate-resource-mappings.py`
- **Purpose**: Generates resource event mapping entries from `x-resource-mapping` extensions on event schemas. Used by Puppetmaster's watch system to map events to watchable resources.
- **Input**: All `schemas/*-service-events.yaml` with `x-resource-mapping` on event definitions; also `x-lifecycle` `resource_mapping` entries
- **Output**: `bannou-service/Generated/ResourceEventMappings.cs` — Static class with `IReadOnlyList<ResourceEventMappingEntry>`
- **Tools used**: Python 3, `ruamel.yaml`
- **Pipeline position**: Step 4 in `generate-all-services.sh`

### `generate-compression-callbacks.py`
- **Purpose**: Generates compression callback registration code from `x-compression-callback` declarations in event schemas. Used for hierarchical resource archival via lib-resource.
- **Input**: All `schemas/*-service-events.yaml` with `x-compression-callback`
- **Output**: `plugins/lib-{service}/Generated/{Service}CompressionCallbacks.cs` — Contains `RegisterAsync()` method for callback registration.
- **Tools used**: Python 3, `ruamel.yaml`
- **Pipeline position**: Step 21 in `generate-all-services.sh`

### `generate-event-templates.py`
- **Purpose**: Generates event template registration code from `x-event-template` declarations on event schema definitions. Templates are used with `emit_event:` ABML actions.
- **Input**: All `schemas/*-service-events.yaml` with `x-event-template` on events
- **Output**: `plugins/lib-{service}/Generated/{Service}EventTemplates.cs` — Static class with `RegisterAll(IEventTemplateRegistry)` method.
- **Tools used**: Python 3, `ruamel.yaml`
- **Pipeline position**: Step 22 in `generate-all-services.sh`

---

## State & Variable Provider Generators

### `generate-state-stores.py`
- **Purpose**: Dual-purpose script that (1) generates `StateStoreDefinitions.cs` with string constants for all state store names, and (2) generates `docs/generated/GENERATED-STATE-STORES.md` documentation.
- **Input**: `schemas/state-stores.yaml`
- **Output**:
  - `lib-state/Generated/StateStoreDefinitions.cs` — Static class with `const string` fields (e.g., `public const string AuthStatestore = "auth-statestore";`)
  - `docs/generated/GENERATED-STATE-STORES.md` — Documentation reference
- **Tools used**: Python 3, `ruamel.yaml`
- **Pipeline position**: Step 1 in `generate-all-services.sh` (first thing generated, since services reference these constants)

### `generate-variable-providers.py`
- **Purpose**: Dual-purpose script that (1) generates `VariableProviderDefinitions.cs` with namespace constants for variable providers, and (2) generates documentation.
- **Input**: `schemas/variable-providers.yaml`
- **Output**:
  - `bannou-service/Generated/VariableProviderDefinitions.cs`
  - `docs/generated/GENERATED-VARIABLE-PROVIDERS.md`
- **Tools used**: Python 3, `ruamel.yaml`
- **Pipeline position**: Step 2 in `generate-all-services.sh`

---

## Documentation Generators

### `generate-event-docs.py`
- **Purpose**: Generates comprehensive event documentation from all event schemas.
- **Input**: All `schemas/*-service-events.yaml`, `schemas/Generated/*-service-lifecycle-events.yaml`
- **Output**: `docs/generated/GENERATED-EVENTS.md`
- **Tools used**: Python 3, `ruamel.yaml`

### `generate-config-docs.py`
- **Purpose**: Generates configuration documentation from all `*-configuration.yaml` schemas.
- **Input**: All `schemas/*-configuration.yaml`
- **Output**: `docs/generated/GENERATED-CONFIGURATION.md`
- **Tools used**: Python 3, `ruamel.yaml`

### `generate-service-details-docs.py`
- **Purpose**: Generates three types of output: (1) combined service reference, (2) per-layer service detail files, and (3) the composition reference document. Uses `docs/plugins/*.md` deep dives as primary source of truth (extracts `## Overview`, `> **Layer**:`, `> **Short**:`), with `schemas/*-api.yaml` providing version and endpoint counts. Also generates `GENERATED-COMPOSITION-REFERENCE.md` by combining sections from `BANNOU-ASPIRATIONS.md`, `ORCHESTRATION-PATTERNS.md`, and `SERVICE-HIERARCHY.md` with a service registry table built from deep dive `> **Short**:` fields.
- **Input**: `docs/plugins/*.md` (deep dives), `schemas/*-api.yaml` (for endpoint counts), `docs/maps/*.md` (for map link detection), `docs/BANNOU-ASPIRATIONS.md`, `docs/reference/ORCHESTRATION-PATTERNS.md`, `docs/reference/SERVICE-HIERARCHY.md`
- **Output**: `GENERATED-SERVICE-DETAILS.md` (combined), `GENERATED-{LAYER}-SERVICE-DETAILS.md` (5 per-layer files), `GENERATED-COMPOSITION-REFERENCE.md` (composition + orchestration + hierarchy + registry table)
- **Tools used**: Python 3, `ruamel.yaml` (583 lines)

### `generate-client-api-docs.py`
- **Purpose**: Generates client API reference showing all typed proxy methods available in the Client SDK. Includes quick reference table (service → proxy property → method count), C# usage examples, and per-service method tables grouped by tag with request/response types and access levels extracted from `x-permissions`.
- **Input**: All `schemas/*-api.yaml`
- **Output**: `docs/generated/GENERATED-CLIENT-API.md`
- **Tools used**: Python 3, `ruamel.yaml` (388 lines)

### `generate-client-events-docs.py`
- **Purpose**: Generates client events reference showing all subscribable events in the Client SDK. Includes quick reference table, C# subscription examples (with `ClientEventRegistry` usage), and per-service event details with property tables. Skips `x-internal: true` events and `BaseClientEvent`.
- **Input**: All `schemas/*-client-events.yaml`
- **Output**: `docs/generated/GENERATED-CLIENT-EVENTS.md`
- **Tools used**: Python 3, `ruamel.yaml` (322 lines)

### `generate-metadata-docs.py`
- **Purpose**: Generates No Metadata Bag Contracts compliance tracking document. Scans ALL schema files (not just API) for `additionalProperties: true` properties, checks each for compliance marker text ("no bannou plugin reads specific keys", "client-only", "opaque to all bannou plugins"), categorizes as compliant/non-compliant, groups by service layer. Includes hardcoded structural exceptions section documenting infrastructure primitives (State L0, Messaging L0, Connect L1), polymorphic response data, and behavior-authored content that are NOT metadata bags.
- **Input**: All `schemas/*.yaml` (excluding Generated/)
- **Output**: `docs/generated/GENERATED-METADATA-PROPERTIES.md` — Compliance summary, per-service property tables, structural exceptions, non-compliant property listing
- **Tools used**: Python 3, `ruamel.yaml` (420 lines)

### `generate-deprecation-docs.py`
- **Purpose**: Generates deprecation entities reference listing every entity with `deprecation: true` in its `x-lifecycle` definition. Tracks `instanceEntity` declarations and validates they reference actual `x-lifecycle` entities in the same events file. Reports compliance summary (with/without `instanceEntity`, invalid references) and per-entity detail sections with service, schema, topic prefix, and model fields.
- **Input**: All `schemas/*-service-events.yaml` with `x-lifecycle` blocks
- **Output**: `docs/generated/GENERATED-DEPRECATION-ENTITIES.md`
- **Tools used**: Python 3, `ruamel.yaml` (246 lines)

### ~~`generate-state-store-docs.py`~~ (DELETED)
- **Status**: Removed — orphaned dead code. Read Dapr-style component YAML files from `provisioning/state-stores/` (which no longer exists). Superseded by the dual-purpose `generate-state-stores.py` which reads `schemas/state-stores.yaml` and generates both `StateStoreDefinitions.cs` and `GENERATED-STATE-STORES.md`.

### `generate-env-example.py`
- **Purpose**: Generates `.env.example` file from all configuration schemas. Uses `example` values (priority over `default`), with smart secret detection (JWT, OAuth, API keys get appropriate placeholders), URL/connection string pattern detection, and category grouping (JWT, OAuth, State Store, Timing/TTL, etc.). Priority-ordered service sections (auth/account/connect first, then alphabetical). Appends service enable/disable flags and local development config sections.
- **Input**: All `schemas/*-configuration.yaml` (reads `x-service-configuration` blocks)
- **Output**: `.env.example` in repository root
- **Tools used**: Python 3, `ruamel.yaml` (382 lines)
- **Pipeline position**: Step 17 in `generate-all-services.sh`

### `generate-localization-categories.py`
- **Purpose**: Dual-purpose script that generates both C# code and markdown documentation from `schemas/localization-categories.yaml`. Generates category code constants, metadata with validation modes (`None`/`WarnOnMissing`/`RejectOnMissing`), and consumer declarations.
- **Input**: `schemas/localization-categories.yaml` (reads `x-localization-categories`)
- **Output**:
  - `bannou-service/Generated/LocalizationCategoryDefinitions.cs` — Static class with `const string` constants, `Metadata` dictionary with `LocalizationCategoryMetadata` records (description, validation mode, consumer list)
  - `docs/generated/GENERATED-LOCALIZATION-CATEGORIES.md` — Documentation with validation mode reference and structural enforcement details
- **Tools used**: Python 3, `ruamel.yaml` (258 lines)

---

## Document Catalog Generators

These Python scripts scan document directories, extract frontmatter metadata, and generate catalog index files for efficient documentation search.

### `generate-guides-catalog.py`
- **Purpose**: Generates the guides catalog from `docs/guides/*.md`.
- **Output**: `docs/generated/GENERATED-GUIDES-CATALOG.md`

### `generate-planning-catalog.py`
- **Purpose**: Generates the planning documents catalog from `docs/planning/*.md`. Groups documents by Type using prefix matching (Vision Documents, Design, Research, Implementation Plans, Architectural Analysis). Extracts Type, Status, Created, Last Updated, North Stars, Related Plugins metadata fields.
- **Output**: `docs/generated/GENERATED-PLANNING-CATALOG.md` (278 lines)

### `generate-faq-catalog.py`
- **Purpose**: Generates the FAQ catalog from `docs/faq/*.md`.
- **Output**: `docs/generated/GENERATED-FAQ-CATALOG.md`

### `generate-operations-catalog.py`
- **Purpose**: Generates the operations catalog from `docs/operations/*.md`. Excludes `CLAUDE-SKILLS.md`. Extracts Last Updated and Scope metadata fields.
- **Output**: `docs/generated/GENERATED-OPERATIONS-CATALOG.md` (212 lines)

### `generate-specifications-catalog.py`
- **Purpose**: Generates the specifications catalog from `docs/reference/specifications/*.md`. Extracts Version, Status, Last Updated, Schema Scope, and Generated Output metadata fields. Covers all `x-*` extension attribute specifications.
- **Output**: `docs/generated/GENERATED-SPECIFICATIONS-CATALOG.md` (216 lines)

### `generate-sdks-catalog.py`
- **Purpose**: Generates the SDKs catalog from `docs/sdks/*.md`. Groups by Domain, sorts within domain by Layer (Theory → Storyteller → Composer → Bridge → Infrastructure). Extracts SDK, Layer, Domain, Status, Dependencies, Consumers, Short metadata. Cross-references `docs/sdks/maps/` to detect which SDKs have implementation maps. Uses `## Overview` section (not `## Summary`) as the summary text.
- **Output**: `docs/generated/GENERATED-SDKS-CATALOG.md` (267 lines)

All catalog generators use Python 3 and produce markdown index files with summaries, metadata, and links.

---

## Bootstrapping & One-Time Generators

### `generate-project.sh`
- **Purpose**: Creates a new plugin project structure from scratch (directory, `.csproj`, `AssemblyInfo.cs`). Adds the project to the solution via `dotnet sln add`.
- **Input**: Service name
- **Output**: `plugins/lib-{service}/` directory with project files
- **Tools used**: bash, dotnet CLI
- **Pipeline position**: Step 1 in per-service generation order. One-time only (skips if directory exists).

### `generate-plugin.sh`
- **Purpose**: Generates the plugin registration wrapper (`{Service}ServicePlugin.cs`) for assembly loading and service discovery. **Never overwrites** existing files.
- **Input**: Service name
- **Output**: `plugins/lib-{service}/{Service}ServicePlugin.cs`
- **Tools used**: bash
- **Pipeline position**: Step 11 in per-service generation order. One-time only.

### `generate-tests.sh`
- **Purpose**: Creates a unit test project for a service (directory, `.csproj`, `AssemblyInfo.cs`, `GlobalUsings.cs`, initial test class). Adds to solution via `dotnet sln add`.
- **Input**: Service name
- **Output**: `plugins/lib-{service}.tests/` directory with test project files
- **Tools used**: bash, dotnet CLI
- **Pipeline position**: Step 13 in per-service generation order. One-time only (skips if directory exists).

### `generate-storyline-archives.sh`
- **Purpose**: Generates typed archive model classes for the storyline-theory SDK. Uses `extract-archive-schemas.py` to create a consolidated schema from response types marked with `x-archive-type: true`, then runs NSwag to generate C# models in the `BeyondImmersion.Bannou.StorylineTheory.Archives` namespace.
- **Input**: All `schemas/*-api.yaml` with `x-archive-type: true` on response schemas
- **Output**: Storyline-theory SDK archive type C# classes
- **Tools used**: bash, NSwag, python3 (`extract-archive-schemas.py`), post-processing
- **Pipeline position**: Called by `make generate` as step 2, also by `generate-all-services.sh` step 19

---

## Other Python Utilities

### `extract-sdk-types.py`
- **Purpose**: Extracts `x-sdk-type` annotations from API schemas. Produces exclusion lists and namespace imports consumed by `generate-models.sh` and `generate-config.sh` to avoid generating duplicate types that already exist in external SDKs.
- **Usage**: `python3 extract-sdk-types.py <schema-file> --format=shell`
- **Input**: A single `schemas/{service}-api.yaml`
- **Output**: Shell-parseable output (`EXCLUDED_TYPES=...` and `SDK_NAMESPACES=...`)
- **Tools used**: Python 3, yaml

### `extract-archive-schemas.py`
- **Purpose**: Extracts types marked with `x-archive-type: true` from a hardcoded list of archive-capable plugins (`character`, `character-personality`, `character-history`, `character-encounter`, `realm-history`). Collects each archive type with all its transitive dependencies (resolving cross-file refs), converts cross-file refs to local refs, and produces a consolidated schema for NSwag generation.
- **Usage**: `python3 scripts/extract-archive-schemas.py [--list]` (`--list` just prints found types without generating)
- **Input**: `schemas/{plugin}-api.yaml` for each archive plugin (prefers resolved versions from `schemas/Generated/`)
- **Output**: `schemas/Generated/storyline-archives.yaml` — Minimal OpenAPI schema with `x-archive-types` and `x-source-plugins` metadata fields
- **Tools used**: Python 3, `ruamel.yaml` (280 lines)

### `generate-service-navigator.py`
- **Purpose**: Generates `IServiceNavigator` partial interface and `ServiceNavigator` implementation by scanning `bannou-service/Generated/Clients/` for `I*Client` interfaces. Much more than a client aggregator — also provides session context, client event publishing, and node identity.
- **Input**: `bannou-service/Generated/Clients/*Client.cs` (discovers clients by scanning C# files for namespace extraction)
- **Output**: `bannou-service/Generated/IServiceNavigator.cs` and `bannou-service/Generated/ServiceNavigator.cs`
- **Generated capabilities**:
  - Service client properties (`navigator.Account`, `navigator.Auth`, etc.)
  - `InstanceId` from `IMeshInvocationClient` (node identity)
  - `GetRequesterSessionId()` / `GetCorrelationId()` / `HasClientContext` via `ServiceRequestContext` (AsyncLocal)
  - `PublishToRequesterAsync<TEvent>()` / `PublishToSessionAsync<TEvent>()` via `IClientEventPublisher`
  - Scoped DI registration for per-request isolation
  - Partial class design — generated code + manual `ServiceNavigator.RawApi.cs`
- **Tools used**: Python 3, regex for C# namespace extraction (355 lines)
- **Pipeline position**: Step 20 in `generate-all-services.sh`

### `print-model-shapes.py`
- **Purpose**: Prints compact model shapes for a service — a human-readable summary of all request/response models (~6x smaller than reading generated C# or YAML schemas). Shows required (`*`), nullable (`?`), and default (`= val`) markers. Handles enums (inline up to 8 values), arrays, dictionaries, `allOf` inheritance, and `$ref` resolution.
- **Usage**: `make print-models PLUGIN="character"` or `python3 scripts/print-model-shapes.py character`
- **Input**: Reads YAML schemas directly (NOT generated C#): `schemas/{service}-api.yaml`, `schemas/{service}-service-events.yaml`, `schemas/Generated/{service}-service-lifecycle-events.yaml`, `schemas/{service}-client-events.yaml`
- **Output**: stdout — Sectioned output (`[API]`, `[Events]`, `[Lifecycle Events]`, `[Client Events]`) with model/enum count summary on stderr. Models with ≤3 properties are inline; larger ones use multi-line format.
- **Tools used**: Python 3, `ruamel.yaml` (287 lines)

---

## Utility & Non-Generation Scripts

### `build-service-libs.sh`
- **Purpose**: Builds service plugin libraries as standalone DLLs. Supports building specific services (`./build-service-libs.sh auth account`) or all services. Uses single `dotnet restore` + `dotnet build` for efficiency, then copies service-specific DLLs. Requires `xmllint` and `rsync`.
- **Tools used**: bash, dotnet CLI, xmllint, rsync

### `check-editorconfig.sh`
- **Purpose**: Lightweight EditorConfig compliance checker for development (backup for when Docker/Super Linter is unavailable). Checks four things: CRLF line endings (should be LF), missing final newlines, tab characters in C# files (should be 4 spaces), and inconsistent indentation (non-multiples of 4). Excludes `Generated/`, `sdks/`, `bin/`, `obj/` directories. Reports first 5 violations per category.
- **Usage**: `make check` or `./scripts/check-editorconfig.sh`
- **Tools used**: bash, find, grep, awk, file

### `check-plugin-gaps.sh`
- **Purpose**: Counts actionable gaps in a plugin deep dive document. Scans gap sections (Stubs, Potential Extensions, Implementation Gaps, Bugs, Design Considerations) for numbered items, subtracts AUDIT markers and FIXED markers. Exit code 0 = actionable gaps exist (ready to audit), 1 = no actionable gaps (skip).
- **Usage**: `./scripts/check-plugin-gaps.sh docs/plugins/FACTION.md`
- **Output**: `TOTAL=N AUDIT=N FIXED=N ACTIONABLE=N`
- **Tools used**: bash, awk, grep

### `cleanup-test-containers.sh`
- **Purpose**: Removes leftover containers from failed/interrupted test runs. Targets `bannou-test-*`, `bannou-bannou-*`, `bannou-main*`, `bannou-auth*`, `bannou-edge*` containers. Also cleans up orphaned networks (`bannou-test-*`, `bannou_default`). Supports `--dry-run` and `--force` flags.
- **Usage**: `make test-cleanup`, `make test-cleanup-dry`, or `./scripts/cleanup-test-containers.sh [--dry-run] [--force]`
- **Tools used**: bash, docker CLI

### `fix-config.sh`
- **Purpose**: Fixes EditorConfig issues using `eclint`. Processes C#, Markdown, and YAML files (excluding `Generated/`, `sdks/`, `bin/`, `obj/`). Requires `eclint` (`npm install -g eclint`).
- **Usage**: `make fix-config` or `./scripts/fix-config.sh`
- **Tools used**: bash, eclint, find

### `fix-endings.sh`
- **Purpose**: Fixes line endings across the codebase. Converts CRLF → LF, removes trailing whitespace and whitespace-only lines from source code files (`.cs`, `.ts`, `.tsx`, `.js`, `.jsx`), and adds missing final newlines. Processes 14 file extensions (`.cs`, `.ts`, `.tsx`, `.js`, `.jsx`, `.md`, `.json`, `.yml`, `.yaml`, `.sh`, `.txt`, `.xml`, `.csproj`, `.sln`).
- **Usage**: `make fix-endings` or `./scripts/fix-endings.sh`
- **Tools used**: bash, find, sed, file, tail

### `infrastructure-tests.sh`
- **Purpose**: Runs minimal infrastructure integration tests against a running Bannou instance (bannou + rabbitmq + redis, NO databases, NO OpenResty). Tests health endpoint, TESTING plugin availability and execution, and environment configuration. Uses Docker DNS for service discovery (`BANNOU_HOST` env var, default `bannou`).
- **Usage**: `make test-infrastructure` (runs inside Docker container)
- **Tools used**: bash (sh), curl

### `install-dev-tools.sh`
- **Purpose**: Installs development/deployment dependencies. Full setup: Docker/Docker Compose, .NET 10 SDK (preview) + .NET 9 runtimes, NSwag CLI, Python with `ruamel.yaml`, Node.js 20 with eclint. Supports `--docker-only` for minimal deployment setup. Detects already-installed components and skips them.
- **Usage**: `./scripts/install-dev-tools.sh` (full) or `./scripts/install-dev-tools.sh --docker-only`
- **Tools used**: bash, apt/dnf, dotnet tool, pip, npm

### `list-services.sh`
- **Purpose**: Lists all plugin services by scanning `plugins/*.csproj` for `<ServiceLib>` properties. Displays service names and their containing project directories.
- **Usage**: `./list-services.sh`
- **Output**: Service names to stdout with usage examples for `make build-plugins`
- **Tools used**: bash, xmllint, find, grep

### `prepare-release.sh`
- **Purpose**: Prepares a new platform release. Validates semver format, updates `VERSION` file, moves `CHANGELOG.md` `[Unreleased]` section to the new version section, and prints next steps.
- **Usage**: `./scripts/prepare-release.sh <new-version>` (e.g., `./scripts/prepare-release.sh 0.10.0`)
- **Tools used**: bash, sed

### `prepare-test-fixtures.sh`
- **Purpose**: Prepares test fixtures by copying git repo fixtures from `tools/http-tester/fixtures/` to a temp directory, renaming `dot_git` → `.git` and `gitmodules` → `.gitmodules` (following the LibGit2Sharp convention for storing git repos in source control). Validates the resulting git repository is valid.
- **Usage**: `./scripts/prepare-test-fixtures.sh [destination-dir]` (default: `/tmp/bannou-test-fixtures`)
- **Tools used**: bash, git

### `run-tests.sh`
- **Purpose**: Runs unit tests across all service plugins, or for a specific plugin. Discovers all `*.tests.csproj` projects automatically.
- **Usage**: `./scripts/run-tests.sh [plugin-name]` (e.g., `./scripts/run-tests.sh account`)
- **Tools used**: bash, dotnet test, find

### `select-plugins-for-audit.sh`
- **Purpose**: Selects plugins with actionable gaps for batch auditing. Scans all `docs/plugins/*.md` deep dives using `check-plugin-gaps.sh`, identifies plugins with `ACTIONABLE > 0`, then randomly selects N (or all) for audit. Skips `DEEP-DIVE-TEMPLATE.md` and `WEBSITE.md`. Used by the `orchestrate-skill` skill for batched audit-plugin runs.
- **Usage**: `./scripts/select-plugins-for-audit.sh [count]` (0 or omit = all with gaps)
- **Output**: `SCAN:` lines per plugin, `SELECTED:` lines for chosen plugins, `SUMMARY:` totals
- **Tools used**: bash, shuf (random selection)

### `show-help.sh`
- **Purpose**: Displays organized help for all Makefile commands. Categorized into Core Development, Docker & Compose, Service Management, Testing, Test Cleanup, Code Quality, and Git & Versioning. Includes usage examples and tips.
- **Usage**: `make help` or `./scripts/show-help.sh`
- **Tools used**: bash (heredoc output)

### `audit-file-sizes.sh`
- **Purpose**: Audits file sizes across the codebase to identify splitting candidates. Reports: top N largest non-generated C# files, all non-generated C# files over a threshold, reference docs over a threshold, generated docs over a threshold, deep dives over a threshold, implementation maps over a threshold, and FAQs over a separate (lower) threshold. Excludes `Generated/`, `bin/`, `obj/`, `.git/`, `.claude/` directories. All thresholds configurable via flags.
- **Usage**: `make audit-sizes` or `./scripts/audit-file-sizes.sh [--top N] [--cs-threshold N] [--doc-threshold N] [--faq-threshold N] [--cs] [--docs] [--summary] [--all]`
- **Makefile**: `make audit-sizes [TOP=N] [CS=N] [DOC=N] [FAQ=N]`
- **Tools used**: bash, find, wc, sort

### `validate-compose-services.sh`
- **Purpose**: Validates that specific services are included in the latest Docker image by reading `/PLUGIN_MANIFEST.txt` from inside the container. If no services specified, shows all plugins in the image. Reports missing services with rebuild instructions.
- **Usage**: `make validate-compose-services SERVICES="auth account connect"` or `./scripts/validate-compose-services.sh [service1] [service2] ...`
- **Tools used**: bash, docker (run --rm --entrypoint cat)

### `wait-for-health.sh`
- **Purpose**: Waits for the Bannou service health endpoint (`/health`) to return HTTP 200. Retries every 5 seconds up to 60 times (5 minutes). Uses Docker DNS (`BANNOU_HOST` env var, default `bannou`). Exits on unexpected response codes (not just timeouts).
- **Tools used**: bash (sh), curl

### `wait-for-infrastructure.sh`
- **Purpose**: Waits for infrastructure services (RabbitMQ, Redis, MySQL) to accept TCP connections. Total timeout across all services (default 60s, configurable via `TOTAL_TIMEOUT`). All hosts configurable via env vars (`RABBITMQ_HOST`, `REDIS_HOST`, `MYSQL_HOST`). Can be used as entrypoint wrapper — passes remaining args to `exec "$@"` after infrastructure is ready. Skips services with host set to `disabled` or empty.
- **Status**: Not currently referenced by any Docker Compose file or Makefile target. Previously used as a container startup mechanism. Retained as a utility for standalone/sidecar deployments and CI environments without Docker Compose healthchecks.
- **Tools used**: sh, nc (netcat)

---

## Pipeline Execution Order

The complete `make generate` pipeline executes in this order:

### Phase 1: `generate-all-services.sh` (Service Code Generation)

```
1.  generate-state-stores.py          → StateStoreDefinitions.cs + docs
2.  generate-variable-providers.py    → VariableProviderDefinitions.cs + docs
3.  generate-lifecycle-events.py      → schemas/Generated/*-lifecycle-events.yaml
4.  generate-resource-mappings.py     → ResourceEventMappings.cs
5.  generate-resource-templates.py    → ABML archive templates
6.  generate-common-api.sh            → CommonApiModels.cs
7.  generate-common-events.sh         → CommonEventsModels.cs
8.  generate-service-events.sh        → {Service}EventsModels.cs + LifecycleEvents.cs
     ├── generate-lifecycle-events.py  (pre-processing)
     └── resolve-schema-refs.py        (pre-processing: --events --lifecycle)
9.  generate-published-topics.py      → {Service}PublishedTopics.cs
10. generate-event-publishers.py      → {Service}EventPublisher.cs
11. generate-client-events.sh         → CommonClientEventsModels.cs + {Service}ClientEventsModels.cs
12. generate-client-event-whitelist.sh → Connect event whitelist
13. generate-event-subscription-registry.sh → Event type registry
14. embed-meta-schemas.py             → schemas/Generated/*-api-meta.yaml

    ┌─ Per-service loop (for each schemas/*-api.yaml) ─────────────────┐
    │ generate-service.sh calls in order:                               │
    │  1. generate-project.sh          → Plugin project structure       │
    │  2. generate-models.sh           → {Service}Models.cs             │
    │     ├── resolve-schema-refs.py      (pre-processing)                 │
    │     ├── extract-sdk-types.py     (SDK type exclusions)            │
    │     └── generate_lifecycle_event_models() (from common.sh)        │
    │  3. generate-controller.sh       → {Service}Controller.cs         │
    │  4. generate-meta-controller.sh  → {Service}Controller.Meta.cs    │
    │  5. generate-event-subscriptions.sh → {Service}ServiceEvents.cs   │
    │  6. generate-client.sh           → {Service}Client.cs             │
    │  7. generate-interface.sh        → I{Service}Service.cs           │
    │  8. generate-config.sh           → {Service}ServiceConfiguration  │
    │  9. generate-implementation.sh   → {Service}Service.cs (one-time) │
    │ 10. generate-models-internal.sh  → {Service}ServiceModels.cs      │
    │ 11. generate-plugin.sh           → {Service}ServicePlugin.cs      │
    │ 12. generate-permissions.sh      → {Service}PermissionRegistration│
    │ 13. generate-tests.sh            → Test project (one-time)        │
    └───────────────────────────────────────────────────────────────────┘

    Global post-processing (all Generated/*.cs files):
    - Fix AdditionalProperties lazy initialization
    - Remove NSwag pragma warning blocks
    - Remove null checks on enum parameters
    - Replace hardcoded "bannou" app-id defaults

17. generate-env-example.py           → .env.example
18. generate-client-proxies.py        → Client SDK typed proxies
19. generate-storyline-archives.sh    → Storyline archive types
20. generate-service-navigator.py     → IServiceNavigator + ServiceNavigator
21. generate-references.py            → {Service}ReferenceTracking.cs
22. generate-compression-callbacks.py → {Service}CompressionCallbacks.cs
23. generate-event-templates.py       → {Service}EventTemplates.cs
24. generate-client-event-registry.py → Client event registry (.NET)
25. generate-client-event-groups.py   → Service-grouped events
26. generate-client-endpoint-metadata.py → Client endpoint metadata
```

### Phase 2: Storyline Archives
```
generate-storyline-archives.sh       → Storyline-theory SDK types
```

### Phase 3: .NET Client SDK
```
generate-client-sdk.sh               → BeyondImmersion.Bannou.Client assembly
```

### Phase 4: TypeScript SDK
```
generate-client-schema.py            → Consolidated client schema
npm run generate (TS SDK)             → TypeScript types + proxies
```

### Phase 5: Unreal Engine SDK
```
generate-client-schema.py            → Consolidated client schema (shared with Phase 4)
generate-unreal-sdk.py               → UE SDK headers
  ├── generate-unreal-types.py       → BannouTypes.h (USTRUCTs)
  └── generate-unreal-protocol.py    → BannouProtocol.h + BannouEndpoints.h
```

### Phase 6: Documentation
```
generate-docs.sh orchestrates:
  generate-state-stores.py           → GENERATED-STATE-STORES.md
  generate-variable-providers.py     → GENERATED-VARIABLE-PROVIDERS.md
  generate-localization-categories.py → GENERATED-LOCALIZATION-CATEGORIES.md
  generate-event-docs.py             → GENERATED-EVENTS.md
  generate-config-docs.py            → GENERATED-CONFIGURATION.md
  generate-service-details-docs.py   → GENERATED-*-SERVICE-DETAILS.md (per-layer)
  generate-client-api-docs.py        → Client API reference
  generate-client-events-docs.py     → GENERATED-CLIENT-EVENTS.md
  generate-metadata-docs.py          → Metadata properties reference
  generate-deprecation-docs.py       → Deprecation entities reference
  generate-guides-catalog.py         → GENERATED-GUIDES-CATALOG.md
  generate-planning-catalog.py       → GENERATED-PLANNING-CATALOG.md
  generate-faq-catalog.py            → GENERATED-FAQ-CATALOG.md
  generate-operations-catalog.py     → GENERATED-OPERATIONS-CATALOG.md
  generate-specifications-catalog.py → GENERATED-SPECIFICATIONS-CATALOG.md
  generate-sdks-catalog.py           → GENERATED-SDKS-CATALOG.md
```

### Granular Regeneration Commands

For targeted regeneration when only specific schema types changed:

| What Changed | Command | What It Runs |
|---|---|---|
| `{service}-configuration.yaml` | `cd scripts && ./generate-config.sh {service}` | Config generator only |
| `{service}-service-events.yaml` | `scripts/generate-service-events.sh {service}` | Lifecycle events + event models |
| `{service}-api.yaml` (models only) | `cd scripts && ./generate-models.sh {service}` | Model generation + lifecycle events |
| `{service}-client-events.yaml` | `scripts/generate-client-events.sh {service}` | Client event models |
| Multiple schema types for one service | `cd scripts && ./generate-service.sh {service}` | All per-service generators |
| `common-api.yaml` or multiple services | `scripts/generate-all-services.sh` | Full pipeline |
| `x-event-publications` changed | Also run `python3 scripts/generate-published-topics.py` + `python3 scripts/generate-event-publishers.py` | Topic constants + typed publishers |

**Order dependency**: If both API and event schemas changed for the same service, generate models FIRST, then events. The `generate-all-services.sh` script handles this automatically.
