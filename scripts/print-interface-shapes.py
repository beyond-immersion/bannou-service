#!/usr/bin/env python3
"""
Print compact interface shapes for bannou-service shared interfaces.

Extracts interface definitions from C# source files in bannou-service/,
showing method signatures, properties, and inheritance in a compact format.
Designed for AI agents and developers who need to understand the infrastructure
API surface without reading 400+ line files of XML documentation.

Two modes:
  - Catalog mode (no args): Lists ALL interfaces organized by category
  - Detail mode (INTERFACE=name): Full method signatures for a specific interface

Usage:
    python3 scripts/print-interface-shapes.py                    # Catalog mode
    python3 scripts/print-interface-shapes.py IStateStore        # Detail mode
    python3 scripts/print-interface-shapes.py ICacheableState    # Partial match
    python3 scripts/print-interface-shapes.py --list             # List all names

Format key:
    : BaseInterface     = inheritance
    <T>                 = generic type parameter
    where T : class     = generic constraint
    Task<T?>            = async method returning nullable T
"""

import re
import sys
from pathlib import Path
from dataclasses import dataclass, field


# ============================================================================
# Category definitions — derived from directory + name patterns
# ============================================================================

CATEGORIES = [
    ("State Store", [
        "Services/IStateStore.cs",
        "Services/ICacheableStateStore.cs",
        "Services/IJsonQueryableStateStore.cs",
        "Services/ISearchableStateStore.cs",
        "Services/IRedisOperations.cs",
        "Services/IDistributedLockProvider.cs",
    ]),
    ("Messaging & Events", [
        "Services/IMessageBus.cs",
        "Events/IEventConsumer.cs",
        "ClientEvents/IClientEventPublisher.cs",
        "Services/IMessageTap.cs",
        "Events/IEventTemplateRegistry.cs",
    ]),
    ("Service Mesh & Invocation", [
        "Services/IMeshInvocationClient.cs",
        "ServiceClients/IServiceNavigator.cs",
        "ServiceClients/IServiceClient.cs",
        "Services/IServiceAppMappingResolver.cs",
        "Services/IMeshInstanceIdentifier.cs",
    ]),
    ("DI Providers (Pull — Always Distributed-Safe)", [
        "Providers/IVariableProviderFactory.cs",
        "Providers/IPrerequisiteProviderFactory.cs",
        "Providers/IBehaviorDocumentProvider.cs",
        "Providers/IBehaviorDocumentLoader.cs",
        "Providers/ISeededResourceProvider.cs",
        "Providers/ITransitCostModifierProvider.cs",
        "Providers/ILocalizationKeyValidator.cs",
        "Providers/IServiceMappingReceiver.cs",
    ]),
    ("DI Listeners (Push — Local-Only Fan-Out)", [
        "Providers/ISeedEvolutionListener.cs",
        "Providers/ICollectionUnlockListener.cs",
        "Providers/ISessionActivityListener.cs",
        "Providers/IItemInstanceDestructionListener.cs",
    ]),
    ("Entity Session & Client Push", [
        "Providers/IEntitySessionRegistry.cs",
    ]),
    ("Telemetry", [
        "Services/ITelemetryProvider.cs",
    ]),
    ("Service Markers & Lifecycle", [
        "Services/IBannouService.cs",
        "Services/ICleanDeprecatedEntity.cs",
        "Services/IDeprecateAndMergeEntity.cs",
        "Services/IAccountDeletionCleanupRequired.cs",
        "Services/IFlushable.cs",
        "Services/IControlPlaneServiceProvider.cs",
        "Services/IPermissionRegistry.cs",
        "Services/IEmailService.cs",
        "Plugins/IBannouPlugin.cs",
        "Controllers/IBannouController.cs",
        "Configuration/IServiceConfiguration.cs",
        "Attributes/IServiceAttribute.cs",
    ]),
    ("Behavior System", [
        "Behavior/IBehaviorStack.cs",
        "Behavior/ICognitionBuilder.cs",
        "Behavior/ICognitionTemplate.cs",
        "Behavior/IControlGate.cs",
        "Behavior/ICutsceneCoordinator.cs",
        "Behavior/IDialogueResolver.cs",
        "Behavior/IEntityResolver.cs",
        "Behavior/IEntityStateRegistry.cs",
        "Behavior/IExternalDialogueLoader.cs",
        "Behavior/IInputWindowManager.cs",
        "Behavior/IIntentEmitter.cs",
        "Behavior/ILocalizationProvider.cs",
        "Behavior/ISyncPointManager.cs",
        "Behavior/ITemporalManager.cs",
        "Abml/Cognition/IMemoryStore.cs",
        "Abml/Execution/IActionHandler.cs",
    ]),
    ("History & Storage Helpers", [
        "History/IBackstoryStorageHelper.cs",
        "History/IDualIndexHelper.cs",
        "Archives/IResourceArchive.cs",
        "Storage/IAssetStorageProvider.cs",
    ]),
    ("Exception Handling", [
        "Providers/IUnhandledExceptionHandler.cs",
    ]),
]


