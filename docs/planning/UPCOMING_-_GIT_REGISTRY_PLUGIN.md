# Git Registry Plugin - Self-Hosted Git Server for Bannou

> **Status**: Ready for Implementation
> **Last Updated**: 2025-12-27
> **Estimated Effort**: 6-8 weeks
> **Unique Value**: WebSocket-based real-time repository sync (GitHub cannot offer this)

---

## Executive Summary

Implement a self-hosted Git server as a Bannou plugin using `git.exe` process execution for protocol handling, with a comprehensive repository management API and WebSocket-based real-time synchronization.

| Aspect | Assessment |
|--------|------------|
| **Feasibility** | High - Well-documented protocols, proven patterns (Bonobo Git Server) |
| **Complexity** | Medium-High - Protocol details require careful implementation |
| **Time Estimate** | 6-8 weeks for push/pull/clone + real-time sync + management API |
| **Unique Value** | High - Instant WebSocket sync is genuinely novel |
| **CI/CD Trade-off** | Significant - Plan hybrid approach with GitHub mirroring |

### Recommended Approach

Use `git.exe` process execution for Git operations (like Bonobo Git Server) while implementing:
1. Repository management API (schema-first, POST-only)
2. Git Smart HTTP protocol handler (separate controller)
3. WebSocket-based real-time sync via lib-messaging events

---

## Git Smart HTTP Protocol

### Protocol Flow

```
FETCH (git clone/pull):
1. GET /info/refs?service=git-upload-pack → Server returns refs + capabilities
2. POST /git-upload-pack → Client sends want/have, Server returns packfile

PUSH (git push):
1. GET /info/refs?service=git-receive-pack → Server returns refs + capabilities
2. POST /git-receive-pack → Client sends ref updates + packfile, Server applies
```

### Packet-Line Format

All Git protocol communication uses "pkt-line" framing:
```
Format: LLLL<data>
- LLLL = 4 hex digits (total line length including LLLL)
- 0000 = flush (end of message)
- 0001 = delimiter (section separator)
- 0002 = response end

Example:
  001e# service=git-upload-pack\n
  0000
  004895dcfa3633004da0049d3d0fa03f80589cbcaf31 refs/heads/master\0multi_ack\n
```

### Content Types

| Endpoint | Response Content-Type |
|----------|----------------------|
| `/info/refs?service=git-upload-pack` | `application/x-git-upload-pack-advertisement` |
| `/info/refs?service=git-receive-pack` | `application/x-git-receive-pack-advertisement` |
| `/git-upload-pack` | `application/x-git-upload-pack-result` |
| `/git-receive-pack` | `application/x-git-receive-pack-result` |

### Protocol v2 Support

Git Protocol v2 (Git 2.18+) improves performance significantly:
- Request header: `Git-Protocol: version=2`
- On-demand ref listing via `ls-refs` command
- ~3x improvement for no-op fetches

---

## Implementation Approach: Process Execution

Execute `git upload-pack` and `git receive-pack` as external processes:

```csharp
public async Task<Stream> HandleUploadPackAsync(string repositoryPath, Stream requestBody)
{
    var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = _configuration.GitExecutablePath,
            Arguments = $"upload-pack --stateless-rpc \"{repositoryPath}\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };

    process.Start();
    await requestBody.CopyToAsync(process.StandardInput.BaseStream);
    process.StandardInput.Close();

    return process.StandardOutput.BaseStream;
}
```

**Why process execution over LibGit2Sharp:**
- LibGit2Sharp does not implement server-side protocol (confirmed by maintainers)
- 100% Git compatibility guaranteed
- Automatic support for new Git features
- Proven by Bonobo Git Server (production-tested)

---

## Service Architecture

### Plugin Structure

```
lib-git/
├── Generated/                           # NSwag auto-generated
│   ├── GitController.cs                 # Repository management API
│   ├── IGitService.cs
│   ├── GitClient.cs
│   ├── GitModels.cs
│   └── GitServiceConfiguration.cs
├── GitService.cs                        # Business logic (manual)
├── GitServicePlugin.cs                  # Plugin registration
├── Protocol/                            # Git protocol (manual, not schema-generated)
│   ├── GitProtocolController.cs         # /git/{owner}/{repo}.git/* endpoints
│   ├── PacketLineParser.cs              # pkt-line format parsing
│   ├── GitProcessExecutor.cs            # git.exe process management
│   └── RepositoryManager.cs             # Bare repository operations
└── lib-git.csproj
```

### Integration with Bannou Services

