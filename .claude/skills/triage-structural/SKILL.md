---
description: "Run structural tests, investigate up to 5 failure instances from a single test, and classify each as overly-broad test / easy fix / design decision required. Read-only triage — no code modifications, no fixes applied. User decides action based on the summary."
argument-hint: "[test-name] - Optional: fuzzy-matched test method name (e.g., 'telemetry', 'key builders', 'deprecation'). Omit to run all and pick from failures."
disable-model-invocation: true
---

# Triage Structural Tests

Runs structural tests, selects a failing test, investigates up to 5 failure instances in depth, classifies each, and presents a summary for the user to decide on action.

**The failure mode this skill exists to prevent**: classifying from minimal reads, then producing more text when challenged instead of going back to read more. Every phase below is structured to make that failure physically harder.

## Rules

1. **No source modifications during triage.** You do not edit code, schemas, tests, or documentation. You investigate and report. The user decides all actions.
2. **No agents.** All work is done by you directly.
3. **Tests are presumed correct.** Structural tests are frozen artifacts. If you believe a test is wrong, that is an 🟡 Overly Broad finding — present it, don't dismiss it.
4. **Investigation has a minimum evidence bar enforced in Phase 5.** A "classification" that cites no plugin documentation, no implementation map, no referenced issue, and no authoritative schema source is not a classification — it is a guess. Phase 4 defines the required reads. Phase 5 (Quality Gate) will reject classifications that lack them.
5. **Up to 5 instances per test.** If a test has more than 5 failures, pick 5 representative instances (prefer variety — different plugins, different violation patterns). Note the total count.
6. **Classification is the deliverable.** Every instance gets exactly one of the three classifications with supporting evidence.
7. **Pushback triggers investigation, not rewording.** If the user challenges any classification after the report is issued, your next response MUST cite evidence you did not have in the prior response. Rewording, reframing, or flipping classifications using only already-gathered material is the failure mode this skill exists to prevent. See Pushback Protocol at the bottom of this file.
8. **Uncertainty is read more, not ask less.** If you feel unsure about a classification, go back to Phase 4 and gather more evidence. Do not resolve uncertainty by softening language or presenting both classifications as "possible."

---

## Classifications

| Symbol | Classification | Meaning | Evidence Required |
|--------|---------------|---------|-------------------|
| 🟡 | **Overly Broad** | Test catches something outside its intended spirit — the code is reasonable but the heuristic is too coarse | Verbatim quote of the test's XML doc intent; citation from a plugin doc, implementation map, or referenced issue that positively establishes this instance as outside the test's stated intent |
| 🟢 | **Legitimate / Easy Fix** | Clear tenet or rule violation with a mechanical, unambiguous resolution | Verbatim quote of the violated tenet/rule; exact description of the mechanical fix; demonstration (from plugin doc or map) that no judgment is required — the fix is the same regardless of context |
| 🔴 | **Design Decision Required** | Legitimate concern but the resolution requires human judgment — multiple valid approaches, cross-service implications, or architectural trade-offs | Enumeration of two or more concrete fix options with specific mechanisms; citation from plugin docs, maps, or referenced issues establishing why the choice is consequential; explicit identification of the missing data/judgment that would resolve the decision mechanically |

**Rule: evidence before classification.** If you cannot produce the required evidence from Phase 4 reads, the classification is invalid regardless of how plausible it sounds. The Quality Gate in Phase 5 will catch this.

---

## Phase 1: Context & Test Execution

**Step 1a:** Load development context:
```
prepare_context(profile: "dev")
```

**Step 1b:** Determine the test to run.

**If an argument was provided** (test name — may be partial/fuzzy):

Fuzzy-match the argument against all structural test method names. The test files are:

| File | Test Class |
|------|-----------|
| `structural-tests/StructuralTests.cs` | `StructuralTests` |
| `structural-tests/SchemaValidationTests.cs` | `SchemaValidationTests` |
| `structural-tests/SourceCodePatternTests.cs` | `SourceCodePatternTests` |
| `structural-tests/ProjectValidationTests.cs` | `ProjectValidationTests` |
| `structural-tests/ConfigurationDefaultTests.cs` | `ConfigurationDefaultTests` |
| `structural-tests/ChangeFieldsCoverageTests.cs` | `ChangeFieldsCoverageTests` |
| `structural-tests/TypeSafetyTests.cs` | `TypeSafetyTests` |
| `structural-tests/PluginLifecycleTests.cs` | `PluginLifecycleTests` |
| `structural-tests/DistributedLockTests.cs` | `DistributedLockTests` |
| `structural-tests/CatchHandlingTests.cs` | `CatchHandlingTests` |
| `structural-tests/SingletonEncapsulationTests.cs` | `SingletonEncapsulationTests` |
| `structural-tests/SchemaFieldCoverageTests.cs` | `SchemaFieldCoverageTests` |

