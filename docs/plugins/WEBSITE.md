# Website Plugin Deep Dive

> **Plugin**: lib-website
> **Schema**: schemas/website-api.yaml
> **Version**: 1.0.0
> **State Store**: None (no state stores defined)

---

## Overview

Public-facing website service (L3 AppFeatures) for browser-based access to news, account profiles, game downloads, CMS pages, and contact forms. Intentionally does NOT access game data (characters, subscriptions, realms) to respect the service hierarchy. Uses traditional REST HTTP methods (GET, PUT, DELETE) with path parameters for browser compatibility, which is an explicit exception to Bannou's POST-only pattern. **Currently a complete stub** -- every endpoint returns `NotImplemented`. When implemented, will require lib-account integration and state stores for CMS data.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-messaging (`IMessageBus`) | Error event publishing via `TryPublishErrorAsync` |
| lib-messaging (`IEventConsumer`) | Event handler registration (constructor call, no handlers registered) |

**Note**: The service currently has no dependency on `IStateStoreFactory`, generated service clients, or any other infrastructure libs. When implemented, it will require `IAccountClient` (for profile retrieval) and `IStateStoreFactory` for CMS page persistence. Per the service hierarchy (L3 cannot depend on L2 game services), this service intentionally does NOT use `ICharacterClient`, `ISubscriptionClient`, or `IRealmClient`.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| None identified | No other service references `IWebsiteClient` or `WebsiteClient` in its service implementation |

The generated `WebsiteClient` exists in `bannou-service/Generated/Clients/WebsiteClient.cs` and is registered in the `IServiceNavigator`, but no plugin currently consumes it. The website service is a terminal endpoint intended for browser clients, not inter-service communication.

---

## State Storage

**No state stores are defined for this service.**

The `schemas/state-stores.yaml` file contains no entries for "website". When CMS functionality is implemented, state stores will be needed for:

| Future Key Pattern | Data Type | Purpose |
|-------------------|-----------|---------|
| `page:{slug}` | `PageContent` | Individual CMS page content |
| `news:{newsId}` | `NewsItem` | News articles |
| `site-settings` | `SiteSettings` | Global site configuration |
| `theme` | `ThemeConfig` | Visual theme configuration |
| `contact:{ticketId}` | `ContactRequest` | Submitted contact forms |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| *(none)* | | Permission registration is now DI-based via `IPermissionRegistry` |
| `error.*` | Error event (via `TryPublishErrorAsync`) | On unhandled exceptions in any endpoint |

No domain-specific events are published (all endpoints are stubs).

### Consumed Events

This plugin does not consume external events. The `RegisterEventConsumers` call in the constructor is a no-op (no event handlers are registered).

**Note**: No `schemas/website-events.yaml` file exists. When implemented, the service may publish events like `website.contact-submitted`, `website.page-created`, `website.page-updated`, `website.page-deleted`.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| (none) | -- | -- | Configuration class is empty (only `ForceServiceId` from `IServiceConfiguration`) |

