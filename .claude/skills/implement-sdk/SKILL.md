---
description: "Implement an SDK from scratch using its deep dive, implementation map, and planning document as specification. Sequential worker agents per subsystem with mandatory audit gates between each. One SDK per invocation."
argument-hint: "<sdk-name> - SDK to implement (e.g., 'sprite-theory'). Must have deep dive + implementation map."
disable-model-invocation: true
---

# Implement SDK

Implements a Bannou SDK from scratch using its **deep dive** (high-level design), **implementation map** (method-by-method pseudocode), and **planning document** (project structure and architecture) as the specification. The parent agent orchestrates scaffolding, dispatches one worker per subsystem with mandatory audit between each, then adds comprehensive tests.

**This skill is for SDKs that have documentation but NO code.** If the SDK already has code, use `/implement-feature` instead.

## Prerequisites

The SDK must have:
1. **Deep dive** at `docs/sdks/{SDK_NAME}.md` — overview, public API surface, data model, computation pipeline
2. **Implementation map** at `docs/sdks/maps/{SDK_NAME}.md` — method-by-method pseudocode, data structures, algorithms
3. **Planning document** at `docs/planning/*` — project structure, package identity, directory layout (recommended, not required)

If deep dive or implementation map is missing → STOP and report.

## Rules

1. **The implementation map is the specification.** Pseudocode → C# code, mechanically. If you discover the map is wrong, stop and report.
2. **One worker per subsystem, sequential.** Never dispatch the next worker before auditing the previous. The `audit: true` flag on `dispatch_worker` enforces this mechanically.
3. **Audit means read and verify.** Read the worker's output, compare against the map, check for tenet compliance, make corrections. Only then call `clear_audit_gate`.
4. **Tests are a separate phase.** All subsystems are implemented first, then tests are added per subsystem.
5. **Use existing SDKs as structural examples.** music-theory, voxel-core, storyline-theory show the conventions for .csproj, namespaces, directory layout, test projects.
6. **No shortcuts on shared types.** Records must be immutable. Enums must match the map. Value types must have the documented fields.
7. **`coverage_check` after each subsystem.** Run the coverage check tool to mechanically verify progress.

---

## Phase 0: Parse, Load Context, Plan

### Step 1: Parse the SDK Name

Extract `SDK_NAME` from the argument. Normalize to kebab-case (e.g., `sprite-theory`).

Derive:
- `PascalName` — `SpriteTheory`
- `PackageId` — `BeyondImmersion.Bannou.SpriteTheory`
- `Namespace` — `BeyondImmersion.Bannou.SpriteTheory`

### Step 2: Verify Prerequisites

Read and verify existence of:
```
docs/sdks/{SDK_NAME}.md         (deep dive)
docs/sdks/maps/{SDK_NAME}.md    (implementation map)
```

Search for a planning document:
```bash
find docs/planning/ -iname "*${SDK_NAME}*" -o -iname "*${PascalName}*" | head -5
```

### Step 3: Load Context

```
prepare_context(profile: "dev")
```
Read all composites.

Then read ALL three documents in full using `get_document`:
- Deep dive
- Implementation map
- Planning document (if found)

### Step 4: Check GitHub Issues

```bash
gh issue list --search "{SDK_NAME}" --limit 20
```

### Step 5: Examine Existing SDK Patterns

Read .csproj files from reference SDKs:
```bash
find sdks/music-theory/ -name "*.csproj" -exec cat {} \;
find sdks/voxel-core/ -name "*.csproj" -exec cat {} \;
```
Note the patterns: TargetFrameworks, PackageId, RootNamespace, InternalsVisibleTo, documentation settings.

If a test project exists for a reference SDK, examine its structure too:
```bash
ls sdks/music-theory.tests/ 2>/dev/null || ls sdks/voxel-core.tests/ 2>/dev/null
```

### Step 5.5: Design Fidelity Checkpoint (MANDATORY)

Before decomposing subsystems or dispatching workers, verify your implementation approach matches what the deep dive, implementation map, and planning document prescribe STRUCTURALLY.

**Why this exists:** Incident #19 (`docs/reference/INCIDENT-HISTORY.md`): an agent shipped a reflection-based `DirectDispatchHelper` when `docs/planning/BANNOU-EMBEDDED.md § Section 3` prescribed statically-typed delegate dispatch per generated client method. Functionally equivalent; structurally opposite; AOT-hostile. The divergence was never flagged. SDKs ship to consumers including iOS targets — AOT-hostile patterns here block the entire consumer surface.

Per QUALITY TENETS (T33 Design Specification Fidelity), when a planning doc or map prescribes a technical approach, the implementation MUST match it STRUCTURALLY — not just functionally.

