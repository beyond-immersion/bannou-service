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
Generate FAQ Catalog from FAQ Documents.

Scans docs/faqs/*.md and extracts header metadata (Last Updated, Related
Plugins) and the > **Short Answer**: block (which may span multiple
continuation lines). The Short Answer IS the summary. Sorted alphabetically
by filename.

Output: docs/GENERATED-FAQ-CATALOG.md

Usage:
    python3 scripts/generate-faq-catalog.py
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


def extract_short_answer(content: str) -> str:
    """Extract the > **Short Answer**: block including continuation lines.

    Continuation lines start with '> ' but do not start with '> **'.
    """
    lines = content.split('\n')
    result_lines = []
    in_short_answer = False

    for line in lines:
        if not in_short_answer:
            if line.startswith('> **Short Answer**:'):
                text = line.split(':', 1)[1].strip()
                result_lines.append(text)
                in_short_answer = True
            continue

        # Stop at non-blockquote or new field
        if line.startswith('> **') or not line.startswith('>'):
            break

        # Continuation line: strip '> ' or '>' prefix
        if line.startswith('> '):
            result_lines.append(line[2:])
        else:
            result_lines.append(line[1:])

    return '\n'.join(result_lines)


def extract_fallback_blockquote(content: str) -> str:
    """Extract first blockquote paragraph after title as fallback.

    Used when no > **Short Answer**: field exists. Looks for the first
    blockquote text that is not a metadata field.
    """
    lines = content.split('\n')
    title_found = False
    collecting = False
    blockquote_lines = []

    for line in lines:
        if not title_found:
            if line.startswith('# ') and not line.startswith('## '):
                title_found = True
            continue

        stripped = line.strip()

        if not collecting:
            # Skip blank lines, separators
            if stripped == '' or stripped == '---':
                continue
            # Skip metadata fields
            if stripped.startswith('> **'):
                continue
            # Start collecting plain blockquote text
            if stripped.startswith('> '):
                collecting = True
                blockquote_lines.append(stripped[2:])
            elif stripped.startswith('>'):
                collecting = True
                blockquote_lines.append(stripped[1:])
            else:
                break
        else:
            if stripped.startswith('> '):
                blockquote_lines.append(stripped[2:])
            elif stripped.startswith('>'):
                blockquote_lines.append(stripped[1:])
            else:
                break

    return '\n'.join(blockquote_lines)


def scan_faqs(faqs_dir: Path) -> list:
    """Scan all FAQ documents and extract metadata and short answers."""
    faqs = []

    if not faqs_dir.exists():
        return faqs

    for md_file in sorted(faqs_dir.glob('*.md')):
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
        related_plugins = extract_header_field(file_lines, 'Related Plugins')

        short_answer = extract_short_answer(content)
        if not short_answer:
            short_answer = extract_fallback_blockquote(content)

        faqs.append({
            'filename': md_file.name,
            'title': title,
            'anchor': anchor,
            'last_updated': last_updated,
            'related_plugins': related_plugins,
            'short_answer': short_answer,
        })

    return faqs


def generate_markdown(faqs: list) -> str:
    """Generate the catalog markdown."""
    lines = [
        "# Generated FAQ Catalog",
        "",
        "> **Source**: `docs/faqs/*.md`",
        "> **Do not edit manually** - regenerate with `make generate-docs`",
        "",
        "Architectural rationale FAQ documents explaining key design decisions in Bannou.",
        "",
    ]

    for faq in faqs:
        lines.append(f"## {faq['title']} {{#{faq['anchor']}}}")
        lines.append("")

        meta_parts = []
        if faq['last_updated']:
            meta_parts.append(f"**Last Updated**: {faq['last_updated']}")
        if faq['related_plugins']:
            meta_parts.append(f"**Related Plugins**: {faq['related_plugins']}")
        meta_parts.append(f"[Full FAQ](faqs/{faq['filename']})")

        lines.append(' | '.join(meta_parts))
        lines.append("")

        if faq['short_answer']:
            lines.append(faq['short_answer'])
            lines.append("")

    lines.extend([
        "## Summary",
        "",
        f"- **Documents in catalog**: {len(faqs)}",
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
    faqs_dir = repo_root / 'docs' / 'faqs'

    faqs = scan_faqs(faqs_dir)

    if not faqs:
        print("Warning: No FAQ documents found")

    markdown = generate_markdown(faqs)

    output_file = repo_root / 'docs' / 'GENERATED-FAQ-CATALOG.md'
    with open(output_file, 'w') as f:
        f.write(markdown)

    print(f"Generated {output_file} with {len(faqs)} FAQs")


if __name__ == '__main__':
    main()