```
┌────────────────────────────────────────────────────────────────────┐
│                        Bannou Architecture                          │
├────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  ┌──────────┐     ┌──────────┐     ┌─────────────────────────────┐ │
│  │   Auth   │────►│ Connect  │────►│     Git Service Plugin      │ │
│  │ Service  │     │ Service  │     │                             │ │
│  └──────────┘     └──────────┘     │  ┌───────────────────────┐  │ │
│       │                │           │  │  Repository Manager   │  │ │
│       │                │           │  │  (lib-state store)    │  │ │
│       ▼                ▼           │  └───────────────────────┘  │ │
│  ┌──────────────────────────┐     │           │                  │ │
│  │    Permission Service    │     │           ▼                  │ │
│  │  - git.repo.read         │◄────│  ┌───────────────────────┐  │ │
│  │  - git.repo.write        │     │  │  Git Protocol Handler │  │ │
│  │  - git.repo.admin        │     │  └───────────────────────┘  │ │
│  └──────────────────────────┘     │           │                  │ │
│                                   │           ▼                  │ │
│                                   │  ┌───────────────────────┐  │ │
│                                   │  │   git.exe process     │  │ │
│                                   │  └───────────────────────┘  │ │
│                                   └─────────────────────────────┘ │
│                                                                     │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │                 lib-messaging Events                         │   │
│  │  - git.repository.created                                    │   │
│  │  - git.push.received (with commit details)                   │   │
│  │  - git.ref.updated                                           │   │
│  └─────────────────────────────────────────────────────────────┘   │
└────────────────────────────────────────────────────────────────────┘
```

### State Management

**Repository Metadata** (lib-state with `git-statestore`):
```csharp
private readonly IStateStore<RepositoryMetadata> _repoStore;

public GitService(IStateStoreFactory stateStoreFactory, ...)
{
    _repoStore = stateStoreFactory.GetStore<RepositoryMetadata>("git-statestore");
}

// Stored in Redis via lib-state:
// - Repository metadata (owner, name, description, visibility)
// - Access control lists
// - Webhook configurations
// - Sync subscription state
```

**Bare Repository Storage** (file system):
```
/var/git/repositories/{owner}/{repo}.git/
├── HEAD
├── config
├── description
├── hooks/
│   └── post-receive  # Triggers lib-messaging events
├── objects/
└── refs/
```

---

## API Design

### Management API (POST-only, Tenet 1)

All endpoints use POST-only pattern for zero-copy WebSocket routing.

```yaml
# schemas/git-api.yaml
openapi: 3.0.3
info:
  title: Bannou Git Service API
  version: 1.0.0
  description: Self-hosted Git repository management with real-time sync

servers:
  - url: http://localhost:5012

paths:
  /git/repositories/list:
    post:
      operationId: listRepositories
      summary: List repositories accessible to authenticated user
      tags: [Git Repositories]
      x-permissions:
        - role: user
          states: {}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ListRepositoriesRequest'
      responses:
        '200':
          description: Repository list
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/RepositoryListResponse'

  /git/repositories/create:
    post:
      operationId: createRepository
      summary: Create a new repository
      tags: [Git Repositories]
      x-permissions:
        - role: user
          states: {}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CreateRepositoryRequest'
      responses:
        '201':
          description: Repository created
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Repository'
        '409':
          description: Repository already exists

  /git/repositories/get:
    post:
      operationId: getRepository
      summary: Get repository details
      tags: [Git Repositories]
      x-permissions:
        - role: user
          states: {}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetRepositoryRequest'
      responses:
        '200':
          description: Repository details
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Repository'
        '404':
          description: Repository not found

  /git/repositories/delete:
    post:
      operationId: deleteRepository
      summary: Delete repository
      tags: [Git Repositories]
      x-permissions:
        - role: admin
          states: {}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/DeleteRepositoryRequest'
      responses:
        '204':
          description: Repository deleted

  /git/repositories/refs:
    post:
      operationId: listRefs
      summary: List repository references (branches, tags)
      tags: [Git Repositories]
      x-permissions:
        - role: user
          states: {}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ListRefsRequest'
      responses:
        '200':
          description: Reference list
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/RefListResponse'

  /git/repositories/commits:
    post:
      operationId: listCommits
      summary: List commits on a branch
      tags: [Git Repositories]
      x-permissions:
        - role: user
          states: {}
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ListCommitsRequest'
      responses:
        '200':
          description: Commit list
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CommitListResponse'
```

### Git Protocol Endpoints (Separate Controller, Not Schema-Generated)

