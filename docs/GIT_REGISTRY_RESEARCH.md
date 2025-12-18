# Git Registry Research: Self-Hosted Git Server as Bannou Plugin

**Research Date**: 2025-12-15
**Status**: Initial Research Complete
**Purpose**: Feasibility analysis for implementing a self-hosted Git registry/server as a Bannou microservice plugin

---

## Executive Summary

Implementing a self-hosted Git server ("registry") as a Bannou plugin is **technically feasible** and could leverage our existing architecture for unique real-time synchronization capabilities that GitHub cannot offer. However, it requires significant implementation effort due to the complexity of Git's wire protocol.

### Key Findings

| Aspect | Assessment | Notes |
|--------|------------|-------|
| **Feasibility** | High | Well-documented protocols, existing .NET libraries |
| **Complexity** | Medium-High | Protocol implementation requires careful attention |
| **Time Estimate** | 4-8 weeks | For basic push/pull/clone functionality |
| **Unique Value** | High | WebSocket-based real-time sync is genuinely novel |
| **CI/CD Trade-off** | Significant | Losing free GitHub Actions runners is major |

### Recommended Approach

**Hybrid Strategy**: Use `git.exe` process execution for Git operations (like Bonobo Git Server) while implementing a comprehensive repository management API and WebSocket-based real-time synchronization as a Bannou plugin. This provides the fastest path to a working solution while maintaining our schema-first architecture.

---

## Table of Contents