The `schemas/website-configuration.yaml` defines no properties (`x-service-configuration: { properties: {} }`). The generated `WebsiteServiceConfiguration` contains only the inherited `ForceServiceId` property. The configuration is injected but never referenced in the service implementation (dead config violation, acceptable for stub services).

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<WebsiteService>` | Scoped | Structured logging (warning for stub calls, error for exceptions) |
| `WebsiteServiceConfiguration` | Singleton | Configuration (currently unused) |
| `IMessageBus` | Scoped | Error event publishing |
| `IEventConsumer` | Scoped | Event handler registration (no handlers) |

Service lifetime is **Scoped** (per-request). No background services. Plugin discovery is via `WebsiteServicePlugin : StandardServicePlugin<IWebsiteService>` with `PluginName = "website"`.

---

## API Endpoints (Implementation Notes)

**CRITICAL: ALL 14 endpoints are stubs.** All methods log a debug message and return `(StatusCodes.NotImplemented, null)`. The error handling catch blocks call `TryPublishErrorAsync` but can never be reached because the try blocks contain only a log statement and return.

### Status Endpoint (1 endpoint)

- **GetStatus** (`GET /website/status`): Returns service health, version, and uptime. Access: anonymous. **Stub** -- returns NotImplemented. When implemented, should return actual service health metrics, version from assembly, and calculated uptime.

### Content Endpoints (2 endpoints)

- **GetPageContent** (`GET /website/content/{slug}`): Retrieves CMS page by URL slug. Access: anonymous. Path parameter `slug` must match pattern `^[a-z0-9-]+$`. **Stub** -- logs the slug but returns NotImplemented. When implemented, will need a state store for page data with slug-based lookups.

- **GetNews** (`GET /website/news`): Paginated news listing. Access: anonymous. Query params: `limit` (1-50, default 10), `offset` (min 0, default 0). **Stub** -- returns NotImplemented. When implemented, will need a state store for news items with ordering by publish date.

### Downloads Endpoint (1 endpoint)

- **GetDownloads** (`GET /website/downloads`): Lists game client downloads, optionally filtered by platform (windows/macos/linux). Access: anonymous. **Stub** -- returns NotImplemented. When implemented, may read from configuration or asset service for download URLs, checksums, and version info.

### Contact Endpoint (1 endpoint)

- **SubmitContact** (`POST /website/contact`): Submits a contact form. Access: anonymous. This is the only POST endpoint in the public-facing API. Request body: `ContactRequest` with required email, subject (5-200 chars), message (10-2000 chars), optional name and category (general/support/bug/feedback/business). Schema defines 429 Too Many Requests response. **Stub** -- returns NotImplemented. When implemented, should generate a ticket ID, store the contact request, and potentially publish an event or send email notification.

### Account Endpoint (1 endpoint)

- **GetAccountProfile** (`GET /website/account/profile`): Retrieves logged-in user's account profile. Access: user (requires BearerAuth). **Stub** -- returns NotImplemented. When implemented, will need to resolve the authenticated user's identity and call `IAccountClient` for account data.

**Note**: Game-specific account endpoints (characters, subscriptions) were intentionally removed from the Website schema to respect the L3/L2 service hierarchy boundary. Character and subscription data should be accessed through a future L4 "game-portal" service or directly via game clients.

### CMS Endpoints (7 endpoints)

- **ListPages** (`GET /website/cms/pages`): Lists all CMS pages with optional `includeUnpublished` filter (default false). Access: developer. **Stub** -- returns NotImplemented. Returns `ICollection<PageMetadata>` (slug, title, published status, dates, author).

- **CreatePage** (`POST /website/cms/pages`): Creates a new CMS page. Access: developer. Request body: `PageContent` with slug, title, content, contentType (html/markdown/blazor), published flag, optional SEO metadata. **Stub** -- returns NotImplemented.

- **UpdatePage** (`PUT /website/cms/pages/{slug}`): Updates an existing CMS page by slug. Access: developer. Path param: slug, request body: `PageContent`. **Stub** -- logs slug but returns NotImplemented.

- **DeletePage** (`DELETE /website/cms/pages/{slug}`): Deletes a CMS page by slug. Access: developer. Returns only a status code (no response body). **Stub** -- logs slug but returns NotImplemented.

- **GetSiteSettings** (`GET /website/cms/site-settings`): Retrieves global site configuration (name, URL, language, maintenance mode, social links, analytics, custom scripts). Access: developer. **Stub** -- returns NotImplemented.

- **UpdateSiteSettings** (`PUT /website/cms/site-settings`): Updates global site configuration. Access: developer (requires BearerAuth). Request body: `SiteSettings`. **Stub** -- returns NotImplemented.

- **GetTheme** (`GET /website/cms/theme`): Retrieves visual theme configuration (colors, fonts, logo, navigation). Access: developer. **Stub** -- returns NotImplemented.

- **UpdateTheme** (`PUT /website/cms/theme`): Updates visual theme configuration. Access: developer (requires BearerAuth). Request body: `ThemeConfig`. Returns only status code (no response body on the interface method `Task<StatusCodes>`). **Stub** -- returns NotImplemented.

---

## Visual Aid

```
Website Service Architecture (Planned)
========================================

  Browser / Game Launcher
       │
       ├── GET /website/status
       ├── GET /website/news
       ├── GET /website/downloads
       ├── GET /website/content/{slug}
       ├── POST /website/contact
       │
       │   (Authenticated via JWT)
       ├── GET /website/account/profile
       │
       │   (Developer access)
       ├── GET/POST /website/cms/pages
       ├── PUT/DELETE /website/cms/pages/{slug}
       ├── GET/PUT /website/cms/site-settings
       └── GET/PUT /website/cms/theme
              │
              ▼
     ┌─────────────────────────────────────┐
     │         WebsiteService              │
     │   (L3 App Feature - Pure CMS)       │
     │   (ALL STUBS - NotImplemented)      │
     │                                     │
     │   Dependencies (current):           │
     │   ├── IMessageBus (error events)    │
     │   └── IEventConsumer (unused)       │
     │                                     │
     │   Dependencies (future):            │
     │   ├── IStateStoreFactory            │
     │   └── IAccountClient (L1)           │
     │                                     │
     │   ❌ NO L2 game service deps:       │
     │   ├── ICharacterClient              │
     │   ├── ISubscriptionClient           │
     │   └── IRealmClient                  │
     └─────────────────────────────────────┘


