#!/usr/bin/env python3
"""
Post-process NSwag-generated model files to add change-tracking to Update request models.

Transforms auto-properties on Update/Modify/Set request classes into tracked properties
with backing fields, enabling the server to distinguish "field absent" from "field
explicitly set to null" on nullable optional fields.

See GitHub Issue #722 for the systemic design.

Usage: python3 scripts/postprocess-change-tracking.py <model-file>
       Returns exit code 0 on success (even if no target classes found).

What this script does:
  1. Identifies classes matching Update*Request, Modify*Request, Set*Request, BulkUpdate*Request
  2. For each target class, transforms every auto-property into a tracked property:
       Before:  public string? Name { get; set; } = default!;
       After:   private string? _name = default!;
                public string? Name { get => _name; set { _name = value; _TrackChange("name"); } }
  3. Injects the _changeFields HashSet field, ChangeFields property, and _TrackChange helper
  4. Extracts the camelCase field name from existing [JsonPropertyName("...")] attributes

What it does NOT touch:
  - Non-target classes (responses, events, enums, other request types)
  - Properties with [JsonExtensionData] (AdditionalProperties)
  - Properties with [JsonIgnore]
  - XML doc comments, using directives, namespace declarations
"""

import re
import sys
from pathlib import Path

# Classes whose names match these patterns get change-tracking
# Pattern: starts with Update/Modify/Set/BulkUpdate AND ends with Request
TARGET_CLASS_RE = re.compile(
    r'^(?:Update|Modify|BulkUpdate)\w*Request$'  # Update*, Modify*, BulkUpdate*
    r'|^Set[A-Z]\w*Request$'                      # Set* (but not Setup*)
)

# Auto-property: "    public TYPE NAME { get; set; } = INIT;" or without initializer
AUTO_PROP_RE = re.compile(
    r'^(\s+)public\s+'           # leading whitespace + "public "
    r'(.+?)\s+'                  # TYPE (captured, non-greedy)
    r'(\w+)\s*'                  # NAME (captured)
    r'\{\s*get;\s*set;\s*\}'     # { get; set; }
    r'(\s*=\s*(.+?)\s*;)?'      # optional " = INIT;" (group 4 = full, group 5 = value)
    r'\s*$'                      # trailing whitespace
)

# [JsonPropertyName("camelCase")] — extract the camelCase name
JSON_PROP_NAME_RE = re.compile(
    r'\[System\.Text\.Json\.Serialization\.JsonPropertyName\("(\w+)"\)\]'
)

# Attributes that signal "skip this property"
SKIP_ATTRIBUTES = ['[System.Text.Json.Serialization.JsonExtensionData]',
                    '[System.Text.Json.Serialization.JsonIgnore]']


def to_backing_field_name(prop_name: str) -> str:
    """Convert PascalCase property name to _camelCase backing field name."""
    return f"_{prop_name[0].lower()}{prop_name[1:]}"


def to_camel_case(prop_name: str) -> str:
    """Convert PascalCase to camelCase."""
    return f"{prop_name[0].lower()}{prop_name[1:]}"


