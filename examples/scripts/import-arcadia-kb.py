#!/usr/bin/env python3
"""
Arcadia Knowledge Base Import Script

This script imports the arcadia-kb repository into the Bannou Documentation Service.
It demonstrates the content transformation that will be automated by repository binding.

Usage:
    python3 scripts/import-arcadia-kb.py [options]

Options:
    --dry-run           Show what would be imported without actually importing
    --output FILE       Write import payload to JSON file instead of API call
    --api-url URL       Documentation API URL (default: http://localhost:5000)
    --namespace NAME    Target namespace (default: arcadia-kb)
    --analyze           Only analyze documents and show suggestions
    --verbose           Show detailed processing information

Requirements:
    pip3 install pyyaml requests

Document Modifications for Better Imports:
    Add YAML frontmatter to documents for explicit metadata:

    ---
    title: Guardian Spirit System
    category: game-systems
    tags: [core, guardian-spirits, player-mechanics]
    summary: Overview of the guardian spirit possession mechanic
    voiceSummary: Guardian spirits let players possess household members
    private: true  # Use this to exclude specific documents
    ---
    # Document content starts here...

Supported frontmatter fields:
    - title: Override filename-based title
    - category: Must be one of: getting-started, api-reference, architecture,
                deployment, troubleshooting, tutorials, game-systems, world-lore,
                npc-ai, other
    - tags: List of tags for the document
    - summary: Brief summary (for search results)
    - voiceSummary: Voice-friendly summary (max 200 chars)
    - slug: Override auto-generated slug
    - private: Set to true to exclude from import
    - relatedDocuments: List of slugs for related documents
"""

import argparse
import json
import os
import re
import sys
from pathlib import Path
from typing import Any, Optional
from dataclasses import dataclass, field, asdict
from datetime import datetime

try:
    import yaml
except ImportError:
    print("Error: PyYAML not installed. Run: pip3 install pyyaml")
    sys.exit(1)

try:
    import requests
except ImportError:
    requests = None  # Optional for dry-run/output modes


# =============================================================================
# Configuration
# =============================================================================

ARCADIA_KB_PATH = Path.home() / "repos" / "arcadia-kb"

# Valid DocumentCategory enum values
VALID_CATEGORIES = {
    "getting-started",
    "api-reference",
    "architecture",
    "deployment",
    "troubleshooting",
    "tutorials",
    "game-systems",
    "world-lore",
    "npc-ai",
    "other",
}

# Category mapping from folder names to DocumentCategory
CATEGORY_MAPPING = {
    "01 - Core Concepts": "game-systems",
    "02 - World Lore": "world-lore",
    "03 - Character Systems": "game-systems",
    "04 - Game Systems": "game-systems",
    "05 - NPC AI Design": "npc-ai",
    "06 - Technical Architecture": "architecture",
    "07 - Implementation Guides": "tutorials",
    "08 - Crafting Systems": "game-systems",
    "09 - Engine Research": "architecture",
    "Claude": "other",
}

# Patterns to exclude
EXCLUDE_PATTERNS = [
    ".git",
    ".obsidian",
    ".claude",
    "Templates",
    "node_modules",
    "__pycache__",
]

# File extensions to exclude
EXCLUDE_EXTENSIONS = {
    ".zip", ".json", ".png", ".jpg", ".jpeg", ".gif", ".pdf",
    ".exe", ".dll", ".db", ".sqlite",
}


# =============================================================================
# Data Classes
# =============================================================================

@dataclass
class DocumentImport:
    """Document ready for import via API."""
    slug: str
    title: str
    category: str
    content: str
    summary: Optional[str] = None
    voiceSummary: Optional[str] = None
    tags: list[str] = field(default_factory=list)
    metadata: dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> dict:
        """Convert to API request format."""
        result = {
            "slug": self.slug,
            "title": self.title,
            "category": self.category,
            "content": self.content,
            "tags": self.tags,
        }
        if self.summary:
            result["summary"] = self.summary
        if self.voiceSummary:
            result["voiceSummary"] = self.voiceSummary
        if self.metadata:
            result["metadata"] = self.metadata
        return result


@dataclass
class ProcessingResult:
    """Result of processing a single file."""
    file_path: Path
    document: Optional[DocumentImport]
    skipped: bool = False
    skip_reason: Optional[str] = None
    warnings: list[str] = field(default_factory=list)
    suggestions: list[str] = field(default_factory=list)


# =============================================================================
# Content Processing
# =============================================================================