HTTP Methods in Use (Unique to Website Service)
=================================================

  Standard Bannou Service:     Website Service:
  ┌──────────────────────┐     ┌─────────────────────────────────────┐
  │ POST /service/get    │     │ GET  /website/status                │
  │ POST /service/create │     │ GET  /website/content/{slug}        │
  │ POST /service/update │     │ GET  /website/news?limit=&offset=   │
  │ POST /service/delete │     │ GET  /website/downloads?platform=   │
  └──────────────────────┘     │ POST /website/contact               │
       (POST only)             │ GET  /website/account/profile       │
                               │ POST /website/cms/pages             │
                               │ PUT  /website/cms/pages/{slug}      │
                               │ DELETE /website/cms/pages/{slug}    │
                               │ PUT  /website/cms/site-settings     │
                               │ PUT  /website/cms/theme             │
                               └─────────────────────────────────────┘
                                    (GET, POST, PUT, DELETE)


Permission Matrix
==================

  anonymous ──► status, content/{slug}, news, downloads, contact

  user ────────► account/profile

  developer ───► cms/pages (GET, POST, PUT, DELETE),
                  cms/site-settings (GET, PUT),
                  cms/theme (GET, PUT)


Generated Model Hierarchy
===========================

  SiteSettings
  ├── siteName (required)
  ├── siteUrl (required, URI)
  ├── defaultLanguage (required, default "en")
  ├── supportedLanguages[]
  ├── maintenanceMode, maintenanceMessage
  ├── contactEmail, socialLinks{}
  ├── analytics: Analytics
  │   ├── googleAnalyticsId
  │   └── otherTrackers{}
  └── customScripts: CustomScripts
      ├── head, bodyStart, bodyEnd

  ThemeConfig
  ├── themeName (required)
  ├── primaryColor (required, hex)
  ├── secondaryColor, backgroundColor, textColor (hex)
  ├── fontFamily, customCSS
  ├── logo: Logo { url, alt }
  ├── favicon (URI)
  └── navigation[]: NavigationItem
      ├── label, url, order (required)
      ├── target (_self|_blank)
      └── children[]: NavigationItem (recursive)

  PageContent
  ├── slug (required, pattern: ^[a-z0-9-]+$)
  ├── title, content (required)
  ├── contentType (html|markdown|blazor, required)
  ├── template, published, publishedAt
  ├── lastModified, author, metadata{}
  └── seo: SEOMetadata
      ├── description, keywords[]
      └── ogTitle, ogDescription, ogImage