def scan_property_preamble(lines: list[str], prop_line_idx: int) -> dict:
    """Scan backwards from a property line to find its XML docs and attributes.
    Returns dict with 'json_name', 'skip', 'preamble_start_idx',
    'doc_lines' (XML comment line indices), 'attr_lines' (attribute line indices).
    The preamble is everything from the first XML doc line through the last attribute line."""
    result = {
        'json_name': None,
        'skip': False,
        'preamble_start_idx': prop_line_idx,
        'doc_lines': [],
        'attr_lines': [],
    }

    # Phase 1: scan backwards collecting attributes (stop at docs/empty/other)
    # NSwag occasionally emits malformed comments (e.g., "/ TODO(system.text.json):")
    # between attributes — treat lines starting with "/" as inline comments to skip through
    i = prop_line_idx - 1
    while i >= max(prop_line_idx - 10, 0):
        line = lines[i].strip()

        if line.startswith('['):
            result['attr_lines'].insert(0, i)
            result['preamble_start_idx'] = i

            if any(skip in line for skip in SKIP_ATTRIBUTES):
                result['skip'] = True

            match = JSON_PROP_NAME_RE.search(line)
            if match:
                result['json_name'] = match.group(1)
            i -= 1
        elif line.startswith('/') and not line.startswith('///'):
            # Inline comment between attributes (e.g., NSwag "/ TODO" lines) —
            # treat as part of the attribute block, keep scanning
            result['attr_lines'].insert(0, i)
            result['preamble_start_idx'] = i
            i -= 1
        else:
            break

    # Phase 2: continue backwards collecting XML doc lines
    while i >= max(prop_line_idx - 15, 0):
        line = lines[i].strip()

        if line.startswith('///'):
            result['doc_lines'].insert(0, i)
            result['preamble_start_idx'] = i
            i -= 1
        elif not line:
            # Skip blank lines between doc and whatever is above
            break
        else:
            break

    return result


def generate_tracking_infrastructure(indent: str) -> list[str]:
    """Generate the _changeFields field, ChangeFields property, and _TrackChange helper."""
    return [
        f"{indent}// === Change-tracking infrastructure (auto-generated, see Issue #722) ===\n",
        f"{indent}private System.Collections.Generic.HashSet<string>? _changeFields;\n",
        f"\n",
        f"{indent}/// <summary>\n",
        f"{indent}/// Fields explicitly set on this request. Populated automatically by property\n",
        f"{indent}/// setters. When serialized, enables the server to distinguish \"field not\n",
        f"{indent}/// provided\" from \"field explicitly set to null\" for nullable properties.\n",
        f"{indent}/// </summary>\n",
        f"{indent}[System.Text.Json.Serialization.JsonPropertyName(\"changeFields\")]\n",
        f"{indent}public System.Collections.Generic.ICollection<string>? ChangeFields\n",
        f"{indent}{{\n",
        f"{indent}    get => _changeFields?.Count > 0 ? _changeFields : null;\n",
        f"{indent}    set\n",
        f"{indent}    {{\n",
        f"{indent}        if (value != null)\n",
        f"{indent}        {{\n",
        f"{indent}            _changeFields ??= new(System.StringComparer.OrdinalIgnoreCase);\n",
        f"{indent}            foreach (var f in value)\n",
        f"{indent}                _changeFields.Add(f);\n",
        f"{indent}        }}\n",
        f"{indent}    }}\n",
        f"{indent}}}\n",
        f"\n",
        f"{indent}private void _TrackChange(string fieldName)\n",
        f"{indent}    => (_changeFields ??= new(System.StringComparer.OrdinalIgnoreCase)).Add(fieldName);\n",
        f"\n",
    ]