Search for the method name using keyword matching:
```bash
grep -rn 'public.*void\|public.*async Task' structural-tests/*.cs | grep -v '/obj/' | grep -i "{argument}"
```

If exactly one match → use it. If multiple matches → present the list and ask the user to pick. If zero matches → report "No test method matching '{argument}' found" and STOP.

Run the specific test:
```bash
dotnet test --project structural-tests/structural-tests.csproj --filter-method "*{MethodName}*" > /tmp/structural-triage.txt 2>&1
```

**If no argument was provided:**

Run all structural tests (non-informational):
```bash
dotnet test --project structural-tests/structural-tests.csproj > /tmp/structural-triage.txt 2>&1
```

Read the output:
```
read_file("/tmp/structural-triage.txt")
```

## Phase 2: Parse Failures & Select Test

**Step 2a:** Parse the test output. Identify:
- Total tests run / passed / failed / skipped
- For each failing test: test name, failure message, affected items

**If all tests pass** → report "All structural tests passed." and STOP.

**If a specific test was requested** (argument provided):
- If that test passed → report "{TestName} passed with no failures." and STOP.
- If that test failed → proceed to Phase 3 with that test.

**If no argument** (full suite run):

Present the failure summary:
```
## Structural Test Failures

| # | Test | Failures | File |
|---|------|----------|------|
| 1 | {TestName} | {count or "1"} | {TestClass} |

Total: {N} failing tests across {M} test classes

Which test would you like to investigate?
```

**⛔ STOP. Wait for the user to select a test.**

## Phase 3: Read Test Source & Extract Signals

**Step 3a:** Read the test file containing the selected test:
```
read_file("structural-tests/{TestFile}.cs")
```

If the file is split (continuation parts), read ALL parts.

**Step 3b:** Locate the specific test method. Extract:
1. **XML doc summary** — the test's stated intent. This is the primary source for "what is this test trying to catch?" You will quote it verbatim in the report.
2. **Test body** — the validation logic, what it scans, what patterns it detects.
3. **Assertion message** — the error text format. This tells you what the failure output looks like. **If the assertion message tells the agent what to do** (e.g., "Fix the schema, not the consumer") rather than describing what was detected, treat that as suspicious — prescriptive assertion messages lead agents to short-circuit investigation. Prefer the XML doc intent over the assertion message when they diverge.
4. **Exclusion lists** — any `*Exclusions` HashSets or `IntentionallyUncalled*` sets. These define the test's own carve-outs.

**Step 3c:** Identify the test shape:
- **Theory test** (parameterized by `Type serviceType`): Each failing service is a separate instance. Parse individual test case names from the output.
- **Fact test** (single assertion with violation list): Parse the assertion message to extract individual violation entries.

**Step 3d — MANDATORY: Extract referenced GH issues.** Scan the test's XML doc, class-level doc, file header comments, and assertion message for any issue number reference (e.g., "issue #720 follow-up", "per #608", "tracks #{N}"). Record every issue number found. These are **mandatory reads in Phase 4**. A test that cites an issue is telling you where its intent comes from — ignoring the citation is ignoring the intent.

**Step 3e — MANDATORY: Extract key concepts for docs search.** From the XML doc and assertion message, extract 2-3 keywords describing the pattern the test detects. Examples: "polymorphic identifier", "metadata bag cross-service", "accountId boundary", "nullable field coverage", "ChangeFields IsFieldSet", "x-references cleanup". These feed `search_docs` in Phase 4.

**Step 3f:** Announce what you extracted:
```
## Test Intent

**Test file:** `structural-tests/{File}.cs`
**Intent (verbatim):** "{quoted XML summary}"
**Referenced issues:** #{numbers} or "none"
**Key concepts for docs search:** {2-3 keywords}
**Total failures to investigate:** {N} ({M} to examine if >5}
```

## Phase 4: Per-Instance Investigation

**For EACH selected instance**, perform ALL required reads below BEFORE any classification analysis. None are optional. You will cite each source in the report — maintain an evidence trail per instance as you go.