**Protocol:**

1. **Re-read the planning doc's technical-approach sections.** The planning doc you found in Step 2 often contains a "Section 3 — Architecture" or "Project Structure" or "Data Flow" section that prescribes specific shapes (delegate signatures, whether to use source generators, whether types are compile-time-known, whether reflection is permitted).

2. **For each worker task you're about to dispatch**, write the intended approach in 2-3 bullets. For each bullet, quote the specific planning-doc / map line that prescribes that shape.

3. **Flag any divergence from prescription.** If you intend to have a worker write reflection-based code where the map specifies typed dispatch, that's a divergence — stop and present to the user.

4. **IMPLEMENTATION TENETS (T34 AOT Compatibility)** applies to SDK code that plugins consume. `sdks/*/` directories are AOT-disciplined by default (they ship to iOS / NativeAOT targets). `Assembly.LoadFrom`, `MakeGenericMethod` with runtime types, `MethodInfo.Invoke`, `ValueTuple.GetField("Item1")`, and `AppDomain.GetAssemblies` scans are all forbidden in SDK implementation. Test projects (`sdks/*.tests/`) are exempt.

5. **If you catch yourself planning "the worker can use reflection as a shortcut, the functional behavior will be the same"**, you are about to create Incident #20 in an SDK context. Stop, present the divergence, wait for direction.

### Step 6: Decompose into Subsystems

From the implementation map and planning document, identify subsystems (directory groupings). Create a task for each:

Use `TaskCreate` for:
1. **Scaffolding** — project creation, .csproj, solution, directory structure (YOU do this, not a worker)
2. **One task per subsystem** — each dispatched to a worker with `audit: true`
3. **One task per subsystem for tests** — each dispatched to a worker with `audit: true`
4. **Final verification** — coverage check, full build, full test run

Each task description MUST include:
- Which subsystem (directory/namespace)
- Which types and methods from the map
- The exact pseudocode section references
- Required reading list for the worker
- Acceptance criteria (build passes, coverage check shows types present)

Set up `blockedBy` dependencies: scaffolding blocks all subsystems, subsystems block tests.

Present the task list and begin.

---

## Phase 1: Scaffolding (Parent Does This Directly)

**You do this yourself — no worker.** Scaffolding requires solution-level knowledge and cross-file consistency.

### Step 1: Create the SDK Project

Create `sdks/{sdk-name}/{PackageId}.csproj` following the music-theory/voxel-core pattern:
- `TargetFrameworks`: `net8.0;net9.0`
- `PackageId`, `RootNamespace`, `AssemblyName`: all `BeyondImmersion.Bannou.{PascalName}`
- `VersionPrefix`: `0.1.0`
- NuGet metadata (Authors, Description, PackageTags, License, URLs)
- `GenerateDocumentationFile`: true
- `TreatWarningsAsErrors`: true
- `InternalsVisibleTo` for the test project and any known consumer SDKs

Dependencies from the map's Dependencies section only.

### Step 2: Create Directory Structure

From the planning document's "Project Structure" section, create all subdirectories:
```bash
mkdir -p sdks/{sdk-name}/{Subsystem1} sdks/{sdk-name}/{Subsystem2} ...
```

### Step 3: Create the Test Project

Create `sdks/{sdk-name}.tests/{PackageId}.Tests.csproj`:
- Same TargetFrameworks
- Reference to the SDK project
- xUnit v3 test dependencies (match existing test projects)

### Step 4: Add to Solution

```bash
dotnet sln sdks/bannou-sdks.sln add sdks/{sdk-name}/{PackageId}.csproj
dotnet sln sdks/bannou-sdks.sln add sdks/{sdk-name}.tests/{PackageId}.Tests.csproj
```

### Step 5: Verify Build

```bash
dotnet build sdks/{sdk-name}/{PackageId}.csproj --no-restore > /tmp/{sdk-name}-scaffold-build.txt 2>&1
```

Must pass before proceeding.

---

## Phase 2: Subsystem Implementation (Sequential Workers + Audit)

For EACH subsystem task (in dependency order):

### Step A: Dispatch Worker

Use `dispatch_worker` with `audit: true`:

```
dispatch_worker(
  task: "<full task description with pseudocode references, type list, acceptance criteria>",
  scopes: ["sdk:{sdk-name}"],
  audit: true,
  timeout_ms: 600000
)
```

The worker's task description MUST include:
1. Which files to create (exact paths)
2. Which types and methods to implement (with pseudocode from the map)
3. The namespace to use
4. Required reading: deep dive + implementation map paths
5. Build command to verify: `dotnet build sdks/{sdk-name}/{PackageId}.csproj --no-restore`
6. Instruction to call `stop_scope` as final action