1. [Git Protocol Overview](#1-git-protocol-overview)
2. [Implementation Options](#2-implementation-options)
3. [Existing Solutions Analysis](#3-existing-solutions-analysis)
4. [Bannou Integration Architecture](#4-bannou-integration-architecture)
5. [Real-Time Synchronization Design](#5-real-time-synchronization-design)
6. [API Schema Design](#6-api-schema-design)
7. [CI/CD Considerations](#7-cicd-considerations)
8. [Implementation Roadmap](#8-implementation-roadmap)
9. [Risk Analysis](#9-risk-analysis)
10. [Recommendations](#10-recommendations)

---

## 1. Git Protocol Overview

### 1.1 Protocol Types

Git supports two HTTP-based transfer protocols:

#### Dumb Protocol
- Requires only a standard HTTP server
- Read-only, serves static files
- Less efficient, downloads entire packfiles
- Endpoints: `/info/refs`, `/objects/{sha}/`, `/HEAD`

#### Smart HTTP Protocol
- Requires Git-aware backend (CGI or server module)
- Bidirectional, supports push and pull
- Efficient packfile negotiation
- Endpoints: `/info/refs?service=git-upload-pack`, `/git-upload-pack`, `/git-receive-pack`

### 1.2 Smart HTTP Protocol Flow

```
┌─────────────────────────────────────────────────────────────────────┐
│                      FETCH (git clone/pull)                         │
├─────────────────────────────────────────────────────────────────────┤
│ 1. Client: GET /info/refs?service=git-upload-pack                   │
│    Server: Returns refs list with capabilities                       │
│                                                                      │
│ 2. Client: POST /git-upload-pack                                    │
│    Body: want/have negotiation                                       │
│    Server: Returns packfile with requested objects                   │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                      PUSH (git push)                                 │
├─────────────────────────────────────────────────────────────────────┤
│ 1. Client: GET /info/refs?service=git-receive-pack                  │
│    Server: Returns current refs and capabilities                     │
│                                                                      │
│ 2. Client: POST /git-receive-pack                                   │
│    Body: ref updates + packfile                                      │
│    Server: Applies updates, returns status                           │
└─────────────────────────────────────────────────────────────────────┘
```

### 1.3 Protocol v2 Improvements

Git Protocol v2 (supported since Git 2.18) offers significant improvements:

| Feature | v1 | v2 |
|---------|----|----|
| **Statelessness** | Partial | Full (designed for HTTP/stateless-rpc) |
| **Ref Advertisement** | Automatic | On-demand via `ls-refs` command |
| **Capabilities** | Hidden behind NUL byte | Dedicated section |
| **Performance** | Baseline | 3x improvement for no-op fetches |
| **Extensibility** | Limited | Built-in via capability system |

**Protocol v2 Request Format:**
```
GET /info/refs?service=git-upload-pack HTTP/1.1
Git-Protocol: version=2
```

**Key v2 Commands:**
- `ls-refs` - Explicit reference listing
- `fetch` - Packfile download with advanced filtering
- `object-info` - Object metadata without full fetch

### 1.4 Packet-Line Format

All Git protocol communication uses "pkt-line" framing:

```
Format: LLLL<data>
- LLLL = 4 hex digits representing total line length (including LLLL)
- Special packets:
  - 0000 = flush (end of message)
  - 0001 = delimiter (section separator)
  - 0002 = response end (stateless connections)

Example:
  001e# service=git-upload-pack\n
  0000
  004895dcfa3633004da0049d3d0fa03f80589cbcaf31 refs/heads/master\0multi_ack\n
```

### 1.5 Content Types

| Endpoint | Request Content-Type | Response Content-Type |
|----------|---------------------|----------------------|
| `/info/refs?service=git-upload-pack` | - | `application/x-git-upload-pack-advertisement` |
| `/info/refs?service=git-receive-pack` | - | `application/x-git-receive-pack-advertisement` |
| `/git-upload-pack` | `application/x-git-upload-pack-request` | `application/x-git-upload-pack-result` |
| `/git-receive-pack` | `application/x-git-receive-pack-request` | `application/x-git-receive-pack-result` |

---

## 2. Implementation Options

### 2.1 Option A: Process Execution (Recommended)

**Approach**: Execute `git upload-pack` and `git receive-pack` as external processes, similar to Bonobo Git Server.

**Pros:**
- ✅ Full Git compatibility guaranteed
- ✅ Fastest path to working implementation
- ✅ Automatic support for new Git features
- ✅ Battle-tested by Bonobo Git Server
- ✅ Supports both protocol v1 and v2

**Cons:**
- ❌ Requires Git installation on server
- ❌ Process spawning overhead per request
- ❌ Harder to intercept/modify protocol flow

**Implementation Pattern (C#):**
```csharp
public async Task<Stream> HandleUploadPack(string repositoryPath, Stream requestBody)
{
    var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "git",
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

### 2.2 Option B: LibGit2Sharp with Custom Protocol

**Approach**: Use LibGit2Sharp for repository operations and implement protocol handling manually.

**Pros:**
- ✅ No external process dependency
- ✅ Full control over operations
- ✅ Can hook into all operations

**Cons:**
- ❌ LibGit2Sharp does not implement server-side protocol
- ❌ Must implement pkt-line parsing manually
- ❌ Must implement negotiation algorithm manually
- ❌ Significant development effort

**LibGit2Sharp Capabilities:**
```csharp
// Available (client-side and local operations)
using (var repo = new Repository(path))
{
    repo.Refs.Log(repo.Head);           // Reflogs
    repo.ObjectDatabase.CreateTree();    // Tree manipulation
    repo.Pack(packPath);                 // Create packfiles (via PackBuilder)
}

// NOT Available (requires manual implementation)
// - Server-side negotiation
// - pkt-line parsing
// - Smart HTTP protocol handling
```

**Maintainer Statement** (from GitHub issue #1384):
> "libgit2 does not implement any server components... Building a Git server needs more than packfile handling—it demands protocol negotiation, policy enforcement, and hook execution."

### 2.3 Option C: Embed Existing Solution

**Approach**: Wrap Gitea/Forgejo or Bonobo as a containerized service, expose via Bannou API.

**Pros:**
- ✅ Complete solution immediately
- ✅ Web UI included
- ✅ All features already implemented

**Cons:**
- ❌ Not integrated with Bannou architecture
- ❌ Duplicate authentication systems
- ❌ Limited customization
- ❌ Additional operational complexity

### 2.4 Comparison Matrix

| Criterion | Process Execution | LibGit2Sharp | Embed Existing |
|-----------|------------------|--------------|----------------|
| **Development Time** | 4-6 weeks | 8-12 weeks | 1-2 weeks |
| **Git Compatibility** | 100% | ~90% | 100% |
| **Integration Quality** | High | Highest | Low |
| **Maintenance Burden** | Low | High | Medium |
| **Performance** | Good | Best | Variable |
| **Customization** | High | Highest | Low |
| **Recommended** | ⭐ Yes | For v2 | Quick prototype |

---

## 3. Existing Solutions Analysis

### 3.1 Bonobo Git Server

**Repository**: https://github.com/jakubgarfield/Bonobo-Git-Server

**Architecture:**
- ASP.NET MVC 3 on .NET Framework 4.0+
- SQLite for metadata storage
- Originally used GitSharp, now deprecated
- Process execution for Git operations
- IIS hosting with WebDAV support

**Key Implementation Insights:**
- Uses Tcl scripts (25% of codebase) for Git hooks
- Environment variables for hook context: `AUTH_USER`, `AUTH_USER_TEAMS`, `AUTH_USER_ROLES`
- Repository directory configuration is flexible
- Anonymous access controls per-repository

**Lessons for Bannou:**
- Process execution is a proven, reliable approach
- Hook integration enables powerful automation
- User/team permission model maps well to our auth system

### 3.2 Gitea

**Repository**: https://github.com/go-gitea/gitea

**Architecture:**
- Go single-binary application
- ~100MB binary size
- Embedded database (SQLite) or external (MySQL, PostgreSQL)
- Runs on any Go-supported platform
- Built-in CI/CD (Gitea Actions)

**API Structure:**
```
Base: /api/v1/
Authentication: Authorization: token <api_key>
Pagination: ?page=1&limit=20

Key Endpoints:
  POST   /repos                    - Create repository
  GET    /repos/:owner/:repo       - Get repository
  DELETE /repos/:owner/:repo       - Delete repository
  GET    /repos/:owner/:repo/raw/:branch/:path - Get file contents
  GET    /repos/:owner/:repo/git/trees/:sha    - Get tree
  GET    /repos/:owner/:repo/git/blobs/:sha    - Get blob
```

**Lessons for Bannou:**
- Comprehensive REST API with OpenAPI spec (`/swagger.v1.json`)
- Token-based auth with scopes
- Pagination via headers (`Link`, `x-total-count`)

### 3.3 Forgejo

**Repository**: https://forgejo.org/

**Differentiation from Gitea:**
- Non-profit governance (Codeberg e.V.)
- "Hard fork" as of early 2024
- More active development (217 contributors vs 136, 3,039 commits vs 1,228 since July 2024)
- Working on Forge Federation (ActivityPub)
- Drop-in replacement for Gitea (API compatible)

**Relevance:**
- Demonstrates community demand for open governance
- Federation features could inspire our real-time sync design

### 3.4 GitLab

**Architecture:**
- Ruby on Rails monolith + microservices
- Gitaly service for Git operations (Go)
- Heavy resource requirements
- Comprehensive CI/CD built-in

**Relevance:**
- Too heavy for our use case
- Gitaly's architecture is interesting (separates Git operations)
- Extensive webhook system worth studying

---

## 4. Bannou Integration Architecture

### 4.1 Proposed Service Structure

Following Bannou's schema-first plugin architecture:

```
lib-git/
├── Generated/                           # NSwag auto-generated files
│   ├── GitController.Generated.cs       # Repository management API controller
│   ├── IGitService.cs                   # Service interface
│   ├── GitClient.cs                     # Inter-service client
│   ├── GitModels.cs                     # Request/response models
│   └── GitServiceConfiguration.cs       # Configuration class
├── GitService.cs                        # Business logic (ONLY manual file)
├── GitServicePlugin.cs                  # Plugin registration
├── Protocol/                            # Git protocol implementation
│   ├── GitHttpHandler.cs               # HTTP Smart Protocol handler
│   ├── PacketLineParser.cs             # pkt-line format parsing
│   ├── GitProcessExecutor.cs           # git.exe process management
│   └── RepositoryManager.cs            # Bare repository operations
├── Events/                              # Git event definitions
│   ├── RepositoryCreatedEvent.cs
│   ├── PushReceivedEvent.cs
│   └── RefUpdatedEvent.cs
└── lib-git.csproj
```

### 4.2 HTTP Endpoint Structure

**Management API** (standard Bannou pattern):
```yaml
# schemas/git-api.yaml
paths:
  /git/repositories:
    get:
      summary: List repositories
    post:
      summary: Create repository

  /git/repositories/{owner}/{name}:
    get:
      summary: Get repository details
    delete:
      summary: Delete repository

  /git/repositories/{owner}/{name}/refs:
    get:
      summary: List references (branches, tags)

  /git/repositories/{owner}/{name}/commits:
    get:
      summary: List commits with pagination
```

**Git Protocol Endpoints** (separate controller, not schema-generated):
```csharp
[Route("/git/{owner}/{repo}.git")]
public class GitProtocolController : ControllerBase
{
    [HttpGet("info/refs")]
    public async Task<IActionResult> InfoRefs([FromQuery] string service)

    [HttpPost("git-upload-pack")]
    [Consumes("application/x-git-upload-pack-request")]
    public async Task<IActionResult> UploadPack()

    [HttpPost("git-receive-pack")]
    [Consumes("application/x-git-receive-pack-request")]
    public async Task<IActionResult> ReceivePack()
}
```

### 4.3 Integration with Existing Services

```
┌─────────────────────────────────────────────────────────────────┐
│                        Bannou Architecture                       │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌──────────┐     ┌──────────┐     ┌──────────────────────────┐│
│  │   Auth   │────▶│ Connect  │────▶│   Git Service Plugin     ││
│  │ Service  │     │ Service  │     │                          ││
│  └──────────┘     └──────────┘     │  ┌────────────────────┐  ││
│       │                │           │  │ Repository Manager │  ││
│       │                │           │  └────────────────────┘  ││
│       ▼                ▼           │           │              ││
│  ┌──────────────────────────┐     │           ▼              ││
│  │   Permissions Service    │     │  ┌────────────────────┐  ││
│  │                          │◀────│  │ Git Protocol Handler│ ││
│  │  - repo.read             │     │  └────────────────────┘  ││
│  │  - repo.write            │     │           │              ││
│  │  - repo.admin            │     │           ▼              ││
│  └──────────────────────────┘     │  ┌────────────────────┐  ││
│                                   │  │ git.exe process    │  ││
│                                   │  └────────────────────┘  ││
│                                   └──────────────────────────┘│
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │                      RabbitMQ Events                      │  │
│  │  - git.repository.created                                 │  │
│  │  - git.push.received (with commit details)                │  │
│  │  - git.ref.updated                                        │  │
│  │  - git.hook.triggered                                     │  │
│  └──────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

### 4.4 State Management

**Dapr State Store Components:**
```yaml
# components/git-statestore.yaml
apiVersion: dapr.io/v1alpha1
kind: Component
metadata:
  name: git-statestore
spec:
  type: state.redis
  metadata:
    - name: redisHost
      value: redis:6379
    - name: keyPrefix
      value: git
```

**Stored State:**
- Repository metadata (owner, name, description, visibility)
- Access control lists
- Webhook configurations
- Sync status for real-time features

**Bare Repository Storage:**
- File system storage (not in state store)
- Configurable path: `/var/git/repositories/{owner}/{repo}.git`
- Standard Git bare repository structure

---

## 5. Real-Time Synchronization Design

This is where Bannou can offer something GitHub cannot: **instant, bidirectional repository synchronization via WebSocket**.

### 5.1 Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    Real-Time Git Sync Architecture                          │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  Developer A                              Bannou Git Server                  │
│  ┌──────────────┐                        ┌──────────────────────┐           │
│  │ Local Repo   │                        │                      │           │
│  │              │  ──── git push ────▶   │   Bare Repository    │           │
│  │   .git/      │                        │                      │           │
│  └──────────────┘                        └──────────┬───────────┘           │
│        ▲                                            │                       │
│        │                                            │ post-receive hook     │
│        │                                            ▼                       │
│  ┌─────┴────────┐                        ┌──────────────────────┐           │
│  │ Sync Agent   │◀──── WebSocket ────────│  Connect Service     │           │
│  │ (optional)   │      push.received     │  (WebSocket Gateway) │           │
│  └──────────────┘                        └──────────┬───────────┘           │
│                                                     │                       │
│                                                     │ RabbitMQ              │
│  Developer B                                        ▼                       │
│  ┌──────────────┐                        ┌──────────────────────┐           │
│  │ Local Repo   │                        │                      │           │
│  │              │◀──── git fetch ────────│   Git Service        │           │
│  │   .git/      │   (triggered by        │   (event handler)    │           │
│  └──────────────┘    WebSocket event)    │                      │           │
│        ▲                                 └──────────────────────┘           │
│        │                                                                    │
│  ┌─────┴────────┐                                                           │
│  │ Sync Agent   │◀──────── WebSocket: refs.updated ─────────────────────────│
│  │ (subscribes  │          { ref: "refs/heads/main",                        │
│  │  to events)  │            old_sha: "abc123",                             │
│  └──────────────┘            new_sha: "def456",                             │
│                              commits: [...] }                               │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 5.2 WebSocket Events

**Event: `git.push.received`**
```json
{
  "event_type": "git.push.received",
  "repository": {
    "owner": "parnassian",
    "name": "bannou-service"
  },
  "pusher": {
    "account_id": "uuid",
    "username": "lysander"
  },
  "refs": [
    {
      "ref": "refs/heads/main",
      "old_sha": "abc123def456...",
      "new_sha": "789ghi012jkl...",
      "forced": false
    }
  ],
  "commits": [
    {
      "sha": "789ghi012jkl...",
      "message": "Add git registry feature",
      "author": "Lysander <lysander@example.com>",
      "timestamp": "2025-12-15T10:30:00Z",
      "files_changed": ["src/git/...", "docs/..."]
    }
  ],
  "timestamp": "2025-12-15T10:30:05Z"
}
```

**Event: `git.ref.created`**
```json
{
  "event_type": "git.ref.created",
  "repository": { "owner": "parnassian", "name": "bannou-service" },
  "ref": "refs/tags/v1.0.0",
  "sha": "abc123...",
  "ref_type": "tag",
  "tagger": { "account_id": "uuid", "username": "lysander" }
}
```

**Event: `git.ref.deleted`**
```json
{
  "event_type": "git.ref.deleted",
  "repository": { "owner": "parnassian", "name": "bannou-service" },
  "ref": "refs/heads/feature/old-branch",
  "last_sha": "abc123..."
}
```

### 5.3 Client Sync Agent

A lightweight daemon that can be installed alongside Git:

**Functionality:**
1. Connects to Bannou via WebSocket (reuses existing binary protocol)
2. Subscribes to events for watched repositories
3. On `git.push.received`, optionally auto-fetches
4. Provides desktop notifications for repository activity
5. Queues offline changes for sync on reconnect

**Protocol Integration:**
```
┌─────────────────────────────────────────────────────────────────┐
│                   Bannou WebSocket Protocol                      │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Binary Header (31 bytes):                                       │
│  [Flags: 1][Channel: 2][Sequence: 4][ServiceGUID: 16][MsgID: 8] │
│                                                                  │
│  Service GUID for Git: Client-salted, unique per connection      │
│                                                                  │
│  Message Types:                                                  │
│  - git.subscribe     { repositories: ["owner/repo", ...] }       │
│  - git.unsubscribe   { repositories: ["owner/repo", ...] }       │
│  - git.event         { <event payload> }                         │
│  - git.sync.request  { repository: "owner/repo", ref: "main" }  │
│  - git.sync.status   { queued: 3, syncing: "owner/repo" }       │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 5.4 Diff Queue for Offline Support

When developers push while disconnected from real-time sync:

```
┌──────────────────────────────────────────────────────────────────┐
│                     Offline Sync Queue                            │
├──────────────────────────────────────────────────────────────────┤
│                                                                   │
│  State Store (Redis):                                             │
│  git:sync:queue:{owner}/{repo} = [                                │
│    { ref: "main", sha: "abc...", timestamp: "...", synced: false },
│    { ref: "main", sha: "def...", timestamp: "...", synced: false }
│  ]                                                                │
│                                                                   │
│  On WebSocket Reconnect:                                          │
│  1. Client sends git.sync.catchup { since: last_sync_timestamp }  │
│  2. Server streams queued events                                  │
│  3. Client acknowledges receipt                                   │
│  4. Server marks events as delivered                              │
│                                                                   │
└──────────────────────────────────────────────────────────────────┘
```

---

## 6. API Schema Design

### 6.1 OpenAPI Schema (`schemas/git-api.yaml`)

```yaml
openapi: 3.0.1
info:
  title: Bannou Git Service API
  description: Self-hosted Git repository management with real-time sync
  version: 1.0.0

servers:
  - url: http://localhost:3500/v1.0/invoke/bannou/method

paths:
  /git/repositories:
    get:
      operationId: listRepositories
      summary: List repositories accessible to the authenticated user
      parameters:
        - name: page
          in: query
          schema:
            type: integer
            default: 1
        - name: limit
          in: query
          schema:
            type: integer
            default: 20
            maximum: 100
        - name: visibility
          in: query
          schema:
            $ref: '#/components/schemas/RepositoryVisibility'
      responses:
        '200':
          description: Repository list
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/RepositoryListResponse'

    post:
      operationId: createRepository
      summary: Create a new repository
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

  /git/repositories/{owner}/{name}:
    parameters:
      - name: owner
        in: path
        required: true
        schema:
          type: string
      - name: name
        in: path
        required: true
        schema:
          type: string

    get:
      operationId: getRepository
      summary: Get repository details
      responses:
        '200':
          description: Repository details
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Repository'
        '404':
          description: Repository not found

    patch:
      operationId: updateRepository
      summary: Update repository settings
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/UpdateRepositoryRequest'
      responses:
        '200':
          description: Repository updated
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Repository'

    delete:
      operationId: deleteRepository
      summary: Delete repository
      responses:
        '204':
          description: Repository deleted
        '404':
          description: Repository not found

  /git/repositories/{owner}/{name}/refs:
    get:
      operationId: listRefs
      summary: List repository references (branches, tags)
      parameters:
        - name: type
          in: query
          schema:
            type: string
            enum: [all, heads, tags]
            default: all
      responses:
        '200':
          description: Reference list
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/RefListResponse'

  /git/repositories/{owner}/{name}/commits:
    get:
      operationId: listCommits
      summary: List commits on a branch
      parameters:
        - name: ref
          in: query
          schema:
            type: string
            default: HEAD
        - name: page
          in: query
          schema:
            type: integer
            default: 1
        - name: limit
          in: query
          schema:
            type: integer
            default: 30
      responses:
        '200':
          description: Commit list
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CommitListResponse'

  /git/repositories/{owner}/{name}/tree/{ref}:
    get:
      operationId: getTree
      summary: Get repository tree at a specific ref
      parameters:
        - name: path
          in: query
          schema:
            type: string
            default: ''
        - name: recursive
          in: query
          schema:
            type: boolean
            default: false
      responses:
        '200':
          description: Tree contents
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/TreeResponse'

  /git/repositories/{owner}/{name}/blob/{ref}/{path}:
    get:
      operationId: getBlob
      summary: Get file contents
      responses:
        '200':
          description: File contents
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/BlobResponse'
            application/octet-stream:
              schema:
                type: string
                format: binary

  /git/repositories/{owner}/{name}/webhooks:
    get:
      operationId: listWebhooks
      summary: List repository webhooks
      responses:
        '200':
          description: Webhook list
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/WebhookListResponse'

    post:
      operationId: createWebhook
      summary: Create a webhook
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CreateWebhookRequest'
      responses:
        '201':
          description: Webhook created
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Webhook'

components:
  schemas:
    RepositoryVisibility:
      type: string
      enum: [public, private, internal]

    Repository:
      type: object
      required: [id, owner, name, full_name, visibility, created_at]
      properties:
        id:
          type: string
          format: uuid
        owner:
          type: string
        name:
          type: string
        full_name:
          type: string
        description:
          type: string
        visibility:
          $ref: '#/components/schemas/RepositoryVisibility'
        default_branch:
          type: string
          default: main
        clone_url_http:
          type: string
          format: uri
        clone_url_ssh:
          type: string
        size_bytes:
          type: integer
          format: int64
        created_at:
          type: string
          format: date-time
        updated_at:
          type: string
          format: date-time
        pushed_at:
          type: string
          format: date-time

    CreateRepositoryRequest:
      type: object
      required: [name]
      properties:
        name:
          type: string
          pattern: '^[a-zA-Z0-9_-]+$'
          minLength: 1
          maxLength: 100
        description:
          type: string
          maxLength: 500
        visibility:
          $ref: '#/components/schemas/RepositoryVisibility'
        default_branch:
          type: string
          default: main
        auto_init:
          type: boolean
          default: false
          description: Initialize with README
        gitignore_template:
          type: string
          description: Name of .gitignore template
        license_template:
          type: string
          description: Name of license template

    UpdateRepositoryRequest:
      type: object
      properties:
        description:
          type: string
        visibility:
          $ref: '#/components/schemas/RepositoryVisibility'
        default_branch:
          type: string

    RepositoryListResponse:
      type: object
      required: [repositories, total_count, page, limit]
      properties:
        repositories:
          type: array
          items:
            $ref: '#/components/schemas/Repository'
        total_count:
          type: integer
        page:
          type: integer
        limit:
          type: integer

    Ref:
      type: object
      required: [ref, sha, type]
      properties:
        ref:
          type: string
          example: refs/heads/main
        sha:
          type: string
          pattern: '^[0-9a-f]{40}$'
        type:
          type: string
          enum: [branch, tag, other]
        target_sha:
          type: string
          description: For annotated tags, the commit SHA

    RefListResponse:
      type: object
      properties:
        refs:
          type: array
          items:
            $ref: '#/components/schemas/Ref'

    Commit:
      type: object
      required: [sha, message, author, committer, committed_at]
      properties:
        sha:
          type: string
        message:
          type: string
        author:
          $ref: '#/components/schemas/GitSignature'
        committer:
          $ref: '#/components/schemas/GitSignature'
        parent_shas:
          type: array
          items:
            type: string
        committed_at:
          type: string
          format: date-time

    GitSignature:
      type: object
      required: [name, email]
      properties:
        name:
          type: string
        email:
          type: string
          format: email

    CommitListResponse:
      type: object
      properties:
        commits:
          type: array
          items:
            $ref: '#/components/schemas/Commit'
        total_count:
          type: integer
        page:
          type: integer

    TreeEntry:
      type: object
      required: [path, type, sha]
      properties:
        path:
          type: string
        type:
          type: string
          enum: [blob, tree, commit]
        sha:
          type: string
        size:
          type: integer
          description: Only for blobs
        mode:
          type: string
          example: '100644'

    TreeResponse:
      type: object
      properties:
        sha:
          type: string
        entries:
          type: array
          items:
            $ref: '#/components/schemas/TreeEntry'
        truncated:
          type: boolean

    BlobResponse:
      type: object
      properties:
        sha:
          type: string
        size:
          type: integer
        encoding:
          type: string
          enum: [utf-8, base64]
        content:
          type: string

    Webhook:
      type: object
      required: [id, url, events, active]
      properties:
        id:
          type: string
          format: uuid
        url:
          type: string
          format: uri
        events:
          type: array
          items:
            type: string
            enum: [push, create, delete, pull_request]
        active:
          type: boolean
        secret:
          type: string
          writeOnly: true
        created_at:
          type: string
          format: date-time

    CreateWebhookRequest:
      type: object
      required: [url, events]
      properties:
        url:
          type: string
          format: uri
        events:
          type: array
          items:
            type: string
        secret:
          type: string
        active:
          type: boolean
          default: true

    WebhookListResponse:
      type: object
      properties:
        webhooks:
          type: array
          items:
            $ref: '#/components/schemas/Webhook'

  x-service-configuration:
    properties:
      RepositoryBasePath:
        type: string
        default: /var/git/repositories
      MaxRepositorySize:
        type: integer
        default: 1073741824
        description: Maximum repository size in bytes (1GB default)
      AllowAnonymousRead:
        type: boolean
        default: false
      EnableWebSocket:
        type: boolean
        default: true
      GitExecutablePath:
        type: string
        default: git
```

### 6.2 Events Schema (`schemas/git-events.yaml`)

```yaml
components:
  schemas:
    GitPushReceivedEvent:
      type: object
      required: [event_id, timestamp, repository, pusher, refs]
      properties:
        event_id:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
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
      type: object
      required: [event_id, timestamp, repository, ref, sha, ref_type]
      properties:
        event_id:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        repository:
          $ref: '#/components/schemas/RepositoryRef'
        ref:
          type: string
        sha:
          type: string
        ref_type:
          type: string
          enum: [branch, tag]
        creator:
          $ref: '#/components/schemas/AccountRef'

    GitRefDeletedEvent:
      type: object
      required: [event_id, timestamp, repository, ref, last_sha]
      properties:
        event_id:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        repository:
          $ref: '#/components/schemas/RepositoryRef'
        ref:
          type: string
        last_sha:
          type: string
        deleter:
          $ref: '#/components/schemas/AccountRef'

    GitRepositoryCreatedEvent:
      type: object
      required: [event_id, timestamp, repository, creator]
      properties:
        event_id:
          type: string
          format: uuid
        timestamp:
          type: string
          format: date-time
        repository:
          $ref: '#/components/schemas/RepositoryRef'
        creator:
          $ref: '#/components/schemas/AccountRef'

    RepositoryRef:
      type: object
      required: [owner, name]
      properties:
        owner:
          type: string
        name:
          type: string
        full_name:
          type: string

    AccountRef:
      type: object
      required: [account_id, username]
      properties:
        account_id:
          type: string
          format: uuid
        username:
          type: string

    RefUpdate:
      type: object
      required: [ref, old_sha, new_sha]
      properties:
        ref:
          type: string
        old_sha:
          type: string
        new_sha:
          type: string
        forced:
          type: boolean
          default: false

    CommitSummary:
      type: object
      required: [sha, message, author]
      properties:
        sha:
          type: string
        message:
          type: string
        author:
          type: string
        timestamp:
          type: string
          format: date-time
        files_changed:
          type: array
          items:
            type: string
```

---

## 7. CI/CD Considerations

### 7.1 The GitHub Actions Trade-off

**What We Lose:**
- Free compute minutes (2,000/month for free tier, unlimited for public repos)
- Pre-built action ecosystem (thousands of actions)
- Marketplace integrations
- Matrix builds with automatic parallelization
- Secret management infrastructure
- Workflow visualization and debugging tools

**What We Gain:**
- Full control over execution environment
- No rate limits or usage caps
- Private network access without exposing secrets
- Custom hardware (GPUs, specialized CPUs)
- No vendor lock-in

### 7.2 Self-Hosted Runner Architecture

GitHub Actions can work with self-hosted runners, providing a hybrid approach:

```
┌─────────────────────────────────────────────────────────────────────┐
│                    Hybrid CI/CD Architecture                         │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  Option A: Keep GitHub as remote, self-host runners                  │
│  ┌──────────────┐        ┌──────────────┐       ┌──────────────┐    │
│  │ Bannou Git   │───────▶│   GitHub     │◀──────│ Self-Hosted  │    │
│  │ (primary)    │ mirror │  (CI only)   │ poll  │   Runners    │    │
│  └──────────────┘        └──────────────┘       └──────────────┘    │
│                                                                      │
│  Option B: Full self-hosted with CI service                          │
│  ┌──────────────┐        ┌──────────────┐       ┌──────────────┐    │
│  │ Bannou Git   │───────▶│  Drone CI    │◀──────│  Runners     │    │
│  │ (primary)    │webhook │  or Jenkins  │       │  (local)     │    │
│  └──────────────┘        └──────────────┘       └──────────────┘    │
│                                                                      │
│  Option C: Bannou-native CI (future)                                 │
│  ┌──────────────┐        ┌──────────────┐       ┌──────────────┐    │
│  │ Bannou Git   │───────▶│ Bannou CI    │───────│ Dapr Jobs    │    │
│  │ Plugin       │ event  │ Plugin       │       │ (containers) │    │
│  └──────────────┘        └──────────────┘       └──────────────┘    │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

### 7.3 CI Alternatives

| Solution | Pros | Cons |
|----------|------|------|
| **Drone CI** | Lightweight, container-native, Go-based | Limited ecosystem |
| **Jenkins** | Extensive plugins, mature | Heavy, complex setup |
| **Woodpecker CI** | Drone fork, active community | Smaller community |
| **Gitea Actions** | Gitea-native, GitHub Actions compatible | Requires Gitea |
| **GitHub (mirrored)** | Full GitHub features | Requires external mirror |

### 7.4 Recommended Approach

**Phase 1**: Keep GitHub as primary with self-hosted runners for cost control
**Phase 2**: Add Bannou Git for internal/private repos, mirror to GitHub for CI
**Phase 3**: Evaluate native CI plugin if demand exists

---

## 8. Implementation Roadmap

### Phase 1: Core Git Protocol (Weeks 1-2)

**Week 1: Protocol Handler**
- [ ] Implement pkt-line parser/writer
- [ ] Create GitProtocolController with endpoints
- [ ] Implement `git-upload-pack` process execution
- [ ] Implement `git-receive-pack` process execution
- [ ] Add Protocol v2 support (`Git-Protocol: version=2`)

**Week 2: Repository Management**
- [ ] Bare repository creation/deletion
- [ ] Ref listing from bare repositories
- [ ] Basic authentication integration with Auth service
- [ ] Permission checks via Permissions service

### Phase 2: Management API (Weeks 3-4)

**Week 3: CRUD Operations**
- [ ] Define and generate git-api.yaml schema
- [ ] Implement GitService with CRUD operations
- [ ] Repository metadata storage in Dapr state store
- [ ] List/search repositories with pagination

**Week 4: Content API**
- [ ] Tree listing endpoint
- [ ] Blob content retrieval
- [ ] Commit history listing
- [ ] Ref management (create/delete branches, tags)

### Phase 3: Real-Time Sync (Weeks 5-6)

**Week 5: Event System**
- [ ] Git hook integration (post-receive)
- [ ] Event publishing to RabbitMQ
- [ ] WebSocket event routing via Connect service
- [ ] Subscription management

**Week 6: Client Tooling**
- [ ] Sync agent daemon (cross-platform)
- [ ] Event subscription protocol
- [ ] Offline queue and catchup mechanism
- [ ] Desktop notification integration

### Phase 4: Polish & Testing (Weeks 7-8)

**Week 7: Testing**
- [ ] Unit tests for protocol parsing
- [ ] Integration tests for push/pull/clone
- [ ] HTTP API integration tests
- [ ] WebSocket event tests

**Week 8: Documentation & Polish**
- [ ] User documentation
- [ ] API reference generation
- [ ] Deployment guide
- [ ] Performance optimization

---

## 9. Risk Analysis

### 9.1 Technical Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| **Git protocol complexity** | Medium | High | Use process execution, follow Bonobo patterns |
| **Performance issues** | Medium | Medium | Optimize process pooling, caching |
| **Security vulnerabilities** | Medium | High | Strict input validation, process sandboxing |
| **LibGit2Sharp limitations** | Low | Medium | Fall back to process execution |

### 9.2 Operational Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| **Storage growth** | High | Medium | Implement quotas, garbage collection |
| **Backup complexity** | Medium | High | Document backup procedures, test recovery |
| **Git version compatibility** | Low | Low | Test with multiple Git versions |
| **Network bandwidth** | Medium | Medium | Implement delta compression, caching |

### 9.3 Strategic Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| **CI/CD gap** | High | High | Plan hybrid approach, self-hosted runners |
| **Feature parity expectations** | Medium | Medium | Set clear scope boundaries |
| **Maintenance burden** | Medium | Medium | Keep implementation minimal |

---

## 10. Recommendations

### 10.1 Should You Build This?

**Build it if:**
- ✅ Real-time sync is a compelling differentiator for your use case
- ✅ Privacy/control is paramount (sensitive codebases)
- ✅ You want to demonstrate Bannou's extensibility
- ✅ You're willing to maintain CI/CD via hybrid approach
- ✅ You enjoy technically interesting challenges

**Don't build it if:**
- ❌ GitHub Actions free tier meets your needs
- ❌ You need extensive integrations (code scanning, project boards, etc.)
- ❌ Maintenance burden would detract from core development
- ❌ Real-time sync isn't actually needed

### 10.2 Recommended Approach

1. **Start with Process Execution**: Use `git.exe` for Git protocol handling (fastest path to working solution)

2. **Focus on WebSocket Sync**: This is your unique value proposition - make it excellent

3. **Keep API Compatible**: Follow Gitea/GitHub API patterns for client compatibility

4. **Plan Hybrid CI/CD**: Mirror to GitHub for CI, or use self-hosted runners

5. **Consider Scope**: Start with push/pull/clone; add PR/issue features later (if ever)

### 10.3 Quick Win Alternative

If real-time sync is the primary goal, consider:

**Webhook-to-WebSocket Bridge Plugin**

Instead of a full Git server, create a lightweight plugin that:
1. Receives GitHub/GitLab webhooks
2. Broadcasts events to connected WebSocket clients
3. Tracks repository watch lists per user
4. Provides instant notifications without replacing GitHub

This gives you the "cute" real-time sync feature with ~1 week of work instead of 8.

---

## References

### Protocol Documentation
- [Git HTTP Protocol](https://git-scm.com/docs/http-protocol) - Official HTTP protocol spec
- [Git Protocol v2](https://git-scm.com/docs/protocol-v2) - Modern protocol improvements
- [gitprotocol-pack](https://git-scm.com/docs/gitprotocol-pack) - Packfile transfer protocol
- [git-upload-pack](https://git-scm.com/docs/git-upload-pack) - Fetch operation
- [git-receive-pack](https://git-scm.com/docs/git-receive-pack) - Push operation

### .NET Libraries
- [LibGit2Sharp](https://github.com/libgit2/libgit2sharp) - .NET Git bindings
- [LibGit2Sharp Issue #1384](https://github.com/libgit2/libgit2sharp/issues/1384) - HTTP server discussion
- [LibGit2Sharp Issue #90](https://github.com/libgit2/libgit2sharp/issues/90) - Server-side support

### Existing Solutions
- [Bonobo Git Server](https://github.com/jakubgarfield/Bonobo-Git-Server) - ASP.NET Git server
- [Gitea](https://github.com/go-gitea/gitea) - Go-based Git forge
- [Forgejo](https://forgejo.org/) - Community-driven Gitea fork
- [Gitea API Documentation](https://docs.gitea.com/development/api-usage) - API patterns reference

### Architecture Guides
- [GitHub Webhooks](https://docs.github.com/en/webhooks/about-webhooks) - Event patterns
- [GitLab Webhooks](https://docs.gitlab.com/user/project/integrations/webhooks/) - Event patterns
- [GitHub Self-Hosted Runners](https://docs.github.com/actions/hosting-your-own-runners) - CI/CD alternative
- [Git Repository Layout](https://git-scm.com/docs/gitrepository-layout) - Bare repository structure

---

*This document will be updated as implementation progresses. Last updated: 2025-12-15*