def parse_frontmatter(content: str) -> tuple[dict, str]:
    """
    Parse YAML frontmatter from markdown content.

    Returns:
        Tuple of (frontmatter_dict, content_without_frontmatter)
    """
    if not content.startswith("---"):
        return {}, content

    # Find the closing ---
    lines = content.split("\n")
    end_index = None
    for i, line in enumerate(lines[1:], start=1):
        if line.strip() == "---":
            end_index = i
            break

    if end_index is None:
        return {}, content

    frontmatter_text = "\n".join(lines[1:end_index])
    remaining_content = "\n".join(lines[end_index + 1:]).lstrip("\n")

    try:
        frontmatter = yaml.safe_load(frontmatter_text) or {}
    except yaml.YAMLError:
        return {}, content

    return frontmatter, remaining_content


def generate_slug(file_path: Path, base_path: Path) -> str:
    """
    Generate URL-safe slug from file path.

    Example:
        "01 - Core Concepts/Guardian Spirit System.md"
        -> "01-core-concepts-guardian-spirit-system"
    """
    # Get relative path without extension
    relative = file_path.relative_to(base_path)
    path_str = str(relative.with_suffix(""))

    # Convert to lowercase
    slug = path_str.lower()

    # Replace path separators and spaces with hyphens
    slug = re.sub(r"[/\\]", "-", slug)
    slug = re.sub(r"\s+", "-", slug)

    # Remove or replace special characters
    slug = re.sub(r"[^a-z0-9-]", "", slug)

    # Collapse multiple hyphens
    slug = re.sub(r"-+", "-", slug)

    # Remove leading/trailing hyphens
    slug = slug.strip("-")

    return slug


def extract_title(file_path: Path, content: str) -> str:
    """
    Extract title from content or filename.

    Priority:
        1. First H1 heading in content
        2. Filename without extension
    """
    # Try to find first H1 heading
    match = re.search(r"^#\s+(.+)$", content, re.MULTILINE)
    if match:
        return match.group(1).strip()

    # Fall back to filename
    return file_path.stem.replace("-", " ").replace("_", " ")


def map_category(file_path: Path, base_path: Path) -> str:
    """
    Map file path to DocumentCategory.

    Uses folder prefix matching against CATEGORY_MAPPING.
    """
    relative = file_path.relative_to(base_path)
    parts = relative.parts

    if len(parts) > 1:
        folder = parts[0]
        if folder in CATEGORY_MAPPING:
            return CATEGORY_MAPPING[folder]

    return "other"


def generate_tags(file_path: Path, base_path: Path, prefix: str = "arcadia") -> list[str]:
    """
    Generate tags from folder structure.

    Example:
        "08 - Crafting Systems/01 - Fundamental Processes/Charcoal.md"
        -> ["arcadia:crafting-systems", "arcadia:fundamental-processes"]
    """
    relative = file_path.relative_to(base_path)
    tags = []

    for part in relative.parts[:-1]:  # Exclude filename
        # Remove numbered prefix (e.g., "01 - ")
        clean = re.sub(r"^\d+\s*-\s*", "", part)
        # Convert to tag format
        tag = clean.lower().replace(" ", "-")
        tag = re.sub(r"[^a-z0-9-]", "", tag)
        if tag and prefix:
            tags.append(f"{prefix}:{tag}")
        elif tag:
            tags.append(tag)

    return tags


def transform_references(content: str, namespace: str) -> str:
    """
    Transform @ references to documentation links.

    Example:
        @~/repos/arcadia-kb/01 - Core Concepts/Guardian Spirit System.md
        -> [Guardian Spirit System](/documentation/view/01-core-concepts-guardian-spirit-system?ns=arcadia-kb)
    """
    # Pattern for @ references
    pattern = r"@~/repos/arcadia-kb/([^)\s\]]+\.md)"

    def replace_ref(match):
        path_str = match.group(1)
        # Generate slug from path
        slug = path_str.lower()
        slug = re.sub(r"\.md$", "", slug)
        slug = re.sub(r"[/\\]", "-", slug)
        slug = re.sub(r"\s+", "-", slug)
        slug = re.sub(r"[^a-z0-9-]", "", slug)
        slug = re.sub(r"-+", "-", slug)
        slug = slug.strip("-")

        # Extract title from path
        title = Path(path_str).stem.replace("-", " ").replace("_", " ")

        return f"[{title}](/documentation/view/{slug}?ns={namespace})"

    return re.sub(pattern, replace_ref, content)