### Step B: Audit Worker Output

After the worker completes, **you MUST do all of the following before clearing the gate**:

1. **Read the worker's output** — the full result text including stop_scope findings
2. **Read all files the worker created** — every .cs file in the subsystem
3. **Compare against the map** — does each type have all fields? Does each method follow the pseudocode?
4. **Check tenet compliance**:
   - XML documentation on public members (T19)
   - Proper types — no strings for enums/GUIDs (T25)
   - No sentinel values (T26)
   - Async methods use async/await (T23)
   - Disposables use `using` (T24)
5. **Make corrections** — edit files directly to fix any issues found
6. **Run coverage check**: `coverage_check(name: "{sdk-name}", kind: "sdk")`
7. **Verify build passes**

### Step C: Clear Gate

Only after completing Step B:
```
clear_audit_gate(attestation: "I have fully audited this agent's work and corrected it to bring in line with developer TENETS and schema-rules")
```

### Step D: Update Task

Mark the subsystem task as completed. Move to the next subsystem.

---

## Phase 3: Test Implementation (Sequential Workers + Audit)

After ALL subsystems are implemented:

For EACH subsystem's test task:

### Step A: Dispatch Test Worker

Use `dispatch_worker` with `audit: true`:

The worker's task description MUST include:
1. Which test files to create (exact paths in the test project)
2. What to test — each public method from the subsystem
3. Test naming convention: `MethodName_Condition_ExpectedResult`
4. Required: determinism tests (same input → same output for all deterministic methods)
5. Required: edge case tests (empty inputs, boundary values, invalid inputs)
6. Required: known-value tests (specific inputs from the map's examples → expected outputs)
7. Build + test commands to verify

### Step B: Audit Test Output

1. Read all test files the worker created
2. Verify test coverage — every public method has at least one test
3. Verify test quality — tests actually assert meaningful things, not just "no exception"
4. Verify naming convention compliance
5. Make corrections
6. Run tests: `dotnet test sdks/{sdk-name}.tests/{PackageId}.Tests.csproj --no-restore > /tmp/{sdk-name}-test.txt 2>&1`

### Step C: Clear Gate + Update Task

Same as Phase 2.

---

## Phase 4: Final Verification

### Step 1: Full Coverage Check

```
coverage_check(name: "{sdk-name}", kind: "sdk")
```

Report should show high coverage. Any missing items are gaps to address.

### Step 2: Full Build

```bash
dotnet build sdks/{sdk-name}/{PackageId}.csproj --no-restore > /tmp/{sdk-name}-final-build.txt 2>&1
```

### Step 3: Full Test Run

```bash
dotnet test sdks/{sdk-name}.tests/{PackageId}.Tests.csproj --no-restore > /tmp/{sdk-name}-final-test.txt 2>&1
```

### Step 4: Report

```markdown
## SDK Implementation Complete: {SDK_NAME}

### Summary
| Metric | Count |
|--------|-------|
| Subsystems | {N} |
| Types implemented | {N} |
| Methods implemented | {N} |
| Test classes | {N} |
| Test methods | {N} |
| Audit rounds | {N} |
| Corrections made | {N} |

### Coverage Check
{coverage_check output}

### Files Created
| File | Lines | Purpose |
|------|-------|---------|
| {path} | {N} | {purpose} |

### Build: {PASS/FAIL}
### Tests: {N passed, N failed}

### Audit Observations
| # | Observation | Resolution |
|---|-------------|------------|
{or "No issues discovered during audits."}

### Recommended Follow-Up
- [ ] `git diff` to review all changes
- [ ] `make format` before committing
- [ ] Update deep dive Work Tracking section
```

---

## Compaction Recovery

If context compacts mid-execution:
1. Call `TaskList` to see progress
2. Call `TaskGet` on the next pending task
3. Run `coverage_check` to see current state
4. Resume from the next pending task

The task list descriptions contain full worker prompts — independently launchable after compaction.

---

## Common Pitfalls

1. **"I'll implement multiple subsystems in one worker"** — NO. One subsystem per worker. The audit gate enforces this.
2. **"I'll skip the audit — the worker's stop_scope was clean"** — NO. stop_scope checks build, not map compliance. You must compare against the pseudocode.
3. **"I'll clear the audit gate before reading the files"** — The attestation requires you to affirm you've audited. If you haven't, you're lying.
4. **"I'll write tests inline with implementation"** — NO. Phase 3 is separate. All subsystems first, then all tests. Workers need the complete SDK to write meaningful tests.
5. **"The map has a typo/error, I'll fix it silently"** — NO. Report map observations. The map is frozen-adjacent documentation.