```csharp
/// <summary>
/// Handles Git Smart HTTP protocol. NOT schema-generated - uses Git-specific content types.
/// </summary>
[Route("/git/{owner}/{repo}.git")]
public class GitProtocolController : ControllerBase
{
    private readonly IGitProcessExecutor _gitExecutor;
    private readonly IPermissionClient _permissions;

    [HttpGet("info/refs")]
    public async Task<IActionResult> InfoRefs(
        string owner, string repo,
        [FromQuery] string service)
    {
        // Validate permissions via lib-mesh call to Permission service
        // Return ref advertisement with capabilities
    }

    [HttpPost("git-upload-pack")]
    [Consumes("application/x-git-upload-pack-request")]
    [Produces("application/x-git-upload-pack-result")]
    public async Task<IActionResult> UploadPack(string owner, string repo)
    {
        // Requires git.repo.read permission
        // Execute: git upload-pack --stateless-rpc {repo_path}
    }

    [HttpPost("git-receive-pack")]
    [Consumes("application/x-git-receive-pack-request")]
    [Produces("application/x-git-receive-pack-result")]
    public async Task<IActionResult> ReceivePack(string owner, string repo)
    {
        // Requires git.repo.write permission
        // Execute: git receive-pack --stateless-rpc {repo_path}
        // Triggers post-receive hook → lib-messaging event
    }
}
```

---

## Real-Time Synchronization

This is the unique value proposition - instant, bidirectional repository sync via WebSocket.

### Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                    Real-Time Git Sync                                │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  Developer A                           Bannou Git Server             │
│  ┌──────────────┐                     ┌──────────────────────┐      │
│  │ Local Repo   │── git push ────────►│   Bare Repository    │      │
│  └──────────────┘                     └──────────┬───────────┘      │
│        ▲                                         │                   │
│        │                                         │ post-receive hook │
│        │                                         ▼                   │
│  ┌─────┴────────┐                     ┌──────────────────────┐      │
│  │ Sync Agent   │◄── WebSocket ───────│  Connect Service     │      │
│  │ (optional)   │    git.push.received│  (via lib-messaging) │      │
│  └──────────────┘                     └──────────────────────┘      │
│                                                  ▲                   │
│  Developer B                                     │                   │
│  ┌──────────────┐                               │                   │
│  │ Local Repo   │◄── auto-fetch (triggered) ────┘                   │
│  └──────────────┘                                                    │
│        ▲                                                             │
│  ┌─────┴────────┐                                                    │
│  │ Sync Agent   │◄── WebSocket: git.ref.updated ─────────────────────│
│  └──────────────┘    { ref: "main", old_sha, new_sha, commits }      │
└─────────────────────────────────────────────────────────────────────┘
```

### Client Events (via IClientEventPublisher)

Add to `git-client-events.yaml`:

```yaml
GitPushReceivedEvent:
  allOf:
    - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
  type: object
  x-client-event: true
  required: [event_name, event_id, timestamp, repository, pusher, refs]
  properties:
    event_name:
      type: string
      enum: ["git.push_received"]
    repository:
      $ref: '#/components/schemas/RepositoryRef'
    pusher:
      $ref: '#/components/schemas/AccountRef'
    refs:
      type: array
      items:
        $ref: '#/components/schemas/RefUpdate'
    commits:
      type: array
      items:
        $ref: '#/components/schemas/CommitSummary'

GitRefCreatedEvent:
  allOf:
    - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
  type: object
  x-client-event: true
  required: [event_name, event_id, timestamp, repository, ref, sha, ref_type]
  properties:
    event_name:
      type: string
      enum: ["git.ref_created"]
    repository:
      $ref: '#/components/schemas/RepositoryRef'
    ref:
      type: string
    sha:
      type: string
    ref_type:
      type: string
      enum: [branch, tag]

GitRefDeletedEvent:
  allOf:
    - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
  type: object
  x-client-event: true
  required: [event_name, event_id, timestamp, repository, ref, last_sha]
  properties:
    event_name:
      type: string
      enum: ["git.ref_deleted"]
    repository:
      $ref: '#/components/schemas/RepositoryRef'
    ref:
      type: string
    last_sha:
      type: string
```

### Sync Agent (Optional Client Tool)

Lightweight daemon for developers:
1. Connects to Bannou via WebSocket (reuses existing binary protocol)
2. Subscribes to events for watched repositories
3. On `git.push_received`, optionally auto-fetches
4. Provides desktop notifications
5. Queues offline events for catchup on reconnect

---

## Configuration

Add to `git-configuration.yaml`:

```yaml
x-service-configuration:
  RepositoryBasePath:
    type: string
    env: GIT_REPOSITORY_BASE_PATH
    default: /var/git/repositories

  MaxRepositorySizeBytes:
    type: integer
    env: GIT_MAX_REPOSITORY_SIZE_BYTES
    default: 1073741824
    description: Maximum repository size (1GB default)

  AllowAnonymousRead:
    type: boolean
    env: GIT_ALLOW_ANONYMOUS_READ
    default: false

  EnableRealTimeSync:
    type: boolean
    env: GIT_ENABLE_REALTIME_SYNC
    default: true

  GitExecutablePath:
    type: string
    env: GIT_EXECUTABLE_PATH
    default: git

  ProtocolV2Enabled:
    type: boolean
    env: GIT_PROTOCOL_V2_ENABLED
    default: true