def should_exclude(file_path: Path) -> tuple[bool, Optional[str]]:
    """
    Check if file should be excluded from import.

    Returns:
        Tuple of (should_exclude, reason)
    """
    # Check extension
    if file_path.suffix.lower() in EXCLUDE_EXTENSIONS:
        return True, f"Excluded extension: {file_path.suffix}"

    # Check path components
    for part in file_path.parts:
        if part in EXCLUDE_PATTERNS:
            return True, f"Excluded pattern: {part}"
        if part.startswith("."):
            return True, f"Hidden directory: {part}"

    # Only process markdown files
    if file_path.suffix.lower() != ".md":
        return True, f"Not a markdown file: {file_path.suffix}"

    return False, None


def process_file(
    file_path: Path,
    base_path: Path,
    namespace: str,
    verbose: bool = False
) -> ProcessingResult:
    """
    Process a single markdown file for import.

    Returns:
        ProcessingResult with document or skip information
    """
    result = ProcessingResult(file_path=file_path, document=None)

    # Check exclusions
    exclude, reason = should_exclude(file_path)
    if exclude:
        result.skipped = True
        result.skip_reason = reason
        return result

    # Read file content
    try:
        content = file_path.read_text(encoding="utf-8")
    except Exception as e:
        result.skipped = True
        result.skip_reason = f"Read error: {e}"
        return result

    # Parse frontmatter
    frontmatter, body = parse_frontmatter(content)

    # Check for private flag
    if frontmatter.get("private", False):
        result.skipped = True
        result.skip_reason = "Marked as private in frontmatter"
        return result

    # Generate slug (frontmatter override or auto-generated)
    slug = frontmatter.get("slug") or generate_slug(file_path, base_path)

    # Extract title (frontmatter override or from content)
    title = frontmatter.get("title") or extract_title(file_path, body)

    # Map category (frontmatter override or from folder)
    category = frontmatter.get("category")
    if category and category not in VALID_CATEGORIES:
        result.warnings.append(
            f"Invalid category '{category}' in frontmatter, using folder mapping"
        )
        result.suggestions.append(
            f"Valid categories: {', '.join(sorted(VALID_CATEGORIES))}"
        )
        category = None
    if not category:
        category = map_category(file_path, base_path)

    # Get tags (combine frontmatter and auto-generated)
    fm_tags = frontmatter.get("tags", [])
    if isinstance(fm_tags, str):
        fm_tags = [fm_tags]
    auto_tags = generate_tags(file_path, base_path)
    tags = list(set(fm_tags + auto_tags))

    # Transform content
    transformed_content = transform_references(body, namespace)

    # Get optional fields
    summary = frontmatter.get("summary")
    voice_summary = frontmatter.get("voiceSummary")

    # Check voice summary length
    if voice_summary and len(voice_summary) > 200:
        result.warnings.append(
            f"voiceSummary too long ({len(voice_summary)} chars), will be truncated to 200"
        )
        voice_summary = voice_summary[:197] + "..."

    # Build metadata
    metadata = {
        "sourceFilePath": str(file_path.relative_to(base_path)),
        "importedAt": datetime.utcnow().isoformat() + "Z",
    }

    # Preserve unknown frontmatter fields in metadata
    known_fields = {
        "title", "category", "tags", "summary", "voiceSummary",
        "slug", "private", "relatedDocuments"
    }
    for key, value in frontmatter.items():
        if key not in known_fields:
            metadata[f"frontmatter_{key}"] = value

    # Check for suggestions
    if not frontmatter:
        result.suggestions.append(
            "Consider adding YAML frontmatter for explicit metadata control"
        )
    if not summary:
        result.suggestions.append(
            "Consider adding 'summary' in frontmatter for better search results"
        )
    if not voice_summary:
        result.suggestions.append(
            "Consider adding 'voiceSummary' for voice AI integration"
        )

    # Create document
    result.document = DocumentImport(
        slug=slug,
        title=title,
        category=category,
        content=transformed_content,
        summary=summary,
        voiceSummary=voice_summary,
        tags=tags,
        metadata=metadata,
    )

    return result


# =============================================================================
# Main Processing
# =============================================================================

def scan_repository(base_path: Path) -> list[Path]:
    """
    Scan repository for markdown files.

    Returns:
        List of markdown file paths
    """
    files = []
    for file_path in base_path.rglob("*.md"):
        # Skip excluded directories
        skip = False
        for part in file_path.relative_to(base_path).parts:
            if part in EXCLUDE_PATTERNS or part.startswith("."):
                skip = True
                break
        if not skip:
            files.append(file_path)

    return sorted(files)


