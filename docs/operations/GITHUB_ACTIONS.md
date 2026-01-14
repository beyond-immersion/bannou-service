# GitHub Actions CI/CD

This document describes Bannou's CI/CD pipeline implemented with GitHub Actions.

## Workflows Overview

| Workflow | Trigger | Purpose |
|----------|---------|---------|
| `ci.integration.yml` | Push/PR to master | Main integration pipeline |
| `ci.lint.yml` | Push/PR | EditorConfig and linting validation |
| `ci.unit.yml` | Push/PR | Fast unit test feedback |
| `ci.sdk-label-check.yml` | PR | Ensures SDK releases have proper labels |
| `ci.sdk-release.yml` | Manual | Stable SDK release to NuGet |

## Integration Pipeline

The main pipeline (`ci.integration.yml`) runs a sequential testing progression:

```
┌─────────────────┐
│ Checkout & Setup│
└────────┬────────┘
         │
┌────────▼────────┐
│   Unit Tests    │ dotnet test
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
└────────┬────────┘
         │
┌────────▼────────┐
│ NuGet Publish   │ Preview packages
└─────────────────┘
```

### Test Stages

**1. Unit Tests**
- Runs all unit tests with `dotnet test`
- Fast feedback on code changes
- No external dependencies required

**2. Infrastructure Tests**
- Starts Docker Compose stack
- Validates container health
- Tests infrastructure configuration (Redis, RabbitMQ, MySQL)
- Verifies basic connectivity

**3. HTTP Integration Tests**
- Tests service-to-service HTTP communication
- Uses generated clients
- Validates API contracts against schemas

**4. WebSocket Backward Compatibility**
- Tests existing clients against new server code
- Ensures protocol backward compatibility
- Uses `.env.ci.edge` configuration

**5. WebSocket Forward Compatibility**
- Tests new clients against existing server
- Validates protocol forward compatibility
- Prevents breaking client updates

**6. NuGet Preview Publishing**
- Only runs on master branch pushes
- Packages SDK with preview version suffix
- Publishes to NuGet.org

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

## Environment Files

CI uses specific environment configurations:

| File | Purpose |
|------|---------|
| `.env.ci.http` | HTTP integration test settings |
| `.env.ci.edge` | WebSocket test settings |

Key variables:
```bash
# CI-specific JWT settings
BANNOU_JWT_SECRET=ci-test-secret
BANNOU_JWT_ISSUER=bannou-ci
BANNOU_JWT_AUDIENCE=bannou-ci-tests

# Mock OAuth for testing
AUTH_MOCK_PROVIDERS=true

# Admin role for test accounts
ACCOUNT_ADMIN_EMAIL_DOMAIN=@admin.test.local
```

## SDK Release Process

### Preview Releases (Automatic)

Every successful master build publishes preview packages:

```
Version pattern: {SDK_VERSION}-preview.{run_number}
Example: 1.0.0-preview.123
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

### Push Events
- All workflows run on push to master
- Full pipeline validates changes

### Pull Request Events
- Lint and unit tests run on PRs
- Integration tests run on PRs to master
- SDK label check validates release PRs

### Manual Dispatch
- Integration pipeline: Re-run tests
- SDK release: Publish stable packages

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
# Compare local vs CI env
diff .env .env.ci.http
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

- [Testing Guide](../guides/TESTING.md) - Detailed test documentation
- [NuGet Setup](NUGET_SETUP.md) - SDK package configuration
- [Deployment Guide](../guides/DEPLOYMENT.md) - Production deployment
