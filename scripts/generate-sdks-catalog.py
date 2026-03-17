#!/usr/bin/env python3
# ⛔⛔⛔ AGENT MODIFICATION PROHIBITED ⛔⛔⛔
# This script is part of Bannou's code generation pipeline.
# DO NOT MODIFY without EXPLICIT user instructions to change code generation.
#
# Changes to generation scripts silently break builds across ALL 76+ services.
# An agent once changed namespace strings across 4 scripts in a single commit,
# breaking every service. If you believe a change is needed:
#   1. STOP and explain what you think is wrong
#   2. Show the EXACT diff you propose
#   3. Wait for EXPLICIT approval before touching ANY generation script
#
# This applies to: namespace strings, output paths, exclusion logic,
# NSwag parameters, post-processing steps, and file naming conventions.
# ⛔⛔⛔ AGENT MODIFICATION PROHIBITED ⛔⛔⛔

"""
Generate SDK Catalog from SDK Deep Dive Documents.

Scans docs/sdks/*.md and extracts header metadata (SDK, Layer, Domain,
Status, Dependencies, Consumers, Short) and ## Overview sections. Groups
SDKs by Domain. Cross-references docs/sdks/maps/ to note which SDKs
have implementation maps.

Output: docs/generated/GENERATED-SDKS-CATALOG.md

Usage:
    python3 scripts/generate-sdks-catalog.py
"""

from pathlib import Path


def extract_title(content: str) -> str:
    """Extract the # Title from a document."""
    for line in content.split('\n'):
        if line.startswith('# ') and not line.startswith('## '):
            return line[2:].strip()
    return ''


def extract_header_field(lines: list, field: str) -> str:
    """Extract a > **Field**: value from the first 15 lines."""
    for line in lines[:15]:
        if line.startswith(f'> **{field}**:'):
            return line.split(':', 1)[1].strip()
    return ''


def extract_overview_section(content: str) -> str:
    """Extract the ## Overview section from a document."""
    lines = content.split('\n')
    in_overview = False
    overview_lines = []

    for line in lines:
        if line.strip() == '## Overview':
            in_overview = True
            continue
        if in_overview:
            if line.startswith('## ') or line.strip() == '---':
                break
            overview_lines.append(line)

    while overview_lines and not overview_lines[0].strip():
        overview_lines.pop(0)
    while overview_lines and not overview_lines[-1].strip():
        overview_lines.pop()

    return '\n'.join(overview_lines)


def extract_fallback_summary(content: str) -> str:
    """Extract first non-blank paragraph after title/header as fallback."""
    lines = content.split('\n')
    title_found = False
    collecting = False
    paragraph_lines = []

    for line in lines:
        if not title_found:
            if line.startswith('# ') and not line.startswith('## '):
                title_found = True
            continue

        stripped = line.strip()

        if not collecting:
            # Skip header blockquotes, blank lines, separators, headings
            if stripped == '' or stripped.startswith('>') or stripped == '---':
                continue
            if stripped.startswith('## ') or stripped.startswith('### '):
                continue
            collecting = True
            paragraph_lines.append(line)
        else:
            if stripped == '' or stripped.startswith('## ') or stripped == '---':
                break
            paragraph_lines.append(line)

    return '\n'.join(paragraph_lines)