def process_repository(
    base_path: Path,
    namespace: str,
    verbose: bool = False
) -> tuple[list[ProcessingResult], dict]:
    """
    Process entire repository.

    Returns:
        Tuple of (results, statistics)
    """
    files = scan_repository(base_path)
    results = []
    stats = {
        "total_files": len(files),
        "processed": 0,
        "skipped": 0,
        "with_frontmatter": 0,
        "with_warnings": 0,
        "with_suggestions": 0,
        "by_category": {},
    }

    for file_path in files:
        result = process_file(file_path, base_path, namespace, verbose)
        results.append(result)

        if result.skipped:
            stats["skipped"] += 1
        else:
            stats["processed"] += 1
            category = result.document.category
            stats["by_category"][category] = stats["by_category"].get(category, 0) + 1

            # Check for frontmatter
            if result.document.metadata.get("sourceFilePath"):
                content = file_path.read_text(encoding="utf-8")
                if content.startswith("---"):
                    stats["with_frontmatter"] += 1

        if result.warnings:
            stats["with_warnings"] += 1
        if result.suggestions:
            stats["with_suggestions"] += 1

    return results, stats


def build_import_request(
    results: list[ProcessingResult],
    namespace: str
) -> dict:
    """
    Build API import request from processing results.
    """
    documents = [
        r.document.to_dict()
        for r in results
        if r.document is not None
    ]

    return {
        "namespace": namespace,
        "conflictResolution": "update",
        "documents": documents,
    }


def send_import_request(api_url: str, request: dict) -> dict:
    """
    Send import request to Documentation API.
    """
    if requests is None:
        raise RuntimeError(
            "requests library not installed. Run: pip3 install requests"
        )

    url = f"{api_url}/documentation/import"
    headers = {"Content-Type": "application/json"}

    response = requests.post(url, json=request, headers=headers, timeout=300)
    response.raise_for_status()

    return response.json()


# =============================================================================
# Output Formatting
# =============================================================================

def print_stats(stats: dict) -> None:
    """Print processing statistics."""
    print("\n" + "=" * 60)
    print("PROCESSING STATISTICS")
    print("=" * 60)
    print(f"Total files scanned:     {stats['total_files']}")
    print(f"Documents processed:     {stats['processed']}")
    print(f"Documents skipped:       {stats['skipped']}")
    print(f"With YAML frontmatter:   {stats['with_frontmatter']}")
    print(f"With warnings:           {stats['with_warnings']}")
    print(f"With suggestions:        {stats['with_suggestions']}")

    print("\nDocuments by category:")
    for category, count in sorted(stats["by_category"].items()):
        print(f"  {category}: {count}")


def print_analysis(results: list[ProcessingResult], verbose: bool = False) -> None:
    """Print analysis of documents with suggestions."""
    print("\n" + "=" * 60)
    print("DOCUMENT ANALYSIS")
    print("=" * 60)

    docs_with_suggestions = [r for r in results if r.suggestions and not r.skipped]
    docs_with_warnings = [r for r in results if r.warnings]

    if docs_with_warnings:
        print(f"\n--- Documents with Warnings ({len(docs_with_warnings)}) ---")
        for result in docs_with_warnings[:10]:  # Limit output
            rel_path = result.file_path.relative_to(ARCADIA_KB_PATH)
            print(f"\n  {rel_path}")
            for warning in result.warnings:
                print(f"    WARNING: {warning}")

    if docs_with_suggestions:
        print(f"\n--- Documents with Suggestions ({len(docs_with_suggestions)}) ---")
        if not verbose:
            print("  (showing first 10, use --verbose for all)")

        shown = 0
        for result in docs_with_suggestions:
            if not verbose and shown >= 10:
                break
            rel_path = result.file_path.relative_to(ARCADIA_KB_PATH)
            print(f"\n  {rel_path}")
            for suggestion in result.suggestions:
                print(f"    SUGGESTION: {suggestion}")
            shown += 1

    # Print example frontmatter
    print("\n" + "=" * 60)
    print("EXAMPLE FRONTMATTER TO ADD TO DOCUMENTS")
    print("=" * 60)
    print("""
Add this to the top of your markdown files for better control:

---
title: Your Document Title
category: game-systems
tags:
  - core-mechanics
  - player-systems
summary: A brief summary for search results (1-2 sentences)
voiceSummary: Very brief for voice AI (under 200 chars)
---

# Your Document Title

Content starts here...

Available categories:
  - getting-started
  - api-reference
  - architecture
  - deployment
  - troubleshooting
  - tutorials
  - game-systems
  - world-lore
  - npc-ai
  - other

To exclude a document from import, add:
  private: true
""")


