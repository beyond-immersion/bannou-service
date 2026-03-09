# GitHub Actions CI/CD

> **Last Updated**: 2026-03-08
> **Scope**: GitHub Actions CI/CD workflows, integration pipeline stages, SDK publishing, and reusable actions

## Summary

GitHub Actions CI/CD pipeline configuration covering the integration testing pipeline, unit test and lint workflows, SDK preview and stable release publishing, and reusable composite actions. Reference when investigating CI failures, adding new test stages, configuring SDK releases, or understanding workflow triggers and sequencing.

## Workflows Overview

| Workflow | Trigger | Purpose |
|----------|---------|---------|
| `ci.integration.yml` | PR to master / Manual | Main integration pipeline |
| `ci.lint.yml` | Push (any branch) | EditorConfig and linting validation |
| `ci.unit.yml` | Push (any branch) | Fast unit test feedback |
| `ci.generation-check.yml` | PR to master | Validates generated files match schemas |
| `ci.sdk-label-check.yml` | PR | Ensures SDK releases have proper labels |
| `ci.sdk-preview.yml` | Manual | Manual SDK preview publish (bypasses CI) |
| `ci.sdk-preview-auto.yml` | Push to master | Automatic SDK preview publish on merge |
| `ci.sdk-release.yml` | Manual | Stable SDK release to NuGet |
| `ci.release.yml` | Push to master (VERSION change) / Manual | Platform release (git tag + GitHub release) |

## Integration Pipeline

The main pipeline (`ci.integration.yml`) runs on PRs to master and manual dispatch. It executes a sequential testing progression. Unit tests are NOT included here -- they run separately via `ci.unit.yml` on every push.

```
┌─────────────────┐
│ Checkout & Setup│
└────────┬────────┘
         │
┌────────▼────────┐
│ Infrastructure  │ Docker health checks
└────────┬────────┘
         │
┌────────▼────────┐
│ HTTP Integration│ Service-to-service
└────────┬────────┘
         │
┌────────▼────────┐
│ WS Backward     │ Old client → new server
└────────┬────────┘
         │
┌────────▼────────┐
│ WS Forward      │ New client → old server
└─────────────────┘
```

### Test Stages

**1. Infrastructure Tests**
- Starts Docker Compose stack via `docker-compose.test.infrastructure.yml`
- Validates container health
- Tests infrastructure configuration (Redis, RabbitMQ, MySQL)
- Verifies basic connectivity

**2. HTTP Integration Tests**
- Tests service-to-service HTTP communication via mesh
- Uses Docker Compose test stack (`docker-compose.test.http.yml`)
- Uses generated clients
- Validates API contracts against schemas

**3. WebSocket Backward Compatibility**
- Tests published SDK clients against new server code
- Ensures protocol backward compatibility
- Uses Docker Compose test stack (`docker-compose.test.edge.yml`)
- Skipped if no published SDK exists on NuGet.org yet

**4. WebSocket Forward Compatibility**
- Tests new clients against existing server
- Validates protocol forward compatibility
- Prevents breaking client updates

## Reusable Actions

Located in `.github/actions/`:

| Action | Purpose |
|--------|---------|
| `setup-bannou` | .NET setup and build |
| `infrastructure-test` | Docker health validation |
| `http-integration-test` | HTTP API testing |
| `websocket-backward-test` | Backward compatibility |
| `websocket-forward-test` | Forward compatibility |
| `discord-notify` | Discord webhook notifications |

## CI Environment Configuration

CI tests use Docker Compose overlay files for environment configuration rather than `.env` files:

| Compose Overlay | Purpose |
|-----------------|---------|
| `docker-compose.test.yml` | Base test configuration |
| `docker-compose.test.infrastructure.yml` | Infrastructure test stack (minimal) |
| `docker-compose.test.http.yml` | HTTP integration test stack |
| `docker-compose.test.edge.yml` | WebSocket edge test stack |

These overlays are located in `provisioning/` and configure service enablement, test containers, and environment variables for each test tier.

## SDK Release Process

### Preview Releases (Automatic)

