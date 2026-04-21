# SpriteBatcher CLI Tool Design

> **Type**: Design
> **Status**: Active
> **Created**: 2026-04-19
> **Last Updated**: 2026-04-19
> **North Stars**: #4 (Ship Games Fast), #5 (Emergent Over Authored)
> **Related Designs**: [SPRITE-COMPOSER-SDK.md](SPRITE-COMPOSER-SDK.md) (parent — SpriteBatcher is that document's § Phase 3.5), [SPRITE-COMPOSER.md](../sdks/SPRITE-COMPOSER.md) (deep dive the batcher reuses verbatim), [SPRITE-THEORY.md](../sdks/SPRITE-THEORY.md) (underlying computation primitives)
> **Primary Consumer**: Defenders of Ba'gata — batched sprite-sheet production for the full roster (heroes, troops, enemies, bosses, NPCs) without the interactive editor in the loop
> **SDK Naming Reference**: `defenders-kb/00-meta/QUESTIONS-RESOLVED.md` § Q76 (2026-04-19) — canonical SDK stack uses "sprite-composer" (renamed from pixel-composer in D72)

## Summary

SpriteBatcher is a headless CLI tool that drives `sprite-composer` + an engine bridge to execute sprite-sheet captures without UI. It consumes the same `.spriteproj.json` files the interactive editor uses — a single `SpriteProject` can drive every capture for an entire class of content (heroes, troops, enemies, bosses) through the batcher, and that same file opens in the interactive editor when a designer wants to tweak a variant.

The batcher exists because the multi-variant `SpriteProject` shape (see SPRITE-COMPOSER-SDK.md § Decision 10.12) collapses Defenders' 50–80 character variants to ~4 project files, and at that scale batch capture is the normal production path — not an occasional CI convenience. Interactive capture through the editor remains for iteration and one-off tweaks; the batcher handles scheduled rebuilds, CI regressions, and bulk production.

This document is the complete design for the tool. Implementation lands as Phase 3.5 of the Sprite Composer SDK roadmap — alongside or just after Phase 3 (sprite-composer-stride bridge), once there is at least one bridge implementation the batcher can drive.

---

## Part 1: Overview & Goals

### What it is

A `dotnet` CLI tool at `tools/sprite-batcher/` that:

1. Accepts one or more `.spriteproj.json` files on the command line.
2. Instantiates an `ISpriteComposerBridge` implementation (Stride by default) in a long-running process.
3. For each project, runs `sprite-composer.CaptureSession.ExecuteAsync` across every `VariantBinding` × `CameraRig` × effective animation × frame, in the same loop order the interactive editor uses.
4. Exports per-group (per variant × rig) atlases + JSON metadata via `ExportPipeline.ExportAsync`.
5. Reports a structured summary: variants succeeded / failed, frames captured / skipped, atlases produced, per-frame errors with full identity (variant, rig, angle, animation, frame).
6. Exits with a status code meaningful to CI (0 = all projects succeeded, non-zero = any project failed).

### What it is NOT

- **Not a capture reimplementation.** The batcher reuses `sprite-composer`'s `CaptureSession` verbatim. It does not bypass the composer to call the bridge directly. Any behavior that differs between the interactive editor and the batcher is a bug — they are the same pipeline with different front-ends.
- **Not an authoring tool.** The batcher does not create or modify `SpriteProject` files. Projects are authored in the interactive editor (or hand-edited for simple changes) and checked into version control. The batcher is a consumer.
- **Not a sprite runtime.** The batcher produces atlases + JSON metadata that the game runtime consumes at playback time. Runtime sprite rendering, animation playback, and lighting are out of scope.

### Design principles

1. **Parity with the interactive editor.** Same `SpriteProject` format, same `ISpriteComposerBridge` contract, same `CaptureSession` loop, same `ExportPipeline`. If the editor can produce a certain output, the batcher can produce the same bytes.
2. **Per-project isolation.** One project failing does not affect subsequent projects — the batcher continues to the next and reports partial success. Within a project, per-variant error isolation (from the capture session) continues to handle individual variant failures.
3. **CI-friendly.** Deterministic outputs, meaningful exit codes, structured logs, `--dry-run` for validation without rendering, optional machine-readable summary.
4. **Single-process, single-bridge.** One CLI invocation, one bridge instance. Multiple projects in the same invocation share the bridge lifetime. No bridge re-initialization between projects unless `--parallel > 1` requires multiple bridge instances (see § Part 4).

---

## Part 2: Execution Model

```
SpriteBatcher entry point
  ├── Parse CLI arguments → BatcherOptions
  ├── Validate option combinations (e.g., --parallel > 1 must be supported by the selected bridge)
  ├── If --dry-run:
  │     For each project:
  │       Load .spriteproj.json → SpriteProject via ProjectSerializer
  │       Compute CaptureManifest.AggregateMultiVariant across every variant
  │       Print expected totals (variants, rigs, frames captured, frames mirrored, atlases)
  │     Exit 0 (validation pass) or non-zero (invalid project)
  │
  ├── Instantiate bridge (Stride by default; --bridge flag selects others as they land)
  │
  ├── For each project (sequentially, or in parallel per --parallel):
  │     Load .spriteproj.json → SpriteProject
  │     Create SpriteComposer (headless — no UI events)
  │     composer.SetBridge(bridge)
  │     composer.LoadProject(projectPath)                     (effectively: deserialize + assign)
  │     Apply CLI filters (--filter-variants, --filter-rigs) to project.Variants / ExportOptions.RigsToExport
  │     result ← await composer.StartCaptureAsync(ct)          (returns CaptureResult)
  │     await composer.ExportAsync(result, ct)                 (writes atlas PNGs + JSON metadata per group)
  │     Record summary entry: variants OK, variants failed, frames captured, errors by (variant, rig)
  │
  ├── Dispose bridge
  └── Print aggregated summary; exit 0 if every project succeeded, non-zero otherwise
```

**Loop ownership**: The batcher does not own the capture loop. `CaptureSession` owns it. The batcher's responsibility ends at creating the composer, applying CLI filters, and calling `StartCaptureAsync` + `ExportAsync`. This preserves behavioral parity with the interactive editor.

**Filter application**: `--filter-variants` and `--filter-rigs` translate to project-level filters applied before the capture session starts:

- `--filter-variants <regex>`: `project.Variants = project.Variants.Where(v => Regex.IsMatch(v.Variant.Name, regex))` — only matching variants are captured.
- `--filter-rigs <regex>`: `project.ExportOptions.RigsToExport = project.Rigs.Where(r => Regex.IsMatch(r.Name, regex)).Select(r => r.Name).ToList()` — only matching rigs are exported (captures still iterate all rigs, but only matching rigs are written to disk).

**Output directory override**: `--output <dir>` overrides `project.ExportOptions.OutputDirectory` for the invocation. If the CLI flag is absent, each project's own `OutputDirectory` applies.

---

## Part 3: CLI Surface

```
sprite-batcher
  --projects <path>                   Required. Repeatable. Path to a .spriteproj.json file.
  --output <dir>                      Optional. Override every project's OutputDirectory for this invocation.
  --parallel <N>                      Optional. Default 1. Capture N variants in parallel (GPU memory permitting).
  --bridge <stride|godot|unity>       Optional. Default stride. Selects the bridge implementation.
  --asset-bundles <dir>               Optional. Directory containing .bannou asset bundles (or loose FBX the
                                      filesystem asset source will register as single-asset bundles).
                                      The bridge uses this to resolve AssetReference(BundleId, AssetId).
  --filter-variants <regex>           Optional. Only capture variants whose Variant.Name matches the regex.
                                      Applied as a pre-capture filter on project.Variants.
  --filter-rigs <regex>               Optional. Only export rigs whose Name matches. Applied via
                                      project.ExportOptions.RigsToExport so captures still execute but
                                      non-matching rigs are not written to disk.
  --dry-run                           Optional. Validate projects + compute expected CaptureManifest totals
                                      for each; print a summary; skip all rendering and asset writes.
  --verbose                           Optional. Per-frame log output (normally: per-variant summary only).
  --summary-json <path>               Optional. Write a machine-readable summary to <path>. Schema in § Part 5.
  --fail-fast                         Optional. Stop processing remaining projects on the first failed project.
                                      Off by default — every project gets a chance to run.
  --help / -h                         Show this help and exit.
  --version                           Print version and exit.
```

### Worked example — Defenders full production build

```bash
sprite-batcher \
  --projects ./sprites/heroes.spriteproj.json \
  --projects ./sprites/troops.spriteproj.json \
  --projects ./sprites/enemies.spriteproj.json \
  --projects ./sprites/bosses.spriteproj.json \
  --output ./assets/sprites/ \
  --asset-bundles ./synty-assets/ \
  --parallel 1 \
  --verbose
```

Runs every Defenders project sequentially through the Stride bridge, writes atlases + JSON into `./assets/sprites/`, resolves Synty asset references from the bundle directory, logs per-frame progress.

### Worked example — CI regression gate

```bash
sprite-batcher \
  --projects ./sprites/heroes.spriteproj.json \
  --dry-run \
  --summary-json ./build-artifacts/sprite-batch-dryrun.json
```

Validates the heroes project file (referential integrity, pattern placeholders, AnimationSet bindings), computes expected capture totals, writes a machine-readable report. Exits 0 only if the project is structurally valid.

### Worked example — Hot-fix single boss

```bash
sprite-batcher \
  --projects ./sprites/bosses.spriteproj.json \
  --filter-variants '^jorak_final$' \
  --output ./assets/sprites/ \
  --asset-bundles ./synty-assets/
```

Runs only the `jorak_final` variant from the bosses project. All rigs, all animations on that variant, full export. Useful when one boss's art updated and the other bosses' atlases are already current.

---

## Part 4: Parallelism

### The GPU serialization constraint

Most engine bridges (Stride, Godot, Unity) render on a single GPU command queue per bridge instance. Rendering frame N of variant A while rendering frame M of variant B on the same bridge instance is not supported — render commands must be serialized per bridge.

This means:

- `--parallel 1` (default): One bridge instance, one variant at a time. Safe for every bridge.
- `--parallel N` (N > 1): Requires N concurrent bridge instances. Each bridge holds its own GPU resources (render targets, staging textures, loaded models). Memory pressure scales with N.

### Supported modes per bridge

| Bridge | `--parallel 1` | `--parallel N` |
|--------|---------------|----------------|
| `stride` | Supported | Experimental — requires `N` separate Stride `Game` instances in-process. Not validated for Phase 3.5 initial implementation. |
| `godot` (future) | Planned | Same constraint as Stride. |
| `unity` (future) | Planned | Same constraint as Stride. |

Each bridge's documentation declares whether `--parallel > 1` is supported and what memory / GPU budget it requires. The CLI rejects unsupported combinations at startup.

### Per-project vs per-variant parallelism

`--parallel` applies at the **variant** level, not the project level. The batcher runs one project at a time; within that project, up to `--parallel` variants are captured concurrently. This matches how the multi-variant `SpriteProject` is structured — variants in the same project share rigs and AnimationSets, so variant-parallelism within a project is natural.

Cross-project parallelism (running heroes + troops projects simultaneously) is not supported. Each project loads its own `SpriteProject` and applies its own filters; the bridge's state transitions between projects (asset cache warming, render-target re-sizing for different frame sizes) make cross-project concurrency fragile.

---

## Part 5: Output Artifacts & Error Handling

### Files produced

For each `(variantName, rigName)` group in each project:

- `<variant>_<rig>_<atlas>.png` — the color atlas (or multiple, for multi-atlas overflow).
- `<variant>_<rig>_<atlas>_normal.png` — the normal map atlas, only when `rig.IncludeNormalMap AND bridge.SupportsDepthCapture`.
- `<variant>_<rig>.json` — the `SpriteSheet` metadata.

Filename templates are the project's `ExportOptions.AtlasFilenamePattern` / `NormalMapFilenamePattern` / `MetadataFilenamePattern`. `ExportPipeline.ValidatePatterns` runs at the start of each project's export and throws if any required placeholder (`{variant}`, `{rig}`) is missing — the batcher catches this and reports a project-level failure without writing any files for that project.

### Per-item error isolation

At three levels of granularity:

1. **Per-project**: A project whose `SpriteProject` fails to deserialize, fails referential integrity, or fails pattern validation is recorded as a project-level failure. The batcher moves to the next project (unless `--fail-fast` is set).
2. **Per-variant**: A variant whose model fails to load (missing asset bundle, malformed FBX, bone mismatch in equipment) raises an exception from the capture session. The session records it as a `CaptureError` with `VariantName` stamped, skips the variant's remaining captures, and continues to the next variant.
3. **Per-frame**: A frame whose bridge call throws (GPU readback failure, animation evaluation error) is recorded in `CaptureError` and the session continues to the next frame. Standard IMPLEMENTATION TENETS per-item error isolation pattern (see CLAUDE-PRACTICES.md § Per-Item Error Isolation and sprite-composer's capture session pseudocode).

### Structured summary (console output)

At the end of every batcher run (successful or failed), the console prints:

```
sprite-batcher summary
───────────────────────
Projects: 4 total | 3 succeeded | 1 failed

Heroes (sprites/heroes.spriteproj.json)
  Variants:  4 succeeded / 0 failed
  Captured:  3840 frames | Mirrored: 2560 | Atlases: 8
  Elapsed:   3m 12s

Troops (sprites/troops.spriteproj.json)
  Variants:  27 succeeded / 0 failed
  Captured:  25920 frames | Mirrored: 17280 | Atlases: 54
  Elapsed:   21m 34s

Enemies (sprites/enemies.spriteproj.json)
  Variants:  20 succeeded / 0 failed
  Captured:  19200 frames | Mirrored: 12800 | Atlases: 40
  Elapsed:   16m 48s

Bosses (sprites/bosses.spriteproj.json)  ✗ FAILED
  Variants:  5 succeeded / 1 failed
  Captured:  4800 frames | Mirrored: 3200 | Atlases: 10
  Failed:    jorak_final — IOException("Missing asset bundle entry 'Jorak_Final'")
  Elapsed:   4m 02s

Exit code: 1
```

### Structured summary (--summary-json)

Schema for the JSON emitted when `--summary-json <path>` is set:

```json
{
  "version": "1.0",
  "generator": "BeyondImmersion.Bannou.SpriteBatcher",
  "startedAt": "2026-04-19T10:00:00Z",
  "completedAt": "2026-04-19T10:45:36Z",
  "totalElapsedMs": 2736000,
  "projects": [
    {
      "path": "sprites/heroes.spriteproj.json",
      "name": "Heroes",
      "status": "Succeeded",
      "variantsSucceeded": 4,
      "variantsFailed": 0,
      "framesCaptured": 3840,
      "framesMirrored": 2560,
      "atlasesProduced": 8,
      "elapsedMs": 192000,
      "errors": []
    },
    {
      "path": "sprites/bosses.spriteproj.json",
      "name": "Bosses",
      "status": "Failed",
      "variantsSucceeded": 5,
      "variantsFailed": 1,
      "framesCaptured": 4800,
      "framesMirrored": 3200,
      "atlasesProduced": 10,
      "elapsedMs": 242000,
      "errors": [
        {
          "variantName": "jorak_final",
          "rigName": "TopDown-8Dir",
          "angleName": "",
          "animationName": "",
          "frameIndex": -1,
          "exceptionType": "IOException",
          "message": "Missing asset bundle entry 'Jorak_Final'",
          "timestamp": "2026-04-19T10:42:12Z"
        }
      ]
    }
  ],
  "exitCode": 1
}
```

This format is consumed by CI dashboards, Git commit statuses, and batch-log viewers. The per-frame `errors` array is bounded (default cap: 1000 entries per project; excess entries are summarized as `"...and N more"`) to prevent unbounded JSON growth on catastrophic failures.

---

## Part 6: Integration with CI/CD

### Headless bridge requirement

The Stride bridge normally requires a graphics context. Phase 1.5's Stride capture spike (see SPRITE-COMPOSER-SDK.md § Phase 1.5) validates whether Stride can run without a window — if not, the CI runner needs a GPU with a display (Xvfb + GPU acceleration on Linux, normal console on Windows). This is a bridge-specific concern; the batcher does not abstract it. Each bridge's documentation declares its headless-mode support level.

### Exit codes

| Code | Meaning |
|------|---------|
| 0 | All projects succeeded |
| 1 | At least one project failed (per-frame errors within succeeding projects do NOT cause exit 1) |
| 2 | CLI argument error — no projects executed |
| 3 | Bridge initialization failure — no projects executed |
| 4 | Bridge does not support requested `--parallel N` |

### Deterministic outputs

Atlas byte content is deterministic given the same `SpriteProject`, same bridge implementation, and same engine version. Atlas layout, frame ordering, mirror generation, and JSON serialization are all deterministic in sprite-theory. The only variable is the bridge's rendered pixel data, which may differ across graphics-driver versions. In practice, pinning a Stride version + driver version + CI runner image produces bitwise-stable atlases.

### Reproducibility tag

The `--summary-json` output records the bridge version and engine version so CI can correlate atlas regressions to specific infrastructure upgrades. This is bridge-supplied metadata — each bridge's `IBridgeVersionInfo` surface declares its version string (TBD in Phase 3).

---

## Part 7: Relationship to the Interactive Editor

Both the batcher and the interactive Defenders Composer (Phase 4) consume the same `SpriteProject`. No behavioral difference in capture logic:

| Aspect | Interactive Editor | SpriteBatcher |
|--------|-------------------|---------------|
| Project format | `.spriteproj.json` | `.spriteproj.json` (identical) |
| Capture engine | `sprite-composer.CaptureSession` | `sprite-composer.CaptureSession` (same class) |
| Bridge | `ISpriteComposerBridge` (pluggable) | `ISpriteComposerBridge` (pluggable) |
| Loop order | per-variant → per-rig → per-angle → per-animation → per-frame | identical |
| Pivot resolution | 4-step order (Override / AnchorBone / Bounds / Default) | identical |
| Pause/cancel | Yes (UI controls) | No (CLI runs to completion; kill process to abort) |
| Export | `ExportPipeline.ExportAsync` | `ExportPipeline.ExportAsync` (same class) |
| Progress UX | UI progress bars + event stream | Structured console log (+ `--verbose` per-frame lines) |
| Undo/redo | Yes (UI commands) | N/A (batcher does not modify projects) |
| Preview | Yes (post-capture playback) | N/A (captures written to disk, no in-process preview) |

This parity is the design's load-bearing property. A capture regression that the batcher produces is a regression in the composer or the bridge, not in the batcher — which means the interactive editor will reproduce it, and designers can investigate interactively. A capture that works interactively but fails in the batcher is almost certainly a bridge-headless-mode issue (see § Part 6), not a sprite-composer bug.

---

## Part 8: Sample Invocations

### Defenders full production build

```bash
sprite-batcher \
  --projects ./sprites/heroes.spriteproj.json \
  --projects ./sprites/troops.spriteproj.json \
  --projects ./sprites/enemies.spriteproj.json \
  --projects ./sprites/bosses.spriteproj.json \
  --output ./assets/sprites/ \
  --asset-bundles ./synty-assets/ \
  --parallel 1 \
  --summary-json ./build-artifacts/sprite-batch.json \
  --verbose
```

### CI nightly rebuild

```bash
sprite-batcher \
  --projects ./sprites/*.spriteproj.json \
  --output ./ci-artifacts/sprites/ \
  --asset-bundles ./ci-assets/ \
  --summary-json ./ci-artifacts/sprite-batch-$(date +%Y%m%d).json \
  --fail-fast
```

(Shell globbing expands `--projects ./sprites/*.spriteproj.json` to one `--projects` per file; the CLI accepts the flag repeatedly.)

### Validation-only mode (no capture)

```bash
sprite-batcher \
  --projects ./sprites/heroes.spriteproj.json \
  --projects ./sprites/troops.spriteproj.json \
  --projects ./sprites/enemies.spriteproj.json \
  --projects ./sprites/bosses.spriteproj.json \
  --dry-run \
  --summary-json ./build-artifacts/sprite-validation.json
```

### Developer loop — single variant rebuild

```bash
sprite-batcher \
  --projects ./sprites/troops.spriteproj.json \
  --filter-variants '^spearman-iron$' \
  --output ./assets/sprites/ \
  --asset-bundles ./synty-assets/
```

### Single-rig regeneration (e.g., re-capture all side-views after a pivot tweak)

```bash
sprite-batcher \
  --projects ./sprites/heroes.spriteproj.json \
  --projects ./sprites/troops.spriteproj.json \
  --filter-rigs '^SideView-Brawler$' \
  --output ./assets/sprites/ \
  --asset-bundles ./synty-assets/
```

---

## Part 9: Open Questions

### 1. Should the batcher support incremental captures?

**Open**: An incremental mode would compute a hash of each `VariantBinding` + rig + effective animation config + model asset content, compare against a manifest of previously-captured outputs, and only re-capture variants whose hash changed. For Defenders' 60-variant roster, a typical change (one hero's weapon mesh) would re-capture 1 variant in ~1 minute instead of re-capturing everything in ~45 minutes.

**Blocker**: Requires a stable hash of the `SpriteProject` + resolved asset bundle content. Asset bundle content hashing is the bridge's responsibility and not yet specified. Deferred until asset-bundler supplies content hashes (tracked in asset-bundler roadmap).

**Recommendation**: Not in scope for Phase 3.5 initial implementation. Add a `--force` flag from the start so future incremental mode can be the default and `--force` overrides it.

### 2. Should `--parallel N` spawn separate CLI processes or use in-process bridge instances?

**Open**: Process-per-parallel gives clean GPU resource isolation but duplicates bridge startup cost (~1–3 seconds for Stride). In-process parallel shares startup cost but requires the bridge to support concurrent `Game` instances — non-trivial for Stride.

**Recommendation**: In-process for `--parallel N` where the bridge explicitly supports it (declared via a capability flag on `ISpriteComposerBridge` or a bridge-specific static method). Fall back to process-per-parallel via self-spawning subprocesses when the bridge does not support in-process concurrency. Defer both implementations to Phase 3.5's second iteration — ship `--parallel 1` only in the first version.

### 3. Summary JSON schema versioning

**Open**: The `--summary-json` schema will evolve as CI consumers request fields (per-animation timing, bridge version, asset bundle checksum, etc.). A schema version field is included, but there is no protocol yet for migrating old consumers.

**Recommendation**: The summary JSON already declares `"version": "1.0"`. Breaking changes bump the major version; additive changes keep 1.x. Consumers should match the major version and tolerate unknown fields.

### 4. Should the batcher retry transient bridge failures?

**Open**: GPU readback can occasionally fail on contended systems (driver timeouts, staging texture allocation failures). A per-frame retry (1–2 attempts) might recover from transients that otherwise count as capture errors.

**Recommendation**: Out of scope for Phase 3.5 initial. If empirical data from early runs shows significant transient failure rates, add a `--retry-frames N` flag later. Until then, the per-frame error isolation pattern surfaces transient failures in the summary for investigation.

---

## Part 10: Implementation Checklist

When Phase 3.5 starts, the following work items land in this order:

1. **Project skeleton**: `tools/sprite-batcher/sprite-batcher.csproj` targeting `net10.0`; references `sprite-composer` and at least the Stride bridge. Add to `bannou-sdks.sln`.
2. **CLI argument parser**: `System.CommandLine` or equivalent. Validate option combinations at parse time.
3. **Dry-run path**: `ProjectSerializer.Deserialize` → `ComputeCaptureManifest` → print summary. No bridge, no rendering. Exercises every structural validation the batcher needs.
4. **Bridge factory**: `--bridge stride` instantiates `StrideSpriteComposerBridge` (Phase 3 output). Factory hook for future bridges.
5. **Per-project capture + export**: `SpriteComposer.StartCaptureAsync` + `ExportAsync`. Apply `--filter-variants` / `--filter-rigs` before capture.
6. **Summary formatter**: Console output + `--summary-json` writer.
7. **Integration test**: End-to-end capture of a synthetic 1-variant project with a mock bridge, asserting atlas bytes + JSON metadata match expected fixtures.
8. **Documentation**: README in `tools/sprite-batcher/` with the sample invocations above; link from the SDK guide.

### Not in scope for Phase 3.5 initial

- `--parallel N` > 1 (requires bridge-side support validation).
- Incremental captures (requires asset bundle content hashing).
- Retry logic.
- Cross-project parallelism.

---

*This document is the complete design for the SpriteBatcher CLI tool. For the architectural context (Theory/Composer pattern, multi-variant SpriteProject, bridge contract), see [SPRITE-COMPOSER-SDK.md](SPRITE-COMPOSER-SDK.md). For the sprite-composer API the batcher consumes verbatim, see [SPRITE-COMPOSER.md](../sdks/SPRITE-COMPOSER.md) and its implementation map [SPRITE-COMPOSER.md](../sdks/maps/SPRITE-COMPOSER.md).*