@dataclass
class InterfaceMember:
    """A method or property on an interface."""
    kind: str  # "method", "property", "event"
    return_type: str
    name: str
    params: str = ""  # for methods
    generic_params: str = ""  # <T> etc
    constraints: str = ""  # where T : class
    is_default: bool = False  # default interface method


@dataclass
class InterfaceInfo:
    """Parsed interface definition."""
    name: str
    generic_params: str = ""
    constraints: str = ""
    bases: list[str] = field(default_factory=list)
    summary: str = ""
    members: list[InterfaceMember] = field(default_factory=list)
    source_file: str = ""


def extract_summary(lines: list[str], end_line: int) -> str:
    """Extract the XML <summary> content above a declaration."""
    summary_lines = []
    in_summary = False
    # Walk backwards from the declaration to find the summary
    for i in range(end_line - 1, max(end_line - 80, -1), -1):
        if i < 0:
            break
        line = lines[i].strip()
        if line == "/// </summary>":
            in_summary = True
            continue
        if in_summary:
            if line == "/// <summary>":
                break
            if line.startswith("///"):
                text = line[3:].strip()
                if text:
                    summary_lines.insert(0, text)

    return " ".join(summary_lines)


def _find_method_name(line: str) -> tuple[str, str, str] | None:
    """Find method return type, name, and generic params from a line.

    Uses bracket-depth approach to handle tuple return types like
    Task<(TValue? Value, string? ETag)>.

    Returns (return_type, method_name, generic_params) or None.
    """
    # Find the opening '(' of the parameter list at bracket-depth 0
    depth = 0  # tracks <> and ()
    paren_pos = -1
    for ci, ch in enumerate(line):
        if ch in ("<", "("):
            if ch == "(" and depth == 0:
                paren_pos = ci
                break
            depth += 1
        elif ch in (">", ")"):
            depth -= 1

    if paren_pos <= 0:
        return None

    # Everything before '(' is "return_type name<T>"
    before_paren = line[:paren_pos].rstrip()

    # Extract optional generic params at the end: Name<T>
    generic_params = ""
    if before_paren.endswith(">"):
        gd = 0
        for ci in range(len(before_paren) - 1, -1, -1):
            if before_paren[ci] == ">":
                gd += 1
            elif before_paren[ci] == "<":
                gd -= 1
                if gd == 0:
                    generic_params = before_paren[ci:]
                    before_paren = before_paren[:ci].rstrip()
                    break

    # Now find the method name (last word)
    name_match = re.search(r"(\w+)\s*$", before_paren)
    if not name_match:
        return None

    method_name = name_match.group(1)
    ret_type = before_paren[:name_match.start()].strip()

    if not ret_type:
        return None

    return (ret_type, method_name, generic_params)


def _extract_params(full_sig: str) -> str:
    """Extract parameter list from a full method signature, handling nested brackets."""
    # Find the opening '(' of the parameter list at bracket-depth 0
    depth = 0
    start = -1
    for ci, ch in enumerate(full_sig):
        if ch in ("<",):
            depth += 1
        elif ch in (">",):
            depth -= 1
        elif ch == "(" and depth == 0:
            start = ci
            break

    if start < 0:
        return ""

    # Find matching ')' at the same depth
    depth = 0
    for ci in range(start, len(full_sig)):
        if full_sig[ci] == "(":
            depth += 1
        elif full_sig[ci] == ")":
            depth -= 1
            if depth == 0:
                params = full_sig[start + 1:ci]
                return re.sub(r"\s+", " ", params).strip()

    return ""


