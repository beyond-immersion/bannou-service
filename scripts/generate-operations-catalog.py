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
Generate Operations Catalog from Operations Documents.

Scans docs/operations/*.md (excluding CLAUDE-SKILLS.md) and extracts header
metadata (Last Updated, Scope) and ## Summary sections. Documents missing
standardized headers are included with whatever metadata exists.

Output: docs/GENERATED-OPERATIONS-CATALOG.md

Usage:
    python3 scripts/generate-operations-catalog.py
"""

from pathlib import Path

"""Files to exclude from the catalog."""
EXCLUDED_FILES = {'CLAUDE-SKILLS.md'}


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


def scan_operations_docs(operations_dir: Path) -> list:
    """Scan all operations documents and extract metadata and summaries."""
    docs = []

    if not operations_dir.exists():
        return docs

    for md_file in sorted(operations_dir.glob('*.md')):
        if md_file.name in EXCLUDED_FILES:
            continue

        try:
            content = md_file.read_text()
        except Exception:
            continue

        title = extract_title(content)
        if not title:
            continue

        file_lines = content.split('\n')
        anchor = md_file.stem.lower()

        last_updated = extract_header_field(file_lines, 'Last Updated')
        scope = extract_header_field(file_lines, 'Scope')

        summary = extract_summary_section(content)
        if not summary:
            summary = extract_fallback_summary(content)

        docs.append({
            'filename': md_file.name,
            'title': title,
            'anchor': anchor,
            'last_updated': last_updated,
            'scope': scope,
            'summary': summary,
        })

    return docs


def generate_markdown(docs: list) -> str:
    """Generate the catalog markdown."""
    lines = [
        "# Generated Operations Catalog",
        "",
        "> **Source**: `docs/operations/*.md`",
        "> **Do not edit manually** - regenerate with `make generate-docs`",
        "",
        "Operations, deployment, testing, and CI/CD documentation.",
        "",
    ]

    for doc in docs:
        lines.append(f"## {doc['title']} {{#{doc['anchor']}}}")
        lines.append("")

        meta_parts = []
        if doc['last_updated']:
            meta_parts.append(f"**Last Updated**: {doc['last_updated']}")
        if doc['scope']:
            meta_parts.append(f"**Scope**: {doc['scope']}")
        meta_parts.append(f"[Full Document](operations/{doc['filename']})")

        lines.append(' | '.join(meta_parts))
        lines.append("")

        if doc['summary']:
            lines.append(doc['summary'])
            lines.append("")

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
    operations_dir = repo_root / 'docs' / 'operations'

    docs = scan_operations_docs(operations_dir)

    if not docs:
        print("Warning: No operations documents found")

    markdown = generate_markdown(docs)

    output_file = repo_root / 'docs' / 'GENERATED-OPERATIONS-CATALOG.md'
    with open(output_file, 'w') as f:
        f.write(markdown)

    print(f"Generated {output_file} with {len(docs)} documents")


if __name__ == '__main__':
    main()