`ci.sdk-preview-auto.yml` runs on every push to master and publishes preview packages:

```
Version pattern: {SDK_VERSION+patch}-preview.{run_number}
Example: 1.0.1-preview.123 (when SDK_VERSION is 1.0.0)
```

Preview versions are always higher than the last stable release by bumping the patch version.

### Preview Releases (Manual)

`ci.sdk-preview.yml` allows manual preview publishing when CI fails for infrastructure reasons but code is known-good. Requires typing "publish" to confirm and a reason for the manual publish.

```bash
# Trigger manual preview publish
gh workflow run ci.sdk-preview.yml
```

### Stable Releases (Manual)

The `ci.sdk-release.yml` workflow requires:
1. `sdk-release` label on PR
2. Manual workflow dispatch
3. Version bump in `sdks/SDK_VERSION` file

```bash
# Trigger stable release
gh workflow run ci.sdk-release.yml
```

## Discord Notifications

Each stage sends notifications via Discord webhook:

- **Success**: Green embed with stage name
- **Failure**: Red embed with error context
- **Link**: Direct link to workflow run

Configure webhook secret:
```
DISCORD_WEBHOOK: https://discord.com/api/webhooks/...
```

## Local CI Simulation

Run the CI pipeline locally:

```bash
# Full development cycle (clean, generate, format, build, all tests)
make all

# Individual stages
make test                    # Unit tests
make test-infrastructure     # Infrastructure validation
make test-http               # HTTP integration
make test-edge               # WebSocket tests
```

## GitHub Secrets

Required secrets for CI:

| Secret | Purpose |
|--------|---------|
| `DISCORD_WEBHOOK` | Discord notifications |
| `NUGET_API_KEY` | NuGet package publishing |

## Workflow Triggers

### Push Events (Any Branch)
- `ci.unit.yml` - Unit tests on every push
- `ci.lint.yml` - EditorConfig validation on every push

### Push Events (Master Only)
- `ci.sdk-preview-auto.yml` - Automatic SDK preview publish
- `ci.release.yml` - Platform release (only when `VERSION` file changes)

### Pull Request Events (To Master)
- `ci.integration.yml` - Full integration pipeline
- `ci.generation-check.yml` - Validates generated files match schemas
- `ci.sdk-label-check.yml` - Validates release PR labels

### Manual Dispatch
- `ci.integration.yml` - Re-run integration tests
- `ci.sdk-preview.yml` - Manual SDK preview publish
- `ci.sdk-release.yml` - Stable SDK release
- `ci.release.yml` - Platform release (with optional dry-run and force flags)

## Adding New Test Stages

1. Create action in `.github/actions/`:
```yaml
# .github/actions/my-test/action.yml
name: 'My Test'
runs:
  using: 'composite'
  steps:
    - name: Run tests
      shell: bash
      run: make my-test
```

2. Add to integration pipeline:
```yaml
- name: My Tests
  if: success()
  uses: ./.github/actions/my-test

- name: Notify My Tests Result
  if: always()
  uses: ./.github/actions/discord-notify
  with:
    stage-name: "My Tests"
    # ...
```

## Troubleshooting

### Tests Pass Locally But Fail in CI

1. Check environment differences:
```bash
# Compare local docker-compose config vs CI test overlays
diff provisioning/docker-compose.services.yml provisioning/docker-compose.test.http.yml
```

2. Verify Docker state is clean:
```bash
docker compose down -v
docker system prune -f
```

3. Check for race conditions:
```bash
# Add explicit waits in tests
await Task.Delay(1000);  # Or use proper health checks
```

### SDK Publishing Fails

1. Verify NuGet API key is valid
2. Check version doesn't already exist
3. Ensure `sdks/SDK_VERSION` file is updated

### Discord Notifications Not Working

1. Verify webhook URL in secrets
2. Check workflow has access to secrets
3. Test webhook manually with curl

## Next Steps

- [Testing Guide](TESTING.md) - Detailed test documentation
- [NuGet Setup](NUGET-SETUP.md) - SDK package configuration
- [Deployment Guide](DEPLOYMENT.md) - Production deployment
