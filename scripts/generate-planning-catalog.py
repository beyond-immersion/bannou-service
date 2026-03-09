#!/usr/bin/env python3
# ⛔⛔⛔ AGENT MODIFICATION PROHIBITED ⛔⛔⛔
# This script is part of Bannou's code generation pipeline.
# DO NOT MODIFY without EXPLICIT user instructions to change code generation.
#
# Changes to generation scripts silently break builds across ALL 48 services.
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
Generate Planning Catalog from Planning Documents.

Scans docs/planning/*.md and extracts header metadata (Type, Status, Created,
Last Updated, North Stars, Related Plugins) and ## Summary sections. Entries
are grouped by Type. Documents missing standardized headers are included with
whatever metadata exists.

Output: docs/GENERATED-PLANNING-CATALOG.md

Usage:
    python3 scripts/generate-planning-catalog.py
"""

from pathlib import Path


"""Ordered groups for planning document types. Documents whose Type value
starts with a group prefix are placed in that group."""
TYPE_GROUPS = [
    ('Vision Documents', 'Vision'),
    ('Design', 'Design'),
    ('Research', 'Research'),
    ('Implementation Plans', 'Implementation'),
    ('Architectural Analysis', 'Architectural'),
]


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


def extract_summary_section(content: str) -> str:
    """Extract the ## Summary section from a document."""
    lines = content.split('\n')
    in_summary = False
    summary_lines = []

    for line in lines:
        if line.strip() == '## Summary':
            in_summary = True
            continue
        if in_summary:
            if line.startswith('## ') or line.strip() == '---':
                break
            summary_lines.append(line)

    while summary_lines and not summary_lines[0].strip():
        summary_lines.pop(0)
    while summary_lines and not summary_lines[-1].strip():
        summary_lines.pop()

    return '\n'.join(summary_lines)


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


def classify_type(type_value: str) -> str:
    """Classify a Type field value into a group name, or empty for ungrouped."""
    for group_name, prefix in TYPE_GROUPS:
        if type_value.startswith(prefix):
            return group_name
    return ''


def scan_planning_docs(planning_dir: Path) -> list:
    """Scan all planning documents and extract metadata and summaries."""
    docs = []

    if not planning_dir.exists():
        return docs

    for md_file in sorted(planning_dir.glob('*.md')):
        try:
            content = md_file.read_text()
        except Exception:
            continue

        title = extract_title(content)
        if not title:
            continue

        file_lines = content.split('\n')
        anchor = md_file.stem.lower()

        doc_type = extract_header_field(file_lines, 'Type')
        status = extract_header_field(file_lines, 'Status')
        created = extract_header_field(file_lines, 'Created')
        last_updated = extract_header_field(file_lines, 'Last Updated')
        north_stars = extract_header_field(file_lines, 'North Stars')
        related_plugins = extract_header_field(file_lines, 'Related Plugins')

        summary = extract_summary_section(content)
        if not summary:
            summary = extract_fallback_summary(content)

        group = classify_type(doc_type) if doc_type else ''

        docs.append({
            'filename': md_file.name,
            'title': title,
            'anchor': anchor,
            'type': doc_type,
            'status': status,
            'created': created,
            'last_updated': last_updated,
            'north_stars': north_stars,
            'related_plugins': related_plugins,
            'summary': summary,
            'group': group,
        })

    return docs


def generate_entry(doc: dict) -> list:
    """Generate markdown lines for a single planning document entry."""
    lines = []

    meta_parts = []
    if doc['type']:
        meta_parts.append(f"**Type**: {doc['type']}")
    if doc['status']:
        meta_parts.append(f"**Status**: {doc['status']}")
    if doc['last_updated']:
        meta_parts.append(f"**Last Updated**: {doc['last_updated']}")
    if doc['north_stars']:
        meta_parts.append(f"**North Stars**: {doc['north_stars']}")
    meta_parts.append(f"[Full Document](planning/{doc['filename']})")

    lines.append(' | '.join(meta_parts))
    lines.append("")

    if doc['summary']:
        lines.append(doc['summary'])
        lines.append("")

    return lines


def generate_markdown(docs: list) -> str:
    """Generate the catalog markdown with grouping by Type."""
    lines = [
        "# Generated Planning Catalog",
        "",
        "> **Source**: `docs/planning/*.md`",
        "> **Do not edit manually** - regenerate with `make generate-docs`",
        "",
        "Planning, design, research, and architectural analysis documents.",
        "",
    ]

    # Collect groups in order
    grouped = {}
    ungrouped = []

    for doc in docs:
        if doc['group']:
            grouped.setdefault(doc['group'], []).append(doc)
        else:
            ungrouped.append(doc)

    # Emit groups in defined order
    for group_name, _ in TYPE_GROUPS:
        group_docs = grouped.get(group_name, [])
        if not group_docs:
            continue

        lines.append(f"## {group_name}")
        lines.append("")

        for doc in group_docs:
            lines.append(f"### {doc['title']} {{#{doc['anchor']}}}")
            lines.append("")
            lines.extend(generate_entry(doc))

    # Emit ungrouped documents
    if ungrouped:
        lines.append("## Other")
        lines.append("")

        for doc in ungrouped:
            lines.append(f"### {doc['title']} {{#{doc['anchor']}}}")
            lines.append("")
            lines.extend(generate_entry(doc))

    lines.extend([
        "## Summary",
        "",
        f"- **Documents in catalog**: {len(docs)}",
        "",
        "---",
        "",
        "*This file is auto-generated. See [TENETS.md](reference/TENETS.md) for architectural context.*",
        "",
    ])

    return '\n'.join(lines)


def main():
    script_dir = Path(__file__).parent
    repo_root = script_dir.parent
    planning_dir = repo_root / 'docs' / 'planning'

    docs = scan_planning_docs(planning_dir)

    if not docs:
        print("Warning: No planning documents found")

    markdown = generate_markdown(docs)

    output_file = repo_root / 'docs' / 'GENERATED-PLANNING-CATALOG.md'
    with open(output_file, 'w') as f:
        f.write(markdown)

    print(f"Generated {output_file} with {len(docs)} documents")


if __name__ == '__main__':
    main()
