#!/usr/bin/env python3
"""
Post-processor: adds [PolymorphicType] attribute to generated C# properties
whose field names are listed in x-polymorphic-type-properties in the schema.

Usage:
    postprocess-polymorphic-type.py <schema.yaml> <output.cs>

The script reads the YAML schema's info.x-polymorphic-type-properties list,
converts each camelCase property name to PascalCase, then finds all matching
C# property declarations in the generated file and inserts the attribute.

If no x-polymorphic-type-properties are found, the script exits silently.
"""

import re
import sys
import os


def to_pascal_case(name: str) -> str:
    """Convert camelCase to PascalCase."""
    if not name:
        return name
    return name[0].upper() + name[1:]


def extract_polymorphic_properties(schema_path: str) -> list[str]:
    """
    Extract x-polymorphic-type-properties from the info: block of a YAML file.
    Uses line-based parsing to avoid a PyYAML dependency (consistent with other
    post-processors in the pipeline).
    """
    properties = []
    in_info = False
    in_polymorphic_list = False

    try:
        with open(schema_path, 'r', encoding='utf-8') as f:
            for line in f:
                stripped = line.rstrip()

                # Detect info: block (top-level, no indent)
                if stripped == 'info:':
                    in_info = True
                    continue

                # Detect end of info: block (next top-level key)
                if in_info and stripped and not stripped[0].isspace():
                    in_info = False
                    in_polymorphic_list = False
                    continue

                if not in_info:
                    continue

                # Detect x-polymorphic-type-properties: within info:
                if 'x-polymorphic-type-properties:' in stripped:
                    in_polymorphic_list = True
                    continue

                # Detect end of polymorphic list (next info-level key)
                if in_polymorphic_list:
                    # List items are indented further and start with "- "
                    item_match = re.match(r'^\s+- (.+)$', stripped)
                    if item_match:
                        prop_name = item_match.group(1).strip()
                        if prop_name:
                            properties.append(prop_name)
                    elif stripped.strip():
                        # Non-empty, non-list line = end of list
                        in_polymorphic_list = False
    except FileNotFoundError:
        return []

    return properties


def add_attribute_to_properties(cs_path: str, pascal_names: set[str]) -> int:
    """
    Add [BeyondImmersion.BannouService.Attributes.PolymorphicType] to all
    matching property declarations in the C# file.

    Returns the number of properties annotated.
    """
    ATTRIBUTE_LINE = '    [BeyondImmersion.BannouService.Attributes.PolymorphicType]'

    try:
        with open(cs_path, 'r', encoding='utf-8') as f:
            lines = f.readlines()
    except FileNotFoundError:
        print(f"  ⚠️  C# file not found: {cs_path}", file=sys.stderr)
        return 0

    # Build a regex that matches property declarations for any of our target names
    # Pattern: public <type> <PropertyName> { get;
    names_pattern = '|'.join(re.escape(n) for n in pascal_names)
    prop_regex = re.compile(
        rf'^(\s+)public\s+\S+\??\s+({names_pattern})\s*{{\s*get;'
    )

    modified_lines = []
    count = 0

    for line in lines:
        match = prop_regex.match(line)
        if match:
            indent = match.group(1)
            # Check the line hasn't already been annotated
            if modified_lines and 'PolymorphicType' in modified_lines[-1]:
                modified_lines.append(line)
                continue
            modified_lines.append(f'{indent}[BeyondImmersion.BannouService.Attributes.PolymorphicType]\n')
            count += 1
        modified_lines.append(line)

    if count > 0:
        with open(cs_path, 'w', encoding='utf-8') as f:
            f.writelines(modified_lines)

    return count


def main():
    if len(sys.argv) != 3:
        print(f"Usage: {sys.argv[0]} <schema.yaml> <output.cs>", file=sys.stderr)
        sys.exit(1)

    schema_path = sys.argv[1]
    cs_path = sys.argv[2]

    # Extract property names from schema
    properties = extract_polymorphic_properties(schema_path)
    if not properties:
        # No x-polymorphic-type-properties in this schema — nothing to do
        return

    # Convert to PascalCase for C# property matching
    pascal_names = {to_pascal_case(p) for p in properties}

    # Apply attributes
    count = add_attribute_to_properties(cs_path, pascal_names)
    if count > 0:
        names_str = ', '.join(sorted(pascal_names))
        print(f"  ✅ Added [PolymorphicType] to {count} properties ({names_str})")


if __name__ == '__main__':
    main()
