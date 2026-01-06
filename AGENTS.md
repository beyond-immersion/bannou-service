# AGENTS Guidelines for Bannou

This repository is a schema-driven monoservice for multiplayer games. Follow these
instructions when working with Codex or other coding agents.

## Sources of Truth (read first)

- `CLAUDE.md` - mandatory constraints and workflows
- `docs/reference/TENETS.md` - non-negotiable development rules
- `docs/BANNOU_DESIGN.md` - architecture and design philosophy
- `Makefile` - authoritative commands and patterns
- `docs/guides/TESTING.md` - required for any testing work

## Non-Negotiable Rules (highlights)

- Schema-first development: update `/schemas/` and regenerate; never edit generated code.
- Only manual code in service plugins is the service implementation (e.g. `lib-foo/FooService.cs`).
- Do not edit `*/Generated/` files, controllers, interfaces, models, or config classes.
- Never use null-forgiving operators (`!`) or cast null to non-nullable types.
- Avoid `?? string.Empty` unless it matches the two documented exceptions and includes a comment.
- Prefer `.env` files; never `export` environment variables.
- Never run integration tests (`make test-http`, `make test-edge`, `make test-infrastructure`, `make all`) unless explicitly asked.
- Forbidden without explicit approval: `git checkout`, `git stash`, `git reset`, `mv` (for code files).
- Never commit unless explicitly requested by the user.

## Schema-First Workflow

1. Edit the OpenAPI YAML in `/schemas/`.
2. Generate code: `scripts/generate-all-services.sh` (or `make generate`).
3. Implement only in the service implementation class.
4. Format: `make format`.

## Verification

- Do not claim completion until `dotnet build` succeeds (use `make build`).
- For any testing-related task, first read `docs/guides/TESTING.md` in full and respond with:
  "I have referred to the service testing document."

## Useful Commands (from `Makefile`)

| Command | Purpose |
| --- | --- |
| `make build` | Build all .NET projects |
| `make generate` | Regenerate services, SDK, docs |
| `make format` | Fix formatting and line endings |
| `make test` | Unit tests (run only if requested) |

## Coding Guidelines (short list)

- Use generated config classes; do not call `Environment.GetEnvironmentVariable` directly.
- Use infrastructure libraries (lib-state, lib-messaging, lib-mesh) instead of direct DB/queue/HTTP access.
- Return `(StatusCodes, TResponse?)` tuples from service methods.
- Use `BannouJson` for JSON serialization.

If a rule is unclear, stop and ask for direction.
