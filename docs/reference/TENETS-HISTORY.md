# Tenet & Violation Change History

> ⛔ **APPEND-ONLY CHANGELOG** — emitted by the tenet/violation write tools in
> `.claude/mcp/helpers/tenets.mjs`. Do NOT edit existing rows by hand; new rows
> are appended automatically whenever a write tool succeeds.

Each row records one mutation to `TENETS.md` or a tenet category file. `Tool`
is the helper / MCP tool that performed the write. `Target` is the affected
tenet id (or comma-separated list of cited ids for violation rows). `Summary`
is a one-line description of what changed.

| Timestamp | Tool | Target | Summary |
|-----------|------|--------|---------|
| 2026-04-20T03:11:49.478Z | `add_violation` | T4 | Added row "__MCP_TEST__ Direct Redis call in helper method" citing T4 |
| 2026-04-20T03:20:53.696Z | `add_violation` | T4 | Added row "__MCP_AUDIT__ Second test violation for T4 (tier 1 test)" citing T4 |
| 2026-04-20T03:24:24.465Z | `edit_violation` | T4 | Edited fix for "__MCP_AUDIT__ Second test violation for T4 (tier 1 test)": "__MCP_AUDIT__ Test fix for T4 row" → "__MCP_AUDIT__ UPDATED fix text after ed…" |
| 2026-04-20T03:24:29.330Z | `remove_violation` | T4 | Removed row "__MCP_AUDIT__ Second test violation for T4 (tier 1 test)" citing T4 |
| 2026-04-20T03:24:29.482Z | `remove_violation` | T4 | Removed row "__MCP_TEST__ Direct Redis call in helper method" citing T4 |
| 2026-04-20T03:27:51.583Z | `add_tenet` | T99 | Added T99 "Audit Test Tenet" (TEST) to quality (docs/reference/tenets/QUALITY.md), strategy=after-predecessor T22; derived: t0=3, nav=4, summary=1 |
| 2026-04-20T03:35:43.949Z | `edit_tenet` | T99 | Edited body of T99 (Audit Test Tenet) in docs/reference/tenets/QUALITY.md: 653 → 328 chars |
| 2026-04-20T03:35:51.825Z | `add_violation` | T99 | Added row "__MCP_AUDIT__ Renumber test violation citing T99" citing T99 |
| 2026-04-20T03:35:54.222Z | `renumber_tenet` | T99→T98 | Renumbered T99 → T98 "Audit Test Tenet" in docs/reference/tenets/QUALITY.md (heading✓, summary✓, 1 QR citation(s), 3 T0 list(s), 4 nav row(s)) |
| 2026-04-20T03:36:13.039Z | `remove_tenet` | T98 | Removed T98 "Audit Test Tenet" from docs/reference/tenets/QUALITY.md (14 lines); derived: t0=3, nav=4, summary=1 |
| 2026-04-20T03:36:17.821Z | `remove_violation` | T98 | Removed row "__MCP_AUDIT__ Renumber test violation citing T99" citing T98 |
| 2026-04-20T04:03:05.733Z | `add_tenet` | T97 | Added T97 "F6 Audit Test Tenet" (TEST) to quality (docs/reference/tenets/QUALITY.md), strategy=after-predecessor T22; derived: t0=3, nav=4, summary=1 |
| 2026-04-20T04:03:07.666Z | `add_violation` | T97 | Added row "__F6_TEST__ Sole-citation violation (would be deleted)" citing T97 |
| 2026-04-20T04:03:08.514Z | `add_violation` | T97 | Added row "__F6_TEST__ Second sole-citation (also would be deleted)" citing T97 |
| 2026-04-20T04:03:15.827Z | `add_violation` | T4 | Added row "__F6_TEST__ Shared-citation violation (would be rewritten)" citing T4 |
| 2026-04-20T04:04:02.539Z | `remove_tenet` | T97 | Removed T97 "F6 Audit Test Tenet" from docs/reference/tenets/QUALITY.md (8 lines); derived: t0=3, nav=4, summary=1; citations: deleted=2, rewritten=1 |
| 2026-04-20T04:06:16.314Z | `remove_violation` | T4, T97 | Removed row "__F6_TEST__ Shared-citation violation (would be rewritten)" citing T4, T97 |
| 2026-04-20T04:06:16.426Z | `remove_violation` | T97 | Removed row "__F6_TEST__ Sole-citation violation (would be deleted)" citing T97 |
| 2026-04-20T04:06:23.854Z | `remove_violation` | T4 | Removed row "__F6_TEST__ Shared-citation violation (would be rewritten)" citing T4 |
| 2026-04-20T04:06:32.331Z | `add_violation` | T6 | Added row "BackgroundService passing `IStateStoreFactory` to sub-methods" citing T6 |
| 2026-04-20T04:12:49.090Z | `add_tenet` | T97 | Added T97 "F6 Audit Test Tenet" (TEST) to quality (docs/reference/tenets/QUALITY.md), strategy=after-predecessor T22; derived: t0=3, nav=4, summary=1 |
| 2026-04-20T04:12:50.639Z | `add_violation` | T97 | Added row "__F6_RETEST__ First sole-citation (should be deleted)" citing T97 |
| 2026-04-20T04:12:51.850Z | `add_violation` | T97 | Added row "__F6_RETEST__ Second sole-citation (should be deleted)" citing T97 |
| 2026-04-20T04:12:52.792Z | `add_violation` | T4 | Added row "__F6_RETEST__ Shared-citation (should be rewritten)" citing T4 |
| 2026-04-20T05:35:50.418Z | `remove_tenet` | T97 | Removed T97 "F6 Audit Test Tenet" from docs/reference/tenets/QUALITY.md (8 lines); derived: t0=3, nav=4, summary=1; citations: deleted=2, rewritten=1 |
| 2026-04-20T05:36:02.556Z | `remove_violation` | T4 | Removed row "__F6_RETEST__ Shared-citation (should be rewritten)" citing T4 |
| 2026-04-20T14:13:50.315Z | `add_tenet` | T33 | Added T33 "Design Specification Fidelity" (ABSOLUTE) to quality (docs/reference/tenets/QUALITY.md), strategy=after-predecessor T22; derived: t0=3, nav=4, summary=1 |
| 2026-04-20T14:14:59.029Z | `add_tenet` | T34 | Added T34 "AOT Compatibility" (MANDATORY) to implementation-behavior (docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md), strategy=after-predecessor T31; derived: t0=3, nav=4, summary=1 |
| 2026-04-20T14:15:16.850Z | `add_violation` | T33 | Added row "Implementation diverges from planning doc's prescribed technical approach" citing T33 |
| 2026-04-20T14:15:17.907Z | `add_violation` | T33 | Added row "Shipping a functional equivalent with different machine-code shape (e.g., refle…" citing T33 |
| 2026-04-20T14:15:18.580Z | `add_violation` | T33 | Added row "Architectural change bundled into multi-concern commit, hiding divergence from …" citing T33 |
| 2026-04-20T14:15:18.683Z | `add_violation` | T34 | Added row "Assembly.LoadFrom or Assembly.LoadFile in bannou-service or plugins code paths" citing T34 |
| 2026-04-20T14:15:20.169Z | `add_violation` | T34 | Added row "MakeGenericMethod or MakeGenericType with runtime-discovered types in shipping …" citing T34 |
| 2026-04-20T14:15:21.279Z | `add_violation` | T34 | Added row "method.Invoke on runtime-resolved MethodInfo in hot paths" citing T34 |
| 2026-04-20T14:15:22.691Z | `add_violation` | T34 | Added row "Activator.CreateInstance with a runtime Type variable (non-typeof) in shipping …" citing T34 |
| 2026-04-20T14:15:23.678Z | `add_violation` | T34 | Added row "ValueTuple GetField("Item1") reflection unpacking" citing T34 |
| 2026-04-20T14:15:24.848Z | `add_violation` | T34 | Added row "AppDomain.CurrentDomain.GetAssemblies().GetTypes() in runtime paths" citing T34 |
| 2026-04-20T14:15:25.475Z | `add_violation` | T34 | Added row "Expression.Compile() on hot paths in shipping code" citing T34 |
| 2026-04-20T14:15:27.221Z | `add_violation` | T34 | Added row "Reflection.Emit, DynamicMethod, AssemblyBuilder, ModuleBuilder, TypeBuilder, or…" citing T34 |
| 2026-04-20T14:15:28.778Z | `add_violation` | T34 | Added row "Roslyn scripting (CSharpScript, Microsoft.CodeAnalysis.Scripting) in runtime pa…" citing T34 |