**Stop-and-read trigger**: the moment you catch yourself reasoning from general knowledge, your memory of the tenets, or the file contents you read in Phase 1 instead of from a specific source you read for THIS instance, stop and read more. "I already know T25 says X" is not evidence — a direct quote from the specific rule is evidence.

### Step 4a: Identify BOTH Consumer AND Owner

From the failure message, identify **both sides of the interaction** — not just the violating code:

- **Consumer** — the plugin whose code contains the violation (usually obvious from the file path in the failure message)
- **Owner** — the plugin/service that defines the schema, type, contract, or convention that the consumer is interacting with. This is often NOT obvious from the failure message alone. Reading the consumer code tells you what type/field/client is being used; tracing that type back to its defining schema tells you the owner.

Record both. Every subsequent read step in 4b applies to BOTH services unless otherwise noted. Violations like type-safety mismatches, cross-service contract issues, and hierarchy concerns all involve two parties — reading only the consumer's code misses half the story.

### Step 4b: Required Reads (NONE ARE OPTIONAL)

The following reads are MANDATORY per instance. If a read produces "not found" or "no such plugin," record that explicitly in the evidence trail — don't silently omit.

1. **Consumer source file** — `read_file` on the full violating file (all continuation parts). Read enough surrounding context to understand how the flagged line participates in the overall method.

2. **Owner schema** — `get_schema(name: "{owner-service}")`. Read the complete schema — surrounding fields often reveal the intended typing pattern, lifecycle, or deprecation model. If the violation is about a field, READ THE FIELD DESCRIPTION — it will tell you if the field is deliberately polymorphic, deliberately opaque, or simply mistyped.

3. **Owner plugin docs** — `get_plugin_docs(name: "{owner}")` — reads BOTH deep dive AND implementation map. The deep dive explains why the schema looks the way it does and what quirks/design considerations exist. The map explains how the owner populates the field and what behavior consumers can rely on.

4. **Consumer plugin docs** — `get_plugin_docs(name: "{consumer}")` — same tool, for the consumer. Explains how the consumer is supposed to treat the field and whether this interaction is documented as a quirk, a known limitation, or standard behavior.

5. **Relevant tenet(s)** — the test's error message and XML doc cite tenet categories (FOUNDATION, IMPLEMENTATION, QUALITY). The dev profile has these in context, but re-read the **specific tenet section** referenced — including its stated exceptions, qualifications, and the "Acceptable" or "Allowed Exceptions" subsection if one exists. The tenet's exceptions list is often the answer.

6. **Referenced GH issues** — for EVERY issue number extracted in Step 3d, run:
   ```bash
   gh issue view {number} --comments
   ```
   Read the FULL issue body AND every comment. Issues frequently contain: the original problem that motivated the test, design alternatives considered, user decisions that shape the "correct" resolution, and scope changes that the issue body alone doesn't reflect. **If the test says "issue #720 follow-up, item #12," that issue is the primary source of the test's intent.**

7. **Docs search on key concepts** — for each keyword from Step 3e, run:
   ```
   search_docs(query: "{keyword}")
   ```
   If results include docs you haven't read (planning docs, guides, FAQs), read the top 2-3 relevant ones via `get_document`.

### Step 4c: Maintain the Evidence Trail

As you complete Step 4b for each instance, build an explicit list of sources consulted:

```
Evidence for Instance N:
  1. read_file plugins/lib-{consumer}/SomeFile.cs
  2. get_schema {owner-service} — specifically examined: {field name} description
  3. get_plugin_docs {owner} — deep dive section "{X}", map section "{Y}"
  4. get_plugin_docs {consumer} — deep dive section "{X}", map section "{Y}"
  5. tenet: {Category} {rule name} — specifically paragraph "{quoted text}"
  6. gh issue view {number} — summary of discussion
  7. search_docs "{keyword}" → get_document {path}
```

The minimum floor is 5 substantive entries (items 1-5 above). Items 6-7 are mandatory ONLY when Step 3d/3e surfaced them — if the test cites no issues and keyword searches produce no relevant docs, note that explicitly ("no issues referenced", "search_docs returned no new docs beyond already-loaded context"). More is fine. Fewer is a fail.

### Step 4d: Unknowns Checklist

Before proceeding to classification, answer EACH of these explicitly in your own notes (not just "yes I thought about it"):