def process_file(filepath: str) -> bool:
    """Process a generated model file. Returns True if any changes were made."""
    path = Path(filepath)
    if not path.exists():
        print(f"  ⚠️  File not found: {filepath}", file=sys.stderr)
        return False

    lines = path.read_text().splitlines(keepends=True)
    modified = False
    target_classes_found = 0

    # Phase 1: Find all target class ranges (start line, opening brace line, closing brace line)
    class_ranges = []
    i = 0
    while i < len(lines):
        line = lines[i].rstrip()

        # Look for "public partial class FooRequest"
        class_match = re.match(r'^public\s+partial\s+class\s+(\w+)', line)
        if class_match:
            class_name = class_match.group(1)
            if TARGET_CLASS_RE.match(class_name):
                # Find the opening brace
                brace_idx = i
                while brace_idx < len(lines) and '{' not in lines[brace_idx]:
                    brace_idx += 1

                if brace_idx < len(lines):
                    # Track brace depth to find closing brace
                    depth = 0
                    for j in range(brace_idx, len(lines)):
                        depth += lines[j].count('{') - lines[j].count('}')
                        if depth == 0:
                            class_ranges.append((class_name, i, brace_idx, j))
                            break

        i += 1

    if not class_ranges:
        return False

    # Phase 2: Process each target class (work backwards to preserve line numbers)
    for class_name, class_start, brace_start, brace_end in reversed(class_ranges):
        target_classes_found += 1
        props_transformed = 0
        indent = "    "  # NSwag uses 4-space indent for members

        # Collect property transformations within this class
        prop_lines_to_transform = []

        for line_idx in range(brace_start + 1, brace_end):
            match = AUTO_PROP_RE.match(lines[line_idx])
            if not match:
                continue

            preamble = scan_property_preamble(lines, line_idx)
            if preamble['skip']:
                continue

            prop_indent = match.group(1)
            prop_type = match.group(2)
            prop_name = match.group(3)
            initializer_value = match.group(5)  # "default!" or None

            # Get the camelCase name from [JsonPropertyName], fall back to computed
            json_name = preamble['json_name']
            if json_name is None:
                json_name = to_camel_case(prop_name)

            backing_field = to_backing_field_name(prop_name)

            prop_lines_to_transform.append({
                'prop_line_idx': line_idx,
                'preamble_start_idx': preamble['preamble_start_idx'],
                'doc_line_indices': preamble['doc_lines'],
                'attr_line_indices': preamble['attr_lines'],
                'indent': prop_indent,
                'type': prop_type,
                'name': prop_name,
                'backing_field': backing_field,
                'json_name': json_name,
                'initializer_value': initializer_value,
            })

        # Apply transformations in reverse order (preserve line indices)
        for prop in reversed(prop_lines_to_transform):
            prop_idx = prop['prop_line_idx']
            preamble_start = prop['preamble_start_idx']
            doc_indices = prop['doc_line_indices']
            attr_indices = prop['attr_line_indices']
            ind = prop['indent']
            bf = prop['backing_field']
            ptype = prop['type']
            pname = prop['name']
            jname = prop['json_name']
            init = prop['initializer_value']

            init_str = f" = {init};" if init else ""

            # Collect doc + attribute line texts (to move onto the public property)
            doc_texts = [lines[di] for di in doc_indices]
            attr_texts = [lines[ai] for ai in attr_indices]

            # Build replacement: backing field (bare) + XML docs + attributes + tracked property
            new_lines = [
                f"{ind}private {ptype} {bf}{init_str}\n",
            ]
            new_lines.extend(doc_texts)
            new_lines.extend(attr_texts)
            new_lines.append(
                f"{ind}public {ptype} {pname} {{ get => {bf}; set {{ {bf} = value; _TrackChange(\"{jname}\"); }} }}\n"
            )

            # Replace: entire preamble (docs + attrs) + property line → new block
            replace_start = preamble_start
            replace_end = prop_idx + 1  # exclusive
            lines[replace_start:replace_end] = new_lines
            props_transformed += 1

        # Inject tracking infrastructure after the opening brace
        # (adjusted for any line shifts from property transformations above)
        # Find the current opening brace position (may have shifted)
        # Since we worked backwards on properties, brace_start is still valid
        infra_lines = generate_tracking_infrastructure(indent)
        lines[brace_start + 1:brace_start + 1] = ["\n"] + infra_lines

        modified = True
        print(f"  ✅ {class_name}: {props_transformed} properties tracked")

    if modified:
        path.write_text(''.join(lines))

    return modified


def main():
    if len(sys.argv) < 2:
        print("Usage: python3 scripts/postprocess-change-tracking.py <model-file>", file=sys.stderr)
        sys.exit(1)

    filepath = sys.argv[1]
    changed = process_file(filepath)

    if changed:
        print(f"  📝 Change tracking applied to {filepath}")
    # Exit 0 even if no changes — not every service has Update request models


if __name__ == "__main__":
    main()
