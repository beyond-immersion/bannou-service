#!/usr/bin/env python3
"""
Print compact model shapes for a Bannou service plugin.

Extracts model type information from OpenAPI schemas in a minimal format,
~6x smaller than raw schemas or generated C# models. Designed for AI agents
and developers who need to understand model shapes without loading full files.

Parses all schema files for the given service:
  - schemas/{service}-api.yaml           (request/response models)
  - schemas/{service}-events.yaml        (event models)
  - schemas/{service}-client-events.yaml (WebSocket push event models)
  - schemas/Generated/{service}-lifecycle-events.yaml (lifecycle events)

Usage:
    python3 scripts/print-model-shapes.py <service>
    python3 scripts/print-model-shapes.py character
    python3 scripts/print-model-shapes.py game-session

Format key:
    *  = required property
    ?  = nullable type
    = val = default value
    Guid = UUID, DateTimeOffset = ISO 8601 datetime
    Type[] = array, Dict<K, V> = map, extends Base = inheritance
"""

import sys
from pathlib import Path

try:
    from ruamel.yaml import YAML
    yaml = YAML()
    yaml.preserve_quotes = True
except ImportError:
    print("ERROR: ruamel.yaml is required. Install with: pip install ruamel.yaml",
          file=sys.stderr)
    sys.exit(1)


# OpenAPI type+format → compact C# type name
TYPE_MAP = {
    ("string", ""): "string",
    ("string", "uuid"): "Guid",
    ("string", "date-time"): "DateTimeOffset",
    ("string", "binary"): "byte[]",
    ("string", "byte"): "byte[]",
    ("string", "uri"): "string",
    ("string", "email"): "string",
    ("integer", ""): "int",
    ("integer", "int32"): "int",
    ("integer", "int64"): "long",
    ("number", ""): "double",
    ("number", "float"): "float",
    ("number", "double"): "double",
    ("boolean", ""): "bool",
}

MAX_INLINE_ENUM_VALUES = 8
MAX_INLINE_PROPS = 3


def extract_ref_name(ref: str) -> str:
    """Extract type name from $ref string."""
    return ref.split("/")[-1]


def resolve_type(prop: dict) -> str:
    """Convert an OpenAPI property definition to a compact type string."""
    if not isinstance(prop, dict):
        return "object"

    if "$ref" in prop:
        return extract_ref_name(prop["$ref"])

    if "allOf" in prop:
        refs = [x for x in prop["allOf"] if isinstance(x, dict) and "$ref" in x]
        if refs:
            return extract_ref_name(refs[0]["$ref"])
        return "object"

    for key in ("oneOf", "anyOf"):
        if key in prop:
            items = prop[key]
            if items and isinstance(items[0], dict):
                return resolve_type(items[0])

    type_ = str(prop.get("type", "object"))
    format_ = str(prop.get("format", ""))

    if "enum" in prop and type_ == "string":
        values = [str(v) for v in prop["enum"]]
        if len(values) <= MAX_INLINE_ENUM_VALUES:
            return f"enum({', '.join(values)})"
        return f"enum({', '.join(values[:MAX_INLINE_ENUM_VALUES - 1])}, +{len(values) - MAX_INLINE_ENUM_VALUES + 1})"

    if type_ == "array":
        items_def = prop.get("items", {})
        item_type = resolve_type(items_def)
        return f"{item_type}[]"

    if type_ == "object" and "additionalProperties" in prop:
        ap = prop["additionalProperties"]
        if isinstance(ap, dict):
            val_type = resolve_type(ap)
            return f"Dict<string, {val_type}>"
        return "Dict<string, object>"

    return TYPE_MAP.get((type_, format_), type_)