- **Owner implementation map**: What does it say about this field/type/contract? Quote or paraphrase with section reference.
- **Consumer implementation map**: What does it say about how this consumer interacts with the owner? Same.
- **Referenced GH issue** (if any): Does it discuss the specific pattern the violation represents? What does it say? What decisions were made?
- **Docs search**: Did results reveal a documented design consideration, quirk, open question, or planning document about this pattern?
- **Specific tenet text**: What does the full rule say, including exceptions and qualifications? Quote the relevant sentence(s) verbatim.
- **The schema description**: What exactly does the field's description say? (A single-line description often reveals deliberate polymorphism, deliberate opacity, or hierarchy-isolation intent — all of which change the classification.)

If any answer is "I don't know," "I didn't find anything," or "I didn't check," you are not ready to classify. Return to Step 4b and read what's missing.

---

## Phase 5: Classification Quality Gate

Before writing each instance's classification in the report, pass it through this gate. Failing any question sends the instance back to Phase 4.

### Gate 1: Evidence Completeness

Count the substantive entries in the evidence trail for this instance. If fewer than 5, return to Phase 4. If any mandatory category from Step 4b was skipped without a "not applicable" note, return to Phase 4.

### Gate 2: Citation Defense

For the classification you intend to assign, can you satisfy the **Evidence Required** column with direct quotes or specific references from the sources you actually read?

- **🟢 Easy Fix** requires: verbatim tenet rule quote showing the violation unambiguously + mechanical fix description + demonstration (from plugin docs/map) that no judgment is required + evidence that the field/pattern is NOT intentionally polymorphic, opaque, or hierarchy-isolated.

- **🔴 Design Decision** requires: two+ named fix options with specific mechanisms (not "we could rewrite" — "Option A: split into fields X and Y per T14 pattern; Option B: split the topic; Option C: keep current shape") + plugin doc / map / issue citation explaining why the choice is consequential + explicit identification of the missing product-level, architectural, or design-intent data that would resolve the decision.

- **🟡 Overly Broad** requires: verbatim test intent quote + plugin doc / map / issue citation positively establishing that the instance falls outside the stated intent (e.g., the schema description explicitly documents this as intentional polymorphism; the referenced issue explicitly scoped this case out).

If you cannot produce the required evidence from Phase 4 reads, the classification is invalid. Either reclassify to the category whose evidence you DO have, or return to Phase 4 and read more.

### Gate 3: The Pushback Pre-Check

Before committing to the classification, ask: **"If the user challenges this classification and says 'explain this one to me,' what source would they point me at that I have not yet read?"**

- If you can identify such a source, read it NOW, before finalizing the classification.
- If you cannot identify one, the classification is defensible — proceed.

This gate catches the failure mode where the agent classifies on shallow evidence, then scrambles to produce more text when challenged. If there's a source you haven't read, read it in the triage, not in the pushback.

### Gate 4: Anti-Flip Commitment

The classification you assign in the report is the classification you **defend under pushback**. You do NOT flip classifications in response to challenge unless the user points at evidence you did not have access to during Phase 4.

If your Phase 4 investigation was complete, the classification should survive challenge. If it doesn't survive because a source was missed, the missed source is the new evidence — go read it. The legitimate path under pushback is: read more sources → cite them → either hold the classification with the new evidence or change it because the new evidence demands it. The illegitimate path is: produce more text using the same evidence.

---

## Phase 6: Report

Present the full triage report:

```markdown
## Structural Test Triage: {TestName}

**Test file:** `structural-tests/{File}.cs`
**Test intent (XML summary, verbatim):** "{quoted}"
**Referenced issues:** #{numbers} or "none"
**Total failures:** {N} ({M} investigated)

### Summary

| # | Instance | Classification | Affected |
|---|----------|---------------|----------|
| 1 | {brief description} | 🟢 Easy Fix | `{file}` |
| 2 | {brief description} | 🔴 Design Decision | `{file}` |
| 3 | {brief description} | 🟡 Overly Broad | `{file}` |

### Instance Details

#### Instance N: {description}
**Classification:** {symbol} {category}
**Affected:** `{file:line}`
**Detected:** {what the test found}

**Evidence consulted:**
- {source 1 — specific section or quote referenced}
- {source 2}
- {source 3}
- {source 4}
- {source 5}
- {additional sources as needed}

**Rule/Intent (verbatim):** "{quoted}"

**Analysis:**
{For 🟢: cite the violated rule verbatim, describe the mechanical fix, demonstrate no judgment is required by referencing the plugin doc / map / issue}
{For 🔴: name each fix option with its specific mechanism, cite the doc/map/issue establishing the design tension, name the missing judgment or product-level decision}
{For 🟡: quote the test intent, cite the doc/map/issue establishing why this instance falls outside that intent}

**Unknowns that could change this classification:**
- {anything still unresolved, or "none — evidence is complete"}

{...repeat for all investigated instances}

### Classification Breakdown

| Classification | Count | Action |
|---------------|-------|--------|
| 🟢 Legitimate / Easy Fix | {N} | Can be fixed mechanically |
| 🔴 Design Decision | {N} | Requires human judgment before proceeding |
| 🟡 Overly Broad | {N} | Consider test refinement |

### Uninvestigated Failures
{If total > 5: "{N} additional failures not investigated. Re-run to examine more."}
{If total <= 5: "All failures investigated."}
```