```

---

## Stubs & Unimplemented Features

1. **ALL 14 endpoints are stubs**: Every method in `WebsiteService.cs` immediately logs a debug message and returns `(StatusCodes.NotImplemented, null)`. There is zero business logic.

2. **No state store**: The `schemas/state-stores.yaml` has no website entries. CMS pages, news items, site settings, and theme configuration have nowhere to persist.

3. **No service client dependencies**: The service does not inject `IAccountClient`. The account profile endpoint cannot function without this. Note: per service hierarchy, Website (L3) intentionally does NOT depend on L2 game services like Character, Subscription, or Realm.

4. **No authentication context**: The service has no mechanism to identify the authenticated user (no `IHttpContextAccessor`, no JWT claims extraction). The `user`-role endpoints cannot determine whose profile/characters/subscription to return.

5. **Dead error handling**: The catch blocks in every method call `TryPublishErrorAsync` but can never be reached -- the preceding try block contains only a log statement and a return. The pattern (`LogDebug` then return) cannot throw.

6. **No event publishing for domain actions**: When implemented, CMS page creation/update/deletion should publish domain events. No events schema exists (`schemas/website-events.yaml` is missing).

7. **No rate limiting for contact form**: The schema defines a 429 response for `/website/contact`, but no rate limiting mechanism is implemented.

8. **No test coverage**: `WebsiteServiceTests.cs` contains only a constructor test verifying the service can be instantiated without throwing.

9. **Configuration is empty**: `WebsiteServiceConfiguration` has no properties. When implemented, it will need settings like default pagination limits, maintenance mode flags, allowed content types, rate limit thresholds, etc.

---

## Potential Extensions

1. **CMS state store implementation**: Add `website-pages`, `website-news`, `website-settings` to `schemas/state-stores.yaml` and implement CRUD operations with slug-based indexing.

2. **Service client integration**: Inject `IAccountClient` (L1) to power the account profile endpoint. Note: L2 game service clients are intentionally excluded per service hierarchy.

3. **Authentication context**: Add `IHttpContextAccessor` to extract JWT claims (account ID) for authenticated endpoints.

4. **Rate limiting**: Implement contact form rate limiting per IP/session using Redis-backed counters or a dedicated rate-limit state store.

5. **Events schema**: Create `schemas/website-events.yaml` with events for page lifecycle, contact submissions, and settings changes.

6. **Configuration properties**: Add pagination defaults, maintenance mode control, CMS content size limits, allowed HTML tags (sanitization), and contact form rate limit thresholds.

7. **Content rendering**: The `contentType` field supports html/markdown/blazor. A rendering pipeline could transform markdown to HTML server-side.

8. **SEO and sitemap generation**: The PageContent model includes SEO metadata. A sitemap.xml endpoint could auto-generate from published pages.

9. **News feed integration**: RSS/Atom feed generation from news items for syndication.

10. **i18n support**: The `SiteSettings` schema defines `supportedLanguages` -- multi-language content delivery could be built on top of the slug-based page system.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None currently identified.

### Intentional Quirks (Documented Behavior)

1. **Path params on GET endpoints**: The `content/{slug}` and `cms/pages/{slug}` endpoints use path parameters, which means they cannot be routed via the WebSocket binary protocol (which requires static POST paths for GUID mapping). This service is accessed directly via HTTP, not through the Connect gateway.

2. **No NotImplemented mapping in ConvertToActionResult**: The generated `ConvertToActionResult` switch expression has no case for `StatusCodes.NotImplemented`. Since all stubs currently return NotImplemented, the controller will fall through to the `_ => StatusCode(500, result)` default case, meaning clients receive 500 Internal Server Error instead of 501 Not Implemented.

3. **Scaffolded error handling in stub methods**: Every endpoint method has a try-catch block where the catch cannot currently be reached (the try block contains only `LogDebug` + return, which cannot throw). This is intentional scaffolding - when real business logic is added to the try blocks, the error handling structure with `TryPublishErrorAsync` will become reachable without requiring structural changes.

### Design Considerations (Requires Planning)

1. **No WebSocket access**: Because this service uses REST HTTP methods, it cannot be accessed via the Connect WebSocket gateway. This means browser clients must make direct HTTP requests to the Bannou service, which has implications for authentication (must use Bearer tokens, not WebSocket session tokens).

2. **CMS content storage strategy**: The `PageContent.content` field stores raw HTML/markdown/blazor template strings. Large pages could exceed reasonable Redis value sizes. Consider whether MySQL-backed state stores are more appropriate for CMS content.

3. **NavigationItem recursive structure**: The `ThemeConfig.navigation` array contains `NavigationItem` objects which can have `children` arrays of the same type. This creates an unbounded recursive structure. When implementing, a depth limit should be enforced.

4. **No versioning for CMS content**: Unlike the save-load service which has versioned saves, the CMS page system has no version history. Accidental page overwrites via PUT would be unrecoverable.

5. **Contact form spam prevention**: The schema includes 429 rate limiting but no CAPTCHA or honeypot field. Anonymous access to the contact endpoint makes it a spam target.

6. **Missing `IStateStoreFactory` dependency**: The service does not even inject the state store factory. Implementation will require a constructor signature change and re-registration of the service.

7. **Schema defines `additionalProperties: false` globally**: All response models forbid additional properties. This means future schema evolution (adding fields) requires client updates -- no backwards-compatible field additions are possible without a version bump.

8. **Non-nullable collection fields default to null**: Multiple `ICollection<T>` fields like `Keywords`, `SupportedLanguages`, `SocialLinks`, `Navigation`, `Tags`, and `Children` are typed as non-nullable but assigned `= default!` in the generated models. Accessing these without initialization throws `NullReferenceException`.

9. **Non-nullable nested objects default to null**: Fields like `Seo`, `Logo`, `Analytics`, and `CustomScripts` in WebsiteModels.cs are typed as non-nullable references but assigned `= default!`. Since they're not in the schema's `required` array, they'll be null if not provided in JSON.

10. **`Size` field is `int` limiting downloads to ~2GB**: The `DownloadInfo.Size` property is `int`, not `long`. This limits represented file sizes to ~2GB, which may be insufficient for large game clients.

11. **Metadata stored as untyped `object`**: Both `PageContent.Metadata` and `Analytics.OtherTrackers` are typed as `object`, meaning they accept any JSON but provide no type safety.

12. **Dead configuration reference**: The `_configuration` field is assigned but never used. Acceptable for a stub service, but should be wired up when implementing.

13. **Missing XML documentation**: Several public methods lack `<param>` and `<returns>` tags. Acceptable for a stub service, but should be completed when implementing.

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow.

*No active work items.*