```

---

## CI/CD Strategy

### The GitHub Actions Trade-off

**What we lose:** Free compute minutes, action ecosystem, matrix builds, workflow visualization

**What we gain:** Full control, no rate limits, private network access, custom hardware

### Recommended Hybrid Approach

```
┌─────────────────────────────────────────────────────────────────┐
│  Bannou Git  ──mirror──►  GitHub  ◄──poll──  Self-Hosted Runners │
│  (primary)                (CI only)           (our hardware)     │
└─────────────────────────────────────────────────────────────────┘
```

**Phase 1**: Keep GitHub as primary with self-hosted runners for cost control
**Phase 2**: Add Bannou Git for internal repos, mirror to GitHub for CI
**Phase 3**: Evaluate native CI plugin if demand exists (Drone CI compatible webhooks)

---

## Implementation Phases

### Phase 1: Core Git Protocol (2 weeks)

**Week 1: Protocol Handler**
- [ ] Implement `PacketLineParser` (pkt-line format parsing)
- [ ] Create `GitProtocolController` with info/refs, upload-pack, receive-pack
- [ ] Implement `GitProcessExecutor` for git.exe process management
- [ ] Add Protocol v2 support (`Git-Protocol: version=2` header)

**Week 2: Repository Management**
- [ ] Implement `RepositoryManager` (bare repo creation/deletion)
- [ ] Authentication integration via Auth service JWT validation
- [ ] Permission checks via Permission service (lib-mesh calls)
- [ ] State store integration for repository metadata

### Phase 2: Management API (2 weeks)

**Week 3: CRUD Operations**
- [ ] Create `git-api.yaml` schema (POST-only pattern)
- [ ] Run `scripts/generate-all-services.sh`
- [ ] Implement `GitService` CRUD operations
- [ ] Repository list/search with pagination

**Week 4: Content API**
- [ ] Tree listing (run `git ls-tree`)
- [ ] Blob content retrieval (run `git cat-file`)
- [ ] Commit history listing (run `git log --format=json`)
- [ ] Ref management (create/delete branches, tags)

### Phase 3: Real-Time Sync (2 weeks)

**Week 5: Event System**
- [ ] Create `git-client-events.yaml` schema
- [ ] Implement post-receive hook script that calls Bannou API
- [ ] Event publishing via `IMessageBus`
- [ ] Client event delivery via `IClientEventPublisher`
- [ ] Repository subscription management

**Week 6: Client Tooling**
- [ ] Sync agent daemon design (cross-platform .NET)
- [ ] WebSocket event subscription protocol
- [ ] Offline queue and catchup mechanism
- [ ] Integration tests for sync flow

### Phase 4: Testing & Polish (1-2 weeks)

**Week 7-8:**
- [ ] Unit tests for protocol parsing
- [ ] Integration tests for push/pull/clone operations
- [ ] HTTP tester integration tests for management API
- [ ] Edge tester WebSocket tests for real-time events
- [ ] Documentation and deployment guide

**Total: 6-8 weeks**

---

## Risk Analysis

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| **Git protocol complexity** | Medium | High | Use process execution, follow Bonobo patterns |
| **Security vulnerabilities** | Medium | High | Strict input validation, process sandboxing, permission checks |
| **Storage growth** | High | Medium | Implement quotas, garbage collection, size alerts |
| **CI/CD gap** | High | High | Plan hybrid approach, mirror to GitHub |
| **Maintenance burden** | Medium | Medium | Keep scope minimal (no PR/issue features) |

---

## Scope Boundaries

**In Scope (Phase 1):**
- Push, pull, clone operations
- Repository CRUD
- Branch/tag management
- Commit/tree/blob browsing
- Real-time sync events
- Basic webhooks

**Out of Scope (Maybe Later):**
- Pull requests / merge requests
- Issue tracking
- Code review
- Wiki
- CI/CD execution (use webhooks to external CI)

---

## References

### Protocol Documentation
- [Git HTTP Protocol](https://git-scm.com/docs/http-protocol)
- [Git Protocol v2](https://git-scm.com/docs/protocol-v2)
- [git-upload-pack](https://git-scm.com/docs/git-upload-pack)
- [git-receive-pack](https://git-scm.com/docs/git-receive-pack)

### Implementation References
- [Bonobo Git Server](https://github.com/jakubgarfield/Bonobo-Git-Server) - ASP.NET, process execution pattern
- [Gitea API](https://docs.gitea.com/development/api-usage) - API design patterns