**STOP. The user decides what to do with these findings.**

---

## Pushback Protocol

When the user challenges, questions, asks you to explain, or expresses doubt about any classification AFTER the report is issued, the following protocol applies. This is the part of the skill designed to prevent the most common failure mode.

### Rule 1: The First Response to Pushback Is Evidence Gathering, NOT Text

Do NOT produce classification analysis, revised framing, reworded explanation, or defensive elaboration in the same turn as the pushback.

The correct first response to "explain this one to me," "why is this acceptable?" or "I don't buy the classification" is:
1. Identify what you did NOT read in Phase 4 that could inform the answer
2. Read those sources via tool calls
3. THEN respond, citing the new evidence

If your first response to a pushback contains only material that was already in context when the pushback happened, you have failed this protocol.

### Rule 2: Every Response to Pushback Must Cite NEW Evidence

Every response you produce after a pushback MUST contain at least one of:
- A direct quote from a source not previously cited in this conversation
- A new finding from a tool call made in response to the pushback
- An explicit acknowledgment that the pushback points at a gap you haven't investigated, followed by the investigation in the same turn

"Let me rephrase my earlier analysis" is not new evidence. "I re-read my earlier analysis and now see that..." is not new evidence. Going back to read the plugin's deep dive that you skipped in Phase 4 IS new evidence.

### Rule 3: Do Not Flip Classifications Under Social Pressure

The user's tone, phrasing, or evident frustration is not evidence. "Explain this to me" is a request for deeper analysis backed by the evidence base, not a signal that the classification is wrong. Common failure pattern:

- Agent: "🔴 Design Decision — here's why..."
- User: "Why do you think this is acceptable?"
- Agent (failure mode): "You're right, it's 🟢 Easy Fix. Here's why..." — with NO new evidence, just reframing in response to the tone.

The correct behavior:

- Agent: "🔴 Design Decision — here's why..."
- User: "Why do you think this is acceptable?"
- Agent (correct): "That question points at something I didn't fully investigate. Let me read {specific source} and come back with evidence." → reads → "After reading {source}, the classification changes to 🟢 because {new evidence}" OR "After reading {source}, the classification stands at 🔴 because {new evidence confirms it}."

Either outcome is acceptable as long as it is driven by evidence, not by pressure.

### Rule 4: "I've Checked Everything Relevant and the Classification Stands" Is a Valid Response

If you've exhausted the relevant sources and the classification is correct, say so:

> "I've re-read {sources consulted}, and the classification remains {X} because {specific evidence}. If you have a source in mind that I haven't checked, please point me to it and I'll investigate."

This is an acceptable outcome. Standing by a well-evidenced classification under challenge is not stubbornness — it is the honest result of the investigation. Capitulation without new evidence is not helpfulness — it is dishonesty dressed as agreeableness.

### Rule 5: When the User Says the Classification Is Wrong (Without Pointing at a Source)

Treat this as a signal to widen the investigation, not as a conclusion to adopt. Ask yourself: "What source have I not read that could be the one the user knows about?" Read it. Return with evidence.

If the user explicitly says "the classification is wrong because {specific reason}," that IS new evidence — respond to it directly by reading sources that address their specific reason.

---

## The Pattern This Skill Prevents

Without this skill, the default pattern is:

1. Read test source, affected file, schema → form an answer
2. Under challenge → rephrase the same answer
3. Under continued challenge → flip to the opposite classification
4. Under continued pushback → generate increasingly defensive text

Every step of that pattern is a failure of the investigation. With this skill:

1. Investigation is structured (Phase 4 required reads)
2. The evidence bar is enforced mechanically (Phase 5 Quality Gate)
3. Challenges trigger more investigation, not more text (Pushback Protocol)
4. Classifications hold when evidence supports them; change when new evidence demands it; never flip based on tone

The test was called out to be investigated, not re-explained. Investigation requires evidence. This skill enforces that evidence must be gathered before classification and must be gathered again under challenge if the challenge reveals a gap.