def format_model(name: str, schema: dict) -> list[str]:
    """Format a model schema as compact shape lines."""
    if not isinstance(schema, dict):
        return [f"{name} {{}}"]

    # Enum
    if schema.get("type") == "string" and "enum" in schema:
        values = [str(v) for v in schema["enum"]]
        return [f"enum {name} {{ {', '.join(values)} }}"]

    # Detect inheritance via allOf
    base_type = None
    own_properties = schema.get("properties", {})
    own_required = set(schema.get("required", []))

    if "allOf" in schema:
        for item in schema["allOf"]:
            if isinstance(item, dict) and "$ref" in item:
                base_type = extract_ref_name(item["$ref"])
            elif isinstance(item, dict) and "properties" in item:
                own_properties = {**own_properties, **item.get("properties", {})}
                own_required = own_required | set(item.get("required", []))

    extends = f" extends {base_type}" if base_type else ""

    if not own_properties:
        return [f"{name}{extends} {{}}"]

    # Build property strings
    prop_strs = []
    for prop_name, prop_def in own_properties.items():
        if not isinstance(prop_def, dict):
            continue

        type_str = resolve_type(prop_def)
        nullable = prop_def.get("nullable", False)
        is_required = prop_name in own_required
        default = prop_def.get("default")

        req = "*" if is_required else ""
        null = "?" if nullable else ""

        entry = f"{prop_name}{req}: {type_str}{null}"
        if default is not None:
            if isinstance(default, bool):
                entry += f" = {'true' if default else 'false'}"
            else:
                entry += f" = {default}"

        prop_strs.append(entry)

    if len(prop_strs) <= MAX_INLINE_PROPS:
        inline = ", ".join(prop_strs)
        return [f"{name}{extends} {{ {inline} }}"]
    else:
        lines = [f"{name}{extends} {{"]
        for p in prop_strs:
            lines.append(f"  {p}")
        lines.append("}")
        return lines


def extract_models_from_file(yaml_file: Path) -> list[str]:
    """Parse a schema file and return formatted model lines."""
    lines = []
    try:
        with open(yaml_file) as f:
            content = yaml.load(f)

        if content is None:
            return lines

        components = content.get("components", {})
        if not isinstance(components, dict):
            return lines
        schemas = components.get("schemas", {})
        if not isinstance(schemas, dict):
            return lines

        for model_name, model_schema in schemas.items():
            if not isinstance(model_schema, dict):
                continue
            lines.extend(format_model(model_name, model_schema))

    except Exception as e:
        print(f"Warning: Failed to parse {yaml_file}: {e}", file=sys.stderr)

    return lines


def schema_category_label(filename: str) -> str:
    """Return a display label for the schema category."""
    if "-lifecycle-events" in filename:
        return "Lifecycle Events"
    if "-client-events" in filename:
        return "Client Events"
    if "-events" in filename:
        return "Events"
    return "API"


def main():
    if len(sys.argv) < 2 or sys.argv[1] in ("-h", "--help"):
        print("Usage: python3 scripts/print-model-shapes.py <service>")
        print()
        print("Examples:")
        print("  python3 scripts/print-model-shapes.py character")
        print("  python3 scripts/print-model-shapes.py game-session")
        print("  python3 scripts/print-model-shapes.py currency")
        print()
        print("Format: * = required, ? = nullable, = val = default")
        sys.exit(0 if sys.argv[1:] else 1)

    service = sys.argv[1].lower().strip()

    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    schemas_dir = repo_root / "schemas"

    if not schemas_dir.exists():
        print("ERROR: schemas/ directory not found", file=sys.stderr)
        sys.exit(1)

    # Schema files to look for, in display order
    schema_files = [
        (schemas_dir / f"{service}-api.yaml", "API"),
        (schemas_dir / f"{service}-events.yaml", "Events"),
        (schemas_dir / "Generated" / f"{service}-lifecycle-events.yaml", "Lifecycle Events"),
        (schemas_dir / f"{service}-client-events.yaml", "Client Events"),
    ]

    found_any = False
    total_models = 0
    total_enums = 0

    for yaml_file, category in schema_files:
        if not yaml_file.exists():
            continue

        lines = extract_models_from_file(yaml_file)
        if not lines:
            continue

        found_any = True

        # Count
        for line in lines:
            if line.startswith("enum "):
                total_enums += 1
            elif not line.startswith("  ") and line != "}" and "{" in line:
                total_models += 1

        # Print section
        print(f"[{category}]")
        print()
        for line in lines:
            print(line)
        print()

    if not found_any:
        # List available services to help the user
        available = set()
        for f in sorted(schemas_dir.glob("*-api.yaml")):
            svc = f.name.replace("-api.yaml", "")
            available.add(svc)

        print(f"ERROR: No schemas found for '{service}'", file=sys.stderr)
        print(f"Available services: {', '.join(sorted(available))}", file=sys.stderr)
        sys.exit(1)

    print(f"# {total_models} models, {total_enums} enums", file=sys.stderr)


if __name__ == "__main__":
    main()