def parse_interface_file(filepath: Path) -> list[InterfaceInfo]:
    """Parse a C# file and extract all interface definitions."""
    try:
        content = filepath.read_text(encoding="utf-8")
    except Exception:
        return []

    lines = content.split("\n")
    interfaces = []

    # Regex for interface declaration (possibly multi-line)
    # Matches: public interface IName<T> : IBase where T : class
    iface_pattern = re.compile(
        r"^public\s+interface\s+(\w+)"
        r"(<[^>]+>)?"          # optional generic params
        r"\s*"
    )

    i = 0
    while i < len(lines):
        line = lines[i].strip()
        m = iface_pattern.match(line)
        if not m:
            i += 1
            continue

        iface_name = m.group(1)
        generic_params = m.group(2) or ""

        # Gather full declaration (may span multiple lines until '{')
        decl_start = i
        decl_lines = [lines[i]]
        j = i + 1
        while j < len(lines) and "{" not in lines[j - 1]:
            decl_lines.append(lines[j])
            j += 1

        full_decl = " ".join(dl.strip() for dl in decl_lines)

        # Extract constraints first (remove from consideration for bases)
        constraints = ""
        where_match = re.search(r"(where\s+.+?)(?:\s*\{)", full_decl)
        if where_match:
            constraints = where_match.group(1).strip()

        # Extract bases: look for ':' between interface name/generics and 'where'/{
        # Strip the where clause and opening brace from the declaration first
        decl_for_bases = full_decl
        if constraints:
            decl_for_bases = decl_for_bases[:decl_for_bases.find("where")].strip()
        decl_for_bases = decl_for_bases.rstrip(" {")

        bases = []
        # Find ':' after the interface name (not inside the where clause)
        name_end = decl_for_bases.find(iface_name) + len(iface_name)
        if generic_params:
            # Skip past the generic params
            gp_start = decl_for_bases.find("<", name_end)
            if gp_start >= 0:
                depth = 0
                for ci, ch in enumerate(decl_for_bases[gp_start:]):
                    if ch == "<":
                        depth += 1
                    elif ch == ">":
                        depth -= 1
                        if depth == 0:
                            name_end = gp_start + ci + 1
                            break

        remainder = decl_for_bases[name_end:].strip()
        if remainder.startswith(":"):
            bases_str = remainder[1:].strip()
            # Split on ',' but not inside angle brackets
            depth = 0
            current: list[str] = []
            for ch in bases_str:
                if ch == "<":
                    depth += 1
                elif ch == ">":
                    depth -= 1
                elif ch == "," and depth == 0:
                    bases.append("".join(current).strip())
                    current = []
                    continue
                current.append(ch)
            if current:
                bases.append("".join(current).strip())

        summary = extract_summary(lines, decl_start)

        iface = InterfaceInfo(
            name=iface_name,
            generic_params=generic_params,
            constraints=constraints,
            bases=bases,
            summary=summary,
            source_file=str(filepath),
        )

        # Now parse members until closing brace
        brace_depth = 0
        k = j - 1  # start at the line with '{'
        while k < len(lines):
            ln = lines[k]
            stripped = ln.strip()

            # Only count braces on non-comment lines to avoid XML doc examples
            if not stripped.startswith("//"):
                brace_depth += ln.count("{") - ln.count("}")
            if brace_depth <= 0:
                break

            # Skip comments, blank lines, region markers, section comments,
            # and standalone type declarations (records, classes, enums) within the file
            if (not stripped or stripped.startswith("//") or
                stripped.startswith("///") or stripped.startswith("#") or
                stripped.startswith("[") or stripped == "{" or stripped == "}" or
                stripped.startswith("private ") or stripped.startswith("internal ") or
                stripped.startswith("public record ") or
                stripped.startswith("public class ") or
                stripped.startswith("public enum ") or
                stripped.startswith("public static ")):
                k += 1
                continue

            # Property: Type Name { get; set; } or Type Name { get; }
            prop_match = re.match(
                r"^([\w<>\[\]?,\s\(\)]+?)\s+(\w+)\s*\{\s*get;\s*(?:set;\s*)?\}",
                stripped
            )
            if prop_match:
                ret_type = prop_match.group(1).strip()
                prop_name = prop_match.group(2)
                member_summary = extract_summary(lines, k)
                iface.members.append(InterfaceMember(
                    kind="property",
                    return_type=ret_type,
                    name=prop_name,
                ))
                k += 1
                continue

            # Method: ReturnType Name<T>(params...) where T : constraint
            # May span multiple lines. Use bracket-depth approach for tuples.
            if stripped.startswith("=>") or stripped.startswith("static "):
                k += 1
                continue

            # Find method name: last identifier before '(' at depth 0
            method_info = _find_method_name(stripped)
            if method_info:
                ret_type, method_name, method_generic = method_info

                # Gather full method signature (until ';' or '=>')
                sig_lines = [lines[k]]
                mk = k
                # Only gather continuation lines if first line doesn't end the sig
                first_line = lines[k].strip()
                if ";" not in first_line and "=>" not in first_line:
                    mk = k + 1
                    while mk < len(lines):
                        sl = lines[mk].strip()
                        sig_lines.append(lines[mk])
                        if ";" in sl or "=>" in sl:
                            break
                        mk += 1

                full_sig = " ".join(sl.strip() for sl in sig_lines)

                # Extract parameters using bracket-depth matching
                params_str = _extract_params(full_sig)

                # Check for default implementation (=>)
                is_default = "=>" in full_sig

                # Extract method constraints
                method_constraints = ""
                mwhere = re.search(r"(where\s+\w+\s*:\s*[^;]+?)(?:;|$)", full_sig)
                if mwhere:
                    method_constraints = mwhere.group(1).strip()

                iface.members.append(InterfaceMember(
                    kind="method",
                    return_type=ret_type,
                    name=method_name,
                    generic_params=method_generic,
                    params=params_str,
                    constraints=method_constraints,
                    is_default=is_default,
                ))
                k = mk + 1
                continue

            k += 1

        interfaces.append(iface)
        i = k + 1

    return interfaces