def print_skipped(results: list[ProcessingResult]) -> None:
    """Print skipped files."""
    skipped = [r for r in results if r.skipped]
    if not skipped:
        return

    print("\n" + "=" * 60)
    print(f"SKIPPED FILES ({len(skipped)})")
    print("=" * 60)

    by_reason = {}
    for result in skipped:
        reason = result.skip_reason or "Unknown"
        by_reason.setdefault(reason, []).append(result.file_path)

    for reason, files in sorted(by_reason.items()):
        print(f"\n{reason} ({len(files)} files)")
        for f in files[:5]:
            rel = f.relative_to(ARCADIA_KB_PATH)
            print(f"  - {rel}")
        if len(files) > 5:
            print(f"  ... and {len(files) - 5} more")


# =============================================================================
# CLI
# =============================================================================

def main():
    parser = argparse.ArgumentParser(
        description="Import arcadia-kb into Bannou Documentation Service",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Show what would be imported without actually importing",
    )
    parser.add_argument(
        "--output",
        type=str,
        help="Write import payload to JSON file instead of API call",
    )
    parser.add_argument(
        "--api-url",
        type=str,
        default="http://localhost:5000",
        help="Documentation API URL (default: http://localhost:5000)",
    )
    parser.add_argument(
        "--namespace",
        type=str,
        default="arcadia-kb",
        help="Target namespace (default: arcadia-kb)",
    )
    parser.add_argument(
        "--analyze",
        action="store_true",
        help="Only analyze documents and show suggestions",
    )
    parser.add_argument(
        "--verbose",
        action="store_true",
        help="Show detailed processing information",
    )
    parser.add_argument(
        "--repo-path",
        type=str,
        default=str(ARCADIA_KB_PATH),
        help=f"Path to arcadia-kb repository (default: {ARCADIA_KB_PATH})",
    )

    args = parser.parse_args()

    # Validate repository path
    repo_path = Path(args.repo_path)
    if not repo_path.exists():
        print(f"Error: Repository path does not exist: {repo_path}")
        sys.exit(1)
    if not repo_path.is_dir():
        print(f"Error: Repository path is not a directory: {repo_path}")
        sys.exit(1)

    print(f"Processing repository: {repo_path}")
    print(f"Target namespace: {args.namespace}")

    # Process repository
    results, stats = process_repository(repo_path, args.namespace, args.verbose)

    # Print statistics
    print_stats(stats)

    # Print skipped files
    if args.verbose:
        print_skipped(results)

    # Analyze mode - just show suggestions
    if args.analyze:
        print_analysis(results, args.verbose)
        return

    # Build import request
    request = build_import_request(results, args.namespace)

    # Output mode - write to file
    if args.output:
        output_path = Path(args.output)
        with open(output_path, "w", encoding="utf-8") as f:
            json.dump(request, f, indent=2, ensure_ascii=False)
        print(f"\nImport payload written to: {output_path}")
        print(f"Total documents: {len(request['documents'])}")
        return

    # Dry run mode - just show what would happen
    if args.dry_run:
        print("\n" + "=" * 60)
        print("DRY RUN - Documents that would be imported:")
        print("=" * 60)
        for doc in request["documents"][:20]:
            print(f"  {doc['category']:20} {doc['slug']}")
        if len(request["documents"]) > 20:
            print(f"  ... and {len(request['documents']) - 20} more")
        print(f"\nTotal documents: {len(request['documents'])}")

        # Show suggestions
        print_analysis(results, args.verbose)
        return

    # Actually import
    if requests is None:
        print("\nError: requests library not installed for API calls.")
        print("Run: pip3 install requests")
        print("Or use --output to write JSON file for manual import.")
        sys.exit(1)

    print(f"\nSending import request to {args.api_url}...")
    try:
        response = send_import_request(args.api_url, request)
        print("\n" + "=" * 60)
        print("IMPORT RESULT")
        print("=" * 60)
        print(f"Namespace: {response.get('namespace')}")
        print(f"Created:   {response.get('created', 0)}")
        print(f"Updated:   {response.get('updated', 0)}")
        print(f"Skipped:   {response.get('skipped', 0)}")

        failed = response.get("failed", [])
        if failed:
            print(f"\nFailed documents ({len(failed)}):")
            for item in failed[:10]:
                print(f"  {item.get('slug')}: {item.get('error')}")

    except requests.RequestException as e:
        print(f"\nError: API request failed: {e}")
        sys.exit(1)


if __name__ == "__main__":
    main()