def scan_sdk_docs(sdks_dir: Path, maps_dir: Path) -> list:
    """Scan all SDK deep dive documents and extract metadata and summaries."""
    sdks = []

    if not sdks_dir.exists():
        return sdks

    # Build set of SDK names that have implementation maps
    map_names = set()
    if maps_dir.exists():
        for md_file in maps_dir.glob('*.md'):
            map_names.add(md_file.stem)

    for md_file in sorted(sdks_dir.glob('*.md')):
        try:
            content = md_file.read_text()
        except Exception:
            continue

        title = extract_title(content)
        if not title:
            continue

        file_lines = content.split('\n')
        anchor = md_file.stem.lower()

        sdk = extract_header_field(file_lines, 'SDK')
        layer = extract_header_field(file_lines, 'Layer')
        domain = extract_header_field(file_lines, 'Domain')
        status = extract_header_field(file_lines, 'Status')
        dependencies = extract_header_field(file_lines, 'Dependencies')
        consumers = extract_header_field(file_lines, 'Consumers')
        short = extract_header_field(file_lines, 'Short')

        summary = extract_overview_section(content)
        if not summary:
            summary = extract_fallback_summary(content)

        has_map = md_file.stem in map_names

        sdks.append({
            'filename': md_file.name,
            'title': title,
            'anchor': anchor,
            'sdk': sdk,
            'layer': layer,
            'domain': domain,
            'status': status,
            'dependencies': dependencies,
            'consumers': consumers,
            'short': short,
            'summary': summary,
            'has_map': has_map,
        })

    return sdks


def generate_markdown(sdks: list) -> str:
    """Generate the catalog markdown, grouped by Domain."""
    lines = [
        "# Generated SDK Catalog",
        "",
        "> **Source**: `docs/sdks/*.md`",
        "> **Do not edit manually** - regenerate with `make generate-docs`",
        "",
        "Pure-computation SDK deep dives covering Bannou's creative and infrastructure libraries.",
        "",
    ]

    # Group by domain
    domains = {}
    for sdk in sdks:
        domain = sdk['domain'] or 'Other'
        if domain not in domains:
            domains[domain] = []
        domains[domain].append(sdk)

    # Layer ordering for sorting within a domain
    layer_order = {
        'Theory': 0,
        'Storyteller': 1,
        'Composer': 2,
        'Bridge': 3,
        'Infrastructure': 4,
    }

    for domain in sorted(domains.keys()):
        domain_sdks = sorted(domains[domain], key=lambda s: layer_order.get(s['layer'], 99))

        lines.append(f"## {domain}")
        lines.append("")

        for sdk in domain_sdks:
            lines.append(f"### {sdk['title']} {{#{sdk['anchor']}}}")
            lines.append("")

            # Build metadata line with only non-empty fields
            meta_parts = []
            if sdk['layer']:
                meta_parts.append(f"**Layer**: {sdk['layer']}")
            if sdk['status']:
                meta_parts.append(f"**Status**: {sdk['status']}")
            if sdk['dependencies']:
                meta_parts.append(f"**Dependencies**: {sdk['dependencies']}")
            if sdk['consumers']:
                meta_parts.append(f"**Consumers**: {sdk['consumers']}")
            if sdk['has_map']:
                meta_parts.append(f"[Implementation Map](../sdks/maps/{sdk['filename']})")
            meta_parts.append(f"[Full Deep Dive](../sdks/{sdk['filename']})")

            lines.append(' | '.join(meta_parts))
            lines.append("")

            if sdk['short']:
                lines.append(f"*{sdk['short']}*")
                lines.append("")

            if sdk['summary']:
                lines.append(sdk['summary'])
                lines.append("")

    # Count maps
    map_count = sum(1 for s in sdks if s['has_map'])

    lines.extend([
        "## Summary",
        "",
        f"- **SDKs in catalog**: {len(sdks)}",
        f"- **Domains**: {', '.join(sorted(domains.keys()))}",
        f"- **Implementation maps**: {map_count}",
        "",
        "---",
        "",
        "*This file is auto-generated. See [TENETS.md](../reference/TENETS.md) for architectural context.*",
        "",
    ])

    return '\n'.join(lines)


def main():
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    sdks_dir = repo_root / 'docs' / 'sdks'
    maps_dir = sdks_dir / 'maps'

    sdks = scan_sdk_docs(sdks_dir, maps_dir)

    if not sdks:
        print("Warning: No SDK documents found")

    markdown = generate_markdown(sdks)

    output_file = repo_root / 'docs' / 'generated' / 'GENERATED-SDKS-CATALOG.md'
    with open(output_file, 'w') as f:
        f.write(markdown)

    print(f"Generated {output_file} with {len(sdks)} SDKs")


if __name__ == '__main__':
    main()