def compact_params(params: str) -> str:
    """Compress parameter list: remove defaults, shorten names."""
    if not params:
        return ""

    parts = []
    depth = 0
    current = []
    for ch in params:
        if ch in ("<", "("):
            depth += 1
        elif ch in (">", ")"):
            depth -= 1
        elif ch == "," and depth == 0:
            parts.append("".join(current).strip())
            current = []
            continue
        current.append(ch)
    if current:
        parts.append("".join(current).strip())

    compact = []
    for part in parts:
        part = part.strip()
        # Remove default values
        if "=" in part:
            part = re.sub(r"\s*=\s*[^,]+$", "", part)
        compact.append(part)

    return ", ".join(compact)


def format_catalog_entry(iface: InterfaceInfo) -> str:
    """Format a single interface for catalog mode (one-liner)."""
    name = f"{iface.name}{iface.generic_params}"
    summary = iface.summary or "(no summary)"
    # Truncate long summaries
    if len(summary) > 80:
        summary = summary[:77] + "..."
    return f"  {name:<40s} {summary}"


def format_detail(iface: InterfaceInfo) -> list[str]:
    """Format a single interface for detail mode (full signatures)."""
    out = []

    # Header
    header = f"{iface.name}{iface.generic_params}"
    if iface.bases:
        header += " : " + ", ".join(iface.bases)
    out.append(header)
    if iface.constraints:
        out.append(f"  {iface.constraints}")
    if iface.summary:
        out.append(f"  {iface.summary}")
    out.append("")

    # Group members
    properties = [m for m in iface.members if m.kind == "property"]
    methods = [m for m in iface.members if m.kind == "method" and not m.is_default]
    defaults = [m for m in iface.members if m.kind == "method" and m.is_default]

    if properties:
        out.append("  Properties:")
        for p in properties:
            out.append(f"    {p.return_type} {p.name} {{ get; }}")
        out.append("")

    if methods:
        out.append("  Methods:")
        for m in methods:
            params = compact_params(m.params)
            sig = f"    {m.return_type} {m.name}{m.generic_params}({params})"
            if m.constraints:
                sig += f" {m.constraints}"
            out.append(sig)
        out.append("")

    if defaults:
        out.append("  Default Methods:")
        for m in defaults:
            params = compact_params(m.params)
            sig = f"    {m.return_type} {m.name}{m.generic_params}({params})"
            out.append(sig)
        out.append("")

    return out


def find_bannou_service_dir() -> Path:
    """Find the bannou-service directory."""
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    bs_dir = repo_root / "bannou-service"
    if not bs_dir.exists():
        print("ERROR: bannou-service/ directory not found", file=sys.stderr)
        sys.exit(1)
    return bs_dir


def load_all_interfaces(bs_dir: Path) -> dict[str, list[InterfaceInfo]]:
    """Load all interfaces organized by relative file path."""
    result: dict[str, list[InterfaceInfo]] = {}

    for cs_file in sorted(bs_dir.rglob("I*.cs")):
        # Skip generated files
        if "Generated" in str(cs_file) or "bin" in str(cs_file) or "obj" in str(cs_file):
            continue

        rel_path = str(cs_file.relative_to(bs_dir))
        interfaces = parse_interface_file(cs_file)
        if interfaces:
            result[rel_path] = interfaces

    return result


def print_catalog(all_interfaces: dict[str, list[InterfaceInfo]]) -> None:
    """Print catalog mode: all interfaces by category."""
    total_interfaces = 0
    total_methods = 0

    # Build lookup: relative path -> interfaces
    for cat_name, cat_files in CATEGORIES:
        entries = []
        for rel_path in cat_files:
            if rel_path in all_interfaces:
                for iface in all_interfaces[rel_path]:
                    entries.append(iface)

        if not entries:
            continue

        print(f"[{cat_name}]")
        for iface in entries:
            print(format_catalog_entry(iface))
            total_interfaces += 1
            total_methods += len([m for m in iface.members if m.kind == "method"])
        print()

    # Check for uncategorized interfaces
    categorized_files = set()
    for _, cat_files in CATEGORIES:
        categorized_files.update(cat_files)

    uncategorized = []
    for rel_path, ifaces in all_interfaces.items():
        if rel_path not in categorized_files:
            for iface in ifaces:
                uncategorized.append((rel_path, iface))

    if uncategorized:
        print("[Uncategorized]")
        for rel_path, iface in uncategorized:
            line = format_catalog_entry(iface)
            print(f"{line}  ({rel_path})")
            total_interfaces += 1
            total_methods += len([m for m in iface.members if m.kind == "method"])
        print()

    print(f"# {total_interfaces} interfaces, {total_methods} methods", file=sys.stderr)


def print_detail(all_interfaces: dict[str, list[InterfaceInfo]], query: str) -> None:
    """Print detail mode: full signatures for matching interface(s)."""
    matches = []
    for rel_path, ifaces in all_interfaces.items():
        for iface in ifaces:
            # Exact match (case-insensitive)
            if iface.name.lower() == query.lower():
                matches.append((rel_path, iface))
            # Partial match
            elif query.lower() in iface.name.lower():
                matches.append((rel_path, iface))

    if not matches:
        print(f"ERROR: No interface matching '{query}' found", file=sys.stderr)
        print("Available interfaces:", file=sys.stderr)
        for rel_path, ifaces in sorted(all_interfaces.items()):
            for iface in ifaces:
                print(f"  {iface.name}{iface.generic_params}  ({rel_path})", file=sys.stderr)
        sys.exit(1)

    # Exact matches first, then partial
    exact = [(p, i) for p, i in matches if i.name.lower() == query.lower()]
    if exact:
        matches = exact

    for rel_path, iface in matches:
        print(f"# Source: bannou-service/{rel_path}")
        for line in format_detail(iface):
            print(line)


def print_list(all_interfaces: dict[str, list[InterfaceInfo]]) -> None:
    """Print just the interface names."""
    for rel_path, ifaces in sorted(all_interfaces.items()):
        for iface in ifaces:
            print(f"{iface.name}{iface.generic_params}  ({rel_path})")


def main():
    if len(sys.argv) > 1 and sys.argv[1] in ("-h", "--help"):
        print("Usage:")
        print("  python3 scripts/print-interface-shapes.py              # Catalog (all interfaces)")
        print("  python3 scripts/print-interface-shapes.py IStateStore   # Detail (specific interface)")
        print("  python3 scripts/print-interface-shapes.py --list        # List all interface names")
        print()
        print("Catalog mode shows all interfaces by category with one-line summaries.")
        print("Detail mode shows full method signatures for matching interface(s).")
        print("Partial name matching is supported (e.g., 'Cacheable' matches ICacheableStateStore).")
        sys.exit(0)

    bs_dir = find_bannou_service_dir()
    all_interfaces = load_all_interfaces(bs_dir)

    if not all_interfaces:
        print("ERROR: No interfaces found in bannou-service/", file=sys.stderr)
        sys.exit(1)

    if len(sys.argv) > 1:
        query = sys.argv[1]
        if query == "--list":
            print_list(all_interfaces)
        else:
            print_detail(all_interfaces, query)
    else:
        print_catalog(all_interfaces)


if __name__ == "__main__":
    main()
